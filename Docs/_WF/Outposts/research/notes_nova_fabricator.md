# Nova Sector: colony fabricator and colony-adjacent modules

Source: `C:/Users/jzo12/Documents/GitHub/NovaSector` at `7499f47bfc6` (2026-07-30). Paths below are relative to
that root; `CF/` = `modular_nova/modules/colony_fabricator/code/`.

Units: `SHEET_MATERIAL_AMOUNT` = 100 units = 1 sheet, `HALF_SHEET` = 0.5 sheet, `SMALL_MATERIAL_AMOUNT` = 10 units
= 0.1 sheet. Costs below are in **sheets**. `CARGO_CRATE_VALUE` (CCV) = 200 credits (`code/__DEFINES/cargo.dm`).
BYOND time is deciseconds (ds).

## 1. The machine: `/obj/machinery/rnd/production/colony_lathe` (`CF/colony_fabricator.dm`)

- Name "rapid construction fabricator"; lore: Akhter Company Frontier Equipment (`COMPANY_FRONTIER` manufacturer
  examine). It is an ordinary tg lathe (`/obj/machinery/rnd/production`) with these overrides:
  - `circuit = null` and `tool_blocker` for screwdriver and crowbar: it cannot be opened, deconstructed or upgraded.
  - `allowed_buildtypes = COLONY_FABRICATOR` (bit `1<<11`, `code/__DEFINES/machines.dm:79`, comment "Can be made by
    the orderable colony fabricator").
  - `speedup_disabled = TRUE`: opts out of Nova's global "faster lathes" edit (`build_time *= 0.1` for every other
    lathe, `code/modules/research/machinery/_production.dm:347`). The colony fab is roughly 10x slower than a station
    lathe for the same design.
  - `build_efficiency()` returns 1: no material discount, ever.
  - Own private research: `handle_network()` creates `new /datum/techweb/colony_fabricator` per machine; it is never
    linked to station R&D.
  - `update_designs()` rewritten to list every researched design whose `build_type & COLONY_FABRICATOR`, and to
    `say("Received N new designs")` with a beep when the count grows.
  - Visuals: deploy flick (`colony_lathe_deploy`) when not mapload, working icon + light while printing,
    `colony_fabricator_running` looping sound (`CF/looping_sounds.dm`), `colony_lathe_finish_print` flick.
- **Feeding (materials).** Upstream `/datum/remote_materials` (`code/datums/materials/material_container/remote_materials.dm`):
  - Built mid-round (not mapload) it gets a **local** material container: accepts any `/obj/item/stack` whose
    materials are `MATERIAL_SILO_STORED` (iron, glass, silver, gold, diamond, plasma, uranium, titanium, bluespace,
    plastic, bananium...). Insert by clicking stacks on it.
  - Local capacity is `local_size = INFINITY`. `RefreshParts()` (which would cap it at 37.5 sheets x bin tier) only
    runs from `circuit.apply_default_parts`, and the colony fab has no circuit, so the cap is never applied
    (inferred from the code path, not tested in game).
  - Multitool with an ore silo in the buffer links it to that silo (same-z check `check_z_level`); local mats are
    moved into the silo. A mapped fab on a station z-level auto-links to `GLOB.ore_silo_default` at roundstart.
  - Plastic wall panels are a stack with 0.5 plastic + 0.5 glass each, so they can be fed back in. Ore stacks carry
    1 sheet of material per ore (`code/modules/mining/ores_coins.dm`), and nothing in the container rejects them, so
    raw ore probably inserts at 1:1 (not verified). The arc furnace exists to beat that at 1.5:1.
  - `/datum/component/payment` with price 0: printing is free of credits.
- **Power.** `active_power_usage = 0.05 * STANDARD_CELL_RATE` = 500 W "per full stack of materials spent"; energy per
  item = (total material units / 5000) x 500 J, insertion costs 40% of that. Trivial amounts, but `do_make_item`
  stops with "power failure" if `!is_operational` and "no APC in area" if the area has none. **It needs an APC.**
