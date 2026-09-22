cmd-mapundo-desc = Takes back your last mapping placements, erases or tile changes. Only works on a map that has not been initialised.
cmd-mapundo-help = Usage: mapundo [count]
cmd-mapredo-desc = Puts back what mapundo took away.
cmd-mapredo-help = Usage: mapredo [count]

mapping-undo-bad-count = Count has to be a whole number of steps, one or more.
mapping-undo-nothing = Nothing to undo.
mapping-redo-nothing = Nothing to redo.
mapping-undo-done = Undid: {$what}
mapping-redo-done = Redid: {$what}
mapping-undo-left = {$undo} step(s) to undo, {$redo} to redo.
mapping-undo-skipped = Some of that was already gone and was left alone.
mapping-undo-truncated = That burst was too large to record in full; part of it could not be put back.
mapping-undo-links = Restored objects are new entities: device links and anything else pointing at the old ones are lost.

mapping-undo-part-placed = placed {$what}
mapping-undo-part-erased = erased {$what}
mapping-undo-part-tiles = tiled {$what}
mapping-undo-part-more = (+{$count} more)
mapping-undo-part-nothing = nothing

ui-options-function-mapping-undo = Mapping: undo
ui-options-function-mapping-redo = Mapping: redo
