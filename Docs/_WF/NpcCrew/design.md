# NPC crew

Module name `NpcCrew`. Folders `Content.{Shared,Server}/_WF/NpcCrew/`, `Resources/Prototypes/_WF/NpcCrew/`,
`Resources/Locale/en-US/_WF/NpcCrew/`, tests in `Content.IntegrationTests/Tests/_WF/NpcCrew/`. Namespaces
`Content.*._WF.NpcCrew`, prototype IDs `WFCrew*`, blackboard keys `WFCrew*`, Fluent IDs `wf-crew-*`.

Human-style NPCs that do a job and drop it to fight when a hostile shows up, then go back to the job. Place First
Officer Jeff on any ship and he walks to the helm, takes it and flies his orders; place Radio Officer Jack and he
narrates the ship on Shortband and Broadband. Nothing here knows about encounters, freighters or factions beyond what
the mob's own `NpcFactionMember` says. RES (`Docs/_WF/Encounters/design.md`) consumes this module; it does not define
it.

## The idea in one paragraph

Every crew NPC runs one HTN root with three branches in priority order: **fight**, **do your job**, **idle**. The HTN
planner already replans every half second and swaps to an earlier branch the moment it becomes valid, shutting the
current task down with `BetterPlan`. So "Jeff pilots until he sees a hostile, fights, then goes back to piloting" is
not a state machine we write; it is the branch order plus one rule: every job task tidies up when shut down (Jeff
releases the helm). Jobs are separate compounds selected by a blackboard key, each with its own system, so a new job
is a new compound and a new system, nothing else changes.

## What exists

- **Combat**: Frontier's hostile humanoid packages (`MobHumanoidHostileAISimpleRanged`, `...Melee`, `...Complex` in
  `_NF/Entities/Mobs/NPCs/mob_hostile_base.yml`) with `RangedCombatCompound`, `MeleeCombatCompound`, `NPCRetaliation`
  and the gun built into the mob (infinite ammo, loadout is cosmetic). Target acquisition is `NearbyHostiles`: faction
  hostility or an aggro entry, within `VisionRadius` (10 tiles), with a line-of-sight consideration. Both combat
  compounds fail when there is no target, which is what makes them a clean first branch.
- **Piloting**: `ShuttleConsoleSystem.AddPilot(console, mob, comp)` attaches a mob with `PilotComponent` to a helm.
  Upstream already enforces most of "physically controls the console": a pilot cannot move (`PilotComponent` blocks
  `CanMove`), is dropped every tick it can't interact with the console (crit, dead, stunned, cuffed), and is cleared
  when the console loses power or is destroyed. Steering comes from Mono `ShipSteeringSystem`: `Steer(mob, coords)`
  puts `ShipSteererComponent` on the mob and it answers `GetShuttleInputsEvent` with thrust toward the target, with
  collision and projectile avoidance, `GoToRange` or `Orbit` modes. The two coexist on one mob: the pilot handler
  writes no input while its buttons are zero, so the steerer's input stands.
- **HTN plumbing**: `KeyEqualsPrecondition` (Mono) for string blackboard keys, `MoveToOperator`, `SpeakOperator`,
  `WaitOperator`, `IdleCompound`, `NPCSystem.SetBlackboard`, `HTNSystem.Replan`, `NpcFactionSystem.AggroEntity`.
