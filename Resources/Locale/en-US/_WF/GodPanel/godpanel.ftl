# God Panel, client side

wf-admin-tab-god-panel = God Panel
wf-godpanel-title = God Panel

wf-godpanel-war-hot = HOT
wf-godpanel-war-cold = COLD

# Top bar
wf-godpanel-top-waiting = Waiting for the server...
wf-godpanel-top-round = ROUND {$time}
wf-godpanel-top-war = WAR: {$level}
wf-godpanel-top-reavers = REAVERS: tier {$tier} ({$name}), {$cells} cells
wf-godpanel-top-encounters-running = ENCOUNTERS: {$count} running
wf-godpanel-top-encounters-paused = ENCOUNTERS: {$count} running (paused)
wf-godpanel-top-sound = SOUND: {$title}
wf-godpanel-top-sound-none = SOUND: none
wf-godpanel-top-gms = GMs: {$count}
wf-godpanel-top-confirm = Confirm?
wf-godpanel-top-internet-sound = Internet sound
wf-godpanel-top-pause-encounters = Pause encounters
wf-godpanel-top-resume-encounters = Resume encounters
wf-godpanel-top-war-level = War level
wf-godpanel-top-war-to-hot = Set war HOT
wf-godpanel-top-war-to-cold = Set war COLD
wf-godpanel-top-pause-reavers = Pause Reavers
wf-godpanel-top-resume-reavers = Resume Reavers
wf-godpanel-top-crew = Crew Setup
wf-godpanel-top-encounters = Encounters
wf-godpanel-top-vessel = Spawn Vessel
wf-godpanel-top-ert = ERT
wf-godpanel-top-planets = Planets
wf-godpanel-top-power = Grid Power

# Roster
wf-godpanel-tab-players = Players
wf-godpanel-tab-ships = Ships
wf-godpanel-tab-ops = Ops
wf-godpanel-search = Search...
wf-godpanel-roster-empty = Nothing matches.
wf-godpanel-player-title = {$character} ({$username})
wf-godpanel-tag-antag = antag
wf-godpanel-tag-admin = admin
wf-godpanel-tag-offered = offered
wf-godpanel-tag-gq = GQ
wf-godpanel-tag-sos = SOS
wf-godpanel-tag-encounter = encounter
wf-godpanel-tag-sanctuary = sanctuary
wf-godpanel-status-alive = alive
wf-godpanel-status-critical = critical
wf-godpanel-status-dead = dead
wf-godpanel-status-ghost = ghost
wf-godpanel-status-lobby = in lobby
wf-godpanel-status-disconnected = disconnected
wf-godpanel-kind-station = station
wf-godpanel-kind-ship = ship
wf-godpanel-kind-encounter = encounter
wf-godpanel-kind-debris = debris
wf-godpanel-grid-people = {$players} players, {$crew} crew
wf-godpanel-grid-power = power {$percent}
wf-godpanel-grid-no-power = no power net
wf-godpanel-grid-code = code {$code}

# Ops tab
wf-godpanel-ops-round = Round time: {$time}
wf-godpanel-ops-war = War level: {$level}
wf-godpanel-ops-reavers = Reavers: tier {$tier} ({$name}), holding {$cells} cells
wf-godpanel-ops-reavers-state = Reaver campaign: { $paused ->
    [yes] paused
   *[no] running
}
wf-godpanel-ops-encounters-state = Encounter scheduler: {$running} running, { $paused ->
    [yes] paused
   *[no] active
}
wf-godpanel-ops-sound = Sound now playing: {$title} (asked by {$requester})
wf-godpanel-ops-sound-none = Sound now playing: nothing
wf-godpanel-ops-gms = GMs with the panel open: {$count}
wf-godpanel-ops-encounters = Encounters
wf-godpanel-ops-encounters-heading = Encounters ({$count})
wf-godpanel-ops-encounter-line = {$state}: {$ships}
wf-godpanel-ops-end = End

# Map
wf-godpanel-map-recentre = Recentre
wf-godpanel-map-recentre-tooltip = Put the map back on your own position.

# Context bar
wf-godpanel-ctx-none = Nothing selected. Pick a player or ship from the list or the map.
wf-godpanel-ctx-selected = SELECTED: {$what}
wf-godpanel-ctx-health = {$status} {$health}
wf-godpanel-ctx-on-grid = on {$grid}
wf-godpanel-ctx-owner = owner {$owner}
wf-godpanel-ctx-power = power {$percent}, breakers {$on}/{$total}
wf-godpanel-ctx-encounter = encounter {$name} ({$state})
wf-godpanel-ctx-docked = docked to {$to}
wf-godpanel-ctx-watch = Watch
wf-godpanel-ctx-follow = Follow
wf-godpanel-ctx-goto = Go to
wf-godpanel-ctx-bring = Bring
wf-godpanel-ctx-second-chance = Second chance
wf-godpanel-ctx-offer = Offer to ghosts
wf-godpanel-ctx-offer-tooltip = Offers a body nobody is in to the ghosts.
wf-godpanel-ctx-rejuvenate = Rejuvenate
wf-godpanel-ctx-outfit = Spawn as outfit
wf-godpanel-ctx-player-panel = Player panel
wf-godpanel-ctx-camera = Camera
wf-godpanel-ctx-power-on = Power on
wf-godpanel-ctx-power-off = Power off
wf-godpanel-ctx-with-announcement = With announcement
wf-godpanel-ctx-gq-on = GQ on
wf-godpanel-ctx-gq-off = GQ off
wf-godpanel-ctx-sos-on = SOS on
wf-godpanel-ctx-sos-off = SOS off
wf-godpanel-ctx-hail = Hail as God
wf-godpanel-ctx-crew = Crew setup
wf-godpanel-ctx-end-encounter = End encounter
wf-godpanel-ctx-announce = Announce
wf-godpanel-ctx-announce-placeholder = PA announcement
wf-godpanel-ctx-code = Set code...
wf-godpanel-ctx-sound-placeholder = Sound link
wf-godpanel-ctx-pa-play = Play on PA
wf-godpanel-ctx-pa-stop = Stop PA sound

# Feeds
wf-godpanel-feeds-title = Feeds
wf-godpanel-feeds-free = + Free camera
wf-godpanel-feeds-free-tooltip = Opens a camera you can drag around the map.
wf-godpanel-feeds-empty = No feeds. Double-click a player or ship, or press Watch.
wf-godpanel-feed-free = Free camera
wf-godpanel-feed-loading = Loading...
wf-godpanel-feed-unknown = Unknown
wf-godpanel-feed-drag-hint = Drag to move
wf-godpanel-feed-follow = Follow
wf-godpanel-feed-goto = Go to
wf-godpanel-feed-select = Select
wf-godpanel-feed-close = Close

# Timeline
wf-godpanel-timeline-title = TIMELINE
wf-godpanel-timeline-empty = Nothing has happened yet.
wf-godpanel-category-all = All
wf-godpanel-category-death = Deaths
wf-godpanel-category-player = Players
wf-godpanel-category-ship = Ships
wf-godpanel-category-encounter = Encounters
wf-godpanel-category-sector = Sector
wf-godpanel-category-round = Round
wf-godpanel-category-gm = GM
