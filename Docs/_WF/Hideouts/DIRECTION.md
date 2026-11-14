# Player outposts: direction

A handover for the next session. Read this first; it is meant to replace re-reading the code. `DESIGN.md` beside it
explains how each built part works and why, and the module README (`Content.Server/_WF/Hideouts/README.md`) names the
entry points.

Last updated 2026-11-13. Branch `Player-Outposts`, worktree `Wolfgate-contracts`. Everything below marked done is
staged but not committed, and none of it has been seen in a real client.

## The vision

A player's outpost is their home on a planet: they lay it out themselves, it is there every round, and it keeps one
ship of theirs. The feel is a base builder (RimWorld, Dwarf Fortress, Starbound): free building and decoration, no
timers, no unlock chains. Outposts are also meant to be many: plan for 160 or more standing at once.

Standing rules from the owner:

- Tune numbers in prototypes (`modules.yml`, `shipwright.yml`, `safes.yml`), not in new server variables.
- Nothing is committed without review. Stage and show.
- Keep the server side light. Objects are the cost; floor tiles and decals are nearly free.
- New players must not be outclassed by a veteran's stockpile.
- Token use matters: work in few large steps, and don't re-derive what this file says.

## Done

**Buying, not upgrading.** The Tarkov-style module levels (materials, build timers, prerequisites) are gone. Each
building is bought outright on the console's Hideout tab and leaves a survey stake that lays its prefab. Thirteen
buildings in three categories (Ships, Living, Work). Any can be bought again except a hangar that already stands.
Groundworks and Defences were removed; turrets and masts are on the Trade tab.

**Flat caps.** A hideout has 4,000 floor tiles, 2,500 objects, 128 tiles across and 10 cryopods (a company's: 1,600
tiles, 1,000 objects and 6 cryopods more). Hangar pad 21 wide, shipwright bay 25. Insurance 20% to insure, 15% to
claim.

**Storage rule.** Everything built in the claim is saved. Goods are saved only inside a safe. Safes come in three
sizes against 10 storage points (lockbox 2, safe 5, vault safe 10) and are laid from the blueprint. Stakes, packed
machines and decoration laid by a prefab or the blueprint are exempt. Safes take materials, tools, seeds and produce,
clothing, paper, instruments, hand lights and power cells; never weapons, ammunition, armour, explosives or containers.

**Blueprint.** About 1,100 parts. The palette has a search box and two dropdowns (category, then section) in place of
one long list. Categories: Structure, Ship parts, Storage, Furniture, Decoration, Wall pieces, Floor decals. Shelves
carry a scope, so safes only go on outposts and ship parts only on ships. Most shelves are written by
`Tools/_WF/Hideouts/shelves.py` from the game's prototypes; re-run it after an upstream merge.

**Shipwright.** With the bay empty the console is a frame picker: search, class filter, and a preview of the frame.
Edit interior opens the blueprint on the hull: floor painted from the hull's edge grows it (1,500 a tile, on the bay,
under the largest frame's size), the removal brush takes out the frame's own walls and fittings (not hardpoints, guns
or fitted systems), and a Ship parts category adds hull walls, airlocks, thrusters, gyroscopes, generators, helm and
radar consoles, air, and antenna and radar panel fittings. The refit point cap is what stops omniships and nothing in
the interior adds to it.

**Wording.** Buttons across the Hideout tab, blueprint, shipwright and registry now say what they do (Purchase, Build
frame, Edit interior, Retrieve ship, File claim, Discard plan, and so on), with tooltips on the blueprint's tools.

**Scale.**

- Autosave checks one outpost a second, so many coming due together never hash in one tick.
- A landing save's YAML is parsed on the worker thread that unpacks it, not the main thread.
- The round's last saves are taken one outpost a tick through the post-round lobby, with whatever is left taken as
  the lobby ends, in place of all at once in the tick the round ends.
- The blueprint palette is priced once, not on every open.
- The level system's once-a-second timer and material scans are gone.
- `wf.outposts.max_live` is 200 (was 40).

## In progress

- **Verification.** Integration tests for Hideouts and Outposts pass apart from one intermittent failure, a songbird
  networking error log that predates this work. Not yet done: a real client run (and its sandbox check), and the YAML
  linter. The new ship parts, the palette dropdowns and the wording have only been compiled.
- **Safe whitelist review.** The list above is a first guess for the owner to correct. Food is missing: its component
  is server-only, so a storage whitelist can't name it; it needs a tag.

## Not done

Roughly in order of value.

