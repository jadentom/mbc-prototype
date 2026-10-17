# From prototype to spiritual sequel — the plan

**MbcPrototype is not a remake of *MoonBase Commander*.** It is the
systems-and-feel **base for a spiritual sequel**: a turn-based, cord-network
artillery-strategy game of our own, using the 2002 original as the reference for
what this genre's mechanics should feel like when they work. The original's
manual is our starting dataset, and its numbers are a **first guess** — not a
truth we are chasing.

So this document plans the **base**: the systems, the data, the tuning workflow,
and the first playable slice. It stops there. The sequel's own roster, balance,
content, art and differentiation are deliberately *not* planned here — §11
exists to say that and nothing more.

Companion documents:

- [units.md](units.md) — the original's roster, economy, cord rules and world
  rules, transcribed from the official manual. Reference data and a starting
  balance, **not** a specification of our game.
- [../camera/README.md](../camera/README.md) — the existing camera/minimap
  documentation, which the map work below will rewrite.
- [../../AGENTS.md](../../AGENTS.md) — the project guide; keep it in step as
  this plan lands.

---

## 1. What this is for

### What the base must be

Three properties, in priority order. When they conflict, the higher one wins.

| the base must be | because |
| --- | --- |
| **playable early** | everything we learn about this genre comes from playing it. A system we cannot play is a system we cannot judge. |
| **tunable without code** | every number will change, repeatedly, during balancing and iteration (§2). If changing one means an edit, a rebuild and a merge, iteration stops happening. |
| **expandable without rework** | more players, maps and units than the first slice must arrive as *content*, not as a refactor. |

Note what is deliberately **not** on that list: *faithful*. The original is a
reference bar, not a deliverable. A decision that makes iteration slower is the
wrong decision even when it is the more faithful one.

### The first playable slice

**One 1v1 match: one human against one AI, on one hardcoded map.** That is the
whole committed scope of the base. Everything else in this document either
serves that or is explicitly deferred.

