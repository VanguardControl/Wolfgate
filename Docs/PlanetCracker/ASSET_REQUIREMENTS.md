# Planet Cracking — asset and mapping requirements

Handout for spriters and mappers. The full design is `PLANET_CRACKER_DESIGN.md`; this file is self-contained.

## What the feature is, in five lines

A capital-class ship, the **planet cracker**, parks in orbit above a planet. Its crew ferries two huge **gravity anchors** to the surface, drags them into place and drills them in over five minutes while things crawl out of the fissures around the drills. Back on the ship, the crew spins up a giant **centrifuge**, and two **projectors** beam down to the anchors for ten to twenty minutes of shaking and rumbling. The disc of planet between the anchors is torn out and hangs in orbit in front of the ship as a **chunk**, leaving a **hole** on the surface. Crews mine the chunk with **crack miners**, then switch the anchors off and drop the chunk back into the hole, where it explodes.

## Art direction

Wolfgate follows Aphelion's direction: **cassette futurism with hyper-modern cyberpunk fitted on top**. For this feature that means:

- **Anchors, centrifuge, miners, crates:** old-era heavy industry. Painted metal, worn edges, mismatched repair plates, physical switches, recessed amber readouts, warning stripes with faded paint. Colours: charcoal, dusty olive, faded orange, warm ivory, with a washed-out stripe or two.
- **Projectors, beams, consoles, survey tools:** new-era. Sleek housings, glass, holographic screens, sharp neon accents (cyan or magenta) so the beam reads as the one impossibly modern thing bolted onto a rusty ship.
- **Where the eras meet:** show the brackets and adapters. A projector on a riveted hull plate with a modern cable run taped to it is exactly right.
- **Fissures, crack ring, hole:** natural, dark, with a faint inner glow (magma or the same neon as the beam, per planet) so they read at a glance against any biome.

References: cari.institute cassette futurism, ARC Raiders concept art, Cyberpunk 2077 neokitsch.

## Technical conventions

- Space Station 14 RSI format: a folder with `meta.json` and PNGs; 32 px per tile; multi-tile machines are one big state (96×96 for 3×3).
- Directional sprites need 4 directions (south, north, east, west) in one state.
- Animated states list frame delays in `meta.json`; 0.1–0.2 s per frame is typical. Loops should be seamless.
- Light-emitting parts go in a separate `-unshaded` state so they glow in the dark.
- Every machine needs a `broken` state (dented, dark, sparks optional) and most need `off`.
- Console screens are separate states layered on the shared computer body; only the screen art is new.
- The console diagrams themselves (site map, centrifuge dial, timeline) are drawn in code. Only small UI icons are needed from artists.

## Sprites

Priority **P1** is needed for a playable prototype. **P2** is polish and can land later. **RESOLVED** rows need no
art at all: they were settled with something the tree already ships, and `SPRITE_LIST.md`'s "Uses existing art"
table names exactly what each of them borrows. The `_WF/PlanetCracker/` placeholders for those are deleted.