- **Print time.** `build_time_per_item = (construction_time * lathe_time_factor * efficiency_coeff) ** 0.8` in ds;
  `efficiency_coeff` stays at its default 1 (again no RefreshParts). Design default `construction_time` = 3.2 s.
  Stacks print the whole requested amount (1-50 per order) in **one** cycle; non-stacks take one cycle per item.

  | Listed `construction_time` | 0.5 s | 1 s | 3.2 s (default) | 4 s | 5 s | 7 s | 10 s | 15 s | 20 s | 30 s | 40 s | 1 min | 2 min |
  |---|---|---|---|---|---|---|---|---|---|---|---|---|---|
  | Actual per item | 0.4 s | 0.6 s | 1.6 s | 1.9 s | 2.3 s | 3.0 s | 4.0 s | 5.5 s | 6.9 s | 9.6 s | 12.1 s | 16.7 s | 29.1 s |

  Output drops on its own tile, or on a tile you set as `drop_direction` (upstream lathe feature).
- **Carrying it.** `/obj/item/flatpacked_machine` (base type of every colony flatpack): `WEIGHT_CLASS_BULKY`,
  `custom_materials` = 10 iron, 7.5 glass, 2.5 titanium, 0.5 gold, 0.5 silver; `deploy_time = 4 SECONDS`.
  Upstream `/datum/component/deployable` (`code/datums/components/deployable.dm`): use in hand, deploys on the tile
  you face after a `do_after`, refuses a blocked turf, sets the machine's dir to yours, deletes the item.
- **Repacking.** `/datum/element/repackable` (`CF/repacking_element.dm`) on the fab (and every colony machine):
  right-click with an empty hand shows "Repack"; after a `do_after` it spawns the packed item and calls
  `deconstruct(TRUE)`. Note the element ignores its `repacking_time` argument and always waits **3 s**.
- Borg apparatus `sheet_manipulator` and `circuit` can carry `/obj/item/flatpacked_machine`.
- **How it is obtained:** (a) Colonization Starter Kit (2200 cr, below); (b) company import "rapid construction
  fabricator" (6 CCV = 1200 cr, discountable); (c) printed by another colony fab (`flatpack_colony_fab`, 2 min);
  (d) mapped: Port Tarkon (`_maps/RandomRuins/SpaceRuins/nova/port_tarkon.dmm`), Interdyne lavaland base
  (`lavaland_surface_interdyne_base1.dmm`, with 10 packed RTGs), police random ship.

## 2. The design flag system

- One build-type bit, `COLONY_FABRICATOR`. Designs join the fab in two ways:
  1. **Native designs**: `build_type = COLONY_FABRICATOR` only, listed in four hidden techweb nodes
     (`TECHWEB_NODE_COLONY_STRUCTURES/_APPLIANCES/_FLATPACKS/_TOOLS`, `code/__DEFINES/~nova_defines/techweb_nodes.dm`)
     with `hidden = TRUE`, `show_on_wiki = FALSE`, `starting_node = TRUE`, cost `50000000000000` "God save you".
     They are researched everywhere but only a colony fab can print them.
  2. **Flag additions**: `CF/design_datums/fabricator_flag_additions/*.dm` override `New()` on ~126 upstream designs
     with `build_type |= COLONY_FABRICATOR`. A few Nova designs set it directly (vox gas filter, LRM board, airbag,
     window polarizer, prescription engi goggles).
- `/datum/techweb/colony_fabricator/New()` (`modular_nova/master_files/code/modules/research/techweb/techweb_types.dm`)
  loops all `SSresearch.techweb_designs` and `add_design_by_id` for every design with the bit. So **all** flagged
  designs, including normally research-locked ones (RPD, RCD ammo, super cells, satchel of holding, shuttle boards),
  are available from the first minute, with no research.
- UI categories come from each design's `category` (`RND_CATEGORY_INITIAL` + e.g. `"/Autofab Structures"`; the
  comment says the "A" is only there to sort it first).

## 3. Native designs (`CF/design_datums/`)

### Construction, "Autofab Structures" (`construction.dm`)
| Design id | Output | Cost | Time |
|---|---|---|---|
| `prefab_airlock_kit` | `/obj/item/flatpacked_machine/airlock_kit` | 5 iron, 2 glass | 10 s |
| `prefab_manual_airlock_kit` | `/obj/item/flatpacked_machine/airlock_kit_manual` | 5 iron, 2 glass | 5 s |
| `prefab_shutters_kit` | `/obj/item/flatpacked_machine/shutter_kit` | 5 iron, 2 glass | 10 s |
| `prefab_floor_tile` | `/obj/item/stack/tile/iron/colony` | 0.25 iron | 0.5 s |
| `prefab_cat_floor_tile` | `/obj/item/stack/tile/catwalk_tile/colony_lathe` | 0.25 iron | 0.5 s |
| `colony_fab_plastic_wall_panel` | `/obj/item/stack/sheet/plastic_wall_panel` | 0.5 plastic, 0.5 glass | 1 s |

