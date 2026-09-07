# Server & Netcode Research: Authoritative Server + Shared-Core Builds

> **What decision this feeds.** Pantheon is a headless C#/Unity Hearthstone-like card engine. This document gathers primary-source evidence for the networking direction we have chosen: an **authoritative server + thin client**. The server holds the single source of truth for a match; clients send *intents* ("play card 3 on target 7") and *render* whatever the server tells them; clients never decide outcomes. The game rules live in one shared library, **`Pantheon.Core`**, which is compiled into **both** the Unity client build **and** a separate **headless .NET server build** — one repository, two build artifacts, two deploy destinations. The reader's specific "aha," which this reading reinforces: **you don't deploy a repo, you deploy builds.** The cloud runs only the compiled headless server plus the shared-rules DLL and a .NET runtime — never the Unity project, editor, or art assets. The five topics below establish (1) why an authoritative server is the right core model, (2) why the deterministic-lockstep alternative is a poor fit for a hidden-information card game, (3) how a headless server and shared client/server code actually work, (4) the one-repo/many-builds/many-targets mental model in concrete .NET terms, and (5) what the card-game-specific literature does and doesn't tell us. Every non-obvious claim is cited inline; a consolidated source list is at the end.
>
> A note for a reader coming from PHP web dev: the closest familiar analogy is "never trust the client, validate everything server-side" — but game netcode pushes it much further. In a web app the client submits a *form* and the server validates *that submission*; in an authoritative game the client submits an *intent* and the server runs *the entire game simulation*, then streams back results. The client is closer to a browser rendering server-sent HTML than to a SPA that owns state. Keep that framing and most of this document will click.

---

## Topic 1 — The authoritative server model (the core concept)

### 1.1 What "server-authoritative" means

