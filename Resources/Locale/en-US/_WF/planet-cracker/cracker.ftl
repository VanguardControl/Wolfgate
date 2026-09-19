## Machines
wf-centrifuge-window-title = Gravitic centrifuge

# The centrifuge's own machine window. The dial's wf-centrifuge-spin and wf-centrifuge-load stay for the crack
# console, which still draws them by hand; this window shows the same numbers in Labels instead.
wf-centrifuge-window-spin = Rotor spin:
wf-centrifuge-window-load = Gravity load:
wf-centrifuge-window-spin-value = { $percent }%
wf-centrifuge-window-load-value = { $mass } / { $capacity }
wf-centrifuge-window-load-none = No rated capacity
wf-centrifuge-window-rotor-full = ROTOR AT FULL
wf-centrifuge-window-rotor-spinning = SPINNING UP
wf-centrifuge-window-rotor-stopped = ROTOR STOPPED
wf-centrifuge-window-rotor-hint-full = The rotor will hold a cut.
wf-centrifuge-window-rotor-hint-spinning = A cut needs full spin before it will hold.
wf-centrifuge-window-rotor-hint-stopped = Switch the machine on to spin the rotor up.

wf-projector-upgrade-crack-time = crack time
wf-projector-examine-multiplier = Rated at { $percent }% of stock crack time.
wf-projector-examine-broken = The emitter housing is cracked.

# One per WFProjectorState, for the crack console's projector panel; it never shows a server-sent string.
wf-projector-state-off = OFFLINE
wf-projector-state-idle = IDLE
wf-projector-state-charging = CHARGING
wf-projector-state-firing = FIRING
wf-projector-state-broken = BROKEN

## Berth
wf-berth-examine = Berth { $width } by { $height } tiles, centred { $distance } tiles out.

## Anchor capacity
wf-transport-capacity-examine = Rated for { $capacity } anchor(s); { $aboard } aboard.
wf-transport-capacity-exceeded = Gravity generator overloaded: too many anchors aboard.

## Crack console
wf-crack-console-title = Crack control
wf-crack-console-state = Stage: { $state }
wf-crack-console-crack-remaining = Cut remaining: { $time }
wf-crack-console-crack-paused = CUT SUSPENDED — anchor damaged
wf-crack-console-grace = HULL LOSS IN { $time }
wf-crack-console-grace-nominal = Hold nominal.
wf-crack-console-disconnect = DISCONNECT ARMED — { $time }
wf-crack-console-evac = CHUNK RELEASE IN { $time }
wf-crack-console-btn-target = TARGET PAIR
wf-crack-console-btn-untarget = CLEAR TARGET
wf-crack-console-btn-begin = BEGIN CRACK

# One per WFCrackState member.
wf-crack-console-state-idle = Idle
wf-crack-console-state-surveying = Surveying
wf-crack-console-state-anchors-placed = Anchors placed
wf-crack-console-state-anchors-locked = Anchors locked
wf-crack-console-state-cracking = Cracking
wf-crack-console-state-cracked = Cracked
wf-crack-console-state-disconnecting = Disconnecting
wf-crack-console-state-released = Released
wf-crack-console-state-falling = Falling

## Stage strip cells; the full names above are the tooltips.
wf-crack-console-stage-short-idle = Idle
wf-crack-console-stage-short-surveying = Survey
wf-crack-console-stage-short-anchors-placed = Placed
wf-crack-console-stage-short-anchors-locked = Locked
wf-crack-console-stage-short-cracking = Cracking
wf-crack-console-stage-short-cracked = Cracked
wf-crack-console-stage-short-disconnecting = Disconnect
wf-crack-console-stage-short-released = Released
wf-crack-console-stage-short-falling = Falling

# One per WFCrackFailure flag: what the grace countdown is counting down for.
wf-crack-console-fail-centrifuge = Centrifuge below full spin
wf-crack-console-fail-projectors-short = Not enough gravity projectors
wf-crack-console-fail-projector-power = A gravity projector has lost power
wf-crack-console-fail-projector-broken = A gravity projector is broken

# One per WFCrackBlocker flag, eleven of them: the hover list on a greyed BEGIN CRACK must name the
# actual fault, so no key covers two faults.
wf-crack-console-blocker-wrong-state = Not in the anchors-locked stage
wf-crack-console-blocker-no-pair = No anchor pair belongs to this ship
wf-crack-console-blocker-not-targeted = The pair has not been targeted
wf-crack-console-blocker-not-aligned = The berth is outside alignment tolerance
wf-crack-console-blocker-obstructed = Another ship is over the destination
wf-crack-console-blocker-in-grid-network = The hull is linked to another grid
wf-crack-console-blocker-centrifuge-missing = No gravitic centrifuge aboard
wf-crack-console-blocker-centrifuge-not-full = The centrifuge is not at full spin
wf-crack-console-blocker-projectors-short = Not enough gravity projectors aboard
wf-crack-console-blocker-projectors-unpowered = A gravity projector has no power
wf-crack-console-blocker-projectors-broken = A gravity projector is broken
wf-crack-console-blocker-planet-cracked = This planet has already been cracked

