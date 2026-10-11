# Feature parity with MoonBase Commander — the plan

How **MbcPrototype** gets from "a turn-based node-launching prototype" to a
build where someone who knows the 2002 original can sit down and find every
mechanic, unit, rule, mode and interface affordance they expect, behaving the
way they remember.

Companion documents:

- [units.md](units.md) — the original's eighteen units, the energy economy, the
  cord rules and the world rules, transcribed from the official manual. **Read
  that first**: it is the specification this plan implements.
- [../camera/README.md](../camera/README.md) — the existing camera/minimap
  documentation, which the map work below will rewrite.
- [../../AGENTS.md](../../AGENTS.md) — the project guide; keep it in step as
  this plan lands.

---

## 1. What "parity" means here

Parity is not "looks like MoonBase Commander". It is four separate bars, and
they are worth reaching in this order, because each one is useful on its own:

| bar | meaning | reached when |
| --- | --- | --- |
| **Rules parity** | same mechanics, same numbers, same edge cases | a player's expectations about *what happens* are never wrong |
| **Content parity** | all 18 units, 4 factions, the terrain and pool content | a player's favourite strategy exists |
| **Mode parity** | skirmish setup, challenge missions, replays, map editor, multiplayer | a player can play the way they used to |
| **Presentation parity** | HUD layout, key commands, audio, damage feedback | the game *reads* the same at a glance |

### Non-goals, and what replaces them

Some of the original is period infrastructure rather than game design. Copying
it literally would be waste:

| original | replacement |
| --- | --- |
| GameSpy Arcade matchmaking | Godot high-level multiplayer (ENet) + LAN discovery |
| DirectX 8.1, 640×480, CD install, AutoPlay | nothing — we are a Godot 4.5.1 build |
| Pre-rendered 2D sprites of 3D models | the existing 3D pipeline |
| The original's music/voice assets | *not redistributable.* Reproduce the dynamic-music **behaviour** (intensity tracks game state) with new audio, or ship silence |

Two things are deliberately **not** non-goals even though they are expensive,
because they are load-bearing for the feel of the game: **the wrapping map** and
**fog of war**. Both change how every other rule is played.

### Where numbers are unknown

The manual publishes armor, damage, energy costs and the objective. It does
**not** publish launch ranges, cord lengths, flight arcs, pool sizes, terrain
heights or bot personalities. Those are "tuned to feel" and belong in one
tuning table so they can be argued about and changed in one place (see
§7). Do not scatter them.

---

## 2. Where the prototype stands

Verified against the code today. The right-hand column is the gap this plan
closes; the **size** column is a rough ordering device, not a promise.

| system | original | prototype today | size |
| --- | --- | --- | --- |
| **Objective** | destroy the opponent's **main Hub** | destroy the enemy chain's root — `GameManager.EvaluateGameOver()` treats "no root" as defeat | ✅ aligned |
| **Cord network** | the spatial system: reach, overlap rules, cord nodes, cascade, lights | implicit parent/child links only (`BaseNode.ParentBase`/`Children`, `CableShader`). No cord geometry, no reach, no overlap test, no cord nodes | **XL** |
| **Damage model** | subtractive **Armor** (1–5), flat integer Damage | integer HP, `BaseNode.MaxHealth = 6`, two-band blast falloff in `Bomb.ApplyBlastDamage()` | **L** |
| **Energy** | 1/3/7 per unit, max 35, per-round refill, income units | none | **L** |
| **Turn model** | a turn is an energy budget; round ends when all have spent or skipped; order rotates left | one launch per turn; `CurrentTurn` counts rotation wraps | **L** |
| **Units** | 18, three levels | 2: `AmmoType { Node, Bomb }`, hardcoded in `AmmoLauncher.Launch()` (`gm.ProjectileScene` / `gm.BombScene`) | **XL** |
| **Players** | up to 4, teams, shared line of sight | exactly 2, hardcoded in `GameManager.CreateCombatants()` | **L** |
| **Map** | authored, sized, four terrain states, water, wrap-around | flat 10000×10000 plane; play area approximated by `MinimapWorldSize = 475` | **XL** |
| **Terrain height** | Water/Ground/Plateau/Mountain; height changes launch range | none — everything sits at y=0 | **L** |
| **Purple pools** | Energy Pools; size determines how many Collectors fit | `PoolField` scatter, decorative, `PoolAt`/`OverlappingPools` written but **never called** | **S** |
| **Fog / vision** | option; black unseen terrain; Tower/Balloon vision | none | **L** |
| **Wind** | intensity option, direction random per game, bends shots | none | **M** |
| **Anti-air / shields / EMP** | interception and disable pillars | none | **L** |
| **AI** | 10 BOTs with distinct play styles | one `EnemyCombatant`: random aim in a 25° cone, random power, alternates node/bomb | **L** |
| **Modes** | Skirmish, Challenge (16 missions), LAN, GameSpy | one hardcoded skirmish | **XL** |
| **Map editor** | size, 4 terrain states, pools, starting positions, save/load/share | none | **L** |
| **Replays / stats** | Replay Theatre + 7-stat game summary | none; no serialization of game state at all | **L** |
| **HUD / keys** | energy bar, build menu, HUD bar, D/R/C/N/P/L/A/Tab keys | power bar, two ammo icons, turn label, win/lose labels; `fire_shot`, `aim_left/right`, `switch_ammo_next/previous` | **M** |
| **Factions / colors** | 4 factions × 6 colors | none | **S** |

