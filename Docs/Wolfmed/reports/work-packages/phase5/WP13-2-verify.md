# WP13-2 verification — IPC as a wound host, and circulation (P5-1c / P5-2)

Scope re-checked: PLAN5 §WP13-2 (PROTO Q on `MobIPC`, §2.4 `SetBleedRates` hardening), DECISIONS.md's phase-5
scope block and its §8.4 answers, and `WP13-2-report.md`'s claims, against the actual worktree, Onyx source
(via `git show`, since `Corvax/Body/Species/ipc.yml` is outside the sparse checkout), and independently
re-run builds/server/tests.

**Verdict: PASS.**

---

## 1. Build / server (independently re-run)

```
dotnet build Content.Server/Content.Server.csproj -c DebugOpt -v q -nologo   -> Build succeeded. 0 Error(s)
dotnet build Content.Client/Content.Client.csproj -c DebugOpt -v q -nologo   -> Build succeeded. 0 Error(s)
```

Headless server, port 1299, ~130 s (`WP13-2-verify-server.log`, 109 lines):
- Reaches `Server Version 277.0.0.0 -> Ready`, binds `[::]:1299` and `0.0.0.0:1299`.
- `grep -nE "\[ERRO\]|\[FATL\]|Exception"` → **0 hits**.
- `grep -inE "wound|bloodstream|siliconwolfmed|inorganicwolfmed|MobIPC|circulator"` → **0 hits** (no
  bloodstream-solution warning, no unknown-component/field, no missing-prototype/parent, no duplicate id).

Matches the report's own build/server log content exactly.

## 2. Upstream discipline

`git diff HEAD --stat -- Content.Shared Content.Server Content.Client Resources Content.IntegrationTests`
(no Docs) shows 9 tracked files changed, cumulative across WP13-0/WP13-1/WP13-2 (nothing in phase 5 is
committed yet, per DECISIONS.md's "no commits" rule and the same pattern already verified in
`WP13-0-verify.md`/`WP13-1-verify.md`). Of those 9, **WP13-2 itself only touches two**, confirmed by diffing
each file and cross-checking against WP13-1's own report/verify (which already accounts for the other 7 —
`diona.yml`, `slime.yml`, `welders.yml`, `_EinsteinEngines/Body/Parts/ipc.yml`, `nanite_applicator.yml`,
`_Onyx/Wounds/wounds.yml`, `_Shitmed/Body/Parts/cybernetic.yml` — as WP13-0/WP13-1's, with zero further
edits from WP13-2):