### Tools (`tools.dm`, `equipment.dm`), default 3.2 s
| Id | Output | Cost |
|---|---|---|
| `colony_power_drive` | `/obj/item/screwdriver/omni_drill` "powered driver" | 1.75 iron, 0.75 silver, 0.5 titanium |
| `colony_crowbar` | plain `/obj/item/crowbar` | 0.05 iron |
| `colony_arc_welder` | `/obj/item/weldingtool/electric/arc_welder` | 0.5 iron, 0.5 glass, 0.75 plasma |
| `colony_compact_drill` | `/obj/item/pickaxe/drill/compact` | 3 iron, 0.5 glass |
| `survival_knife` | `/obj/item/knife/combat/survival` | 6 iron |

### Appliances (`appliances.dm`)
| Id | Output | Cost | Time |
|---|---|---|---|
| `wall_multi_cell_rack` | `/obj/item/wallframe/cell_charger_multi` (30 kW charge rate) | 2 iron, 1 silver | 15 s |
| `portable_lil_pump` | `/obj/machinery/portable_atmospherics/pump` (machine, not a kit) | 7.5 iron, 3 glass | 30 s |
| `portable_scrubbs` | `/obj/machinery/portable_atmospherics/scrubber` | 7.5 iron, 3 glass | 30 s |
| `wall_heater` | `/obj/item/wallframe/wall_heater` (space heater on a wall, 2x heat and efficiency, still cell powered) | 4 iron, 1 silver, 0.1 gold | 15 s |
| `water_synth` | `.../water_synth` plumbing synth, water only | 2.5 iron, 1 glass | 30 s |
| `hydro_synth` | `.../hydro_synth`: E-Z, Left 4 Zed, Robust Harvest, Enduro-Grow, Liquid Earthquake, weedkiller, pestkiller | 2.5 iron, 1 glass | 30 s |
| `frontier_sustenance_dispenser` | `/obj/machinery/chem_dispenser/frontier_appliance`: 16 drinks/powders + nutraslop + enzyme, own high cell, 2 kW recharge, purity 0.5 | 2 iron, 1 glass, 0.5 titanium | 30 s |
| `co2_cracker` | `/obj/machinery/electrolyzer/co2_cracker`: CO2 -> O2 1:1, `working_power` fixed at 2 | 7.5 iron, 3 glass, 0.5 plasma | 30 s |
| `portable_recycler` | `/obj/machinery/colony_recycler`: hand-fed, returns 80% of materials as sheets | 7.5 iron, 3 glass, 0.5 titanium | 30 s |
| `foodricator` | `/obj/item/flatpacked_machine/organics_ration_printer` | 5 iron, 2 glass, 1 silver, 0.5 gold | 30 s |
| `macrowave` | microwave kit | 5 iron, 2 glass, 0.5 silver | 30 s |
| `frontier_range` | oven kit | 7 iron, 3 glass, 0.5 silver | 1 min |
| `tabletop_griddle` | griddle kit (sits on a table) | 7 iron, 3 glass, 0.5 silver | 1 min |

