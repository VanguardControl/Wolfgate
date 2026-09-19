## Placement
wf-anchor-not-ground = The anchor only grips bare planet surface, not a deck.
wf-anchor-no-room = The anchor needs three by three tiles of clear, solid ground.
wf-anchor-lost-room = Something moved into the rig's footprint; it will not sit.
# Shown for Drilling and Locked. Off has its own line below: it is already switched off, and there is no player re-arm.
wf-anchor-locked-unwrench = The anchor is drilled in. Switch it off first.
wf-anchor-off-unwrench = The anchor is switched off but still drilled into the crust.

## Examine
wf-anchor-examine-state = It is { $state }.
wf-anchor-examine-unpaired = No partner anchor within { $min } to { $max } tiles.
wf-anchor-examine-pair = Paired at { $distance } tiles; the cut would be { $diameter } tiles across.
wf-anchor-examine-drill = Drilling: { $percent }% complete.
wf-anchor-examine-damaged = The housing is split and sparking.

## States
wf-anchor-state-loose = loose
wf-anchor-state-deployed = wrenched down
wf-anchor-state-paired = paired
wf-anchor-state-drilling = drilling
wf-anchor-state-locked = locked
wf-anchor-state-off = switched off
wf-anchor-state-broken = broken

## Verbs
wf-anchor-verb-drill = Start drilling
wf-anchor-verb-drill-unpaired = It has no partner anchor yet.
wf-anchor-verb-drill-busy = It is already running.
wf-anchor-verb-off = Switch off
wf-anchor-verb-off-not-locked = It has not locked yet.
wf-anchor-verb-off-refused = { $reason }

## Crate
wf-anchor-crate-not-ground = Unpack the anchor on bare planet surface, not on a deck.
wf-anchor-crate-no-room = There is no room here for a three by three rig.
wf-anchor-crate-in-container = Take the crate out first.
wf-anchor-crate-examine = Pry it open on a planet surface to deploy the anchor.

## Disconnect (F7)
# These three are substituted RAW into wf-anchor-verb-off-refused, so each must be a complete sentence.
wf-anchor-off-not-cracked = the chunk is not cut free yet.
wf-anchor-off-aborting = the projectors are spinning down.
wf-anchor-off-not-on-chunk = this anchor is still on the surface.
