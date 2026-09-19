# Planet Cracking — playtest checklist

For one human on a dev server. Runs the whole loop of `PLANET_CRACKER_DESIGN.md` §2 on the code-built test hulls. As-built detail and every known deviation are in that document's §10; the placeholders are listed at the end of this file.

Each step is **Do** (what you type or click), **See** (what should happen) and **Report** (what to write down if it does not). Report anything with a file and line from the client or server log where you have one.

You need admin rights: every command here is `AdminFlags.Spawn | AdminFlags.Mapping`, and the hulls have no spawn point on them.

Times to budget: 4 min centrifuge spin-up, 5 min anchor drill (both run in parallel), 5.6–20 min cut, 60 s disconnect window, 60 s evacuation. §10 of the checklist lists the admin subcommands that skip every one of those.

---

## 1. Enable the CVar

**Do.** Run a dev server with the Build development preset. `Resources/ConfigPresets/Build/development.toml` ends with:

```
# WOLFGATE
[wf]
planet_networks = true
```

If you are not on that preset, set `wf.planet_networks true` in the server console before anything else. It is `CVar.SERVERONLY` and is read at registration time.

**See.** `wfplanet list` answers rather than refusing.

**Report.** `Planet networks are off. Set wf.planet_networks to true.` — the CVar did not take. Nothing else in this checklist will work.

## 2. Make a planet to crack

The dev preset disables the lobby, which forces the sandbox preset, which carries no star system — so nothing is registered until you make one. Two routes:

**Do (A, the real path).** Attach to an entity on the sector map (aghost is enough), then `wfplanet system SystemKyphrus`.

**See.** `Set SystemKyphrus on map <n>.` Five bodies appear — Fervidus, Merak, Asclepiu, Aerumna, Thrascias. `wfplanet list` shows Asclepiu with `surface: WFSurfaceAsclepiu` and a network id; the other four have neither. `WFSurfaceAsclepiu` has `buildAtRoundStart: true`, so its five maps are built the moment it registers.

**Do (B, the shortcut).** `wfplanet spawn WFSurfaceAsclepiu` builds a **standalone** network at the sector origin with no sector body behind it.

**See.** `Built 5 layers for WFSurfaceAsclepiu: network <n>, orbit <n>.`

**Note.** A standalone network has no body to measure range from, so the inbound FTL gate stays wide open and you can jump to the orbit layer from anywhere. Route A is the one that exercises the real range check.

**Report.** `Failed to build a network for …` (see the server log for the real cause), or a body that registers with `surface: none` when it is Asclepiu. Also report if `wfplanet list` shows a network id but you cannot see the orbit map in any FTL destination list.

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

**Orbit is a button on the shuttle console, not an FTL jump.** You do not need an FTL drive and the orbit layer is not a destination. The nav screen's settings column carries an *Enter orbit: &lt;planet&gt;* button, which lights up when the hull is parked on the body's own sector map within `orbitRange` (2000) of it; in orbit the same button reads *Leave orbit: &lt;planet&gt;*. The hop runs the ordinary FTL transit internally — 5 s warm-up, 5 s in hyperspace, the usual sound and shake — so it arrives at the same world XY on the far layer, or at the FTL arrival's free spot when something is already parked there. `WfAllowFTL` still refuses every jump **out of** a planet network from a surface or air layer, and every jump mid-transit: you climb to orbit first.

**Do.** FTL to `Asclepiu`. Once you are parked near the body, open the shuttle console's nav screen and press *Enter orbit: Asclepiu*.

**See.** The button is enabled only within 2000 of the body, and greys out (`Busy`) once the hop starts. After the warm-up and transit you arrive on a starless map with space atmosphere and a dim blue ambient light. The hull does **not** fall — the orbit layer is exempt from the CE gravity sweep. Looking down through the z-view you should see the air layers and eventually the ground.

**Report.** The button missing entirely while parked beside the body, or enabled from thousands of tiles away (the readout sweep or the range gate is wrong). A popup instead of a hop with the hull clearly in range. The hull starts falling on arrival (orbit exemption broken). Also report if you can FTL away while standing on the surface — the outbound gate should refuse that.

**Going down.** The same control block carries *Enter atmosphere: Asclepiu* under the orbit button, with the hull's lift ratio beneath it (F10). That is the only way out of orbit downward — a held **F** from orbit is refused with a popup naming the button. See §7.

**Leaving.** Press *Leave orbit* on the same console; you come back out beside the body on the sector map. The sector body's own FTL beacon still works as a second route in for a hull that has a drive. If your gravgen dies while you are on a non-orbit layer you are stranded — the console offers neither the orbit button nor a destination list; `wfplanet tp <planet>` is the escape hatch.

