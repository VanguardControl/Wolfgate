# Symphony

The hooks for the SSymphony panel. It is the one module outside `_WF`: its files are `Content.*/Symphony/`,
`*.Symphony.cs` and `Resources/Locale/en-US/symphony/`, as listed in `Tools/_WF/Ci/modules.yml`.

- Status host endpoints, behind the admin API token: `/symphony/hub` switches hub advertising while the server runs,
  `/symphony/players` lists who is connected with their character and job, and which admins are stealthed;
  `/symphony/roles` and `/symphony/roles/refresh` list the whitelisted roles to map and re-read a player's rows after
  the panel writes them,
  and `/symphony/cvars` lists every CVar and sets one while the server runs (confidential ones are never shown or set).
  `/symphony/commands` lists every console, Toolshed and client command with its description, help and the admin
  flags it needs, read the way the admin manager reads them, for the panel's staff handbook.
  `/symphony/lobby/shenanigans` sets a lobby shenanigan off for every client, the only way one is set off, or clears
  them all (the
  [LobbyShenanigans module](../_WF/LobbyShenanigans/README.md)).
- Player feedback: the panel reads bug reports and suggestions straight from `wolfgate_feedback_report`.
  `/symphony/feedback/status` sets a report's status and message and tells its player in game;
  `/symphony/feedback/refresh` does the telling after the panel wrote the row itself. The table, the context JSON and
  both calls are laid out in the [Feedback module](../_WF/Feedback/README.md).
- Censuses: the panel reads `wolfgate_census` and `wolfgate_census_response`, lists and saves censuses through
  `/symphony/census` (GET and POST) and deletes one through `/symphony/census/delete`; opening one tells the players
  online. Laid out in the [Census module](../_WF/Census/README.md).
- `/status` reports the hook version, the round clock and the test merges the build carries (`SymphonyTestMerges`,
  read from `Resources/Symphony/testmerges.json`, which `Tools/_WF/Ci/test_merge.py` writes). On a test merge build,
  `SymphonyTestMergeSystem` also announces the merged pull requests at round start and to each player joining the
  lobby (`symphony-test-merges-active`).
- Whitelist by Discord: with `symphony.url` set, a player the whitelist turns away gets a one-time link ticket, and
  the launcher's connect-failed screen shows Link Discord and Copy link buttons. A revoke is re-checked on reconnect.

- Web character creator (hook version 17): `/symphony/characters` and `/symphony/creator/*`, laid out below; version
  18 added the custom marking library (`/symphony/custom-markings`) and studio warps (`/symphony/studio/warps`).

`SharedSymphony.ModuleVersion` is the contract version; bump it when the status field, the refusal keys, the link
ticket table, the feedback table, the census tables or any of the endpoints change.

## Web character creator

SSymphony signs a player in with Discord, picks an account they own through `discord_links`, and edits that
account's characters here. The user id is trusted as given (SSymphony never sends one it hasn't proven); every rule
about what a character may be is applied here, as in the game's creator. All routes take
`authorization: SS14Token <admin.api_token>` (401 otherwise). Errors are JSON `{"error": "<code>", "detail": "..."}`;
a bare 404 means an older build without the route.

### Limits on requests

Save and validate bodies are at most 262144 bytes (256 KiB), art and warp bodies at most 1048576 (1 MiB); bigger
answers `413 too_large`. JSON nesting is capped at 64. One save or validate per account runs at a time; another one
while it runs answers `429 busy`. Reading the profile happens off the game thread; checking and saving it on it.

### Profiles as JSON

A profile is `HumanoidCharacterProfile` as the game's serializer writes it: the `profile:` mapping of the creator's
Export YAML, as JSON. Keys are the DataField names, camelCase (`name`, `species`, `age`, `sex`, `gender`,
`appearance {hair, hairColor, facialHair, facialHairColor, eyeColor, skinColor, height, width, markings}`,
`_jobPriorities`, `_antagPreferences`, `_traitPreferences`, `_loadouts` (DataFields on private fields keep the
underscore), `studioTweaks`, `customMarkings`, ...). A marking is `{markingId, markingColor: [...], visible}`. Every scalar is a JSON string
with YAML's meaning (`"18"`, `"true"`, `"#FFE0D0FF"`); a node with a YAML type tag carries it as a `"!type"` key.
Send profiles back in the same shape; a field left out takes its default. `sex` is `Male`, `Female` or `Unsexed`.

