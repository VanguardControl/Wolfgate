# Caverns

Every planet network gets a cavern at depth -1: a biome-backed map built with the network and deleted with it,
roofed by the ground above and dark except where daylight falls through a gap in the ground. Each of the six worlds has
its own cavern (`wfCavern`, one per `wfPlanetSurface`) with its own air, light and geology (`Biomes/<world>.yml`):
the Underkarst's wet limestone and plunge pools, the Cinder Vaults' basalt and lava tubes, the Sandstone Galleries'
pillared halls and fossil beds, the Umbral Deeps' chromite crawls, shadow groves and pink geodes, the Rime Galleries'
ice halls and plasma lakes, and the Gut's flesh throats, stomachs and blood channels. Rock is solid and veined with the
world's ores, chambers hold its decor and wildlife (`WFCavernFauna<World>`, under the planet fauna caps). Each world's
glowing plant (`Entities/flora.yml`: glowcaps, ember lichen, lamp agaves, shadow blooms, rime thistles, nerve clusters)
lines its tunnels and chambers as the main light below ground. The Gut also pools digestive acid in its stomachs, which
digests whoever wades in, air or not, but spares its own creatures and anyone on a catwalk (`WFDigestiveAcidSystem`);
whoever it burns hisses with a looping sizzle until they leave it or die (`WFDigestiveAcidHissSystem`),
and chokes some throats with tendons that slow you (`Entities/gut.yml`).
Caverns are behind `wf.caverns` (`CavernCVars`), which
is off by default and on in development builds, and apply to networks built after it is set. Ships never go below
ground, and nobody on or above the ground loads the cavern under them except near a mouth, where it shows through the
hole.

Every world has a gate mouth near its centre: a pinned hole in the ground, grown from its own seed to the world's shape
and size (`WFCavernMouthShape`: a sinkhole, a skylight, a sand funnel, a rift, a moulin or a throat, never a plain
rectangle), with a solid lip and rim decor in proportion to its size. Looking into a mouth you see the cavern below
through it, drawn by CE's z-level renderer: the landing floor or plunge pool, the climb point and whatever stands down
there, lit where daylight falls in. A shade over each hole tile only frames that view: on the dual grid it draws the
lip crumbling over the hole's own tiles, so the hole shows no square corner, and leaves their middle clear
(`WFCavernShadeVisualsSystem`, art from `Tools/_WF/Caverns/gen_pits.py`). In the cavern a pinned, rock-free pad holds
the world's landing tile under the hole and a climb point under the lip. Players walk in and take a small fall whose damage depends on the landing tile;
examining a shade tells them where it goes, what the air below is like and how hard the landing is. *Climb down* on a
shade (3 s) lowers them unhurt onto the pad instead, and *Climb up* on a climb point (verb or activate; 4 s, longer in
high gravity, up to 10 s on Aerumna) brings them out onto the nearest solid ground beside the hole, refused while a ship
is parked over it. Admins use `wfcavern` to list caverns, teleport to a gate (`tp <planet> [pad|mouth]`), list mouths,
carve one by hand (`open`) and measure the terrain around them (`stats <planet>`: open share, connectivity on foot,
ore share and how much of the tunnel floor is within a short walk of a light, over a 192-tile square, read from noise
by `WFCavernSampler`, the same sampler the cavern tests use).

