# Spawning, jobs, ship ownership and multiplay: what Outposts builds on

Source: worktree `ship-broadcast-pa-system-04bd45` at `64f27cb43f` (equal to `origin/main`). Paths are repo-relative.

## 1. GameTicker round flow

**Lobby**
- CVars (`Content.Shared/CCVar/CCVars.Game.cs`): `game.lobbyenabled` true, `game.lobbyduration` 180 s ("Frontier: 150<180"),
  `game.defaultpreset` "nfpirate", `game.map` "Frontier". `Resources/ConfigPresets/_Mono/monolithCore.toml` sets
  `lobbyduration = 180`, `lobbyenabled = true`. `Build/development.toml` sets `lobbyenabled = false`, `map = "NFDev"`,
  so a dev server skips the pre-round lobby entirely.
- `GameTicker.Lobby.cs`: `_playerGameStatuses: Dictionary<NetUserId, PlayerGameStatus>` (NotReadyToPlay, ReadyToPlay,
  JoinedGame). `ToggleReady(player, ready)` works only in `GameRunLevel.PreRoundLobby` and after `_userDb.IsLoadComplete`.
  The client sends console `toggleready {bool}` (`Content.Client/Lobby/LobbyState.cs:260`).
- `RoundPreloadTime` = 15 s: maps load 15 s before the countdown ends (`GameTicker.RoundFlow.cs` ~1095).
- There is no group, party or sub-lobby state anywhere in the ticker. Readiness is per player.

**Round start**: `GameTicker.StartRound(bool force)` (`Content.Server/GameTicking/GameTicker.RoundFlow.cs:365`)
1. Collects ReadyToPlay sessions and each one's `SelectedCharacter` (random profile if prefs are not cached).
2. `LoadMaps()` (raises `LoadingMapsEvent`), `StartGamePresetRules()`, raises `RoundStartingEvent`, `StartPreset(...)`.
3. `_map.InitializeMap(DefaultMap)`, with the comment "MapInitialize *before* spawning players".
4. `SpawnPlayers(readyPlayers, profiles, force)`, then RunLevel = InRound and `RoundStartedEvent`.
- NF POIs are spawned by game rules in `Started` (`Content.Server/_NF/GameRule/PointOfInterestSystem.cs`), so they exist
  and their stations are spawnable before `SpawnPlayers` runs.

**`SpawnPlayers`** (`GameTicker.Spawning.cs:64`)
- First raises `RulePlayerSpawningEvent(PlayerPool, Profiles, Forced)`. Doc: "Remove the players you spawned from the
  PlayerPool and call GameTicker.PlayerJoinGame on them." `AntagSelectionSystem.OnPlayerSpawning`
  (`Content.Server/Antag/AntagSelectionSystem.cs:85`) does exactly that. This is the round-start hook for custom spawns.
- `GetSpawnableStations()` = every entity with `StationJobsComponent` + `StationSpawningComponent`.
- `_stationJobs.AssignJobs(profiles, stations)`, then `AssignOverflowJobs` (Wolfgate: Vagrant only, see section 4).
- A player with no job gets "job-not-available-wait-in-lobby" and stays in the lobby.
- Otherwise `SpawnPlayer(session, profile, station, job, lateJoin: false)`, then `RulePlayerJobsAssignedEvent`.

**`SpawnPlayer(player, character, station, jobId, lateJoin, silent)`** (`GameTicker.Spawning.cs:156`)
- Invalid station: picks a random spawnable station. `lateJoin && DisallowLateJoin`: joins as an observer.
- Raises broadcast `PlayerBeforeSpawnEvent(Player, Profile, JobId, LateJoin, Station)`
  (`Content.Shared/GameTicking/PlayerBeforeSpawnEvent.cs`). If `Handled`: `PlayerJoinGame` and return, skipping all
  spawning. Current subscribers: `DeathMatchRuleSystem`, and `CryoSleepSystem.Returning` (resets the cryo body).
  More broadcast subscribers are allowed; the one-subscriber rule only covers directed (component, event) pairs.
- Job restrictions come from `GetDisallowedJobsEvent` plus job bans, then `PickBestAvailableJobWithPriority` if jobId is null.
- `PlayerJoinGame`, `_mind.CreateMind(userId, character.Name)`. DeltaV: `JobPrototype.AlwaysUseSpawner` forces
  `SpawnPointType.Job` and lateJoin = false.
- `_stationSpawning.SpawnPlayerCharacterOnStation(station, jobId, character, spawnPointType, session)`, then
  `_mind.TransferTo`, `_roles.MindAddJobRole`, a late-join announcement (WF custom title), and
  `_stationJobs.TryAssignJob(station, job, userId)`. The slot is consumed after the spawn.
- Finally raises `PlayerSpawnCompleteEvent(mob, player, jobId, lateJoin, silent, joinOrder, station, profile)`, directed at
  the mob and broadcast. Subscribers: `CustomJobTitleSystem` (WF), `CryoSleepSystem` (stores
  `PlayerJobComponent{JobPrototype, SpawnStation}`), `SpawnCoordinatesSystem` (Mono: chats the spawn X/Y and adds them to
  the briefing), `NfAdventureRuleSystem` (adds `CargoSellBlacklistComponent` and starts the round-end bank tally), and
  antag late-join selection.

**Late join**
- In round, the lobby Ready button becomes Join and opens `Content.Client/_NF/LateJoin/Windows/PickerWindow.xaml.cs`
  (tabs StationOrCrewLarge, Crew and Station). The Station tab lists stations with `IsLateJoinStation` (no
  `ExtraShuttleInformationComponent`); the Crew tab lists player-ship stations. Joining runs
  `joingame <jobId> <stationNetEntity>`.
