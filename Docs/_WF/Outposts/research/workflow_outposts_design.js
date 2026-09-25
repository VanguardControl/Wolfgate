export const meta = {
  name: 'outposts-design-doc',
  description: 'Read Nova colony_fabricator + Wolfgate planet/save/spawn/POI code, draft the Outposts design doc, refute, revise',
  phases: [
    { title: 'Read', detail: 'six parallel readers write notes files' },
    { title: 'Draft', detail: 'architect completes the HackMD' },
    { title: 'Refute', detail: 'three lenses attack the draft' },
    { title: 'Revise', detail: 'architect folds in the critiques' },
  ],
}

const SCRATCH = 'C:/Users/jzo12/AppData/Local/Temp/claude/C--Users-jzo12-Documents-GitHub-Wolfgate--claude-worktrees-ship-broadcast-pa-system-04bd45/6d42945e-aae7-4a3a-b481-435937919ded/scratchpad/outposts'
const WORKTREE = 'C:/Users/jzo12/Documents/GitHub/Wolfgate/.claude/worktrees/ship-broadcast-pa-system-04bd45'
const PLANETS = 'C:/Users/jzo12/Documents/GitHub/Wolfgate/.claude/worktrees/modest-chaum-01364c'
const NOVA = 'C:/Users/jzo12/Documents/GitHub/NovaSector/modular_nova/modules/colony_fabricator'
const NOVA_ROOT = 'C:/Users/jzo12/Documents/GitHub/NovaSector'
const TG = 'C:/Users/jzo12/Documents/GitHub/tgstation'
const ORIGINAL = SCRATCH + '/HACKMD_ORIGINAL.md'
const DOC = WORKTREE + '/Docs/_WF/Outposts/OUTPOSTS_DESIGN.md'

const COMMON = `
Context: Wolfgate is a Space Station 14 fork (Monolith -> Frontier -> DeltaV -> CE layers). The user is designing "Outposts": buildable, saveable player outposts on planets (think RimWorld), with spawn options, POIs, NPC raids, farming, gizmos. Their draft design doc is at ${ORIGINAL} — read it first so you know what facts matter.
You are READ-ONLY on every repository. Never edit, build, run git write commands, or create files inside any repository. The only place you may write is ${SCRATCH}.
Write your notes as a Markdown file at the path given below. Notes must be concrete: exact type/prototype/file names with paths, field names, numbers, and short quotes where a detail matters. Prefer facts over opinions; put a short "Implications for Outposts" list at the end. Aim for 150-400 lines; do not pad. Return only the structured output.`

const NOTES_SCHEMA = {
  type: 'object',
  properties: {
    notesPath: { type: 'string' },
    summary: { type: 'string', description: '5-10 line summary of the most important findings' },
    gaps: { type: 'array', items: { type: 'string' }, description: 'things you could not determine' },
  },
  required: ['notesPath', 'summary', 'gaps'],
}