### Flatpacked machines (`flatpack_machines.dm`)
| Id | Deploys | Cost | Time | Key stats |
|---|---|---|---|---|
| `flatpack_colony_fab` | another colony fab | 10 iron, 7.5 glass, 2.5 Ti, 0.5 gold, 0.5 silver | 2 min | self-replication |
| `flatpack_solar_panel` | `/obj/machinery/power/solar/deployable` | 1.5 iron, 2 glass | 4 s | 2.5 kW peak (`SOLAR_GEN_RATE` x tier 1) |
| `..._titaniumglass` | tier 2 | + 1 Ti | 4 s | 5 kW |
| `..._plasmaglass` | tier 3 | + 1 plasma | 4 s | 7.5 kW |
| `..._plastitaniumglass` | tier 4 | + 1 plasma, 1 Ti | 4 s | 10 kW |
| `flatpack_solar_tracker` | `/obj/machinery/power/tracker/deployable` | 2 iron, 1.75 glass | 7 s | still needs a solar control console (board is flag-added) |
| `flatpack_arc_furnace` | `/obj/machinery/arc_furnace` | 7.5 iron, 3 glass | 15 s | 1 ore stack at a time, 1 s per ore, 10 kW while smelting, yields floor(1.5 x ore) sheets, vents hot CO2/N2 (1200-2000 K) |
| `flatpack_station_battery` | `/obj/machinery/power/smes/battery_pack` | 7 iron, 2 glass, 0.5 silver | 20 s | 10 MJ, 400 kW in/out |
| `flatpack_station_battery_large` | `.../battery_pack/large` | 12 iron, 4 glass, 1 gold | 40 s | 100 MJ, 50 kW in/out |
| `flatpack_fuel_generator` | `/obj/machinery/power/port_gen/pacman/solid_fuel` "A.W generator" | 5 iron, 1 glass, 1 Ti, 0.5 gold | 30 s | uranium sheets, 25 max, 2x PACMAN `power_gen` (20 kW per level), must be anchored, emits water vapour + helium at 400 K |
| `flatpack_rtg` | `/obj/machinery/power/rtg/portable` | 5 iron, 5 uranium, 5 plasma, 1 gold | 30 s | 15 kW forever, no fuel, lightly radioactive, 40 integrity, explodes (2 heavy / 4 light) when destroyed |
| `flatpack_thermo` | limited thermomachine | 7.5 iron, 1 glass | 20 s | 273 K to ~473 K, heat capacity 10000 |
| `flatpack_ore_silo` | `/obj/machinery/ore_silo/colony_lathe` | 5 iron, 5 glass | 1 min | shared material pool for linked machines |
| `flatpack_bsc` | `/obj/structure/ore_box/boulder_collector` (ghost_mining) | 5 iron, 3 plasma, 3 Ti | 30 s | auto-collects boulders from a set direction, links to an LRM |
| `flatpack_turbine_team_fortress_two` | `/obj/machinery/power/colony_wind_turbine` | 5 iron, 2 glass, 0.5 gold | 30 s | 2.5 kW, **10 kW while any weather is active** on its z-level/area; needs `area.outdoors` and >= 5 kPa |
| `flatpack_bootleg_teg` | `/obj/machinery/power/stirling_generator` | 15 iron, 5 glass, 10 plasma, 5 Ti, 5 gold | 2 min | up to 150 kW at an 8000 K difference between piped hot gas and ambient air |

### Flag-added upstream designs (names are the `/datum/design` types)
- Computer boards: `solarcontrol`, `atmosalerts`, `powermonitor`, `shuttle/shuttle_docker`, `shuttle/flight_control`.
- Construction: `apc_board`, `airalarm_electronics`, `airlock_board`, `firealarm_electronics`, `firelock_board`,
  `control` (button), `infrared_emitter`, `prox_sensor`, `signaler`, `timer`, `ignition_control`, `light_tube`,
  `light_bulb`, `conveyor_belt`, `conveyor_switch`, `lavarods`, `material/rglass`, and alloys plasteel,
  plasma glass, reinforced plasma glass, titanium glass, plastitanium, plastitanium glass.
- Equipment: `radio_navigation_beacon`, `engine_goggles`, `pneumatic_seal`, welding goggles/helmet, gas filters
  (incl. plasmaman), `plasmarefiller`, engi emergency oxygen, plasmaman belt tank, generic/plasma gas tanks,
  `diagnostic_hud`, `portaseeder`, `oven_tray`, `bowl`, beakers (normal, large, x-large), `bioelec_gen`,
  `aquarium_kit`, `auto_reel`, `fishing_rod_tech`, `fish_analyzer`, `fish_case`, `shuttle_rods`.
- Machine boards: `hydroponics` tray, `cyborgrecharger`, `processor`, `suit_storage_unit`, `reagentgrinder`,
  `fishing_portal_generator`, full gas turbine set (computer, compressor, rotor, stator + the three parts),
  factory machines (`big_manipulator`, `manulathe`, `manucrafter`, `manucrusher`, `manurouter`, `manusorter`,
  `manuunloader`, `manusmelter`), `propulsion_engine` (shuttle engine).
- Stock parts: `water_recycler`, `super_cell`, tier-2 parts (adv capacitor, adv scanning, nano servo, high micro
  laser, adv matter bin), `rped`, `basic_battery`, `super_battery`.
