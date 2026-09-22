# Pantheon — Agent Context

Start here. This file tells a fresh agent what this repo is, how the engine is
architected, and how to work with the human on it. It is an **index + locked
decisions**, not a full spec — the linked docs hold the detail.

Pantheon is a turn-based tactical card game (Hearthstone-style combat, rival
mythological pantheons). It is a **learning pet project** for a small (~3 person)
team — the human writes the code to learn game dev; agents guide and pair.
Stack: **C# / .NET, Unity 6, NUnit** tests, asmdef isolation.

---

## How to read this repo (progressive disclosure)

**This file is the map, not the territory. Read it in full — it is short — then
stop.** Everything below links out to detail. Open a linked doc, or read a source
file, **only when your current task needs it** — the "Holds" / "when to open"
notes tell you which one. Do **not** preload every doc, read files you won't
touch, or pull in a whole `docs/` tree "for context." Load skills the same way:
only the one that matches the task in front of you. Pull detail on demand; keep
your context lean.

## Read first (canonical docs — don't duplicate them, update them)

| Doc | Holds | Open when |
|---|---|---|
| [`MVP.md`](MVP.md) | The current build target: headless combat engine, start→play→finish. | Deciding if a piece of work is in the current scope. |
| [`docs/design.md`](docs/design.md) | Game design — the *what & why*. Locked pillars, resource/faction model, data-driven cards. | Touching game rules, cards, effects, or a design question. |
| [`docs/research/state-architecture.md`](docs/research/state-architecture.md) | Deep dive: mutable state + Clone, AI search, seeded RNG. | You need the *reasoning* behind decisions 1–2 below, not just the rule. |
| [`docs/research/server-and-netcode.md`](docs/research/server-and-netcode.md) | Deep dive: authoritative server vs lockstep, one-repo-many-builds. | Working on netcode / server — parked for MVP. |

---

## Engine architecture — LOCKED

These were resolved by grilling + research; treat as settled. Each: **decision —
why — what NOT to do.**

1. **State model: mutable entities + `GameState.Clone()`.**
   Entities mutate in place (e.g. `CardInstance.TakeDamage`). AI does forward
   simulation by cloning the whole state, applying a candidate move, scoring,
   discarding (clone→apply→score→discard).
   *Why:* avoids spreading a copy-tax across all gameplay code; precedent is
   SabberStone (open-source HS sim).
   *Don't:* make entities immutable, or return new-state snapshots from every rule.

2. **Randomness: ONE seeded PRNG owned by `GameState`.**
   Every random draw routes through it; the seed is stored in the game record, so
   the game is `pure(seed, ordered moves)` → replay + deterministic tests.
   *Don't:* ever call `System.Random` / `UnityEngine.Random` ad hoc, and never roll
   randomness in the view.

3. **Events: FAT semantic events.**
   A semantic name (`MinionAttacked`) carrying its deltas as payload (health 3→1).
   Plain data records appended to a `List<GameEvent>` log on `GameState`.
   *Why:* the view renders with zero rules knowledge; netcode still gets exact numbers.
   *Don't:* use the C# `event` keyword or `UnityEvent`; don't emit semantic-only
   (view must re-run rules) or delta-only (loses the "why" for animation).

4. **Core is a SYNCHRONOUS step function.**
   `StartGame` / `GetValidMoves` / `ApplyMove`. Pure, Unity-free, trivially testable
   like a stateless service. `ApplyMove` mutates state and returns fat events.
   *Don't:* put Unity types, async, or I/O inside Core.

5. **Input seam: ASYNC Runner + `IAgent`.**
   The Runner runs the loop and `await`s `IAgent.DecideAsync(view) → Task<Move>`.
   Implementations: `LocalPlayerAgent`, `AiAgent`, `NetworkAgent` — **swap the
   agent, not the loop**, so PvE and PvP are the same engine. `Move` = the network
   request; `List<GameEvent>` = the network response. AI returns an
   already-completed task (`Task.FromResult`), keeping the seam async for free.
   *Turn-based:* exactly **one** agent acts per turn (the current player). Never
   await both agents in parallel.
   *Data flow:* Runner→Agent = state in; Agent→Runner = a `Move`; Runner→Core =
   `ApplyMove`; Runner→View = events. The agent returns a Move, **not** state, and
   never talks to the view.

6. **GameRunner orchestrates; Core owns the rules.**
   The Runner is dumb loop-glue: ask current agent → `ApplyMove` → dispatch events →
   repeat until a winner. Rules logic lives in Core, **not** in the Runner.

7. **Server: authoritative server + thin client (NOT deterministic lockstep).**
   Rules live once as `Pantheon.Core` (a DLL) referenced by both the Unity client
   build and a separate headless `Pantheon.Server` build. You deploy **builds, not
   the repo**: Unity build → players; `dotnet publish` of the server → a cloud
   container running only .NET + Server DLL + Core DLL (no Unity/assets/editor).
   The server IS the Runner with `RemotePlayerAgent`s awaiting move packets. PvE
   runs Core locally; PvP runs authoritative Core on the server. Hidden info =
   per-recipient filtered events. The client may call read-only `GetValidMoves()`
   for UI but never applies moves authoritatively.
   *Repo layout:* monorepo, `Pantheon.Server` **outside** `Assets/`.
   *Status:* parked — MVP is headless/local. Accounts/login are a separate platform
   layer (Steam identity / BaaS), out of the engine, using fake identities for now.

**One combat core, mode-agnostic.** Only the *opponent brain* (`IAgent`) and the
*meta-wrapper* (ladder / campaign / roguelike map) vary. See design.md.

---

## Code layout & current state

```
Assets/Scripts/Core/
  Cards/        CardDefinition (immutable blueprint), CardInstance (mutable runtime),
                CardPrice, CardType
  GameState/    GameState, IGameState, GameStateFactory
  GameRunner/   GameRunner, IGameRunner
  Agents/       IAgent, Agent
Assets/Tests/   Cards/*, GameRunner/*   (NUnit)
```

- **asmdef isolation:** `Pantheon.Core` has **No Engine References** — keep it
  Unity-free so it compiles headless and stays testable.
- **In progress (TDD):** `GameRunner`. Constructor DI + guard clauses + tests done.
  Next seams: `RunAsync()` loop (async, one agent per turn) and where `ApplyMove`
  lives (in Core, not the Runner). `Move` shape not yet designed.

---

## Working style (how to pair with the human)

- **The human writes the code, to learn.** Guide, give boilerplate / test
  scaffolding / API shapes / hints, and **only write implementation logic when
  explicitly asked.** Docs like this one are fair game to write.
- **TDD:** test at pre-agreed **seams** (public interfaces), confirm the seam first,
  work in **vertical slices** (one test → one impl → repeat), red before green.
- **Pace:** conversational, one thing at a time. The human comes from PHP web dev —
  explain C#/.NET idioms that differ (async/`Task`, guard clauses, arrays).

---

## Suggested skills (call via the Skill tool)

- `mattpocock-skills:tdd` — the active workflow for building the engine.
- `mattpocock-skills:domain-modeling` — when shaping `Move`, `GameEvent`, or Core seams.
- `superpowers:brainstorming` — before any new feature/design (loop, effect system).
- `superpowers:systematic-debugging` — any bug / Unity-won't-compile / test failure.
- `mattpocock-skills:grilling` — to stress-test a design decision before committing to it.
