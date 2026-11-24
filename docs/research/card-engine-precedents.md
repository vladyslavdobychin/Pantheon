# Card-Engine Precedents: How Open-Source Rules Engines Are Built

> **What decision this feeds.** Pantheon's engine shape is locked in [`AGENTS.md`](../../AGENTS.md): mutable entities + `GameState.Clone()`, one seeded PRNG, fat semantic events, a synchronous Core (`StartGame` / `GetValidMoves` / `ApplyMove`), an async Runner that asks one `IAgent` at a time, and an authoritative server later. This document reads the **source code** of four open-source card-game engines to check those decisions against how working engines actually solve the same problems. It answers seven questions, one per section, comparing the engines side by side. Then it lists where a precedent contradicts or complicates a Pantheon decision.
>
> **Relationship to [`state-architecture.md`](state-architecture.md).** That doc already covers SabberStone's project layout, its `Game`/`Controller`/`Entity` graph, how `Game.Clone()` deep-copies it, the `DeepCloneableRandom` reseed-on-clone nuance, the beam-search and greedy bots, and the `POGame` placeholder-card trick for hidden information. This doc does **not** repeat that. It points back to it and covers what that doc does not: zones, buffs and auras, resolution order, mid-resolution choices, card authoring, and the other three engines.

## The engines

| Engine | Game | Language | Why it is here |
|---|---|---|---|
| **SabberStone** | Hearthstone | C# (.NET Core) | Closest precedent to Pantheon: same language, same genre, built for AI search. |
| **Fireplace** | Hearthstone | Python | Same genre, very different style: computes stats on read, writes cards in a Python mini-language. |
| **Forge** | Magic: The Gathering | Java | The largest and most rules-heavy of the four. Shows what a full-rules engine needs, and what it costs. |
| **Metastone** | Hearthstone | Java | Added for two things only: cards written as pure JSON data, and an agent interface that looks like Pantheon's `IAgent`. |

**Pinned versions.** Every link below points at a fixed commit, so line numbers will not drift:

- SabberStone @ `1db6e27` (2021-01-15, the last upstream commit). **Note:** the canonical repo `HearthSim/SabberStone` returned HTTP 404 from both github.com and the GitHub API on 2026-11-24. Its fork network now has `ifeherva/SabberStone` as its root. I read the code from the fork `MokonaNico/SabberStone`, whose `master` is exactly upstream's last commit `1db6e2711a13…` (author darkfriend77). Forks share git objects, so this SHA is the same code upstream had.
- Fireplace @ `47a2572` (2025-12-19).
- Forge @ `3e1fa4e` (2026-11-24).
- Metastone @ `bab2c6a` (2018-08-17, the last commit).

**Vocabulary used throughout** (Hearthstone / MTG terms a backend developer may not know):

