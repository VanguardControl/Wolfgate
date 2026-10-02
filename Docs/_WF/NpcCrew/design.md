# NPC crew

Module name `NpcCrew`. Folders `Content.{Shared,Server}/_WF/NpcCrew/`, `Resources/Prototypes/_WF/NpcCrew/`,
`Resources/Locale/en-US/_WF/NpcCrew/`, tests in `Content.IntegrationTests/Tests/_WF/NpcCrew/`. Namespaces
`Content.*._WF.NpcCrew`, prototype IDs `WFCrew*`, blackboard keys `WFCrew*`, Fluent IDs `wf-crew-*`.

Human-style NPCs that do a job and drop it to fight, then go back to the job. Place First Officer Jeff on any ship and
he walks to the helm, takes it and flies his orders; place Radio Officer Jack and he narrates the ship on Shortband
and Broadband. Crew carry real loadouts: the gun stays holstered until there is a fight, and a dead crewman's kit is
what you loot. Nothing here knows about encounters, freighters or factions beyond what the mob's own
`NpcFactionMember` says. RES (`Docs/_WF/Encounters/design.md`) consumes this module; it does not define it.

## The idea in one paragraph

Every crew NPC runs one HTN root with three branches in priority order: **fight**, **do your job**, **idle**. The HTN
planner already replans every half second and swaps to an earlier branch the moment it becomes valid, shutting the
current task down with `BetterPlan`. So "Jeff pilots until someone shoots him, fights, then goes back to piloting" is
not a state machine we write; it is the branch order plus one rule: every job task tidies up when shut down (Jeff
releases the helm) and every fight starts by drawing and ends by holstering. Jobs are separate compounds selected by a
blackboard key, each with its own system, so a new job is a new compound and a new system, nothing else changes.

## What exists

- **Combat**: upstream `RangedCombatCompound` / `GunCombatCompound` (shoot while `GunAmmoPrecondition` holds, needs the
  gun in the active hand), `MeleeCombatCompound`, `NPCRetaliation` (aggro on whoever damages you, remembered for
  `attackMemoryLength`), `NearbyHostiles` (faction hostility or an aggro entry, within `VisionRadius` of 10 tiles, with
  a line-of-sight consideration). Both combat compounds fail with no target, which is what makes them a clean first
  branch. Reloading and drawing from inventory are `TODO` comments in `Resources/Prototypes/NPCs/Combat/gun.yml`; the
  upstream reason is that searching nested storage is painful. Frontier sidestepped it by building an infinite gun
  into the mob (`MobHumanoidHostileAISimpleRanged`), which is why their mercenaries never holster.
- **Death**: `NPCSystem` puts an NPC to sleep when it goes critical or dies; nothing wakes a corpse.
- **Piloting**: `ShuttleConsoleSystem.AddPilot(console, mob, comp)` attaches a mob with `PilotComponent` to a helm.
  Upstream already enforces most of "physically controls the console": a pilot cannot move, is dropped every tick it
  can't interact with the console (crit, dead, stunned, cuffed), and is cleared when the console loses power or is
  destroyed. Steering comes from Mono `ShipSteeringSystem`: `Steer(mob, coords)` puts `ShipSteererComponent` on the
  mob and it answers `GetShuttleInputsEvent` with thrust toward the target, with collision and projectile avoidance,
  `GoToRange` or `Orbit` modes, and `InRangeRotation` for arriving at a given heading. The two coexist on one mob: the
  pilot handler writes no input while its buttons are zero, so the steerer's input stands.
- **Docking**: `DockingSystem.GetDockingConfig(shuttle, targetGrid)` already works out which pair of docks fits and the
  exact shuttle pose that puts them together (the FTL-to-dock path uses it). `CanDock` wants the two docks within 1.2 m
  and 15° and both free; `Dock` is public. There is no auto-dock.
- **Inventory**: sidearms carry a `Clothing` component for the belt and suit-storage slots, longarms for the back and
  suit storage; `InventorySystem.TryEquip`/`TryUnequip` and `HandsSystem.TryPickup` move items; guns take ammo by
  `InteractUsing` (loose rounds into a ballistic provider, a magazine into a magazine gun).
