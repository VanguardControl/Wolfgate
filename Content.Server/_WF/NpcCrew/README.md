# NpcCrew

Crew Setup separates objectives, crew creation, faction/security settings and immediate orders into tabs. The
selected ship and active-crew picker stay visible. Security settings can be changed without replacing the queue;
unauthorized boarding and docking independently support Ignore, Warn and Hostile. Authorization uses company
membership where available, otherwise a shared NPC faction. A crew's ordered docking destination is authorized
for that approach. Incoming ships do not cause the stationary crew to announce that it is docking. Radio officers
announce their own approach and completed docking, and warn unauthorized arrivals.

Foot boarders must be seen within normal sight range through an unobstructed view before security reacts. Crew
relay sightings through their equipped Shortband headsets; switched-off, removed or blocked radios do not share
contacts. Boarding alerts go to both the local and alert channels. Action chatter has a fifteen-second speaker
cooldown and a one-minute repeat cooldown. Deckhands and marines select safe deck patrol stops 45–90 seconds
apart; other roles keep their stations. Pilots and gunners maintain their facing toward occupied consoles.
Pilots, gunners, captains and radio officers defend themselves only against personal attackers, even when their
engagement rule is On Sight. They actively return after displacement or combat; pilots retain their assigned helm.
Injury and casualty reports require a witness or working headset reception on the same ship.

Crew use their equipped ammunition and weapons, then unarmed combat, without scavenging empty loose guns.
They stop pursuing hostiles that leave their ship. Assigned supply/salvage trips remain allowed, and displaced
crew return home when work ends. Departure waits for living autonomous crew to return aboard, then releases
the docks and backs clear before resuming the next flight order. Long docking approaches face the flight path;
the final docking corridor still uses the required port alignment and may involve lateral thrust.

Escort Grid assigns separate staggered formation slots behind and beside the leader, with spacing enlarged for
hull clearance. Slots move and rotate with the leader, and escorts match its heading. Captain reactions preserve
the slot when resuming an interrupted escort. Follow Grid remains the simpler proximity-following order.

Autonomous crew projectiles pass through allies; allied damage is also stopped before wound routing, armor or
retaliation. Crews holster when no living hostile remains. Forward travel faces the destination, and pilots face
their console when taking the helm. Spawned crew register ordinary, revocable ID credentials even on map-loaded
ships that do not yet have ship access management.
Crew-operated cannon rounds also protect allies and allied hulls, retaining their firing origin after a gunner dies
or a player takes over. Explicit Attack orders and hostile docking still permit fire against the targeted ship.

Repair orders assign deckhands physical work through HTN navigation. Deckhands have a built-in unlimited SRD and
repair tool, disabled permanently on death; no unlimited item drops. The ship's existing SRD snapshot is preserved,
or initialized when its first crew member spawns. Repair restores missing snapshot tiles/structures and uses normal
timed repairs for repairable equipment. Resupply docks and retrieves loose ammunition and filled oxygen tanks;
Salvage docks and retrieves loose material stacks. Workers carry cargo back to their posts. Neither job fabricates
supplies, opens containers or dismantles structures. Unreachable work times out visibly and can be retried with
pause/resume. These jobs require a deckhand, a pilot for the objective queue, and suitable EVA equipment.
Repair skips snapshot entries their SRD cannot rebuild, respecting prototype and grid restrictions and tool modes.

Default crew loadouts include pressure suits, helmets, masks and finite oxygen tanks. Internals connect before
work and in unsafe air. Below the reserve threshold, crew replace the tank from a reachable loose spare or leave
work for safe air; once safe they can seek a filled tank on their home ship. Oxygen is not replenished magically.
Custom loadouts without suitable protection or air cannot start work orders.

