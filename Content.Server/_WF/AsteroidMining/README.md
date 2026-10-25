# AsteroidMining

Belt asteroids as single rocks, cracked with a ship-mounted mining laser (Star Citizen-style rock cracking with EVE
Online-style beams). Worldgen swaps every ore tile asteroid, in the Wilds, the 9 km ring, Cluster A and Very Far, for
a cluster of single rocks of the same kind holding the same amount of ore. Scrap asteroids and wrecks stay as tiles,
and so do half of the gas-bearing rock asteroids, which carry the gas deposits gas mining needs.

- **Worldgen:** `replacement.yml` maps each tile asteroid kind (rock, ice, basalt, sand, chromite, andesite) to a
  cluster marker. `WFAsteroidSystem` swaps it through `WFDebrisPlacingEvent` and gives the cluster the tile
  asteroid's rock count as its mass budget. A tick later the marker scatters rocks around its centre by its size
  weights (small the most common, huge the rarest) until the budget runs out, hands them to its world chunk and
  removes itself. Compositions mirror each kind's mineral table, ore weights and plain-rock share, so a zone's ore mix
  and amount stay as they were (chromite is still where lithium comes from).
- **Metallic rocks:** each cluster rock has a 3% chance to roll `WFAsteroidMetallic` instead of its cluster's kind:
  mostly iron, silver and gold, sometimes diamond, with little plain rock, drawn from the `metallic` look's RSIs
  (blue steel with bare plates). It resists heat (45-60%), so it needs high power or two lasers. Fragments and
  boulders from it stay metallic.
- **Radar:** asteroid grids carry no label on any scanner. `WFAdminRadarUnlabelled` keeps the admin scanner's true
  IFF from adding one.
- **Rocks:** each asteroid is one rock entity (`WFAsteroidRock*`) anchored at the centre of a small grid of invisible,
  indestructible `WFAsteroidCore` tiles (`WFAsteroid{Small,Medium,Large,Huge}`). The grid gives the rock ship
  collisions, a radar hull, FTL clearance and tractor beam support. Each rock rolls a mass (in ore veins, plain rock
  included), one to three ores from its `wfAsteroidComposition`, a resistance, an instability and a cracking window.
  Sprite variants 0-2 are round; 3-5 are elongated, lobed or bitten, and their `shapes` (from the sprite generator)
  give the tiles' outline, a convex hull fixture and a smaller mass.
- **Cracking:** the `WFWeaponTurretMiningBeam` (M40 Prospector, heavy) and `WFWeaponTurretMiningLaserSmall` (M12
  Prospector, light: 40 m, about half the heat and crack speed) are energy ship guns in the M25/Apollo family, aimed
  and fired from a gunnery console. Their hitscan pulses (10 a second) heat the asteroid or boulder they hit instead of
  damaging it, and their forks spin while they fire. The gunnery console grows a mining laser panel
  (`FireControlWindow.AsteroidMining`, under the shield control) once its ship has a mining laser: it sets the power
  of every mining laser on the ship in 1/10% steps (50% by default) and scans the rock they are on, or were last on
  while it cools (ores, plain rock, resistance, heat and window, crack progress, yield). Heat settles at
  `power × strength × (1 − resistance)` and unstable rocks drift around it. In the green window the crack bar fills at
  the beams' summed crack rate (`1.5 s + 0.035 s` per vein, divided by 1.6 for an M40 and 0.6 for an M12). The yellow band past it fills the bar half as
  fast again but each second there costs ore, down to half the rock for a crack made all in yellow. The cyan notch on
  the meter shows where the current power will settle; the impact flare brightens in the window and its sparks turn
  red in the yellow band, so the trigger can also be feathered by eye. Several lasers on one rock add their power and
  crack rate.
- **Outcomes:** a clean crack keeps the mass its yield allows. Large and huge rocks split into two smaller asteroids
  (two fifths or half of the mass), which come off as hot as their parent; of the rest, a quarter comes out as boulders and the remainder as floating ore
  piles. Holding the rock past the red line (heat 85%) for 2 s blows it up: 35% of the mass survives, the blast grows
  with plasma, and the lasers can't fire for 10 s. Weapons and explosions shatter a rock for a tenth of its mass once
  it has taken its `shatterDamage` (about 530 per vein), so an Apollo or M25 can break a rock in a pinch, but slowly and for
  far less ore than a mining laser. Past 60% crack progress the rock shakes; when it breaks, `WFAsteroidCrackEffect` draws a
  flash, a shockwave, small tumbling shards and a dust cloud, and fragments fly apart before the grids' damping
  settles them. Anything else on a cracked asteroid's grid, players included, is left floating where it was.
