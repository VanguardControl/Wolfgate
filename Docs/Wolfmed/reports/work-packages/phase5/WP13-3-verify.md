# WP13-3 verification — protogen exclusion lift (P5-1d)

Verifier pass against `C:/tmp/wolfmed-plan/DECISIONS.md` (Phase 5 sections incl. §8.4 answers),
`C:/tmp/wolfmed-plan/p5/PLAN5.md` (§WP13-3, §1, §3, §5, §8, Revision notes), and
`C:/tmp/wolfmed-plan/p5/wp/WP13-3-report.md`. WG worktree = `rules-motd-updates-11c89c`.

## 1. Build / server

- `dotnet build Content.Server/Content.Server.csproj -c DebugOpt -v q -nologo`: **Build succeeded. 0 Error(s).**
- `dotnet build Content.Client/Content.Client.csproj -c DebugOpt -v q -nologo`: **Build succeeded. 0 Error(s).**
- Headless server, port 1299, 120 s (`C:/tmp/wolfmed-plan/p5/wp/WP13-3-verify-server.log`, 109 lines):
  reached `Server Version 277.0.0.0 -> Ready`, bound `[::]:1299` and `0.0.0.0:1299`. `grep -nE
  "\[ERRO\]|\[FATL\]|Exception"` → **0 hits**. `grep -niE "wound|protogen|species|prototype"` → only the
  unrelated guidebook-protodata info line; no wound/body/species/prototype warnings or errors.

## 2. Upstream discipline

`git diff HEAD --stat -- Content.Shared Content.Server Content.Client Resources
Content.IntegrationTests` at this point in the (uncommitted, sequential) worktree shows the accumulated
diff of WP13-0 through WP13-3 (12 files: this is expected — packages run sequentially in one worktree with
no commits, per PLAN5 §4). Files under `_Onyx`/`_WF` are exempt from the marker requirement (vendored /
glue); every other file was diffed individually:

| File | WP | Marked? | Matches PLAN5 §3.2 entry |
|---|---|---|---|
| `Resources/Prototypes/Body/Parts/diona.yml` | WP13-1 | yes, inline `# WOLFGATE (P5-1)` | PROTO N |
| `Resources/Prototypes/Body/Parts/slime.yml` | WP13-1 | yes, inline `# WOLFGATE (P5-1)` | PROTO M |
| `Resources/Prototypes/Entities/Objects/Tools/welders.yml` | WP13-2 | yes, block `# WOLFGATE (P5-D5, PROTO S)` | PROTO S |
| `Resources/Prototypes/_Mono/Entities/Objects/Tools/nanite_applicator.yml` | WP13-2 | yes, block `# WOLFGATE (P5-D5, PROTO T)` | PROTO T |
| `Resources/Prototypes/_EinsteinEngines/Body/Parts/ipc.yml` | WP13-1 | yes, 3 marked edits | PROTO O(a)/(b)/(c) |
| `Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/ipc.yml` | WP13-2 | yes, every block marked | PROTO Q |
| `Resources/Prototypes/_Shitmed/Body/Parts/cybernetic.yml` | WP13-1 | yes, 3 marked edits | PROTO P(a)/(b)/(c) |

**WP13-3's own three files** (the ones this package actually authored):

| File | Diff | Marked? | Matches |
|---|---|---|---|
| `Content.Shared/_WF/Wolfmed/Body/WolfmedWoundHostExclusionSystem.cs` | `ExcludedAncestors = new() { "BaseMobProtogen" }` → `ExcludedAncestors = new()`, doc comment replaced with `// WOLFGATE (P5-D9): …` | yes | EXT 3 verbatim (§3.1) |
| `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs` | "a borg, a Protogen" → "a borg, a synthetic species" inside the existing `// WOLFGATE (D2)` comment; `HasComp<WoundHostComponent>` gate untouched | yes (comment-only, inherits the existing D2 marker) | EXT 4 (§3.1) |
| `Resources/Prototypes/_Mono/Entities/Mobs/Species/protogen.yml` | `BaseMobProtogen`'s marked comment block rewritten; confirmed via `git diff` that only comment lines (`-`/`+` all start with `#`) changed, no `components:`/`id:` line touched | yes, already a tracked upstream file since WP7 | PROTO V verbatim (§3.2) |

