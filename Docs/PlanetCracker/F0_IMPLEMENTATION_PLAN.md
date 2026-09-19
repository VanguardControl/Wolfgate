# F0 implementation plan (architect output, revision 2, 2026-09-13)

Produced by the F0 design workflow after two adversarial review passes. Sections A-Z are the architect's plan; FILES / UPSTREAM HOOKS / STAGES / TESTS / OPEN RISKS are its structured output. Orchestrator amendment: every planet-network layer also carries a shared `WFPlanetLayerComponent` marker so the outbound FTL gate only applies to planet networks, never to a future station z-network.

## F0 — Sector planets get an orbit layer and a surface (revision 2)

**Scope:** F0 only. No cracker, anchors, chunk, projectors, centrifuge, survey console or deep veins.

**Revision 2 folds in all seven reviewer issues.** Every one verified against the worktree and accepted; none rejected. Two of the seven (the prototype-kind blocker) were the same defect reported twice. In two cases I verified the defect but chose a different fix from the one proposed — reasons and evidence in section Z.

---

## A. What the design document gets wrong after PR 4690, and what this plan does instead

| Design doc claim | Reality in this worktree | What this plan does |
|---|---|---|
| §4/F0 "The orbit layer gets `CEZOrbitLayerComponent` and `UpdateGridGravity` skips grids parked there **the same way it skips the ground layer**" | There is no ground-layer skip to mirror. `UpdateGridGravity` (`Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.Gravity.cs:48`) skips only: grace window (`:85`), non-z-map (`:90`), `BodyType.Static` (`:93`), and `RigidSetHasSupport` (`:99`). Support is pooled gravgen lift over the rigid set **or** `HasGroundUnderFootprint` (`:624`), which reads non-empty tiles of the *map entity's own* `MapGridComponent`. The comment at `Gravity.cs:96-98` says so explicitly. | Writes the exemption from scratch as a new `continue` in the fall-gate loop, inserted at `Gravity.cs:92` between the z-map check and the Static check. Component named `WFOrbitLayerComponent` (not `CEZOrbitLayerComponent`, because it lives in `_WF`, not `_CE`). |
| §4/F0 stack diagram labels depth 0 "GROUND LAYER … (`CEZGroundLayerComponent`)", implying the component makes the layer solid | `CEZGroundLayerComponent` (`Content.Shared/_CE/ZLevels/Core/Components/CEZGroundLayerComponent.cs`) is purely presentational per its own doc comment; its only consumers are `Content.Client/_CE/ZLevels/Core/ScalingViewport.CEZLevels.cs:172` and `:193`. | The ground layer is solid because `BiomeSystem.EnsurePlanet` puts a `MapGridComponent` **on the map entity itself** (`Content.Server/Parallax/BiomeSystem.PlanetSetup.cs:30`) and the biome writes tiles into it. `CEZGroundLayer` is added anyway, purely so the client stops rendering below the surface. |
| §4/F0 "all added with `TryAddMapsIntoNetwork`" | `CreateMapNetwork(ComponentRegistry?)` (`CEZLevelsSystem.Maps.cs:20`) must come first and owns the shared registry; `InitializeZNetwork` (`CEZLevelsSystem.cs:108`) map-inits the stack; `QuickApiCache` (`Maps.cs:126`) only takes its first-element branch at depth 0, so **depth 0 must be added first and depths must expand contiguously**. `TryAddMapsIntoNetwork` also writes the entry even when it logs an error and returns false (`Maps.cs:47-60`). | Builder adds **one depth per call, ascending from 0**, and checks the return value of every call, unwinding on failure. |
| §4/F0 implies the shared component set is a per-network convenience | The same registry is stamped on every member map at MapInit with `removeExisting` defaulted to **true** (`CEZLevelMappingSystem.cs:50`, `EntityManager.Components.cs:188`), so anything in the registry *overwrites* what `EnsurePlanet` put on the ground map. On transit maps it is stamped with `removeExisting: false` instead (`Transit.cs:832`). | Network registry carries **only** a breathable `MapAtmosphere` (so air layers and transit gaps are breathable). Ground atmosphere and orbit vacuum are re-applied explicitly **after** `InitializeZNetwork`, last-write-wins. `MapLight`/`Roof` are never in the registry. The ground mixture is given a **distinct temperature** from the registry mixture so the ordering is actually observable — see A.3. |
| §4/F0 "The orbit layer gets an `FTLDestination`. A ship within range of the sector planet sees *Enter orbit*" | `FTLDestinationComponent` (`Content.Shared/Shuttles/Components/FTLDestinationComponent.cs:7`) has only `Whitelist`, `Enabled`, `BeaconsOnly`, `RequireCoordinateDisk`. **There is no per-destination range concept anywhere.** The only range logic is `FTLFree`'s raw cross-map drive-range test (`SharedShuttleSystem.cs:245`). | Range gating is written from scratch as a guard in `SharedShuttleSystem.CanFTLTo` (`Content.Shared/Shuttles/Systems/SharedShuttleSystem.cs:44`), called by exactly the two places that matter (client list `MapScreen.xaml.cs:332`, server jump `ShuttleConsoleSystem.FTL.cs:152`). |
| §0 "Only the round-start station builds a network today" | No prototype in `Resources/` carries `CEStationZLevelsComponent`, so `CEZLevelsSystem.OnStationPostInit` (`CEZLevelsSystem.cs:39`) never fires. Nothing builds a z-network at round start. The only live builders are the `znetwork-*` admin commands. | F0's builder is the first production network builder in the fork. It deliberately does **not** go through the station path (which would also hand terrain layers an `FTLDestination` via `ShuttleSystem.OnStationPostInit`, `ShuttleSystem.FasterThanLight.cs:137`). |
| §0 "Piloted ascend/descend … Landed ships get thrusters disabled" | `CEZLevelsSystem.GroundFriction.cs:34-37` states explicitly that nothing parks a grid or changes its body type; there is no landed state. | Irrelevant to F0's build, but it is *why* the outbound FTL gate in A.2 is needed: a hull resting on a surface layer is still Dynamic and FTL-capable. Also note `UpdateFTLArriving` (`ShuttleSystem.FasterThanLight.cs:894`) calls `Disable` when the arrival map has a `MapGridComponent` — which is why **the orbit layer must never have a `MapGridComponent`**. |
| §4/F0 "ascending back ends in `TryExitTransit` onto the orbit layer" | True only if the orbit map is a real `CEZMapComponent` network member (`Transit.cs:785-789`). Also `ConvoyBlockedByPlane` (`Transit.cs:645`) refuses a climb if the plane above has terrain across the footprint. | The orbit map is a full network member at the top depth and is kept terrain-free, so climbs into it and descents out of it both succeed. |
| §4/F0 "a stack of ordinary maps … Grids move between layers … through a temporary transit map" | PZN made transit **convoy**-shaped: one transit map per z-grid-network layer, linked by `TransitAbove`/`TransitBelow` with one `ConvoyLead` (`CEZTransitMapComponent.cs:44/:50/:58`). Also PR 4690 added an entirely separate *grid* network concept (`CEZGridConnectorComponent` → `CEZGridNetworkComponent`) rebuilt every dirty tick by `CEZGridConnectorSystem`. | F0 touches neither. It only builds a *map* network. Noted so F4–F6 are re-read against convoys. |
| §9 assumes a z-level test fixture exists | `Content.IntegrationTests` has zero z-level tests. | The fixture is built from nothing in Stage 5. |
| §4/F1 cites `GridHasActiveGravgen` | No such symbol exists. | Out of F0 scope; flagged so F1 does not inherit it. |

### A.1 Biome generation is driven by **view subscribers**, not by attached players — and a z-eye is one

This corrects a claim the previous revision of this plan made in B.4 ("chunks load around players only") and its stated round cost.

`BiomeSystem.ProcessPlayerChunkRequests` (`Content.Server/Parallax/BiomeSystem.PlayerTracker.cs:22-62`) runs two branches per session: the attached entity (`:25-40`) **and then every entry of `pSession.ViewSubscriptions`** (`:42-61`). The viewer branch calls `AddChunksInRange` *and* `AddMarkerChunksInRange` for every `biome.MarkerLayers` entry, at the viewer's own world position. `CanLoad` (`PlayerTracker.cs:65-68`) excludes only ghosts (`!_ghostQuery.HasComp(uid) || _tags.HasTag(uid, AllowBiomeLoadingTag)`, tag id at `BiomeSystem.cs:51`).

`CEZLevelsSystem.UpdateViewer` (`Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.View.cs:147`) spawns one `CEZLevelEye` per map below the viewer, up to `MaxZLevelsBelowRendering = 10` (`Content.Shared/_CE/ZLevels/Core/EntitySystems/CESharedZLevelsSystem.Constants.cs:10`), at `new EntityCoordinates(targetMap, globalPos)` — the viewer's own world XY (`View.cs:206`) — and `_viewSubscriber.AddViewSubscriber(newEye, actor.PlayerSession)` (`View.cs:210`). `Resources/Prototypes/_CE/Entities/zEye.yml` is four lines and declares **no components at all**, so no `GhostComponent` and `CanLoad` returns true.

Consequence: a ship parked on the orbit layer (depth 4) keeps eyes on depths 3, 2, 1 and **0 — the biome map** — and each of those eyes seeds terrain chunks *and every marker layer* on the surface directly beneath it, with nobody on the surface.

The accumulation half is worse than the generation half. `BiomeComponent.LoadedMarkers` (`Content.Shared/Parallax/Biomes/BiomeComponent.cs:75`) is `Dictionary<string, HashSet<Vector2i>>` — chunk indices only, no entity list — whereas template-layer entities are tracked in `LoadedEntities` (`:55`) and deleted by `UnloadEntities` (`BiomeSystem.ChunkLoader.cs:233-274`). **Marker-spawned entities are never unloaded.** For the `entityMask` ore layers that is bounded (they substitute for `WallRock*` entities that already exist, one pass per marker chunk, never repeated). For the mob layers it is unbounded: `Lizards` spawns `MobLizard` in groups of 3-5 (`Resources/Prototypes/Procedural/biome_markers.yml:2-5`) and `Carps` spawns `MobCarpDungeon` (`:31-33`), permanently, under any orbiting or mid-air ship.

**Fix taken:** `WFAsclepiuSurface` ships **no mob marker layers in F0**. `biomeMarkerLayers` is the eight `entityMask` ore layers only; `Lizards` and `Carps` are removed. Hostile fauna return in a later feature together with an explicit gate (see Z.1 for the 2-line `BiomeSystem` guard that is available and why F0 does not spend it).

**Corrected round cost:** one biome map plus four bare maps, *plus* biome terrain generation over a 16-tile-radius load area (`_loadArea`, `BiomeSystem.cs:65`, `DefaultLoadRange = 16f` at `:49`) on the ground layer beneath every player anywhere in the Asclepiu stack, plus a one-shot ore-marker pass per marker chunk so traversed. Steady-state marker cost is zero because `LoadedMarkers` suppresses regeneration.

### A.2 Orbit gating must be two-directional

The previous revision only guarded the FTL *destination*. Verified: `CanFTLTo` (`SharedShuttleSystem.cs:44-52`) keyed solely on the target; the sector map is an enabled, non-disk, non-beacons-only destination for every station grid's map (`ShuttleSystem.FasterThanLight.cs:137`, `TryAddFTLDestination(gridXform.MapID, true, false, false, out _)`); nothing parks or disables a hull resting on a layer (`GroundFriction.cs:34-37`); and because every layer shares the sector planet's world XY, `FTLFree`'s raw cross-map distance test (`SharedShuttleSystem.cs:243-247`) is ~0 from any layer. So a ship sitting on the Asclepiu ground, an air layer or the cloud layer could FTL straight to the sector map, skipping the climb entirely — the exact inverse of the fixed decision that ascent/descent is the `DescendZ` pilot action.

**Fix taken:** the single shared helper now gates both ends. `WfAllowFTL(shuttleUid, targetMapUid)` returns false when the shuttle's current map is a z-network member (`CEZMapComponent`, `Content.Shared/_CE/ZLevels/Core/Components/CEZMapComponent.cs:13`, networked) that is **not** an orbit layer, or is a transit map (`CEZTransitMapComponent.cs:13`, also shared and networked — transit maps get no `CEZMapComponent`, see `CreateTransitMap`, `Transit.cs:820-845`), and false when the target is an orbit layer out of range. Still one two-line insert in `CanFTLTo`, still after the same-map early-out so a hull on a layer can always target its own map, still predicted identically on both sides because every component it reads is networked. The orbit layer becomes the only FTL door in and out of a planet network.

### A.3 The registry-overwrite test was vacuous

Verified: `Resources/Prototypes/_CE/ZLevels/zmaps.yml:10-21` is the source both mixtures were copied from (volume 2500, immutable, 293.15 K, moles 21.824879 / 82.10312). With the network registry mixture and `WFAsclepiuSurface.atmosphere` byte-identical, `AtmosphereIsPerLayer`'s ground assertion passes whether or not the post-init `SetMapAtmosphere` fixup ran — deleting the fixup would leave every test green, while the fixup is the entire stated reason `MapLight` and `CEZLevelRoof` are kept out of the registry.

**Fix taken:** `WFAsclepiuSurface.atmosphere.temperature` becomes **288.15** (15 °C, a temperate habitable world — justified on its own terms) while the network registry keeps **293.15**. The test asserts ground temperature, not just moles. `MapAtmosphere` **stays in the registry**: `CreateTransitMap` (`Transit.cs:831-832`) copies only the network registry onto a transit map, so removing it there would make every transit gap vacuum.

---

## B. Architecture

### B.1 Data flow

```
Resources/Prototypes/_WF/PlanetCracker/planets.yml
  - type: wfPlanetSurface                 WFPlanetSurfacePrototype, [Prototype("wfPlanetSurface")]
      id: WFSurfaceAsclepiu
      planetType: PlanetAsclepiu  ────────────┐  (ProtoId<PlanetTypePrototype>)
      ground: WFAsclepiuSurface  ─────┐       │
      ...                             │       │
                                      │       │   Resources/Prototypes/_FarHorizons/Space/systems.yml
  - type: planet                      │       └──>  SystemKyphrus.planets[2].planet == PlanetAsclepiu
      id: WFAsclepiuSurface  <────────┘                (distance 12800, angle 2.4)
      biome: WFBiomeAsclepiu


round start (preset carries StarSystemKyphrus)
  StarSystemRuleSystem.Started (StarSystemRuleSystem.cs:11)
    -> StarSystemMapSystem.SetSystem (StarSystemMapSystem.cs:34)
         -> SpawnEntities (:45) spawns one PlanetEntity per Planet helper
              // WOLFGATE hook at :66
              -> WfPlanetSpawned(map, planetEntity, planet)          [_WF partial]
                   resolve StarSystemPlanet entry by exact position match
                   -> entry.Planet (ProtoId<PlanetTypePrototype>)
                   -> lookup wfPlanetSurface whose planetType == that id
                   -> AddComp<WFSectorPlanetComponent>{Surface}       <- THE REGISTRY
                   -> if CVar on && surface.BuildAtRoundStart: TryBuildNetwork()

WFPlanetNetworkSystem.TryBuildNetwork
  centre = _transform.GetWorldPosition(planetEntity)        <- WORLD, not LocalPosition
  depth 0 ground  = PlanetSystem.SpawnPlanet(surface.Ground, runMapInit: false)   [+ optional grid at centre]
  depth 1..N air  = MapSystem.CreateMap(runMapInit: false)
  depth N+1 cloud = MapSystem.CreateMap(runMapInit: false)   + CEZCloudLayer
  depth top orbit = MapSystem.CreateMap(runMapInit: false)
  network = CEZLevelsSystem.CreateMapNetwork(surface.NetworkComponents)
  for depth ascending from 0: TryAddMapsIntoNetwork(network, {layer: depth})
  CEZLevelsSystem.InitializeZNetwork(network)          <- registry lands here, at MapInit
  post-init fixups (last write wins)
  orbit: WFOrbitLayerComponent{Planet, Range} + FTLDestination + WFOrbitBeacon at centre
```

