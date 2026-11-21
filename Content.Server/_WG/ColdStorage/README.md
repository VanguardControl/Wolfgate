# ColdStorage

Cold Storage makes the server's cost follow the players online, not the age of the round or the size of the world.
It is the package behind rounds that never have to end and a population cap set by hardware, not by entity count.

Today every ship, wreck, rock and site ever made stays fully loaded until the round restarts. A round that starts at
330,000 entities passes 800,000 in a few hours, and the server drops below 30 TPS whoever is online. Cold Storage
keeps only what someone can see or use loaded, in three tiers:

| Tier | State | Cost | Wakes in |
|---|---|---|---|
| Live | Someone is aboard or near. Simulated as now. | Full | - |
| Stasis | Nobody near for a few minutes. Paused where it stands; keeps every entity and all state. | Memory only | One tick |
| Stored | In stasis long enough. Saved to the database and deleted, leaving one stand-in entity. | A database row | A second or two, spread over ticks, before anyone can see it |

A stored grid's stand-in keeps its place in the world: its radar blip, name, owner, position, drift and outline. A
player approaching, plotting a jump to it, or an owner logging in brings the grid back before it is in view. Time that
passed while stored (fuel, power, spoilage) is applied when it wakes, not simulated.

The unit is always a whole grid, never single entities: a ship is a closed unit with its own air, power and links, so
it can be saved and rebuilt intact. Anything outside that points into a grid (a deed, a dock, an NPC's home) points at
the stand-in.

Because stored grids live in the database, the world survives a restart or a crash. A restart becomes a pause, not
the end of a round.

## Parts

1. **Census** (`EntityCensus`, built): what the entities are and what is spawning.
2. **Pressure governor**: tightens cleanup and spawn budgets as the count climbs.
3. **Stasis** (built): the planet site stash (`Dormancy`) extended to grids in space.
4. **Store and restore**: saving a grid to the database, the stand-in, waking on approach, catch-up on wake.
5. **Engine (Lupine, server-only)**: grid loading spread over ticks so a restore never stalls a tick, and a per-player
   always-visible set so unchanged grids aren't re-added for every player every tick.
6. **Persistence**: restoring the stored world after a restart.

## Stasis

`WGGridStasisSystem` pauses everything on a grid in space that nobody has attended for `wg.stasis.idle` (300 s), mobs
last, leaving anchored walls, windows, doors and cables alone as the site stash does. The grid entity itself is never
paused: a hull keeps drifting and colliding, and its deed, IFF and station membership keep working. Atmos skips a grid
in stasis (`AtmosphereSystem.WGGridCache` in `Performance`).

A grid is attended by a viewer within `wg.stasis.range` (128 m) of its bounds (player bodies, a vehicle's for someone
in its cabin, ghosts, eye targets and camera views, and spots announced with `NoteArrival`), by any body with a mind
aboard (a sleeping or dead player's too), and by being docked to an attended grid. A station never attends what is
docked to it. A grid in stasis wakes when a viewer comes within `wg.stasis.wake_range` (96 m), which is raised to PVS
range plus 32 m if set lower, and the stash range to the wake range plus 32 m. Attendance is worked out four times a
second: each viewer looks up the grids round it in its map's grid tree, and each mind marks the grid it is on.

Policy by owner (`WGStasisPolicy`): ownerless grids (wrecks, derelicts, debris) may enter; deeded ships only with
`wg.stasis.player_ships` (off) and only while no supplier is putting out power and no APC has its breaker on and charge
left. Stations, planets and grids on them, grids in FTL or on a paused map, vehicle cabins, encounter ships and
asteroids never do.

`Wake(grid, hold)` wakes a grid this tick and keeps it awake for `hold` (60 s); `Pin`/`Unpin` hold it awake for as long
as any pin is held; `WGGridStasisChangedEvent` is raised on the grid when it starts going into stasis and when it
wakes. Waking a grid within PVS range plus 32 m, or for a mind, pin, hold or jump, happens whole that tick; the rest is
done within `wg.stasis.budget_ms` (2 ms) a tick. A grid in stasis is swept every `wg.stasis.sweep` (60 s) for anything
that turned up since. `wg.stasis.enabled` (off) turns it all off and wakes every grid at once; tests run with it off
unless they turn it on. Gauges: `wg_stasis_grids{state}`, `wg_stasis_entities` and `wg_stasis_worst_tick_ms`.

Never paused, on any grid: docks, spawn points, cryo pods and cryo sleep points (each with everything inside),
bodies with a mind, and any ID card or voucher holding a ship deed (the item only, even in a paused locker). Stasis is
not applied to a grid that changed map (the record is dropped and nothing is left paused).

Wakes beyond attendance: a split of the grid, a player taking a body on it, an explosion near it, a ship weapon hit or
ram or shield hit (120 s hold), and a jump or hop announced to exit beside it (`NoteArrival`). Systems that need a grid
awake call `Wake`, or `Pin` for as long as they work on it (rope ends, tractor beams, drydock jobs, hacks, autopilot
docking and orbit, salvage magnets, hull retirement); armed warheads and planted beacons keep their grid awake with a
repeating `Wake`. `wg_stasis` (Debug) shows the counts and wakes, pins or unpins a grid.

## Pressure governor

`WGEntityPressureSystem` turns the entity count into a pressure from 0 to 1 and the systems below read it where they
use a value, so a relaxed server behaves exactly as before. It is off by default (`wg.pressure.enabled`). Pressure is
0 at or below `wg.pressure.low` (500000), 1 at or above `wg.pressure.high` (800000), linear between. It is sampled
once a second and moves at most 0.02 a second toward the target, so a mass spawn or delete doesn't whip the knobs;
disabling it drops it to 0 at once. `Pressure` and `Scale(relaxed, strict)` are the API;
`WGEntityPressureChangedEvent` is broadcast when it moves 0.05, and when it returns to 0. The gauge is
`wg_entity_pressure`; crossing 0.25, 0.5, 0.75 and 1 either way writes an Info log line. `wg_entcensus` and `wg_stasis`
show it.

| What | Value at pressure 1 |
|---|---|
| Space cleanup player and grid protection radii (floors of 0.5 m kept) | 50% |
| Mob cleanup player and grid protection radii | 50% |
| Grid cleanup player distance, and how long a grid must stay a candidate | 50% |
| `wg.stasis.idle` | 25% |
| Encounter and freight spawn intervals | 200% |
| `wg.encounters.cleanup_delay` and `cleanup_linger` | 50% |
| Worldgen chunk unload grace and GC queue minimum age | 25% |

Cleanup price ceilings and `max_active` caps never change, and stasis wake ranges stay as they are.

## Stored tier (wrecks)

`WGColdStoreSystem` stores ownerless wrecks only. A grid in stasis for `wg.coldstore.idle` (900 s, down to 25% at full
pressure) is saved with the raw engine serializer, deleted, and replaced by one stand-in entity; it is rebuilt where it
was, at the same position and rotation and at rest, when anything comes near. It is off by default
(`wg.coldstore.enabled`) and needs stasis on; turning either off restores every stored grid, one a tick.

A grid is stored only if all of these hold: stasis policy Ownerless and fully in stasis for the idle time; not pinned
or in FTL; on an unpaused map; not docked; at rest (under 0.1 m/s and 0.01 rad/s, so a drifting wreck stays in
stasis); no viewer or announced arrival within `wg.coldstore.restore_range` (600 m) of its bounds and no other grid
within half of it; nothing aboard with a mind or a player; nothing aboard that won't save (`MapSavable: false`) but
engine sounds and despawning effects, so any mob, corpse, mech or AI holo makes it ineligible; at least
`wg.coldstore.min_entities` (150) entities; and nothing outside holding its uid. That last one covers worldgen debris
(`OwnedDebris`), contract wrecks and their items, salvage claims, encounter deeds and squatters, game rules
(`RuleGrids`, bluespace errors), Shipmaster event targets, corruption, NPC pilots docking with or following it and
autodock targets; magnets, drydocks, ropes, beams, hacks and hull retirement pin the grid instead. Other systems can
veto with `WGColdStoreCheckEvent`. A grid whose save fails is put back and never tried again that round.

Storing is one grid at a time: the hull is parked and copied in slices by `WGHullRetireSystem.CopyParked` (nothing is
ever stripped), the YAML is written and gzipped on a worker and kept by an `IWGColdStoreBackend` (in memory for now),
then the stand-in is placed, `WGGridStoredEvent` is raised and the hull is deleted in slices. Any throw or serializer
error aborts. The stand-in sits at the centre of the grid's bounds with `WGStoredGridComponent`, a static body with no
fixtures and a `RadarBlip` the size of those bounds in the grid's IFF colour (none for a hidden grid); it shows no
label or tile outline. Space and grid cleanup leave it alone.

A stand-in is restored, nearest first and one a tick, by a viewer or arrival within the restore range, another grid
within half of it, a blast near it, `wg_coldstore restore`, or storing being turned off. The grid is loaded onto a
paused staging map in one tick, run through `UsedShipReinitSystem.ReinitLoadedShip` (device net, wires, nav map,
shields and the other map-init state a load skips), placed, nudged off any grid now in the way, held awake 60 s and
`WGGridRestoredEvent` is raised. A failed load keeps the stand-in and bytes, logs an error and is retried once a minute,
three times. A grid that grid cleanup would have deleted keeps cleanup's clock while stored, reset by a player near;
when it runs out the stand-in and bytes are deleted. Records are dropped at round end.

Known losses on a round trip: gas in pipe nets, wire layouts (redone by the re-init), turret links to hardpoints and
gunnery, a few links that aren't data fields, sounds playing, and radiation suit hoods, which a load deletes for a
reason not yet known. Power, breakers, doors, lockers, stacks, damage, solutions, tile air, canisters, decals, device
links, access and names come back.

Gauges: `wg_coldstore_grids`, `wg_coldstore_entities`, `wg_coldstore_bytes`, `wg_coldstore_worst_restore_ms`, and
counters for stores, restores, aborts, failed loads and expiries; `wg_entcensus` shows the stored totals.
`wg.coldstore.max_restore_ms` (50) only marks slow restores in the log, as a load can't be split yet.
`wg_coldstore` (Debug) lists stored grids, `wg_coldstore store <grid>` stores one now (skipping only the idle time) or
says why not, and `wg_coldstore restore <stand-in|all>` brings them back.

## Limits

- The entity count becomes proportional to players, not zero. 150 players spread over ships and stations still hold a
  few hundred thousand live entities.
- It does not make fights cheaper. Physics and state sending per player are separate work.
- Wrecks, derelicts and abandoned ships are stored first. Player ships with their owner offline follow once
  round-trips are proven lossless; a ship that fails to restore exactly is worse than a slow server.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WG/ColdStorage/CleanupPressure.cs`](CleanupPressure.cs)
- [`Content.Server/_WG/ColdStorage/GridCleanupSystem.WGColdStore.cs`](GridCleanupSystem.WGColdStore.cs)
- [`Content.Server/_WG/ColdStorage/SalvageSystem.WGStasis.cs`](SalvageSystem.WGStasis.cs)
- [`Content.Server/_WG/ColdStorage/WGColdStore.cs`](WGColdStore.cs)
- [`Content.Server/_WG/ColdStorage/WGColdStoreCommand.cs`](WGColdStoreCommand.cs)
- [`Content.Server/_WG/ColdStorage/WGColdStoreCVars.cs`](WGColdStoreCVars.cs)
- [`Content.Server/_WG/ColdStorage/WGColdStoreSystem.cs`](WGColdStoreSystem.cs)
- [`Content.Server/_WG/ColdStorage/WGColdStoreSystem.Eligibility.cs`](WGColdStoreSystem.Eligibility.cs)
- [`Content.Server/_WG/ColdStorage/WGColdStoreSystem.Restore.cs`](WGColdStoreSystem.Restore.cs)
- [`Content.Server/_WG/ColdStorage/WGEntityPressureSystem.cs`](WGEntityPressureSystem.cs)
- [`Content.Server/_WG/ColdStorage/WGGridStasis.cs`](WGGridStasis.cs)
- [`Content.Server/_WG/ColdStorage/WGGridStasisSystem.Attendance.cs`](WGGridStasisSystem.Attendance.cs)
- [`Content.Server/_WG/ColdStorage/WGGridStasisSystem.cs`](WGGridStasisSystem.cs)
- [`Content.Server/_WG/ColdStorage/WGGridStasisSystem.Exemptions.cs`](WGGridStasisSystem.Exemptions.cs)
- [`Content.Server/_WG/ColdStorage/WGGridStasisSystem.Policy.cs`](WGGridStasisSystem.Policy.cs)
- [`Content.Server/_WG/ColdStorage/WGGridStasisSystem.Wakes.cs`](WGGridStasisSystem.Wakes.cs)
- [`Content.Server/_WG/ColdStorage/WGPressureCVars.cs`](WGPressureCVars.cs)
- [`Content.Server/_WG/ColdStorage/WGStasisCommand.cs`](WGStasisCommand.cs)
- [`Content.Server/_WG/ColdStorage/WGStasisCVars.cs`](WGStasisCVars.cs)
- [`Content.Server/_WG/ColdStorage/WGStoredGridComponent.cs`](WGStoredGridComponent.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WG/ColdStorage/ColdStoreTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ColdStorage/ColdStoreTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ColdStorage/EntityPressureTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ColdStorage/EntityPressureTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ColdStorage/GridRoundTripAudit.cs`](../../../Content.IntegrationTests/Tests/_WG/ColdStorage/GridRoundTripAudit.cs)
- [`Content.IntegrationTests/Tests/_WG/ColdStorage/GridStasisHooksTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ColdStorage/GridStasisHooksTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ColdStorage/GridStasisTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ColdStorage/GridStasisTest.cs)

### Localization

- [`Resources/Locale/en-US/_WG/ColdStorage/cold-storage.ftl`](../../../Resources/Locale/en-US/_WG/ColdStorage/cold-storage.ftl)

## Non-modular edits

- [`Content.IntegrationTests/PoolManager.Cvars.cs`](../../../Content.IntegrationTests/PoolManager.Cvars.cs): tests look at grids nobody is near; stasis tests turn it on
- [`Content.Server/_Mono/Cleanup/GridCleanupSystem.cs`](../../_Mono/Cleanup/GridCleanupSystem.cs)
  - tighter under entity pressure
  - sooner under entity pressure
- [`Content.Server/_Mono/Cleanup/MobCleanupSystem.cs`](../../_Mono/Cleanup/MobCleanupSystem.cs): tighter under entity pressure
- [`Content.Server/_Mono/Cleanup/SpaceCleanupSystem.cs`](../../_Mono/Cleanup/SpaceCleanupSystem.cs): radii shrink as entity pressure climbs
- [`Content.Server/_Mono/FireControl/FireControlSystem.cs`](../../_Mono/FireControl/FireControlSystem.cs): paused guns of a grid in stasis still count
- [`Content.Server/_NF/Shipyard/Systems/ShipyardSystem.Consoles.cs`](../../_NF/Shipyard/Systems/ShipyardSystem.Consoles.cs)
  - the console query below skips paused consoles
  - the sale prechecks look at the hull's items, which are paused in stasis
- [`Content.Server/_NF/Shipyard/Systems/ShipyardSystem.cs`](../../_NF/Shipyard/Systems/ShipyardSystem.cs)
  - the organics check, appraisal and clean-up need the hull awake
  - a deed on a paused holder still counts
- [`Content.Server/Salvage/SalvageSystem.Magnet.cs`](../../Salvage/SalvageSystem.Magnet.cs)
  - split pieces are salvage too
  - the magnet's grid may sleep again
  - salvage stays out of stasis until the magnet lets go
  - the magnet's own grid stays out of stasis too
- [`Content.Server/Shuttles/Systems/ShuttleConsoleLockSystem.cs`](../../Shuttles/Systems/ShuttleConsoleLockSystem.cs): a deed on a paused holder still counts

<!-- WOLFGATE-GENERATED END -->
