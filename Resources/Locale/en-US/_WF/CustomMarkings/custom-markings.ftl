wf-custom-marking-creator-button = { $worn ->
    [0] Custom markings
    *[other] Custom markings ({ $worn } worn)
}
wf-custom-marking-default-name = Custom marking

wf-custom-marking-library-title = Custom markings
wf-custom-marking-library-hint = Draw your own markings and keep them here for any of your characters. Everything you save is stored on the server, shown to other players and covered by the server rules.
wf-custom-marking-library-new = Draw new
wf-custom-marking-library-import = Import PNG
wf-custom-marking-library-import-tooltip = A 64x64 image holding the four facings (south and north on top, east and west below), or a 32x32 image of the south facing alone.
wf-custom-marking-library-counts = { $saved } of { $limit } saved, { $worn } of { $max } worn
wf-custom-marking-library-loading = Loading your library...
wf-custom-marking-library-empty = Nothing here yet. Press Draw new to make your first marking.
wf-custom-marking-library-wear = Wear
wf-custom-marking-library-take-off = Take off
wf-custom-marking-library-edit = Edit
wf-custom-marking-library-export = Export
wf-custom-marking-library-delete = Delete
wf-custom-marking-library-delete-confirm = Really delete?
wf-custom-marking-library-keep = Add to library
wf-custom-marking-library-keep-tooltip = This character wears a marking that isn't in your library. Add it to keep it for other characters and to edit it.
wf-custom-marking-library-stray = Not in your library

wf-custom-marking-editor-title-new = New custom marking
wf-custom-marking-editor-title-edit = Edit custom marking
wf-custom-marking-editor-tools = Tools
wf-custom-marking-editor-undo = Undo
wf-custom-marking-editor-redo = Redo
wf-custom-marking-editor-facing-tools = This facing
wf-custom-marking-editor-flip = Flip
wf-custom-marking-editor-mirror = Mirror to other side
wf-custom-marking-editor-nudge = Move this facing by one pixel
wf-custom-marking-editor-clear = Clear
wf-custom-marking-editor-facings = Facing
wf-custom-marking-editor-show-body = Show body
wf-custom-marking-editor-show-clothes = Show clothes
wf-custom-marking-editor-show-grid = Show grid
wf-custom-marking-editor-opacity = Opacity
wf-custom-marking-editor-colour-body = Your character:
wf-custom-marking-editor-colour-skin = Skin color
wf-custom-marking-editor-colour-hair = Hair color
wf-custom-marking-editor-colour-facial-hair = Facial hair color
wf-custom-marking-editor-colour-eyes = Eye color
wf-custom-marking-editor-name = Name
wf-custom-marking-editor-placement = Drawn
wf-custom-marking-editor-save = Save to library
wf-custom-marking-editor-cancel = Cancel
wf-custom-marking-editor-saving = Saving...

wf-custom-marking-tool-pencil = Pencil
wf-custom-marking-tool-pencil-tooltip = Left click draws. Right click erases with any tool.
wf-custom-marking-tool-eraser = Eraser
wf-custom-marking-tool-eraser-tooltip = Clears pixels.
wf-custom-marking-tool-fill = Fill
wf-custom-marking-tool-fill-tooltip = Recolors the pixel you click and every same-colored pixel touching it.
wf-custom-marking-tool-picker = Pick color
wf-custom-marking-tool-picker-tooltip = Takes the color of a pixel you have drawn.

wf-custom-marking-facing-south = Front
wf-custom-marking-facing-north = Back
wf-custom-marking-facing-east = Facing right
wf-custom-marking-facing-west = Facing left

wf-custom-marking-placement-behind = Behind the body
wf-custom-marking-placement-behind-hint = Drawn behind the whole body, so only what sticks out past it shows: a tail or wings seen from the front. Hidden by clothing that hides tails.
wf-custom-marking-placement-skin = On the skin
wf-custom-marking-placement-skin-hint = Drawn on the body, under underwear and clothing. The hands and feet draw over it; use On the hands and feet for those.
wf-custom-marking-placement-hands = On the hands and feet
wf-custom-marking-placement-hands-hint = Drawn over the hands and feet, under gloves and shoes. Anything drawn elsewhere shows over the uniform.
wf-custom-marking-placement-hair = Over the hair
wf-custom-marking-placement-hair-hint = Drawn over the head and hair, under hats and masks. Hidden by headgear that hides hair.
wf-custom-marking-placement-front = Over clothing
wf-custom-marking-placement-front-hint = Drawn over clothing and cloaks: a tail or wings seen from behind. Hidden by clothing that hides tails.

wf-custom-marking-error-disabled = Custom markings are turned off on this server.
wf-custom-marking-error-cooldown = Wait a moment before saving again.
wf-custom-marking-error-invalid = That marking couldn't be read.
wf-custom-marking-error-blank = Draw something first.
wf-custom-marking-error-failed = The marking couldn't be saved. Try again.
wf-custom-marking-error-missing = That marking is no longer in your library.
wf-custom-marking-error-full = Your library is full. Delete a marking to make room.
wf-custom-marking-error-blocked = That art has been blocked by an admin.
wf-custom-marking-error-import = That file isn't a 64x64 or 32x32 PNG.
wf-custom-marking-error-export = The file couldn't be written.

wf-custom-marking-admin-remove = Remove custom markings
wf-custom-marking-admin-block = Block custom markings

cmd-custommarkingblock-desc = Blocks custom marking art by hash, taking it off every character and refusing it from now on, or lifts a block.
cmd-custommarkingblock-help = Usage: { $command } <hash> [blocked: true/false]
cmd-custommarkingblock-invalid-args = Expected an art hash from the admin log, and optionally true or false.
cmd-custommarkingblock-unknown = No art has that hash.
cmd-custommarkingblock-blocked = Art blocked.
cmd-custommarkingblock-unblocked = Art unblocked.
