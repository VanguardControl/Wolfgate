# Mapping undo

Undo and redo for in-game mapping. Takes back walls you dragged in the wrong place, brings back
the locker you box-erased, and puts a tile back the way it was.

## Using it

| | |
|---|---|
| `mapundo [count]` | take back the last step (or `count` steps) |
| `mapredo [count]` | put it back |
| `Ctrl+Z` | `mapundo` |
| `Ctrl+Y`, `Ctrl+Shift+Z` | `mapredo` |

Both commands need the same admin flags as `mapping` (Server + Mapping). The keys only fire for an
admin with the Mapping or Host flag, and do nothing while a text box has the keyboard, so `Ctrl+Z`
still edits chat normally. They work both in ordinary gameplay with the mapping hotbar and in the
`mappingeditor` screen; they are rebindable under Options → Controls → UI.

You get a line in the console and a popup at the cursor, e.g.

```
Undid: placed 12× WallReinforced
Undid: erased WFTraderFuelTechnician
Nothing to undo.
```

## What gets recorded

Only actions that go through the engine's placement manager, and only on a map that has **not**
been map-initialised — that is, a map made by the `mapping` command. Ordinary rounds, admin
spawning and anything on a live map are never recorded, and the handlers early-out on the map's
`MapInitialized` flag before doing any work.

Three kinds of action:

- **Placed** — undo deletes it, redo puts it back.
- **Erased** — the entity is snapshotted to YAML *before* the engine deletes it; undo loads it back
  at the same grid-local position, rotation and anchoring, redo deletes it again.
- **Tile** — the old tile, with its variant and rotation/mirror bits, against the new one.

Each mapper has their own undo and redo stack, 200 steps deep. Recording anything new throws away
that mapper's redo stack. Stacks are dropped when the map is deleted and on round restart.

### Grouping

Actions join the previous step when they come from the same mapper on the same map and either
happen in the same tick (rect erase, line and grid placement modes) or follow within 0.25 s (a
dragged line of walls). So one `Ctrl+Z` takes back the whole drag, not one wall at a time.

## How restoring works

An erased entity is saved with `MapLoaderSystem.TrySaveEntity` into a string. Options that matter:

- `MissingEntityBehaviour.Ignore` — references to entities outside the snapshot are written as
  `invalid` instead of being chased or logged. Without this, erasing one wall could drag other
  entities into the snapshot and spam the log.
- `ErrorOnOrphan = false` — the entity is deliberately saved without its grid.
- On load, `LogInvalidEntities = false`, because those dropped references are expected.

Children come along recursively, so a locker keeps exactly what was inside it, once.

`TryLoadEntity` puts the entity back in null-space as an orphan, which means the engine unanchors
it on init. So after loading we re-parent it to the recorded grid at the recorded local position
and rotation, then anchor it again by hand. Map-init is never run: pre-init entities are written
back as pre-init and paused, so a restored wall is in exactly the state a freshly placed one is.

Prototypes marked `save: false` (mobs, among others) are normally dropped by the serializer without
asking. For the length of one capture the flag is lifted on that entity's tree, so an erased NPC
comes back. The flag is restored immediately afterwards.

Restored entities get **new** EntityUids. Later stack entries do not hold raw uids; they hold a
handle that is re-pointed when the entity comes back, including the handle for an entity's parent,
so a locker restored in the same step as its contents still adopts them.

## Limits

- **External links break.** Device links, cameras and anything else pointing at an erased entity
  point at a uid that no longer exists. The command says so when it restores something.
- **Decals are not covered.** Out of scope for this pass.
- **Players are never snapshotted.** Erasing something with an `ActorComponent` is not recorded.
- **Huge bursts are capped** at 500 entities per step. Past that the step is marked truncated and
  the rest of the burst cannot be put back; the command says so.
- **Tile placement that creates a new grid**: undoing sets the tile back to empty but leaves the
  grid behind.
- **Replace mode** erases and places in the same tick, which groups into one step — undoing takes
  back both halves at once, which is what you want, but it is one step rather than two.
- Stacks live in memory on the server only. They do not survive a round restart or the map being
  deleted, and they are not saved with the map.

## Where the code is

| | |
|---|---|
| `Content.Server/_WF/Mapping/MappingUndoSystem.cs` | recording, stacks, capture and restore |
| `Content.Server/_WF/Mapping/MappingUndoCommands.cs` | `mapundo`, `mapredo` |
| `Content.Client/_WF/Mapping/MappingUndoInputSystem.cs` | the keybinds |
| `Resources/Locale/en-US/_WF/mapping.ftl` | command and message strings |
| `Content.IntegrationTests/Tests/_WF/Mapping/MappingUndoTest.cs` | tests |

Upstream files touched, all marked `// WOLFGATE`: `Content.Shared/Input/ContentKeyFunctions.cs`,
`Content.Client/Input/ContentContexts.cs`, `Content.Client/Options/UI/Tabs/KeyRebindTab.xaml.cs`
and `Resources/keybinds.yml`.
