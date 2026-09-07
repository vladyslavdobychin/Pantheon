# State Architecture Research: Clone-based Search + Event Sourcing

> **What decision this feeds.** Pantheon is a headless C#/Unity Hearthstone-like combat engine. This document gathers primary-source evidence for the architectural direction we are considering: a **mutable game-state object that can be cheaply `Clone()`d** (so AI can "apply a candidate move to a copy, score it, throw it away"), combined with an **append-only event log + seeded/deterministic RNG** as the real source of truth (so the game becomes a pure function of `seed + ordered inputs`, giving us replay, netcode reconciliation, analytics, and per-player hidden-information redaction). The three topics below are the pillars of that decision: (1) how the reference engine SabberStone actually does mutable-state + Clone, (2) why every flavor of card-game AI needs that cheap copy operation, and (3) how event sourcing + deterministic RNG give us everything else. Every non-obvious claim is cited inline; a consolidated source list is at the end.

A note on how to read this: the recurring thread is one sentence — **"apply a move to a copy, score it, discard it."** SabberStone is the existence proof that this works for a Hearthstone-scale engine; game-tree search is *why* you need it; event sourcing is the complementary idea that makes the *authoritative* timeline reproducible instead of just fast to fork.

---

## Topic 1 — SabberStone architecture (the key reference)

