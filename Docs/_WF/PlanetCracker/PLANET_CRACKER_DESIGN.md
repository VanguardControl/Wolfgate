# Planet Cracking — design, revision 3

**Status:** decisions D1–D25 taken (§7). F0, F1+F3, F4, F5, F2, F7 and F6 are built — see **§10 As built** for what shipped and where it differs from §4. F8 and F9 are in progress. Branch `clanker/planet-cracker-design-a8c500`.

Revision 3 resolves the proofread issues (chunk berth, cut radius, mining numbers, no abort after begin, chunk watchdog, grace hysteresis) and corrects F0: the sector already has planets (Far Horizons star system) and a planet-surface prototype pipeline (DeltaV, used by Monolith's desert world), so F0 is the glue between them and the z-level stack, not planets from scratch.

§0 is what the codebase already gives us (verified, paths inline). §1 glossary, §2 the corrected player loop, §3 the state machine, §4 the features F0–F9, §5 numbers, §6 pointer to the asset requirements, §7 decisions taken, §8 remaining questions, §9 testing, §10 as built.

---

## 0. What already exists

| Capability | Where | Notes |
|---|---|---|
| **Planets in the sector** | `Content.Server/_FarHorizons/StarSystem/StarSystemMapSystem.cs`, `Resources/Prototypes/_FarHorizons/Space/{systems,planets}.yml`, `Entities/Objects/StarSystem/star_system.yml` | The `StarSystemKyphrus` rule (in every Monolith preset) spawns a `PlanetEntity` per planet on the sector map at a distance and angle from the star: Fervidus, Merak, Asclepiu, Aerumna, Thrascias. Each is a warp point, an FTL beacon and an IFF blip, rendered by a client shader overlay (`PlanetOverlay.cs`). **That is the planet you see in space.** They have no surface and nothing happens when you fly into one. |
| **Planet surface prototype** | `Content.Shared/_DV/Planet/PlanetPrototype.cs`, `Content.Server/_DV/Planet/PlanetSystem.cs`, `_DV/Station/.../StationPlanetSpawner*` | `- type: planet` = biome template + atmosphere + map light + marker layers + extra components. `PlanetSystem.SpawnPlanet` builds a flat surface map from it; `LoadPlanet` also drops a hand-made grid on top and reserves its tiles so rocks do not spawn inside. Monolith's `DesertWorld` (`Resources/Prototypes/_Mono/Planets/permanent_planet.yml`, biome `MonoDesertPermanentPlanet`, surface outpost `/Maps/_Mono/POI/surface_outpost_desert.yml`) uses it and is reachable by FTL. It is **commented out** on the live Caelestinus map and only on in the dev map. Monolith also ships ocean and desert biomes, planet ore tables, fauna and weather under `_Mono/Planets`. |
| Planets as stacked z-levels | `Content.Server/_CE/ZLevels`, `Resources/Prototypes/_CE/ZLevels/zmaps.yml` | See F0. Only the round-start station builds a network today. |
| Grids fall without a gravity generator | `CEZLevelsSystem.Gravity.cs` | Every 0.5 s a grid on an air layer with no active gravgen (or whose `FixturesMass` exceeds pooled `maxHandledMass`) enters a transit map and plummets at up to 1.2 levels/s. |
| Grids crash into the ground | `CEZLevelsSystem.Gravity.cs` `CrashGrid` | Touchdown at ≥ 0.35 levels/s explodes every hull tile plus a central blast scaled by tile count. **This is the fall and the chunk explosion for free.** |
| Piloted ascend/descend, spool, landing, dust | `CEZLevelsSystem.PilotControl.cs`, `ShuttleButtons.AscendZ/DescendZ` | Vertical accel = thrust/mass, so a heavy hull is slow vertically without extra work. Landed ships get thrusters disabled. Vertical flight needs a working gravgen. |
| Gravity generator capacity and spin-up | `Content.Server/Gravity/GravityGeneratorComponent.cs` (`maxHandledMass`), `Power/Components/PowerChargeComponent.cs` (`Charge` 0–1, `ChargeRate`, `Intact`, `SwitchedOn`) | Standard gravgen carries 250 mass, mini 50 (`gravity_generator.yml`). Charge ramps up and down with power. **The centrifuge is a gravgen variant.** |
| Crushing whatever is under a landing grid | `ShuttleSystem.FasterThanLight.cs` `Smimsh` | |
| Immobilising a grid | `Content.Server/_NF/Shuttles/Components/ForceAnchorComponent.cs` | Static body, thrusters off, FTL refused. **This is the "becomes a station" primitive.** Reacts to map-init and FTL completion only; runtime use needs a helper. |
| Ore veins and mining | `Content.Shared/Mining/Components/OreVeinComponent.cs`, `Content.Server/Mining/MiningSystem.cs`, `_NF/Gatherable`, `_Mono/.../laser_drill.yml` | Ore drops when a vein entity is destroyed or gathered. Monolith already has a stationary laser drill machine (`ItemMiner`) worth looking at for the crack miner. |
| Planet surface procgen, ore, hostile factions | `Content.Server/Salvage/SpawnSalvageMissionJob.cs` + `BiomeSystem`, `Procedural/salvage_factions.yml`, `biome_markers.yml` | Expedition planets already spawn biomes with ore and mob factions. Same biome machinery as the planet prototype above. |
| Sector-wide announcements | `ChatSystem.DispatchGlobalAnnouncement` | |
| Shipyard vessel definitions | `Content.Shared/_NF/Shipyard/Prototypes/VesselPrototype.cs` | `price`, `category` (Micro–Large), `class` (includes `Capital`), `limit`, `requireCrew`, `addComponents`. Current most expensive hull: 1,540,950. |
| Beams between two entities | `Content.Server/Beam/BeamSystem.cs` | Same map only; not used (see F5). |
| Grid-wide camera shake | `Content.Shared/Gravity/GravityShakeComponent.cs`, `SharedGravitySystem.Shake.cs` | |
| Custom-drawn console controls | `Content.Client/_WF/Shuttles/UI/ShipViewControl.cs` | Precedent for diagram controls drawn in code with the Wolfgate theme. |
| Looking up and down through layers | `CEZLevelViewerComponent`, `CEZLevelBlurOverlay`, `CEToggleZLevelLookUpAction` | Players see the layer below rendered under them, and can toggle to look up. Beams and the crack animation use this. |
| TSF | Locale only (TSF Comms, TSF Contractor, TSF Diplomat) | A faction name; no automated response. |
| Necromorphs | nothing | No mobs, no event, no marker. |

---

## 1. Glossary

- **Cracker** — the planet cracker vessel.
- **Transport** — the micro shuttle docked to the cracker; carries one anchor per trip.
- **Sector planet** — the Far Horizons planet entity you see and FTL to in the sector map.
- **Planet network** — the stack of maps that is one planet's orbit, air and surface (F0).
- **Orbit layer** — the top map of a planet network; space atmosphere; where ships arrive and where the cracker parks.
- **Anchor** — one of two heavy deployable drilling machines. The pair defines the circle.
- **Circle** — the crack region; the anchors are the two ends of its diameter.
- **Berth** — the zone on the cracker, marked by the mapper, where the chunk hangs. Can be on any side of the hull.
- **Projector** — one of two ship machines that beam down to the anchors. Both must be powered.
- **Centrifuge** — the cracker's oversized gravity generator. Its spin (charge) holds ship and chunk in orbit during the crack.
- **Chunk** — the extracted disc of surface, held in the berth on the orbit layer, directly above the hole.
- **Crack miner** — a machine placed on the chunk over a deep vein.
- **Fissure** — cracks that spread around a drilling anchor; hostile mobs crawl out of them.
- **Fall** — the cracker and chunk losing orbit and crashing.

---

## 2. Player loop (corrected)

1. **Choose a planet** on the cracker's sector survey console: name, sanctioned or not, rough vein rating, already cracked or not.
2. **Fly** there. FTL to the planet's beacon in the sector as today, then take the *Enter orbit* jump that appears once you are close. You arrive on the planet's orbit layer.
3. **Survey** — take the transport down, walk the surface with the handheld surveyor. It reveals deep veins that only a crack can reach.
4. **Deploy anchors** — two transport trips, one anchor each. Drag each anchor to position and wrench it down. Once both are within the distance band the circle preview is drawn on the ground. Activate each; it drills unattended for 5 minutes. Fissures spread around each drill and mobs crawl out (F8); the anchors need defending.
5. **Return** to the cracker. Pilot so the berth ghost on the console sits over the circle; within 8 tiles is good enough. On the crack console, target the pair.
6. **Spin up** the centrifuge to full and press *begin crack*. The gravity lock engages: the ship shifts the last few tiles so the berth is exactly above the circle, then force-anchors. The **fall stage** begins: from here until release, a projector or the centrifuge dropping out starts a 5 minute countdown to the fall. **There is no abort.**
7. **Crack** — beams from the projectors to the anchors, shaking, rumble, a crack ring growing around the circle on the surface. Duration depends on circle size and projector parts.
8. **Extract** — the circle, anchors included, is cut out of the ground layer into a new grid in the berth, directly above the hole it left. The planet is flagged cracked.
9. **Mine** — wrench crack miners onto the chunk over revealed veins. High yield. EVA required on the chunk.
10. **Disconnect** — on the chunk, switch both anchors off within 60 s of each other. 60 s evacuation alarm, then the chunk drops straight down through the air layers into the hole and its crash explosion goes off.
11. The cracker is released and can leave orbit.

Other ships can take the same *Enter orbit* jump at any point, see the cracker and chunk on radar, dock, board, or shoot. That is intended.

---

## 3. State machine

State lives on `PlanetCrackerComponent` on the cracker grid, mirrored to the crack console.

| State | Entered by | Exits to | Rules in force |
|---|---|---|---|
| `Idle` | purchase, or `Released` | `Surveying` | none |
| `Surveying` | cracker parked on an orbit layer | `AnchorsPlaced`, `Idle` (ship leaves) | surveyor works; anchors deployable |
| `AnchorsPlaced` | both anchors wrenched down within the band | `AnchorsLocked`, `Surveying` (anchor unwrenched or destroyed) | circle preview on the surface; drills can start; fissures and mobs while drilling |
| `AnchorsLocked` | both drills finished | `Cracking`, `Surveying` (anchor destroyed) | console can target the pair once the berth is aligned; ship may leave and come back |
| `Cracking` (fall stage) | *begin crack* with centrifuge at full and berth aligned | `Cracked`, `AnchorsPlaced` (anchor broken), `Surveying` (anchor destroyed), `Falling` | gravity lock snap, then ForceAnchor on the cracker; grace timer watches projectors and centrifuge; an anchor below half health **pauses** the crack timer until repaired; **no voluntary exit** |
| `Cracked` | crack timer done | `Disconnecting`, `Falling` | chunk exists in the berth, both grids force-anchored; miners run; planet flagged cracked |
| `Disconnecting` | both anchors switched off within 60 s | `Released` | 60 s evacuation timer; anchors cannot be re-armed |
| `Released` | chunk dropped | `Idle` | ForceAnchor removed from the cracker |
| `Falling` | grace expired in `Cracking`/`Cracked` | terminal | ForceAnchor removed, centrifuge capacity zeroed, both grids pushed into downward transit; existing gravity code crashes them |

**Grace timer (D25):** the centrifuge counts as "at full" once charge reaches 0.98 and stops counting as full when it drops below 0.95, so power ripple does not flap the timer. Any tick where it is not at full, or either projector is unpowered or broken, starts or continues the 5 minute countdown. Restoring all three resets it. The console shows the countdown and which condition is failing.

**Anchor damage (D9):** anchors are damageable machines with the usual three stages. Below 50% health during `Cracking` the beam sputters and the crack timer pauses; repair with a welder to resume. **Broken** (the destructible threshold; the machine is still there and repairable): the crack aborts to `AnchorsPlaced`, the pair must re-lock. **Destroyed** (the entity is gone): abort to `Surveying` with one anchor left. In both abort cases the projectors spin down for 30 s, then the cracker's ForceAnchor is released. The fall is reserved for the ship's own systems failing, because anchor loss is usually the work of hostiles on the surface while the crew is in orbit and cannot defend them, and a crash for that would be a punishment with no counterplay. Replacement anchors are a cargo purchase at a steep price.

**Chunk watchdog (D24):** the chunk carries `PlanetChunkComponent { Cracker }`. If its cracker is deleted, or is no longer on the same orbit layer, the chunk drops as if disconnected, with the evacuation alarm. Nothing hangs in orbit without a cracker under it.

---

## 4. Features

Build order: F0 → F1 → F3 → F4 → F5 → F2 → F6 → F7 → F8 → F9.

### F0 — Giving sector planets an orbit and a surface (prerequisite, separate feature)

**What exists.** Two halves, unconnected:

1. **Sector planets** are Far Horizons `PlanetEntity`s spawned by `StarSystemMapSystem.SetSystem` from `SystemKyphrus` (`Resources/Prototypes/_FarHorizons/Space/systems.yml`): Fervidus, Merak, Asclepiu, Aerumna, Thrascias, each at a distance and angle from the star. They are warp points, FTL beacons and IFF blips, drawn by the client's `PlanetOverlay` shader. Players already FTL to them; there is nothing there but space.
2. **Planet surfaces** come from the DeltaV `- type: planet` prototype and `PlanetSystem.SpawnPlanet` / `LoadPlanet`: a biome template, atmosphere, light, marker layers, optional hand-made grid with reserved tiles. Monolith's `DesertWorld` is a working example (biome `MonoDesertPermanentPlanet`, outpost grid `/Maps/_Mono/POI/surface_outpost_desert.yml`), currently disabled on Caelestinus. The surface is generated chunk by chunk around players (`BiomeSystem.ChunkLoader`), so it is unbounded and free until someone stands on it. Expedition planets use the same biome machinery with ore and faction marker layers.

**What a planet network is.** In the CE z-level code a stack of ordinary maps owned by one `CEZMapNetworkComponent` entity, each at an integer depth, sharing world XY, with a component set applied to every map. Grids move between layers by changing map while keeping XY, through a temporary transit map. Players see the layer below rendered under their own and can look up. Only the station builds one today.

```
depth 4   ORBIT LAYER   (new)  space atmosphere, arrival point, grids do not fall  <- cracker parks, chunk in the berth
depth 3   cloud layer          /Maps/_CE/clouds.yml
depth 2   air layer            empty map
depth 1   air layer            empty map
depth 0   GROUND LAYER         DeltaV planet prototype surface (CEZGroundLayerComponent)  <- anchors, fissures, hole
```

As built there is no cloud layer on a crackable world — three air layers in its place, so the stack is still five deep with orbit at depth 4. See §10 F4.

**What F0 builds.**

- `PlanetSurfaceComponent` on a sector `PlanetEntity`, set from a new field on the planet type (`surface: DesertWorld` style, pointing at a `planet` prototype plus optional hand-made grid). On first approach (or at round start for sanctioned planets) it builds the network: `PlanetSystem.SpawnPlanet` for the ground layer, empty maps for the air layers, a new orbit map with space atmosphere on top, all added with `TryAddMapsIntoNetwork`. F2's deep vein spawner is one more marker layer.
- **Enter orbit / leave orbit.** The orbit layer gets an `FTLDestination`. A ship within range of the sector planet sees *Enter orbit: Asclepiu* on its shuttle console; a ship on the orbit layer sees *Leave orbit*, which puts it back next to the sector planet. No new physics, and pirates intercept in the sector or in orbit as they like.
- **The orbit mechanic.** Today a grid on any non-ground layer falls unless a gravgen holds it. The orbit layer gets `CEZOrbitLayerComponent` and `UpdateGridGravity` skips grids parked there the same way it skips the ground layer. Sitting in orbit costs nothing. Descending is the existing `DescendZ` pilot action, which already requires a working gravgen; ascending back ends in `TryExitTransit` onto the orbit layer. The cracker's fall is therefore explicit: `Falling` pushes the grids into downward transit with `TryEnterTransit` and zeroes the centrifuge's capacity. During `Cracking` and `Cracked` the centrifuge does no physical work; it is the fiction that justifies the grace timer.
- A round-scoped planet registry (name, sector entity, network entity, flags) that F2's survey console reads. Planets that never get a surface stay as they are.

**As built.** §10 F0, with the details in `F0_IMPLEMENTATION_PLAN.md`. Tests: `Content.IntegrationTests/Tests/_WF/PlanetCracker/PlanetNetworkTest.cs`.

### F1 — The cracker vessel

**Contents:** crack control room with the console and two projectors on a hull edge; centrifuge hall; power plant sized for the crack; docking port with the transport already docked; cargo bay with two crated anchors and the surveyor; sector survey console on the bridge; point-defence mounts only.

**Berth (D20).** The mapper places a `ChunkBerthComponent` marker on the ship: a rectangle in grid space, on any side of the hull, big enough for the largest circle (44 tiles across, see §5) with clearance from the hull. The projectors sit on the hull edge facing the berth. Where the berth is decides where the chunk hangs, so the control room can be wherever access is best; it only needs a view of the berth.

**Shipyard entry:** `category: Large`, `class: [Capital]`, `limit: 1`, `requireCrew: true`, price 3,000,000, `addComponents: PlanetCracker`. Thrust-to-mass deliberately low so it is slow in every axis.

**Transport:** micro shuttle, one anchor per trip. Its mini gravgen is rated to carry the shuttle plus exactly one anchor, so two anchors aboard exceed capacity and the shuttle drops (`GridHasActiveGravgen`). The transport's console warns before take-off when overloaded (D16).

### F2 — Survey: sector console and surface surveyor

**Sector survey console (cracker bridge, and one at the outpost).** Lists every planet in the round's registry: name, sector position and distance, sanctioned or unsanctioned, cracked or not, and a vein rating (poor / fair / rich / very rich, derived from the planet's vein table without revealing exact contents). Selecting a planet sets it as the FTL target on the shuttle console. Reuses the registry from F0 and the existing shuttle console targeting.

