# Outposts implementation plan

The step-by-step order for building `OUTPOSTS_DESIGN.md`. The design doc says what; this says in which order, in
what size pieces, and when the game is in a shippable state in between. Phase numbers (F0 to F10) are the design
doc's; every step below is one pull request inside a phase.

Rules for every step:

- One PR, mergeable on its own, reviewed on its own. Target under 1,500 changed lines; split anything bigger.
- Behind a cvar (`wf.outposts.*`) or invisible to players until the step that turns it on, so `main` stays playable.
- Ships with its integration test (never `Destructive`), its loc strings and its README section.
- Marked upstream edits only where the design doc's hooks table names one. New behaviour goes in `_WF` systems.
- Nothing in a later step is allowed to be a prerequisite for merging an earlier one. If it is, the order is wrong.

Sizes: **S** under 500 lines, **M** 500 to 1,500, **L** 1,500 to 3,000 (split it if you can).

## Milestones (stop points)

The plan has four places where you can stop and the feature is coherent:

| Milestone | After step | What players get |
|---|---|---|
| **M1 Persistent base** | F1.3 | Found an outpost with a console, build on it with stock construction, it comes back next round for parts + 50% |
| **M2 Outpost economy** | F2.4 | Kit, fabricator, prefab building, own power, trade panel with parachute drops. No ship needed once you are down |
| **M3 Live there** | F4.2 | Spawn at your outpost with your Outpost Job, friends join through positions. Ship access overhaul shipped on the way |
| **M4 Something happens** | F8.3 | Bounded worlds with POIs to walk to, night raids that breach, early warning, turrets that only shoot raiders |

Everything after M4 (crash start, play groups, pads, farming, orbital defence, jetpacks, basements) is additive.

## Order

For one or two developers, in this order. Steps on the same line can run in parallel because they do not touch the
same files.

1. F0.1, F0.2, F0.3 (spikes, in parallel)
2. F1.1, then F1.2
3. F1.3
4. F1.4, F1.5 in parallel with F3.1 (ship access, ships only)
5. F2.1, F2.2, F2.3, F2.4 in order, in parallel with F3.2, F3.3
6. F1.6 (sale), F3.4 (outposts adopt ship access)
7. F4.1, F4.2, F4.3 in order, in parallel with F2.5, F2.6
8. F7.1 (bounds), F6.1 (landing guard) in parallel
9. F8.1, F8.2, F8.3 in order, in parallel with F7.2, F7.3
10. F4.4 (presets), F5.1, F5.2, F5.3
11. F6.2 (pads), F7.4, F7.5, F9.1, F9.2, F9.3
12. F10.x as wanted

## F0: Spikes

No player-facing change. Each spike is a branch with a test or a benchmark and a paragraph of results pasted into
the design doc's open questions. Merge the tests where they are cheap to keep.

| Step | What | Done when | Size |
|---|---|---|---|
| F0.1 Breach spike | An NPC on natural ground next to a small `ForceAnchor` grid with a wall and a door. Measure whether stock HTN pathing reaches the grid at all, then prototype the WF "breach" operator (target the nearest wall or door of a grid on another navmesh, walk to it, attack it). This decides whether separate-grid outposts can be raided | A test where an NPC starting 20 tiles out breaks a prefab wall and stands on the grid | S |
| F0.2 Save benchmark | Build a 1,600 tile, 4,000 entity grid in a test, `TrySaveGrid` to string, compress, `TryLoadGrid` back. Record ms and bytes at 500 / 1,000 / 2,000 / 4,000 entities | Numbers in the design doc's Performance budget, caps set from them | S |
| F0.3 Roof and outdoors | `RoofComponent` on a ground grid instead of `ImplicitRoofComponent`, walls with `IsRoof`, a test roof panel, `IsRooved` per tile, weather overlay and thunder behaviour on and off roof | A test that a roofed tile is rooved and an open one is not, screenshot of the overlay | S |
| F0.4 Client YAML load | `MapLoaderSystem.TryLoadGrid(MapId, TextReader, ...)` from client code with a string, checked against a real client log for `Sandbox violation` | A yes or no in the design doc's Preview section | S |