**SabberStone** is an open-source Hearthstone simulator "written in C#" implementing roughly 98% of Standard cards, built as a black-box engine for AI research and licensed under the **GNU Affero GPLv3** ([repo README, HearthSim/SabberStone](https://github.com/HearthSim/SabberStone)). It is a *library*, not a runnable game — you drive it from code or from a bot harness ([SabberStone wiki: Getting Started](https://github.com/HearthSim/SabberStone/wiki/Getting-Started)). It came out of the academic **Hearthstone-AI Competition** (IEEE Conference on Games, 2018–2020), described in Dockhorn & Mostaghim, *"Introducing the Hearthstone-AI Competition"* ([arXiv:1906.04238](https://arxiv.org/abs/1906.04238)); active development largely stopped in 2019, which is where the "~98% of base cards" figure is usually quoted from ([competition retrospective, Kowalski & Miernik, arXiv:2305.11814](https://arxiv.org/html/2305.11814)).

> ⚠️ **Two repos, don't mix them up.** The canonical engine is [`HearthSim/SabberStone`](https://github.com/HearthSim/SabberStone). The **AI competition template** — the part with the ready-made `POGame`, `Simulate()`, and example bots (Greedy/BeamSearch/etc.) — lives in the separate fork [`ADockhorn/HearthstoneAICompetition`](https://github.com/ADockhorn/HearthstoneAICompetition). The base repo has a *smaller* `SabberStoneBasicAI` under `core-extensions/` with only score functions and a beam-search `OptionNode`. I cite both below and say which is which.

### 1.1 Project layout

Top-level projects in `HearthSim/SabberStone` (verified via the GitHub tree API):

| Project | What it is |
|---|---|
| **SabberStoneCore** | The engine. All simulation logic. This is the part Pantheon should study. |
| **SabberStoneCoreTest** | Unit tests, with auto-generated test stubs per card. |
| **SabberStoneGui** | A (deprecated) WinForms visualizer; also carries its own copy of the `Score/` heuristics. |
| **core-extensions/** | Add-ons: `SabberStoneBasicAI` (bots/scoring), Kettle (a gRPC client-server protocol), console runners. |
| **docs/** | Wiki assets, proto files, log samples. |

Inside `SabberStoneCore/src/` the important folders are `Model/` (the game state), `Tasks/` (the "moves"), `Actions/`, `Auras/`, `Enchants/`, `Triggers/`, `Conditions/`, `CardSets/`, `Enums/`, and `Splits/`. The state itself lives in `Model/`, which contains `Game.cs` plus subfolders `Entities/` and `Zones/`.

### 1.2 How game state is structured

The single most important type is **`Game`**. Its own class comment calls it *"The state machine which processes the given input and generates results ... THE MOST IMPORTANT type"* ([Game.cs](https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Model/Game.cs)). Key fields:

- **Two players** as `Controller` objects: `Controller Player1`, `Controller Player2`, plus a `Controller[] _players` array ([Game.cs](https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Model/Game.cs)).
- **A flat entity registry**: `EntityList IdEntityDic` — every entity (minion, spell, hero, weapon, enchantment) has an integer `Id` and lives in this one lookup ([Game.cs](https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Model/Game.cs)).
- **Global mechanic lists**: `List<IAura> Auras`, `List<Trigger> Triggers`, `List<Minion> DeadMinions`, one-turn effects, plus a **`TaskQueue`** (the engine executes "moves" as queued tasks) ([Game.cs](https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Model/Game.cs)).
- **Optional logging/history**: `Queue<LogEntry> Logs` and a `PowerHistory` (a stream of deltas — see Topic 3). Both are gated by config flags and are **off by default** because they cost memory/time.
- **RNG**: `Random` is a `Util.DeepCloneableRandom` (a seedable, cloneable PRNG — this is central and comes back in Topic 3) ([Game.cs, ctor](https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Model/Game.cs), [Utils.cs](https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Model/Utils.cs)).

**`Controller` *is* the "Player."** Its class comment: *"Instance that represents a player in SabberStone game instances"* ([Controller.cs](https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Model/Entities/Controller.cs)). A `Controller` owns the player's **zones** (declared as fields):

- `DeckZone`, `HandZone`, `BoardZone` (the minions in play), `GraveyardZone`, `SecretZone`, `SetasideZone` ([Controller.cs](https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Model/Entities/Controller.cs)),
- the `Hero` (which itself owns `HeroPower` and optional `Weapon`), a pending `Choice`, and mana/resource tags.

Everything visible or invisible in the game derives from a base **`Entity`**, described in its class comment as *"the base class of all data-holding/action-performing/visible or invisible objects ... defined as a collection of properties, called Tags"* ([Entity.cs](https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Model/Entities/Entity.cs)). The type hierarchy under `Entities/` is: `Entity` → `Playable` → `Character` → {`Minion`, `Hero`}, plus `Spell`, `Weapon`, `Enchantment`, `HeroPower`, and `Controller`.

The crucial performance decision is **how tags are stored**. An entity's stats/properties are *not* a `Dictionary<GameTag,int>`; they live in an **`EntityData`** whose backing store is a **single flat `int[] _buckets`** holding key/value pairs in alternating slots ([EntityData.cs](https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Model/Entities/EntityData.cs)). This choice exists specifically to make copying cheap (next section).

> Terminology map for Pantheon: SabberStone **Controller ≈ our Player**, **Entity/Minion ≈ our card instance on board**, **Zone ≈ our hand/board/deck collections**, **Tag ≈ our stat/attribute**, **PlayerTask ≈ our "move"/action**, **Game ≈ our whole battle state**.

### 1.3 The mutable-state + `Clone()` approach

SabberStone does **not** use immutable/persistent data structures. It uses one big mutable `Game` graph and, when search needs a "what if," it **deep-copies the entire thing**. The public entry point is a three-argument `Clone`:

```csharp
// SabberStoneCore/src/Model/Game.cs  (~line 1228)
public Game Clone(bool logging = false, bool resetRandomSeed = true, bool history = false)
{
    return new Game(this, logging, resetRandomSeed, history);
}
```

([Game.cs, `Clone`](https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Model/Game.cs)). It delegates to a **private copy constructor** (`private Game(Game game, …)`, ~line 389) that rebuilds the whole state graph:

- `IdEntityDic = new EntityList(game.IdEntityDic.Count);` — a fresh entity registry sized to match ([Game.cs ~L392](https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Model/Game.cs)).
- `Player1 = game.Player1.Clone(this); Player2 = game.Player2.Clone(this);` — each player deep-copied into the *new* game ([Game.cs ~L426](https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Model/Game.cs)).
- `Random = resetRandomSeed ? new Util.DeepCloneableRandom() : game.Random.Clone();` — see the RNG nuance below ([Game.cs ~L424](https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Model/Game.cs)).
- Fresh `Auras`, `Triggers`, one-turn-effect lists, and `TaskQueue`.

`Controller.Clone` (public entry ~L268, copy ctor `private Controller(in Game game, in Controller controller)` ~L207) is where the bulk of the work happens. Its comment states the contract plainly: *"Copied instance and all entities in its zones are deep copied to the target `Game` instance."* It clones, one by one:

```csharp
// SabberStoneCore/src/Model/Entities/Controller.cs  (copy ctor ~L207-236)
Hero            = (Hero)controller.Hero.Clone(this);
Hero.HeroPower  = (HeroPower)controller.Hero.HeroPower.Clone(this);
if (weapon)       Hero.Weapon = (Weapon)controller.Hero.Weapon.Clone(this);
Choice          = controller.Choice?.Clone(this);
SetasideZone    = controller.SetasideZone.Clone(this);
DeckZone        = controller.DeckZone.Clone(this);
HandZone        = controller.HandZone.Clone(this);
GraveyardZone   = controller.GraveyardZone.Clone(this);
SecretZone      = controller.SecretZone.Clone(this);
ControllerAuraEffects = controller.ControllerAuraEffects.Clone(this);
```

([Controller.cs](https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Model/Entities/Controller.cs)).

At the leaf level, cloning an entity copies its tag store by value. The base `Entity` copy constructor does `_data = new EntityData(entity._data);` ([Entity.cs](https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Model/Entities/Entity.cs)), and `EntityData`'s copy constructor allocates a fresh `int[]` and blits the old one across with `unsafe` pointer copy ([EntityData.cs](https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Model/Entities/EntityData.cs)):

```csharp
public unsafe EntityData(in EntityData entityData) {
    int len = entityData._buckets.Length;
    _buckets = new int[len];
    fixed (int* srcPtr = entityData._buckets, dstPtr = _buckets) { /* memcpy-style copy */ }
    ...
}
```

**Why deep-copy the whole world?** Because a "move" in Hearthstone can cascade arbitrarily (a spell triggers a deathrattle that summons a minion that triggers an aura…). Mutating a shared state and trying to *undo* it precisely is error-prone; forking a fresh independent copy and mutating *that* is simpler and bug-resistant. The whole design goal, in the authors' words, is a simulator that can "clone any board state ... and simulate on its own application" for AI ([Introducing the Hearthstone-AI Competition, arXiv:1906.04238](https://arxiv.org/pdf/1906.04238)).

**How expensive is it?** The architecture is *built to make clone cheap* — flat `int[]` tag stores copied with pointer blits, `EntityList` sized up front, deferred/optional logging and history. That is why AI bots can afford thousands of clones per turn. However, I could **not find a published, authoritative ns-per-clone or clones-per-second benchmark** in the repo, wiki, or the competition paper; the "it's fast" claim is architectural inference plus the fact that search bots clone heavily in practice. Treat any specific number as unverified. *(Noted as a gap.)*

> 🔑 **The RNG nuance you must not miss for Pantheon.** `Clone(resetRandomSeed: true)` is the **default**, meaning a clone gets a *brand-new* random stream, not a continuation of the parent's. That is deliberate for search: each rollout should explore a *different* random future, not replay the same coin-flips. When you instead want a faithful, reproducible fork (replay, debugging, deterministic tests), you pass `resetRandomSeed: false`, and `game.Random.Clone()` duplicates the PRNG's internal state so the sequence continues identically. This single boolean is the hinge between "fork for search" and "fork for replay" — Pantheon will want both modes for the same reason. (See Topic 3 for the PRNG itself.)

### 1.4 How Clone is used by the AI

**(a) In the competition framework — the textbook "clone, apply, score, discard" loop.** Each bot is handed a `POGame` (Partially-Observable Game — see hidden info below). `POGame.Simulate` takes a list of legal `PlayerTask`s and returns, for each, the *resulting* game — by cloning and processing on the copy:

```csharp
// ADockhorn/HearthstoneAICompetition .../PartialObservation/POGame.cs  (~L173)
public Dictionary<PlayerTask, POGame> Simulate(List<PlayerTask> tasksToSimulate) {
    var simulated = new Dictionary<PlayerTask, POGame>();
    foreach (PlayerTask task in tasksToSimulate) {
        ...
        try {
            Game clone = game.Clone();          // fork
            clone.Process(task);                // apply the candidate move
            simulated.Add(task, new POGame(clone, ...));   // keep the result
        } catch (Exception e) { ... simulated.Add(task, null); }   // discard on failure
    }
    return simulated;
}
```

([POGame.cs](https://github.com/ADockhorn/HearthstoneAICompetition/blob/master/core-extensions/SabberStoneBasicAI/src/PartialObservation/POGame.cs)). The **Greedy (one-ply) bot** then just scores each resulting state and takes the best:

```csharp
// .../AIAgents/Examples/GreedyAgent.cs
var validOpts = game.Simulate(player.Options()).Where(x => x.Value != null);
return validOpts.Any()
    ? validOpts.OrderBy(x => Score(x.Value, player.PlayerId)).Last().Key   // best-scoring move
    : player.Options().First(x => x.PlayerTaskType == PlayerTaskType.END_TURN);
