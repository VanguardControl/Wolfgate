# PlanetPois

Randomized ground points of interest and connecting routes are generated before a planet preloads.
`WFPlanetPoiSystem` draws procedural dungeons or single-grid authored maps from `wfPlanetPoiTable`,
reserves their footprints and connects every non-isolated site before biome terrain loads. Asclepiu
and Merak use paved roads; the other worlds use routes suited to their terrain. A site that finds no
room, or whose entrances can't be reached, is skipped or left off the roads with a warning, and a route
that can't be laid costs the world its roads, never its sites; roads clear whatever the biome had grown
on unpinned ground, as the boundary ring does.

The roads are a network, not a string of paths (`WFPlanetRoadPlanner`). On a world with room for it, an arterial
network goes down first: an inner ring road round the landing zone, an outer ring short of the world's edge and
straight trunks between them (one a side, two on a wide world), each ring an octagon of long straight sides with 45°
corners. Every site then joins the nearest road by a spur that runs straight and turns square. Whatever is in the way
(a site, a cavern mouth, a lake) is routed round; a stretch that can't get round is cut and the rest stays joined up. A
table can turn the arterial network off (`backbone: false`), and a small world gets just the spurs, site to site.

No site, road, footpath, camp, wreck, parked vehicle or roamer is put down in standing water
(`BiomeSystem.WfIsWater`, which reads the biome's recipe before the ground exists). A footprint over 48 tiles square may
cover the edge of a pond, which is drained where it is laid. Every world's water comes as lakes and ponds, not seas.

A network that names a `wfRoadDressing` is furnished as it is painted (`WFPlanetPoiSystem.Dressing`,
laid out by `WFRoadDressingPlanner`): the shoulder becomes kerbed pavement facing the road, a dashed
line runs down the middle of the straight stretches, street lights stand on alternating sides, signals
on the corners where roads meet, and parked and wrecked vehicles, barriers, bins and litter are
scattered along it from the Streets and Vehicles props. Blocking pieces keep off the middle of the road
and apart from one another, so a road is never shut, and nothing is placed inside a site. Asclepiu and
Merak have one each (`road_dressing.yml`).

A network's `tunnels` (`WFPlanetPoiSystem.Tunnels`) wall one or two straight stretches of road in the world's rock and roof
them over: dark, with webs and bones along the edges and lurkers inside (`tunnels.yml`: giant spiders and the world's
risen wreck crew). A tunnel keeps off the sites and clear of bends and junctions, and a footpath meeting the road
inside one leaves a gap in its wall. Every road world has them.

A table's `wrecks` (`WFPlanetPoiSystem.Wrecks`, themes in `wrecks.yml`) crash a few vessels off the roads, torn open
and reclaimed by the world, with its wildlife and what's left of the crew inside: spacers dressed for that world who
rise as zombies when spawned (`WFZombifyOnSpawn`, `wreck_crew.yml`). About half the wrecks still have something in the hold
(`wreck_loot.yml`): a crate with a ship gun flatpack and the hardpoint it mounts on, or a crate of gunnery co-processor
chips (`_WF/GunneryChips`).

A table's `camps` (`WFPlanetPoiSystem.Camps`, `camps.yml`) set a ship down intact and square to the world beside a
road, just off the shoulder of a straight stretch and nose to it, where whoever drives by sees it (out in the open
instead when no stretch has room, or for a camp with `roadside: false`), with an armed crew from an NpcCrew profile
guarding it: two or three on the deck at once, more round the hull behind a
ring of cover once the ground there exists. Dwelling sites (`dwelling: true`, such as the houses) may get a resident. Every world has a Reaver encampment (Ashfall Reaver raiders) and an ADS
control site (ADS automatons); the hold has loot of the same kind as the wrecks'.

Once the ground exists, a table's `vehicles` (`WFPlanetPoiSystem.Vehicles`, `vehicles.yml`) park a few working
trucks, vans and the odd humvee by the roads and out in the open, part-fuelled, with the key on the driver's seat; in
about a third a squatter (`WFStrangerSquatter`) has holed up with the key in his hand, attacks whoever comes in and
drops it when he dies. A table's `roadPatrols` send an NPC-crewed vehicle up and down a stretch of road, there and back
along points traced down the centreline: every world has a Reaver APC (`WFVehicleCrewReaverRoadPatrol`) that leaves
people be unless shot at.

An authored site's `below` is a level under it, such as a basement: it is laid on the world's level under the ground
(the cavern, or the rock a city adds) in the site's frame and turn, clearing the rock there, and the site's floor is
opened over its stairs (`WFPlanetPoiSystem.LayBelow`). With no level under the ground the site goes without it.

An isolated site with `trail: true` stands off the roads and gets a footpath instead (`WFPlanetPoiSystem.Trails`,
routed by `WFPlanetTrailPlanner`): a wandering track of the network's `trailTile`, a tile or two wide, from just
outside its first socket (a door) to the nearest road or earlier footpath. A site more than `trailLength` (240) steps from
any road goes without. `Tools/_WF/PlanetPois/roadside_map.py <map> <name> --trail` turns a mapper's grid into one.

Players discover orbit signals by approaching sites on the ground. Content authors add maps, entry
limits and road sockets through the table prototypes; see [MapperGuide](../../../Docs/_WF/PlanetPois/MapperGuide.md)
for the map contract and content roadmap. The authored maps are the mappers' own: Verda's houses,
gas station and bandit hideout, and Carcinoma's shrine, alongside the expedition dungeons. The gas
station's fuel merchant sells usable fuel items through Traders; it does not refuel docked ships.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/PlanetPois/WFPlanetPoiComponent.cs`](WFPlanetPoiComponent.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiPlacementPlanner.cs`](WFPlanetPoiPlacementPlanner.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiSystem.Camps.cs`](WFPlanetPoiSystem.Camps.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiSystem.cs`](WFPlanetPoiSystem.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiSystem.Dressing.cs`](WFPlanetPoiSystem.Dressing.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiSystem.Network.cs`](WFPlanetPoiSystem.Network.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiSystem.Roamers.cs`](WFPlanetPoiSystem.Roamers.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiSystem.Rotation.cs`](WFPlanetPoiSystem.Rotation.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiSystem.Trails.cs`](WFPlanetPoiSystem.Trails.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiSystem.Tunnels.cs`](WFPlanetPoiSystem.Tunnels.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiSystem.Vehicles.cs`](WFPlanetPoiSystem.Vehicles.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiSystem.Wrecks.cs`](WFPlanetPoiSystem.Wrecks.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetRoadPlanner.cs`](WFPlanetRoadPlanner.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetTrailPlanner.cs`](WFPlanetTrailPlanner.cs)
- [`Content.Server/_WF/PlanetPois/WFRoadDressingPlanner.cs`](WFRoadDressingPlanner.cs)
- [`Content.Server/_WF/PlanetPois/WFZombifyOnSpawnSystem.cs`](WFZombifyOnSpawnSystem.cs)

### Shared

- [`Content.Shared/_WF/PlanetPois/WFPlanetCampPrototype.cs`](../../../Content.Shared/_WF/PlanetPois/WFPlanetCampPrototype.cs)
- [`Content.Shared/_WF/PlanetPois/WFPlanetPoiTablePrototype.cs`](../../../Content.Shared/_WF/PlanetPois/WFPlanetPoiTablePrototype.cs)
- [`Content.Shared/_WF/PlanetPois/WFRoadDressingPrototype.cs`](../../../Content.Shared/_WF/PlanetPois/WFRoadDressingPrototype.cs)
- [`Content.Shared/_WF/PlanetPois/WFWreckThemePrototype.cs`](../../../Content.Shared/_WF/PlanetPois/WFWreckThemePrototype.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetCampTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetCampTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiContentTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiContentTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiLegacyNetworkTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiLegacyNetworkTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiNetworkTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiNetworkTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiPlacementPlannerTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiPlacementPlannerTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiProceduralAccessTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiProceduralAccessTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiRotationTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiRotationTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetRoadDressingPlannerTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetRoadDressingPlannerTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetRoadDressingTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetRoadDressingTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetRoadPlannerTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetRoadPlannerTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetTrailPlannerTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetTrailPlannerTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetWreckTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetWreckTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/PlanetPois/camps.yml`](../../../Resources/Prototypes/_WF/PlanetPois/camps.yml)
- [`Resources/Prototypes/_WF/PlanetPois/dungeons.yml`](../../../Resources/Prototypes/_WF/PlanetPois/dungeons.yml)
- [`Resources/Prototypes/_WF/PlanetPois/entities.yml`](../../../Resources/Prototypes/_WF/PlanetPois/entities.yml)
- [`Resources/Prototypes/_WF/PlanetPois/road_dressing.yml`](../../../Resources/Prototypes/_WF/PlanetPois/road_dressing.yml)
- [`Resources/Prototypes/_WF/PlanetPois/road_props.yml`](../../../Resources/Prototypes/_WF/PlanetPois/road_props.yml)
- [`Resources/Prototypes/_WF/PlanetPois/tables.yml`](../../../Resources/Prototypes/_WF/PlanetPois/tables.yml)
- [`Resources/Prototypes/_WF/PlanetPois/traders.yml`](../../../Resources/Prototypes/_WF/PlanetPois/traders.yml)
- [`Resources/Prototypes/_WF/PlanetPois/tunnels.yml`](../../../Resources/Prototypes/_WF/PlanetPois/tunnels.yml)
- [`Resources/Prototypes/_WF/PlanetPois/vehicles.yml`](../../../Resources/Prototypes/_WF/PlanetPois/vehicles.yml)
- [`Resources/Prototypes/_WF/PlanetPois/wreck_crew.yml`](../../../Resources/Prototypes/_WF/PlanetPois/wreck_crew.yml)
- [`Resources/Prototypes/_WF/PlanetPois/wreck_loot.yml`](../../../Resources/Prototypes/_WF/PlanetPois/wreck_loot.yml)
- [`Resources/Prototypes/_WF/PlanetPois/wrecks.yml`](../../../Resources/Prototypes/_WF/PlanetPois/wrecks.yml)

### Localization

- [`Resources/Locale/en-US/_WF/PlanetPois/pois.ftl`](../../../Resources/Locale/en-US/_WF/PlanetPois/pois.ftl)
- [`Resources/Locale/en-US/_WF/PlanetPois/settlements.ftl`](../../../Resources/Locale/en-US/_WF/PlanetPois/settlements.ftl)

### Maps

- [`Resources/Maps/_WF/PlanetPois/abandoned_bandit_hideout.yml`](../../../Resources/Maps/_WF/PlanetPois/abandoned_bandit_hideout.yml)
- [`Resources/Maps/_WF/PlanetPois/atalis_house.yml`](../../../Resources/Maps/_WF/PlanetPois/atalis_house.yml)
- [`Resources/Maps/_WF/PlanetPois/atalis_house_below.yml`](../../../Resources/Maps/_WF/PlanetPois/atalis_house_below.yml)
- [`Resources/Maps/_WF/PlanetPois/carcinoma_fountain_of_life.yml`](../../../Resources/Maps/_WF/PlanetPois/carcinoma_fountain_of_life.yml)
- [`Resources/Maps/_WF/PlanetPois/planetary_vault.yml`](../../../Resources/Maps/_WF/PlanetPois/planetary_vault.yml)
- [`Resources/Maps/_WF/PlanetPois/Tests/through_service.yml`](../../../Resources/Maps/_WF/PlanetPois/Tests/through_service.yml)
- [`Resources/Maps/_WF/PlanetPois/verda_corner_house.yml`](../../../Resources/Maps/_WF/PlanetPois/verda_corner_house.yml)
- [`Resources/Maps/_WF/PlanetPois/verda_gas_station.yml`](../../../Resources/Maps/_WF/PlanetPois/verda_gas_station.yml)
- [`Resources/Maps/_WF/PlanetPois/verda_house_1.yml`](../../../Resources/Maps/_WF/PlanetPois/verda_house_1.yml)
- [`Resources/Maps/_WF/PlanetPois/verda_small_house.yml`](../../../Resources/Maps/_WF/PlanetPois/verda_small_house.yml)

### Tools

- [`Tools/_WF/PlanetPois/map_author.py`](../../../Tools/_WF/PlanetPois/map_author.py)
- [`Tools/_WF/PlanetPois/roadside_map.py`](../../../Tools/_WF/PlanetPois/roadside_map.py)

### Docs

- [`Docs/_WF/PlanetPois/MapperGuide.md`](../../../Docs/_WF/PlanetPois/MapperGuide.md)

## Non-modular edits

- [`Content.Server/Procedural/DungeonJob/DungeonJob.PostGenAutoCabling.cs`](../../Procedural/DungeonJob/DungeonJob.PostGenAutoCabling.cs): a restart after the frontier empties needs a cost too, or the next step throws.

<!-- WOLFGATE-GENERATED END -->
