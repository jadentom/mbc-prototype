# MoonBase Commander — the original's unit roster and economy

Reference data for the parity plan in [README.md](README.md). Everything here is
transcribed from the **official manual** shipped with *MoonBase Commander*
(Infogrames / Humongous, 2002), which is the authoritative source for unit
stats — the in-game numbers are never shown to the player, so the manual is the
only written record. Source: the manual PDF distributed with the Steam
re-release, <http://cdn.akamai.steamstatic.com/steam/apps/254880/manuals/MoonBase%20Commander.pdf>
(pages 19–26, *Units*, *Destruction*, *Strategies*).

Where the manual is ambiguous, this file says so in a **Note** rather than
guessing. Read those before treating a number as final.

## The two numbers that define combat

- **Armor** is a unit's hit points, and it is **subtractive, not a health bar**.
  A Bomb (Damage 3) hitting a Hub (Armor 5) leaves the Hub at 2 Armor. There is
  no partial damage and no falloff — see the *Blast* note below.
- **Damage** is a flat integer per weapon. A weapon that does not exceed the
  target's remaining Armor does nothing at all.

> **Note — blast model.** The original resolves a weapon against the unit it
> actually struck; it has no area-of-effect damage falloff for ordinary bombs.
> Only EMP (disables everything in a radius) and Power Spike (travels a cord)
> are explicitly area effects. The prototype's two-band blast (full damage
> inside 1.5 units, half inside 3) is an invention of this repository, not a
> reproduction of the original.

## Energy and levels

- Every unit costs **1, 3 or 7 energy**, and that cost *is* its "level". The
  build menu is three pages — the `1`, `3` and `7` buttons.
- **Maximum stored energy is 35.**
- Energy is per-round, not per-turn: a round ends when every player has spent
  all their energy or has skipped, and then everyone's energy refills.
- Unspent energy carries into the next round. **Skip** forfeits the rest of your
  turns in the current round and transfers *all* current energy forward, which
  is an explicit strategic option and not merely a "pass".
- **Player order shifts left every round** — the player who went first goes
  last next round.
- Energy income sources:
  | source | energy per round |
  | --- | --- |
  | Main Hub | 7 |
  | Energy Collector on an energy pool | 3 |
  | Energy Collector on ordinary terrain | 1 |
  | Reclaimer (armor absorbed, granted at end of round) | up to 5 from your own units, 3 from an opponent's |
  | Bonus for reclaiming units and destroying opponents | manual does not quantify |

- A Collector disabled by EMP or Virus at the moment the round ends yields
  **nothing** that round.

## The eighteen units

Each faction has the same eighteen units with the same statistics and different
art. **Level** is the energy cost.

### Level 1 (1 energy)

| Unit | Armor | Damage | Function and rules |
| --- | --- | --- | --- |
| **Anti-Air Missile Launcher** | 3 | — | Automatically fires a defensive missile at any enemy projectile within its range. The missile kills the projectile **unless** (a) the projectile is destroyed before the missile arrives, or (b) the missile hits an enemy shield. After firing, the launcher is **inactive for one full turn cycle** while it reloads. |
| **Bomb** | — | 3 | Detonates on striking another unit or solid ground. |
| **Bridge** | 3 | — | The only structure that lands safely **in water**, turning that area into safe flat land. Every rule that applies to flat land applies on top of a Bridge. If a Bridge is destroyed, any unit or cord on it drops into the water and is destroyed. |
| **Cluster Bomb** | — | 1.5 × 3 | Splits into three smaller Bomblets at the apex of its flight arc. Each Bomblet behaves as a weaker Bomb. Its real value is drawing anti-air fire. |
| **Repair** | 1 | heals 1 | Restores **1 Armor point** to any unit it hits — including enemy units, since the manual does not restrict it to friendly targets. |
| **Tower** | 3 | — | Expands your field of view so you can see more of the map while Fog is active. |

> **Note — Tower.** The manual gives the Tower only a vision function. The
> *Strategies* section separately refers to penning Crawlers "behind a Tower
> cord", which implies the Tower also sits in and extends the cord network (and
> therefore build reach). Confirm against the shipped game or a frame-accurate
> source before implementing it as cord-extending rather than vision-only.

### Level 3 (3 energy)

| Unit | Armor | Damage | Function and rules |
| --- | --- | --- | --- |
| **Guided Missile** | — | 3 | At the apex of its arc its guidance engages and it flies toward the **closest enemy unit**. If it arrives without being shot down or hitting an enemy shield, it inflicts exactly the same damage as a Bomb. |
| **Spy Balloon** | 1 | — | Fragile: anything that touches it destroys it. Its whole point is providing vision **without needing a cord**, which matters under Fog. |
| **EMP** | — | 2 | Travels like a Bomb, and on hitting the ground or an enemy unit **disables every unit within its range for one full turn cycle**. Units struck directly may additionally be damaged. Its area of effect is **larger than an Anti-Air unit's or a Shield's**. |
| **Mines** | — | 3 | To arm, the Mine must land on the ground without colliding with an existing unit or Shield and without being intercepted by an Anti-Air missile. Once deployed it detects any unit moving nearby and detonates, damaging that unit. |
| **Reclaimer** | — | absorbs armor | Absorbs a unit's Armor and transfers it to the launcher's owner **as energy at the end of the round**. Up to **5** Armor from the reclaimer's own units, **3** from an opponent's. |
| **Power Spike** | — | 3 | Sends damage **down each direction of a cord**, tapering off at a constant rate, with **no effect after travelling 50 cord nodes**. |

