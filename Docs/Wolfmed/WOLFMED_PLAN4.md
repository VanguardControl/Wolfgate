# WOLFMED PHASE 4 — implementation plan (lead architect)

**Onyx pin:** `2f5bab9946539cbe083010c9ae6fbc59b47ae377`, sparse reference at `C:/Users/jzo12/Documents/Wolfmed/onyx` (**ONYX**).
**Wolfgate worktree:** `C:/Users/jzo12/Documents/GitHub/Wolfgate/.claude/worktrees/rules-motd-updates-11c89c`
(**WG**), branch `clanker/wolfmed-port-orchestration-454c3d`, phases 1–3 committed (`6329d204e3 Phase 3
completion`), RobustToolbox 277 junctioned at `WG/RobustToolbox` — **never touched**.

This document is to phase 4 what `PLAN3.md` is to phase 3. It supersedes the five phase-4 analyst reports
(`reagents.md`, `tools.md`, `surgery.md`, `analyzer.md`, `tests.md`) wherever they disagree; every overruling
is given inline with the evidence that settled it, and every load-bearing claim was independently re-verified
in the live tree during this pass. Agents follow this file literally; the analyst reports are evidence, not
instructions.

**Phase 4 in one line:** everything that lets a medic undo what phases 1–3 inflict — reagents that suppress
pain and mend bone, a real tourniquet, six wound surgeries on Shitmed's step system, an organ-healing path,
a diagnostics readout, and (newly in scope) explosion amputation.

**Ground rules (unchanged from PLAN.md/PLAN2.md/PLAN3.md, restated because they are binding):**

1. Vendored Onyx code keeps its Onyx relative path under `_Onyx/`, its Onyx namespace and its Onyx header
   **where it has one** — note that `TourniquetComponent.cs`, `TourniquetSystem.cs`,
   `MedicalPatchComponent.cs` and `MedicalPatchSystem.cs` have **no** licence header in Onyx; do not invent
   one. Every in-file change is marked `// WOLFGATE` with a one-line reason.
2. New Wolfgate code lives under `_WF/Wolfmed`. `_WF` style: no licence header, `/// <summary>` one-liners,
   `[Dependency] private X _x = default!;` (no `readonly`).
3. Upstream Wolfgate files get one- or two-line `// WOLFGATE` hooks only, **except the sites §3 explicitly
   authorises**. Nothing outside §3 may be edited in an upstream file without escalating.
4. **Never** add a directed subscription for a `(Component, Event)` pair without checking §5 first. RT stores
   one registration per pair for the whole bus and throws `Duplicate Subscriptions for comp=…, event=…` at
   `WG/RobustToolbox/Robust.Shared/GameObjects/EntityEventBus.Directed.cs:407,419` — a server-start crash.
5. Every WP ends with the build checkpoint: `dotnet build Content.Server`, `dotnet build Content.Client` **and**
   `dotnet build Content.IntegrationTests` all green (0 errors), `-c DebugOpt`. YAML lints in **Release** only
   (`ErrorNode` crashes the linter elsewhere).
6. **Packages run SEQUENTIALLY in the one worktree** and each appends its own rows directly to
   `Docs/Wolfmed/WOLFMED_MANIFEST.md` (§7). WP12-10 *reconciles*, it does not merge. One owner per shared file
   (§4, serialisation rules).
7. **No commits.** Work packages leave the tree uncommitted; snapshot a patch per WP under
   `C:/Users/jzo12/Documents/Wolfmed/plan/p4/snapshots/`. The user commits.
8. Before blaming Wolfmed for a test failure, run `DockTest` first (the `db.ef` sqlite warnings fail every pair
   test in this repo — project memory).

---

## 1. Decisions

### 1.1 Earlier decisions that bind phase 4 (restated, unchanged)

| ID | Decision as it applies to phase 4 | Evidence |
|---|---|---|
| **D2** | Entities without `WoundHostComponent` behave exactly as today. Every phase-4 change must be `HasComp<WoundHostComponent>`-scoped or structurally unreachable for non-hosts. **Phase 4 has four D2 pressure points**, all handled below: the tourniquet swap (P4-D11), the two shallow tend surgeries gaining a severity window (P4-D19), the incision-scar chain (P4-D21) and the explosion hook (P4-D14). A prototype edit on a shared abstract is never D2-scoped — the rule that killed HOOK 19. | PLAN §1.1, PLAN3 §1.1 |
| **D4** | Balance = Onyx defaults; tuning later. **Phase 4 ships three deliberate numeric deviations**, each flagged as a user decision in §8.4: the organ-heal amount (P4-D23), honouring `Immediate` (P4-D5), and the reagent Tier-A scope's Oxycodone recipe re-author (P4-D2). Everything else is Onyx's number or Wolfgate's existing number. | PLAN §1.1 |
| **D5** | Missing APIs get a compat shim in `Content.Shared/_WF/Wolfmed/Compat`; where impossible, a `// WOLFGATE` edit in the vendored file. **Phase 4 adds no new compat-layer file.** Phases 1–3 already shipped everything phase 4 needs to bind: `WolfmedDamageableSystem`, `WoundTargetResolver`, `WolfmedBodySystem`, `WolfmedBodyPartSystem`, `PartStatusSeverity`, `DamageDealtEvent` (§2.0). | verified §2.0 |
| **D6** | Layout: vendored Onyx code at its Onyx relative path under `_Onyx/`; Wolfgate glue under `_WF/Wolfmed`; docs in `Docs/Wolfmed/`. | PLAN §1.1 |
| **D7** | Wolfgate keeps Shitmed surgery. Onyx's `_Onyx/Medical/Surgery/SharedSurgerySystem.*` and `_Onyx/Surgery` are **not** ported — 11 partial files plus 4 server files, a second BUI, a second step registry and 22 colliding component names. Phase 4 re-expresses Onyx's *wound surgeries* on Shitmed's step system instead (§2.4–§2.6). | PLAN §1.1, re-confirmed `surgery.md` §2.1 |
| **D8** | Wolfgate stays on Shitmed's `BodyPartComponent`/`OrganComponent`; Onyx's extra fields live on `WolfmedBodyPartComponent`/`WolfmedOrganComponent`. **This is why `OrganHealthSystem.ChangeHealth` takes `Entity<WolfmedOrganComponent>`, not Onyx's `Entity<OrganComponent>`** (`WG/Content.Server/_Onyx/Body/Systems/OrganHealthSystem.cs:80`, read in full here), and why the analyzer's organ builder reads `WolfmedOrganComponent.Health` while addressing the organ through `OrganComponent.SlotId`. | verified directly |
| **D9** | No `BodyPartType.Chest`/`.Groin`. Affects every ported Onyx surgery `part:` field (`Chest`→`Torso`, `Groin` dropped) and the analyzer's `TargetBodyPart` keys — **WG must never emit a `Groin` diagnostic row**: `SharedTargetingSystem.GetValidParts()` has 10 entries with `Groin` commented out, and `ConvertTargetBodyPart` maps Groin→Torso. | verified directly |
| **D13** | Server-only wound systems live in `Content.Server/_Onyx/…` keeping Onyx's `Content.Shared._Onyx.*` namespace. `WoundBleedingSystem`, `WoundInternalBleedingSystem`, `OrganDamageSystem`, `WoundHealingSystem` and `OrganHealthSystem` are all `Content.Server`. **This is what forces `TourniquetSystem` out of `Content.Shared` (P4-D10) and what splits the surgery handlers server/shared (P4-D16).** | verified directly |
| **D16** | Onyx's ECS `EntityEffectSystem<TComp, TEffect>` framework is absent; wound reagent effects are rewritten as old-style `EntityEffect` classes keeping Onyx's `!type:` names. `WG/Content.Shared/EntityEffects/EntityEffect.cs:22,33,48` — `[ImplicitDataDefinitionForInheritors] public abstract partial class EntityEffect`, `protected abstract string? ReagentEffectGuidebookText(IPrototypeManager, IEntitySystemManager)`, `public abstract void Effect(EntityEffectBaseArgs args)`. Both abstract members must be implemented or the class will not compile. | verified directly |
| **D18** | Guards test `HasComp<WoundHostComponent>` directly. No marker component. | PLAN §1.2 |
| **D23 / HOOK 10** | `PartDamageModifyEvent` carries a WG-only trailing `float armorPenetration`. Phase 4 touches the armour path only through P4-D14's origin-flag passthrough; **it must not disturb `WoundDamageRoutingSystem`'s two `before: [typeof(SharedArmorPlateSystem)]` orderings** (`:64`, `:66`). | verified directly |
| **D32** | `WoundHost` is on `BaseMobSpeciesOrganic`; Protogen is stripped at runtime by `WolfmedWoundHostExclusionSystem`. **Protogen is therefore the only humanoid that phase 4's wound-gated treatments cannot reach** — the whole population affected by P4-D11. | P2-D22 |
| **D35** | Wound-host damage routing is unpredicted. Phase 4 adds no prediction: `WoundSystem`'s mutators are `_net.IsServer`-gated (`WoundSystem.cs:284,338,356,401`), `WoundFractureSystem.TrySetTreatment` is gated at `:188`, and `TourniquetSystem`/`MedicalPatchSystem` become server-only. A client running a surgery step effect sees no change until server state arrives — the one-tick lag phase 1 accepted. | verified directly |
| **P2-D16** | **Every numeric literal in a ported test is a prediction until measured.** Phase 4 inherits four literals that must be re-derived rather than copied: `BluntWound` severity from a 20-Blunt hit, `SurgicalIncisionWound`'s bleed rate, the organ-heal repeat count, and `PainSystem.GetPain` after suppression. Put the derivation in a `// WOLFGATE` comment at each assertion. | WP9 §3.2 |
| **P2-D23** | Wound creation can be a dice roll; fracture grades are not. `OrganicFractureProfile.grades` are `Hairline 20 / Simple 35 / Displaced 50 / Comminuted 60` with `creationChance` `0.05 / 0.25 / 0.65 / 1` (`wounds.yml:60-79`, re-read here) — **only a ≥60 fracture hit is deterministic**, which is why every fracture test in phases 2–4 uses 75 Blunt. | verified directly |
| **P2-D24** | Pain-adjacent fixtures need `- type: StatusEffects` with an explicit `allowed:` plus `- type: MobState`. **T-REAGENT-SUPPRESS and T-SURG-PAIN need this**; the fracture and bleeding fixtures do not. | P2-D24 |
| **P3-D2** | `AmputationConsequenceWound` ships inert and `CanAttachPart` is deliberately NOT hooked, *handed to phase 4*. **Phase 4 closes the gap — but not the way P3-D2's forward-reference assumed (P4-D18).** | PLAN3 §1.2 |
| **P3-D3** | Explosion amputation was OUT of phase 3 for three reasons: D24 forbade the hook, the `_routedModifiers` passthrough was missing, and both CVars had no consumer. **DECISIONS P4-6 lifts D24 conditionally; the analysts confirmed the hook is small; P4-D14 takes it.** | PLAN3 §1.2, `tools.md` §3 |
| **P3-D22** | `OrganHealthSystem`'s brain branch stays live: a brain at 0 HP kills the mob and the organ is never destroyed. **Consequence for P4-3: `SurgeryHealBrain` can save a dying brain but a dead patient's brain is still present, so the surgery remains listable on a corpse.** Record, do not "fix". | PLAN3 §1.2, verified |
| **§8.6-1** | Guns and lasers can sever (Piercing finishing minimum 12, Heat thresholds added per part, Heat finishing minimum 15). **The guidebook text phase 4 writes must say so** (P4-D15). | DECISIONS phase-3 answers |
| **§8.6-4** | "Organ damage ships irreversible" was a phase-3 record, not a permanent design. **P4-3 makes it "reversible while the organ lives"** — `OrganHealthSystem.Update` destroys any organ at `Health <= 0` on the next tick, so a destroyed organ is still unrecoverable (P4-D24). | verified `OrganHealthSystem.cs:33-63` |

### 1.2 New phase-4 decisions

