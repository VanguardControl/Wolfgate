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
wf-mapping-mode-too-many-own-sessions = You can own {$max ->
    [one] one mapping session
   *[other] {$max} mapping sessions
} at a time.
wf-mapping-mode-kicked-no-rejoin = You were removed from "{$name}" and can't join it again.
wf-mapping-mode-new-code = The join code is now {$code}. The old one no longer works.
wf-mapping-mode-new-code-not-owner = Only the session owner can change the join code.

wf-mapping-mode-opened = Opened "{$name}" {$version}. Its join code is {$code}.

## Library

wf-mapping-mode-saved = Saved "{$name}" as {$version}.
wf-mapping-mode-save-failed = The map couldn't be saved.
wf-mapping-mode-save-too-soon = Wait a few seconds between saves.
wf-mapping-mode-autosave-failed = Autosave of "{$name}" failed.
wf-mapping-mode-autosave-not-owner = Only the session owner can change autosave.
wf-mapping-mode-library-full-maps = Your library is full ({$max} maps). Delete a map first.
wf-mapping-mode-library-full-bytes = Your library is full ({$max} MiB). Delete some versions first.
wf-mapping-mode-library-not-found = That map or version isn't in your library.
wf-mapping-mode-library-read-failed = Your library couldn't be read.
wf-mapping-mode-open-failed = "{$name}" couldn't be opened.
wf-mapping-mode-deleted-map = Deleted "{$name}".
wf-mapping-mode-deleted-version = Deleted {$version} of "{$name}".
wf-mapping-mode-delete-failed = That couldn't be deleted.

## Downloads

wf-mapping-mode-download-busy = A download is already running.
wf-mapping-mode-download-done = Downloaded "{$name}" {$version} ({$count ->
    [one] 1 file
   *[other] {$count} files
}).
wf-mapping-mode-download-failed = The download failed.
wf-mapping-mode-download-too-large = A level is over the {$max} MiB download limit.
wf-mapping-mode-download-stalled = The download stopped responding and was dropped.
wf-mapping-mode-download-cancelled = The download was cancelled.

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
wf-mapping-mode-limit-selection = That covers more than {$max} tiles at once.
wf-mapping-mode-limit-edit-entities = That touches more than {$max} entities at once.
wf-mapping-mode-limit-edit-decals = That carries more than {$max} decals at once.
wf-mapping-mode-limit-off-grid = Place things on a grid. Lay a tile first.

## Reference grids

wf-mapping-mode-reference-not-listed = That file can't be loaded as a reference.
wf-mapping-mode-reference-too-large = That file is over the {$max} MiB reference limit.
wf-mapping-mode-reference-failed = That file couldn't be loaded.
wf-mapping-mode-reference-empty = That file has no grids.
wf-mapping-mode-reference-expired = The reference wasn't placed in time and was dropped.
wf-mapping-mode-reference-busy = Another reference is loading. Try again in a moment.
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

## Lobby

wf-mapping-mode-lobby-title = Mapping workshop
wf-mapping-mode-lobby-intro = Start a map, or join someone else's with their join code. Maps save to your library on this server.
wf-mapping-mode-lobby-new = New map
wf-mapping-mode-lobby-new-placeholder = Map name
wf-mapping-mode-lobby-create = Create
wf-mapping-mode-lobby-join = Join a session
wf-mapping-mode-lobby-join-placeholder = Join code
wf-mapping-mode-lobby-join-button = Join
wf-mapping-mode-lobby-sessions = Your sessions
wf-mapping-mode-lobby-sessions-none = You aren't in a session.
wf-mapping-mode-lobby-session-entry = "{$name}", owner {$owner}, {$count ->
    [one] 1 member
   *[other] {$count} members
}
wf-mapping-mode-lobby-rejoin = Rejoin
wf-mapping-mode-lobby-maps = My maps
wf-mapping-mode-lobby-refresh = Refresh
wf-mapping-mode-observe = Observe
wf-mapping-mode-observe-tooltip = Admins only. Puts you on a private admin map; use tpto or wf_mappingmode_tp to reach a session.

## Library list