- **`Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/ipc.yml`** (+48/−1, matches the report's
  claim exactly). Every added line sits under a `# WOLFGATE (...)` comment block: `- type: WoundHost` (P5-1/
  P5-2, PROTO Q), `- type: PainShockTarget` (P5-D10/U1), `- type: Bloodstream` with `bloodReagent: Oil`,
  `bloodMaxVolume: 250`, `chemicalMaxVolume: 0`, `bloodlossDamage {Bloodloss: 0.5}`,
  `bloodlossHealDamage {Bloodloss: -1}` (P5-2/P5-D4/U16, with P5-D6's Bloodloss-not-Heat reasoning inline),
  `- type: Damageable` with `damageContainer: SiliconWolfmed` + `damageModifierSet: IPC` (P5-D5/P5-D5b), and
  the `Destructible` `damage: 400 → 1500` with a `# WOLFGATE (D22/P5-D11)` comment placed above the block
  (D3's documented deviation from PLAN5's exact comment placement — legal YAML, same information, no
  behaviour change). **No unmarked line.** Content matches PLAN5's "PROTO Q in full" snippet (§3) almost
  verbatim — the few wording differences (citation line numbers folded together, `# WOLFGATE (P5-1/P5-2,
  PROTO Q)` vs the plan's split multi-line comment) carry identical meaning.
- **`Content.Server/_Onyx/Chemistry/Circulation/CirculatoryStreamSystem.cs`** (+9/−3, matches). In-vendored
  `_Onyx` file, `SetBleedRates`'s `rates.GetValueOrDefault(PrimaryStream)` replaced by a `foreach` sum over
  `rates.Values`, marked `// WOLFGATE: P5-2/P5-D3` verbatim against PLAN5 §2.4's code block (identical
  variable names, identical comment text, identical call to `TryModifyWoundBleedProjection`). No new
  `using`. Every changed line is inside the marked block; the pre-existing D15 comment above it is
  untouched.

Both edits are authorised by PLAN5 §3 for WP13-2 (PROTO Q's table row and the §3.3 in-vendored-edit table
row both name WP13-2 as owner). **D2 holds**: the `SiliconWolfmed` switch is confined to the single
`MobIPC` prototype (confirmed in §4 below — no other entity parents `PlayerSiliconHumanoidBase`, and
`MobIPCDummy` parents `MobHumanDummy`, unaffected); the `SetBleedRates` sum is mathematically a no-op for
every existing profile (organic and IPC alike) since exactly one `circulatoryStream` value (`Organic`)
exists anywhere in the tree (P5-D3, re-confirmed against `IpcBodyPartProfile`'s measured
`circulatoryStream = Organic`) — summing one populated dictionary entry equals reading it directly. No
non-wound-host entity is reachable by either change. The protogen `WoundHost` exclusion lift (the one
documented exception to "D2 holds") is explicitly WP13-3's job and is untouched here.

**PROTO S/PROTO T** (`welders.yml`, `nanite_applicator.yml`) are in PLAN5's WP13-2 file table but were
**not** touched by this package — verified already landed in WP13-1 (its own D1 deviation), each line
carrying `# WOLFGATE (P5-D5, PROTO S/T)`. This was independently verified in `WP13-1-verify.md` and is
unchanged here; re-diffing both files against the current tree shows zero further edits from WP13-2. The
deviation is consistently tracked across both WP reports and both verify passes — not a discrepancy.

## 3. Vendoring fidelity

WP13-2 vendors no wholesale YAML block from Onyx (the 16 wound prototypes and 4 body-part abstracts were
WP13-0/WP13-1's job, already verified). Its Onyx-sourced content is the *numeric/structural* content of
PROTO Q, checked directly against Onyx via `git show` (the exact path `Corvax/Body/Species/ipc.yml` is
outside the pinned sparse checkout's working tree, but `git show HEAD:<path>` still resolves it from the
partial clone — the same technique `WP13-1-verify.md` used):

- `git show HEAD:Resources/Prototypes/Corvax/Body/Species/ipc.yml` → `MobIpc`'s `- type: Bloodstream` block
  reads `bloodlossDamage: {Bloodloss: 0.5}` and `bloodlossHealDamage: {Bloodloss: -1}` — **byte-identical**
  numbers to what WP13-2 wrote (report's P5-D4 claim confirmed). `bloodReferenceSolution: {Oil, 250}` maps
  onto Wolfgate's `bloodReagent: Oil` / `bloodMaxVolume: 250` exactly as the marked comment states (WG's
  `BloodstreamComponent` has no `bloodReferenceSolution` field, confirmed by reading
  `Content.Server/Body/Components/BloodstreamComponent.cs` — no such member exists, `BloodReagent`/
  `BloodMaxVolume`/`ChemicalMaxVolume` do). `- type: WoundHost` matches directly.
- `git show HEAD:Resources/Prototypes/Body/species_base.yml` → confirms `- type: PainShockTarget
  # <Onyx-PainShock>` sits on `BaseSpeciesMob` (not on `MobIpc` itself), and `MobIpc`'s `parent:` list
  (`[AppearanceIpc, BaseSpeciesMob, MobBloodstream]`) includes `BaseSpeciesMob` — so Onyx IPCs genuinely do
  inherit pain shock, exactly as P5-D10 and the marked comment claim.
- Onyx's `MobIpc` carries **no** `Destructible` block at all (confirmed: `grep -n "Destructible"` over the
  file returns nothing). The `400 → 1500` raise is therefore correctly *not* presented as vendored content —
  it's a Wolfgate-only D22 concern, and the report/manifest never claim otherwise.
- Onyx's container split (`- type: Damageable / damageModifierSet: Ipc` + a separate
  `- type: Injurable / damageContainer: SiliconIpc`) doesn't exist in Wolfgate (`Injurable` isn't a WG
  component); WG's `Damageable` carries both `damageContainer` and `damageModifierSet` in one component.
  This is a pre-established architecture difference (not new to WP13-2), correctly handled by putting both
  fields on the one `Damageable` block.