### The architectural blockers

These are why the plan starts with a foundations phase instead of "add unit 3
of 18". Each is a structural decision that every later feature depends on, and
retrofitting any of them after the content exists costs several times more.

1. **Units are code, not data.** `AmmoType` is a two-value enum and
   `AmmoLauncher.Launch()` is an `if/else` over two `PackedScene` fields — with a
   `// TODO: Switch this to inheritance and use an interface` already sitting on
   line 62. Eighteen units with armor, cost, level, launch rules, vision radius
   and build permissions cannot live in a `switch`.
2. **The damage model is wrong at the root.** `BaseNode.MaxHealth = 6` with
   integer subtraction cannot express Armor 1–5 against flat Damage with
   "does not exceed armor ⇒ no effect", which is what makes MBC's unit
   match-ups interesting.
3. **There is no cord graph.** Build legality, the destruction cascade, Power
   Spike, Virus propagation and the Hub lineage lights are all cord operations.
   Today "chain" is an ad-hoc parent pointer.
4. **There is no round/energy model, and a turn is exactly one shot.** The turn
   system in `GameManager` (`RegisterTurnEvent` / `CompleteTurn` /
   `AdvanceTurn`) commits the turn *synchronously* on the first launch and has
   **no end-turn input at all** — a turn ends only by firing. "Spend an energy
   budget across several launches, then pass" is a different shape, and the
   rotation (a linear scan of `_combatants`) needs an explicit end-of-turn
   concept it does not have.
5. **There is no map.** The 10000×10000 plane with `MinimapWorldSize = 475`
   standing in for bounds is already flagged in `AGENTS.md` and in the camera
   doc's *Scaling seam* section as the thing to replace.
6. **No determinism, no serialization.** Replays, save/load and multiplayer all
   need the same missing thing: a game state that is a function of a seed plus
   an ordered command list.
7. **`GameManager.cs` is a 1355-line god object** owning turn flow, aiming,
   combatant creation, camera, minimap, pools and input. It will not survive
   contact with 18 units and 4 players.
8. **Tuning lives in code, and sometimes twice.** Every gameplay number is a C#
   field default — `main_scene.tscn` overrides no tuning value at all — and the
   four `EnemyCombatant` knobs are *also* exported on `GameManager` and injected
   at construction, so the same numbers exist in two places. Doubling that
   across 18 units is not survivable.
9. **Single-player is baked in.** `_playerCombatant` is one field read directly
   by the defeat check, and the enemy's start position is derived from the
   camera's size and the window aspect — not from a map. Both have to go.

---

## 3. Milestones

Each milestone ends with **exit criteria** that are checkable, not vibes. Order
is dependency order, except where noted as parallelisable.

