# Feature parity with MoonBase Commander — the plan

How **MbcPrototype** gets from "a turn-based node-launching prototype" to a
build where someone who knows the 2002 original can sit down and find the
mechanics, units, rules and interface affordances they expect, behaving the way
they remember.

Companion documents:

- [units.md](units.md) — the original's eighteen units, the energy economy, the
  cord rules and the world rules, transcribed from the official manual. It
  documents the **original in full**, including the parts §1 cuts. Use it as the
  reference for the twelve units we build; do not read it as the scope.
- [../camera/README.md](../camera/README.md) — the existing camera/minimap
  documentation, which the map work below will rewrite.
- [../../AGENTS.md](../../AGENTS.md) — the project guide; keep it in step as
  this plan lands.

---

## 1. What we are building

**One 1v1 match: one human against one AI, on one hardcoded map.** That is the
whole committed scope. Everything else in this document either serves that or is
explicitly deferred.

Parity is a direction, not a promise. Four bars, in the order they are worth
reaching:

| bar | meaning | reached when |
| --- | --- | --- |
| **Rules parity** | same mechanics, same numbers, same edge cases | a player's expectations about *what happens* are never wrong |
| **Content parity** | the twelve in-scope units, the terrain and the pools | a player's favourite strategy **within the subset** exists |
| **Mode parity** | 1v1 setup, replays, one map | a player can play the way they used to |
| **Presentation parity** | HUD layout, key commands, damage feedback, audio | the game *reads* the same at a glance |

Full content parity — eighteen units, four-player games, teams, challenge
missions, internet play — is **not** a goal. See the cut list below.

### In scope

