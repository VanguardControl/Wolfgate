# Outposts

Outposts is the next iteration of planet implementation, and it brings forth the final stages of the whole cycle. Think RIMWORLD.

Outposts aims to introduce buildable outposts, as well as spawning outposts, on planets.

Planets will now be a set size instead of being infinite, allowing for "story generation" style random gen, where there are specific POIs on the map with beacons that players can venture to. These POIs will range from ruins and trade posts all the way to heavenly shrines and alien artifact sites.

Players will have the option to build and save their outpost. Those that wish to start a new one will have to purchase the "Planet Outpost Kit" containing the required building blocks to start their own outpost. The most important part is the "Outpost Console", the outpost version of the shuttle console. It is what determines that the grid is actually an outpost, and it is the command center for all outpost functions.

Outposts will be the first version of our "shipsaving" system, where players will be able to put their Outpost Console down and save from within the console.

After a player has saved their first outpost, a new spawn option becomes available, "Spawn at Outpost", which loads their saved outpost and places them inside it, as long as they have an "Outpost Cryopod" built. More on that in the spawn options section.

Outposts will have access control, defaulting to the player's access (see Ship Access Overhaul; outposts get the same feature).

Outposts are ANCHORED and cannot be moved, even with thrusters put on them, unless you destroy the outpost console, in which case they can be.

Players can only own ONE OUTPOST AT A TIME PER ROUND.

You can set the name of your outpost.

## At a glance

### How to read this doc

Every feature bullet carries a tag:

- **[EXISTS]** already in the fork; the named system is what the outpost hooks, not something we rebuild.
- **[PORT: Nova]** taken from Nova Sector's `colony_fabricator` module (or a sibling Nova module); the `.dm` file is named.
- **[NEW]** has to be built.

Numbers are starting values for tuning. Where a Nova number was rescaled for SS14, the Nova original follows in parentheses; Nova numbers kept as they are carry no note. Money is in spesos. "Band" is the trade-panel price band used across the doc, per unit or per stack as the row says:

| Band | Price (spesos) | Feel |
|---|---|---|
| A | 250 to 1,000 | Consumables, small appliances |
| B | 1,000 to 3,000 | Everyday machines |
| C | 3,000 to 8,000 | Core base machines |
| D | 8,000 to 20,000 | Big upgrades |
| E | 20,000+ | Late game, prestige |

Bands are shop prices, not value. What saves, loads and sales use is the appraisal: every outpost machine's `StaticPrice` is its print cost (material value), and `PricingSystem` appraises from that. A band price is always at or above the appraisal of what you get (tested, see Tests).

Tiers: **T1** recipes are built into the Outpost Fabricator and on the trade panel from the first order. **T2** and **T3** recipes come on recipe disks (band C for T2, band D for T3), bought on the trade panel or looted from Workshop POIs; the finished T2/T3 gizmos can also be bought outright at their band price. The gate is money and exploration, not research.

Note for devs: the planet stack (layers, flight, weather, parachutes) lives on the `Planets-and-cracking` branch, not on `main` yet, under `_WF/PlanetCracker` folders and `WOLFGATE(PlanetCracker)` markers. The planet-cracker itself has been removed; the surviving planet stack is being renamed to the `Planets` module, which is the name this doc uses. Pieces that lived only in cracker folders (the orbit surveyor, chunk pinning) are called out where this doc needs them. Everything here assumes that branch has landed.

### Core rules

- An outpost is **its own grid** sitting on a planet's ground map, not tiles painted onto the ground. Only what is on the outpost grid is part of the outpost (and gets saved). [NEW]
- The Outpost Console founds the outpost: deploying it on natural ground creates a 3x3 foundation grid under it. [NEW]
- Outposts only exist on the ground layer (depth 0) of a planet. The console refuses to deploy in space, on a hull, on an air or orbit layer, or anywhere the placement rule (see Spawn-distance rules) fails. [NEW]
- **The ground under an outpost is carved.** When an outpost is founded, extended or loaded, the planet tiles under its footprint become a no-weather "foundation bed" tile and are reserved (`BiomeSystem.ReserveTiles`), so the biome never regenerates them. Drill holes are the exception: the natural tile under a drill hole is reserved but not carved, so the ore thumper can read it. Without this, ground systems see open sky under the base: lightning spawns inside it and fauna sites pick its tiles. As a second guard, the planet weather strike and the fauna site test also skip any tile covered by another grid (outposts, POIs, landed hulls). [NEW] in Planets, on [EXISTS] `ReserveTiles`
- Anchoring uses `ForceAnchorComponent` + `PreventGridAnchorChangesComponent` + `ShuttleSystem.Disable(force: true)`. The planet stack already treats `ForceAnchor` as "no liftoff, no drift, no orbit decay, no gravity well". [EXISTS]
- Destroying the console removes the anchor (`ShuttleSystem.Enable(force: true)`), so thrusters can move the grid again. [NEW]
- One live outpost at a time per player (`NetUserId`) per round, across all their characters. Selling an outpost frees the slot; an Abandoned outpost still holds it. [NEW]
- Outposts get `CleanupImmuneComponent` so Mono grid cleanup never eats one (planet-parented grids are skipped anyway). [EXISTS]
- Name: set in the console, max 30 characters, same cleaning as ship names; admins can force a rename. The shipyard rename path needs an owning station, so outposts get their own rename. [NEW]
- Until PvP (Q1) is settled, outposts are PvE by default: turrets, mines and orbital weapons target NPCs only, only the owner can re-found an Abandoned outpost, and only the owner and allow-list Builders can repack. Players can still break walls and doors by hand, as the server conflict rules already allow for ships. [NEW]

### The Outpost Console

The console is the outpost's brain. It is only usable by the outpost owner (see Ship Access Overhaul). Tabs:

| Tab | What it does | Tag |
|---|---|---|
| Overview | Name, owner, founded round, power summary, residents online, current alert code, raid threat level, "Active" countdown, size cap usage, unsaved structures in the claim zone | [NEW] |
| Save | Save to a slot, load, delete, autosave status, preview, preset outposts | [NEW] |
| Access | Access panel and door diagram, same as the ship one | [NEW] |
| Trade | Buy gizmos, see orders in flight, UTH status | [NEW] |
| Crew | Outpost Job editor, joinable positions, play group, kick | [NEW] |
| Defense | Early warning status, turret controls, outpost alert code (Green/Yellow/Red via ShipPa) | [NEW] + [EXISTS] `ShipAlertSystem` |
| Sell | Sell now, "sell at round end" toggle, current sale value and what it doesn't pay for | [NEW] |

- The console has an internal battery, so **saving always works**, even with the base unpowered. Trade and defense need base power (500 W). [NEW]
- The console is tough (high structural HP, immune to NPC raider targeting) so a raid can't delete your outpost by accident. Players with explosives can still destroy it; that is how an outpost is made movable. [NEW]
- The console adds an IFF blip and warp point for the outpost on the orbit layer, like `WFOrbitBeacon`, so pilots can find it from orbit. Separate grids on the ground are not drawn by the radar terrain, so without this outposts would be invisible. The beacon carries `CEPvsOverrideComponent` (a global PVS override): the orbit layer has no bound (ships arrive at their own sector position, anywhere within 2 km of the body), and clients only draw IFF beacons they have been sent. Today's `WFOrbitBeacon` has no override, so the existing planet marker likely has the same gap. [NEW] on the [EXISTS] `WFOrbitBeacon` pattern and CE PVS override

### Outpost grids and building

- **Outpost foundation plates** (a tile stack, from the kit and the fabricator): laid on natural ground inside your claim zone and touching your outpost grid, they extend the outpost grid instead of the ground map, and carve the ground tile under them. They are their own `_WF` item with their own AfterInteract handler, not a stock floor tile: stock `FloorTileSystem` refuses any placement within a tile of another grid, and the outpost grid is always that close. No `FloorTileSystem` edit. [NEW]
- Anything anchored on an outpost tile belongs to the outpost grid. Stuff built on raw ground stays "ground": it is not saved and planet systems treat it as terrain. Outpost prefab parts and outpost flatpacks only deploy on your own foundation (except the Outpost Console flatpack, which deploys on natural ground and founds the outpost). Stock construction on raw ground inside your claim zone shows a "not part of the outpost" popup, and the Overview tab counts unsaved structures in the claim zone. [NEW]
- **Roofs and "outdoors"**: roofs on the CE stack only come from tiles on the layer above or mapper markers, and every shuttle grid gets `ImplicitRoofComponent`, which draws the whole grid as roofed. Outpost grids get `RoofComponent` instead. A tile is roofed when `SharedRoofSystem.IsRooved` says so: walls already carry `IsRoof`, and a buildable **roof panel** (an overhead entity with `IsRoof`) roofs a floor tile. "Outdoors" anywhere in this doc means "on the outpost grid and not rooved". It is the one rule for the UTH, wind turbines, solar panels, the ore thumper and drill holes. Weather's `CanWeatherAffect` is not used for it, because floor tiles never take weather. [NEW] on [EXISTS] `RoofComponent` / `IsRoof`
- **Claim zone**: the outpost's bounding box plus 8 tiles. Nobody else can found an outpost, load a save or land a non-pad hull inside it. Inside it, only the owner, crew and allow-listed players can anchor or build on raw ground, so nobody can wall in your doors. It is not an anti-trespass zone: people can walk, parachute or jump in, and trespass is part of the PvP question (Q1). [NEW]
- **Size cap**: 64 x 64 tiles bounding box, 1,600 tiles, 4,000 saved entities, as starting caps; the final numbers come from the save/load benchmark (see Performance budget). The console shows usage. [NEW]
- Carcinoma's hull infestation (`WFCarcinomaInfestationSystem`) would pin and meat-ify an outpost; outposts are exempt (they get more raids there instead), pending Q3. [NEW] on [EXISTS]

## Why an Outpost?

I dunno. It's fun. And you can trade things, and build things more easily. And... you can have a home?

To put that in RimWorld terms:

- **A home.** Your outpost comes back every round, with your stuff in it, where you left it.
- **Easier building.** Tool-less prefab walls, flatpacked machines that deploy in seconds and repack with a right click, a fabricator that prints without research.
- **Trade.** The trade panel parachutes supplies in, so you don't need a ship to keep going.
- **Stories.** Night raids, crash landings, a world with ruins and shrines to find, animals to tame.
- **A crew hub.** Friends spawn in your cryopods; your outpost is where the play group lives.
- **A money sink that pays back.** Loading costs parts + 50%, so an outpost is an investment, and you can sell it for its value.

## The Planet Outpost Kit

The starter crate. Modelled on Nova's "Colonization Starter Kit" (`cargo_packs.dm`: fab, organics printer, GPS beacon, 50 plastic wall panels, 25 rods, 20 iron, 2 manual airlocks, APC set, battery, 11 crate values). Nova's kit has no generator; ours ships one, because a dead console on night one is no fun. Nova's organics printer is left out on purpose (ours is T1 / B on the trade panel from the first order, see Fabrication), and so is the GPS beacon (the console's own beacon covers it).

| Item | Qty | Tag |
|---|---|---|
| Outpost Console (flatpack) | 1 | [NEW] |
| Universal Trade Hub (flatpack) | 1 | [NEW] |
| Outpost Fabricator (flatpack) | 1 | [PORT: Nova] `colony_fabricator.dm` |
| Outpost Cryopod (flatpack) | 1 | [NEW] |
| Outpost foundation plates | 60 (2 stacks of 30) | [NEW] |
| Prefab wall panels | 50 (2 stacks of 25) | [PORT: Nova] `construction/turfs.dm` |
| Roof panels | 30 (1 stack) | [NEW] |
| Manual door kits | 2 | [PORT: Nova] `manual_door.dm` |
| Prefab airlock kit | 1 | [PORT: Nova] `doors.dm` |
| Wind turbine (flatpack) | 2 | [PORT: Nova] `wind_turbine.dm` |
| Small stationary battery (flatpack) | 1 | [PORT: Nova] `power_storage_unit.dm` |
| Wall substation + APC set and electronics | 1 | [EXISTS] stock construction |
| Cable coils (HV, MV, LV) | 1 each | [EXISTS] |
| Steel sheets / glass sheets / rods | 30 / 20 / 30 | [EXISTS] |
| Rations printer (flatpack) | 1 | [PORT: Nova] `foodricator.dm` |
| Emergency air canister | 1 | [EXISTS] |
| Planet timepiece | 1 | [EXISTS] `WFPlanetTimepiece` |
| Parachute (to drop the kit from a ship) | 1 | [EXISTS] `WFParachute` |

- **Price: 25,000 spesos** (band E). The kit is a bundle: cheaper than buying each piece on the trade panel, never below the appraisal of its deployed contents. Sold at the shipyard outfitters and WF trader vendors, and at tradepost POIs. A bare Outpost Console is sold on its own for 6,000 (band C) for people loading a save; the console can't be printed. [NEW] on [EXISTS] WF Traders
- Add-on packs on the trade panel, modelled on Nova's "Frontier Kitchen Equipment" (1,000 cr) and "Hydroponics Plumbing Synthesizer Pack" (400 cr): Kitchen Pack, Farm Pack, Atmos Pack (CO2 cracker, scrubber, temperature regulator, 2 wall heaters), Defense Pack (2 turrets, control panel, early warning), Power Pack (4 solar, large battery). Each pack is priced at or above the appraisal of its contents. [NEW] bundles

## Outpost Saving / Loading

Outposts can be saved at any time via the Outpost Console, and are autosaved every 10 minutes into an autosave slot.

You save/load your outpost in the Save tab of the console. Saved outposts are linked to your current character and cannot be transferred. You can view your character's saved outposts in the character customiser. A character can have up to 3 outposts saved, plus 1 autosave. You can delete saves in the save/load menu.

This tab shows a preview of your outpost (like the ship previewer).

### What a save is

- A save is the outpost grid serialised to YAML, the same way the used ship market does it (`ShipyardSystem.TrySaveShip`: `TrySaveGrid` with `MissingEntityBehaviour.Ignore`, docks undone first). [EXISTS]
- Saves live in **their own DB table** with a foreign key to the character's DB profile id (cascade delete). This is unlike `SafetyDepositBox`, which is keyed on user id + character slot with no foreign key. `HumanoidCharacterProfile` doesn't carry its DB id, so the server resolves (user id, slot) to a profile id through a new `IServerDbManager` call, cached per session; the profile row is updated in place on save, so the id is stable. Saves are **not** stored as Mono persistent profile items: those are loaded for every character at connect, sent to the client in every prefs message and rewritten on every bank change. [NEW]
- Each save stores metadata next to the YAML: name, planet surface id, world position and rotation of the console, console position inside the grid, appraisal at save time, tile count, entity count, footprint, lineage id, format version, round id. [NEW]
- **Lineage**: every outpost has a lineage id, written on every save made from it. It is how a sale voids old copies (see Outpost ownership and sale). [NEW]

### Saving pipeline

