# Wolfgate planets: how they are actually built (Planets-and-cracking worktree)

Source: `C:/Users/jzo12/Documents/GitHub/Wolfgate/.claude/worktrees/modest-chaum-01364c` (branch `planet-cracking`,
HEAD `c6bd838a99 planets module`). Read-only survey. All paths below are relative to that root.

## 0. Where the code is right now

- HEAD `c6bd838a99` moved all planet code out of `_WF/PlanetCracker` into a new **`Planets`** module and deleted
  `Content.Server/_WF/PlanetCracker/README.md`. The live README is `Content.Server/_WF/Planets/README.md`.
  Markers are now `WOLFGATE(Planets)`; only one `WOLFGATE(PlanetCracker)` marker remains upstream
  (`Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs:641`, `DrawWfBerth`).
- The previous commit `ae49e906f5` removed planet cracking. In this worktree the `_WF/PlanetCracker` files are back as
  **staged, uncommitted** additions (git status `A`). Treat cracking as separate, unmerged work.
- Planet docs: `Docs/_WF/Planets/{PLANET_ECOLOGY.md, PLAYTEST_CHECKLIST.md, SHIP_LANDING_ASSESSMENT.md}`. The history
  of the build is in `Docs/_WF/PlanetCracker/PLANET_CRACKER_DESIGN.md` §10 "As built" (F0 networks, F10 flight, F11 orbit decay).
- Master switch: the CVar `wf.planet_networks` (`Content.Shared/_WF/CCVar/PlanetCVars.cs`), `SERVERONLY`, default
  **false**. It is set to true in `Resources/ConfigPresets/Build/development.toml` (marked). `CCVars.Game.cs` default preset
  changed to `MonoStandard` (marked), whose round start spawns the star system.

## 1. How a planet surface is built

### Prototypes
- `WFPlanetSurfacePrototype`, kind `wfPlanetSurface` (`Content.Shared/_WF/Planets/WFPlanetSurfacePrototype.cs`). Fields:
  `PlanetType` (FarHorizons `PlanetTypePrototype`, required; the registry keys on it), `Ground` (DeltaV `planet` proto,
  required), `GroundGrid` (optional `ResPath`: a hand-made grid loaded at the planet centre; **no prototype uses it**),
  `AirLayers` (2), `CloudLayer` (true), `Gravity` (1 g), `OrbitRange` (2000), `OrbitMaxSpeed` (6 m/s), `OrbitDamping` (3),
  `AirMaxSpeed` (12 m/s), `AirDamping` (1.5), `BuildAtRoundStart` (false), `Sanctioned` (true), `Seed` (int?, fixed biome
  seed), `NetworkComponents` / `GroundComponents` / `AirComponents` / `CloudComponents` / `OrbitComponents`
  (ComponentRegistry), `OrbitMapName`, `OrbitMarkerName`.
- DeltaV `PlanetPrototype`, kind `planet` (`Content.Shared/_DV/Planet/PlanetPrototype.cs`): `Biome` (biomeTemplate),
  `MapName`, `MapLight`, `AddedComponents`, `Atmosphere` (GasMixture), `BiomeMarkerLayers` (ores).
- Files: `Resources/Prototypes/_WF/Planets/planets.yml` (Asclepiu surface), `planet_surfaces.yml` (the Asclepiu `planet`),
  `kyphrus_planets.yml` (Fervidus, Merak, Aerumna, Thrascias: surface + `planet` + `biomeTemplate` each),
  `carcinoma.yml` (planetType/palette/surface/planet/biome for the sixth world), `open_biomes.yml`, `biomes.yml`,
  `carcinoma_regions.yml`, `fauna.yml`.

### The six worlds (all `airLayers: 3`, `cloudLayer: false`, `orbitRange: 2000`, `buildAtRoundStart: true`)

| Surface | planetType | g | seed | air (K, moles) | sanctioned | sector distance |
|---|---|---|---|---|---|---|
| WFSurfaceFervidus | PlanetFervidus | 0.9 | 20260914 | 373.15, [0,75,25] (no O2) | yes | 6200 |
| WFSurfaceMerak | PlanetMerak | 1.15 | 20260915 | 318.15, breathable | yes | 9400 |
| WFSurfaceAsclepiu | PlanetAsclepiu | 1 (default) | 20260913 | 293.15, breathable | yes | 12800 |
| WFSurfaceAerumna | PlanetAerumna | **3** | 20260916 | 285.15, [5,70,25] | yes | 17500 |
| WFSurfaceCarcinoma | WFPlanetCarcinoma | 1 | 20260918 | 310.15, breathable | **no** | 22000 |
| WFSurfaceThrascias | PlanetThrascias | 1.25 | 20260917 | 180, [0,100] | yes | 27500 |

Sector positions come from `SystemKyphrus` in `Resources/Prototypes/_FarHorizons/Space/systems.yml` (Carcinoma entry is a
marked addition). Every layer map uses the same world XY as the sector, so a planet's ground is centred on the body's
sector position (6-27 km from origin).

