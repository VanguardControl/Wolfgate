# Wolfgate ERT Builder

## Window
wf-admin-tab-ert = ERT Builder
wf-ert-title = ERT Builder
wf-ert-info = Spawns a ship and one ghost role per member aboard it. Players keep their own character if its species is allowed; otherwise they get a random allowed species.
wf-ert-section-team = Team
wf-ert-team-name = Name
wf-ert-team-name-placeholder = e.g. Rescue Team
wf-ert-team-name-default = Emergency Response Team
wf-ert-briefing = Briefing
wf-ert-briefing-placeholder = Shown to ghosts when they pick the role.
wf-ert-members = Members
wf-ert-leader = First member is the leader
wf-ert-section-outfits = Outfits
wf-ert-member-outfit = Members
wf-ert-leader-outfit = Leader
wf-ert-outfit-search = Search outfits...
wf-ert-same-as-members = Same as members
wf-ert-section-ids = ID cards
wf-ert-leader-title = Leader title
wf-ert-member-title = Member title
wf-ert-keep-access = Keep the outfit ID's own access
wf-ert-access-hint = Extra access for every ID:
wf-ert-section-species = Allowed species
wf-ert-species-hint = Leave all unticked to allow any species.
wf-ert-generic-humans = Make everyone a default bald male human with a random name
wf-ert-section-ship = Ship
wf-ert-ship-search = Search ships...
wf-ert-no-ship = No ship (spawn at my position)
wf-ert-spawn = Spawn ERT

## Server messages
wf-ert-default-leader-title = {$team} Leader
wf-ert-default-member-title = {$team} Responder
wf-ert-default-briefing = You are part of the {$team}. Follow your leader and the admins' instructions.
wf-ert-spawned = Spawned the {$team} with {$count} ghost roles and asked {$ghosts} ghosts to sign up.
wf-ert-error-no-name = Give the team a name.
wf-ert-error-members = Members must be between 1 and {$max}.
wf-ert-error-outfit = Unknown outfit "{$id}".
wf-ert-error-vessel = Unknown ship "{$id}".
wf-ert-error-vessel-failed = Couldn't spawn the ship. Check the server log.
wf-ert-error-no-entity = You need a body or ghost in the world to spawn a team at.
wf-ert-error-no-map = You aren't on a map.

## Ghost sign-up prompt
wf-ert-prompt-title = Emergency Response
wf-ert-prompt-places = {$count} places. Signing up enters you into a short draw.
wf-ert-prompt-sign-up = Sign up
wf-ert-prompt-sign-up-leader = Sign up as leader
wf-ert-prompt-all-roles = All ghost roles
wf-ert-signup-joined = You're in the draw for {$title}.
wf-ert-signup-already = You've already signed up for this team.
wf-ert-signup-full = No member places are left.
wf-ert-signup-no-leader = The leader place is already taken.
wf-ert-signup-not-ghost = Only ghosts can sign up.
wf-ert-signup-closed = This team has no places left.

## Commands
cmd-ertbuilder-desc = Opens the ERT Builder.
cmd-ertbuilder-help = Usage: {$command}
cmd-ertbuilder-no-player = Only players can open the ERT Builder.
cmd-ertbuilderui-desc = Opens the ERT Builder window.
cmd-ertbuilderui-help = Usage: {$command}