- Tools: atmos and engi holosigns, analyzer, extinguisher, cable coil, decal painter, inducer, multitool, t-ray,
  pipe painter, RWD, bolter wrench, RPD + unwrench upgrade, loaded RTD, RCD ammo, light replacer, mini RLD,
  satchel of holding, mining scanner, flashlight, ducts, plunger, hand labeler, paper roll, spraycan, pickaxe,
  bucket, watering can, mop, broom, tray, cultivator, plant analyzer, shovel, spade, hatchet, secateurs,
  `telesci_gps`, `shuttle_blueprints`.
- Note what is absent: no machine boards for lathes/autolathe, no APC frame design (only the APC board), no
  weapons, no medical. The shuttle docker/flight control/propulsion boards plus shuttle blueprints and rods mean a
  colony can in principle build its own shuttle.

## 4. Rations printer designs (`CF/design_datums/rations_printer_designs/`)

Printed by `/obj/machinery/biogenerator/foodricator` ("organic rations printer", `CF/appliances/foodricator.dm`), a
tg biogenerator subtype: plants in, biomass out, `circuit = null`, unanchored, sits on tables, forced to
`efficiency = 1`, `productivity = 3` in `RefreshParts`. Its `show_categories` hide the normal biogenerator menu. All
costs are biomass:
- **Containers** (100): flour sack, korta flour sack, rice sack, sugar sack, soy milk, milk (all `small_ration`).
- **Ingredients**: egg, butter, cheese wedge, firm cheese slice (25); chicken slab, "meat product" slab (50).
- **Luxuries**: gum (100); Activin wake-up gum, energy bar, Uplift cigarettes, Engine Fodder, Fueljack's Snack,
  rice crackers (50).
- **Utensils**: plastic fork/spoon/knife (10), plastic cup (25).
- **Synthesized Seeds** (25): white-beet, potato, soybean, rice, oat, korta nut, plump-helmet. Enough to start a farm
  with no seed vendor.

Its sibling `/obj/machinery/biogenerator/organic_printer` (Kahraman, `modular_nova/modules/kahraman_equipment/code/organic_printer.dm`,
productivity 2, `max_items` 35) prints frontier clothing and kits (jumpsuit 75, boots/gloves 50, flak jacket and soft
helmet 150, medical kits 200, backpack/satchel 100...) and, importantly, **plastic sheets (25 biomass) and cloth
(10)**. That is the plastic source for plastic wall panels.

## 5. Cargo packs

### `CF/cargo_packs.dm`
| Pack | Contents | Cost |
|---|---|---|
| `/datum/supply_pack/service/hydro_synthesizers` "Hydroponics Plumbing Synthesizer Pack" | 2 x `water_synth`, 2 x `colony_hydroponics` (deployed machines, not kits); hydroponics crate | 2 CCV = 400 cr |
| `/datum/supply_pack/service/frontier_kitchen` "Frontier Kitchen Equipment" | water synth, sustenance dispenser, tabletop griddle, microwave, frontier range (all `unanchored`), foodricator | 5 CCV = 1000 cr |
| `/datum/supply_pack/engineering/colony_starter` "Colonization Starter Kit" | colony fab flatpack, Kahraman organics printer kit, packed GPS beacon, 50 plastic wall panels, 25 rods, 20 iron, 2 manual airlock kits, APC frame (`/obj/item/wallframe/apc`), APC electronics, high-capacity battery | 11 CCV = 2200 cr ("6 for the lathe, 3 for the organics printer, 2 for the rest") |

Desc of the starter kit: "The Sol standard minimum kit for frontier colonization, contains everything you need to
construct a mostly functioning colony in most places across the galaxy." Note it has **no generator** and no glass,
silver, gold or titanium.

### Company imports, Akh Frontier (`modular_nova/master_files/code/modules/cargo/packs/companies/machines.dm`)
Group "★ Machines and Flatpacks"; `/datum/supply_pack/companies` sets `order_flags = ORDER_COMPANY`, large import
crate, `access_view = NONE`, and gives cargo a cut of each sale (`_companies.dm`). One item per pack:
- Fab 6 CCV (1200 cr); foodricator 2 (400); Kahraman organics printer 3 (600).
- Appliances: multi-cell charger 0.25 (50), wall heater 0.25 (50), water synth 0.5 (100), hydro synth 0.5 (100),
  sustenance dispenser 1 (200).
