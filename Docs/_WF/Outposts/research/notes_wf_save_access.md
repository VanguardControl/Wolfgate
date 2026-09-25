# Outposts groundwork: saving, pricing, ownership, access (main @ 64f27cb43f)

Source: `C:/Users/jzo12/Documents/GitHub/Wolfgate/.claude/worktrees/ship-broadcast-pa-system-04bd45` (main-based,
read only). Planet branch facts come from `git show planet-cracking:...` (branch head c6bd838a99 "planets module").

## 1. Monolith Persistence (ported in 699b60f689, "Port Monolith#4743: Persistence: Atempt 2")

### What is stored per character
- `Content.Shared/Preferences/HumanoidCharacterProfile.cs` (Mono block ~L168-176):
  `List<string> Flags`, `List<PersistentProfileComponent> Components`, `List<PersistentProfileItem> Items`.
  Constructor takes `flags/components/items` (optional, last args). `WithPersistentData(flags, components, items)`
  (L466) returns a copy. They are part of `MemberwiseEquals` and `GetHashCode`.
- `Content.Shared/Preferences/PersistentProfileData.cs`: two `[DataDefinition, Serializable, NetSerializable]`
  records, both `{ string Data; bool Sticky; }`: `PersistentProfileComponent` and `PersistentProfileItem`.
  - Component `Data` = YAML of a one-entry `ComponentRegistry` (`SerializeComponent`, `alwaysWrite: true`).
  - Item `Data` = YAML from `MapLoaderSystem.TrySaveEntity(uid, StringWriter)` (full entity + children, a
    `FileCategory.Entity` file).
  - `Sticky`: kept after it is applied at spawn. Non-sticky entries are consumed (removed) once applied.
- `Content.Shared/_Mono/Persistence/PersistAtRoundEndComponent.cs`: `[DataField] bool Sticky; bool Once;`. No
  prototype in `Resources/Prototypes` uses it yet (grep empty); admins add it with the toolshed command.

### DB
- `Content.Server.Database/Model.cs`: `Profile.Flags` (`List<string>`, Postgres `text[]`, default empty list),
  `Profile.Components : List<ProfileComponent>`, `Profile.Items : List<ProfileItem>`; entity classes
  `ProfileComponent`/`ProfileItem` = `{ int Id; int ProfileId; Profile Profile; string Data; bool Sticky; }`.
  FK to `profile` with `onDelete: Cascade` (deleting the character slot deletes them).
- Migrations: `Content.Server.Database/Migrations/{Postgres,Sqlite}/202609211544xx_PersistentData.cs`. Tables
  `profile_component`, `profile_item`; `data` is `text` (no length cap).
- `Content.Server/Database/ServerDbBase.cs`:
  - `GetPlayerPreferencesAsync` (L55-66) `Include(...Components)`, `Include(...Items)` for every profile of the
    user at connect. All persistent data of all characters is loaded into memory at connect.
  - `ConvertProfile` (L311-318) maps rows to records; `ConvertProfiles` (L360-374) does `Components.Clear()` +
    `AddRange` and `Items.Clear()` + `AddRange`: every `SaveCharacterSlotAsync` deletes and reinserts all rows.
  - `SaveCharacterSlotAsync` (L100-143) is called on every `SetProfile`, including every bank balance change
    (`BankSystem.TryBankWithdraw/TryBankDeposit` call `_prefsManager.SetProfile(... WithBankBalance ...)`).

### Round trip and trust
- `IServerPreferencesManager.SetProfile(userId, slot, profile, bool authoritative = true)`. Client edits come in
  with `authoritative: false`; `ServerPreferencesManager.cs` L135-146 then overwrites `BankBalance` and
  `Flags/Components/Items` with the stored profile's values (or defaults for a new slot). The client can never
  write persistent data.
- The records are `NetSerializable` and ride inside the profile in `MsgPreferencesAndSettings` (server to client)
  and `MsgUpdateCharacter` (client to server). No size cap anywhere: no length checks in the persistence code, and
  the messages write a variable-length blob (`buffer.ReadVariableInt32()` length). Only `MsgConVars` in RT has a
  size warning.
- Editor: `Content.Client/Lobby/UI/HumanoidProfileEditor.xaml.cs` `RefreshSavedItems()` (L1286-1366) builds a
  "saved items" tab: for each `Profile.Items`, the client itself runs `_mapLoader.TryLoadEntity(reader,
  "persistent profile item", out entity)`, shows a `SpriteView` + name, and a yellow "(!)" tooltip for sticky
  (`humanoid-profile-editor-saved-item-sticky`) or round-end (`...-saved-item-round-end`). The tab is removed
  when empty. Import (L2497-2501) keeps `oldProfile` bank + persistent data ("no becoming God either").

