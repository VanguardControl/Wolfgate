## Sessions

wf-mapping-mode-map-name = Mapping: {$name}
wf-mapping-mode-admin-map-name = Mapping mode admin map
wf-mapping-mode-off = Mapping mode is off.
wf-mapping-mode-not-in-lobby = You have to be in the lobby to do that.
wf-mapping-mode-bad-name = Names are 1 to {$max} letters, digits, spaces, dashes or underscores.
wf-mapping-mode-too-many-sessions = The server is at its limit of {$max} mapping sessions.
wf-mapping-mode-no-session = No session has that code.
wf-mapping-mode-session-full = That session is full ({$max} members).
wf-mapping-mode-created = Created "{$name}". Its join code is {$code}.
wf-mapping-mode-joined = Joined "{$name}".
wf-mapping-mode-left = You left the session.
wf-mapping-mode-kicked = You were removed from "{$name}".
wf-mapping-mode-kick-not-owner = Only the session owner can remove members.
wf-mapping-mode-kick-not-member = That player isn't another member of your session.
wf-mapping-mode-closed = The session "{$name}" was closed.
wf-mapping-mode-owner-now = You now own "{$name}".

## Limits and refusals

wf-mapping-mode-not-your-map = That isn't your session's map.
wf-mapping-mode-limit-area = Outside the {$size} × {$size} map limit.
wf-mapping-mode-limit-grids = Grid limit reached ({$max}).
wf-mapping-mode-limit-entities = Entity limit reached ({$max}).
wf-mapping-mode-limit-entities-per-tile = Too many entities on that tile (limit {$max}).
wf-mapping-mode-limit-decals-per-tile = Too many decals on that tile (limit {$max}).
wf-mapping-mode-limit-levels = Level limit reached ({$max}).
wf-mapping-mode-limit-prototype = That can't be placed in mapping mode.
wf-mapping-mode-bad-tile = Unknown tile.
wf-mapping-mode-erase-refused = That can't be erased.

## Reference grids

wf-mapping-mode-reference-not-listed = That file can't be loaded as a reference.
wf-mapping-mode-reference-too-large = That file is over the {$max} MiB reference limit.
wf-mapping-mode-reference-failed = That file couldn't be loaded.
wf-mapping-mode-reference-empty = That file has no grids.
wf-mapping-mode-reference-expired = The reference wasn't placed in time and was dropped.
wf-mapping-mode-reference-placed = Placed {$count ->
    [one] the reference grid
   *[other] {$count} reference grids
}.

## Admin commands

cmd-wf_mappingmode-no-session = No mapping session has the code {$code}.
cmd-wf_mappingmode-hint-code = <session code>
cmd-wf_mappingmode-hint-player = <player name>

cmd-wf_mappingmode_list-desc = Lists mapping sessions with their codes, owners, members and maps.
cmd-wf_mappingmode_list-help = Usage: {$command}
cmd-wf_mappingmode_list-none = No mapping sessions.
cmd-wf_mappingmode_list-absent = {$name} (away)
cmd-wf_mappingmode_list-entry = {$code} "{$name}" owner {$owner}; members: {$members}; maps {$maps}; unsaved changes {$dirty ->
    [yes] yes
   *[no] no
}; last saved {$saved}

cmd-wf_mappingmode_tp-desc = Takes you to a mapping session's ground level.
cmd-wf_mappingmode_tp-help = Usage: {$command} <code>
cmd-wf_mappingmode_tp-no-entity = You need to be in game (observe first).

cmd-wf_mappingmode_close-desc = Autosaves a mapping session to its owner's library and closes it.
cmd-wf_mappingmode_close-help = Usage: {$command} <code>
cmd-wf_mappingmode_close-closed = Closed mapping session {$code}.

cmd-wf_mappingmode_kick-desc = Removes a player from their mapping session.
cmd-wf_mappingmode_kick-help = Usage: {$command} <player>
cmd-wf_mappingmode_kick-no-player = No player called {$name}.
cmd-wf_mappingmode_kick-not-member = {$name} isn't in a mapping session.
cmd-wf_mappingmode_kick-kicked = Removed {$name} from mapping session {$code}.

cmd-wf_mappingmode_save-desc = Saves a mapping session to its owner's library.
cmd-wf_mappingmode_save-help = Usage: {$command} <code>
cmd-wf_mappingmode_save-saved = Saved mapping session {$code}.
cmd-wf_mappingmode_save-failed = Couldn't save mapping session {$code}.
