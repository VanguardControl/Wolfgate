# Outposts

Outposts is the next iteration of planets, and it brings forth the final stages of the whole cycle. Think RIMWORLD.

Outposts aims to introduce buildable outposts, as well as spawning outposts, on planets.

Planets will now be a set size instead of being infinite, allowing for "story generation" style random gen, where there are specific POIs on the map with beacons that players can venture to. These POIs will range from ruins and trade posts all the way to heavenly shrines and alien artifact sites.

Players will have the option to build and save their outpost. Those that wish to start a new one will have to purchase the "Planet Outpost Kit" containing the required building blocks to start their own outpost. The most important part is the "Outpost Console", the outpost version of the shuttle console. It is what determines that the grid is actually an outpost, and it is the command center for all outpost functions.

Outposts will be the first version of our "shipsaving" system, where players will be able to put their Outpost Console down and save from within the console.

After a player has saved their first outpost, a new spawn option becomes available, "Spawn at Outpost", which loads their saved outpost and places them inside it, as long as they have an "Outpost Cryopod" built. More on that in the spawn options section.

Outposts will have access control, defaulting to the player's access (see Ship Access Overhaul; outposts get the same feature).

Outposts are ANCHORED and cannot be moved, even with thrusters put on them, unless you destroy the outpost console, in which case they can be.

Players can only own ONE OUTPOST AT A TIME PER ROUND.

You can set the name of your outpost.

### How to read this doc

Every feature bullet carries a tag:

- **[EXISTS]** already in the fork; the named system is what the outpost hooks, not something we rebuild.
- **[PORT: Nova]** taken from Nova Sector's `colony_fabricator` module (or a sibling Nova module); the `.dm` file is named.
- **[NEW]** has to be built.

Numbers are starting values for tuning. Where a Nova number was rescaled for SS14, the Nova original is given in brackets. Money is in spesos. "Band" is the price band used across the doc:

| Band | Price (spesos) | Feel |
|---|---|---|
| A | 250 to 1,000 | Consumables, small appliances |
| B | 1,000 to 3,000 | Everyday machines |
| C | 3,000 to 8,000 | Core base machines |
| D | 8,000 to 20,000 | Big upgrades |
| E | 20,000+ | Late game, prestige |

Tiers: **T1** available from the kit or first trade order, **T2** mid game, **T3** late game.

Note for devs: the planet stack (layers, flight, weather, parachutes) lives in the `Planets` module on the `planet-cracking` branch, not on `main` yet. Everything here assumes that branch has landed.

### Core rules at a glance

- An outpost is **its own grid** sitting on a planet's ground map, not tiles painted onto the ground. Only what is on the outpost grid is part of the outpost (and gets saved). [NEW]
- The Outpost Console founds the outpost: deploying it on natural ground creates a 3x3 foundation grid under it. [NEW]
- Outposts only exist on the ground layer (depth 0) of a planet. The console refuses to deploy in space, on a hull, on an air or orbit layer, or inside someone else's claim zone. [NEW]
- Anchoring uses `ForceAnchorComponent` + `PreventGridAnchorChangesComponent` + `ShuttleSystem.Disable(force: true)`. The planet stack already treats `ForceAnchor` as "no liftoff, no drift, no orbit decay, no gravity well". [EXISTS]
- Destroying the console removes the anchor (`ShuttleSystem.Enable(force: true)`), so thrusters can move the grid again. [NEW]
- One outpost per player (`NetUserId`) per round, across all their characters. [NEW]
- Outposts get `CleanupImmuneComponent` so Mono grid cleanup never eats one (planet-parented grids are skipped anyway). [EXISTS]
- Name: set in the console, max 30 characters, same cleaning as ship names. The shipyard rename path needs an owning station, so outposts get their own rename. [NEW]

### The Outpost Console

The console is the outpost's brain. It is only usable by the outpost owner (see Ship Access Overhaul). Tabs:

| Tab | What it does | Tag |
|---|---|---|
| Overview | Name, owner, founded round, power summary, residents online, current alert code, raid threat level, "Active" countdown | [NEW] |
| Save | Save to a slot, load, delete, autosave status, preview, preset outposts | [NEW] |
| Access | Access panel and door diagram, same as the ship one | [NEW] |
| Trade | Buy gizmos, see orders in flight, UTH status | [NEW] |
| Crew | Outpost Job editor, joinable positions, play group | [NEW] |
| Defense | Early warning status, turret controls, outpost alert code (Green/Yellow/Red via ShipPa) | [NEW] + [EXISTS] `ShipAlertSystem` |
| Sell | Sell now, "sell at round end" toggle, current appraisal | [NEW] |

- The console has an internal battery, so **saving always works**, even with the base unpowered. Trade and defense need base power. [NEW]
- The console is tough (high structural HP, immune to NPC raider targeting) so a raid can't delete your outpost by accident. Players with explosives can still destroy it. [NEW]
- The console adds an IFF blip and warp point for the outpost on the orbit layer, like `WFOrbitBeacon`, so pilots can find it from orbit. Separate grids on the ground are not drawn by the radar terrain, so without this outposts would be invisible. [NEW] on the [EXISTS] `WFOrbitBeacon` pattern.

### Outpost grids and building

- **Outpost foundation plates** (a tile stack, from the kit and the fabricator): laid on natural ground inside your claim zone and touching your outpost grid, they extend the outpost grid instead of the ground map. This is a hook in `FloorTileSystem`, which the Planets module already edits for lattice-on-ground. [NEW]
- Anything anchored on an outpost tile belongs to the outpost grid. Stuff built on raw ground stays "ground": it is not saved and planet systems treat it as terrain. [NEW]
- **Claim zone**: the outpost's bounding box plus 8 tiles. Nobody else can found an outpost, load a save or land a non-pad hull inside it. [NEW]
- **Size cap**: 64 x 64 tiles bounding box, 1,600 tiles, 4,000 saved entities. The console shows usage. [NEW]
- Carcinoma's hull infestation (`WFCarcinomaInfestationSystem`) would pin and meat-ify an outpost; outposts are exempt (they get more raids there instead). [NEW] on [EXISTS]

## Why an Outpost?

I dunno. It's fun. And you can trade things, and build things more easily. And... you can have a home?

To put that in RimWorld terms:

- **A home.** Your outpost comes back every round, with your stuff in it, where you left it.
- **Easier building.** Tool-less prefab walls, flatpacked machines that deploy in seconds and repack with a right click, a fabricator that prints everything without research.
- **Trade.** The trade panel parachutes supplies in, so you don't need a ship to keep going.
- **Stories.** Night raids, crash landings, a world with ruins and shrines to find, animals to tame.
- **A crew hub.** Friends spawn in your cryopods; your outpost is where the play group lives.
- **A money sink that pays back.** Loading costs parts + 50%, so an outpost is an investment, and you can sell it for its value.

## Outpost Saving / Loading

Outposts can be saved at any time via the Outpost Console, and are autosaved every 10 minutes into an autosave slot.

You save/load your outpost in the Save tab of the console. Saved outposts are linked to your current character and cannot be transferred. You can view your character's saved outposts in the character customiser. A character can have up to 3 outposts saved, plus 1 autosave. You can delete saves in the save/load menu.

This tab shows a preview of your outpost (like the ship previewer).

### What a save is

- A save is the outpost grid serialised to YAML, the same way the used ship market does it (`ShipyardSystem.TrySaveShip`: `TrySaveGrid` with `MissingEntityBehaviour.Ignore`, docks undone first). [EXISTS]
- Saves live in **their own DB table** keyed to the character (DB profile id, cascade delete), like `SafetyDepositBox` does. They are **not** stored as Mono persistent profile items: those are loaded for every character at connect, sent to the client in every prefs message and rewritten on every bank change. [NEW] on the [EXISTS] SafetyDepositBox pattern.
- Each save stores metadata next to the YAML: name, planet surface id, world position and rotation of the console, console position inside the grid, appraisal at save time, tile count, entity count, footprint, format version, round id. [NEW]

### Saving pipeline

1. Owner presses Save (or the autosave timer fires). [NEW]
2. **Strip pass** on a list of what not to save (see below). Nothing is deleted from the live outpost; excluded entities are just left out of the file. [NEW]
3. Livestock in pens are written as **livestock records** (prototype, name, infant or adult, product cooldowns) instead of mob YAML, because mobs are not map-savable. [NEW]
4. `TrySaveGrid` to an in-memory string, compressed. Refuse if over the size cap (2 MB compressed) and tell the owner what is big. [EXISTS] + [NEW]
5. **Appraise** with `PricingSystem.AppraiseGrid` (recurses containers) plus a per-tile value for foundation plates (the appraiser ignores tiles). Store the number. [EXISTS] + [NEW]
6. Write the row asynchronously. The console shows "Saved" with the load price. [NEW]

**Stripped on save:**

| Thing | Why |
|---|---|
| Players and any mob (already non-savable by prototype) | Bodies don't persist |
| Ghost role spawners and ghost role mobs | No free roles from a save |
| Cash (spesos stacks) and anything with a bank link | Money lives in the bank, not in lockers |
| ID cards carrying ship deeds, vouchers | Deeds are round-local |
| Trade crates, bounty items, `CargoSellBlacklist` items | They would dupe cargo income |
| Timed-despawn items, audio, projectiles | Junk |
| Raider loot flagged `WFRaidLoot` still unclaimed | Stops raid-loot farming across rounds |

Everything else stays: machines, items in lockers, silo contents, gas in pipes, charge in batteries, fuel in generators.

### Autosave

- Every 10 minutes per outpost, into slot 4 (the autosave). Timers are offset per outpost, and the server does at most one autosave every 5 seconds, so twenty outposts never save on the same tick. [NEW]
- A final autosave runs on `RoundEndedEvent`, unless the outpost is being sold at round end. [EXISTS] event, [NEW] handler
- If the console is destroyed, autosave stops and the slot keeps the last good save. [NEW]
- Autosave is free. Manual saves are free. Loading is what costs money.

### Limits and rules