| area | decision |
| --- | --- |
| **Players** | two, 1v1: one human and one AI. No teams. |
| **Map** | **one hardcoded map** for the whole of development. The map editor is the last milestone (M9). |
| **Units** | **twelve** of the original's eighteen — see [The twelve units](#the-twelve-units). |
| **AI** | **one** opponent planner. Personalities (the original's ten BOTs) are later data, not now. |
| **World** | a height field with three terrain states, and a **wrapping map**. |
| **Fog** | yes, as a game option — it is what makes the Spy Balloon worth building. |
| **Energy & rounds** | the 1/3/7 economy, max 35, per-round refill, rotating player order. |
| **Cords** | the full spatial rules: cord geometry, no overlap, reach, cascade, lineage lights. |
| **Fidelity** | rules parity where it can be measured. Art and audio are ours — the original's assets are not redistributable. |

### Cut, and what cutting it costs

Each of these is a decision, not an oversight. The right-hand column is what
disappears with it, so nobody rediscovers it later and treats it as a bug.

| cut | why | what it costs |
| --- | --- | --- |
| **Wind** | not wanted | one option, and shots that bend. Nothing else depends on it. |
| **Water** | not wanted | the fourth terrain state; **Bridges**; structures being destroyed by landing in water; the Crawler's ability to travel under water. Terrain becomes a height-and-cover puzzle instead of a river-crossing one. |
| **GameSpy-era multiplayer** (lobbies, LAN discovery, chat addressing, mid-match drops) | period infrastructure, and premature while the mode itself is 1v1 | nothing today. The command log (§3, M0.5) keeps the door open — see *Built for expansion*. |
| **Three- and four-player games, and teams** | 1v1 first | shared line of sight, allied interception rules, side bars for defeated players. |
| **The other six units** (Bridge, Tower, Mines, Reclaimer, Offensive Unit, Virus) | the roster is trimmed to what the 1v1 game needs | see *The six units we are not building*, below and in M4 |
| **Challenge mode** (16 missions) | single-player content that needs the mission format and authored maps | the ladder and its efficiency bonuses. |
| **Factions, music, voice commentary** | cosmetic or unshippable | nothing mechanical: factions differ only in art, and every faction has the same eighteen units. |

### Built for expansion, not built yet

Only 1v1 ships, but the following must hold from M0 — retrofitting them later is
what makes "we'll add players later" expensive:

- the match spawns a **list of combatants**, not two named fields, and the mode
  ("1v1") is a setup parameter rather than an assumption in the code;
- `Combatant.Team` stays, and both the rotation and the defeat check stay
  team-aware, even though every game today is one team each;
- units are **data** (`UnitDefinition` in a catalog) and the map is a
  **resource**, so a second player, a second map and later a second launcher are
  content rather than code;
- the **command log** is player-agnostic and serializable, which is what a
  future network layer would need;
- no code may assume "the enemy" is a single object.

### Where numbers are unknown

The manual publishes hit points, damage, energy costs and the objective. It does
**not** publish launch ranges, cord lengths, the blast falloff curve, pool
sizes, terrain heights or bot personalities. Those are "tuned to feel" and
belong in one tuning table (§6). Do not scatter them.

---

## 2. Where the prototype stands

Verified against the code today. The right-hand column is the gap this plan
closes; the **size** column is a rough ordering device, not a promise.

| system | original | prototype today | size |
| --- | --- | --- | --- |
| **Objective** | destroy the opponent's **main Hub** | destroy the enemy chain's root — `GameManager.EvaluateGameOver()` treats "no root" as defeat | ✅ aligned |
| **Cord network** | the spatial system: reach, overlap rules, cord nodes, cascade, lights | implicit parent/child links only (`BaseNode.ParentBase`/`Children`, `CableShader`). No cord geometry, no reach, no overlap test, no cord nodes | **XL** |
| **Durability** | **hit points** per unit type (the manual's *Armor* column), damage subtracted, partial damage normal | integer HP, but **one number for every node**: `BaseNode.MaxHealth = 6` | **M** |
| **Blast damage** | a weapon damages what it strikes **and** what is near it, with a falloff the manual never publishes | a two-band blast in `Bomb.ApplyBlastDamage()` (full inside 1.5 units, half to 3), with damage 2/1 rather than the Bomb's damage 3 | **M** — the shape is a model, the numbers are invented |
| **Energy** | 1/3/7 per unit, max 35, per-round refill, income units | none | **L** |
| **Turn model** | a turn is an energy budget; round ends when all have spent or skipped; order rotates left | one launch per turn; `CurrentTurn` counts rotation wraps | **L** |
| **Units** | 18, three levels | 2: `AmmoType { Node, Bomb }`, hardcoded in `AmmoLauncher.Launch()` (`gm.ProjectileScene` / `gm.BombScene`) | **XL** |
| **Players** | up to 4, teams, shared line of sight | exactly 2, hardcoded in `GameManager.CreateCombatants()` | **M** — generalise the shape, not the count |
| **Map** | authored, sized, four terrain states, water, wrap-around | flat 10000×10000 plane; play area approximated by `MinimapWorldSize = 475` | **XL** |
| **Terrain height** | Water/Ground/Plateau/Mountain; height changes launch range | none — everything sits at y=0 | **M** — three states, not four (water is cut) |
| **Purple pools** | energy pools; size determines how many Harvesters fit | `PoolField` scatter, decorative, `PoolAt`/`OverlappingPools` written but **never called** | **S** |
| **Fog / vision** | option; black unseen terrain; Hub and Balloon vision | none | **L** |
| **Anti-air / shields / EMP** | interception and disable | none | **L** |
| **AI** | 10 BOTs with distinct play styles | one `EnemyCombatant`: random aim in a 25° cone, random power, alternates node/bomb | **L** — one good planner, not ten |
| **Modes** | Skirmish, Challenge (16 missions), LAN, GameSpy | one hardcoded skirmish | **M** — a 1v1 setup screen |
| **Map editor** | size, terrain states, pools, starting positions, save/load/share | none | **L** — **last** (M9) |
| **Replays / stats** | Replay Theatre + 7-stat game summary | none; no serialization of game state at all | **L** |
| **HUD / keys** | energy bar, build menu, HUD bar, D/R/C/N/P/L/A/Tab keys | power bar, two ammo icons, turn label, win/lose labels; `fire_shot`, `aim_left/right`, `switch_ammo_next/previous` | **M** |
| **Audio** | dynamic music, voice commentary, SFX and 4 volume channels | nothing at all; the repo has no audio assets | **S** — placeholder SFX only |

### The architectural blockers

These are why the plan starts with a foundations phase instead of "add unit 3 of
12". Each is a structural decision that every later feature depends on, and
retrofitting any of them after the content exists costs several times more.

1. **Units are code, not data.** `AmmoType` is a two-value enum and
   `AmmoLauncher.Launch()` is an `if/else` over two `PackedScene` fields — with a
   `// TODO: Switch this to inheritance and use an interface` already sitting on
   line 62. Twelve units with hit points, cost, level, launch rules, vision
   radius and build permissions cannot live in a `switch`.
2. **Durability is one hardcoded number and the blast curve is invented.**
   `BaseNode.MaxHealth = 6` is the same for every node and is reverse-engineered
   from this repository's own bomb (three direct hits, "a half hit is a whole
   point"), not from the original, where the Hub has 5 hit points, the Spy
   Balloon 1, and damage is the manual's flat 3 for a Bomb. The two-band blast
   is our model of a curve the manual does not publish; it has to live in the
   unit definition as a labelled guess instead of in `Bomb.cs` as a fact.
3. **There is no cord graph.** Build legality, the destruction cascade, Power
   Spike, EMP's reach along a network and the Hub lineage lights are all cord
   operations. Today "chain" is an ad-hoc parent pointer.
4. **There is no round/energy model, and a turn is exactly one shot.** The turn
   system in `GameManager` (`RegisterTurnEvent` / `CompleteTurn` /
   `AdvanceTurn`) commits the turn *synchronously* on the first launch and has
   **no end-turn input at all** — a turn ends only by firing. "Spend an energy
   budget across several launches, then pass" is a different shape, and the
   rotation (a linear scan of `_combatants`) needs an explicit end-of-turn
   concept it does not have.
5. **There is no map.** The 10000×10000 plane with `MinimapWorldSize = 475`
   standing in for bounds is already flagged in `AGENTS.md` and in the camera
   doc's *Scaling seam* section as the thing to replace. Even one hardcoded map
   needs a resource to hardcode.
6. **No determinism, no serialization.** Replays, save/load and regression
   testing all need the same missing thing: a game state that is a function of a
   seed plus an ordered command list — and it is what keeps a future second
   player cheap.
7. **`GameManager.cs` is a 1355-line god object** owning turn flow, aiming,
   combatant creation, camera, minimap, pools and input. It will not survive
   contact with twelve units.
8. **Tuning lives in code, and sometimes twice.** Every gameplay number is a C#
   field default — `main_scene.tscn` overrides no tuning value at all — and the
   four `EnemyCombatant` knobs are *also* exported on `GameManager` and injected
   at construction, so the same numbers exist in two places. Doubling that
   across twelve units is not survivable.
9. **Single-player is baked in.** `_playerCombatant` is one field read directly
   by the defeat check, and the enemy's start position is derived from the
   camera's size and the window aspect — not from a map. Both have to go, and
   both are the same edit that lets a map and a combatant list arrive.

---

## 3. Milestones

Each milestone ends with **exit criteria** that are checkable, not vibes. Order
is dependency order, except where noted as parallelisable.

### M0 — Foundations

The load-bearing refactor. No new player-visible content; everything after this
is cheap because of it.

- **M0.1 — Unit definition resource.** Introduce `UnitDefinition` (a Godot
  `Resource`) carrying: id, display name, level/energy cost, hit points, damage,
  blast radii, build permission, vision radius, projectile scene, and behaviour
  flags. Replace `AmmoType` with the definition; make `AmmoLauncher` take a
  `UnitDefinition` and dispatch through a behaviour interface rather than the
  `if (instance is Projectile) … else if (instance is Bomb)` chain. Units live
  in a **catalog** (also a resource) so the HUD, the launcher and a future
  scenario all read the same list.
- **M0.2 — Hit points and the blast curve.** Move durability into the
  definition: **hit points per unit type**, from the manual's *Armor* column
  (Hub 5, Harvester 5, Anti-Air 3, Shield 3, Crawler 3, Repair 1, Balloon 1).
  Delete the "MaxHealth = 6 because a half hit is a whole point" reasoning in
  `BaseNode.cs` and the one-number-for-everything default.
  - Damage is a **flat integer subtracted from the target's hit points**: a Bomb
    (damage 3) against a Hub (5) leaves 2, and partial damage is normal. There
    is **no armor threshold** — nothing in the original says damage that fails
    to exceed a unit's armor does nothing, and the earlier draft of this plan
    claimed otherwise. Read "Armor" in [units.md](units.md) as *hit points*.
  - A weapon also damages what is **near** where it goes off, with a falloff the
    manual does not publish. The existing two-band model (full damage inside
    `DirectHitRadius`, half out to `OuterRadius`) is a fine *model*, but it is
    ours: move the radii into unit definitions and label them in the tuning
    table (§6), and give the Bomb the manual's damage 3 rather than today's 2.
- **M0.3 — Structure vs. cord split.** `BaseNode` currently means both "a unit"
  and "the link in the network". Split into a `Structure` (hit points, ownership,
  vision, build permission, the cords it hangs from and anchors) and `Cord` (the
  geometry between two structures, the cable, and later the cord nodes). This is
  the change that makes twelve units possible.
- **M0.4 — Map resource, and the development map.** A `MapDefinition` holding
  bounds, a height field, the terrain states, energy pools (with size), and the
  two **start positions** for a 1v1 match. Ship exactly one, hardcoded, as the
  map every milestone is developed and tested on. `GetMinimapWorldRect()` and
  `CreateMinimap()`'s camera size come off it — that is exactly the *Scaling
  seam* the camera doc already specifies. Move `PoolField`'s scatter to generate
  into the map rather than onto the plane, and take the start positions from it
  instead of from the camera and window aspect.
- **M0.5 — Command log & determinism.** Every player and AI action becomes a
  serializable command appended to an ordered log; simulation advances only by
  applying commands. Fixed timestep for physics-affecting steps. This is the
  prerequisite for replays (M7), regression testing (§5) and any future second
  player.
- **M0.6 — Split `GameManager`.** Turn flow, camera/minimap, map/world, and
  match setup become separate nodes, and the match stops knowing how many
  combatants there are. Keep `GameManager.Instance` during the transition so
  call sites do not all change at once.

**Exit criteria:** the game still plays as it does today, on the development
map, with the two existing units — and a match is fully described by (map, seed,
command log) and replays identically from it.

### M1 — The core loop: energy, rounds, turns, build menu

This is the milestone that makes it *MoonBase Commander* rather than a
projectile duel, and it is worth doing before more units.

- Energy pool per player: current, next-round income, **maximum 35**.
- Unit cost = level = 1/3/7; spending energy replaces "one launch per turn".
- A round ends when both players have spent out or skipped; energy then refills;
  **player order rotates left** each round.
- **Skip** — forfeits the rest of the round's turns and carries all current
  energy forward.
- Income: main Hub 7/round; a **Harvester** 3 on a pool, 1 off it. A Harvester
  disabled by an EMP when the round ends yields nothing.
- **Build menu UI**: the `1` / `3` / `7` pages, unit buttons, selection carrying
  into the launch view, **Cancel** back to the menu.
- **Launch Power Meter** as the HUD's primary affordance (today's `PowerBar`
  with `ChargeSpeed` is a good starting point).
- **Turn clock**: per-move timer, default 60 s, with the HUD countdown.

> **Open rules question — one turn, many launches?** The manual says the *round*
> ends once all energy is used, and that Skip "skips all of your turns in the
> current round" (plural). The natural reading is: one turn per player per round,
> in which you may launch repeatedly until your energy is gone. Confirm against
> the shipped game before building the turn loop, because the two readings
> produce different UIs (a persistent build menu vs. a one-shot launcher).

**Exit criteria:** a full round can be played with correct income, spend,
carry-over and rotation, with no units other than **Hubs and Harvesters**.

### M2 — The cord network

The spatial rules that make the map a puzzle.

- Cords as first-class geometry between a structure and its parent, with
  **cord nodes** at intervals (not units; no damage; Power Spike only).
- **Build legality**: a launch is refused if the resulting cord would overlap an
  existing cord, or if the target site is outside reach.
- **Reach is a property of the launcher, not of the network.** A structure must
  be launched from a structure already in your network, and how far that
  launcher throws is its own number (§6). Nothing increases it. Expansion works
  because **a Hub can build another Hub**, and each new Hub is itself a launcher
  standing further out: the network grows one Hub hop at a time.
  - Note this for the record: the **Tower grants vision only** in the original —
    it does not extend cord reach. An earlier draft of this plan had it
    backwards. (The Tower is out of scope anyway, but the reason matters: no
    in-scope unit grants reach, so reach must work from launchers alone.)
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
  cord nodes. (The travel lives here because it is a cord operation; the unit
  that fires it is M4.)

**Exit criteria:** you cannot build outside reach, cords visibly refuse to
overlap, and killing a Hub takes its whole subtree with it.

### M3 — Terrain and the wrapping world

- Height field with **three** states — **Ground, Plateau, Mountain**. The
  original's fourth state, **Water, is cut** (§1): with it go Bridges, structures
  destroyed by landing in water, and the Crawler's water travel.
- The rule that new units must land on **flat ground** (a structure landing on a
  hillside is destroyed on impact).
- **Height changes launch range**: shots from high ground go farther, and shots
  *uphill* fall short. This is the mechanic that makes terrain a weapon.
- **Wrapping map**: leaving one edge re-enters on the other, in all
  world↔map maths — minimap, camera bounds, distance queries, cord reach, AI
  targeting. Wrapping is a cross-cutting concern, not a boundary condition: do
  it here or fight it forever.

**Exit criteria:** the development map, given a mountain and a plateau, plays
differently from a flat one, and nothing breaks at the map seam.

### M4 — The twelve units

Implemented in dependency order, not level order, because units share
subsystems. Build the subsystem, then the units that use it.

| subsystem | enables |
| --- | --- |
| **1. Shared ballistic launch** — one lobbing solver, power→distance, angle, gravity, terrain-aware landing prediction | everything |
| **2. Interception pipeline** — projectiles can be targeted and destroyed in flight | Anti-Air, Shield Generator, Guided Missile survivability |
| **3. Status effects** — a timed *disabled* state on structures and units | EMP, Anti-Air reload |
| **4. Vision** — a vision radius per structure, unioned per player | Hub, Spy Balloon, fog (M5) |
| **5. Sub-munitions** — a projectile that spawns children at apex | Cluster Bomb |
| **6. Homing** — steering toward a chosen target after apex | Guided Missile |
| **7. Phase-movers** — units that act between rounds, in birth order | Crawler |
| **8. Attachment effects** — on-land, on-hit and per-round behaviours | Repair, Harvester income, Shield aura |
| **9. Launcher capability** — who may build what, and how far they throw | Hub |

#### The twelve units

Our names on the left, the manual's on the right; levels are the energy cost.

| unit | manual name | level | HP | damage | what makes it work |
| --- | --- | --- | --- | --- | --- |
| **Bomb** | Bomb | 1 | — | 3 | the baseline weapon; detonates on a unit or solid ground |
| **Repair** | Repair | 1 | 1 | heals 1 | restores 1 hit point to **any** unit it hits, enemy ones included |
| **Cluster** | Cluster Bomb | 1 | — | 1.5 × 3 | splits into three bomblets at the apex; its real value is drawing anti-air fire |
| **Anti-Air** | Anti-Air Missile Launcher | 1 | 3 | — | shoots down enemy projectiles in range; **inactive one full turn cycle** while it reloads |
| **Missile** | Guided Missile | 3 | — | 3 | homing engages at the apex and flies at the closest enemy unit; a Bomb's damage if it arrives |
| **Balloon** | Spy Balloon | 3 | 1 | — | vision without a cord; anything that touches it destroys it |
| **EMP** | EMP | 3 | — | 2 | disables everything in a radius for one turn cycle; radius **larger than Anti-Air's or a Shield's** |
| **Spike** | Power Spike | 3 | — | 3 | travels a cord in both directions, tapering, dead after 50 cord nodes |
| **Crawler** | Crawler | 7 | 3 | 4 | moves between rounds in birth order, slips under shields, detonates only against an enemy unit or cord |
| **Harvester** | Energy Collector | 7 | 5 | — | 3 energy/round on a pool, 1 off it; **explodes violently when destroyed** |
| **Hub** | Hub | 7 | 5 | — | the only launcher, and the only unit that builds everything; only the **main** Hub generates energy |
| **Shield** | Shield Generator | 7 | 3 | — | destroys enemy projectiles crossing it, passes friendlies; Crawlers go underneath |

#### The six units we are not building

Bridge, Tower, Mines, Reclaimer, Offensive Unit, Virus. One is a consequence of
another cut — Bridge goes with water — and the rest are trims of the roster.
What each one takes with it:

- **Tower** — a cord-based vision structure. Vision now comes from Hubs and Spy
  Balloons only, which makes the Balloon's cord-free vision matter more.
- **Offensive Unit** — the second launcher, the weapons-only build list, and its
  "nearly twice a Hub" throw. **The Hub is now the only launcher**: reach is the
  Hub's, and `BuildPermission` has one entry that matters. Keep the mechanism —
  a second launcher should be data, later.
- **Virus** — the cord-hopping disable. EMP is the only disable in the game.
- **Mines** — area denial, and the reason to watch where your units stand.
- **Reclaimer** — the only unit that turns absorbed hit points into energy.
  Income is now the main Hub plus Harvesters, and nothing else.
- **Bridge** — already gone with water.

**Exit criteria:** each of the twelve is buildable and behaves as described,
including its stated exceptions (the Anti-Air reload, the Crawler's detonation
condition, the Shield passing friendlies, the Balloon dying to a touch), and the
six cut units appear nowhere in the catalog.

### M5 — Fog and vision

- **Fog of war** as a game option: unseen terrain renders black, hiding units
  and terrain; vision expands as structures spread; **explosions and smoke stay
  visible through fog** (an easy rule to miss that matters a lot in play).
- Vision comes from the **Hub** and the **Spy Balloon** — and the Balloon's
  whole point is that it needs no cord.
- Vision must compose with M3's wrapping, which is the fiddly part.

**Exit criteria:** a fog game is genuinely about scouting, and a Balloon placed
forward is worth its 3 energy.

### M6 — The AI, and the 1v1 match setup

- **Game setup screen** for two players: colour (so the two sides can be told
  apart), name, and the single **BOT slot**. Options: clock, fog, map (one map
  for now). **No faction selection** and **no team assignment** — see §1.
- **Lift the two-combatant assumption** in `GameManager.CreateCombatants()`: the
  match builds a combatant list from the setup, and 1v1 is what the setup
  produces today. `EvaluateGameOver()` is already team-aware; the rotation is
  not, and neither is the camera.
- **AI**: replace `EnemyCombatant`'s random cone with one real planner — energy
  budgeting across the round, expansion by building Hubs outward, Harvesters on
  pools, cord-space management, threat response (it should notice an incoming
  Cluster and answer with Anti-Air), and a bias toward killing the main Hub.
  Personality is **not** in scope: when the original's ten BOTs arrive they
  should be a weights resource, not ten subclasses.

**Exit criteria:** a 1v1 fog game against the AI runs to a correct conclusion —
the AI spends its energy sensibly, expands, and can win and lose.

### M7 — Replays, save/load and the game summary

- **Replay Theatre**: record every match to the command log from M0.5; watch it
  back with play/pause, Track In / Track Out, and per-player view. Fog is
  **nearly transparent** in replay so you can see the whole map while still
  understanding where Balloons went.
- **Save/load** of an in-progress match, from the same command log.
- **Game summary**: Units built, Energy generated, Shots, Hits (drawing
  anti-air fire explicitly does **not** count), Destroyed, Accuracy, Bonus.

**Exit criteria:** any finished match can be saved and replayed frame-accurately,
and the summary numbers are right.

### M8 — Presentation and interface parity

- **HUD bar**: time, current player's turn, **player order** (which visibly
  shifts left each round), and the **Next Round Energy** indicator.
