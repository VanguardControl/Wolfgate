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
- **Metallic rocks:** each cluster rock has a 3% chance to roll `WFAsteroidMetallic` instead of its cluster's kind: a
  blue-grey rock that is mostly iron, silver and gold, sometimes diamond, with little plain rock. It resists heat
  (45-60%), so it needs high power or two lasers. Fragments and boulders from it stay metallic.
- **Radar:** asteroid grids carry no label on any scanner. `WFAdminRadarUnlabelled` keeps the admin scanner's true
  IFF from adding one.
- **Rocks:** each asteroid is one rock entity (`WFAsteroidRock*`) anchored at the centre of a small grid of invisible,
  indestructible `WFAsteroidCore` tiles (`WFAsteroid{Small,Medium,Large,Huge}`). The grid gives the rock ship
  collisions, a radar hull, FTL clearance and tractor beam support. Each rock rolls a mass (in ore veins, plain rock
  included), one to three ores from its `wfAsteroidComposition`, a resistance, an instability and a cracking window.
- **Cracking:** the `WFWeaponTurretMiningBeam` (M40 Prospector) is a heavy energy ship gun in the M25/Apollo family,
  aimed and fired from a gunnery console. Its `WFMiningBeamPulse` hitscan pulses (10 a second) heat the asteroid they
  hit instead of damaging it, and its fork spins while it fires. The gunnery console grows a mining laser panel
  (`FireControlWindow.AsteroidMining`, under the shield control) once its ship has a mining laser: it sets the power
  of every mining laser on the ship in 1/10% steps (70% by default, which never overcharges) and scans the rock they
  are on, or were last on while it cools (ores, plain rock, resistance, heat and window, crack progress). Heat settles at `power × strength × (1 − resistance)` and
  unstable rocks drift around it. Holding the heat in the green window fills the crack bar (`4 s + 0.1 s` per vein).
  The cyan notch on the meter shows where the current power will settle, so the power can be set without timing;
  the trigger can also be feathered by eye, since the impact flare brightens and its sparks double while the rock is
  in its window. The beam is drawn along each pulse's shot, out to where it hit. Several lasers on
  one rock add their power, so tough rocks are a two-ship job.
- **Outcomes:** a clean crack keeps all the mass. Large and huge rocks split into two smaller asteroids (half or
  three fifths of the mass); of the rest, a quarter comes out as boulders and the remainder as floating ore piles.
  Staying over the red line for 2.5 s blows the rock up: half the mass survives, the blast grows with plasma, and the
  lasers can't fire for 10 s. Weapons and explosions shatter a rock for 40% of its mass once it has taken its
  `shatterDamage`. Past 60% crack progress the rock shakes; when it breaks, `WFAsteroidCrackEffect` draws a flash,
  a shockwave, flying chips and a dust cloud, and fragments fly apart before the grids' damping settles them. Anything
  else on a cracked asteroid's grid, players included, is left floating where it was.
- **Hand mining:** `WFAsteroidBoulder` is a pullable chunk of 2-4 veins. Pull it aboard and break it with a pickaxe,
  drill or kinetic accelerator (it is `Gatherable`), or crack it in space with the mining laser like a small rock, for
  all of its ore. It is opaque to the laser's ray and blocks bullets.
- **Collection:** ore floats where the rock was; ore magnets, ore boxes and tractor beams pick it up as before.
- **Getting one:** the laser board is in the cargo board pack and unlocks with Mass Excavation; the laser mounts on
  a heavy energy hardpoint.

`WFAsteroidSystem` swaps worldgen debris, fills clusters, builds the grids, rolls compositions and runs queued cracks;
`DebrisFeaturePlacerSystem.Asteroids` gives cluster rocks and fragments to their world chunk so they unload with it.
`WFMiningBeamSystem` turns pulses into heat and feeds the gunnery consoles' mining panel. On the client, `WFAsteroidVisualsSystem` picks
sprite variants, tints the veins by the leading ore and lights the cracks with heat, `WFMiningBeamOverlay` draws the
beams, `WFAsteroidCrackOverlay` draws rocks breaking and `WFMiningBeamVisualizerSystem` spins the fork.

Balance knobs: which kinds swap and the size budgets in `replacement.yml`, rock masses, fragment and boulder shares in
`asteroids.yml`, ore weights and rock stats in `compositions.yml`, and laser strength on `WFMiningBeamEmitterComponent`
and its fire rate and battery in `mining_beam.yml`.

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
- [`Content.Shared/_WF/AsteroidMining/WFAsteroidReplacementPrototype.cs`](../../../Content.Shared/_WF/AsteroidMining/WFAsteroidReplacementPrototype.cs)
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
- [`Resources/Textures/_WF/AsteroidMining/Asteroids/asteroid_large.rsi/`](../../../Resources/Textures/_WF/AsteroidMining/Asteroids/asteroid_large.rsi/)
- [`Resources/Textures/_WF/AsteroidMining/Asteroids/asteroid_medium.rsi/`](../../../Resources/Textures/_WF/AsteroidMining/Asteroids/asteroid_medium.rsi/)
- [`Resources/Textures/_WF/AsteroidMining/Asteroids/asteroid_small.rsi/`](../../../Resources/Textures/_WF/AsteroidMining/Asteroids/asteroid_small.rsi/)
- [`Resources/Textures/_WF/AsteroidMining/Asteroids/boulder.rsi/`](../../../Resources/Textures/_WF/AsteroidMining/Asteroids/boulder.rsi/)
- [`Resources/Textures/_WF/AsteroidMining/Structures/mining_beam.rsi/`](../../../Resources/Textures/_WF/AsteroidMining/Structures/mining_beam.rsi/)
- [`Resources/Textures/_WF/AsteroidMining/Tiles/asteroid_core.png`](../../../Resources/Textures/_WF/AsteroidMining/Tiles/asteroid_core.png)

## Non-modular edits

- [`Content.Client/_Mono/FireControl/UI/FireControlConsoleBoundUserInterface.cs`](../../../Content.Client/_Mono/FireControl/UI/FireControlConsoleBoundUserInterface.cs): the mining laser panel's scan
- [`Content.Server/Worldgen/Systems/Debris/DebrisFeaturePlacerSystem.cs`](../../Worldgen/Systems/Debris/DebrisFeaturePlacerSystem.cs): a placing handler may swap the debris
- [`Resources/Prototypes/Recipes/Lathes/Packs/cargo.yml`](../../../Resources/Prototypes/Recipes/Lathes/Packs/cargo.yml): mining laser board
- [`Resources/Prototypes/Research/industrial.yml`](../../../Resources/Prototypes/Research/industrial.yml): mining laser board

<!-- WOLFGATE-GENERATED END -->
