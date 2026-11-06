# Blueprint Artifact System (BAS)

Crews choose lost technology, go and recover its pieces from the sector's worst places, rebuild them, and sell copies
at Caelestinus Central. The further and more dangerous the site, the better the tech. Everything is per round, any
ship can take part, and pieces can be stolen.

## Setting

The sector's techweb is what survived the Corporate War: the **Inherited Codex**, common patterns every lathe knows.
Everything past it was lost, and the pieces lie scattered across the worlds and the dark past the anchors.

A **pattern shard** is a sliver of glyph-etched crystal holding part of a lost design. Common shards are old human
work: syndicate data cores, corporate vault slates. The rarest are not human. Their glyphs match the Lattice anchors,
and whoever wrote them knew the star would fall (see `WFLoreLattice`). These are the **unknown technologies**.

The Vanguard reverse-engineers syndicate equipment and treats recovered patterns with something close to reverence.
The TSF calls it salvage. The Bench calls it evidence. Under the Common Writ an unregistered object recovered beyond
the anchors is **Harbouring an Artifact**, so a Construct has to be registered at the Archaeolathe, which the Bench
runs, before it is lawful.

Keep the STC flavour, not its names: no "STC", "Omnissiah", "Mechanicus" or "tech-priest". Use Codex, Pattern, Shard,
Construct and Template.

## The loop

1. **Locate.** At the R&D console's locator, pick up to four patterns from the list and pay the locate fee.
2. **Hunt.** The locator marks a site. Fly there, fight or solve your way in, and find the shards.
3. **Bring them home.** Shards are ordinary items: they drop when you die, and anyone can take them.
4. **Decode.** Each shard is decoded at an R&D console and comes out a Blueprint Construct.
5. **Unlock.** Use the Construct on an R&D console. The pattern unlocks on its research server until the round ends.
6. **Sell (optional).** List the pattern at the Archaeolathe. You keep your unlock and earn a royalty on every print.

## Tiers

Value follows risk. A pattern's tier fixes where its shard can be found, how hard its assembly is and what it sells for.

| Tier | Where | Law | Assembly | Notes |
|---|---|---|---|---|
| I, Recovered | Moored and Slack space; Moderate worlds (Asclepiu, Merak) | Lawful | Easy | Solo-able |
| II, Lost | Adrift and Untethered space; Hazardous worlds (Fervidus, Thrascias) | Deep Writ needed for space | Medium | Bring a crew. Shards carry weak Drift |
| III, Forgotten | The Deep (band 4, past 30 km); Extreme worlds (Aerumna, Carcinoma) | Bench-sanctioned expedition | Hard | Always rolls one unknown tech. Strong Drift. Public signal |

Band and world difficulty come from what already exists: `WFLatticeSystem` bands and the `difficulty` on
`wfPlanetPoiTable` entries.

### Sanctioned vs. unsanctioned

When a request goes into Lattice space, the locator offers a choice:

- **Sanctioned:** pay the Bench fee. The hunt is registered and its shards are lawful. Judges leave you alone.
- **Unsanctioned:** no fee. The shards carry the `WFUnlawful` contraband tag until registered, and the crew is
  trespassing. Judges can ticket them and seize the shards into bond.

This gives the judges and the cold war something to do with BAS, without either side being forced to play along.

## Machines

### On the R&D console

The locator and the decoder are both part of the R&D console every research ship already has, so no map changes are
needed. The console links to the ship's research server.

- **Lost patterns** button: opens the locator window.
  - **Patterns:** every BAS pattern with its tier and whether this server has it.
  - **Hunt:** pick up to 4 patterns (one tier per request), see the fee and choose sanctioned or not. Shows the active
    hunt's site and its shards' state. One active hunt per console.
  - **Exchange** (later): what is for sale at the Archaeolathe, and listing your own patterns there.
- **Decode:** use a shard on the console. A tier-scaled read (the minigame later, through `_WF/Minigames`) seals it into
  a Blueprint Construct. A failure costs the shard integrity rather than destroying it: about three failures at Tier
  III and the shard is gone.
- **Unlock:** use a Construct on the console to add its pattern to the server. The Construct is the portable form, so it
  can be carried to another ship or sold.

### Archaeolathe

A large CC machine, run by the Bench.

- **Register:** insert a Construct to make it lawful.
- **List:** the seller sets a price per print within the tier's floor and ceiling, and the Bench takes a cut.
- **Print:** a buyer pays the price plus materials and gets the item. The royalty goes to the seller's bank account.
  Buyers never own the recipe.
- **Announce:** a Tier III or unknown pattern listed for the first time triggers a sector announcement.

## Sites

The locator picks a site that suits the request's tier. It never generates a new planet site mid-round.

- **Planet:** a free site in a world table of the right difficulty, armed the same way `PlanetExpeditions` arms one.
  It never takes a site an expedition has claimed.
- **Space:** a derelict placed at the band's distance through the encounter scheduler (`latticeBand`, as the Deep
  hulks are placed).

Shards spread across hiding places the site offers:

- loot containers
- guard NPCs' inventories
- fauna butcher drops ("in the stomach of beasts")
- cavern levels below
- a Remade-filled lost ship in the Deep

Each tier sets the guards' faction and strength. The locator shows the site as a waypoint. Deep coordinates garble as the
Lattice already garbles them, so the crew has to search. A Tier III hunt is announced as a signal any ship can pick up.

An abandoned hunt, or one still unfinished when the round ends, has its unclaimed shards removed.

## Patterns

The Codex stays whole. Everything a ship needs from day to day stays in points research, and BAS takes nothing away
from it. BAS patterns are new things that can't be had any other way.