### GET /symphony/characters?user=&lt;guid&gt;

`200 {"symphony_module": 18, "user": "<guid>", "online": bool, "selected": int, "max_slots": int, "slots": [{"slot": int, "etag": "<16 hex>", "profile": {...}}]}`.
Empty slots are left out. `online` says the player is connected; their live preferences are read then, else the
stored ones. `404 no_preferences`: the account never played on this server.

### PUT /symphony/characters?user=&lt;guid&gt;&slot=&lt;int&gt;

Body `{"etag": "<from GET, null for an empty slot>", "profile": {...}}`. Answers
`200 {"slot", "etag", "profile", "kept": [...]}` with the profile as the game cleaned and stored it. `kept` names the
fields held at their stored values: always `bankBalance`, `persistentData` and `augments`, plus `studioTweaks` and
`customMarkings` for a player with the `WFAppearanceStudio` feature ban. A connected player's creator is sent the
change. `dropped_art` lists custom marking and warp hashes taken off because the server doesn't hold the art or an
admin blocked it. For an account that isn't connected, loadout job requirements are checked against its stored play
times, and loadouts it hasn't earned are taken off (as the game does for a connected player). Errors: `409 stale` with `"current"` (the slot as stored, or null) when the etag no longer matches;
`400 bad_slot`, `400 bad_request`, `400 bad_profile` (with the reader's message); `403 banned` (an active server ban);
`404 no_preferences`.

### POST /symphony/characters/validate?user=&lt;guid&gt;[&slot=&lt;int&gt;]

Body `{"profile": {...}}`; `200 {"profile": {...}, "dropped_art": [...]}`, cleaned as a save would be and not stored. Pass `slot` to
hold the same fields a save to that slot would hold (bank, persistent data, augments, and the studio fields under a
feature ban); without it they take their defaults.

### GET /symphony/creator/catalog[?version=&lt;v&gt;]

Everything the creator offers, built once per build or prototype reload and served from memory. `ETag` is the
version; a matching `If-None-Match` or `?version=` gets `304`.

- `version`, `symphony_module`.
- `limits`: `studio {enabled, maxOffset, minScale, maxScale, warps}` and
  `customMarkings {enabled, maxWorn, maxFrames, eraseBody, bodyReach}`.
- `layers`: the `HumanoidVisualLayers` names in enum order.
- `dolls`: `{"<species>": {"prototype": "<doll entity>", "mob": "<species mob entity>", "layers": [...]}}`, the doll entity's Sprite layers after
  inheritance in draw order (first is the bottom), raw. Body layers are mapped by `HumanoidVisualLayers` names,
  clothing slots by bookmark keys such as `jumpsuit` and `head`.
- `prototypes`: `{"<kind>": {"<id>": <raw YAML mapping as written>}}` for `species`, `speciesBaseSprites`,
  `humanoidBaseSprite` (`baseSprite: {sprite, state}`, `matchSkin`, `layerAlpha`), `marking`, `markingPoints`,
  `loadout`, `loadoutGroup`, `roleLoadout`, `startingGear`, `trait`, `job`, `department`, `traitCategory`,
  `customJobTitle`, `genitalShape`, `genitalSettings`, `genitalPreset`, `genitalSheath`, `wfMessageSound`, `emoteVoice`,
  `speechSounds`, `wfChatFont`, `typingIndicator`, `wfLegStyle`, the `dataset`/`localizedDataset` name lists species draw
  names from, and `entity`: the dolls and every entity a loadout
  or starting gear names, with their parents' components merged in (a component's fields override its parent's)
  and cut to `id`, `parent`, `name` and their Sprite, Clothing, Item, Inventory (slots and displacement maps),
  Hands, Vocal, TypingIndicator and Speech components. Species mobs are included for their default voice, message
  sound and typing bubble; `emoteSounds` is sent as a kind too.
- `names`: localised names by kind: `species`, `marking`, `trait`, `job`, `entity`, and from each prototype's own name
  field `department`, `traitCategory`, `wfChatFont`, `wfMessageSound`, `emoteVoice`, `speechSounds`, `genitalShape`,
  `customJobTitle`, `wfLegStyle` and `typingIndicator`.
- `rsis`: every `.rsi` directory the catalog names, relative to `/Textures`.

Drawing a body as the game does (`Content.Client/Humanoid/HumanoidAppearanceSystem.cs`): each layer of
`speciesBaseSprites` uses its `humanoidBaseSprite`, except Chest and Head, which use `<id><Sex>` (`<id>` when
Unsexed). With `matchSkin` the layer is `skinColor` at `layerAlpha`. Hair and facial hair are markings named by
`appearance.hair`/`facialHair`, coloured by `hairColor`/`facialHairColor`. A marking goes just above the layer its
`bodyPart` names, in profile order; sprite `i` takes `markingColor[i]`, or the colour of the sprite `colorLinks` ties
it to, else white; a marking with `visible: false` isn't drawn. The doll is scaled by `height` and `width`. Studio
tweaks: see `Content.Client/_WF/AppearanceStudio/StudioSpriteSystem.cs` and the AppearanceStudio module README.

### GET /symphony/creator/rsi?path=&lt;rsi dir&gt;/&lt;file&gt;

The file under `/Textures`, for `meta.json` or a `.png` inside a directory listed in the catalog's `rsis`
(`?path=Mobs/Species/Human/parts.rsi/head_m.png`). `ETag` is the catalog version. `404 not_in_catalog` for anything
else, `404 missing` when the file isn't there. A packaged server has no `Textures` (server packaging leaves them out
and Wolfgate's builds aren't hybrid ACZ), so there every file answers `404 no_textures`; SSymphony then serves the
file from its own checkout of the fork, still limited to the catalog's `rsis`.