- **Radio**: `RadioSystem.SendRadioMessage(source, text, channel, radioSource)` from any entity. `Traffic` is
  "Shortband" (1500 m, needs a telecom server in reach or `TelecomExemptComponent` on the radio source); `Common` is
  "Broadband" (whole map through the outpost's telecom server). Faction channels `Nfsd` (TSF) and `Ussp` exist.
- **Ship events**: `DockEvent`/`UndockEvent` (both grids and both docks), `FTLStartedEvent`, `FTLCompletedEvent`,
  `ShipWeaponProjectile` on hull hits.
- **Gaps**: no shared awareness between NPCs; pathfinding treats every door with an access reader as pry-only and
  never asks whether the NPC is allowed through; `InteractWithOperator` on a console opens a UI, which an NPC cannot
  do; NPC HTNs sleep when no player is within 32 tiles.

## The compartments

Each is a system plus a component plus (where it acts) an HTN compound. Each works alone; the arrows below are the
only couplings, and they are events.

```
 Crew core ──selects──> Pilot duty ──raises WFHelm*/WFOrders* events──> Radio duty
     │                      ▲                                            ▲
     └──alert events───> Crew alert (optional)      Ship events (dock, FTL, hull hits)
 Access & doors: a marked pathfinding edit, used by everything that walks.
```

### 1. Crew core

`WFCrewComponent` on the mob: `Duty` (string, mirrored to blackboard `WFCrewDuty`), `Post` (an entity to work at or
return to, usually a map marker), `Group` (string; everyone on a ship with the same group is one crew), `StaysAtPost`
(ignore crew alerts unless a hostile is within `PostDefenceRange`; for pilots). `WFCrewSystem` writes the blackboard
keys on map init and when they change, and raises `WFCrewDutyChangedEvent`.

HTN root `WFCrewCompound`:

1. `RangedCombatCompound` then `MeleeCombatCompound` (unchanged Frontier compounds).
2. One branch per duty, each gated by `KeyEqualsPrecondition WFCrewDuty == <Duty>`, task = that duty's compound.
3. `IdleCompound`, with `IdleRange` small so idle crew drift around their post rather than the ship.

Rules every duty compound follows: its operators' `Shutdown` undoes whatever the operator holds (helm, a UI, an
anchor), it must be restartable from any state (Jeff might be anywhere when combat ends), and it never sets the
`Target` key. The planner does the switching.

### 2. Pilot duty (First Officer Jeff)