- **HTN plumbing**: `KeyEqualsPrecondition` (Mono), `MoveToOperator`, `SpeakOperator`, `WaitOperator`, `IdleCompound`,
  `NPCSystem.SetBlackboard`, `HTNSystem.Replan`, `NpcFactionSystem.AggroEntity`.
- **Radio**: `RadioSystem.SendRadioMessage(source, text, channel, radioSource)` from any entity. `Traffic` is
  "Shortband" (1500 m, needs a telecom server in reach or `TelecomExemptComponent` on the radio source); `Common` is
  "Broadband" (whole map through the outpost's telecom server). Faction channels `Nfsd` (TSF) and `Ussp` exist.
- **Ship events**: `DockEvent`/`UndockEvent`, `FTLStartedEvent`, `FTLCompletedEvent`, `ShipWeaponProjectile` on hull
  hits, `MobStateChangedEvent` on crew.
- **Gaps**: no shared awareness between NPCs; pathfinding treats every door with an access reader as pry-only and
  never asks whether the NPC is allowed through; an NPC cannot open a console UI, so it cannot take a helm the way a
  player does; NPC HTNs sleep when no player is within 32 tiles.

## The compartments

Each is a system plus a component plus (where it acts) an HTN compound. Each works alone; the arrows below are the
only couplings, and they are events.

```
 Crew core ──selects──> Pilot duty ──raises WFHelm*/WFOrders* events──> Radio duty
     │                      ▲                                            ▲
     ├──alert events───> Crew alert (optional)      Ship events (dock, FTL, hull hits, crew down)
     └──fight branch──> Weapons (draw, reload, holster)
 Access & doors: a marked pathfinding edit, used by everything that walks.
```

### 1. Crew core

`WFCrewComponent` on the mob: `Duty` (string, mirrored to blackboard `WFCrewDuty`), `Post` (an entity to work at or
return to, usually a map marker), `Group` (string; everyone on a ship with the same group is one crew), `Role` (for
radio lines and events: Captain, Pilot, RadioOperator, Deckhand...), `Engagement`:

- `OnSight`: fights anyone its faction is hostile to or that the crew alert hands it. Deckhands and marines.
- `WhenAttacked`: fights only whoever damages it. Implemented as data: the mob sits in a faction with no hostiles
  (`WFCrewNeutral`), so `NearbyHostiles` only ever returns the aggro entries `NPCRetaliation` adds when it is hit, and
  the crew alert skips it. `attackMemoryLength` is 60 s rather than Frontier's 10 so it finishes the fight instead of
  hopping between helm and gun. Pilots and radio operators.

`WFCrewSystem` writes the blackboard keys on map init and when they change, raises `WFCrewDutyChangedEvent`, and on
`MobStateChangedEvent` raises `WFCrewMemberDownEvent(group, role, dead)` for anyone who cares (the radio operator, the
alert system, RES).

HTN root `WFCrewCompound`:

1. Fight: `WFDrawWeaponOperator`, then `RangedCombatCompound`, then `MeleeCombatCompound` (unchanged upstream).
2. One branch per duty, each gated by `KeyEqualsPrecondition WFCrewDuty == <Duty>`, whose first task is
   `WFHolsterWeaponOperator` (a no-op when nothing is drawn), then that duty's compound.
3. `IdleCompound`, with `IdleRange` small so idle crew drift around their post rather than the ship.

Rules every duty compound follows: its operators' `Shutdown` undoes whatever the operator holds (helm, a UI, an
anchor), it must be restartable from any state (Jeff might be anywhere when combat ends), and it never sets the
`Target` key. The planner does the switching.

**What a deckhand is.** The baseline crewman: duty `Guard`, which is "walk to your post and idle near it". He exists
to be the body count on a ship, to prove the core (branch switching, draw and holster, walking through doors) with the
simplest possible job, and to be the thing boarders fight. Variants are only loadout: sidearm, melee, rifle (the
`Marine`).