### GET /symphony/creator/art?hash=&lt;64 hex&gt;

Custom marking and studio warp art as `image/png`, 64 pixels wide and laid out like an RSI: 32x32 cells two to a row,
cell `facing * frames + frame` with facings south, north, east, west. Still art is south and north on the top row,
east and west below. Animated art adds `X-Frame-Times` (milliseconds, comma separated)
and `X-Ping-Pong: 1`. `&part=erase` returns the art's 512-byte erase mask (`application/octet-stream`) instead, or
`404 no_erase` when it has none. A warp is linear data: red `128 + dx`, green `128 - dy` (image y down), alpha 0 cuts; it draws
the item's pixel at `(x + dx, y + dy)` inside the item's own 32x32 frame. `404 missing` when there is none or an admin
blocked it.

### Custom marking library and studio warps (hook 18)

The game never reads an image a player made: the browser decodes the PNG or canvas and sends raw RGBA as base64, and
the game checks the size, hashes the pixels and encodes the PNG itself, as its own editor's save does. Uploads are
frame-major: each frame is a 64x64 sheet (south and north on the top 32 rows, east and west below), 16384 bytes, one
after another; fully transparent pixels are zeroed. The erase mask is 512 bytes, a bit for each pixel of the four
32x32 facings row by row, bit `i` being `byte[i >> 3] >> (i & 7)`. This is not the PNG layout `/symphony/creator/art`
returns for animated art; re-lay fetched art before editing it.

**GET /symphony/custom-markings?user=&lt;guid&gt;** answers
`200 {"symphony_module", "entries": [{"id", "name", "hash", "placement"}], "library_limit", "daily_art_limit", "daily_art_left", "banned"}`,
entries oldest first. `placement` is `Behind`, `Skin`, `Hands`, `Hair` or `Front`. `banned` is the `WFAppearanceStudio`
feature ban (saves will be refused). `daily_art_left` is null with no daily limit.