Crew Setup also lists active crews and their remaining objectives, refreshing every two seconds. Select a crew
and build a queue of Hold, GoTo coordinates (repeat for patrol waypoints), Dock, Undock, Loiter at a grid, Escort,
Attack or Retreat to a grid. Append preserves the running task; Replace starts the edited queue from its first
entry. Edit, move up, remove, pause, resume and skip are available. Timed Hold/Loiter/Escort/Attack tasks use seconds;
zero means indefinite until skipped. Travel, retreat, dock and undock advance on arrival. Missing targets or a failed
dock pause the queue with a visible status; a replacement pilot can continue it. Immediate orders cancel the queue.
Queues last for the round and are scoped by ship and group. Attack explicitly assigns the gunner's target, including
a normally friendly grid, while regular defensive targeting still respects faction friendship.
An explicit hostile-docking response overrides faction friendship for that visitor. Captain interruptions during
automatic departure preserve the intended next order and its destination beyond the temporary undocking maneuver.

Crew AI stays awake away from player bodies (`KeepActive`, default true; opt out in VV for intentionally dormant
crew). Loaded guns close their bolt/chamber a round before fighting, and ammunition checks continue during combat.
Captains evade external ship threats by orbiting at 300 m and restore the interrupted course after all-clear;
onboard-only threats still cause a hold. Pilots use Mono projectile avoidance outside docking.
The final docking approach aligns the ship early and enters the prechecked port corridor without the destination's
collision-avoidance buffer pushing it away. Approach and settle have bounded retries. The physical Dredger/Drillsite
regression uses the real hulls and thrusters, with test power supplied, and never uses FTL.

Human-style NPC crew for ships: each NPC works a job at a post and drops it to fight, then goes back to work. Crew
carry real loadouts and keep the weapon holstered until there is a fight; a dead crewman's kit is the loot. Mappers
place crew with `WFCrewSpawnPoint*` markers; admins use `wf_crew` to plan a grid (`plan here`), spawn the plan
(`spawn here`), spawn one role where they stand (`spawnrole deckhand`), list, clear a group, change a duty, give a
pilot orders (`orders <mob> hold | goto <x> <y> ... | loiter <x> <y> <radius> | follow <grid|here> | dock <grid|here> |
undock`) or set what a radio officer calls the ship (`callsign <mob> <text...>`).

Crew planning and setup spawning require clear flooring with safe pressure, temperature, oxygen and low
contamination. Unsafe marker posts are omitted. Dock and Follow orders expose a Destination grid picker;
select another grid on the same map, then apply orders. Refresh preserves the destination selection.

Spawn `WFMobCrewGunner` from the entity menu for a ready-to-work gunner, or use `wf_crew spawnrole gunner`
to assign crew membership and ship credentials. Crew Setup and gunner markers use the same NPC. It carries
the officer loadout and operates a powered gunnery console against reported hostile ships.