**Surface surveyor (handheld).** Used on the surface it pulses and reveals deep vein markers within its radius on the client overlay, with ore type and estimated yield. Deep veins are hidden entities from a marker layer added when the network is built, seeded from the planet, and only mineable once they are on a chunk.

**Rules:** `DeepVeinComponent { Ore, TotalYield, Rate }`, a new prototype family. `CrackablePlanetComponent { Sanctioned, Cracked, VeinTable }` lives on the planet network entity. Unsanctioned planets roll richer tables. `Cracked` is set at extraction (F5); the crack console refuses a cracked planet.

**New:** survey console BUI, surveyor item and overlay, deep vein prototypes and marker layer, planet flags.

### F3 — Gravity anchors

**Flow:** uncrate on the surface, drag (standard pulling) to position, wrench down. When both anchors are wrenched down within the distance band they pair and the circle preview appears on the ground. Activate each: 5 minute unattended drill with progress on examine and an animated sprite. Both locked → the pair is targetable.

**Rules:** distance band 16–40 tiles between anchor centres; the cut circle's radius is half that distance plus 2 tiles so both 3×3 anchors are inside the cut and ride up with the chunk (D21). No alignment rule. Circle size drives crack time and vein count (§5). Unwrenching a locked anchor is refused. Anchors are owned by the cracker that bought them so two crackers cannot share a pair. Anchors have health and can be repaired; see D9 in §3.

**New:** `GravityAnchorComponent` + system (pairing, drill timer, lock, health thresholds), client circle overlay, sprites and sounds.

### F4 — Crack control: console, projectors, centrifuge

**Berth alignment (D20).** The console's site diagram and the shuttle console radar both show the berth as a ghost rectangle projected onto the surface below, next to the circle. The readout is the offset between berth centre and circle centre with arrows. The pair can be targeted once the offset is within 8 tiles. On *begin crack* the gravity lock engages: the ship is translated by that offset so the berth centre sits exactly over the circle centre (shake, sound), then force-anchored. Pilots only have to get close.

**Console UI.** Every panel is a diagram drawn in code (custom `Control`s with `DrawingHandleScreen`, Wolfgate theme, the same approach as the ship view control), not text tables. Layout:

```
+----------------------------------------------------------------------------------+
| CRACK CONTROL              state: CRACKING          crack remaining 07:42         |
+------------------------------------------+---------------------------------------+
|  SITE (top-down)                         |  CENTRIFUGE                            |
|                                          |         .-'''-.                        |
|      hull ===========[P]=====[P]=====    |       /  \  |  /  \     spin 100%      |
|            +-------- berth --------+     |      |   --( o )--  |   load 1,830 /   |
|            |        .------.       |     |       \  /  |  \  /      3,000 mass    |
|            |      (A)      (A)     |     |         '-...-'                        |
|            |        '------'       |     |  rotor animates at charge speed;       |
|            +-----------------------+     |  slows when losing power               |
|                                          +---------------------------------------+
|  anchors: LOCKED / LOCKED                |  PROJECTORS                            |
|  berth offset 0 tiles (locked)           |   [P1] power ok  integrity 100%  beam  |
|                                          |   [P2] power ok  integrity  92%  beam  |
+------------------------------------------+---------------------------------------+
|  TIMELINE  Survey > Anchors > Locked > [CRACKING] > Cracked > Disconnect > Released|
|  GRACE  --:--  (all systems nominal)                          [ BEGIN CRACK ]      |
+----------------------------------------------------------------------------------+
```

- **Site diagram:** hull edge, berth rectangle, the two anchors as dots with the circle between them, projector positions with beam lines when firing, berth offset readout with arrows. Colours follow state (grey unpaired, amber drilling, green locked, red damaged).
- **Centrifuge dial:** a rotor whose rotation speed follows `Charge`, a spin percentage, and a load bar of grid mass against capacity. Visibly slows when spinning down.
- **Projectors:** two icons with power and integrity bars, beam indicator.
- **Timeline strip:** the state machine as a row with the current state lit and timers under it.
- **Grace countdown** in red when running, naming the failing system.
- Buttons: *target pair* / *untarget* before begin, *begin crack* (enabled only when every precondition passes; hover lists the failing ones). **No abort after begin (D23).** The only exits from `Cracking` are completion, anchor loss, or the fall.

**Centrifuge:** gravity generator prototype variant with `maxHandledMass` above cracker + chunk mass, slow `ChargeRate` (full spin from cold in about 4 minutes), heavy `ActivePowerUse`. Its own machine UI is the same rotor dial.

**Projectors:** two powered machines with machine-part slots. Part tier multiplies crack time (§5). Both must be powered and intact during `Cracking` and `Cracked`.

**Fall stage:** see §3 and F0. Nothing new falls; the feature only decides when to stop holding the ship up. `ForceAnchorSystem` only reacts to `MapInitEvent` and FTL completion, so adding the component at runtime needs a small helper that also calls `ShuttleSystem.Disable` and adds `PreventGridAnchorChangesComponent`, and a matching release helper.

**New:** `PlanetCrackerComponent`, `ChunkBerthComponent`, console BUI and diagram controls, berth ghost on the radar, centrifuge and projector prototypes and systems, grace timer, admin verbs (set state, complete drill, complete crack, force disconnect, mark planet cracked) in the Wolfgate admin tab.

### F5 — The crack: extraction, chunk, hole, effects

**Extraction:** on timer completion, copy every tile inside the cut circle from the ground layer into a new grid; move every entity inside it (walls, deep veins, anchors, mobs, players) onto it; stamp the hole (tiles inside the circle become the crack-hole tile, the rim gets a decal ring). The chunk grid gets `PlanetChunkComponent` and `ForceAnchorComponent` and is placed in the berth on the orbit layer. Because the gravity lock aligned the berth over the circle, the chunk is directly above the hole and the beams run straight down. `Smimsh` on placement for safety. The tether is logical: both grids are static, so neither can move. The chunk has no atmosphere (D3).