### M0 — Foundations

The load-bearing refactor. No new player-visible content; everything after this
is cheap because of it.

- **M0.1 — Unit definition resource.** Introduce `UnitDefinition` (a Godot
  `Resource`) carrying: id, display name, level/energy cost, armor, damage, build
  permission (Hub / Offensive Unit / neither), vision radius, projectile scene,
  and behaviour flags. Replace `AmmoType` with the definition; make
  `AmmoLauncher` take a `UnitDefinition` and dispatch through a behaviour
  interface rather than the `if (instance is Projectile) … else if (instance is
  Bomb)` chain.
- **M0.2 — Armor model.** Change `BaseNode` (and its successor, see M0.3) to
  subtractive armor with a per-unit-type maximum, and damage that is a flat
  integer per weapon. Delete the "MaxHealth = 6 because a half hit is a whole
  point" reasoning in `BaseNode.cs` and the two-band blast in
  `Bomb.ApplyBlastDamage()` — see the *Blast model* note in [units.md](units.md).
- **M0.3 — Structure vs. projectile split.** `BaseNode` currently means both
  "chain node" and "unit". Split into a `Structure` (armor, ownership, cord
  attachment, vision, build permission) and the cord geometry that links them.
  This is the change that makes 18 units possible.
- **M0.4 — Map resource.** A `MapDefinition` holding bounds, a height field, the
  four terrain states, water, energy pools (with size), and start positions.
  `GetMinimapWorldRect()` and `CreateMinimap()`'s camera size come off it — that
  is exactly the *Scaling seam* the camera doc already specifies. Move
  `PoolField`'s scatter to generate into the map rather than onto the plane.
- **M0.5 — Command log & determinism.** Every player and AI action becomes a
  serializable command appended to an ordered log; simulation advances only by
  applying commands. Fixed timestep for physics-affecting steps. This single
  change is the prerequisite for replays (M8), multiplayer (M9) and regression
  testing (§6).
- **M0.6 — Split `GameManager`.** Turn flow, camera/minimap, map/world, and
  match setup become separate nodes. Keep `GameManager.Instance` during the
  transition so call sites do not all change at once.

**Exit criteria:** the game still plays as it does today; a match is fully
described by (map, seed, command log) and replays identically from it.

### M1 — The core loop: energy, rounds, turns, build menu

This is the milestone that makes it *MoonBase Commander* rather than a
projectile duel, and it is worth doing before more units.

- Energy pool per player: current, next-round income, **maximum 35**.
- Unit cost = level = 1/3/7; spending energy replaces "one launch per turn".
- A round ends when every active player has spent out or skipped; energy then
  refills; **player order rotates left** each round.
- **Skip** — forfeits the rest of the round's turns and carries all current
  energy forward.
- Income: main Hub 7/round; Energy Collector 3 on a pool, 1 off it; Reclaimer's
  absorbed armor granted at end of round; bonuses for reclaims and kills.
- **Build menu UI**: the `1` / `3` / `7` pages, unit buttons, selection carrying
  into the launch view, **Cancel** back to the menu.
- **Launch Power Meter** as the HUD's primary affordance (today's `PowerBar`
  with `ChargeSpeed` is a good starting point).
- **Turn clock**: per-move timer, default 60 s, with the HUD countdown.

> **Decision needed — one turn, many launches?** The manual says the *round*
> ends once all energy is used, and that Skip "skips all of your turns in the
> current round" (plural). The natural reading is: one turn per player per
> round, in which you may launch repeatedly until your energy is gone. Confirm
> against the shipped game before building the turn loop, because the two
> readings produce different UIs (a persistent build menu vs. a one-shot
> launcher).

**Exit criteria:** a full round can be played with correct income, spend,
carry-over and rotation, with no units other than Hubs and Energy Collectors.

### M2 — The cord network

The spatial rules that make the map a puzzle.

- Cords as first-class geometry between a structure and its parent, with
  **cord nodes** at intervals (not units; no damage; Power Spike only).
