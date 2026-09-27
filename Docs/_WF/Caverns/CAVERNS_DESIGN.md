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
   - `FloorWater` already has `fallDamageMultiplier: 0`, but its `WaterOverlay` shader draws white foam wherever it
     meets another tile, which reads as a white border on a cave floor. Asclepiu's pools therefore use the surface's
     water (`MonoFloorWaterEntity` on dirt), and its landing is `WFCavernFloorPoolbed` (dirt, fall ×0) under that
     entity.
6. **Anchoring onto an empty tile is refused** (`SharedMapSystem.Grid.cs:1285`, `AddToSnapGridCell`). Anything that
   marks a hole is therefore unanchored.
7. **Chunk loading** (`Content.Server/Parallax/BiomeSystem*.cs`):
   - Chunks load around attached non-ghost players and every entity in their `ViewSubscriptions` (`PlayerTracker.cs`).
   - The load area is `ceil(net.pvs_range / 8) × 8` = ±32 tiles, chunk-aligned, so up to about 40 tiles out.
   - One chunk per biome unloads every 10 s.
   - `UnloadTiles` keeps only modified tiles and tiles holding an entity anchored to *that grid*. Ground under a
     parked hull therefore empties (`ChunkLoader.cs:~245`), except where a biome entity the hull touched still stands:
     it is no longer default, so `UnloadEntities` keeps it and pins its tile, and that one tile keeps the hull
     supported (`HasGroundUnderFootprint`).
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
   biome chunks load around it: item 7's fixed area, whatever the eye's PVS scale. Without a change, every ground, air
   and orbit viewer, ghosts included, would generate the cavern under them. `UpdateViewer` runs only when a viewer is
   dirtied (attached, moved to another map, or queued); every second `UpdateView` just moves and rescales the eyes. On
   its own, CE's client renders nothing below a `CEZGroundLayer` map; the cavern view opens it at a mouth (2.7). Map
   entities themselves are force-sent to every client (RT's `PvsOverrideSystem.OnMapCreated`), so a client always
   has the cavern map; what stands on it arrives only through an eye.
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
12. **Orbital falls.** `WFOrbitalMobFallSystem.IsSurfaceImpact` only accepts `MapUid == Ground`, so an orbital faller who
    drops through a mouth skips the orbital rule (one arm and one leg severed, left critical) and lands as an ordinary
    fall. Orbit is depth 4 (three air layers), so the fall is five levels and hits at about 10 m/s, far below the
    20 m/s cap (which needs about 20 levels): `(int)(v² × 0.75 × tile multiplier)`, about 75 Blunt on a ×1 tile.
    F2b measured it with the fix reverted: 58 Blunt on Fervidus ash (×0.75), alive and unmaimed; 116 on Aerumna scree
    (×1.5), critical and unmaimed. Asclepiu's water (×0) would make a mouth a free way down from orbit.
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
`WFPlanetLayerComponent`. An orbital faller who lands in a cavern is then maimed and left critical exactly as on the
ground, instead of taking an ordinary fall scaled by the landing tile (2.1 item 12).

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

- **Eye cap** (F1, opened at mouths by the cavern view), in `Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.View.cs`,
  `UpdateViewer`. It is required, not optional (verified in 2.1 item 9). There are three marked lines:

  ```csharp
  var wfAbove = map.Value; // WOLFGATE(Caverns): the level above the next eye, for the ground cap below.
  for (var i = 1; i <= MaxZLevelsBelowRendering; i++)
  {
      if (WfEyesStopUnder(ent, map.Value, wfAbove, globalPos, pvsScale)) // WOLFGATE(Caverns): under a ground layer, eyes only on its cavern and only while a hole is in view.
          break;

      if (!TryMapOffset(map.Value, -i, out var mapUidBelow))
          break;

      SpawnViewerEye(eyes, actor, map.Value, mapUidBelow, globalPos, pvsScale);
      coveredMaps.Add(mapUidBelow);
      wfAbove = mapUidBelow; // WOLFGATE(Caverns): track the level above the next eye.
  }
  ```

  `WfEyesStopUnder` (`Content.Server/_WF/Caverns/CEZLevelsSystem.Caverns.cs`) stops the walk under a ground layer
  unless `WFCavernEyeSystem.SeesCavern` finds one of its holes in the viewer's view, and always under a cavern looked
  into from above, so only that one level opens. The rule:
  - A hole is an open hole tile: an entry in `WFCavernGroundComponent.Shades`. A hole with no shade doesn't count;
    the F2c hole queue gives dug and blown holes theirs.
  - A viewer sees a hole when one lies within the square an eye on the ground sees, plus 4 tiles (`EnterMargin`).
    That square's half-size is half of `net.pvs_range` (RT's cvar is the side, `PvsSystem.CalcViewBounds` halves it)
    times the PVS scale the ground eye gets: the viewer's own scale and zoom, widened for each level it is above the
    ground, as `GetZEyePvsScale` does. A ground viewer at zoom 1 gets the cavern with a hole 16.5 tiles away. A viewer
    that has the cavern keeps it until every hole is more than 12 tiles past that square (`LeaveMargin`, 24.5 tiles
    on the ground); `WFCavernViewerComponent` records which ground it sees into, so eyes don't churn at the edge.
  - A ghost that may not load terrain (`BiomeSystem.CanLoad`, through `WfCanLoad`: a ghost without the
    `AllowBiomeLoading` tag) never gets the cavern. Its eye has no `GhostComponent`, so it would generate the cavern
    at every mouth the ghost passes while the ground under the ghost stays unloaded. An admin ghost has the tag.
  - `WFCavernEyeSystem` measures every viewer against the holes twice a second and, when the answer changes, queues
    the viewer for `UpdateViewer` (`WfQueueViewerUpdate`), which asks again and records the answer.
    `WfGroundInView` repeats the walk to find the ground. A check compares the viewer with each shade on that ground,
    stopping at the first in range.

  How this affects each kind of viewer:
  - **Ground viewer:** gets an eye on the cavern while a hole is in view, and none otherwise.
  - **Ghosts:** an observer gets none, as it loads no ground either; an admin ghost gets one like a player.
  - **Air and orbit viewers:** keep their eyes down to the ground, and get one on the cavern on the same rule, over
    the wider range their height gives them. It falls out of the walk; nothing tells them apart.
  - **Cavern viewer:** has nothing below. It keeps its eye on the ground above, so the ground over it stays loaded
    and roofs it.
  - **Networks without a map below ground:** nothing changes.

  Cost: a cavern eye loads the same 81 cavern chunks a cavern viewer does. `GroundViewerNearMouthLoadsCavern`
  measured 3,744 entities on the Asclepiu cavern around the gate. With PVS on (test pairs run without it, so the test
  turns it on), 1,129 of them reached the client within 90 ticks: those in the cavern eye's square, 29 tiles across
  for a ground viewer (38 for PVS priority entities such as lights), sent within the 50-new-entities-a-tick budget it
  shares with the ground. A ground viewer far from every mouth is sent none.
