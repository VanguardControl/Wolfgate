# Sector control

Faction territory on the sector map: a hex grid over the sector, cells held by factions, drawn on the shuttle
console's sector map and the nav radar, with a claim API that drivers use. The first driver is the Ashfall Reaver
campaign (`Docs/_WF/ReaverCampaign/design.md`); ADS incursions and TSF/PDV sector control come later as drivers of
their own. This module knows nothing about strongholds, threat tiers or pirates.

## Model

**Hex grid.** Flat-top hexes on axial coordinates `(q, r)`, centred on the sector origin (0, 0), circumradius from
`wf.sector.cell_size` (3000 m; the sector's stations lie from the central cluster at 0 m out to about 27 km, which is
97 cells). Cells exist implicitly; only claimed, contested or recently active cells are stored. Every cell has a
callsign for announcements and the map (`WFSectorHex.Callsign(cell)`: a letter run for `q`, a number for `r`, such as
`K-7`), and a world position for its centre and corners.

**Faction.** A `wfSectorFaction` prototype: name (LocId), colour, the company it fights as (for standing and for who
is "theirs"), and `blockades` (whether a station in a cell it holds is cut off from NPC freight; the Encounters
scheduler asks through `WFEncounterStationsEvent`).

**Claim.** `{ map, cell, faction, sources }`. A source is an entity (a stronghold encounter, later a station or a
flagship) that supports the claim; a cell stays held while any of its sources lives. `TryClaim(map, cell, faction,
source)` raises a cancellable `WFSectorClaimAttemptEvent` first, so protection rules veto once for every driver;
`Release(source)` frees every cell that source alone supported; `Free(map, cell)` frees one. A claim on a cell with
another owner fails (drivers contest explicitly).

**Protected cells.** `WFSectorProtectionSystem` vetoes claims on the origin cell and its ring, and on any cell that
holds a station whose name matches `wf.sector.protected_stations` (default `Halcyon`: the TSF flagship). Hidden
stations (`wf.encounters.hidden_stations`, PDV Helios) are not protected: an unclaimable hole in held space would give
them away. Drivers keep spawns and routes out of them with the Encounters placement helper, which already knows the
hidden clearance.

**Activity.** `WFSectorActivitySystem` scans every 60 s. A grid counts when it is on the sector map, has a living,
non-ghost player aboard, is not an encounter grid, is not a station (a `StationMember` grid with no `ShuttleDeed`) and
has moved at least 100 m since the last scan: parked, docked and AFK ships hold nothing. A ship that jumped (its cell
changed by more than one ring) marks the cell it left as well as the one it arrived in; a ship in FTL keeps its track
for three minutes, so a jump that a scan lands in the middle of still counts. `LastActivity(map, cell)`
answers when a cell was last active; `IsQuiet(map, cell, since)` is the driver's question.

**Contested cells.** A driver may `Contest(map, cell, faction, deadline)` instead of claiming at once. The cell is
shown as contested on the map with a countdown, `WFSectorCellContestedEvent` fires (drivers announce it), and at the
deadline the territory system claims it for the faction if the cell has been quiet since the contest began, or drops
the contest. For a station cell this is the blockade warning: traffic can hold the lane by flying it.

**Notices.** `WFSectorNoticeSystem` tells those aboard a player ship when it has been inside held space for 60 s and
when it has been outside for 60 s, through the ship's PA (`ShipPaSystem.Announce`), or in chat to those aboard where
the ship has no working speaker, with a 5 min per-ship cooldown. Sector control is the only framework code that
touches ShipPa.

**Sync.** `WFSectorSyncSystem` sends `WFSectorTerritoryEvent` to everyone when territory changes and to a player when
they attach: the map, the cell size, the faction ids, the claims as `(q, r, faction index)`, the contested cells with
their deadlines. One empty update after the last claim goes, then silence. Drivers send `WFSectorStatusEvent`: per
faction, a legend line already localised on the server (a driver's tier and stronghold count are its own business;
the framework only shows the line).

