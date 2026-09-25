export const meta = {
  name: 'outposts-final-fixes',
  description: 'Apply the final-check defects to the Outposts design doc, verify, repeat once if needed',
  phases: [
    { title: 'Fix', detail: 'architect applies the 16 final-check items', model: 'opus' },
    { title: 'Verify', detail: 'checker confirms each item and the structural rules', model: 'opus' },
  ],
}

const MODEL = 'opus'
const REPO = '/home/user/Wolfgate'
const DOC = REPO + '/Docs/_WF/Outposts/OUTPOSTS_DESIGN.md'
const RESEARCH = REPO + '/Docs/_WF/Outposts/research'
const ORIGINAL = RESEARCH + '/HACKMD_ORIGINAL.md'
const SCRATCH = '/tmp/claude-0/-home-user-Wolfgate/881edd38-e84b-595f-b80b-797ce9a1150f/scratchpad'
const CHECK = SCRATCH + '/final_check.md'
const PLANETS = SCRATCH + '/planets'
const NOVA = SCRATCH + '/NovaSector/modular_nova/modules/colony_fabricator'

const CONTEXT = `Context: Wolfgate is a Space Station 14 fork. ${DOC} is the revised "Outposts" design doc (about 1100 lines) for buildable, saveable player outposts on planets. The user's original outline is ${ORIGINAL}; reader notes from the real code are in ${RESEARCH}/notes_*.md. Reference checkouts, READ-ONLY (never edit, build or run git commands that change state there): the main-based repo at ${REPO}, the planet stack branch at ${PLANETS} (module folder still named PlanetCracker), Nova's colony_fabricator at ${NOVA}. The doc is the ONLY file you may write; write nowhere else.`

const RULES = `Doc rules: keep the user's section structure, voice and every point; [EXISTS]/[PORT: Nova]/[NEW] tags accurate; Decisions D1.. and Open questions Q1.. contiguous, one line each; no em-dashes or en-dashes (the file is pure ASCII today, keep it so); no HTML; no line-number citations; LF endings; no trailing whitespace; every Markdown table keeps its column count.`

const VERIFY_SCHEMA = {
  type: 'object',
  properties: {
    clean: { type: 'boolean' },
    remaining: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          item: { type: 'string', description: 'which check item or rule' },
          severity: { type: 'string', enum: ['major', 'minor'] },
          problem: { type: 'string' },
          fix: { type: 'string', description: 'exact text change, quoting the doc' },
        },
        required: ['item', 'severity', 'problem', 'fix'],
      },
    },
    notes: { type: 'string', description: 'what passed, briefly' },
  },
  required: ['clean', 'remaining', 'notes'],
}

let items = `Read the full list of 16 numbered defects at ${CHECK} (written by the previous checker; quoted doc line numbers refer to the doc as it was before your edits, so locate each by its quoted text).`
let lastVerify = null

for (let round = 1; round <= 2; round++) {
  phase('Fix')
  const fixed = await agent(`${CONTEXT}
${RULES}
You are the architect. ${items}
Apply every fix. Where a fix asserts a fact about the code (the chunk pin in BiomeSystem.WFChunkPin.cs, client uses of StringReader/TryLoadGrid, TrySaveShip's MapSavable flip, WFOrbitLayerComponent.Range, ContainerSpawnPointSystem's conditions, the gps beacon file), verify it in the checkouts before writing and use the accurate wording. Where an item asks you to state a number that is currently missing, choose a defensible SS14 value consistent with the doc's neighbours and mark nothing as pending. Where an item asks you to decide or move to an open question, decide unless it is genuinely the user's call. When you add or remove a Decision or Open question, renumber so D and Q stay contiguous. After editing, run a self-check with grep on the doc: no non-ASCII bytes, no trailing whitespace, no CRLF, tables consistent, D/Q contiguous. Return a numbered list matching the defect numbers: what you changed (quote the new text briefly) or why you did not.`, { label: 'fix:round' + round, phase: 'Fix', model: MODEL, effort: 'high' })
  log(`round ${round}: fixes applied`)

  phase('Verify')
  const v = await agent(`${CONTEXT}
${RULES}
You are the checker. The fixer's report for this round:
${String(fixed).slice(0, 8000)}

Verify against the doc: (1) each of the 16 defects listed at ${CHECK} is fixed, or the fixer's reason for not fixing it is sound (check the code yourself where a fact is asserted); (2) the structural rules above hold across the whole file (grep for non-ASCII bytes, trailing whitespace, CRLF, "<" outside code spans, "line [0-9]"; count table columns; check D and Q numbering is contiguous and each is one line); (3) nothing from ${ORIGINAL} was lost or reversed by this round's edits; (4) no new internal contradiction was introduced by the edits (read each edited passage against the Decisions list and the neighbouring sections). Report only real remaining defects with exact fixes; do not add new design opinions.`, { label: 'verify:round' + round, phase: 'Verify', schema: VERIFY_SCHEMA, model: MODEL, effort: 'high' })
  lastVerify = v
  if (!v) { log('verifier failed'); break }
  log(`round ${round}: ${v.clean ? 'clean' : v.remaining.length + ' remaining'}`)
  if (v.clean || v.remaining.length === 0) break
  items = `The checker found these remaining defects after your previous round:\n` + v.remaining.map((r, i) => `${i + 1}. [${r.severity}] ${r.item}: ${r.problem}\n   Fix: ${r.fix}`).join('\n')
}

return { verify: lastVerify }