Entry points: `WFCavernSystem` adds the cavern map through the Planets `WFPlanetLowerLayersEvent`, then fits it out on
`WFPlanetNetworkBuiltEvent` (its own atmosphere, no day cycle, sun shadows or parallax, the roof colour), links the
ground to it with `WFCavernGroundComponent` and asks `WFCavernMouthSystem` to claim the gate. `WFCavernMouthSystem`
evaluates mouth cells from pure noise (`.Claims.cs`: seeded candidates, ground and cavern checks, stamping) and keeps
the registry of cells, mouths, shades and climb points on the ground; its API is `GetGate`, `TryClaimCell`,
`TryOpenMouth` and `TryGetNearestMouth`. The shaft examine is `SharedWFCavernShaftSystem`, fed by the air reading
(`WFCavernAirClassifier`) each shade stores when it spawns. `SharedWFCavernClimbSystem` offers the climb verbs, starts
the DoAfter with its predicted popups and examines climb points; the server's `WFCavernClimbSystem` does the move when
it finishes (`ClimbUp`, `ClimbDown`, `FindExit`). `WFCavernSystem` also deletes surface wildlife that drops
into a cavern because its ground chunk unloaded (`BiomeSystem.WfIsChunkLoaded`), and each second mirrors the ground's
clock onto the cavern (watches read "Underground") along with its soundscape: the surface's day and night ambience,
quieter and muffled through the rock (`surfaceAmbienceVolume`, `surfaceAmbienceOcclusion`), which the Planets player
eases in as you go down, or the cavern's own `ambience` played clear when it names one. Marked CE edits keep the
cavern safe to have and show it: the Planets hull guard (`WfClosedToHulls`); the eye cap in `CEZLevelsSystem.View.cs`,
which stops z-level eyes at a ground layer unless `WFCavernEyeSystem` finds one of its holes in the viewer's view (with
a wider margin to lose it than to gain it); on the client, `WfAddCavernPass` in CE's z-level renderer, which draws the
cavern under the ground while a mouth is in view (`WFCavernViewSystem`); and a check in `ParallaxOverlay` that keeps
the sky out of caverns and mouths. The design, including what is still to come (lazy cell claims and holes opened later,
the rest of F4 and the mining loop), is in `Docs/_WF/Caverns/CAVERNS_DESIGN.md`.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/Caverns/BiomeSystem.Caverns.cs`](BiomeSystem.Caverns.cs)
- [`Content.Server/_WF/Caverns/CEZLevelsSystem.Caverns.cs`](CEZLevelsSystem.Caverns.cs)
- [`Content.Server/_WF/Caverns/WFCavernClimbSystem.cs`](WFCavernClimbSystem.cs)
- [`Content.Server/_WF/Caverns/WFCavernCommand.cs`](WFCavernCommand.cs)
- [`Content.Server/_WF/Caverns/WFCavernEyeSystem.cs`](WFCavernEyeSystem.cs)
- [`Content.Server/_WF/Caverns/WFCavernGroundComponent.cs`](WFCavernGroundComponent.cs)
- [`Content.Server/_WF/Caverns/WFCavernMouthShape.cs`](WFCavernMouthShape.cs)
- [`Content.Server/_WF/Caverns/WFCavernMouthSystem.Claims.cs`](WFCavernMouthSystem.Claims.cs)
- [`Content.Server/_WF/Caverns/WFCavernMouthSystem.cs`](WFCavernMouthSystem.cs)
- [`Content.Server/_WF/Caverns/WFCavernSampler.cs`](WFCavernSampler.cs)
- [`Content.Server/_WF/Caverns/WFCavernSystem.cs`](WFCavernSystem.cs)
- [`Content.Server/_WF/Caverns/WFCavernViewerComponent.cs`](WFCavernViewerComponent.cs)
- [`Content.Server/_WF/Caverns/WFDigestiveAcidHissComponent.cs`](WFDigestiveAcidHissComponent.cs)
- [`Content.Server/_WF/Caverns/WFDigestiveAcidHissSystem.cs`](WFDigestiveAcidHissSystem.cs)

### Shared

- [`Content.Shared/_WF/Caverns/SharedWFCavernClimbSystem.cs`](../../../Content.Shared/_WF/Caverns/SharedWFCavernClimbSystem.cs)
- [`Content.Shared/_WF/Caverns/SharedWFCavernShaftSystem.cs`](../../../Content.Shared/_WF/Caverns/SharedWFCavernShaftSystem.cs)
- [`Content.Shared/_WF/Caverns/WFCavernAir.cs`](../../../Content.Shared/_WF/Caverns/WFCavernAir.cs)
- [`Content.Shared/_WF/Caverns/WFCavernClimbComponent.cs`](../../../Content.Shared/_WF/Caverns/WFCavernClimbComponent.cs)
- [`Content.Shared/_WF/Caverns/WFCavernClimbDoAfterEvent.cs`](../../../Content.Shared/_WF/Caverns/WFCavernClimbDoAfterEvent.cs)
- [`Content.Shared/_WF/Caverns/WFCavernLayerComponent.cs`](../../../Content.Shared/_WF/Caverns/WFCavernLayerComponent.cs)
- [`Content.Shared/_WF/Caverns/WFCavernMouthSpec.cs`](../../../Content.Shared/_WF/Caverns/WFCavernMouthSpec.cs)
- [`Content.Shared/_WF/Caverns/WFCavernPrototype.cs`](../../../Content.Shared/_WF/Caverns/WFCavernPrototype.cs)
- [`Content.Shared/_WF/Caverns/WFCavernShaftComponent.cs`](../../../Content.Shared/_WF/Caverns/WFCavernShaftComponent.cs)
- [`Content.Shared/_WF/Caverns/WFDigestiveAcidComponent.cs`](../../../Content.Shared/_WF/Caverns/WFDigestiveAcidComponent.cs)
- [`Content.Shared/_WF/Caverns/WFDigestiveAcidSystem.cs`](../../../Content.Shared/_WF/Caverns/WFDigestiveAcidSystem.cs)

### Client

- [`Content.Client/_WF/Caverns/ScalingViewport.Caverns.cs`](../../../Content.Client/_WF/Caverns/ScalingViewport.Caverns.cs)
- [`Content.Client/_WF/Caverns/WFCavernClimbSystem.cs`](../../../Content.Client/_WF/Caverns/WFCavernClimbSystem.cs)
- [`Content.Client/_WF/Caverns/WFCavernShadeVisualsSystem.cs`](../../../Content.Client/_WF/Caverns/WFCavernShadeVisualsSystem.cs)
- [`Content.Client/_WF/Caverns/WFCavernViewSystem.cs`](../../../Content.Client/_WF/Caverns/WFCavernViewSystem.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/Caverns/CavernAmbienceTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Caverns/CavernAmbienceTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Caverns/CavernBiomeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Caverns/CavernBiomeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Caverns/CavernClimbTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Caverns/CavernClimbTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Caverns/CavernCommandTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Caverns/CavernCommandTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Caverns/CavernFallTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Caverns/CavernFallTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Caverns/CavernFixture.cs`](../../../Content.IntegrationTests/Tests/_WF/Caverns/CavernFixture.cs)
- [`Content.IntegrationTests/Tests/_WF/Caverns/CavernGenerationTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Caverns/CavernGenerationTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Caverns/CavernGutTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Caverns/CavernGutTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Caverns/CavernHullTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Caverns/CavernHullTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Caverns/CavernMouthTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Caverns/CavernMouthTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Caverns/CavernNetworkTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Caverns/CavernNetworkTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Caverns/CavernOrbitalFallTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Caverns/CavernOrbitalFallTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Caverns/CavernPrototypeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Caverns/CavernPrototypeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Caverns/CavernRoofTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Caverns/CavernRoofTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Caverns/CavernViewerEyeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Caverns/CavernViewerEyeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Caverns/CavernViewTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Caverns/CavernViewTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Caverns/CavernWildlifeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Caverns/CavernWildlifeTest.cs)

### Unit tests

- [`Content.Tests/_WF/Caverns/CavernAirTest.cs`](../../../Content.Tests/_WF/Caverns/CavernAirTest.cs)
- [`Content.Tests/_WF/Caverns/CavernPassTest.cs`](../../../Content.Tests/_WF/Caverns/CavernPassTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/Caverns/Biomes/aerumna.yml`](../../../Resources/Prototypes/_WF/Caverns/Biomes/aerumna.yml)
- [`Resources/Prototypes/_WF/Caverns/Biomes/asclepiu.yml`](../../../Resources/Prototypes/_WF/Caverns/Biomes/asclepiu.yml)
- [`Resources/Prototypes/_WF/Caverns/Biomes/carcinoma.yml`](../../../Resources/Prototypes/_WF/Caverns/Biomes/carcinoma.yml)
- [`Resources/Prototypes/_WF/Caverns/Biomes/fervidus.yml`](../../../Resources/Prototypes/_WF/Caverns/Biomes/fervidus.yml)
- [`Resources/Prototypes/_WF/Caverns/Biomes/merak.yml`](../../../Resources/Prototypes/_WF/Caverns/Biomes/merak.yml)
- [`Resources/Prototypes/_WF/Caverns/Biomes/thrascias.yml`](../../../Resources/Prototypes/_WF/Caverns/Biomes/thrascias.yml)
- [`Resources/Prototypes/_WF/Caverns/caverns.yml`](../../../Resources/Prototypes/_WF/Caverns/caverns.yml)
- [`Resources/Prototypes/_WF/Caverns/Entities/decor.yml`](../../../Resources/Prototypes/_WF/Caverns/Entities/decor.yml)
- [`Resources/Prototypes/_WF/Caverns/Entities/fauna.yml`](../../../Resources/Prototypes/_WF/Caverns/Entities/fauna.yml)
- [`Resources/Prototypes/_WF/Caverns/Entities/flora.yml`](../../../Resources/Prototypes/_WF/Caverns/Entities/flora.yml)
- [`Resources/Prototypes/_WF/Caverns/Entities/gut.yml`](../../../Resources/Prototypes/_WF/Caverns/Entities/gut.yml)
- [`Resources/Prototypes/_WF/Caverns/Entities/mouths.yml`](../../../Resources/Prototypes/_WF/Caverns/Entities/mouths.yml)
- [`Resources/Prototypes/_WF/Caverns/levels.yml`](../../../Resources/Prototypes/_WF/Caverns/levels.yml)
- [`Resources/Prototypes/_WF/Caverns/tiles.yml`](../../../Resources/Prototypes/_WF/Caverns/tiles.yml)

