# NpcCrew handoff

For whoever (or whatever) picks up the NPC crew work. Everything below is true as of the `npc-crew` branch head.
Paste this whole file as the opening brief; it is written for an AI agent working in a fresh checkout.

## Where things are

- Repository `VanguardControl/Wolfgate`, branch `npc-crew` (an older copy of the same commits sits on
  `clanker/relaxed-lovelace-q1366b`; ignore it). Never commit to `main`. Keep current work local; never open
  a PR without explicit user permission. If authorized, target `VanguardControl/Wolfgate` and fill in
  `.github/PULL_REQUEST_TEMPLATE.md` with a `:cl:` changelog. Commit as
  `gandalf2k15 <9026500+Gandalf2k15@users.noreply.github.com>` without assistant attribution trailers.
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

# The module's tests (35 cases; runtime varies by host)
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Debug --no-build --filter "FullyQualifiedName~WFCrewTest" --logger "console;verbosity=detailed" -nologo 2>&1 | grep -vE "warning RA|warning CS" > /tmp/wfcrew-tests.log
grep -E "^\s*(Passed|Failed) |Error Message|Passed!|Failed!" -A3 /tmp/wfcrew-tests.log | head -40

# Prototype linter (about 4 min, builds Release); must end with "No errors found"
dotnet run --project Content.YAMLLinter -c Release -v q -nologo 2>&1 | grep -vE "warning|^\s*$" | tail -15