### Level 7 (7 energy)

| Unit | Armor | Damage | Function and rules |
| --- | --- | --- | --- |
| **Crawler** | 3 | 4 (on detonation) | A freestanding bomb that **moves between rounds**, taking turns in the order of its "birth". It slips **underneath** enemy shields, and detonates only when it bumps into an enemy unit or cord — landing on solid ground does not set it off. It can be disabled by an EMP, and while disabled can be destroyed **without** triggering its charge. It can be reclaimed. It can be launched into, and travel under, water. |
| **Energy Collector** | 5 | — | Yields 3 energy/round on an energy pool, 1 off it (see the economy table). When destroyed it produces **a large and violent explosion** — a hazard to whatever you built beside it. |
| **Hub** | 5 | — | The only unit that may build **all** other units. Launch distance varies with the height of the terrain it stands on. Only the **main** Hub generates energy (7/round); secondary Hubs build but do not generate. |
| **Offensive Unit** (Launcher) | 3 | — | Besides the Hub, the only thing that can launch at all. It launches **weapons only** — Bomb, Cluster Bomb, Guided Missile, EMP, Power Spike, Crawler and Virus — and throws them **nearly twice as far as a Hub**. |
| **Shield Generator** | 3 | — | Generates a hollow shell of plasma that **destroys any enemy projectile attempting to penetrate it** while letting all friendly objects pass. Crawlers walk underneath it. |
| **Virus** | — | 1 (+1 per hop) | Disables the unit it hits. After a full cycle of turns it follows a cord attached to the affected unit, disables the unit at that cord's end, deals it **1 point of damage**, and returns it to normal operation. This continues through the cord network and unit structures until it has travelled through all units. |

## Cords

The cord network is the game's actual spatial system — it is what limits where
you may build, and it is what the destruction cascade propagates along.

- A cord is the link between a unit and its parent. **Cords cannot overlap**,
  and a launch is refused outright if the resulting cord would overlap an
  existing one.
- **Cord nodes** (the dots separating cord sections) are *not* units. They take
  no damage and can only be hit by a Power Spike.
- In most cases a cord **cannot be damaged at all** — a Bomb has no effect on
  one. A cord is severed when a Bridge it depends on is destroyed.
- When a cord is destroyed, **every unit depending on it is destroyed too**.
- A Power Spike damages the units connected to the cord it strikes.
- The blinking lights on cords always flow **back toward the main Hub**, so a
  player can read the chain of descent and trace an opponent's network back to
  the Hub that built it.

## Destruction

- **Hub destruction cascades.** Destroying a Hub destroys every connected unit
  built by that Hub — including other Hubs — recursively. Killing an opponent's
  *original* Hub therefore destroys their entire network. This is the win
  condition: the object of the game is to destroy the opponent's main Hub.
- Damage is shown by pressing **D** for the damage bar; the **colour of the
  smoke** rising from a damaged unit also encodes how badly hurt it is (darker
  smoke = more damage).

## World rules

- **New units must land on flat ground.** Hubs and other structures landing on a
  hillside or in water are destroyed on impact.
- **Only Bridges and Crawlers can land on water.** Build on top of Bridges to
  cross water.
- **Terrain has four states: Water, Ground, Plateau, Mountain.** Launching from
  a mountain top throws much farther, so high ground is a range advantage —
  and correspondingly an opponent firing **uphill** has a shorter range.
- **Maps wrap.** There are no map edges: leaving one edge brings you back on the
  other, so flanking is always possible in both directions.

## Game options that change the rules

| Option | Effect |
| --- | --- |
| **Clock** | Per-move time limit. Default **60 seconds**. |
| **Wind** | Adjustable intensity; direction is **randomised at the start of each game**. Wind bends shots, and because maps wrap every player is downwind of someone. |
| **Fog** | Unseen terrain is **black** — no units or terrain visible beneath it. Vision expands as you spread across the planet, via Hubs, Towers and Spy Balloons. **Explosions and smoke remain visible through fog.** |
| **Teams** | Teammates share line of sight and their units cooperate — an ally's Anti-Air will not shoot down your projectiles, and their units are effectively yours. Teammates do **not** share energy and cannot control each other's units. A team stays active while any member functions. |
| **BOTs** | Ten computer opponents, "which all play differently"; up to three per game. Names hint at their play style. |

## Sources and confidence

- Unit stats, energy economy, cord rules, destruction cascade, terrain states,
  wrapping, wind, fog and the objective: **official manual, pages 19–26** — high
  confidence, transcribed directly.
- Tower's cord function and Repair's friendly/enemy targeting: **inferred or
  unstated** — flagged above, verify before implementing.
- Numbers the manual never gives (launch ranges, cord lengths, pool sizes,
  weapon flight arcs, bot personalities, terrain heights): **unknown**. These
  have to be tuned to feel, or recovered from the shipped data files.