const READERS = [
  {
    key: 'nova_machines',
    prompt: `${COMMON}
Task: catalogue every machine and appliance in Nova Sector's colony_fabricator module at ${NOVA}/code/machines and ${NOVA}/code/appliances (and ${NOVA}/code/looping_sounds.dm, ${NOVA}/code/repacking_element.dm). For EACH machine/appliance (arc furnace, ore silo, power storage unit, RTG, solar panels, solid fuel generator, stirling generator, thermomachine, wind turbine, co2 cracker, foodricator, recycler, space heater, wall cell charger, chem machines, griddle/macrowave/range, anything else): name, what it does, inputs/outputs, key numbers (power output or draw in watts, capacities, rates, fuel types, temperatures), materials cost if defined there, how it is packed/deployed (flatpack, repacking element, wrench), what makes it "colony grade" versus the normal tg machine it derives from (read the parent in ${TG} if needed), and available icons/sounds under ${NOVA}/icons and ${NOVA}/sound. Note any interesting mechanic worth porting to SS14 (e.g. wind turbine needing open sky/planet, solid fuel generator burning wood, ore silo sharing). Also note ${NOVA}/sound/attributions.txt licensing.
Write notes to ${SCRATCH}/notes_nova_machines.md.`,
  },
  {
    key: 'nova_fabricator',
    prompt: `${COMMON}
Task: document the colony fabricator itself and everything around it: ${NOVA}/code/colony_fabricator.dm (how it is fed, materials, the design flag system, what it can print, print times, how it is obtained), ${NOVA}/code/design_datums/** (list every design grouped by category with material costs; summarise the rations printer designs), ${NOVA}/code/cargo_packs.dm (every cargo pack: contents and credit cost), ${NOVA}/code/construction/** (prefab walls, windows, doors, manual doors, turfs: how they differ from normal construction, costs, whether they need tools/power), ${NOVA}/code/tools/tools.dm. Then search the rest of ${NOVA_ROOT}/modular_nova/modules for other colony/outpost-adjacent modules (grep folder names and code for: colony, outpost, prefab, flatpack, self_sustain, wind, fulton, mining drone/bot, hydroponics on planets, cabin, camp, tent, settlement) and give a one-paragraph summary of each relevant one with paths. Finally describe how Nova intends a colony to bootstrap and be self-sufficient (the progression from cargo pack to running colony).
Write notes to ${SCRATCH}/notes_nova_fabricator.md.`,
  },
  {
    key: 'wf_planets',
    prompt: `${COMMON}
Task: document Wolfgate's AS-BUILT planet implementation on the Planets-and-cracking branch, checked out at ${PLANETS} (read only there; do not touch its git state). Start with ${PLANETS}/Content.Server/_WF/PlanetCracker/README.md and ${PLANETS}/Docs/_WF/PlanetCracker/*.md, then the code under ${PLANETS}/Content.Server/_WF/PlanetCracker, ${PLANETS}/Content.Shared/_WF/PlanetCracker, ${PLANETS}/Content.Client/_WF/PlanetCracker and ${PLANETS}/Resources/Prototypes/_WF/PlanetCracker. Cover: how a planet surface is built (WFPlanetSurfacePrototype fields, planet_surfaces.yml, kyphrus_planets.yml, the DeltaV planet prototype + BiomeSystem it uses — is the ground infinitely chunk-generated? what would a bounded/set-size planet need?), the z-level stack (WFPlanetNetworkSystem, layers, orbit layer, WFPlanetLayerComponent, WFOrbitLayerComponent), what counts as ground vs hull (WFDetachedTerrainComponent, WFPlanetBuiltTilesComponent, lattice on ground via WFFlightSystem.Lattice), WFPlanetGroundSpawnedEvent, day/night + weather (WFPlanetWeatherSystem*, is there an "is it night" query, storm phases, planet_weather.yml), timepiece, gravity per planet (WFPlanetEnvironmentComponent? anything about gravity strength), fauna/wildlife spawner (WFPlanetFaunaSystem, WFPlanetWildlifeComponent, fauna.yml, biomass), jetpacks in atmosphere (SharedJetpackSystem.WfInAtmosphere marked edit — find it), parachutes (WFParachuteSystem: how a thing dropped from orbit falls and lands), landing (landing thrusters, CEZLevelsSystem.WFLandingClearance — what makes a landing legal/where you can land, WFLiftoffAttemptEvent, WFGridLiftLoadEvent), crash/skid/breakup, orbit decay/station keeping, gravity well, ship radar terrain, admin Planet Control, the wfplanet command, ShipPa hooks used for GPWS callouts, and the Carcinoma infestation. List the marked upstream edits (grep "WOLFGATE(PlanetCracker" across ${PLANETS}/Content.*) so the Outposts doc knows which upstream systems are already hooked.
Write notes to ${SCRATCH}/notes_wf_planets.md.`,
  },
  {
    key: 'wf_save_access',
    prompt: `${COMMON}
Task: document, from the main-based checkout at ${WORKTREE} (read only), everything Outposts' saving, pricing, ownership and access would build on:
1. Monolith Persistence (Content.Server/_Mono/Persistence/*, Content.Shared/Preferences/PersistentProfileData.cs, Content.Shared/_Mono/Persistence, the DB Model.cs additions, ServerDbBase changes, HumanoidProfileEditor.xaml.cs changes, PersistAtRoundEndSystem, PersistenceCommand): what is stored per character, how it round-trips through the DB and the profile editor, size limits, how round end triggers it.
2. Grid save/load: MapLoaderSystem API (TrySaveGrid/TryLoadGrid/serialising a grid to yaml in memory — find the exact methods in RobustToolbox/Robust.Shared/EntitySerialization or Map), Content.Server/_WF/Shipyard/UsedShipMarketSystem.cs + UsedShipReinitSystem.cs (in-memory save of a sold ship and re-init on load), any Frontier/Mono ship-save code.
3. Shipyard: Content.Server/Shipyard or _NF Shipyard ShipyardSystem (TryPurchaseShuttle, TrySellShuttle, TryAssignDeed, ShuttleDeedComponent, rename), Content.Server/_WF/Shipyard/ShipyardSystem.Wolfgate*.cs and ShipyardWolfgateEvents.cs, PricingSystem.AppraiseGrid, sale rate/tax, ShipSoldEvent.
4. Money: bank account system (BankSystem, where balance lives, station bank / TaxAccounts), how purchases are charged.
5. Ship previewer: Content.Client/_WF/ShipPreview (how it takes a grid path or an entity to render).
6. Access today: AccessReaderComponent, ID card access, how a bought ship gets access (deed/ID), Monolith factions/IFF (faction access), any per-door configuration UI (access configurator), Mono "ship access" or "crew manifest" features, right-click verbs on doors. Also Content.Server/_WF/Station and Content.Server/_WF/Roles.
7. Grid anchoring: how a grid is made immovable today (Static body, ShuttleComponent removal, FTL lock, WF force-anchor), and how the planet branch keeps landed grids put.
Write notes to ${SCRATCH}/notes_wf_save_access.md.`,
  },
  {
    key: 'wf_spawn_jobs',
    prompt: `${COMMON}
Task: document, from the main-based checkout at ${WORKTREE} (read only), everything Outposts' spawn options and multiplay would build on: GameTicker round flow (lobby, ready-up, round start player spawning, late join, respawn), StationSpawningSystem/SpawnPlayer and spawn points (SpawnPointComponent, arrivals, Frontier/Mono changes), job selection and the lobby character editor (tabs, loadouts: LoadoutSystem/RoleLoadout, how a custom loadout is stored on the profile), Content.Shared/_WF/Roles + Content.Server/_WF/Roles (CustomJobTitle), Content.Server/_WF/Station/StationJobsSystem.Overflow.cs, cryosleep/cryopod (Frontier CryoSleep: leaving and returning; any "return to your body" flow), ghost roles and how NPC crews are offered, Content.Client/_WF/Spawning (say what it is), how a purchased ship is tied to the buyer (deed on ID, ShuttleDeedComponent) and whether anything lets a player spawn INTO a ship at round start (grep _Mono and _NF for starting-ship, home ship, "spawn on shuttle", ArrivalsSystem), the shuttle console pilot lock (how to make a console uncontrollable: PilotComponent, ShuttleConsoleLock, FTL lock, WF ShipStatus), Content.Server/_WF/ShipPa Announce/StartAlarm/SetCode API for a crash countdown, admin "spawnvessel"/vessel spawner in Content.Server/_WF/Administration (how it spawns a vessel grid at a place), and anything resembling parties/groups/lobbies (grep "party", "squad", "group", "invite" in Content.Shared and Content.Server, incl. _NF/_Mono/_DV).
Write notes to ${SCRATCH}/notes_wf_spawn_jobs.md.`,
  },
  {
    key: 'wf_poi_npc_farm',
    prompt: `${COMMON}
Task: document, from the main-based checkout at ${WORKTREE} (read only), everything Outposts' POIs, attacks, farming, trade and gizmos would build on:
1. POIs: Frontier PointOfInterestPrototype + the system that spawns them (Content.Server/_NF/... PointOfInterest / StationSystem rules), Mono sector generation (StarSystemKyphrus rule, PlanetEntity, warp points, FTL beacons, ProtectedGrid), how many POIs exist and where their maps live; procedural generation available (Content.Server/Procedural dungeon configs, salvage expeditions, DungeonConfigPrototype list) for ruins/structures.
2. NPCs and raids: NpcFactionSystem + factions list, HTN/NPC combat capability, existing NPC ship/raid events (_Mono, _NF StationEvents: pirates, ghost-role raiders, "bloodmoon"?), vehicles (Mono vehicle system, if any), mob spawners (fauna, burrows/nests like SpawnerComponent/ConditionalSpawner, Mono wildlife on planets).
3. Defence: turrets (WeaponTurret prototypes, BaseWeaponTurret, Mono ship weapons + hardpoints, Wolfgate carrier consoles), point defence, IFF/faction targeting, sensors (radar, motion/proximity sensors, Mono "early warning"/IFF radar), shields.
4. Trade/cargo: CargoOrderConsole/cargo system and how orders arrive (cargo shuttle? pallets? Frontier "cargo pallet"/"trade crate"), station bank taxes, anything named "Universal Trade Hub"/"trade hub"/"UTH" (grep), vending/market modifiers.
5. Farming/animals: Content.Server/Animals (EggLayer, Udder, Wool), reproduction/breeding systems (DeltaV/Nyano/Floof/HardLight: Reproductive, Breeding, pregnancy), animal taming/herding/leashes, hydroponics on planets, Mono planet ore/biomass.
6. Power/utility gizmos already in the fork: solar panels, RTG, PACMAN/TEG, Mono GeneratorCRPinch/Sterling/other static generators, wind (grep "wind"), batteries/SMES, the WF timepiece/weather readout, comms consoles + radio/telecom ranges across maps, mining bots/drones (grep "MiningDrone", "drone", "bot"), ore processing (ore processor, arc furnace equivalents), material silos (ore silo? "MaterialSilo"), flatpacks (FlatpackComponent/flatpacker), autolathes/protolathes and how recipes are gated.
Write notes to ${SCRATCH}/notes_wf_poi_npc_farm.md.`,
  },
]