### Server API (`Content.Server/_Mono/Persistence/PersistentProfileSystem.*.cs`, one partial system)
- Profile lookup: `TryGetProfile(uid, out session, out slot, out profile)` = session by attached entity +
  `TryGetCachedPreferences` + `SelectedCharacter` + `SelectedCharacterIndex`. So everything keys off the
  selected character slot of the player attached to `uid`.
- Save: `SaveProfile(session, slot, profile)` fire-and-forget `await _preferences.SetProfile(...)` with error log.
- Flags: `TryGetFlags`, `AddFlag(uid, key)`, `RemoveFlag`. Components: `AddComponent(uid, IComponent|string,
  sticky)`, `RemoveComponent`, `TryApplyComponent(uid, data, overwrite)`. Items: `AddItem(uid, EntityUid, sticky)`,
  `RemoveItem(uid, string data)`, `SaveRoundEndItems(uid, entities)`, `TrySerializeEntity(uid, out string)`,
  `TryDeserializeEntity(string, out uid)` (loaded into nullspace).
- Spawn: `Content.Server/Station/Systems/StationSpawningSystem.cs` L304-307 (Mono) calls
  `_persistence.LoadPersistentData(entity, profile, session)` after gear/bank: applies components, deserialises
  items and equips them (`GiveItem`: first clothing slot matching `ClothingComponent.Slots`, else hands/drop),
  then saves back only the sticky subset.
- Dedup is by exact `Data` string equality (same YAML = same item).

### Round end
- `PersistAtRoundEndSystem` subscribes `RoundEndedEvent` (declared in
  `Content.Server/Nyanotrasen/GameTicking/RoundEndedEvent.cs`, namespace `Content.Shared.GameTicking`; raised at the
  end of `GameTicker.EndRound`, `GameTicker.RoundFlow.cs` L652, after the round-end message).
- It queries `PersistAtRoundEndComponent`, walks `ParentUid` up to a session-attached entity (stops at a map), groups
  by player and calls `SaveRoundEndItems`; the component is removed before serialising if `Sticky || Once`. Only
  things physically on/in a connected player's body at round end are saved. Chat notice:
  `persistence-round-end-items-saved` (`Resources/Locale/en-US/_Mono/persistence.ftl`).
- Admin command: `Content.Server/_Mono/Persistence/PersistenceCommand.cs`, toolshed `persistence get|add|remove
  <Flag|Component|Item> <player> [key] [sticky]`, `AdminFlags.Admin`.
- Unrelated upstream `persistencesave` (`Content.Server/Administration/Commands/PersistenceSaveCommand.cs`) just
  `TrySaveMap`s a whole map to `game.map`.

### Sibling precedent: SafetyDepositBox (own DB tables, not the profile)
- `Content.Server/_WF/SafetyDepositBox/SafetyDepositBoxSystem.cs` + `Model.cs` `WayfarerSafetyDepositBox`
  (`BoxId Guid` unique, `OwnerUserId Guid`, `CharacterIndex int` = slot, `OwnerName`, `Nickname?`, `ProtoId`,
  `PurchaseDate`, `LastWithdrawn?`, `LastWithdrawnRoundId?`) and `WayfarerSafetyDepositBoxItem` (`EntityData`
  YAML text, `DepositDate`). API in `IServerDbManager` L402-409 (`PurchaseSafetyDepositBox`,
  `GetPlayerSafetyDepositBoxes(ownerUserId, characterIndex)`, `DepositSafetyDepositBoxItems`,
  `ClearSafetyDepositBoxItems(boxId, roundId)`, `DeleteStaleSafetyDepositBoxes(daysStale)`, ...).
- Items saved with `_loader.TrySaveEntity(item, writer)`, loaded with `_loader.TryLoadEntity(reader, ...)`.
- "Lost" detection: withdrawn in round X and not redeposited = `LastWithdrawnRoundId != current RoundId`.
- Ownership check: `box.OwnerUserId == userId && box.CharacterIndex == prefs.SelectedCharacterIndex`. Keyed by slot,
  so deleting a character and reusing the slot inherits the box. Migration `20260820125814_MonoSafetyDepositBoxes`.

## 2. Grid save/load

### MapLoaderSystem (RobustToolbox/Robust.Shared/EntitySerialization/Systems)
- Save (`MapLoaderSystem.Save.cs`):
  - `bool TrySaveGrid(EntityUid grid, TextWriter target, SerializationOptions? options = null)` (L183) and a
    `ResPath` overload (L174). Refuses non-grids and map-grids. Sets `opts.Category = FileCategory.Grid`.
  - `TrySaveEntity(EntityUid, TextWriter|ResPath, ...)` refuses maps and grids. `TrySaveMap(...)`,
    `TrySaveGeneric(...)`, `TrySaveAllEntities(...)`.
  - `(MappingDataNode Node, FileCategory Category) SerializeEntitiesRecursive(HashSet<EntityUid>, options)` (L27):
    in-memory node without writing; raises `BeforeSerializationEvent` / `AfterSerializationEvent`.
  - `SerializationOptions` (`Options.cs`): `MissingEntityBehaviour` (default `IncludeNullspace`),
    `EntityExceptionBehaviour` (default `Rethrow`), `ErrorOnOrphan`, `LogAutoInclude`, `ExpectPreInit`, `Category`.
  - Prototypes with `MapSavable: false` (mobs, borgs, mechs...) are skipped with everything inside them.
