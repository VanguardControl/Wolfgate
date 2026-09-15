# WP13-4 verification — cable coil `treatmentCapabilities` (P5-3, U5)

Verifier pass against `C:/tmp/wolfmed-plan/DECISIONS.md` (Phase 5 sections incl. §8.4 answers),
`C:/tmp/wolfmed-plan/p5/PLAN5.md` (§WP13-4, §1, §3, §5, §8, Revision notes), and
`C:/tmp/wolfmed-plan/p5/wp/WP13-4-report.md`. WG worktree = `rules-motd-updates-11c89c`.

## 1. Build / server

- `dotnet build Content.Server/Content.Server.csproj -c DebugOpt -v q -nologo`: **Build succeeded. 0 Error(s).**
- `dotnet build Content.Client/Content.Client.csproj -c DebugOpt -v q -nologo`: **Build succeeded. 0 Error(s).**
- Headless server, port 1299, 120 s (`C:/tmp/wolfmed-plan/p5/wp/WP13-4-verify-server.log`, 110 lines):
  reached `Server Version 277.0.0.0 -> Ready`, bound `[::]:1299` and `0.0.0.0:1299`.
  `grep -nE "\[ERRO\]|\[FATL\]|Exception"` → **0 hits**. `grep -inE "wound|bodypart|species|prototype" |
  grep -iE "error|warn|fail|invalid|unknown"` → **0 hits**. Only routine `[WARN] eng: MainLoop: Cannot keep
  up!` (a startup-load warning present in every prior package's log too, unrelated to this change).

## 2. Upstream discipline

`git diff HEAD --stat -- Content.Shared Content.Server Content.Client Resources
Content.IntegrationTests` at this point (uncommitted, sequential worktree, no commits between packages)
shows the accumulated diff of WP13-0 through WP13-4 (13 files — expected per PLAN5 §4's execution model).
Diffed `C:/tmp/wolfmed-plan/p5/snapshots/WP13-3.patch` against a fresh
`git diff HEAD -- Content.Shared Content.Server Content.Client Resources Docs Content.IntegrationTests`
to isolate exactly what WP13-4 itself changed on top of the already-verified WP13-0–WP13-3 baseline:

**Delta since WP13-3's snapshot (WP13-4's own authored change):**

| File | Diff | Marked? | Matches PLAN5 §3.2 |
|---|---|---|---|
| `Resources/Prototypes/Entities/Objects/Tools/cable_coils.yml` | one line added: `treatmentCapabilities: [Electrical] # WOLFGATE (P5-3): …` under the existing `damageContainers: [Silicon]` | yes, full-line trailing comment | **PROTO U**, verbatim — compared byte-for-byte against PLAN5 §3.2's quoted PROTO U text, identical |
| `Docs/Wolfmed/WOLFMED_MANIFEST.md` | `### WP13-4` section appended + U5 bullet flipped from "not yet landed" to "**LANDED in WP13-4**" | n/a (docs) | matches report §5 |

No other file changed. Confirmed by diffing the full `git diff` against `WP13-3.patch` and the full
`git ls-files --others --exclude-standard` against `WP13-3.untracked.txt`: the untracked list is byte-identical
(no new files), and the only two hunks not already present in WP13-3's snapshot are the two rows above.

**Carryover files from earlier WPs** (already independently verified in `WP13-0-verify.md` through
`WP13-3-verify.md`, all PASS) are present in the diff as expected and were spot-re-read here for sanity:
`Resources/Prototypes/Body/Parts/{diona,slime}.yml` (WP13-1, PROTO M/N), `.../Entities/Objects/Tools/welders.yml`
+ `_Mono/.../nanite_applicator.yml` (WP13-2, PROTO S/T), `_EinsteinEngines/Body/Parts/ipc.yml` +
`_EinsteinEngines/Entities/Mobs/Player/ipc.yml` (WP13-1/2, PROTO O/Q), `_Shitmed/Body/Parts/cybernetic.yml`
(WP13-1, PROTO P), `_Mono/Entities/Mobs/Species/protogen.yml` (WP13-3, PROTO V) — every changed line in every
one of these carries a `// WOLFGATE`/`# WOLFGATE` marker; none was touched again by WP13-4. D2 holds: WP13-4
adds no new D2 pressure point beyond the one PLAN5 §1 already names for it ("(e) the cable-coil
`treatmentCapabilities` annotation... structurally D2-safe (§5.4)") — verified directly in §4 below.

## 3. Vendoring fidelity

Diffed the coil's `treatmentCapabilities` value against Onyx at the pinned commit:

```
git -C C:/tmp/onyx show HEAD:Resources/Prototypes/Entities/Objects/Tools/cable_coils.yml
```

Confirmed `C:/tmp/onyx` is checked out at `2f5bab9946539cbe083010c9ae6fbc59b47ae377` — matches
DECISIONS.md's pinned Onyx commit exactly. Onyx's `CableStack`'s `- type: Healing` block (inside a
`##Corvax-species-start/end` fence) reads:

```
- type: Healing
  treatmentCapabilities: [Electrical] # <Onyx-WoundTreatment>
  healWounds: true # <Onyx-WoundTreatment>
  damageContainers:
    - SiliconIpc
  damage: {Blunt: -3, Slash: -3, Piercing: -3, Heat: -3, Shock: -5}
  delay: 3.5
  ...
```

Wolfgate's added value (`[Electrical]`) is copied verbatim from Onyx's `treatmentCapabilities` line —
the single differing element (comment text: Onyx's `# <Onyx-WoundTreatment>` vs Wolfgate's
`# WOLFGATE (P5-3): …`) carries the WOLFGATE marker. `healWounds: true` is correctly **not** added —
`HealingComponent.HealWounds` already defaults `true` (confirmed in §4 below), so an explicit line would
be redundant, not a fidelity gap. Onyx's `delay: 3.5`, its five-type damage set, and its
`damageContainers: [SiliconIpc]` are correctly **not** adopted, exactly as PROTO U and the report specify
("Only this one line"). No other prototype block in this WP is copied from Onyx (this WP touches no other
file), so no other vendoring comparison applies.

## 4. Collisions / new C# / inheritance reasoning

- New prototype ids added by WP13-4: **zero** (report and manifest both state this; confirmed — the diff
  is a single scalar-field addition inside an existing `id: CableStack` block, no new `id:` line anywhere).
- New `.cs` files: **zero**. `git diff HEAD --diff-filter=A --name-only -- Content.Shared Content.Server
  Content.Client Content.IntegrationTests | grep '\.cs$'` → no output. `git ls-files --others
  --exclude-standard` (untracked) contains only the two pre-existing new `_WF` files from WP13-0/WP13-1
  (`species_parts.yml`, `containers.yml`), neither of which is `.cs`.
- **`Destructible`/damage-container reasoning:** WP13-4 makes **zero** change to any `Destructible` block
  or `damageContainer` assignment — it adds one `HealingComponent.TreatmentCapabilities` line to an item
  prototype, not a body-part or mob prototype. N/A for this WP's own diff; the `Destructible`/
  `damageContainer` changes present in the accumulated tree (PROTO O(b)/(c), P(b)/(c)) are WP13-1's and were
  already independently re-derived and passed in `WP13-1-verify.md` §4.
- **Mechanism trace (independent re-derivation, not just trusting the report's citations):** read the
  actual C# to confirm every line-number claim in PROTO U's comment:
  - `Content.Server/Medical/Components/HealingComponent.cs:67` — `public bool HealWounds = true;` (report
    cites `:66-67`, matches).
  - `Content.Server/Medical/Components/HealingComponent.cs:73` — `public HashSet<TreatmentCapability>
    TreatmentCapabilities = [TreatmentCapability.Biological];` (report cites `:72-73`, matches — default is
    confirmed `[Biological]`).
  - `Content.Server/Medical/HealingSystem.cs:204-207` — `if (!woundHost && // WOLFGATE: HOOK 8 …
    !component.DamageContainers.Contains(...))` (report cites `:201-210`, matches — the `DamageContainers`
    gate is skipped whenever `HasComp<WoundHostComponent>(target)` is true).
  - `Content.Server/_Onyx/Wounds/WoundHealingSystem.cs:154-166`, `IsCompatiblePart` — takes a
    `damageContainers` parameter but the method body never references it; the only gate is
    `profile.TreatmentCapabilities.Overlaps(treatmentCapabilities)` against the resolved part's
    `WoundableComponent.Profile`. Confirmed by reading the full method body.
  - Traced the call path end-to-end: `HealingSystem.TryHeal` → (wound-host branch) →
    `Content.Server/_WF/Wolfmed/Medical/HealingSystem.Wolfmed.cs:IsWoundDamaged` → raises
    `ResolveHealingPartEvent` carrying `healing.TreatmentCapabilities` → (elsewhere in the wound routing
    chain) → `WoundHealingSystem.IsCompatiblePart`'s `profile.TreatmentCapabilities.Overlaps(...)` check.
    The non-wound-host branch of `TryHeal` (line 220-225, `HasDamage`/`IsPartDamaged`/blood-restore) never
    calls `IsWoundDamaged` or touches `TreatmentCapabilities` at all — confirming the report's and PLAN5
    §5's D2-safety claim ("Non-hosts see no change").
  - Read `Resources/Prototypes/_Onyx/Wounds/wounds.yml` directly: `OrganicBodyPartProfile.treatmentCapabilities:
    [Biological]` (no longer overlapping `[Electrical]` — organics lose the heal, as intended);
    `IpcBodyPartProfile.treatmentCapabilities: [Mechanical, Electrical]` and
    `CyberneticBodyPartProfile.treatmentCapabilities: [Mechanical, Electrical]` (both still overlap
    `[Electrical]` — IPC/cybernetic parts keep the heal, as intended, and as PLAN5 §4's sequencing
    rationale predicted).
  - Every citation in PROTO U's comment and the report is accurate to within a line or two and functionally
    correct; the mechanism produces exactly the behaviour claimed.

## 5. Manifest

`Docs/Wolfmed/WOLFMED_MANIFEST.md` carries a `### WP13-4` section with one row (`cable_coils.yml`, matching
the report's single-file change), a "bug this closes" note, a sequencing note, a D2 note, a line-endings
note, and a checkpoint block citing the build/server logs. The U5 checklist bullet under "Phase 5 — user
decisions" is flipped from "not yet landed" to "**LANDED in WP13-4**" with a changelog-worthy callout. Every
file WP13-4 touched (`cable_coils.yml`; the manifest does not need a self-referential row, consistent with
every prior WP's manifest section) has a row.

## 6. Plan conformance

PLAN5 §4 WP13-4's table lists exactly 1 file (`cable_coils.yml`, PROTO U, 1 line) — it exists and matches
verbatim. PLAN5 §9's WP13-4 row ("cable coil treatmentCapabilities, 1 file") matches. DECISIONS.md's Phase-5
§8.4 answer relevant to this WP — **U5: "YES, nerf now — Mechanical-only `treatmentCapabilities`; humans
lose the cable-coil burn heal. Changelog-worthy."** — is honoured in substance: the shipped value is
`[Electrical]`, not literally `[Mechanical]`, but this matches PLAN5's own resolved PROTO U spec (which
predates and refines the DECISIONS.md shorthand with full reasoning: `[Electrical]` already overlaps both
IPC's and cybernetic's `[Mechanical, Electrical]` profiles, so the practical outcome — organics lose the
heal, IPC/cybernetic keep it, changelog-worthy — is exactly what U5 calls for). This same "Mechanical-only"
phrasing is present verbatim in the master planning-stage `C:/tmp/wolfmed-plan/DECISIONS.md`, i.e. it
predates WP13-4 and is not something this package introduced or could have "corrected" without exceeding
its one-line mandate. Noted as a documentation-wording nit, not a functional deviation (see Minor finding
below). No other §8.4 answer names WP13-4 in PLAN5 §8.4's "Needed by" column.

PLAN5 §4's WP13-4 Checkpoint line ("YAMLLinter; `DockTest`") was **not** re-run by the report, which defers
both to WP13-7's cross-package reconciliation pass, citing "no new prototype ids and no new file means no
new failure surface." Independently assessed: the headless server run in §1 above loads every prototype
in the game (including `cable_coils.yml`) through the same `PrototypeManager` YAMLLinter itself exercises,
and reached `Ready` with zero errors — a malformed `TreatmentCapability` enum value or YAML structure error
would have thrown at prototype-load time, before `Ready`. This is strong (though not identical) evidence in
place of a standalone YAMLLinter run. Treated as a minor process note, not a blocker.

## 7. Snapshot

Written:
- `C:/tmp/wolfmed-plan/p5/snapshots/WP13-4.patch` (1421 lines) — `git diff HEAD -- Content.Shared
  Content.Server Content.Client Resources Docs Content.IntegrationTests`. Contains the cumulative WP13-0
  through WP13-4 diff, consistent with sequential no-commit execution.
- `C:/tmp/wolfmed-plan/p5/snapshots/WP13-4.untracked.txt` (2 lines) — the same two new files as every prior
  snapshot (`Resources/Prototypes/_WF/Wolfmed/Body/species_parts.yml`,
  `Resources/Prototypes/_WF/Wolfmed/Damage/containers.yml`), both from earlier packages (WP13-0/WP13-1), not
  from WP13-4.

## Findings

- **Minor** — DECISIONS.md's U5 answer text says "Mechanical-only `treatmentCapabilities`" while the shipped
  value is `[Electrical]`. Functionally correct (verified end-to-end in §4: `[Electrical]` overlaps both
  IPC's and cybernetic's `[Mechanical, Electrical]` profiles and does not overlap organic's `[Biological]`
  profile — the exact outcome U5 asks for), and the wording predates this WP in the master planning doc, so
  it is a pre-existing shorthand-vs-spec phrasing gap, not something WP13-4 could fix within its one-line
  mandate. No action needed; flagging for WP13-7's docs pass to consider tightening the DECISIONS.md phrase
  if it revisits changelog wording.
- **Minor** — PLAN5's own WP13-4 Checkpoint line calls for YAMLLinter + DockTest; the report skipped both,
  deferring to WP13-7. The headless server run (§1/§6) substantially covers the YAMLLinter risk for this
  specific single-scalar-field change (prototype load already succeeded with zero errors). Not re-run by
  this verifier either, per the checklist actually assigned to this pass. No functional risk identified.

## Verdict

**PASS.** Both builds clean (0 errors), headless server clean for 120 s (no ERRO/FATL/Exception, no
wound/body/species/prototype warnings), the one-line PROTO U edit matches PLAN5 §3.2 verbatim and Onyx's own
`[Electrical]` value, the full HOOK 8 / `IsCompatiblePart` / profile-overlap mechanism was independently
re-derived from source and confirmed to produce exactly the claimed organic-loses/IPC-and-cybernetic-keep
behaviour, zero new C#, zero new prototype ids, zero unmarked or unauthorized upstream edits anywhere in the
accumulated diff, manifest has a row for the touched file and the U5 bullet is flipped, and the plan table
is honoured. Two minor (non-blocking) documentation/process notes recorded above; no blocker or major
findings.