- **Energy bar** with its numeric readout and the 35 cap (today's `PowerBar` is
  a different thing — the launch meter — and both must coexist).
- **Hub scrolling**: Next Unit, Previous Unit, Center Current Unit.
- **Key commands**: Damage Bar (**D**), Range Radius (**R**), Center Unit
  (**C**), Next Unit (**N**), Previous Unit (**P**), Closest Launcher (**L**),
  Show Attacked (**A**), plus a **remappable controls** screen. None of these
  exist today, and **R** (anti-air and shield range display) is genuinely
  load-bearing for play.
- **Navigation**: right-drag to scroll, left-click to select, drag a unit to
  scroll, click a unit's picture to re-center, click a cord to jump to the unit
  at its far end, arrow keys to scroll.
- **Track In / Track Out** camera modes with **Smart Camera** and **Auto Return
  Camera**; plus Auto Scroll and Auto Hub Select. (All default **OFF** in the
  original — keep those defaults.)
- **Damage feedback**: damage bar and the **smoke colour that encodes severity**.
- **Audio**: **placeholder SFX only** — launch, impact, explosion, anti-air
  fire, structure destroyed, UI click — plus **one master volume** and a mute.
  No music, no voice, no commentary, and therefore no per-channel volume mixing.

**Exit criteria:** a returning player's hands work without being told.

