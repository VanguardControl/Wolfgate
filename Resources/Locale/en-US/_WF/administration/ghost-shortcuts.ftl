# Wolfgate admin ghost shortcuts

## Drag a ghost onto a body
wf-ghost-possess-prompt-title = Body already has a player
wf-ghost-possess-prompt-message = [bold]{$body}[/bold] is [bold]{$character}[/bold], played by [bold]{$player}[/bold]. Put {$ghost} in it anyway?
wf-ghost-possess-prompt-warning = {$character} will be ghosted and cannot return to this body.
wf-ghost-possess-prompt-replace = Replace
wf-ghost-possess-prompt-abort = Abort
wf-ghost-possess-disconnected = a disconnected player
wf-ghost-possess-no-player = That ghost has no player.
wf-ghost-possess-still-occupied = Could not free the body of its current player.
wf-ghost-possess-done = {$player} now controls {$body}.
wf-ghost-possess-notice = An admin has placed you in {$body}.

## spawnoutfitghost command
cmd-spawnoutfitghost-desc = Spawns a ghost's player as their selected character wearing a starting gear outfit and puts them in control.
cmd-spawnoutfitghost-help = Usage: {$command} <ghost entity> <starting gear ID>
cmd-spawnoutfitghost-hint-ghost = <ghost entity>
cmd-spawnoutfitghost-invalid-args = Expected a ghost entity and a starting gear ID.
cmd-spawnoutfitghost-invalid-ghost = That entity is not a ghost.
cmd-spawnoutfitghost-no-player = That ghost has no player.
cmd-spawnoutfitghost-success = Spawned {$player} as {$mob} wearing {$gear}.