| area | decision |
| --- | --- |
| **Players** | two, 1v1: one human and one AI. No teams. |
| **Map** | **one hardcoded map** for the whole of development. The map editor is the last milestone (M9); until then, authoring content is not the bottleneck, iterating on balance is. |
| **Units** | **twelve** of the original's eighteen — see [the starting roster](#the-twelve-units). Every one of their numbers is provisional. |
| **AI** | **one** opponent planner. Personalities (the original's ten BOTs) are later data, not now. |
| **World** | a height field with three terrain states, and a **wrapping map**. |
| **Fog** | yes, as a game option — it is what makes the Spy Balloon worth building. |
| **Energy & rounds** | the 1/3/7 economy, max 35, per-round refill, rotating player order. Provisional like everything else. |
| **Cords** | the full spatial rules: cord geometry, no overlap, reach, cascade, lineage lights. This is the mechanic the whole genre rests on, so it is worth getting structurally right before tuning it. |
| **Fidelity** | the original's *feel* is the reference; its numbers are the opening bid. Art and audio are ours — its assets are not redistributable. |

### Cut from the first slice

Each of these is a decision about the first slice, not a verdict on the sequel.
The right-hand column is what disappears with it, so nobody rediscovers it later
and treats it as a bug.

| cut | why | what it costs |
| --- | --- | --- |
| **Wind** | not wanted in the first slice | one option, and shots that bend. Nothing else depends on it. |
| **Water** | not wanted in the first slice | the fourth terrain state; **Bridges**; structures being destroyed by landing in water; the Crawler's ability to travel under water. Terrain becomes a height-and-cover puzzle instead of a river-crossing one. |
| **GameSpy-era matchmaking** (its lobbies, its chat addressing, its discovery model) | period infrastructure. If the sequel wants network play it will be designed for the sequel, not for 2002's service. | nothing today. The command log (§4, M0.5) is what keeps that door open — see *Built for expansion*. |
| **Three- and four-player games, and teams** | 1v1 first | shared line of sight, allied interception rules, side bars for defeated players. |
| **The other six units** (Bridge, Tower, Mines, Reclaimer, Offensive Unit, Virus) | the roster is trimmed to what the first slice needs | see *The six units we are not building*, below and in M4 |
| **Challenge mode** (16 missions) | single-player content that needs the mission format and authored maps | the ladder and its efficiency bonuses. |
| **Factions, music, voice commentary** | cosmetic or unshippable | nothing mechanical: factions differ only in art, and every faction has the same eighteen units. |

### Built for expansion

Only 1v1 ships, but the following must hold from M0 — retrofitting them later is
what makes "we'll add players later" expensive:

- the match spawns a **list of combatants**, not two named fields, and the mode
  ("1v1") is a setup parameter rather than an assumption in the code;
- `Combatant.Team` stays, and both the rotation and the defeat check stay
  team-aware, even though every game today is one team each;
- units are **data** (`UnitDefinition` in a catalog) and the map is a
  **resource**, so a second player, a second map, a second launcher and a whole
  second roster are content rather than code;
- a unit's behaviour lives on a scene behind a small interface, so a *new* unit
  is a definition plus a behaviour, not a new branch in a launcher;
- the **command log** is player-agnostic and serializable;
- no code may assume "the enemy" is a single object, or that a match has exactly
  two of anything.

### What we take from the original

Four reference bars. They are how we know the base is *sound* — not a promise
about what we ship. Reaching them means the mechanics work; matching the
original's numbers is explicitly not the goal.

| bar | meaning | reached when |
| --- | --- | --- |
| **Rules** | the same mechanics and edge cases exist and behave sensibly | a player who knows the genre is never surprised by *what happens* |
| **Content** | the twelve units, the terrain and the pools exist | a player's favourite strategy **within the subset** is possible |
| **Mode** | a 1v1 match can be set up, played and watched back | the base is playable end to end |
| **Presentation** | HUD layout, key commands, damage feedback | the game *reads* at a glance |

Full content parity — eighteen units, four-player games, teams, challenge
missions, internet play — is not a goal; see §11.

### Where the numbers come from

The manual publishes hit points, damage, energy costs and the objective. It does
**not** publish launch ranges, cord lengths, the blast falloff curve, pool sizes,
terrain heights or bot personalities. Both kinds are provisional: the first is a
reasonable opening bid, the second a guess. All of them live in data and are
listed in one place — §2 and §7. Do not scatter them, and do not treat a number
being "from the manual" as a reason it cannot change.

---

## 2. Tunability — the load-bearing requirement

**Every gameplay number will change, repeatedly, and changing one must be
cheap.** That is not a nicety about this genre; it is the whole point of building
a base rather than a game. Balance is found by iterating, and iteration is what
the sequel will spend most of its time doing.

Concretely, four rules. They are not aspirations; they are what M0.7 and §7 are
for.

1. **No gameplay number in a `.cs` file.** Hit points, damage, blast radii,
   energy costs, income, launch ranges, cord reach, vision radii, the clock, the
   energy cap — all of it lives in a Godot `Resource` a designer can open and
   edit.
2. **One home per number.** A number that exists in two places is a bug, because
   sooner or later the two disagree. Today's four `EnemyCombatant` knobs are
   exported on `GameManager` *and* injected at construction: that duplication is
   the pattern to delete, not to copy.
3. **A number must be changeable without a rebuild**, and ideally without a
   restart. Resources are editable in the editor and reloadable at runtime;
   M0.7 adds a dev overlay that edits live values in a running match and dumps
   them back into the resource. If a balance change takes longer than the change
   is worth, it will not get made.
4. **Derived values are functions, not constants.** Effective launch range on a
   slope, damage at a distance, income at the end of a round: computed from the
   data, never a second hand-maintained number.

Defaults in code are allowed for exactly one reason — the project must still run
with no assets — and those defaults are fallbacks, never the source of truth.

**What this buys us.** Because the whole game state is a function of (map, seed,
command log) from M0.5, a replay exercises the live numbers: change a damage
value, replay a recorded match, and see what changed. Balance iteration becomes
"edit, replay, compare" instead of "edit, rebuild, play for ten minutes, hope".

**Where each number lives** is §7 — one table, every tunable value, its home and
its current starting value.

---

## 3. Where the prototype stands

Verified against the code today. The right-hand column is the gap this plan
closes; the **size** column is a rough ordering device, not a promise.

| system | reference | prototype today | size |
| --- | --- | --- | --- |
| **Objective** | destroy the opponent's **main Hub** | destroy the enemy chain's root — `GameManager.EvaluateGameOver()` treats "no root" as defeat | ✅ aligned |
| **Cord network** | the spatial system: reach, overlap rules, cord nodes, cascade, lights | a link is a `Cord` object — two endpoints, its length, the cable — and the destruction cascade runs down it (M0.3). Still no cord nodes, no reach, no overlap test, no lights | **XL** |
| **Durability** | **hit points** per unit type (the manual's *Armor* column), damage subtracted, partial damage normal | per unit type, from the unit's own resource: `UnitDefinition.HitPoints` (Hub 5), read by `Structure.MaxHealth` | ✅ M0.2 |
| **Blast damage** | a weapon damages what it strikes **and** what is near it, with a falloff curve nobody published | the two-band model survives, but every number in it is data: radii and damage on the unit, the band share on `MatchRules`; the Bomb does the manual's 3 | ✅ M0.2 |
| **Energy** | 1/3/7 per unit, max 35, per-round refill, income units | none | **L** |
| **Turn model** | a turn is an energy budget; round ends when all have spent or skipped; order rotates left | one launch per turn; `CurrentTurn` counts rotation wraps | **L** |
| **Units** | 18, three levels | 2 of 12 as resources: `hub` and `bomb` in a `UnitCatalog`; `AmmoLauncher` takes a `UnitDefinition` and dispatches through `ILaunchedAmmo` | **L** — ten units to go, and none of them needs a launcher change |
| **Tunability** | (our own requirement, §2) | the roster, its numbers, the blast curve, the launch ballistics and the AI's profile are all resources; no gameplay number is left in a `.cs` file. The F1 dev overlay edits any of them live and writes them back, and `Scenes/harness.tscn --tune=…` does the same headlessly | ✅ M0.7 |
| **Players** | up to 4, teams, shared line of sight | exactly 2, hardcoded in `GameManager.CreateCombatants()` | **M** — generalise the shape, not the count |
| **Map** | authored, sized, terrain states, wrap-around | one `MapDefinition` resource: bounds, a height field (flat today), the two 1v1 start positions and the pool field. The minimap and the pools read it (M0.4). Still flat, still unwrapped | **L** — M3 makes the world wrap and the heights matter |
| **Terrain height** | height changes launch range | none — everything sits at y=0 | **M** — three states, not four |
| **Purple pools** | energy pools; size determines how many Harvesters fit | `PoolField` scatter, decorative, `PoolAt`/`OverlappingPools` written but **never called** | **S** |
| **Fog / vision** | unseen terrain black; Hub and Balloon vision | none | **L** |
| **Anti-air / shields / EMP** | interception and disable | none | **L** |
| **AI** | opponents that play differently | one `EnemyCombatant`: random aim in a 25° cone, random power, alternates node/bomb | **L** — one good planner, not ten |
| **Modes** | skirmish setup | one hardcoded skirmish | **M** — a 1v1 setup screen |
| **Map editor** | size, terrain, pools, starting positions, save/load/share | none | **L** — **last** (M9) |
| **Replays / stats** | Replay Theatre + game summary | a command log recording every action from both seats, with the seed and map, written and read as JSON, plus a state hash to check a replay against (M0.5). No Replay Theatre, no save/load, no summary, no UI | **M** — M7 is the theatre and the summary |
| **HUD / keys** | energy bar, build menu, HUD bar, D/R/C/N/P/L/A/Tab keys | power bar, two ammo icons, turn label, win/lose labels; `fire_shot`, `aim_left/right`, `switch_ammo_next/previous` | **M** |
| **Audio** | music, voice, SFX and volume channels | nothing at all; the repo has no audio assets | **S** — placeholder SFX only |

### The architectural blockers

These are why the plan starts with a foundations phase instead of "add unit 3 of
12". Each is a structural decision that every later feature depends on, and
retrofitting any of them after the content exists costs several times more.

1. ~~**Units are code, not data.**~~ **Landed in M0.1.** `AmmoType` was a
   two-value enum and `AmmoLauncher.Launch()` an `if/else` over two
   `PackedScene` fields, with a
   `// TODO: Switch this to inheritance and use an interface` sitting on line 62.
   A unit is now a `UnitDefinition` resource in a `UnitCatalog`, and the
   launcher's dispatch is the behaviour interface `ILaunchedAmmo`. Twelve units
   are twelve resources, not twelve branches.
2. ~~**Durability is one hardcoded number and the blast curve is invented.**~~
   **Landed in M0.2.** `MaxHealth` was 6 for every node, reverse
   engineered from this repository's own bomb (three direct hits, "a half hit is
   a whole point"). Hit points now come from the unit's definition (Hub 5) and
   the blast's radii and damage from the firing unit, with the falloff between
   them on `MatchRules`. The two-band shape is still our *model* of a curve the
   manual never published — legitimate as a first guess, and now a number a
   designer can change rather than a constant inside `Bomb.cs`.
3. **There is no cord graph.** Build legality, the destruction cascade, Power
   Spike, EMP's reach along a network and the lineage lights are all cord
   operations. Today "chain" is an ad-hoc parent pointer.
4. **There is no round/energy model, and a turn is exactly one shot.** The turn
   system in `GameManager` (`RegisterTurnEvent` / `CompleteTurn` /
   `AdvanceTurn`) commits the turn *synchronously* on the first launch and has
   **no end-turn input at all** — a turn ends only by firing. "Spend an energy
   budget across several launches, then pass" is a different shape, and the
   rotation (a linear scan of `_combatants`) needs an explicit end-of-turn
   concept it does not have.
5. ~~**There is no map.**~~ **Landed in M0.4.** The 10000×10000 plane with
   `MinimapWorldSize = 475` standing in for bounds, flagged in `AGENTS.md` and in
   the camera doc's *Scaling seam* section, is replaced by
   `MapDefinition` — bounds, height field, pools and start positions in one
   resource, with one development map shipped. The plane is still what the
   ground *is* (M3's wrapping world is what gives the bounds teeth), and the
   height field is painted but flat.
6. ~~**No determinism, no serialization.**~~ **Landed in M0.5.** A match is now a
   function of (map, seed, command log): every action from either seat goes
   through one entry point that records it and applies it, structures carry stable
   ids, the simulation's randomness comes from a seeded generator, physics runs on
   a fixed step, and `Scenes/harness.tscn` replays a log and asserts the state
   hash it lands in. What is still wall-clock-paced — the AI's think time and the
   cascade's one second per generation — is called out in `AGENTS.md`; the cascade
   belongs to M2.
7. ~~**`GameManager.cs` is a 1355-line god object**~~ **Landed in M0.6.** Turn
   flow, camera/minimap, map/world and match setup are separate nodes, and the
   match no longer knows how many combatants it has: `MatchSetup` makes one per
   seat the map defines. `GameManager` is still the entry point and the facade
   call sites use (`GameManager.Instance` was kept on purpose, per this plan), so
   it is ~1000 lines of input, HUD and forwarding rather than 1831 of everything —
   M8 takes the HUD and M7 the summary, and the forwards go with them.
8. **Tuning lives in code, and sometimes twice.** Every gameplay number is a C#
   field default, and the four `EnemyCombatant` knobs are *also* exported on
   `GameManager`, so the same numbers exist in two places. §2 makes this the
   first thing to fix, not the last.
9. **Single-player is baked in.** `_playerCombatant` is one field read directly
   by the defeat check, and the enemy's start position is derived from the
   camera's size and the window aspect — not from a map. Both have to go, and
   both are the same edit that lets a map and a combatant list arrive.

---

## 4. Milestones

Each milestone ends with **exit criteria** that are checkable, not vibes. Order
is dependency order, except where noted as parallelisable. The exit criteria ask
"does this system work, and can we tune it?" — never "does it match the
original?".

### M0 — Foundations

The load-bearing refactor. No new player-visible content; everything after this
is cheap because of it.

- **M0.1 — Unit definition resource.** **Landed.** `UnitDefinition` is a Godot
  `Resource` carrying: id, display name, level/energy cost, hit points, damage,
  blast radii, build permission, vision radius, projectile scene, and behaviour
  flags. `AmmoType` is gone; `AmmoLauncher` takes a `UnitDefinition` and
  dispatches through `ILaunchedAmmo` rather than the
  `if (instance is Projectile) … else if (instance is Bomb)` chain. Units live
  in the `UnitCatalog` resource, which the HUD, the launcher and the AI all read.
- **M0.2 — Hit points and the blast curve.** **Landed.** Durability lives in
  the definition: **hit points per unit type**, starting from the manual's
  *Armor* column (Hub 5 — the only structure in the roster so far). The
  "MaxHealth = 6 because a half hit is a whole point" reasoning in
  `Structure.cs` is deleted along with the one-number-for-everything default.
  - Damage is a **flat integer subtracted from the target's hit points**: a Bomb
    (damage 3) against a Hub (5) leaves 2, and partial damage is normal. There
    is **no armor threshold** — nothing in the original says damage that fails
    to exceed a unit's armor does nothing, and an earlier draft of this plan
    claimed otherwise. Read "Armor" in [units.md](units.md) as *hit points*.
  - A weapon also damages what is **near** where it goes off, with a falloff the
    manual does not publish. The two-band model (full damage inside the unit's
    `DirectHitRadius`, `MatchRules.BlastOuterBandFraction` of it out to its
    `OuterRadius`, nothing past it) stays as the opening guess, with the radii on
    the unit definition and the band share in the match rules, and the Bomb now
    does the manual's damage 3 rather than the old 2 — its splash is 1.
- **M0.3 — Structure vs. cord split.** **Landed.** `BaseNode` meant both "a
  unit" and "the link in the network"; it is now a `Structure` (hit points,
  ownership, the cords it hangs from and anchors) and a `Cord` (the geometry
  between two structures, the cable, and later the cord nodes). A structure is
  the root of a chain when no live cord hangs above it, and the destruction
  cascade runs down its cords. Reach, overlap, lineage lights and Power Spike
  travel now have an object to be rules *of* — which is what M2 needs, and what
  makes twelve units possible.
- **M0.4 — Map resource, and the development map.** **Landed.** `MapDefinition`
  holds bounds, a height field and the three terrain states, the energy pools
  (with size), and the two **start positions** for a 1v1 match. Exactly one is
  shipped — `Resources/Maps/development_map.tres`, the map every milestone is
  developed and tuned on. `GetMinimapWorldRect()` and `CreateMinimap()`'s camera
  size come off it, through exactly the *Scaling seam* the camera doc specifies;
  `MapDefinition.CreatePoolField()` scatters the pools over the map's own bounds
  rather than over the region the minimap happened to show; and both chains now
  spawn at the map's start positions — the player's is no longer a node in the
  scene, the enemy's is no longer two screen widths to its right
  (`EnemyStartScreensRight` is gone, and with it the window-aspect dependency).

  The height field is *painted but flat*: every cell is ground, and the per-state
  heights (ground 0, plateau 2, mountain 4 — our first guess) are read by nothing
  until M3 turns them into terrain, the height→range curve and the flat-ground
  landing rule. Water is not a state at all (§1).
- **M0.5 — Command log & determinism.** **Landed.** Every player and AI action is
  a serializable `MatchCommand` appended to an ordered `CommandLog`, and the
  simulation only ever advances by `GameManager.ApplyCommand` — the player's shots
  and the AI's both go through `SubmitCommand`, which records and applies.
  Commands name their seat, their unit and their origin structure by id, and
  structures get those ids in creation order, so a log means the same thing on
  every run. Randomness comes from the match's own generator seeded by
  `MatchSeed`; physics-affecting steps run on the fixed 60/s step; and a state
  hash makes a replay checkable. This is what makes replays (M7), regression
  testing (§6) and iteration-on-a-replay (§2) possible — and the harness already
  does all three.
- **M0.6 — Split `GameManager`.** **Landed.** Four parts, each owning one job:
  `MatchWorld` (the map, its bounds and its pools), `MatchCamera` (centering,
  panning and the whole minimap), `TurnController` (the rotation, the turn events,
  the round, the outcome) and `MatchSetup` (the roster, the rules, the seed, the
  command log and the combatants). `TurnController` announces `TurnStarted` and
  does not act for a seat — input, the AI or a replay does — which is what let the
  replay driver stay out of the turn system. The match stops knowing how many
  combatants there are: one is created per seat the map defines, so a three-seat
  map is a three-way match. `GameManager.Instance` was kept, as this plan asked,
  so call sites did not all change at once; what remains in it is input, the HUD,
  selection, the command log and the structure registry.

  Landed with the M0.5 harness as the check: the same command log produces the
  same state hash as before the split (`c6cd89abf0971af6`, 10 rounds, seed 7),
  which is how a refactor this size is known to be behaviour-preserving.
- **M0.7 — Tuning data and the dev overlay.** **Landed.** Every number §7 lists
  for the systems that exist is in a resource, with the code's literals deleted:
  - **Launch ballistics** — gravity, the launch force range, the upward bias and
    the muzzle height — moved off `GameManager`'s exports and into
    `MatchRules`; the ammo reads them from the rules it was launched with, so
    there is one launch path and one set of arcs.
  - **Reach per launcher** is a unit number: a launcher's own
    `UnitDefinition.LaunchForceScale` multiplies the match's force range, which
    is what §7 means by "launch force range per launcher" and what will let a
    second launcher out-throw the Hub without a code change. `MatchRules` owns
    the derivation, so the lerp exists once.
  - **Aiming and charging** — the aim rotation speed and how fast the power
    meter fills — are rules too. They are balance, not presentation: they decide
    how long a player has to place a shot.
  - **The AI's numbers** were exported three times (on `GameManager`, on
    `MatchSetup` *and* on `EnemyCombatant`, injected from each into the next) and
    are now one `AiProfile` resource the combatant reads itself. That triplication
    is exactly the pattern §2 rule 2 names as a bug waiting to happen.
  - **The dev overlay** — F1 in a running match — lists every number the match's
    resources export, changes it live (arrows; Shift for ×10, Ctrl for ÷10), and
    writes the lot back into the `.tres` files on Enter. `R` re-reads them from
    disk, which is the other half of "changeable without a restart": a value
    edited in the editor while the match runs is picked up without relaunching.
    It shows 34 numbers today and no list of them is maintained by hand — it is a
    view over the resources, so the next number added to one appears in it.
  - **The same surface is reachable from the harness** (`--tune=bomb.Damage=6`,
    optionally `--save-tuning`), so "change a damage value, replay, compare the
    state hash" is a command and not an afternoon — §2's iteration loop, and how
    the write-back is tested without a window.

  Landed behaviour-preserving: the M0.5 harness produces the same state hash as
  before the move (`c6cd89abf0971af6`, 10 rounds, seed 7), and the same hash
  changes the moment a number does.

**Exit criteria:** the game still plays as it does today, on the development map,
with the two existing units; a match is fully described by (map, seed, command
log) and replays identically from it; and **a damage value can be changed and
felt without touching a `.cs` file or restarting the editor**. All three hold:
the harness replays the log to the same hash, and
`--tune=bomb.Damage=6` on that replay lands structures at different hit points
and a different hash.

### M1 — The core loop: energy, rounds, turns, build menu

This is the milestone that makes it *this genre* rather than a projectile duel,
and it is worth doing before more units. Every number below is a starting value
from §7, not a target.

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
> produce different UIs (a persistent build menu vs. a one-shot launcher). If it
> is genuinely ambiguous, pick the one that is easier to tune and revisit.

**Exit criteria:** a full round can be played with correct income, spend,
carry-over and rotation, using no units other than **Hubs and Harvesters** — and
the income and cost numbers can be changed from data while the match runs.

### M2 — The cord network

The spatial rules that make the map a puzzle.

- Cords as first-class geometry between a structure and its parent, with
  **cord nodes** at intervals (not units; no damage; Power Spike only).
- **Build legality**: a launch is refused if the resulting cord would overlap an
  existing cord, or if the target site is outside reach.
- **Reach is a property of the launcher, not of the network.** A structure must
  be launched from a structure already in your network, and how far that
  launcher throws is its own tunable number (§7). Nothing increases it.
  Expansion works because **a Hub can build another Hub**, and each new Hub is
  itself a launcher standing further out: the network grows one Hub hop at a
  time.
  - Note this for the record: the **Tower grants vision only** in the original —
    it does not extend cord reach. An earlier draft of this plan had it
    backwards. (The Tower is out of the first slice anyway, but the reason
    matters: no in-scope unit grants reach, so reach has to work from launchers
    alone.)
- **Hub destruction cascade** over the cord graph — destroying a Hub destroys
  everything built by it, recursively, including other Hubs. Note the current
  implementation waits **1.0 s per generation** (`Structure.Destroy()`), so a
  realistic network of 40-odd units would take tens of seconds to collapse and
  would hold the turn open the whole time via `TurnEvent`. The cascade needs to
  fan out concurrently, or be resolved as a graph operation with one animation
  budget — this is a pacing problem, not a parity one.
- **Lineage lights**: cord lights blink toward the main Hub, so a player can
  trace an opponent's network back to its origin. Today's `CableShader` is the
  canvas for this.
- **Power Spike** travel: damage down both directions, tapering, dead after a
  tunable number of cord nodes (50 in the original). The travel lives here
  because it is a cord operation; the unit that fires it is M4.

**Exit criteria:** you cannot build outside reach, cords visibly refuse to
overlap, and killing a Hub takes its whole subtree with it — with reach and cord
spacing tunable per launcher.

### M3 — Terrain and the wrapping world

- Height field with **three** states — **Ground, Plateau, Mountain**. The
  original's fourth state, **Water, is out of the first slice** (§1): with it go
  Bridges, structures destroyed by landing in water, and the Crawler's water
  travel.
- The rule that new units must land on **flat ground** (a structure landing on a
  hillside is destroyed on impact).
- **Height changes launch range**: shots from high ground go farther, and shots
  *uphill* fall short. The height→range relationship is one tunable curve, not a
  per-state constant — it is exactly the kind of number that will be rebalanced
  twenty times.
- **Wrapping map**: leaving one edge re-enters on the other, in all
  world↔map maths — minimap, camera bounds, distance queries, cord reach, AI
  targeting. Wrapping is a cross-cutting concern, not a boundary condition: do
  it here or fight it forever.

**Exit criteria:** the development map, given a mountain and a plateau, plays
differently from a flat one; nothing breaks at the map seam; and terrain heights
and the height→range curve are data.

### M4 — The starting roster: twelve units

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

Our names on the left, the manual's on the right. Levels, hit points and damage
are the **opening bid** from the manual — first values to iterate from, all of
them living in the unit's definition (§2, §7).

| unit | manual name | level | HP | damage | what makes it work |
| --- | --- | --- | --- | --- | --- |
| **Bomb** | Bomb | 1 | — | 3 | the baseline weapon; detonates on a unit or solid ground |
| **Repair** | Repair | 1 | 1 | heals 1 | restores 1 hit point to **any** unit it hits, enemy ones included |
| **Cluster** | Cluster Bomb | 1 | — | 1.5 × 3 | splits into three bomblets at the apex; its real value is drawing anti-air fire |
| **Anti-Air** | Anti-Air Missile Launcher | 1 | 3 | — | shoots down enemy projectiles in range; **inactive one full turn cycle** while it reloads |
| **Missile** | Guided Missile | 3 | — | 3 | homing engages at the apex and flies at the closest enemy unit; a Bomb's damage if it arrives |
| **Balloon** | Spy Balloon | 3 | 1 | — | vision without a cord; anything that touches it destroys it |
| **EMP** | EMP | 3 | — | 2 | disables everything in a radius for one turn cycle; radius **larger than Anti-Air's or a Shield's** |
| **Spike** | Power Spike | 3 | — | 3 | travels a cord in both directions, tapering, dying after a set number of cord nodes |
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
  a second launcher should be data, and it is a likely sequel addition.
- **Virus** — the cord-hopping disable. EMP is the only disable in the game.
- **Mines** — area denial, and the reason to watch where your units stand.
- **Reclaimer** — the only unit that turns absorbed hit points into energy.
  Income is now the main Hub plus Harvesters, and nothing else.
- **Bridge** — already gone with water.

**Exit criteria:** each of the twelve is buildable and behaves as described,
including its stated exceptions (the Anti-Air reload, the Crawler's detonation
condition, the Shield passing friendlies, the Balloon dying to a touch); the six
cut units appear nowhere in the catalog; and adding a thirteenth unit is a
definition plus a behaviour rather than a code change in the launcher.

### M5 — Fog and vision

- **Fog of war** as a game option: unseen terrain renders black, hiding units
  and terrain; vision expands as structures spread; **explosions and smoke stay
  visible through fog** (an easy rule to miss that matters a lot in play).
- Vision comes from the **Hub** and the **Spy Balloon** — and the Balloon's
  whole point is that it needs no cord.
- Vision must compose with M3's wrapping, which is the fiddly part.

**Exit criteria:** a fog game is genuinely about scouting, and a Balloon placed
forward is worth its cost.

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
  Its weights live in a resource (§2), because tuning the AI is balance work
  too. Personalities are **not** in scope: when more opponents arrive they
  should be weight sets, not subclasses.

**Exit criteria:** a 1v1 fog game against the AI runs to a correct conclusion —
the AI spends its energy sensibly, expands, and can win and lose — and its
aggression is tunable without a rebuild.

### M7 — Replays, save/load and the game summary

- **Replay Theatre**: record every match to the command log from M0.5; watch it
  back with play/pause, Track In / Track Out, and per-player view. Fog is
  **nearly transparent** in replay so you can see the whole map while still
  understanding where Balloons went.
- **Save/load** of an in-progress match, from the same command log.
- **Game summary**: Units built, Energy generated, Shots, Hits (drawing anti-air
  fire explicitly does **not** count), Destroyed, Accuracy, Bonus.

Replays are not only a feature: they are the iteration loop from §2. A replay is
what makes a balance change reviewable.

**Exit criteria:** any finished match can be saved and replayed frame-accurately,
the summary numbers are right, and a tuning change shows up in a replay.

### M8 — Presentation and interface

- **HUD bar**: time, current player's turn, **player order** (which visibly
  shifts left each round), and the **Next Round Energy** indicator.