`WFPilotDutyComponent`: `Console` (optional assigned helm, else the nearest powered `ShuttleConsoleComponent` on the
mob's grid), `Orders` and their parameters:

| Order | Behaviour | Steering |
|---|---|---|
| `Hold` | Take the helm, stop the ship, keep station. | `GoToRange` to own position, `Range` 5 |
| `GoTo` | Fly a list of waypoints, then `Hold` at the last; raise `WFPilotOrdersCompletedEvent`. | `GoToRange`, `InRangeMaxSpeed` = cruise speed |
| `Loiter` | Circle a point or a grid at a radius. | `Orbit` |
| `Follow` | Keep range on another grid (an escort). | `GoToRange` with coordinates relative to the target grid; `EntityCoordinates` follow for free |
| `Dock` | Fly to a standoff point near the target grid, then dock. | `GoToRange`, then `ShuttleSystem.TryFTLDock` for v1 (the short hop shipyard deliveries use); hand-flown docking later |

`WFPilotDutyCompound`: pick the helm (`WFPickHelmOperator`, writes `WFCrewHelm`), `MoveToOperator` to it, then
`WFTakeHelmOperator`: `EnsureComp<PilotComponent>`, `AddPilot`, `ShipSteeringSystem.Steer` to the current target, and
stay in the operator while the pilot is attached. Its `Shutdown` calls `Stop` and `RemovePilot`, whatever the reason.
If the helm attachment is lost for any upstream reason (crit, power, destroyed console) the operator fails and the
planner restarts the compound: Jeff gets up, finds a helm, takes it again when he can.

Waypoint advancing, orbit centre updates and `Dock` sequencing live in `WFPilotDutySystem.Update`, not in the HTN
operator, so a ship far from any player keeps flying while Jeff's HTN sleeps. The operator only owns the helm.
`SetOrders(uid, orders)` is the public API and raises `WFPilotOrdersChangedEvent`; `WFHelmTakenEvent` and
`WFHelmReleasedEvent` fire on attach and release.

What "can't pilot" means, and who enforces it:

- dead or down: upstream drops the pilot the tick he can't interact; steering stops in the operator's shutdown;
- fighting: the combat branch wins the replan, `WFTakeHelmOperator` shuts down, helm released;
- walking: impossible while attached, movement is blocked;
- no console, no power, console gone: upstream clears pilots, the operator fails, Jeff waits at the wreck of his helm
  under `IdleCompound` and retries every replan.

A ship with Jeff down is a ship that drifts. That is intended.

### 3. Radio duty (Radio Officer Jack)

`WFRadioOperatorComponent` on the mob: a `Channels` map from message class to radio channel (`Local → Traffic`,
`Alert → Common`, optional `Faction → Nfsd`), `Callsign` (defaults to the grid's name, "Freighter XX-121"),
`SitrepInterval` (120 s), `Cooldown` per message class, `Posts` the mob returns to (`Hold` branch is just
`MoveToOperator` to the post then idle).

`WFRadioOperatorSystem` is event-driven, not HTN-driven, so it works while Jack's HTN sleeps and needs no operator of
its own. It listens on Jack's grid and speaks through `RadioSystem.SendRadioMessage(jack, text, channel, jack)` with
Jack's name as sender, so killing or downing Jack ends the broadcasts literally (the system checks he is alive and not
critical before every line):

| Trigger | Class | Example |
|---|---|---|
| `DockEvent` with another grid | Local | "Freighter XX-121 docking at Caelestinus Central, port 3." |
| `UndockEvent` | Local | "Freighter XX-121 clear of Caelestinus Central, departing." |
| `FTLStartedEvent` / `FTLCompletedEvent` | Local | "Freighter XX-121 jumping." / "Freighter XX-121 arriving, bearing 040." |
| `WFPilotOrdersCompletedEvent` | Local | "Freighter XX-121 on station." |
| Crew alert raised, or a `ShipWeaponProjectile` hull hit from another grid | Alert | "Mayday, Freighter XX-121 under attack at 4120, -1870, hostile vessel Ratbag." |
| Every `SitrepInterval` while alerted | Alert | "Freighter XX-121 still under attack, hull holding, two crew down." |
| Alert cleared | Alert | "Freighter XX-121, hostiles gone, resuming course." |

All text is Fluent with the callsign, station, dock, bearing and hostile name as arguments; several variants per line
so repeated messages aren't dropped by the radio system's duplicate filter. Jack carries `TelecomExemptComponent` (a
ship radio set) so Shortband works away from the outpost; Broadband already does. He also says a short local line
(`SpeakOperator` style, via `ChatSystem`) so people on the bridge hear him talk.

Jack has no special HTN beyond standing at his post and fighting like everyone else. If his ship has no Jeff, he still
reports docks, jumps and attacks; if it has no hostile crew alert system, hull hits alone trigger the alert class.

### 4. Crew alert (optional, shared awareness)

`WFCrewAlertSystem`: once a second, for each `Group` on a grid, look at members' blackboards for a `Target`. When one
appears, raise `WFCrewAlertEvent(group, hostiles)` and for every other member without `StaysAtPost` (or with the
hostile inside their `PostDefenceRange`): `AggroEntity(member, hostile)`, raise `AggroVisionRadius` to cover the ship,
`Replan`. The alert decays `AlertDecay` (60 s) after the last member lost its target, restoring vision and raising
`WFCrewAlertClearedEvent`. Without this system the crew still fights, one NPC at a time, as it sees things; with it
the whole crew converges on a boarder, which is what a ship's crew should do.

### 5. Access and doors

Crew need two things to walk a real ship: access, and a pathfinder that respects it. Access is `AccessComponent` tags
on the mob (Frontier mobs already have the component); for purchased ships locked by `ShipAccess`, `WFCrewSystem`
registers the NPC in the ship's allow list the way a crew member would be (an integration point, not a bypass).

The pathfinder flags any door with an `AccessReaderComponent` as `Access` and offers NPCs only prying through it.
This needs a small marked edit outside `_WF` (`// WOLFGATE(NpcCrew)`), three hunks:

- `PathfindingSystem.GetFlags`: a blackboard `NavAccess` sets `PathFlags.Access`, which exists but nothing sets today.
- `PathfindingSystem.GetTileCost`: an `Access` door costs the same as a plain door for a request carrying that flag.
- `NPCSteeringSystem.TryHandleFlags`: with the flag, if `AccessReaderSystem.IsAllowed(npc, door)` use
  `InteractionActivate` (the door opens normally) and only fall back to prying when not allowed and `NavPry` is set.

Without this edit everything still works on a map whose interior doors have no reader; with it Jeff walks through his
own ship's airlocks like a person and boarders still find the exterior airlocks locked.

### 6. Spawning and tools

- `WFCrewSpawnPointComponent` map marker: `Role` (crew prototype), `Post` (itself by default), `Group`. `WFCrewSystem`
  spawns the mob on map init and fills in `WFCrewComponent`. Mappers place crew by placing markers.
- Naming: `RandomMetadata` with the species name datasets and a role title from Fluent, so the sender reads
  "First Officer Jeff Marlow".
- `wf_crew` admin command: `spawn <role> [post]`, `orders <npc> hold|goto x y|loiter x y r|follow <grid>|dock <grid>`,
  `duty <npc> <duty>`, `list [grid]`. An admin verb "Orders..." on a crew NPC opens the same choices.
- Everything on `WFCrewComponent`, `WFPilotDutyComponent` and `WFRadioOperatorComponent` is editable in VV.

## NPCs to start with

In build order; each one proves a compartment.

1. **Deckhand** (`WFCrewDeckhand`, duty `Guard`). Stands at a post, idles nearby, fights what it sees, goes back.
   Proves the core compound and the shutdown discipline with the simplest job (`MoveToOperator` to post, idle).
   Variants: sidearm, melee, rifle (`Marine`), straight from the Frontier bases.
2. **First Officer** (`WFCrewPilot`, duty `Pilot`, `StaysAtPost`). Jeff. Proves helm attach and release, orders,
   waypoints, the pilot-and-steerer coexistence, and "down means drifting".
3. **Radio Officer** (`WFCrewRadioOperator`, duty `Hold` at the radio post). Jack. Proves event-driven speech and
   channel routing; also the first thing players will notice.
4. **Captain** (`WFCrewCaptain`). A radio officer whose system also issues orders: on alert, `SetOrders(Hold)` if his
   `HeaveTo` flag is set, otherwise leave the pilot flying; on alert cleared, restore the previous orders. One
   component (`WFCaptainComponent`) with those two reactions; everything else is Jack's code. Gives a ship one entity
   whose death matters.
5. **Gunner** (`WFCrewGunner`, duty `Gunnery`), later. Takes a gunnery console the way Jeff takes the helm and fires
   the ship's `FireControllable` guns at the crew's hostiles through the same `FireControlSystem.AttemptFire` path
   Mono's ship AI uses; no gunner at the console, no ship guns. Waits on a faction-aware target source.

Not now: engineers (repair is a deep rabbit hole), medics (upstream medibot logic exists but patient selection is a
project), anyone who needs inventory management.

## Risks

- **Sleep**. NPC HTNs pause with no player within 32 tiles. Steering and radio are system-driven and keep working;
  the HTN only owns attach, release and walking. If attach itself has to happen unobserved (a ship spawned far away),
  give the pilot prototype `SleepPlayerCheckRangeOverride`, as Mono's AI cores do.
- **Combat compounds on a moving ship**. Steering is grid-relative and should be fine, but `MoveToOperator` under
  acceleration hasn't been exercised; the first live test is Jeff's own ship under way with a boarder.
- **Pry-happy crew**. Until the access edit lands, a crew on a stock ship pries its own doors. Ship this with the edit
  or with readerless interior doors; don't ship without either.
- **Duplicate radio lines**. The radio system drops an identical message already in flight. Variants and arguments
  in every template, and the cooldown per message class.
- **Replan thrash**. A hostile flickering at the edge of vision would make Jeff hop on and off the helm. The combat
  branch already carries attack memory (`NPCRetaliation`, 10 s); add a `WFCrewMinDutyTime` (5 s) precondition on the
  helm operator so he doesn't re-take it before the fight is clearly over.

## Tests

`Content.IntegrationTests/Tests/_WF/NpcCrew/`: a test grid with a helm, a post and a door.

- Core: a deckhand with a hostile spawned in view plans the combat branch; with the hostile removed, within two
  replans it is back on the duty branch.
- Pilot: Jeff spawned with `GoTo` acquires `PilotComponent` and `ShipSteererComponent` within a few seconds; the grid
  gains velocity toward the waypoint; killing Jeff removes both and the grid stops being driven; a hostile spawned on
  the bridge releases the helm while Jeff lives.
- Radio: a `DockEvent` on Jack's grid produces one message on `Traffic` with the callsign and station name; a hull hit
  from another grid produces one on `Common`; a dead Jack produces none.
- Doors (with the access edit): a crew NPC with access paths through a reader door without prying it; one without
  access pries.

No `Destructive` pool settings.

## Open questions

1. **Does Jeff fight at all?** Assumed yes but only when a hostile is near his post (`StaysAtPost`), otherwise a
   single boarder anywhere stops the ship. Alternative: pilots never fight, they hold the helm until downed.
2. **Hand-flown docking or the FTL hop?** Assumed the hop for v1. Real docking means aligning a dock within 1.2 m and
   15°, which the steering system can't do yet.
3. **Broadband for every attack?** Assumed yes. If it becomes noise, route attacks to the faction channel and keep
   Broadband for ships with no faction.
4. **Should NPC crew count as crew for `ShipAccess`?** Assumed yes via the allow list. The alternative is a blanket
   "NPCs ignore ship locks", which players will dislike the first time a hostile boarder uses it.
