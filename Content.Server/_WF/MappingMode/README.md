# MappingMode

A server-wide mode that turns the server into a shared mapping workshop. Rounds never start, the lobby becomes a
mapping menu, and every player gets the full MappingTools toolset on their own map without admin rights. Maps save into
a per-player library on the server, with versions and autosave, and can be downloaded to the player's computer.

A host turns it on with `wf.mapping_mode.enabled true` (replicated, off by default). It forces `game.lobbyenabled`
on, `events.enabled`, `vote.autovote_enabled`, `vote.map_enabled` and `vote.preset_enabled` off and
`shuttle.auto_call_time` to 0, holds the ticker in the lobby and blocks late join. The other cvars are server only
unless noted, all under `wf.mapping_mode.`:

| Cvar | Default | What it limits |
|---|---|---|
| `map_size` (replicated) | 256 | Side of the square, in tiles and centred on the origin, that tiles must lie in |
| `autosave_interval` | 120 | Seconds between autosaves of a dirty session, until the owner picks a step |
| `autosave_min_interval` / `autosave_max_interval` | 60 / 900 | Range the owner's autosave step is clamped to, in seconds |
| `max_versions` | 25 | Versions kept per library map |
| `max_maps` | 50 | Maps per player library |
| `max_library_bytes` | 256 MiB | Size of a player library |
| `max_sessions` | 32 | Live sessions on the server |
| `max_sessions_per_player` | 1 | Live sessions one player may own, empty ones in their grace included |
| `max_members` | 8 | Members per session, absent ones included |
| `max_levels` | 3 | Z-levels per session |
| `empty_session_grace` | 300 | Seconds a session whose members are all away survives; one with no members closes at once |
| `grids_per_player` / `max_grids_per_session` | 2 / 8 | Grids each member adds to the session cap, and the cap itself |
| `max_entities_per_tile` / `max_decals_per_tile` | 30 / 16 | Entities and decals on one tile |
| `max_entities` | 25000 | Entities across all of a session's maps |
| `max_reference_bytes` | 1.5 MiB | Size of a file loaded as a reference grid; admits every ship, refuses stations |
| `reference_cooldown` | 2 | Seconds after any reference load before anyone on the server may load another |
| `max_edits_per_second` | 60 | Placement, tile, erase and decal requests per second per mapper |
| `max_selection_cells` | 4096 | Cells one tools selection or area erase may cover (a whole-grid delete is checked by entities) |
| `max_edit_entities` | 2000 | Entities, contents included, one copy, paste, delete, area erase or undo step may touch |
| `max_edit_decals` | 4096 | Decals one copy or paste may carry |
| `max_history` / `max_history_bytes` | 30 / 16 MiB | Undo steps per mapper, and their estimated saved size; the oldest go first |

Players see a mapping panel in the lobby instead of the ready button. New map takes a name and makes a session with a
fresh map and a six character join code. Join takes a code. Your sessions lists the ones you can rejoin after a
reconnect, and My maps lists the library: Open starts a new session from a version, Download copies it to the
client, Delete asks for a second click. In game, the Mapping entry of the escape menu opens the Mapping window. Its
Session tab shows the name, join code (Copy, and New code for the owner, which revokes the old one), the live limits,
members (the owner can remove them for good and set the autosave step), Delete the grid under me, Load reference and
Leave. Save saves into your own library under a name
(blank uses the session name); Library lists, downloads and deletes. Downloads land in the client's
`Mapping/<map>/<version>/` data folder, with an Open folder button.

Reference grids are game ships (`/SharedMaps`), game maps (`/Maps`) or a level of your own library, loaded onto your
session's map to copy from. Pick one, move the outline, R turns it, click puts it down, Escape or right click drops
it. They count against the grid, entity and area limits like anything else.