### Build sequence (`WFPlanetNetworkSystem.BuildNetwork`, `Content.Server/_WF/Planets/WFPlanetNetworkSystem.cs`)
1. Registration: `StarSystemMapSystem` (marked call `WfPlanetSpawned`) -> `WFPlanetRegistrySystem.RegisterPlanet` matches the
   spawned body to its star-system entry by exact float position (name fallback), adds `WFSectorPlanetComponent
   {Surface, Network, OrbitMap, Sanctioned}` (networked, unsaved), and builds at once if `buildAtRoundStart`.
2. `PlanetSystem.SpawnPlanet(surface.Ground, runMapInit: false)` (`Content.Server/_DV/Planet/PlanetSystem.cs`) creates a
   map and calls `BiomeSystem.EnsurePlanet`: the **map entity itself** gets `MapGridComponent`, `PlanetMapComponent`,
   `BiomeComponent`, `GravityComponent{Enabled, Inherent}`, `MapLightComponent`, `RoofComponent`, `LightCycleComponent`,
   `SunShadowComponent`, `SunShadowCycleComponent`; marker layers are added; the map atmosphere is set.
3. Fixed seed applied (`BiomeSystem.SetSeed`) before any chunk loads.
4. **`WFPlanetGroundSpawnedEvent(Ground, Surface)`** raised broadcast (by ref), before the ground grid and before any
   chunk generates. `WFDeepVeinSystem` (cracking) uses it to add its own biome marker layers.
5. Optional `GroundGrid` loaded with `MapLoaderSystem.TryLoadGrid(..., offset: centre)`, then `BiomeSystem.ReserveTiles`
   over its AABB so no rocks spawn inside. (Upstream `OnBiomeMapInit` also calls `_shuttles.Disable` on every grid already
   on the map at MapInit.)
6. Creates `AirLayers` empty maps, optional cloud map, and the orbit map; `CEZLevelsSystem.CreateMapNetwork(networkComponents)`;
   adds depths 0..N one per call; `InitializeZNetwork`.
7. Per layer after MapInit: `WFPlanetLayerComponent {Network, Gravity, MaxSpeed, LinearDamping}`; optional
   `WFPlanetAmbienceComponent`; ground/air/cloud/orbit component registries; orbit gets space atmosphere, loses
   `MapLight`, gets `WFOrbitLayerComponent` and a `WFOrbitBeacon` marker at the centre (`WarpPoint adminOnly`,
   `FTLSmashImmune`, `IFF`; not an FTL beacon).
8. Network entity gets `WFPlanetNetworkComponent {Planet, GroundMap, OrbitMap, Layers, Surface, Centre}` (unsaved), then
   `WFPlanetWeatherSystem.Configure`.
- Teardown: `DeleteNetwork` clears the body record, deletes transit maps touching any layer, then
  `CEZLevelsSystem.DeleteMapNetwork`. A body's `ComponentShutdown` deletes its network.
- Each world = 5 maps (6 worlds = 30 maps), built at round start. Surface prototype comment: no `FTLDestination` on the
  ground ("an FTLDestination on a mapgrid map makes arriving hulls disable themselves").

### Is the ground infinite? Yes.
- Upstream `BiomeSystem` (`Content.Server/Parallax/BiomeSystem*.cs`, Mono-split partials) runs every 0.1 s: for every
  player's attached entity and every `ViewSubscription` on a map with an enabled `BiomeComponent`, it adds all 8x8 chunks
  (`SharedBiomeSystem.ChunkSize = 8`) within `_loadRange` (= `net.maxupdaterange` rounded up to 8) and loads them.
  Unload check every 10 s. **No bounds exist anywhere**: terrain generates wherever anyone goes.
- Unload keeps a tile if it is in `ModifiedTiles`, has an anchored entity, or differs from the biome tile; unmodified
  anchored biome entities (rocks, trees) are deleted and regenerate. Player walls and floors therefore persist in memory.
  Tiles/entities removed or changed are pinned in `BiomeComponent.ModifiedTiles` (a DataField).
- WF partial `Content.Server/_WF/Planets/BiomeSystem.Wolfgate.cs` exposes `WfPinTiles`, `WfIsPinned`, `WfLoadChunk`,
  `WfUnloadChunk` (manual chunk control, written for cracking).
- Radar samples the biome recipe for unloaded chunks (`WFPlanetRadarSystem.Sample`), so it also draws infinite terrain.

### What a bounded, set-size planet would need (gaps, not built)
- A chunk filter: `AddChunksInRange` / `_activeChunks` are private upstream; bounding needs a marked edit or a new
  partial hook, or pre-generate the area once (`WfLoadChunk` + `WfPinTiles`) and then `BiomeSystem.SetEnabled(false)`.
- An edge: beyond the bound the ground is empty tiles at depth 0; needs a wall/sea/cliff ring or clamp on mobs and grids.
- Orbit and air bounds: a hull enters orbit at its own sector XY anywhere within `orbitRange` (2000 m) of the body and keeps
  that XY all the way down. `WFOrbitLayerComponent` has speed/damping caps but no positional bound. Descent points would
  have to be clamped or remapped into the bounded area.
- Radar: `WFPlanetRadarSystem.Sample` must return empty outside the bound.
- POIs: no POI system exists. Candidates: biome marker layers added in `WFPlanetGroundSpawnedEvent`, or grids loaded like
  `GroundGrid` with `ReserveTiles`. Fauna sites only come from biome marker entities (see 7).
