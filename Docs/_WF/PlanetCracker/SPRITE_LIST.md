# Planet Cracking — sprite list (as built)

Every RSI the code and prototypes load, with the exact canvas each state needs. Paths are under
`Resources/Textures/_WF/PlanetCracker/`. Placeholders with these exact names and sizes are already checked in, so a
real sprite is a drop-in replacement of the PNGs; `meta.json` only changes where noted.

Conventions: 32 px per tile. "Canvas" is one frame of one direction. Animated states are horizontal strips
(frames left to right); directional states are vertical rows in the order south, north, east, west. Frame delays are
what the placeholder `meta.json` declares; change them freely, the code reads the RSI.

## Structures

| RSI | Footprint | State | Canvas | Dirs | Frames | Delay | Used for |
|---|---|---|---|---|---|---|---|
| `Structures/gravity_anchor.rsi` | 3×3 | `off` | 96×96 | 1 | 1 | | loose, not deployed |
| | | `deployed` | 96×96 | 1 | 1 | | deployed and paired |
| | | `drilling` | 96×96 | 1 | 8 | 0.10 | drilling loop |
| | | `locked` | 96×96 | 1 | 4 | 0.25 | drilled in, slow pulse |
| | | `broken` | 96×96 | 1 | 1 | | destroyed |
| | | `damaged` | 96×96 | 1 | 4 | 0.10 | sparks overlay while damaged |
| | | `drilling-unshaded` | 96×96 | 1 | 8 | 0.10 | glow layer for drilling |
| | | `locked-unshaded` | 96×96 | 1 | 4 | 0.25 | glow layer for locked |
| `Structures/centrifuge.rsi` | 3×3 | `off` | 96×96 | 1 | 1 | | stopped |
| | | `spinning` | 96×96 | 1 | 8 | 0.08 | one seamless rotor loop |
| | | `broken` | 96×96 | 1 | 1 | | destroyed |
| | | `glow-unshaded` | 96×96 | 1 | 8 | 0.08 | interior glow while spinning |
| `Structures/crack_miner.rsi` | 2×2 | `idle` | 64×64 | 1 | 1 | | idle, also doubles as off |
| | | `mining` | 64×64 | 1 | 6 | 0.10 | drilling loop |
| | | `exhausted` | 64×64 | 1 | 1 | | vein under it is spent |
| | | `broken` | 64×64 | 1 | 1 | | destroyed |
| | | `mining-unshaded` | 64×64 | 1 | 6 | 0.10 | glow while mining |

## Effects (entities, drawn unshaded)

| RSI | State | Canvas | Frames | Delay | Used for |
|---|---|---|---|---|---|
| `Effects/chunk_burst.rsi` | `burst` | 96×96 | 8 | 0.07 | one-shot at hole and chunk on extraction |

## Decals (ground)

Decals render frame 0 and ignore RSI directions; the code rotates each decal itself. Deliver these as **single
direction 32×32** and the `meta.json` `directions` will be set to 1 when the art lands. The placeholders carry four
directions only because they were generated that way.

| RSI | State | Canvas | Used for |
|---|---|---|---|
| `Decals/crack_rim.rsi` | `rim-straight` | 32×32 | lip of the hole, straight run |
| | `rim-curve` | 32×32 | lip of the hole, corner |
| `Decals/deep_vein.rsi` | `vein` | 32×32 | grey, tinted in code per ore |
| | `vein-rich` | 32×32 | rich vein variant |

## Interface

| RSI | States | Canvas | Used for |
|---|---|---|---|
| `Interface/icons.rsi` | `projector`, `beam`, `warning`, `chunk` | 16×16 | console diagram and legend |
| | `anchor`, `centrifuge` | 16×16 | declared, not drawn yet; keep for the legend |

## Uses existing art

Seven of the original asks were resolved with art (or a code effect) the tree already ships, so no artist time goes
into them and the generated placeholders are deleted. Nothing under `_WF/PlanetCracker/` carries these any more;
the prototypes and the beam overlay name the paths below directly, and `gen_placeholders.py` no longer makes them.