phase('Read')
const notes = await parallel(READERS.map(r => () =>
  agent(r.prompt, { label: 'read:' + r.key, phase: 'Read', schema: NOTES_SCHEMA, model: 'opus' })
    .then(n => n && { key: r.key, ...n })))
const good = notes.filter(Boolean)
log(`readers done: ${good.length}/${READERS.length}`)
for (const n of good) log(`${n.key}: ${n.summary.split('\n')[0]}`)

const NOTE_FILES = good.map(n => `- ${n.key}: ${n.notesPath}`).join('\n')
const GAPS = good.flatMap(n => n.gaps.map(g => `- (${n.key}) ${g}`)).join('\n')

const DRAFT_BRIEF = `You are the architect finishing a design doc for "Outposts" in Wolfgate (SS14 fork). The user's own draft is at ${ORIGINAL}; it is a HackMD the user will paste back into HackMD for their team/community, so keep THEIR section structure, order, casual voice and every point they made (you may tighten wording and fix typos, never drop a decision). Fill every "TBA" and thin section, and expand each section with the concrete mechanics needed to make it buildable. Reader notes (facts from the real code and from Nova Sector's colony_fabricator module) are here:
${NOTE_FILES}
Reader gaps (things nobody could confirm; treat as open questions, do not invent):
${GAPS}

Read the original and ALL notes files fully before writing.

Required content of the finished doc (in addition to the user's sections):
- Every feature bullet tagged with one of: [EXISTS] (already in the fork, name the system), [PORT: Nova] (from colony_fabricator, name the .dm), [NEW]. Reuse beats new; when something exists, say how the outpost hooks it rather than re-inventing it.
- "Outpost Gizmos": a full catalogue as tables per category (General, Defense, Power Generation, Fabrication, Orbital Defense, plus Life Support/Atmos, Storage, Kitchen/Food, Comms/Logistics, Mobility if warranted). Columns: Gizmo | What it does | Numbers (power, rates) | Source tag | Tier/price band. Draw heavily on Nova's colony fabricator, ore silo, PSU, RTG, solar, solid fuel generator, stirling, thermomachine, wind turbine, CO2 cracker, foodricator, recycler, space heater, wall cell charger, chem machines, kitchen appliances, prefab walls/doors/windows/manual doors, tools, rations printer, cargo packs — adapted to SS14/Wolfgate (say what changes). Include the user's own ideas (time/weather console, early warning, wind turbine, comms, cheaper components, mining bots, orbital defense).
- "Planet Outpost Kit" and the preset outposts: exact contents and price bands, referencing Nova's cargo packs as the model.
- Set-size planets and story-gen POIs: how bounded worlds fit the as-built planet stack (say what the notes show is infinite/chunked today and what bounding needs), a POI taxonomy table (the user's list: mines, abandoned outposts, automated outposts, tradeposts, derelict capital ships, small derelicts, farms, workshops, ruins, shrines, alien artifact sites) with what each offers, whether claimable, beacon behaviour, and placement rules (spacing from outposts/crash sites).
- Saving/loading: the concrete pipeline (serialise grid -> per-character persistence -> price = parts + 50% -> clearance check -> 30 s ghost overlay -> load anchored on the console), autosave, limits (3 + autosave), preview, transfer/sale rules, what is stripped on save (players, ghost roles, money?) and anti-dupe rules.
- Spawn options and Multiplay/Play Groups: the flow in the lobby, what the server has to check, Outpost Job (custom loadout), Outpost Cryopod, Shuttle Crash sequence step by step with timings, and the spawn-distance rules.
- Ship Access Overhaul: data model (owner, allow list, 4-digit code, per-door overrides), UI, defaults for faction ships, verbs.
- Landing pads: how it works with the as-built lift/landing rules (pads waive lift; "roughly above" tolerance; take-off only from the pad).
- Trade panel + Universal Trade Hub + parachute delivery using the parachute system.
- Farming: capture, pens, breeding/herding, products, using what exists.
- Outpost Attacks: threat sources, the "Active" state tied to the day/night system, escalation ladder from rabid rabbit to raider party, arrival modes (burrows, shuttles, vehicles), early warning, what gates them.
- Jetpacks in the atmosphere: atmospheric jetpack on welding fuel, gravity gate.
- Then three closing sections: "Under the hood (for devs)" (module layout Content.*/_WF/Outposts, key new components/systems/prototypes, upstream hooks needed, save format, DB, networking, performance budget for N outposts and bounded planets, rough phase plan F0..Fn), "Decisions" (numbered D1.. capturing every decision already implied by the user's draft plus defaults you chose, each one line), "Open questions" (numbered Q1.., only things a reader could not confirm or the user must rule on).

Style: Markdown, HackMD-friendly (## headings like the original, tables allowed, no HTML). Keep the user's tone in the narrative parts; technical annex can be terse. No em-dashes. Do not cite line numbers; file/type names are fine. Length: as long as it needs to be to be complete, roughly 600-1000 lines.

Write the finished doc to ${DOC} (create the folder). That is the ONLY file you may write inside the repository. Do not run git commands that change state. Return a short summary of what you filled in and any place where you had to choose a default.`

