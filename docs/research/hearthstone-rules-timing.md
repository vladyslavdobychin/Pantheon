# Hearthstone's Rules Timing Model

> **What decision this feeds.** The next ticket, *"Action resolution and trigger ordering"*, has to decide how Pantheon's Core resolves a move. It must settle the queue and ordering model for triggers, when deaths are processed, how simultaneous effects are ordered, and how each resolution step maps to the fat `GameEvent`s that `ApplyMove` returns (AGENTS.md decisions 3 and 4). Pantheon's ruleset is explicitly Hearthstone-derived ([`docs/design.md`](../design.md): Battle Cry, Deathrattle, start/end-of-turn triggers, mutual combat damage, fatigue, overdraw burn). This document records how Hearthstone actually times those things, so the engine copies a model that is known to work instead of inventing one.

**Audience.** A backend developer who is new to game rules engines. Each section first explains the mechanic in plain terms, then says what it implies for an engine (marked **Engine implication**).

**About the sources.** Blizzard has never published a rulebook. The team relies on internal unit tests and private documentation, so the community reverse-engineered the rules by testing ([Advanced rulebook § Why isn't there an official rulebook?](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Why_isn't_there_an_official_rulebook?)). The main primary source is the Hearthstone Wiki's [Advanced rulebook](https://hearthstone.wiki.gg/wiki/Advanced_rulebook). It is backed here by the source code of two open-source simulators:

- **SabberStone** (C#). The canonical repo `github.com/HearthSim/SabberStone` returned **HTTP 404** when checked on 2026-11-24, and the HearthSim organisation no longer lists it. The same `SabberStoneCore` code is readable in the AI-competition fork [`ADockhorn/HearthstoneAICompetition`](https://github.com/ADockhorn/HearthstoneAICompetition), which [`state-architecture.md`](state-architecture.md) already cites. All SabberStone links below point to that fork.
- **Fireplace** (Python): [`jleclanche/fireplace`](https://github.com/jleclanche/fireplace).

The simulators *approximate* the rulebook, and where they differ this document says so. All example cards are invented.

---

## 0. Vocabulary: four nested layers

Hearthstone's timing makes sense once you see that it has four nested layers. Everything later in this document builds on them ([Advanced rulebook § Sequences, Phases, Queues, Resolution](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Sequences,_Phases,_Queues,_Resolution); [§ Glossary](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Glossary)).

| Layer | Plain meaning | Example |
|---|---|---|
| **Sequence** | Everything that happens because of one player action. It is an ordered list of Phases and Steps. | "Play a minion", "attack", "end turn". |
| **Phase** | One block of the sequence. Starting a Phase builds a Queue of every trigger that responds to it. Phases can nest inside other Phases. | "On Play Phase", "Battlecry Phase", "Combat Phase". |
| **Event** | One atomic state change that triggers can respond to. | Damage, healing, summon, draw, death. |
| **Queue** | The ordered list of triggers that respond to one Phase or Event. It is built once and then **locked**. | Three "whenever a minion takes damage" triggers, in order of play. |

There are also **Steps**. A Step is housekeeping that the game runs between Phases, and no trigger responds to it. The important Steps are the **Aura Update** and the **Death Creation Step**.

**Order of play** is the tie-breaker for nearly everything. Each entity gets a timestamp when it enters play, and older entities go first. Minions, heroes, weapons, attached enchantments and added Deathrattles all share one list ([§ Sequences, Phases, Queues, Resolution](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Sequences,_Phases,_Queues,_Resolution)). An enchantment has its own timestamp, but an enchantment created by an aura uses the timestamp of the aura's owner ([§ Auras](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Auras)).

**Engine implication.** Model these four layers directly. A Sequence is one `ApplyMove` call. A Phase is a scope that owns a trigger queue. An Event is one `GameEvent` record. A Queue is a list snapshot that is sorted by an integer `PlayOrder` stamp. The stamp comes from a counter on `GameState` that only ever goes up. It is deterministic, cheap to clone, and needs no hashing.

---

## 1. Turn structure

Ending a turn is itself a Sequence. In order ([§ Start and end of turn](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Start_and_end_of_turn)):

1. **End of Turn Phase.** All end-of-turn triggers are queued in order of play and resolved.
2. **Win/loss/draw check.** This is also where the game is declared a draw once turn 89 has ended.
3. **Turn switch.** This is a bookkeeping Step, not a Phase, so nothing triggers on it. Expired "this turn" enchantments wear off and per-turn counters reset. The incoming player's mana refills, their Hero Power becomes usable again, and their characters lose exhaustion. The [Mana](https://hearthstone.wiki.gg/wiki/Mana) page adds that the incoming player gains one new mana crystal at this point (capped at 10). Last turn's pending Overload becomes locked crystals at the same time.
4. **Start of Turn Phase.** All start-of-turn triggers are queued and resolved.
5. **Win/loss/draw check.**
6. **Draw a Card Phase.** The incoming player draws one card, and its consequences resolve, including fatigue.
7. **Win/loss/draw check.**

Note that the turn's draw comes **after** start-of-turn triggers. A "at the start of your turn, draw a card" effect therefore resolves before the normal draw.

**Simulators agree.** SabberStone's turn loop in [`Game.cs`](https://github.com/ADockhorn/HearthstoneAICompetition/blob/master/SabberStoneCore/src/Model/Game.cs) runs `MainReady` (unexhaust, reset counters, and mana via `MainRessources`), then `MainStartTriggers` (start-of-turn triggers, then death processing), then `MainStart` (the draw, then death processing), then the action phase. After the action phase it runs `MainEnd` (end-of-turn triggers, then death processing), `MainCleanUp` (unfreeze, one-turn effects) and `MainNext` (switch player). Each trigger step is followed by its own `DeathProcessingAndAuraUpdate()`. Fireplace's `_begin_turn` in [`game.py`](https://github.com/jleclanche/fireplace/blob/master/fireplace/game.py) also adds the mana crystal and applies overload before calling `player.draw()`.

**Engine implication.** In Pantheon, "end turn" is a `Move`. Its Sequence covers both players' halves: the outgoing End of Turn Phase, the switch, the incoming Start of Turn Phase and the draw. All of it lives inside Core's `ApplyMove`. None of it belongs in the Runner (AGENTS.md decision 6). The Runner only notices that `CurrentPlayer` changed and asks the other agent next. Also plan a hard turn limit like Hearthstone's turn 89. Fatigue makes games end in normal play, but AI self-play and fuzz tests benefit from a guaranteed cap.

---

## 2. Play and attack sequences

### 2.1 Playing a minion

([§ Playing/summoning a minion](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Playing/summoning_a_minion))

1. **Setup (no triggers yet).** The card leaves the hand, its cost is paid, and it enters the board. Its chosen target is stored and **not re-checked** later.
2. **On Play Phase.** "Whenever you play a card/minion" triggers resolve.
3. **Summon Resolution Step.** "Whenever you summon a minion" triggers resolve for the new minion. This Step runs at the end of the outermost Phase, after the first Aura Update and before the Death Creation Step ([§ Summon Resolution Step](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Summon_Resolution_Step)).
4. **Battlecry Phase.** Target-redirect effects resolve first. Then the minion's Battle Cry resolves completely, as one Phase.
5. **After Play / After Summon Phase.** "After you play" triggers resolve, then "after you summon" triggers.
6. **Win/loss/draw check.**

The Death Creation Step runs between these Phases, never inside one. A minion that is summoned *without* being played (for example by a Deathrattle) skips steps 1–4. It just enters play and gets its After Summon handling inside whatever Phase is already running.

**Edge case: the Battle Cry survives its own minion's death.** In general a later Phase of a Sequence is skipped if its subject has left play (Rule 6 in [§ Sequences, Phases, Queues, Resolution](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Sequences,_Phases,_Queues,_Resolution)). The Battle Cry is the exception. If the played minion dies before its Battlecry Phase, the Battle Cry still resolves, using the minion's default stats and its last board position ([§ Minions removed from play during summoning](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Minions_removed_from_play_during_summoning)).

**SabberStone agrees** in [`PlayCard.cs`](https://github.com/ADockhorn/HearthstoneAICompetition/blob/master/SabberStoneCore/src/Actions/PlayCard.cs). Mana is paid in `PayPhase`, and then the card is removed from hand and added to `BoardZone`. Next come `OnPlayTrigger` (ending in death processing), `OnSummonTrigger`, the Battle Cry via `ActivateTask(PowerActivation.POWER, …)` (ending in death processing) and `OnAfterPlayMinionTrigger`. Last come the after-summon triggers (ending in death processing).

### 2.2 Playing a spell

([§ Playing a spell](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Playing_a_spell))

Setup (pay cost, store target) → **On Play Phase** ("whenever you cast a spell") → **Spell Text Phase** (the whole spell text runs as *one* Phase) → **After Spell Phase** ("after you cast a spell") → win/loss/draw check. SabberStone's `PlaySpell` follows the same order. It calls death processing after the on-play triggers, after the spell text and again at the end ([`PlayCard.cs`](https://github.com/ADockhorn/HearthstoneAICompetition/blob/master/SabberStoneCore/src/Actions/PlayCard.cs)).

Because the spell text is one Phase, a spell that deals 3 damage to every minion and then gives all friendly minions +1 Health can save a friendly minion it just dropped to 0. Nothing dies until the Phase ends.

### 2.3 An attack

([§ Combat](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Combat))

1. **Combat Preparation Phase.** The *Proposed Attack Event* resolves first. This is where "when a minion attacks" Secrets and redirect effects fire, and it is the only point where the defender can change. If the defender changes, a new Proposed Attack Event is queued. Then the *Attack Event* resolves, but only if the defender did not change. The attacker loses Stealth.
2. **Win/loss/draw check.** This is the one exception to "check only at the end of the Sequence".
3. **Combat Phase.** The attacker and the defender deal their damage **simultaneously**, in one Phase. The *After Attack Event* resolves in the same Phase, even if 0 damage was dealt.
4. **Win/loss/draw check.**

Deaths are processed between Preparation and Combat. If either combatant dies or leaves play during Preparation, the attack is **cancelled**, and the attacker has *not* spent its attack.

**Simulators approximate this.** SabberStone's `AttackBlock` ([`Attack.cs`](https://github.com/ADockhorn/HearthstoneAICompetition/blob/master/SabberStoneCore/src/Actions/Attack.cs)) runs `OnAttackTrigger`, then aborts if either side has `ToBeDestroyed` set or is no longer in `Zone.PLAY`. It runs no full death step at that point. It then re-reads the defender from `CurrentEventData.EventTarget`, so redirects are honoured. Next it calls `target.TakeDamage(source, …)` and then `source.TakeDamage(target, …)` with **no death check between them**. It finishes with `OnAfterAttackTrigger` and a single `DeathProcessingAndAuraUpdate()`. Fireplace's `Attack` ([`actions.py`](https://github.com/jleclanche/fireplace/blob/master/fireplace/actions.py)) has the same shape. It broadcasts the attack, re-reads `proposed_defender`, bails out if `should_exit_combat`, and queues two `Hit`s. It snapshots the defender's attack before either hit lands.

### 2.4 Hero Power (for later)

The Hero Power Phase runs first, and the power is then exhausted. Then the Inspire Phase runs: "after you use your Hero Power" triggers. The win/loss/draw check comes last ([§ Using your Hero Power](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Using_your_Hero_Power)). Pantheon has deferred Hero Powers, but this is the same pattern as the other actions: a main Phase, then an "after" Phase.

**Engine implication.** Every player action has the same shape: **setup → "whenever" Phase → main-effect Phase → "after" Phase → win check**, with death processing between Phases. Build one generic "run a Phase, then clean up" routine and express each action as a short list of Phases. Do not write a separate resolution loop for each action type.

---

## 3. Death processing

This is the most important mechanic to get right ([§ Sequences, Phases, Queues, Resolution](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Sequences,_Phases,_Queues,_Resolution), Rules 1c, 3, 4a, 7, 8; [§ Death Phases and consequences of Death](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Death_Phases_and_consequences_of_Death)).

- **Nothing dies mid-Phase.** A minion at 0 or less Health is **mortally wounded**, not dead. It stays on the board and keeps its triggers. It can still be healed or buffed back above 0 within the same Phase ([Health](https://hearthstone.wiki.gg/wiki/Health)). A "destroy" effect does not remove its target either. It only marks the target **pending destroy**.
- **Only the end of the *outermost* Phase triggers cleanup.** Phases nest, for example a damage trigger that fires inside a spell's Phase. Ending an inner Phase does nothing. When the outermost Phase ends, the game runs these Steps in order:
  1. **Aura Update (Health/Attack).**
  2. **Death Creation Step.** Every mortally wounded or pending-destroy entity is found and removed from play **at the same time**. One death event is created per entity, in order of play, and each records its board position.
  3. **Aura Update (Other).**
- **Then a Death Phase runs.** The deaths resolve one at a time in order of play. For each death, every trigger that responds to it is queued in order of play and fully resolved: its Deathrattle, "whenever a friendly minion dies" effects, on-death Secrets. Only then does the next death start. Deathrattles get **no priority** over other death triggers ([Deathrattle § Sequence of events](https://hearthstone.wiki.gg/wiki/Deathrattle)).
- **Repeat until stable.** Deathrattles can wound more minions. Those minions do *not* die inside the current Death Phase. They are collected at its end, and a new Death Phase runs. This repeats until a Death Creation Step finds nobody (Rule 8).
- **Exception: Forced Death Phase.** A few effects force deaths partway through a Phase, so that "destroy X, then summon a copy" chains work ([§ Forced Death Phase](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Forced_Death_Phase)). Pantheon can ignore this until it designs such a card.
- **The game also keeps a Death Event Cache** of every death: entity, controller at death, and turn. Resurrect effects and "if X died this game" checks read the cache, not the graveyard ([§ Death Event Cache](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Death_Event_Cache)).

**Simulators agree closely.** SabberStone's `DeathProcessingAndAuraUpdate()` ([`Game.cs`](https://github.com/ADockhorn/HearthstoneAICompetition/blob/master/SabberStoneCore/src/Model/Game.cs)) runs `AuraUpdate()`, then the summon triggers for `SummonedMinions`, then the loop `do { GraveYard(); ProcessTasks(); } while (DeadMinions.Count != 0);`, then `AuraUpdate()` again. `GraveYard()` sorts the dead with `DeadMinions.InsertionSort(GetOrderOfPlay)`. For each one it fires the death trigger, records `LastBoardPosition`, removes it from the zone, and queues its Deathrattle with `ActivateTask(PowerActivation.DEATHRATTLE)`. Fireplace ([`game.py`](https://github.com/jleclanche/fireplace/blob/master/fireplace/game.py)) uses a depth counter, `_action_stack`. When it returns to 0, `action_end` calls `refresh_auras()` and then `process_deaths()`. `process_deaths` wraps a `Death(...)` action in its own block, so closing that block re-enters `process_deaths` until nothing is dead. Fireplace's `dead` property ([`card.py`](https://github.com/jleclanche/fireplace/blob/master/fireplace/card.py)) is true if the card is in the graveyard, has `to_be_destroyed` set, or has health at or below zero. That is the "mortally wounded / pending destroy" idea expressed as a property.

**Engine implication.**
- `TakeDamage` must **never** remove a minion from the board or emit a death. It only lowers Health, which may go negative.
- Death is a separate **cleanup routine** that runs only when the Phase depth returns to 0. It snapshots every dead entity, sorts by `PlayOrder`, removes all of them, then resolves their triggers one death at a time. It loops until no deaths remain.
- Record each death's board position *before* removing anything. Deathrattle summons need it (see §7).

---

## 4. Simultaneity and trigger ordering

### 4.1 Queues are built once, then locked

When a Phase or Event starts, the game queues every trigger that responds to it, in order of play. Once the first entry starts to resolve, the queue is **locked** and nothing can join it (Rules 2a and 2c, [§ Sequences, Phases, Queues, Resolution](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Sequences,_Phases,_Queues,_Resolution)). As a result, a minion summoned in the middle of a resolution cannot respond to the Event already in progress. It can respond to later Phases and Events.

*Example.* "Oracle of Delphi" has the Deathrattle "summon a 2/2 Shade". "Shade" has "whenever a friendly minion dies, gain +1 Attack". If three minions die together, the Shade summoned by the first death **misses** the other deaths of that Death Phase, because their queues were built before it existed.

### 4.2 Simultaneous events resolve one at a time, in order of play

"Simultaneous" in Hearthstone does *not* mean "in parallel". When several Events happen together, each Event is queued in order of play, and each one (with its triggers) fully resolves before the next begins (Rules 1b and 2b). For area damage, **all the damage is applied first**. Then the "takes damage" triggers resolve, in order of play ([§ Damage and Healing](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Damage_and_Healing), rules DH1 and DH2a). A few named spells are exceptions: they resolve each damage event as soon as it is created, though they still pick all their targets first (DH3).

*Example.* "Pyre of Hephaestus" deals 2 damage to all minions. "Bronze Sentinel" (played first, takes damage → deal 1 to the enemy hero) and "Ember Wisp" (played second, same text) both take 2. Then Sentinel's trigger resolves, then Wisp's. If either reached 0, it still triggers: it is mortally wounded, not dead.

### 4.3 Combat damage

The attack and the counterattack are two damage Events in the same Combat Phase, created in the order *attack, then counterattack*. No death step runs between them, so a minion that drops to 0 from the first hit still deals its counter-damage. Because the attacker's hit comes first, the defender's "whenever this takes damage" trigger resolves before the attacker's. For "whenever this deals damage", it is the other way round ([§ Combat](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Combat), examples). Both simulators apply the attacker's hit on the defender first, then the counterattack, with no death check in between (§2.3).

### 4.4 How each trigger family queues relative to the others

| Trigger family | When it is queued | Notes |
|---|---|---|
| **"Whenever X"** (whenever you play, whenever a minion takes damage, whenever a minion dies) | During the Event or Phase it responds to. It resolves **inside** that Phase, as a nested Phase. | Damage dealt by a "whenever" trigger still kills nobody until the outermost Phase ends. |
| **Battle Cry** | Not a queued trigger. It is a Phase of its own minion's play Sequence: after On Play and Summon Resolution, before After Play. | Resolves even if its minion has died (§2.1). |
| **"After X"** (after you play, after you summon, after you cast, after attacking) | In a separate, **later** Phase of the Sequence, after deaths from the earlier Phases are processed. | Pre-checked: since patch 9.2.0.21517 an "after" trigger works only if it was valid when the Sequence began ([§ Playing a spell](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Playing_a_spell)). |
| **Deathrattle and on-death triggers** | In the Death Phase that follows the Death Creation Step. Grouped per death, in order of play. | A Deathrattle resolves even though its owner has already left the board. Other triggers of a removed entity are cancelled (§4.6). |
| **Start / end of turn** | In the dedicated Start of Turn and End of Turn Phases of the end-turn Sequence (§1). | Each queue is built in order of play across the whole board. |

So, for one minion play: **"whenever you play" → "whenever you summon" → Battle Cry → (deaths) → "after you play/summon" → (deaths) → win check.** For a death: **death removed → its Deathrattle and every on-death trigger, in order of play → next death → (new deaths) → next Death Phase.**

### 4.5 Condition-check timing

A trigger's condition can be checked at three points: at **pre-check** (Sequence start, used only by "after" triggers and Death Phase triggers), at **queuing**, and at **resolution**. Most triggers queue on the event and check their condition only when they resolve. Some keywords check at both queue and resolution time, for example Avenge-style triggers that need a non-empty board ([§ Avenge, Redemption and Effigy](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Avenge,_Redemption_and_Effigy); [§ Trigger conditions and Queuing conditions](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Trigger_conditions_and_Queuing_conditions); Rule 10b). Most Battle Cries check their target only when the card is played. Random "negative" effects (random damage, random destroy, misdirection) **skip** mortally wounded and pending-destroy characters. Area effects, healing and buffs **include** them (Rule 5; [§ What actually ignores Mortally Wounded?](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#What_actually_ignores_Mortally_Wounded?)).

### 4.6 Leaving play mid-Phase cancels pending triggers, except Deathrattles

If an entity leaves play mid-Phase, for example by being returned to hand, its other triggers already in the queue are removed and never fire. Deathrattles are the exception: they resolve from any zone (Rules MPR1 and MPR2, [§ Mid-Phase removal and triggers](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Mid-Phase_removal_and_triggers)).

### 4.7 The Dominant Player Bug: a determinism trap

For a shared Event, Hearthstone resolves all of **player 1's** triggers first (player 1 means internal `PLAYER_ID` 1, not "who went first"), each player's in order of play. Then it resolves player 2's. Within a single player, triggers in play come before triggers in hand, which come before triggers in deck (Rules DP1 and DP4, [§ Dominant Player Bug](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Dominant_Player_Bug); [§ The Order of Battlefield, Hand and Deck Triggers](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#The_Order_of_Battlefield,_Hand_and_Deck_Triggers)). This bug does not change the order of simultaneous deaths or damage. Fireplace mirrors the zone order: `Action.broadcast` scans `game.entities`, then each hand, then each deck ([`actions.py`](https://github.com/jleclanche/fireplace/blob/master/fireplace/actions.py)).

### 4.8 How the simulators implement the queue

- **SabberStone** keeps a `TaskQueue` ([`TaskProcessor.cs`](https://github.com/ADockhorn/HearthstoneAICompetition/blob/master/SabberStoneCore/src/Model/TaskProcessor.cs)) that is a **stack of FIFO queues**, `Stack<Queue<TaskInstance>>`. `StartEvent()` pushes a fresh queue and `EndEvent()` pops back to the outer one, so tasks raised by an inner event drain before the outer queue continues. That is Hearthstone's nested Phases. Triggers enqueue their task in `Trigger.ProcessInternal` ([`Trigger.cs`](https://github.com/ADockhorn/HearthstoneAICompetition/blob/master/SabberStoneCore/src/Triggers/Trigger.cs)). End-of-turn triggers use a `FastExecution` path that runs at once. Trigger *order* comes from .NET multicast-delegate subscription order. That order is close to order of play but is not an explicit sort. Only the dead are explicitly sorted by `OrderOfPlay`.
- **Fireplace** uses a single depth counter (`_action_stack`), a `play_counter` stamp that `live_entities` is sorted by, and `ON`/`AFTER` listener timings on each action ([`game.py`](https://github.com/jleclanche/fireplace/blob/master/fireplace/game.py), [`actions.py`](https://github.com/jleclanche/fireplace/blob/master/fireplace/actions.py)).

**Engine implication.** You need three things. First, an explicit **Phase depth** (Fireplace's counter is enough). Second, a **per-Phase queue snapshot** sorted by `PlayOrder` (SabberStone's stack of queues is one way to do it). Third, cleanup that runs **only at depth 0**. Sort explicitly. Do not rely on subscription or collection order the way SabberStone does, because that is how replays silently diverge. Do not copy the Dominant Player Bug. Pick one documented rule for shared Events and test it. The simplest rule is pure order of play across both players, which is what the rulebook's single shared order-of-play list implies.

---

## 5. Auras

An **aura** is an ongoing effect that applies only while its source is in play, for example "other friendly minions have +1 Attack". A **buff** is an enchantment that stays once given ([Aura § Auras](https://hearthstone.wiki.gg/wiki/Aura)).

- **Auras are not recalculated continuously.** They update at fixed Steps: after each outermost Phase (Health/Attack *before* the Death Creation Step, Other *after* it), when a minion is played (before its Battle Cry), and when a minion is summoned (Rules 4a and 4b; [§ Auras](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Auras)). They are **not** recalculated when a minion leaves play or changes sides mid-Phase. A minion therefore keeps an aura's bonus until the next update, even if the aura's source has already gone.
- **Why Health/Attack updates before deaths.** A Health aura is recalculated before the Death Creation Step, so a new +Health aura can save a minion that dropped to 0 in the same Phase. The reverse also holds: when an aura minion dies alongside others, its Health bonus lingers through that Death Phase ([§ Auras](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Auras)).
- **Ordering among stat changes.** Ordinary buffs apply in the order they were given. Aura bonuses are always applied **after** all other enchantments ([Aura § Notes](https://hearthstone.wiki.gg/wiki/Aura)). A "double this minion's Attack" buff therefore does not double the aura part.
- **Losing max Health is not damage.** When max Health goes down, current Health only drops if it is above the new max ([Health § Enchantments](https://hearthstone.wiki.gg/wiki/Health)). *Example.* "Hoplite" is a 2/1 that gets +1 Health from "Phalanx Captain", making it 2/2. It takes 1 damage, so it is 2/1 (damaged). Then the Captain dies. The Hoplite's max drops back to 1, its current Health stays 1, and it **survives**, undamaged.
- **Silencing an aura's target does not remove the aura's bonus.** Only silencing or removing the aura's source ends it ([Aura § Auras](https://hearthstone.wiki.gg/wiki/Aura)).

**Simulators.** SabberStone's `AuraUpdate()` walks the aura list and calls `Update()` on each. It is called at the start and end of `DeathProcessingAndAuraUpdate()`, so it is a single combined update rather than Hearthstone's two-part split ([`Game.cs`](https://github.com/ADockhorn/HearthstoneAICompetition/blob/master/SabberStoneCore/src/Model/Game.cs)). Fireplace's `refresh_auras()` runs every update script sorted by a `priority` (default 50). It then drops aura buffs whose `tick` is stale, which recomputes auras from scratch on each refresh ([`game.py`](https://github.com/jleclanche/fireplace/blob/master/fireplace/game.py)).

**Engine implication.** Treat auras as **derived state that is recomputed at defined Steps**, never as permanent mutations. The simplest correct design is Fireplace's: drop all aura bonuses and re-apply them from every live source. Run it right before and right after the Death Creation Step, and on play or summon. The Hoplite example also shows that naively storing `damage` and computing `health = maxHealth - damage` gets the result wrong (it gives 1 − 1 = 0, so the Hoplite dies). When max Health drops, clamp `damage` so current Health equals `min(oldCurrent, newMax)`. Auras are outside the MVP card set, but leave the two update slots in the cleanup routine now so they need no refactor later.

---

## 6. Fatigue and overdraw

**Fatigue** ([Fatigue](https://hearthstone.wiki.gg/wiki/Fatigue)): each attempt to draw from an empty deck deals damage to the drawing player's own hero. The damage is 1 the first time, 2 the next, and so on, so the total after N failed draws is N(N+1)/2. Each player has their own counter, and it **never resets**, even if cards are later shuffled back into the deck. Each card of a multi-card draw is a separate attempt. Armor absorbs fatigue damage like any other damage. Only *draws* cause fatigue. Moving a card from deck to hand by another means (putting it into hand, discarding, removing it from the deck) does not.

**Overdraw** ([Hand § Mechanics](https://hearthstone.wiki.gg/wiki/Hand); [§ Zones](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Zones); [§ Drawing a Card](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Drawing_a_Card)): the hand limit is 10. A card drawn into a full hand is **destroyed** and revealed to both players. It goes to the graveyard, and its "when drawn" effects **do not** trigger. Hearthstone does not treat this as a discard, so discard triggers do not fire either. A multi-card draw first counts the cards, then resolves them one at a time. On-draw triggers fire in order of play, then the card's own "when drawn" effect resolves (rules DC1 and DC2).

**Full board** ([§ Zones](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Zones), rules Z0–Z2): a minion that would be *created* on a full board is simply not created (Z0). It is not created and then killed. A minion that would *move* onto a full board dies (Z1). If several moves happen at once, they resolve in order of play, so the newest ones are the ones lost (Z2).

**Simulators.** SabberStone computes fatigue as `c.Hero.Fatigue == 0 ? 1 : c.Hero.Fatigue + 1` and deals it to the drawing player's own hero ([`Draw.cs`](https://github.com/ADockhorn/HearthstoneAICompetition/blob/master/SabberStoneCore/src/Actions/Draw.cs)). Fireplace's `Fatigue` action increments `fatigue_counter` and queues `Hit(target.hero, target.fatigue_counter)`. Its `Draw` action, on a full hand, logs an overdraw and calls `card.discard()` ([`actions.py`](https://github.com/jleclanche/fireplace/blob/master/fireplace/actions.py)). Using the discard method there is a trap: if any listener reacts to discards, it would wrongly fire on an overdraw.

**Engine implication.** Keep a per-player `FatigueCounter` in `GameState` that never resets. Model a draw as a single Event that has three distinct outcomes: card drawn, card burned, or fatigue damage. Each outcome has its own event type, and "burned" is not the same as "discarded". The fatigue damage is an ordinary damage Event, so a hero who reaches 0 from fatigue during the start-of-turn draw loses at the win check that follows it.

---

## 7. Edge cases that break naive implementations

1. **Removing a minion the instant it reaches 0 Health.** This breaks "damage then heal in one spell", same-Phase aura saves, and counter-damage from a minion the first hit already wounded (§3, §4.3).
2. **Ending the game the instant a hero reaches 0.** Hearthstone checks win/loss only at the end of a Sequence (and once mid-attack). Later triggers still resolve, so both heroes can reach 0 in one Sequence, and that is a **draw** (Rule 9, [§ Sequences, Phases, Queues, Resolution](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Sequences,_Phases,_Queues,_Resolution)). This matches design.md's "both at 0 → draw". Fireplace's `check_for_end_game` turns two losing players into `TIED` ([`game.py`](https://github.com/jleclanche/fireplace/blob/master/fireplace/game.py)).
3. **Letting new entities join a queue that is already resolving.** Queues lock (§4.1). Without the lock, one Deathrattle summon can chain-react through deaths it should never have seen.
4. **Running a Deathrattle "on the board".** Its owner is already gone. Deathrattle summons appear at the dead minion's *recorded* position. Positions are computed by removing the earlier-dying minions first, and are not adjusted for other changes ([§ Where do Minions summoned by Deathrattles spawn?](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Where_do_Minions_summoned_by_Deathrattles_spawn?)). With a full board, the summon simply fails (§6).
5. **Firing a bounced minion's queued triggers.** They are cancelled, except Deathrattles (§4.6).
6. **Cancelling a Battle Cry because its minion died.** It still resolves (§2.1).
7. **Charging the attack of a cancelled attack.** If a combatant dies during Combat Preparation, the attack never happened and the attacker can attack again (§2.3).
8. **Letting random damage pick a mortally wounded minion.** Random negative effects skip such minions, while area effects and buffs include them (§4.5). A random-target helper therefore needs an "is alive for targeting" filter.
9. **Losing Health from an aura is not damage.** A naive `max − damage` formula kills minions that should survive (§5).
10. **Counting overdraw as a discard, or fatigue on non-draw moves.** Both are wrong (§6).
11. **Iterating triggers in collection or hash order.** Order of play is a single total order. Anything else makes replays and AI clones disagree. This is the same reason AGENTS.md bans ad-hoc randomness (decision 2).
12. **Turn-limit draw.** Hearthstone declares a draw once turn 89 ends (§1). Without a cap, a stalemate between two AIs that each have Armor-gain effects never ends.

---

## 8. Implications for Pantheon's engine

### 8.1 A resolution model to bring to the next ticket

These are recommendations for the *"Action resolution and trigger ordering"* ticket, not decisions. They are a slimmed-down version of Hearthstone's model, sized to the MVP (Creatures, Battle Cry and Deathrattle only, per design.md):

1. **One `ApplyMove` is one Sequence.** It runs synchronously to completion. The MVP has no mid-resolution player choices (no Discover or "Choose One"), so the trigger queue can live **on the call stack inside the resolver**, and not on `GameState`. `Clone()` then never has to copy a half-finished queue, because AI only clones between moves (AGENTS.md decision 1). If mid-resolution choices arrive later, resolution has to become suspendable. SabberStone needed `_pendingTasks` and `ResumePendingTasks` for this.
2. **Each action is a fixed list of Phases.** *Play minion:* `OnPlay → BattleCry → AfterPlay`. *Attack:* `Preparation → Combat`. *End turn:* `EndOfTurn → Switch (Step) → StartOfTurn → Draw`. A generic `RunPhase` builds a sorted queue snapshot, resolves it with nesting, and runs cleanup when depth returns to 0.
3. **Cleanup at depth 0:** aura update (a no-op in the MVP) → Death Creation (snapshot every entity with Health ≤ 0 or pending destroy, sort by `PlayOrder`, record positions, remove all) → aura update → Death Phase (one death at a time, with triggers in `PlayOrder`) → loop until no deaths remain.
4. **Win check at the end of the Sequence**, plus once between attack Preparation and Combat. Both heroes at 0 → draw.
5. **`PlayOrder`** is an `int` stamp from a counter on `GameState` that only goes up. It is assigned when an entity enters play. Every queue sorts by it, for both players together (no Dominant Player rule).
6. **Health** is stored as base and max Health plus a damage amount. Death is a *query* ("Health ≤ 0 or pending destroy"), never a side effect of `TakeDamage`.
7. **Random choices** happen at resolution time through the single `GameState` PRNG, over a candidate list sorted by `PlayOrder`. That keeps them reproducible (AGENTS.md decision 2).

A sketch of the shape. It shows the shape only and is not meant to be copied as code:

```text
ApplyMove(move):
  foreach phase in SequenceFor(move):
    if phase.Subject has left play and phase is not BattleCry: stop the sequence
    RunPhase(phase)                 // depth++ ; build queue (sorted, locked) ; resolve ; depth--
    if depth == 0: Cleanup()        // aura → deaths (loop) → aura
  CheckWinLossDraw()
```

### 8.2 Mapping steps to fat events

AGENTS.md decision 3 requires semantic events that carry their deltas, so the view can animate with no rules knowledge. Hearthstone's own client follows the same split: the server resolves the whole action first, and the client then animates the result, sometimes running behind the real state ([§ Visual appearance of Player Actions](https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Visual_appearance_of_Player_Actions)). Suggested emission points:

| Resolution step | Event(s) appended to the log | Payload ("the deltas") |
|---|---|---|
| Setup of a play | `CardPlayed`, `ManaSpent`, `MinionSummoned` | card, slot index, mana `5→2`, `PlayOrder` |
| Battlecry Phase starts | `BattleCryTriggered` | source minion; the effect's own events follow it |
| Each damage Event | `MinionDamaged` / `HeroDamaged` | source, target, amount, Health `3→-1`, `Cause` |
| Attack Preparation | `AttackDeclared` | attacker, defender (the final one, after any redirect) |
| Combat Phase | two damage events, attack first and counterattack second | as above |
| Death Creation Step | one `MinionDied` per death, in `PlayOrder` | id, owner, board position at death |
| Each death's triggers | `DeathrattleTriggered`, then the effect's events | source (now in the graveyard), position |
| Aura update (later) | `StatsChanged`, only when a value actually changes | id, Attack `2→3`, Health `2→3` |
| Draw outcomes | `CardDrawn` / `CardBurned` / `FatigueDamaged` | card (hidden from the opponent), fatigue amount N |
| Turn switch | `TurnEnded`, `TurnStarted`, `ManaRefilled` | player, turn number, max/available mana |
| End-of-Sequence check | `GameEnded` | winner, or draw |

Two design choices follow from this.

- **Give each event a `Cause`.** It names the source entity and the trigger kind (Battle Cry, Deathrattle, start of turn, …). The view then knows *why* without re-running rules, and you do not need `PhaseStarted`/`PhaseEnded` marker events. (Hearthstone's own protocol does use block markers: SabberStone writes `PowerHistoryBuilder.BlockStart(...)` around each block ([`Game.cs`](https://github.com/ADockhorn/HearthstoneAICompetition/blob/master/SabberStoneCore/src/Model/Game.cs)). That is an option for later if animations need grouping.)
- **Emit events in resolution order.** Do not emit them grouped by type. The log order *is* the animation order, and replay tests can assert it directly.

### 8.3 What to adopt now, what to defer

- **Adopt for the MVP:** Sequence/Phase/Queue nesting; deaths only at depth 0; the death loop; order-of-play sorting; locked queues; Battle Cry surviving its minion's death; simultaneous combat damage; the win check at Sequence end, with a draw when both heroes die; fatigue and burn; a turn cap.
- **Leave a slot, implement later:** the two aura update Steps; "after X" Phases with pre-check (Pantheon has no "after" cards yet); cancelling a bounced minion's queued triggers.
- **Do not copy:** the Dominant Player Bug; Forced Death Phases (until a card needs one); Hearthstone's many card-specific exceptions to condition timing.

---

## Bottom line

- Hearthstone resolves each player action as a **Sequence of Phases**. Each Phase builds a **locked queue of triggers sorted by order of play**, and Phases nest.
- **Nothing dies mid-Phase.** Health can go to 0 or below ("mortally wounded"). Deaths are processed only when the **outermost** Phase ends: Aura Update → remove every dead entity at once → Aura Update → a **Death Phase** that resolves each death's triggers one death at a time, in order of play, repeated until no new deaths occur.
- **"Simultaneous" means "applied together, then resolved one by one in order of play".** Area damage lands on every target first, then the triggers fire. In combat, both hits land in one Phase (attack first, then counterattack) with no death check between them.
- **Trigger families have fixed slots.** "Whenever" triggers resolve inside the Phase that raised them. The Battle Cry is its own Phase between On Play and After Play. "After" triggers are a later Phase, pre-checked at Sequence start. Deathrattles and on-death triggers run in the Death Phase. Start and end of turn have their own Phases, and the turn's draw comes *after* start-of-turn triggers.
- **Win/loss is checked at the end of the Sequence** (and once mid-attack). Both heroes at 0 → draw.
- **Auras are recomputed at fixed Steps, not continuously.** Losing an aura's Health is not damage, so store damage and clamp it when max Health drops.
- **Fatigue:** a per-player counter that never resets, dealing 1, 2, 3 … damage per failed draw. **Overdraw:** the card is destroyed, its draw effects do not fire, and it is not a discard. **Full board:** a summon simply fails.
- **SabberStone and Fireplace both confirm the core model:** a nested queue or depth counter, cleanup at depth 0, deaths sorted by play order, and a repeat-until-no-deaths loop. They differ from the rulebook in small ways (for example, SabberStone aborts an attack on a `ToBeDestroyed` flag instead of running a full death step).
- **For Pantheon:** one `ApplyMove` = one Sequence, run synchronously, with the queue on the call stack. Use an explicit `PlayOrder` stamp, a `RunPhase`/`Cleanup` pair, and `MinionDied` events emitted in play order at the Death Creation Step. Each fat event carries a `Cause`, and the log order is the animation order.

---

## Sources

**Hearthstone Wiki (community primary source, reverse-engineered by testing)**
- Advanced rulebook (all sections cited above): https://hearthstone.wiki.gg/wiki/Advanced_rulebook
  - § Sequences, Phases, Queues, Resolution (Rules 1–10): https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Sequences,_Phases,_Queues,_Resolution
  - § Death Phases and consequences of Death: https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Death_Phases_and_consequences_of_Death
  - § Start and end of turn: https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Start_and_end_of_turn
  - § Playing a spell: https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Playing_a_spell
  - § Playing/summoning a minion: https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Playing/summoning_a_minion
  - § Combat: https://hearthstone.wiki.gg/wiki/Advanced_rulebook#Combat
  - § Auras, § Zones, § Drawing a Card, § Damage and Healing, § Death Event Cache, § Visual appearance of Player Actions, § Glossary: same page, matching anchors
  - Esoteric rulebook: § Forced Death Phase, § Summon Resolution Step, § Dominant Player Bug, § Mid-Phase removal and triggers, § Avenge, Redemption and Effigy, § Where do Minions summoned by Deathrattles spawn?, § What actually ignores Mortally Wounded?
- Fatigue: https://hearthstone.wiki.gg/wiki/Fatigue
- Hand (overdraw): https://hearthstone.wiki.gg/wiki/Hand
- Health: https://hearthstone.wiki.gg/wiki/Health
- Aura: https://hearthstone.wiki.gg/wiki/Aura
- Deathrattle (§ Sequence of events): https://hearthstone.wiki.gg/wiki/Deathrattle
- Mana (crystal gain, Overload timing): https://hearthstone.wiki.gg/wiki/Mana

**SabberStone (C#, AGPL-3.0). Read via the `ADockhorn/HearthstoneAICompetition` fork because `HearthSim/SabberStone` returned 404 on 2026-11-24**
- `Game.cs` (turn steps, `DeathProcessingAndAuraUpdate`, `GraveYard`, `AuraUpdate`): https://github.com/ADockhorn/HearthstoneAICompetition/blob/master/SabberStoneCore/src/Model/Game.cs
- `Attack.cs` (`AttackBlock`, damage exchange): https://github.com/ADockhorn/HearthstoneAICompetition/blob/master/SabberStoneCore/src/Actions/Attack.cs
- `PlayCard.cs` (`PlayMinion`, `PlaySpell`): https://github.com/ADockhorn/HearthstoneAICompetition/blob/master/SabberStoneCore/src/Actions/PlayCard.cs
- `Draw.cs` (fatigue): https://github.com/ADockhorn/HearthstoneAICompetition/blob/master/SabberStoneCore/src/Actions/Draw.cs
- `Trigger.cs` (enqueue vs `FastExecution`, sequence validation): https://github.com/ADockhorn/HearthstoneAICompetition/blob/master/SabberStoneCore/src/Triggers/Trigger.cs
- `TaskProcessor.cs` (the active `TaskQueue`: a stack of FIFO queues): https://github.com/ADockhorn/HearthstoneAICompetition/blob/master/SabberStoneCore/src/Model/TaskProcessor.cs

**Fireplace (Python)**
- `game.py` (`action_end`, `process_deaths`, `refresh_auras`, `_begin_turn`, `check_for_end_game`): https://github.com/jleclanche/fireplace/blob/master/fireplace/game.py
- `actions.py` (`Attack`, `Death`, `Draw`, `Fatigue`, `Play`, `broadcast`): https://github.com/jleclanche/fireplace/blob/master/fireplace/actions.py
- `card.py` (`dead`, `to_be_destroyed`, `should_exit_combat`, damage-based Health): https://github.com/jleclanche/fireplace/blob/master/fireplace/card.py

## Verification notes and gaps

- **The rulebook is reverse-engineered, not official.** It reflects community testing, and some of its notes are marked untested (for example, whether patch 9.2 removed the hand/deck "Quad Queue"). The [Patch 9.2.0.21517](https://hearthstone.wiki.gg/wiki/Patch_9.2.0.21517) notes page does **not** mention the "after triggers must be valid at Sequence start" change. That claim rests on the Advanced rulebook alone.
- **The Advanced rulebook is about 480k characters long.** I read it section by section and paraphrased it. Rule numbers (1b, 2c, DH1, Z0, DC1, MPR1, DP1 …) are the rulebook's own labels, so a reader can find each claim.
- **Simulator code was read from `master` on 2026-11-24**, through web fetches of the raw files. The links are not pinned to commits and may drift. The canonical SabberStone repo now 404s, so the links in [`state-architecture.md`](state-architecture.md) that point at `HearthSim/SabberStone` are probably broken too and should be updated.
- **Not verified in simulator source:** how SabberStone orders same-event triggers across the two players, how Fireplace's `discard()` interacts with discard listeners on overdraw, and how either simulator handles losing an aura's Health. These statements are taken from the wiki only.
- **The §8 recommendations are synthesis**, reasoned from the sources plus Pantheon's locked decisions. They are inputs for the next ticket, not settled rules.