- Persistence: every WF planet component is `[UnsavedComponent]`; networks are rebuilt each round from fixed seeds, and all
  player changes to the ground are lost at round end.

## 2. The z-level stack

- CE z-network (`Content.Server/_CE/ZLevels`): depth 0 ground (`CEZGroundLayer`, `CEZLevelRoof`), depths 1-3 air layers
  (`MapLight`, `Gravity{enabled, inherent}`), depth 4 orbit. No world uses a cloud layer (its opaque deck hid the ground).
- Moving between layers uses on-demand `CEZTransitMapComponent` maps (`LowerMap`/`UpperMap`), one per moving grid set.
  Console FTL is refused on a transit map (`WFPlanetNetworkSystem.OnConsoleFTLAttempt`, broadcast subscription).
- `WFPlanetLayerComponent` (every layer, networked, unsaved): `Network`, `Gravity` (g), `MaxSpeed` 12, `LinearDamping` 1.5.
  `WFPlanetDragSystem` applies the cap/damping (restores the grid's own damping on leaving; `WFPlanetDragComponent`).
- `WFOrbitLayerComponent` (orbit only): `RadarLayers`, `RadarSeed`, `RadarGround`, `RadarScars` (List<Vector3> xy+radius,
  "Holes cut into the ground ... other modules add them"), `Planet`, `Range`, `Network`, `MaxSpeed` 6, `LinearDamping` 3.
  Orbit is vacuum, no gravity component, no weather, keeps space lighting.
- Orbit parking (`Content.Server/_WF/Planets/CEZLevelsSystem.Wolfgate.cs`): `WfRefreshOrbitParking` turns off
  `VelocityGravity` and zeroes z state for grids on orbit; `WfRefusesLevelHop` stops a grid being handed straight down.
- Entering orbit is a **shuttle-console button**, not FTL (`WFOrbitEntrySystem.TryEnterOrbit`): pilot of that console,
  hull on the body's sector map, within `Range`, `CanFTL` clear; then `ShuttleSystem.WfFTLToLayer` (5 s spool + 5 s
  travel, keeps XY and rotation if the spot is clear, else FTL free-spot search). Unsanctioned worlds need a confirmed
  request (client `WFUnsanctionedOrbitConfirmWindow`). Leaving orbit is the same hop back to the sector map.
- FTL gates: `SharedShuttleSystem.WfAllowFTL` (marked in `CanFTLTo`) forbids FTL **into** any orbit layer and **out of** any
  planet layer except orbit; `ShuttleSystem.WfRefusesFtlDeparture` (marked in `TrySetupFTL` and `FTLToDock`).
- Console state: `WFConsoleOrbitTargetComponent` on every shuttle console, refreshed at 1 Hz (planet, in orbit,
  unsanctioned, busy, liftoff available/active, lift ratio, atmosphere power demand/deficit, decay seconds). Client
  `WFOrbitButton` (marked into `NavScreen.xaml`) drives Enter/Leave orbit, Enter atmosphere, Liftoff.

## 3. Ground vs hull

- **Ground** = the planet map entity itself (map + `MapGridComponent` + `BiomeComponent` + `WFPlanetLayerComponent`).
  CE's `HasGroundUnderFootprint(grid, map)` (`CEZLevelsSystem.Gravity.cs:664`) only checks non-empty tiles of the map's
  own grid under the hull's AABB. `WfHasSkidGround` = on a planet layer map that is a grid, with ground under footprint.
- **Hull** = any separate `MapGridComponent` entity on a layer. `WFPlanetBiomassSystem.IsPlanet`: "A separate hull remains a
  legitimate host even when its map is a planet."
- `WFDetachedTerrainComponent` (`Content.Shared/_WF/Planets/WFDetachedTerrainComponent.cs`, networked, empty): a separate grid
  that is **ground, not a hull**. Honoured by flight/landing clearance, structural crash, drag, orbit decay, Carcinoma,
  biomass, ambience. Only cracking's chunk extraction adds it.
- `WFPlanetBuiltTilesComponent` (unsaved, `Underlay: Dictionary<Vector2i, Tile>`): remembers the natural tile under
  lattice laid on ground.
- Building on the ground (marked edits in `Content.Shared/Tiles/FloorTileSystem.cs` and `Content.Shared/Maps/TileSystem.cs`):
  rods lay lattice **straight onto untouched biome ground** (`WfIsPlanetTerrain` compares the tile with
  `TryGetBiomeTile`), storing the underlay; cutting that lattice restores the ground instead of a hole. Built tiles use
  the normal base-turf chain. So outposts built in place live **on the ground map grid**, not on their own grid.
- `WFFlightSystem.Lattice.cs` is unrelated to building: `RestoreCrashLattice` keeps the severed seam of a broken-up hull as
  lattice on one section.
- `SharedMoverController.Input.cs` (marked): stepping off a rotated hull onto planet ground snaps to cardinal like a grid.

## 4. Hooks other modules use

- `WFPlanetGroundSpawnedEvent(EntityUid Ground, WFPlanetSurfacePrototype Surface)` (`Content.Server/_WF/Planets/`),
  broadcast, before generation.