- `JoinGameCommand` (`Content.Server/GameTicking/Commands/JoinGameCommand.cs`) refuses players who already joined and
  refuses during PreRoundLobby. It checks `TryGetJobSlot > 0`, then calls `ticker.MakeJoinGame(player, station, id)`,
  which goes through `SpawnPlayer` with lateJoin true.
- The job list is sent as `TickerJobsAvailableEvent(Dictionary<NetEntity, StationJobInformation>)`, built in
  `StationJobsSystem.GenerateJobsAvailableEvent` (`Content.Server/Station/Systems/StationJobsSystem.cs:502`).
  `StationJobInformation{StationName, JobsAvailable, IsLateJoinStation, StationDisplayInfo, VesselDisplayInformation{
  VesselAdvertisement, Vessel, HiddenIfNoJobs}}` is in `Content.Shared/GameTicking/SharedGameTicker.cs:155`.

**Respawn**
- The ghost Respawn button runs `ghostrespawn` (`Content.Server/_NF/Commands/GhostRespawnCommand.cs`). It needs
  `nf14.respawn.enabled`, a ghost body and an expired `RespawnSystem.GetRespawnTime`, then calls `GameTicker.Respawn`:
  `_mind.WipeMind`, `RespawnSystem.Respawn`, and `PlayerJoinLobby`. The player is back in the lobby and rejoins
  through the picker.
- Timers (`Content.Shared/_NF/CCVar/NFCCVars.cs`, `Content.Server/_Corvax/Respawn/RespawnSystem.cs`):
  `nf14.respawn.cryo_first_time` 20 s, `nf14.respawn.time` 1200 s after death or a repeat cryo. Admins and players
  leaving a ghost role are not penalised.

## 2. StationSpawningSystem and spawn points

`Content.Server/Station/Systems/StationSpawningSystem.cs`
- `SpawnPlayerCharacterOnStation(station?, job?, profile?, stationSpawning, SpawnPointType, ICommonSession? session)`
  raises `PlayerSpawningEvent` and returns `ev.SpawnResult`. It throws if `station` is not a station.
- `SpawnPlayerMob(EntityCoordinates, job?, profile?, station?, entity?, session?)` is public. Anyone can build a
  character at arbitrary coordinates with it; the ERT builder does (section 8). It:
  - resolves the `RoleLoadout` from `profile.Loadouts[LoadoutSystem.GetJobPrototype(job)]`, or the default loadout;
  - if the job has a `JobEntity`, spawns that instead (borg-style) and returns early;
  - spawns the species mob (`ic.random_characters` can override the species);
  - Frontier loadout billing: equips each selected loadout, in group order, while
    `Price <= bankBalance && (Price <= 0 || hasBalance)`. `hasBalance` holds only if the session's cached prefs contain
    this profile. The starting budget is `profile.BankBalance` plus the MonoCoins balance. Group fallbacks fill up to
    `MinLimit`, job `StartingGear` is equipped, and the total is charged with `_bank.TryBankWithdraw`;
  - Mono: `_persistence.LoadPersistentData(entity, profile, session)` restores per-character saved flags, components
    and items;
  - sets the name (the loadout `EntityName` pseudonym wins), and `SetPdaAndIdCardData(entity, name, job, station)`: ID
    name, job title and icon, `SetAccessToJob(card, job, extendedAccess)`, and the PDA owner. With `job == null` no ID
    is set up.
- `PlayerSpawningEvent` fields: `SpawnResult`, `Job`, `HumanoidCharacterProfile`, `Station`, `DesiredSpawnPointType`
  (DeltaV), `Session` (Frontier). Handlers run in this order:
  1. `ContainerSpawnPointSystem` (before SpawnPointSystem). Acts only if `profile.SpawnPriority == Cryosleep` or the
     job has a `JobEntity`. Skips desired types Observer and LateJoin. Inserts the mob into a
     `ContainerSpawnPointComponent{ContainerId, Job, SpawnType}` on the same station.
  2. `ArrivalsSystem.HandlePlayerSpawning`: late join only, and off because `shuttle.arrivals` is false
     ("Frontier: false", `Content.Shared/CCVar/CCVars.Shuttle.cs:33`). Arrivals is not used.
  3. `SpawnPointSystem` (`Content.Server/Spawners/EntitySystems/SpawnPointSystem.cs`), last. It keeps only points
     where `GetOwningStation(point) == args.Station`, using Job-type points that match the job before the round and
     LateJoin points in round. It falls back to any spawn point on the same station, and otherwise logs
     "No spawn points were available for station" and leaves the result null.
  4. DeltaV `MailSystem` runs after it to register mail.
- `SpawnPointComponent` (`Content.Server/Spawners/Components/SpawnPointComponent.cs`): `job_id`, and `spawn_type`
  (`SpawnPointType {Unset, LateJoin, Job, Observer}`). Markers `SpawnPointLatejoin` and `SpawnPointJobBase` are in
  `Resources/Prototypes/Entities/Markers/Spawners/jobs.yml` and are used on the Mono outposts and POIs
  (`Resources/Maps/_Mono/Outpost/colonial.yml`, `colossus_central.yml`, `POI/camelot.yml`, ...).
- Upstream container spawners `CryogenicSleepUnitSpawner` and `CryogenicSleepUnitSpawnerLateJoin` are in
  `Resources/Prototypes/Entities/Structures/cryogenic_sleep_unit.yml`. The Frontier cryopod `MachineCryoSleepPod`
  (`Resources/Prototypes/_NF/Entities/Structures/Machines/cryopod.yml`) has no `ContainerSpawnPoint`: it is for leaving,
  not arriving.