- **Hand mining:** `WFAsteroidBoulder` is a pullable chunk of 2-4 veins. Pull it aboard and break it with a pickaxe,
  drill or kinetic accelerator (it is `Gatherable`), or crack it in space with the mining laser like a small rock, for
  all of its ore. It is opaque to the laser's ray and blocks bullets.
- **Collection:** ore floats where the rock was; ore magnets, ore boxes and tractor beams pick it up as before.
- **Getting one:** cargo sells both as flatpacks under Shuttle (M12 2,500, M40 7,500). Their boards are in the cargo
  board pack and cost real materials; the M12 unlocks with Salvage Equipment, next to the M25, and mounts on a light
  energy hardpoint; the M40 unlocks with Mass Excavation and needs a heavy one.

`WFAsteroidSystem` swaps worldgen debris, fills clusters, builds the grids, rolls compositions and runs queued cracks;
`DebrisFeaturePlacerSystem.Asteroids` gives cluster rocks and fragments to their world chunk so they unload with it.
`WFMiningBeamSystem` turns pulses into heat and feeds the gunnery consoles' mining panel; `WFAsteroidHeat` holds the
bands. On the client, `WFAsteroidVisualsSystem` picks sprite variants and looks, tints the veins by the leading ore
and lights the cracks with heat, `WFMiningBeamOverlay` draws the beams, `WFAsteroidCrackOverlay` draws rocks breaking
and `WFMiningBeamVisualizerSystem` spins the fork.

Balance knobs: which kinds swap and the size budgets in `replacement.yml`, rock masses, shatter damage, fragment and
boulder shares in `asteroids.yml`, ore weights and rock stats in `compositions.yml`, the bands in `WFAsteroidHeat`,
and each laser's strength, crack rate, range, fire rate and battery in `mining_beam.yml`.

The rock sprites come from a procedural generator (`Tools/_WF/AsteroidMining/gen_rocks.py`); rerunning it
rewrites the RSIs and the irregular variants' `shapes`, which must then be copied into `asteroids.yml`.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/AsteroidMining/DebrisFeaturePlacerSystem.Asteroids.cs`](DebrisFeaturePlacerSystem.Asteroids.cs)
- [`Content.Server/_WF/AsteroidMining/WFAsteroidClusterComponent.cs`](WFAsteroidClusterComponent.cs)
- [`Content.Server/_WF/AsteroidMining/WFAsteroidGridComponent.cs`](WFAsteroidGridComponent.cs)
- [`Content.Server/_WF/AsteroidMining/WFAsteroidSystem.cs`](WFAsteroidSystem.cs)
- [`Content.Server/_WF/AsteroidMining/WFMiningBeamSystem.cs`](WFMiningBeamSystem.cs)

### Shared

- [`Content.Shared/_WF/AsteroidMining/WFAsteroidComponent.cs`](../../../Content.Shared/_WF/AsteroidMining/WFAsteroidComponent.cs)
- [`Content.Shared/_WF/AsteroidMining/WFAsteroidCompositionPrototype.cs`](../../../Content.Shared/_WF/AsteroidMining/WFAsteroidCompositionPrototype.cs)
- [`Content.Shared/_WF/AsteroidMining/WFAsteroidCrackEffectComponent.cs`](../../../Content.Shared/_WF/AsteroidMining/WFAsteroidCrackEffectComponent.cs)
- [`Content.Shared/_WF/AsteroidMining/WFAsteroidHeat.cs`](../../../Content.Shared/_WF/AsteroidMining/WFAsteroidHeat.cs)
- [`Content.Shared/_WF/AsteroidMining/WFAsteroidReplacementPrototype.cs`](../../../Content.Shared/_WF/AsteroidMining/WFAsteroidReplacementPrototype.cs)
- [`Content.Shared/_WF/AsteroidMining/WFAsteroidShape.cs`](../../../Content.Shared/_WF/AsteroidMining/WFAsteroidShape.cs)
- [`Content.Shared/_WF/AsteroidMining/WFMiningBeamEmitterComponent.cs`](../../../Content.Shared/_WF/AsteroidMining/WFMiningBeamEmitterComponent.cs)
- [`Content.Shared/_WF/AsteroidMining/WFMiningBeamGunneryMessages.cs`](../../../Content.Shared/_WF/AsteroidMining/WFMiningBeamGunneryMessages.cs)
- [`Content.Shared/_WF/AsteroidMining/WFMiningPulseComponent.cs`](../../../Content.Shared/_WF/AsteroidMining/WFMiningPulseComponent.cs)