phase('Draft')
const draft = await agent(DRAFT_BRIEF, { label: 'draft', phase: 'Draft', model: 'opus', effort: 'high' })
log('draft written')

const LENSES = [
  {
    key: 'feasibility',
    prompt: `You are a skeptical Wolfgate engineer. Read the Outposts design doc at ${DOC}, the original at ${ORIGINAL}, and the reader notes:\n${NOTE_FILES}\nAttack the doc on FEASIBILITY against the real code: every [EXISTS] claim that names a system that does not exist or does not do that, every mechanic that contradicts the as-built planet stack (z-levels, landing clearance, lattice on ground, weather, persistence, map loader, shipyard, access), every place where the doc under-estimates an upstream edit or a sandbox/networking trap, and any missing hook. You may read the checkouts (${WORKTREE}, ${PLANETS}) read-only to verify. Be concrete: quote the doc line, say what the code actually does, propose the fix. Return a numbered list of findings, most serious first, each with severity (blocker/major/minor). No file writes anywhere.`,
  },
  {
    key: 'gamedesign',
    prompt: `You are a game designer who has run RimWorld-like persistent-base systems on multiplayer servers. Read the Outposts design doc at ${DOC} and the original at ${ORIGINAL}. Attack it on GAME DESIGN and OPERATIONS: economy exploits (save/load dupes, price arbitrage, selling loaded outposts, autosave abuse), griefing (loading over others, raid spawns near others, play-group abuse, access-code brute force), pacing (round length vs building time, does anyone ever want to leave the outpost, why go to POIs), balance of raids vs turrets, server performance with many outposts and bounded planets, admin burden, onboarding (is the flow understandable to a new player), and anything the doc promises that will not be fun. For each finding give the concrete rule or number that fixes it. Return a numbered list, most serious first, each with severity (blocker/major/minor). No file writes anywhere.`,
  },
  {
    key: 'completeness',
    prompt: `You are an editor. Compare the Outposts design doc at ${DOC} against the user's original at ${ORIGINAL} and against the Nova colony_fabricator notes at ${SCRATCH}/notes_nova_machines.md and ${SCRATCH}/notes_nova_fabricator.md. Find: (1) any point, decision or number from the original that was dropped or changed in meaning; (2) any original section that still reads as a stub; (3) Nova machines, appliances, construction parts, designs or cargo packs that would clearly help an outpost but are missing from the gizmo catalogue, or are listed with wrong facts; (4) internal contradictions between sections (e.g. limits, prices, access rules stated twice differently); (5) decisions implied in the text but missing from the Decisions list, and open questions that are actually answered elsewhere. Return a numbered list, most serious first, each with severity (blocker/major/minor). No file writes anywhere.`,
  },
]