- **Energy bar** with its numeric readout and the energy cap (today's `PowerBar`
  is a different thing — the launch meter — and both must coexist).
- **Hub scrolling**: Next Unit, Previous Unit, Center Current Unit.
- **Key commands**: Damage Bar (**D**), Range Radius (**R**), Center Unit
  (**C**), Next Unit (**N**), Previous Unit (**P**), Closest Launcher (**L**),
  Show Attacked (**A**), plus a **remappable controls** screen. None of these
  exist today, and **R** (anti-air and shield range display) is genuinely
  load-bearing for play — and becomes more so as those radii are tuned.
- **Navigation**: right-drag to scroll, left-click to select, drag a unit to
  scroll, click a unit's picture to re-center, click a cord to jump to the unit
  at its far end, arrow keys to scroll.
- **Track In / Track Out** camera modes with **Smart Camera** and **Auto Return
  Camera**; plus Auto Scroll and Auto Hub Select. (All default **OFF** in the
  original — keep those defaults.)
- **Damage feedback**: damage bar and the **smoke colour that encodes severity**.
- **Audio**: **placeholder SFX only** — launch, impact, explosion, anti-air
  fire, structure destroyed, UI click — plus **one master volume** and a mute.
  No music, no voice, no commentary. Placeholders are ours and are meant to be
  replaced; pick neutral ones so a change of art does not force a re-tune.

