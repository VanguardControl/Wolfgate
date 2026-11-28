# Engine

Wolfgate runs its server on **Lupine**, a thin fork of RobustToolbox at
[VanguardControl/Lupine](https://github.com/VanguardControl/Lupine). The `RobustToolbox` submodule points at it. Lupine
is an upstream release tag plus a short stack of server-side patches, rebased onto each new tag; it is not a diverging
engine.

## Rule: the client stays stock

Players connect with the standard SS14 launcher, which installs engines only from space-wizards' build manifest by
the version string the server reports. Lupine never changes that string (`Tools/publish_*.py` reads it from the
nearest upstream tag, `v295.0.3`), so clients run the unpatched upstream engine of the same version. Every patch must
therefore satisfy one of:

- it only runs on the server (`Robust.Server`, server-only paths in `Robust.Shared`), or
- it changes shared code without changing wire format, serialised types or the results the client predicts.

Any shared API Lupine adds is for server content only. `Content.Client` compiles against the fork but runs on the stock
engine, so a client-side reference to a Lupine-only member fails at load. A patch that needs the client (the client's
side of the far-distance jitter, for example) needs a forked launcher and engine CDN first; that is a separate
decision.

## Patch stack (branch `wolfgate/v295`, base `v295.0.3`)

Rebased from `wolfgate/v291` (base `v291.0.4`) on 2026-11-23, which also folded in the `netstats`, duplicate-pair and
`cold-storage` lines. Upstream v292.0.0 took over the PVS ack locks and removed the island partition, so those halves
of the first and fourth patches are gone.

| Patch | Area | Why |
|---|---|---|
| `transfer socket loss handling` | Transfer | A lost transfer websocket killed the process from a finalizer. Formerly `Docs/_WG/LiveFixes/engine-live-fixes.patch`; its `PendingAcks` lock is upstream's since v292.0.0. |
| `pvs eviction, per-session deletions, range-limited roots` | PVS | Clients never dropped entities that left view, so a ghost touring the sector held the whole sector (110k entities, 7 GB). The server now tells a client to delete entities unseen for `net.pvs_evict_seconds` (default 300), `net.pvs_evict_budget` per sweep every `net.pvs_evict_interval` seconds, and sends them as new on return. Deletion lists are built per session (only deletions newer than that client's ack) instead of one list for everyone. `PvsOverrideSystem.SetRangeLimited(uid, true)` takes a grid or map out of the global force-send, so planet layers need not be serialised for every join and full-state request; such a root is deleted on a client when it leaves view (the stock client can't re-attach a detached grid) and sent as new when a viewer returns. Content opts grids in; nothing is range-limited by default. |
| `double-precision solver origin` | Physics | Float world coordinates hold about a centimetre 100 km out, so bodies on far grids jittered and moved off-axis. The island solver now works relative to a double-precision origin per island (per body for the lone island). Server-side only until a client engine ships; the player's own predicted movement still uses the stock solver. |
| `identical-result perf fixes` | Entities, physics, serialization | `Component` and `FixtureProxy` hash by a stored counter instead of the runtime's slow first identity hash (mass spawns). `IsEntityOrParentInContainer` stops at the first grid or map parent (runs on every move). `CollideContacts` skips a sleeping pair before touching transforms. Paused awake bodies sleep until unpaused (`physics.sleep_paused_bodies`). The server precompiles the serializer's write delegates for every component on a worker thread after PostInit (`serialization.precompile`), so the first ship save no longer stalls 2 s. |
| `broadphase margin, traversal fast path, anchored swap-remove` | Physics, map | Behaviour-changing, each behind a cvar. **Off by default, opt-in for a test-server round:** the broadphase tree stores a fat box (`physics.broadphase_margin`, 0 = exact; 0.1 made the swept ship-round test detonate behind its wall, cause not yet found) so a body is reinserted and re-paired only when its exact box leaves it, with lookup queries kept exact; a body over a non-empty tile of its own grid skips the map-wide grid lookup (`physics.traversal_tile_fast_path`, off: it keeps the current grid where two overlap, and content's gravity test encodes the lookup's choice). **On:** removing an anchored entity from a tile swap-removes (`map.anchored_swap_remove`), which changes enumeration order on that tile. |
| `net: serialise broadcasts once, decrypt batches in parallel` | Network | A message to many clients is serialised once and the bytes copied per recipient (`net.broadcast_serialize_once`, on). Each tick's packets are read as a batch and the encrypted ones can be decrypted with `Parallel.For` before being handled in order (`net.parallel_decrypt`, **off** until a test server has run it, batches of at least `net.parallel_decrypt_min`). Integration tests use an in-memory network, so neither path is covered by them; the dev server boot covers the broadcast path with one client. |
| `pvs eviction sweeps a slice of the table` | PVS | A full scan of the entity table per player every 5 s cost 24 ms/s at 100 players. Each sweep now scans `net.pvs_evict_scan` slots (16384) from where the last stopped, once a second. |
| `transfer: fall back to the game connection when the socket can't connect` | Transfer | A joining client that hasn't reached the websocket channel within `transfer.http_fallback_seconds` (8) is sent a second transfer init without the websocket; the stock client takes it and switches to the game connection, so the join no longer hangs. The API lock stops a fallback after the socket connected. A player whose socket failed, dropped, or who left with "Error in transfer socket" gets the game connection straight away on later joins until restart (`transfer.http_fallback_remember`). `IServerTransferStatus` tells content which channel each player got; the join console shows it as a green or red fast-file-channel row. A stock client whose socket drops mid-session still disconnects itself once; the remembering makes that a one-off. |
| `broadphase: skip moved grids without a body instead of throwing every tick` | Physics | Round 317: content lifted a planet map (its terrain is the map's own grid, with no physics body) into a transit map. `FindGridContacts` threw on the missing body every substep and physics stopped for every map for 12 minutes. Moved grids without a body or fixtures are now dropped from the moved set with one error log each. |
| `pvs: wait for the deletions job before sessions are freed or added` | PVS | Upstream race: the job clearing freed entity slots walks every session's data on worker threads, but a disconnect freed that session's memory (and a join added a session) without waiting for it. CI crashed with an access violation in `ClearEntityPvsData`; live, a player leaving at the wrong tick could take the server down. `ProcessDisconnections` now waits for the job first. |
| `physics: scan only awake contacts` | Physics | `CollideContacts` walked every contact each step though most are between sleeping bodies (13.6 to 15.4 ms/s live). A contact is parked once it passes the checks before the sleep test and taken back when either body wakes, `CanCollide` turns off, the map changes or a refilter runs; woken contacts merge back in creation order, so results are the upstream ones (`physics.awake_contacts`, on). Gauges `physics_contact_count`, `physics_awake_contact_count`. |
| `physics: keep island lists across steps`, `lone bodies hand their island lists back` | Physics | Island lists came from capped first-in first-out pools, so they were regrown or reallocated every step (3.1 of `BuildIslands`' 3.7 ms/s). An uncapped last-in first-out free list keeps their capacity; lone bodies return theirs. |
| `physics: far grids keep their solved position, far contacts are built locally` | Physics | A float is 8 mm coarse 76 km out. The solver leaves the position it reached with the transform system for an entity parented to a map, while the float still matches (`physics.precise_root_positions`), and a far contact's manifold places the first body at zero and the second by a difference taken in doubles (`physics.precise_contacts`). Positions on the wire are still floats. |
| `net: netstats command` | Network | `netstats start <seconds>` breaks down sent traffic by message, event, component and prototype. |
| `physics: skip a pair collected twice in one step instead of asserting` | Physics | The parallel pair search can collect a pair twice in one step; the second tripped `AddPair`'s already-in-contact assert and is now skipped. |
| `pvs: force-sent entities are only added to a state when they changed` | PVS | Stock PVS re-adds every grid and map to every session's send set each tick, and the ack and leave jobs walk them again. With `net.pvs_force_delta`, a session that provably holds the full list only gets the force-sent entities modified or added since its last acked state; joins, full-state requests and lagging clients take the stock walk. |
| `map loader: load a grid over several ticks`, and four follow-ups | Map loader | Staged loads (cold storage): a grid loads over several ticks, onto an existing grid if asked, keeping references to live entities outside the save, can skip prototype migrations, can save with kept parents and put loose entities back under them, and presents each staged entity whole, with its delta components sent in full, the tick it starts. |
| `ci: build and test the wolfgate branches` | CI | Upstream's Build & Test only ran for `master`; it now runs for `wolfgate/**` pushes and pull requests too. |

Tests: `Robust.Server.IntegrationTests/GameStates/PvsEvictionTest.cs`, `PvsRangeLimitedGridTest.cs`, `PvsForceDeltaTest.cs`,
`NetTrafficStatsTest.cs`, `Robust.Server.IntegrationTests/Network/TransferFallbackTest.cs`,
`Robust.Shared.IntegrationTests/Physics/FarOriginPrecision_Test.cs`, `FarGridPrecision_Test.cs`, `AwakeContacts_Test.cs`,
`IslandListPool_Test.cs`, `PausedBodySleep_Test.cs`, `BodilessMovedGrid_Test.cs`,
`Robust.Shared.IntegrationTests/EntitySerialization/StagedGridLoadTest.cs`.

## Working on Lupine

- The Wolfgate submodule gitlink must point at a commit on `wolfgate/v295`, and there is one line: a patch for the
  live server goes on that branch, not on a side branch the gitlink follows.
- Engine bump: fetch upstream tags, `git rebase --onto vNEW v295.0.3 wolfgate/v295` onto a new `wolfgate/vNEW`
  branch, fix conflicts, push it, and let Lupine's Build & Test run on it (the PVS and physics namespaces are
  `Server.GameStates` and `Shared.Physics`). Then move the gitlink and `.gitmodules`' branch in a content PR and run
  `git submodule sync --recursive` in content checkouts.
- Generic fixes (the performance items in the engine docket) go upstream as PRs to space-wizards and are picked up at
  the next bump; only Wolfgate-specific or urgent fixes join the stack.
- New engine topics are filed in the engine docket artifact first, not patched ad hoc.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

None.

## Non-modular edits

- [`.github/workflows/wg-modules.yml`](../../../.github/workflows/wg-modules.yml): fails while the engine gitlink is not a commit on the Lupine branch .gitmodules names
- [`.gitmodules`](../../../.gitmodules): The engine submodule points at the Lupine fork (branch wolfgate/v295) instead of space-wizards.
- [`BuildChecker/git_helper.py`](../../../BuildChecker/git_helper.py): pick up the Lupine URL in clones made before the submodule moved off space-wizards

<!-- WOLFGATE-GENERATED END -->