Every differing line (the field-name remapping, the container id, the Destructible raise) carries a
`# WOLFGATE` marker. **Fidelity confirmed** on every number that has a direct Onyx counterpart.

## 4. Collisions / inheritance

- **New prototype ids: none.** `SiliconWolfmed`/`InorganicWolfmed` were both created by WP13-0 (already
  grepped clean there); WP13-2 only *applies* the first to `MobIPC`.
- **New C# files: zero**, including tests — `git ls-files --others --exclude-standard` over
  `Content.Shared/Content.Server/Content.Client/Content.IntegrationTests` returns no `.cs` path; the
  throwaway `ZzWp132ProbeTest.cs` is confirmed absent from the tree (`find` returns nothing).
- **`MobIPC` parent chain:** `grep -n "id: MobIPC"` / `parent:` shows a single-entry parent,
  `PlayerSiliconHumanoidBase`. Grepping `Resources/Prototypes` for `PlayerSiliconHumanoidBase` returns
  exactly 3 hits: the declaration (`silicon_base.yml:3`), `MobIPC`'s `parent:` line, and the stale TODO
  comment in `Entities/Mobs/Species/base.yml:9` — confirming `MobIPC` is the *only* descendant, exactly as
  D11/M1 claim. `MobIPCDummy` separately parents `MobHumanDummy`, unaffected.
- **Effective `Destructible` threshold:** `PlayerSiliconHumanoidBase`'s own block (`silicon_base.yml:91-94`)
  is `!type:DamageTrigger damage: 500` (a *total*-damage trigger). `MobIPC` declares its own
  `- type: Destructible` with one `!type:DamageTypeTrigger damageType: Blunt damage: 1500`.
  `DestructibleComponent.Thresholds` is confirmed a plain `[DataField("thresholds")]` list
  (`Content.Server/Destructible/DestructibleComponent.cs:12-13`, read directly — no `Always`-composition
  attribute), so RT's default list-composition rule applies: the child's list **replaces** the parent's
  wholesale. Effective threshold on a spawned `MobIPC` = **exactly one** `DamageTypeTrigger` (Blunt 1500);
  the inherited 500 total-damage trigger is unreachable. This matches the report's measured probe output
  (`Destructible trigger=DamageTypeTrigger {}`) and the manifest's claim. `PROTO R` (touching
  `silicon_base.yml`) is correctly left dropped (U18) — the edit would be a provable no-op.
- **`SiliconWolfmed` composition:** `supportedGroups: [Brute]` + `supportedTypes: [Heat, Shock, Radiation,
  Bloodloss]`. `Brute` group = `[Blunt, Slash, Piercing]` (`Damage/groups.yml:2-7`, confirmed). Union =
  `{Blunt, Slash, Piercing, Heat, Shock, Radiation, Bloodloss}` — **exactly** the 7 types in the probe log's
  measured `DamageableComponent.Damage.DamageDict` keys. `IPC` damageModifierSet (`_EinsteinEngines/Damage/
  modifier_sets.yml`) carries `Cold: 0.2`, confirming WP13-2-1's stated asymmetry (a modifier for a type the
  container can never hold) is real, not invented.
- **`BloodlossDamage`/`BloodlossHealDamage` required-field claim:** confirmed —
  `Content.Server/Body/Components/BloodstreamComponent.cs` has both marked `[DataField(required: true)]`
  (lines 69 and 76), matching the report's citation.
- **`WoundDamageComponents.cs` `LocalizedDamageTypes` claim:** confirmed — the default set is `[Blunt,
  Slash, Piercing, Heat, Cold, Shock, Caustic]`; `Bloodloss` is absent, confirming P5-D6's "Bloodloss stays
  systemic, Heat would self-reinforce" reasoning and the WP13-2-1 finding's premise both hold.
- **`DamageableSystem`'s silent-drop-of-unsupported-types claim:** confirmed —
  `if (!dict.TryGetValue(type, out var oldValue)) continue;` in the body-level `ChangeDamage` path skips any
  damage type the container doesn't define, exactly as cited.
- **`SolutionContainerManager` / `EnsureSolution` claim:** confirmed —
  `BloodstreamSystem.OnComponentInit` (`Content.Server/Body/Systems/BloodstreamSystem.cs:180`) calls
  `_solutionContainerSystem.EnsureSolution` three times, which is why `MobIPC` doesn't need to declare
  `SolutionContainerManager` itself.