## 5b. Orbit decay and planet drag (F11)

**An orbit layer holds up only what is holding itself up.** A grid keeps station while one linear thruster is switched on and running (landing thrusters count, gyroscopes do not), or it is force-anchored, or it is docked to something that has one. Anything else gets sixty seconds and then falls.

**Do.** With the hull parked in orbit, read the line under the lift ratio: it says *Orbit: stable*. Now kill the hull's thrusters — cut the power, or take the thrusters apart — and watch the same line.

**See.** A PA call: *"Orbit decaying: station-keeping lost, 60 seconds to atmospheric entry"*, the caution chime, and the ship's situation code taken over by **Orbit Decay**. The console line counts down, *Orbit decaying: 42 s*. Restore power inside the minute and the countdown clears, the code goes quietly back to whatever the ship was flying under, and the line reads *Orbit: stable* again. Let it run out and the hull drops out of orbit exactly as *Enter atmosphere* would have dropped it: lift lost, the wind, the GPWS callouts all the way down, a hard landing or a crash at the bottom. A wreck on the ground stays there — nothing cleans it up.

**Also do.** Spawn a bare grid or shoot a chunk off a hull in orbit and leave it alone; it should come down on its own inside the minute with nobody aboard. Then fly around in orbit and on the air layers.

**See.** Flight over a planet is slow and heavy: about 6 m/s in orbit and 12 m/s on an air layer, with the thrusters clearly fighting the drag rather than beating it. That is deliberate — the surface streams in under you and a fast hull outruns its own terrain. Off the planet, the hull is its old self again the instant it leaves the layer.

**Report.** A hull with a running thruster decaying anyway, or a dead hull that never does. The countdown restarting, or the PA line repeating every second. The ship's own code not coming back after a recovery. A landed wreck disappearing. The chunk in the berth being given a countdown. A hull that is still fast on a planet layer, or one that stays sluggish after leaving orbit.

## 6. Survey the surface (optional, F2)

**Do.** Spawn a `WFSurveyor` and a `WFSectorSurveyConsole`. Open the console on the cracker.

**See.** One row per star-system body: distance, sanctioned/unsanctioned, cracked/intact, vein rating (poor / fair / rich / very rich) and the FTL destination name. Bodies with no Wolfgate surface read `no crackable ground`.

**Report.** Every row reading `no crackable ground` including Asclepiu (registry did not apply the surface). A rating of `unrated` on Asclepiu.

**Note.** The console is **read-only by design as built** — selecting a row is a client-local highlight and does not set the shuttle console's FTL target, contrary to §4 F2. Do not report that as a bug; the row is there to be read and typed into the map screen.

**Do.** On the surface (step 7), hold the surveyor and use it in hand.

**See.** A scan do-after, a ping, then `Found <n> dense returns.` and the revealed deep veins drawn on the ground overlay within 12 tiles. Examining one names the ore and a yield band.

**Report.** `The surveyor needs solid planet ground beneath it.` while you are plainly on ground. Zero returns over a wide sweep of several different areas — that is the known vein-density problem (§10 F2), worth recording with the area you swept.

## 7. Descend with the transport

**Lift over a planet is landing thrusters, not the gravity generator (F10).** The transport now carries two `WFThrusterLanding` instead of its gravgen; anything else you fly down there needs some bolted on, or converted with a `WFLandingThrusterKit` (spawn one, click an anchored thruster with it). The console shows the hull's *lift ratio* under the orbit button: green at 1.00 or better, amber down to 0.50, red below that. At 1.00 the hull flies exactly as it always did.

**Do.** Board the transport, take its shuttle console, and press *Enter atmosphere: Asclepiu*. Then hold **F** to descend. On the ground, use **Liftoff** on the shuttle console to ascend to orbit; click again or press **F** to cancel. **R** remains an airborne climb control.

**See.** The lift readout above the button reads about 2.6 : 1 with both thrusters powered. The hull drops out of orbit into the gap below it and hangs there; **F** then walks it down a layer at a time: orbit (depth 4) → three air layers → ground (depth 0). The altimeter tracks it. On touchdown the thrusters disable.

**FTL is barred below orbit.** From the surface, from an air layer and from mid-transit no FTL will start at all - not to a beacon, not to a listed destination, not to a dock. Climb back to orbit and *Leave orbit* first.

**Report.** A hull that FTLs away from the ground or from an air layer, through any console route. *Enter atmosphere* missing or greyed while parked in orbit. **F** doing nothing below orbit with the lift reading 1.00 or better. A hull leaving orbit on its own — orbit is parking, nothing pulls on you there. A layer skipped, or a hull arriving on the ground without the transit maps.