- The profile field `SpawnPriority` (`Content.Shared/Preferences/SpawnPriorityPreference.cs`) is
  `{None=0, Arrivals=1, Cryosleep=2}`, commented "Stored in database!" and "DO NOT TOUCH". The editor's
  `SpawnPriorityButton` sits on the Species tab. `EnsureValid` clamps unknown values to None
  (`HumanoidCharacterProfile.cs:743`).

## 3. Job selection, loadouts and the lobby character editor

- Editor tabs (`Content.Client/Lobby/UI/HumanoidProfileEditor.xaml(.cs)`, indices shifted by WF): 0 Species, 1 Appearance,
  2 Jobs, 3 Traits, 4 Company (Mono faction), 5 Markings, 6 Genitals (WF). A Mono "Saved items" tab is appended at
  runtime only when `Profile.Items` is non-empty. It loads each saved item's YAML on the client with
  `MapLoaderSystem.TryLoadEntity` to preview it (~line 1296), a precedent for previewing saved data in the editor.
- Jobs tab: `PreferenceUnavailableButton` and `JobList`, where each job row has a priority selector and a loadout
  button. The button calls `OpenLoadout(job, roleLoadout, roleLoadoutProto)`, which creates
  `new LoadoutWindow(Profile, roleLoadout, roleLoadoutProto, session, collection)`
  (`Content.Client/Lobby/UI/Loadouts/LoadoutWindow.xaml.cs`). WF adds a Custom Job button on rows whose role has a
  `CustomJobTitlePrototype` (`Content.Client/_WF/Roles/HumanoidProfileEditor.CustomJobTitle.cs`).
- Profile (`Content.Shared/Preferences/HumanoidCharacterProfile.cs`): `_loadouts: Dictionary<string, RoleLoadout>` keyed
  by role loadout id = `LoadoutSystem.GetJobPrototype(jobId)` = `"Job" + jobId` (e.g. `JobContractor`);
  `_jobPriorities: Dictionary<ProtoId<JobPrototype>, JobPriority>`; `SpawnPriority`; `BankBalance` (Frontier);
  `Company` (Mono); `Flags`, `Components`, `Items` (Mono persistence); `CustomSpeciesName` and `Genitals` (WF).
- `RoleLoadout` (`Content.Shared/Preferences/Loadouts/RoleLoadout.cs`): `Role`,
  `SelectedLoadouts: Dictionary<ProtoId<LoadoutGroupPrototype>, List<Loadout>>`, `EntityName`, `CustomJobTitle` (WF
  DataField) and `Points`. `Loadout` is just `{Prototype}`.
- DB (`Content.Server.Database/Model.cs:653`): `ProfileRoleLoadout{RoleName, EntityName(256), CustomJobTitle(256, WF)}`,
  then `ProfileLoadoutGroup{GroupName}`, then `ProfileLoadout{LoadoutName}`. Everything is keyed by string ids, so a new
  role loadout needs no migration. But `EnsureValid` deletes any role loadout whose `RoleLoadoutPrototype` does not exist
  (`HumanoidCharacterProfile.cs` ~826).
- Prototypes: `RoleLoadoutPrototype{Groups, Points, CanCustomizeName (Mono default true), NameDataset}`;
  `LoadoutGroupPrototype{Name, MinLimit=1, MaxLimit=1, Hidden, Loadouts, Subgroups, Fallbacks}`;
  `LoadoutPrototype{StartingGear, Equipment, Inhand, Storage, Components, Effects, Price (Frontier), DummyEntity,
  PreviewEntity}`. Example: `JobContractor` in `Resources/Prototypes/_NF/Loadouts/role_loadouts.yml:21`, with groups
  MercenaryHead, ContractorNeck, MercenaryJumpsuit, ... ContractorPDA.

## 4. Wolfgate Roles (CustomJobTitle) and Station (overflow)

**Roles** (`Content.Server/_WF/Roles/README.md`)
- `CustomJobTitlePrototype` (`Content.Shared/_WF/Roles/CustomJobTitlePrototype.cs`, type `customJobTitle`). Its id is the
  role loadout id. Fields: `MaxLength` (default `IdCardConsoleComponent.MaxJobTitleLength`), `BlockedTitles` and
  `BlockedWords`. The only instance is `JobContractor` in `Resources/Prototypes/_WF/Roles/custom_job_titles.yml`.
  Contractor's display name is "Vagrant" (`job-name-contractor`).
- `CustomJobTitleRules` (shared static): `Clean`, `Normalize` (folds look-alike digits), `IsValid` (ASCII plus
  `" -'.,&/()"`, must contain a letter, may not match any job's localized name, blocked titles or whole-word
  phrases), `GetTitle(profile, jobId, proto)`, `Sanitize(title, role, proto)` and `JoinMenuName`
  (e.g. "Vagrant (Bounty Hunter)").
- The title is stored as `RoleLoadout.CustomJobTitle` and in the DB column `ProfileRoleLoadout.CustomJobTitle`.
- `CustomJobTitleSystem` (server) handles `PlayerSpawnCompleteEvent`: it puts `CustomJobTitleComponent{Job, Title}` on
  the mind and calls `IdCardSystem.TryChangeJobTitle`. On `AfterGeneralRecordCreatedEvent` it rewrites
  `Record.JobTitle`. `SharedJobSystem.MindGetAdminJobName` shows "Job (Title)" to admins.
