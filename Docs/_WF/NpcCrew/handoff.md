# NpcCrew handoff

For whoever (or whatever) picks up the NPC crew work. Everything below is true as of the `npc-crew` branch head.
Paste this whole file as the opening brief; it is written for an AI agent working in a fresh checkout.

## Where things are

- Repository `VanguardControl/Wolfgate`, branch `npc-crew` (an older copy of the same commits sits on
  `clanker/relaxed-lovelace-q1366b`; ignore it). Never commit to `main`. Open PRs against
  `VanguardControl/Wolfgate` and fill in `.github/PULL_REQUEST_TEMPLATE.md` with a `:cl:` changelog.
- `AGENTS.md` at the repo root is the rulebook. Read it first, every session. The parts that bite:
  - All new code lives in module folders: `Content.{Server,Shared,Client}/_WF/NpcCrew`, tests in
    `Content.IntegrationTests/Tests/_WF/NpcCrew`, prototypes in `Resources/Prototypes/_WF/NpcCrew`, strings in
    `Resources/Locale/en-US/_WF/NpcCrew`. Cvars for Wolfgate modules go in `Content.Shared/_WF/CCVar/` (see
    `NpcCrewCVars.cs`).
  - Any edit to a file outside `_WF` needs a `// WOLFGATE(NpcCrew): reason` marker (or `START`/`END` block). Then run
    `python3 Tools/_WF/Ci/modules.py --write` and commit the regenerated READMEs. CI runs `--check` and
    `--pr-check`.
  - No license headers. `[Dependency] private X _x = default!;` without `readonly`. One-line `/// <summary>` on
    types and public members. Every player-facing string through Fluent. Prototype IDs start with `WF`.
  - Never edit `RobustToolbox`. Client and Shared code is sandbox-checked at client load (no `BinaryWriter`,
    `Process`, collection expressions into `List<T>`, `string += char`).
  - Only one system may subscribe a given (component, event) pair. Grep Content.Server and Content.Shared for an
    existing `SubscribeLocalEvent<ThatComponent, ThatEvent>` before adding a directed subscription on an upstream
    component; a duplicate compiles and crashes the server at startup. Broadcast subscriptions and subscriptions on
    our own `WF*` components are always safe.
