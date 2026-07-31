# Pantheon v1 Engine — Implementation Plan (learning-shaped)

> **Execution model:** This plan is executed **by the developer, by hand**, to learn. It is *not* for agentic workers. Steps use `- [ ]` for tracking.

**Goal:** Build the headless v1 combat engine (vanilla creatures only) that plays a full match AI-vs-AI in the Unity Test Runner with zero graphics.

**Architecture:** One Unity project. All rules live in a pure-C# `Pantheon.Core` assembly walled off from Unity by an asmdef (`No Engine References`). Tests drive Core via `Pantheon.CoreTests`. The Unity `View` layer is built later, after Slice 5.

**Tech Stack:** Unity 6 LTS · C# · Unity Test Framework (NUnit, EditMode) · asmdefs.

**Companion specs:** [engine architecture](../specs/2026-07-31-engine-architecture-design.md) · [game foundation](../specs/2026-07-26-card-game-foundation-design.md)

## Global Constraints
- `Pantheon.Core` must never reference `UnityEngine`/`UnityEditor` (`noEngineReferences: true`). If it won't compile because of a `using UnityEngine`, that's the guardrail working — remove the dependency, don't disable the flag.
- **TDD:** every logic behavior gets a failing test *before* its implementation.
- **Commit frequently:** after each green step or small refactor.
- **v1 scope:** vanilla creatures only. No abilities, keywords, spells, hero powers, mulligan, or networking. Placeholder numbers (hero 30 HP, board cap 7, mana cap 10) live in named constants so tuning is trivial later.

## Working protocol (how you and I run each step)
- **Boilerplate steps** (project setup, asmdefs, `.gitignore`, test scaffolding): exact copy-paste / exact clicks below. Just do them — no learning lost.
- **Logic steps:** I give you (a) the behavior, (b) the test to write with **assertions described in words**, (c) the **API shape** to aim for, (d) hints. **You write the C#.** Then paste it here; I review and explain.
- **Escape hatch:** stuck or analysis-paralysis → say *"how do I start here?"* for a hint, or *"write this one"* and I'll draft that component with commentary.
- **Cadence per behavior:** red (write test, watch it fail) → green (your minimal code) → refactor → commit.

---

## Slice 0 — Toolchain skeleton (BOILERPLATE — copy-paste / clicks)

**Deliverable:** empty project that compiles, with three assemblies wired, and one green throwaway test that proves Tests can see Core.

- [ ] **Step 1 — Install & create the project.**
  Install **Unity Hub**, then a **Unity 6 LTS** editor. In Hub → **New Project** → any template (2D or 3D — non-binding per the spec) → set **Location** to `/Users/vlad_d/Desktop` and **Project name** to `Pantheon` so the Unity project root *is* this repo. (If Hub refuses to create into the existing folder, create `Pantheon-tmp` beside it and move `Assets/`, `Packages/`, `ProjectSettings/` into this repo root afterward.)

- [ ] **Step 2 — Add the Unity `.gitignore`.**
  Create `/.gitignore` at the repo root with this content:

```gitignore
# Unity generated
[Ll]ibrary/
[Tt]emp/
[Oo]bj/
[Bb]uild/
[Bb]uilds/
[Ll]ogs/
[Uu]ser[Ss]ettings/
[Mm]emoryCaptures/
[Rr]ecordings/

# Asset meta data (only ignore for deleted assets — keep meta files otherwise)
!/[Aa]ssets/**/*.meta

# Unity3D generated for Visual Studio / Rider
.vs/
.idea/
*.csproj
*.sln
*.user
*.userprefs
Assembly-CSharp*

# OS
.DS_Store
```

- [ ] **Step 3 — Create the Core assembly.**
  In the Project window: `Assets/Scripts/Core/` → right-click → **Create ▸ Assembly Definition** → name it **`Pantheon.Core`**. Select it, and in the Inspector **tick `No Engine References`**, then Apply. The `.asmdef` should read:

```json
{
    "name": "Pantheon.Core",
    "rootNamespace": "Pantheon.Core",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "noEngineReferences": true,
    "autoReferenced": true
}
```

- [ ] **Step 4 — Create the View assembly (empty for now).**
  `Assets/Scripts/View/` → **Create ▸ Assembly Definition** → **`Pantheon.View`**. In the Inspector, under **Assembly Definition References**, add **`Pantheon.Core`**. Leave `No Engine References` unticked.

- [ ] **Step 5 — Create the Tests assembly (let Unity wire it).**
  `Assets/Tests/EditMode/` → right-click → **Create ▸ Testing ▸ EditMode Test Assembly Folder**. This generates a correctly-wired test asmdef. Rename it **`Pantheon.CoreTests`**, and in the Inspector add **`Pantheon.Core`** to its references.

- [ ] **Step 6 — Throwaway Core symbol + smoke test (copy-paste).**
  This only proves the wiring; you'll delete it in Slice 1.
  `Assets/Scripts/Core/CoreInfo.cs`:

```csharp
namespace Pantheon.Core
{
    public static class CoreInfo
    {
        public const string Name = "Pantheon.Core";
    }
}
```

  `Assets/Tests/EditMode/SmokeTest.cs`:

```csharp
using NUnit.Framework;
using Pantheon.Core;

namespace Pantheon.CoreTests
{
    public class SmokeTest
    {
        [Test]
        public void Tests_CanSee_Core()
        {
            Assert.AreEqual("Pantheon.Core", CoreInfo.Name);
        }
    }
}
```

- [ ] **Step 7 — Run it green.**
  Unity → **Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All**. Expect `Tests_CanSee_Core` PASS. If Core won't compile or Tests can't find `CoreInfo`, the asmdef references are wrong — fix before moving on.

- [ ] **Step 8 — Commit.**

```bash
git add .gitignore Assets ProjectSettings Packages
git commit -m "chore: scaffold Unity project with Core/View/Tests assemblies"
```

---

## Slice 1 — State you can build and inspect (GUIDED — you write the C#)

**Deliverable:** the data types exist and can be constructed; no rules yet. Delete `CoreInfo`/`SmokeTest` once the first real test references a real type.

**API shapes to aim for** (fields/signatures are the target; you write the bodies):
- `enum CardType { Creature }`
- `class CardDefinition` — `string Name`, `int Cost`, `int Attack`, `int Health`, `CardType Type`. Immutable (set once via constructor).
- `class CardInstance` — built from a `CardDefinition`; `CardDefinition Definition`, `int CurrentAttack`, `int CurrentHealth`, `bool IsSummoningSick`, `bool HasAttackedThisTurn`, unique `int Id`. On creation: current stats copied from the definition, `IsSummoningSick = true`.
- `class Hero` — `int Health` (starts 30 via a `const int StartingHealth = 30`).
- `class Deck` — holds an ordered `List<CardDefinition>`; `int Count`; `CardDefinition Draw()` removes and returns the top; some signal (e.g. `bool IsEmpty`) for empty.
- `class Hand` — wraps a `List<CardDefinition>`; `int Count`, `const int MaxSize = 10`.
- `class Board` — wraps a `List<CardInstance>`; `int Count`, `const int MaxSize = 7`.
- `class PlayerState` — `Hero Hero`, `Deck Deck`, `Hand Hand`, `Board Board`, `int CurrentMana`, `int MaxMana`.
- `class GameState` — two `PlayerState`s, `int ActivePlayerIndex`, `int TurnNumber`, `int? WinnerIndex` (null = ongoing).

- [ ] **Test 1 — `CardInstance` copies stats and starts sick.** Given a `CardDefinition` "Hoplite" (cost 2, atk 2, hp 3), a new `CardInstance` from it has `CurrentAttack == 2`, `CurrentHealth == 3`, `IsSummoningSick == true`, `HasAttackedThisTurn == false`.
- [ ] **Test 2 — fresh `GameState` baseline.** Both heroes at 30 HP, both boards empty, `WinnerIndex == null`, `TurnNumber` at its start value.
- [ ] **Test 3 — `Deck.Draw()` returns top and shrinks.** A deck built from a known list returns the expected top card and `Count` drops by one.
- [ ] Red → green → refactor for each; then **commit** (`feat: core state types`). Delete the Slice-0 throwaways.

> Hint if stuck on Test 1: start by writing the `CardDefinition` constructor, then a `CardInstance` constructor that takes a `CardDefinition` and copies its numbers.

---

## Slice 2 — Turn lifecycle (GUIDED)

**Deliverable:** turns advance; mana ramps; draw + fatigue + win-check work. No cards played yet.

**API shapes to aim for:**
- A rules entry point, e.g. `static class GameEngine`.
- Turn start logic (call it when a turn begins): `MaxMana = min(MaxMana + 1, 10)`, `CurrentMana = MaxMana`, draw 1 into hand, clear `IsSummoningSick`/`HasAttackedThisTurn` on that player's board.
- Fatigue: add `int FatigueCounter` to `PlayerState`. Drawing from an empty deck increments it and deals that much damage to the player's own hero.
- Win check: after any HP change, if a hero ≤ 0, set `WinnerIndex`.
- `GameState ApplyMove(GameState state, Move move)` for `EndTurnMove` (v1: mutate the passed state and return it — simplest; immutability can come later).

- [ ] **Test 1 — start of turn 1.** After starting the first turn, active player has `MaxMana == 1`, `CurrentMana == 1`, and one more card in hand than before.
- [ ] **Test 2 — mana ramps and caps.** Repeated turn starts take `MaxMana` 1→2→…→10 and then stop at 10.
- [ ] **Test 3 — `EndTurn` passes control.** Applying `EndTurnMove` flips `ActivePlayerIndex` and runs the next player's turn start.
- [ ] **Test 4 — fatigue escalates.** Drawing from an empty deck deals 1, then 2, then 3 to your own hero.
- [ ] **Test 5 — lethal fatigue wins.** Enough fatigue to drop a hero to ≤ 0 sets `WinnerIndex` to the other player.
- [ ] Red → green → refactor each; **commit** (`feat: turn lifecycle, mana ramp, fatigue`).