**Holding F in orbit does nothing on purpose.** You get a popup pointing at the console button. That is the only way down, and it is where the lift warning lives.

**Descending without lift.** Switch a landing thruster off (or unanchor one) so the readout goes amber or red, then press *Enter atmosphere*. You get a confirm dialog naming the ratio. Confirm it and the hull is in **lift lost**: it sinks and cannot climb back out. Between 0.50 and 1.00 the sink is slowed in proportion — near 0.90 it is a long slow descent; under 0.50 it falls at the full rate. Give the hull some sideways speed before you drop and it glides, picking up about 25 % more speed along its heading for each layer it falls through - and a hull that was holding station gets a 2 m/s nudge along its own facing anyway, so nothing ever comes down like a dropped brick.

**See.** The PA takes the ship over: the caution alarm starts **looping** and keeps looping for the whole emergency, with *Don't sink* out of orbit, *Sink rate*, *Terrain*, *Too low — terrain* in the last gap and *Pull up* repeating every 3 s in its final seconds. The ship's situation code shows each one on the console's PA panel and your own code comes back when it is over.

**Report.** Callouts out of order, a stage repeating, or the ship left on a flight code after it has landed. No alarms at all with the hull clearly sinking. A caution alarm that chimes once instead of looping, or one still looping after the hull is down or has got its lift back. Alarms you cannot hear with speakers online (check §Ship PA if the ship has none — the state still applies, you just cannot hear it).

**Hear.** Flying inside the atmosphere is not silent any more: a wind loop plays to everyone aboard on every air layer and in every gap between them, getting louder and higher the faster the hull is moving, and a second airframe rumble comes in under it the moment the lift goes and builds as the fall speeds up. Both stop when you land, and both stop when you reach orbit.

**Report.** Silence below orbit. Wind you can still hear after landing or in orbit. Two wind loops at once (it should be one, however long you fly), or a loop that keeps playing after the ship is deleted.

**Touchdown.** A **full free fall is now a crash** - it always was meant to be, and it was not: the threshold used to be a fixed 0.8 levels/second, which no fall in the game ever reaches, so every plummet landed softly and did nothing. It is now measured against the speed a free fall actually arrives at (about 0.47 levels/second), so dropping with no lift at all explodes the hull the way it always should have.

A **hard landing** is what you get below 80 % of that - a partial-lift sink, or a glide that shed speed. It costs you: one thud, a looping scrape, light damage across the whole hull, and heavy damage concentrated on the leading edge if you came in with any sideways speed, which normally means broken thrusters and machines along the nose and some tiles torn off it. The hull itself survives, is repairable, and flies again once the lift is back at 1.00. It then skids along the ground keeping the speed it came in with, flattening walls, crates and anything else anchored in its path. A crash that still had real speed left keeps ploughing after the blast rather than stopping on the spot.

**Report.** A slow lift-lost touchdown exploding anyway, or a full free fall walking away without an explosion (that is the bug this was). A hard landing that costs the hull nothing at all, or one that guts it. A hull that stops dead on the tile it touched instead of sliding. A skid that never stops. Tiles coming off a hull that is barely moving (the leading edge should only grind above about 4 m/s).

**Note.** Biome chunks unload about 10 s after the last viewer leaves, and nothing reserves tiles under a landed hull. A transport parked with its crew back in orbit can lose the ground under it intermittently. Record it, but it is a known limit, not new.

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
- **Every flight sound is a placeholder.** `lift_lost`, `dont_sink`, `sink_rate`, `terrain`, `too_low_terrain`, `pull_up`, `hard_landing` and `skid` under `/Audio/_WF/PlanetCracker/Flight/` are ffmpeg tones, distinct from each other and nothing more; `atmo_wind` and `fall_rumble` are stand-in copies of the stock weather wind and space wind loops. Report only that you cannot tell two of them apart, or that a loop is obviously the wrong length.
- **No fauna on Asclepiu.** Mob marker layers are deliberately absent — marker-spawned entities are never unloaded and orbiting hulls seed them.
- **The surface is deterministic.** `WFSurfaceAsclepiu` fixes its biome seed, so the same terrain and the same veins come back every round.


### Terrain, thrust volume and unprotected orbital falls

- Radar terrain remains visible in orbit, atmospheric layers, transit gaps and on the surface.
- Thruster loop gain is 60% of its former level (-10.44 dB).
- A living mob falling on its own from orbit loses one available arm and one available leg at surface impact and receives damage sufficient for critical condition. The orbital impact replaces ordinary accumulated fall damage; already-dead bodies are not revived. Short falls and occupants riding crashing ships retain their existing injury behavior.

## Liftoff and Carcinoma hull infestation