# Server refusal popups.
wf-crack-console-refuse-network = Refused: the hull is linked to another grid and cannot be moved.
wf-crack-console-refuse-obstructed = Refused: another ship sits over the destination.
wf-crack-console-refuse-alignment = Refused: the berth is too far from the cut circle.
wf-crack-console-refuse-no-abort = The cut cannot be called off once it has begun.
wf-crack-console-refuse-no-shuttle = Refused: this hull has no thruster control and could never be released.
wf-crack-console-refuse-external-lock = Refused: this hull is already force-anchored by another system.

wf-crack-console-offset = Offset { $x }, { $y } — { $distance } tiles
wf-crack-console-aligned = Aligned
# Reserved for a console-only readout. The console hosts the same WFCentrifugeDial as the machine window, so both
# hosts draw these two through wf-centrifuge-spin and wf-centrifuge-load instead; edit those to change what is shown.
wf-crack-console-spin = Spin { $percent }%
wf-crack-console-load = Load { $mass } / { $capacity }
wf-crack-console-abort = Spinning down: { $seconds } s
wf-crack-console-abort-to = Returning to { $state }

## Disconnect protocol (F7)
wf-crack-announce-sender = Crack control
wf-crack-disconnect-armed = First anchor switched off. Switch the second off within { $seconds } seconds or the first re-arms.
wf-crack-disconnect-warning = Disconnect window closing: { $seconds } seconds.
wf-crack-disconnect-lapsed = The disconnect window lapsed. The first anchor has re-armed.
wf-crack-disconnect-committed = Disconnect confirmed. Chunk release in { $seconds } seconds — clear the chunk.
wf-crack-evac-warning = Chunk release in { $seconds } seconds.
wf-crack-released = The chunk is away. Gravity lock released.

## Centrifuge dial
wf-centrifuge-spin = Spin { $percent }%
wf-centrifuge-load = Load { $mass } / { $capacity }
wf-centrifuge-at-full = AT FULL

## wfcracker command
cmd-wfcracker-desc = Spawn the code-built planet cracker test grids.
cmd-wfcracker-help = Usage: { $command } spawn <cracker | transport> | { $command } state <stage> | { $command } begin drill | { $command } complete <crack | drill> | { $command } fissure <ring | surge> | { $command } disconnect | { $command } rearm | { $command } release | { $command } fall | { $command } extract | { $command } drop | { $command } veins | { $command } mine
cmd-wfcracker-invalid-args = Expected: spawn <cracker | transport>, state <stage>, begin drill, complete <crack | drill>, fissure <ring | surge>, disconnect, rearm, release, fall, extract, drop, veins or mine.
cmd-wfcracker-unknown-kind = No test grid named "{ $kind }". Try cracker or transport.
cmd-wfcracker-no-map = Attach to an entity on a map first.
cmd-wfcracker-spawned = Built { $kind } as { $grid } on map { $map }.
cmd-wfcracker-spawned-with-transport = Built cracker { $cracker } with transport { $transport } docked, on map { $map }.
cmd-wfcracker-state-set = Set { $grid } to { $state }.
cmd-wfcracker-unknown-state = No crack stage named "{ $state }".
cmd-wfcracker-completed = Forced the cut on { $grid } to finish.
cmd-wfcracker-disconnected = Switched off both anchors of { $grid }.
cmd-wfcracker-drilled = Finished drilling both anchors of { $grid }.
cmd-wfcracker-no-cracker = Stand on a grid that has a planet cracker first.
cmd-wfcracker-hint-sub = <spawn|state|begin|complete|fissure|disconnect|rearm|release|fall|extract|drop|veins|mine>
cmd-wfcracker-hint-kind = <cracker|transport>
cmd-wfcracker-hint-state = <crack stage>
cmd-wfcracker-hint-target = <crack|drill>
cmd-wfcracker-hint-begin = <drill>
cmd-wfcracker-hint-fissure = <ring | surge>
cmd-wfcracker-rearmed = Re-armed both anchors of { $grid }.
cmd-wfcracker-released = Forced { $grid } to release its chunk.
cmd-wfcracker-began-drill = Started both drills on { $grid }.
cmd-wfcracker-fissured = Spread { $count } fissure ring(s).
cmd-wfcracker-surged = The extraction surge spawned { $count } threats.
cmd-wfcracker-no-fissures = Neither anchor carries a fissure spawner.