## F1: Outpost grid, save, load, sale

Module `Outposts`. The spine; everything else hangs off it.

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F1.1 Console and grid | `WFOutpostComponent`, `WFOutpostConsoleComponent`, the console entity (bought at WF traders for 6,000, no kit yet), deploy on natural ground founds a 3x3 grid with `ForceAnchor` + `PreventGridAnchorChanges` + `ShuttleSystem.Disable`, ownership by `NetUserId`, one live outpost per player, name (30 chars), `CleanupImmune`, destroying the console unanchors, the Overview tab with name and owner, cvar `wf.outposts.enabled` | Test: found, name, second console refused, destroy frees the slot and the grid moves | M | Planets branch merged |
| F1.2 Foundation and ground | Foundation plates (own item, own AfterInteract, extend the grid, carve and reserve the ground tile), ground carving on found, `RoofComponent` and the roof panel from F0.3, claim zone (bounding box + 8) that blocks other founders and raw-ground building by non-crew, size cap counters on the Overview tab, weather strike and fauna site skip grid-covered tiles (Planets marked edits), Carcinoma exemption | Test: plates extend the grid and not the ground map, claim zone refuses a second founder, thunder never lands under a grid | M | F1.1 |
| F1.3 Save and load | Migration `WolfgateOutposts` and `wf_outpost_save`, `IServerDbManager` API, profile id lookup, the save predicate v1 (players, mobs, cash, deeds, ghost roles), appraisal with the predicate plus plate value, Save tab (save to 3 slots, delete, list with price), staging map load, strip on the copy, clearance v1 (other grids' tiles, claim zones, stale state removed), price = appraisal x 1.5 via session `TryBankWithdraw`, 30 s overlay with cancel and refund, re-check, clear ground, swap, `ReinitLoadedShip` + outpost re-init, "in use this round" | Test: round trip of a small outpost with a locker of items, battery charge and a door; strip list gone; price charged once; blocked load refunds | L, split into F1.3a (DB + save + Save tab) and F1.3b (load pipeline) if it passes 1,500 lines | F1.2, F0.2 |
| F1.4 Save hardening | Autosave (10 min, skip when unchanged, staggered), final autosave at round end, size cap refusal with the "what is big" list, lineage id on every save, save stamps (`WFSaveOriginComponent`; deposit box and Mono persistent items refuse them), character editor Outposts tab (metadata list, delete), admin commands (`wfoutpost list/restore/refund/delete`, admin log types) | Test: autosave skips an unchanged outpost; a stamped item is refused by the deposit box | M | F1.3 |
| F1.5 Preview | Server decompresses and sends one save's YAML to its owner on request, rate limited; `ShipPreview` gets a `TextReader` overload; preview in the Save tab and the editor tab; preset grids preview too | Real client log clean of `Sandbox violation`; preview renders a saved outpost | S | F1.4, F0.4 |
| F1.6 Sale | Sell tab: sell to the game at the sale predicate's value (structures and machines, not goods), shuttle-style checks, "sell at round end" toggle with the `wf_outpost_payout` table for offline owners, sell to a player standing at the console (price, accept, transfer), any sale voids the lineage, Abandoned state with owner-only re-founding | Test: sale pays the predicate value, voids every save of the lineage, buyer owns the grid | M | F1.4 |

**M1 lands at F1.3.** Between F1.3 and F2, an outpost is built with stock walls, stock generators and whatever the
owner hauls down by ship. That is fine; it is a persistent base already.

## F2: Kit, fabricator, gizmos, trade

Module `Outposts` (gizmos may get a sub-folder per category).

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F2.1 Deploy and repack framework | `WFDeployableComponent` (use in hand on the tile in front, 2 to 4 s, only on your own foundation, console excepted), repack verb (owner and Builders, 3 s, the machine itself goes into the pack's container so all state survives), the no-board rule and its test (no `Machine`, `MachineBoard` or `Construction` component on an outpost gizmo), `StaticPrice` = print cost rule and its test, the console and UTH converted to it | Test: deploy, use, repack, redeploy keeps battery charge and contents | M | F1.2 |
| F2.2 Fabricator and construction pack | Outpost Fabricator (`LatheComponent` with a static T1 recipe pack, slow, no upgrades, prints another fabricator but never a console), prefab wall panel, prefab window, roof panel, manual door kit, prefab airlock kit, shutters kit, foundation plate recipe, powered driver, arc welder, the stock parts/boards/electronics pack, recipe disks for T2/T3 (the gate only; disks come later) | Test: every T1 recipe prints and its product's `StaticPrice` is at or above its material cost | M | F2.1 |
| F2.3 Power pack | Wind turbine (outdoors, 5 kPa, storm bonus in windy weather), deployable solar strings with the day/night scaling system, small and large stationary batteries, compact RTG, A.W. fuel generator, the 0.6 scale rule | Test: turbine makes power outdoors and not under a roof; solar output follows `IsNight` | M | F2.1 |
| F2.4 Kit, UTH and trade panel | `WFUniversalTradeHubComponent` (outdoors, 1 kW), Trade tab with the catalogue as cargo-product listings in an outpost group, owner pays with the cargo tax split, 5 in flight, parachute drop from the top air layer onto the UTH with the blocked-pad fallback, the Planet Outpost Kit at the shipyard outfitters and WF traders, add-on packs, the price >= appraisal test over kit, packs and listings | Test: an order lands on the UTH within 5 tiles; kit price is at or above its deployed appraisal | M | F2.2, F2.3 |
| F2.5 Atmos and kitchen pack | CO2 cracker (1 mol/s cap), temperature regulator, wall heater, water and nutrient synthesizers, sustenance dispenser, rations printer, griddle, range, animal feeder (the entity only; pens come in F9) | Test: cracker converts CO2 to O2 and stops at the cap | M | F2.1 |
| F2.6 Comms, logistics and the time/weather console | Telecom relay flatpack, intercom, planet broadcast console with its rate limits, time/weather console reading `TryGetReport` plus the new forecast API (next weather change, storm phase, time to night and to the Active window), floodlight post, multi-cell charger, materials recycler, organics printer, ore silo flatpack, fabrication silo flatpack | Test: the forecast API returns time-to-night that matches `IsNight` flipping | M | F2.1 |

**M2 lands at F2.4.**

## F3: Ship Access Overhaul

Module `ShipAccess`. Independent of F1 until F3.4, so it can start on day one with a second developer. Ships first,
because they exist and outposts do not yet.

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F3.1 Owner and allow list | `WFShipAccessComponent` on the grid (owner, mode Private or Faction, allow list, Locked on by default for new purchases), the hook in `HasShipAccess` before its early returns, readers ensured on anchor, grid change and load, allow-list ids networked so doors predict, the Builder tick, the Access tab on the shuttle console (settings and allow list only), existing verbs mapped onto the model, `ShuttleConsoleLockSystem` and `ShipyardSystem` marked edits | Test: a non-listed player is refused at a door on a private ship; adding them opens it; a stolen card does not | M | none |
| F3.2 Per-door rules | `WFDoorAccessRuleComponent` with the seven rules, the door diagram on the Access tab (reuse the ShipStatus whole-ship view), rules serialise with the grid | Test: a Sealed door refuses the owner at the door and opens from the console | M | F3.1 |
| F3.3 Codes | 4-digit ship and door codes, right click > Enter Code keypad (only offered without access), misses per person per ship, 5 in 10 minutes locks that person out for 15, console alert and admin log | Test: the right code opens, the lockout holds, codes never appear in a client state | S | F3.2 |
| F3.4 Outposts adopt it | Outpost console owner-only, the Access tab on the outpost console, group members and joiners added to the allow list, faction members' outposts start in Faction mode | Test: an allow-listed crew member opens doors but not the console | S | F3.3, F1.3 |

## F4: Outpost Spawn

Module `SpawnOptions` (lobby, round-start handler, picker tab) plus `Outposts` (cryopod, Outpost Job).

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F4.1 Cryopod and Outpost Job | `WFOutpostCryopodComponent` on a Frontier `CryoSleepComponent` pod (unpowered spawn, anchored, max 6 per outpost), the `WFOutpostSettler` job with `setPreference: false`, `JobWFOutpostSettler` role loadout, custom title entry, loadout editing in the editor's Outposts tab, and the smallest way to use them: a "Spawn at my outpost" button on the late-join screen for a player who owns a live outpost with a free pod | Test: a late joiner who owns a live outpost appears in its pod as the settler job with their loadout | M | F1.3, F3.4 |
| F4.2 Spawn Options and round start | Lobby Spawn Options button and panel (Standard, Outpost Spawn; Crash greyed out), round-state pick, `RulePlayerSpawningEvent` handler, footprint reservation before POIs (a no-op until F7), queued round-start loads (one grid per two ticks), players wait in the lobby until their grid lands then `MakeJoinGame`, fallback to Standard with a refund and a message, the checks table, the live-outpost cap cvar | Test: two readied outpost players both land in their pods; a third with no money falls back to Standard | L, split F4.2a (panel and pick) and F4.2b (round-start handler) | F4.1 |
| F4.3 Joinable positions | `WFOutpostStation` config, the outpost becomes a station at load, public slots always 0, `HiddenWithoutOpenJobs`, the WF join handler that opens and closes a slot in one tick, the Outposts tab on the late-join picker, the Outpost Configurator (Spawn Options panel pre-round, Crew tab in-round), positions saved with the outpost, visibility Open / Code / Group only / Private | Test: `joingame` cannot take a position; the WF handler can | M | F4.2 |
| F4.4 Presets | `wfOutpostPreset` prototype, the four preset grids in `Resources/SharedMaps/_WF/Outposts`, MapInit path in the loader, presets in the Save tab and in Spawn at Outpost, the universal preset test | Test: every preset has a console, a pod, a UTH, fits the cap and is priced at or above appraisal x 1.5 | M | F4.2, F2.4 |

**M3 lands at F4.2.**

## F5: Play groups and Shuttle Crash

Module `SpawnOptions`.

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F5.1 Play groups | `WFPlayGroupManager` (in memory, cleared each round), lobby group browser, Open / Join Code / Private with requests and invites (ERT prompt message pattern), one group per player, leader kick, outpost scenario only, members' spawn option locked to the group | Test: a private group's request and accept flow; a member lands in the leader's pod | M | F4.3 |
| F5.2 Shuttle Crash, solo | `wfCrashPool`, world pick (sanctioned, 3 g excluded, Carcinoma opt-in), crash job pool, orbit-layer spawn, `WFCrashSequenceComponent` (console refused, pilots cleared, thrusters off, orbit decay suppressed), the PA sequence with `WFAlertCrash`, `TryDropFromOrbit` at T+2:30 with `EnterLiftLost`, impact clamp, post-impact unlock and the Crashed code, deed to the leader after the crew spawns, sale baseline on the hull, spawn-distance rules against outposts and crash sites | Test: a solo crash player wakes buckled in orbit and is on the ground at about T+3:00 with a hull that has a console | L, split F5.2a (spawn and sequence) and F5.2b (drop, clamp, aftermath) | F4.2 |
| F5.3 Crash multiplay | Groups may pick Crash, seats cap the group, all members spawn buckled, everyone on the allow list of the hull | Test: a group of three lands together with door access | S | F5.1, F5.2 |

## F6: Landing guard and pads

Module `Outposts`, hooks in Planets.

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F6.1 Landing guard | Enter Atmosphere refused over a claim zone with the owner named, transit exit refused over one, a lift-lost hull shifted to the nearest clear ground before touchdown | Test: a hull dropped over an outpost lands next to it, not on it | S | F1.2 |
| F6.2 Landing pads | `WFLandingPadComponent` marker, pad area (5x5 to 21x21) on the outpost grid, orbit capture within 32 m for pilots with access, fit check, scripted descent and launch (lift ignored), `WFPadDockedComponent` (static while parked, touchdown skips the `Smimsh` crush against the outpost), pad access from the access panel, 5 kW while guiding, pad markers on orbit radar | Test: a lift-less hull descends to the pad, sits without crushing it, launches back to orbit | L, split F6.2a (capture and descent) and F6.2b (dock, launch, access, radar) | F6.1, F3.4 |

## F7: Bounded worlds and POIs

Module `PlanetPois`, bounds in Planets.

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F7.1 World bounds | `Bounds` on the surface prototype, the `BiomeSystem` chunk filter hook, the edge ring per world, mobs and items pushed back, air-layer soft wall, Enter Atmosphere and pad descent refused outside, `WFPlanetRadarSystem.Sample` empty outside | Test: a chunk outside the bound never loads; a mob at the edge is pushed in | M | Planets |
| F7.2 POI placer and beacons | `wfPlanetPoiTable`, `wfPoiType`, the placer (after outpost reservations, spacing from the placement rule, gives up instead of returning a bad roll), POI grids as separate grids with `ReserveTiles`, orbit beacons with the PVS override starting as "Unknown signal", reveal at 50 m, the first three types from existing grids: Mine (surface part), Abandoned outpost, Small derelict | Test: a world rolls 8 to 14 POIs, none inside a reserved footprint, each with a beacon | M | F7.1, F4.2 |
| F7.3 Claiming | Claim on a POI console (no hostiles, repaired or hacked as the type says), free, claim baseline stored, half-of-world cap, claimed POIs save and sell like any outpost | Test: claim an abandoned outpost, save it, sale pays only what was added | S | F7.2, F1.6 |
| F7.4 The rest of the taxonomy | Automated outpost (turrets, hackable core), Tradepost (WF Traders, public pad, public UTH, safe zone), Derelict capital ship, Farm, Workshop (recipe disks drop here), Ruins with clue items, Heavenly shrine, Alien artifact site, faint signals, the survey tool | Each type has a grid, a test that it loads, and a beacon behaviour | L across several PRs, one or two types each | F7.3 |
| F7.5 Undergrounds interplay | When the underground layers land: the WF network builder accepts depths below 0, underground chunks under a claim zone generate as unmineable foundation rock, clearance and placement ignore depths below 0, Mine POI claims cover the surface grid only, nothing below depth 0 is saved, raid director gets the "tunneller" arrival that surfaces at the claim edge | Test: a tunnel chunk generated under a claim zone is solid rock; an outpost above an existing tunnel loads and its save is unchanged | M | F7.2, the underground branch |

## F8: Outpost attacks

Module `OutpostRaids`.

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F8.1 Active state and warning | Per-world Active window from `IsNight` with prototype overrides, storm stacking, the Overview and time/weather console countdowns, `WFEarlyWarningComponent` and the mast (120 s, 240 s long-range), PA code Red via `ShipPa`, the 15 s popup without one | Test: the Active window opens at 18:00 local and the EWS fires 120 s before a scripted raid | S | F1.2, F2.6 |
| F8.2 Director and wildlife raids | `WFRaidDirectorSystem` (attention, threat points, gates, caps, grace, one raid per window), `wfRaidTier` and `wfRaidTable`, ladder steps 0 and 1 (wildlife), walk-in spawner (40 to 60 tiles out, 24 from players, inside bounds, away from POIs and crash sites), `SleepPlayerCheckRangeOverride` on raid mobs, the raid chunk loader in Planets, director despawn at dawn and by leash, turrets and mines hostile to raider factions and hostile wildlife, turret cap | Test: with someone home and the window open, a step-1 pack spawns, walks to the claim edge and despawns at dawn | M | F8.1, F7.1 |
| F8.3 Humanoid raiders | The breach HTN operator from F0.1, steps 3 and 4 (scavengers, raider party with a breacher), raider factions, mounted-cosmetic arrival, `WFRaidLootComponent` (`CargoSellBlacklist`, not saved, not paid for), breacher targets turrets first, console never targeted | Test: a raider party breaks a prefab wall, enters, and its loot is not in the next save | M | F8.2 |
| F8.4 Burrows, shuttles, siege | `WFRaidBurrowComponent` with `TimedSpawner` (step 2), the raider shuttle (`WFRaidShuttleComponent`, drop from orbit outside the claim zone, leaves at dawn) (step 5), siege (step 6) | Test: a burrow keeps spawning until destroyed; a raid shuttle lands outside the claim zone | M | F8.3, F6.1 |

**M4 lands at F8.3.**

## F9: Farming

Module `Husbandry`.

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F9.1 Pens, feeders, livestock records | `WFAnimalFeederComponent` (from the F2.5 entity), pen detection (enclosed area with a feeder), 6 per feeder, `WFLivestockComponent`, passive faction, livestock records in the save (prototype, name, age, cooldowns) and respawn at feeders on load | Test: two chickens in a pen are in the record, come back on load, and the appraisal counts them | M | F1.4 |
| F9.2 Capture, taming, herding | Lasso (WF rope leash, 3 s struggle, big animals take two), taming by feeding 3 times in 5 minutes, protected flag so wildlife never retires them, herding whistle (Follow / Stay / Go to pen on HTN follow) | Test: lasso, tame, whistle to pen | M | F9.1 |
| F9.3 Native livestock | Per-world livestock variants in each fauna table with `Udder`, `EggLayer`, `Wooly` and hide, predators take unpenned animals in Active windows | Test: a world's fauna table contains at least one tameable species | S | F9.2, F8.2 |

## F10: Late

Each is its own PR or small series; order by demand.

| Step | Ships | Needs |
|---|---|---|
| F10.1 Atmospheric jetpack | `WFAtmosphericJetpackComponent`, the four `WfInAtmosphere` call sites, the server fuel branch, layer climb and descend, gravity gate at 1.5 g, fuel scaling above 1 g | Planets |
| F10.2 Parts press, power exporter, mining bots | The producers with their home gate and caps, bot HTN and dock | F2.2, F8.2 |
| F10.3 Orbital defence | Sensor uplink, flak battery, orbital defence battery, descent jammer, all against `WFRaidShuttleComponent` only until Q1 is answered | F8.4 |
| F10.4 Basements | An outpost as a set of grids, one per depth, bound by CE grid connectors like a multi-deck ship; the save format gains a depth per grid; a dug-down hatch in the outpost floor becomes a CE ladder onto the owner's depth -1 grid; caps apply to the set | F7.5, F1.4 |
| F10.5 Deposit terminal, broadcast console extras, T2/T3 disk drops in Workshops | | F2.6, F7.4 |

## Rules of thumb while executing

- **When a step grows past L, stop and split it**, even if the split ships a stub UI. A stub Save tab that only
  saves is better than one giant PR.
- **Do not start F4 before F1.4 is in.** Spawn at Outpost is where dupes and refunds go wrong; hardening first.
- **F3 is the parallel track.** Give it to whoever is not on the spine. It touches the shuttle console, doors and
  Mono access, none of which the spine touches.
- **F7.1 before any raid work.** Raids that walk in from infinite terrain never end; the bound is what makes the
  director's caps mean anything.
- **Every step re-runs the price tests.** Kit, packs, listings, recipes and presets all sit under the same
  "price at or above appraisal" test; a new gizmo that breaks it does not merge.
- **The underground branch is not a blocker** for anything before F7.5. Outposts only exist at depth 0 until F10.4,
  and the column rule in F7.5 is a generator change, not an outpost change.