- **Zone** — a named place a card can be: deck, hand, board ("play" / "battlefield"), graveyard, and so on.
- **Tag** — Hearthstone's name for one integer property of an entity (attack, health, cost, "has taunt"). Blizzard's own client models every entity as a bag of tags, and SabberStone and Fireplace copy that model.
- **Buff / enchantment** — a stat change attached to a card ("+2 Attack"). In Hearthstone, a buff is itself a small hidden entity called an *enchantment*.
- **Aura** — a continuous effect that lasts only while its source is present: "your other minions have +1 Attack" stops the moment the source dies.
- **Silence** — removes every buff and ability from a minion, so it goes back to its printed stats.
- **Battlecry / Deathrattle** — an effect that runs when the card is played / when it dies.
- **Trigger** — an ability that waits for an event ("whenever a minion dies, draw a card").
- **Discover** — "choose one of three random cards". The player must make a choice in the middle of an effect.
- **Choose One** — a card with two modes; the player picks one when playing it.
- **Mulligan** — at game start, each player may swap cards from their opening hand.
- **The stack / priority** (MTG only) — spells wait in a last-in-first-out pile, and players take turns getting the right ("priority") to respond before the top item resolves.
- **State-based actions** (MTG) — rules checks the engine runs between actions, for example "a creature with 0 toughness dies".
- **Layers** (MTG) — a fixed order for applying continuous effects, so that "set power to 1" and "+2/+2" give the same answer however they were cast.
- **Determinization** — for AI search under hidden information: guess one concrete version of what you cannot see (opponent's hand, deck order), then search as if the game were fully visible.

---

## 1. Entity and zone model

**The question:** Is the card in your hand the same object as the minion it becomes on the board? How are entities identified?

| | Same object hand→board? | Identity |
|---|---|---|
| **SabberStone** | **Yes.** One `Minion` object is created from the card and moved between zone lists. | Integer `Id` from a per-game counter. Every entity is registered in one id→entity table. |
| **Fireplace** | **Yes.** One Python object; setting `card.zone` moves it between per-player lists. | Integer `entity_id` from a per-game counter. |
| **Forge** | **No, not always.** Moving onto the battlefield keeps the object. Most other moves build a **new** `Card` object with the **same integer id**, plus a frozen "last known information" (LKI) copy of the old one. | Integer id from a per-game counter. Timestamps tell "old" and "new" versions of the same id apart. |
| **Metastone** | Yes (one object per card). | Integer id. |

**SabberStone.** `Entity.FromCard` picks the subclass (`Minion`, `Spell`, `Weapon`, `Hero`, `HeroPower`) from the card type and registers the new object under its id in `game.IdEntityDic` ([Entity.cs L251–L314][sb-fromcard]). Ids come from a simple counter on `Game` ([Game.cs L223][sb-nextid]). Playing a minion first removes it from the hand zone and then adds the *same* object to `BoardZone` ([PlayCard.cs L55][sb-removezone], [PlayCard.cs L247][sb-boardadd]). Each entity also keeps a reference to its immutable `Card` blueprint ([Entity.cs L123][sb-entity-card]). This is the same split as Pantheon's `CardDefinition` (blueprint) vs `CardInstance` (runtime).

**Fireplace.** `Card(id)` builds a `Minion` / `Spell` / `Enchantment` / … from the card database ([card.py L35][fp-cardfactory]). `Player.card()` registers it with the game manager, which hands out the next integer `entity_id` ([player.py L253–L264][fp-playercard], [managers.py L56–L71][fp-newentity]). The `zone` setter removes the object from the old zone's list and appends it to the new one ([card.py L201][fp-setzone]). Each entity also gets a random `uuid4` ([entity.py L18][fp-uuid]). It is not used by the rules, but it is a second, non-deterministic identity to be careful with.

**Forge.** `GameAction.changeZone` decides per move whether to keep the object. Moving to the battlefield keeps it (`copied = c`). Otherwise it builds a new object with `new CardCopyService(c).copyCard(false)` and records an LKI snapshot ([GameAction.java L169–L207][fg-changezone]). The copy keeps the original id ([CardCopyService.java L117–L125][fg-copyid]). This follows MTG's rule that a card which changes zones becomes a new object: an effect that targeted the old one must not follow it. `Card.equalsWithGameTimestamp` tells versions apart ([Card.java L6927][fg-eqts]). Ids come from `Game.nextCardId()` ([Game.java L1169][fg-nextid]).

> **Why this matters for Pantheon.** Both Hearthstone engines use the simple model Pantheon already has: one `CardInstance` per physical card, moved between zones, stable integer id. Forge's "new object per zone change" is an MTG rules need, not a Hearthstone one, so Pantheon can skip it. The lesson that does carry over: **refer to entities by id wherever state must survive `Clone()`** (pending choices, aura targets, trigger targets). After a clone, object references point into the *old* state; ids still work. SabberStone's pending `Choice` stores entity ids (`List<int>`), not object references, for exactly this reason ([Choice.cs L84, L111][sb-choice-fields]).

---

## 2. Stats and modifiers

**The question:** Are stats mutated in place, or computed from base + buffs + auras? How do silence and aura removal work?

All four engines keep the **printed** value on the blueprint and the **current damage** as its own number (health = max health − damage). They differ on buffs and auras:

| | Buffs (enchantments) | Auras | Silence |
|---|---|---|---|
| **SabberStone** | **Mutated in place.** Applying a buff edits the entity's own tag value. Removing it applies the inverse operation. | **Separate layer.** Aura contributions go into a per-entity `AuraEffects` accumulator, added on read. | Imperative: clear keyword flags, remove own aura and trigger, remove enchantments **except** aura-granted ones, reset attack to the printed value. |
| **Fireplace** | **Computed on read.** Each buff is an `Enchantment` entity in a `buffs` list. A stat getter folds base + every buff + every aura slot. | Recomputed after every action from scratch: each aura re-marks what it affects; anything not re-marked is deleted. | Clear the `buffs` list and event listeners, set `silenced`. A silenced minion's own aura then stops being refreshed and disappears by itself. |
| **Forge** | **Computed on read.** Changes live in per-layer tables keyed by (timestamp, ability id). | All continuous effects are **wiped and reapplied** in MTG layer order on every check. | No special case: "loses all abilities" is just one more continuous effect. |

**SabberStone.** The tag getter reads the entity's own stored value, falls back to the blueprint's value, then adds the aura layer ([Entity.cs L208–L230][sb-entity-idx]):

```csharp
if (!_data.TryGetValue(t, out int value))
    Card.Tags.TryGetValue(t, out value);
value += AuraEffects?[in t] ?? 0;
```

A buff is an `Effect` (tag, operator, value). `ApplyTo` changes the stored tag directly (`NativeTags[Tag] += Value`), and `RemoveFrom` applies the opposite operation ([Effect.cs L73–L94][sb-effect-apply], [Effect.cs L235–L257][sb-effect-remove]). Aura effects go through `ApplyAuraTo` / `RemoveAuraFrom` instead, which only touch the separate `AuraEffects` object ([Effect.cs L162][sb-effect-aura], [Effect.cs L259][sb-effect-aura-remove]). Auras are applied lazily: an aura keeps a priority queue of add/remove instructions and works through them when the game calls `AuraUpdate()` ([Aura.cs L235–L270][sb-aura-update], [Game.cs L1164][sb-auraupdate]). When an aura's source leaves, `Remove()` queues a remove-all, and `RemoveInternal` subtracts its effect from every entity it touched ([Aura.cs L275][sb-aura-remove], [Aura.cs L448–L483][sb-aura-removeinternal]). `Minion.Silence()` is a long hand-written list: clear each keyword, remove the minion's own aura and trigger, remove its enchantments but skip any whose creator is an aura, then reset attack and clamp health ([Minion.cs L55–L115][sb-silence]).

**Fireplace.** Stats are Python properties. `int_property("atk")` calls `_getattr`, which starts from the stored base and folds every buff, then every aura slot, then an optional per-card script hook ([entity.py L113–L121][fp-getattr], [entity.py L159–L169][fp-intprop]). A buff is an `Enchantment` entity. Putting it into play appends it to its owner's `buffs`; removing it takes it out and adjusts damage so health cannot go negative by accident ([card.py L1499–L1520][fp-ench-zone]). Auras use a **mark-and-sweep** pass: `refresh_auras()` runs every aura script, and each one stamps the buffs it wants with the current tick. Any aura buff whose tick is old gets removed, then the tick advances ([game.py L275–L297][fp-refresh], [aura.py L30–L81][fp-aura]). Silence clears buffs and listeners ([actions.py L1634–L1652][fp-silence]). A silenced minion reports `ignore_scripts = True` ([card.py L1162][fp-ignorescripts]), so its aura scripts stop running and the next sweep removes its aura buffs. No extra code is needed.

**Forge.** A `Card` holds tables per MTG layer, for example `newPT` (set P/T, layer 7b) and `boostPT` (+N/+N, layer 7c), each keyed by timestamp and static-ability id ([Card.java L286–L289][fg-pttables]). Power is computed on read: the last "set" value wins over the base, then boosts and +1/+1 counters are added ([Card.java L4425–L4450][fg-netpower]). Static abilities ("creatures you control get +1/+1") are never patched incrementally. `checkStaticAbilities` clears **all** static effects and reapplies them layer by layer ([GameAction.java L1100–L1175][fg-checkstatic]). A temporary pump writes a timestamped boost and registers an end-of-turn command that deletes that row ([PumpEffect.java L61, L96–L122][fg-pump]). Silence-like effects are a `RemoveAllAbilities` parameter on a continuous effect ([StaticAbilityContinuous.java L352][fg-removeall]).

> **Why this matters for Pantheon.** "Mutable entities" does not mean "one mutable `Attack` field". Even SabberStone, the most mutation-heavy engine, keeps aura contributions in a separate layer, because removing an aura by hand-subtracting is the bug-prone part. The cheapest safe shape for Pantheon: store **base stats (from `CardDefinition`) + damage + a list of modifiers + an aura layer that is fully recomputed** after each action (Fireplace's mark-and-sweep, or Forge's wipe-and-reapply). Then silence is "clear the modifier list", and aura removal is automatic. This still fits decision 1: the entity is mutable and cloned. Only the *derived* numbers are computed.

---

## 3. Action resolution

**The question:** Is there a task/action queue? How are triggers ordered? When are deaths processed?

| | Resolution model | Trigger order | Deaths |
|---|---|---|---|
| **SabberStone** | A **stack of FIFO queues**, one queue per nested "event". Triggers enqueue tasks; the engine drains the queue. | Order of subscription (≈ order the minions were played). | After each phase of a play/attack: collect dead minions, sort by play order, run deathrattles, **repeat until none die**. |
| **Fireplace** | **Direct recursion.** An action runs at once and broadcasts ON/AFTER events; listeners run nested. A counter tracks the nesting depth. | Entities scanned in play order. | When the outermost action finishes (depth back to 0): refresh auras, then process all dead entities. |
| **Forge** | MTG's **stack** (LIFO) + **priority**. Triggers wait in a list and go onto the stack the next time someone would get priority. | Active player's triggers first, then the opponent's (MTG's APNAP rule). **Each player chooses** the order of their own simultaneous triggers. | State-based actions, checked in a loop (up to 9 passes) before any player gets priority. |