### Client

- [`Content.Client/_WF/AsteroidMining/FireControlWindow.AsteroidMining.cs`](../../../Content.Client/_WF/AsteroidMining/FireControlWindow.AsteroidMining.cs)
- [`Content.Client/_WF/AsteroidMining/WFAsteroidCrackOverlay.cs`](../../../Content.Client/_WF/AsteroidMining/WFAsteroidCrackOverlay.cs)
- [`Content.Client/_WF/AsteroidMining/WFAsteroidVisualsSystem.cs`](../../../Content.Client/_WF/AsteroidMining/WFAsteroidVisualsSystem.cs)
- [`Content.Client/_WF/AsteroidMining/WFMiningBeamOverlay.cs`](../../../Content.Client/_WF/AsteroidMining/WFMiningBeamOverlay.cs)
- [`Content.Client/_WF/AsteroidMining/WFMiningBeamVisualizerSystem.cs`](../../../Content.Client/_WF/AsteroidMining/WFMiningBeamVisualizerSystem.cs)
- [`Content.Client/_WF/AsteroidMining/WFMiningChargeMeter.cs`](../../../Content.Client/_WF/AsteroidMining/WFMiningChargeMeter.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/AsteroidMining/AsteroidMiningTest.cs`](../../../Content.IntegrationTests/Tests/_WF/AsteroidMining/AsteroidMiningTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/AsteroidMining/asteroids.yml`](../../../Resources/Prototypes/_WF/AsteroidMining/asteroids.yml)
- [`Resources/Prototypes/_WF/AsteroidMining/compositions.yml`](../../../Resources/Prototypes/_WF/AsteroidMining/compositions.yml)
- [`Resources/Prototypes/_WF/AsteroidMining/mining_beam.yml`](../../../Resources/Prototypes/_WF/AsteroidMining/mining_beam.yml)
- [`Resources/Prototypes/_WF/AsteroidMining/replacement.yml`](../../../Resources/Prototypes/_WF/AsteroidMining/replacement.yml)
- [`Resources/Prototypes/_WF/AsteroidMining/shaders.yml`](../../../Resources/Prototypes/_WF/AsteroidMining/shaders.yml)
- [`Resources/Prototypes/_WF/AsteroidMining/tiles.yml`](../../../Resources/Prototypes/_WF/AsteroidMining/tiles.yml)

### Localization

- [`Resources/Locale/en-US/_WF/AsteroidMining/asteroid-mining.ftl`](../../../Resources/Locale/en-US/_WF/AsteroidMining/asteroid-mining.ftl)

### Textures

