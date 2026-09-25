cmd-wfcavern-desc = Inspect caverns, visit their gates and carve mouths by hand.
cmd-wfcavern-help = Usage: { $command } <list | tp <planet> [pad|mouth] | mouths <planet> | open>
cmd-wfcavern-disabled = Caverns are off. Set wf.caverns to true.
cmd-wfcavern-invalid-args = Expected one of: list, tp <planet> [pad|mouth], mouths <planet>, open.
cmd-wfcavern-unknown-planet = No built planet network named "{ $planet }".
cmd-wfcavern-no-cavern = { $planet } has no cavern, or its cavern has no gate.
cmd-wfcavern-empty = Nothing to list.
cmd-wfcavern-row = { $planet } | cavern: { $cavern } | mouths: { $mouths } | below: { $players }
cmd-wfcavern-row-none = none
cmd-wfcavern-mouth-row = { $kind } | origin: { $origin } | size: { $size } | climb tile: { $climb }
cmd-wfcavern-tp-done = Moved to the gate { $target } of { $planet }.
cmd-wfcavern-no-map = Attach to an entity first.
cmd-wfcavern-not-ground = Stand on a planet's ground above a cavern first.
cmd-wfcavern-open-done = Carved a mouth with its hole at { $origin }.
cmd-wfcavern-open-refused = Can't carve a mouth here: { $reason ->
    [grid] a ship or debris is over it.
    [built] something built stands in the hole.
    [mob] someone is standing in the hole.
   *[mouth] there is a mouth there already.
}
cmd-wfcavern-hint-sub = <list|tp|mouths|open>
cmd-wfcavern-hint-planet = <planet name>
cmd-wfcavern-hint-target = <pad|mouth>