**POST /symphony/custom-markings?user=&lt;guid&gt;** takes
`{"id": 0 or an entry id, "name", "placement", "art": null or {"pixels": "<base64>", "frameTimes": [ms, ...], "pingPong": bool, "erase": null or "<base64>"}}`.
Id 0 adds an entry; null art keeps the entry's art, so this also renames and moves. Answers
`200 {"entry", "previous_hash", "erase_dropped", "daily_art_left"}`. When `previous_hash` is set the art changed: swap
it for the new hash in the `customMarkings` of the profile being edited, as the game's creator does. `erase_dropped`
means erasing is off and the mask wasn't kept. Errors: `400 invalid` (size, more frames than `maxFrames`; frame times
are clamped to 20..10000 ms), `400 blank`, `400 bad_request`, `403 banned`, `403 feature_banned`, `404 missing` (no
such entry), `409 full` (with `limit`), `409 blocked`, `429 cooldown` (with `cooldown_ms`; one save a second per
account, shared with the game), `429 daily`, `503 disabled`. Error bodies also carry `daily_art_left`.

**DELETE /symphony/custom-markings?user=&lt;guid&gt;&id=&lt;int&gt;** answers `204`, or `404 missing`. The art stays,
as saved characters may wear it.

**POST /symphony/studio/warps?user=&lt;guid&gt;** takes `{"pixels": "<base64, one frame>"}` and answers
`200 {"hash", "daily_art_left"}`; put the hash in a `studioTweaks` entry's `warp` and save the character. Errors:
`400 invalid` (a pixel not alpha 0 or 255, blue not 0, or drawing from outside its 32x32 frame), `400 neutral` (it
changes nothing; to remove a warp, save the tweak with `warp` null), `403 banned`, `403 feature_banned`,
`409 blocked`, `429 cooldown` (with `cooldown_ms`), `429 daily`, `503 disabled`.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/Audio/ContentAudioSystem.Symphony.cs`](../Audio/ContentAudioSystem.Symphony.cs)
- [`Content.Server/Connection/ConnectionManager.Symphony.cs`](../Connection/ConnectionManager.Symphony.cs)
- [`Content.Server/GameTicking/GameTicker.Symphony.cs`](../GameTicking/GameTicker.Symphony.cs)
- [`Content.Server/Symphony/SymphonyApi.cs`](SymphonyApi.cs)
- [`Content.Server/Symphony/SymphonyCensusSystem.cs`](SymphonyCensusSystem.cs)
- [`Content.Server/Symphony/SymphonyCharactersSystem.cs`](SymphonyCharactersSystem.cs)
- [`Content.Server/Symphony/SymphonyCommandsSystem.cs`](SymphonyCommandsSystem.cs)
- [`Content.Server/Symphony/SymphonyCreatorSystem.cs`](SymphonyCreatorSystem.cs)
- [`Content.Server/Symphony/SymphonyCustomMarkingsSystem.cs`](SymphonyCustomMarkingsSystem.cs)
- [`Content.Server/Symphony/SymphonyCVarsSystem.cs`](SymphonyCVarsSystem.cs)
- [`Content.Server/Symphony/SymphonyFeedbackSystem.cs`](SymphonyFeedbackSystem.cs)
- [`Content.Server/Symphony/SymphonyHubSystem.cs`](SymphonyHubSystem.cs)
- [`Content.Server/Symphony/SymphonyJson.cs`](SymphonyJson.cs)
- [`Content.Server/Symphony/SymphonyLinkTickets.cs`](SymphonyLinkTickets.cs)
- [`Content.Server/Symphony/SymphonyLobbySystem.cs`](SymphonyLobbySystem.cs)
- [`Content.Server/Symphony/SymphonyPlayersSystem.cs`](SymphonyPlayersSystem.cs)
- [`Content.Server/Symphony/SymphonyRolesSystem.cs`](SymphonyRolesSystem.cs)
- [`Content.Server/Symphony/SymphonyStatusSystem.cs`](SymphonyStatusSystem.cs)
- [`Content.Server/Symphony/SymphonyTestMerges.cs`](SymphonyTestMerges.cs)
- [`Content.Server/Symphony/SymphonyUploads.cs`](SymphonyUploads.cs)

### Shared

- [`Content.Shared/CCVar/CCVars.Symphony.cs`](../../Content.Shared/CCVar/CCVars.Symphony.cs)
- [`Content.Shared/Symphony/SharedSymphony.cs`](../../Content.Shared/Symphony/SharedSymphony.cs)
- [`Content.Shared/Symphony/SymphonyLobbyNotice.cs`](../../Content.Shared/Symphony/SymphonyLobbyNotice.cs)

### Client

- [`Content.Client/Launcher/LauncherConnectingGui.Symphony.cs`](../../Content.Client/Launcher/LauncherConnectingGui.Symphony.cs)
- [`Content.Client/Launcher/LauncherFlourish.Symphony.cs`](../../Content.Client/Launcher/LauncherFlourish.Symphony.cs)
- [`Content.Client/Lobby/SymphonyWait.Symphony.cs`](../../Content.Client/Lobby/SymphonyWait.Symphony.cs)
- [`Content.Client/Lobby/UI/AnimatedBackground.Symphony.cs`](../../Content.Client/Lobby/UI/AnimatedBackground.Symphony.cs)
- [`Content.Client/Lobby/UI/LobbyNotice.Symphony.cs`](../../Content.Client/Lobby/UI/LobbyNotice.Symphony.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/Symphony/SymphonyCharactersTest.cs`](../../Content.IntegrationTests/Tests/Symphony/SymphonyCharactersTest.cs)
- [`Content.IntegrationTests/Tests/Symphony/SymphonyCustomMarkingsTest.cs`](../../Content.IntegrationTests/Tests/Symphony/SymphonyCustomMarkingsTest.cs)

