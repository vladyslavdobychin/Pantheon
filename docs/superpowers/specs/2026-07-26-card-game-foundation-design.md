# Card Game — Foundation Design

**Status:** Brainstorm in progress — Layers 1–2 locked · Layer 3 (Systems) in progress.
**Last updated:** 2026-07-26
**Team:** 3 people. **Goal:** pet project — learn game dev + game design.
**Working title:** Pantheon (repo folder: `card_mvp`).

> This is a *living* document. We fill it top-down as the brainstorm proceeds. It becomes the final foundation spec once Layers 1–3 are settled.

---

## How to read this — design altitude

Game design has layers, high → low. We design **top-down**: lock the structure before naming any card. Specific cards and exact numbers come last.

| Layer | Answers | Status |
|---|---|---|
| 1. Vision / Pillars | what experience, what fantasy, why fun | **LOCKED** |
| 2. Core loop | what the player does each turn / each match | **LOCKED** |
| 3. Systems & economy | resources, win/lose, board rules, progression | in progress |
| 4. Content | specific cards, gods, spells | parked |
| 5. Tuning | exact numbers | parked |

The teammate's card notes live at layers 4–5. They're preserved as raw material and parked until 1–3 are done — you can't balance a card before the economy it plugs into exists.

---

## Layer 1 — Vision (LOCKED)

- **Genre:** tactical, turn-based card game with a **persistent creature board** (Hearthstone-style combat).
- **Core experience / fun:** **engine-building** — combining cards into combos and synergies.
- **Design pillar (the tie-breaker):** *Cards are cogs; the fun is assembling engines, not just trading blows.* When two design options conflict, favor the one that rewards synergy over raw stats.
- **Theme:** rival mythological pantheons (Greek, Egyptian, …). Theme is **load-bearing**, not decoration — it's the hook and the mnemonic that makes a large card pool learnable and distinct.

---

## Architecture principle (the extensibility requirement)

The **combat system** is identical across every mode we might ever build. Only two things vary by mode:

| Swappable part | PvP | Story mode | Roguelike mode |
|---|---|---|---|
| **Opponent brain** | human over network | scripted AI | AI |
| **Meta-wrapper** | ladder / matchmaking | fixed encounter chain | procedural map + downtime |

**Requirement:** build the combat core as a standalone module that knows **nothing** about who the opponent is or which mode wraps it. Interface shape: `state in → legal moves out → apply move`.

- An **opponent** is a pluggable agent (scripted AI, network peer, or local human).
- A **mode** is a wrapper that sets up matches and carries state between them.

This keeps all future modes open **without designing them now**. First opponent = **scripted AI** (solo-testable, and reused as-is by Story/Roguelike modes). **No networking** in the first slice.

---

## Faction model (LOCKED)

- **Light asymmetry:** one shared rules engine; each pantheon is a card pool with its own **signature keywords/mechanics**.
- A pantheon is **not** locked to a playstyle. Like Hearthstone classes / Magic colors: within one pantheon you can build aggro, control, or combo.
- **Consequence:** each pantheon needs **depth** — enough cards across the aggro ↔ control axis (cheap attackers, defensive/stall cards, value/draw engines, payoffs). One deep pantheon beats two shallow ones → start with one.

---

## First slice — scope (the walking skeleton)

Goal: prove the combat core end-to-end with the **absolute minimum**. **No card abilities at all** — vanilla creatures only. This slice is deliberately *not fun*; it proves the engine works so effects can be layered on afterward.

**In scope (v1):**
- One shared rules engine, persistent-board combat, incrementing mana.
- **Vanilla creatures only:** a creature can be *played* (pay cost → enters board), can *attack* (mutual damage), can *take damage and die*. Summoning sickness applies.
- Two heroes with HP; reduce enemy hero to 0 → win. Draw 1/turn; fatigue on empty deck.
- One small test deck of stat-stick Greek creatures (varied cost/atk/hp).
- **Scripted AI** opponent, single match, no meta-wrapper.
- The data-driven ability framework is *present* (cards carry an `abilities[]` field) but the **effect library is empty**.