### B.2 Where the surface definition attaches to a sector planet

**Decision: on a new `_WF` prototype, keyed by `PlanetTypePrototype` id — not by a new field on `StarSystemPlanet`, and not by a component on `PlanetEntity`.**

Rationale, from the code:
- `Planet` (`Content.Shared/_FarHorizons/StarSystem/Helpers/Planet.cs:8`) carries no prototype id at all — only a display `Name` copied from `PlanetTypePrototype.Name` (`:31`). `SpawnEntities` iterates that helper list, so the spawned entity has no back-reference to any id.
- `StarSystemPlanet` (`StarSystemPrototype.cs:15`) is `sealed partial`, so a `_WF` partial could legally add a `[DataField]`. I deliberately did **not** do this: the field would then also have to be threaded through `Planet`'s constructor (`Planet.cs:28`, upstream) to reach `SpawnEntities`, which is a second upstream edit for no gain.
- Keying on `PlanetTypePrototype` works because `systems.yml` already uses the *type* id as a per-body id (`PlanetAsclepiu`, `PlanetFervidus`, …). If two systems ever place the same type and want different surfaces, add a `starSystem` filter field to `WFPlanetSurfacePrototype` then; the resolution helper is one method.

The bridge from spawned entity → type id is done inside `_WF`, by matching the spawned body's `Planet.Position` against the entries of `StarSystemPrototype.Planets` recomputed with the identical expression from `SharedStarSystemMapSystem.BuildPlanetarySystem:26` (`Vector2(cos(angle), sin(angle)) * distance`). The float maths is bit-identical, so exact equality holds; the helper falls back to a name comparison and logs if neither matches.

**The prototype kind string is declared explicitly.** `[Prototype("wfPlanetSurface")]`, never bare `[Prototype]`. Robust's auto-derivation (`RobustToolbox/Robust.Shared/Prototypes/PrototypeUtility.cs:15-22`, used at `PrototypeManager.cs:1046`) lowercases **only index 0** and strips the `Prototype` suffix, so `WFPlanetSurfacePrototype` would register as `wFPlanetSurface` — capital F — and every `- type: wfPlanetSurface` document would fail to resolve to a kind at load. The `_CE` prototypes with the same acronym shape already do this for the same reason: `Content.Shared/_CE/ZLevels/Mapping/Prototypes/CEZLevelMapPrototype.cs:11` is `[Prototype("zMap")]` and `CEZGridStackPrototype.cs:22` is `[Prototype("cezGridStack")]`. Bare `[Prototype]` is only safe for single-capital names, as in `Content.Shared/_DV/Planet/PlanetPrototype.cs:8` → `planet` and `Content.Shared/_WF/Genitals/Prototypes/GenitalPresetPrototype.cs:7` → `genitalPreset`.

### B.3 When the network is built, and by whom

Three entry points, in order of how the feature is normally exercised:

1. **Round start, eagerly, per planet with `buildAtRoundStart: true`.** Driven by the `// WOLFGATE` hook in `SpawnEntities`, gated on `wf.planet_networks`. Only `WFSurfaceAsclepiu` sets it true.
2. **Admin command `wfplanet build <name>`** — the on-demand builder required by the decisions, and the only way to exercise the feature in the default dev environment (see B.7).
3. **Integration tests** call `WFPlanetNetworkSystem.BuildNetwork` directly.

`centre` is **`_transform.GetWorldPosition(planetEntity)`**, never `Transform(planetEntity).LocalPosition`. `SpawnEntities` creates the body with `SpawnAtPosition(planetEnt.ID, planetCoords)` (`StarSystemMapSystem.cs:62-63`), which runs grid traversal, so the parent is not guaranteed to remain the map entity — a grid overlapping the planet marker reparents it and `LocalPosition` silently becomes grid-local. The orbit beacon coordinate, `WFPlanetNetworkComponent.Centre` and the shared range gate (which compares `XformSystem.GetWorldPosition` of planet and shuttle) must all measure in the same frame or the beacon lands thousands of tiles from where the gate measures.

**Lazy build on approach is explicitly deferred.** The design doc's "on first approach" needs a server-side proximity watcher over shuttle consoles, and the `CanFTLTo` gate is shared/predicted so it cannot build anything. That watcher belongs with F2's survey console. For F0 the eager/commanded build is both smaller and more testable, and since the orbit map must already exist for the destination to appear in the list at all, lazy building would only save memory on planets nobody visits.

### B.4 Exactly what components each map gets

Layer roles for `WFSurfaceAsclepiu` (`airLayers: 2`, `cloudLayer: true`): depth 0 ground, 1 air, 2 air, 3 cloud, 4 orbit.

**Network-wide `ComponentRegistry`** (`CEZMapNetworkComponent.Components`, stamped on every member map by `CEZLevelMappingSystem` with `removeExisting` defaulted true, and on every transit map by `CreateTransitMap:832` with `removeExisting: false`) contains **only**:

```yaml
- type: MapAtmosphere            # breathable, 293.15 K - deliberately NOT the ground mixture
  space: False
  mixture: { volume: 2500, immutable: True, temperature: 293.15, moles: [21.824879, 82.10312] }
```

Nothing else. `MapLight`, `CEZLevelRoof` and `Roof` are deliberately kept out, because `AddComponents` defaults to `removeExisting: true` and would otherwise clobber what `EnsurePlanet` put on the ground map. Putting only atmosphere in the registry means the transit gaps between air layers are breathable, which is the one thing the registry is genuinely needed for.

**Depth 0 — ground layer.** Built by `PlanetSystem.SpawnPlanet(surface.Ground, runMapInit: false)`, which routes through `BiomeSystem.EnsurePlanet` (`Content.Server/Parallax/BiomeSystem.PlanetSetup.cs:25`) and therefore already carries:

| Component | Source | Why it matters here |
|---|---|---|
| `MapGridComponent` on the map entity | `PlanetSetup.cs:30` | **This is what makes the layer solid.** `HasGroundUnderFootprint` (`Gravity.cs:624`) requires `TryComp<MapGridComponent>(mapUid)` and then any non-empty tile under the grid's world AABB. |
| `PlanetMapComponent` | `:31` | `Parallax` id and exclusion from grid garbage cleanup. |
| `BiomeComponent` | `:32-37` | Seed + `WFBiomeAsclepiu` template. Chunks load around **every view subscriber**, z-eyes included — see A.1. |
| `ParallaxComponent` | `:39-41` | From `PlanetMapComponent.Parallax`. |
| `GravityComponent{Enabled, Inherent}` | `:43-46` | Walkable surface. |
| `MapLightComponent` | `:53-55` | From `PlanetPrototype.MapLight`. |
| `RoofComponent`, `LightCycleComponent`, `SunShadowComponent`, `SunShadowCycleComponent` | `:57-62` | Day/night and shadows. |
| `MapAtmosphereComponent` | `:64-70`, then overwritten by `SpawnPlanet:44` from `PlanetPrototype.Atmosphere` | Breathable surface, **288.15 K**. |

Then, added by the builder **after** `InitializeZNetwork`:
- `_atmos.SetMapAtmosphere(ground, false, groundProto.Atmosphere)` — re-applied because the network registry's 293.15 K `MapAtmosphere` overwrote it at MapInit. The 5 K difference is what makes this observable in test 2.
- `CEZLevelRoofComponent` — mirrors `zmaps.yml:19`; the ground layer is the one layer that is a mapgrid, so it is the one that wants automatic roof-tile management.
- `CEZGroundLayerComponent` — **presentational only** (`ScalingViewport.CEZLevels.cs:172/:193`). Without it, a player in orbit renders ten levels of planet.
- `surface.GroundComponents` with `removeExisting: false`.
- `FTLDestinationComponent` is **never** added to the ground layer. Unlike Monolith's `DesertWorld` (`permanent_planet.yml:6-7`), `WFAsclepiuSurface.addedComponents` must be empty, because `UpdateFTLArriving:894` calls `_shuttle.Disable` when the arrival map has a `MapGridComponent`.

**Depths 1..N — air layers.** Bare `MapSystem.CreateMap` maps. **No `MapGridComponent`** — that is what makes them fall-through. They receive the network registry's breathable `MapAtmosphere` and `surface.AirComponents` (`MapLight { ambientLightColor: "#c8fdff" }` from `zmaps.yml:20-21`, plus `Gravity { enabled: true, inherent: true }` so a person who falls out of a hull is not weightless mid-air). No `CEZLevelRoof` (no mapgrid to roof), no `Parallax`.

**Depth N+1 — cloud layer.** Bare map + `CEZCloudLayerComponent` (`Content.Shared/_CE/ZLevels/Core/Components/CEZCloudLayerComponent.cs:14`) + the same air components. Client occluder with a dissolve band. No `MapGridComponent`.

**Depth top — orbit layer.** Bare map, defined largely by what it does *not* have:

| Given | Withheld | Reason |
|---|---|---|
| `WFOrbitLayerComponent { Planet, Range, Network }` — networked | | Drives the fall exemption (server), and both halves of the FTL gate (shared, so the client agrees). |
| `MapAtmosphereComponent` via `_atmos.SetMapAtmosphere(orbit, true, GasMixture.SpaceGas)` after init | | Overrides the registry's breathable air. `GasMixture.SpaceGas` is `Content.Shared/Atmos/GasMixture.cs:19`. |
| `FTLDestinationComponent` via `_shuttle.TryAddFTLDestination(orbitMapId, true, false, false, out _)` — the **4-arg** overload at `ShuttleSystem.FasterThanLight.cs:209`; the 2-arg one at `:204` defaults `requireDisk` to **true** | | Non-beacons-only so free-position clicks work and two ships can enter orbit without `FTLFree`'s buffer test deadlocking on one marker. |
| Map entity name `Loc.GetString("wf-planet-orbit-map-name", ("planet", name))` → "Asclepiu orbit" | | `MapScreen.xaml.cs:336` uses the map entity's name verbatim as the destination-tree heading. |
| One `WFOrbitBeacon` entity at `centre` (world frame) | | Gives the heading a named child entry and a radar-centre target. |
| | **No `MapGridComponent`** | Or `UpdateFTLArriving:894` disables every arriving hull, and `ConvoyBlockedByPlane` (`Transit.cs:645`) would block climbs into orbit and descents out of it. |
| | **No `MapLightComponent`** | Orbit is dark; only grid lights. Also keeps `CEZLevelShadowOverlay` (`CEZLevelShadowOverlay.cs:64`) from casting surface shadows in vacuum. |
| | **No `CEZLevelRoof`, no `CEZGroundLayer`, no `PlanetMap`, no terrain** | Roof needs a mapgrid; ground-layer marker would hide the planet from anyone in orbit. |
| | **No `GravityComponent`** | Vacuum. Vertical flight's gravgen requirement is checked on the *grid*, not the map (`PilotControl.cs:190`), so this does not block `DescendZ`. |

**PVS.** `CreateMapNetwork` already `EnsureComp<CEPvsOverrideComponent>`s the network entity (`Maps.cs:25`), map entities are always networked, `StarSystemMapSystem` global-overrides every `PlanetEntity` (`:65`), and `View.cs:170-201` spawns one `CEZLevelEye` per visible level with PVS scaled by `ZLevelViewShrink`. A five-layer planet costs up to five eyes per player on that network — budgeted, not fixed, and see A.1 for what those eyes do to the biome.

### B.5 The orbit gravity exemption — exact hook

**File:** `Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.Gravity.cs`
**Insertion point:** line **92**, immediately after the z-map guard's `continue;` on line 91 and before the `BodyType.Static` guard on line 93.

```csharp
90                if (xform.MapUid is not { } mapUid || !_zMapQuery.HasComp(mapUid))
91                    continue;
92
92+               if (WfIsOrbitLayer(mapUid)) // WOLFGATE: grids parked on a planet orbit layer never fall.
93+                   continue;
94+
93                if (_physQuery.TryComp(uid, out var body) && body.BodyType == BodyType.Static)
```

Two lines of code. `WfIsOrbitLayer` is a one-line private helper in a `_WF` partial of `CEZLevelsSystem` (which is `public sealed partial class`, `CEZLevelsSystem.cs:18`), so the upstream file needs no new `using` and no new query field.

Why *there*:
- After the z-map guard, so `mapUid` is already non-null and known to be a network member.
- Before `RigidSetHasSupport` (`:99`), so it costs one `HasComp` and skips the rigid-set flood entirely for parked ships.
- **Not** inside `SetHasSupport` (`:545`). Putting it there would make an orbit member hold up docked or z-grid-network partners hanging on *other* layers — a semantic change to the rigid-set rule that F0 should not decide.
- It does not affect descent: `UpdateTakeoffSpool` → `TryEnterTransit` (`PilotControl.cs:229`) never consults the fall gate, and `IntegrateFallingGrid` (`Gravity.cs:264`) drives grids already in transit from a separate query at `:116-140`.
- Giving the orbit map terrain instead would be wrong twice over: `HasGroundUnderFootprint` also backs `ConvoyBlockedByPlane`, so tiles on the orbit plane would block ships climbing to it *and* descending from it.
- The `BodyType.Static` skip at `:93` already covers anything holding `ForceAnchorComponent`, so the exemption is specifically what lets an *ordinary visiting ship* park in orbit with a cold gravgen.

Why it is load-bearing rather than a nicety: `RefreshGridZPhysics` (`Transit.cs:77-98`) adds `CEZPhysicsComponent` + `CEZGridFallerComponent` with `GravityTime = now + 3s` to *any* grid re-parented onto a `CEZMapComponent` map. Without the exemption, entering orbit is a three-second countdown to a plummet.

### B.6 FTL — both jumps, both directions

**One shared helper, one two-line upstream insert.** `WfAllowFTL(EntityUid shuttleUid, EntityUid targetMapUid)` lives in `Content.Shared/_WF/PlanetCracker/Planets/SharedShuttleSystem.Wolfgate.cs` and answers three questions in order:

1. **Outbound.** If the shuttle's current map has `CEZTransitMapComponent`, return false — FTL launched mid-transit orphans the transit map (`CleanupOrphanedTransitMaps`, `Transit.cs:616`) and leaves sibling convoy layers pointing at a dead `ConvoyLead`.
2. **Outbound.** If the shuttle's current map has `CEZMapComponent` and does **not** have `WFOrbitLayerComponent`, return false — you leave a planet by climbing to orbit, not by jumping off the grass. Verified necessary: nothing parks a landed hull (`GroundFriction.cs:34-37`), the sector map is an open destination (`ShuttleSystem.FasterThanLight.cs:137`), and `FTLFree`'s distance test (`SharedShuttleSystem.cs:243-247`) is ~0 from any layer because layers share the planet's world XY.
3. **Inbound.** If the target map has `WFOrbitLayerComponent`, resolve `Planet` (a `NetEntity`; the `PlanetEntity` is PVS-global-overridden so the client has it), require the shuttle to be on the planet's own map, and require world distance ≤ `Range` (default **2000**, inside every shipped drive: 5000 / 10000 / 25000, `_Mono/Entities/Structures/Machines/ftldrive.yml:24`, `:119`, `heavy_drive.yml:31`). Otherwise return true.

Every component it reads is shared and networked (`CEZMapComponent.cs:13`, `CEZTransitMapComponent.cs:13`, and `WFOrbitLayerComponent` itself), so client and server compute the same answer.

**Location of the insert:** `SharedShuttleSystem.CanFTLTo` (`Content.Shared/Shuttles/Systems/SharedShuttleSystem.cs:44`), at line **52**, after the same-map early-out (`:49-50`) and before the `FTLDestinationComponent` lookup. This is the only function that knows shuttle + destination map + console, and its two call sites are exactly the client list filter (`Content.Client/Shuttles/UI/MapScreen.xaml.cs:332`) and the server jump choke point (`Content.Server/Shuttles/Systems/ShuttleConsoleSystem.FTL.cs:152`), so what the player sees and what the server allows cannot diverge. Placing it after the same-map early-out means a hull sitting on a layer can still target its own map. `ConsoleFTLAttemptEvent` (`Content.Server/Shuttles/Events/ConsoleFTLAttemptEvent.cs:9`) is *not* usable for this — it carries only `(Uid, Cancelled, Reason)`, is raised from `CanFTL` before any destination is known, and changing its shape would break `ForceAnchorSystem.cs:26`, `SalvageSystem.Runner.cs:39` and `NukeopsRuleSystem.cs:312`.

**Enter orbit.**
- *Mechanism:* the orbit map is an ordinary `FTLDestination` (enabled, no disk, not beacons-only) whose map entity is named "Asclepiu orbit", carrying one `WFOrbitBeacon` (`WarpPoint` + `FTLSmashImmune` + `FTLBeacon` + `IFF`, copied from `PlanetEntity`, `star_system.yml:14-23`) at the orbit map's `centre`.
- *UI exposure:* zero new client code, zero new BUI messages. `MapScreen.RebuildMapObjects` (`MapScreen.xaml.cs:315`) enumerates every `MapComponent` entity the client knows, filters with `CanFTLTo` (`:332`), and makes a `Collapsible` heading from the map entity's name (`:336`); `ShuttleConsoleSystem.GetBeacons` (`ShuttleConsoleSystem.FTL.cs:93`) is a global unfiltered query so the beacon appears under that heading with no wiring (`MapScreen.xaml.cs:426`). The jump itself is the normal radar click (`ShuttleMapControl.xaml.cs:132`).
- *Arrival placement:* beacon FTL resolves the target as the beacon's world position (`ShuttleConsoleSystem.FTL.cs:73`); free-position FTL uses the clicked coordinates. Either way `ConsoleFTL:205` offsets by `-shuttlePhysics.LocalCenter` rotated into the player's scroll-wheel angle, and `UpdateFTLArriving:794` applies it verbatim. `FTLAntiCollisionSystem.OnFTLCompleted` (`FTLAntiCollisionSystem.cs:64`) then pushes the arrival ≥50 away from anything within 30, and `Smimsh` (`:870`) clears what is under the hull — which is why the beacon carries `FTLSmashImmune`.
- *Why the orbit map's origin is co-located with the sector planet:* `FTLFree` (`SharedShuttleSystem.cs:225`) compares raw world positions with **no map awareness** (`:245`), and `ClampCoordinatesToFTLRange` (`:287`) silently rewrites an out-of-range target while keeping the destination map — so a badly-placed orbit map does not fail the jump, it drops the ship at a garbage coordinate. Placing the orbit beacon at the planet's own sector **world** coordinates (`cos(2.4), sin(2.4)) * 12800` ≈ `(-9439, 8646)` for Asclepiu) makes both jumps ~zero distance, so the clamp never fires and any drive suffices. It also makes "leave orbit puts you next to the planet" fall out for free, and matches the z-code's own invariant that layers share world XY (`MoveGridSetToMap`, `Transit.cs:352/:365`).

**Leave orbit.**
- *Mechanism:* **nothing new.** The sector `PlanetEntity` already carries `FTLBeacon` and `FTLSmashImmune` (`star_system.yml:20-21`), and the sector map already has an enabled, non-disk, non-beacons-only `FTLDestination` from `ShuttleSystem.OnStationPostInit` (`ShuttleSystem.FasterThanLight.cs:137`). From the orbit layer the console's destination tree lists the sector map with the planet beacon named under it; clicking it lands the ship on the planet marker it departed from. From any other layer step 2 of `WfAllowFTL` refuses.
- *Arrival placement:* the `PlanetEntity`'s exact coordinates, `FTLSmashImmune` so the marker survives, then `FTLAntiCollisionSystem` separation.
- Deliberately **not** done: a second "departure" beacon offset from the planet, and making `TryGetFTLProximity` (`ShuttleSystem.FasterThanLight.cs:1141`, `private`) public.

**One extra server-side guard, kept.** `WFPlanetNetworkSystem` subscribes the broadcast `ConsoleFTLAttemptEvent` and cancels when `Transform(shuttle).MapUid` has `CEZTransitMapComponent`, with `args.Reason = Loc.GetString("wf-shuttle-console-in-transit")` — **localized**, matching `ForceAnchorSystem.cs:36` (`args.Reason = Loc.GetString("shuttle-console-force-anchored")`) and the sibling in-expedition path at `ShuttleSystem.FasterThanLight.cs:275`. `Reason` is returned raw as the out-param of `CanFTL` (`:283`) and is a human-readable string, not a LocId; both consumers currently drop it behind a `// TODO: Session popup` (`ShuttleConsoleSystem.FTL.cs:145-149`), which is exactly how a raw LocId would survive to whoever wires that popup up. This handler is redundant with step 1 of `WfAllowFTL` for console jumps, and kept because `CanFTL` is reachable from paths that never touch `CanFTLTo`. `ForceAnchorSystem.cs:20` is the precedent that this broadcast event takes multiple handlers; do **not** copy its `before: new[] { typeof(ShuttleSystem) }` ordering hint — `ShuttleSystem` raises this event (`:278`), it does not subscribe it.

### B.7 CVar

`Content.Shared/_WF/CCVar/PlanetCrackerCVars.cs`, following the `InternetSoundCVars.cs` pattern:

```csharp
/// <summary>Master switch for Wolfgate planet networks: orbit layers, surfaces and the orbit FTL jumps.</summary>
public static readonly CVarDef<bool> PlanetNetworks =
    CVarDef.Create("wf.planet_networks", false, CVar.SERVERONLY);
```

Two-segment name on purpose: `development.toml` maps `[wf] planet_networks = true`, and a three-segment name would need a nested TOML table. `SERVERONLY` is correct because the shared gate reads *components*, never the CVar — when the feature is off no orbit map and no planet network exists, so neither half of `WfAllowFTL` can fire.

The CVar gates registration and both build paths. The admin command refuses with a clear message when it is off rather than bypassing it.

**Dev environment.** `Resources/ConfigPresets/Build/development.toml` gets a `[wf] planet_networks = true` block. Note the trap that makes this insufficient on its own: `development.toml:3` sets `lobbyenabled = false`, which forces the preset to `"sandbox"` (`Content.Server/GameTicking/GameTicker.GamePreset.cs:109`), and `Resources/Prototypes/game_presets.yml:109-117` lists only the `Sandbox` rule — so `StarSystemKyphrus` never starts and **no `PlanetEntity` is spawned in dev**. No map under `Resources/Maps` sets `StarSystemMap` either. That is why the admin command carries `system` and `spawn` subcommands.

### B.8 Round-scoped planet registry

`WFSectorPlanetComponent` (Shared, networked, `UnsavedComponent`) on each spawned `PlanetEntity`:

```
ProtoId<WFPlanetSurfacePrototype>? Surface   // null = a body with no Wolfgate surface
NetEntity? Network                            // the CEZMapNetwork entity, once built
NetEntity? OrbitMap
```

The registry is the set of these components: `AllEntityQuery<WFSectorPlanetComponent>()`, wrapped by `WFPlanetRegistrySystem.GetPlanets()` / `TryGetPlanetByName(string)`. No manager entity, no `RoundRestartCleanupEvent` handler — it is round-scoped for free because the planet entities die with the sector map, and networked so F2's survey console can read it client-side without a bespoke BUI message.

### B.9 Subscription audit (Robust one-directed-subscription-per-pair rule)

| System | Subscription | Kind | Conflict? |
|---|---|---|---|
| `WFPlanetRegistrySystem` | `<WFSectorPlanetComponent, ComponentShutdown>` | directed | **None.** Grep of `Content.Server`, `Content.Shared`, `Content.Client`, `Resources/Prototypes` for `WFSectorPlanet` / `WFOrbitLayer` / `WFPlanet` / `PlanetCracker` returns zero hits outside `obj/`. The component is new in this change. |
| `WFPlanetNetworkSystem` | `<ConsoleFTLAttemptEvent>` | **broadcast** | Legal. Verified the full subscriber set with a repo grep: `ForceAnchorSystem.cs:20`, `SalvageSystem.Runner.cs:36`, `NukeopsRuleSystem.cs:66`. The duplicate-crash rule applies only to `(component, event)` directed pairs. |

No directed subscription is added on any upstream component. The plan deliberately avoids `<StarSystemMapComponent, MapInitEvent>` and `<CEZMapComponent, CEMapAddedIntoZNetworkEvent>` (the latter already taken by `CEZLevelMappingSystem.cs:21`) by using a direct call hook and post-init fixups instead.

### B.10 Sandbox safety

Everything in `Content.Shared/_WF/PlanetCracker/Planets/` is a prototype class, two components and one `SharedShuttleSystem` partial method. No `System.IO`, no `Process`, no `ConditionalWeakTable`, no reflection. `Content.Client` gets **no new files at all** — the gate rides in the shared partial and the destination UI is vanilla.

---

## C. The Asclepiu surface

Three prototype files plus one entity file, all under `Resources/Prototypes/_WF/PlanetCracker/`.

**`biomes.yml` — `WFBiomeAsclepiu`**, a `biomeTemplate` composed of three `BiomeMetaLayer`s, following the worked example at `Resources/Prototypes/_Mono/Planets/biome_ocean.yml:47-76`:

| Meta layer | `template` | Verified at | Threshold / noise |
|---|---|---|---|
| Continents | `Grasslands` | `Resources/Prototypes/Procedural/biome_templates.yml:74` | `-0.5`, frequency `0.001`, OpenSimplex2/FBm, octaves 2, lacunarity 2 |
| Seas | `MonoOcean` | `Resources/Prototypes/_Mono/Planets/biome_ocean.yml:2` | `0.25`, frequency `0.01` |
| Poles | `Snow` | `Resources/Prototypes/Procedural/biome_templates.yml:241` | `0.5`, frequency `0.001` |

`Grasslands` is the right base because it already plants `MonoPlanetmapOreBase` rock formations (`biome_templates.yml:105`) — an `entityTable` (`_Mono/Planets/ore.yml:162-180`) that rolls `WallRock` 0.67 plus `WallRockCoal/Tin/Quartz/Silver/Gold/Plasma/Uranium`. That gives baseline mining *and* supplies the `WallRock*` entities the `entityMask` ore marker layers need in order to fire at all. It also ships `FloraTree`/`FloraTreeLarge` (`:87-88`), `CEWater` ponds (`:122`), a `BiomeDummyLayer id: Loot` insertion point (`:106`) for F2's deep-vein `AddTemplate`, and `FloorPlanetDirt`/`FloorPlanetGrass` fill. Crucially, template-layer entities are tracked in `BiomeComponent.LoadedEntities` and deleted by `UnloadEntities` (`BiomeSystem.ChunkLoader.cs:233-274`), unlike marker-layer spawns.

**`planet_surfaces.yml` — `WFAsclepiuSurface`**, a DeltaV `- type: planet` (`Content.Shared/_DV/Planet/PlanetPrototype.cs:9`), shaped on `DesertWorld` (`_Mono/Planets/permanent_planet.yml:1-13`) with three deliberate differences:

```yaml
- type: planet
  id: WFAsclepiuSurface
  biome: WFBiomeAsclepiu
  mapName: wf-planet-asclepiu-surface          # LocId
  mapLight: "#c8fdff"                          # matches the air layers, zmaps.yml:21
  # NO addedComponents: unlike DesertWorld, no FTLDestination - see B.4
  atmosphere:
    volume: 2500
    immutable: True
    temperature: 288.15                        # 15 C. DELIBERATELY != the network registry's 293.15
    moles: [21.824879, 82.10312]
  biomeMarkerLayers:
    # ore only - entityMask layers, Procedural/biome_ore_templates.yml
    - OreIron        # :3
    - OreQuartz      # :17
    - OreCoal        # :30
    - OreSalt        # :44
    - OreGold        # :60
    - OreSilver      # :75
    - OrePlasma      # :91
    - OreUranium     # :106
    # NO mob marker layers in F0 - see A.1
```

Three notes.

1. **The 288.15 K is load-bearing, not flavour.** It is the only thing that makes the post-init `SetMapAtmosphere` fixup observable, and therefore the only thing that makes test 2 a real regression guard against `CEZLevelMappingSystem.cs:50` clobbering the ground layer at MapInit.
2. **No mob marker layers.** `Lizards` (`Procedural/biome_markers.yml:2-5`, `MobLizard`, groups 3-5) and `Carps` (`:31-33`, `MobCarpDungeon`) are removed from F0. Marker-spawned entities are recorded only as chunk indices in `BiomeComponent.LoadedMarkers` (`Content.Shared/Parallax/Biomes/BiomeComponent.cs:75`) and are never unloaded, while z-eyes seed marker generation on the ground map under any player anywhere in the stack (A.1). Shipping them would spawn permanent hostile fauna under unattended orbiting ships. `Xenos` (`biome_markers.yml:27-29`) and the two removed layers are available to a later feature that adds an explicit gate.
3. `DesertWorld` declares **no** `biomeMarkerLayers` at all, so Asclepiu is the first planet in the fork to exercise `PlanetSystem.SpawnPlanet`'s `AddMarkerLayer` loop (`PlanetSystem.cs:36-39`) — `BiomeSystem.AddMarkerLayer` (`BiomeSystem.ConfigManager.cs:101`) takes a raw string and does not validate, so a typo surfaces only later inside `ProtoManager.Index`. And `salvage_factions.yml` is **not** usable here: it defines `salvageFaction` prototypes consumed by the expedition job, and `PlanetPrototype` has no faction field.