```

([GreedyAgent.cs](https://github.com/ADockhorn/HearthstoneAICompetition/blob/master/core-extensions/SabberStoneBasicAI/src/AIAgents/Examples/GreedyAgent.cs)). The example set also includes `BeamSearchAgent`, `DynamicLookaheadAgent`, and `RandomAgent`, all in `.../AIAgents/Examples/`.

**(b) In the base repo — a beam-search game tree via `OptionNode`.** Every node *is* a cloned game with one task already applied:

```csharp
// HearthSim/SabberStone .../SabberStoneBasicAI/src/Nodes/OptionNode.cs
public OptionNode(OptionNode parent, Game game, int playerId, PlayerTask playerTask, IScore scoring) {
    _parent = parent;
    _game   = game.Clone();     // create clone
    ...
    if (!IsRoot) Execute();     // Execute() calls _game.Process(PlayerTask); then Score = Scoring.Rate();
}
```

`GetSolutions` expands all legal options at each depth, scores them, and **prunes to the top `maxWidth`** (a beam search), collecting end-of-turn states as candidate full-turn plans:

```csharp
depthNodes = nextDepthNodes
    .Where(p => !p.Value.IsEndTurn && p.Value.IsRunning)
    .OrderByDescending(p => p.Value.Score)   // greedy pruning
    .Take(maxWidth)
    .ToDictionary(p => p.Key, p => p.Value);