**What happens when a crewman dies.** The engine side: his HTN goes to sleep, so he stops planning; if he held the helm
upstream drops him that tick and `WFTakeHelmOperator`'s shutdown stops the steering, so the ship drifts; the alert
system stops counting him. The body stays where it fell with its full loadout, holstered or in hand, and that kit is
the loot (Mono's mob cleanup only removes mobs far from any grid and player, so bodies aboard are never tidied away).
`WFCrewMemberDownEvent` goes out with his role: the radio operator turns a captain's or pilot's death into a line,
nobody else's; RES counts crew losses toward "derelict". Critical is the same as dead for everything above except the
event flag; a downed crewman is out of the fight and, without someone treating him, dies within minutes.

### 2. Weapons and loadouts

Crew spawn with a real `Loadout` (`StartingGear`): uniform, armour if any, a holstered sidearm in the belt or suit
storage slot, a longarm on the back for the roles that have one, and two or three magazines or a box of rounds in the
pockets or belt. The convention that keeps this tractable, and that upstream's `TODO` comments were asking for:
weapons and ammo live in equipment slots and pockets, never inside a bag. `WFCrewWeaponComponent` on the mob records
`Drawn` (the entity in hand) and `HolsterSlot` (where it came from).

- `WFDrawWeaponOperator` (first task of the fight branch): pick the best weapon across equipment slots (longarm,
  then sidearm, then anything with `MeleeWeapon`), `TryUnequip` it and `TryPickup` into the active hand, record the
  slot. Fails if nothing is found, which lets the melee compound try with bare hands.
- `WFReloadOperator`: when `GunAmmoPrecondition` fails and the gun has an ammo provider, find a compatible magazine or
  round stack in pockets or belt, swap to a free hand, `InteractUsing` it on the gun (that is exactly what a player
  does), and go back to shooting. No ammo left: holster the empty gun and draw the next weapon, down to melee. This
  is the one genuinely new piece of combat behaviour and the one most likely to need polish.
- `WFHolsterWeaponOperator` (first task of every duty branch, and the fight branch's shutdown): `TryEquip` the drawn
  weapon back into `HolsterSlot`; if the slot is gone, keep holding it. Idempotent.

Trade-off accepted: Frontier's built-in infinite gun never runs dry and needs none of this. Real loadouts mean a crew
that fights too long ends up in melee, and that a boarding party is paid in rifles and magazines. Brackets and
rosters should be written knowing that the loadout is the loot table.

### 3. Pilot duty (First Officer Jeff)

`WFPilotDutyComponent`: `Console` (optional assigned helm, else the nearest powered `ShuttleConsoleComponent` on the
mob's grid), `Orders` and their parameters:

| Order | Behaviour | Steering |
|---|---|---|
| `Hold` | Take the helm, stop the ship, keep station. | `GoToRange` to own position, `Range` 5 |
| `GoTo` | Fly a list of waypoints, then `Hold` at the last; raise `WFPilotOrdersCompletedEvent`. | `GoToRange`, `InRangeMaxSpeed` = cruise speed |
| `Loiter` | Circle a point or a grid at a radius. | `Orbit` |
| `Follow` | Keep range on another grid (an escort). | `GoToRange` with coordinates relative to the target grid; `EntityCoordinates` follow for free |
| `Dock` | Fly to the target grid and dock by hand (below). | three phases, then `DockingSystem.Dock` |
| `Undock` | Undock and back off to a standoff point. | `DockingSystem.Undock`, then `GoToRange` |

`WFPilotDutyCompound`: pick the helm (`WFPickHelmOperator`, writes `WFCrewHelm`), `MoveToOperator` to it, then
`WFTakeHelmOperator`: `EnsureComp<PilotComponent>`, `AddPilot`, `ShipSteeringSystem.Steer` to the current target, and
stay in the operator while the pilot is attached. Its `Shutdown` calls `Stop` and `RemovePilot`, whatever the reason.
If the helm attachment is lost for any upstream reason (crit, power, destroyed console) the operator fails and the
planner restarts the compound: Jeff gets up, finds a helm, takes it again when he can.

Waypoint advancing, orbit centre updates and docking phases live in `WFPilotDutySystem.Update`, not in the HTN
operator, so a ship far from any player keeps flying while Jeff's HTN sleeps. The operator only owns the helm.
`SetOrders(uid, orders)` is the public API and raises `WFPilotOrdersChangedEvent`; `WFHelmTakenEvent` and
`WFHelmReleasedEvent` fire on attach and release.

What "can't pilot" means, and who enforces it:

- dead or down: upstream drops the pilot the tick he can't interact; steering stops in the operator's shutdown;
- fighting: Jeff is `WhenAttacked`, so only being shot gets him off the helm; then the fight branch wins the replan,
  `WFTakeHelmOperator` shuts down and the helm is released. Boarders who leave him alone get a ship that keeps flying,
  which is the correct trade: shoot the pilot and you stop the ship, at the price of a pilot shooting back;
- walking: impossible while attached, movement is blocked;
- no console, no power, console gone: upstream clears pilots, the operator fails, Jeff waits at the wreck of his helm
  under `IdleCompound` and retries every replan.

**Docking by hand.** The `Dock` order runs three phases in `WFPilotDutySystem`, reusing the FTL-dock maths:

1. **Plan**: `GetDockingConfig(ownGrid, targetGrid)` gives a dock pair and the final pose (position and angle) of our
   grid with the docks mated. Reject a config whose approach lane (a box from the final pose out 40 m along the target
   dock's outward normal, our grid's width wide) intersects another grid; try the next pair; none left, abort.
2. **Approach**: steer to a standoff pose 40 m out along that normal with `InRangeRotation` = the final angle, normal
   avoidance on, `InRangeMaxSpeed` 3. Arrive, settle until angular and linear velocity are near zero.
3. **Creep**: steer to the final pose with `AvoidCollisions` off (the target grid is 40 m away and avoidance would
   fight the approach), `InRangeMaxSpeed` 1.5, `Range` 0.3, heading held. Every tick check `CanDock(ownDock,
   targetDock)`; the moment it holds, `Dock`. `CanDock` wants 1.2 m and 15°, which is loose compared to the steerer's
   heading tolerance (about 2°), so position is the only thing that has to be good.

Abort rules: a collision in phase 3 (`FinishOnCollide`), or 60 s in phase 3 without `CanDock`, backs off to the standoff
and retries; three failures mean `Hold` at the standoff and a `WFPilotDockFailedEvent` (Jack says so on Shortband).
Both docks must stay free during the creep; a player docking there first is an abort. The FTL hop (`TryFTLDock`) is kept
only behind `wf.crew.dock_ftl_fallback`, off by default, for ships that must arrive docked no matter what. Expect this
phase to need tuning per hull: thruster placement decides how cleanly a grid can creep sideways, and a hull that can't
hold a heading at 1.5 m/s will not dock by hand at all.

### 4. Radio duty (Radio Officer Jack)

`WFRadioOperatorComponent` on the mob: a `Channels` map from message class to radio channel (`Local → Traffic`,
`Alert → Common`, optional `Faction → Nfsd`), `Callsign` (defaults to the grid's name, "Freighter XX-121"), a
`Cooldown` per message class, `Post` to stand at (`Hold` branch is just `MoveToOperator` to the post then idle).

`WFRadioOperatorSystem` is event-driven, not HTN-driven, so it works while Jack's HTN sleeps and needs no operator of
its own. It listens on Jack's grid and speaks through `RadioSystem.SendRadioMessage(jack, text, channel, jack)` with
Jack's name as sender, so killing or downing Jack ends the broadcasts literally (the system checks he is alive and not
critical before every line). Lines are for things that happen, never for the passage of time; there are no periodic
sitreps:

| Trigger | Class | Example |
|---|---|---|
| `DockEvent` with another grid | Local | "Freighter XX-121 docking at Caelestinus Central, port 3." |
| `UndockEvent` | Local | "Freighter XX-121 clear of Caelestinus Central, departing." |
| `FTLStartedEvent` / `FTLCompletedEvent` | Local | "Freighter XX-121 jumping." / "Freighter XX-121 arriving, bearing 040." |
| `WFPilotOrdersCompletedEvent` / `WFPilotDockFailedEvent` | Local | "Freighter XX-121 on station." / "Docking aborted, holding off the port." |
| First hostile hull hit (`ShipWeaponProjectile` from another grid) or crew alert raised | Alert | "Mayday, mayday, Freighter XX-121 under attack at 4120, -1870, hostile vessel Ratbag!" |
| A non-crew hostile mob on the grid | Alert | "We are being boarded!" |
| `WFCrewMemberDownEvent` for the captain | Alert | "Captain is down!" |
| `WFCrewMemberDownEvent` for the pilot | Alert | "Helm is down, we're adrift!" |
| No hostile on or firing at the grid for 2 min after an alert | Alert | "Freighter XX-121, hostiles gone, resuming course." |

One mayday per attack episode (an episode runs until the all-clear), one line per crew role going down. All text is
Fluent with the callsign, station, dock, bearing and hostile name as arguments and several variants per line, so
repeats aren't eaten by the radio system's duplicate filter. Jack carries `TelecomExemptComponent` (a ship radio set)
so Shortband works away from the outpost; Broadband already does. He also says a short local line (`SpeakOperator`
style, via `ChatSystem`) so people on the bridge hear him talk.

Jack has no special HTN beyond standing at his post; he is `WhenAttacked` so he stays on the radio while the fight is
elsewhere. If his ship has no Jeff, he still reports docks, jumps and attacks.

### 5. Crew alert (optional, shared awareness)

`WFCrewAlertSystem`: once a second, for each `Group` on a grid, look at members' blackboards for a `Target`. When one
appears, raise `WFCrewAlertEvent(group, hostiles)` and for every `OnSight` member: `AggroEntity(member, hostile)`,
raise `AggroVisionRadius` to cover the ship, `Replan`. The alert decays `AlertDecay` (60 s) after the last member lost
its target, restoring vision and raising `WFCrewAlertClearedEvent`. `WhenAttacked` members are left alone. Without
this system the crew still fights, one NPC at a time, as it sees things; with it the deckhands converge on a boarder,
which is what a ship's crew should do.

### 6. Access and doors

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

### 7. Spawning and tools

- `WFCrewSpawnPointComponent` map marker: `Role` (crew prototype), `Post` (itself by default), `Group`. `WFCrewSystem`
  spawns the mob on map init and fills in `WFCrewComponent`. Mappers place crew by placing markers.
- Naming: `RandomMetadata` with the species name datasets and a role title from Fluent, so the sender reads
  "First Officer Jeff Marlow".
- `wf_crew` admin command: `spawn <role> [post]`, `orders <npc> hold|goto x y|loiter x y r|follow <grid>|dock <grid>|
  undock`, `duty <npc> <duty>`, `list [grid]`. An admin verb "Orders..." on a crew NPC opens the same choices.
- Everything on `WFCrewComponent`, `WFPilotDutyComponent`, `WFCrewWeaponComponent` and `WFRadioOperatorComponent` is
  editable in VV.

## NPCs to start with

In build order; each one proves a compartment.

1. **Deckhand** (`WFCrewDeckhand`, duty `Guard`, `OnSight`). Stands at a post, idles nearby, draws and fights what it
   sees, holsters and goes back. Proves the core compound, the shutdown discipline and draw/holster with the simplest
   job. Variants by loadout: sidearm, melee, rifle (`WFCrewMarine`, with armour).
2. **First Officer** (`WFCrewPilot`, duty `Pilot`, `WhenAttacked`). Jeff. Proves helm attach and release, orders,
   waypoints, the pilot-and-steerer coexistence, "down means drifting", and then docking by hand.
3. **Radio Officer** (`WFCrewRadioOperator`, duty `Hold` at the radio post, `WhenAttacked`). Jack. Proves event-driven
   speech and channel routing; also the first thing players will notice.
4. **Captain** (`WFCrewCaptain`). A radio officer whose system also issues orders: on alert, `SetOrders(Hold)` if his
   `HeaveTo` flag is set, otherwise leave the pilot flying; on alert cleared, restore the previous orders. One
   component (`WFCaptainComponent`) with those two reactions; everything else is Jack's code. Gives a ship one entity
   whose death matters, and the "Captain is down!" line something to mean.
5. **Gunner** (`WFCrewGunner`, duty `Gunnery`), later. Takes a gunnery console the way Jeff takes the helm and fires
   the ship's `FireControllable` guns at the crew's hostiles through the same `FireControlSystem.AttemptFire` path
   Mono's ship AI uses; no gunner at the console, no ship guns. Waits on a faction-aware target source.

Not now: engineers (repair is a deep rabbit hole), medics (upstream medibot logic exists but patient selection is a
project), anyone who needs to manage a bag.

## Risks

- **Reloading.** The one new combat behaviour. If `WFReloadOperator` misbehaves, the fallback is already in the
  design (holster, draw the next weapon, melee), so a broken reload degrades to a short fight, not a frozen NPC.
- **Docking by hand.** Depends on each hull creeping straight at 1.5 m/s. Build it against two or three freighter hulls
  before promising it for any ship; the FTL fallback stays behind a cvar for the ones that can't.
- **Sleep.** NPC HTNs pause with no player within 32 tiles. Steering, docking phases and radio are system-driven and
  keep working; the HTN only owns attach, release, drawing and walking. If attach itself has to happen unobserved (a
  ship spawned far away), give the pilot prototype `SleepPlayerCheckRangeOverride`, as Mono's AI cores do.
- **Combat compounds on a moving ship.** Steering is grid-relative and should be fine, but `MoveToOperator` under
  acceleration hasn't been exercised; the first live test is Jeff's own ship under way with a boarder.
- **Pry-happy crew.** Until the access edit lands, a crew on a stock ship pries its own doors. Ship this with the edit
  or with readerless interior doors; don't ship without either.
- **Duplicate radio lines.** The radio system drops an identical message already in flight. Variants and arguments
  in every template, and the cooldown per message class.

## Tests

`Content.IntegrationTests/Tests/_WF/NpcCrew/`: a test grid with a helm, a post, a dock and a door; a second grid with a
matching dock.

- Core: a deckhand with a hostile spawned in view plans the fight branch with its sidearm in hand; with the hostile
  removed, within two replans it is back on the duty branch with the sidearm back in its slot.
- Weapons: an empty gun with a magazine in a pocket is reloaded; with no ammo anywhere the NPC ends up in melee.
- Pilot: Jeff spawned with `GoTo` acquires `PilotComponent` and `ShipSteererComponent` within a few seconds; the grid
  gains velocity toward the waypoint; killing Jeff removes both and the grid stops being driven; a hostile shooting
  Jeff releases the helm, a hostile standing next to him does not.
- Docking: a `Dock` order against the second grid ends with the two docks docked within 90 s on the test hull.
- Radio: a `DockEvent` on Jack's grid produces one message on `Traffic` with the callsign and station name; a hull hit
  from another grid produces one mayday on `Common` and no second one; the captain's death produces one line; a dead
  Jack produces none.
- Doors (with the access edit): a crew NPC with access paths through a reader door without prying it; one without
  access pries.

No `Destructive` pool settings.

## Decisions taken

- Pilots and radio operators fight only when attacked; deckhands and marines fight on sight and take crew alerts.
- Real loadouts, holstered outside a fight, drawn for it, reloaded from pockets; the loadout is the loot.
- Docking is done by hand, in three phases on top of the FTL-dock maths; the FTL hop is a cvar fallback, off.
- The radio reports events only (docking, jumps, mayday, boarded, captain down, helm down, all clear). No sitreps.

## Open questions

1. **How much ammo?** Assumed two or three magazines per crewman. More means longer fights and richer loot; less means
   boarders face melee sooner.
2. **Does being hit by a ship gun count as "attacked" for Jeff?** Assumed no: hull hits are the deckhands' and the
   radio's business; Jeff keeps flying until someone shoots *him*. Yes would stop every freighter at the first volley.
3. **Should the radio operator also stay quiet about non-faction ships docking?** Assumed he reports every dock on
   Shortband; it is local range and that is the flavour.
4. **Should NPC crew count as crew for `ShipAccess`?** Assumed yes via the allow list. The alternative is a blanket
   "NPCs ignore ship locks", which players will dislike the first time a hostile boarder uses it.
