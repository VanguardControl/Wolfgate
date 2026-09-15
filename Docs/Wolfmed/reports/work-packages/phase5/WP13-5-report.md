# WP13-5 — Mechanical analyzer and examine wording (P5-5, P5-D17, U9(a))

Package scope as assigned to this session: locale keys only (the 4 new LocIds PLAN5 §2.5 lists) plus
verification of the data-only "wound prototype display names" wiring. **No new C#** — a deliberate narrowing
of PLAN5's own WP13-5 file list, per this package's own task brief. See §3 for the exact gap this leaves.

## 1. Files created/modified

| File | Status | Change |
|---|---|---|
| `Resources/Locale/en-US/_Onyx/medical/health-analyzer-component.ftl` | modified | appended 3 keys + a `# WOLFGATE (P5-5)` comment block |
| `Resources/Locale/en-US/_Onyx/medical/health-examinable.ftl` | modified | appended 1 key + a `# WOLFGATE (P5-5)` comment line |
| `Docs/Wolfmed/WOLFMED_MANIFEST.md` | modified | appended `### WP13-5` section before "Phase 5 — user decisions" |

No `.cs` file touched. No new prototype ids. No new component/system/subscription of any kind.

## 2. Every WOLFGATE edit and reason

**`health-analyzer-component.ftl`** (appended after the existing fracture-treatment keys):
```
# WOLFGATE (P5-5): mechanical-species variants of the three generic short labels above. Onyx has no
# equivalent (its IPCs read as "bruises"/"fracture") - these are a Wolfgate improvement, unconsumed
# until a later package adds the HealthAnalyzerWoundDiagnostic.Mechanical flag and branches on it
# (PLAN5 P5-D17/§2.6; deferred out of this package per its "No new C#" scope).
health-analyzer-wound-bleeding-short-mechanical = fluid leak
health-analyzer-wound-fracture-short-frame = frame damage: { $grade }
health-analyzer-wound-fracture-treated-short-frame = frame damage: { $grade } ({ $treatment })
```
Text is PLAN5 §2.5 verbatim. Reason: PLAN5 P5-D17/U9(a) — the medic-facing analyzer strings for
mechanical/frame damage, deferring the seven flesh-flavoured examine adjectives.

**`health-examinable.ftl`** (appended next to `health-examinable-part-bleeding`):
```
# WOLFGATE (P5-5): mechanical-species variant of the label above; unconsumed until a later package
# branches on it (PLAN5 P5-D17/§2.6 - deferred out of this package per its "No new C#" scope).
health-examinable-part-bleeding-mechanical = leaking fluid
```
Text is PLAN5 §2.5 verbatim.

Both comments state plainly, at the point of addition, that the keys have no consumer yet — see §3.

**Onyx-key copying:** checked `C:/tmp/onyx` `git show HEAD:Resources/Locale/en-US/_Onyx/medical/{health-
analyzer-component,health-examinable}.ftl` directly. Onyx's own files carry **no** mechanical/frame-damage
short-label variants at all (confirmed by reading both files in full) — Onyx's IPCs read as plain "bruises"/
"fracture" today, exactly as PLAN5 P5-D17 says. So there was nothing to copy for these 4 keys; PLAN5's own
text (a Wolfgate improvement) is what was ported. The wound-specific keys that *do* mirror Onyx 1:1
(`wound-name-ipc-mechanical-damage`, `wound-name-cybernetic-frame-fracture`, `wound-stage-frame-*`,
`wound-stage-mechanical-*`, `wound-examine-frame-*`, all four `wound-name-slime-*`/`wound-name-plant-*`) were
already ported verbatim in WP13-0/WP13-1 and are confirmed still present and correctly referenced — no
changes needed to them this package (§4 below).

## 3. Deviations from PLAN5, justified

**PLAN5 §4 WP13-5 lists six files; this package touches two.** The four skipped are the C# half of P5-5/
§2.6: `Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs` (append `bool Mechanical` to the
payload record), `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs` (set the flag in
`BuildWoundDiagnostics`), `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml.cs` (branch the
three analyzer LocIds on it), `Content.Shared/_Onyx/HealthExaminable/HealthExaminableSystem.PartStatus.cs`
(branch the examine LocId). This session's own task brief scoped WP13-5 down to "locale keys ... and any
data-only wiring ... No new C#", which is narrower than PLAN5's file table. I followed the brief rather than
PLAN5's fuller list; this is recorded as a deviation, not silently absorbed.