phase('Refute')
const critiques = (await parallel(LENSES.map(l => () =>
  agent(l.prompt, { label: 'refute:' + l.key, phase: 'Refute', model: 'opus' })
    .then(c => c && `### Lens: ${l.key}\n${c}`)))).filter(Boolean)
log(`critiques: ${critiques.length}/3`)

phase('Revise')
const revised = await agent(`You are the architect who wrote ${DOC} (your earlier summary: ${JSON.stringify(draft).slice(0, 4000)}). Three reviewers attacked it. Their findings:\n\n${critiques.join('\n\n')}\n\nRevise the doc IN PLACE at ${DOC}: fix every blocker and major finding, fix minors where cheap, and where you reject a finding say why in one line in your return text (not in the doc). Keep the user's structure, voice and every one of their points. Re-verify against the notes files (${NOTE_FILES}) before changing a fact; do not invent code that the notes do not show. Keep [EXISTS]/[PORT: Nova]/[NEW] tags accurate. Update the Decisions and Open questions lists to match the final text. The doc is the ONLY file you may write; no git state changes. Return: a list of the changes you made, the findings you rejected with reasons, and the final line count.`, { label: 'revise', phase: 'Revise', model: 'opus', effort: 'high' })

return { doc: DOC, readers: good.map(n => ({ key: n.key, summary: n.summary, gaps: n.gaps })), draft, critiques, revised }