- Load (`MapLoaderSystem.Load.cs`):
  - `TryLoadGrid(MapId map, TextReader reader, string source, out Entity<MapGridComponent>? grid,
    DeserializationOptions? options = null, Vector2 offset = default, Angle rot = default)` (L297): merges onto an
    existing map (`MapLoadOptions.MergeMap`); fails and deletes if the file holds not exactly one grid.
  - `TryLoadGrid(MapId, ResPath, ...)`, `TryLoadGrid(TextReader|ResPath, out map, out grid, ...)` (new map),
    `TryLoadEntity(TextReader, source, out Entity<TransformComponent>?)` (nullspace), `TryLoadGeneric`,
    `Delete(LoadResult)`.
  - `DeserializationOptions`: `StoreYamlUids`, `InitializeMaps`, `PauseMaps`, `LogOrphanedGrids`,
    `LogInvalidEntities`, `AssignMapIds`.
  - `EntityDeserializer.cs` L357 reads a per-entity `mapInit` flag and `SetMapInitLifestage()` (L1015) sets
    `EntityLifeStage.MapInitialized` without raising `MapInitEvent`. A saved live grid comes back "initialised" but
    every map-init handler is skipped.

### WF in-memory ship copy (`Content.Server/_WF/Shipyard`)
- `ShipyardSystem.WolfgateTrader.cs` (partial of `Content.Server._NF.Shipyard.Systems.ShipyardSystem`):
  - `TrySaveShip(EntityUid grid, List<EntityUid> carry, out string? data)`: `StringWriter` + `TrySaveGrid` with
    `MissingEntityBehaviour.Ignore` (docked ship references to the station) and `LogAutoInclude = null`; temporarily
    flips `EntityPrototype.MapSavable = true` for the prototypes of `carry` entities, restored in `finally`.
  - `TryAddSavedShip(string data, out EntityUid? shuttleGrid)`: `TryLoadGrid(ShipyardMap, StringReader, "used ship",
    offset: new Vector2(500f + _shuttleIndex, 1f))`, then bumps `_shuttleIndex` by grid width + 1.
  - `StripForResale(grid)`: removes `ShuttleDeed`, `ShipOwnership`, `LinkedLifecycleGridParent`, `StationMember`,
    `Company`, `FTL`, `ShipGuestAccess`, `ShuttleConsoleJobSlots`, `PilotedShuttle`, `CrewedShuttle`, `FTLLock`,
    `ShipPaBroadcast`, `ShipAlert`. This is the list of grid-level owner state that a save carries.
  - `TryDockLoadedShuttle(station, grid)`: raises `ShipBoughtEvent`, `_shuttle.TryFTLDock` to the station's largest
    grid.
  - `TryGetShipDeedInfo`, `TryGetDeedShip`, `HasDeed`, `GetDeedName`, `GetShipAppraisal`, `GetPostSaleRateBill`,
    `GetShipResaleValue`, `TryHostConsole` (trader stands in for a console).
- `UsedShipMarketSystem.cs`: `UsedShipListing { Id, ShipName, DesignId, DesignName, SellerName, SaleValue, Price,
  SoldAt, AvailableAt, string Data, Announced }`. In memory only, cleared on `RoundRestartCleanupEvent`.
  - `OnShipSold(ref ShipSoldEvent)` only for stations with `UsedShipMarketComponent`; markup/delay from the station's
    `TraderUsedShipsComponent` (defaults `DefaultMarkup = 0.05f`, `DefaultRelistDelay = 1 min`).
  - `TryCapture`: `GetUnsavableAboard` (walks children; non-`MapSavable` prototypes except players, audio and
    `TimedDespawn`), `_docking.UndockDocks(shuttle)` first ("a dangling reference makes the whole load fail"),
    `TrySaveShip`, `Price = ceil(saleValue * (1 + markup))`.
  - `TryLoadListing`: `TryAddSavedShip` -> `StripForResale` -> `_reinit.ReinitLoadedShip` -> `TryDockLoadedShuttle`.