- Misc deployables: arc furnace, CO2 cracker, recycler 0.5 each (100); **ore thumper 5 (1000)**; GPS beacon 0.2 (40).
- Power: wind turbine 0.25 (50), A.W generator 3 (600), stirling 1.5 (300), RTG 3 (600), solar T1 0.25 (50),
  T2 0.5 (100), T3 1 (200), T4 1.5 (300), tracker 0.5 (100), solar control board 1.5 (300).
- Heliostatic Coalition surplus: `/obj/item/flatpack/food_replicator` 9 CCV (1800).

## 6. Construction (`CF/construction/`)

- **Plastic wall panel** `/obj/item/stack/sheet/plastic_wall_panel` (`turfs.dm`): 0.5 plastic + 0.5 glass per panel
  (twice the walls per plastic, per the design comment). **Right-click an open turf**: refuses groundless turfs
  (space, chasm, openspace) and blocked tiles, `do_after` 3 s, then `place_on_top(/turf/closed/wall/prefab_plastic,
  CHANGETURF_INHERIT_AIR)`. **No girder, no tools, no power.** The stack's craft menu also has "prefabricated
  wall" (3 s, one per turf, solid ground) and "prefabricated window" (1 s, full tile).
- **Prefab wall** `/turf/closed/wall/prefab_plastic`: `girder_type = null` (deconstructs straight to 1 panel),
  `sheet_amount = 1` (normal wall 2), `hardness = 70` (lower is harder; normal wall 40, so easier to smash),
  `slicing_duration = 5 SECONDS` (normal 10 s), cannot be engraved. Desc: "It's a little unnerving, but it's
  better than nothing at all."
- **Prefab window** `/obj/structure/window/fulltile/colony_fabricator` (`windows.dm`): full tile, returns 1 plastic
  panel. Made from the stack menu, or by clicking a panel on an **anchored grille** (1 s; Nova adds a
  `/obj/structure/grille/item_interaction` override for this).
- **Prefab airlock** `/obj/machinery/door/airlock/colony_prefab` (`doors.dm`): the kit deploys a finished, powered
  airlock machine in 4 s (default flatpack `deploy_time`), no assembly steps. Its `assemblytype` is a door assembly
  with `noglass = TRUE`, so normal deconstruction drops a prefab assembly. Needs area power like any airlock; access
  is whatever an unconfigured airlock has.
- **Manual airlock** `/obj/structure/mineral_door/manual_colony_door` (`manual_door.dm`): a structure, **no power**.
  1 s `do_after` to open or close, bumping does nothing, 1 s animation, will not close on a mob, pickaxes cannot dig
  it, disassembles back into its kit. Two come in the starter kit.
- **Prefab shutters** `/obj/machinery/door/poddoor/shutters/colony_fabricator/preopen`: deploys open; a poddoor, so
  it still needs a button (the `control` design is flag-added) and power.
- **Floors**: `/obj/item/stack/tile/iron/colony` -> `/turf/open/floor/iron/colony` (grey, texture, bolts and white
  variants via `tile_reskin_types`); `/obj/item/stack/tile/catwalk_tile/colony_lathe` -> catwalk over plating
  (cables visible). Normal tile placement rules.

## 7. Tools (`CF/tools/tools.dm`)
- `/obj/item/screwdriver/omni_drill` "powered driver": screwdriver, wrench and wirecutters in one (radial menu in
  hand), `toolspeed = 1` ("not much quicker than unpowered tools"), cuts zipties instantly.
- `/obj/item/crowbar/large/doorforcer` "prybar": `force_opens = TRUE` (forces doors like jaws of life),
  `toolspeed = 1.3`, 1.75 iron + 0.5 Ti. **Not a colony design**: sold by the tool vendor (`premium_nova` list, 2 in stock, `modules/modular_vending/code/tool.dm`) and the black market.
- `/obj/item/pickaxe/drill/compact`: normal-weight drill that fits a backpack, `toolspeed = 0.6`.
- `/obj/item/weldingtool/electric/arc_welder`: electric welder, `toolspeed = 1`, `POWER_CELL_USE_INSANE` drain.

## 8. Other colony/outpost-adjacent Nova modules

