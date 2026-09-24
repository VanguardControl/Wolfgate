# Wolfgate grid power: SS13's "Make all areas powered/unpowered"

## Window
wf-admin-tab-grid-power = Grid Power
wf-grid-power-title = Grid Power
wf-grid-power-info = Unpowered drains every SMES, substation and APC battery and switches APC breakers off. Crews recover by flipping breakers back on. Powered fills them and turns everything back on.
wf-grid-power-all-label = All grids:
wf-grid-power-all-powered = All areas powered
wf-grid-power-all-unpowered = All areas unpowered
wf-grid-power-powered = Powered
wf-grid-power-unpowered = Unpowered
wf-grid-power-confirm = Confirm?
wf-grid-power-search = Search grids or maps...
wf-grid-power-announce = Announce
wf-grid-power-refresh = Refresh
wf-grid-power-row = {$map} | APC breakers on: {$on}/{$apcs} | Batteries: {$batteries} | Stored: {$percent}%
wf-grid-power-count = Showing {$shown} of {$total} grids
wf-grid-power-empty = No grids with power equipment found.

## Announcements (SS13 wording)
wf-grid-power-target-all = the sector
wf-grid-power-off-sender = Critical Power Failure
wf-grid-power-on-sender = Power Systems Nominal
wf-grid-power-off-announcement = Abnormal activity detected in {$target}'s powernet. As a precautionary measure, its power will be shut off for an indeterminate duration.
wf-grid-power-on-announcement = Power has been restored to {$target}. We apologize for the inconvenience.

## gridpower command
cmd-gridpower-desc = SS13's "Make all areas powered/unpowered" for one grid or every grid.
cmd-gridpower-help = Usage: {$command} <on|off> <all|grid entity> [announce: true/false]
cmd-gridpower-invalid-args = Expected on or off, a grid entity or "all", and optionally true or false for the announcement.
cmd-gridpower-invalid-grid = "{$grid}" isn't a grid.
cmd-gridpower-done = {$state} {$count} grids.
cmd-gridpower-powered = Powered
cmd-gridpower-unpowered = Unpowered
cmd-gridpower-hint-state = <on|off>
cmd-gridpower-hint-target = <all|grid entity>
cmd-gridpower-hint-announce = [announce: true/false]
