export const meta = {
  name: 'outposts-refute-revise',
  description: 'Four review lenses attack the Outposts design doc, skeptics verify blocker/major findings, architect revises in place, final completeness check',
  phases: [
    { title: 'Refute', detail: 'feasibility, game design, completeness, Nova accuracy', model: 'opus' },
    { title: 'Verify', detail: 'one skeptic per blocker/major finding', model: 'opus' },
    { title: 'Revise', detail: 'architect folds in upheld findings', model: 'opus' },
    { title: 'Check', detail: 'final critic: nothing dropped, tags, lists, no em-dashes', model: 'opus' },
  ],
}

const MODEL = 'opus'
const REPO = '/home/user/Wolfgate'
const DOC = REPO + '/Docs/_WF/Outposts/OUTPOSTS_DESIGN.md'
const RESEARCH = REPO + '/Docs/_WF/Outposts/research'
const ORIGINAL = RESEARCH + '/HACKMD_ORIGINAL.md'
const SCRATCH = '/tmp/claude-0/-home-user-Wolfgate/881edd38-e84b-595f-b80b-797ce9a1150f/scratchpad'
const PLANETS = SCRATCH + '/planets'
const NOVA = SCRATCH + '/NovaSector/modular_nova/modules/colony_fabricator'

const NOTE_FILES = [
  '- nova_machines: ' + RESEARCH + '/notes_nova_machines.md',
  '- nova_fabricator: ' + RESEARCH + '/notes_nova_fabricator.md',
  '- wf_planets: ' + RESEARCH + '/notes_wf_planets.md',
  '- wf_save_access: ' + RESEARCH + '/notes_wf_save_access.md',
  '- wf_spawn_jobs: ' + RESEARCH + '/notes_wf_spawn_jobs.md',
  '- wf_poi_npc_farm: ' + RESEARCH + '/notes_wf_poi_npc_farm.md',
].join('\n')

const CONTEXT = `Context: Wolfgate is a Space Station 14 fork (Monolith -> Frontier -> DeltaV -> CE layers). The user is designing "Outposts": buildable, saveable per-character player outposts on planets (think RimWorld) with spawn options, play groups, POIs, NPC raids, farming and gizmos. The design doc under review is ${DOC} (about 1000 lines, an unreviewed first draft). The user's original outline is ${ORIGINAL}. Reader notes written from the real code are:\n${NOTE_FILES}\nReference checkouts, all READ-ONLY (never edit, build, or run git commands that change state; never create files anywhere): the main-based Wolfgate repo at ${REPO}; the as-built planet stack on the Planets-and-cracking branch checked out at ${PLANETS} (its module folder there is still named PlanetCracker under Content.*/_WF/ and Resources/Prototypes/_WF/; the user has since removed the planet-cracker itself, so treat the planet stack as the surviving part and note that the doc calls the module "Planets"); Nova Sector's colony_fabricator module at ${NOVA} (DreamMaker .dm source; only this module is checked out).`

const FINDINGS_SCHEMA = {
  type: 'object',
  properties: {
    findings: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          severity: { type: 'string', enum: ['blocker', 'major', 'minor'] },
          title: { type: 'string', description: 'one line' },
          docQuote: { type: 'string', description: 'short verbatim quote from the doc, or the section heading' },
          problem: { type: 'string', description: 'what is wrong, with the concrete evidence (file/type names, numbers)' },
          fix: { type: 'string', description: 'the concrete change to the doc' },
        },
        required: ['severity', 'title', 'docQuote', 'problem', 'fix'],
      },
    },
    overall: { type: 'string', description: '3-6 lines: the general state of the doc from this lens' },
  },
  required: ['findings', 'overall'],
}

