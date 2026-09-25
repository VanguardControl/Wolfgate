# Nova Sector colony_fabricator: machines and appliances

Source root: `NovaSector/modular_nova/modules/colony_fabricator/` (abbreviated `CF/` below). Parents were read in
NovaSector's own tg base (`NovaSector/code/...`), which matches `tgstation` for every define used here. Authored
by Paxilmaniac (Skyrat, then Nova; icon history starts 2023-12-24 with the `modular_skyrat -> modular_nova` rename).

## Units and constants used below

- `SHEET_MATERIAL_AMOUNT` = 100, `HALF_SHEET_MATERIAL_AMOUNT` = 50, `SMALL_MATERIAL_AMOUNT` = 10. Costs below are in **sheets**.
- `BASE_MACHINE_ACTIVE_CONSUMPTION` = 1 kW (idle 100 W x 10).
- `STANDARD_CELL_CHARGE` = 10 kJ, `STANDARD_CELL_RATE` = 10 kW, `STANDARD_BATTERY_CHARGE` = 1 MJ. High cell = 100 kJ.
- `SSmachines` ticks every 2 s. `CARGO_CRATE_VALUE` = 200 cr.
- `COMPANY_FRONTIER` (`code/__DEFINES/~nova_defines/manufacturer_strings.dm:35`): examine label "Akhter Company
  Frontier Equipment ... alongside various xerxian proof-marks".

## Shared framework (what makes a machine "colony grade")

Every machine repeats the same recipe:

1. `circuit = null`: no board, no stock parts, no upgrades. Stats are hard-set on the type and usually re-forced in a
   `RefreshParts()` override (`// Nuh uh!` in the wall charger). With no circuit, tg's `apply_default_parts` never runs,
   so `RefreshParts()` is not called at init; the type vars are the live values.
2. `AddElement(/datum/element/repackable, <packed type>, <time>)`: right-click with an empty hand to fold back into a flatpack.
3. `AddElement(/datum/element/tool_blocker, TOOL_SCREWDRIVER / TOOL_CROWBAR)`: blocks the normal panel/deconstruct path,
   so repacking is the only way to move or recover it.
4. `AddElement(/datum/element/manufacturer_examine, COMPANY_FRONTIER)`: branding line.
5. A `flick("<name>_deploy")` animation when built at runtime (`if(!mapload)`).
6. Printed at the colony fabricator (build type `COLONY_FABRICATOR`) from hidden techweb nodes
   `colony_fabricator_flatpacks` / `colony_fabricator_appliances` (`starting_node = TRUE`, `hidden = TRUE`,
   research cost 5e13 / 5e15 so they can never be researched elsewhere).

### Flatpack item: `/obj/item/flatpacked_machine` (`CF/code/colony_fabricator.dm`)

- Vars: `type_to_deploy`, `deploy_time` (default **4 s**), `w_class = WEIGHT_CLASS_BULKY` (several subtypes drop to NORMAL).
- `give_deployable_component()` adds tg `/datum/component/deployable` (`code/datums/components/deployable.dm`):
  use in hand -> checks the tile in front of you is not blocked -> ratchet sound -> `do_after(deploy_time)` -> spawns
  `type_to_deploy` on that tile facing your direction (`direction_setting`), then deletes the pack.
- `desc` reuses the machine's text: `desc = /obj/machinery/...::desc`.
- `custom_materials` on each pack equals the fabricator design cost, so recycling a pack refunds it.
- Cyborg sheet/circuit manipulators may carry flatpacks (`storable += /obj/item/flatpacked_machine`).

### Repacking: `/datum/element/repackable` (`CF/code/repacking_element.dm`)

- Bespoke element, args `(item_to_pack_into, repacking_time = 1 SECONDS, disassemble_objects = TRUE)`.
- Hooks `COMSIG_ATOM_ATTACK_HAND_SECONDARY` (RMB, empty hand, needs `NEED_DEXTERITY`), examine hint "It can be
  **repacked** with **right click**", screentip "Repack".
- `repack()`: balloon "repacking...", `do_after(user, 3 SECONDS)`, ratchet sound, `new item_to_pack_into(drop_location())`,
  then `deconstruct(TRUE)` for objs (else `qdel`).
- **Quirk:** `repacking_time` is stored but never used. Every repack takes a fixed **3 s**, whatever the
  per-machine value (1 s solar, 2 s arc furnace, 5 s lathe, 10 s silo/stirling). `disassemble_objects` is also unused.

### The fabricator itself: `/obj/machinery/rnd/production/colony_lathe` "rapid construction fabricator"