**Drawing.** `ShuttleMapControl.SectorControl.cs` draws, right after the star system and before FTL ranges and map
objects: each held cell as a filled hex in its faction's colour at alpha 0.10 (one `DrawPrimitives` TriangleList per
faction), every edge whose neighbour has another owner as a border line at alpha 0.8, contested cells hatched or
outlined with their countdown, the cell callsign in held cells when zoomed in, and a legend at the top-left corner
(the cursor readout owns the bottom-left) with one line per faction from the status event. Edge segments are built
once when an event arrives. `ShuttleNavControl.SectorControl.cs` draws the border edges within the scope beneath grids
and contacts, so a pilot sees the line they are about to cross. Both hooks are one marked line in the upstream
control, like the Planets and Encounters hooks.

**Admin.** `wf_sector status | claim <faction> <q> <r> | free <q> <r> | contest <faction> <q> <r> <minutes> | clear
[faction] | callsign <x> <y>` (`LocalizedEntityCommands`, `AdminFlags.Admin`).

**State and lifecycle.** Claims, contests and activity live on `WFSectorTerritoryComponent` on the sector map entity,
so VV shows them, and are reset on `RoundRestartCleanupEvent`. Nothing runs unless `wf.sector.enabled` (default true)
and the round is in progress.

## Public API

`WFSectorTerritorySystem`: `CellOf(MapCoordinates)`, `Centre(map, cell)`, `Owner(map, cell)`, `TryClaim`, `Release`,
`Free`, `Contest`, `Held(faction)`, `Frontier(faction)` (unowned cells adjacent to held ones), `Sources(map, cell)`,
`StationsIn(map, cell)`. `WFSectorActivitySystem`: `LastActivity`, `IsQuiet`, `ActiveShips(map, cell)`. Local events
in `WFSectorEvents.cs`: `WFSectorClaimAttemptEvent` (cancellable, by ref), `WFSectorCellChangedEvent`,
`WFSectorCellContestedEvent`, `WFSectorContestResolvedEvent`, and `WFSectorResetEvent`, raised when territory comes back on
after being switched off or resized, so drivers can claim what they still hold again.

## Module layout

- `Content.Shared/_WF/SectorControl`: `WFSectorHex.cs`, `WFSectorCell.cs` (the `(q, r)` struct), `WFSectorFactionPrototype.cs`,
  `WFSectorNetEvents.cs`, `SectorControlCVars.cs`.
- `Content.Server/_WF/SectorControl`: `README.md`, `WFSectorEvents.cs`, `Components/WFSectorTerritoryComponent.cs`,
  `Systems/WFSectorTerritorySystem.cs`, `Systems/WFSectorActivitySystem.cs`, `Systems/WFSectorProtectionSystem.cs`,
  `Systems/WFSectorSyncSystem.cs`, `Systems/WFSectorNoticeSystem.cs`, `Commands/WFSectorCommand.cs`.
- `Content.Client/_WF/SectorControl`: `WFSectorClientSystem.cs`, `ShuttleMapControl.SectorControl.cs`,
  `ShuttleNavControl.SectorControl.cs`.
- `Resources/Locale/en-US/_WF/SectorControl/sectorcontrol.ftl`. The module ships no faction; each driver defines its
  own `wfSectorFaction` (the Reavers' is in `Resources/Prototypes/_WF/ReaverCampaign/factions.yml`), and the tests
  declare theirs.
- Tests: `Content.Tests/_WF/SectorControl/WFSectorHexTest.cs`; `Content.IntegrationTests/Tests/_WF/SectorControl/`
  (claims and sources, protection veto, activity with the station and moved rules, contest deadline, sync event).

Upstream edits, each one marked line: `Content.Client/Shuttles/UI/ShuttleMapControl.xaml.cs` after
`DrawStarSystem(handle, matty);` and `Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs` after the `DrawWfTerrain`
line. Edits inside `_WF/Encounters` (same fork, no markers): `WFEncounterStationsEvent` raised in the scheduler's
`Stations()` so blockaded stations drop out of freight circuits.

## Decisions

- Hexes, not circles of influence: union rendering is "draw the edges whose neighbour differs", frontier and support
  are ring counts, and a later owner needs no new drawing.
- Claims carry sources, not ranges: the framework only checks liveness; what supports what is the driver's rule.
- Protection is an event, so every driver gets the same vetoes without a list of its own.
- Moving ships only count as activity. Parked pickets would otherwise fence the sector for free.
- Only the framework draws and networks; drivers send text for the legend.