**Out of scope (v1 — deferred until the first effect lands):**
- All card abilities, triggers, keywords.
- **Hero Power** — it needs an effect to do anything (e.g. Zeus's power needs "deal damage"), so it's inert until the first effect exists.
- **Spells** and **Events** — they *are* effects.
- Networking, other modes, second pantheon.

---

## Layer 2 — Core loop (IN PROGRESS)

Two nested loops:

- **Turn loop:** draw → spend resource to play cards → attack → pass → opponent's turn.
- **Match loop:** reduce the opponent's life to 0 → win. (Later, in modes: carry the result into the next encounter.)

**Combat model:** persistent creature board — minions with Attack/HP that stay on the table and trade blows (HS-style, not StS's transient enemies). *Details TBD in Layer 3.*

### Resource system — LOCKED: Option C

Decides whether the game feels **tempo** (HS) or **engine** (StS).

- **A) Incrementing mana (HS):** start 1 max, +1 per turn, refill each turn, cap ~10. Costs 1–10 — matches the «Подношения» in the notes.
- **B) Fixed energy (StS):** flat ~3/turn, cheap cards, thin fast-cycling deck. Maximal combo feel, but built for a game with *no persistent enemy board* → fights our combat model, high balance risk.
- **C) A + engines-via-cards (RECOMMENDED):** incrementing-mana base (simple, AI-friendly, keeps the notes valid), with engine-building living in **card design** — cost-reducers, resource generators (the «земледелец» already does this), draw engines, "spend → trigger" payoffs.

**Decision: C (locked 2026-07-26).** Preserves the engine pillar on a simple base that fits a persistent board — engine-building comes from synergy, not from the resource model.

---

## Layer 3 — Ruleset skeleton (LOCKED — HS defaults)

Adopted from Hearthstone defaults. All numbers are Layer-5 placeholders — revisit in tuning.

**Hero**
- Each player has a hero with HP (placeholder 30). Hero at 0 HP → you lose. Both at 0 simultaneously → draw.
- **Hero Power (Option A):** each hero has one signature ability, usable **once per turn** for a fixed mana cost. Defines hero identity and gives consistent engine fuel (e.g. Zeus «Удар молнии»: 2 mana, 1 dmg). Upgradeable powers can layer on later as a card that swaps the power.

**Deck & hand**
- ~30-card deck, max 2 copies per card.
- Draw 1 at the start of each turn. Starting hand ~3–4 with a mulligan. Hand cap ~10 (overdraw burns).
- Empty deck → fatigue (escalating self-damage per draw) so games always terminate.

**Board**
- Persistent minions, max ~7 per side.
- **Summoning sickness:** a minion can't attack the turn it's played unless it has a Rush/Charge keyword.

**Combat resolution**
- A minion attacks an enemy minion *or* the enemy hero.
- **Mutual damage:** attacker and defender deal their Attack to each other simultaneously; 0 HP → dies.
- **Taunt/Provoke** forces enemy attackers to hit it first.

**Card types**
- **Creatures** (persistent board), **Spells** (one-shot effect), **Events/Epics** (multi-turn effects), **Hero + Hero Power**.
- *v1 uses only Creatures* (Spells/Events need effects — deferred).

### Card & effect model (data-driven — LOCKED)

Cards are **data, not code**. A card = `{ cost, type, attack, hp, abilities: [ { trigger, effect(s) } ] }`.

- **Trigger** = *when* (on-play, on-death, start/end of turn, on-attack, passive/aura).
- **Effect** = *what*, from a fixed primitive library (deal damage, heal, buff, summon, draw, give keyword, destroy, …).
- **Keyword** = a named bundle of trigger + effect (Taunt, Rush, Shield, Deathrattle, Freeze).

Why: a new card = a data row reusing existing effects → **no new code**. Non-programmers can author cards. Each effect is tested once. This *is* the extensibility requirement, realized.

**Interaction model:** pure turn-based — no instant-speed responses on the opponent's turn (HS-style). No priority/response system.

**v1 effect library = EMPTY.** The framework ships in slice 1 with zero effects (see First-slice scope); effects are added incrementally afterward. Post-v1, defer the machinery-heavy ones: random-miss, mind-control, opponent cost-manipulation, off-board hiding, damage reflection.

---

## Parking lot (deferred, not forgotten)

- **Story mode** — mythic campaigns (e.g. Troy); add cards between encounters.
- **Roguelike / Hades-like mode** — pick a god, encounter chain, downtime with other gods for plot + power-ups.
- **PvP networking** — added later by plugging a network opponent into the combat core.
- **Egyptian pantheon** — candidate identity: death/afterlife value engine — but multi-archetype, same as Greek.
- **Teammate's card list** (Zeus / Ares / Medusa / spells / events) — layer-4 raw material for when we design content.

---

## Next

Confirm the v1 walking-skeleton scope and the trivial v1 AI, then move to the implementation plan (writing-plans skill).