### Localization

- [`Resources/Locale/en-US/_WF/Caverns/caverns.ftl`](../../../Resources/Locale/en-US/_WF/Caverns/caverns.ftl)
- [`Resources/Locale/en-US/_WF/Caverns/commands.ftl`](../../../Resources/Locale/en-US/_WF/Caverns/commands.ftl)
- [`Resources/Locale/en-US/_WF/Caverns/entities.ftl`](../../../Resources/Locale/en-US/_WF/Caverns/entities.ftl)

### Textures

- [`Resources/Textures/_WF/Caverns/digestive_acid.rsi/`](../../../Resources/Textures/_WF/Caverns/digestive_acid.rsi/)
- [`Resources/Textures/_WF/Caverns/glow_flora.rsi/`](../../../Resources/Textures/_WF/Caverns/glow_flora.rsi/)
- [`Resources/Textures/_WF/Caverns/Mouths/aerumna_pit.rsi/`](../../../Resources/Textures/_WF/Caverns/Mouths/aerumna_pit.rsi/)
- [`Resources/Textures/_WF/Caverns/Mouths/asclepiu_pit.rsi/`](../../../Resources/Textures/_WF/Caverns/Mouths/asclepiu_pit.rsi/)
- [`Resources/Textures/_WF/Caverns/Mouths/carcinoma_pit.rsi/`](../../../Resources/Textures/_WF/Caverns/Mouths/carcinoma_pit.rsi/)
- [`Resources/Textures/_WF/Caverns/Mouths/fervidus_pit.rsi/`](../../../Resources/Textures/_WF/Caverns/Mouths/fervidus_pit.rsi/)
- [`Resources/Textures/_WF/Caverns/Mouths/merak_pit.rsi/`](../../../Resources/Textures/_WF/Caverns/Mouths/merak_pit.rsi/)
- [`Resources/Textures/_WF/Caverns/Mouths/thrascias_pit.rsi/`](../../../Resources/Textures/_WF/Caverns/Mouths/thrascias_pit.rsi/)

