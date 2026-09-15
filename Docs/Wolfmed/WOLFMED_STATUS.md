# Wolfmed status

Phases 1, 2, 3, 4 and 5 of the Space Onyx wound port are implemented, build, and pass their tests. Nothing is committed.

- **Branch / worktree:** `clanker/wolfmed-port-orchestration-454c3d` in `.claude/worktrees/rules-motd-updates-11c89c`.
- **Onyx pin:** `2f5bab9946539cbe083010c9ae6fbc59b47ae377`. Reference sparse checkout at `C:\tmp\onyx` (recreate with the clone command in `WOLFMED_HANDOFF.md`, using `core.longpaths=true` and a short path).
- **Documents:** `DECISIONS.md` (D1–D35 plus the phase-2, phase-3, phase-4 and phase-5 sections), `WOLFMED_PLAN.md` (phase 1's file-level plan), `WOLFMED_PLAN2.md` (phase 2's), `WOLFMED_PLAN3.md` (phase 3's), `WOLFMED_PLAN4.md` (phase 4's), `WOLFMED_PLAN5.md` (phase 5's), `WOLFMED_MANIFEST.md` (every file: Onyx path, Wolfgate path, status, deviations, including the phase-2 §8.2, phase-3 §8.6, phase-4 §8.4 and phase-5 §8.4 user-decision summaries), `reports/analysis` (phase 1), `reports/analysis/phase2` (phase 2's five analyst reports plus `CRITIQUE2.md`), `reports/analysis/phase3` (phase 3's five analyst reports plus `CRITIQUE3.md`), `reports/analysis/phase4` (phase 4's five analyst reports plus `CRITIQUE4.md`) and `reports/analysis/phase5` (phase 5's five analyst reports plus `CRITIQUE5.md`), `reports/work-packages` (phase 1), `reports/work-packages/phase2` (one report and one verification per WP10-N package), `reports/work-packages/phase3` (one report and one verification per WP11-N package), `reports/work-packages/phase4` (one report and one verification per WP12-N package) and `reports/work-packages/phase5` (one report and one verification per WP13-N package).
- **Reports:** the per-phase analyst, critique, work-package and verification reports were removed from the repo on 2026-09-14 to slim the PR; they live locally at `C:SERSJZO12DOCUMENTSWOLFMEDPLANeports` (same layout: `analysis/`, `analysis/phaseN/`, `work-packages/`, `work-packages/phaseN/`). Manifest rows that cite `Docs/Wolfmed/reports/...` refer to that local copy.

## What phase 1 delivers

- `StatusEffectNew` framework vendored at the upstream path (11 of 13 files byte-identical).
- Wound core in `Content.Shared/_Onyx/Wounds` (wound prototypes, `WoundSystem`, damage routing, projection, scars, wound status effects, pain, fractures, part functionality) and the server half in `Content.Server/_Onyx/Wounds` (bleeding, internal bleeding, organ damage, healing) plus a trimmed `CirculatoryStreamSystem`.
- Compat layer in `Content.Shared/_WF/Wolfmed/Compat`: `WolfmedDamageableSystem` (new-style damage API on a distinct name so it cannot bind to the legacy overload), `DamageDealtEvent` seam, `AlertsSystem.UpdateAlert`, body/stun/chat shims, `WoundTargetResolver`, `WolfmedBodyPartComponent`, part lifecycle bridge, part armour, wound-host exclusion.
- Damage bridge: for entities with `WoundHost`, Onyx routing owns limb damage; Shitmed spreading, sever-at-130 and part regen are skipped by component-gated `// WOLFGATE` guards. Armour applies exactly once. `TryChangeDamage` still returns the applied delta on the cancelled pass, so hitscan pierce, melee stamina and hit logs keep working.
- Prototypes: `wounds.yml`, wound status effects, alerts, textures (all CC-BY-SA-3.0), locale. `WoundHost` lands on `BaseMobSpeciesOrganic`; protogen is excluded at runtime; PassiveDamage is neutralised on wound hosts; the gib threshold is raised to 1500 on organics.
- Tests: `Content.IntegrationTests/Tests/_Onyx/Wounds` (6 ported) and `Tests/_WF/Wolfmed/WolfmedDamageBridgeTest.cs`. Last run: 30 passed, 0 failed; spawn-all-entities, prototype-save and dock smoke tests pass.

## What phase 2 delivers

Phase 2 (WP10-1 through WP10-6b, PLAN2's WP10 + WP9's handed-forward gates) makes fractures, pain and the
part-status examine live on real mobs for the first time. What a player now sees:

- **A fracture alert and its effects.** Breaking a limb hard enough (a Comminuted hit, `creationChance: 1`)
  shows the `BrokenBones` alert and — tracked by grade and treatment — slows movement (a Comminuted leg:
  walk ×0.5) and slows manipulation do-afters (a Comminuted arm: ×2.0, using the corrected balance below).
  Mending or detaching the limb clears both the alert and the penalty.
- **A pain overlay.** The existing brute/burn vignette is now driven by wound pain on wound hosts
  (`min(1, GetPain / SoftPainCap)`, floored below a small threshold) instead of raw projected damage, and a
  pain-numb character (the `PainNumbness` trait) is correctly exempted. At high pain (raw pain ≥ 130) a mob
  is paralysed for a few seconds, forced to scream, jitters, and gets a temporary reduced pain sensitivity
  window — pain shock's first real run on a mob in this fork.
- **Pain sounds.** Wound hosts now emote (e.g. `Scream`, `Crying`) as their total damage crosses configured
  thresholds — a Space Onyx feature that never actually worked at the pinned commit (see Deviations below)
  and now does in Wolfgate.
- **Examine shows part-by-part injury status and pain**, replacing the old body-level threshold text
  ("they don't look hurt" / "they look pale") for wound hosts specifically — non-wound-hosts (borgs,
  silicons, animals, the excluded Protogen) keep today's behaviour unchanged.
- **A `HighPainThreshold` trait** (25% less pain gain from wounds), mutually exclusive with `PainNumbness`.

Test counts: **39 of 39 passed** (`Content.IntegrationTests/Tests/_Onyx/Wounds` + `Tests/_WF/Wolfmed`,
`--filter "FullyQualifiedName~_Onyx.Wounds|FullyQualifiedName~Wolfmed"`), per `C:\tmp\wolfmed-plan\p2\wp\WP10-6b-tests.log`
— up from phase 1's 30. New in phase 2: `FractureAlertTracksGradeAndTreatmentTest`,
`FractureAlertRespectsMinimumGradeTest`, `FractureManipulationUsesHeldHandSymmetryTest`, the ported
`EffectsRefreshOnTreatmentHealingAndDetachTest`, `PainOverlayLevelTracksPainTest`,
`PainShockStunsAtThresholdTest`, `HighPainThresholdReducesWoundPainGainTest`,
`PainNumbnessSuppressesWoundPainTest`, plus WP10-6a's `ArmorPenetrationReachesWoundHostsTest` and the two
`PassiveDamage` canaries (T-PASSIVE-A/B, gating D29).

## What phase 3 delivers

Phase 3 (WP11-0 through WP11-5, `PLAN3.md`) is the body-integrity phase: limbs can now be severed, organs
can fail, armour protects per limb instead of the whole body, and severed limbs show their wounds. What a
player now sees:

- **Amputation, by melee, guns and lasers.** A limb (or the head) that accumulates enough damage across the
  damage types its `amputationThresholds` names becomes `Severable`; the *next* hit that also meets that
  type's `dismembermentFinishingDamage` severs it, dropping it as a live, re-attachable entity and leaving a
  `DismembermentWound` + `AmputationConsequenceWound` on the parent stump (the latter is a marker only — it
  does not yet block re-attachment, phase 4). **User decision (DECISIONS.md §8.6-1): guns and lasers can
  sever**, deviating from Onyx's melee-only defaults. Piercing's finishing minimum was lowered from Onyx's
  40 to **12**, and a `Heat` amputation-threshold row was added per part (equal to that part's Piercing
  threshold — Head/Hand 200, Arm/Leg 250, Foot 220) with a Heat finishing minimum of **15**. Measured
  outcome: a hand is severed by the **16th** consecutive 14-Piercing round or the **14th** consecutive
  16-Heat laser shot; 5 bullets into a foot (70/220) leave it attached. Amputation thresholds themselves
  stay at Onyx values, so gunfire still needs many hits before a finishing shot; melee is unchanged (a
  machete needs 6 × Slash 32 on an arm). A lost vital part (head or torso) now charges its own total damage
  as systemic Bloodloss on wound hosts (§8.6-3, P3-D1) instead of the flat −100 HardLight/Shitmed decapitation
  used to read as; non-wound-hosts are provably untouched.
- **Organ damage, live for the first time.** Brain, eyes, heart, lungs, liver, stomach and kidneys (human
  and human-lineage species only — §8.6-7) now take damage from hits to their containing part and fail at
  0 HP: heart failure delays death 60s and blocks defib, brain destruction kills outright (without deleting
  the organ), eye destruction causes temporary blindness, lung loss causes suffocation. **Organ damage ships
  irreversible** (§8.6-4, Onyx defaults) — nothing in Wolfgate heals an organ yet. Balance: each hit caps at
  `MaxHealth × 0.3` organ HP, so exactly 4 solid hits destroy any organ regardless of size; per-hit odds are
  low enough (0.95%–4.0% depending on organ and part) that this is a shift-long ratchet, not a gunfight
  event — roughly 166 (lungs) to 421 (kidneys) torso hits, or ~100 head hits for the brain, expected to
  destroy one.
- **Locational armour.** Coverage-aware armour (`ArmorComponent.Coverage`/`CoverageSymmetry`/`PartModifiers`)
  now exists and is content-annotated on the five `_Mono` bulletproof vests (`coverage: [Torso, Arm, Leg]`)
  and the one `_Mono` light ballistic helmet (`coverage: [Head]`) — **user decision, §8.6-2**. Every other
  piece of armour in the game (272 of 273 `- type: Armor` blocks, `ClothingHeadHelmetSwat` included) is
  unaffected and keeps protecting the whole body. **The aimed-headshot change:** for the five annotated
  vests, aimed torso protection is unchanged, but aimed **head** damage against a vest-only wearer rises
  **2.8 → 11.2** (×4) for a 14-Piercing round, because the vest stops covering the head while the
  unannotated helmet keeps covering the torso. Aimed hand/foot shots now bypass an annotated vest entirely.
  A heavy vest's average *unaimed* Piercing mitigation falls from 75.0% to 52.9%. Combined with amputation:
  a vest at ≤0.46 Slash now lets a 32-Slash machete finish a covered limb it previously could not.
- **Limb wound sprites and severed-limb art.** Per-limb brute/burn damage sprites now render on the attached
  body instead of one aggregate overlay (Option A), and — because the package stayed green — **severed
  limbs render their own wounds too** (Option B, §8.6-5, 156 texture files ported from Onyx,
  CC-BY-SA-3.0/Ubaser, same licence already shipped in WG's own effects). Hand and foot wounds are folded
  onto the arm/leg reading rather than shipped invisible (§8.6-8, P3-D25) — Wolfgate has no hand/foot damage
  art, same as Onyx.

Test counts: **65 of 65 passed** (`Content.IntegrationTests/Tests/_Onyx/Wounds` + `Tests/_Onyx/Body` +
`Tests/_WF/Wolfmed`, `--filter "FullyQualifiedName~_Onyx.Wounds|FullyQualifiedName~_Onyx.Body|FullyQualifiedName~Wolfmed"`),
per `C:\tmp\wolfmed-plan\p3\wp\WP11-5-report-tests.log` — up from phase 2's 39. New in phase 3: T-REATTACH,
T-VISUALS (WP11-0); the vendored `AmputationSystem` subscription plus T-AMP-VITAL/T-AMP-GUN/T-AMP-OVERFLOW/
T-AMP-EXPLOSION/T-AMP-CONSEQUENCE-SEPARATE and 3 of Onyx's `AmputationConsequenceTest`s (WP11-1/WP11-5);
7 organ tests T-ORG-DATA/CAP/DESTROY/HEART/BRAIN/EYES/FUNC/INERT (WP11-2/WP11-5); 5 locational-armour tests
including the Wolfgate-only `PartModifiersRouteThroughArmorPenetrationTest` (WP11-3); the restored
`TraumaticAmputationCreatesSevereStumpBleedingTest` (WP11-5).

**Gaps closed in phase 4** (struck from the phase-3 list above, WP12-10 reconcile): organ damage now has a
healing path (`SurgeryHeal<Organ>` via `OrganHealthSystem.ChangeHealth`, reversible while the organ lives —
P4-D24); the health analyzer now reports wounds, fractures, bleeding, organs, chemicals and vital damage
(WP12-6/7); `AmputationConsequenceWound` now blocks re-attachment — through the surgery layer (HOOK 25,
P4-D18), **not** `SharedBodySystem.CanAttachPart`, which stays deliberately ungated so Mono's prosthetic
traits and the Goob autosurgeon are untouched — and the affected attach surgeries are **hidden, not greyed**:
six of them (Head, both arms, both legs, Hands) for an untreated torso stump, one (that limb's hand or foot)
for an arm or leg stump. Explosion amputation landed (§8.6-6 reversed, see below). The pre-existing
`GibbingSystem.cs:141` container-mutation crash was fixed before phase 4 started (two `.ToArray()` snapshots,
marked `// WOLFGATE`, manifest row added by WP12-10).

**Gaps still open, carried to phase 5+:** pain numbness / narcotics (`ModifyStatusEffect`) is skipped and
recorded (P4-D8) — the cure for a destroyed organ remains `SurgeryInsert<Organ>` (no organ is repaired once
`Health` reaches 0, only while it is dying); the cable coil is the first `treatmentCapabilities` annotation
due in phase 5 (P4-D7), the only item in the tree where the omission is a real gap rather than a restated
default.

## What phase 4 delivers

Phase 4 (WP12-0 through WP12-10, `PLAN4.md`) is the treatment phase: everything phases 1–3 inflict, a medic
can now undo. What a medic can do that they could not before, per treatment:

- **Stop bleeding fast with a tourniquet.** Wolfgate's existing `Tourniquet` item (id unchanged) now stops
  bleeding on one selected limb entirely while applied, in exchange for that limb's use — a real trade instead
  of the old flat `Healing` stopgap. Works on any wound host; the excluded Protogen loses tourniquet function
  entirely (P4-D11), same as every other wound mechanic it sits outside of.
- **Field-craft a medical patch.** A new craftable item (1 Cloth or 4 WebSilk, 5 s do-after) that sticks to a
  target and transfers its solution over time — zero placement in loot/vending/cargo, mirroring Onyx exactly
  (P4-D13).
- **A four-rung painkiller ladder plus a dedicated fracture medicine.** Ibuprofen (0.5 suppression) → Ketorolac
  (0.9) → Tramadol (1.25) → Oxycodone (2.0), each decaying over its own duration; Cognac, Bicaridine and
  Desoxyephedrine also carry a lesser suppression alongside their existing uses. Stasizium — already in every
  combat medkit — now mends fractures in place (extended, not renamed: P4-D3).
- **Treat wounds surgically, on Shitmed's own step system.** Six new wound surgeries reachable through the
  existing surgery BUI: stop external bleeding (clamp), tend a deep brute/burn wound (a severity window keeps
  the two shallow tend surgeries from also listing on a badly wounded limb, P4-D19), stop internal bleeding,
  mend a fracture in two steps (BoneSetter reduces, then BoneGel mends — a Hairline fracture is reachable only
  at the Mended step, corrected from a naive read of the completion check, P4-D20), heal the amputation
  consequence on a stump (clearing it for re-attachment), and heal a damaged organ (7 human organs, including
  the fork's first kidney surgery, `SurgeryHealKidneys`).
- **Surgery now hurts and can scar.** Every new wound surgery step inflicts pain (`WolfmedSurgeryPainEffect`,
  Onyx's amounts) — mechanisms (`PainSystem`, `WoundScarSystem` + `surgery.scar_chance`) that were fully built
  and unreachable since phases 1–2. Opening a surgical incision now also opens a real, clampable
  `SurgicalIncisionWound` beside the existing flat bloodloss cost (the four-prototype scar chain, P4-D21); a
  cleanly closed incision may leave a permanent medical scar.
- **Read a real diagnostics readout.** The health analyzer gained a self-contained parallel panel
  (`WolfmedDiagnosticPanel`, mounted by three marked lines — P4-D25) reporting per-part wounds, fracture grade
  and treatment, bleeding and its clotting phase, pain, organ health/function, and relevant blood chemistry —
  none of which the stock analyzer showed for a wound host before.
- **Explosions now spread across limbs and can sever them.** The distributed-damage mechanic that has been
  fully ported and unreachable since phase 1 (`TryApplyDistributedDamage`, `PickExplosionAmputationCandidate`)
  is live (HOOK 22, P4-D14) — a blast can pick a random limb and, if damage is high enough, amputate it. The
  same change closes the phase-3 armour-plate hole for explosions: a plated wound host absorbs blast damage on
  the distributed path for the first time (T-EXPLOSION-PLATE: 0 damage plated, 40 unplated for the same
  40-Blunt blast).
- **A rewritten guidebook.** Two new entries, `Wounds` and `Wound treatment`, inserted into the existing
  Medical guide (PROTO L) and written for what Wolfgate actually shipped — organic species only, guns and
  lasers can finish an amputation, fracture effects described qualitatively, the painkiller ladder named, and
  the amputation-consequence stump behaviour explained in plain terms.

Test counts: **98 of 98 passed, 0 skipped** (`Content.IntegrationTests/Tests/_Onyx/Wounds` +
`Tests/_Onyx/Body` + `Tests/_Onyx/Medical` + `Tests/_WF/Wolfmed`,
`--filter "FullyQualifiedName~_Onyx.Wounds|FullyQualifiedName~_Onyx.Body|FullyQualifiedName~_Onyx.Medical|FullyQualifiedName~Wolfmed"`),
per `C:\tmp\wolfmed-plan\p4\wp\WP12-9-report-tests.log` — up from phase 3's 65 (33 new: 31 new test methods
plus 2 phase-1/3 skips restored, `TourniquetStopsOnlySelectedPartTest` and
`SurgicalHealRemovesConsequenceAndUnblocksTest`). Smoke filter `EntityTest|PrototypeSaveTest|DockTest`:
**9 passed, 0 failed**, the 2 skips being the two permanently `[Ignore]`d upstream tests
(`WP12-9-report-smoke.log`). `DockTest` run first every package per project memory: 3/3 each time. Headless
server (120 s) reached `Server Version 277.0.0.0 -> Ready` with zero `[ERRO]`/`[FATL]`/exception lines on
every phase-4 package that touched a prototype or locale file, including this reconcile package's guidebook
addition (`WP12-10-report-server.log`).

## What phase 5 delivers

Phase 5 (WP13-0 through WP13-6, `PLAN5.md`) is species coverage: it adds **zero new C# types, zero new
components, and zero new `SubscribeLocalEvent` pairs** — everything is 16 appended wound prototypes, 4 new
`_WF` part abstracts, 2 new damage containers, and 8 marked one- or two-line upstream edits. It also lifts
the protogen exclusion and closes the one balance leak DECISIONS.md flagged for it, the cable coil.

Per-species player experience, as shipped (PLAN5 §8.1, corrected against what WP13-0..WP13-6 measured on
real spawned mobs — WP13-6-1's correction on `Cold`/`Caustic` folded in):

| | changes? | bleeds | pain | scars | bone / frame fracture | limb loss | passive + bed heal | treated by | organ damage |
|---|---|---|---|---|---|---|---|---|---|
| **Human & the other 23 organic species** | only the cable coil | Blood ×1.0 | yes | yes | bone | severed by Slash 130 / Piercing 250 / Heat 250 on an arm; destroyed outright at Blunt 190 / Slash 210, burned to Ash at Heat 250 (pure-Blunt severing stays unreachable, a pre-existing phase-3 defect, not phase 5's) | yes | Biological — every medicine, topical, gauze, tourniquet, surgery — **minus the cable coil** | yes, human lineage only |
| **Diona** | yes, big | Sap ×1.0, from the first scratch | yes | yes — the only non-organic profile that scars | none — no `BrokenBones` alert, no `SurgeryMendFracture`, no fracture penalty ever | never cleanly severed (`amputationThresholds: {}`), but still destroyed outright at Blunt 190 / Slash 210 / Ash at Heat 250 — no thrown limb, no stump, no reattachment | yes | Biological (unchanged) | none — diona organs carry no `OrganDamage` (pre-existing) |
| **Slime** | yes, big | Slime ×1.15, from the first scratch | slightly less from blunt (0.7 vs 0.87); burns identical | never | none | same as diona: unseverable, still destroyed at 190/210/250 | yes, organic schedule | Biological (unchanged) | none |
| **IPC** | yes — joins the whole medical system at once | Oil ×1.0, every chassis wound leaks (chance 1, no minimum), `flammability: 2` — the trail can be set on fire; and it really does move `MobIPC`'s body total (`Bloodloss`) and `SlowOnDamage`'s bands, not just the part | yes, plus pain shock (2 s paralyze + forced scream + jitter + 30 s adrenaline) — and **no chemical relief exists at all** (no metabolizer); also drunk + stuttering below 90 % fluid, same as any bleeding organic | never | none | severable on the organic arm numbers (Slash 130 / Piercing 250 / Heat 250); gib ceiling raised 110/150 → 190/210, no Heat/Ash rung — an IPC limb never burns to ash. IPC hands/feet end up tougher than flesh, the IPC head destructible where a human head is not | never (`passiveRecoveryMultiplier: 0`, `bedRecoveryMultiplier: 0`) | welder, nanite applicator, cable coil, tourniquet, every wound surgery. **No medicine, no brute pack, no ointment, no gauze**. `Cold`/`Caustic` restored as live anti-robot damage (`InorganicWolfmed` container) | none |
| **Cybernetic limb on an organic body** | yes | the wearer's own blood, ×0.5, only from `Dismemberment`/`SurgicalIncision` — mechanical wounds carry no bleed behaviour | none — the limb is numb, `PainComponent` actively removed | never | frame fracture (microcrack → cracked → deformed → shattered), same thresholds, same `BrokenBones` alert, mendable by `SurgeryMendFracture` | severable on the organic arm numbers; own `Destructible` at Blunt 190 / Slash 210, **no Heat/Ash rung** (removes the old steel-prosthetic-burns-to-Ash bug) | never | cable coil (and the welder, only if the body is an IPC); `Cold`/`Caustic` restored. No medicine | n/a |
| **Protogen** | yes — exclusion lifted | Blood ×1.0 | yes, including pain shock and `EmoteOnDamage` screams | yes | bone | organic | yes | everything an organic gets — gains the tourniquet, the analyzer panel, the pain overlay and every wound surgery it silently lacked before | none — `BaseProtogenOrgan`-derived organs carry no `OrganDamage`, so no internal bleeding and none of the seven organ surgeries (recorded gap, U12′) |
| **Skeleton, borgs, monkeys, chimera, diona nymphs, every NPC** | no change | — | — | — | — | — | — | — | — |

**The one nerf, stated plainly:** the cable coil has been healing organic Heat/Shock damage since phase 4's
HOOK 8 landed — `-3.0` Heat and `-3.0` Shock per 0.6 s, faster than any other healing item in the tree
(Ointment/Brutepack are 2 s), on an item every engineer already carries. It was never intended — the item's
own comment says it is for "Estacao Pirata IPCs" and relied on `damageContainers: [Silicon]`, which HOOK 8
bypasses for wound hosts. Phase 5 gives `CableStack`'s `Healing` component `treatmentCapabilities:
[Electrical]` (PROTO U, one line): humans lose the cable-coil burn heal, IPC and cybernetic parts keep it.
**Changelog-worthy — a deliberate balance fix (DECISIONS.md U5), not a bugfix-only note.**

Test counts: **116 of 116 passed, 0 skipped** (`Content.IntegrationTests/Tests/_Onyx/Wounds` +
`Tests/_Onyx/Medical` + `Tests/_WF/Wolfmed`, `--filter
"FullyQualifiedName~_Onyx.Wounds|FullyQualifiedName~_Onyx.Medical|FullyQualifiedName~Wolfmed"`), per
`C:\tmp\wolfmed-plan\p5\wp\WP13-6-tests.log` — 20 new tests across 4 files (3 new, 1 extended), all
Wolfgate-authored (no Onyx phase-5 test source): `WolfmedSpeciesProfileTest.cs` (8, per-profile behaviour),
`WolfmedSpeciesSpawnTest.cs` (8, whole-mob spawn/delete/damage), `WolfmedTreatmentMatrixTest.cs` (3, the
treatment-capability matrix and the cable-coil nerf), and one test added to `WolfmedAnalyzerTest.cs`
(mechanical wound diagnostic text resolves through locale, not a raw key). No new prototype ids, no new
C# types, components or subscriptions were needed to reach 116/116; D2 exposure from the test suite itself
is nil. Smoke filter `EntityTest|PrototypeSaveTest|DockTest`: **9 passed, 2 skipped** out of 11, both skips
the same permanently `[Ignore]`d upstream tests carried since phase 1 (`WP13-6-smoke.log`); `DockTest` run
first and alone, 3/3, so no environmental `db.ef` masking. Headless server (120 s, port 1299) reached
`Server Version 277.0.0.0 -> Ready` with zero `[ERRO]`/`[FATL]`/exception lines on every phase-5 package
that touched a prototype or locale file (`WP13-6-report-server.log`).

## Upstream footprint

**55 tracked upstream files** carry `// WOLFGATE` hooks (see the manifest) — 16 shipped in phase 1, 6 more
in phase 2, 5 more in phase 3, 20 more in phase 4, and **8 more in phase 5**: `Resources/Prototypes/Body/Parts/{slime,diona}.yml`
(one `parent:` line each), `Resources/Prototypes/_EinsteinEngines/Body/Parts/ipc.yml` (parent line + the two
gib numbers + U13′(b) container), `Resources/Prototypes/_Shitmed/Body/Parts/cybernetic.yml` (parent line +
U13′(b) container + U15(a) `Destructible`), `Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/ipc.yml`
(PROTO Q — `WoundHost`, `PainShockTarget`, `Bloodstream`, `Damageable`, `Destructible` raise),
`Resources/Prototypes/Entities/Objects/Tools/welders.yml` and
`Resources/Prototypes/_Mono/Entities/Objects/Tools/nanite_applicator.yml` (PROTO S/T, mandatory companions
to the IPC damage-container change), and `Resources/Prototypes/Entities/Objects/Tools/cable_coils.yml` (PROTO
U, the one-line cable-coil nerf). `_Mono/Entities/Mobs/Species/protogen.yml` was already tracked since phase
1 (a documentation-only comment) and gains no new row; `_EinsteinEngines/Entities/Mobs/Player/silicon_base.yml`
is confirmed **not** touched (U18, PROTO R dropped as a provable no-op). Re-measured via `git diff
2b4a4675d0 --name-only -- Content.Shared Content.Server Content.Client Resources Content.IntegrationTests`,
filtered to files outside `_Onyx`/`_WF`/`Content.IntegrationTests`: exactly these 8, matching PLAN5 §3.5's
prediction precisely. Phase 5 also registers **zero** new `SubscribeLocalEvent` pairs — the first Wolfmed
phase with an empty subscription-audit row.

**Phase 1–4 hooks (unchanged; see prior sections above):** 47 tracked upstream files — 16 shipped in phase 1,
6 more in phase 2, 5 more in phase 3, and **20 more in phase 4** (measured via
`git diff 6329d204e3 --name-only`, filtered to files outside `_Onyx`/`_WF`/`Content.IntegrationTests`):
`Content.Client/HealthAnalyzer/UI/HealthAnalyzerWindow.xaml(.cs)` (HOOK 26),
`Content.Server/EntityEffects/Effects/{HealthChange,EvenHealthChange}.cs` (HOOK 9),
`Content.Server/Explosion/EntitySystems/ExplosionSystem.Processing.cs` (HOOK 22),
`Content.Server/Medical/HealthAnalyzerSystem.cs` (HOOK 23), `Content.Shared/Gibbing/Systems/GibbingSystem.cs`
(the pre-phase-4 gibbing fix), `Content.Shared/MedicalScanner/HealthAnalyzerScannedUserMessage.cs` (4
nullable fields), `Content.Shared/_Shitmed/Surgery/{SharedSurgerySystem.cs, Conditions/SurgeryWoundedConditionComponent.cs}`
(HOOK 24/25 + EXT 1), `Resources/Locale/en-US/medical/components/health-analyzer-component.ftl`,
`Resources/Prototypes/Catalog/Fills/Items/firstaidkits.yml` (withdrawn edit, comment only),
`Resources/Prototypes/Entities/Objects/Specific/Medical/healing.yml` (PROTO D, `Tourniquet` in place),
`Resources/Prototypes/Guidebook/medical.yml` (PROTO L, 2 lines), `Resources/Prototypes/Reagents/Consumable/Drink/alcohol.yml`
(PROTO K), `Resources/Prototypes/Reagents/{medicine,narcotics}.yml` (PROTO I/J),
`Resources/Prototypes/_Goobstation/Reagents/medicine.yml` (PROTO H, Stasizium), `Resources/Prototypes/_Shitmed/Entities/Surgery/{surgeries,surgery_steps}.yml`
(PROTO F severity window, PROTO G the four-site scar chain). This measured count of 20 is 2 files above
PLAN4 §3.4's predicted "27 → 45" — the two extras are `Resources/Prototypes/Guidebook/medical.yml` (PROTO L,
this WP) and `Content.Shared/Gibbing/Systems/GibbingSystem.cs` (the pre-phase-4 fix), both of which PLAN4's
running total accounted for elsewhere; no untracked upstream edit exists beyond this list.

**Phase 1–3 hooks (unchanged):** `Content.Client/Damage/DamageVisualsSystem.cs` (HOOK 20),
`Content.Shared/Body/Part/BodyPartComponent.cs` (HOOK 21, a single word), `Resources/Prototypes/Body/Organs/human.yml`
(PROTO A, 7 one-line `parent:` edits), `Resources/Prototypes/_Mono/Entities/Clothing/Head/Helmets/bulletproof_helmets.yml`
and `.../OuterClothing/Armor/bulletproof_vests.yml` (PROTO B, 6 `coverage:` lines total) — plus one more phase-3
block in an already-hooked prototype (`Resources/Prototypes/Entities/Mobs/Species/base.yml`, PROTO C's 2
`sprite:` retargets). Phase 2's 6 were `Content.Shared/HealthExaminable/HealthExaminableSystem.cs` (GUARD F),
`Content.Client/Examine/ExamineSystem.cs` (HOOK 14), `Content.Client/UserInterface/Systems/DamageOverlays/Overlays/DamageOverlay.cs`
(HOOK 15), `Content.Client/UserInterface/Systems/DamageOverlays/DamageOverlayUiController.cs` (HOOK 16),
`Content.Server/Chat/EmoteOnDamageComponent.cs` (HOOK 17), `Content.Server/Chat/Systems/EmoteOnDamageSystem.cs`
(HOOK 18). The phase-1 16 were the `DamageableSystem` seam, the Shitmed targeting guards, the healing and
armour hooks, and the species base prototype block. Every phase-3 hook is one line or one word except PROTO
A/B, which are one-line-per-entry data edits — no upstream file gained a new subscription pair without a §5
audit (phase 3 audited and registered zero new upstream-side pairs; its two new subscriptions,
`<WoundableComponent, PartDamageOverflowedEvent>` and `<BodyPartComponent, AfterAutoHandleStateEvent>`, are
both owned by vendored/hook-body files, not by editing an upstream `Initialize()` a second time).

## Bugs found and fixed during the port (re-check on every Onyx re-sync)

- `PainSystem` used `new ModifyPainGainEvent()`, which zeroes the struct's `Multiplier`; all pain was multiplied by zero. Onyx's source is identical, so this is an upstream Onyx bug.
- Wolfgate has no Nubody `BodyInventorySlotSystem`, so part add/remove had to be bridged (`WolfmedBodyPartLifecycleSystem`) with terminating-entity guards, and bleeding part-lifecycle entry points needed a caller.
- Four Onyx test literals disagree with Onyx's own pinned prototypes (fracture grade thresholds, bleeding minimum severity, reopen severity, healing multiplier); the ported tests assert the prototype values.
- **(Phase 2) `OrganicFractureProfile.manipulationModifier` was inverted relative to Onyx's own formula** —
  values below 1 made a shattered arm's do-afters *faster*. Corrected to the C# defaults (user decision,
  DECISIONS.md §8.2-1).
- **(Phase 2) Onyx's `EmoteOnDamage` pain sounds were dead at the pin** — `species_base.yml` writes the YAML
  key `emotes:` for a field whose serialized name is `emotesThreshold`; RT silently drops the unknown key.
  Corrected (user decision, DECISIONS.md §8.2-3).
- **(Phase 2) `PainSystem.IsPainNumb` was permanently false** — it only recognised Onyx's status-effect form
  of pain numbness, which nothing in Wolfgate can apply; Wolfgate's actual `PainNumbness` trait grants a
  different, legacy component. Widened to recognise both.
- **(Phase 3) The manifest's WP6-era `OrganDamageSystem.cs` note named the wrong line numbers** for the D26
  amputation comment-outs (`:24`/`:36` instead of the real `:25-26`/`:38-39`) — corrected in the WP11-6
  reconcile, no behaviour change.
- **(Phase 3) `WolfmedBodyPartComponent.MaxDamage` was documented as the amputation gate; it is not.**
  `MaxDamage` only gates `AmputationSystem`'s overflow branch (dead for every organic limb, in Onyx too);
  `amputationThresholds` is what gates severing, and it was already populated for all ten limb abstracts
  since WP7 — no YAML gap ever existed (P3-D12). Corrected in the manifest, not a code change.
- **(Phase 3, fixed before phase 4) Pre-existing upstream bug:** `GibbingSystem.cs:141`'s `Drop`/`Gib`
  branches enumerate a container while removing from it (`InvalidOperationException`), reachable whenever a
  body part holding contents crosses a `Destructible` gib threshold. Flagged in phase 3, fixed ahead of
  phase 4 with two `.ToArray()` snapshots + `using System.Linq`, all marked `// WOLFGATE`; manifest row added
  by WP12-10.
- **(Phase 4) `TakeStaminaDamage` honours `Immediate` where Onyx's own effect never reads it** — a
  corrected-upstream-bug deviation (P4-D5), keeping Onyx's `false` default since Wolfgate's
  `StaminaSystem.TakeStaminaDamage(..., immediate: true)` default silently refuses to apply a negative amount
  to an already-critical target.
- **(Phase 4) `WoundFractureSystem.CanTreat`'s `Reduced` gate requires at least a Simple fracture grade** —
  a naive "complete when `Treatment == Reduced`" surgery check would stall forever on the commonest (Hairline)
  fracture. `WolfmedSurgeryMendFractureEffect`'s completion check was written to also complete when `CanTreat`
  can never succeed (P4-D20), not fixed by editing the vendored gate.
- **(Phase 4) A test runner false-positive, not a Wolfmed bug:** a dirty-disposed integration-test pair can
  report a genuinely failing test as `Skipped` while the run summary still reads `Test Run Successful`.
  Recorded so a future phase-4+ log is read for `Skipped:` as carefully as for `Failed:`.

## Known deviations and balance flags

See `WOLFMED_PLAN.md` §8.2, `WOLFMED_PLAN2.md` §8.2, `WOLFMED_PLAN3.md` §8, `WOLFMED_PLAN4.md` §8,
`WOLFMED_PLAN5.md` §7.3/§8, and the manifest's Deviations section (including the consolidated phase-2 §8.2,
phase-3 §8.6, phase-4 §8.4 and phase-5 §8.4 user-decisions summaries). Notable for playtest: limb damage versus armour changes (armour now applies
once), environmental damage creates limb wounds, do-afters no longer interrupt on wound hosts, wound-host
damage is unpredicted (transient client mispredict), pain stun re-triggers stun VFX per call, a fractured
arm's do-after penalty is lost if the do-after itself opts out of `MultiplyDelay`, the client never mirrors a
server-side limb attach/detach movement-speed refresh (corrects within one network state), and
**`WoundPrototype.HealingMultiplier` is still 1 for every wound** — a balance-pass item kept on record since
phase 1. Phase-3-specific: **guns and lasers can now sever limbs** (§8.6-1, a deliberate deviation from
Onyx's melee-only defaults, expressed only in `_WF/Wolfmed/Body/parts.yml`); **the five `_Mono` vests + one
BP helmet quadruple aimed-headshot damage for their wearer** (§8.6-2); Onyx's `MaskComponent.IsToggled`
armour gate and its `traumaDeductions` field are not ported (P3-D20); the two Onyx locational-armour tests
that are red against Onyx's own shipped code were rewritten against the corrected
(first-match-wins-then-coverage-fallback) behaviour instead of skipped.

**Phase-4-specific:** **organ heal ships at `amount: 3` per step, not Onyx's `amount: 1`** (§8.4-2, P4-D23) —
~10 s per organ instead of ~30 s, the one deliberate pace deviation from D4; organ damage is now **reversible
only while the organ lives** (P4-D24) — a destroyed organ (`Health <= 0`) is still unrecoverable, and
`SurgeryRemoveKidneys`/`InsertKidneys` still do not exist. **Explosions now sever limbs** (§8.4-1, P4-D14,
reversing the phase-3 record), tunable by `explosion.damage_variation`/`explosion.wounding_multiplier`.
**Opening a surgical incision on a wound host costs more than it did** on `SurgeryStepOpenIncisionScalpel`
only — the existing flat 10 Bloodloss stays *and* a severity-10 `SurgicalIncisionWound` opens beside it
(deviation 9, §7.3 of `WOLFMED_PLAN4.md`); `SurgeryStepCarefulIncisionScalpel` is unchanged. **Surgery now
hurts and can scar** (P4-D21/D22) — both mechanisms existed and were unreachable since phases 1–2.
**Non-wound-hosts (Protogen only) lose tourniquet function entirely**, and the tourniquet's begin/end sounds
lose client-side prediction, degrading to server-fired PVS audio (P4-D10/D11). **`SalicylicAcid` is dropped
outright, not ported or renamed** (P4-D4) — WG's is an unrelated Frontier precursor reagent; 20 of Onyx's 22
medicine reagents are not ported (Tier B/C and the virology set, P4-D2). **Pain numbness / narcotics
(`ModifyStatusEffect`) is skipped** (§8.4-8, P4-D8) — `PainNumbnessStatusEffectComponent` remains dead code
with two readers and no writer; re-entry cost is roughly half a day (~45 LOC, a new `_WF` action enum member,
2 prototypes, 2 locale keys, 1 test). **No `treatmentCapabilities:` annotation was added to any existing
item or reagent** (§8.4-7, P4-D7) — the cable coil is recorded as the first phase-5 action. **No organ
examine line was added** (§8.4-6). **`GroupHealSpecifier` is not ported** (P4-D12) — it carries a second
licence (Wega, GPL-3.0) under Onyx's AGPL and is relevant only if Vampire is ever ported.
**`AmputationConsequenceWound` now blocks re-attachment through the surgery layer, not `CanAttachPart`**
(P4-D18) — a prosthetic can still be bolted to an untreated stump via the non-surgery `AttachPart` callers
(two Mono traits, the Goob autosurgeon, Shitmed's child-part generation), which is deliberate: those callers
are not surgery and should not be blocked by a surgery-shaped gate.

**Phase-5-specific:** **the cable coil no longer heals organic Heat/Shock damage** — closed as a deliberate
balance fix, not a bugfix (U5, §8.2 above). **IPC limb gib triggers raised 110/150 → 190/210** to match
every organic arm/leg's own `MajorLimb` trigger (U3′(b)) — accepted asymmetries: IPC hands/feet end up
tougher than organic ones, the IPC head is destructible where an organic head has no gib trigger at all.
**Cybernetic limbs gain their own `Destructible` at Blunt 190 / Slash 210 with no Heat rung** (U15(a)) —
removes the pre-existing bug where a steel prosthetic burned to `Ash` at Heat 250 with a flesh sound. **IPC
oil loss now really damages the IPC** (`SiliconWolfmed` container, `Bloodloss` as a type, not the `Airloss`
group — U2(a)); PROTO S/T keep the welder and nanite applicator working on it. **`Cold`/`Caustic` restored
as live damage on IPC and cybernetic parts** (`InorganicWolfmed` container, U13′(b)) — acid and cryogenics
become working anti-robot tools again, and WP13-6 measured that this is a bigger deal than WP13-2 first
recorded: the projection path (`WolfmedDamageableSystem.SetDamage`) always wrote `Cold`/`Caustic` to the
mob's own damage total regardless of container support, so the "asymmetry" WP13-2 flagged for the balance
pass does not exist and is struck (WP13-6-1). **Protogen is now a wound host** (D32 exclusion lifted, U4) —
it gains the tourniquet, the analyzer panel, the pain overlay and every wound surgery it silently lacked;
its organs carry no `OrganDamage` (recorded gap, U12′). **Diona and slime limbs are destroyed, not severed**
past 190/210 Blunt/Slash — `amputationThresholds: {}` disables only `AmputationSystem`, the inherited gib
trigger still deletes the limb, so no thrown limb, stump wound or reattachment exists for them (U17,
pre-existing pattern, phase 5 is the first document to state it). **Pain numbness / narcotics is closed
permanently, not deferred** (U8, P5-D14) — `PainNumbnessStatusEffectComponent` stays dead code. **Skeleton
(`MobSkeletonPerson`) is recorded as a known exclusion** (U10, P5-D18) — mechanically ready but "bone
fracture"/"bleeding" on an undead skeleton needs its own design, never previously documented.

## Next phases (not started)

1. **Phase 6: predicted routing** (wound-host damage is still unpredicted, D35); **the full 272-entry
   locational-armour content pass** (P3-D6); **`HurtCommand` part argument** (patch kept at
   `reports/work-packages` as `WP8-hurtcommand-deferred.patch` in `C:\tmp\wolfmed-plan\wp`). Explosion
   amputation and the `GibbingSystem.cs` container-mutation fix, both previously slated for phase 6, shipped
   in phase 4 instead and are struck from this list. **The phase-1 "blocked on a shared stage-based
   metabolizer" line above this one is struck outright, not carried to phase 6**: PLAN5 §8.7 found it
   factually void at the pin — none of Onyx's five `bodyPartProfile`s selects a non-primary circulatory
   stream, and Onyx's own `MetabolismStagePrototype`/`SolutionManagerComponent`/stage-based
   `MetabolizerComponent` chain has zero live consumers even in Onyx itself. If a second fluid on one body
   is ever wanted, it is unused upstream capability to port fresh, not phase-5/6 debt.
2. **Explicitly deferred, not phase 6 — carried to the balance pass instead** (PLAN5 §8.7): re-tuning
   `MajorLimb`/`MinorLimb` and the pure-Blunt severing contradiction (R11, pre-existing since phase 3);
   organ-damage instrumentation for non-human species (U12′) and skeleton (U10); the mechanical examine
   adjective set (U9, only the analyzer/examine strings shipped); `allowedWoundStages` balance mirroring
   (U6); a welder `Healing` block for cybernetic limbs (U7); diona/slime limb triggers of their own (U17).
   **Pain numbness / narcotics (P5-D14) is closed, not deferred** — it is not on this list for a future
   phase to pick up; re-entry would need a fresh decision, not a resumption.

## How to verify

```bash
dotnet build Content.Server/Content.Server.csproj -c DebugOpt
dotnet build Content.Client/Content.Client.csproj -c DebugOpt
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt --filter "FullyQualifiedName~_Onyx.Wounds|FullyQualifiedName~_Onyx.Body|FullyQualifiedName~_Onyx.Medical|FullyQualifiedName~Wolfmed"
dotnet run --project Content.YAMLLinter -c Release
```

A worktree needs `RobustToolbox` junctioned from the main checkout before building: `cmd /c rmdir RobustToolbox` then `cmd /c mklink /J RobustToolbox <main>\RobustToolbox`; remove the junction with `cmd /c rmdir RobustToolbox` afterwards.