- **Build legality**: a launch is refused if the resulting cord would overlap an
  existing cord, or if the target site is outside cord reach.
- **Reach** determined by the Hub's/Tower's network and by terrain height.
- **Hub destruction cascade** over the cord graph — destroying a Hub destroys
  everything built by it, recursively, including other Hubs. Note the current
  implementation waits **1.0 s per generation** (`BaseNode.Destroy()`), so a
  realistic network of 40-odd units would take tens of seconds to collapse and
  would hold the turn open the whole time via `TurnEvent`. The cascade needs to
  fan out concurrently, or be resolved as a graph operation with one animation
  budget — this is a parity *and* a pacing problem.
- **Lineage lights**: cord lights blink toward the main Hub, so a player can
  trace an opponent's network back to its origin. Today's `CableShader` is the
  canvas for this.
- **Power Spike** travel: damage down both directions, tapering, dead after 50
  cord nodes.

**Exit criteria:** you cannot build outside reach, cords visibly refuse to
overlap, and killing a Hub takes its whole subtree with it.

### M3 — Terrain, water and the wrapping world

- Height field with the four states — **Water, Ground, Plateau, Mountain** — and
  the rule that new units must land on **flat ground** (a structure landing on a
  hillside or in water is destroyed on impact).
- **Height changes launch range**: shots from high ground go farther, and shots
  *uphill* fall short. This is the mechanic that makes terrain a weapon.
- **Water**: only Bridges and Crawlers may land on it. Bridges convert their
  footprint to buildable land; destroying a Bridge drops everything on it into
  the water, destroying it.
- **Wrapping map**: leaving one edge re-enters on the other, in all
  world↔map maths — minimap, camera bounds, distance queries, cord reach, AI
  targeting. Wrapping is a cross-cutting concern, not a boundary condition: do
  it here or fight it forever.

**Exit criteria:** a map with a river and a mountain plays differently from a
flat one, and nothing breaks at the map seam.

### M4 — The eighteen units

Implemented in dependency order, not level order, because units share
subsystems. Build the subsystem, then the units that use it.

| subsystem | enables |
| --- | --- |
| **1. Shared ballistic launch** — one lobbing solver, power→distance, angle, gravity, terrain-aware landing prediction | everything |
| **2. Interception pipeline** — projectiles can be targeted and destroyed in flight | Anti-Air, Shields, Guided Missile survivability |
| **3. Status effects** — a timed *disabled* state on structures | EMP, Virus, Anti-Air reload |
| **4. Vision** — a vision radius per structure, unioned per team | Tower, Spy Balloon, Fog (M5) |
| **5. Sub-munitions** — a projectile that spawns children at apex | Cluster Bomb |
| **6. Homing** — steering toward a chosen target after apex | Guided Missile |
| **7. Phase-movers** — units that act between rounds, in birth order | Crawler |
| **8. Attachment effects** — on-land, on-hit, on-cord-link behaviours | Repair, Reclaimer, Mines, Bridge, Energy Collector |
| **9. Launcher capability** — who may build what, and how far they throw | Hub, Offensive Unit |

Then, in level order:

- **Level 1:** Anti-Air Missile Launcher, Bomb, Bridge, Cluster Bomb, Repair,
  Tower.
- **Level 3:** Guided Missile, Spy Balloon, EMP, Mines, Reclaimer, Power Spike.
- **Level 7:** Crawler, Energy Collector, Hub, Offensive Unit, Shield Generator,
  Virus.

Full statistics and edge-case rules for each are in [units.md](units.md) —
including the awkward ones: the Crawler detonating only on an enemy unit or
cord, the Mine's arming conditions, the Anti-Air's one-turn reload, the Virus
hopping the cord network one unit per cycle, and the Shield passing friendlies
while destroying enemies.

> **Decision needed — Tower.** The manual gives the Tower vision only, but the
> strategy section refers to a "Tower cord". Resolve before implementing.

**Exit criteria:** every unit in [units.md](units.md) is buildable and behaves
as described, including its stated exceptions.

### M5 — Fog, vision and wind