### M9 — The map editor and authored maps

Last by decision, not by dependency: the whole of development runs on the one
hardcoded map from M0.4, so nothing before this milestone waits on it.

- **Map editor**: map size, the three terrain states, energy pools of varying
  size, and **starting positions** — validated for 1v1 as a pair of positions,
  with the setting left open for the original's larger icon sets.
- Clear / load / save, and planet/terrain selection.
- **Authored maps**: the editor's output is what the game loads, and the
  hardcoded development map becomes just the first entry in that set rather than
  a special case in code.

**Exit criteria:** a player can build a map, save it, play it, and play 1v1 on it
against the AI.

---

## 4. What can run in parallel

The critical path is M0 → M1 → M2 → M3 → M4. After M0 lands, three tracks can
proceed independently and only meet at M6:

- **Content track:** M1 → M4 (units), M5 (fog/vision).
- **World track:** M2 → M3.
- **Platform track:** M0.5 → M7 (replays/summary).

M6 needs the content and world tracks. M8 can be interleaved anywhere from M1
onward, since the HUD work is mostly independent of simulation. M9 is last by
decision.

---

## 5. Cross-cutting engineering work

**Determinism.** M0.5's command log is the spine: replays, save/load and
regression testing all consume it, and it is what would keep a second human
player from being a rewrite. Budget for it properly — rounding in the ballistic
solver, physics timestep, and iteration order over structures are all places
where non-determinism creeps in, and it is far cheaper to keep determinism than
to restore it.

