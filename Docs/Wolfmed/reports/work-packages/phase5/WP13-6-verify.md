# WP13-6 — Verification (phase 5 — tests, P5-6)

Verifier pass over `C:/tmp/wolfmed-plan/p5/wp/WP13-6-report.md` against the real tree at
`C:/Users/jzo12/Documents/GitHub/Wolfgate/.claude/worktrees/rules-motd-updates-11c89c` (WG), cross-checked
against `DECISIONS.md` (Phase 5 + §8.4), `PLAN5.md` §WP13-6/§1/§3/§5/§8/Revision notes, and the WP13-5
cumulative snapshot (`C:/tmp/wolfmed-plan/p5/snapshots/WP13-5.patch`).

**Verdict: PASS.** No blocker, no major. One minor note (line-ending claim double-checked and confirmed
correct after an initial false alarm from a shell artifact — see §1).

---

## 1. Build / headless server

```
Content.Server  (-c DebugOpt, -v q): Build succeeded.  0 Error(s)
Content.Client  (-c DebugOpt, -v q): Build succeeded.  0 Error(s)
```

Headless server, port 1299, ~130 s (`C:/tmp/wolfmed-plan/p5/wp/WP13-6-verify-server.log`, 109 lines):
reaches `[INFO] root: Server Version 277.0.0.0 -> Ready` and binds both sockets cleanly.
`grep -cE "\[ERRO\]|\[FATL\]|Exception"` = **0** (whole log, not just the wound/species/prototype subset —
none exist at all). Confirms the report's own log independently.