Built so far: the crew core (`WFCrewSystem`, `WFCrewComponent`, the `WFCrewCompound` HTN root with fight, duty and
idle branches), weapons (`WFCrewWeaponSystem` with the draw and holster operators), the planner (`WFCrewPlannerSystem`:
markers win, else a pilot and radio officer beside the helm, a deckhand inside each airlock and the rest on open
deck), role prototypes (`wfCrewRole`) for deckhand, marine, pilot, radio operator, captain and gunner, the pilot duty and the
command. The pilot (`WFPilotDutySystem`, `WFPilotDutyComponent` on every officer) walks to the nearest powered helm,
takes it and flies its orders (Hold, GoTo waypoints, Loiter, Follow, Dock, Undock) with Mono's ship steering; the
orders advance in the system, so the ship keeps flying while the pilot's HTN sleeps, and the helm is let go when he
dies, is taken over, loses the console or leaves for a fight. Dock is flown by hand (`WFPilotDutySystem.Docking.cs`):
plan a free dock pair with a clear approach lane, fly to a standoff 40 m out from the target dock, settle, creep in and
connect the moment the docks line up, retrying up to three times and then holding (`WFPilotDockedEvent`,
`WFPilotDockFailedEvent`); `wf.crew.dock_ftl_fallback` docks by FTL instead of giving up. Undock releases every port
and backs off. The radio officer (`WFRadioOperatorSystem`, `WFRadioOperatorComponent`, added by his role's
`components`) stands guard at his post and reports what happens to his ship as himself: docking, undocking, jumps,
arrival, orders flown or docking aborted on Shortband; a mayday on the first hostile act of an attack (a crewman hurt
from outside the crew, or a hostile mob aboard), the boarding call, the captain or pilot going down and the all-clear
two minutes after the last hostile activity on Broadband (and a faction channel if set). Events only, no sitreps; he
goes quiet when he is down. Crew retaliation catches incoming damage before Wolfmed routes it through body parts, using upstream faction checks
and attack memory so officers fight back when hit. Captains evade external threats or hold for boarders, then resume the remaining
route afterward; newer orders, death, player takeover, or leaving the crew cancel that saved course.
Shared alerts (`WFCrewAlertSystem`) let living on-sight crew aboard the same ship and group share targets once a
second. Awareness expands across the ship until 60 seconds without a target; crew leaving the group or switching
to when-attacked engagement recover their original awareness. `ShareAlerts` on the crew component opts out.
Officers are excluded, and `WFCrewAlertEvent` / `WFCrewAlertClearedEvent` expose transitions for future duties.
Crew use normal access readers before trying to pry a door. On ShipAccess-managed grids, `SpawnCrewman` equips
and registers an ordinary ID card through `WFCrewAccessSystem`; removing the card or revoking its allow-list entry
removes that access. Movement and duty changes never enroll crew on another ship. Unmanaged doors use the crew's
configured access tags and cards. The access-aware steering partial lives in this module, with small marked hooks
in upstream navigation.
Reloading uses real compatible magazines from equipment slots through normal item interaction. Spent guns are
holstered in favor of a loaded sidearm, melee weapon, or empty hands; no ammunition is created.
Hull impacts from another ship's artillery raise crew alerts and a radio mayday through the existing projectile
handler. Gunners walk to an available powered console and use Mono's ship targeting and fire-control path only
against explicitly reported hostile vessels, excluding friendly factions. Console loss, incapacitation, player
takeover, leaving the post, or changing duty stops their targeting.
Admins open Crew Setup from the Wolfgate tab or a crew NPC's admin verb. Choose a ship (or spawn a vessel), plan and
edit posts, roles, loadouts and engagement, then set company, NPC faction, radio channels and pilot orders. Preview
shows posts only to the requesting admin for 30 seconds; clear affects only the selected grid and group.
`WFCrewSetupSystem.TrySpawn` is the validated encounter-facing roster and mission API;
`WFCrewAlertSystem.ReportShipThreat` supplies encounter vessel threats. RES itself remains a separate project.

