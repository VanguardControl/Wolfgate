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

Scope when resumed: client prediction of wound routing (removes the transient damage-number / limb-doll flicker on wound hosts), the `HurtCommand` part argument (patch kept at `C:\Users\jzo12\Documents\Wolfmed\plan\wp\WP8-hurtcommand-deferred.patch`), locational-armour follow-ups. Run it lean: one Opus design+implement agent using `C:SERSJZO12DOCUMENTSWOLFMEDPLANeportsnalysisdamage-bridge.md` and PLAN.md §2/§3, one Sonnet verify at the end.

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