| Was | Now uses | States | Why |
|---|---|---|---|
| `Structures/anchor_crate.rsi` | `Structures/Storage/Crates/engicrate_secure.rsi` | `base` + `closed`/`open`, `locked` on the replacement | The stock engineering secure crate already reads as a heavy shipping container. 32×32 art on a 2×2 fixture, so the sprite carries `scale: 2, 2`; the lid is the mapped `WFCrateVisualLayers.Base` layer and the body sits under it. The replacement variant's stencil layer is the crate's own lock light, because no stencil art exists. |
| `Structures/gravity_projector.rsi` | `_Mono/Objects/ShuttleWeapons/artillery.rsi` | `space_artillery`, `fcs-unshaded` | The AK570 shuttle autocannon: a 64×64 single-direction mount drawn to be swung to any angle, which is exactly what the cut now does to it. One body state, so off/idle/charging/firing/broken separate on colour instead; `fcs-unshaded` is the emitter glow layer. |
| `Structures/crack_console.rsi` | `Structures/Machines/computers.rsi` | `shuttle`, `telesci`, `telesci_red`, `explosive` | The screen layer drops its own `sprite:` and inherits BaseComputer's sheet. Nav grid idle, targeting grid once paired, the same grid red while cutting, ordnance warning on the grace timer. |
| `Structures/survey_console.rsi` | `Structures/Machines/computers.rsi` | `sensors`, `mining` | Same inheritance. Sensor face at rest, mineral face while a density scan runs. |
| `Objects/surveyor.rsi` | `Objects/Specific/Research/anomalylocator.rsi` | `icon`, `inhand-left`, `inhand-right` | A handheld density scanner with both in-hands already drawn. The scanning state is dropped: nothing ever loaded it. |
| `Effects/crack_beam.rsi` | `_Mono/Objects/Weapons/Guns/Projectiles/lasers.rsi` | `grayscale_beam` | The ship laser's own beam, and the one state in that sheet authored colourless so a gun can tint it — which is what the overlay's per-pair modulate needs. It is a HORIZONTAL full-width line, so `WFCrackBeamOverlay` lays its rect along X and takes the thickness from the frame's height. |
| `Effects/sky_beam.rsi` | `_Mono/Objects/Weapons/Guns/Projectiles/lasers.rsi` | `grayscale_beam` | The same frame, stood up: the layer takes `scale: 5, 1` and then `rotation: 90`, which is the 1×5 pillar the old sheet drew by hand. |
| `Effects/survey_pulse.rsi` | no art at all | — | `WFEffectSurveyPulse` carries `SingularityDistortion` (intensity 400, falloff 2) and the client's own singularity overlay lenses the screen around it. |
| `Effects/mob_emerge.rsi` | no art at all | — | Dropped outright. A fissure mob simply appears on its tile; the stone-door sound is the cue. |
| `Decals/fissure.rsi` (`fissure-1..4`) | `Structures/Windows/cracks.rsi` | `DamageOverlay_5`, `DamageOverlay_10`, `DamageOverlay_20`, `DamageOverlay_20` | The stock window damage overlays are three escalating near-white spiderweb cracks, single-direction, which read as splits in the ground. The sheet has three stages and the design wants four, so stage 4 repeats the widest. |
| `Decals/fissure.rsi` (`burst`) | `EffectSparks` | `Effects/sparks.rsi` `sparks` | The stock half-second spark one-shot, already spawned this way all over the tree. `WFFissureSpawnerComponent.BurstEffect` names it. |

## Not needed

- `Decals/crack_ring.rsi` (`ring-straight-1..3`, `ring-curve-1..3`): nothing loads it. The surface scar uses
  `crack_rim.rsi`; the in-progress circle is drawn by an overlay in code. Skip unless you want stage rings later.
- `Tiles/crack_hole.rsi`: nothing loads it. The hole is open space (empty tiles), not a tile sprite.

## Totals

| Group | Distinct PNGs | Largest canvas |
|---|---|---|
| Structures | 17 | 96×96, with 8-frame strips at 768×96 |
| Effects | 1 | 96×96 strip |
| Decals | 3 | 32×32 |
| Interface | 6 | 16×16 |

Maps (cracker hull, transport) are separate and unchanged from `ASSET_REQUIREMENTS.md`.