- Marked upstream edits: `GameTicker.Spawning.cs` (late-join announcement), `RoleLoadout.cs`, `Model.cs`,
  `ServerDbBase.cs`, `HumanoidProfileEditor.xaml.cs`, the NF picker controls, `AdminSystem.cs` and `SharedJobSystem.cs`.
- For Outposts: an "Outpost Job" (the player's own title plus a starting outfit) maps onto this pair: a new
  `RoleLoadoutPrototype` for its gear, and a `customJobTitle` entry with the same id for its name.

**Station** (`Content.Server/_WF/Station/`)
- `StationJobsSystem.Overflow.cs` is a partial with `WFNotifyNoOverflowSlot(session)`, which sends
  `wf-job-no-overflow-slot-wait-in-lobby`.
- Marked edit `StationJobsSystem.cs:58`: overflow jobs are filtered to `SharedGameTicker.FallbackOverflowJob`
  (= "Contractor", i.e. Vagrant). Marked edit `StationJobsSystem.Roundstart.cs:343`: a player who gets no job is told
  so. Covered by `Content.IntegrationTests/Tests/_WF/Station/WFOverflowJobTest.cs`.
- Frontier also checks `_playTime.IsAllowed(session, overflowJob)` inside `AssignOverflowJobs`, and only players with
  `PreferenceUnavailable == SpawnAsOverflow` receive overflow at all.

## 5. Cryosleep (Frontier) and "return to your body"

`Content.Server/_NF/CryoSleep/`
- `CryoSleepComponent{BodyContainer, LeaveSound, CryosleepDoAfter}` sits on `MachineCryoSleepPod`. The
  `MachineCryoSleepPodFallback` variant adds `CryoSleepFallbackComponent`, a pod to return to when yours is gone.
- Leaving: `CryoStoreBody(body, pod)` raises `CryosleepBeforeMindRemovedEvent`, which RespawnSystem uses to start the
  short cryo respawn timer. It then ghosts the mind, records `_storedBodies[userId] = {Body, Cryopod}` and reopens the job
  slot, but only when the player's `PlayerJobComponent.SpawnStation` has a grid with `ForceAnchorComponent` (anchored
  stations, not ships). The body moves to a paused storage map at an incrementing offset (Mono), `CryosleepEnterEvent`
  is raised, and a radio message goes to Common, Freelance (pirates) or Nfsd (TSF). A `Timer` then deletes the body after
  `nf14.uncryo.maxtime` = 180*60 s (3 h).
- Returning (`CryoSleepSystem.Returning.cs`): the ghost UI's Cryosleep Return button (`GhostGui.xaml.cs`) opens
  `CryosleepWakeupWindow`, which sends `WakeupRequestMessage`. `TryReturnToBody(mind)` needs
  `nf14.uncryo.enabled` (true) and a ghost. It inserts the body into the original pod, or into any free
  `CryoSleepFallback` pod if the original is deleted, and fails with `Occupied` if the original pod is busy. It then
  calls `_mind.ControlMob`, forces 5 s of sleep and raises `CryosleepWakeUpEvent`. Results are
  `ReturnToBodyStatus` values (Success, Disabled, BodyMissing, NotAGhost, NoCryopodAvailable, Occupied).
- The stored body is forgotten (and deleted if still on the storage map) on `PlayerJoinedLobbyEvent`, on
  `PlayerBeforeSpawnEvent`, and on round end (`_storedBodies.Clear()`), so it does not survive a respawn or a round.
- Upstream `Content.Server/Bed/Cryostorage/CryostorageSystem.cs` also exists and reopens slots with
  `TryAdjustJobSlot(uniqueStation, job, 1)`.

## 6. Ghost roles, and how crews are offered

- `GhostRoleComponent` (`Content.Server/Ghost/Roles/Components/GhostRoleComponent.cs`): `name`, `description`, `rules`,
  `requirements`, `makeSentient`, `prob`, `MindRoles` (default `MindRoleGhostRoleNeutral`), `reregister`,
  `raffle: GhostRoleRaffleConfig`, `job: ProtoId<JobPrototype>?` and `Prototype`.
- `GhostRoleSystem` API: `RegisterGhostRole`, `UnregisterGhostRole`, `Request(player, identifier)` (joins the
  raffle), `Takeover`, `GhostRoleInternalCreateMindAndTransfer(player, roleUid, mob, role)`, and `LeaveRaffle`.
  `TakeGhostRoleEvent` is raised on the role entity. Ghost roles are only open to ghosts, not to lobby players.
- `Content.Server/_NF/Players/GhostRole/` adds whitelist and candidate events (`GetDisallowedGhostRolesEvent`,
  `IsGhostRoleAllowedEvent`, `GhostRolesGetCandidatesEvent`).
- Player crews on Frontier/Mono are not ghost roles. A purchased ship whose vessel id has a matching `gameMap` becomes
  a station with `StationJobs` (usually `Contractor: [0, 0]`, e.g. `Resources/Prototypes/_Mono/Shipyard/Civilian/arribane.yml`,
  station proto `StandardFrontierVessel` in `Resources/Prototypes/_NF/Entities/Stations/nanotrasen.yml:87`, which
  parents `BaseStationJobsSpawning` = StationJobs + StationSpawning). The owner opens slots and sets an advertisement
  through the ship's station records console (`AdjustStationJobMsg`, `SetStationAdvertisementMsg` in
  `Content.Server/StationRecords/Systems/GeneralStationRecordConsoleSystem.cs:39`). Late joiners pick the ship in the
  picker's Crew tab and spawn at its spawn points. `ShuttleConsoleSystem.HandleJobSlotsOnPowerChange` saves the slots and
  sets them to 0 while no shuttle console on the grid has power, then restores them.