- **Cavern pass**, in `Content.Client/_CE/ZLevels/Core/ScalingViewport.CEZLevels.cs`, `RenderZLevels`: two marked
  lines, where the view stops at a ground layer (the observer's own, and one the downward walk reaches), call
  `WfAddCavernPass` (`Content.Client/_WF/Caverns/ScalingViewport.Caverns.cs`). See 2.7.
- **Sky**, in `Content.Client/Parallax/ParallaxOverlay.cs`, `BeforeDraw`: one marked line (and its `using`) draws no
  parallax on a map `WFCavernViewSystem.HidesSky` names. See 2.7.
- **`Resources/ConfigPresets/Build/development.toml`** (F1b, landed): inside the existing `[wf]` table, which sits
  inside the `WOLFGATE(Planets)` block, add `caverns = true` with
  `# WOLFGATE(Caverns): caverns are on in development builds.` on the line above. TOML forbids a second `[wf]` header,
  so the line has to live in that block. A single-line marker inside a block passes `modules.py`. It landed with the
  hull guard and the eye cap, never before: without them a dev build sinks unsupported hulls into the cavern and loads
  cavern chunks under every viewer. Development builds now have caverns on.
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
| `seedOffset` | `int` | Cavern seed = `surface.Seed + seedOffset` (unchecked); the ground's rolled seed stands in for a surface without one |
| `roofColor` | `Color` | `RoofComponent.Color`, the ambient under solid ground |
| `shaftLight` | `float`, default 0.5 | Cavern `MapLight` = the ground's current `MapLight` × this (F4), so shafts dim at night |
| `arrival` | `LocId` | Popup shown on entering the cavern (F4) |
| `mouths` | `WFCavernMouthSpec` (F2) | See below |
| `ambience` | `ProtoId<WFPlanetAmbiencePrototype>?` (F4) | A soundscape played underground instead of the surface's, clear. Unset on all six: the cavern plays the surface's |
| `surfaceAmbienceVolume` | `float`, default −6 | dB taken off the surface soundscape heard below (F4) |
| `surfaceAmbienceOcclusion` | `float`, default 2.5 | Audio occlusion on the surface soundscape heard below: the engine's low-pass (F4) |
| `hazards` | `WFCavernHazardSpec?` (F5) | `caveInChance`, `caveInDamage` (`DamageSpecifier`), `rubble`, `disturbanceThreshold` (60), `disturbanceDecay` (3/min), `awakeningCooldown` (1200 s), `deepTable` (`ProtoId<EntityTablePrototype>`) |

`WFCavernMouthSpec`:

| Field | Default | Meaning |
|---|---|---|
| `cellSize` | 96 | Tiles per mouth cell |
| `style` | Round | How the hole is grown from its seed: `Round` (an ellipse), `Blob` (an ellipse whose edge wanders) or `Rift` (a wandering crack) |
| `minTiles`, `maxTiles` | 4, 9 | The hole's size range in tiles |
| `elongation` | 1.2 | Round and Blob: largest ratio of the long axis to the short one; the axis turns at random |
| `roughness` | 0.1 | Round and Blob: how far the edge wanders in and out, as a share of the radius |
| `riftWidth` | 2 | Rift: its widest stretch, 1 or 2 tiles |
| `gateOffset` | (0, 24) | Gate candidate relative to the planet centre |
| `groundTiles` | required | Natural ground tiles a mouth may cut |
| `avoid` | empty | Natural ground entities a footprint must not touch (liquids, boulders) |
| `landingTile` | required | Cavern tile under the hole |
| `landingEntity` | none | Anchored on every pad tile that is the landing tile, and on every other pad tile where the cavern would grow it, since pinned tiles grow no biome entities: Asclepiu's water. The cavern check (3.2) counts it as open floor |
| `padRadius` | 3 | Pinned, rock-free pad around the hole in the cavern |
| `climbSide` | South | Side of the hole whose lip holds the climb point |
| `shade` | required | Unanchored pit entity over each hole tile |
| `climbPoint` | required | Anchored climb entity in the cavern under the lip |
| `rim` | empty | Decor anchored on random lip tiles, never on or beside the climb tile |
| `rimCount` | 3 | About how many rim decor entities a hole at the top of the size range gets; a smaller one gets proportionally fewer, at least one |
| `climbSeconds` | 4 | Base climb-up time, × `clamp(surface gravity, 1, 2.5)` |

A cavern's own `ambience` is a `wfPlanetAmbience` with no `planetType`: the network picks a surface's soundscape by
planet type, so one without is never picked for a surface, and `PlanetAmbiencePrototypeTest` counts only those with
one, though it checks every profile's files. There is no `levels` list: v1 is −1 only, and the Planets event list already supports a −2 later ("the Heart" under
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
    style: Blob
    minTiles: 8
    maxTiles: 18
    elongation: 1.8
    roughness: 0.3
    groundTiles: [FloorBasalt]
    avoid: [FloorLavaEntity, MonoPlanetmapOreBasalt]
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
| `WFCavernGroundComponent` | Server | ground map | `Cavern`, `Prototype`, `Centre` (the planet centre, for the gate candidate), `Cells` (`Dictionary<Vector2i, WFCavernCell>`: `State` = Unclaimed/Claimed/Deferred/Empty (`WFCavernClaim`), `Evaluated`, cached `Site`: anchor and shape), `Mouths` (list of `WFCavernMouth`: `Origin` (the anchor), `Shape`, `Hole`, `Ring`, `ClimbTile`, `Kind` = Gate/Cell/Hole/Admin), `Shades` (`Dictionary<Vector2i, EntityUid>`), `ClimbPoints` (`Dictionary<Vector2i, EntityUid>`) |
| `WFCavernUnstableComponent` (F5) | Server | unstable rock and vent walls | `Disturbance` (int), `CaveIn` (bool) |
| `WFCavernStateComponent` (F5) | Server | cavern map | `Disturbance`, `Warned`, `NextAwakening` |

Every component is `UnsavedComponent`. `WFCavernAir` is a shared enum with a pure classifier,
`WFCavernAirClassifier.Classify(GasMixture)` (a C# enum can't carry a method, so the classifier is a static class beside
it), applied in this order:
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
    the void (2.1 item 4). The handler reads the ground tile above the landing position: it is deleted only when that
    tile is empty, not pinned (`WfIsPinned`) and its chunk is not loaded (`WfIsChunkLoaded`). A hole dug or blown in a
    loaded chunk is pinned only when that chunk unloads, so until then the loaded check is what keeps its fallers.
    A mob with a mind is never deleted. The event is raised inside the z-physics pass, so the delete is a `QueueDel`.
  - F4 (built): every second it mirrors the ground's `WFPlanetEnvironmentComponent` onto the cavern, with `Weather`
    set to `wf-cavern-weather-underground`, and gives the cavern a `WFPlanetAmbienceComponent`: the cavern's own
    `ambience` at no offset and no occlusion, or else the ground's profile at the ground's offset plus
    `surfaceAmbienceVolume`, with `Occlusion = surfaceAmbienceOcclusion`. Still to come: the cavern `MapLight` =
    ground `MapLight` × `shaftLight`.
- **`WFCavernMouthSystem`** (F2), with partials `.Claims.cs` and `.Holes.cs`: cells, claims, stamping, the hole queue,
  shades, climb points and the registry. Test and admin API: `GetGate`, `TryClaimCell` (returns
  Claimed/Deferred/Empty), `TryOpenMouth(ground, origin, out refusal, ignore)`, `TryGetNearestMouth`; `ClaimGate` is
  what `WFCavernSystem` calls from the built event. F2a landed the main file and `.Claims.cs` (candidates, pure
  checks, state checks, stamping); the polling loop that claims ahead of viewers (F2c) calls `TryClaimCell`, and
  `.Holes.cs` (F2c) adds the hole queue.
- **`WFCavernClimbSystem : SharedWFCavernClimbSystem`** (F2b): the move itself, server only, on
  `(WFCavernClimbComponent, WFCavernClimbDoAfterEvent)` (up) and `(WFCavernShaftComponent, WFCavernClimbDoAfterEvent)`
  (down). Test API: `ClimbUp`, `ClimbDown`, `FindExit` (3.6) and `TryFindLanding` (3.5). Hauling is F5.
- **`WFCavernHazardSystem`** (F5): cave-ins, vents and disturbance.
- **`WFCavernCommand`** (F2): `wfcavern`.
- **`BiomeSystem.Caverns.cs`** (`Content.Server/_WF/Caverns/`, namespace `Content.Server.Parallax`) exposes two
  helpers that need the protected `ChunkSize`:
  - `WfIsChunkLoaded(Entity<BiomeComponent>, Vector2i)` (F1b; the wildlife handler is its first user);
  - `WfIsBiomeSpawned(Entity<BiomeComponent>, EntityUid, Vector2i)` (F2).

  Pinning uses the existing Planets `WfPinTiles` and `WfIsPinned`. Pure evaluation uses the public
  `TryGetTile`/`TryGetEntity` with `grid: null`.

### 2.7 Client

- **`SharedWFCavernClimbSystem`** (shared, abstract, F2b): the verbs *Climb up* and *Climb down* (`AlternativeVerb`s),
  *activate in world* on climb points, the DoAfter (`BreakOnMove`, `BreakOnDamage`, `NeedHand = false`,
  `BlockDuplicate`, `MultiplyDelay = false`), predicted start popups and the climb-point examine. Verbs require
  `CanAccess && CanInteract`, and the climber must stand on the climb point's or shade's own grid, so nobody climbs
  down through the deck of a hull parked over a hole. `MultiplyDelay` is off because climbing needs no hands: the
  hands' DoAfter multiplier (0.9 per human hand) would otherwise cut Aerumna's 10 s to 8.1 s. The client registers
  it through an empty `WFCavernClimbSystem` in `Content.Client/_WF/Caverns`, so the verbs and popups are predicted.
- **`SharedWFCavernShaftSystem`** (shared, F2a): the shaft examine on shades (`(WFCavernShaftComponent,
  ExaminedEvent)`). It landed before the climb system, so it is a system of its own; the climb system subscribes other
  events on the same components.
- **Cavern view** (after the shaped mouths): a hole shows the cavern under it, drawn by CE's z-level renderer.
  - `WfAddCavernPass` adds the cavern as a pass one level below a ground layer, and moves the floor of the view down
    to it, when this client has the cavern's map (`WFCavernViewSystem.TryGetCavernBelow`) and a shade lies in its view
    of the ground (`WFCavernShadeVisualsSystem.AnyPitWithin`, over the observer's view plus a tile, widened and
    shifted as the ground's pass is: `WFCavernViewSystem.LevelViewBox` widens by the pass's absolute depth and shifts
    by its depth below the observer, as the pass eye is built). `CavernPassDepth` and `LevelViewBox` are pure
    functions. A mouth opens it, not any empty tile, so unloaded ground at the edge of a far view from the air keeps
    the sky it had.
  - The painter's order does the rest: the cavern pass draws first and clears to black, the ground's empty tiles draw
    nothing, and the cavern shows only through its holes, shrunk and offset like any level below
    (`ZLevelViewShrink`, `ZLevelOffset`).
  - Lighting: RT lights each tile and sprite from its own pass's light map, so the ground's light never falls on the
    cavern twice or darkens it. The cavern lights itself: its `MapLight` where the roof is open (a hole's `Space` is
    transparent, so the cavern under it is unroofed, 2.1 item 10), its roof colour under the ground, its own glowing
    plants. It has no sun shadows. `CEZLevelShadowOverlay` leaves out the ground map-grid itself (`includeMap:
    false`), so only hulls above shadow it; `CEZLevelBlurOverlay` blurs it and tints it towards its ambient like any
    level below. Lower passes never draw FOV; the observer's own pass still blacks out what its FOV hides, the cavern
    included.
  - Sky: `WFCavernViewSystem.HidesSky` keeps the parallax off a cavern (whose fallback would be space) and off a
    ground while one of its mouths is in view, so a hole never shows sky, even before the cavern's contents stream
    in. Elsewhere nothing changes: a ground player never had parallax over a cavern (`TryMapDown` finds it), and a
    ground seen from the air with no mouth in view keeps its sky.
  - Cost: one more render pass while a mouth is on screen; the decision each frame walks the shades the client knows.
  - Clicking: the shades' `Clickable` bounds cover the whole tile (`ClickableSystem` tries them before the click map),
    so a hole's clear middle still examines and offers *Climb down*.
- **Ambience** (F4, built differently from the plan): no cavern player. By default the underground hears the
  surface: the Planets `WFPlanetAmbienceSystem` plays whatever `WFPlanetAmbienceComponent` the listener's map carries,
  and the cavern's copy (2.6) names the surface's profile with a lower `VolumeOffset` and an `Occlusion`.
  - The player eases its own occlusion toward the map's at 3 a second and writes it to its bed, outgoing bed and
    accent. `Occlusion` drives the engine's EFX low-pass, and the engine leaves it alone on global streams.
  - The cavern's clock is the ground's, so the day and night beds change below as above, muffled.
  - Within one world (the same `WFPlanetLayerComponent.Network`) a profile change crossfades instead of cutting, so
    a cavern with its own `ambience` fades the surface bed out; going down with the default keeps the same stream and
    only its volume and muffle ease, over about a second. Crossing to another world is still a hard stop.
  - Ghosts and observers hear what their map carries, like anyone else. Music stays suppressed below, as on the
    surface (caverns carry `WFPlanetLayerComponent`).
  - Weather and thunder stay on the surface: the cavern gets no `WeatherComponent` (the weather system applies only to
    `Layers`), and thunder is a positional `PlayStatic` on the ground map. A muffled rumble below would need a new
    cross-map channel, which the roofed caverns don't call for.
  - The `arrival` popup is not built yet.

### 2.8 Settings

`CavernCVars.Caverns` (`wf.caverns`, `CVar.SERVERONLY`) defaults to **false**. Production and the Planets test suite
stay unchanged until caverns are signed off. `development.toml` turns the CVar on since F1b, which landed it together
with the hull guard and the eye cap, so development builds have caverns on. It is read at build time, so it affects
networks built after it changes.

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
- **Decals** in a nested template are looked up against the floor already on the grid, since `LoadDecals` runs after
  `LoadTiles`, so in game a decal template needs no tile layer. Without a grid (a sampler or a preview) `TryGetDecals`
  reads the tile from each template's own layers instead, so a decal template repeats its parent's floor to show there
  (the Gut's pool edge, 4.7). No decal lands on a tile that holds an anchored entity (`BiomeSystem.LoadDecals`), which
  keeps the bile out from under the acid. Decals cost no entities.

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
| `WFCavernFloorPoolbed` | `FloorPlanetDirt` | no tools, fall 0 / stun 0.1 (already indestructible); Asclepiu's landing, under `MonoFloorWaterEntity` |
| `WFCavernFloorGut` | `FloorAsteroidIronsand` | indestructible, no tools, fall 0.4 / stun 0.4 |

`FloorBasalt`, `FloorIce`, `FloorSnowDug` and `FloorPlanetDirt` are used as they are. `FloorWater` is not used: its
overlay foams white against any other tile, so cavern water is the surface's `MonoFloorWaterEntity`.

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
Ground (depth 0), a 12-tile blob             Cavern (depth −1), padRadius 3
  . . . . . . . . .                          p p p p p p p p p p p p p
  . . r r r r r . .    H  hole (empty)       p p p p p p p p p p p p p
  . r r H H H r k .    r  lip ring           p p p p p p p p p p p p p
  . r H H H H r r .    k  rim decor          p p p p p L L L p p p p p
  . r H H A H H r .    A  anchor (a hole     p p p p L L L L p p p p p    L  landing tile
  . k r H H H r r .       tile)              p p p L L L L L L p p p p    p  pad: natural floor, no rock
  . . r r c r r . .    c  climb tile         p p p p L L L L p p p p p    C  climb point, under c
  . . . . . . . . .                          p p p p p C p p p p p p p
                                             p p p p p p p p p p p p p
```

Every tile shown above is pinned (`WfPinTiles`), so the biome never regenerates, fills or unloads it.

**Shapes.** A mouth's hole is grown by `WFCavernMouthShape.Generate(spec, seed)`, a pure function: the same spec and seed
give the same hole in every process, so a site never depends on when its cell is claimed.
- **Round and Blob:** an ellipse of a random area in `[minTiles, maxTiles]`, stretched up to `elongation` along a
  random axis, its edge moved in and out by three harmonics with random phases scaled by `roughness`, then one
  majority pass so lone bumps fill out and lone notches close.
- **Rift:** a crack walked one tile per step along a heading that drifts but stays within 0.6 rad of its first
  direction, so it never turns back; a diagonal step gets an elbow tile, and stretches widen to 2 tiles at random when
  `riftWidth` is 2.
- **Tidy:** enclosed ground is filled, two tiles touching only at a corner get a tile between them, tiles hanging on by
  one edge are trimmed (a rift keeps its two ends), and the largest edge-connected piece is kept.
- **Fit:** `Fits` checks the tidied hole again rather than trusting Tidy's few passes: inside the size range, not a
  plain rectangle (straight sides read as a dug square whatever the art does), joined, with no spur, pinch or enclosed
  ground. A hole that fails is grown again, up to 16 times. Only then does the fallback stand in (`Fallback` is set):
  the smallest digital disc that holds `minTiles` and is not a square, or for a rift a one-tile staircase. No seed in
  2,000 per world needed it. A hole with every tile on two sides can't have 5 or 6 tiles without being a rectangle, so
  the smallest round hole is 7 tiles (Thrascias).
- **Anchor:** the hole tile nearest its centroid becomes `(0, 0)`. The **ring** is every tile touching the hole,
  diagonals included, that is not hole. The **climb tile** is the ring tile farthest out on `climbSide` with a hole tile
  beside it, nearest the centroid across that side, so with the climb side south it lies south of the whole hole.
  **Rim spots** are ring tiles picked at random, never on or beside the climb tile, nor beside each other: `rimCount`
  of them for a hole at the top of the size range, proportionally fewer for a smaller one, less one at random, and
  at least one, so a small moulin isn't ringed with crystals.

The pit art rounds what the tiles can't (4.1): it is drawn on the dual grid, so the lip overhangs the hole's own tiles
and a hole shows none of the tile grid's square corners.

### 3.2 Cells and claims

- **Cells.** Cell index = `floor(tile / cellSize)` on the ground grid.
- **Polling.** Every 0.5 s, `WFCavernMouthSystem` collects claim sources for each ground map with
  `WFCavernGroundComponent`:
  - every session's attached entity, if it is on that ground or its cavern;
  - every view-subscription eye on either map.
- **Claim range.** Each unclaimed cell whose square intersects a ±96-tile box around a source is claimed. Chunks load
  up to about 40 tiles out (2.1 item 7), so a claim lands at least 56 tiles ahead of the load front. Air-layer ships
  at 12 m/s move 6 tiles per poll.
- **Candidates.** The gate cell, the cell holding `Centre + gateOffset`, tries that tile first, with a shape seeded by
  `WFCavernMouthShape.SeedAt(seed, tile)`. Every cell then tries 8 candidates drawn from
  `new System.Random(unchecked(seed * 7919 + cell.X * 73856093 + cell.Y * 19349663))`: each draws a shape seed and two
  fractions that spread its anchor uniformly over the spots where its whole pad stays 4 tiles inside the cell
  (`CellMargin`). A shape too big for that is skipped.
  - The seed is plain arithmetic, never `HashCode.Combine`, which is randomised per process. It is the ground
    biome's seed.
  - The candidate is the hole's anchor, the hole tile nearest its centroid.
- **Site.** The cell's site is the first candidate that passes both pure checks. Pure checks read only noise, so
  the site never depends on when the cell is claimed:
  1. **Ground.** Every tile of the shaped footprint (hole plus ring) has a natural tile (`TryGetTile`, `grid: null`) in
     `groundTiles`, and no natural entity (`TryGetEntity`) in `avoid`. This keeps mouths off seas, lava rivers,
     plasma lakes, blood channels and boulders.
  2. **Cavern.** The natural cavern entity at the anchor is empty (or the `landingEntity`, so a pool counts as open
     floor), and so are at least 3 of the 4 points 2 tiles past
     the hole's extent along each axis from the anchor (`OpennessMargin`, N/E/S/W). The pad then sits in a chamber, a
     junction or a tunnel wider than the hole: a straight ridged tunnel 3-4 tiles wide leaves the two points across it
     in rock. The cavern check runs first, as it is five lookups. F2a found the F1
     placeholder's tunnels alone passed about 1 candidate in 9, and none of 81 around Aerumna's centre, so the
     placeholder biome gained the section 2.9 chamber layer (see F1).
- **Stamp, wait or give up.** The site is stamped (3.3) and the cell becomes **Claimed** when nothing blocks it.
  - A pinned footprint or pad tile blocks it for good, since pins are never lifted: another mouth's lip or pad, or
    ground something else changed. The cell becomes **Empty**. The gate is placed where its candidate lies, so near its
    cell's edge its hole and pad can reach into the next cell, whose site would otherwise wait on them for ever.
  - Otherwise the cell is **Deferred** and retried on the next poll while a footprint tile holds an anchored entity, a
    grid other than the map lies within 4 tiles of the footprint's bounds, or a ground chunk under the footprint or a
    cavern chunk under the pad is in `LoadedChunks`.
- **Empty cells.** If none of the 8 candidates passes the pure checks, the cell is **Empty** for good too: a sea cell
  has no mouth. A cell whose site stays blocked by something players built simply stays Deferred.
- **Instant arrivals.** FTL preloads and admin teleports load chunks with no warning. They only delay a mouth; they
  never make a mouth cut into loaded terrain.
- **Gate.** At build, from the built event, cells are claimed outward from the gate cell (its own, then its edge
  neighbours, then its corners), up to 9, until one is Claimed. That site is the gate (`Kind = Gate`). A cell the
  search leaves Deferred stays Deferred, and the F2c polling claims it later as an ordinary cell.
- **Cost.** A candidate grows its shape (well under a millisecond) and evaluates 5 cavern lookups, then one tile and
  one entity lookup per footprint tile, 20 to 70 of them (2.1 item 8). The site is
  computed once and cached in the cell, so a Deferred cell's retries only re-run the cheap state checks. The claim
  step logs a warning when one cell takes more than 20 ms.

### 3.3 Stamping a site

Each map gets one `SetTiles` call per site. All of it works on unloaded chunks.

0. **Loaded chunks only** (`wfcavern open`): delete the anchored entities the biome spawned (`WfIsBiomeSpawned`) on
   the footprint and the pad. A claim never stamps into a loaded chunk, so this does nothing there.
1. **Ground.**
   - Set the ring tiles that are empty to their natural tile; a loaded ring keeps what is on it. Empty the hole.
   - Pin ring and hole. The hole stays empty, so the cavern below it stays unroofed: a light shaft.
2. **Cavern.**
   - Set `landingTile` under every hole tile and the natural tile over the rest of the pad (every tile within
     `padRadius` of a hole tile in either axis).
   - Pin every pad tile. Pinned tiles skip entity generation, so no rock ever spawns on the pad.
3. **Entities.**
   - One `shade` spawns on each hole tile that has none (F2c's `EnsureHole` reuses this). It stores the cavern, the
     level's air (`WFCavernAirClassifier.Classify`) and the landing tile's `fallDamageMultiplier`.
   - `landingEntity`, when set, is anchored on every pad tile that is the landing tile, hole or not, and on every
     other pad tile where the cavern would grow it, unless one is already there: pinned tiles grow no biome entities,
     so this is how Asclepiu's landing pool gets its water and a pool the pad crosses stays wet.
   - `rim` decor goes on the shape's rim spots (3.1), cycling through the `rim` list.
   - The `climbPoint` is anchored on the pad under the climb tile (3.1), with `Delay = climbSeconds × clamp(gravity, 1,
     2.5)`.

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
| Asclepiu | `WFCavernFloorPoolbed` under `MonoFloorWaterEntity` | ×0 | 0 |
| Fervidus | `WFCavernFloorAsh` | ×0.75 | 10 |
| Merak | `WFCavernFloorSandDrift` | ×0.5 | 6 |
| Aerumna | `WFCavernFloorChromiteScree` | ×1.5 | 20 |
| Thrascias | `WFCavernFloorSnowdrift` | ×0.25 | 3 |
| Carcinoma | `WFCavernFloorGut` | ×0.4 | 5 |

F2a measured these exactly in `CavernFallTest.MobFallsAndIsHurtALittle`: 0, 10, 6, 20, 3 and 5 Blunt.

**Climb down.** Use the *Climb down* verb on a shade, standing on the ground itself (not on a hull deck). It is a 3 s
DoAfter that breaks on move or damage (3.6 says which damage). The server then does the following in one tick, and the
climber arrives standing, unhurt, on the pad at the climb point:
1. picks the landing (`TryFindLanding`): the cavern tile under the mouth's climb tile (for a bare hole, the hole tile)
   or, if something hard is anchored there (a wall a player built over the climb point), the nearest solid, free
   cavern tile within 2 tiles in either axis. If there is none, the climb fails with `wf-cavern-climb-down-blocked` and
   the climber stays where they were;
2. `TryMoveDown`, then sets the coordinates to the landing tile's centre;
3. `SetZPosition(0)` and `SetZVelocity(0)`.

Nothing moves until the landing is found, so a refused climb leaves the climber where they stood.

**Other ways down:**
- **Parachutes:** a deployed parachute cancels the landing damage.
- **Moths and harpies** (`CEZFlyer`) can fly down a mouth and back up it.
- **Orbital fallers** who pass through a mouth are maimed and left critical, exactly as on the ground, through the
  `IsSurfaceImpact` fix (2.2). Without it they would take an ordinary five-level fall scaled by the landing tile.

### 3.6 Coming back up

Use *Climb up* on a climb point, by verb or by activating it. It needs no equipment and no hands.
- **Duration.** The DoAfter lasts `climbSeconds × clamp(surface gravity, 1, 2.5)`, stamped on the climb point as
  `WFCavernClimbComponent.Delay` when the mouth is cut:
  - Asclepiu, Fervidus and Carcinoma: 4 s;
  - Merak: 4.6 s;
  - Thrascias: 5 s;
  - Aerumna: 10 s.

  The hands' DoAfter multiplier does not apply (2.7). Hauling multiplies it by 1.5 (F5). It breaks on move or damage.
- **Which damage breaks it.** The DoAfter keeps the default `DamageThreshold` of 1, but `SharedDoAfterSystem` only
  counts damage raised with `interruptsDoAfters`. Air, heat, cold, suffocation, pressure and poison from metabolism
  (`RespiratorSystem`, `TemperatureSystem`, `BarotraumaSystem`, `HealthChange`) all pass `false`, so a player who fell
  into hostile air unprepared can always climb out. Blows, bites, falling rock and falls do break it.
  `CavernClimbTest.UnequippedClimberEscapesHostileAir` proves it on Aerumna, Fervidus and Thrascias, whose air hurt an
  unequipped human within a minute. Carcinoma's ammonia did not hurt one within a minute at F2b, so nothing there can
  break the climb either.
- **Exit tile.** When the climb finishes, the server (`FindExit`) takes the ground tiles within 2 tiles of the climb
  point in either axis (a 5×5 square), nearest first, and picks the first that meets all of these:
  - it is non-empty and not a hole in the registry;
  - no grid other than the map covers it;
  - no hard anchored entity stands on it.

  If none qualifies, the climb fails with `wf-cavern-climb-blocked-hull` when a hull covered any solid tile that is
  not a hole, and with `wf-cavern-climb-blocked` otherwise. The climber stays below.
- **Move.** In one tick: `TryMoveUp`, set the coordinates to the exit tile's centre on the ground, `SetZPosition(0)`,
  `SetZVelocity(0)`. The exit is solid and the move recaches the ground height under the new tile, so the climber
  stands and does not fall back or bounce.
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
- **Debris and pods.** Holes reach 30 tiles on Asclepiu, about 6×6, so a pod or a piece of debris up to about 4×4 can
  sit wholly over one. With no ground under it, it churns the same way and never enters the cavern
  (`DebrisInsideMouthNeverEntersCavern`, over the largest square of hole in the Asclepiu gate). That is existing
  behaviour over unloaded terrain, and it is accepted.
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
| `CavernClimbTest.ClimbUpLandsOnSolidExitAndStays`, `UnequippedClimberEscapesHostileAir` | anything calls the caverns accessible without equipment |
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
  world's `_WF/Caverns/Mouths/<world>_pit.rsi`, draw depth `LowFloors`, drawn lit so the ground's light falls on the
  lip, and have `Clickable` (bounds over the whole tile) and `WFCavernShaft`. A hole's tiles are empty, so the cavern
  pass (2.7) shows the cavern through them; a shade only frames that view, and paints only its hole's own tiles, since
  the lip tiles are solid ground the cavern never shows through. The client's `WFCavernShadeVisualsSystem` draws a
  hole on the dual grid: one 32-pixel piece per tile corner, centred on it and drawn by the lowest hole tile around it
  as a layer offset from that tile's shade; a corner with hole all round draws nothing. A piece's state is
  `v<mask>_<labels>_<variant>`: which of its four tiles are hole (1 to 14), whether the lip overhangs deep or shallow
  where the rim crosses each piece border (a tile edge's label is a stable hash of the edge, so both pieces on it
  agree), and a drawing picked by a hash of the corner (two, one for a diagonal pair). `Tools/_WF/Caverns/gen_pits.py`
  draws them:
  - the lip overhangs the hole by 3–10 pixels in the ground's own texture, so it continues the lip tile: Asclepiu
    grass, Fervidus basalt, Merak asteroid sand, Aerumna chromite, Thrascias snow, Carcinoma flesh. It wanders in and
    out between borders; a hole corner is rounded to a 10-pixel radius and a ground corner poking in gets a 9-pixel
    bulge of lip round it, so no square corner shows;
  - the lip ends in a dark broken edge with a lit rim behind it (strongest on Aerumna, whose dark chromite needs it);
  - inside the opening: a suggestion of shaft wall under a north rim (strata, opaque at the top of the cut and faded
    out 8 pixels down), a sliver of side wall inside east and west rims and a soft shadow along every rim, all gone
    16 pixels from the lip, so the middle of a hole tile stays clear;
  - each world's touch hangs off the lip in about one piece in three: roots (Asclepiu), glowing cracks and embers
    (Fervidus), sand pouring in (Merak), crystal glints (Aerumna), icicles and frost (Thrascias), sinew (Carcinoma);
    elsewhere pebbles on the lip and clods at its edge;
  - the depth comes from the renderer (shrink, offset, blur) and the cavern's own dark, so nothing shades a big hole's
    middle.

  Near a piece's borders nothing depends on the tiles beyond its own four: across every border two pieces can share,
  the rim sits on the same label and every shadow has faded out, so pieces join seamlessly. The piece below a rim
  can't see it, so over the 4 pixels above a piece's bottom border the shaft wall fades out and the side ledge the
  piece below draws takes over (`SEAM`); without that, narrow arms showed a notch at the middle of each hole tile,
  alpha jumping by up to 159 across the border. `PitStatesExist` ties the
  client's state names to the RSIs. Ground tiles with edge sprites (grass, snow, sand) also draw their fringe onto a
  hole's empty tiles, since RT draws a neighbour's edge onto an empty tile; the lip covers its first few pixels, and
  grass blades reach further in.
- **Climb points** (`WFCavernClimbBase`): anchored, no fixtures, and `Clickable`/`InteractionOutline`. The sprite is a
  CE ladder RSI (`_CE/Structures/Architecture/Ladders/<rsi>`, state `straight`) with a tint, carrying
  `WFCavernClimb`.
- **Fauna** comes from `WFPlanetFaunaSpawner` markers on the chamber tile C (OpenSimplex2, frequency 3, threshold
  0.95), so the existing caps apply: 32 per map, 128 in total. Cavern fauna retires once no cavern player is near,
  because the eye cap means surface viewers observe it only near a mouth.
- **Why go down.** Surface ore is sparse boulders (`MonoPlanetmapOre*`). Every cavern rock tile is a wall, and veins
  fill about 15% of the rock. The ores the surface lacks are listed per world.
- **Mouth ids.** Every tile and entity a mouth row names exists on this branch (checked in F2a), so none was
  substituted. Besides liquids, each world's `avoid` lists its surface rock-outcrop spawner (`MonoPlanetmapOre*`, and
  `WallMeat` on Carcinoma): a footprint is pinned before its chunk loads, so without it a mouth could sit walled in by
  an outcrop, reachable only by mining.
- **Open band** is the fraction of open tiles `CavernBiomeTest.OpenFractionInBand` expects over a 192² sample around the
  gate candidate. A tile is open when it has a floor and no airtight entity, so liquids, decor and crystals count as
  open and walls, pillars and ice columns don't (`WFCavernSampler`). The noise numbers below started as the design's
  values; F3 tuned the rows that say "from" and left the rest.
- **Walkable** is what `TunnelsConnect` counts: open, and not a tile that hurts whoever stands in it
  (`WFCavernSampler.IsHazard`: lava and liquid plasma, which set you alight, and the Gut's digestive acid
  (`WFDigestiveAcid`), which digests you). A lava or plasma line is a wall to someone on foot, so a liquid core that runs unbroken
  along a tube or gallery would cut the cavern into cells; both cores therefore run in stretches (4.3, 4.6).
- **Glow.** Each world's glowing plant is its main light below ground (`Entities/flora.yml`, `WFCavernGlowFloraBase`):
  anchored, no fixtures, cut down with 15 damage, drawn shaded with an unshaded glow layer on top so it reads before
  its light reaches you, and a `PointLight` of radius 5.5 and energy 1.2 unless its row says otherwise. The sprites are
  existing plants recoloured onto the world's palette by `Tools/_WF/Caverns/gen_flavour.py` (`_WF/Caverns/glow_flora.rsi`).
  The plant stands in the tunnels (an entity layer on T just above the litter, OpenSimplex2 frequency 1, seed 32) and in
  the chambers (on C, seed 317); Fervidus also lines its lava-tube banks and Aerumna gathers them under its shadow
  trees. Fewer, stronger lights rather than many weak ones: a viewer loads 24–53 lights (existing crystals and glow-worms
  included). `GlowReachesTunnels` walks from each tile of the tunnel floor (the biome's first tile) in the connected web
  to the nearest light, around rock and hazards: in 128² around the gate 94–100% of it lies within a 35-tile walk of
  one, half within 8–12 tiles and 90% within 19–30. Aerumna is the far end of each range: its blooms are the dimmest
  plant (radius 4, energy 0.9) under the darkest roof, so it stays the darkest cavern, but its crawls hold enough of
  them to find the way from one to the next (4.5). Thrascias, whose galleries were already full of crystals, is the
  brightest.
- **Measured (F3).** Around each gate: open 42% Asclepiu, 39% Fervidus, 50% Merak, 38% Aerumna, 51% Thrascias, 51%
  Carcinoma. The shaped mouths moved some gates, so the sample now centres on the gate candidate (centre plus
  `gateOffset`), which no seed moves: 42.8%, 37.5%, 50.0%, 40.3%, 48.9% and 50.7%, and Aerumna's band is 0.25–0.42 to
  hold its 40.3%. The largest walkable region holds 83–99% of the walkable tiles in 128², and 96–100% of the walkable
  tiles in 384² are reachable from the gate. Veins (walls with a set ore) are 21–26% of the rock rather than 15%, and
  1.5% on Carcinoma, where only the calcified nodes carry ore; the thresholds are this section's, so the richer rock is
  a balance call left open.
  After the glow and Gut pass a viewer on the gate pad loads about 2,870 (Merak) to 3,490 (Aerumna and Fervidus) entities, varying a little from run to run with the wildlife, and 24 (Carcinoma) to 53 (Thrascias) lights.

### 4.2 Asclepiu: the Underkarst

The beginner cavern: wet limestone, breathable air and a water landing.

| | |
|---|---|
| Level | `WFCavernAsclepiuLevel`: `mapName: wf-cavern-asclepiu-map`, `mapLight: "#6f8f86"`, atmosphere `[21.824879, 82.10312]` at 285.15 K (Breathable) |
| Tiles | T `WFCavernFloorLimestone`; C `FloorPlanetDirt`; pools `MonoFloorWaterEntity` on C, the surface's water |
| Skeleton | Tunnels: ridged frequency 0.035, rock where ridged ≤ 0.50. Chambers: FBm frequency 0.025, threshold ≥ 0.35. Both use their own seeds (17, 117), since the placeholder had these numbers at seeds 7 and 101 and the Underkarst would otherwise keep its old layout. Signature (in the chamber template): karst pools, `MonoFloorWaterEntity` on `FloorPlanetDirt` at OpenSimplex2 frequency 0.06, threshold ≥ 0.45 (a `FloorWater` tile layer until its foam edges showed as white borders). Open band 0.35–0.55 |
| Rock and ore | `WallRockAndesite`. Common: `…Coal`, `…Tin`. Uncommon: `…Quartz`, `…Salt`, `…Copper`. Rare: `…Silver`, `…Gold`. Very rare: `…ArtifactFragment`. Hazard: `WallRockAndesiteQuartzGolem` (0.97) |
| Light | Roof `#070a08`, `shaftLight` 0.5. `WFCavernGlowcaps` (chanterelles recoloured cyan-green, `#6dffc8`) in tunnels (≥ 0.925) and chambers (≥ 0.95). `WFCavernGlowworms` in chambers (≥ 0.97): no sprite, `PointLight` `#7dffb4`, radius 4, energy 0.7. `CrystalGreen`/`CrystalCyan` (≥ 0.99) |
| Hazards | Pools slow you. `SpiderWeb` choke points in chambers (≥ 0.985). Golems. Cave-ins 0.15 (F5). No vents |
| Fauna | `MobBat` 5, `MobFrog` 3, `MobMouse` 2, `MobSnake` 2, `MobGiantSpider` 1 |
| Decor | Litter `FloraStalagmite`, `FloraGreyStalagmite`. Chambers `Cobweb1`, `Cobweb2` |
| Deep (F5) | `MobRatKing` + 3 `MobRatServant` |
| Ambience | The surface's day and night soundscape, muffled (no `ambience`). A candidate own soundscape: Loop `/Audio/Ambience/ambicave.ogg`. One-shots `/Audio/Effects/waterswirl.ogg`, `/Audio/Effects/drop.ogg` |
| Mouth | Sinkhole: a blob of 14–30 tiles, elongation 1.6, roughness 0.35; cell 96. `groundTiles: [FloorPlanetGrass, FloorPlanetDirt, FloorSnow]`, `avoid: [MonoFloorWaterEntity, MonoPlanetmapOreBase, MonoPlanetmapOreSnow]`. Shade `asclepiu_pit`. Climb point `dirt_cliff.rsi` (roots). Rim `FloraRockSolid`, 1–2 by size. Lands in a plunge pool for 0 Blunt: `WFCavernFloorPoolbed` with `landingEntity: MonoFloorWaterEntity` |
| Only below | Continuous salt and silver veins, artifact fragments |

### 4.3 Fervidus: the Cinder Vaults

Basalt cut by lava tubes, with magma chambers and diamonds.

| | |
|---|---|
| Level | `WFCavernFervidusLevel`: `mapLight: "#c48b71"`, atmosphere `[0, 60, 40]` at 413.15 K (Scalding; the surface is 373 K) |
| Tiles | T `FloorBasalt`; C `WFCavernFloorAsh`; landing `WFCavernFloorAsh` |
| Skeleton | Tunnels: frequency 0.04, ≤ 0.64 (tuned in F3 from 0.60). Chambers: frequency 0.028, ≥ 0.42 (from 0.38), with magma lakes (`FloorLavaEntity` on C, FBm 2 octaves, frequency 0.05, ≥ 0.55). Signature `WFCavernSignatureFervidus`: lava tubes, ridged frequency 0.009, ≥ 0.86. It has an ash bank (tile C) and a nested core template (`WFCavernTubeCoreFervidus`) on the same noise at ≥ 0.95 placing `FloorLavaEntity` where OpenSimplex2 frequency 0.04 is ≥ 0, so the lava runs in stretches (F3: an unbroken core walled the cavern into cells, and only 6% of it was reachable from the gate without crossing lava). Open band 0.30–0.45 |
| Rock and ore | `WallRockBasalt`. Common: `…Coal`, `…Plasma`. Uncommon: `…Tin`, `…Uranium`. Rare: `…Gold`, `…Silver`, `…Diamond` (0.90). Very rare: `WallRockBasaltBluespace`. Hazard: `WallRockBasaltPlasmaGolem` (0.97) |
| Light | Roof `#1c0803`, `shaftLight` 0.45. Lava tile emission. `WFCavernEmberLichen` (lingzhi with an ember glow layer, `#ff8a3a`, energy 1.4) in tunnels (≥ 0.92), chambers (≥ 0.94) and on the lava-tube banks (seed 320, ≥ 0.93, under the core). `CrystalOrange` (≥ 0.99) |
| Hazards | Lava. Heat. Sulphur vents `WFCavernVentFervidus` (`SulfuricAcid` smoke, F5). Cave-ins 0.2 |
| Fauna | `WFMobArgocyteSlurvaBasalt` 4, `WFMobArgocyteCrawlerBasalt` 4, `WFMobArgocyteSwiperBasalt` 2, `MobWatcherMagmawing` 1 |
| Decor | Litter `BasaltOne`–`BasaltFive`. Chambers `FloraGreyStalagmite` |
| Deep (F5) | `MobArgocyteLeviathing` |
| Ambience | The surface's day and night soundscape, muffled (no `ambience`). A candidate own soundscape: Loops `/Audio/Ambience/ambilava1.ogg`, `…ambilava2.ogg`, `…ambilava3.ogg`. One-shots `/Audio/Effects/sizzle.ogg`, `/Audio/Magic/rumble.ogg` |
| Mouth | Skylight: a blob of 8–18 tiles, elongation 1.8, roughness 0.3; cell 96. `groundTiles: [FloorBasalt]`, `avoid: [FloorLavaEntity, MonoPlanetmapOreBasalt]`. Shade `fervidus_pit`. Climb point `stone.rsi` tinted `#5a4a44`. Rim `BasaltOne`, `BasaltThree`. Lands for 10 Blunt |
| Only below | Diamonds and bluespace in basalt |

### 4.4 Merak: the Sandstone Galleries

Cool pillared halls under a 45 °C desert, with the richest gold and the most collapses.

| | |
|---|---|
| Level | `WFCavernMerakLevel`: `mapLight: "#dfc396"`, atmosphere `[21.824879, 82.10312]` at 294.15 K (Breathable; a refuge from the surface's 318 K) |
| Tiles | T `WFCavernFloorSand`; C `WFCavernFloorSandstone`; landing `WFCavernFloorSandDrift` |
| Skeleton | Tunnels: frequency 0.03, ≤ 0.50. Chambers are pillared halls: FBm frequency 0.02, ≥ 0.20, with single-tile `WallRockSand` pillars on C (OpenSimplex2 frequency 0.35, ≥ 0.84). Signature: buried camps in halls, a camp template (`WFCavernSignatureMerak`, OpenSimplex2 frequency 0.05, ≥ 0.9, a few tiles across) holding `SalvageSpawnerScrapValuable` (≥ 0.6) and `SalvageHumanCorpseSpawner` (≥ 0.75) at frequency 1 (tuned in F3 from single spawners at ≥ 0.995 and ≥ 0.997, which scattered them one by one), plus fossil beds in rock (`WallRockSandArtifactFragment` at frequency 0.05, ≥ 0.88). Open band 0.40–0.60 |
| Rock and ore | `WallRockSand`. Common: `…Tin`, `…Quartz`. Uncommon: `…Copper`, `…Salt`. Rare: `…Gold` (0.86), `…Silver`. Very rare: `…Diamond`. Hazard: `WallRockSandGoldCrabNF` (0.96) |
| Light | Roof `#120d07`, `shaftLight` 0.55. `WFCavernLampAgave` (aloe recoloured amber, `#ffbe55`) in tunnels (≥ 0.94) and halls (≥ 0.955). `CrystalOrange`/`CrystalPink` (≥ 0.992) |
| Hazards | Cave-ins 0.35 (sandstone). Dust pockets `WFCavernVentMerak` (`TearGas` smoke, F5). Ore crabs |
| Fauna | `MobLizard` 3, `MobSnake` 3, `MobPurpleSnake` 1, `MobGiantSpider` 1 |
| Decor | Litter `FloraRockSolid` |
| Deep (F5) | 2 `MobGiantSpiderAngry` |
| Ambience | The surface's day and night soundscape, muffled (no `ambience`). A candidate own soundscape: Loop `/Audio/Ambience/ambimine.ogg`. One-shots `/Audio/Effects/break_stone.ogg`, `/Audio/Effects/rustle4.ogg` |
| Mouth | Sand funnel: round, 12–28 tiles, elongation 1.25, roughness 0.1; cell 96. `groundTiles: [FloorAsteroidSandPlanet, FloorDesertPlanet, FloorAsteroidSandUnvariantizedPlanet]`, `avoid: [MonoFloorWaterEntity, MonoPlanetmapOreSandRich]`. Shade `merak_pit`. Climb point `wooden.rsi` (rope ladder). Rim `FloraRockSolidPlanet`, 1–2 by size. Lands for 6 Blunt. A shovel opens a way down anywhere (3.4) |
| Only below | Gold-rich veins, fossils, lost prospectors' gear |

### 4.5 Aerumna: the Umbral Deeps

The darkest cavern: chromite, 3 g, toxic air, xenos, and the only anomaly rock.

| | |
|---|---|
| Level | `WFCavernAerumnaLevel`: `mapLight: "#2a1f3a"`, atmosphere `[4, 72, 24]` at 277.15 K (Toxic from CO₂) |
| Tiles | T `WFCavernFloorChromite`; C `WFCavernFloorBedrock`; landing `WFCavernFloorChromiteScree` |
| Skeleton | Tight crawls: ridged frequency 0.05, ≤ 0.58 (tuned in F3 from 0.62, which left the crawls in pieces: 43% of the open tiles connected). Chambers are cathedral galleries: FBm frequency 0.012, ≥ 0.34 (from 0.30, to stay in the band). Signatures in galleries: shadow groves (meta FBm frequency 0.02, ≥ 0.3, placing `ShadowTree`, `ShadowBasaltOne`, `ShadowBasaltTwo` on C at ≥ 0.5, frequency 2; tuned in F3 from 0.5 and 0.65, which grew about one tree in 2,000 tiles) and pink geodes (meta OpenSimplex2 frequency 0.08, seed 56, ≥ 0.75, placing `CrystalPink` on C at ≥ 0.5, frequency 1). Open band 0.25–0.42, sampled at the gate candidate (4.1) |
| Rock and ore | `WallRockChromite`. Common: `…Tin`, `…Plasma`. Uncommon: `…Quartz`, `…Uranium`. Rare: `…Silver`, `…Gold`. Very rare: `…Diamond`, `WallRockChromiteBluespace`, `WallRockChromiteArtifactAnomaly` |
| Light | Roof `#050408`, `shaftLight` 0.3. The geodes, and `WFCavernShadowBloom` (spaceman's trumpet recoloured violet, `#b65cff`, radius 4, energy 0.9): along the crawls (≥ 0.92, spaced so you can walk from one to the next; at 0.97 only 0.4% of the crawl floor had one and 90% of it lay within a 56-tile walk of a light), a few in the galleries (≥ 0.97) and more under the shadow trees (in the grove template, seed 321, ≥ 0.93). The dimmest glow under the darkest roof, so still the darkest cavern |
| Hazards | Darkness. CO₂. Xenos. A 10 s climb out at 3 g (clear the pad first). Spore pockets `WFCavernVentAerumna` (`Nocturine` smoke, F5). Cave-ins 0.25 |
| Fauna | `MobXenoRunner` 3, `MobXenoDrone` 2, `MobArgocyteSlurva` 3, `MobXenoSpitter` 1, `MobXenoPraetorian` 0.5 |
| Deep (F5) | `MobXenoPraetorian` + 2 `MobXenoRunner` |
| Ambience | The surface's day and night soundscape, muffled (no `ambience`). A candidate own soundscape: Loop `/Audio/Ambience/ambimystery.ogg`. One-shots `/Audio/Effects/glass_crack1.ogg`, `/Audio/Magic/rumble.ogg` |
| Mouth | Rift: a crack of 9–22 tiles, 1–2 wide; cell 128. `groundTiles: [FloorChromite]`, `avoid: [MonoFloorWaterEntity, MonoPlanetmapOreChromite]`. Shade `aerumna_pit`. Climb point `stone.rsi` tinted `#3a3342`. Rim `ShadowBasaltOne`, `ShadowBasaltThree`, `CrystalPink`. Lands for 20 Blunt, the hard landing of a 3 g world. Explosions open ways down (3.4) |
| Only below | Artifact anomalies, bluespace, diamonds |

### 4.6 Thrascias: the Rime Galleries

Ice halls with plasma lakes, milder than the 180 K surface but still lethal.

| | |
|---|---|
| Level | `WFCavernThrasciasLevel`: `mapLight: "#9fc3dc"`, atmosphere `[0, 100]` at 235.15 K (Freezing) |
| Tiles | T `FloorSnowDug`; C `WFCavernFloorSnowdrift`; galleries `FloorIce`; landing `WFCavernFloorSnowdrift` |
| Skeleton | Tunnels: frequency 0.045, ≤ 0.60. Chambers: frequency 0.025, ≥ 0.35. Signature `WFCavernSignatureThrascias`: ice galleries, ridged frequency 0.018, ≥ 0.65 (tuned in F3 from 0.75, which made 3–5 tile channels rather than halls). They have a `FloorIce` tile, `WallIce` columns on ice (OpenSimplex2 frequency 0.4, ≥ 0.9), `CrystalBlue`/`CrystalCyan` (frequency 1, ≥ 0.88, from 0.97, so that they are common), and a nested core (`WFCavernLakeThrascias`) on the same noise at ≥ 0.80 (from 0.93) with `FloorLiquidPlasmaEntity` where OpenSimplex2 frequency 0.05 is ≥ 0.25: long lakes down the middle of a hall with ice to walk on either side, rather than an unbroken plasma thread. Open band 0.35–0.55 |
| Rock and ore | `WallRockSnow`. Common: `…Coal`, `…Tin`. Uncommon: `…Quartz`, `…Plasma`. Rare: `…Silver`, `…Uranium`. Very rare: `…Diamond`, `WallRockSnowBluespace` |
| Light | Roof `#060c12`, `shaftLight` 0.5. Blue crystals are common in the galleries. `WFCavernRimeThistle` (glasstle recoloured pale blue, `#9fdcff`, energy 1) in the tunnels (≥ 0.945) and snowdrift chambers (≥ 0.96), where the crystals don't reach |
| Hazards | Cold. Momentum on ice next to plasma lakes. Frost pockets `WFCavernVentThrascias` (`FrostOil` smoke, F5). Cave-ins 0.2 |
| Fauna | `MobArgocyteSlurva` 4, `MobArgocyteBarrier` 2, `MobPenguin` 2, `MobWatcherIcewing` 1, `MobBearSpace` 1 |
| Loot | `SalvageSpawnerTreasureValuable` in chambers (frequency 1, ≥ 0.997): frozen caches |
| Deep (F5) | 2 `MobBearSpace` |
| Ambience | The surface's day and night soundscape, muffled (no `ambience`). A candidate own soundscape: Loop `/Audio/Ambience/ambiatmos2.ogg`. One-shots `/Audio/Effects/glass_crack2.ogg`, `/Audio/Effects/glass_crack1.ogg` |
| Mouth | Moulin: round, 7–14 tiles, elongation 1.5, roughness 0.3; cell 96 (a round hole needs 7 tiles to be anything but a rectangle, and the longer, rougher edge gives about 100 different moulins in 400 seeds rather than 36). `groundTiles: [FloorSnow, FloorIce]`, `avoid: [FloorLiquidPlasmaEntity, MonoPlanetmapOreSnow]`. Shade `thrascias_pit`. Climb point `stone.rsi` tinted `#bfe6ff`. Rim `CrystalCyan`, 1–3 by size, so the glow marks the mouth at night. Lands for 3 Blunt |
| Only below | Diamonds, bluespace, preserved caches |

### 4.7 Carcinoma: the Gut

Flesh throats and stomachs grown over a mineral world, where the infestation began. The stomachs pool digestive acid
and some throats are choked with tendons.

| | |
|---|---|
| Level | `WFCavernCarcinomaLevel`: `mapLight: "#5a1a22"`, atmosphere `[21.824879, 76, 0, 0, 0, 0, 1]` at 310.15 K (Foul: breathable, with ammonia) |
| Tiles | T `WFCavernFloorFlesh`; C `WFCavernFloorGut`; landing `WFCavernFloorGut`; acid pools `WFCavernDigestiveAcid` on C |
| Skeleton | Throats: ridged, 2 octaves with gain 0.3 for wiggle, frequency 0.06, ≤ 0.40 (tuned in F3 from 0.62: two octaves at the default gain 0.5 compress the ridged range, so 0.62 left 32% open and 7% of it connected, and 0.30 kept them connected only by widening them into broad ground; the lower gain keeps them narrow and joined). Stomachs: FBm frequency 0.03, ≥ 0.25 (from 0.35, making up the open share the narrower throats give up). Signature: digestive channels, `WFBloodRiver` placed straight on T (ridged frequency 0.012, ≥ 0.94, highest priority). Stomachs hold `WFCarcinomaAssimilationSack` (≥ 0.985), `WFFleshPustule` (≥ 0.985), `WFFleshPolyp` (≥ 0.98) and `WFCavernGutGlow` (≥ 0.975). Acid pools: a meta layer in the stomach template, highest there (`WFCavernAcidCarcinoma`, FBm 2 octaves, frequency 0.06, seed 60, ≥ 0.48), placing `WFCavernDigestiveAcid` on C, ringed on the same noise at ≥ 0.3 by `WFCavernPoolEdgeCarcinoma`: bile (`WFCavernBile1`–`4`, decals of `Fluids/vomit_toxin.rsi`, ≥ 0.2) and bones (the `Remains` decal, ≥ 0.8). Choked throats: `WFCavernOvergrowthCarcinoma` (OpenSimplex2 frequency 0.03, seed 61, ≥ 0.3), the lowest-priority layer on T, fills its stretches with `WFCavernTendons` (frequency 0.6, ≥ −0.45, about three tiles in four). In 192² around the gate the pools cover 4.4% of all tiles, 8.6% of the open floor and 14% of the stomach floor, the tendons 18% of the open throat floor, and 96% of the walkable tiles in 128² stay connected. Open band 0.35–0.55 |
| Rock and ore | `WallMeat`, as on the surface. It can't be mined: cut it down like any wall. The rock template has a calcified-node meta layer (FBm frequency 0.05, ≥ 0.6) of `WallRockAndesite`, with `…Salt`, `…Silver`, `…Gold`, `…Plasma` and `…Uranium` veins (F3 tiers: common salt and plasma, uncommon silver and uranium, rare gold). The flesh grew over a mineral world |
| Light | Roof `#140306`, `shaftLight` 0.4. `WFCavernNerveCluster` (the flora anomaly's bulb recoloured red, `#ff4d72`) along the throats (≥ 0.93) and in the stomachs (≥ 0.95). `WFCavernGutGlow`: no sprite, `PointLight` `#ff4a5a`, radius 3 |
| Hazards | Digestive acid (`WFDigestiveAcid`, `Content.Shared/_WF/Caverns`): 5 Caustic a second to any mob wading in, with no fire or air needed, through the contact damage `DamageContactsSystem` ticks, and wading at 0.6 speed. It spares the Gut's own creatures by faction, since they share no tag: `AberrantFleshExpeditionNF` (the aberrant flesh, their newborns and the assimilated miners) and `Chimera` (the ticks and the Letoferol). Like lava it spares anyone on a catwalk over it. It digests the dead too, so a body left in it is soon past saving, and leaves items alone. Whoever it burns hisses: `WFDigestiveAcidHissSystem` loops the pool's `burnSound` (the deep fryer sizzle, `/Audio/Nyanotrasen/Ambience/Objects/deepfryer_sizzling.ogg`, −4 dB, 8 tiles) on them, positional, while they take its damage and are alive; leaving, dying or a catwalk stops it, and spared natives never start one. One stream per burning mob keeps the client's audio budget (`WFAudioBudgetSystem`, 96 network streams) safe, and a body in the acid falls silent. It holds no reagent, so it spills nothing and leaves the floor whole. Choked throats slow you to 0.65 (`WFCavernTendons`: `WFCarcinomaTendons`, the surface forest's non-hard tendons and the art of the climb point, drawn at `FloorObjects` under the mobs so a click on someone standing in them reaches them, with `SpeedModifierContacts`; cut them down at 40 damage). Ammonia (masks). Ticks. Pustules. Bile pockets `WFCavernVentCarcinoma` (`Ammonia` smoke, F5). No cave-ins: flesh doesn't collapse |
| Decor | Bile and bones around the acid pools, as decals |
| Fauna | `WFCavernFaunaCarcinoma`: nested `WFFaunaCarcinoma` 3, `WFMobFleshTick` 4, `MobFleshAssimilatedMiner` 1 |
| Deep (F5) | `MobLetoferolHorror` |
| Ambience | The surface's day and night soundscape, muffled (no `ambience`). A candidate own soundscape: Loop `/Audio/Ambience/anomaly_scary.ogg`. One-shots `/Audio/Effects/gib1.ogg`, `/Audio/Effects/Fluids/blood1.ogg`, `/Audio/Ambience/Objects/drain.ogg` |
| Mouth | Throat: a blob of 8–20 tiles, elongation 1.5, roughness 0.45; cell 80. `groundTiles: [WFFloorFlesh]`, `avoid: [WFBloodRiver, WallMeat]`. Shade `carcinoma_pit`. Climb point the tendons (`fleshkudzu.rsi`, `kudzu_11`). Rim `WFFleshPolyp`. Lands for 5 Blunt. Prying and cutting the flesh opens ways down (3.4) |
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
| `wf-cavern-climb-down-blocked` | Something blocks the way down. | F2 |
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

Watches work underground because of the environment mirror (F4). The on-screen watch reads the cavern's environment;
using or examining one goes through `WFPlanetWeatherSystem.TryGetReport`, which for a map in the network's
`LowerLayers` takes the weather from that map's environment instead of the ground's sky.

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
| `tp <planet> [pad\|mouth]` | Moves the caller to the gate's climb tile: on the cavern pad beside the climb point (default) or on the lip on the ground | F2 |
| `mouths <planet>` | Lists claimed mouths: kind, anchor, hole size in tiles, climb tile | F2 |
| `open` | Carves a mouth (`Kind = Admin`) anchored at the caller's ground tile, its shape seeded by that tile. On a loaded chunk it deletes only biome-spawned entities in the footprint and pad, and refuses if a grid, a player-built anchored entity or a mob other than the caller is in the hole, or (`mouth`) the footprint overlaps another mouth's hole or climb point, or its pad a pinned pad tile, which the stamp would overwrite with natural floor; it also refuses (`cavern`) when either map is gone. Walls from self-deleting outcrop spawners are no longer tracked by the biome, so they count as built | F2 |
| `stats <planet>` | Open fraction, largest walkable region share and ore share of a 192² pure-noise sample (the same sampler as the tests): around the caller when they stand on that planet's ground or cavern, otherwise around the gate's centre tile. It says it has started, reads a few rows a tick as a job (2 ms a tick) and prints when done; while the game is paused, as an empty server is, it reads the square at once | F3 |
| `awaken <planet>` | Forces the deep-table spawn near the caller | F5 |

Keys: `cmd-wfcavern-desc`, `-help`, `-disabled`, `-invalid-args`, `-unknown-planet`, `-no-cavern`, `-empty`, `-row`,
`-row-none`, `-mouth-row`, `-mouth-kind` (a `$kind` selector: gate, cell, hole, admin), `-tp-done`, `-no-map`,
`-not-ground`, `-open-done`, `-open-refused` (a `$reason` selector: cavern, grid, built, mob, mouth), `-stats-started`, `-stats`, `-awakened`, `-hint-sub`, `-hint-planet`, `-hint-target`. `-stats-started`,
`-stats` and `-awakened` come with their subcommands.

`<planet>` matches, ignoring case, the sector body's name, the name the network was built under (`wfplanet spawn`
uses the ground map name, such as `Asclepiu`) or the surface id with or without its `WFSurface` prefix. Everything but
`list` needs `wf.caverns` on.

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
| `CavernNetworkTest.EveryWorldGetsOneCavern` [6] | Each world built and torn down in turn on one pair. Depth −1; `TryMapDown(ground)`/`TryMapUp(cavern)` link; `Layers` unchanged. The cavern has `WFPlanetLayer` (right network and gravity), `WFCavernLayer`, the right biome and seed, and no `LightCycle`, `SunShadow`, `Parallax` or `CEZGroundLayer`. Its `MapAtmosphere` equals the level's. The ground has `WFCavernGround` | F1 |
| `CavernNetworkTest.DeleteRemovesCavernAndTransits` | A stub transit whose `LowerMap` is the cavern is deleted with the network | F1 |
| `CavernRoofTest.GroundTilesRoofCavern` | `LayTiles` on the ground roofs those cavern tiles; emptying one unroofs it | F1 |
| `CavernViewerEyeTest.GroundViewerFarFromMouthsLoadsNoCavern` | A ground viewer 160 tiles east of the gate has no eye on the cavern and isn't recorded as seeing it, the cavern's `LoadedChunks` stays empty for 60 ticks, and with PVS on no cavern entity reaches the client | F1, cavern view |
| `CavernViewerEyeTest.GroundViewerNearMouthLoadsCavern` | A ground viewer on the gate's climb tile has one eye on the cavern, the cavern chunk under the hole and one three chunks aside load, and with PVS on cavern entities reach the client; logs the cost | cavern view |
| `CavernViewerEyeTest.CavernEyeKeepsAMarginBeforeLeaving` | Moved east of the gate, measured from half of `net.pvs_range`: the eye stays between the enter and leave margins after the viewer had it, goes past the leave margin, stays gone between the margins, and comes back inside the enter margin | cavern view |
| `CavernViewerEyeTest.GhostSeesCavernOnlyIfItMayLoadTerrain` [2] | On the gate's climb tile, a `MobObserver` has no eye on the cavern and loads no cavern chunk; an `AdminObserver` has one and loads it | cavern view |
| `CavernViewerEyeTest.AirViewerOverMouthLoadsCavern` | A viewer on air layer 1 over the gate has one eye on the ground and one on the cavern; moved 160 tiles away it keeps the first and loses the second | cavern view |
| `CavernViewerEyeTest.CavernViewerLoadsGroundAbove` | A cavern viewer has an eye on the ground, and ground chunks load over it | F1 |
| `CavernViewTest.ClientSeesCavernUnderMouth` (client pair) | Beside the gate, the client finds the cavern under the ground; the cavern and the ground around the hole hide the sky, and ground 200 tiles away doesn't | cavern view |
| `CavernViewTest.ShadeClicksAcrossItsTile` (client pair) | The shade of a gate tile with hole all round, whose art is clear, takes clicks at its centre and near its corner, and not 1.6 tiles away | cavern view |
| `Content.Tests: CavernPassTest` | `CavernPassDepth` is one level under the ground at any ground depth with the cavern known and a mouth in view, and null otherwise; `LevelViewBox` is the box an RT `Eye` built as the renderer builds a pass eye shows, from a jumping observer, three levels up and with a turned eye | cavern view |
| `CavernHullTest.UnsupportedHullNeverDescends` | A `BuildHull` without lift on the ground map over chunks that were never loaded, so no tile is under it (a hull on loaded terrain can keep a tile through `WfUnloadChunk`, 2.1 item 7): sampled every tick for 10 s, the hull's map is never the cavern and no transit touches the cavern | F1 |
| `CavernHullTest.PilotCannotDescendFromGround` | A `BuildLander` hovering on its landing thrusters (lift ratio ≥ 1) over unloaded ground, with `HoldDescend`: sampled every tick for 10 s it never leaves depth ≥ 0 and stays on the ground map, and no transit gap is created at all (an unguarded descend enters one and lands again within a tick) | F1 |
| `CavernHullTest.LiftoffAndLandingUnchanged` | With caverns on, a `BuildLander` lifts to air layer 1 and lands back on the ground | F1 |
| `CavernWildlifeTest.WildlifeOverUnloadedGroundIsRemoved` | An awake wildlife mob whose ground chunk unloads falls into the cavern and is deleted, so it never lingers `Protected`; a non-wildlife mob beside it lands in the cavern and stays | F1 |
| `CavernWildlifeTest.WildlifeThroughPinnedHoleIsKept` | A wildlife mob on a hand-pinned tile survives the chunk unload; emptying the tile drops it into the cavern, where it is kept | F1 |
| `CavernWildlifeTest.WildlifeThroughLoadedHoleIsKept` | A wildlife mob over a tile emptied on a loaded chunk, unpinned (a hole is pinned only when its chunk unloads), falls into the cavern and is kept; the chunk is still loaded and the tile unpinned afterwards, so the loaded check decided | F1 |
| `CavernMouthTest.GateExists` [6] | The gate is claimed at build. Its hole tiles are pinned and empty with a shade each. Its ring is pinned and solid. The pad is pinned, with the landing tile under the hole and no rock after its chunks load; the test loads them with `WfLoadChunk`, as a cavern viewer's loader would, rather than attaching a viewer. The climb point is anchored under the climb tile, with the section 3.6 delay. The hole is inside the world's size range, the climb tile is on the lip beside the hole, each rim spot has its decor and nothing stands on the climb tile | F2 |
| `CavernMouthTest.ShapesStayInRange` | Over 400 seeds per world: the hole is inside its size range, holds the anchor, is joined edge to edge, has no tile hanging on by one edge (a rift may have its two ends), no two tiles touching only at a corner and no enclosed ground; the ring is every tile touching it; the climb tile is on the ring, beside a hole tile on `climbSide` and past the whole hole on that side; rim spots are on the ring, clear of the climb tile and no more than `rimCount` scaled by the hole's size; the same seed gives the same hole and rim. At most 5% of non-rift holes are plain rectangles and at most 5% fall back, the holes take at least half the sizes in range, and the seeds give at least 80 different holes. The specs are the worlds' prototypes, so it runs on a pair | F2 |
| `CavernMouthTest.PitStatesExist` | On the client, every world's pit RSI holds every state `WFCavernShadeVisualsSystem.AllStates` lists (`pit` and the pieces for masks 1 to 14), and no other | F2, cavern view |
| `CavernMouthTest.PinnedSiteIsEmpty` | Merak: with the cavern pinned under a whole cell, a cell that has a site claims Empty, and stays Empty | F2 |
| `CavernMouthTest.PadKeepsPools` | Asclepiu: of admin mouths cut 3 tiles from a pool's edge, one whose pad crosses the pool holds exactly one `MonoFloorWaterEntity` on every pad tile off the hole where the cavern grows one; `GateExists` checks the same on every gate | F4 |
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
| `CavernClimbTest.ClimbUpLandsOnSolidExitAndStays` | Asclepiu, by the *Climb up* verb from the pad: after the DoAfter the mob is on the ground, on the solid lip over the climb point (not a hole), at `LocalPosition < 0.1`, and stays there for 120 ticks | F2 |
| `CavernClimbTest.ClimbDownIsHarmless` | By the *Climb down* verb from the lip: 0 damage, no knockdown, on a pinned pad tile at or next to the climb point, and still there unhurt 120 ticks later | F2 |
| `CavernClimbTest.ClimbDownAvoidsBuiltPad` | With a `WallSolid` anchored on the pad over the climb point, *Climb down* lands on a pad tile beside it, unhurt, and stays there for 120 ticks | F2 |
| `CavernClimbTest.UnequippedClimberEscapesHostileAir` [3] | Aerumna, Fervidus, Thrascias: an unequipped human waits in the cavern until the air hurts it, then climbs out by verb; it keeps taking damage during the climb, the DoAfter is not cancelled, and it reaches the ground | F2 |
| `CavernClimbTest.ClimbRefusedUnderHull` | A static 5×5 `BuildDebris` over every exit tile (a dynamic one is shoved off the outcrops the viewer loads): the climb finishes, the mob stays below, and the connected client receives the hull popup | F2 |
| `CavernClimbTest.DelayScalesWithGravity` | The stamped delay is Asclepiu 4 s and Aerumna 10 s (±1 tick). On Aerumna, unequipped, the DoAfter's delay is 10 s ±1 tick, and by the game clock the climb lands on the first tick at or past it | F2 |
| `CavernHullTest.HullOverMouthStaysOnGround` | A 3×3 grid (`BuildDebris`; `BuildHull` is a fixed 15×15) centred on the hole tile over the climb tile, so over hole and lip, stays on the ground with no transit for 5 s | F2 |
| `CavernHullTest.DebrisInsideMouthNeverEntersCavern` | A debris grid as big as the largest square of hole in the Asclepiu gate (2×2 to 4×4), with no ground under it, never has the cavern as its map (it may churn) | F2 |
| `CavernOrbitalFallTest.OrbitalFallIntoMouthMaimsLikeGround` | Fervidus (a ×0.75 ash landing). Dropped from orbit over the gate, at rest on the cavern: critical, one arm and one leg severed. Without the `IsSurfaceImpact` fix it fails, because the faller lands alive and unmaimed with 58 Blunt (2.1 item 12) | F2 |
| `CavernCommandTest.ListTpMouthsOpen` | `list` prints six rows, `tp` lands on the gate pad, `stats` samples around a caller on that planet and around the gate for a caller elsewhere (F3), `open` creates a mouth, `mouths` lists the gate by the sector body's name, the build name and the surface id with and without its prefix, ignoring case, and a second `TryOpenMouth` beside the carved mouth, clear of its hole and climb point but sharing its pad, refuses with `mouth`, `TryOpenMouth` over a missing cavern refuses with `cavern`, whose Fluent variant is its own (output read from the client console: a content test can't implement `IConsoleShell`) | F2 |
| `Content.Tests: CavernAirTest.ClassifiesEachWorld` | The six level atmospheres classify as in section 4.8, and the thresholds are exact at their edges | F2 |
| `CavernPrototypeTest.OneCavernPerSurface` | One `wfCavern` per `wfPlanetSurface`, and every reference resolves (level, biome, tiles, entities, sounds) | F3 |
| `CavernPrototypeTest.FloorsIndestructibleAndUndiggable` | Every tile a cavern template or mouth can place is `Indestructible`, and neither `CanShovel` nor `CanCrowbar`. This also proves tile `parent` inheritance | F3 |
| `CavernPrototypeTest.NoLeakingWallSpawners` | No cavern template entity layer names a self-deleting spawner other than the allow-listed fauna and loot markers (no `MonoPlanetmap*`) | F3 |
| `CavernBiomeTest.OpenFractionInBand` [6] | 192² pure sample around the gate candidate (centre plus `gateOffset`) falls inside the section 4 band | F3 |
| `CavernBiomeTest.TunnelsConnect` [6] | In 128², the largest 4-connected walkable component holds at least 60% of the walkable tiles; lava, liquid plasma and digestive acid block (4.1) | F3 |
| `CavernBiomeTest.OreOnlyInRock` [6] | Every sampled vein entity stands on T | F3 |
| `CavernBiomeTest.SignaturePresent` [6] | Over 512², sampled every 2nd tile: `MonoFloorWaterEntity` (the plunge pools), pillars, fossils and camp corpses, pink geodes and shadow trees, `FloorIce` and `WallIce`, `WFBloodRiver`, each world's glow plant, and Carcinoma's acid pools on C and nerve clusters. Lava tubes and plasma lakes are checked through their templates: sampled alone, `WFCavernSignatureFervidus` and `WFCavernSignatureThrascias` must place lava or plasma, and every tile where they do must carry it in the cavern, so magma lakes can't stand in for the tubes. Carcinoma must have a choked throat: some 5×5-sample window (10 tiles) where `WFCavernTendons` fill at least half of the open ground | F3 |
| `CavernBiomeTest.GlowReachesTunnels` [6] | Sampled over 192² around the gate: of the tunnel floor (the biome's first tile) in the largest walkable region within the middle 128², at least 90% lies within a 35-tile walk of a light, stepping around rock and hazards (`WFCavernSample.LightWalks`); the central 72² a viewer loads holds 1–100 lights, the bound `CavernGenerationTest` puts on a live viewer | F3 |
| `CavernGutTest.AcidDigestsIntrudersAndSparesNatives` | On a bare airless grid of `WFCavernFloorGut`, each mob in its own pool of `WFCavernDigestiveAcid` for 3 s: a `MobHuman` takes at least 10 Caustic, while a `MobHuman` on a `Catwalk` over its pool and every creature the `WFCavernFaunaCarcinoma` and `WFCarcinomaAssimilationSack` tables can spawn (20, put to sleep so they stay in the acid) touch the acid but take none; the floors under the pools are unchanged and no puddle appears | F3 |
| `CavernGutTest.HissStopsOutOfTheAcid` | A `MobHuman` burning in acid carries a looping `burnSound` stream attached to it (also checked in `AcidDigestsIntrudersAndSparesNatives`, where the catwalk walker and the natives carry none); walked onto bare floor, it stops burning and the stream is deleted | F4 |
| `CavernBiomeTest.SamplerCountsWaterAsOpen` | The sampler counts `MonoFloorWaterEntity` as neither rock nor a hazard | F4 |
| `CavernGutTest.TendonsSlowWalkers` | A `MobHuman` in `WFCavernTendons` walks at under 80% of the speed of one on bare floor | F3 |
| `CavernGutTest.SamplerClassifiesGutFeatures` | The sampler counts acid as a barrier and not rock, tendons as neither, and nerve clusters as lights | F3 |
| `CavernGenerationTest.PadClearAndWorldFloors` [6] | A viewer on the gate pad: nothing but the climb point is anchored on the pad, the world's T and C tiles are present within 24 tiles, the cavern holds at most 4,000 entities and 5–100 `PointLight`s, and a wildlife pass beside one of the world's fauna markers in the loaded chunks spawns wildlife on the cavern map. F3 raised the planned 2,500: a viewer loads 81 chunks (5,184 tiles) and 45–70% of a cavern is rock, so 2,700–3,590 entities load (risk 2) | F3 |
| `CavernAtmosphereTest.HumanOutcomePerWorld` [6] | An unequipped `MobHuman` for 60 s: Asclepiu and Merak take no damage; Carcinoma takes some Poison but is not critical; the others take air, heat or cold damage | F4 |
| `CavernAtmosphereTest.FaunaSurvivesItsCavern` [6] | Every fauna and deep-table mob, on a test map with that cavern's atmosphere, is alive and not critical after 30 s | F4 |
| `CavernAmbienceTest.CavernHearsTheSurfaceMuffled` (client pair) | Asclepiu: a listener on the ground hears the day bed; walked into the cavern, the same stream plays at least 3 dB quieter with the player's occlusion at `surfaceAmbienceOcclusion`, and the cavern's environment has the ground's minute and planet with weather "Underground", which `TryGetReport` on the listener also gives (on the ground it does not); at night the muffled night bed plays; `ambience` set while the listener is below, back on the ground, and down again, the two beds play together for a while each time (a crossfade, not a cut) before the new one plays alone, the cavern's clear. A headless client shares one dummy audio source, so the occlusion is read from the player | F4 |
| `CavernEnvironmentTest.MirrorsClockAndShaftLight` | Within 10 s the cavern's environment has the ground's `PlanetName` and `MinuteOfDay` with weather "Underground", and its `MapLight` equals ground × `shaftLight` | F4 |
| `CavernAmbiencePrototypeTest.SoundsResolve` | Every loop and one-shot exists | F4 |
| `CavernAmbiencePlaybackTest` (client pair) | Superseded by `CavernAmbienceTest` for the bed; still to test: the arrival popup shows once | F4 |
| `CavernHazardTest.VentHissesAndReleasesSmoke` [5] | A vent wall has `AmbientSound`; gathering it spawns an entity with `SmokeComponent` that spreads past one tile | F5 |
| `CavernHazardTest.UnstableRockWarnsThenCavesIn` | With the chance forced to 1, gathering shows the warning popup, then 1.5 s later drops 1–3 rubble within 2 tiles, never on a tile holding a mob, and damages mobs in the radius | F5 |
| `CavernHazardTest.DisturbanceWarnsThenAwakensOnce` | Warning at 75%; one deep group at 100%, 12–20 tiles away; none during the cooldown | F5 |
| `CavernClimbTest.ClimbHaulsPulledOreBox` | A pulled `OreBox` arrives on the ground next to the climber, is being pulled again, and the climb took 1.5× | F5 |

**Manual playtest** (`Docs/_WF/Caverns/PLAYTEST_CHECKLIST.md`, F6, in the Planets checklist's Do/See/Report format):
1. `wfplanet spawn WFSurfaceAsclepiu`, fly down and find the gate. Through the hole you see the plunge pool, the pad
   and the climb point below, lit where the daylight falls in; clicking anywhere on the hole examines it, and the
   examine text gives the air.
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
# V-server: headless boot, prototypes and systems load cleanly. The dev map has no star system, so the worlds are
# built from stdin; a container without IPv6 also needs --cvar net.bindto=0.0.0.0 --cvar status.bind=127.0.0.1:1212.
(sleep 120; for w in Asclepiu Fervidus Merak Aerumna Thrascias Carcinoma; do echo "wfplanet spawn WFSurface$w"; \
  sleep 3; done; sleep 60) | timeout 240 dotnet run --project Content.Server -c DebugOpt --no-build -- \
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

**Status:** F1 is complete. F1a landed the Planets hooks, `wf.caverns`, the placeholder cavern under every world, and
`CavernNetworkTest` and `CavernRoofTest`. F1b landed the hull guard (`WfClosedToHulls`, `WfRefusesLevelHop` and the
four marked CE lines), the eye cap in `CEZLevelsSystem.View.cs`, the wildlife `CEZLevelFallMapEvent` handler with
`BiomeSystem.Caverns.cs` (`WfIsChunkLoaded` only; `WfIsBiomeSpawned` comes with F2), `caverns = true` in
`development.toml`, so development builds now have caverns on, and `CavernViewerEyeTest`, `CavernHullTest` (the F1
cases) and `CavernWildlifeTest`. F2a added the section 2.9 chamber layer to the placeholder (`WFCavernChamberPlaceholder`:
`FloorPlanetDirt`, FBm frequency 0.025, ≥ 0.35), so mouth pads can pass the openness check (3.2).

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
  - `Resources/ConfigPresets/Build/development.toml`: `caverns = true` (F1b, with the hull guard and the eye cap).
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

**Status:** F2 ships in three parts; F2a and F2b have landed, F2c remains.
- **F2a (landed):** the gate mouth on every world, claimed at build; falling in; the shades, their examine and the
  landing tiles; the climb points (placed, no verbs yet); the orbital-fall fix; and `wfcavern list|tp|mouths|open`.
  Code: the mouth spec, `WFCavernAir.cs` (`WFCavernAirClassifier`), `WFCavernShaftComponent`, `WFCavernClimbComponent`,
  `SharedWFCavernShaftSystem` (the shaft examine), `WFCavernMouthSystem` with `.Claims.cs`, the registry on
  `WFCavernGroundComponent`, `WfIsBiomeSpawned`, `WFCavernCommand`, `Entities/mouths.yml`, the five new landing tiles
  and the `mouths:` blocks. Tests: `CavernMouthTest.GateExists` and `.GateSurvivesUnloadReload`, `CavernFallTest`,
  `CavernOrbitalFallTest`, `CavernCommandTest`, the two F2 `CavernHullTest` cases and `CavernAirTest`.
- **F2b (landed): climbing.** `SharedWFCavernClimbSystem` (verbs, activation, DoAfter, predicted popups, the
  climb-point examine) with its empty client subclass, `WFCavernClimbDoAfterEvent`, `WFCavernClimbSystem` (the
  moves and `FindExit`, 3.5 and 3.6), the verb, popup and examine keys in `caverns.ftl`, and `CavernClimbTest`. The
  climb points' `Delay` was already stamped by F2a. It also carried the F2a review fixes: `CavernOrbitalFallTest` on
  Fervidus, `open`'s own `cavern` refusal, `mouths` printing its kind through `-mouth-kind`, `wfcavern` matching a
  planet by every name section 5 lists, and summaries on `WFCavernAir` and `CavernAirTest`.
- **F2c (remaining): lazy claims and the hole queue.** The 0.5 s polling that claims cells ahead of viewers through
  `TryClaimCell`, `WFCavernMouthSystem.Holes.cs` (`(WFCavernGroundComponent, TileChangedEvent)`, `EnsureHole`,
  `EnsureClimbNear`), `CavernHoleTest`, and `CavernMouthTest.ClaimAheadOfViewer`, `.ClaimDeferredWhileChunkLoaded`
  and `.NoMouthOffTheAllowlist`.
- **F2a deviations** (each recorded where it applies): the classifier is `WFCavernAirClassifier.Classify` (2.5); the
  shaft examine lives in `SharedWFCavernShaftSystem` (2.7); the ground component keeps `Centre` (2.5); the gate search
  starts at the cell holding the gate candidate (3.2); the placeholder biome gained chambers (3.2, F1); each world's
  `avoid` adds its outcrop spawner, and Thrascias' rim uses the two north corners (4); `open` ignores the caller and
  treats spawner-made outcrop walls as built, and `-row-none` was added (5); `HullOverMouthStaysOnGround` uses a 3×3
  `BuildDebris` grid (6). No section 4 id needed substituting.
- **F2b deviations** (each recorded where it applies): the climb DoAfter sets `MultiplyDelay = false`, and the verbs
  need the climber on the target's own grid (2.7); the client registers the shared system through an empty
  `WFCavernClimbSystem` (2.7); climbing down lands on the pad tile of the climb point itself (3.5); the exit search is
  the 5×5 square, nearest first, and the hull popup wins when a hull covered any solid, non-hole tile (3.6);
  `ClimbRefusedUnderHull` parks a static debris grid, and `DelayScalesWithGravity` times the climb by the game clock
  (6). After review: climbing down checks its landing the same way (`TryFindLanding`) and moves nobody until it is
  found, with its own `wf-cavern-climb-down-blocked` popup (3.5, 5); ambient damage never breaks a climb, which
  `UnequippedClimberEscapesHostileAir` proves (3.6); 2.1 item 12's 300 Blunt was wrong, an unfixed orbital fall into
  a mouth being an ordinary five-level landing, so the orbital test is now `OrbitalFallIntoMouthMaimsLikeGround` (2.1,
  2.2, 3.5, 6).

- **Shaped mouths** (after F2b): holes grown per seed to each world's style and size, never a plain rectangle (3.1,
  4), rim decor on random lip tiles in proportion to the hole, and pit art on the dual grid that cuts into the lip,
  rounds the corners, darkens towards the middle and carries a world-specific edge (4.1); `ShapesStayInRange` and
  `PitStatesExist` (6). The climb tests climb down from the hole tile over the climb tile, which is in reach. After
  review: a pinned site is Empty rather than Deferred (3.2), `open` refuses a shared pad (5), the open-fraction
  sample centres on the gate candidate (4.1), Thrascias moulins are 7–14 tiles (4.6), and debris wholly over a hole
  is tested (3.7).

- **Cavern view** (after the shaped mouths): a hole shows the cavern under it. The eye cap opens for a viewer with a
  hole in view (2.3, `WFCavernEyeSystem`), the client draws the cavern as a pass under the ground and keeps the sky
  out (2.7), and the pit art became a lip over the hole's own tiles with a clear middle, clickable across the whole
  tile (4.1). Tests: the new `CavernViewerEyeTest` rows, `CavernViewTest` and `CavernPassTest` (6). Not yet seen on a
  client. The cavern's `MapLight` is still its level's, not the ground's × `shaftLight` (F4), so at night the floor
  under a hole stays lit. After review: the eye's range is half of `net.pvs_range` (it was the whole side, so the
  cavern opened about twice as far out as meant), ghosts that load no terrain get no cavern eye, the pass box widens
  by the ground's absolute depth (a jumping observer's box was up to 15% too narrow), the shaft wall no longer breaks
  at a piece's bottom border, and the cost test runs with PVS on.

- **Add:**
  - Shared: `WFCavernShaftComponent.cs`, `WFCavernClimbComponent.cs`, `WFCavernClimbDoAfterEvent.cs`,
    `SharedWFCavernClimbSystem.cs`, `SharedWFCavernShaftSystem.cs`, `WFCavernAir.cs`, `WFCavernMouthSpec.cs`. The mouth
    spec is added to `WFCavernPrototype`.
  - Server: `WFCavernMouthSystem.cs`, `WFCavernMouthSystem.Claims.cs`, `WFCavernMouthSystem.Holes.cs`,
    `WFCavernClimbSystem.cs`, `WFCavernCommand.cs`. `WFCavernGroundComponent` gains the registry.
  - Client: `Content.Client/_WF/Caverns/WFCavernClimbSystem.cs`, the empty subclass that predicts the climb verbs.
  - Prototypes:
    - `Entities/mouths.yml`: `WFCavernShadeBase`, `WFCavernClimbBase`, and six of each;
    - `tiles.yml`: the six landing tiles;
    - `caverns.yml`: the `mouths:` blocks from section 4.
  - Locale: `caverns.ftl` (examine, verbs and popups), `entities.ftl`, `commands.ftl`.
  - `WolfgateAdminCommands.Cavern` (no marker).
  - Tests: `CavernMouthTest.cs`, `CavernFallTest.cs`, `CavernHoleTest.cs`, `CavernClimbTest.cs`, `CavernHullTest.cs`
    (the F2 cases), `CavernOrbitalFallTest.cs`, `CavernCommandTest.cs`, and
    `Content.Tests/_WF/Caverns/CavernAirTest.cs`.
  - Placeholder biome: the chamber layer (F2a, see the F1 status).
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

**Status:** F3 is built and its tests pass; it lands after the visual pass below, which nobody has done yet. Each world has its biome file (biome, rock, chamber and signature templates, with named
sub-templates for the tube core, the lake, the groves, the geodes and the calcified nodes, and Merak's camps), the five remaining tiles,
`WFCavernGlowworms` and `WFCavernGutGlow` in `Entities/decor.yml`, the six `WFCavernFauna<World>` markers and tables in
`Entities/fauna.yml`, `WFCavernSampler` (an entity system returning a `WFCavernSample`), `wfcavern stats`, and
`CavernPrototypeTest`, `CavernBiomeTest` and `CavernGenerationTest`. Every id section 4 names exists on this branch, so
none was substituted. Left for later, as planned: vents, unstable rock, gas pockets, cave-ins and deep tables (F5), and
ambience and arrival popups (F4).
- **F3 deviations** (each recorded where it applies): the Fervidus, Aerumna and Carcinoma skeletons and the Thrascias
  galleries were tuned (4.3, 4.5–4.7); the Fervidus tube core and the Thrascias lakes run in stretches, and connectivity
  counts lava and plasma as walls (4.1); Merak's camps, Aerumna's groves and Thrascias's crystals were made denser
  (4.4–4.6); open means no airtight entity, and veins run above the 15% estimate (4.1); the generation bound is 4,000
  instead of 2,500 (6, risk 2); `stats` also prints the ore share and samples around the gate for a caller elsewhere
  (5); Carcinoma's node ores got tiers (4.7). Values section 4 leaves open were chosen as follows: chamber decor, webs,
  glow, crystals and loot at OpenSimplex2 frequency 1; golem and crab walls at frequency 0.2; grove trees at frequency
  2; the magma lakes at 2 FBm octaves; the Asclepiu pools, Merak camps and Aerumna groves and geodes sit in the chamber
  template as `WFCavernSignature<World>`; signature seeds are 55 and up (the geodes 56, Merak's camps 57, the tube
  core's stretches 58, the lakes' 59), and the Asclepiu skeleton moved to seeds 17 and 117 (4.2); Aerumna, Thrascias
  and Carcinoma have no tunnel litter, as their tables list none. The fauna markers are named in `entities.ftl`.
- **Glow and Gut pass** (after F3, at the user's request): each world got a glowing plant as its main light (4.1,
  `Entities/flora.yml`), and the Gut got digestive acid pools with bile and bones around them and tendon-choked
  throats (4.7, `Entities/gut.yml`, `WFCavernBile1`–`4`). The art is existing sprites recoloured by
  `Tools/_WF/Caverns/gen_flavour.py` (`digestive_acid.rsi` is `lava.rsi` hue-shifted; `glow_flora.rsi` holds the six
  plants); names are in `entities.ftl`. `WFCavernSampler` counts acid as a hazard and walks from the tunnels to the
  nearest light, `wfcavern stats` prints how much of the tunnel floor is within a 35-tile walk of one, and
  `CavernGutTest` and `CavernBiomeTest.GlowReachesTunnels` are new. A review then gave the acid its own
  `WFDigestiveAcid`, as `DamageContacts` could spare none of the Gut's creatures (none carries the `Flesh` tag it
  checked), replaced the glow test's straight-line reach through walls with the walk, and filled Aerumna's crawls,
  which the old measure passed while they were nearly unlit. This settles the
  sparse glow the F3 visual pass was to check. Not yet seen on a client: how the unshaded glow layers and the
  hue-shifted acid read in game, and whether the tendons show against the flesh floor.
- **Needs the user:** the visual pass on a client (each world against section 4, with the Aerumna and Carcinoma
  tunings, the denser signatures and the glow and Gut pass in mind), and whether 21–26% veins and the Fervidus diamond
  share (more diamond than gold or silver, as 4.3's thresholds give) are wanted.

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

- **Built early** (after a walk through the caverns): the environment and soundscape mirror in `WFCavernSystem`;
  `ambience`, `surfaceAmbienceVolume` and `surfaceAmbienceOcclusion` on `WFCavernPrototype`; the muffle and
  same-world crossfade in the Planets `WFPlanetAmbienceSystem` (2.7); `wf-cavern-weather-underground`; the acid hiss
  (`WFDigestiveAcidHissSystem`); Asclepiu's water as the surface's entity; and `CavernAmbienceTest.cs`. The default
  underground soundscape is the surface's, muffled; a cavern names its own only when it wants one.
- **Add:** the shaft-light mirror; arrival keys; and the tests `CavernAtmosphereTest.cs`,
  `CavernEnvironmentTest.cs` and an arrival-popup test.
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
| 1 | The client has never seen the cavern map before its first fall, so predicted z-physics may stutter until PVS arrives (`Update.cs:22`, `_clientSimulation`) | Real-client check in F2. Fallback: `CEPvsOverride` on the cavern map entity, after measuring `BiomeComponent`'s state size. Since the cavern view, a player next to a mouth already has the cavern around it streamed in |
| 2 | Cavern chunk loads are dense: F3 measured 2,700–3,590 entities around one cavern viewer (81 chunks), not the 1,300 walls first estimated | `CavernGenerationTest` bounds it at 4,000. F3 profiled one viewer per world (Debug integration server, server time only): arriving in a cavern costs one tick of 0.6–0.8 s while its chunks load, against 0.3–5.7 s arriving on the same world's surface, and a cavern tick then costs 0.8–1.1 ms against 0.7–1.2 ms on the surface |
| 3 | FTL preloads or admin teleports load a cell before its claim | That cell stays Deferred until the chunk unloads. Accepted: a mouth may appear late, never cut into loaded terrain |
| 4 | An awake item over a ground chunk that unloads falls into the cavern void (2.1 item 4) | Wildlife is handled (F1). Items are rare, because sleeping bodies don't fall. Accepted |
| 5 | A player floor laid on a pad and deconstructed down to space opens the bottom layer, because pads aren't natural terrain for `WfIsPlanetTerrain` | Needs deliberate multi-step work, and the result is "stuck in the floor", not death. Accepted |
| 6 | A hole opened over a player-built cavern structure lands the faller inside it (the queue only clears biome walls) | Rare. Accepted |
| 7 | A pod or debris up to about 4×4 wholly over a hole churns up and back | This is today's behaviour over unloaded terrain. Covered by `DebrisInsideMouthNeverEntersCavern` |
| 8 | Fauna may not survive CO₂, ammonia, heat or cold | `FaunaSurvivesItsCavern` decides the tables in F4 |
| 9 | `SmokeOnTrigger` may not spread on a map grid without `GridAtmosphere` | `VentHissesAndReleasesSmoke` decides. Fallback: vents spawn a puddle of the reagent instead |
| 10 | Restarting a pull across a map change may be refused | `ClimbHaulsPulledOreBox` decides. Fallback: move the hauled entity without restarting the pull |
| 11 | `GetNoise` allocates on every call, so claims cost about 1 ms per candidate and sampled tests take seconds per world | Claims are spread over time and logged above 20 ms. If tests are too slow, add a cached sampler to `BiomeSystem.Caverns.cs` and assert it agrees with `TryGetTile` |
| 12 | Six more maps share the 128 fauna cap | Cavern fauna retires without observers, and the eye cap keeps surface viewers' eyes out of the cavern except near a mouth. Watch `PlanetPopulationTest` |
| 13 | For the first 0.1 s after a load, a cavern chunk can show shaft light before the ground above it loads | Cosmetic. Both load in the same `BiomeSystem` pass |
| 14 | The eye cap, the cavern pass and the hull guard touch CE files that change upstream | Single-line marked edits that call into `_WF`. Recheck on every CE merge |
| 15 | `Nocturine` spore pockets in Aerumna's dark with xenos may be too punishing | Small spread (≤ 6 tiles) and the hiss warns first. Tune after the F5 playtest |
| 16 | `MobWatcherMagmawing` and `MobWatcherIcewing` are flying lavaland mobs | `FaunaSurvivesItsCavern` checks them, and no cavern mob has `CEZFlyer` |
| 17 | Lava and liquid plasma hurt by setting you alight, and `FlammableSystem` puts a fire out below 1 mol of oxygen, so in the oxygen-free Fervidus and Thrascias caverns they do nothing (measured in the F3 review) | F4 gives the cavern liquids harm that needs no oxygen and checks it in `CavernAtmosphereTest` |
| 17 | The climb breaks on damage, so hostile air could trap an unprepared player below if ambient damage ever interrupted DoAfters | It doesn't today: air, heat, cold, suffocation, pressure and metabolism damage pass `interruptsDoAfters: false` (3.6). `UnequippedClimberEscapesHostileAir` fails if an upstream merge changes that; the fallback is to stop breaking the climb on damage without an origin |