**Exit criteria:** a player who knows the genre can drive the game without being
told, and the numbers on screen match the tuning data.

### M9 — The map editor and authored maps

Last by decision, not by dependency: the whole of development runs on the one
hardcoded map from M0.4, and iterating on balance does not need a second map.

- **Map editor**: map size, the three terrain states, energy pools of varying
  size, and **starting positions** — validated for 1v1 as a pair of positions,
  with the setting left open for larger icon sets.
- Clear / load / save, and terrain/planet selection.
- **Authored maps**: the editor's output is what the game loads, and the
  hardcoded development map becomes the first entry in that set rather than a
  special case in code.

This is where the prototype stops being a base and becomes a *tool*: the sequel
needs to author content far faster than we can write it in C#, and this is the
milestone that unlocks that. It is last because until the systems and their
numbers have settled, authored content would be built on sand.

**Exit criteria:** a player can build a map, save it, play 1v1 on it against the
AI, and keep it as part of a growing map set.

---

## 5. What can run in parallel

The critical path is M0 → M1 → M2 → M3 → M4. After M0 lands, three tracks can
proceed independently and only meet at M6:

- **Content track:** M1 → M4 (units), M5 (fog/vision).
- **World track:** M2 → M3.
- **Platform track:** M0.5 → M7 (replays/summary), which is also the iteration
  tooling.