- NPC ship crews (Mono) are AI ships run by HTN (`Content.Server/_Mono/NPC/HTN/ShipSteeringSystem.cs`,
  `ShipTargetingSystem.cs`, `ShipNpcTargetComponent`), not offered to players. Ghost-role spawners are rare on maps
  (the only common one is `SpawnChimeraGhostrole`, in 12 map files).
- The WF ERT builder is the one system that offers a group of players places on a spawned ship (section 8).

## 7. How a purchased ship is tied to the buyer

- `ShuttleDeedComponent` (`Content.Shared/_NF/Shipyard/Components/ShuttleDeedComponent.cs`, "Tied to an ID card when a
  ship is purchased. 1 ship per captain."): `ShuttleUid`, `ShuttleName`, `ShuttleNameSuffix`, `ShuttleOwner` (a name
  string), `PurchasedWithVoucher`, `PurchaseVoucherUid` and `DeedHolder`. The same component goes on both the ID card
  and the shuttle grid. It is access-restricted to `SharedShipyardSystem`, `SharedShuttleRecordsSystem` and
  `SharedShuttleConsoleLockSystem`, so writes must go through a `ShipyardSystem` partial (which is why WF puts its code
  in `Content.Server/_WF/Shipyard/ShipyardSystem.WolfgateDeed.cs`).
- The purchase (`ShipyardSystem.Consoles.cs:93 OnPurchaseMessage`) refuses an ID that already has a deed
  ("shipyard-console-already-deeded"). `TryPurchaseShuttle` loads the grid on the shipyard map and
  `TryFTLDock`s it to the console's station (`ShipyardSystem.cs:132`). If a `gameMap` with the vessel id exists,
  `_station.InitializeNewStation(stationProto.Stations[vessel.ID], [shuttle])` makes it a station with
  `ExtraShuttleInformationComponent.Vessel`. Then: `FTLLockComponent`, deeds on the ID and the grid, a
  `ShuttleConsoleLockComponent` plus `SetShuttleId` on every console, `ShipOwnershipSystem.RegisterShipOwnership`, the
  ID job title set to the console's `NewJobTitle` (if set and no voucher), a "Captain" general record on the ship's station, and
  `ShipAccessReaderComponent` on doors and lockers (Mono, `AddShipAccessToEntities`).
- `ShipOwnershipComponent{OwnerUserId: NetUserId, LastStatusChangeTime, IsOwnerOnline, DeletionTimeoutSeconds=3600}` on
  the grid. Only the online flag is tracked; nothing reads `DeletionTimeoutSeconds`, so nothing deletes ships.
- Mono `DeedCopySystem`: using a deeded ID on a deedless ID copies the deed. NF `StationDeedSpawnerComponent`: an ID
  spawned on a deeded grid copies that grid's deed at MapInit.
- WF `ShipyardSystem.TryAssignDeed(shuttleUid, idCard, owner, vessel?, shipName?)`
  (`ShipyardSystem.WolfgateDeed.cs`) runs every post-payment purchase step on an already spawned grid: company, station
  (if a vessel `gameMap` exists), FTL lock, both deeds, console locks, ownership, the owner's general record, ship
  access, `LinkedLifecycleGridParentComponent`, vessel tags and crew requirement, the shuttle record,
  `ShipyardShuttlePurchaseEvent` and the name. It skips payment, vouchers, ID access and job title. This is the call to
  register a spawned crash ship or outpost to a player.
- `ShipyardSystem.WolfgateTrader.cs` also has `TrySaveShip(grid, out yaml)`, `TryAddSavedShip(yaml, out grid)`,
  `TryDockLoadedShuttle(station, grid)`, `StripForResale`, `GetShipAppraisal` and `TryGetDeedShip(idCard, out ship)`,
  which serialise a grid to YAML in memory and load it back.
- **Spawning into a ship at round start does not exist.** A grep of `_Mono`, `_NF` and `_WF` for starting ship, home
  ship, spawn on shuttle and similar only finds the PDA's `OwnedShipName`. Round-start spawning only targets stations
  that exist when `SpawnPlayers` runs, and ship stations are created mid-round by purchases. Arrivals is off. A deed does
  not follow the player: it lives on an ID card, and when a cryo body expires or a player respawns, the new character
  has no deed.

## 8. Putting players aboard a spawned ship: the WF ERT builder

`Content.Server/_WF/Administration/Systems/ErtSystem.cs` (admin, `AdminFlags.Spawn`) is the closest existing flow to a
multiplay crash:
1. `TrySpawnTeam(admin, ErtConfig)` (config: TeamName, Briefing, Members 1..`ErtLimits.MaxMembers`=30, HasLeader,
   titles, outfits (StartingGear ids), Species, GenericHumans, AccessGroups, KeepOutfitAccess, Vessel) loads the vessel
   with `AdminVesselSpawnSystem.TrySpawnVessel` at the admin's position and names it `"{vessel} ({team})"`.
2. `FindSpawnSpot(grid)` returns the first `SpawnPointComponent`, else a `CryoSleepComponent`, else a
   `ShuttleConsoleComponent` on that grid.
3. It spawns one `WFErtSpawner` marker per place (`GhostRole` with a short raffle plus `WolfgateErtSpawner`,
   `Resources/Prototypes/_WF/Administration/Entities/Markers/ert_spawner.yml`), created uninitialised so the role
   registers with the right text.
4. `PromptGhosts` sends a network `ErtCalledEvent(teamId, team, briefing, places, hasLeader)` to every ghost with a
   chime. A ghost answers with `ErtSignUpEvent`, and `PickSlot` spreads entrants over the open places before calling
   `_ghostRoles.Request`.
