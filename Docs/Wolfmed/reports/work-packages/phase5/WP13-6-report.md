# WP13-6 — Tests (PLAN5 §4 WP13-6 / §6, P5-6)

20 tests across 4 files, all green, plus the whole wound suite and the smoke filter. **No production file
touched**: zero new prototype ids, zero new `[TestPrototypes]` ids, zero new C# types/components/
subscriptions, nil D2 exposure. Three deviations from PLAN5's test wording and one correction to a WP13-2
finding, all recorded below and at the assertions themselves.

---

## 1. Files created / modified

| File (absolute) | Status | Contents |
|---|---|---|
| `C:/Users/jzo12/Documents/GitHub/Wolfgate/.claude/worktrees/rules-motd-updates-11c89c/Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedSpeciesProfileTest.cs` | **new** (8 tests) | T-P5-1, -21, -4, -8, -19, -5, -7, -6 |
| `…/Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedSpeciesSpawnTest.cs` | **new** (8 tests) | T-P5-2, -3, -12, -11, -10, -9, -20, -14 |
| `…/Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedTreatmentMatrixTest.cs` | **new** (3 tests) | T-P5-15, -16, -17 |
| `…/Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAnalyzerTest.cs` | **modified** (+1 test, +1 `using`) | T-P5-18 (`MechanicalWoundDiagnosticTextResolvesTest`), inserted before the private helper block |
| `…/Docs/Wolfmed/WOLFMED_MANIFEST.md` | **modified** | `### WP13-6` section appended before `## Phase 5 — user decisions` |

`T-P5-13` is deliberately **not** written: U13′(b) shipped, so PLAN5's own alternative **T-P5-21**
(`IpcTakesColdAndCausticTest`) replaces it, exactly as §6.2 specifies.

Line endings: the four `.cs` files are **LF** in both git and the working tree, matching every existing file
in `Tests/_WF/Wolfmed/` (verified with `file` and `git show HEAD:…`). The manifest is **CRLF**, and the
appended block was converted to CRLF before insertion.

---

## 2. Every test, with where its expected value comes from

### `WolfmedSpeciesProfileTest.cs`