```

([OptionNode.cs](https://github.com/HearthSim/SabberStone/blob/master/core-extensions/SabberStoneBasicAI/src/Nodes/OptionNode.cs)).

**The evaluation function** these bots share is a plain hand-written heuristic. The abstract `Score` base exposes convenient views of the state (`HeroHp`, `OpHeroHp`, `BoardZone`, `MinionTotAtk`, `MinionTotHealthTaunt`, hand/deck counts, …) ([Score.cs](https://github.com/HearthSim/SabberStone/blob/master/core-extensions/SabberStoneBasicAI/src/Score/Score.cs)); a concrete strategy overrides `Rate()`:

```csharp
// AggroScore.Rate()  — an unashamedly hand-tuned formula
if (OpHeroHp < 1) return int.MaxValue;              // winning is infinitely good
if (HeroHp   < 1) return int.MinValue;              // losing is infinitely bad
int result = 0;
if (OpBoardZone.Count == 0 && BoardZone.Count > 0) result += 1000;   // board control
if (OpMinionTotHealthTaunt > 0) result += OpMinionTotHealthTaunt * -1000;  // taunts in the way
result += MinionTotAtk;
result += (HeroHp - OpHeroHp) * 1000;               // race their face
return result;
```

([AggroScore.cs](https://github.com/HearthSim/SabberStone/blob/master/core-extensions/SabberStoneBasicAI/src/Score/AggroScore.cs)). Other strategies: `ControlScore`, `MidRangeScore`, `RampScore`, `Fatigue`.

**Hidden information & randomness in SabberStone.** The `POGame` wrapper is how the framework hides what a real player couldn't see: it clones the game and **replaces the opponent's hand and deck cards with dummy/placeholder cards** so the bot can't cheat by reading them ([POGame.cs](https://github.com/ADockhorn/HearthstoneAICompetition/blob/master/core-extensions/SabberStoneBasicAI/src/PartialObservation/POGame.cs); mechanism described in [arXiv:1906.04238](https://arxiv.org/pdf/1906.04238)). There is also an **experimental "Splits" mechanism** (`SabberStoneCore/src/Splits/SplitNode.cs`) intended to represent a *probability tree* of the distinct outcomes a random effect can produce (each `SplitNode` carries a `Probability`). **Caveat:** in current `master` the entire `SplitNode` class is commented out, so I could not verify it is live/working — treat "Splits" as a design idea present in the tree, not a feature you can rely on. *(Noted as a gap.)*

### 📌 Why this matters for Pantheon
> SabberStone is a direct, working precedent for exactly the shape we're considering: **one mutable `Game` graph + a deep `Clone()` + tasks you `Process()` on the copy.** Concretely, it tells us: (1) store entity stats in a **flat, blittable structure** (their `int[]` tag store), not a per-entity `Dictionary`, if you want clone to be cheap; (2) put a **`resetRandomSeed`-style switch** on your clone so the *same* copy operation serves both search (fresh randomness) and replay (continued PRNG state); (3) keep **logging/history off by default** and opt-in, because it's pure overhead during search; (4) model a "move" as a **task/command object** (`PlayerTask`) that the engine executes, which is also exactly what you'll want to put in the event log (Topic 3). Copy their tag-store idea; you don't have to copy their (deprecated) GUI or their commented-out Splits.

---

## Topic 2 — Game-tree search / AI for card games

All the AI methods below share one primitive: **a function that takes a state, applies one legal move, and returns the successor state** — cheaply, and many times over. Everything else is bookkeeping about *which* successors to generate and how to score them. This is why Topic 1's `Clone()`+`Process()` is not an AI detail but *the* engine requirement.

### 2.1 Heuristic / greedy (one-ply) AI — the floor

The simplest competent bot: for each legal move, compute the state it would produce, run a **hand-written evaluation function** on that state, and pick the highest score. That is *literally* `GreedyAgent` above: `Simulate(options).OrderBy(Score).Last()` ([GreedyAgent.cs](https://github.com/ADockhorn/HearthstoneAICompetition/blob/master/core-extensions/SabberStoneBasicAI/src/AIAgents/Examples/GreedyAgent.cs)), where `Score` is a formula like `AggroScore.Rate()` ([AggroScore.cs](https://github.com/HearthSim/SabberStone/blob/master/core-extensions/SabberStoneBasicAI/src/Score/AggroScore.cs)).

**Why even greedy needs a "what would this move produce?" copy.** The evaluation function scores a *board*, not a *move*. You cannot know a move's value without materializing the board it leads to — which means you must apply it somewhere. If you apply it to the live state you've destroyed the ability to try the *other* moves; so you apply it to a **copy**. One ply of lookahead = one copy per legal move. (Card games make this worse than chess: a single "turn" is many chained sub-moves, so even one-ply-per-move bots like `OptionNode` end up doing a small beam search across a whole turn — again, one clone per node.)

### 2.2 Minimax + alpha-beta — adversarial lookahead

When you look more than one move ahead in a two-player zero-sum game, you must assume the opponent replies with *their* best move. **Minimax** alternates maximizing (you) and minimizing (opponent) layers down the tree and backs the value up. **Alpha-beta pruning** is the classic optimization: keep bounds α (best guaranteed for the maximizer) and β (best for the minimizer) and stop exploring a branch once it can't beat what you already have — provably the same answer as full minimax, far fewer nodes ([chessprogramming.org: Alpha-Beta](https://www.chessprogramming.org/Alpha-Beta); foundational analysis: Knuth & Moore, *"An Analysis of Alpha-Beta Pruning,"* Artificial Intelligence 6(4), 1975).

**Why it needs fast state copy/undo.** Depth-first minimax visits an exponential number of nodes; at every node it must produce a child state and, on the way back up, restore the parent. Two implementation strategies: **copy-on-descend** (clone the state for each child — simple, matches SabberStone's model) or **make/undo** (mutate in place going down, reverse the mutation coming up — less memory, but you must write a correct inverse for every effect). Card games with cascading triggers make correct `undo` very hard, which is a strong argument for the copy approach — the same reason SabberStone chose clone over undo (§1.3).

### 2.3 Monte Carlo Tree Search (MCTS) — the workhorse for card games

MCTS "combines the precision of tree search with the generality of random sampling" and is built by repeating four steps many times, growing an asymmetric tree biased toward promising lines ([Browne et al., *A Survey of Monte Carlo Tree Search Methods*, IEEE TCIAIG 4(1):1–43, 2012 — PDF](http://repository.essex.ac.uk/4117/1/MCTS-Survey.pdf), [IEEE DOI](https://ieeexplore.ieee.org/document/6145622)). The four phases:

1. **Selection** — from the root, repeatedly pick a child using a *tree policy* until you reach a node with unexpanded moves. The standard policy is **UCT/UCB1**, which balances *exploitation* (high average value) against *exploration* (rarely-tried moves): pick the child maximizing `X̄ᵢ + C·√(ln N / nᵢ)`, where `X̄ᵢ` is the child's mean reward, `N` the parent's visit count, `nᵢ` the child's visit count, and `C` a tuning constant (Browne et al. 2012, §3; the UCT algorithm is Kocsis & Szepesvári 2006, cited therein).
2. **Expansion** — add one (or more) new child node for an untried move.
3. **Simulation / rollout (a.k.a. playout)** — from the new node, play the game to the end using a fast *default policy*, typically **random** move selection, and observe the outcome (win/loss). This is the "Monte Carlo" part.
4. **Backpropagation** — walk back up the visited path, incrementing visit counts and updating value estimates with the rollout result.

After a fixed budget of iterations (time or count), you play the root child with the most visits (or highest value).

**Why rollouts need many cheap throwaway state copies.** Each rollout plays *dozens* of random moves from the current node to a terminal state, and MCTS does *thousands* of rollouts per decision. Each rollout must run on a **private, disposable copy** of the state (you can't corrupt the real game, and sibling rollouts must not see each other's moves). So MCTS's appetite for "clone → play random moves → read result → discard" is enormous — orders of magnitude more copies than minimax. An engine whose clone is slow simply cannot run competitive MCTS. This is the sharpest possible statement of the recurring requirement. (In the SabberStone ecosystem, rollouts are exactly `game.Clone()` then repeated `Process(randomOption)` — the `RandomGames()` loop in the base `Program.cs` shows the random-playout primitive.)

### 2.4 Determinism + hidden information (the card-game-specific hard part)

Chess is *perfect information* and *deterministic*; card games are neither. Two problems break vanilla tree search:

- **Hidden information**: you don't know the opponent's hand or the order of either deck.
- **Stochasticity**: cards have random effects ("summon a random Beast," "deal damage to a random enemy").

The standard fix is **determinization**: sample a concrete "possible world" consistent with what you *can* see (guess a specific opponent hand and deck ordering), run ordinary tree search on that fully-observable, deterministic instance, and repeat over many sampled worlds, aggregating the results ("Perfect Information Monte Carlo"). Its known weakness — it assumes future information is known and can't reason about *gathering* or *hiding* information — motivates **Information Set MCTS (IS-MCTS)**, which searches trees of *information sets* (states indistinguishable to the player) rather than concrete states, "more directly analyzing the true structure of the game" ([Cowling, Powley & Whitehouse, *Information Set Monte Carlo Tree Search*, IEEE TCIAIG 4(2), 2012 — PDF](https://eprints.whiterose.ac.uk/id/eprint/75048/1/CowlingPowleyWhitehouse2012.pdf)). SabberStone's practical, lighter-weight version of determinization is `POGame` swapping the opponent's unseen cards for dummies (§1.4), and the (dormant) `Splits` probability tree for random effects.

### 2.5 Tie it back

Greedy, minimax/alpha-beta, and MCTS look different but rest on the **same engine primitive**: *apply a move to a copy of the state, evaluate, discard.* Greedy does it once per move; minimax does it exponentially with copy/undo; MCTS does it thousands of times with cheap random rollouts; determinization/IS-MCTS does it across many sampled hidden-info worlds. **The more capable the AI you eventually want, the cheaper that copy has to be.** Designing Pantheon's state for fast clone from day one is therefore not premature optimization — it is the one decision that keeps every future AI option open.

### 📌 Why this matters for Pantheon
> This is the *justification* for the whole clone-centric design. If Pantheon ever wants more than a hard-coded scripted opponent, it will want (at least) a greedy heuristic bot, and ideally MCTS. All of them demand `clone(state)` + `applyMove(clone)` + `score(clone)`. Build that primitive well and the AI ladder (random → greedy → beam search → MCTS → IS-MCTS) is a matter of adding search code on top, not re-architecting the engine. Start with the **greedy one-ply bot** (it's ~15 lines, per `GreedyAgent`) to validate that your clone+apply+score loop is correct and fast, *then* climb toward MCTS.

---

## Topic 3 — Event sourcing for games

Where Topic 1/2 fork state *sideways* for hypothetical search, event sourcing concerns the *authoritative forward timeline*: making the real game reproducible.

### 3.1 Event log as source of truth

Martin Fowler's canonical definition: **Event Sourcing "captures all changes to an application state as a sequence of events,"** stored so that "we can query an application's state to find out the current state of the world, and this answers many questions. However there are times when we don't just want to see where we are, we also want to know how we got there." ([martinfowler.com — Event Sourcing](https://martinfowler.com/eaaDev/EventSourcing.html)). The defining property: you can **throw away current state and rebuild it entirely by replaying the event log from an empty starting state.** Fowler lists the payoffs — **complete rebuild**, **temporal query** (state as of any past moment), **event replay** (fix a bad event and re-run), and an **audit trail** ([same article](https://martinfowler.com/eaaDev/EventSourcing.html)).

For a game this maps cleanly: the *initial state* is `(deck lists, starting seed, rules version)`; the *events* are the ordered player moves (and any external inputs); the *current board* is a **derived value** you compute by replaying. State stops being the thing you store and becomes a cache of "the log so far."

### 3.2 Seeded / deterministic RNG — the linchpin

Event sourcing only works if replaying the same events yields the same state. Any randomness must therefore be **reproducible**. The technique is a **seeded pseudo-random generator (PRNG)**: same seed + same sequence of draws → same sequence of numbers. Combined with an ordered input log, this makes the whole game a **pure function of `(seed, ordered inputs)`**.

This is exactly the principle behind **deterministic lockstep** networking, where machines exchange only *inputs* and each re-simulates to the identical result: *"Deterministic lockstep is a method of networking ... by sending only the inputs that control [the system], rather than the state"* ([Glenn Fiedler, *Deterministic Lockstep*](https://gafferongames.com/post/deterministic_lockstep/)). The classic Age of Empires write-up makes the same point at scale — it ran 1,500 units over a modem by syncing commands, not unit positions ([Bettner & Terrano, *"1500 Archers on a 28.8"*, Game Developer/Gamasutra 2001](https://www.gamedeveloper.com/programming/1500-archers-on-a-28-8-network-programming-in-age-of-empires-and-beyond); [PDF mirror](https://zoo.cs.yale.edu/classes/cs538/readings/papers/terrano_1500arch.pdf)).

**Why ad-hoc `System.Random` / `UnityEngine.Random` breaks this.** Determinism requires that *every* random draw comes from a generator whose **entire state is part of the game state** and is advanced only by game logic in a fixed order. Ad-hoc RNG violates this in several ways: a `static`/global `Random` (or Unity's static `Random`) is shared across systems and its draw order depends on frame timing, GC, or unrelated code; an un-seeded `new Random()` seeds from the wall clock, so no two runs match; and because that state lives *outside* your saved/replayed data, you cannot reconstruct it. The moment any board outcome depends on such a generator, `replay(events)` no longer reproduces the original game — the core event-sourcing invariant is gone. (This is the software-side corollary of Fiedler's warning that determinism is *fragile* and must be engineered deliberately, e.g. also across floating point: *"that does not necessarily mean it would also be deterministic across different compilers, a different OS or different machine architectures"* — [Deterministic Lockstep](https://gafferongames.com/post/deterministic_lockstep/); for a headless C# engine, prefer integer/fixed-point math for any state-affecting arithmetic to sidestep cross-platform float drift).

**SabberStone already implements the correct counter-pattern.** Its `DeepCloneableRandom` is a seedable xorshift-style PRNG whose *entire* state is two `long`s (`_state0`, `_state1`); it has constructors taking an explicit `long seed` (or raw state pair), and a `Clone()` that copies the two state words so a forked game continues the identical sequence ([Utils.cs](https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Model/Utils.cs)). Because the RNG state is self-contained and cloneable, SabberStone can (a) run deterministically from a seed and (b) fork randomness precisely — which is exactly what an event-sourced, replayable Pantheon needs. Note the default `DeepCloneableRandom()` ctor seeds *non-deterministically* from `System.Random`; for replay you must construct it from an explicit seed (SabberStone threads a `RandomSeed` through its game config).

### 3.3 What event sourcing + determinism buy you

Once the game is `pure(seed, inputs)`:

- **Replay / spectating / bug repro**: store `(seed, inputs)` — a few KB — and reconstruct any match frame-perfectly. A bug report becomes a seed + input list.
- **Netcode reconciliation**: with an authoritative server as "the one and only authority regarding everything that happens in the world" ([Gambetta, *Client-Server Game Architecture*](https://gabrielgambetta.com/client-server-game-architecture.html)), clients can predict locally and reconcile against the authoritative event stream; deterministic lockstep is the extreme form where clients exchange only inputs ([Fiedler](https://gafferongames.com/post/deterministic_lockstep/)).
- **Analytics**: the event log *is* the analytics feed — every action is already a structured record ("what fraction of games open with X?") without extra instrumentation (this is Fowler's "audit trail" reframed — [Event Sourcing](https://martinfowler.com/eaaDev/EventSourcing.html)).
- **Hidden-information redaction**: because the server derives state and *emits* events, it can send each player a **filtered event stream** — the same authoritative timeline, minus what that player shouldn't see (opponent's drawn card sent as "opponent drew a card," not *which* card). This is the online, per-recipient version of what SabberStone's `POGame` does offline by swapping in dummy cards (§1.4). Server-authority is the precondition that makes redaction trustworthy ([Gambetta](https://gabrielgambetta.com/client-server-game-architecture.html)).

### 3.4 Semantic events vs. state deltas

Two very different things can travel down that log/stream, and Pantheon will care about both:

- **Semantic event** — a *domain* statement of what happened in game terms: **"Hoplite attacked Spearman."** It carries intent and causality but not, by itself, the numeric consequences. (Fowler's events are domain events in exactly this sense — [Event Sourcing](https://martinfowler.com/eaaDev/EventSourcing.html); he separates the *narrative* of what happened from the resulting state in the related [Event Narrative note](https://martinfowler.com/eaaDev/EventNarrative.html).)
- **State delta** — a low-level record of *which fields changed*: **"entity 4 Health 3 → 1."** SabberStone's `PowerHistory` is precisely this: a stream of tagged deltas describing state changes ([Game.cs `PowerHistory`](https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Model/Game.cs)), used by its Kettle client-server layer to sync a UI.

**Tradeoffs.**

| | Semantic event | State delta |
|---|---|---|
| **Carries** | intent, causality, "the story" | exact resulting numbers |
| **Client needs** | must know rules to compute effects | can apply blindly, no rules engine |
| **Animation** | ✅ ideal — "attacked" → play the attack anim | ✗ just sees numbers change, must *infer* the animation |
| **Netcode/reconciliation** | ✗ ambiguous; two clients might compute effects differently → desync risk | ✅ authoritative and unambiguous; apply and you're identical to the server |
| **Log size / coupling** | compact, but couples reader to game rules & version | verbose, but decoupled from rules |
| **Redaction** | easy to filter at the domain level | easy to filter per-entity/per-field |

Animation-driven clients want **semantic** events (they need to know a *thing happened* to play the right effect); netcode and reconciliation want **deltas** (unambiguous, rules-independent, guaranteed to reproduce the server's exact numbers). The common resolutions:

1. **Emit semantic, derive deltas** — the log stores intent; each consumer replays through the rules engine to get deltas. Compact and rules-faithful, but every consumer needs the (versioned) engine, and old logs can break when rules change.
2. **Emit deltas, no semantics** — simplest for sync (SabberStone's `PowerHistory` approach), but the client must reverse-engineer *why* to animate it well.
3. **Emit both** — the semantic event *with* its resulting deltas attached (e.g. `AttackEvent{attacker, target, damageDealt, deaths:[…]}`). Redundant on the wire, but the client gets the story *and* the exact numbers, and reconciliation stays unambiguous. This "fat event" is a common pragmatic choice for animation-heavy card games. *(I could not find a single authoritative primary source prescribing option 3 specifically for card games; it is synthesized from the Fowler event/narrative distinction plus the delta-based sync seen in SabberStone's PowerHistory and general server-authoritative practice — flagged as partly inferred.)*

### 📌 Why this matters for Pantheon
> Make the authoritative battle a **pure function of `(seed, ordered inputs)`**: one seeded, self-contained PRNG (copy SabberStone's `DeepCloneableRandom` idea — its whole state is two `long`s and it's cloneable), and never touch `UnityEngine.Random` or an un-seeded `System.Random` for anything that affects the board. Do that and you get replay, deterministic tests, netcode, analytics, and hidden-info redaction almost for free — and it *composes with Topic 1*: a clone is just "replay the log up to now onto a fresh state," and a cloned PRNG makes a fork bit-identical. For the event log, lean toward **emitting semantic events and attaching the resulting deltas** ("fat events"): the Unity client animates from the semantics, while reconciliation/tests rely on the deltas. Keep state-affecting math in integer/fixed-point to avoid float non-determinism.

---

## Sources

**Topic 1 — SabberStone (primary: source code)**
- Repo & README (AGPLv3): https://github.com/HearthSim/SabberStone
- Wiki (Getting Started): https://github.com/HearthSim/SabberStone/wiki/Getting-Started
- `Game.cs` (Game state machine, `Clone`, copy ctor, RNG): https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Model/Game.cs
- `Controller.cs` (the Player; zones; `Clone`): https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Model/Entities/Controller.cs
- `Entity.cs` (base entity / Tags; copy ctor): https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Model/Entities/Entity.cs
- `EntityData.cs` (flat `int[]` tag store; unsafe copy): https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Model/Entities/EntityData.cs
- `Utils.cs` (`DeepCloneableRandom` seeded/cloneable PRNG): https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Model/Utils.cs
- `Splits/SplitNode.cs` (probability tree — currently commented out): https://github.com/HearthSim/SabberStone/blob/master/SabberStoneCore/src/Splits/SplitNode.cs
- Base-repo AI: `OptionNode.cs` (beam search): https://github.com/HearthSim/SabberStone/blob/master/core-extensions/SabberStoneBasicAI/src/Nodes/OptionNode.cs
- `Score.cs` (eval base): https://github.com/HearthSim/SabberStone/blob/master/core-extensions/SabberStoneBasicAI/src/Score/Score.cs
- `AggroScore.cs` (example heuristic): https://github.com/HearthSim/SabberStone/blob/master/core-extensions/SabberStoneBasicAI/src/Score/AggroScore.cs
- Competition framework repo: https://github.com/ADockhorn/HearthstoneAICompetition
- `POGame.cs` (`Simulate` = clone+process+discard; hidden-info dummies): https://github.com/ADockhorn/HearthstoneAICompetition/blob/master/core-extensions/SabberStoneBasicAI/src/PartialObservation/POGame.cs
- `GreedyAgent.cs` (one-ply bot): https://github.com/ADockhorn/HearthstoneAICompetition/blob/master/core-extensions/SabberStoneBasicAI/src/AIAgents/Examples/GreedyAgent.cs
- Paper: Dockhorn & Mostaghim, *Introducing the Hearthstone-AI Competition* (arXiv:1906.04238): https://arxiv.org/abs/1906.04238 (PDF: https://arxiv.org/pdf/1906.04238)
- Competition retrospective (98%/2019 figure, method survey): Kowalski & Miernik, arXiv:2305.11814: https://arxiv.org/html/2305.11814

**Topic 2 — Game-tree search / AI**
- Browne et al., *A Survey of Monte Carlo Tree Search Methods*, IEEE TCIAIG 4(1), 2012 — PDF: http://repository.essex.ac.uk/4117/1/MCTS-Survey.pdf ; DOI/landing: https://ieeexplore.ieee.org/document/6145622
- Cowling, Powley & Whitehouse, *Information Set Monte Carlo Tree Search*, IEEE TCIAIG 4(2), 2012 — PDF: https://eprints.whiterose.ac.uk/id/eprint/75048/1/CowlingPowleyWhitehouse2012.pdf
- Alpha-beta pruning: https://www.chessprogramming.org/Alpha-Beta (foundational: Knuth & Moore, *An Analysis of Alpha-Beta Pruning*, Artificial Intelligence 6(4), 1975)

**Topic 3 — Event sourcing & determinism**
- Martin Fowler, *Event Sourcing*: https://martinfowler.com/eaaDev/EventSourcing.html
- Martin Fowler, *Event Narrative* (event vs. resulting state): https://martinfowler.com/eaaDev/EventNarrative.html
- Glenn Fiedler, *Deterministic Lockstep*: https://gafferongames.com/post/deterministic_lockstep/
- Bettner & Terrano, *1500 Archers on a 28.8* (Age of Empires deterministic lockstep): https://www.gamedeveloper.com/programming/1500-archers-on-a-28-8-network-programming-in-age-of-empires-and-beyond (PDF: https://zoo.cs.yale.edu/classes/cs538/readings/papers/terrano_1500arch.pdf)
- Gabriel Gambetta, *Client-Server Game Architecture* (authoritative server, reconciliation): https://gabrielgambetta.com/client-server-game-architecture.html

---

## Recommended reading order (for a learner)

1. **Gambetta, *Client-Server Game Architecture*** — gentlest intro; establishes "authoritative server, clients send inputs." Sets the mental model. https://gabrielgambetta.com/client-server-game-architecture.html
2. **Fowler, *Event Sourcing*** — the "log is the source of truth, state is derived" idea in the abstract. https://martinfowler.com/eaaDev/EventSourcing.html
3. **Fiedler, *Deterministic Lockstep*** — why the sim must be a pure function of inputs, and why determinism is fragile. https://gafferongames.com/post/deterministic_lockstep/
4. **SabberStone `Game.cs` + `Controller.cs` `Clone`** — see the abstract ideas as real C#: mutable graph, deep copy, seeded RNG. Read `EntityData.cs` right after to understand *why* clone is cheap.
5. **SabberStone `GreedyAgent.cs` + `AggroScore.cs`** — the smallest complete "clone → apply → score → pick" AI. This is what to build first in Pantheon.
6. **Browne et al., *MCTS Survey*, §§2–3 only** — read just the four-phase algorithm and UCT; skip the exhaustive variant taxonomy on a first pass. http://repository.essex.ac.uk/4117/1/MCTS-Survey.pdf
7. **Cowling et al., *IS-MCTS*** (optional, later) — only once you actually want to handle hidden information well. https://eprints.whiterose.ac.uk/id/eprint/75048/1/CowlingPowleyWhitehouse2012.pdf

---

## Verification notes / gaps

- **No hard clone benchmark found.** SabberStone is *architected* for fast clone (flat blittable tag store, pre-sized collections, opt-in logging), and bots clone heavily in practice, but I found no published ns/clone or clones-per-second figure in the repo, wiki, or paper. The "cheap" claim is architectural inference, not a measured number.
- **`Splits`/`SplitNode` is dormant.** The probability-tree mechanism for random outcomes exists in the tree but its class body is **entirely commented out** in current `master`; I could not verify it runs. `POGame` dummy-card swapping is the mechanism that is actually live for hidden information.
- **Line numbers are approximate.** Cited line numbers (e.g. `Clone` ~L1228) are from `master` at the time of research and will drift; the blob links point to the files, not pinned commits.
- **"Emit both / fat events" (Topic 3.4) is partly synthesized.** The event-vs-delta *distinction* is well-sourced (Fowler; SabberStone `PowerHistory`), but I found no single primary source prescribing "attach deltas to semantic events" *specifically for card games*; that recommendation is reasoned from the cited sources plus common practice, not quoted from one authority.
- **Browne survey PDF host.** The author's own copy (cameronius.com / mcts.ai) no longer resolves; I substituted the University of Essex repository PDF, which does resolve, and the IEEE DOI landing page. Content is the same published paper.