- **Fog of war** as a game option: unseen terrain renders black, hiding units
  and terrain; vision expands as structures spread; **explosions and smoke stay
  visible through fog** (an easy rule to miss that matters a lot in play).
- **Wind**: intensity option, direction randomised per game, bending every
  projectile — and because the map wraps, everyone is downwind of someone.
- Vision must compose with M3's wrapping, which is the fiddly part.

**Exit criteria:** a fog game is genuinely about scouting, and wind visibly
changes where a maximum-power shot lands.

### M6 — Match setup, factions, teams and bots

- **Game setup screen**: faction (NiceCo, DeWulf Industrial, System7, Team
  Alpha — same units, different art), colour (six, unique per player), player
  name, BOT slots (**up to three of ten, playing differently**), team assignment
  (teammates share line of sight and their units cooperate — an ally's Anti-Air
  will not shoot your projectiles — but they never share energy or control each
  other's units).
- **Lift the two-combatant assumption** in `GameManager.CreateCombatants()`:
  arbitrary players with teams, and the defeat rule already generalises
  (`EvaluateGameOver()` is team-aware; the rotation is not).
- **Map selection** with the options the setup screen exposes: clock, wind,
  fog, chat.
- **AI**: replace `EnemyCombatant`'s random cone with a real planner — energy
  budgeting, expansion toward pools, cord-space management, threat response,
  and the ten personalities the original advertises.

**Exit criteria:** a 4-player, 2v2, fog, windy game against bots runs to a
correct conclusion and produces a winner.

### M7 — Challenge mode and the map editor

- **Challenge mode**: 16 missions across the four factions, with a progression
  screen and per-mission bonuses for efficiency. Needs the mission-definition
  format from M0.4 plus AI scripting hooks.
- **Map editor**: map size, the four terrain states, energy pools of varying
  size (a bigger pool fits more Collectors), **starting positions** (the
  original enforces placing the 4/3/2-player icon sets for a valid map),
  planet/terrain selection, clear/load/save.
- **Map content**: a shipped set of maps, since parity in modes with nothing to
  play them on is not parity. Custom maps travel to other players — which in
  our case means a map is a resource the host sends (M9).

**Exit criteria:** a player can build a map, save it, play it, and play the
first challenge mission.

### M8 — Replays, game summary and save/load

- **Replay Theatre**: record every match to the command log from M0.5; watch it
  back with play/pause, Track In / Track Out, and per-player view. Fog is
  **nearly transparent** in replay so you can see the whole map while still
  understanding where Towers and Balloons went — a specific, easily-missed
  detail.
- **Save/load** of an in-progress match, from the same command log.
- **Game summary**: Units built, Energy generated, Shots, Hits (drawing
  anti-air fire explicitly does **not** count), Destroyed, Accuracy, Bonus.

**Exit criteria:** any finished match can be saved and replayed frame-accurately,
and the summary numbers are right.

### M9 — Multiplayer

- Host/join over LAN and internet (ENet), with the lobby, ready-up and
  host-authoritative settings the original had. Players cannot join a game in
  progress.
- Command-log lockstep (or host-authoritative simulation) on top of M0.5;
  custom maps transferred to clients.
- **Chat**, including the original's addressing: `name: message` for a private
  message, `team:` for teammates, `Tab` to toggle input.
- **Side bars** when a player is defeated or leaves, and correct handling of a
  player dropping mid-match.

**Exit criteria:** two machines complete a match, including a mid-match drop.

### M10 — Presentation and interface parity

- **HUD bar**: time, current player's turn, **player order** (which visibly
  shifts left each round), and the **Next Round Energy** indicator.
- **Energy bar** with its numeric readout and the 35 cap (today's `PowerBar` is
  a different thing — the launch meter — and both must coexist).
- **Hub/Offensive Unit scrolling**: Next Unit, Previous Unit, Center Current
  Unit.
- **Key commands**: Damage Bar (**D**), Range Radius (**R**), Center Unit
  (**C**), Next Unit (**N**), Previous Unit (**P**), Closest Launcher (**L**),
  Show Attacked (**A**), Chat Toggle (**Tab**), plus a **remappable controls**
  screen. None of these exist today, and **R** (anti-air/mine range display) is
  genuinely load-bearing for play.