| # | Pri | Asset | Size | States | Notes |
|---|---|---|---|---|---|
| 1 | P1 | Gravity anchor | 3×3 (96×96), non-directional | `deployed`, `drilling` (anim, 8–12 frames), `locked` (anim, slow pulse), `off`, `broken`; `damaged` overlay (sparks, anim); `-unshaded` glow for `drilling` and `locked` | A squat drilling rig with a central bit, hydraulic legs and one big physical lever. Should look far too heavy to carry. |
| 2 | RESOLVED | Anchor crate | — | — | Uses `Structures/Storage/Crates/engicrate_secure.rsi` at `scale: 2, 2`; the replacement variant is marked with that crate's own `locked` lock light instead of a stencil. |
| 3 | RESOLVED | Gravity projector | — | — | Uses the AK570 shuttle autocannon, `_Mono/Objects/ShuttleWeapons/artillery.rsi` (`space_artillery` + `fcs-unshaded`). Placed facing outward as before; the cut now swings it onto its own anchor and hands the placed facing back afterwards. |
| 4 | P1 | Centrifuge | 3×3 (96×96), non-directional | `off`, `spinning` (anim, one seamless loop of a rotor; playback speed is set in code), `broken`; `-unshaded` interior glow | A ring rotor inside a caged housing; visible mass. Old-era. |
| 5 | RESOLVED | Crack control console | — | — | Stock `Structures/Machines/computers.rsi` faces: `shuttle`, `telesci`, `telesci_red`, `explosive`. |
| 6 | RESOLVED | Sector survey console | — | — | Stock `computers.rsi` faces: `sensors` and `mining`. Also placed at the outpost. |
| 7 | P1 | Crack miner | 2×2 (64×64), non-directional | `idle`, `mining` (anim), `exhausted`, `broken`; `-unshaded` | Old-era. A drill head on a frame with a visible battery slot. |
| 8 | RESOLVED | Handheld surveyor | — | — | Uses `Objects/Specific/Research/anomalylocator.rsi` (`icon` plus both in-hands). No scanning state: nothing ever loaded one. |
| 9 | RESOLVED | Surveyor ground pulse | — | — | No art: `WFEffectSurveyPulse` carries `SingularityDistortion` and the client's singularity overlay lenses the screen around it. |
| 10 | P1 | Deep vein marker | 1×1 decal | `vein`, `vein-rich` | Grey base tinted in code per ore type. Reads as a shimmer under the ground. |
| 11 | RESOLVED | Fissure decals | — | — | Uses the stock window damage overlays `Structures/Windows/cracks.rsi` (`DamageOverlay_5`/`_10`/`_20`, stage 4 repeating `_20`, so stage 4 has no glow). The burst is the stock `EffectSparks`. |
| 12 | P1 | Crack ring decals | 1×1 decal set | `ring-straight`, `ring-curve`, each at 3 stages | Segments laid along the circle perimeter; stage 3 is wide and glowing. |
| 13 | P1 | Crack-hole tile | tile 32×32, edge variants | like the existing chasm tile: full, plus edges and corners | Dark pit with a faint glow. Plus a `rim` decal set (straight, curve) for the lip. |
| 14 | RESOLVED | Beam segment | — | — | Uses the ship laser's own tintable beam, `_Mono/Objects/Weapons/Guns/Projectiles/lasers.rsi` `grayscale_beam`. The overlay stretches one frame along the line rather than tiling it. |
| 15 | RESOLVED | Sky beam | — | — | The same frame stood up with `scale: 5, 1` then `rotation: 90`; no separate sheet. |
| 16 | P2 | Chunk burst | 3×3 effect | `burst` (anim, 8 frames, one-shot) | Plays at the hole and at the chunk on extraction. Dust and rock. |
| 17 | RESOLVED | Mob emerge overlay | — | — | Dropped: a fissure mob simply appears on its tile, with the stone-door sound as the only cue. |
| 18 | P2 | Liftoff/extraction dust | reuse existing `CEZLiftoffDust` | none | |
| 19 | P2 | Console UI icons | 16×16 | `anchor`, `projector`, `centrifuge`, `chunk`, `warning`, `beam` | Flat, single colour, for the diagram legend. |
| 20 | P2 | Shipyard preview | as the shipyard uses | one image | Only if the shipyard listing shows one. |

## Sounds

| Pri | Sound | Notes |
|---|---|---|
| P1 | Drill loop | Heavy industrial, loops for 5 minutes, needs to sit under gunfire. |
| P1 | Anchor lock thunk | One-shot; the moment a drill finishes. |
| P1 | Centrifuge spin loop | Loops; pitch is shifted in code with spin, so record it at mid speed with a clean tone. |
| P1 | Crack rumble loop | Deep, loops for the whole crack, with occasional cracks and groans. |
| P1 | Crack complete boom | One-shot, big. |
| P1 | Evacuation alarm | Loops for 60 s; distinct from existing station alarms. |
| P1 | Grace klaxon | Loops while the fall countdown runs; must be alarming. |
| P2 | Projector charge and fire | Two one-shots. |
| P2 | Anchor pairing chime | One-shot when the circle appears. |
| P2 | Fissure crack | Short one-shot, several variants. |
| P2 | Mob emerge | Short one-shot. |
| P2 | Chunk placement boom | One-shot when the chunk appears in orbit. |
| P2 | Console alert | Short blip. |