### Tools

- [`Tools/_WF/Caverns/gen_flavour.py`](../../../Tools/_WF/Caverns/gen_flavour.py)
- [`Tools/_WF/Caverns/gen_pits.py`](../../../Tools/_WF/Caverns/gen_pits.py)

### Docs

- [`Docs/_WF/Caverns/CAVERNS_DESIGN.md`](../../../Docs/_WF/Caverns/CAVERNS_DESIGN.md)

## Non-modular edits

- [`Content.Client/_CE/ZLevels/Core/ScalingViewport.CEZLevels.cs`](../../../Content.Client/_CE/ZLevels/Core/ScalingViewport.CEZLevels.cs): the cavern shows through the ground's holes.
- [`Content.Client/Parallax/ParallaxOverlay.cs`](../../../Content.Client/Parallax/ParallaxOverlay.cs): no sky in a cavern or through a cavern mouth.
- [`Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.View.cs`](../../_CE/ZLevels/Core/CEZLevelsSystem.View.cs)
  - the level above the next eye, for the ground cap below.
  - under a ground layer, eyes only on its cavern and only while a hole is in view.
  - track the level above the next eye.
- [`Resources/ConfigPresets/Build/development.toml`](../../../Resources/ConfigPresets/Build/development.toml): caverns are on in development builds.

<!-- WOLFGATE-GENERATED END -->
