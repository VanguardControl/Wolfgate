# ShipAccess

Per-card access control for purchased ships, working the way a normal airlock does: the doors read the ID
cards a person carries. Whoever carries the ship's deed card is its owner; the owner keeps an allow list of ID
cards, and in Faction mode anyone carrying an ID card of the ship's company may also enter. While a ship is
locked, only those cards open its doors and lockers. Unlocked ships stay open to everyone, as before.

The card is the key, never the person or the account behind it: steal a listed card and you get in, hand the
deed over and you hand the ship over, and a clone without their card is a stranger. Lists hold card entities,
like Mono's guest cards, so they are round state and are not saved with the grid. The tab shows the buyer's
name for reference only.

A refusal at a door plays the door's normal deny state and sound, and the ship check is skipped while the
door's own access reader is hacked (its access wire pulsed or cut) or the airlock is on emergency access, so a
ship door can be broken into the way any airlock can. An emag opens an airlock outright and never reaches the
check.

Players use the Access tab on the shuttle console: it shows who the ship is registered to, its mode and lock,
and lets the deed holder lock the ship, add the card of a humanoid standing near the console, remove cards and
mark them as builders' (stored only for now). A ship bought before this module existed gets its access record
the first time its deed holder edits it. Buying a ship locks it when `wf.shipaccess.lock_new_ships` is true
(the default). Doors and lockers added to a ship later get a reader that mirrors the lock.

The old console verbs keep working and map onto this: granting guest access also puts the guest's card on the
allow list, resetting guest access also clears the allow list, and the lock verb flips the grid's lock.

## Per-door rules

Each door can override the ship rule with `WFDoorAccessRuleComponent`, saved with the grid: Ship default,
Deed only, Chosen IDs (the deed plus allow-listed cards ticked for that door), Code, Chosen IDs or code,
Public and Sealed. Every rule but Ship default applies whether or not the ship is locked, so a ruled
door keeps its reader enabled on an unlocked ship. Sealed closes and bolts the door for everyone, the owner
included; picking another rule unbolts it, and bolts that sealing added are removed again. Lockers keep the
ship rule. Cards dropped from the allow list are dropped from every door they were ticked on.

The Access tab shows the ship outline with each door as a node in its rule's colour (the same nav map the Ship
tab uses, sized to the tab instead of the nav map's fixed square) and a legend; clicking a node selects the door
and shows its name, rule and, for the people rules, checkboxes over the allow list. Firelocks are left out, on
the diagram and at the server. Only the owner can edit; others get a read-only view. The diagram is built
client-side from the doors the client knows about, so doors outside the client's view range on a very large
ship appear once the player has been near them.

Decisions taken while building F3.2: a Code door admits only the deed at the reader (chosen cards count
only under Chosen IDs or code); Sealed is applied on map init as well, so a saved sealed door comes back
bolted; bolts need power, as everywhere in the game, so an unpowered sealed door is refused at the reader only
and bolts the moment it is powered; `ShipViewControl` was unsealed so the diagram reuses its hull drawing and
fit.

## Codes and the keypad

The deed holder can set a four-digit ship code and, on any Code or Chosen-IDs-or-code door, a code of the
door's own; both open such a door. Someone the door's rule does not admit right-clicks it and picks Enter Code, which
opens a keypad. The server repeats the verb's checks, since a client can send a code without it: the person
must be able to act and reach the door within two and a half tiles with nothing solid in between, and the door
must still have a code rule and be closed and unbolted. A matching code stands in for the ship check only; the
door then opens through the normal door path, so it still needs power, must not be welded, and keeps its own
ID access. Wrong codes count per person per
ship: five inside ten minutes lock that person out of every keypad on that ship for fifteen minutes. Every
miss and lockout is admin-logged, and the owner's Access tab shows an alert line with the failed attempts and
the people locked out. Changing a code never affects anyone already inside. Reselling a ship on the used market
clears the ship code, every door code and rule, and lifts any seal; a seal lifted without power unbolts when
power returns.

Codes never travel on a networked component. The ship code and the lockout table live in the server-only
`WFShipAccessCodeComponent` on the grid; a door's own code is a server-only `WFDoorCodeComponent` on the door,
so it saves and moves with the door, and the networked rule component only carries `HasOwnCode`. The owner's
console asks for the codes when the Access tab opens and gets them in a directed network event; the tab
forgets them when it closes or switches ship, shows them masked with a Show toggle, and nobody else ever
receives them.

Decisions taken while building F3.3: the Enter Code verb sits on the door's rule component, because the
prying system already owns the alternative-verb pair on `DoorComponent`; a door with its own code takes the
ship code as well, as the design table says ("its own, or the ship code"); misses and lockouts are counted per
character name, like everything else here, so an NPC can lock itself out too; lockouts are round state and are not
saved with the grid; the lockout alert only reaches the owner, by the same directed event path as the codes.

