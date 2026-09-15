# WP13-3 — Protogen exclusion lift (P5-1d)

Implements PLAN5 §4 WP13-3 (U4 = lift, DECISIONS.md "Phase 5 — answers to PLAN5.md §8.4"). No new C#, no
new prototypes, no new subscriptions — exactly as PLAN5 predicted for this package.

## 1. Files created/modified

| File | Status | Change |
|---|---|---|
| `Content.Shared/_WF/Wolfmed/Body/WolfmedWoundHostExclusionSystem.cs` | modified | `ExcludedAncestors = new() { "BaseMobProtogen" }` → `ExcludedAncestors = new()` (EXT 3) |
| `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs` | modified | stale D2 comment reworded, "a Protogen" → "a synthetic species" (EXT 4) |
| `Resources/Prototypes/_Mono/Entities/Mobs/Species/protogen.yml` | modified | `BaseMobProtogen`'s marked comment rewritten to state the exclusion is lifted and record the organ gap (PROTO V) |
| `Docs/Wolfmed/WOLFMED_MANIFEST.md` | modified | new `### WP13-3` section (files table, D2, census, organ-gap note, checkpoint results, "what protogen gains") + the U4 checklist line updated from "not yet landed" to "LANDED in WP13-3" |

No file outside this list was touched. `WolfmedWoundHostExclusionSystem.cs` was **kept, not deleted**,
per PLAN5 §7.1's explicit instruction ("note changes to 'exclusion set is empty as of P5-D9; kept as the
mechanism for any future synthetic species'") — see Deviations.

## 2. Every WOLFGATE edit and reason

- **EXT 3** (`WolfmedWoundHostExclusionSystem.cs`): emptied the `HashSet` and replaced the field's doc
  comment with `// WOLFGATE (P5-D9): protogen is biologically organic (Biological container, OrganicPart
  limbs, Blood bloodstream, Hunger/Thirst, Respirator, FoodMeatHuman) - exclusion lifted. Add new synthetic
  species here.` The `<WoundHostComponent, ComponentInit>` subscription and the WP9 `RemComp` (not
  `RemCompDeferred`) fix are untouched — they simply match nothing now.
- **EXT 4** (`HealthAnalyzerSystem.Wolfmed.cs`): comment-only. The D2 guard's example listed "a borg, a
  Protogen" as an entity with `SurgeryTargetComponent` but no `WoundHost`; after the lift a protogen has
  `WoundHost`, so the example is stale. Reworded to "a borg, a synthetic species". `HasComp<WoundHostComponent>`
  gate itself unchanged.
- **PROTO V** (`protogen.yml`): comment-only, exact wording from PLAN5 §3.2 — explains the exclusion was
  lifted, protogen's organic components, and points at P5-D19 for the organ gap. No `components:` change.

## 3. Deviations from PLAN5

**One, and it is PLAN5 overriding the generic task instruction, not an author decision.** The task brief's
generic rule says "if the exclusion system becomes empty, delete it and its manifest row noting why."
PLAN5 §7.1 is more specific and binding for this exact case: it says the manifest row for
`WolfmedWoundHostExclusionSystem.cs` should read "exclusion set is empty as of P5-D9; kept as the mechanism
for any future synthetic species" — i.e. explicitly keep the file rather than delete it, so a future
synthetic species (skeleton, a hypothetical new borg-adjacent species, etc.) has a ready opt-out mechanism
instead of needing the system reinvented. Followed PLAN5; the file and its subscription are kept, and the
manifest documents why in the WP13-3 table.

No other deviation. Census, files-touched count (3), and checkpoint list all match PLAN5's WP13-3 table
and P5-D9/E7 exactly.

## 4. Build / server / test output tails

**Content.Server build:**
```
Build succeeded.
    0 Error(s)
```

**Content.Client build:**
```
Build succeeded.
    0 Error(s)
```

**Headless server** (120 s, port 1299, `C:/tmp/wolfmed-plan/p5/wp/WP13-3-report-server.log`): reached
`Server Version 277.0.0.0 -> Ready`, bound to port 1299, zero `[ERRO]`/`[FATL]`/`Exception` lines anywhere
in the log (grep returned nothing).

