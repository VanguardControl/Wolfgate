wf-encounter-ship-name = {$vessel} {$designation}
wf-encounter-name-convoy = Convoy {$designation}

# wf_encounter command
cmd-wf_encounter-desc = Lists, spawns and ends encounters, and runs or pauses the encounter scheduler.
cmd-wf_encounter-help = Usage: {$command} list | spawn <prototype> [distance] | end <encounter> | schedule | pause | resume
cmd-wf_encounter-hint-sub = <subcommand>
cmd-wf_encounter-hint-prototype = <encounter prototype>
cmd-wf_encounter-hint-distance = [metres north of you]
cmd-wf_encounter-hint-uid = <encounter entity>
cmd-wf_encounter-list-line = {$uid} {$prototype} "{$name}" {$state}, {$ships} ships, origin {$x}, {$y}
cmd-wf_encounter-list-footer = {$count} encounters.
cmd-wf_encounter-state-active = Active
cmd-wf_encounter-no-player = You need a body or a ghost on a map to spawn an encounter.
cmd-wf_encounter-unknown = {$prototype} is not an encounter.
cmd-wf_encounter-bad-distance = {$arg} is not a distance between 0 and 20000.
cmd-wf_encounter-spawned = Started "{$name}" as {$uid}.
cmd-wf_encounter-spawn-failed = {$prototype} could not be started; the server log says which ship failed.
cmd-wf_encounter-not-found = {$arg} is not an encounter.
cmd-wf_encounter-ended = Ended {$uid} and removed its ships.
cmd-wf_encounter-scheduled = The scheduler started {$uid}.
cmd-wf_encounter-not-scheduled = The scheduler started nothing; the server log says why.
cmd-wf_encounter-paused = Encounter scheduler paused.
cmd-wf_encounter-resumed = Encounter scheduler resumed.