`design.md` under `Docs/_WF/NpcCrew/` is the full brief: pilot duty and orders, docking by hand, the event-driven
radio officer, crew alerting, the access-door edit and the Crew Setup admin window.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/NpcCrew/Commands/WFCrewCommand.cs`](Commands/WFCrewCommand.cs)
- [`Content.Server/_WF/NpcCrew/Components/WFCaptainComponent.cs`](Components/WFCaptainComponent.cs)
- [`Content.Server/_WF/NpcCrew/Components/WFCrewComponent.cs`](Components/WFCrewComponent.cs)
- [`Content.Server/_WF/NpcCrew/Components/WFCrewRadioComponent.cs`](Components/WFCrewRadioComponent.cs)
- [`Content.Server/_WF/NpcCrew/Components/WFCrewRepairComponent.cs`](Components/WFCrewRepairComponent.cs)
- [`Content.Server/_WF/NpcCrew/Components/WFCrewSecurityComponent.cs`](Components/WFCrewSecurityComponent.cs)
- [`Content.Server/_WF/NpcCrew/Components/WFCrewShipFireComponent.cs`](Components/WFCrewShipFireComponent.cs)
- [`Content.Server/_WF/NpcCrew/Components/WFCrewSpawnPointComponent.cs`](Components/WFCrewSpawnPointComponent.cs)
- [`Content.Server/_WF/NpcCrew/Components/WFCrewWeaponComponent.cs`](Components/WFCrewWeaponComponent.cs)
- [`Content.Server/_WF/NpcCrew/Components/WFGunnerDutyComponent.cs`](Components/WFGunnerDutyComponent.cs)
- [`Content.Server/_WF/NpcCrew/Components/WFPilotDutyComponent.cs`](Components/WFPilotDutyComponent.cs)
- [`Content.Server/_WF/NpcCrew/Components/WFRadioOperatorComponent.cs`](Components/WFRadioOperatorComponent.cs)
- [`Content.Server/_WF/NpcCrew/HTN/WFCrewMayFightPrecondition.cs`](HTN/WFCrewMayFightPrecondition.cs)
- [`Content.Server/_WF/NpcCrew/HTN/WFCrewReturnOperator.cs`](HTN/WFCrewReturnOperator.cs)
- [`Content.Server/_WF/NpcCrew/HTN/WFCrewTargetOperator.cs`](HTN/WFCrewTargetOperator.cs)
- [`Content.Server/_WF/NpcCrew/HTN/WFCrewWorkOperator.cs`](HTN/WFCrewWorkOperator.cs)
- [`Content.Server/_WF/NpcCrew/HTN/WFDrawWeaponOperator.cs`](HTN/WFDrawWeaponOperator.cs)
- [`Content.Server/_WF/NpcCrew/HTN/WFHolsterWeaponOperator.cs`](HTN/WFHolsterWeaponOperator.cs)
- [`Content.Server/_WF/NpcCrew/HTN/WFPickGunneryOperator.cs`](HTN/WFPickGunneryOperator.cs)
- [`Content.Server/_WF/NpcCrew/HTN/WFPickHelmOperator.cs`](HTN/WFPickHelmOperator.cs)
- [`Content.Server/_WF/NpcCrew/HTN/WFReloadOperator.cs`](HTN/WFReloadOperator.cs)
- [`Content.Server/_WF/NpcCrew/HTN/WFTakeGunneryOperator.cs`](HTN/WFTakeGunneryOperator.cs)
- [`Content.Server/_WF/NpcCrew/HTN/WFTakeHelmOperator.cs`](HTN/WFTakeHelmOperator.cs)
- [`Content.Server/_WF/NpcCrew/Systems/FireControlSystem.Crew.cs`](Systems/FireControlSystem.Crew.cs)
- [`Content.Server/_WF/NpcCrew/Systems/NPCSteeringSystem.Access.cs`](Systems/NPCSteeringSystem.Access.cs)
- [`Content.Server/_WF/NpcCrew/Systems/SpaceArtillerySystem.Crew.cs`](Systems/SpaceArtillerySystem.Crew.cs)
- [`Content.Server/_WF/NpcCrew/Systems/WFCaptainSystem.cs`](Systems/WFCaptainSystem.cs)
- [`Content.Server/_WF/NpcCrew/Systems/WFCrewAccessSystem.cs`](Systems/WFCrewAccessSystem.cs)
- [`Content.Server/_WF/NpcCrew/Systems/WFCrewAlertSystem.cs`](Systems/WFCrewAlertSystem.cs)
- [`Content.Server/_WF/NpcCrew/Systems/WFCrewCommsSystem.cs`](Systems/WFCrewCommsSystem.cs)
- [`Content.Server/_WF/NpcCrew/Systems/WFCrewEvaSystem.cs`](Systems/WFCrewEvaSystem.cs)
- [`Content.Server/_WF/NpcCrew/Systems/WFCrewFriendlyFireSystem.cs`](Systems/WFCrewFriendlyFireSystem.cs)
- [`Content.Server/_WF/NpcCrew/Systems/WFCrewObjectiveSystem.cs`](Systems/WFCrewObjectiveSystem.cs)
- [`Content.Server/_WF/NpcCrew/Systems/WFCrewPlannerSystem.cs`](Systems/WFCrewPlannerSystem.cs)
- [`Content.Server/_WF/NpcCrew/Systems/WFCrewRoutineSystem.cs`](Systems/WFCrewRoutineSystem.cs)
- [`Content.Server/_WF/NpcCrew/Systems/WFCrewSecuritySystem.cs`](Systems/WFCrewSecuritySystem.cs)
- [`Content.Server/_WF/NpcCrew/Systems/WFCrewSetupSystem.cs`](Systems/WFCrewSetupSystem.cs)
- [`Content.Server/_WF/NpcCrew/Systems/WFCrewSpeechSystem.cs`](Systems/WFCrewSpeechSystem.cs)
- [`Content.Server/_WF/NpcCrew/Systems/WFCrewSystem.cs`](Systems/WFCrewSystem.cs)
- [`Content.Server/_WF/NpcCrew/Systems/WFCrewWeaponSystem.cs`](Systems/WFCrewWeaponSystem.cs)
- [`Content.Server/_WF/NpcCrew/Systems/WFCrewWeaponSystem.Reload.cs`](Systems/WFCrewWeaponSystem.Reload.cs)
- [`Content.Server/_WF/NpcCrew/Systems/WFCrewWorkSystem.cs`](Systems/WFCrewWorkSystem.cs)
- [`Content.Server/_WF/NpcCrew/Systems/WFCrewWorkSystem.Repair.cs`](Systems/WFCrewWorkSystem.Repair.cs)
- [`Content.Server/_WF/NpcCrew/Systems/WFGunnerDutySystem.cs`](Systems/WFGunnerDutySystem.cs)
- [`Content.Server/_WF/NpcCrew/Systems/WFPilotDutySystem.cs`](Systems/WFPilotDutySystem.cs)
- [`Content.Server/_WF/NpcCrew/Systems/WFPilotDutySystem.Docking.cs`](Systems/WFPilotDutySystem.Docking.cs)
- [`Content.Server/_WF/NpcCrew/Systems/WFRadioOperatorSystem.cs`](Systems/WFRadioOperatorSystem.cs)
- [`Content.Server/_WF/NpcCrew/WFCrewEvents.cs`](WFCrewEvents.cs)

### Shared

- [`Content.Shared/_WF/NpcCrew/WFCrewEngagement.cs`](../../../Content.Shared/_WF/NpcCrew/WFCrewEngagement.cs)
- [`Content.Shared/_WF/NpcCrew/WFCrewRolePrototype.cs`](../../../Content.Shared/_WF/NpcCrew/WFCrewRolePrototype.cs)
- [`Content.Shared/_WF/NpcCrew/WFCrewSetupMessages.cs`](../../../Content.Shared/_WF/NpcCrew/WFCrewSetupMessages.cs)
- [`Content.Shared/_WF/NpcCrew/WFPilotOrder.cs`](../../../Content.Shared/_WF/NpcCrew/WFPilotOrder.cs)

### Client

- [`Content.Client/_WF/NpcCrew/WFCrewSetupClientSystem.cs`](../../../Content.Client/_WF/NpcCrew/WFCrewSetupClientSystem.cs)
- [`Content.Client/_WF/NpcCrew/WFCrewSetupOverlay.cs`](../../../Content.Client/_WF/NpcCrew/WFCrewSetupOverlay.cs)
- [`Content.Client/_WF/NpcCrew/WFCrewSetupWindow.cs`](../../../Content.Client/_WF/NpcCrew/WFCrewSetupWindow.cs)
- [`Content.Client/_WF/NpcCrew/WFCrewSetupWindow.Objectives.cs`](../../../Content.Client/_WF/NpcCrew/WFCrewSetupWindow.Objectives.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.CaptainResume.cs`](../../../Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.CaptainResume.cs)
- [`Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.Command.cs`](../../../Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.Command.cs)
- [`Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.cs`](../../../Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.cs)
- [`Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.Doors.cs`](../../../Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.Doors.cs)
- [`Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.FireAndRepair.cs`](../../../Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.FireAndRepair.cs)
- [`Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.Flight.cs`](../../../Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.Flight.cs)
- [`Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.Objectives.cs`](../../../Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.Objectives.cs)
- [`Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.RadioSecurity.cs`](../../../Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.RadioSecurity.cs)
- [`Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.Reload.cs`](../../../Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.Reload.cs)
- [`Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.Routines.cs`](../../../Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.Routines.cs)
- [`Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.Security.cs`](../../../Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.Security.cs)
- [`Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.Setup.cs`](../../../Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.Setup.cs)
- [`Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.Stations.cs`](../../../Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.Stations.cs)
- [`Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.Work.cs`](../../../Content.IntegrationTests/Tests/_WF/NpcCrew/WFCrewTest.Work.cs)

