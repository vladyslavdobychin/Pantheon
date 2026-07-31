# Engine Architecture — Design

**Status:** Design approved 2026-07-31 — ready for implementation plan.
**Scope:** How the v1 walking skeleton is *built* (code structure, tooling, workflow).
**Companion doc:** [`2026-07-26-card-game-foundation-design.md`](2026-07-26-card-game-foundation-design.md) — the *game* design (what we're building and why). This doc is the *technical* design (how the code is organized).
**Working style:** The developer writes all game code themselves to learn. This spec and the resulting plan are guidance, not code to hand over.

---

## Decisions locked this session

| Decision | Choice | Why |
|---|---|---|
| Engine (Unity project) | Unity 6 LTS, **2D** template, created via **Unity Hub** | Card game is 2D. Hub scaffolds the project; the editor does not "convert" a folder. |
| Where to start | **Headless engine first**, visuals last | Directly honors the foundation doc's rule: *combat core knows nothing about presentation.* Logic-first also makes TDD pleasant. |
| Engine vs. Unity split | **One Unity project**, Core walled off with an **assembly definition (asmdef)** | Compiler-enforced isolation without the plumbing of a second project. |
| Working method | **TDD** — write tests first, per slice | Core is pure logic (no Unity), so tests are fast, deterministic, no scene. The risky part (rules) becomes the fully-tested part. |

**External validation (2026-07-31 web scan):** Separating game rules from the view is the near-universal recommended Unity pattern (MonoBehaviours for visuals/input, plain C# for rules). asmdef is the standard tool to enforce that boundary and speed up compilation (zero runtime cost); its value grows with project size. Separation is non-negotiable; the asmdef is the mainstream way to lock it in.

---

## Architecture — two layers, one-way dependency

```
   Core  (plain C#, NO UnityEngine)          View (MonoBehaviours, Unity)
   ─────────────────────────────────         ────────────────────────────
   GameState  ← the whole match              BoardView, CardView, HeroView
   Rules engine (the "orchestrator")   <---  reads state, renders it
   Move types (player/AI intent)       --->  turns clicks into Moves
   Agents (AI now, human later)              feeds Moves back to engine

              View may call Core.   Core may NEVER call View.
```

The same engine the AI drives is the engine the human UI will drive. A click and an AI decision both become a `Move`; the engine can't tell them apart. This is the foundation doc's "pluggable opponent" requirement, realized.

---

## Component decomposition (Core)

Maps the developer's original mental model (board / card / deck / avatar / orchestrator) onto headless types.

| Envisioned | Headless type | What it is |
|---|---|---|
| card | **`CardDefinition`** + **`CardInstance`** | *Two things* — see correction #1 |
| board | field inside `GameState` | a list of ≤7 `CardInstance` per side — **data, not a visual** |
| deck | `Deck` | ordered card list + draw + fatigue counter |
| avatar | `Hero` | hp now; hero-power later |
| — | `PlayerState` | one player's hero + deck + hand + board + mana |
| — | **`GameState`** | both players + whose turn + turn # + winner. **Single source of truth** |
| orchestrator | **Rules engine** | pure logic over `GameState`; resolves damage, deaths, win |
| — | **`Move`** types | `PlayCard`, `Attack`, `EndTurn` — intent as data |
| — | **`IAgent`** | picks a Move; `ScriptedAgent` now, human UI later |

### Engine contract (v1)
- `GetLegalMoves(state)` → what the current player may legally do now
- `ApplyMove(state, move)` → next state (resolve damage, deaths, win check)
- turn lifecycle → start-of-turn (mana +1 capped at 10, refill, draw 1, clear summoning-sickness/has-attacked); end-of-turn

### Mental-model corrections (the load-bearing bits)
1. **Card = definition + instance — keep them separate.** `CardDefinition` = immutable blueprint (e.g. "Hoplite: cost 2, 2/3"), shared by every copy. `CardInstance` = one played copy with *current* hp and flags (summoning-sick, has-attacked). Two Hoplites on board = two instances of one definition.
2. **Board / hero / deck are data in `GameState`, not screen objects.** The visual board renders that data and is built last; it never owns state.
3. **The orchestrator is a pure engine, not a `MonoBehaviour`.** That is what makes it testable and reusable across future modes.
4. **Player input and AI share one path** — both emit `Move`s into the same engine.

---

## Project layout

One Unity project at the repo root; `docs/` stays where it is (Unity ignores non-`Assets/` folders).

```
Pantheon/                      <- repo root = Unity project root
|-- Assets/
|   |-- Scripts/
|   |   |-- Core/                    <- Pantheon.Core.asmdef  (NO engine refs)
|   |   |   |-- Cards/              CardDefinition, CardInstance
|   |   |   |-- State/             GameState, PlayerState, Hero, Deck, Hand
|   |   |   |-- Moves/              PlayCard, Attack, EndTurn
|   |   |   |-- Engine/            GetLegalMoves, ApplyMove, turn lifecycle
|   |   |   `-- Agents/             IAgent, ScriptedAgent
|   |   `-- View/                    <- Pantheon.View.asmdef  (refs Core + UnityEngine)
|   |       `-- (MonoBehaviours — built last)
|   |-- Tests/
|   |   `-- EditMode/                <- Pantheon.CoreTests.asmdef (refs Core)
|   |-- Data/                        card data assets (later)
|   |-- Scenes/
|   `-- Prefabs/
|-- Packages/            } Unity-generated
|-- ProjectSettings/     }
|-- docs/                            existing frames + specs
`-- .gitignore                       Unity ignore (Library/, Temp/, obj/, Logs/, Build/)
```