M6 needs the content and world tracks. M8 can be interleaved anywhere from M1
onward, since the HUD work is mostly independent of simulation. M9 is last by
decision.

---

## 6. Cross-cutting engineering work

**Tunability.** §2 is a cross-cutting requirement, not a milestone feature:
every milestone below adds numbers, and every one of them has to land in data
with one home and no rebuild to change it. M0.7 sets the pattern; the rest
follow it.

**Determinism.** M0.5's command log is the spine: replays, save/load, regression
testing and replay-driven balance iteration all consume it. Budget for it
properly — rounding in the ballistic solver, physics timestep, and iteration
order over structures are all places where non-determinism creeps in, and it is
far cheaper to keep determinism than to restore it.

**Verification.** The original is available for reference (the Steam
re-release), so *feel* claims can be checked rather than argued — but the
original is a reference, not an oracle. Use it to answer "does a Bomb's blast
catch a neighbour?" and never to settle "should a Bomb do 3?":

- ~~a headless harness that loads a map + command log and asserts the resulting
  state hash~~ — **exists** (M0.5): `Scenes/harness.tscn` plays a match with
  nobody at the keyboard, records its log and prints a state hash, and replays a
  log with `--expect=<hash>`. It is the regression suite for every rule we
  implement, and it is already how a tuning change is shown to have reached the
  simulation. Run it with `--fixed-fps 60` (see `AGENTS.md`);