### Prototypes

- [`Resources/Prototypes/_WF/NpcCrew/ai_factions.yml`](../../../Resources/Prototypes/_WF/NpcCrew/ai_factions.yml)
- [`Resources/Prototypes/_WF/NpcCrew/gear.yml`](../../../Resources/Prototypes/_WF/NpcCrew/gear.yml)
- [`Resources/Prototypes/_WF/NpcCrew/htn.yml`](../../../Resources/Prototypes/_WF/NpcCrew/htn.yml)
- [`Resources/Prototypes/_WF/NpcCrew/markers.yml`](../../../Resources/Prototypes/_WF/NpcCrew/markers.yml)
- [`Resources/Prototypes/_WF/NpcCrew/mobs.yml`](../../../Resources/Prototypes/_WF/NpcCrew/mobs.yml)
- [`Resources/Prototypes/_WF/NpcCrew/roles.yml`](../../../Resources/Prototypes/_WF/NpcCrew/roles.yml)

### Localization

- [`Resources/Locale/en-US/_WF/NpcCrew/crew.ftl`](../../../Resources/Locale/en-US/_WF/NpcCrew/crew.ftl)
- [`Resources/Locale/en-US/_WF/NpcCrew/radio-reports.ftl`](../../../Resources/Locale/en-US/_WF/NpcCrew/radio-reports.ftl)