# Module docs; the only acceptable complaint is the pre-existing fork_point one in a shallow clone
python3 Tools/_WF/Ci/modules.py --write
```

Never run two dotnet builds at once. `Content.Server` treats nullable warnings as errors. Say plainly in every
report what you built, what you ran and the exact counts; never call something working without running it.

## What is built (35 crew cases verified; see latest validation below)

One idea: every crew NPC runs one HTN root, `WFCrewCompound` (`Resources/Prototypes/_WF/NpcCrew/htn.yml`), with
branches in priority order: **fight** (gated by `WFCrewMayFightPrecondition`, starts with `WFDrawWeaponOperator` and `WFReloadOperator`,
then the upstream `RangedCombatCompound`/`MeleeCombatCompound`), then one branch per **duty** selected by the
`WFCrewDuty` blackboard key (`Pilot` → `WFCrewPilotCompound`, `Guard` → `WFCrewGuardCompound`, `Gunnery` → `WFCrewGunneryCompound`), then **idle**. Every
duty branch starts with `WFHolsterWeaponOperator`. The planner replans every 0.45 s and swaps to an earlier branch
as soon as it is valid, so a fight interrupts a duty and the duty resumes afterwards; nothing else switches state.

| Piece | Files | What it does |
|---|---|---|
| Crew core | `Systems/WFCrewSystem.cs`, `Components/WFCrewComponent.cs`, `WFCrewEvents.cs`, `Content.Shared/_WF/NpcCrew/*` | `WFCrewComponent` (Role, Duty, Engagement, Group, Post, PostRange) mirrored to blackboard keys `WFCrewDuty`, `WFCrewPost`, `WFCrewPostRange`; role title put before the random name; `WFCrewMemberDownEvent` on crit/death. API: `SpawnCrewman(role, post, group)`, `SpawnCrew(plan, group)`, `ClearGroup`, `SetDuty`, `SetPost`, `SetEngagement`, `Apply`. `WFCrewRolePrototype` (`wfCrewRole`: mob, duty, engagement, title, order, extra `components`). |
| Weapons | `Systems/WFCrewWeaponSystem.cs`, `HTN/WFDrawWeaponOperator.cs`, `HTN/WFHolsterWeaponOperator.cs`, `Components/WFCrewWeaponComponent.cs` | Draws the best weapon from equipment slots (back, suitstorage, belt, pockets; longarm > sidearm > melee) into any free hand and selects it; holsters it back into the slot it came from. Weapons never live in bags. Reload and fallback now use real equipment-slot magazines. |
| Engagement | `HTN/WFCrewMayFightPrecondition.cs` | `OnSight` crew may always fight (the combat compounds still need a target); `WhenAttacked` crew only while `NPCRetaliationComponent.AttackMemories` holds an unexpired entry. The crew core also feeds upstream retaliation from `BeforeDamageChangedEvent`, before Wolfmed loses the origin. |
| Planner | `Systems/WFCrewPlannerSystem.cs` | `Plan(grid, deckhands)`: `WFCrewSpawnPoint` markers win; else a Pilot beside the helm, a RadioOperator beside that, a Deckhand on the first free tile inside each airlock (dock outward normal = local rotation applied to (0,-1)), the rest on the most open deck tiles kept 3 apart. Returns `WFCrewPost(Coordinates, Role, Kind)`. |
| Pilot duty | `Systems/WFPilotDutySystem.cs`, `Components/WFPilotDutyComponent.cs`, `HTN/WFPickHelmOperator.cs`, `HTN/WFTakeHelmOperator.cs`, `Content.Shared/_WF/NpcCrew/WFPilotOrder.cs` | Walks to the assigned or nearest powered `ShuttleConsoleComponent`, `EnsureComp<PilotComponent>` + `ShuttleConsoleSystem.AddPilot`, then flies with Mono's `ShipSteeringSystem.Steer(mob, coords)` (`Content.Server/_Mono/NPC/HTN/`). Orders `Hold`, `GoTo(waypoints)`, `Loiter(center, radius)`, `Follow(grid, range)`; waypoints advance in `Update`, not in the HTN, so unattended ships keep flying. The helm is kept while the HTN sleeps (no player within 32 tiles) and released when the pilot dies, is displaced, the console loses power, or the HTN wakes into a plan without the helm. Upstream does not drop pilots on power loss; this system checks power itself. Hold counts as arrived under 0.5 m/s. Events: `WFHelmTakenEvent`, `WFHelmReleasedEvent`, `WFPilotOrdersChangedEvent`, `WFPilotOrdersCompletedEvent`. |
| Docking | `Systems/WFPilotDutySystem.Docking.cs` | `Dock(target)` / `Undock()`. Phases Plan → Approach (standoff 40 m along the target port's normal, avoidance on) → Settle (brake) → Creep (avoidance off, heading held, 1.5 m/s) → `DockingSystem.Dock` the tick `CanDock` holds. `TryPlanDock(grid, target, standoff, out WFDockPlan)` tries every dock pair from `DockingSystem.GetDockingConfig`, nearest standoff first. Abort on timeout or collision, three attempts, then hold and `WFPilotDockFailedEvent`; `WFPilotDockedEvent` on success. Cvar `wf.crew.dock_ftl_fallback` (default false) uses the docking config plus `FTLDock` instead of giving up. A powered-thruster integration test docks without FTL. Manual tuning on production freighter hulls remains a playtest task. |
| Radio officer | `Systems/WFRadioOperatorSystem.cs`, `Components/WFRadioOperatorComponent.cs` | Event-driven, no HTN. Shortband (`Traffic`, 1500 m, needs `TelecomExempt`, which the role adds): docking/undocking once per grid pair, "jumping" while the drive spools (polled; `FTLStartedEvent` fires already in FTL space), "arriving", "on station", "docking aborted". Broadband (`Common`): one mayday per episode, "boarded", "Captain is down", "Helm is down", all-clear after 120 s quiet. Hostile acts: a crewman of the group hurt by an outsider (`BeforeDamageChangedEvent` on `WFCrewComponent`, because Wolfmed routes body damage through parts and the body's `DamageChangedEvent` has no origin) and a hostile mob aboard (`NpcFactionSystem.GetNearbyHostiles`, polled). Cooldown per line. `Sent` keeps the last 20 transmissions for VV and tests. `SetCallsign`. |
| Command | `Commands/WFCrewCommand.cs` (`wf_crew`, `AdminFlags.Spawn`) | `plan <grid|here> [deckhands]`, `spawn <grid|here> [group] [deckhands]`, `spawnrole <role> [group]`, `list [group]`, `clear <group>`, `duty <mob> <duty>`, `orders <mob> hold|goto x y ...|loiter x y r|follow <grid|here>|dock <grid|here>|undock`, `callsign <mob> <text>`. |
| Prototypes | `roles.yml`, `mobs.yml`, `gear.yml`, `markers.yml`, `ai_factions.yml`, `htn.yml` | Roles `WFCrewDeckhand`, `WFCrewMarine` (OnSight), `WFCrewPilot`, `WFCrewRadioOperator`, `WFCrewCaptain` (WhenAttacked; Guard duty with command/radio), `WFCrewGunner`. Mobs `WFMobCrewBase` (parent `[BaseMobHuman, MobPrying]`, faction `WFCrew`, `NPCRetaliation`, HTN root), `WFMobCrewDeckhand`, `WFMobCrewMarine`, `WFMobCrewOfficer` (carries `WFPilotDuty`). Faction `WFCrew` is hostile to SimpleHostile, Zombie, Xeno and nobody else. Markers `WFCrewSpawnPoint<Role>`. |
| Tests | `Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.cs` | `DeckhandDrawsForHostileAndHolstersAfter`, `PlannerPlansHelmRadioDockAndDeck`, `PilotTakesHelmAndReleasesOnDeath`, `DockPlanPutsStandoffOutsideTheTargetDock`, `DockFallsBackToFtlDockWhenAllowed`, `RadioOperatorReportsDocking`, `RadioOperatorMaydayThenCaptainDownThenSilence`. New regression coverage: pilot retaliation, alert scope/decay for named and empty groups, distant recruitment, departure cleanup and preservation of personal retaliation (35 cases total, including access, hull alerts, captain orders, reload/fallback, gunnery, setup UI/API and powered docking). Helpers: `CreateDeck(origin, size, gravity)`, `WaitUntil`, `Describe` (dumps awake state, plan, target, hostiles, held item, factions, nearby mobs). Test decks must sit within ~10 m of the origin: the `InteractionTest` player is there and NPCs sleep with no player within 32 tiles. The hostile test mob is `WFTestHostileMob` (faction `SimpleHostile`); test factions can't be declared in `[TestPrototypes]` because the faction system caches its table before they load. |

## Implementation and verification history

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
   Access doors are implemented below.
3. **Access doors implemented and verified.** Crew opt in to `NavAccess`; marked pathfinding hooks and
   `NPCSteeringSystem.Access.cs` try normal authorized opening before existing obstacle handling. `NavPry` is
   still required for prying. Crew steering compares both bodies' hard collision masks because the engine helper
   reads the first body's fixtures twice, misclassifying closed doors as free space. A second context hook stops
   blended input while settling at obstacles. RobustToolbox is unchanged. Permission is checked at the door,
   not during per-door path pricing.
   `WFCrewAccessSystem` gives crew spawned aboard a ShipAccess-managed grid an ordinary worn ID and registers its
   record key with `TryAddCard`. Removing or revoking the card removes access; duty changes and boarding another
   ship never enroll the crew again. Unmanaged ships require ordinary configured access tags/cards.
   Debug integration build: 0 errors, 1328 warnings; all 18 crew test cases passed. The five new cases cover
   authorized opening with and without prying enabled, denied access with and without prying, and worn-card
   removal, revocation and cross-ship enrollment. Module generation, `--check` and `--pr-check origin/main` passed.
   Initial traversal tests failed with `door=Closed pries=0` because the collision helper reported free space;
   the crew-scoped correction resolves all three failures. No prototype or Client/Shared changes; the linter and
   live-client checks were not rerun. Changes remain local; no PR was opened or updated.
   Remaining features are implemented below.
4. **Hull hits:** `SpaceArtillerySystem.Crew.cs` extends the existing projectile-hit handler with one marked hook.
   Positive hits on anchored hull entities from another grid broadcast `WFCrewHullHitEvent`. The radio reports one
   mayday per episode and the crew alert records the hostile vessel for 60 seconds after the last report. Infantry
   do not receive a ship as their combat target; pilots still retaliate only for personal attacks.
5. **Captain:** `WFCaptainSystem` holds same-grid/group pilots on alert when `HeaveTo` is enabled, saves remaining
   waypoints or the current follow/loiter/dock/undock order, and restores it on all-clear. New orders override the
   saved course for the episode. Death, takeover and departure cancel restoration. The captain role includes radio.
6. **Reload:** `WFReloadOperator` and `WFCrewWeaponSystem.Reload.cs` use compatible magazines from equipment slots,
   ordinary ejection/insertion and bolt cycling. No bags or generated ammunition. No spare means a loaded backup,
   melee weapon, or bare hands. Current crew loadouts use detachable magazines; loose-round weapons are not reloaded.
7. **Crew Setup:** Wolfgate tab and crew admin verb, with an admin-checked network API. Existing or newly spawned
   vessel, planner roster, editable roles/loadouts/posts/engagement, captain, group, callsign, company, NPC faction,
   channels, orders, 30-second private preview, teleport, spawn, clear and reissue. Clear is grid/group scoped.
   All posts and mission prototypes are validated before spawning. Reissuing a mission validates it independently
   of the spawn roster. `TryApplyMission` provides the same operation to encounter callers. Local post coordinates and map destination
   coordinates are separate. Existing planner uses spaced open tiles rather than room flood-fill.
8. **Gunner:** `WFCrewGunner`, `WFGunnerDuty`, HTN console acquisition, mapper marker and planner posts are implemented.
   At an unoccupied powered console, a living gunner drives Mono's `ShipTargetingSystem` against reported hostile
   vessels; friendly factions and other maps are excluded. Physical absence, console loss, player use, combat,
   incapacitation or duty changes stop fire. Only one crew gunner drives a grid's weapons per tick.
9. **Encounter integration:** `WFCrewSetupSystem.TrySpawn(grid, posts, mission, out crew)` and
   `WFCrewAlertSystem.ReportShipThreat(grid, group, attacker)` are the reusable entry points. The Encounters module
   has design docs but no implementation in this checkout; its scheduler, zones, payouts and lifecycle are outside
   NPC crew scope. No speculative RES runtime was added.
10. **Verification (2026-10-02):** final Debug integration build (Server, Shared and Client) passed with 0 errors
    and 2493 warnings. All 28 crew integration cases passed (2.0432 minutes). Release YAML linter: `No errors found in 178853 ms.` Module generation, `--check` and
    `--pr-check origin/main` passed. A fresh headless server reached Ready with no `[ERRO]` or `[FATL]`.
    A real graphical client enabled sandboxing, checked three assemblies and connected; no sandbox violation.
    An earlier client launch logged unrelated Discord IPC failure: `Failed connection to discord-ipc-0. Access to the path is denied.`
    A real-thruster docking integration test now reaches the dock without FTL, and the Crew Setup window is constructed
    and closed by a client integration test. Manual flight tuning on production freighter hulls remains a playtest task.

### Dedicated gunner NPC

`WFMobCrewGunner` is directly spawnable as "ship's gunner" with Gunnery duty, the officer loadout and
when-attacked engagement. `WFCrewGunner` now uses this body, so the setup UI, command and markers agree.
Use `wf_crew spawnrole gunner [group]` when crew membership and spawn-ship credentials are needed.
Both direct and role-based spawning pass `GunnerOperatesConsoleUntilLost` (2/2 targeted cases).
The Debug integration build passed with zero errors; headless startup reached Ready without errors.
The Release prototype linter and module checks passed. No Client/Shared code changed in this addition.

### Crew Setup playtest fixes

Planner posts, including mapper posts, require clear floor and safe atmosphere. Setup rechecks every post before
spawning: upstream pressure/temperature limits, oxygen partial pressure of at least 16 kPa, and at most 0.1 kPa
of gases other than oxygen/nitrogen. Empty plans explain the missing safe posts. This is a spawn-time check;
it does not protect crew from later breaches or unsafe travel routes.
Dock/Follow show a labeled Destination grid picker, exclude the source ship and preserve selection across refresh.
Grid IDs distinguish duplicate names. Choose another grid on the same map and Apply orders.
Procedural test grids need explicit tile-air fixtures; setting map atmosphere alone leaves their existing tiles
in vacuum. The first full run failed three atmosphere-dependent tests (27/30) for this fixture issue.
After correcting the fixtures, all 30 crew tests passed. Server/Client/Shared Debug build passed with zero
errors; the test-only rebuild also passed. A redundant full rebuild hit running executable locks (`MSB3026`),
so the fixture-only rebuild skipped project references to preserve the live playtest session.
Module checks passed. The restarted server and real client connected and reached InGame without logged errors
or sandbox violations. No prototypes changed, so the prototype linter was not rerun for this fix.

### Objectives and unattended crews

`WFCrewObjectiveSystem` owns round-local queues keyed by `(grid, group)`. `SetQueue`, `Control`, `Cancel` and
`Snapshot` are the admin/encounter entry points. Tasks: Hold, GoTo, Dock, Undock, Loiter at grid, Follow/Escort,
Attack grid and Retreat to grid. Repeat GoTo for patrol waypoints. Travel tasks finish on arrival; timed tasks
use seconds, with zero meaning indefinite. Append leaves the current task intact; replacement restarts it.
Pause/resume/skip retain later tasks. A lost target or docking failure pauses with a status; missing pilots wait.
Immediate setup orders cancel the queue. The active-crew list polls every two seconds, retaining unsaved edits.
Queues do not persist across rounds. Attack explicitly authorizes the named grid even across friendly factions;
regular defensive gunnery still excludes friendly factions. Attack has a timer or manual skip, not a damage quota.

A marked `NPCSystem` hook treats crew with `KeepActive` (default true) as active without nearby living players.
It retains upstream incapacity/player checks. Set KeepActive false for deliberately dormant fixtures or actors.
Tests that manually sleep crew now opt out explicitly. Loaded guns close open bolts or rack empty chambers, and
crew check ammunition every half-second during ongoing combat, without creating rounds. Rifle wield interactions
can consume a UseInHand event, so bolts use the gun API directly. Both pistol and rifle provider-shot tests cover it.

Captain response to external ship threats now orbits at 300 m; onboard-only threats still hold. The interrupted
route is restored on all-clear. Pilots enable Mono projectile avoidance outside docking. The checkbox is now
"Captain reacts to attacks"; its existing HeaveTo field remains compatible.

The Dredger/Derelict Drillsite failure reproduced with an awake pilot at the helm stuck in Approach. Aligning
through approach and entering the prechecked docking corridor without the target's avoidance buffer resolved it.
The real hull regression physically mates the ports without FTL (test power is supplied to isolate navigation).
Approach and settle now have bounded retries rather than an infinite hover.

Verification: Debug Server/Shared/Client/integration build passed with 0 errors (2491 warnings). Latest full run:
34/35 passed; the new evasion fixture called TryTakeHelm before the console finished initializing. After allowing
five ticks, the focused evasion retest passed (1/1). Earlier cleanup failures were `TransformComponent` missing
in client `MeleeWeaponSystem.UpdateEffects` after deleting combat actors; fixtures now stop combat and drain effects
before cleanup. Thus all 35 cases are verified across the full run and focused retest. Final test-only build passed.
Module generation/check/PR-marker check passed. Real graphical client enabled sandboxing and reached InGame;
server/client logs contain no `[ERRO]`, `[FATL]` or sandbox violations. Test pair remains on 127.0.0.1:1219.
No prototype changes in this batch, so the Release prototype linter was not rerun. Work remains local.

## Security, work orders and EVA follow-up (2026-10-02)

Crew Setup now has Objectives & Queue, Create Crew, Faction & Security, and Manual Orders tabs. Selecting a live
crew loads its saved faction/radio/security settings. Apply Rules preserves the running queue. Boarding and docking
each support Ignore, Warn or Hostile. Same-company visitors (where companies are present) or overlapping NPC
factions are authorized. Outbound Dock orders authorize their destination. Incoming unauthorized docking raises a
ship threat and a warning; it no longer impersonates an outbound docking announcement. Outbound approach and
successful docking get separate radio lines.

Crew on previously unmanaged map ships now initialize ship access and receive normal revocable cards. Friendly
crew projectiles skip allies, and friendly damage is cancelled before inventory armor, wound routing and retaliation.
Live-threat gating and periodic holstering stop drawing/racking after combat. Tanks are excluded from fallback
weapon selection. GoTo/Follow face their destination and pilots face the console when taking it.

Repair, Resupply and Salvage are queued work orders. Deckhands use built-in ShipRepairTool and nanite-applicator
capabilities: no LimitedCharges component, no lootable unlimited item. Death removes both capabilities permanently
and cancels work. Existing ship repair snapshots are preserved; otherwise the first crew spawn initializes one.
Missing snapshot floors/structures use the ordinary SRD do-after; damaged Repairable entities use the ordinary
repair interaction. Resupply collects loose ammunition and filled oxygen tanks; Salvage collects loose material
stacks. Neither opens containers, fabricates supplies or dismantles structures. A deckhand physically walks to the
target and carries cargo home. Navigation failure times out visibly; pause/resume retries. Objective queues still
require a pilot. Docked-grid navigation uses upstream docking pathfinding portals.

Default loadouts include pressure suits, helmets, masks and finite oxygen tanks. Internals connect before work and
in unsafe air. At the reserve threshold crew try a reachable loose spare; otherwise work is cancelled and the work
navigation branch heads to safe air, then to a filled tank on the home ship. New work is blocked without suitable
EVA protection and oxygen. Custom loadouts remain responsible for providing those items. Blocked paths or a ship
with no safe air/spare tanks still need intervention; oxygen is finite. Crew cannot rebuild damage absent from their
saved SRD snapshot.

Validation: full Debug crew suite passed 47/47. Strengthening the equipment repair fixture to require actual HTN
walking and repair passed 1/1; the updated outbound/incoming docking radio cases passed 2/2. These include SRD floor
and structure reconstruction, permanent death shutdown, real spare-tank replacement, cargo range/carry checks,
boarding rules, friendly damage prevention and the Dredger/Drillsite thruster docking regression. Release YAML
linter: no errors. Module generator/check/PR-marker check passed. The real graphical client enabled sandboxing,
connected to 127.0.0.1:1219 and reached InGame/lobby. Server/client logs contain no `[ERRO]`, `[FATL]` or sandbox
violations (ordinary startup timing/localization warnings remain). The fresh local server/client pair is running.
All work remains local; do not create a PR or push without the user's explicit permission.

## Crew behavior and formation follow-up (2026-10-02)

Added a separate Escort objective (Follow was previously labelled Escort without formation). Each pilot reserves
a distinct staggered target-relative slot, sized for hull clearance, follows its moving/rotating leader and matches
heading. Captain evasion preserves the slot. Any flight order issued while docked first undocks and backs clear;
orders to dock at the already-connected grid complete immediately. Departure waits for living autonomous crew
whose assigned post belongs to the ship. Cruise facing follows the helm orientation; docking holds final alignment
only within the last approach corridor. Pilots and gunners continuously face their occupied consoles.

Crew combat uses dedicated compounds: no loose gun or melee-item scavenging, no chasing to another grid, no
random combat juking. Spent chamber cartridges do not count as usable ammunition. Stale targets cancel combat
movement; work trips are separate and remain permitted. Off-grid crew return to their post when work ends.
Deckhands/marines choose safe deck patrol stops every 45–90 seconds; officers stay at their stations.

Unauthorized foot boarding requires actual unobstructed sight within ten tiles. A witness can report via a real
WFCrewHeadset on Traffic; only ordinary RadioReceiveEvent recipients gain that report. Switched-off receivers
do not share sightings. Contacts expire after thirty seconds unless refreshed. Unauthorized docking remains an
immediate event. Boarding/security alerts also go out on Shortband, alongside the configured alert channel.
Local action lines are limited to one per speaker per fifteen seconds and one per action per minute.

Validation: Debug Server/Shared/Client/integration build passed. The full crew run passed 52/53; the remaining escort fixture needed normal AI helm acquisition, after which its focused retest passed 1/1. This verifies all 53 cases, including actual Dredger docking/departure, hidden boarding and disabled-radio reception, spent-ammo fallback, no loose gun scavenging, console facing and escort slots following a leader turn. Release YAML linter passed with no errors. Its first attempt hit shared output locks while integration tests were running; the sequential rerun passed. Module inventory/check/PR-marker check and git diff checks passed. The fresh Debug server is bound to 127.0.0.1:1219; the graphical CrewTesting client enabled sandboxing and reached GameplayState. No errors, fatal entries or sandbox violations were found in the pair logs; ordinary localization/timing warnings remain. No PR or push was performed.

## Review fixes and station priority (2026-10-02)

Station operators (Pilot/Gunnery duties and Captain/RadioOperator roles) only engage their own remembered personal
attackers, even with On Sight or Hostile boarding selected. Security can still report those boarders; station crew
keep working. A dead, departed or expired attacker no longer justifies fighting. Pilots retain their assigned helm
and require actual unobstructed interaction range both to take and keep it; displacement releases the attachment
that otherwise blocks walking. Idle fallbacks also return to assigned posts. Crew resume their job after combat.

Captain snapshots preserve the intended order behind automatic undocking. Internal departure continuations carry
an explicit flag, so completing evasive undocking no longer discards the interrupted course as a new command.
Objective timers and completion checks suspend while the captain owns the course, with a visible danger-response
status; queued work resumes with its remaining time on all-clear.

Boarding, injury and casualty announcements require personal sight or actual received headset reports. Named groups
do not share incidents across grids. Headset Enabled is checked immediately as well as ActiveRadio, because disabling
a headset defers removal of ActiveRadio until the end of the tick. Explicit hostile docking is tracked independently
of hull impacts and overrides gunner faction filtering; changing security policy clears this override.

Ship targeting passes its controller through the existing fire-control API. Cannon bursts and launched projectiles
retain NPC attribution for friendly-fire protection, including allied hulls. Manual commands clear future cannon
attribution while already launched rounds retain it. Explicit Attack/hostile-docking targets remain damageable.
Missing snapshot repairs skip entries rejected by the ordinary SRD's grid/prototype whitelist or tool repair modes,
so an unsupported destroyed structure cannot block later eligible repairs.

Validation builds are isolated under `bin/NpcCrewReview` (Debug) and `bin/NpcCrewReviewLint` (Release), using the
MSBuild OutDir override. This leaves the live test server's `bin/Content.Server` binaries untouched. The local
playtest received `cvar movement.mob_pushing false`; include `--cvar movement.mob_pushing=false` on future test
launches because the development preset enables it. The ongoing user test has not been restarted to load these fixes.

Validation: the final full Debug crew suite passed 75/75, including all four station roles, both immediate radio-off
cases, captain departure/timer restoration, real cannon projectiles/manual takeover, restricted SRD repairs and
physical Dredger docking/departure. The final Debug build passed with 0 errors (125 existing warnings). Release YAML
linter reported no errors in 79120 ms; an independent headless server reached Ready on port 1220 without ERRO/FATL
entries and was stopped afterwards. No Client/Shared source changed. Module inventory/check/PR-marker check and
git diff checks passed. Initial failures exposed deferred radio disabling and test fixtures with uninitialized power,
an open cannon bolt, insufficient projectile lifetime and armor absorbing the damage probe; these were fixed before
the final passing run. Logs are in TEMP: `wfcrew-review-final-tests.log`, `wfcrew-review-build.log`,
`wfcrew-review-linter.log`, and `wfcrew-review-server.log`. Work stays local; no PR or push.

## Guided crew setup redesign (2026-10-02)

Crew Setup now separates four-step creation (ship, roster, rules, review) from a live dashboard. The persistent
sidebar lists/searches active crews; ship, vessel and loadout dropdowns use the existing native searchable options.
Roster equipment, engagement and ship-local post coordinates expand per member. Creation has its own settings and
always starts with Hold, independently of unfinished manual overrides. Duplicate group creation is rejected locally.

Live crews have an immutable ship/group selection and tabs for objectives, settings and admin tools. Adding a task
appends without interrupting progress. Explicit queue editing keeps a per-crew draft with edit/up/down/remove,
apply-and-restart, and discard controls. Only applicable target, range, coordinates and duration fields appear;
unused inputs normalize to safe defaults. Immediate overrides and crew deletion have inline confirmation panels.
Refreshes preserve drafts and grid identity; a vanished selection becomes unavailable instead of targeting another ship.

Setup requests carry a client-wide RequestId echoed by the server. Windows route operation replies to the originating
context and queue revision. Successful queue saves settle the originating draft even after switching crews, while
newer edits survive. Pending duplicate mutations are suppressed, later plans supersede older ones, roster edits
invalidate pending plans, and spawning a new vessel discards the previous ship's roster.

Validation: isolated Debug client and integration builds passed (final integration build: 0 errors, 1301 existing
warnings). All 17 setup-focused cases passed, including 15 new cases and the existing setup/window checks. The
fresh headless server reached Ready on 127.0.0.1:1220 without ERRO/FATL entries. Desktop control was stopped with
the physical Escape key before launching a graphical client, so this redesign's visual layout and real-client
sandbox check remain unverified. Do not claim that it was visually tested. The original server on 1219 still runs
the earlier playtest build; updated binaries are isolated in bin/NpcCrewReview. No prototype changes were made.
Logs: TEMP/wfcrew-ui-agent-build.log, wfcrew-ui-agent-tests.log, wfcrew-ui-server.log and wfcrew-ui-server-error.log.

### Horizontal layout correction

The setup window now opens at 1280 x 720 with a 1100-wide minimum. Creation, management, crew-list and queue
scroll containers disable horizontal scrolling and reserve vertical scrollbar space, so help text wraps within
the visible panel instead of measuring to unlimited width. The isolated Debug client build passed with 0 errors
(1722 existing warnings). No logic changed or new tests were added. The real client enabled sandboxing and reached
GameplayState without ERRO/FATL or sandbox violations. Desktop control was stopped with Escape before the final
visual check. Logs are TEMP/wfcrew-width-build.log and wfcrew-width-client*.log.

The current test server runs bin/NpcCrewReview on 127.0.0.1:1220 with mob pushing disabled. The client was reconnected
as CrewTesting from bin/NpcCrewUiWidth to load this layout fix; the server and round were preserved.

## Additional engine traps

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
  Its Powered state still initializes asynchronously after docking; wait for `TryFindHelm` instead of fixed ticks.
- Cannon tests must use a closed bolt and a projectile that can reach the target before its lifetime expires.
  Damage probes against walls need `ignoreResistances: true` so flat armor reductions cannot mask a failed control.
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