| Test | PLAN5 | Derivation of the literals |
|---|---|---|
| `IpcPartRoutesBleedsAndFeelsPainTest` | T-P5-1 | `IpcMechanicalDamageWound.damageTypes.Slash` sets no `severityMultiplier` → C# default 1 → Slash 20 = severity 20. Wound-level `WoundBleedingBehavior rate: 0.08 chance: 1` with **no** `minimumSeverity` → component guaranteed. `IpcBodyPartProfile` never sets `canFeelPain` → default true (trap 5: asserted as `HasComponent<PainComponent>` true). `scarrable: false` → `CreateScar` null. `WolfmedPartIpc.fractureProfile: null` → `WolfmedBodyPartSystem.Get(part).FractureProfile` null. Profile id asserted to pin R1 (parent order). |
| `IpcTakesColdAndCausticTest` | T-P5-21 | `InorganicWolfmed` = `supportedGroups [Brute, Burn] + [Radiation]`, `Burn = {Heat, Shock, Cold, Caustic}` → Caustic 15 and Cold 20 land on the part verbatim. Both types are in `IpcBodyPartProfile.acceptedDamageTypes` **and** `IpcMechanicalDamageWound.damageTypes` (`Cold` reopen 18 / mult 1, `Caustic` reopen 12 / mult 1) → 35 merged severity. Body projection asserted at `Caustic 15 / Cold 20 / TotalDamage 35` — see §4 finding WP13-6-1. Also asserts both part bases' `DamageContainerID` (PROTO O(b), P(b)). |
| `CyberneticLimbBleedsHalfFeelsNoPainAndFramefracturesTest` | T-P5-4 | `JawsOfLifeLeftArm` (the only *concrete* cybernetic arm, PLAN5 E8). `canFeelPain: false` → `PainComponent` **absent** (trap 5). `CyberneticMechanicalDamageWound` `rate: 0.08` × severity 40 × `bleedingMultiplier: 0.5` = **1.6** (`WoundBleedingSystem.RefreshWound`: `BaseRate = BleedingSeverity * behavior.Rate * multiplier`, `NaturalClotting` 0 on a fresh wound). 75 Blunt → `GetEffectiveTrauma` 75 → Comminuted (threshold 60, `creationChance: 1`) and the wound id is `CyberneticFrameFractureWound` (trap 6, R9). `scarrable: false`. |
| `CyberneticFrameFractureIsMendableBySurgeryTest` | T-P5-8 | Reuses `WolfmedStepSetBone`/`WolfmedStepMendBone` from `WolfmedWoundSurgeryTest` (global `[TestPrototypes]` pool). `CyberneticFractureProfile.reductionMinimumGrade: Simple` → Comminuted reducible; `removeWoundWhenMended: true` → `GetFracture` null after the gel step. Resolves `species.md` T3 empirically: the chain is profile-agnostic. |
| `CyberneticLimbGibCeilingTest` | T-P5-19 (U15(a)) | Reads `DestructibleComponent.Thresholds` and asserts exactly **two** `DamageTypeTrigger`s — Blunt 190, Slash 210, **no Heat** (PROTO P(c)). Behavioural halves: Slash 140 → not deleted (rung 210) and `Severable` true (arm Slash threshold 130, `_WF/Wolfmed/Body/parts.yml`); Heat 260 → not deleted, i.e. `MajorLimb`'s old Heat-250 Ash rung is genuinely gone (R12). |
| `SlimePartRoutesBleedsSoonerAndNeverFracturesTest` | T-P5-5 | `SlimeSlashWound` mult 1 → Slash 10 = severity 10. At severity **8** the slime wound has `WoundBleedingComponent` and the human `SlashWound` control does **not** — the single mechanical difference (`minimumSeverity: 9` present on the organic set, absent on the slime set). Pain present, `CreateScar` null (`scarrable: false`, and the profile does not list `MedicalScarWound`), `fractureProfile: null` so 75 Blunt produces no fracture. |
| `SlimeBleedsFifteenPercentFasterTest` | T-P5-7 | Equal severity 20, same Minor stage, same `rate: 0.1` on both prototypes → only `bleedingMultiplier` differs. Human `20 × 0.1 × 1.0 = 2.0`; slime `20 × 0.1 × 1.15 = 2.3`; ratio asserted at 1.15. |
| `DionaPartScarsAndIsNeverSeverableTest` | T-P5-6 | `AmputationThresholds` **empty** (R2: `amputationThresholds: {}` is a present key, suppressing `Base<Slot>`'s dict). `PlantSlashWound` mult 1 → severity 20. `PlantBodyPartProfile` leaves `scarrable` unset (default true) **and** lists `MedicalScarWound` → `CreateScar` succeeds — the only non-organic profile that scars. Slash total kept at 140, under the inherited 210 rung; a 14-Piercing finishing hit (DECISIONS §8.6-1) still leaves `Severable` false. |

### `WolfmedSpeciesSpawnTest.cs`

| Test | PLAN5 | Derivation |
|---|---|---|
| `IpcIsAWoundHostWithTheIpcProfileTest` | T-P5-2 | `WoundHost` + `PainShockTarget` (P5-D10). **Every** body child's profile is `IpcBodyPartProfile` and every `fractureProfile` is null. `BloodReagent == Oil`, `BloodMaxVolume == 250`, `ChemicalMaxVolume == 0` (U16). `bloodlossDamage`/`bloodlossHealDamage` key sets are exactly `{Bloodloss}` — P5-D6's "never Heat". Delete + 5 ticks with no assert. |
| `IpcLeaksOilAndTakesBloodlossTest` | T-P5-3 (U2(a)) | `DamageContainerID == SiliconWolfmed`. Slash 30 → `BleedAmount > 0` (wound-level `chance: 1`). Blood solution contains `Oil`; `GetBloodLevelPercentage` falls after `TryModifyBloodLevel(-50)`. **The guard**: `Bloodloss 10` lands as 10 on the body — 0 without `SiliconWolfmed`, since stock `Silicon` has no `Bloodloss` type and `DamageableSystem` drops unsupported types silently. |
| `WelderStillRepairsAnIpcTest` | T-P5-12 (R3) | `SiliconRepairFinishedEvent` is `protected` inside `SharedWeldingHealableSystem` and cannot be raised from a test, so the **gate** is asserted directly (`Welder` and `NaniteApplicator` `WeldingHealing.DamageContainers` both contain the IPC's container id — PROTO S/T) and the **payload line** `OnRepairFinished` runs after it is driven by hand (`TryChangeDamage(body, welderHealing.Damage, ignoreResistances: true)`), asserting the chassis wound's severity falls. |
| `IpcLimbSeverabilityAndGibCeilingTest` | T-P5-11 (U3′(b)) | (i) Slash 140 → not deleted (rung 210, raised from 150 by PROTO O(c)) and `Severable` true (140 ≥ 130). (ii) Blunt 195 on a fresh IPC → **deleted** (rung 190, raised from 110), because the Blunt amputation threshold is 250 — trap 7 / R11 documented, not asserted as working. |
| `IpcDoesNotGibFromRoutineLimbDamageTest` | T-P5-10 | 150 Blunt on each of the four limbs — each under its own 190 rung — puts >400 on the projected body total (asserted, so the guard is not vacuous) and the mob survives, because PROTO Q raised its own threshold 400 → 1500 (D22/P5-D11). |
| `ProtogenIsAWoundHostTest` | T-P5-9 (U4(a)) | Both hosts EXT 3 creates: `MobProtogen` **and** `MobProtogenRandom`. `WoundHost` present; every part's profile is `OrganicBodyPartProfile`; **no organ carries `OrganDamageComponent`** — P5-D19/U12′ pinned as a known gap (trap 9: an absence assertion, never a positive organ test). Delete + 5 ticks guards the WP9 `RemCompDeferred` crash class. |
| `DionaLimbIsDestroyedNotSeveredTest` | T-P5-20 (U17(a)) | `AmputationThresholds` empty **and** Blunt 195 deletes the limb via the inherited `MajorLimb` Blunt-190 rung. This is the test that stops "cannot be severed" being read as "indestructible". |
| `EveryBodyPartProfileUsesThePrimaryStreamTest` | T-P5-14 | Enumerates every `BodyPartProfilePrototype` and asserts `CirculatoryStream == CirculatoryStreamPrototype.PrimaryStream`. Enforces P5-D3 loudly: a stray `circulatoryStream:` would silently zero that species' bleeding with no log. |

### `WolfmedTreatmentMatrixTest.cs`

| Test | PLAN5 | Derivation |
|---|---|---|
| `TreatmentCapabilityMatrixTest` | T-P5-15 | Six cells driven through `WoundDamageRoutingSystem.WithTreatmentCapabilities` — the same scope HOOK 9 opens for a reagent and HOOK 8 for an item. Slash 30 on both bodies (mult 1 in both wound sets, trap 3 rules out Blunt). Biological heals only the organic wound (30 → 25); Mechanical and Electrical each heal only the chassis wound (30 → 25 → 20). Every refused cell leaves the **flat part damage** unchanged too, since `CanTreatPart` refuses the part outright. |
| `CableCoilNoLongerHealsOrganicsButHealsMechanicalTest` | T-P5-16 (U5) | `CableApcStack`'s `HealingComponent.TreatmentCapabilities == {Electrical}` (PROTO U). Heat 20 on both bodies. Organic: `TryApplyHealing` returns **false**, severity unchanged. IPC: returns true, severity 20 → **17** (the coil's `Heat: -3.0` × `UniversalTopicalsHealModifier` 1). The nerf and its compensating gain in one test. |
| `CableCoilRadiationStillHealsSystemicallyTest` | T-P5-17, re-derived | See §4 finding WP13-6-2. |

### `WolfmedAnalyzerTest.cs` (extended)

`MechanicalWoundDiagnosticTextResolvesTest` (T-P5-18): severity 30 sits in the Moderate band
(`Minor 0 / Moderate 25 / Severe 50 / Critical 80`), so `BuildWoundDiagnostics` yields exactly one
`HealthAnalyzerVisibleWound` with `Name == "wound-name-ipc-mechanical-damage"`,
`StageName == "wound-stage-mechanical-moderate"`, `Count == 1`, and both resolve through
`ILocalizationManager` to **"chassis damage"** and **"moderate"** rather than to a raw key.

---

## 3. WOLFGATE edits

**No production file was touched.** Every marked comment is inside a test file and documents a derivation or
a deviation. The load-bearing ones:

1. `WolfmedSpeciesProfileTest.cs` class remarks — why every routed hit passes `ignoreResistances: true`
   (finding WP13-6-5), and the three traps that shape the file.
2. `WolfmedSpeciesProfileTest.cs` `IpcTakesColdAndCausticTest` — the WP13-2-1 correction, stated in full at
   the assertion (finding WP13-6-1).
3. `WolfmedSpeciesProfileTest.cs` `CyberneticLimbBleedsHalfFeelsNoPainAndFramefracturesTest` — why the
   bleed-rate assertion uses a second, undamaged prosthetic and `CreateOrMergeWound` (finding WP13-6-4).
4. `WolfmedSpeciesSpawnTest.cs` `WelderStillRepairsAnIpcTest` — why the do-after event cannot be raised from
   a test and what stands in for it.
5. `WolfmedTreatmentMatrixTest.cs` `CableCoilRadiationStillHealsSystemicallyTest` remarks — the T-P5-17
   re-derivation (finding WP13-6-2).
6. `WolfmedTreatmentMatrixTest.cs` usings — `WoundHealingSystem`'s file is under `Content.Server/_Onyx/Wounds`
   but its namespace is the shared one, the same mismatch PLAN5 N2 records for `CirculatoryStreamSystem`.
7. `WolfmedAnalyzerTest.cs` `MechanicalWoundDiagnosticTextResolvesTest` remarks — why `Mechanical == true` is
   not asserted (finding WP13-6-3), and `using Robust.Shared.Localization;`.

---

## 4. Deviations from PLAN5, with justification

**WP13-6-1 — correction to WP13-2's finding "WP13-2-1".** WP13-2 recorded that `Cold`/`Caustic` reach an IPC
*part* but are dropped from the *body* total, because the mob container `SiliconWolfmed` supports neither, and
handed the resulting "asymmetry" to the balance pass. **Measured, that is not what happens.**
`WoundDamageProjectionSystem.RefreshBodyDamage` projects the part total with
`WolfmedDamageableSystem.SetDamage` (`Content.Shared/_WF/Wolfmed/Compat/WolfmedDamageableSystem.cs:143-171`),
which writes `dict[type] = amount` for every type in the incoming spec. That is a **set**, not a
`TryChangeDamage`, so `DamageableSystem`'s container filter
(`if (!dict.TryGetValue(type, out var oldValue)) continue;`, `DamageableSystem.cs:270-277`) is never on the
path, and `DamageChanged` then recomputes `TotalDamage` from the whole dict. `Caustic 15` + `Cold 20` on
`LeftArmIPC` lands on `MobIPC` as `Caustic 15 / Cold 20 / TotalDamage 35`. The mob container gates what can be
**dealt to the mob directly**, not what the projection writes.
*Consequence:* U13′(b) is more complete than WP13-2 believed — acid and cryogenics really can kill an IPC,
exactly as in Onyx. **WP13-7 should strike the "Cold/Caustic body-total asymmetry" from the deviations block
and from the balance-pass inbox rather than carry it forward.** The test asserts the measured values with the
full explanation inline, so the behaviour cannot drift silently in either direction. *(The test was written
the other way first, on WP13-2's word, and failed — the failure is what surfaced this.)*

**WP13-6-2 — T-P5-17 re-derived.** PLAN5 words it as "the coil's `Radiation: -3.0` component still lands" on
an organic host. It does not. `WoundHealingSystem.OnResolveHealingPart` sets
`Accepted = Part != null || !hasLocalized`, where `hasLocalized` is true if **any** type in the item's spec is
in `WoundHostComponent.LocalizedDamageTypes`. The coil's spec carries `Heat` and `Shock`, both localized, so
on a body with no capability-compatible part the entire application is refused before `Radiation` is looked
at. The invariant PLAN5 actually wanted — *systemic types bypass the capability gate* — lives one layer down
and is asserted directly: systemic `Radiation 10` healed by `-3` inside a
`WithTreatmentCapabilities({Electrical})` scope on an organic host falls to 7. Both halves are in the test,
with the correction at the assertion. No production behaviour is wrong here; only PLAN5's description of the
layer was.

**WP13-6-3 — T-P5-18 does not assert `Mechanical == true`.** That payload member does not exist. WP13-5
shipped only the locale half of PLAN5 §2.6 (the four new `-mechanical`/`-frame` keys) and explicitly deferred
the C# half — the appended `bool Mechanical` on `HealthAnalyzerWoundDiagnostic` plus the three consumer
branches. The test asserts everything assertable today (the per-wound `Name`/`StageName` path, complete since
WP13-0) and carries a marked note telling the follow-up package exactly where to add the flag assertion. This
matches WP13-5's own handoff note 4, which predicted T-P5-18 would be unaffected.

**WP13-6-4 — bleed-rate assertions create the wound directly.** `WoundBleedingSystem.HandlePartDamageApplied`
immediately reduces a damage-created bleed by the body's `DamageBleedModifiers`, which differ per species, so
a routed wound's `CurrentRate` is not comparable across species. Severity, presence and absence are all
asserted from routed damage as PLAN5 intends; only the two multiplier assertions (T-P5-4's `0.5`, T-P5-7's
`1.15`) use `CreateOrMergeWound`. T-P5-4's rate is read off a second, undamaged prosthetic for the same
reason.

**WP13-6-5 — every routed hit passes `ignoreResistances: true`.** `DamageableSystem.TryChangeDamage` applies
the body's `damageModifierSet` **before** the Wolfmed routing seam (resistance block, then the
`DamageDealtEvent` raise), and all three new species carry one: IPC `Cold 0.2 / Heat 1.5 / Shock 2.5`, Slime
`Slash 1.2 / Blunt 0.6`, Diona `Slash 0.8 / Blunt 0.7`. Without the flag, "Slash 20" would be a different
severity on each species and every literal in these files would be a per-species number instead of a
profile-derived one — which would defeat the purpose of the package.

**Not a deviation, worth stating:** PLAN5's T-P5-11 expects "Blunt 190 < Blunt threshold 250" and T-P5-6
expects diona limbs to survive under the rung; both matched WP13-1's measured table exactly, as did every
other threshold. WP13-1's two corrections to PLAN5 (organic heads *do* have a 500/600/700 rung; slime limbs
*are* severable) are consistent with these tests — no slime severability assertion was written, and no test
depends on the head claim.

