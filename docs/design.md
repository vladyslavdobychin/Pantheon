# Pantheon — Game Design

The **what & why** of the game (living doc). Separate from *how the engine is built* (see [`architecture/overview.html`](architecture/overview.html)) and from *MVP scope* (see [`../MVP.md`](../MVP.md)). Numbers are placeholders — balance is a later pass.

> Context: small team pet project to learn game dev + game design. Theme and pillars are locked; content and tuning are parked.

---

## Vision & pillars

- **Genre:** turn-based tactical card game with a **persistent creature board** (Hearthstone-style combat).
- **Core fun:** **engine-building** — combining cards into combos and synergies.
- **Pillar (the tie-breaker):** *cards are cogs; the fun is assembling engines, not trading raw stats.* When two options conflict, favor the one that rewards synergy.
- **Theme:** rival mythological pantheons (Greek, Egyptian, …). **Load-bearing** — the hook and the mnemonic that makes a large card pool learnable and distinct.

## Resource system — LOCKED

- **Incrementing mana** (HS-style): start 1 max, +1 per turn, refill each turn, cap ~10.
- **Engine-building lives in card design** (cost-reducers, resource generators, draw engines, "spend → trigger" payoffs), **not** in the resource model. Keeps a simple base that fits a persistent board.

## Factions — LOCKED

- **Light asymmetry:** one shared rules engine; each pantheon is a **card pool** with its own signature keywords/mechanics.
- A pantheon is **not** locked to a playstyle (like HS classes / MTG colors) — aggro, control, and combo are all buildable within one.
- Each pantheon needs **depth** across the aggro ↔ control axis. **Start with one deep pantheon (Greek);** one deep beats two shallow.

## Cards & effects — data-driven, LOCKED

- **Cards are data, not code:** `{ cost, type, attack, hp, abilities: [ { trigger, effect(s) } ] }`.
- **Trigger** = *when*: on-play (**Battle Cry**), on-death (**Deathrattle**), start/end of turn, on-attack, passive/aura.
- **Effect** = *what*: from a fixed primitive library (deal damage, heal, buff, summon, draw, give keyword, destroy, …).
- **Keyword** = a named bundle of trigger + effect (Taunt, Rush, Deathrattle, Freeze, …).
- **Why:** a new card = a data row reusing existing effects → little to no new code; non-programmers can author cards; each effect is tested once.
- **Interaction model:** pure turn-based, HS-style — no instant-speed responses on the opponent's turn, no priority/response system.

## Ruleset defaults (HS-derived; placeholder numbers)

- **Hero:** HP (placeholder 30); 0 HP → you lose; both at 0 → draw.
- **Hero Power:** one signature ability, once per turn, fixed mana cost. *Deferred until effects exist.*
- **Deck & hand:** ~30 cards, max 2 copies each. Draw 1 at start of turn. Opening hand ~3–4 with a mulligan. Hand cap ~10 (overdraw burns).
- **Fatigue:** empty deck → escalating self-damage per draw, so games always terminate.
- **Board:** persistent minions, max ~7 per side. **Summoning sickness** (can't attack the turn it's played, unless Rush/Charge).
- **Combat:** a minion attacks an enemy minion *or* the hero; **mutual simultaneous damage**; 0 HP dies. Taunt forces attackers to hit it first.
- **Card types:** Creatures (board), Spells (one-shot), Events/Epics (multi-turn), Hero + Hero Power. *MVP uses Creatures + Battle Cry/Deathrattle only.*

## Extensibility principle

The **combat core is identical across every mode**. Only two things vary:

| Swappable part | PvP | Story | Roguelike |
|---|---|---|---|
| **Opponent brain** | human over network | scripted AI | AI |
| **Meta-wrapper** | ladder / matchmaking | fixed encounter chain | procedural map + downtime |

Build combat as a standalone module that knows **nothing** about who the opponent is or which mode wraps it. Realized by the `IAgent` seam — see the architecture doc.

## Parking lot (deferred, not forgotten)

- **Story mode** — mythic campaigns (e.g. Troy); add cards between encounters.
- **Roguelike / Hades-like mode** — pick a god, encounter chain, downtime with other gods.
- **PvP networking** — plug a network opponent into the combat core.
- **Egyptian pantheon** — candidate identity: death/afterlife value engine.
- **Content raw material** — the teammate's card list (Zeus / Ares / Medusa / spells / events) for when content design starts.

---

*Related: [`../MVP.md`](../MVP.md) · [`architecture/overview.html`](architecture/overview.html) · [`research/`](research/)*