- `WFLiftoffAttemptEvent {Cancelled, Reason}` (`Content.Shared/_WF/Planets/Flight/`), raised on every member of the
  liftoff transit set; users: `WFCarcinomaInfestationSystem`, cracking's `WFCrackerSystem`.
- `WFGridLiftLoadEvent(float Mass)` (`Content.Server/_WF/Planets/Flight/`), raised on a grid when pooled lift is weighed;
  add virtual cargo mass. Summed by `CEZLevelsSystem.GetWFVirtualMass` (marked into CE's lift and readout).
- `WFOrbitLayerComponent.RadarScars` for radar holes; `WFDetachedTerrainComponent` for ground-like grids.
- Public APIs worth reusing: `WFOrbitEntrySystem.TryDropFromOrbit(grid, out reason)`, `TryEnterOrbit`, `TryLeaveOrbit`,
  `TryEnterAtmosphere`; `CEZLevelsSystem.WfTryGetLiftRatio`, `WfCanLiftoff`, `WfClearLandingObstacles`;
  `WFPlanetWeatherSystem.TryGetReport/TryGetState/SetMinuteOfDay/SetWeather/Strike`; `WFPlanetRegistrySystem.GetPlanets`,
  `TryGetPlanetByName`, `TryGetSurface`; `WFPlanetNetworkSystem.BuildNetwork/DeleteNetwork`; `WFGridAudienceSystem.Aboard`.

## 5. Day/night, weather, timepiece

- One scheduler per world: `WFPlanetWeatherComponent` on the network entity (unsaved, paused-aware: `Profile`,
  `PlanetName`, `Current`, `Epoch`, `NextChange`, `Phase`, `Storm`, `NextThunder`). `WFPlanetWeatherSystem` updates at 1 Hz.
- `WFPlanetWeatherPrototype` (kind `wfPlanetWeather`, `planet_weather.yml`): `PlanetType`, `Weather` list (single-phase),
  `Storms` list of `WFPlanetStorm {Telegraph?, Main, End?, TelegraphSeconds 30, EndSeconds 30, Thunder}`,
  `ClearMinSeconds` 180, `ClearMaxSeconds` 420, `WeatherMinSeconds` 120, `WeatherMaxSeconds` 240, `DaySeconds` 3600,
  `InitialHour` 8. Overrides: Merak clear 300-600 s; initial hours Asclepiu 8, Fervidus 12, Merak 15, Aerumna 18,
  Thrascias 6, Carcinoma 21. No profile changes `DaySeconds`, so a local day is 60 real minutes.
- Storm phases: `WFStormPhase {None, Telegraph, Main, End}`. Weather is applied with upstream `SharedWeatherSystem.SetWeather`
  on ground + air + transit maps, never orbit. Weather is visual/audio only (no damage); roofs shelter as upstream.
- Thunder (`WFPlanetWeatherSystem.Storms.cs`): only in a storm's Main phase with `thunder: true` (Asclepiu heavy rain,
  Carcinoma blood storm); every 6-22 s, `Strike(ground)` picks a random player on the ground map, tries 8 tiles 4-20 m away
  that `CanWeatherAffect` (unroofed), spawns `WFThunderbolt`, map-wide thunder, electrocutes everything within 0.7 m
  (25 damage, 3 s, ignores insulation).
- Clock: `GetMinuteOfDay = InitialHour*60 + elapsed*1440/DaySeconds` (private). Published every second into
  `WFPlanetEnvironmentComponent {PlanetName, MinuteOfDay, Weather}` on **every layer and transit map** (networked).
  **Night query: `WFPlanetEnvironmentComponent.IsNight`** (shared property: before 06:00 or from 18:00). Only the client
  ambience reads it today; nothing server-side acts on night.
- Daylight (`WFPlanetWeatherSystem.Daylight.cs`): drives stock `LightCycleComponent` and `SunShadowCycleComponent` from
  the same clock; `MinLightLevel` 0.12; orbit excluded.