Entry points: `WFShipAccessComponent` (grid), `WFDoorAccessRuleComponent` (door), `WFShipAccessCodeComponent`
and `WFDoorCodeComponent` (server-only codes), `WFShipAccessSystem` (shared decision, hooked into
`ShipAccessReaderSystem.HasShipAccess`), `WFShipAccessServerSystem` (purchase, lock, allow list, door rules,
codes, keypad verb, console messages, verb bridge), `ShipAccessScreen` (the console tab),
`ShipAccessDoorMapControl` (the door diagram), `WFShipAccessClientSystem` and `ShipAccessKeypadWindow` (the
keypad).

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/ShipAccess/WFShipAccessCodeComponent.cs`](WFShipAccessCodeComponent.cs)
- [`Content.Server/_WF/ShipAccess/WFShipAccessServerSystem.Codes.cs`](WFShipAccessServerSystem.Codes.cs)
- [`Content.Server/_WF/ShipAccess/WFShipAccessServerSystem.Console.cs`](WFShipAccessServerSystem.Console.cs)
- [`Content.Server/_WF/ShipAccess/WFShipAccessServerSystem.cs`](WFShipAccessServerSystem.cs)
- [`Content.Server/_WF/ShipAccess/WFShipAccessServerSystem.Doors.cs`](WFShipAccessServerSystem.Doors.cs)

### Shared

- [`Content.Shared/_WF/ShipAccess/WFDoorAccessRuleComponent.cs`](../../../Content.Shared/_WF/ShipAccess/WFDoorAccessRuleComponent.cs)
- [`Content.Shared/_WF/ShipAccess/WFShipAccessComponent.cs`](../../../Content.Shared/_WF/ShipAccess/WFShipAccessComponent.cs)
- [`Content.Shared/_WF/ShipAccess/WFShipAccessEvents.cs`](../../../Content.Shared/_WF/ShipAccess/WFShipAccessEvents.cs)
- [`Content.Shared/_WF/ShipAccess/WFShipAccessSystem.cs`](../../../Content.Shared/_WF/ShipAccess/WFShipAccessSystem.cs)

### Client

- [`Content.Client/_WF/ShipAccess/ShipAccessDoorMapControl.cs`](../../../Content.Client/_WF/ShipAccess/ShipAccessDoorMapControl.cs)
- [`Content.Client/_WF/ShipAccess/ShipAccessKeypadWindow.xaml`](../../../Content.Client/_WF/ShipAccess/ShipAccessKeypadWindow.xaml)
- [`Content.Client/_WF/ShipAccess/ShipAccessKeypadWindow.xaml.cs`](../../../Content.Client/_WF/ShipAccess/ShipAccessKeypadWindow.xaml.cs)
- [`Content.Client/_WF/ShipAccess/ShipAccessScreen.xaml`](../../../Content.Client/_WF/ShipAccess/ShipAccessScreen.xaml)
- [`Content.Client/_WF/ShipAccess/ShipAccessScreen.xaml.cs`](../../../Content.Client/_WF/ShipAccess/ShipAccessScreen.xaml.cs)
- [`Content.Client/_WF/ShipAccess/ShuttleConsoleBoundUserInterface.ShipAccess.cs`](../../../Content.Client/_WF/ShipAccess/ShuttleConsoleBoundUserInterface.ShipAccess.cs)
- [`Content.Client/_WF/ShipAccess/ShuttleConsoleWindow.ShipAccess.cs`](../../../Content.Client/_WF/ShipAccess/ShuttleConsoleWindow.ShipAccess.cs)
- [`Content.Client/_WF/ShipAccess/WFShipAccessClientSystem.cs`](../../../Content.Client/_WF/ShipAccess/WFShipAccessClientSystem.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/ShipAccess/ShipAccessCodeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipAccess/ShipAccessCodeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipAccess/ShipAccessDoorRuleTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipAccess/ShipAccessDoorRuleTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipAccess/ShipAccessTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipAccess/ShipAccessTest.cs)

### Localization

- [`Resources/Locale/en-US/_WF/ShipAccess/ship-access.ftl`](../../../Resources/Locale/en-US/_WF/ShipAccess/ship-access.ftl)

## Non-modular edits

- [`Content.Client/Shuttles/BUI/ShuttleConsoleBoundUserInterface.cs`](../../../Content.Client/Shuttles/BUI/ShuttleConsoleBoundUserInterface.cs)
- [`Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml`](../../../Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml)
  - xmlns:access for the access tab
  - access tab button
- [`Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml.cs): access tab
- [`Content.Client/UserInterface/Controls/MapGridControl.xaml.cs`](../../../Content.Client/UserInterface/Controls/MapGridControl.xaml.cs)
  - virtual, so the door map can centre its drawing in a control of any size
  - virtual, so the door map can fit the hull to its own shorter side
- [`Content.Server/Shuttles/Systems/ShuttleConsoleLockSystem.cs`](../../Shuttles/Systems/ShuttleConsoleLockSystem.cs)
  - a guest's card also joins the allow list
  - resetting guests also empties the allow list, and that alone counts as a reset
  - Locked on the grid is the source of truth and flips the readers itself
- [`Content.Shared/_Mono/Shipyard/ShipAccessReaderSystem.cs`](../../../Content.Shared/_Mono/Shipyard/ShipAccessReaderSystem.cs)
  - a door whose own access reader is hacked, or on emergency access, skips the ship check, and a refusal plays the door's deny state as a normal airlock does
  - per-person access (owner, allow list, faction) is decided before the deed rules

<!-- WOLFGATE-GENERATED END -->