Every limit is enforced on the server. A refusal reaches the player as a cursor popup in game and a status line in the
Mapping window or lobby, naming the limit. Placement past a limit is reverted or dropped, a paste or reference load
that would cross one is refused whole, and requests over their rate are dropped. A save past `max_maps`, or into a
library over `max_library_bytes`, is refused until something is deleted.

Admins keep everything they have. An admin with the Mapping flag may edit any map and is never limited. Admins see an
Observe button in the lobby, which puts them on a private admin map (Return to lobby in the Mapping window brings
them back); from there `tpto <name>` reaches any mapper. Console commands, all `AdminFlags.Mapping` and admin logged:
`wf_mappingmode_list` (sessions with code, owner, members, maps, unsaved state, last save), `wf_mappingmode_tp <code>`,
`wf_mappingmode_close <code>` (autosaves, then closes), `wf_mappingmode_kick <player>` and `wf_mappingmode_save <code>`.
Turning the cvar off closes every session after an autosave and sends everyone back to the lobby.

Entry points: `WFMappingModeSystem` (server; sessions, registry, guard, limits, reference loads, autosave and admin
parts), `WFMappingLibrarySystem` (library, versions, open, delete), `WFMappingDownloadSystem` (chunked downloads),
`WFMappingModeCommands` (console), `WFMappingObserverSystem` (shared; what an observer can't do),
`WFMappingClientSystem` (client state and setup), `WFMappingLobbyUIController` and `WFMappingLobbyPanel` (lobby),
`WFMappingMenuUIController` and `WFMappingMenuWindow` (Mapping window), `WFMappingReferenceSystem` (reference
placement), `WFMappingBoundsOverlay` (map limit border). Cvars are in `WFMappingModeCVars`. The `WFMappingObserver`
prototype is in `Resources/Prototypes/_WF/MappingMode`.

## Design notes

- Sessions and registry: a session (`WFMappingSession`) has an id, a code from `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`, an
  owner, members (one observer each; absent members keep their place) and its levels. A player is in one session at a
  time; create, join and open end the old membership first. The owner passes to the longest-present member when they
  leave. A session whose last member leaves or is kicked closes at once, autosaving if dirty; one whose members are
  all away closes after `empty_session_grace`. A kicked player can't join that session again, and the owner can
  replace the join code. The server makes every map a
  session uses and records map to session in a registry (`WFMappingSessionMapComponent` mirrors it); a request is
  checked against the sender's session, never against a map the client names. Z-levels are a CE z-network private to
  the session and are registered when added.
- Permissions: `CanMap(session)` is true for a present member or a Mapping admin, and `CanEdit(session, map)` for a
  level of the player's session or any map for a Mapping admin. MappingTools asks through `WFMappingCanUseEvent` and
  `WFMappingCanEditEvent` (`WFMappingPermissionEvents.cs`), which nothing cancels outside the mode. Engine placement
  goes through the placement check in `SandboxSystem`, which calls `WFMappingModeSystem.AllowPlacement` for
  non-admins. Decal requests have their own handlers, ordered between MappingTools and the decal system. The client
  learns it is mapping from `WFMappingObserverComponent` on its entity, never from admin data.
- Guard: the placement gate refuses off-session and over-limit requests before the engine acts. A backstop on
  `PlacementEntityEvent` and `PlacementTileEvent` deletes an entity or reverts a tile that still landed off the
  session's maps, and deletes a grid made past the cap. Erasures can't be reverted there: the engine deletes the
  entity on the line after the event, so the gate is the only place to refuse them. Maps, actors, observers and
  anything a player is parented under are never erasable.
- Limits: the entity total is recounted at most once a second and grows by one per allowed placement; tool edits and
  reference loads recount. Per-tile counts take every entity whose world position falls in the cell, whatever its
  parent (container contents aren't counted), and apply to placements, pastes, moves and mirrors; mappers can't place
  entities off a grid. Pastes check decals per cell the same way. Undo and redo are checked against the entity and
  grid caps like a paste. Area is checked on the tile under a placement, or the AABB of a tool edit. Reference picks
  are refused by file size before loading, checked against the caps (counting other members' pending references)
  straight after, and one loads at a time per `reference_cooldown`. Throttles per player: edits as above, tool
  requests 10/s, create, join and open 1 per 3 s, manual save 1 per 10 s, kick 1/s, reference list 2/s, pick 1 per 5 s.
- Library on disk: `/Mapping/Library/<NetUserId>/<MapName>/v<NNNN>_<yyyyMMdd-HHmmss>_<manual|auto>/z<D>.yml` in the
  server data folder, one file per level with 0 the ground. There is no index; listings are read from folder names and
  cached for 30 s. Map names are 1 to 48 letters, digits, spaces, dashes or underscores, and not a Windows device
  name. Pruning keeps `max_versions`, removes `auto` versions before `manual` ones and never the newest. Only the
  owner's manual save or an autosave clears the dirty flag, as autosaves go to the owner's library. Autosave steps
  are Off, 1, 2, 5, 10 and 15 minutes, clamped to the cvar range; at most one session is saved per tick, and the
  first check is offset at random so sessions don't line up. A round restart saves every dirty session at once.
- Download: the server sends each level as its own transfer in chunks of up to 256 KiB, two in flight, each acked in
  order; 32 MiB per file, one download at a time per player, dropped after 30 s without an ack. The client assembles
  with `WFMappingDownloadAssembler` (pure and shared, so a unit test can cover it), checks the map name, version
  folder and `z<D>.yml` name before writing, and deletes what it wrote if a chunk is damaged or the server cancels.
- Observer: `WFMappingObserver` doesn't inherit `AdminObserver` or `MobObserverBase`, which bring hands, pulling,
  stripping, consoles, access, global radio and unrestricted examine. It is `Incorporeal` and `BaseMob` with the
  `aghost` input context. `WFMappingObserverSystem` cancels interaction, use, pickup, drop, emote, speak, throw,
  attack and pointing, and the marked `SharedVerbSystem` edit gives it no verbs unless its player has admin data. It isn't a ghost, so it has no warp
  list or dead chat, and `GhostAttemptHandleEvent` is handled so a mapper can't become one.
- Admin only: the `mapping` command, the Maps window of MappingTools (its client button needs `AdminFlags.Mapping`),
  anyone else's library, `tp`, `loadgrid` and the rest of the admin tools. Nothing a mapper sends is run as a console
  command.
- Nothing but mapping: the lobby hides ready, character, round setup, vote and observe (observe stays for admins),
  the game menu hides Character, Round setup and Sector map, and the round setup handler drops its requests. Nothing
  loads a station, sector, planet or vessel, because no round starts.
- Known gaps: Delete the grid under me goes through a MappingTools delete over the grid's tile box and refuses grids
  over 256 x 256 cells. Deleting the grid a mapper stands on probably deletes the mapper, who is a child of that grid,
  and leaves them with no entity; not exercised. The client can't tell the admin map from a session, so every admin
  sees the Mapping button. The decal window stays open when a mapper leaves. A planet file loaded as a reference makes
  the engine log an error and loads without its planet grid. Opening a large library map loads synchronously, with no
  size cap beyond `max_library_bytes`. A decal removal dropped by the edit throttle is still recorded in MappingTools
  history. The forced cvars aren't restored when the mode is turned off. Save, autosave, open, delete and download
  were built and sandbox-checked but not run against a live server and client.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/MappingMode/Commands/WFMappingModeCommands.cs`](Commands/WFMappingModeCommands.cs)
- [`Content.Server/_WF/MappingMode/GameTicker.MappingMode.cs`](GameTicker.MappingMode.cs)
- [`Content.Server/_WF/MappingMode/WFMappingDownloadSystem.cs`](WFMappingDownloadSystem.cs)
- [`Content.Server/_WF/MappingMode/WFMappingLibrarySystem.cs`](WFMappingLibrarySystem.cs)
- [`Content.Server/_WF/MappingMode/WFMappingModeSystem.Admin.cs`](WFMappingModeSystem.Admin.cs)
- [`Content.Server/_WF/MappingMode/WFMappingModeSystem.Autosave.cs`](WFMappingModeSystem.Autosave.cs)
- [`Content.Server/_WF/MappingMode/WFMappingModeSystem.cs`](WFMappingModeSystem.cs)
- [`Content.Server/_WF/MappingMode/WFMappingModeSystem.Guard.cs`](WFMappingModeSystem.Guard.cs)
- [`Content.Server/_WF/MappingMode/WFMappingModeSystem.Limits.cs`](WFMappingModeSystem.Limits.cs)
- [`Content.Server/_WF/MappingMode/WFMappingModeSystem.Reference.cs`](WFMappingModeSystem.Reference.cs)
- [`Content.Server/_WF/MappingMode/WFMappingModeSystem.Registry.cs`](WFMappingModeSystem.Registry.cs)
- [`Content.Server/_WF/MappingMode/WFMappingModeSystem.Riders.cs`](WFMappingModeSystem.Riders.cs)
- [`Content.Server/_WF/MappingMode/WFMappingModeSystem.Sessions.cs`](WFMappingModeSystem.Sessions.cs)
- [`Content.Server/_WF/MappingMode/WFMappingSession.cs`](WFMappingSession.cs)
- [`Content.Server/_WF/MappingMode/WFMappingSessionMapComponent.cs`](WFMappingSessionMapComponent.cs)

### Shared

- [`Content.Shared/_WF/MappingMode/WFMappingAdminEvents.cs`](../../../Content.Shared/_WF/MappingMode/WFMappingAdminEvents.cs)
- [`Content.Shared/_WF/MappingMode/WFMappingDownloadAssembler.cs`](../../../Content.Shared/_WF/MappingMode/WFMappingDownloadAssembler.cs)
- [`Content.Shared/_WF/MappingMode/WFMappingFootprint.cs`](../../../Content.Shared/_WF/MappingMode/WFMappingFootprint.cs)
- [`Content.Shared/_WF/MappingMode/WFMappingLibraryEvents.cs`](../../../Content.Shared/_WF/MappingMode/WFMappingLibraryEvents.cs)
- [`Content.Shared/_WF/MappingMode/WFMappingLibraryNames.cs`](../../../Content.Shared/_WF/MappingMode/WFMappingLibraryNames.cs)
- [`Content.Shared/_WF/MappingMode/WFMappingModeCVars.cs`](../../../Content.Shared/_WF/MappingMode/WFMappingModeCVars.cs)
- [`Content.Shared/_WF/MappingMode/WFMappingModeEvents.cs`](../../../Content.Shared/_WF/MappingMode/WFMappingModeEvents.cs)
- [`Content.Shared/_WF/MappingMode/WFMappingObserverComponent.cs`](../../../Content.Shared/_WF/MappingMode/WFMappingObserverComponent.cs)
- [`Content.Shared/_WF/MappingMode/WFMappingObserverSystem.cs`](../../../Content.Shared/_WF/MappingMode/WFMappingObserverSystem.cs)
- [`Content.Shared/_WF/MappingMode/WFMappingPrune.cs`](../../../Content.Shared/_WF/MappingMode/WFMappingPrune.cs)
- [`Content.Shared/_WF/MappingMode/WFMappingReferenceEvents.cs`](../../../Content.Shared/_WF/MappingMode/WFMappingReferenceEvents.cs)
- [`Content.Shared/_WF/MappingMode/WFMappingSessionCodes.cs`](../../../Content.Shared/_WF/MappingMode/WFMappingSessionCodes.cs)
- [`Content.Shared/_WF/MappingMode/WFMappingThrottle.cs`](../../../Content.Shared/_WF/MappingMode/WFMappingThrottle.cs)

### Client

- [`Content.Client/_WF/MappingMode/LobbyGui.MappingMode.cs`](../../../Content.Client/_WF/MappingMode/LobbyGui.MappingMode.cs)
- [`Content.Client/_WF/MappingMode/WFMappingBoundsOverlay.cs`](../../../Content.Client/_WF/MappingMode/WFMappingBoundsOverlay.cs)
- [`Content.Client/_WF/MappingMode/WFMappingClientSystem.cs`](../../../Content.Client/_WF/MappingMode/WFMappingClientSystem.cs)
- [`Content.Client/_WF/MappingMode/WFMappingClientSystem.Download.cs`](../../../Content.Client/_WF/MappingMode/WFMappingClientSystem.Download.cs)
- [`Content.Client/_WF/MappingMode/WFMappingDownloadRow.cs`](../../../Content.Client/_WF/MappingMode/WFMappingDownloadRow.cs)
- [`Content.Client/_WF/MappingMode/WFMappingLibraryList.cs`](../../../Content.Client/_WF/MappingMode/WFMappingLibraryList.cs)
- [`Content.Client/_WF/MappingMode/WFMappingLobbyPanel.xaml`](../../../Content.Client/_WF/MappingMode/WFMappingLobbyPanel.xaml)
- [`Content.Client/_WF/MappingMode/WFMappingLobbyPanel.xaml.cs`](../../../Content.Client/_WF/MappingMode/WFMappingLobbyPanel.xaml.cs)
- [`Content.Client/_WF/MappingMode/WFMappingLobbyUIController.cs`](../../../Content.Client/_WF/MappingMode/WFMappingLobbyUIController.cs)
- [`Content.Client/_WF/MappingMode/WFMappingMenuUIController.cs`](../../../Content.Client/_WF/MappingMode/WFMappingMenuUIController.cs)
- [`Content.Client/_WF/MappingMode/WFMappingMenuWindow.xaml`](../../../Content.Client/_WF/MappingMode/WFMappingMenuWindow.xaml)
- [`Content.Client/_WF/MappingMode/WFMappingMenuWindow.xaml.cs`](../../../Content.Client/_WF/MappingMode/WFMappingMenuWindow.xaml.cs)
- [`Content.Client/_WF/MappingMode/WFMappingReferenceOverlay.cs`](../../../Content.Client/_WF/MappingMode/WFMappingReferenceOverlay.cs)
- [`Content.Client/_WF/MappingMode/WFMappingReferenceSystem.cs`](../../../Content.Client/_WF/MappingMode/WFMappingReferenceSystem.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingAdminTest.cs`](../../../Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingAdminTest.cs)
- [`Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingGridDeleteTest.cs`](../../../Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingGridDeleteTest.cs)
- [`Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingLibraryListingTest.cs`](../../../Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingLibraryListingTest.cs)
- [`Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingLibraryTest.cs`](../../../Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingLibraryTest.cs)
- [`Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingLimitsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingLimitsTest.cs)
- [`Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingModeTestUtil.cs`](../../../Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingModeTestUtil.cs)
- [`Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingPermissionTest.cs`](../../../Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingPermissionTest.cs)
- [`Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingReferenceTest.cs`](../../../Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingReferenceTest.cs)
- [`Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingSessionTest.cs`](../../../Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingSessionTest.cs)
- [`Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingTickerTest.cs`](../../../Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingTickerTest.cs)

### Unit tests

- [`Content.Tests/_WF/MappingMode/WFMappingDownloadAssemblerTest.cs`](../../../Content.Tests/_WF/MappingMode/WFMappingDownloadAssemblerTest.cs)
- [`Content.Tests/_WF/MappingMode/WFMappingFootprintTest.cs`](../../../Content.Tests/_WF/MappingMode/WFMappingFootprintTest.cs)
- [`Content.Tests/_WF/MappingMode/WFMappingLibraryNamesTest.cs`](../../../Content.Tests/_WF/MappingMode/WFMappingLibraryNamesTest.cs)
- [`Content.Tests/_WF/MappingMode/WFMappingPruneTest.cs`](../../../Content.Tests/_WF/MappingMode/WFMappingPruneTest.cs)
- [`Content.Tests/_WF/MappingMode/WFMappingSessionCodesTest.cs`](../../../Content.Tests/_WF/MappingMode/WFMappingSessionCodesTest.cs)
- [`Content.Tests/_WF/MappingMode/WFMappingThrottleTest.cs`](../../../Content.Tests/_WF/MappingMode/WFMappingThrottleTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/MappingMode/observer.yml`](../../../Resources/Prototypes/_WF/MappingMode/observer.yml)

### Localization

- [`Resources/Locale/en-US/_WF/MappingMode/mapping-mode.ftl`](../../../Resources/Locale/en-US/_WF/MappingMode/mapping-mode.ftl)

### Docs

- [`Docs/_WF/MappingMode/DESIGN.md`](../../../Docs/_WF/MappingMode/DESIGN.md)
- [`Docs/_WF/MappingMode/PLAN.md`](../../../Docs/_WF/MappingMode/PLAN.md)
- [`Docs/_WF/MappingMode/REVIEW.md`](../../../Docs/_WF/MappingMode/REVIEW.md)
- [`Docs/_WF/MappingMode/S1-NOTES.md`](../../../Docs/_WF/MappingMode/S1-NOTES.md)
- [`Docs/_WF/MappingMode/S2-NOTES.md`](../../../Docs/_WF/MappingMode/S2-NOTES.md)
- [`Docs/_WF/MappingMode/S3-NOTES.md`](../../../Docs/_WF/MappingMode/S3-NOTES.md)
- [`Docs/_WF/MappingMode/S5-NOTES.md`](../../../Docs/_WF/MappingMode/S5-NOTES.md)
- [`Docs/_WF/MappingMode/S6-NOTES.md`](../../../Docs/_WF/MappingMode/S6-NOTES.md)

## Non-modular edits

- [`Content.Client/Options/UI/EscapeMenu.xaml`](../../../Content.Client/Options/UI/EscapeMenu.xaml): session, save and library window while mapping
- [`Content.Client/UserInterface/Systems/DecalPlacer/DecalPlacerUIController.cs`](../../../Content.Client/UserInterface/Systems/DecalPlacer/DecalPlacerUIController.cs): mappers open the decal placer
- [`Content.Client/UserInterface/Systems/EscapeMenu/EscapeUIController.cs`](../../../Content.Client/UserInterface/Systems/EscapeMenu/EscapeUIController.cs): mapping session window; crew and character entries hidden while mapping
- [`Content.Client/UserInterface/Systems/Sandbox/SandboxUIController.cs`](../../../Content.Client/UserInterface/Systems/Sandbox/SandboxUIController.cs): mappers open the spawn menus
- [`Content.Server/Physics/Controllers/MoverController.cs`](../../Physics/Controllers/MoverController.cs): mapping observers move on their paused session maps like admin ghosts
- [`Content.Server/Sandbox/SandboxSystem.cs`](../../Sandbox/SandboxSystem.cs): mappers place, erase and lay tiles on their own session's maps
- [`Content.Shared/Input/ContentKeyFunctions.cs`](../../../Content.Shared/Input/ContentKeyFunctions.cs): Escape drops a reference grid being placed
- [`Content.Shared/Verbs/SharedVerbSystem.cs`](../../../Content.Shared/Verbs/SharedVerbSystem.cs): mapping observers without admin data get no verbs; admins keep theirs
- [`Resources/keybinds.yml`](../../../Resources/keybinds.yml): Escape drops a reference grid being placed, ahead of the game menu

<!-- WOLFGATE-GENERATED END -->