---

## 5. Build / server / test output

```
Content.Server            (-c DebugOpt)   Build succeeded.   0 Error(s)
Content.Client            (-c DebugOpt)   Build succeeded.   0 Error(s)
Content.IntegrationTests  (-c DebugOpt)   Build succeeded.   0 Error(s)
```

Headless server, 120 s, port 1299 (`C:/tmp/wolfmed-plan/p5/wp/WP13-6-report-server.log`, 110 lines);
`grep -cE "\[ERRO\]|\[FATL\]|Exception"` = **0**:
```
[INFO] root: Server Version 277.0.0.0 -> Ready
[INFO] net: "::": "Socket bound to [::]:1299: True"
[INFO] net: "0.0.0.0": "Socket bound to 0.0.0.0:1299: True"
```

`DockTest` first and alone (`WP13-6-docktest.log`) — no environmental `db.ef` masking, no re-run needed:
```
Test Run Successful.
Total tests: 3
     Passed: 3
```

Wound suite, `--filter "FullyQualifiedName~_Onyx.Wounds|FullyQualifiedName~_Onyx.Medical|FullyQualifiedName~Wolfmed"`
(`WP13-6-tests.log`):
```
Test Run Successful.
Total tests: 116
     Passed: 116
 Total time: 1.8356 Minutes
```
All 20 new tests verified present in that run by name (each appears exactly once as `Passed`).