**SabberStone.** `TaskQueue` holds a `Stack<Queue<TaskInstance>>`. `StartEvent()` pushes a fresh queue, `Enqueue` adds to the current one, `Process()` runs the head task, and `EndEvent()` pops back to the outer queue ([TaskProcessor.cs L70, L97, L157, L220][sb-taskqueue]). The play sequence is hard-coded as phases. For a minion: add to board → on-play triggers → summon triggers → battlecry → **death and aura processing** → after-play triggers → death processing again ([PlayCard.cs L238–L319][sb-playminion]). `DeathProcessingAndAuraUpdate` updates auras, runs summon triggers, then loops `GraveYard()` + `ProcessTasks()` until no new deaths appear ([Game.cs L1191–L1220][sb-deathproc]). `GraveYard()` sorts the dead by `OrderOfPlay` before firing death triggers and deathrattles ([Game.cs L1086–L1120][sb-graveyard]). Triggers subscribe to C# `event` fields on a `TriggerManager` ([TriggerManager.cs L23][sb-triggermgr], [Trigger.cs L141–L146][sb-trigger-sub]). When one fires, it enqueues its task rather than running it ([Trigger.cs L297–L325][sb-trigger-process]). So trigger order is the order handlers were added to the delegate.

**Fireplace.** Every player action is an `action_block` (start, run the actions, end) ([game.py L132–L172][fp-actionblock]). Inside, actions run immediately. `Action.broadcast` walks every entity on board, in hand and in deck, and fires any matching listener at once, so nested triggers are plain recursion ([actions.py L164–L176][fp-broadcast]). `game.entities` and `game.board` are sorted by `play_counter`, which gives play-order trigger resolution ([game.py L77][fp-board]). The depth counter `_action_stack` decides when to clean up. Only when it drops to zero does the game refresh auras and call `process_deaths()` ([game.py L140–L143][fp-stackempty], [game.py L186][fp-deaths]). The `Death` action moves each dead card to the graveyard and queues its deathrattle. Then it broadcasts AFTER for all of them, using `play_counter` to decide which listeners still see each death ([actions.py L332–L374][fp-death]).

**Forge.** Spells and abilities go on `MagicStack` and resolve top-first ([MagicStack.java L632][fg-resolve]). Triggered abilities are not run when their event happens. `TriggerHandler.runTrigger` appends to `waitingTriggers`, and `runWaitingTriggers` processes them later ([TriggerHandler.java L260–L290][fg-waiting]). When several trigger at once, MTG rule 603.3b applies: each player in turn order puts theirs on the stack **in an order that player chooses**, through a controller callback ([MagicStack.java L898–L939][fg-apnap]). Deaths are state-based actions. `checkStateEffects` runs up to nine passes, re-checking static abilities each time ([GameAction.java L1424–L1441][fg-sba]). The main loop runs it before every priority prompt ([PhaseHandler.java L1162–L1192][fg-mainloop]).

> **Why this matters for Pantheon.** Hearthstone's death rule ("finish the current phase, then process all deaths in play order, repeat until stable") is cheap to build either way. SabberStone and Fireplace each do it in a few dozen lines. What matters is that it lives **inside `ApplyMove`**: Runner glue must never call "process deaths". Two notes for later design work. (a) An explicit queue (SabberStone) is easier to clone and to pause mid-way than Python-style recursion (Fireplace), because the queue is data. (b) Ordering by a stored `PlayOrder` number avoids relying on delegate subscription order, which a clone has to rebuild.

---

## 4. Mid-resolution player choices

**The question:** Targets, Discover, Choose One, mulligan: how does the engine pause for a player decision and resume?

This is the question where the engines split most sharply. There are two families:

| | **"Pause and return"** (decision becomes the next move) | **"Call out and block"** (engine calls the player synchronously) |
|---|---|---|
| Engines | **SabberStone**, **Fireplace** | **Forge**, **Metastone** |
| How | Store a pending `Choice` on the player and return. The next input is a "choose" move. | Deep inside the rules, call `controller.chooseX(...)` and wait for the answer. |
| Continuation stored as | SabberStone: **data** (a task object + entity ids). Fireplace: **Python closures**. | The call stack itself (the engine thread is parked). |

**Targets and Choose One are not mid-resolution in Hearthstone.** Both Hearthstone engines take them **up front, as part of the play move**. SabberStone's `PlayCardTask` carries `Target`, `ZonePosition` and a `chooseOne` index ([PlayCardTask.cs L61–L79][sb-playcardtask]). Fireplace's `play(target, index, choose)` takes the same three ([card.py L539][fp-play]). Forge does the same at cast time: MTG requires targets and modes (`Charm`, MTG's Choose One) to be picked when a spell goes on the stack ([PlayerController.java L146, L315][fg-pc-targets]).

**SabberStone (pause, continuation as data).** `DiscoverTask` rolls three options with the game RNG and calls `CreateChoiceCards`, passing the follow-up task (`_taskTodo`). That task is stored on the controller's `Choice` as `AfterChooseTask` ([DiscoverTask.cs L134–L196][sb-discover], [Choice.cs L105][sb-choice-after]). While a choice is pending, `Controller.Options()` returns **only** `ChooseTask`s, one per option. For a mulligan it returns every subset of the hand ([Controller.cs L338–L359][sb-options]). Processing the `ChooseTask` runs `ChoicePick`, which validates the pick, then runs `AfterChooseTask` with the picked card, then moves to `NextChoice` if any ([Choose.cs L29–L235][sb-choicepick]). The choice has a copy constructor and is cloned along with its controller ([Choice.cs L53][sb-choice-copy], [Controller.cs L222][sb-choice-clone]), so an AI can clone a game with a choice pending. The mulligan uses the same mechanism: `BeginMulligan` creates a MULLIGAN choice for **both players at once** ([Game.cs L700–L716][sb-mulligan]).

**Fireplace (pause, continuation as closures).** `Discover.do` sets `player.choice = self` and moves the action's callbacks into `_callback`. Later, `choose(card)` clears the choice and runs those callbacks with the picked card ([actions.py L1152–L1205][fp-discover]). Any targeted action that tries to run while its controller has a pending choice is deferred: it captures `lambda: self._trigger(i, source)` in `choice_callback` and returns ([actions.py L679–L681][fp-choicecallback]). Mulligan is again simultaneous: both players hold a `MulliganChoice`, and the game continues only when the second one finishes ([actions.py L417–L452][fp-mulligan]). The bundled random bot shows the driver side. It loops "while `player.choice`, pick something", then takes a normal action ([utils.py L216–L222][fp-playturn]).

**Forge (call out and block).** `PlayerController` is an abstract class with **113 abstract methods**: `chooseTargetsFor`, `chooseModeForAbility`, `orderAndPlaySimultaneousSa`, `mulliganKeepHand`, `chooseSpellAbilityToPlay`, and many more ([PlayerController.java L51–L318][fg-pc]). The rules call them synchronously from wherever the decision arises. The engine's own main loop calls `chooseSpellAbilityToPlay()` for the priority player ([PhaseHandler.java L1192][fg-mainloop]). The human implementation **parks the game thread** until the GUI answers (`defaultInput.showAndWait()`, [PlayerControllerHuman.java L1799][fg-human-wait]).

**Metastone (call out and block, small interface).** `IBehaviour` has only `mulligan(...)` and `requestAction(context, player, validActions)` ([IBehaviour.java][ms-ibehaviour]). That is close to Pantheon's `IAgent`. But the engine calls `requestAction` from inside rule code too. Example: battlecry targets are requested in the middle of playing a minion ([GameLogic.java L1697][ms-battlecry]), and Discover options are requested inside the spell ([SpellUtils.java L113][ms-discover]).

> **Why this matters for Pantheon.** Pantheon's synchronous `ApplyMove` cannot "call out and block". There is nothing to await inside Core (decision 4). So Pantheon **must** use the SabberStone pattern: *a pending decision is part of `GameState`, `GetValidMoves` returns only the answers to it, and the answer arrives as an ordinary `Move`*. Concretely, the `Move` set needs `Choose(optionId)` and `Mulligan(cardIds[])` next to `PlayCard(cardId, targetId?, position?, chooseOneIndex?)`, `Attack`, `EndTurn`. Store the continuation as **data** (SabberStone's `AfterChooseTask` + id lists), **not** as C# lambdas (Fireplace's closures). Lambdas capture object references that `Clone()` cannot remap. Forge shows the other path's cost: once the engine asks the player through callbacks, the agent interface grows to 113 methods. With every decision as a `Move`, Pantheon's single `DecideAsync(view) → Move` covers everything.