- 3 manual slots + 1 autosave per character. Saving over a slot asks for confirmation. [NEW]
- Saves are per character, shown in a new **Outposts** tab in the character editor (list, planet, price, last saved, preview). The Mono "saved items" tab already previews saved YAML on the client, so the pattern exists. [NEW] on [EXISTS]
- Deleting a character deletes its saves (cascade). [NEW]
- Saves cannot be moved to another character or player. A **live** outpost can be sold to another player (see Ship Access Overhaul); the buyer then saves it into their own slots. [NEW]
- A save that is **live this round** is marked "in use" and cannot be loaded a second time this round (the SafetyDepositBox "withdrawn round" trick). [NEW]

### Preview

- The Save tab and the character editor preview a save in the ship previewer (`Content.Client/_WF/ShipPreview`). The previewer only loads file paths today, so it needs a `TextReader` overload; the map loader already has one. [EXISTS] + [NEW]
- The server sends the save YAML only to its owner, on request, size-capped and rate-limited. The lobby list gets metadata only. [NEW]

### Loading a Saved Outpost

In order to load your previously saved outpost you must first place down an outpost console (bought, or from a kit), then go to the Save tab and click Load. Each save is priced as the sum of the value of all of its parts + 50%. When you press Load, it is deducted from your account (if you have enough funds). The game then checks the area for any non-planetary turfs (to prevent griefing by loading over other grids), makes sure it's safe to load, and gives a 30 second warning to clear the area (a ghost overlay shows). Then the grid loads, with the outpost console exactly where it is in your saved grid, making it the "anchor point".

Step by step:

1. **Place a console** on natural ground. It founds a fresh 3x3 outpost. Load only works from a fresh console (nothing built on it yet). [NEW]
2. **Rotate to aim.** The saved outpost is placed so its console lands on your console's tile, facing the same way. Rotating the placed console rotates the whole load in 90 degree steps. [NEW]
3. **Press Load.** Price = stored appraisal x 1.5. Charged with the session form of `BankSystem.TryBankWithdraw` (works from the lobby too). Refused if you can't afford it. [EXISTS] + [NEW]
4. **Clearance check** on the footprint (every saved tile, translated to the target): [NEW]
   - every tile is natural planet ground (no tile of another grid: hull, outpost, POI);
   - no lava or deep water tiles;
   - inside the planet bounds, and not on detached terrain;
   - not inside another outpost's claim zone, and not inside a POI's exclusion radius;
   - you don't already own a live outpost this round.
   A failed check shows the offending tiles in red and refunds in full.
5. **30 second warning.** A ghost overlay of the footprint appears for everyone nearby, with a countdown and a popup for anyone standing inside it. The owner can cancel for a full refund. [NEW]
6. **Re-check at T-0.** If a grid moved in, abort and refund. [NEW]
7. **Clear the ground.** Mobs inside the footprint are pushed to the nearest edge tile (never gibbed). Loose items are moved to the edge. Rocks and trees under the footprint are removed and the tiles reserved (`BiomeSystem.ReserveTiles`, the `GroundGrid` precedent). [EXISTS] + [NEW]
8. **Swap.** Delete the fresh 3x3 grid, `TryLoadGrid` the save onto the ground map at the computed offset and rotation. [EXISTS]
9. **Re-init.** A loaded grid comes back "initialised" but skips every map-init handler, so run `UsedShipReinitSystem.ReinitLoadedShip` (device networks, wires, charge, PA speakers, nav map) plus outpost extras: apply `ForceAnchor` explicitly, `CleanupImmune`, ownership, access binding, livestock respawn at feeders, IFF beacon. [EXISTS] + [NEW]
10. Mark the save "in use this round". [NEW]

### Preset outposts

There will also be some preset base game outposts that you can spawn for their set price, or you can just build your own from scratch.

- Presets are prototypes (`wfOutpostPreset`) with a grid in `Resources/SharedMaps/_WF/Outposts` (client builds drop `Resources/Maps`, and the previewer must load them). [NEW]
- They show up in the Save tab under "Presets" and in the Spawn at Outpost list, and load through the same pipeline at their fixed price. [NEW]
- The universal vessel/outpost test checks every preset: price >= appraisal x 1.5, has a console, at least one cryopod, a UTH, fits the size cap. [NEW]

| Preset | Contents | Cryopods | Price band |
|---|---|---|---|
| WFOutpostHomestead | 7x9 sealed hab, 4 wind turbines, small battery, fabricator, rations printer, 4 hydro trays, UTH | 2 | E (about 40,000) |
| WFOutpostMiningCamp | Hab, arc furnace, ore silo, ground drill, ore boxes, 2 solar strings, UTH, landing pad | 3 | E (about 60,000) |
| WFOutpostTradepost | Hab, big storage, landing pad, UTH, telecom relay, 2 turrets | 4 | E (about 75,000) |
| WFOutpostFortified | Walled compound, 4 turrets + control panel, early warning mast, RTG, battery bank, pen | 6 | E (about 110,000) |

### Planet Outpost Kit

The starter crate. Modelled on Nova's "Colonization Starter Kit" (`cargo_packs.dm`: fab, organics printer, GPS beacon, 50 plastic wall panels, 25 rods, 20 iron, 2 manual airlocks, APC set, battery, 11 crate values). Nova's kit has no generator; ours ships one, because a dead console on night one is no fun.

| Item | Qty | Tag |
|---|---|---|
| Outpost Console (flatpack) | 1 | [NEW] |
| Universal Trade Hub (flatpack) | 1 | [NEW] |
| Outpost Fabricator (flatpack) | 1 | [PORT: Nova] `colony_fabricator.dm` |
| Outpost Cryopod (flatpack) | 1 | [NEW] |
| Outpost foundation plates | 60 | [NEW] |
| Prefab wall panels | 50 | [PORT: Nova] `construction/turfs.dm` |
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

- **Price: 25,000 spesos** (band E). Sold at the shipyard outfitters and WF trader vendors, and at tradepost POIs. A bare Outpost Console is sold on its own for 6,000 (band C) for people loading a save. [NEW] on [EXISTS] WF Traders
- Add-on packs on the trade panel, modelled on Nova's "Frontier Kitchen Equipment" (1,000 cr) and "Hydroponics Plumbing Synthesizer Pack" (400 cr): Kitchen Pack, Farm Pack, Atmos Pack (CO2 cracker, scrubber, temperature regulator, 2 wall heaters), Defense Pack (2 turrets, control panel, early warning), Power Pack (4 solar, tracker, large battery). [NEW] bundles

## Spawn Options

Players can now pick how they start the round, which overrides how they spawn in entirely.

There is a spawn menu (new **Spawn Options** button in the lobby, next to Ready) that lets you configure each spawn type (see the options per spawn option below). Pre-round you can select and set up your Outpost / Shuttle Crash scenarios.

"Spawn at Outpost" shows your outposts that can be played, plus the base maps (presets) to start with, costing the amount from your bank account.

### Lobby flow

1. Open Spawn Options. Pick Standard (default), Shuttle Crash or Outpost Spawn. The choice is kept in new server state for this round, **not** in the profile's `SpawnPriority` (that field is DB-stored and marked "do not touch"). [NEW]
2. Configure: for Outpost Spawn pick a save or preset, see price and planet; for Crash, nothing to pick (it is random), just group size. [NEW]
3. Optionally create or join a Play Group. [NEW]
4. Press Ready. The lobby shows your pick under the Ready button. [NEW]
5. At round start, the server handles non-standard picks in `RulePlayerSpawningEvent` (broadcast, after maps and rules load, before job assignment; the documented contract is "take players out of the pool and join them yourself", exactly what antag selection does). Anyone whose pick fails falls back to Standard, with a chat message saying why and a refund. [EXISTS] event + [NEW] handler

### What the server checks

| Check | Outpost Spawn | Shuttle Crash |
|---|---|---|
| Player is ready and prefs are loaded | yes | yes |
| Save belongs to the selected character and is not in use this round | yes | n/a |
| Player doesn't already own a live outpost this round | yes | n/a |
| Target planet exists this round, is built and sanctioned (or the player opted in) | yes | picks one |
| Balance >= load price + billed loadout | yes | n/a |
| Footprint fits at the saved position, or a valid spot within 300 m | yes | n/a |
| A crash site exists that meets the spawn-distance rules | n/a | yes |
| Group members are ready, and group size fits cryopods / seats | yes | yes |
| Job bans: the Outpost Job and crash jobs go through `GetDisallowedJobsEvent` | yes | yes |

Round-start loads are queued (one grid per couple of ticks) so twenty outposts don't hitch the first second of the round.

### Multiplay Spawn Options

Some spawn options have Multiplay. You can set up your play group to be joinable pre-game, so another player can configure themselves to join your outpost or crash landing at roundstart, if you allow it. You can also invite a player to join. They'll be set up with the same access to doors and such (they are added to the access list).

You can create **Play Groups**, essentially sub-lobbies of the game that players can join to be part of your multiplay group. You can set a group to Open / Join Code / Private (requests are sent to you as a notification pre-roundstart, and you can directly invite a connected player).

In the Outpost Configurator you can also set up joinable positions for your outpost, which again other players can join; they spawn at the outpost cryopods.

For the crash landed start, players can only join your play group in the lobby before the round starts, and they spawn on the ship with you.

**Play Groups in detail** [NEW]:

- A registry keyed by `NetUserId`, in memory, cleared on `RoundRestartCleanupEvent`. Nothing like it exists (`_Rat` squads are in-round and entity-based; Mono Company is a faction). [NEW]
- Fields: leader, name, mode (Open / Join Code / Private), 6-character join code, scenario (Crash, or Outpost + save id), max size, member list, pending requests, pending invites.
- **Open**: anyone in the lobby sees it in the group browser and joins. **Join Code**: hidden, joined by code. **Private**: hidden; players send a request (the leader gets a lobby notification), or the leader invites any connected player.
- Max size: Crash 6 (and never more than the picked shuttle's seats); Outpost = number of outpost cryopods in the save.
- Members' own spawn option is locked to the group's scenario while they are in it.
- If the leader isn't ready at round start, the group falls back to Standard. Members who didn't ready up: for an outpost they can still join later through joinable positions; for a crash they miss the flight.
- The invite and request UX copies the WF ERT builder's message pattern (`ErtCalledEvent` / `ErtSignUpEvent` and its slot balancing). That flow is ghost-only, so only the pattern is reused. [EXISTS] pattern
- Group chat in the lobby: nice to have, later phase.

**Joinable positions** [NEW]:

- Set in the console's Crew tab: number of slots, a title, an optional preset loadout, and visibility (Open / Code / Group only / Private invite).
- The outpost is made a station at load (`InitializeNewStation` from a `WFOutpostStation` config with `StationJobs` + `StationSpawning`), holding a `WFOutpostSettler` job. That is how ships offer crew slots today, and it makes the late-join picker, job slots, mind setup and `PlayerSpawnCompleteEvent` subscribers all work for free. [EXISTS] pattern
- Positions show in a new **Outposts** tab of the late-join picker (next to the NF Crew tab). Joining goes through a WF request message, the server checks visibility and code, then calls `GameTicker.MakeJoinGame(player, outpostStation, WFOutpostSettler, silent: true)`. `IsJobAllowedEvent` carries no station, so the gate has to live in our request handler. [NEW] + [EXISTS]
- Joiners spawn in an outpost cryopod and are added to the outpost's access list for the round.

### Currently Proposed Spawn Options

1. **Standard**: normal spawning as your selected job. (Difficulty: Normal) (Options: Role) [EXISTS] unchanged.
2. **Shuttle Crash**: you spawn in a randomly selected shuttle, crash landing on a randomly selected world, as a randomly selected job. There is a crash landing sequence on the ship; the console cannot be controlled during it. The PA announces that the shuttle will crash in 3 minutes (time you have to prepare) and then enter atmosphere. The shuttle spawns in the orbit layer of the planet. (Difficulty: Very Hard). The player then has to scrounge up materials to escape the planet. Spawning takes nearby POIs and other players' outposts into account so it lands far enough away not to impact them. (Options: Multiplay) [NEW] on lots of [EXISTS]
3. **Outpost Spawn**: you spawn on your outpost as your Outpost Job (a custom job you create for yourself in the outpost console, which lets you pick the starting jumpsuit etc., basically the loadout menu). The outpost is spawned on the planet you saved it on, deducting the price of the outpost from your balance, and rejecting if you don't have enough. (Options: Selected Outpost to Spawn at, Multiplay) [NEW]

### Outpost Spawn in detail

- Available at round start and as a late join, as long as you don't own a live outpost this round. [NEW]
- Placement: the save's own world position on its own planet. POIs are rerolled every round, so at round start the server **reserves the footprints of every readied Outpost Spawn before POIs are placed**. Late loads that find their spot blocked search outward (spiral, up to 300 m) for the nearest valid spot. [NEW]
- If the save's planet isn't in this round's system, the player picks another built world in the menu (the save keeps its layout, only the spot changes). [NEW]
- Mid-round, the 30 second clearance overlay runs before the grid appears; the player waits in the lobby with a countdown. [NEW]
- Owner spawns in a cryopod as their Outpost Job; group members spawn in the remaining pods. [NEW]

**Outpost Job** [NEW on EXISTS]:

- A real `JobPrototype` `WFOutpostSettler` + a `RoleLoadoutPrototype` `JobWFOutpostSettler` (its gear groups: jumpsuit, shoes, head, back, gloves, belt, PDA...) + a `customJobTitle` entry with the same id for the name. Profile loadouts are keyed by string, and `ProfileRoleLoadout` already stores `CustomJobTitle`, so **no DB migration**. [EXISTS] WF Roles
- Edited in the console's Crew tab (reusing `LoadoutWindow`) and in the character editor's Jobs tab as its own row. Loadout prices bill from the bank at spawn, as usual. [EXISTS]
- The title follows `CustomJobTitleRules` (no impersonating real jobs). [EXISTS]
- Access: the settler job has no station access tags; outpost doors use the outpost access list.

**Outpost Cryopod** [NEW on EXISTS]:

- A pod with the Frontier `CryoSleepComponent` (so you can also cryo out of it), plus a WF spawn handler on `PlayerSpawningEvent` ordered before `SpawnPointSystem` that inserts the new body into a free pod on the outpost. `ContainerSpawnPointSystem` only fires for the Cryosleep spawn priority, so it can't be reused as is. [EXISTS] + [NEW]
- Works unpowered for spawning (no soft-locks from a flat battery). Must be anchored on the outpost grid.
- At least one cryopod is required to spawn at an outpost. The number of pods caps group size and joinable positions.
- Cryo-ing out on an outpost reopens the slot. Frontier only reopens slots on `ForceAnchor` grids, which outposts are. [EXISTS]

### Shuttle Crash in detail

Setup at round start (inside `RulePlayerSpawningEvent`):

1. **Pick a world**: random from built, sanctioned worlds. Carcinoma (unsanctioned) only if the whole group ticked "I want pain". Worlds with gravity too high for a crashed hull to ever climb (Aerumna, 3 g) are left out. [NEW]
2. **Pick a shuttle** from a crash pool (`wfCrashPool`: small civilian vessels with enough seats for the group and a powered PA speaker or air alarm). [NEW] on [EXISTS] vessel prototypes
3. **Pick jobs**: each member gets a random job from the crash job pool (pilot, engineer, medic, cook, miner, salvager...), with that job's starting gear only. No billed loadout. [NEW]
4. **Pick a site** that meets the spawn-distance rules (below), then load the shuttle onto the **orbit layer** at that XY. [EXISTS] `TryLoadGrid` + [NEW] site picker
5. **Own it**: `ShipyardSystem.TryAssignDeed` to the leader, so the deed, console lock, ship access and records all work. [EXISTS]
6. **Spawn the crew** with the ERT builder pattern: `SpawnPlayerMob` at seats (buckled) or spawn points, create the mind, add the job role and raise `PlayerSpawnCompleteEvent` ourselves so WF, Mono and NF subscribers see the spawn. [EXISTS] pattern

The sequence (times from round start):

| Time | What happens | Tag |
|---|---|---|
| T+0:00 | Crew wakes buckled in. `WFCrashSequenceComponent` goes on the grid: console UI refused, pilots cleared (`ShuttleConsoleSystem.ClearPilots`), thrusters forced off. The normal owner lock can't be used because the owner's ID unlocks it. | [NEW] |
| T+0:00 | PA: new non-selectable alert code `WFAlertCrash`, klaxon loop (`ShipPaSystem.StartAlarm`), announcement "Orbit failing. Impact in 3 minutes. Grab what you need and strap in." | [EXISTS] ShipPa |
| T+0:00 | Orbit decay is suppressed on this grid (it would otherwise start its own 60 s countdown). | [NEW] |
| T+1:00 | PA: "2 minutes." | [EXISTS] |
| T+2:00 | PA: "1 minute. Suits on, find a seat." | [EXISTS] |
| T+2:50 | PA: "10 seconds. Brace." | [EXISTS] |
| T+3:00 | `WFOrbitEntrySystem.TryDropFromOrbit(grid)`. Lift is forced lost. The existing GPWS callouts play on the way down (lift lost, sink rate, terrain, pull up). | [EXISTS] |
| about T+3:20 to T+3:40 | Impact. Severity is clamped to a hard landing or a structural crash into at most 2 sections, never the full per-tile explosion crash. Crash-damaged APCs flicker until repaired, as built. | [EXISTS] + [NEW] clamp |
| After impact | Console lock lifts, thrusters come back (damaged), code switches to "Crashed" (amber). If the PA has no working speaker, announcements fall back to a chat message to everyone aboard. | [NEW] |

How you get off the planet (the goal): repair the hull and fit landing thrusters (`WFLandingThrusterKit`) until lift >= 1, or find a small derelict POI that can fly, or reach a tradepost and buy passage, or build an outpost and a landing pad and call a ship. [EXISTS] + [NEW]

### Spawn-distance rules

| New thing | From outpost claim zones | From POIs | From crash sites | From world edge |
|---|---|---|---|---|
| Crash site | 300 m | 250 m | 200 m | 128 m |
| New outpost (console deploy or save load) | 150 m (outside any claim zone) | 100 m (unless claiming that POI) | 100 m | 64 m |
| POI (story-gen) | 200 m from reserved footprints | 150 m | n/a (placed first) | 100 m |
| Raid spawn | 40 m from the outpost edge | n/a | n/a | inside bounds |

NF's POI placer returns its last roll even when it fails; ours gives up and falls back instead (Crash falls back to Standard with a message). [NEW]

## Ship Access Overhaul

Ships now get an access options panel in the shuttle console. A bought ship or outpost defaults to ONLY the player's access UNLESS they are part of a faction, in which case the shuttle spawns with the faction access as normal. The owner of a non-faction vessel can add specific people to the access list, or set an "Access Code" which gates doors behind that code. Each door on the shuttle can be configured individually as well (on the access diagram, a wire diagram showing selectable doors). So you can set a specific door to a specific player or code. To open a code-locked door when you don't have access, right click the door > Enter Code, and a code UI lets you enter the 4 digit code, opening the door.

Outpost Consoles are different: they are only accessible to the outpost owner. You can sell your outpost to other players as well. Outposts can be sold at round end, or mid-round in the console (same shuttle sell checks occur) for the exact calculated value of the outpost at the time.

### What exists today

- Mono deed access: `ShipAccessReaderComponent` on doors and lockers, checked by `ShipAccessReaderSystem` (admin ghost, then faction company match for hard-coded USSP/Rogue/TSF, then a deed on a held card, then guest cards). Disabled until the owner flips "lock ship". [EXISTS]
- Console verbs: lock/unlock console, guest access, reset guest access, lock/unlock ship (`ShuttleConsoleLockSystem`). No per-door rules, no codes, no per-player list. [EXISTS]
- Deeds are per ID card; `DeedCopySystem` copies a deed to a crewmate's card. [EXISTS]
- Doors have no right-click verbs today. [EXISTS] gap

### Data model [NEW]

On the grid, `WFShipAccessComponent`:

| Field | Meaning |
|---|---|
| Owner | `NetUserId` + character name (outposts: + DB profile id) |
| Mode | Private (default for non-faction) or Faction (default for faction ships) |
| AllowList | entries of `NetUserId` + character name + label ("Bob, medic") |
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
- The check hooks into `ShipAccessReaderSystem.HasShipAccess` with a WF event (the `(ShipAccessReaderComponent, BeforeDoorOpenedEvent)` pair is taken, so we don't subscribe a second time). [NEW] marked hook
- Faction ships: Faction mode keeps the current company rule and access tags. The owner can add people and codes on top, but can't remove faction access. [EXISTS] + [NEW]
- Existing verbs map onto the model: "guest access" adds you to the allow list, "reset guest access" clears it, "lock/unlock ship" flips Locked. [EXISTS] verbs, [NEW] backing

### Codes [NEW]

- Right click a code door > **Enter Code** (only offered if you don't already have access) opens a 4-digit keypad.
- 3 wrong codes in 60 s locks that keypad for 30 s and logs it on the console. Brute forcing 10,000 codes at that rate is not practical.
- Codes can be changed any time; changing one doesn't kick anyone already inside.

### UI [NEW]

- New **Access** tab on the shuttle console and the outpost console.
- Left: the ship outline (reusing the WF ShipStatus whole-ship view), doors drawn as selectable nodes, coloured by rule.
- Right: ship settings (mode, code, locked), allow list (add a player standing near the console, or type a name from the crew list), and the selected door's rule.
- Only the owner can edit. Crew see a read-only view of what they can open.

### Outpost ownership and sale [NEW]

- Outposts don't use the ID-card deed (one deed per card would stop a ship owner from also owning an outpost). They use `WFOutpostComponent` ownership (`NetUserId` + character), re-bound at load.
- **Console access**: owner only. Play group members and allow-listed players get doors, not the console.
- **Sell to the game**: in the Sell tab, for the exact current appraisal (not parts + 50%, and not the shipyard's 0.85 rate). Same checks as selling a shuttle: no living organics aboard other than you, no hostiles within 32 m, not mid-raid. The outpost is deleted, the save slots stay. Paid with `TryBankDeposit`. [EXISTS] + [NEW]
- **Sell at round end**: a toggle; at `RoundEndedEvent` the outpost is appraised and paid out, and no final autosave is written. [NEW]
- **Sell to a player**: owner sets a price and names a buyer standing at the console; the buyer must not own a live outpost this round and must accept and pay. Ownership, console and allow list transfer. The seller keeps their save slots. [NEW]
- **Claimed POI outposts** pay only for what you added: the sale value is appraisal minus the claim baseline stored at claim time, so claiming and selling free POIs isn't a money printer. [NEW]
- **Console destroyed**: the outpost becomes "Abandoned". The owner can place a new console within 10 minutes and keep it. After that, anyone without an outpost can found one on the grid with a new console. [NEW]

## Outpost Landing Pads

Currently, the only way to land on a planet is with a ship that has enough lift.

Outpost landing pads let you designate an area of your outpost where a shuttle can land without the lift requirement. Such ships can only land at the pad and take off from it, and they have to be roughly above it. It acts as a docking port essentially (in game it looks like a small staircase with railings).

### How landing works today (as built)

- You descend from orbit with the console's Enter Atmosphere button, and land **anywhere** under your hull's XY. There is no pad, beacon or slope rule. [EXISTS]
- On touchdown, `Smimsh(explodeGrids: true)` deletes everything under the hull and **explodes both grids** where the hull overlaps another grid. Landing on an outpost today would wreck it. [EXISTS]
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

- A 3x3 receiving pad with a beacon post. One per outpost. Must be on the outpost grid and **open to the sky** (not roofed, checked like weather exposure). A roofed UTH can't receive drops.
- Needs power (2 kW) to take orders.

### Ordering [NEW on EXISTS]

- The catalogue is data: cargo-product style listings in an outpost group, with categories matching the gizmo tables below. [EXISTS] `CargoProductPrototype` shape
- Payment from the owner's bank (or the orderer's, if the owner allows crew orders), with the same sector tax split cargo consoles use (Frontier, NFSD, Medical). [EXISTS] `BankSystem`, cargo tax accounts
- Up to 5 crates in flight per outpost. ETA 60 s + 10 s per extra crate.
- No selling here. Selling happens the normal way: haul it to a depot, or sell the outpost.

### Parachute delivery [EXISTS]

- When an order is due, the crate spawns on the **top air layer** above the UTH with `WFParachutedComponent` (air layers have gravity, so no push is needed). It falls about 3 layers at 1.5 layers/s, the chute opens on the first level drop, and it lands softly on the pad. [EXISTS] `WFParachuteSystem`
- It lands on the UTH centre +/- 1 tile. If the pad is blocked it lands on the nearest clear tile within 5 tiles; if there is none, the order waits and refunds after 10 minutes. [NEW]
- The used parachute pack is left at the drop site, as built (recyclable). [EXISTS]
- The UTH beeps and the console shows "Delivered". [NEW]

## Outpost Gizmos

Outposts get a bunch of unique-to-them items that make outposts more viable and interesting. Here's the list so far (it will be updated). Think comms, wind gen, cheaper component generation, mining bots and so on.

### The gizmo pattern

Nova's colony machines all follow one recipe, and we copy it:

- **Fixed stats, no board, no parts, no upgrades.** What you buy is what you get. [PORT: Nova] shared framework in `colony_fabricator.dm`
- **Flatpacked**: deploy by using the pack in hand on the tile in front of you (2 to 4 s). Upstream `FlatpackComponent` does the unpack (it wants a pulsing tool today; outpost packs drop that requirement). [EXISTS] + [NEW]
- **Repack**: right click with an empty hand > Repack, 3 s. The machine folds back into its pack **with its state kept on the pack** (charge, fuel, stored materials) or refuses while not empty. Nova's version drops the board and cell next to the pack and loses the charge; we don't. [PORT: Nova] `repacking_element.dm`, fixed
- **Printed at the Outpost Fabricator** or bought on the trade panel.
- **Pack value = print cost**, so the save price (parts + 50%) is honest.

### General

| Gizmo | What it does | Numbers | Source tag | Tier / band |
|---|---|---|---|---|
| Outpost Console | Founds the outpost; save/load, access, trade, crew, defense, sale | Internal battery for saves; 1 kW for trade/defense | [NEW] | T1 / C (in kit) |
| Time/weather console | Tells you the time and weather, plus the forecast: next weather change, storm phase (30 s telegraph), time until night and until the "Active" window | 200 W. Reads `WFPlanetWeatherSystem.TryGetReport` | [EXISTS] timepiece report + [NEW] console | T1 / A |
| Outpost GPS beacon | Marks the outpost (or any spot) on the orbit radar with a name; handy for pads and friends | 100 W, range: whole orbit layer | [PORT: Nova] kahraman `gps beacon`, on the [EXISTS] `WFOrbitBeacon` pattern | T1 / A |
| Outpost Cryopod | Spawn point for you and your crew; also a cryo exit | Unpowered for spawning | [NEW] on [EXISTS] `CryoSleepComponent` | T1 / B |
| Landing pad | See Landing Pads | 5 kW while guiding | [NEW] | T2 / D |
| Universal Trade Hub | Receives parachute drops | 2 kW | [NEW] | T1 / B (in kit) |
| Wall multi-cell charger | 3 cells at once | 30 kW per cell (Nova: same) | [PORT: Nova] `wall_cell_charger.dm` | T1 / A |
| Outpost PA speakers | Outpost-wide announcements and alert codes | As ship speakers | [EXISTS] `ShipPa` (works on any grid) | T1 / A |
| Floodlight post | Lights the yard; raids come at night | 300 W | [EXISTS] stock light posts | T1 / A |

### Construction

| Gizmo | What it does | Numbers | Source tag | Tier / band |
|---|---|---|---|---|
| Outpost foundation plates | Extend the outpost grid over natural ground | 1 tile each, 0.25 steel | [NEW] | T1 / A |
| Prefab wall panel | Right click open ground (or a foundation) to raise a wall; no girder, no tools, no power | 3 s. About half a steel wall's durability; breaks back into 1 panel. Nova: hardness 70 vs 40, half slicing time | [PORT: Nova] `construction/turfs.dm` | T1 / A |
| Prefab window | Full-tile window from a panel | 1 s | [PORT: Nova] `windows.dm` | T1 / A |
| Manual door kit | Unpowered door, 1 s to open or close by hand, ignores bumps (raiders can't tailgate) | No power | [PORT: Nova] `manual_door.dm` | T1 / A |
| Prefab airlock kit | Finished powered airlock, deploys in 4 s; takes access rules | Area power | [PORT: Nova] `doors.dm` | T1 / A |
| Prefab shutters kit | Deploys open, needs a button | Area power | [PORT: Nova] `doors.dm` | T1 / A |
| Wooden fence + gate | Pens, yards, cheap perimeter | No power | [EXISTS] stock wooden fences | T1 / A |
| Inflatable wall/door | Emergency pressure seal, fragile | Melts in fire | [EXISTS] stock inflatables | T1 / A |

### Tools

| Gizmo | What it does | Numbers | Source tag | Tier / band |
|---|---|---|---|---|
| Powered driver | Screwdriver + wrench + wirecutters in one | Tool speed 1 | [EXISTS] stock power drill (Nova `tools.dm` omni drill adds cutters) | T1 / A |
| Arc welder | Electric welder, runs off a cell | Heavy cell drain | [PORT: Nova] `tools.dm` | T1 / A |
| Compact drill | Mining drill that fits a bag | Tool speed 0.6 | [EXISTS] stock mining drill | T1 / A |
| Herding whistle | Commands tamed animals (see Farming) | 8 tile range | [NEW] | T1 / A |
| Lasso | Catch an animal (see Farming) | 6 tile throw, 3 s struggle | [NEW] on [EXISTS] WF `Tether` ropes | T1 / A |

### Defense

| Gizmo | What it does | Numbers | Source tag | Tier / band |
|---|---|---|---|---|
| Early Warning System | Radar mast that alerts you of an incoming attack with a small grace period. Announces on the outpost PA and sets code Red | 2 kW. Warning 120 s (240 s with the long-range upgrade module) before arrival | [NEW], announces via [EXISTS] `ShipPa` | T1 / B |
| Motion sensor post | Pings the console when something walks into range; cheap perimeter | 7 tile radius, 100 W | [EXISTS] `TriggerOnProximity` | T1 / A |
| Outpost ballistic turret | Magazine-fed deployable turret. Exempts anyone on the outpost access list | Magazine fed | [EXISTS] `DeployableTurret` + `TurretTargetSettings`, reference [PORT: Nova] `magfed_turret` | T2 / C |
| Outpost laser turret | Grid-powered turret | 3 kW idle, more firing | [EXISTS] energy turret bases | T2 / C |
| Turret control panel | Arms/disarms turrets, sets targeting | Wall mount | [EXISTS] `DeployableTurretController` | T2 / B |
| Land mine | Proximity mine for approaches | One use | [EXISTS] stock land mine | T2 / A |
| Outpost shield generator | Grid-wide bubble | 150 kW. The Mono POI bio generator is literally "for stationary outposts" | [EXISTS] `ShieldGeneratorPOIBio` | T3 / E |

Turret friend/foe in SS14 is by NPC faction. Outpost turrets use the access-exemption path (`TurretTargetSettings`) so owners, crew and livestock are safe and raiders aren't.

### Power Generation

| Gizmo | What it does | Numbers | Source tag | Tier / band |
|---|---|---|---|---|
| Wind turbine | Turns to make power. brrrrr. Must be outdoors (not roofed) with at least 5 kPa of air | 5 kW, 15 kW during a windy storm's main phase (Nova: 2.5 / 10 kW, any weather) | [PORT: Nova] `wind_turbine.dm`, on [EXISTS] `RoofComponent` + `WFPlanetWeatherSystem` | T1 / A |
| Deployable solar panel | Planet solar string; output follows the local day/night light level | Wolfgate NF panel values, x0 at night | [EXISTS] `NFPowerSolarSystem` panels, flatpacked like [PORT: Nova] `solar_panels.dm` | T1 / A |
| Solar tracker | Points panels at the sun | Needs the solar console | [EXISTS] `SolarTracker` | T1 / A |
| Small stationary battery | Low storage, high throughput | Nova: 10 MJ, 400 kW in/out; rescale against stock SMES | [PORT: Nova] `power_storage_unit.dm` | T1 / B |
| Large stationary battery | High storage, low throughput backup | Nova: 100 MJ, 50 kW in/out | [PORT: Nova] `power_storage_unit.dm` | T2 / C |
| Compact RTG | Flat power forever, no fuel. Fragile, lightly radioactive, explodes when destroyed | 20 kW (Nova 15 kW; Wolfgate's `GeneratorRTG` is 40 kW). Pack needs uranium + plasma | [PORT: Nova] `rtg.dm` | T2 / C |
| A.W. fuel generator | PACMAN-style generator burning uranium sheets, 4 power levels, vents hot water vapour | 20 to 80 kW (Nova same, 2x PACMAN); 25 sheets; one sheet lasts 6 min at L1, 1.5 min at L4 | [PORT: Nova] `solid_fuel_generator.dm` on [EXISTS] portable generator family | T2 / C |
| Stirling generator | "TEG at home": hot gas in a pipe vs the room's air. Doesn't work in vacuum | 18.75 W per K difference, cap 150 kW at 8000 K | [PORT: Nova] `stirling_generator.dm` | T3 / C |
| Power exporter | Sells surplus grid power for spesos | Pays per MWh exported, capped per hour | [PORT: Nova] `powerator` | T3 / D |

Rule of thumb for the power ladder: wind < solar < RTG < A.W. < stirling, same ratios as Nova, scaled to Wolfgate's own generator numbers.

### Fabrication

| Gizmo | What it does | Numbers | Source tag | Tier / band |
|---|---|---|---|---|
| Outpost Fabricator | Prints every outpost gizmo, construction piece and tool with no research. Slow on purpose, no material discount, can't be upgraded. Can print another fabricator | Needs an APC; 500 W printing. Print times roughly 10x a normal lathe | [PORT: Nova] `colony_fabricator.dm` as a [EXISTS] `LatheComponent` with static recipe packs | T1 / C (in kit) |
| Arc furnace | Smelts one ore stack at a time at 1.5x yield; vents hot CO2 (ventilate it) | 10 kW, 1 s per ore, exhaust about 1200 K | [PORT: Nova] `arc_furnace.dm` (Mono already has arc furnace sprites/protos to lean on) | T1 / B |
| Materials recycler | Hand-fed; turns junk back into sheets | Returns 80% (Nova advertises 80% but returns 100%; we fix it) | [EXISTS] `MaterialReclaimer`, outpost flatpack | T1 / B |
| Organics printer | Biomass into plastic and cloth (plastic feeds prefab walls) | Plastic sheet 25 biomass, cloth 10 | [EXISTS] biogenerator + [PORT: Nova] kahraman `organic_printer.dm` recipes | T1 / B |
| Parts press | Cheaper component generation: prints stock machine parts (capacitors, manipulators, bins, lasers) | 60% of normal lathe material cost, 2x time. Outpost grids only | [NEW] | T2 / B |
| Flatpacker | Packs stock machines into flatpacks | As stock | [EXISTS] `MachineFlatpacker` | T2 / B |
| Ground drill | Planet drill that coughs up ore on natural ground | 50 kW, ore every 10 s | [EXISTS] Goob `ItemMiner` + `PlanetMiner` (with `requireExpedition: false`) | T2 / C |
| Ore thumper | Outdoor on sand/snow/ash-type ground; slams every 15 s, drops a weighted ore box every 30 slams; stops if more than 5 ore piles nearby | 50 kW, 2 tile spacing | [PORT: Nova] kahraman `ore_thumper.dm` | T2 / D |
| Mining bot | Small drone that mines nearby rock and hauls ore to the outpost silo or an ore box | Max 3 per outpost, 1 kW to recharge at a dock | [NEW] (HTN) | T3 / D |

### Storage

| Gizmo | What it does | Numbers | Source tag | Tier / band |
|---|---|---|---|---|
| Outpost ore silo | Shared materials for linked machines on the outpost. Contents are saved with the outpost | Link range as stock | [EXISTS] `OreSiloComponent`, outpost flatpack | T1 / B |
| Fabrication silo | Parts silo + chemical silo | As built | [EXISTS] WF `FabricationSilo` | T2 / C |
| Shelves, crates, lockers | Storage | n/a | [EXISTS] | T1 / A |
| Outpost deposit terminal | Access your safety deposit box from home | 500 W | [EXISTS] WF `SafetyDepositBox` | T3 / D |

### Life Support / Atmos

| Gizmo | What it does | Numbers | Source tag | Tier / band |
|---|---|---|---|---|
| CO2 cracker | Makes oxygen from CO2. The breathable-air answer on Fervidus and Thrascias | CO2 to O2 1:1, up to 10 mol per tick | [PORT: Nova] `co2_cracker.dm` | T1 / B |
| Temperature regulator | Limited thermomachine | 0 to 200 C, heat capacity 10000 (Nova same) | [EXISTS] stock thermomachine, limited like [PORT: Nova] `thermomachine.dm` | T1 / A |
| Wall heater | Cell-powered wall heater/cooler, 0 to 60 C target | Nova: 80 kJ per tick | [EXISTS] stock space heater, wall mount like [PORT: Nova] `space_heater.dm` | T1 / A |
| Portable pump / scrubber | As stock | As stock | [EXISTS] | T1 / A |
| Air alarm, firelock, fire alarm electronics | As stock | As stock | [EXISTS] | T1 / A |
| Water synthesizer | A tank that refills itself with water (SS14 has no plumbing, so it's a regenerating tank) | 5 u/s, 2.75 kW (Nova same) | [PORT: Nova] `chem_machines.dm` on [EXISTS] solution regeneration | T1 / A |

### Kitchen / Food

| Gizmo | What it does | Numbers | Source tag | Tier / band |
|---|---|---|---|---|
| Rations printer | Plants into biomass into seeds, eggs, meat product, flour, milk, rice, sugar, snacks. Seeds start a farm with no seed vendor | Seeds 25 biomass, eggs/butter 25, meat 50, sacks 100 | [PORT: Nova] `foodricator.dm` on [EXISTS] biogenerator | T1 / B |
| Sustenance dispenser | Water, powdered milk/coffee/tea/cocoa, sugar, salt, pepper, enzyme and friends | Own cell, 2 kW recharge | [PORT: Nova] `chem_machines.dm` on [EXISTS] reagent dispenser | T1 / A |
| Microwave | As stock, flatpacked | As stock | [EXISTS] | T1 / A |
| Tabletop griddle | Griddle that sits on a table | 1 kW | [PORT: Nova] `kitchen_appliances` | T1 / A |
| Frontier range | Oven + stove | 1.2 kW | [PORT: Nova] `kitchen_appliances` | T1 / B |
| Hydroponics tray / planet soil | Grow food | As stock | [EXISTS] trays, NF soil, Mono planet sand soil | T1 / A |
| Nutrient synthesizer | Regenerating tank of E-Z Nutrient, Robust Harvest, weed/pest killer | 5 u/s, 2.75 kW | [PORT: Nova] `chem_machines.dm` | T1 / A |
| Animal feeder | Trough that pens need (see Farming) | Holds 200 u of feed | [NEW] | T1 / A |

### Comms / Logistics

| Gizmo | What it does | Numbers | Source tag | Tier / band |
|---|---|---|---|---|
| Outpost telecom relay | Radio works on a planet map only if a powered telecom server with that channel is on the sender's map; this is that server | 2 kW, common channels | [EXISTS] `TelecomServer`, outpost flatpack | T1 / B |
| Outpost intercom | Handheld-exempt intercom | As stock | [EXISTS] intercoms (telecom exempt) | T1 / A |
| Planet broadcast console | Send a short message to everyone on this planet (trade offers, SOS) | 1 message per 5 min | [NEW] | T2 / B |

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
| Flak battery | Fires at hostile hulls on the air layers directly above; hits resolve as explosions on the target layer after a flight delay | 15 kW, 300 m radius, 3 s flight | [NEW] on [EXISTS] Mono artillery ammo and fire control | T3 / D |
| Orbital defense battery | Long-range strikes on hulls in orbit above the outpost; slow, heavy, needs a gunnery console | 60 kW, 1 km radius, 8 s flight, 20 s reload | [NEW] on [EXISTS] `FireControl` | T3 / E |
| Descent jammer | Hostile hulls can't use Enter Atmosphere within 500 m of the outpost while it runs | 40 kW | [NEW] | T3 / E |

Main use: shooting down raider shuttles on their way in (see Outpost Attacks). Whether players can use these against other players is an open question.

## Farming

Given planets have their own fauna and wildlife, you will be able to farm animals and such, using breeding and animal herding mechanics that let you capture these animals and use them to produce things. Requires an animal pen and such.

### What exists [EXISTS]

- Breeding: `AnimalHusbandrySystem` + `ReproductiveComponent` (tries every 45 to 60 s, partner within 3 tiles, stops at 6 animals nearby, 15% chance, 1.5 min gestation, infants). Eggs hatch with `TimedSpawner`.
- Products: `EggLayer` (eggs), `Udder` (milk, 25 u per 30 s on cows), `Wooly` (wool). Butchering gives meat.
- Livestock: chickens, ducks, cows, goats, pigs.
- Planet wildlife spawns from biome fauna sites and retires when nobody's around, unless it has been damaged, possessed or moved onto a hull, which protects it for good.
- HTN `FollowCompound`, WF ropes (a leash is close).
- Not there: taming, leashes, herding, pens, feeders.

### Capture [NEW]

- **Lasso**: throw at an animal within 6 tiles; it struggles for 3 s (DoAfter), then it's leashed with a WF rope. Big animals take two lassos.
- **Cage / animal crate**: stuff a small animal in a crate or pet carrier. [EXISTS]
- **Tranquiliser**: sleep chems knock it out for a safe carry. [EXISTS]
- Capturing marks the animal protected so the wildlife system never retires it. [EXISTS] flag, [NEW] call

### Taming and herding [NEW]

- Feed a leashed or penned animal its favourite food 3 times over 5 minutes: it becomes **livestock** (`WFLivestockComponent`: outpost, owner, name) and joins a passive faction so turrets ignore it.
- **Herding whistle**: Follow me / Stay / Go to pen for tamed animals within 8 tiles (HTN follow). [NEW] on [EXISTS] HTN
- Tamed animals left outside a pen at night can be taken by predators during Active windows.

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
| Chunk filter | Chunks outside the bounds never load. The chunk list is private upstream, so this is a marked hook in `BiomeSystem` (or a partial). Pre-generating the whole world was ruled out: 2 million+ tiles per world |
| Edge | A ring of impassable terrain just inside the bound (cliffs, deep water or storm wall, per world). Mobs and items that get past it are pushed back in; hulls on air layers are clamped by a soft wall |
| Orbit | Orbit entry stays as is; Enter Atmosphere and pad descents are refused unless the hull's XY is inside the bounds |
| Radar | `WFPlanetRadarSystem.Sample` returns empty outside the bounds |

### Story-gen

- At network build, after pending outpost footprints are reserved, each world rolls its POIs from a per-planet table (`wfPlanetPoiTable`, weighted by planet type). Default 8 to 14 POIs per world. [NEW]
- POI grids load as separate grids on the ground map (like `GroundGrid` does, with `ReserveTiles`), not as biome markers, so they can be claimed and saved. [EXISTS] precedent + [NEW] placer
- The NF POI system is sector-map only and hard-wired to the default map; we reuse its prototype fields and grid spawn, not its placer. [EXISTS] + [NEW]
- POI protos get their own base (not `BaseMobilePOI`, whose preset list misses Wolfgate's MonoXeno). Claimable POIs don't get `ProtectedGrid { KillHostileMobs }`, or raids could never reach them. [NEW]
- Grids live in `Resources/Maps/_WF/Outposts/POI`. Existing NF/Mono POI grids and NF dungeon configs (planet base, mineshaft, salvage outpost...) are the first source of rooms. [EXISTS] + [NEW]

### Beacons [NEW]

- Every POI has a beacon on the orbit layer at its XY (the `WFOrbitBeacon` pattern: warp point + IFF), shown on orbit radar.
- Beacons start as **"Unknown signal"**. The name and type reveal when any player gets within 50 m on the ground, or when someone scans it with a survey tool from orbit.
- **Hidden** POIs (shrines, some artifact sites) have no beacon; they're found by exploring, or from clue items found in ruins.

### POI taxonomy

| POI | What it offers | Claimable | Beacon | Placement |
|---|---|---|---|---|
| Mine | Rich ore veins, a broken drill, ore boxes, cave fauna | Yes (becomes a mining outpost) | Unknown signal | Rocky biomes, 150 m from other POIs |
| Abandoned outpost | A half-wrecked prefab base with a dead console, some machines, loot | Yes, after you repair the console | Unknown signal | Anywhere, 200 m from reserved outposts |
| Automated outpost | Turrets, drones, a fabricator or drill still running; guarded | Yes, after the defenses are down and the core is hacked | Named from the start ("Automated facility") | 300 m from crash sites |
| Tradepost | NPC traders (buy/sell, refuel, repairs), a public landing pad and UTH, safe zone | No (NPC-run) | Named, always visible | 1 to 2 per world, near the world centre |
| Derelict capital ship | Big salvage dungeon, hostiles, valuable parts | No (too big) | Unknown signal | Max 1 per world, 400 m from everything |
| Small derelict | Crashed shuttle, salvage; sometimes repairable enough to fly (a crash survivor's way out) | No, but it can be repaired and flown | Unknown signal | 150 m from other POIs |
| Farm | Livestock to capture, crops, a barn and pens | Yes | Unknown signal | Grassy biomes |
| Workshop | Machine frames, a fabricator with random recipe packs, tools | Yes | Unknown signal | Anywhere |
| Ruins | Dungeon rooms, loot, clue items pointing to hidden POIs | No | Unknown signal | Anywhere |
| Heavenly shrine | A one-time blessing (full heal, a mood boost, or a relic worth money), lore | No | Hidden | Remote, 300 m from outposts |
| Alien artifact site | Xenoarchaeology artifacts, strange fauna, anomalies | No | Hidden or unknown | Remote, max 2 per world |

Artifacts, dungeons and traders are all existing systems (xenoarchaeology, `DungeonSystem` with NF configs, WF `Traders`). [EXISTS]

### Claiming a POI [NEW]

1. Walk up to the POI's outpost console. It shows "Claim" if you don't own a live outpost this round.
2. Requirements: no hostile NPCs on the grid, and (for automated outposts) the core hacked. Abandoned consoles need repairing first.
3. Claiming is free. The POI becomes your outpost: ownership, access panel, save tab. It keeps `ForceAnchor`.
4. The appraisal at claim time is stored as the **claim baseline**, and selling only pays what's above it.
5. Once saved, it's yours like any other save (loading costs parts + 50%, as usual).

## Outpost Attacks

Outposts draw the attention of nearby settlers and fauna. These are NPCs that attack your base at night, or whenever the planet's "Active" state is. There is a gizmo (the Early Warning System) that gives you a few minutes' notice of an incoming attack. Attacks range from a rabid rabbit all the way to a raiding group of raiders (all NPC controlled). It is recommended that you equip your outpost with defence turrets.

Depending on the attack type, these nasties come from burrows, shuttles or vehicles.

### What exists

- Day/night per world: a local day is 60 real minutes; `WFPlanetEnvironmentComponent.IsNight` (before 06:00 or from 18:00) is published every second on every layer. Only client ambience reads it today. [EXISTS]
- Hostile humanoid NPCs with HTN combat, and pathing flags for prying airlocks and smashing walls, so raiders can breach. [EXISTS]
- NPC factions for raiders: PirateNF, StreetGangNF, MercenariesExpeditionNF and friends. [EXISTS]
- Mono AI shuttles for the shuttle arrival mode. [EXISTS]
- `TimedSpawner` for burrows (the xeno burrower marker is exactly this). [EXISTS]
- NPC AI sleeps more than 32 tiles from players, and mob cleanup deletes mobs far from players, so raids must spawn near someone. [EXISTS]
- Not there: a raid director, waves, the Active state, early warning. [NEW]

### The "Active" state [NEW]

- Each world has an Active window tied to its day/night clock. Default: Active = night (18:00 to 06:00, 30 real minutes per local day).
- Per-world overrides in the surface prototype: e.g. Fervidus (hot) is Active at midday, Carcinoma is always Active at a low level.
- Storms stack: a storm's main phase during an Active window bumps threat by one step.
- The time/weather console shows the countdown to Active.

### Raid director [NEW]

One per world, ticking every 30 s during Active windows:

- **Attention** per outpost = wealth (appraisal) + power draw (noise) + age in rounds + livestock count, minus recent raids.
- **Threat points** from attention; the director buys a raid from the ladder with them.
- **Gates**:
  - raids are on (`wf.outposts.raids`) and the world allows them;
  - someone is home: at least one owner or crew within 64 tiles (chunks and NPCs sleep otherwise);
  - the outpost is at least 20 minutes old this round (grace);
  - at most one raid per outpost per Active window, 2 concurrent raids per world, 24 raid mobs per world, 64 server-wide.
- **Early warning**: with an EWS the raid is announced 120 s (240 s upgraded) before arrival: PA code Red, console alert, a direction arrow. Without one, you get a 15 s "you hear something out there" popup.
- **Dawn**: when the Active window ends, surviving raiders retreat and despawn once out of sight.
- Raiders never target the outpost console (so a raid can't delete your outpost by accident).

### Escalation ladder

| Step | Threat | Example | Arrival |
|---|---|---|---|
| 0 | A rabid rabbit | 1 to 2 small wild animals gone aggressive | Walk in from the edge of view |
| 1 | Pack | 3 to 5 predators (world fauna) | Walk in |
| 2 | Burrowers | Burrow opens 20 to 40 tiles out, spits creatures for 30 s until destroyed | Burrow |
| 3 | Scavengers | 2 to 3 humanoids with melee and tools; pry doors | Walk in |
| 4 | Raider party | 4 to 6 armed humanoids, a breacher with explosives | Vehicles (ride in on ATVs, dismount) |
| 5 | Raider shuttle | Shuttle lands 30 m or more from the outpost (landing guard applies), 6 to 8 raiders | Shuttle |
| 6 | Siege | Mixed party + a shuttle + burrows, only for rich, old outposts | All |

Arrival modes in detail:

- **Walk in**: spawn 40 to 60 tiles from the outpost edge, at least 24 tiles from every player (the fauna spawn rule), inside the world bounds. [EXISTS] rules, [NEW] spawner
- **Burrow**: a `WFRaidBurrow` entity with a `TimedSpawner`; it keeps spawning until destroyed or the raid ends. [EXISTS] + [NEW]
- **Vehicles**: NPCs can't drive today. Phase one: raiders spawn mounted and dismount at 20 tiles (cosmetic). NPC driving is later work. [NEW]
- **Shuttle**: a raider shuttle drops from the orbit layer via `TryDropFromOrbit`, lands near the outpost, raiders disembark. Orbital defense can shoot it down on the way. [EXISTS] + [NEW]

Loot: raiders drop their gear, flagged `WFRaidLoot` (stripped from saves until picked up and used), so raids pay something without turning into a farm.

## Jetpacks in the Atmosphere

Atmospheric jetpacks can transit through the atmosphere levels; normal jetpacks are useless there. They use welding fuel. Some planets have super strong gravity, where they don't work.

- **Normal jetpacks** already refuse to work below orbit and cut out if carried down (`SharedJetpackSystem.WfInAtmosphere`, "cannot hold you up in atmosphere"). [EXISTS]
- **Atmospheric jetpack** [NEW]:
  - Exempt from that check through a new `WFAtmosphericJetpackComponent` (one marked line in `SharedJetpackSystem`).
  - Fuel: welding fuel tank, 100 u. Burn 1 u/s hovering, 2 u/s thrusting or climbing. Refill at any welding fuel tank.
  - Works on the ground and air layers, and can climb and descend between them (0.5 layers/s). It needs air to burn, so it doesn't work on the orbit layer.
  - Out of fuel mid-air: you fall. Wear a parachute (the parachute system already catches falls through a level).
- **Gravity gate** [NEW]: planet gravity is one number per world, used only for ship lift today. The jetpack reads `WFPlanetLayerComponent.Gravity` and refuses above 1.5 g ("too heavy here"). So Aerumna (3 g) is a no-go, Merak (1.15 g) and Thrascias (1.25 g) are fine but burn fuel 25% faster above 1 g.
- Price: band C, T2, on the trade panel and at tradeposts.

## Under the hood (for devs)

### Modules

Per AGENTS.md, one feature per module. `Spawning` is taken (admin spawn menu) and `Station` is the overflow tweak.

| Module | Holds |
|---|---|
| `Outposts` | Console, outpost grid, foundation tiles, save/load, pricing, kit, presets, gizmos, trade panel, UTH, landing pads, cryopods, Outpost Job |
| `OutpostRaids` | Active state, raid director, ladder, burrows, early warning |
| `SpawnOptions` | Spawn menu, play groups, crash start, round-start handler, joinable positions picker tab |
| `ShipAccess` | Access overhaul for ships and outposts, door rules, codes, access tab |
| `PlanetPois` | Bounds hooks it needs, POI tables, placer, beacons, claiming |
| `Husbandry` | Capture, taming, herding, pens, feeders, livestock records |
| `Planets` (existing) | World bounds, atmospheric jetpack exemption, landing guard hooks in flight code |

Paths: `Content.{Client,Server,Shared}/_WF/<Module>`, `Resources/Prototypes/_WF/<Module>`, `Resources/Locale/en-US/_WF/<Module>`, `Resources/Textures/_WF/<Module>`, `Resources/Audio/_WF/<Module>`, presets in `Resources/SharedMaps/_WF/Outposts`, POI grids in `Resources/Maps/_WF/Outposts/POI`, tests in `Content.IntegrationTests/Tests/_WF/<Module>`.

### Key new pieces

Components (shared unless noted):

- `WFOutpostComponent` (grid): owner user + profile id + character name, outpost name, founded round, save id, claim baseline, abandoned-at time.
- `WFOutpostConsoleComponent`, `WFOutpostFoundationComponent` (tile marker), `WFOutpostCryopodComponent`, `WFLandingPadComponent`, `WFPadDockedComponent`, `WFUniversalTradeHubComponent`.
- `WFRepackableComponent` (pack proto, state keys to carry), `WFWindTurbineComponent`, `WFEarlyWarningComponent`.
- `WFShipAccessComponent` (grid), `WFDoorAccessRuleComponent` (door), `WFDoorCodeKeypadComponent`.
- `WFCrashSequenceComponent` (grid, server), `WFAtmosphericJetpackComponent`.
- `WFLivestockComponent`, `WFAnimalFeederComponent`, `WFLassoComponent`.
- `WFPlanetBoundsComponent` (network), `WFPoiSiteComponent`, `WFPoiBeaconComponent`, `WFRaidBurrowComponent`, `WFRaidLootComponent`.

Server systems: `WFOutpostSystem`, `WFOutpostSaveSystem`, `WFOutpostLoadSystem` (clearance, overlay, swap, re-init), `WFOutpostPricingSystem`, `WFOutpostTradeSystem`, `WFLandingPadSystem`, `WFOutpostSpawnSystem`, `WFCrashStartSystem`, `WFShipAccessSystem`, `WFPlanetPoiSystem`, `WFPlanetBoundsSystem`, `WFRaidDirectorSystem`, `WFHusbandrySystem`. Play groups as an IoC manager (`WFPlayGroupManager`), lobby state has no entities.

Prototypes: `wfOutpostPreset`, `wfOutpostTradeListing` (or cargo products in an outpost group), `wfPlanetPoiTable`, `wfPoiType`, `wfRaidTier`, `wfRaidTable`, `wfCrashPool`, job `WFOutpostSettler` + role loadout `JobWFOutpostSettler` + `customJobTitle`, alert codes `WFAlertCrash`, `WFAlertCrashed`, `WFAlertRaid`.

### Reused systems (no rebuild)

`ShipyardSystem.TrySaveShip` / `TryAddSavedShip` / `TryAssignDeed`, `UsedShipReinitSystem.ReinitLoadedShip`, `PricingSystem.AppraiseGrid`, `BankSystem` (session withdraw, untaxed deposit), `ForceAnchorSystem` components, `ShipPaSystem` / `ShipAlertSystem`, `WFOrbitEntrySystem` (drop, enter atmosphere), `WFParachuteSystem`, `WFPlanetWeatherSystem`, `CryoSleepSystem`, `StationSpawningSystem.SpawnPlayerMob`, `GameTicker.MakeJoinGame`, `RulePlayerSpawningEvent`, `LatheComponent`, `OreSiloComponent`, `FlatpackComponent`, `AnimalHusbandrySystem`, `DeployableTurret`, WF `ShipPreview`, WF `Roles` custom titles, WF `Traders`.

### Upstream hooks (marked edits)

| File or system | Hook | Module |
|---|---|---|
| `FloorTileSystem` (already marked by Planets) | Foundation plates extend the outpost grid | Outposts |
| `BiomeSystem` | Chunk filter for world bounds | Planets |
| `SharedJetpackSystem` (already marked) | Atmospheric jetpack exemption | Planets |
| `ShipAccessReaderSystem.HasShipAccess` | Raise a WF access event before the deed check | ShipAccess |
| CE transit exit / touchdown (Planets partials) | Landing guard and pad touchdown skip the grid crush | Planets / Outposts |
| Lobby UI (`LobbyGui`) | Spawn Options button | SpawnOptions |
| `HumanoidProfileEditor` | Outposts tab | Outposts |
| Shuttle console window / `NavScreen` | Access tab, pad buttons | ShipAccess / Outposts |
| NF late-join picker | Outposts tab | SpawnOptions |
| `Model.cs`, `ServerDbBase`, `IServerDbManager` | Outpost save table and API | Outposts |

Everything else is new `_WF` systems subscribing to existing events, or partials.

### Save format and DB

Migration `WolfgateOutposts` (Postgres + SQLite), table `wf_outpost_save`:

| Column | Type | Notes |
|---|---|---|
| id | Guid | PK |
| profile_id | int | FK to `profile`, cascade delete |
| owner_user_id | Guid | For admin tools and audits |
| slot | int | 0 to 2 manual, 3 autosave; unique per profile |
| name | text(30) | |
| surface_id | text | Planet surface prototype id |
| anchor_x, anchor_y, anchor_rot | float | World position and rotation of the console |
| console_local_x, console_local_y, console_local_rot | float | Console position inside the grid |
| appraisal | int | Parts value at save time |
| tile_count, entity_count, bounds_w, bounds_h | int | Size cap checks and lobby display |
| livestock | text | JSON records |
| format_version | int | Bump on breaking changes |
| data | text | Compressed, base64 YAML, max 2 MB |
| created_at, updated_at | timestamp | |
| saved_round_id, last_loaded_round_id | int | "In use this round" and audits |

- On load of an old `format_version`, run a migration step, or refuse with a clear message. Prototype ID renames go through `Resources/migration.yml` as usual.
- Prices are stored at save time and shown before loading; the charge uses the stored number.

### Networking

- Lobby: `MsgWFOutpostSaveList` (metadata only) on connect and on change.
- Preview: request/response for one save's YAML, owner only, max one request per 5 s.
- Load overlay: networked footprint (tile list + origin + rotation + seconds left) to clients in range.
- Play groups: small lobby messages (browse, create, join, request, invite, result), modelled on the ERT prompt messages.
- Access: door rules and allow list are networked to the owner's console only; codes never leave the server except to the owner.

### Performance budget

| Thing | Budget |
|---|---|
| Live outposts per server | 20 target, 30 hard cap (cvar) |
| One outpost | 1,600 tiles, 4,000 saved entities, 2 MB compressed |
| Save | Serialise on the main thread (target under 50 ms), compress and write off-thread; autosaves staggered, max 1 per 5 s server-wide |
| Round-start loads | Queued, one grid every 2 ticks |
| Worlds | 6 bounded worlds, streaming unchanged inside bounds; POI grids idle without players |
| POIs | 8 to 14 per world, one grid each |
| Raid mobs | 24 per world, 64 server-wide; fauna caps unchanged (32 per world, 128 total) |
| Mining bots | 3 per outpost |

### Tests

Integration tests under `Content.IntegrationTests/Tests/_WF/<Module>`, never `Destructive`:

- Save/load round trip on a small test outpost (entities, silo contents, battery charge, door rules survive; strip list removed).
- Pricing: load price = appraisal x 1.5; claim baseline sale.
- Clearance: refuses overlap with a grid, a claim zone, out of bounds.
- Access: owner, allow list, code, per-door rules, faction mode.
- Spawn options: validation and fallback to Standard.
- Presets: one universal test over every preset grid (not one test per preset).

### Phase plan

| Phase | Scope | Depends on |
|---|---|---|
| F0 | Planets branch merged; outpost grid: console, foundation plates, anchoring, claim zone, cleanup and Carcinoma exemptions, landing guard | Planets |
| F1 | Save/load: DB, Save tab, autosave, clearance + overlay, pricing, previewer overload, editor tab | F0 |
| F2 | Outpost Kit, fabricator, repack, gizmo pack 1 (power, construction, atmos, kitchen), trade panel, UTH parachute drops | F1 |
| F3 | Ship Access Overhaul (ships and outposts) | F0 |
| F4 | Outpost Spawn: spawn menu, Outpost Job, cryopods, joinable positions, presets | F1, F3 |
| F5 | Play groups, Shuttle Crash start | F4 |
| F6 | Landing pads | F0 |
| F7 | Bounded worlds, story-gen POIs, beacons, claiming | F1 |
| F8 | Outpost attacks: Active state, raid director, EWS, turrets | F2 |
| F9 | Farming | F2 |
| F10 | Orbital defense, atmospheric jetpacks, mining bots, late gizmos | F6, F8 |

## Decisions

1. D1: An outpost is its own grid on the ground map; only the outpost grid is saved.
2. D2: The Outpost Console founds an outpost (3x3 foundation) and is what makes a grid an outpost.
3. D3: Outposts only on the ground layer of a planet.
4. D4: Outposts are anchored with `ForceAnchor` + `PreventGridAnchorChanges`; destroying the console unanchors.
5. D5: One live outpost per player (`NetUserId`) per round.
6. D6: Owners can name their outpost (30 characters).
7. D7: Save any time from the console; autosave every 10 minutes into its own slot, plus a final one at round end.
8. D8: 3 manual saves + 1 autosave per character; deletable in the save menu.
9. D9: Saves are per character and can't be transferred; live outposts can be sold.
10. D10: Saves are viewable, with a preview, in the console and the character editor.
11. D11: Saves live in a dedicated DB table keyed to the DB profile id, not in Mono persistent profile data.
12. D12: Load price = stored appraisal x 1.5 (sum of parts + 50%), charged when you press Load, refunded if the load fails or is cancelled.
13. D13: Loading needs a freshly placed console; the save's console lands on that tile and facing.
14. D14: Load clearance refuses other grids' tiles, lava, deep water, out of bounds, claim zones and POI exclusion zones.
15. D15: 30 second ghost overlay before the grid appears; mobs and items are pushed out, never gibbed.
16. D16: Saves strip players, mobs, ghost roles, cash, deeds, trade crates and unclaimed raid loot.
17. D17: Penned livestock is saved as records and respawned at feeders.
18. D18: A save that is live this round can't be loaded again this round.
19. D19: Preset outposts exist, priced at a fixed price >= appraisal x 1.5, loaded through the same pipeline.
20. D20: The Planet Outpost Kit costs 25,000 and includes a generator; a bare console costs 6,000.
21. D21: Spawn options are Standard, Shuttle Crash and Outpost Spawn; the choice is round state, not a profile field.
22. D22: Non-standard spawns are handled in `RulePlayerSpawningEvent`; failures fall back to Standard with a refund.
23. D23: Play groups are Open, Join Code or Private (requests + invites), in memory, cleared each round.
24. D24: Group members get door access (allow list), never console access.
25. D25: Crash groups can only be joined in the lobby before round start; outpost positions can be joined mid-round.
26. D26: Outpost Job = `WFOutpostSettler` job + role loadout + custom title, no DB migration.
27. D27: Spawning at an outpost needs at least one Outpost Cryopod; pods cap group size and positions.
28. D28: Outpost Spawn uses the save's own position, with footprints reserved before POIs roll; blocked late loads search 300 m.
29. D29: Crash: random sanctioned world (Carcinoma opt-in, 3 g worlds excluded), random small shuttle, random crash job, orbit layer spawn.
30. D30: Crash countdown is 3 minutes on the PA with a locked console; impact severity is clamped to hard landing or a 2-piece break.
31. D31: Spawn-distance rules as in the table (crash 300 m from outposts, 250 m from POIs).
32. D32: Non-faction ships and outposts default to owner-only access; faction ships keep faction access and can't drop it.
33. D33: Access is matched by person (mind + character), not card; the owner's deed still works.
34. D34: Door rules: Default, Owner only, Players, Code, Players or code, Public, Sealed.
35. D35: 4-digit codes via right click > Enter Code; 3 misses in 60 s lock the keypad for 30 s.
36. D36: Outpost Consoles are owner-only.
37. D37: Outposts sell to the game for the exact current appraisal, mid-round or at round end, with shuttle-style sale checks.
38. D38: Claimed POIs sell for appraisal minus the claim baseline.
39. D39: A destroyed console leaves the outpost Abandoned; the owner has 10 minutes to re-found it before anyone can.
40. D40: Landing on or over an outpost claim zone is refused; falling hulls are shifted off it.
41. D41: Landing pads waive lift; capture within 32 m above the pad; scripted descent and launch; lift-less hulls can only leave by pad.
42. D42: The trade panel is buy-only and needs a UTH open to the sky.
43. D43: Deliveries parachute from the top air layer onto the UTH; 5 in flight, 60 s ETA.
44. D44: Gizmos follow the Nova pattern: fixed stats, flatpacked, 3 s repack that keeps or refuses state.
45. D45: Wind turbines only get the storm bonus in windy storms (not any weather, unlike Nova).
46. D46: Nova numbers are scaled to Wolfgate's generator values, keeping Nova's ratios.
47. D47: Outpost turrets use access exemption, not faction, for friend/foe.
48. D48: Farming reuses breeding/eggs/milk/wool; capture, taming, herding, pens and feeders are new.
49. D49: Worlds become bounded (default 1536 m square) with a chunk filter and an edge ring, not pre-generation.
50. D50: POIs are rerolled each round on fixed-seed terrain, 8 to 14 per world, as separate grids.
51. D51: POI beacons start as "Unknown signal"; shrines and some artifact sites are hidden.
52. D52: Tradeposts, derelict capital ships, ruins, shrines and artifact sites are not claimable.
53. D53: Raids only happen during the Active window (default night), only when someone is home, after a 20 minute grace.
54. D54: Raids escalate from a rabid rabbit to a siege, arriving on foot, from burrows, on vehicles or by shuttle.
55. D55: Early Warning gives 120 s (240 s upgraded); without it, 15 s.
56. D56: Raiders never target the outpost console; survivors leave at dawn.
57. D57: Atmospheric jetpacks run on welding fuel, work on ground and air layers but not orbit, and refuse above 1.5 g.
58. D58: The work is split into the modules Outposts, OutpostRaids, SpawnOptions, ShipAccess, PlanetPois, Husbandry, plus Planets.
59. D59: Prices bands A to E as defined at the top; all numbers are tuning defaults.
60. D60: Only Nova art/sound under CC-BY-SA gets ported; Pixabay-licensed sounds get replaced.

## Open questions

1. Q1: PvP: can players raid, bombard or claim other players' outposts (orbital defense, destroying consoles, the Abandoned rule), or are outposts PvE only?
2. Q2: Selling to the game at "exact calculated value": should the shipyard's sector taxes apply, or is it truly untaxed?
3. Q3: Can the Shuttle Crash start be picked mid-round, or only at round start as defaulted here?
4. Q4: Should the outpost owner be able to delegate console access (a manager role), or stay strictly owner-only?
5. Q5: Should outposts on Carcinoma be exempt from infestation (default here) or be infested as a feature?
6. Q6: Kit price (25,000), preset prices (40,000 to 110,000) and the gizmo bands: what does the economy team want?
7. Q7: Do crash survivors get a reward for escaping the planet, and do they keep their normal bank balance?
8. Q8: Should raids happen while the owner is offline but crew are home, or only when the owner is home?
9. Q9: Is a save kept if its planet is removed from the game, and do saves survive a character wipe or economy reset?
10. Q10: A loaded save skips map-init handlers. Does a grid loaded onto a live planet map need more than `ReinitLoadedShip` (atmosphere, CE z-level registration, planet components)? Not tested.
11. Q11: Which lookup flags does `Smimsh`'s uncontained lookup cover, i.e. are anchored entities under a landing hull always deleted? Inferred from code only.
12. Q12: What happens to loose items on natural ground when their chunk unloads and the tile is emptied? Matters for stuff left outside an outpost.
13. Q13: Does the merged Monolith persistence port (#80) touch planet maps or grids in a way that affects outpost saving?
14. Q14: The PlanetCracker code is staged and uncommitted on the planets branch; which hooks actually ship?
15. Q15: What does the CE z-level system do to a mob or item that walks off the edge of depth-0 ground onto empty tiles? Needed for the world-edge design.
16. Q16: Can `IServerPreferencesManager.SetProfile` safely save a changed loadout mid-round (editing the Outpost Job from the console)?
17. Q17: Can `DisallowLateJoin` be true during `RulePlayerSpawningEvent`, turning a `MakeJoinGame` call there into an observer spawn?
18. Q18: What access checks does the station records console apply to adjusting job slots, and do outposts need a different gate?
19. Q19: How does `ShuttleSystem.Impact` behave for a grid falling from the orbit layer, and does it interact with the crash clamp?
20. Q20: Do ship door prototypes carry their own access tags, which would change how the default "owner only" rule behaves on existing ships?
21. Q21: Is `ShipOwnershipComponent.DeletionTimeoutSeconds` enforced anywhere (a hidden cleanup that could delete an outpost)?
22. Q22: Do NPC pathfinding and HTN combat work on a planet map grid with unloaded chunks around the outpost? Raids depend on it.
23. Q23: How do Mono AI shuttle events move from their own map into play? The raider shuttle arrival wants the same path onto a planet.
24. Q24: Which existing POI grids already contain consoles, cryopods or turrets suitable for claimable planet POIs?
25. Q25: Nova assets: license of `AW_reactor.ogg` (freesound dobroide) and the Pixabay terms for the conditioner, welder and door sounds; and whether the DMI art fits SS14 once converted.
26. Q26: Real power draw of the regulator, recycler, water synth and dispenser in use, and a full comparison against SS14 power values, before numbers are locked.
27. Q27: Which planet weather types count as "windy" for the turbine storm bonus?
28. Q28: Does the Outpost Fabricator accept raw ore at 1:1 like Nova's probably does, or only sheets (which keeps the arc furnace worth building)?
29. Q29: Do Crescent shield bubbles and Mono artillery behave on planet maps and CE layers?
30. Q30: Should the livestock record approach be extended to pets and tamed wildlife that are not in pens?