> Maps to `docs/gameplay-loop/frames/turn-0-opening.drawio`.

---

## Slice 3 — Play a creature (GUIDED)

**Deliverable:** a creature can be played from hand to board, legally.

**API shapes to aim for:**
- `class PlayCardMove : Move` — `int HandIndex`, optional `int BoardPosition`.
- `IReadOnlyList<Move> GetLegalMoves(GameState state)` — for now returns the affordable `PlayCardMove`s (creature cost ≤ current mana) when the board isn't full, plus `EndTurnMove`.
- `ApplyMove` for `PlayCardMove`: spend mana, remove the card from hand, add a summoning-sick `CardInstance` to the active board.

- [ ] **Test 1 — can't afford → not legal.** With 1 mana and a 2-cost creature in hand, `GetLegalMoves` does **not** include a play for it.
- [ ] **Test 2 — playing it updates state.** With 2 mana, applying the play puts one `CardInstance` on the board (summoning-sick), reduces `CurrentMana` by 2, and shrinks the hand by 1.
- [ ] **Test 3 — full board blocks plays.** With 7 minions already out, no `PlayCardMove` is legal.
- [ ] Red → green → refactor each; **commit** (`feat: play creature move`).

> Maps to `docs/gameplay-loop/frames/turn-1-first-move.drawio`.

---

## Slice 4 — Attack / combat resolution (GUIDED)

**Deliverable:** minions attack minions or the enemy hero, with mutual damage and death.

**API shapes to aim for:**
- `class AttackMove : Move` — attacker `int AttackerId`, and a target (enemy `CardInstance` id, or a marker meaning "enemy hero").
- `GetLegalMoves` also returns attacks for minions that `!IsSummoningSick && !HasAttackedThisTurn`.
- `ApplyMove` for `AttackMove`: minion↔minion deals each other's `CurrentAttack` simultaneously; minion→hero subtracts `CurrentAttack` from hero HP; mark `HasAttackedThisTurn`; remove minions at ≤ 0 HP; run win check.

- [ ] **Test 1 — summoning sickness.** A minion played this turn has no attack move; after its owner's next turn start, it does.
- [ ] **Test 2 — mutual damage & death.** A 2/3 vs a 3/2: both take damage; the 2-HP one dies, the 3-HP one survives at 1.
- [ ] **Test 3 — attack the hero.** A 3-attack minion hitting the enemy hero drops that hero's HP by 3.
- [ ] **Test 4 — lethal to hero wins.** Reducing a hero to ≤ 0 via attack sets `WinnerIndex`.
- [ ] Red → green → refactor each; **commit** (`feat: attack and combat resolution`).

---

## Slice 5 — Scripted AI + full-match harness (GUIDED)

**Deliverable:** the engine plays itself end-to-end and terminates with a winner.

**API shapes to aim for:**
- A test-deck helper (plain code, no ScriptableObjects yet), e.g. `static class TestDecks` returning a `List<CardDefinition>` of a few Greek stat-stick creatures at varied cost/atk/hp.
- A game factory: `static GameState CreateNewGame(List<CardDefinition> deckA, List<CardDefinition> deckB, int shuffleSeed, int openingHandSize = 3)` — builds both `PlayerState`s, **shuffles decks with a seeded RNG** (`new System.Random(shuffleSeed)` — a fixed seed keeps tests deterministic), deals opening hands, sets `ActivePlayerIndex`, and runs the first turn start.
- `interface IAgent { Move ChooseMove(GameState state, IReadOnlyList<Move> legalMoves); }`
- `class ScriptedAgent : IAgent` — policy: play the first affordable creature; else attack the enemy hero with any able minion; else `EndTurnMove`.
- A harness, e.g. `static GameState RunToCompletion(GameState state, IAgent p1, IAgent p2, int maxTurns = 100)` that loops: get legal moves for the active player, ask that player's agent to choose, apply, repeat until `WinnerIndex != null` or `maxTurns` guard trips.

- [ ] **Test 0 — new game is playable.** `CreateNewGame` with a fixed seed yields both players holding `openingHandSize` cards, decks reduced by that many, `WinnerIndex == null`, and turn 1 started (active player mana 1/1). Same seed → identical setup twice (determinism).
- [ ] **Test 1 — a match completes.** An AI-vs-AI game from `CreateNewGame` ends with a non-null `WinnerIndex` within the turn guard. (Fatigue guarantees termination.)
- [ ] **Test 2 — no illegal moves.** (Optional but valuable) assert the harness only ever applies moves that were in `GetLegalMoves`.
- [ ] Red → green → refactor; **commit** (`feat: scripted agent and match harness`). **v1 engine complete.**

---

## After Slice 5
The engine plays headless in the Test Runner. Next design pass: the Unity **View** layer (render `GameState`, turn clicks into `Move`s, feed the same engine), and the render-look decision (flat vs 2.5D) — both parked in the spec until now.