**Biome gotchas:** the ground layer is generated lazily, so extraction must call `BiomeSystem.Preload` on the circle's bounds before reading tiles, or unloaded chunks come back empty. The hole must be written through the biome's `ModifiedTiles` path (any tile set through the map API is recorded there), otherwise the biome regenerates the surface over the hole the next time the area unloads and reloads.

**Beams (D4).** Projectors and anchors are on different maps, so `BeamSystem` cannot link them. Each projector carries a networked `CrackBeamComponent { Target: NetEntity }` and the client draws the beam in an overlay: from the projector's screen position to the anchor's position projected onto the layer below, using the same transform the z-level renderer already uses to draw the lower layer under the player. On the surface, looking up, the same overlay draws the beam from the anchor to the projector's projected position above; without look-up, a tall sky-beam sprite stands at each anchor. Animated beam texture, additive shader.

**Effects during `Cracking`:** repeating grid shake on the cracker and near the anchors, looping rumble, crack-ring decals spreading around the circle perimeter on the surface in stages, and the same ring visible from orbit through the z-view.

**Extraction moment:** heavy shake, one-shot boom, the chunk is teleported into the berth (D5) with a burst effect at the hole and at the chunk.

**New:** extraction system (tile and entity copy is the hard part), `PlanetChunkComponent`, crack-hole tile, decals, beam overlay, sounds.

### F6 — Chunk mining

**Flow:** crack miners are machines wrenched onto the chunk over a deep vein marker. Each converts the vein's total yield into ore stacks at the vein's rate while powered. The chunk has no power grid, so miners run on an internal swappable power cell (D15). Move the miner to the next vein when one is exhausted. Monolith's laser drill (`ItemMiner`) is the closest existing machine and a good starting point.

**Rules:** miners refuse to run anywhere except on a chunk over a deep vein. Output is ore, so the existing ore economy prices it.

**New:** `CrackMinerComponent` + system, prototype, sprites.

### F7 — Disconnect protocol and chunk fall

**Flow:** on the chunk, switch both anchors off within 60 s of each other (the first switch-off starts a visible countdown; if the second is late, the first re-arms). Success starts a 60 s evacuation alarm on chunk and cracker. On expiry the chunk loses its ForceAnchor and capacity and is pushed into downward transit (the orbit layer does not drop grids on its own). It falls straight through the air layers into the hole below the berth and `CrashGrid` explodes it. The chunk grid is deleted after the crash; the crater decals stay. The cracker's ForceAnchor is removed as it enters `Released`.

**Rules:** anchors cannot be switched off before `Cracked`. There is no tether entity to cut. Anyone still on the chunk goes down with it. The chunk watchdog (§3, D24) uses the same drop path when the cracker is gone.

**Fall of both grids (`Falling`):** cracker and chunk enter the same transit gap at the same moment with the same gravity, so they fall in lockstep and never swap order; the transit collision check only fires on an order swap with overlapping bounds, and the berth keeps their bounds apart anyway.

**New:** anchor off-switch logic and timers, alarms, watchdog, post-crash cleanup.

### F8 — Site threats: fissures

**Flow:** when an anchor starts drilling, fissure decals begin spreading outward from it in rings, one ring per minute of the 5 minute drill. Each new ring picks a few fissure tiles and spawns mobs from them with a crawl-out effect (burst decal, a short emerge animation on the mob, a crack sound). Mobs come from the planet's faction table and target the anchor first, then players. Unsanctioned planets use a nastier table and more spawns. At extraction a final surge crawls out of the crack ring on the perimeter before the circle lifts.

**Rules:** fissure decals persist for the round (D19) and are cosmetic. Spawn counts are capped per anchor. Mobs keep spawning only while the drill runs, so a locked anchor is quiet until the crack.

**Reuses:** `salvage_factions.yml`, the NPC spawners expeditions use.

**New:** `FissureSpawnerComponent` hooked to drill start/progress and extraction, decals, emerge effect, sounds.

### F9 — Flavour and escalation (later)

- **Sanctioning:** `CrackablePlanetComponent.Sanctioned` per planet. Beginning a crack on an unsanctioned planet raises a sector-wide announcement naming the planet and the ship. TSF players respond freely; no automated response.
- **Unsanctioned yield:** richer vein tables and a chance of a marker vein.
- **Necromorph marker:** nothing exists in the codebase. Separate feature. Extraction raises `PlanetCrackedEvent { Sanctioned, VeinTable }` on the chunk as the hook.

---

## 5. Initial numbers (balance later)

| Thing | Value |
|---|---|
| Cracker price | 3,000,000 |
| Anchor distance band (`d`, centre to centre) | 16–40 tiles |
| Cut circle radius | `d` / 2 + 2 tiles (20–44 tiles across) |
| Berth size | at least 48 × 48 tiles of clear space |
| Berth alignment tolerance | 8 tiles between berth centre and circle centre |
| Anchor drill time | 5 min each, in parallel |
| Centrifuge full spin from cold | 4 min |
| Centrifuge "at full" hysteresis | counts as full at ≥ 0.98 charge, stops at < 0.95 |
| Crack time | 12 min × (`d` / 24) × part multiplier; part multiplier 1.0 (tier 1) to 0.7 (tier 4). Range 5.6 min (small circle, best parts) to 20 min (largest circle, stock parts) |
| Deep veins per circle | 2 + floor(`d` / 8): 4 at 16 tiles, 7 at 40 |
| Vein yield | 1,500–4,000 ore each; unsanctioned ×2 |
| Crack miner rate | 150 ore/min (10–27 min per vein per miner) |
| Grace before fall | 5 min |
| Anchor damage pause threshold | 50% health |
| Disconnect pairing window | 60 s |
| Evacuation window | 60 s |
| Fissure spawns | 2–4 mobs per ring per anchor, cap 15 per anchor; unsanctioned ×1.5 |
| Replacement anchor | 400,000 |

Whole loop with no interference and three or four miners running: roughly 60–80 minutes.

---

## 6. Asset and mapping requirements

Moved to `Docs/PlanetCracker/ASSET_REQUIREMENTS.md` (sprites with sizes and states, sounds, maps, priorities). Keep that file as the single list so the two never drift.

---

## 7. Decisions taken

| ID | Decision |
|---|---|
| D1 | Dedicated orbit layer with space atmosphere; an ordinary map, so every ship can jump to it, see the cracker and chunk on radar, dock, board and shoot. |
| D2 | Chunk is one layer deep (ground layer only). |
| D3 | Chunk has no atmosphere; EVA on the chunk. |
| D4 | Beams are client-drawn from a networked target, projected across layers; no cross-map `BeamSystem`. |
| D5 | Extraction teleports the chunk into place with effects. |
| D6 | Anyone inside the circle rides the chunk up. |
| D7 | Unsanctioned cracks trigger a sector announcement; TSF players intervene freely; no automated response. |
| D8 | The cracker may leave between survey and lock; anchors persist; the console re-validates on return. |
| D9 | Anchor damage pauses the crack below half health, aborts when broken or destroyed; it never triggers the fall. Replacement anchors are purchasable. |
| D10 | Price 3,000,000, one active per round. |
| D11 | One anchor per transport trip, enforced by the transport gravgen's capacity. |
| D12 | Crack time scales with circle diameter and projector part tier; vein count scales with diameter. |
| D13 | Crack console and centrifuge UI are diagram controls drawn in code. |
| D14 | Hostile mobs arrive by crawling out of fissures that spread around drilling anchors, with a final surge at extraction. |
| D15 | Crack miners run on an internal swappable power cell; no cabling across the tether gap. |
| D16 | The transport's shuttle console warns before take-off when its gravgen is overloaded. |
| D17 | Replacement anchors are sold at any cargo console. |
| D18 | The sector survey console also exists at the outpost so crews can plan before buying. |
| D19 | Fissure decals persist for the round. |
| D20 | A mapper-placed berth marker on the cracker defines where the chunk hangs, on any side of the hull. Targeting needs the berth within 8 tiles of the circle; *begin crack* snaps the ship the rest of the way, so the chunk sits directly above the hole. |
| D21 | Cut radius is half the anchor distance plus 2 tiles, so both anchors ride up with the chunk. |
| D22 | Mining numbers: 150 ore/min per miner, 1,500–4,000 ore per vein. Adjust later. |
| D23 | No abort after *begin crack*. Untargeting is only possible before it. |
| D24 | The chunk has a watchdog: if its cracker is gone it drops into the hole on its own. |
| D25 | The centrifuge "at full" check uses hysteresis (≥ 0.98 on, < 0.95 off). |

---

## 8. Remaining questions

None open as of revision 3.

---

## 9. Testing and tooling

- Headless integration tests per the Wolfgate convention: a fixture that builds a ground layer, two air layers and an orbit layer, spawns a cracker grid with a berth marker, centrifuge and projectors, places two anchors on the ground, and drives the state machine directly (no UI). Cover: orbit layer exempts grids from falling; descent still needs a gravgen; pairing refuses out-of-band distances; drill timer; berth alignment refusal outside 8 tiles and the snap on begin; grace timer hysteresis, reset and expiry; fall pushes both grids into transit; extraction tile count includes both anchors and leaves the hole; once-per-round refusal; anchor damage pause and abort; disconnect pairing window; chunk drop lands on the hole's footprint; watchdog drop when the cracker is deleted.
- Admin verbs under the Wolfgate admin tab: set crack state, complete drill, complete crack, force disconnect, mark planet cracked, give a sector planet a surface network at the admin's request.

---

## 10. As built

Build order actually run: F0 → F1+F3 → F4 → F5 → F2 → F7 → F6. F8 is in flight and F9 is planned. Every new file is under `Content.Shared/_WF/PlanetCracker`, `Content.Server/_WF/PlanetCracker`, `Content.Client/_WF/PlanetCracker`, `Resources/Prototypes/_WF/PlanetCracker` and `Resources/Locale/en-US/_WF/planet-cracker`. Three admin commands cover the whole family — `wfplanet`, `wfcracker`, `wfsurvey` (names in `Content.Shared/_WF/Administration/WolfgateAdminCommands.cs`). The master switch is the CVar `wf.planet_networks` (`Content.Shared/_WF/CCVar/PlanetCrackerCVars.cs`), default false, set true in `Resources/ConfigPresets/Build/development.toml`.

**Upstream edits, whole family.** Every marked site is listed here; everything else goes through `_WF` partials of upstream classes, which cost no upstream line.