5. On `TakeGhostRoleEvent`: `PickProfile` (the player's own selected character if its species is allowed),
   `_stationSpawning.SpawnPlayerMob(coords, null, profile, null)`, `SetOutfitCommand.SetOutfit`, ID name, title and
   access, `GhostRoleInternalCreateMindAndTransfer`, then the marker is deleted and prompts close when the team is full.
- The pattern (spawn the grid, find a spot, build each player from their profile with `SpawnPlayerMob`, transfer the
  mind) is reusable. The ghost-role and raffle layer is not usable pre-round, because lobby players are not ghosts.

## 9. Admin vessel spawner

`Content.Server/_WF/Administration/Systems/AdminVesselSpawnSystem.cs` and `Commands/SpawnVesselCommand.cs`
(`spawnvessel <vesselId> [ownerUsername]`, `AdminFlags.Spawn`):
- `TrySpawnVessel(VesselPrototype, MapId, Vector2 worldPos, EntityUid? spawner, out grid)` calls
  `_mapLoader.TryLoadGrid(mapId, vessel.ShuttlePath, out grid, offset: position)`, then
  `EntityManager.AddComponents(grid, vessel.AddComponents)` and sets the name to `vessel.Name`, with an admin log. It sets
  no deed and no owner, and does not check whether the area is clear.
- `TryGetDeedCard(owner, out idCard, out errorKey)` fails if the owner has no entity or no ID, or the ID already has a
  deed ("cmd-spawnvessel-owner-has-deed"). `TryAssignOwner(grid, vessel, idCard, owner)` wraps
  `ShipyardSystem.TryAssignDeed`.
- The command spawns at the admin's world position on their map. The owner is resolved before spawning, so a bad owner
  leaves nothing behind.
- NF `PointOfInterestSystem.TrySpawnPoiGrid` (`Content.Server/_NF/GameRule/PointOfInterestSystem.cs:254`) is the
  round-start equivalent: `TryLoadGrid(map, path, offset, rot: random)` plus `InitializeNewStation` from a same-id
  `gameMap`. `GetRandomPOICoord` retries `nf14.worldgen.poi_placement_retries` (10) times to keep
  `nf14.worldgen.min_poi_distance` (400) from existing stations, and gives up by returning the last roll.

## 10. Making a shuttle console uncontrollable

- Piloting starts in `ShuttleConsoleSystem.OnConsoleUIOpenAttempt` (the directed sub
  `(ShuttleConsoleComponent, ActivatableUIOpenAttemptEvent)`, `Content.Server/Shuttles/Systems/ShuttleConsoleSystem.cs:178`),
  which applies the crewed-shuttle rule and then `TryPilot(user, console)`. `TryPilot` requires the `CanPilot` tag, power,
  an anchored console, `CanInteract`, `_access.IsAllowed` (Frontier) and an unlocked console. It then
  `EnsureComp<PilotComponent>(user)` and `AddPilot`. `PilotComponent` (`Content.Shared/Shuttles/Components/PilotComponent.cs`)
  holds `Console`, `Position`, `HeldButtons` and Mono's `SetMaxVelocity`. Public helpers: `AddPilot`,
  `RemovePilot(entity)` and `ClearPilots(ShuttleConsoleComponent)`.
- Deed lock: `ShuttleConsoleLockComponent{Locked=true, ShuttleId}` on consoles and `ShipGridLockComponent{Locked, ShuttleId}`
  on the grid. `SharedShuttleConsoleLockSystem.GetEffectiveLockState` lets the grid lock override the console. The
  lock system also cancels the UI through its own `(ShuttleConsoleLockComponent, ActivatableUIOpenAttemptEvent)`
  subscription. `SetGridLockState` is protected. The server `ShuttleConsoleLockSystem` locks and unlocks by swiping
  the deeded ID, PDA or voucher, adds guest access verbs, and kicks the subscribed pilots when it locks. Because the
  owner's ID unlocks it, this is not a forced lock.
- A forced "crash sequence" lock therefore needs its own marker component on the console (or grid) with its own
  `ActivatableUIOpenAttemptEvent` subscription, which is allowed since it is a new component. Pair it with
  `ClearPilots` when the sequence starts. Both existing (component, event) pairs above are already taken.
- `FTLLockComponent.Enabled` (`Content.Server/Shuttles/Components/FTLLockComponent.cs`) only controls "whether a shuttle
  will FTL with docked shuttles or automatically undock"; it is not a pilot lock. FTL APIs on `ShuttleSystem`:
  `CanFTL(shuttle, out reason)`, `FTLToCoordinates`, `FTLToDock`, `TryFTLDock` and `TryFTLProximity`
  (`ShuttleSystem.FasterThanLight.cs`).
- `ShuttleSystem.Enable` and `Disable(uid, force)` switch the grid body between Dynamic and Static; both return early
  on `PreventGridAnchorChangesComponent` unless `force`. NF `ForceAnchorComponent` "sets the grid to anchored and
  prevents further changes on init". It is also what cryo slot reopening checks, and it exempts a grid from impact
  damage (`ShuttleSystem.Impact.cs:175`).
- WF ShipStatus (`Content.Shared/_WF/Shuttles/ShipStatus.cs`, `Content.Server/_WF/Shuttles/Systems/ShipStatusSystem.cs`)
  is hull telemetry for the console's whole-ship view (per-tile damage, fire, unpowered and pressure, sent only while
  the view is open). It locks nothing, but it could show crash damage.
