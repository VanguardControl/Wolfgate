# Wolfmed orchestrator decisions (2026-09-12)

These are constraints for every agent. Do not re-litigate them; flag concrete problems with evidence.

- **Onyx source pinned:** commit `2f5bab9946539cbe083010c9ae6fbc59b47ae377` (Space-Onyx/space-onyx-14 master, 2026-09-13). Reference sparse checkout at `C:\Users\jzo12\Documents\Wolfmed\onyx`. Only paths listed in the sparse set exist; if a path is absent say so, never guess file contents.
- **Wolfgate worktree:** `C:\Users\jzo12\Documents\GitHub\Wolfgate\.claude\worktrees\rules-motd-updates-11c89c` (branch `clanker/wolfmed-port-orchestration-454c3d`). RobustToolbox 277.0.0 is junctioned in. Baseline builds with 0 errors.
- **D1 StatusEffectNew:** port it verbatim from Onyx's copy at the upstream path `Content.Shared/StatusEffectNew` (plus any client/server parts and the prototypes/locale it needs). Treat as vendored upstream code; edits marked `// WOLFGATE`. Rationale: keeps `_Onyx` wound files verbatim; it is upstream Wizden code that Monolith may inherit later. The old `Content.Shared.StatusEffect` system stays and keeps serving existing content.
- **D2 Damage bridge:** for entities with `WoundHostComponent`, Onyx `WoundDamageRoutingSystem` owns part damage. Shitmed's in-`DamageableSystem` spreading, sever-at-130 and part regen are bypassed for those entities via minimal `// WOLFGATE` guards. Entities without `WoundHostComponent` behave exactly as today. Everything is gated on Onyx's `CCVars.Wounds` plus component presence.
- **D3 Phase 1 species:** organic humanoids only (the Human body and any species sharing organic parts). IPC/cybernetic/slime/plant profiles are later phases.
- **D4 Balance:** Onyx defaults. Tuning later via CCVars and prototypes.
- **D5 Missing APIs** (verified absent in Wolfgate): `StatusEffectNew`, new-style `DamageableSystem` API (`Entity<DamageableComponent?>` overloads, `ChangeDamage`, `HealEvenly`, `HealDistributed`, `GetTotalDamage`...), `EntityEffectSystem<T>` ECS entity effects (Wolfgate has the old class-based `EntityEffect`), `AlertsSystem.UpdateAlert`, `DamageSpecifier.ArmorPenetration`. Strategy: a compat layer in `Content.Shared/_WF/Wolfmed/Compat` (extension methods, adapter classes) wherever a shim keeps a vendored file verbatim. Where a shim is impossible, a `// WOLFGATE` edit inside the vendored file. Upstream Wolfgate systems only get one- or two-line `// WOLFGATE` hooks.
- **D6 Layout:** vendored Onyx code in `Content.{Shared,Server,Client}/_Onyx/...`, `Resources/Prototypes/_Onyx/...`, `Resources/Locale/en-US/_Onyx/...`, `Resources/Textures/_Onyx/...` keeping Onyx's relative paths. Wolfgate glue in `_WF/Wolfmed`. Docs and manifest in `Docs/Wolfmed/`.
- **D7 Surgery:** phase 1 keeps Wolfgate's Shitmed surgery. Onyx's own surgery system (`_Onyx/Medical/Surgery/SharedSurgerySystem.*`, `_Onyx/Surgery`) is NOT ported. Wound surgeries are re-expressed on Shitmed's step system later (phase 4).
- **D8 Body:** stay on Wolfgate's Shitmed `BodyPartComponent`. Onyx's extra part fields go in a separate `_WF/Wolfmed` component. Onyx `_Onyx/Body` Nubody glue is not ported; only organ-damage and functional-organ pieces the wounds need.
- **Style:** `_WF` files: no license header, `/// <summary>` one-liners, `[Dependency] private X _x = default!;` without readonly. Vendored `_Onyx` files keep Onyx's headers verbatim.
- **Testing:** headless integration tests only. Port Onyx's wound tests into `Content.IntegrationTests/Tests/_Onyx/Wounds`.

## Orchestrator calls on PLAN.md §8.1 (2026-09-13) — reversible, flagged to the user

- **D32 Species (§8.1 item 1):** `- type: WoundHost` on `BaseMobSpeciesOrganic`. Diona and slime ship on the Organic profile (Onyx-consistent) until phase 5. **Protogen is excluded** (synthetic: remove `WoundHost` in its own prototype with a `# WOLFGATE` comment). Re-enumerate descendants at implementation time and list every exclusion in the manifest.
- **D33 PassiveDamage (§8.1 item 2):** accept D29 — body-level `PassiveDamage` neutralised on wound hosts; Onyx per-profile recovery is the only passive heal. Recorded as a balance deviation.
- **D34 Do-afters (§8.1 item 5):** accept the loss of damage-interrupts-do-after on wound hosts for phase 1.
- **D35 Prediction (§8.1 item 6):** accept unpredicted wound-host damage for phase 1 (transient mispredict). Predicting routing is a later phase.
- **No commits.** Work packages leave the tree uncommitted; the verify stage snapshots a patch per WP under `C:/Users/jzo12/Documents/Wolfmed/plan/snapshots/`. The user commits.

## Phase 2 (2026-09-13) — scope and constraints

Phase 1 is committed (`23c0a74cb9 initial commit of port`). Phase 2 = PLAN.md WP10 plus WP9's handed-forward gates. Same rules: vendored Onyx files in `_Onyx` marked `// WOLFGATE`, glue in `_WF/Wolfmed`, upstream hooks one or two lines (bodies in `_WF` partials), no directed subscription without checking existing subscribers, no commits, RobustToolbox untouched.

