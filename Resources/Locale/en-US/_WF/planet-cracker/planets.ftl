## Map and marker names
wf-planet-orbit-map-name = { $planet } orbit
wf-planet-orbit-marker-name = { $planet } orbital marker
wf-planet-network-name = { $planet } planet network
wf-planet-asclepiu-surface = Asclepiu

## FTL
wf-shuttle-console-in-transit = Cannot engage FTL while changing altitude.

## Shuttle console orbit controls
wf-shuttle-console-enter-orbit = Enter orbit: { $planet }
wf-shuttle-console-leave-orbit = Leave orbit: { $planet }
wf-shuttle-console-orbit-none = No planet in range
wf-orbit-no-hull = This console is not aboard a flyable hull.
wf-orbit-no-network = { $planet } has no orbit layer to drop into.
wf-orbit-no-sector = This orbit layer has no sector body to return to.
wf-orbit-not-in-sector = { $planet } is not in this system.
wf-orbit-not-in-orbit = This hull is not in planet orbit.
wf-orbit-out-of-range = Too far from { $planet } for orbital insertion.
wf-orbit-refused = Orbital insertion refused.

## wfplanet command
cmd-wfplanet-desc = Build, inspect and tear down Wolfgate planet networks.
cmd-wfplanet-help = Usage: { $command } <list | build <planet> | delete <planet> | spawn <surface id> | system <starSystem id> | tp <planet>>
cmd-wfplanet-disabled = Planet networks are off. Set wf.planet_networks to true.
cmd-wfplanet-invalid-args = Expected one of: list, build, delete, spawn, system, tp.
cmd-wfplanet-unknown-planet = No sector planet named "{ $planet }".
cmd-wfplanet-unknown-surface = No wfPlanetSurface prototype "{ $surface }".
cmd-wfplanet-unknown-system = No starSystem prototype "{ $system }".
cmd-wfplanet-no-surface = { $planet } has no Wolfgate surface.
cmd-wfplanet-already-built = { $planet } already has a network ({ $network }).
cmd-wfplanet-not-built = { $planet } has no network.
cmd-wfplanet-built = Built { $layers } layers for { $planet }: network { $network }, orbit { $orbit }.
cmd-wfplanet-build-failed = Failed to build a network for { $planet }; see the server log.
cmd-wfplanet-deleted = Deleted the network for { $planet }.
cmd-wfplanet-row = { $planet } | surface: { $surface } | network: { $network }
cmd-wfplanet-row-none = none
cmd-wfplanet-empty = No sector planets registered.
cmd-wfplanet-system-set = Set { $system } on map { $map }.
cmd-wfplanet-no-map = Run this with a map in mind: attach to an entity on the sector map first.
cmd-wfplanet-tp-done = Moved to the orbit layer of { $planet }.
cmd-wfplanet-hint-sub = <list|build|delete|spawn|system|tp>
cmd-wfplanet-hint-planet = <planet name>
cmd-wfplanet-hint-surface = <wfPlanetSurface id>
cmd-wfplanet-hint-system = <starSystem id>
