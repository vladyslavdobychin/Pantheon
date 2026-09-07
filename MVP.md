# Pantheon — MVP

**Goal:** a headless combat engine that plays a full Hearthstone-style match from start to finish, runnable in the **console / tests** — no Unity, no graphics, no network. Proving the game can *start, play, and finish* is the whole point; balance is not.

## In scope

- **Two identical decks**, one per player.
- **Three creature cards:**
  - **2/3** — vanilla, no effect.
  - **1/1** — **Battle Cry** (an `OnPlay` effect).
  - **1/2** — **Deathrattle** (an `OnDeath` effect).
- **Heroes:** pure avatars — HP only. No hero power, no abilities.
- **Basic AI** drives both sides, so a full match runs headless to a winner.

## Out of scope

- Unity view, rendering, player input.
- Networking / online PvP, accounts, matchmaking.
- Spells, hero powers, keywords beyond the two effects above, card collection.
- Balancing — all numbers are placeholders.

## Done when

- A match can **start → play → finish** with a winner, driven by AI, in a single test/console run.
- The two effects fire correctly: **Battle Cry** on play, **Deathrattle** on death.

---

*Builds on the locked architecture — see [`docs/architecture/overview.html`](docs/architecture/overview.html): sync Core step function + Runner + `IAgent` seam + fat events + seeded RNG + mutable state with `Clone()`.*