`wfBlueprintPattern` prototype: tier, site tags, minigame pool, and the recipes the pattern unlocks. It holds its own
recipes and isn't tied to a Codex technology.

### What makes a pattern

- **Unobtainable otherwise.** It isn't on any lathe, in any store or on any loot table.
- **Changes how you play.** It gives a new verb or a new option. A plain stat bump on existing gear doesn't count.
- **Broad.** Ships, people, worlds, the economy and the Lattice all get patterns, not just weapons.
- **Has a counter.** Every strong pattern has a way to be answered, so the market doesn't decide every fight.
- **Fits its tier:**
  - Tier I is lost human tech: clever and useful.
  - Tier II is corporate and military prototypes: powerful.
  - Tier III is Builder tech: it bends the rules of the sector.

Unknown patterns are Tier III Builder pieces that aren't on the locator's list until someone finds one.

### Starter catalogue

These are candidates, not commitments. Each one names the existing system it builds on. "Unknown" means it can't be
listed and only turns up on a Tier III hunt.

**Tier I: Recovered**

| Pattern | What it does | Builds on |
|---|---|---|
| Survey drone bay | Launches a remote-piloted scout that maps a site's interior and marks loot | Cockpit, Waypoints |
| Courier pad | Sends one crate to CC cargo without docking, on a long cooldown | CargoSales |
| Field kitchen synthesiser | Turns biomass into rations and drinks that buff morale | — |
| Deep-core drill rig | Anchored planet rig that slowly pulls ore from below the surface | Mining, Planets |
| Weather mast | Shows a planet's incoming weather a few minutes early | WolfWeather |
| Crew locator band set | Wearable bands whose wearers show on the ship's radar | Waypoints |

**Tier II: Lost**

| Pattern | What it does | Builds on |
|---|---|---|
| Phantom projector | Puts false contacts on enemy radars, using the Lattice's own trick. Counter: they don't answer a scan | Lattice hallucinations |
| Garble cowl | Shows your ship's name and IFF to others as Deep-garbled text | Lattice garbling |
| Grav-anchor | Pins a ship in place against tractors, harpoons and impacts while powered | TractorBeam, Bench harpoon |
| Personal ward | Belt-worn bubble that stops projectiles for a few seconds, then needs a long recharge | Remade Ward |
| Regrowth cradle | Regrows lost limbs and organs from biomass | Wolfmed |
| Song dampener | Ship module that makes its crew deaf to the Choir and shields them from Scion dread | Choir, Monolith |
| Stormbreaker | Clears the weather over a small area of a planet for a while | WolfWeather |
| Assault walker | One-off unique walker chassis | Vehicles |

**Tier III: Forgotten (Builder tech)**

| Pattern | What it does | Builds on |
|---|---|---|
| Glyph lantern | Shows which things around you are Drift hallucinations | Drift effects |
| Fold gate (pair) | Two linked pads on different grids that carry people between them | — |
| Lattice key | Passes an anchor's shield once without bringing it down. Unlawful to carry | Lattice barrier |
| Kyphrus lens | Solar collector that runs off the black hole's disk, and works best in the Deep | Power |
| Builder relay | Relay anchor that holds while the ship moves, at half strength | Lattice relays |
| Matter loom (unknown) | Weaves exotic matter into any common material | Exotic matter |
| Clockwork glyph (unknown) | Ship module that slows Drift build-up and time for the crew in a room | Drift |
| Still heart (unknown) | Implant that brings its wearer back once, Remade-free, then crumbles | Cloning, Remade |
| Pilot's echo (unknown) | Records a flight path and lets the ship fly it again on its own | Shuttles |
| Seed of Aerumna (unknown) | Plants a living patch that turns hostile ground breathable and green | Planets, GroundCover |

Each one is its own small feature, so the catalogue ships in batches after the MVP: three or four per tier first,
then more after playtest. Fewer, better ones.

## Codex retheme

The techweb rethemed as the Inherited Codex:

- discipline names
- research console strings
- one lore page

Renaming all 208 techs would mean marked edits in upstream Fluent files, so don't. Leave IDs and mechanics alone.

## Build order

1. **Data:** `wfBlueprintPattern` prototypes with their own recipes, plus the shard and Construct items. No change to
   points research. The first patterns are reskins of existing rare items, so the loop can be tested before the
   catalogue is built.
2. **Locator (minimal):** the pattern list and hunt request on the R&D console.
3. **Sites:** planet arming (reusing the expedition site picker), shard placement in containers and NPCs, and the
   waypoint.
4. **Unlock:** a Construct used on the console adds the pattern to the server.
5. **Decode:** start with a timed read, then the `_WF/Minigames` pool by tier.

   Steps 1–5 are the MVP. The loop is playable here, so playtest before going further.

6. **Lattice space:** space derelicts by band, Drift sources on shards, sanctioned hunts with the Bench fee and
   unlawful tagging.
7. **Archaeolathe:** register, list, print, royalties, announcements, and placement on the CC map.
8. **Catalogue:** patterns in batches, each with its own test. Then the unknown patterns and the Tier III signal.
9. **Codex retheme and lore pages.**

Each step gets an integration test under `Content.IntegrationTests/Tests/_WF/Blueprints`:

- request → shards placed at a site of the right tier
- decode → unlock
- list → print → royalty paid

## Open points for playtest

- The fee and price curve per tier.
- Shard integrity and how many failures each tier allows.
- How often Tier III should roll an unknown pattern beyond the guaranteed one.
- Whether pirate theft of Tier III shards needs a counter, such as a homing ping from the locator that carries over.
