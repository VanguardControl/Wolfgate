# Notices to a ship crossing a border
sector-control-notice-sender = Sector Watch
sector-control-notice-entered = {$ship} has entered {$faction} space at {$callsign}.
sector-control-notice-left = {$ship} has left {$faction} space. Now at {$callsign}.

# wf_sector command
cmd-wf_sector-desc = Shows and edits faction territory on the sector map.
cmd-wf_sector-help = Usage: {$command} status | claim <faction> <q> <r> | free <q> <r> | contest <faction> <q> <r> <minutes> | clear [faction] | callsign <x> <y>
cmd-wf_sector-hint-sub = Subcommand
cmd-wf_sector-hint-faction = Faction id
cmd-wf_sector-hint-q = Cell q
cmd-wf_sector-hint-r = Cell r
cmd-wf_sector-hint-minutes = Minutes until the contest ends
cmd-wf_sector-hint-x = Map x in metres
cmd-wf_sector-hint-y = Map y in metres
cmd-wf_sector-status-header = Sector control: enabled {$enabled}, map {$map}, cell size {$size} m.
cmd-wf_sector-status-faction = {$faction} ({$name}): {$cells} cells {$callsigns}
cmd-wf_sector-status-contest = Contested {$callsign} by {$faction}, {$seconds} s left.
cmd-wf_sector-claimed = {$faction} now holds {$callsign}.
cmd-wf_sector-claim-failed = {$faction} cannot claim {$callsign}.
cmd-wf_sector-freed = {$callsign} is free.
cmd-wf_sector-not-held = {$callsign} is not held or contested.
cmd-wf_sector-contested = {$faction} contests {$callsign}.
cmd-wf_sector-contest-failed = {$faction} cannot contest {$callsign}.
cmd-wf_sector-bad-minutes = Not a number of minutes: {$arg}
cmd-wf_sector-cleared = Cleared {$count} cells.
cmd-wf_sector-bad-position = Not a position: {$x} {$y}
cmd-wf_sector-callsign = {$callsign} is cell ({$q}, {$r}), centre at {$x} {$y}.
cmd-wf_sector-unknown-faction = Unknown faction: {$faction}
cmd-wf_sector-bad-cell = Not a cell: {$q} {$r}