- A grounded shuttle offers **Liftoff**. With sufficient lift and power it spools up and climbs to orbit without holding a key. Cancel from the same button or by pressing descend. Losing the pilot, power/lift, or acquiring a tether cancels the ascent.
- Leave a stationary hull on Carcinoma for 45�75 seconds. Large flesh tendrils begin appearing around its perimeter, at most four; cut or attack every tendril to release the hull. The last tendril grants at least 45 seconds to launch before another can grow. A tendril on a docked hull also blocks departure.
- Sealed hulls exclude biomass. Open an exterior door and wait for growth: actual chimera flesh enters and spreads across connected hull tiles. It cannot spread onto planet terrain, extracted chunks, or through a dock onto another grid. Growth is capped at 256 biomass entities per hull.
- Supplied original audio is attributed to Gandalf under CC0-1.0. Existing borrowed audio retains its prior attribution.

### Fall and tendril effects

- An unprotected orbital fall triggers the species' scream emote once as the fall begins; surface impact plays a single wet gib splat while retaining the one-arm/one-leg critical-injury outcome.
- Each anchoring tendril plays the supplied quiet, positional loop (8-tile range, -12 dB). Removing the tendril removes its loop.
- A newly grown tendril converts existing walls in its immediate 3x3 hull neighborhood to meat walls. Doors are preserved, and terrain walls beneath the ship are unaffected.

- Carcinoma random accents now follow the same local day/night phase as its lighting and ambience beds. Night uses `carcinoma_random_sound_night_1`�`3`; dawn stops any remaining night accent. Other planets can optionally define separate day/night accent playlists, with their existing all-day lists as fallback.
- Tendril growth randomly plays `tendril_deploy_1`�`3` once, alongside its quiet persistent loop. Imported one-shots have trailing silence trimmed and are normalized to match the existing planet accents.

## Flesh ticks and pustule traps

- Spawn a flesh tick (`WFMobFleshTick`) near a non-Chimera humanoid. It should jump, remain visibly attached, and play one bite sound when it successfully latches. Confirm recurring damage affects the attachment site, blood declines, and Natural Letoferol accumulates gradually.
- Click the attached tick to remove it. Feeding must stop immediately on removal or death; removal must not leave a hidden attached entity. Chimera allies should not be attacked.
- An unlatched wandering tick occasionally plays a hunt vocalization. Death plays one death sound, without leaving a vocalization loop running.
- Approach an intact flesh pustule as a non-Chimera character. It should burst once, release exactly three ticks and a Natural Letoferol spill, then remain visibly popped. Repeated approaches must not release more ticks.
- Use the Chimera planting action on hive biomass, then try bare ground, an occupied trap tile, and an inaccessible location. Only the valid biomass location should accept a new pustule.
- Explore Carcinoma to find sparse generated pustules. Leaving and returning to a triggered site must not reset it into a new supply of ticks.

- Flesh ticks also chase and bite at close range. Each successful bite attempts to latch; at most three can attach to one host, while additional ticks can still bite. Attached ticks only use their feeding pulse. After manual removal, allow four seconds before another attack.

## Carcinoma regional ecology

- Explore widely for dormant tendon forests, assimilation sacks, and broad blood oceans; existing flesh groves and blood rivers remain. Tendons can be cut, but do not grow, damage walkers, or tether ships.
- Ambient encounters include the native aberrant flesh roster (Jared, golems, clamps, lovers, newborn variants and rare assimilated miners) alongside Chimera. They use the existing capped encounter system rather than spawning uncounted NPCs from markers.
- Assimilation sacks produce a newborn encounter at most once per four minutes while visitors are nearby and population capacity is available. They share the 4-nearby / 32-per-planet / 128-total wildlife limits. Destroying a sack stops its spawning; no independent endless spawner runs on it.
- With a hull held by tendrils, pressing Liftoff displays the remaining tendril count and the instruction to cut/destroy them. This is an attempted-liftoff popup, not a persistent cockpit warning.

## Imported flesh trees and harvested pustules

- Normal flesh trees choose randomly from the twelve replacement ordinary 64x64 frames. Pustule trees use frame 10 before harvest and frame 11 afterwards; the old shadow-tree recolour is no longer used.
- Click a pustule tree with an empty hand: receive exactly one flesh pustule and hear pustule_pop. Repeat harvesting gives nothing. Harvested terrain is pinned so leaving/re-entering cannot refill it.
- Eat the item for 15 units Natural Letoferol and 5 Nutriment in one bite. Click clear ground within two tiles to plant a normal nest, or throw it to trigger the usual nest-breaking sound, Letoferol spill and three ticks on impact. Merely dropping it does not pop it.
- Impact bursts still pop at the population cap, but omit tick spawning when capacity is exhausted; they never leave an armed grenade behind or bypass the cap.