- Admin partial (`WFPlanetWeatherSystem.Admin.cs`): `TryGetState`, `SetMinuteOfDay` (shifts `Epoch`), `SetWeather(world,
  weather?, duration)` (held as a storm's End phase, then the cycle resumes).
- Timepiece: `WFPlanetTimepiece` (`timepiece.yml`), armband slots, `StaticPrice` 50, cargo product 100 (engineering),
  free Contractor loadout (marked in `_NF/Loadouts/contractor_loadout_groups.yml`), AstroVend. `WFPlanetTimepieceSystem`
  reports planet, HH:mm and actual surface weather on use/examine; worn it shows a HUD (`WFPlanetTimepieceHudSystem`).

## 6. Gravity

- Planet gravity is a single float (`WFPlanetSurfacePrototype.Gravity` -> `WFPlanetLayerComponent.Gravity`). It is used
  **only for ship lift**: `CEZLevelsSystem.WfTryGetPlanetGravity` / `WfTryGetLiftRatio` (a transit map uses the layer below).
- Mobs, items, jetpacks, throwing and movement ignore it; ordinary SS14 gravity is on/off (`GravityComponent`).
- Admin Planet Control sets it per world (clamped 0.05-10 g) and invalidates the lift cache. Aerumna (3 g) lets 6 of 144
  stock vessels hover on ordinary engines, 51 if all engines are converted (`SHIP_LANDING_ASSESSMENT.md`).

## 7. Fauna, wildlife, biomass

- Biome templates place invisible `WFPlanetFaunaSpawner` marker entities (e.g. `WFFaunaAsclepiu`, `fauna.yml`; each points to an
  `entityTable`). `WFPlanetFaunaSystem.OnMapInit` registers a site (8-tile cell, max 2048 sites FIFO) and deletes the marker
  unless `KeepEntity` (a nest, e.g. Carcinoma sacks, `SpawnDelay` 180 s).
- Every 5 s: up to 64 candidate sites, at most 4 spawns per pass; a site must be within 64 tiles of a player on the ground and
  outside 24 tiles of every observer (nests excepted); ground loaded and unobstructed; <= 4 animals per 32-tile area;
  caps `MaxPerPlanet` 32, `MaxTotal` 128; site cooldown 180 s. Ghost roles are stripped.
- Spawned mobs get `WFPlanetWildlifeComponent {Ground, Protected, LastNearby}`; unprotected ones retire after 120 s beyond
  96 tiles of every observer. Damage, possession, a mind, or reparenting (onto a hull/container) protects them for good.
- Fauna is ambient only: no aggression schedule, no raids, no day/night behaviour. NPC AI sleeps beyond 32 tiles of players
  (`npc.pause_when_no_players_in_range`). Site registry and living list are in-memory only.
- Biomass: `WFPlanetBiomassSystem` deletes chimera biomass on ground/detached terrain; on hulls it caps it at 256 per hull.
  Marked edits in `CreateEntityTileReaction.cs` and `_Mono/.../Chimera/biomass.yml` stop blood/action biomass seeding planets.

## 8. Jetpacks

- `Content.Shared/Movement/Systems/SharedJetpackSystem.cs` (6 marked lines): `protected bool WfInAtmosphere(user)` = user's map
  has `WFPlanetLayerComponent` and not `WFOrbitLayerComponent`. `CanEnable` refuses there; an active pack is turned off
  there; the refusal popup is `wf-jetpack-atmosphere` ("cannot hold you up in atmosphere").
- `Content.Server/Movement/Systems/JetpackSystem.cs` (marked): cuts the pack of a wearer carried below orbit on a hull
  (their parent never changes).
- On orbit a jetpack holds you on the layer; when it stops you fall. No "atmospheric jetpack" exists; nothing reads gravity.

## 9. Parachutes and falling from orbit

- `WFParachute` item (`parachute.yml`, price 150; AstroVend + autolathe via marked `lathe.yml` edit). Use on a crate, item,
  person or yourself: 3 s do-after adds `WFParachutedComponent {Deployed, FallSpeed 1.5 levels/s, Pack}`.
- `WFParachuteSystem` (shared): opens on `CEZLevelFallMapEvent` (falling through a level, not a ledge step), clamps z-velocity
  to -1.5, zeroes fall damage/stun (`CEZFallingDamageCalculateEvent`), and on landing (height <= 0.05 over ground) the server
  spawns the pack at the spot and removes the component. Removable by hand before deployment.
- Orbit has no gravity: a loose object on the orbit layer must be pushed down (`ParachuteTest` sets z-velocity -2). Air
  layers do have gravity.
- `WFOrbitalMobFallSystem`: a live mob without a parachute that falls out of orbit into the layer below screams, and on ground
  impact loses one arm and one leg and is set to critical+1 damage (fall damage itself replaced).

## 10. Flight, landing, liftoff

- Lift ratio (`CEZLevelsSystem.WFFlight.cs`): sum over the rigid set of `WfAtmosphericForce/9.81` over
  `(fixture mass + virtual mass) * g`. Ordinary linear engines give 0.5x thrust at 3x power below orbit; engines with
  `WFLandingThrusterComponent` keep 1x/1x (`ThrusterSystem.WFAtmosphere.cs`). `WFThrusterLanding` entity, or
  `WFLandingThrusterKit` (price 400; YouTool + autolathe) converts an anchored linear thruster. Gravgens give no lift over a
  planet (marked). A `CEZMappingAnchorGridComponent` set counts as infinite lift.
- `WFFullLiftRatio` 1: hover. Below 1 the set is lift-lost (`WFLiftLostComponent`); 0.5-1 sinks at `g*(1-ratio)`.
  Remaining thrust above hover is the manoeuvring factor.
- Descent: only via the console's **Enter atmosphere** button from orbit (`WFOrbitEntrySystem.TryEnterAtmosphere`);
  asks for confirmation if lift < 1 or power is short; `TryDropFromOrbit` seeds transit at progress 0.7, 0.15 levels/s.
  Raw descend input on orbit is refused. Below orbit, airborne input is CE's normal control.
- Where you can land: **anywhere** under the hull's XY. There is no pad, beacon or slope rule. CE slows the approach near
  terrain (`TouchdownSpeed` 0.06, `ApproachGain` 1.2). On arrival `TryExitTransit` calls
  `ShuttleSystem.Smimsh(grid, explodeGrids: true)` (`ShuttleSystem.FasterThanLight.cs:1390`): every entity under the
  hull's fixtures that is not on the hull and not `FTLSmashImmune` is deleted (bodies are gibbed), biome tiles under it are
  reserved, and any **other grid** intersecting it is crushed: `CrushGrid` explodes **both** grids at the overlap
  (intensity = area x 5, clamped 300-25000). The Frontier "no gibbing" guard only applies on the default sector map.
- Touchdown outcome (marked block in `CEZLevelsSystem.Gravity.cs` ~406-424): if impact < `GridCrashVelocity` (0.35 levels/s)
  or no ground under footprint, it just lands. Otherwise per grid: `WfClearLandingObstacles` (breaks and deletes every
  anchored, hard, `Impassable` entity **on the ground map grid** within 1 m of hull tiles and reserves those tiles),
  then `WfTryHardLanding` (lift-lost and impact < 0.8 x free-fall speed: crew shock, tile damage, skid), else
  `WfTryStructuralCrash` (planet ground, not detached terrain), else CE `CrashGrid` (per-tile explosions, one sound) and
  `WfSkidAfterCrash`.
- Liftoff: grounded hulls use the console **Liftoff** button (latched `WFLiftoffComponent`); gates in `WfCanLiftoff`:
  `WFLiftoffAttemptEvent` veto, planet flight, no `FTLComponent`, **no `ForceAnchorComponent` anywhere in the transit set**,
  no power deficit, ratio >= 1. The latch stops once high in the first gap (progress 0.8). `WFFlightSystem.PlayTakeoff`.
- Leaving the planet: climb to orbit, then Leave orbit or FTL (FTL only from orbit).

## 11. Crash, skid, breakup

- `WFSkidComponent`: every 0.25 s while speed > 0.5 m/s: `WfClearLandingObstacles(reportImpacts)`, `ScarSkidGround`
  (ground under the hull becomes `FloorPlanetDirt` + dark "Damaged" decal, tiles reserved, <= 256 per bite, never water/
  lava), `WearSlidingHull`, and `Smimsh(grid)` for the main hull. Tile friction is scaled by 0.0125 while skidding. `WfPloughThroughWalls` (marked in
  CE wall collision) breaks hard obstacles hit, costing 1.5 m/s each. Grind loop audio.
- Hard landing: `ImpactTileDamage` 12 on all tiles, `ImpactEdgeDamage` 45 on the leading edge; a tile tears off at 40.
- Structural crash (`WFFlightSystem.Breakup.cs`): cuts the hull into 2-4 sections (4 if severity >= 1.4 or >= 14 m/s, 3 if
  >= 1.1 or >= 8 m/s) via `WFCrashFractures.Plan`, breaks machinery on the cut, `WFCrashBurst` + silent explosions
  (intensity 15, `maxTileBreak: 0`, no vacuum), lattice seam kept; sections skid as debris.
- Extras: `WFCrashApcFaultSystem` (crash-damaged APCs flicker until multitool repair), `ThrusterSystem.WFCrashThrust`
  (severed engines keep last command), crash audio collections in `crash_effects.yml`.

## 12. Orbit decay, station keeping, gravity well

- `WFOrbitDecaySystem` (1 Hz sweep): grids on an orbit layer without station keeping start a countdown after a 10 s settle
  and two consecutive adrift sweeps. Station keeping = any enabled, `IsOn` **linear thruster** on the grid or anything
  docked to it, or a `ForceAnchorComponent`. `WFOrbitDecayComponent.Grace` 60 s; PA warning with countdown, code
  `WFAlertOrbitDecay`; then `TryDropFromOrbit` (lift-lost if it cannot hover). Detached terrain is exempt.
- `WFGravityWellSystem`: on the sector map, an adrift non-static shuttle within `orbitRange` of a body (no FTL, no
  `ForceAnchor`, no station keeping) is pulled after 10 s: 0.15 -> 1 m/s^2 from rim to capture line, infall cap 2 -> 8 m/s;
  inside 35% of range after 30 s adrift it is captured onto the orbit layer. One PA warning per fall.

## 13. Ship radar terrain

- Client partial `Content.Client/_WF/Planets/ShuttleNavControl.Terrain.cs` (+ marked `ShuttleNavControl.xaml.cs` lines):
  a terrain toggle draws ~50x50 samples under the radar every 0.5 s. `WFPlanetRadarSystem` (shared) samples the real ground
  tile if loaded, else the biome recipe carried on `WFOrbitLayerComponent` (never loads chunks); `RadarScars` draw as holes;
  biome scenery entities (lava, water) via `SampleFeature`. Separate grids on the ground are not part of this terrain.

## 14. Admin

- **Planet Control** (`Content.Server/_WF/Planets/Administration/PlanetControlSystem.cs`, client window, Wolfgate admin tab;
  command `planetcontrol`, `AdminFlags.Admin`): per world set local time (h:mm), weather + seconds (1-86400, `clear` allowed),
  gravity (0.05-10 g), sanction. Unbuilt worlds only take the sanction. Logged as `AdminCommands`.
- **`wfplanet`** (`Commands/WFPlanetCommand.cs`, `AdminFlags.Server | Mapping`): `list | build <planet> | delete <planet|id> |
  spawn <surface id> (standalone network at origin) | system <starSystem id> | tp <planet>` (orbit centre). All but `list`
  need `wf.planet_networks`.

## 15. ShipPa hooks (GPWS callouts)

- `WFFlightSystem` (lift-lost) and `WFOrbitDecaySystem` call `ShipAlertSystem.SetCode(grid, code, announce: false)` then
  `ShipPaSystem.Announce(grid, message, proto.Sound, color: proto.Color)`; `ShipPaSystem.GetShipName(grid)` fills `{ship}`.
  If the PA fails to broadcast, the voice line plays to `WFGridAudienceSystem.Aboard(grid)` directly.
- Codes (`flight_alerts.yml`, `shipAlertCode`, `selectable: false`): `WFAlertLiftLost`, `WFAlertDontSink` (orbit gap),
  `WFAlertSinkRate` (top air), `WFAlertTerrain` (middle), `WFAlertTooLowTerrain` (last gap), `WFAlertPullUp` (last 35% of
  the last gap, repeats every 3 s), `WFAlertOrbitDecay`. Lift-lost also loops `lift_lost.ogg` (re-issued every 20 s) and
  restores the prior code on recovery. `WFGravityWellSystem` sends a plain `Announce` (`wf-gravity-well-warning`).

## 16. Carcinoma

- Unsanctioned flesh world (`carcinoma.yml`, `carcinoma_regions.yml`, `infestation.yml`): `WFFloorFlesh` ground, static
  meat walls, flesh trees, blood rivers (static entities), `WFCarcinomaAssimilationSack` nests, `WFFleshPustule` traps;
  blood rain/storm with thunder (visual only).
- `WFCarcinomaInfestationSystem` (2 s sweep): any separate, non-detached grid resting on Carcinoma's ground (checked by
  surface id `"WFSurfaceCarcinoma"`) gets `WFCarcinomaInfestationComponent`; after 45-75 s, then every 45-90 s while still,
  it grows a `WFCarcinomaHullTendril` on the exposed perimeter (max 4), **turns the grid Static**, converts tagged `Wall`
  entities in the 3x3 around it into `WallMeat`, and seeds one `ChimeraFleshKudzu` on an open perimeter tile (hull cap 256).
  Tendrils die at 40 damage; with none left the body type is restored (unless `ForceAnchor`) and 45 s of grace follow.
  Tendrils veto liftoff through `WFLiftoffAttemptEvent` (`wf-carcinoma-tethered`).