Smoke, `--filter "FullyQualifiedName~EntityTest|FullyQualifiedName~PrototypeSaveTest|FullyQualifiedName~DockTest"`
(`WP13-6-smoke.log`):
```
Test Run Successful.
Total tests: 11
     Passed: 9
    Skipped: 2
```
Both skips are **pre-existing `[Ignore]` attributes** in `Content.IntegrationTests/Tests/EntityTest.cs`:
`SpawnAndDirtyAllEntities` (`[Ignore("Preventing CI tests from failing")]`, line 150) and
`SpawnAndDeleteEntityCountTest` (`[Ignore("Even wizden calls this test ass")]`, line 238). Everything that
matters ran green: `SpawnAndDeleteAllEntitiesOnDifferentMaps`, `SpawnAndDeleteAllEntitiesInTheSameSpot`,
`AllComponentsOneToOneDeleteTest`, `PrototypeSaveTest.UninitializedSaveTest`, `DockTest` ×3.

**No real production failure was found.** The single red run during development was
`IpcTakesColdAndCausticTest`, and it was the test's own premise (inherited from WP13-2's report) that was
wrong, not the code — see finding WP13-6-1. No production edit was needed or made.

YAMLLinter **not run**: this package touches no YAML or FTL, and PLAN5's WP13-6 checkpoint lists only the
Wolfmed and smoke filters.