1. Owner presses Save (or the autosave timer fires). [NEW]
2. **Save predicate**: one shared function decides what goes into a save and what the appraisal counts (the table below). The same function drives the strip at load and the appraisal, so the price always matches what loads. [NEW]
3. Livestock in pens are written as **livestock records** (prototype, name, infant or adult, product cooldowns) instead of mob YAML, because mobs are not map-savable. [NEW]
4. `TrySaveGrid` to an in-memory string, compressed. `TrySaveGrid` has no per-entity filter: whole prototypes that are never saved (cash stacks) are switched off `MapSavable` for the length of the write (the reverse of `TrySaveShip`, which switches carried mob and mech prototypes on), and everything else the predicate rejects is stripped from the loaded copy at load (the used ship market's `StripForResale` pattern). Refuse if over the size cap (2 MB compressed) and tell the owner what is big. [EXISTS] + [NEW]
5. **Appraise** with `PricingSystem.AppraiseGrid` (recurses containers) plus a per-tile value for foundation plates (the appraiser ignores tiles), passing the save predicate. Without it the appraiser counts every mob on the grid (every humanoid has a 1,500 `MobPrice`) and everything they carry. Store the number. [EXISTS] + [NEW]
6. Write the row asynchronously. The console shows "Saved" with the load price. [NEW]

**Left out of a save (and of its appraisal):**

| Thing | Why |
|---|---|
| Players, any mob, and everything they carry | Bodies don't persist; your gear isn't the outpost's |
| Ghost role spawners and ghost role mobs | No free roles from a save |
| Cash (spesos stacks) and anything with a bank link | Money lives in the bank, not in lockers |
| ID cards carrying ship deeds, vouchers | Deeds are round-local |
| Trade crates, bounty items, `CargoSellBlacklist` items | They would dupe cargo income |
| Timed-despawn items, audio, projectiles | Junk |
| Raider loot flagged `WFRaidLoot` | Stops raid-loot farming across rounds |

Everything else stays: machines, items in lockers, silo contents, gas in pipes, charge in batteries, fuel in generators.

### Autosave

- Every 10 minutes per outpost, into the autosave slot (slot 3 in the DB, which counts from 0), skipped when nothing changed since the last autosave (same entity count, tile count and appraisal). Timers are offset per outpost, and the server does at most one autosave every 5 seconds, so twenty outposts never save on the same tick. [NEW]
- A final autosave runs on `RoundEndedEvent`, unless the outpost is being sold at round end. [EXISTS] event, [NEW] handler
- If the console is destroyed, autosave stops and the slot keeps the last good save. [NEW]
- Autosave is free. Manual saves are free. Loading is what costs money. [NEW]

### Limits and rules

- 3 manual slots + 1 autosave per character. Saving over a slot asks for confirmation. [NEW]
- Saves are per character, shown in a new **Outposts** tab in the character editor (list, planet, price, last saved, preview; the Outpost Job loadout is edited here too). The Mono "saved items" tab already previews saved YAML on the client, so the pattern exists. [NEW] on [EXISTS]
- Deleting a character deletes its saves (cascade). Deleting a save hides it from the player; admins can restore it (see Admin tools). [NEW]
- Saves cannot be moved to another character or player. A **live** outpost can be sold to another player (see Ship Access Overhaul); the buyer then saves it into their own slots. [NEW]
- A save that is **live this round** is marked "in use" and cannot be loaded a second time this round (the SafetyDepositBox "withdrawn round" trick). [NEW]
- Any sale of an outpost voids every save of its lineage: they stay listed as "Sold" but can't be loaded. [NEW]
- **Save stamps**: every entity that comes out of a loaded save carries `WFSaveOriginComponent` (lineage id). The safety deposit box and Mono persistent items refuse stamped entities, so an item can't sit in a stale save slot and a deposit box at once. A full persistent-item ledger is Q23. [NEW] on [EXISTS] `SafetyDepositStoredComponent` pattern

### Preview

- The Save tab and the character editor preview a save in the ship previewer (`Content.Client/_WF/ShipPreview`). The previewer only loads file paths today, so it needs a `TextReader` overload; the map loader already has one. [EXISTS] + [NEW]
- The server decompresses the save and sends plain YAML only to its owner, on request, size-capped and rate-limited, so the client only needs `StringReader` and `MapLoaderSystem.TryLoadGrid(MapId, TextReader, ...)`. `StringReader` is already used in client code (the saved-items tab, with `TryLoadEntity(TextReader)`); the `TryLoadGrid(MapId, TextReader, ...)` overload is not yet (client grid loads pass paths), so F1 checks it with a real client log for "Sandbox violation". The lobby list gets metadata only. [NEW]

### Loading a Saved Outpost

In order to load your previously saved outpost you must first place down an outpost console (bought, or from a kit), then go to the Save tab and click Load. Each save is priced as the sum of the value of all of its parts + 50%. When you press Load, it is deducted from your account (if you have enough funds). The game then checks the area for any non-planetary turfs (to prevent griefing by loading over other grids), makes sure it's safe to load, and gives a 30 second warning to clear the area (a ghost overlay shows). Then the grid loads, with the outpost console exactly where it is in your saved grid, making it the "anchor point".

Step by step:

1. **Place a console** on natural ground, where the placement rule allows. It founds a fresh 3x3 outpost. Load only works from a fresh console (nothing built on it yet). [NEW]
2. **Rotate to aim.** The saved outpost is placed so its console lands on your console's tile, facing the same way. Rotating the placed console rotates the whole load in 90 degree steps. [NEW]
3. **Press Load.** Price = stored appraisal x 1.5. Charged with the session form of `BankSystem.TryBankWithdraw` (works from the lobby too). Refused if you can't afford it. [EXISTS] + [NEW]
4. **Stage.** `TryLoadGrid` the save onto a paused holding map (the used ship market loads onto `ShipyardMap` the same way). On the staged copy: strip what the save predicate rejects (`StripForResale` pattern), and remove stale z-level and flight state the save carried (`CEZGridFallerComponent`, `CEZPhysicsComponent`, `WFSkidComponent`; the liftoff and lift-lost components are already unsaved), so the CE registration on grid add builds fresh ones with their grace period. Then re-appraise with the save predicate. If prices rose since the save, the difference x 1.5 is charged too; if you can't cover it, the load aborts with a full refund. [EXISTS] pattern + [NEW]
5. **Clearance check** on the footprint (every staged tile, translated to the target): [NEW]
   - every tile is natural planet ground (no tile of another grid: hull, outpost, POI);
   - no lava or deep water: these are biome entity layers, not tiles, so loaded chunks are checked with an anchored-entity lookup and unloaded chunks by sampling the biome recipe (`WFPlanetRadarSystem.SampleFeature`);
   - inside the planet bounds;
   - the placement rule (see Spawn-distance rules): outside other claim zones by 150 m, 100 m from POIs (unless claiming that POI), 100 m from crash sites, 64 m from the world edge;
   - you own no live outpost other than this console's;
   - the server and world are below the live-outpost cap.
   A failed check shows the offending tiles in red and refunds in full.
6. **30 second warning.** A ghost overlay of the footprint appears for everyone nearby, with a countdown and a popup for anyone standing inside it. The owner can cancel for a full refund. [NEW]
7. **Re-check at T-0.** If a grid moved in, abort and refund. [NEW]
8. **Clear the ground.** Mobs inside the footprint are pushed to the nearest edge tile (never gibbed). Loose items are moved to the edge. In loaded chunks, anchored biome entities (rocks, trees) in the footprint are deleted. Then the footprint tiles are carved to foundation bed and reserved (`BiomeSystem.ReserveTiles`, the `GroundGrid` precedent); for chunks that aren't loaded yet, reserving is enough, since generation skips reserved tiles. [EXISTS] + [NEW]
9. **Swap.** Delete the fresh 3x3 grid, move the staged grid onto the ground map at the computed offset and rotation. [EXISTS] pattern
10. **Re-init.** A loaded save comes back "initialised" but skips every map-init handler, so run `UsedShipReinitSystem.ReinitLoadedShip` (device networks, wires, charge, PA speakers, nav map) plus outpost extras: apply `ForceAnchor` explicitly, `CleanupImmune`, `RoofComponent`, ownership, access binding and readers, livestock respawn at feeders, IFF beacon. [EXISTS] + [NEW]
11. Mark the save "in use this round". [NEW]

### Preset outposts

There will also be some preset base game outposts that you can spawn for their set price, or you can just build your own from scratch.

- Presets are prototypes (`wfOutpostPreset`) with a grid in `Resources/SharedMaps/_WF/Outposts` (client builds drop `Resources/Maps`, and the previewer must load them). [NEW]
- They show up in the Save tab under "Presets" and in the Spawn at Outpost list, and load through the same pipeline at their fixed price, with one branch: a preset grid is a map file that has never been map-initialised, so it gets normal MapInit on arrival (lockers fill, `ForceAnchor` MapInit fires) and **no** `ReinitLoadedShip`. Saves get re-init and the state reset, and no MapInit. One load API, tested both ways. [NEW]
- The universal vessel/outpost test checks every preset: price >= appraisal x 1.5, has a console, at least one cryopod, a UTH, fits the size cap. [NEW]

| Preset | Contents | Cryopods | Price band |
|---|---|---|---|
| WFOutpostHomestead | 7x9 sealed hab, 4 wind turbines, small battery, fabricator, rations printer, 4 hydro trays, UTH | 2 | E (about 40,000) |
| WFOutpostMiningCamp | Hab, arc furnace, ore silo, ground drill on a drill hole, ore boxes, 2 solar strings, UTH, landing pad | 3 | E (about 60,000) |
| WFOutpostTradepost | Hab, big storage, landing pad, UTH, telecom relay, 2 turrets | 4 | E (about 75,000) |
| WFOutpostFortified | Walled compound, 4 turrets + control panel, early warning mast, RTG, battery bank, pen | 6 | E (about 110,000) |

## Spawn Options

Players can now pick how they start the round, which overrides how they spawn in entirely.

There is a spawn menu (new **Spawn Options** button in the lobby, next to Ready) that lets you configure each spawn type (see the options per spawn option below). Pre-round you can select and set up your Outpost / Shuttle Crash scenarios.

"Spawn at Outpost" shows your outposts that can be played, plus the base maps (presets) to start with, costing the amount from your bank account. It appears once your character has saved their first outpost; before that, presets load from a placed console's Save tab.

### Lobby flow

1. Open Spawn Options. Pick Standard (default), Shuttle Crash or Outpost Spawn. The choice is kept in new server state for this round, **not** in the profile's `SpawnPriority` (that field is DB-stored and marked "do not touch"). [NEW]
2. Configure: for Outpost Spawn pick a save or preset, see price, planet and outpost slots left, and set up joinable positions (the Outpost Configurator, see Multiplay); for Crash, nothing to pick (it is random), just group size. Both have an "allow unsanctioned worlds" tick. [NEW]
3. Optionally create or join a Play Group. [NEW]
4. Press Ready. The lobby shows your pick under the Ready button. [NEW]
5. At round start, the server handles non-standard picks in `RulePlayerSpawningEvent` (broadcast, after maps and rules load, before job assignment; the documented contract is "take players out of the pool and join them yourself", exactly what antag selection does). Crash starts are spawned there and then. Outpost Spawn players are taken out of the pool and stay in the lobby, still readied, on an "Arriving" countdown; their grids load from a queue after job assignment, and each owner and group joins with `GameTicker.MakeJoinGame` once their grid is in. Anyone whose pick fails falls back to Standard, with a chat message saying why and a refund. [EXISTS] event + [NEW] handler

### What the server checks

| Check | Outpost Spawn | Shuttle Crash |
|---|---|---|
| Player is ready and prefs are loaded | yes | yes |
| Save belongs to the selected character and is not in use this round | yes | n/a |
| Player doesn't already own a live outpost this round | yes | n/a |
| Live outposts are below the cap (server and world) | yes, before charging | n/a |
| Target planet exists this round and is built; unsanctioned only if the player ticked "allow unsanctioned worlds" | yes | picks one |
| Balance >= load price + billed loadout | yes | n/a |
| Footprint fits at the saved position, or a valid spot within 300 m | yes | n/a |
| A crash site exists that meets the spawn-distance rules | n/a | yes |
| Group members are ready, and group size fits cryopods / seats | yes | yes |
| Job bans: the Outpost Job and crash jobs go through `GetDisallowedJobsEvent` | yes | yes |

Round-start outpost loads are queued one grid at a time, spaced by what the save/load benchmark measures, so twenty outposts don't hitch the first second of the round. A single large load still blocks the tick it runs on; the benchmark sets the size cap so that stays within budget.

### Multiplay Spawn Options

Some spawn options have Multiplay. You can set up your play group to be joinable pre-game, so another player can configure themselves to join your outpost or crash landing at roundstart, if you allow it. You can also invite a player to join. They'll be set up with the same access to doors and such (they are added to the access list).

You can create **Play Groups**, essentially sub-lobbies of the game that players can join to be part of your multiplay group. You can set a group to Open / Join Code / Private (requests are sent to you as a notification pre-roundstart, and you can directly invite a connected player).

In the Outpost Configurator (the Outpost Spawn panel of Spawn Options before the round, and the console's Crew tab during it) you can also set up joinable positions for your outpost, which again other players can join; they spawn at the outpost cryopods.

For the crash landed start, players can only join your play group in the lobby before the round starts, and they spawn on the ship with you.

**Play Groups in detail** [NEW]:

- A registry keyed by `NetUserId`, in memory, cleared on `RoundRestartCleanupEvent`. Nothing like it exists (`_Rat` squads are in-round and entity-based; Mono Company is a faction). [NEW]
- Fields: leader, name, mode (Open / Join Code / Private), 6-character join code, scenario (Crash, or Outpost + save id), max size, member list with each member's "allow unsanctioned worlds" tick, pending requests, pending invites. [NEW]
- **Open**: anyone in the lobby sees it in the group browser and joins. **Join Code**: hidden, joined by code. **Private**: listed in the browser as request-only; players send a request (the leader gets a lobby notification), or the leader invites any connected player. [NEW]
- Max size: Crash 6 (and never more than the picked shuttle's seats); Outpost = number of outpost cryopods in the save. [NEW]
- Members' own spawn option is locked to the group's scenario while they are in it. One group per player. [NEW]
- The leader can kick a member at any time; a kicked member loses their allow-list entry at once. [NEW]
- If the leader isn't ready at round start, the group falls back to Standard. Members who didn't ready up: for an outpost they can still join later through joinable positions; for a crash they miss the flight. [NEW]
- The invite and request UX copies the WF ERT builder's message pattern (`ErtCalledEvent` / `ErtSignUpEvent` and its slot balancing). That flow is ghost-only, so only the pattern is reused. [EXISTS] pattern
- Group chat in the lobby: nice to have, later phase. [NEW]

**Joinable positions** [NEW]:

- Set in the Outpost Configurator: number of slots, a title, an optional preset loadout, and visibility (Open / Code / Group only / Private invite; default Group only). The configuration is saved with the outpost, so it is live at round start. [NEW]
- The outpost is made a station at load (`InitializeNewStation` from a `WFOutpostStation` config with `StationJobs` + `StationSpawning`), holding a `WFOutpostSettler` job. That is how ships offer crew slots today, and it makes job slots, mind setup and `PlayerSpawnCompleteEvent` subscribers all work for free. [EXISTS] pattern
- The outpost station's public slot count is always 0. `joingame` only checks for an open slot, and every station's slots are broadcast to the lobby, so an open slot would let anyone in. The WF request handler checks visibility and code, opens one slot, calls `GameTicker.MakeJoinGame(player, outpostStation, WFOutpostSettler, silent: true)` and closes the slot in the same tick. `IsJobAllowedEvent` carries no station, so the gate has to live in our handler. [NEW] + [EXISTS]
- Outpost stations get `ExtraShuttleInformationComponent` with `HiddenWithoutOpenJobs`, so they appear in neither the NF Station tab nor the Crew tab; they show only in a new **Outposts** tab of the late-join picker. [NEW] on [EXISTS]
- Joiners spawn in an outpost cryopod and are added to the outpost's allow list for the round (door access only, not Builder). [NEW]

### Currently Proposed Spawn Options

1. **Standard**: normal spawning as your selected job. (Difficulty: Normal) (Options: Role) [EXISTS] unchanged.
2. **Shuttle Crash**: you spawn in a randomly selected shuttle, crash landing on a randomly selected world, as a randomly selected job. There is a crash landing sequence on the ship; the console cannot be controlled during it. The PA announces that the shuttle will crash in 3 minutes (time you have to prepare) and then enter atmosphere. The shuttle spawns in the orbit layer of the planet. (Difficulty: Very Hard). The player then has to scrounge up materials to escape the planet. Spawning takes nearby POIs and other players' outposts into account so it lands far enough away not to impact them. (Options: Multiplay) [NEW] on lots of [EXISTS]
3. **Outpost Spawn**: you spawn on your outpost as your Outpost Job (a custom job you create for yourself in the outpost console, which lets you pick the starting jumpsuit etc., basically the loadout menu). The outpost is spawned on the planet you saved it on, deducting the price of the outpost from your balance, and rejecting if you don't have enough. (Options: Selected Outpost to Spawn at, Multiplay) [NEW]

### Outpost Spawn in detail

- Available at round start and as a late join, as long as you don't own a live outpost this round. [NEW]
- Placement: the save's own world position on its own planet. POIs are rerolled every round, so at round start the server **reserves the footprints of every readied Outpost Spawn before POIs are placed**. Late loads that find their spot blocked search outward (spiral, up to 300 m) for the nearest valid spot. [NEW]
- If the save's planet isn't in this round's system, the player picks another built world in the menu (the save keeps its layout, only the spot changes). [NEW]
- Round start: the player waits in the lobby on an "Arriving" countdown until their queued grid lands (see Lobby flow). Mid-round, the 30 second clearance overlay runs before the grid appears, with the same lobby countdown. [NEW]
- Owner spawns in a cryopod as their Outpost Job; group members spawn in the remaining pods. [NEW]

**Outpost Job** [NEW on EXISTS]:

- A real `JobPrototype` `WFOutpostSettler` + a `RoleLoadoutPrototype` `JobWFOutpostSettler` (its gear groups: jumpsuit, shoes, head, back, gloves, belt, PDA...) + a `customJobTitle` entry with the same id for the name. Profile loadouts are keyed by string, and `ProfileRoleLoadout` already stores `CustomJobTitle`, so **no DB migration**. [EXISTS] WF Roles
- The job has `setPreference: false`, so it never shows in the Jobs tab and round-start job assignment never hands it out. Its loadout is edited in the console's Crew tab (reusing `LoadoutWindow`) and in the character editor's Outposts tab. Loadout prices bill from the bank at spawn, as usual. [EXISTS] + [NEW]
- The title follows `CustomJobTitleRules` (no impersonating real jobs). [EXISTS]
- Access: the settler job has no station access tags; outpost doors use the outpost access list. [NEW]

**Outpost Cryopod** [NEW on EXISTS]:

- A pod with the Frontier `CryoSleepComponent` (so you can also cryo out of it), plus a WF spawn handler on `PlayerSpawningEvent` ordered before `SpawnPointSystem` that inserts the new body into a free pod on the outpost. `ContainerSpawnPointSystem` only fires for the Cryosleep spawn priority or `JobEntity` jobs, so it can't be reused as is. [EXISTS] + [NEW]
- Works unpowered for spawning (no soft-locks from a flat battery). Must be anchored on the outpost grid. [NEW]
- At least one cryopod is required to spawn at an outpost. The number of pods caps group size and joinable positions; max 6 pods per outpost. [NEW]
- Cryo-ing out on an outpost reopens the slot. Frontier only reopens slots on `ForceAnchor` grids, which outposts are. [EXISTS]

### Shuttle Crash in detail

Setup at round start (inside `RulePlayerSpawningEvent`):

1. **Pick a world**: random from built, sanctioned worlds. Carcinoma (unsanctioned) only if every group member ticked "allow unsanctioned worlds" ("I want pain"). Worlds with gravity too high for a crashed hull to ever climb (Aerumna, 3 g) are left out. [NEW]
2. **Pick a shuttle** from a crash pool (`wfCrashPool`: small civilian vessels with enough seats for the group and a powered PA speaker or air alarm). [NEW] on [EXISTS] vessel prototypes
3. **Pick jobs**: each member gets a random job from the crash job pool (pilot, engineer, medic, cook, miner, salvager...), with that job's starting gear only. Members spawn from a copy of their profile with that job's role loadout removed, so nothing is billed. [NEW]
4. **Pick a site** that meets the spawn-distance rules (below), then load the shuttle onto the **orbit layer** at that XY. [EXISTS] `TryLoadGrid` + [NEW] site picker
5. **Spawn the crew** with the ERT builder pattern: `SpawnPlayerMob` at seats (buckled) or spawn points, create the mind, add the job role and raise `PlayerSpawnCompleteEvent` ourselves so WF, Mono and NF subscribers see the spawn. [EXISTS] pattern
6. **Own it**: `ShipyardSystem.TryAssignDeed` to the leader's ID card (which only exists after step 5), so the deed, console lock, ship access and records all work. The hull also gets a sale baseline (`WFSaleBaselineComponent`, its appraisal at spawn): a shipyard sale or used-ship listing only pays for value added above it, so a free crash hull isn't a free sale. [EXISTS] + [NEW]

The sequence (times from round start):

| Time | What happens | Tag |
|---|---|---|
| T+0:00 | Crew wakes buckled in. `WFCrashSequenceComponent` goes on the grid: console UI refused, pilots cleared (`ShuttleConsoleSystem.ClearPilots`), thrusters forced off. The normal owner lock can't be used because the owner's ID unlocks it. | [NEW] |
| T+0:00 | PA: new non-selectable alert code `WFAlertCrash`, klaxon loop (`ShipPaSystem.StartAlarm`), announcement "Orbit failing. Impact in 3 minutes. Grab what you need and strap in." | [EXISTS] ShipPa |
| T+0:00 | Orbit decay is suppressed on this grid (it would otherwise start its own 60 s countdown). | [NEW] |
| T+1:00 | PA: "2 minutes." | [EXISTS] |
| T+2:00 | PA: "1 minute. Suits on, find a seat." | [EXISTS] |
| T+2:20 | PA: "10 seconds to entry. Brace." | [EXISTS] |
| T+2:30 | `WFOrbitEntrySystem.TryDropFromOrbit(grid)`, then `WFFlightSystem.EnterLiftLost(grid, 0)` explicitly: the drop only enters lift lost when the hull's lift ratio is below 1, and a crash hull with good thrusters would otherwise descend under control. Thrusters stay off through `WFCrashSequenceComponent`. The existing GPWS callouts play on the way down (lift lost, sink rate, terrain, pull up). | [EXISTS] + [NEW] |
| about T+2:50 to T+3:10 | Impact. Severity is clamped to a hard landing or a structural crash into at most 2 sections, never the full per-tile explosion crash. Crash-damaged APCs flicker until repaired, as built. | [EXISTS] + [NEW] clamp |
| After impact | Console lock lifts, thrusters come back (damaged), code switches to "Crashed" (amber). If the PA has no working speaker, announcements fall back to a chat message to everyone aboard. | [NEW] |

How you get off the planet (the goal): repair the hull and fit landing thrusters (`WFLandingThrusterKit`) until lift >= 1, or find a small derelict POI that can fly, or reach a tradepost and buy passage, or build an outpost and a landing pad and call a ship. [EXISTS] + [NEW]

### Spawn-distance rules

| New thing | From outpost claim zones | From POIs | From crash sites | From world edge |
|---|---|---|---|---|
| Crash site | 300 m | 250 m (300 m from automated outposts) | 200 m | 128 m |
| New outpost (console deploy, save load, Outpost Spawn) | 150 m | 100 m (unless claiming that POI) | 100 m | 64 m |
| POI (story-gen) | 200 m from reserved footprints | 150 m | n/a (placed first) | 100 m |
| Raid spawn | Walk-in 40 to 60 tiles, burrow 20 to 40 tiles, shuttle 40 m or more and outside the claim zone, all from the target outpost's edge; at least 24 tiles from every player | 60 m | 60 m | inside bounds |

- This table is the one placement rule. The console checks it when deployed, the load clearance checks it for the whole footprint, and Outpost Spawn checks it for the reserved footprint. A POI's "exclusion radius" is the POI column (100 m for new outposts). [NEW]
- Per-type placement distances in the POI taxonomy override these defaults where they are stricter. [NEW]
- NF's POI placer returns its last roll even when it fails; ours gives up and falls back instead (Crash falls back to Standard with a message). [NEW]

## Ship Access Overhaul

Ships now get an access options panel in the shuttle console. A bought ship or outpost defaults to ONLY the player's access UNLESS they are part of a faction, in which case the shuttle spawns with the faction access as normal. The owner of a non-faction vessel can add specific people to the access list, or set an "Access Code" which gates doors behind that code. Each door on the shuttle can be configured individually as well (on the access diagram, a wire diagram showing selectable doors). So you can set a specific door to a specific player or code. To open a code-locked door when you don't have access, right click the door > Enter Code, and a code UI lets you enter the 4 digit code, opening the door.

Outpost Consoles are different: they are only accessible to the outpost owner. You can sell your outpost to other players as well. Outposts can be sold at round end, or mid-round in the console (same shuttle sell checks occur) for the exact calculated value of the outpost at the time.

### What exists today

- Mono deed access: `ShipAccessReaderComponent` on doors and lockers, checked by `ShipAccessReaderSystem.HasShipAccess`: admin ghost and AI bypasses; then "allow" if the target is on no grid, and "allow" if its grid has no `ShuttleDeedComponent`; then the faction company match for hard-coded USSP/Rogue/TSF, a deed on a held card, then guest cards. [EXISTS]
- Readers are only added by `ShipyardSystem.AddShipAccessToEntities` (private), run once over the grid at purchase (and from the WF deed path), and start disabled (`Enabled` defaults to false) until the owner flips "lock ship". Doors and lockers built, deployed or loaded later get no reader. [EXISTS]
- Console verbs: lock/unlock console, guest access, reset guest access, lock/unlock ship (`ShuttleConsoleLockSystem`, upstream). No per-door rules, no codes, no per-player list. [EXISTS]
- Deeds are per ID card; `DeedCopySystem` copies a deed to a crewmate's card. [EXISTS]
- Doors have no right-click verbs today. [EXISTS] gap

### Data model [NEW]

On the grid, `WFShipAccessComponent`:

| Field | Meaning |
|---|---|
| Owner | `NetUserId` + character name (outposts: + DB profile id) |
| Mode | Private (default for non-faction) or Faction (default for faction ships and faction members' outposts) |
| AllowList | entries of `NetUserId` + character name + label ("Bob, medic") + a Builder tick (may repack and dismantle prefab parts; default off) |
| Code | 4 digits, ship-wide, server-only (never networked except to the owner's console) |
| Locked | the old "lock ship" toggle, now on by default for new purchases |

On each door, `WFDoorAccessRuleComponent` (serialises with the grid, so outpost saves keep it):

| Rule | Who opens it |
|---|---|
| Default | Follows the ship: owner + allow list (+ faction in Faction mode) |
| Owner only | Owner |
| Players | Owner + the players picked for this door |
| Code | Owner + anyone with the door's code (its own, or the ship code) |
| Players or code | Either |
| Public | Everyone |
| Sealed | Nobody (bolted), owner can still unseal from the console |

- People are matched **by person, not card**: the opener's mind (`NetUserId`) and character. A stolen ID doesn't open your door; an NPC raider (no mind) never matches. The owner also matches through the existing deed on their card, so current flows keep working. [NEW]
- Door opens are predicted: `BeforeDoorOpenedEvent` runs in shared code on the client too. So the owner's and allow-list `NetUserId`s (and each door's picked players) are networked on the grid and door components, and the client only predicts its own opens. Labels, codes and code state stay on the server; a correct code opens the door from the server. Player ids being visible to clients is accepted. [NEW]
- The check hooks into `ShipAccessReaderSystem.HasShipAccess` with a WF event raised right after the ghost and AI bypasses, before the "no grid" and "no deed" early returns; outposts have no deed, so a later hook would never run. The `(ShipAccessReaderComponent, BeforeDoorOpenedEvent)` pair is taken, so we don't subscribe a second time. [NEW] marked hook
- A WF system makes sure every door and locker anchored to an outpost or an owned ship grid has a reader, on anchor, on grid change and after a load, since readers otherwise only come at purchase. [NEW]
- Faction ships: Faction mode keeps the current company rule and access tags. Allow lists and codes are for owners of non-faction vessels, as the original says; extending owner editing to faction ships is Q24. Play-group members and outpost joiners are still added to the allow list on faction ships and outposts; only the owner's own allow-list and code editing is limited to non-faction owners. [EXISTS] + [NEW]
- Existing verbs map onto the model: "guest access" adds you to the allow list, "reset guest access" clears it, "lock/unlock ship" flips Locked. This needs marked edits in `ShuttleConsoleLockSystem`, and "Locked by default" a marked edit where `ShipyardSystem` sets up readers at purchase. [EXISTS] verbs, [NEW] backing

### Codes [NEW]

- Right click a code door > **Enter Code** (only offered if you don't already have access) opens a 4-digit keypad.
- Wrong codes count per person per ship, not per keypad: a ship-wide code is shared by every code door, so a per-keypad lockout lets a group try many doors at once. 5 wrong codes in 10 minutes lock that person out of every keypad on that ship for 15 minutes. The 3rd miss sends a console alert and a PA line naming the character, and writes an admin log.
- Codes can be changed any time; changing one doesn't kick anyone already inside.

### UI [NEW]

- New **Access** tab on the shuttle console and the outpost console.
- Left: the ship outline (reusing the WF ShipStatus whole-ship view), doors drawn as selectable nodes, coloured by rule.
- Right: ship settings (mode, code, locked), allow list (add a player standing near the console, or type a name from the crew list; Builder tick), and the selected door's rule.
- Only the owner can edit. On the shuttle console, crew see a read-only view of what they can open (outpost crew have no console access).

### Outpost ownership and sale [NEW]

- Outposts don't use the ID-card deed (one deed per card would stop a ship owner from also owning an outpost). They use `WFOutpostComponent` ownership (`NetUserId` + character), re-bound at load.
- **Console access**: owner only. Play group members and allow-listed players get doors, not the console.
- **Sell to the game**: in the Sell tab, for the exact current appraisal of the outpost, untaxed (not parts + 50%, not the shipyard's 0.85 rate, no sector tax cut). The sale appraises with a sale predicate: the grid's tiles, structures, machines and machine state (charge, fuel) count; goods don't. Loose stacks, ore, sheets, silo and ore-box contents, gas canisters, `CargoSellBlacklist` items, trade crates and `WFRaidLoot` are not paid for, because they sell at a depot the normal way. The Sell tab lists what won't be paid and asks you to confirm. Same checks as selling a shuttle: no living organics aboard other than you, no hostiles within 32 m, not mid-raid. The outpost is deleted and every save of its lineage is voided. Paid with `TryBankDeposit` with tax off. [EXISTS] + [NEW]
- **Sell at round end**: a toggle; at `RoundEndedEvent` the outpost is appraised and paid out the same way, no final autosave is written, and its lineage is voided. A bank deposit needs a session and cached prefs, so an owner who has disconnected is paid by a queued DB write, applied on their next connect. [NEW]
- **Sell to a player**: owner sets a price and names a buyer standing at the console; the buyer must not own a live outpost this round and must accept and pay. Ownership, console and allow list transfer. The seller's saves of that lineage are voided; the buyer's saves start a new lineage. [NEW]
- **Claimed POI outposts** pay only for what you added: the sale value is appraisal minus the claim baseline stored at claim time, so claiming and selling free POIs isn't a money printer. Once a save of it is loaded at its paid price, the baseline is cleared, since the load paid for everything. [NEW]
- **Console destroyed**: the outpost becomes "Abandoned" and still counts as the owner's live outpost. Only the owner can place a new console on it this round and keep it (pending Q1). If Q1 ever allows others to take one over, the re-founder gets a claim baseline set at re-founding, like a POI claim. [NEW]

## Outpost Landing Pads

Currently, the only way to land on a planet is with a ship that has enough lift.

Outpost landing pads let you designate an area of your outpost where a shuttle can land without the lift requirement. Such ships can only land at the pad and take off from it, and they have to be roughly above it. It acts as a docking port essentially (in game it looks like a small staircase with railings).

### How landing works today (as built)

- You descend from orbit with the console's Enter Atmosphere button, and land **anywhere** under your hull's XY. There is no pad, beacon or slope rule. [EXISTS]
- On touchdown, `Smimsh(explodeGrids: true)` deletes everything under the hull (see Q9) and **explodes both grids** where the hull overlaps another grid. Landing on an outpost today would wreck it. [EXISTS]
- Liftoff needs lift ratio >= 1 and no `ForceAnchor` in the set; ordinary engines give half thrust below orbit, landing thrusters give full. [EXISTS]

### Landing guard (all hulls) [NEW]

- Enter Atmosphere is refused if the hull's footprint would overlap an outpost claim zone (and the refusal says whose).
- A hull that ends up over a claim zone anyway (flying low on an air layer) cannot touch down there: the transit exit is refused, it holds on the lowest air layer, and the PA says "Landing zone occupied".
- A lift-lost hull falling onto a claim zone is shifted to the nearest clear ground before touchdown instead of crushing the outpost.

### Pads [NEW]

- **Landing pad**: a deployable 1x1 marker (the staircase with railings) plus a painted pad area the owner sizes on the outpost grid (5x5 up to 21x21). The pad area is part of the outpost grid.
- **Capture**: on the orbit layer, a hull whose centre is within 32 m (XY) of a pad the pilot has access to gets a "Descend to pad" button next to Enter Atmosphere. "Roughly above" = that 32 m.
- **Fit check**: the hull's footprint must fit inside the pad area, and the pad must be clear.
- **Descent**: scripted, like `TryDropFromOrbit` in reverse control: the hull is carried down at a steady rate, its XY eased onto the pad centre and its rotation onto the pad's facing. Lift doesn't matter. Touchdown on a pad skips the `Smimsh` grid crush against the outpost.
- **Parked**: the hull sits on the pad as a separate grid, static while docked (a pad-dock component, released on launch).
- **Launch**: "Launch from pad" on the console carries the hull back up to the orbit layer, scripted, lift not needed. A hull that landed on a pad **without** lift can only leave by pad launch; a hull with lift can still use normal Liftoff.
- **Access**: pads follow the access panel (owner, allow list, code, or public). A public pad makes a trading post.
- **Power**: 5 kW while guiding a hull. No power, no guidance.
- The orbit radar shows pad markers on outpost blips so pilots can line up.

## Outpost Trade Panel

Outposts get a trade panel in their console that, as long as you have a cargo area set (done by placing the Universal Trade Hub supplied with your new outpost or with a preset), lets the player BUY (it's a buy-only hub) outpost-specific supplies and gizmos (see below). These gizmos are then parachuted in to the UTH.

### Universal Trade Hub (UTH) [NEW]

- A 3x3 receiving pad with a beacon post. One per outpost. Must be on the outpost grid and outdoors (not rooved, see Outpost grids and building). A roofed UTH can't receive drops.
- Needs power (1 kW) to take orders.

### Ordering [NEW on EXISTS]

- The catalogue is data: cargo-product style listings in an outpost group, with categories matching the gizmo tables below. Every listing is priced at or above the appraisal of what it delivers. [EXISTS] `CargoProductPrototype` shape
- Payment from the owner's bank, with the same sector tax split cargo consoles use (Frontier, NFSD, Medical). [EXISTS] `BankSystem`, cargo tax accounts
- Up to 5 crates in flight per outpost. ETA 60 s + 10 s per extra crate. [NEW]
- No selling here. Selling happens the normal way: haul it to a depot, or sell the outpost. [NEW]

### Parachute delivery [NEW on EXISTS]

- When an order is due, the crate spawns on the **top air layer** above the UTH with `WFParachutedComponent` (air layers have gravity, so no push is needed). It falls about 3 layers at 1.5 layers/s, the chute opens on the first level drop, and it lands softly on the pad. [EXISTS] `WFParachuteSystem` + [NEW] order spawner
- It lands on the UTH centre +/- 1 tile. If the pad is blocked it lands on the nearest clear tile within 5 tiles; if there is none, the order waits and refunds after 10 minutes. [NEW]
- The used parachute pack is left at the drop site, as built (recyclable). [EXISTS]
- The UTH beeps and the console shows "Delivered". [NEW]

## Outpost Gizmos

Outposts get a bunch of unique-to-them items that make outposts more viable and interesting. Here's the list so far (it will be updated). Think comms, wind gen, cheaper component generation, mining bots and so on.

### The gizmo pattern

Nova's colony machines all follow one recipe, and we copy it:

- **Fixed stats, no board, no parts, no upgrades.** What you buy is what you get. Nova does this per machine with `circuit = null`, stubbed `RefreshParts` and a `tool_blocker` on screwdriver and crowbar. Outpost variants of stock SS14 machines (thermomachine, dispenser, microwave, silo, reclaimer, telecom server, flatpacker) drop their `Machine`, `MachineBoard` and `Construction` components, so nobody can pull a board or RPED-upgrade one; repack is the only way to move it. A test checks that no repackable prototype carries a machine board. [PORT: Nova] `colony_fabricator.dm` (flatpack base), `repacking_element.dm`, per-machine `circuit = null` + `tool_blocker`
- **Flatpacked**: deploy by using the pack in hand on the tile in front of you, facing your direction (2 to 4 s), only on your own outpost foundation (except the Outpost Console flatpack, which deploys on natural ground and founds the outpost). This is a new `WFDeployableComponent`. Upstream `FlatpackComponent` doesn't fit: it needs a pulsing tool, only unpacks a pack lying on the floor, and spawns a fresh machine, so no state can come along. [NEW], like Nova's `/datum/component/deployable`
- **Repack**: right click with an empty hand > Repack, 3 s. Only the owner and allow-list Builders can repack on an outpost; anyone else has to break it. The machine itself is unanchored and held in a container on the pack (`WFRepackableComponent`), not re-created, so charge, fuel, stored materials and contents all come back on deploy. A repack never yields parts, and is refused while a mob is inside or buckled. Nova's repack instead dumps the machine's contents (the arc furnace ejects its ore, the A.W. drops its fuel), and its stationary battery appears to also drop a machine frame, the board and an oversized empty megacell (read from code, not tested). [PORT: Nova] `repacking_element.dm`, fixed
- **Printed at the Outpost Fabricator** or bought on the trade panel. [NEW]
- **Value**: each outpost machine's `StaticPrice` is its print cost, and a packed machine appraises as the machine inside it (the appraiser recurses containers), so the save price (parts + 50%) is honest. [NEW]

### General

| Gizmo | What it does | Numbers | Source tag | Tier / band |
|---|---|---|---|---|
| Outpost Console | Founds the outpost; save/load, access, trade, crew, defense, sale | Internal battery for saves; 500 W for trade/defense. Not printable | [NEW] | T1 / C (in kit) |
| Time/weather console | Tells you the time and weather, plus the forecast: next weather change, storm phase (30 s telegraph), time until night and until the "Active" window | 200 W. The time and weather lines come from `WFPlanetWeatherSystem.TryGetReport` (three display strings); the forecast needs a new Planets API (`TryGetForecast`: next change, phase, phase end, next night), since the weather component's next change, phase and storm have no public accessor | [EXISTS] timepiece report + [NEW] forecast API and console | T1 / A |
| Outpost GPS beacon | Marks the outpost (or any spot) on the orbit radar with a name; handy for pads and friends | 100 W, range: whole orbit layer (global PVS override) | [PORT: Nova] `kahraman_equipment` `/obj/item/gps/computer/beacon`, on the [EXISTS] `WFOrbitBeacon` pattern | T1 / A |
| Outpost Cryopod | Spawn point for you and your crew; also a cryo exit | Unpowered for spawning; max 6 per outpost | [NEW] on [EXISTS] `CryoSleepComponent` | T1 / B |
| Landing pad | See Landing Pads | 5 kW while guiding | [NEW] | T2 / D |
| Universal Trade Hub | Receives parachute drops | 1 kW | [NEW] | T1 / B (in kit) |
| Wall multi-cell charger | 3 cells at once | 250 W charge rate, about 2x the stock recharger; stock `ChargerSystem` draws the rate once, however many cells sit in it (Nova: 30 kW per cell, sized for tg's 10 kJ cells; SS14 cells hold under 10 kJ) | [PORT: Nova] `wall_cell_charger.dm` | T1 / A |
| Outpost PA speakers | Outpost-wide announcements and alert codes | As ship speakers | [EXISTS] `ShipPa` (works on any grid) | T1 / A |
| Floodlight post | Lights the yard; raids come at night | 300 W | [EXISTS] stock light posts | T1 / A |

### Construction

| Gizmo | What it does | Numbers | Source tag | Tier / band |
|---|---|---|---|---|
| Outpost foundation plates | Extend the outpost grid over natural ground | Stack of 30; 0.25 steel per plate | [NEW] | T1 / A per stack |
| Prefab wall panel | Right click your outpost foundation to raise a wall; no girder, no tools, no power | Stack of 25. 3 s. About half a steel wall's durability; breaks back into 1 panel (owner and Builders; anyone else has to break it). Nova: hardness 70 vs 40, half slicing time | [PORT: Nova] `construction/turfs.dm` | T1 / A per stack |
| Prefab window | Full-tile window from a panel | 1 s | [PORT: Nova] `windows.dm` | T1 / A |
| Roof panel | Roofs one floor tile (see Outpost grids and building); what makes a room "indoors" | Stack of 30; 1 s each, no tools, no power | [NEW] on [EXISTS] `IsRoof` | T1 / A per stack |
| Manual door kit | Unpowered door, 1 s to open or close by hand, ignores bumps (raiders can't tailgate) | No power | [PORT: Nova] `manual_door.dm` | T1 / A |
| Prefab airlock kit | Finished powered airlock, deploys in 4 s; takes its rules from the outpost's access panel (Nova's deploys with no access at all) | Area power | [PORT: Nova] `doors.dm` + [NEW] access | T1 / A |
| Prefab shutters kit | Deploys open, needs a button | Area power | [PORT: Nova] `doors.dm` | T1 / A |
| Wooden fence + gate | Pens, yards, cheap perimeter | No power | [EXISTS] stock wooden fences | T1 / A |
| Inflatable wall/door | Emergency pressure seal, fragile | Melts in fire | [EXISTS] stock inflatables | T1 / A |

### Tools

| Gizmo | What it does | Numbers | Source tag | Tier / band |
|---|---|---|---|---|
| Powered driver | Screwdriver + wrench + wirecutters in one | Tool speed 0.8 | [PORT: Nova] `tools.dm` omni drill, on the [EXISTS] stock power drill | T1 / A |
| Arc welder | Electric welder, runs off a cell | Heavy cell drain | [PORT: Nova] `tools.dm` | T1 / A |
| Compact drill | Mining drill that fits a bag | Tool speed 0.6 | [PORT: Nova] `tools.dm`, on the [EXISTS] stock mining drill | T1 / A |
| Prybar | Heavy crowbar that forces powered doors open, like jaws; for raider-held POIs | Tool speed 1.3 | [PORT: Nova] `tools.dm` doorforcer | T2 / B, trade panel only |
| Herding whistle | Commands tamed animals (see Farming) | 8 tile range | [NEW] | T1 / A |
| Lasso | Catch an animal (see Farming) | 6 tile throw, 3 s struggle | [NEW] on [EXISTS] WF `Tether` ropes | T1 / A |

### Defense

| Gizmo | What it does | Numbers | Source tag | Tier / band |
|---|---|---|---|---|
| Early Warning System | Radar mast that alerts you of an incoming attack with a small grace period. Announces on the outpost PA and sets code Red | 2 kW. Warning 120 s before arrival | [NEW], announces via [EXISTS] `ShipPa` | T1 / B |
| Long-range EWS mast | A taller mast that extends the warning | 4 kW. Warning 240 s before arrival | [NEW] | T2 / C |
| Motion sensor post | Pings the console when something walks into range; cheap perimeter | 7 tile radius, 100 W | [EXISTS] `TriggerOnProximity` | T1 / A |
| Outpost ballistic turret | Magazine-fed deployable turret | No power (like the stock NF ASMGT); 50-round ASMGT ammo boxes (9x19mm, 5.56x45mm, 7.62x39mm or 6.35x40mm); 6 rounds per second, full auto | [EXISTS] `DeployableTurret`, reference [PORT: Nova] `magfed_turret` | T2 / C |
| Outpost laser turret | Grid-powered turret | Idle 5 W; about 250 W while recharging its 2,000 J battery (stock `BaseWeaponEnergyTurret`), so ammo is effectively unlimited | [EXISTS] energy turret bases | T2 / C |
| Turret control panel | Arms/disarms turrets, sets targeting | Wall mount | [EXISTS] `DeployableTurretController` | T2 / B |
| Outpost land mine | Proximity mine for approaches; triggers only for raider and hostile fauna factions, and only arms on outpost-grid tiles | One use | [NEW] on [EXISTS] stock land mine | T2 / A |
| Outpost shield generator | Grid-wide bubble | 150 kW. The Mono POI bio generator is literally "for stationary outposts" | [EXISTS] `ShieldGeneratorPOIBio` | T3 / E |

Turret friend/foe [NEW on EXISTS]: SS14 turrets pick targets by NPC faction hostility first (`NearbyGunTargets`), then `TurretTargetSettings` exempts anyone whose ID holds one of its access tags; it knows nothing of people, allow lists or animals. Outpost turrets and mines get a faction that is hostile only to raider factions and hostile wildlife. Players (owner, crew, traders, crash survivors) are never targets, and livestock are safe through their passive faction (see Farming). A mode that shoots players waits on Q1; it would need a new WF consideration that exempts the outpost's owner and allow list by person. Cap: 2 turrets plus 1 per 300 foundation tiles, max 6 per outpost, and each turret adds raid threat (see Raid director).

### Power Generation

| Gizmo | What it does | Numbers | Source tag | Tier / band |
|---|---|---|---|---|
| Wind turbine | Turns to make power. brrrrr. Must be outdoors with at least 5 kPa of air | 1.5 kW, 6 kW during a windy storm's main phase (Nova: 2.5 / 10 kW, any weather) | [PORT: Nova] `wind_turbine.dm`, on [EXISTS] `RoofComponent` + `WFPlanetWeatherSystem` | T1 / A |
| Deployable solar panel | Planet solar string, outdoors; output follows the local day/night light level | Stock SS14 panel tiers: glass 1.5 kW, plasma glass 2 kW, uranium glass 3 kW peak, x0 at night (Nova: 2.5 to 10 kW over 4 glass tiers) | [EXISTS] `NFPowerSolarSystem` panels + [NEW] day/night scaling, flatpacked like [PORT: Nova] `solar_panels.dm` | T1 / A (plasma and uranium T2 / B) |
| Solar tracker | Points panels at the sun | Not sold for outposts. It aims at the NF sun angle, one global angle shared with space solar, so it can't follow a planet's day; on planet maps panel output ignores facing instead (see below) | [EXISTS] `SolarTracker` | n/a (space only) |
| Small stationary battery | Low storage, high throughput | 1.6 MJ, 300 kW in/out (Nova: 10 MJ, 400 kW) | [PORT: Nova] `power_storage_unit.dm` | T1 / B |
| Large stationary battery | High storage, low throughput backup | 16 MJ, 37.5 kW in/out (Nova: 100 MJ, 50 kW) | [PORT: Nova] `power_storage_unit.dm` | T2 / C |
| Compact RTG | Flat power forever, no fuel. Fragile (40 integrity), lightly radioactive, explodes (heavy 2, light 4) only when destroyed by damage | 9 kW (Nova: 15 kW; Wolfgate's `GeneratorRTG` is 40 kW). Pack: 5 steel, 5 uranium, 5 plasma, 1 gold | [PORT: Nova] `rtg.dm` | T2 / C |
| A.W. fuel generator | PACMAN-style generator burning uranium sheets, 4 power levels, must be anchored; vents 9 mol of water vapour at 400 K every 2 s (Nova also vents 1 mol of helium; SS14 has none, so it is dropped) | 12 to 48 kW (Nova: 20 to 80 kW, 2x PACMAN); 25 sheets; one sheet lasts 6 min at L1, 1.5 min at L4 | [PORT: Nova] `solid_fuel_generator.dm` on [EXISTS] SUPERPACMAN (`PortableGeneratorSuperPacman`) | T2 / C |
| Stirling generator | "TEG at home": hot gas in a pipe vs the room's air. Doesn't work in vacuum. Output comes from the heat actually moved from the pipe gas into the tile's air (TEG-style, energy conserved), so it heats its room: place it outdoors or on a vented tile | Up to 90 kW (Nova: 18.75 W per K difference, cap 150 kW at 8000 K). Nova's output depends only on the temperature difference and its lost heat vanishes, so a trace of hot gas gives full power; that is what we fix | [PORT: Nova] `stirling_generator.dm`, fixed | T3 / C |
| Power exporter | Sells surplus grid power for spesos | 1 per outpost; pays 20,000 spesos per MWh exported (a 100 kW surplus for an hour reaches the cap), capped at 2,000 spesos an hour; only runs while someone is home | [PORT: Nova] `powerator` module | T3 / D |

Rule of thumb for the power ladder: wind = basic solar < RTG < A.W. < stirling. Every Nova generator number is multiplied by one factor, 0.6 (Nova's tier-1 solar panel, 2.5 kW, against the stock SS14 solar panel, 1.5 kW), so Nova's ratios between generators hold. Solar keeps SS14's own panel tiers, and the batteries scale against stock `SMESBasic` (8 MJ, 150 kW) the way Nova's relate to tg's SMES (50 MJ, 200 kW). The solar day/night link is new: NF solar runs its own rotating sun with no planet light input, so a WF system scales panel supply by the layer's light level (`SharedLightCycleSystem.CalculateLightLevel`, or `WFPlanetEnvironmentComponent.MinuteOfDay`). [NEW] on [EXISTS] `NFPowerSolarSystem`. On planet maps it also drops the facing and sun-occlusion factor, since `PowerSolarSystem.TowardsSun` is one global angle; an outdoor panel gives its peak times the light level, whichever way it faces.

### Fabrication

| Gizmo | What it does | Numbers | Source tag | Tier / band |
|---|---|---|---|---|
| Outpost Fabricator | Prints every T1 outpost gizmo, construction piece and tool with no research, and T2/T3 ones once their recipe disk is inserted. Also a fixed pack of mid-tier stock parts, boards and electronics: advanced capacitor and scanning module, nano servo, high-power micro-laser, advanced matter bin, super cell, RPED, APC, air alarm, airlock, fire alarm and firelock electronics, hydroponics tray and processor boards, solar control and power monitor boards. Slow on purpose, no material discount, can't be upgraded. Can print another fabricator; can't print the Outpost Console or the Parts press | Needs an APC; draws like a stock autolathe. Print times follow Nova's real ones (listed time^0.8: a listed 30 s prints in 9.6 s, 1 min in 16.7 s, 2 min in 29 s), about 3x a stock SS14 lathe | [PORT: Nova] `colony_fabricator.dm` and `fabricator_flag_additions/*.dm`, as an [EXISTS] `LatheComponent` with static recipe packs | T1 / C (in kit) |
| Arc furnace | Smelts one ore stack at a time at 1.5x yield (rounded down to whole sheets; each stack is started by hand). Vents hot gas every second it runs, so place it outdoors or on a scrubbed tile; it pairs with the CO2 cracker | 10 kW, 1 s per ore. Per second: 20 mol CO2 at 1200 K; uranium 50 mol CO2; plasma 75 mol CO2 at 2000 K; silver 10 mol N2; titanium 10 mol N2 + 10 mol CO2. A 50-ore stack dumps about 1,000 mol, lethal in a sealed hab | [PORT: Nova] `arc_furnace.dm` (Mono already has arc furnace sprites/protos to lean on) | T1 / B |
| Materials recycler | Hand-fed; turns junk back into sheets | Returns 80% (Nova advertises 80% but returns 100%; we fix it) | [EXISTS] `MaterialReclaimer`, outpost flatpack; reference [PORT: Nova] `recycler.dm` | T1 / B |
| Organics printer | Biomass into plastic and cloth (plastic feeds prefab walls) | Plastic sheet 25 biomass, cloth 10 | [EXISTS] biogenerator + [PORT: Nova] kahraman `organic_printer.dm` recipes | T1 / B |
| Parts press | Cheaper component generation: prints stock machine parts (capacitors, manipulators, bins, lasers) | 65% of normal lathe material cost (never below 0.614, the fully upgraded lathe multiplier `MaterialArbitrageTest` models), 2x time. Outpost grids only. Not printable | [NEW] | T2 / B |
| Flatpacker | Packs stock machines into flatpacks | As stock | [EXISTS] `MachineFlatpacker` | T2 / B |
| Ground drill | Planet drill that coughs up ore. Sits on a drill hole: a lattice tile in the outpost grid, over natural ground | 1 per outpost; 50 kW; one 5-ore stack every 60 s (about 300 ore an hour); pauses with 10 or more ore piles within 3 tiles; only runs while someone is home | [EXISTS] Goob `ItemMiner` + `PlanetMiner` (`requireExpedition: false`, `requireGround: false`, which already accepts lattice, as the Mono laser drill does) + [NEW] 5-ore spawner and home gate (a WF component on `ItemMinerCheckEvent`) | T2 / C |
| Ore thumper | Slams every 15 s; every 30 slams (7.5 min) it scatters 2 to 4 loose ore piles on free tiles within 2 tiles (type weighted, fixed pile sizes from 25 for common ore down to 1 for the rarest, mapped to SS14 ores). Stops while more than 5 piles lie within 2 tiles, so ore has to be hauled away | 50 kW from a cable under it; outdoors on a drill hole over sand, snow or ash ground; 2 tiles clear of other thumpers; only runs while someone is home | [PORT: Nova] kahraman `ore_thumper.dm` | T2 / D |
| Mining bot | Small drone that mines nearby rock and hauls ore to the outpost silo or an ore box | Max 3 per outpost, 1 kW to recharge at a dock; only works while someone is home | [NEW] (HTN) | T3 / D |

### Storage

| Gizmo | What it does | Numbers | Source tag | Tier / band |
|---|---|---|---|---|
| Outpost ore silo | Shared materials for linked machines on the outpost. Contents are saved with the outpost | SS14 silo rules: powered, same grid as the linked machine, within 20 m. The same-grid rule keeps links inside the outpost (Nova's colony silo links by multitool with no range check) | [EXISTS] `OreSiloComponent`, outpost flatpack | T1 / B |
| Fabrication silo | Parts silo + chemical silo | As built | [EXISTS] WF `FabricationSilo` | T2 / C |
| Shelves, crates, lockers | Storage | n/a | [EXISTS] | T1 / A |
| Outpost deposit terminal | Access your safety deposit box from home; refuses items stamped from an outpost save | 500 W | [EXISTS] WF `SafetyDepositBox` | T3 / D |

### Life Support / Atmos

| Gizmo | What it does | Numbers | Source tag | Tier / band |
|---|---|---|---|---|
| CO2 cracker | Turns CO2 into oxygen inside a sealed room: for habs on CO2 worlds (Fervidus, Aerumna) and for scrubbing breath and arc-furnace exhaust. Planet outdoor air is a fixed map atmosphere, so it only works indoors | CO2 to O2 1:1, at most half of its own tile's CO2 each tick, capped at 1 mol per second (Nova: min(CO2/2, 2.5) mol per tick live, 10 only if `RefreshParts` ran, which it probably never does) | [PORT: Nova] `co2_cracker.dm` | T1 / B |
| Temperature regulator | Limited thermomachine, as a heater and a freezer variant | 0 to 200 C; heat capacity 5,000 W as stock (Nova's 10000 is tg thermal mass in J/K, not watts) | [EXISTS] stock thermomachine heater and freezer, limited like [PORT: Nova] `thermomachine.dm` | T1 / A |
| Wall heater | Wall heater/cooler on area power, 0 to 60 C target (Nova's runs off a cell; an SS14 cell would last about a second at most at these rates) | As the stock space heater: 1.75 / 3.5 / 7 kW of heating at low / medium / high, cooling at 0.9x (Nova: 80 kJ per tick) | [EXISTS] stock space heater, wall mount like [PORT: Nova] `space_heater.dm` | T1 / A |
| Portable pump / scrubber | As stock | As stock | [EXISTS] | T1 / A |
| Air alarm, firelock, fire alarm electronics | As stock | As stock | [EXISTS] | T1 / A |
| Water synthesizer | A tank that refills itself with water (SS14 has no plumbing, so it's a regenerating tank) | 1 u/s into a 200 u tank, 2.75 kW (Nova: 5 u per 2 s machine tick, 2.5 u/s, and it only tops up a small buffer as plumbing drains it) | [PORT: Nova] `chem_machines.dm` on [EXISTS] solution regeneration | T1 / A |

Thrascias air is pure N2 at 180 K, with no CO2 to crack, so oxygen there comes as canisters from the trade panel. [EXISTS] canisters

### Kitchen / Food

| Gizmo | What it does | Numbers | Source tag | Tier / band |
|---|---|---|---|---|
| Rations printer | Plants into biomass into seeds, eggs, meat product, flour, milk, rice, sugar, snacks. Seeds start a farm with no seed vendor | Seeds 25 biomass, eggs/butter 25, meat 50, sacks 100 | [PORT: Nova] `foodricator.dm` on [EXISTS] biogenerator | T1 / B |
| Sustenance dispenser | Water, powdered milk/coffee/tea/cocoa, sugar, salt, pepper, enzyme and friends | Own cell, 2 kW recharge. Repackable here (Nova's can't be repacked) | [PORT: Nova] `chem_machines.dm` on [EXISTS] reagent dispenser | T1 / A |
| Microwave | As stock, flatpacked and repackable (Nova's can't be repacked) | As stock | [EXISTS] | T1 / A |
| Tabletop griddle | Griddle that sits on a table | 1 kW | [PORT: Nova] `kitchen_appliances` | T1 / A |
| Frontier range | Oven + stove. Repackable here (Nova's can't be repacked) | 1.2 kW | [PORT: Nova] `kitchen_appliances` | T1 / B |
| Hydroponics tray / soil | Grow food | As stock. Trays and NF soil sit on the outpost grid; Mono planet sand soil only works on raw ground, so it isn't part of the outpost and isn't saved | [EXISTS] trays, NF soil, Mono planet sand soil | T1 / A |
| Nutrient synthesizer | Regenerating tank of E-Z Nutrient, Left4Zed, Robust Harvest, weed killer, pest killer (Nova's Enduro-Grow and Liquid Earthquake have no SS14 reagent and are cut) | 1 u/s into a 200 u tank, 2.75 kW (Nova: as the water synthesizer) | [PORT: Nova] `chem_machines.dm` | T1 / A |
| Animal feeder | Trough that pens need (see Farming) | Holds 200 u of feed | [NEW] | T1 / A |

### Comms / Logistics

| Gizmo | What it does | Numbers | Source tag | Tier / band |
|---|---|---|---|---|
| Outpost telecom relay | Radio works on a planet map only if a powered telecom server with that channel is on the sender's map; this is that server | 2 kW, common channels | [EXISTS] `TelecomServer`, outpost flatpack | T1 / B |
| Outpost intercom | Handheld-exempt intercom | As stock | [EXISTS] intercoms (telecom exempt) | T1 / A |
| Planet broadcast console | Send a short message to everyone on this planet (trade offers, SOS), signed with the outpost's name | 1 message per 15 min per outpost, max 1 per 60 s per world; admin-logged | [NEW] | T2 / B |

### Mobility

| Gizmo | What it does | Numbers | Source tag | Tier / band |
|---|---|---|---|---|
| Atmospheric jetpack | See Jetpacks in the Atmosphere | Welding fuel | [NEW] | T2 / C |
| ATV | Ground vehicle | As stock | [EXISTS] NF ATV | T1 / B |
| Parachute | Drop from orbit or an air layer | As built | [EXISTS] `WFParachute` | T1 / A |
| Landing thruster kit | Converts a ship thruster to full atmospheric rating | As built | [EXISTS] `WFLandingThrusterKit` | T1 / A |

### Orbital Defense

shooty shooty pew pew

The planet stack puts orbit and each air layer on their own maps, so nothing can shoot from the ground to an air or orbit layer today. Everything here is new cross-layer work.

| Gizmo | What it does | Numbers | Source tag | Tier / band |
|---|---|---|---|---|
| Orbital sensor uplink | Shows hulls on the air layers and the orbit layer above the outpost on the console radar | 5 kW, 1 km radius | [NEW] (reuses the orbit radar drawing) | T2 / C |
| Flak battery | Fires at raid shuttles on the air layers directly above; hits resolve as explosions on the target layer after a flight delay | 15 kW, 300 m radius, 3 s flight | [NEW] on [EXISTS] Mono artillery ammo and fire control | T3 / D |
| Orbital defense battery | Long-range strikes on raid shuttles in orbit above the outpost; slow, heavy, needs a gunnery console | 60 kW, 1 km radius, 8 s flight, 20 s reload | [NEW] on [EXISTS] `FireControl` | T3 / E |
| Descent jammer | Raid shuttles can't use Enter Atmosphere over the claim zone plus 50 m while it runs | 40 kW | [NEW] | T3 / E |

Main use: shooting down raider shuttles on their way in (see Outpost Attacks). "Hostile" here means only grids the raid director tags `WFRaidShuttleComponent`; these weapons never target player hulls, and the jammer never blocks a neighbour. Whether players could use them against other players is Q1. [NEW]

## Farming

Given planets have their own fauna and wildlife, you will be able to farm animals and such, using breeding and animal herding mechanics that let you capture these animals and use them to produce things. Requires an animal pen and such.

### What exists [EXISTS]

- Breeding: `AnimalHusbandrySystem` + `ReproductiveComponent` (tries every 45 to 60 s, partner within 3 tiles, stops at 6 animals nearby, 15% chance, 1.5 min gestation, infants). Eggs hatch with `TimedSpawner`.
- Products: `EggLayer` (eggs), `Udder` (milk, 25 u per 30 s on cows), `Wooly` (wool). Butchering gives meat.
- Livestock: chickens, ducks, cows, goats, pigs.
- Planet wildlife spawns from biome fauna sites and retires when nobody's around, unless it has been damaged, possessed or moved onto a hull, which protects it for good.
- HTN `FollowCompound`, WF ropes (a leash is close).

Not there yet: taming, leashes, herding, pens, feeders. [NEW]

### Capture [NEW]

- **Lasso**: throw at an animal within 6 tiles; it struggles for 3 s (DoAfter), then it's leashed with a WF rope. Big animals take two lassos.
- **Cage / animal crate**: stuff a small animal in a crate or pet carrier. [EXISTS]
- **Tranquiliser**: sleep chems knock it out for a safe carry. [EXISTS]
- Capturing marks the animal protected so the wildlife system never retires it. [EXISTS] flag, [NEW] call

### Taming and herding [NEW]

- Feed a leashed or penned animal its favourite food 3 times over 5 minutes: it becomes **livestock** (`WFLivestockComponent`: outpost, owner, name) and joins a passive faction so turrets ignore it.
- **Herding whistle**: Follow me / Stay / Go to pen for tamed animals within 8 tiles (HTN follow). [NEW] on [EXISTS] HTN
- Tamed animals left outside a pen at night can be taken by predators during Active windows (only while someone is home, like raids; see Q7).

### Pens [NEW]

- A pen is any enclosed area (fence, wall, gate) on the outpost grid with at least one **animal feeder** inside.
- Capacity: 6 animals per feeder (matching the breeding crowd cap).
- Animals in a fed pen breed on their own (existing breeding), produce on their own (existing udder/eggs/wool), and don't wander off.
- Feeders take grain, hay, meat or rations-printer biomass, depending on the animal's diet.

### Products

| Animal | Product | Existing hook |
|---|---|---|
| Chicken, duck | Eggs, meat, more chickens | `EggLayer`, `Reproductive` |
| Cow | Milk, meat | `Udder` |
| Goat | Milk, wool, meat | `Udder`, `Wooly` |
| Pig | Meat, piglets | `Reproductive` |
| Planet-native livestock (per world, new variants in each world's fauna table) | Milk, wool, eggs, hide, special reagents | Same components [EXISTS], new prototypes [NEW] |

### Saving livestock [NEW]

Mobs can't be map-saved, so penned livestock is saved as records and respawned at their pen's feeders on load, up to feeder capacity. Loose animals are not saved.

## Planetary Points of Interest

Planets will have a plethora of POIs to investigate, things like mines, abandoned outposts, automated outposts, tradeposts, derelict capital ships, smol derelicts, farms, workshops, ruins, shrines and alien artifact sites.

You will be able to claim these POIs as your outpost if you don't have one already. Each claimable one has an outpost console to interact with.

### Set-size planets

What the planet stack does today, and what "set size" needs:

- **Today the ground is infinite.** Terrain streams in 8x8 chunks around every player and viewer, wherever they go, with no bounds anywhere. Radar samples the biome recipe, so it draws infinite terrain too. Orbit entry works anywhere within 2 km of the body, and a hull keeps that XY all the way down. [EXISTS]
- **Terrain is seeded per world** (fixed seeds), so a world's landscape is the same every round. POIs are rerolled per round, which is what makes each round a new story. [EXISTS] + [NEW]
- **Nothing on a planet persists between rounds**: every WF planet component is unsaved and networks rebuild each round. That's why outposts save their own grid. [EXISTS]

Bounding a world [NEW]:

| Piece | How |
|---|---|
| Size | New `Bounds` on the surface prototype. Default: a 1536 m square (small worlds 1024 m, big worlds 2048 m), centred on the world's ground centre |
| Chunk filter | Chunks outside the bounds never load. The chunk list is private upstream, and a partial can read it but can't stop `AddChunksInRange` without a call site, so this is a marked hook in `BiomeSystem`. Pre-generating the whole world was ruled out: 2 million+ tiles per world |
| Edge | A ring of impassable terrain just inside the bound (cliffs, deep water or storm wall, per world). Mobs and items that get past it are pushed back in; hulls on air layers are clamped by a soft wall |
| Orbit | Orbit entry stays as is; Enter Atmosphere and pad descents are refused unless the hull's XY is inside the bounds |
| Radar | `WFPlanetRadarSystem.Sample` returns empty outside the bounds |

### Undergrounds (coming)

Planets are getting underground layers: the Mine POI becomes a surface entrance and ops area with linked pre-mined tunnels below, and the occasional underground safehouse. What that means for outposts, given how the stack is built:

- CE keys a z-network's maps by depth in a dictionary with a sorted min/max cache, so depths below 0 work at the CE level; the "contiguous from 0" rule is only in `WFPlanetNetworkSystem`, which adds ground, air layers and orbit as 0..N. Adding depth -1, -2 under the ground is a change in that builder, not in CE. [EXISTS]
- Moving between depths already exists: CE ladders (with a ladder cache), falling through open tiles, and grid connectors, which bind grids on adjacent depths into one grid network (that is how multi-deck ships work). Roofs come from the tiles on the layer above, so an outpost grid is the ceiling of whatever is under it. [EXISTS]
- **An outpost claims its column** [NEW]:
  - Underground chunks under a claim zone generate as unmineable foundation rock: no tunnels, no safehouses, no fauna spawns. This is a generator rule on the underground biome, the same reservation the surface carve already makes, one layer down.
  - Tunnels that already exist under a spot when an outpost is founded or loaded there stay as they are. Nothing can dig up through the outpost's tiles (CE has no dig-up), so a tunnel below is only reachable if the owner opens a hatch in their own floor.
  - Load clearance and the placement rule look at depth 0 only; what is below never blocks a load.
  - Claiming a Mine POI claims its surface grid. The tunnels stay world terrain, shared with everyone, rerolled each round, and never saved. Ore underneath is not reserved for the owner (see Q28).
  - Nothing below depth 0 is saved in the first version. Basements (an outpost as a set of grids, one per depth, bound by grid connectors and saved together) are a later phase, once the save format carries a depth per grid.
  - Raids get a "tunneller" arrival later: they surface at the claim edge, never inside the outpost, since the foundation rock rule means there is no tunnel to surface from under the base.

### Story-gen

- At network build, after pending outpost footprints are reserved, each world rolls its POIs from a per-planet table (`wfPlanetPoiTable`, weighted by planet type). Default 8 to 14 POIs per world. [NEW]
- POI grids load as separate grids on the ground map (like `GroundGrid` does, with `ReserveTiles`), not as biome markers, so they can be claimed and saved. [EXISTS] precedent + [NEW] placer
- The NF POI system is sector-map only and hard-wired to the default map; we reuse its prototype fields and grid spawn, not its placer. [EXISTS] + [NEW]
- POI protos get their own base (not `BaseMobilePOI`, whose preset list misses Wolfgate's MonoXeno). Claimable POIs don't get `ProtectedGrid { KillHostileMobs }`, or raids could never reach them. [NEW]
- Grids live in `Resources/Maps/_WF/Outposts/POI`. Existing NF/Mono POI grids and NF dungeon configs (planet base, mineshaft, salvage outpost...) are the first source of rooms. [EXISTS] + [NEW]

### Beacons [NEW]

- Every POI has a beacon on the orbit layer at its XY (the `WFOrbitBeacon` pattern: warp point + IFF, with the same global PVS override as outpost beacons), shown on orbit radar.
- Beacons start as **"Unknown signal"**. The name and type reveal when any player gets within 50 m on the ground, or when someone scans it with a survey tool from orbit. The orbit surveyor went with the planet-cracker, so the survey tool is new work in `PlanetPois`.
- Shrines and artifact sites have a **faint signal**: their beacon only shows on radar to a hull within 300 m on the orbit layer, or once a clue item found in ruins has revealed it. The mystery stays, and they still have beacons.

### POI taxonomy

| POI | What it offers | Claimable | Beacon | Placement |
|---|---|---|---|---|
| Mine | Rich ore veins, a broken drill, ore boxes, cave fauna | Yes (becomes a mining outpost) | Unknown signal | Rocky biomes, 150 m from other POIs |
| Abandoned outpost | A half-wrecked prefab base with a dead console, some machines, loot | Yes, after you repair the console | Unknown signal | Anywhere, 200 m from reserved outposts |
| Automated outpost | Turrets, drones, a fabricator or drill still running; guarded | Yes, after the defenses are down and the core is hacked | Named from the start ("Automated facility") | Anywhere, 200 m from reserved outposts (crash sites keep 300 m from it) |
| Tradepost | NPC traders (buy/sell, refuel, repairs), a public landing pad and UTH, safe zone | No (NPC-run) | Named, always visible | 1 to 2 per world, near the world centre |
| Derelict capital ship | Big salvage dungeon, hostiles, valuable parts | No (too big) | Unknown signal | Max 1 per world, 400 m from everything |
| Small derelict | Crashed shuttle, salvage; sometimes repairable enough to fly (a crash survivor's way out) | No, but it can be repaired and flown | Unknown signal | 150 m from other POIs |
| Farm | Livestock to capture, crops, a barn and pens | Yes | Unknown signal | Grassy biomes |
| Workshop | Machine frames, a fabricator with random recipe disks, tools | Yes | Unknown signal | Anywhere |
| Ruins | Dungeon rooms, loot, clue items that reveal faint signals | No | Unknown signal | Anywhere |
| Heavenly shrine | A one-time blessing (full heal, a mood boost, or a relic worth money), lore | No | Faint signal | Remote, 300 m from outposts |
| Alien artifact site | Xenoarchaeology artifacts, strange fauna, anomalies | No | Faint signal | Remote, max 2 per world |

Artifacts, dungeons and traders are all existing systems (xenoarchaeology, `DungeonSystem` with NF configs, WF `Traders`). [EXISTS]

### Claiming a POI [NEW]

1. Walk up to the POI's outpost console. It shows "Claim" if you don't own a live outpost this round.
2. Requirements: no hostile NPCs on the grid, and (for automated outposts) the core hacked. Abandoned consoles need repairing first. At most half of a world's claimable POIs can be claimed at once.
3. Claiming is free. The POI becomes your outpost: ownership, access panel, save tab. It keeps `ForceAnchor`.
4. The appraisal at claim time is stored as the **claim baseline**, and selling only pays what's above it.
5. Once saved, it's yours like any other save (loading costs parts + 50%, as usual, and a paid load clears the baseline).

## Outpost Attacks

Outposts draw the attention of nearby settlers and fauna. These are NPCs that attack your base at night, or whenever the planet's "Active" state is. There is a gizmo (the Early Warning System) that gives you a few minutes' notice of an incoming attack. Attacks range from a rabid rabbit all the way to a raiding group of raiders (all NPC controlled). It is recommended that you equip your outpost with defence turrets.

Depending on the attack type, these nasties come from burrows, shuttles or vehicles.

### What exists

- Day/night per world: a local day is 60 real minutes; `WFPlanetEnvironmentComponent.IsNight` (before 06:00 or from 18:00) is published every second on every layer. Only client ambience reads it today. [EXISTS]
- Hostile humanoid NPCs with HTN combat, and pathing flags for prying airlocks and smashing walls. [EXISTS]
- Those flags only work on the NPC's own grid. A raider on the planet ground whose target is on another grid is sent straight at it (the Monolith port of wizden#38846 in `MoveToOperator` sets a direct move across grids and skips A*), and direct moves skip obstacle handling. The ground navmesh also ignores outpost walls, since it only counts entities parented to the ground grid. So today a raider walks into the outpost's outer wall and stays there. [EXISTS] gap
- NPC factions for raiders: PirateNF, StreetGangNF, MercenariesExpeditionNF and friends. [EXISTS]
- Mono AI shuttles for the shuttle arrival mode. [EXISTS]
- `TimedSpawner` for burrows (the xeno burrower marker is exactly this). [EXISTS]
- HTN NPCs sleep when no living player is within 32 tiles (`npc.player_pause_distance`), unless the HTN sets `SleepPlayerCheckRangeOverride` (a Mono field). Terrain only loads within about 16 tiles of players and viewers. Mono mob cleanup never runs on planet ground (it only takes mobs that are off every grid, and planet ground is a map grid). [EXISTS]
- Not there: a raid director, waves, the Active state, early warning, breaching across grids. [NEW]

### Breaching [NEW]

- Raiders get a custom HTN operator: while direct-moving at a target on another grid, it smashes or pries whatever wall or door it runs into. Once a raider is on the outpost grid, normal pathing and the existing pry and smash flags take over. No pathfinding edit.
- This is a phase-0 spike: an integration test puts a mob on the ground with a target inside a walled outpost and checks it gets in, before any other raid work starts.

### The "Active" state [NEW]

- Each world has an Active window tied to its day/night clock. Default: Active = night (18:00 to 06:00, which is 30 real minutes of each 60-minute local day).
- Per-world overrides in the surface prototype: e.g. Fervidus (hot) is Active at midday, Carcinoma is always Active at a low level.
- Storms stack: a storm's main phase during an Active window bumps threat by one step.
- The time/weather console shows the countdown to Active.

### Raid director [NEW]

One per world, ticking every 30 s during Active windows:

- **Attention** per outpost = wealth (appraisal) + power draw (noise) + age in rounds + livestock count, minus recent raids.
- **Threat points** from attention, plus one per turret; the director buys a raid from the ladder with them.
- **Gates**:
  - raids are on (`wf.outposts.raids`) and the world allows them;
  - someone is home: at least one owner or crew within 64 tiles of the outpost;
  - the outpost is at least 20 minutes old this round (grace);
  - at most one raid per outpost per Active window, 2 concurrent raids per world, 24 raid mobs per world, 64 server-wide.
- **Keeping raiders awake**: every raid mob gets `SleepPlayerCheckRangeOverride` of 128 tiles (spawn distance plus the widest outpost), and the director keeps the terrain chunks along the approach loaded while the raid runs. Nothing keeps a biome chunk loaded today: only players and viewers load chunks (`BiomeSystem.PlayerTracker.cs`), and loaded chunks outside that set are unloaded, one per unload pass (every 10 s or more). `BiomeSystem.WFChunkPin.cs` (in the cracker's Chunk folder, moving into Planets) only pins tiles against regeneration, and its by-hand chunk load is unloaded again by those passes. Keeping the approach loaded is new work in Planets (a raid chunk loader).
- **Despawning**: mob cleanup doesn't run on planets, so the director removes its own raiders: at dawn, and any raider more than 100 tiles from its target outpost (so nobody can lead a raid into a neighbour's base).
- **Early warning**: with an EWS the raid is announced 120 s before arrival (240 s with the long-range mast): PA code Red, console alert, a direction arrow. Without one, you get a 15 s "you hear something out there" popup.
- **Dawn**: when the Active window ends, surviving raiders retreat and despawn once out of sight. A landed raider shuttle and anything on it despawn with them.
- Raiders never target the outpost console (so a raid can't delete your outpost by accident).

### Escalation ladder

| Step | Threat | Example | Arrival |
|---|---|---|---|
| 0 | A rabid rabbit | 1 to 2 small wild animals gone aggressive | Walk in from the edge of view |
| 1 | Pack | 3 to 5 predators (world fauna) | Walk in |
| 2 | Burrowers | Burrow opens 20 to 40 tiles out, spits creatures for 30 s until destroyed | Burrow |
| 3 | Scavengers | 2 to 3 humanoids with melee and tools; pry doors | Walk in |
| 4 | Raider party | 4 to 6 armed humanoids, a breacher with explosives who goes for turrets first | Vehicles (ride in on ATVs, dismount) |
| 5 | Raider shuttle | Shuttle lands 40 m or more from the outpost, outside the claim zone (landing guard applies), 6 to 8 raiders | Shuttle |
| 6 | Siege | Mixed party + a shuttle + burrows, only for rich, old outposts | All |

Arrival modes in detail:

- **Walk in**: spawn 40 to 60 tiles from the outpost edge, at least 24 tiles from every player (the fauna spawn rule), at least 60 m from POIs and crash sites, inside the world bounds. [EXISTS] rules, [NEW] spawner
- **Burrow**: a `WFRaidBurrow` entity with a `TimedSpawner`, 20 to 40 tiles out; it keeps spawning until destroyed or the raid ends. [EXISTS] + [NEW]
- **Vehicles**: NPCs can't drive today. Phase one: raiders spawn mounted and dismount at 20 tiles (cosmetic). NPC driving is later work. [NEW]
- **Shuttle**: a raider shuttle (tagged `WFRaidShuttleComponent`) drops from the orbit layer via `TryDropFromOrbit`, lands near the outpost, raiders disembark. Orbital defense can shoot it down on the way. [EXISTS] + [NEW]

Loot: raiders drop their gear, flagged `WFRaidLoot`. It carries `CargoSellBlacklist` so no depot buys it, the outpost sale doesn't pay for it, and it is left out of saves. It is yours to use this round, so raids pay something without turning into a farm. A defense bounty is Q7. [NEW] on [EXISTS] `CargoSellBlacklist`

## Jetpacks in the Atmosphere

Atmospheric jetpacks can transit through the atmosphere levels; normal jetpacks are useless there. They use welding fuel. Some planets have super strong gravity, no workies.

- **Normal jetpacks** already refuse to work below orbit and cut out if carried down: `SharedJetpackSystem.WfInAtmosphere` ("cannot hold you up in atmosphere") is checked at three places in `SharedJetpackSystem` and once in the server `JetpackSystem.Update`. [EXISTS]
- **Atmospheric jetpack** [NEW]:
  - Keeps `JetpackComponent` for movement and adds `WFAtmosphericJetpackComponent`. `WfInAtmosphere` takes only the user today, so it gets the pack as well and skips atmospheric packs, at all four call sites (marked edits in both jetpack systems).
  - Fuel: welding fuel is a reagent, but the server `JetpackSystem` only enables packs with a `GasTankComponent` holding enough moles, and only burns gas. A marked branch in its `CanEnable` and `Update` hands atmospheric packs to a WF solution-burn system instead. Tank 100 u; burn 1 u/s hovering, 2 u/s thrusting or climbing. Refill at any welding fuel tank.
  - Works on the ground and air layers, and can climb and descend between them (0.5 layers/s). It needs air to burn, so it doesn't work on the orbit layer.
  - Out of fuel mid-air: you fall. Wear a parachute (the parachute system already catches falls through a level).
- **Gravity gate** [NEW]: planet gravity is one number per world, used only for ship lift today. The jetpack reads `WFPlanetLayerComponent.Gravity` (networked, so shared code can read it) and refuses above 1.5 g ("too heavy here"). So Aerumna (3 g) is a no-go, Merak (1.15 g) and Thrascias (1.25 g) are fine but burn fuel 25% faster above 1 g.
- Price: band C, T2, on the trade panel and at tradeposts. [NEW]

## Under the hood (for devs)

### Modules

Per AGENTS.md, one feature per module. `Spawning` is taken (admin spawn menu) and `Station` is the overflow tweak.

| Module | Holds |
|---|---|
| `Outposts` | Console, outpost grid, foundation tiles, roof panels, save/load, pricing, lineage, kit, presets, gizmos, deploy/repack, trade panel, UTH, landing pads, cryopods, Outpost Job, admin tools |
| `OutpostRaids` | Active state, raid director, ladder, burrows, breaching operator, early warning |
| `SpawnOptions` | Spawn menu, play groups, crash start, round-start handler, joinable positions picker tab |
| `ShipAccess` | Access overhaul for ships and outposts, door rules, codes, readers, access tab |
| `PlanetPois` | Bounds hooks it needs, POI tables, placer, beacons, survey tool, claiming |
| `Husbandry` | Capture, taming, herding, pens, feeders, livestock records |
| `Planets` (existing; `_WF/PlanetCracker` on the branch, being renamed) | World bounds, ground carving, covered-tile checks in weather strikes and fauna sites, raid chunk loader, forecast API, atmospheric jetpack, landing guard hooks in flight code |

Paths: `Content.{Client,Server,Shared}/_WF/<Module>`, `Resources/Prototypes/_WF/<Module>`, `Resources/Locale/en-US/_WF/<Module>`, `Resources/Textures/_WF/<Module>`, `Resources/Audio/_WF/<Module>`, presets in `Resources/SharedMaps/_WF/Outposts`, POI grids in `Resources/Maps/_WF/Outposts/POI`, tests in `Content.IntegrationTests/Tests/_WF/<Module>`.

### Key new pieces

Components (shared unless noted):

- `WFOutpostComponent` (grid): owner user + profile id + character name, outpost name, founded round, save id, lineage id, claim baseline, abandoned-at time.
- `WFOutpostConsoleComponent`, `WFOutpostFoundationComponent` (tile item), `WFRoofPanelComponent`, `WFOutpostCryopodComponent`, `WFLandingPadComponent`, `WFPadDockedComponent`, `WFUniversalTradeHubComponent`.
- `WFDeployableComponent`, `WFRepackableComponent` (the machine held in a container on the pack), `WFWindTurbineComponent`, `WFEarlyWarningComponent`, `WFHomeGatedComponent` (server; cancels producers while nobody is home).
- `WFSaveOriginComponent` (lineage stamp), `WFSaleBaselineComponent` (crash hulls).
- `WFShipAccessComponent` (grid), `WFDoorAccessRuleComponent` (door), `WFDoorCodeKeypadComponent`.
- `WFCrashSequenceComponent` (grid, server), `WFAtmosphericJetpackComponent`.
- `WFLivestockComponent`, `WFAnimalFeederComponent`, `WFLassoComponent`.
- `WFPlanetBoundsComponent` (network), `WFPoiSiteComponent`, `WFPoiBeaconComponent`, `WFRaidBurrowComponent`, `WFRaidLootComponent`, `WFRaidShuttleComponent`.

Server systems: `WFOutpostSystem`, `WFOutpostSaveSystem`, `WFOutpostLoadSystem` (staging, clearance, overlay, swap, re-init), `WFOutpostPricingSystem` (save and sale predicates), `WFOutpostTradeSystem`, `WFOutpostAdminSystem`, `WFLandingPadSystem`, `WFOutpostSpawnSystem`, `WFCrashStartSystem`, `WFShipAccessSystem`, `WFPlanetPoiSystem`, `WFPlanetBoundsSystem`, `WFRaidDirectorSystem`, `WFHusbandrySystem`. Play groups as an IoC manager (`WFPlayGroupManager`), lobby state has no entities.

Prototypes: `wfOutpostPreset`, `wfOutpostTradeListing` (or cargo products in an outpost group), `wfPlanetPoiTable`, `wfPoiType`, `wfRaidTier`, `wfRaidTable`, `wfCrashPool`, job `WFOutpostSettler` + role loadout `JobWFOutpostSettler` + `customJobTitle`, alert codes `WFAlertCrash`, `WFAlertCrashed`, `WFAlertRaid`, tile `WFFoundationBed`.

### Reused systems (no rebuild)

`ShipyardSystem.TrySaveShip` / `TryAddSavedShip` / `StripForResale` / `TryAssignDeed`, `UsedShipReinitSystem.ReinitLoadedShip`, `PricingSystem.AppraiseGrid`, `BankSystem` (session withdraw, deposit with tax off), `ForceAnchorSystem` components, `ShipPaSystem` / `ShipAlertSystem`, `WFOrbitEntrySystem` (drop, enter atmosphere), `WFFlightSystem.EnterLiftLost`, `WFParachuteSystem`, `WFPlanetWeatherSystem`, `BiomeSystem.ReserveTiles`, `SharedRoofSystem` / `IsRoof`, `CEPvsOverrideComponent`, `CryoSleepSystem`, `StationSpawningSystem.SpawnPlayerMob`, `GameTicker.MakeJoinGame`, `RulePlayerSpawningEvent`, `ExtraShuttleInformationComponent`, `LatheComponent`, `OreSiloComponent`, `ItemMiner` / `PlanetMiner`, `AnimalHusbandrySystem`, `DeployableTurret`, HTN `SleepPlayerCheckRangeOverride`, `CargoSellBlacklistComponent`, WF `ShipPreview`, WF `Roles` custom titles, WF `Traders`, WF `SafetyDepositBox`.

### Upstream hooks (marked edits)

| File or system | Hook | Module |
|---|---|---|
| `ShipAccessReaderSystem.HasShipAccess` | Raise a WF access event right after the ghost and AI bypasses, before the no-grid and no-deed early returns | ShipAccess |
| `ShuttleConsoleLockSystem` | Guest access and lock-ship verbs backed by the WF access model | ShipAccess |
| NF `ShipyardSystem` | New purchases start Locked; the sale and used-ship listing paths honour `WFSaleBaselineComponent` | ShipAccess / SpawnOptions |
| `BiomeSystem` | Chunk filter for world bounds (a partial can't stop `AddChunksInRange` without a call site); raid chunk loader, extra chunks kept in the active set next to the player and viewer requests | Planets |
| `SharedJetpackSystem` (already marked by Planets) | `WfInAtmosphere` takes the pack; atmospheric exemption at its three call sites | Planets |
| Server `JetpackSystem` (already marked by Planets) | `WfInAtmosphere` call site; solution-fuel branch in `CanEnable` and `Update` | Planets |
| CE `CEZLevelsSystem.Transit.cs` (already marked by Planets) | Landing guard on transit exit; pad touchdown skips the `Smimsh` crush against the host outpost | Planets / Outposts |
| CE `CEZLevelsSystem.Gravity.cs` (already marked by Planets) | A falling hull is shifted off a claim zone before touchdown | Planets |
| Shared `AnchorableSystem` | Refuse anchoring on raw ground in someone else's claim zone; `AnchorAttemptEvent` is raised directed at the entity, never broadcast, so no WF system can see all of them | Outposts |
| Mono `PersistAtRoundEndSystem` / `PersistentProfileSystem.AddItem` | Refuse entities with `WFSaveOriginComponent` (neither raises an event that can be cancelled) | Outposts |
| Lobby UI (`LobbyGui`) | Spawn Options button | SpawnOptions |
| `HumanoidProfileEditor` | Outposts tab | Outposts |
| Shuttle console window / `NavScreen` | Access tab, pad buttons | ShipAccess / Outposts |
| NF late-join picker | Outposts tab | SpawnOptions |
| `Model.cs`, `ServerDbBase`, `ServerDbManager.cs` (`IServerDbManager` and its implementation) | Outpost save table, API, profile id lookup | Outposts |

Everything else is new `_WF` systems subscribing to existing events, or partials. The weather strike and fauna site checks are WF code in Planets, not upstream edits. Generated EF migrations are exempt from markers.

### Save format and DB

Migration `WolfgateOutposts` (Postgres + SQLite), table `wf_outpost_save`:

| Column | Type | Notes |
|---|---|---|
| id | Guid | PK |
| profile_id | int | FK to `profile`, cascade delete |
| owner_user_id | Guid | For admin tools and audits |
| slot | int | 0 to 2 manual, 3 autosave (counting from 0); unique per profile among live rows |
| lineage_id | Guid | Shared by every save of one outpost; a sale voids them all |
| status | int | Active, Sold or Deleted; only Active rows load, admins can restore the others |
| name | text(30) | |
| surface_id | text | Planet surface prototype id |
| anchor_x, anchor_y, anchor_rot | float | World position and rotation of the console |
| console_local_x, console_local_y, console_local_rot | float | Console position inside the grid |
| appraisal | int | Save-predicate value at save time |
| tile_count, entity_count, bounds_w, bounds_h | int | Size cap checks and lobby display |
| livestock | text | JSON records |
| format_version | int | Bump on breaking changes |
| data | bytea (Postgres) / BLOB (SQLite) | Compressed YAML, max 2 MB, stored as binary rather than base64 text |
| created_at, updated_at | timestamp | |
| saved_round_id, last_loaded_round_id | int | "In use this round" and audits |

Plus `wf_outpost_payout` (user id, profile id, amount, reason, created_at) for round-end sales owed to owners who had disconnected, paid on their next connect.

- On load of an old `format_version`, run a migration step, or refuse with a clear message. Prototype ID renames go through `Resources/migration.yml` as usual.
- Prices are stored at save time and shown in the lobby as an estimate; the charge is the stored number, plus the difference if the staged copy re-appraises higher (see Loading).
- Retention: an admin prune command drops Deleted and Sold rows, and saves not loaded for a set number of days, modelled on `DeleteStaleSafetyDepositBoxes`.

### Networking

- Lobby: `MsgWFOutpostSaveList` (metadata only) on connect and on change.
- Preview: request/response for one save, decompressed on the server and sent as plain YAML, owner only, size-capped, max one request per 5 s.
- Load overlay: networked footprint (tile list + origin + rotation + seconds left) to clients in range.
- Play groups: small lobby messages (browse, create, join, request, invite, kick, result), modelled on the ERT prompt messages.
- Access: owner and allow-list `NetUserId`s, and each door's picked players, are networked on the grid and door components so door opens predict. Labels and Builder flags go to the owner's console only; codes never leave the server except to the owner.

### Admin tools [NEW]

Every lost outpost after a crash or a bug becomes an ahelp, so admins get tools from F1:

- Commands: `outpost list`, `outpost tp`, `outpost stow` (final save, unload), `outpost delete`, `outpost rename`; `outpostsave list <user>`, `outpostsave restore <saveId>` (sets a Sold or Deleted row back to Active), `outpostsave refund <user> <amount>`, `outpostsave prune <days>`; `outpostraid toggle <world>`, `outpostraid fire <outpost> <step>`.
- Admin log types: save, load, sell, transfer, access change, code failure, repack, raid result, abandonment.
- Admins can open any save's preview.
- Precedents: the Mono `persistence` toolshed command and WF `AdminVesselSpawnSystem`.

### Performance budget

Nothing here has been measured yet. Before F1 ships, a benchmark test saves and loads a maximum-size outpost and logs the time; the size cap, the autosave spacing and the round-start queue spacing are set from it (target: save under 150 ms, load under 500 ms).

| Thing | Budget |
|---|---|
| Live outposts per server | A cvar that scales with connected players, hard cap 30. At the cap, Spawn Options shows no slots left and refuses Outpost Spawn before charging, and console deploys are refused with a message |
| One outpost | 1,600 tiles, 4,000 saved entities, 2 MB compressed, as starting caps |
| Cryopods | Max 6 per outpost |
| Save | Serialise on the main thread, compress and write off-thread; autosaves only when something changed, staggered, max 1 per 5 s server-wide |
| Loads | Staged on a holding map, one at a time; round-start loads queued after job assignment while their players wait in the lobby |
| Worlds | 6 bounded worlds, streaming unchanged inside bounds. POI grids don't idle: atmos, power and machines tick on every grid whether or not anyone is near, and only HTN NPCs sleep. Keep POIs small and mostly static, or spawn their contents when a player first comes near |
| POIs | 8 to 14 per world, one grid each |
| Raid mobs | 24 per world, 64 server-wide; fauna caps unchanged (32 per world, 128 total) |
| Automated producers | 1 ground drill, 1 power exporter, 3 mining bots per outpost; all stop while nobody is home |
| Turrets | 2 plus 1 per 300 foundation tiles, max 6 per outpost |

### Tests

Integration tests under `Content.IntegrationTests/Tests/_WF/<Module>`, never `Destructive`:

- Save/load round trip on a small test outpost, loaded onto a real planet ground map (entities, silo contents, battery charge, door rules survive; stripped entities gone; no stale z-level or flight components).
- Pricing: load price = appraisal x 1.5; a player standing on the outpost doesn't change the stored price; the sale predicate leaves out goods and raid loot; claim baseline sale and its reset after a paid load.
- Lineage: any sale voids every save of that lineage; save-stamped items are refused by the deposit box.
- Arbitrage: kit, bare console, add-on packs and every trade-panel listing are priced at or above the appraisal of their deployed contents; every Outpost Fabricator recipe's material value is at or above its product's appraisal; the Parts press respects `MaterialArbitrageTest`'s multiplier.
- Gizmos: no repackable prototype carries a machine board; a repack and deploy keeps charge and contents.
- Clearance: refuses overlap with a grid, a claim zone, a POI or crash site within the placement rule, out of bounds.
- Access: owner, allow list, code, per-person code lockout, per-door rules, faction mode; readers appear on doors built after purchase.
- Joinable positions: `joingame` onto an outpost station is refused; the WF join path works.
- Raids: a mob on the ground with a target inside a walled outpost gets in (phase-0 spike).
- Spawn options: validation and fallback to Standard.
- Presets: one universal test over every preset grid (not one test per preset), through the MapInit load branch.
- Benchmark: save and load of a maximum-size outpost within budget.

### Phase plan

The coarse phases. The step-by-step order, one pull request per step, with sizes, tests and the four stop points, is in `IMPLEMENTATION_PLAN.md`; its step numbers (F1.3, F2.4...) refer to these phases.

| Phase | Scope | Depends on |
|---|---|---|
| F0 | Spikes: raiders breaching a separate-grid outpost, save/load benchmark, roofs on a ground grid, client YAML load in the sandbox | Planets |
| F1 (grid) | Outpost grid: console, foundation plates, ground carving, roofs and the outdoors rule, anchoring, claim zone, cleanup and Carcinoma exemptions | F0 |
| F1 (save) | Save/load: DB, Save tab, autosave, staging + clearance + overlay, pricing and predicates, lineage, previewer overload, editor tab, admin tools; sale: Sell tab, sale predicate, sell at round end, sell to a player, `wf_outpost_payout` | F1 (grid) |
| F2 | Outpost Kit, fabricator, deploy/repack, gizmo pack 1 (power, construction, tools, atmos, kitchen, comms/logistics), time/weather console and its forecast API, trade panel, UTH parachute drops | F1 |
| F3 | Ship Access Overhaul (ships and outposts) | F0 |
| F4 | Outpost Spawn: spawn menu, Outpost Job, cryopods, joinable positions, presets | F1, F3 |
| F5 | Play groups, Shuttle Crash start | F4 |
| F6 | Landing guard, landing pads | F1 |
| F7 | Bounded worlds, story-gen POIs, beacons, claiming, undergrounds interplay | F1 |
| F8 | Outpost attacks: Active state, raid director, EWS, turrets | F2 |
| F9 | Farming | F2 |
| F10 | Orbital defense, atmospheric jetpacks, mining bots, late gizmos, basements | F6, F8 |

## Decisions

- D1: An outpost is its own grid on the ground map; only the outpost grid is saved.
- D2: The Outpost Console founds an outpost (3x3 foundation) and is what makes a grid an outpost.
- D3: Outposts only on the ground layer of a planet.
- D4: Outposts are anchored with `ForceAnchor` + `PreventGridAnchorChanges`; destroying the console unanchors.
- D5: One live outpost at a time per player (`NetUserId`) per round; selling frees the slot, an Abandoned outpost still holds it.
- D6: Owners can name their outpost (30 characters); admins can force a rename.
- D7: Save any time from the console; autosave every 10 minutes (only when something changed) into its own slot, plus a final one at round end.
- D8: 3 manual saves + 1 autosave per character; deletable in the save menu.
- D9: Saves are per character and can't be transferred; live outposts can be sold.
- D10: Saves are viewable, with a preview, in the console and the character editor.
- D11: Saves live in a dedicated DB table with a foreign key to the DB profile id, not in Mono persistent profile data.
- D12: Load price = appraisal x 1.5 (sum of parts + 50%), charged when you press Load, topped up if the staged copy re-appraises higher, refunded if the load fails or is cancelled.
- D13: Loading needs a freshly placed console; the save's console lands on that tile and facing.
- D14: Load clearance refuses other grids' tiles, lava and water features, out of bounds, and anything the placement rule forbids.
- D15: 30 second ghost overlay before the grid appears; mobs and items are pushed out, never gibbed.
- D16: One save predicate decides what is saved and appraised: no players or their gear, mobs, ghost roles, cash, deeds, trade crates or raid loot.
- D17: Excluded entities are stripped from the staged copy at load, since `TrySaveGrid` can't filter single entities.
- D18: Penned livestock is saved as records and respawned at feeders.
- D19: A save that is live this round can't be loaded again this round.
- D20: Every outpost has a lineage; any sale voids every save of that lineage.
- D21: Entities from a loaded save are stamped and can't go into the deposit box or Mono persistent items.
- D22: Preset outposts exist, priced at a fixed price >= appraisal x 1.5, loaded through the same pipeline with MapInit instead of re-init.
- D23: The Planet Outpost Kit costs 25,000 and includes a generator; a bare console costs 6,000 and can't be printed.
- D24: Kits, packs and trade listings are priced at or above the appraisal of their contents; bands are shop prices, and appraisal uses `StaticPrice` = print cost.
- D25: Spawn options are Standard, Shuttle Crash and Outpost Spawn; the choice is round state, not a profile field.
- D26: Non-standard spawns are handled in `RulePlayerSpawningEvent`; outpost players wait in the lobby until their grid lands; failures fall back to Standard with a refund.
- D27: Outpost Spawn appears after a character's first save; before that, presets load from a placed console.
- D28: Play groups are Open, Join Code (hidden) or Private (listed, request-only, plus invites), in memory, cleared each round; leaders can kick.
- D29: Group members and joiners get door access (allow list), never console access or Builder.
- D30: Crash groups can only be joined in the lobby before round start; outpost positions can be joined mid-round.
- D31: Joinable positions are set in the Outpost Configurator (Spawn Options panel, then console Crew tab) and saved with the outpost.
- D32: An outpost with positions is a station (`WFOutpostStation`) with public slots always 0, hidden from stock late-join tabs, joined only through the WF handler.
- D33: Outpost Job = `WFOutpostSettler` job (`setPreference: false`) + role loadout + custom title, no DB migration.
- D34: Spawning at an outpost needs at least one Outpost Cryopod; pods cap group size and positions, max 6 per outpost.
- D35: Outpost Spawn uses the save's own position, with footprints reserved before POIs roll; blocked late loads search 300 m.
- D36: Crash: random sanctioned world (Carcinoma only if every member opted in, 3 g worlds excluded), random small shuttle, random crash job with no billed loadout, orbit layer spawn.
- D37: Crash countdown is 3 minutes to impact on the PA with a locked console; impact severity is clamped to hard landing or a 2-piece break.
- D38: The crash hull is deeded to the leader but only sells for value added above its spawn appraisal.
- D39: Spawn-distance rules as in the table are the one placement rule for console deploys, loads and Outpost Spawn.
- D40: Claim zone = bounding box + 8 tiles; it blocks others founding, loading, non-pad landings and building on raw ground, but not trespass.
- D41: Size cap 64 x 64 / 1,600 tiles / 4,000 entities / 2 MB, final values set from the benchmark.
- D42: The live-outpost cap scales with population (hard cap 30); at the cap, Outpost Spawn and console deploys are refused before charging.
- D43: The ground under an outpost is carved to a reserved no-weather tile; weather strikes and fauna sites skip grid-covered tiles.
- D44: Outpost grids use `RoofComponent`; "outdoors" = on the outpost grid and not rooved, one rule for every gizmo that cares.
- D45: Until Q1 is answered, outposts are PvE: turrets, mines and orbital weapons target NPCs only, only the owner re-founds, only owner and Builders repack.
- D46: Non-faction ships and outposts default to owner-only access; faction ships and outposts start in Faction mode.
- D47: Owner-edited allow lists and codes are for non-faction owners, as the original says; play-group members and joiners are added on every ship and outpost.
- D48: Access is matched by person (mind + character), not card; the owner's deed still works; allow-list ids are networked so doors predict, codes stay on the server.
- D49: Door rules: Default, Owner only, Players, Code, Players or code, Public, Sealed.
- D50: 4-digit codes via right click > Enter Code; misses count per person per ship, 5 in 10 minutes lock that person out ship-wide for 15 minutes.
- D51: Outpost Consoles are owner-only.
- D52: Outposts use `WFOutpostComponent` ownership, not ID-card deeds.
- D53: Outposts sell to the game for the exact current appraisal, untaxed, counting structures and machines but not goods, mid-round or at round end, with shuttle-style sale checks.
- D54: Claimed POIs sell for appraisal minus the claim baseline; a paid load clears the baseline.
- D55: A destroyed console leaves the outpost Abandoned; only the owner can re-found it this round.
- D56: Landing on or over an outpost claim zone is refused; falling hulls are shifted off it.
- D57: Landing pads waive lift; capture within 32 m above the pad; scripted descent and launch; lift-less hulls can only leave by pad.
- D58: The trade panel is buy-only, paid by the owner with the cargo tax split, and needs an outdoor UTH.
- D59: Deliveries parachute from the top air layer onto the UTH; 5 in flight, 60 s ETA.
- D60: Gizmos follow the Nova pattern: fixed stats, no board, own deploy, 3 s repack that keeps the machine itself (and all its state) in the pack.
- D61: T1 recipes are built into the fabricator; T2 and T3 need recipe disks.
- D62: Wind turbines only get the storm bonus in windy storms (not any weather, unlike Nova).
- D63: Nova generator numbers are all scaled by 0.6; solar keeps SS14's panel tiers (on planets, peak x light level, facing ignored, so no tracker) and batteries scale against `SMESBasic`.
- D64: Outpost turrets and mines are hostile only to raider factions and hostile wildlife; livestock are safe through a passive faction.
- D65: Turrets are capped at 2 plus 1 per 300 foundation tiles, max 6, and each adds raid threat.
- D66: Automated producers (drill, thumper, mining bots, power exporter) only run while someone is home; 1 drill and 1 exporter per outpost.
- D67: Farming reuses breeding/eggs/milk/wool; capture, taming, herding, pens and feeders are new.
- D68: Worlds become bounded (default 1536 m square) with a chunk filter and an edge ring, not pre-generation.
- D69: POIs are rerolled each round on fixed-seed terrain, 8 to 14 per world, as separate grids.
- D70: Every POI has a beacon; beacons start as "Unknown signal", and shrines and artifact sites give a faint signal.
- D71: Tradeposts, derelict capital ships, small derelicts, ruins, shrines and artifact sites are not claimable; at most half of a world's claimable POIs can be claimed at once.
- D72: Raids only happen during the Active window (default night), only when someone is home, after a 20 minute grace.
- D73: Raids escalate from a rabid rabbit to a siege, arriving on foot, from burrows, on vehicles or by shuttle.
- D74: Raiders breach across grids with a WF HTN operator, stay awake through a sleep-range override, and are despawned by the director.
- D75: Early Warning gives 120 s (240 s with the long-range mast); without it, 15 s.
- D76: Raiders never target the outpost console; survivors and their shuttle leave at dawn.
- D77: Raid loot can't be sold, isn't paid for in an outpost sale and isn't saved.
- D78: Outposts are exempt from Carcinoma hull infestation (Carcinoma raids more instead), pending Q3.
- D79: Atmospheric jetpacks run on welding fuel, work on ground and air layers but not orbit, and refuse above 1.5 g.
- D80: The work is split into the modules Outposts, OutpostRaids, SpawnOptions, ShipAccess, PlanetPois, Husbandry, plus Planets.
- D81: Admins get outpost and save commands, restore, refund and raid controls, and admin log types from F1.
- D82: Price bands A to E as defined at the top; all numbers are tuning defaults.
- D83: Only Nova art/sound under CC-BY-SA gets ported, with Nova Sector credited in each RSI; Pixabay and unattributed sounds get replaced.
- D84: An outpost claims its column: underground chunks under its claim zone generate as unmineable foundation rock, nothing below depth 0 is saved, a Mine POI claim covers the surface grid only; basements are a later phase.

## Open questions

- Q1: PvP: beyond breaking walls and doors by hand (which the server's Fair Play conflict rules already allow for ships), should players be able to shoot outposts with turrets, bombard them with orbital weapons, take over Abandoned outposts or be kept out of claim zones? A per-world flag or a war declaration? D45 holds until this is answered.
- Q2: Can the Shuttle Crash start be picked mid-round, or only at round start as defaulted here?
- Q3: Should outposts on Carcinoma be exempt from infestation (default here, D78) or be infested as a feature?
- Q4: Kit price (25,000), preset prices (40,000 to 110,000) and the gizmo bands: what does the economy team want?
- Q5: Do crash survivors get a reward for escaping the planet (for example a flat bonus on reaching any station alive), and do they keep their normal bank balance?
- Q6: Should raids happen while the owner is offline but crew are home, or only when the owner is home (default here: any owner or crew at home counts)?
- Q7: Leaving home during an Active window skips the raid at no cost. Should there be a mild off-screen outcome while members are online on the same world (predators taking unpenned livestock or unprotected crops), and should a defeated raid pay a capped defense bounty?
- Q8: Is a save kept if its planet is removed from the game for good (a planet missing for one round is handled in Outpost Spawn in detail), and do saves survive a character wipe or economy reset?
- Q9: Which lookup flags does `Smimsh`'s uncontained lookup cover, i.e. are anchored entities under a landing hull always deleted? Inferred from code only.
- Q10: What happens to loose items on natural ground when their chunk unloads and the tile is emptied? Matters for stuff left outside an outpost.
- Q11: What does the CE z-level system do to a mob or item that walks off the edge of depth-0 ground onto empty tiles? Needed for the world-edge design, and for whether carving could use empty tiles instead of a foundation bed tile.
- Q12: Can `IServerPreferencesManager.SetProfile` safely save a changed loadout mid-round (editing the Outpost Job from the console)?
- Q13: Can `DisallowLateJoin` be true when a queued outpost player is joined with `MakeJoinGame`, turning it into an observer spawn?
- Q14: What access checks does the station records console apply to adjusting job slots, and do outposts need a different gate?
- Q15: How does `ShuttleSystem.Impact` behave for a grid falling from the orbit layer, and does it interact with the crash clamp?
- Q16: Ship doors use a generic Captain tag that opens every ship's Captain doors. Do any ship door prototypes carry other per-door access tags that would change how the default "owner only" rule behaves?
- Q17: How do Mono AI shuttle events move from their own map into play? The raider shuttle arrival wants the same path onto a planet.
- Q18: Which existing POI grids already contain consoles, cryopods or turrets suitable for claimable planet POIs?
- Q19: Nova assets: the license of `AW_reactor.ogg` (freesound, dobroide); the fabricator and arc furnace recordings (given "for free open source use" by an unnamed contributor, with no stated license, so replaced unless cleared); the Kahraman `ore_thumper_fan` loop the Stirling uses; and how the DMI art and its deploy and print animations convert to RSI states.
- Q20: Real power draw of the regulator, recycler, synthesizers and dispenser in use, and a full comparison against SS14 power values, before numbers are locked.
- Q21: Which planet weather types count as "windy" for the turbine storm bonus?
- Q22: Does the Outpost Fabricator accept raw ore at 1:1 like Nova's probably does, or only sheets (which keeps the arc furnace worth building)?
- Q23: Items can still be copied by moving them from a stale save into another player's outpost and reloading (paid at 1.5x their appraisal). Is a persistent-item ledger (one record of where each persistent entity lives) worth building?
- Q24: Should owners of faction ships and faction members' outposts also be able to edit allow lists and codes, on top of faction access?
- Q25: After playtests, is the split of players between planets and space healthy, and how should the population-scaled outpost cap be tuned?
- Q26: Do Crescent shield bubbles and Mono artillery behave on planet maps and CE layers?
- Q27: Should the livestock record approach be extended to pets and tamed wildlife that are not in pens?
- Q28: Undergrounds: are they negative depths in the same network or does the ground shift up; are tunnels that already exist under a newly loaded outpost left or backfilled; and should the ore under a claimed Mine POI be reserved for its owner?