## 17. Marked upstream edits (`WOLFGATE(Planets)` in 36 files outside `_WF`, plus one `WOLFGATE(PlanetCracker)`)

Server: `Chemistry/TileReactions/CreateEntityTileReaction.cs` (no planetary chimera hive); `Explosion/EntitySystems/
ExplosionSystem.cs` + `.Processing.cs` (silent flag, one bang per crash); `Movement/Systems/JetpackSystem.cs` (cut pack
below orbit); `Physics/Controllers/MoverController.cs` (reserve thrust for lift); `Shuttles/Systems/ShuttleSystem.
FasterThanLight.cs` (no FTL off surface/air/transit, 2 sites); `Shuttles/Systems/ThrusterSystem.cs` (atmospheric rating,
power, overload recovery, severed engines; 5 sites); `_CE/ZLevels/Core/CEZGroundFrictionController.cs` (crash skids slide);
`CEZLevelsSystem.Gravity.cs` (15: gravgen off on planets, landing-thruster lift, orbit never falls, sink/lift lost, landing
outcomes, clearance, silent crash, virtual mass); `CEZLevelsSystem.GroundFriction.cs`; `CEZLevelsSystem.PilotControl.cs`
(7: liftoff latch, orbit descent only via button, only surplus thrust climbs); `CEZLevelsSystem.Transit.cs` (4: orbit holds
height, leave orbit through transit, rider contacts, climb pops into orbit); `CEZLevelsSystem.WallCollision.cs` (skid
ploughs); `_FarHorizons/StarSystem/StarSystemMapSystem.cs` (register bodies).
Shared: `CCVar/CCVars.Game.cs` (MonoStandard preset); `Maps/TileSystem.cs` (lattice gives ground back);
`Movement/Systems/SharedJetpackSystem.cs` (6); `Movement/Systems/SharedMoverController.Input.cs` (ground is not space);
`Shuttles/Systems/SharedShuttleSystem.cs` (`WfAllowFTL`); `Tiles/FloorTileSystem.cs` (7: lattice on ground, underlay);
`_CE/ZLevels/Throwing/CEZLevelThrowingSystem.cs`; `_CE/.../CESharedZLevelsSystem.WallCollision.cs` (2);
`_CE/.../CESharedZLevelsSystem.Update.cs`.
Client: `Audio/ContentAudioSystem.AmbientMusic.cs` (2); `Light/RoofOverlay.cs` (2: open grating is no roof);
`_CE/ZLevels/Core/Overlays/CEZLevelShadowOverlay.cs` (2); `Shuttles/UI/ShuttleNavControl.xaml.cs` (5 Planets + 1 PlanetCracker);
`Shuttles/UI/NavScreen.xaml` + `.xaml.cs` (orbit button); `_Mono/Audio/AudioEchoSystem.cs` (NaN guards);
`_FarHorizons/StarSystem/PlanetOverlay.cs` (made public).
Resources: `ConfigPresets/Build/development.toml`; `Prototypes/_FarHorizons/Space/systems.yml` (Carcinoma);
`Prototypes/_Mono/Entities/Mobs/Chimera/biomass.yml`; `Prototypes/_NF/Loadouts/contractor_loadout_groups.yml` (timepiece);
`Prototypes/Entities/Structures/Machines/lathe.yml` (kits, parachutes).
Partials (no upstream line): `BiomeSystem.Wolfgate.cs`, `CEZLevelsSystem.Wolfgate.cs`, `Flight/CEZLevelsSystem.WF*.cs`,
`Flight/ThrusterSystem.WF*.cs`, `Flight/MoverController.WFAtmosphere.cs`, `ShuttleSystem.WFOrbit.cs`,
`StarSystemMapSystem.Wolfgate.cs`, `SharedShuttleSystem.Wolfgate.cs`, client `ShuttleNavControl.Terrain.cs`,
`ContentAudioSystem.Planets.cs`.