**`planets.yml` — `WFSurfaceAsclepiu`**, the `_WF` glue prototype:

```yaml
- type: wfPlanetSurface
  id: WFSurfaceAsclepiu
  planetType: PlanetAsclepiu          # Resources/Prototypes/_FarHorizons/Space/planets.yml:172
  ground: WFAsclepiuSurface
  airLayers: 2
  cloudLayer: true
  orbitRange: 2000
  buildAtRoundStart: true
  networkComponents:
    - type: MapAtmosphere             # the ONLY registry entry - see B.4
      space: False
      mixture: { volume: 2500, immutable: True, temperature: 293.15, moles: [21.824879, 82.10312] }
  groundComponents:
    - type: CEZLevelRoof
    - type: CEZGroundLayer
  airComponents:
    - type: MapLight
      ambientLightColor: "#c8fdff"
    - type: Gravity
      enabled: true
      inherent: true
  cloudComponents:
    - type: CEZCloudLayer
    - type: MapLight
      ambientLightColor: "#c8fdff"
  orbitComponents: []
```

The YAML kind `wfPlanetSurface` matches the class's **explicit** `[Prototype("wfPlanetSurface")]` attribute; see B.2 for why bare `[Prototype]` would register `wFPlanetSurface` instead and break every one of these documents.

No `groundGrid` for Asclepiu — F0 ships a bare habitable surface; the optional hand-made grid path exists on the prototype for later use and reuses `PlanetSystem.LoadPlanet`'s reserve-tiles trick.

**`entities.yml` — `WFOrbitBeacon`**, copying `PlanetEntity`'s component list verbatim (`_FarHorizons/Entities/Objects/StarSystem/star_system.yml:14-23`): `WarpPoint`, `FTLSmashImmune`, `FTLBeacon`, `IFF` with a distinct colour, `categories: [ HideSpawnMenu ]`. `FTLBeaconComponent` lives in `Content.Server` only; `PlanetEntity` already declares it in a prototype and works, so this is safe by precedent.

---

## D. Locale

`Resources/Locale/en-US/_WF/planet-cracker/planets.ftl`:

```
## Map and beacon names
wf-planet-orbit-map-name = { $planet } orbit
wf-planet-orbit-beacon-name = { $planet } orbital insertion
wf-planet-network-name = { $planet } planet network
wf-planet-asclepiu-surface = Asclepiu

## FTL
wf-shuttle-console-in-transit = Cannot engage FTL while changing altitude.

## wfplanet command
cmd-wfplanet-desc = Build, inspect and tear down Wolfgate planet networks.
cmd-wfplanet-help = Usage: { $command } <list | build <planet> | delete <planet> | spawn <surface id> | system <starSystem id> | tp <planet>>
cmd-wfplanet-disabled = Planet networks are off. Set wf.planet_networks to true.
cmd-wfplanet-invalid-args = Expected one of: list, build, delete, spawn, system, tp.
cmd-wfplanet-unknown-planet = No sector planet named "{ $planet }".
cmd-wfplanet-unknown-surface = No wfPlanetSurface prototype "{ $surface }".
cmd-wfplanet-unknown-system = No starSystem prototype "{ $system }".
cmd-wfplanet-no-surface = { $planet } has no Wolfgate surface.
cmd-wfplanet-already-built = { $planet } already has a network ({ $network }).
cmd-wfplanet-not-built = { $planet } has no network.
cmd-wfplanet-built = Built { $layers } layers for { $planet }: network { $network }, orbit { $orbit }.
cmd-wfplanet-build-failed = Failed to build a network for { $planet }; see the server log.
cmd-wfplanet-deleted = Deleted the network for { $planet }.
cmd-wfplanet-row = { $planet } | surface: { $surface } | network: { $network }
cmd-wfplanet-row-none = none
cmd-wfplanet-empty = No sector planets registered.
cmd-wfplanet-system-set = Set { $system } on map { $map }.
cmd-wfplanet-no-map = Run this with a map in mind: attach to an entity on the sector map first.
cmd-wfplanet-tp-done = Moved to the orbit layer of { $planet }.
cmd-wfplanet-hint-sub = <list|build|delete|spawn|system|tp>
cmd-wfplanet-hint-planet = <planet name>
```

---

## E. Integration tests

One fixture, `Content.IntegrationTests/Tests/_WF/PlanetCracker/PlanetNetworkTest.cs`, in the house `_WF` style (`await using var pair = await PoolManager.GetServerClient()`, one `server.WaitAssertion` per phase, `await pair.CleanReturnAsync()`; see `Content.IntegrationTests/Tests/_WF/Power/SynthRechargeTest.cs`). Opens with `#nullable enable` because the csproj sets `<Nullable>disable</Nullable>`.

Shared setup: `pair.Server.CfgMan.SetCVar("wf.planet_networks", true)` inside a `WaitPost` (`TestPair` reverts every CVar change on return, `RobustToolbox/Robust.UnitTesting/Pool/TestPair.cs:224/:245`; `PoolSettings` has no CVar dictionary). Timing constants come from `pair.SecondsToTicks(4f)` — never hard-coded 90 — because the fall grace is 3 s (`CEZGridFallerComponent.cs:44`) and the support sweep is 0.5 s (`Gravity.cs:42`) at the default 30 Hz tick. Every test ends by calling `WFPlanetNetworkSystem.DeleteNetwork` and running ~5 ticks, so no maps leak into a pooled pair (`AssemblyInfo.cs:8` runs two pairs concurrently).

1. **`BuildsTheFullStack`** — build `WFSurfaceAsclepiu` standalone. Assert: five maps at depths 0–4 with `CEZMapComponent.Depth` matching and `NetworkUid` pointing at the network; `TryMapUp(ground)` and `TryMapDown(orbit)` both resolve (they must go through `MapOffset`'s dictionary fallback, `CESharedZLevelsSystem.Maps.cs:187-195`, because `TryAddMapsIntoNetwork` only back-wires the *new* map's links, `Maps.cs:71-75`); ground has `MapGridComponent` + `BiomeComponent` + `MapLightComponent`; orbit has **no** `MapGridComponent` and **no** `MapLightComponent`, and does have `WFOrbitLayerComponent` and an enabled `FTLDestinationComponent` with `RequireCoordinateDisk` false and `BeaconsOnly` false.
2. **`AtmosphereIsPerLayer`** — the registry/fixup ordering test. Assert `MapAtmosphereComponent.Space` is `false` on ground and both air layers and `true` on orbit, **and that ground's mixture temperature is 288.15, not the registry's 293.15**. The temperature assertion is the whole point: with the two mixtures identical (as in revision 1) the test passed whether or not the fixup ran.
3. **`GridOnOrbitLayerDoesNotFall`** — spawn a 3×3 grid on the orbit layer, `EnsureComp<ShuttleComponent>` + `ShuttleSystem.Enable(force: true)` so it is **Dynamic** (a fresh `CreateGridEntity` grid is `BodyType.Static`, `RobustToolbox/Robust.Shared/Physics/Components/PhysicsComponent.Physics.cs:68`, and the fall gate skips Static at `Gravity.cs:93`). Precondition-assert it has `CEZGridFallerComponent`. Run 4 s. Assert its `MapUid` is still the orbit map and has no `CEZTransitMapComponent`.
4. **`GridOnAirLayerFallsWithoutGravgen`** — the control for #3. Same grid on depth 1, run 4 s, assert its map now has `CEZTransitMapComponent`.
5. **`OrbitIsReachableOnlyInRange`** — spawn a dummy sector map with a `PlanetEntity`, build a network bound to it, then place a shuttle with a powered `FTLDriveComponent` at 500 and at 5000 from the planet and assert `SharedShuttleSystem.CanFTLTo(shuttle, orbitMapId, console)` returns true then false. Also assert it returns true for a shuttle already *on* the orbit map (`SharedShuttleSystem.cs:49-50`).
6. **`CannotFTLOffASurfaceLayer`** — new in revision 2, the control for the outbound half of the gate. Place the same shuttle on the **ground** layer and on an **air** layer, and assert `CanFTLTo(shuttle, sectorMapId, console)` is false from both; then place it on the **orbit** layer and assert `CanFTLTo(shuttle, sectorMapId, console)` is true. Without the outbound guard the first two return true and the orbit layer is not the only door.
7. **`DeleteNetworkRemovesEveryLayer`** — `DeleteNetwork`, run 5 ticks, assert all five map entities and the network entity are deleted or terminating, and that `WFSectorPlanetComponent.Network` is cleared.

Deliberately **not** tested: "a grid rests on the ground layer". `HasGroundUnderFootprint` reads live tiles, and biome chunk loading is driven from `ProcessPlayerChunkRequests` over sessions and their view subscribers (`BiomeSystem.PlayerTracker.cs:22-62`), so an integration test with no attached player and no eyes has zero tiles under the hull and the grid falls. Testing it would require `BiomeSystem.ReserveTiles` first, which tests the fixture rather than the feature.

Local run (Windows, from the worktree root):
`dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt --filter "FullyQualifiedName~PlanetNetworkTest" --logger "console;verbosity=detailed"`
No `Assert.Warn` and no warning-emitting `Ignore` anywhere in the file — CI runs `NUnit.MapWarningTo=Failed` (`.github/workflows/build-test-debug.yml:59`).

---

## F. Build order rationale for the stages

Agents cannot use isolated worktrees here, so the five stages run one after another in this checkout with strictly disjoint file sets. Stage 1 lands the shared vocabulary and the single riskiest upstream line (the fall gate). Stage 2 lands the server builder. Stage 3 lands the two-directional FTL gate, the only change that touches `Content.Client`'s compile (via `Content.Shared`). Stage 4 lands data and tooling, where prototype errors surface — including the `[Prototype("wfPlanetSurface")]` string, which **no C#-only build can catch**; it only fails at prototype load, so Stage 1's gate carries an explicit grep for the literal.

---

## Z. Rejected critiques

**None of the seven reported issues was rejected.** All seven were reproduced against this worktree and folded in. Two clarifications about the review itself, and two places where I took a different fix from the one proposed:

**Z.0 — Two of the seven were the same defect.** Both blockers describe the `[Prototype]` name derivation. Verified once at `RobustToolbox/Robust.Shared/Prototypes/PrototypeUtility.cs:15-22` and `PrototypeManager.cs:1046`; fixed once. The second report's evidence (`CEZLevelMapPrototype.cs:11` `[Prototype("zMap")]`, `CEZGridStackPrototype.cs:22` `[Prototype("cezGridStack")]`) is the stronger of the two and is what section B.2 cites.

**Z.1 — Biome/eye issue (major): defect confirmed, fix (a) taken, fix (b) declined for F0.** The reviewer offered three fixes. I took (a), dropping the mob marker layers, and declined (b), tagging `CEZLevelEye` so `BiomeSystem.CanLoad` rejects it.

Evidence for declining (b): `CanLoad` gates the *entire* viewer branch, terrain chunks included (`BiomeSystem.PlayerTracker.cs:44-61` — `AddChunksInRange` and the marker loop sit behind the same `continue`). Making eyes fail `CanLoad` would stop the ground layer generating terrain under a player standing on an air layer one level up, i.e. it would make the planet invisible and un-landable from above, which is a worse regression than the one being fixed.

Evidence for (a) being sufficient rather than a partial fix: the two failure modes have different shapes. Terrain and ore generation is **bounded and self-limiting** — `_loadArea` is ±16 tiles (`BiomeSystem.cs:49/:65`), `LoadedMarkers` (`BiomeComponent.cs:75`) records each marker chunk so it is generated at most once, and the `entityMask` ore layers substitute for `WallRock*` entities that the `Grasslands` template planted anyway. Mob layers are **unbounded**: `LoadedMarkers` stores no entity list, `UnloadEntities` (`ChunkLoader.cs:233-274`) only walks `LoadedEntities`, so marker mobs are never collected and accumulate for the round wherever a ship has orbited. Removing two YAML lines fixes the unbounded half with no upstream edit at all.

The targeted upstream fix remains available for whichever feature reintroduces fauna, and is recorded here so it is not re-derived: in the viewer branch of `ProcessPlayerChunkRequests`, immediately after `AddChunksInRange(biome, worldPos);` (`PlayerTracker.cs:53`), insert `if (WfSkipMarkerLayers(viewer)) continue;` with a `// WOLFGATE` marker — exactly two lines, because the marker loop is the only remaining work in that loop body, so no re-indentation and no unreachable-code warning. It needs a marker component on `Resources/Prototypes/_CE/Entities/zEye.yml` (currently four lines with no `components:` block) and a `_WF` partial of `BiomeSystem` (`public sealed partial class`, `PlayerTracker.cs:11`) holding the query. F0 does not spend those two upstream edits for a bounded cost.

**Z.2 — Atmosphere test (major): defect confirmed, fix taken, reviewer's alternative declined.** The reviewer's primary suggestion (differentiate the temperatures) is what section A.3 adopts. Their alternative — "drop `MapAtmosphere` from `networkComponents` and put it in `airComponents`/`cloudComponents` instead" — is declined with evidence: `CreateTransitMap` (`Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.Transit.cs:831-832`) copies **only** `network.Comp.Components` onto a transit map (plus a `MapLightComponent` it ensures separately at `:834-839`). With no `MapAtmosphere` in the registry, every transit gap between air layers becomes vacuum, so a crewmember riding an open-topped hull between layers would suffocate. The reviewer's own note flags this; recording it explicitly so the alternative is not revisited.