- **`kahraman_equipment`** (`modular_nova/modules/kahraman_equipment/`): the second frontier brand. The organics
  printer (section 4), `/obj/item/gps/computer/beacon` (anchored, repackable GPS beacon used as a colony homing
  marker), frontier clothing/armour, and the **ore thumper** `/obj/machinery/power/colony_ore_thumper`
  (`ore_thumper.dm`). The thumper must be outdoors on ash, snow or sand with a wired connection and 50 kW spare. It
  slams every 15 s and after 30 slams (7.5 min) drops a weighted box of ore (iron x25 and basalt sand x25 weight 5,
  plasma x15 weight 4, uranium/silver/gold/titanium x10 weight 3, diamond x5 weight 2, bluespace x1 weight 1). It
  stops if more than 5 ore piles are nearby and needs 2 tiles clear of other thumpers. Cargo only (1000 cr), not
  printable.
- **`ghost_mining`**: the BSC refinery box `/obj/structure/ore_box/boulder_collector` (fab-printable), the
  `/obj/machinery/lrm` Linked Retrieval Matrix that teleports boulders out of linked BSCs (board `lrm_board` has the
  colony bit), and `/obj/structure/ore_vent/ghost_mining`, resettable ore vents for ghost roles with random boulder
  sizes, mineral mixes and wave threats. Mining for groups with no station ORM.
- **`flatpacks`**: only `/obj/item/flatpacked_machine/self_actualization_device`, reusing the colony flatpack base
  for command lockers. Shows the flatpack base being reused as a generic "machine in a box".
- **`food_replicator`**: `/obj/machinery/biogenerator/food_replicator` "Pioneer-Class Matter Resequencer" (HC
  faction) prints food, medical items and clothing from biomass at 0.75x efficiency/productivity. Placed in the
  lavaland colonist homestead ruin; 1800 cr as a company import.
- **`bluespace_miner`**: `/obj/machinery/bluespace_miner` generates ore into a connected ore silo every 4-6 s
  (faster with servos). Fails if too hot, low or high pressure, or another miner is within 1 tile. A later "passive
  mining gizmo" in station form.
- **`powerator`**: `/obj/machinery/powerator` sells surplus grid power to CentCom for credits (engineering budget).
  A model for "outposts export power".
- **`inflatables`**: `/obj/structure/inflatable` walls and doors meant to replace holofans; quick, fragile
  pressure seals that melt above fire temperature. Emergency outpost sealing.
- **`primitive_structures` / `primitive_production`**: wooden fencing and fence gates (`/obj/structure/railing/wooden_fencing`),
  large wooden gates, thatch roofs and tiles, wooden shelves, wall torches, wooden ladders; ceramics and
  glassblowing crafting. Low-tech building set for the icecat and ashwalker ghost roles, and the obvious fit for
  animal pens.
- **`magfed_turret`**: magazine-fed deployable turrets for ruins and ghost roles, with faction/IFF modes, throw
  deployment, tool-less packing and a target designator. Reference for outpost defence turrets.
- **`tarkon`**: Port Tarkon, an 8-person ghost-role station ruin with the brief "help finish construction", mapped
  with a colony fab, A.W generator, battery pack and Tarkon BSCs. The closest thing Nova has to a player colony.