- Design docs: `Docs/_WF/NpcCrew/design.md` (this module's brief, with an "as built" note per section) and
  `Docs/_WF/Encounters/design.md` (the Random Encounters System that consumes crews). Module overview:
  `Content.Server/_WF/NpcCrew/README.md` (the generated section lists every file).

## Toolchain

```
# .NET 10 SDK (the environment's network must allow builds.dotnet.microsoft.com and download.visualstudio.microsoft.com)
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir $HOME/.dotnet
export PATH=$HOME/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1
git submodule update --init --recursive --depth 1 RobustToolbox

# Build everything the tests need (about 2.5 min); only errors shown
dotnet build Content.IntegrationTests/Content.IntegrationTests.csproj -c Debug -v q -nologo 2>&1 | grep -E "error CS|error RA|Error\(s\)" | sort -u

# The module's tests (13 cases; runtime varies by host)
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Debug --no-build --filter "FullyQualifiedName~WFCrewTest" --logger "console;verbosity=detailed" -nologo 2>&1 | grep -vE "warning RA|warning CS" > /tmp/wfcrew-tests.log
grep -E "^\s*(Passed|Failed) |Error Message|Passed!|Failed!" -A3 /tmp/wfcrew-tests.log | head -40

# Prototype linter (about 4 min, builds Release); must end with "No errors found"
dotnet run --project Content.YAMLLinter -c Release -v q -nologo 2>&1 | grep -vE "warning|^\s*$" | tail -15

# Module docs; the only acceptable complaint is the pre-existing fork_point one in a shallow clone
python3 Tools/_WF/Ci/modules.py --write
```

Never run two dotnet builds at once. `Content.Server` treats nullable warnings as errors. Say plainly in every
report what you built, what you ran and the exact counts; never call something working without running it.

## What is built (13 crew tests green; prototype linter clean at the previous handoff)

One idea: every crew NPC runs one HTN root, `WFCrewCompound` (`Resources/Prototypes/_WF/NpcCrew/htn.yml`), with
branches in priority order: **fight** (gated by `WFCrewMayFightPrecondition`, starts with `WFDrawWeaponOperator`,
then the upstream `RangedCombatCompound`/`MeleeCombatCompound`), then one branch per **duty** selected by the
`WFCrewDuty` blackboard key (`Pilot` → `WFCrewPilotCompound`, `Guard` → `WFCrewGuardCompound`), then **idle**. Every
duty branch starts with `WFHolsterWeaponOperator`. The planner replans every 0.45 s and swaps to an earlier branch
as soon as it is valid, so a fight interrupts a duty and the duty resumes afterwards; nothing else switches state.

| Piece | Files | What it does |
|---|---|---|
| Crew core | `Systems/WFCrewSystem.cs`, `Components/WFCrewComponent.cs`, `WFCrewEvents.cs`, `Content.Shared/_WF/NpcCrew/*` | `WFCrewComponent` (Role, Duty, Engagement, Group, Post, PostRange) mirrored to blackboard keys `WFCrewDuty`, `WFCrewPost`, `WFCrewPostRange`; role title put before the random name; `WFCrewMemberDownEvent` on crit/death. API: `SpawnCrewman(role, post, group)`, `SpawnCrew(plan, group)`, `ClearGroup`, `SetDuty`, `SetPost`, `SetEngagement`, `Apply`. `WFCrewRolePrototype` (`wfCrewRole`: mob, duty, engagement, title, order, extra `components`). |
| Weapons | `Systems/WFCrewWeaponSystem.cs`, `HTN/WFDrawWeaponOperator.cs`, `HTN/WFHolsterWeaponOperator.cs`, `Components/WFCrewWeaponComponent.cs` | Draws the best weapon from equipment slots (back, suitstorage, belt, pockets; longarm > sidearm > melee) into any free hand and selects it; holsters it back into the slot it came from. Weapons never live in bags. No reload yet. |
| Engagement | `HTN/WFCrewMayFightPrecondition.cs` | `OnSight` crew may always fight (the combat compounds still need a target); `WhenAttacked` crew only while `NPCRetaliationComponent.AttackMemories` holds an unexpired entry. The crew core also feeds upstream retaliation from `BeforeDamageChangedEvent`, before Wolfmed loses the origin. |
| Planner | `Systems/WFCrewPlannerSystem.cs` | `Plan(grid, deckhands)`: `WFCrewSpawnPoint` markers win; else a Pilot beside the helm, a RadioOperator beside that, a Deckhand on the first free tile inside each airlock (dock outward normal = local rotation applied to (0,-1)), the rest on the most open deck tiles kept 3 apart. Returns `WFCrewPost(Coordinates, Role, Kind)`. |
| Pilot duty | `Systems/WFPilotDutySystem.cs`, `Components/WFPilotDutyComponent.cs`, `HTN/WFPickHelmOperator.cs`, `HTN/WFTakeHelmOperator.cs`, `Content.Shared/_WF/NpcCrew/WFPilotOrder.cs` | Walks to the assigned or nearest powered `ShuttleConsoleComponent`, `EnsureComp<PilotComponent>` + `ShuttleConsoleSystem.AddPilot`, then flies with Mono's `ShipSteeringSystem.Steer(mob, coords)` (`Content.Server/_Mono/NPC/HTN/`). Orders `Hold`, `GoTo(waypoints)`, `Loiter(center, radius)`, `Follow(grid, range)`; waypoints advance in `Update`, not in the HTN, so unattended ships keep flying. The helm is kept while the HTN sleeps (no player within 32 tiles) and released when the pilot dies, is displaced, the console loses power, or the HTN wakes into a plan without the helm. Upstream does not drop pilots on power loss; this system checks power itself. Hold counts as arrived under 0.5 m/s. Events: `WFHelmTakenEvent`, `WFHelmReleasedEvent`, `WFPilotOrdersChangedEvent`, `WFPilotOrdersCompletedEvent`. |
| Docking | `Systems/WFPilotDutySystem.Docking.cs` | `Dock(target)` / `Undock()`. Phases Plan → Approach (standoff 40 m along the target port's normal, avoidance on) → Settle (brake) → Creep (avoidance off, heading held, 1.5 m/s) → `DockingSystem.Dock` the tick `CanDock` holds. `TryPlanDock(grid, target, standoff, out WFDockPlan)` tries every dock pair from `DockingSystem.GetDockingConfig`, nearest standoff first. Abort on timeout or collision, three attempts, then hold and `WFPilotDockFailedEvent`; `WFPilotDockedEvent` on success. Cvar `wf.crew.dock_ftl_fallback` (default false) uses the docking config plus `FTLDock` instead of giving up. **Hand-flown docking is untested in game: the test decks have no thrusters.** |
| Radio officer | `Systems/WFRadioOperatorSystem.cs`, `Components/WFRadioOperatorComponent.cs` | Event-driven, no HTN. Shortband (`Traffic`, 1500 m, needs `TelecomExempt`, which the role adds): docking/undocking once per grid pair, "jumping" while the drive spools (polled; `FTLStartedEvent` fires already in FTL space), "arriving", "on station", "docking aborted". Broadband (`Common`): one mayday per episode, "boarded", "Captain is down", "Helm is down", all-clear after 120 s quiet. Hostile acts: a crewman of the group hurt by an outsider (`BeforeDamageChangedEvent` on `WFCrewComponent`, because Wolfmed routes body damage through parts and the body's `DamageChangedEvent` has no origin) and a hostile mob aboard (`NpcFactionSystem.GetNearbyHostiles`, polled). Cooldown per line. `Sent` keeps the last 20 transmissions for VV and tests. `SetCallsign`. |
| Command | `Commands/WFCrewCommand.cs` (`wf_crew`, `AdminFlags.Spawn`) | `plan <grid|here> [deckhands]`, `spawn <grid|here> [group] [deckhands]`, `spawnrole <role> [group]`, `list [group]`, `clear <group>`, `duty <mob> <duty>`, `orders <mob> hold|goto x y ...|loiter x y r|follow <grid|here>|dock <grid|here>|undock`, `callsign <mob> <text>`. |
| Prototypes | `roles.yml`, `mobs.yml`, `gear.yml`, `markers.yml`, `ai_factions.yml`, `htn.yml` | Roles `WFCrewDeckhand`, `WFCrewMarine` (OnSight), `WFCrewPilot`, `WFCrewRadioOperator`, `WFCrewCaptain` (WhenAttacked; the captain still works Guard). Mobs `WFMobCrewBase` (parent `[BaseMobHuman, MobPrying]`, faction `WFCrew`, `NPCRetaliation`, HTN root), `WFMobCrewDeckhand`, `WFMobCrewMarine`, `WFMobCrewOfficer` (carries `WFPilotDuty`). Faction `WFCrew` is hostile to SimpleHostile, Zombie, Xeno and nobody else. Markers `WFCrewSpawnPoint<Role>`. |
| Tests | `Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.cs` | `DeckhandDrawsForHostileAndHolstersAfter`, `PlannerPlansHelmRadioDockAndDeck`, `PilotTakesHelmAndReleasesOnDeath`, `DockPlanPutsStandoffOutsideTheTargetDock`, `DockFallsBackToFtlDockWhenAllowed`, `RadioOperatorReportsDocking`, `RadioOperatorMaydayThenCaptainDownThenSilence`. New regression coverage: pilot retaliation, alert scope/decay for named and empty groups, distant recruitment, departure cleanup and preservation of personal retaliation (13 cases total). Helpers: `CreateDeck(origin, size, gravity)`, `WaitUntil`, `Describe` (dumps awake state, plan, target, hostiles, held item, factions, nearby mobs). Test decks must sit within ~10 m of the origin: the `InteractionTest` player is there and NPCs sleep with no player within 32 tiles. The hostile test mob is `WFTestHostileMob` (faction `SimpleHostile`); test factions can't be declared in `[TestPrototypes]` because the faction system caches its table before they load. |

## Known gaps and bugs, in the order to fix them

1. **Retaliation fixed and verified (2026-10-02).** `WFCrewSystem` subscribes to `BeforeDamageChangedEvent` on
   `NPCRetaliationComponent`, limited to crew, and calls upstream `TryRetaliate`. This reuses faction checks,
   attack memory and expiry cleanup instead of adding a second memory store. The radio keeps its existing
   subscription on `WFCrewComponent`. `PilotRetaliatesAfterBodyDamage` checks that a nearby hostile does not
   interrupt piloting, a body hit releases the helm and draws, and removing the attacker restores duty.
   Debug integration build: 0 errors; all 8 `WFCrewTest` cases passed. Module `--write`, `--check` and
   `--pr-check origin/main` passed. No prototype or Client/Shared changes in this fix; no new live-client
   or thrustered-docking validation. Shared crew alerts are now implemented below.
2. **Crew alert implemented and verified (2026-10-02).** `WFCrewAlertSystem` shares live onboard targets among
   eligible on-sight members of one `(grid, group)`, with a `ShareAlerts` opt-out. Both normal and aggro vision
   expand across the grid; combat keeps its existing line-of-sight rules. Sixty seconds without a target clears
   the alert; departed, downed or player-controlled members are cleaned up on the next poll. Added hostility and
   original vision overrides are tracked separately, preserving pre-existing hostility and personal retaliation.
   Broadcast `WFCrewAlertEvent` / `WFCrewAlertClearedEvent` carry grid and group. Radio remains independent.
   Debug integration build: 0 errors, 1330 warnings; all 13 crew test cases passed. Module generation,
   `--check` and `--pr-check origin/main` passed. An initial run had 11/12 pass with a room-echo
   `SharedAudioSystem.SetAuxiliary` assertion; the new combat test disables client room echo and restores it
   afterward. No prototype or Client/Shared changes; linter and live-client checks were not rerun.
   Next implementation task: access doors below.
3. **Access doors** (design.md section 6): a small marked edit so NPCs with access open doors instead of prying.
   `PathfindingSystem.GetFlags` sets the existing `PathFlags.Access` from a `NavAccess` blackboard key;
   `GetTileCost` treats access doors as plain doors for that flag; `NPCSteeringSystem.TryHandleFlags` uses
   `InteractionActivate` when `AccessReaderSystem.IsAllowed(npc, door)` and only pries otherwise. Crew need
   `AccessComponent` tags (and, on ShipAccess-locked ships, a crew record in the allow list).
4. **Hull hits as a hostile act** for the radio officer and the alert: the only projectile hit event is already
   subscribed by Mono's `SpaceArtillerySystem`; find another hook (damage on anchored entities of the grid with a
   `ShipWeaponProjectile` origin, or a broadcast event if one exists).
5. **Captain** (design.md "NPCs to start with" 4): a radio officer whose component also changes the pilot's orders on
   alert (`Hold` if `HeaveTo`) and restores them on all-clear.
6. **Reload**: `WFReloadOperator`, magazines from pockets or belt via `InteractUsing`; empty gun → holster, next weapon,
   melee. Upstream left this as a TODO in `Resources/Prototypes/NPCs/Combat/gun.yml`.
7. **Crew Setup admin window** (design.md section 8), built like `Content.Client/_WF/Administration/UI/Ert/` and
   `VesselSpawn/`; the planner and the orders API are its backend. Then the Gunner duty, then RES integration.
8. **In-game checks nobody has done**: start a real client and grep its log for `Sandbox violation` (Shared code
   added: a prototype class, two enums, a cvar file); fly a thrustered ship with `wf_crew spawnrole pilot` then
   `wf_crew orders <mob> dock <grid>` and tune `DockStandoff`/`DockCreepSpeed` per hull.

## Traps that each cost a build cycle

- Robust requires every subscription one system makes to the same event type to use identical ordering constraints
  (`after:`/`before:`), or the server fails to start with "uses different ordering constraints".
- `SharedHandsSystem.TrySelectEmptyHand` is always false in this fork (it asks `IsHolding` about a null entity). Use
  `TryPickupAnyHand` then `TrySelect(uid, item)`.
- `MapInitEvent`, `MobStateChangedEvent`, `DamageChangedEvent` are by-value events: `(EntityUid, TComp, TEvent)`
  handlers. Check each event for `[ByRefEvent]`; our own events are all `[ByRefEvent] readonly record struct`, raised
  with `RaiseLocalEvent(uid, ref ev, true)`.
- Local functions can't see a null check on a captured variable; copy to a non-nullable local first (nullable
  warnings are errors).
- `LocalizedCommands.Loc` is an instance member; command helpers that use it can't be static.
- `ShipSteeringSystem.Stop` only removes the steering component; `Status` refreshes only when the ship asks for
  input, so set `Status = Moving` after re-steering. `FinishOnCollide` defaults to true. `InRangeRotation` applies
  only once in range; hold a heading during an approach with `AlwaysFaceTarget` plus a `TargetRotation` offset.
- `DockingSystem.GetDocks` clears a set `UndockDocks` still iterates; don't call it from an undock handler.
- A test deck is a bare grid; add `ShuttleComponent` for steering, `GravitySystem.EnableGravity` for walking, and a
  helm prototype with `ApcPowerReceiver needsPower: false` to count as powered without an APC.
- Blackboard YAML: `Key: !type:Bool` on one line, the value on the next (see `mobs.yml`).

## Upstream API cheat sheet (verified in this tree)

- `ShuttleConsoleSystem.AddPilot(EntityUid console, EntityUid mob, ShuttleConsoleComponent)` needs `PilotComponent`
  on the mob first; `RemovePilot(EntityUid mob)`; a pilot cannot move and is dropped each tick it can't interact.
- `ShipSteeringSystem.Steer(Entity<ShipSteererComponent?> mob, EntityCoordinates)` → component or null (grid needs
  `ShuttleComponent`); fields `Coordinates`, `Status` (`Moving`/`InRange`), `Mode` (`GoToRange`/`Orbit`/`OrbitCW`),
  `Range`, `InRangeMaxSpeed`, `InRangeRotation`, `AvoidCollisions`, `FinishOnCollide`, `AlwaysFaceTarget`,
  `TargetRotation`. Input reaches the ship through `GetShuttleInputsEvent`; `PilotComponent` and the steerer coexist.
- `DockingSystem.GetDockingConfig(shuttle, targetGrid, priorityTag?, DockType)`, `CanDock(a, b)` (1.2 m, 15°),
  `Dock(a, b)`, `Undock(dock)`, `CanUndock`. A dock's outward normal is its rotation applied to (0, -1).
- `RadioSystem.SendRadioMessage(source, text, ProtoId<RadioChannelPrototype>, radioSource, ...)`; channels `Traffic`
  ("Shortband", 1500 m), `Common` ("Broadband"), `Nfsd` (TSF), `Ussp`. Identical text already in flight is dropped.
- `NpcFactionSystem.GetNearbyHostiles(mob, range)`, `AggroEntity`, `IsEntityFriendly`; factions in
  `Resources/Prototypes/{,_NF/,_Mono/}ai_factions.yml`. NPC vision is the `VisionRadius` blackboard key (10 tiles).
- `NPCSystem.SetBlackboard(uid, key, value, htn?)`, `NPCSystem.IsAwake(uid, htn)`, `HTNSystem.Replan(htn)`;
  `HTNComponent.Plan?.CurrentOperator`. `MoveToOperator` keys: `targetKey` (EntityCoordinates), `rangeKey`,
  `removeKeyOnFinish`. Blackboard defaults: `IdleRange` 7, `InteractRange`, `MovementRange` 1.5, `MeleeRange` 1.
- Grid maths: `SharedMapSystem.GetAllTilesEnumerator(grid, comp)`, `TileIndicesFor`, `GridTileToLocal`,
  `TurfSystem.IsTileBlocked(grid, tile, CollisionGroup.MobMask, comp)`.
- Players and purchased grids carry Monolith's `CompanyComponent` (TSF, PDV, USSP, MMC...); NPC factions are
  separate (`TSFMC`, `USSP`, `VG`, `MMC`). There is no "Rogue".