### Artefacts

| Path | Contents |
|---|---|
| `C:/tmp/wolfmed-plan/p5/wp/WP13-6-report-server.log` | 120 s headless run, port 1299, 0 errors |
| `C:/tmp/wolfmed-plan/p5/wp/WP13-6-docktest.log` | `DockTest` 3/3 |
| `C:/tmp/wolfmed-plan/p5/wp/WP13-6-tests.log` | Wolfmed suite 116/116 |
| `C:/tmp/wolfmed-plan/p5/wp/WP13-6-smoke.log` | `EntityTest`/`PrototypeSaveTest`/`DockTest` 9 passed, 2 pre-ignored |
| `C:/tmp/wolfmed-plan/p5/wp/WP13-6-new.log` | first full run of the four files (27 passed, 1 red — the WP13-6-1 discovery) |

---

## 6. What later packages must know

- **WP13-7 must strike the "`Cold`/`Caustic` body-total asymmetry" (WP13-2 §6 / WP13-2-1)** from the phase-5
  deviations block, the status doc and the balance-pass inbox. It does not exist: the projection writes
  through `SetDamage`, which bypasses the container filter, so an IPC's `TotalDamage` includes both types and
  acid/cryo can kill one. Finding WP13-6-1 has the citations; `IpcTakesColdAndCausticTest` is the proof.