- `UsedShipReinitSystem.ReinitLoadedShip(grid)`: redoes the safe map-init work: `DeviceNetworkSystem
  .ReinitLoadedDevice`, `WiresSystem.ReinitLoadedWires`, shield emitter reset (deletes `ShipShieldComponent`
  bubbles, removes `ShipShieldedComponent`), `ShipPaSystem.ReindexSpeaker`, `PowerChargeSystem.ReinitLoadedCharge`,
  upgrade power baselines (`UpgradePowerDraw/Supplier/SupplyRamping`), vending cash slot, then
  `RemComp<NavMapComponent>` + `_navMap.EnsureGridNavMap`. Comment: a blanket map init "would refill every locker,
  vending machine and light". Partials `DeviceNetworkSystem.Wolfgate.cs`, `WiresSystem.Wolfgate.cs`,
  `PowerChargeSystem.Wolfgate.cs` expose those hooks.
- Buyer flow (`Content.Server/_WF/Traders/TraderUsedShipsSystem.cs` `OnBuy`, L217-296): check card has no deed and a
  session exists -> load before payment ("a hull that will not come out of the yard costs nothing") ->
  `TryTakePayment` (zone cash first, then `_bank.TryBankWithdraw(customer, remainder)`) -> `TryAssignDeed(shuttle,
  idCard, session, design, listing.ShipName)`; on deed failure deletes the hull and refunds with
  `TryBankDeposit(..., tax: false)`.
- Other placement precedent: `Content.Server/_WF/Administration/Systems/AdminVesselSpawnSystem.TrySpawnVessel`
  loads a vessel at `(mapId, position)` via `TryLoadGrid(mapId, path, out grid, offset: position)`, then
  `AddComponents(vessel.AddComponents)` + name; ownership through `TryAssignDeed`.
- No other Frontier/Mono ship-save-to-disk code exists on main (no ship-saving system in `_NF` or `_Mono`).

## 3. Shipyard (`Content.Server/_NF/Shipyard/Systems/ShipyardSystem*.cs`)
- `TryPurchaseShuttle(stationUid, ResPath shuttlePath, out shuttleEntityUid)` (`ShipyardSystem.cs` L132): loads onto
  `ShipyardMap` (private map created by `SetupShipyardIfNeeded`, deleted on round restart) and FTL-docks to the
  station's largest grid. The appraisal here is only logged.
- Console purchase `OnPurchaseMessage` (`ShipyardSystem.Consoles.cs` L92-449): ID or voucher in `TargetIdSlot`; refuses
  if the card already has `ShuttleDeedComponent` ("1 ship per captain"); console `AccessReader`; availability;
  needs `BankAccountComponent`; loads the ship first; `AttemptShipyardShuttlePurchaseEvent` (cancellable, deletes
  hull); hullmods from `ShipyardListingComponent.Hullmods`; payment `bank.Balance <= vessel.Price` refused, then
  `_bank.TryBankWithdraw(player, vessel.Price)`. The charge is the prototype `VesselPrototype.Price`, not an
  appraisal. Then company, station from a matching `GameMapPrototype`, `FTLLock`, ID `NewAccessLevels`, deeds on
  card and grid, console locks, `RegisterShipOwnership`, job title, records, `vessel.AddComponents`,
  `AddShipAccessToEntities`, `LinkedLifecycleGridParent`, `VesselComponent.VesselId`, tags, `ShipyardShuttlePurchaseEvent`.
- `TryAssignDeed(shuttleUid, idCard, owner, VesselPrototype? vessel, string? shipName = null)`
  (`Content.Server/_WF/Shipyard/ShipyardSystem.WolfgateDeed.cs` L38): all post-payment steps on an existing grid
  (needs `ShuttleComponent` on the grid and `IdCardComponent` on the card). Skips payment, vouchers, attempt event,
  ID access levels, job title.
- Sale: `OnSellMessage` (Consoles.cs L463-600): `ShipyardConsoleActionAttemptEvent` (WF, cancellable), needs deed
  on card, `FindDisableShipyardSaleObjects` (`ShipyardSellConditionComponent`), then `TrySellShuttle(station,
  shuttle, console, out bill)` (`ShipyardSystem.cs` L191): must be docked to the station's largest grid, no organics
  (`FoundOrganics`), deletes the shuttle's station, `CleanGrid` (teleports `PreserveOnSale` items to the console),
  `bill = AppraiseGrid(shuttle, LacksPreserveOnSaleComp)`, raises WF `ShipSoldEvent`, `QueueDel(shuttle)`.
  Back in `OnSellMessage`: removes card deed; if not voucher: `bill *= _baseSaleRate` (unless
  `IgnoreBaseSaleRate`), then for each `TaxAccounts` entry `tax = (int)(originalBill * coeff)` ->
  `_bank.TrySectorDeposit(account, tax, LedgerEntryType.ShipyardTax)`, `bill -= tax`; `_bank.TryBankDeposit(player,
  bill)` (taxed deposit, see 4).