- No crash-landing code exists: a grep for crashland, CrashLanding and ShuttleCrash finds nothing.

## 11. ShipPa API for a crash countdown

`Content.Server/_WF/ShipPa/` (module ShipPa). Speakers are any `ShipPaSpeakerComponent` anchored to the grid. Air
alarms stand in when a grid has none (`Fallback`). A speaker works if it is anchored, not broken, and powered or
without an `ApcPowerReceiver`.
- `ShipPaSystem.Announce(grid, message, SoundSpecifier? sound = null, string? sender = null, Color? color = null)` returns
  false if the grid has 0 working speakers. It plays the chime (default: the grid's
  `ShipAlertComponent.AnnouncementChime`) as an Announcement broadcast with a caption, and sends
  `DispatchFilteredAnnouncement` only to `GetListeners(grid)`, i.e. players in range of a working speaker.
- `Broadcast(grid, sound, audioParams?, priority, key = "announcement")` returns the broadcast id or null.
- `StartAlarm(grid, key, sound, audioParams?, message?, color?, priority = DefaultAlarmPriority)` loops until
  `StopAlarm(grid, key)`, and `IsAlarmActive(grid, key)` reports it. A new broadcast replaces any with the same key.
  `StartTrack(...)` plays once.
- `ShipAlertSystem.SetCode(grid, ProtoId<ShipAlertCodePrototype>, user? = null, announce = true)` works for codes that
  are not `Selectable` when user is null. It raises `ShipAlertCodeChangedEvent`. Codes are in
  `Resources/Prototypes/_WF/ShipPa/alert_codes.yml`: `WFShipCodeGreen`, `WFShipCodeYellow`, `WFShipCodeRed` and
  `WFShipCodeBlack`. Prototype fields: `Name`, `Description`, `Announcement` (loc with `ship`), `Color`, `Order`,
  `Selectable` and `Sound`.
- `ShipAlertSystem.SetGeneralQuarters(grid, active, user?)` starts or stops the klaxon loop (key `"general-quarters"`)
  plus an announcement, and raises `ShipGeneralQuartersChangedEvent`.
- Also `GetShipName(grid)` and `CountSpeakers(grid)` returning (Online, Total, Fallback). Admin commands:
  `shippa_announce`, `shippa_code`, `shippa_gq` and `shippa_speakers`.
- A crash countdown can be `SetCode(grid, <new non-selectable "crash" code>)`, `StartAlarm(grid, "wf-crash", klaxon)`,
  and a timer calling `Announce` at T-3:00, T-1:00 and T-0:10. It needs a powered speaker or air alarm on the
  shuttle; otherwise `Announce` returns false and nobody hears it.

## 12. Parties, groups, invites

- Nothing pre-round. A grep for party, squad, invite, playgroup, join code and fireteam across Content.* finds only:
  - `Content.Server/_Rat/Squad/SquadSystem.cs` and `Content.Shared/_Rat/Squad/SquadComponent.cs{SquadId, SquadName}`:
    in-round squads per faction (`CreateSquad(faction, name)`, `AssignToSquad(entity, squadId, faction)`), in memory,
    cleared on round restart, driven by the `_Rat/Overwatch` console. They are entity-based and have no lobby side.
  - Mono `Company` (`Content.Shared/_Mono/Company/CompanyPrototype.cs`; profile `Company`, ID `CompanyName`, ship
    `CompanyComponent`, `CompanyAccessReader`): faction membership chosen in the editor's Company tab, with an optional
    whitelist (`MsgCompanyWhitelist`). This is the "faction" the design doc means for ship access.
- The only existing invite-like UX is ERT (`ErtCalledEvent` prompt, then `ErtSignUpEvent`) and the ghost role raffle.
  Both are ghost-only.
- Existing join-someone's-crew flow: ship station job slots plus the late-join picker's Crew tab (section 6). It works
  only in round, and anyone can take an open slot; there is no per-player invite or code.

## 13. Content.Client/_WF/Spawning

Not player spawning. It is the admin and sandbox **entity spawn menu** (`WolfgateSpawnUIController`,
`WolfgateSpawnWindow`, `WolfgateSpawnClassifier`, `WolfgateSpawnCategories`, `WolfgateSpawnCatalog`, favourites in the
`wf.spawn_favourites` cvar), replacing the engine's flat spawn panel. The module name `Spawning` is taken, so Outposts'
spawn options need a different module name.

## 14. Other APIs spawn options will touch

- Bank (`Content.Server/_NF/Bank/BankSystem.cs`): `TryBankWithdraw(ICommonSession, PlayerPreferences, HumanoidCharacterProfile,
  amount, out newBalance, spendLongTerm = false)` (line 191) works on the profile balance before any body exists, which
  is what "deduct the outpost price at spawn, reject if short" needs. `TryBankWithdraw(EntityUid mob, amount)` is the
  in-round version. `StationSpawningSystem` charges loadouts with the first form after spawning.
- `IsJobAllowedEvent(Player, JobId, Cancelled)` (`Content.Server/GameTicking/Events/IsJobAllowedEvent.cs`) is raised in
  the `SpawnPlayer(player, station, jobId, ...)` overload used by `MakeJoinGame`. It carries no station, so it cannot
  gate "this outpost's slots" per player unless each outpost role has its own job id.
- `GameTicker.SpawnPlayer` is private. The public entry points are `MakeJoinGame(player, station, jobId?, silent)`
  (requires a known lobby status and a loaded user DB, and honours `DisallowLateJoin`), `JoinAsObserver`,
  `PlayerJoinGame(session, silent)` and `Respawn`.

## Implications for Outposts