- **golden replays** for the awkward rules (cascade, Power Spike taper, EMP
  disable, anti-air reload, Balloon death, Shield pass-through, wrap-around
  range);
- side-by-side play against the original for anything qualitative — launch arcs,
  terrain heights, how a blast feels. The answers feed §7 and are expected to
  change again.

**Iteration.** The measure of this prototype is how fast a number can be
changed, played with and kept or reverted. When a milestone makes that slower,
the milestone is wrong. M0.7 is the standing answer: F1 edits any number the
match's resources export, Enter writes it back, and `--tune=` does the same for
a replayed match — so the loop is "change, replay, compare", and no milestone
needs to invent its own.

**AI.** One planner, with its weights in an `AiProfile` resource (M0.7), so
that personality is tuning rather than code when more opponents arrive.

**Documentation.** `AGENTS.md` is the entry point for this repo and currently
describes a game that will no longer exist. Update it in the same commit as the
code it describes, and keep [units.md](units.md) authoritative by citing it from
the code that implements each rule. Where this plan and the original disagree,
this plan wins — the original is the reference we started from.

---

## 7. The tuning surface (where every number lives)

This is the list to keep honest. **Every** gameplay number appears here exactly
once, with its home resource — that is what §2's "one home per number" means in
practice. Values marked *(manual)* are the original's published numbers used as
an opening bid; *(guess)* means nobody published one and this is our first
estimate. **All of them are expected to change**, repeatedly, and none of them
requires a code edit to change.

