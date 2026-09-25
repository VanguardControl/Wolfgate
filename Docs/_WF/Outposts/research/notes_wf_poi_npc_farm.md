# Outposts groundwork on main: POIs, NPCs/raids, defence, trade, farming, gizmos

Source: read-only checkout `C:/Users/jzo12/Documents/GitHub/Wolfgate/.claude/worktrees/ship-broadcast-pa-system-04bd45`
(main at `64f27cb43f`). Paths below are relative to that root. The planet-cracker work (`Planets-and-cracking`,
PR #40) is on its own branch and **not** on main; everything here is what main has today.

## 1. POIs and sector generation

### PointOfInterestPrototype (`Content.Server/_NF/GameRule/PointOfInterestPrototype.cs`)
`type: pointOfInterest`, inheriting (`parent`, `abstract`). Fields:
- `name` (required), `gridPath` (required, `ResPath`), `nameWarp` (default true), `hideWarp` (false)
- `minimumDistance` 5000 / `maximumDistance` 10000 (metres from map origin), `posX`/`posY` offsets
- `addComponents` (`ComponentRegistry`, `AlwaysPushInheritance`) added to the loaded grid
- `spawnGamePreset` (list of `GamePresetPrototype`; empty = all presets)
- `spawnGroup` (default `"Optional"`): `CargoDepot`, `MarketStation`, `Required`, `Optional`, or any custom string
- `spawnChance` (0-1), used only for custom groups

A `gameMap` prototype with the **same ID** is optional; if present, the grid is made a station
(`StationSystem.InitializeNewStation(stationProto.Stations[proto.ID], ...)`), which is how depots get
`TradeCrateDestination`, records, bank etc.

### PointOfInterestSystem (`Content.Server/_NF/GameRule/PointOfInterestSystem.cs`, 314 lines)
Called once from `NFAdventureRuleSystem.Started` (`Content.Server/_NF/GameRule/NfAdventureRuleSystem.cs` line 310;
skipped when `HyperwarRuleSystem.HyperwarActive`). It enumerates every `PointOfInterestPrototype`, filters by preset,
buckets by `spawnGroup`, then:
- `GenerateDepots`: `nf14.worldgen.cargo_depots` (default **4**) depots on a ring, evenly rotated; names get " A".."Z";
  sets `TradeCrateDestinationComponent.DestinationProto = "Cargo{A..}"`.
- `GenerateMarkets`: `nf14.worldgen.market_stations` (default 1). No active `MarketStation` POI exists on main
  (`trade.yml`/`trademall.yml` are commented out).
- `GenerateRequireds`: every Required POI.
- `GenerateOptionals`: shuffled, `nf14.worldgen.optional_stations` (default **6**).
- `GenerateUniques`: per custom group, shuffle, roll `spawnChance`, first success wins, one per group.
- `TrySpawnPoiGrid`: `MapLoaderSystem.TryLoadGrid(mapId, gridPath, offset, rot: random)`, set name, `AddComponents`,
  `StationRenameWarpsSystems.SyncWarpPointsToStation/Grids` (warp names follow the grid).
- Placement: `GetRandomPOICoord` random vector in [min,max], retried `nf14.worldgen.poi_placement_retries` (10) times to
  stay `nf14.worldgen.min_poi_distance` (400 m) from earlier POIs. Always on `GameTicker.DefaultMap` (one sector map;
  no per-planet-map support).
- All in `Content.Shared/_NF/CCVar/NFCCVars.cs` lines ~77-107.

Base prototypes (`Resources/Prototypes/_NF/PointsOfInterest/base.yml`):
- `BaseMobilePOI` (abstract): 4000-8000 m, group Optional, `IFF color #ffa600 readOnly`, and a **hard-coded
  `spawnGamePreset` list** (NFAdventure, NFPirate, MonoStandard, MonoRogueTSF, MonoRogueUSSP, MonoTSFUSSP, MonoADS,
  MonoChimera, MonoMixed, MonoAllAtOnce, MonoChimeraTsf, MonoChimeraPdv). Wolfgate's `MonoXeno` preset
  (`Resources/Prototypes/_WF/Xenoborgs/game_presets.yml`) is not in it, so no POIs spawn in MonoXeno.
- `BasePOI` adds `ForceAnchor`; `BaseRepairablePOI` adds `InitRepairSnapshot` (Mono ship repair baseline).
- Depot example (`depots.yml`) adds `ProtectedGrid { preventArtifactTriggers }`, `SolarPoweredGrid
  { trackOnInit, doNotCull }`, `WorldGenDistanceCarver` (clears asteroids near the POI).

### Count and map locations
- 35 uncommented `- type: pointOfInterest` entries (3 abstract, so **32 concrete**), 10 commented out.
  Prototypes: `Resources/Prototypes/_NF/PointsOfInterest/*.yml` and `Resources/Prototypes/_Mono/PointsOfInterest/**`.
- Grids: `Resources/Maps/_NF/POI/` (19 files), `Resources/Maps/_Mono/POI/` (18) + `Resources/Maps/_Mono/POI/Dungeons/`
  (6), `Resources/Maps/_Mono/Supercapitals/`. All are in `Maps`, which client builds exclude (a previewer would need
  `SharedMaps`).
- Active groups: CargoDepot (`CargoDepot`, `CargoDepotAlt`, 8000-10000 m); Required (Edison, BeaconClusterA
  `posX 5056,posY 4480`, BeaconWilds, Mining/derelictdrillsite, Zvezda, Hokkaido, ZetaNode, Sevastopol, USSPCamelot,
  TSFMCIndustry, TSFMCHalcyon, HeliosFortress, plus hyperwar-only Chengdu, Jupiter_HW, *Hyperwar variants); Optional
  (derelict dungeons Jupiter 15-17.5 km, Lancelot, Polaris, AutomatedTanker, Zenith 20-25 km; CaseysCasino,
  Omnichurch); unique groups RestStop (Bahama, Tinnia), Scrapyard (Grifty, McHobo), Arena (ThePit), ScienceLab
  (AnomalousLab), Christmas (NorthPole, `spawnChance: 0`).

### Grid protection / anchoring components
- `ForceAnchorComponent` (`Content.Server/_NF/Shuttles/Components/ForceAnchorComponent.cs`): anchors the grid on
  MapInit and blocks FTL (`ForceAnchorSystem`); also treated as collision-protected in `ShuttleSystem.Impact.cs:175`.
- `ProtectedGridComponent` (`Content.Shared/Tiles/ProtectedGridComponent.cs`): `PreventFloorRemoval`,
  `PreventFloorPlacement`, `PreventRCDUse`, `PreventEmpEvents`, `PreventExplosions`, `PreventArtifactTriggers`,
  `KillHostileMobs` (+ `HostileMobKillSound`). `HostileNPCDeletionSystem` (`Content.Server/_NF/NPC/Systems/`)
  "Destroys enemy NPCs on protected grids".
- Mono `GridClaimer`/`ClaimableGrid` (`Content.Server/_Mono/GridClaimer/`) keep a grid from despawning.
  `GridCleanupSystem` (`Content.Server/_Mono/Cleanup/`) deletes small/cheap grids far from players
  (`mono.cleanup.grid.distance` 628, `max_value` 30000, duration 1800 s) but skips map entities and anything
  parented to a planet map (lines 61-62). `MobCleanupSystem` deletes mobs > `mono.cleanup.mob.distance` 1280 m from
  players.

### Star system and planets (visual only on main)
- `_FarHorizons` star system: `StarSystemRuleComponent { system }` + `StarSystemRuleSystem`
  (`Content.Server/_FarHorizons/StarSystem/`), rule `StarSystemKyphrus` (`Resources/Prototypes/_FarHorizons/GameRules/
  star_system.yml`) in every Mono preset and MonoXeno. `SystemKyphrus` (`Resources/Prototypes/_FarHorizons/Space/
  systems.yml`): star `StarKyphrus`, planets Fervidus 6200 m, Merak 9400, Asclepiu 12800, Aerumna 17500,
  Thrascias 27500 (distance + angle).
- `StarSystemMapSystem.SpawnEntities` spawns `StarEntity`/`PlanetEntity` on the sector map with a global PVS
  override. `PlanetEntity` (`Resources/Prototypes/_FarHorizons/Entities/Objects/StarSystem/star_system.yml`) is just
  `WarpPoint`, `FTLSmashImmune`, `FTLBeacon`, `IFF #7FB2FF`; drawn by client shader overlays. No landable surface.
- Landable planet plumbing that exists: `BiomeSystem.EnsurePlanet` (`Content.Server/Parallax/BiomeSystem.PlanetSetup.cs`,
  Mono-refactored; adds `PlanetMapComponent`, gravity, `MapLightComponent`, `RoofComponent`, `LightCycleComponent`,
  `SunShadowComponent`/`SunShadowCycleComponent`, O2/N2 atmosphere) and DeltaV `PlanetSystem.SpawnPlanet/LoadPlanet`
  (`Content.Server/_DV/Planet/PlanetSystem.cs`, uses `PlanetPrototype` `type: planet`; `LoadPlanet` reserves tiles
  under a loaded grid). One planet proto: `DesertWorld` (`Resources/Prototypes/_Mono/Planets/permanent_planet.yml`,
  biome `MonoDesertPermanentPlanet`, `FTLDestination`, 305.75 K); its `StationPlanetSpawner` use is commented out in
  `_Mono/Outpost/caelestinus.yml` and `_NF/Maps/Outpost/frontier.yml` (only `_NF/Maps/debug.yml` uses it).
- `PlanetMapComponent` (`Content.Server/_Mono/Planets/`) only has `Parallax = "bedrock"`.
- Biome chunks load only around **player-attached entities and their view subscriptions** (ghosts need the
  `AllowBiomeLoading` tag) - `BiomeSystem.PlayerTracker.cs`; load range follows `NetMaxUpdateRange`.

### Procedural structures
- Dungeon configs (`type: dungeonConfig`): upstream `Resources/Prototypes/Procedural/dungeon_configs.yml`
  (PlanetBase, Experiment, Haunted, LavaBrig, Mineshaft, SnowyLabs), NF `_NF/Procedural/dungeon_configs.yml`
  (NFPlanetBase, NFExperiment, NFHaunted, NFLavaBrig, NFMineshaft, NFSnowyLabs, NFCaveFactory, NFMedSci,
  NFFactoryDorms, NFLavaMercenary, NFVirologyLab, NFSalvageOutpost), plus asteroid/VGRoid families (vgroid.yml and
  NF basalt/cave/chromite/scrap/snow variants) and Magnet asteroid/debris. Room maps in `Resources/Maps/_NF/Dungeon/`.
- Generation: `DungeonSystem.GenerateDungeonAsync(config, id, mapUid, grid, origin, seed)`
  (`Content.Server/Procedural/`).
- Salvage expeditions: `SpawnSalvageMissionJob` (`Content.Server/Salvage/SpawnSalvageMissionJob.cs`) builds a fresh
  map with biome, gravity, `SalvageAirMod` atmosphere, weather, light, `SalvageExpeditionComponent`, then a dungeon
  offset 8-20 tiles from origin. Factions in `Resources/Prototypes/Procedural/salvage_factions.yml`: Xenos, Carps,
  Syndicate, Cultists, Flesh, Argocytes, Dinosaurs, Mercenaries, Explorers, Silicons, Punks. Cvars:
  `nf14.salvage.expedition_max_active` 15, cooldown 300 s, failed cooldown 450 s.
- `BluespaceErrorRule` (`Content.Server/_NF/StationEvents/Events/BluespaceErrorRule.cs`) spawns grid groups
  (`BluespaceGridSpawnGroup`, paths, min/max distance) or **dungeon groups** (`BluespaceDungeonSpawnGroup`) on a
  scratch map, then FTLs/places them at a random point on the default map; `LinkedLifecycleGridParent` cleans up.

## 2. NPCs, factions and raids

### Factions (`type: npcFaction`)
Files: `Resources/Prototypes/ai_factions.yml`, `_NF/`, `_Mono/`, `_HL/ai_factions.yml`. IDs: AberrantFleshExpeditionNF,
AllHostile, ArtifactConstruct, BloodCultNF, CC, Cat, Chicken, Chimera, ContrabandClothing, ContrabandDetection,
DinosaursNF, Dragon, Dwarf, ExplorersExpeditionNF, Felinid, Goblin, Harpy, HostileUniversally, MD, MMC, MercenariesExpeditionNF,
Monkey, Monolithic, Mothership, Mouse, NanoTrasen, Passive, PetsNT, Pibble, PirateNF, Revolutionary, Shadekin,
SiliconsExpeditionNF, SimpleHostile, SimpleNeutral, StreetGangNF, Syndicate, SyndicateNF, TSFMC, TSFMCTurret, USSP,
USSPTurret, VG, VGTurret, WizFedFaction, Wizard, Xeno, Xenoborg, XenoborgTurret, Zombie.
`NpcFactionPrototype` (`Content.Shared/NPC/Prototypes/NpcFactionPrototype.cs`) has Mono additions `neutral`,
`defaultHostile` (hostile to everything not listed), `defaultHostileIncluded`. No per-player/per-outpost factions;
`FactionExceptionComponent` handles per-entity exceptions.

### Mob AI
- HTN on upstream NPC systems (`Content.Server/NPC/Systems/`: combat melee/ranged, steering, perception,
  retaliation, `NPCImprintingOnSpawnBehaviourSystem`). NF hostile mobs mostly use `SimpleHostileCompound` (7),
  `SimpleRangedHostileCompound` (4), `SimpleHumanoidHostileCompound` (3) in `Resources/Prototypes/_NF/Entities/Mobs/NPCs/`
  (punkganger, mercenaries, syndicate, rogue_ai, expeditions_* etc.).
- Obstacles: `NPCSteeringSystem.Obstacles.cs` supports `PathFlags.Prying` (pry airlocks), `Smashing` (break walls),
  `Climbing`, `Interact`, `Access` (`Content.Server/NPC/Pathfinding/PathFlags.cs`). So raiders can breach bases.
- Mono ship AI (`Content.Server/_Mono/NPC/HTN/`): `ShipSteererComponent` (evasion, `AvoidCollisions`,
  `GridSearchBuffer` 312), `ShipTargetingComponent`, `ShipNpcTargetComponent`, operators `ShipMoveToOperator`,
  `ShipFireGunsOperator`. Compounds in `Resources/Prototypes/_Mono/NPCs/Shuttle/shuttle.yml` + `specific.yml`
  (Rammer*, Approacher*, Attacker*, Shooter, Broadside, Autopilot, Drone* per drone type). Pilot entities
  `NpcStationAi*`/`NpcDroneAi*` in `Resources/Prototypes/_Mono/Entities/Mobs/NPCs/ai.yml`; `DroneController`
  (`Resources/Prototypes/_Crescent/NPCs/Shuttle/control.yml`) with `WorldLoader radius 256` and
  `sleepPlayerCheckRangeOverride: 100000`.

### Existing raid-like events
- Mono AI ship schedulers (`Resources/Prototypes/_Mono/GameRules/base.yml`): `MonoAISTCShuttleSpawnerScheduler`
  (first at 1 h, then 90-120 min; table `DamagedAIShipsTable`: Zenith, Wyrm, Nebula), `...Tier2` (from 3 h, every
  60 min), `MonoChimeraSTCShuttleSpawnerScheduler` (first 10 min, every 15-30 min), Asakim (commented out in presets).
  Wolfgate `MonoXenoborgShuttleSpawnerScheduler` (`_WF/Xenoborgs/GameRules/`). Rules derive `BaseRandomShuttleRule`
  (`StationEvent`, `RuleGrids`, `LoadMapRule gridPath`), grids in `Resources/Maps/_Mono/ShuttleEvent/` (21 files +
  Scrap/, Xeno/). `LoadMapRuleSystem` loads them on a new map; I did not trace how they then reach the sector.
- Mono world drones: `GridSpawnerComponent` (`Content.Server/_Mono/Spawning/`; `path`, `nameDataset`,
  `addComponents`) loads a grid at the spawner's position on MapInit. Used by `SpawnDrone*` / `MonoDroneSpawnerT0..T2*`
  in `Resources/Prototypes/_Mono/Entities/World/Debris/drone.yml`, grids `Resources/SharedMaps/_Mono/Shuttles/World/`
  (18 drones: wasp, crown, gust, piercer, medusa, assembly, bracket, mauler, quake, needle, wedge...).
- NF bluespace events (`Resources/Prototypes/_NF/Events/nf_bluespace_grids_events.yml`): `BluespaceSyndicateFTLInterception`
  (1500-2500 m, duration 1800-2400 s), `BluespacePreFractureFTLInterception`, caches/vaults. `BluespaceBloodMoon`
  exists only commented out (map `/Maps/_NF/Bluespace/bloodmoon.yml`).
- Mono `GridRaiderSystem` (`Content.Server/_Mono/GridRaiderSystem.cs`) is not a raid: it applies NoHack/NoDeconstruct
  to doors/vendors on a grid.
- No NPC raid-on-player-base system, no night/active-state scheduler, no wave spawner.

### Spawners and fauna
- `TimedSpawner` (upstream): e.g. xeno burrower marker `Entities/Markers/Spawners/Conditional/timed.yml`
  (`MobXeno`, chance 0.85, every 30 s, 2-4), NF aberrant flesh (`_NF/Entities/Structures/Specific/aberrant_flesh.yml`,
  every 240 s, 1-3). Fertilized eggs hatch with `TimedSpawner` too.
- `NFSalvageMobRestrictions` (`Content.Server/_NF/Salvage/`) ties a mob to its grid; leaving adds `TimedDespawn`,
  passive damage, `Pacified`.
- Planet fauna: biome layer spawns `MonoPlanetmapFaunaDesert` (`Resources/Prototypes/_Mono/Planets/fauna.yml`):
  MobLizard 0.5, MobSnake 0.25, MobArgocyteSlurva 0.15, MobKangarooPlanet 0.1, MobCrab 0.1, MobArgocyteSwiper 0.055,
  MobArgocyteCrawler 0.025.

### Vehicles
Upstream buckle vehicles only (`Content.Shared/Vehicle/`, `Resources/Prototypes/Entities/Objects/Vehicles/buckleable.yml`):
VehicleJanicart, VehicleSecway, VehicleATV, VehicleSyndicateSegway, VehicleSkeletonMotorcycle, VehicleUnicycle,
VehicleWheelchair; NF `VehicleATVNF` etc. in `_NF/Entities/Objects/Vehicles/vehicles.yml`; `_NF/Vehicle/VehicleHornComponent`.
No NPC driving.

## 3. Defence

### Personal-scale turrets
- Bases: `BaseWeaponTurret` (`Entities/Objects/Weapons/Guns/Turrets/turrets_base.yml`), `BaseWeaponTurretNF`,
  `BaseWeaponTurretBallisticNF`, `BaseWeaponTurretEnergyNF`, `BaseWeaponTurretMagazineFed` (`_NF/.../base_turret.yml`).
- HTN `TurretCompound`/`EnergyTurretCompound` (`Resources/Prototypes/NPCs/root.yml`) use utility query
  `NearbyGunTargets` (`NPCs/utility_queries.yml`: `NearbyHostilesQuery` + `TurretTargetingCon`, LOS). Targeting is
  **by `NpcFactionMember`**, so variants exist per faction: `WeaponTurretCC`, `...TSFMC`, `...USSP`, `...PDV`
  (PirateNF), `...Viper`, `...Xenoborg`, `WeaponTurretLaserTSFMC`, etc. (`_Mono/Entities/Objects/Misc/turrets.yml`).
- Access-based: upstream `DeployableTurret` + `TurretTargetSettings { exemptAccessLevels }`
  (`Content.Shared/Turrets/`) with `DeployableTurretController` wall panels (`Entities/Structures/Wallmounts/
  turret_controls.yml`); Mono `BallisticTurretHeavyAI*` (`_Mono/Entities/Structures/Wallmounts/turret_controls.yml`,
  exempt Security/Borg/BasicSilicon; faction controls `WeaponEnergyTurret*ControlPanel`).
- Portable: `BaseMk290Sentry`/`WeaponTurretMk290` (TSFMCTurret faction, packs via `WeaponTurretMk290PackingGraph`,
  magazine-fed, fireRate 9); `WeaponTurretAsmgt*Packed/Deployed`, `WeaponTurretContrabandDenying*`,
  `WeaponTurretLaserNanoTrasen*`.
### Ship-scale weapons
- `_Crescent` hardpoints (`Content.Shared/_Crescent/Hardpoints/HardpointComponent.cs`: `class` Ballistic/..., `size`).
- Mono space artillery (`Resources/Prototypes/_Mono/Entities/SpaceArtillery/`), fired through gunnery consoles and a
  `FireControlServer` (`Content.Server/_Mono/FireControl/`: `ProcessingPower`, `MaxConsoles`). Point defence is a
  role, not a system: e.g. `WeaponTurretL85Autocannon` "rapid fire point defense"; flare launchers.
- Wolfgate: `WFShipHarpoonTurret` (`_WF/Tether`). The WF carrier consoles live on the unpushed `vesselsreturn`
  branch, not main.
### Shields
- `_Crescent` ship shields (`Content.Shared/_Crescent/ShipShields/ShipShieldEmitterComponent.cs`: `DamageLimit`,
  `HealPerSecond` 250, `MaxDraw` 150000, `BaseDraw` 50000, `DamageOverloadTimePunishment` 30,
  `CollisionResistanceMultiplier`), grid-wide bubble. Generators in `_Mono/Entities/Structures/Machines/
  shield_generator.yml`: `ShieldGeneratorSmall` (65k, 60 kW), `ShieldGenerator` (75k), `ShieldGeneratorMedium`
  (180k, 75 kW), `ShieldGeneratorTSFCapital` (85k), `ShieldGeneratorPOI` (500k, 150 kW, "static, large-scale"),
  `ShieldGeneratorPOIBio*` (150k, "relegated to usage only on stationary outposts").
### Sensors and warning
- No "early warning" machine. `EarlyWarning` exists only as a vessel class enum (`VesselPrototype.cs:201`, "elite+ radar").
- Mono detection: `DetectionSystem.IsGridDetected(grid, byUid)` returns a `DetectionLevel`
  (`Content.Shared/_Mono/Detection/DetectionSystem.cs`, cvars `ThermalDetectionMultiplier`, `VisualDetectionMultiplier`);
  `ThermalSignature`, `MachineThermalSignature { Signature }`, `RadarBlip`, `HitscanRadar`.
- `TargetSeekerAlertComponent` (`Content.Server/_Mono/TargetSeekingAlert/`) plays sounds when a missile seeker
  tracks the grid, with distance thresholds.
- Radar consoles: upstream `RadarConsole`, `ComputerAdvancedRadar` (NF), `ComputerEliteRadar`, `ComputerStationRadar`
  (`_Mono/.../radar_computers.yml`); consoles carry `WorldLoader` (700-1536). Space-scale only.
- `ProximitySensor` is a robotics part (`Entities/Objects/Misc/botparts.yml`); `TriggerOnProximity` exists for mines.
- IFF: `IFFComponent` flags `HideLabel`, `Hide`; POIs set `readOnly` colour.
- Wolfgate `ShipPa` (alert codes, general quarters, speakers) and `CollisionWarning` could carry attack alerts.

## 4. Trade and cargo

- Order flow (`Content.Server/Cargo/Systems/CargoSystem.Orders.cs`): `CargoOrderConsole` needs an owning station and
  an order database; approval requires the **player's** `BankAccountComponent`, withdraws from the player, and pays
  `TaxAccounts` (`ComputerCargoOrders`: Frontier 0.25, Nfsd 0.15, Medical 0.1) to sector accounts via
  `BankSystem.TrySectorDeposit(..., LedgerEntryType.CargoTax)`. `SectorBankAccount` enum: Frontier, Nfsd, Medical,
  Mieyo, BlackMarket.
- Delivery: a `CargoTelepad` linked by device link to the console spawns the console's approved orders after
  `Delay` (part-upgradeable) (`CargoSystem.Telepad.cs`). No shuttle; pallets (`CargoPalletBuy`) only in the old
  `TryFulfillOrder` path.
- Selling: `CargoPalletSell` on grids whose station has `TradeStationComponent` (added by
  `Entities/Stations/base.yml:52`), i.e. depots. The cargo sale console board has `MarketModifier mod: 0` to stop
  ship use.
- Trade crates (`Content.Server/_NF/Cargo/Systems/CargoSystem.TradeCrates.cs`): destination = random depot;
  `ValueAtDestination` vs `ValueElsewhere`, express bonus/penalty, Mono `TradeCrateWildcardDestination
  { ValueMultiplier }`. Mono makes trade goods on lathes (`_Mono/Recipes/Lathes/trade_crates.yml`, e.g.
  `TradeGoodMetals`: Steel 600, Plasteel 200, Plasma 50, Carbon 20; packs `MonoTradeCrates*`).
- NF market (`Content.Server/_NF/Market/`): sold goods become buyable at a `MarketConsole` that spawns crates on a
  `CrateMachine` within 8 tiles, `TransactionCost` 600.
- Vendor markup: `MarketModifierComponent { Mod, Buy }` (`Content.Shared/_NF/Bank/Components/`), e.g. vending x5-x10.
  Mono `DriftingPriceComponent` (only used by bitcoin).
- Air-drop analogues: `BluespaceCargoRule` spawns crates at random open tiles of a station's largest grid with a
  flash effect; NF `DeadDropSystem` FTLs a small pod grid to coordinates (`FTLToCoordinates`, max 5 pods).
- No "Universal Trade Hub", "trade hub" or "UTH" anywhere (grep of cs/yml/ftl/xml/md: 0 hits).
- Wolfgate: `_WF/Traders` dialogue NPCs fronting vendors, shipyard, used ships, refuel, parcels; `WFVendingMachineWolfgate`
  sells flatpacks (`Resources/Prototypes/_WF/Traders/Entities/Structures/Machines/vending_machines.yml`).

## 5. Farming and animals

- `AnimalHusbandrySystem` (`Content.Server/Nutrition/EntitySystems/AnimalHusbandrySystem.cs`),
  `ReproductiveComponent` (`Content.Shared/Nutrition/AnimalHusbandry/`): breed attempt every 45-60 s, `BreedRange` 3,
  `Capacity` 6 (stops when crowded), `BreedChance` 0.15, `Offspring`, `GestationDuration` 1.5 min,
  `HungerPerBirth` 75, `MakeOffspringInfant`, `PartnerWhitelist`; partner needs `ReproductivePartner`.
- Livestock (`Resources/Prototypes/Entities/Mobs/NPCs/animals.yml`): MobChicken and MobDuckMallard (Reproductive,
  breedChance 0.05, lay `FoodEggChickenFertilized` which hatches via `TimedSpawner`; `EggLayer` -> `FoodEgg`),
  MobCow (Udder: Milk 25 per 30 s), MobGoat (Udder + `Wooly`), MobPig (Reproductive). `Content.Shared/Animals/`
  (`UdderSystem`, `WoolySystem`), `Content.Server/Animals/EggLayerSystem`.
- No taming, leash, herding, pen, feeder or pregnancy systems in any layer (DeltaV/Nyano/Floof/HL grep: none).
  Closest: `NPCImprintingOnSpawnBehaviour` (befriend nearby on spawn), `FollowCompound` HTN (`NPCs/follow.yml`),
  and WF ropes (`RopeSystem`, attach points) which could make a leash.
- Plants: upstream botany, NF `HydroponicsSoilEmpty`/`HydroponicsSoilNutrition` (+ flatpacks, biogen recipe
  `NFBioGenSoil`), Mono planet primitives in `_Mono/Entities/Objects/Specific/Planet/`: `hydroponicsSand` / `Sand`
  ("Somehow works as a growing spot", `SandGraph`), `FloraPilumaPlant` / `FloraOrakimPlant` (destructible, yield
  `FoodPilumaStalk`), `Forge` (rock-built primitive ore refiner, `ForgeGraph`), `DesertStone`.
- Planet ore: `MonoPlanetmapOre{Sand,Snow,Chromite,Basalt,Base}` (`_Mono/Planets/ore.yml`) and
  `MonoPlanetmapOreSandRich` (coal/tin/quartz/silver/gold/plasma/uranium weights).

## 6. Power and utility gizmos

- Solar: NF `NFPowerSolarSystem` (`Content.Server/_NF/Solar/`) tracks per grid via `SolarPoweredGridComponent`
  (`TargetPanelRotation`, `TotalPanelPower`); `NFSolarPanelComponent MaxSupply 1500`, `ObstructedCoverage 0.5`;
  occlusion check 20 m. Panels `SolarPanel`, `SolarPanelPlasma`, `SolarPanelUranium`, `SolarTracker`,
  `ComputerSolarControl`.
- RTG `GeneratorRTG` 40 kW (Mono), flatpacks. PACMAN family: `PortableGeneratorJrPacman/Pacman/SuperPacman`
  (+ Flatpack, Shuttle). TEG (`TegCenter`, `TegCirculator` flatpacks). `GeneratorBasic15kW`, wallmount APU.
- Mono static (`_Mono/Entities/Structures/Power/Generators/generators.yml`): `GeneratorCRPinch` 105 kW (50-160 kW,
  FuelGradePlasma, storage 10000), `GeneratorSterling` 40 kW, `GeneratorSterlingPDV`, `*Shuttle` prefilled variants.
- Wind: **none** (no turbine or wind code). Weather: `WeatherComponent` set per map by salvage/Mono weather protos
  (`_Mono/Planets/weather.yml`: MonoLightRain, MonoRain, MonoStorm, MonoSnowLight, MonoSnow ...); no scheduler.
- Storage: `SMESBasic`, `SMESAdvanced` (+ Empty, boards).
- Time: `ClockSystem` station time = `GlobalTimeManager.TimeOffset` (0) + round duration; only `Wristwatch` has
  `Clock`. Day/night: `LightCycleComponent` (`Duration` 30 min default, `Offset`); `SharedLightCycleSystem
  .CalculateLightLevel(comp, time)` gives brightness. No WF timepiece/weather console on main.
- Radio (`Content.Server/Radio/EntitySystems/RadioSystem.cs`): the same-map check is commented out, so radio crosses
  maps, but non-`longRange` channels need a powered `TelecomServer` with that key **on the sender's map**
  (`HasActiveServer(mapId, ...)`) unless the sender is `TelecomExempt` (RadioHandheld, BaseIntercom,
  BaseShuttleIntercom). `maxRange` channels (NF `Traffic` 1500 m) use `TryDistance`, which fails across maps.
  Long-range: CentCom, Syndicate, Handheld, Binary, Freelance, VanguardCommand, Ussp, Viper, Freeport, etc.
- Mining automation: Goob `ItemMiner` (`Content.Shared/_Goobstation/ItemMiner/`: `Proto`, `Interval`, `SpawnChance`,
  `NeedApcPower`) with `PlanetMinerComponent { RequireExpedition, RequireGround }` and `PowerConsumerMiner`.
  `LaserDrill` (50 kW HV, `SpawnLootOres15` every 10 s, `requireExpedition: true`), `StationLaserDrill` (150 kW,
  space). Mono `ShipDrill` (`MachineShipDrill`), NF `GasMiningDrill`. No mining bots/drones as NPCs.
- Ore processing: `OreProcessor`, `OreProcessorIndustrial` (+ flatpacks), Mono `IndustrialFurnaceEconomy` /
  `...Compact` arc furnaces (pack `ArcFurnaceStatic`), industrial presses, precision assemblers, centrifuges
  (`_Mono/Entities/Structures/Machines/lathe.yml`), `MaterialReclaimer`, `Recycler`.
- Silos: upstream `MachineMaterialSilo` with `OreSilo range: 125` (Mono), clients via `OreSiloClient`; Wolfgate
  `FabricationSilo` (parts silo + 1500u chemical silo) in `Content.Server/_WF/Lathe/`.
- Flatpacks: `FlatpackComponent` (`QualityNeeded` Pulsing, `Entity`), `MachineFlatpacker` (`FlatpackCreator`,
  `OreSiloClient range 3`); 209 `*Flatpack` prototypes; `BaseNFFlatpack` price 150.
- Recipe gating: lathes use `staticPacks` / `dynamicPacks` (`latheRecipePack`); dynamic packs unlock through research
  (193 `technology` protos) and NF blueprints (`BlueprintReceiver`, 2 users).
- Other: `_CE/ZLevels` exists on main (vertical layers; relevant to "orbit/atmosphere layer" ideas).

## Implications for Outposts

1. POI spawning is sector-map-only and hard-wired to `GameTicker.DefaultMap`; planet POIs need a new placer that
   takes a planet map and a bounded area, but can reuse `PointOfInterestPrototype` fields and `TrySpawnPoiGrid`.
2. Put Outpost POI protos on their own base, not `BaseMobilePOI`, whose preset list already misses `MonoXeno`.
3. Don't give claimable POIs `ForceAnchor` + `ProtectedGrid { KillHostileMobs }` if raids must reach them; do reuse
   `ForceAnchor` for "anchored until the console dies".
4. Planet maps are safe from Mono grid cleanup, but `MobCleanupSystem` (1280 m) and player-only biome chunk loading
   mean raid mobs must spawn near players, or the planner must pre-load chunks.
5. Raiders: existing HTN humanoids with `Prying`/`Smashing` flags can breach; nothing picks a base as a target or
   schedules waves, so an Outposts raid director (night via `LightCycle`) is new work.
6. Turret friend/foe is faction-based; outposts need either a per-outpost faction or `TurretTargetSettings`
   access exemption (already works with deployable turrets and outpost access).
7. Early Warning can build on `DetectionSystem.IsGridDetected` for shuttle raids and a simple range query for
   ground raids, announcing through WF `ShipPa`.
8. The trade panel can reuse `CargoOrderConsole` pricing and bank withdrawal, but delivery must be new (telepad or
   `BluespaceCargoRule`-style spawn); "UTH" is a new entity.
9. Farming has breeding, eggs, milk and wool; taming, pens, leashes (WF ropes are a start) and planet-native
   livestock are new.
10. Wind turbine, weather/time console and mining bots don't exist; `ItemMiner` + `PlanetMiner` (set
   `requireExpedition: false`) is the quickest "planet drill" gizmo.
11. Comms on a planet map need a local telecom server or long-range/exempt radios.