**Consequence, verified by grep:** no existing `.cs` file references any of the 4 new keys. `Loc.GetString`
call sites for the labels these substitute for are unchanged:
`WolfmedDiagnosticPanel.xaml.cs:164-165,172` still hard-codes `health-analyzer-wound-fracture-short`,
`-fracture-treated-short` and `-bleeding-short`; `HealthExaminableSystem.PartStatus.cs:105` still hard-codes
`health-examinable-part-bleeding`. So today, and until a follow-up package lands the flag + branches, an IPC
or cybernetic part's analyzer summary still reads "external bleeding" / "fracture: Simple" and examine still
reads "active bleeding" — the new keys are correct, collision-free, and inert. This is exactly the gap
flagged in the manifest's "later packages must know" note.

**Everything else matches PLAN5 exactly:** key names, key text, comment style, file choice, append-only
placement, and the "no upstream file touched" constraint (§3.1 of PLAN5: WP13-5 touches zero upstream files;
confirmed — only two `_Onyx` locale files, both already Wolfmed-owned).

## 4. Build/server/test output tails

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
(Both expected 0 errors since no `.cs` file was touched; run sequentially, never concurrently.)

**Headless server, 120 s, port 1299** (`C:/tmp/wolfmed-plan/p5/wp/WP13-5-report-server.log`, 109 lines):
```
[INFO] root: Server Version 277.0.0.0 -> Ready
[INFO] net: "::": "Socket bound to [::]:1299: True"
[INFO] net: "::": "Network thread started"
[INFO] net: "0.0.0.0": "Socket bound to 0.0.0.0:1299: True"
[INFO] net: "0.0.0.0": "Network thread started"
```
Zero `[ERRO]`/`[FATL]`/`Exception` lines (grep confirmed empty).

**YAML linter (Release):**
```
No errors found in 72762 ms.
```
No duplicate `.ftl` key — confirms the 4 new keys are genuinely new (matches PLAN5 §2.5's claim).

**`Content.IntegrationTests` build:**
```
Build succeeded.
    0 Error(s)
```

**`DockTest` (`C:/tmp/wolfmed-plan/p5/wp/WP13-5-docktest.log`):**
```
Passed TestDockingConfig(<0.5, 1.5>,<0.5, 1.5>,0 rad,0 rad,False) [17 ms]
Passed TestPlanetDock [682 ms]

Test Run Successful.
Total tests: 3
     Passed: 3
```
No environmental `db.ef` sqlite warning failure; no re-run needed.

## 5. What later packages must know

1. **The locale text for P5-5 is done and plan-exact.** A follow-up package only needs the C# wiring: append
   `bool Mechanical` to `HealthAnalyzerWoundDiagnostic` (one new positional member, one construction site to
   update at `HealthAnalyzerSystem.Wolfmed.cs:118`, computed as
   `!profile.TreatmentCapabilities.Contains(TreatmentCapability.Biological)`), then branch on it in
   `WolfmedDiagnosticPanel.xaml.cs` (three LocIds) and `HealthExaminableSystem.PartStatus.cs` (one LocId).
   PLAN5 §2.6 has the exact code shape. No further locale work is needed for P5-5 — do not add new keys.
2. **`HealthAnalyzerSystem.Wolfmed.cs` was not touched this package** (unlike PLAN5's file table implied) —
   only the two `.ftl` files changed. A follow-up package can edit it directly without needing to "re-read
   after WP13-5" in the sense PLAN5's R13 warned about; WP13-3's comment-only edit there is still the last
   change to that file.
3. **Wound-specific display names (the data-only half of P5-5) needed zero work this package** — verified
   complete from WP13-0/WP13-1: all 30 wound prototypes in `wounds.yml` have correct `name:`/
   `examineDescription:` fields and every LocId they reference already resolves in
   `Resources/Locale/en-US/_Onyx/prototypes/wounds/wounds.ftl` and `health-examinable.ftl`. This includes the
   six species/mechanical wounds (`IpcMechanicalDamageWound`, `CyberneticMechanicalDamageWound`,
   `CyberneticFrameFractureWound`, four `Slime*`/`Plant*` sets). Nothing to revisit here.
4. **PLAN5's own WP13-6 (tests) includes T-P5-18** (`MechanicalWoundDiagnosticTextResolvesTest`), which
   asserts a `HealthAnalyzerVisibleWound`'s `Name`/`StageName` LocIds resolve to `"chassis damage"`/
   `"moderate"` — that test only exercises the per-wound name path (already complete, §4 above) and does
   **not** touch `Mechanical`/the four new short-label keys, so it is unaffected by this deviation and can
   run as originally scoped in WP13-6 without modification.
5. **Manifest:** the `### WP13-5` section is appended in `Docs/Wolfmed/WOLFMED_MANIFEST.md` immediately
   before "## Phase 5 — user decisions", following the same sequential-append convention as WP13-0..4. The
   "Phase 5 — user decisions" block itself was left unmodified — U9 (analyzer wording) is not one of the
   DECISIONS.md-binding items (U5/U4/U1/U3′/U15/U2/U13′/group B); PLAN5 already recommends default (a) for
   it and this package implements exactly that default, so no new user-decision line was needed there.