## Implications for Outposts

1. **Set-size planets are new work.** Terrain streams without limit around every player and viewer, and orbit/descent XY
   can be anywhere within 2 km of the body. Bounding needs a chunk filter (upstream-private: marked edit or a
   pre-generate-then-`SetEnabled(false)` approach), an edge treatment, orbit/descent clamping and a radar bound.
2. **Outposts should be their own grid, not tiles on the ground map.** Anything built on the ground map grid is "ground":
   no separate entity to save, and `WfClearLandingObstacles` plus skids break and delete anchored `Impassable` ground
   entities (walls) under or next to a landing or crashing hull. A separate grid is a "hull" to every planet system.
3. **Landing on an outpost grid destroys it.** `TryExitTransit` calls `Smimsh(..., explodeGrids: true)` on landing: both
   grids explode and everything under the hull is deleted or gibbed, whether it sits on an outpost grid or on the ground
   map. Landing pads and the "check the area for non-planetary turfs" load check must block descent over outposts (e.g. a
   veto before CE transit exits, or a pad that docks instead of landing). No pad or landing-zone concept exists yet.
4. **Anchoring already exists.** `ForceAnchorComponent` (`_NF`) blocks console FTL, blocks liftoff (`WfCanLiftoff`), counts
   as orbit station keeping, stops the gravity well and survives Carcinoma release; its MapInit disables the shuttle and adds
   `PreventGridAnchorChangesComponent`. `WFLiftoffAttemptEvent` is the clean veto for an "outpost console" rule.
   `WFDetachedTerrainComponent` is the alternative if an outpost should count as ground (but then it is exempt from
   biomass, Carcinoma, drag and structural crash).