- A protolathe-style `rnd/production` machine, `allowed_buildtypes = COLONY_FABRICATOR`, own techweb
  `/datum/techweb/colony_fabricator` (not the station's), `speedup_disabled = TRUE`, `build_efficiency()` returns 1
  (no material discount). Light yellow, power 5 while printing.
- Pack cost 10 iron, 7.5 glass, 2.5 titanium, 0.5 gold, 0.5 silver. It can print itself (`flatpack_colony_fab`, **2 min**).
- Printing plays `colony_fabricator_running` loop (start/mid x4/end WAVs) and `colony_lathe_working` / `_finish_print`.
- Other printable lists live in `design_datums/fabricator_flag_additions/`: 22 machine boards (hydroponics tray,
  cyborg recharger, processor, suit storage, grinder, gas turbine parts, "manu" factory machines), 5 computer boards
  (solar control, atmos alerts, power monitor, shuttle docker, flight control), 10 stock parts (T2-T3 parts, RPED,
  batteries), 40 tools, 26 equipment, 23 construction designs (APC/air alarm/airlock/firelock electronics, lights,
  conveyors, alloys, prefab walls/windows/tiles).

## Machines (`CF/code/machines/`)

### Arc furnace: `/obj/machinery/arc_furnace` (`arc_furnace.dm`)
- Does: smelts one ore stack at a time into sheets at **x1.5** yield (`ARC_FURNACE_ORE_MULTIPLIER 1.5`,
  `round(amount * 1.5)` whole sheets; output split into max-size stacks).
- Input: any `/obj/item/stack/ore` (inserted by hand; only when empty and idle). Radial menu: Use / Eject.
- Time: `ore_amount * 1 SECONDS` (50 ore = 50 s -> 75 sheets).
- Power: `active_power_usage = 10 kW`; `use_energy(active_power_usage)` once per 1 s loop step (10 kJ per ore).
  Stops if `NOPOWER|BROKEN` mid-run (ore kept).
- **Exhaust gas every second** onto its turf via `atmos_spawn_air`:
  default `co2=20;TEMP=1200`; silver `n2=10;TEMP=1200`; uranium `co2=50;TEMP=1200`;
  titanium `n2=10;co2=10;TEMP=1200`; plasma `co2=75;TEMP=2000`. Examine warns it "may exhaust waste gasses to the air".
- Light range 1.5 while running, `arc_furnace_running` sound loop (4 x 1 s WAVs, volume 200).
- Pack: `arc_furnace_folded`, 7.5 iron, 3 glass, print 15 s. Repack arg 2 s.
- Colony vs tg: no tg parent (tg uses ORM/smelter at 1:1). Unique trade-off: more metal for power + a polluted room.
- Quirks: tool blockers are added in `examine()` instead of `Initialize()`; `smelt_it_up` does not `return` after
  "nothing to smelt".
- Icons (`machines.dmi`): `arc_furnace`, `arc_furnace_overlay`, `arc_furnace_overlay_active`, `arc_furnace_deploy` (7f).
  The inserted ore is drawn as a squashed overlay (`matrix(... 0.8 ...)`).

### Colony ore silo: `/obj/machinery/ore_silo/colony_lathe` (`ore_silo.dm`)
- tg ore silo (`code/modules/mining/machine_silo.dm`): unlimited shared material storage (`INFINITY`) that lathes,
  ORMs etc. link to via `remote_materials` (multitool), with an access log, ID restriction and per-user bans.
- Colony changes only: no circuit, crowbar blocked, custom icon, and `silo_log()` plays `beep.ogg` on every logged
  transaction ("Ore silo except it beeps"). A runtime-deployed one never becomes `GLOB.ore_silo_default`
  (that needs `mapload` on a station z).
- Pack: `ore_silo`, 5 iron, 5 glass, print **1 min**. Repack arg 10 s (really 3 s).
- Icons (`ore_silo.dmi`): `silo`, `silo_active` (2f), `ore_silo` (pack).

### Stationary batteries: `/obj/machinery/power/smes/battery_pack` and `/large` (`power_storage_unit.dm`)
| | small "stationary battery" | large "large stationary battery" | tg SMES (T1) |
|---|---|---|---|
| capacity | 10 MJ (`10 * STANDARD_BATTERY_CHARGE`, "1 high megacell") | 100 MJ | 50 MJ (5 high megacells) |
| in/out max | 400 kW / 400 kW | 50 kW / 50 kW | 200 kW / 200 kW |
| pack cost | 7 iron, 2 glass, 0.5 silver | 12 iron, 4 glass, 1 gold | board build |
| print time | 20 s | 40 s | |
- Design intent from the descs: small = "low storage high output" regulator, large = "high storage low output" backup.
- Uses an imaginary board `/obj/item/circuitboard/machine/battery_pack` holding one megacell whose `maxcharge` is
  forced to `total_capacity`. `RefreshParts()` and `exchange_parts()` are stubbed, so no RPED upgrades.
- `/precharged` subtypes start full (mapping use). Deploy anim `smes_deploy` (4f). Repack arg 5 s.
- **Likely quirk (read from code, not tested):** unlike the others this keeps a non-null `circuit`, and
  `on_deconstruction()` is stubbed. tg `handle_deconstruct()` still calls `spawn_frame()` and drops component parts,
  so a repack appears to leave a machine frame, the board and the megacell next to the new flatpack, and the stored
  charge is lost.
- Icons: `power_storage_unit/small_battery.dmi` and `large_battery.dmi`, full SMES overlay set
  (`smes-og1..5` charge, `smes-op0/1`, `smes-oc0/1`, `smes-o`, `smes_deploy`). Packs `battery_small_packed`, `battery_large_packed`.

### Portable RTG: `/obj/machinery/power/rtg/portable` "High-Temperature Self-Contained Reaction Generator" (`rtg.dm`)
- Flat **15 kW**, no fuel, no input ("50% more than a t4 solar or winded turbine"). tg RTG base is 1 kW multiplied by
  summed part tiers (`affected_by_parts`); with no parts, `part_level || 1` gives 15 kW x 1.
- `max_integrity = 40` (fragile). When destroyed by damage: `explosion(dev 0, heavy 2, light 4, flash 5)` + smoke +
  `shockwave_explosion.ogg`.
- `AddElement(/datum/element/radioactive, 1, RAD_LIGHT_INSULATION, URANIUM_IRRADIATION_CHANCE * 0.5,
  URANIUM_RADIATION_MINIMUM_EXPOSURE_TIME * 7)`: range 1 tile, threshold 0.8, 5% chance, 21 s minimum exposure.
- Pack `rtg_packed`: 5 iron, **5 uranium, 5 plasma**, 1 gold; print 30 s. Deploy anim `rtg_deploy` (4f).
- Colony vs tg: much stronger, no parts, but a bomb that irradiates.

### Deployable solar panels and tracker (`solar_panels.dm`)
- `/obj/machinery/power/solar/deployable` + `/titaniumglass` (tier 2), `/plasmaglass` (3), `/plastitaniumglass` (4).
  tg output = `SOLAR_GEN_RATE (2500 W) * sunfrac * power_tier`, i.e. 2.5 / 5 / 7.5 / 10 kW peak per panel. Sun comes
  from `SSsun` azimuth with Lambert cosine falloff; occluded panels (`is_sunlight_blocked`, `TRAIT_TURF_SUN_BLOCKED`) make 0.
- Colony changes: prebuilt panel (no assembly + glass step), `crowbar_act` returns (cannot pry out glass), screwdriver
  blocked, the internal `solar_assembly` is deleted on deconstruct (no refund dupe).
- Packs (`w_class NORMAL`, fit in a bag): base 1.5 iron + 2 glass; +1 titanium; +1 plasma; +1 plasma +1 titanium.
  Deploy 2 s, print 4 s.
- `/obj/machinery/power/tracker/deployable`: pack 2 iron + 1.75 glass, deploy 3 s, print 7 s. Solar control console
  board is printable from the fabricator (`computer_board.dm`).
- Icons (`machines.dmi`): `sp_base` (4f), `solar_panel*`, one set per glass type
  (`solar_panel_<glass|titanium glass|plasmaglass|plastitanium glass>` + `_edge` + `-b`), `tracker`, `tracker_edge`,
  `tracker_base` (4f). Packs `solar_panel_packed`, `solar_tracker_packed`.

### A.W.-type portable generator ("solid fuel generator"): `/obj/machinery/power/port_gen/pacman/solid_fuel` (`solid_fuel_generator.dm`)
- Despite the path, it burns **uranium sheets**, not wood/coal. PACMAN subtype, UI and fuel math inherited.
- `power_gen = parent * 2` = **20 kW per power level**; levels 1-4 (tg `ui_act`), so 20-80 kW.
- `time_per_sheet = 180` (inherited): at level 1 one sheet lasts 180 ticks = 360 s; level 4 = 90 s.
  `max_sheets = 25` -> 150 min at L1, 37.5 min at L4. Comparison comments: PACMAN 50 sheets/10 kW plasma;
  SUPERPACMAN 20 sheets/30 kW uranium/60 ticks per sheet.
- Inherited heat/overheat: explodes above 300 heat (`explosion(2,5,2)`), which only happens when emagged past level 4.
- `anchored = TRUE` by default ("must be bolted to the ground"), `drag_slowdown = 1.5`.
- **Byproduct gas each tick while active:** `water_vapor=9;TEMP=400` and `helium=1;TEMP=400` (desc: "520C helium and
  water", "profitable byproduct gasses").
- Sound: own loop `solid_fuel_generator` (`AW_reactor.ogg`, 1 s, volume 80) replacing the PACMAN loop.
- Pack `fuel_generator_packed`: 5 iron, 1 glass, 1 titanium, 0.5 gold; print 30 s.
- History (git log -S): the first version (#24058 "Cargo Engineering Content Part 1") was a diesel analogue:
  **plasma** sheets, 12.5 kW, 100 ticks/sheet, exhaust `co2=10;TEMP=480` ("Standard UK diesel engine operating
  temp is about 220 celsius"). Rebuilt into the uranium A.W. by "Akhter Company begins rolling out A.W. 2.0 (#6010)"
  and rebalanced in "Powerators Changes (#6130)".
- Icons (`machines.dmi`): `fuel_generator_0`, `fuel_generator_1` (8f running), `fuel_generator_deploy` (9f).

### Stirling generator: `/obj/machinery/power/stirling_generator` (`stirling_generator.dm`), "We have TEG at home"
- Input: hot gas through one pipe port (`/datum/gas_machine_connector`, volume `CELL_VOLUME * 0.5` = 1250 L), facing
  `dir`. Cold side: the turf air around it. Examine: "It will not work in a **vacuum**".
- `process_atmos()`: dT = pipe gas temp - room temp; if dT <= 0, output 0. Cools the pipe gas by
  `CALCULATE_CONDUCTION_ENERGY(dT, pipe_cap, room_cap) / pipe_cap`.
  Power = `max_power_output (150 kW) / round(8000 / min(dT, 8000), 0.01)`, i.e. linear **18.75 W per K**, capped at
  **150 kW at dT >= 8000 K** (1000 K dT = 18.75 kW).
- Notes: output depends only on dT, not on how much heat moves; the removed heat is not added to the room (the room
  never warms up).
- `use_power = NO_POWER_USE`, `max_integrity 300`, thermomachine armor, cable layer changeable. LMB wrench rotates
  90 degrees. Sound loop `ore_thumper_fan` (from `modular_nova/modules/kahraman_equipment`).
- Pack (`stirling_generator/packed_machines.dmi` `stirling`): 15 iron, 5 glass, **10 plasma**, 5 titanium, 5 gold;
  print **2 min** (most expensive flatpack). Repack arg 10 s.
- Icons `stirling_generator/big_generator.dmi`: `stirling` (4 dirs), `stirling_on` (4 dirs, 4f).

### Atmospheric temperature regulator: `/obj/machinery/atmospherics/components/unary/thermomachine/deployable` (`thermomachine.dm`)
| | colony regulator | tg thermomachine, T1 parts |
|---|---|---|
| range | 273.15-473.15 K (0-200 C; `T0C` to `FIRE_MINIMUM_TEMPERATURE_TO_SPREAD + 50`) | 73.15-573.15 K |
| heat capacity | 10000 J/K | 5000 J/K (`5000 * (bins - 1)^2`) |
- Values re-forced in `RefreshParts()`. Power draw uses the tg formula (idle + heat moved).
- Pack sets `direction_setting = FALSE` on the deployable component ("prevents some weird visual bugs with the inlet"),
  so it deploys in its default direction, not the player's facing. Plays `thermo_deploy` even on mapload.
- Sound loop `conditioner_running` (4 x 3 s WAVs, volume 40) while on. Greyscale config
  `/datum/greyscale_config/thermomachine/deployable` -> `thermomachine.dmi`.
- Pack `thermomachine_packed`: 7.5 iron, 1 glass; print 20 s.
- Icons: `thermo_base`, `thermo_base_1` (4f), `thermo_base-o`, `pipe` (4 dirs), `temp_meter`, `temp_meter_1` (4f),
  `temp_meter-o`, `thermo_deploy` (6f).

## Appliances (`CF/code/appliances/`)

### Miniature wind turbine: `/obj/machinery/power/colony_wind_turbine` (`wind_turbine.dm`)
- Needs **all** of: `get_area(src).outdoors` (area flag, i.e. planet surface), turf pressure >= **5 kPa**
  (`minimum_pressure`). Otherwise 0 W and idle icon; examine explains which is missing.
- Output: **2.5 kW** normally, **10 kW** when any `SSweather.processing` weather covers its z-level or area and is not
  in `END_STAGE`. Any weather counts (it doesn't check the weather type).
- `max_integrity 100`, dense, `layer = ABOVE_MOB_LAYER` (tall post), `idle_power_usage 0`, connects to cable on init.
- Pack `turbine_packed` (in `wind_turbine.dmi`, `w_class NORMAL`): 5 iron, 2 glass, 0.5 gold; print 30 s. Design id
  `flatpack_turbine_team_fortress_two`.
- Icons: `turbine` (still), `turbine_normal` (2f), `turbine_storm` (2f).

### Portable CO2 cracker: `/obj/machinery/electrolyzer/co2_cracker` (`co2_cracker.dm`)
- tg electrolyzer subtype (cell or APC powered, alt-click on/off, anchor = APC power). No cell by default
  (`circuit = null`, parent `cell` is null), so it must be anchored or given a cell.
- Own reaction registry `GLOB.cracker_reactions` (`/datum/cracker_reaction`, with MIN/MAX_TEMP + gas requirements),
  kept separate "because that'd let electrolyzers do co2 cracking".
- `co2_cracking`: needs CO2 >= `MINIMUM_MOLE_COUNT` (0.01). Per atmos tick converts
  `min(CO2 / 2, 2.5 * working_power^2)` moles CO2 -> O2 1:1 (**max 10 mol/tick** at `working_power 2`), temperature
  rescaled to keep thermal energy.
- Fixed `working_power = 2`, `efficiency = 1`; tg energy formula `5*(3*wp)*wp/(eff+wp)` = 20 J per tick.
- crowbar disabled; sound loop `conditioner_running`.
- Pack (`parts_kits.dmi` `co2_cracker`): 7.5 iron, 3 glass, 0.5 plasma ("pretend plasma is the catalyst"); print 30 s.
- Icons (`portable_machines.dmi`): `electrolyzer-off`, `-standby`, `-working` (4f), `-open`.

### Organic rations printer ("foodricator"): `/obj/machinery/biogenerator/foodricator` (`foodricator.dm`)
- tg biogenerator (plants -> biomass -> printed goods). `productivity = 2.5` (T1 biogen = 1; `RefreshParts` would
  set 3 but never runs), `efficiency = 1`, `max_items` 20. Unanchored, `PASSTABLE`, `anchored_tabletop_offset = 6`.
- Shows only Akhter categories (`design_datums/rations_printer_designs/`), costs in biomass:
  seeds 25 each (white-beet, potato, soybean, rice, oat, korta, plump-helmet); ingredients 25-50 (egg, chicken,
  meat product, butter, cheese, firm cheese); ration sacks 100 (flour, korta flour, rice, sugar, soy milk, milk);
  snacks 50-100 (gum, Activin gum, energy bar, cigarettes, moth snack bags, rice crackers); utensils 10-25.
  Desc: "steak and eggs for breakfast with a lack of any livestock at all".
- Pack `biogenerator_parts` (`foodricator.dmi`): 5 iron, 2 glass, 1 silver, 0.5 gold; print 30 s. Repack arg 5 s.
- Icons: `biogenerator` + `_o_panel/_o_screen/_o_process/_o_container` + `_o_biomass_1..7`.

### Materials recycler: `/obj/machinery/colony_recycler` (`recycler.dm`)
- Hand-fed crusher (not belt-fed). Own `material_container` with 11 materials (iron, glass, silver, plasma, gold,
  diamond, plastic, uranium, bananium, titanium, bluespace), capacity `INFINITY`.
- On each item consumed: `recycler_grind` flick, forge sound (from `modular_nova/modules/reagent_forging`),
  `use_energy(min(250 J, amount/100))`, then `retrieve_all()` drops every full sheet; partial sheets stay inside.
- Examine says "Reclaiming 80%", but `amount_produced = 80` is display-only; `user_insert` calls
  `insert_item(..., multiplier 1)`, so **100% is returned**.
- RMB wrench anchors/unanchors. Starts unanchored.
- Pack (`parts_kits.dmi` `recycler`): 7.5 iron, 3 glass, 0.5 titanium; print 30 s. Icons `recycler`, `recycler_grind` (3f).

### Mounted heater: `/obj/machinery/space_heater/wall_mounted` (`space_heater.dm`)
- tg space heater on a wall (`MAPPING_DIRECTIONAL_HELPERS`, pixel shift 29), non-dense, climb/elevation removed.
- `heating_energy` = 2 x 40 kJ = **80 kJ per atmos tick** max, `efficiency` = 2 x (20 MJ / 10 kJ) = 4000 J heat per J
  of cell. Heats or cools toward a target of 0-60 C (median 30 C, range +/-30). Spreads over its turf + adjacent turfs.
- **Cell-powered only.** A mapped one has `cell = null`; the wallframe item `/obj/item/wallframe/wall_heater` carries
  a high cell (100 kJ) and moves it in on mount. Cells swap on the frame (click to insert, RMB to remove).
- Wrench: 1 s -> deconstructs back to the frame (cell kept). Not `repackable`; the wallframe is the "pack".
- Frame cost 4 iron, 1 silver, 0.1 gold; print 15 s. Icons (`space_heater.dmi`): `sheater-off/-heat/-cool/-standby`
  (+ `-emissive`), `sheater-open`.

### Mounted multi-cell charging rack: `/obj/machinery/cell_charger_multi/wall_mounted` (`wall_cell_charger.dm`)
- Parent is Nova's `modular_nova/modules/multicellcharger` (4 slots, 10 kW x capacitor tiers / 6 per cell).
- Colony: **3 slots**, fixed **30 kW per cell** (`STANDARD_CELL_RATE * 3`, re-forced), wall mounted, wrench 1 s -> wallframe.
- Frame `/obj/item/wallframe/cell_charger_multi` (`packed_machines.dmi` `cell_charger_packed`): 2 iron, 1 silver;
  print 15 s. Icons (`cell_charger.dmi`): `wall_charger`, `-cell`, `-o0..o4` (2f each), `wall_charger_deploy` (6f).

### Chemistry appliances (`chem_machines.dmi`, `chem_machines.dm`)
- **Water synthesizer** `/obj/machinery/plumbing/synthesizer/water_synth`: plumbing synthesizer limited to water.
  tg synth: 0-5 u per tick, `active_power_usage` 2.75 kW, 50 u buffer, must be plumbed. Starts unanchored.
  Kit `water_synth_parts`: 2.5 iron, 1 glass, `w_class NORMAL`, deploy 2 s, print 30 s. Icons `water_synth(_inactive)`.
- **Hydroponics chemical synthesizer** `/obj/machinery/plumbing/synthesizer/colony_hydroponics`: E-Z Nutrient,
  Left 4 Zed, Robust Harvest, Enduro-Grow, Liquid Earthquake, weed killer, pest killer. Same cost/stats as water.
- **Sustenance dispenser** `/obj/machinery/chem_dispenser/frontier_appliance`: 16 drink/food reagents (water, powdered
  milk/lemonade/coco/coffee/tea, sugar, vanilla, caramel, korta nectar and milk, astrotame, salt, pepper, nutraslop,
  enzyme). 400 J per unit (tg 100 J/u), recharge 2 kW (tg 300 W x capacitor tier), `base_reagent_purity 0.5`
  (tg 1), no pH shown, spawns a high cell (100 kJ = 250 u). Tabletop (`PASSTABLE`, offset 4). **Not repackable**
  ("can be deconstructed normally"). Kit `dispenser_parts`: 2 iron, 1 glass, 0.5 titanium; print 30 s.
  Icons `dispenser`, `dispenser_working`, `dispenser_nopower`, `dispenser_panel-o`, `disp_beaker`.

### Kitchen appliances (`kitchen_appliances/`)
- **Tabletop griddle** `/obj/machinery/griddle/frontier_tabletop`: `variant = "table"`, sits on tables
  (`PASSTABLE`, offset 3), tg griddle otherwise (8 items, 1 kW active). Repackable (arg 2 s). `/unanchored` subtype
  for crates. Kit `griddle_parts`: 7 iron, 3 glass, 0.5 silver; print 1 min. Icons `griddletable_off/_on`.
- **Microwave oven ("macrowave")** `/obj/machinery/microwave/frontier_printed`: `max_n_of_items = 5` (tg T1 10),
  `efficiency = 2` (T2-laser equivalent: cook step wait `max(12 - 2*eff, 2)` = 8 vs 10, more power per step), `vampire_charging_capable = TRUE`
  (tg needs a T2+ capacitor). **Not repackable.** Kit `packed_microwave`: 5 iron, 2 glass, 0.5 silver; print 30 s.
  Full microwave icon set in `microwave.dmi`.
- **Frontier range** `/obj/machinery/oven/range_frontier`: oven + `/datum/component/stove` (container offset -3,14),
  1.2 kW active (same as tg range). **Not repackable.** Kit `range_packed`: 7 iron, 3 glass, 0.5 silver; print 1 min.
  Icons `range_on/_off`, `range_lid_open/closed`, `range_on_flame` (4f), `range_light_mask`, `range_on_overlay`.

### Other fabricator designs in the appliance node
- tg `portable_atmospherics/pump` and `/scrubber`, 7.5 iron + 3 glass, 30 s (`appliances.dm` also gives them
  `custom_materials` so they recycle).
- Compressed BSC refinery box (`flatpack_bsc`, 5 iron, 3 plasma, 3 titanium, 30 s): lava/fireproof ore box that
  auto-pulls boulders from a set direction; defined in `modular_nova/modules/ghost_mining/code/boulder_collector.dm`.

## Cargo (`CF/code/cargo_packs.dm`)
- "Colonization Starter Kit" (engineering), **11 x 200 = 2200 cr** ("6 for the lathe, 3 for the organics printer, 2
  for the rest"): fabricator flatpack, organics printer flatpack (Kahraman), GPS beacon flatpack, 50 plastic wall
  panels, 25 rods, 20 iron, 2 manual airlock kits, APC frame + electronics, high megacell. Desc: "The Sol standard
  minimum kit for frontier colonization".
- "Frontier Kitchen Equipment", 1000 cr: water synth, sustenance dispenser, griddle, microwave, range, foodricator
  (as deployed machines, not packs).
- "Hydroponics Plumbing Synthesizer Pack", 400 cr: 2 water + 2 hydro synths.

## Sound loops (`CF/code/looping_sounds.dm`)
| datum | files | mid_length | volume | falloff | used by |
|---|---|---|---|---|---|
| `colony_fabricator_running` | `fabricator_start.wav` (0.1 s), `fabricator_mid_1..4.wav` (3 s), `fabricator_end.wav` (2 s) | 3 s | 100 | 3 | fabricator |
| `arc_furnace_running` | `arc_furnace_mid_1..4.wav` (1 s, 48 kHz stereo) | 1 s | 200 ("very quiet") | 2 | arc furnace |
| `conditioner_running` | `conditioner_1..4.wav` (3 s, 24 kHz stereo) | 3 s | 40 | 3 | thermo regulator, CO2 cracker |
| `solid_fuel_generator` | `AW_reactor.ogg` listed 4 times | 1 s | 80 | default | A.W. generator |
Other sounds in `sound/`: `arc_welder/arc_welder.ogg` (tool), `manual_door/manual_door_open/close.wav` (manual airlock).

## Icons summary (`CF/icons/`, all 32x32 states)
- Machines: `machines.dmi` (arc furnace, solars, tracker, fabricator, A.W. generator, RTG), `ore_silo.dmi`,
  `power_storage_unit/{small,large}_battery.dmi`, `stirling_generator/big_generator.dmi` (4-dir),
  `thermomachine.dmi`, `wind_turbine.dmi`, `space_heater.dmi`, `cell_charger.dmi`, `portable_machines.dmi`
  (CO2 cracker + recycler), `chemistry_machines.dmi`, `foodricator.dmi`, `kitchen_stuff/{griddle,microwave,range}.dmi`.
- Packs: `packed_machines.dmi` (arc furnace, solar panel, tracker, lathe, both batteries, fuel gen, RTG, cell charger,
  thermomachine), `parts_kits.dmi` (co2_cracker, recycler), `stirling_generator/packed_machines.dmi`.
- Deploy animations exist for arc furnace (7f), fabricator (5f), fuel gen (9f), RTG (4f), batteries (4f),
  thermomachine (6f), wall charger (6f).
- Construction (not machines): `prefab_wall.dmi`/`prefab_window.dmi` (smoothing sets), `tiles*.dmi`, `doors/*`, `tools.dmi`.

## Licensing
- Repo: code AGPLv3; "All assets including icons and sound are under a Creative Commons 3.0 BY-SA license unless
  otherwise indicated" (`NovaSector/README.md:77`). No per-file icon credits in the module, so the icons fall under
  CC-BY-SA 3.0 (credit Paxilmaniac / NovaSector).
- `sound/attributions.txt`:
  - `arc_furnace_mid_1-4.wav`, `fabricator_mid_1-4.wav`, `fabricator_start/end.wav`: recorded by an anonymous
    contributor "specifically for free open source use from the novasector codebase". No named license.
  - `conditioner_1-4.wav`: Pixabay "wall-air-conditioner-43901".
  - `arc_welder.ogg`: Pixabay "welder-3-54547".
  - `manual_door/*`: Pixabay "schlonk-107321" (the attribution path has a typo, `colony_fabriactor_event_code`).
  - `AW_reactor.ogg`: freesound.org/people/dobroide/sounds/29611, shortened. No license named in the file; check the
    freesound page before use.
- Pixabay audio is under the Pixabay Content License, not CC. It does not allow redistributing files as-is on their
  own, which conflicts with shipping raw files in a CC-BY-SA asset tree. Treat conditioner, welder and door sounds as
  not portable without review. Also not covered here: the stirling fan (Kahraman module) and the recycler `forge.ogg`
  (reagent_forging module).

## Quirks found (read from code, not tested in game)
1. Repack always takes 3 s; the per-machine `repacking_time` is ignored.
2. Battery pack repack probably drops a frame, the board and the megacell alongside the pack, and loses the charge.
3. Recycler advertises 80% but returns 100%.
4. Wind turbine: any weather = storm bonus x4.
5. Stirling: power from dT alone; heat is removed from the pipe and not put into the room.
6. Arc furnace tool blockers only apply after the first examine.
7. "Solid fuel" generator burns uranium; wood/coal was never a fuel (originally plasma).

## Implications for Outposts
- **Gizmo pattern to copy:** fixed-stat, board-less machine + flatpack printed at the outpost + in-hand deploy +
  RMB repack. Wolfgate already has upstream `FlatpackComponent` (`Content.Shared/Construction/Components/FlatpackComponent.cs`:
  `Entity`, `QualityNeeded = "Pulsing"`, one-way unpack). A `_WF` repack component (verb, 3 s DoAfter, spawn pack
  proto, delete machine) would make outpost machines movable like RimWorld furniture. Lesson from item 2: repacking
  must not drop board/parts, and should either keep internal state (charge, fuel, silo contents) on the pack or refuse
  while non-empty.
- **Wind turbine is a direct fit** for the doc's "Wind turbine: Turns to make power". SS14 has the pieces:
  `RoofComponent` / `SharedRoofSystem.IsRooved` for "outdoors", `SharedWeatherSystem.CanWeatherAffect` +
  `WeatherComponent` for storms, tile atmos pressure (>= 5 kPa rule) to fail on airless worlds. A storm-only
  multiplier (restricted to windy weather types, unlike Nova) makes weather matter to the base.
- **Planet-only power tiers (tg numbers, compare as ratios):** wind 2.5 kW (10 kW storm) < solar 2.5-10 kW/panel <
  RTG 15 kW < A.W. 20-80 kW (uranium) < stirling up to 150 kW (needs a heat source and ambient air). Wolfgate's
  `GeneratorRTG` is 40 kW (Mono) and a solar panel `MaxSupply` is 750 W, so rescale rather than copy absolutes.
  The stirling "needs air around it" rule is a natural planet-surface generator.
- **Byproduct-gas trade-offs** (arc furnace CO2/N2 at 1200-2000 K, A.W. water vapour + helium at 400 K) give outposts
  real ventilation design problems. They pair with CO2 cracking and the doc's "Active"/night raids.
- **Survival-loop set for crash-landing / new outposts:** CO2 cracker (breathable air from CO2 atmospheres), water
  synth, hydroponics nutrient synth, rations printer that turns biomass into seeds/eggs/meat/flour. This covers the
  "Farming" section and the Shuttle Crash "scrounge to survive" start without new systems.
- **Starter-kit precedent:** Nova's 2200 cr "Colonization Starter Kit" (fabricator, bio printer, GPS beacon, walls,
  airlocks, APC, battery) maps onto the doc's "Planet Outpost Kit". Put the Outpost Console in the kit and the
  fabricator's machine list behind the Outpost Trade Panel.
- **Ore silo:** Wolfgate already has `OreSiloComponent` (`Range = 20`, client toggles, `silo.yml`); an outpost flatpack
  silo only needs a pack + repack. Outpost saves must persist the silo's materials.
- **Arc furnace** (x1.5 yield, 10 kW, 1 s/ore, pollution) is a good outpost-only refinery: it rewards a fixed base
  with power and ventilation over a ship's ore processor.
- **Save/load pricing:** the pack `custom_materials` equal the print cost, so a saved outpost can be priced from its
  deployed machines' pack costs (the doc's "sum of parts + 50%").
- **Assets:** icons are CC-BY-SA 3.0 and convertible to RSI (all 32x32; animated and 4-dir states listed above).
  Of the sounds, only the anonymous fabricator/arc-furnace WAVs and possibly the freesound A.W. loop are candidates;
  replace the Pixabay files.