- Sale rate: cvar `shuttle.shipyard_base_sell_rate` (`NFCCVars.ShipyardSellRate`, default `0.85f`, clamped 0-1).
  `CalculateShipResaleValue` = appraisal * rate - sum(taxes). `ShipyardConsoleComponent`
  (`Content.Shared/_NF/Shipyard/Components/SharedShipyardConsoleComponent.cs`): `TargetIdSlot`, `ShipyardChannel`,
  `SecretShipyardChannel`, `NewJobTitle`, `NewAccessLevels`, `CanTransferDeed`, `Dictionary<SectorBankAccount,float>
  TaxAccounts`, `IgnoreBaseSaleRate`.
- `ShipSoldEvent(EntityUid Shuttle, EntityUid Console, EntityUid Station, int Appraisal)` (`ShipyardWolfgateEvents.cs`,
  by-ref, raised just before `QueueDel` while the deed is still on the grid). Also `ShipyardConsoleActionAttemptEvent
  (Actor, ShipyardConsoleAction {Sell, UnassignDeed})`.
- `ShuttleDeedComponent` (`Content.Shared/_NF/Shipyard/Components/ShuttleDeedComponent.cs`, networked, access-limited to
  `SharedShipyardSystem`, `SharedShuttleRecordsSystem`, `SharedShuttleConsoleLockSystem`): `MaxNameLength = 30`,
  `MaxSuffixLength = 9`; fields `EntityUid? ShuttleUid`, `ShuttleName`, `ShuttleNameSuffix` (yaml `shuttleSuffix`),
  `ShuttleOwner` (display name string), `PurchasedWithVoucher`, `PurchaseVoucherUid`, `EntityUid? DeedHolder`. Lives on
  both the ID card and the grid.
- Rename: `OnRenameMessage` (L1064): trimmed, non-empty, `<= MaxNameLength` (hard-coded English popups), then
  `TryRenameShuttle(deedUid, deed, name, suffix)` which updates all deeds pointing at the ship, station name, grid
  name, shuttle record. It fails when the ship has no owning station (`_station.GetOwningStation`).
- Unassign: `OnUnassignDeedMessage` removes card deed with `ShipyardUnassignCooldownComponent` cooldown.
- Deed copy: `Content.Server/_Mono/DeedCopy/DeedCopySystem.cs`: using a deeded ID on another ID copies the deed
  (`ISerializationManager.CreateCopy`). This is how crew get owner-level ship access today.
- Pricing: `PricingSystem.AppraiseGrid(grid, predicate, afterPredicate)` (`Content.Server/Cargo/Systems/PricingSystem.cs`
  L530) sums `GetPriceConditional(child, true, predicate)` over the grid's direct transform children, recursing into
  containers. Tiles are not priced. Needs live entities; `GetEstimatedPrice(EntityPrototype)` exists but "Does not
  consider contained entities". `ShipyardTests.cs` L80-95 asserts `vessel.Price >= appraise * MinPriceMarkup`
  (`VesselPrototype.MinPriceMarkup` default 1.0, `MaxPriceMarkup` 2.5).
- `VesselPrototype` (`Content.Shared/_NF/Shipyard/Prototypes/VesselPrototype.cs`): `Name`, `limit`, `Description`,
  `Price`, `RequireCrew`, `Category`, `Group`, `Classes`, `Engines`, `Access`, `ShuttlePath`, `GuidebookPage`,
  `Purchasable`, `Tags`, `Company`, `AddComponents`, ...
- `ShipOwnershipComponent` (`Content.Shared/_NF/Shipyard/Components/ShipOwnershipComponent.cs`): `NetUserId
  OwnerUserId`, `LastStatusChangeTime`, `IsOwnerOnline`, `DeletionTimeoutSeconds = 3600`.
  `ShipOwnershipSystem.RegisterShipOwnership(grid, session)` only tracks online status; no deletion code in that
  file. It is the only grid-side owner identity that survives serialisation (a `NetUserId`, not a character).

## 4. Money (`Content.Server/_NF/Bank`)
- Player balance lives in the character profile: `HumanoidCharacterProfile.BankBalance` (int, `DefaultBalance =
  75000`), DB `Profile.BankBalance`. `BankAccountComponent.Balance` on the mob is a mirror refreshed on
  attach/prefs load.
- `TryBankWithdraw(EntityUid mob, int amount)`: needs `BankAccountComponent`, attached session, cached prefs; refuses
  `IronmanComponent` (Mono); calls the session overload and updates `bank.Balance`.
- `TryBankWithdraw(session, prefs, profile, amount, out newBalance, bool spendLongTerm = false)`: works with no mob
  (lobby-safe). `spendLongTerm` first spends MonoCoins (per-player `Preference.MonoCoins`, `MonoCoinsManager`).
  Writes via `_prefsManager.SetProfile(userId, index, profile.WithBankBalance(...))`, raises `BalanceChangedEvent`.
  Used by the station spawner and the safety deposit box with `spendLongTerm: true`.