1. **Loading in pieces.** A load is still one hitch of about 0.16 ms an object (0.4 s for a full hideout), once per
   outpost per round. Parsing is now off the main thread, which is part of it, but spawning the entities is not. The
   real fix is to save an outpost as several chunks and land them over ticks. With 160 outposts landing at round start
   this is the largest remaining cost.
2. **Dormant outposts.** An outpost with nobody on its world still runs power, atmosphere and machines. Stowing an
   outpost whose owner and crew have left (save, remove, land again on demand) would make the live count the number of
   outposts in use, not the number owned. Needs care around hangar ships, raids and trade orders in flight.
3. **Removing hull tiles** on a shipwright ship, with a check that the cut doesn't split the ship.
4. **Leaving loose goods out of the save file.** Saves still write them and a load strips them. Skipping them in the
   serialiser was tried and backed out: a storage component lists what it holds by entity, and serialising it with
   those entities left out throws. It needs the goods detached from their containers for the save, or the component's
   list filtered, not just skipped.
5. **Hangar ships at round end** are still all saved in one tick.
6. **Registry window.** Only its button text changed. It could show each world's plots on a map and the price of
   landing before the owner commits.
7. **Palette comforts.** Favourites and recently used parts; a hover preview of a part at full size.
8. **Trimming still to weigh** (each was an earlier decision of the owner's, so ask): certifying hand-built hulls,
   shell stakes, the home-world move fee, insurance.
9. **Prefabs.** The vault prefab has no safe in it; laying one with the vault would teach the storage rule.
10. **Company outposts.** Shared purse, extra hangar pads per member, company-only buildings and colours. Only the
    company wing and the larger caps exist.

## Loose ideas so far

Not designed, not agreed; here so they aren't lost.

- **Farming.** Botany rebuilt toward Stardew Valley: seasons or weather that matter, crops worth tending, a reason for
  the Farm building. Safes already take seeds and produce for it.
- **Rooms that mean something** (RimWorld): detect an enclosed room and what is in it, and give small comforts for a
  proper bedroom, dining room or workshop.
- **Starbound-style themes**: more material themes for stakes, and furniture sets that match them.
- **Refit roles**: split a ship's refit points into Combat, Industry and Logistics pools, after Starsector.
- **Outposts as destinations**: visitors, a shop counter, a public pad, a guest book; a reason to see someone else's.
- **Standing**: a company's outpost counting double toward its world's development.
- **Storage growth**: more storage points as a reward for something other than money, kept small.

## Where things are

| What | Where |
|---|---|
| Buildings, caps, insurance, storage points | `Resources/Prototypes/_WF/Hideouts/modules.yml` |
| Safes and their whitelist | `Resources/Prototypes/_WF/Hideouts/safes.yml` |
| Frames, refit points, hull tile price | `Resources/Prototypes/_WF/Hideouts/shipwright.yml` |
| Palette: hand-listed brushes, shelves, categories | `blueprint.yml`, `blueprint_catalogue.yml`, `blueprint_categories.yml` |
| Shelf generator | `Tools/_WF/Hideouts/shelves.py` |
| Prefab generator | `Tools/_WF/Hideouts/build_prefabs.py` |
| Buying, stakes, caps | `Content.Server/_WF/Hideouts/WFHideoutSystem.cs` |
| What a save leaves behind, safes | `WFHideoutSystem.Storage.cs`, answering `WFOutpostCargoEvent` |
| Blueprint building | `WFHideoutSystem.Blueprint.cs` (`ApplyPlan`) |
| Shipwright console, hull growth | `Shipwright/WFShipwrightSystem.Console.cs` (`PaintHull`, `IsFitting`) |
| Saves, autosave, round-end saves | `Content.Server/_WF/Outposts/WFOutpostSystem.Saves.cs`, `.SlicedSave.cs` |
| Loading and drops | `WFOutpostSystem.Loading.cs`, `.Drops.cs` |
| Hideout tab, palette, shipwright window | `Content.Client/_WF/Hideouts/` |
| All text | `Resources/Locale/en-US/_WF/Hideouts/hideouts.ftl` |

Testing: `Tools/_WF/Testing/test.ps1 -Where "test =~ 'Tests[.]_WF[.]Hideouts[.]'"` (about four minutes). In game,
`wfhideouttest` builds a world with a ready outpost and `wfhideouttest gallery` lays every layout. After adding or
moving a file under `_WF`, run `python Tools/_WF/Ci/modules.py --write`.

Tests of plain outposts (span, six cryopods, load price, autosave on a moved item, loading a save by hand onto a
fresh outpost) run with `wf.hideouts.enabled` off, because a hideout changes all five. The last is a real edge: a
hideout begins saving itself soon after it is founded, and a load by hand in that moment is told to try again.