**DockTest** (`C:/tmp/wolfmed-plan/p5/wp/WP13-3-docktest.log`):
```
Passed TestDockingConfig(<0.5, 1.5>,<0.5, 1.5>,0 rad,0 rad,False) [20 ms]
Passed TestPlanetDock [708 ms]
Test Run Successful.
Total tests: 3
     Passed: 3
```

**`EntityTest.SpawnAndDeleteAllEntitiesOnDifferentMaps`** (`C:/tmp/wolfmed-plan/p5/wp/WP13-3-tests.log`):
```
Passed SpawnAndDeleteAllEntitiesOnDifferentMaps [1 m 46 s]
Test Run Successful.
Total tests: 1
     Passed: 1
```
This test spawns and ticks and deletes **every** non-abstract entity prototype in the game, `MobProtogen`
and `MobProtogenRandom` included — exactly the regression guard the exclusion system's own WP9 comment
warns about (`RemCompDeferred` previously left `WoundHostComponent` in `EntityManager`'s `_deleteSet` at
life stage `Initialized`, tripping `StartComponents`'s assert on every `MobProtogen` spawn). Zero
`[ERRO]`/`[FATL]`/`Exception`/`Assert` lines in the log; no environmental `db.ef` failure, no re-run needed.

## 5. What later packages must know

- **U4 is COMPLETE.** `ExcludedAncestors` is empty; protogen (`MobProtogen`, `MobProtogenRandom`) is now
  a plain organic wound host — inherited `PartProtogen`-based parts already carry `OrganicBodyPartProfile`
  from phase-1 wiring, so nothing else needed touching for the profile itself.
- **Organ gap is real and permanent for phase 5** (P5-D19/U12′): protogen's organs
  (`OrganProtogenBrain/Eyes/Lungs/Heart/Stomach/Liver/Kidneys`, all `BaseProtogenOrgan`/
  `BaseProtogenOrganUnGibbable`-derived) carry no `OrganDamageComponent`/`WolfmedOrgan*`. No organ damage,
  destruction, internal bleeding or organ surgery on protogen. `_Mono/Body/Organs/protogen.yml` was
  deliberately not touched, per PLAN5 §3.4. WP13-6's tests must not write a positive organ-damage
  assertion for protogen (test-plan trap 9 / T-P5-9).
- **R13 honoured:** WP13-5 (mechanical analyzer/examine wording) also edits
  `HealthAnalyzerSystem.Wolfmed.cs` — it lands after this package and must re-read the file before adding
  the `Mechanical` flag producer, since EXT 4's comment reword changed line numbers slightly (block now at
  `:32-34`, one line later than PLAN5's citation of `:31-34`).
- **`WolfmedWoundHostExclusionSystem` stays in the tree** as the mechanism for any future synthetic
  species; its `ExcludedAncestors` set is simply empty right now.
- No new prototype ids, no new components, no new subscriptions were added — WP13-3 changes zero D2
  exposure beyond the intended one (protogen itself becoming a host, per PLAN5 §5.4's "Intentional, U4"
  verdict).

Files touched (absolute paths):
- `C:/Users/jzo12/Documents/GitHub/Wolfgate/.claude/worktrees/rules-motd-updates-11c89c/Content.Shared/_WF/Wolfmed/Body/WolfmedWoundHostExclusionSystem.cs`
- `C:/Users/jzo12/Documents/GitHub/Wolfgate/.claude/worktrees/rules-motd-updates-11c89c/Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs`
- `C:/Users/jzo12/Documents/GitHub/Wolfgate/.claude/worktrees/rules-motd-updates-11c89c/Resources/Prototypes/_Mono/Entities/Mobs/Species/protogen.yml`
- `C:/Users/jzo12/Documents/GitHub/Wolfgate/.claude/worktrees/rules-motd-updates-11c89c/Docs/Wolfmed/WOLFMED_MANIFEST.md`

Logs:
- `C:/tmp/wolfmed-plan/p5/wp/WP13-3-report-server.log`
- `C:/tmp/wolfmed-plan/p5/wp/WP13-3-docktest.log`
- `C:/tmp/wolfmed-plan/p5/wp/WP13-3-tests.log`