- **P2-1 Scope:** `FractureEffectsSystem`, `FractureAlertSystem`, fracture/pain/shock alerts and their textures, `MovementModStatusEffectComponent` + trimmed `MovementModStatusSystem` and the `StatusEffectSlowdown` chain if wounds need it, `_Onyx/StatusEffects/wounds.yml` only if a consumer exists (Onyx's two entries are orphaned at the pin; skip if so), the client pain damage overlay, `EmoteOnDamage` pain sounds, GUARD E2 + `_Onyx/HealthExaminable` (examine part status and pain), `HighPainThreshold` trait + `PainNumbness` status effect, `MobStandStatusEffectBase` with a Wolfgate `KnockdownImmune` tag only if something in scope needs it.
- **P2-2 Tests:** port `WoundFractureTest.EffectsRefreshOnTreatmentHealingAndDetachTest`; write T-AP (armour penetration survives routing) and T-PASSIVE (D29: no passive heal on wound hosts) from PLAN.md §6.2; add a fracture-alert and pain-alert assertion.
- **P2-3 `wounds.body_part_functionality_enabled` stays false.** Fracture movement/hand multipliers apply through `FractureEffectsSystem` regardless; Shitmed's `Enabled` thresholds stay the only limb-disable mechanism.
- **P2-4 Pain is live for the first time** (PainSystem multiplier fix). Balance defaults stay Onyx's, but every number the pain HUD shows must be verified against a real mob in a test, not assumed from Onyx's tests.
- **P2-5 Docs:** phase-2 plan at `Docs/Wolfmed/WOLFMED_PLAN2.md`; manifest and status doc updated in the same work.

## Phase 2 — answers to PLAN2.md §8.2 (2026-09-13)

- **§8.2-1 Fracture manipulation (user decision):** FIX. Set the four `manipulationModifier` values to the C# defaults `1.1 / 1.25 / 1.5 / 2.0` in the ported YAML behind a `# WOLFGATE` balance comment; a fractured arm slows hand work. Record as a corrected-upstream-bug deviation.
- **§8.2-2 Do-after `Used`:** zero-edit active-hand approximation.
- **§8.2-3 Pain sounds (user decision):** PORT with the YAML key corrected (`emotesThreshold`); recorded as a corrected upstream bug (Onyx's never bound).
- **§8.2-4/5/6 Hooks:** GUARD F + HOOK 14, HOOK 15 + 16, HOOK 17 + 18 are authorised, with bodies in `_WF` partials so each upstream site stays one or two marked lines.
- **§8.2-7 `PartDamageVisualsComponent`:** defer to phase 3; note in the manifest.
- **§8.2-8 Part status readout:** wound hosts only (P2-D20).
- **§8.2-9 `HealingMultiplier`:** leave for the balance pass; keep on record.
- **Execution note:** packages run SEQUENTIALLY in the one worktree (concurrent builds collide), so each package appends its rows directly to `Docs/Wolfmed/WOLFMED_MANIFEST.md`; WP10-7 reconciles rather than merges. `base.yml` still has one owner (WP10-5).

## Phase 3 (2026-09-13) — scope and constraints

Phase 2 is committed (`1171e02fb6 phase 2`). Phase 3 = the deferred body-integrity pieces. Same rules as phases 1–2 (vendored `_Onyx` files marked `// WOLFGATE`, glue in `_WF/Wolfmed`, upstream hooks one or two lines with bodies in `_WF` partials, subscription audit, no commits, RobustToolbox untouched, sequential packages, manifest appended directly).

- **P3-1 Amputation:** port `AmputationSystem.cs` (D26 lifted): traumatic amputation from overflow + finishing hits, explosion chance, thrown limb, `AmputationConsequence` wound on the parent, `Severable`, the `WolfmedBodySystem.TryDetachPart` shim already exists. Re-enable `OrganDamageSystem`'s amputation dependency and call. Reconcile with Shitmed: GUARD B already suppresses sever-at-130 for wound hosts; Shitmed's `DropPart`/`PartRemoveDamage` cascade must not double-apply with Onyx's consequence wound. `WolfmedBodyPartComponent` gets the amputation fields (`AmputationThresholds`, `DismembermentFinishingDamage`, `AmputationConsequenceSeverity`, `DismembermentSeverity`) populated for human parts.
- **P3-2 Organ consequences:** what Onyx does when organs take damage (`OrganDamageSystem` caps, `OrganConsequenceComponents`, `FunctionalOrganComponent`, server `OrganEffectSystem` if it is wound-driven) mapped onto Wolfgate's organs (Shitmed organ prototypes, Mono pump organ for IPCs is phase 5). Only pieces with a consumer at the pin.
- **P3-3 Per-part armour:** Onyx's locational armour (`ArmorComponent` coverage / coverageSymmetry / partModifiers) on top of phase-1's `WolfmedPartArmorSystem`; the three locational-armour tests. Wolfgate is gun PvP: this is the piece that makes helmets and vests matter per limb.
- **P3-4 Limb damage sprites:** the `PartDamageVisualsComponent` consumer (Onyx `Content.Client/Damage/DamageVisualsSystem.cs` changes + `_Onyx/Wounds/{brute,burn}_damage.rsi`, already copied in WP7 — verify) so wounds show on the body sprite.
- **P3-5 Tests:** `TraumaticAmputationCreatesSevereStumpBleedingTest`, the three locational-armour tests, a surgery-attach assertion (a reattached limb gets `Woundable` and rejoins bleeding — PLAN.md §8.3 trap 2), an organ-damage consequence assertion, a limb-visuals state assertion if testable headlessly. `RepairSelectionAndSnapshotValidationTest` only if `_Onyx.Repairable` is cheap; otherwise skip and record.
- **P3-6 Balance:** Onyx defaults (D4). Amputation thresholds and finishing damage must be reported as numbers in the plan so the user can sanity-check them against Wolfgate gun damage before playtest.
- **P3-7 Docs:** `Docs/Wolfmed/WOLFMED_PLAN3.md`, manifest and status doc updated in the same work.

## Phase 3 — answers to PLAN3.md §8.6 (2026-09-13)

- **§8.6-1 Amputation by guns/lasers (user decision): YES, guns and lasers can sever.** Deviation from Onyx defaults, expressed ONLY in Wolfgate's own part YAML (the `_WF/Wolfmed` WolfmedBodyPart data), marked `# WOLFGATE (P3 balance)`: (a) Piercing finishing minimum lowered from 40 to **12** so a standard 14-Piercing round can finish an over-threshold limb; (b) **Heat** thresholds added per part equal to that part's Piercing threshold (Head 200, Arm 250, Hand 200, Leg 250, Foot 220) with a Heat finishing minimum of **15** so a 16-Heat laser can finish. Amputation thresholds themselves stay at Onyx values, so gunfire still needs many hits before a finishing shot. The implementing agent must verify how `AmputationSystem` compares accumulated part damage to the threshold (per damage type vs total) and document it; if Heat needs a wound-type mapping (Burn wounds) to count, add it in the same YAML. Replace T-AMP-NOGUN with **T-AMP-GUN** (a limb over threshold is severed by one 14-Piercing hit and by one 16-Heat hit; a below-threshold limb is not) plus a melee case. Record in the manifest as a Wolfgate balance deviation; the balance pass may retune.
- **§8.6-2 Armour (user decision): annotate the 5 vests + the ballistic helmet** (PROTO B as planned). Record the aimed-headshot change in the status doc.
- **§8.6-3:** take P3-D1 (host-gated vital-part Bloodloss charge in `WolfmedBodyPartLifecycleSystem.OnPartRemoved`; no HOOK 19).
- **§8.6-4:** organ damage ships irreversible with Onyx caps; recorded.
- **§8.6-5:** Option B (severed-limb art) in WP11-4 if the package is otherwise green.
- **§8.6-6:** explosion amputation stays out.
- **§8.6-7:** organ damage human-lineage only.
- **§8.6-8:** hand/foot wound visuals fold onto arm/leg (P3-D25).
- **Execution:** packages sequential in the one worktree; append manifest rows directly; one owner per shared YAML file as PLAN3 assigns.

## Phase 4 (2026-09-13) — scope and constraints

Phase 3 is committed (`6329d204e3 Phase 3 completion`). The `GibbingSystem.cs` container-mutation crash is fixed with a marked `.ToArray()` (two loops) before phase 4 starts. Phase 4 = treatment and diagnostics: everything that lets a medic fix what phases 1–3 inflict. Same rules as before (vendored `_Onyx` files marked `// WOLFGATE`, glue in `_WF/Wolfmed`, upstream hooks one or two lines with bodies in `_WF` partials, subscription audit, D2 for non-wound-hosts, sequential packages, manifest appended directly, no commits, RobustToolbox untouched).

- **P4-1 Reagent treatments (D16):** old-style `EntityEffect` classes in `Content.Shared/_WF/Wolfmed/EntityEffects` (or Server if the base is server-only) keeping Onyx's `!type:` names: `SuppressPain`, `MendFractures`, `TakeStaminaDamage`, `StaminaDamageCondition`, `TreatmentCapabilities` datafield + HOOK 9 on `HealthChange` and `EvenHealthChange` (and `DistributedHealthChange` only if a ported reagent uses it). Onyx wound reagents (`_Onyx/Reagents/Medicine/*.yml`) ported with id collisions resolved: `Stasizium` and `SalicylicAcid` already exist in Wolfgate with different definitions — **rename Onyx's to `OnyxStasizium`/`OnyxSalicylicAcid`** unless the analysts find Wolfgate's versions are functionally equivalent, in which case extend Wolfgate's (marked). Existing Wolfgate topicals/medicines (brute pack, ointment, bicaridine, dermaline, etc.) gain `treatmentCapabilities` so they treat wounds; propose the mapping.
- **P4-2 Tourniquet** (`_Onyx/Medical/Tourniquet`, server-side per D13) and **medical patch** (`Content.Server/_Onyx/Medical/MedicalPatch*`), with prototypes, sprites (license-checked), locale, and cargo/loadout/medkit placement (`_WF/Wolfmed` fills, minimal marked edits to existing medkit fills).
- **P4-3 Wound surgeries on Shitmed steps (D7):** `SurgeryStopBleeding`, `SurgeryTendWoundsBruteDeep`/`BurnDeep`, `SurgeryStopInternalBleeding`, `SurgeryMendFracture` (BoneGel/BoneSetter drive reduction → mended), `SurgeryHealAmputationConsequence` (and make `AmputationConsequenceWound` block `CanAttachPart` until treated — the phase-3 P3-D2 gap), `SurgeryHeal<Organ>` via `OrganHealthSystem.ChangeHealth` (the phase-3 organ-healing gap). Reuse Wolfgate's `SurgeryOpenIncision`/`SurgeryCloseIncision`; **extend** Wolfgate's `SurgeryTendWoundsEffectComponent`/`SurgeryWoundedConditionComponent` (marked) rather than vendoring Onyx's colliding components. Onyx's own `SharedSurgerySystem.*` is NOT ported.
- **P4-4 Diagnostics:** health analyzer wound/fracture/bleeding/organ/pain readout (`HealthAnalyzerWoundDiagnostic`, `HealthAnalyzerOrganInfo`, `HealthAnalyzerChemicalInfo`, Onyx's `HealthAnalyzerSystem` hooks) on Wolfgate's Shitmed analyzer. The UI shape (graft onto Shitmed's monolithic analyzer window vs a parallel Wolfmed panel/tab) is a user decision the plan must present with a recommendation. Examine gains organ-damage status only if Onyx has it.
- **P4-5 Pain numbness / narcotics:** `StatusEffectPainNumbness` chain and the `ModifyStatusEffect` entity effect (deferred from phase 2) if the ported reagents need them; otherwise record as skipped.
- **P4-6 Explosion amputation:** the `ExplosionSystem` hook (one or two lines, body in `_WF`) with the Mono `DamageOriginFlag.Explosion` plate regression test, if the analysts confirm the hook is small; otherwise stays phase 6.
- **P4-7 Guidebook:** Onyx's wounds guidebook entry (`_Onyx/guidebook/wounds.ftl` + prototype) adapted to what Wolfgate actually shipped (no IPC/slime yet, guns can sever, corrected fracture multipliers).
- **P4-8 Tests:** tourniquet (`TourniquetStopsOnlySelectedPartTest`), medical patch, each wound surgery step (`WoundSurgeryTest`/`WoundSurgeryScarTest` from Onyx adapted to Shitmed steps), reagent treatment (a topical with `treatmentCapabilities` heals a wound, one without does not; `SuppressPain`), organ healing, analyzer message contents, reattachment blocked until consequence treated. `HealthAnalyzerPartDamageTest` from Onyx if portable.
- **P4-9 Docs:** `Docs/Wolfmed/WOLFMED_PLAN4.md`, manifest, status doc.

## Phase 4 — answers to PLAN4.md §8.4 (2026-09-13)

All defaults taken (orchestrator; user pre-authorised more lethality in phase 3):
- **§8.4-1 Explosion amputation:** SHIP (WP12-8), gated on T-EXPLOSION-PLATE + T-EXPLOSION-WRAPPER; tunable via Onyx's explosion CVars.
- **§8.4-2 Organ heal rate:** `amount: 3`.
- **§8.4-3 Surgery scarring / incision bleeding:** ship the four-prototype chain as revised (careful incision carries no bleed effect; `SurgeryStepSealTendWound` closes).
- **§8.4-4 Reagents:** Tier A; Tier B only if WP12-2 is green with time to spare.
- **§8.4-5 Analyzer UI:** PARALLEL — self-contained `_WF` panel mounted by marked lines; Shitmed's window geometry untouched.
- **§8.4-6:** no organ examine line. **§8.4-7:** no `treatmentCapabilities` annotation on existing items now (cable coil first in phase 5). **§8.4-8:** pain numbness skipped and recorded.
- **Gibbing fix:** `Content.Shared/Gibbing/Systems/GibbingSystem.cs` two `.ToArray()` snapshots + `using System.Linq`, all marked `// WOLFGATE`; add a manifest row (WP12-10).
- **Execution:** sequential packages; manifest appended directly; one owner per shared YAML/XAML file as PLAN4 assigns.

## Phase 5 (2026-09-13) — scope and constraints

Phase 4 is committed (`2b4a4675d0 phase 4`). Phase 5 = species coverage and the deferred treatment/pain items. Same rules as before (vendored `_Onyx` files marked `// WOLFGATE`, glue in `_WF/Wolfmed`, upstream hooks one or two lines with bodies in `_WF` partials, subscription and name-collision audits, D2, sequential packages, manifest appended directly, no commits, RobustToolbox untouched).

- **P5-1 Species profiles:** Onyx's `Ipc`, `Cybernetic`, `Slime` and `Plant` body-part profiles, fracture profiles and species wound sets (`wounds.yml` species sections: mechanical damage, `CyberneticFrameFracture`, slime and plant variants) mapped onto Wolfgate's species. Wolfgate targets: IPC (Mono's IPC with pump organ), protogen (currently excluded from `WoundHost` — decide whether it becomes a cybernetic wound host), slime, diona, and cybernetic limb replacements (`_Shitmed/Cybernetics`, Onyx `_Onyx/Cybernetics`). The plan must state for each species what changes for the player (bleeding oil/coolant? no bleeding? no pain? scars?).
- **P5-2 Circulatory streams for non-organic species:** the phase-1 blocker was that Onyx's IPC/Slime/Plant streams need a shared stage-based metabolizer. The plan must find the CHEAPEST route that gives correct behaviour: (a) profiles that simply set bleed multiplier 0 / a Wolfgate-native bloodstream reagent (IPC oil/coolant, slime jelly) via the existing server-only `BloodstreamComponent`, without porting the stage metabolizer; (b) only if (a) cannot work, scope the metabolizer rewrite as a separate, explicitly out-of-scope project. Do not port `MetabolismStagePrototype`/`SolutionManagerComponent` in phase 5.
- **P5-3 `treatmentCapabilities` on existing items:** the annotation pass deferred from phase 4 (§8.4-7): which Wolfgate items should treat which wound stages (cable coil / welder for IPC and cybernetic limbs, brute packs, gauze, ointment, etc.) — data-driven `_WF` overrides or marked lines; the plan lists every item and its capability set.
- **P5-4 Pain numbness / narcotics:** `StatusEffectPainNumbness` chain and an old-style `ModifyStatusEffect`-equivalent entity effect if any ported or Wolfgate reagent should grant pain numbness (morphine-class chems); otherwise record as skipped with reasons.
- **P5-5 Health analyzer / examine:** whatever the species profiles need to display correctly (mechanical damage names, "leaking oil" instead of bleeding) in the phase-4 panel and phase-2 examine text.
- **P5-6 Tests:** one wound-host test per new species profile (routing, bleeding or its absence, pain or its absence, fracture profile), cybernetic limb on an organic body, the treatment-capability matrix (at least three items), IPC/protogen spawn-and-delete smoke, analyzer text for a mechanical wound.
- **P5-7 Docs:** `Docs/Wolfmed/WOLFMED_PLAN5.md`, manifest, status doc; the status doc's "next phases" collapses to phase 6 (predicted routing) plus anything phase 5 explicitly defers.

## Phase 5 — answers to PLAN5.md §8.4 (2026-09-14)

- **U5 Cable coil (user decision): YES, nerf now** — Mechanical-only `treatmentCapabilities`; humans lose the cable-coil burn heal. Changelog-worthy; record as a deliberate balance fix.
- **U4 Protogen (user decision): LIFT the exclusion** — organic-profile wound host; organ gap (no `OrganDamage` on protogen organs) recorded.
- **U1 IPC pain (user decision): KEEP Onyx's pain on IPCs** (pain shock possible, no chemical relief; repair lowers pain).
- **U3′:** (b) 190/210 `MajorLimb` parity for IPC limb gib triggers. **U15:** (a) marked `Destructible` on `CyberneticPartBase`, no Heat/Ash rung. **U2:** (a) `SiliconWolfmed` container + PROTO S/T companion lines (welder/nanite keep working). **U13′:** (b) `_WF` `InorganicWolfmed` part container restoring Cold/Caustic.
- **Group B:** all PLAN5 §8.4 defaults (U16 `chemicalMaxVolume: 0`, no `InjectableSolution`; U17 diona limbs destroyed not severed; U18 drop PROTO R; pain numbness closed permanently per numbness.md).
- **Execution:** sequential packages; manifest appended directly; one owner per shared YAML file as PLAN5 assigns; the `_WF` container file lands in WP13-0 (revision N-ordering fix).

## Phase 6 (2026-09-14) — DEFERRED by the user (token budget)

Scope when resumed: client prediction of wound routing (removes the transient damage-number / limb-doll flicker on wound hosts), the `HurtCommand` part argument (patch kept at `C:\Users\jzo12\Documents\Wolfmed\plan\wp\WP8-hurtcommand-deferred.patch`), locational-armour follow-ups. Run it lean: one Opus design+implement agent using `C:/Users/jzo12/Documents/Wolfmed/plan/reports/analysis/damage-bridge.md` and PLAN.md §2/§3, one Sonnet verify at the end.

## Phase 6 — shipped (2026-09-19)

- **P6-D1 The flicker is fixed by suppression, not by prediction.** The mechanism: on a wound host the client
  ran the ordinary whole-body path (routing's `OnBeforeDamageChanged` and `OnDamageDealt` are both
  `_net.IsServer`-gated), wrote the full post-armour figure into the mob's own `DamageableComponent`, and had
  it replaced one state later by the server's projection (the sum of what each part kept, after the part
  profile and the locational armour). Predicting the routing properly is not possible: wounds are entities
  created server-side in a part container, the part choice consults server-only state, and `WoundSystem`
  refuses to run off the server. So the client now suppresses the *write* at the existing GUARD D seam and
  lets the mob's damage be pure server state. Cost: damage numbers and the health bar move one state late on
  wound hosts, which is what D35 already recorded and what the flicker was hiding.
- **P6-D2 The predicted hit still reports its damage.** `DamageDealtEvent` gained a `Suppressed` flag instead
  of reusing "clear the dict": clearing it would make `TryChangeDamage` return an empty specifier, and
  `SharedMeleeWeaponSystem` gates the red damage flash and the blunt→stamina prediction on that return while
  the server's `DoDamageEffect` filter deliberately excludes the attacker. Clearing it would therefore have
  left an attacker with no feedback at all on wound hosts. One token of upstream change
  (`|| dealt.Suppressed`); the decision itself lives in `Content.Client._WF.Wolfmed.Damage.WolfmedPredictedDamageSystem`.
- **P6-D3 Hitscan is derived, not declared.** `WolfmedWoundCause.Hitscan` existed but nothing ever produced
  it, so beam hits fell through to `Environmental`. `HitscanBasicDamageSystem` now passes the beam entity as
  `tool` (one marked line) and `WolfmedWoundRuleSystem.GetCause` reads `HitscanBasicDamageComponent`, so no
  per-prototype data is needed. **Only `WolfmedRuleGunshot` and `WolfmedRuleGraze` admit it**: the fork's
  Piercing hitscan is railgun and coilgun fire (`Magnum45`, `Coilgun134x92mm`, `Hitscan145x114mm`), which is
  hypervelocity and leaves nothing behind, so it gets the through-and-through wound but never a lodged round
  or a spent-round item. Every laser is Heat or Radiation, so the Piercing rules never see one and burns are
  untouched. Side effect, accepted: a Blunt hitscan (`Hitscan145x114mmEMP`) no longer counts as a fall for
  `WolfmedRuleDislocationThrown`.
- **P6-D4 The armour pass is by slot, and full-body suits stay unannotated.** 123 `- type: Armor` blocks
  gained `coverage:`: head, mask and eye items `[Head]`, gloves `[Hand]`, dedicated over-uniform armour
  `[Torso, Arm, Leg]` (the set phase 3 gave the `_Mono` vests). Hardsuits, EVA, bio/rad suits, modsuit bodies,
  coats, winter coats and jumpsuits keep coverage **unset**, which P3-D5 defines as "protects every part" —
  that is what "full-body suits declare full coverage" means here, and writing an explicit part list for them
  would go stale the first time a new `BodyPartType` appears. `WolfmedArmorCoverageTest` guards both halves.
- **P6-D5 Balance consequence, stated plainly.** This finishes what P3-D26 flagged: a helmet no longer
  protects the torso and a vest no longer protects the head, so stacking a vest with an unrelated helmet stops
  working. Aimed headshots against a vest-only wearer hit for full damage, and hands/feet are now protected
  only by gloves/boots or a full-body suit. Left for a balance pass, not guessed at: coats/winter coats and
  jumpsuits with armour (soft armour over an ambiguous area), the `_Mono` Aurora exosuit (described as having
  no head covering but sealing the rest), `_NF` brass knuckles (an armour *penalty* on a hands item), and
  every `- type: Armor` on non-clothing (vehicles, blast doors, mothroaches).

## Phase 7 backlog — "Viscera" (user wants, 2026-09-14; not started, token budget)

Wolfmed-only additions in `_WF/Wolfmed` (no Onyx source), to be planned lean (one Opus design+implement agent per package, builds per stage, lint/server/tests once at the end, one two-reviewer round):
- **V1 Wound SFX by cause:** on `WoundCreatedEvent`/`WoundChangedEvent` above a severity floor, play a sound keyed by the wound's damage type (Slash/Piercing splatter, Blunt thud, Heat sizzle, Shock crackle; mechanical profiles: clank/spark). Data-driven map on the body-part profile or a `_WF` `WoundSoundProfile` prototype; audio predicted where the routing is server-only means `PlayPvs`.
- **V2 Dismemberment SFX + VFX:** on `PartDetachedEvent`/amputation, a tear/rip sound (organic) or shear/spark (mechanical), a blood splatter (existing `BloodstreamSystem` puddle spawn) or spark/debris entities for synthetics, plus a gib-style limb fling (already thrown).
- **V3 Part degradation visuals:** per-part severity → sprite layer stages showing muscle/bone (organic) or struts/wiring (synthetic) on the humanoid sprite; extends phase-3's `PartDamageVisualsComponent` layers with new RSI states (needs art: 2 stages × 8 parts × organic/synthetic).
- **V4 Debris/gib particles on hits:** small blood mist / spark effect entities spawned at the struck part's position scaled by severity; throttled per part.
- **Health bar fix (done 2026-09-14):** `EntityHealthBarOverlay.CalcProgress` ratios clamped to [0,1] (marked) — projected wound damage can exceed the dead threshold, which drew a negative bar for ghosts.
- **V5 Splint (2026-09-14):** a `_WF` handheld splint item (craftable from cloth + metal rod or steel; stocked in medkits where a cell is free and the medical vendor) applied to a fractured limb via do-after; sets the fracture's treatment to *Reduced* (effects ×0.25, no surgery) and shows on the analyzer; a new hard hit resets it as usual. Mended still needs bone gel surgery or Osteogen/Stasizium. Reuse `WoundFractureSystem.SetTreatment` and the tourniquet's part-targeting pattern.
- **Guide fixes (done 2026-09-14):** Onyx's `FTLTextpart` tag inlined as markdown, bracket escapes removed, headings capped at `##`, guide names restored in `_WF/Wolfmed/guidebook/wounds.ftl`.

## Phase 8 backlog — "Wound expansion" (user wants, 2026-09-14; all confirmed wanted; not started)

All in `_WF/Wolfmed` data and small `_WF` behaviours; Onyx files untouched. Plan lean (per-package Opus design+implement, builds per stage, lint/server/tests once at the end, one review round). Order by gameplay value:

- **W0 Healing multiplier fix (first, data-only):** bleeding wounds `healingMultiplier: 0` (damage removal never closes them; gauze/sutures/surgery do), all others 0.15 (Onyx's intended value). Bruise packs treat Blunt wounds only; ointment Burn only; sutures Slash/Piercing. Update the treatment-matrix tests and the guide.
- **W1 Ballistic:** `GunshotWound` (Piercing from projectiles; through-and-through vs *lodged round* at higher severity — lodged keeps bleeding/pain until removed), `ShrapnelWound` (explosions, buckshot: several small embedded fragments), `GrazeWound` (low severity, brief bleed). **Removal ala CMSS13:** forceps or any sharp item (knife, scalpel, glass shard) can pry fragments/rounds out via a do-after; knives hurt more and risk a small slash wound; forceps/surgery are clean. Removed objects spawn as items (spent round / shrapnel).
- **W2 Slash and bite:** severe-stage `ArterialBleed` (fast bleed; gauze only slows, tourniquet or clamp surgery stops), `TendonCut` on arms/hands (manipulation penalty, suture surgery) and legs (limp), `Avulsion` from bites (infection risk, scars).
- **W3 Blunt:** `CrushInjury` at severe stage (function loss + internal-bleed chance), `Concussion` on head (blur, stutter, short knockdown; sleep/time heals), `Dislocation` on joints (fracture-like penalty, cleared by a painful "relocate" do-after, no bone gel), `OrganContusion` on torso (organ damage without destruction).
- **W4 Burns:** `Charring` top stage (needs skin graft step or synthflesh), *cauterisation* (Heat on a bleeding wound seals it: lasers/welders stop bleeding at the cost of a burn), `Frostbite` (numbness then necrosis), `ChemicalBurn` (keeps ticking until washed at a shower/water), electrical internal burns with a heart-damage chance and spasm.
- **W5 Time-based:** `Infection` (open wound untreated for N minutes: fever, slow toxin, worsening; antibiotics or cleaning; start from Onyx's surgery-infection component), `Necrosis` (tourniquet left too long or late reattachment; permanent, amputate and replace), `Sepsis` as the systemic stage.
- **W6 Mechanical (IPC/cybernetic):** `Dent`, `Breach` (fluid leak, welder), `ShortCircuit` (shock: brief stun + spark), `ServoDamage` (function loss, cable coil), `Overheating` (slows until cooled).
- **W7 Treatment matrix + content:** forceps item, skin graft step, antibiotics reagent, shower/water wash interaction, guide and analyzer wording, tests per wound type.
- **Balance note (2026-09-14):** fractures almost never occur with Onyx's per-hit thresholds (20/35/50/60, 5–100 %) against Wolfgate melee (8–15 Blunt per swing); proposed `_WF` override 12/20/32/45 at 25/50/80/100 % with accumulation 0.8, and optional Piercing fracture profile for heavy rounds — decide with W0.
- **Art sourcing (2026-09-14):** sprites for the new items (forceps, splint, spent round, shrapnel, skin graft) and phase-7 degradation/debris art may be taken from other SS13/SS14 codebases (tg, CM-SS13, Goon, Bay, other SS14 forks) provided the source license is CC-BY-SA or otherwise compatible and every `meta.json` records license and attribution verbatim. CC-BY-NC-SA and unlicensed art are excluded. Record each borrowed RSI in the manifest with its source repo and commit.

## Final stages (2026-09-19)

Phases 6, 7 and 8 plus the crit heartbeat (WP H) shipped this run: H, W0–W7 (phase 8), V5/V124/V3 (phase 7)
and P6 (phase 6), 14 work packages, one report each in `C:\Users\jzo12\Documents\Wolfmed\plan\p6\wp\`. The
decisions below were made by those packages, not re-litigated here; this section exists so the owner does
not have to open all 14 reports to find them.

**Decisions carried from the individual work-package reports:**
- **Fracture thresholds are 12/20/32/45 at 25/50/80/100 %** (`WolfmedFractureProfile`, W0), replacing Onyx's
  20/35/50/60 at 5–100 % because Wolfgate melee (8–15 Blunt per swing) almost never crossed the Onyx bands.
  `accumulationMultiplier` raised 0.4 → 0.8 alongside it. `BoneFractureWound`'s own stage thresholds were
  lowered to match (20/35/50/60 → 12/20/32/45) so a fresh Hairline fracture is not below the wound's own
  lowest stage.
- **Deterministic damage bands instead of chance rolls, wherever the spec allowed it** (W1, W2): a heavy
  round (>= 18 Piercing in one hit) always lodges rather than lodging "at high damage or by chance"; the
  arterial-bleed and tendon-cut thresholds are flat damage bands, not rolls. The one chance roll that
  remains is internal bleeding at 30 Blunt / 40 % (W3), kept because Onyx's own `InternalBleedingWound`
  is chance-based. Rarity lives in the thresholds, which keeps the wound suite RNG-free.
- **No forceps item.** The existing hemostat and tweezers components are the "clean tool" for pulling an
  embedded object (W1); W7's planned forceps item was dropped because the two components it would have
  carried already exist and are already lathe-printable and in the surgical crate.
- **No Piercing fracture profile.** `WolfmedBodyPartComponent.FractureProfile` is a single id and
  `WoundFractureSystem` assumes one fracture per part (`GetFracture` returns the first, creation bails if
  one exists, grading re-derives the profile from the part). A second damage type needs a list on the
  component plus a rewrite of creation, grading and treatment — past the "small marked change" the task
  allowed, so W0 skipped it as scoped. Still open; see below.
- **Mechanical wounds keep `healingMultiplier: 1`, not the 0.15 organic nerf** (W0). A welder is the only
  thing that closes a chassis wound and it works by removing damage; at 0.15 the repeat loop would burn
  fuel on a wound it could no longer reduce and chassis wounds would become permanently open. Flesh has
  sutures and surgery for the 0.15 case; chassis does not.
- **Spaceacillin is not in a medkit** (W7). Every medkit is at 4–8 cells already and `StorageFill` silently
  drops an entry (and logs an error that fails every pair test) when a kit overflows. The medical vendor
  (3), `CrateMedicalSupplies` (2) and the chemistry reaction from W5 are three routes without risking a
  spawn error; the skin graft did fit the burn kit and shipped there instead.
- **The routing flicker is fixed by client-side suppression, not by prediction** (P6-D1/D2). Wound routing
  cannot be predicted — wounds are server-side entities in a per-part container and `WoundSystem` refuses
  to run off the server — so the client now suppresses the *write* of predicted damage onto a wound host's
  `DamageableComponent` at the existing GUARD D seam (`DamageDealtEvent.Suppressed`, one upstream token)
  instead of writing and then being overwritten a state later. The report (not the return value) is kept,
  so melee's red-flash and stamina prediction still fire for the attacker. Cost, already recorded at D35:
  damage numbers and the health bar move one network state late on wound hosts.
- **The heartbeat asset's license is pending the project owner's confirmation** (WP H /
  `Resources/Audio/_WF/Wolfmed/attributions.yml`). The audio and its attributions entry were already on the
  branch before WP H; the entry reads `license: "Custom"` with copyright text "Source and license to be
  confirmed by the owner." WP H only wired playback and did not source or re-license the file. Needs an
  answer before this branch ships to players.

**Every gap still open, for the owner to act on:**
- No Piercing fracture profile (W0) — see decision above; needs a list-typed `FractureProfile` plus a
  rewrite of `WoundFractureSystem`'s creation/grading/treatment assumptions if a second fracture type per
  part is wanted.
- `Surgery` stays in `WolfmedWoundCause` with nothing deriving it, and no rule filters on it (W1/W7);
  deriving it would add an upstream hook nothing currently reads, so it was left undone on purpose.
- Systemic bleeding chemicals (`ModifyBodyBleeding`/`StopBodyBleeding`) still stop an arterial bleed; only
  the part-targeted topical path is gated behind the arterial-bleed treatment ladder (W2, open per W4).
- The analyzer names every wound but has no dedicated arterial / dislocated / concussed / numb / residue /
  mechanical-overheating flag; a medic reads the wound name and the guide's quick reference (W2–W6).
- Necrosis has no sprite or visual on the limb, only the analyzer flag, the popup and the wound name (W5).
- The short circuit's spark effect spawns at the body rather than at the struck part, because parts live in
  nullspace with no coordinate of their own (W6). `WolfmedOverheatingComponent.CoolingPerMinute` is
  networked but nothing on the client reads it yet.
- Seven interactions reuse the plain do-after bar with no sound or custom visuals of their own: embedded
  object removal (W1), joint relocation (W3), deliberate cautery (W4), tourniquet loosening (W5), wrench
  panel-beating (W6) and the splint (V5). V124 gave five of these a *begin/end sound* through
  `SoundSpecifier` fields with sane defaults, so only the bar and any bespoke visual are still missing.
- Spaceacillin has a chemistry reaction and is stocked on the vendor and in a supply crate, but has no
  lathe recipe and is in no medkit fill (W5/W7, deliberate per the decision above).
- Fever (infection's Spreading stage) climbs toward a fixed 313 K with no shiver, sweat or other feedback,
  and nothing reads it back out except the existing temperature UI (W5). Non-sterile *conditions* are not
  modelled beyond W1's improvised embedded-object removal calling `Contaminate` once (W5).
- V3's per-part degradation stage thresholds (25/60 summed severity) are a first guess against a
  dismemberment range of 80–200 and want a playtest pass; the bone overlay reads subtle at 32 px on pale
  skin tones.
- No inhand sprite for the splint (V5) — the RSI has room for eight frames. Nothing clears a splint's
  reduction when the part is amputated (the item is consumed on use, so there is no worn state to clean up
  and the reduction simply dies with the part).
- Wound sounds and hit debris are all `PlayPvs`, never predicted, because wound creation is server-only
  (V124); blood mist is the existing puddle-splatter sprite re-tinted, not bespoke art.
- Locational armour is unannotated (full coverage) on armoured coats, winter coats and jumpsuits (soft
  armour over an ambiguous body area), the `_Mono` Aurora exosuit (seals everything except the head), `_NF`
  brass knuckles (an armour *penalty*, so narrowing its coverage would be a buff) and every non-clothing
  `- type: Armor` block — all left for a balance pass rather than guessed at (P6-D4/D5).
- The heartbeat asset's license is pending owner confirmation — see decision above.

## Playtest fixes (2026-09-19)

- **IPCs take no Airloss-group damage, ever (owner decision, reverses P5-D5/P5-D5b).** `Bloodloss` came off `SiliconWolfmed` and the IPC bloodstream's `bloodlossDamage`/`bloodlossHealDamage` are empty: Bloodloss reads as oxygen loss on the analyzer and an IPC does not breathe. Consequence: an oil leak currently costs an IPC nothing but the oil. Open: give low oil its own consequence (slowdown or overheating) if leaks should matter.
- **Body damage ceiling.** `wolfmed.body_damage_cap` (default 600, absolute, 0 disables). Routed part damage past it is discarded (`WolfmedBodyPartSystem.ClampToBodyCap`, one marked hook in `WoundDamageRoutingSystem`). Absolute rather than a multiple of the dead threshold because an IPC dies at 100 while its limbs come off near 200. Infection and sepsis no longer damage corpses.
- **One severed arm took both IPC arms.** `SharedBodySystem.PartAppearance` copied every marking in the limb's category (Arms spans both sides; IPC limbs are markings). Now filtered to the layer's own markings (marked).
- **Heartbeat kept looping after death.** `SharedAudioSystem.Stop` is a no-op on ticks that are not first-time-predicted; the client system now deletes its stream directly and reconciles every frame.
- **Infection tick crashed the server** ("Collection was modified"): infection, necrosis and frostbite ticks now buffer their targets.
- **Test harness trap:** a test that fails inside a whole-suite run can be reported as *Skipped* ("dirty-disposed") while the run says Passed. Always rerun skipped tests standalone.

## Playtest fixes, round 2 (2026-09-20)

- **A stopped bleed stays stopped.** Two causes of "gauze held for seconds" and "a bruise pack made it bleed": (1) `WoundBleedingSystem.ReduceBleeding` removed the bleeding component at zero, and `WoundSystem.SyncRuntimeComponents` re-rolled the bleed at full wound severity on the next severity change of any kind, healing included; (2) nothing ever set `Bandaged` for an ordinary wound, so no dressing showed. Now: gauze (`dressing: true`) keeps the component at zero marked `Bandaged`, and a wound only rolls for bleeding when it has grown since the last sync (`WoundComponent.LastSyncSeverity`, marked). A fresh hit still reopens it.
- **Bleeding slowed.** `wolfmed.bleed_rate` (default 0.6) multiplies every wound's rate where it is computed, so analyzer, spurts and bloodstream agree. Tests that assert Onyx's literal rates pin it to 1.
- **Splints take torso and head.** Ribs and skulls fracture and the procedure says to splint; the art has chest and head wraps.
- **Health analyzers need no power cell** (`PowerCellDraw`, `ToggleCellDraw`, `ActivatableUIRequiresPowerCell` commented out on `HandheldHealthAnalyzer`; the slot stays so fills and maps load).
- **Analyzer window geometry (REVIEW, 2026-09-22).** The analyzer window itself is 900x600 and resizable,
  two panes side by side, rather than the fixed 350x650 of §8.4-5. The wound panel, the treatment advice and
  the doll do not fit a single narrow column; §8.4-5's "window geometry untouched" is superseded for the
  window frame, and everything inside it is still Shitmed's own layout.
- **Analyzer doll geometry.** Shitmed laid the analyzer's doll buttons out at 2.5x over a doll `SetupIcon` draws at 3x. The XAML now carries the HUD targeting doll's exact 3x geometry and the highlight uses the HUD's mechanism (base part texture at 3x, centred).

## EMP and machine bodies (2026-09-20)

- `WolfmedEmpSystem` (server): an `EmpPulseEvent` reaching a non-organic `Woundable` part that is attached to a wound host deals Shock to it through the routing, which opens the W6 short-circuit wound (stun, sparks, cable coil). Parts are collected per body and resolved at the end of the tick so one pulse shares a budget: `wolfmed.emp_part_damage` (15) per part, `wolfmed.emp_body_damage` (45) per body per pulse. A lone cybernetic limb takes 15; a ten-part IPC takes 4.5 a part. Shitmed's own `CyberneticsSystem` still disables cybernetic parts for the pulse duration; this adds the damage. IPCs die at 100, so one EMP cannot kill a healthy one and three can.


## Evisceration (2026-09-20)

- **The torso's overflow buys disembowelment.** D9 never severs a torso, so `WoundableComponent.AmputationOverflow`
  past the torso's 250 cap had no consumer. `AmputationSystem.OnPartDamageOverflowed` now raises
  `WolfmedTorsoOverflowEvent` there (a marked three-line raise, no logic) and `WolfmedEviscerationSystem`
  (server) answers it.
- **Trigger, all data.** `wolfmedEviscerationProfile` (`WolfmedEviscerationDefault`), named by the torso's
  `WolfmedBodyPart.eviscerationProfile`. A part that names no profile can never be opened. `finishingDamage`
  is `Slash: 35`, so Blunt, Piercing and Heat can never do it however hard they land;
  `explosionFinishingDamage` (`Slash 25 / Heat 30 / Blunt 45`) is the blast table. Both are read against the
  OVERFLOW of one hit, which is the whole hit once the torso is at its cap. No CVar gate; alive, crit and
  dead all qualify; one evisceration per torso, held by `WolfmedEviscerationComponent`.
- **What comes out is data.** `organs` (stomach, liver, kidneys, intestines) always; `vitalOrgans` (heart,
  lungs) only for an explosion or on `vitalChance` 0.15, seeded through `WolfmedEviscerationSystem.ForcedRoll`
  in tests. The brain is in the head and a positronic one is deliberately absent from the chassis list.
- **The open belly IS the open incision.** The system grants Shitmed's own `IncisionOpen` and `SkinRetracted`
  to the torso, so every torso surgery that wants an open incision lists with no scalpel and no retractor,
  and the SHIPPED organ insertion surgeries are what put the organs back. It remembers whether it granted
  them, so a patient a medic had already cut open keeps their incision when the tear is closed.
- **Exit.** `SurgeryCloseEvisceration` (hemostat clamp, then cautery) removes `WolfmedEviscerationWound` and
  leaves a sutured `SlashWound` at the severity its `WolfmedClearedWoundBehavior` names. It completes with
  organ slots still empty on purpose: a closed patient needing a transplant beats an open abdomen.
- **IPCs do it too.** The same trigger on a mechanical torso makes `WolfmedChassisBreachWound` ("torn
  chassis"), ejects the chassis's own torso components, leaks the body's own reagent (oil), hisses instead of
  tearing and refreshes `WolfmedMachineSparkSystem`. `SurgeryWeldChassisBreach` (wrench, then welder) leaves
  the ordinary `WolfmedBreachWound`. An IPC torso had no damage cap at all, because it parents
  `BaseTorsoInorganic` rather than `BaseTorso`; `WolfmedBaseTorsoIpc` supplies one.
- **The procedure window can see an empty slot.** `HealthAnalyzerWoundDiagnostic.MissingOrgans` and the
  `OrgansRestored` step check, so "organs back in" greys itself.

## Aim scatter (2026-09-20)

Bullets no longer always land on the shooter's targeted part. `WolfmedAimScatterSystem` (shared, `_WF/Wolfmed/Targeting`)
projects the gun's current spread cone (`GunComponent.CurrentAngle`, floor `MinAngleModified`) out to the target's range and
compares its half-width to the part's size (`WolfmedBodyPart.aimSize`, defaults per part type): chance = size / stray,
clamped to `wolfmed.aim_worst_chance` (0.15) .. `wolfmed.aim_best_chance` (0.9). A miss goes to a weighted neighbouring part.
Only hits whose tool is a projectile are rolled; melee, thrown items and explicit `targetPart` calls are untouched. Hitscan
already lands on a random part (its origin is the gun, which has no targeting). `wolfmed.aim_scatter` turns it off.
Hook: one marked line in `WoundDamageRoutingSystem`. Test: `WolfmedAimScatterTest`.

## Blast dismemberment (2026-09-20)

Onyx's explosion severing reads one limb's share of the blast against that limb's totals, so it almost never fires. After a
blast is routed, `WolfmedExplosionSystem.TryBlastDismember` rolls on the whole blast (armour already applied): chance per limb =
(total - `wolfmed.blast_dismember_min` 30) / (`wolfmed.blast_dismember_full` 150 - min) x `wolfmed.blast_dismember_chance` 0.8,
rolling 1 + total/full random limbs through `AmputationSystem.TryAmputate`. Torso never; head only with
`wolfmed.blast_dismember_head`. `wolfmed.blast_dismember` turns it off. Test: `WolfmedExplosionTest.BlastSizeRollsLimbsOffTest`.

## Dying view (2026-09-20)

Client-only `WolfmedDyingEffectsSystem` + `WolfmedDyingOverlay` (shader `WolfmedDying`, `Textures/_WF/Wolfmed/Shaders/dying.swsl`).
A level 0..1 starts at half the crit threshold, reaches 0.55 on going critical and 1 at the dead threshold, eased so it never pops.
It drives: colour draining to cold grey, double vision, a tunnel that squeezes on a heartbeat (pulse races toward crit, then slows),
eyes drifting shut every few seconds past 0.72, and a slow camera sway through `GetEyeOffsetEvent` on a client-only
`WolfmedDyingSwayComponent`. Sway honours `accessibility.reduced_motion` and the screen shake slider; `wolfmed.dying_effects`
(client) turns it all off. Aim scatter defaults moved to 0.75 best / 0.1 worst.

## Consciousness (2026-09-22)

The premise, from the owner: players are far too easy to kill. On a wound host (`WoundHostComponent`) damage
totals now decide nothing at all. Two meters replace the `MobThresholds` crit/dead decision; this package is
the first, CONSCIOUSNESS. LIFE is the BRAIN package and is not built yet. Mobs without `WoundHost` are
untouched and still cross their thresholds exactly as before.

- **The gate is one marked line.** `MobThresholdSystem.CheckThresholds` opens with
  `if (_wolfmedConsciousness.OwnsMobState(target)) return;`, where `OwnsMobState` is
  `wolfmed.consciousness && HasComp<WoundHostComponent>`. Nothing else in the threshold system changed:
  `CurrentThresholdState` simply never moves for a wound host, so `OnUpdateMobState` leaves `MobState.Invalid`
  and `ChangeState` no-ops. Consequence: **a wound host can no longer die of damage at all** until BRAIN
  lands. Dead is reachable only by the paths that never went through the thresholds (a destroyed brain in
  `OrganHealthSystem`, a gib, an admin). Defib and CPR still compare damage to the dead threshold and were
  left alone per the spec.
- **Three states.** Up, Downed (`WolfmedDownedComponent`, conscious and on the floor) and Unconscious
  (`MobState.Critical`, unchanged). Dead is untouched.
- **Four inputs, each normalised to "1 = this state".** Effective pain / (0.70 x soft cap) for Downed; pain
  before the soft clamp / (1.25 x soft cap) for Unconscious; blood volume against 0.60 and 0.45; both legs
  disabled or missing; and external pressures (0 none, 1 unconscious), which reach Downed at 0.7. The worst
  wins. Hysteresis `wolfmed.consc_hysteresis` 0.1 means leaving a state needs the reading back under 0.9.
- **"Pain before the soft clamp" is the sum of the parts, not a new field.** `PainSystem.SetPain` clamps the
  part *and* the body to `SoftPainCap` (135), so the body's own value can never read past 1.0 of the cap.
  `WolfmedConsciousnessSystem.GetUncappedPain` sums `GetPain` over the body's parts instead. No `_Onyx` edit,
  and the vignette's own value is untouched.
- **Airloss is a pressure, not a threshold.** `Airloss / (MobThresholds Critical)` is pushed through
  `SetExternalPressure(body, "airloss", level)` on every `DamageChangedEvent`, so suffocation still reaches
  Critical with the thresholds gated off. BRAIN replaces this key with real brain oxygenation.
- **Blood is read on the bloodstream's own tick.** `BloodstreamComponent` raises no event when its volume
  changes, so `BloodstreamSystem.Update` carries one marked three-line call at the point where it has already
  computed the percentage. Everything else is event-driven (pain changed, damage changed, part functionality
  changed, a painkiller dose) plus a 0.5 s poll of bodies that still have a nonzero input.
- **Painkillers are four tiers and no healing.** `WolfmedPainReliefComponent` holds one dose per reagent,
  written by the `WolfmedPainRelief` metabolism effect. Weak subtracts pain from the Downed test only; Strong
  subtracts from both, masks the wound slowdowns (one marked hook in `FractureEffectsSystem.OnRefreshSpeed`)
  and builds sedation; Stimulant lifts Downed outright regardless of pain; Emergency lifts both for a window
  and then crashes (pain x1.3, Downed 10 s). **No tier touches blood, airloss or an external pressure.**
- **Sedation is the overdose.** It slows movement and, past 0.6, applies Asphyxiation scaled by how far past.
  That is a stand-in: BRAIN takes it over through `SetExternalPressure(body, "sedation", level)`.
- **The dying view reads consciousness, not damage.** `WolfmedDyingEffectsSystem.TargetLevel` returns
  `WolfmedConsciousnessComponent.Depth` for a wound host (0..0.35 approaching Downed, 0.35..0.55 Downed,
  0.55..1 Unconscious by blood and pressure). `Level` itself is unchanged and still serves everything else.
- **Rejuvenate is now the only thing that revives a wound host**, because the thresholds no longer do; the
  handler sets `MobState.Alive` itself and re-evaluates a tick later, once every other rejuvenate handler has
  run.

## Autodoc (2026-09-22)

A one-tile surgical pod, `MachineAutodoc`, running "S.A.M.". It performs REAL surgery: the pod is the
performer in Shitmed's own step machinery, so every wound, condition, sound and side effect of a
hand-performed surgery applies. It can only perform surgeries and push reagents; it never bandages, never
heals by fiat and never applies a topical.

- **The pod is the surgeon, through three marked hooks.** `SharedSurgerySystem.GetTools` asks the performer
  for its own toolset first (`WolfmedSurgeryToolsEvent`) and falls back to hands, so a machine with no hands
  can hold a scalpel; `IsLyingDown` and the operating-table condition both accept a pod occupant
  (`SharedAutodocSystem.OnOperatingPlatform`). All three bodies live in
  `Content.Shared/_WF/Wolfmed/Surgery/SharedSurgerySystem.Autodoc.cs`; the upstream files carry six marked
  lines between them. `WolfmedPerformStep` is the entry point: it repeats every check `OnTargetDoAfter` makes
  and then raises the same `SurgeryStepEvent`, with no do-after and no hands.
- **A procedure ends on its own last step, or when the surgery stops being valid.** `GetNextStep` walks the
  requirement chain, and the closing step of most surgeries removes the incision its requirement opened, so a
  naive loop re-opens the patient forever. The pod stops when the queued surgery's own final step reads
  complete, or when a step it already started performing no longer validates - which is exactly what a
  surgeon sees when the entry leaves the menu after the fracture is mended.
- **The pod is sterile and slow.** `SanitizedComponent` is on the machine, so the unsterile-surgery poison
  never fires against a patient the pod is treating. Base step time is 1.25x a surgeon's, falling to 0.9x at
  the best manipulator tier; matter bins scale the reservoir; capacitors the draw. Malfunction is 2% a step
  scaled by machine damage, and the only result is one shallow `SlashWound` on the part being worked on.
- **Disks gate the advanced library, in data.** `autodocProgram` prototypes list surgery ids;
  `WolfmedAutodocProgramBase` ships on the machine and Transplant / Limb / Neuro / Cavity each need their
  disk in the slot. `autodocProcedure` prototypes (id = the surgery id) override the default 15u
  anaesthetic / 10u antibiotic per procedure.
- **One reagent list, not a flag on every reagent.** `autodocReagents` `WolfmedAutodocReagents` names every
  administrable reagent and its role. Anything else in a beaker stays in the beaker and the pod says so.
- **Downed can crawl in.** `WolfmedDownedReachableComponent` is a named exception to CONSC's "self only"
  interaction rule, carried by the pod. Unconscious is still blocked by the ordinary action blocker.
- **The UI reuses the analyzer.** The server sends the analyzer's own `HealthAnalyzerScannedUserMessage`
  inside the pod's BUI state (`HealthAnalyzerSystem.WolfmedBuildScanMessage`), so the window mounts the
  analyzer's `WolfmedDiagnosticPanel` verbatim and a new `WolfmedBodyDoll` control carrying the analyzer's
  exact 3x doll geometry.
- **One voice line, one file, one transcript.** `autodocVoice` maps 62 events to 74 line ids; each id has one
  ogg, one Fluent transcript and one priority, all written by `Tools/_WF/wolfmed/gen_autodoc_voice*.py` from
  one table, so the spoken line and the chat line can never disagree. Generated with eSpeak NG. No
  `SoundCollection` is used: a collection would pick a file independently of the transcript.
- **The pod is never two voices at once (AUTODOC2).** Every line carries an `AutodocVoicePriority`. `Urgent`
  stops the playing stream and speaks now, `Info` waits in a queue of at most three, `Step` is dropped
  outright if anything is playing or waiting, and `Chatter` only starts after eight seconds of silence. Step
  announcements are one or two words and a procedure speaks at most one per `AutodocStepFamily`, so a long
  surgery says "INCISION. CLAMPING. SAWING. SUTURING." and not a sentence a step. Line length is read off the
  ogg, so the queue can never outrun the audio, and a tool sound plays at 0.6 gain while a line is running.
- **The pod is a bed, not a box (AUTODOC2).** Two sprite layers: the open pod as the base, the lid over it.
  With the lid open the sprite sits at `BelowMobs` and the occupant (`showEnts: true`, laid down through the
  standing-state appearance key) is drawn on top; closed, it goes to `OverMobs` and the lid covers them. The
  dim unpowered colour is applied and cleared by `AutodocVisualizerSystem`: a `GenericVisualizer` could only
  ever set a layer colour, never put it back, so one unpowered tick at map init dimmed the pod for good.
- **Emag is a threat, not a tool.** The lid locks, the lines switch to the sinister set and the pod queues an
  amputation of a random limb and runs it, repeating until the power goes or somebody pries the lid.
- **No revival.** `AutodocDefibModuleComponent` is recognised and reported in the window and does nothing;
  that is BRAIN's.

## Brain death (2026-09-22)

LIFE, the second meter CONSC left a hole for. On a wound host the heart either beats or it does not, and the
brain either has oxygen or it is running out of it. **There is no permanent unrevivable state from Wolfmed.**
Brain death IS `MobState.Dead` - the ghost, the corpse, the "YOU DIED" screen - and a dead body stays
revivable if somebody does the work. `RottingSystem.IsRotten` is still the only hard stop and is untouched.

- **Cardiac arrest** (`WolfmedCardiacArrestComponent`, networked, on the body). The body is Critical through
  `SetExternalPressure(body, "arrest", 1)`, examine and the analyzer say "no pulse", the crit heartbeat loop
  goes silent after one flat tone (`/Audio/_WF/Wolfmed/flatline.ogg`), the G2 spurts wait instead of firing
  and passive bleeding runs at `WolfmedLifeSystem.ArrestBleedFactor` (0.25) applied to the body's cached
  stream rates. Triggers: heart destroyed or removed; blood <= `wolfmed.arrest_blood` (0.30); brain
  oxygenation <= `wolfmed.arrest_oxygenation` (0.15); a pain shock while blood <= `wolfmed.arrest_shock_blood`
  (0.5); late sepsis at `wolfmed.arrest_sepsis` (80) with `wolfmed.arrest_sepsis_chance` (0.01/s); an
  electrocution of `wolfmed.arrest_shock_damage` (60) or more. Ends: a defibrillator, a rejuvenate, or - only
  when the heart was the cause - the heart back in the chest with blood above the arrest level. CPR never
  ends it.
- **The clock** (`WolfmedBrainComponent` on the brain organ, networked, `Oxygenation` 1 to 0). Drains by the
  worst of: heart stopped (full drain in `wolfmed.brain_arrest_seconds` 120 s), not breathing (airloss over
  the old crit threshold, or a sedation overdose, full drain in 180 s at 1.0), blood under 0.5 (linear to a
  full drain in 300 s at 0.3), late sepsis (600 s). Refills at the arrest rate x `wolfmed.brain_refill_factor`
  (0.5) when nothing drains. Multipliers: cold (`WolfmedBrainComponent.ColdSteps`, x0.5 under 30 C and x0.1
  under 20 C, scaled by `wolfmed.brain_cold_factor`), CPR (x0.25, and CPR answers for breathing and counts
  blood as at least 0.5), stimulants (x0.6). Under `wolfmed.brain_damage_oxygenation` (0.4) the brain ORGAN
  takes `wolfmed.brain_damage_rate` (0.1/s at zero oxygenation, linear from the threshold) of irreversible
  organ damage, and organ health 0 is death on the existing `OrganHealthSystem` path.
- **Real timings, which are not the spec's own arithmetic.** With the rule as written (damage under 0.4
  oxygenation, 0.1/s at zero) an untreated arrest reaches 0.4 at 72 s, zero at 120 s, and brain death at
  about 246 s, not the 198 s the spec's summary line quoted. Under CPR the brain never reaches the damage
  band inside ten minutes. The rule was implemented; the illustrative figure was not.
- **Brain missing is event driven, never a poll.** `OrganRemovedFromBodyEvent` on `WolfmedOrganComponent`
  and `WolfmedPartAmputatedEvent`, and the amputation handler asks whether the part that came off was
  carrying the brain. A wound host that never had a brain (every brainless test fixture, including the one
  `PassiveDamageMechanismStillRoutesIfReenabledTest` uses) lives exactly as long as anything else.
- **Hypoxia is a pressure of its own.** CONSC's `"airloss"` stand-in is gone; the clock writes
  `"hypoxia"`, ramping from `wolfmed.brain_pressure_start` (0.75) to `wolfmed.brain_pressure_out` (0.45).
  Sedation's Asphyxiation stand-in is gone too: `WolfmedPainReliefSystem.GetRespiratoryDepression` is now
  both a `"sedation"` pressure and a breathing input to the clock.
- **Defibrillation** replaces the damage-threshold gate in `DefibrillatorSystem.Zap` with one marked block:
  brain present, blood > `wolfmed.defib_blood` (0.40), brain organ health > 0, then a roll of
  `wolfmed.defib_chance` (0.85) x lerp(`wolfmed.defib_oxygenation_floor` 0.15, 1, oxygenation). Success
  clears the arrest, leaves 0.35 oxygenation and hands the state back to consciousness; a body that had been
  Dead always comes back Critical, because it comes back on what the paddles put into it. A failed shock
  costs the zap damage and can be tried again.
- **Brain repair surgery** `SurgeryRepairBrain` (head incision, saw, `SurgeryStepRepairBrain` with a
  tending tool, seal) is the only thing that raises organ health, matching D7. It restores the organ to
  maximum and leaves `WolfmedBrainTraumaComponent` for `wolfmed.brain_trauma_minutes` (30), which feeds W3's
  concussion effects. It is gated on a DESTROYED organ (`destroyed: true` on the organ condition), so it
  never overlaps `SurgeryHealBrain`, which still handles a damaged but living brain.
- **CPR** marks the chest (`WolfmedCprComponent`) for the do-after's length and nothing else. One marked
  condition in `CPRSystem` skips the damage-threshold resuscitation on a wound host.
- **The autodoc's defib module works.** With `AutodocDefibModuleComponent` in the module slot the pod runs
  the same rule on an arrested occupant before the first procedure of a queue and again when the queue ends,
  with three new eSpeak NG lines (`defib-charge` "CLEAR.", `defib-success`, `defib-failure`).
- **Mechanical bodies have no clock.** `WolfmedShutdownComponent` (pressure `"shutdown"`) when the cell is
  pulled or flat (`SiliconChargeDeathEvent`) or the micro pump is destroyed or removed. "Mechanical" is a
  wound host that carries `SiliconComponent` AND runs no clock, not merely a body with no brain: the second
  half alone is the brainless-poll bug in a different costume and it shut down every brainless fixture. That is not death and
  nothing runs out. `PositronicBrain` and `OrganIPCPump` gained `WolfmedOrgan` + `OrganDamage`, so a
  destroyed positronic brain is death on the same organ path a fleshy brain uses, and the way back is the
  same: repair the brain, then a jolt.
- **Shitmed's delayed death is gated off on a wound host** (one marked `continue` in
  `DelayedDeathSystem.Update`): a missing heart is arrest, not a countdown. Its defib refusal for a body
  with no heart or brain is kept.
- Wolfmed still never adds `UnrevivableComponent`. Other content that sets it is still honoured.

## Arrest looks dead, autofix, pod alarms (AUTODOC3, 2026-09-22)

### Cardiac arrest is a living body that looks dead

The owner's complaint was that shooting somebody and watching them gasp is not satisfying. The first cut of
this was to make arrest `MobState.Dead`; that was withdrawn, because metabolism, objectives, rot and every
other rule that reads a living body have to keep treating an arrested patient as alive. **Arrest stays
`MobState.Critical` exactly as BRAIN built it.** What changed is only what a person can perceive:

- **No gasping.** One marked condition in `RespiratorSystem.Update` skips the gasp emote while
  `WolfmedCardiacArrestComponent` is present. The body still suffocates and still takes the damage; the gasp
  was the only part of it anybody could see or hear. Every other emote and vocalisation is already blocked
  by `MobStateSystem`'s crit check, which the gasp bypassed with `ignoreActionBlocker: true`.
- **Examine reads as a corpse.** `WolfmedVisualInspectionSystem` adds `wolfmed-look-appears-dead-*` for an
  arrested body at any range, and the "is not breathing" note, which used to need the details range, now
  shows at a distance too. "Has no pulse" still wants a hand on the neck.
- **The medical HUD tells the truth.** `HealthIconWolfmedArrest` (a flatline drawn from the stock Critical
  icon, `_WF/Wolfmed/Interface/health_icons.rsi`) replaces the crit icon on an arrested body, through one
  marked hook in `ShowHealthIconsSystem.DecideHealthIcons` whose body is a `_WF` partial. The analyzer's
  CARDIAC ARREST line is unchanged.
- Death screen, banners, the crit heartbeat and the flatline tone are BRAIN's and are untouched.

### Autofix module and triage

- `AutodocTriagePrototype` (`autodocTriage`) is an ordered list of steps, each naming surgeries, categories,
  or the cardiac module. `WolfmedAutodocTriage` is the shipped order: defibrillate, arterial and internal
  bleeding, bleeding, evisceration, organ repair in place, bones, deep wounds, shallow wounds, mechanical
  repair, limbs and organs the tray already holds, and finally closing whatever somebody else left open.
- `AutodocSystem.Triage.cs` walks it against `GetAvailable`, so the planner can only ever queue a procedure
  a human could have queued from the same window. It drops anything the disks do not unlock and anything
  whose requirements want an item the delivery tray is not already holding.
- `requiresStarted: true` on a step queues it only when the body is already past the procedure's own
  requirements. Without it the last step would have queued a close on every intact limb, because closing an
  incision is a valid surgery on anybody: the pod would simply open one first.
- **PLAN** (base pod, no module) writes that queue and says "I HAVE A PLAN."; the operator still presses
  START. In self-service the two are one **FIX ME** button. **AUTO** needs `AutodocAutofixModuleComponent`
  in its own slot and plans and starts on its own every `autoPlanInterval` seconds while somebody is in the
  pod, re-planning when a queue ends and saying "NOTHING MORE I CAN DO." once when the plan comes back
  empty. An emagged pod ignores AUTO and keeps its own plans.

### Vital alarm

`AutodocAlarm` reads the occupant once a tick: Flatline (brain gone) beats Arrest beats Critical beats Dying
(the oxygenation clock draining). Arrest beeps every 2 s, everything else every 4 s, with
`/Audio/Machines/quickbeep.ogg`; a flatline is one `flatline.ogg` and then silence, because there is nothing
left to call anybody for. The first beep of each escalation carries a line ("PATIENT IS DYING." for the
clock, the existing critical line otherwise). `wolfmed.autodoc_alarm` turns the whole thing off.

### Lid and eject

The open art and the closed art are both whole pod sprites, so drawing them together showed the bed through
the lid: the visualizer now draws exactly one layer per state, and the body container's `showEnts` follows
the lid, server side, with the client re-occluding the occupant itself because the engine only recomputes
container occlusion on a parent change. An eject only says "EMERGENCY EJECT" while something is running;
from Idle or Complete it is "GOODBYE." and no abort.

### No procedure may loop (AUTODOC4)

The owner's pod repeated "Repair damaged tissue" for ever on a torso with a lodged round in it. Four things
were wrong, and all four are fixed:

- **The two hand-only treatments are surgeries now.** `SurgeryRemoveEmbeddedObjects` (open incision ->
  `SurgeryStepExtractEmbedded` -> seal) and `SurgeryRelocateJoint` (one toolless step) call
  `WolfmedEmbeddedRemovalSystem.TryRemoveOne` and `WolfmedDislocationSystem.TryRelocate`, the very code the
  hand do-afters call, so the pod leaves what a medic's hemostat would leave and the verbs are untouched.
  Both are base-library programs; the extraction lists only while the part still holds something and the
  relocation only while the limb carries a `WolfmedDislocationWound`.
- **The tend check read the whole body (HOOK 26).** Shitmed's `OnTendWoundsCheck` cancels while the BODY has
  damage of the group, so tending a torso could never finish while a hand had a scratch. On a wound host the
  check reads the part, and reads its wounds rather than its damage figure. HOOK 24 got the same treatment on
  the listing side, which is what stopped the planner queueing a tend on every limb.
- **A tend pass is worth the incision (HOOK 27).** The step's damage removal reached a wound through the
  routing's `HealingMultiplier`, a tenth of what came off; tending now treats the part's matching wounds
  directly at `wolfmed.surgery_tend_strength` (15) a pass, the way a suture does, so a moderate cut closes in
  two or three passes. Wounds nothing closes and wounds refusing treatment are untouched, because
  `TreatWound` raises the same attempt event a dressing does.
- **The stall guard.** A step whose completion check still fails and whose part looks exactly as it did after
  the previous run made no progress; `wolfmed.autodoc_step_retries` (3) of those and the pod says
  "THIS IS NOT WORKING.", drops the procedure, records it in `FailedProcedures` for this occupant, closes the
  patient if it was the pod that opened them, and moves on. The signature deliberately leaves out pain and
  bleed rates, which move on their own every tick.
- **The planner.** It never queues anything in `FailedProcedures`, it puts `SurgeryRemoveEmbeddedObjects`
  first after the defib and `SurgeryRelocateJoint` before the bones, and it queues nothing else on a part
  that still has something lodged in it. AUTO re-plans at most `AutoReplanLimit` (2) more times against an
  unchanged body and then idles with "NOTHING MORE I CAN DO."

### What else the playtest found (AUTODOC4)

- **One anaesthetic per queue, not per procedure.** A twenty-item plan used to push 15u of opiate twenty
  times, walking sedation to 100% and stopping the patient's breathing. The pod doses once for the run, tops
  up only when the painkiller has under `AnaestheticTopUp` (20 s) left, never past
  `wolfmed.autodoc_sedation_cap` (0.5, against a 0.6 depression threshold) and says "SEDATION AT LIMIT."
  instead. While the anaesthetic is in them the occupant is held under with `ForcedSleepingComponent`, which
  is what silences Shitmed's surgery scream, and is woken when the queue ends, aborts or they leave. Past the
  threshold the pod pushes 5u of a dexalin-class chem if the reservoir holds one.
- **AUTO stopped eating hand-written queues.** `TryPlan` no longer clears the queue when the plan is empty,
  and the module runs a queue somebody typed instead of replacing it every few seconds.
- **The reservoir takes bottles and jugs.** The three slots whitelisted `FitsInDispenser` only, and the code
  read only that solution; both now fall back to `DrainableSolution`.
- **A clothed patient no longer faults the pod.** `StepInvalidReason.Armor` is answered with
  "REMOVE YOUR CLOTHING." and a retry every tick, not with `Fault`.
- **A damaged brain has a procedure.** `SurgeryRepairBrain` was gated on a destroyed brain, so a patient at
  three per cent brain tissue had nothing listed at all; the condition takes `anyDamage: true` now, and the
  analyzer calls anything under 60% brain damage and anything under 25% critical.

## Pod blood, clothing, residual damage, UI fit (AUTODOC5, 2026-09-22)

- **Downed is a transition, not a state that can flicker.** Consciousness adds and removes
  `WolfmedDownedComponent` as its inputs cross the threshold, and the component downed and stood the body on
  every add and remove: on the edge, where a stun leaves you, that was a body-fall sound several times a
  second. Downed now only downs a body that is standing with no knockdown on it, only stands one that is
  down with no knockdown left, and `WolfmedConsciousnessComponent.DownedUntil` keeps a body on the floor for
  at least two seconds whatever the inputs do. Hysteresis is a band and the inputs cross it; a dwell is not.
  The dwell needs `WasUp`: a body still being assembled has no legs yet, which reads as both legs gone, and
  holding one down for two seconds before it ever stood up broke every test that spawns a mob and uses it in
  the same breath.
- **Going down drops what you were holding**, the way a knockdown does. Run from `WolfmedDownedSystem`'s
  Update on the first tick of being down, not from the component's startup: startup runs inside the
  consciousness evaluation, where a hand's container will not give its item up. Picking things back up while
  Downed is still allowed, so the CONSC rule "self only" is unchanged.
- **A wound does not own the damage it was made from.** The part's `DamageableComponent` carries it and the
  body totals every part, so surgery that closes a wound directly (HOOK 27's tend, embedded removal) left
  ghost brute nothing but a brute pack could clear. `WolfmedWoundDamageSyncSystem` lowers each wound-backed
  damage type on a part to what its remaining wounds account for, and never raises one, so damage nothing
  models is untouched and no path can invent healing.
- **The pod cuts clothing rather than waiting on it for ever.** AUTODOC4 had it ask and retry, which is a
  deadlock when there is nobody to ask. A conscious patient is told to undress or press CUT; one who cannot
  is cut for by AUTO after five seconds. Only the outer layer and the jumpsuit, which are the slots the
  surgery access rules read - the pod has no business with an ID or a backpack.
- **A BoxContainer that runs out of room clamps its last children to zero.** That is why the window's
  headless test passed while the owner's bottom row was missing: nothing can hang below the window, it just
  stops existing. The controls row moved onto the window's own column so the layout serves it before the
  body, and the test now measures the row's height at two UI scales instead of asking about overflow.
- **A corpse gets the flat defibrillator chance once it has been repaired.** The oxygenation scaling is what
  makes speed matter on an arrested body still on the clock; a dead body has no circulation and therefore no
  way to raise that number, so the medic who repaired the brain and put the blood back was being told "no
  response" at 13% a shock for a reason nothing on the body showed. CPR on a corpse now also refills
  oxygenation slowly, because chest compressions circulate.
- **"Charging again" has to be true.** The pod retries a failed shock every five seconds up to
  `wolfmed.autodoc_defib_attempts`, then says it cannot restart the heart. A gate refusal (no blood, brain
  damage) never charges at all: it says which gate, once, and again only when the reason changes.
- **Asphyxiation is capped at `wolfmed.airloss_cap` (200); Bloodloss is not.** The body damage cap only ever
  saw part damage, so suffocation counted past 700 on a body that cannot die of the number. Bloodloss is left
  alone because decapitation and the other vital losses deal a fixed lethal figure through it. BRAIN's hypoxia clock carries the
  lethality; the reading stops at the old death line, which is also what stops a blood pack being spent on
  bloodloss damage that could never come down.
- **One close chain serves every part, so its bone step is named for no part.** "Mend ribcage" read wrong on
  a head. Renamed "Mend bone" rather than split into a per-part chain: `SurgeryCloseIncision` is named as a
  requirement by a dozen surgeries and splitting it is a change of its own shape.

## Synthetic HUD (2026-09-22)

A mechanical body gets its own damage presentation. The organic red vignette and the dying view are gated
off for it (`WolfmedSyntheticHudOverlaySystem.OwnsView`, checked in `DamageOverlay` and in four places in
`WolfmedDyingEffectsSystem`); organic bodies and non-wound-host silicons are untouched.

- **A body is mechanical when its torso is.** `WolfmedSyntheticHudSystem.IsMechanicalBody` asks W6's
  `WolfmedWoundTraitSystem.IsMechanical` about the torso only, so a human with one cybernetic arm is still
  flesh and still gets the vignette.
- **Pushed, not pulled.** Wound entities are server-only, so the server fills a networked
  `WolfmedSyntheticHudComponent` (faults, integrity, fluid, power, servos, shutdown, core, advice) twice a
  second and dirties it only when something moved. Twelve lines maximum.
- **Every line is data.** `syntheticHudLine` prototypes map a wound id or a condition to a locale line, a
  tag, an escalation severity and a one-line advice; an unmapped wound whose `analyzerCategory` is
  `Mechanical` hits the fallback row. Nothing in the overlay hardcodes a fault string.
- **Newest first.** A fault the readout did not carry last tick goes to the top, worst tag leading;
  everything already on screen keeps its order. The advice line is always the top fault's.
- **Four tiers off one number.** `Strain` is the worse of CONSC's `Depth` and lost chassis integrity;
  0.08 / 0.25 / 0.55 separate Idle (a corner glyph), Light (SYSTEM block), Moderate (DIAGNOSTICS and a
  still cyan rim) and Heavy (pulsing rim, one-pixel jitter, an occasional torn slice, and the integrity
  banner). Downed, shutdown/unconscious and dead override the banner with crawl mode, standby and a kernel
  panic into `CORE OFFLINE`.
- **Integrity is the parts, not the damage total.** Each part is read against its own amputation
  thresholds, torso and head weighted double, so losing an arm costs less than a cracked chassis.
- **It can never take a click.** The readout is a plain screen-space `Overlay` with no controls at all, and
  `WolfmedSyntheticHudLayout` keeps its three blocks in the top 32 % of the screen, clear of the hotbar,
  alerts column, chat pane and targeting doll at 1920x1080 and 1280x720.
- **Reduced motion and two CVars.** `accessibility.reduced_motion` drops the slide-in, the jitter and the
  glitch and leaves the tint; `wolfmed.synthetic_hud` puts a chassis back on the organic presentation and
  `wolfmed.synthetic_hud_scale` sizes the text.