---

## 5. Card ability authoring

**The question:** Are abilities code or data? What is the vocabulary of effects, selectors and conditions?

| | Card stats/text | Card behaviour | Format |
|---|---|---|---|
| **SabberStone** | Blizzard's `CardDefs.xml`, embedded | C# object initialisers: a `Power` with `PowerTask`, `Aura`, `Trigger`, `Enchant` | Code that reads like data (C# DSL) |
| **Fireplace** | Blizzard's `CardDefs.xml` | A Python class per card id, with attributes such as `play = …`, `deathrattle = …`, `events = …`, `update = Refresh(…)` | Python DSL with a composable selector algebra |
| **Forge** | The script file itself | Plain-text script lines: `A:` (ability), `T:` (trigger), `S:` (static), `SVar:` (sub-ability), each a list of `Key$ Value` pairs | **Data**, parsed at runtime into objects |
| **Metastone** | JSON | JSON: `battlecry`, `spell`, `aura`, `trigger` objects naming a Java class plus parameters | **Data** (JSON) |

The same card, Elven Archer ("Battlecry: deal 1 damage"), in three engines:

```csharp
// SabberStone — CoreCardsGen.cs
cards.Add("CS2_189", new CardDef(new Dictionary<PlayReq, int>() { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_NONSELF_TARGET, 0 } }, new Power
{
    PowerTask = new DamageTask(1, EntityType.TARGET)
}));
```

```python
# Fireplace — cards/classic/neutral_common.py
class CS2_189:
    requirements = {PlayReq.REQ_NONSELF_TARGET: 0, PlayReq.REQ_TARGET_IF_AVAILABLE: 0}
    play = Hit(TARGET, 1)
```

```json
// Metastone — minion_elven_archer.json (excerpt)
"battlecry": { "targetSelection": "ANY", "spell": { "class": "DamageSpell", "value": 1 } }
```

Sources: [CoreCardsGen.cs L2404–L2421][sb-elven], [neutral_common.py L142–L146][fp-elven], [minion_elven_archer.json][ms-elven]. An aura card, Raid Leader ("your other minions have +1 Attack"), is just as short in each: SabberStone `Aura = new Aura(AuraType.BOARD_EXCEPT_SOURCE, "CS2_122e")` ([CoreCardsGen.cs L2195–L2206][sb-raidleader]); Fireplace `update = Refresh(FRIENDLY_MINIONS - SELF, buff="CS2_122e")` ([neutral_common.py L8–L14][fp-raidleader]); Metastone `"aura": { "class": "BuffAura", "target": "OTHER_FRIENDLY_MINIONS", "attackBonus": 1 }` ([minion_raid_leader.json][ms-raidleader]). The MTG equivalent, Glorious Anthem, is one script line: `S:Mode$ Continuous | Affected$ Creature.YouCtrl | AddPower$ 1 | AddToughness$ 1` ([glorious_anthem.txt][fg-anthem]).

**The shared vocabulary.** Under different syntax, all four engines build cards from the same five kinds of building block:

