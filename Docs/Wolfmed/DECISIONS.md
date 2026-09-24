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
  *[M6 correction: closed by OD18 (a). One part hit of `wolfmed.doafter_interrupt_damage` (10) or more cancels the hit
  body's treatment, surgery and break-on-damage do-afters; ticks and systemic damage do not. See "M6".]*
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
  *[M6 correction: the CONSC report's "IPCs have no pain" was wrong; this entry is right. Since M1a (OD9) pain only
  Downs a machine: it never faints one or holds one under.]*
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
  *[M6 correction: the lodged round is a roll, not a certainty: 35 % at 18 Piercing or more
  (`WolfmedRuleLodgedRoundHeavy`) and 20 % from 7 to 18 (`WolfmedRuleLodgedRound`), kept as flavour by OD15. The crush
  internal bleed became a band in M3: every Blunt hit of 40 or more.]*
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
  *[M6 correction: a leak costs more than the oil. Consciousness reads oil as the blood input: 50 % or less Downs the
  chassis and 35 % or less shuts it down, cause Oil ("HYDRAULIC PRESSURE LOW", M1a).]*
- **Body damage ceiling.** `wolfmed.body_damage_cap` (default 600, absolute, 0 disables). Routed part damage past it is discarded (`WolfmedBodyPartSystem.ClampToBodyCap`, one marked hook in `WoundDamageRoutingSystem`). Absolute rather than a multiple of the dead threshold because an IPC dies at 100 while its limbs come off near 200. Infection and sepsis no longer damage corpses.
  *[M6 correction: the ceiling only ever applied to damage with no origin that was not an explosion, and M1b replaced
  it: a per-part ceiling (0.8 of the part's lowest destruction trigger) for that damage on the living, with the cut
  still carried as the hit's Overflow; 600 is now a corpse ceiling. An IPC does not die at 100: damage totals decide
  nothing on a wound host; a chassis dies of core failure, losing its core or head, or a gib.]*
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
  *[M6 correction: the shipped values are 40 per part and 120 per body. IPCs do not die of damage totals, so no number
  of EMPs kills one by itself; the Shock lands on the parts (pain, short circuits) and reaches the core only through the
  torso's reach line (M3).]*


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
*[M6 correction: the shipped clamps are 0.1 worst and 0.75 best, as "Dying view" below records.]*

## Blast dismemberment (2026-09-20)

Onyx's explosion severing reads one limb's share of the blast against that limb's totals, so it almost never fires. After a
blast is routed, `WolfmedExplosionSystem.TryBlastDismember` rolls on the whole blast (armour already applied): chance per limb =
(total - `wolfmed.blast_dismember_min` 30) / (`wolfmed.blast_dismember_full` 150 - min) x `wolfmed.blast_dismember_chance` 0.8,
rolling 1 + total/full random limbs through `AmputationSystem.TryAmputate`. Torso never; head only with
`wolfmed.blast_dismember_head`. `wolfmed.blast_dismember` turns it off. Test: `WolfmedExplosionTest.BlastSizeRollsLimbsOffTest`.
*[M6 correction: until M3 Onyx's own per-part explosion roll could still take the head with the CVar off (P27). M3's
veto in `AmputationSystem.HandlePartDamageApplied` makes this sentence true (`BlastHeadTest`).]*

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
  *[M6 correction: the shipped lines are pain 0.95 and 1.4 of the soft cap (`wolfmed.consc_pain_down`,
  `wolfmed.consc_pain_out`) and blood 0.5 and 0.35 (`wolfmed.consc_blood_down`, `wolfmed.consc_blood_out`). Since M1a
  the 1.4 pain line starts a bounded faint (20 s) rather than holding anybody Unconscious.]*
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
  run. *[M6 correction: no longer the only one. The defibrillator (BRAIN, the hand paddles and the pod) and the IPC and
  synth restart button (M2) revive a wound host too.]*

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
  ends it. *[M6 correction: the pain-shock arrest is gone (`wolfmed.arrest_shock_blood` 0, M1a), the sepsis roll is gone
  (`wolfmed.arrest_sepsis_chance` 0: sepsis drains the brain and stops the heart through the oxygen trigger, M2), and
  the electrocution reads the shock after insulation (M2). Cold, toxins and heat stroke stop the heart too (M5).]*
- **The clock** (`WolfmedBrainComponent` on the brain organ, networked, `Oxygenation` 1 to 0). Drains by the
  worst of: heart stopped (full drain in `wolfmed.brain_arrest_seconds` 120 s), not breathing (airloss over
  the old crit threshold, or a sedation overdose, full drain in 180 s at 1.0), blood under 0.5 (linear to a
  full drain in 300 s at 0.3), late sepsis (600 s). Refills at the arrest rate x `wolfmed.brain_refill_factor`
  (0.5) when nothing drains. Multipliers: cold (`WolfmedBrainComponent.ColdSteps`, x0.5 under 30 C and x0.1
  under 20 C, scaled by `wolfmed.brain_cold_factor`), CPR (x0.25, and CPR answers for breathing and counts
  blood as at least 0.5), stimulants (x0.6). Under `wolfmed.brain_damage_oxygenation` (0.4) the brain ORGAN
  takes `wolfmed.brain_damage_rate` (0.1/s at zero oxygenation, linear from the threshold) of irreversible
  organ damage, and organ health 0 is death on the existing `OrganHealthSystem` path.
  *[M6 correction: "not breathing" read the whole Airloss group, Bloodloss included (P7), not airloss and sedation.
  M1a replaced it with real suffocation (the respirator's own reading) and sedation's depression; M3 added damaged
  lungs, M5 the toxic coma and heat stroke drains. The refill runs whenever no drain does.]*
- **Real timings, which are not the spec's own arithmetic.** With the rule as written (damage under 0.4
  oxygenation, 0.1/s at zero) an untreated arrest reaches 0.4 at 72 s, zero at 120 s, and brain death at
  about 246 s, not the 198 s the spec's summary line quoted. Under CPR the brain never reaches the damage
  band inside ten minutes. The rule was implemented; the illustrative figure was not.
  *[M6 correction: under CPR it does. The death rundown derives the damage band at about 289 s and catastrophic brain
  injury at about 534 s with CPR throughout.]*
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
  costs the zap damage and can be tried again. *[M6 correction: the gate is `wolfmed.defib_blood` 0.25 and a shock
  leaves `wolfmed.post_shock_oxygenation` 0.5 with a 45 s grace, since M1a (OD6); the refusals are shared with the pod
  in `WolfmedRevivalSystem.GetRefusal`.]*
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
  *[M6 correction: it also charges again every 5 s after a failed shock, up to `wolfmed.autodoc_defib_attempts` (5),
  as AUTODOC5 records.]*
- **Mechanical bodies have no clock.** `WolfmedShutdownComponent` (pressure `"shutdown"`) when the cell is
  pulled or flat (`SiliconChargeDeathEvent`) or the micro pump is destroyed or removed. "Mechanical" is a
  wound host that carries `SiliconComponent` AND runs no clock, not merely a body with no brain: the second
  half alone is the brainless-poll bug in a different costume and it shut down every brainless fixture. That is not death and
  nothing runs out. `PositronicBrain` and `OrganIPCPump` gained `WolfmedOrgan` + `OrganDamage`, so a
  destroyed positronic brain is death on the same organ path a fleshy brain uses, and the way back is the
  same: repair the brain, then a jolt. *[M6 correction: no surgery reached a positronic brain until M2. The way back is
  core repair (`SurgeryRepairCore` on an IPC's chassis, `SurgeryRepairSynthCore` on a synth's head), then the restart
  button, not a defibrillator. Since M6 the pod runs both with the neuro disk.]*
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
  Downed is still allowed, so the CONSC rule "self only" is unchanged. *[M6 correction: it was not; the interaction
  block refused the floor (P16). Since M1a a Downed body picks up loose items within `wolfmed.downed_reach` (1.5 m).]*
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
  bloodloss damage that could never come down. *[M6 correction: that figure no longer kills anything. Losing a vital
  part is death through the amputation handler (the brain's part, or the last head), and Bloodloss damage decides
  nothing on a wound host; blood volume is the route.]*
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

## Playtest fixes: IPC decapitation, readout placement (2026-09-22)

- **A vital part taken off is death.** `WolfmedLifeSystem` killed a body only when the severed part
  carried a brain. An IPC keeps its positronic brain in the torso, so a decapitated chassis walked on.
  The prototype already calls a head vital (`BodyPartComponent.IsVital`, which upstream only ever turned
  into bloodloss damage an inorganic damage container does not carry), so the amputation handler now also
  kills a body that has lost its last part of a vital type. Taking the positronic brain out by surgery was
  already death on the organ path and now has a test.
- **The readout drew a viewport away from its corners.** `OverlayDrawArgs.ViewportBounds` is the viewport
  control's draw box in global physical pixels, while a screen-space overlay's handle is already
  translated to that control's top-left. `WolfmedSyntheticHudLayout.Screen` takes the global origin back
  off and `.Scale` multiplies the player's text setting by the control's UI scale, because those pixels
  are physical ones. `WolfmedDeathBannerOverlay` read the same bounds the same wrong way and is fixed with
  it. The fault list is now also cut to `MaxLines`, so a large text scale shortens the list instead of
  running it under the game HUD.
- **STANDBY over a walking machine.** Two causes, both fixed. `MobIPC` had no `Critical` state, so CONSC's
  Unconscious could never reach the mob state: pain or a pulled pump left the chassis on its feet with the
  readout saying STANDBY. Critical is back in `allowedStates` (marked); `MobThresholds` still names no
  Critical rung, so damage totals cannot put it there. And `WolfmedShutdownSystem.Refresh` skipped writing
  the pressure whenever the flag already agreed, so anything that clears every pressure (a rejuvenate)
  left the flag behind: it now writes both halves every time and re-reads the cause once a second for
  bodies that are already down.

## Playtest fixes: defib on arrest, autofix stops (2026-09-22)

Four owner findings from the Wolfmed playtest.

- **Nothing would shock an arrested patient.** `wolfmed.defib_blood` (0.40) sat above every arrest trigger
  that involves blood (arrest at 0.30, a pain shock under 0.50), so the patients who arrest in practice were
  all refused at the gate. The gate is now strictly under the threshold, the refusal is popped up to the
  medic as well as spoken, and the pod transfuses out of its reservoir before it charges and again whenever
  blood is what is blocking it. A plain arrest with blood in the body was always shockable; nobody had one.
- **AUTO looped.** Surgery leaves an incision, a suture and a cautery burn behind it, so the body always
  looked different and always had "work" on it. Wounds that appear while the pod operates carry
  `WolfmedPodWoundComponent`; the body signature ignores them, and a triage step marked `ignorePodWounds`
  skips a part whose every wound the pod made. QUEUE COMPLETE is spoken once per run.
- **The pod held for a patient it was asked to operate on because they were dead.** Repairing a brain means
  operating on a corpse. The vitals hold now only fires for a death that happens mid-run, says
  "PATIENT IS DEAD. PROCEEDING." once for one that was already dead, and marks the death as handled so the
  operator's RESUME carries on instead of stopping again on the same body.
- **The queue reorder buttons did nothing.** The window worked out what could move from the row it was
  drawing rather than from the pod's state, so the buttons around the running procedure were live and the
  server threw away every message they sent. `AutodocQueueRules.FirstMovable` is the one rule both ends use.

## M1a B: causes, faint and alerts (2026-09-23)

Plan: `WOLFMED_DEATH_PLAN.md` §5.1, §5.2 (without the explanation card and the sedation warnings), §3.1, §2.3,
§3.11 (M1a parts), §5.6.

**What was built.**
- **Cause and Blockers.** `WolfmedConsciousnessComponent` carries networked `Cause` (`WolfmedCause`),
  `CauseSource` (`WolfmedCauseSource`: the hypoxia drain, the arrest trigger, the shutdown reason) and
  `Blockers` (`WolfmedCauseFlags`, one bit per cause). `Evaluate` keeps every input under its cause (pain,
  pain faint, blood or oil, one per pressure key, legs, crash) and picks the cause with the plan's tie order
  (Arrest > Shutdown > Blood > Oil > Hypoxia > Sedation > Other > PainFaint > Pain > Legs > Crash). Critical
  states read the Unconscious line, Downed the Downed line. `Apply` raises `WolfmedConsciousnessChangedEvent`
  on any change of state, cause or blockers.
- **How a cause is picked, precisely.** An input past its line (level ≥ 1) names the state before one that is
  only inside its leave band (≥ 0.9). Everything at or past its leave line, other than the cause, is a
  blocker. So a faint at 40% blood is Cause PainFaint, Blockers Blood (blood is in its band); at 34% blood the
  cause becomes Blood and the faint is the blocker. The plan's `OverlappingCausesTest` reads this way.
- **Pain faint** (plan §3.1). Summed part pain at or over the faint line (1.4 × 135 = 189) faints for
  `wolfmed.pain_faint_seconds` 20; nothing extends it. Waking records the summed pain as the baseline, starts
  `wolfmed.pain_faint_cooldown` 30 and disarms. It re-arms under the leave line (170.1) or on a rise of
  `wolfmed.pain_faint_rise` 40 over the baseline, never inside the cooldown. A Strong or Emergency dose ends a
  faint and blocks the next (`WolfmedPainReliefSystem.EndsFaint`, which reads the doses, so a stimulant on top
  of an opiate still counts). Mechanical bodies never faint. Pain no longer holds anybody Unconscious; only the
  faint does, and it is `MobState.Critical` and breathing (package A).
- **One pain number (P13).** Marked `PainSystem.SetPain` edit: a body's pain is min(soft cap, Σ parts) after
  every change, and a direct set on the body is rederived from the parts. Suppression shares are part ÷ Σ
  parts, so the body's suppression comes off once. The Downed test still reads the capped body value less
  relief.
- **Pain shock and adrenaline (OD5).** The shock's constants are CVars (`wolfmed.pain_shock_threshold` 130,
  `wolfmed.pain_shock_rearm` 110, `wolfmed.adrenaline_seconds` 30, `wolfmed.adrenaline_crawl_multiplier` 1.5).
  `GetPain` no longer multiplies by 0.7, so adrenaline stands nobody up; while it runs a Downed body crawls
  ×1.5 and loses the 1.5× do-after penalty (`WolfmedDownedSystem`). Start and end are told to the patient
  through the broadcast `WolfmedAdrenalineEvent` raised by `WolfmedBodyPainSystem`, the `_WF` half of the edit.
- **Alerts.** `MobThresholdSystem.SetTriggersAlerts` (marked, beside `SetAllowRevives`) turns the stock health
  alerts off on a wound host at startup when `wolfmed.consciousness` is on. `WolfmedConditionAlertSystem` then
  owns the Health category: the stock Alive alert (`HumanHealth`/`BorgHealth`) while Up with its severity from
  the worst Downed-level input, a per-cause alert while Downed or Critical, and the stock Dead alert. Bodies
  that are not wound hosts are untouched. `HumanCrit` and `HumanHealth` readers were grepped: the thresholds'
  own dictionary and `PainNumbnessSystem` only.
- **Pain numbness decision.** The Up doll does not raise `BeforeAlertSeverityCheckEvent`. Its one reader,
  pain numbness, pins the doll at full health, which would hide blood loss too; numbness already zeroes pain
  in the inputs the severity reads.
- **Lines.** One popup plus a chat line (Notifications channel) per transition: down, out (faint, arrest and
  shutdown included), waking, standing, the heart restarting ("… You are still held down by {cause}: {help}"),
  adrenaline start and end. Every line that names a cause and a state adds "Still holding you down: …" while
  Blockers is not empty. The last line is kept in `LastConditionLine` for tests and admins.
- **Cause prototypes.** `wolfmedConsciousnessCause`, one per M1a cause in
  `Resources/Prototypes/_WF/Wolfmed/Consciousness/causes.yml`, id = enum name; text in `consciousness.ftl`.
- **IPC.** `WolfmedShutdownComponent.Reason` (Power or Pump, networked) is set in `WolfmedShutdownSystem.Refresh`;
  an empty cell wins when both are missing. The blood input on a mechanical body is cause Oil. The synthetic
  HUD's banner is the cause's `syntheticHudLine` (`WolfmedSyntheticHudComponent.CauseLine`) instead of the
  blanket STANDBY: "CELL EMPTY: SHUTDOWN. AWAITING POWER.", "COOLANT PUMP OFFLINE: SHUTDOWN",
  "HYDRAULIC PRESSURE LOW", "MOBILITY LOST: FRAME DAMAGE", "MOBILITY LOST: ACTUATORS OFFLINE". The crit
  heartbeat is silent for any body with the synthetic readout and for a faint.

**Differs from the plan, and why.**
- The plan calls `PainFaintUntil` and `PainFaintArmedBelow` existing fields; neither existed. All four faint
  fields are new: `PainFaintUntil`, `PainFaintArmed` (a flag rather than a stored line), `PainFaintBaseline`,
  `PainFaintCooldownUntil`.
- Hypoxia sub-sources are Airway, Lungs, Circulation, Sepsis and Sedation: the drains that exist in M1a
  (blood below 50% and sedation depression are drains too). Toxin and Heat arrive with M5.
- `WolfmedCause.Other` names a pressure key no cause claims (admin and test keys); stock alerts, generic lines.
- Alert tooltips are static in the engine, so they cannot change with the blockers. Every alert is written
  conditionally ("… unless something else is holding you down") and clicking one (`WolfmedConditionAlertEvent`)
  pops up and prints the full text with the blockers; the transition lines carry the blockers too.
- The prototype has Downed and Critical forms of the help (`help`/`helpBlocked`, `helpOut`/`helpOutBlocked`),
  titles, and a mechanical Downed alert and help for pain (`WolfmedDownedFrame`: painkillers do nothing for a
  chassis). The plan's field list had one `help`.
- Sedation plus hypoxia: with air back and the overdose still in the body, the cause stays Hypoxia with source
  Sedation, not Sedation, because depression is its own brain drain (plan §3.4) and keeps the brain under the
  hypoxic line. The text names the overdose ("no oxygen (breathing slowed)", blocker "overdose"). When the dose
  ends the patient wakes as sedation falls and the brain refills: 59 s in the test.
- The arrest help does not yet say "You can choose to let go": Succumb is package C's.
- The heartbeat gate reads the synthetic HUD component rather than `OwnsView`, which is false when the player
  turned the readout off.

**Numbers.** Faint 20 s, rise 40, cooldown 30; shock 130 / re-arm 110; adrenaline 30 s, crawl ×1.5; the faint
line stays `wolfmed.consc_pain_out` 1.4. Measured: `SustainedFireFaintTest` (10-stack fire, Blunt 6 every 2 s,
2 min) spent 18.5 s Critical in one faint, Downed for the rest, so the 30 s cooldown holds the 40 s budget
without the 50 s fallback. IPC 10-stack fire (M4 input, `IpcShutdownScenarioTest` output): chassis peak 1112 K
at about 50 s, about 155 s above 383 K and about 125 s above 500 K.

**Art debt.** Every new alert reuses an icon: `downed.rsi` for the Downed ones, the stock critical, bleed,
breathing, dead and borg-critical icons for the rest.

**Test fixtures that changed with the faint.** Pain past 189 now faints where the pain shock's 0.7 adrenaline
discount used to keep it under: `WolfmedAutodocLoopTest.LongQueueDosesOnceAndWakesThePatientTest` (two broken legs)
is made pain-numb, and package A's `PainShockNoArrestTest` hits the head with 40 instead of 60 so the shock fires on
a conscious body (a fainted, Critical body takes no paralysis).

**Package A's state.** Package A stopped before committing; its work was committed unchanged as a `wip:` checkpoint
before B started, and B builds on it. A has no report or DECISIONS section of its own.

**Left for later packages.** The analyzer's "FAINTED: pain" and "SHUTDOWN: no power" lines (D; the scenario
tests assert the patient's titles instead). Succumb in the scenario tests (C). The synthetic HUD's pain/sensor
and core-temperature rows (§5.6) were not added.

## M1a C: honest endings and crawling (2026-09-23)

Plan: `WOLFMED_DEATH_PLAN.md` §5.4, §5.3, §2.2, and the autodoc rows of §7.2.

**What was built.**
- **Crit actions stripped** (§5.4 item 1). The new shared `WolfmedCritActionsSystem` removes the `MobState.Critical` entry
  from `MobStateActionsComponent.Actions` at `ComponentStartup` on wound hosts while `wolfmed.consciousness` is on,
  on both client and server. It removes the whole list, so Fake Death goes with it; Play dead is M2 (OD20). The
  `MobStateActionsComponent` + `ComponentStartup` pair was grepped and is free. The list is replaced with a copy
  rather than edited in place.
- **Succumb and Last Words** (§5.4 items 2-4, OD2 (a), OD3). `ActionWolfmedSuccumb` and `ActionWolfmedLastWords`
  are in `Actions/dying.yml` and raise the new `WolfmedSuccumbActionEvent` and `WolfmedLastWordsActionEvent`
  (Last Words' cap is `maxLength: 30` on the event). `WolfmedDyingActionsSystem` grants them from `StartArrest` and
  removes them from `EndArrest` and from consciousness's death handler. All three are direct calls. Both actions
  open the Succumb dialog. Last Words first whispers the capped text with "..." added. On confirm, the brain
  organ's health goes to 0 where it sits, then `Kill`, then `EndArrest` on the corpse, then
  `OnGhostAttempt(canReturnGlobal: true)`. The body is dead by then, so the ghost is returnable. The mind stays
  owned by the body, and brain repair plus a defibrillator brings the same person back.
- **The dialogs.** `WolfmedChoiceEui` (server) and its client window show the exact text with two buttons. Closing
  the window counts as no. Wording is in `death.ftl` ([OD1 wording], OD1 (b)): "Let go?" / "Let go" / "Keep
  fighting", naming the arrest cause and the minutes until the body rots (from `PerishableComponent`; a body
  that does not rot gets the text without the time). The "Leave your body?" dialog reads "Your character will be
  left alive but empty. You cannot return to this body." with "Leave" / "Stay". A dialog still open when the
  heart restarts is withdrawn. A "leave" confirmed after the body has gone into arrest opens Succumb instead.
- **The ghost command** (§5.4 item 5). The marked `GhostSystem.OnGhostAttempt` hook hands a wound host's own
  `ghost` command (`viaCommand && !forced && canReturnGlobal`) to `WolfmedDyingActionsSystem.TryOpenGhostDialog`.
  In arrest that is the Succumb dialog. In any other living state it is the leave dialog, which ghosts with
  `canReturn = false` and does not touch the body. The hook returns true, so the command prints no "denied"
  (the plan's "to confirm": `GhostCommand` prints the denial only on false). A second marked condition keeps a
  wound host out of the kill-crit branch altogether, so the Asphyxiation top-up never applies and a Critical
  wound host never gets upstream's free returnable ghost.
- **Pod return prompt** (§5.4 item 6). `WolfmedRevivalSystem.OfferReturn` opens the stock `ReturnToBodyEui` for
  a ghost whose body the pod has just revived. `AutodocSystem.TryDefibrillateOccupant` calls it on success. The
  hand defibrillator already opened the prompt.
- **Pickup within reach** (§5.3, P16, OD7 (b)). `WolfmedDownedSystem.OnInteractionAttempt` also lets a Downed body
  reach an `ItemComponent` that is not anchored, not in a container, and within `wolfmed.downed_reach` (1.5 m:
  the body's own tile and the neighbouring ones, diagonals included). Guns still cannot fire and throwing is
  still blocked.
- **Call for help** (§5.3). The `ActionWolfmedCallForHelp` action (`Actions/downed.yml`) exists only while
  Downed. `WolfmedCallForHelpSystem.Refresh` is called directly from consciousness's `Apply` and death handler. The
  action opens a one-line dialog; an empty line shouts "Help! I'm down!". The line is capped at `maxLength: 60`
  on the event and said aloud at normal range, where the exclamation makes it a shout. The call sets
  `WolfmedCallForHelpComponent.FlagUntil` for `wolfmed.call_for_help_seconds` 60 and `CooldownUntil` for
  `wolfmed.call_for_help_cooldown` 30. Both fields are networked. The client's `WolfmedCallForHelpIconSystem`
  shows `HealthIconWolfmedCallForHelp` to anyone with a medical HUD while the flag lasts. The flag outlasts
  Downed on purpose, so a caller who goes under is still marked.
- **Autodoc and faints** (§7.2). `WolfmedConsciousnessSystem.InFaint` means Critical with cause PainFaint; the
  head blow joins it in M3. `MaintainAnaesthesia` now anaesthetises a fainted occupant, and `GetAlarm` does not
  raise `AutodocAlarm.Critical` for a faint. Rot, `Unrevivable` and no heart were already in the shared
  `GetRefusal` (package A), so the pod refuses in the hand defibrillator's words; the tests now pin it.
- **Arrest text.** The arrest help and alert now end with "You can choose to let go." Package B had held this
  back until Succumb existed.

**Differs from the plan, and why.**
- **Succumb order.** The plan's order is `EndArrest`, then `Kill`. This build runs `Kill` first and then ends
  the arrest on the corpse. Ending the arrest on a living body re-evaluates it for a moment as having a heartbeat.
  That changes its cause and plays "Your heart lurches back into rhythm" to a patient who is letting go. The end
  state is the one the plan asks for: Dead, brain at 0, no arrest component. The plan's real constraint, that
  `Kill` runs before `OrganHealthSystem`'s next update, still holds.
- **Hook placement.** The plan puts the whole hook inside the kill-crit branch. The dialog is placed earlier
  instead: after upstream's `PreventGhosting` and `CanGhostInteract` checks and before `UnVisit`. From there it
  does not depend on the `ghost.killcrit` CVar and runs before any side effect. The branch itself gets a one-line
  "not a wound host" condition. Upstream's `GhostAttemptHandleEvent` would have needed no hook, but it carries no
  `viaCommand` or `forced`, and cryosleep calls the attempt with `viaCommand: true`, so it cannot tell the
  player's own command apart.
- **The dialog is a new EUI.** `QuickDialog` has no body text and no yes/no. Tests answer through
  `WolfmedDyingActionsSystem.Confirm`, the same method the window's buttons call.
- **"Editable" Call for help** is a one-line dialog each time with a default. The length caps (30 and 60) are
  fields on the action events, not CVars.
- **The HUD flag** is drawn by its own client system. It reads the medical HUD state through a `WolfmedHudActive`
  accessor added to the `_WF` partial of `ShowHealthIconsSystem`. Subclassing `EquipmentHudSystem` would
  duplicate its component subscriptions.

**Found on the way (not changed).**
- **Going Downed stuns briefly.** The fall adds `KnockedDown` and a short `Stunned` for about 2 s, and `Stunned`
  cancels every interaction, including reaching the body's own tile. `DownedPickupTest` waits it out.
- **A succumbed corpse bleeds faster.** It has no arrest component, so it bleeds passively at the full rate. A
  body that died of untreated arrest keeps the component and bleeds at ×0.25. Worth a look in M2, when corpse
  bleeding is revisited.
- **An upstream helper is broken.** `SharedHandsSystem.TrySelectEmptyHand` never selects anything, because it asks
  `IsHolding(null)`, which is always false. The test selects an empty hand itself.

**Numbers.** `wolfmed.downed_reach` 1.5 m, `wolfmed.call_for_help_seconds` 60, `wolfmed.call_for_help_cooldown`
30; Last Words 30 characters, a call 60 characters.

**Art debt.** Call for help reuses the stock critical health icon on the right, and the action uses the scream
icon. Succumb and Last Words use the upstream crit action icons.

**Left for later.** IPC thermal shutdown's Succumb (M4). The Succumb and leave dialogs have no client test that
presses their buttons; the EUI reaching the client is tested, and the answer is tested on the server.

## M1a A: breathing and the clock (2026-09-22; written up by package D)

Package A stopped before it wrote anything down; package B committed its tree unchanged as `8f71617774`. This
section records that work from the code and its comments, so the M1a record is complete. Plan: §4, §3.2, §3.3,
§7.1, §7.2 (M1a rows).

**What was built.**
- **Who breathes.** `WolfmedBreathingSystem` (server, `_WF/Wolfmed/Life`). The one marked hook in
  `RespiratorSystem` asks `BreathingSuppressed` instead of `IsIncapacitated`. A wound host stops breathing only
  when dead or in arrest. A body that is not a wound host, or any body while `wolfmed.consciousness` is off,
  keeps the upstream rule. The `DebrainedComponent` and gasp-in-arrest guards are untouched.
- **The breathing input (§4.3).** `SuffocationLevel` is 0 unless the respirator is suffocating now, and then it
  is Asphyxiation ÷ `wolfmed.airloss_full` (100). "Suffocating" means as many short cycles in a row as the
  respirator's own alert needs (`SuffocationCycleThreshold`), not one: a body getting its breath back dips
  under the line for a single cycle. Bloodloss is never read (P7). The refill runs whenever every live drain is
  0, however much Asphyxiation or Bloodloss is left.
- **Vital signs.** `Assess` gives what the chest is doing and why: None (dead, arrest, no brain, no lungs),
  Gasping (no air), Depressed (sedation past its line) or Normal. `GetBloodBand` gives the circulation band:
  pale at `wolfmed.blood_band_pale` 0.8, weak at the Downed line, barely palpable at the Unconscious line,
  none in arrest or death. Both are networked on `WolfmedConsciousnessComponent` (`Breathing`,
  `BreathingSource`, `BloodBand`); there is no new component. The life tick writes them and dirties them only
  on a change.
- **The post-shock course (§7.1).** A successful shock ends the arrest. On the first shock of an episode,
  oxygenation becomes max(current, `wolfmed.post_shock_oxygenation` 0.5); a body that had died gets exactly
  0.5. `WolfmedPostShockComponent` opens a `wolfmed.post_shock_grace_seconds` (45) grace that holds off the blood
  and oxygen triggers; the drains still run. Another success within `wolfmed.post_shock_repeat_seconds` (300)
  only restarts the heart. The patient comes round on consciousness's own lines, so a patient whose blood was
  not the cause is Downed at once. `RepairBrain` uses the same 0.5.
- **The two transfusion numbers.** `GetTransfusionGuidance`: N = units to `wolfmed.post_shock_blood_target`
  (35%) plus the current bleed rate × the grace; M = units to `wolfmed.brain_blood_start` (50%).
  `GetPostShockAdvice` feeds the analyzer's "Revived: … Transfuse ≈ N u within 45 s …; ≈ M u to 50% …" line
  (`WolfmedPostShockText`, shared).
- **Revival refusals (§7.2).** One `WolfmedRevivalSystem.GetRefusal` for the hand defibrillator and the pod: rot,
  other content's `Unrevivable`, no brain, a destroyed brain, no heart (only for a species whose body prototype
  carries a Wolfmed heart), pulse present, and the blood gate `wolfmed.defib_blood` 0.25 (strictly under).
  `LocalizeLine` puts the patient's numbers into the paddle line ("Shock refused: blood 24% … Transfuse ≈ 33 u
  first; ≈ 78 u to reach 50%"). No line says a shock will work.
- **Data.** `wolfmed.arrest_shock_blood` 0: the pain-shock arrest is gone.
- **P22.** `WoundInternalBleedingSystem` bleeds once a second, so each amount is well above FixedPoint2's 0.01
  step (marked Onyx edit).
- **Tests.** `Scenarios/WolfmedBreathingClockTest.cs`: `BleedingScenarioTest`, `RepeatedShockTest`,
  `PostShockOxygenTest`, `OxygenScenarioTest`, `InternalBleedTickTest`, `PainShockNoArrestTest`,
  `NonWoundHostCriticalStillDoesNotBreatheTest`. The scenario helper `WolfmedScenario` drives them. The
  migrations are in `WolfmedBrainTest`, `WolfmedArrestLooksDeadTest` and `WolfmedPlaytestFixesTest`.

**Differs from the plan, as far as the code shows.**
- The plan says `DefibrillatorSystem.cs` is not edited. A made one small marked edit there: the hand
  defibrillator's refusal is localised with the patient's numbers. The rot and `Unrevivable` checks stayed in
  the hand defibrillator, as the plan says, and are also in `GetRefusal`.
- "Suffocating" needs the respirator's alert threshold of short cycles, not `SuffocationCycles > 0` (above).

## M1a D: medic lines, conformance, closure (2026-09-23)

Plan: §5.5 (the M1a lines and the defib verdict), §1.5, §9.1, §11, §12 M1a.

**What was built.**
- **The analyzer's vitals block.** `WolfmedVitalsReport` (shared, networked) rides on
  `HealthAnalyzerWoundDiagnostics.Vitals`, one marked Onyx field. `WolfmedVitalsText` holds the words, shared so
  the panel and the tests read the same text. `HealthAnalyzerSystem.Vitals.cs` (server, `_WF`) builds it:
  - the state (Up, Downed, Faint, Unconscious, Arrest, Shutdown, Dead), cause, sub-source and blockers from
    consciousness;
  - breathing and the blood band read fresh from `WolfmedBreathingSystem.Assess` and
    `WolfmedLifeSystem.GetBloodBand`, not the networked copy the life tick writes once a second, so the pulse
    words always agree with the blood % on the same line;
  - blood %, its trend, and the units to the brain-safe line (`wolfmed.brain_blood_start`, 50%). The trend is
    net regeneration against every bleed; "falling fast" starts at a net loss of `wolfmed.analyzer_blood_fast`
    (new, 1 u/s);
  - the defib verdict, from the shared `GetRefusal`.
- **What it says.**
  - State: "DOWNED: blood loss", "FAINTED: pain", "UNCONSCIOUS: no oxygen (no air)", "CARDIAC ARREST: blood",
    "SHUTDOWN: no power", "DEAD: catastrophic brain injury" [OD1 wording]. Blockers follow as "(also: …)",
    minus the one an arrest already names.
  - Breathing: "normal", "depressed: sedation 72%", "none: no air, gasping", "none: cardiac arrest", "none: no
    working lungs".
  - Circulation: "pulse weak and rapid; blood 45%, rising; transfuse ≈ 14 u to 50%".
  - Verdict (Faint, Unconscious, arrest and dead only): "Defib: shock indicated", or "Defib: refused: …" with
    pulse present, "no heart, transplant first", "blood 24%, transfuse ≈ 33 u first (≈ 78 u to 50%)", "brain
    destroyed, brain repair surgery first", "no brain", "body decayed", or other content's reason. It never
    says "will work".
  - Machines: "ONLINE", "SHUTDOWN: no power" or "coolant pump offline", "DOWNED: frame damage" or "hydraulic
    pressure low", "CORE FAILURE"; "Cooling: pump running/offline" in place of breathing; "Hydraulics: oil
    100%, steady" in place of circulation; no verdict (the restart button is M2).
- **The panel.** The block is the first banner row of the wound tab. Its text is redrawn on every scan, and
  the row is rebuilt only when the state changes. The pod mounts the same panel.
- **Examine** (`WolfmedVisualInspectionSystem`) reads the networked `Breathing` and `BloodBand`:
  - "is not breathing": arrest, death, no lungs, no brain. It shows at a distance too while arrested, as
    before.
  - "is gasping for air"; "is breathing slowly and shallowly" for sedation past 0.6, which used to read "not
    breathing".
  - "looks pale" (≤ 80% blood), "is pale and clammy, with a weak, rapid pulse" (≤ 50%), "has a barely palpable
    pulse" (≤ 35%), "has no pulse" (dead).
  - These are close-examination findings only, like a hand on the neck. Machines get none of them.
- **Vitals on organ insertion.** Any organ going in now refreshes the vital signs (the heart already ticked the
  body). A body being assembled gets its heart before its lungs, so it read "not breathing: no lungs" for up to
  a second after spawning; the new examine test found it. A lung transplant now reads as breathing at once.
- **Synthetic HUD rows (§5.6, left by B).** SENSOR (the chassis's pain against its soft cap) and CORE (chassis
  temperature in K, the M4 core-heat input) in the SYSTEM block.
- **Species conformance, report mode (§9.1).** `WolfmedSpeciesConformanceTest.KnownGapsAreExactlyTheReportTest`
  spawns every round-start species. Checks:
  - it is a wound host;
  - organics: the brain has `WolfmedBrain` and `WolfmedOrgan`, heart and lungs have `WolfmedOrgan`, 29% blood
    arrests, and removing the brain kills;
  - machines: core and pump have `WolfmedOrgan`, pulling the cell shuts the chassis down, and removing the core
    kills.

  The result is 98 gaps across 32 species, checked in as `KnownGaps` in the test file and grouped as §9.2
  groups them. It matches §9.2's prediction, with two things §9.2 did not say:
  - ProtoThaven's lungs lack data too;
  - every protogen subspecies is group C.

  IPC and the full-data group (Human, Oni, Dwarf, Chitinid, Resomi, Thaven, Vox, Avali) conform.

**Differs from the plan, and why.**
- **The defib verdict ships in M1a.** The brief asks for it; §5.5 had put it in M2.
- **The analyzer reads breathing and the blood band fresh** rather than the networked field (above). Examine,
  which is shared code, reads the networked field as the plan says.
- **Not in M1a, and not built:** AVPU, blue lips, weeping burns, pupils. They are §5.5 signs outside M1a's
  scope list. A strong pulse is not an examine finding: adding it to every healthy body would have replaced
  "You see no injuries".
- **One new CVar** for the trend words: `wolfmed.analyzer_blood_fast`.
- **Test names.** The conformance test is class `WolfmedSpeciesConformanceTest`, method
  `KnownGapsAreExactlyTheReportTest`, because a C# method cannot share its class's name. `AnalyzerStateLinesTest`
  is in `Scenarios/WolfmedMedicLinesTest.cs`. Its hypoxic case lowers oxygenation directly; the airless
  "Breathing: none: no air" is asserted in `OxygenScenarioTest`.
- **The analyzer lines B and A left out are now asserted where the plan put them:** "DOWNED: blood loss" in
  `BleedingScenarioTest`, "UNCONSCIOUS: no oxygen" and "none: no air" in `OxygenScenarioTest`, "FAINTED: pain"
  in `PainScenarioTest`, "SHUTDOWN: no power" in `IpcShutdownScenarioTest`.
- **The self-aid "hold pressure on your own wound" (§3.2, "to confirm")** needs no new verb. It is gauze or a
  bandage on yourself, which a Downed body can already use at 1.5× time.

**Not done in M1a (found in the §11 audit).** The stock `LowOxygen` alert is not replaced on wound hosts by
"Can't breathe: {source}" (§3.3). The per-cause hypoxia alerts ("Short of breath", "Unconscious: no oxygen")
carry the cause instead. Replacing the respirator's own alert needs a marked upstream edit; it goes with M2's
alert work.

**Still red, not caused by M1a.** `WolfmedPlaytestFixesTest.AutofixRunsOnceAndThenOnlyForSomethingNewTest`
failed about half its solo runs before M1a as well: package B measured 3 of 5 at the base `8e4a5cd15b`. Two things
were mixed in it:
- **The vacuum.** The test map is a vacuum, and barotrauma kept landing new damage on the patient. The test now
  gives the map station air.
- **A real autodoc gap.** The failures left show it every time. In the runs that fail, the pod abandons
  `SurgeryStopBleeding` on the fractured arm (the stall guard). It then finishes its queue with its own incision
  still open, and the planner wants `SurgeryCloseIncision` there straight after QUEUE COMPLETE. The likely
  trigger is the crush rule's chance of an internal bleed at Blunt ≥ 30 (plan §8, M3 makes it deterministic).
  `TryQueueClosure` does not queue the closure at that moment. Accepting an open `IncisionOpenComponent` there
  was tried and did not change it, so it was reverted.
- **What is left for the autodoc work.** Find why the closure is not valid when the stop-bleeding procedure is
  abandoned, and why that procedure stalls on an internal bleed at all. The test's failure message now names
  the abandoned procedures, the state and the queue.

## M1a complete: what a player now experiences (2026-09-23)

M1a changes no damage model. It changes who breathes, what every state is called, and what the choices do.

- **Getting hurt.** Pain puts you on the floor (body pain ≥ 128.25) and knocks you out for at most 20 s when the
  summed pain crosses 189. Nothing extends a faint. Another needs a rise of 40 over the pain at waking and
  never comes within 30 s. A strong or emergency painkiller ends a faint. A pain shock (130) drops and stuns
  you and gives 30 s of adrenaline: crawl ×1.5 and no do-after penalty. It no longer stands you up or stops a
  bleeding heart. Two minutes of fire and blows cost 18.5 s of unconsciousness. Pain never kills. Machines are
  only ever Downed by damage.
- **Unconscious bodies breathe.** Only arrest and death stop the chest. The brain reads real suffocation (no
  air, no lungs), never Bloodloss. With no air: Downed at about 188 s, Unconscious at about 205 s, arrest at
  about 259 s. Air back at the Unconscious line wakes you in about 7 s.
- **Bleeding.** Downed at 50%, Unconscious at 35% and still breathing, arrest at 30%. The defibrillator refuses
  under 25% and says how much blood to give. A shock gives oxygenation 0.5 and a 45 s grace. The analyzer
  asks for N u within the grace (to 35% plus the bleed) and M u to 50%. Repeated shocks without blood do not
  help.
- **Every state names its cause.** The patient gets per-cause alerts and one line per transition ("The pain
  takes you under"), each with what else is holding them down. The IPC HUD says CELL EMPTY, COOLANT PUMP
  OFFLINE, HYDRAULIC PRESSURE LOW or MOBILITY LOST, and shows sensor load and chassis temperature. Machines
  and faints get no heartbeat.
- **The medic reads it.** The analyzer heads its panel with the state and cause, breathing, circulation (blood %,
  trend, units to 50%) and the defib verdict. Examine shows the chest (not breathing, gasping, slow and
  shallow) and the pulse (pale; pale and clammy with a weak, rapid pulse; barely palpable).
- **Crawling.** Downed, you can pick up loose items on your own tile and the ones next to it (1.5 m). You still
  cannot fire. Call for help shouts your line and flags you on medical HUDs for 60 s, with a 30 s cooldown.
- **Honest endings.** Succumb and Last Words appear only in cardiac arrest. Each opens a dialog that says
  exactly what happens: catastrophic brain injury, the brain stays in the body, a returnable ghost, and revival
  by brain repair plus a defibrillator until the body rots. Typing `ghost` in arrest opens the same dialog.
  Anywhere else it offers "left alive but empty" with no return and no damage. The pod offers the way back
  after a revival, as the hand defibrillator does. The pod keeps a fainted occupant anaesthetised and does not
  sound its Critical alarm for a faint.
- **Species.** The scenarios run for Human and IPC. The report-mode conformance test lists the 98 known gaps
  in 32 other round-start species until M4 closes them.

## M1a review fixes (2026-09-23)

Two reviewers read the M1a commits. What changed:

- **"Left alive but empty" can no longer come back returnable.** If the body died some other way while that
  dialog was open, confirming it used to ghost with `canReturnGlobal` = dead, so the ghost could return,
  against the dialog's "you cannot return". Now death withdraws any open dialog (`Revoke` on death withdraws
  both kinds; heart restarts still withdraw only Succumb), `LeaveAlive` refuses a dead body, and it always
  ghosts with no way back. The player ghosts from the corpse the ordinary way. Test: a new branch in
  `HonestEndingScenarioTest`.
- **A recovered patient's next arrest is a new episode.** Plan §7.1 item 6 grants the restore and the grace once
  per arrest episode, and the code defined the episode only by `wolfmed.post_shock_repeat_seconds` (300 s)
  since the last restore. A patient who got up and walked off, then arrested again from a new wound within
  300 s, got no restore and no grace. Now the post-shock record also ends once the grace is spent and the
  patient is Up, not in arrest, with blood at or above `wolfmed.brain_blood_start` (the line where blood stops
  draining the brain, also where the analyzer's post-shock advice stops). A patient still down, or up with low
  blood, keeps the 300 s window, so the plan's repeat-shock rule is unchanged for a continuing episode. No new
  CVar. Test: a new branch in `RepeatedShockTest`.
- **Asphyxiation readers the M1a test-migration table names but no package touched.** `WolfmedSpeciesSpawnTest`
  (`IpcLeaksOilAndTakesNoAirlossTest`) and `WolfmedVitalLimitsTest` only assert the raw `Asphyxiation` and
  `Bloodloss` entries in `DamageableComponent.Damage` (IPC takes none; the Airloss cap). Neither reads the
  brain's breathing input or consciousness, so neither needed migrating. Both run, and pass, in the full filter.
- **Package A's paperwork.** `plan/p7/wp/A-report.md` now exists as a standalone record, reconstructed from
  commit `8f71617774`, the code and the `## M1a A` section above, and labelled as a reconstruction.
- **Review pathspec.** In this repository the code lives in `Content.Server`, `Content.Shared`, `Content.Client`
  and `Content.IntegrationTests`; a pathspec of `Content` matches none of them. Diff M1a with
  `git diff 8e4a5cd15b..HEAD -- Content.* Resources Docs`. No code change.

## M1a playtest 1 fixes (2026-09-23)

The owner's first M1a playtest, eight findings in priority order (`plan/p7/PLAYTEST1-spec.md`).

**1. Bleeding nerfed.** `wolfmed.bleed_rate` 0.6 → 0.3 (every wound bleed ×0.5; the arterial wound keeps its own
0.8 stage rate, about four times a critical slash, so it stays the worst bleed). Internal bleeding does not read
that knob, so its rate is halved in data (`InternalBleedingWound` 0.02 → 0.01 per severity per second, marked
Onyx YAML). The words follow the rates: the examine bleed bands (`look.yml`) and the gore system's major-bleed
rate (`sfx.yml` 0.9 → 0.45) are halved too, so a cut artery still reads "spurting". Regeneration (0.33 u/s),
the 300 u pool and the lines (50/35/30%) are unchanged. Measured by the new `BleedTimingTest` (human, full blood,
station air, untreated):

| | before (0.6) | after (0.3) |
|---|---|---|
| Arterial arm cut (Slash 25), rate at the cut | 2.75 u/s | 1.38 u/s |
| Downed / Unconscious / arrest | 64 / 91 / 100 s | 186 / 253 / 274 s |
| Plain cut (Slash 15), rate at the cut | 0.30 u/s | 0.15 u/s |
| Plain cut clots / lowest blood | 30 s / 99.7% | 30 s / 99.8% |

The arterial cut now takes 4.6 minutes from Up to arrest (the owner's floor is 4), with about three minutes on
the feet to tie a tourniquet. The test pins the shipped rate and asserts arrest ≥ 240 s, within +20% of 274 s,
Downed within ±20% of 186 s, and a plain cut clotted inside a minute having cost under 5% of the blood.
`InternalBleedTickTest` expects 0.1 and 0.2 u/s now. `BleedingScenarioTest` needed no band change; it reads the
blood band fresh at the Unconscious line, because the slower bleed lands there between two life ticks.

**Found on the way (not changed): vacuum bleeds.** A human in vacuum bleeds 1.8 u/s by two minutes in, more than
an arterial cut: barotrauma's Blunt opens bleeding blunt wounds on every part. Downed by pain at about 55 s, by
blood at about 150 s, arrest at about 205 s. Worth a look with M1b's caps.

**2. Painkillers you can feel.**
- Why a drink "did nothing": a swallowed dose waits the stomach's `DigestionDelay` (20 s) before it reaches the
  blood. Now any reagent with a `WolfmedPainRelief` effect waits `wolfmed.painkiller_absorb_seconds` (new, 4 s)
  instead: `WolfmedOralAbsorptionSystem` (`_WF`) answers the stomach's one marked line. Measured: the analgesic
  pill takes hold 5 s after swallowing and stands a pain-Downed patient up at once; 5 u of opiate ends a pain
  faint inside the same 5 s.
- The tiers reach the tests as designed: weak 22 lifts any pain-Downed body (body pain is capped at 135, and
  135 − 22 = 113 is under the 115.4 stand line); strong ends a faint; neither lifts a blood-Downed body.
- The sedation cost: the opiate adds 0.018 sedation a second while a dose runs and metabolises at 0.1 u/s, so
  5 u runs about 54 s and peaks at 74% sedation, past the 60% line where breathing slows. That is the designed
  overdose and was left alone; the guidebook already says so.
- Lines: `WolfmedPainReliefSystem` raises `WolfmedPainReliefTierChangedEvent` when the strongest tier changes, and
  the condition alert system tells the patient "The painkiller takes the edge off." / "The opiate takes hold." /
  "The stimulant kicks in." / "The stim hits. You have seconds on your feet." on reaching a tier, "The stronger
  painkiller wears off." on a drop, and "The painkiller wears off." at none. The stim's end is the crash's own
  line. Machines hear none of it.
- Pens (`painkillers.yml`, the stim pen's medipen sprite recoloured, CC-BY-SA-3.0): `WolfmedAnalgesicPen` 10 u
  analgesic (price 10) and `WolfmedOpiatePen` 3 u opiate (price 20). 3 u is about the most opiate one pen can
  hold and stay under the breathing line: measured 26 s of strong relief, 46% peak sedation. The analgesic pen:
  54 s, no sedation. Both inject on use-in-hand, which a Downed body may do to itself. Vended in `wolfgate.yml`
  (10/6), `wallmed.yml` (4/2) and `civimed.yml` (infinite/40). Guidebook line in `WoundTreatment.xml`.

**3. The defibrillator's "No response".** `WolfmedRevivalSystem.TryDefibrillate` used "No response" for both the
failed roll and the "this system does not own the body" fallback. Now the roll alone says "No response. Charge
again." and the fallback is `wolfmed-defib-not-monitored` ("Shock refused: no vital signs this device can
read."); the pod treats it as a gate and says it once, and the analyzer hides the verdict for it. What the owner
hit: the gate passes a transfused patient in arrest, and the paddles and the analyzer read the same `GetRefusal`.
The odds are what bite: `GetChance` scales `wolfmed.defib_chance` 0.85 by the brain's oxygen, down to
`wolfmed.defib_oxygenation_floor` 0.15. After about two minutes of arrest the oxygen is near 0 and a shock takes
at 13-19% (18.5% in the test, at oxygenation 0.08), so several "No response" in a row is the expected run. Left
as data for the owner: a floor of 0.4 would make the worst case 34%. A body that has died of the brain meanwhile
is refused with "Brain flatlined. Repair the brain first.", and the analyzer says "brain repair surgery first".
Shocks repeat freely while the heart is stopped; the hand defibrillator's zap delay is the only limit.

**4. Message spam in space.** The remembered words are one line: "You feel your wounds painfully close!"
(`bloodstream-component-wounds-cauterized`). Onyx's bleeding system printed it on every damage event whose Heat
trimmed a bleed on the part, with no limit, so any Heat arriving every second on a bleeding body (a fire, hot gas,
a fight somewhere hot) printed it every second. It now goes through
`WolfmedCauterySystem.TryAnnounceWoundsClosing`, once per `wolfmed.cautery_popup_seconds` (new, 10 s) per body.
Measured: 19 lines in 20 s unlimited, 5 in the next 40 s. In pure vacuum it did not fire at all (barotrauma's
Blunt outweighs its Heat in the bleed modifier sum), and the condition lines were one per real change: 2 in the
first minute, 6 over four minutes (down, adrenaline, blood loss, out, arrest). No Downed/Up flapping was found;
the Downed dwell and the hysteresis already hold.

**5. The "Let go" dialog.** The window opened centred on an empty body and grew off the bottom when the text
arrived, and `OpenCentered` measures a window before it has a parent, at whatever UI scale it last had. The EUI
now opens (or re-centres) when the state arrives, measures again in place and re-centres. The body text wraps at
420 units with no vertical expand, the old 200-unit minimum height is gone, and the margins match the treatment
window. `WolfmedChoiceWindowLayoutTest` at UI scale 1 and 1.25: on screen, centred, text wrapped and unpadded,
both buttons inside (one unit of slack for the pixel grid at 1.25).

**6. IPC crawl on low power.** Not zeroed on the server: an IPC at charge state 1 (12%) Downed by frame pain moves
at walk 0.34, sprint 0.61 (crawl 0.3 × low power 0.45). The Silicon speed table's `0: 0.00` entry is never read
(the lookup floors at key 1). What was wrong is prediction: `SiliconComponent.ChargeState` was never networked,
so the client predicted every IPC at full charge and its crawl snapped back. It is networked now (marked EE
edit), dirtied only on change. A cell under 5% rounds to charge state 0, which the Silicon death system treats as
empty: that IPC shuts down (Unconscious) and cannot crawl. That is the Silicon design and was not changed.

**7. Rejuvenate and a missing cell.** `WolfmedShutdownSystem.RestoreCell` on `WolfmedRejuvenateEvent`: a chassis
whose cell slot is empty gets its slot's `startingItem` (the prototype's own cell) inserted past the slot lock,
and the charge state is pushed at once, so the shutdown clears on the same tick.

**8. The resolve errors.** All 149 came from one upstream path: `SharedGunSystem.OnRevolverGetState` sending a
revolver's `AmmoSlots` with a deleted casing in them. Mono's spent-casing despawn (30 s) gave a fired casing a
`TimedDespawn` while it was still in the cylinder, so it was deleted in its slot. No Wolfmed system was involved.
`SetCartridgeSpent` takes a `despawn` flag (marked), the revolver passes false when it fires, and
`EmptyRevolver` arms the timer when it drops a spent casing on the floor.

**Review lows folded in.** `OnMobStateChanged`'s death branch now goes through `Apply`, so a death raises
`WolfmedConsciousnessChangedEvent` like any change (the alert system says nothing for the dead) instead of
repeating Apply's side effects by hand. The `MathF.Max(outLevel, faintLevel)` in `Evaluate`'s depth is not dead:
`faintLevel` is summed pain against the faint line, which deepens a Downed body's view as its pain climbs; the
PainFaint input only puts 1 in `outLevel` during a faint, when the body is Unconscious. Kept, with a comment.

**Numbers.** `wolfmed.bleed_rate` 0.3, internal bleed 0.01, `wolfmed.painkiller_absorb_seconds` 4,
`wolfmed.cautery_popup_seconds` 10; pens 10 u analgesic and 3 u opiate.

## M1b (2026-09-23)

Burns and caps. Plan: `WOLFMED_DEATH_PLAN.md` §12 M1b, §6, §3.7, §2.5/§6.3, OD11 (yes), OD12 as the owner answered
it on 2026-09-23 (appendages can crumble after long charring, head and torso never), P31.

**The measurement, first.** `WolfmedBurnScenarioTest.FireMeasurementTest`, a human in a 10-stack fire in station
air, never patting it out.
- Before any M1b code, with `wolfmed.body_damage_cap` 0: the fire lasts 100 s (the stacks fade 0.1 a second).
  It lands 1614 Heat, 1485 stored and 129 cut by the torso's 250. Nearly all of it lands in the first 100 s:
  1485 stored by 120 s and no more after. Total burn severity is 1615 at 120 s and 1647 at 300 s, from body heat.
  The critic's estimate of Heat "on the order of 2,000" was high. Arms and legs stored 181 to 200 Heat, past the
  new 152 ceiling but under the 250 ash line, so this fire would not have ashed a limb even uncapped.
- The same fire under M1b: Total Heat 1615 (every hit's Total, stored or not). Stored 1272 to 1290 and 324 to 343
  not stored. Burn severity is about 1700 by 120 s: the ceiling cuts nothing from the wounds, and a burn at its
  200 escalates into charring. Fluid loss runs at 1.36 u/s.
- **The rate set against it:** `fluidLossPerSeverity` **0.0008** u/s per point (plan start 0.002). At 0.002 the
  measured burns would lose about 3.4 u/s. The heart would stop about two minutes after ignition, before a
  medic could reasonably arrive. At 0.0008, from ignition untreated: Downed by blood at about 195 s, Unconscious
  at about 245 s, arrest at 255 to 270 s (`BurnScenarioTest`: arrest 260 s against 258 s derived from the
  measured burns). That is close to the arterial cut's 274 s (playtest 1).
- A dressed full-body burn loses 1.06 → 0.26 u/s, under regeneration (0.33 u/s), so a dressed patient's blood is
  stable. A graft takes it to 0.

**What was built.**
- **One event per hit (§6.2).** `PartDamageAppliedEvent` gets `Overflow` (what a ceiling cut from this hit) and a
  computed `Total` (Applied + Overflow). Its `Damage` stays the stored amount (Applied). In routing, one
  `PartDamageAppliedEvent` is raised per hit, including one that stored nothing. `PartDamageOverflowedEvent`
  (evisceration, tear-off pressure) still goes first and still carries only the torso cap's overflow. The ambient
  ceiling's cut never counts as tear-off pressure. The dispatcher (`OrganDamageSystem.OnPartDamageApplied`)
  hands `Total` to wounds, bleeding and the organ roll, once. Fractures and amputation get Applied. The D27
  accumulator adds Applied only.
- **The ceilings (§6.1).** `WolfmedBodyPartSystem.ClampToBodyCap(body, part, damage)` now returns what it cut:
  - *Per-part ceiling* for damage with no origin that is not an explosion:
    `wolfmed.ambient_part_cap_fraction` 0.8 × the part's lowest Destructible trigger. That gives arm and leg 152,
    hand and foot 120, and a human head 400. IPC parts get 152, the head included, because their base carries
    the limb triggers. `WolfmedPartCeilingSystem` (server) answers the new `WolfmedPartDestructionThresholdEvent`
    from the part's `DestructibleComponent`. The torso's 400 trigger gives 320, so its own 250 cap governs, as
    the plan says.
  - *Corpse ceiling:* `wolfmed.body_damage_cap` 600 now applies only to Dead bodies.
  - *Admin bypass (P31):* `WolfmedBodyPartSystem.WithCeilingBypass`, used by the part form of `damage`. `_WF` on
    both ends, no hook.
- **Burn fluid loss (§3.7, OD11).**
  - `WolfmedFluidLossSystem` runs once a second. Every wound whose prototype declares `WolfmedFluidLossBehavior`
    loses severity × `perSeverity` × `wolfmed.burn_fluid_rate` (1) once it is at `from` or above.
  - Declared on `BurnWound` (`from` 20, marked Onyx YAML) and on `WolfmedCharringWound` (`from` 0).
  - The volume is split out of the blood solution and discarded, so there is no puddle. A sub-hundredth
    remainder carries to the next tick.
  - Dead bodies lose nothing. Arrest does not slow it. `WolfmedBurnFluidLossComponent` on the body carries the
    rate for the analyzer and examine.
- **Dressing and graft.**
  - A healing item that removes Heat (ointment, burn packs, gel) dresses the resolved part's weeping wounds
    (`WolfmedDressedComponent`), from `HealingSystem.Wolfmed.cs`: × `wolfmed.burn_dressed_fluid_factor` 0.25.
  - The existing graft surgery step (`SurgeryStepGraftSkin`) carries the new `WolfmedSurgeryGraftBurnsEffect`.
    It grafts every weeping wound on the part: × 0.
  - A dressed or grafted wound that grows `wolfmed.burn_treatment_lost_severity` (15, the burn's reopen line)
    past where it was treated loses the treatment.
- **Burn infection (P20).** Infection profile `dressedMultiplier` 0.15. A wound with no bleeding treatment that
  is dressed or grafted progresses at 0.15. Measured 3.29 against 21.95 over three minutes (0.150).
- **A wound at its cap escalates (§6.3).** Heat on a part whose burn is at its 200 becomes charring:
  `wolfmed.char_escalation` 1 point per point of Heat, up to the charring's 120.
- **OD12, crumbling.** A hand or foot whose charring sits at its maximum (120) crumbles to ash after
  `wolfmed.char_crumble_seconds` 180 of Heat still arriving. "Still arriving" means gaps of at most
  `wolfmed.char_crumble_gap_seconds` 10. Arms and legs take × `wolfmed.char_crumble_limb_multiplier` 2. Head and
  torso never crumble. The crumble goes through Shitmed's `BurnPart`, a tick later: no stump wound (cauterised),
  organs dropped, the ordinary part-loss handling, and "The left hand crumbles to ash!". It is wired through
  `WolfmedPartHitSystem`, the dispatcher's one-line hand-off, which sees each hit once before the wounds do.
  `CharCrumbleTest` with the clock pinned at 20 s: the hand crumbled 20 s after charring through and the arm 39 s
  after. The head and torso, under the same 10 Heat a second, charred through at 30 s and were still attached at
  120 s, the head storing at most 400.
- **Extinguishing while Downed (to confirm).** Confirmed, no change. `FlammableSystem.Resist` needs
  `CanInteract(uid, null)`, and `WolfmedDownedSystem` blocks only interactions with a target. Only the fall's own
  2 s stun blocks it. `DownedCanPatOutFireTest` pins it.
- **What people read.**
  - Analyzer: "Fluid loss from burns: slow/fast (x u/s)" under the circulation line; fast from
    `wolfmed.analyzer_burn_fast` 0.5 u/s. The blood trend and the post-shock transfusion number now count burn loss
    (`WolfmedLifeSystem.GetVolumeLossRate`).
  - Examine, close up: "has weeping burns".
  - The patient: "Your burns are weeping fluid. Dress them; you will need fluids." when it starts.

**The pain faint's cooldown, 30 → 50 s (plan §3.1's fallback).** Burns now grow past where the old 600 stopped
them, so pain keeps rising through a fire, and each +40 over the pain at waking re-arms a faint. At 30 s, a pure
10-stack fire fainted the patient three times in two minutes (60 s Critical). `SustainedFireFaintTest` (fire plus
blows) measured 53 s in one full run and 37.5 s in another. Plan §3.1 names the fix if that test fails: raise the
cooldown to 50, which caps any two minutes at two faints by the timers alone. With 50:
- the burn scenario faints at 15 s and 85 s: 40 s in the fire's two minutes;
- `SustainedFireFaintTest` read 40.5 s at its half-second samples, two faints of 20 s, the second 50 s after
  waking.

**Differs from the plan, and why.**
- **Burn rate 0.0008, not 0.002.** Set against the measurement, as the plan asks.
- **`BurnScenarioTest` sees two faints, not "one faint".** The M1a re-arm rule (+40 over the pain at waking)
  refaints a patient whose burns keep growing. The test asserts each faint ≤ 20 s and at most 40 s Critical in
  the fire's two minutes (§2.3).
- **`fluidLossFrom`/`fluidLossPerSeverity` are one wound behavior**, `WolfmedFluidLossBehavior { from, perSeverity }`,
  declared once on `BurnWound`'s base behaviors (a single marked YAML block) instead of on each stage. The values
  are the same at every stage, so one declaration is the smaller Onyx edit. The charring wound weeps too.
  - Why: the plan puts the fields on "the Wolfmed burn prototypes (burns.yml)" as well.
  - The effect: the overflow that escalates into charring keeps adding fluid loss. The plan's AmbientCeilingTest
    asks for this ("grows the burn wound and its fluid loss").
- **The graft is the existing charring surgery.** A burn under 80 never chars, so it has no graft to receive. A
  dressing leaves it at × 0.25, which at those severities is under 0.04 u/s. There is no graft item outside
  surgery ("burn kit" in the plan).
- **A new hand-off method.** `WolfmedPartHitSystem.OnHit` is one more line in the same marked dispatcher method
  (inventory #8). The plan's §6.3 escalation and the owner's OD12 crumble both need each hit's Total once, and
  only one system may subscribe the event. It also carries the test seam (`Observer`). `SaturatedTorsoTest` and
  `AmbientCeilingTest` read the Total the dispatcher hands to wounds and organs there. Organ damage itself is
  M3's to assert.
- **Treatment is lost on regrowth** (`wolfmed.burn_treatment_lost_severity`, new). The plan says nothing about a
  dressed burn burned again. Without this, a grafted burn would never weep again.
- **The patient's "Burns weeping fluid" is a popup and a line when it starts, not a status alert.** An alert would
  need a prototype and an icon. The analyzer and examine carry the ongoing state.
- **The analyzer's burn line is its own row** under circulation, not appended to a bleed-rate figure. The panel
  has no bleed-rate figure.
- **`TryApplyPartDamage` returns false for a hit that stored nothing**, although its event is raised and its
  wounds grow. It reports what was stored, as before.
- **IPC heads get a 152 ambient ceiling**, not 400. Their part base carries the MajorLimb triggers (190/210).

**Test migration.**
- `DamageTotalsNeverCritAWoundHostTest`: comment only. The 600 it names is no longer a body-wide cap on the
  living.
- `WolfmedDamageCommandTest`: new `DamageCommandPassesTheAmbientCeilingTest`. The command stores 200 Heat on an
  arm; the same Heat with no origin stores 152.
- `WolfmedBurnWoundTest`: new `BurnAtItsCapEscalatesIntoCharringTest`.
- `WolfmedEviscerationTest`: new `OverflowHitFollowsTheTearTest`. The hit past the cap is one event, raised after
  evisceration has had its overflow, with Applied 0 and Overflow the whole cut.
- `WolfmedInfectionTest`: a dressed burn infects at under a quarter of an open one.
- `WolfmedSpeciesSpawnTest`:
  - `BodyDamageIsCappedTest` now asserts each living part at its per-part ceiling. It sends 300 Heat a part,
    past the torso's 250.
  - `CappedCorpseCanStillBeDismemberedTest` kills the body first, since the corpse ceiling is what it tests.
  - `DionaLimbIsDestroyedNotSeveredTest` and `IpcLimbSeverabilityAndGibCeilingTest` give their 195 Blunt an
    attacker, because ambient damage no longer destroys limbs.
- `WolfmedAmputationTest`:
  - `OverflowAmputationMechanismIsInertOnShippedLimbsTest` gives its head hits an attacker. 16 × 25 ambient
    Blunt sat exactly at the head's 400 ceiling.
  - `GunsAndLasersAmputateOverThresholdLimbsTest` gives its bullets and lasers a shooter. A hand's ambient
    ceiling (120) is under its 200 thresholds.
- These tests were skipped in every full run, which hid the failures. Each failed alone until migrated.
- `SustainedFireFaintTest` pins the shipped 50 s cooldown and checks it on the server clock (`PainFaintCooldownUntil`).
  It now times everything on the server clock too. Counting half-second loops ran about 6% slow, because every
  `WaitPost`/`WaitAssertion` runs ticks of its own: the M1a version read a real 50 s gap as 47 s, and 30 s as
  28.5 s. It asserts at most two faints in the two minutes and at most 21 s each as sampled (≤ 42 s in all).
  Two 20 s faints read 40.5 s and 41.6 s in two runs.

**Tests.** Final full filter (`_Onyx.Wounds|Wolfmed|GibTest|Tests.Body|Autodoc`): 424 total, 414 passed, 0 failed,
10 skipped; each skipped test passes alone.

**Numbers.** `wolfmed.ambient_part_cap_fraction` 0.8, `wolfmed.body_damage_cap` 600 (corpse), `burn_fluid_rate` 1,
`burn_dressed_fluid_factor` 0.25, `burn_treatment_lost_severity` 15, `char_escalation` 1, `char_crumble_seconds`
180, `char_crumble_limb_multiplier` 2, `char_crumble_gap_seconds` 10, `analyzer_burn_fast` 0.5,
`pain_faint_cooldown` 50; `fluidLossPerSeverity` 0.0008 (burn from 20, charring from 0); infection
`dressedMultiplier` 0.15.

**For the M3 merge.**
- M3's organ hand-off in `OrganDamageSystem.OnPartDamageApplied` must read `total` (the hit's Total), not `args`.
- Barotrauma routes through `ClampToBodyCap(body, part, damage)` like any damage with no origin.
- `WolfmedPartHitSystem.OnHit` runs first in that method.

**Found on the way.**
- **Mono's grid cleanup deletes long scenario grids.** In two of three full runs, `PainScenarioTest` failed with
  its body gone ("does not have WolfmedConsciousnessComponent"). `GridCleanupSystem` had deleted the test grid
  during the ten-minute stretch: no player nearby, no powered APC, low value. The test passes alone. The new
  `WolfmedScenario.KeepGrid` gives the grid `CleanupImmuneComponent`. `PainScenarioTest` and the M1b burn
  scenarios call it.
- The dressing hook in `HealingSystem.Wolfmed.cs` is exercised only through its `Dress` seam. No test drives a
  real ointment do-after.

## Playtest 2: faint timer, fire helplessness (2026-09-23)

The owner's second playtest (`plan/p7/PLAYTEST2-spec.md`): "There needs to be some kind of indication of when you'll
come round from pain crit" and "I stood in fire and my hands stopped working / I couldn't crawl at all".

**1. The faint counts down.**
- The faint alert (`WolfmedFaintPain`) carries the engine's alert cooldown from the faint's start to
  `PainFaintUntil` (new server field `PainFaintStart`), so the icon ticks down to waking. It is shown only while the
  faint is all that holds the body under (cause PainFaint, no blockers). A faint that ends early (a strong
  painkiller) changes the state or cause, and the alert that replaces it has no cooldown.
- The condition text says "Coming round in {N} s." (`wolfmed-cause-pain-faint-help-timed`) in place of "You come
  round in seconds." while unblocked; the blocked form is unchanged ("something else keeps you under" plus the
  blockers). The "out" line at the faint's start carries the seconds too.
- The analyzer: "FAINTED: pain, {N} s" (`FaintSeconds` on the vitals report); a blocked faint keeps
  "FAINTED: pain (also: …)" with no seconds.

**2. What took the hands and the crawl: Shitmed's limb switch-off, not Wolfmed.** Reproduced in
`FireHelplessnessTest` before any change (10-stack fire, station air, sampled every 5 s):
- `SharedBodySystem.CheckBodyPart` switches a part off (`BodyPartComponent.Enabled = false`) when its stored damage
  reaches `IntegrityThresholds[CriticallyWounded]`, 90, whatever the damage is. M1b's ambient ceilings let a
  burning arm or leg store up to 152 and a hand or foot 120, so every limb crossed 90 during the fire.
- A switched-off arm or hand raises `BodyPartDisabledEvent` and `HandsSystem` removes the hand: 2 hands at 20 s,
  1 at 25 s (right arm off at 103), 0 at 35 s (left arm off at 98). No hand, no pen, no pickup.
- A switched-off leg leaves `BodyComponent.LegEntities`; `UpdateMovementSpeed` averages over the enabled legs, so
  one leg off halved the base speed (crawl 0.188 at 40 s) and both off set it to 0 (walk 0.000 from 50 s on, to
  the end of the run). Nothing Wolfmed could multiply brought it back.
- Not the cause: Onyx's `BodyPartFunctionalityState` stayed Functional throughout (P2-3 keeps it off); Wolfmed's
  limb penalties never went past ×0.65 movement (charring stayed at 20-32, its Moderate stage, with no penalty);
  the faints (15-30 s, 85-100 s) and the pain shock's stun were short and ended. `CanInteract` and Call for help were
  intact at every Downed sample; the hands were simply gone.

**The fix, to the plan's rule (§2.2: a Downed body can always crawl, use its carried items on itself and Call for
help).**
- **Burns never switch a limb off.** Marked edit in `CheckBodyPart`: on a wound host, the switch-off and
  switch-on lines read the part's damage less its Burn group (Heat, Cold, Shock, Caustic;
  `WolfmedLimbIntegrity.ForEnable`). Brute past 90 still switches a limb off exactly as before; the targeting doll
  still reads the full damage. Applies to legs too, so the plan's "painkillers still lift a badly burned patient"
  holds.
- **Burns slow the hands instead.** `BurnWound` gets `WolfmedLimbPenaltyBehavior` manipulation ×1.25 at Severe (50)
  and ×1.5 at Critical (80) (marked Onyx YAML); charring keeps its own ×1.3/×1.6 and movement penalties. Measured:
  an arm burned to 152 and its hand to 120 do a pen's do-after at ×2.06.
- **Crawl floor** (`wolfmed.crawl_floor` 0.35, new, replicated). `WolfmedCrawlSystem` answers a new
  `WolfmedSpeedFloorEvent`, raised by a marked line in `MovementSpeedModifierSystem.RefreshMovementSpeedModifiers`
  after every other modifier. While Downed a wound host never crawls under 0.35 × lying-down 0.3 × the default base
  (2.5 walk, 4.5 sprint): 0.263 walk. With no working leg it drags itself on its arms at exactly that (×1.5 while
  adrenaline runs); with no working arm and no working leg it cannot move. "Working" is attached, switched on and not
  Onyx-Disabled. Because Shitmed gives a body with no enabled leg a base speed of 0, a second marked line in
  `SharedBodySystem.UpdateMovementSpeed` gives a legless wound host the default base instead and refreshes the
  modifiers against it; the floor then sets the real speed. An arm switching on or off refreshes the speed on the
  next update (`WoundableComponent` + `BodyPartEnableChangedEvent`).
- **Legs switched off Down the body.** `WolfmedConsciousnessSystem.LegsGone` counts a leg Shitmed has switched off,
  since that is what drops the body; before, only Onyx-Disabled or missing legs counted, and a body with both legs
  broken past 90 lay on the floor while Wolfmed called it Up.
- **The penalty is told.** When a wound penalty first slows the hands or the legs the patient hears "Your hands are
  badly burned; everything takes longer." / "Your legs are badly burned; moving is slow." (or "…are hurt…" for other
  wounds), once until it clears; a body that was out hears it on coming round. The same line sits in the condition
  text while it lasts. `WolfmedWoundTraitSystem.GetBodyLimbPenalty` reads the worst penalty and whether a burn is
  behind it; the condition alert system answers the broadcast `WolfmedWoundLifecycleEvent` and checks once a tick.

**After the fix**, the same fire: both hands at every sample, the pen picked up and injected at 60 s (the analgesic
then stood the body up, as the plan's standing decision says), crawl 0.375-0.563 at every Downed sample, Call for
help at every Downed sample, faints at 15-30 s and 85-100 s unchanged.

**Differs from the spec, and why.**
- The spec suspected Wolfmed's limb penalties or the M1b escalation. Neither disabled anything; the cause was the
  Shitmed switch-off that DECISIONS P2-3 deliberately left running, reached by M1b's higher ambient ceilings.
- Burns are excluded from the switch-off on every limb, not only hands and arms: a burned leg switched off would
  make the body Downed by legs, which no painkiller lifts.
- Burns carry no movement penalty of their own; charring's existing one stays.
- `FireHelplessnessTest` accepts a sample where the pain shock's 2 s stun blocks movement (speed is still above 0).

**Numbers.** `wolfmed.crawl_floor` 0.35 (floor 0.263 walk, 0.473 sprint for a human); BurnWound manipulation ×1.25
(Severe), ×1.5 (Critical).

**Tests.** Full filter (`_Onyx.Wounds|Wolfmed|GibTest|Tests.Body|Autodoc`): 428 total, 421 passed, 0 failed, 7
skipped; each skipped test passes alone.

## M2 (2026-09-23)

Arrest, revival and medic information. Plan: `WOLFMED_DEATH_PLAN.md` §12 M2, §5.2-5.5, §3.3 (the breathing alert),
§3.4, §7.2 (M2 rows), §11 (M2 rows); the owner's answers of 2026-09-23: OD7 (c), OD8 (b), OD10 (b) with no trauma on a
repaired core, OD14 (a new antagonist), OD15 (sepsis half), OD17, OD20 (a). Branch `Wolfmed-m2`, from `bedaf99945`.

**What was built.**
- **Sedation model (§3.4, P15).** The `WolfmedPainRelief` effect's per-second `sedation` is gone; `sedationPerUnit`
  replaces it. Each metabolism tick the dose asks for (units of that reagent still in the blood) × per unit; the body's
  sedation moves toward the sum at `wolfmed.sedation_rise` 0.05/s and falls toward it at the existing 0.035/s, so a
  steady dose levels off and a finished one decays. Opiate 0.22/u, tramadol 0.11/u, oxycodone 0.12/u (marked Onyx YAML).
  - Measured (`SedationModelTest`, one standard dose: the 3 u opiate pen, 5 u tramadol or oxycodone): peaks 0.46, 0.46
    and 0.49. Doubled: 0.92, 0.90 and 0.97, each Downed with cause Sedation. Three doses reach full sedation.
  - Warnings (self popup and chat, once each as sedation climbs; a line re-arms once sedation falls back under it):
    "You feel heavy and drowsy." at `wolfmed.sedation_warn` 0.4, "Your breathing slows. Another dose could stop it." at
    the component's `SedationAirlossThreshold` 0.6 (so it moves with the depression line), "You can barely stay
    awake." at `wolfmed.sedation_warn_heavy` 0.8. Measured firing at 0.41, 0.62 and 0.83.
    `WolfmedSedationWarningSystem` (server) tells them through the condition alert system.
  - Depression past 0.6 is still its own brain drain and never Asphyxiation (`SedationOverdoseTakesAirTest` keeps that
    assertion).
- **Naloxone (OD14).** `WolfmedNaloxone` ("naloxone"), `WolfmedReverseSedation` effect: each unit metabolised takes
  `sedationReversePerUnit` 0.3 off the sedation and holds the target at zero for 4 s past the last tick, so the opioid
  still in the blood cannot pull it back up meanwhile. Metabolism 0.5 u/s; it wears off before the opioid does, so a
  large overdose can come back. Recipe Inaprovaline + Ammonia → 2 (no conflict with an existing reaction). The 5 u
  `WolfmedNaloxonePen`: NanoMed 2 and the Wolfgate vendor 4, wallmed 1, civimed 40. Measured: a full overdose was out
  of Unconscious 0.5 s after the pen and under 0.6 within the pen's run.
- **The autodoc under the new model.** The pod pushes only the units that keep the occupant's sedation target under
  `wolfmed.autodoc_sedation_cap` 0.5, at the strongest sedation per unit among the anaesthetics it may push, and tops
  up as the dose is used. Its 15 u opiate default would have asked for 3.3 and overdosed every patient.
- **Stim on strong (P30).** `MasksSlowdown` reads the doses, as `EndsFaint` already did: a stimulant on top of a strong
  painkiller no longer brings the wound slowdowns back just because Stimulant outranks Strong.
- **Sepsis on a clock (OD15, data).** `wolfmed.arrest_sepsis_chance` 0.01 → 0. Sepsis at 80 drains the brain at 1/600
  a second and the heart stops through the oxygen trigger; that arrest is named "sepsis" when sepsis is the drain behind
  it. Measured 511 s from full, both runs (derived 510 s).
- **Cold (P24).** Tissue loss below 0.4 oxygenation is multiplied by the same cold factor as the drain; so is the
  analyzer's brain-death estimate. Measured 0.100 of the warm loss at 280 K.
- **Electrocution (P28).** The arrest reads shock × the siemens coefficient the electrocution attempt left after
  insulation, the same figure it deals as damage. No upstream edit was needed (below).
- **Sutures (P20).** `WolfmedSutureComponent` on `MedicatedSuture` (marked YAML); `HealingSystem.Wolfmed.cs` marks the
  treated part's open wounds that share the item's treated types with `WolfmedSuturedComponent`. Infection reads the
  profile's existing `Sutured` rate (0) for them until the wound grows `wolfmed.suture_treatment_lost_severity` 15 past
  where it was sutured. Measured: 6 minutes, sutured 0.00 against open 36.46.
- **Executions and suicide (OD17, P10, P29).** HOOK 13 rewritten: the execution raises `WolfmedEndingEvent` on the
  victim, and `WolfmedDyingActionsSystem.EndDeliberately` sets the brain (or positronic core) to 0 where it sits, kills,
  and ends any arrest on the corpse: the order Succumb uses. A suicide raises the same event from the self-execution
  branch, and `SuicideSystem.Suicide` calls the same method after its own events (marked line). The ghost from a
  suicide is upstream's, non-returnable. Brain repair plus a shock revives an executed body.
- **Restart button (§7.2).** Marked line in `DeadStartupButtonSystem`: on a wound host
  `WolfmedRevivalSystem.TryRestart` decides instead of the damage total. `GetRestartRefusal`: rot, other content's
  `Unrevivable`, no head, no core, core destroyed, no pump, no power; each with a line ("buzzes: core destroyed. Core
  repair surgery first."). A restart goes to Critical and lets consciousness decide, as a shock does.
- **Core repair (OD10).** `SurgeryRepairCore` on the torso, listed while the `posbrain` slot's core is damaged: unbolt
  the housing (wrench, `WolfmedHullPlate`), re-flash the core (multitool, new `WolfmedCoreProbe`, marked YAML on
  `Multitool`), weld the housing shut (welder, `WolfmedHullWeld`); `WolfmedCoreHousingOpen` marks the step in between.
  The repair step is `WolfmedSurgeryBrainRepairEffect` with a new `slot` field (`posbrain`). `RepairBrain` gives a
  machine no `WolfmedBrainTrauma`; `WolfmedCoreRestoredComponent` puts "CORE RESTORED: DIAGNOSTICS" (Info) on the
  synthetic HUD for `wolfmed.brain_trauma_minutes`. The analyzer's core lines name core repair, and a dead core's banner
  reads "CORE FAILURE - core destroyed. Core repair surgery, then the restart button."
- **The vitals block, completed (§5.5).** Two new lines and one verdict:
  - "Getting worse: bleeding (pressure, gauze, tourniquet); sepsis (antibiotics)", or "Getting worse: nothing now".
    `WolfmedLifeSystem.GetActiveRoutes` lists every drain on the brain (arrest, airway, lungs, sedation, circulation,
    sepsis), tissue loss, bleeding, internal bleeding and burn fluid loss.
  - "After a restart: arrest cause blood. Still present: yes. Transfuse ≈ N u within 45 s to stop the heart stopping
    again; ≈ M u to 50% to stop the brain injury." `WolfmedArrestMemoryComponent` is written whenever a living heart
    restarts and kept `wolfmed.arrest_cause_memory_seconds` 300. "Still present": blood under the brain-safe line,
    still suffocating or overdosed, the heart still failed, sepsis still past its line. The M1a post-shock banner hides
    while this line carries the same numbers.
  - A dead chassis: "Restart: ready" or "Restart: refused: core destroyed, core repair surgery first".
  - The brain line and the arrest countdown were already their own rows; the defib verdict shipped in M1a.
- **Examine (§5.5, §5.3).** Close up, others only: AVPU from the state and cause ("awake and answers you" while Downed,
  "drowsy and responds only to voice" at `wolfmed.sedation_warn` or Downed by sedation, "stirs only to pain" in a faint,
  "unresponsive" out cold or in arrest; nothing for somebody up and clear-headed); "lips are blue" under
  `wolfmed.examine_cyanosis_oxygenation` 0.54; "pupils are pinpoint" with the sedation; "pupils are unequal, and they
  are confused" with a brain injury (the brain organ under its concussion line, or a repaired brain's trauma). A head
  wound's own concussion adds nothing: `InternalFindingsNeverShowTest` keeps a mild one invisible. At range, a Downed
  body playing dead "appears lifeless".
- **The explanation card (§5.2, §2.3).** `WolfmedExplanationCard` (shared) builds the lines from the cause prototype:
  title, "Also holding you down: …", symptom, the help that wakes you in its blocked form while something else holds
  you (so a blocked faint never says "shortly"), "Someone is giving you CPR.", "A medic is examining you.". While Dying,
  a ten-cell bar of the rescue window with no seconds. `WolfmedCardSystem` (server) keeps `WolfmedCardComponent`
  (networked) on unconscious bodies: the bar in tenths of the untreated window the brain had when the arrest was first
  read (CPR refills it), CPR, and an analyzer read within `wolfmed.card_examined_seconds` 3. The client's
  `WolfmedExplanationCardSystem` draws it low on the screen; a machine whose synthetic HUD owns the view gets none.
- **Dying view (§5.2).** The Unconscious depth follows the route toward arrest (shared `WolfmedDyingDepth`: blood 35% to
  30%, oxygenation 0.45 to 0.15, whichever is further), not `pressure − 1`, which was dead code. A faint draws no dying
  view; it gets its own white-out.
- **"Can't breathe" (§3.3).** A marked line in `RespiratorSystem` shows "Can't breathe: no air" in place of the lung's
  stock low-gas alert on a wound host; `WolfmedBreathingAlertSystem` shows "Can't breathe: no lungs" for a body with no
  working lungs, which the respirator never alerts because it alerts once per lung. Same Breathing category, so the
  respirator's own clear takes them off.
- **Crawling stage (§5.3).** `WolfmedCrawlActionsSystem`, granted from consciousness's `Apply` beside Call for help:
  Check yourself (Up or Downed: the condition text and the self look, to your own chat) and Play dead (Downed only,
  OD20; moving, speaking or doing anything to anything ends it; being dragged does not). Adjacent aid (OD7 (c)):
  `WolfmedDownedSystem.CanAidAdjacent` lets a Downed body interact with a Downed neighbour within `wolfmed.downed_reach`
  while the item in its active hand is tagged `Gauze`; the Downed do-after penalty still applies.
- **Wait as a ghost (OD8 (b)).** `WolfmedDormantSystem`: Unconscious or shut down, alive, not in arrest, not a faint, no
  drain on a brain and no route running is stable; after `wolfmed.dormant_offer_seconds` 90 of that without a break the
  body is flagged in distress on medical HUDs (the Call for help icon, once) and gets the "Wait as a ghost" action. It
  opens a dialog with the plan's text; yes spawns a returnable ghost from the living body through the ghost system's own
  spawn (a visit, not `OnGhostAttempt`). Anything getting worse withdraws the flag, the action and an open dialog; a
  waiting ghost is told "Your body is getting worse: {route}. You can return to it now." once per route. When the body
  wakes the ghost is offered the return prompt.

**Numbers.** `wolfmed.sedation_rise` 0.05, `sedation_warn` 0.4, `sedation_warn_heavy` 0.8,
`examine_cyanosis_oxygenation` 0.54, `arrest_cause_memory_seconds` 300, `dormant_offer_seconds` 90,
`card_examined_seconds` 3, `suture_treatment_lost_severity` 15, `arrest_sepsis_chance` 0; `sedationPerUnit` opiate 0.22,
tramadol 0.11, oxycodone 0.12; naloxone 0.3 per unit, 0.5 u/s, 5 u pen.

**Differs from the plan, and why.**
- **The standard dose "targets about 0.45"** is read as reaching about 0.45. With the target falling as the reagent is
  used, a 0.45 target reaches only about 0.35 at the planned rise, and two doses would not Down anybody. The per-unit
  figures are set so the realised peaks match the plan's ladder: one dose about 0.46, two about 0.9, three full.
  The to-confirm per item: `WolfmedOpiatePen` delivers 3 u; tramadol and oxycodone ship in no item, so 5 u (the
  syringe's smallest setting) is their dose, and a full 15 u syringe is three doses.
- **The antagonist also holds the target at zero** while it is in the blood. "Lowers sedation by 0.3 a unit" alone
  would be undone at 0.05/s by the opioid still there.
- **No marked edit in `ElectrocutionSystem` (inventory #10).** The electrocuted event already carries the coefficient
  the insulation left, so the fix is `_WF`-only.
- **Execution goes through an event, not a call.** `SharedExecutionSystem` is shared and the ending is server code;
  `WolfmedEndingEvent` has one server subscriber. The old `_woundRouting` dependency and its `using` left the upstream
  file with it; `WoundDamageRoutingSystem.TryApplyLethalDamage` now has no caller (left in place, vendored Onyx).
  *[M6: removed, with the routing's MobThresholdSystem dependency it alone used.]*
- **Suicide's kill is in `Suicide()`, after upstream's own events,** rather than in the default damage handler, so an
  environmental suicide (a microwave, a gun) kills a wound host too.
- **Core repair has no incision requirement**; the three chassis steps are the whole surgery, as the chassis breach
  weld is. The plan said "a copy of SurgeryRepairBrain".
- **"CORE INTEGRITY {word}" is M3's** (the core-injury input); M2 adds only CORE RESTORED.
- **The routes** add bleeding, internal bleeding and tissue loss to the plan's list, because each is something getting
  worse with its own first aid. Wait as a ghost uses the same list, so any bleeding blocks the offer.
- **AVPU and the pupils are close-up findings for others only.** Nobody sees their own pupils; "you are unresponsive"
  to yourself is meaningless. Nothing is added for a healthy body, as M1a D did for the strong pulse.
- **Play dead reads "appears lifeless" at range only.** Close up the medic sees them breathing and answering.
- **Adjacent aid is gauze only, onto a Downed neighbour only.** "Pressure" has no verb of its own (M1a D: pressure is
  gauze or a bandage); an unconscious neighbour is not "Downed" in the plan's terms. Widening it to anyone on the floor
  is one condition in `CanAidAdjacent`, for the owner's playtest.
- **The distress flag is on medical HUDs only** and reuses the Call for help icon. There is no robotics HUD to put it on.
- **"Can't breathe: lungs damaged"** is not built: it is M3's lung route. The two alerts M2 ships are "no air" and "no
  lungs".
- **The rescue line "A medic is examining you"** comes from any vitals build: the hand analyzer and the pod's panel.
- **Test names:** `AdjacentDownedAidTest` is new (the plan lists no test for OD7 (c)); Check yourself is asserted in
  `PlayDeadTest`. `StimOnStrongTest` asserts the masking through `MasksSlowdown`, not a measured walk speed, because a
  fracture's grade is a roll.

**Test migration.**
- `WolfmedConsciousnessTest.SedationOverdoseTakesAirTest`: a target of 1.5 and 16 s instead of 0.5 a second; keeps
  "no Asphyxiation". `StackedPainkillersHitTheReliefCapTest` needed nothing: its doses ask for no sedation.
- `Scenarios/WolfmedCauseScenarioTest.OverlappingCausesTest`: the overdose asks for 1.5 and gets 22 s to reach it.
- `WolfmedDyingLevelTest`: new `DepthFollowsTheRoute`.
- `WolfmedBrainTest.ArrestClockRunsOutTest`: the cold brain has lost no more tissue than the warm one;
  `MechanicalShutdownAndDeathTest`: a repaired core carries no trauma and the restart refuses a chassis with no pump.
- `Scenarios/WolfmedMedicLinesTest`: a Downed patient's vitals block gains "Getting worse: nothing now" (four lines).
- `WolfmedLocaleCoverageTest`: the route and restart-verdict enum families.
- grep `arrest_sepsis_chance`: no test pinned it.

**Tests.** New in `Scenarios/WolfmedRevivalTest.cs`: `RestartHookTest`, `CoreRepairTest`, `ExecutionAndSuicideTest`,
`SepsisDeterministicTest`, `ColdBrainTest`, `InsulatedShockTest`, `SutureInfectionTest`. New in
`Scenarios/WolfmedMedicInfoTest.cs`: `AnalyzerVitalsTest`, `ExplanationCardTest`, `SedationModelTest`,
`StimOnStrongTest`, `WaitAsGhostTest`, `PlayDeadTest`, `AdjacentDownedAidTest`.
Full filter (`_Onyx.Wounds|Wolfmed|GibTest|Tests.Body|Autodoc`): 443 total, 435 passed, 0 failed, 8 skipped; each
skipped test passes alone.

**Found on the way.**
- `BloodstreamSystem.TryModifyBleedAmount` does nothing on a wound host (GUARD E3), so tests that need a bleed make a
  wound.
- A test that raises a `SurgeryStepEvent` by hand has to pass the tools: Shitmed applies a step's add and remove only
  when one of them carries the step's tool.
- **Fixed (M1a):** `SetExternalPressure` ignored any change under 0.001, so a ramp whose last step onto 1 was smaller
  stalled just short (hypoxia held at 0.9994) and never crossed its line; with the new sedation sitting at exactly 1,
  `OverlappingCausesTest` named Sedation instead of Hypoxia. A step onto or off the full line now always lands.
- **Fixed (test):** `WolfmedTreatmentRestrictionTest.SuturesTreatCutsAndBleedingNotBruisesTest` failed alone about one
  run in four: its 10 Blunt bruise rolled BluntWound's 25% bleed, which the suture then stopped. The bruise is 7 now,
  under the bleed's `minimumSeverity: 8`.

**Art debt.** Play dead reuses the Fake Death icon, Check yourself the eye icon, Wait as a ghost the ghost icon; the
distress flag the Call for help icon; the naloxone pen the medipen sprite recoloured; the "Can't breathe" alerts the
stock low-oxygen icon.

**For the M3 merge.**
- `WolfmedCause` and `WolfmedConsciousnessCausePrototype` are untouched; the card reads any cause M3 adds from its
  prototype (a head-blow faint gets the white-out through `WolfmedCauses.IsFaint`).
- M3's lung route should add a `WolfmedCantBreatheLungsDamaged` source to `WolfmedBreathingAlertSystem.SuffocationAlert`
  and a route bit if it drains through its own source.
- M2 edited `WolfmedLifeSystem` (tissue loss cold factor, sepsis naming, routes, arrest memory, core restore),
  `WolfmedConsciousnessSystem` (the Downed actions call in `Apply`, the depth function, the pressure dead band) and
  `WolfmedSyntheticHudSystem` (one CORE RESTORED line after the core block).

## M3 (2026-09-23)

Consequences keep mattering. Plan: `WOLFMED_DEATH_PLAN.md` §12 M3, §8, §3.6, §3.7 "stumps" (P19), P23, P27, OD15 as the
owner answered it (bands for crush internal bleed and electrical heart damage; sepsis is M2's).

**What was built.**
- **Organ reach (§8.1-3).** `WolfmedBodyPartComponent.OrganReach` (per damage type) on the torso (Piercing 10, Slash 18,
  Blunt 30, Heat 20, Shock 15), the head (8, 15, 15, 20, 15) and, restated, the IPC torso (OD10: penetrating torso hits
  reach the core). `WolfmedOrganThresholdSystem` (server, `_WF/Wolfmed/Body`) takes each hit's **Total** once: for every
  type over its line, organ damage = (hit - line) × the organ's `damageMultipliers` entry × its `selectionWeight` share
  of the working organs in the part × `wolfmed.organ_damage_scale`, capped at `wolfmed.organ_hit_cap` (5) per organ per
  hit. No roll: `hitChance`, `maxDamageFraction` and the profile chances are left to Onyx's roll, which still runs for a
  part without reach lines. The hand-off is one marked line in `OrganDamageSystem.OnPartDamageApplied` (inventory #13),
  after M1b's `WolfmedPartHitSystem.OnHit`, reading `total` (the M1b note). No new subscription.
- **A destroyed organ leaves the split.** An organ at 0 is skipped at once and is deleted a tick later, so the share
  is always over the organs that still work. An eviscerated or shot-out chest sends the next hits into what is left.
- **Graded bands (§8.5).** `WolfmedOrganComponent.ImpairedBelow` (0.5) and `Band` (OK / impaired / failed).
  - Lungs: a breathing input, (0.5 - health fraction) / 0.5 × `wolfmed.lung_damage_factor` (1), into the same drain as
    suffocation, hypoxia sub-source Lungs. Breathing reads `Laboured` ("short of breath"; examine and analyzer).
  - Heart: `impairedRegenFactor` 0.5 halves blood regeneration (one marked line at the bloodstream's regeneration step,
    `BloodstreamSystem` → `WolfmedLifeSystem.BloodRegenFactor`); a networked `PulseIrregular` for examine
    ("has an irregular pulse") and the analyzer.
  - Brain: below `wolfmed.consc_brain_down` (0.25) the new `injury` pressure (`wolfmed.injury_down_pressure` 0.75)
    holds the patient Downed, cause Brain ("Downed: head injury"), never Unconscious. Examine: "has unequal pupils and
    seems confused".
  - IPC core: the same pressure from the brainless branch of the life tick under `wolfmed.consc_core_down` (0.25), cause
    Core, never a shutdown by itself. HUD: "CORE INTEGRITY CRITICAL: MOBILITY LOST".
  - The analyzer: an "Organs:" vitals line naming every impaired or failed organ with its effect ("lungs impaired
    (short of breath); heart impaired (irregular pulse, blood slow to recover)"); the organ tab adds "impaired" or
    "failed" after the health.
- **Head blow (§3.6).** Blunt ≥ `wolfmed.head_knockout_blunt` (30) in one hit to the head (the whole hit, after armour)
  knocks the patient out for `wolfmed.head_knockout_seconds` (5), cause HeadBlow, checked inside the organ hand-off. It
  is a faint on M1a's machinery: `HeadBlowUntil`/`HeadBlowStart` beside the pain faint's fields, `WolfmedCauses.IsFaint`,
  `InFaint` (the pod keeps a knocked-out occupant anaesthetised and raises no Critical alarm), the countdown alert and
  "Coming round in N s", the analyzer's "FAINTED: head blow, N s", no heartbeat. A blow during the knockout never
  lengthens it. Machines are never knocked out (OD9).
- **OD15 bands.** Electrical: every hit's Shock (whole, after armour) over `wolfmed.electric_heart_from` (15) takes
  `wolfmed.electric_heart_factor` (0.2) per point off the heart, on flesh only (the parts the internal burn forms on),
  read off the broadcast `WolfmedPartDamageEvent`. The 35/50/75% rolls and their fields are gone; the spasm stays.
  Crush: `WolfmedRuleCrushInternalBleed` fires on every Blunt hit of 40 or more (was 40% from 30), a `_WF` rule edit.
  The lodged-round chances stay (flavour).
- **Stumps (P19).** Marked YAML in `Body/Parts/base.yml`: `MajorLimb` Blunt 190 → 400, Heat 250 → 350; `MinorLimb`
  Blunt 150 → 270, Heat 230 → 320. Each is 100 over the highest sever threshold of the limbs that share it (leg Blunt
  300, arm/leg Heat 250, foot Blunt 170, foot Heat 220). And limbs now finish on Blunt 10
  (`dismembermentFinishingDamage`, a separate limb anchor in `parts.yml`; the head keeps the host's 50), so a club
  finishes a limb beaten past its threshold and it comes off with a bleeding stump.
- **Barotrauma (P23).** Marked edit: on a wound host the pressure damage has no origin, so routing lands it on a part
  by weight and M1b's ambient ceiling applies (`ClampToBodyCap`), not the victim's own doll pick.
- **Blast head (P27).** One marked line at the top of `AmputationSystem.HandlePartDamageApplied`: an explosion hit on a
  head does nothing there unless `wolfmed.blast_dismember_head` is on (`WolfmedBodyPartSystem.BlastMaySever`). That one
  place covers both the Onyx per-part explosion roll and an ordinary finishing hit from a blast share.

**Numbers.** `wolfmed.organ_damage_scale` 3.4, `organ_hit_cap` 5, `lung_damage_factor` 1, `consc_brain_down` 0.25,
`consc_core_down` 0.25, `injury_down_pressure` 0.75, `head_knockout_seconds` 5, `head_knockout_blunt` 30,
`electric_heart_factor` 0.2, `electric_heart_from` 15; organ `impairedBelow` 0.5, heart `impairedRegenFactor` 0.5;
crush internal bleed from Blunt 40; limb rungs above; limb Blunt finishing 10.

**Measured** (`WolfmedConsequencesTest`, the shipped values pinned).
- **Calibration, rifle round (Piercing 14) to an unarmoured chest**, organ health after each hit (of 15):

  | Hit | Lungs | Heart | Liver | Stomach | Kidneys |
  |---|---|---|---|---|---|
  | 1 | 13.12 | 14.06 | 13.44 | 14.27 | 14.34 |
  | 4 | 7.48 (impaired) | 11.24 | 8.76 | 12.08 | 12.36 |
  | 8 | 0 (failed) | 7.48 (impaired) | 2.52 | 9.16 | 9.72 |
  | 10 | 0 | 4.68 | 0 | 6.98 | 7.74 |
  | 12 | 0 | 0.06 | 0 | 3.40 | 4.48 |
  | 13 | 0 | 0 (failed: arrest) | 0 | 1.61 | 2.85 |

  Two bodies took identical damage hit for hit. A hit at the line (Piercing 10) reached nothing, and five rounds on a
  torso already holding its 250 did exactly what they did to a fresh one. In play the chest's other routes arrive
  first: destroyed lungs are real suffocation (hit 8), and each destroyed organ leaves its internal bleed.
- **Head:** a rifle round is (14 - 8) × 0.42 × the brain's 77% share × 3.4 = 6.6, capped at 5, so three rounds to the
  head destroy the brain (catastrophic brain injury), and the Downed rung (under 25%) is passed over. A Blunt-30 blow
  takes 4.5 off the brain and knocks out; four such blows destroy it.
- **IPC:** the core (54% of the chassis torso's organ weight) takes 3.08 a rifle round: under 25% (Downed, cause Core)
  on hit 4, core failure on hit 5, with the pump at 2.5 of 15.
- **Lungs at 30%:** breathing level 0.40; Downed (hypoxia, source Lungs) at 206 s and Unconscious at 247 s in station
  air (derived 207 and 248).
- **Head blow:** the patient was up again 5.8 s after a Blunt-30 blow, measured at half-second polls, with a second
  heavy blow 2 s in; a Blunt-29 blow knocked nobody out.
- **Heart impaired (40%):** 3 u of blood regenerated in 20 s against 6 u for a healthy heart.
- **Electrical:** Shock 35 to an arm took exactly 4 off the heart on three bodies; Shock 15 took nothing.
- **Stumps:** a bat (Blunt 15) took the arm off on hit 18 at 270 stored (sever 250, destruction 400), the leg on hit
  21 at 315 (300 / 400), the hand on hit 11 at 165 (150 / 270), the foot on hit 13 at 195 (170 / 270); a laser (Heat
  16) took the other arm off on hit 17 at 272 (250 / 350). Each left a bleeding stump on its parent.
- **Barotrauma:** 84 vacuum ticks on a body whose doll was on the left arm landed on all 10 parts, the left arm 7 to
  12 times and the torso 17 to 23 (three runs).

**Differs from the plan, and why.**
- **Scale 3.4, not 4.** The plan derived its calibration (lungs impaired after about 4 rifle rounds, heart failing
  after 13-14) with every organ's share fixed. Here a destroyed organ leaves the split, so the survivors take more:
  at 4 the heart failed on the 11th round, outside the test's 12-16. 3.4 gives lungs impaired on hit 4 and the heart
  failing on hit 13, each one hit inside its band. Keeping destroyed organs in the split would have needed their
  weights remembered after deletion; the concentrating rule is also the one that makes an eviscerated or shot-out
  chest dangerous.
- **The head blow uses M1a's faint machinery** (the orchestrator's instruction, which replaces the brief's "its own
  pressure/timer"): its own `HeadBlowUntil`/`HeadBlowStart` beside the pain faint's fields, and every faint reader
  (`IsFaint`, `InFaint`, the countdown, the timed help, the analyzer) covers it. The pain faint's re-arm, cooldown and
  painkiller rules do not apply: a concussion is not ended by an opiate, and the knockout never lengthens.
- **The timed faint text is prototype data** (`helpOutTimed`); playtest 2 had the pain faint's key hard-coded in the
  condition alert system.
- **Blast head veto in `AmputationSystem`, through `WolfmedBodyPartSystem`, not `WolfmedExplosionSystem`.** Confirmed:
  Onyx's per-part explosion roll raises no event `_WF` can cancel (inventory #15's "to confirm"). The candidate pick in
  `WoundDamageRoutingSystem` is not the only way a blast severs a head (a blast share can also be an ordinary finishing
  hit on a severable head), and `HandlePartDamageApplied` sees both. `AmputationSystem` is shared, so the check lives in
  the shared `WolfmedBodyPartSystem` rather than the server-only explosion system.
- **The regeneration seam (inventory #16) lands in M3, not M5.** The impaired heart halves blood regeneration, which
  needs exactly the line M5's radiation marrow route needs. `WolfmedLifeSystem.BloodRegenFactor` is the `_WF` end; M5
  multiplies its own factor in there.
- **Liver clearance and IPC pump cooling bands are not built.** Neither route exists yet: toxin clearance is M5 (which
  the plan says reads "the liver band") and chassis cooling is M4's core-heat route. `WolfmedOrganComponent.Band` and
  `ImpairedBelow` are what they read; the factor fields (`impairedClearanceFactor`, `impairedCoolingFactor`) belong
  with their consumers, so no inert tunable ships.
- **Electrical band per hit, on `WolfmedPartDamageEvent`.** The old roll answered the internal burn's lifecycle, which
  does not carry the hit's Shock. The band reads the broadcast per-hit seam on flesh parts (the parts the internal burn
  can form on). The heart slot is a constant; the behaviour's `organDamageChance`/`organDamage`/`organSlot` are removed.
- **Blunt finishing 10 on limbs.** The plan raises the destruction rungs; alone that leaves clubs (10-20 Blunt) unable
  to finish a severable limb against Onyx's Blunt 50, so they would pound on to the new rung and still destroy it. The
  head keeps 50 (its own anchor).
- **The ambient ceilings follow the rungs.** M1b's per-part ceiling is 0.8 × the lowest Destructible trigger, which is
  now Slash (210 limbs, 180 hands and feet): arm and leg 168 (was 152), hand and foot 144 (was 120), head 400 as before.
  Fire still cannot destroy or sever a limb (Heat 168 of a 250 sever threshold, 350 destruction).
- **Diona limbs** (never severable, `amputationThresholds: {}`) now need Blunt 400 / Heat 350 / Slash 210 to be
  destroyed, like any organic limb.
- **IPC parts** keep their own Destructible (Blunt 190, Slash 210, no ash; `PartIPCBase`), so an IPC limb is still
  destroyed rather than severed by Blunt: P19 is about bleeding stumps and the plan's edit is to `Body/Parts/base.yml`.
- **The pod closes up after a cut-short procedure** (below). Not in the plan; the test that exposed it is red otherwise.
- **New analyzer, examine and HUD text** the plan names only as effects: the "Organs:" vitals line, "impaired"/"failed"
  in the organ tab, "has an irregular pulse", "has unequal pupils and seems confused", Breathing "laboured: lungs
  damaged", the HUD's "CORE INTEGRITY CRITICAL: MOBILITY LOST" (a fixed line; the HUD line takes no word).

**Test migration.**
- `WolfmedOrganTest`: the T-ORG-CAP torso declares an empty `organReach`, since Onyx's roll runs only on a part
  without reach lines; new `OrganDamageFollowsTheReachLineTest` (the line, the formula, the per-hit cap).
- `WolfmedBluntWoundTest.CrushCanBleedInternallyTest`: the band, every Blunt 40 torso blow bleeds inside and no Blunt 35
  crush does, instead of "at least one in 24 at 40%".
- `WolfmedBurnWoundTest.ShockBurnsInsideAndSpasmsTest`: the heart has already lost the band (at least 5) to the Shock-40
  hit; no roll to drive by hand.
- `WolfmedAmputationTest`: comments only (the new rungs; limbs finish on Blunt 10). No assertion moved.
- `WolfmedExplosionTest`: unchanged. `BlastHeadTest` covers the veto.
- The M1b ceiling tests follow the new rungs: `AmbientCeilingTest` (an arm holds 168, not 152; the crossing tick starts
  at 166, not 150; the admin command destroys a limb with Blunt 410, over the 400 rung), `DamageCommandPassesTheAmbientCeilingTest`
  (168), `DionaLimbIsDestroyedNotSeveredTest` (Slash 215 over the unchanged Slash rung, instead of Blunt 195), comments
  in `WolfmedPlaytestTwoTest` and `WolfmedSpeciesSpawnTest`.
- `WolfmedPlaytestFixesTest.AutofixRunsOnceAndThenOnlyForSomethingNewTest`: no change to the test; the pod fix below.
- Tests whose head blow is now a knockout: `PainShockNoArrestTest` and `WolfmedPainTest.PainShockStunsAtThresholdTest`
  put their second Blunt 40 / 60 on an arm instead of the head (a knocked-out body is Critical and takes no stun), and
  `DamageTotalsNeverCritAWoundHostTest` aims its 250 and 350 Blunt at the torso (routed to a random part, it could land
  on the head and knock the patient out, which is the blow's doing, not a damage total's).
- `WolfmedBluntWoundTest.ConcussionFadesOnlyWithTimeTest`: its Blunt-25 head hit now also takes 3 off the brain, and
  a brain under 90% slurs on its own; the test puts the brain back to full so it still reads the wound's stages alone.
- `WolfmedCrawlingActionsTest.CallForHelpTest`: station air, and the call waits 3 s after the fall. Going Downed stuns
  for about 2 s, and a stun stutters (a Goob edit to `TryStun`), which mangled "airlock" in the shouted line at
  random: the test passed alone at the base and failed alone on M3, whose extra random draws (barotrauma's part pick)
  moved the stutter onto the word.

**Found on the way, and fixed: the pod left incisions open.** `AutofixRunsOnceAndThenOnlyForSomethingNewTest` (Blunt 60
on an arm) failed about half its runs before M3 (M1a D traced it to the crush rule's 40% internal bleed). The band makes
the bleed certain, so it failed every run, which let it be traced. Every wound surgery ends with a seal step that closes
the incision its requirement opened, but when the problem it treats goes away before that step (the internal bleed
stopped, the bone mended, the tissue repaired) the surgery stops being valid, the pod completes it without the seal,
and the incision stays open. The next procedure on the part then found the open incision as its bleeding wound and
stalled on it, and the run ended with the arm open. Now a procedure cut short that way queues `SurgeryCloseIncision`
straight after itself (`AutodocSystem.FinishStep`, the same `TryQueueClosure` an abandoned procedure uses). The test
passes alone and in the full run. A closure the pod adds this way (or after an abandoned procedure) is marked a
continuation (`AutodocQueued.Continuation`) and takes no fresh anaesthetic: the pod tops up at every procedure while
the relief has under 20 s left, so two extra closures pushed a third dose in `LongQueueDosesOnceAndWakesThePatientTest`
(45 u against its 30).

**Found, not changed.** `SurgeryStopBleeding` on a bleeding crush wound still stalls after three clamps: the stall guard
compares severities, not bleed rates, so a clamp that is lowering the rate reads as no progress. The pod abandons it,
closes up and goes on; the wound is later repaired by tending. Worth a look with the autodoc.
*[M6: fixed. The signature carries each wound's bleeding severity, which only treatment lowers.]*

**For the owner's playtest.** Three rifle rounds to the head are catastrophic brain injury, and five to an IPC's torso
are core failure. Both follow from the plan's reach lines and the calibration scale; if either is too quick, the head's
and the IPC torso's `organReach` (data in `parts.yml` and `species_parts.yml`) or `wolfmed.organ_hit_cap` are the knobs.

**Tests.** Final full filter (`_Onyx.Wounds|Wolfmed|GibTest|Tests.Body|Autodoc`): 439 total, 433 passed, 0 failed,
6 skipped (dirty-disposed: `SlipOpensOneSmallWoundTest`, `CutClothingUnblocksTheProcedureTest`,
`VisualStateFollowsTheLidTest`, `PodChargesAgainAfterAFailedShockTest`, `AutofixStopsReplanningABodyItIsNotChangingTest`,
`FaintedOccupantIsAnaesthetisedTest`); each skipped test passes alone. Two tests failed once each in earlier full runs
and pass alone and in the final run: `OxygenScenarioTest` (M1b saw the same) and `HeartbeatTracksLocalPlayerCritTest`.
Mid-session another worktree rebuilt the shared RobustToolbox, and a run failed to load types until this project was
rebuilt; the final run is on a fresh build.

## M4 (2026-09-24)

Species and IPC death. Plan: `WOLFMED_DEATH_PLAN.md` §12 M4, §9, §3.11, §11 (M4 rows); the owner's answers of 2026-09-24:
OD16 (a) parity with Synth **mechanical**, OD10 (b) (the core-heat route; core repair shipped in M2), OD3 (b) (Succumb in
thermal shutdown). Branch `Wolfmed-m4`, from `7baf456a14`.

**The measurement, first** (`IpcFireScenarioTest`'s trace; the plan asked for the core-heat rate to be set against it).
- Under M3 the overheat pulse (20 Heat a second from 383 K, 30 after the IPC's ×1.5 Heat modifier) crossed the torso's Heat
  reach line (20) and went into the core: an IPC's core was destroyed about 30 s into a 10-stack fire, by a hidden second
  route and before any thermal shutdown could exist.
- The chassis in a 10-stack fire in station air: 501 K at 4 s, a peak of about 1140 K at 48 s, then down as the stacks
  fade. Above 500 K for about 125 s untreated, 108 s when put out at 60 s, 88 s at 40 s and 56 s at 20 s.
- So a chassis-temperature line (the plan's "once the chassis passes 500 K") cannot give the plan's target: untreated and
  put out at a minute differ by 15% in time over the line, and the chassis passes 500 K four seconds after ignition, before
  the IPC is even Downed, so "the IPC can pat itself out while still Downed, before 500 K" was impossible.

**What was built.**
- **The core-heat route** (`WolfmedOverheatSystem`, plan §3.11). The positronic core has its own temperature
  (`WolfmedCoreHeatComponent`, shared and networked, ensured on every mechanical wound host). Each second it closes
  `wolfmed.ipc_core_heat_soak` (0.02) of its gap to the chassis, and a working, powered coolant pump takes
  `wolfmed.ipc_pump_cooling` (5 K/s) off it, down to body temperature. The pump's M3 band finally has its consumer: an
  impaired pump cools at its organ's `impairedCoolingFactor` (0.5), a destroyed, missing or unpowered one not at all.
  - Over `wolfmed.ipc_core_heat_k` (500 K) the core loses `wolfmed.ipc_core_heat_rate` (0.2) health a second for every
    100 K it is over the line, and the chassis is in **thermal shutdown**: the `coreheat` pressure, Unconscious (Critical),
    cause `CoreHeat` (tie order Arrest > CoreHeat > Shutdown), Dying. Under `wolfmed.ipc_core_heat_wake_k` (450 K) it
    ends and the chassis comes round on consciousness's own inputs (Downed by pain, in a fire). A core at 0 is core failure
    through the organ path, as before.
  - Steady state (derived): with a working pump the core passes its line only while the chassis holds over
    500 + 5 / 0.02 = 750 K; with an impaired pump 625 K; with no pump or no power, 500 K.
  - **Measured** (`IpcFireScenarioTest`, shipped values): Downed by pain at 6 s, CORE TEMP CRITICAL on the readout before
    the shutdown, thermal shutdown at 41 s, core failure at 102 s untreated. Put out at 60 s: thermal shutdown from 42 s to
    125 s, the core left at 2.8 of 15 (19%: Downed, cause Core, core repair needed). Patted out from 12 s: the core peaked
    at 486 K and never shut down.
- **The pulse no longer reaches the organs.** `WolfmedOrganThresholdSystem.WithoutOrganReach` wraps the pulse, so heat
  reaches the core only by the core-heat route (principle C). The pulse still burns the parts, which is what Downs a
  burning chassis by pain; it never kills (`OverheatBurnsInsteadOfKillingTest`).
- **Succumb and Last Words in thermal shutdown** (OD3 (b), plan §5.4). `IsDying` is arrest or thermal shutdown; the
  overheat system grants and revokes the actions by direct call, as arrest does. Succumb keeps its order: the core to 0
  where it sits, `Kill`, the arrest ended, the thermal shutdown ended on the corpse, a returnable ghost. The dialog: "Your
  core is overheating. Let go and your chassis dies now: core failure. Your core stays in your chassis, and you stay you; a
  technician can bring you back with core repair and the restart button." The `ghost` command in thermal shutdown opens it.
  Wait as a ghost is never offered in thermal shutdown (Succumb covers it).
- **What people read.** The readout's banner is the cause's line, "THERMAL SHUTDOWN: CORE DAMAGE"; before it, a
  "CORE TEMP CRITICAL" fault from `wolfmed.ipc_core_heat_warn_k` (400 K, core or chassis); the SYSTEM block's CORE row is
  now the core's own temperature. The analyzer: "THERMAL SHUTDOWN: core overheating", "Temperature: core N K, chassis M K"
  while either is over the warning line, and the route "core overheating (put the fire out, cool the chassis)". Examine, at
  any range: "is smoking, too hot to touch". The alert `WolfmedOutCoreHeat`. Coming out: "Core temperature back under the
  line. Systems online.", or "… Still down: {cause}." when something else holds the chassis. The card's bar (for a
  player who turned the readout off) is what is left of the core.
- **Organ data (plan §9.2).** Marked parent edits: A′ lungs (Feroxi, Harpy, Goblin, Hydrakin); B hearts (`OrganAnimalHeart`,
  `OrganArachnidHeart`) and the arachnid's `OrganAnimalLungs`; C brains, hearts and lungs (protogen, which covers every
  Proto- subspecies and ProtoThaven; skrell; the diona's nymph brain and lungs; the slime's `SentientSlimeCore` as its brain,
  and its gas sacs). **A balance change to announce:** moths, reptilians, vulpkanin, canines, felionoids, tajaran, rodentia,
  arachnids (and anything else on the animal heart) can now arrest from heart trauma, and the group C species arrest, run a
  brain clock and die when the brain is taken.
- **Circulatory collapse** (OD16, C′). `WolfmedConsciousnessComponent.Heartless` (networked) is set when an arrest starts on
  a body whose prototype has no heart in any slot (Diona, the slimes). The arrest is the same state with the same triggers
  and routes; its words are the `CirculatoryCollapse` cause prototype's (`WolfmedCauses.PrototypeId`): "Circulatory
  collapse: blood loss", "Your circulation has collapsed.", "Your circulation returns.", the Succumb dialog, the analyzer's
  "CIRCULATORY COLLAPSE: blood" and "Breathing: none: circulatory collapse", the alert `WolfmedOutCollapse` and the banner
  "YOUR CIRCULATION HAS COLLAPSED". A human whose heart is taken out is not heartless: that is arrest, cause heart.
- **A slime survives decapitation** (plan §9.3). Its head is marked `vital: false`, since its core is in the torso.
- **Group D wound hosts** (OD16). Shadekin and ProtoKin get `WoundHost` and `PainShockTarget` (ProtoKin also `LayingDown`
  for the crawl); they are not reparented, because `BaseMobSpeciesOrganic` would give a respirator to species built without
  lungs. For parity with the organic base: their passive regeneration is neutralised (D29), their Blunt gib rises 400 → 1500
  (D22, the body total is the sum of the parts), and their body-level Heat 1500 ash is removed: the living per-part ceilings
  sum past 1500, so a long fire would have burned the body away, which OD12 rules out.
- **Synth, the machine ladder** (OD16, the plan's M4-mechanical branch). The parts take the IPC wound profile
  (`WolfmedPartIpc` first in `PartSynth`'s parents; the torso and head keep the organic reach lines and caps), the ccu is
  the positronic core (`WolfmedOrganPositronicBrain`), the heart is the coolant pump (`WolfmedOrganIpcPump`), and the mob
  gets `WoundHost`, `PainShockTarget` and the restart button (`DeadStartupButton`). `IsMechanical` recognises the
  `SynthBattery`; `HasPower` reads its `Unpowered` flag, polled once a second by the shutdown system because the battery
  raises nothing another system may subscribe; the readout's power reads the cell in the battery organ slot. Its core sits
  in the head's brain slot, so it gets its own core repair on the head (`SurgeryRepairSynthCore`: wrench, multitool,
  welder, the same steps as the IPC's) and the organic brain repair is excluded on a synth.
- **The conformance test is strict** (plan §9.1). `WolfmedSpeciesConformanceTest.EverySpeciesConformsOrIsExcusedTest`: the
  `KnownGaps` array is gone. The checks are an enum (`WolfmedSpeciesCheck`); the deliberate differences are
  `wolfmedSpeciesException` prototypes (`Body/species_exceptions.yml`: the species id, whether it runs the machine ladder,
  the checks it is excused, the reason). The test fails, naming the species, on a failure not excused, on an excuse the
  species no longer needs, on a species on a ladder its exception does not name, and on an exception for a species that is
  not round-start.

**The species matrix, after M4.** 41 round-start species. 35 organic species conform with no exception at all. IPC and
Synth conform on the machine ladder, which their exceptions record. Diona, ProtoDionae, SlimePerson and ProtoSlimePerson
are excused one check, Heart: they have none and collapse instead (plan §9.3). Every gap §9.2 predicted and M1a's report
listed (98 across 32 species) is closed.

**Differs from the plan, and why.**
- **A core temperature, not the chassis's.** The plan triggers thermal shutdown on the chassis at 500 K. Measured, that
  cannot separate an untreated fire from one put out at a minute, and it leaves no time to pat the fire out (above). The
  core soaks up the chassis's heat instead; the plan itself names "chassis and core temperature" for the medic and a core
  temperature row for the HUD. The CVar names and lines (500 K, 450 K) are the plan's, read on the core.
- **The rate scales with how far over the line the core is** (0.2 HP/s per 100 K), not a flat HP/s. Simulated on the
  measured trace, a flat rate leaves 78% between the put-out and the untreated core loss, a proportional one 63%, so both
  outcomes keep a margin of about 20%. Hotter cooks faster, which the readout's CORE row shows.
- **The pump cools, and needs power.** Nothing cooled a chassis before; the plan's pump band ("cooling × 0.5, overheats
  sooner") needed a consumer (M3 left `impairedCoolingFactor` for it). A pump with no power behind it does not pump, so a
  chassis whose cell is out while it burns soaks its heat unopposed.
- **The overheat pulse reaches no organ** (above). Not in the plan; the plan's single-route principle needs it.
- **The lung check applies to a body that breathes.** The M1a report flagged a body with no lungs; Shadekin and ProtoKin
  are built with no lungs and no respirator, deliberately, so that is not a gap. A body with a respirator and no lungs
  still fails.
- **The exception list names the machine ladder.** IPC and Synth fail no check, but their entries say they run the
  machine ladder, and the test holds each species to the ladder its entry names (plan §9.3 lists both).
- **Synth core repair and the restart button.** Not in the plan's Synth branch; without them a synth's core failure was
  permanent, against the owner's "no one is unrevivable".
- **Group D parity edits** (passive regeneration, the gib line, the Heat ash) are not in the plan; each applies a rule an
  earlier package set for every organic body (D29, D22, OD12).
- **Numbered for the parallel M5:** `WolfmedCause.CoreHeat` is 20 and `WolfmedRoutes.CoreHeat` is `1 << 15`, so M5's new
  causes and routes can take 15 upwards and bit 10 upwards without a clash.
- **`wolfmed-vitals-cause-shutdown-mechanical` ("shutdown").** A shutdown as a blocker ("THERMAL SHUTDOWN: core overheating
  (also: shutdown)") read "(also: unknown cause)".

**Numbers.** `wolfmed.ipc_core_heat_k` 500, `ipc_core_heat_wake_k` 450, `ipc_core_heat_rate` 0.2 (per 100 K over),
`ipc_core_heat_soak` 0.02, `ipc_pump_cooling` 5, `ipc_core_heat_warn_k` 400; pump `impairedCoolingFactor` 0.5.

**Test migration.**
- `WolfmedSpeciesConformanceTest`: report mode to strict (above).
- `WolfmedOverheatTest.OverheatBurnsInsteadOfKillingTest`: unchanged assertions (the pulse never kills); its summary says
  the core-heat route is the lethal one now.
- `WolfmedSpeciesSpawnTest.ProtogenIsAWoundHostTest`: flipped as its comment asked: the protogen brain, heart and lungs
  carry `OrganDamage`, the rest still do not.
- `WolfmedSyntheticHudTest.SystemBlockCarriesSensorsAndCoreTemperatureTest`: the CORE row is the core's temperature, set
  through the core-heat route.
- `WolfmedSpeciesProfileTest`: nothing needed changing.
- `Scenarios/WolfmedCauseScenarioTest.OverlappingCausesTest`: the IPC branch deferred from M1a (cell pulled in thermal
  shutdown, then cooled).

**Tests.** New: `Scenarios/WolfmedIpcDeathTest.cs` (`IpcFireScenarioTest`, `ThermalShutdownSuccumbTest`),
`Scenarios/WolfmedSpeciesTest.cs` (`SpeciesArrestTest`, `SynthBranchTest`). Full filter (`_Onyx.Wounds|Wolfmed|GibTest|Tests.Body|Autodoc`), final run: 458 total, 451 passed, 0 failed, 7 skipped (all autodoc or blast fixtures, disposed dirty); each skipped test passes alone. An earlier full run had the same counts with a different set of 7 skips, each also passing alone. Measured in the final run: Downed 5.2 s, thermal shutdown 41.3 s, core failure 102.3 s; put out at 60 s the core was left at 3.1 of 15; patted out, the core peaked at 484 K.

**For the M5 merge.**
- `WolfmedCause`, `WolfmedCauseFlags`, `WolfmedCauses.Priority`, `WolfmedRoutes` and consciousness's `PressureCause` switch
  each gained one M4 entry at the end (CoreHeat); M5's entries go beside them.
- Synth is mechanical now, but its damage container is still HardLight's `Synth` (Brute, Burn, Toxin, Genetic, Bloodloss),
  so it takes Poison: M5's toxin route should skip `IsMechanical` bodies, as the plan says for IPCs. Synth also
  regenerates its fluid through HardLight's `SynthBloodstream`, not the bloodstream step M5's radiation factor hooks.
- Diona, the slimes, the protogens and skrell run a brain clock now, so M5's toxin and heat drains reach them.

**Found, not changed.**
- The autodoc knows neither `SurgeryRepairCore` (M2) nor `SurgeryRepairSynthCore`: a pod will not repair a machine's core.
  *[M6: fixed, both on the neuro disk.]*
- A synth's nanite self-repair (`SynthBloodstream`) heals Brute and Burn over time while it has fluid and hunger, which no
  other wound host does; it is HardLight's species feature and was left.
- The plan's "temperature bar" on the readout is the CORE row's number; no graphic bar was drawn.
- A synth's `SynthBlood` is its fluid on the machine ladder, so losing it reads as the oil cause ("hydraulic pressure
  low") at the same 50% and 35% lines. A synth's own fire has not been measured: its chassis has a thermal regulator and a
  lower specific heat than an IPC's, and the core-heat route applies to it unchanged.
- The Release YAML linter was not run: a Release build writes into the RobustToolbox junction this worktree shares with
  the main checkout, which the brief forbids. Every touched YAML file was parsed, (kind, id) pairs checked for duplicates,
  and every prototype loads in the integration server.

**Art debt.** `WolfmedOutCoreHeat` reuses the borg critical icon, `WolfmedOutCollapse` the human dead icon.

## M5 (2026-09-23)

The remaining causes: toxins, radiation, cold and heat. Plan: `WOLFMED_DEATH_PLAN.md` §12 M5, §3.8-3.10, P21, the §11
rows marked M5; the owner's OD13 (a) of 2026-09-24 (toxins and radiation get lethal routes, liver clearance is the one
passive-healing exception, infection and sepsis no longer deal Poison); the fire grace rules as revised. Branch
`Wolfmed-m5`, from `7baf456a14`, in parallel with M4.

**The measurement, first** (`WolfmedTemperatureTest.ColdRoomMeasurementTest`, run on the base before any M5 code). A
naked human's surface temperature, the `TemperatureComponent` reading the atmosphere and the regulator move:
- In air it settles about 22 K over the air within a minute: 293 K air holds 308 K, 273 K holds 295 K, 253 K holds
  275 K, 233 K holds 254 K, 213 K holds 235 K, 173 K holds 195 K. The maps' freezers are 235 K air (about 256 K).
- **In space it reaches 16 K within a minute** (224 K two seconds after spawning, 96 K at 12 s, 49 K at 22 s). The
  default test map behaves the same: it has no atmosphere, so every test body on it is in space.
- A 10-stack fire in station air peaks at 832-836 K at 30-35 s and burns out at 100 s (about 440 K); the surface is
  under 318 K 30 s later. **Put out at its hottest (30 s), it takes 55 s** to get under 318 K.

Read straight off that surface, the plan's lines (for a human 290, 275 and 262 K, below) would stop the heart of
anyone in space within about 3 s, and of a cook in a 235 K freezer within about 35 s: the surface reaches equilibrium in
a minute, so the air alone would decide the state. The plan assumed a core that takes time to cool (§3.10 names a "core
temperature" and asks for this measurement before the lines are picked). Hence the core model below.

**What was built.**
- **Toxins (§3.8, OD13).** `WolfmedToxinSystem` (server). The load is the body's systemic Poison
  (`SystemicDamageComponent`), never wound damage. Consciousness reads it as cause Toxin against two lines: Downed at
  `wolfmed.consc_toxin_down` 60, a toxic coma at `wolfmed.consc_toxin_out` 120 (Unconscious, breathing). The ordinary 0.9
  hysteresis puts waking under 108 and standing under 54. In the coma the brain drains at 1/`wolfmed.brain_toxin_seconds`
  (600) a second, the sepsis shape, and the heart stops through the oxygen trigger, named "toxin" ("CARDIAC ARREST:
  poisoning"). Clearance: a working liver (the organ carrying `LiverComponent`) takes `wolfmed.toxin_clearance` 0.1
  Poison a second out in the life tick; an impaired one × its `impairedClearanceFactor` (new organ field, 0.5 on
  `WolfmedOrganLiver`); a failed or missing one nothing. A liver with no Wolfmed organ data counts as working.
- **Pools separated (OD13).** A spreading infection and sepsis deal no Poison. The profile's `spreadingPoisonPerMinute`
  and `sepsisPoisonPerMinute` are removed rather than left inert. Fever stays; sepsis kills through its own brain drain
  (M2), so a septic patient is not driven twice.
- **Radiation (§3.9).** `WolfmedRadiationSystem` (server). Systemic Radiation at `wolfmed.rad_marrow_stop` 40 stops blood
  regeneration: a × 0 inside `WolfmedLifeSystem.BloodRegenFactor`, the seam M3 built on the bloodstream's marked line. At
  `wolfmed.rad_marrow_bleed` 100 the marrow also costs `wolfmed.rad_marrow_rate` 0.1 u/s, split out of the blood solution
  in the life tick (no puddle) and counted in the analyzer's trend and transfusion numbers. At `wolfmed.consc_rad_down`
  80 the patient is Downed, cause Radiation ("radiation sickness"), never Unconscious; it kills through the blood route.
  Machines: no marrow and no radiation cause. A chassis does carry Radiation (its container takes it; the test's IPC
  kept 50 of 100 after its modifier); it stays the chassis's slowdown and pain.
- **Cold and heat (§3.10).** `WolfmedBodyTemperatureSystem` (server, its own one-second update).
  - **Lines per species** from the `TemperatureComponent` damage thresholds (a protecting container's while inside
    one): hypothermic (Downed) under cold + `wolfmed.hypothermia_down_offset` 30 K; Unconscious, breathing, under
    + `hypothermia_out_offset` 15 K; the heart stops, cause "cold", under + `hypothermia_arrest_offset` 2 K. Heat
    exhaustion (Downed) over heat − `wolfmed.hyperthermia_down_offset` 7 K; heat stroke (Unconscious) over the heat
    threshold, with a brain drain of 1/`wolfmed.hyperthermia_brain_seconds` (300) a second and an arrest through the
    oxygen trigger named "heat". A human: 290, 275 and 262 K; 318 and 325 K.
  - **The core temperature** the lines read (deviation 1): it follows a warmer surface at once, and above the normal
    temperature it follows the surface down at once; below normal it closes the gap to a colder surface over
    `wolfmed.core_cooling_seconds` (900). A body starts with a normal core. A container that protects from cold damage
    (the cryo pod) holds it, so a cryo patient is neither hypothermic in the pod nor arrested on the way out (read
    from `ParentColdDamageThreshold`, which the pod's `ContainerTemperatureDamageThresholds` sets; no test drives a
    real pod).
  - **Fire grace**, exactly as revised: granted on catching fire unless the body already holds a heat cause; it holds
    while burning and `wolfmed.heat_fire_grace_seconds` after the first time the fire goes out; re-ignition never moves
    that end; it only delays entering a heat cause and never clears one; a new one comes only once the grace is over and
    the core is back under the heat exhaustion line. Default 60 s, not 30 (deviation 3).
  - An input is the core's offset from normal over the line's offset from normal (1 at the line), with the ordinary
    0.9 hysteresis, and nothing at or under `wolfmed.temperature_input_floor` (0.5) of the way: a body a couple of
    kelvin off normal in ordinary air gets no notch on the health doll and costs no polling.
  - Cold arrest keeps the brain's existing cold protection (× 0.1 on the drain and on tissue loss). The paddles and the
    pod refuse a core still under the arrest line ("Shock refused: core temperature 250 K. Rewarm above 262 K first.");
    a heart restarted there would stop again on the next tick (addition 5). A corpse's core keeps following its surface
    for that gate.
- **The causes.** `Toxin` 16, `Radiation` 17, `Cold` 18, `Heat` 19, in the plan's tie order (… Sedation > Toxin > Cold >
  Heat > Radiation > … Brain). Prototypes in `Consciousness/remaining_causes.yml`, alerts in `Alerts/remaining_alerts.yml`
  (stock toxin-gas and temperature icons, art debt), text in `remaining-causes.ftl`: "Downed: poisoned", "Unconscious:
  toxic coma", "Downed: radiation sickness", "Downed: hypothermic", "Unconscious: hypothermia", "Downed: heat exhaustion",
  "Unconscious: heat stroke", each with symptom, help, blocked forms and transition lines. Hypoxia gains the sources
  toxic coma and heat stroke; arrest gains cold, poisoning and heat stroke.
- **What the medic reads.** Analyzer: "Toxins: 72, high; liver clearing" (low / high / comatose, brain at risk; the liver
  clearing / impaired, clearing slowly / failed / missing), "Radiation: 120, marrow failing: blood not regenerating,
  losing 0.1 u/s" (or "marrow suppressed"), "Core temperature: 271 K, hypothermic" (or "overheating") while the core
  counts, and "Defib: refused: core 250 K, rewarm above 262 K first". Routes: "toxic coma (antitoxin)", "heat stroke (cool
  them, now)", "marrow failing (anti-radiation drugs, blood)", "still cooling (warm them)"; they also withdraw "wait as a
  ghost" and tell a waiting ghost. Examine, close up, while the cause holds the body: retching and sweating; grey and
  sickly, bruising at the gums; cold and stiff to the touch; skin hot and dry.
- **P21, acid residue.** `WolfmedChemicalBurnSystem` marks the part and the wound the residue is biting through
  (`ResidueTarget`) while it deals its damage, and `WolfmedWoundRuleSystem` puts that damage into that chemical burn
  instead of a plain burn. The residue deepens its own burn, whose stages bite harder, until it is washed off.
- **Guidebook.** Infection no longer "takes toxin damage" (Wounds.xml, WoundTreatment.xml, the treatment card); one
  paragraph on the four causes.

**Numbers.** `wolfmed.consc_toxin_down` 60, `consc_toxin_out` 120, `brain_toxin_seconds` 600, `toxin_clearance` 0.1,
`rad_marrow_stop` 40, `rad_marrow_bleed` 100, `rad_marrow_rate` 0.1, `consc_rad_down` 80, `hypothermia_down_offset` 30,
`hypothermia_out_offset` 15, `hypothermia_arrest_offset` 2, `hyperthermia_down_offset` 7, `hyperthermia_brain_seconds`
300, `heat_fire_grace_seconds` 60, and new: `core_cooling_seconds` 900, `temperature_line_max_share` 0.6,
`temperature_input_floor` 0.5; liver `impairedClearanceFactor` 0.5.

**Measured** (the M5 tests, shipped values pinned).
- **Poison.** 60: Downed at once. 130 with no liver: Unconscious and breathing, the brain drained 0.100 in 60 s, and the
  heart stopped at 511 s, cause "toxin" (derived 510 s). A working liver cleared 6.00 in 60 s, an impaired one 3.00.
  130 on the liver alone: awake at 221 s (load 107.9; derived 220 s), under the Downed line at 761 s (derived 760 s).
  15 u of dylovene at 130: awake at 20 s (load 107.0), standing at 67 s (load 53.3).
- **Radiation.** Sixty seconds at 90% blood: no radiation +21.0 u, radiation 40 +0.0 u, radiation 100 −5.8 u (derived
  −6.0 u), the chassis at 100 +0.0 u of oil. 15 u of hyronalin took the dose under 40 in 60 s, and the blood then came
  back 7.0 u in 20 s. Derived from those rates for a human at radiation 100 from full blood: Downed by blood at 25 min,
  Unconscious at 32.5 min, arrest at 35 min; Downed by radiation sickness at once.
- **Hypothermia.** The 235 K freezer (surface 256 K), fast-forwarded: Downed at 419 s, Unconscious at 943 s, arrest
  (cause "cold") at 1980 s, each the second the core's cooling derives; the arrested brain drained at exactly a tenth of
  the arrest rate; the paddles refused until the core was rewarmed, then shocked; a patient rewarmed to 305 K woke the
  next second and stood. **Space, real time** (`SpaceColdSmokeTest`): hypothermic at 69 s, Unconscious from cold at
  126 s, cold arrest at 172 s (derived from the recorded surface 75, 125 and 171 s). The re-run measurement: in 173 K air
  Downed at about 195 s, in 213 K air just after five minutes; 233 K and warmer air do not Down a naked human within five minutes.
- **Heat stroke.** At 330 K the brain drained 0.200 in 60 s and the heart stopped at 256 s, cause "heat" (derived 255 s).
  The heat cause came 61 s after a fire went out, at the shipped 60 s grace; re-igniting 15 s into the grace moved nothing;
  a heat stroke caught fire and stayed; a still-hot body's second fire got no grace, a cooled one's did.
- **Acid residue.** Ten residue ticks: the chemical burn 20 → 35, the plain burn on the same torso 30 → 30.
- **Species lines.** Reptilian (cold threshold 285 K, normal 310 K): 300.1 / 292.5 / 286.0 K. Avali (normal 261 K, heat
  threshold 310 K): 303 / 310 K, fever ceiling 282 K. Human, reptilian and avali stand in station air.

**Differs from the plan, and why.**
1. **A core temperature, not the surface reading.** See the measurement. `wolfmed.core_cooling_seconds` 900 gives space
   about a minute before hypothermia, two before unconsciousness and three before the cold arrest, close to the blood
   arrest barotrauma gives (M1a playtest 1: about 205 s) but on a route that protects the brain; and a naked cook seven
   minutes in a freezer before going down. Warming follows the surface at once, so rewarming wakes a patient as soon as
   they are somewhere warm; heat reads the surface as it is, so the measured fire numbers hold.
2. **The offsets shrink for a species whose normal temperature is near a threshold** (`wolfmed.temperature_line_max_share`
   0.6: no Downed line further than 60% of the way from the threshold towards normal, the other lines scaled alike).
   At the plan's fixed offsets a reptilian, an asakim or a Proto reptile (cold threshold 285 K, normal 310 K) would be
   hypothermic at its own normal temperature (Downed under 315 K). A human's lines are exactly the plan's.
3. **Fire grace 60 s, not 30.** A fire that burns out leaves a surface under the heat exhaustion line 30 s later; one put
   out at its hottest takes 55 s. At 30 s, putting someone out promptly, the right first aid, earned them heat stroke 30 s
   later. The rules are exactly as revised; the test pins the shipped 60 and checks the grace ends on it.
4. **A fever never Downs, for every species.** The infection's fever climbs to the profile's 313 K, which for a species
   with a normal temperature of 261 K (avali, some protogen subspecies; heat threshold 310 K) is heat stroke. It now stops
   at the point where the species' heat starts to count (the input floor), which leaves the human fever at 313 K.
5. **Additions the plan implies but does not name:** the TooCold defib refusal; heat stroke's arrest named "heat" (the
   plan's list of arrest causes has cold and toxin); the analyzer's core temperature line; four routes (toxic coma, heat
   stroke, marrow, still cooling), so "wait as a ghost" is withdrawn for them as §5.4 asks; examine signs for each cause.
6. **Enum values leave room for M4.** `WolfmedCause` 15 and `WolfmedRoutes` bit 10 are unused, for M4's core heat, so the
   parallel branches do not collide on a number. Sources: hypoxia `Toxin` 10 and `Heat` 11; arrest `ArrestCold` 30,
   `ArrestToxin` 31, `ArrestHeat` 32.
7. **No marked edit.** Inventory #16 landed in M3; the radiation factor multiplies into the same method.
8. **The cold and heat inputs tick on their own one-second update, not the life tick**, because the surface they follow
   moves only in real time; `WolfmedBodyTemperatureSystem.Tick` is the test seam. Toxin clearance and the marrow loss run
   in the life tick (`WolfmedScenario.Advance` fast-forwards them).
9. **Tests the plan does not list:** `ColdRoomMeasurementTest` (the opening measurement, records only),
   `SpaceColdSmokeTest` (the milestone's real-time smoke test), `SpeciesLinesTest`.

**Test migration.**
- `WolfmedInfectionTest`: the spreading stage runs a fever and deals no Poison; `SepsisPoisonsAndAntibioticsClearItTest`
  is now `SepsisShowsAndAntibioticsClearItTest` and asserts no Poison.
- `WolfmedBurnWoundTest.ChemicalBurnsTickUntilWashedTest`: unchanged and green; `AcidResidueTest` carries P21's assertion.
- `WolfmedBleedingLifecycleTest`: reads no regeneration; nothing to migrate (the seam was M3's).
- `WolfmedLocaleCoverageTest`: the route and dormant-route families pick up the four new routes by themselves.

**Found on the way.**
- **A test's own output can vanish.** `TestContext.Out` written inside a server callback, while other tests run in
  parallel, sometimes lands in no test's output. The M5 tests collect their lines and write them from a `[TearDown]`.
- **The default test map is space for temperature.** Every existing test body on it cools like a spaced crewman. The
  full filter stayed green: none holds a body there long enough to reach the hypothermia line (about a minute).

**Tests.** New: `Scenarios/WolfmedRemainingCausesTest.cs` (`ToxinScenarioTest`, `RadiationScenarioTest`,
`SepsisNotToxinTest`, `AcidResidueTest`), `Scenarios/WolfmedTemperatureTest.cs` (`ColdRoomMeasurementTest`,
`HypothermiaScenarioTest`, `HeatStrokeScenarioTest`, `SpeciesLinesTest`, `SpaceColdSmokeTest`).
Final full filter (`_Onyx.Wounds|Wolfmed|GibTest|Tests.Body|Autodoc`): 463 total, 456 passed, 0 failed, 7 skipped
(dirty-disposed: `AcidResidueTest`, `AutofixStopsReplanningABodyItIsNotChangingTest`,
`BrainDeadOccupantIsOperatedOnWithoutHoldingTest`, `EmbeddedObjectIsRemovedBeforeAnythingElseOnThePartTest`,
`PainkillerPenTest("WolfmedAnalgesicPen",Weak)`, `StalledProcedureIsAbandonedAndNeverReplannedTest`,
`VisualStateFollowsTheLidTest`); each passes alone. The run before the 60 s grace and the fever ceiling: 462 total,
454 passed, 0 failed, 8 skipped, each passing alone.

**For the merge with M4.**
- `WolfmedCause` 15 and `WolfmedRoutes` bit 10 are free for CoreHeat. Both branches append to the tie order array, the
  arrest source switches in `WolfmedLifeSystem.ArrestSource` / `GetRestartMemory` and `WolfmedConsciousnessSystem.GetSource`,
  `WolfmedOrganComponent` (M5 `ImpairedClearanceFactor` beside M4's `ImpairedCoolingFactor`), `organs.yml` and the CVars.
- Mechanical bodies get none of the M5 inputs (`WolfmedShutdownSystem.IsMechanical`); a Synth made mechanical in M4 is
  covered by the same check. The IPC core-heat route is M4's and reads nothing here.
- Species conformance (M4) does not check temperature lines; `SpeciesLinesTest` covers the three species it names.

**For the owner's playtest.** A chemistry poisoning (dylovene wakes a toxic coma in about 20 s), a radiation leak (at 100
the blood starts going; 40 already stops it coming back), a freezer (naked: down in 7 minutes, out in 16, the heart at
33), space (down in about a minute, the heart about 3 minutes in, cold arrest protects the brain: rewarm, then shock), a
hot room, and a fire (no heat stroke, whether it burns out or is put out). Knobs: `wolfmed.core_cooling_seconds`, the
offsets, `wolfmed.heat_fire_grace_seconds`.

## M6 (2026-09-24)

Leftovers. Plan: `WOLFMED_DEATH_PLAN.md` §12 M6, §11 (M6 rows), P17, P25, P30, the rundown's Appendix A; the owner's
OD18 (a) of 2026-09-24. Also the small leftovers earlier milestones flagged: the pod and a machine's core (M4), a
synth's Poison (M4), Onyx's uncalled `TryApplyLethalDamage` (M2), the pod's stop-bleeding progress check (M3). Branch
`Wolfmed-m6`, from `a76ecd98f5`.

**What was built.**
- **Being hit interrupts (OD18, P17).** `WolfmedDoAfterInterruptSystem` (server) is called once per hit from
  `WolfmedPartHitSystem.OnHit`, the dispatcher's existing hand-off. A hit whose Total (after armour, stored or not)
  is `wolfmed.doafter_interrupt_damage` (10) or more cancels the do-afters the hit body is performing: treatment
  (`HealingDoAfterEvent`, the tourniquet) and surgery steps (`SurgeryDoAfterEvent`) always, and anything that asks to
  break on damage (cuffing, prying, injecting, Wolfmed's splint, embedded removal, cautery and joint relocation, which
  all asked for it and never got it on a wound host). The hit carries the caller's `interruptsDoAfters` on
  `PartDamageAppliedEvent.InterruptsDoAfters` (one marked Onyx field), so the ticks upstream already marks as not
  interrupting (fire, temperature, barotrauma, the bloodstream) never count; systemic damage (Bloodloss, Poison) lands
  on no part and never counts. The IPC overheat pulse now passes false like fire; an explosion passes true, as it does
  upstream (it passed false before).
- **The routed pass keeps the caller's arguments (P25).** `BeforeDamageChangedEvent` carries `IgnoreResistances`,
  `InterruptsDoAfters` and `PartMultiplier` (one marked upstream line at construction, one at the record). Routing
  passes the first two to its routed pass instead of the defaults, skips the part's armour (`PartDamageModifyEvent`)
  when resistances are ignored, and scales the localized damage that reaches the part by Shitmed's part multiplier,
  once, before the armour.
- **Fracture grade (P30).** A fracture is created at the trauma that graded it (the hit plus the part's earlier Blunt ×
  `accumulationMultiplier`), not at the hit alone, which sat under the fracture's own lowest grade: a fracture from
  accumulated damage had no grade, no penalty, could not be treated and shadowed any other limb penalty on the part.
  One marked Onyx line in `WoundFractureSystem.HandlePartDamageApplied`.
- **Necrosis risk multiplier (P30).** A wound's `riskMultiplier` divides its onset
  (`WolfmedWoundTraitSystem.GetPartNecrosisOnset`, the soonest over the part's wounds); it was only ever read as "is
  there a risk". Data unchanged, so frostbite frozen through (risk 1.5, onset 300 s) kills the limb in 200 s and
  charring (risk 1, 480 s) keeps its 480 s. Tourniquets and late reattachment are unchanged.
- **The pod repairs a machine's core** (M4's leftover). `SurgeryRepairCore` and `SurgeryRepairSynthCore` are on the
  neuro disk beside brain repair, in the neuro category and in the planner's organ-repair step, with no anaesthetic
  or antibiotic (`autodocProcedure`), and the pod carries a multitool for the re-flash step. The pod test found that
  both surgeries went invalid the moment the core was whole, so neither the pod nor a surgeon could reach the weld that
  closes the housing: the organ condition's new `validWhile` (the open-housing marker) keeps them listed until it is
  welded.
- **The pod sees a clamp working** (M3's leftover). The stall guard's part signature carries each wound's bleeding
  severity, which does not drift (only a treatment lowers it and only a new injury raises it), so a clamp that is
  closing a bleed is progress; a bleed needing five clamps is closed in one procedure instead of abandoned after three.
- **A synth's Poison runs no toxin route** (M4's leftover): confirmed, no change needed. Consciousness, the analyzer and
  liver clearance all skip `IsMechanical` bodies, and a synth has no brain clock to drain. The Poison itself still lands
  (HardLight's `Synth` container takes the Toxin group) and stays, since nothing clears a machine's Poison.
- **Onyx's `TryApplyLethalDamage` is removed**, with the routing's `MobThresholdSystem` dependency it alone used: its
  one caller (HOOK 13) went in M2.
- **DECISIONS.md corrections** (the rundown's Appendix A and what later milestones changed): marked inline as
  *[M6 correction: …]* where each stale statement stands, so the history reads as it was decided and the note says
  what the game does now. The stale comments the appendix names are corrected in place (the tourniquet's Asphyxiation
  in `_WF/Wolfmed/Damage/containers.yml`, the IPC profile's "inert" Cold, Caustic and organ damage in Onyx's
  `wounds.yml`, the `"airloss"` pressure key in the consciousness system and component).

**Appendix A, row by row.**

| Topic | Where | Now |
|---|---|---|
| Consciousness thresholds | "Consciousness" | corrected inline: pain 0.95 / 1.4, blood 0.5 / 0.35 |
| EMP 15/45, three EMPs kill an IPC | "EMP and machine bodies" | corrected inline: 40/120, no damage total kills an IPC |
| IPCs die at 100 | "Playtest fixes", "EMP" | corrected inline: core failure, core or head lost, gib |
| Body cap scope | "Playtest fixes" | corrected inline: no origin, not an explosion; per-part ceiling and corpse ceiling since M1b |
| Aim scatter 0.15/0.9 | "Aim scatter" | corrected inline: 0.1/0.75 |
| Why Bloodloss is uncapped | "AUTODOC5" | corrected inline: the figure kills nothing; vital-part loss is death through the amputation handler |
| Heavy round always lodges | "Final stages" | corrected inline: 35 % / 20 % rolls, kept as flavour (OD15) |
| Blast head only with the CVar | "Blast dismemberment" | true since M3 (inline note) |
| IPC oil costs nothing | "Playtest fixes" | corrected inline: oil 50 % Downs, 35 % shuts down (M1a) |
| IPC pain | "Phase 5" U1 | the entry was right, the CONSC report wrong; pain only Downs a machine (M1a) |
| Pickup while Downed | "AUTODOC5" | true since M1a (inline note) |
| CPR never reaches the damage band | "Brain death" | corrected inline: 289 s, CBI 534 s |
| Pod shocks | "Brain death" | corrected inline: also every 5 s, up to 5 |
| IPC brain: repair, then a jolt | "Brain death" | corrected inline: core repair, then the restart button (M2), pod since M6 |
| Rejuvenate the only revival | "Consciousness" | corrected inline: defibrillator and restart button too |
| "Not breathing" | "Brain death" | corrected inline: the Airloss group; replaced in M1a |
| Overheat kills the pump first | code comment, manifest | the code comment was rewritten in M4; the manifest row is annotated |
| Tourniquet Asphyxiation on the part | `containers.yml` comment | corrected: it goes to the systemic pool |
| IPC organ damage, Cold, Caustic inert | Onyx `wounds.yml` comments | corrected: live |
| Acid residue cannot deepen a second burn | `burns.yml` | true since M5 (P21) |
| `"airloss"` pressure key | consciousness comments | corrected: gone since BRAIN |

Also annotated inline, because M1a, M2 and M5 changed them: the arrest triggers (pain-shock arrest and sepsis roll
gone, electrocution after insulation, cold, toxin and heat added), the defib gate (0.25) and post-shock oxygenation
(0.5), D34 (closed by OD18), and M2's, M3's and M4's "left in place" / "not changed" notes this milestone closes.

**§11, the M6 rows.** P17 fixed (OD18). P25 fixed. P30: fracture grade and necrosis multiplier fixed; the stage
functionality and the `wolfmed.consciousness` toggle stay dropped, as the plan decided. The deferred list stays deferred,
because the plan makes each "if still wanted" and nobody asked: P26 (targeting), the zombie flicker (P29), airway
obstruction, and the Cellular route (P12). On P12, for the owner: content does deal Cellular (unstable reagents, some
gases, a Mono projectile with Cellular 5), and on a wound host it still has no route.

**Numbers.** `wolfmed.doafter_interrupt_damage` 10 (new). Necrosis onsets now effective: frostbite 200 s, charring
480 s (data unchanged).

**Differs from the plan, and why.**
1. **Break-on-damage do-afters are interrupted too**, not only self-treatment and surgery, at the same one-hit line
   (upstream's own default threshold is 1 per damage event). P17 names cuffing, D34 had accepted losing exactly this, and
   Wolfmed's own treatments already asked to break on damage. A hit under 10 interrupts nothing on a wound host.
2. **"Self-treatment" is the treatment the hit body is doing**, to itself or to someone else: a medic shot while
   bandaging is interrupted. A hit on the patient does not stop the surgeon's step, because a do-after belongs to the
   one performing it (upstream's rule).
3. **Inventory #18 is one field on the Onyx event**, `PartDamageAppliedEvent.InterruptsDoAfters`, and its argument where
   routing raises the event; the handler is `_WF`. That is the plan's "to confirm".
4. **P25 needed an upstream edit** in `DamageableSystem.cs` as well as inventory #17's Onyx lines: routing takes the hit
   from `BeforeDamageChangedEvent`, which carried none of the three arguments.
5. **The part multiplier scales damage, not healing.** Shitmed scales both; on a wound host the only healing multiplier
   that is not 1 is the Shitmed tend's 2.5, which HOOK 27 already replaced with direct wound treatment.
6. **Balance consequences of P25, stated plainly.** A heavy (wide) melee swing deals its part multiplier (0.5) to a
   wound host, as Shitmed meant. Callers that ignore resistances now skip the part's armour too: explosions (whose
   armour is the explosion resistance applied before the damage arrives; wound hosts got part armour on top, a second
   reduction), EMP Shock on chassis parts, temperature and barotrauma damage (which also stop taking the species'
   modifier set, as upstream intends), and the admin `damage` command with its ignore-resistances flag.
7. **The overheat pulse and explosions' `interruptsDoAfters`** changed (false and true), so each is what it is: a tick
   and a hit.
8. **Fracture: fixed, not removed.** Necrosis multiplier: fixed, not removed; the data keeps its values, so frostbite is
   faster than it was (200 s against 300 s), as the field always said it should be.
9. **Core repair's `validWhile`** is not in the plan; without it the pod left every housing unbolted, and so would a
   surgeon working from the menu.
10. **Where the tests live.** The plan's migration targets: `RoutingPassesIgnoreResistancesTest` in
    `WolfmedDamageBridgeTest`, `FractureGradeTest` in `WolfmedBluntWoundTest`, `NecrosisRiskTest` in
    `WolfmedInfectionTest`; `HitInterruptsSelfTreatmentTest` and the leftovers' tests in
    `Scenarios/WolfmedLeftoversTest.cs`. The milestone's real-time test is `PodRepairsACoreTest` (the pod runs the
    surgeries tick by tick).

**Test migration.**
- `WolfmedDamageBridgeTest`: new `RoutingPassesIgnoreResistancesTest`. The existing armour and penetration tests pass
  unchanged.
- `WolfmedBluntWoundTest`: new `FractureGradeTest` (a certain-creation copy of `WolfmedFractureProfile` as a test
  prototype, so the grade is not a roll).
- `WolfmedInfectionTest`: new `NecrosisRiskTest`; `TourniquetClockSurvivesATreatedWoundTest` asserts the freeze's
  onset is 200 s.
- `WolfmedTreatmentProcedureTest`, `WolfmedWoundSurgeryTest`: nothing to migrate for the interruption. The surgery
  tests raise step events directly, past the do-after, and the procedure tests deal no damage during a do-after.
- `WolfmedMechanicalWoundTest` (not in the plan's table): its damage helper ignores resistances, and
  `ShortCircuitAndServoDamageTest` (a 21-severity short from the IPC's Shock × 2.5) and
  `OverheatingCoolsRatherThanHealsTest` (48 from Heat × 1.5, then a partial cooling from the chassis's Cold modifier)
  counted on the modifier set applying anyway, which was P25. Those three hits now take the resistances they expect.
- `WolfmedWoundSurgeryTest.SurgeryFractureLadderReducesThenMendsTest` (not in the table): its second Blunt 75 lands on
  an arm already holding 75, so the new fracture starts at 135 (P30), not 75. It is driven down to 15 from wherever it
  starts, and the test asserts it started above 75.
- `Scenarios/WolfmedRemainingCausesTest.AcidResidueTest` (M5's, flaky, not caused by M6): it ran in the default map's
  vacuum, where the body's surface passes the cold damage line about two seconds in; a cold tick under the frostbite
  rule's 8 lands as a plain `BurnWound`, and on the torso that read as the residue's. It failed alone once in the final
  M6 run. It now gets station air, as the other M5 scenarios do, and passed alone three times running.

**Tests.** New: `HitInterruptsSelfTreatmentTest`, `RoutingPassesIgnoreResistancesTest`, `FractureGradeTest`,
`NecrosisRiskTest`, `PodRepairsACoreTest`, `PodClampProgressTest`, `SynthRunsNoToxinRouteTest`.
Final full filter (`_Onyx.Wounds|Wolfmed|GibTest|Tests.Body|Autodoc`, DebugOpt, on the final code): 474 total, 468
passed, 0 failed, 6 skipped (dirty-disposed: `PodClampProgressTest`, `CutClothingUnblocksTheProcedureTest`,
`AutofixStopsReplanningABodyItIsNotChangingTest`, `EmbeddedObjectIsRemovedBeforeAnythingElseOnThePartTest`,
`VisualStateFollowsTheLidTest`, `BrainDeadOccupantIsOperatedOnWithoutHoldingTest`); each passes alone. (The first solo
run of `EmbeddedObjectIsRemovedBeforeAnythingElseOnThePartTest` ended in a test-host "Internal CLR error"; it passed in
three solo reruns after.) Earlier full runs: 474 / 465 / 1 failed / 8 skipped (the short-circuit migration), then
474 / 469 / 0 / 5 skipped; the failures and solo failures they showed are the migrations listed above.

**Found, not changed.**
- A synth's Poison has nowhere to go: no route, no clearance, and HardLight's `Synth` container keeps taking it, so a
  poisoned synth keeps the systemic damage (and its pain) until something heals Toxin on a synth. Whether a machine
  synth should take Poison at all is the owner's species call.
- Acid residue ticks are 1 to 3 Caustic, under the interrupt line, so they never interrupt; a future stronger residue
  would need `interruptsDoAfters: false` (the routing's part entry point takes none).
- The Cellular route stays deferred (above).

**For the owner's playtest** (plan §12 M6). Bandage yourself while being shot (a hit of 10 or more stops it; fire does
not); a heavy swing against a wound host does half; admin `damage` with ignore-resistances passes armour; a pod with
the neuro disk repairs an IPC's or a synth's core; read this file against what the game does.