| ID | Decision | One-line rationale | Evidence |
|---|---|---|---|
| **P4-D1** | **HOOK 9 lands in phase 4**, on `HealthChange` and `EvenHealthChange` only, and is recorded in the manifest as *inert plumbing until a non-Biological `bodyPartProfile` exists*. | `OrganicBodyPartProfile` is the only profile in the tree and it is `treatmentCapabilities: [Biological]` (`Resources/Prototypes/_Onyx/Wounds/wounds.yml:2-4`, re-read here), so `CanTreatPart` — which returns `true` whenever no scope is open (`WoundDamageRoutingSystem.cs:933-941`, read in full) — can never refuse in phase 4. Landing it anyway costs ~14 lines across two upstream files, has zero regression surface, makes every present and future `!type:HealthChange` reagent automatically correct the moment phase 5 adds an IPC/cybernetic profile, and avoids re-opening two upstream files after the phase-4 manifest is signed off. | `reagents.md` §4.1–§4.5, every cited line re-read here |
| **P4-D2** | **The reagent content port is Tier A only** (`reagents.md` §6.6): the four `<Onyx-PartPain>` `SuppressPain` blocks on Cognac / Bicaridine / Desoxyephedrine / Happiness, `MendFractures` on Wolfgate's existing `Stasizium`, and five new reagents — `Osteogen`, `Ibuprofen`, `Ketorolac`, `Tramadol`, `Oxycodone`. **Tier B (`Probital` + `Mitogen`) is pre-authorised as an in-package extension** if WP12-2 is otherwise green. Tier C and the virology set are out of scope. | Of the 22 reagents in `ONYX _Onyx/Reagents/Medicine/*.yml`, **20 do not exist in Wolfgate in any form** — no prototype, no `reagent-name-*` locale key, no reaction — so a reagent port is net-new content, not a merge, at roughly four touchpoints each. Tier A is the entire gameplay point (a painkiller ladder 0.5 → 0.9 → 1.25 → 2.0 suppression plus the dedicated fracture medicine) and needs **zero** missing effect types. Tier C needs three condition types Wolfgate does not have — `PressureThreshold`, `TypedDamageThreshold` (whose Onyx body calls the also-absent `DamageableSystem.GetAllDamage`) and `ModifyStatusEffect` — each a separate port. | `reagents.md` §6.1–§6.6; the "MISSING" column was spot-checked here for `Osteogen`, `Ibuprofen`, `Ketorolac`, `Tramadol`, `Oxycodone` (`grep "id: X$" Resources/Prototypes` → 0 hits each) |
| **P4-D3** | **`Stasizium`: extend Wolfgate's entry in place with one marked `!type:MendFractures` block. Do not create `OnyxStasizium`.** | Read side by side here. WG's `_Goobstation/Reagents/medicine.yml:1-41` and ONYX `_Onyx/Reagents/Medicine/first_aid.yml:1-46` share `name`, `desc`, `physicalDesc`, `flavor`, `color: "#8364BE"`, `worksOnTheDead: true`, `ModifyBloodLevel 10`, a −2 bleed modifier, `ReduceRotting 30` gated on Dead, a −20 five-group bulk heal, a Blunt-100 overdose at threshold 21 and a sub-263.15 K freeze. It is the same reagent in two tag dialects; the only functional difference is the fracture block. WG's copy is shipped content (`MedkitCombatStasiziumFilled`, `StasiziumAutoInjector`). **Leave WG's `HealthChange`-vs-`EvenHealthChange` and its `-50000`-vs-`-1000000` temperature alone** — that is Wolfgate's own balance, and D4 does not reach it. **Note the metabolism group is `Medicine`, not Onyx's `Bloodstream`** — the block goes in `Medicine:`. | verified: `sed -n '1,42p' Resources/Prototypes/_Goobstation/Reagents/medicine.yml` |
| **P4-D4** | **`SalicylicAcid`: DROP Onyx's entirely.** Not extended, not renamed. Recorded as a deliberate omission with the collision reason. | WG's `SalicylicAcid` (`_NF/Reagents/chemicals.yml:1-6`) is an inert Frontier **precursor** — no `group:`, no `metabolisms:`, no effects — consumed by exactly two reactions (`_NF/Recipes/Reactions/chemicals.yml:2`, `_NF/Recipes/Reactions/medicine.yml:6` → Traumoxadone). Onyx's is a `group: Medicine` brute healer. Adding metabolisms to a precursor would make a chem intermediate heal on ingestion — an unintended Frontier-content change. A rename is worse than it looks: Onyx's own **reaction** is also `id: SalicylicAcid`, so the rename cascades, and the reagent's only interesting behaviour needs `TypedDamageThreshold`, a whole missing condition type. Its role (threshold-scaled brute healing) is already covered by Bicaridine + Brutepack. | `reagents.md` §6.4; both WG files re-read here |
| **P4-D5** | **`TakeStaminaDamage` honours `Immediate`, and its `[DataField] public bool Immediate` keeps Onyx's default of `false`.** Recorded as a corrected-upstream-bug deviation (precedent: §8.2-1, §8.2-3). | Onyx's `TakeStaminaDamageEntityEffectSystem` never reads `Immediate` and its own comment says vanilla lacks the mode. **Wolfgate has it**: `StaminaSystem.cs:273-275` (`// goob edit - stunmeta`) `public void TakeStaminaDamage(EntityUid uid, float value, StaminaComponent? component = null, EntityUid? source = null, EntityUid? with = null, bool visual = true, SoundSpecifier? sound = null, bool immediate = true)`. **Read `:288-292` before assuming this is free**: `if (component.Critical && immediate) { EnterStamCrit(uid, component, true); return; }` — the `immediate` branch fires only on an already-critical target and **returns without applying the value**, so passing `immediate: true` with a *negative* amount would silently refuse the heal. Keeping Onyx's `false` default is therefore both faithful and strictly safer than binding WG's `true` default. | every line re-read here |
| **P4-D6** | **`DistributedHealthChange` is NOT authored. Recorded as *not needed*, not *deferred*.** | Zero `!type:DistributedHealthChange` in `ONYX Resources/Prototypes/_Onyx/Reagents/**` and in `ONYX Resources/Prototypes/Reagents/**`; zero `DistributedHealthChange` anywhere in WG. DECISIONS P4-1's conditional ("only if a ported reagent uses it") resolves to no. | `reagents.md` §4.4 |
| **P4-D7** | **No `treatmentCapabilities:` is written into any Wolfgate reagent or item in phase 4.** The `[Biological]` default is already correct for all 11 `HealingComponent` entities and all ~173 `HealthChange`/`EvenHealthChange` reagent uses. The one non-default candidate in the tree — the cable coil (`Entities/Objects/Tools/cable_coils.yml:36-45`, `damageContainers: [Silicon]`, heals Heat/Shock/Radiation) — is recorded in `WOLFMED_STATUS.md` as the **first phase-5 action**, not annotated now. | Annotating now would add 170+ diff lines that restate the default, and would have to be revisited anyway once phase 5 decides what an IPC limb accepts. Also record the structural limit so nobody debugs it later: `RouteAppliedDamage` sends only **localized** negatives through `CanTreatPart` (`WoundDamageRoutingSystem.cs:701-723`), so healing of Toxin, Airloss, Bloodloss, Genetic, Cellular and Radiation bypasses the capability gate entirely — Onyx behaves the same way. | `reagents.md` §7.1–§7.3 |
| **P4-D8** | **P4-5 (pain numbness / `ModifyStatusEffect`) is SKIPPED and recorded**, with the exact re-entry cost written into `WOLFMED_STATUS.md`. | DECISIONS P4-5's own wording ("if the ported reagents need them; otherwise record as skipped") resolves to skipped on the evidence: not one reagent in `_Onyx/Reagents/Medicine/*.yml` uses `ModifyStatusEffect`, and none of the four `<Onyx-PartPain>` blocks does. Wolfgate's parity is already met by the legacy trait path (`PainNumbnessComponent` + the P2-D8 widening at `PainSystem.cs:329-334`). And it is a balance change dressed as a port: `PainNumbnessStatusEffect` hides the damage overlay and shows the health alert as full, which would be a real combat buff on meth that Wolfgate has never had. **Cost to re-enter later, for the record:** ~45 LOC for `ModifyStatusEffect`, a separate `_WF` action enum (WG's `StatusEffectMetabolismType` is `{Add, Remove, Set}` — adding Onyx's `Update` member to the shipped enum would silently no-op inside `GenericStatusEffect`'s `if/else` chain, a landmine), 2 prototypes, 2 locale keys, 1 test. Roughly half a day. **Cleanup note:** `PainNumbnessStatusEffectComponent` stays dead code with two readers (`PainSystem.cs:336`, `EmoteOnDamageSystem.PainSounds.cs:37`) and no writer; say so in the manifest so a future reader does not assume a live feature. | `reagents.md` §8 |
| **P4-D9** | **The tourniquet replaces Wolfgate's existing `Tourniquet` entity's `- type: Healing` block in place, same id — and the `Tourniquet` **tag** Onyx adds is NOT ported.** | **The collision is real and the phase-1 evidence missed it** (`medical-extras.md` checked C# symbols only): `Resources/Prototypes/Entities/Objects/Specific/Medical/healing.yml:266-296` already declares `id: Tourniquet`, `parent: BaseHealingItem`, the same name/description/`state: tourniquet`/`heldPrefix: tourniquet`/`delay: 0.5` and the same two sound paths — with `- type: Healing` (`Brute: 5`, `Asphyxiation: 5`, `bloodlossModifier: -10`) as the pre-Wolfmed stand-in. Onyx's own tree patches **the same upstream entity, same id**, under `<Onyx-TargetedTourniquet-edited>`. A second `id: Tourniquet` entity is a duplicate-prototype error, and the id is referenced by 8 other files (sec/gib vending, security spawners, `job.yml` belts, `cmo_webbing.yml`, two `_NF` loot fills, `nfsdtec.yml`) — an in-place swap touches none of them. **Correction to `tools.md` §1.3:** its proposed YAML adds `- Tourniquet` to the `tags:` list; `grep "id: Tourniquet"` over `Resources/Prototypes` returns only the entity and two fill references — **there is no `Tourniquet` tag prototype in WG**, and an undeclared tag fails the Release YAML lint. Keep the `tags:` list exactly as it is (`SecBeltEquip` only). | every line re-read here |
| **P4-D10** | **`TourniquetSystem.cs` is vendored to `Content.Server/_Onyx/Medical/Tourniquet/`, keeping Onyx's `Content.Shared._Onyx.Medical.Tourniquet` namespace. `TourniquetComponent.cs` stays in `Content.Shared`.** | `CanApply` calls `WoundBleedingSystem.GetPartRate`, and D13 put that class in `Content.Server`; `Content.Shared` cannot reference it, so the file cannot compile where Onyx has it. `CanApply` is reached from `TryStart`, which is the `UseInHandEvent`/`AfterInteractEvent` handler, so the whole class moves — there is no data/logic split as there was for `HealingComponent`/`HealingSystem`. Direct precedent in the tree: `Content.Server/Medical/HealingSystem.cs` is a server-only class subscribing those same two events on a shared component. **Recorded deviation:** `_audio.PlayPredicted` degrades to server-fired PVS audio (a few ticks of delay on the begin/end sound), the same class of loss as the existing healing items. | `tools.md` §1.2–§1.3; `WoundBleedingSystem.cs` public surface re-listed here (`SetTreatment:131`, `ReduceBleeding:142`, `TreatPart:257`, `GetPartRate:273`) |
| **P4-D11** | **Non-wound-hosts lose tourniquet function entirely. Accepted and recorded; no dual-component composition.** | `TourniquetSystem.CanApply` requires `HasComp<WoundableComponent>(part)`, which only wound-host parts carry. Per D32 the affected population is **Protogen alone** — every other organic humanoid is a wound host — and what it loses is a weak generic `Healing.bloodlossModifier: -10`. The alternative (both `Healing` and `Tourniquet` on one entity, gated by `HasComp<WoundHostComponent>`) puts two systems on the same `UseInHandEvent` for the same entity and makes the outcome depend on handler dispatch order, which this codebase does not make deterministic. Not worth it for one excluded species. | `tools.md` §1.3 |
| **P4-D12** | **`GroupHealSpecifier` is NOT ported.** PLAN.md's WP11 line bundled it with Tourniquet/Medical Patch; there is no dependency edge. | `MedicalPatchComponent.cs`/`MedicalPatchSystem.cs` were read in full and reference it nowhere. Its only consumers in the whole Onyx tree are `_Onyx/Vampire/**`, `DamageInContainerComponent`, `LeechMeleeWeaponComponent` and `_Onyx/Damage/Systems/DamageableSystem.API.cs` — the Vampire antagonist feature, named as in-scope by no decision document. It also carries a second licence header (derived from Wega, GPL-3.0) layered under Onyx's AGPL; record that in the manifest as a *reason not to port it casually*, and revisit only if Vampire ever is. | `tools.md` §2.3; grep re-run here |
| **P4-D13** | **Medical Patch ports verbatim to `Content.Server/_Onyx/Medical/`, with zero fill / vending / cargo / loadout placement — mirroring Onyx exactly.** | Onyx places `MedicalPatchMakeshift` nowhere: `git grep -i medicalpatch` over `Resources/Prototypes` returns only the definition file, the tag file and one **storage whitelist** entry on its surgery kit. It is a field-craft item (1 Cloth or 4 WebSilk, 5 s do-after) with the flavour text to match. Its only WG mention is a commented-out line in `_Goobstation/Entities/Clothing/Belt/belts.yml:30`. **This is the lowest-risk item in the phase**: zero wound coupling, zero collisions, zero upstream edits, zero new subscriptions on any pre-existing component. | `tools.md` §2.1–§2.5; grep re-run here |
| **P4-D14** | **Explosion amputation IS in phase 4 (HOOK 22), and the same package closes the P3-D3 armour-plate hole.** Gated on **T-EXPLOSION-PLATE**. **This is the one item that changes live combat balance and is escalated for explicit sign-off (§8.4 decision 1); the default is ship.** | DECISIONS P4-6 lifts D24 conditionally ("if the analysts confirm the hook is small"); they do. Three verified facts made the call. (a) **Explosion damage against a wound host is already routed today**: `ExplosionSystem.Processing.cs:471` calls `_damageableSystem.TryChangeDamage(entity, damage, ignoreResistances: true, ignoreGlobalModifiers: true, originFlag: DamageableSystem.DamageOriginFlag.Explosion)` — the universal entry point GUARD D hooks — and `WoundDamageRoutingSystem.OnBeforeDamageChanged` subscribes `<WoundHostComponent, BeforeDamageChangedEvent>` with no origin filter. So today a blast already lands on one random part with the plate flag correctly threaded. (b) **Onyx's distinctive mechanic is already fully ported and unreachable**: `TryApplyDistributedDamage`/`TryRouteDistributedDamage` (`:251-330`, `:904-929`), `PickExplosionAmputationCandidate` (`:943`), `AmputationSystem.TryExplosionAmputate`, and both CVars (`CCVars.Wounds.cs:22-26`, `explosion.damage_variation` 2f / `explosion.wounding_multiplier` 4f) have zero consumers. (c) **The P3-D3 plate hole is a 3-field tuple**: `_routedModifiers` is `Dictionary<EntityUid, (float ArmorPenetration, EntityUid? Tool, DamageableSystem.DamageOriginFlag? OriginFlag)>` (`:54`), written only at `:79` and read at `:686-688`; the distributed entry points bypass that write, so `SharedArmorPlateSystem.OnBeforeDamageChanged`'s gate (`Origin == null && OriginFlag != Explosion`) would refuse plate protection outright. One optional parameter threaded through two signatures closes it. | every line re-read here; `tools.md` §3 |
| **P4-D15** | **Guidebook: two new `_WF` entries (`Wounds`, `WoundTreatment`) authored as Wolfgate content using Onyx's structure as a draft. Onyx's `Surgery` guideEntry is skipped outright. The amputation-consequence paragraph SHIPS.** | `id: Surgery` is already taken by Shitmed's own guide entry (`Resources/Prototypes/Guidebook/medical.yml:60`, with children `PartManipulation`/`OrganManipulation`/`UtilitySurgeries`), and Onyx's copy documents Onyx's unported surgery system (D7) — it needs neither a rename nor a merge. `guide-entry-wounds` / `-wound-treatment` / `-body-part-damage` and the two new ids are collision-free. Drop every IPC / cybernetic / slime-plant section (D3, and `SyntheticRepairTool` does not exist in WG); reword dismemberment for §8.6-1's gun/laser severing; state fracture effects qualitatively rather than quoting numbers, since §8.2-1 corrected the shipped `manipulationModifier` values. **`tools.md` §4.4 recommends holding the amputation-consequence paragraph until the mechanic is real; P4-3 makes it real in this same phase, so it ships** — but WP12-10 runs after WP12-5, and if P4-3's attach block is dropped the paragraph must be dropped with it. | `tools.md` §4; `Resources/Prototypes/Guidebook/medical.yml` re-read here |
| **P4-D16** | **Surgery extensions: `SurgeryWoundedConditionComponent` gains three marked datafields (EXT 1). `SurgeryTendWoundsEffectComponent` is NOT changed.** | Shitmed's step damage already reaches the wound layer for free: `SurgeryStepDamageEvent` → `SurgerySystem.SetDamage` → `TryChangeDamage(body, …, targetPart: _body.GetTargetBodyPart(partComp))` → the phase-1 routing seam → `PartDamageAppliedEvent` → `WoundSystem.HandlePartDamageApplied`, which heals wounds on negative amounts. So `SurgeryTendWoundsBrute`/`Burn` and every `SurgeryDamageChangeEffect` step already act on wounds today. The only Onyx feature WG's tend handler lacks is computing the heal bonus from *wound severity* rather than *projected group damage* — a balance nuance, not a capability. **This is the single biggest scope saving in P4-3, and it overrules `tests.md`'s T-SURG-TEND-*-DEEP**, which assumed an Onyx `GetGroupSeverity` + `TryHealWounds` branch would be grafted onto the shared handler; §6 rewrites those two tests accordingly. | `surgery.md` §1.5, §2.3c, §3.1; the call chain re-read here (`SurgerySystem.cs:89-105`, `SharedSurgerySystem.Steps.cs:343-372`) |
| **P4-D17** | **Every new surgery component takes a `WolfmedSurgery*` prefix.** Onyx's own names are not used even where they are currently free. | Two of Onyx's seven collide outright (`SurgeryWoundedCondition`, `SurgeryTendWoundsEffect` — both `[RegisterComponent]` in Shitmed today, and a duplicate registered name is a `ComponentFactory` crash at boot). The other five (`SurgeryHasWoundCondition`, `SurgeryClampBleedingEffect`, `SurgeryTreatWoundEffect`, `SurgeryMendFractureEffect`, `SurgeryFractureGradeCondition`) read as upstream Shitmed names and a future Monolith merge could introduce them. **This overrules `tests.md` §4's "vendorable verbatim" column**, which is technically true today and a latent collision tomorrow; §6's fixtures use the plan's names. All ten new type names were grepped in this pass and return **0** hits (§5.3). | `surgery.md` §3.2, §6.2; grep re-run here for all ten |
| **P4-D18** | **The re-attachment block is a 2-line hook in `SharedSurgerySystem.OnPartRemovedConditionValid` (HOOK 25). `SharedBodySystem.CanAttachPart` is NOT hooked.** This overrules `tests.md` §8 decision 2 and supersedes DECISIONS P4-3's literal wording. | **DECISIONS P4-3 names `CanAttachPart` in prose; the tree says that is the wrong seam.** `CanAttachPart(parentId, slotId, partId, …)` (`SharedBodySystem.Parts.cs:606-619`) is called from `AttachPart(parentPartId, BodyPartSlot slot, …)` at `:676`, and `AttachPart` has **five callers outside Shitmed surgery**, all re-grepped here: `Content.Server/_Mono/Traits/Physical/PrybarProstheticsSystem.cs:118`, `Content.Server/_Mono/Traits/Physical/BionicLegsSystem.cs:104`, `Content.Shared/_Goobstation/Autosurgeon/AutoSurgeonSystem.cs:116`, `Content.Shared/_Shitmed/BodyEffects/Subsystems/GenerateChildPartSystem.cs:48`, plus `TryCreatePartSlotAndAttach` behind three admin commands and `_Mono/Body/Systems/BodyRejuvenateSystem.cs:330`. A guard keyed on the **parent** carrying an `AmputationConsequenceWound` is exactly the "you lost a leg, bolt on a prosthetic" case for the two Mono traits — they would silently `QueueDel` the new part. The surgery-layer hook instead cancels `SurgeryValidEvent` on whichever `SurgeryAttach*` surgeries target the stump, which both hides them from the BUI **and** aborts a do-after that somehow started, because `OnTargetDoAfter` re-runs `IsSurgeryValid` (`SharedSurgerySystem.cs:94`). It is client/server consistent because wounds replicate. **Scope, re-measured in this revision (CRITIQUE4 m2/m7):** there are **10** `SurgeryAttach*` surgeries, not 11 (`grep -c "id: SurgeryAttach" Resources/Prototypes` → 10: Head, LeftArm, RightArm, LeftLeg, RightLeg, Hands, LeftHand, RightHand, LeftFoot, RightFoot). The hook keys on `args.Part`, the part the slot attaches **to**, and `ApplyAmputationConsequences` puts the wound on that same parent (`AmputationSystem.cs:59-70`). In WG's human graph the head, both arms and both legs connect to the torso (`Body/Prototypes/human.yml:5-27`), so an untreated **torso** stump hides the **six** surgeries whose `SurgeryPartCondition` is `part: Torso` — Head, LeftArm, RightArm, LeftLeg, RightLeg and Hands — while `SurgeryAttachLeft/RightHand` (`part: Arm`) and `SurgeryAttachLeft/RightFoot` (`part: Leg`) are blocked only by a stump on that arm or leg. CRITIQUE4's "one stump hides all ten" is therefore rejected on the prototype evidence; it hides six. | all five call sites and `AttachPart`'s body re-read here; `surgery.md` §4.1; attach-surgery conditions re-read in this revision |
| **P4-D19** | **`maxWoundSeverity: 99.99` is added to `SurgeryTendWoundsBrute` and `SurgeryTendWoundsBurn` (PROTO F).** | Without it a badly wounded limb lists **four** overlapping tend surgeries instead of two. The new datafields are null by default and the helper returns early when both bounds are null **and** when the body has no `WoundHostComponent`, so animals, borgs and Protogen are provably unchanged (D2). | `surgery.md` §5.3 |
| **P4-D20** | **Two-step fracture ladder: `SurgeryStepSetBone` (`BoneSetter` → `FractureTreatment.Reduced`) then `SurgeryStepMendFracture` (`BoneGel` → `Mended`) — with a mandatory correction to the completion check.** | DECISIONS P4-3 asks for the ladder, the enum already has both members, and `BoneSetterComponent` is declared, shipped on three items (`Entities/Objects/Specific/Medical/surgery.yml:229`, `:464`, `_NF/Entities/Objects/Tools/tools.yml:191`) and **referenced by zero step prototypes** — a free slot. **The correction the reports missed:** `CanTreat` (`WoundFractureSystem.cs:186-193`, read in full here) is `Reduced => fracture.Grade >= profile.ReductionMinimumGrade && fracture.Treatment == FractureTreatment.None`, and `OrganicFractureProfile.reductionMinimumGrade` is **`Simple`** (`wounds.yml:47`). So `TrySetTreatment(Reduced)` returns **false** on a Hairline fracture, and a naive "step is complete when Treatment == Reduced" check would stall the surgery forever on the commonest fracture grade. `WolfmedSurgeryMendFractureEffect`'s complete-check must read *"complete when `Treatment` is already at or past the target, **or** when `CanTreat` can never succeed"* (§2.5). The ladder is worth having: `treatmentEffectScales` is `None 1 / Reduced 0.25 / Mended 0` (`wounds.yml:52-55`), so reduction alone already cuts the fracture's movement and manipulation penalty to a quarter. | every line re-read here |
| **P4-D21** | **Surgery scarring ships as a complete four-prototype chain or not at all** (§8.4 decision 3, default ship). `WolfmedSurgeryIncisionWoundEffect` goes on **`SurgeryStepOpenIncisionScalpel` only**, **added beside** its flat `Bloodloss: 10` and **never replacing it** (replacing would strip the incision cost from non-hosts — a D2 breach, §8.5 trap 14). `WolfmedSurgeryIncisionTreatmentEffect { treatment: Clamp }` on `SurgeryStepClampBleeders`; `{ treatment: Close }` on **both** `SurgeryStepCloseIncision` **and** `SurgeryStepSealTendWound`. **`SurgeryStepCarefulIncisionScalpel` gets nothing.** | Today **surgery in Wolfgate can never scar**: `WoundScarSystem` is fully ported and CVar-driven (`surgery.scar_chance`, default 0.35, `CCVars.Surgery.cs:7-8`) and `SurgicalIncisionWound` is a shipped prototype, but nothing in the tree ever creates one. **Correction to `surgery.md` U-4(A), which prices this at "~20 lines + 2 marked YAML lines": that is one third of the mechanism and shipping it alone is worse than shipping nothing.** Onyx's chain, read in full here (`ONYX Content.Server/_Onyx/Medical/Surgery/SurgerySystem.WoundEffects.cs:13-42`), is: `SurgeryStepBleedEffect { damage: 10 }` → `CreateOrMergeWound(part, "SurgicalIncisionWound", 10)`; `SurgeryClampBleedEffect` → `TreatPart(part, BleedingTreatment.Clamped, SurgicalIncision)`; `SurgeryCloseIncisionEffect` → for every open/stabilised incision wound, `SetTreatment(Cauterized)`, `CloseWound`, roll `scar_chance`, `RemoveWound`. Without the second and third effects a `SurgicalIncisionWound` at severity 10 (`rate: 0.1`, `awakeMultiplier: 3` → 3.0/s raw while awake) bleeds until natural clotting, on a `mergeMode: SeparateInstances` prototype that stacks one new wound per operation. Onyx's incision step carries **no** flat Bloodloss — the wound *is* the cost — but **replacing** WG's `SurgeryDamageChangeEffect { Bloodloss: 10 }` would strip that cost from non-hosts, so phase 4 **adds beside** and records the ~1.3× increase as deviation 9 (§7.3). **Two corrections, both re-verified in the tree during this revision.** (a) `SurgeryStepCarefulIncisionScalpel` (`surgery_steps.yml:303-316`) carries **no** damage effect at all — its whole component list is `SurgeryStep` (Scalpel, `add: IncisionOpen`, `duration: 3`) + `Sprite` + `SurgeryStepEmoteEffect` (`grep -n "SurgeryDamageChangeEffect" surgery_steps.yml` → `22, 44, 187, 231, 271, 372, 467, 480, 493, 535`, nothing in `:303-316`) — and Onyx's own copy carries no bleed effect either (`ONYX surgery_steps.yml:958-970`: `SurgeryStep` + `Sprite` + `SurgeryStepPainInflicter { amount: 17, sleepModifier: 0 }`). Putting the wound effect there would open a `mergeMode: SeparateInstances` bleeder on the two commonest surgeries in the game, whose step lists contain neither treatment step. It is dropped. (b) The wound surgeries that *do* open a real incision end on `SurgeryStepSealTendWound`, which removes `IncisionOpen` but is **not** `SurgeryStepCloseIncision`; without a `Close` effect there the incision wound survives clamped-but-open until the medic separately runs `SurgeryCloseIncision`. Adding `{ treatment: Close }` to `SurgeryStepSealTendWound` (`:359`) closes the loop. Onyx has no `SealTendWound` step at all (`git grep` → 0 hits), so this is a Wolfgate adaptation, not a deviation from Onyx. | `ONYX surgery_steps.yml:9-25` and `SurgerySystem.WoundEffects.cs` read in full here; WG's `surgery_steps.yml:9-27`, `:31`, `:168`, `:303` re-read |
| **P4-D22** | **Surgery inflicts pain (`WolfmedSurgeryPainEffect`), with Onyx's amounts.** | Wolfgate has had a live `PainSystem` since phase 2 and no surgery pain at all; Onyx attaches `SurgeryStepPainInflicter` to eight wound/organ steps (`amount: 5` default; `34` with `sleepModifier: 0.12` on the scalpel incision, `24` with `sleepModifier: 0` on the organ-heal base, `12` on mend-fracture). `PainSystem.ChangePain(Entity<PainComponent?>, FixedPoint2)` is SAME (`PainSystem.cs:225`) and `PainComponent` lives on the part (`PainSystem.cs:69`), so the component is ~12 lines and one subscription. It is what makes awake surgery on a wound host dangerous the way phase 2's pain shock intends. **`SleepModifier` ships as a datafield but is inert in phase 4** — Wolfgate has no anaesthesia-scaling consumer; record it. | `surgery.md` §2.3i; Onyx `SurgeryEffects.cs:10-13` and `surgery_steps.yml` amounts read here |
| **P4-D23** | **Organ heal ships at `amount: 3` per step** (5 repeats of a 2 s step ≈ 10 s per organ), marked `# WOLFGATE (P4 balance)`, **not Onyx's `amount: 1`** (15 repeats ≈ 30 s). Recorded as a balance deviation from D4; reverting is a one-line YAML edit per step. | `WolfmedOrganComponent.MaxHealth` is 15 and `OrganDamageSystem` caps organ damage at `MaxHealth × MaxDamageFraction = 4.5` per application, so phase 3 measured organ loss as a shift-long ratchet rather than a firefight event. A 30-second per-organ repeat loop of 2 s do-afters is dead time, not tension, and there are seven organs. This is the one place in phase 4 where Onyx's number is bad for Wolfgate's pace rather than merely different. | `surgery.md` U-6; `OrganHealthSystem.cs` / `OrganDamageComponent.cs` values from PLAN3 §8.4 |
| **P4-D24** | **Organ healing saves a *dying* organ, never a destroyed one, and `SurgeryHealKidneys` ships.** | `OrganHealthSystem.Update` (`:33-63`) destroys any organ at `Health <= 0` on the next tick (`DestroyOrgan` → `RemoveOrgan` + `QueueDel`), except the brain (P3-D22). So `SetHealth`'s clamp to `[0, MaxHealth]` means the surgery is reachable only while `0 < Health < MaxHealth`. Phase 3's "organ damage ships irreversible" becomes **"organ damage is reversible while the organ lives"**; the cure for a destroyed organ remains `SurgeryInsert<Organ>`. Wolfgate has `OrganHumanKidneys` with full Wolfmed data and **no kidney surgery of any kind** — `SurgeryHealKidneys` is one prototype pair and would be the fork's first; `SurgeryRemoveKidneys`/`InsertKidneys` stay absent, so a destroyed kidney is still unrecoverable. Word the guidebook and the analyzer accordingly. | `OrganHealthSystem.cs:33-90` read in full here; `surgery.md` §4.2, U-7 |
| **P4-D25** | **Analyzer UI: PARALLEL (option b) — a self-contained `_WF` panel mounted by three marked lines, with the tab strip inside the panel (geometry option G2, no upstream size change), and the fracture grade printed (§7.3 of `analyzer.md`).** | Onyx and Wolgate independently rewrote the same three files: Onyx wraps everything in a `HealthAnalyzerUiState` struct with **no** `Part`, **no** `Uncloneable` and **no** `Dictionary<TargetBodyPart, TargetIntegrity> Body`, and splits the window into a 622-line `HealthAnalyzerControl`; Shitmed kept the flat message and added a per-part selection round-trip plus an 11-button doll inline at `HealthAnalyzerWindow.xaml:68-203`. Neither Onyx file can be vendored. GRAFT means ~40–70 lines of new XAML inside a window that is `SetWidth="350" SetHeight="650" Resizable="False"` and already vertically full, i.e. a geometry change that alters the analyzer for **every** scan in the game including non-hosts, plus a permanent conflict surface against any Shitmed re-sync, plus every new `Name=` entering the window's `[GenerateTypedNameReferences]` scope where the reserved-name trap lives (`WindowTitle`, `HelpButton`, `CloseButton`, `ContentsContainer` on `FancyWindow`, on top of 27 names already taken). PARALLEL is 3 upstream lines, keeps every new name inside the panel's own scope, and is the only shape in which Onyx's control can be cited nearly line-for-line for a future re-sync. Printing the fracture closes a genuine Onyx gap — Onyx carries `Fracture`/`FractureTreatment` in the payload and its UI never renders them, so a fractured part shows as a bullet with no details; Wolfgate has shipped fractures with a live alert and two movement penalties since phase 2. | `analyzer.md` §0, §2.3–§2.5, §7; `HealthAnalyzerWindow.xaml`/`.xaml.cs` and `FancyWindow.xaml` re-checked here |
| **P4-D26** | **The four analyzer builders are `public`, not `private`**, so `WolfmedAnalyzerTest` can call them without a client harness. | `UpdateScannedUser` ends in `_uiSystem.ServerSendUiMessage(...)` (`HealthAnalyzerSystem.cs:276-287`, read in full here) — there is no headless capture point for the wire message. Onyx itself solved this by factoring the payload into directly-callable builders and testing those (`ONYX HealthAnalyzerPartDamageTest.cs`). This resolves `tests.md` §8 decision 1 in the affirmative at zero extra cost: P4-4 has to compute the data anyway. | `analyzer.md` §9; `tests.md` §2.4 |
| **P4-D27** | **`BuildPartDamage` is NOT ported**, and Onyx's `HealthAnalyzerPartDamageTest.BuildsIsolatedPartSnapshotTest` is dropped rather than adapted. | WG's client already reads exact per-part damage from the selected part's networked `DamageableComponent` (`HealthAnalyzerWindow.xaml.cs:118,183,224-227`) and gets 11-part severity buckets from `msg.Body`. Porting `Dictionary<TargetBodyPart, DamageSpecifier>` would duplicate both. Onyx's test additionally asserts `snapshot[Groin] == snapshot[Chest]` by reference, which is **actively wrong for WG** under D9. | `analyzer.md` §5, §9 |
| **P4-D28** | **Hook numbering.** HOOK 9 is the reserved-since-phase-1 reagent hook. New numbers: **HOOK 22** = `ExplosionSystem.Processing.cs`; **HOOK 23** = `HealthAnalyzerSystem.cs`; **HOOK 24** = `SharedSurgerySystem.OnWoundedValid`; **HOOK 25** = `SharedSurgerySystem.OnPartRemovedConditionValid`; **HOOK 26** = the client analyzer window (`.xaml` + `.xaml.cs`, one number, two sites, as HOOK 20 did). | `tools.md` and `analyzer.md` **both** claimed HOOK 22 for different files — a direct collision, resolved here. Highest number live in the tree is **21** (`grep -o "HOOK [0-9]*"` over `Content.*` and `Resources` → 6,7,8,10,11,12,13,14,15,16,17,18,20,21; 19 was withdrawn in phase 3, 9 is reserved and unlanded). | grep re-run here |
| **P4-D29** | **Upstream non-hook edits get their own IDs so the manifest can audit them: `EXT 1` for the `SurgeryWoundedConditionComponent` datafields, `PROTO D`–`PROTO L` for prototype/locale edits** (§3). | PLAN3 established `PROTO A/B/C` for prototype edits that are not one-line code hooks; phase 4 has nine, plus one component-datafield extension that is additive-only and therefore not a behavioural hook. Keeping them numbered is what lets WP12-10 reconcile the upstream-file count (27 after phase 3 → **45** after phase 4, §3.4). | PLAN3 §3 |
| **P4-D30** | **`reagents.md`'s retraction of PLAN.md §8.2's `Scale` tuning note is ADOPTED.** The note was backwards and is struck from the record. | PLAN.md §8.2 said "Onyx's framework clamps Scale to ≤1 unless Scaling is set; Wolfgate's is unclamped". The opposite is true. WG: `mostToRemove = FixedPoint2.Clamp(rate, 0, quantity); scale = mostToRemove / rate` (`MetabolizerSystem.cs:184-186`) ⇒ structurally `[0, 1]`. Onyx: `rate = solutionData.MetabolizeAll ? quantity : entry.MetabolismRate; scale = (float) mostToRemove; if (!MetabolizeAll) scale /= rate` ⇒ **can exceed 1**. Wolfgate has no `MetabolizeAll`. `Amount * scale` is therefore identical or slightly more conservative in Wolfgate. **No tuning work item; no `MinScale` port either** — no reagent in `_Onyx/Reagents/**` sets `minScale:`. | `reagents.md` §9 |

---
## 2. New `_WF` / vendored pieces

### 2.0 What already exists — verified in this pass, nothing to build

Phase 4 adds **no compat-layer file**. Each of these was read in the current tree:

| Symbol | Site | Status |
|---|---|---|
| `WoundDamageRoutingSystem.WithTreatmentCapabilities(EntityUid, IReadOnlySet<TreatmentCapability>, Action)` | `Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs:136-147` | present; **non-reentrant** (see trap T3, §8.5) |
| `WoundDamageRoutingSystem.TryApplyPartDamage(EntityUid, EntityUid, DamageSpecifier, EntityUid? = null, bool = false, bool = true)` | `:240-249` | present |
| `WoundDamageRoutingSystem.TryApplyDistributedDamage` / `TryRouteDistributedDamage` | `:251-261`, `:904-914` | present, both 10-parameter; P4-D14 appends an 11th |
| `WoundSystem.GetWounds(Entity<WoundableComponent?>) -> IEnumerable<Entity<WoundComponent>>` | `Content.Shared/_Onyx/Wounds/WoundSystem.cs:155` | present |
| `WoundSystem.CreateOrMergeWound(Entity<WoundableComponent?>, ProtoId<WoundPrototype>, FixedPoint2) -> EntityUid?` | `:166-172` | present — **returns `EntityUid?`, not `Entity<WoundComponent>?`** |
| `WoundSystem.ChangeSeverity` / `TreatWound` / `SetWoundState` / `CloseWound` / `RemoveWound` / `TryHealWounds` | `:284`, `:316`, `:338`, `:354`, `:356`, `:400` | present; all `_net.IsServer`-gated |
| `WoundBleedingSystem.SetTreatment` / `ReduceBleeding` / `ReducePartBleeding` / `TreatPart` / `GetPartRate` | `Content.Server/_Onyx/Wounds/WoundBleedingSystem.cs:131`, `:142`, `:226`, `:257`, `:273` | present, **server assembly**. `TreatPart` returns **`int`** (the count treated), not `bool` — correction to `surgery.md` §3.4 |
| `WoundFractureSystem.GetFracture` / `TryReduce` / `TryMend` / `TrySetTreatment` / `GetGrade` | `Content.Shared/_Onyx/Wounds/WoundFractureSystem.cs:88`, `:99`, `:100`, `:111`, `:128` | present, **shared** in WG (server in Onyx) |
| `WoundFractureSystem.CanTreat(WoundFractureComponent, FractureProfilePrototype, FractureTreatment)` | `:186-193`, `private static` | present; **the `ReductionMinimumGrade` gate P4-D20 corrects for** |
| `WoundScarSystem.CreateScar(Entity<WoundComponent?>)` | `Content.Shared/_Onyx/Wounds/WoundScarSystem.cs:64` | present |
| `PainSystem.SuppressPain(Entity<PainComponent?>, string, FixedPoint2, TimeSpan, float = 1f) -> bool` | `Content.Shared/_Onyx/Wounds/PainSystem.cs:340` | present; guards `!_net.IsServer`, `amount <= 0`, `decayDuration <= 0`, `recoveryMultiplier < 1f` |
| `PainSystem.GetPain` / `GetRawPain` / `ChangePain` | `:165`, `:198`, `:225` | present |
| `OrganHealthSystem.SetHealth` / `ChangeHealth`, both `Entity<WolfmedOrganComponent>` | `Content.Server/_Onyx/Body/Systems/OrganHealthSystem.cs:65`, `:80` | present, **server assembly, `Content.Shared._Onyx.Body.Systems` namespace** |
| `BodyPartFunctionalitySystem.GetState(Entity<WoundableComponent?>)` | `Content.Shared/_Onyx/Wounds/BodyPartFunctionalitySystem.cs:18` | present |
| `MobThresholdSystem.CheckVitalDamage(EntityUid, DamageableComponent)` | `Content.Shared/_Onyx/Mobs/Systems/MobThresholdSystem.cs:25` | present (HOOK 11) |
| `WoundTargetResolver.TryResolveExact(EntityUid, TargetBodyPart, out EntityUid)` | `Content.Shared/_WF/Wolfmed/Targeting/WoundTargetResolver.cs:60` | present — **exact match for Onyx's `TargetResolverSystem` call site** |
| `WolfmedDamageableSystem.GetAllDamage` / `GetTotalDamage` / `ChangeDamage` | `Content.Shared/_WF/Wolfmed/Compat/WolfmedDamageableSystem.cs:125`, `:134`, `:28` | present (D12 facade) |
| `SharedSurgerySystem.GetSingleton(EntProtoId) -> EntityUid?` | `Content.Shared/_Shitmed/Surgery/SharedSurgerySystem.cs:349` | **public** — the headless test entry point |
| `SharedBodySystem.GetPartOrgans` / `GetBodyOrgans` / `GetTargetBodyPart(BodyPartType, BodyPartSymmetry)` / `BodyHasChild` | `SharedBodySystem.Parts.cs`, `Body.cs:277`, `_Shitmed/Body/Systems/SharedBodySystem.Targeting.cs:378`, `Parts.cs:978` | all present |
| `SharedTargetingSystem.GetValidParts()` / `IsSelectable` | `Content.Shared/_Shitmed/Targeting/SharedTargetingSystem.cs:7-25`, `:28` | present; **10 entries, `Groin` commented out at `:13`** |
| `_Shitmed/Targeting/TargetingComponent.Target` | `Content.Shared/_Shitmed/Targeting/TargetingComponent.cs:15` | present — identical field name/type to Onyx's D10-excluded copy |
| `StaminaSystem.GetStaminaDamage` / `TakeStaminaDamage` | `Content.Shared/Damage/Systems/StaminaSystem.cs:98`, `:274` | present; class is `StaminaSystem`, **not** Onyx's `SharedStaminaSystem` |
| `StickySystem` / `StickyComponent` / `EntityStuckEvent` / `EntityUnstuckEvent` / `UnremoveableComponent` | `Content.Shared/Sticky/**`, `Content.Shared/Interaction/Components/UnremoveableComponent.cs` | all present, vanilla |
| `SharedHandsSystem.IsHolding(EntityUid, EntityUid?, out Hand?, HandsComponent? = null)` | `Content.Shared/Hands/EntitySystems/SharedHandsSystem.cs:285` | present. **DIFFERENT signature from Onyx's `IsHolding(Entity<HandsComponent?>, EntityUid?, out string? inHand)` but call-compatible**: `MedicalPatchSystem.OnUnstuck` writes `_hands.IsHolding(args.User, patch, out var hand)`, which binds to this overload with `hand` inferred as `Hand?`. Added for CRITIQUE4 m5 |
| `SharedHandsSystem.TryPickup(EntityUid, EntityUid, Hand, …)` | `Content.Shared/Hands/EntitySystems/SharedHandsSystem.Pickup.cs:91` | present. Onyx's `_hands.TryPickup(args.User, used, hand)` binds here. **No CS8604 warning**, contrary to CRITIQUE4 m5: WG's `IsHolding` declares `[NotNullWhen(true)] out Hand? inHand` (`:285`) and the call sits inside `if (_hands.IsHolding(…))`, so nullable flow analysis proves `hand` non-null. `MedicalPatchSystem.cs` really is verbatim, 0 edits |

**MISSING and worked around, not shimmed** (each has a one-line substitution, listed at its use site):
`SharedTargetingSystem.TryConvert` → `_body.GetTargetBodyPart(type, symmetry)`;
`SharedTargetingSystem.SelectableParts` → `GetValidParts()`;
`SharedBodySystem.TryGetOrganInSlot` → `GetPartOrgans(part)` + `OrganComponent.SlotId`;
`SharedBodySystem.HasAmputationConsequence` → §2.7's `WolfmedStumpBlocksAttachment`;
`OrganComponent.Category` / `OrganCategoryPrototype` → `OrganComponent.SlotId` (WG's human organs already carry `brain, eyes, lungs, heart, stomach, liver, kidneys`);
`OrganComponent.Health`/`.MaxHealth` → `WolfmedOrganComponent`;
`BloodstreamComponent.MetabolitesSolutionName` → `ChemicalSolutionName` (`Content.Server/Body/Components/BloodstreamComponent.cs:151`);
`DamageableSystem.GetAllDamage` → `WolfmedDamageableSystem.GetAllDamage`;
`StitchesComponent` → `Hemostat`/`Tending`;
`RepeatSurgeryStepComponent` → `SurgeryRepeatableStepComponent`;
`StepInvalidReason.AmputationConsequence` → not added (the surgery is hidden, not greyed out — P4-D18).

---

### 2.1 Reagent entity effects — `Content.Shared/_WF/Wolfmed/EntityEffects/` (4 new files)

Namespace `Content.Shared._WF.Wolfmed.EntityEffects` for all four. **These are data classes, not systems** — they hold no `[Dependency]` fields and resolve systems through `args.EntityManager.System<T>()`, exactly as WG's own `HealthChange` does (`HealthChange.cs:149,167`). They register **zero** subscriptions and **zero** components (§5).

**`!type:` resolution is by bare `Type.Name`** against public subclasses of the field's base type (`RobustToolbox/Robust.Shared/Reflection/ReflectionManager.cs:319-359`), so the namespace is irrelevant and **the four class names must be exact** — renaming one to `WolfmedSuppressPain` silently breaks every ported YAML as a prototype-load failure at server start, not a compile error. Precedent for a `_WF`/fork old-style effect: `Content.Shared/_Mono/Claws/ClawsGrowthModifierEffect.cs:8`.

#### `SuppressPain.cs`

```csharp
using Content.Shared._Onyx.Wounds;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.EntityEffects;

/// <summary>Adds keyed, decaying pain suppression to a wound host and speeds natural pain recovery while active.</summary>
public sealed partial class SuppressPain : EntityEffect
{
    [DataField(required: true)] public FixedPoint2 Amount;
    [DataField(required: true)] public TimeSpan DecayDuration;
    [DataField] public string Identifier = "PainSuppressant";
    [DataField] public float RecoveryMultiplier = 1f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-suppress-pain",
            ("chance", Probability),
            ("amount", Amount.Float()),
            ("duration", DecayDuration.TotalSeconds),
            ("recoveryMultiplier", RecoveryMultiplier));

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.TryGetComponent(args.TargetEntity, out PainComponent? pain))
            return;

        var scale = args is EntityEffectReagentArgs reagent ? reagent.Scale : FixedPoint2.New(1);
        args.EntityManager.System<PainSystem>()
            .SuppressPain((args.TargetEntity, pain), Identifier, Amount * scale, DecayDuration, RecoveryMultiplier);
    }
}
```

Notes. The explicit `TryGetComponent<PainComponent>` replaces Onyx's ECS component filter — old-style dispatch does no filtering (`MetabolizerSystem.cs:218` is a bare virtual call). `PainComponent` is ensured on the **body** by `WoundDamageProjectionSystem.SetupBody`, so a body-targeted reagent finds it, and `PainSystem.GetPainBeforeAdrenaline` (`:182-195`) redistributes body suppression pro rata to every part. `DecayDuration` deserialises from the bare seconds number Onyx's YAML uses (`decayDuration: 9`).

#### `MendFractures.cs`

```csharp
using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Systems;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.EntityEffects;

/// <summary>Reduces the severity of matching fractures on every body part of a wound host.</summary>
public sealed partial class MendFractures : EntityEffect
{
    /// <summary>Fracture wound prototypes to treat. Empty treats every fracture wound.</summary>
    [DataField] public HashSet<ProtoId<WoundPrototype>> Wounds = ["BoneFractureWound"];
    [DataField] public FractureGrade MinimumGrade = FractureGrade.Hairline;
    [DataField] public FractureGrade MaximumGrade = FractureGrade.Comminuted;
    [DataField] public FixedPoint2 Amount = 1;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        var wounds = Wounds.Count == 0
            ? Loc.GetString("reagent-effect-guidebook-all-fractures")
            : string.Join(", ", Wounds.Select(id =>
                prototype.TryIndex(id, out WoundPrototype? wound) ? Loc.GetString(wound.Name) : id.Id));
        return Loc.GetString("reagent-effect-guidebook-mend-fractures",
            ("chance", Probability),
            ("amount", Amount.Float()),
            ("wounds", wounds),
            ("minimumGrade", Loc.GetString($"fracture-grade-{MinimumGrade.ToString().ToLowerInvariant()}")),
            ("maximumGrade", Loc.GetString($"fracture-grade-{MaximumGrade.ToString().ToLowerInvariant()}")));
    }

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.HasComponent<WoundHostComponent>(args.TargetEntity))
            return;

        var scale = args is EntityEffectReagentArgs reagent ? reagent.Scale : FixedPoint2.New(1);
        var amount = Amount * scale;
        var body = args.EntityManager.System<SharedBodySystem>();
        var fractures = args.EntityManager.System<WoundFractureSystem>();
        var wounds = args.EntityManager.System<WoundSystem>();

        foreach (var (part, _) in body.GetBodyChildren(args.TargetEntity))
        {
            if (fractures.GetFracture(part) is not { } fracture ||
                Wounds.Count != 0 && !Wounds.Contains(fracture.Comp1.Prototype) ||
                fracture.Comp2.Grade < MinimumGrade ||
                fracture.Comp2.Grade > MaximumGrade)
                continue;

            wounds.ChangeSeverity(fracture.Owner, -amount);
        }
    }
}
```

`GetBodyChildren` yields body **parts**, and `GetFracture` returns on the first hit before any mutation, so the enumeration is not invalidated by `ChangeSeverity`'s possible `RemoveWound`. **If the implementer ever changes it to heal *all* fractures per part, materialise with `.ToArray()` first** (the `GibbingSystem` container-mutation crash class).

#### `TakeStaminaDamage.cs`

```csharp
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.EntityEffects;

/// <summary>Applies stamina damage from a metabolised reagent.</summary>
public sealed partial class TakeStaminaDamage : EntityEffect
{
    [DataField] public float Amount = 10f;

    /// <summary>Whether an already-critical target is dropped immediately instead of waiting for decay.</summary>
    [DataField] public bool Immediate;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-take-stamina-damage",
            ("chance", Probability), ("amount", Amount));

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.TryGetComponent(args.TargetEntity, out StaminaComponent? stamina))
            return;

        // WOLFGATE (P4-D5): Onyx gates on Scale == 1 because its framework can hand out partial ticks;
        // Wolfgate's MetabolizerSystem does the same (scale = mostToRemove / rate), so the gate is kept.
        if (args is EntityEffectReagentArgs reagent && reagent.Scale != FixedPoint2.New(1))
            return;

        // WOLFGATE (P4-D5): Wolfgate's StaminaSystem has the `immediate` mode Onyx's comment says vanilla lacks,
        // and defaults it to true; Onyx's datafield default is false, which is what this effect passes.
        args.EntityManager.System<StaminaSystem>()
            .TakeStaminaDamage(args.TargetEntity, Amount, stamina, visual: false, immediate: Immediate);
    }
}
```

#### `StaminaDamageCondition.cs`

```csharp
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.EntityEffects;

/// <summary>True while stamina damage on the target is strictly between Min and Max.</summary>
public sealed partial class StaminaDamageCondition : EntityEffectCondition
{
    [DataField] public float Min = -1f;
    [DataField] public float Max = float.PositiveInfinity;

    public override bool Condition(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.TryGetComponent(args.TargetEntity, out StaminaComponent? stamina))
            return false;

        return args.EntityManager.System<StaminaSystem>().GetStaminaDamage(args.TargetEntity, stamina) is var damage
               && damage > Min && damage < Max;
    }

    public override string GuidebookExplanation(IPrototypeManager prototype) => string.Empty;
}
```

Mirrors `Content.Server/EntityEffects/EffectConditions/TotalDamage.cs:16-26`. Wolfgate's `EntityEffectCondition` has neither Onyx's `Inverted` flag nor its `sourceEnt`, and no in-scope YAML uses either.

---

### 2.2 Tourniquet — 1 vendored shared file + 1 relocated server file

| File | Path | Edits |
|---|---|---|
| `TourniquetComponent.cs` (+ `TourniquetDoAfterEvent`) | `Content.Shared/_Onyx/Medical/Tourniquet/TourniquetComponent.cs` | **verbatim, zero edits.** No licence header in Onyx — do not invent one |
| `TourniquetSystem.cs` | **`Content.Server/_Onyx/Medical/Tourniquet/TourniquetSystem.cs`** (relocated, namespace unchanged) | **3 `// WOLFGATE` edits** |

```csharp
namespace Content.Shared._Onyx.Medical.Tourniquet;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TourniquetComponent : Component
{
    [DataField, AutoNetworkedField] public TimeSpan Delay = TimeSpan.FromSeconds(0.5);
    [DataField] public DamageSpecifier Damage = new();
    [DataField] public SoundSpecifier? BeginSound;
    [DataField] public SoundSpecifier? EndSound;
}

[Serializable, NetSerializable]
public sealed partial class TourniquetDoAfterEvent : SimpleDoAfterEvent
{
    public readonly NetEntity Part;
    public TourniquetDoAfterEvent(NetEntity part) { Part = part; }
}
```

The three system edits, all marked:

1. `using Content.Shared._Onyx.Targeting;` → `using Content.Shared._Shitmed.Targeting;` **and** `using Content.Shared._WF.Wolfmed.Targeting;` — D10. Onyx's `TargetingComponent` registers as `"Targeting"`, the exact name Shitmed's already uses, so porting it is a `ComponentFactory` crash at boot; WG's `_Shitmed` copy has the identical `public TargetBodyPart Target` field, so `TryComp(user, out TargetingComponent? targeting)` and `.Target` read unchanged.
2. `[Dependency] private TargetResolverSystem _targeting = default!;` → `[Dependency] private WoundTargetResolver _targeting = default!;` — `TryResolveExact(EntityUid body, TargetBodyPart target, out EntityUid part)` is a signature-exact match for Onyx's call site.
3. Drop the `[Dependency] private INetManager _net` field and its single `if (_net.IsServer) QueueDel(tourniquet);` guard, replacing the guard with the bare `QueueDel(tourniquet);` and a one-line reason — the class is now `Content.Server`, so the condition is always true.

Behaviour (`TryStart` → `CanApply` → do-after → `OnDoAfter` → `Apply`) needs **zero** logic changes. Every other symbol is SAME: `SharedBodySystem.BodyHasChild` (`Parts.cs:978`), `WoundSystem.GetWounds` (`:155`), `WoundBleedingSystem.SetTreatment`/`GetPartRate`, `WoundDamageRoutingSystem.TryApplyPartDamage` (`:240`), `WoundBleedingComponent.CurrentRate` (`WoundDamageComponents.cs:216`), `BleedingTreatment.Clamped` (`:272`, a 0f bleed multiplier at `WoundBleedingSystem.cs:469`).

---

### 2.3 Medical patch — 2 vendored server files, verbatim

`Content.Server/_Onyx/Medical/MedicalPatchComponent.cs` and `MedicalPatchSystem.cs`, namespace `Content.Server._Onyx.Medical`, **zero edits**. No wound dependency of any kind — it is a `StickyComponent`-driven periodic solution transfer. Every dependency is vanilla and present: `IGameTiming`, `SharedSolutionContainerSystem` (`TryGetSolution`, `TryGetInjectableSolution`, `SplitSolution`, `TryAddSolution`), `ReactiveSystem`, `StickySystem`, `SharedHandsSystem`, `ISharedAdminLogManager`, `UnremoveableComponent`.

```csharp
[RegisterComponent]
public sealed partial class MedicalPatchComponent : Component
{
    [DataField] public string SolutionName = "drink";
    [DataField] public FixedPoint2 TransferAmount = FixedPoint2.New(1);
    [DataField] public bool SingleUse;
    [DataField] public string? TrashObject = "UsedMedicalPatch";
    [DataField] public float UpdateTime = 1f;
    [DataField] public TimeSpan NextUpdate;
    [DataField] public FixedPoint2 InjectAmmountOnAttatch;      // sic — Onyx's spelling, kept verbatim
    [DataField] public FixedPoint2 InjectPercentageOnAttatch;   // sic
}
```

---

### 2.4 New surgery components — `Content.Shared/_WF/Wolfmed/Surgery/WolfmedSurgeryComponents.cs`

One file, namespace `Content.Shared._WF.Wolfmed.Surgery`. All ten registered names grepped free in this pass (§5.3). Every one is `[RegisterComponent, NetworkedComponent]` so the shared condition handlers agree client-side; none needs `AutoGenerateComponentState` (they are prototype data on nullspace singletons, never mutated at runtime).

```csharp
/// <summary>Gates a wound surgery on a matching wound being present on the selected part.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryWoundConditionComponent : Component
{
    [DataField] public ProtoId<WoundPrototype>? WoundPrototype;
    [DataField] public WoundVisibility? Visibility;
    [DataField] public WoundState? State;
    [DataField] public bool Bleeding;
    [DataField] public bool InternalBleeding;
    /// <summary>Inverts the whole test, so the surgery lists only while the wound is absent.</summary>
    [DataField] public bool Inverse;
}

/// <summary>Suture step: reduces the bleeding rate of the worst bleeding wound on the part.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryClampBleedingEffectComponent : Component
{
    [DataField(required: true)] public FixedPoint2 Amount;
    [DataField] public ProtoId<WoundPrototype>? WoundPrototype;
}

/// <summary>Treats a matching wound outright; the default Amount removes it in one step.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryTreatWoundEffectComponent : Component
{
    [DataField] public ProtoId<WoundPrototype>? WoundPrototype;
    [DataField] public bool InternalBleeding;
    [DataField] public FixedPoint2 Amount = FixedPoint2.MaxValue;
}

/// <summary>Gates a surgery on the selected part carrying a fracture in a grade/treatment window.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryFractureConditionComponent : Component
{
    [DataField] public FractureGrade MinGrade = FractureGrade.Hairline;
    [DataField] public FractureGrade? Grade;
    [DataField] public FractureTreatment? Treatment;
}

/// <summary>Advances the part's fracture to a treatment stage (Reduced by a bone setter, Mended by bone gel).</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryMendFractureEffectComponent : Component
{
    [DataField] public FractureTreatment Treatment = FractureTreatment.Mended;
}

/// <summary>Gates an organ surgery on a named organ slot on the selected part being damaged but alive.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryOrganDamagedConditionComponent : Component
{
    [DataField(required: true)] public string Slot = string.Empty;
    [DataField] public bool Inverse;
}

/// <summary>Restores health to a named organ slot on the selected part.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryOrganHealEffectComponent : Component
{
    [DataField(required: true)] public string Slot = string.Empty;
    [DataField] public FixedPoint2 Amount = 1;
}

/// <summary>Charges pain to the operated part when the step completes.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryPainEffectComponent : Component
{
    [DataField] public FixedPoint2 Amount = 5;
    /// <summary>Onyx's anaesthesia scale. Ships inert - Wolfgate has no consumer (P4-D22).</summary>
    [DataField] public FixedPoint2 SleepModifier = 1;
}

/// <summary>Opens a surgical incision as a real wound on the part, so it can bleed, be clamped and scar.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryIncisionWoundEffectComponent : Component
{
    [DataField] public FixedPoint2 Severity = 10;
    [DataField] public ProtoId<WoundPrototype> Wound = "SurgicalIncisionWound";
}

/// <summary>Clamps or closes the incision wounds this operation opened.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryIncisionTreatmentEffectComponent : Component
{
    [DataField] public ProtoId<WoundPrototype> Wound = "SurgicalIncisionWound";
    /// <summary>Clamp (the bleeders step) or Close (the close-incision step, which also rolls for a scar).</summary>
    [DataField] public WolfmedIncisionTreatment Treatment = WolfmedIncisionTreatment.Clamp;
}

public enum WolfmedIncisionTreatment : byte { Clamp, Close }
```

---

### 2.5 `Content.Shared/_WF/Wolfmed/Surgery/WolfmedSurgeryConditionSystem.cs` — shared

`public sealed class WolfmedSurgeryConditionSystem : EntitySystem`. **Every `SurgeryValidEvent` and `SurgeryStepCompleteCheckEvent` handler must live here, in `Content.Shared`** — `SurgeryBui.cs:281` runs `GetNextStep` → `IsStepComplete` client-side, and `:310` runs `CanPerformStep`; a server-only check handler makes the client treat the step as permanently complete and highlight the wrong row. All the data these handlers read is networked (`WoundComponent`, `WoundBleedingComponent`, `WoundInternalBleedingComponent`, `WoundFractureComponent`, `WoundScarComponent`, `WoundableComponent`, `WolfmedOrganComponent`, and the wounds container is a plain `Container`).

Dependencies: `IPrototypeManager`, `WoundSystem`, `WoundFractureSystem`, `SharedBodySystem`.

```csharp
public override void Initialize()
{
    SubscribeLocalEvent<WolfmedSurgeryWoundConditionComponent, SurgeryValidEvent>(OnWoundValid);              // S1
    SubscribeLocalEvent<WolfmedSurgeryFractureConditionComponent, SurgeryValidEvent>(OnFractureValid);        // S2
    SubscribeLocalEvent<WolfmedSurgeryOrganDamagedConditionComponent, SurgeryValidEvent>(OnOrganValid);       // S3
    SubscribeLocalEvent<WolfmedSurgeryClampBleedingEffectComponent, SurgeryStepCompleteCheckEvent>(OnClampCheck);   // S4
    SubscribeLocalEvent<WolfmedSurgeryTreatWoundEffectComponent, SurgeryStepCompleteCheckEvent>(OnTreatCheck);      // S5
    SubscribeLocalEvent<WolfmedSurgeryMendFractureEffectComponent, SurgeryStepCompleteCheckEvent>(OnFractureCheck); // S6
    SubscribeLocalEvent<WolfmedSurgeryOrganHealEffectComponent, SurgeryStepCompleteCheckEvent>(OnOrganCheck);       // S7
    SubscribeLocalEvent<WolfmedSurgeryIncisionTreatmentEffectComponent, SurgeryStepCompleteCheckEvent>(OnIncisionCheck); // S8
}

/// <summary>The highest-severity wound on a part matching a condition's filters, or null.</summary>
public Entity<WoundComponent>? FindWound(
    EntityUid part,
    ProtoId<WoundPrototype>? prototypeId = null,
    WoundState? state = null,
    WoundVisibility? visibility = null,
    bool bleeding = false,
    bool internalBleeding = false);

/// <summary>Sum of non-scar wound severity on a part whose prototype damages any type in the group.</summary>
public FixedPoint2 GetGroupSeverity(EntityUid part, ProtoId<DamageGroupPrototype> group);

/// <summary>The named organ slot on a part, if it carries Wolfmed health data.</summary>
public bool TryFindOrgan(EntityUid part, string slot, out Entity<WolfmedOrganComponent> organ);
```

`FindWound` is a port of `ONYX WoundSurgerySystem.cs:187-231`; `GetGroupSeverity` of `:167-185`. Both are `public` so the server system (§2.6) binds them by `[Dependency]` rather than duplicating ~60 lines.

`TryFindOrgan` is the D8/`TryGetOrganInSlot` substitution, and is the exact pattern Shitmed itself uses at `SharedSurgerySystem.Steps.cs:133`:

```csharp
public bool TryFindOrgan(EntityUid part, string slot, out Entity<WolfmedOrganComponent> organ)
{
    foreach (var (id, comp) in _body.GetPartOrgans(part))
    {
        if (comp.SlotId != slot || !TryComp(id, out WolfmedOrganComponent? health))
            continue;
        organ = (id, health);
        return true;
    }
    organ = default;
    return false;
}
```

**`OnFractureCheck` — the P4-D20 correction, spelled out because getting it wrong stalls the surgery:**

```csharp
private void OnFractureCheck(Entity<WolfmedSurgeryMendFractureEffectComponent> ent, ref SurgeryStepCompleteCheckEvent args)
{
    // Complete when the fracture is gone, already at or past the target treatment, or when the target can
    // never be reached: CanTreat gates Reduced on Grade >= profile.ReductionMinimumGrade (Simple), so a
    // Hairline fracture can never be Reduced and a "wait for Reduced" check would never finish.
    if (_fractures.GetFracture(args.Part) is not { } fracture)
        return;
    if (fracture.Comp2.Treatment >= ent.Comp.Treatment || !CanEverReach(fracture, ent.Comp.Treatment))
        return;
    args.Cancelled = true;
}
```

`CanEverReach` re-states `WoundFractureSystem.CanTreat`'s rule without touching the upstream `private static` (grade ≥ the profile's `ReductionMinimumGrade` for `Reduced`; always reachable for `Mended`).

---

### 2.6 `Content.Server/_WF/Wolfmed/Surgery/WolfmedWoundSurgerySystem.cs` — server

`public sealed class WolfmedWoundSurgerySystem : EntitySystem`. **Every `SurgeryStepEvent` handler must live here, in `Content.Server`** — `WoundBleedingSystem` and `OrganHealthSystem` are server-assembly classes despite their `Content.Shared._Onyx.*` namespaces, and a `Content.Shared` file taking them as a `[Dependency]` compile-fails on the client.

Because the event *types* differ, one component handled by both systems is legal — the crash rule is one system per `(component, event)` pair, and every pair below is unique (§5.1).

Dependencies: `IPrototypeManager`, `IRobustRandom`, `IConfigurationManager`, `WolfmedSurgeryConditionSystem`, `WoundSystem`, `WoundBleedingSystem`, `WoundFractureSystem`, `WoundScarSystem`, `WoundDamageRoutingSystem`, `Content.Shared._Onyx.Body.Systems.OrganHealthSystem`, `SharedBodySystem`, `PainSystem`.

```csharp
public override void Initialize()
{
    SubscribeLocalEvent<WolfmedSurgeryClampBleedingEffectComponent, SurgeryStepEvent>(OnClampBleeding);   // V1
    SubscribeLocalEvent<WolfmedSurgeryTreatWoundEffectComponent, SurgeryStepEvent>(OnTreatWound);         // V2
    SubscribeLocalEvent<WolfmedSurgeryMendFractureEffectComponent, SurgeryStepEvent>(OnMendFracture);     // V3
    SubscribeLocalEvent<WolfmedSurgeryOrganHealEffectComponent, SurgeryStepEvent>(OnHealOrgan);           // V4
    SubscribeLocalEvent<WolfmedSurgeryPainEffectComponent, SurgeryStepEvent>(OnSurgeryPain);              // V5
    SubscribeLocalEvent<WolfmedSurgeryIncisionWoundEffectComponent, SurgeryStepEvent>(OnOpenIncision);    // V6
    SubscribeLocalEvent<WolfmedSurgeryIncisionTreatmentEffectComponent, SurgeryStepEvent>(OnTreatIncision); // V7
}
```

Handler bodies, one line each in substance:

| # | Handler | Body |
|---|---|---|
| V1 | `OnClampBleeding` | `if (_conditions.FindWound(args.Part, ent.Comp.WoundPrototype, bleeding: true) is { } w) _bleeding.ReduceBleeding(w.Owner, ent.Comp.Amount);` |
| V2 | `OnTreatWound` | `if (_conditions.FindWound(args.Part, ent.Comp.WoundPrototype, internalBleeding: ent.Comp.InternalBleeding) is { } w) _wounds.TreatWound(w.Owner, ent.Comp.Amount);` — with `Amount = FixedPoint2.MaxValue` this drives `ChangeSeverity` to zero, which **removes the wound entity outright** (`WoundSystem.cs:284-314`) |
| V3 | `OnMendFracture` | `Reduced` → `_fractures.TryReduce(fracture.Owner)`; `Mended` → `_fractures.TryMend(fracture.Owner)` (which also honours `profile.RemoveWoundWhenMended`, `true` on `OrganicFractureProfile`) |
| V4 | `OnHealOrgan` | `if (_conditions.TryFindOrgan(args.Part, ent.Comp.Slot, out var organ)) _organHealth.ChangeHealth(organ, ent.Comp.Amount);` — `SetHealth` clamps to `[0, MaxHealth]` and raises `OrganFunctionChangedEvent` on a zero crossing, so a revived organ re-enables its phase-3 consequences |
| V5 | `OnSurgeryPain` | `_pain.ChangePain((args.Part, null), ent.Comp.Amount);` — `PainComponent` lives on the part |
| V6 | `OnOpenIncision` | `if (HasComp<WoundHostComponent>(args.Body)) _wounds.CreateOrMergeWound(args.Part, ent.Comp.Wound, ent.Comp.Severity);` — **the host gate is what makes PROTO G D2-safe** (§2.7) |
| V7 | `OnTreatIncision` | `Clamp` → `_bleeding.TreatPart(args.Part, BleedingTreatment.Clamped, ent.Comp.Wound)`; `Close` → for every open/stabilised matching wound (materialised with `.ToArray()`): `SetTreatment(Cauterized)`, `CloseWound`, roll `Math.Clamp(_cfg.GetCVar(CCVars.SurgeryScarChance), 0f, 1f)` and `_scars.CreateScar` on success, then `RemoveWound`. Port of `ONYX SurgerySystem.WoundEffects.cs:20-42` |

---

### 2.7 `Content.Shared/_WF/Wolfmed/Surgery/SharedSurgerySystem.Wolfmed.cs` — the hook bodies

`namespace Content.Shared._Shitmed.Medical.Surgery; public abstract partial class SharedSurgerySystem`. Precedent: `Content.Server/_WF/Wolfmed/Medical/HealingSystem.Wolfmed.cs:19-21`. It holds HOOK 24's and HOOK 25's bodies plus two new `[Dependency]` fields (`WoundSystem`, `WolfmedSurgeryConditionSystem`), so **neither upstream site gains a `using` or a dependency line**.

```csharp
/// <summary>HOOK 24 body: true when a wound-severity window is declared and the part is outside it.</summary>
private bool WolfmedWoundWindowFails(Entity<SurgeryWoundedConditionComponent> ent, EntityUid body, EntityUid part)
{
    if (ent.Comp.MinWoundSeverity is null && ent.Comp.MaxWoundSeverity is null ||
        !HasComp<WoundHostComponent>(body))
        return false;

    var severity = _wolfmedConditions.GetGroupSeverity(part, ent.Comp.WoundGroup);
    return ent.Comp.MinWoundSeverity is { } min && severity < min ||
           ent.Comp.MaxWoundSeverity is { } max && severity > max;
}

/// <summary>HOOK 25 body: true while a stump still carries an untreated AmputationConsequenceWound.</summary>
private bool WolfmedStumpBlocksAttachment(EntityUid parent)
{
    return TryComp(parent, out BodyPartComponent? bodyPart) && bodyPart.Body is { } body &&
           TryComp(body, out WoundHostComponent? host) &&
           _wolfmedWounds.GetWounds(parent).Any(w => w.Comp.Prototype == host.AmputationConsequenceWound);
}
```

Both return `false` immediately for a non-wound-host, which is what makes HOOK 24 and HOOK 25 **provably D2-neutral**: the two existing tend surgeries declare no bounds, and no non-host part can carry a wound at all.

**The D2-safe form of PROTO G (the incision-wound chain).** Re-verified in this revision: `SurgeryStepOpenIncisionScalpel` (`surgery_steps.yml:9-27`) carries `- type: SurgeryDamageChangeEffect { damage: { types: { Bloodloss: 10 } }, sleepModifier: 0.5 }` at `:22-26`. **`SurgeryStepCarefulIncisionScalpel` (`:303-316`) carries no damage effect at all** — `SurgeryStep` + `Sprite` + `SurgeryStepEmoteEffect`, nothing else. An earlier draft of this plan said both steps carried the block; that was wrong, and it is why PROTO G originally listed the careful incision.

Replacing the flat block outright would take the incision cost away from **non**-hosts too. The authorised edit therefore **keeps** the `SurgeryDamageChangeEffect` block on `SurgeryStepOpenIncisionScalpel` and adds `- type: WolfmedSurgeryIncisionWoundEffect { severity: 10 }` beside it. The result on a wound host is Onyx's wound *plus* Wolfgate's existing flat charge; both are `Bloodloss`-flavoured and `Bloodloss` is not in `WoundHostComponent.LocalizedDamageTypes`, so the flat 10 goes systemic exactly as it does today and the incision wound adds the bleed Onyx charges. **This is a deliberate, recorded ~1.3× increase in the cost of opening an incision on a wound host** (§7.3 deviation 9) chosen over a D2 breach. If the user prefers strict Onyx parity, the alternative is a marked conditional inside `SurgeryDamageChangeEffect`'s handler, which is a third upstream hook and is not authorised here.

**`SurgeryStepCarefulIncisionScalpel` is deliberately left alone (CRITIQUE4 B1).** It is used **only** by `SurgeryTendWoundsBrute` (`surgeries.yml:285-294`) and `SurgeryTendWoundsBurn` (`:298-307`), whose step lists are `[SurgeryStepCarefulIncisionScalpel, SurgeryStepRepair{Brute,Burn}Tissue, SurgeryStepSealTendWound]` and which contain neither `SurgeryStepClampBleeders` nor `SurgeryStepCloseIncision` (`grep -n` over `surgeries.yml` returns exactly `:291, :293, :304, :306` and nothing else). A wound effect there would leave one un-clamped, un-closed, never-scarred `SurgicalIncisionWound` per tend operation, stacking on `mergeMode: SeparateInstances` (`wounds.yml:374-382`) — the exact failure §8.5 trap 15 names. Onyx's careful incision carries no bleed effect either (`ONYX surgery_steps.yml:958-970`), so dropping it is Onyx parity as well as the safe call.

**`SurgeryStepSealTendWound` gains the `Close` treatment** so the chain is self-closing. It is the terminal cautery step of `SurgeryStopInternalBleeding`, `SurgeryMendFracture`, `SurgeryHealAmputationConsequence` and both `SurgeryTendWounds*Deep` (WP12-5), all of which open their incision through `requirement: SurgeryOpenIncision`. `SurgeryStepSealTendWound` (`:359-374`) removes only `IncisionOpen`; `SkinRetracted` and `BleedersClamped` survive on the part, so `SurgeryCloseIncision` (`surgeries.yml:20-30`) does remain listable and runnable afterwards — it is gated by `SurgeryPartPresentCondition`, and `SurgeryCloseIncisionConditionComponent`, whose `OnCloseIncisionValid` demands all five markers (`SharedSurgerySystem.cs:108-118`), is declared but used by **zero** prototypes (`grep -rn "SurgeryCloseIncisionCondition" Resources/Prototypes` → 0 hits). Giving `SealTendWound` the `Close` effect simply means the medic does not have to remember. A later `SurgeryCloseIncision` is then a wound-layer no-op, because V7's `Close` branch finds no matching open wound.

---

### 2.8 Analyzer payload — 3 vendored shared files, near-verbatim

`Content.Shared/_Onyx/Medical/`, Onyx's own relative paths (D6). Every type is a `record struct`/`class`/`enum`; **P4-4 registers no component and therefore has no registration-collision surface at all.**

```csharp
// HealthAnalyzerWoundDiagnostic.cs  - 1 marked edit: using Content.Shared._Shitmed.Targeting; for TargetBodyPart (D10)
[Serializable, NetSerializable]
public readonly record struct HealthAnalyzerWoundDiagnostic(
    FractureGrade Fracture, FractureTreatment FractureTreatment,
    float BleedingRate, BleedingTreatment BleedingTreatment,
    ushort ScarCount, FixedPoint2 Pain,
    List<HealthAnalyzerVisibleWound> VisibleWounds,
    BodyPartFunctionalityState Functionality,
    float InternalBleedingRate, HealthAnalyzerClottingPhase ClottingPhase)
{
    public bool HasFindings => Fracture != FractureGrade.None || BleedingRate > 0f || ScarCount > 0 ||
        Pain > FixedPoint2.Zero || VisibleWounds.Count > 0 ||
        Functionality != BodyPartFunctionalityState.Functional || InternalBleedingRate > 0f;
}

[Serializable, NetSerializable]
public readonly record struct HealthAnalyzerVisibleWound(LocId Name, LocId? StageName, int Count);

[Serializable, NetSerializable]
public enum HealthAnalyzerClottingPhase : byte { NotApplicable, None, InProgress, Complete, Mixed }

[Serializable, NetSerializable]
public sealed class HealthAnalyzerWoundDiagnostics
{
    public readonly Dictionary<TargetBodyPart, HealthAnalyzerWoundDiagnostic> Parts;
    public HealthAnalyzerWoundDiagnostics(Dictionary<TargetBodyPart, HealthAnalyzerWoundDiagnostic> parts) { Parts = parts; }
}

// HealthAnalyzerOrganInfo.cs - verbatim
[Serializable, NetSerializable]
public readonly record struct HealthAnalyzerOrganInfo(NetEntity Entity, FixedPoint2 Health, FixedPoint2 MaxHealth, int Order);

// HealthAnalyzerChemicalInfo.cs - verbatim
[Serializable, NetSerializable] public enum HealthAnalyzerSolutionType : byte { Bloodstream, Metabolites, Stomach, Lung }
[Serializable, NetSerializable] public readonly record struct HealthAnalyzerReagentInfo(string Prototype, FixedPoint2 Quantity);
[Serializable, NetSerializable] public sealed record HealthAnalyzerChemicalInfo(HealthAnalyzerSolutionType Type, List<HealthAnalyzerReagentInfo> Reagents);
```

`FractureGrade`, `FractureTreatment`, `BleedingTreatment`, `BodyPartFunctionalityState`, `TargetBodyPart`, `LocId`, `FixedPoint2` and `NetEntity` are all confirmed `[Serializable, NetSerializable]` in WG.

---

### 2.9 `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs` — server builders

`namespace Content.Server.Medical; public sealed partial class HealthAnalyzerSystem` — legal because the upstream class is `sealed partial` (`HealthAnalyzerSystem.cs:31`), and necessary because `HealthAnalyzerComponent` carries `[Access(typeof(HealthAnalyzerSystem), typeof(CryoPodSystem))]` (`Components/HealthAnalyzerComponent.cs:13`), which a standalone `_WF` system could not satisfy.

Five new `[Dependency]` fields live **here**, so the upstream file's dependency block is untouched: `WoundSystem _wounds`, `PainSystem _pain`, `BodyPartFunctionalitySystem _functionality`, `MobThresholdSystem _mobThreshold`, `IPrototypeManager _prototypes`. `_bodySystem` (`:37`) and `_solutionContainerSystem` (`:39`) are private fields of the same partial class and are already reachable.

```csharp
/// <summary>Per-part wound findings for a wound host, or null for anything else.</summary>
public HealthAnalyzerWoundDiagnostics? BuildWoundDiagnostics(EntityUid body);

/// <summary>Organ health rows in medical reading order, or null for a non-wound-host.</summary>
public List<HealthAnalyzerOrganInfo>? BuildOrganInfo(EntityUid body);

/// <summary>Reagent contents of the bloodstream, chemical, stomach and lung solutions.</summary>
public List<HealthAnalyzerChemicalInfo>? BuildChemicalInfo(EntityUid body, BloodstreamComponent? bloodstream);

/// <summary>The damage figure that actually decides crit and death on a wound host.</summary>
public FixedPoint2? BuildVitalDamage(EntityUid body);
```

All four are **`public`** per P4-D26.

**`BuildWoundDiagnostics` — port of `ONYX HealthAnalyzerSystem.cs:337-440` with exactly 2 marked edits:**

1. `if (!HasComp<SurgeryTargetComponent>(body)) return null;` → `if (!HasComp<WoundHostComponent>(body)) return null;` — **D2.** A Shitmed `SurgeryTarget` without `WoundHost` (a borg, a Protogen) has no `WoundableComponent` anywhere and would return an always-empty dict, which the client renders as "no findings" instead of "unavailable".
2. `SharedTargetingSystem.TryConvert(bodyPart.PartType, bodyPart.Symmetry, out var target)` → `_bodySystem.GetTargetBodyPart(bodyPart.PartType, bodyPart.Symmetry) is not { } target` (`_Shitmed/Body/Systems/SharedBodySystem.Targeting.cs:378`).

Everything else ports verbatim: worst untreated fracture (`Treatment != Mended && Grade > fracture`); `bleedingRate +=` each wound's `CurrentRate` with `BleedingTreatment` taken from the highest-rate bleeder; `scarCount++` per `WoundScarComponent`; `internalBleedingRate += Rate * Severity.Float()` for **open** wounds only; the clotting phase (`AutomaticClottingAt != null` → `InProgress`; else `NaturalClotting > 0f && CurrentRate <= 0f` → `Complete`; else `None`; 0 phases → `NotApplicable`, >1 → `Mixed`); visible wounds keyed `(prototype.Name, GetStageDefinition(severity)?.Name)` and counted, skipping anything not `WoundVisibility.Visible` or in state `Healed`/`Scarred`; `_pain.GetPain((part, painComponent))`; `_functionality.GetState((part, woundable))`; and the `HasFindings` gate that keeps clean parts out of the dict entirely.

**`BuildOrganInfo` — rewritten for D8, ~20 lines:**

```csharp
public List<HealthAnalyzerOrganInfo>? BuildOrganInfo(EntityUid body)
{
    if (!HasComp<WoundHostComponent>(body))
        return null;

    var result = new List<HealthAnalyzerOrganInfo>();
    foreach (var (organ, component) in _bodySystem.GetBodyOrgans(body))
    {
        // WOLFGATE (D8): health lives on WolfmedOrganComponent, and Onyx's OrganCategoryPrototype is Nubody -
        // Shitmed's SlotId already carries the same ids (brain, eyes, lungs, heart, stomach, liver, kidneys).
        if (!TryComp(organ, out WolfmedOrganComponent? health))
            continue;
        result.Add(new HealthAnalyzerOrganInfo(GetNetEntity(organ), health.Health, health.MaxHealth,
            OrganOrder(component.SlotId)));
    }
    result.Sort((l, r) => l.Order.CompareTo(r.Order));
    return result;
}

private static int OrganOrder(string slotId) => slotId.ToLowerInvariant() switch
{
    "brain" => 0, "eyes" => 1, "ears" => 2, "tongue" => 3, "lungs" => 4,
    "heart" => 5, "liver" => 6, "stomach" => 7, "appendix" => 8, "kidneys" => 9, _ => 10,
};
```

Only the seven organs PROTO A annotated in phase 3 carry `WolfmedOrganComponent`, so the Organs tab shows **7 rows for a human and nothing for any other species** until phase 5 — a recorded limitation (§7.3), and the same idea as Onyx's "hide `MaxHealth == 0`" filter.

**`BuildChemicalInfo`** ports verbatim with one marked edit — `bloodstream.MetabolitesSolutionName` → `bloodstream.ChemicalSolutionName` (`BloodstreamComponent.cs:151`, default `"chemicals"`) — keeping `HealthAnalyzerSolutionType.Metabolites` as the wire value and localising it as **"Chemicals"** so the wire type does not churn. Its gate drops Onyx's `SurgeryTarget` requirement (chemicals are not a wound feature, so D2 does not apply): any target with a bloodstream or organs gets a list. `StomachSystem.DefaultSolutionName` (`"stomach"`) and `LungComponent.SolutionName` are SAME — note `LungComponent` is in `Content.Shared.Body.Components` in WG, not `Content.Server`.

**`BuildVitalDamage`** is five lines and fixes a real phase-1..3 usability gap — today the analyzer's "Total Damage" is the projection sum, which diverges from what actually kills a wound host:

```csharp
public FixedPoint2? BuildVitalDamage(EntityUid body) =>
    HasComp<WoundHostComponent>(body) && TryComp(body, out DamageableComponent? damageable)
        ? _mobThreshold.CheckVitalDamage(body, damageable)
        : null;
```

---

### 2.10 Analyzer client — 4 files

| File | Content |
|---|---|
| `Content.Client/_Onyx/Medical/HealthAnalyzer/EllipsisLabel.cs` | **verbatim** from ONYX (133 lines). Sandbox-clean: `Rune`, `StringBuilder`, `StringRuneEnumerator` and `EnumerateRunes()` are all whitelisted (`RobustToolbox/Robust.Shared/ContentPack/Sandbox.yml:896,897,994,1509`) and `Font.GetCharMetrics` exists. **Keep Onyx's `OopsConcat` trick** — it exists precisely to stop Roslyn emitting span code the sandbox rejects. A plain `Label` with `ClipText` is the fallback if it fights the build |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml` | ~45 lines. **Root element is a `BoxContainer`, not a window** — state it explicitly in the WP brief, because the project's reserved-name rule (`CloseButton`/`ContentsContainer`/`TitleLabel`/`WindowHeader`) applies only to `DefaultWindow` descendants and none of the panel's own names may drift into that trap. A **4**-button tab strip: `DamageButton`/`WoundsButton`/`OrgansButton`/`ChemicalsButton`, `StyleClasses` `OpenRight`/`OpenBoth`/`OpenBoth`/`OpenLeft` (all present at `Stylesheets/StyleBase.cs:21,23,24`). `Damage` is the fourth tab because the tab strip has to be able to give the upstream damage-groups section back — it shows `WolfmedDamageGroupsPanel` (the `Name=` HOOK 26 adds at `HealthAnalyzerWindow.xaml:288`) and hides the panel's own containers. Hiding the inner `GroupsContainer` alone would leave an empty expanded black panel, which is why the name goes on the outer `PanelContainer`. Then `WoundFindingsContainer`, `OrgansContainer`, `ChemicalsContainer`, a `VitalDamageLabel`, and a `ScrollContainer` so the panel can never push the window |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml.cs` | ~200 lines: `Populate(HealthAnalyzerScannedUserMessage msg)`, `DrawWoundDiagnostics`, `DrawOrgans`, `DrawChemicals`, plus local `CreateDiagnosticGroupTitle`/`CreateDiagnosticItemLabel`/`GetTexture` copies (duplicating three small helpers beats making the upstream ones public). Lifted from `ONYX HealthAnalyzerControl.xaml.cs:252-331, 357-409, 413-520`. Also carries `internal static bool IsDangerousBloodLevel(float level) => level < 0.65f;` |
| `Content.Client/_WF/Wolfmed/Medical/HealthAnalyzerWindow.Wolfmed.cs` | `namespace Content.Client.HealthAnalyzer.UI; public sealed partial class HealthAnalyzerWindow` — `PopulateWolfmed(msg)`: hides `WolfmedPanel` entirely when `msg.WoundDiagnostics == null && msg.Organs == null` (so a non-wound-host scan renders byte-for-byte as it does today), otherwise feeds the panel and swaps `GroupsContainer`'s visibility against it for the tab strip. Legal because the window class is `public sealed partial` (`HealthAnalyzerWindow.xaml.cs:32-33`), so the partial can read the generated private fields a standalone control could not |

The findings list renders one bullet per part with findings, in `SharedTargetingSystem.GetValidParts()` order, as `health-analyzer-wound-part-summary = { $part }: { $details }` with details joined by ` · `:

| Order | Detail | Source |
|---|---|---|
| 1 | visible wounds, e.g. `Laceration (deep) ×2`, comma-joined | `VisibleWounds` |
| 2 | **`fracture: {grade}` / `fracture: {grade} ({treatment})`** | `Fracture`/`FractureTreatment` — **new, P4-D25**; Onyx carries these and never prints them |
| 3 | `external bleeding` | `BleedingRate > 0` |
| 4 | `internal bleeding` | `InternalBleedingRate > 0` |
| 5 | `clotting in progress` / `bleeding stopped` / `partial hemostasis` | `ClottingPhase` |
| 6 | `scars: {count}` | `ScarCount > 0` |
| 7 | `pain: {pain}` | `Pain > 0` |
| 8 | `reduced function` / `function lost` / `part absent` | `Functionality != Functional` |

Plus, above the list: `The patient has a [color=red]dangerously low[/color] blood level.` when `BloodLevel < 0.65`; `No contact with patient.` when scan mode is off; `Diagnostics unavailable for this patient.` when `WoundDiagnostics` is null.

**Do not port** `Content.Client/_Onyx/Targeting/UI/HealthAnalyzerStatusDoll.xaml(.cs)` or `/Textures/_Onyx/Interface/Targeting/**` — WG already has an 11-button doll inline at `HealthAnalyzerWindow.xaml:68-203` driven by `/Textures/_Shitmed/Interface/Targeting/Status/*.rsi` (11 RSIs, verified).

---

### 2.11 `Content.Server/_WF/Wolfmed/Explosion/WolfmedExplosionSystem.cs` (P4-D14)

Holds the two CVar reads too, so **`ExplosionSystem.CVars.cs` needs no edit at all** (unlike Onyx, which reads them there).

```csharp
namespace Content.Server._WF.Wolfmed.Explosion;

/// <summary>Routes explosion damage on wound hosts through the distributed per-part split instead of one flat body hit.</summary>
public sealed class WolfmedExplosionSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private WoundDamageRoutingSystem _routing = default!;

    private float _variation;
    private float _multiplier;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_cfg, CCVars.ExplosionLimbDamageVariation, v => _variation = v, true);
        Subs.CVar(_cfg, CCVars.ExplosionWoundMultiplier, v => _multiplier = v, true);
    }

    /// <summary>True when the blast was spread across a wound host's parts; false means the caller should fall through.</summary>
    public bool TryApplyExplosionDamage(EntityUid entity, DamageSpecifier damage)
    {
        if (!HasComp<WoundHostComponent>(entity))
            return false;

        return _routing.TryRouteDistributedDamage(entity, damage, TargetBodyPart.All,
            DamageDistribution.SplitWithVariation, ignoreResistances: true, interruptsDoAfters: false,
            variation: _variation, isExplosion: true, woundSeverityMultiplier: _multiplier,
            originFlag: DamageableSystem.DamageOriginFlag.Explosion);
    }
}
```

The origin-flag parameter is new; it is added inside the already heavily-marked vendored routing file (§3, in-vendored-file edits), as an **optional trailing parameter** so every existing caller — including phase 3's `T-AMP-EXPLOSION` — keeps compiling with identical behaviour:

```csharp
// WOLFGATE (P4-D14): the distributed entry points bypass OnBeforeDamageChanged, the only other writer of
// _routedModifiers, so without this the re-entrant write reaches SharedArmorPlateSystem with OriginFlag == null
// and its gate (Origin == null && OriginFlag != Explosion) refuses plate protection against explosions entirely.
    DamageableSystem.DamageOriginFlag? originFlag = null)
...
    var hadModifiers = _routedModifiers.TryGetValue(body, out var previous);   // WOLFGATE (P4-D14)
    _routedModifiers[body] = (0f, null, originFlag);                           // WOLFGATE (P4-D14)
    try { ... }
    finally
    {
        if (hadModifiers) _routedModifiers[body] = previous;                   // WOLFGATE (P4-D14)
        else _routedModifiers.Remove(body);
    }
```

The save/restore (rather than a bare `Remove`) is deliberate: `_routedModifiers` is a per-body dictionary with one other writer, and a bare remove would be the same non-reentrancy bug as trap T3.

---

### 2.12 Locale files

| File | Status | Content |
|---|---|---|
| `Resources/Locale/en-US/_Onyx/guidebook/entity-effects.ftl` | new | Onyx's 3 guidebook bodies verbatim, **keys renamed to Wolfgate's `reagent-effect-guidebook-*` convention** (`reagent-effect-guidebook-suppress-pain`, `-mend-fractures`, `-all-fractures`), plus the four `fracture-grade-*` keys **unrenamed** (`MendFractures` builds them by string interpolation), plus one new key with no Onyx source, `reagent-effect-guidebook-take-stamina-damage` (Onyx's effect overrides nothing; Wolfgate's `ReagentEffectGuidebookText` is `abstract`, so every subclass must return something — returning `null` would hide the effect from the chemistry guidebook entirely). `NATURALFIXED` and `MANY` are registered (`ContentLocalizationManager.cs:38,52`) |
| `Resources/Locale/en-US/_Onyx/reagents/medicine.ftl` | new | `reagent-name-*`/`reagent-desc-*` for the five Tier-A reagents. **None of these keys exists in WG today** — verified for osteogen, ibuprofen, ketorolac, tramadol, oxycodone |
| `Resources/Locale/en-US/_Onyx/medical/tourniquet.ftl` | new | Onyx's 3 keys verbatim (`tourniquet-selected-part-missing`, `-no-bleeding`, `-applied`). No collision with WG's `medical-item-*` namespace. Skip Onyx's `ru-RU` copy |
| `Resources/Locale/en-US/_Onyx/medical/medical_patch.ftl` | new | Onyx's 8 keys verbatim (2 `ent-*` names + 4 sticky popups) |
| `Resources/Locale/en-US/_WF/wolfmed/surgery-popup.ftl` | new | 12 `surgery-popup-step-*` keys, in **Wolfgate's `{$user}` style** (no inner spaces), not Onyx's `{ $user }`. Popup keys resolve as `surgery-popup-procedure-{surgery}-step-{step}` then `surgery-popup-step-{step}` (`Steps.cs:481-489`). Entity **names** are inline `name:` in Wolfgate YAML, so Onyx's entity-name ftl is not needed |
| `Resources/Locale/en-US/_Onyx/medical/health-analyzer-component.ftl` | new | the 11 live wound keys from ONYX `:6-16` (skip the 4 disease keys and the dead `health-analyzer-wound-pain` at `:5`) + the 2 new fracture keys from P4-D25 |
| `Resources/Locale/en-US/_Onyx/targeting/targeting.ftl` | new | the 11 `targeting-part-*` keys from ONYX `:20-32`, **`chest` renamed `torso`, `groin` omitted** (D9). WG has zero `targeting-part-*` keys today |
| `Resources/Locale/en-US/medical/components/health-analyzer-component.ftl` | **modified (upstream)** | 19 keys appended inside one `# WOLFGATE (P4-4)` block: vital-damage, body/organs tab + organ health, chemicals tab + 4 solution names + empty + reagent, whole-body + damage-part, wound-diagnostics title/inactive/unavailable, blood-level-dangerous |
| `Resources/Locale/en-US/_WF/Wolfmed/guidebook/wounds.ftl` | new | adapted subset of Onyx's ~30 keys: drop the ~9 IPC/slime/cybernetic keys, reword ~3 for guns/fractures/amputation-consequence |

---

## 3. Upstream `// WOLFGATE` hooks — the complete authorised phase-4 list

Nothing outside this section may be edited in an upstream (non-`_Onyx`, non-`_WF`) file without escalating.
Line numbers were re-verified against the tree as it stands today (phases 1–3 committed, HEAD `6329d204e3`).

### 3.1 Code hooks

| # | File | Site | Change | WP |
|---|---|---|---|---|
| **HOOK 9** | `Content.Server/EntityEffects/Effects/HealthChange.cs` | usings after `:6`; field list after `:38`; the single `TryChangeDamage(...)` call at **`:167-176`** | **3 marked additions, ~16 lines.** (a) `using Content.Shared._Onyx.Wounds; // WOLFGATE: HOOK 9`. (b) `[DataField] public HashSet<TreatmentCapability> TreatmentCapabilities = [TreatmentCapability.Biological];`. (c) turn the call into a local function and branch: `var change = Damage * scale; void Apply() => …TryChangeDamage(args.TargetEntity, change, IgnoreResistances, interruptsDoAfters: false, targetPart: TargetBodyPart.All, partMultiplier: 1.00f, canSever: false);` then `if (change.DamageDict.Values.Any(a => a < 0) && args.EntityManager.HasComponent<WoundHostComponent>(args.TargetEntity)) …WithTreatmentCapabilities(args.TargetEntity, TreatmentCapabilities, Apply); else Apply();`. **The Shitmed/Mono argument block at `:172-176` must survive byte-for-byte** (`partMultiplier: 1.00f` carries a `// Mono, 0.5f->1.00f` comment). `System.Linq` is already imported at `:9`. A pure two-line hook is impossible here because the call must become a delegate | WP12-1 |
| **HOOK 9** (b) | `Content.Server/EntityEffects/Effects/EvenHealthChange.cs` | usings (which end at `:8`); field list; the `TryChangeDamage(...)` call at **`:135-139`** | Same three additions, ~15 lines. **Needs `using System.Linq;` as well — it is not currently present.** The healing test is `Damage.Values.Any(amount => amount < 0)` over `Dictionary<ProtoId<DamageGroupPrototype>, FixedPoint2>`. **Note the difference from HOOK 9(a): this file passes `dspec * scale`, i.e. the modifier-adjusted spec, so the local becomes `var final = dspec * scale;`** | WP12-1 |
| **HOOK 22** | `Content.Server/Explosion/EntitySystems/ExplosionSystem.Processing.cs` | the `_damageableSystem.TryChangeDamage(...)` call at **`:471-473`**, plus one `[Dependency]` field and one `using` | **3 marked lines.** `if (!_wolfmedExplosion.TryApplyExplosionDamage(entity, damage)) { <the existing call, unchanged, including its `// Mono: Explosion flag for plate protection` comment> }`. Body in `Content.Server/_WF/Wolfmed/Explosion/WolfmedExplosionSystem.cs` (§2.11). Non-hosts fall straight through to today's call — D2-identical | WP12-8 |
| **HOOK 23** | `Content.Server/Medical/HealthAnalyzerSystem.cs` | the `ServerSendUiMessage` argument list ending at **`:286`** | **One line**: after `part != null ? GetNetEntity(part) : null,` add `BuildWoundDiagnostics(target), BuildOrganInfo(target), BuildChemicalInfo(target, bloodstream), BuildVitalDamage(target) // WOLFGATE: HOOK 23`. **No `using` and no `[Dependency]` land upstream** — the builders and their five dependencies live in the `_WF` partial (§2.9). `bloodstream` is already in scope from the `TryComp` at `:256` | WP12-6 |
| **HOOK 24** | `Content.Shared/_Shitmed/Surgery/SharedSurgerySystem.cs` | end of `OnWoundedValid`, after **`:127`** | **2 marked lines**: `if (WolfmedWoundWindowFails(ent, args.Body, args.Part)) // WOLFGATE: HOOK 24 — P4-D19 wound-severity window` / `    args.Cancelled = true;`. Body in the `_WF` partial (§2.7), which also carries the two new `[Dependency]` fields, so **no `using` lands upstream** | WP12-4 |
| **HOOK 25** | `Content.Shared/_Shitmed/Surgery/SharedSurgerySystem.cs` | inside `OnPartRemovedConditionValid` (`:255-274`), immediately after the `CanAttachToSlot` guard block, which closes at **`:262`** | **2 marked lines**: `if (WolfmedStumpBlocksAttachment(args.Part)) // WOLFGATE: HOOK 25 — P4-D18 untreated amputation consequence` / `    { args.Cancelled = true; return; }`. Because `OnTargetDoAfter` re-runs `IsSurgeryValid` (`:94`), this hides the `SurgeryAttach*` surgeries that target the stump **and** aborts a do-after that somehow started. **There are 10 `SurgeryAttach*` surgeries, not 11**, and a torso stump hides 6 of them (P4-D18) | WP12-4 |
| **HOOK 26** | `Content.Client/HealthAnalyzer/UI/HealthAnalyzerWindow.xaml` **and** `.xaml.cs` | `.xaml` header; the damage-groups `PanelContainer` at **`:288`**; inside `RootContainer` (which closes at `:309`) at **`:308`**, after that panel's close at `:307`. `.xaml.cs` **first** statement of `Populate` (**`:112`**, immediately after the `{` at `:111`) and inside the early-return block at **`:119-122`** | **5 marked lines total (revised — CRITIQUE4 M8).** XAML (marked `<!-- WOLFGATE: HOOK 26 -->`): (a) `xmlns:wolfmed="clr-namespace:Content.Client._WF.Wolfmed.Medical"`; (b) `Name="WolfmedDamageGroupsPanel"` on the existing un-named `<PanelContainer VerticalExpand="True" HorizontalExpand="True" Margin="4 4 4 4">` at `:288`, so the tab strip can hide the whole damage section instead of just the inner `GroupsContainer` — hiding `GroupsContainer` alone leaves an empty expanded black panel; (c) `<wolfmed:WolfmedDiagnosticPanel Name="WolfmedPanel" Visible="False" VerticalExpand="True" />` at `:308`. Code-behind: (d) `PopulateWolfmed(msg); // WOLFGATE: HOOK 26` as the **first** statement of `Populate`, **not the last** — `Populate` early-returns at `:119-122` (`_target == null`, or the target/part has no `DamageableComponent`) and a last-statement call would leave the panel rendering the **previous** patient's rows next to "No patient data", reachable once a second simply by walking out of range; (e) `WolfmedPanel.Visible = false; // WOLFGATE: HOOK 26` inside that early-return block, because a target outside client PVS can still carry non-null diagnostics. **Only `WolfmedPanel` and `WolfmedDamageGroupsPanel` enter the window's `[GenerateTypedNameReferences]` scope** (31 `Name=` attributes there today); everything else lives in the panel's own scope, which is the point of P4-D25 | WP12-7 |

### 3.2 Upstream non-hook edits

| # | File | Change | WP |
|---|---|---|---|
| **EXT 1** | `Content.Shared/_Shitmed/Surgery/Conditions/SurgeryWoundedConditionComponent.cs` | The file currently ends `public sealed partial class SurgeryWoundedConditionComponent : Component;` at `:7`. Replace the `;` with a body carrying three marked datafields: `[DataField] public ProtoId<DamageGroupPrototype> WoundGroup = "Brute";`, `[DataField] public FixedPoint2? MinWoundSeverity;`, `[DataField] public FixedPoint2? MaxWoundSeverity;`. **+6 lines, purely additive; null bounds keep the pre-Wolfmed behaviour exactly** | WP12-4 |
| **EXT 2** | `Content.Shared/MedicalScanner/HealthAnalyzerScannedUserMessage.cs` | **Authorised here because §3 is exhaustive and WP12-6 must edit it (CRITIQUE4 M1).** The file is upstream and Wolfmed-untouched today — read in full in this revision, it carries only `// Shitmed Change` and `// Frontier` markers. Add **4 nullable fields** after `public bool? Uncloneable; // Frontier`, **4 optional trailing constructor parameters** after the existing `NetEntity? part = null`, and **2 marked `using`s** (`Content.Shared._Onyx.Medical` for the three payload types, `Content.Shared.FixedPoint` for `FixedPoint2`). ~10 lines, purely additive. Safe because the only other construction site, `CryoPodSystem.cs:206-221`, passes nine positional arguments | WP12-6 |
| **PROTO D** | `Resources/Prototypes/Entities/Objects/Specific/Medical/healing.yml`, the `Tourniquet` entity at **`:267-298`** (`id:` at `:268`; corrected in this revision) | Replace the `- type: Healing` block (and only that block) with `- type: Tourniquet` carrying `damage: { types: { Blunt: 5, Asphyxiation: 5 } }`, `delay: 0.5`, and the same two `beginSound`/`endSound` paths the `Healing` block already names (`/Audio/Items/Medical/brutepack_{begin,end}.ogg`, both present). **Leave the `tags:` list alone** — P4-D9: Onyx adds a `Tourniquet` tag, WG has no such tag prototype, and an undeclared tag fails the Release lint. Zero new ids, zero new assets, zero touched fill/vending/spawner files (the id is referenced by 8 other files and all keep working) | WP12-3 |
| **PROTO E** | `Resources/Prototypes/Catalog/Fills/Items/firstaidkits.yml`, `MedkitAdvancedFilled` at **`:78-87`** (`id:` at `:78`, `contents:` at `:83-87`; corrected in this revision) | **One line**: `      - id: Tourniquet # WOLFGATE (P4-2): matches Onyx's own <Onyx-MedkitContents> edit`. **`MedkitCombatFilled` (`:88-98`) is NOT touched** — Onyx does not add one there and guessing is worse than parity | WP12-3 |
| **PROTO F** | `Resources/Prototypes/_Shitmed/Entities/Surgery/surgeries.yml`, under `- type: SurgeryWoundedCondition` on `SurgeryTendWoundsBrute` (**`:294`**) and `SurgeryTendWoundsBurn` (**`:307`**) | **Two lines**: `    maxWoundSeverity: 99.99   # WOLFGATE (P4-D19): deep wounds get SurgeryTendWounds*Deep instead`. Plus `woundGroup: Burn` on the Burn one so `GetGroupSeverity` reads the right group. **Line numbers corrected in this revision (CRITIQUE4 m1)** — `grep -n "SurgeryWoundedCondition" surgeries.yml` → `294`, `307`; the earlier `:285`/`:298` were the `id:` lines of the two entities | WP12-5 |
| **PROTO G** | `Resources/Prototypes/_Shitmed/Entities/Surgery/surgery_steps.yml` — `SurgeryStepOpenIncisionScalpel` (**`:9`**), `SurgeryStepClampBleeders` (**`:31`**), `SurgeryStepCloseIncision` (**`:168`**), `SurgeryStepSealTendWound` (**`:359`**) | **Four marked component additions (P4-D21) — and `SurgeryStepCarefulIncisionScalpel` (`:303`) is NOT one of them (CRITIQUE4 B1).** `SurgeryStepOpenIncisionScalpel` gains `- type: WolfmedSurgeryIncisionWoundEffect { severity: 10 }` **beside** its existing `SurgeryDamageChangeEffect` (§2.7 — do not remove it, that would change non-host behaviour). `SurgeryStepClampBleeders` gains `- type: WolfmedSurgeryIncisionTreatmentEffect { treatment: Clamp }`. `SurgeryStepCloseIncision` **and** `SurgeryStepSealTendWound` each gain `- type: WolfmedSurgeryIncisionTreatmentEffect { treatment: Close }`. **All four or none** — shipping only the first leaves a permanently bleeding stacking wound, and shipping the first on the careful incision leaves one on every tend operation | WP12-5 |
| **PROTO H** | `Resources/Prototypes/_Goobstation/Reagents/medicine.yml`, `Stasizium`'s `Medicine:` effect list | One marked `- !type:MendFractures` block (`amount: 10`, `wounds: []`, `minimumGrade: Hairline`, `maximumGrade: Comminuted`). **The group is `Medicine`, not Onyx's `Bloodstream`** | WP12-2 |
| **PROTO I** | `Resources/Prototypes/Reagents/medicine.yml`, `Bicaridine` (**`:145`**) | One marked `- !type:SuppressPain` block (`amount: 0.75`, `decayDuration: 18`, `identifier: Bicaridine`, `recoveryMultiplier: 1.75`) in the **`Medicine:`** group (Onyx puts it in `Bloodstream`; WG's Bicaridine has no such group) | WP12-2 |
| **PROTO J** | `Resources/Prototypes/Reagents/narcotics.yml`, `Desoxyephedrine` (**`:2`**) and `Happiness` (**`:566`**) | Two marked `- !type:SuppressPain` blocks. Desoxyephedrine's goes in the **`Narcotic:`** group (`:45`), **not** `Poison:` — `amount: 0.75`, `decayDuration: 9`, `identifier: Desoxyephedrine`, `recoveryMultiplier: 1.75`. Happiness: `amount: 0.4`, `decayDuration: 9`, `identifier: Happiness`, `recoveryMultiplier: 1.5` | WP12-2 |
| **PROTO K** | `Resources/Prototypes/Reagents/Consumable/Drink/alcohol.yml`, `Cognac` (**`:106`**) | One marked `- !type:SuppressPain` block (`amount: 0.25`, `decayDuration: 9`, `identifier: Painkiller`, `recoveryMultiplier: 1.1`) **in the `Drink:` group (`:120-127`) — Cognac's only metabolism group (CRITIQUE4 M5).** Onyx puts its copy in a `Digestion:` group (`ONYX alcohol.yml:111-122`) that Wolfgate does not have. Cognac redeclares the whole `Drink:` block rather than inheriting `BaseAlcohol`'s (`base_drink.yml:54-61`), so the addition goes into Cognac's own block. `Drink` is processed by the stomach metabolizer rather than the liver's `Medicine`/`Narcotic` pass, so the suppression cadence differs slightly from Onyx's — recorded as deviation 24 (§7.3) | WP12-2 |
| **PROTO L** | `Resources/Prototypes/Guidebook/medical.yml`, `Medical`'s `children:` list (**`:5-12`**, `MedicalDoctor` at `:7`, `Chemist` at `:8` — corrected in this revision, CRITIQUE4 m4) | **Two lines**: `  - Wounds` and `  - WoundTreatment`, inserted after `MedicalDoctor` and before `Chemist` | WP12-10 |
| **LOC A** | `Resources/Locale/en-US/medical/components/health-analyzer-component.ftl` | 19 appended keys in one `# WOLFGATE (P4-4)` block (§2.12) | WP12-7 |

**Metabolism-group trap for PROTO H–K:** Onyx's ported reagents all use `Bloodstream:`; Wolfgate's use `Medicine`, `Narcotic`, `Poison` or `Bloodstream` depending on the reagent. A blind copy of Onyx's group header lands a `SuppressPain` in a group the target's metabolizer does not process, and the effect silently never fires. The four groups above were read out of the tree in this pass; re-confirm each before editing.

### 3.3 In-vendored-file `// WOLFGATE` edits (not upstream hooks)

| File | Edits | WP |
|---|---|---|
| `Content.Server/_Onyx/Medical/Tourniquet/TourniquetSystem.cs` (new, vendored, relocated) | **3 sites** — the `using` swap, the `TargetResolverSystem` → `WoundTargetResolver` dependency, and the dead `INetManager` guard (§2.2) | WP12-3 |
| `Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs` (new, vendored) | **1 site** — `using Content.Shared._Shitmed.Targeting;` for `TargetBodyPart` (D10) | WP12-6 |
| `Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs` | **3 sites** — the optional `originFlag` parameter on `TryApplyDistributedDamage` and `TryRouteDistributedDamage`, and the save/restore of `_routedModifiers` around the distributed body (§2.11). The file already carries ~25 marked edits | WP12-8 |

### 3.4 Explicitly NOT touched in phase 4 (and why)

- **`Content.Shared/Body/Systems/SharedBodySystem.Parts.cs`** — **P4-D18.** A `CanAttachPart` guard reaches `AttachPart`'s five non-surgery callers (`PrybarProstheticsSystem.cs:118`, `BionicLegsSystem.cs:104`, `AutoSurgeonSystem.cs:116`, `GenerateChildPartSystem.cs:48`, plus `TryCreatePartSlotAndAttach`'s admin commands and `BodyRejuvenateSystem.cs:330`) and would silently break prosthetics on exactly the limb they exist for. **This overrules DECISIONS P4-3's prose and `tests.md` §8 decision 2.**
- **`Content.Shared/_Shitmed/Surgery/Effects/Step/SurgeryTendWoundsEffectComponent.cs` and `SharedSurgerySystem.Steps.cs`** — **P4-D16.** Routing already carries negative step damage into `HealWounds`; the only missing Onyx feature is a severity-weighted bonus, which is balance, not capability.
- **`Content.Shared/StatusEffectNew/**` and `Content.Server/EntityEffects/Effects/StatusEffects/GenericStatusEffect.cs`** — **P4-D8.** Adding Onyx's `Update` member to the shipped `StatusEffectMetabolismType` enum would fall through `GenericStatusEffect`'s `if/else` chain as a silent no-op.
- **`Content.Server/EntityEffects/Effects/HealthChange.cs`'s universal-modifier bug** — the method builds `damageSpec`, applies the universal reagent modifiers at `:142,149-165`, then passes **`Damage * scale`** at `:169`, discarding them. That is Wolfgate's current live behaviour; fixing it is a balance change unrelated to Wolfmed. HOOK 9 must keep `change = Damage * scale`.
- **`Content.Shared/MedicalScanner/HealthAnalyzerPartMessage`, `HealthAnalyzerUiKey`, and `OnHealthAnalyzerPartSelected`** — the Shitmed selection round-trip is untouched; P4-4 adds **no** new BUI message (§5.2).
- **`Content.Server/Medical/CryoPodSystem.cs`** — its `HealthAnalyzerScannedUserMessage` construction at `:206-221` passes nine positional arguments and keeps compiling because all four new parameters are optional. Do not "tidy" it.
- **Crew monitor and suit sensors** — confirmed: Onyx's only markers in `CrewMonitoringConsoleSystem.cs` and the three `SuitSensors` files are `<Onyx-CommandTrackingImplant>`, an unrelated command-staff filter. Nothing to port; they stay on PLAN §3's "explicitly NOT touched" list.
- **`Content.Server/HealthExaminable/**`** — confirmed: Onyx has **no** organ-damage examine line anywhere (`git grep` over `_Onyx/HealthExaminable` and `HealthExaminable` returns no organ reference, `OrganHealthSystem.cs` has no `Examine` reference, and the Onyx health-examinable ftl has no organ key). P4-4's conditional ("only if Onyx has it") resolves to **NO**. An organ examine line would be a Wolfgate invention; §8.4 decision 6 offers it with a default of no.
- **Onyx's `Surgery` guideEntry** — P4-D15, id collision plus D7.
- **`Content.Shared/_Onyx/Damage/GroupHealSpecifier.cs`** — P4-D12.
- **`Content.Client/_Onyx/Targeting/UI/HealthAnalyzerStatusDoll.*` and `/Textures/_Onyx/Interface/Targeting/**`** — WG's doll already serves (§2.10).
- Everything on PLAN §3's, PLAN2 §3's and PLAN3 §3's "Explicitly NOT touched" lists.

**Upstream-file count after phase 4:** 27 (after phase 3, per `WOLFMED_STATUS.md:118`) + `HealthChange.cs`, `EvenHealthChange.cs`, `ExplosionSystem.Processing.cs`, `HealthAnalyzerSystem.cs`, **`HealthAnalyzerScannedUserMessage.cs`**, `SharedSurgerySystem.cs`, `SurgeryWoundedConditionComponent.cs`, `HealthAnalyzerWindow.xaml`, `HealthAnalyzerWindow.xaml.cs`, `healing.yml`, `firstaidkits.yml`, `_Shitmed/…/surgeries.yml`, `_Shitmed/…/surgery_steps.yml`, `_Goobstation/Reagents/medicine.yml`, `Reagents/medicine.yml`, `Reagents/narcotics.yml`, `alcohol.yml`, `Guidebook/medical.yml`, `health-analyzer-component.ftl` = **46** (19 files). **Corrected from 45 in this revision (CRITIQUE4 M1)** — `HealthAnalyzerScannedUserMessage.cs` was required by WP12-6 but absent from §3 and from the arithmetic; it is now EXT 2. Note `SharedSurgerySystem.cs` carries **two** hooks and is a first-time Wolfmed touch — it had no `// WOLFGATE` marker before this phase. **No chem-dispenser file is on this list** (CRITIQUE4 M2 — WP12-2 item 8 is deleted).

---
## 4. Work packages

Build order — **packages run SEQUENTIALLY in the one worktree**:

```
WP12-0  Medical patch (P4-2a)                    6 files   ← warm-up: zero wound coupling, zero upstream edits
   │
WP12-1  Reagent effect classes + HOOK 9 (P4-1a)  8 files   [owns HealthChange.cs, EvenHealthChange.cs]
   │
WP12-2  Reagent content, Tier A (P4-1b)          9 files   [owns every Reagents/*.yml and Recipes/*.yml]
   │
WP12-3  Tourniquet (P4-2b)                       6 files   [owns healing.yml, firstaidkits.yml]
   │
WP12-4  Wound surgery C# + HOOK 24/25 (P4-3a)    5 files   [owns SharedSurgerySystem.cs, SurgeryWoundedConditionComponent.cs]
   │
WP12-5  Wound surgery content (P4-3b)            6 files   [owns _Shitmed/…/surgeries.yml + surgery_steps.yml]
   │
WP12-6  Analyzer payload + server (P4-4a)        7 files   [owns HealthAnalyzerScannedUserMessage.cs, HealthAnalyzerSystem.cs]
   │
WP12-7  Analyzer client UI + locale (P4-4b)      7 files   [owns HealthAnalyzerWindow.xaml(.cs), health-analyzer-component.ftl]
   │
WP12-8  Explosion amputation (P4-6)              4 files   [owns WoundDamageRoutingSystem.cs, ExplosionSystem.Processing.cs]
   │
WP12-9  Tests (P4-8)                             8 files   [owns every test file]
   │
WP12-10 Guidebook + docs reconcile (P4-7/P4-9)   7 files   [sole owner of Docs/Wolfmed/ and Guidebook/medical.yml at this stage]
```

**Serialisation rule 1 — one owner per shared file.** The bracketed ownerships above are exclusive. In
particular: `Resources/Prototypes/_Shitmed/Entities/Surgery/surgeries.yml` and `surgery_steps.yml` belong to
**WP12-5 only** (both PROTO F and PROTO G land there); `Content.Shared/_Shitmed/Surgery/SharedSurgerySystem.cs`
belongs to **WP12-4 only** (both HOOK 24 and HOOK 25); `Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs`
belongs to **WP12-8 only**; every `Content.IntegrationTests` file belongs to **WP12-9 only**, including the
existing `WoundBleedingTest.cs`, `AmputationConsequenceTest.cs` and `WolfmedReattachTest.cs`.

**Serialisation rule 2 — medkit and reagent YAML.** `Catalog/Fills/Items/firstaidkits.yml` is WP12-3's
(the Tourniquet line). If WP12-2 wants a Tier-A reagent in a medkit it must hand the line to WP12-3's owner
instead of editing the file; the recommended answer is **no medkit placement for the five new reagents** —
they are chem-dispensable and the phase already adds one medkit item.

**Serialisation rule 3 — `[TestPrototypes]` ids are a global pool** across the whole suite. Taken today:
`WoundFoundation*`, `WoundBleedingBody*`, `WoundHealingBody*`, `WoundScarBody*`, `WoundFractureBody*`,
`WoundFractureArmor`, `WoundFractureHandsBody*`, `WoundFractureHeldItem`, `WolfmedBridgeBody*`,
`WolfmedBridgeArmor`, `WolfmedPainShockBody`, `WolfmedHighPainThresholdBody`, `WolfmedAmputationBody*`,
`WolfmedAmputationOverflow*`, `AmputationConsequenceTest*`, `WolfmedOrganTest*`, `WolfmedOrganControlBody`,
`WolfmedOrganFunc*`, `WolfmedReattachBody*`. Phase 4 uses the `WolfmedSurgery*`, `WolfmedTreatment*`,
`WolfmedPatch*` and `WolfmedAnalyzer*` prefixes; **re-grep before adding**, because packages land in sequence.

**Serialisation rule 4 — the manifest.** Each WP appends its own `### WP12-N` rows and deviations directly to
`Docs/Wolfmed/WOLFMED_MANIFEST.md` (ground rule 6). WP12-10 **reconciles** (amends the stale rows in §7.1,
dedupes, writes `WOLFMED_PLAN4.md` and updates `WOLFMED_STATUS.md`); it does not re-append.

---

### WP12-0 — Medical patch (P4-2a)

**Goal:** the lowest-risk item in the phase lands first, so the build and the manifest cadence are warm before
anything touches a wound path.

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | `Content.Server/_Onyx/Medical/MedicalPatchComponent.cs` | same path | **new (vendored), verbatim, 0 edits** |
| 2 | `Content.Server/_Onyx/Medical/MedicalPatchSystem.cs` | same path | **new (vendored), verbatim, 0 edits** |
| 3 | `Resources/Prototypes/_Onyx/Entities/Objects/Specific/Medical/medical_patch.yml` | same path | **new, verbatim** — `BaseMedicalPatch` (abstract), `MedicalPatchMakeshift` (the one spawnable item, `updateTime: 2`, `singleUse: true`), `UsedMedicalPatch`, `UsedMedicalPatchMakeshift`, plus the two construction graphs (1 Cloth / 4 WebSilk, 5 s do-after) |
| 4 | `Resources/Prototypes/_Onyx/Tags/medical_patch.yml` | same path | **new, verbatim** — one tag, `MedicalPatch`; grepped free in WG |
| 5 | `Resources/Textures/_Onyx/Objects/Medical/medical_patch.rsi/*` (**22 files: 21 PNG + `meta.json`**) | same path | **new, verbatim.** `meta.json` declares `"license": "CC-BY-SA-3.0"`, `"copyright": "@jorgun  inspired by Studenterhue of Goonstation"` — **record that artist/licence pair in the manifest exactly as-is**; it is new to Wolfmed. **File count corrected in this revision:** `git ls-tree -r --name-only HEAD` on the pin returns **22** entries — `GenericPatch.png`, `GenericPatch-1–9.png`, `GenericPatchBorder.png`, `GenericPatchSmall.png`, `GenericPatchSmall-1–5.png`, `GenericPatchSmallCornerLayer.png`, `GenericPatchSmallUsed.png`, `MakeshiftPatch.png`, `MakeshiftPatchUsed.png`, `meta.json`. Both this plan's earlier “18” and CRITIQUE4 m3's “24” are wrong |
| 6 | `Resources/Locale/en-US/_Onyx/medical/medical_patch.ftl` | same path | **new, verbatim** — 8 keys |

**Zero upstream edits. Zero fill/vending/cargo/loadout placement** (P4-D13). Do **not** add `MedicalPatch` to
any storage whitelist — Wolfgate has no confirmed surgery-kit equivalent with Onyx's whitelist, and a backpack
or pocket already holds it.

**Subscription pairs this WP registers:** `<MedicalPatchComponent, EntityStuckEvent>` and
`<MedicalPatchComponent, EntityUnstuckEvent>` — both free by construction (brand-new component, §5.1).

**Build checkpoint:** three builds green; Release YAML lint green (this WP adds three new YAML files and a new
RSI — the lint is the only thing that catches a malformed `meta.json`); server starts with no
`Duplicate Subscriptions` throw; `DockTest` green.

---

### WP12-1 — Reagent effect classes and HOOK 9 (P4-1a)

**Goal:** the four `!type:` names Onyx's YAML uses exist and work, and healing reagents carry a
treatment-capability scope.

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | `Content.Shared/_Onyx/Wounds/SuppressPainEntityEffect.cs` | `Content.Shared/_WF/Wolfmed/EntityEffects/SuppressPain.cs` | **new, re-authored old-style** (§2.1), ~35 lines |
| 2 | `Content.Shared/_Onyx/Wounds/ReagentTreatmentEffects.cs:27-54` | `Content.Shared/_WF/Wolfmed/EntityEffects/MendFractures.cs` | **new, re-authored**, ~55 lines |
| 3 | `Content.Shared/_Onyx/Chemistry/TakeStaminaDamageEntityEffectSystem.cs` | `Content.Shared/_WF/Wolfmed/EntityEffects/TakeStaminaDamage.cs` | **new, re-authored**, ~35 lines. **P4-D5 divergence: `Immediate` is honoured** |
| 4 | `Content.Shared/_Onyx/Chemistry/StaminaDamageCondition.cs` | `Content.Shared/_WF/Wolfmed/EntityEffects/StaminaDamageCondition.cs` | **new, re-authored**, ~28 lines |
| 5 | `Resources/Locale/en-US/_Onyx/guidebook/entity-effects.ftl` | same path | **new** — 3 renamed keys + 4 `fracture-grade-*` + 1 new `take-stamina-damage` key (§2.12) |
| 6 | — | `Content.Server/EntityEffects/Effects/HealthChange.cs` | **modified — HOOK 9(a)**, ~16 lines |
| 7 | — | `Content.Server/EntityEffects/Effects/EvenHealthChange.cs` | **modified — HOOK 9(b)**, ~15 lines, **plus `using System.Linq;`** |
| 8 | — | `Docs/Wolfmed/WOLFMED_MANIFEST.md` | append `### WP12-1` |

**Exact edits:** §3.1 HOOK 9 rows and §2.1 for the four class bodies. Four things that will bite:

* **T2 — `ReagentEffectGuidebookText` is `abstract`, not virtual** (`EntityEffect.cs:33`). All four classes must
  implement it or the build fails. `TakeStaminaDamage` has no Onyx text — the new locale key exists for it.
* **T5 — `EvenHealthChange.cs` has no `using System.Linq;`** (its usings end at `:8`). HOOK 9(b)'s `.Any(...)` needs it.
* **T4 — do not "fix" `HealthChange`'s discarded universal modifiers** (§3.4). `change = Damage * scale`, not `damageSpec * scale`.
* **T1 — the four class names are load-bearing** (`!type:` resolves by bare `Type.Name`). Renaming one produces a
  prototype-load failure at server start, not a compile error.

**Subscription pairs this WP registers:** **none.** These are `[ImplicitDataDefinitionForInheritors]` data
classes dispatched by a direct virtual call from `MetabolizerSystem.cs:218`; HOOK 9 mutates two existing
classes. **This WP also registers no component.**

**Build checkpoint:** three builds green; Release lint green; server starts clean; the whole
`_Onyx.Wounds|_Onyx.Body|Wolfmed` filter still green (HOOK 9 must not change any existing assertion — it is
inert with one profile, which is exactly what T-REAGENT-CAP-NO in WP12-9 will prove).

---

### WP12-2 — Reagent content, Tier A (P4-1b)

**Goal:** a painkiller ladder and a fracture medicine exist in the world.

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | `Resources/Prototypes/Reagents/Consumable/Drink/alcohol.yml:115-123` | `Resources/Prototypes/Reagents/Consumable/Drink/alcohol.yml` | **modified — PROTO K**, Cognac |
| 2 | `Resources/Prototypes/Reagents/medicine.yml:153-159` | `Resources/Prototypes/Reagents/medicine.yml` | **modified — PROTO I**, Bicaridine (`Medicine:` group) |
| 3 | `Resources/Prototypes/Reagents/narcotics.yml:50-56, :632-638` | `Resources/Prototypes/Reagents/narcotics.yml` | **modified — PROTO J**, Desoxyephedrine (`Narcotic:` group) + Happiness |
| 4 | `_Onyx/Reagents/Medicine/first_aid.yml:29-33` | `Resources/Prototypes/_Goobstation/Reagents/medicine.yml` | **modified — PROTO H**, Stasizium's `MendFractures` |
| 5 | `_Onyx/Reagents/Medicine/medicine.yml` (rows 13, 16, 17, 21, 22) | `Resources/Prototypes/_Onyx/Reagents/Medicine/medicine.yml` | **new** — Osteogen, Ibuprofen, Ketorolac, Tramadol, Oxycodone, ~140 lines |
| 6 | `_Onyx/Recipes/Reactions/medicine.yml` | `Resources/Prototypes/_Onyx/Recipes/Reactions/medicine.yml` | **new** — 5 recipes, ~60 lines |
| 7 | — | `Resources/Locale/en-US/_Onyx/reagents/medicine.ftl` | **new** — 10 keys (name + desc × 5) |
| 8 | — | `Docs/Wolfmed/WOLFMED_MANIFEST.md` | append `### WP12-2` |
| 9 | *(Tier B, optional)* | `…/medicine.yml` + recipes + locale | Probital + Mitogen, if the package is otherwise green |

**Deleted in this revision — the former item 8, “list the five in the chem-dispenser inventory” (CRITIQUE4 M2).** It is not implementable as written and is not needed. `Resources/Prototypes/Catalog/ReagentDispensers/chemical.yml:1-22` is `ChemDispenserStandardInventory`, whose `inventory:` list holds **jug entity ids for base elements** (`JugAluminium`, `ReinforcedJugCarbon # Frontier`, `JugFluorine`, `JugIodine`, `JugIron`, `JugMercury`, `JugRadium`, `JugSodium`, `JugSulfur`, …), not reagent ids — `grep -c Bicaridine` on that file returns **0**, so no medicine is dispensable there at all. Listing the five would need five new `Jug*` entity prototypes plus sprites, a content change with no Onyx precedent. It is also redundant: item 6 ships five reactions whose precursors all exist in WG (re-verified in this revision — `Benzene, Acetone, Inaprovaline, Phosphorus, Milk, Charcoal, Fluorine, Epinephrine, Carbon, Ethanol, Bicaridine` 1 hit each, `Plasma` 2 (the gas and the material), `Heroin` **0** — which is why P4-D2 re-authors Oxycodone). **Record in the manifest that the five Tier-A reagents are reaction-only (chemist-craftable), matching Onyx.**

**The `!type:` translation table — Onyx reagent YAML does not port verbatim.** This is the single biggest
source of silent breakage in this WP; every tag in a copied block must be checked against it:

| Onyx `!type:` | Wolfgate |
|---|---|
| **`metabolisms:` group header — `Bloodstream:`** | → **`Medicine:`**. **Read this row first (CRITIQUE4 B2).** Wolfgate has **no** `Bloodstream` metabolism group: the complete set is `Poison, Medicine, Narcotic, Alcohol, Food, Drink, Gas, PlantMetabolisms` (`Resources/Prototypes/Chemistry/metabolism_groups.yml:3,7,11,15,19,23,28,33`) plus `Cryogenic` (`_NF/Chemistry/metabolism_groups.yml:3`), and `grep -rn "^    Bloodstream:$" Resources/Prototypes` returns **0 hits**. All five new Tier-A reagents are `group: Medicine` in Onyx and their effects belong in the liver's `Medicine` pass. The key is `ProtoId`-validated (`Content.Shared/Chemistry/Reagent/ReagentPrototype.cs:155`, `FrozenDictionary<ProtoId<MetabolismGroupPrototype>, ReagentEffectsEntry>? Metabolisms`), so a copied `Bloodstream:` is a **hard prototype-load / Release-lint failure for the whole file**, not a silent one. Applies to Tier B (`Probital`, `Mitogen`) too if it is taken, and Onyx's `Digestion:` (used by Cognac and Mitotrophin) does not exist either — see PROTO K |
| `HealthChange`, `EvenHealthChange`, `ModifyBloodLevel`, `ReduceRotting`, `AdjustTemperature`, `AdjustReagent`, `MobStateCondition`, `GenericStatusEffect`, `Jitter`, `Drunk`, `PopupMessage`, `Emote` | **SAME** |
| `PlantAdjustWeeds`, `PlantAdjustHealth` (Ibuprofen's top-level `plantMetabolism:` block, `ONYX medicine.yml:198-202`) | **SAME** — `Content.Server/EntityEffects/Effects/PlantMetabolism/PlantAdjustWeeds.cs:7` and `PlantAdjustHealth.cs:6`, and `ReagentPrototype` has the field (`:163-164`, `[DataField("plantMetabolism", serverOnly: true)]`). Added in this revision (CRITIQUE4 m6) because the table is billed as the checklist for **every** tag in a copied block |
| `ModifyBleed` | → **`ModifyBleedAmount`** |
| `ReagentCondition` | → **`ReagentThreshold`** (same `min`/`max`/`reagent` fields) |
| `MovementSpeedModifier` | → **`MovespeedModifier`**, and `time:` → `statusLifetime:` |
| `Satiate` + `satiationType:` | → **`SatiateThirst`** / **`SatiateHunger`** (split) |
| `Vomit` | → **`ChemVomit`** |
| `TemperatureCondition` | → **`Temperature`** (`EffectConditions/BodyTemperature.cs`) |
| `ModifyParalysis` | → **`Paralyze`** (re-check field names at implementation) |
| `Flammable` | → **`FlammableReaction`** |
| `PressureThreshold`, `TypedDamageThreshold`, `ModifyStatusEffect`, `ImmunityModifier`, `DiseaseProgressChange`, `ChemCureDnaDisease` | **MISSING** — every reagent needing one is out of scope (P4-D2) |

**Reaction chain, verified dependencies:** `Osteogen` = Bicaridine + Milk + Phosphorus (all three present in
WG) — fully portable as-is. `Tramadol` = Acetone + Inaprovaline + Ethanol (all present). `Ibuprofen` =
Charcoal + Benzene + Fluorine — **check Benzene exists in WG before writing the recipe**. `Ketorolac` =
Ibuprofen + Tramadol + Carbon + Plasma (chain dependency — order the file so both precursors exist).
**`Oxycodone` = Tramadol + Heroin + Epinephrine + Plasma, and `Heroin` is in `_Onyx/Reagents/Narcotics/opioids.yml`,
outside P4-1's scope** — **re-author the recipe without `Heroin`** as a marked `# WOLFGATE (P4-D2)` deviation
(the obvious substitution is Tramadol + Acetone + Plasma or Tramadol + Ethanol + Plasma; pick one, verify the
reagents exist, and record it). Do **not** widen the scope to pull `Heroin` in.

**Subscription pairs / components:** **none.**

**Build checkpoint:** three builds green; **Release YAML lint green is the real gate here** — a mistranslated
`!type:` is a prototype-load failure, and this WP is almost entirely YAML; server starts clean; the existing
suite still green.

---

### WP12-3 — Tourniquet (P4-2b)

**Goal:** the tourniquet Wolfgate already ships stops bleeding on the limb the medic aimed at.

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | `Content.Shared/_Onyx/Medical/Tourniquet/TourniquetComponent.cs` | same path | **new (vendored), verbatim** |
| 2 | `Content.Shared/_Onyx/Medical/Tourniquet/TourniquetSystem.cs` | **`Content.Server/_Onyx/Medical/Tourniquet/TourniquetSystem.cs`** | **new (vendored, relocated), 3 `// WOLFGATE` edits** (§2.2) |
| 3 | `Resources/Prototypes/Entities/Objects/Specific/Medical/healing.yml:261-289` | `Resources/Prototypes/Entities/Objects/Specific/Medical/healing.yml` | **modified — PROTO D**, in-place block swap on the existing `Tourniquet` entity |
| 4 | `Resources/Prototypes/Catalog/Fills/Items/firstaidkits.yml` | same path | **modified — PROTO E**, one line on `MedkitAdvancedFilled` |
| 5 | `Resources/Locale/en-US/_Onyx/medical/tourniquet.ftl` | same path | **new, verbatim** — 3 keys |
| 6 | — | `Docs/Wolfmed/WOLFMED_MANIFEST.md` | append `### WP12-3` |

**Zero new assets.** Both sound files (`/Audio/Items/Medical/brutepack_{begin,end}.ogg`) and both sprite states
(`tourniquet`, `tourniquet-inhand-left/right` in `Textures/Objects/Specific/Medical/medical.rsi`) already exist.
**Zero touched fill/vending/spawner files beyond PROTO E** — the id is unchanged, so the eight existing
references keep working.

**Traps:** do **not** add the `Tourniquet` tag (P4-D9 — no tag prototype in WG, Release lint failure); do **not**
create a second `id: Tourniquet` entity; do **not** leave `TourniquetSystem.cs` in `Content.Shared` (it will not
compile — `WoundBleedingSystem` is server-assembly).

**Subscription pairs this WP registers:** `<TourniquetComponent, UseInHandEvent>`,
`<TourniquetComponent, AfterInteractEvent>`, `<TourniquetComponent, TourniquetDoAfterEvent>` — all free
(brand-new component, §5.1).

**Build checkpoint:** three builds green; Release lint green; server starts clean; **plus a D2 spot check that
costs one minute — confirm a non-wound-host (`MobMonkey`, or a Protogen) simply gets no tourniquet effect
rather than throwing**, which is P4-D11's accepted loss and not a crash.

---

### WP12-4 — Wound surgery C# and HOOK 24/25 (P4-3a)

**Goal:** the surgery layer can read and change wound state, and an untreated stump refuses re-attachment.

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | `Content.Shared/_Onyx/Medical/Surgery/WoundSurgeryComponents.cs` + `SurgeryEffects.cs:54` | `Content.Shared/_WF/Wolfmed/Surgery/WolfmedSurgeryComponents.cs` | **new**, 10 components + 1 enum, ~130 lines (§2.4) |
| 2 | `Content.Server/_Onyx/Medical/Surgery/WoundSurgerySystem.cs:167-231` | `Content.Shared/_WF/Wolfmed/Surgery/WolfmedSurgeryConditionSystem.cs` | **new**, S1–S8 + `FindWound`/`GetGroupSeverity`/`TryFindOrgan`, ~210 lines (§2.5) |
| 3 | `Content.Server/_Onyx/Medical/Surgery/WoundSurgerySystem.cs` + `SurgerySystem.WoundEffects.cs` | `Content.Server/_WF/Wolfmed/Surgery/WolfmedWoundSurgerySystem.cs` | **new**, V1–V7, ~140 lines (§2.6) |
| 4 | — | `Content.Shared/_WF/Wolfmed/Surgery/SharedSurgerySystem.Wolfmed.cs` | **new**, HOOK 24 + HOOK 25 bodies + 2 `[Dependency]`s, ~45 lines (§2.7) |
| 5 | — | `Content.Shared/_Shitmed/Surgery/Conditions/SurgeryWoundedConditionComponent.cs` | **modified — EXT 1**, +6 lines |
| 6 | — | `Content.Shared/_Shitmed/Surgery/SharedSurgerySystem.cs` | **modified — HOOK 24 (`:127`) + HOOK 25 (`:263`)**, +4 lines |
| 7 | — | `Docs/Wolfmed/WOLFMED_MANIFEST.md` | append `### WP12-4` |

**Order matters:** #1 before #2/#3 (`CS0246` otherwise); #4 before #6 (the hook calls into the partial).

**The split is mandatory, not stylistic** (§2.5/§2.6): `SurgeryValidEvent` and `SurgeryStepCompleteCheckEvent`
handlers in `Content.Shared`, `SurgeryStepEvent` handlers in `Content.Server`. Getting it backwards produces
either a client-side compile failure (`OrganHealthSystem` / `WoundBleedingSystem` are server assemblies) or a
client that highlights the wrong step forever.

**Four behavioural traps, each of which ships silently if ignored:**

1. **`SurgeryValidEvent` on a *step* prototype does not hide the surgery.** `SurgerySystem.RefreshUI`
   (`SurgerySystem.cs:61-88`) raises it on the **surgery** singleton only; `IsSurgeryValid`
   (`SharedSurgerySystem.cs:315-347`) raises it on both at completion time. Onyx attaches
   `SurgeryHasWoundCondition` to its clamp *step*; copying that placement into Wolfgate produces a condition
   that only bites at do-after completion. **Visibility conditions go on the surgery.**
2. **`args.Repeat` re-runs `IsSurgeryValid` on every repeat** (`SharedSurgerySystem.cs:94`). A tight condition
   like `bleeding: true` on the *surgery* aborts the repeat loop the instant the last bleeder is clamped and
   logs `"tried to start invalid surgery"` at `:98`. Put the tight condition on the **step**, the loose one on
   the **surgery**, or accept the warning.
3. **Do not build anything on `SurgeryCompletedEvent`.** It is an empty `record struct` that nothing raises;
   its only subscriber is commented out at `SharedSurgerySystem.cs:76`.
4. **`OnFractureCheck` must honour `ReductionMinimumGrade`** (P4-D20, §2.5) or the mend surgery stalls forever
   on the commonest fracture grade.

**Subscription pairs this WP registers:** S1–S8 (shared) and V1–V7 (server) — 15 pairs, all audited free in
§5.1. **10 new registered component names**, all grepped free in §5.3.

**Build checkpoint:** three builds green; **server starts with no `Duplicate Subscriptions` throw is the gate
for this WP specifically** — it adds more directed subscriptions than the rest of the phase combined; existing
suite green (HOOK 24 and HOOK 25 must be provably inert until WP12-5 lands the prototypes, since no shipped
`SurgeryWoundedCondition` declares a bound and no shipped surgery is reachable on a consequence stump).

---

### WP12-5 — Wound surgery content (P4-3b)

**Goal:** six wound surgeries plus seven organ-heal surgeries appear in the BUI and work.

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | `_Onyx/Entities/Surgery/surgery_steps.yml` (the wound/organ steps) | `Resources/Prototypes/_WF/Wolfmed/Surgery/surgery_steps.yml` | **new**, 12 steps + 1 abstract base, ~190 lines |
| 2 | `_Onyx/Entities/Surgery/surgeries.yml:908-1200` | `Resources/Prototypes/_WF/Wolfmed/Surgery/surgeries.yml` | **new**, 13 surgeries, ~160 lines |
| 3 | — | `Resources/Prototypes/_Shitmed/Entities/Surgery/surgeries.yml` | **modified — PROTO F**, +2 lines |
| 4 | — | `Resources/Prototypes/_Shitmed/Entities/Surgery/surgery_steps.yml` | **modified — PROTO G**, 4 marked component additions on `:9`, `:31`, `:168`, `:359` (P4-D21). **`:303` `SurgeryStepCarefulIncisionScalpel` is NOT touched** |
| 5 | — | `Resources/Locale/en-US/_WF/wolfmed/surgery-popup.ftl` | **new**, 12 keys |
| 6 | — | `Docs/Wolfmed/WOLFMED_MANIFEST.md` | append `### WP12-5` |

**The thirteen surgeries.** Every step and surgery id was grepped free in this pass (§5.3). All reuse
Wolfgate's existing tools (`Hemostat`, `Tending`, `BoneGel`, `BoneSetter`) and existing surgeries
(`SurgeryOpenIncision`, `SurgeryOpenRibcage`) and steps (`SurgeryStepSealTendWound`,
`SurgeryStepRepairBruteTissue`/`BurnTissue`, `SurgeryStepSealOrganWound`) — **no new tool prototype and no new
sprite is needed anywhere in this package.**

```yaml
# surgeries.yml, abridged - see §5 of surgery.md for the full listing this is derived from.
- type: entity
  parent: SurgeryBase
  id: SurgeryStopBleeding            # no incision: a suture on an open bleeder
  components:
  - type: Surgery
    steps: [ SurgeryStepSutureBleeding ]
  - type: WolfmedSurgeryWoundCondition
    state: Open
    bleeding: true

- type: entity
  parent: SurgeryBase
  id: SurgeryStopInternalBleeding
  components:
  - type: Surgery
    requirement: SurgeryOpenIncision
    steps: [ SurgeryStepStopInternalBleeding, SurgeryStepSealTendWound ]
  - type: WolfmedSurgeryWoundCondition
    internalBleeding: true

- type: entity
  parent: SurgeryBase
  id: SurgeryMendFracture
  components:
  - type: Surgery
    requirement: SurgeryOpenIncision
    steps: [ SurgeryStepSetBone, SurgeryStepMendFracture, SurgeryStepSealTendWound ]
  - type: WolfmedSurgeryFractureCondition
    minGrade: Hairline

- type: entity
  parent: SurgeryBase
  id: SurgeryHealAmputationConsequence
  components:
  - type: Surgery
    requirement: SurgeryOpenIncision
    steps: [ SurgeryStepHealAmputationConsequence, SurgeryStepSealTendWound ]
  - type: WolfmedSurgeryWoundCondition
    woundPrototype: AmputationConsequenceWound

- type: entity
  parent: SurgeryBase
  id: SurgeryTendWoundsBruteDeep
  components:
  - type: Surgery
    requirement: SurgeryOpenIncision
    steps: [ SurgeryStepRepairBruteTissue, SurgeryStepSealTendWound ]
  - type: SurgeryWoundedCondition        # Wolfgate's own, extended by EXT 1
    woundGroup: Brute
    minWoundSeverity: 100
# ...SurgeryTendWoundsBurnDeep identically with woundGroup: Burn and SurgeryStepRepairBurnTissue.
# ...SurgeryHeal{Heart,Lungs,Liver,Stomach,Kidneys} on requirement: SurgeryOpenRibcage + SurgeryPartCondition part: Torso
#    + WolfmedSurgeryOrganDamagedCondition slot: <heart|lungs|liver|stomach|kidneys>;
#    steps: [ SurgeryStepHeal<Organ>, SurgeryStepSealOrganWound ].
# ...SurgeryHeal{Brain,Eyes} on requirement: SurgeryOpenIncision + part: Head + slot: <brain|eyes>;
#    steps: [ SurgeryStepSawBones, SurgeryStepHeal<Organ>, SurgeryStepSealOrganWound ]  <- note the saw.
```

**Every one of the 26 new prototypes carries `name:` and `categories: [ HideSpawnMenu ]` (CRITIQUE4 M6).**
`SurgeryBase` (`surgeries.yml:1-3`) and `SurgeryStepBase` (`surgery_steps.yml:1-5`) set `categories` but **no**
`name:`, and every shipped child re-states both (`SurgeryOpenIncision` → `name: Open Incision` at
`surgeries.yml:8` and its own `categories:` at `:9`; `SurgeryStepOpenIncisionScalpel` → `name: Cut with a
scalpel` at `surgery_steps.yml:10`, `categories:` at `:11`). An unnamed prototype renders with an empty label
in the surgery BUI's surgery list and step rows — shipped-broken UI, not a crash, and invisible to the lint.
Entity **names** are inline in Wolfgate YAML (§2.12), so this is not covered by `surgery-popup.ftl`.
`T-SURGERY-PROTOTYPE-SANITY` asserts it (§6.2).

**The organ-heal bone gate, resolved (CRITIQUE4 M7 — accepted for the head, rejected for the torso, with
evidence).** Shitmed's own organ surgeries all saw bone first: `SurgeryRemoveBrain` (`surgeries.yml:329-345`),
`SurgeryInsertBrain` (`:366-382`), `SurgeryRemoveEyes` (`:558-574`) and `SurgeryInsertEyes` (`:576-594`) are
all `requirement: SurgeryOpenIncision` + `steps: [ SurgeryStepSawBones, … ]`, and `SurgeryRemoveHeart`
(`:398-414`) / `SurgeryInsertHeart` (`:416-437`) do the same under `SurgeryOpenRibcage`. Two facts decide it:

* **Torso: already gated, no change needed.** `SurgeryOpenRibcage` (`:34-45`) is itself
  `requirement: SurgeryOpenIncision` + `steps: [ SurgeryStepSawBones, SurgeryStepPriseOpenBones ]`, so the saw
  has already run before any `requirement: SurgeryOpenRibcage` surgery is reachable. The reason Shitmed's torso
  `Remove*`/`Insert*` can list `SurgeryStepSawBones` again harmlessly is `OnToolCheck`
  (`SharedSurgerySystem.Steps.cs:185-198`): a step whose `add:` components are already present on the part is
  **already complete**, and `SurgeryStepSawBones` is `add: [ RibcageSawed ]`. A repeated saw is a zero-second
  no-op, not a gate. The five torso heals therefore keep `steps: [ SurgeryStepHeal<Organ>,
  SurgeryStepSealOrganWound ]` and §8.2's ~20 s stands.
* **Head: genuinely missing — add it.** `SurgeryHealBrain` and `SurgeryHealEyes` are
  `requirement: SurgeryOpenIncision`, which does **not** saw (`:7-16`: `[ OpenIncisionScalpel, RetractSkin,
  ClampBleeders ]`). Without the step a surgeon would repair a brain with scalpel + retractor + hemostat +
  cautery while *removing* that same brain needs a circular saw. **Prefix `SurgeryStepSawBones` to both**
  (`duration: 4`, tool `BoneSaw`, already in a standard kit): §8.2's head-organ row becomes ~24 s.
* **`SurgeryStepClampInternalBleeders` is deliberately not added**, and this is recorded rather than silent:
  Shitmed's own `SurgeryInsert*` surgeries omit it too (`SurgeryInsertHeart`/`InsertBrain`/`InsertEyes` are
  `[ SawBones, Insert…, SealOrganWound ]`), so heal-without-clamp matches the closest Shitmed precedent. Record
  it in §7.3 (deviation 25) with the reason: healing does not breach the organ, only the cavity.

**`SurgeryStepSealOrganWound` is safe to reuse as the terminal step, verified.** It carries
`- type: SurgeryAffixOrganStep` (`surgery_steps.yml`), but both handlers early-return unless the **surgery**
entity carries `SurgeryOrganConditionComponent` with `Reattaching == true` (`SharedSurgerySystem.Steps.cs:587-592`
and `:603-609`). The organ-heal surgeries carry `WolfmedSurgeryOrganDamagedCondition`, not `SurgeryOrganCondition`,
so the affix logic never engages and the step behaves as a plain 2 s cautery with `SurgeryDamageChangeEffect
{ Heat: -5 }`. Do **not** add `SurgeryOrganCondition` to them.

**Organ slot ids, verified against `Resources/Prototypes/Body/Prototypes/human.yml:11-27`:** head → `brain`,
`eyes`; torso → `heart`, `lungs`, `stomach`, `liver`, `kidneys`. **Seven, not Onyx's ten** — Wolfgate has no
Tongue/Ears/Appendix slot in any body graph, and no `Groin` part (D9).

**Organ-heal `amount: 3` on every organ step** (P4-D23), each carrying `# WOLFGATE (P4 balance): Onyx ships 1;
15 repeats of a 2 s step is dead time at Wolfgate's pace. Revert by editing this line.`

**The fracture ladder** (P4-D20): `SurgeryStepSetBone` uses `- type: BoneSetter` (the shipped-but-unused
Shitmed tool, sprite `bonesetter.rsi` state `bonesetter`) with `treatment: Reduced`; `SurgeryStepMendFracture`
uses `- type: BoneGel` (`bone-gel.rsi` state `bone-gel`) with `treatment: Mended`.

**Surgery pain** (P4-D22): `- type: WolfmedSurgeryPainEffect` on the wound and organ steps at Onyx's amounts
(`12` on mend-fracture, `24 / sleepModifier: 0` on the organ-heal base, `5` default elsewhere).

**Two behaviours to record rather than fix:**

* **`OnTendWoundsCheck` is body-scoped, not part-scoped** (`SharedSurgerySystem.Steps.cs:367-372`): the repeat
  loop continues while *any* Brute/Burn remains on the **body**. `SurgeryTendWoundsBruteDeep` inherits that, so
  a medic tending one limb keeps repeating until the whole patient is clean. Pre-existing Shitmed behaviour.
* **`SurgeryRemovePart` still amputates cleanly.** `SurgeryStepRemoveFeature` raises `AmputateAttemptEvent`,
  which goes through `WolfmedBodySystem.TryDetachPart`; P4-3 adds no consequence wound there, so a
  *surgically* removed limb can still be re-attached. Only `AmputationSystem`'s traumatic path sets the block —
  T-SURG-AMPCONSEQ asserts exactly this.

**Subscription pairs / components:** **none** (this WP is pure YAML and locale).

**Build checkpoint:** three builds green; **Release YAML lint green**; server starts clean; then a
**prototype-resolution smoke run** — `T-SURGERY-PROTOTYPE-SANITY` (WP12-9) is the permanent version, but this
WP must at minimum confirm every new `Surgery.steps` entry resolves, because a typo'd step id is a runtime
`GetSingleton` null, not a lint error.

---

### WP12-6 — Analyzer payload and server (P4-4a)

**Goal:** the analyzer message carries wound, organ, chemical and vital-damage data; the builders are callable
from a headless test.

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | `Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs` | same path | **new (vendored), 1 marked `using`** |
| 2 | `Content.Shared/_Onyx/Medical/HealthAnalyzerOrganInfo.cs` | same path | **new (vendored), verbatim** |
| 3 | `Content.Shared/_Onyx/Medical/HealthAnalyzerChemicalInfo.cs` | same path | **new (vendored), verbatim** |
| 4 | — | `Content.Shared/MedicalScanner/HealthAnalyzerScannedUserMessage.cs` | **modified — EXT 2** (§3.2), 4 fields + 4 optional ctor params + 2 marked `using`s, ~10 lines |
| 5 | `Content.Server/Medical/HealthAnalyzerSystem.cs:316-515` | `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs` | **new**, 5 `[Dependency]`s + 4 public builders, ~170 lines (§2.9) |
| 6 | — | `Content.Server/Medical/HealthAnalyzerSystem.cs` | **modified — HOOK 23**, 1 line |
| 7 | — | `Docs/Wolfmed/WOLFMED_MANIFEST.md` | append `### WP12-6` |

The message edit, exactly:

```csharp
    public bool? Uncloneable; // Frontier
    public HealthAnalyzerWoundDiagnostics? WoundDiagnostics;   // WOLFGATE: P4-4
    public List<HealthAnalyzerOrganInfo>?   Organs;            // WOLFGATE: P4-4
    public List<HealthAnalyzerChemicalInfo>? Chemicals;        // WOLFGATE: P4-4
    public FixedPoint2?                     VitalDamage;       // WOLFGATE: P4-4
```

with four **appended optional** constructor parameters after the existing `NetEntity? part = null`. This is
safe because the only other construction site, `CryoPodSystem.cs:206-221`, passes nine positional arguments and
keeps compiling unchanged (re-read here), and NetSerializer emits members in declaration order for both peers
from the same build, so appending is a clean wire change.

**Rejected alternative, recorded so it is not re-litigated:** sending only the selected part's diagnostic over
the existing `HealthAnalyzerPartMessage` round-trip. A triaging medic wants the overview (*which* limb is
bleeding), `BuildWoundDiagnostics` already iterates every part so the server saves nothing, and the whole dict
for a human is ~10 small structs at a 1 Hz update. **Send the whole dict, independent of the part selection.**

**Subscription pairs / components: none.** `UpdateScannedUser` is already reached from the five existing
handlers; the part round-trip is unchanged. See §5.2 for the four pairs this WP must **not** register.

**Build checkpoint:** three builds green (**`Content.Client` is the one that catches a non-NetSerializable
field**); server starts clean; existing suite green; a one-minute manual check that scanning a human still
renders today's window (the client ignores the four new fields until WP12-7).

---

### WP12-7 — Analyzer client UI and locale (P4-4b)

**Goal:** the medic can read all of it.

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | `Content.Client/_Onyx/Medical/HealthAnalyzer/EllipsisLabel.cs` | same path | **new (vendored), verbatim**, 133 lines |
| 2 | `Content.Client/_Onyx/Medical/HealthAnalyzer/HealthAnalyzerControl.xaml` (structure only) | `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml` | **new**, ~40 lines |
| 3 | `…/HealthAnalyzerControl.xaml.cs:252-331, 357-409, 413-520` | `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml.cs` | **new**, ~200 lines |
| 4 | — | `Content.Client/_WF/Wolfmed/Medical/HealthAnalyzerWindow.Wolfmed.cs` | **new**, `PopulateWolfmed` + the `GroupsContainer` swap, ~50 lines |
| 5 | — | `Content.Client/HealthAnalyzer/UI/HealthAnalyzerWindow.xaml` + `.xaml.cs` | **modified — HOOK 26**, **5 lines total** (§3.1, revised): 3 XAML (`xmlns`, `Name="WolfmedDamageGroupsPanel"` on the `:288` panel, the panel element at `:308`) + 2 code (`PopulateWolfmed(msg);` as the **first** statement of `Populate`, and `WolfmedPanel.Visible = false;` in the early-return block) |
| 6 | — | `Resources/Locale/en-US/_Onyx/medical/health-analyzer-component.ftl` + `_Onyx/targeting/targeting.ftl` + `medical/components/health-analyzer-component.ftl` | **2 new + 1 modified (LOC A)**, 13 + 11 + 19 keys |
| 7 | — | `Docs/Wolfmed/WOLFMED_MANIFEST.md` | append `### WP12-7` |

**Reserved-name trap.** The window derives from `FancyWindow`, whose own scope reserves `WindowTitle`,
`HelpButton`, `CloseButton`, `ContentsContainer`; `WindowHeader`/`TitleLabel` belong to `DefaultWindow` and
would bite a `DefaultWindow`-derived control. The 27 names already used in `HealthAnalyzerWindow.xaml` are
listed in `analyzer.md` §3.2. **Under P4-D25 only `WolfmedPanel` enters that scope** — everything else lives in
the panel's own `[GenerateTypedNameReferences]` scope, which is most of the reason to prefer PARALLEL.

**D2 gate, explicit:** `PopulateWolfmed` must hide the panel outright when `msg.WoundDiagnostics == null &&
msg.Organs == null`, so scanning a non-wound-host renders the window **byte-for-byte as it does today**.

**Subscription pairs / components: none.** No new BUI message (§5.2, §5.3).

**Build checkpoint:** three builds green; **Release lint green** (the FTL files); server + client start clean;
a manual scan of a wounded human and of a non-host, confirming the panel appears for one and is absent for the
other. No sprite-pixel or screenshot test (project memory: prefer logic tests; and the user may be working).

---

### WP12-8 — Explosion amputation (P4-6)

**Goal:** grenades spread their blast across a wound host's limbs and can finish an amputation — and armour
plates keep protecting against explosions while doing it.

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | — | `Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs` | **modified**, 3 sites — the optional `originFlag` on both distributed entry points + the `_routedModifiers` save/restore (§2.11) |
| 2 | — | `Content.Server/_WF/Wolfmed/Explosion/WolfmedExplosionSystem.cs` | **new**, ~35 lines |
| 3 | — | `Content.Server/Explosion/EntitySystems/ExplosionSystem.Processing.cs` | **modified — HOOK 22**, 3 lines (1 `using`, 1 `[Dependency]`, the if/fallback at `:471`) |
| 4 | — | `Docs/Wolfmed/WOLFMED_MANIFEST.md` | append `### WP12-8` |

**Order matters:** #1 before #2 (the new parameter must exist), #2 before #3.

**Zero edits to `ExplosionSystem.CVars.cs`** — the two CVars are read in the `_WF` system, unlike Onyx.
Both already exist unconsumed since phase 1 (`CCVars.Wounds.cs:22-26`: `explosion.damage_variation` 2f,
`explosion.wounding_multiplier` 4f).

**This WP must not disturb** `WoundDamageRoutingSystem`'s two `before: [typeof(SharedArmorPlateSystem)]`
orderings at `:64` and `:66` (D23).

**Subscription pairs this WP registers:** **none.** `WolfmedExplosionSystem` has no `Initialize` subscription
beyond two `Subs.CVar` registrations, which are not directed subscriptions.

**Build checkpoint:** three builds green; server starts clean; **T-EXPLOSION-PLATE and T-AMP-EXPLOSION both
green is the gate** (T-AMP-EXPLOSION is phase 3's existing test and must stay green with the new optional
parameter defaulting to `null`); plus a D2 check — a **non**-wound-host takes flat vanilla explosion damage
with plate protection unchanged.

---

### WP12-9 — Tests (P4-8)

**Goal:** every mechanic phase 4 adds has a headless assertion, and the two phase-3 skips are restored.

| # | File | Status |
|---|---|---|
| 1 | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedMedicalPatchTest.cs` | **new** — T-PATCH |
| 2 | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedReagentTreatmentTest.cs` | **new** — T-REAGENT-CAP-YES/NO, T-REAGENT-SUPPRESS, T-REAGENT-MEND, T-REAGENT-STAM, T-REAGENT-SYSTEMIC-BYPASS, **T-REAGENT-PROTOTYPE-SANITY** (new in this revision — CRITIQUE4 B2/M4) |
| 3 | `Content.IntegrationTests/Tests/_Onyx/Wounds/WoundBleedingTest.cs` | **modified** — restore `TourniquetStopsOnlySelectedPartTest` (T-TOURNIQUET), replacing the skip note |
| 4 | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedWoundSurgeryTest.cs` | **new** — T-SURG-BLEED, T-SURG-FRACTURE, T-SURG-INTERNAL, T-SURG-AMPCONSEQ, T-SURG-ORGAN, T-SURG-WINDOW, T-SURG-SCAR, T-SURG-PAIN |
| 5 | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedReattachTest.cs` | **modified** — T-REATTACH-BLOCKED alongside the existing `ReattachedPartRejoinsWoundTrackingTest` |
| 6 | `Content.IntegrationTests/Tests/_Onyx/Wounds/AmputationConsequenceTest.cs` | **modified** — restore `SurgicalHealRemovesConsequenceAndUnblocksTest`, update the `<remarks>` skip block at `:20-33` |
| 7 | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAnalyzerTest.cs` | **new** — T-AN-GATE, T-AN-FINDINGS, T-AN-CLEARS, T-AN-CLOT, T-AN-PAIN, T-AN-ORGANS, T-AN-CHEM, T-AN-VITAL |
| 8 | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedExplosionTest.cs` | **new** — T-EXPLOSION-PLATE, **T-EXPLOSION-WRAPPER** (new in this revision — CRITIQUE4 M9), plus `SurgeryStepSequencePrototypeTest`'s Wolfgate substitute T-SURGERY-PROTOTYPE-SANITY (co-located; it needs no mob) |

**Test count: 30** (2 restored skips, 28 new) — up from 28 rows in the pre-revision draft, which its prose miscounted as 27, which added no
coverage for the phase's own two highest-rated silent-failure risks (§8.5 risks 4 and 5) and none for HOOK 22
itself. Full setup and derivations in §6. **This WP is the sole owner of every file under
`Content.IntegrationTests`** — if an earlier WP wants a test it hands the assertion here.

**Build checkpoint:** three builds green; **`DockTest` first, always**; then the combined filter (§6.3) green,
reported as one pass/fail count in `WOLFMED_STATUS.md` as phases 1–3 did.

---

### WP12-10 — Guidebook, docs and manifest reconcile (P4-7 / P4-9)

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | `Resources/ServerInfo/_Onyx/Guidebook/Medical/Wounds.xml` | `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/Wounds.xml` | **new, adapted** |
| 2 | `Resources/ServerInfo/_Onyx/Guidebook/Medical/WoundTreatment.xml` | `Resources/ServerInfo/_WF/Wolfmed/Guidebook/Medical/WoundTreatment.xml` | **new, adapted** |
| 3 | `Resources/Prototypes/_Onyx/Guidebook/medical.yml` (2 of its 5 entries) | `Resources/Prototypes/_WF/Wolfmed/Guidebook/medical.yml` | **new** — `Wounds` and `WoundTreatment` guideEntry rows only |
| 4 | — | `Resources/Prototypes/Guidebook/medical.yml` | **modified — PROTO L**, 2 lines |
| 5 | `Resources/Locale/en-US/_Onyx/guidebook/wounds.ftl` | `Resources/Locale/en-US/_WF/Wolfmed/guidebook/wounds.ftl` | **new, adapted subset** |
| 6 | — | `Docs/Wolfmed/WOLFMED_PLAN4.md` | **new** |
| 7 | — | `Docs/Wolfmed/WOLFMED_MANIFEST.md`, `WOLFMED_STATUS.md` | **modified** — reconcile (§7) |

**Content edits, each a judgment call already made:**

* **Drop entirely** the "Part material" section (slime/plant, IPC mechanical, cybernetic mechanical,
  electrical-by-material) and the IPC/cybernetic-repair paragraph. Nothing in phases 1–4 supports non-organic
  wounds, and the `SyntheticRepairTool` embed does not exist in WG.
* **Reword "Dismemberment"** per §8.6-1: guns and lasers can finish an amputation, not only melee.
* **Reword "Fracture"** qualitatively ("a fractured arm slows hand work; a fractured leg slows movement") —
  do not quote numbers, since §8.2-1 corrected the shipped `manipulationModifier` values.
* **Keep** the treatment checklist and the biological-tissue paragraph, and **add** the tourniquet
  (`<GuideEntityEmbed Entity="Tourniquet"/>`) and a sentence on the painkiller ladder from WP12-2.
* **Keep the amputation-consequence paragraph** (P4-D15) — WP12-5 made it real. **Say what it actually does**
  (CRITIQUE4 m7): the block is on the **stump**, and every limb that attaches to that stump is hidden from the
  surgery list, not greyed out with a reason — `StepInvalidReason.AmputationConsequence` and its popup are out
  of scope (§8.7), so a medic sees the attach surgeries simply vanish. For a torso stump that is six surgeries
  (head, both arms, both legs, hands); an arm or leg stump hides only that limb's hand or foot. One sentence in
  the guidebook and one line in `WOLFMED_STATUS.md`. **If the attach block was
  dropped, drop this paragraph with it.**
* **Fold `BodyPartDamage`'s three short paragraphs into `Wounds`** rather than reconstructing a fourth page:
  its locale keys are present in Onyx's `wounds.ftl` but its XML is outside the sparse checkout, and this plan
  does not guess file contents.
* **Skip Onyx's `Surgery` guideEntry entirely** (P4-D15). Wolfgate's existing Surgery entry already documents
  the step system that WP12-5's surgeries plug into.

**Embed check, already run:** `Gauze`, `Brutepack`, `Ointment`, `MedicatedSuture`, `RegenerativeMesh`,
`Bonesetter`, `BoneGel`, `HandheldHealthAnalyzer`, `ChemDispenser`, `Syringe`, `Welder`, `CableApcStack`,
`Tourniquet` all resolve in WG. `SyntheticRepairTool` does **not** — drop it.

**Build checkpoint:** three builds green; **Release lint green**; the guidebook opens in-client without a
missing-file or missing-key error; manifest and status doc reconciled per §7.

---
## 5. Duplicate directed subscription and component-name audit

RT stores **one** registration per `(component, event)` pair for the whole bus
(`EntityEventBus.Directed.cs:407,419`). Every row below was re-grepped in the current tree during this pass.

### 5.1 Every pair phase 4 registers

| # | Component | Event | Registrant | WP | Existing WG owner | Free? |
|---|---|---|---|---|---|---|
| 1 | `MedicalPatchComponent` | `EntityStuckEvent` | `MedicalPatchSystem` (server) | WP12-0 | **none** — brand-new component | **YES** |
| 2 | `MedicalPatchComponent` | `EntityUnstuckEvent` | `MedicalPatchSystem` (server) | WP12-0 | **none** | **YES** |
| 3 | `TourniquetComponent` | `UseInHandEvent` | `TourniquetSystem` (server) | WP12-3 | **none** — brand-new component | **YES** |
| 4 | `TourniquetComponent` | `AfterInteractEvent` | `TourniquetSystem` (server) | WP12-3 | **none** | **YES** |
| 5 | `TourniquetComponent` | `TourniquetDoAfterEvent` | `TourniquetSystem` (server) | WP12-3 | **none** | **YES** |
| S1 | `WolfmedSurgeryWoundConditionComponent` | `SurgeryValidEvent` | `WolfmedSurgeryConditionSystem` (shared) | WP12-4 | **none** | **YES** |
| S2 | `WolfmedSurgeryFractureConditionComponent` | `SurgeryValidEvent` | same | WP12-4 | **none** | **YES** |
| S3 | `WolfmedSurgeryOrganDamagedConditionComponent` | `SurgeryValidEvent` | same | WP12-4 | **none** | **YES** |
| S4 | `WolfmedSurgeryClampBleedingEffectComponent` | `SurgeryStepCompleteCheckEvent` | same | WP12-4 | **none** | **YES** |
| S5 | `WolfmedSurgeryTreatWoundEffectComponent` | `SurgeryStepCompleteCheckEvent` | same | WP12-4 | **none** | **YES** |
| S6 | `WolfmedSurgeryMendFractureEffectComponent` | `SurgeryStepCompleteCheckEvent` | same | WP12-4 | **none** | **YES** |
| S7 | `WolfmedSurgeryOrganHealEffectComponent` | `SurgeryStepCompleteCheckEvent` | same | WP12-4 | **none** | **YES** |
| S8 | `WolfmedSurgeryIncisionTreatmentEffectComponent` | `SurgeryStepCompleteCheckEvent` | same | WP12-4 | **none** | **YES** |
| V1 | `WolfmedSurgeryClampBleedingEffectComponent` | `SurgeryStepEvent` | `WolfmedWoundSurgerySystem` (server) | WP12-4 | **none** | **YES** — different *event* from S4, so one component in two systems is legal |
| V2 | `WolfmedSurgeryTreatWoundEffectComponent` | `SurgeryStepEvent` | same | WP12-4 | **none** | **YES** |
| V3 | `WolfmedSurgeryMendFractureEffectComponent` | `SurgeryStepEvent` | same | WP12-4 | **none** | **YES** |
| V4 | `WolfmedSurgeryOrganHealEffectComponent` | `SurgeryStepEvent` | same | WP12-4 | **none** | **YES** |
| V5 | `WolfmedSurgeryPainEffectComponent` | `SurgeryStepEvent` | same | WP12-4 | **none** | **YES** |
| V6 | `WolfmedSurgeryIncisionWoundEffectComponent` | `SurgeryStepEvent` | same | WP12-4 | **none** | **YES** |
| V7 | `WolfmedSurgeryIncisionTreatmentEffectComponent` | `SurgeryStepEvent` | same | WP12-4 | **none** | **YES** |

**Twenty pairs, every one on a brand-new component.** WP12-1, WP12-2, WP12-5, WP12-6, WP12-7, WP12-8, WP12-9
and WP12-10 register **nothing**: the reagent effects are data classes dispatched by a virtual call
(`MetabolizerSystem.cs:218`), HOOK 9/23/24/25/26 extend existing handlers, the analyzer package adds no BUI
message, and `WolfmedExplosionSystem` registers only two `Subs.CVar` callbacks, which are not directed
subscriptions.

**Do not use `SubSurgery<T>`** in the `_WF` systems — it is Shitmed's private helper that registers *both*
`SurgeryStepEvent` and `SurgeryStepCompleteCheckEvent` at once (`SharedSurgerySystem.Steps.cs:67`). Phase 4
deliberately splits those two events across two assemblies (§2.5/§2.6), so each subscription is written out.

### 5.2 Pairs phase 4 deliberately does **not** register

| Pair | Existing owner | Consequence if added |
|---|---|---|
| `<SurgeryWoundedConditionComponent, SurgeryValidEvent>` | `SharedSurgerySystem.cs:67` | server-start crash. **HOOK 24 extends that handler's body; it never re-subscribes** |
| `<SurgeryPartRemovedConditionComponent, SurgeryValidEvent>` | `SharedSurgerySystem.cs:68` | server-start crash. Same for HOOK 25 |
| `<SurgeryTendWoundsEffectComponent, SurgeryStepEvent>` / `<…, SurgeryStepCompleteCheckEvent>` | `SharedSurgerySystem.Steps.cs:49` (`SubSurgery`) | server-start crash. P4-D16 leaves this component alone entirely |
| `<SurgeryStepComponent, SurgeryStepEvent / SurgeryStepCompleteCheckEvent / SurgeryCanPerformStepEvent>` | `Steps.cs:39-41` | server-start crash |
| `<SurgeryStepEmoteEffectComponent, SurgeryStepEvent>`, `<SurgeryStepSpawnEffectComponent, SurgeryStepEvent>` | server `SurgerySystem.cs:55-56` | server-start crash |
| `<SurgeryTargetComponent, SurgeryStepDamageEvent>`, `<SurgeryDamageChangeEffectComponent, SurgeryStepDamageChangeEvent>` | server `SurgerySystem.cs:50,53-54` | server-start crash. **P4-D21 adds a component beside `SurgeryDamageChangeEffect`, never a second handler for it** |
| `<HealthAnalyzerComponent, AfterInteractEvent / HealthAnalyzerDoAfterEvent / EntGotInsertedIntoContainerMessage / ItemToggledEvent / DroppedEvent>` | `HealthAnalyzerSystem.cs:46-50` | server-start crash |
| `<HealthAnalyzerComponent, HealthAnalyzerPartMessage>` | `HealthAnalyzerSystem.cs:52-55` (`Subs.BuiEvents`) | server-start crash. Extend the existing handler |
| `<WoundableComponent, PartDamageAppliedEvent>` | `OrganDamageSystem.cs:31` — the single fan-out point | server-start crash |
| `<WoundHostComponent, BeforeDamageChangedEvent>` / `<…, DamageDealtEvent>` | `WoundDamageRoutingSystem.cs:65-66` | server-start crash. WP12-8 changes method signatures inside that system, never its registrations |
| `<BodyPartComponent, DamageChangedEvent>` | `SharedBodySystem.Targeting.cs:70` | server-start crash. Do not "push" analyzer refreshes from damage |
| `<WoundHostComponent, BodyPartAddedEvent / BodyPartRemovedEvent>` | `WolfmedBodyPartLifecycleSystem.cs:22-23` | server-start crash. **If a future refund of P3-D1a's vital charge is wanted, extend that handler** |
| `<ArmorComponent, InventoryRelayedEvent<PartDamageModifyEvent>>` | `WolfmedPartArmorSystem.cs:25` | server-start crash |

**On adding a new BUI message, for the record:** `Subs.BuiEvents<TComp>(key, …)` expands to
`SubscribeLocalEvent<TComp, TEvent>` with a UI-key filter, so a *new* message type would give a free pair.
P4-4 adds none (§2.8 / WP12-6): four appended nullable fields on the existing once-per-second message are
strictly cheaper than a second round-trip.

### 5.3 Component and type registration names

**New `[RegisterComponent]` names — 12, all grepped in this pass, all 0 hits:**
`MedicalPatch`, `Tourniquet`, `WolfmedSurgeryWoundCondition`, `WolfmedSurgeryClampBleedingEffect`,
`WolfmedSurgeryTreatWoundEffect`, `WolfmedSurgeryFractureCondition`, `WolfmedSurgeryMendFractureEffect`,
`WolfmedSurgeryOrganDamagedCondition`, `WolfmedSurgeryOrganHealEffect`, `WolfmedSurgeryPainEffect`,
`WolfmedSurgeryIncisionWoundEffect`, `WolfmedSurgeryIncisionTreatmentEffect`.

**Two names deliberately NOT used** (P4-D17) because they are already registered by Shitmed and a duplicate
registered name is a `ComponentFactory` crash at boot: `SurgeryWoundedCondition`
(`Content.Shared/_Shitmed/Surgery/Conditions/SurgeryWoundedConditionComponent.cs:7`) and
`SurgeryTendWoundsEffect` (`.../Effects/Step/SurgeryTendWoundsEffectComponent.cs:7`). **Five more avoided out
of caution** even though currently free, because they read as upstream Shitmed names a Monolith merge could
introduce: `SurgeryHasWoundCondition`, `SurgeryClampBleedingEffect`, `SurgeryTreatWoundEffect`,
`SurgeryMendFractureEffect`, `SurgeryFractureGradeCondition`, `SurgeryOrganHealEffect`.

**New non-component C# type names — 13, all 0 hits:** `SuppressPain`, `MendFractures`, `TakeStaminaDamage`,
`StaminaDamageCondition`, `WolfmedIncisionTreatment`, `WolfmedSurgeryConditionSystem`,
`WolfmedWoundSurgerySystem`, `WolfmedExplosionSystem`, `WolfmedDiagnosticPanel`, `EllipsisLabel`,
`HealthAnalyzerWoundDiagnostic(s)`, `HealthAnalyzerVisibleWound`, `HealthAnalyzerClottingPhase`,
`HealthAnalyzerOrganInfo`, `HealthAnalyzerChemicalInfo`, `HealthAnalyzerSolutionType`,
`HealthAnalyzerReagentInfo`.

**Near-miss to watch:** `StatusEffectMetabolismType` already exists in WG
(`Content.Server/EntityEffects/Effects/StatusEffects/GenericStatusEffect.cs:76-81`). It is an enum and so
cannot collide at YAML level, but it would collide at C# level if P4-5 were ever taken and a `_WF` file
declared another one with both namespaces imported. Use a distinct name then (P4-D8).

**New prototype ids — 26 checked in this pass, all 0 hits:** the 13 surgeries, the 13 steps (including
`SurgeryStepHealOrganBase`), the 5 reagents, `MedicalPatchMakeshift`/`UsedMedicalPatch`/`UsedMedicalPatchMakeshift`,
the `MedicalPatch` tag, and the `Wounds`/`WoundTreatment` guideEntry ids. **The one genuine prototype collision
in the phase is `id: Tourniquet`**, resolved by editing in place (P4-D9), and the one guideEntry collision is
`id: Surgery`, resolved by skipping it (P4-D15).

### 5.4 Ordering constraints

* **None of the 20 new subscriptions needs `before:`/`after:`.** The surgery events each have a single
  authoritative raiser and the effect handlers are order-independent (a clamp, a treat, a mend and an organ
  heal on the same step entity would be a prototype error, not a race).
* **`WoundDamageRoutingSystem`'s existing `before: [typeof(SharedArmorPlateSystem)]` on both handlers
  (`:64`, `:66`) stays exactly as shipped** — WP12-8 edits method bodies and signatures in that file and must
  not disturb the registrations.
* **`OrganDamageSystem`'s explicit call order `_wounds` → `_fractures` → `_amputation` → `_bleeding`
  (`:36-40`) is load-bearing and untouched by phase 4.**

---

## 6. Test plan

### 6.1 Standing traps (apply to every phase-4 test)

1. **`DockTest` first.** `db.ef` sqlite warnings fail every pair test in this repo (project memory).
2. **`TerminatingOrDeleted`.** Any new reactor to part/organ lifecycle must guard before
   `EnsureComp`/`CreateOrMergeWound`. One unguarded spawn/dispose poisons the shared pooled pair for the whole
   run — it cost WP9 13 unrelated failures. Phase 4's new reactors are all step-event handlers on nullspace
   singletons, which is why none of them needs the guard; **do not add one that does without it.**
3. **`Assert.Multiple` bodies must be fully synchronous** — no `async`/`await` inside the lambda.
4. **`[TestPrototypes]` ids are a global pool** (§4 rule 3).
5. **YAML lints in Release only** (`ErrorNode` crashes the linter elsewhere).
6. **Every Onyx literal is a prediction until measured** (P2-D16). Put the derivation in a `// WOLFGATE`
   comment at each assertion, as WP9 / WP10-6b / WP11-5 did.
7. **`_treatmentCapabilities` is opt-in, not ambient.** `CanTreatPart` returns `true` unconditionally unless
   the calling code first opened `WithTreatmentCapabilities`. A test that calls `TryApplyPartDamage` directly
   gets **zero** capability gating — T-REAGENT-CAP-NO only means something because the code under test opens
   the scope itself.
8. **Reagent-effect testing needs no chemistry.** Construct the args directly:
   `new EntityEffectReagentArgs(target, entityManager, organEntity: null, source: null, quantity: FixedPoint2.New(1), reagent: null, method: null, scale: FixedPoint2.New(1))`
   (`EntityEffect.cs:109-124`, verified) and call `.Effect(args)`.
9. **Surgery-step testing needs no do-after and no UI.** `SharedSurgerySystem.GetSingleton(EntProtoId)` is
   **public** (`:349`); spawn or fetch the step singleton and raise the event directly:
   `var ev = new SurgeryStepEvent(user, body, part, new List<EntityUid>(), surgeryEnt); EventBus.RaiseLocalEvent(stepEnt, ref ev);`
   **Note WG's 5-tuple** — Onyx's tests use a 4-tuple and every ported line needs the extra `Surgery` argument.
   This bypasses `IsSurgeryValid`, `PreviousStepsComplete`, `CanPerformStep` and the do-after delay; it does
   **not** bypass anything the effect handler itself queries.
10. **Pain fixtures need `- type: StatusEffects` with an explicit `allowed:` plus `- type: MobState`**
    (P2-D24) — T-REAGENT-SUPPRESS and T-SURG-PAIN only.
11. **Fracture creation is a dice roll below 60 damage** (P2-D23). Every fracture test uses **75 Blunt**, which
    reaches the Comminuted threshold (60) whose `creationChance` is 1.

### 6.2 Test table

| # | Test | File (WP12-9) | Setup | Derived expected values (and the derivation) |
|---|---|---|---|---|
| **T-PATCH** | `MedicalPatchInjectsOnScheduleAndStopsOnUnstickTest` | `_WF/Wolfmed/WolfmedMedicalPatchTest.cs` | Bespoke `[TestPrototypes]`: `WolfmedPatchItem` with `- type: MedicalPatch { solutionName: patch, transferAmount: 5, updateTime: 1 }` + a `SolutionContainerManager` `patch` solution pre-filled with a new inert test reagent `WolfmedPatchTestChem` (no metabolisms — keeps this independent of WP12-2); a wound-host target with an injectable solution. Stick via `RaiseLocalEvent(patch, new EntityStuckEvent(target, user))` | (a) with `injectAmmountOnAttatch` set, the target's solution gains that amount immediately; (b) after `RunSeconds(1.1f)` it has gained **5u**; (c) after `EntityUnstuckEvent` + another 1.1 s, **no further transfer**; (d) with `singleUse: true` + `trashObject`, `entities.Deleted(patch)` is true and the trash prototype spawned. **Deliberately asserts no wound healing** — this is the delivery device, and a D2 sanity check that it behaves identically on a wound host |
| **T-REAGENT-CAP-YES** | `TreatmentCapabilityMatchHealsWoundTest` | `_WF/Wolfmed/WolfmedReagentTreatmentTest.cs` | Wound host; `routing.TryApplyPartDamage(body, head, Spec("Blunt", 20))` creates a `BluntWound`. Build `EntityEffectReagentArgs` (trap 8) and call `new HealthChange { Damage = Blunt -30, TreatmentCapabilities = [Biological] }.Effect(args)` | Both the part's `GetAllDamage` total **and** the wound's `Severity` fall. **Re-derive the starting severity from the shipped `BluntWound.damageTypes.Blunt.severityMultiplier` (1 at `wounds.yml:85-88`) — do not assume 20.** This pins §2.9's "reagents already heal wounds today" so a later refactor cannot silently break it |
| **T-REAGENT-CAP-NO** | `TreatmentCapabilityMismatchDoesNotHealTest` | same | Identical, but `TreatmentCapabilities = [Mechanical]` | **Both the raw damage total and the wound severity are unchanged.** Derivation: `OrganicBodyPartProfile.treatmentCapabilities` is `[Biological]` (`wounds.yml:4`), so `profile.TreatmentCapabilities.Overlaps({Mechanical})` is false and `CanTreatPart` refuses. **This is the only test that proves HOOK 9 is wired at all** (trap 7): if the implementer forgets `WithTreatmentCapabilities`, `CanTreatPart` defaults to `true` and the heal lands |
| **T-REAGENT-SYSTEMIC-BYPASS** | `SystemicHealingIgnoresTreatmentCapabilitiesTest` | same | A `Toxin: -5` `HealthChange` with `TreatmentCapabilities = [Mechanical]` | **The heal still lands.** `Toxin` is not in `WoundHostComponent.LocalizedDamageTypes` (`WoundDamageComponents.cs:35-44`), so `RouteAppliedDamage` sends it to `SystemicDamageComponent` without passing `CanTreatPart` (`:701-723`). Guards against someone "fixing" the gate into the systemic branch |
| **T-REAGENT-SUPPRESS** | `SuppressPainLowersEffectivePainTest` | same | `MobHuman` wound host (P2-D24 fixture); raise pain with `routing.TryApplyPartDamage(body, head, Spec("Blunt", 15))`; `new SuppressPain { Amount = 20, DecayDuration = 30s }.Effect(args)` | `pain.GetPain(body)` (the **effective**, suppression-adjusted value at `PainSystem.cs:165`, distinct from `GetRawPain` at `:198`) drops by up to the full `Amount`, floored at zero; `PainComponent.SuppressionModifiers` contains `"PainSuppressant"` with a positive amount; **a second dose with the same identifier accumulates rather than resets** (`PainSystem.cs:346-350` reads `TryGetValue` and adds), and a different identifier stacks independently; then tick and assert decay |
| **T-REAGENT-MEND** | `MendFracturesReducesMatchingFractureTest` | same | 75 Blunt to an arm → Comminuted fracture; `new MendFractures { Amount = 5 }.Effect(args)` | Severity drops by 5. Then: `MaximumGrade = Simple` refuses a Comminuted fracture (no change); `Wounds = ["SomeOtherWound"]` is a no-op; a body **without** `WoundHostComponent` is a no-op (the `HasComponent` gate) |
| **T-REAGENT-STAM** | `StaminaEffectAndConditionTest` | same | `new TakeStaminaDamage { Amount = 30 }.Effect(args)`; then `new StaminaDamageCondition { Min = 20 }.Condition(args)` | `StaminaComponent.StaminaDamage` rises by 30 via `StaminaSystem.GetStaminaDamage`; the condition is false at 0 and true at 30. Also assert the `Scale != 1` gate: an args with `scale: FixedPoint2.New(0.5)` is a **no-op** |
| **T-TOURNIQUET** | `TourniquetStopsOnlySelectedPartTest` (restored) | `_Onyx/Wounds/WoundBleedingTest.cs` — **replaces the skip note at `:147-148`** | Existing `WoundBleedingBody`; a fresh `Slash 10` wound on head **and** torso (both bleed); `entities.System<TourniquetSystem>().Apply(body, head)` directly (target-agnostic — the do-after/targeting layer is skipped per trap 9) | `Apply` returns `true`; `bleeding.GetPartRate(head) == 0` (every bleeding wound on the part gets `BleedingTreatment.Clamped`, a 0f multiplier at `WoundBleedingSystem.cs:469`); `bleeding.GetPartRate(torso) > 0`, untouched. **Plus a check Onyx's own test never made:** a second `Apply(body, head)` returns **`false`**, because `CanApply` requires `GetPartRate(part) > 0` |
| **T-SURG-BLEED** | `SurgeryClampBleedingReducesTheWorstBleederTest` | `_WF/Wolfmed/WolfmedWoundSurgeryTest.cs` | Bare `[TestPrototypes]` part (`BodyPart Arm` + `Woundable`); `wounds.CreateOrMergeWound(part, "SlashWound", 20)` and a second at 10; spawn a bare `WolfmedSurgeryClampBleedingEffect { amount: 10 }` entity; raise `SurgeryStepEvent` (trap 9) | The **highest-severity** wound's bleeding falls first (20 → 10 → gone); the lower-severity wound on the same part and any wound on a different part are untouched; `SurgeryStepCompleteCheckEvent` on the same entity is `Cancelled == true` while a bleeder remains and `false` once `GetPartRate(part) == 0` |
| **T-SURG-FRACTURE** | `SurgeryFractureLadderReducesThenMendsTest` | same | `WoundFractureBody`-shaped fixture; `routing.TryApplyPartDamage(body, arm, Spec("Blunt", 75))` | `GetFracture(arm)` non-null with `Grade == Comminuted` (75 ≥ the Comminuted threshold **60**, `creationChance` 1 — P2-D23). `SurgeryStepSetBone` → `Treatment == FractureTreatment.Reduced` (`CanTreat` allows it: `Comminuted >= ReductionMinimumGrade (Simple)` and `Treatment == None`). `SurgeryStepMendFracture` → `GetFracture(arm)` is **null**, because `removeWoundWhenMended: true` on `OrganicFractureProfile` makes `TryMend` remove the wound. A third raise is a no-op, not an exception. **Plus the P4-D20 regression:** a **Hairline** fracture (drive severity down to 20-34) must leave `SurgeryStepSetBone`'s complete-check **uncancelled**, because `Reduced` is unreachable below `Simple` and a naive check would stall the surgery forever |
| **T-SURG-INTERNAL** | `SurgeryStopInternalBleedingRemovesTheWoundTest` | same | Torso with an `InternalBleedingWound` (either `OrganHealthSystem.SetHealth(organ, 0)` + a tick, as `WolfmedOrganTest` does, or `CreateOrMergeWound(torso, "InternalBleedingWound", 30)` directly); bare `WolfmedSurgeryTreatWoundEffect { internalBleeding: true }`; one `SurgeryStepEvent` | Before: `WoundInternalBleedingComponent.Severity > 0` and the wound is in `GetWounds(torso)`. After **one** raise: **the wound entity is gone** — `Amount` defaults to `FixedPoint2.MaxValue`, and `TreatWound` → `ChangeSeverity` → `if (severity == Zero) return RemoveWound(wound)` (`WoundSystem.cs:284-314`). The condition then cancels |
| **T-SURG-AMPCONSEQ** | `SurgeryHealAmputationConsequenceRemovesTheWoundTest` | same | Reuse `WolfmedAmputationTest`'s fixture: amputate an arm so the **torso** gains the consequence wound; bare `WolfmedSurgeryTreatWoundEffect { woundPrototype: AmputationConsequenceWound }`; raise the step **targeting the torso** | Before: exactly one `AmputationConsequenceWound` on the torso (**severity 35** on a stock body, 50 with the WP11-5 fixture line — re-derive, do not assume). After one raise: **zero**. Note `AmputationConsequenceWound` has `damageTypes: {}`, so `TryHealWounds`, topicals and passive recovery cannot touch it — **surgery is the only cure, by design** |
| **T-SURG-ORGAN** | `SurgeryHealOrganRestoresHealthTest` | same | Reuse `WolfmedOrganTest`'s fixture; `OrganHealthSystem.SetHealth(heart, 6)` directly (deterministic, avoids the ~1–4 % per-hit organ roll); bare `WolfmedSurgeryOrganHealEffect { slot: heart, amount: 3 }` | `WolfmedOrganComponent.Health` goes 6 → 9 → 12 → **15** and **clamps** (`SetHealth`'s `FixedPoint2.Clamp`); the complete-check cancels while `Health < MaxHealth` and passes at `MaxHealth`; a further raise is a no-op. **Plus the function-restore assertion:** heal an organ back from exactly 0 in the one-tick window before `Update` destroys it and assert `OrganFunctionChangedEvent(true)` fired and `WolfmedOrganConsequenceSystem` restored its `OnAdd` grants (reuse T-ORG-FUNC's `MutedComponent` fixture organ) |
| **T-SURG-WINDOW** | `WoundSeverityWindowSelectsShallowOrDeepTest` | same | A part at **≥100** Brute wound severity and the same fixture at **60** | At 100: `SurgeryTendWoundsBruteDeep`'s `SurgeryValidEvent` passes and `SurgeryTendWoundsBrute`'s is **cancelled** (`maxWoundSeverity: 99.99`). At 60: the reverse. **The D2 canary:** on a body **without** `WoundHostComponent` both bounds are ignored and `OnWoundedValid` behaves byte-identically to today — assert the existing tend surgery still lists on a damaged non-host part |
| **T-SURG-SCAR** | `SurgeryIncisionBleedsClampsAndScarsTest` | same | Pin `CCVars.SurgeryScarChance` in a `try/finally` (Onyx's own pattern). Raise `WolfmedSurgeryIncisionWoundEffect` on a part, then the `Clamp` treatment, then the `Close` treatment. **The `Close` step entity must be raised twice in the final assertion** — once standing in for `SurgeryStepCloseIncision`, once for `SurgeryStepSealTendWound`, which now carries the same component (PROTO G) | After the incision effect: a `SurgicalIncisionWound` at severity 10 exists and `GetPartRate(part) > 0` (**derive the rate from `rate: 0.1 × severity 10 × awakeMultiplier 3` and the `MaxBleedAmount` clamp of 10 — do not assume**). After `Clamp`: the part rate is 0. After `Close` with chance **0**: the incision wound is removed and **no** `MedicalScarWound` exists. With chance **1**: exactly **one** scar, and a second `Close` does not create a second. **This test is the whole justification for P4-D21 — if it is cut, cut the mechanic** |
| **T-SURG-PAIN** | `SurgeryStepInflictsPainTest` | same | P2-D24 fixture; bare `WolfmedSurgeryPainEffect { amount: 12 }`; one step raise | `PainSystem.GetRawPain` on the **part** rises by 12 (`PainComponent` lives on the part, `PainSystem.cs:69`), and the body's aggregate pain rises accordingly |
| **T-REATTACH-BLOCKED** | `AmputatedLimbCannotBeReattachedUntilConsequenceTreatedTest` | `_WF/Wolfmed/WolfmedReattachTest.cs` (new method) | Amputate an arm on a `WolfmedAmputationBody`-shaped fixture (torso gains the consequence wound). Fetch the `SurgeryAttachLeftArm` singleton via `GetSingleton` and raise `SurgeryValidEvent(body, torso)` on it | **`ev.Cancelled == true`** while the consequence wound is present — HOOK 25. **Then** raise T-SURG-AMPCONSEQ's treat step on the torso and assert the **same** `SurgeryValidEvent` now passes (`Cancelled == false`). **Also assert `_body.CanAttachPart(torso, "left arm", arm)` is STILL `true` throughout** — P4-D18: the body layer is deliberately not gated, so Mono prosthetics keep working. **This is the direct replacement for `tests.md`'s `CanAttachPart`-based shape, and the assertion that proves prosthetics were not collateral damage** |
| **T-SURG-AMP-CLEAN** | `SurgicallyRemovedLimbCanStillBeReattachedTest` | same file | Remove a limb through `SurgeryRemovePart`'s path (`AmputateAttemptEvent` → `WolfmedBodySystem.TryDetachPart`) rather than traumatically | No `AmputationConsequenceWound` on the parent; `SurgeryValidEvent` on `SurgeryAttachLeftArm` passes. Confirms only `AmputationSystem`'s traumatic path sets the block |
| **T-AN-GATE** | `AnalyzerBuildersReturnNullForNonHostsTest` | `_WF/Wolfmed/WolfmedAnalyzerTest.cs` | A plain `Damageable` entity, and a `SurgeryTarget`-without-`WoundHost` mob | `BuildWoundDiagnostics` and `BuildOrganInfo` both return **`null`** — the D2 gate (§2.9 edit 1). `BuildChemicalInfo` still returns a list for anything with a bloodstream |
| **T-AN-FINDINGS** | `WoundDiagnosticsReportsInjuredPartsOnlyTest` | same | Human wound host: `CreateOrMergeWound(head, "SlashWound", 10)`; `routing.TryApplyPartDamage(body, arm, Spec("Blunt", 75))`; a closed `BluntWound` scarred on the torso | `Parts[Head].BleedingRate > 0`; `Parts[LeftArm].Fracture == FractureGrade.Comminuted` with `.FractureTreatment == FractureTreatment.None`; `Parts[Torso].ScarCount == 1`; an uninjured part is **absent** (`HasFindings` gating); **`Parts.ContainsKey(TargetBodyPart.Groin) == false`** — D9, WG never emits a Groin row |
| **T-AN-CLEARS** | `TreatmentDisappearsFromTheReadoutTest` | same | `fractures.TryMend(fracture.Owner)` then rebuild; then `wfBody.TryDetachPart(head)` then rebuild | `Parts` loses `LeftArm`, then loses `Head`. This is the acceptance criterion for WP12-5's surgeries — **schedule it after WP12-5 or mark it `[Ignore]` until then** |
| **T-AN-CLOT** | `ClottingPhaseIsClassifiedTest` | same | One bleeding wound with `AutomaticClottingAt` set; one with `NaturalClotting > 0 && CurrentRate <= 0`; one with neither; then two of different phase on one part | `InProgress` / `Complete` / `None` / **`Mixed`** respectively; a part with no bleeding wound reads `NotApplicable` |
| **T-AN-PAIN** | `WoundDiagnosticsCarriesPartPainTest` | same | Apply pain to a part | `Parts[part].Pain == PainSystem.GetPain((part, pain))` — the phase-2 numbers, now readable |
| **T-AN-ORGANS** | `OrganRowsAreOrderedAndScopedTest` | same | A human; then `OrganHealthSystem.SetHealth(heart, 0)` | Exactly **7** rows, ordered brain(0) → eyes(1) → lungs(4) → heart(5) → liver(6) → stomach(7) → kidneys(9) — Onyx's `ears`(2), `tongue`(3) and `appendix`(8) slots simply do not exist in WG's body graph. After `SetHealth(heart, 0)` the heart row reads `Health == 0`. **Plus:** a non-human-lineage species yields an **empty list, not null** (P3-D7 limitation) |
| **T-AN-CHEM** | `ChemicalInfoListsEverySolutionTest` | same | Inject a reagent into the bloodstream; feed the stomach | A `Bloodstream` entry contains the reagent at the right quantity; a `Stomach` entry appears; a mob with no bloodstream yields an **empty list, not null** |
| **T-AN-VITAL** | `VitalDamageDiffersFromProjectedTotalTest` | same | Damage a wound host's limbs | `BuildVitalDamage(body) == _mobThreshold.CheckVitalDamage(body, damageable)` and it **differs** from `damageable.TotalDamage` once damage is routed to non-vital parts. This is the number that actually decides crit and death |
| **T-EXPLOSION-PLATE** | `DistributedExplosionDamageKeepsPlateProtectionTest` | `_WF/Wolfmed/WolfmedExplosionTest.cs` | A wound host wearing `ArmorPlateHolderComponent` gear with an inserted plate; call `routing.TryRouteDistributedDamage(body, dmg, TargetBodyPart.All, SplitWithVariation, variation: 0f, isExplosion: true, originFlag: DamageOriginFlag.Explosion)` directly (routing-API level, same style as phase 3's T-AMP-EXPLOSION — no live grenade) | Applied damage is **lower** than the same call with the plate removed, proving the flag survived the distributed path. **Plus the both-directions closure:** a mob **without** `WoundHostComponent` takes flat vanilla damage with plate protection unchanged. **This is the gate on P4-D14** — without it the package reproduces the exact hole P3-D3 identified |
| **T-REAGENT-PROTOTYPE-SANITY** | `ShippedReagentsCarryTheRightEffectInTheRightGroupTest` | `_WF/Wolfmed/WolfmedReagentTreatmentTest.cs` | **Prototype-only, no mob, ~35 lines.** `_prototypes.Index<ReagentPrototype>` each of `Cognac`, `Bicaridine`, `Desoxyephedrine`, `Happiness`, `Stasizium`, `Osteogen`, `Ibuprofen`, `Ketorolac`, `Tramadol`, `Oxycodone` and walk `proto.Metabolisms` | **This is the only test that can catch B2 or a mistranslated `!type:`** (§8.5 risks 4 and 5 — previously untested, CRITIQUE4 M4). For each reagent assert (a) `Metabolisms` contains the expected `ProtoId<MetabolismGroupPrototype>` key — `Drink` for Cognac, `Medicine` for Bicaridine/Stasizium and all five new ones, `Narcotic` for Desoxyephedrine and Happiness — and (b) that group's `Effects` contains exactly one `SuppressPain` (or `MendFractures`) instance with the §8.1 amounts. **Assert no reagent in `_Onyx/Reagents/**` declares a group outside the nine that exist** — iterate `Metabolisms.Keys` and `_prototypes.HasIndex<MetabolismGroupPrototype>` each, which fails loudly on a copied `Bloodstream:`/`Digestion:` rather than relying on the lint. **Also assert `Desoxyephedrine`'s `SuppressPain` is in `Narcotic`, not `Poison`** — it has both groups (`narcotics.yml:12` and `:45`) and the Release lint cannot tell a valid-but-wrong group from a right one |
| **T-EXPLOSION-WRAPPER** | `ExplosionWrapperSpreadsOnHostsAndFallsThroughOtherwiseTest` | `_WF/Wolfmed/WolfmedExplosionTest.cs` | Call `entities.System<WolfmedExplosionSystem>().TryApplyExplosionDamage(host, dmg)` and the same on a non-host | **Pins HOOK 22's contract (CRITIQUE4 M9).** On a wound host the call returns **`true`** and damage lands on ≥2 attached parts; on a non-host it returns **`false`** and nothing is applied — the D2 fall-through. Without this the whole package can land (`WolfmedExplosionSystem.cs` + the two vendored-file edits) with HOOK 22's three lines forgotten, and every listed test stays green while the mechanism is dormant — exactly the P3-D3 failure shape this WP exists to fix. Pair it with a one-line WP12-8 checklist grep that `ExplosionSystem.Processing.cs` contains `_wolfmedExplosion.TryApplyExplosionDamage` |
| **T-SURGERY-PROTOTYPE-SANITY** | `EveryWoundSurgeryStepResolvesTest` | same file (no mob needed) | Prototype-only: for each of the 13 new surgery ids, `_prototypes.Index<EntityPrototype>` it, read its `SurgeryComponent` | `Steps` is non-empty; **every step id in it resolves and carries `SurgeryStepComponent`** (a typo'd step id is otherwise a runtime `GetSingleton` null, invisible to the lint); each surgery carries at least one condition component so it cannot be started on a healthy part; **`proto.Name` is non-empty and `proto.Categories` contains `HideSpawnMenu`** for all 13 surgeries and all 13 steps (CRITIQUE4 M6). **Plus the closed-loop invariant that permanently kills B1's bug class:** iterate **every** `EntityPrototype` carrying `WolfmedSurgeryIncisionWoundEffectComponent` and assert each one appears in at least one `SurgeryComponent.Steps` list that also reaches a step carrying `WolfmedSurgeryIncisionTreatmentEffectComponent { Treatment == Close }`, directly or through `SurgeryComponent.Requirement`; and assert **no** step carrying the wound effect is reachable from `SurgeryTendWoundsBrute` or `SurgeryTendWoundsBurn`. **Use the plain `server.ResolveDependency<T>()` inside `WaitAssertion` pattern every other prototype-only test here uses** — Onyx's `[SidedDependency(Side.Server)]`/`[RunOnSide(Side.Server)]` attributes were **not confirmed present** in WG's `Content.IntegrationTests/Fixtures/Attributes` and are not worth blocking on |

**Existing tests that must stay green, unchanged:** the whole 65-test phase-1..3 suite, and in particular
`WoundDamageFoundationTest`'s armour tests (HOOK 9 must not change resistance maths),
`WolfmedAmputationTest.T-AMP-EXPLOSION` (WP12-8's new optional parameter defaults to `null`), and
`WoundFractureTest`'s grade-boundary and effects tests (WP12-5's ladder must not touch `GetGrade`).

**Explicitly not ported:** Onyx's `WoundSurgeryTest` and `WoundSurgeryScarTest` as written (both target Onyx's
own excluded surgery framework — their *pattern* is what §6.1 trap 9 adopts); Onyx's
`SurgeryStepSequencePrototypeTest`'s three assertions (Shitmed's `Steps` is a flat `List<EntProtoId>` with no
section/fallback concept); Onyx's `HealthAnalyzerPartDamageTest.BuildsIsolatedPartSnapshotTest` (P4-D27);
`ClassifiesDangerousBloodLevel` (client-side, low value — the 0.65f constant ships in
`WolfmedDiagnosticPanel` as `internal static` if anyone wants it later); any sprite, BUI-message-over-the-wire
or screenshot assertion.

### 6.3 Run commands

```
dotnet build Content.Server/Content.Server.csproj -c DebugOpt -v q -nologo
dotnet build Content.Client/Content.Client.csproj -c DebugOpt -v q -nologo
dotnet build Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt -v q -nologo
dotnet run --project Content.YAMLLinter -c Release
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt --no-build --filter "FullyQualifiedName~DockTest"
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt --no-build \
  --filter "FullyQualifiedName~_Onyx.Wounds|FullyQualifiedName~_Onyx.Body|FullyQualifiedName~_Onyx.Medical|FullyQualifiedName~Wolfmed" \
  --logger "console;verbosity=detailed"
```

Run each new file individually during development; the combined filter is the final phase-4 gate, reported as
one pass/fail count in `WOLFMED_STATUS.md` as phases 1–3 did. **`_Onyx.Medical` is new to the filter** —
add it even though phase 4 places most new tests under `_WF/Wolfmed`, so a later Onyx-pathed test is not missed.

---

## 7. Manifest rows

### 7.1 Row changes (existing rows) — WP12-10 reconciles these

| Manifest row | Change |
|---|---|
| `:100` `ReagentTreatmentEffects/ReagentTreatmentSystems/SuppressPainEntityEffect.cs … skipped … WP11` | → **`skipped (re-authored)` / WP12-1**, note: "D16 — rewritten old-style as four classes in `_WF/Wolfmed/EntityEffects`. **`DistributedHealthChange` is not needed, not deferred** (P4-D6): zero `!type:` uses in Onyx's reagent set and zero references in WG" |
| `:96` `HealingComponent.cs` HOOK 7 / D14 | append: "**Items and reagents reach wounds by two different mechanisms and phase 4 does not unify them.** Items pass `TreatmentCapabilities` explicitly into `WoundHealingSystem.ResolveHealingPart`; reagents open `WithTreatmentCapabilities` (HOOK 9). Do not nest the two — the scope is a non-reentrant dictionary with a `finally` remove" |
| `:132` `WoundBleedingTest.cs … tourniquet test still skipped (phase 4)` | → "6 of Onyx's 6. **`TourniquetStopsOnlySelectedPartTest` restored in WP12-9**, driven through `TourniquetSystem.Apply` directly, plus a new does-not-double-apply assertion Onyx never made" |
| `AmputationConsequenceTest.cs` row (3 of Onyx's 5) | → "**4 of Onyx's 5.** `SurgicalHealRemovesConsequenceAndUnblocks` restored in WP12-9 against HOOK 25's surgery-layer gate. `HealingDamageKeepsConsequenceBlocked` stays skipped — its payload is the `TryAttachPart … Is.False` assertion, and **P4-D18 deliberately leaves `CanAttachPart` ungated**" |
| `WOLFMED_STATUS.md:104-107` known-gaps paragraph | **Strike three gaps**: organ healing now exists (`SurgeryHeal<Organ>`, reversible while the organ lives — P4-D24); the analyzer now reports wounds, fractures, bleeding, organs, chemicals and vital damage; `AmputationConsequenceWound` now blocks re-attachment (through the surgery layer, **not** `CanAttachPart` — say so explicitly, and say that the affected attach surgeries are **hidden rather than greyed**, six of them for a torso stump). **Add two**: pain numbness is skipped (P4-D8) and the cable coil is the phase-5 `treatmentCapabilities` action (P4-D7) |
| `WOLFMED_STATUS.md:174` "explosion amputation is out (§8.6-6)" | → **"landed in WP12-8 (P4-D14)"**, with the plate-protection fix and T-EXPLOSION-PLATE named |
| PLAN.md §8.2's `Scale` tuning note | **Struck** (P4-D30) — the note was backwards |
| PLAN.md WP11's `GroupHealSpecifier` line | **Struck** (P4-D12) — a bundling error with no dependency edge |

### 7.2 New rows

| Onyx path | Wolfgate path | Status | WP | Notes |
|---|---|---|---|---|
| `Content.Server/_Onyx/Medical/MedicalPatch{Component,System}.cs` | same | new (vendored), verbatim | WP12-0 | zero wound coupling, zero upstream edits. No licence header in Onyx |
| `Resources/Prototypes/_Onyx/Entities/…/medical_patch.yml`, `_Onyx/Tags/medical_patch.yml` | same | new | WP12-0 | only spawnable item is the craftable `MedicalPatchMakeshift`; **no fill/vending/cargo placement**, mirroring Onyx (P4-D13) |
| `Resources/Textures/_Onyx/Objects/Medical/medical_patch.rsi` (22 files) | same | new | WP12-0 | **CC-BY-SA-3.0, "@jorgun inspired by Studenterhue of Goonstation"** — a new artist/licence pair for Wolfmed |
| — | `Content.Shared/_WF/Wolfmed/EntityEffects/{SuppressPain,MendFractures,TakeStaminaDamage,StaminaDamageCondition}.cs` | new | WP12-1 | D16 old-style rewrites. `!type:` resolves by bare `Type.Name` — **the four names are load-bearing**. Zero subscriptions, zero components |
| — | `Content.Server/EntityEffects/Effects/{HealthChange,EvenHealthChange}.cs` | modified (hook) | WP12-1 | **HOOK 9.** `TreatmentCapabilities` datafield + a `WithTreatmentCapabilities` branch on negative damage to a wound host. **Inert until a non-Biological `bodyPartProfile` exists** (P4-D1). `EvenHealthChange` also needed `using System.Linq;` |
| — | `Resources/Prototypes/_Onyx/Reagents/Medicine/medicine.yml` + `_Onyx/Recipes/Reactions/medicine.yml` + locale | new | WP12-2 | Tier A: Osteogen, Ibuprofen, Ketorolac, Tramadol, Oxycodone. **Oxycodone's recipe is re-authored without `Heroin`** (P4-D2 deviation) |
| — | four existing reagent YAML files | modified | WP12-2 | **PROTO H–K.** Stasizium gains `MendFractures` (P4-D3, extend not rename); Cognac/Bicaridine/Desoxyephedrine/Happiness gain `SuppressPain`. **Metabolism groups are Wolfgate's, not Onyx's** |
| `Content.Shared/_Onyx/Medical/Tourniquet/TourniquetComponent.cs` | same | new (vendored), verbatim | WP12-3 | |
| `Content.Shared/_Onyx/Medical/Tourniquet/TourniquetSystem.cs` | **`Content.Server/_Onyx/Medical/Tourniquet/TourniquetSystem.cs`** | new (vendored, relocated), 3 marked edits | WP12-3 | **D13** — `WoundBleedingSystem` is server-assembly. `TargetResolverSystem` → `WoundTargetResolver`; Onyx's `TargetingComponent` → `_Shitmed`'s (D10, and Onyx's registers as `"Targeting"` — a boot crash). Deviation: `PlayPredicted` degrades to server audio |
| — | `Resources/Prototypes/Entities/Objects/Specific/Medical/healing.yml` | modified | WP12-3 | **PROTO D** — in-place swap of the existing `id: Tourniquet`'s `Healing` block. **The phase-1 evidence missed this collision** (it checked C# names only); Onyx patches the same upstream entity. `Tourniquet` **tag** not ported — no tag prototype in WG |
| — | `Content.Shared/_WF/Wolfmed/Surgery/WolfmedSurgeryComponents.cs` | new | WP12-4 | 10 components + 1 enum, `WolfmedSurgery*` prefix (P4-D17) |
| — | `Content.Shared/_WF/Wolfmed/Surgery/WolfmedSurgeryConditionSystem.cs` | new | WP12-4 | S1–S8 **shared** — `SurgeryBui.cs:281` runs the completion check client-side |
| — | `Content.Server/_WF/Wolfmed/Surgery/WolfmedWoundSurgerySystem.cs` | new | WP12-4 | V1–V7 **server** — `WoundBleedingSystem`/`OrganHealthSystem` are server assemblies |
| — | `Content.Shared/_WF/Wolfmed/Surgery/SharedSurgerySystem.Wolfmed.cs` | new | WP12-4 | HOOK 24 + HOOK 25 bodies; both return `false` for non-hosts |
| — | `Content.Shared/_Shitmed/Surgery/{SharedSurgerySystem.cs, Conditions/SurgeryWoundedConditionComponent.cs}` | modified (hook + EXT 1) | WP12-4 | **First Wolfmed touch of either file.** HOOK 25 is the P3-D2 gap closed — **at the surgery layer, not `CanAttachPart`**, because `AttachPart` has five non-surgery callers including two Mono prosthetic traits (P4-D18) |
| — | `Resources/Prototypes/_WF/Wolfmed/Surgery/{surgeries,surgery_steps}.yml` + locale | new | WP12-5 | 13 surgeries, 13 steps. **No new tool, no new sprite.** Organ heal at `amount: 3` (P4-D23 balance deviation); fracture ladder BoneSetter→Reduced→BoneGel→Mended (P4-D20); `SurgeryHealKidneys` is the fork's first kidney surgery |
| — | `Resources/Prototypes/_Shitmed/Entities/Surgery/{surgeries,surgery_steps}.yml` | modified | WP12-5 | **PROTO F** (severity window ×2, at `:294`/`:307`) and **PROTO G** (the incision-scar chain ×4, at `:9`/`:31`/`:168`/`:359`). PROTO G's wound effect is **added beside**, not instead of, the existing flat Bloodloss, to keep non-hosts unchanged (§2.7), and goes on `SurgeryStepOpenIncisionScalpel` **only** — not on `SurgeryStepCarefulIncisionScalpel`, whose two tend surgeries have no clamp or close step. `SurgeryStepSealTendWound` gains the `Close` treatment so the wound surgeries are self-closing |
| `Content.Shared/_Onyx/Medical/HealthAnalyzer{WoundDiagnostic,OrganInfo,ChemicalInfo}.cs` | same | new (vendored) | WP12-6 | 1 marked `using` total. **Zero components — no registration-collision surface in this package at all** |
| — | `Content.Shared/MedicalScanner/HealthAnalyzerScannedUserMessage.cs` | modified | WP12-6 | 4 appended nullable fields + 4 optional ctor params. **Onyx's own message cannot be ported** — it has no `Part`, no `Uncloneable`, no `Body` dict |
| — | `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs` + `Content.Server/Medical/HealthAnalyzerSystem.cs` | new + modified (HOOK 23) | WP12-6 | 4 **public** builders (P4-D26, so they are headlessly testable). `BuildWoundDiagnostics` gates on `WoundHostComponent`, not Onyx's `SurgeryTargetComponent` (D2); `BuildOrganInfo` reads `WolfmedOrganComponent` + `OrganComponent.SlotId` (D8); `BuildChemicalInfo` swaps `MetabolitesSolutionName` → `ChemicalSolutionName`. **`BuildPartDamage` deliberately not ported** |
| `Content.Client/_Onyx/Medical/HealthAnalyzer/EllipsisLabel.cs` | same | new (vendored), verbatim | WP12-7 | sandbox-clean (`Rune`, `StringBuilder`, `EnumerateRunes` all whitelisted). Keep Onyx's `OopsConcat` trick |
| `Content.Client/_Onyx/Medical/HealthAnalyzer/HealthAnalyzerControl.xaml(.cs)` | `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml(.cs)` | new (re-authored) | WP12-7 | **P4-D25 PARALLEL.** Onyx's 622-line control cannot be vendored; its draw methods are cited nearly line-for-line inside a `_WF` panel. Prints the fracture grade Onyx carries and never renders |
| — | `Content.Client/HealthAnalyzer/UI/HealthAnalyzerWindow.xaml(.cs)` + a `_WF` partial | modified (HOOK 26) + new | WP12-7 | **3 upstream lines.** Only `WolfmedPanel` enters the window's `[GenerateTypedNameReferences]` scope. Panel hidden entirely for non-hosts, so today's window is byte-identical for them |
| — | `Content.Server/_WF/Wolfmed/Explosion/WolfmedExplosionSystem.cs` + `ExplosionSystem.Processing.cs` | new + modified (HOOK 22) | WP12-8 | **P4-D14 reverses §8.6-6.** Also closes P3-D3's plate hole inside `WoundDamageRoutingSystem` (optional `originFlag`, save/restore of `_routedModifiers`). Both CVars finally have a consumer |
| — | 8 test files | new / modified | WP12-9 | 27 tests, §6.2 |
| — | `Resources/{ServerInfo,Prototypes,Locale}/_WF/Wolfmed/…Guidebook…` + `Prototypes/Guidebook/medical.yml` | new + modified (PROTO L) | WP12-10 | Onyx's `Surgery` guideEntry skipped (id collision + D7). IPC/slime/cybernetic content dropped; dismemberment reworded for guns and lasers |

### 7.3 Deviations block to add (phase 4)

1. **HOOK 9 ships inert.** `treatmentCapabilities` can only refuse when a part profile lacks the capability, and
   `OrganicBodyPartProfile` is the only profile in the tree. Forward-compat plumbing, not a phase-4 feature.
2. **Systemic healing is never capability-gated.** Toxin, Airloss, Bloodloss, Genetic, Cellular and Radiation
   heals bypass `CanTreatPart` by construction. Onyx behaves identically; recorded so nobody debugs it.
3. **`TakeStaminaDamage` honours `Immediate` where Onyx ignores it** (P4-D5), with Onyx's `false` default.
4. **`SalicylicAcid` is dropped, not ported or renamed** (P4-D4) — WG's is an unrelated Frontier precursor.
5. **`Oxycodone`'s reaction is re-authored without `Heroin`** (P4-D2) — a Wolfgate recipe deviation.
6. **20 of Onyx's 22 medicine reagents are not ported** (Tier B/C and the virology set), three of them blocked
   on condition types Wolfgate does not have (`PressureThreshold`, `TypedDamageThreshold`, `ModifyStatusEffect`).
7. **Pain numbness / `ModifyStatusEffect` is skipped** (P4-D8). `PainNumbnessStatusEffectComponent` remains
   dead code with two readers and no writer; the legacy trait path is what actually works.
8. **Non-wound-hosts (Protogen only) lose tourniquet function entirely** (P4-D11), and the tourniquet's begin/end
   sounds lose client prediction (P4-D10).
9. **Opening a surgical incision on a wound host costs more than it did** — **on `SurgeryStepOpenIncisionScalpel`
   only**. The existing flat 10 `Bloodloss` stays *and* a `SurgicalIncisionWound` at severity 10 is opened,
   because replacing the flat charge would change non-host behaviour too (§2.7). Onyx charges only the wound.
   **`SurgeryStepCarefulIncisionScalpel` is unchanged**: it carries no damage effect in either tree, and its
   only consumers (`SurgeryTendWoundsBrute`/`Burn`) contain no clamp and no close step, so a wound there would
   never be treated. **`SurgeryStepSealTendWound` gains the `Close` treatment** — a Wolfgate adaptation with no
   Onyx counterpart (Onyx has no such step), so the four wound surgeries that end on it close their own
   incision instead of relying on the medic to run `SurgeryCloseIncision` afterwards.
10. **Surgery now hurts and now scars** (P4-D21/P4-D22) — both are new to Wolfgate, both were already fully
    built and unreachable (`WoundScarSystem` + `surgery.scar_chance` since phase 1; `PainSystem` since phase 2).
11. **Organ healing is `amount: 3`, not Onyx's 1** (P4-D23) — the one deliberate pace deviation.
12. **Organ damage is reversible only while the organ lives** (P4-D24). A destroyed organ is still replaced,
    never repaired, and `SurgeryRemoveKidneys`/`InsertKidneys` still do not exist.
13. **`SurgeryHeal<Organ>` covers 7 organs, human lineage only** (P3-D7 inherited). The analyzer's Organs tab
    is empty for every other species until phase 5.
14. **The re-attachment block lives at the surgery layer, not the body layer** (P4-D18). `CanAttachPart` stays
    ungated, so Mono prybar prosthetics, Mono bionic legs, the Goob autosurgeon and Shitmed's child-part
    generation are untouched — and a prosthetic can still be bolted to an untreated stump.
15. **`SurgeryTendWoundsEffectComponent` is not extended** (P4-D16). Onyx's severity-weighted heal bonus is a
    balance nuance Wolfgate does not take; the deep/shallow split is expressed entirely by the severity window.
16. **`OnTendWoundsCheck` is body-scoped**: a medic tending one limb keeps repeating until the whole patient is
    clean. Pre-existing Shitmed behaviour that `SurgeryTendWoundsBruteDeep` inherits.
17. **A surgically removed limb has no consequence wound** and can always be re-attached; only traumatic
    amputation blocks. Deliberate, and asserted by T-SURG-AMP-CLEAN.
18. **The analyzer's Onyx message, window and control are re-expressed, not vendored** (P4-D25), and
    `BuildPartDamage` is not ported at all (P4-D27) — WG's client already reads per-part damage directly.
19. **The analyzer prints the fracture grade Onyx carries and never renders** — a corrected upstream gap.
20. **Explosions now spread across a wound host's limbs and can sever them** (P4-D14), tunable by two CVars
    that have been dormant since phase 1. **Armour plates protect against explosions on the distributed path
    for the first time** — the P3-D3 hole is closed in the same change.
21. **`GroupHealSpecifier` is not ported** (P4-D12); PLAN.md's WP11 line was a bundling error. It carries a
    second licence (Wega, GPL-3.0) under Onyx's AGPL and should only ever be revisited alongside Vampire.
22. **Onyx's `Surgery` guidebook entry is skipped** (P4-D15) — id collision with Shitmed's, and it documents an
    unported system. All IPC/cybernetic/slime guidebook content is dropped.
23. **PLAN.md §8.2's `Scale` note was backwards and is struck** (P4-D30). Wolfgate's `Scale` is structurally
    `[0,1]`; Onyx's is the one that can exceed 1. No tuning item; `MinScale` is not ported.
24. **Cognac's pain suppression is metabolised by the stomach, not the liver.** Onyx puts its `SuppressPain`
    in a `Digestion:` group; Wolfgate has no such group, so PROTO K puts it in Cognac's only group, `Drink:`
    (`alcohol.yml:120-127`). Same amount, different metabolizer and rate — small at `amount: 0.25`, but it is
    a deviation rather than parity. The five new Tier-A reagents and Stasizium/Bicaridine/Desoxyephedrine/
    Happiness are all mapped off Onyx's `Bloodstream:` onto Wolfgate's `Medicine:`/`Narcotic:` for the same
    reason.
25. **The organ-heal surgeries do not clamp internal bleeders**, where Shitmed's organ *removal* surgeries do.
    Healing does not breach the organ, only the cavity, and Shitmed's own `SurgeryInsert*` surgeries omit the
    clamp too. The head heals **do** saw bone (`SurgeryStepSawBones`), matching every other head-organ surgery;
    the torso heals inherit the saw from `SurgeryOpenRibcage`.
26. **`SurgeryStepSealOrganWound` is reused as the organ-heal terminal step with its affix logic dormant.**
    Its `SurgeryAffixOrganStep` handlers early-return unless the surgery carries `SurgeryOrganCondition`
    with `reattaching: true`, which the heal surgeries deliberately do not. Recorded so nobody “fixes” it by
    adding that component.
27. **Phase 4 adds no pain to upstream Shitmed steps.** Onyx attaches `SurgeryStepPainInflicter` to its own
    incision steps (34 on the open incision, 17 on the careful one, 9 on the close); Wolfgate's
    `WolfmedSurgeryPainEffect` goes only on the 13 new `_WF` steps (P4-D22), because a pain component on an
    upstream step would be a fifth PROTO G addition and would charge non-hosts nothing while changing the
    marked-line budget. Surgery on a wound host still hurts, just not during the incision itself.

---

## 8. Risks, balance, and user decisions

### 8.1 What each treatment actually does, per use

| Treatment | Effect per application | Source |
|---|---|---|
| **Cognac** | `SuppressPain 0.25`, decays over 9 s, recovery ×1.1, identifier `Painkiller` | ONYX `alcohol.yml:115-123` |
| **Happiness** | `SuppressPain 0.4` / 9 s / ×1.5 | ONYX `narcotics.yml:632-638` |
| **Bicaridine** | `SuppressPain 0.75` / 18 s / ×1.75, on top of its existing `EvenHealthChange Brute -1` | ONYX `medicine.yml:153-159` |
| **Desoxyephedrine** | `SuppressPain 0.75` / 9 s / ×1.75 | ONYX `narcotics.yml:50-56` |
| **Ibuprofen** | `SuppressPain 0.5` / 27 s / ×2.5; `Brute -1`; `TakeStaminaDamage 2.4` above 10u | ONYX `medicine.yml:190` |
| **Ketorolac** | `SuppressPain 0.9` / 50 s / ×3; `Brute -0.5`; `ModifyBleed +0.15` | ONYX `medicine.yml:242` |
| **Tramadol** | `SuppressPain 1.25` / 45 s / ×3.5; Adrenaline + `IgnoreSlowOnDamage` status | ONYX `medicine.yml:412` |
| **Oxycodone** | `SuppressPain 2.0` / 60 s / ×4.5; Adrenaline status | ONYX `medicine.yml:442` |
| **Osteogen** | `MendFractures amount: 1`, `BoneFractureWound` only, **`maximumGrade: Simple`** — it cannot touch a Displaced or Comminuted fracture. Surgery is the cure for those | ONYX `medicine.yml:129` |
| **Stasizium** | adds `MendFractures amount: 10`, **all grades, all fracture prototypes** (`wounds: []`), on top of its existing 5-group −20 heal | ONYX `first_aid.yml:29-33` |
| **Tourniquet** | clamps **every** bleeding wound on the selected part to a 0f bleed multiplier, and deals `Blunt 5 + Asphyxiation 5` to that part. 0.5 s do-after. Refuses a part that is not bleeding | ONYX `healing.yml:261-289` + `TourniquetSystem` |
| **Medical patch** | transfers 1u (makeshift: every 2 s) of whatever is inside it into the target's injectable solution while stuck. Single-use, craftable from 1 Cloth or 4 WebSilk | Onyx defaults |
| **`SurgeryStopBleeding`** | `ReduceBleeding(worst bleeder, 10)` per step, repeatable. No incision | §2.6 V1 |
| **`SurgeryStopInternalBleeding`** | removes the `InternalBleedingWound` outright in **one** step (`Amount = MaxValue`). Needs an incision | §2.6 V2 |
| **`SurgeryMendFracture`** | `Reduced` (fracture effect → 25 %) then `Mended` (effect → 0 and the wound is removed). Needs an incision | §2.6 V3 |
| **`SurgeryHealAmputationConsequence`** | removes the wound in **one** step; it is the **only** cure (`damageTypes: {}` means nothing else can touch it) | §2.6 V2 |
| **`SurgeryHeal<Organ>`** | +3 organ HP per 2 s step, 5 repeats from zero to full. Only while the organ still lives | §2.6 V4, P4-D23 |
| **`SurgeryTendWounds*Deep`** | unchanged Shitmed tend maths, now reachable only above 100 wound severity | P4-D16/P4-D19 |

### 8.2 Surgery durations, end to end

| Surgery | Steps | Nominal duration (before tool speed and the ×4 self-surgery penalty) |
|---|---|---|
| `SurgeryStopBleeding` | 1 × 2 s, repeatable | 2 s per application; a severity-20 bleeder needs ~2 |
| `SurgeryStopInternalBleeding` | incision (2+2+2) + 4 + seal | ~12 s |
| `SurgeryMendFracture` | incision (6) + 3 + 3 + seal | ~14 s |
| `SurgeryHealAmputationConsequence` | incision (6) + 4 + seal | ~12 s |
| `SurgeryHeal<Organ>` (torso) | ribcage + 5 × 2 + seal | ~20 s from a fully wrecked organ — the saw is already spent on `SurgeryOpenRibcage` |
| `SurgeryHeal{Brain,Eyes}` (head) | incision + **saw (4)** + 5 × 2 + seal | ~24 s — `SurgeryStepSawBones` prefixed in this revision (§4 WP12-5), matching `SurgeryRemove/InsertBrain` and `…Eyes` |
| `SurgeryTendWounds*Deep` | incision (6) + repeats + seal | open-ended (the repeat is body-scoped, §7.3 item 16). The incision it opens is now closed by the seal step itself (PROTO G) |
| `SurgeryTendWounds{Brute,Burn}` (shallow, pre-existing) | careful incision (3) + repeats + seal | **unchanged in every respect** — the careful incision opens no wound, so these two surgeries are exactly what they are today on hosts and non-hosts alike |

`Omnimed` carries every tool at `speed: 2`, halving each step. The `Hemostat` doubles as `Tweezers` and
`Tending`, so the whole wound set needs **Scalpel + Retractor + Hemostat + BoneSetter + BoneGel + Cautery** —
all already in a standard surgery kit.

### 8.3 Balance notes that deserve a second look before playtest

* **The painkiller ladder tops out at 2.0 suppression on a 60-second decay** (Oxycodone). Phase 2's pain HUD
  and pain-shock thresholds were tuned with **no** painkillers in the game. Expect the shock threshold to feel
  loose once the ladder lands; that is a tuning pass, not a bug.
* **Stasizium becomes a universal fracture cure** (`wounds: []`, `amount: 10`, all grades) and it is stocked in
  a combat medkit and an autoinjector. If fractures should require a surgeon for the worst grades, the knob is
  `maximumGrade` on PROTO H's block, not the surgery.
* **Explosions change shape, not just severity** (P4-D14): today a blast lands entirely on one random part;
  after WP12-8 it spreads across every attached part with `explosion.damage_variation` 2.0 and a
  `explosion.wounding_multiplier` of **4.0** on wound severity. Four is a large multiplier that has never run
  live in this fork. **If sign-off is given but the result feels wrong in playtest, retune the CVar before
  reverting the hook** — the mechanism is sound, the default is Onyx's.
* **The real incision now bleeds** (P4-D21). `SurgicalIncisionWound` at severity 10 with `rate: 0.1` and
  `awakeMultiplier: 3` is a raw 3.0/s on an awake patient, clamped by `MaxBleedAmount` 10, and it runs from
  `SurgeryStepOpenIncisionScalpel` until `SurgeryStepClampBleeders` — the very next step but one in
  `SurgeryOpenIncision`. Surgery on a conscious patient was already a bad idea; it is now measurably worse.
  **The two shallow tend surgeries are unaffected**: they use the careful incision, which opens no wound
  (§2.7). That is deliberate — they are the commonest surgeries in the game and they have no clamp step.

### 8.4 Genuine user decisions — needed before the named WP starts

| # | Question | Default if no answer | Gates |
|---|---|---|---|
| **1** | **Explosion amputation (P4-D14).** DECISIONS §8.6-6 said it stays out; DECISIONS P4-6 allows it back if the hook is small, and it is (3 marked lines + one `_WF` system + a 3-line change inside an already-marked vendored file). It also closes the P3-D3 hole that currently makes armour plates useless against explosions on the distributed path. But it changes live combat balance on a gun-PvP server: grenades gain the ability to sever limbs, and blast damage stops concentrating on one part. | **Ship it (WP12-8), gated on T-EXPLOSION-PLATE.** Alternative: skip WP12-8 entirely — nothing else in phase 4 depends on it, and the dormant mechanism stays dormant. | WP12-8 |
| **2** | **Organ heal rate (P4-D23).** Onyx's `amount: 1` means 15 repeats of a 2 s step per organ; the plan ships `amount: 3` (5 repeats) as a marked balance deviation. | **Ship `amount: 3`.** Reverting is one line per organ step, at any time. | WP12-5 |
| **3** | **Surgery scarring and incision bleeding (P4-D21), re-scoped in this revision.** Two new effect components and **four** marked prototype additions make `surgery.scar_chance` (0.35, dormant since phase 1) live and make an incision a real wound. **The shape being approved is now narrower than the earlier draft's:** the incision that bleeds is the `SurgeryOpenIncision` one only; `SurgeryStepCarefulIncisionScalpel` is untouched, so the two tend-wounds surgeries keep their clean careful incision and cannot leak a bleeder; and `SurgeryStepSealTendWound` closes what the wound surgeries opened. `surgery.md`'s "~20 lines" costing was for one third of the mechanism. | **Ship the four-prototype chain.** Alternative: ship none, drop T-SURG-SCAR, and record that Wolfgate surgery never scars. **Do not ship a partial chain**, and do not put the wound effect on the careful incision. | WP12-5 |
| **4** | **Reagent scope (P4-D2).** Tier A is 5 new reagents + 5 marked YAML blocks. Tier B (`Probital` + `Mitogen`) exercises `StaminaDamageCondition` and both `TakeStaminaDamage` modes but adds 2 more reagents, 2 recipes and a `ForcedSleeping`/`Stuttering` status chain. | **Tier A; take Tier B inside WP12-2 only if the package is otherwise green.** Tier C is a separate content pass. | WP12-2 |
| **5** | **Analyzer UI shape (P4-D25).** PARALLEL (a `_WF` panel, **5** upstream lines after the HOOK 26 revision — 3 XAML, 2 code — a 4-tab strip inside the panel, no geometry change) vs GRAFT (inline markup, ~70 upstream lines, and a window-width change that affects every scan in the game including non-hosts). | **PARALLEL with tabs inside the panel (G2).** Take GRAFT only if one seamless panel matters more than the conflict surface, and accept widening `SetWidth="350"` as a recorded deviation. | WP12-7 |
| **6** | **An organ-damage examine line.** Confirmed: **Onyx has none** — its only organ readout is the analyzer tab. Adding one would be a Wolfgate invention (~10 lines in the existing `HealthExaminableSystem.PartStatus.cs` detail list, gated to self-examine or to a surgeon). | **No.** The analyzer is the readout; phase 2 already shipped everything Onyx's examine has. | — |
| **7** | **`treatmentCapabilities` annotation (P4-D7).** Annotate nothing now, or annotate the cable coil (`cable_coils.yml:37`, `damageContainers: [Silicon]`) as `[Mechanical, Electrical]` — provably behaviour-neutral today. | **Annotate nothing; record the cable coil as the phase-5 first action.** | WP12-1 |
| **8** | **Pain numbness (P4-D8).** Skip and record (no in-scope consumer, the trait path already works, and it would hand meth full HUD-blindness immunity), or port the ~½-day chain anyway. | **Skip and record.** If taken, the minimum honest scope is `ModifyStatusEffect` + a separate `_WF` action enum + 2 prototypes + the Desoxyephedrine block as a **marked Wolfgate balance addition** + a test — and **not** `TraitStatusEffectPainNumbness`, which would give one character two numbness sources. | — |

### 8.5 Risks that will silently ship something broken if ignored

1. **Renaming any of the four reagent effect classes** → every ported `!type:` breaks as a prototype-load
   failure at server start, not a compile error (`!type:` resolves by bare `Type.Name`).
2. **Forgetting `WithTreatmentCapabilities` in HOOK 9** → `CanTreatPart` defaults to `true`, the capability
   scope never engages, and the feature is invisible-shaped rather than broken. T-REAGENT-CAP-NO is the gate.
3. **Nesting `WithTreatmentCapabilities`** → `_treatmentCapabilities[body] = …; try { … } finally { Remove(body); }`
   (`:136-147`) is a plain dictionary, so an inner `finally` clears the outer scope and the rest of the outer
   heal runs unscoped. Phase 4 must not nest it. `HealingSystem.Wolfmed` deliberately does not use it, so today
   there is no nesting. WP12-8's `_routedModifiers` change has the same shape and is why it saves and restores
   rather than removing.
4. **Copying Onyx's metabolism group header** (`Bloodstream:`, or `Digestion:`) into a Wolfgate reagent → a
   `ProtoId` that does not resolve, i.e. a **prototype-load / Release-lint failure for the whole file**; and if
   it were tolerated, five inert reagents. Wolfgate's nine groups are `Poison, Medicine, Narcotic, Alcohol,
   Food, Drink, Gas, PlantMetabolisms, Cryogenic`. The correct group is named in **PROTO H–K and in WP12-2's
   translation table, whose first row now covers the group header as well as the `!type:` tags** — the five
   brand-new reagents (item 5) are the ones the earlier draft left uncovered. `T-REAGENT-PROTOTYPE-SANITY`
   asserts the mapping rather than trusting it, including the valid-but-wrong case the lint cannot catch
   (`SuppressPain` landing in Desoxyephedrine's `Poison:` block instead of its `Narcotic:` one).
5. **Mistranslating a `!type:`** in a copied reagent block → Release YAML lint failure at best, a silently
   different effect at worst. WP12-2's table is not optional reading.
6. **Adding the `Tourniquet` tag** → undeclared tag, Release lint failure (P4-D9).
7. **Leaving `TourniquetSystem` in `Content.Shared`** → it will not compile; `WoundBleedingSystem` is a
   server-assembly class.
8. **Creating a second `id: Tourniquet` entity** instead of editing in place → duplicate prototype id.
9. **Putting a surgery visibility condition on a *step*** → `RefreshUI` raises `SurgeryValidEvent` on the
   **surgery** singleton only, so the condition only bites at do-after completion. Onyx puts one on a step;
   copying that placement is the bug.
10. **Putting a `SurgeryStepCompleteCheckEvent` handler in `Content.Server`** → `SurgeryBui.cs:281` runs
    `GetNextStep` → `IsStepComplete` client-side, so the client marks the step permanently complete and
    highlights the wrong row. Every S4–S8 handler is shared for this reason.
11. **Putting a `SurgeryStepEvent` handler in `Content.Shared`** → the client half will not compile
    (`OrganHealthSystem`, `WoundBleedingSystem`).
12. **Ignoring `ReductionMinimumGrade` in the fracture check** → `SurgeryMendFracture` stalls forever on a
    Hairline fracture, the commonest grade (P4-D20).
13. **Hooking `CanAttachPart`** → Mono prybar prosthetics and bionic legs silently `QueueDel` the replacement
    part on exactly the stump they exist for (P4-D18). The T-REATTACH-BLOCKED assertion that `CanAttachPart`
    stays `true` is the guard.
14. **Replacing `SurgeryDamageChangeEffect`'s flat Bloodloss on the incision step** → non-wound-hosts lose the
    incision cost entirely, a D2 breach. PROTO G **adds beside**, it does not replace (§2.7), and P4-D21's
    decision cell says so — an earlier draft said "replaces" there and "adds beside" everywhere else.
15. **Putting `WolfmedSurgeryIncisionWoundEffect` on `SurgeryStepCarefulIncisionScalpel`** → a permanently
    bleeding, `SeparateInstances`-stacking wound on **every tend-wounds operation**, the commonest surgery in
    the game, because its only two consumers contain no clamp and no close step. PROTO G is four additions on
    `:9`, `:31`, `:168`, `:359` and the careful incision at `:303` is **not** one of them.
15a. **Shipping only the incision-wound effect without the clamp and close effects** → the same stacking
    bleeder, one per operation (P4-D21). All four or none.
16. **Building on `SurgeryCompletedEvent`** → it is an empty struct that nothing raises.
17. **Reusing `SubSurgery<T>`** in a `_WF` system → it registers both step events at once, which is exactly
    what phase 4's server/shared split must avoid.
18. **Adding a `Groin` key anywhere** — analyzer row, surgery `part:`, or locale — → D9; `GetValidParts()` has
    it commented out and `ConvertTargetBodyPart` folds it to Torso.
19. **Making the analyzer builders `private`** → T-AN-* has no assertion point, since `UpdateScannedUser` ends
    in a network send (P4-D26).
20. **Gating `BuildWoundDiagnostics` on `SurgeryTargetComponent`** (Onyx's gate) → a borg or a Protogen returns
    an always-empty dict, which the client renders as "no findings" instead of "unavailable".
21. **Forgetting `using System.Linq;` in `EvenHealthChange.cs`** → HOOK 9(b) does not compile.
22. **Disturbing `WoundDamageRoutingSystem`'s `before: [typeof(SharedArmorPlateSystem)]` registrations** while
    editing that file in WP12-8 → armour ordering breaks for every wound host, not just explosions.
23. **Asserting "reagent X does not affect wounds before HOOK 9"** → it will fail. GUARD D already routes
    reagent healing into wounds; the tests must assert the **capability scope**, not wound healing per se.

### 8.6 Blockers

| Id | Item | Status |
|---|---|---|
| **B-1** | Two analyst reports both claimed **HOOK 22** for different upstream files | **RESOLVED** by P4-D28's renumbering (22/23/24/25/26). Highest live number in the tree is 21 |
| **B-2** | `tests.md` §8 decision 2 asks to authorise a `CanAttachPart` guard; `surgery.md` §4.1 says that breaks four other callers | **RESOLVED in favour of `surgery.md` (P4-D18)** — all five `AttachPart` callers re-grepped here. DECISIONS P4-3's prose is superseded, and `tests.md`'s T-REATTACH-BLOCKED is rewritten to assert on `SurgeryValidEvent` |
| **B-3** | `tests.md` T-SURG-TEND-*-DEEP assumes an Onyx severity branch grafted onto `SurgeryTendWoundsEffectComponent`; `surgery.md` recommends no change to it | **RESOLVED in favour of `surgery.md` (P4-D16)**; the two tests are replaced by T-SURG-WINDOW |
| **B-4** | `tests.md` §4 says five Onyx surgery component names are "vendorable verbatim"; `surgery.md` §3.2 renames all of them | **RESOLVED in favour of `surgery.md` (P4-D17)** — currently free, latently colliding on a Monolith merge |
| **B-5** | `tools.md` §1.3's Tourniquet YAML adds a `Tourniquet` **tag** | **RESOLVED: dropped (P4-D9).** No such tag prototype exists in WG; an undeclared tag fails the Release lint |
| **B-6** | `surgery.md` U-4(A) prices the scar chain at "~20 lines + 2 marked YAML lines" | **RESOLVED: corrected (P4-D21).** That is one of three effects; shipping it alone leaves a permanently bleeding wound. All three or none |
| **B-7** | `surgery.md` §3.4 lists `WoundBleedingSystem.TreatPart` as returning `bool` | **RESOLVED: corrected.** It returns `int` (`:257`). Cosmetic, but it would be a compile error in a copied line |
| **B-8** | `surgery.md` U-3's two-step ladder does not account for `CanTreat`'s `ReductionMinimumGrade` gate | **RESOLVED: corrected (P4-D20).** Without the correction the mend surgery stalls on Hairline fractures |
| **B-9** | `Heroin` is outside P4-1's scope but Onyx's `Oxycodone` reaction needs it | **RESOLVED: re-author the recipe (P4-D2)**, recorded as a deviation. Do not widen the reagent scope |

| **B-10** | CRITIQUE4 B1: PROTO G put the incision wound on `SurgeryStepCarefulIncisionScalpel`, whose only two consumers have no clamp and no close step | **RESOLVED: dropped (option 1) and the seal step gained `Close`.** Re-verified in the tree: the careful incision carries no damage effect at all in either WG or Onyx, and `grep -n` over `surgeries.yml` shows it paired only with `SurgeryStepSealTendWound`. §2.7, PROTO G, P4-D21, §7.3 deviation 9, §8.2, §8.3, §8.4 decision 3 and §8.5 traps 14/15 are all re-written together, and `T-SURGERY-PROTOTYPE-SANITY` gains a prototype-level closed-loop invariant |
| **B-11** | CRITIQUE4 B2: the five new Tier-A reagents would have carried Onyx's `Bloodstream:` metabolism group, which does not exist in WG | **RESOLVED: a group row added at the top of WP12-2's translation table**, §8.5 risk 4 re-scoped to cover item 5, PROTO K corrected to `Drink:` (CRITIQUE4 M5), and `T-REAGENT-PROTOTYPE-SANITY` added to assert every shipped group key resolves |

**No outstanding blocker.** Every phase-4 sub-area has a decided path, and the only item awaiting a real
user answer before its package can start is §8.4 decision 1 (WP12-8).

### 8.7 Explicitly out of scope for phase 4

**Reagents:** `DistributedHealthChange` (P4-D6 — not needed, not deferred); `PressureThreshold`,
`TypedDamageThreshold` (and the `DamageableSystem.GetAllDamage` it wants), `ModifyStatusEffect`,
`ImmunityModifier`, `DiseaseProgressChange`, `ChemCureDnaDisease`; `MinersSalve`, `Luxurium`, `Oxandrolone`,
`SalicylicAcid`, `SilverSulfadiazine`, `StypticPowder`, `Synthflesh` (which additionally drags in an
AGPL-3.0-or-later header with 18 SPDX lines), `VitriumFroth`, `SerakaExtract`, `Mitotrophin`, `Atropine`;
the four virology reagents and `Mutadon`; `Narcotics/opioids.yml`, `Narcotics/capsaicin.yml`,
`Toxins/bitrunning.yml`; `EntityEffect.MinScale`; any `treatmentCapabilities` annotation (P4-D7).

**Tools:** `GroupHealSpecifier` (P4-D12); any medical-patch fill/vending/cargo/loadout placement (P4-D13);
`MedkitCombatFilled`; dual-component tourniquet composition for non-hosts (P4-D11); Onyx's `ru-RU` locale copies.

**Surgery:** Onyx's `SharedSurgerySystem.*` (11 partials + 4 server files) and `_Onyx/Surgery` (D7); the
`cybernetic:` fracture variant and `MechanicalSurgeryStepComponent`/`CyberneticsComponent`;
`StepInvalidReason.AmputationConsequence` and its popup (the surgery is hidden, not greyed);
`SurgeryTargetPartContextComponent` (WG's `SurgeryStepEvent` already carries `Part`); the Tongue, Ears and
Appendix organ surgeries (no WG body graph slots them); `SurgeryRemoveKidneys`/`SurgeryInsertKidneys`;
extending `SurgeryTendWoundsEffectComponent` (P4-D16); any `CanAttachPart` edit (P4-D18);
`SurgeryCompletedEvent`-driven anything.

**Diagnostics:** Onyx's `HealthAnalyzerUiState` message, `HealthAnalyzerWindow`, `HealthAnalyzerControl` and
`HealthAnalyzerStatusDoll` (P4-D25); `BuildPartDamage` (P4-D27); Onyx's disease block and its four locale keys;
crew monitor and suit sensors (confirmed: no Onyx wound data); an organ-damage examine line (confirmed: Onyx
has none); `Content.Client/Medical/Cryogenics` (WG has no cryo-pod window); any new BUI message.

**Other:** pain numbness / `StatusEffectPainNumbness` / `TraitStatusEffectPainNumbness` (P4-D8); prediction of
wound-host routing (D35); `HealingMultiplier` tuning (on record since phase 2); the phase-3 `HealthChange`
universal-modifier bug; non-organic `bodyPartProfile`s and everything gated behind them (phase 5); a refund of
P3-D1a's vital-part charge on re-attachment; Onyx's `Surgery` guidebook entry and all IPC/cybernetic/slime
guidebook content (P4-D15).

---

## 9. Work-package table (final)

| WP | Title | Model | Files |
|---|---|---|---|
| **WP12-0** | Medical patch — 2 vendored server files, 2 prototypes, 1 RSI, 1 locale; zero upstream edits | **sonnet** | 6 |
| **WP12-1** | Reagent effect classes + HOOK 9 — 4 old-style rewrites, 1 locale, 2 upstream hooks | **opus** | 8 |
| **WP12-2** | Reagent content Tier A — 4 marked reagent edits, 5 new reagents, 5 recipes, locale | **opus** | 9 |
| **WP12-3** | Tourniquet — vendored component, relocated server system, PROTO D in-place swap, PROTO E, locale | **sonnet** | 6 |
| **WP12-4** | Wound surgery C# — 10 components, 2 systems (15 subscriptions), the `_WF` partial, EXT 1, HOOK 24 + HOOK 25 | **opus** | 5 |
| **WP12-5** | Wound surgery content — 13 surgeries, 13 steps, PROTO F, PROTO G's incision-scar chain, locale | **opus** | 6 |
| **WP12-6** | Analyzer payload + server — 3 vendored types, the message extension, 4 public builders, HOOK 23 | **opus** | 7 |
| **WP12-7** | Analyzer client — `EllipsisLabel`, the `_WF` panel, the window partial, HOOK 26, 3 locale files | **opus** | 7 |
| **WP12-8** | Explosion amputation — the `originFlag` passthrough, `WolfmedExplosionSystem`, HOOK 22 | **opus** | 4 |
| **WP12-9** | Tests — 30 tests across 8 files (2 restored skips, 28 new) | **opus** | 8 |
| **WP12-10** | Guidebook + docs reconcile — 2 XML, 1 prototype, PROTO L, 1 locale, `WOLFMED_PLAN4.md`, manifest, status | **sonnet** | 7 |

File counts cover new `_WF` files, vendored files, upstream hook files, prototypes, locale and tests. They
**exclude** `Docs/Wolfmed/WOLFMED_MANIFEST.md`, which every package appends to (ground rule 6), except in
WP12-10 where the docs *are* the work.

**Model rationale.** Opus for WP12-1 (two upstream effect files where a discarded-modifier "fix" or a missing
`using` is a silent balance change, and four class names are load-bearing), WP12-2 (a 13-row tag-translation
table where one wrong tag is a silent no-op and one wrong metabolism group is an effect that never fires),
WP12-4 (15 directed subscriptions, a mandatory server/shared split, two upstream hooks on a file Wolfmed has
never touched, and the `ReductionMinimumGrade` correction), WP12-5 (26 new prototype ids, a four-part
prototype chain that is wrong unless all four land, and the one deliberate balance deviation), WP12-6 and
WP12-7 (a wire-format change with a second construction site, the reserved-name trap, and a 200-line control
re-expression), WP12-8 (a combat-balance hook plus a non-reentrant dictionary) and WP12-9 (every literal is a
prediction until measured — P2-D16 — and three assertions only mean anything because of one fixture line).
Sonnet for WP12-0 (a verbatim vendor with no coupling), WP12-3 (a mechanical dependency swap and a YAML block
replacement, both fully specified in §2.2 and PROTO D) and WP12-10 (content editing and a reconcile).

---

## 10. Revision notes (CRITIQUE4 pass)

This section records what changed between the first PLAN4 draft and this one, why, and which critique findings
were **rejected or narrowed** on evidence. Everything below was re-grepped or re-read in **WG** (HEAD
`6329d204e3`) or **ONYX** (pin `2f5bab9`) during this revision; nothing was taken on the critique's word.

### 10.1 Blockers — both accepted

| Id | Verdict | What changed |
|---|---|---|
| **B1** — PROTO G leaves a stacking bleeder on tend-wounds | **ACCEPTED, option 1 plus one addition** | `SurgeryStepCarefulIncisionScalpel` is dropped from PROTO G. Verified: it carries **no** damage effect (`surgery_steps.yml:303-316` read in full — `SurgeryStep` + `Sprite` + `SurgeryStepEmoteEffect`), Onyx's copy carries no bleed effect either (`ONYX surgery_steps.yml:958-970`), and its only consumers are `SurgeryTendWoundsBrute` (`surgeries.yml:285-294`) and `SurgeryTendWoundsBurn` (`:298-307`), whose step lists reach neither `SurgeryStepClampBleeders` nor `SurgeryStepCloseIncision` (`grep -n` → exactly `:291,:293,:304,:306`). **Beyond the critique's fix**, `SurgeryStepSealTendWound` (`:359`) gains `{ treatment: Close }`, because the four wound surgeries that *do* open a real incision end there rather than on `SurgeryStepCloseIncision` and would otherwise leave a clamped-but-open incision wound until the medic separately ran `SurgeryCloseIncision`. PROTO G stays at four additions, on `:9`, `:31`, `:168`, `:359`. §2.7 (which falsely claimed both incision steps carried `SurgeryDamageChangeEffect`), P4-D21, §3.2, §7.2, §7.3 deviation 9, §8.2, §8.3, §8.4 decision 3 and §8.5 traps 14/15 were re-written together, and `T-SURGERY-PROTOTYPE-SANITY` gained a prototype-level closed-loop invariant so the bug class cannot return |
| **B2** — the five new reagents keep Onyx's `Bloodstream:` group | **ACCEPTED** | WP12-2's translation table gains a **first row covering the `metabolisms:` group header**, not just `!type:` tags. Verified: WG's complete group set is `Poison, Medicine, Narcotic, Alcohol, Food, Drink, Gas, PlantMetabolisms` (`Chemistry/metabolism_groups.yml:3,7,11,15,19,23,28,33`) + `Cryogenic` (`_NF/…:3`); `grep -rn "^    Bloodstream:$" Resources/Prototypes` → **0**; the key is `ProtoId`-validated (`ReagentPrototype.cs:155`); and all five Onyx Tier-A reagents are under `Bloodstream:` (`ONYX _Onyx/Reagents/Medicine/medicine.yml:138,204,251,421,451`). §8.5 risk 4 re-scoped, and `T-REAGENT-PROTOTYPE-SANITY` added |

### 10.2 Majors

| Id | Verdict | Note |
|---|---|---|
| **M1** | **ACCEPTED** | `HealthAnalyzerScannedUserMessage.cs` added to §3.2 as **EXT 2** (read in full: upstream, only `// Shitmed Change` and `// Frontier` markers). §3.4 corrected **45 → 46** (19 files) |
| **M2** | **ACCEPTED** | WP12-2 item 8 deleted; WP12-2 drops 10 → 9 files in §4 and §9. Verified `ChemDispenserStandardInventory` (`Catalog/ReagentDispensers/chemical.yml:1-22`) lists **jug entity ids for base elements**, `grep -c Bicaridine` → 0. All reaction precursors independently re-confirmed present (`Heroin` → 0 hits, which is why P4-D2 re-authors Oxycodone) |
| **M3** | **ACCEPTED** | P4-D21's decision cell said "replaces" while §2.7, PROTO G and trap 14 said "adds beside". The cell now says **adds beside**, and trap 14 names the contradiction so it is not reintroduced |
| **M4** | **ACCEPTED** | `T-REAGENT-PROTOTYPE-SANITY` added to WP12-9 (prototype-only, ~35 lines): asserts the group key for all 10 touched reagents, that every group key in `_Onyx/Reagents/**` resolves, and that Desoxyephedrine's `SuppressPain` is in `Narcotic:` not `Poison:` — the valid-but-wrong case no lint can catch |
| **M5** | **ACCEPTED** | PROTO K now names **`Drink:`**. Verified: WG Cognac (`alcohol.yml:106-127`) has `Drink:` and nothing else, and redeclares the block rather than inheriting `BaseAlcohol`'s (`base_drink.yml:54-61`); Onyx uses `Digestion:` (`ONYX alcohol.yml:111-122`), which WG lacks. Recorded as §7.3 deviation 24 |
| **M6** | **ACCEPTED** | All 26 new prototypes must carry `name:` and `categories: [ HideSpawnMenu ]`. Verified: `SurgeryBase`/`SurgeryStepBase` set `categories` but no `name:`, and every shipped child re-states both. `T-SURGERY-PROTOTYPE-SANITY` asserts it |
| **M7** | **NARROWED — accepted for the head, rejected for the torso** | The critique is right that the heal surgeries skipped a gate every Shitmed organ surgery uses, but wrong that this applies to the torso. `SurgeryOpenRibcage` (`surgeries.yml:34-45`) **is itself** `[ SurgeryStepSawBones, SurgeryStepPriseOpenBones ]`, and `OnToolCheck` (`SharedSurgerySystem.Steps.cs:185-198`) marks a step whose `add:` components are already present as complete — which is why Shitmed's torso `Remove*` can list a second saw harmlessly. So the five torso heals need no change and §8.2's ~20 s stands. The **two head heals** are `requirement: SurgeryOpenIncision`, which does not saw, while `SurgeryRemove/InsertBrain` and `…Eyes` all do — `SurgeryStepSawBones` is prefixed to both, §8.2 gains a head row at ~24 s. `SurgeryStepClampInternalBleeders` stays out and is now **recorded** (§7.3 deviation 25) with Shitmed's own `SurgeryInsert*` as precedent. **New finding while checking this:** `SurgeryStepSealOrganWound` carries `SurgeryAffixOrganStep`, whose handlers early-return unless the *surgery* carries `SurgeryOrganCondition { reattaching: true }` (`Steps.cs:587-609`) — the heal surgeries do not, so reuse is safe and the step is a plain cautery. Recorded as deviation 26 |
| **M8** | **ACCEPTED and extended** | `PopulateWolfmed(msg)` becomes the **first** statement of `Populate` (`:112`), plus `WolfmedPanel.Visible = false;` in the early-return block (`:119-122`) for the out-of-PVS case. **Extended:** the plan's "swap `GroupsContainer`'s visibility" was not implementable — `GroupsContainer` (`HealthAnalyzerWindow.xaml:301`) is the inner `BoxContainer` and hiding it leaves an empty expanded panel, while its parent `PanelContainer` (`:288`) has no `Name=`. HOOK 26 therefore adds `Name="WolfmedDamageGroupsPanel"` there and the panel ships a **4**-tab strip (Damage / Wounds / Organs / Chemicals). HOOK 26 is now **5 marked lines**, still PARALLEL, still only two new names in the window's scope (which holds 31 `Name=` attributes today) |
| **M9** | **ACCEPTED** | `T-EXPLOSION-WRAPPER` added: `TryApplyExplosionDamage` returns `true` and spreads across ≥2 parts on a host, `false` on a non-host; plus a WP12-8 checklist grep that `ExplosionSystem.Processing.cs` contains `_wolfmedExplosion.TryApplyExplosionDamage`. Call site re-verified at `:470-473` |

### 10.3 Minors

* **m1 — accepted.** PROTO F corrected to `:294`/`:307` (`grep -n "SurgeryWoundedCondition" surgeries.yml`). The old `:285`/`:298` were the `id:` lines.
* **m2 — accepted.** **10** `SurgeryAttach*` surgeries, not 11. P4-D18 and HOOK 25 corrected.
* **m3 — rejected as stated; corrected to a third number.** `git ls-tree -r --name-only HEAD` on `medical_patch.rsi` returns **22** entries (21 PNG + `meta.json`), enumerated in WP12-0. The plan's "18" and the critique's "23 PNGs + meta.json = 24" are both wrong. The licence/copyright note is right and stands.
* **m4 — accepted.** PROTO L's citation corrected to `Guidebook/medical.yml:5-12` (`MedicalDoctor` at `:7`, `Chemist` at `:8`).
* **m5 — accepted in substance, rejected on the warning.** `SharedHandsSystem.IsHolding` and `TryPickup` added to §2.0. But WG's overload is `IsHolding(EntityUid, EntityUid?, [NotNullWhen(true)] out Hand?, HandsComponent? = null)` (`:285`), and Onyx's call sits inside `if (_hands.IsHolding(…))`, so nullable flow analysis proves `hand` non-null at the `TryPickup(…, Hand hand, …)` overload (`Pickup.cs:91`). **No CS8604**; `MedicalPatchSystem.cs` really is verbatim, 0 edits, 0 warnings.
* **m6 — accepted.** `PlantAdjustWeeds` / `PlantAdjustHealth` added to WP12-2's table as **SAME** (`Content.Server/EntityEffects/Effects/PlantMetabolism/PlantAdjustWeeds.cs:7`, `PlantAdjustHealth.cs:6`; Ibuprofen's block at `ONYX medicine.yml:198-202`).
* **m7 — accepted in part, corrected in part.** The no-feedback point is accepted: the guidebook paragraph and `WOLFMED_STATUS.md` now say the attach surgeries are **hidden, not greyed**. But "one untreated stump hides all ten" is **wrong**: the attach surgeries' `SurgeryPartCondition` splits them — six are `part: Torso` (Head, LeftArm, RightArm, LeftLeg, RightLeg, Hands), two are `part: Arm` (Left/RightHand) and two are `part: Leg` (Left/RightFoot). A torso stump hides **six**.
* **m8 — resolved by B1.** §8.2's tend row and §8.3's incision bullet are re-written for the narrowed chain, and §8.2 gains the head-organ row from M7.

### 10.4 Claims from the critique re-verified and confirmed (no change needed)

Spot-checked independently rather than inherited: the `!type:` and component-registration collision audits
(§5.3), the 20-pair subscription audit (§5.1 — `SharedSurgerySystem.cs:67,68` and
`SharedSurgerySystem.Steps.cs:49` are exactly where §5.2 says), the shared/server split of the two step events
(`SurgeryBui.cs:281` does call `GetNextStep`, so `Steps.cs:47`'s "Check DOES only run on the server side"
comment is stale — **quote that in WP12-4's brief**), the `NetSerializable` wire change,
`EntityEffect`'s two abstract members (`EntityEffect.cs:33,48`) and `EntityEffectReagentArgs`'s 8-parameter
constructor (`:109-124`), `PROTO D`'s in-place swap (WG `healing.yml:267-298` vs `ONYX :262-292`, byte-comparable
apart from the `Tourniquet` tag WG must not add), `CanTreat`'s `ReductionMinimumGrade` gate
(`WoundFractureSystem.cs:186-193` + `reductionMinimumGrade: Simple` at `wounds.yml:47`), `TreatPart` returning
`int` (`:257`), `OrganHealthSystem.SetHealth`/`ChangeHealth` (`:65`/`:80`), `GetSingleton` being `public`
(`:349`), all four SuppressPain amounts against Onyx, and every `§2.0` symbol row.

### 10.5 Net effect on the phase

No change to the phase's shape, package order, model assignments, or any decision in `DECISIONS.md`. Counts
that moved: upstream files **45 → 46**; WP12-2 files **10 → 9**; WP12-9 tests **28 → 30** (the draft's prose said 27; the table had 28 rows); HOOK 26 **3 → 5**
marked lines; PROTO G still 4 additions but a different four. §8.4's user decisions are unchanged except that
**decision 3 is re-presented with the narrowed chain** — approving it no longer approves a bleeder on the
commonest surgery in the game.