One `.asmdef` sits at the root of each assembly folder; subfolders inherit it. `Cards/`, `State/`, etc. are just organization inside the single `Pantheon.Core` assembly.

### The three assemblies
| Assembly | Key setting | References |
|---|---|---|
| `Pantheon.Core` | **No Engine References = ON** (the enforcement switch) | none |
| `Pantheon.View` | normal engine refs | `Pantheon.Core` |
| `Pantheon.CoreTests` | **Test Assembly**, EditMode only | `Pantheon.Core` + test framework |

Dependency flow is one-way and cycle-free: **View → Core**, **Tests → Core**, Core → nobody. With "No Engine References" on, a stray `using UnityEngine;` in Core will not compile — that compile error *is* the enforcement.

### Creating them (in Unity)
- Core & View: Project window → right-click → **Create ▸ Assembly Definition**. On Core, tick **No Engine References**. On View, add `Pantheon.Core` under References.
- Tests: right-click → **Create ▸ Testing ▸ EditMode Test Assembly Folder** (Unity wires the framework); add `Pantheon.Core` to its References.

### Watch-out: ScriptableObjects vs. the pure Core
`ScriptableObject` is a `UnityEngine` type, so it **cannot** live in the no-engine Core. Do **not** make `CardDefinition` a ScriptableObject. For v1's tiny test deck, build definitions in plain code or JSON. Later pattern (deferred, developer wants to learn SOs when relevant): pure `CardDefinition` in Core + a `CardDefinitionSO : ScriptableObject` in `Data/` that *produces* a Core definition.

---

## TDD approach

**What a "unit" is here:** the engine contract, not tiny getters. The high-value test shape is *given a `GameState`, apply a `Move`, assert the resulting `GameState`.* These read like the rulebook and stay valid as internals change.

**Frames are tests.** The drawio frames in `docs/gameplay-loop/frames/` describe exact state transitions, so each becomes an integration test: set up the frame's state, apply the move, assert the next frame. `turn-0-opening` ≈ setup/lifecycle; `turn-1-first-move` ≈ the `PlayCard` slice. Design docs and test suite become one artifact.

**Rhythm:** within each slice, red → green → refactor per rule. The `superpowers:test-driven-development` skill is the working discipline once coding starts (not during design).

---

## Build order (dependency-ordered slices)

Each slice: write the failing tests, then implement to green.

- **Slice 0 — Toolchain skeleton.** Create project, 2D, `.gitignore`, three asmdefs. One throwaway test that references Core and asserts something trivial. *Proves the chain works before any real logic.*
- **Slice 1 — State (data only, no rules).** `CardDefinition`, `CardInstance`, `Hero`, `Deck`, `Hand`, `Board`, `PlayerState`, `GameState`. First test: a `CardInstance` from a 2/3 definition has attack 2, hp 3, summoning-sick. Also: fresh `GameState` → both heroes 30, winner none; `Deck.Draw()` returns top and shrinks.
- **Slice 2 — Turn lifecycle.** Start-of-turn (mana +1 cap 10, refill, draw 1, clear flags), `EndTurn` swaps player, fatigue on empty deck, win check. First test: start of turn 1 → mana 1/1, hand +1. Maps to `turn-0-opening`.
- **Slice 3 — Play a creature (`PlayCard`).** `GetLegalMoves` offers affordable creatures when board not full; `ApplyMove` pays mana, moves card hand→board as summoning-sick instance; illegal plays rejected. First test: a 2-cost creature with 1 mana is not a legal move. Maps to `turn-1-first-move`.
- **Slice 4 — Attack (combat).** `GetLegalMoves` offers attacks for minions that can act, vs enemy minions or hero; `ApplyMove` does mutual damage, hero damage, removes dead minions, win check. First test: a minion can't attack the turn it's played; next turn it can.
- **Slice 5 — Scripted AI + full-match harness.** `IAgent.ChooseMove(state, legalMoves)`; `ScriptedAgent` = play first affordable creature, swing everything at face, end turn. Harness loops until a winner. First test: an AI-vs-AI match runs to completion and produces a winner (fatigue guarantees termination). **End-to-end proof the engine works headless — v1 done.**

After Slice 5 the engine plays itself in the Test Runner with zero graphics. Only then is the Unity View layer built on top.

---

## Deferred (not forgotten)
- **View layer** — MonoBehaviours that render `GameState` and turn clicks into `Move`s. Designed after Slice 5.
- **Human agent** — an `IAgent` fed by UI input, dropped into the same harness.
- **ScriptableObject card authoring** — the `CardDefinitionSO` bridge pattern; developer wants to learn SOs when it becomes relevant.
- Everything the foundation doc parks (abilities/effects, spells, hero power, second pantheon, other modes, networking).

---

## Next
Turn this into a step-by-step implementation plan (`writing-plans` skill), sliced as above, test-first.