An **authoritative server** is exactly what the name says: *"the one and only authority regarding everything that happens in the world is the server"* ([Gabriel Gambetta, *Client-Server Game Architecture*](https://www.gabrielgambetta.com/client-server-game-architecture.html)). You *"make everything in your game happen in a central server under your control, and make the clients just privileged spectators of the game"* ([Gambetta](https://www.gabrielgambetta.com/client-server-game-architecture.html)). Clients do two things and only two things: they *"send their actions to the server. The server updates the game state periodically, and then sends the new game state back to clients, who just render it on the screen"* ([Gambetta](https://www.gabrielgambetta.com/client-server-game-architecture.html)).

Valve's engine documents the same architecture for shipping games: *"Multiplayer games based on the Source Engine use a client-server networking architecture,"* where the server *"is a dedicated host that runs the game and is authoritative about the world simulation, the game rules, and the player input processing"* ([Valve Developer Community, *Source Multiplayer Networking*](https://developer.valvesoftware.com/wiki/Source_Multiplayer_Networking)). The client *"samples data from the input devices … and sends these input samples to the server,"* while receiving the current world state back — at a packet rate of *"usually 20 to 30 [snapshots] per second"* for a fast action game ([Valve](https://developer.valvesoftware.com/wiki/Source_Multiplayer_Networking)). (For a turn-based card game the rate is *much* lower — you send a message when something happens, not 30 times a second. See §1.4.)

### 1.2 Contrast with the alternatives

There are three broad models, and the historical arc runs through all of them ([Glenn Fiedler, *What Every Programmer Needs To Know About Game Networking*](https://gafferongames.com/post/what_every_programmer_needs_to_know_about_game_networking/)):

- **Peer-to-peer (P2P) / deterministic lockstep** — "in the beginning games were networked peer-to-peer, with each computer exchanging information with each other in a fully connected mesh topology" ([Fiedler](https://gafferongames.com/post/what_every_programmer_needs_to_know_about_game_networking/)). There is no central authority; every machine runs the whole simulation and they exchange only inputs. Still used by RTS games (Topic 2).
- **Client-authoritative** — the client computes outcomes and *tells* the server what happened. This is the model to avoid: any hacked client can simply lie. Edgegap's explainer frames the authoritative alternative precisely as the fix — the server *"has ultimate control over game state and player interactions,"* it *"processes all player inputs and enforces game rules,"* and clients send actions the server *"validates and broadcasts"* ([Edgegap, *Headless / Authoritative / Dedicated / P2P — what's the difference?*](https://edgegap.com/blog/headless-game-server-authoritative-game-server-dedicated-game-server-peer-to-peer-what-s-the-difference)).
- **Client-server with an authoritative server** — introduced for action games by Quake in 1996, where "each player was now a 'client'… they all communicated with just one computer called the 'server'," and critically **the server stays authoritative** even when clients guess ahead locally ([Fiedler](https://gafferongames.com/post/what_every_programmer_needs_to_know_about_game_networking/)). This is Pantheon's model.

### 1.3 Why authoritative = cheat-resistant

The whole strategy is: *"don't trust the player. Always assume the worst — that players will try to cheat"* ([Gambetta](https://www.gabrielgambetta.com/client-server-game-architecture.html)). Because the server holds the real state, a lying client gains nothing:

- If a hacked client claims 10000% health, it doesn't matter — *"the server knows it only has 10% — when the player is attacked it will die"* ([Gambetta](https://www.gabrielgambetta.com/client-server-game-architecture.html)).
- The client never sends absolute state, only intent: *"the server knows the player is at (10,10), the client tells the server 'I want to move one square to the right'"* ([Gambetta](https://www.gabrielgambetta.com/client-server-game-architecture.html)). The server decides whether that's legal and what results.

Unity's own netcode docs give the same rationale for choosing server authority: it provides *"a centralized authority to manage any potential game state conflicts"* and is used *"where having a central server authority is necessary to minimize cheating and the effects of bad actors"* ([Unity, *Netcode for GameObjects — Authority*](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.7/manual/terms-concepts/authority.html)). For a card game this is the entire ballgame: mana costs, whether a play is legal, what a random effect rolls, and — crucially — **what's in the opponent's hand** must all be adjudicated somewhere the player can't touch. That somewhere is the server.

### 1.4 Why turn-based games get authority *for free* (no prediction/rollback needed)

Fast action games pay a heavy tax for server authority: latency. If the client must round-trip every input to the server before seeing a result, movement feels laggy. The industry answer is a stack of techniques — **client-side prediction** (the client simulates the input immediately) plus **server reconciliation** (when the authoritative state arrives, the client "re-applies" its still-unacknowledged inputs on top of it), and **entity interpolation** for other players ([Gambetta, *Client-Side Prediction and Server Reconciliation*](https://www.gabrielgambetta.com/client-side-prediction-server-reconciliation.html); [Gambetta, *Entity Interpolation*](https://www.gabrielgambetta.com/entity-interpolation.html)). Unity notes the same trade-off: server authority "can come at the expense of adding latencies, because all state changes must be sent to the server game instance, processed, and then sent out to other game instances" ([Unity Authority](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.7/manual/terms-concepts/authority.html)).

**Here is the good news for Pantheon: none of that machinery is required for a turn-based card game.** Prediction, reconciliation, rollback, and interpolation all exist to hide the ~50–200 ms round-trip in a continuously-simulated world sampled 30–60 times per second. A card game is event-driven and asynchronous: a player takes an action every few *seconds*, and a round-trip of a couple hundred milliseconds before the card visibly resolves is imperceptible (and can be masked entirely by animation). The engine can therefore use the authoritative-server model in its *plainest* form — client sends intent → server validates and mutates state → server sends result events → client animates — with no local re-simulation. You get the cheat-resistance and the single-source-of-truth without paying the latency-hiding complexity tax. This is the single most important reason the authoritative model is *easier*, not harder, for Pantheon than for an FPS.

> ### 📌 Why this matters for Pantheon
> Build the server as the sole authority over the match: clients submit **intents** (`PlayCard`, `Attack`, `EndTurn`), the server validates them against `Pantheon.Core`'s rules, mutates the authoritative state, and emits result events. Do **not** implement client-side prediction, reconciliation, or rollback — those solve a real-time-latency problem you don't have. The mental model is deliberately close to a request/response web backend you already know, except the "request" is a game intent and the "response" is a stream of state-change events. Cheat-resistance and hidden-information safety fall out of the architecture, not from extra code.

---

## Topic 2 — Deterministic lockstep (the contrast, brief)

### 2.1 What it is

**Deterministic lockstep** is the P2P alternative to an authoritative server: *"a method of networking a system from one computer to another by sending only the inputs that control that system, rather than the state of that system"* ([Glenn Fiedler, *Deterministic Lockstep*](https://gafferongames.com/post/deterministic_lockstep/)). Every machine runs the **full simulation**; they exchange only the small stream of player commands and each independently arrives at the identical world. The payoff is bandwidth: *"bandwidth is proportional to the size of the input, not the number of objects in the simulation"* — you can "network a physics simulation of one million objects with the same bandwidth as just one" ([Fiedler](https://gafferongames.com/post/deterministic_lockstep/)).

This is the classic RTS technique. The canonical account is **Age of Empires**: rather than sync thousands of unit positions over a 28.8k modem, *"the expectation was to run the exact same simulation on each machine, passing each an identical set of commands"* — because just passing X/Y/status/facing/damage *"would limit us to 250 moving units in the game at the most"*, and they wanted thousands ([Bettner & Terrano, *1500 Archers on a 28.8: Network Programming in Age of Empires and Beyond*](https://www.gamedeveloper.com/programming/1500-archers-on-a-28-8-network-programming-in-age-of-empires-and-beyond); [PDF mirror](https://zoo.cs.yale.edu/classes/cs538/readings/papers/terrano_1500arch.pdf)). The machines *"synchronize their game watches … allow players to issue commands, and then execute in exactly the same way"* ([Bettner & Terrano](https://www.gamedeveloper.com/programming/1500-archers-on-a-28-8-network-programming-in-age-of-empires-and-beyond)).

### 2.2 Why it's fragile and cheat-prone

Lockstep demands **bit-exact determinism**: *"given the same initial condition and the same set of inputs your simulation gives exactly the same result … Exact down to the bit-level"* ([Fiedler](https://gafferongames.com/post/deterministic_lockstep/)). This is brutally hard to guarantee across machines: *"that does not necessarily mean it would also be deterministic across different compilers, a different OS or different machine architectures (eg. PowerPC vs. Intel)"* ([Fiedler](https://gafferongames.com/post/deterministic_lockstep/)), largely because of floating-point differences. And the failure mode is total: in AoE, *"one tiny difference results in complete desynchronization over time"* ([Fiedler, *What Every Programmer…*](https://gafferongames.com/post/what_every_programmer_needs_to_know_about_game_networking/)). Two more structural weaknesses: latency is gated by the slowest peer (everyone waits for all commands before advancing a turn), and — decisive for us — **there is no central authority and every machine has the full game state**, so hiding information and preventing cheating are inherently hard.

### 2.3 Why lockstep is the wrong fit for a hidden-information card game

Lockstep works for RTS because RTS state is (mostly) *public* — both players can, in principle, know everything on the map, so having the full simulation on every machine is acceptable. A card game is the opposite: **your opponent's hand and deck order are secret**. In a pure lockstep model every client would need the full state to simulate it, which means the secret cards would be *on the cheater's machine* — exactly what you cannot allow. Add the stochastic effects card games love ("summon a random minion," "discover a card") and you'd be fighting cross-platform determinism for no benefit you actually need. **For Pantheon, an authoritative server wins on every axis that matters**: it can withhold secret information (only the server knows the full state), it makes randomness trivially fair (the server rolls it), and it sidesteps the bit-exact-determinism nightmare because only one machine — the server — computes outcomes.

> ### 📌 Why this matters for Pantheon
> Understand lockstep mainly so you can *rule it out* with confidence. It's a beautiful fit for public-information, unit-heavy RTS games on a tight bandwidth budget, and a poor fit for a secret-hand card game. That said, one *idea* from lockstep carries over and is independently valuable: keep the simulation **deterministic and driven by a seeded RNG** so a match is a pure function of `(seed, ordered inputs)`. Pantheon wants that for replays, tests, and reconciliation — but it will run that deterministic sim on **one authoritative server**, not on every peer. (This dovetails with the event-sourcing/seeded-RNG direction documented in `state-architecture.md`.)

---

## Topic 3 — Headless / dedicated servers, and sharing code client↔server

### 3.1 What "headless" / "dedicated" means

A **dedicated server** is a build of the game that runs the simulation with **no rendering and no graphics** — a "computer that's optimized to run server applications," with the build stripping out the rendering pipeline, audio, textures, meshes, and shaders because "rendering and asset management processes occur unnecessarily when … executing server runtimes" ([Unity, *Introduction to Dedicated Server*](https://docs.unity3d.com/Manual/dedicated-server-introduction.html)). "Headless" is simply the same idea with the emphasis on the missing display: Edgegap defines a headless server as "essentially the same as dedicated servers but emphasizes the lack of a graphical interface," a dedicated server as "a standalone server running game logic without rendering graphics," and an authoritative server as one that "has ultimate control over game state." Their key point: these are **not mutually exclusive** — "Different Names, Same Concept" — one server can be headless *and* dedicated *and* authoritative at once ([Edgegap](https://edgegap.com/blog/headless-game-server-authoritative-game-server-dedicated-game-server-peer-to-peer-what-s-the-difference)). Pantheon's server is all three.

### 3.2 Two ways to build a headless server — and why Pantheon picks the second

**Option A — Unity Dedicated Server build target.** Unity ships a first-class "Dedicated Server" platform: you take *the same Unity project* and build it with the server subtarget (`-standaloneBuildSubtarget Server`), producing "a headless executable with no graphical interface." "The goal of the Dedicated Server build target is to reduce the resource demand of server builds, including the disk size, the size in memory, and the CPU usage," achieved by "stripping code and assets that aren't necessary for a server build" ([Unity, *Build your application for Dedicated Server*](https://docs.unity3d.com/Manual/dedicated-server-build.html); [*Introduction to Dedicated Server*](https://docs.unity3d.com/Manual/dedicated-server-introduction.html)). This is the standard path when your gameplay is deeply entangled with Unity systems (physics, `MonoBehaviour` lifecycles, NavMesh, colliders). Unity's [Netcode for GameObjects](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.7/manual/terms-concepts/authority.html) is designed for exactly this "single game instance that is defined as the server … responsible for running the main simulation."

**Option B — a plain headless .NET app that shares only the pure-logic library (Pantheon's choice).** Because a card game's rules are just *logic* — no physics, no NavMesh, no per-frame `Update()` — Pantheon can put all of them in a plain C# library (`Pantheon.Core`) with **zero Unity dependencies**, and host that library in an ordinary **.NET console/server application**. The server is then just a normal .NET program, not a Unity build at all. Trade-offs, honestly:

| | Option A: Unity Dedicated Server | Option B: plain .NET + shared logic lib (Pantheon) |
|---|---|---|
| **What the server actually is** | The Unity project, built headless | A plain `dotnet` app referencing `Pantheon.Core.dll` |
| **Engine coupling** | Server carries the Unity runtime | Server has **no** Unity runtime at all |
| **Reuse of engine features** | Physics/NavMesh/etc. available server-side | None available — but a card game needs none |
| **Server footprint & startup** | Heavier (Unity player, even stripped) | Very light (console app + .NET runtime) |
| **Testability of rules** | Must run inside Unity/PlayMode harness | Pure library → fast plain `dotnet test`, no engine |
| **Hosting/ops** | Unity-specific deploy & tooling | Standard .NET container/host tooling (Topic 4) |
| **Best when** | Gameplay is entangled with Unity systems | Gameplay is pure logic (card games, board games) |

For a Hearthstone-like game, Option B is the cleaner fit: the rules are pure logic, so keeping them in an engine-free library makes them trivially unit-testable and lets the server be a lightweight, standard .NET process. (Option A remains the right default for real-time, physics-driven games.)

### 3.3 Sharing game logic as ONE codebase compiled into both

Whichever option you pick, the golden rule is **one authoritative implementation of the rules**, not two. If the client re-implements "does this card kill that minion?" in its own code and the server does too, the two *will* drift, and drift between client and server prediction is the textbook cause of desync — the community-standard fix is to establish "a single source of truth," with "game state update logic [that is] deterministic … so that the server and client produce the same game state given the same input" ([Bugnet, *How to Fix Multiplayer Desync in a Game*](https://bugnet.io/blog/how-to-fix-multiplayer-desync-in-a-game); the general principle is the classic [single source of truth](https://en.wikipedia.org/wiki/Single_source_of_truth) idea). In .NET the mechanism is a shared **class library**: "a class library defines types and methods that are called by an application," and *multiple* projects consume it by adding a **project reference** to it ([Microsoft Learn, *Create a .NET class library*](https://learn.microsoft.com/en-us/dotnet/core/tutorials/library-with-visual-studio)). Concretely: `Pantheon.Core.csproj` holds the rules; both `Pantheon.Server.csproj` (the headless app) and the Unity client reference it, so both link the *same compiled rules DLL*. Rule duplication becomes structurally impossible.

Note the client and server use `Pantheon.Core` *differently*, even though it's the same code. The server runs it **authoritatively** to decide outcomes. The client may run *read-only* parts of it to answer "which of my cards are even legal to click right now?" for a responsive UI — but it never trusts its own answer; the server re-checks every intent. This is the plain-authoritative flow of §1.4, not client-side prediction: the client is asking the shared rules a *display* question, not deciding the *game*.

### 3.4 Hidden information: the server sends per-recipient filtered state

Because the server derives all state and *sends* it out, it can send **each player a different, filtered view** — the online version of "don't put secrets on the client." A player must see their own hand but not the opponent's; the server therefore transmits your opponent's draw as "opponent drew a card," never *which* card. This is the same instinct behind fog-of-war netcode, where the guiding rule is that **the server must not send a client any state it isn't entitled to see** — sending full state and hiding it in the UI is exploitable, so visibility must be enforced *at the server, on the wire*. Valve's engines implement this generally via the **PVS (Potentially Visible Set)**: the server sends a client only the entities relevant to it, both to save bandwidth and to deny wallhack-style cheats ([Valve, *Source Multiplayer Networking*](https://developer.valvesoftware.com/wiki/Source_Multiplayer_Networking)). For Pantheon the "PVS" is trivial and domain-specific: **your zone contents are visible to you; your opponent's hand and deck are not.** The reference engine SabberStone does the offline analog — its `POGame` wrapper replaces the opponent's unseen cards with dummy placeholders so an AI can't cheat by reading them (documented in `state-architecture.md`, §1.4); Pantheon's server does the *online* version by emitting a redacted event stream per recipient. Server authority is the precondition that makes this redaction trustworthy: the secret never leaves the server ([Gambetta](https://www.gabrielgambetta.com/client-server-game-architecture.html)).

> ### 📌 Why this matters for Pantheon
> Keep `Pantheon.Core` **engine-free** (no `UnityEngine` references) so it can be hosted equally by a plain headless .NET server and by the Unity client — that is what makes Option B possible and keeps the rules unit-testable without Unity. Compile the *one* library into *both* artifacts via project references; never re-implement a rule on the client. And design the server's outbound messages as **per-player filtered views from day one** — build a `GetStateFor(playerId)` / per-recipient event redaction step so the opponent's hand is *never serialized to the wrong client*. Treat "the client can render it hidden" as a bug, not a feature: if a secret reaches the client, it's already leaked.

---

## Topic 4 — One repo, multiple builds, multiple deploy targets (the reader's actual "aha")

### 4.1 The mental model: you deploy *builds*, not the *repo*

This is the crux the reader wanted reinforced. A monorepo is just *source organization*: one repository containing the shared library plus each app that consumes it. **Building** is a separate step that turns that source into deployable **artifacts**, and each artifact goes to its own destination:

```
Pantheon/                      (ONE git repo — source only)
├── Pantheon.Core/             → compiles to Pantheon.Core.dll   (shared rules; no Unity)
├── Pantheon.Server/           → `dotnet publish` → headless server build   ──► CLOUD
└── Pantheon.Client (Unity)/   → Unity build (client subtarget) → game build ──► PLAYERS
```

Both apps reference `Pantheon.Core`, so both *contain* the same rules DLL after building — but they are **two independent build outputs deployed to two different places.** The Unity editor, the `Assets/` folder, art, and scenes are *inputs to the client build*; they are **not** part of the server build and **never ship to the cloud.** The cloud receives only the compiled server + the shared rules DLL + the .NET runtime. This is a general software-engineering pattern, not a game-specific one, but it's the exact source of the confusion, so state it plainly: *the repository is what you edit; the build is what you deploy.*

### 4.2 .NET specifics: what `dotnet publish` actually produces

"Publishing a .NET app means compiling source code to create an executable or binary, along with its dependencies and related files, for distribution. After publishing, you deploy the app to a server … container, or cloud environment" ([Microsoft Learn, *.NET application publishing overview*](https://learn.microsoft.com/en-us/dotnet/core/deploying/)). The command `dotnet publish -c Release` "compiles your app to the *publish* folder" — for our server that folder contains `Pantheon.Server.dll` (your code as IL), `Pantheon.Core.dll` (the shared rules, pulled in by the project reference), a native launcher executable, a `.deps.json`, and a `.runtimeconfig.json` ([Microsoft Learn, *Containerize an app with Docker*](https://learn.microsoft.com/en-us/dotnet/core/docker/build-container)). Two publish modes decide whether the .NET runtime rides along ([Microsoft Learn, *publishing overview*](https://learn.microsoft.com/en-us/dotnet/core/deploying/)):

- **Framework-dependent** (default): the publish folder holds your app + dependencies but **not** the runtime — "the environment that runs the app must have a version of the .NET runtime installed." Smaller output; the host (or base container image) supplies .NET.
- **Self-contained** (`--self-contained true`): the publish folder "includes … the .NET runtime required to run the app. The environment that runs the app doesn't need to have the .NET runtime preinstalled." Bigger output; runs anywhere.

Either way, the *deployable unit is the publish folder*, not the `.csproj` / source tree. That folder is what you copy to a VM, or bake into a container.

### 4.3 Containerizing the server — the concrete "aha," in Docker terms

The Microsoft tutorial's Dockerfile makes the deploy-a-build-not-a-repo point unmistakably concrete. It uses a **multi-stage build**: a heavy `sdk` image compiles the source, then the final runtime image copies in *only the published output* ([Microsoft Learn, *Containerize an app with Docker*](https://learn.microsoft.com/en-us/dotnet/core/docker/build-container)):

```dockerfile
# --- build stage: has the full SDK, compiles source ---
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /App
COPY . ./
RUN dotnet publish -o out

# --- runtime stage: what actually ships/runs ---
FROM mcr.microsoft.com/dotnet/aspnet:9.0          # just the .NET runtime, no SDK, no source
WORKDIR /App
COPY --from=build /App/out .                        # copy ONLY the published build output
ENTRYPOINT ["dotnet", "Pantheon.Server.dll"]        # run the compiled server DLL
```

Read the runtime stage carefully — it is the whole lesson in five lines: the final image is `dotnet/aspnet:9.0` (**the runtime only**), into which you `COPY` **only the published build output**, and whose entrypoint is `dotnet <YourServer>.dll`. The SDK, the source tree, and — for Pantheon — the entire Unity project are absent from the shipped image. "The image can be used to create containers for your local development environment, private cloud, or public cloud" ([Microsoft Learn, *Containerize an app with Docker*](https://learn.microsoft.com/en-us/dotnet/core/docker/build-container)). The .NET SDK can even build that image with **no Dockerfile at all** via `dotnet publish -t:PublishContainer` / `--target PublishContainer`, which packages the app and its dependencies into an image against Microsoft's optimized runtime base images ([Microsoft Learn, *.NET publishing overview* — container deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/); [*Containerize a .NET app with `dotnet publish`*](https://learn.microsoft.com/en-us/dotnet/core/containers/sdk-publish)).

> ### 📌 Why this matters for Pantheon
> Lay the repo out as one solution with three projects: `Pantheon.Core` (engine-free rules), `Pantheon.Server` (headless .NET host, references Core), and the Unity client (also references Core). Ship the server as `dotnet publish` output — most simply a **container**: a slim `dotnet/aspnet` (or `dotnet/runtime`) base image with your publish folder copied in and `ENTRYPOINT ["dotnet","Pantheon.Server.dll"]`. Internalize the runtime stage of that Dockerfile as the corrective to the old confusion: **the cloud runs the compiled server DLL + the shared rules DLL + the .NET runtime — nothing else.** The Unity project, editor, and `Assets/` are inputs to the *client* build and never leave your build machine. One repo, two builds, two destinations.

---

## Topic 5 — Card-game / turn-based multiplayer specifically

### 5.1 What the sources say (and how thin they are)

There is a large, high-quality primary literature on **real-time** netcode (Gambetta, Fiedler, Valve, the Overwatch and WoW GDC talks) and on **RTS lockstep** (Age of Empires). There is comparatively **little rigorous, primary material specifically on hidden-information turn-based card-game netcode** — Blizzard has not published a Hearthstone networking talk of the caliber of the Overwatch one, and most of what circulates is forum discussion and blog explainers rather than conference talks or engineering papers. This is called out honestly in the gaps section; treat this topic's citations as weaker than Topics 1–4.

What the reputable-ish sources converge on is unsurprising given Topics 1 and 3: the standard architecture *is* an authoritative server with thin clients. Industry explainers describe the "Hearthstone model" as one where clients are essentially remote controllers — they tell the server what the player wants to do and display what happens on the server, while the server handles all the logic: authorizing moves, calculating damage, and activating effects ([Andrew Ching / We Are Mighty, *What are Server-authoritative Realtime Games?*](https://medium.com/wearemighty/what-are-server-authoritative-realtime-games-e2463db534d1)). That is exactly the plain-authoritative flow of §1.4 applied to a card game, and it aligns with the hidden-information filtering of §3.4 (a player sees their own hand; the server withholds everyone else's).

### 5.2 Existence proofs in the wild

Two open-source C#/.NET projects show this shape is real and buildable, though both come with caveats:

- **SabberStone** — a mature, engine-free C# Hearthstone *simulator* library. It isn't a networked server, but it *is* the reference for "all the rules in one pure-logic library you can host anywhere and unit-test without an engine" — precisely Pantheon's `Pantheon.Core`. (Covered in depth in `state-architecture.md`.) Its `POGame` hidden-information handling (§3.4) is the closest thing to a documented per-recipient state-filtering pattern in the Hearthstone ecosystem.
- **Firestone** — an open-source *"Hearthstone server implementation in .NET Core"*, explicitly a C#/.NET Core project ([HearthCode/Firestone](https://github.com/HearthCode/Firestone)). It demonstrates the "headless .NET server for a card game" concept concretely. **Caveat:** it is work-in-progress and, per its README, more of a lobby/server emulator than a complete authoritative game engine — use it as an *existence proof and layout reference*, not a blueprint.

### 5.3 The one card-game-specific nuance worth internalizing

The defining difference from action-game netcode is **stochastic, hidden-information state**, and it pushes two design choices that Topics 1–4 already imply: (1) **all randomness is server-side** (the server rolls "summon a random Beast" and tells the client the *result*, so a client can never bias or peek at an RNG it doesn't own), and (2) **the wire protocol is per-recipient and event-shaped** — the server emits domain events ("opponent played a 3-mana minion," "you drew Lightning Bolt") filtered so secrets never cross to the wrong client. Both are simply the card-game reading of "authoritative server + filtered state," which is why the *general* server-authority sources (Topics 1, 3) are actually the strongest guidance available for a card game, more so than the thin card-game-specific writing.

> ### 📌 Why this matters for Pantheon
> Don't wait for a definitive Hearthstone-netcode paper — it doesn't really exist in primary form. The correct architecture for Pantheon is fully specified by the *general* authoritative-server literature plus the card-game nuances above: authoritative server, thin clients, **all RNG server-side**, and **per-recipient event streams** that redact secret zones. Use SabberStone as the rules-library precedent and Firestone as a rough "headless .NET card server exists" data point, while remembering both are references, not gospel.

---

## Sources

**Topic 1 — Authoritative server model**
- Gabriel Gambetta, *Fast-Paced Multiplayer (Part I): Client-Server Game Architecture*: https://www.gabrielgambetta.com/client-server-game-architecture.html
- Gabriel Gambetta, *Client-Side Prediction and Server Reconciliation*: https://www.gabrielgambetta.com/client-side-prediction-server-reconciliation.html
- Gabriel Gambetta, *Entity Interpolation*: https://www.gabrielgambetta.com/entity-interpolation.html
- Valve Developer Community, *Source Multiplayer Networking*: https://developer.valvesoftware.com/wiki/Source_Multiplayer_Networking
- Glenn Fiedler, *What Every Programmer Needs To Know About Game Networking*: https://gafferongames.com/post/what_every_programmer_needs_to_know_about_game_networking/
- Unity, *Netcode for GameObjects — Authority*: https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.7/manual/terms-concepts/authority.html
- Edgegap, *Headless / Authoritative / Dedicated / Peer-to-Peer — what's the difference?*: https://edgegap.com/blog/headless-game-server-authoritative-game-server-dedicated-game-server-peer-to-peer-what-s-the-difference

**Topic 2 — Deterministic lockstep**
- Glenn Fiedler, *Deterministic Lockstep*: https://gafferongames.com/post/deterministic_lockstep/
- Glenn Fiedler, *Snapshot Interpolation* (state-based contrast to lockstep): https://gafferongames.com/post/snapshot_interpolation/
- Bettner & Terrano, *1500 Archers on a 28.8: Network Programming in Age of Empires and Beyond* (Game Developer / Gamasutra, 2001): https://www.gamedeveloper.com/programming/1500-archers-on-a-28-8-network-programming-in-age-of-empires-and-beyond (PDF mirror: https://zoo.cs.yale.edu/classes/cs538/readings/papers/terrano_1500arch.pdf)

**Topic 3 — Headless/dedicated servers & shared code**
- Unity, *Introduction to Dedicated Server*: https://docs.unity3d.com/Manual/dedicated-server-introduction.html
- Unity, *Build your application for Dedicated Server*: https://docs.unity3d.com/Manual/dedicated-server-build.html
- Unity, *Dedicated Server* (manual root): https://docs.unity3d.com/Manual/dedicated-server.html
- Microsoft Learn, *Create a .NET class library*: https://learn.microsoft.com/en-us/dotnet/core/tutorials/library-with-visual-studio
- Bugnet, *How to Fix Multiplayer Desync in a Game* (single source of truth / shared deterministic logic): https://bugnet.io/blog/how-to-fix-multiplayer-desync-in-a-game
- Wikipedia, *Single source of truth*: https://en.wikipedia.org/wiki/Single_source_of_truth

**Topic 4 — One repo, multiple builds, multiple targets**
- Microsoft Learn, *.NET application publishing overview* (framework-dependent vs self-contained; container deployment): https://learn.microsoft.com/en-us/dotnet/core/deploying/
- Microsoft Learn, *Containerize an app with Docker* (multi-stage Dockerfile, copy publish output, ENTRYPOINT): https://learn.microsoft.com/en-us/dotnet/core/docker/build-container
- Microsoft Learn, *Containerize a .NET app with `dotnet publish`* (no Dockerfile): https://learn.microsoft.com/en-us/dotnet/core/containers/sdk-publish

**Topic 5 — Card-game / turn-based multiplayer**
- Andrew Ching / We Are Mighty, *What are Server-authoritative Realtime Games?* (the "Hearthstone model" of clients as remote controllers): https://medium.com/wearemighty/what-are-server-authoritative-realtime-games-e2463db534d1
- HearthCode/Firestone, *Hearthstone server implementation in .NET Core (WIP)*: https://github.com/HearthCode/Firestone
- (Rules-library precedent) HearthSim/SabberStone — see `state-architecture.md` for full treatment: https://github.com/HearthSim/SabberStone

---

## Recommended reading order (for a learner)

1. **Gambetta, *Client-Server Game Architecture*** — the gentlest possible intro to "authoritative server, clients send inputs, clients are spectators." Read only Part I first; it establishes the whole mental model. https://www.gabrielgambetta.com/client-server-game-architecture.html
2. **Valve, *Source Multiplayer Networking*** (intro section) — the same model as shipped in a real engine, in a few paragraphs; note "the server is authoritative about world simulation, game rules, and player input." https://developer.valvesoftware.com/wiki/Source_Multiplayer_Networking
3. **Fiedler, *What Every Programmer Needs To Know About Game Networking*** — the historical arc P2P-lockstep → client-server; skim, don't study. https://gafferongames.com/post/what_every_programmer_needs_to_know_about_game_networking/
4. **Fiedler, *Deterministic Lockstep*** + **Bettner & Terrano, *1500 Archers*** — read these together to understand the model Pantheon is *not* using, and *why* (fragile determinism, no authority, public-info assumption). https://gafferongames.com/post/deterministic_lockstep/ · https://www.gamedeveloper.com/programming/1500-archers-on-a-28-8-network-programming-in-age-of-empires-and-beyond
5. **Unity, *Introduction to Dedicated Server*** — what "headless" concretely means (rendering/assets stripped), even though Pantheon uses the plain-.NET variant. https://docs.unity3d.com/Manual/dedicated-server-introduction.html
6. **Microsoft Learn, *.NET application publishing overview*** — framework-dependent vs self-contained; internalize "you deploy the publish folder, not the source." https://learn.microsoft.com/en-us/dotnet/core/deploying/
7. **Microsoft Learn, *Containerize an app with Docker*** — read *only* the Dockerfile's runtime stage; it is the "deploy a build, not a repo" lesson in five lines. https://learn.microsoft.com/en-us/dotnet/core/docker/build-container
8. **Gambetta Parts II–III** (*Prediction/Reconciliation*, *Entity Interpolation*) — **optional, for context only.** Read so you can recognize the latency-hiding machinery Pantheon deliberately *skips* because it's turn-based. https://www.gabrielgambetta.com/client-side-prediction-server-reconciliation.html

---

## Gaps / conflicting sources

- **Topic 5 primary sources are genuinely thin.** There is no Blizzard "Hearthstone netcode" GDC talk or engineering paper comparable to the Overwatch/WoW networking talks. The card-game-specific citations here (We Are Mighty; Firestone) are industry-blog / open-source-repo quality, not conference talks or papers — deliberately flagged as weaker. The strongest guidance for a card game actually comes from the *general* authoritative-server sources (Topics 1 & 3), which the card-game framing simply specializes. No source *conflicted* with the authoritative-server recommendation; they differed only in rigor.
- **Valve wiki could not be auto-fetched.** `developer.valvesoftware.com` returns HTTP 200 to real browsers but is behind an **Anubis proof-of-work anti-scraping challenge**, so WebFetch/curl receive the challenge page rather than the article. The URL is live and canonical; the quotes used here ("client-server networking architecture," "authoritative about the world simulation, the game rules, and the player input processing," "20 to 30 [snapshots] per second") come from the search engine's index of the page and match the well-known canonical text. The PVS (Potentially Visible Set) per-client-visibility detail in §3.4 is attributed to Valve's Source networking model as an established engine behavior; I could not re-verify its exact wording against the live page due to the same wall — treat the PVS *wording* as paraphrase, though the concept is well-documented Valve behavior.
- **We Are Mighty article could not be re-fetched directly** (HTTP 403 to the automated fetcher). Its "clients as remote controllers / server handles all logic" characterization of the Hearthstone model came from the search engine's summary of the page; the URL resolves for normal browsers. Because it's a secondary blog source anyway, I lean on it lightly and only where it echoes the primary authoritative-server sources.
- **Firestone is WIP and lobby-focused.** Its README describes a Hearthstone *server/lobby emulator* in .NET Core that the authors state is incomplete ("We just started on it"), not a finished authoritative game engine. It's cited as an existence proof of the "headless .NET card server" shape, not as an architecture to copy.
- **Prediction/reconciliation deliberately excluded, not overlooked.** Several strong sources (Gambetta II–III, Unity Authority) spend most of their length on client-side prediction, reconciliation, and rollback. This document intentionally treats those as *out of scope* for Pantheon because it is turn-based (§1.4); that is a design decision, not a gap in the sources.
- **`.deps.json` / publish-folder contents** are quoted from the .NET 9 Docker tutorial's example listing; exact filenames vary slightly by .NET version and project type, but the structure (app DLL + shared-lib DLL + launcher + `.deps.json` + `.runtimeconfig.json`) is stable across recent .NET.