**Z.3 — `centre` frame (minor), outbound gate (major), and `Reason` localization (minor)** were each reproduced exactly as described and adopted verbatim; see A.2, B.3 and B.6. Two incidental line-number corrections found while verifying: the 4-arg `TryAddFTLDestination` overload is at `ShuttleSystem.FasterThanLight.cs:209` (revision 1's section B.4 said 204, which is the 2-arg overload that defaults `requireDisk` to true — Stage 2 had it right at 209), and `SalvageSystem.Runner.cs` subscribes `ConsoleFTLAttemptEvent` at `:36` with the handler at `:39`, `NukeopsRuleSystem` at `:66` with the handler at `:312`.

## FILES
- [create] Content.Shared/_WF/PlanetCracker/Planets/WFPlanetSurfacePrototype.cs — [Prototype("wfPlanetSurface")] — the kind string is DECLARED EXPLICITLY, never derived; bare [Prototype] would register `wFPlanetSurface` (PrototypeUtility.cs:15-22 lowercases only index 0) and break every YAML document. Describes one planet's whole z-stack. Fields: ID; PlanetType (ProtoId<PlanetTypePrototype>, required); Ground (ProtoId<PlanetPrototype>, required); GroundGrid (ResPath?); AirLayers (int = 2); CloudLayer (bool = true); OrbitRange (float = 2000f); BuildAtRoundStart (bool = false); NetworkComponents / GroundComponents / AirComponents / CloudComponents / OrbitComponents (ComponentRegistry?); OrbitMapName (LocId = "wf-planet-orbit-map-name"); OrbitBeaconName (LocId = "wf-planet-orbit-beacon-name"). No license header; /// <summary> one-liner on the type and every member.
- [create] Content.Shared/_WF/PlanetCracker/Planets/WFOrbitLayerComponent.cs — Marks a map as a planet's orbit layer. [RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]. Fields: NetEntity? Planet (the sector PlanetEntity), float Range (enter-orbit gate radius), NetEntity? Network. Read by the fall-gate exemption on the server and by BOTH halves of the shared FTL gate (inbound range, outbound 'orbit is the only door') on client and server.
- [create] Content.Shared/_WF/PlanetCracker/Planets/WFSectorPlanetComponent.cs — The round-scoped planet registry entry, on each spawned Far Horizons PlanetEntity. [RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]. Fields: ProtoId<WFPlanetSurfacePrototype>? Surface; NetEntity? Network; NetEntity? OrbitMap. Networked so F2's survey console can read it without a bespoke BUI message.
- [create] Content.Shared/_WF/CCVar/PlanetCrackerCVars.cs — [CVarDefs] sealed class PlanetCrackerCVars with the single CVarDef<bool> PlanetNetworks = CVarDef.Create("wf.planet_networks", false, CVar.SERVERONLY). Two-segment name so development.toml can express it as [wf] planet_networks = true.
- [create] Content.Server/_WF/PlanetCracker/Planets/CEZLevelsSystem.Wolfgate.cs — Partial of Content.Server._CE.ZLevels.Core.CEZLevelsSystem (declared public sealed partial at CEZLevelsSystem.cs:18) holding one helper: private bool WfIsOrbitLayer(EntityUid mapUid) => HasComp<WFOrbitLayerComponent>(mapUid). Exists so the upstream fall-gate edit is two lines with no new using and no new query field.
- [create] Content.Server/_WF/PlanetCracker/Planets/WFPlanetNetworkComponent.cs — Server-only component on the CEZMapNetwork entity recording what this network is. [RegisterComponent, UnsavedComponent]. Fields: EntityUid? Planet; EntityUid GroundMap; EntityUid OrbitMap; List<EntityUid> Layers; ProtoId<WFPlanetSurfacePrototype> Surface; Vector2 Centre (WORLD frame, matching what the shared range gate measures).
- [create] Content.Server/_WF/PlanetCracker/Planets/WFPlanetRegistrySystem.cs — Owns registration and the registry API. RegisterPlanet(Entity<StarSystemMapComponent> map, EntityUid planetEntity, Planet helper): CVar gate, resolve the StarSystemPlanet entry by recomputing Vector2(cos(angle),sin(angle))*distance and matching planet.Position (name fallback + log), look up the wfPlanetSurface whose PlanetType matches entry.Planet from a dictionary built once in Initialize, AddComp<WFSectorPlanetComponent>, and call WFPlanetNetworkSystem.TryBuildNetwork when surface.BuildAtRoundStart. Public: GetPlanets(), TryGetPlanetByName(string, out Entity<WFSectorPlanetComponent>), TryGetSurface(ProtoId<PlanetTypePrototype>, out WFPlanetSurfacePrototype?). Subscribes SubscribeLocalEvent<WFSectorPlanetComponent, ComponentShutdown> to tear the network down.
- [create] Content.Server/_WF/PlanetCracker/Planets/WFPlanetNetworkSystem.cs — The builder. Dependencies: PlanetSystem, BiomeSystem, MapSystem, MapLoaderSystem, AtmosphereSystem, MetaDataSystem, TransformSystem, CEZLevelsSystem, ShuttleSystem, IPrototypeManager, IConfigurationManager. Public: bool TryBuildNetwork(Entity<WFSectorPlanetComponent> planet, out EntityUid network); EntityUid? BuildNetwork(WFPlanetSurfacePrototype surface, Vector2 centre, string displayName, EntityUid? planetEntity); void DeleteNetwork(EntityUid network). `centre` is always a WORLD position (TryBuildNetwork computes it with _transform.GetWorldPosition, never Transform().LocalPosition — SpawnAtPosition at StarSystemMapSystem.cs:63 runs grid traversal so the PlanetEntity's parent is not guaranteed to be the map). Build order exactly as in plan B.3/B.4: all maps uninitialised, CreateMapNetwork(surface.NetworkComponents), one TryAddMapsIntoNetwork call per depth ascending from 0 with the bool checked and unwound on failure, InitializeZNetwork, then post-init fixups (ground atmosphere at 288.15 K + GroundComponents, AirComponents per air layer, CloudComponents + CEZCloudLayer, orbit SetMapAtmosphere(space:true) + OrbitComponents + WFOrbitLayerComponent + TryAddFTLDestination 4-arg overload (FasterThanLight.cs:209) + named WFOrbitBeacon at centre + map entity name). Also subscribes the broadcast SubscribeLocalEvent<ConsoleFTLAttemptEvent> to cancel FTL from a CEZTransitMapComponent map with args.Reason = Loc.GetString("wf-shuttle-console-in-transit") — LOCALIZED, matching ForceAnchorSystem.cs:36, because Reason is a display string not a LocId.
- [create] Content.Server/_WF/PlanetCracker/Planets/StarSystemMapSystem.Wolfgate.cs — Partial of Content.Server._FarHorizons.StarSystem.StarSystemMapSystem (declared public sealed partial at StarSystemMapSystem.cs:12) with [Dependency] private WFPlanetRegistrySystem _wfPlanets = default!; and private void WfPlanetSpawned(Entity<StarSystemMapComponent> map, EntityUid planetEntity, Planet planet) => _wfPlanets.RegisterPlanet(map, planetEntity, planet). Mirrors the existing ShuttleConsoleWindow.Wolfgate.cs / ShuttleConsoleBoundUserInterface.Wolfgate.cs precedent.
- [create] Content.Shared/_WF/PlanetCracker/Planets/SharedShuttleSystem.Wolfgate.cs — Partial of Content.Shared.Shuttles.Systems.SharedShuttleSystem (declared public abstract partial at SharedShuttleSystem.cs:15) with protected bool WfAllowFTL(EntityUid shuttleUid, EntityUid targetMapUid) — the TWO-DIRECTIONAL gate. Outbound: false if the shuttle's map has CEZTransitMapComponent, or has CEZMapComponent without WFOrbitLayerComponent (a hull on a surface/air/cloud layer must climb to orbit, not jump off the grass). Inbound: if the target has WFOrbitLayerComponent, resolve Planet, require the shuttle on the planet's map and world distance <= Range. Otherwise true. Every component read is shared+networked (CEZMapComponent.cs:13, CEZTransitMapComponent.cs:13) so client and server agree. Uses the class's existing private _xformQuery and protected XformSystem. Sandbox-clean: no IO, no reflection.
- [create] Content.Server/_WF/PlanetCracker/Planets/Commands/WFPlanetCommand.cs — [AdminCommand(AdminFlags.Server | AdminFlags.Mapping)] sealed partial class WFPlanetCommand : LocalizedEntityCommands, Command => WolfgateAdminCommands.Planet ("wfplanet"). Subcommands: list; build <planet name>; delete <planet name>; spawn <wfPlanetSurface id> (standalone network, no sector planet, for dev); system <starSystem id> (calls StarSystemMapSystem.SetSystem on the shell player's current map so dev has planets at all); tp <planet name> (moves the shell player's attached entity to the orbit layer's centre). Refuses with cmd-wfplanet-disabled when wf.planet_networks is false. CompletionResult hints from the locale keys, matching GridPowerCommand.cs.
- [edit] Content.Shared/_WF/Administration/WolfgateAdminCommands.cs — Add one const to the existing _WF command-name table: public const string Planet = "wfplanet"; after ErtBuilderUi (line 15). This file is already _WF, so it is not an upstream edit.
- [create] Resources/Prototypes/_WF/PlanetCracker/biomes.yml — biomeTemplate WFBiomeAsclepiu: three BiomeMetaLayers over existing templates Grasslands (threshold -0.5, freq 0.001), MonoOcean (0.25, freq 0.01), Snow (0.5, freq 0.001), following the MonoContinentalDesertOcean pattern at _Mono/Planets/biome_ocean.yml:47-76.
- [create] Resources/Prototypes/_WF/PlanetCracker/planet_surfaces.yml — - type: planet id WFAsclepiuSurface: biome WFBiomeAsclepiu, mapName wf-planet-asclepiu-surface, mapLight "#c8fdff", atmosphere 2500 / 288.15 K / [21.824879, 82.10312] — the 288.15 is DELIBERATELY different from the network registry's 293.15 so the post-init SetMapAtmosphere fixup is observable in test 2. biomeMarkerLayers is the eight entityMask ore layers ONLY [OreIron, OreQuartz, OreCoal, OreSalt, OreGold, OreSilver, OrePlasma, OreUranium]; Lizards and Carps are deliberately absent because marker-spawned entities are never unloaded (BiomeComponent.cs:75 tracks chunk indices only) and z-eyes seed marker generation on the ground map under any orbiting ship (BiomeSystem.PlayerTracker.cs:42-61 + CEZLevelsSystem.View.cs:170-210). Explicitly NO addedComponents: an FTLDestination here would make UpdateFTLArriving disable every arriving hull.
- [create] Resources/Prototypes/_WF/PlanetCracker/planets.yml — - type: wfPlanetSurface id WFSurfaceAsclepiu: planetType PlanetAsclepiu, ground WFAsclepiuSurface, airLayers 2, cloudLayer true, orbitRange 2000, buildAtRoundStart true, networkComponents [MapAtmosphere breathable at 293.15 ONLY], groundComponents [CEZLevelRoof, CEZGroundLayer], airComponents [MapLight #c8fdff, Gravity enabled+inherent], cloudComponents [CEZCloudLayer, MapLight], orbitComponents []. The `wfPlanetSurface` kind matches the class's explicit [Prototype("wfPlanetSurface")] attribute.
- [create] Resources/Prototypes/_WF/PlanetCracker/entities.yml — Entity WFOrbitBeacon, categories [HideSpawnMenu], components WarpPoint / FTLSmashImmune / FTLBeacon / IFF with a distinct colour — a verbatim copy of PlanetEntity's component list from _FarHorizons/.../star_system.yml:14-23 so it survives Smimsh and appears in every console's beacon list.
- [create] Resources/Locale/en-US/_WF/planet-cracker/planets.ftl — All F0 locale keys: wf-planet-orbit-map-name, wf-planet-orbit-beacon-name, wf-planet-network-name, wf-planet-asclepiu-surface, wf-shuttle-console-in-transit, and the cmd-wfplanet-* set (see plan section D for the full list).
- [create] Content.IntegrationTests/Tests/_WF/PlanetCracker/PlanetNetworkTest.cs — [TestFixture][TestOf(typeof(WFPlanetNetworkSystem))] with the SEVEN tests from plan section E: BuildsTheFullStack, AtmosphereIsPerLayer (asserts ground temperature 288.15 vs registry 293.15, not just moles), GridOnOrbitLayerDoesNotFall, GridOnAirLayerFallsWithoutGravgen, OrbitIsReachableOnlyInRange, CannotFTLOffASurfaceLayer (new: the outbound half of the gate), DeleteNetworkRemovesEveryLayer. #nullable enable; sets wf.planet_networks on pair.Server.CfgMan; uses pair.SecondsToTicks(4f); always DeleteNetwork before CleanReturnAsync.
- [edit] Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.Gravity.cs — UPSTREAM (_CE). Two lines with a // WOLFGATE marker inserted at line 92, between the z-map guard (line 90-91) and the BodyType.Static guard (line 93), calling WfIsOrbitLayer(mapUid) to skip grids parked on an orbit layer.
- [edit] Content.Shared/Shuttles/Systems/SharedShuttleSystem.cs — UPSTREAM (vanilla). Two lines with a // WOLFGATE marker inserted at line 52 in CanFTLTo, after the same-map early-out (:49-50), calling WfAllowFTL(shuttleUid, mapUid) and returning false when it fails. One insert now gates BOTH directions: entering orbit only from within range of the sector planet, and leaving a planet network only from the orbit layer. Drives the client destination list (MapScreen.xaml.cs:332) and the server jump (ShuttleConsoleSystem.FTL.cs:152) from one place.
- [edit] Content.Server/_FarHorizons/StarSystem/StarSystemMapSystem.cs — UPSTREAM (_FarHorizons). One line with a // WOLFGATE marker inserted at line 66 inside the planet spawn loop, after _pvs.AddGlobalOverride(spawnedPlanet), calling WfPlanetSpawned(ent, spawnedPlanet, planet) so _WF can register the body and build its network.
- [edit] Resources/ConfigPresets/Build/development.toml — UPSTREAM (shared config). A three-line block appended at line 41 with a # WOLFGATE comment: [wf] / planet_networks = true, so the feature is on in the dev environment.

## UPSTREAM HOOKS
- Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.Gravity.cs:92 — Insert after the z-map guard's `continue;` on line 91 and before the `BodyType.Static` guard on line 93, inside the `EntityQueryEnumerator<CEZGridFallerComponent, MapGridComponent>` loop of UpdateGridGravity:

                if (WfIsOrbitLayer(mapUid)) // WOLFGATE: grids parked on a planet orbit layer never fall.
                    continue;

Two lines. `WfIsOrbitLayer` is defined in the new _WF partial Content.Server/_WF/PlanetCracker/Planets/CEZLevelsSystem.Wolfgate.cs, so no new `using` and no new EntityQuery field are needed here. Placed before RigidSetHasSupport (line 99) so a parked ship costs one HasComp and skips the rigid-set flood entirely. Do not move the existing comment block at lines 96-98.
- Content.Shared/Shuttles/Systems/SharedShuttleSystem.cs:52 — Insert in CanFTLTo (declared line 44), after the same-map early-out (`if (shuttleMap == targetMap) return true;`, lines 49-50) and before the FTLDestinationComponent lookup currently on line 52:

        if (!WfAllowFTL(shuttleUid, mapUid)) // WOLFGATE: orbit is the only FTL door in and out of a planet network, and only from within range.
            return false;

Two lines. `WfAllowFTL` is defined in the new _WF partial Content.Shared/_WF/PlanetCracker/Planets/SharedShuttleSystem.Wolfgate.cs; the class is `public abstract partial class` (line 15). REVISION 2: this single helper now gates both directions. Outbound it refuses any jump launched from a CEZTransitMapComponent map or from a CEZMapComponent map that is not the orbit layer (verified necessary: nothing parks a landed hull, GroundFriction.cs:34-37; the sector map is an open destination, ShuttleSystem.FasterThanLight.cs:137; and FTLFree's distance test, SharedShuttleSystem.cs:243-247, is ~0 from any layer because layers share the planet's world XY). Inbound it applies the orbit range gate. Placement after the same-map early-out is deliberate so a hull on a layer can still target its own map. The two call sites — client destination list (Content.Client/Shuttles/UI/MapScreen.xaml.cs:332) and server jump (Content.Server/Shuttles/Systems/ShuttleConsoleSystem.FTL.cs:152) — therefore cannot disagree. Add no usings to the upstream file.
- Content.Server/_FarHorizons/StarSystem/StarSystemMapSystem.cs:66 — Insert inside the planet loop of SpawnEntities, immediately after `_pvs.AddGlobalOverride(spawnedPlanet);` on line 65 and before the closing brace on line 66:

                WfPlanetSpawned(ent, spawnedPlanet, planet); // WOLFGATE: register sector bodies that have a Wolfgate surface.

One line. `WfPlanetSpawned` is defined in the new _WF partial Content.Server/_WF/PlanetCracker/Planets/StarSystemMapSystem.Wolfgate.cs; the class is `public sealed partial class` (line 12). This is the only registration hook: the spawned uid is otherwise discarded upstream, and the Planet runtime helper carries no prototype id, so nothing else in the codebase can link a sector body back to its definition. Note for the implementer: the body is created at :62-63 with `SpawnAtPosition(planetEnt.ID, planetCoords)`, which runs grid traversal — never read its LocalPosition downstream, always GetWorldPosition.
- Resources/ConfigPresets/Build/development.toml:41 — Append at the end of the file (currently 40 lines), after the [mono] block:

# WOLFGATE
[wf]
planet_networks = true

Turns the feature on in the dev environment. Note this is necessary but not sufficient in dev: development.toml:3 sets lobbyenabled = false, which forces the preset to "sandbox" (Content.Server/GameTicking/GameTicker.GamePreset.cs:109), and game_presets.yml:109-117 carries only the Sandbox rule — so StarSystemKyphrus never runs and no PlanetEntity is spawned. Use `wfplanet system SystemKyphrus` or `wfplanet spawn WFSurfaceAsclepiu` to exercise the feature there.

## STAGES
### Stage 1 — shared vocabulary, CVar, and the fall-gate exemption
Files: Content.Shared/_WF/PlanetCracker/Planets/WFPlanetSurfacePrototype.cs, Content.Shared/_WF/PlanetCracker/Planets/WFOrbitLayerComponent.cs, Content.Shared/_WF/PlanetCracker/Planets/WFSectorPlanetComponent.cs, Content.Shared/_WF/CCVar/PlanetCrackerCVars.cs, Content.Server/_WF/PlanetCracker/Planets/CEZLevelsSystem.Wolfgate.cs, Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.Gravity.cs
Gate: 1. dotnet build Content.Server/Content.Server.csproj -c Debug — zero errors.
2. Confirm the upstream diff is exactly two added lines plus one blank: git diff --stat Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.Gravity.cs should report 3 insertions, 0 deletions.
3. PROTOTYPE-KIND GREP (this is the only check that catches the Stage 4 blocker, because no C# build does): grep -n 'Prototype("wfPlanetSurface")' Content.Shared/_WF/PlanetCracker/Planets/WFPlanetSurfacePrototype.cs must return exactly one hit. If the file instead contains a bare `[Prototype]`, the stage has failed.

Create the three shared types, the CVar class, the CEZLevelsSystem _WF partial, and make the single upstream fall-gate edit.

WFPlanetSurfacePrototype.cs — namespace Content.Shared._WF.PlanetCracker.Planets.

*** THE ATTRIBUTE MUST BE `[Prototype("wfPlanetSurface")]`, WITH THE EXPLICIT STRING. ***
Do NOT write bare `[Prototype]`. Robust derives an unqualified kind name with PrototypeUtility.CalculatePrototypeName (RobustToolbox/Robust.Shared/Prototypes/PrototypeUtility.cs:15-22, used at PrototypeManager.cs:1046), which lowercases ONLY index 0 and strips the trailing "Prototype": `WFPlanetSurfacePrototype` -> `wFPlanetSurface`, with a capital F. Every `- type: wfPlanetSurface` document in Stage 4 would then fail to resolve to a kind at prototype load. Nothing in a C#-only build catches this. The _CE prototypes with the same acronym shape already declare explicitly for this reason: CEZLevelMapPrototype.cs:11 is `[Prototype("zMap")]`, CEZGridStackPrototype.cs:22 is `[Prototype("cezGridStack")]`.

sealed partial class : IPrototype with [IdDataField] string ID. Fields, each with a /// <summary> one-liner: [DataField(required: true)] ProtoId<PlanetTypePrototype> PlanetType; [DataField(required: true)] ProtoId<PlanetPrototype> Ground; [DataField] ResPath? GroundGrid; [DataField] int AirLayers = 2; [DataField] bool CloudLayer = true; [DataField] float OrbitRange = 2000f; [DataField] bool BuildAtRoundStart; [DataField] ComponentRegistry? NetworkComponents/GroundComponents/AirComponents/CloudComponents/OrbitComponents; [DataField] LocId OrbitMapName = "wf-planet-orbit-map-name"; [DataField] LocId OrbitBeaconName = "wf-planet-orbit-beacon-name". Usings: Content.Shared._DV.Planet (PlanetPrototype), Content.Shared._FarHorizons.StarSystem.Prototypes (PlanetTypePrototype), Robust.Shared.Prototypes, Robust.Shared.Utility (ResPath). No license header.

WFOrbitLayerComponent.cs — [RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent] sealed partial class with [DataField, AutoNetworkedField] NetEntity? Planet; [DataField, AutoNetworkedField] float Range = 2000f; [DataField, AutoNetworkedField] NetEntity? Network. Doc the type as 'The top map of a planet network: vacuum, no terrain, the only FTL door in or out, and grids parked here never fall.'

WFSectorPlanetComponent.cs — [RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent] with [DataField, AutoNetworkedField] ProtoId<WFPlanetSurfacePrototype>? Surface; NetEntity? Network; NetEntity? OrbitMap. Doc it as the round-scoped planet registry entry.

PlanetCrackerCVars.cs — copy the shape of Content.Shared/_WF/CCVar/InternetSoundCVars.cs: namespace Content.Shared._WF.CCVar, [CVarDefs] public sealed class PlanetCrackerCVars, one field `public static readonly CVarDef<bool> PlanetNetworks = CVarDef.Create("wf.planet_networks", false, CVar.SERVERONLY);` with an XML doc comment.

CEZLevelsSystem.Wolfgate.cs — namespace Content.Server._CE.ZLevels.Core; `public sealed partial class CEZLevelsSystem` containing only:
    /// <summary>True when this map is a planet orbit layer, where parked grids never fall.</summary>
    private bool WfIsOrbitLayer(EntityUid mapUid) => HasComp<WFOrbitLayerComponent>(mapUid);
with `using Content.Shared._WF.PlanetCracker.Planets;`.

Upstream edit — CEZLevelsSystem.Gravity.cs. Insert at line 92, i.e. between `continue;` (line 91, the z-map guard) and the blank line preceding the BodyType.Static guard on line 93:

                if (WfIsOrbitLayer(mapUid)) // WOLFGATE: grids parked on a planet orbit layer never fall.
                    continue;

followed by a blank line. Do not add a using to this file; the helper is on the partial. Do not touch anything else in the file, and do not move the existing comment block at lines 96-98.

Write every file with LF line endings.

### Stage 2 — server builder, registry, and the star-system hook
Files: Content.Server/_WF/PlanetCracker/Planets/WFPlanetNetworkComponent.cs, Content.Server/_WF/PlanetCracker/Planets/WFPlanetRegistrySystem.cs, Content.Server/_WF/PlanetCracker/Planets/WFPlanetNetworkSystem.cs, Content.Server/_WF/PlanetCracker/Planets/StarSystemMapSystem.Wolfgate.cs, Content.Server/_FarHorizons/StarSystem/StarSystemMapSystem.cs
Gate: 1. dotnet build Content.Server/Content.Server.csproj -c Debug — zero errors.
2. git diff --stat Content.Server/_FarHorizons/StarSystem/StarSystemMapSystem.cs must report exactly 1 insertion, 0 deletions.
3. grep the new server files for `SubscribeLocalEvent<` and confirm exactly two hits: one directed on WFSectorPlanetComponent+ComponentShutdown and one broadcast on ConsoleFTLAttemptEvent.
4. grep WFPlanetNetworkSystem.cs and WFPlanetRegistrySystem.cs for `LocalPosition` — must return zero hits; the builder measures in world space only.
5. grep WFPlanetNetworkSystem.cs for `args.Reason` and confirm the line contains `Loc.GetString`.

WFPlanetNetworkComponent.cs — namespace Content.Server._WF.PlanetCracker.Planets. [RegisterComponent, UnsavedComponent] sealed partial class on the CEZMapNetwork entity: EntityUid? Planet; EntityUid GroundMap; EntityUid OrbitMap; List<EntityUid> Layers = new(); ProtoId<WFPlanetSurfacePrototype> Surface; Vector2 Centre (world frame).

WFPlanetNetworkSystem.cs — the builder. Dependencies (all `[Dependency] private X _x = default!;`, no readonly): PlanetSystem _planet, BiomeSystem _biome, MapSystem _map, MapLoaderSystem _mapLoader, AtmosphereSystem _atmos, MetaDataSystem _meta, CEZLevelsSystem _zLevels, ShuttleSystem _shuttle, TransformSystem _transform, IPrototypeManager _proto, IConfigurationManager _cfg.

Initialize(): SubscribeLocalEvent<ConsoleFTLAttemptEvent>(OnConsoleFTLAttempt). This is a BROADCAST subscription, legal alongside the three existing handlers (ForceAnchorSystem.cs:20, SalvageSystem.Runner.cs:36, NukeopsRuleSystem.cs:66). Do NOT copy ForceAnchorSystem's `before: new[] { typeof(ShuttleSystem) }` — ShuttleSystem raises this event (FasterThanLight.cs:278), it does not subscribe it. OnConsoleFTLAttempt: if args.Cancelled return; if Transform(args.Uid).MapUid is {} m && HasComp<CEZTransitMapComponent>(m) then args.Cancelled = true and
    args.Reason = Loc.GetString("wf-shuttle-console-in-transit");
LOCALIZE IT. `Reason` is a human-readable display string, not a LocId: it is returned raw as the out-param of CanFTL (ShuttleSystem.FasterThanLight.cs:283) alongside the sibling path at :275 which does `reason = Loc.GetString("shuttle-console-in-expedition")`, and the precedent handler does `args.Reason = Loc.GetString("shuttle-console-force-anchored")` (ForceAnchorSystem.cs:36). Both consumers currently drop it behind a `// TODO: Session popup` (ShuttleConsoleSystem.FTL.cs:145-149), which is exactly how a raw LocId would survive unnoticed.

public EntityUid? BuildNetwork(WFPlanetSurfacePrototype surface, Vector2 centre, string displayName, EntityUid? planetEntity):
  `centre` is a WORLD position in every use below.
  1. Bail (log + null) if !_cfg.GetCVar(PlanetCrackerCVars.PlanetNetworks).
  2. groundProto = _proto.Index(surface.Ground). ground = _planet.SpawnPlanet(surface.Ground, runMapInit: false). If surface.GroundGrid is {} path: get the MapId from Comp<MapComponent>(ground), _mapLoader.TryLoadGrid(...) at offset `centre` (check the overload for the offset parameter; if only the 3-arg form exists, load then _transform.SetLocalPosition the grid to centre), then _biome.ReserveTiles(ground, Comp<MapGridComponent>(grid).LocalAABB.Enlarged(0.2f), scratch list) exactly as PlanetSystem.LoadPlanet:67-69 does. On grid failure Del(ground) and return null. Do NOT call _map.InitializeMap here — the whole stack stays uninitialised until step 6.
  3. Build the ordered layer list: index 0 = ground; then surface.AirLayers bare maps from _map.CreateMap(out _, runMapInit: false); then one more bare map if surface.CloudLayer; then one more bare map as orbit. Record orbit and its MapId.
  4. network = _zLevels.CreateMapNetwork(surface.NetworkComponents); _meta.SetEntityName(network, Loc.GetString("wf-planet-network-name", ("planet", displayName))).
  5. For depth = 0 to layers.Count - 1 IN ASCENDING ORDER, call _zLevels.TryAddMapsIntoNetwork(network, new Dictionary<EntityUid,int> { { layers[depth], depth } }) — ONE DEPTH PER CALL. Depth 0 must be first; QuickApiCache (CEZLevelsSystem.Maps.cs:126) only takes its first-element branch at depth 0. Check the returned bool; on false, _zLevels.DeleteMapNetwork(network), log an error and return null.
  6. _zLevels.InitializeZNetwork((network, network.Comp)). The network ComponentRegistry lands on every layer here, at MapInit (CEZLevelMappingSystem.cs:50), with removeExisting defaulted to TRUE.
  7. Post-init fixups, in this order, because last write wins:
     ground: _atmos.SetMapAtmosphere(ground, false, groundProto.Atmosphere) to undo the registry's overwrite — this restores the 288.15 K surface mixture over the registry's 293.15 K one, and that 5 K delta is the only thing test 2 can observe; then EntityManager.AddComponents(ground, surface.GroundComponents, removeExisting: false) when non-null.
     each air layer: AddComponents(layer, surface.AirComponents, removeExisting: false).
     cloud layer (if any): AddComponents(layer, surface.CloudComponents, removeExisting: false); EnsureComp<CEZCloudLayerComponent>(layer).
     orbit: _atmos.SetMapAtmosphere(orbit, true, GasMixture.SpaceGas); RemComp<MapLightComponent>(orbit) if present; AddComponents(orbit, surface.OrbitComponents, removeExisting: false); var ol = EnsureComp<WFOrbitLayerComponent>(orbit) with Planet = GetNetEntity(planetEntity), Range = surface.OrbitRange, Network = GetNetEntity(network), then Dirty(orbit, ol); _meta.SetEntityName(orbit, Loc.GetString(surface.OrbitMapName, ("planet", displayName))); _shuttle.TryAddFTLDestination(orbitMapId, true, false, false, out _) — the FOUR-ARG overload at ShuttleSystem.FasterThanLight.cs:209, NEVER the two-arg one at :204 which defaults requireDisk to true; beacon = SpawnAtPosition("WFOrbitBeacon", new EntityCoordinates(orbit, centre)) and _meta.SetEntityName(beacon, Loc.GetString(surface.OrbitBeaconName, ("planet", displayName))).
     ASSERT IN CODE (Log.Error, do not throw): orbit must NOT have MapGridComponent. If it does, UpdateFTLArriving:894 will disable every arriving hull and ConvoyBlockedByPlane will block climbs.
  8. EnsureComp<WFPlanetNetworkComponent>(network) filled in (Centre = centre); return network.

public bool TryBuildNetwork(Entity<WFSectorPlanetComponent> planet, out EntityUid network): resolve the surface prototype from planet.Comp.Surface, compute
    var centre = _transform.GetWorldPosition(planet.Owner);
USE GetWorldPosition, NOT Transform(planet).LocalPosition. The body is created with SpawnAtPosition (StarSystemMapSystem.cs:62-63), which runs grid traversal, so its parent is not guaranteed to remain the sector map entity — a grid overlapping the planet marker reparents it and LocalPosition silently becomes grid-local, which would put the orbit beacon thousands of tiles from where the shared range gate (which compares XformSystem.GetWorldPosition of planet and shuttle) measures. Then displayName = MetaData(planet).EntityName, call BuildNetwork, then write planet.Comp.Network / OrbitMap and Dirty.

public void DeleteNetwork(EntityUid network): clear the owning WFSectorPlanetComponent's Network/OrbitMap fields (Dirty) then _zLevels.DeleteMapNetwork(network), which QueueDels every member map and the network entity.

WFPlanetRegistrySystem.cs — Dependencies: IPrototypeManager _proto, IConfigurationManager _cfg, WFPlanetNetworkSystem _networks, MetaDataSystem _meta, TransformSystem _transform. Initialize(): build a Dictionary<ProtoId<PlanetTypePrototype>, WFPlanetSurfacePrototype> from _proto.EnumeratePrototypes<WFPlanetSurfacePrototype>(), logging a warning on a duplicate planetType; SubscribeLocalEvent<WFSectorPlanetComponent, ComponentShutdown>(OnSectorPlanetShutdown) — the ONLY directed subscription this feature adds, on a component that exists nowhere else in the repo.

public void RegisterPlanet(Entity<StarSystemMapComponent> map, EntityUid planetEntity, Planet planet): return early if the CVar is off or map.Comp.System is null. Index the StarSystemPrototype, iterate its Planets entries recomputing `new Vector2(MathF.Cos(entry.Angle), MathF.Sin(entry.Angle)) * entry.Distance` — the identical expression to SharedStarSystemMapSystem.cs:26, so equality is exact — and take the first entry whose position equals planet.Position; fall back to matching _proto.Index(entry.Planet).Name == planet.Name and log a warning if the position match failed. If no entry matches, log an error and return. Look the surface up by entry.Planet in the dictionary; if there is none, return without adding anything. Otherwise AddComp<WFSectorPlanetComponent>(planetEntity) with Surface set and Dirty, and if surface.BuildAtRoundStart call _networks.TryBuildNetwork.

public helpers: GetPlanets() over AllEntityQuery<WFSectorPlanetComponent>, TryGetPlanetByName(string name, out Entity<WFSectorPlanetComponent>) comparing MetaData names case-insensitively, TryGetSurface(ProtoId<PlanetTypePrototype>, out WFPlanetSurfacePrototype?).

OnSectorPlanetShutdown: if the component's Network resolves, call _networks.DeleteNetwork on it.

StarSystemMapSystem.Wolfgate.cs — namespace Content.Server._FarHorizons.StarSystem; `public sealed partial class StarSystemMapSystem` with `[Dependency] private WFPlanetRegistrySystem _wfPlanets = default!;` and `private void WfPlanetSpawned(Entity<StarSystemMapComponent> map, EntityUid planetEntity, Planet planet) => _wfPlanets.RegisterPlanet(map, planetEntity, planet);` plus a /// <summary>.

Upstream edit — StarSystemMapSystem.cs. Insert exactly one line at line 66, after `_pvs.AddGlobalOverride(spawnedPlanet);`:
                WfPlanetSpawned(ent, spawnedPlanet, planet); // WOLFGATE: register sector bodies that have a Wolfgate surface.
Nothing else in that file changes; add no usings (the partial carries them).

### Stage 3 — the two-directional FTL gate
Files: Content.Shared/_WF/PlanetCracker/Planets/SharedShuttleSystem.Wolfgate.cs, Content.Shared/Shuttles/Systems/SharedShuttleSystem.cs
Gate: 1. dotnet build Content.Client/Content.Client.csproj -c Debug AND dotnet build Content.Server/Content.Server.csproj -c Debug — both zero errors (the client build is what proves the gate is genuinely predictable and sandbox-clean).
2. git diff --stat Content.Shared/Shuttles/Systems/SharedShuttleSystem.cs must report 3 insertions, 0 deletions.
3. grep SharedShuttleSystem.Wolfgate.cs for `System.IO`, `Process`, `Reflection`, `ConditionalWeakTable` — zero hits.

SharedShuttleSystem.Wolfgate.cs — namespace Content.Shared.Shuttles.Systems; `public abstract partial class SharedShuttleSystem` with a single method that gates BOTH directions:

    /// <summary>The orbit layer is the only FTL door in or out of a planet network, and only from within range of the sector planet.</summary>
    protected bool WfAllowFTL(EntityUid shuttleUid, EntityUid targetMapUid)
    {
        var shuttleXform = _xformQuery.GetComponent(shuttleUid);

        // Outbound: never from mid-transit, and never off a surface, air or cloud layer - you climb to orbit first.
        if (shuttleXform.MapUid is { } shuttleMapUid)
        {
            if (HasComp<CEZTransitMapComponent>(shuttleMapUid))
                return false;

            if (HasComp<CEZMapComponent>(shuttleMapUid) && !HasComp<WFOrbitLayerComponent>(shuttleMapUid))
                return false;
        }

        // Inbound: an orbit layer is only reachable from within range of its own sector planet.
        if (!TryComp<WFOrbitLayerComponent>(targetMapUid, out var orbit))
            return true;

        if (orbit.Planet is not { } netPlanet || !TryGetEntity(netPlanet, out var planet))
            return false;

        var planetXform = _xformQuery.GetComponent(planet.Value);

        if (planetXform.MapUid != shuttleXform.MapUid)
            return false;

        var delta = XformSystem.GetWorldPosition(planetXform) - XformSystem.GetWorldPosition(shuttleXform);
        return delta.LengthSquared() <= orbit.Range * orbit.Range;
    }

Every component this reads is shared and networked — CEZMapComponent (Content.Shared/_CE/ZLevels/Core/Components/CEZMapComponent.cs:13), CEZTransitMapComponent (CEZTransitMapComponent.cs:13) and WFOrbitLayerComponent — so the client filter and the server jump compute the same answer. Note transit maps deliberately carry CEZTransitMapComponent but NOT CEZMapComponent (CreateTransitMap, CEZLevelsSystem.Transit.cs:820-845), which is why both checks are needed.

`_xformQuery` is private on the upstream partial (SharedShuttleSystem.cs:29) and accessible here because this is the same class; `XformSystem` is protected (:20). Usings: Content.Shared._CE.ZLevels.Core.Components, Content.Shared._WF.PlanetCracker.Planets, System.Numerics, Robust.Shared.GameObjects. This file must stay sandbox-clean: no IO, no reflection, no Process.

Upstream edit — SharedShuttleSystem.cs, inside CanFTLTo (declared line 44). Insert at line 52, i.e. after the blank line following `return true;` (line 50) and before `if (!TryComp<FTLDestinationComponent>(mapUid, out var destination) || !destination.Enabled)`:

        if (!WfAllowFTL(shuttleUid, mapUid)) // WOLFGATE: orbit is the only FTL door in and out of a planet network, and only from within range.
            return false;

followed by a blank line. Placing it AFTER the same-map early-out is required: a shuttle already parked on any layer must still be able to target its own map. Add no usings to the upstream file.

Do not touch MapScreen.xaml.cs, ShuttleMapControl.xaml.cs, ShuttleConsoleSystem, ShuttleConsoleBoundUserInterface or ShuttleConsoleWindow — the destination tree and radar-click jump already do everything the feature needs, and no new BUI message is introduced.

### Stage 4 — prototypes, locale, admin command, dev config
Files: Resources/Prototypes/_WF/PlanetCracker/biomes.yml, Resources/Prototypes/_WF/PlanetCracker/planet_surfaces.yml, Resources/Prototypes/_WF/PlanetCracker/planets.yml, Resources/Prototypes/_WF/PlanetCracker/entities.yml, Resources/Locale/en-US/_WF/planet-cracker/planets.ftl, Content.Server/_WF/PlanetCracker/Planets/Commands/WFPlanetCommand.cs, Content.Shared/_WF/Administration/WolfgateAdminCommands.cs, Resources/ConfigPresets/Build/development.toml
Gate: 1. dotnet build Content.Server/Content.Server.csproj -c Release.
2. Start the headless server against the dev config for ~30 s. The log must contain no prototype, serialisation or ErrorNode entries, and specifically NO 'Unknown prototype type' / 'Failed to index' line mentioning wfPlanetSurface, WFBiomeAsclepiu, WFAsclepiuSurface, WFSurfaceAsclepiu or WFOrbitBeacon. An 'unknown prototype kind wfPlanetSurface' line here is the [Prototype] attribute regression from Stage 1 — fix the attribute, not the YAML.
3. Via the console run `wfplanet spawn WFSurfaceAsclepiu` and confirm it reports five layers built with no errors, then `wfplanet list`, then `wfplanet delete`.
4. grep Resources/Prototypes/_WF/PlanetCracker/planet_surfaces.yml for 'Lizards' and 'Carps' — zero hits — and confirm its temperature line reads 288.15 while planets.yml's networkComponents temperature reads 293.15.
5. Per the fork's YAML validation workflow, lint in Release and treat any ErrorNode as a hard failure.

biomes.yml — biomeTemplate WFBiomeAsclepiu with three !type:BiomeMetaLayer entries over Grasslands (threshold -0.5), MonoOcean (0.25), Snow (0.5), each with `noise: { frequency: 0.001 or 0.01, noiseType: OpenSimplex2, fractalType: FBm, octaves: 2, lacunarity: 2 }`. Copy the exact YAML shape from Resources/Prototypes/_Mono/Planets/biome_ocean.yml:47-76. All three referenced template ids exist (biome_templates.yml:74, biome_ocean.yml:2, biome_templates.yml:241).

planet_surfaces.yml — `- type: planet` id WFAsclepiuSurface exactly as in plan section C. Two things are load-bearing and must not be "tidied":
  (a) `atmosphere.temperature: 288.15`. It MUST differ from the 293.15 in planets.yml's networkComponents. That delta is the only observable difference between the network registry's MapAtmosphere (stamped at MapInit with removeExisting: true, CEZLevelMappingSystem.cs:50) and the builder's post-init SetMapAtmosphere fixup. With both at 293.15 (as an earlier draft had) test 2 passes whether or not the fixup runs, and the whole post-init phase becomes untested.
  (b) `biomeMarkerLayers` is the EIGHT ore layers only: [OreIron, OreQuartz, OreCoal, OreSalt, OreGold, OreSilver, OrePlasma, OreUranium] (ids at Procedural/biome_ore_templates.yml:3, :17, :30, :44, :60, :75, :91, :106). Do NOT add Lizards or Carps or any other mob layer. Marker-spawned entities are tracked only as chunk indices in BiomeComponent.LoadedMarkers (Content.Shared/Parallax/Biomes/BiomeComponent.cs:75) and are never collected by UnloadEntities (BiomeSystem.ChunkLoader.cs:233-274, which walks LoadedEntities only), while z-eyes spawned on every map below a player (CEZLevelsSystem.View.cs:170-210) are ordinary view subscribers that seed marker generation on the ground biome map (BiomeSystem.PlayerTracker.cs:42-61; CanLoad at :65-68 excludes only ghosts, and zEye.yml declares no components). Shipping mob layers would spawn permanent hostile fauna under unattended orbiting ships.
  Also: NO `addedComponents` key at all, unlike DesertWorld (_Mono/Planets/permanent_planet.yml:6-7); an FTLDestination on a mapgrid map makes UpdateFTLArriving:894 disable every arriving hull. AddMarkerLayer does no validation (BiomeSystem.ConfigManager.cs:101-105), so a typo only fails later inside ProtoManager.Index.

planets.yml — `- type: wfPlanetSurface` id WFSurfaceAsclepiu as laid out in the plan. The kind string must match the explicit [Prototype("wfPlanetSurface")] attribute written in Stage 1; if that attribute was left bare the kind is `wFPlanetSurface` and this file will not load. networkComponents must contain ONLY the MapAtmosphere entry (293.15 K); anything else there is stamped over the ground layer's MapLight/Roof because AddComponents defaults to removeExisting: true. Do NOT move MapAtmosphere out of networkComponents into airComponents: CreateTransitMap (CEZLevelsSystem.Transit.cs:831-832) copies only the network registry onto transit maps, so every transit gap would become vacuum.

entities.yml — entity WFOrbitBeacon, `categories: [ HideSpawnMenu ]`, components WarpPoint / FTLSmashImmune / FTLBeacon / IFF (pick a colour distinct from PlanetEntity's #7FB2FF). Copy the block structure from _FarHorizons/Entities/Objects/StarSystem/star_system.yml:14-23.

planets.ftl — every key listed in plan section D, verbatim.

WFPlanetCommand.cs — model it on Content.Server/_WF/Administration/Commands/GridPowerCommand.cs: [AdminCommand(AdminFlags.Server | AdminFlags.Mapping)], sealed partial class : LocalizedEntityCommands, `public override string Command => WolfgateAdminCommands.Planet;`, dependencies WFPlanetRegistrySystem, WFPlanetNetworkSystem, StarSystemMapSystem, IPrototypeManager, IConfigurationManager, TransformSystem, IAdminLogManager. Subcommands list / build / delete / spawn / system / tp per the plan. Refuse everything except `list` with cmd-wfplanet-disabled when wf.planet_networks is false. `spawn <surface id>` builds a standalone network with planetEntity = null at centre Vector2.Zero. `system <starSystem id>` resolves the shell player's attached entity's map, EnsureComp<StarSystemMapComponent> and calls SetSystem. Provide GetCompletion hints from the cmd-wfplanet-hint-* keys. Log high-impact actions through IAdminLogManager as GridPowerCommand does.

WolfgateAdminCommands.cs — add `public const string Planet = "wfplanet";` after ErtBuilderUi.

development.toml — append the three-line `# WOLFGATE` / `[wf]` / `planet_networks = true` block.

All files LF. YAML files get `# WOLFGATE` only where they edit upstream content; wholly-new _WF files need no marker.

### Stage 5 — integration tests
Files: Content.IntegrationTests/Tests/_WF/PlanetCracker/PlanetNetworkTest.cs
Gate: dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt --filter "FullyQualifiedName~PlanetNetworkTest" --logger "console;verbosity=detailed" — all seven tests pass. Expect roughly two minutes including build and pooled server/client startup. Sanity-check the two guard tests actually guard: temporarily comment out the ground SetMapAtmosphere fixup and confirm AtmosphereIsPerLayer fails; temporarily comment out the outbound branch of WfAllowFTL and confirm CannotFTLOffASurfaceLayer fails. Restore both. If a run fails with a bare db.ef line and no assertion message, reproduce against Content.IntegrationTests/Tests/Shuttle/DockTest.cs first before blaming this fixture.

One new fixture file, no other file touched. Open with `#nullable enable` (the csproj sets <Nullable>disable</Nullable>). namespace Content.IntegrationTests.Tests._WF.PlanetCracker; [TestFixture][TestOf(typeof(WFPlanetNetworkSystem))].

House style, from Content.IntegrationTests/Tests/_WF/Power/SynthRechargeTest.cs: `await using var pair = await PoolManager.GetServerClient();`, static async helpers taking TestPair, server.WaitPost for mutation, server.WaitRunTicks between phases, server.WaitAssertion for asserts, `await pair.CleanReturnAsync();` last.

Shared helpers:
- `EnableFeature(pair)`: inside server.WaitPost, `pair.Server.CfgMan.SetCVar("wf.planet_networks", true)`. TestPair records and reverts CVar changes on return, so no cleanup is needed. Do NOT touch net.tickrate — it is a process-wide DefaultCvar shared with the second concurrently-running pair (AssemblyInfo.cs:8 sets LevelOfParallelism(2)).
- `BuildStack(pair)`: server.System<WFPlanetNetworkSystem>().BuildNetwork(proto WFSurfaceAsclepiu, Vector2.Zero, "Asclepiu", null) inside WaitPost; run 1 tick; return the network uid and its WFPlanetNetworkComponent layers.
- `SpawnShip(pair, layer)`: mapManager.CreateGridEntity on that layer's MapId, mapSystem.SetTiles for a 3x3 block, then EnsureComp<ShuttleComponent> and ShuttleSystem.Enable(grid, force: true) so the body is Dynamic — a fresh CreateGridEntity grid is BodyType.Static (PhysicsComponent.Physics.cs:68) and the fall gate skips Static at Gravity.cs:93, which would make the orbit test pass for the wrong reason. Run 2 ticks so fixtures and mass settle.
- `PastGrace = pair.SecondsToTicks(4f)` — never a hard-coded 90. Grace is 3 s (CEZGridFallerComponent.cs:44), the sweep is 0.5 s (Gravity.cs:42), default tick 30 Hz.
- `Teardown(pair, network)`: WFPlanetNetworkSystem.DeleteNetwork inside WaitPost, then 5 ticks so CleanupOrphanedTransitMaps sweeps any transit maps a falling test created. Always call it before CleanReturnAsync; the pair is pooled.

Tests:
1. BuildsTheFullStack — five layers; each has CEZMapComponent with the right Depth and NetworkUid; zLevels.TryMapUp(ground) and TryMapDown(orbit) both resolve; ground has MapGridComponent, BiomeComponent and MapLightComponent; orbit has NEITHER MapGridComponent NOR MapLightComponent, and does have WFOrbitLayerComponent and an enabled FTLDestinationComponent with RequireCoordinateDisk false and BeaconsOnly false.
2. AtmosphereIsPerLayer — MapAtmosphereComponent.Space is false on ground and on both air layers and true on orbit; AND the ground layer's mixture TEMPERATURE is 288.15f (within tolerance), not the network registry's 293.15f. The temperature assertion is mandatory: the moles are identical between the two mixtures by design (both derive from zmaps.yml:12-18), so asserting moles alone passes whether or not the post-init SetMapAtmosphere fixup ran, which is the exact bug this test exists to catch.
3. GridOnOrbitLayerDoesNotFall — ship on the orbit layer; precondition-assert HasComp<CEZGridFallerComponent>; run PastGrace; assert MapUid is still the orbit map and has no CEZTransitMapComponent.
4. GridOnAirLayerFallsWithoutGravgen — the control for #3: same ship on depth 1, run PastGrace, assert its MapUid now has CEZTransitMapComponent.
5. OrbitIsReachableOnlyInRange — build a throwaway sector map, spawn a PlanetEntity on it, BuildNetwork bound to that entity, spawn a shuttle with a powered FTLDriveComponent on the sector map at 500 then at 5000 from the planet, and assert SharedShuttleSystem.CanFTLTo(shuttle, orbitMapId, console) flips true then false. Third case: a shuttle placed on the orbit map itself returns true via the same-map early-out.
6. CannotFTLOffASurfaceLayer — the control for the outbound half of the gate, and the only test that fails if the WfAllowFTL outbound branch is dropped. Reusing the sector-map fixture from #5: place the shuttle on the GROUND layer and assert CanFTLTo(shuttle, sectorMapId, console) is FALSE; place it on an AIR layer and assert FALSE; place it on the ORBIT layer and assert TRUE. (Leaving orbit for the sector map must stay possible — that is the 'Leave orbit' jump.)
7. DeleteNetworkRemovesEveryLayer — after DeleteNetwork and 5 ticks, every layer and the network entity are Deleted or Terminating, and WFSectorPlanetComponent.Network is null.

Do NOT add a 'grid rests on the ground layer' test: biome chunk loading runs from ProcessPlayerChunkRequests over sessions and their view subscribers (BiomeSystem.PlayerTracker.cs:22-62), so with no attached player and no z-eyes there are no tiles under the hull and HasGroundUnderFootprint is false. Use Assert.Multiple for grouped asserts. Never Assert.Warn and never an Ignore that emits a warning — CI runs NUnit.MapWarningTo=Failed (.github/workflows/build-test-debug.yml:59).


## TESTS
- PlanetNetworkTest.BuildsTheFullStack — five layers at depths 0..4 with correct CEZMapComponent Depth/NetworkUid; TryMapUp(ground) and TryMapDown(orbit) resolve; ground has MapGridComponent + BiomeComponent + MapLightComponent; orbit has neither MapGridComponent nor MapLightComponent but does have WFOrbitLayerComponent and an enabled FTLDestinationComponent with RequireCoordinateDisk false and BeaconsOnly false.
- PlanetNetworkTest.AtmosphereIsPerLayer — MapAtmosphereComponent.Space false on ground and both air layers, true on orbit; AND ground's mixture temperature is 288.15, not the network registry's 293.15. The temperature assertion (not the moles, which are identical by design) is the regression guard on the build order, since CEZLevelMappingSystem.cs:50 stamps the registry with removeExisting: true at MapInit and the post-init SetMapAtmosphere fixup is the only thing that undoes it.
- PlanetNetworkTest.GridOnOrbitLayerDoesNotFall — a Dynamic shuttle grid on the orbit layer still has that map as its MapUid after 4 s and its map has no CEZTransitMapComponent. This is the test the fall-gate exemption exists for; without the upstream Gravity.cs edit it fails.
- PlanetNetworkTest.GridOnAirLayerFallsWithoutGravgen — the control: the same grid on depth 1 acquires a CEZTransitMapComponent parent within 4 s. Without it, the orbit test can pass for the wrong reason (a Static body, or a grid with no CEZGridFallerComponent).
- PlanetNetworkTest.OrbitIsReachableOnlyInRange — the inbound half of the gate. CanFTLTo returns true for a shuttle 500 from the sector planet, false at 5000 with orbitRange 2000, and true for a shuttle already on the orbit map (same-map early-out). Covers both the client destination-list filter and the server jump choke point, since both go through this one function.
- PlanetNetworkTest.CannotFTLOffASurfaceLayer — the outbound half of the gate, new in revision 2. CanFTLTo(shuttle, sectorMapId, console) is false for a shuttle on the ground layer and on an air layer, and true for one on the orbit layer. Without the outbound branch of WfAllowFTL a hull can jump straight off the grass to the sector map, bypassing the ascent entirely; nothing else in the fork prevents it (no landed state per GroundFriction.cs:34-37, the sector map is an open destination per ShuttleSystem.FasterThanLight.cs:137, and FTLFree's distance test is ~0 from any layer).
- PlanetNetworkTest.DeleteNetworkRemovesEveryLayer — after DeleteNetwork every layer map and the network entity are deleted or terminating, and WFSectorPlanetComponent.Network is cleared. Guards against leaking maps into the pooled TestPair, which runs two pairs concurrently.

## OPEN RISKS
- Z-eyes drive biome generation, and F0 only blunts it. Confirmed: BiomeSystem.ProcessPlayerChunkRequests loads chunks and marker chunks for every entry of pSession.ViewSubscriptions (BiomeSystem.PlayerTracker.cs:42-61), CanLoad excludes only ghosts (:65-68), and CEZLevelsSystem spawns a view-subscribed CEZLevelEye on every map below a viewer up to MaxZLevelsBelowRendering = 10 (View.cs:170-176, :206, :210) with zEye.yml declaring no components at all. So a ship parked in Asclepiu orbit generates terrain and ore on the ground biome map beneath it with nobody on the surface. F0 removes the mob marker layers, which fixes the unbounded half (marker entities are recorded only as chunk indices in BiomeComponent.LoadedMarkers:75 and are never collected by UnloadEntities, ChunkLoader.cs:233-274). Terrain and ore remain and are bounded (16-tile _loadArea, BiomeSystem.cs:49/:65; one-shot per marker chunk). Any later feature that reintroduces fauna must add the guard first — two lines in the viewer branch, see plan section Z.1.
- Biome chunk unloading vs support. HasGroundUnderFootprint (Gravity.cs:624) reads live tiles, and UnloadTiles (BiomeSystem.ChunkLoader.cs:294) writes Tile.Empty back for unmodified tiles roughly 10 s after the last viewer leaves. A ship parked on the Asclepiu surface with its crew back in orbit can therefore have the ground vanish and fall through the ground layer — though note the orbiting crew's own z-eye keeps the chunk under them active, so the failure is position-dependent and will be intermittent. F0 does not address this; the fix when a landing feature needs it is BiomeSystem.ReserveTiles over the landing footprint at touchdown (the trick PlanetSystem.LoadPlanet:69 already uses) or ForceAnchor on the landed hull. Note also that UnloadChunks `return`s rather than `continue`s after unloading the first chunk (ChunkLoader.cs:307-321), so only one chunk unloads per sweep.
- TryAddMapsIntoNetwork is not atomic: it logs an error and returns false on a duplicate depth or an already-networked map, but still writes ZLevels[depth] and ZLevelByEntity[map] (Maps.cs:47-60, the guards do not `continue`). The builder checks the return value and unwinds, but a partially-corrupted network could still exist for the duration of that call. There is no upstream fix short of editing _CE.
- MapAbove is never back-wired onto an existing lower map. TryAddMapsIntoNetwork:71-75 only sets the NEW map's links from neighbours already present, so after an ascending build every layer's MapBelow is set but every layer's MapAbove is null. Traversal works only because MapOffset falls back to the network's ZLevels dictionary (CESharedZLevelsSystem.Maps.cs:187-195). Any future code that reads CEZMapComponent.MapAbove directly will see null on a planet network and behave differently from a station-built one.
- Five layers per planet means up to five CEZLevelEyes and five PVS sets per player on that network (View.cs:170-201), and MaxZLevelsBelowRendering is a public static MUTABLE field (CESharedZLevelsSystem.Constants.cs:10) whose apparent CVar is never read. Only Asclepiu ships a surface, so the round cost is bounded, but building networks for all five Kyphrus bodies would be a real load change with no precedent in the fork.
- The FTL gate lives in shared CanFTLTo, so client and server must resolve the planet entity identically. The PlanetEntity is PVS-global-overridden (StarSystemMapSystem.cs:65) so the client normally has it, but if it ever is not replicated the inbound gate returns false client-side while the server would allow the jump — and ConsoleFTL rejects silently on almost every path (FTL.cs:148, :154, :164, :169, all with no popup). A divergence reads to the player as a dead button, not an error. The outbound half has no such dependency: it reads only components on the shuttle's own map.
- The outbound gate makes a hull with a dead gravgen unable to leave a planet layer by any means. By design a ship only reaches a layer by descending, which requires a gravgen (PilotControl.cs:190), so it should be able to ascend again — but a gravgen destroyed while landed strands the hull permanently, with the console silently listing no destinations (ConsoleFTL rejects with no popup, FTL.cs:145-149). Watch for this in playtest; the escape hatch is an admin `wfplanet tp` or relaxing the gate for hulls with no functioning drive.
- GetBeacons (ShuttleConsoleSystem.FTL.cs:93) is a global, unfiltered AllEntityQuery with no PVS or range check, and its results go into every console's BUI state. The WFOrbitBeacon therefore leaks the existence and coordinates of every built orbit layer to every ship in the sector regardless of the CanFTLTo gate. Acceptable while sector planets are public beacons; a problem if orbit layers ever need to be secret.
- Position matching in RegisterPlanet relies on float equality between two evaluations of Vector2(cos(angle), sin(angle)) * distance. The expression is identical to SharedStarSystemMapSystem.cs:26 so it is bit-exact today, but any refactor of that line silently breaks registration. The name fallback and the error log are the mitigation; a cleaner fix would be a _WF partial DataField on StarSystemPlanet, which is available because the type is sealed partial.
- SetSystem is reachable from two places (StarSystemMapSystem.cs:31 via PostGameMapLoad and StarSystemRuleSystem.cs:19 via the rule) and is not idempotent — it calls SpawnEntities unconditionally. The rule guards by checking map.System != null, so today only one path runs, but a map file that sets System while a preset also carries StarSystemKyphrus would spawn duplicate planets and duplicate networks. RegisterPlanet does not defend against this.
- In the default dev environment no sector planet exists at all: development.toml:3 disables the lobby, which forces the sandbox preset (GameTicker.GamePreset.cs:109), and game_presets.yml:109-117 carries only the Sandbox rule, so StarSystemKyphrus never runs. No map under Resources/Maps sets StarSystemMap either. The feature is therefore only reachable in dev through `wfplanet system` or `wfplanet spawn`, and the round-start path is exercised for the first time only on a Mono preset.
- ShuttleSystem.OnStationPostInit adds an FTLDestination to the map of every station grid (ShuttleSystem.FasterThanLight.cs:137), and CEZLevelsSystem.OnStationPostInit adds z-level maps carrying MapGridComponent to the station (CEZLevelsSystem.cs:77, :96) — both on StationPostInitEvent with no declared ordering. F0 deliberately builds planet networks outside the station path, but if a later feature routes one through CEStationZLevelsComponent, the Asclepiu surface would silently become a public FTL destination (and the outbound gate would then be the only thing stopping hulls jumping off it, while arriving hulls would still be disabled by UpdateFTLArriving:894).
- The outbound gate refuses FTL from ANY CEZMapComponent map that is not a WFOrbitLayer, which includes station z-network layers if a later feature ever builds one via CEStationZLevelsComponent. Today nothing does (no prototype in Resources carries that component), so the gate is inert outside planet networks, but a station z-network would silently become FTL-locked on every layer. If that feature lands, narrow the outbound check to maps whose network carries WFPlanetNetworkComponent.
- FTLFree rejects a jump outright when any grid sits within GetFTLBufferRange + 20 of the target (SharedShuttleSystem.cs:266-279). Because the orbit layer is not beacons-only, players can pick an empty spot and this rarely bites — but a ship parked exactly on the WFOrbitBeacon will block anyone who clicks the beacon, with no feedback. If playtesting shows this, add two or three offset beacons rather than switching to BeaconsOnly.
- CEZPhysicsComponent.CurrentZLevel is written only at component map-init (CESharedZLevelsSystem.Activation.cs:39-43) and on CEZLevelMapMoveEvent (Movement.cs:153). FTL raises neither, so a grid that already carries CEZPhysicsComponent from one planet network and jumps into another keeps a stale depth cache. The console altimeter is safe because GetAbsoluteAltitude reads the map's Depth (Movement.cs:92), but the flight systems read the cache.
- CEZGridConnectorSystem marks itself dirty on every TileChangedEvent and GridSplitEvent anywhere in the world (CEZGridConnectorSystem.cs:105, :110) and then re-floods every connector, ending in RevalidateTransitConvoys. A biome map generating chunks writes a great many tiles, and per the first risk above those writes now happen under orbiting ships too. This is a pre-existing interaction, not one F0 creates, but a planet surface is the first thing in the fork that will trigger it at scale.
- The entityMask ore layers are silent no-ops on any template that plants no WallRock*/AsteroidRock entities. MonoOcean plants none, so the ocean meta-layer of WFBiomeAsclepiu will produce no ore at all; only the Grasslands and Snow portions will. Probably desirable, worth confirming in playtest rather than assuming.