*(Process note: `dotnet build` and the 130 s server run both exceed the Bash tool's default 120 s
foreground timeout and were run backgrounded / via `Monitor`; this changes nothing about the commands
executed, which match the task's spec exactly.)*

*(Line-ending sanity check: an initial `grep -c $'\r'` pass appeared to contradict the report's claim that
the four `.cs` files are LF and the manifest is CRLF. A raw `xxd` byte dump of both file families resolved
this — the `grep` result was a shell-tool artifact, not a real discrepancy. Confirmed by hex dump: the new
and modified `.cs` files contain no `0d` bytes (pure LF), byte-identical in style to the pre-existing
`WolfmedAmputationTest.cs`; `WOLFMED_MANIFEST.md` contains `0d0a` throughout (CRLF). The report's claim is
correct.)*

## 2. Upstream discipline

`git diff HEAD --stat` (path-limited to `Content.Shared Content.Server Content.Client Resources
Content.IntegrationTests`, `RobustToolbox` never touched, no `cd` into the worktree) lists 16 files. This is
the **cumulative** phase-5 diff since the phase-4 commit `2b4a4675d0` (no commits between packages, per
DECISIONS.md's phase-5 execution rule) — not just this WP's own edits.

Isolated WP13-6's own contribution by diffing the current tree's file list against
`WP13-5.patch`'s (the snapshot taken immediately before this package started):

- **Exactly one file newly appears**: `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAnalyzerTest.cs`
  (extended, +58 lines / +1 `using`).
- **Every other tracked file** in the current diff is present, byte-identical in its diff hunk, in
  `WP13-5.patch` — confirmed directly for `Resources/Prototypes/_Onyx/Wounds/wounds.yml` (the most
  sensitive vendored file) and `Docs/Wolfmed/DECISIONS.md` (`diff` of the two hunks: identical). The
  manifest (`Docs/Wolfmed/WOLFMED_MANIFEST.md`) is also present in both, but with a new appended section —
  see below.
- **New untracked files**: exactly the 3 new test files
  (`WolfmedSpeciesProfileTest.cs`/`SpeciesSpawnTest.cs`/`TreatmentMatrixTest.cs`) plus the two pre-existing
  `_WF` prototype files from WP13-0/WP13-1 (`species_parts.yml`, `containers.yml`), which are unrelated to
  this WP and were already untracked in `WP13-5.untracked.txt`.

**Conclusion: the report's central claim — "No production file touched" — is independently confirmed.**
WP13-6 touches exactly 4 files: 3 new test files (`.cs`) and 1 extended test file, plus its manifest
section. No `_Onyx`, `_WF` production file, no upstream prototype/code file changed since WP13-5.

D2 exposure: nil (no production code path changed).

## 3. Vendoring fidelity

**Not applicable to this WP's own changes** — WP13-6 copies no Onyx prototype block (confirmed above:
`Resources/Prototypes/_Onyx/Wounds/wounds.yml` is byte-identical to its WP13-5 state). The test files
derive their expected literals from the *shipped* Wolfgate prototypes (`wounds.yml`, `species_parts.yml`),
not from a fresh Onyx copy, which is correct for a tests-only package — spot-checked several derivations
against the actual files:

- `IpcMechanicalDamageWound`'s `Cold`/`Caustic` reopen values and the `InorganicWolfmed` container's
  `supportedGroups`/`supportedTypes` — code comments in `IpcTakesColdAndCausticTest` match
  `Resources/Prototypes/Damage/groups.yml`'s `Burn = {Heat, Shock, Cold, Caustic}`.
- The WP13-6-1 finding (the load-bearing correction this package makes) was independently re-derived from
  source, not taken on the report's word:
  - `Content.Shared/_WF/Wolfmed/Compat/WolfmedDamageableSystem.cs:143-171` (`SetDamage`): writes
    `dict[type] = amount` for every key in the incoming spec, unconditionally — confirmed by reading the
    method in full.
  - `Content.Shared/Damage/Systems/DamageableSystem.cs:266-283` (`TryChangeDamage`'s delta loop):
    `if (!dict.TryGetValue(type, out var oldValue)) continue;` — confirmed this is the container-membership
    filter the finding describes, and confirmed it is **not** on `SetDamage`'s path.
  - `Content.Shared/_Onyx/Wounds/WoundDamageProjectionSystem.cs:156-199` (`RefreshBodyDamage`): sums every
    part's positive damage with no container-membership check at all, then calls `_damage.SetDamage(body,
    total)` — confirmed this is the exact call site the finding names.
  - The finding is technically sound: the mob container's key set gates direct damage dealt to the mob
    (`TryChangeDamage`), not what the wound-host body-total projection writes.

## 4. Collisions

- **Zero new prototype ids.** Grepped the three brand-new test files for `[TestPrototypes]` `id:` lines:
  none. Grepped the whole `Tests/_WF/Wolfmed/` directory for duplicate ids across all files (old and new):
  every id appears exactly once. Matches the report.
- **Zero new production C# files.** `find … -name "*.cs" -newer WP13-5.patch` (excluding
  `RobustToolbox`/`bin`/`obj`) returns exactly the 4 touched test files and nothing else.
- **No `Destructible`/damage-container change this WP** — none to audit for inheritance. (The
  `CyberneticLimbGibCeilingTest` and `IpcLimbSeverabilityAndGibCeilingTest` tests *read* the already-shipped
  `PartIPCBase`/`CyberneticPartBase` `Destructible` blocks from earlier WPs; they do not add or change any
  threshold. Spot-checked the read assertions against the live component: `IpcLimbSeverabilityAndGibCeilingTest`
  asserts Slash 210/Blunt 190 survive-vs-delete at 140/195, and `CyberneticLimbGibCeilingTest` asserts the
  `DestructibleComponent.Thresholds` collection on a spawned `JawsOfLifeLeftArm` contains exactly two
  `DamageTypeTrigger`s (Blunt 190, Slash 210, no Heat) — both pass, both match PROTO O(c)/P(c) as documented
  in PLAN5 §3.2, and both were exercised live in the WP13-6-tests.log run, not just asserted against static
  YAML.)

## 5. Manifest

`Docs/Wolfmed/WOLFMED_MANIFEST.md` gains one appended `### WP13-6` section (confirmed via clean diff-of-diffs
against `WP13-5.patch`'s manifest hunk — 70 new lines, nothing altered upstream of the insertion point) with
a table row for each of the 4 touched test files, plus the deviation/finding prose (WP13-6-1..5) and the
build/test checkpoint summary. Every file this WP touches has a manifest row; no row is fabricated (all 4
paths verified to exist on disk with the stated test counts — see §7 below for exact per-test pass
confirmation).

## 6. Plan conformance

**PLAN5 §4 WP13-6 file table: exact match.** All 4 listed files present with the listed test IDs:

| File | Plan-listed tests | Present in file |
|---|---|---|
| `WolfmedSpeciesProfileTest.cs` (new) | T-P5-1, 4, 5, 6, 7, 8, 13/21, 19 | `IpcPartRoutesBleedsAndFeelsPainTest`, `IpcTakesColdAndCausticTest` (T-P5-21), `CyberneticLimbBleedsHalfFeelsNoPainAndFramefracturesTest`, `CyberneticFrameFractureIsMendableBySurgeryTest`, `CyberneticLimbGibCeilingTest`, `SlimePartRoutesBleedsSoonerAndNeverFracturesTest`, `SlimeBleedsFifteenPercentFasterTest`, `DionaPartScarsAndIsNeverSeverableTest` — 8 tests, matches |
| `WolfmedSpeciesSpawnTest.cs` (new) | T-P5-2, 3, 9, 10, 11, 12, 14, 20 | `IpcIsAWoundHostWithTheIpcProfileTest`, `IpcLeaksOilAndTakesBloodlossTest`, `WelderStillRepairsAnIpcTest`, `IpcLimbSeverabilityAndGibCeilingTest`, `IpcDoesNotGibFromRoutineLimbDamageTest`, `ProtogenIsAWoundHostTest`, `DionaLimbIsDestroyedNotSeveredTest`, `EveryBodyPartProfileUsesThePrimaryStreamTest` — 8 tests, matches |
| `WolfmedTreatmentMatrixTest.cs` (new) | T-P5-15, 16, 17 | `TreatmentCapabilityMatrixTest`, `CableCoilNoLongerHealsOrganicsButHealsMechanicalTest`, `CableCoilRadiationStillHealsSystemicallyTest` — 3 tests, matches |
| `WolfmedAnalyzerTest.cs` (extend) | T-P5-18 | `MechanicalWoundDiagnosticTextResolvesTest` — 1 test, matches |

**T-P5-13 vs T-P5-21:** PLAN5 states these are alternatives and exactly one ships. Confirmed only
`T-P5-21` (`IpcTakesColdAndCausticTest`) exists in the tree; `T-P5-13`
(`IpcAndCyberneticIgnoreColdAndCausticTest`) is absent, correctly reflecting U13′(b) having shipped in
WP13-1.

**DECISIONS.md §8.4 answers relevant to this WP** (the U-decisions that determine which alternative a
decision-dependent test asserts): all correctly reflected in the shipped test bodies, checked by reading
each test's assertions against the actual decision:

- **U13′(b)** ("add `InorganicWolfmed`"): `IpcTakesColdAndCausticTest` asserts `Cold`/`Caustic` land on both
  the IPC and cybernetic part's `DamageableComponent`, container id `InorganicWolfmed` on both. Matches.
- **U4(a)** ("lift protogen exclusion"): `ProtogenIsAWoundHostTest` asserts `WoundHostComponent` present on
  both `MobProtogen` and `MobProtogenRandom`, every part on `OrganicBodyPartProfile`, and (P5-D19/U12′) no
  organ carries `OrganDamageComponent` — a stated gap, not silently dropped. Matches.
- **U3′(b)** ("190/210 gib parity"): `IpcLimbSeverabilityAndGibCeilingTest` asserts Slash 140 survives
  (rung 210) and Severable true (threshold 130); Blunt 195 deletes (rung 190 < threshold 250). Matches.
- **U15(a)** ("marked `Destructible` on `CyberneticPartBase`, no Heat rung"): `CyberneticLimbGibCeilingTest`
  asserts exactly 2 triggers (Blunt 190, Slash 210), no Heat key, and that Heat 260 does not delete the
  limb. Matches.
- **U2(a)** ("`SiliconWolfmed` container + PROTO S/T"): `IpcLeaksOilAndTakesBloodlossTest` and
  `WelderStillRepairsAnIpcTest` both assert this path is live (Bloodloss lands, welder/nanite
  `DamageContainers` list contains the IPC's actual container). Matches.
- **U17(a)** ("accept diona/slime destroyed-not-severed and say so"): `DionaLimbIsDestroyedNotSeveredTest`
  and `DionaPartScarsAndIsNeverSeverableTest` both assert this directly. Matches.
- **U5(a)** ("fix the cable coil now"): `CableCoilNoLongerHealsOrganicsButHealsMechanicalTest` asserts the
  nerf (organic refused) and the compensating gain (IPC/cybernetic heal) in one test. Matches.
- **Pain numbness closed (Group B, numbness.md):** no pain-numbness test written or implied anywhere in the
  4 files. Matches (P5-D14 — closed, not deferred).

No relevant DECISIONS.md §8.4 answer is contradicted or silently unaddressed.

## 7. Tests

All logs read directly, not taken on the report's word.

- **`WP13-6-docktest.log`**: `DockTest` run first and alone — `Total tests: 3 / Passed: 3`.
- **`WP13-6-tests.log`** (Wolfmed + `_Onyx.Wounds`/`_Onyx.Medical` filter): `Total tests: 116 / Passed: 116`,
  **0** `Failed` lines anywhere in the file. Every one of the 20 new/extended tests located by name and
  confirmed `Passed` (not merely present):
  `MechanicalWoundDiagnosticTextResolvesTest`, `CyberneticFrameFractureIsMendableBySurgeryTest`,
  `CyberneticLimbBleedsHalfFeelsNoPainAndFramefracturesTest`, `CyberneticLimbGibCeilingTest`,
  `DionaLimbIsDestroyedNotSeveredTest`, `DionaPartScarsAndIsNeverSeverableTest`,
  `EveryBodyPartProfileUsesThePrimaryStreamTest`, `IpcPartRoutesBleedsAndFeelsPainTest`,
  `IpcDoesNotGibFromRoutineLimbDamageTest`, `IpcIsAWoundHostWithTheIpcProfileTest`,
  `IpcTakesColdAndCausticTest`, `SlimeBleedsFifteenPercentFasterTest`, `IpcLeaksOilAndTakesBloodlossTest`,
  `SlimePartRoutesBleedsSoonerAndNeverFracturesTest`, `IpcLimbSeverabilityAndGibCeilingTest`,
  `ProtogenIsAWoundHostTest`, `WelderStillRepairsAnIpcTest`, `CableCoilRadiationStillHealsSystemicallyTest`,
  `CableCoilNoLongerHealsOrganicsButHealsMechanicalTest`, `TreatmentCapabilityMatrixTest` — all `Passed`.
- **`WP13-6-smoke.log`**: `Total tests: 11 / Passed: 9 / Skipped: 2`. Both skips are pre-existing
  `[Ignore]` attributes in `Content.IntegrationTests/Tests/EntityTest.cs`
  (`SpawnAndDirtyAllEntities`, `SpawnAndDeleteEntityCountTest`) — confirmed these two ignore attributes
  exist in the file at the report's cited lines and pre-date this WP. No failure anywhere.

**No test failure to report.** The report's own account of one red run during development
(`IpcTakesColdAndCausticTest`, resolved as the test's premise being wrong, not the code — finding
WP13-6-1) is consistent with `WP13-6-new.log` (27 passed, 1 red on the first full run of the four files)
and does not appear in any of the three green logs checked above.

## 8. Snapshot

Written:
- `C:/tmp/wolfmed-plan/p5/snapshots/WP13-6.patch` (1646 lines) —
  `git diff HEAD -- Content.Shared Content.Server Content.Client Resources Docs Content.IntegrationTests`.
- `C:/tmp/wolfmed-plan/p5/snapshots/WP13-6.untracked.txt` (5 lines) —
  `git ls-files --others --exclude-standard` over the same paths: the 3 new test files plus the two
  pre-existing untracked `_WF` prototype files from WP13-0/WP13-1 (unrelated to this WP, present in every
  snapshot since WP13-0).

## Verdict

**PASS.** Both builds clean (0 errors each). Headless server clean for the full run (0
`[ERRO]`/`[FATL]`/exception lines). Upstream discipline holds absolutely — independently confirmed WP13-6
changed exactly 4 test-tree files and nothing under `_Onyx`/`_WF`/upstream; D2 exposure nil. No vendoring to
check (no Onyx block copied this WP), but the one load-bearing technical correction the package makes
(WP13-6-1, the `SetDamage`/`TryChangeDamage` container-filter asymmetry) was independently re-derived from
the actual source and holds up. Zero new prototype ids, zero new production `.cs` files, no duplicate
`[TestPrototypes]` ids. Manifest carries a row for every touched file. PLAN5 §4's WP13-6 file/test table
matches exactly, including the T-P5-13/T-P5-21 alternative selection. Every DECISIONS.md §8.4 answer
relevant to a decision-dependent test (U13′, U4, U3′, U15, U2, U17, U5) is correctly reflected in the
shipped assertions. All 20 new tests pass; the full 116-test Wolfmed suite passes; DockTest and smoke are
clean. Snapshot written.