- **Navigation**: right-drag to scroll, left-click to select, drag a unit to
  scroll, click a unit's picture to re-center, click a cord to jump to the unit
  at its far end, arrow keys to scroll.
- **Track In / Track Out** camera modes with **Smart Camera** and **Auto Return
  Camera**; plus Auto Scroll and Auto Hub Select. (All default **OFF** in the
  original — keep those defaults.)
- **Damage feedback**: damage bar and the **smoke colour that encodes severity**.
- **Audio**: SFX/music/interface/voice volumes, commentary toggle, and the
  **dynamic music system** that tracks how the game is going.

**Exit criteria:** a returning player's hands work without being told.

---

## 4. What can run in parallel

The critical path is M0 → M1 → M2 → M3 → M4. After M0 lands, three tracks can
proceed independently and only meet at M6:

- **Content track:** M1 → M4 (units), M5 (fog/wind).
- **World track:** M2 → M3 → M7 (terrain, wrap, then editor and maps).
- **Platform track:** M0.5 → M8 (replays/stats) → M9 (multiplayer).

M6 needs all three. M10 can be interleaved anywhere from M1 onward, since the
HUD work is mostly independent of simulation.

---

## 5. Cross-cutting engineering work

**Determinism.** M0.5's command log is the spine: replays, save/load,
multiplayer and testing all consume it. Budget for it properly — rounding in the
ballistic solver, physics timestep, and iteration order over structures are all
places where non-determinism creeps in, and it is far cheaper to keep
determinism than to restore it.

**Verification.** The original is available for reference (the Steam
re-release), so parity claims can be checked rather than argued:

- a headless harness that loads a map + command log and asserts the resulting
  state hash — this is the regression suite for every rule in [units.md](units.md);
- **golden replays** for the awkward rules (cascade, Power Spike taper, Virus
  hop, anti-air reload, mine arming, bridge collapse, wrap-around range);
- side-by-side play against the original for anything the manual describes
  qualitatively — launch ranges, flight arcs, terrain heights — which is how
  the tuning table in §7 gets filled in.

**AI.** The ten BOTs are a content problem once the planner exists: personality
should be data (a weights resource), not ten subclasses.

**Documentation.** `AGENTS.md` is the entry point for this repo and currently
describes a game that will no longer exist. Update it in the same commit as the
code it describes, and keep [units.md](units.md) authoritative by citing it from
the code that implements each rule.

---

## 6. Tuning table (the unknowns)

These have no published value. Collect them in one place — a resource, not
scattered `[Export]`s — and record what the original actually does as it is
measured. **This table is expected to change; nothing else in the plan is.**