**Verification.** The original is available for reference (the Steam
re-release), so parity claims can be checked rather than argued:

- a headless harness that loads a map + command log and asserts the resulting
  state hash — this is the regression suite for every rule in [units.md](units.md)
  that we implement;
- **golden replays** for the awkward rules (cascade, Power Spike taper, EMP
  disable, anti-air reload, Balloon death, Shield pass-through, wrap-around
  range);
- side-by-side play against the original for anything the manual describes
  qualitatively — launch ranges, flight arcs, terrain heights, the blast
  falloff curve — which is how the tuning table in §6 gets filled in.

**AI.** One planner, written so that personality is data (a weights resource)
when more opponents arrive.

**Documentation.** `AGENTS.md` is the entry point for this repo and currently
describes a game that will no longer exist. Update it in the same commit as the
code it describes, and keep [units.md](units.md) authoritative by citing it from
the code that implements each rule.

---

## 6. Tuning table (the unknowns)

These have no published value. Collect them in one place — a resource, not
scattered `[Export]`s — and record what the original actually does as it is
measured. **This table is expected to change; nothing else in the plan is.**

Note what is *not* here: hit points, damage, energy costs, income and the clock
are all published in the manual and belong in the unit definitions and the
economy code, not in a tuning table.

| quantity | original value | ours | notes |
| --- | --- | --- | --- |
| Hub launch range (flat ground) | — | 20 (today's `MaxLaunchForce`) | varies with terrain height |
| Cord segment / node spacing | — | — | also sets the 50-node Power Spike reach |
| Cord reach from a launcher | — | — | the base build radius; only Hubs grant it |
| Terrain height per state | — | — | Ground / Plateau / Mountain |
| Blast falloff curve | a bomb does partial damage to nearby units; the curve is unpublished | full inside 1.5 units, half out to 3 | **our model, not a transcription** |
| Cluster bomblet damage | "1.5 × 3" in the manual's shorthand | — | three bomblets, each weaker than a Bomb; the split may not survive integer hit points |
| Pool sizes (small/medium/large) | — | 6.0 (`PoolSide`) | "the larger the pool, the more Harvesters fit" |
| Projectile gravity / arc | — | 9.8 (`Projectile.Gravity`) | |
| Anti-Air range | "smaller than an EMP's" | — | |
| EMP radius | larger than Anti-Air and Shields | — | |
| Shield radius | — | — | |
| Vision radius (Hub / Balloon) | — | 12 (placeholder) | |
| Turn clock default | **60 s** | — | documented |
| Max stored energy | **35** | — | documented |
| Hub income | **7**/round | — | documented |
| Harvester income | **3** on pool, **1** off | — | documented |
| Bot personality weights | 10 personalities | — | later; data-driven when it lands |

---

## 7. Risks

| risk | why it bites | mitigation |
| --- | --- | --- |
| **Scope.** Rules+content+mode parity is still a large project with the roster trimmed. | Morale and half-finished systems. | Milestones are independently shippable. Stop after M1 and you have a better game than today. |
| **Cut features creeping back.** Wind, water, multiplayer, challenge missions and six units each have a plausible "while we're here". | The plan loses its edge and nothing finishes. | §1 lists every cut with its cost, and the checklist in §8 marks them as deliberate. Re-adding one is a decision to make in the open, not a detail to slip in. |
| **M0 refactor churn.** | Everything is edited at once and nothing works. | Keep `GameManager.Instance` and the old ammo path alive behind the new one; land M0 with the game still playable (that is its exit criterion). |
| **The spec documents more than we build.** [units.md](units.md) describes eighteen units, water and wind. | Someone implements a rule for a unit that does not exist. | §1 is the scope; read it before the spec. |
| **Wrapping maps touch everything.** | Discovered late, it invalidates distance/targeting/camera code. | It is in M3, before the last of the units and the AI; do the world↔map maths once, centrally. |
| **Determinism retrofitted.** | Replays break subtly, and a second player would too. | M0.5, before any consumer of it. |
| **Unknown numbers.** | "Parity" arguments with no referee. | Tuning table, measured against the original, kept in one resource. |
| **Original assets.** | Copyright. | Behaviour and layout parity only; new art, placeholder audio. |
| **`GameManager` god object.** | Every feature edits one 1355-line file and conflicts. | M0.6. |

---

## 8. Parity checklist

Track progress here. Each line is a claim that can be demonstrated. Lines
struck through are **deliberately out of scope** (§1) — do not "fix" them.

**Rules**
- [ ] Energy 1/3/7, max 35, per-round refill, carry-over
- [ ] Round ends when both players spend out or skip
- [ ] Player order shifts left each round
- [ ] Skip forfeits the round's remaining turns
- [ ] Hit points per unit type; damage subtracts; partial damage is normal
- [ ] A weapon damages what it strikes **and** what is near it, on the tuning curve
- [ ] Cords cannot overlap; illegal launches refused
- [ ] Nothing but a launcher grants build reach — and only a Hub is a launcher
- [ ] Cord nodes take no damage except from a Power Spike
- [ ] Hub destruction cascades recursively through the network
- [ ] Units must land on flat ground
- [ ] Terrain height modifies launch range (downhill helps, uphill hurts)
- [ ] The map wraps in both axes
- [ ] Win = destroy the opponent's main Hub
- [ ] ~~Water landings, Bridges, Crawlers under water~~ — cut with water
- [ ] ~~Wind bends shots~~ — cut

**Units** — the twelve in [The twelve units](#the-twelve-units), each with its
stated exceptions
- [ ] Level 1: Anti-Air, Bomb, Cluster, Repair
- [ ] Level 3: Balloon, EMP, Missile, Spike
- [ ] Level 7: Crawler, Harvester, Hub, Shield
- [ ] ~~Bridge, Tower, Mines, Reclaimer, Offensive Unit, Virus~~ — cut

**Options**
- [ ] Clock (default 60 s)
- [ ] Fog (black unseen area; explosions still visible)
- [ ] ~~Wind~~ — cut
- [ ] ~~Teams~~ — cut with >2 players

**Modes**
- [ ] 1v1 setup: colour, name, the single BOT slot, map, clock, fog
- [ ] Replay Theatre: record, save, play/pause, per-player view, near-transparent fog
- [ ] Game summary: Units, Energy, Shots, Hits, Destroyed, Accuracy, Bonus
- [ ] Map editor: size, terrain, pools, 1v1 start positions, save/load (M9, last)
- [ ] ~~Challenge mode: 16 missions~~ — deferred
- [ ] ~~Multiplayer: LAN + internet, lobby, chat~~ — deferred

**Interface**
- [ ] Energy bar with next-round income indicator
- [ ] Build menu on 1/3/7 pages
- [ ] Launch power meter
- [ ] HUD: time, current player, player order, next-round energy
- [ ] Hub scrolling (next/previous/center)
- [ ] Keys: D, R, C, N, P, L, A, remappable
- [ ] Track In / Track Out, Smart Camera, Auto Return, Auto Scroll, Auto Hub Select (all default off)
- [ ] Damage bar and smoke-severity feedback
- [ ] Placeholder SFX (launch, impact, explosion, anti-air, destruction, UI) with a master volume

---

## 9. Where to start

**M0.1–M0.3, then M1.** Concretely, the first commit is `UnitDefinition` plus
the hit-point model, because until a unit is data and durability is per unit
type, every unit added is throwaway work. The second is the energy/round loop,
because that is the difference between a duel and MoonBase Commander.

The earliest point at which this is a *game* rather than a prototype is the end
of **M1**: energy, rounds, income, a build menu, and Hubs and Harvesters
expanding across the development map. That is worth reaching before deciding how
far down the rest of the list to go.
