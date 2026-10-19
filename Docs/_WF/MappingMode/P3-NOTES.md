# P3 notes: upload

Stage P3 of PLAN2.md. What was built, and the choices the plan left open.

## Built

- Shared: `WFMappingUploadEvents` (begin, ready, chunk, ack, done, cancel request, cancelled), the pure
  `WFMappingUploadAssembler`, cvars `max_upload_bytes` (1.5 MiB, replicated, clamped to the 8 MiB `MaxFileBytes`)
  and `upload_cooldown` (30 s), `WFMappingVersionKind.Upload` with the `_upload` suffix, `UploadNameFromFile`.
- Server: `WFMappingUploadSystem`, `WFMappingUploadValidator`, `TryReserveLoad`/`TryLoadOntoScratch` in the
  reference partial (reference pick now uses them), `SaveVersion` over a level list with the session overload
  wrapping it, `SaveUpload`, `HasRoomForMap`.
- Client: `WFMappingUploadClientSystem`, `WFMappingUploadRow`, Upload buttons and rows in the lobby panel and the
  Mapping window's Library tab, the `upload` kind label.
- Test: `Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingUploadTest.cs`.

## Choices

- **Dialog.** `GetFileAndName` rather than `OpenFile`, since the default map name comes from the file name. Same
  dialog, same `.yml` filter, `FileAccess.Read`.
- **Map name.** The upload goes under `UploadNameFromFile(file)` with no name prompt; an existing map of that name
  gets a new version. Nothing usable left gives `Upload`.
- **Chunks.** 128 KiB, two in flight, so the 8 MiB hard cap is at most 64 chunks.
- **Caps.** An upload is always checked against the caps of a new session (`max_grids_per_session`,
  `max_entities`, per-tile caps, `map_size` where the grids sit), wherever it was sent from, because it goes to the
  library and only ever opens as a new session. `TryLoadOntoScratch` still takes a session for reference loads.
- **Cooldown.** Spent when the server accepts the begin, not on a refused one. A refusal because the global load
  slot is busy gives it back, since that isn't the player's doing. A silent 2/s request throttle sits in front, as the
  other handlers have.
- **Every begin is answered.** A refused begin sends `WFMappingUploadCancelledEvent(0)` plus the result, so the client
  never waits on nothing; the client also gives up after 40 s without a word.
- **Validation extras.** YAML nesting deeper than 64 is refused during the event scan, since `DataNodeParser`
  recurses per level and a deep file could overflow the stack. Prototype ids go through the same renames and deletions
  the loader applies (`BeforeEntityReadEvent`), so a file with a migrated id isn't refused. A file without grids is
  refused with the reference's "no grids" message.
- **What is stored.** The engine's save of the stripped scratch map. The file's own map entity and its components
  don't survive the merge onto the scratch map, as with reference loads.
- **Outcomes for tests and admins.** `WFMappingUploadSystem.ActiveTransfer(user)` and `LastOutcome(user)` (the last
  result's Fluent id) are public, since a dummy session can't receive the result events.
- **Open from the Mapping window.** The upload row's Open is hidden there, as the library list's is: opening needs the
  lobby.