### Localization

- [`Resources/Locale/en-US/symphony/connection-messages.ftl`](../../Resources/Locale/en-US/symphony/connection-messages.ftl)
- [`Resources/Locale/en-US/symphony/launcher-connecting.ftl`](../../Resources/Locale/en-US/symphony/launcher-connecting.ftl)
- [`Resources/Locale/en-US/symphony/test-merges.ftl`](../../Resources/Locale/en-US/symphony/test-merges.ftl)

## Non-modular edits

- [`.github/workflows/publish.yml`](../../.github/workflows/publish.yml): run name for the SSymphony panel
- [`Content.Client/Audio/ContentAudioSystem.LobbyMusic.cs`](../../Content.Client/Audio/ContentAudioSystem.LobbyMusic.cs)
  - attempt
  - an uploaded track is asked of the cache only once it is here, since the cache remembers a miss
  - a track the panel uploaded can arrive just after the playlist naming it
- [`Content.Client/Launcher/LauncherConnectingGui.xaml.cs`](../../Content.Client/Launcher/LauncherConnectingGui.xaml.cs)
  - Symphony, the Link Discord buttons, built in LauncherConnectingGui.Symphony.cs
  - Symphony, show or hide the Link Discord buttons
- [`Content.Client/Lobby/LobbyState.cs`](../../Content.Client/Lobby/LobbyState.cs)
  - a background the panel uploaded can arrive just after the status naming it
  - shown once the uploaded file is here. A big picture over a slow link can take
- [`Content.Client/Lobby/UI/LobbyGui.xaml`](../../Content.Client/Lobby/UI/LobbyGui.xaml)
  - an animated background the SSymphony panel made from a GIF, shown over the still one
  - the notice staff set from the SSymphony panel; hidden when there is none
- [`Content.Client/Lobby/UI/LobbyGui.xaml.cs`](../../Content.Client/Lobby/UI/LobbyGui.xaml.cs): the GIF background covers the screen as the still one does
- [`Content.Server/Audio/ContentAudioSystem.cs`](../Audio/ContentAudioSystem.cs): a playlist the SSymphony panel set plays in its own order, or reshuffled each round
- [`Content.Server/Connection/ConnectionManager.cs`](../Connection/ConnectionManager.cs)
  - Symphony, the Discord link as a property so the client can offer a button
  - Symphony, not one who passed a running whitelist, so a revoke holds
  - Symphony, with the Discord link that gets them whitelisted
  - Symphony, so a later revoke is checked on reconnect rather than waved through
- [`Content.Server/GameTicking/GameTicker.LobbyBackground.cs`](../GameTicking/GameTicker.LobbyBackground.cs)
  - a background the SSymphony panel set holds over every round until it is cleared
  - or one from the panel's pool, a different one each round

<!-- WOLFGATE-GENERATED END -->