const LENSES = [
  {
    key: 'feasibility',
    effort: 'high',
    prompt: `${CONTEXT}
You are a skeptical Wolfgate engineer. Read the doc, the original, and all six notes files fully. Attack the doc on FEASIBILITY against the real code. Verify in the checkouts (grep Content.Server, Content.Shared, Content.Client, RobustToolbox, Resources/Prototypes in ${REPO} and ${PLANETS}) rather than trusting the notes when a claim matters. Look for: every [EXISTS] claim naming a system, component, method or prototype that does not exist or does not do what the doc says; every mechanic that contradicts the as-built planet stack (z-level layers and orbit, landing clearance and liftoff rules, lattice/tiles on ground, FloorTileSystem hooks, weather and day/night, parachutes, ForceAnchor, gravity well, radar, orbit beacons); wrong claims about MapLoaderSystem grid save/load, the used ship market, PricingSystem, shipyard/deeds, bank, DB/persistence, GameTicker spawn flow, cryosleep, loadouts, access readers, NPC factions and HTN, turrets, cargo; under-estimated upstream edits (the doc should prefer new _WF systems subscribing to events, then partial classes, then marked edits); sandbox traps in Client/Shared; networking or performance traps (bounded planets, many grids, chunk generation); and missing hooks the pipeline needs. Also check that each [NEW] item is buildable with the engine as it is. Be concrete: quote the doc, say what the code actually does (name the file/type), propose the fix. Severity: blocker = the design cannot work as written; major = a stated fact is wrong or a needed hook is missing; minor = imprecise. Aim for the 15-40 most important findings; do not pad with style nits.`,
  },
  {
    key: 'gamedesign',
    effort: 'high',
    prompt: `${CONTEXT}
You are a game designer who has run RimWorld-like persistent-base systems on multiplayer servers with a live economy. Read the doc and the original fully (skim the notes for facts on prices, round flow, persistence and raids). Attack it on GAME DESIGN and OPERATIONS: economy exploits (save/load dupes, price arbitrage between appraisal and shop prices, selling loaded outposts, autosave abuse, trade-panel arbitrage, livestock records); griefing (loading over others, claim zones, raid spawns near others, play-group abuse, access-code brute force, parachute drops); pacing (round length versus building time, why anyone leaves the outpost, why go to POIs, does the loop hold for a 2-3 hour round); balance of raids versus turrets and early warning; server performance with many outposts and bounded planets; admin burden; onboarding (is the lobby flow understandable to a new player; how many clicks to a first outpost); persistence fairness (rich players snowball; new players); and anything the doc promises that will not be fun or will not be used. For each finding give the concrete rule or number that fixes it. Severity: blocker = will break the economy or the server; major = will be exploited or not fun as written; minor = tuning. Aim for the 15-35 most important findings.`,
  },
  {
    key: 'completeness',
    effort: 'high',
    prompt: `${CONTEXT}
You are an exacting editor. Compare the doc against the user's original line by line, and against the two Nova notes (notes_nova_machines.md, notes_nova_fabricator.md). Find: (1) any point, decision, number or phrasing of intent from the original that was dropped, weakened or changed in meaning (the original's sentences must all survive in substance, in the same section order and casual voice); (2) any original section that still reads as a stub or still says TBA; (3) internal contradictions between sections (limits, prices, access rules, timings, tags stated twice differently; a gizmo table row disagreeing with the narrative); (4) decisions implied in the text but missing from the Decisions list, Decisions that contradict the text, and Open questions that are actually answered elsewhere in the doc or in the notes; (5) [EXISTS]/[PORT: Nova]/[NEW] tags that are missing on feature bullets or clearly wrong; (6) style violations: em-dashes or en-dashes anywhere (search for the characters U+2014 and U+2013), HTML, cited line numbers, headings that break the original's structure. Severity: blocker = a user point dropped or reversed; major = contradiction or stub; minor = wording. Aim for the 15-40 most important findings.`,
  },
  {
    key: 'nova',
    effort: 'high',
    prompt: `${CONTEXT}
You are a tgstation/Nova Sector coder. Read the doc's gizmo catalogue tables, the Planet Outpost Kit, preset outposts, and every [PORT: Nova] bullet, then read the actual Nova source at ${NOVA} (code/colony_fabricator.dm, code/machines/*, code/appliances/*, code/design_datums/*, code/cargo_packs.dm, code/construction/*, code/tools/tools.dm, code/repacking_element.dm, code/looping_sounds.dm, icons/, sound/attributions.txt). Check every Nova fact in the doc: machine names, what each does, power output or draw, capacities, rates, fuels, temperatures, material costs, print times, design flags, cargo pack contents and credit costs, how flatpacks/repacking work, the .dm file named for each [PORT: Nova] tag. Flag: wrong numbers or mechanics; a named .dm that does not exist or holds something else; Nova machines, appliances, construction parts, designs or cargo packs that would clearly help an outpost but are missing from the catalogue; SS14 adaptations that miss why the Nova design works (for example what the ore silo or the wind turbine actually checks); licensing constraints on sounds/icons the doc should mention. Severity: blocker = the port as described cannot do what the doc says; major = wrong fact; minor = omission. Aim for the 15-40 most important findings.`,
  },
]

