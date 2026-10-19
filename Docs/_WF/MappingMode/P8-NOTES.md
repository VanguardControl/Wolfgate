# MappingMode phase 2, P8 notes

Pipe mode: what it does where the brief left room, and where the colours and prototypes come from. Each names the file
it lives in.

## What exists

- No pipe painter tool or pipe auto-router exists in this fork or its layers. The spray painter (`SprayPainter`,
  `Resources/Prototypes/Entities/Objects/Tools/spray_painter.yml`) paints pipes from a palette, and `colornetwork`
  (`Content.Server/Sandbox/Commands/ColorNetworkCommand.cs`) paints a formed pipe net by entity id. Neither builds pipes.
- Pipe layers exist: `AtmosPipeLayersComponent` with Primary, Secondary and Tertiary. Each plain pipe names its other
  layers in `alternativePrototypes` (`GasPipeStraightAlt1`, `GasPipeStraightAlt2`, ... in `alt_layers.yml`).

## Colours

The presets are the spray painter's "standard atmos pipes" entries, which are also what Monolith's maps use most:

| Preset | Colour | Spray painter key | Pipes with it in `Resources/Maps` and `Resources/SharedMaps` |
|---|---|---|---|
| Distro | `#0055CC` | `distro` | 41,996 |
| Waste | `#990000` | `waste` | 39,314 |
| Air | `#03FCD3` | `air` | 1,434 |
| Mix | `#947507` | `mix` | 538 |

There is no separate scrubber colour: scrubbers sit on the waste loop, and `#B30000` appears in no map, so the Waste
preset is labelled "Waste (scrubbers)". Custom takes a `#RRGGBB` hex; Auto sends no colour and asks the server for the
colour of the first pipe the path joins (none when it joins nothing). Painting needs a colour, so Auto can't paint.

## Path (`MappingToolsPipes.Extend`, shared)

- The client samples the cell under the mouse every frame while the button is held (`IWFMappingFrameHandler`, called
  from `WFMappingBuildModeSystem.FrameUpdate`), on the grid under the press. Cells off that grid are ignored, so a path
  never crosses grids.
- A jump of more than one cell is bridged with an L, along the axis of the last step first, so a fast drag keeps going
  the way it was going before it turns.
- Reaching a cell already on the path cuts the path back to it, so dragging back undoes the path. A closed loop is
  therefore two drags; the second one joins the first through Auto-join or Replace.
- The server runs the same function over the cells it gets, so a request with diagonal steps is bridged the same way.
  A request whose jumps add up to more than four times the cap is refused before it is walked.

## Plan (`MappingToolsPipes.Plan`, shared)

- Each path cell opens to the path cells before and after it. Cells that are next to each other but not consecutive
  stay apart, as two pipes running side by side do.
- Auto-join adds two things. A cell opens to any pipe that already points at it (a device on the path, a half, an open
  end). And each end of the path runs into the plain pipe straight past it, which gains the side it needs: straight to
  T, bend to T, T to four-way, half to straight or bend. Only the ends upgrade pipes; a path running alongside an
  existing line would otherwise tee into every pipe of it.
- A cell already holding one plain pipe on the layer is rebuilt with its old sides plus the new ones when Replace is
  on, and kept as it is when Replace is off. A kept pipe joins the path only where it already points at it. Devices
  (anything with pipe nodes that isn't one of the fifteen plain pipe prototypes), and cells holding more than one pipe
  entity on the layer, are never rebuilt.
- A path cell that ends up with no sides (a lone click with nothing around) gets nothing; the server says how many
  cells it left alone.

## Server (`MappingToolsSystem.Pipes.cs`)

- The shape for a set of sides is the plain pipe whose base direction (`PipeShapeHelpers.ToBaseDirection`) turned by 0,
  90, 180 or 270 degrees gives exactly that set, the same rotation `PipeNode` applies. Pipes are spawned at the cell
  centre with that local rotation, so they anchor with the right connections at once.
- The layer's prototypes come from the primary pipe's `AtmosPipeLayersComponent.AlternativePrototypes`, read on the
  server, never from the client. `WFMappingCanEditEvent.Prototypes` names the primary ids: the `...Alt1` and `...Alt2`
  pipes are `HideSpawnMenu`, which MappingMode's prototype check refuses, and they are the same pipe on another layer.
- A placed pipe on a cell with no floor refuses the whole draw instead of skipping the cell as entity shapes do. A
  skipped cell would leave the pipes on both sides open towards nothing.
- Colours: an explicit colour paints every pipe laid or rebuilt. Auto paints the new pipes with the joined pipe's colour
  and keeps the colour of rebuilt ones. A rebuilt pipe whose sides don't change isn't respawned, only painted if the
  colour differs.
- The edit puts the rebuilt pipes in a deleted group (applied first, so the new pipe never overlaps the old one) and
  the new pipes in a created group. Painting is a new `MappingEdit.PipeColors` list of colour changes on the same
  entities, so undo and redo don't respawn anything for it. Undo checks count those changes as touched entities.
- The check is `Paste` with the new pipes as entities and entity cells, the rebuilt pipes as `Moving` (so the per-tile
  count doesn't count a pipe that is about to go) and the path length as selection cells.
- Network paint floods from the pipe nodes on the clicked cell: the chosen layer if a pipe is there, else the first
  layer that has one. It follows sides that face each other with the same node group and layer, exactly the rule
  `PipeNode.GetReachableNodes` joins nets by, so it works on a paused, uninitialised mapping map without waiting for
  node groups to form. It goes through devices' pipe nodes the same way, so it stops where a pump or valve splits the
  net. Only entities with `AtmosPipeColorComponent` are painted. The flood stops at 16384 entities for everyone; for
  mappers the edit check refuses it whole past `max_edit_entities`.

## Client

- `WFMappingPipeMode` (`Modes/`) is the handler; the settings live on `WFMappingBuildModeSystem` beside the shape and
  erase settings, and Properties shows them in Pipe mode. The preview is the shape overlay, in the preset colour (grey
  for Auto, red past the cell cap).
- Right click drops a drag, or disarms Paint a network. Escape does the same, then goes back to Place.
- Pipes are subfloor; the Layers tab shows subfloor by default.

## Noticed, not changed

- `AlignAtmosPipeLayers` (`Content.Client/Atmos/AlignAtmosPipeLayers.cs`) swaps the placement's prototype for the
  `...Alt` one when the cursor is over layer 2 or 3, and `WFMappingModeSystem.AllowPlace` refuses those for mappers
  because they are `HideSpawnMenu`. So a mapper placing a pipe or atmos device on layer 2 or 3 in Place mode is likely
  refused (read from the code, not tried). Pipe mode doesn't go through placement and isn't affected.

## Not verified

- The preview, the Properties rows and the drag in a real client haven't been looked at; the tests cover the server and
  the pure parts.
