wf-vehiclecrew-desc = Spawns, lists and clears NPC-crewed vehicles.
wf-vehiclecrew-help = Usage: wf_vehiclecrew spawn <crew> [behaviour] [distance] | list | clear [group]
wf-vehiclecrew-unknown-sub = Unknown subcommand {$sub}.
wf-vehiclecrew-no-entity = You need a body to spawn a vehicle beside.
wf-vehiclecrew-unknown-crew = Unknown vehicle crew {$crew}.
wf-vehiclecrew-bad-behaviour = Unknown behaviour {$behaviour}. Use Hold, Patrol, Hunt, Escort or Flee.
wf-vehiclecrew-bad-distance = Distance must be a number of tiles, 0 or more.
wf-vehiclecrew-spawn-failed = Could not spawn {$crew}; see the server log.
wf-vehiclecrew-spawned = Spawned {$vehicle}, crew group {$group}.
wf-vehiclecrew-list-empty = No NPC vehicle crews.
wf-vehiclecrew-list-line = {$vehicle} ({$crew}), group {$group}: {$behaviour}, {$alive} alive
wf-vehiclecrew-list-line-bailed = {$vehicle} ({$crew}), group {$group}: bailed out, {$alive} alive
wf-vehiclecrew-cleared = Cleared {$count} {$count ->
    [one] vehicle
   *[other] vehicles
}.
wf-vehiclecrew-hint-sub = spawn, list or clear
wf-vehiclecrew-hint-crew = vehicle crew
wf-vehiclecrew-hint-group = crew group
wf-vehiclecrew-hint-behaviour = behaviour
wf-vehiclecrew-hint-distance = tiles from you, default 6