| Marked site | Call | For |
|---|---|---|
| `Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.Gravity.cs:93` | `WfIsOrbitLayer` | grids parked on an orbit layer never fall (F0) |
| `CEZLevelsSystem.Gravity.cs:453`, `:610` | `GetWFVirtualMass` | crated anchors count against pooled lift, and the readout agrees (D11, F1+F3) |
| `Content.Shared/Shuttles/Systems/SharedShuttleSystem.cs:52` (`CanFTLTo`) | `WfAllowFTL` | orbit is the only FTL door **out** of a planet network, and never a door in (F0) |
| `Content.Client/Shuttles/UI/NavScreen.xaml` (nav settings column) | `<wf:WFOrbitButton>` | the enter/leave orbit button (F0) |
| `Content.Client/Shuttles/UI/NavScreen.xaml.cs` (`SetConsole`) | `WfOrbitButton.SetConsole` | binds that button to the console (F0) |
| `Content.Server/_FarHorizons/StarSystem/StarSystemMapSystem.cs:66` | `WfPlanetSpawned` | registers sector bodies that have a Wolfgate surface (F0) |
| `Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs:621` | `DrawWfBerth` | berth ghost on the radar (D20, F4) |
| `CEZLevelsSystem.Transit.cs` (`RefreshGridZPhysics`, `TryMoveGrid`) | `WfRefreshOrbitParking`, `WfRefusesLevelHop` | a grid on an orbit layer has no z-gravity and is never handed straight to the layer below (F0 fix) |
| `CEZLevelsSystem.PilotControl.cs` (`UpdateTakeoffSpool`) | `WfRefusesOrbitDescent`, `WfHasVerticalLift` | a raw descend input out of orbit is refused so the console's confirm cannot be bypassed, and landing-thruster lift stands in for the gravgen the grid has not got (F10; replaced F0's `WfIsColdOrbitDescent`) |
| `CEZLevelsSystem.Gravity.cs` (capacity sweep, twice) | `WfGravgenIsOnPlanet`, `WfAddLandingThrusterCapacity` | over a planet a gravity generator is no lift and landing thrusters are all of it (F10) |
| `CEZLevelsSystem.Gravity.cs` (`IntegrateFallingGrid`, sink branch) | `WfSinkGravity` | partial lift scales the fall, and lift lost begins (F10) |
| `CEZLevelsSystem.Gravity.cs` (`IntegrateFallingGrid`, landing loop) | `WfTryHardLanding` | a slow touchdown out of lift lost skids instead of exploding (F10) |
| `CEZLevelsSystem.Gravity.cs` (`IntegrateFallingGrid`, landing loop) | `WfSkidAfterCrash` | a crash with planar speed left ploughs on instead of stopping dead (F10) |
| `ShuttleSystem.FasterThanLight.cs` (`TrySetupFTL`, `FTLToDock`) | `WfRefusesFtlDeparture` | no FTL starts from a surface, an air layer or mid-transit, whichever console branch asked (F0/F10) |
| `CEZLevelsSystem.WallCollision.cs` (`UpdateWallCollision`) | `WfPloughThroughWalls` | a skidding hull flattens obstacles instead of bouncing off them (F10) |
| `CEZLevelsSystem.Gravity.cs` (`CrashGrid`) and `Content.Server/Explosion/EntitySystems/ExplosionSystem.cs`, `.Processing.cs` | `QueuedExplosion.Silent` | a grid crash plays and shakes for one blast, not one per tile (F7 fix) |
| `Content.Shared/Gibbing/Systems/GibbingSystem.cs` (`FlingDroppedEntity`) | physics guard | bodiless giblets are not flung (F8) |
| `Resources/Prototypes/Entities/Objects/Weapons/Guns/Turrets/turrets_ballistic.yml` (`WeaponTurretXeno`) | parent swap | one ammo provider instead of two stacked (F8) |

The `_WF` partials those calls land in: `Planets/CEZLevelsSystem.Wolfgate.cs`, `Flight/CEZLevelsSystem.WFFlight.cs`, `Planets/StarSystemMapSystem.Wolfgate.cs`, `Planets/SharedShuttleSystem.Wolfgate.cs` (shared), `Planets/ShuttleSystem.WFOrbit.cs`, `Cracker/CEZLevelsSystem.WFVirtualMass.cs`, `Cracker/CEZLevelsSystem.WFGravityCache.cs`, `Cracker/GravityGeneratorSystem.WFCentrifuge.cs`, `Chunk/BiomeSystem.WFChunkPin.cs`, `Chunk/CEZLevelsSystem.WFChunkMove.cs`, and `Content.Client/_WF/PlanetCracker/Cracker/ShuttleNavControl.Wolfgate.cs`.

**Naming.** Everything the feature sections name without a prefix shipped with `WF`: `WFPlanetCrackerComponent`, `WFChunkBerthComponent`, `WFGravityAnchorComponent`, `WFPlanetChunkComponent`, `WFDeepVeinComponent`, `WFCrackMinerComponent`, `WFFissureSpawnerComponent`, `WFCrackBeamComponent`. `CrackablePlanetComponent` (§4 F2) was never built: `Sanctioned`, `Cracked` and the vein table live on `WFSectorPlanetComponent` (the sector body, so they survive a network rebuild) and on `WFPlanetSurfacePrototype`.

**Borrowed art.** Seven of the placeholder sheets were replaced with art the tree already ships rather than drawn: the anchor crate is the stock engineering secure crate at `scale: 2, 2`, the gravity projector is the `_Mono` AK570 shuttle autocannon, both console screens are stock `computers.rsi` faces, the surveyor is the anomaly locator, both beams draw the `_Mono` ship laser's tintable `grayscale_beam`, the survey pulse is a `SingularityDistortion` with no sprite at all, and the fissure decals are the stock window damage overlays with `EffectSparks` as their burst. The mob-emerge effect was dropped outright. `SPRITE_LIST.md`'s "Uses existing art" table is the index. Because the projector is now a turret, `WFCrackerSystem.Beams.cs` swings each one onto the anchor it is cutting with while the cut runs and hands back the facing the mapper gave it (`WFGravityProjectorComponent.PlacedRotation`) when it ends or aborts.

**No real map anywhere.** There is no shipyard vessel, no cargo product, and no prototype from this family is placed on any map in `Resources/Maps`. The two hulls are code-built by `WFTestGridFactory` (`Content.Server/_WF/PlanetCracker/Testing/`): a 15×15 cracker (shuttle console, `WFCrackConsole`, gyroscope, `MachineFTLDrive`, `WFCentrifuge`, two `WFGravityProjector`, a `WFChunkBerthMarker` shrunk to 12×12 at distance 8, airlock, two `WFAnchorCrate`, four thrusters) and a 7×9 transport (shuttle console, **two `WFThrusterLanding`** since F10, airlock, one `WFAnchorCrate`, four thrusters). Every machine on both is switched to `!NeedsPower` because there is no cabling. They stand in until the user's real maps exist; every mass, capacity and `maxHandledMass` number keyed to them is a placeholder.

### F0 — planet networks, orbit layer, FTL gate

`WFPlanetSurfacePrototype` (`- type: wfPlanetSurface`, keyed by `planetType`) describes a whole stack; `WFSurfaceAsclepiu` binds the Asclepiu sector body to the DeltaV `planet` prototype `WFAsclepiuSurface` (biome `WFBiomeAsclepiu`). `WFPlanetRegistrySystem` registers each spawned Far Horizons body as `WFSectorPlanetComponent`; `WFPlanetNetworkSystem` builds the maps into one CE network and records it with `WFPlanetNetworkComponent`. Every layer carries `WFPlanetLayerComponent`; the orbit map also carries `WFOrbitLayerComponent { Planet, Range, Network }`, space atmosphere and a `WFOrbitBeacon` marker entity named `<planet> orbital marker` (a `WarpPoint` + `IFF` radar label, **not** an `FTLBeacon`). Admin: `wfplanet list | build <planet> | delete <planet> | spawn <surface id> | system <starSystem id> | tp <planet>`.

***Enter orbit* is a shuttle console button, not an FTL jump** — an FTL drive is not a spaceflight prerequisite for reaching a world you are already parked beside. The orbit map is deliberately **not** an `FTLDestination` and `WfAllowFTL` refuses any jump into an orbit layer even if something registers one. `WFOrbitEntrySystem` runs a 1 Hz sweep over every `ShuttleConsoleComponent` and writes `WFConsoleOrbitTargetComponent { Planet, PlanetName, InOrbit, Busy }` onto the console (a networked component rather than `NavInterfaceState`, because the shuttle BUI state is only pushed on docking, beacon and power events and would be stale while the hull flies). The client `WFOrbitButton` — one control in the nav screen's settings column — reads it each frame and labels itself *Enter orbit: Asclepiu* or *Leave orbit: Asclepiu*, sending `WFEnterPlanetOrbitMessage` / `WFLeavePlanetOrbitMessage` on the shuttle console's own UI key. The server re-checks every gate in `TryEnterOrbit` / `TryLeaveOrbit`: pilot of that console, hull on the body's own sector map (never a planet layer or a transit map), within `orbitRange` (2000) of the body, and `CanFTL` clear. The hop is `ShuttleSystem.WfFTLToLayer`, a `_WF` partial that calls the undocumented-as-checkless `FTLToCoordinates` with a fixed 5 s startup and 5 s travel, so undocking, the hyperspace map, the arrival sweep and the transition audio all still run while `GetFTLRange` — which gives a driveless hull a range of zero — is never consulted. Arrival keeps the hull's world XY and rotation when that spot is clear, and falls back to the FTL arrival's free-spot search when it is not. *Leave orbit* is the same hop in reverse onto the sector map; the body's own FTL beacon is still a second route in for a hull that does have a drive.

Deviations and limits:

- The dev sandbox preset has no star system, so nothing is registered until `wfplanet system SystemKyphrus` or `wfplanet spawn WFSurfaceAsclepiu` is run. The round-start path only runs on a Mono preset.
- Asclepiu ships ore marker layers and **no mob marker layers**: marker-spawned entities are never unloaded and a CE z-eye seeds markers under any orbiting hull, so fauna would accumulate for the round. The z-eye guard the plan specifies was not written; F8 declined it too.
- `MapAbove` is never back-wired, so it is null on every planet-network layer and traversal works only through the network dictionary.
- The outbound gate strands a hull whose gravgen dies while it is on a non-orbit layer: the console lists no destinations and offers no orbit button, silently. `wfplanet tp` is the escape hatch. (`WFOrbitBeacon` no longer leaks into every console's beacon list, because it is no longer an `FTLBeacon`.)
- The orbit button is `ShuttleConsoleComponent` only; a `DroneConsoleComponent` piloting a remote shuttle gets nothing. Power is not checked in `TryEnterOrbit` either — the BUI cannot be open without it, so the check would be redundant, not a second gate.
- *Leave orbit* drops the hull at its own orbit XY, which is the body's frame, so a hull parked exactly over the body centre lands on the `WFOrbitBeacon` marker's twin on the sector map. The marker is `FTLSmashImmune`, so this is cosmetic.
- A driveless hull arrives with the ordinary 10 s `FTLComponent` cooldown (`UpdateFTLArriving` falls back to 10 s with no drive to read), so the orbit button stays greyed for ten seconds after every hop.
- Registration matches a body by float-equal position against `SharedStarSystemMapSystem`'s own expression, with a name fallback.
- **Orbit parking had to be fixed twice.** The `WfIsOrbitLayer` exemption only gated the fall sweep. The shared z-physics integrator (`CESharedZLevelsSystem.Update`/`Movement`) has its own gravity: an orbit map is vacuum, so `ComputeGroundHeightInternal` returned -1, `TryMoveDown` fired every tick and `TryMoveGrid` handed the hull straight down one layer at a time with no transit map and no crash — a fresh arrival inside its 3 s grace reached the ground in one tick and landed inside whatever was there. The only thing that ever held a hull up was its origin happening to sit on one of its own tiles. Now a grid on an orbit layer has `VelocityGravity` off and its z state zeroed, `TryMoveGrid` refuses to hand it down, and a downward pilot input from orbit works without a gravgen: a cold hull enters transit and falls for real instead of being stranded. `OrbitArrivalTest` pins all three.

### F1+F3 — hull skeleton and gravity anchors

F1 shipped as skeleton only — components, prototypes and the two code-built hulls above, no BUI and no vessel. F3 shipped whole: `WFGravityAnchorComponent` + `WFGravityAnchorSystem` (`.Pairing`, `.Control`, `.ChunkRide`), `WFAnchorCrateComponent`/`WFAnchorCrateSystem`, `WFAnchorState`, the client `WFCrackCircleOverlay` and `WFCrackCircleOverlaySystem`, and `WFCrackerOwnershipSystem` binding crated anchors to the hull that bought them. Numbers match §5 in code: band 16–40 tiles (`MinDistance`/`MaxDistance`), cut radius `d/2 + 2` (`CutPadding`, D21), 5 min drill (`DrillDuration`), 50% damage threshold. D11 is enforced rather than warned: `WFAnchorCapacitySystem` writes a `WFGridAnchorLoadComponent.VirtualMass` that the CE pooled-lift check reads, so a second crated anchor takes the test transport over its gravgen rating and it drops. Prototypes: `anchors.yml`, `machines.yml` (`WFCentrifuge`, `WFGravityProjector`, `WFTransportGravgen`), `consoles.yml` (`WFChunkBerthMarker`), `boards.yml`. The transport ships with the cracker rather than separately: `WFCrackerOwnershipSystem.Transport.cs` loads `WFPlanetCrackerComponent.TransportMap` beside a newly bought hull and FTL-docks it onto the hull's airlock (parking it alongside if no dock pair fits), and `wfcracker spawn cracker` does the same with the code-built transport through `WFTestGridFactory.BuildCrackerWithTransport`; either way the transport's crate is stamped with the cracker.

Deviations and limits:

- D16 asked for a take-off *warning*; the as-built rule drops the shuttle, with a one-second popup as the only notice. The 31.5/6/40 numbers are tuned to the 63-tile test transport and must be re-derived against a real map.
- Dragging a 450-mass anchor feels exactly like dragging a chair — `PullerComponent`'s speed modifier is a flat 0.95 regardless of mass, and no mass-scaled modifier was added.
- The projector's 64×64 art overhangs its inherited 1×1 fixture (an even-sided AABB cannot align to the snap grid); mappers must leave ≥ 2 tiles between projectors. The crack miner has the same problem for the same reason.
- The berth marker parents `MarkerBase`, so it is invisible and unclickable to ordinary players and routinely outside PVS. Its examine line only reads with markers toggled on.
- `WFGravityAnchorComponent.BreakDamage` is a hand-maintained mirror of the prototype's Breakage trigger; a test asserts they match.
- Deployed anchors get `CEPvsOverrideComponent`, which is a global override — every client replicates every deployed anchor.

### F4 — crack control: console, projectors, centrifuge, fall

`WFCrackerSystem` (`.StateMachine`, `.Lock`, `.Crack`, `.Beams`) owns the whole state machine of §3 on `WFPlanetCrackerComponent`; `WFCrackState`, `WFCrackFailure` and `WFCrackBlocker` (`WFCrackFlags.cs`) are the vocabulary, `WFCrackStateChangedEvent` and `WFCrackCompletedEvent`/`WFCrackerFallingEvent` the hooks. `WFCentrifugeSystem` applies the D25 hysteresis server-side (0.98 on, 0.95 off) because `PowerChargeComponent` has none; `WFGravityProjectorSystem` carries the D12 part multiplier. `WFCrackConsoleSystem` builds `WFCrackConsoleState`; the client draws it entirely in code — `WFDiagramControl`, `WFSiteDiagram`, `WFCentrifugeDial`, `WFProjectorPanel`, `WFCrackTimeline`, in `WFCrackConsoleWindow` and `WFCentrifugeWindow` (D13). Berth alignment, the 8-tile tolerance and the snap on *begin crack* are in `WFCrackerSystem.Lock.cs`; the berth ghost on the radar is `ShuttleNavControl.Wolfgate.cs`. Admin: `wfcracker state <stage>`, `wfcracker complete <crack|drill>`, `wfcracker fall`.

Deviations and limits:

- **The cloud layer was dropped from crackable planet stacks** (`planets.yml`: `cloudLayer: false`, `airLayers: 3`). `ScalingViewport.RenderZLevels` breaks its downward walk at the first `CEZCloudLayerComponent` and the cloud pass paints an opaque deck, so from orbit the crack site was invisible and the circle overlay never got a pass. The air layer count goes 2 → 3 in the same edit to keep the stack five maps deep with orbit at depth 4, so every fall duration stays as designed. The cost is four extra render passes on every orbiting client. `cloudComponents` is left in the prototype for planets that still want a deck.
- The site is still not reliably visible from orbit: the downward walk gates on a single grid resolved at the screen's bottom-left corner, so a pilot in a sealed control room sees nothing below. The console diagram is the authoritative readout; the z-view is decoration.
- The part multiplier is the **mean** of both projectors. §5 quotes only uniform-tier endpoints, so the mean is an invention that reproduces the stated 5.6–20 min range; "worse of the two" would change the formula and its test together.
- `PowerChargeSystem` still zeroes the centrifuge's charge on any non-wrench unanchor (explosion, grid split). During `Cracking` that silently arms the grace timer with a falling dial as the only cue. Not fixed.
- The snap is refused outright when the hull is in a CE grid network of two or more grids (`WFCrackBlocker.InGridNetwork`), because `CEZGridSyncSystem` would revert or propagate the move. A link formed mid-tick is not covered.
- ASSET_REQUIREMENTS asks for a centrifuge spin loop pitch-shifted in code; `SharedAudioSystem` exposes no `SetPitch`, so the rotor dial is the only speed cue.
- D23 holds: there is no abort message and no abort button. `wfcracker state` is the only escape hatch from a stuck precondition chain, which is why it is a shipped command rather than a debug aid.

### F5 — extraction, chunk, hole, beams

`WFPlanetChunkSystem.Extraction.cs` copies the whole `Tile` struct of every disc tile onto a new grid, moves every rider onto it, stamps the hole, and parks the grid in the berth; `BiomeSystem.WFChunkPin.cs` is the only legal writer into `BiomeComponent` and pins the hole and rim tiles so the biome never regenerates over them. `CEZLevelsSystem.WFChunkMove.cs` moves the chunk between maps keeping world XY and rotation. `WFChunkExtractedEvent` and `WFPlanetCrackedEvent` are the hooks; `WFPlanetChunkSystem.Effects.cs` plays the shake, boom and `WFEffectChunkBurst`. Beams are D4 as designed: `WFCrackBeamComponent` on each projector with a networked target, drawn by the client `WFCrackBeamOverlay`, which draws the same line for surface viewers from the anchor up to the mount's world XY — `WFCrackBeamTargetComponent`, stamped on the anchor each sweep because the projector is never in a surface viewer's PVS. Admin: `wfcracker extract`.

Deviations and limits:

- There is **no crack-hole tile and no rim tile prototype**. The hole is `Tile.Empty` and the rim is two decals (`WFCrackRimStraight`, `WFCrackRimCurve`) on pinned biome ground. `Tiles/crack_hole.rsi` and `Decals/crack_ring.rsi` ship referenced by nothing — either a later feature claims them or they should be deleted.
- **Decals only ever draw frame zero**, so the rim decals are rotated per decal through `TryAddDecal`'s `rotation` argument (`snapCardinals: false`, `defaultSnap: false`) instead of through directional RSI states. `crack_rim.rsi` is authored `directions: 4` and three quarters of the sheet is unreachable; the meta is deliberately left alone because changing `directions` without re-cutting the PNGs fails RSI validation. F8's fissure decals take the same code-rotated approach.
- A rim tile that is still space-like after the reserve gets no decal, so a circle cut beside a chasm ships with a visibly broken ring.
- A grid parked inside the cut circle (a landed transport) is deliberately left behind, over empty tiles, neither falling nor landing; including grids would drag dock joints and CE networks with them.
- `MapLight` was added to `orbitComponents` for the chunk's sake, which lights every hull parked on any crackable planet's orbit layer.
- Extraction is a tick spike: roughly 1520 broadphase queries per `TileChangedEvent` on a radius-22 cut, three events in consecutive ticks, plus CE connector re-floods.
- Anchors pin their 3×3 footprints and nothing releases those reservations, so two squares inside the hole stay in `ModifiedTiles` after the disc lifts.
- F5 left `Cracked → Disconnecting → Released` unwritten and the hull force-anchored after extraction; that is F7's.

### F2 — survey console, surveyor, deep veins

`WFDeepVeinComponent` veins come from a `WFDeepVeins` biome marker layer on `WFAsclepiuSurface`, rolled from `WFVeinTablePrototype` (`WFVeinTableAsclepiu`); `WFSurveyorComponent`/`WFSurveyorSystem` is the handheld pulse, `WFSurveyedComponent` the per-player reveal, and the client draws revealed veins through `WFDeepVeinOverlay` and `WFDeepVeinVisualsSystem`. `WFSectorSurveyConsoleComponent` + `WFSurveyConsoleSystem` feed `WFSurveyConsoleState` to `WFSurveyConsoleWindow`, one `WFSurveyPlanetRow` per star-system body with distance, sanctioned/unsanctioned, cracked/intact, the vein rating band (`WFVeinRatingBandsPrototype`, `WFVeinRating`) and the destination name. Admin: `wfsurvey list | veins [radius] | reveal [radius]`.

Deviations and limits:

- **The console does not set an FTL target.** §4 F2 says selecting a planet targets it on the shuttle console; as built the window sends no message at all — selection is a client-local highlight and the row just names the destination for the pilot to find. The console's own hint line says to ping or rebuild the shuttle console's map, because `MapScreen` only rebuilds its destination tree on a ping.
- **Vein count is a density, not §5's `2 + floor(d/8)`.** A biome marker layer places by uniform area density, so veins in a circle scale with r². Tuned as shipped (radius 12, `maxCount` 93, size 128) a small circle can contain zero veins and a large one many more than seven. Either §5 is restated as a density or extraction tops the chunk up; unresolved.
- Ore marker layers with an `entityMask` are silent no-ops where the template plants no rock, so the ocean portion of `WFBiomeAsclepiu` produces no ore at all.
- `WFSurfaceAsclepiu` fixes a biome seed (`seed: 20260913`) so the same world yields the same veins — which also freezes its terrain. Adding or removing **any** marker layer re-rolls every other layer's placement.
- The reveal is client-side visibility with `Ore` and `TotalYield` networked, so it is readable by a modified client.
- D18's outpost survey console is a mapping job that was not done: `WFSectorSurveyConsole` is placed on no map.
- `deep_vein.rsi` is filed under `Decals/` but consumed as an entity sprite. The surveyor's ping reuses `/Audio/Machines/sonar-ping.ogg`, which ASSET_REQUIREMENTS does not list. The surveyor itself and its pulse no longer carry art of their own — see §10's art note.

### F7 — disconnect protocol, evacuation, chunk fall, scar

`WFCrackerSystem.Disconnect.cs` runs the 60 s pairing window (`DisconnectWindow`) and the 60 s evacuation (`EvacDuration`, re-issued every 15 s), `WFPlanetChunkSystem.Disconnect.cs` drops the chunk into downward transit and cleans the wreck up 10 s after impact, and `WFCrackScarSystem`/`WFCrackScarComponent` keep the crater from being healed by a later `ReserveTiles` sweep. `WFCrackerReleasingEvent` and `WFChunkDroppedEvent`/`WFChunkLandedEvent` are the hooks. Admin: `wfcracker disconnect`, `wfcracker rearm`, `wfcracker release`, `wfcracker drop`.

Deviations and limits:

- The crash is **per-tile blasts only** — `CrashIntensityPerTile` is zeroed on every drop path and no central blast replaces it, so the chunk crash is deliberately quieter than an ordinary CE grid crash. The merged footprint is about twice the disc's area, and tile breakage inside it makes the rim decal ring probabilistic in production.
- The wreck is deleted on **all three** drop paths, including the F4 hull-fall and the D24 watchdog, so a hull that simply falls loses its chunk permanently. No design section says this.
- Entering `Disconnecting` permanently disarms the grace timer: a centrifuge or projector fault during the 60 s evacuation no longer drops the hull. A deliberate reading of §3 that §3 does not state either way.
- Only minded mobs, borgs and occupied mind containers are restored off a dropped chunk. Loose mobs, items, crates and **mined ore left on the chunk are deleted with it**, with no warning.
- A player restored onto the wreck's empty crater tile may be treated as standing over nothing and fall again.
- `WFCrackScarComponent` lives on the ground grid, so `wfplanet delete`/rebuild loses every scar while the body stays flagged `Cracked` — the crater reverts to intact terrain with no way to re-cut it. There is no back-fill for a chunk cut before F7 and no admin command to add or clear a scar.
- `Released → Idle` is gated on a settle timer plus a force-anchor check, so a hull force-anchored by a mapper sticks in `Released` forever. Logged once, deliberately.
- The pairing countdown is visible only on the crack console and in three popups near the anchors; the anchor examine line was not added because that subscription is already claimed.
- **A grid crash used to play one explosion per tile.** CE `CrashGrid` queues a blast per hull tile and every blast played two networked audio streams, so a 15×15 hull crash created 226 explosion clips at once, exhausted the client's OpenAL sources and, on a debug client, tripped an engine assert that killed the game. Now only the centre blast (or the first tile when a drop path has no centre blast) makes sound and shakes the camera; damage, craters and tile breakage are unchanged. `CrashAudioTest` pins the count. The ~110 explosion *visual* entities per crash remain.

### F6 — chunk mining

**Hang pose and gangway (playtest round 4).** The chunk hangs on the berth marker's line, pushed out until its near edge sits `WFPlanetChunkSystem.BerthClearance` (2) tiles past the hull's furthest extent along that line, whatever the radius; the marker's `Distance` is only a floor. It is turned about its own disc centre to the hull's heading so its tiles line up with the deck. `WFPlanetChunkSystem.Gangway.cs` then lays a one-tile `Lattice` catwalk on the hull from the marker's tile, over any tile the hull lacks, to one tile inside the disc, refusing whole (logged) if another grid sits on the line or the line never reaches the disc; the tiles are recorded on `WFPlanetChunkComponent.GangwayTiles` and lifted at drop. The hang pose is no longer the hole pose and `DropChunk` does not put it back: the chunk falls from exactly where the rig carried it and lands there, in the hole only if that is where it hangs (decided in playtest; the more honest outcome). Orbit stays deadly off the catwalk: a mob that steps off it falls to the planet, by decision.

`WFCrackMinerComponent` + `WFCrackMinerSystem` (`.Placement`) is a machine wrenched onto a chunk tile over a deep vein; it refuses to run anywhere else, converts `Remaining` into ore at the vein's `Rate` (150/min, D22) in batches of 25 while its internal cell holds out (D15), and `WFCrackMinerState` drives the sprite. Prototypes `WFCrackMiner` and `WFCrackMinerEmpty` in `mining.yml`, board `WFCrackMinerCircuitboard`. Admin: `wfcracker veins` lists every seam on the hull's chunk with what is left and whether a miner sits on it, `wfcracker mine` plants a `WFCrackMiner` on the first free live seam.

Deviations and limits:

- **The miner has no off state.** `crack_miner.rsi` ships `idle`/`mining`/`exhausted`/`broken` only, so `WFCrackMinerState` has no `Off` member and idle doubles as off. `ASSET_REQUIREMENTS.md:26` says otherwise and is stale.
- **The cell blacklist is server-only in effect.** `BatterySelfRechargerComponent` is declared only in `Content.Server`, so the client drops the name from the blacklist and *predicts* a successful insert of a self-recharging cell, corrected a tick later by server state. The server refusal and the `wf-crack-miner-cell-rejected` popup are what the player actually gets. Fixing the mispredict would mean tagging upstream `powercells.yml`; accepted as cosmetic.
- The miner is `bodyType: Dynamic` and `anchored: false`, so an unanchored one can be shoved around by explosions and thrown crates, and one built from a machine frame arrives anchored without ever raising the placement gate — it looks installed and silently never runs.
- **There is no way to buy a crack miner**, nowhere to recharge a cell on the hull, and no cargo product for either. Where miners and cells come from is an open mapping/economy job.
- `WFDeepVeinComponent` is `[UnsavedComponent]`: a partly mined chunk does not survive a map save/load.
- F5's ride-up is an upper bound on vein count — a vein whose destination chunk tile is empty is left on the ground with an error log.
- `Remaining` is deliberately not networked, so the vein is never dirtied by mining; the examine line is the readout.

### F8 — site threats: fissures

`WFFissureSpawnerComponent` rides on every `WFGravityAnchor`; `WFFissureSpawnerSystem` (`.Rings`, `.Mobs`, `.Surge`, `.Control`) arms it when a real drill starts and disarms on lock, break, destroy, switch-off or pair dissolve. Five rings spread at 0/20/40/60/80 % of the drill (radius 2 then +1.5 per ring): each ring picks 2–4 free tiles on its band, pins them against biome regeneration, stamps `WFFissure1..4` decals (promoted a stage per ring), plays a burst (`WFFissureSpawnerComponent.BurstEffect`, the stock `EffectSparks`) and rolls 2–4 mobs from the surface's `faction` table (`planets.yml`: Asclepiu uses `Xenos`, unsanctioned `Argocytes`, both chosen because their ground mobs carry NavSmash and can chew an anchor). Unsanctioned worlds multiply fissures and mobs by 1.5. A cumulative cap of 15 mobs per anchor. At extraction a surge of 6 (9 unsanctioned) crawls out of the crack rim band per anchor, ordered before `WFPlanetChunkSystem` so it lands on ground that is about to be cut, and only when the cut will actually go ahead (no chunk held, planet not already cracked, berth resolvable). Every spawned mob with a `MobState` is re-rooted onto `WFFissureThreatCompound` (target the anchor via `WFFissureTargets`, then melee, then idle) and aggroed against the anchor; pre-existing bystanders and turret entries are left alone. Anchors answer examine with their fissure count. Admin: `wfcracker begin drill` starts both drills for real, `wfcracker fissure ring | surge` forces a ring or the surge.

Deviations and limits:

- **Threats are only a threat with players nearby.** `NPCSystem` pauses every HTN mob with no player within 32 tiles on the *same map*, so an anchor left drilling with the crew in orbit is never attacked. The design's "the anchors need defending" holds only while someone stays on the surface.
- **The 3×3 anchor is outside NPC melee range** from its own tile edge, so the only damage path is the NavSmash obstacle branch of pathfinding. Factions without NavSmash (Cultists, Mercenaries, Explorers, Punks, most Silicons) and flying Carps cannot hurt an anchor at all; keep them off `wfPlanetSurface.faction`.
- **Fissure mobs are never cleaned up.** They are not biome-loaded entities, so unloading a chunk leaves them and can drop the ground from under them. The per-anchor cap and the surge budget are the only bounds for the round.
- **Pinned fissure tiles never unpin** (`BiomeSystem.WFChunkPin` has no unpin), so each site permanently stops regeneration and marker generation on up to ~30 tiles per anchor.
- **Fissure decals inside the cut circle are destroyed at extraction** with the tiles; D19's "persist for the round" holds only outside the disc.
- **Acid-blooded threats erase decals.** Xeno blood carries `PryTileReaction`; a Xeno crushed by the landing chunk deconstructs the tiles under it and `TileSystem.DeconstructTile` deletes every decal there, rim and fissure alike. Accepted as upstream behaviour; the F7 wreck test deletes the surge threats before landing for that reason.
- **One upstream edit** (`// WOLFGATE`): `GibbingSystem.FlingDroppedEntity` skips bodiless giblets. Landing grids gib crushed mobs, and gibbed organs drop solution entities with no physics; without the guard every landing logged hundreds of resolve errors.
- Fissure decals draw frame 0 only, so rotation comes from the stamping angle (same as the crack rim). Stages 3 and 4 are the same crack: the borrowed sheet ships three. A static `ProtoId<>` of a server-only prototype kind in a test class fails the YAML linter (it validates the test assembly on the client instance too); use a `const string`.

### F9 — sanction notices

`WFCrackSanctionSystem` listens to the crack state machine and to `WFPlanetCrackedEvent`. When a hull enters `Cracking` above a body whose `WFSectorPlanetComponent.Sanctioned` is false, it sends one global announcement from "TSF Sector Watch" naming the ship and the planet with the attention sound; a second, silent notice goes out when the chunk lifts. `WFCrackNoticeComponent` on the body latches each notice: the crack notice re-arms when the hull drops back to `AnchorsLocked` (an abort-then-retry announces again, on purpose), the extraction notice is permanent for the round. CVar `wf.planet_cracker.announce` (server-only, default true) silences both. Locale in `sanction.ftl`. Nothing responds automatically (D7); `WFPlanetCrackedEvent` remains the necromorph hook and F9 adds nothing to it.

Deviations and limits:

- **The notice is global, not per sector.** `ChatSystem` can filter announcements, but nothing in the fork can build an "everyone in this sector" filter, so every player hears it.
- **No shipped planet is unsanctioned.** Asclepiu ships `sanctioned: true`, so the notices never fire on the dev map until a surface flips the flag or the survey rework adds unsanctioned worlds; the tests use their own unsanctioned surface.
- Richer unsanctioned vein tables and marker veins from the original F9 list are not built; the vein table is per surface (F2) and can simply be pointed at a richer table.

### F10 — atmospheric flight

Lift over a planet is landing thrusters and nothing else. `WFLandingThrusterComponent { LiftThrust }` rides an ordinary thruster; a hull's pooled capacity on a WF planet layer is the sum of the `LiftThrust` of every landing thruster that is `Enabled` and `IsOn`, divided by the layer's `gravity`, plus infinity for a force-anchored grid as before. A gravity generator contributes nothing there — the same marked sweep that adds the thrusters skips every gravgen on a planet — so the cracker's centrifuge stops being lift the moment the hull is over a world. Dividing at the sweep rather than at the comparison is deliberate: CE's own "pooled mass fits inside pooled capacity" test then *is* "lift ratio at least one", with no second rule to keep in step. `CEZLevelsSystem.WfTryGetLiftRatio(grid, out r)` pools the same numbers over the whole rigid set for the console readout; `gravity` is a new `wfPlanetSurface` field copied onto every `WFPlanetLayerComponent` at build time.

**Partial lift.** `r >= 1` flies as CE flies anything. `0.5 <= r < 1` scales CE's downward acceleration by `(1 - r)`, so a hull just shy of flying sinks gently. Under 0.5 it falls at the full rate. Orbit is still free. Anything below orbit at `r < 1` is a `WFLiftLostComponent` hull.

**The descent decision is the console's, once.** In orbit the shuttle console offers *Enter atmosphere: &lt;planet&gt;* under the orbit button with the lift ratio beneath it, coloured green / amber / red at the 1.0 and 0.5 thresholds and computed server-side onto `WFConsoleOrbitTargetComponent.LiftRatio`. `WFEnterAtmosphereMessage { Confirmed }` is refused by the server when `r < 1` and not confirmed; the client answers with `WFEnterAtmosphereConfirmWindow`. The hull is dropped into the gap below orbit at progress 0.7 — under CE's settle zone, or a hull *with* lift would drift straight back into orbit — seeded at 0.15 levels/s. The raw pilot descend input out of orbit is now refused with a popup naming the button, which is what makes the confirm unavoidable. Below orbit F and R work normally for anything that can fly.

**Lift lost.** `WFFlightSystem` watches every `WFLiftLostComponent` hull each tick. Entry remembers the planar velocity and the ship's own situation code, starts the `lift_lost.ogg` caution alarm **looping** to everyone aboard for the whole emergency, and sounds `WFAlertLiftLost` over it; a hull that was holding station is nudged to `GlideMinSpeed` along its own facing so every fall glides rather than dropping like a brick, and each layer the hull falls through multiplies the planar velocity by `1 + GlideGainPerLayer` along its own heading. The alarm loop is re-cut every `AlarmReissue` for anyone who boarded mid-fall and is stopped by the component's own shutdown, so recovery, landing and grid deletion all end it. The callouts are `ShipAlertCodePrototype`s set through `ShipAlertSystem.SetCode`, `selectable: false` so no pilot can pick one: `WFAlertDontSink` out of orbit, `WFAlertSinkRate` and `WFAlertTerrain` stepping down the stack, `WFAlertTooLowTerrain` in the last gap and `WFAlertPullUp` in its final third, re-announced every 3 s. The stage only ever advances. The state ends when `r >= 1` again or the hull is off a transit gap, and the ship's own code is restored silently.

**Touchdown is measured against the fall, not against a fixed speed.** CE zeroes `CEZGridFallerComponent.Velocity` at every layer boundary - the plummet path zeroes it before `TryEnterTransit` and the landing path zeroes it on the way out - so a fall is one gap long however high it started, tops out near **0.47 levels/s** and never approaches the 1.2 `GridTerminalVelocity` the taper aims at. The original fixed `WFHardLandingSpeed` of 0.8 levels/s therefore sat above every touchdown in the game and made every free fall a hard landing, which is exactly what the playtest saw: no explosion, no damage, and a hull that stopped dead where it touched. The threshold is now `WFHardLandingFraction` of `CEZLevelsSystem.WfGetFreeFallSpeed(faller)`, which integrates CE's own `ApproachTerminal` over the one level a gap is worth and caches the answer per (gravity, terminal) pair. A free fall arrives at exactly that reference and is a **crash**; the partial-lift band arrives at about `sqrt(1 - r)` of it - 0.74 at the 0.5 floor - and is a **hard landing**, which is why the fraction is 0.8 and not something lower.

**Touchdown.** In CE's landing loop a lift-lost hull under the threshold becomes a hard landing instead of a crash: `WFSkidComponent`, `hard_landing.ogg` once and `skid.ogg` looped, and the hull keeps the planar speed it arrived with. **A hard landing hurts.** At touchdown every tile of the hull takes `ImpactTileDamage` scaled by impact over the reference - light, and under the tear-off threshold on its own - and, when the hull came in with planar speed, the leading edge takes `ImpactEdgeDamage` on top of that through the same tile-damage helper the skid uses, so a fast hard landing tears its nose off and the machinery along it with it while the hull itself survives. **A crash that still had somewhere to go carries on:** after `CrashGrid` a lift-lost hull still moving faster than `SkidRamSpeed` starts a skid - with no second thud, one crash is still one bang - instead of stopping on the tile it detonated over. CE's ground friction then grinds it out as it already does for anything that flew in. Above `SkidRamSpeed` the leading-edge tiles — the ones furthest along the direction of travel, by projection, so a corner-first slide loses its corner — take damage every quarter second and are torn off with a small silent blast when they have had enough; `ShuttleSystem.Smimsh` crushes whatever is under the hull; and the anchored obstacles CE's wall pass would have bounced are broken and cost speed instead. A hard-landed hull is an ordinary grid: repairable, and it takes off again the moment `r >= 1`. At or above the threshold it is the existing `CrashGrid`. The F7 chunk drop never has a lift-lost hull, so it keeps its own straight-down crash untouched.

**Atmospheric ambience.** `WFFlightAmbienceSystem` sweeps every grid at 1 Hz and gives each one below orbit and off the ground - air layers and the transit gaps between them, never orbit and never the ground layer once landed - a looping `atmo_wind.ogg` stream played to everyone aboard through the grid filter the evacuation alarm uses (`PlayGlobal` + `Filter.AddInGrid`, never `PlayPvs` on the grid, which on a capital hull is wind in one corridor). Volume runs from `WindMinVolume` to `WindMaxVolume` across `WindMaxSpeed` of planar speed and is written live onto the stream; pitch is quantised and the loop re-cut when the band changes, because a live stream's pitch cannot be changed. A hull carrying `WFLiftLostComponent` gets a second stream, `fall_rumble.ogg`, whose volume climbs to `RumbleMaxVolume` as the fall speed approaches the same free-fall reference the landing is judged against. Both loops are re-cut every 30 s so somebody who boarded mid-flight is inside the filter, and both are stopped by `WFFlightAmbienceComponent`'s own shutdown - landing, reaching orbit and grid deletion all remove the component, so no stream is left playing in nullspace.

**FTL out of an atmosphere is refused at the departure, not at the destination.** `WfAllowFTL` only ever runs inside `CanFTLTo`, which is consulted by the console branches that pick a destination out of a list; the beacon and free-FTL branches call `FTLToCoordinates` directly, and it documents itself as taking no checks - which is how a landed hull FTL'd off the planet on the live server while `CannotFTLOffASurfaceLayer` still passed. `ShuttleSystem.WfRefusesFtlDeparture` is now asked at the two places an FTL actually starts, `TrySetupFTL` (every `FTLToCoordinates` path) and `FTLToDock` (which never calls it), and refuses any departure from a map carrying `WFPlanetLayerComponent` without `WFOrbitLayerComponent`, or carrying `CEZTransitMapComponent`. The orbit hop `WfFTLToLayer` starts from orbit or from the sector map, so it is unaffected.

**Prototypes and data.** `WFThrusterLanding` (parent `Thruster`, `requireSpace: false` because planet layers have air in them, `liftThrust: 50`) and `WFLandingThrusterKit`, an item that swaps an anchored ordinary thruster for the landing variant in place. The code-built transport carries two landing thrusters instead of its gravity generator; the F4 `Fall` additionally puts the hull into lift lost so the alarms run. Audio is `/Audio/_WF/PlanetCracker/Flight/*.ogg` — eight generated placeholder tones from `Tools/_WF/PlanetCracker/gen_flight_placeholders.py`, plus `atmo_wind.ogg` and `fall_rumble.ogg`, which the same script copies verbatim from `/Audio/Effects/Weather/wind_2_1.ogg` and `/Audio/Effects/space_wind.ogg` because a synthesised tone makes a poor continuous bed. Nothing is ever overwritten, so a real recording dropped in under any of these names survives the script. All ten are marked as placeholders in their own `attributions.yml`. Locale in `flight.ftl`. Tests: `FlightTest.cs`.

**Tunables.**

| Name | Where | Default |
|---|---|---|
| `LiftThrust` | `WFLandingThrusterComponent`, per prototype | 50 (`WFThrusterLanding`) |
| `gravity` | `WFPlanetSurfacePrototype`, copied to `WFPlanetLayerComponent` | 1 |
| `WFFullLiftRatio` | `CEZLevelsSystem.WFFlight.cs` | 1 |
| `WFPartialLiftRatio` | `CEZLevelsSystem.WFFlight.cs` | 0.5 |
| `WFHardLandingFraction` | `CEZLevelsSystem.WFFlight.cs` | 0.8 of the free-fall reference (~0.47 levels/s) |
| `WFPloughSpeedCost` | `CEZLevelsSystem.WFFlight.cs` | 1.5 m/s per obstacle |
| `WFOrbitRefusalCooldown` | `CEZLevelsSystem.WFFlight.cs` | 4 s |
| `GlideGainPerLayer` | `WFFlightSystem.cs` | 0.25 |
| `GlideMinSpeed` | `WFFlightSystem.cs` | 2 m/s along the hull's facing |
| `AlarmReissue` / `AlarmVolume` | `WFFlightSystem.cs` | 20 s / -6 dB |
| `PullUpProgress` | `WFFlightSystem.cs` | 0.35 of the last gap |
| `PullUpRepeat` | `WFFlightSystem.cs` | 3 s |
| `SkidStopSpeed` | `WFFlightSystem.Skid.cs` | 0.5 m/s |
| `SkidRamSpeed` | `WFFlightSystem.Skid.cs` | 4 m/s |
| `SkidTileDamageRate` | `WFFlightSystem.Skid.cs` | 3 per second per m/s of speed |
| `SkidTileThreshold` | `WFFlightSystem.Skid.cs` | 40 |
| `SkidEdgeDepth` | `WFFlightSystem.Skid.cs` | 1.5 tiles |
| `SkidTileSpeedCost` | `WFFlightSystem.Skid.cs` | 0.5 m/s per tile lost |
| `SkidBiteInterval` | `WFFlightSystem.Skid.cs` | 0.25 s |
| `ImpactTileDamage` | `WFFlightSystem.Skid.cs` | 12, hull-wide, at full severity |
| `ImpactEdgeDamage` | `WFFlightSystem.Skid.cs` | 45, leading edge only, at full severity |
| `WindMinVolume` / `WindMaxVolume` | `WFFlightAmbienceSystem.cs` | -14 dB / -2 dB |
| `WindMaxSpeed` | `WFFlightAmbienceSystem.cs` | 30 m/s |
| `RumbleMaxVolume` | `WFFlightAmbienceSystem.cs` | -3 dB |
| `AtmosphereEntryProgress` / `AtmosphereEntrySeed` | `WFOrbitEntrySystem.Atmosphere.cs` | 0.7 / 0.15 levels/s |

Deviations and limits:

- **Every existing mapped ship is now a brick over a planet.** Nothing in the tree carries a landing thruster, so any hull that drops out of orbit falls. That is the design, but it means the kit — or a mapper's edit — is a prerequisite for planet-side flight, and the cracker itself has none.
- **The kit is `AfterInteractEvent` on the kit, not `InteractUsingEvent` on the thruster.** `ThrusterComponent`'s directed subscriptions are already claimed and only one owner per (component, event) pair is allowed server-wide. Same interaction for the player: click an anchored thruster with the kit in hand.
- **The orbit control is a `BoxContainer`, not a `Button`.** `WFOrbitButton` keeps its name and its `SetConsole` binding — the two marked lines in `NavScreen.xaml`/`.xaml.cs` are unchanged — but it now stacks the orbit button, the enter-atmosphere button and the lift readout vertically, because the nav settings column is one narrow stack and a readout beside the button would have squashed it.
- **`MoveGridSetToMap` already preserves planar velocity** (it saves and restores linear and angular velocity around the map change), so the glide needed no fix there. It does not survive *friction*: a grid's ordinary airborne damping eats into the glide over the seconds a fall takes, so two 25% boosts land nearer 1.3x than 1.5625x. The gain is still per layer; it is just not free, and `FlightTest` asserts the weaker bound for that reason.
- **Lift lost begins in a transit gap, not on the layer.** The state is entered from CE's sink branch, which only runs for a grid already in a gap, so a hull that loses a thruster while hovering enters lift lost within the half second the fall gate takes to push it into one, not on the tick the thruster died. The deliberate descent is the exception: it enters the state before the hull has moved, so the caution chime sounds first.
- **The prior code is restored silently.** `SetCode(..., announce: false)`, so a hull that lands does not read its old code back out over the PA; the alarms simply stop.
- **The skid's wall pass destroys every hard anchored body under the footprint**, not only the ones CE reported a contact with — the contact record carries boxes, not entities. It only runs on a tick where CE did find a contact, so a hull sliding over open ground destroys nothing.
- **Nothing tests the F4 fall's alarms end to end.** The hook is one line in `WFCrackerSystem.Crack.Fall` and `FlightTest` covers the same path from the console; a cracker that somehow had `r >= 1` would fall without alarms, which the design did not consider.
- **The transport's gravity generator moved into the tests.** `WFTransportGravgen` is still the D11 anchor-capacity fixture and still a shipped prototype, but the factory no longer puts one on the hull; the three `CrackerTestGridTest` cases about it bolt one on themselves through a local `AddGravgen` helper. The knock-on is that the **transport no longer overloads on two crates**: two 50-rated landing thrusters are 100 of lift against 43.5 of loaded hull. Cargo still weighs against lift and the readout still moves, but D11's *count* cap (`WFAnchorCapacity`) rides the gravgen and is therefore only reachable with one aboard. Playtest §8 says so.

### F11 — orbit decay

**An orbit layer holds up only what is holding itself up.** A grid keeps station while it has at least one `ThrusterComponent` of type `Linear` that is `Enabled` and `IsOn` (landing thrusters count, gyroscopes do not), **or** it carries `ForceAnchorComponent`, **or** it is docked — directly or transitively — to a grid that has one of those. Everything else on an orbit layer is decaying: a ship whose power died, a hull whose thrusters were shot off, a fragment split off in combat, debris, a wreck. Station-keeping is deliberately not the lift question: a hull with one landing thruster rates 0.40 of its own weight and could not hover for a second, and it still holds an orbit.

`WFOrbitDecaySystem` sweeps at 1 Hz over every grid whose map carries `WFOrbitLayerComponent` — the orbit marker is not a grid and is skipped by that alone, and a `WFPlanetChunkComponent` grid is exempt deliberately, because F7 owns when a chunk falls (a countdown stamped while it was still a bare grid is cleared rather than merely ignored). A grid with no station-keeping gets `WFOrbitDecayComponent { Grace, DecayAt, Announced, PriorCode }`, a `WFAlertOrbitDecay` situation code (`selectable: false`, the `lift_lost.ogg` chime) and one PA line naming the seconds it has left. Regaining station-keeping inside the grace drops the component and hands the ship its own code back silently, the same restore F10's lift-lost state does. On expiry the hull goes down `WFOrbitEntrySystem.TryDropFromOrbit` — the very routine the console's *Enter atmosphere* button ends in, refactored out of `TryEnterAtmosphere` rather than copied — so an unmanned wreck falls with the same seed, the same transit, the same lift-lost state and the full GPWS sequence a piloted hull gets. **Wrecks are never cleaned up**: the feature puts grids on the ground and stops.

**The console says so.** `WFConsoleOrbitTargetComponent.DecaySeconds` (-1 while stable) is filled by the existing `WFOrbitEntrySystem` sweep and `WFOrbitButton` reads *Orbit: stable* or *Orbit decaying: 42 s* under the lift ratio, green or red.

**Planet flight is slow flight.** A world's surface streams in around whatever is over it, so a hull crossing a planet layer at shuttle speeds outruns its own terrain and the chunk loader behind it. `WFPlanetDragSystem` sweeps every grid each tick: on a layer carrying `WFPlanetLayerComponent` — orbit and the air layers, never the ground layer once landed and never a transit gap, which carries no layer marker at all — it lends the grid the layer's `LinearDamping` (remembering the grid's own on `WFPlanetDragComponent`) and clamps its linear velocity to the layer's `MaxSpeed`. Leaving the layer by any route hands the damping straight back. Thrusters are untouched; they fight the drag rather than beat it. A `WFLiftLostComponent` hull is allowed `LiftLostAllowance` over the cap so a glide still reads as a fall, and the chunk and force-anchored grids are skipped entirely.

**Tunables.**

| Name | Where | Default |
|---|---|---|
| `Grace` | `WFOrbitDecayComponent`, per grid | 60 s |
| `SweepInterval` | `WFOrbitDecaySystem.cs` | 1 s |
| `SettleDelay` | `WFOrbitDecaySystem.cs` | 10 s |
| `orbitMaxSpeed` / `orbitDamping` | `WFPlanetSurfacePrototype`, copied to `WFOrbitLayerComponent` | 6 m/s / 3 |
| `airMaxSpeed` / `airDamping` | `WFPlanetSurfacePrototype`, copied to `WFPlanetLayerComponent` | 12 m/s / 1.5 |
| `LiftLostAllowance` | `WFPlanetDragSystem.cs` | 1.5x the layer's cap |

Deviations and limits:

- **An arrival is given ten seconds, and a stamp takes two sweeps.** An FTL hop lands with the shuttle's thrusters disabled and they come back on their own power event, so the first sweeps after an arrival read a powered ship as adrift — a vessel a crew had only just boarded was stamped and dropped on a countdown it never earned. `SettleDelay` runs from the sweep that first sees a grid on the layer (and is dropped the moment it leaves, so a return is a fresh arrival), and a stamp additionally needs the grid read as adrift on two sweeps running. Neither delays a genuine loss by more than a sweep once the hull has settled.
- **The warning is announced by hand, not by `SetCode`.** `SetCode`'s own announcement takes only `$ship`, and the one thing the crew needs here is how many seconds they have, so the code is set with `announce: false` and the countdown line goes out through `ShipPaSystem.Announce` — exactly the shape F10's repeating pull-up callout uses. The prototype's own `announcement` is the countdown-free wording, for an admin who sets the code by hand.
- **A ship that was on no code at all keeps the warning code after recovery.** `PriorCode` is null, and F10's restore has the same shape: there is nothing to hand back.
- **A refused drop retries.** If `TryDropFromOrbit` says no — the hull is mid-FTL, or the stack has nowhere below it — the component stays with its deadline already past and the sweep tries again a second later. The code is handed back before the drop and put back up if the drop failed, so the PA never reads the warning out twice.
- **Docking is followed by ports, not by CE's rigid set.** `CollectRigidSet` also welds z-network connectors, which would let a network membership hold a wreck in orbit; the rule the user set is docking, so the flood is over `DockingSystem.GetDocks` and `DockingComponent.DockedWith` and asks for no `ShuttleComponent`, which a fragment does not have.
- **The drag restore is tested by moving the grid off the layer**, not by the orbit hop: to the sweep they are the same event — a change of map — and the hop itself is already covered by `OrbitArrivalTest`.
- **Every existing hull in orbit is now on a 60-second clock unless something aboard is running.** That is the design, but it means a mapper's derelict parked on an orbit layer needs `ForceAnchorComponent` (or a longer `Grace` on the component) if it is meant to stay put.

Tests: `Content.IntegrationTests/Tests/_WF/PlanetCracker/OrbitDecayTest.cs` and `PlanetDragTest.cs`.


### Atmospheric thruster revision (2026-09-18)

Ordinary linear engines now provide atmospheric lift from their actual thrust ratings.
Below orbit, including the surface and transit gaps, they run at 50% thrust and draw
three times their normal APC power continuously while enabled. Landing-converted
engines retain full thrust and normal draw. Orbit and open space restore the normal
ratings; repeated transitions do not stack modifiers. Gyroscopes do not provide lift.

Lift capacity is effective force / 9.81; the existing pooled hull mass, virtual anchor
cargo mass and planet gravity determine the lift ratio. The same calculation is used
by the console, fall gate and takeoff. Hover consumes part of the thrust budget, with
the remaining fraction available for planar manoeuvring and climbing. Power loss
removes an engine's lift immediately; recovery needs two seconds of stable power.

The landing kit upgrades an engine in place, preserving damage, parts, model, size
and electrical connection. It no longer replaces a large engine with a small one.
The obsolete fixed liftThrust field is removed from the landing marker.

The orbit console reports estimated atmospheric engine demand and warns if the
connected APC networks lack headroom. Descent requires confirmation for either
insufficient lift or a forecast power deficit. This is a current network-capacity
estimate, not a guarantee that batteries or fuel will last for the whole flight.
Aerumna is 3 g. SHIP_LANDING_ASSESSMENT.md lists the measured stock vessel limits.

Content-only upstream hooks: ThrusterSystem update and part refresh; MoverController
planar force; CEZLevelsSystem vertical acceleration. No RobustToolbox source edits.

GPWS voice warnings retain normal PA delivery when a speaker can broadcast. If the
PA has no working speakers, the voice uses one hull-audience emergency stream, so
a brownout or missing PA cannot silence terrain/pull-up warnings while the separate
lift-lost alarm continues. The fallback never duplicates a successful PA broadcast.


### Planetary crash breakup

Ship crashes on planetary ground now cut narrow structural seams instead of queuing an explosion over every hull tile. The planner checks real tile connectivity, aims for two to four substantial sections, and severs at most one eighth of the original floor footprint. After the grids separate, those seam cells are restored as exposed lattice owned by an adjacent wreck section, preferring the largest side of the tear. The lattice moves with its section without reconnecting the grids or creating loose lattice-only fragments. Small or unsuitable hulls can remain in one piece rather than producing tiny fragments. At most eight small, silent explosions (up to four along fractures and one inside each section) accompany the existing single crash sound. Above-deck, unshaded explosion animations make the blasts visible over the overlapping terrain grid; machinery destroyed at the seams may still cause ordinary secondary damage. Extracted planet chunks retain their previous drop/crater behavior.

Touchdown delivers one blunt-impact shock and knockdown to living crew aboard, including occupants away from a fracture. Severity and planar speed increase injury; buckling halves the shock damage. A one-second per-grid latch prevents duplicate injury from repeated callbacks. This does not make fracture-line occupants safe from local explosions or destroyed equipment.

Wreck sections retain their momentum through the existing grid split. Detached fragments inherit crash-skid friction and one positional grinding loop each. Every sliding section clears nearby obstacles and leaves a dirt trail. Small mass-balanced outward impulses separate the sections. Fractures prefer diagonals where connectivity allows, vary from 15 to 90 percent across the footprint, and can shear off smaller ends or sides rather than always bisecting the hull. Skid resistance and per-bite speed loss were increased by 25 percent from the previous long-slide tuning, targeting approximately 20 percent shorter travel rather than a guaranteed distance on every terrain. Existing dirt scars and clearance remain.

### Atmospheric overload retries

Ordinary atmospheric thrusters now forecast full demand on their actual APC network before the next power solve. When full demand exceeds supply, they rest at a one-watt standby load for two seconds, then retry full atmospheric draw for one second. Cooling disables actual thrust and blocks power-change callbacks from restarting engines early, while preserving the player's enabled switch. Adequate supply, leaving atmosphere, or conversion removes the limiter. Converted landing thrusters retain normal continuous operation.

The retry never supplies free power: the real network still determines whether thrusters, lights and consoles have power. Bursts can brown out an overloaded ship; rest periods release engine demand so the rest of the grid can recover. Missing generation, disconnected cabling or insufficient non-engine supply can still leave equipment off.

### Jagged seams and damaged crash power

Fracture boundaries wander by about one tile and retain the torn seam as exposed lattice on one separated grid. Impact audio is a single +8 dB broadcast to the hull audience and the footprint radius plus 32 metres, rather than many overlapping positional sources.

A crash while ordinary engines are overload-limited damages their supplying onboard APC regulators. Each damaged APC interrupts its actual output independently, with randomized 2-5 second dropouts and 1.2-3.2 second powered stretches. The fault persists until a three-second multitool (Pulsing) repair; examination explains the fault and repair gives feedback. Repair preserves the main-breaker setting and does not invent power. Grounded wrecks revert to normal engine demand; an ascent command restores atmospheric demand and its overload protection. The grounded check is cached per grid per tick.

### Severed engine commands

A linear engine firing at impact retains that command if the split leaves it on a section without an anchored shuttle console. Actual power, enable state and nozzle checks still gate thrust. A dropout stops the burn without erasing the command; power recovery resumes it. The existing thrust loop follows firing engines on each separated section and stops during a blackout, with one stream per section. Manual engine shutdown, unanchoring or adding a console clears the retained command. Detached engines are removed from the original ship's thrust bank, so the surviving pilot cannot command remote wreckage.

Powered console-less sections receive their engines' directional force, limited to 2 metres/second squared and 12 metres/second along the force direction to keep wreckage from becoming streaming-speed projectiles. They retain terrain scars while sliding. Sections with a console receive normal shuttle controls instead; no extra crash sound loops are created.

Crash burst tuning: each small blast uses total intensity 15 (previously 12), slope 3 and maximum tile intensity 3.5. Visible flashes are scaled to 1.2, with a 3.5-metre light radius; the eight-burst limit and no floor removal remain. Atmospheric power forecasts tolerate freed electrical nodes while the power network rebuilds after destruction.

### Ground grinding and rock contacts

Every moving grounded wreck section plays the supplied ground_grind_loop at +6 dB, audible up to 80 metres beyond its half-diagonal. The positional loop follows the section; it stops on rest, takeoff or removal, and refreshes its audience every ten seconds without stacking streams. Touchdown and rock/terrain-obstacle contacts randomly use crash10, crash11, crash12, crash3 or crash4. Obstacle impacts allow one contact per section per 0.75 seconds and replace any previous contact stream.

Soil abrasion affects up to four leading-edge tiles per quarter second, at 0.15 damage per metre/second per second (maximum 0.5 per update). Rock contact applies 0.5-3 blunt damage to up to four nearby tiles and their anchored structures, as already-reduced wear (bypassing flat armour subtraction) and excluding living occupants. The stronger hull clears the obstacle; worn floor exposes lattice at 40 accumulated damage instead of recursively fragmenting or exploding. All sections retain their terrain trails. ground_impact_2 was superseded by the five crash variants and is not imported.

Crash detachment now also unlocks section rotation and applies alternating randomized angular kicks of 0.2-0.35 rad/s, scaled by relative mass (0.65-1.5x). Inherited spin is retained, with the initial result capped at +/-0.65 rad/s. Crash-skid friction also scales angular drag, so the kick remains visible before settling. A section rotating against the ground keeps its grind loop and wear even when its centre is barely translating.