const VERDICT_SCHEMA = {
  type: 'object',
  properties: {
    upheld: { type: 'boolean', description: 'true if the finding is correct and the doc should change' },
    severity: { type: 'string', enum: ['blocker', 'major', 'minor'], description: 'your corrected severity' },
    note: { type: 'string', description: '1-4 lines: the evidence, and any correction to the proposed fix' },
  },
  required: ['upheld', 'severity', 'note'],
}

phase('Refute')
log('four lenses reading the draft')
const perLens = await pipeline(
  LENSES,
  l => agent(l.prompt, { label: 'refute:' + l.key, phase: 'Refute', schema: FINDINGS_SCHEMA, model: MODEL, effort: l.effort }),
  async (res, l) => {
    if (!res) return null
    const findings = res.findings.map((f, i) => ({ ...f, lens: l.key, id: l.key + '-' + (i + 1) }))
    log(`${l.key}: ${findings.length} findings (${findings.filter(f => f.severity !== 'minor').length} blocker/major)`)
    const verified = await parallel(findings.map(f => () => {
      if (f.severity === 'minor') return Promise.resolve({ ...f, upheld: true, verifiedSeverity: 'minor', verifyNote: 'minor, not verified' })
      return agent(`${CONTEXT}
A reviewer (lens: ${f.lens}) raised this finding against the doc:
Title: ${f.title}
Severity claimed: ${f.severity}
Doc quote: ${f.docQuote}
Problem: ${f.problem}
Proposed fix: ${f.fix}

Your job is to REFUTE it. Read the relevant part of the doc and the original, then check the primary source: the code in ${REPO} or ${PLANETS} for engine/fork claims, the .dm files in ${NOVA} for Nova claims, and the original for "the user said X" claims. For a game-design finding, check whether the doc already handles it elsewhere and whether the proposed rule is consistent with the rest of the doc. Uphold only if the evidence supports the finding; if you cannot find evidence either way, say so and uphold at reduced severity. Return the verdict with the evidence in the note (file or type names, the actual number).`, { label: 'verify:' + f.id, phase: 'Verify', schema: VERDICT_SCHEMA, model: MODEL, effort: 'medium' })
        .then(v => v ? { ...f, upheld: v.upheld, verifiedSeverity: v.severity, verifyNote: v.note } : { ...f, upheld: true, verifiedSeverity: f.severity, verifyNote: 'verifier failed; passed through' })
    }))
    return { lens: l.key, overall: res.overall, findings: verified.filter(Boolean) }
  },
)

const lensResults = perLens.filter(Boolean)
const all = lensResults.flatMap(r => r.findings)
const upheld = all.filter(f => f.upheld)
const rejected = all.filter(f => !f.upheld)
log(`findings: ${all.length} total, ${upheld.length} upheld, ${rejected.length} refuted by skeptics`)

const rank = { blocker: 0, major: 1, minor: 2 }
upheld.sort((a, b) => rank[a.verifiedSeverity] - rank[b.verifiedSeverity])
const FINDINGS_TEXT = upheld.map(f =>
  `#### [${f.verifiedSeverity.toUpperCase()}] ${f.id}: ${f.title}\nDoc: "${f.docQuote}"\nProblem: ${f.problem}\nFix: ${f.fix}\nSkeptic check: ${f.verifyNote}`).join('\n\n')