- `TryBankDeposit(mob, amount, bool tax = true)`: gated by cvar `mono.deposit.enabled`; `GetTaxedDepositAmount`
  (`SharedBankSystem.cs` L51) diverts the part above `mono.deposit.threshold` (default 2,000,000) into MonoCoins.
  `TryBankDeposit(session, prefs, profile, amount, out newBalance)` is the untaxed profile write.
- Sector accounts: `SectorBankComponent.Accounts: Dictionary<SectorBankAccount, SectorBankAccountInfo{Balance,
  IncreasePerSecond}>`; enum `SectorBankAccount { Invalid, Frontier, Nfsd, Medical, Mieyo, BlackMarket }`;
  `TrySectorWithdraw/TrySectorDeposit(account, amount, LedgerEntryType)`, `TryGetBalance(account, out)`. Ledger in
  `BankSystem.Ledger.cs`, cleared on round restart.

## 5. Ship previewer (`Content.Client/_WF/ShipPreview`, client only)
- `ShipPreviewSystem`: `Acquire()` -> `ShipPreviewHandle` (own client map, negative id), `Release(handle)`,
  `GetCurrent(handle)`, `TryLoad(handle, VesselPrototype, out ShipPreviewGrid)`, `TryLoad(handle, ResPath path,
  string? name, out ShipPreviewGrid)`, `Clear(handle)`. Loads with `_loader.TryLoadGrid(handle.MapId, path, out
  grid, new DeserializationOptions { InitializeMaps = false, PauseMaps = true })`, parks it at origin unrotated,
  counts tiles. `ShipPreviewGrid(Grid, LocalBounds, SizeInTiles, TileCount)`.
- `ShipPreviewControl.SetVessel(VesselPrototype?)` (loads next frame), `FitToGrid()`, `Release()`, `TileCount`,
  `GridSize`, `HasPreview`, event `PreviewUpdated`. `UI/ShipPreviewWindow.xaml(.cs)`. Command `wf_shippreview`.
- Only file paths are supported: no `TextReader`/string overload, and grids must be under `Resources/SharedMaps`
  (client builds drop `Resources/Maps`). Users: `Content.Client/_NF/Shipyard/BUI/ShipyardConsoleBoundUserInterface.cs`
  and `Content.Client/_WF/Administration/UI/VesselSpawn/VesselSpawnWindow.xaml`.
- Older Mono server-side preview still exists: `Content.Server/_Mono/Shipyard/ShipyardPreviewSystem.cs` (preview map
  + `PreviewObserver` visiting mind).

## 6. Access today
- `AccessReaderComponent` (`Content.Shared/Access/Components/AccessReaderComponent.cs`): `Enabled`, `DenyTags`,
  `access` (`List<HashSet<ProtoId<AccessLevelPrototype>>> AccessLists`), `HashSet<StationRecordKey> AccessKeys`
  (per-person access by station record, round-local), `ContainerAccessProvider`, access log. `AccessReaderSystem
  .IsAllowed(user, target)`, `SetAccesses(uid, reader, list)`, `FindAccessTags`, `FindStationRecordKeys`.
- ID access: `AccessComponent.Tags` on the card. A bought ship gives the card `ShipyardConsoleComponent.NewAccessLevels`
  (`computers_shipyard.yml`: `[Captain]`, faction consoles `[Captain, Security, Brig]`). A generic Captain tag opens
  every ship's Captain doors, which is why Mono added deed access.
