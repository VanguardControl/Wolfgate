# God Panel

One window that lets a gamemaster run the round like a commander: see every player and ship on a map, watch several
of them at once, read what just happened, and act on any of it without hunting through the admin menu.

The model is the overwatch screen of an RTS or a tabletop GM screen: a map in the middle, a roster at the side,
camera feeds, a timeline at the bottom, and a context bar whose buttons change with what is selected. Nothing in
it is new game logic. Every action calls a system Wolfgate already has (Second Chance, Offer to Ghosts, grid power,
ship PA, encounters, the Reaver campaign, sector control, ERT, hails, internet sounds), so the panel is a thin
networked view over them plus an event log.

## Layout

```
+-----------------------------------------------------------------------------------------------+
| ROUND 01:42:10 | WAR: COLD | REAVERS: tier 2 (3 cells) | ENCOUNTERS: 4 running | SOUND: none   |
| [Internet sound] [Pause encounters] [War level] [Crew Setup] [Encounters] [Vessel] [ERT] [...]  |
+---------------------+-----------------------------------------+-------------------------------+
| PLAYERS | SHIPS | OPS|                                         |  FEEDS            [+ free cam] |
| [search..........]  |                                         | +-----------+ +-----------+   |
| * John Doe  (jzo)   |              MAP                        | | feed 1    | | feed 2    |   |
|   Pilot, Halcyon    |        (admin mass scanner,             | | J. Doe    | | Halcyon   |   |
|   72% alive         |         players on, true IFF)           | +-----------+ +-----------+   |
| * Ann Lee (annl)    |                                         | +-----------+ +-----------+   |
|   ghost             |                                         | | feed 3    | | free cam  |   |
| ...                 |                                         | +-----------+ +-----------+   |
+---------------------+-----------------------------------------+-------------------------------+
| SELECTED: John Doe (jzo) on Halcyon, alive 72%, Pilot                                          |
| [Watch] [Follow] [Go to] [Bring] [Second chance] [Offer to ghosts] [Outfit] [Rejuvenate] [...] |
+-----------------------------------------------------------------------------------------------+
| TIMELINE  [all] [deaths] [ships] [encounters] [sector] [gm]                                     |
| 01:41:50  death     John Doe died on Halcyon (gunshot)                                          |
| 01:41:12  ship      Dusk Runner sent an SOS at N-13                                             |
| 01:40:00  encounter Pirate boarders started near Halcyon                                        |
| 01:38:30  gm        Gandalf unpowered Halcyon                                                   |
+-----------------------------------------------------------------------------------------------+
```

The window is a `FancyWindow` and gets the Wolfgate pop-out button, so a GM can put it on a second monitor and keep
the game on the first. Every pane is a split container the GM can resize; the roster, feeds and timeline can each
be collapsed.

## Who may open it

The `godpanel` server command and the God Panel button in the Wolfgate admin tab need the Admin flag. Every action
the panel sends is checked on the server against the flag the underlying tool already needs:

| Flag | Actions |
|---|---|
| Admin | subscribe, feeds, go to, bring, second chance, offer to ghosts, rejuvenate, grid power, end encounter, pause and resume the scheduler, Reaver pause/resume, war level |
| Fun | PA announce, situation code, general quarters, SOS, hail as God, internet sound (all and over PA) |
| Spawn | spawn as outfit, crew setup, spawn vessel, ERT (these open the existing windows; their own commands check) |

A client that lacks a flag sees the button disabled. A request that fails the server check is dropped and logged.

## State

The server keeps a set of subscribed sessions. Every `wf.godpanel.update_interval` seconds (default 1) it builds a
`WFGodPanelState` and sends it to each subscriber. The state is small enough to send whole; the event log is sent
as a delta.

**Round**: round time, war level, Reaver tier and held cell count, running and scheduled encounter counts and
whether the scheduler is paused, the internet sound now playing (title, requester), how many GMs are subscribed.

**Players** (one per session with a body or ghost, plus disconnected bodies): entity, username, character name,
job, antag flag and role from the upstream `PlayerInfo`, grid and its name, map, position (grid-relative, as the
admin scanner sends them), status (`Alive`, `Critical`, `Dead`, `Ghost`, `Lobby`, `Disconnected`), health as a
0..1 fraction from the mob thresholds, whether they are an admin or GM, and whether their body is currently being
offered (second chance or ghost offer pending).

**Grids** (every grid with a name that is a station, has a deed, has players, has NPC crew or belongs to an
encounter): entity, name, kind (`Station`, `Ship`, `Encounter`, `Debris`), map, position, owner username (via the
ShipAccess owner, else the deed), player count, NPC crew count, power (stored fraction and breakers on/total from
`GridPowerSystem.BuildList`), situation code, general quarters, distress, encounter name and state, what it is
docked to, whether it is in station sanctuary.

**Events**: the server keeps the last 500 in a ring with increasing ids; each state carries the events after the
id that session last received. An event has time, category (`Death`, `Player`, `Ship`, `Encounter`, `Sector`,
`Round`, `Gm`), text, an optional subject entity and optional coordinates so the timeline can select or centre on
it. Sources:

- `MobStateChangedEvent` for players (death, crit, revived), with the grid name.
- Player attached and detached (joined a body, ghosted, left); round start and end.
- `WFEncounterStartedEvent`, `WFEncounterResolvedEvent`, encounter reveal.
- `WFSectorCellChangedEvent`, `WFSectorCellContestedEvent`, `WFSectorCellRetakenEvent`, `WFSectorCellDisputedEvent`.
- `WarLevelChangedEvent`.
- Ship alerts: a new `WFShipAlertChangedEvent` (grid, what changed: code, general quarters, distress, announcement)
  raised by `ShipAlertSystem` so the panel and the log see pilots' own changes too.
- Admin tooling: `WFSecondChanceAnsweredEvent`, `WFGhostOfferTakenEvent`, `WFErtSpawnedEvent`, raised by those
  systems, and every panel action, so GMs see each other work.

## Feeds

A feed is the upstream admin camera: the server spawns an `AdminCamera` entity attached to the target and adds a
view subscriber for the GM's session, as `AdminCameraEui` does; the client puts that entity's eye in a
`ScalingViewport`. The panel keeps up to `wf.godpanel.max_feeds` (default 6) per session. A feed follows its
target because the camera is parented to it. The **free camera** is a feed whose camera is parented to the map
instead; the GM drags it (the viewport pans the camera) and the scanner's Teleport toggle can also send it to a
clicked spot, so the GM can look anywhere without moving their ghost. Feeds close when the GM closes them, when
the target is deleted, when the panel unsubscribes or the session leaves.

The map is the existing `AdminRadarControl` (true IFF, players, docks, teleport), driven by a `NavInterfaceState`
the client builds every frame from the GM's own entity, so it needs no console in the world. Clicking a player
label on the map selects that player; the scanner's Teleport toggle keeps working as it does today.

## Actions

Every action is one network message, `WFGodPanelActionEvent` (action, target entity, optional text and flag). The
server system resolves the target, checks the flag, does the action through the owning system and logs it to the
admin log and the timeline.

| Target | Actions |
|---|---|
| Player | Watch (open a feed), Follow (upstream follower), Go to (teleport the GM's ghost to them), Bring (teleport them to the GM), Second Chance, Offer to Ghosts (when the body has no player), Rejuvenate, Spawn as Outfit and Set Outfit (open the existing picker), Player panel and Camera (the upstream verbs) |
| Grid | Watch, Go to, Power on/off (with or without the announcement), Announce over PA (text), Situation code (pick), General quarters on/off, SOS on/off, Hail as God (the server picks a powered shuttle console on the grid), Play sound over PA (url), Stop PA sound, Crew Setup (opens the window on that grid), End encounter |
| Round | Internet sound play/stop, pause/resume the encounter scheduler, Reaver pause/resume, war level COLD/HOT, open Crew Setup, Encounters, Spawn Vessel, ERT Builder, Planet Control, Grid Power |

Actions that already have a prompt elsewhere (Second Chance asks the player, Replace asks the GM) keep it.
Announce and PA sound take their text from a line edit in the context bar.

## Module

`GodPanel` in every area. Shared holds the state records and messages; the server system is split into partials
(`.State.cs`, `.Feeds.cs`, `.Actions.cs`, `.Log.cs`); the client has its system, the window and its panes.
Strings live in `Resources/Locale/en-US/_WF/GodPanel/`. The command is `godpanel` (server, Admin flag) which
sends `godpanelui` to the client, as the other Wolfgate admin windows do.

### Changes to other modules

- `ShipPa`: raise `WFShipAlertChangedEvent` from `SetCode`, `SetGeneralQuarters`, `SetDistress` and announcements.
- `Administration`: raise `WFSecondChanceAnsweredEvent` and `WFGhostOfferTakenEvent` when a player answers, and
  `WFErtSpawnedEvent` after a team spawns. The God Panel button goes in `WolfgateTab.xaml`.
- `Encounters`, `ReaverCampaign`, `SectorControl`, `Audio`, `NpcCrew`, `Hailing`, Mono `WarLevelSystem`: called
  through their existing public methods and events; no change.
- Upstream: none. The admin camera prototype, `ViewSubscriberSystem`, `FollowerSystem`, `AdminSystem.PlayerList`
  and the admin verbs are used as they are.

## Not in the first version

Scripted scenes (cue lists that fire several actions on a timer), per-feed pop-out, ahelp relay for event-related
tickets, a shared GM whiteboard, and drawing on the map. Each fits the same state and action channels later.

## Verifying

`WFGodPanelTest` under `Content.IntegrationTests/Tests/_WF/GodPanel`: a non-admin subscribe is ignored; an admin
subscribe gets a state listing a spawned player and a named grid; opening a feed creates an `AdminCamera` parented
to the target and closing it deletes it; a `WFEncounterStartedEvent` and a mob death land in the log with the
right category; an action without the flag does nothing. The client is checked with the headless sandbox run.