- **P3-D1 vital-part Bloodloss charge claim:** confirmed —
  `Content.Server/_WF/Wolfmed/WolfmedBodyPartLifecycleSystem.cs` applies a `Bloodloss` `DamageSpecifier` to
  the body on vital-part loss, which previously had nowhere to land on `MobIPC` (stock `Silicon` has no
  `Bloodloss`) and now does via `SiliconWolfmed`.
- **`MobIPC` and `PrototypeSaveTest` exposure:** confirmed `save: false` on `PlayerSiliconHumanoidBase:2`,
  inherited by `MobIPC`, so it sits outside `UninitializedSaveTest`'s prototype set — the report's stated
  reason `SolutionContainerManager` was deliberately not declared holds.

No collision found anywhere in this package's own additions (it adds zero new ids).

## 5. Manifest

`Docs/Wolfmed/WOLFMED_MANIFEST.md` carries a `### WP13-2` section (line 2258) with one table row for each of
WP13-2's two tracked-file edits (`ipc.yml` Mob, `CirculatoryStreamSystem.cs`), the PROTO S/T
not-re-applied note, the PROTO R dropped note, the measured-state table (matching `WP13-2-probe.log`
line for line), the WP13-2-1 finding written out in full, the "what an IPC experiences" block, and the
phase-5 user-decision ticks for U1/U2(a)/U16/U18/U11(a) — all present and consistent with the report and
with what I independently re-derived above.

**One minor inaccuracy, not a blocker:** the report's own file table (§1) annotates the manifest edit as
"(+303)". `git diff HEAD --stat -- Docs/Wolfmed/WOLFMED_MANIFEST.md` does return `303 insertions(+)` — but
that is the **cumulative** Phase 5 total (the whole uncommitted "## Phase 5" section, `### WP13-0` +
`### WP13-1` + `### WP13-2` + the user-decisions block, none of it committed yet), not WP13-2's own
contribution. WP13-2's own section (`### WP13-2` through the end of its "what an IPC experiences" block,
lines 2258–2373) is ~116 lines. The manifest's *content* is correct and none of it is misattributed to the
wrong package internally — only the report's single summary digit over-counts by including WP13-0's and
WP13-1's already-verified manifest growth. Cosmetic; no action needed.

## 6. Plan conformance / DECISIONS §8.4

Every file in PLAN5's WP13-2 table (§4) is accounted for: `ipc.yml` (PROTO Q, landed here) and
`CirculatoryStreamSystem.cs` (§2.4, landed here) are both present and correctly edited (§2/§3 above).
`welders.yml`/`nanite_applicator.yml` (PROTO S/T) are also present — landed one package early by WP13-1's
D1, already accepted in `WP13-1-verify.md`, and correctly *not* re-touched here (re-applying them would risk
a duplicate-line YAML mistake for zero benefit).

DECISIONS.md's "Phase 5 — answers to PLAN5.md §8.4" answers relevant to this WP:
- **U1 = "KEEP Onyx's pain on IPCs"** — landed exactly: `PainShockTarget` on `MobIPC`, `canFeelPain` left at
  its Onyx-faithful default (`true`), verified live (`Pain=True` in the probe log). The stated caveat (no
  chemical relief) is carried into the manifest and the report's §5/§7.