- [`Resources/Textures/_WF/AsteroidMining/Asteroids/asteroid_huge.rsi/`](../../../Resources/Textures/_WF/AsteroidMining/Asteroids/asteroid_huge.rsi/)
- [`Resources/Textures/_WF/AsteroidMining/Asteroids/asteroid_huge_metallic.rsi/`](../../../Resources/Textures/_WF/AsteroidMining/Asteroids/asteroid_huge_metallic.rsi/)
- [`Resources/Textures/_WF/AsteroidMining/Asteroids/asteroid_large.rsi/`](../../../Resources/Textures/_WF/AsteroidMining/Asteroids/asteroid_large.rsi/)
- [`Resources/Textures/_WF/AsteroidMining/Asteroids/asteroid_large_metallic.rsi/`](../../../Resources/Textures/_WF/AsteroidMining/Asteroids/asteroid_large_metallic.rsi/)
- [`Resources/Textures/_WF/AsteroidMining/Asteroids/asteroid_medium.rsi/`](../../../Resources/Textures/_WF/AsteroidMining/Asteroids/asteroid_medium.rsi/)
- [`Resources/Textures/_WF/AsteroidMining/Asteroids/asteroid_medium_metallic.rsi/`](../../../Resources/Textures/_WF/AsteroidMining/Asteroids/asteroid_medium_metallic.rsi/)
- [`Resources/Textures/_WF/AsteroidMining/Asteroids/asteroid_small.rsi/`](../../../Resources/Textures/_WF/AsteroidMining/Asteroids/asteroid_small.rsi/)
- [`Resources/Textures/_WF/AsteroidMining/Asteroids/asteroid_small_metallic.rsi/`](../../../Resources/Textures/_WF/AsteroidMining/Asteroids/asteroid_small_metallic.rsi/)
- [`Resources/Textures/_WF/AsteroidMining/Asteroids/boulder.rsi/`](../../../Resources/Textures/_WF/AsteroidMining/Asteroids/boulder.rsi/)
- [`Resources/Textures/_WF/AsteroidMining/Asteroids/boulder_metallic.rsi/`](../../../Resources/Textures/_WF/AsteroidMining/Asteroids/boulder_metallic.rsi/)
- [`Resources/Textures/_WF/AsteroidMining/Structures/mining_beam.rsi/`](../../../Resources/Textures/_WF/AsteroidMining/Structures/mining_beam.rsi/)
- [`Resources/Textures/_WF/AsteroidMining/Structures/mining_laser_small.rsi/`](../../../Resources/Textures/_WF/AsteroidMining/Structures/mining_laser_small.rsi/)
- [`Resources/Textures/_WF/AsteroidMining/Tiles/asteroid_core.png`](../../../Resources/Textures/_WF/AsteroidMining/Tiles/asteroid_core.png)

### Tools

- [`Tools/_WF/AsteroidMining/gen_rocks.py`](../../../Tools/_WF/AsteroidMining/gen_rocks.py)

## Non-modular edits

- [`Content.Client/_Mono/FireControl/UI/FireControlConsoleBoundUserInterface.cs`](../../../Content.Client/_Mono/FireControl/UI/FireControlConsoleBoundUserInterface.cs): the mining laser panel's scan
- [`Content.Server/Worldgen/Systems/Debris/DebrisFeaturePlacerSystem.cs`](../../Worldgen/Systems/Debris/DebrisFeaturePlacerSystem.cs): a placing handler may swap the debris
- [`Resources/Prototypes/_Mono/Shipyard/Civilian/caracara.yml`](../../../Resources/Prototypes/_Mono/Shipyard/Civilian/caracara.yml): fitted with the M40 mining laser in place of the Apollo
- [`Resources/Prototypes/_Mono/Shipyard/MMC/akasono.yml`](../../../Resources/Prototypes/_Mono/Shipyard/MMC/akasono.yml): an M12 mining laser in place of the pulser
- [`Resources/Prototypes/_Mono/Shipyard/MMC/kami.yml`](../../../Resources/Prototypes/_Mono/Shipyard/MMC/kami.yml): M12 mining lasers in place of the mining pulsers
- [`Resources/Prototypes/Recipes/Lathes/Packs/cargo.yml`](../../../Resources/Prototypes/Recipes/Lathes/Packs/cargo.yml): heavy and light mining laser boards
- [`Resources/Prototypes/Research/industrial.yml`](../../../Resources/Prototypes/Research/industrial.yml)
  - light mining laser board
  - mining laser board
- [`Resources/SharedMaps/_Mono/Shuttles/Civilian/caracara.yml`](../../../Resources/SharedMaps/_Mono/Shuttles/Civilian/caracara.yml): Its Apollo mining laser is swapped for the M40 Prospector, the heavy asteroid mining laser.
- [`Resources/SharedMaps/_Mono/Shuttles/MMC/akasono.yml`](../../../Resources/SharedMaps/_Mono/Shuttles/MMC/akasono.yml): Its M25 mining pulsar is swapped for the M12 Prospector, the light asteroid mining laser.
- [`Resources/SharedMaps/_Mono/Shuttles/MMC/kami.yml`](../../../Resources/SharedMaps/_Mono/Shuttles/MMC/kami.yml): Its M25 mining pulsars are swapped for M12 Prospectors, the light asteroid mining laser.
- [`Resources/SharedMaps/_Mono/Shuttles/TSF/shiv.yml`](../../../Resources/SharedMaps/_Mono/Shuttles/TSF/shiv.yml): Its M25 mining pulsars are swapped for M12 Prospectors, the light asteroid mining laser.

<!-- WOLFGATE-GENERATED END -->