- **WP13-7 must not repeat PLAN5's T-P5-17 or T-P5-18 wording** in the shipped `WOLFMED_PLAN5.md` §6 without
  the corrections in findings WP13-6-2 and WP13-6-3 — the first misstates which layer refuses an incompatible
  item, the second names a payload member that does not exist.
- **The `Mechanical` flag is still the one open piece of P5-5.** When a follow-up package lands the
  `bool Mechanical` append on `HealthAnalyzerWoundDiagnostic` and the three consumer branches, add the flag
  assertion to `MechanicalWoundDiagnosticTextResolvesTest` — the marked note in that test says so and says
  where. Nothing else in the suite is affected.
- **Test-writing rules this package established**, for phase 6 and the balance pass:
  (a) pass `ignoreResistances: true` on any routed hit whose exact severity matters — the species modifier
  sets apply before the routing seam;
  (b) never assert a bleed **rate** from routed damage — `HandlePartDamageApplied` reduces it by the body's
  `DamageBleedModifiers`;
  (c) assert destruction only after `Pair.RunTicksSync` — `GibPartBehavior` ends in `QueueDel`;
  (d) `SiliconRepairFinishedEvent` is `protected` and cannot be raised from a test; drive
  `OnRepairFinished`'s payload line instead and assert the gate separately.
- **`WolfmedStepSetBone` / `WolfmedStepMendBone` are now used by two fixtures** (`WolfmedWoundSurgeryTest`
  and `WolfmedSpeciesProfileTest`). Do not rename or retune them without checking both.
- **Balance-pass inbox from this package:** nothing new. Every asymmetry these tests pin (IPC extremities
  tougher than flesh, the IPC head at 190, pure-Blunt severing unreachable on every species) was already
  recorded by WP13-1/WP13-2 and is asserted here as documentation rather than as a defect.