- **U2 = "(a) SiliconWolfmed container + PROTO S/T companion lines"** — the container switch lands here
  (`MobIPC`'s `Damageable.damageContainer = SiliconWolfmed`, measured); the companion lines already landed
  in WP13-1. Manifest correctly marks U2(a) COMPLETE only now that both halves exist.
- **U16 = "(a) chemicalMaxVolume: 0, no InjectableSolution"** — landed and measured
  (`chemicalMaxVolume=0`, `InjectableSolution=False`).
- **U18 = "(a) drop PROTO R"** — honoured: `silicon_base.yml` untouched, confirmed by the diff stat above
  (not in the 9-file change list) and by the single-threshold measurement in §4.
- **U11(a)** ("keep `BloodRefreshAmount` default") — measured `refresh=1` in the probe log, matching Onyx's
  unmodified default.
- No other §8.4 row (U3′, U4, U5, U13′, U15, U17) calls for anything in WP13-2's scope; none of those files
  were touched by this package.

**Verdict: PASS.**

## 7. Snapshot

```
git -C WG diff HEAD -- Content.Shared Content.Server Content.Client Resources Docs Content.IntegrationTests > C:/tmp/wolfmed-plan/p5/snapshots/WP13-2.patch
git -C WG ls-files --others --exclude-standard -- Content.Shared Content.Server Content.Client Resources Docs Content.IntegrationTests > C:/tmp/wolfmed-plan/p5/snapshots/WP13-2.untracked.txt
```

- `WP13-2.patch` — written, 1278 lines, 11 `diff --git` headers (the 9 tracked files from §2 plus
  `Docs/Wolfmed/DECISIONS.md` and `Docs/Wolfmed/WOLFMED_MANIFEST.md`).
- `WP13-2.untracked.txt` — written, 2 lines: `Resources/Prototypes/_WF/Wolfmed/Body/species_parts.yml`,
  `Resources/Prototypes/_WF/Wolfmed/Damage/containers.yml` (both WP13-0-owned, unchanged by WP13-2).

## 8. Test re-run (independent)

- `FullyQualifiedName~SpawnAndDeleteAllEntitiesOnDifferentMaps` → **1/1 passed** (1 m 55 s). `MobIPC` is
  non-abstract, carries neither `MapGrid` nor `RoomFill` nor a spawner category, so it is included, spawned,
  ticked, and deleted with no assertion failure — the requested proof that `MobIPC` (now carrying
  `WoundHost`/`Bloodstream`/the new `Damageable`) spawns and deletes cleanly.
- `FullyQualifiedName~DockTest` → **3/3 passed**, no `db.ef` sqlite warnings (checked first per project
  memory, clean — so the entity/Wolfmed runs below needed no re-run for environmental failures).
- `FullyQualifiedName~_Onyx.Wounds|_Onyx.Body|_Onyx.Medical|Wolfmed` → **98/98 passed** (50.9 s), matching
  the report's claimed count exactly.

All three independently re-run and green.

## Summary

Both files WP13-2 owns (`ipc.yml`'s `MobIPC` block, `CirculatoryStreamSystem.cs`'s `SetBleedRates`) carry
only WOLFGATE-marked changes, content matches PLAN5 §3/§2.4 (PROTO Q, the §2.4 sum) with no unmarked line,
every DECISIONS §8.4 answer relevant to this WP (U1, U2(a), U16, U18, U11(a)) is honoured and measured, not
assumed. Vendoring fidelity against Onyx — fetched via `git show` since the mob-level IPC path is outside
the pinned sparse checkout — confirms the Bloodstream numbers, the `PainShockTarget`-on-`BaseSpeciesMob`
placement, and the absence of a `Destructible` block on Onyx's own `MobIpc`. Inheritance was re-derived by
hand: `MobIPC` is the sole descendant of `PlayerSiliconHumanoidBase`, its own `Destructible` list replaces
the parent's wholesale (a plain non-`Always` `[DataField]`), and the effective threshold is exactly the one
measured `DamageTypeTrigger` at Blunt 1500 — `PROTO R` is correctly left dropped as a proven no-op. No new
prototype ids, no new C# files (not even tests — the probe harness was deleted as claimed), and no collision
anywhere in this package's zero new identifiers. The manifest carries a complete `### WP13-2` section with a
row per touched file and correct user-decision ticks; the one inaccuracy found — the report's "(+303)"
annotation being the cumulative Phase 5 manifest total rather than WP13-2's own ~116-line share — is
cosmetic and doesn't misrepresent any actual content. Builds are independently re-run at 0/0 errors, the
120 s headless server is clean with zero error/warning lines touching wounds/bodies/species/prototypes, and
`SpawnAndDeleteAllEntitiesOnDifferentMaps`, `DockTest`, and the full Wolfmed/Onyx suite were all indepen-
dently re-run and green, matching the report's claimed counts exactly.

## Files touched by this verification

Only `C:/tmp/wolfmed-plan/p5/wp/WP13-2-verify.md` (this file), `C:/tmp/wolfmed-plan/p5/wp/WP13-2-verify-server.log`,
and the two snapshot files under `C:/tmp/wolfmed-plan/p5/snapshots/`. No file under WG was modified.
