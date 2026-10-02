# Names
wf-crew-name-format = {$title} {$name}
wf-crew-role-deckhand = Deckhand
wf-crew-role-marine = Marine
wf-crew-role-pilot = First Officer
wf-crew-role-radio-operator = Radio Officer
wf-crew-role-captain = Captain

# Planner post kinds
wf-crew-post-marker = marker
wf-crew-post-helm = helm
wf-crew-post-radio = radio
wf-crew-post-dock = airlock
wf-crew-post-deck = deck

# Pilot orders
wf-crew-order-hold = hold
wf-crew-order-goto = go to
wf-crew-order-loiter = loiter
wf-crew-order-follow = follow
wf-crew-order-dock = dock
wf-crew-order-undock = undock

# wf_crew command
cmd-wf_crew-desc = Plans, spawns, lists and clears NPC crew on a grid.
cmd-wf_crew-help = Usage: {$command} plan <grid|here> [deckhands] | spawn <grid|here> [group] [deckhands] | spawnrole <role> [group] | list [group] | clear <group> | duty <mob> <duty> | orders <mob> hold | orders <mob> goto <x> <y> [<x> <y> ...] | orders <mob> loiter <x> <y> <radius> | orders <mob> follow <grid|here> | orders <mob> dock <grid|here> | orders <mob> undock
cmd-wf_crew-unknown = Unknown subcommand: {$sub}
cmd-wf_crew-hint-sub = <subcommand>
cmd-wf_crew-hint-role = <role>
cmd-wf_crew-hint-grid = <grid entity, or here>
cmd-wf_crew-hint-mob = <crewman entity>
cmd-wf_crew-hint-order = <order>
cmd-wf_crew-not-on-grid = You are not on a grid.
cmd-wf_crew-not-a-grid = {$arg} is not a grid.
cmd-wf_crew-bad-number = {$arg} is not a whole number.
cmd-wf_crew-bad-decimal = {$arg} is not a number.
cmd-wf_crew-no-player = You need a body to spawn at.
cmd-wf_crew-unknown-role = Unknown crew role: {$role}
cmd-wf_crew-not-crew = {$arg} is not a crewman.
cmd-wf_crew-not-pilot = {$arg} is not a crewman who can pilot.
cmd-wf_crew-not-on-map = {$name} is not on a map.
cmd-wf_crew-dock-own-grid = {$name} can't dock with their own grid.
cmd-wf_crew-plan-header = {$count} posts on {$grid}:
cmd-wf_crew-plan-line = {$kind}: {$role} at {$x}, {$y}
cmd-wf_crew-spawned = Spawned {$count} crew in group {$group}.
cmd-wf_crew-spawned-one = Spawned {$name} ({$uid}) in group {$group}.
cmd-wf_crew-spawn-failed = Could not spawn {$role}.
cmd-wf_crew-list-line = {$uid} {$name}: {$role}, duty {$duty}, group {$group}
cmd-wf_crew-list-footer = {$count} crew.
cmd-wf_crew-cleared = Removed {$count} crew from group {$group}.
cmd-wf_crew-duty-set = {$name} now works {$duty}.
cmd-wf_crew-orders-set = {$name}'s orders are now: {$orders}.