1. **Effects** (verbs): deal damage, heal, draw, summon, give buff, destroy, silence, discover. SabberStone has about 100 `SimpleTask` classes ([`Tasks/SimpleTasks/`][sb-simpletasks]). Forge lists over 200 effect types in `ApiType` ([ApiType.java][fg-apitype]).
2. **Selectors** (who is affected): `TARGET`, `SOURCE`, `HERO`, `OP_HERO`, `MINIONS`, `ENEMIES`, `ALL_NOSOURCE`, … (SabberStone's `EntityType` enum, [IncludeTask.cs L27][sb-entitytype]). Fireplace makes them **composable sets**: `FRIENDLY + MINION`, `FRIENDLY_MINIONS - SELF`, `RANDOM_ENEMY_CHARACTER`, `ADJACENT(SELF)` ([selector.py L289–L436][fp-selectors]). Forge uses filter strings such as `Creature.YouCtrl`.
3. **Conditions** (predicates): SabberStone's `SelfCondition` has about 125 ready-made checks and check factories, such as `IsRace(BEAST)` or `IsDead` ([SelfCondition.cs][sb-selfcond]).
4. **Triggers** = event type + source filter + optional condition + effect. SabberStone's Starving Buzzard is `Trigger(TriggerType.SUMMON) { TriggerSource = MINIONS_EXCEPT_SELF, Condition = IsRace(BEAST), SingleTask = new DrawTask() }` ([CoreCardsGen.cs L476–L484][sb-buzzard]). Fireplace writes `events = Death(FRIENDLY + MINION).on(Draw(CONTROLLER))` ([neutral_common.py L93–L96][fp-cultmaster]). Forge writes `T:Mode$ ChangesZone | Origin$ Battlefield | Destination$ Graveyard | …` ([blood_artist.txt][fg-bloodartist]).
5. **Composition**: run a sequence (`ComplexTask.Create(...)`, Forge `SubAbility$`), pick modes (Forge `Charm`, [cryptic_command.txt][fg-cryptic]), and pass results along (SabberStone's per-task `TaskStack`, Fireplace's `.then(...)` callbacks).

> **Why this matters for Pantheon.** [`design.md`](../design.md) wants data-driven cards. Forge and Metastone prove that a **pure-data** format scales: Forge runs all of MTG this way. But in both, the data only names **code-defined building blocks** (a `DamageSpell` class, a `DealDamage` effect). Data never contains logic. The vocabulary to design is the five lists above. A good first slice for Pantheon: a handful of effects (`Damage`, `Heal`, `Draw`, `Summon`, `Buff`), a selector enum like SabberStone's `EntityType`, and triggers as `(event, selector, condition, effect)`. Fireplace's composable selectors are the nicer API if the list grows.

---

## 6. AI / simulation

**The question:** How is state cloned, and how is hidden information handled?

SabberStone's side is covered in [`state-architecture.md` §1.3–1.4](state-architecture.md): a hand-written deep `Clone()` over a flat tag store, cheap enough for beam search, plus the `POGame` wrapper that swaps the opponent's hand and deck for placeholder cards. The other three:

| | Clone / copy | Hidden information for the AI |
|---|---|---|
| **SabberStone** | Hand-written deep copy, built to be cheap (see state-architecture.md). Pending `Choice` is copied too. | `POGame` replaces unseen opponent cards with placeholders. |
| **Fireplace** | **None.** No clone, deepcopy or snapshot in the engine. The only bot is a random player. | None needed (no search). |
| **Forge** | `GameCopier` builds a fresh `Game` and **rebuilds every card from its card script**. Separately, a reflective snapshot/restore (`GameMemento`) powers undo. | Copies **everything**, then `HiddenInfoShuffler` re-deals the cards the AI cannot see (determinization), using a caller-supplied seeded `Random`. |
| **Metastone** | Hand-written `GameContext.clone()` (logic, players, triggers, cost modifiers). | I found no hidden-info step in `GameContext.clone()`; it copies both players as they are. |

**Fireplace.** A search of the core modules (`game`, `card`, `entity`, `actions`, `player`, `managers`, `dsl/`) for `deepcopy`, `copy.copy`, `clone` and `pickle` finds them only in the DSL's random-card picker, never for the game. The bundled `play_turn` bot just picks random legal actions using the game RNG ([utils.py L216–L240][fp-playturn]). The closures stored for pending choices (§4) would also block a generic copy.

**Forge.** `GameCopier.makeCopy` creates a new `Game` and maps every player and card across ([GameCopier.java L95][fg-makecopy]). For each card it calls `CardFactory.getCard(paperCard, …)`, which re-parses the card from its script, and a code comment notes this is "very expensive and accounts for the vast majority of GameCopier execution time" ([GameCopier.java L425–L436][fg-copier-cost]). The stack is not copied by default (`COPY_STACK = false`, [GameSimulator.java L20][fg-copystack]). There is a hidden-info pruning switch, but it is off (`PRUNE_HIDDEN_INFO = false`, [GameCopier.java L404][fg-prune]). Instead, `HiddenInfoShuffler` pools the hand and library cards the AI may not see, shuffles them, and deals them back into the same slots. Its doc comment says that without this the AI would effectively know the opponent's hand and the library order ([HiddenInfoShuffler.java L18–L38][fg-shuffler]). Because decisions are callbacks (§4), the AI explores alternatives by keeping a list of "choice points" and re-running the simulation with a different answer at each point ([SpellAbilityChoicesIterator.java L15–L27][fg-choicepoints]). Undo is a separate mechanism: `GameMemento` takes a reflective snapshot of the live object graph and can restore it ([GameMemento.java L13][fg-memento]).

**Metastone.** `GameContext.clone()` copies logic, both players, temp cards, the trigger manager and cost modifiers ([GameContext.java L84–L105][ms-clone]).

> **Why this matters for Pantheon.** Three lessons. (1) **Clone cost is set by the data layout, not the algorithm.** SabberStone's flat tag arrays copy fast. Forge's rebuild-from-script is its main cost. So Pantheon's `CardInstance` should reference its immutable `CardDefinition` and copy only runtime fields. (2) **A straight clone leaks hidden information.** If the AI clones the real `GameState`, deck order and the seeded RNG come with it, so the AI can foresee draws. Both serious precedents add a separate step: SabberStone's `POGame` placeholders, Forge's `HiddenInfoShuffler` re-deal. Pantheon needs the same: something like `GameState.CloneFor(playerId, rng)` that determinizes. (3) **Every pending decision must be cloneable**, which §4's data-not-closures rule already ensures.

---

## 7. Module / assembly layout

| Engine | Layout | Rules core depends on |
|---|---|---|
| **SabberStone** | `SabberStoneCore` (one .NET library: rules, all card definitions, Kettle protocol types, a text visualizer) + `SabberStoneCoreTest` + `core-extensions/` (AI, GUI, gRPC). Detail in [state-architecture.md §1.1](state-architecture.md). | .NET only. Card data is an embedded `CardDefs.xml` ([CardLoader.cs L33–L37][sb-cardloader]). |
| **Fireplace** | One Python package `fireplace/` (rules, `dsl/`, `cards/`) + a separate `kettle/` socket server that wraps the engine + `tests/`. | The `python-hearthstone` library, for enums and `CardDefs.xml` ([entity.py L3][fp-hsimport]). |
| **Forge** | Maven multi-module: `forge-core` (utilities, card database, RNG) → `forge-game` (rules) → `forge-ai` → `forge-gui` (shared UI logic, human controller, **card scripts** in `res/cardsfolder`) → front-ends `forge-gui-desktop`, `-mobile`, `-android`, `-ios` ([pom.xml L65–L75][fg-pom]). | `forge-game` depends only on `forge-core` ([forge-game/pom.xml][fg-gamepom]). |
| **Metastone** | Gradle: `game` (engine), `cards` (JSON card files), `app` (JavaFX UI), `shared` ([repo tree][ms-tree]). | — |

Two patterns repeat. The rules core never depends on UI or AI: Forge enforces it with Maven modules, and SabberStone keeps AI and GUI in `core-extensions`. And the network server is a **thin wrapper around the unchanged engine**: Fireplace's `kettle/` registers itself as an observer on the game and turns its callbacks into packets ([kettle.py][fp-kettle]). SabberStone keeps its Kettle protocol types inside the core next to `PowerHistory`, with the gRPC server as an extension.

> **Why this matters for Pantheon.** Pantheon's planned split (`Pantheon.Core` DLL with no engine references, shared by the Unity client and a headless `Pantheon.Server`) is the Forge `forge-game` → `forge-core` shape and the SabberStone `core-extensions` shape. No precedent puts rules in the UI layer, and none needs to.

---

## Tension with Pantheon's locked decisions

| # | Locked decision | Verdict | Evidence | What it implies |
|---|---|---|---|---|
| 1 | **Mutable entities + `Clone()`** | **Supported, with a caveat.** | All four engines mutate in place. SabberStone and Metastone deep-clone ([state-architecture.md §1.3](state-architecture.md), [GameContext.java L84][ms-clone]). | The caveat: *derived* stats (auras) are recomputed or kept in a separate layer in every engine (§2). Also, anything that must survive `Clone()` should hold **entity ids, not references** (§1, [Choice.cs L84, L111][sb-choice-fields]). Forge's rebuild-from-script copy shows what happens when clone cost is ignored ([GameCopier.java L434][fg-copier-cost]). |
| 2 | **One seeded PRNG on `GameState`** | **Supported. Precedents show why it must be strict.** | SabberStone: one cloneable PRNG on `Game` (state-architecture.md §1.3). Fireplace: a seeded `Random(seed)` on the game ([game.py L41][fp-seed]), **but** `Discover` calls `random_class()` with no game, which silently uses Python's global RNG ([actions.py L1170][fp-leak], [utils.py L126–L131][fp-randomclass]). Forge: one process-wide, thread-local RNG, seeded from `SecureRandom` unless overridden ([MyRandom.java L67, L111–L120][fg-myrandom]). Its undo system has to count draws and forbid undo after any random draw ([UndoManager.java L178][fg-undo-barrier]). Metastone: unseeded `ThreadLocalRandom` everywhere ([GameLogic.java L1465][ms-random]). | Fireplace's leak shows how ad hoc randomness creeps in through a helper with a "default" fallback. **Don't give helpers an optional RNG parameter.** Make `GameState`'s RNG the only reachable source. For AI clones, give each rollout a fresh seed *on purpose* (§6), never by accident. |
| 3 | **Fat semantic events in a `List<GameEvent>`; no C# `event` keyword** | **Supported. Two complications.** | Forge emits semantic records with payload (`GameEventCardDamaged(card, source, amount, type)`) on an event bus ([GameEventCardDamaged.java L5][fg-evdamaged], [Game.java L98, L1143][fg-eventbus]). SabberStone's `PowerHistory` wraps tag-change deltas in typed blocks (POWER / TRIGGER / DEATHS, with source and target) ([TaskProcessor.cs L220–L255][sb-taskqueue], [Game.cs L1086–L1100][sb-graveyard]). That block-plus-deltas shape is close to "fat events". | (a) **Nesting.** Both Hearthstone engines record *which action caused which* (block start/end, Fireplace's `action_start`/`action_end` observers, [managers.py][fp-newentity]). A flat list loses that. Give each `GameEvent` a cause or parent id, or add block-start/end events, so the view can tell "deathrattle caused by the attack". (b) SabberStone uses C# `event`s **internally** for trigger dispatch ([TriggerManager.cs L23][sb-triggermgr]), so every clone must re-subscribe handlers ([Trigger.cs L119][sb-trigger-activate], `cloning` flag). Pantheon's ban avoids that. Keep triggers as plain data scanned in play order, as Fireplace does. |
| 4 | **Synchronous Core step function `StartGame` / `GetValidMoves` / `ApplyMove`** | **Contradicted by Forge and Metastone; supported by SabberStone.** | Forge and Metastone ask the player from deep inside rules and block ([PlayerControllerHuman.java L1799][fg-human-wait], [GameLogic.java L1697][ms-battlecry]). That cannot work in a synchronous Core. SabberStone shows the compatible pattern: pending `Choice` in state, `Options()` returns only choice answers, `ChooseTask` resumes ([Controller.cs L338–L359][sb-options], [Choose.cs L205][sb-choicepick]). | Design `Move` and `GameState` for this **before** the first Discover/mulligan card: a `PendingDecision` in `GameState` (cloned), `GetValidMoves` limited to its answers, and continuations as data. Targets and Choose One go in the `PlayCard` move up front, as both Hearthstone engines do (§4). |
| 5 | **Async Runner + `IAgent`; exactly one agent acts per turn (the current player)** | **Complicated by mulligan.** | Both Hearthstone engines open the mulligan for **both players at once** and continue only when both answer ([Game.cs L700–L716][sb-mulligan], [actions.py L417–L452][fp-mulligan]). Also, a pending choice can belong to a player other than the one whose turn it is. | Have Core say **who must decide now** (for example `GameState.DecidingPlayer`) instead of the Runner assuming "current player". Mulligan can stay "one agent at a time" by asking players one after the other. That is safe because each player's mulligan is hidden from the other (filtered events). Metastone's `IBehaviour` ([IBehaviour.java][ms-ibehaviour]) confirms the small `requestAction(validActions)` interface works, if no decision is hidden inside rule code. |
| 6 | **Runner is dumb glue; Core owns the rules** | **Supported.** | Forge and Metastone put the main loop inside the engine ([PhaseHandler.java L1162][fg-mainloop], [GameContext.java L490][ms-requestaction]) because their decisions are callbacks. SabberStone leaves the loop to the caller: `Process(task)` + `Options()` ([Game.cs L481][sb-process]). | Fine as long as death, aura and trigger processing all happen inside `ApplyMove` (§3). Then the Runner's loop really is "ask the deciding agent → `ApplyMove` → dispatch → repeat". |
| 7 | **Authoritative server; per-recipient filtered events** | **Supported. One complication.** | Fireplace's `kettle` wraps the engine as a socket server ([kettle.py][fp-kettle]). Hidden info is filtered per viewer: Fireplace has `dump()` vs `dump_hidden()` per entity ([card.py L90–L93][fp-dumphidden]); Forge has a zone-based `CardView.canBeShownTo(viewer)` ([CardView.java L629][fg-canbeshown]). | When a hidden card becomes visible (drawn card shown to its owner, or a card played from hand), the client needs its full data **at that moment**. Kettle has separate `full_entity` and `show_entity` messages for this ([kettle.py L260–L270][fp-kettle-show]). Pantheon's filtered events will need a "reveal" event (or per-recipient payload) that carries a card's identity the first time a recipient may see it. |

---

## Bottom line

- **Pantheon's locked decisions hold up against all four engines.** Nothing in them argues for immutable state, a lockstep server, or rules in the view.
- **The one real gap is mid-resolution choices.** Forge and Metastone block on player callbacks, which a synchronous Core cannot do. Copy SabberStone: a `PendingDecision` stored in `GameState`, `GetValidMoves` returning only its answers, a `Choose` / `Mulligan` `Move` to resume, and the continuation stored as **data, never lambdas**.
- **Targets and Choose One belong in the `PlayCard` move**, picked up front, as both Hearthstone engines do. Only Discover-style effects and mulligan need the pending-decision mechanism.
- **The Runner should ask Core who decides next** instead of assuming the current player. Mulligan is simultaneous in both Hearthstone engines. Asking players one at a time keeps "one agent at a time" and is safe.
- **Keep derived stats derived.** Store base + damage + modifier list on the mutable `CardInstance`, and recompute the aura layer after every action (Fireplace's mark-and-sweep is a few dozen lines). Silence and aura removal then come almost free.
- **Use ids, not references, in any state that must survive `Clone()`**: pending choices, aura targets, trigger bookkeeping.
- **A clone for AI is not a view.** Cloning the real state hands the AI the deck order and the RNG. Plan a determinizing clone (reshuffle unseen cards, reseed), like SabberStone's `POGame` and Forge's `HiddenInfoShuffler`.
- **Make the seeded RNG the only reachable random source.** Fireplace's seeded engine still leaks through one helper with a global fallback, and Forge's global RNG forced extra machinery into its undo system.
- **Add causality to the event list.** Give each `GameEvent` a parent/cause id or block markers, plus a "reveal" event for hidden cards, so the view can animate chains and the server can filter per recipient.
- **For data-driven cards, design the vocabulary, not the file format:** effects, selectors, conditions, triggers (event + selector + condition + effect), composition. Forge (all of MTG in text scripts) and Metastone (JSON) prove a pure-data format scales, as long as the data only names code-defined building blocks.

---

## Verification notes / gaps

- **SabberStone's canonical repo is gone.** `HearthSim/SabberStone` returned 404 on 2026-11-24, so the `HearthSim/...` links in `state-architecture.md` are now dead. This doc reads the same code from a fork pinned at upstream's last commit `1db6e27`. Consider updating the older doc's links.
- **SabberStone Discover vs the rest of the queue.** I confirmed that `DiscoverTask` only creates the `Choice` and stores the follow-up task, and that `Options()` offers only choice answers while one is pending. I did not trace every edge case of how tasks queued *after* a Discover in the same effect interleave with the pick (`EnqueuePendingTask` / `ResumePendingTasks`, [TaskProcessor.cs][sb-taskqueue]).
- **Fireplace RNG leak scope.** The global-RNG fallback in `Discover` only runs when both the hero's class and the source card's class are neutral, so it is rare. It is still a real path to non-reproducible games. I did not run Fireplace to show a replay diverging.
- **Forge network play** was not studied. I only read the shared human controller, not the networking layer in `forge-gui`.
- **Counts are approximate.** "About 100" SabberStone simple tasks and "over 200" Forge `ApiType` entries come from counting files and enum lines at the pinned commits.
- **Metastone** has been inactive since 2018. It is cited only for its JSON card format, its `IBehaviour` interface, its clone, and its RNG.

---

## Sources

All links are pinned to the commits listed at the top.

<!-- SabberStone (MokonaNico/SabberStone @ 1db6e2711a13b9061a5de2cd6fc4a00827203d6b, = upstream HearthSim master tip) -->
[sb-fromcard]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Model/Entities/Entity.cs#L251-L314
[sb-entity-card]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Model/Entities/Entity.cs#L123
[sb-entity-idx]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Model/Entities/Entity.cs#L208-L230
[sb-nextid]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Model/Game.cs#L223
[sb-process]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Model/Game.cs#L481
[sb-mulligan]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Model/Game.cs#L700-L716
[sb-graveyard]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Model/Game.cs#L1086-L1120
[sb-auraupdate]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Model/Game.cs#L1164-L1169
[sb-deathproc]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Model/Game.cs#L1191-L1220
[sb-removezone]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Actions/PlayCard.cs#L55
[sb-boardadd]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Actions/PlayCard.cs#L247
[sb-playminion]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Actions/PlayCard.cs#L238-L319
[sb-taskqueue]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Model/TaskProcessor.cs#L70-L255
[sb-effect-apply]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Enchants/Effect.cs#L73-L94
[sb-effect-aura]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Enchants/Effect.cs#L162
[sb-effect-remove]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Enchants/Effect.cs#L235-L257
[sb-effect-aura-remove]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Enchants/Effect.cs#L259
[sb-aura-update]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Auras/Aura.cs#L235-L270
[sb-aura-remove]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Auras/Aura.cs#L275
[sb-aura-removeinternal]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Auras/Aura.cs#L448-L483
[sb-silence]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Model/Entities/Minion.cs#L55-L115
[sb-triggermgr]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Model/TriggerManager.cs#L21-L51
[sb-trigger-activate]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Triggers/Trigger.cs#L119
[sb-trigger-sub]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Triggers/Trigger.cs#L141-L146
[sb-trigger-process]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Triggers/Trigger.cs#L297-L325
[sb-choice-copy]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Model/Choice.cs#L53
[sb-choice-fields]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Model/Choice.cs#L84-L111
[sb-choice-after]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Model/Choice.cs#L105
[sb-choice-clone]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Model/Entities/Controller.cs#L222
[sb-options]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Model/Entities/Controller.cs#L338-L359
[sb-choicepick]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Actions/Choose.cs#L29-L235
[sb-discover]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Tasks/SimpleTasks/DiscoverTask.cs#L134-L196
[sb-playcardtask]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Tasks/PlayerTasks/PlayCardTask.cs#L61-L79
[sb-entitytype]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Tasks/SimpleTasks/IncludeTask.cs#L27
[sb-simpletasks]: https://github.com/MokonaNico/SabberStone/tree/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Tasks/SimpleTasks
[sb-selfcond]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Conditions/SelfCondition.cs
[sb-elven]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/CardSets/Standard/CoreCardsGen.cs#L2404-L2421
[sb-raidleader]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/CardSets/Standard/CoreCardsGen.cs#L2195-L2206
[sb-buzzard]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/CardSets/Standard/CoreCardsGen.cs#L476-L484
[sb-cardloader]: https://github.com/MokonaNico/SabberStone/blob/1db6e2711a13b9061a5de2cd6fc4a00827203d6b/SabberStoneCore/src/Loader/CardLoader.cs#L33-L37

<!-- Fireplace (jleclanche/fireplace @ 47a2572a000db66645bb74a425a090d51f1004fa) -->
[fp-hsimport]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/entity.py#L3
[fp-uuid]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/entity.py#L18
[fp-getattr]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/entity.py#L113-L121
[fp-intprop]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/entity.py#L159-L169
[fp-newentity]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/managers.py#L56-L71
[fp-playercard]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/player.py#L253-L264
[fp-cardfactory]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/card.py#L35-L53
[fp-dumphidden]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/card.py#L90-L93
[fp-setzone]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/card.py#L201-L233
[fp-play]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/card.py#L539
[fp-ignorescripts]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/card.py#L1162-L1163
[fp-ench-zone]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/card.py#L1499-L1520
[fp-aura]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/aura.py#L30-L81
[fp-seed]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/game.py#L41
[fp-board]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/game.py#L77-L80
[fp-actionblock]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/game.py#L132-L172
[fp-stackempty]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/game.py#L140-L143
[fp-deaths]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/game.py#L186-L191
[fp-refresh]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/game.py#L275-L297
[fp-broadcast]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/actions.py#L164-L176
[fp-death]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/actions.py#L332-L374
[fp-mulligan]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/actions.py#L417-L452
[fp-choicecallback]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/actions.py#L679-L681
[fp-discover]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/actions.py#L1152-L1205
[fp-leak]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/actions.py#L1170
[fp-silence]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/actions.py#L1634-L1652
[fp-randomclass]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/utils.py#L126-L131
[fp-playturn]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/utils.py#L216-L240
[fp-selectors]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/dsl/selector.py#L289-L436
[fp-raidleader]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/cards/classic/neutral_common.py#L8-L14
[fp-cultmaster]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/cards/classic/neutral_common.py#L93-L96
[fp-elven]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/fireplace/cards/classic/neutral_common.py#L142-L146
[fp-kettle]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/kettle/kettle.py
[fp-kettle-show]: https://github.com/jleclanche/fireplace/blob/47a2572a000db66645bb74a425a090d51f1004fa/kettle/kettle.py#L260-L280

<!-- Forge (Card-Forge/forge @ 3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4) -->
[fg-pom]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/pom.xml#L65-L75
[fg-gamepom]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-game/pom.xml
[fg-changezone]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-game/src/main/java/forge/game/GameAction.java#L169-L207
[fg-checkstatic]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-game/src/main/java/forge/game/GameAction.java#L1100-L1175
[fg-sba]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-game/src/main/java/forge/game/GameAction.java#L1424-L1441
[fg-eventbus]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-game/src/main/java/forge/game/Game.java#L98
[fg-nextid]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-game/src/main/java/forge/game/Game.java#L1168-L1171
[fg-copyid]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-game/src/main/java/forge/game/card/CardCopyService.java#L117-L125
[fg-pttables]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-game/src/main/java/forge/game/card/Card.java#L286-L289
[fg-netpower]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-game/src/main/java/forge/game/card/Card.java#L4425-L4450
[fg-eqts]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-game/src/main/java/forge/game/card/Card.java#L6927-L6929
[fg-canbeshown]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-game/src/main/java/forge/game/card/CardView.java#L629-L660
[fg-evdamaged]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-game/src/main/java/forge/game/event/GameEventCardDamaged.java#L5
[fg-mainloop]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-game/src/main/java/forge/game/phase/PhaseHandler.java#L1162-L1192
[fg-pc]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-game/src/main/java/forge/game/player/PlayerController.java#L51-L318
[fg-pc-targets]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-game/src/main/java/forge/game/player/PlayerController.java#L146-L315
[fg-pump]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-game/src/main/java/forge/game/ability/effects/PumpEffect.java#L61-L122
[fg-apitype]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-game/src/main/java/forge/game/ability/ApiType.java
[fg-removeall]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-game/src/main/java/forge/game/staticability/StaticAbilityContinuous.java#L352
[fg-waiting]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-game/src/main/java/forge/game/trigger/TriggerHandler.java#L260-L290
[fg-resolve]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-game/src/main/java/forge/game/zone/MagicStack.java#L632
[fg-apnap]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-game/src/main/java/forge/game/zone/MagicStack.java#L898-L939
[fg-memento]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-game/src/main/java/forge/game/undo/GameMemento.java#L13
[fg-undo-barrier]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-game/src/main/java/forge/game/undo/UndoManager.java#L178
[fg-myrandom]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-core/src/main/java/forge/util/MyRandom.java#L64-L120
[fg-makecopy]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-ai/src/main/java/forge/ai/simulation/GameCopier.java#L95
[fg-prune]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-ai/src/main/java/forge/ai/simulation/GameCopier.java#L404
[fg-copier-cost]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-ai/src/main/java/forge/ai/simulation/GameCopier.java#L425-L436
[fg-copystack]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-ai/src/main/java/forge/ai/simulation/GameSimulator.java#L20
[fg-shuffler]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-ai/src/main/java/forge/ai/simulation/HiddenInfoShuffler.java#L18-L38
[fg-choicepoints]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-ai/src/main/java/forge/ai/simulation/SpellAbilityChoicesIterator.java#L15-L27
[fg-human-wait]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-gui/src/main/java/forge/player/PlayerControllerHuman.java#L1799
[fg-anthem]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-gui/res/cardsfolder/g/glorious_anthem.txt
[fg-bloodartist]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-gui/res/cardsfolder/b/blood_artist.txt
[fg-cryptic]: https://github.com/Card-Forge/forge/blob/3e1fa4ed132edd5a782ac2ba044d938c2b08ebc4/forge-gui/res/cardsfolder/c/cryptic_command.txt

<!-- Metastone (demilich1/metastone @ bab2c6a143705cd0c6ce3d77078d8a1063ca2882) -->
[ms-tree]: https://github.com/demilich1/metastone/tree/bab2c6a143705cd0c6ce3d77078d8a1063ca2882
[ms-clone]: https://github.com/demilich1/metastone/blob/bab2c6a143705cd0c6ce3d77078d8a1063ca2882/game/src/main/java/net/demilich/metastone/game/GameContext.java#L84-L105
[ms-requestaction]: https://github.com/demilich1/metastone/blob/bab2c6a143705cd0c6ce3d77078d8a1063ca2882/game/src/main/java/net/demilich/metastone/game/GameContext.java#L490
[ms-ibehaviour]: https://github.com/demilich1/metastone/blob/bab2c6a143705cd0c6ce3d77078d8a1063ca2882/game/src/main/java/net/demilich/metastone/game/behaviour/IBehaviour.java
[ms-random]: https://github.com/demilich1/metastone/blob/bab2c6a143705cd0c6ce3d77078d8a1063ca2882/game/src/main/java/net/demilich/metastone/game/logic/GameLogic.java#L1465-L1467
[ms-battlecry]: https://github.com/demilich1/metastone/blob/bab2c6a143705cd0c6ce3d77078d8a1063ca2882/game/src/main/java/net/demilich/metastone/game/logic/GameLogic.java#L1697
[ms-discover]: https://github.com/demilich1/metastone/blob/bab2c6a143705cd0c6ce3d77078d8a1063ca2882/game/src/main/java/net/demilich/metastone/game/spells/SpellUtils.java#L113
[ms-elven]: https://github.com/demilich1/metastone/blob/bab2c6a143705cd0c6ce3d77078d8a1063ca2882/cards/src/main/resources/cards/basic/neutral/minion_elven_archer.json
[ms-raidleader]: https://github.com/demilich1/metastone/blob/bab2c6a143705cd0c6ce3d77078d8a1063ca2882/cards/src/main/resources/cards/basic/neutral/minion_raid_leader.json