| number | home | starting value |
| --- | --- | --- |
| **Per unit:** hit points, damage, level/energy cost, vision radius, launch-force scale, build permission, blast radii | each unit's `UnitDefinition` | HP and damage: *(manual)* — Hub 5, Harvester 5, Anti-Air 3, Shield 3, Crawler 3, Repair 1, Balloon 1; Bomb/Missile 3, EMP 2, Cluster 1.5 × 3, Crawler 4. Costs 1/3/7 *(manual)*. Vision and launch scale: *(guess)* |
| **Match rules:** turn clock, max stored energy, main-Hub income, Harvester income (on/off pool), rounds-per-game if any, disable duration (EMP, anti-air reload), aim rotation speed, charge-meter speed | `MatchRules` | clock 60 s *(manual)*, cap 35 *(manual)*, Hub 7/round *(manual)*, Harvester 3 / 1 *(manual)*, disable 1 turn cycle *(manual)*; aim 3 rad/s, a full charge in one second *(guess)* |
| **Blast falloff curve** — the shape between full and no damage | `MatchRules` | two bands: full inside the unit's inner radius, half out to its outer radius *(guess)* |
| **Launch ballistics:** gravity, arc, muzzle height, launch force range per launcher (and the slope effect on the first, M3) | `MatchRules`, with each launcher's `UnitDefinition.LaunchForceScale` | gravity 9.8, force 4→20, upward bias 5, muzzle height 2 *(guess)*; the shipped Hub throws at the match's reach (scale 1.0) |
| **Cord rules:** reach per launcher, cord-node spacing, Power Spike taper and node limit | `MatchRules` | spacing and reach *(guess)*; 50-node limit *(manual)* |
| **World:** map bounds, terrain heights per state, height→range curve, pool sizes, the two start positions | the `MapDefinition` | bounds 475×475 today, pool side 6.0, heights *(guess)* |
| **Effects:** anti-air range, EMP radius, shield radius, homing turn rate, crawler speed | the relevant `UnitDefinition` (+ `MatchRules` for shared defaults) | relative sizes only *(manual)*: EMP > anti-air, EMP > shield; absolutes *(guess)* |
| **AI:** every weight the planner uses — how long it thinks, its aim spread, the power range it fires at, and later aggression, expansion, defence, target preference | `AiProfile` (`Resources/AI/ai_profile.tres`) | think 1 s, spread ±25°, power 0.3–0.9 *(guess)* |
| **Presentation:** nothing gameplay. Keep it out of this table | — | — |

**The rule for anything new.** A number added by a milestone lands in a resource
in the same commit that introduces it. A literal that survives a review is a bug
report waiting to happen, and the fastest way to find one is to try changing
balance from the dev overlay (M0.7) and see what refuses to move.

---

## 8. Risks