No unmarked line, no file outside the WP13-3 report's list of 3 was touched by this package. `Docs/Wolfmed/DECISIONS.md`'s diff in the snapshot (adding "Phase 5 — scope and constraints" / "§8.4 answers") and the earlier `### WP13-0/1/2` manifest sections predate WP13-3 (confirmed: the manifest diff contains `### WP13-0`, `### WP13-1`, `### WP13-2` sections in addition to WP13-3's own, i.e. cumulative from earlier packages, not authored by this WP). D2 holds: PLAN5 §5.4's row for "EXT 3 — protogen" reads "Makes `MobProtogen` + `MobProtogenRandom` wound hosts. Nothing else in the set. **Intentional**, U4" — exactly the one, expected, documented D2 exposure point for this WP.

## 3. Vendoring fidelity

**N/A for this WP's own diff.** WP13-3 touches zero lines of vendored Onyx prototype content — its only
prototype edit (`protogen.yml`) is a Wolfgate-authored comment on an already-upstream file, not a
vendored Onyx block, and it makes zero change to `wounds.yml`. Confirmed: `git diff HEAD --stat --
Resources/Prototypes/_Onyx/Wounds/wounds.yml` shows the same `663 insertions(+), 1 deletion(-)` as before
this package ran — no additional change from WP13-3. Full vendoring-fidelity diffing of `wounds.yml`
against `git -C C:/tmp/onyx show HEAD:Resources/Prototypes/_Onyx/Wounds/wounds.yml` was already completed
and passed in `WP13-0-verify.md` §3 ("Every differing line is WOLFGATE-marked; no silent drift").

## 4. Collisions / new C# / inheritance reasoning

- New prototype ids added by WP13-3: **zero** (report and manifest both state this; confirmed by diff —
  `protogen.yml`'s change is comment-only).
- New `.cs` files: **zero**. `git diff HEAD --stat -- '*.cs'` shows only 3 modified existing files
  (`CirculatoryStreamSystem.cs` from WP13-2, and WP13-3's own `HealthAnalyzerSystem.Wolfmed.cs` +
  `WolfmedWoundHostExclusionSystem.cs`); `git ls-files --others --exclude-standard -- '*.cs'` returns
  nothing.
- **Inheritance / Destructible reasoning:** WP13-3 makes no change to any `Destructible` block or
  `damageContainer` assignment. Its effect is indirect — via the exclusion `HashSet` going empty,
  `BaseMobProtogen`'s descendants stop having `WoundHostComponent` removed at `ComponentInit`. Traced the
  full parent chain:
  - `BaseMobProtogen: parent: BaseMobSpeciesOrganic` (`protogen.yml:2`) — inherits `WoundHost`,
    `PainShockTarget`, `EmoteOnDamage` from the organic species base, same as every other organic species
    (§3.4: "no change" to `Entities/Mobs/Species/base.yml`).
  - `MobProtogen: parent: BaseMobProtogen` (`_Mono/Entities/Mobs/Player/protogen.yml:4-5`).
  - `MobProtogenRandom: parent: MobProtogen` (`_Goobstation/Entities/Mobs/Player/humanoid.yml:226-227`).
  - `MobProtogenDummy: parent: BaseSpeciesDummy` (`_Mono/.../protogen.yml:89`) — confirmed by direct read,
    **not** a `BaseMobProtogen` descendant, unaffected, matching the report's claim and PLAN5 E7.
  - Protogen organs: `grep -n "OrganDamage\|WolfmedOrgan" _Mono/Body/Organs/protogen.yml` → **0 hits**.
    `OrganProtogenBrain/Eyes/Tongue/Appendix/Lungs/Heart/Stomach/Liver/Kidneys` all parent
    `BaseProtogenOrgan` (root `BaseProtogenOrganUnGibbable`, parent `BaseItem`) except `OrganProtogenEars`
    (`BaseHumanOrgan` — not one of the seven instrumented organs). Confirms P5-D19/the report's organ-gap
    claim: no `OrganDamageComponent`/`WolfmedOrgan*` reaches any protogen organ, so `organDamage.chances`
    on `OrganicBodyPartProfile` rolls inertly, exactly as documented.
  - `WolfmedWoundHostExclusionSystem`'s own subscription (`<WoundHostComponent, ComponentInit>`) and the
    WP9 `RemComp` (not `RemCompDeferred`) fix are both structurally unchanged — read the full file,
    confirmed only the `HashSet` initializer and its doc comment differ.

## 5. Manifest

`Docs/Wolfmed/WOLFMED_MANIFEST.md` carries a `### WP13-3` section (lines ~2375-2426) with one row per
touched file (`WolfmedWoundHostExclusionSystem.cs`, `HealthAnalyzerSystem.Wolfmed.cs`, `protogen.yml`),
plus a census note, an organ-gap note, a D2 note, a checkpoint block citing all three logs, a "what
protogen gains" summary, and the U4 checklist line flipped from "not yet landed" to "**LANDED in WP13-3**".
All three files this WP touched have a manifest row; no file is missing one.

## 6. Plan conformance

PLAN5 §4 WP13-3 table lists exactly 3 files — all three exist, all three match the table's stated action
(EXT 3, EXT 4, PROTO V) verbatim. PLAN5 §9's work-package-table row for WP13-3 ("3 files, 0 new ids,
PROTO V (comment) + EXT 3/4 (`_WF`), 0 subs") matches. DECISIONS.md §8.4 answer relevant to this WP —
**"U4 Protogen: LIFT the exclusion — organic-profile wound host; organ gap recorded"** — is honoured:
`ExcludedAncestors` is empty (lift confirmed), and the organ gap is recorded in both the report and the
manifest (P5-D19/U12′). No other §8.4 answer names WP13-3 as its "Needed by" column in PLAN5 §8.4's table.

Independent re-verification (not just trusting the report's own logs):
- `dotnet test … --filter "FullyQualifiedName~DockTest"`: **Passed – 3/3** (25 s), matching the report.
- `dotnet test … --filter "FullyQualifiedName~EntityTest"`: **Passed – 4 passed, 2 skipped, 0 failed** (2 m),
  no crash — confirms `MobProtogen`/`MobProtogenRandom` (spawned as part of the full entity sweep, per
  PLAN5's WP13-3 checkpoint: "spawn `MobProtogen` and `MobProtogenRandom` in a headless run and confirm no
  `RemCompDeferred`/`_deleteSet` assert") spawn and delete cleanly as wound hosts.

## 7. Snapshot

Written:
- `C:/tmp/wolfmed-plan/p5/snapshots/WP13-3.patch` (1378 lines) — `git diff HEAD -- Content.Shared
  Content.Server Content.Client Resources Docs Content.IntegrationTests`. Contains the cumulative WP13-0
  through WP13-3 diff (12 files under the audited paths + `Docs/Wolfmed/DECISIONS.md` +
  `Docs/Wolfmed/WOLFMED_MANIFEST.md`), consistent with sequential no-commit execution.
- `C:/tmp/wolfmed-plan/p5/snapshots/WP13-3.untracked.txt` (2 lines) — `Resources/Prototypes/_WF/Wolfmed/Body/
  species_parts.yml` and `Resources/Prototypes/_WF/Wolfmed/Damage/containers.yml`, both new files from
  earlier packages (WP13-0/WP13-1), not from WP13-3.

## Verdict

**PASS.** Both builds clean (0 errors), headless server clean for 120 s (no ERRO/FATL/Exception, no
wound/body/species/prototype warnings), DockTest and EntityTest independently re-run and green, all three
WP13-3 edits are WOLFGATE-marked and match PLAN5 §3.1/§3.2 verbatim, zero new C#, zero new prototype ids,
zero new subscriptions, the protogen census and organ-gap claims verified directly against the tree (not
just the report's prose), manifest has a row for every touched file, and the plan table and DECISIONS.md
§8.4 U4 answer are both honoured. No blocker or major findings.