| quantity | original value | ours | notes |
| --- | --- | --- | --- |
| Hub launch range (flat ground) | — | 20 (today's `MaxLaunchForce`) | varies with terrain height |
| Offensive Unit range | "nearly twice a Hub" | — | ratio is documented, absolute is not |
| Cord segment / node spacing | — | — | also sets the 50-node Power Spike reach |
| Cord reach from a Hub | — | — | the base build radius |
| Terrain height per state | — | — | Water/Ground/Plateau/Mountain |
| Pool sizes (small/medium/large) | — | 6.0 (`PoolSide`) | "the larger the pool, the more Collectors fit" |
| Projectile gravity / arc | — | 9.8 (`Projectile.Gravity`) | |
| Anti-Air range | "smaller than an EMP's" | — | |
| EMP radius | larger than Anti-Air and Shields | — | |
| Shield radius | — | — | |
| Wind strength range | adjustable intensity | — | |
| Vision radius (Hub / Tower / Balloon) | — | — | |
| Turn clock default | **60 s** | — | documented |
| Max stored energy | **35** | — | documented |
| Hub income | **7**/round | — | documented |
| Collector income | **3** on pool, **1** off | — | documented |
| Bot personalities (×10) | — | — | data-driven weights |

---

## 7. Risks

| risk | why it bites | mitigation |
| --- | --- | --- |
| **Scope.** Rules+content+mode parity is a large project, not a sprint. | Morale and half-finished systems. | Milestones are independently shippable. Stop after M1 and you have a better game than today. |
| **M0 refactor churn.** | Everything is edited at once and nothing works. | Keep `GameManager.Instance` and the old ammo path alive behind the new one; land M0 with the game still playable (that is its exit criterion). |
| **Wrapping maps touch everything.** | Discovered late, it invalidates distance/targeting/camera code. | It is in M3, before units and modes; do the world↔map maths once, centrally. |
| **Determinism retrofitted.** | Replays and multiplayer both break subtly. | M0.5, before any of them. |
| **Unknown numbers.** | "Parity" arguments with no referee. | Tuning table, measured against the original, kept in one resource. |
| **Original assets.** | Copyright. | Behaviour and layout parity only; new art and audio. |
| **`GameManager` god object.** | Every feature edits one 1355-line file and conflicts. | M0.6. |

---

## 8. Parity checklist

Track progress here. Each line is a claim that can be demonstrated.

**Rules**
- [ ] Energy 1/3/7, max 35, per-round refill, carry-over
- [ ] Round ends when all players spend out or skip
- [ ] Player order shifts left each round
- [ ] Skip forfeits the round's remaining turns
- [ ] Subtractive armor; damage that does not exceed armor does nothing
- [ ] Cords cannot overlap; illegal launches refused
- [ ] Cord nodes take no damage except from a Power Spike
- [ ] Hub destruction cascades recursively through the network
- [ ] Units must land on flat ground
- [ ] Only Bridges and Crawlers land on water
- [ ] Terrain height modifies launch range (downhill helps, uphill hurts)
- [ ] Maps wrap in both axes
- [ ] Win = destroy the opponent's main Hub

**Units** — all 18 from [units.md](units.md), each with its stated exceptions
- [ ] Level 1: Anti-Air, Bomb, Bridge, Cluster Bomb, Repair, Tower
- [ ] Level 3: Guided Missile, Spy Balloon, EMP, Mines, Reclaimer, Power Spike
- [ ] Level 7: Crawler, Energy Collector, Hub, Offensive Unit, Shield Generator, Virus

**Options**
- [ ] Clock (default 60 s)
- [ ] Wind (intensity; random direction per game)
- [ ] Fog (black unseen area; explosions still visible)
- [ ] Teams (shared line of sight, cooperating units, no shared energy)

**Modes**
- [ ] Skirmish setup: faction, colour, name, bots, teams, map
- [ ] Challenge mode: 16 missions, 4 factions
- [ ] Replay Theatre: record, save, play/pause, per-player view, near-transparent fog
- [ ] Game summary: Units, Energy, Shots, Hits, Destroyed, Accuracy, Bonus
- [ ] Map editor: size, terrain, pools, start positions, save/load
- [ ] Multiplayer: LAN + internet, lobby, chat with `name:`/`team:` addressing, side bars on defeat

**Interface**
- [ ] Energy bar with next-round income indicator
- [ ] Build menu on 1/3/7 pages
- [ ] Launch power meter
- [ ] HUD: time, current player, player order, next-round energy
- [ ] Hub/Offensive Unit scrolling (next/previous/center)
- [ ] Keys: D, R, C, N, P, L, A, Tab, remappable
- [ ] Track In / Track Out, Smart Camera, Auto Return, Auto Scroll, Auto Hub Select (all default off)
- [ ] Damage bar and smoke-severity feedback
- [ ] Dynamic music, commentary toggle, four volume channels

---

## 9. Where to start

**M0.1–M0.3, then M1.** Concretely, the first commit is `UnitDefinition` plus the
armor model, because until a unit is data and armor is subtractive, every unit
added is throwaway work. The second is the energy/round loop, because that is
the difference between a duel and MoonBase Commander.

The earliest point at which this is a *game* rather than a prototype is the end
of **M1**: energy, rounds, income, a build menu, and Hubs and Energy Collectors
expanding across a map. That is worth reaching before deciding how far down the
rest of the list to go.