5. **Carcinoma infests any grid resting on its ground**, including outposts: it pins it Static and converts walls to meat.
   Outposts there need an exemption (`IsLandedHere` checks separate non-map grids only).
6. **Night and time are queryable** via `WFPlanetEnvironmentComponent.IsNight` / `MinuteOfDay` on every layer map (1 Hz),
   or `WFPlanetWeatherSystem.TryGetReport`. Raids "at night" and the time/weather console can read these; the timepiece
   report is a ready template. Current weather is cosmetic apart from thunder shocks on unroofed tiles.
7. **No raid or hostile-NPC scheduler exists.** Fauna is ambient, capped (32/planet, 128 total), spawns only 24-64 tiles from
   players, retires when unobserved, and its site registry is in memory. Raids, burrows and farming need new systems; captured
   fauna are already protected from retirement once reparented or damaged.
8. **Supply drops can use parachutes.** Spawn the crate on the lowest air layer (which has gravity) above the UTH with
   `WFParachutedComponent`; on orbit it would need a z-velocity push. The pack is left at the drop site.
9. **Crash-landing spawn can reuse orbit decay.** A hull on the orbit layer with no running linear thrusters gets a PA
   countdown (60 s grace after a 10 s settle) and is dropped through `TryDropFromOrbit`; lift-lost callouts, skid, breakup
   and crew maiming then run as built. A console lock for the sequence does not exist. The design's "3 minutes" would need
   a longer `Grace` or a scripted `TryDropFromOrbit`. The PA already speaks through `ShipPaSystem.Announce`.
10. **Atmospheric jetpacks need an exemption** in `SharedJetpackSystem.WfInAtmosphere` (marked upstream shared code), and a
    gravity rule would have to read `WFPlanetLayerComponent.Gravity`. Today planet gravity only affects ship lift.
11. **Nothing planet-side persists.** All WF planet components are `[UnsavedComponent]`, networks are rebuilt each round from
    fixed seeds, and `BiomeComponent.ModifiedTiles` (the record of ground edits) is lost. Outpost saving must serialize
    its own grid and re-place it with `ReserveTiles` (as `GroundGrid` does) on a freshly built network. `GroundGrid` is the
    existing precedent for loading a grid onto a planet: it loads at the centre before MapInit, so upstream `OnBiomeMapInit`
    disables it as a shuttle.
12. **Where to hook POIs and outposts:** `WFPlanetGroundSpawnedEvent` (before generation, for marker layers), then the
    network's `WFPlanetNetworkComponent.GroundMap/Centre`. Radar will not show separate grids on the ground from orbit;
    POI beacons need a radar or IFF path (the `WFOrbitBeacon` pattern is a `WarpPoint` with `IFF` on the orbit map).