wf-mapping-mode-library-loading = Loading your library...
wf-mapping-mode-library-empty = No saved maps yet.
wf-mapping-mode-library-never = never
wf-mapping-mode-library-usage = {$maps}/{$maxMaps} maps, {$used}/{$max} MiB
wf-mapping-mode-library-map-info = {$count ->
    [one] 1 version
   *[other] {$count} versions
}, last saved {$saved}
wf-mapping-mode-library-version = {$version}, {$saved}, {$kind}, {$levels ->
    [one] 1 level
   *[other] {$levels} levels
}, {$size} MiB
wf-mapping-mode-kind-manual = saved
wf-mapping-mode-kind-auto = autosave
wf-mapping-mode-library-open = Open
wf-mapping-mode-library-open-latest-tooltip = Open the newest version as a new session.
wf-mapping-mode-library-download = Download
wf-mapping-mode-library-download-latest-tooltip = Download the newest version to your computer.
wf-mapping-mode-library-versions = Versions
wf-mapping-mode-library-hide-versions = Hide versions
wf-mapping-mode-library-delete = Delete
wf-mapping-mode-library-delete-map-tooltip = Delete this map and every version of it.

## Download progress

wf-mapping-mode-download-progress = Downloading "{$name}" {$version}: file {$file} of {$files}, {$percent}%
wf-mapping-mode-download-ready = Downloaded "{$name}" {$version}.
wf-mapping-mode-download-cancel = Cancel
wf-mapping-mode-download-open-folder = Open folder
wf-mapping-mode-download-bad-data = The download arrived damaged and was stopped.
wf-mapping-mode-download-write-failed = The download couldn't be written to your data folder.

## Mapping window

wf-mapping-mode-escape-button = Mapping
wf-mapping-mode-menu-title = Mapping
wf-mapping-mode-menu-tab-session = Session
wf-mapping-mode-menu-tab-save = Save
wf-mapping-mode-menu-tab-library = Library
wf-mapping-mode-menu-no-session = You aren't in a mapping session.
wf-mapping-mode-menu-return = Return to lobby
wf-mapping-mode-menu-return-tooltip = Leave the admin map and go back to the mapping lobby.
wf-mapping-mode-menu-code = Join code: {$code}
wf-mapping-mode-menu-copy = Copy
wf-mapping-mode-menu-copied = Copied the join code.
wf-mapping-mode-menu-new-code = New code
wf-mapping-mode-menu-new-code-tooltip = Replace the join code. The old one stops working; members stay in.
wf-mapping-mode-menu-status = Grids {$grids}/{$maxGrids} · Entities {$entities}/{$maxEntities} · Area {$size} × {$size}
wf-mapping-mode-menu-members = Members
wf-mapping-mode-menu-member = {$name}{$owner ->
    [yes] {" "}(owner)
   *[no] {""}
}{$away ->
    [yes] {" "}(away)
   *[no] {""}
}
wf-mapping-mode-menu-kick = Remove
wf-mapping-mode-menu-autosave = Autosave
wf-mapping-mode-menu-autosave-off = Off
wf-mapping-mode-menu-autosave-step = Every {$minutes ->
    [one] minute
   *[other] {$minutes} minutes
}
wf-mapping-mode-menu-autosave-owner-only = Only the owner can change this.
wf-mapping-mode-menu-grids = Grids
wf-mapping-mode-menu-delete-grid = Delete the grid under me
wf-mapping-mode-menu-delete-grid-tooltip = Deletes every tile and entity on the grid you're over. Undo brings it back.
wf-mapping-mode-menu-delete-grid-none = You aren't over a grid.
wf-mapping-mode-menu-delete-grid-too-large = That grid is too big to delete at once. Select and delete it in parts.
wf-mapping-mode-menu-reference = Reference grids
wf-mapping-mode-menu-reference-load = Load reference
wf-mapping-mode-menu-reference-hint = Pick a file and press Place. R turns it, click puts it down, Escape or right click drops it.
wf-mapping-mode-menu-reference-search = Search
wf-mapping-mode-menu-reference-place = Place
wf-mapping-mode-menu-reference-source-saved = My library
wf-mapping-mode-menu-reference-source-ships = Ship grids
wf-mapping-mode-menu-reference-source-maps = Maps
wf-mapping-mode-menu-leave = Leave session
wf-mapping-mode-menu-leave-tooltip = Leave this session for good. Save first; you can only come back with the join code.
wf-mapping-mode-menu-save-name = Save as
wf-mapping-mode-menu-save-now = Save now
wf-mapping-mode-menu-save-hint = Saves go into your own library under this name.
wf-mapping-mode-menu-last-saved = Last saved {$time}.
wf-mapping-mode-menu-never-saved = Not saved yet.
wf-mapping-mode-menu-next-autosave = Next autosave check in {$time}.
wf-mapping-mode-menu-autosave-disabled = Autosave is off.
wf-mapping-mode-menu-dirty = Unsaved changes.
wf-mapping-mode-menu-clean = No unsaved changes.

## Map limit overlay

wf-mapping-mode-bounds-label = {$size} × {$size} map limit
