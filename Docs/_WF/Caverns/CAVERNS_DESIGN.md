# Caverns: design

Design for the `Caverns` module on `planet-caverns`. It starts from the traversal design (the judges' pick), grafts the
best ideas of the geology and ecology designs onto it, and fixes every flaw the judges found. Engine claims were
re-checked against this branch; section 2.1 lists them with file references. Where this document and a candidate
design disagree, this document wins.

## 1. Concept

Every planet network gets a cavern at depth −1: a biome-backed map built with the network and deleted with it,
roofed by the ground above and dark except where a shaft lets daylight in or the rock itself glows. Each of the six
worlds has its own underground, with its own rock, ore, air, light, hazards, fauna and sound:

- Asclepiu: the Underkarst, wet limestone with plunge pools and glow-worms.
- Fervidus: the Cinder Vaults, basalt cut by lava tubes and magma chambers.
- Merak: the Sandstone Galleries, cool pillared halls under a 45 °C desert.
- Aerumna: the Umbral Deeps, lightless chromite with shadow groves, pink geodes and xenos.
- Thrascias: the Rime Galleries, ice halls with plasma lakes.
- Carcinoma: the Gut, flesh throats and stomachs grown over a mineral world.

You get down by walking into a cave mouth, which is a real, pinned hole in the ground, and taking a small fall. You
can also climb down beside it. You get back up by climbing the steps, rope or roots at the foot of the shaft, with no
equipment. Mouths are wherever players go: the ground is split into cells about 96 tiles across, and each cell's
mouth is claimed just ahead of the terrain streaming in. Every world also has a gate mouth near its centre, and any
hole a shovel or an explosion opens in the ground becomes a way down too.

The reason to go down is ore. Cavern rock is solid and veined, far richer than the surface's scattered boulders, and
some ore exists only below. Hazards warn before they strike: vents hiss, unstable rock is visibly cracked, the ceiling
groans before it falls, and the deep stirs before it wakes. Ships never go below ground.

## 2. Architecture

### 2.1 Engine facts this design relies on

Checked in code on this branch. Line numbers are approximate.

1. **Build order.** `WFPlanetNetworkSystem.BuildNetwork` (`Content.Server/_WF/Planets/WFPlanetNetworkSystem.cs`) does
   the following, in order:
   - spawns the ground from a `planet` prototype with `runMapInit: false` and sets its seed;
   - raises `WFPlanetGroundSpawnedEvent`;
   - creates the air and orbit maps;
   - calls `CreateMapNetwork`;
   - adds depths 0..N+1, one map per `TryAddMapsIntoNetwork` call;
   - calls `InitializeZNetwork`;
   - stamps `WFPlanetLayerComponent` on each layer;
   - calls `WFPlanetWeatherSystem.Configure`.
2. **Depth −1 must be added after depth 0.** In `QuickApiCache`
   (`Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.Maps.cs:127-176`), adding −1 first gives `SortedMin = -1`. A
   later 0 then writes `list[0 - (-1)]` on a list of length 1 and throws. Added after 0, −1 is inserted at the front.
   The ground's `MapBelow` cache stays null and `MapOffset` falls back to
   `ZLevels` (`CESharedZLevelsSystem.Maps.cs:153-195`). `GetAllMapsBelow` reads `SortedZLevels` (`Maps.cs:130`),
   which is correct only with this order.
3. **Falling.** `ComputeGroundHeightInternal` (`CESharedZLevelsSystem.Movement.cs:165-253`, `maxFloors = 1`) returns
   0 for a solid tile, −1 for a solid tile one level down, and −1 when it finds nothing. `ProcessZPhysics`
   (`CESharedZLevelsSystem.Update.cs:69-176`) calls `TryMoveDown` once `LocalPosition < 0`.
   - On the bottom layer `TryMoveDown` fails and the body comes to rest at −1: a level below the floor, in rock or in
     unloaded void. An unprepared hole is therefore a "stuck in the floor" bug.
   - It is not a ~300 Blunt death, as two candidate designs claimed: the impact from about two levels is ~28 Blunt.
4. **Only active bodies are simulated** (`CESharedZLevelsSystem.Activation.cs`). A body sleeps after 2 s at rest.
   `OnTileChanged` (`Movement.cs:106`) recaches ground height without waking the body. A sleeping item over a tile that
   was emptied by an unload does not fall. An awake one, such as a wandering animal, does.
5. **Fall damage** (`Content.Shared/_CE/ZLevels/Damage/CEZLevelDamageSystem.cs:56-128`):
   - A one-level fall hits at about 4.3 m/s. Damage is `(int)(v² × 0.75 × landing tile FallDamageMultiplier)`, which
     is 13 Blunt on a ×1 tile.
   - Knockdown is `v² × 0.1 × FallStunMultiplier`, capped at 5 s.
   - Anyone standing on the landing spot takes `v² × 0.4`, about 7 Blunt.
   - Planet gravity does not scale falls: `GravityMultiplier` comes only from the jetpack and flyer
     `CECheckGravityEvent` handlers.
   - `FloorWater` already has `fallDamageMultiplier: 0`.
6. **Anchoring onto an empty tile is refused** (`SharedMapSystem.Grid.cs:1285`, `AddToSnapGridCell`). Anything that
   marks a hole is therefore unanchored.
7. **Chunk loading** (`Content.Server/Parallax/BiomeSystem*.cs`):
   - Chunks load around attached non-ghost players and every entity in their `ViewSubscriptions` (`PlayerTracker.cs`).
   - The load area is `ceil(net.pvs_range / 8) × 8` = ±32 tiles, chunk-aligned, so up to about 40 tiles out.
   - One chunk per biome unloads every 10 s.
   - `UnloadTiles` keeps only modified tiles and tiles holding an entity anchored to *that grid*. Ground under a
     parked hull therefore empties (`ChunkLoader.cs:~245`).
   - `LoadedChunks.Remove` runs after `UnloadTiles` has emptied the tiles (`ChunkLoader.cs:~176-190`).
   - Modified (pinned) tiles skip tile, entity and decal generation on load.
   - `ReserveTiles` works on unloaded areas (`PlanetSetup.cs:76`).
   - Chunk loads raise `TileChangedEvent`.
8. **Biome evaluation** (`Content.Shared/Parallax/Biomes/SharedBiomeSystem.cs:114-300`):
   - Layers are evaluated last to first, and the first match wins.
   - A `BiomeMetaLayer` recurses into its template and supports `invert`.
   - Entity layers check `allowedTiles` against the tile already chosen.
   - Direct entity-layer spawns are tracked in `LoadedEntities`: untouched ones unload, mined ones mark the tile
     modified.
   - Marker-layer spawns pin their tile and never unload.
   - `GetNoise` allocates and serialiser-copies a noise object on every call, about 3 µs, so pure sampling costs
     microseconds per layer per tile.
9. **Eyes load chunks.** `UpdateViewer` (`Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.View.cs:147-199`) spawns a
   `CEZLevelEye` on every map below the viewer, up to 10 of them, and one above. The eye has no `GhostComponent`, so
   biome chunks load around it. Without a change, every ground, air and orbit viewer, ghosts included, would generate
   the cavern under them. The client already refuses to render below a `CEZGroundLayer` map.
10. **Roof** (`Content.Shared/_CE/ZLevels/Roof/CESharedZLevelsRoofSystem.cs:56-93`):
    - Ground tile changes propagate a roof bit to every map below. `Space` is `transparent`, so a hole is an unroofed
      shaft.
    - `RoofComponent.Color` defaults to black.
    - `CEZLevelMappingSystem.OnMapInit` re-adds the network components, `MapAtmosphere` included, at
      `InitializeZNetwork`.
    - `EnsurePlanet` adds `LightCycle`, `SunShadow`, `SunShadowCycle`, `Parallax`, `Roof`, `MapLight` and `Gravity`
      (`PlanetSetup.cs:25-70`).
11. **Hull support.**
    - `HasGroundUnderFootprint` (`CEZLevelsSystem.Gravity.cs:664`) needs one non-empty tile under the hull's AABB.
    - An unsupported hull calls `TryEnterTransit` (`Gravity.cs:118`), which descends when `TryMapDown` succeeds
      (`Transit.cs:433`).
    - A descending convoy hops gaps down through `TryHopConvoyGap` (`Transit.cs:586-597`, `722`).
    - Grid z-physics can hop a hull a level through `TryMoveGrid` (`Transit.cs:102-113`); grids are active z-bodies.
    - Pilots descend at `PilotControl.cs:217`.
    - Today every one of these fails at the ground, because nothing exists below it. With a cavern, a parked hull
      whose ground chunk unloads would sink into it.
12. **Orbital falls.** `WFOrbitalMobFallSystem.IsSurfaceImpact` only accepts `MapUid == Ground`. An orbital faller who
    drops through a mouth takes the full accumulated fall, up to 20² × 0.75 = 300 Blunt.
13. **Planet control.** `PlanetControlSystem`'s gravity loop covers `network.Layers` only
    (`PlanetControlSystem.cs:121`).
14. **Tiles.**
    - `FloorCave`, `FloorCaveDrought`, `FloorChromite`, `FloorBedrock` and `FloorAsteroidSand` have base turf `Space`
      and are not indestructible.
    - `FloorAsteroidSandPlanet` (Merak) is indestructible but diggable to `Space`.
    - `FloorSnow` digs to `FloorDirt`, then `FloorBedrock`. Bedrock has no tools, so a shovel stops there, but it can
      explode to `Space`.
    - `FloorFlesh` pries to `Plating`.
    - `FloorBasalt`, `FloorIce`, `FloorWater` and `FloorSnowDug` are indestructible with no tools.
    - Explosions skip `Indestructible` (`ExplosionSystem.Processing.cs:522`).
    - `ContentTileDefinition` inherits through `parent` (precedent: `WFFloorFlesh`).
15. **Existing subscriptions.**
    - `CEZLevelFallMapEvent` is subscribed on `CEZPhysicsComponent` (`View.cs`), `MobStateComponent`
      (`WFOrbitalMobFallSystem`) and `WFParachutedComponent`.
    - `TileChangedEvent` is subscribed directed on `CEZMapComponent`, `CEZLevelRoofComponent` and `ShuttleComponent`.
    - `GatheredEvent` is subscribed on `OreVeinComponent` (`MiningSystem`).
    - There is no cancellable pre-move event for non-grid entities.
16. **Guidebook.** The upstream `Salvage` guide entry is commented out (`Resources/Prototypes/Guidebook/cargo.yml:16`),
    so the cavern guide hangs under `Expeditions` (`Resources/Prototypes/_NF/Guidebook/expeditions.yml`).
17. **Z-flight.** Only `moth` and `harpy` carry `CEZFlyer`. Jetpacks are off on every planet layer that isn't orbit
    (`SharedJetpackSystem.WfInAtmosphere`).

### 2.2 Planets hooks

These hooks are generic and nothing in them names Caverns. Files under `_WF/Planets` need no marker. The four CE
lines outside `_WF` are single-line edits marked `WOLFGATE(Planets)`.

**New events** (`Content.Server/_WF/Planets/`):

```csharp
/// <summary>Raised broadcast before a planet's depths are added; handlers append uninitialised maps.</summary>
// Lower[i] becomes depth -(i + 1), so handlers add nearest first.
[ByRefEvent]
public readonly record struct WFPlanetLowerLayersEvent(
    EntityUid Ground, WFPlanetSurfacePrototype Surface, Vector2 Centre, List<EntityUid> Lower);

/// <summary>Raised broadcast last in BuildNetwork, after InitializeZNetwork re-stamped network components.</summary>
[ByRefEvent]
public readonly record struct WFPlanetNetworkBuiltEvent(
    EntityUid Network, EntityUid Ground, WFPlanetSurfacePrototype Surface, IReadOnlyList<EntityUid> Lower);
```

**`BuildNetwork` changes** (`WFPlanetNetworkSystem.cs`), in this order:

1. After `layers.Add(orbit)` and before `CreateMapNetwork`, create `var lower = new List<EntityUid>()` and raise
   `WFPlanetLowerLayersEvent`.
2. The existing upward-loop unwind also calls `QueueDel` on every map in `lower`.
3. After the upward loop, add each lower map in its own call:
   `TryAddMapsIntoNetwork(network, {lower[i], -(i + 1)})`. Put this comment above the loop:
   `// Below ground only once depth 0 exists: QuickApiCache indexes past its list if -1 arrives before 0.`
   On failure, unwind the same way as the upward loop.
4. The `WFPlanetLayerComponent` stamp loop runs over `layers.Concat(lower)`: same network, gravity and caps. This
   gives the jetpack cut-out, biomass rules and music suppression below ground.
   - Ambience, `GroundComponents`, weather and daylight stay on `Layers` only.
5. Set `comp.LowerLayers = lower`.
6. Raise `WFPlanetNetworkBuiltEvent` as the last statement before `return`.

**`WFPlanetNetworkComponent.LowerLayers`**: `List<EntityUid>`, documented as "member maps below ground, nearest first;
never in `Layers`". `Layers` stays ground-first and orbit-last, so `PlanetNetworkTest`'s `Count == 5` holds.

**`DeleteNetwork`**: the transit sweep matches `comp.Layers.Contains(x) || comp.LowerLayers.Contains(x)` for both
`LowerMap` and `UpperMap`. `DeleteMapNetwork` already deletes every `ZLevels` map.

**`PlanetControlSystem`**: the gravity loop covers `network.Layers.Concat(network.LowerLayers)`.

**`WFOrbitalMobFallSystem.IsSurfaceImpact`** (F2): also accept a map with `CEZMapComponent.Depth < 0` and
`WFPlanetLayerComponent`. An orbital faller who lands in a cavern is then maimed, not killed.

**Hull guard** (F1). Add these to `Content.Server/_WF/Planets/CEZLevelsSystem.Wolfgate.cs`:

```csharp
/// <summary>True for a planet layer below its ground; hulls never enter one.</summary>
private bool WfClosedToHulls(Entity<CEZMapComponent> map) => map.Comp.Depth < 0 && HasComp<WFPlanetLayerComponent>(map);

/// <summary>True on an orbit layer (left through transit) or when the hop would take the hull below ground.</summary>
private bool WfRefusesLevelHop(EntityUid grid, Entity<CEZMapComponent> target)
    => WfIsOrbitLayer(Transform(grid).MapUid ?? EntityUid.Invalid) || WfClosedToHulls(target);
```

These are the four marked CE lines. Each keeps the upstream expression visible and appends the guard:

| File:line | New line (marker at end) |
|---|---|
| `CEZLevelsSystem.Transit.cs:106` | `if (WfRefusesLevelHop(grid, (targetMap.Owner, targetMap.Comp1))) // WOLFGATE(Planets): hulls leave orbit through transit and never hop below ground.` (replaces the existing marked line) |
| `CEZLevelsSystem.Transit.cs:433` | `var hasBelow = TryMapDown(layers[0].SourceMap, out var wfBelow) && !WfClosedToHulls(wfBelow); // WOLFGATE(Planets): hulls never descend below a planet's ground.` |
| `CEZLevelsSystem.Transit.cs:722` | `if (old.LowerMap is not { } newUpper \|\| !TryMapDown(newUpper, out var below) \|\| WfClosedToHulls(below)) // WOLFGATE(Planets): a descending convoy lands on the ground instead of hopping below it.` |
| `CEZLevelsSystem.PilotControl.cs:217` | `if (down && (grounded \|\| !TryMapDown(mapUid.Value, out var wfBelow) \|\| WfClosedToHulls(wfBelow))) // WOLFGATE(Planets): pilots can't descend below a planet's ground.` |

With the guard, every hull path behaves at the ground exactly as it does today:
- an unsupported hull churns up and back;
- a descending convoy lands;
- a pilot's descend is a no-op.

Nothing in flight code treats depth −1 as ground: `WFLiftoff.cs:195`, `WFFlightSystem.cs:427`,
`WFFlightAmbienceSystem.cs:120` and `WFPlanetDragSystem.cs:55` all test `Depth == 0 || CEZGroundLayer`, and hulls never
reach −1 anyway.

**Planets README**: add the two events, `LowerLayers` and `WfClosedToHulls` to the "Other modules build on it through"
line.

### 2.3 Other edits outside `_WF`

- **Eye cap** (F1), in `Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.View.cs`, `UpdateViewer`. It is required, not
  optional (verified in 2.1 item 9). There are three marked lines:

  ```csharp
  var wfAbove = map.Value; // WOLFGATE(Caverns): the level above the next eye, for the ground cap below.
  for (var i = 1; i <= MaxZLevelsBelowRendering; i++)
  {
      if (HasComp<CEZGroundLayerComponent>(wfAbove)) // WOLFGATE(Caverns): no eyes or chunk loads under a ground layer.
          break;

      if (!TryMapOffset(map.Value, -i, out var mapUidBelow))
          break;

      SpawnViewerEye(eyes, actor, map.Value, mapUidBelow, globalPos, pvsScale);
      coveredMaps.Add(mapUidBelow);
      wfAbove = mapUidBelow; // WOLFGATE(Caverns)
  }
  ```

  How this affects each kind of viewer:
  - **Ground viewer:** gets no eye below.
  - **Air viewer:** keeps its eye on the ground and stops there.
  - **Cavern viewer:** has nothing below. It keeps its eye on the ground above, so the ground over it stays loaded
    and roofs it.
  - **Networks without a map below ground:** nothing changes.
- **`Resources/ConfigPresets/Build/development.toml`** (F1): inside the existing `[wf]` table, which sits inside the
  `WOLFGATE(Planets)` block, add `caverns = true` with `# WOLFGATE(Caverns): caverns are on in development builds.` on
  the line above. TOML forbids a second `[wf]` header, so the line has to live in that block. A single-line marker
  inside a block passes `modules.py`.
- **`Resources/Prototypes/_NF/Guidebook/expeditions.yml`** (F6): add
  `  - WFCaverns # WOLFGATE(Caverns): cavern field guide` to `Expeditions`' children.

Edits inside other `_WF` modules need no marker:
- `WolfgateAdminCommands.Cavern = "wfcavern"` (F2).
- `Content.Shared/_WF/CCVar/CavernCVars.cs`, with one clause added to the CCVar README overview (F1).

### 2.4 Caverns prototypes

`wfCavern` (`Content.Shared/_WF/Caverns/WFCavernPrototype.cs`), one per surface, id `WFCavern<World>`:

| Field | Type | Meaning |
|---|---|---|
| `surface` | `ProtoId<WFPlanetSurfacePrototype>`, required | The lookup key; a test enforces exactly one per surface |
| `name` | `LocId`, required | Shown in examines: `wf-cavern-<world>-name` |
| `level` | `ProtoId<PlanetPrototype>`, required | The existing DV `planet` kind (biome, `mapName`, `mapLight`, `atmosphere`), spawned with `PlanetSystem.SpawnPlanet(runMapInit: false)` |
| `seedOffset` | `int` | Cavern seed = `surface.Seed + seedOffset` (unchecked) |
| `roofColor` | `Color` | `RoofComponent.Color`, the ambient under solid ground |
| `shaftLight` | `float`, default 0.5 | Cavern `MapLight` = the ground's current `MapLight` × this (F4), so shafts dim at night |
| `arrival` | `LocId` | Popup shown on entering the cavern (F4) |
| `mouths` | `WFCavernMouthSpec` (F2) | See below |
| `ambience` | `WFCavernAmbienceSpec` (F4) | `loops`, `oneShots` (`SoundSpecifier` lists), `loopVolume` (−16), `oneShotVolume` (−10), `minInterval` (25), `maxInterval` (70), `crossfadeSeconds` (4) |
| `hazards` | `WFCavernHazardSpec?` (F5) | `caveInChance`, `caveInDamage` (`DamageSpecifier`), `rubble`, `disturbanceThreshold` (60), `disturbanceDecay` (3/min), `awakeningCooldown` (1200 s), `deepTable` (`ProtoId<EntityTablePrototype>`) |

`WFCavernMouthSpec`:

| Field | Default | Meaning |
|---|---|---|
| `cellSize` | 96 | Tiles per mouth cell |
| `holeSize` | 2 | 1 or 2; mouths are 1×1 or 2×2 squares |
| `gateOffset` | (0, 24) | Gate candidate relative to the planet centre |
| `groundTiles` | required | Natural ground tiles a mouth may cut |
| `avoid` | empty | Natural ground entities a footprint must not touch (liquids, boulders) |
| `landingTile` | required | Cavern tile under the hole |
| `padRadius` | 3 | Pinned, rock-free pad around the hole in the cavern |
| `climbSide` | South | Side of the hole whose lip holds the climb point |
| `shade` | required | Unanchored pit entity over each hole tile |
| `climbPoint` | required | Anchored climb entity in the cavern under the lip |
| `rim` | empty | Decor anchored on the lip's corner tiles |
| `climbSeconds` | 4 | Base climb-up time, × `clamp(surface gravity, 1, 2.5)` |

The ambience is not a `wfPlanetAmbience`, because `PlanetAmbiencePrototypeTest` asserts exactly six of those. There is
no `levels` list: v1 is −1 only, and the Planets event list already supports a −2 later ("the Heart" under
Carcinoma is the natural candidate).

Example (F2 shape):

```yaml
- type: wfCavern
  id: WFCavernFervidus
  surface: WFSurfaceFervidus
  name: wf-cavern-fervidus-name
  level: WFCavernFervidusLevel
  seedOffset: 7919
  roofColor: "#1c0803"
  shaftLight: 0.45
  arrival: wf-cavern-fervidus-arrival
  mouths:
    holeSize: 2
    groundTiles: [FloorBasalt]
    avoid: [FloorLavaEntity]
    landingTile: WFCavernFloorAsh
    shade: WFCavernShadeFervidus
    climbPoint: WFCavernClimbFervidus
    rim: [BasaltOne, BasaltThree]
```

### 2.5 Components

| Component | Side | On | Fields |
|---|---|---|---|
| `WFCavernLayerComponent` | Shared, networked | cavern map | `Cavern` (proto id); server-only `Ground` |
| `WFCavernShaftComponent` | Shared, networked | shade entities | `Cavern` (proto id), `Air` (`WFCavernAir`), `LandingMultiplier` (float) |
| `WFCavernClimbComponent` | Shared, networked | climb points | `Delay` (seconds, gravity already applied) |
| `WFCavernGroundComponent` | Server | ground map | `Cavern`, `Prototype`, `Cells` (`Dictionary<Vector2i, WFCavernCell>`), `Mouths` (list of `WFCavernMouth`: `Origin`, `Size`, `ClimbTile`, `Kind` = Gate/Cell/Hole/Admin), `Shades` (`Dictionary<Vector2i, EntityUid>`), `ClimbPoints` (`Dictionary<Vector2i, EntityUid>`) |
| `WFCavernUnstableComponent` (F5) | Server | unstable rock and vent walls | `Disturbance` (int), `CaveIn` (bool) |
| `WFCavernStateComponent` (F5) | Server | cavern map | `Disturbance`, `Warned`, `NextAwakening` |

Every component is `UnsavedComponent`. `WFCavernAir` is a shared enum with a pure classifier,
`WFCavernAir.Classify(GasMixture)`, applied in this order:
1. above 330 K: Scalding;
2. below 260 K: Freezing;
3. CO₂ ≥ 5 kPa, or any plasma or tritium: Toxic;
4. O₂ below 16 kPa: Thin;
5. any ammonia or N₂O: Foul;
6. otherwise: Breathable.

Partial pressure is `moles × Atmospherics.R × T / volume`. The shade stores the result when it spawns, so examine is
shared and predicted.

Shared events: `WFCavernClimbDoAfterEvent : SimpleDoAfterEvent` (`[Serializable, NetSerializable]`).

### 2.6 Server systems

- **`WFCavernSystem`**
  - `WFPlanetLowerLayersEvent`: if `wf.caverns` is on and the surface has a `wfCavern`, it spawns the level with
    `runMapInit: false`, sets the seed, adds `WFCavernLayerComponent` and appends the map.
  - `WFPlanetNetworkBuiltEvent`, for each map in `Lower` that has `WFCavernLayerComponent`:
    1. `SetMapAtmosphere(level.Atmosphere)`;
    2. remove `LightCycle`, `SunShadow`, `SunShadowCycle` and `Parallax`;
    3. set the roof colour;
    4. add `WFCavernGroundComponent` to the ground;
    5. ask `WFCavernMouthSystem` to claim the gate (F2).
  - F1: `(WFPlanetWildlifeComponent, CEZLevelFallMapEvent)` is a free pair. Surface wildlife that falls into a cavern
    over an *unloaded* ground chunk is deleted, so it never leaks against the fauna caps as a `Protected` resident of
    the void (2.1 item 4).
  - F4: every 10 s it mirrors the ground's `WFPlanetEnvironmentComponent` onto the cavern, with `Weather` set to
    `wf-cavern-weather-underground`. It also sets the cavern `MapLight` to ground `MapLight` × `shaftLight`.
- **`WFCavernMouthSystem`** (F2), with partials `.Claims.cs` and `.Holes.cs`: cells, claims, stamping, the hole queue,
  shades, climb points and the registry. Test and admin API: `GetGate`, `TryClaimCell` (returns
  Claimed/Deferred/Empty), `TryOpenMouth(ground, origin)`, `TryGetNearestMouth`.
- **`WFCavernClimbSystem : SharedWFCavernClimbSystem`** (F2): the move itself, server only. Hauling is F5.
- **`WFCavernHazardSystem`** (F5): cave-ins, vents and disturbance.
- **`WFCavernCommand`** (F2): `wfcavern`.
- **`BiomeSystem.Caverns.cs`** (`Content.Server/_WF/Caverns/`, namespace `Content.Server.Parallax`) exposes two
  helpers that need the protected `ChunkSize`:
  - `WfIsChunkLoaded(Entity<BiomeComponent>, Vector2i)`;
  - `WfIsBiomeSpawned(Entity<BiomeComponent>, EntityUid, Vector2i)`.

  Pinning uses the existing Planets `WfPinTiles` and `WfIsPinned`. Pure evaluation uses the public
  `TryGetTile`/`TryGetEntity` with `grid: null`.

### 2.7 Client

- **`SharedWFCavernClimbSystem`** (shared): the verbs *Climb up* and *Climb down*, *activate in world* on climb points,
  the DoAfter (`BreakOnMove`, `BreakOnDamage`, `NeedHand = false`, `BlockDuplicate`), predicted popups and the shaft
  examine. Verbs require `CanAccess && CanInteract`.
- **`WFCavernAmbienceSystem`** (F4, `Content.Client/_WF/Caverns`): a trimmed copy of the planet player, about 150
  lines.
  - It plays one looping bed with crossfades and random one-shots while the local player's map has
    `WFCavernLayerComponent`.
  - On entering a cavern map it shows the `arrival` popup once.
  - The planet ambience stops by itself underground, because caverns carry no `WFPlanetAmbienceComponent`. No
    surface bed plays below, which avoids double ambience.

### 2.8 Settings

`CavernCVars.Caverns` (`wf.caverns`, `CVar.SERVERONLY`) defaults to **false**. Production and the Planets test suite
stay unchanged until caverns are signed off, and `development.toml` turns the CVar on. It is read at build time, so it
affects networks built after it changes.

### 2.9 How a cavern is generated

The rule is **carve by tile.** Wall layers only allow the substrate tile T, so any other tile is open ground. Rock
walls, veins, unstable rock and vents are direct `BiomeEntityLayer`s inside the rock template, never
`MonoPlanetmap*` spawner markers. Mined walls therefore stay mined, untouched walls unload, and ore is always inside
rock. Fauna and loot markers (one-shot, self-deleting) are the only markers.

```yaml
- type: biomeTemplate
  id: WFCavernBiome<World>          # layers run last to first; the first match wins
  layers:
  - !type:BiomeTileLayer            # T: substrate and tunnel floor
    tile: <T>
    threshold: -1
  - !type:BiomeEntityLayer          # tunnel litter, lowest priority
    allowedTiles: [<T>]
    entities: [<litter>]
    threshold: 0.985
    noise: { seed: 31, noiseType: OpenSimplex2, frequency: 1 }
  - !type:BiomeMetaLayer            # rock wherever ridged <= t; tunnels are the ridges
    template: WFCavernRock<World>
    invert: true
    threshold: -<t>
    noise: { seed: 7, noiseType: OpenSimplex2, fractalType: Ridged, octaves: 1, frequency: <f_t> }
  - !type:BiomeMetaLayer            # chambers: tile C, decor, lights, fauna and loot markers
    template: WFCavernChamber<World>
    threshold: <t_c>
    noise: { seed: 101, noiseType: OpenSimplex2, fractalType: FBm, octaves: 2, frequency: <f_c> }
  - !type:BiomeMetaLayer            # signature feature, highest priority
    template: WFCavernSignature<World>
    threshold: <t_s>
    noise: { seed: 55, noiseType: OpenSimplex2, fractalType: Ridged, octaves: 1, frequency: <f_s> }
```

`WFCavernRock<World>` has the wall at `threshold: -1` on T first. Above it are the vein layers, each on its own seed
with OpenSimplex2 noise:

| Tier | Frequency | Threshold |
|---|---|---|
| Common | 0.09 | 0.70 |
| Uncommon | 0.09 | 0.80 |
| Rare | 0.09 | 0.88 |
| Very rare | 0.2 | 0.95 |
| Unstable rock (F5) | 0.03 | 0.82 |
| Vents (F5) | 0.15 | 0.93 |

Each vein layer uses `allowedTiles: [T]`.

- **Tunnels.** Ridged noise draws a connected web; `TunnelsConnect` checks it.
- **Chambers** sit on the web. A chamber that misses it is a sealed geode that miners can open.
- **Signatures** win over everything else. They can carry a bank tile, a nested core template on the same noise (lava,
  plasma), or entities placed straight on T, like the Gut's blood channels.

All cavern floors are indestructible and cannot be dug, so the bottom layer never opens to void. New tiles set
`parent` and inherit the parent's name, so they need no tile loc keys:

| Tile | Parent | Overrides |
|---|---|---|
| `WFCavernFloorLimestone` | `FloorCave` | `indestructible: true`, `deconstructTools: []` |
| `WFCavernFloorAsh` | `FloorCaveDrought` | indestructible, no tools, `fallDamageMultiplier: 0.75`, `fallStunMultiplier: 0.75` |
| `WFCavernFloorSand` | `FloorAsteroidSand` | indestructible, no tools |
| `WFCavernFloorSandDrift` | `FloorAsteroidSand` | indestructible, no tools, fall 0.5 / stun 0.5 |
| `WFCavernFloorSandstone` | `FloorDesertPlanet` | no tools (already indestructible) |
| `WFCavernFloorChromite` | `FloorChromite` | indestructible, no tools |
| `WFCavernFloorChromiteScree` | `FloorChromite` | indestructible, no tools, fall 1.5 / stun 1.2 |
| `WFCavernFloorBedrock` | `FloorBedrock` | indestructible, no tools |
| `WFCavernFloorSnowdrift` | `FloorSnow` | no tools, fall 0.25 / stun 0.25 (already indestructible) |
| `WFCavernFloorFlesh` | `FloorFlesh` | indestructible, no tools |
| `WFCavernFloorGut` | `FloorAsteroidIronsand` | indestructible, no tools, fall 0.4 / stun 0.4 |

`FloorBasalt`, `FloorIce`, `FloorSnowDug`, `FloorPlanetDirt` and `FloorWater` are used as they are.

## 3. Entrances and exits

### 3.1 The mechanism

There is one mechanism, which comes in two forms:
- **Mouths:** pinned holes with a lip, a pad and a climb point.
- **Opened holes:** any ground tile that becomes empty on a loaded chunk, whatever emptied it.

You fall or climb down through the hole, and climb back up at the climb point.

Mouths are **claimed lazily per cell, never stamped into a loaded chunk**:
- Stamping hundreds of sites at build time costs round-start time and memory for ground nobody visits.
- Marker-layer mouths carve into chunks that neighbours have already loaded, and they ignore the tile under them
  (they land in lakes).

Claiming ahead of the load front avoids both.

```
Ground (depth 0), 2×2 mouth           Cavern (depth −1), padRadius 3
  . . . . . .                         p p p p p p p p
  . k r r k .    H  hole (empty)      p p p p p p p p
  . r H H r .    r  lip ring          p p p p p p p p
  . r H H r .    k  lip corner (rim)  p p p L L p p p    L  landing tile
  . r c r k .    c  climb tile        p p p L L p p p    p  pad: natural floor, no rock
  . . . . . .                         p p p C p p p p    C  climb point, under c
                                      p p p p p p p p
                                      p p p p p p p p
```

Every tile shown above is pinned (`WfPinTiles`), so the biome never regenerates, fills or unloads it.

### 3.2 Cells and claims

- **Cells.** Cell index = `floor(tile / cellSize)` on the ground grid.
- **Polling.** Every 0.5 s, `WFCavernMouthSystem` collects claim sources for each ground map with
  `WFCavernGroundComponent`:
  - every session's attached entity, if it is on that ground or its cavern;
  - every view-subscription eye on either map.
- **Claim range.** Each unclaimed cell whose square intersects a ±96-tile box around a source is claimed. Chunks load
  up to about 40 tiles out (2.1 item 7), so a claim lands at least 56 tiles ahead of the load front. Air-layer ships
  at 12 m/s move 6 tiles per poll.
- **Candidates.** The gate cell tries `Centre + gateOffset` first. Every cell then tries 8 candidates drawn from
  `new System.Random(unchecked(seed * 7919 + cell.X * 73856093 + cell.Y * 19349663))`, spread uniformly over the cell
  inset by 8 tiles.
  - The seed is plain arithmetic, never `HashCode.Combine`, which is randomised per process.
  - The candidate is the hole's bottom-left tile.
- **Site.** The cell's site is the first candidate that passes both pure checks. Pure checks read only noise, so
  the site never depends on when the cell is claimed:
  1. **Ground.** Every tile of the footprint (hole plus ring) has a natural tile (`TryGetTile`, `grid: null`) in
     `groundTiles`, and no natural entity (`TryGetEntity`) in `avoid`. This keeps mouths off seas, lava rivers,
     plasma lakes, blood channels and boulders.
  2. **Cavern.** The natural cavern entity at the hole's centre is empty, and so are at least 3 of the 4 points 3
     tiles out (N/E/S/W). The pad then sits in a tunnel or chamber of the connected web.
- **Stamp or wait.** The site is stamped (3.3) and the cell becomes **Claimed** only when both of these hold.
  Otherwise the cell is **Deferred** and retried on the next poll:
  - no footprint tile is pinned or holds an anchored entity, and no grid other than the map lies within 4 tiles;
  - no ground chunk under the footprint and no cavern chunk under the pad is in `LoadedChunks`.
- **Empty cells.** If none of the 8 candidates passes the pure checks, the cell is **Empty** for good: a sea cell has
  no mouth. A cell whose site stays blocked by something players built simply stays Deferred.
- **Instant arrivals.** FTL preloads and admin teleports load chunks with no warning. They only delay a mouth; they
  never make a mouth cut into loaded terrain.
- **Gate.** At build, from the built event, cells are claimed outward from the centre's cell, up to 9, until one
  yields a site. That first site is the gate (`Kind = Gate`).
- **Cost.** A candidate evaluates about 30 pure tile and entity lookups, roughly 1 ms (2.1 item 8). The site is
  computed once and cached in the cell, so a Deferred cell's retries only re-run the cheap state checks. The claim
  step logs a warning when one cell takes more than 20 ms.

### 3.3 Stamping a site

Each map gets one `SetTiles` call per site. All of it works on unloaded chunks.

1. **Ground.**
   - Set the ring tiles to their natural tile.
   - Pin ring and hole. The hole stays empty, so the cavern below it stays unroofed: a light shaft.
2. **Cavern.**
   - Set `landingTile` under the hole and the natural tile over the rest of the pad (hole ± `padRadius`).
   - Pin every pad tile. Pinned tiles skip entity generation, so no rock ever spawns on the pad.
3. **Entities.**
   - `EnsureHole` spawns one `shade` on each hole tile.
   - `rim` decor goes on up to two ring corners, never next to the climb tile.
   - The `climbPoint` is anchored on the pad under the climb tile. The climb tile is the ring tile on `climbSide` of
     the hole's bottom-left.

### 3.4 Holes opened later (the hole queue)

`WFCavernMouthSystem` subscribes `(WFCavernGroundComponent, TileChangedEvent)`, a new pair. For each change it records
the index in one of two queues:
- `opened`: the tile went from non-empty to empty;
- `closed`: it went from empty to non-empty.

Both queues are processed in `Update`, every tick.

- **`opened`.** The index is skipped unless the ground tile is still empty **and** its biome chunk is still in
  `LoadedChunks`. `UnloadTiles` empties tiles before `LoadedChunks.Remove`, so emptiness caused by an unload is dropped
  here. Otherwise the system runs these steps:
  1. **`EnsureHole(index)`**:
     - pin the ground tile;
     - if the cavern tile is not pinned: when its chunk is loaded, delete the anchored entities on it that
       `WfIsBiomeSpawned` reports (never anything a player built); set the natural cavern tile if the tile is empty;
       pin it;
     - spawn a shade if the index has none.
  2. **`EnsureClimbNear(index)`**: if no climb point lies within 8 tiles, take the first 8-neighbour ground tile that
     is solid, not a hole, not under a grid and free of hard anchored entities. Prepare its cavern tile the same way,
     then anchor a climb point there. If no neighbour qualifies, the pit gets no climb point, and its climbers use
     the nearest mouth.
- **`closed`.** Delete the shade at that index. This covers lattice or a tile laid over a hole. Removing it again
  reopens the hole through `opened`.

This catches every way a hole can appear:
- shovels (Merak sand, Thrascias snow once dug and blown);
- explosions (Aerumna chromite, bedrock);
- prying and cutting (Carcinoma flesh to plating to lattice to space);
- RCD, admin tile tools and `wfcavern open`.

A mob in mid-fall has about 0.45 s to reach the floor. The queue runs within a tick, so the landing is always there
first.

### 3.5 Going down

**Walk in.** You fall one level in about 0.45 s. Other players see the CE "falls" popup. You land on the landing tile,
and damage is 13 Blunt × the tile multiplier (table below). It never kills. Knockdown lasts up to ~1.9 s ×
`fallStunMultiplier`. Items and thrown objects fall the same way.

| World | Landing tile | Multiplier | Blunt |
|---|---|---|---|
| Asclepiu | `FloorWater` | ×0 | 0 |
| Fervidus | `WFCavernFloorAsh` | ×0.75 | 10 |
| Merak | `WFCavernFloorSandDrift` | ×0.5 | 6 |
| Aerumna | `WFCavernFloorChromiteScree` | ×1.5 | 20 |
| Thrascias | `WFCavernFloorSnowdrift` | ×0.25 | 3 |
| Carcinoma | `WFCavernFloorGut` | ×0.4 | 5 |

**Climb down.** Use the *Climb down* verb on a shade. It is a 3 s DoAfter that breaks on move or damage. The server then
does the following in one tick, and the climber arrives standing, unhurt, beside the climb point:
1. moves the user to the mouth's climb tile on the ground (for a bare hole, the hole tile);
2. `TryMoveDown`;
3. `SetZPosition(0)` and `SetZVelocity(0)`.

**Other ways down:**
- **Parachutes:** a deployed parachute cancels the landing damage.
- **Moths and harpies** (`CEZFlyer`) can fly down a mouth and back up it.
- **Orbital fallers** who pass through a mouth are maimed rather than killed, through the `IsSurfaceImpact` fix
  (2.2).

### 3.6 Coming back up

Use *Climb up* on a climb point, by verb or by activating it. It needs no equipment and no hands.
- **Duration.** The DoAfter lasts `climbSeconds × clamp(surface gravity, 1, 2.5)`:
  - Asclepiu, Fervidus and Carcinoma: 4 s;
  - Merak: 4.6 s;
  - Thrascias: 5 s;
  - Aerumna: 10 s.

  Hauling multiplies it by 1.5 (F5). It breaks on move or damage.
- **Exit tile.** The server picks the nearest ground tile within 2 tiles of the climb point that meets all of these:
  - it is non-empty and not a hole in the registry;
  - no grid other than the map covers it (a hull there gives `wf-cavern-climb-blocked-hull`);
  - no hard anchored entity stands on it.

  If no tile qualifies, the climb gives `wf-cavern-climb-blocked`.
- **Move.** In one tick: `TryMoveUp`, set the coordinates to the exit tile's centre on the ground, `SetZPosition(0)`,
  `SetZVelocity(0)`. The exit is solid, so the climber stands and does not fall back.
- **Robustness.** A lip dug away by a shovel only moves the exit to the next solid tile. Climbing never teleports anyone
  into a hull.

NPCs never use verbs, and no cavern mob has `CEZFlyer`, so fauna stays below.

### 3.7 Ships and debris

- **A hull over a mouth stays put.**
  - The pinned ring guarantees a solid tile under any hull larger than the hole, and one tile is enough for
    `HasGroundUnderFootprint`.
  - Crew aboard stand on the hull grid. Stepping off the deck into the shaft drops them.
  - From below, a hull over the exit refuses the climb, and the shade of a covered hole is untouched.
- **A parked hull whose ground chunk unloads keeps today's behaviour.** Its tiles empty (2.1 item 7) and it loses
  support. `WfClosedToHulls` refuses every downward route, so it churns up and back instead of sinking into the
  cavern.
- **Debris.** A grid of 2×2 or less sitting entirely inside a hole churns the same way. That is existing behaviour
  over unloaded terrain, and it is accepted.
- **Transit maps.** No transit map ever has a cavern as `LowerMap`, so the weather system's transit sweep never
  reaches one.

### 3.8 Ramps: not in v1

CE high-ground stairs under a hole would work in principle, but ramps are not in v1. The level flip at curve height
1.0 is unproven, and each flip rebuilds the viewer's eyes. Side entry drops up to 0.9 of a level.
`CEZLevelsLaddersCacheSystem` caches every high-ground piece for future z-pathing (it has no consumer today). Climb
points reuse the ladder *sprites* only, without `CEZLevelHighGround`.

A later ramp feature ships only if `CavernRampTest` passes every case:
- stepping up the ramp in 0.05-tile increments gives exactly one map change and a stable stand;
- a mob placed exactly where the curve crosses 1.0 changes map at most once in 120 ticks;
- the same holds in reverse;
- side entry deals under 20 Blunt.

### 3.9 What must be proven before anything depends on it

The tests below are the gates.

| Proven by | Before |
|---|---|
| `CavernHullTest.UnsupportedHullNeverDescends`, `PilotCannotDescendFromGround`, `LiftoffAndLandingUnchanged` | caverns are switched on anywhere (F1) |
| `CavernViewerEyeTest.GroundViewerLoadsNoCavern` | F1 ends; later features assume ground viewers cost nothing below |
| `CavernFallTest.MobFallsAndIsHurtALittle` [6] | the guidebook, the examine texts and the gate rely on walking in |
| `CavernClimbTest.ClimbUpLandsOnSolidExitAndStays` | anything calls the caverns accessible without equipment |
| `CavernMouthTest.GateSurvivesUnloadReload`, `ClaimDeferredWhileChunkLoaded`, `ClaimAheadOfViewer` | lazy claims replace any build-time stamping |
| `CavernHoleTest.UnloadDoesNotOpenHoles`, `DugHoleGetsLandingShadeAndClimb` | the hole queue is trusted with real holes |
| `CavernRampTest` (every case) | any ramp prototype is merged |

## 4. The six caverns

### 4.1 Shared rules

- **Ids per world:**
  - `wfCavern` `WFCavern<World>`, level `WFCavern<World>Level`;
  - biome templates `WFCavernBiome<World>`, `WFCavernRock<World>`, `WFCavernChamber<World>`,
    `WFCavernSignature<World>` (plus named sub-templates);
  - shade `WFCavernShade<World>`, climb point `WFCavernClimb<World>`;
  - fauna marker and entity table `WFCavernFauna<World>` (same id, different kinds, as `WFFaunaAsclepiu` already
    does);
  - deep table `WFCavernDeep<World>` (F5), unstable rock `WFCavernUnstable<World>` (F5), vent wall
    `WFCavernVent<World>`, ore `WFCavernOreGas<World>` and smoke `WFCavernGasPocket<World>` (F5).
- **Shades** (`WFCavernShadeBase`): unanchored, no physics and no `CEZPhysics`, so they never fall. They use the
  `full` state of the world's `Tiles/Planet/Chasms/*_chasm.rsi`, draw depth `LowFloors`, and have `Clickable` and
  `WFCavernShaft`. A hole therefore reads as a pit rather than void or parallax, and it can be seen from low flight.
- **Climb points** (`WFCavernClimbBase`): anchored, no fixtures, and `Clickable`/`InteractionOutline`. The sprite is a
  CE ladder RSI (`_CE/Structures/Architecture/Ladders/<rsi>`, state `straight`) with a tint, carrying
  `WFCavernClimb`.
- **Fauna** comes from `WFPlanetFaunaSpawner` markers on the chamber tile C (OpenSimplex2, frequency 3, threshold
  0.95), so the existing caps apply: 32 per map, 128 in total. Cavern fauna retires once no cavern player is near,
  because the eye cap means surface viewers never observe it.
- **Why go down.** Surface ore is sparse boulders (`MonoPlanetmapOre*`). Every cavern rock tile is a wall, and veins
  fill about 15% of the rock. The ores the surface lacks are listed per world.
- **Open band** is the fraction of open tiles `CavernBiomeTest.OpenFractionInBand` expects over a 192² sample. The
  noise numbers below are starting values; tune them until the band holds.

### 4.2 Asclepiu: the Underkarst

The beginner cavern: wet limestone, breathable air and a water landing.

| | |
|---|---|
| Level | `WFCavernAsclepiuLevel`: `mapName: wf-cavern-asclepiu-map`, `mapLight: "#6f8f86"`, atmosphere `[21.824879, 82.10312]` at 285.15 K (Breathable) |
| Tiles | T `WFCavernFloorLimestone`; C `FloorPlanetDirt`; pools `FloorWater` |
| Skeleton | Tunnels: ridged frequency 0.035, rock where ridged ≤ 0.50. Chambers: FBm frequency 0.025, threshold ≥ 0.35. Signature (in the chamber template): karst pools, a `FloorWater` tile layer at OpenSimplex2 frequency 0.06, threshold ≥ 0.45. Open band 0.35–0.55 |
| Rock and ore | `WallRockAndesite`. Common: `…Coal`, `…Tin`. Uncommon: `…Quartz`, `…Salt`, `…Copper`. Rare: `…Silver`, `…Gold`. Very rare: `…ArtifactFragment`. Hazard: `WallRockAndesiteQuartzGolem` (0.97) |
| Light | Roof `#070a08`, `shaftLight` 0.5. `WFCavernGlowworms` in chambers (≥ 0.97): no sprite, `PointLight` `#7dffb4`, radius 4, energy 0.7. `CrystalGreen`/`CrystalCyan` (≥ 0.99) |
| Hazards | Pools slow you. `SpiderWeb` choke points in chambers (≥ 0.985). Golems. Cave-ins 0.15 (F5). No vents |
| Fauna | `MobBat` 5, `MobFrog` 3, `MobMouse` 2, `MobSnake` 2, `MobGiantSpider` 1 |
| Decor | Litter `FloraStalagmite`, `FloraGreyStalagmite`. Chambers `Cobweb1`, `Cobweb2` |
| Deep (F5) | `MobRatKing` + 3 `MobRatServant` |
| Ambience | Loop `/Audio/Ambience/ambicave.ogg`. One-shots `/Audio/Effects/waterswirl.ogg`, `/Audio/Effects/drop.ogg` |
| Mouth | Sinkhole, 2×2, cell 96. `groundTiles: [FloorPlanetGrass, FloorPlanetDirt, FloorSnow]`, `avoid: [MonoFloorWaterEntity]`. Shade `desert_chasm` tinted `#5d5445`. Climb point `dirt_cliff.rsi` (roots). No rim. Lands in a plunge pool for 0 Blunt |
| Only below | Continuous salt and silver veins, artifact fragments |

### 4.3 Fervidus: the Cinder Vaults

Basalt cut by lava tubes, with magma chambers and diamonds.

| | |
|---|---|
| Level | `WFCavernFervidusLevel`: `mapLight: "#c48b71"`, atmosphere `[0, 60, 40]` at 413.15 K (Scalding; the surface is 373 K) |
| Tiles | T `FloorBasalt`; C `WFCavernFloorAsh`; landing `WFCavernFloorAsh` |
| Skeleton | Tunnels: frequency 0.04, ≤ 0.60. Chambers: frequency 0.028, ≥ 0.38, with magma lakes (`FloorLavaEntity` on C, FBm frequency 0.05, ≥ 0.55). Signature `WFCavernSignatureFervidus`: lava tubes, ridged frequency 0.009, ≥ 0.86. It has an ash bank (tile C) and a nested core template on the same noise at ≥ 0.95 placing `FloorLavaEntity`. Open band 0.30–0.45 |
| Rock and ore | `WallRockBasalt`. Common: `…Coal`, `…Plasma`. Uncommon: `…Tin`, `…Uranium`. Rare: `…Gold`, `…Silver`, `…Diamond` (0.90). Very rare: `WallRockBasaltBluespace`. Hazard: `WallRockBasaltPlasmaGolem` (0.97) |
| Light | Roof `#1c0803`, `shaftLight` 0.45. Lava tile emission. `CrystalOrange` (≥ 0.99) |
| Hazards | Lava. Heat. Sulphur vents `WFCavernVentFervidus` (`SulfuricAcid` smoke, F5). Cave-ins 0.2 |
| Fauna | `WFMobArgocyteSlurvaBasalt` 4, `WFMobArgocyteCrawlerBasalt` 4, `WFMobArgocyteSwiperBasalt` 2, `MobWatcherMagmawing` 1 |
| Decor | Litter `BasaltOne`–`BasaltFive`. Chambers `FloraGreyStalagmite` |
| Deep (F5) | `MobArgocyteLeviathing` |
| Ambience | Loops `/Audio/Ambience/ambilava1.ogg`, `…ambilava2.ogg`, `…ambilava3.ogg`. One-shots `/Audio/Effects/sizzle.ogg`, `/Audio/Magic/rumble.ogg` |
| Mouth | Skylight, 2×2, cell 96. `groundTiles: [FloorBasalt]`, `avoid: [FloorLavaEntity]`. Shade `basalt_chasm`. Climb point `stone.rsi` tinted `#5a4a44`. Rim `BasaltOne`, `BasaltThree`. Lands for 10 Blunt |
| Only below | Diamonds and bluespace in basalt |

### 4.4 Merak: the Sandstone Galleries

Cool pillared halls under a 45 °C desert, with the richest gold and the most collapses.

| | |
|---|---|
| Level | `WFCavernMerakLevel`: `mapLight: "#dfc396"`, atmosphere `[21.824879, 82.10312]` at 294.15 K (Breathable; a refuge from the surface's 318 K) |
| Tiles | T `WFCavernFloorSand`; C `WFCavernFloorSandstone`; landing `WFCavernFloorSandDrift` |
| Skeleton | Tunnels: frequency 0.03, ≤ 0.50. Chambers are pillared halls: FBm frequency 0.02, ≥ 0.20, with single-tile `WallRockSand` pillars on C (OpenSimplex2 frequency 0.35, ≥ 0.84). Signature: buried camps in halls, `SalvageHumanCorpseSpawner` (≥ 0.997) and `SalvageSpawnerScrapValuable` (≥ 0.995), plus fossil beds in rock (`WallRockSandArtifactFragment` at frequency 0.05, ≥ 0.88). Open band 0.40–0.60 |
| Rock and ore | `WallRockSand`. Common: `…Tin`, `…Quartz`. Uncommon: `…Copper`, `…Salt`. Rare: `…Gold` (0.86), `…Silver`. Very rare: `…Diamond`. Hazard: `WallRockSandGoldCrabNF` (0.96) |
| Light | Roof `#120d07`, `shaftLight` 0.55. `CrystalOrange`/`CrystalPink` (≥ 0.992) |
| Hazards | Cave-ins 0.35 (sandstone). Dust pockets `WFCavernVentMerak` (`TearGas` smoke, F5). Ore crabs |
| Fauna | `MobLizard` 3, `MobSnake` 3, `MobPurpleSnake` 1, `MobGiantSpider` 1 |
| Decor | Litter `FloraRockSolid` |
| Deep (F5) | 2 `MobGiantSpiderAngry` |
| Ambience | Loop `/Audio/Ambience/ambimine.ogg`. One-shots `/Audio/Effects/break_stone.ogg`, `/Audio/Effects/rustle4.ogg` |
| Mouth | Sand funnel, 2×2, cell 96. `groundTiles: [FloorAsteroidSandPlanet, FloorDesertPlanet, FloorAsteroidSandUnvariantizedPlanet]`, `avoid: [MonoFloorWaterEntity, MonoPlanetmapOreSandRich]`. Shade `desert_chasm`. Climb point `wooden.rsi` (rope ladder). Lands for 6 Blunt. A shovel opens a way down anywhere (3.4) |
| Only below | Gold-rich veins, fossils, lost prospectors' gear |

### 4.5 Aerumna: the Umbral Deeps

The darkest cavern: chromite, 3 g, toxic air, xenos, and the only anomaly rock.

| | |
|---|---|
| Level | `WFCavernAerumnaLevel`: `mapLight: "#2a1f3a"`, atmosphere `[4, 72, 24]` at 277.15 K (Toxic from CO₂) |
| Tiles | T `WFCavernFloorChromite`; C `WFCavernFloorBedrock`; landing `WFCavernFloorChromiteScree` |
| Skeleton | Tight crawls: ridged frequency 0.05, ≤ 0.62. Chambers are cathedral galleries: FBm frequency 0.012, ≥ 0.30. Signatures in galleries: shadow groves (meta FBm frequency 0.02, ≥ 0.5, placing `ShadowTree`, `ShadowBasaltOne`, `ShadowBasaltTwo` on C at ≥ 0.65) and pink geodes (meta OpenSimplex2 frequency 0.08, ≥ 0.75, placing `CrystalPink` on C at ≥ 0.5). Open band 0.25–0.40 |
| Rock and ore | `WallRockChromite`. Common: `…Tin`, `…Plasma`. Uncommon: `…Quartz`, `…Uranium`. Rare: `…Silver`, `…Gold`. Very rare: `…Diamond`, `WallRockChromiteBluespace`, `WallRockChromiteArtifactAnomaly` |
| Light | Roof `#050408`, `shaftLight` 0.3. Geodes are the only glow |
| Hazards | Darkness. CO₂. Xenos. A 10 s climb out at 3 g (clear the pad first). Spore pockets `WFCavernVentAerumna` (`Nocturine` smoke, F5). Cave-ins 0.25 |
| Fauna | `MobXenoRunner` 3, `MobXenoDrone` 2, `MobArgocyteSlurva` 3, `MobXenoSpitter` 1, `MobXenoPraetorian` 0.5 |
| Deep (F5) | `MobXenoPraetorian` + 2 `MobXenoRunner` |
| Ambience | Loop `/Audio/Ambience/ambimystery.ogg`. One-shots `/Audio/Effects/glass_crack1.ogg`, `/Audio/Magic/rumble.ogg` |
| Mouth | Rift, 1×1, cell 128. `groundTiles: [FloorChromite]`, `avoid: [MonoFloorWaterEntity]`. Shade `chromite_chasm`. Climb point `stone.rsi` tinted `#3a3342`. Lands for 20 Blunt, the hard landing of a 3 g world. Explosions open ways down (3.4) |
| Only below | Artifact anomalies, bluespace, diamonds |

### 4.6 Thrascias: the Rime Galleries

Ice halls with plasma lakes, milder than the 180 K surface but still lethal.

| | |
|---|---|
| Level | `WFCavernThrasciasLevel`: `mapLight: "#9fc3dc"`, atmosphere `[0, 100]` at 235.15 K (Freezing) |
| Tiles | T `FloorSnowDug`; C `WFCavernFloorSnowdrift`; galleries `FloorIce`; landing `WFCavernFloorSnowdrift` |
| Skeleton | Tunnels: frequency 0.045, ≤ 0.60. Chambers: frequency 0.025, ≥ 0.35. Signature `WFCavernSignatureThrascias`: ice galleries, ridged frequency 0.018, ≥ 0.75. They have a `FloorIce` tile, `WallIce` columns on ice (OpenSimplex2 frequency 0.4, ≥ 0.9), `CrystalBlue`/`CrystalCyan` (≥ 0.97), and a nested core on the same noise at ≥ 0.93 with `FloorLiquidPlasmaEntity` lakes. Open band 0.35–0.55 |
| Rock and ore | `WallRockSnow`. Common: `…Coal`, `…Tin`. Uncommon: `…Quartz`, `…Plasma`. Rare: `…Silver`, `…Uranium`. Very rare: `…Diamond`, `WallRockSnowBluespace` |
| Light | Roof `#060c12`, `shaftLight` 0.5. Blue crystals are common |
| Hazards | Cold. Momentum on ice next to plasma lakes. Frost pockets `WFCavernVentThrascias` (`FrostOil` smoke, F5). Cave-ins 0.2 |
| Fauna | `MobArgocyteSlurva` 4, `MobArgocyteBarrier` 2, `MobPenguin` 2, `MobWatcherIcewing` 1, `MobBearSpace` 1 |
| Loot | `SalvageSpawnerTreasureValuable` in chambers (≥ 0.997): frozen caches |
| Deep (F5) | 2 `MobBearSpace` |
| Ambience | Loop `/Audio/Ambience/ambiatmos2.ogg`. One-shots `/Audio/Effects/glass_crack2.ogg`, `/Audio/Effects/glass_crack1.ogg` |
| Mouth | Moulin, 1×1, cell 96. `groundTiles: [FloorSnow, FloorIce]`, `avoid: [FloorLiquidPlasmaEntity]`. Shade `snow_chasm`. Climb point `stone.rsi` tinted `#bfe6ff`. Rim `CrystalCyan` on two opposite corners, so the glow marks the mouth at night. Lands for 3 Blunt |
| Only below | Diamonds, bluespace, preserved caches |

### 4.7 Carcinoma: the Gut

Flesh throats and stomachs grown over a mineral world, where the infestation began.

| | |
|---|---|
| Level | `WFCavernCarcinomaLevel`: `mapLight: "#5a1a22"`, atmosphere `[21.824879, 76, 0, 0, 0, 0, 1]` at 310.15 K (Foul: breathable, with ammonia) |
| Tiles | T `WFCavernFloorFlesh`; C `WFCavernFloorGut`; landing `WFCavernFloorGut` |
| Skeleton | Throats: ridged, 2 octaves for wiggle, frequency 0.06, ≤ 0.62. Stomachs: FBm frequency 0.03, ≥ 0.35. Signature: digestive channels, `WFBloodRiver` placed straight on T (ridged frequency 0.012, ≥ 0.94, highest priority). Stomachs hold `WFCarcinomaAssimilationSack` (≥ 0.985), `WFFleshPustule` (≥ 0.985), `WFFleshPolyp` (≥ 0.98) and `WFCavernGutGlow` (≥ 0.975). Open band 0.35–0.55 |
| Rock and ore | `WallMeat`, as on the surface. It can't be mined: cut it down like any wall. The rock template has a calcified-node meta layer (FBm frequency 0.05, ≥ 0.6) of `WallRockAndesite`, with `…Salt`, `…Silver`, `…Gold`, `…Plasma` and `…Uranium` veins. The flesh grew over a mineral world |
| Light | Roof `#140306`, `shaftLight` 0.4. `WFCavernGutGlow`: no sprite, `PointLight` `#ff4a5a`, radius 3 |
| Hazards | Ammonia (masks). Ticks. Pustules. Bile pockets `WFCavernVentCarcinoma` (`Ammonia` smoke, F5). No cave-ins: flesh doesn't collapse |
| Fauna | `WFCavernFaunaCarcinoma`: nested `WFFaunaCarcinoma` 3, `WFMobFleshTick` 4, `MobFleshAssimilatedMiner` 1 |
| Deep (F5) | `MobLetoferolHorror` |
| Ambience | Loop `/Audio/Ambience/anomaly_scary.ogg`. One-shots `/Audio/Effects/gib1.ogg`, `/Audio/Effects/Fluids/blood1.ogg`, `/Audio/Ambience/Objects/drain.ogg` |
| Mouth | Throat, 2×2, cell 80. `groundTiles: [WFFloorFlesh]`, `avoid: [WFBloodRiver]`. Shade `basalt_chasm` tinted `#4a0f16`. Climb point `dirt_cliff.rsi` tinted `#8a3a3a` (a tendril). Rim `WFFleshPolyp`. Lands for 5 Blunt. Prying and cutting the flesh opens ways down (3.4) |
| Only below | Uranium and plasma in calcified nodes, assimilated miners' gear |

### 4.8 Air at a glance

| World | Readout (`WFCavernAir`) | Without gear |
|---|---|---|
| Asclepiu | Breathable | Safe |
| Fervidus | Scalding | Heat and no O₂; hardsuit required |
| Merak | Breathable | Safe; cooler than the surface |
| Aerumna | Toxic | CO₂ poisoning; internals required |
| Thrascias | Freezing | Cold and no O₂; hardsuit required |
| Carcinoma | Foul | Breathable, with slow ammonia poisoning; a gas mask helps |

## 5. Player and admin surface

All strings are Fluent, in `Resources/Locale/en-US/_WF/Caverns/`. Tiles inherit their parents' names, so they add no
keys.

**`caverns.ftl`**

| Key | Text (English) | Feature |
|---|---|---|
| `wf-cavern-<world>-name` ×6 | the Underkarst, the Cinder Vaults, the Sandstone Galleries, the Umbral Deeps, the Rime Galleries, the Gut | F1 |
| `wf-cavern-<world>-map` ×6 | `{world} Underkarst`, … (map names for the `planet` prototypes) | F1 |
| `wf-cavern-shaft-examine` | A shaft drops into { $cavern }. | F2 |
| `wf-cavern-shaft-air-breathable` | The air rising from it smells clean. | F2 |
| `wf-cavern-shaft-air-foul` | The air rising from it is breathable, but it stinks. | F2 |
| `wf-cavern-shaft-air-thin` | The air below is too thin to breathe. | F2 |
| `wf-cavern-shaft-air-toxic` | The air rising from it stings your throat. It isn't safe to breathe. | F2 |
| `wf-cavern-shaft-air-scalding` | Scalding air rises from it. | F2 |
| `wf-cavern-shaft-air-freezing` | Freezing air rises from it. | F2 |
| `wf-cavern-shaft-landing-water` | You can hear water at the bottom. | F2 |
| `wf-cavern-shaft-landing-soft` | The bottom looks soft. | F2 |
| `wf-cavern-shaft-landing-hard` | It's a hard landing. Climbing down is slower, but safe. | F2 |
| `wf-cavern-climb-examine` | It leads back up to the surface. | F2 |
| `wf-cavern-verb-climb-up` / `-climb-down` | Climb up / Climb down | F2 |
| `wf-cavern-climb-up-start` / `-down-start` | You start climbing up. / You start climbing down. | F2 |
| `wf-cavern-climb-up-start-others` / `-down-start-others` | { CAPITALIZE(THE($user)) } starts climbing up. / … down. | F2 |
| `wf-cavern-climb-blocked` | Something blocks the way up. | F2 |
| `wf-cavern-climb-blocked-hull` | A ship is parked over the exit. | F2 |
| `wf-cavern-<world>-arrival` ×6 | e.g. Fervidus: "The heat presses in. Far below, rock glows red." | F4 |
| `wf-cavern-weather-underground` | Underground | F4 |
| `wf-cavern-climb-haul` | You haul { THE($thing) } up behind you. | F5 |
| `wf-cavern-unstable-examine` | The rock here is cracked and loose. | F5 |
| `wf-cavern-vent-examine` | Gas hisses from a crack in it. | F5 |
| `wf-cavern-cave-in-warning` | The ceiling groans. | F5 |
| `wf-cavern-cave-in` | Rock crashes down! | F5 |
| `wf-cavern-disturbance-warning` | Something stirs deep in the rock. | F5 |
| `wf-cavern-awakened` | Something is coming. | F5 |

Each examine line of a shade is chosen as follows:
- the air line comes from `WFCavernShaftComponent.Air`;
- the landing line is *water* for multiplier 0, *soft* below 0.6 and *hard* otherwise;
- the hard-landing line also suggests climbing down.

Watches work underground because of the environment mirror (F4).

**`entities.ftl`**: an `ent-<Id>` name and `.desc` for every concrete entity. Base prototypes are abstract and
unnamed.

| Group | Names |
|---|---|
| Shades | Asclepiu sinkhole, Fervidus skylight, Merak sand funnel, Aerumna rift, Thrascias moulin, Carcinoma throat |
| Climb points | Asclepiu root-bound bank, Fervidus basalt steps, Merak rope ladder, Aerumna chromite steps, Thrascias ice steps, Carcinoma tendril ladder |
| F3 | `WFCavernGlowworms`, `WFCavernGutGlow` (named, never visible) |
| F5 | `WFCavernUnstable<World>` ("cracked …"), `WFCavernVent<World>` ("hissing …"), `WFCavernGasPocket<World>` |

**`commands.ftl`**: `wfcavern` (`WolfgateAdminCommands.Cavern`) takes
`[AdminCommand(AdminFlags.Server | AdminFlags.Mapping)]` like `wfplanet`, and admin-logs every teleport and change.

| Subcommand | Does | Feature |
|---|---|---|
| `list` | One row per built network: planet, cavern map, claimed mouths, players below | F2 |
| `tp <planet> [pad\|mouth]` | Moves the caller to the gate's cavern pad (default) or beside the gate on the ground | F2 |
| `mouths <planet>` | Lists claimed mouths: kind, origin, climb tile | F2 |
| `open` | Carves a mouth at the caller's ground position. On a loaded chunk it deletes only biome-spawned entities in the footprint, and refuses if a grid, a player-built anchored entity or a mob is in the hole | F2 |
| `stats <planet>` | Open fraction and largest-component share of a 192² pure-noise sample around the caller (the same sampler as the tests) | F3 |
| `awaken <planet>` | Forces the deep-table spawn near the caller | F5 |

Keys: `cmd-wfcavern-desc`, `-help`, `-disabled`, `-invalid-args`, `-unknown-planet`, `-no-cavern`, `-empty`, `-row`,
`-mouth-row`, `-tp-done`, `-no-map`, `-not-ground`, `-open-done`, `-open-refused`, `-stats`, `-awakened`, `-hint-sub`,
`-hint-planet`, `-hint-target`.

**Guidebook** (F6):
- Page: `Resources/ServerInfo/_WF/Caverns/Caverns.xml`.
- Entry: `WFCaverns` in `Resources/Prototypes/_WF/Caverns/Guidebook/caverns.yml`, named `guide-entry-wf-caverns` in
  `guidebook.ftl`, linked under `Expeditions`.
- Key line: "Every world has caves below. Look for dark pits in the ground and examine one before you jump: it tells
  you whether the air below is safe. Climb out at the steps, rope or roots beside where you land."
- One short paragraph per world gives its air, landing and hazards.

## 6. Test plan

Tests live in `Content.IntegrationTests/Tests/_WF/Caverns` and, for pure logic, `Content.Tests/_WF/Caverns`.
- **`CavernFixture`** (static, in the style of `PlanetFixture`) provides:
  - `EnableCaverns(pair)`, which sets `wf.planet_networks` and `wf.caverns`;
  - `BuildWorld(pair, surfaceId)`, which returns the network, `Layers` and the cavern, built at `Vector2.Zero`;
  - `Gate(pair, ground)`, `ClimbTile` and `LandingIndex` helpers.

  Teardown goes through `PlanetFixture.Teardown`, which deletes the lower layers with the network.
- **Pool settings.** Every pair is non-destructive. `Connected = true` is used only where `AttachViewer` is. Every
  test ends with `Teardown` and `CleanReturnAsync`.
- **Runs.** The container has 4 CPUs and 15 GB, so run one fixture at a time.

| Test | Asserts | Feature |
|---|---|---|
| `CavernNetworkTest.CavernsOffLeavesNetworkUntouched` | CVar off: `LowerLayers` is empty, no negative `ZLevels` key, `Layers.Count == 5` | F1 |
| `CavernNetworkTest.EveryWorldGetsOneCavern` [6] | Depth −1; `TryMapDown(ground)`/`TryMapUp(cavern)` link; `Layers` unchanged. The cavern has `WFPlanetLayer` (right network and gravity), `WFCavernLayer`, the right biome and seed, and no `LightCycle`, `SunShadow`, `Parallax` or `CEZGroundLayer`. Its `MapAtmosphere` equals the level's. The ground has `WFCavernGround` | F1 |
| `CavernNetworkTest.DeleteRemovesCavernAndTransits` | A stub transit whose `LowerMap` is the cavern is deleted with the network | F1 |
| `CavernRoofTest.GroundTilesRoofCavern` | `LayTiles` on the ground roofs those cavern tiles; emptying one unroofs it | F1 |
| `CavernViewerEyeTest.GroundViewerLoadsNoCavern` | A ground viewer has no eye on the cavern, and the cavern's `LoadedChunks` stays empty for 60 ticks | F1 |
| `CavernViewerEyeTest.CavernViewerLoadsGroundAbove` | A cavern viewer has an eye on the ground, and ground chunks load over it | F1 |
| `CavernHullTest.UnsupportedHullNeverDescends` | A `BuildHull` on ground without lift, with its chunks forced out (`WfUnloadChunk`): over 10 s the hull's map is never the cavern and no transit touches the cavern | F1 |
| `CavernHullTest.PilotCannotDescendFromGround` | `HoldDescend` over unloaded ground never leaves depth ≥ 0 | F1 |
| `CavernHullTest.LiftoffAndLandingUnchanged` | With caverns on, a `BuildLander` lifts to air layer 1 and lands back on the ground | F1 |
| `CavernWildlifeTest.WildlifeOverUnloadedGroundIsRemoved` | An awake wildlife mob whose ground chunk unloads falls into the cavern and is deleted, so it never lingers `Protected` | F1 |
| `CavernMouthTest.GateExists` [6] | The gate is claimed at build. Its hole tiles are pinned and empty with a shade each. Its ring is pinned and solid. The pad is pinned, with the landing tile under the hole and no rock after a cavern viewer loads it. The climb point is anchored under the climb tile | F2 |
| `CavernMouthTest.GateSurvivesUnloadReload` | `WfUnloadChunk`, then `WfLoadChunk`, on both maps leaves hole, ring, pad and entities unchanged | F2 |
| `CavernMouthTest.ClaimAheadOfViewer` | A viewer at (400, 0): within 1 s every cell within 96 tiles is Claimed or Empty, and none of its sites touched a chunk that was loaded at claim time | F2 |
| `CavernMouthTest.ClaimDeferredWhileChunkLoaded` | After `WfLoadChunk` on a cell's first valid site, `TryClaimCell` returns Deferred. After `WfUnloadChunk` it returns Claimed at the same site | F2 |
| `CavernMouthTest.NoMouthOffTheAllowlist` | An Asclepiu ocean cell and a Carcinoma blood-sea cell come out Empty | F2 |
| `CavernFallTest.MobFallsAndIsHurtALittle` [6] | A `MobHuman` walked into the gate is on the cavern within 120 ticks, alive, with Blunt within ±3 of the section 3.5 table | F2 |
| `CavernFallTest.ItemFallsToPad` | A dropped item ends on the landing tile | F2 |
| `CavernHoleTest.DugHoleGetsLandingShadeAndClimb` | Merak: a ground tile set to empty on a loaded chunk gets a pinned cavern floor with no wall, a shade, and a climb point within 8 tiles | F2 |
| `CavernHoleTest.ExplosionHolesAllGetLandings` | Aerumna: after an explosion over chromite, every ground tile that became empty has a pinned cavern floor | F2 |
| `CavernHoleTest.UnloadDoesNotOpenHoles` | `WfUnloadChunk` on the ground creates no shade and pins no cavern tile | F2 |
| `CavernHoleTest.CoveredHoleLosesShade` | A tile laid over a hole removes its shade, and removing it again restores the shade | F2 |
| `CavernClimbTest.ClimbUpLandsOnSolidExitAndStays` | After the DoAfter the mob is on the ground, on a solid exit, at `LocalPosition < 0.1`, and stays for 120 ticks | F2 |
| `CavernClimbTest.ClimbDownIsHarmless` | 0 damage; the mob stands on the pad beside the climb point | F2 |
| `CavernClimbTest.ClimbRefusedUnderHull` | A hull over the exit tiles refuses the climb with the hull popup, and the mob stays below | F2 |
| `CavernClimbTest.DelayScalesWithGravity` | Asclepiu 4 s, Aerumna 10 s (±1 tick) | F2 |
| `CavernHullTest.HullOverMouthStaysOnGround` | A 3×3 `BuildHull` over the gate stays on the ground with no transit for 5 s | F2 |
| `CavernHullTest.SmallDebrisOverMouthNeverEntersCavern` | A 1×1 debris grid inside the hole never has the cavern as its map (it may churn) | F2 |
| `CavernOrbitalFallTest.OrbitalFallIntoMouthMaimsNotKills` | Dropped from orbit over the gate: critical, not dead, one arm and one leg severed, on the cavern | F2 |
| `CavernCommandTest` | `list` prints six rows, `tp` lands on the gate pad, `open` creates a mouth, `mouths` lists the gate | F2 |
| `Content.Tests: CavernAirTest.ClassifiesEachWorld` | The six level atmospheres classify as in section 4.8, and the thresholds are exact at their edges | F2 |
| `CavernPrototypeTest.OneCavernPerSurface` | One `wfCavern` per `wfPlanetSurface`, and every reference resolves (level, biome, tiles, entities, sounds) | F3 |
| `CavernPrototypeTest.FloorsIndestructibleAndUndiggable` | Every tile a cavern template or mouth can place is `Indestructible`, and neither `CanShovel` nor `CanCrowbar`. This also proves tile `parent` inheritance | F3 |
| `CavernPrototypeTest.NoLeakingWallSpawners` | No cavern template entity layer names a self-deleting spawner other than the allow-listed fauna and loot markers (no `MonoPlanetmap*`) | F3 |
| `CavernBiomeTest.OpenFractionInBand` [6] | 192² pure sample around the gate falls inside the section 4 band | F3 |
| `CavernBiomeTest.TunnelsConnect` [6] | In 128², the largest 4-connected open component holds at least 60% of open tiles | F3 |
| `CavernBiomeTest.OreOnlyInRock` [6] | Every sampled vein entity stands on T | F3 |
| `CavernBiomeTest.SignaturePresent` [6] | Over 512², sampled every 2nd tile: lava, `FloorWater`, pillars and fossils, pink geodes and shadow trees, `FloorIce` and `WallIce`, `WFBloodRiver` | F3 |
| `CavernGenerationTest.PadClearAndWorldFloors` [6] | A viewer on the gate pad: the pad has no hard entity, the world's T and C tiles are present, and there are 2,500 entities or fewer in the load area | F3 |
| `CavernAtmosphereTest.HumanOutcomePerWorld` [6] | An unequipped `MobHuman` for 60 s: Asclepiu and Merak take no damage; Carcinoma takes some Poison but is not critical; the others take air, heat or cold damage | F4 |
| `CavernAtmosphereTest.FaunaSurvivesItsCavern` [6] | Every fauna and deep-table mob, on a test map with that cavern's atmosphere, is alive and not critical after 30 s | F4 |
| `CavernEnvironmentTest.MirrorsClockAndShaftLight` | Within 10 s the cavern's environment has the ground's `PlanetName` and `MinuteOfDay` with weather "Underground", and its `MapLight` equals ground × `shaftLight` | F4 |
| `CavernAmbiencePrototypeTest.SoundsResolve` | Every loop and one-shot exists | F4 |
| `CavernAmbiencePlaybackTest` (client pair) | Entering the cavern starts the bed and shows the arrival popup once; climbing out stops the bed | F4 |
| `CavernHazardTest.VentHissesAndReleasesSmoke` [5] | A vent wall has `AmbientSound`; gathering it spawns an entity with `SmokeComponent` that spreads past one tile | F5 |
| `CavernHazardTest.UnstableRockWarnsThenCavesIn` | With the chance forced to 1, gathering shows the warning popup, then 1.5 s later drops 1–3 rubble within 2 tiles, never on a tile holding a mob, and damages mobs in the radius | F5 |
| `CavernHazardTest.DisturbanceWarnsThenAwakensOnce` | Warning at 75%; one deep group at 100%, 12–20 tiles away; none during the cooldown | F5 |
| `CavernClimbTest.ClimbHaulsPulledOreBox` | A pulled `OreBox` arrives on the ground next to the climber, is being pulled again, and the climb took 1.5× | F5 |

**Manual playtest** (`Docs/_WF/Caverns/PLAYTEST_CHECKLIST.md`, F6, in the Planets checklist's Do/See/Report format):
1. `wfplanet spawn WFSurfaceAsclepiu`, fly down and find the gate. The shade reads as a pit and the examine text
   gives the air.
2. Walk in: the plunge pool does no damage. Watch the light shaft and the darkness away from it. Use *Look up*
   through the mouth.
3. Climb out and climb back down. Park a lander across the mouth, then check from below that the climb is refused.
4. On Merak, dig a hole with a shovel and drop through it. On Aerumna, blow a hole and drop through it.
5. On each world, check the arrival popup, ambience, air readout, fall damage and signature feature.
6. On Aerumna, climb out at 3 g (10 s).
7. Log out for 60 s next to a parked hull. When you log back in, the hull is still on the ground.
8. Grep the server log for `[ERRO]`/`[FATL]` and the client log for `Sandbox violation`.

## 7. Features, in build order

Features ship one at a time on `planet-caverns` branches, and each one builds and tests on its own. Paths are
relative to the repo root. "No marker" means the file is inside `_WF`. Marked lines carry the exact text shown.

**Verification commands.** Each feature runs the subset it lists. Report failures with their output.

```sh
# V-build
dotnet build Content.Server/Content.Server.csproj -c DebugOpt
dotnet build Content.Client/Content.Client.csproj -c DebugOpt
dotnet build Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt
# V-test <Fixture>: one fixture per run (4 CPUs, 15 GB)
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt --no-build \
  --filter "FullyQualifiedName~Content.IntegrationTests.Tests._WF.Caverns.<Fixture>"
# V-planets: Planets regression subset
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt --no-build --filter \
  "FullyQualifiedName~Tests._WF.Planets.PlanetNetworkTest|FullyQualifiedName~Tests._WF.Planets.GroundContactTest\
|FullyQualifiedName~Tests._WF.Planets.LiftoffTest|FullyQualifiedName~Tests._WF.Planets.OrbitalMobFallTest\
|FullyQualifiedName~Tests._WF.Planets.PlanetWeatherTest|FullyQualifiedName~Tests._WF.Planets.PlanetEcologyTest"
# V-unit: pure logic
dotnet test Content.Tests/Content.Tests.csproj -c DebugOpt --filter "FullyQualifiedName~_WF.Caverns"
# V-server: headless boot, prototypes and systems load cleanly
timeout 240 dotnet run --project Content.Server -c DebugOpt --no-build -- \
  --cvar wf.planet_networks=true --cvar wf.caverns=true > server.log 2>&1; grep -E "\[(ERRO|FATL)\]" server.log
# V-client: with V-server running in the background
timeout 180 dotnet run --project Content.Client -c DebugOpt --no-build -- --headless --connect \
  --connect-address 127.0.0.1:1212 > client.log 2>&1; grep "Sandbox violation" client.log
# V-lint
dotnet run --project Content.YAMLLinter -c Release
# V-modules
python3 Tools/_WF/Ci/modules.py --write && python3 Tools/_WF/Ci/modules.py --check
```

### F1: Hooks and a dark cavern under every world

This feature lays the generic Planets hooks, the hull guard and the eye cap, and puts one placeholder cavern under
each of the six worlds. The caverns are roofed, dark and have their final air.

- **Add:**
  - Planets (no marker): `Content.Server/_WF/Planets/WFPlanetLowerLayersEvent.cs`,
    `Content.Server/_WF/Planets/WFPlanetNetworkBuiltEvent.cs`.
  - CCVar module: `Content.Shared/_WF/CCVar/CavernCVars.cs`.
  - Shared: `Content.Shared/_WF/Caverns/WFCavernPrototype.cs` (F1 fields only), `WFCavernLayerComponent.cs`.
  - Server: `Content.Server/_WF/Caverns/WFCavernSystem.cs` (the two handlers and the wildlife handler),
    `BiomeSystem.Caverns.cs`, `WFCavernGroundComponent.cs` (the `Cavern` and `Prototype` fields only).
  - Docs: `Content.Server/_WF/Caverns/README.md`, with a hand-written overview. Until this exists, the new
    `Docs/_WF/Caverns` folder fails `--check`.
  - Prototypes in `Resources/Prototypes/_WF/Caverns/`:
    - `caverns.yml`: six `wfCavern`;
    - `levels.yml`: six `planet` with final atmosphere and `mapLight`, all using the placeholder biome;
    - `tiles.yml`: `WFCavernFloorLimestone`;
    - `Biomes/placeholder.yml`: `WFCavernBiomePlaceholder`, the section 2.9 skeleton with only T and a
      `WallRock` rock template on ridged noise. It must not be upstream `Caves`, whose `FloorAsteroidSandPlanet` can
      be dug to void and whose `MonoPlanetmapOreBase` markers leak walls.
  - Locale: `Resources/Locale/en-US/_WF/Caverns/caverns.ftl` with names and map names.
  - Tests: `CavernFixture.cs`, `CavernNetworkTest.cs`, `CavernRoofTest.cs`, `CavernViewerEyeTest.cs`,
    `CavernHullTest.cs` (the F1 cases), `CavernWildlifeTest.cs`.
- **Planets edits** (no marker): `WFPlanetNetworkSystem.cs`, `WFPlanetNetworkComponent.cs`,
  `Administration/PlanetControlSystem.cs`, `CEZLevelsSystem.Wolfgate.cs` and the Planets README overview (section 2.2).
- **Marked Planets edits outside `_WF`:**
  - `Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.Transit.cs:106`:
    `// WOLFGATE(Planets): hulls leave orbit through transit and never hop below ground.`
  - `Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.Transit.cs:433`:
    `// WOLFGATE(Planets): hulls never descend below a planet's ground.`
  - `Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.Transit.cs:722`:
    `// WOLFGATE(Planets): a descending convoy lands on the ground instead of hopping below it.`
  - `Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.PilotControl.cs:217`:
    `// WOLFGATE(Planets): pilots can't descend below a planet's ground.`
- **Marked Caverns edits outside `_WF`:**
  - `CEZLevelsSystem.View.cs`, the eye cap (section 2.3, three lines).
  - `Resources/ConfigPresets/Build/development.toml`: `caverns = true`.
- **Tests:** every F1 row of section 6.
- **Verify:** V-build, V-test for each F1 fixture, V-planets (caverns off), V-server, V-lint, V-modules.
- **Accept:**
  - F1 tests and V-planets are green.
  - V-server shows no `[ERRO]` or `[FATL]`.
  - `--check` passes.
  - In a dev round, `wfplanet spawn WFSurfaceAsclepiu` builds six maps. A player mob (not a ghost: ghosts load no
    chunks) moved into the cavern with `tp <x> <y> <mapId>` stands in dark, roofed tunnels.

### F2: Entrances and exits

This feature adds mouths, the gate, lazy claims, the hole queue, falling, climbing, the orbital-fall fix and
`wfcavern`, on all six worlds, still over the placeholder biome.

- **Add:**
  - Shared: `WFCavernShaftComponent.cs`, `WFCavernClimbComponent.cs`, `WFCavernClimbDoAfterEvent.cs`,
    `SharedWFCavernClimbSystem.cs`, `WFCavernAir.cs`. The mouth spec is added to `WFCavernPrototype`.
  - Server: `WFCavernMouthSystem.cs`, `WFCavernMouthSystem.Claims.cs`, `WFCavernMouthSystem.Holes.cs`,
    `WFCavernClimbSystem.cs`, `WFCavernCommand.cs`. `WFCavernGroundComponent` gains the registry.
  - Prototypes:
    - `Entities/mouths.yml`: `WFCavernShadeBase`, `WFCavernClimbBase`, and six of each;
    - `tiles.yml`: the six landing tiles;
    - `caverns.yml`: the `mouths:` blocks from section 4.
  - Locale: `caverns.ftl` (examine, verbs and popups), `entities.ftl`, `commands.ftl`.
  - `WolfgateAdminCommands.Cavern` (no marker).
  - Tests: `CavernMouthTest.cs`, `CavernFallTest.cs`, `CavernHoleTest.cs`, `CavernClimbTest.cs`, `CavernHullTest.cs`
    (the F2 cases), `CavernOrbitalFallTest.cs`, `CavernCommandTest.cs`, and
    `Content.Tests/_WF/Caverns/CavernAirTest.cs`.
- **Planets edit** (no marker): `Flight/WFOrbitalMobFallSystem.cs`, `IsSurfaceImpact` (section 2.2).
- **Marked edits:** none.
- **Tests:** every F2 row of section 6.
- **Verify:** V-build, V-test for each F2 fixture, V-unit, V-planets, V-server, V-client (the climb system is shared
  code), V-lint, V-modules.
- **Accept:**
  - F2 tests are green.
  - No `Sandbox violation` appears.
  - On a real client: walk into the gate, land, climb out, climb down. Check for a visible fall stutter on the first
    drop into an unseen cavern (risk 1).

### F3: The six geologies

This feature replaces the placeholder biome with each world's biome from section 4.

- **Add:** `Resources/Prototypes/_WF/Caverns/Biomes/<world>.yml` ×6 (biome, rock, chamber and signature templates);
  the remaining WF tiles; `Entities/decor.yml` (`WFCavernGlowworms`, `WFCavernGutGlow`); `WFCavernFauna<World>`
  markers and tables; the `wfcavern stats` subcommand; the shared `WFCavernSampler` (server) that tests and `stats`
  both use; and the tests `CavernPrototypeTest.cs`, `CavernBiomeTest.cs`, `CavernGenerationTest.cs`.
- **Edit:** `levels.yml` points each level at its biome. Delete `Biomes/placeholder.yml`.
- **Tests:** every F3 row of section 6, plus a rerun of `CavernMouthTest.GateExists`, since the cavern check
  depends on the biome.
- **Verify:** V-build, V-test (F3 fixtures and `CavernMouthTest`), V-server, V-lint, V-modules.
- **Accept:**
  - The noise tests fall inside their bands; tune the noise until they do.
  - The linter and V-server are clean.
  - A visual pass on each world matches section 4.

### F4: Air, light, life and sound

- **Add:** `Content.Client/_WF/Caverns/WFCavernAmbienceSystem.cs`; the ambience spec in `WFCavernPrototype` and its
  `ambience:` blocks; the environment and shaft-light mirror in `WFCavernSystem`; arrival keys; and the tests
  `CavernAtmosphereTest.cs`, `CavernEnvironmentTest.cs`, `CavernAmbiencePrototypeTest.cs`,
  `CavernAmbiencePlaybackTest.cs`.
- **Edit:** the `WFCavernFauna<World>` tables and the atmospheres in `levels.yml`, where the tests demand. A mob
  that fails `FaunaSurvivesItsCavern` is dropped or gets a `WF` variant with `Temperature` overrides, as the basalt
  argocytes do.
- **Tests:** every F4 row of section 6.
- **Verify:** V-build, V-test (F4 fixtures), V-server, V-client, V-lint, V-modules.
- **Accept:**
  - F4 tests are green.
  - No `Sandbox violation` appears.
  - Watches read "Underground" below.
  - The shafts dim at night.

### F5: The mining loop

This feature adds vents, unstable rock and cave-ins, disturbance and deep tables, and hauling.

- **Add:**
  - Server: `WFCavernUnstableComponent.cs`, `WFCavernStateComponent.cs`, `WFCavernHazardSystem.cs`.
  - Prototypes: `Entities/hazards.yml` (`WFCavernUnstable<World>` walls, tinted and parented to the world's wall with
    `WFCavernUnstable`; `WFCavernVent<World>` walls with `OreVein currentOre: WFCavernOreGas<World>` and
    `AmbientSound` `/Audio/Ambience/Objects/gas_hiss.ogg` at range 3; and `WFCavernGasPocket<World>`, built from
    `TriggerOnSpawn`, `SmokeOnTrigger` with the reagent from section 4, and `TimedDespawn`); `ores.yml`
    (`WFCavernOreGas<World>`, whose `oreEntity` is the gas pocket); and `WFCavernDeep<World>` tables.
  - Hazard specs and rock-template vein layers.
  - Hauling in `WFCavernClimbSystem`: stop the pull, move the pulled entity with the climber, then `TryStartPull`.
  - The `wfcavern awaken` subcommand.
- **The rules:**
  - `(WFCavernUnstableComponent, GatheredEvent)` is a new pair.
  - Mining unstable rock adds 1 disturbance, and rolls `caveInChance`. On a cave-in, the ceiling-groans popup
    (range 6) comes first, then 1.5 s later rubble falls: up to 3 of the world's plain wall on open tiles within 2,
    never on a mob's tile. Mobs in the radius take `caveInDamage` (10 Blunt) and a 2 s knockdown.
  - A vent adds 3 disturbance.
  - Disturbance decays 3 per minute. At 75% of the threshold, cavern players within 20 tiles see the warning. At 100%,
    one deep group spawns on an open, loaded floor tile 12–20 tiles from the miner, disturbance resets, and a 20 min
    cooldown starts.
- **Tests:** every F5 row of section 6.
- **Verify:** V-build, V-test (F5 fixtures and `CavernClimbTest`), V-server, V-lint, V-modules.
- **Accept:** F5 tests are green. A dev round shows every warning before its hazard.

### F6: Guidebook and docs

- **Add:** `Resources/ServerInfo/_WF/Caverns/Caverns.xml`, `Resources/Prototypes/_WF/Caverns/Guidebook/caverns.yml`,
  `guidebook.ftl`, `Docs/_WF/Caverns/PLAYTEST_CHECKLIST.md`, and the final README overview. Then run
  `modules.py --write`.
- **Marked edit:** `Resources/Prototypes/_NF/Guidebook/expeditions.yml` gains this child line:

  ```yaml
    - WFCaverns # WOLFGATE(Caverns): cavern field guide
  ```
- **Tests:** the existing guidebook and prototype tests (the XML parses and the entry resolves), and a full run of
  the Caverns folder, one fixture at a time.
- **Verify:** V-build, V-test (every Caverns fixture), V-planets, V-server, V-client, V-lint, V-modules
  (`--pr-check origin/<base>` too).
- **Accept:**
  - Every fixture is green.
  - The playtest checklist has been walked once on a dev server, with its results in the PR.
  - The PR template is filled, with a `:cl:` entry.

## 8. Risks

| # | Risk | Check / mitigation |
|---|---|---|
| 1 | The client has never seen the cavern map before its first fall, so predicted z-physics may stutter until PVS arrives (`Update.cs:22`, `_clientSimulation`) | Real-client check in F2. Fallback: `CEPvsOverride` on the cavern map entity, after measuring `BiomeComponent`'s state size |
| 2 | Cavern chunk loads are dense, about 1,300 walls around one cavern viewer | `CavernGenerationTest` bounds it at 2,500. Profile one viewer per world in F3 |
| 3 | FTL preloads or admin teleports load a cell before its claim | That cell stays Deferred until the chunk unloads. Accepted: a mouth may appear late, never cut into loaded terrain |
| 4 | An awake item over a ground chunk that unloads falls into the cavern void (2.1 item 4) | Wildlife is handled (F1). Items are rare, because sleeping bodies don't fall. Accepted |
| 5 | A player floor laid on a pad and deconstructed down to space opens the bottom layer, because pads aren't natural terrain for `WfIsPlanetTerrain` | Needs deliberate multi-step work, and the result is "stuck in the floor", not death. Accepted |
| 6 | A hole opened over a player-built cavern structure lands the faller inside it (the queue only clears biome walls) | Rare. Accepted |
| 7 | Debris of 2×2 or less inside a hole churns up and back | This is today's behaviour over unloaded terrain. Covered by `SmallDebrisOverMouthNeverEntersCavern` |
| 8 | Fauna may not survive CO₂, ammonia, heat or cold | `FaunaSurvivesItsCavern` decides the tables in F4 |
| 9 | `SmokeOnTrigger` may not spread on a map grid without `GridAtmosphere` | `VentHissesAndReleasesSmoke` decides. Fallback: vents spawn a puddle of the reagent instead |
| 10 | Restarting a pull across a map change may be refused | `ClimbHaulsPulledOreBox` decides. Fallback: move the hauled entity without restarting the pull |
| 11 | `GetNoise` allocates on every call, so claims cost about 1 ms per candidate and sampled tests take seconds per world | Claims are spread over time and logged above 20 ms. If tests are too slow, add a cached sampler to `BiomeSystem.Caverns.cs` and assert it agrees with `TryGetTile` |
| 12 | Six more maps share the 128 fauna cap | Cavern fauna retires without cavern observers, thanks to the eye cap. Watch `PlanetPopulationTest` |
| 13 | For the first 0.1 s after a load, a cavern chunk can show shaft light before the ground above it loads | Cosmetic. Both load in the same `BiomeSystem` pass |
| 14 | The eye cap and hull guard touch CE files that change upstream | Single-line marked edits that keep the upstream expression. Recheck on every CE merge |
| 15 | `Nocturine` spore pockets in Aerumna's dark with xenos may be too punishing | Small spread (≤ 6 tiles) and the hiss warns first. Tune after the F5 playtest |
| 16 | `MobWatcherMagmawing` and `MobWatcherIcewing` are flying lavaland mobs | `FaunaSurvivesItsCavern` checks them, and no cavern mob has `CEZFlyer` |