| risk | why it bites | mitigation |
| --- | --- | --- |
| **Treating the reference as the target.** The manual's numbers are the *original's* balance, and this is a different game. | We spend months reproducing someone else's balance instead of finding ours, or we treat "it's in the manual" as a reason a number cannot move. | §1 and §2: everything is provisional; §7 labels each value *(manual)* or *(guess)* and both are changeable. |
| **Iteration is slow, so it stops.** If a balance change needs a rebuild and a merge, nobody makes it. | The base fails at its main job, and the sequel inherits it. | M0.7, the four rules in §2, and the exit criterion that a damage value changes without touching code. |
| **Scoping the sequel here.** Designing our own differentiated mechanics before the base works. | The base never lands, and the sequel has no foundation to stand on. | §11 is short on purpose. The base is M0–M9; the sequel is after. |
| **Cut features creeping back.** Wind, water, multiplayer, challenge missions and six units each have a plausible "while we're here". | The first slice loses its edge and nothing finishes. | §1 lists every cut with its cost, and the checklist in §9 marks them as deliberate. Re-adding one is an open decision, not a detail to slip in. |
| **M0 refactor churn.** | Everything is edited at once and nothing works. | Keep `GameManager.Instance` and the old ammo path alive behind the new one; land M0 with the game still playable (that is its exit criterion). |
| **The spec documents more than we build.** [units.md](units.md) describes eighteen units, water and wind. | Someone implements a rule for a unit that does not exist. | §1 is the scope; read it before the spec. |
| **Wrapping maps touch everything.** | Discovered late, it invalidates distance/targeting/camera code. | M3, before the last of the units and the AI; do the world↔map maths once, centrally. |
| **Determinism retrofitted.** | Replays and replay-driven tuning both break subtly. | M0.5, before any consumer of it. |
| **Original assets.** | Copyright. | Behaviour and layout only; new art, placeholder audio — and placeholders chosen so replacing them does not force a re-tune. |
| **`GameManager` god object.** | Every feature edits one 1355-line file and conflicts. | M0.6. |

---

## 9. Parity checklist — a starting point, not a ship list

Track progress here. Each line is a claim that can be demonstrated. Lines struck
through are **deliberately out of the first slice** (§1) — do not "fix" them.
Nothing here is a promise about a finished product: it is the mechanic set the
base should have, so that there is something solid to iterate on.

**Rules**
- [ ] Energy 1/3/7, max 35, per-round refill, carry-over
- [ ] Round ends when both players spend out or skip
- [ ] Player order shifts left each round
- [ ] Skip forfeits the round's remaining turns
- [x] Hit points per unit type; damage subtracts; partial damage is normal
- [x] A weapon damages what it strikes **and** what is near it, on a tunable curve
- [ ] Cords cannot overlap; illegal launches refused
- [ ] Nothing but a launcher grants build reach — and only a Hub is a launcher
- [ ] Cord nodes take no damage except from a Power Spike
- [ ] Hub destruction cascades recursively through the network
- [ ] Units must land on flat ground
- [ ] Terrain height modifies launch range (downhill helps, uphill hurts)
- [ ] The map wraps in both axes
- [ ] Win = destroy the opponent's main Hub
- [ ] ~~Water landings, Bridges, Crawlers under water~~ — out of the first slice
- [ ] ~~Wind bends shots~~ — out of the first slice

**Units** — the twelve in [the starting roster](#the-twelve-units), each with its
stated exceptions
- [ ] Level 1: Anti-Air, Bomb, Cluster, Repair
- [ ] Level 3: Balloon, EMP, Missile, Spike
- [ ] Level 7: Crawler, Harvester, Hub, Shield
- [ ] ~~Bridge, Tower, Mines, Reclaimer, Offensive Unit, Virus~~ — out of the first slice

**Tuning**
- [ ] Every number in §7 lives in a resource, once
- [ ] No gameplay literal left in a `.cs` file
- [ ] A value can be changed live, in a running match, and written back
- [x] A balance change can be judged by replaying a recorded match
- [x] A match is reproducible from (map, seed, command log)

**Options**
- [ ] Clock (default 60 s)
- [ ] Fog (black unseen area; explosions still visible)
- [ ] ~~Wind~~ — out of the first slice
- [ ] ~~Teams~~ — out of the first slice

**Modes**
- [ ] 1v1 setup: colour, name, the single BOT slot, map, clock, fog
- [ ] Replay Theatre: record, save, play/pause, per-player view, near-transparent fog
- [ ] Game summary: Units, Energy, Shots, Hits, Destroyed, Accuracy, Bonus
- [ ] Map editor: size, terrain, pools, 1v1 start positions, save/load (M9, last)
- [ ] ~~Challenge mode: 16 missions~~ — deferred
- [ ] ~~Networked multiplayer~~ — deferred to the sequel's own design

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

## 10. Where to start

**M0 has landed, so M1 is next.** The foundations are in: units, their numbers,
the hit-point and blast model, the cord graph, the map, the command log and the
tuning surface are all data, and a match is reproducible from (map, seed, command
log). The next commit worth making is the energy/round loop, because that is the
difference between a duel and this genre.

M0.7 landed with two units rather than twelve on purpose: the tuning surface was
cheapest to put in place before the roster grew, and every unit added from here
inherits it — a new `.tres` whose numbers appear in the F1 overlay without anyone
adding them to a list.

The earliest point at which this is a *game* rather than a prototype is the end
of **M1**: energy, rounds, income, a build menu, and Hubs and Harvesters
expanding across the development map. That is worth reaching — and worth playing
with the numbers — before deciding how far down the rest of the list to go.

---

## 11. After the milestones: the sequel

Deliberately short. Once the base is playable, tunable and authored-content
capable, the sequel begins, and none of it is planned here:

- **Its own roster and balance.** The twelve in-scope units are a starting set
  and their numbers are first guesses; expect the roster to be reshaped and
  every value to move.
- **Its own content and modes**, built with the map editor rather than in code:
  more players, teams, missions, and whatever shape the game turns out to want.
- **Its own art and audio**, replacing every placeholder.
- **Its own differentiation** — the mechanics that make it a sequel rather than
  a clone. Wind, water, network play and the cut six units are all candidates,
  and all of them are design questions for that game, not implementation
  questions for this one.
- **Its own feel claims**, judged by playing it, with the original available as
  one reference among others.