- Mono ship (deed) access: `Content.Shared/_Mono/Shipyard/ShipAccessReaderComponent.cs` (`Enabled` default false,
  `DeniedMessage = "ship-access-denied"`, `ShowDeniedPopup`) + `ShipAccessReaderSystem` (shared) on
  `BeforeDoorOpenedEvent`, `StorageOpenAttemptEvent` (locked only), `LockToggleAttemptEvent`. `HasShipAccess`
  order: admin ghost, AI core -> target not on grid or grid lacks `ShuttleDeedComponent` = allow -> grid
  `CompanyComponent` in hard-coded `"USSP" | "Rogue" | "TSF"` and card `CompanyName` matches -> any held/ID-slot card
  (or PDA's card) with a deed whose `ShuttleUid` matches -> vouchers -> `ShipGuestAccessComponent.GuestIdCards` /
  `GuestCyborgs` (`HashSet<EntityUid>`).
  - Added by `ShipyardSystem.AddShipAccessToEntities(grid)` (private, `ShipyardSystem.cs` L423) to every
    `DoorComponent` and `EntityStorageComponent` in the grid AABB; disabled until the owner turns it on.
- Console verbs (`Content.Server/Shuttles/Systems/ShuttleConsoleLockSystem.cs`, 1011 lines, on
  `ShuttleConsoleLockComponent{Locked, ShuttleId (string of the EntityUid)}`, grid `ShipGridLockComponent`):
  lock/unlock console with deeded ID, "reset guest access", "guest access" (adds the user's cards/borg to
  `ShipGuestAccessComponent`), "lock/unlock ship" (`TryToggleShipAccess` flips `Enabled` on every
  `ShipAccessReaderComponent` among the grid's direct children; deed holders only). This is the whole per-ship
  access UI today: verbs only, no per-door configuration, no codes.
- `CompanyAccessReaderComponent` (`Content.Shared/_Mono/Company/CompanyAccessReader.cs`): `requiredCompanies`,
  `Inverted`, `popupMessage`; gates `ActivatableUIOpenAttemptEvent` only (consoles/UIs, not doors) on the user's
  `CompanyComponent.CompanyName` (`ProtoId<CompanyPrototype>`, default "None"). Faction ships get
  `CompanyComponent` on the grid from the card's `CompanyName` at purchase.
- Access configurator: upstream `AccessOverriderComponent` (`PrivilegedIdSlot`, `AccessLevels`, `DoAfter`) edits a
  door's `AccessReader` tags using a privileged card; `Resources/Prototypes/_NF/Entities/Objects/Tools/access_configurator.yml`
  adds `AccessConfiguratorAntag`. Tag-based only; nothing per-player.
- `Content.Server/_Mono/Access/AccessGrantableSystem.cs`: using an ID on an `AccessGrantable` entity unions the card's
  tags into the entity's `AccessComponent` (agent-ID style).
- Right-click verbs on doors: none (no `GetVerbsEvent` in `Content.Shared/Doors` or `Content.Server/Doors`); the only
  lock verb is `LockSystem.AddToggleLockVerb` on `LockComponent`.
- No Mono "crew manifest" for ships beyond `ShuttleRecordsSystem` (name, suffix, owner name, purchase price per ship).
- `Content.Server/_WF/Station`: only `StationJobsSystem.Overflow.cs` (round-start overflow gives Vagrant or leaves the
  player in the lobby). `Content.Server/_WF/Roles`: custom job titles, stored per role in `RoleLoadout.CustomJobTitle`
  (profile loadouts), rules in `Content.Shared/_WF/Roles/CustomJobTitleRules.cs`, applied on `PlayerSpawnCompleteEvent`
  to ID card, mind (`CustomJobTitleComponent`) and station records. Nothing access-related.
- Grid claiming: `Content.Server/_Mono/GridClaimer` - `GridClaimerComponent` (a banner, `banners.yml`) with verbs
  "claim"/"unclaim" on a `ClaimableGridComponent` grid (debris, bluespace events, monolithic POIs); claiming removes
  `OwnedDebrisComponent` so the grid stops despawning. Records claimer uids only, no owner.

## 7. Grid anchoring
- `ShuttleSystem.Enable/Disable/Toggle(..., bool force = false)` (`Content.Server/Shuttles/Systems/ShuttleSystem.cs`
  L180-225): `Disable` = `SetBodyType(Static)`, `BodyStatus.OnGround`, fixed rotation; `Enable` = Dynamic/InAir.
  All three bail when the grid has `PreventGridAnchorChangesComponent` unless `force`. `OnShuttleShutdown` calls
  `Disable`, so removing `ShuttleComponent` also makes the grid static.
- `Content.Server/_NF/Shuttles/Components`: `ForceAnchorComponent`, `ForceAnchorPostFTLComponent`,
  `PreventGridAnchorChangesComponent` (all marker comps). `ForceAnchorSystem`: on `MapInitEvent`
  `_shuttle.Disable(ent, force: true)` + `EnsureComp<PreventGridAnchorChanges>`; same after `FTLCompletedEvent` for
  PostFTL; cancels `ConsoleFTLAttemptEvent` with `shuttle-console-force-anchored`. `ShuttleSystem.Impact.cs` L175
  treats `ForceAnchor` grids as impact-protected. POIs get it from `Resources/Prototypes/_NF/PointsOfInterest/base.yml`
  (`BasePOI.addComponents: ForceAnchor`); monolithic grids also get `PreventPilot` + `ClaimableGrid`.
- `PreventPilotComponent` (`Content.Shared/Shuttles/Components`) blocks manual piloting (`MoverController.cs` L866) and
  FTL (`ShuttleSystem.FasterThanLight.cs` L263).
- Anchor dampening mode (`_NF/Shuttles/Systems/ShuttleSystem.cs`): `InertiaDampeningMode.Anchor` just sets
  `BodyModifier = 2.5`; a static grid reports `InertiaDampeningMode.Station` and refuses changes. Not an anchor.
- `FTLLockComponent` is not immobilisation: it only decides whether docked ships FTL along.
- Cleanup: `Content.Server/_Mono/Cleanup/GridCleanupSystem` deletes grids with IFF label hidden, no nearby players, no
  powered APC, appraisal under cvar max; skips map-grids, grids parented to a grid, `CleanupImmuneComponent`.
- Planet branch (`planet-cracking`): landed hulls stay dynamic. `CEZLevelsSystem.GroundFriction.cs`: "Nothing here
  parks a grid or otherwise changes its body type"; ground contact applies a Coulomb skid `GroundSkidDecel = 20f`
  m/s² plus drag `GroundDragModifier = 20f` scaled by footprint grip, so only absurd thrust drags a hull. Static
  grids count as a `CEZGridSyncSystem.IsStaticAnchor` for the z-network. `ForceAnchorComponent` is already honoured
  there: liftoff refused (`wf-liftoff-force-anchored`, `Flight/CEZLevelsSystem.WFLiftoff.cs`), and skipped by
  `WFPlanetDragSystem`, `WFOrbitDecaySystem`, `WFGravityWellSystem`. Carcinoma tendrils pin hulls by
  `SetBodyType(Static)` and restore a saved `PreviousBodyType`.

## Implications for Outposts
- Saving: `ShipyardSystem.TrySaveShip` + `TryAddSavedShip` already give string-YAML round trips of a whole grid;
  loading onto the planet map needs only `TryLoadGrid(mapId, reader, src, out grid, offset, rot)` with offset/rotation
  computed from the console's saved local position. Run `UsedShipReinitSystem.ReinitLoadedShip` after every load
  (its list will need outpost additions such as planet/atmos state), and undock first when saving.
- Do not store outpost YAML in `PersistentProfileItem`: it is loaded for every character at connect, sent to the
  client in every prefs message, echoed on every editor save, and rewritten (delete + insert) on every bank change.
  Use dedicated tables like `WayfarerSafetyDepositBox`, name the migration `Wolfgate<Change>`, and send the lobby
  only metadata plus a preview on request.
- Character linkage: nothing stable identifies a character except `(UserId, slot)` or the DB `Profile.Id` (not on
  `HumanoidCharacterProfile`). SafetyDepositBox uses the slot and inherits boxes across slot reuse; outposts should
  key on `profile.Id` with cascade delete, or at least clear rows when a slot is deleted.
- Pricing: `AppraiseGrid` needs a live grid and ignores tiles. The "+50%" load price must be the appraisal stored at
  save time (or at load, then refunded/charged); `ShipyardTests` already uses the appraisal-times-markup idea.
  Selling reuses rate 0.85 and `TaxAccounts`; `ShipSoldEvent` is the hook. Charge with the session overload of
  `TryBankWithdraw` (works from lobby/spawn; `spendLongTerm` optional); refund with `TryBankDeposit(..., tax: false)`.
- Load-then-pay ordering (used-ship trader) avoids refunds when a load fails.
- Ownership: deeds are per ID card and hold `EntityUid`s; `ShuttleConsoleLockComponent.ShuttleId` is a stringified
  uid; `ShipGuestAccessComponent` stores card uids. None survive a save/load. A persistent owner/access list must
  store `NetUserId` + character key (like `ShipOwnershipComponent.OwnerUserId`) and be re-bound to cards at spawn.
  Call `TryAssignDeed` on load so today's deed-based systems (console lock, ship access, records, rename) work,
  after `StripForResale`-style cleanup of stale owner components.
- Access overhaul has only verbs to build on (guest access, ship-wide toggle). Per-door configuration, codes, and a
  per-player list are new; `AccessReaderComponent.AccessKeys` and `ShipAccessReaderComponent` are extension points.
  Note `ShipAccessReaderComponent.Enabled` defaults false and the company exception is hard-coded strings.
- Anchoring: `ForceAnchorComponent` + `PreventGridAnchorChangesComponent` + `ShuttleSystem.Disable(force: true)` do
  it, and the planet branch already treats `ForceAnchor` as "cannot lift off/drift". But `ForceAnchorSystem` acts on
  `MapInitEvent`, which a loaded save never raises; apply it explicitly on load and remove it (with
  `Enable(force: true)`) when the outpost console is destroyed. Add `CleanupImmuneComponent` so grid cleanup never
  eats an outpost.
- Renaming via `TryRenameShuttle` fails for grids without an owning station; outposts need their own rename path
  (limit reuse: `ShuttleDeedComponent.MaxNameLength = 30`).
- Previewer needs a new `TryLoad(handle, TextReader/string, name)` overload (the MapLoader API already has one) and a
  server-to-client transfer of the saved YAML for the save tab and character editor.
- `PersistentProfileSystem` flags are a cheap place for per-character booleans (e.g. "has outpost save"), but its
  writes go through the full profile save.