- **`mapping`**: the colonist homestead lavaland ruin (`colonist_homestead`, cost 5,
  `_maps/RandomRuins/LavaRuins/nova/lavaland_surface_prefab_homestead.dmm`; area comment "Dependent on the
  colony_fabricator module"), built from colony floors, prefab walls, windows, airlocks and shutters, 6 packed solar
  panels, a tracker and solar console, an A.W generator, a food replicator and an autolathe board. Also
  `/obj/machinery/hydroponics/soil/fake_turf` (`mapping/code/turf.dm`): soil that looks like a dirt floor with
  `self_sustaining = 1`, for planet farms. The icemoon turret bunker also uses colony turfs, windows and shutters.
- **`condos`**: `SScondos` loads private room templates (cabin_woods, snowy_cabin, planar_soil, beach_condo...)
  into turf reservations through a "Matrixed Teleportation Unit", Hilbert's Hotel style. Prior art for loading a
  saved template on demand.
- **`modular_persistence`**: `/datum/modular_persistence`, a per-character save file (add a var, it persists).
  Station-side players only. Prior art for per-character storage, not grid saving.
- **Negative results**: no Nova module for fultons (only contractor extraction), tents, camps, settlements,
  mining drones or bots (tg minebots are upstream, not Nova), or dedicated planet hydroponics beyond `fake_turf`
  soil and the synthesizers. `ashwalkers`, `primitive_catgirls` and `icemoon_additions` (pet commands, icecat
  recipes) are tribe ghost roles, not player-built outposts.

## 9. How Nova intends a colony to bootstrap

Inferred from pack contents, costs and descriptions:
1. **Buy the Colonization Starter Kit** (2200 cr) or order pieces from Akh Frontier. You get the fab, a biomass
   printer, a GPS beacon to mark the site, 50 plastic panels, some iron and rods, 2 unpowered manual airlocks and a
   complete APC with a battery.
2. **Shell first, no power needed.** Right-click panels onto open ground (3 s each, no girders), make windows from
   panels, deploy the manual airlocks. Panels carry their own glass, so a sealed room needs nothing else.
3. **Power the room.** Mount the APC; its battery runs the fab (which needs an APC but very little energy).
   Print solar panels (iron + glass) and wind turbines (the turbine's desc says it works "on a planet with an
   atmosphere", with 4x output in storms), or buy an RTG, A.W or stirling generator. Add stationary batteries.
4. **Materials loop.** Feed the fab mined ore or sheets; print the arc furnace (1.5x smelting), ore silo, BSC and LRM
   boards; order an ore thumper for passive ore on planet terrain. The recycler turns junk back into sheets at 80%.
5. **Life support.** CO2 cracker (O2 from CO2), portable pumps and scrubbers, limited thermomachine, wall heaters,
   air alarm, fire alarm and firelock boards.
6. **Food and water.** Water synth plus hydro synth plumbed into hydroponics trays (tray board is flag-added);
   foodricator seeds start the farm; crops go into the foodricator for flour, milk, meat and eggs; kitchen
   appliances cook. The organics printer turns biomass into plastic and cloth, which feed back into walls and
   clothing.
7. **Scale up.** The fab prints another fab (2 min), stirling generators, the factory machine boards, and shuttle
   boards and blueprints. Nothing needs research; the limit is materials and fab time.
The design intent is "build fast, damn the consequences": weak but free and instant structures, no research gate,
everything repackable and portable.

## Implications for Outposts

- The Planet Outpost Kit maps closely onto `colony_starter`: fab, biomass printer, beacon, wall material, manual
  doors and an APC set. Add the Outpost Console and a Universal Trade Hub, and price it the same way (sum of parts +
  margin; Nova charges 11 CCV).
- Upstream SS14 already has `FlatpackComponent`/`FlatpackCreatorComponent` (`Content.Shared/Construction/Components/`),
  `LatheComponent` (with a Wolfgate partial in `Content.Shared/_WF/Lathe/`) and `OreSiloComponent`. An outpost fab
  is a lathe prototype with its own recipe packs, a flatpack pickup/deploy, and no research gate; it needs little
  new code.
- Take on "print time is the limit, not research": Nova's fab is deliberately slow (no 0.1x speedup) and
  unupgradable, but its unlock list is complete from the start.
- A tool-less wall placed from a stack by right-clicking open ground, which falls straight back into 1 stack, is the
  single biggest quality-of-life item for RimWorld-style building. Keep prefab walls weaker than station walls
  (Nova: `hardness` 70 vs 40, half the slicing time) so raids matter.
- The unpowered manual door (1 s `do_after`, ignores bumps) gives a no-power early game and a door that raiders
  cannot tailgate through.
- Wind turbine logic (outdoor area + minimum pressure + weather multiplier) fits the planned wind turbine gizmo and
  planet weather; Nova uses 2.5 kW base and 10 kW in weather.
- The ore thumper is a template for the "mining bots / cheaper component generation" gizmos: an outdoor terrain
  check, a 50 kW power draw, periodic weighted ore drops, a cap on nearby output and spacing between units.
- Foodricator seeds + hydro synth + water synth together solve food and water with no station supply; an outpost
  should get the same closed loop, with animal farming as the upgrade.
- Everything is repackable (right-click, 3 s). For saving outposts that is handy: packed machines are plain items,
  and deployed ones keep no hidden state except fuel, charge and storage.
- Nova's material storage links by same-z silo; outposts will want a per-outpost silo that the console owns.
- Nova has no grid saving; the nearest prior art is `condos` (template into reservation) and `modular_persistence`
  (per-character file); the grid save/load itself has to be built for Wolfgate.