### Docs

- [`Docs/_WF/NpcCrew/design.md`](../../../Docs/_WF/NpcCrew/design.md)
- [`Docs/_WF/NpcCrew/handoff.md`](../../../Docs/_WF/NpcCrew/handoff.md)

## Non-modular edits

- [`Content.Server/_Mono/FireControl/FireControlSystem.cs`](../../_Mono/FireControl/FireControlSystem.cs): distinguish NPC bursts from later manual fire.
- [`Content.Server/_Mono/NPC/HTN/ShipTargetingSystem.cs`](../../_Mono/NPC/HTN/ShipTargetingSystem.cs)
  - preserve the controller for autonomous cannon damage.
  - carry the NPC controlling this burst.
  - attribute crew bursts without changing the muzzle.
- [`Content.Server/_Mono/SpaceArtillery/SpaceArtillerySystem.cs`](../../_Mono/SpaceArtillery/SpaceArtillerySystem.cs): notify crew of damaging impacts from other ships.
- [`Content.Server/NPC/Pathfinding/PathfindingSystem.Common.cs`](../../NPC/Pathfinding/PathfindingSystem.Common.cs): access-aware NPCs may plan through readers and check permission at the door.
- [`Content.Server/NPC/Pathfinding/PathfindingSystem.cs`](../../NPC/Pathfinding/PathfindingSystem.cs): opt-in NPCs try their access before prying doors.
- [`Content.Server/NPC/Systems/NPCSteeringSystem.Context.cs`](../../NPC/Systems/NPCSteeringSystem.Context.cs)
  - compare both bodies for crew while the engine helper reads the first fixtures twice.
  - stop blended input while access-aware crew settle at an obstacle.
- [`Content.Server/NPC/Systems/NPCSteeringSystem.Obstacles.cs`](../../NPC/Systems/NPCSteeringSystem.Obstacles.cs): open authorized doors through normal interaction before considering prying.
- [`Content.Server/NPC/Systems/NPCSystem.cs`](../../NPC/Systems/NPCSystem.cs): ship crews must work even without nearby player bodies.

<!-- WOLFGATE-GENERATED END -->