1. **Round-start spawn options hook `RulePlayerSpawningEvent`.** It is broadcast, runs after maps and rules are
   initialised and before job assignment, and the documented contract is "remove from PlayerPool and call
   `PlayerJoinGame`". Load the crash shuttle or saved outpost grid there, then build each member.
2. **Two ways to build a player on a custom grid.**
   (a) Minimal: make the grid a station (`InitializeNewStation` from a gameMap-style config with `StationJobs` and
   `StationSpawning`, as ships and POIs do) holding a WF outpost/crash job, then call `GameTicker.MakeJoinGame(player,
   station, job, silent: true)`. `SpawnPointSystem` already limits points to the owning station, so players land on
   that grid's spawn points, and the job slot, mind, `PlayerSpawnCompleteEvent`, custom title, cryo bookkeeping and bank
   tally all come for free. Caveats: `lateJoin` is true on this path, so pass `silent` to skip arrival announcements;
   before InRound, SpawnPointSystem wants Job-type points matching the job (otherwise it falls back to any point on the
   station).
   (b) Manual, as ErtSystem does: `SpawnPlayerMob` at the coordinates, `CreateMind` + `TransferTo`, `MindAddJobRole`,
   and raise `PlayerSpawnCompleteEvent` yourself, or the WF, Mono and NF subscribers miss the spawn.
3. **Outpost cryopod spawning.** `ContainerSpawnPointSystem` only runs when profile `SpawnPriority == Cryosleep` or for
   `JobEntity` jobs, and the NF `MachineCryoSleepPod` has no `ContainerSpawnPoint`. An "Outpost Cryopod" therefore needs
   either a WF `PlayerSpawningEvent` handler ordered `before: SpawnPointSystem` that inserts into the pod, or plain
   spawn-point markers next to it. Don't add values to `SpawnPriorityPreference`: it is DB-stored and marked
   "DO NOT TOUCH". Keep the spawn option choice in new state.
4. **Outpost Job is expressible without a DB migration**: a `JobPrototype` plus a `RoleLoadoutPrototype` (`Job<id>`,
   its gear groups) plus a `customJobTitle` entry with the same id. Profile loadouts are keyed by string role id and
   `ProfileRoleLoadout` already stores `CustomJobTitle`. `EnsureValid` drops loadouts whose prototype is missing, so
   the role loadout must be a real prototype. `LoadoutWindow` takes `(profile, RoleLoadout, RoleLoadoutPrototype, session,
   collection)` and can be reused. Loadout prices are charged from the bank at spawn.
5. **Ownership: use `ShipyardSystem.TryAssignDeed`**, but note one deed per ID card ("shipyard-console-already-deeded",
   `TryGetDeedCard`). A player with a ship cannot hold an outpost deed on the same card. Deeds do not survive a
   respawn or the 3 h cryo expiry, so "linked to your character" needs its own record keyed to profile or NetUserId, not
   the deed. `ShipOwnershipComponent.OwnerUserId` is the closest existing player link.
6. **Crash sequence lock**: add a WF marker component on the console with its own `ActivatableUIOpenAttemptEvent`
   subscription and call `ShuttleConsoleSystem.ClearPilots` at the start. Don't reuse `ShuttleConsoleLockComponent` or
   `ShipGridLockComponent`, since the owner's ID unlocks those. `FTLLockComponent` is unrelated. Also think about
   `ForceAnchorComponent` and `ShuttleSystem.Enable`/`Disable(force)` for anchoring outposts. `ShipStatusSystem` can show
   crash damage.
7. **Countdown over the PA**: `ShipAlertSystem.SetCode` (a new non-selectable crash code in
   `Resources/Prototypes/_WF/ShipPa/alert_codes.yml` style), `ShipPaSystem.StartAlarm(grid, key, sound)` and a timer
   calling `Announce`. `Announce` returns false without a working speaker or air alarm, so crash shuttles must have
   powered speakers. Fall back to a popup or chat message when it returns false.
8. **Multiplay needs new server state.** No party, group, invite or join-code system exists (`_Rat` squads are
   in-round and entity-based; Mono Company is a faction). Ghost roles and the ERT prompt only reach ghosts, so they cannot
   carry pre-round sign-ups, but the ERT `Called`/`SignUp`/`Result` message pattern and its slot-balancing are a good
   template for invites and requests. The group registry should be keyed by `NetUserId` and cleared on
   `RoundRestartCleanupEvent`. It also has to decide what happens to members who never readied: they reach the lobby
   picker only after round start.
9. **Mid-round joinable positions already half-exist**: a ship station's `StationJobs` slots, set from the station
   records console (`AdjustStationJobMsg`, `SetStationAdvertisementMsg`), appear in the picker's Crew tab through
   `ExtraShuttleInformationComponent`. Private or code-gated positions need a new gate, because slots are public and
   `IsJobAllowedEvent` has no station.
10. **Placement away from POIs and other outposts**: `PointOfInterestSystem.GetRandomPOICoord` is private and uses
    `nf14.worldgen.min_poi_distance` (400) with 10 retries, returning the last roll even if it is invalid. Outposts
    need their own placement check, and must also check grids and outposts spawned later.
11. **Testing**: `Build/development.toml` sets `lobbyenabled = false`, which skips the pre-round lobby; pre-round spawn
    options need the lobby turned on. Frontier arrivals is off, so there is no arrivals shuttle to lean on.
12. **Naming**: the WF module `Spawning` is the admin entity spawn menu and `Station` is the overflow tweak. Give spawn
    options their own module (e.g. `Outposts`, or `SpawnOptions`) and `WF`-prefixed ids.