Existing explosion sounds cover the chunk impact.

## Maps

### Planet cracker vessel (P1)

Shipyard category **Large**, class Capital. The layout is the gameplay, so these rooms are required:

| Room | Requirements |
|---|---|
| Bridge | Shuttle console, sector survey console, radar. |
| Chunk berth | A rectangle of empty space beside the hull, at least 48 × 48 tiles, on whichever side suits the layout. This is where the extracted chunk hangs. Marked in the map with the berth marker entity (provided with the feature); the game reads its position and size. Nothing may be built inside it. |
| Crack control room | Anywhere with good access and a view of the berth. The crack control console, and both gravity projectors (2×2, facing out) mounted on the hull edge that faces the berth. Big windows onto the berth so the chunk and beams are visible. |
| Centrifuge hall | The 3×3 centrifuge with walking room around it, its own APC, heavy cabling to engineering. Should feel like the heart of the ship. |
| Engineering | Power plant sized for two projectors and the centrifuge at once (a substation and a generous SMES bank; the plant type is the mapper's choice). Standard atmos. |
| Cargo bay | Space for two 2×2 anchor crates, the surveyor in a locker, room to drag a crate to the airlock. |
| Docking port | The transport is pre-docked here; a second port for visitors. |
| Medbay, quarters, storage, kitchen | Small. Crew of 6–10. |
| Defences | Point-defence mounts only. No offensive weapons. |

Keep the hull compact for its category. Mass matters twice: vertical thrust is thrust divided by mass, and the centrifuge rating has to exceed ship plus chunk. Include the vessel prototype entry with `limit: 1`, `requireCrew: true`, price 3,000,000, and the `PlanetCracker` component.

### Transport shuttle (P1)

Category **Micro**. Docking port, cockpit with shuttle console, four seats, a mini gravity generator, and a cargo space that fits exactly one 2×2 anchor crate with room to drag it out through an airlock. Thrusters enough to descend and climb with the anchor aboard. Saved docked to the cracker map.

### Planet surfaces (P1 procedural, P2 hand-made)

The ground layer is generated by the existing planet biome pipeline (Monolith already has desert and ocean planet biomes with ore, fauna and weather under `Resources/Prototypes/_Mono/Planets`), so no surface map is required for a playable prototype. For sanctioned "showcase" planets, one or two hand-made surface grids in the pattern of `/Maps/_Mono/POI/surface_outpost_desert.yml` (a grid dropped onto the procedural surface, which continues beyond the built area). Suggested: one grassland with a ruined TSF survey post, one lava field with a mining outpost. The sector planets these attach to already exist (Fervidus, Merak, Asclepiu, Aerumna, Thrascias).

### Orbit and air layers

No files. Air layers reuse `/Maps/_CE/empty.yml` and `/Maps/_CE/clouds.yml`; the orbit layer is an empty map created in code.

### Outpost edit (P2)

Place one sector survey console at the outpost.

## Delivery checklist

- RSI folders under `Resources/Textures/_WF/PlanetCracker/` with `meta.json` filled in (size, states, directions, delays, license and copyright lines). Nothing is owed for a RESOLVED row above.
- Sounds under `Resources/Audio/_WF/PlanetCracker/` as OGG, with an `attributions.yml`.
- Maps under `Resources/Maps/_WF/Shuttles/` (vessel and transport) and `Resources/Maps/_WF/Planets/` (surfaces), plus the vessel prototype YAML.
- One screenshot of each machine in every state, and one of each ship interior, attached to the PR.