const OVERALLS = lensResults.map(r => `- ${r.lens}: ${r.overall}`).join('\n')
const REJECTED_TEXT = rejected.map(f => `- ${f.id} ${f.title}: ${f.verifyNote}`).join('\n')

phase('Revise')
const revised = await agent(`${CONTEXT}
You are the architect who wrote the doc. Four reviewers attacked it and a skeptic checked each blocker/major finding against the code. Their overall verdicts:
${OVERALLS}

Upheld findings (fix every BLOCKER and MAJOR; fix MINOR where cheap):

${FINDINGS_TEXT}

Findings the skeptics refuted (do NOT act on these; listed so you do not re-introduce them):
${REJECTED_TEXT}

Revise the doc IN PLACE at ${DOC}. Rules:
- Keep the user's section structure, order, casual voice and every one of their points from ${ORIGINAL}; you may tighten wording, never drop or reverse a decision of theirs. If a finding asks to change a user decision, keep the user's decision and add the concern to Open questions instead.
- Before changing a fact, re-verify it in the notes or the checkouts; do not invent code the notes or the code do not show. Keep [EXISTS]/[PORT: Nova]/[NEW] tags accurate, naming the system or .dm.
- Keep the gizmo catalogue tables complete (add missing Nova machines the reviewers found, fix wrong numbers).
- Update the Decisions (D1..) and Open questions (Q1..) lists to match the final text, renumbered contiguously; a Decision must be one line.
- No em-dashes or en-dashes anywhere (use commas, colons or "to"); no HTML; no line numbers. Keep LF line endings, no trailing whitespace at line ends. Markdown tables must keep their column count.
- Prefer editing sections over rewriting the whole file, but rewrite a section when it is wrong end to end.
The doc is the ONLY file you may write; no git state changes. Return: (1) a list of the changes you made, grouped by section; (2) the findings you rejected with a one-line reason each; (3) the final line count.`, { label: 'revise', phase: 'Revise', model: MODEL, effort: 'high' })
log('revision written')

phase('Check')
const check = await agent(`${CONTEXT}
The doc at ${DOC} has just been revised. The reviser's report:
${String(revised).slice(0, 6000)}

You are the final critic. Check, reading the whole doc and the whole original: (a) every sentence of the original survives in substance and the section order is the user's; (b) nothing reads as TBA or a stub; (c) every feature bullet has a tag and no tag names a system that the notes (${NOTE_FILES}) or the checkouts contradict: spot-check 10 [EXISTS] tags and 10 [PORT: Nova] tags against the code; (d) the Decisions list is numbered D1.. contiguously, one line each, and does not contradict the text; the Open questions list is Q1.. contiguous and no question is answered elsewhere in the doc; (e) grep the file for U+2014, U+2013, "<" HTML tags, "line " citations, CRLF, trailing whitespace; (f) each Markdown table has a consistent column count; (g) the upheld BLOCKER findings below were actually fixed in the text:
${upheld.filter(f => f.verifiedSeverity === 'blocker').map(f => '- ' + f.id + ': ' + f.title).join('\n')}
Return a numbered list of remaining defects with severity and the exact fix (quote the doc), most serious first, or "CLEAN" plus the three weakest sections you would improve next. No file writes anywhere.`, { label: 'final-check', phase: 'Check', model: MODEL, effort: 'high' })

return {
  counts: { total: all.length, upheld: upheld.length, rejected: rejected.length,
    upheldBySeverity: { blocker: upheld.filter(f => f.verifiedSeverity === 'blocker').length, major: upheld.filter(f => f.verifiedSeverity === 'major').length, minor: upheld.filter(f => f.verifiedSeverity === 'minor').length } },
  overalls: OVERALLS,
  upheld: upheld.map(f => ({ id: f.id, sev: f.verifiedSeverity, title: f.title })),
  rejected: rejected.map(f => ({ id: f.id, title: f.title, note: f.verifyNote })),
  revised,
  check,
}