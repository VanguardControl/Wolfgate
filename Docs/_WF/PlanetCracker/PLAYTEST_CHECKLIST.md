# Planet Cracking — playtest checklist

For one human on a dev server. Runs the whole loop of `PLANET_CRACKER_DESIGN.md` §2 on the code-built test hulls. As-built detail and every known deviation are in that document's §10; the placeholders are listed at the end of this file. Planet setup, orbit and flight are covered by `Docs/_WF/Planets/PLAYTEST_CHECKLIST.md`; the steps that are only planet flight point there.

Each step is **Do** (what you type or click), **See** (what should happen) and **Report** (what to write down if it does not). Report anything with a file and line from the client or server log where you have one.

You need admin rights: every command here is `AdminFlags.Spawn | AdminFlags.Mapping`, and the hulls have no spawn point on them.

Times to budget: 4 min centrifuge spin-up, 5 min anchor drill (both run in parallel), 5.6–20 min cut, 60 s disconnect window, 60 s evacuation. §10 of the checklist lists the admin subcommands that skip every one of those.

---

## 1. Enable the CVar

As �1 of `Docs/_WF/Planets/PLAYTEST_CHECKLIST.md`.

## 2. Make a planet to crack

As �2 of `Docs/_WF/Planets/PLAYTEST_CHECKLIST.md`. Asclepiu (`WFSurfaceAsclepiu`) is the world this checklist cracks.

## 3. Spawn the test hulls

Both come from `wfcracker spawn`, which builds the grid at **your own position** plus a fixed offset. You must be attached to an entity on a map — a disconnected server console cannot spawn either hull.

**Do.** Stand somewhere clear (the sector map is fine) and run:

- `wfcracker spawn cracker` — 15×15 steel hull at your position + (8, 8), **with the transport built beside it and docked to its airlock** (a bought cracker arrives the same way, so the transport never has to be spawned separately; `wfcracker spawn transport` still builds a lone one). Carries: shuttle console, `WFCrackConsole`, gyroscope, FTL drive, `WFCentrifuge`, two `WFGravityProjector` on the north edge, a `WFChunkBerthMarker` (berth shrunk to 12×12, 8 tiles out, so the berth centre is grid-local (7.5, 22.5) — off the hull's north edge), an airlock, two `WFAnchorCrate`, four thrusters.
- `wfcracker spawn transport` — 7×9 hull at your position + (8, −12). Carries: shuttle console, `WFTransportGravgen`, airlock, one `WFAnchorCrate`, four thrusters.

**See.** `Built cracker <grid> with transport <grid> docked, on map <n>.` Both hulls fly. The two crates on the cracker are bound to it — an anchor from another cracker's crate will not pair with this hull's.

**Report.** `Attach to an entity on a map first.` means you ran it from the server console or as a detached ghost. There is **no collision check** on spawn, so also report an overlap with something already there — that is known, not a bug to chase.

**Note.** No survey console and no surveyor ship on the test cracker. Spawn `WFSectorSurveyConsole` and `WFSurveyor` from the spawn menu if you want to exercise F2 (step 6).

## 4. Power and FTL prerequisites

There is no cabling on either hull: every powered machine is switched to `!NeedsPower` by the factory, so power is not a thing you have to solve. What you do have to do is **switch the centrifuge on**.

**Do.** Board the cracker. Click the `WFCentrifuge`. In its window, switch it on.

**See.** The rotor dial starts turning, `Spin %` climbs, `Load <mass> / 3000` shows the hull's pooled load. It takes **240 s from cold to full** (`chargeRate: 0.00417`) — that wait is the rate, not a fault. Once it is at full the hull has gravity and can descend.

**Report.** Spin stuck at 0% after a minute; `Load` reading 0 or negative; the window throwing on open.

**Do.** Take the cracker's shuttle console and check the FTL destination list.

**See.** Asclepiu's own beacon (named `Asclepiu`). The orbit layer is **not** in the list and must not be: it is not an FTL destination at all. If the map screen shows no destinations at all, **ping the map** — `MapScreen` only rebuilds its destination tree on a ping.

**Report.** An `Asclepiu orbit` entry in the destination list (the layer was registered as a destination again). The FTL button greyed with no popup (the FTL console rejects silently on almost every path, so a dead button is the symptom of a refused jump).

## 5. Enter orbit

As �4 of `Docs/_WF/Planets/PLAYTEST_CHECKLIST.md`: FTL to `Asclepiu`, then press *Enter orbit: Asclepiu* on the cracker's shuttle console.

## 5b. Orbit decay and planet drag

As �4b of `Docs/_WF/Planets/PLAYTEST_CHECKLIST.md`. Also report the chunk in the berth (�13) being given a decay countdown: it is detached terrain and never decays.

## 6. Survey the surface (optional, F2)

**Do.** Spawn a `WFSurveyor` and a `WFSectorSurveyConsole`. Open the console on the cracker.

**See.** One row per star-system body: distance, sanctioned/unsanctioned, cracked/intact, vein rating (poor / fair / rich / very rich) and the FTL destination name. Bodies with no Wolfgate surface read `no crackable ground`.

**Report.** Every row reading `no crackable ground` including Asclepiu (registry did not apply the surface). A rating of `unrated` on Asclepiu.

**Note.** The console is **read-only by design as built** — selecting a row is a client-local highlight and does not set the shuttle console's FTL target, contrary to §4 F2. Do not report that as a bug; the row is there to be read and typed into the map screen.

**Do.** On the surface (step 7), hold the surveyor and use it in hand.

**See.** A scan do-after, a ping, then `Found <n> dense returns.` and the revealed deep veins drawn on the ground overlay within 12 tiles. Examining one names the ore and a yield band.

**Report.** `The surveyor needs solid planet ground beneath it.` while you are plainly on ground. Zero returns over a wide sweep of several different areas — that is the known vein-density problem (§10 F2), worth recording with the area you swept.

## 7. Descend with the transport

As �5 of `Docs/_WF/Planets/PLAYTEST_CHECKLIST.md`, flying the transport (two `WFThrusterLanding`) down from orbit with *Enter atmosphere: Asclepiu* and **F**.

## 8. Drag the crates down

**Do.** Take **one** `WFAnchorCrate` from the cracker onto the transport. Pull it (standard dragging) — the crates are dynamic-bodied and cannot be wrenched.

**See.** With one crate aboard the transport flies: 31.5 hull mass + 6 virtual mass for the crate, against 100 of landing-thruster lift. The console's lift readout drops from about 2.7 : 1 to 2.3 : 1 when the second crate comes aboard — cargo weighs against lift exactly as it always did.

**D11's anchor COUNT cap now needs a gravity generator.** Since F10 the transport flies on thrusters and carries no `WFTransportGravgen`, and `WFAnchorCapacity` — the "rated for 1 anchor(s)" placard and the overload popup — lives on that machine. To exercise it, spawn a `WFTransportGravgen` on the transport (`wfplanet` is not needed, just admin-spawn it) and carry two crates.

**See.** With the gravgen aboard: examining it reads `Rated for 1 anchor(s); 1 aboard.` with one crate and `Gravity generator overloaded: too many anchors aboard.` with two. The popup is the only warning and it lasts a second.

**Report.** The lift readout not moving at all when cargo comes aboard (the virtual mass is not reaching the pooled lift check). The placard or the overload popup missing with a gravgen aboard.

## 9. Deploy and pair the anchors

**Do.** On the surface, pry the crate open with a crowbar (6 s do-after).

**See.** The crate is consumed and a `WFGravityAnchor` appears.

**Report.** `Unpack the anchor on bare planet surface, not on a deck.` while standing on real ground; `There is no room here for a three by three rig.` in an obviously clear area.

**Do.** Drag the anchor into position, then wrench it down. Repeat for the second anchor, **16 to 40 tiles from the first** (centre to centre).

**See.** Once both are down inside the band they pair: the crack circle preview appears on the ground overlay, and examining either reads `Paired at <d> tiles; the cut would be <d + 4> tiles across.` (cut radius is `d/2 + 2`, D21). Out of band, the examine line reads `No partner anchor within 16 to 40 tiles.`

**Report.** A pair that will not form inside the band, a circle preview at the wrong radius, or a pair forming with an anchor owned by a different cracker.

**Note.** Dragging a 450-mass anchor feels exactly like dragging a chair. Known; no mass-scaled drag was built.

**Do.** Alt-click (or right-click) each anchor → **Start drilling**.

**See.** Sprite animates, `Drilling: <n>% complete.` on examine, and it takes **5 minutes** per anchor. Both run in parallel. When both finish they lock, and unwrenching is refused with `The anchor is drilled in. Switch it off first.`

**Report.** A drill that never completes, a verb greyed with `It has no partner anchor yet.` on a visibly paired anchor, or an anchor that unwrenches while locked.

## 10. Fissures (F8)

**Do.** Stay on the surface near the drilling anchors (NPCs sleep with no player within 32 tiles on the same map). Watch the ground around each anchor for the whole drill. Examine an anchor.

**See.** The moment a drill starts, a ring of 2–4 fissure decals appears about two tiles out with a burst and a crack sound, and 2–4 creatures climb out (Xenos on Asclepiu) and go for the anchor, then for you. Four more rings follow, each 1.5 tiles further out, at 20/40/60/80 % of the drill; earlier rings widen a stage each time. Never more than 15 creatures per anchor over the whole drill. Examine says how many fresh fissures surround the anchor. A locked anchor is quiet. On an unsanctioned world everything is ×1.5 and the table is Argocytes; Asclepiu ships sanctioned, so flip `sanctioned: false` on `WFSurfaceAsclepiu` in `planets.yml` to see it.

**Also see.** When the crack completes (step 11), a last surge of 6 creatures per anchor (9 unsanctioned) climbs out of the crack rim just before the disc lifts. They stay on the ground; none ride up.

**Report.** Rings that keep spreading after the anchor locks or after a pair is dissolved. Creatures ignoring the anchor when you are not around it. A creature carried into orbit on the chunk. More than 15 spawns from one anchor. Fissure decals vanishing on chunk reload (they are pinned). Fissures or creatures appearing from `wfcracker complete drill` (that path never arms the spawner; only `wfcracker begin drill` or a real drill does).

**Shortcuts.** `wfcracker begin drill` starts both drills for real; `wfcracker fissure ring` forces the next ring; `wfcracker fissure surge` forces the extraction surge.

## 10b. Unsanctioned notices (F9)

**Do.** Flip `sanctioned: false` on `WFSurfaceAsclepiu` in `planets.yml` (or leave it true to check the silent path), then begin the crack (step 11).

**See.** The moment the crack begins, everyone on the server gets a red "TSF Sector Watch" announcement with the attention sound naming your hull and the planet. When the chunk lifts, a second, silent one says the crack is complete. Abort (anchor break) and begin again: the first notice repeats; the second never repeats. Nothing happens on a sanctioned world. `wf.planet_cracker.announce false` silences both.

**Report.** A notice on a sanctioned world, a missing notice on an unsanctioned one, a second extraction notice, or a notice with a raw `{ $planet }` in it.

## 11. The crack console

**Do.** Fly the cracker so the **berth ghost** on the radar sits over the circle. The berth hangs 8 tiles off the hull's north edge and is 12×12 on the test hull. Then open `WFCrackConsole`.

**See.** The site diagram (hull edge, berth rectangle, two anchor dots with the circle between them, projector positions), the centrifuge rotor dial with spin and load, the two projector panels with power and integrity, the timeline strip, and the grace line reading `Hold nominal.` The offset readout says `Offset x, y — <n> tiles` and switches to `Aligned` inside 8 tiles.

**Report.** A blank or missing panel (Control draw exceptions surface in the client log; overlay exceptions are swallowed into the `clyde.overlay` sawmill, so "the circle just is not there" is a real symptom worth reporting with a screenshot).

**Do.** Press **TARGET PAIR**, spin the centrifuge to full, then press **BEGIN CRACK**.

**See.** `BEGIN CRACK` is greyed until every precondition passes; hovering lists the failing ones by name — wrong stage, no pair, not targeted, berth outside tolerance, another ship over the destination, hull linked to another grid, centrifuge missing / not at full, projectors short / unpowered / broken, planet already cracked. On begin: the gravity lock snaps the hull the last few tiles so the berth centre sits exactly over the circle centre (shake and thud), the hull force-anchors, the projectors go to `FIRING`, beams run down to the anchors — from the ground the same beams climb from each anchor towards the hull overhead — the surface shakes and rumbles, and the crack ring grows around the circle.

**Then.** `Cut remaining: <mm:ss>` counts down. The cut is `12 min × (d / 24) × part multiplier`; with two stock tier-1 projectors that is 12 min at a 24-tile pair and 5.6–20 min across the whole band.

**Report.** The snap not happening or moving the hull to the wrong place; beams drawn from the wrong end or not at all (this needs a running client — no headless test covers it); the timer not counting; or **any** working abort path. There is no abort after begin (D23) and `CLEAR TARGET` must be refused with `The cut cannot be called off once it has begun.`

**Note.** Refusals you should see rather than report: `Refused: the hull is linked to another grid and cannot be moved.` if the cracker is docked to something, and `Refused: another ship sits over the destination.`

## 12. The grace timer and the fall

**Do.** Mid-cut, break a precondition on purpose — switch the centrifuge off, or damage a projector to Broken.

**See.** `HULL LOSS IN <mm:ss>` in red on the console, naming the failing system (centrifuge below full spin / not enough projectors / a projector has lost power / a projector is broken). The countdown is **5 minutes**. Restore all three and it resets. The centrifuge counts as at full at ≥ 0.98 spin and stops counting below 0.95 (D25), so a dial hovering on the line must not flap the timer.

**Do.** Let it expire, or skip to it with `wfcracker fall`.

**See.** The force anchor comes off, the centrifuge stops counting as lift, and **both** hull and chunk are pushed into downward transit together and crash. They must fall in lockstep and never swap order.

**Report.** A countdown that flaps on power ripple; a fall that drops one grid and not the other; a fall that never arrives after the timer hits zero.

**Do.** Also damage an anchor below 50% health during the cut.

**See.** The beam sputters and `CUT SUSPENDED — anchor damaged`; the timer pauses until it is welded back. Breaking an anchor aborts to `Anchors placed` and destroying one aborts to `Surveying` — with a 30 s projector spin-down (`Spinning down: <n> s` / `Returning to <state>`) before the hull is released. Anchor loss must **never** trigger the fall (D9).

## 13. Extraction and the chunk in the berth

**Do.** Let the cut finish, or `wfcracker complete crack`.

**See.** Heavy shake, one boom, a burst effect at the hole and at the berth. The circle — both anchors, the deep veins, any walls, mobs and players inside it (D6) — is cut out as a new grid and hung in the berth directly above the hole. On the ground layer the disc is now empty tiles with a rim decal ring around it. The planet is flagged cracked, and the crack console refuses a second cut on it (`This planet has already been cracked`).

**Examine the chunk.** `A slab of planetary crust roughly <r> tiles across, cut from <planet>.`

**Report.** A chunk that is not directly above the hole; missing anchors or veins on it (a vein whose destination tile was never generated is left behind — known, but record how many you lost); a hole that heals back to terrain after you walk away and return (the pin failed); a rim ring with large gaps on ordinary flat ground.

**See.** A one-tile lattice gangway runs from the berth marker straight out to the chunk, which hangs two tiles clear of the hull with its tiles lined up to the deck. Walk it; stepping off it in orbit is a fall to the planet.

**Known and not a bug.** The chunk has **no atmosphere** (D3) — EVA from here on. A transport parked inside the circle is deliberately left behind on empty tiles. A visible tick spike at the moment of extraction is expected on a large circle.

## 14. Mine the chunk

The test hull carries no miners and there is no way to buy one. Spawn `WFCrackMiner` (ships with a full `PowerCellHigh`) or `WFCrackMinerEmpty` from the spawn menu, or let `wfcracker mine` place one for you.

**Do.** Carry a miner onto the chunk, stand it on a tile with a deep vein under it, and wrench it down. Make sure it has a cell.

**See.** It starts cutting: `mining` sprite with a glow layer, a saw loop, and ore stacks appearing beside it at **150 units a minute** (flushed in batches of 25). Examine reads the ore, the percentage left and the rate. When the seam runs out it goes to `exhausted` — unwrench it and move it to the next vein.

**Report.** `The drill will only bite on a slab of cut crust.` while you are plainly on the chunk; `There is no seam under this tile.` on a tile the surveyor marked; a miner that keeps running with a flat cell; two miners on one tile.

**Known and not a bug.** There is **no off sprite** — idle doubles as off. Inserting a self-recharging cell (microreactor, antique) will look like it worked for a tick and then be spat back out with `The bay spits the cell back out; it will not take one that feeds itself.` — the blacklist is server-only in effect, so the client mispredicts the insert. A miner built from a machine frame on the hull arrives already anchored and silently never runs; only its examine line says why.

## 15. Disconnect and evacuate

**Do.** On the chunk, switch **both** anchors off (alt-click → **Switch off**) within 60 s of each other.

**See.** The first switch-off announces `First anchor switched off. Switch the second off within 60 seconds or the first re-arms.` with a countdown on the crack console and popups near the anchors. If the second is late: `The disconnect window lapsed. The first anchor has re-armed.` On success: `Disconnect confirmed. Chunk release in 60 seconds — clear the chunk.` and an evacuation alarm on both chunk and cracker, re-issued every 15 s.

**Report.** Anchors that can be switched off before the cut is done — the refusals are `the chunk is not cut free yet.`, `the projectors are spinning down.` and `this anchor is still on the surface.` Also report an anchor that re-arms itself after a successful pairing (there is no player re-arm at all; `wfcracker rearm` is admin-only).

**Do.** Get everyone off the chunk before the timer expires.

**Known and not a bug.** A player boarding at second 59 hears nothing — the alarm freezes its recipient set at each 15 s re-issue. A fault in the centrifuge or a projector during the evacuation no longer drops the hull.

## 16. The drop, the crash and the scar

**See.** On expiry the chunk loses its force anchor and is pushed into downward transit. It falls straight through the air layers into the hole below the berth and its crash explodes it tile by tile. About **10 s later** the wreck grid is deleted; the crater decals stay. The cracker's force anchor comes off and it enters `Released`, then `Idle` after a 10 s settle, and it can fly away.

**Report.** A chunk that drifts sideways instead of landing in its own hole; a hole that is healed back to terrain by the landing (the scar exists specifically to stop that — walk away and come back to check); a cracker that never leaves `Released` (a mapper-placed force anchor will do that deliberately; anything else is a bug).

**Known and not a bug.** The crash is per-tile blasts only, with no central boom — deliberately quieter than an ordinary grid crash. Anyone still on the chunk goes down with it. Everything on the chunk that is not a minded mob, borg or occupied mind container — **including mined ore left lying there** — is deleted with the grid, silently. The rim decal ring is probabilistic in production: tile breakage inside the blast footprint can chew holes in it.

**Also worth trying.** Delete the cracker while the chunk hangs in its berth, or move it off the orbit layer. The D24 watchdog should drop the chunk on its own with `EVACUATE. This chunk has lost its cracker and is falling now.` and no countdown.

## 17. Admin shortcuts

All under `wfcracker`. Every one of them acts on the cracker hull **you are standing on** (a server console with exactly one cracker in the world falls back to that one; two or more is refused).

| Command | What it does |
|---|---|
| `wfcracker spawn <cracker \| transport>` | builds a code-built test hull beside you |
| `wfcracker state <stage>` | forces the crack stage; the only escape hatch from a stuck cut (D23) |
| `wfcracker complete <crack \| drill>` | finishes the running cut, or both anchor drills at once |
| `wfcracker disconnect` | switches both anchors off, bypassing the refusals |
| `wfcracker rearm` | puts both anchors back to Locked and drops an armed pairing window |
| `wfcracker release` | lets the chunk go now instead of waiting out the evacuation |
| `wfcracker fall` | drops the hull down the z-stack as an expired grace timer would |
| `wfcracker extract` | cuts the chunk out by hand (use after `state cracked`, which never raises the extraction hook) |
| `wfcracker drop` | pushes the chunk into transit as the watchdog would |
| `wfcracker veins` | lists every seam on the hull's chunk: ore, remaining / total, whether a miner is on it |
| `wfcracker mine` | plants a `WFCrackMiner` on the first free live seam |

Stages for `state` are the `WFCrackState` names: `Idle`, `Surveying`, `AnchorsPlaced`, `AnchorsLocked`, `Cracking`, `Cracked`, `Disconnecting`, `Released`, `Falling`. Tab completion offers all of them.

Supporting commands:

- `wfplanet list | build <planet> | delete <planet> | spawn <surface id> | system <starSystem id> | tp <planet>`
- `wfsurvey list | veins [radius] | reveal [radius]`

A useful fast lap: `wfplanet spawn WFSurfaceAsclepiu` → `wfcracker spawn cracker` → FTL to the orbit layer → drop two anchors → `wfcracker complete drill` → target and begin → `wfcracker complete crack` → `wfcracker veins` / `wfcracker mine` → `wfcracker disconnect` → `wfcracker release`.

---

## Known placeholders

Do not report these.

- **Every sprite is a generated placeholder.** Canvases and states are the real ones (`SPRITE_LIST.md`), so art is a drop-in PNG replacement, but nothing you see is final. `Decals/crack_ring.rsi` and `Tiles/crack_hole.rsi` are loaded by nothing at all, and `surveyor.rsi`'s `scanning` state is unused.
- **Effects are simple.** The beams are a single animated texture with an additive shader drawn in a client overlay, the shake is the stock 2 s grid shake on a re-trigger loop, and the crack ring is drawn by the overlay rather than staged decals. The centrifuge spin has no pitch shift — the engine exposes no `SetPitch` — so the rotor dial is the only speed cue.
- **The hulls are code-built.** There is no shipyard vessel, no cargo product for replacement anchors or miners, no mapped cracker and no outpost survey console (D18). `WFTestGridFactory` builds a 15×15 cracker and a 7×9 transport with `!NeedsPower` machines and a 12×12 berth. Every mass, capacity, `maxHandledMass` and virtual-mass figure is tuned to those two hulls and will have to be re-derived against real maps.
- **No fissures and no sanction announcement.** F8 and F9 are in progress.
- **The surface is deterministic.** `WFSurfaceAsclepiu` fixes its biome seed, so the same terrain and the same veins come back every round.
