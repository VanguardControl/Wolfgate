# WOLFMED PHASE 3 — implementation plan (lead architect)

**Onyx pin:** `2f5bab9946539cbe083010c9ae6fbc59b47ae377`, sparse reference at `C:/Users/jzo12/Documents/Wolfmed/onyx` (**ONYX**).
**Wolfgate worktree:** `C:/Users/jzo12/Documents/GitHub/Wolfgate/.claude/worktrees/rules-motd-updates-11c89c`
(**WG**), branch `clanker/wolfmed-port-orchestration-454c3d`, phases 1 and 2 committed (`1171e02fb6`),
RobustToolbox 277 junctioned at `WG/RobustToolbox` — **never touched**.

This document is to phase 3 what `PLAN2.md` is to phase 2. It supersedes the five phase-3 analyst reports
(`amputation.md`, `organs.md`, `armour.md`, `visuals.md`, `tests.md`) wherever they disagree; every overruling
is given inline with the evidence that settled it. Agents follow this file literally; the analyst reports are
evidence, not instructions.

**Ground rules (unchanged from PLAN.md/PLAN2.md, restated because they are binding):**

1. Vendored Onyx code keeps its Onyx path under `_Onyx/`, its Onyx header (where it has one — note that
   `AmputationSystem.cs` has **no** licence header in Onyx; do not invent one) and its Onyx namespace. Every
   in-file change is marked `// WOLFGATE` with a one-line reason.
2. New Wolfgate code lives under `_WF/Wolfmed`. `_WF` style: no licence header, `/// <summary>` one-liners,
   `[Dependency] private X _x = default!;` (no `readonly`).
3. Upstream Wolfgate files get one- or two-line `// WOLFGATE` hooks only, **except the sites §3 explicitly
   authorises**. Nothing outside §3 may be edited in an upstream file without escalating.
4. **Never** add a directed subscription for a `(Component, Event)` pair without checking §5 first. RT throws
   `Duplicate Subscriptions for comp=…, event=…` at
   `WG/RobustToolbox/Robust.Shared/GameObjects/EntityEventBus.Directed.cs:407,419` — a server-start crash.
5. Every WP ends with the build checkpoint: `dotnet build Content.Server`, `dotnet build Content.Client` **and**
   `dotnet build Content.IntegrationTests` all green (0 errors), `-c DebugOpt`. YAML lints in **Release** only
   (`ErrorNode` crashes the linter elsewhere).
6. **Packages run SEQUENTIALLY in the one worktree** and each appends its own rows directly to
   `Docs/Wolfmed/WOLFMED_MANIFEST.md` (§7). WP11-6 *reconciles*, it does not merge. One owner per shared file
   (§4, serialisation rules).
7. **No commits.** Work packages leave the tree uncommitted; snapshot a patch per WP under
   `C:/Users/jzo12/Documents/Wolfmed/plan/snapshots/`. The user commits.
8. Before blaming Wolfmed for a test failure, run `DockTest` first (the `db.ef` sqlite warnings fail every pair
   test in this repo — project memory).

---

## 1. Decisions

### 1.1 Phase-1/2 decisions that bind phase 3 (restated, unchanged)

| ID | Decision as it applies to phase 3 | Evidence |
|---|---|---|
| **D2** | Entities without `WoundHostComponent` behave exactly as today. Every phase-3 change must be `HasComp<WoundHostComponent>`-scoped or structurally unreachable for non-hosts. Per-part armour satisfies this for free: the gate lives only in `WolfmedPartArmorSystem`, whose only trigger is `PartDamageModifyEvent`, which only `WoundDamageRoutingSystem` raises (`WoundDamageRoutingSystem.cs:741-749`). **A prototype edit on a shared abstract is never D2-scoped**: this is what killed the original HOOK 19 (revision notes, B3-1). `BaseHead` has **26** descendants and ten of them are not wound hosts — `_Mono/Body/Parts/protogen.yml:41`, `_Shitmed/Body/Parts/animal.yml:3`, `Animal/kobold.yml:34`, `Animal/monkey.yml:34`, `_NF/Body/Parts/goblin_parts.yml:38`, `Body/Parts/skeleton.yml:33`, `silicon.yml:88`, `_EinsteinEngines/Body/Parts/ipc.yml:53`, `_Mono/Body/Parts/chimera.yml:30` (all re-grepped here). | PLAN §1.1 |
| **D4** | Balance = Onyx defaults. **Phase 3 ships no deliberate numeric balance deviation.** P3-D1 is now a neutral-by-construction accounting fix (it charges exactly the damage the detached vital part carried away, so the readout cannot *drop*), and the P3-D14 stump-bleed literal is a *clamp*, not a tuning choice. The one balance-visible change is P3-D6's coverage annotation, escalated as user decision 2. | PLAN §1.1 |
| **D5** | Missing APIs get a compat shim in `Content.Shared/_WF/Wolfmed/Compat`; where impossible, a `// WOLFGATE` edit in the vendored file. **Phase 3 adds exactly one new `_WF` system and one new `_WF` partial (§2). The amputation compat layer is already complete.** | PLAN §1.1, verified §2.0 |
| **D6** | Layout: vendored Onyx code at its Onyx relative path under `_Onyx/`; Wolfgate glue under `_WF/Wolfmed`; docs in `Docs/Wolfmed/`. | PLAN §1.1 |
| **D7** | Wolfgate keeps Shitmed surgery. Onyx's `_Onyx/Medical/Surgery` and `_Onyx/Surgery` are **not** ported. This is what makes B-2 (P3-D2) and the organ-healing gap (P3-D9) real, and it is what makes 2 of Onyx's 5 `AmputationConsequenceTest` tests unportable (§6.2). | PLAN §1.1 |
| **D8** | Wolfgate stays on Shitmed's `BodyPartComponent`; Onyx's extra part fields live on `WolfmedBodyPartComponent`, read through `WolfmedBodyPartSystem.Get(EntityUid)`. Verified in the tree today: `WG/Content.Shared/_WF/Wolfmed/Body/WolfmedBodyPartSystem.cs:9` `public WolfmedBodyPartComponent Get(EntityUid part) => CompOrNull<WolfmedBodyPartComponent>(part) ?? None;` with `private static readonly WolfmedBodyPartComponent None = new();` at `:6`. **This is the single largest source of edits in phase 3** (18 sites in `AmputationSystem.cs`). | verified directly |
| **D9** | No `BodyPartType.Chest`/`.Groin`. `WG/Content.Shared/Body/Part/BodyPartType.cs` is `{Other, Torso, Head, Arm, Hand, Leg, Foot, Tail}`. Affects `AmputationSystem.cs` (3 sites), every ported `coverage:` list, and the one Onyx `targetLayers` line that adds `HumanoidVisualLayers.Groin`. | verified directly |
| **D11/D12** | Vendored files bind `WolfmedDamageableSystem`, never `DamageableSystem`. `AmputationSystem` needs it for `GetAllDamage` — verified present at `WG/Content.Shared/_WF/Wolfmed/Compat/WolfmedDamageableSystem.cs:125 public DamageSpecifier GetAllDamage(Entity<DamageableComponent?> ent)`; WG's real `DamageableSystem` has no such method. | verified directly |
| **D13** | Server-only wound systems live in `Content.Server/_Onyx/…` with Onyx namespaces unchanged. **`AmputationSystem` does NOT qualify** — every one of its dependencies is shared, so it stays at `Content.Shared/_Onyx/Wounds/AmputationSystem.cs` (§2.0). | verified §1.7 of `amputation.md`, re-checked |
| **D18** | Guards test `HasComp<WoundHostComponent>` directly. No marker component. | PLAN §1.2 |
| **D23 / HOOK 10** | `PartDamageModifyEvent` carries a WG-only trailing `float armorPenetration = 0f` (`WG/Content.Shared/_Onyx/Wounds/WoundEvents.cs:138-157`). **Every modifier set phase 3's armour package applies must be wrapped in `DamageSpecifier.PenetrateArmor`**, including the new `partModifiers` branch — otherwise AP silently dies against any armour that declares a part profile. | verified; `WolfmedPartArmorSystem.cs:31-32` |
| **D24** | `ExplosionSystem` is on PLAN §3's *Explicitly NOT hooked* list. **Phase 3 does not lift it** (P3-D3). | PLAN §3 |
| **D26** | Lifted for phase 3: `AmputationSystem` is ported and the two comment-outs at `WG/Content.Server/_Onyx/Wounds/OrganDamageSystem.cs:25-26,38-39` are re-enabled. | DECISIONS.md P3-1 |
| **D28** | `WolfmedBodyPartLifecycleSystem` (server) owns `<WoundHostComponent, BodyPartAddedEvent/RemovedEvent>` and re-raises `OrganGot*` over the subtree. **Do not weaken its `TerminatingOrDeleted` guards** (`:31`, `:52`) — that is the `DebugAssertException` WP9 fixed. Phase 3 adds no competing subscriber. | PLAN §1.3, WP9 |
| **D30** | A wound host's own `DamageableComponent` is the projected sum of all parts. This is *why* today's stock `DamageVisuals` block lights every limb layer at once, and why P3-4's per-layer read is a fix rather than a new feature. | WP5 |
| **D32** | `WoundHost` is on `BaseMobSpeciesOrganic`; Protogen is stripped at runtime by `WolfmedWoundHostExclusionSystem`. Phase-3 additions to `base.yml` (Species) are **not** covered by that exclusion — only `WoundHostComponent` is removed (P2-D22's correction). Phase 3's only Species-`base.yml` edit is the Option-B sprite-path swap, which is inert for non-hosts. | P2-D22 |
| **D35** | Wound-host damage routing is unpredicted. `AmputationSystem` is fully `_net.IsServer`-gated at four sites (`ONYX :33,57,68,113`), so it adds no new prediction surface; the detach and the thrown limb arrive one tick late, identically to Shitmed's existing sever. | verified |
| **P2-3** | `wounds.body_part_functionality_enabled` stays `false` (`WG/Content.Shared/_Onyx/CCVar/CCVars.Wounds.cs:7-8`). | verified |
| **P2-D16** | **Every numeric literal in a ported test is a prediction until measured.** Phase 3 has three literals that are *known* wrong in Onyx's own files (P3-D14, P3-D6, P3-D17). Put the derivation in a `// WOLFGATE` comment at each assertion. | WP9 §3.2 |
| **P2-D23** | Wound creation can be a dice roll. **Irrelevant to every phase-3 test**: amputation, organ and armour tests assert `GetAllDamage(part)`, `Severable`, attachment and wound *presence by prototype id*, all of which `CreateOrMergeWound`/`AmputationSystem` produce deterministically. No phase-3 test depends on `creationChance`. | verified |
| **P2-D24** | Pain-adjacent fixtures need `- type: StatusEffects` with an explicit `allowed:` and `- type: MobState`. **No phase-3 test exercises pain shock**, so this does not bind — recorded so nobody adds a pain assertion to an amputation fixture without it. | P2-D24 |

### 1.2 New phase-3 decisions

| ID | Decision | One-line rationale | Evidence |
|---|---|---|---|
| **P3-D1** | **B-1 is fixed inside the existing host-gated `_WF` handler, not by a prototype edit. HOOK 19 (`BaseHead.vitalDamage: 300`) is WITHDRAWN.** `Content.Server/_WF/Wolfmed/WolfmedBodyPartLifecycleSystem.OnPartRemoved` gains a `ChargeVitalPartLoss` call: when the removed part `IsVital` and the body has no other attached part of that type, apply systemic Bloodloss **equal to the damage the removed part was carrying**. Zero upstream files touched, zero new subscriptions, **zero balance deviation** — the charge is neutral by construction. | **The original HOOK 19 was a D2 breach.** `VitalDamage`'s only consumer is `SharedBodySystem.PartRemoveDamage` (`Content.Shared/Body/Systems/SharedBodySystem.Parts.cs:395-408`, re-read in full), which is **not** wound-host-gated and runs from every head-removal path — GUARD B gates only the sever *decision* (`_Shitmed/…/SharedBodySystem.Targeting.cs:234`), never `PartRemoveDamage`. `BaseHead` is a shared abstract with 26 descendants (D2 row), so `vitalDamage: 300` would have raised de-heading a Protogen, monkey, kobold, goblin, skeleton, silicon, IPC or chimera from 100 to 300 Bloodloss. **The problem itself is real and re-verified:** `CheckVitalDamage` sums only *attached* Head+Torso part damage plus systemic (`_Onyx/Mobs/Systems/MobThresholdSystem.cs:31-52`), the head carries ≥ its threshold at detach, and Shitmed adds back only `VitalDamage` = **100** (`Body/Part/BodyPartComponent.cs:39`) → net **−100**. **The new fix is structurally unreachable for non-hosts**: the subscription is `<WoundHostComponent, BodyPartRemovedEvent>` (`WolfmedBodyPartLifecycleSystem.cs:23`), it is `Content.Server`-only, and it already carries the `TerminatingOrDeleted` guards. Charging the part's own total makes the readout *exactly* neutral for any damage composition — it also closes a hole flat-300 left open (a Blunt decapitation needs ≥ **350**, so a flat 300 was still a net −50). After the fix, decapitation always reads **pre-decapitation total + 100**, which is precisely what a non-wound-host sees today. | Every line re-read here: `Parts.cs:356` raises `BodyPartRemovedEvent` **before** `PartRemoveDamage` at `:362`, so both charges land in the same call; `Bloodloss` is absent from `WoundHostComponent.LocalizedDamageTypes` (`WoundDamageComponents.cs:35-44`), so it routes to `SystemicDamageComponent` via `ApplySystemicDamage` (`WoundDamageRoutingSystem.cs:1048`) and **is** counted by `CheckVitalDamage`. Consumers that would otherwise improve on decapitation: `MobThresholdSystem.cs:341,411` (HOOK 11), `DefibrillatorSystem.cs:212` (HOOK 12), `WoundDamageRoutingSystem.cs:528` (HOOK 13). Alternative (c) — charging missing vital parts inside `CheckVitalDamage` — stays rejected: it needs remembered state to know *how much* left, and it splits the mob-state readout from the real damage the defibrillator and the alerts see. |
| **P3-D1a** | **`ChargeVitalPartLoss` is not refunded on re-attachment.** A surgically re-attached head brings its own damage back while the systemic charge stays. Recorded as a §7.3 deviation and handed to phase 4 (Onyx has no equivalent because Nubody blocks re-attachment outright — P3-D2). | A refund needs per-part state (the charged amount) and a second hook in `OnPartAdded`; it is ~15 lines and one new `_WF` datafield for a case that only Shitmed surgery can reach, and phase 4 owns that surgery anyway. The failure mode is conservative: a re-headed corpse reads *more* damaged than it is, never less. | Re-attachment path verified: `WolfmedBodyPartLifecycleSystem.OnPartAdded` is the only host-gated re-entry, and nothing in it touches systemic damage. |
| **P3-D2** | **`AmputationConsequenceWound` ships inert. `SharedBodySystem.Parts.cs:606 CanAttachPart` is NOT hooked.** Recorded as a known Onyx-parity loss, handed to phase 4. | In Onyx the wound blocks re-attachment through Nubody: `ONYX Content.Shared/_Onyx/Body/Systems/SharedBodySystem.cs:209-223 HasAmputationConsequence(EntityUid part)`, called from `TryAttachPart` at `:253`/`:389`, cleared by `ONYX SharedSurgerySystem.BodyParts.cs:148`. D7 skips that surgery. **Hooking `CanAttachPart` in phase 3 would make reattachment permanently impossible** — no phase-3 mechanic clears the wound deliberately (it only decays through `WoundHealingSystem`). Ship the wound as a marker that examine and phase-4 surgery can read. | `amputation.md` §2.6 (B-2). Cost of the loss is visible and bounded: the wound exists, has `damageTypes: {}` and no behaviors (`WG/Resources/Prototypes/_Onyx/Wounds/wounds.yml:398-403`), so it bleeds nothing and only shows in the P2 part-status examine. |
| **P3-D3** | **Explosion amputation is OUT of phase 3.** The `TryExplosionAmputate` branch and both CVars ship and stay inert. No `ExplosionSystem` hook, no `_routedModifiers` origin-flag passthrough. | Three independent reasons. (a) D24 forbids the hook. (b) It is the only phase-3 item that can regress **non-wound** combat: `TryApplyDistributedDamage`/`TryRouteDistributedDamage` never populate `_routedModifiers` (written only at `WoundDamageRoutingSystem.cs:79`), so the moment the hook lands the re-entrant pass replays `originFlag: null` and `WG/Content.Shared/_Mono/ArmorPlate/SharedArmorPlateSystem.cs:60` (`if (args.Origin == null && args.OriginFlag != DamageOriginFlag.Explosion) return;`) makes **armour plates stop protecting wound hosts from explosions entirely**. (c) `CCVars.ExplosionLimbDamageVariation` (2f) / `ExplosionWoundMultiplier` (4f) already exist at `CCVars.Wounds.cs:22-26` with zero consumers, so nothing regresses by waiting. | `amputation.md` §3.1-§3.4, re-verified. The branch still gets test coverage via the public routing API (T-AMP-EXPLOSION, §6.2). |
| **P3-D4** | **Onyx's amputation numbers ship unchanged (D4), and the "no gun can amputate" consequence is pinned by a CI assertion rather than tuned away.** | `WoundHostComponent.DefaultDismembermentFinishingDamage = { Slash 15, Piercing 40, Blunt 50 }` (`WG/Content.Shared/_Onyx/Wounds/WoundDamageComponents.cs:73-78`, verified). WG's `BaseBullet` is `Piercing: 14` (`Entities/Objects/Weapons/Guns/Projectiles/projectiles.yml:106`) — **below the Piercing finishing minimum at any accumulated damage, forever**. Lasers deal `Heat`, which appears in **no** `amputationThresholds` dict, and `GetThresholdProgress` iterates the *threshold* dict (`ONYX AmputationSystem.cs:142-147`) while `IsFinishingHit` skips every type not in `AmputationThresholds` (`:158`) — so `progress` stays 0 and no laser can ever amputate. Amputation in Wolfgate is melee-Slash-only. This is a large, surprising design fact for a gun-PvP server; it is surfaced as numbers in §8.2 and locked by T-AMP-NOGUN so a future tune is a visible, deliberate test change. | `amputation.md` §4.5; weapon prototypes re-verified. |
| **P3-D5** | **Per-part armour ships Onyx's *intent*, not Onyx's shipped code: `Coverage`/`CoverageSymmetry` gate the fallback branch (armour.md Option B). The slot-derived default (Option C) and its CCVar are NOT added in phase 3.** | Onyx declares `Coverage`/`CoverageSymmetry` (`ONYX Content.Shared/Armor/ArmorComponent.Locational.cs:13,19`) and reads them from **no C# anywhere** — `ONYX SharedArmorSystem.cs:124-128` deliberately disabled the gate inside an `<Onyx-ArmorGlobalProtection-edited>` marker. Consequently 2 of Onyx's 3 locational-armour tests are **red against Onyx's own pinned code** (§6.2). Option B is a strict superset of Option A, is what Onyx's doc comments and its own tests describe, is the only version under which P3-3's stated goal ("helmets and vests matter per limb") is true, and is a **zero-behaviour-change no-op until content is annotated** because not one WG prototype declares `coverage:`. Option C is rejected for phase 3 by the same rule that killed the phase-2 status-effect chain (P2-D1): **do not ship a CCVar with no live consumer.** It also silently re-balances 272 armour entries and breaks two currently-green tests (`AppliesArmorExactlyOnceTest` hits the head through an `outerClothing` item; `ArmorPenetrationReachesWoundHostsTest` hits a LeftArm through one). | `armour.md` §0.2, §3.5, §4.1, §4.5; `tests.md` F3 independently corroborates. Both reports recommend B; this plan drops C entirely rather than adding a dead CVar. |
| **P3-D6** | **The phase-3 armour content pass is limited to the `_Mono` bulletproof helmets (`coverage: [Head]`) and the `_Mono` bulletproof vests (`coverage: [Torso, Arm, Leg]`).** Every other `- type: Armor` in the game keeps unset coverage = full protection = today's behaviour. | Without *some* content the whole package is a no-op, and these are precisely Wolfgate's gun-PvP items. Scale of the alternative, measured: `- type: Armor` appears **272 times across 75 files**, and **271 of 272 inherit their `Clothing.slots` from a parent**, so a full pass is a 272-edit change with no data-driven shortcut. Verified vest coefficients (`Resources/Prototypes/_Mono/Entities/Clothing/OuterClothing/Armor/bulletproof_vests.yml`): Light `Piercing 0.45`/Blunt 0.85/Slash 0.85 (`:14-17`), Medium `0.35/0.8/0.7` (`:44-47`), Heavy **`0.25`**/0.3/0.3 (`:76-79`), Polyvalent `0.55/0.6/0.6` (`:109-112`), Stabproof `0.7/0.65/0.45` (`:139-142`); helmet Light `0.85` across the board (`…/Head/Helmets/bulletproof_helmets.yml:16-18`). **The one number that changes in ordinary aimed-torso PvP is stacking** (§8.3) — escalated as a user decision with "annotate both" as the default. | `armour.md` §5.1-§5.3, §10.3; every coefficient re-verified in the tree. |
| **P3-D7** | **Organ damage is switched on by prototype data only, Onyx-faithful (organs.md Option A), limited to the seven `OrganHuman*` ids.** The data-driven `WolfmedOrganProfilePrototype` (Option B) is a phase-4 item. | **The whole organ half of the port is inert today, and it is a prototype problem, not a C# problem:** `grep -rn "type: OrganDamage\|type: WolfmedOrgan" WG/Resources/Prototypes` → **zero hits**, so `OrganDamageSystem.cs:52-59` returns on `organs.Count == 0` for every hit in the game and `OrganHealthSystem.cs:39` queries an empty set every tick. Option A costs one new ~105-line YAML file plus **seven** one-line `parent:` edits (`Resources/Prototypes/Body/Organs/human.yml:53,103,150,189,215,249,270` — every line number re-verified) and covers human, gingerbread, dwarf, vox, yowie and the human-lineage organs of eight more species. Option B's full coverage would need ~27 edits across ~10 files in 6 fork namespaces, or a new prototype kind; neither is worth phase-3 risk. Uncovered roots are a **silent no-op, never a crash**. | `organs.md` §1.3, §5.1-§5.4; greps and line numbers re-verified. |
| **P3-D8** | **Seven of Onyx's nine organ-consequence pieces are SKIPPED, and `MissingHeartComponent` + `BodyStasis.cs` are explicitly NOT needed** (the question DECISIONS.md P3-2 poses is answered **no**). | Onyx destroys an organ at 0 HP; **every consequence except brain-death then flows from organ *removal*, which Wolfgate already implements by another route**: heart → `DelayedDeathComponent` (`WG/Content.Server/_Shitmed/Body/Organ/HeartSystem.cs:26`, `DelayedDeathComponent.cs:10 DeathTime = 60`), brain → `DebrainedComponent` (`BrainSystem.cs:39` → `DebrainedSystem.cs:25-61`), eyes → `TemporaryBlindnessComponent` (`EyesSystem.cs:83`), lungs → no `LungComponent` → suffocation. `FunctionalOrganComponent` maps 1:1 onto Shitmed's `OrganComponent.OnAdd` + `_Shitmed/BodyEffects/OrganEffectSystem.cs:53-59`. `TaggedOrgan*` is **dead code at the pin** (zero prototype users in Onyx). `BreathingImmunityComponent` already exists in WG (`_Shitmed/Body/Components/BreathingImmunityComponent.cs:8`) — **porting Onyx's `OrganConsequenceComponents.cs` wholesale is a server-start crash**, two `[RegisterComponent]`s with the same registered name. `BodyAnatomyComponent` needs `OrganCategoryPrototype`, which is MISSING in WG (zero hits). | `organs.md` §2.4-§2.9, §3.3, §4; `tests.md` F4 reaches the same conclusion on `OrganEffectSystem` independently, by a different route (its surgery/`NeuroInterface` dependencies are D7-excluded). |
| **P3-D9** | **`tests.md` Finding F4's "new upstream hook on `Content.Shared/Body/Organ/OrganComponent.cs` (four additive DataFields), not yet authorised anywhere" is OVERRULED. No such hook is needed or authorised.** | **Stale.** The four fields shipped in phase 1 (WP6) on a `_WF` component: `WG/Content.Shared/_WF/Wolfmed/Body/WolfmedOrganComponent.cs` declares `[DataField, AutoNetworkedField] public FixedPoint2 Health = FixedPoint2.New(15);`, `… MaxHealth = FixedPoint2.New(15);`, `[DataField] public ProtoId<WoundPrototype>? DestructionWound;`, `[DataField] public FixedPoint2 DestructionWoundSeverity;` — read in full here. F4 also lists `OrganDamageComponent`, `OrganHealthSystem` and `OrganDamageSystem` as MISSING; all three are in the tree (`Content.Shared/_Onyx/Body/OrganDamageComponent.cs`, `Content.Server/_Onyx/Body/Systems/OrganHealthSystem.cs`, `Content.Server/_Onyx/Wounds/OrganDamageSystem.cs`), read in full here, and recorded at `WOLFMED_MANIFEST.md:85,89,91`. `tests.md` §0 states it re-verified every claim; this one it did not. **`organs.md` is authoritative for P3-2.** | verified directly, all three files read in full. |
| **P3-D10** | **`AmputationConsequenceWound` and `DismembermentWound` both ship `mergeMode: SeparateInstances`, so repeated amputations create SEPARATE wound entities. `tests.md`'s T-AMP-CONSEQUENCE-MERGE ("one wound with combined severity, not two") is OVERRULED and replaced by T-AMP-CONSEQUENCE-SEPARATE.** | `WG/Resources/Prototypes/_Onyx/Wounds/wounds.yml:384-403` and `ONYX …/wounds.yml:571-581,1042-1046` both declare `mergeMode: SeparateInstances` on **both** prototypes (byte-identical, diffed). `WoundSystem.CreateOrMergeWoundInternal` (`WG/Content.Shared/_Onyx/Wounds/WoundSystem.cs:174-221`) only searches for an existing wound `if (prototype.MergeMode == WoundMergeMode.MergeByPrototype)` (`:185`); otherwise it always spawns. `WoundMergeMode` is `{MergeByPrototype, SeparateInstances}` (`WoundPrototype.cs:298-302`). **Onyx's `AmputationConsequenceTest.cs` has no merge test** — its five tests are `TraumaticAmputationCreatesBlockingConsequence`, `SurgicalHealRemovesConsequenceAndUnblocks`, `HealingDamageKeepsConsequenceBlocked`, `HealingPartAboveThresholdDoesNotAmputate`, `HealingBelowResetRatioClearsSeverable`; test #1 asserts `.Single(w => w.Comp.Prototype == "AmputationConsequenceWound")` precisely because it amputates once. | `tests.md` §2.1 states it "read only far enough to confirm structure … not transcribed assertion-by-assertion"; the file has now been read. |
| **P3-D11** | **`AmputationSystem.cs` is vendored to `Content.Shared/_Onyx/Wounds/`, not `Content.Server`.** | D13 moves a vendored file to `Content.Server` only when it has a server-only dependency. It has none: `SharedBodySystem`, `ThrowingSystem`, `WolfmedDamageableSystem`, `WoundSystem`, `WolfmedBodySystem`, `WolfmedBodyPartSystem` are all shared. Its server-only *behaviour* comes from four `_net.IsServer` guards, not from its assembly. `OrganDamageSystem` is a `Content.Server` file in the shared `Content.Shared._Onyx.Wounds` namespace and may depend on a shared system — it already depends on `WoundFractureSystem` (shared) at `:22` — so the re-enabled `[Dependency] private AmputationSystem _amputation` needs **no extra `using`**. | verified directly. |
| **P3-D12** | **The overflow branch (`OnPartDamageOverflowed`) is ported verbatim even though it is dead code on every organic humanoid — in Onyx too — and `WOLFMED_MANIFEST.md:845-850` is CORRECTED.** | `AccumulateAmputationOverflow` early-returns when `MaxDamage <= 0` (`WoundDamageRoutingSystem.cs:799`); the only WG part with `maxDamage` is `WolfmedBaseTorso` (`Resources/Prototypes/_WF/Wolfmed/Body/parts.yml:15 maxDamage: 250`, file read in full — every other abstract has `amputationThresholds` and no `maxDamage`); and `OnPartDamageOverflowed` returns immediately for `PartType == Torso` (`ONYX :35`). Onyx is identical (`ONYX chest_groin.yml:19` is the only limb-family `maxDamage`). **So the manifest's hazard note — "some limbs can never be severed once phase 3 lands `AmputationSystem`" unless every limb gets a `maxDamage` row — is wrong**: `maxDamage` gates only the dead overflow branch; `amputationThresholds` is what gates severing, and WP7 populated it on all ten limb abstracts. Port the branch anyway: it is live for Onyx animals (`ONYX _Onyx/Body/Parts/animal.yml:11`) and deleting it creates a permanent re-sync diff. | `amputation.md` §2.1, §9; `tests.md` F1 independently. `parts.yml` and `WoundDamageComponents.cs` re-read in full here. |
| **P3-D13** | **Phase 3 adds no YAML for amputation. `parts.yml` is already complete and byte-correct against Onyx.** | Every threshold re-verified against `ONYX Resources/Prototypes/Body/base_organs.yml` and `chest_groin.yml`: Head 200/200/350, Arm 130/250/250, Hand 70/200/150, Leg 150/250/300, Foot 80/220/170, Torso `maxDamage: 250`. `dismembermentFinishingDamage`, `amputationConsequenceSeverity` and `dismembermentSeverity` are unset on every part in **both** trees, so the live values are the C# defaults (§8.1). | `parts.yml` read in full here; `amputation.md` §4.1. |
| **P3-D14** | **The ported stump-bleed assertion is `Is.EqualTo(bloodstream.MaxBleedAmount)` (10f), NOT Onyx's `Is.GreaterThanOrEqualTo(40f)`.** Recorded as a corrected stale literal, same class as WP9's and WP10-6b's. | `DismembermentWound` at Head severity 200 with `rate: 0.2, awakeMultiplier: 1.5` gives a raw rate of 40–60, but WG clamps: `WG/Content.Server/Body/Components/BloodstreamComponent.cs:57 public float MaxBleedAmount = 10.0f;` and GUARD E3's shared implementation clamps at `BloodstreamSystem.cs:432 component.BleedAmount = Math.Clamp(component.BleedAmount, 0, component.MaxBleedAmount);`. Onyx's own default is also 10, so Onyx's test prototype must raise it; WG's `WoundBleedingBody` fixture does not. | `amputation.md` §2.7; both lines re-verified. |
| **P3-D15** | **`IsFinishingHit` and `TryExplosionAmputate` change signature** — `BodyPartComponent part` → `EntityUid part` / the `BodyPartComponent bodyPart` parameter is dropped — because the data they read is on `WolfmedBodyPartComponent`. | `ONYX AmputationSystem.cs:151 private bool IsFinishingHit(EntityUid body, BodyPartComponent part, DamageSpecifier damage)` reads `part.AmputationThresholds` (`:158`) and `part.DismembermentFinishingDamage` (`:161`); `:170-175 private bool TryExplosionAmputate(EntityUid body, Entity<WoundableComponent> part, BodyPartComponent bodyPart, DamageSpecifier hit, DamageSpecifier? totalDamage = null)` reads `bodyPart.AmputationThresholds` (`:188`). Neither field exists on WG's `BodyPartComponent`. Precedent already shipped: `WoundDamageRoutingSystem.cs:799,804,954` already redirect the identical reads through `_wfPart.Get(part).X`. | `amputation.md` §1.4 rows 21-26, `tests.md` F2; ONYX file read in full here. |
| **P3-D16** | **`amputation.md`'s edit table is adopted as-is, with one addition: `ONYX :182` `_damageable.GetAllDamage(part.Owner)` also binds the facade** and compiles unchanged because `WolfmedDamageableSystem.GetAllDamage` takes `Entity<DamageableComponent?>`, to which `EntityUid` implicitly converts. **No `DamageSpecifier.DamageDict` key-type edit is needed anywhere in this file** — the WP10-2 `string`-vs-`ProtoId` trap does not recur. | All four call shapes analysed and re-checked: `:98-101` both sides `string`; `:146` generic inference fixes `TKey = string` from the receiver's exact bound and `ProtoId<T>`'s `implicit operator string` converts the argument; `:158`/`:161` are `Dictionary<ProtoId<…>,…>` receivers taking a `string` key through `implicit operator ProtoId<T>(string)`. No `.Key.Id` access exists in the file. If any site produces `CS0411`/`CS0121`, the fix is an explicit `new ProtoId<DamageTypePrototype>(type)`, not a redesign. | `amputation.md` §1.5; WG `DamageSpecifier.cs:44 public Dictionary<string, FixedPoint2> DamageDict { get; set; }` verified. |
| **P3-D17** | **WP7 never copied `brute_damage.rsi`/`burn_damage.rsi`; the task brief's "already copied in WP7 — verify" premise is false and P3-4 is fully unstarted.** | `WOLFMED_MANIFEST.md:395-397` states it explicitly ("textures deliberately not ported"), and `find WG/Resources/Textures/_Onyx` shows only `Interface/Alerts/fracture.rsi`. `grep -rn "PartDamageVisualsComponent" WG/Content.Client` → **zero hits**: the component has no consumer at all. | `visuals.md` §0; re-verified here. |
| **P3-D18** | **P3-4 ships Option A (attached-body, per-limb-accurate, zero new textures) as the mandatory deliverable; Option B (severed-limb wound rendering, 156 texture files + the `BodyPartComponent` flag flip) is PRE-AUTHORISED as an in-package extension and may be dropped if WP11-4 runs long.** | Option A is exactly DECISIONS.md P3-4's wording ("so wounds show on the body sprite") and is nearly free: the server data plumbing (`PartDamageVisualsComponent`, `WoundDamageProjectionSystem.RefreshBodyDamage`/`RefreshDetachedDamage`/`TryGetVisualLayer`, already D9-folded Torso→Chest) shipped complete in WP5 and was WP9-hardened, with **zero consumers**. WG's own `Mobs/Effects/{brute,burn}_damage.rsi` already carries all 36 states the six targeted layers need. Option B is worth pre-authorising because P3-1 lands amputation in the same phase, so severed limbs will exist to look at, and its server-side source (`RefreshDetachedDamage`) has been live and unconsumed since phase 1. | `visuals.md` §1, §3.1, §6, §10. |
| **P3-D19** | **The `BodyPartComponent` `raiseAfterAutoHandleState: true` flag flip is on the authorised hook list (HOOK 21) and is required for Option B — porting Onyx's `OnBodyPartState` subscription without it compiles cleanly and NEVER FIRES.** | `WG/Content.Shared/Body/Part/BodyPartComponent.cs:18` is `[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]` — no flag. Onyx's own `BodyPartComponent` has `AutoGenerateComponentState(raiseAfterAutoHandleState: true)` (`ONYX _Onyx/Body/Part/BodyPartComponent.cs:41-42`), which is why Onyx's client code works. Confirmed safe: `grep` for `<BodyPartComponent, AfterAutoHandleStateEvent>` across `Content.{Client,Shared,Server}` → **zero** existing subscribers, so the flag can only add behaviour. | `visuals.md` §4.1; re-verified. |
| **P3-D20** | **`traumaDeductions` is NOT ported, in any form.** | Onyx sets it on ~20 `- type: Armor` YAML blocks and has **no C# datafield for it anywhere** (`git grep -rn "TraumaDeduction" HEAD -- '*.cs'` → no output). It is an orphaned feature in Onyx. Porting the key would hard-fail the Release YAML lint. | `armour.md` §0.5. Same defect class as P2-D9's `emotes:`/`emotesThreshold:` key. |
| **P3-D21** | **`PLAN.md §5.2` / `PLAN2.md §5.2` are CORRECTED: `<OrganComponent, OrganAddedToBodyEvent>` and `<OrganComponent, OrganRemovedFromBodyEvent>` were never registered in phase 1 and are FREE.** Phase 3 still does not register them (Option B is deferred), but the record must be right. | `WOLFMED_MANIFEST.md:291-295` already says so; grep of every `SubscribeLocalEvent<*, OrganAddedToBodyEvent/OrganRemovedFromBodyEvent>` in WG returns only `BrainComponent` (`BrainSystem.cs:24,26`), `HeartComponent` (`_Shitmed/Body/Organ/HeartSystem.cs:16,17`) and `NymphComponent` (`NymphSystem.cs:24`) — re-run here. | `organs.md` §7.1. |
| **P3-D22** | **`OrganHealthSystem`'s brain branch stays live.** Once WP11-2's prototypes land, a brain at 0 HP kills the mob outright and the organ is never destroyed. This is Onyx-faithful (D4) and gets its own test. | `WG/Content.Server/_Onyx/Body/Systems/OrganHealthSystem.cs:45-54`, read in full: `if (HasComp<BrainComponent>(uid)) { if (slotted.Body is { } body && TryComp(body, out MobStateComponent? mobState) && !_mobState.IsDead(body, mobState) && _mobState.HasState(body, MobState.Dead, mobState)) _mobState.ChangeMobState(body, MobState.Dead, mobState, uid); continue; }`. `tests.md` §6 item 4 raises this as an open question; it is answered here rather than deferred, because the branch is already shipped and phase 3 is what makes it reachable. Rarity bounds the risk: ~100 head hits (§8.4). | verified directly. |
| **P3-D23** | **`OrganHealthSystem` gains `TerminatingOrDeleted` guards (2 lines, `// WOLFGATE`).** | `DestroyOrgan` calls `_body.RemoveOrgan` and `_wounds.CreateOrMergeWound(parent, …)` on the containing part. `RecursiveDeleteEntity` detaches parts while a mob terminates — this is byte-for-byte the failure WP9 fixed at `WolfmedBodyPartLifecycleSystem.cs:31-33,52-54`, and it cost WP9 13 unrelated pooled-pair failures once. Today the path is unreachable (no organ carries `WolfmedOrgan`); WP11-2 makes it reachable for the first time. | `organs.md` §9.1 file #4, R5; WP9 report. |
| **P3-D24** | **Sequencing rule: `Content.Server/_Onyx/Wounds/OrganDamageSystem.cs` is owned EXCLUSIVELY by WP11-1.** WP11-2 must not touch `:25-26` / `:38-39`. | Both P3-1 and P3-2 have a claim on those lines; only P3-1 changes them. Sequential packages in one worktree mean the second would otherwise clobber the first. | `organs.md` §1.2, R2. |
| **P3-D25** | **`UpdatePartDamageVisuals` folds `LHand→LArm`, `RHand→RArm`, `LFoot→LLeg`, `RFoot→RLeg` before the per-layer lookup.** A deliberate, 4-line divergence from Onyx's own method. | Without it P3-4 is a **regression**, not a fix. `WoundDamageProjectionSystem.TryGetVisualLayer:240-262` maps hands to `HumanoidVisualLayers.LHand/RHand` and feet to `LFoot/RFoot`, and **none of those four is in the stock `targetLayers`** (`Entities/Mobs/Species/base.yml:61-75` — Chest, Head, LArm, LLeg, RArm, RLeg only). Today (D30) a hand injury lights *every* limb layer because the overlay reads the mob's aggregate; after a rote per-layer port it would light **nothing**. There is no art to add instead: `ls Resources/Textures/Mobs/Effects/brute_damage.rsi` is exactly 36 states, `{Chest,Head,LArm,LLeg,RArm,RLeg}_Brute_{10,20,30,50,70,100}` — **no hand or foot states exist in WG**, and only Option B's Onyx RSIs carry `LHand/RHand/LFoot/RFoot` prefixes (for *detached* parts). Folding preserves today's "hand damage shows on the arm" exactly. | Directory listed and both files read here. Onyx has the identical six-layer list, so this is a knowing deviation from Onyx, recorded in §7.3. |
| **P3-D26** | **§8.3's stacking figure is CORRECTED: P3-D6's annotation set does not change aimed-torso protection at all; what it changes is aimed-**head** protection for vest wearers.** | The old text priced "heavy vest + SWAT helmet" as if both were annotated. **They are not**: PROTO B annotates one helmet (`ClothingHeadBPHelmetLight`, the only `- type: Armor` in `_Mono/…/bulletproof_helmets.yml:13`) and five vests; `ClothingHeadHelmetSwat` (`Entities/Clothing/Head/helmets.yml:68-85`, Piercing **0.80**) keeps unset coverage and therefore keeps protecting every part, torso included. So heavy vest + SWAT stays `0.25 × 0.80 = 0.20` on the torso and goes from **0.20 to 0.80** on the head. Full table in §8.3. | All three prototypes re-read here. |

---

## 2. New compat / `_WF` pieces

### 2.0 What already exists — verified, nothing to build

Phase 3's amputation half needs **no new `_WF` file at all.** Each of these was read in full in the current
tree:

| Symbol | Site | Status |
|---|---|---|
| `WolfmedBodySystem.TryDetachPart` | `Content.Shared/_WF/Wolfmed/Compat/WolfmedBodySystem.cs:13` `public bool TryDetachPart(EntityUid part, bool reparent = true)` | present; guards `GetParentPartAndSlotOrNull` + `HasComp<BodyPartComponent>` + `CanDetachPart`, then raises `AmputateAttemptEvent` and returns `_body.GetParentPartOrNull(part) is null` |
| `WolfmedDamageableSystem.GetAllDamage` | `Compat/WolfmedDamageableSystem.cs:125` `public DamageSpecifier GetAllDamage(Entity<DamageableComponent?> ent)` | present (D12 facade) |
| `WolfmedBodyPartSystem.Get` | `_WF/Wolfmed/Body/WolfmedBodyPartSystem.cs:9` | present; never returns null |
| `WolfmedBodyPartComponent` amputation fields | `…/WolfmedBodyPartComponent.cs:16,19,22,25,28` — `MaxDamage`, `AmputationThresholds`, `DismembermentFinishingDamage`, `AmputationConsequenceSeverity = 35`, `DismembermentSeverity` | **all four present.** No component change in phase 3 |
| `WolfmedOrganComponent` | `…/WolfmedOrganComponent.cs:13,16,19,22` — `Health = 15`, `MaxHealth = 15`, `DestructionWound`, `DestructionWoundSeverity` | present (WP6) |
| `OrganDamageComponent` / `OrganHealthSystem` / `OrganDamageSystem` | `Content.Shared/_Onyx/Body/OrganDamageComponent.cs`, `Content.Server/_Onyx/Body/Systems/OrganHealthSystem.cs`, `Content.Server/_Onyx/Wounds/OrganDamageSystem.cs` | **all present** (WP6). P3-D9 |
| `PartDamageVisualsComponent` + writers | `WoundDamageComponents.cs:95-100`; `WoundDamageProjectionSystem.RefreshBodyDamage/RefreshDetachedDamage/TryGetVisualLayer` | present, D9-folded, WP9-hardened, **zero consumers** |
| `WolfmedPartArmorSystem` | `_WF/Wolfmed/Armor/WolfmedPartArmorSystem.cs:25` — sole subscriber of `<ArmorComponent, InventoryRelayedEvent<PartDamageModifyEvent>>` | present; phase 3 rewrites its handler body |
| `PartDamageModifyEvent` with `PartType`/`Symmetry`/`ArmorPenetration` | `WoundEvents.cs:138-157`, raised at `WoundDamageRoutingSystem.cs:741-749` | present |
| `WolfmedBodyPartLifecycleSystem` | `Content.Server/_WF/Wolfmed/WolfmedBodyPartLifecycleSystem.cs:22-23,31,52` | present, guarded |
| `WoundTargetResolver`, `PartStatusSeverity`, `OnyxBodyEvents`, `DamageDealtEvent` | `_WF/Wolfmed/**` | present |

**Phase 3 adds exactly two new `_WF` files** (§2.1, §2.2) plus one new client `_WF` partial (§2.3).

### 2.1 `Content.Shared/_WF/Wolfmed/Armor/ArmorComponent.Wolfmed.cs` (P3-D5) — new

```csharp
using Content.Shared.Body.Part;
using Content.Shared.Damage;

namespace Content.Shared.Armor; // deliberate: extends the upstream partial without touching its file.

public sealed partial class ArmorComponent
{
    /// <summary>Body part types this armour protects. Null or empty protects every part; the worn slot is irrelevant.</summary>
    [DataField] public HashSet<BodyPartType>? Coverage;

    /// <summary>Sides this armour protects. Null or empty protects every symmetry.</summary>
    [DataField] public HashSet<BodyPartSymmetry>? CoverageSymmetry;

    /// <summary>Ordered per-location modifier overrides; the first matching entry wins and skips the coverage gate.</summary>
    [DataField] public List<ArmorPartModifier> PartModifiers = [];
}

/// <summary>A location-specific armour modifier set, matched by part type and symmetry.</summary>
[DataDefinition]
public sealed partial class ArmorPartModifier
{
    /// <summary>Matching body part types. Empty matches every type.</summary>
    [DataField] public HashSet<BodyPartType> Parts = [];

    /// <summary>Matching sides. Empty matches every symmetry.</summary>
    [DataField] public HashSet<BodyPartSymmetry> Symmetry = [];

    /// <summary>Modifiers applied to the struck part's share of the damage.</summary>
    [DataField(required: true)] public DamageModifierSet Modifiers = default!;
}
```

**Zero upstream edits.** `WG/Content.Shared/Armor/ArmorComponent.cs:13` is
`public sealed partial class ArmorComponent : Component` with `[RegisterComponent, NetworkedComponent,
AutoGenerateComponentState]` and **no `[Access]`** (goob edit), so a `_WF` partial can add fields and
`WolfmedPartArmorSystem` may read them. Same pattern as the already-shipped
`Content.Shared/_WF/Wolfmed/Armor/SharedArmorSystem.Wolfmed.cs` (manifest `:121`).

**Traps, each verified:**
* **Do NOT add `[AutoNetworkedField]`.** `ArmorPartModifier` is a `[DataDefinition]`, not
  `[Serializable, NetSerializable]`; an auto-networked `List<ArmorPartModifier>` either fails to compile or
  needs a NetSerializable mirror. These fields are prototype-static and never mutated at runtime, so the client
  already has them from the prototype at spawn.
* **`Coverage` is nullable, deviating from Onyx's non-nullable `= []`.** Recorded as a manifest deviation.
  Semantics: `null` **and** empty both mean "protects everything". Getting that backwards inverts every armour
  in the game.
* `ArmorPartModifier` is a new type name — `grep -rn "ArmorPartModifier" WG/Content.*` → nothing. No collision.
* `ArmorComponent.ShowArmorOnExamine` is Onyx-only and is **not** ported.

**Registers nothing, subscribes nothing.** **Serves:** `WolfmedPartArmorSystem`.

### 2.2 `Content.Server/_WF/Wolfmed/Body/WolfmedOrganConsequenceSystem.cs` (P3-D8) — new

```csharp
using Content.Shared._Onyx.Body;          // OrganFunctionChangedEvent (OrganHealthSystem.cs:14-22 declares it here)
using Content.Shared.Body.Organ;          // OrganComponent — Wolfgate keeps it here, not in _Shitmed
using Content.Shared._Shitmed.Body.Organ; // OrganEnableChangedEvent (OrganEvents.cs:7)
using Content.Shared._WF.Wolfmed.Body;

namespace Content.Server._WF.Wolfmed.Body;

/// <summary>Bridges Onyx's organ-functionality event onto Shitmed's organ enable/disable switch.</summary>
public sealed class WolfmedOrganConsequenceSystem : EntitySystem
{
    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        // Pair verified free: OrganFunctionChangedEvent has no subscriber anywhere in Wolfgate today
        // (declared and raised only in OrganHealthSystem.cs:21,71).
        SubscribeLocalEvent<WolfmedOrganComponent, OrganFunctionChangedEvent>(OnOrganFunctionChanged);
    }

    private void OnOrganFunctionChanged(Entity<WolfmedOrganComponent> ent, ref OrganFunctionChangedEvent args)
    {
        if (TerminatingOrDeleted(ent) || !HasComp<OrganComponent>(ent))
            return;

        // Onyx filters FunctionalOrganComponent grants on organ.Health > 0; Shitmed's equivalent switch is
        // OrganEnableChangedEvent, which OrganComponent's own handler turns into OrganComponentsModifyEvent
        // (revoking OnAdd components) and the eyes blindness path.
        var enable = new OrganEnableChangedEvent(args.Functional);
        RaiseLocalEvent(ent, ref enable);
    }
}
```

**Why raise rather than subscribe:** `<OrganComponent, OrganEnableChangedEvent>` is **already owned** by
`WG/Content.Shared/Body/Systems/SharedBodySystem.Organs.cs:23`. Subscribing it again is a server-start crash.
Raising it is the established Wolfgate pattern — precedent
`WG/Content.Server/_Shitmed/Cybernetics/CyberneticsSystem.cs:27-28,45-46` does exactly this.

**Why this is the only consequence glue phase 3 needs:** everything else Onyx's `OrganEffectSystem` does on
organ *removal* Wolfgate already does (P3-D8). This system covers only the one-tick window in which an organ
is at 0 HP but not yet destroyed — `OrganHealthSystem.Update` destroys it on the next tick
(`OrganHealthSystem.cs:33-58`), and `SetHealth` raises `OrganFunctionChangedEvent` only on a
`Health > 0` ⇄ `Health <= 0` transition (`:60-73`).

**The disable fires twice, and that is fine.** One tick later `DestroyOrgan` → `SharedBodySystem.RemoveOrgan`
raises `OrganEnableChangedEvent(false)` again (`Content.Shared/Body/Systems/SharedBodySystem.Organs.cs:65-66`),
and `OnOrganEnableChanged` (`:273-290`) does **not** early-return on an unchanged value, so
`OrganComponentsModifyEvent` — and therefore `_Shitmed/BodyEffects/OrganEffectSystem.OnOrganComponentsModify`'s
`RemoveComponents` (`:47-68`) — runs a second time against the same `onAdd:` grants. Removing an already-removed
component is a no-op, so this is idempotent; it is stated here so nobody "fixes" it with a guard that also
suppresses the first pass. T-ORG-FUNC ticks once and re-asserts, so the double path is covered (§6.2).

**Known, accepted race (do not add `before:`/`after:`):** `CyberneticsSystem.OnEmpDisabledRemoved` raises
`OrganEnableChangedEvent(true)` unconditionally; an EMP recovery inside that one-tick window could re-enable a
doomed organ. It is deleted the next tick and only reachable on `Cybernetics` organs.

**Serves:** organs with `OnAdd` component grants, and eyes.

### 2.3 `Content.Client/_WF/Wolfmed/Damage/DamageVisualsSystem.Wolfmed.cs` (P3-D18) — new

A `partial` on the upstream `Content.Client.Damage.DamageVisualsSystem` (which is
`public sealed partial class DamageVisualsSystem : VisualizerSystem<DamageVisualsComponent>`), so it may call
that class's private `HandleDamage`, `CheckThresholdBoundary` and `UpdateTargetLayer` — partial parts share
private member access.

```csharp
using Content.Shared._Onyx.Wounds;
using Content.Shared.Damage;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;

namespace Content.Client.Damage;

public sealed partial class DamageVisualsSystem
{
    /// <summary>Re-runs the damage visuals when a wound host's per-part damage projection changes.</summary>
    private void OnPartDamageVisualsState(Entity<PartDamageVisualsComponent> ent, ref AfterAutoHandleStateEvent args) { … }

    /// <summary>Drives each targeted sprite layer from that limb's own damage instead of the mob's aggregate.</summary>
    private void UpdatePartDamageVisuals(EntityUid uid, SpriteComponent sprite,
        DamageVisualsComponent damageVisComp, PartDamageVisualsComponent partDamage) { … }

    /// <summary>Damage shown on one layer: the limb itself plus the extremity Wolfgate has no layer for (P3-D25).</summary>
    private static DamageSpecifier? GetLayerDamage(PartDamageVisualsComponent partDamage, HumanoidVisualLayers layer) { … }

    // Option B only — severed-limb wound rendering:
    private void OnBodyPartState(Entity<BodyPartComponent> ent, ref AfterAutoHandleStateEvent args) { … }
    private void UpdateDetachedPartDamage(…) { … }
    private void UpdateDetachedDamageLayer(…) { … }
    private void SetDetachedDamageLayerVisible(…) { … }
    private int GetDetachedDamageThreshold(…) { … }
    private bool TryGetDetachedDamagePrefix(…) { … }
}
```

**Bodies are Onyx's, re-authored onto WG's calling convention** (this file is upstream, **not** vendored
verbatim, so there is no name-preservation constraint):
* Onyx's bare `ProtoMan` becomes WG's existing `_prototypeManager` field (`DamageVisualsSystem.cs:29`).
  `EntitySystem.ProtoMan` is confirmed **MISSING** in RT 277 (zero hits) — the same gap WP1 shimmed for
  `StatusEffectsSystem` — but no shim is needed here.
* `DamageSpecifier.GetDamagePerGroup(IPrototypeManager)` is **SAME**: `Content.Shared/Damage/DamageSpecifier.cs:298`.
* **Onyx's entity-tuple call shapes do not exist in WG and must be re-typed**, verified line by line:
  Onyx `UpdatePartDamageVisuals(Entity<SpriteComponent, DamageVisualsComponent>, PartDamageVisualsComponent)`
  calling `UpdateTargetLayer(entity, layer, group, threshold)` (`ONYX :533-551`) becomes WG's
  `private void UpdateTargetLayer(SpriteComponent spriteComponent, DamageVisualsComponent damageVisComp,
  object layerMapKey, string damageGroup, FixedPoint2 threshold)` (`WG :621`). `CheckThresholdBoundary` is
  **SAME** shape (`WG :542 private bool CheckThresholdBoundary(FixedPoint2 damageTotal, FixedPoint2
  lastThreshold, DamageVisualsComponent damageVisComp, out FixedPoint2 threshold)`).
  `damageVisComp.TargetLayerMapKeys` is `List<Enum>` (`DamageVisualsComponent.cs:118`), so the
  `is HumanoidVisualLayers` pattern test Onyx uses is required in WG too.
* **The per-group threshold cache is deliberately not consulted.** Onyx passes `lastThreshold:
  FixedPoint2.Zero` and discards the bool; WG's `LastThresholdPerGroup` is keyed by *group*, not by
  (layer, group) (`DamageVisualsComponent.cs:124`), so consulting it would make one limb's threshold suppress
  another's. Keep Onyx's behaviour: recompute per layer, cache nothing.
* **P3-D25 fold.** `GetLayerDamage` returns `partDamage.Damage[layer]` plus, for `LArm`/`RArm`/`LLeg`/`RLeg`,
  the matching `LHand`/`RHand`/`LFoot`/`RFoot` entry (`DamageSpecifier.cs:401 public static DamageSpecifier
  operator +(DamageSpecifier, DamageSpecifier)`, verified present).
  Without it, hand and foot wounds become invisible — see P3-D25 and §7.3 item 18.
* Option B's `SpriteSystem` entity-tuple API is **SAME** in RT 277 and already injected on the base class
  (`RobustToolbox/Robust.Client/GameObjects/EntitySystems/VisualizerSystem.cs:14
  [Dependency] protected SpriteSystem SpriteSystem = default!;`): `LayerMapTryGet` (`SpriteSystem.LayerMap.cs:118,138`),
  `LayerMapSet` (`:14,29`), `AddLayer` (`SpriteSystem.Layer.cs:223`), `LayerSetRsiState`
  (`SpriteSystem.LayerSetters.cs:148,154,160`), `LayerSetVisible` (`:358,364,370`), `LayerSetColor` (`:395,401,407`).
* The attached-body path needs none of that — it reuses WG's own `CheckThresholdBoundary` and
  `UpdateTargetLayer(SpriteComponent, …)` verbatim.

**Option B port traps — both are hard compile errors if ported rotely:**
* **Delete the `Groin` arm from `TryGetDetachedDamagePrefix`.** `ONYX Content.Client/Damage/DamageVisualsSystem.cs:138-146`
  contains `HumanoidVisualLayers.Groin => "Groin",`; WG's enum has no such member
  (`Content.Shared/Humanoid/HumanoidVisualLayers.cs` — only `Chest` at `:15`; `grep Groin` → nothing). **CS0117.**
  D9, the same trap as `coverage: [Groin]`. The Onyx RSIs ship `Groin_*` states WG will simply never request.
* Onyx's detached path iterates `Enum.GetValues<HumanoidVisualLayers>()` (`ONYX :86`), so the four
  `LHand/RHand/LFoot/RFoot` prefixes **do** render on detached parts under Option B — the P3-D25 fold is an
  attached-body concern only and must not be applied to `UpdateDetachedPartDamage`.

**HOOK 20's early `return` narrows two branches for wound hosts** — `DamageVisualizerKeys.ForceUpdate` →
`ForceUpdateLayers` and the `TrackAllDamage` branch (`WG DamageVisualsSystem.cs:367-380`) are skipped once a
`PartDamageVisualsComponent` is present. Onyx does exactly the same (`ONYX :498-505`), and the stock humanoid
sets neither key, so it is inert today. Put that sentence in the hook comment so a future species prototype
that sets `trackAllDamage` does not silently lose it.

**Layer-collision check, done:** Shitmed's `SharedBodySystem.PartAppearance.cs` toggles the *humanoid's own*
body-part layer through `SharedHumanoidAppearanceSystem.SetLayerVisibility`/`AddMarking`;
`DamageVisualsSystem.AddDamageLayerToSprite` (`:325-335`) creates a **separate** layer keyed `"{layer}{group}"`
(e.g. `"ChestBrute"`) inserted after the humanoid layer by index. Different layer-map keys — no collision, no
ordering hazard.

**Sandbox:** `Content.Client` is not loaded into the client's sandboxed prediction domain; no `Sandbox.yml`
entry is needed, and both namespaces this partial pulls in are already referenced from `Content.Client`.

**Serves:** `Content.Client/Damage/DamageVisualsSystem.cs` (HOOK 20).

---

## 3. Upstream `// WOLFGATE` hooks — the complete authorised phase-3 list

Nothing outside this table may be edited in an upstream (non-`_Onyx`, non-`_WF`) file without escalating.
Line numbers were re-verified against the tree as it stands today (phases 1 and 2 committed).

| # | File | Site | Change | WP |
|---|---|---|---|---|
| ~~HOOK 19~~ | ~~`Resources/Prototypes/Body/Parts/base.yml`~~ | — | **WITHDRAWN (P3-D1).** It was a D2 breach: `BaseHead` is a shared abstract with 26 descendants, ten of them non-hosts, and `VitalDamage`'s only consumer (`SharedBodySystem.Parts.cs:395-408`) is not host-gated. B-1 is now fixed inside `WolfmedBodyPartLifecycleSystem` (a `_WF` file — no hook budget, no upstream edit). **Phase 3 makes no prototype edit under `Resources/Prototypes/Body/Parts/`.** | — |
| **HOOK 20** | `Content.Client/Damage/DamageVisualsSystem.cs` | `Initialize()` after **`:35`**; `HandleDamage(…)` after the `UpdateDisabledLayers` call at **`:362`** and before `CheckOverlayOrdering` at **`:365`** | **Two insertions, 1 + 4 lines; all bodies in the `_WF` partial (§2.3).** (a) `SubscribeLocalEvent<PartDamageVisualsComponent, AfterAutoHandleStateEvent>(OnPartDamageVisualsState); // WOLFGATE: HOOK 20` plus `using Content.Shared._Onyx.Wounds;`. (b) <pre>// WOLFGATE: HOOK 20 — per-limb accuracy for wound hosts; body in _WF/Wolfmed/Damage/DamageVisualsSystem.Wolfmed.cs<br>if (damageVisComp.TargetLayers != null &amp;&amp; damageVisComp.DamageOverlayGroups != null &amp;&amp;<br>    TryComp(uid, out PartDamageVisualsComponent? partDamage))<br>{ UpdatePartDamageVisuals(uid, spriteComponent, damageVisComp, partDamage); return; }</pre> **`Content.Client/Damage/DamageVisualsComponent.cs` is NOT touched** — its ONYX/WG diffs are unrelated upstream `ProtoId`-ification and the `Displacement` feature, and carry no `<Onyx-…>` tags. **Adds one client subscription** (§5). | WP11-4 |
| **HOOK 21** | `Content.Shared/Body/Part/BodyPartComponent.cs` | **`:18`** | **One word**, Option B only: `[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)] // WOLFGATE: HOOK 21 — P3-4 Option B needs AfterAutoHandleStateEvent on detached parts.` Confirmed zero existing `<BodyPartComponent, AfterAutoHandleStateEvent>` subscribers, so this can only add behaviour. **Drop this hook if Option B is dropped** — an unused flag widens nothing but is dead weight. | WP11-4 |
| **PROTO A** | `Resources/Prototypes/Body/Organs/human.yml` | **`:53, :103, :150, :189, :215, :249, :270`** | **Seven one-line `parent:` edits**, e.g. `:53` `parent: BaseHumanOrganUnGibbable` → `parent: [BaseHumanOrganUnGibbable, WolfmedOrganBrain] # WOLFGATE (WP11-2, D8): Wolfmed organ health + organ-damage policy`; the other six are `parent: BaseHumanOrgan` → `parent: [BaseHumanOrgan, WolfmedOrgan{Eyes,Lungs,Heart,Stomach,Liver,Kidneys}]`. Ids confirmed at `:52 OrganHumanBrain`, `:102 OrganHumanEyes`, `:149 OrganHumanLungs`, `:188 OrganHumanHeart`, `:214 OrganHumanStomach`, `:248 OrganHumanLiver`, `:269 OrganHumanKidneys`. **`OrganHumanTongue` (`:119`), `OrganHumanAppendix` (`:128`) and `OrganHumanEars` (`:139`) are NOT edited** — no WG body graph slots them (P3-D7 evidence). | WP11-2 |
| **PROTO B** | `Resources/Prototypes/_Mono/Entities/Clothing/Head/Helmets/bulletproof_helmets.yml` and `…/OuterClothing/Armor/bulletproof_vests.yml` | each `- type: Armor` block | **One `coverage:` line per block.** Helmets → `coverage: [Head] # WOLFGATE (WP11-3, P3-D6)`. Vests (blocks at `:12, :42, :74, :107, :137`) → `coverage: [Torso, Arm, Leg] # WOLFGATE (WP11-3, P3-D6)`. **Never emit `Chest` or `Groin`** (D9 — no such enum member; the lint fails). **Nothing else in the repo is annotated in phase 3.** | WP11-3 |
| **PROTO C** | `Resources/Prototypes/Entities/Mobs/Species/base.yml` | the `- type: DamageVisuals` block at **`:61-75`**, `sprite:` lines at **`:72`** and **`:75`** | **Option B only, two lines:** `Mobs/Effects/brute_damage.rsi` → `_Onyx/Wounds/brute_damage.rsi`, same for `burn_damage.rsi`, inside the existing `# WOLFGATE` block convention. **Do NOT add Onyx's `- "enum.HumanoidVisualLayers.Groin"` line** — D9: WG's `HumanoidVisualLayers` has no `Groin` member, and `TryGetVisualLayer` already folds Torso→Chest. **Do NOT add Hand/Foot layers** — Onyx does not either; the live overlay stays at six layers in both trees. | WP11-4 |

**Explicitly NOT touched in phase 3** (and why):

- `Content.Server/Explosion/EntitySystems/ExplosionSystem.{CVars,Processing}.cs` — **D24 / P3-D3.** Hooking it
  also opens the Mono plate-protection hole.
- `Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs` — the `_routedModifiers` origin-flag passthrough is
  part of the deferred explosion package (P3-D3), not phase 3.
- `Content.Shared/Body/Systems/SharedBodySystem.Parts.cs` — **two separate reasons.** `CanAttachPart:606` is
  P3-D2 (no consequence gate without a way to clear it). `PartRemoveDamage:395-408` is P3-D1: the earlier plan
  would have raised the damage it deals for every `BaseHead` descendant in the game; the charge now lives in a
  host-gated `_WF` handler instead, so this file needs no edit at all.
- `Resources/Prototypes/Body/Parts/base.yml` — **P3-D1**, HOOK 19 withdrawn. Moving the value onto
  `WolfmedBaseHead` (`_WF/Wolfmed/Body/parts.yml:17-19`, already `BaseHead`'s parent) is **not** a fix either:
  the inheritance set is identical. Do not mistake it for one.
- `Content.Shared/Body/Organ/OrganComponent.cs` — **P3-D9.** The four fields already live on
  `WolfmedOrganComponent`. `tests.md` F4's proposed hook is withdrawn.
- `Content.Shared/Armor/SharedArmorSystem.cs` and `Content.Shared/Armor/ArmorComponent.cs` — **P3-D5.** Onyx
  registers `<ArmorComponent, InventoryRelayedEvent<PartDamageModifyEvent>>` in `SharedArmorSystem.cs:30`;
  **adding it there is a duplicate directed subscription with `WolfmedPartArmorSystem.cs:25` and crashes the
  server at start.** The new fields go in a `_WF` partial (§2.1).
- `Content.Shared/_Mono/ArmorPlate/**` — plates stay whole-body and absorb once on the routed pass, before the
  systemic/localized split and long before `PartDamageModifyEvent` exists. A plate carrier protecting your feet
  is a recorded balance inconsistency, not a phase-3 fix.
- `Content.Client/Damage/DamageVisualsComponent.cs` — no Onyx-tagged lines; needs no edit.
- `Content.Shared/Inventory/InventorySystem.Relay.cs` — WG's `InventoryRelayedEvent<T>` has no `Owner`; already
  worked around at `SharedArmorSystem.Wolfmed.cs:20` via `Transform(uid).ParentUid`. Option C, which would have
  wanted the slot, is deferred.
- `Content.Shared/Body/Part/BodyPartType.cs`, `Content.Shared/Humanoid/HumanoidVisualLayers.cs`,
  `Content.Shared/_Shitmed/Targeting/TargetBodyPart.cs` — **D9 stands.**
- `Resources/Prototypes/_WF/Wolfmed/Body/parts.yml` — **P3-D13**, already complete and correct.
- `Resources/Prototypes/_Onyx/Wounds/wounds.yml` — `organDamage.chances` (`:27-36`, Head 0.05 / Torso 0.04 /
  Arm 0.02 / Hand 0.01 / Leg 0.02 / Foot 0.01, `maxAffected: 2`) already ships; no phase-3 edit.
- Everything on PLAN §3's and PLAN2 §3's original "Explicitly NOT touched" lists.

**In-vendored-file `// WOLFGATE` edits** (not upstream hooks; full text in §4):

| File | Edits |
|---|---|
| `Content.Shared/_Onyx/Wounds/AmputationSystem.cs` (new, vendored) | **18 sites** — §4/WP11-1 |
| `Content.Server/_Onyx/Wounds/OrganDamageSystem.cs` | **2 sites** — delete 4 comment lines at `:25-26` and `:38-39`, restore the dependency and the call |
| `Content.Server/_Onyx/Body/Systems/OrganHealthSystem.cs` | **2 sites** — `TerminatingOrDeleted` guards (P3-D23) |
| `Content.Shared/_WF/Wolfmed/Armor/WolfmedPartArmorSystem.cs` | handler body rewritten + `<remarks>` updated (this is a `_WF` file, so no `// WOLFGATE` marker convention applies) |
| `Content.Server/_WF/Wolfmed/WolfmedBodyPartLifecycleSystem.cs` | **P3-D1** — `ChargeVitalPartLoss` added to the existing `OnPartRemoved` handler (`_WF` file, no marker convention, no new subscription) |

---

## 4. Work packages

Build order — **packages run SEQUENTIALLY in the one worktree**:

```
WP11-0  Zero-dependency test debt       (2 files)   ← write first; tests already-shipped phase-1 code
   │
WP11-1  Amputation                      (3 files)   [owns OrganDamageSystem.cs, WolfmedBodyPartLifecycleSystem.cs]
   │
WP11-2  Organ damage                    (4 files)   [owns Body/Organs/human.yml]
   │
WP11-3  Per-part armour                 (5 files)   [owns WoundDamageFoundationTest.cs, the _Mono armour YAML]
   │
WP11-4  Limb damage visuals             (2 / 5 files) [owns Species/base.yml, BodyPartComponent.cs]
   │
WP11-5  Amputation + organ tests        (4 files)   [owns WoundBleedingTest.cs]
   │
WP11-6  Docs + manifest reconcile       (3 files)
```

**Serialisation rule 1 — `Content.Server/_Onyx/Wounds/OrganDamageSystem.cs` is owned exclusively by WP11-1**
(P3-D24). WP11-2 changes nothing in it.

**Serialisation rule 2 — one owner per shared test file.** `WoundDamageFoundationTest.cs` → WP11-3 only.
`WoundBleedingTest.cs` → WP11-5 only. New test files belong to whichever WP creates them.

**Serialisation rule 3 — `[TestPrototypes]` ids are a global pool** across the whole suite. Taken today:
`WoundFoundation*`, `WoundBleedingBody*`, `WoundHealingBody*`, `WoundScarBody*`, `WoundFractureBody*`,
`WoundFractureArmor`, `WoundFractureHandsBody*`, `WoundFractureHeldItem`, `WolfmedBridgeBody*`,
`WolfmedBridgeArmor`, `WolfmedPainShockBody`, `WolfmedHighPainThresholdBody`. Phase-3 additions listed per WP;
re-grep before adding, because packages land in sequence.

**Serialisation rule 4 — the manifest.** Each WP appends its own `### WP11-N` rows and deviations directly to
`Docs/Wolfmed/WOLFMED_MANIFEST.md` (ground rule 6). WP11-6 **reconciles** (amends the stale rows in §7.1,
dedupes, and writes the two narrative docs); it does not re-append.

---

### WP11-0 — Zero-dependency test debt (write first)

**Goal:** close two test gaps in already-shipped phase-1 code, before any phase-3 code can muddy the blame.

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedReattachTest.cs` | **new** (T-REATTACH) |
| 2 | — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedVisualsTest.cs` | **new** (T-VISUALS) |

**Why first:** both test code that has been live since phase 1 with no coverage.
`WoundScarTest.ThresholdTreatmentAttachmentAndRejuvenateTest` already proves a wound *survives* a
detach→`AttachPart` round trip, but nothing proves the part re-enters live wound tracking — which is exactly
PLAN §8.3 trap 2, and `WoundDamageProjectionSystem.OnPartInserted`'s only caller is
`WolfmedBodyPartLifecycleSystem`. T-VISUALS tests the server data `PartDamageVisualsComponent` carries, which
has had **zero consumers** since WP5; if it is broken, WP11-4 would look like the culprit.

**Assertions:** §6.2 rows T-REATTACH and T-VISUALS.

**Build checkpoint:** all three `dotnet build` targets green; `DockTest` green; the two new tests green.

---

### WP11-1 — Amputation (P3-1)

**Goal:** traumatic amputation from threshold + finishing hits is live; the consequence and dismemberment
wounds land on the parent; the severed limb is thrown; decapitation is lethal.

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | `Content.Shared/_Onyx/Wounds/AmputationSystem.cs` (212 lines) | same path | **new (vendored), 18 `// WOLFGATE` sites** |
| 2 | — | `Content.Server/_Onyx/Wounds/OrganDamageSystem.cs` | **modified**, 2 sites (4 comment lines deleted) |
| 3 | — | `Content.Server/_WF/Wolfmed/WolfmedBodyPartLifecycleSystem.cs` | **modified**, 1 call + 1 method — **P3-D1** (replaces the withdrawn HOOK 19) |
| 4 | — | `Docs/Wolfmed/WOLFMED_MANIFEST.md` | append `### WP11-1` |

**Order matters:** #1 before #2 (`OrganDamageSystem`'s `[Dependency] private AmputationSystem` is `CS0246`
without it).

#### File 1 — the exact edit table for `AmputationSystem.cs`

Onyx line numbers verified by `git -C C:/Users/jzo12/Documents/Wolfmed/onyx show HEAD:Content.Shared/_Onyx/Wounds/AmputationSystem.cs`
(read in full). Keep Onyx's namespace `Content.Shared._Onyx.Wounds` and every line not listed here **verbatim**.

| # | Onyx line | Before | After |
|---|---|---|---|
| 1 | after `:12` | — | `using Content.Shared._WF.Wolfmed.Body; // WOLFGATE: D8`<br>`using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE: D12 + TryDetachPart` |
| 2 | `:19` | `[Dependency] private DamageableSystem _damageable = default!;` | `[Dependency] private WolfmedDamageableSystem _damageable = default!; // WOLFGATE: D12, GetAllDamage lives on the compat facade.` |
| 3 | after `:23` | — | `    [Dependency] private WolfmedBodyPartSystem _wfPart = default!; // WOLFGATE: D8`<br>`    [Dependency] private WolfmedBodySystem _wfBody = default!; // WOLFGATE: Onyx's SharedBodySystem.TryDetachPart` |
| 4 | `:35` | `bodyPart.PartType == BodyPartType.Chest \|\|` | `bodyPart.PartType == BodyPartType.Torso \|\| // WOLFGATE: D9` |
| 5 | `:36` | `bodyPart.MaxDamage <= FixedPoint2.Zero)` | `_wfPart.Get(part).MaxDamage <= FixedPoint2.Zero) // WOLFGATE: D8` |
| 6 | `:39` | `TryExplosionAmputate(args.Body, part, bodyPart, args.Damage)` | `TryExplosionAmputate(args.Body, part, args.Damage) // WOLFGATE: see #16` |
| 7 | `:44` | `part.Comp.AmputationOverflow >= bodyPart.MaxDamage` | `part.Comp.AmputationOverflow >= _wfPart.Get(part).MaxDamage // WOLFGATE: D8` |
| 8 | `:51` | `IsFinishingHit(args.Body, bodyPart, args.Damage)` | `IsFinishingHit(args.Body, part.Owner, args.Damage) // WOLFGATE: see #15` |
| 9 | `:64` | `parentPart.AmputationConsequenceSeverity` | `_wfPart.Get(parent).AmputationConsequenceSeverity // WOLFGATE: D8` — **keep** the `TryComp(parent, out BodyPartComponent? parentPart)` guard at `:58`; `parentPart` becomes an unused local, which is fine (same precedent as `WoundFractureSystem.cs:148`). **Read this carefully: the severity comes off the PARENT, not the severed part.** |
| 10 | `:73` (a) | `bodyPart.PartType is BodyPartType.Chest \|\|` | `bodyPart.PartType is BodyPartType.Torso \|\| // WOLFGATE: D9` |
| 11 | `:73` (b) | `bodyPart.Parent == null \|\|` | `_body.GetParentPartOrNull(part) is null \|\| // WOLFGATE: Shitmed's BodyPartComponent has no Parent field` (`SharedBodySystem.Parts.cs:414`) |
| 12 | `:74` | `bodyPart.AmputationThresholds.Count == 0 \|\|` | `_wfPart.Get(part).AmputationThresholds.Count == 0 \|\| // WOLFGATE: D8` |
| 13 | `:79` | `TryExplosionAmputate(args.Body, part, bodyPart, args.Damage, damage)` | `TryExplosionAmputate(args.Body, part, args.Damage, damage) // WOLFGATE` |
| 14 | `:84` | `ReachedThreshold(damage, bodyPart.AmputationThresholds)` | `ReachedThreshold(damage, _wfPart.Get(part).AmputationThresholds) // WOLFGATE: D8` |
| 15 | `:91` | `GetThresholdProgress(damage, bodyPart.AmputationThresholds)` | `GetThresholdProgress(damage, _wfPart.Get(part).AmputationThresholds) // WOLFGATE: D8` |
| 16 | `:103-104` | `ReachedThreshold(damageBeforeHit, bodyPart.AmputationThresholds) && IsFinishingHit(args.Body, bodyPart, args.Damage)` | `ReachedThreshold(damageBeforeHit, _wfPart.Get(part).AmputationThresholds) && // WOLFGATE: D8`<br>`IsFinishingHit(args.Body, part.Owner, args.Damage) // WOLFGATE` |
| 17 | `:114` | `bodyPart.PartType == BodyPartType.Chest)` | `bodyPart.PartType == BodyPartType.Torso) // WOLFGATE: D9` |
| 18 | `:117` | `var parent = bodyPart.Parent ?? part;` | `var parent = _body.GetParentPartOrNull(part) ?? part; // WOLFGATE` |
| 19 | `:118` | `if (!_body.TryDetachPart(part))` | `if (!_wfBody.TryDetachPart(part)) // WOLFGATE: compat shim` |
| 20 | `:123` | `bodyPart.DismembermentSeverity ?? GetDismembermentSeverity(host, bodyPart.PartType)` | `_wfPart.Get(part).DismembermentSeverity ?? GetDismembermentSeverity(host, bodyPart.PartType) // WOLFGATE: D8` |
| 21 | `:151` | `private bool IsFinishingHit(EntityUid body, BodyPartComponent part, DamageSpecifier damage)` | `private bool IsFinishingHit(EntityUid body, EntityUid part, DamageSpecifier damage) // WOLFGATE: D8/P3-D15` + insert `var wf = _wfPart.Get(part); // WOLFGATE` as the first statement |
| 22 | `:158` | `!part.AmputationThresholds.ContainsKey(type)` | `!wf.AmputationThresholds.ContainsKey(type)` |
| 23 | `:161-162` | `part.DismembermentFinishingDamage.GetValueOrDefault(type, host.DefaultDismembermentFinishingDamage.GetValueOrDefault(type))` | `wf.DismembermentFinishingDamage.GetValueOrDefault(type, host.DefaultDismembermentFinishingDamage.GetValueOrDefault(type))` |
| 24 | `:170-175` | `private bool TryExplosionAmputate(EntityUid body, Entity<WoundableComponent> part, BodyPartComponent bodyPart, DamageSpecifier hit, DamageSpecifier? totalDamage = null)` | drop the `BodyPartComponent bodyPart` parameter `// WOLFGATE: P3-D15` |
| 25 | `:177` | `IsFinishingHit(body, bodyPart, hit)` | `IsFinishingHit(body, part.Owner, hit)` |
| 26 | `:188` | `GetThresholdProgress(totalDamage, bodyPart.AmputationThresholds)` | `GetThresholdProgress(totalDamage, _wfPart.Get(part.Owner).AmputationThresholds) // WOLFGATE: D8` |

**18 distinct edit *sites*** (26 rows; several share a line).

**Two `GetAllDamage` call sites need NO edit, and here is why** (stated so the implementer does not "fix" them):
`:78` `_damageable.GetAllDamage((part.Owner, damageable))` — `damageable` is declared nullable by the
`!TryComp(part, out DamageableComponent? damageable)` guard at `:75`, so the tuple is already
`Entity<DamageableComponent?>`; and `:182` `_damageable.GetAllDamage(part.Owner).Clone()` — `EntityUid`
converts implicitly to `Entity<DamageableComponent?>` (P3-D16). Both bind the facade (edit #2) unchanged.
`_body` is still needed after edit #19: `GetParentPartOrNull`.
`ThrowingSystem.TryThrow` is `public void` in WG vs `public bool` in Onyx, identical parameter list
(`WG Content.Shared/Throwing/ThrowingSystem.cs:96-107`); `:125` discards the result, so it compiles.

#### File 2 — `OrganDamageSystem.cs`

```
:25  // WOLFGATE: D26, AmputationSystem is phase 3 and is not ported; the field would be CS0246.
:26  // TODO: phase 3 - [Dependency] private AmputationSystem _amputation = default!;
```
→ `    [Dependency] private AmputationSystem _amputation = default!;`

```
:38      // WOLFGATE: D26, amputation is phase 3.
:39      // TODO: phase 3 - _amputation.HandlePartDamageApplied(part, ref args);
```
→ `        _amputation.HandlePartDamageApplied(part, ref args);`

**Call order must stay `_wounds` → `_fractures` → `_amputation` → `_bleeding`** (`:36-40`), matching
`ONYX OrganDamageSystem.cs:34-37`. It is load-bearing: `_wounds` must create the Slash/Blunt wound *before*
amputation can detach the part, and `_fractures` must see the part still attached. **No extra `using`** — the
file's namespace is already `Content.Shared._Onyx.Wounds` (`:14`).

#### File 3 — `WolfmedBodyPartLifecycleSystem.cs` (P3-D1, replaces HOOK 19)

Add to the **existing** `OnPartRemoved` handler (after the `_bleeding.OnPartChanged(body)` call, so the
projection has already refreshed) and one new private method. **No new subscription, no new system, no
upstream file.**

```csharp
        ChargeVitalPartLoss(body, args.Part);
```

```csharp
    /// <summary>Keeps a lost vital part's damage on the books; CheckVitalDamage only sums attached parts.</summary>
    private void ChargeVitalPartLoss(Entity<WoundHostComponent> body, Entity<BodyPartComponent> part)
    {
        if (!part.Comp.IsVital || _body.GetBodyChildrenOfType(body.Owner, part.Comp.PartType).Any())
            return;

        var lost = _damageable.GetTotalDamage(part.Owner);
        if (lost <= FixedPoint2.Zero)
            return;

        // Bloodloss is the type Shitmed's own PartRemoveDamage uses, and it is not in LocalizedDamageTypes,
        // so routing keeps it systemic instead of dealing it to another limb.
        _damageable.ChangeDamage(body.Owner,
            new DamageSpecifier(_prototypes.Index<DamageTypePrototype>("Bloodloss"), lost));
    }
```

Every API in it verified in the tree: `BodyPartComponent.IsVital` (`Body/Part/BodyPartComponent.cs`, YAML key
`vital`), `SharedBodySystem.GetBodyChildrenOfType` is **public** (`SharedBodySystem.Parts.cs:991`),
`WolfmedDamageableSystem.GetTotalDamage(Entity<DamageableComponent?>)` (`:134`) and `.ChangeDamage(…)` (`:28`,
which forces `canSever: false, canEvade: false`). Three new `[Dependency]` lines:
`WolfmedDamageableSystem _damageable`, `IPrototypeManager _prototypes`, plus the `_body` the system already has.

**Why it lands correctly:** `RemovePart` raises `BodyPartRemovedEvent` at `SharedBodySystem.Parts.cs:356` and
calls `PartRemoveDamage` at `:362`, so both charges apply inside the same call — the mob ends the tick at
*pre-removal total + `VitalDamage` (100)*, exactly what a non-wound-host sees today. The handler's existing
`TerminatingOrDeleted` guards (`:31`, `:52`) mean gibbing and mob deletion charge nothing.

**Traps:**
* **Do not reorder it before `_projection.OnPartRemoved`** — `ChangeDamage` re-enters routing, and the
  projection must have dropped the detached limb first.
* **Do not gate it on `PartType == Head`.** `IsVital` is the contract; `BaseHead` is only today's single user.
* **Do not add a `_net.IsServer` check** — the file is `Content.Server`.
* This inflates the `DamageSpecifier` that the *killing* `TryChangeDamage` returns, because
  `ApplySystemicDamage` folds it into the outer `Applied` delta (`WoundDamageRoutingSystem.cs:1074`). Upstream's
  own 100 already does this; §7.3 item 19 records it. Log-only in the tree today (`SharedMeleeWeaponSystem.cs:597`);
  the Blunt-keyed stamina branch at `:588` is unaffected because the charge is `Bloodloss`.

**Subscription pairs this WP registers:** exactly one —
`<WoundableComponent, PartDamageOverflowedEvent>` (`AmputationSystem.Initialize():28`). **Verified free**
(§5.1).

**Build checkpoint:** three builds green; Release YAML lint green (this WP now changes **no** YAML — run it
anyway, it is cheap and catches a stray edit); server starts with **no `Duplicate Subscriptions` throw**;
`WoundDamageFoundationTest`, `WoundHealingTest`, `WoundFractureTest`, `WoundScarTest`, `BodyConsequencesTest`,
`WolfmedDamageBridgeTest` all still green (the `OrganDamageSystem` fan-out changed). **Plus a D2 spot check
that costs one minute:** de-head a non-wound-host (a monkey: `MobMonkey`) and confirm it still takes exactly
**100** Bloodloss, not 300 — the regression the withdrawn HOOK 19 would have shipped.

---

### WP11-2 — Organ damage (P3-2)

**Goal:** organ damage fires for the first time on human-lineage organs, and organ destruction produces its
Shitmed consequences plus internal bleeding.

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | `Resources/Prototypes/Body/base_organs.yml` (values) | `Resources/Prototypes/_WF/Wolfmed/Body/organs.yml` | **new**, ≈105 lines |
| 2 | — | `Resources/Prototypes/Body/Organs/human.yml` | **modified**, 7 `parent:` lines — **PROTO A** |
| 3 | — | `Content.Server/_WF/Wolfmed/Body/WolfmedOrganConsequenceSystem.cs` | **new** (§2.2) |
| 4 | — | `Content.Server/_Onyx/Body/Systems/OrganHealthSystem.cs` | **modified**, 2 `// WOLFGATE` guards (P3-D23) |
| 5 | — | `Docs/Wolfmed/WOLFMED_MANIFEST.md` | append `### WP11-2` |

#### File 1 — `organs.yml`

Values are Onyx's, verbatim, from `ONYX Resources/Prototypes/Body/base_organs.yml`. `health`/`maxHealth` are
**deliberately omitted** — `WolfmedOrganComponent` already defaults to 15/15, identical to Onyx's
`OrganComponent` C# default, and **no Onyx organ prototype overrides them** (`git grep -n "maxHealth" HEAD --
Resources/Prototypes/Body …` → zero hits). Separate abstract ids, not redeclarations, because RT's
`ComponentRegistrySerializer` throws `Duplicate ID` — same shape and same reason as `parts.yml` (WP7).

| id | `destructionWound` / severity | `hitChance` | `selectionWeight` | `damageMultipliers` (Blunt/Slash/Piercing/Heat/Cold/Shock) | Onyx src |
|---|---|---|---|---|---|
| `WolfmedOrganBrain` | none (`OrganHealthSystem` kills the mob instead) | 0.8 | 0.75 | .115 / .25 / .42 / .15 / .05 / .3125 | `base_organs.yml:481-491` |
| `WolfmedOrganEyes` | none | 0.7 | 0.2275 | .115 / .3 / .4375 / .15 / .05 / .25 | `:537-547` |
| `WolfmedOrganLungs` | `InternalBleedingWound` **35** | default 1.0 | **1.38** | .1 / .25 / .42 / .165 / .05 / .25 | `:665-676` |
| `WolfmedOrganHeart` | `InternalBleedingWound` **45** | 0.8 | 0.64 | .1 / .25 / .455 / .15 / .05 / .3375 | `:718-730` |
| `WolfmedOrganLiver` | `InternalBleedingWound` **40** | default 1.0 | 1.1 | .1 / .3 / .4375 / .15 / .05 / .25 | `:808-819` |
| `WolfmedOrganStomach` | `InternalBleedingWound` **25** | 0.85 | 0.56 | .1 / .275 / .4025 / .15 / .05 / .25 | `:755-767` |
| `WolfmedOrganKidneys` | `InternalBleedingWound` **30** | 0.9 | 0.51 | .1 / .25 / .4025 / .15 / .05 / .25 | `:846-858` |

`InternalBleedingWound` is present (`WG/Resources/Prototypes/_Onyx/Wounds/wounds.yml:405-413`,
`WoundInternalBleedingBehavior rate: 0.02, chance: 1, maximumSeverity: 200`). Full YAML body is
`organs.md` §5.3, adopted verbatim.

#### Inheritance side effects to check at implementation time (all verified to exist)

* `Resources/Prototypes/_Shitmed/Body/Organs/cybernetic.yml:2` and `generic.yml:2,8,14,20`, and
  `_Mono/Body/Organs/cybernetics.yml:2,18,120`, parent `OrganHuman{Eyes,Heart,Liver,Lungs}` →
  **cybernetic organs will inherit organic organ-damage policy.** **Leave them** (P3-D10-adjacent): Onyx routes
  organ damage through its `CyberneticBodyPartProfile` too, and phase 5 owns the whole cybernetic/IPC profile.
* `Resources/Prototypes/Body/Organs/rat.yml:3` and `_NF/Body/Organs/goblin_organs.yml` parent `OrganHuman*`;
  those mobs are not wound hosts, so the components sit inert. Harmless.

#### Files 3-4

§2.2 and P3-D23. File 4's two guards: `TerminatingOrDeleted(uid)` before `DestroyOrgan` in the `Update` loop,
and `TerminatingOrDeleted(parent)` before `CreateOrMergeWound` in `DestroyOrgan` (`:80-88`).

**Subscription pairs this WP registers:** exactly one —
`<WolfmedOrganComponent, OrganFunctionChangedEvent>`. **Verified free** (§5.1). It **raises**
`OrganEnableChangedEvent`, never subscribes it.

**Does NOT touch** `OrganDamageSystem.cs` (P3-D24), `wounds.yml`, or any of the ~20 non-`OrganHuman*` organ
roots (recorded as a manifest deviation).

**Build checkpoint:** three builds green; Release YAML lint green (the ErrorNode trap applies — lint in
Release only); server starts clean; `WoundDamageFoundationTest` + `WoundBleedingTest` still green.

**One measurement this WP owes:** PROTO A makes `OrganHealthSystem.Update`'s per-tick
`EntityQueryEnumerator<WolfmedOrganComponent, OrganComponent>()` (`OrganHealthSystem.cs:39`) non-empty **for
the first time**, and it enumerates organs on non-hosts too (`Body/Organs/rat.yml:3`,
`_NF/Body/Organs/goblin_organs.yml`, every `_Shitmed`/`_Mono` cybernetic organ parented to `OrganHuman*`). No
behaviour changes — health never drops off a wound host, so D2 holds — but the loop is new cost. It early-outs
on `organ.Health > FixedPoint2.Zero` (`:42-43`), i.e. one comparison per organ per tick; sample it once on a
populated round and record the number rather than assuming.

---

### WP11-3 — Per-part (locational) armour (P3-3)

**Goal:** armour protects the parts it covers, and an armour may declare per-limb modifier overrides.

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | `Content.Shared/Armor/ArmorComponent.Locational.cs` | `Content.Shared/_WF/Wolfmed/Armor/ArmorComponent.Wolfmed.cs` | **new** (§2.1) |
| 2 | `Content.Shared/Armor/SharedArmorSystem.cs:108-129` (handler body only) | `Content.Shared/_WF/Wolfmed/Armor/WolfmedPartArmorSystem.cs` | **modified** (handler rewritten) |
| 3 | — | `Resources/Prototypes/_Mono/…/Head/Helmets/bulletproof_helmets.yml` | **modified** — PROTO B |
| 4 | — | `Resources/Prototypes/_Mono/…/OuterClothing/Armor/bulletproof_vests.yml` | **modified**, 5 blocks — PROTO B |
| 5 | `…/WoundDamageFoundationTest.cs` (3 tests + fixtures) | same | **modified** (§6.2) |
| 6 | — | `Docs/Wolfmed/WOLFMED_MANIFEST.md` | append `### WP11-3` |

#### File 2 — the rewritten handler (Option B, P3-D5)

```csharp
    /// <summary>Applies this armour's per-part or global penetration-adjusted modifiers to the routed part's damage.</summary>
    private void OnPartDamageModify(EntityUid uid, ArmorComponent component, InventoryRelayedEvent<PartDamageModifyEvent> args)
    {
        foreach (var profile in component.PartModifiers)          // Onyx SharedArmorSystem.cs:114-122, first match wins
        {
            if (profile.Parts.Count != 0 && !profile.Parts.Contains(args.Args.PartType) ||
                profile.Symmetry.Count != 0 && !profile.Symmetry.Contains(args.Args.Symmetry))
                continue;

            args.Args.Damage = DamageSpecifier.ApplyModifierSet(args.Args.Damage,
                DamageSpecifier.PenetrateArmor(profile.Modifiers, args.Args.ArmorPenetration)); // D23
            return;
        }

        if (!Covers(component, args.Args.PartType, args.Args.Symmetry))
            return;

        args.Args.Damage = DamageSpecifier.ApplyModifierSet(args.Args.Damage,
            DamageSpecifier.PenetrateArmor(component.Modifiers, args.Args.ArmorPenetration));
    }

    /// <summary>Whether this armour's global modifiers reach the given part. Unset or empty covers everything.</summary>
    private static bool Covers(ArmorComponent component, BodyPartType type, BodyPartSymmetry symmetry)
    {
        if (component.Coverage is { Count: > 0 } parts && !parts.Contains(type))
            return false;

        return component.CoverageSymmetry is not { Count: > 0 } sides || sides.Contains(symmetry);
    }
```

Design points, each verified — **get any of these wrong and the package ships something broken:**

* **`PenetrateArmor` must wrap `profile.Modifiers` too.** Without it, every AP weapon silently stops working
  against any armour declaring a `partModifiers` entry. `DamageSpecifier.PenetrateArmor`
  (`WG/Content.Shared/Damage/DamageSpecifier.cs:306-330`) returns the set unchanged at `penetration == 0`
  and a **new empty** set at `>= 1f`; `ApplyModifierSet` (`:133-163`) leaves a type untouched when the set has
  no entry, so an empty set is a true no-op. T-P3ARM-AP is the gate.
* **The coverage gate sits AFTER the `PartModifiers` loop**, matching Onyx's own
  `<Onyx-ArmorGlobalProtection>` comment, which scopes coverage only to "an individual body part … not listed
  in `@Coverage`/`@CoverageSymmetry`" — i.e. only the fallback. Putting it first turns
  `LocationalModifierOverridesAndFallbackTest` red on `head = 5` (its armour declares `coverage: [Torso]` only).
* **`Symmetry` is checked independently of `Parts`.** `symmetry: [Left]` alone means "any left part"; a torso
  (`BodyPartSymmetry.None`) is excluded by such a set.
* **No mask gate.** Onyx's `if (TryComp<MaskComponent>(uid, out var mask) && mask.IsToggled) return;`
  (`ONYX :111-112`) is base-game drift; WG has no mask check in any of its four armour handlers, and adding it
  only here would make a toggled-down mask armour the torso and not the head. Recorded as a deliberate
  deviation.
* **Systemic and non-wound-host paths are untouched.** `SharedArmorSystem.Wolfmed.cs:33-63` keeps applying
  global modifiers to systemic types (coverage is a *localized* concept in Onyx too), and mice, vehicles,
  blastdoors and mothroaches that carry `- type: Armor` never see `PartDamageModifyEvent`.
* **D9 mapping for every ported `coverage:` list:** `Chest` → `Torso`; `Groin` → **deleted** (never emit
  `Torso` twice). A `Groin` hit already arrives at the relay as `PartType == Torso, Symmetry == None` because
  `WoundTargetResolver.TryResolveAvailable` → `SharedBodySystem.ConvertTargetBodyPart` folded it upstream
  (`WoundTargetResolver.cs:48`). `BodyPartSymmetry` is `{None, Left, Right}` in both trees — **SAME**.

**Subscription pairs this WP registers: NONE.** It rewrites the body of a handler that already exists.
**Do not add Onyx's `SharedArmorSystem.cs:30` subscription** — duplicate pair, server-start crash.

**New `[TestPrototypes]` ids this WP claims** (§4 rule 3; all four re-grepped clear of the whole suite and of
`Resources/`): `WoundFoundationArmorHead`, `WoundFoundationArmorAllHead`, `WoundFoundationArmorLeftArm`,
`WoundFoundationArmorLocational`. Note Onyx's own id for the second is `WoundFoundationArmorAll`
(`ONYX WoundDamageFoundationTest.cs:97`); the rename is deliberate (it is worn in the **head** slot, and the
name must not read as "the armour that covers everything is the head armour"), so do not "correct" it back
during the port.

**PROTO B is 6 lines across 2 files**, not more: `_Mono/…/bulletproof_helmets.yml` contains exactly **one**
`- type: Armor` block (`:13`, on `ClothingHeadBPHelmetLight`, the file's only entity) and the vests file
contains **five** (`:12, :42, :74, :107, :137` → Light, Medium, Heavy, Polyvalent, Stabproof). Every other
helmet in the game — including `ClothingHeadHelmetSwat` (`Entities/Clothing/Head/helmets.yml:68-85`) — keeps
unset coverage and therefore keeps protecting every part. That asymmetry is the whole of §8.3 (P3-D26).

**Build checkpoint:** three builds green; Release YAML lint green (a stray `coverage: [Chest]` fails it);
`AppliesArmorExactlyOnceTest` (head 5 / torso 5), `NonWoundHostUsesVanillaArmorTest` (10 → 5) and
`ArmorPenetrationReachesWoundHostsTest` (5 / 10 / 10) **all still green unchanged** — Option B changes none of
them, because none of their fixtures declares `coverage:`.

---

### WP11-4 — Limb damage sprites (P3-4)

**Goal:** wounds show on the body sprite, per limb, for organic humanoids.

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | `Content.Client/Damage/DamageVisualsSystem.cs` (4 tagged regions) | `Content.Client/_WF/Wolfmed/Damage/DamageVisualsSystem.Wolfmed.cs` | **new** (§2.3) |
| 2 | — | `Content.Client/Damage/DamageVisualsSystem.cs` | **modified**, 2 insertions (1 + 4 lines) — **HOOK 20** |
| 3 | *(Option B)* — | `Content.Shared/Body/Part/BodyPartComponent.cs` | **modified**, 1 word — **HOOK 21** |
| 4 | *(Option B)* `Resources/Textures/_Onyx/Wounds/{brute,burn}_damage.rsi` | same | **new**, 156 files |
| 5 | *(Option B)* — | `Resources/Prototypes/Entities/Mobs/Species/base.yml` | **modified**, 2 lines — **PROTO C** |
| 6 | — | `Docs/Wolfmed/WOLFMED_MANIFEST.md` | append `### WP11-4` |

**Option A is mandatory; Option B (files 3-5) is pre-authorised and droppable.** If Option B is dropped,
**also drop HOOK 21** and record it as a P3-4 sub-item handed to phase 4.

**Why Option A is nearly free:** WG's stock humanoid already renders a whole-body brute/burn overlay
(`Resources/Prototypes/Entities/Mobs/Species/base.yml:61-75`, six `targetLayers`: Chest, Head, LArm, LLeg,
RArm, RLeg) driven off the mob's **aggregate** `DamagePerGroup` (`DamageVisualsSystem.cs:383`). Since D30 makes
a wound host's `DamageableComponent` the sum of all parts, **every targeted layer already lights up together on
any hit anywhere** — a chest hit makes the arm glow. `UpdatePartDamageVisuals` replaces the aggregate read with
a per-layer read of `PartDamageVisualsComponent.Damage[layer]`. WG's own
`Resources/Textures/Mobs/Effects/{brute,burn}_damage.rsi` already carries all 36 states
(`{Chest,Head,LArm,LLeg,RArm,RLeg}_{Brute|Burn}_{10,20,30,50,70,100}`) those six layers need — **zero new
texture files.**

**Option B licensing, checked:** Onyx's `_Onyx/Wounds/{brute,burn}_damage.rsi` (156 files: 2 `meta.json` +
154 `.png`) both declare `"license": "CC-BY-SA-3.0", "copyright": "Drawn by Ubaser."` — the **identical artist
and licence already shipped and accepted** in WG's own `Mobs/Effects/*_damage.rsi`. No new licensing review.

**Stale-overlay check, done:** `UpdateDisabledLayers` (`:395-421`) reads an appearance key nothing in WG sets,
but a just-detached limb's key naturally drops out of `PartDamageVisualsComponent.Damage` on the next refresh
(`RefreshBodyDamage` `.Clear()`s and only walks attached children), so `GetValueOrDefault` returns null →
threshold 0 → layer hidden. **Self-healing by construction.**

**Subscription pairs:** `<PartDamageVisualsComponent, AfterAutoHandleStateEvent>` (Option A) and
`<BodyPartComponent, AfterAutoHandleStateEvent>` (Option B). Both **verified free** (§5.1). Client
subscriptions are per-`IEntityManager` but the RT duplicate-registration crash is the same class of bug.

**Build checkpoint:** three builds green; Release YAML lint green; client starts and a wound host shows damage
on the struck limb only — **and on the matching arm/leg when you slash a hand or a foot** (P3-D25; that is the
one thing a rote port silently loses). **Re-run WP11-0's `T-VISUALS`**: it asserts the server-side projection
this package is the first consumer of, and it is the cheapest possible bisect if the sprite looks wrong.

---

### WP11-5 — Amputation and organ tests (P3-5)

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | `WoundBleedingTest.TraumaticAmputationCreatesSevereStumpBleedingTest` (`ONYX :173-199`) | `Content.IntegrationTests/Tests/_Onyx/Wounds/WoundBleedingTest.cs` | **modified** — restore the test, delete the `:150-153` skip note |
| 2 | `Content.IntegrationTests/Tests/_Onyx/Wounds/AmputationConsequenceTest.cs` (3 of 5) | same path | **new**, adapted |
| 3 | — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAmputationTest.cs` | **new** |
| 4 | — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedOrganTest.cs` | **new** |
| 5 | — | `Docs/Wolfmed/WOLFMED_MANIFEST.md` | append `### WP11-5` |

Assertions and derived expected values: §6.2. New `[TestPrototypes]` ids: `WolfmedAmputationBody*`,
`WolfmedAmputationOverflowPart`, `WolfmedOrganBody*`, `WolfmedOrganTestOrgan`, `WolfmedOrganTestProfile`,
`AmputationConsequenceTestBody/Torso/Head` — all checked clear of the §4 rule-3 pool.

**Mandatory fixture detail (do not drop it):** `AmputationConsequenceTestTorso` — the **parent** — carries

```yaml
  - type: WolfmedBodyPart
    amputationConsequenceSeverity: 50 # deliberately not the 35 default: proves edit #9 reads the PARENT
```

and both consequence tests assert **50**, not 35. Without it every phase-3 assertion reads `35` whether edit #9
takes the severity off the parent or off the severed part — `WolfmedBodyPartComponent.cs:25` defaults to 35 and
`WolfmedBodyPartSystem.Get` returns a zeroed singleton whose value is *also* 35 (`WolfmedBodyPartSystem.cs:6,9`),
and `_WF/Wolfmed/Body/parts.yml` (read in full) overrides it on no part. This one fixture line is what turns
§8.7 risk 7 from a silent-ship into a red test.

**Build checkpoint:** three builds green; `DockTest` green first; then the full phase-3 filter (§6.3).

---

### WP11-6 — Docs and manifest reconcile

| # | File | Action |
|---|---|---|
| 1 | `Docs/Wolfmed/WOLFMED_MANIFEST.md` | **reconcile**: amend the stale rows in §7.1, dedupe the five appended `### WP11-N` blocks, add the phase-3 deviations block |
| 2 | `Docs/Wolfmed/WOLFMED_PLAN3.md` | **new** — this plan, as shipped |
| 3 | `Docs/Wolfmed/WOLFMED_STATUS.md` | **modified** — amend `:85` (`PartDamageVisualsComponent` no longer deferred), `:90-91` (phase-3 items now done), and add §8's balance numbers plus the three known gaps (no organ healing, no organ readout, consequence wound inert) |

**Build checkpoint:** three builds green; Release YAML lint green; full phase-1/2/3 test filter green.

---

## 5. Duplicate directed subscription audit

RT stores **one** registration per `(component, event)` pair for the whole bus
(`EntityEventBus.Directed.cs:407,419`). Every row below was re-grepped in the current tree.

### 5.1 Every pair phase 3 registers

| # | Component | Event | Registrant | WP | Existing WG owner | Free? |
|---|---|---|---|---|---|---|
| 1 | `WoundableComponent` | `PartDamageOverflowedEvent` | `AmputationSystem.Initialize():28` | WP11-1 | **none** — repo-wide, `PartDamageOverflowedEvent` appears only at its declaration (`WoundEvents.cs:62`) and its raise (`WoundDamageRoutingSystem.cs:761`) | **YES** |
| 2 | `WolfmedOrganComponent` | `OrganFunctionChangedEvent` | `WolfmedOrganConsequenceSystem.Initialize()` | WP11-2 | **none** — the event appears only at its declaration (`OrganHealthSystem.cs:21`) and its raise (`:71`) | **YES** |
| 3 | `PartDamageVisualsComponent` | `AfterAutoHandleStateEvent` | `DamageVisualsSystem` (client) | WP11-4 | **none** — `grep -rn "PartDamageVisualsComponent" Content.Client` → **zero hits** | **YES** |
| 4 | `BodyPartComponent` | `AfterAutoHandleStateEvent` | `DamageVisualsSystem` (client), **Option B only** | WP11-4 | **none** | **YES**, but **dead without HOOK 21** (P3-D19) |

**WP11-3 registers nothing.** **WP11-0, WP11-5 and WP11-6 register nothing.** **P3-D1 registers nothing** — it
extends the body of `WolfmedBodyPartLifecycleSystem`'s existing `<WoundHostComponent, BodyPartRemovedEvent>`
handler (`:23`), which is exactly why it is the right home for the fix: that pair is already claimed, by us,
and it is already host-gated.

`WoundableComponent`'s full claimed-event set after phase 3, for the next phase's convenience:
`ComponentInit` + `RejuvenateEvent` (`WoundSystem.cs:33,36`), `DamageChangedEvent`
(`WoundDamageProjectionSystem.cs:38`), `BeforeDamageChangedEvent` (`WoundDamageRoutingSystem.cs:66`),
`PartDamageAppliedEvent` (`OrganDamageSystem.cs:31`), `Wound{Created,Changed,StateChanged,Removed}Event`
(`WoundStatusEffectSystem.cs:31-34`), `OrganGot{Inserted,Removed}Event` +
`BodyPartFunctionalityChangedEvent` (`FractureEffectsSystem.cs:34-36`), **`PartDamageOverflowedEvent`
(new, phase 3)**.

### 5.2 Pairs phase 3 deliberately does **not** register

| Pair | Owner | Consequence if added |
|---|---|---|
| `<WoundableComponent, PartDamageAppliedEvent>` | `OrganDamageSystem.cs:31` — the single fan-out point | server-start crash. `AmputationSystem.HandlePartDamageApplied` is a plain public method called from `:39`, never a subscription |
| `<ArmorComponent, InventoryRelayedEvent<PartDamageModifyEvent>>` | `WolfmedPartArmorSystem.cs:25` — sole subscriber | server-start crash. Onyx registers it in `SharedArmorSystem.cs:30`; **keep it in `_WF`** |
| `<OrganComponent, OrganEnableChangedEvent>` | `SharedBodySystem.Organs.cs:23` | server-start crash — **raise the event instead** (precedent `CyberneticsSystem.cs:27-28,45-46`), which is exactly what §2.2 does |
| `<OrganComponent, MapInitEvent>` | `SharedBodySystem.Organs.cs:22` | server-start crash |
| `<OrganComponent, OrganComponentsModifyEvent>` | `_Shitmed/BodyEffects/OrganEffectSystem.cs:23` | server-start crash |
| `<WoundHostComponent, BodyPartAddedEvent/RemovedEvent>` | `WolfmedBodyPartLifecycleSystem.cs:22-23` | server-start crash. Extend that system instead — and keep its `TerminatingOrDeleted` guards |
| `<BodyPartComponent, AmputateAttemptEvent>` | `SharedBodySystem.Parts.cs:41` | server-start crash. `WolfmedBodySystem` *raises* it |
| `<WoundableComponent, OrganGotInsertedEvent>` / `<…, OrganGotRemovedEvent>` | `FractureEffectsSystem.cs:34-35` (phase 2) | server-start crash |
| `<BrainComponent, OrganAddedToBodyEvent/RemovedFromBodyEvent>`, `<HeartComponent, …>`, `<NymphComponent, OrganRemovedFromBodyEvent>`, `<EyesComponent, OrganEnabledEvent/OrganDisabledEvent>` | `BrainSystem.cs:24,26`; `HeartSystem.cs:16,17`; `NymphSystem.cs:24`; `EyesSystem.cs:20-21` | already claimed |
| `<OrganComponent, OrganAddedToBodyEvent>` / `<OrganComponent, OrganRemovedFromBodyEvent>` | **nobody — both FREE** (P3-D21 corrects PLAN/PLAN2 §5.2) | phase 3 still does not use them; they belong to the deferred organs Option B |

### 5.3 Component-registration names

Phase 3 registers **no new component**. `AmputationSystem` as a type name is free (the only hits are the two
D26 comment lines in `OrganDamageSystem.cs:25-26` and two comment lines in `WoundBleedingTest.cs:151,153`).
`ArmorPartModifier` is a new `[DataDefinition]` type name with no collision. `WolfmedOrgan` and `OrganDamage`
are already registered (phase 1). `WolfmedOrganConsequenceSystem` is a new system type name — grep before
committing to it.

### 5.4 Ordering constraints

* `AmputationSystem` needs **no** `before:`/`after:` on its one subscription. `PartDamageOverflowedEvent` has a
  single subscriber, and the order that matters — wounds → fractures → amputation → bleeding — is enforced by
  `OrganDamageSystem`'s explicit call sequence, not by the bus.
* `WolfmedOrganConsequenceSystem` needs no ordering. Do **not** add one for the EMP race (§2.2).
* `WoundDamageRoutingSystem`'s existing `before: [typeof(SharedArmorPlateSystem)]` on both handlers
  (`:64,:66`) stays exactly as shipped — WP11-3 must not disturb it.

---

## 6. Test plan

### 6.1 Standing traps (apply to every phase-3 test)

1. **`DockTest` first.** `db.ef` sqlite warnings fail every pair test in this repo (project memory).
2. **`TerminatingOrDeleted`.** Any new reactor to `BodyPartAdded/Removed`, `OrganGot*` or organ destruction must
   guard before `EnsureComp`/`CreateOrMergeWound`. One unguarded spawn/dispose poisons the shared pooled pair
   for every other test in the run — it cost WP9 13 unrelated failures. WP11-2 file #4 exists for this.
3. **`Assert.Multiple` bodies must be fully synchronous** — no `async`/`await` inside the lambda.
4. **`[TestPrototypes]` ids are a global pool** (§4 rule 3).
5. **YAML lints in Release only** (`ErrorNode` crashes the linter elsewhere).
6. **Every Onyx literal is a prediction until measured** (P2-D16). Three are *known* wrong going in: Onyx's
   `BleedAmount >= 40f` (P3-D14), Onyx's two coverage tests (P3-D5/§6.2), and `tests.md`'s merge assertion
   (P3-D10). Put the derivation in a `// WOLFGATE` comment at each assertion.
7. **Amputation is deterministic; fracture creation is not.** No phase-3 test needs `creationChance` handling —
   but if one is ever combined with a fracture assertion, P2-D23 applies.
8. **Pain fixtures need `- type: StatusEffects` with explicit `allowed:` plus `- type: MobState`** (P2-D24).
   No phase-3 fixture currently needs it.
9. **Fixtures are Shitmed-shaped, not Nubody.** Onyx's `InitialBody` / `TransplantCompatibility` / `Injurable`
   do not exist in WG; rebuild on a `body:` graph the way `WoundBleedingTest.cs:27-49` does. Onyx's part-level
   `amputationThresholds:` must move to a `- type: WolfmedBodyPart` block (D8).

### 6.2 Test table

| # | Test | File | Setup | Derived expected values (and the derivation) |
|---|---|---|---|---|
| **T-REATTACH** | `ReattachedPartRejoinsWoundTrackingTest` | `_WF/Wolfmed/WolfmedReattachTest.cs` (WP11-0) | Real wound host; `wfBody.TryDetachPart(arm)`; `body.AttachPart(torso, "left arm", arm)`; **then deal fresh damage to the reattached arm** | `HasComp<WoundableComponent>(arm)` and `HasComp<DamageableComponent>(arm)` after reattach; `_wfPart.Get(arm).AmputationThresholds` still `{Slash 130, Piercing 250, Blunt 250}` (prototype data, never mutated at runtime); the fresh hit creates a **new** wound on the arm and the body's `BleedAmount` rises. **Closes PLAN §8.3 trap 2** — `WoundScarTest` proves wound data survives the round trip but never re-damages the part |
| **T-VISUALS** | `PartDamageProjectsToVisualsComponentTest` | `_WF/Wolfmed/WolfmedVisualsTest.cs` (WP11-0) | Real wound host; targeted Blunt to the **left arm only** | `Comp<PartDamageVisualsComponent>(body).Damage[HumanoidVisualLayers.LArm]` is a positive `DamageSpecifier` matching the dealt amount; `.RArm` is absent or zero. **Also hit the left hand and assert the damage lands under `HumanoidVisualLayers.LHand`** — that key is what `TryGetVisualLayer:253` writes and what the stock six-layer `targetLayers` list cannot render, which is the server-side anchor for P3-D25's fold. Repeat the read on `Pair.Client`'s mirrored entity to prove the networked state arrives. **Do not attempt a sprite-state or screenshot test** — `DamageVisualsSystem` rendering is not headlessly assertable, and this project prefers logic tests |
| **T-AMP-THRESHOLD** | `TraumaticAmputationCreatesSevereStumpBleedingTest` (restored) | `_Onyx/Wounds/WoundBleedingTest.cs` (WP11-5) | Existing `WoundBleedingBody`. `routing.TryApplyPartDamage(body, head, Spec("Slash", 200))`, then `Spec("Slash", 15)` | **Hit 1:** progress = 200/200 = **1.0** ≥ 1 → `Severable == true`, head **still attached** (the threshold hit never detaches). **Hit 2:** post-hit 215, progress 1.075 ≥ `SeverableResetRatio` 0.8; `damageBeforeHit` = 215 − 15 = **200** → `ReachedThreshold` true; `IsFinishingHit`: Slash 15 ≥ `DefaultDismembermentFinishingDamage["Slash"]` = **15** → true → detach. Torso holds a `DismembermentWound` at severity **200** (`DismembermentSeverities[Head]`) and an `AmputationConsequenceWound` at **35**. **`BloodstreamComponent.BleedAmount` is `Is.EqualTo(bloodstream.MaxBleedAmount)` = 10f, NOT Onyx's `>= 40f`** — raw rate 0.2 × 200 × awake 1.5 = 60, clamped at `BloodstreamSystem.cs:432` (P3-D14). Severed head is a live entity: `Assert.That(entities.Deleted(head), Is.False)` and `Transform(head).ParentUid != body` |
| **T-AMP-VITAL** | `DecapitationNeverReducesVitalDamageTest` | `_WF/Wolfmed/WolfmedAmputationTest.cs` (WP11-5) | **(a)** Wound host: record `mobThreshold.CheckVitalDamage(body, damageable)` immediately before the decapitating hit and again after. **(b)** D2 guard: a **non**-wound-host with a `BaseHead`-derived head (`MobMonkey`); remove its head and read the body's `Bloodloss` | (a) after − before == **+100** exactly, for any damage composition — the charge equals the head's own total (P3-D1) and Shitmed adds `VitalDamage` 100 on top (`SharedBodySystem.Parts.cs:395-408`). Assert `>= before` as the contract and `== before + 100` as the derivation. Head at 215 Slash → systemic 215 + 100 = **315** where `CheckVitalDamage` read 215. (b) **exactly 100** Bloodloss, unchanged from today. **(b) is the whole point of the P3-D1 redesign** — it is the assertion the withdrawn HOOK 19 would have failed |
| **T-AMP-NOGUN** | `BulletsNeverAmputateTest` | `_WF/Wolfmed/WolfmedAmputationTest.cs` (WP11-5) | Real `MobHuman` wound host; 20 × `Spec("Piercing", 14)` into one targeted **hand** | The hand becomes `Severable == true` (at hit 15, cumulative 210 > the Hand Piercing threshold **200**) but is **never detached**, because `DefaultDismembermentFinishingDamage["Piercing"]` is **40** and 14 < 40 — forever, at any accumulated damage. **This test pins P3-D4 so a future tune is a visible test change, not a silent balance drift** |
| **T-AMP-OVERFLOW** | `OverflowAmputationMechanismIsInertOnShippedLimbsTest` | same file | (a) Real `MobHuman`: **20 × `Spec("Blunt", 25)`** into one targeted arm (500 total, in deliberately sub-finishing chunks), then **one `Spec("Blunt", 50)`**. (b) A bespoke `[TestPrototypes]` part `WolfmedAmputationOverflowPart` with `- type: WolfmedBodyPart` `maxDamage: 50` + thresholds: drive it past 50 | (a) `WoundableComponent.AmputationOverflow == 0` after every one of the 20 hits (the accumulator early-returns at `WoundDamageRoutingSystem.cs:799` because `_wfPart.Get(arm).MaxDamage == 0`); `Severable == true` from hit 10 (250 = the arm's Blunt threshold); **still attached at 500**; then the single 50 detaches it, proving the threshold path is alive. **The 25s are load-bearing and must carry a comment:** `DefaultDismembermentFinishingDamage["Blunt"]` is **50**, so any chunk ≥ 50 makes hit 11 a finishing hit and the arm comes off at ~300 — the old "drive an arm to 500" setup was unreachable, and once detached `bodyPart.Body == null` short-circuits `HandlePartDamageApplied` at `ONYX :72` so the `AmputationOverflow == 0` assertion stops meaning anything. (b) `AmputationOverflow > 0` accumulates, proving the mechanism is data-gated, not code-dead. **This is a canary against a future `parts.yml` edit, not a play-path test** (P3-D12) |
| **T-AMP-EXPLOSION** | `ExplosionAmputatesDeterministicallyTest` | same file | `routing.TryRouteDistributedDamage(body, Spec("Slash", 260), TargetBodyPart.LeftArm, DamageDistribution.SplitEvenly, variation: 0f, isExplosion: true)` — signature verified at `WoundDamageRoutingSystem.cs:904-914` | Left arm detaches **in this single call**. Determinism has **nothing to do with the CVars** (they have zero live consumers — P3-D3/F5): the single-part mask leaves `PickExplosionAmputationCandidate` exactly one candidate, so `_random.NextFloat()` cannot change the outcome; and `progress = 260/130 = 2.0` makes `chance = clamp(progress × 0.5, 0, 1)` saturate to exactly **1.0**, so `_random.Prob(1.0)` is always true. `IsFinishingHit`: Slash 260 ≥ 15 ✓. **Assert the right arm is unaffected**, proving the mask drove the pick. Put a doc-comment on the test saying it exercises the routing API's branch, **not** an end-to-end grenade path (D24) |
| **T-AMP-CONSEQUENCE-SEPARATE** | `RepeatedAmputationCreatesSeparateConsequenceWoundsTest` | same file | Amputate the left arm, then the right arm — both parented to the same torso | Torso holds **exactly two** `AmputationConsequenceWound` entities at the **torso's own** `amputationConsequenceSeverity` (**50** with the WP11-5 fixture line; 35 on a stock body), and **two** `DismembermentWound` entities at **120 each** (`DismembermentSeverities[Arm]`). **Both prototypes ship `mergeMode: SeparateInstances`** (`wounds.yml:389,397`, byte-identical to Onyx), and `CreateOrMergeWoundInternal` only searches for an existing wound under `MergeByPrototype` (`WoundSystem.cs:185`). **Replaces `tests.md`'s T-AMP-CONSEQUENCE-MERGE, which asserted the opposite** (P3-D10) |
| **T-AMP-CONSEQUENCE-1** | `TraumaticAmputationCreatesConsequenceTest` (ported, reduced) | `_Onyx/Wounds/AmputationConsequenceTest.cs` (WP11-5) | Onyx's own bespoke two-part body, D8/D9-translated: test head carries `- type: WolfmedBodyPart` `amputationThresholds: {Slash: 70}`; torso is `partType: Torso` (Onyx: `Chest`). `Slash 70`, then `Slash 15` | `Severable == true` after 70; detached after +15; torso holds `.Single(w => w.Comp.Prototype == "AmputationConsequenceWound")` at severity **50** — the fixture torso carries `- type: WolfmedBodyPart` `amputationConsequenceSeverity: 50` **specifically so this assertion can tell the parent from the part** (§4/WP11-5). Asserting 35 here proves nothing: `WolfmedBodyPartComponent.cs:25` defaults to 35 and `WolfmedBodyPartSystem.Get`'s zeroed singleton is also 35, so a redirect to the severed part would still read 35. **Onyx's `graph.HasAmputationConsequence(torso)` and `graph.TryAttachPart(torso, spare) Is.False` assertions are DROPPED** — B-2/P3-D2: Shitmed has no such gate |
| **T-AMP-CONSEQUENCE-2** | `HealingPartAboveThresholdDoesNotAmputateTest` (ported verbatim) | same | Part at Slash 80 (above its 70 threshold, `Severable`); apply `Spec("Slash", -1)` | Part still attached; `damage.GetAllDamage(head).DamageDict["Slash"] == 79`. A *heal* is never a finishing hit |
| **T-AMP-CONSEQUENCE-3** | `HealingBelowResetRatioClearsSeverableTest` (ported verbatim) | same | `Slash 70` → `Slash -15` → `Slash 15` | After 70: `Severable == true`. After −15: 55; progress 55/70 = **0.786** < `SeverableResetRatio` **0.8** → `Severable == false`. After +15: 70 again, but `damageBeforeHit` = 55 → `ReachedThreshold(55/70 = 0.786)` is **false** → **still attached**. Exercises the full reset cycle |
| — | `SurgicalHealRemovesConsequenceAndUnblocksTest` | — | — | **NOT PORTED.** Needs `SurgeryStepEvent` + `SurgeryTreatWoundEffect` (D7, phase 4). Record the skip in the file |
| — | `HealingDamageKeepsConsequenceBlockedTest` | — | — | **NOT PORTED as written.** Its payload is the `TryAttachPart … Is.False` assertion, which B-2/P3-D2 removes. Record the skip |
| **T-ORG-DATA** | `OrganPrototypesCarryWolfmedDataTest` | `_WF/Wolfmed/WolfmedOrganTest.cs` (WP11-5) | Prototype-level; no mob spawn | Each of the seven `OrganHuman*` ids resolves with `WolfmedOrgan` (15/15) and `OrganDamage` at WP11-2's numbers. **Cheap, and it catches a mistyped `parent:`** |
| **T-ORG-CAP** | `OrganDamageIsCappedPerApplicationTest` | same | Bespoke fixture, `organDamage.chances` forced to 1.0, one huge Piercing torso hit | Organ health drops by **at most `MaxHealth × MaxDamageFraction` = 15 × 0.3 = 4.5**, i.e. health ≥ **10.5**, regardless of hit size (`OrganDamageSystem.cs:75-76`; `MaxDamageFraction` default 0.3 at `OrganDamageComponent.cs:22`, overridden by no prototype in either tree) |
| **T-ORG-DESTROY** | `OrganDestructionMergesConsequenceWoundTest` | same | `_organHealth.SetHealth(organ, 0)` directly, then tick once | Organ is gone from `GetPartOrgans(torso)` and `Deleted`; the torso holds `InternalBleedingWound` at the prototype severity. Drive it through `SetHealth` rather than through damage so the test is deterministic and independent of the ~1 % per-hit roll |
| **T-ORG-HEART** | `DestroyedHeartAppliesDelayedDeathTest` | same | Heart to 0, tick | `HasComp<DelayedDeathComponent>(body)`. **The single most important assertion in WP11-2** — it is the only proof that the Shitmed consequence chain (P3-D8's whole argument) actually fires from `OrganHealthSystem.DestroyOrgan` |
| **T-ORG-BRAIN** | `DestroyedBrainKillsWithoutDeletingOrganTest` | same | Brain to 0, tick | Mob is `Dead`, **and the brain organ is NOT deleted** (`OrganHealthSystem.cs:45-54`'s `continue`). Pins P3-D22 |
| **T-ORG-EYES** | `DestroyedEyesBlindTest` | same | Eyes to 0, tick | `HasComp<TemporaryBlindnessComponent>(body)` (`EyesSystem.cs:83`) |
| **T-ORG-FUNC** | `ZeroHealthOrganRevokesGrantedComponentsTest` | same | `[TestPrototypes]` organ with `- type: Organ` `onAdd:` granting a marker component; drop it to 0 **without ticking** | The granted component is revoked from the body in the one-tick window before destruction. **Then tick once and re-assert**: `DestroyOrgan` → `RemoveOrgan` raises `OrganEnableChangedEvent(false)` a second time (`SharedBodySystem.Organs.cs:65-66`) and `OnOrganEnableChanged` does not early-return on an unchanged value (`:273-290`), so the revoke runs twice; the component must stay revoked and nothing may throw. **This is the only coverage of §2.2's `WolfmedOrganConsequenceSystem`** |
| **T-ORG-INERT** | `NonWoundHostTakesNoOrganDamageTest` | same | A non-wound-host mob with the same organs takes heavy torso damage | No organ loses health. D2/D3/D32 regression guard |
| **T-P3ARM-1** | `AppliesLocationalArmorExactlyOnceTest` (ported) | `_Onyx/Wounds/WoundDamageFoundationTest.cs` (WP11-3) | New `WoundFoundationArmorHead` (`coverage: [Head]`, `Blunt 0.5`) in `outerClothing`; `TryApplyPartDamage` Blunt 10 to head and to torso | head **5**, torso **10**. **RED against Onyx's own shipped code** (Onyx's fallback would give torso 5) — green only under Option B (P3-D5). Use `WolfmedDamageableSystem` for the reads, as the existing test does at `:440` |
| **T-P3ARM-2** | `EmptyCoverageAndSymmetryTest` (ported) | same | **A:** new `WoundFoundationArmorAllHead` (no `coverage`) in the **`head`** slot; hit the **torso** 10. **B:** new `WoundFoundationArmorLeftArm` (`coverage: [Arm]`, `coverageSymmetry: [Left]`) in `outerClothing`; hit both arms 10 | A: torso **5** — empty/unset coverage protects everything and the worn slot is irrelevant (Onyx's deliberate slot/part mismatch, `ArmorComponent.Locational.cs:10`). B: leftArm **5**, rightArm **10**. **Part B is RED against Onyx's shipped code**; green only under Option B. Part A also guards against an accidental slot-derived default creeping in |
| **T-P3ARM-3** | `LocationalModifierOverridesAndFallbackTest` (ported) | same | New `WoundFoundationArmorLocational`: `coverage: [Torso]` (**D9: Onyx writes `[Chest]`**), global `Blunt 0.8`, `partModifiers` Head 0.25 / Arm+Left 0.5 / Arm+Right 0.75; 20 Blunt to each of the four parts | head **5**, torso **16**, leftArm **10**, rightArm **15**. **Green under both Option A and Option B** — port unconditionally. Note the head takes 5 *despite* `coverage: [Torso]`, which is exactly why the gate must sit after the loop |
| **T-P3ARM-AP** | `PartModifiersRouteThroughArmorPenetrationTest` | same | Same locational armour, `armorPenetration: 1f`, targeted at the head | head takes **20** (full). Nothing in Onyx covers this; **without it the new `partModifiers` branch can silently kill every AP weapon** |
| **T-P3ARM-UNCOVERED-AP** | `UncoveredPartIgnoresArmorPenetrationTest` | same | `WoundFoundationArmorHead` (`coverage: [Head]`), hit the **torso** at AP 0 and AP 1 | **10 in both cases** — the gate returns before any modifier maths, so AP is irrelevant on an uncovered part |

**Existing tests that must stay green, unchanged:** `AppliesArmorExactlyOnceTest`
(`WoundDamageFoundationTest.cs:421-451`, head 5 / torso 5), `NonWoundHostUsesVanillaArmorTest` (`:453-472`),
`ArmorPenetrationReachesWoundHostsTest` (`WolfmedDamageBridgeTest.cs:361-405`, 5 / 10 / 10). None of their
fixtures declares `coverage:`, so Option B is a no-op for all three. **Re-derive each by hand anyway** —
`armour.md` §11.7 is right that this is the class of regression that ships silently.

**Explicitly not tested / not portable:** the two `AmputationConsequenceTest` tests above; Onyx's
`BodyConsequencesTest` inventory-slot coupling and `StandUpAttemptEvent` (neither mechanism exists in WG,
confirmed absent, and nothing in phase 3 adds them); `RepairSelectionAndSnapshotValidationTest`
(`_Onyx.Repairable` is not in scope — record the skip); any sprite-pixel assertion for P3-4.

### 6.3 Run commands

```
dotnet build Content.Server/Content.Server.csproj -c DebugOpt -v q -nologo
dotnet build Content.Client/Content.Client.csproj -c DebugOpt -v q -nologo
dotnet build Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt -v q -nologo
dotnet run --project Content.YAMLLinter -c Release
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt --no-build --filter "FullyQualifiedName~DockTest"
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt --no-build \
  --filter "FullyQualifiedName~_Onyx.Wounds|FullyQualifiedName~_Onyx.Body|FullyQualifiedName~Wolfmed" \
  --logger "console;verbosity=detailed"
```

Run each new suite individually during development; the combined filter is the final gate.

---

## 7. Manifest rows

### 7.1 Row changes (existing rows) — WP11-6 reconciles these

| Manifest line | Change |
|---|---|
| `:80` `AmputationSystem.cs … skipped … WP11` | → **`new (vendored), adapted` / WP11-1**, note: "18 `// WOLFGATE` sites (D8 `_wfPart.Get` redirects ×11, D9 Chest→Torso ×3, D12 facade, `GetParentPartOrNull` ×2, `TryDetachPart` shim, 2 signature changes per P3-D15). Stays in `Content.Shared` — no server-only dependency (P3-D11). Registers `<WoundableComponent, PartDamageOverflowedEvent>`" |
| `:85` `OrganDamageSystem.cs` D26 note | **CORRECT, then append.** The existing row names the disabled sites as `:24` and `:36`; the real ones are **`:25-26`** and **`:38-39`** (file re-read here). Fix those first, then append "**D26 lifted in WP11-1:** both restored; call order `_wounds` → `_fractures` → `_amputation` → `_bleeding` is load-bearing. **Owned exclusively by WP11-1** (P3-D24)". Appending to a wrong row leaves it self-contradicting |
| `:89` `OrganHealthSystem.cs` | append "**WP11-2 adds two `TerminatingOrDeleted` guards** (P3-D23) — `DestroyOrgan` runs during `RecursiveDeleteEntity`, the exact failure WP9 fixed in `WolfmedBodyPartLifecycleSystem`" |
| `:90` `FunctionalOrganComponent.cs … skipped … WP11` | → **`skipped` permanently**, note: "P3-D8 — maps 1:1 onto Shitmed's `OrganComponent.OnAdd` + `_Shitmed/BodyEffects/OrganEffectSystem.cs:53-59`. Onyx's only prototype users are exotic implants Wolfgate does not have" |
| `:91` `WolfmedOrganComponent.cs … No prototype carries it until WP11` | → "**WP11-2 lands the prototypes**: `_WF/Wolfmed/Body/organs.yml` + 7 `parent:` edits in `Body/Organs/human.yml`. `tests.md` F4's claim that these fields need a new upstream `OrganComponent` hook is **wrong** — they have lived here since WP6 (P3-D9)" |
| `:122` `WolfmedPartArmorSystem.cs` | append "**WP11-3 rewrote the handler**: Onyx's first-match-wins `PartModifiers` loop plus a `Coverage`/`CoverageSymmetry` gate on the fallback only (P3-D5, Option B). Both branches wrap `PenetrateArmor` (D23). Onyx's `MaskComponent.IsToggled` gate deliberately not ported" |
| `:132` `WoundBleedingTest.cs … traumatic-amputation test skipped` | → "5 of Onyx's 6. **`TraumaticAmputationCreatesSevereStumpBleedingTest` restored in WP11-5** with the `>= 40f` assertion corrected to `MaxBleedAmount` (10f) — P3-D14. Tourniquet test still skipped (phase 4)" |
| `:395-397` textures-not-ported deviation | **Option B only:** mark superseded — "WP11-4 ported both RSIs (156 files, CC-BY-SA-3.0 / Ubaser, the licence and artist already accepted in `Mobs/Effects/*_damage.rsi`) for severed-limb wound rendering". **If Option A ships alone, leave this row standing and add a P3-4 hand-off note** |
| `:794-796` `PartDamageVisualsComponent` deferred-to-phase-3 note | → **resolved in WP11-4**; name HOOK 20 and the `_WF` client partial |
| `:845-850` the `maxDamage` hazard note | **CORRECT IT.** "`maxDamage` gates only the overflow branch, which is dead for every organic limb in Onyx too; `amputationThresholds` is what gates severing and WP7 populated it on all ten limb abstracts. **No YAML gap exists and none of the analysis that followed from this note was needed**" (P3-D12) |

### 7.2 New rows

| Onyx path | Wolfgate path | Status | WP | Notes |
|---|---|---|---|---|
| `Content.Shared/_Onyx/Wounds/AmputationSystem.cs` | same | new (vendored), adapted | WP11-1 | see `:80` above. No licence header in Onyx — do not invent one |
| — | `Content.Server/_WF/Wolfmed/WolfmedBodyPartLifecycleSystem.cs` | modified | WP11-1 | **P3-D1** — `ChargeVitalPartLoss` in the existing `OnPartRemoved`; charges systemic Bloodloss equal to a lost vital part's own damage. Replaces the withdrawn HOOK 19 (`BaseHead.vitalDamage: 300`), which was a D2 breach: 26 `BaseHead` descendants, ten of them non-hosts, and `PartRemoveDamage` is not host-gated. No upstream file, no new subscription, no balance deviation |
| — | `Resources/Prototypes/_WF/Wolfmed/Body/organs.yml` | new | WP11-2 | 7 abstracts, Onyx values verbatim from `base_organs.yml`. `health`/`maxHealth` omitted (C# defaults 15/15 are already Onyx's) |
| — | `Resources/Prototypes/Body/Organs/human.yml` | modified | WP11-2 | **PROTO A**, 7 one-line `parent:` edits at `:53,103,150,189,215,249,270`. Tongue/appendix/ears deliberately not edited — no WG body graph slots them |
| — | `Content.Server/_WF/Wolfmed/Body/WolfmedOrganConsequenceSystem.cs` | new | WP11-2 | P3-D8. `<WolfmedOrganComponent, OrganFunctionChangedEvent>` (free); **raises** `OrganEnableChangedEvent` rather than subscribing it (precedent `CyberneticsSystem.cs:27-28`) |
| `Content.Shared/Armor/ArmorComponent.Locational.cs` | `Content.Shared/_WF/Wolfmed/Armor/ArmorComponent.Wolfmed.cs` | new | WP11-3 | P3-D5. `partial ArmorComponent` in `namespace Content.Shared.Armor` — zero upstream edits. `Coverage`/`CoverageSymmetry` nullable (deviation); no `[AutoNetworkedField]`; `traumaDeductions` not ported (P3-D20) |
| — | `Resources/Prototypes/_Mono/…/Head/Helmets/bulletproof_helmets.yml`, `…/OuterClothing/Armor/bulletproof_vests.yml` | modified | WP11-3 | **PROTO B**, one `coverage:` line per `- type: Armor` block. The only content annotated in phase 3 (P3-D6) |
| `Content.Client/Damage/DamageVisualsSystem.cs` (4 tagged regions) | `Content.Client/_WF/Wolfmed/Damage/DamageVisualsSystem.Wolfmed.cs` | new | WP11-4 | P3-D18. Re-authored onto WG's calling convention, not vendored — Onyx's `ProtoMan` becomes `_prototypeManager` |
| — | `Content.Client/Damage/DamageVisualsSystem.cs` | modified (hook) | WP11-4 | **HOOK 20**, 2 insertions (1 + 4 lines), bodies in the `_WF` partial |
| — | `Content.Shared/Body/Part/BodyPartComponent.cs` | modified (hook) | WP11-4 | **HOOK 21**, Option B only, 1 word. Zero existing subscribers of the pair it enables (P3-D19) |
| `Resources/Textures/_Onyx/Wounds/{brute,burn}_damage.rsi` | same | new (Option B) | WP11-4 | 156 files, CC-BY-SA-3.0 / Ubaser |
| — | `Resources/Prototypes/Entities/Mobs/Species/base.yml` | modified (Option B) | WP11-4 | **PROTO C**, 2 `sprite:` lines. **Onyx's `HumanoidVisualLayers.Groin` targetLayer is NOT ported** (D9) |
| — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedReattachTest.cs` | new | WP11-0 | T-REATTACH; closes PLAN §8.3 trap 2 |
| — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedVisualsTest.cs` | new | WP11-0 | T-VISUALS; tests WP5 data that had zero consumers |
| `Content.IntegrationTests/Tests/_Onyx/Wounds/AmputationConsequenceTest.cs` | same | new, adapted | WP11-5 | **3 of Onyx's 5.** `SurgicalHealRemovesConsequenceAndUnblocks` skipped (D7); `HealingDamageKeepsConsequenceBlocked` skipped (its payload is the B-2 attach gate). Onyx's `HasAmputationConsequence`/`TryAttachPart` assertions dropped from test #1 |
| — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAmputationTest.cs` | new | WP11-5 | T-AMP-NOGUN, T-AMP-OVERFLOW (canary, P3-D12), T-AMP-EXPLOSION (routing-API branch coverage, not end-to-end), T-AMP-CONSEQUENCE-SEPARATE, **T-AMP-VITAL** (P3-D1, including the non-host D2 guard) |
| — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedOrganTest.cs` | new | WP11-5 | 7 tests. **First-ever coverage of this mechanism in either codebase** — Onyx ships `OrganDamageSystem`/`OrganHealthSystem` with zero tests |

### 7.3 Deviations block to add (phase 3)

1. **Losing a vital part charges its own damage as systemic Bloodloss on wound hosts** (P3-D1) — a Wolfgate
   invention with no Onyx counterpart, because Onyx's `CheckVitalDamage` equivalent has the same blind spot and
   Onyx never noticed. **Not** a balance deviation: the charge is exactly the damage that left with the part, so
   decapitation reads *pre-decapitation total + `VitalDamage` (100)*, which is what a non-wound-host already
   sees. Non-hosts are untouched (the handler is `<WoundHostComponent, BodyPartRemovedEvent>`).
2. **`AmputationConsequenceWound` is inert** (P3-D2). Onyx blocks reattachment through Nubody
   `HasAmputationConsequence`; Shitmed has no equivalent gate and D7 skips Onyx surgery. Phase-4 item.
3. **Explosion amputation ships inert** (P3-D3). D24 + the `_routedModifiers` plate-protection hole.
4. **No Wolfgate firearm or laser can amputate** (P3-D4). Onyx defaults; pinned by T-AMP-NOGUN.
5. **`Coverage`/`CoverageSymmetry` are implemented as Onyx documents them, not as Onyx ships them** (P3-D5).
   Onyx's own two coverage tests are red against Onyx's own code.
6. **`ArmorComponent.Coverage`/`CoverageSymmetry` are nullable in Wolfgate**, unlike Onyx's `= []`.
7. **Onyx's `MaskComponent.IsToggled` armour gate is not ported** (base-game drift, and gating only the part
   path would armour the torso and not the head).
8. **`traumaDeductions` is not ported** (P3-D20) — orphaned YAML key with no C# in either tree.
9. **`_Mono` armour plates stay whole-body** — a plate carrier protects your feet. Recorded, not fixed.
10. **The armour examine verb overstates protection for coverage-gated items.** Onyx has the identical gap;
    fixing it needs a new locale string. Phase-4 content item.
11. **Organ damage covers only human-lineage organs** (P3-D7). ~20 other organ roots (animal, arachnid, diona,
    slime, hydrakin, feroxi, chitinid) are silent no-ops until phase 4's data-driven option.
12. **Organ damage is completely irreversible** — nothing in Wolfgate raises organ health; Onyx's only healer is
    its own surgery (D7). Shitmed can replace an organ but never repair one.
13. **Organ damage is invisible to players** — no health-analyzer organ readout, no examine text, no alert.
    Phase-4 item.
14. **Cybernetic organs inherit organic organ-damage policy** through `OrganHumanEyes`/`OrganHumanHeart`.
    Deliberate; phase 5 owns the cybernetic profile.
15. **Seven of Onyx's nine organ-consequence pieces are skipped** (P3-D8), including
    `MissingHeartComponent` and `BodyStasis.cs` (the DECISIONS.md question is answered **no**).
    `BreathingImmunityComponent` **must not** be ported — name collision, server-start crash.
16. **Onyx's stump-bleed literal corrected** from `>= 40f` to `MaxBleedAmount` (10f) (P3-D14).
17. *(Option A only)* **Severed limbs render without wound art.** `RefreshDetachedDamage` has fed
    `PartDamageVisualsComponent` on detached roots since phase 1 with no consumer; the detached-rendering half
    of P3-4 is handed forward.
18. **Hand and foot wounds render on the arm and the leg, not on the hand and the foot** (P3-D25). WG's
    `targetLayers` list has six entries and its `brute_damage.rsi` has no `LHand/RHand/LFoot/RFoot` states;
    Onyx is identical. The fold preserves today's behaviour exactly — without it P3-4 would make those wounds
    **invisible**, which is a regression, not a port.
19. **A decapitating blow's reported damage total is inflated by the vital charge.** `ApplySystemicDamage` folds
    it into the outer `Applied` delta (`WoundDamageRoutingSystem.cs:1074`), so `TryChangeDamage`'s return value
    for the killing swing carries the head's damage + 100 as `Bloodloss`. Shitmed's own `VitalDamage` already
    did this for 100; P3-D1 widens it. Verified log-only in the tree today
    (`SharedMeleeWeaponSystem.cs:597`); the Blunt-keyed stamina branch at `:588` is unaffected.
20. **A re-attached vital part does not refund its charge** (P3-D1a). Phase-4 surgery item.
21. **Three of DECISIONS P3-1's four `WolfmedBodyPartComponent` fields ship unset.** `parts.yml` sets only
    `amputationThresholds` (plus `maxDamage` on the torso); `dismembermentFinishingDamage`,
    `amputationConsequenceSeverity` and `dismembermentSeverity` run on their C# defaults, **which are Onyx's own
    values** and which no prototype in either tree overrides. Literal deviation from the binding text, zero
    behavioural difference (P3-D13, §8.1).

---

## 8. Risks, balance numbers and user decisions

### 8.1 P3-6 — amputation thresholds, finishing damage and dismemberment severity

All read from the shipped `Resources/Prototypes/_WF/Wolfmed/Body/parts.yml` (read in full) and
`Content.Shared/_Onyx/Wounds/WoundDamageComponents.cs:50-78`, cross-checked against
`ONYX Resources/Prototypes/Body/base_organs.yml` and `chest_groin.yml`.

| Part | Slash T | Piercing T | Blunt T | Dismemberment severity on the parent | `MaxDamage` |
|---|---|---|---|---|---|
| Head | **200** | **200** | **350** | **200** | 0 (overflow inert) |
| Arm (L/R) | **130** | **250** | **250** | **120** | 0 |
| Hand (L/R) | **70** | **200** | **150** | **80** | 0 |
| Leg (L/R) | **150** | **250** | **300** | **120** | 0 |
| Foot (L/R) | **80** | **220** | **170** | **80** | 0 |
| Torso | — (thresholds empty; excluded from amputation regardless) | — | — | 100 (`DefaultDismembermentSeverity`, unreachable) | **250** (the only nonzero one in the game; gates the dead overflow branch) |

Fixed values, C# defaults, overridden by no prototype in either tree (this is §7.3 item 21: DECISIONS P3-1
asks for all four `WolfmedBodyPartComponent` fields "populated for human parts", and three of them ship unset
because the C# defaults already **are** Onyx's numbers — equivalent in effect, recorded as a literal deviation):

| Value | Source | Number |
|---|---|---|
| Finishing minimum, Slash | `WoundDamageComponents.cs:74` | **15** |
| Finishing minimum, Piercing | `:75` | **40** |
| Finishing minimum, Blunt | `:76` | **50** |
| `AmputationConsequenceSeverity` (on the **parent**, every amputation) | `WolfmedBodyPartComponent.cs:25` | **35** |
| `SeverableResetRatio` | `WoundDamageComponents.cs:70` | **0.8** |
| Explosion sever chance | `ONYX AmputationSystem.cs:188` | `clamp(progress × 0.5, 0, 1)` |

**The rule, stated exactly.** `progress = Σ_type damage[type] / T[type]`.
(1) Not `Severable` and `progress ≥ 1` → become `Severable`, **this hit does not detach**.
(2) `Severable` and `progress < 0.8` → clear `Severable`.
(3) `Severable`, `progress ≥ 0.8`, **pre-hit** progress ≥ 1, and the hit contains a type present in `T` with
`amount ≥ finishingMinimum[type]` → **detach**.
With uniform hits of `d` of one type against threshold `T`: **`ceil(T/d) + 1` hits**.

### 8.2 P3-6 — what that means against real Wolfgate weapons

Weapon damage read from WG prototypes: `BaseBullet` **Piercing 14**
(`Entities/Objects/Weapons/Guns/Projectiles/projectiles.yml:106`); `BaseBulletAP` Piercing 11 (`:191`);
`BulletLaser` **Heat 16** (`:1186`); `EnergySword` active **Slash 50** (`Melee/e_sword.yml:124`); `Katana`
Slash 40 (`Melee/sword.yml:101`); `Machete`/large-melee baseline **Slash 32** (`Melee/sword.yml:121,132`);
`FireAxe` Slash 25; `CombatKnife` Slash 22 (`Melee/knife.yml:109`).

Hits to amputate, unarmoured, all landing on the same targeted part:

| Part (Slash T) | Energy sword (50) | Katana (40) | **Machete (32)** | Combat knife (22) | **Any bullet (Piercing 14)** | **Laser (Heat 16)** |
|---|---|---|---|---|---|---|
| Hand — 70 | 3 | 3 | **4** | 5 | **never** | **never** |
| Foot — 80 | 3 | 3 | **4** | 5 | never | never |
| Arm — 130 | 4 | 5 | **6** | 7 | never | never |
| Leg — 150 | 4 | 5 | **6** | 8 | never | never |
| Head — 200 | 5 | 6 | **8** | 11 | never | never |

**Two independent reasons no gun can ever amputate.** (1) `DefaultDismembermentFinishingDamage["Piercing"]`
is **40**; a 14-damage round is never a finishing hit, at any accumulated damage, forever. (2) Even ignoring
that, the Piercing thresholds are 200–250 — 15–18 rounds into one limb. **Lasers are excluded entirely**:
`Heat` appears in no `amputationThresholds` dict, and both `GetThresholdProgress` and `IsFinishingHit` iterate
the *threshold* dict, so `progress` stays 0.

**Armour scales this.** A vest with a 0.7 Slash coefficient turns a machete from 32 to 22.4 — still above the
Slash-15 finishing minimum, but arm hits go from 6 to 8. **A vest at ≤ 0.46 Slash pushes a machete below the
finishing minimum and makes that limb unamputatable by machete.** With P3-D6's annotation the heavy vest
(`Slash 0.30`) does exactly this to the torso-arm-leg set: 32 × 0.30 = 9.6 < 15.

**One-line summary for the user: with Onyx defaults, amputation in Wolfgate is a melee-only mechanic. Three to
eight deliberate slashes to the same limb take it off; no firearm or laser in the game can do it at any range
or volume.** If that is not the intended feel, the knobs are
`WoundHostComponent.DefaultDismembermentFinishingDamage["Piercing"]` (40 → ~12-14) and the Piercing thresholds
in `parts.yml` (200-250 → ~60-120). That is a D4 balance change and needs its own explicit decision.

### 8.3 P3-6 — per-limb armour protection

**Where hits land.** Every projectile deals damage with `origin: component.Shooter`
(`Content.Shared/Projectiles/SharedProjectileSystem.cs:164-169`); routing resolves the part from the origin's
`TargetingComponent`; **`TargetingComponent.Target` defaults to `TargetBodyPart.Torso`**
(`Content.Shared/_Shitmed/Targeting/TargetingComponent.cs:14`) and `targeting.enabled` defaults **true**.
**So every shot from a player who never touches the targeting doll hits the torso.**

Unaimed hits (environmental, traps, most non-player damage) fall to a weighted roll over attached parts,
weights from `WoundDamageComponents.cs:18-29` (Torso 4, Head 1, Arm 2, Hand 1, Leg 2, Foot 1), total **17** on
a stock human:

| Part | Share of an unaimed hit |
|---|---|
| Torso | **23.5 %** |
| Head | **5.9 %** |
| Each arm / each leg | 11.8 % |
| Each hand / each foot | 5.9 % |

**Heavy bulletproof vest (`Piercing 0.25`) against a random unaimed bullet:**

| Coverage rule | Fraction of hits protected | Average Piercing mitigation |
|---|---|---|
| Today / unannotated (protects everything) | 100 % | **75.0 %** |
| `[Torso]` only | 23.5 % | **17.6 %** |
| **P3-D6's `[Torso, Arm, Leg]`** | **70.6 %** | **52.9 %** |

**Against an aimed torso shot — the default and overwhelmingly common case — every row above is identical at
75 %.** Locational armour does not nerf vests in normal PvP; it nerfs them against splash, environmental
damage and deliberate limb-shooting.

**Light ballistic helmet (`Piercing 0.85`) with `coverage: [Head]`:** aimed head shot **15 %** (unchanged);
random unaimed hit `5.9 % × 15 % =` **0.88 %** average (down from 15 %); **aimed torso shot 0 % (today: 15 %)**.

**The number that actually changes in aimed PvP is head protection, not torso protection** (P3-D26 — the
earlier draft of this section got this backwards). Armour stacks multiplicatively across worn slots, and
PROTO B annotates **only** the five `_Mono` vests and the one `_Mono` light ballistic helmet. Every other
helmet in the game keeps unset coverage and therefore keeps protecting the torso as it does today. So the
annotation is **not** symmetrical: vests stop protecting heads, while unannotated helmets go on protecting
torsos.

Piercing coefficients, all re-read: heavy BP vest **0.25** (`bulletproof_vests.yml:79`), BP light helmet
**0.85** (`bulletproof_helmets.yml:18`), SWAT helmet **0.80** (`Entities/Clothing/Head/helmets.yml:82`,
**not annotated**). Damage taken from one 14-Piercing round:

| Loadout | Hit | Today | After PROTO B | Change |
|---|---|---|---|---|
| Heavy vest + SWAT helmet | aimed torso | `0.25 × 0.80` → **2.8** | `0.25 × 0.80` → **2.8** | **none** |
| Heavy vest + SWAT helmet | aimed head | `0.25 × 0.80` → **2.8** | `0.80` → **11.2** | **×4 damage** |
| Heavy vest + BP light helmet | aimed torso | `0.25 × 0.85` → **2.98** | `0.25` → **3.5** | ×1.18 |
| Heavy vest + BP light helmet | aimed head | `0.25 × 0.85` → **2.98** | `0.85` → **11.9** | **×4 damage** |
| Heavy vest, no helmet | aimed head | `0.25` → **3.5** | `1.0` → **14** | **×4 damage** |

**Head is a lethal location** — `CheckVitalDamage` sums Head + Torso + systemic only
(`_Onyx/Mobs/Systems/MobThresholdSystem.cs:31-52`) — so this is a real lethality change, and it is the single
thing to playtest. It is also exactly what "helmets and vests matter per limb" (DECISIONS P3-3) *means*; the
alternative is decision 2's option (b), ship the code with no annotation at all.

**Aimed hand and foot shots bypass an annotated vest entirely.** Hands and feet are selectable doll targets
(`_Shitmed/Targeting/TargetBodyPart.cs`) and `WoundTargetResolver.TryResolveAvailable:52-54` falls Hand→Arm
**only when the hand is missing**, so a hand shot lands on the hand and `coverage: [Torso, Arm, Leg]` does not
cover it. Bounded, but real: limb damage does **not** count toward `CheckVitalDamage`, so "aim at the hands" is
not a lethality exploit — it is a way to bleed, fracture, pain-shock and eventually amputate through a vest.
Gloves and boots with `- type: Armor` are the content answer and are out of scope (P3-D6). Add `Hand`/`Foot` to
the vest coverage only if playtest says otherwise; that is a content edit, not a code change.

### 8.4 P3-6 — organ damage

**The per-hit cap dominates everything.** `MaxHealth × MaxDamageFraction = 15 × 0.3 =` **4.5 organ HP per
damage application → exactly 4 applications to destroy any organ, no matter how hard the hit.** Raw damage at
which the cap binds is `4.5 / multiplier`: **9.9–11.2 Piercing** for every torso organ, 10.3–10.7 for the head
pair. **Wolfgate's base bullet is Piercing 14, above all of them — so every ordinary gunshot that triggers
organ damage does exactly the capped 4.5, and weapon damage above ~11 Piercing is irrelevant to organs.**

Per-hit chance (part-type roll × draw-without-replacement over `selectionWeight` × `hitChance`); torso set
totals weight 4.19, head set has only two organs so **both are always drawn**:

| Organ | P(damaged per hit on that part) | **Expected hits to destroy** |
|---|---|---|
| Lungs | 2.41 % | **≈ 166 torso hits** |
| Liver | 2.05 % | ≈ 195 torso hits |
| Heart | 1.05 % | ≈ 382 torso hits |
| Stomach | 0.98 % | ≈ 408 torso hits |
| Kidneys | 0.95 % | ≈ 421 torso hits |
| Brain | 4.0 % | **≈ 100 head hits** |
| Eyes | 3.5 % | ≈ 114 head hits |

**At Onyx's own numbers organ destruction is a ~100-400-hit event.** A Wolfgate gunfight does not last 166
torso hits. Organ damage as shipped is flavour that accumulates slowly across a shift and — because nothing
heals it — is a **one-way ratchet**.

**When it does land, it is a cliff.** Brain → **instant death**. Heart → `DelayedDeathComponent`, crit then dead
in **60 s**, defib refused, plus internal bleeding at severity 45 = `0.02 × 45 =` **0.9 blood units/s**; with
`BloodMaxVolume 300` and `BloodlossThreshold 0.9`, bloodloss damage starts at **≈ 33 s**. Lungs → suffocation +
0.7 u/s (threshold ≈ 43 s). Liver 0.8 u/s (≈ 37 s). Kidneys 0.6 u/s (≈ 50 s). Stomach 0.5 u/s (≈ 60 s).
Eyes → `TemporaryBlindnessComponent`. **Rare, lethal within a minute, and unrecoverable without a transplant.**

### 8.5 Blockers

| Id | Item | Status |
|---|---|---|
| **B-1** | Decapitation reduces `CheckVitalDamage` by ~100 and can pull a mob out of crit | **RESOLVED by P3-D1**, now a host-gated `_WF` charge rather than HOOK 19. Gated by **T-AMP-VITAL** (both halves) |
| **B-5** | *(new, raised and closed in this revision)* The first P3-D1 — `BaseHead.vitalDamage: 300` — was a **D2 breach**: a shared abstract with 26 descendants, ten of them non-hosts, and `PartRemoveDamage` is not host-gated, so de-heading a monkey, kobold, goblin, Protogen, skeleton, silicon, IPC or chimera would have gone from 100 to 300 Bloodloss | **RESOLVED by the P3-D1 redesign.** No upstream file is touched, and T-AMP-VITAL (b) is the regression guard |
| **B-2** | `AmputationConsequenceWound` has no consumer — Shitmed's `CanAttachPart` has no gate and D7 skips Onyx surgery | **ACCEPTED as a recorded loss (P3-D2).** Hooking it in phase 3 would make reattachment permanently impossible |
| **B-3** | 2 of Onyx's 3 locational-armour tests are red against Onyx's own shipped code | **RESOLVED by P3-D5** (Option B). If the user overrules to Option A, both tests must be **rewritten** to assert the global-fallback behaviour, not skipped |
| **B-4** | `tests.md` F4 requires a new, unauthorised upstream hook on `OrganComponent.cs` | **WITHDRAWN by P3-D9** — the fields shipped in WP6 on `WolfmedOrganComponent`. Verified by reading all four files in full |

**No outstanding blocker.** Every phase-3 sub-area has a decided path.

### 8.6 Genuine user decisions — needed before the named WP starts

| # | Question | Default if no answer | Gates |
|---|---|---|---|
| **1** | **§8.2 — with Onyx defaults, no firearm or laser in the game can ever amputate.** Is that the intended feel for a gun-PvP server, or should the Piercing finishing minimum (40) and Piercing thresholds (200-250) be tuned now? | **Ship Onyx defaults (D4)**, pin it with T-AMP-NOGUN, revisit after playtest | WP11-1 / WP11-5 |
| **2** | **§8.3 — annotating the vests quadruples headshot damage for anyone wearing one** (aimed head with a heavy vest + SWAT helmet: 2.8 → 11.2 from a single 14-Piercing round), because the vest stops protecting the head while unannotated helmets keep protecting the torso. Aimed-torso protection does not change. Aimed hand/foot shots also bypass the vest (non-lethal consequences only). Acceptable? | **Yes — annotate the five `_Mono` vests and the one `_Mono` BP helmet** (P3-D6, 6 lines). Alternatives: **(b)** ship P3-D5's code with **zero** annotation — a verified no-op until a later balance pass; **(c)** annotate the vests only and leave the BP helmet alone | WP11-3 |
| **3** | **P3-D1 is now a host-gated `_WF` charge, not a prototype edit.** A lost vital part charges its own damage as systemic Bloodloss, so decapitation reads *pre-decapitation total + 100* instead of *−100*. The earlier proposal (`BaseHead.vitalDamage: 300`) is withdrawn as a D2 breach. Confirm the redesign, or accept B-1 as a known bug? | **Take the redesign** — zero upstream edits, zero balance deviation, and non-hosts are provably untouched (T-AMP-VITAL b) | WP11-1 |
| **4** | **§8.4 — organ damage is completely irreversible** and a specific torso organ needs ~166-420 hits. Ship as Onyx flavour, or add a Wolfgate-only slow organ regen / a chem / a Shitmed surgery step calling `ChangeHealth(+x)` (~15 lines, a deliberate divergence)? | **Ship as-is (D4)**, record both facts in `WOLFMED_STATUS.md` | WP11-2 |
| **5** | **P3-D18 — P3-4 Option B** (severed limbs render their wounds): 156 texture files, one one-word upstream flag, ~70 extra lines. In WP11-4, or phase 4? | **In WP11-4 if the package is otherwise green** — amputation lands this same phase, so severed limbs will exist; drop it if the package runs long | WP11-4 |
| **6** | **P3-D3 — explosion amputation is out.** It needs an `ExplosionSystem` hook D24 forbids **and** a `_routedModifiers` origin-flag passthrough, without which Mono armour plates stop protecting wound hosts from explosions entirely. Confirm it stays out? | **Stays out.** Ship it with the phase-4/5 balance pass, together with a plate regression test | — |
| **7** | **P3-D7 — organ damage covers human-lineage organs only** (7 edits). The ~20 other roots (animal, arachnid, diona, slime, hydrakin, feroxi, chitinid) stay silent no-ops until phase 4's data-driven option. Accept? | **Accept.** Option B is ~27 edits across 6 fork namespaces or a new prototype kind — not phase-3 risk | WP11-2 |
| **8** | **P3-D25 — hand and foot wounds.** WG (like Onyx) has six damage-overlay layers and no hand/foot art, so a faithful per-layer port makes hand and foot wounds invisible, where today they light every limb. Fold them onto the arm/leg (4 lines, preserves today's look), or ship Onyx-faithful and invisible? | **Fold** (P3-D25). It is the only option that is not a visible regression, and it needs no art | WP11-4 |

### 8.7 Risks that will silently ship something broken if ignored

1. **Forgetting `PenetrateArmor` around `profile.Modifiers`** → every AP weapon stops working against any
   armour with a `partModifiers` entry. T-P3ARM-AP is the gate.
2. **Putting the coverage gate before the `PartModifiers` loop** → `LocationalModifierOverridesAndFallbackTest`
   goes red on `head = 5`.
3. **`coverage: [Chest]` or `[Groin]` surviving into WG YAML** → Release YAML lint failure (no such enum
   member). Every ported list must be D9-mapped, with `Chest` + `Groin` collapsed to a single `Torso`.
4. **Adding `<ArmorComponent, InventoryRelayedEvent<PartDamageModifyEvent>>` to upstream
   `SharedArmorSystem.cs` as Onyx does** → duplicate directed subscription, server crashes at start.
5. **Empty vs unset `coverage` inverted** → every armour in the game flips. `null` and empty both mean
   "protects everything".
6. **Porting `OrganConsequenceComponents.cs` wholesale** → `BreathingImmunity` registers twice, server-start
   crash. §7.3 item 15 exists so nobody re-adds the file.
7. **Redirecting `AmputationConsequenceSeverity` to the severed part instead of the parent** (edit #9) → the
   consequence wound lands at the wrong severity. **Gated only by the WP11-5 fixture's
   `amputationConsequenceSeverity: 50` on the torso** — with the stock 35 everywhere, both readings agree and
   the bug ships silently. Do not drop that fixture line.
8. **Changing `OrganDamageSystem`'s fan-out order** → wounds must be created before amputation can detach, and
   fractures must see the part attached.
9. **WP11-2 touching `OrganDamageSystem.cs:25-26,38-39`** → clobbers WP11-1 in the shared worktree (P3-D24).
10. **Porting Onyx's `OnBodyPartState` subscription without HOOK 21** → compiles, never fires, silently dead
    (P3-D19).
11. **Assuming the Onyx tests pass upstream.** Two of the three armour tests do not. If a WG implementation
    "fails to reproduce Onyx", check §6.2 before changing the implementation.
12. **Weakening `WolfmedBodyPartLifecycleSystem`'s or `OrganHealthSystem`'s `TerminatingOrDeleted` guards** →
    `DebugAssertException` on every mob deletion, poisoning the pooled test pair (13 unrelated failures in WP9).
    P3-D1 adds a damage call to that same handler, so the guards now protect two things.
13. **Editing a prototype on a shared abstract to fix a wound-host problem.** `BaseHead`, `BaseTorso`,
    `BaseMobSpeciesOrganic` and friends are inherited by species that are not wound hosts; D2 makes any such
    edit a breach unless the *consumer* is host-gated. This is what killed HOOK 19 (B-5). Re-grep the
    descendants (`grep -rn "BaseHead" Resources/Prototypes` → 26 hits) before proposing one.
14. **Porting Onyx's `TryGetDetachedDamagePrefix` rotely** → `HumanoidVisualLayers.Groin` does not exist in WG,
    `CS0117` in `Content.Client` (Option B only).
15. **Dropping the P3-D25 hand/foot fold** → hand and foot wounds become invisible; nobody will report it as a
    bug because the sprite simply looks clean.
16. **Putting `ChargeVitalPartLoss` before `_projection.OnPartRemoved`** → it re-enters routing while the
    projection still counts the detached limb (P3-D1, WP11-1 file 3).

### 8.8 Explicitly out of scope for phase 3

**Amputation:** explosion amputation and the `ExplosionSystem`/`_routedModifiers` package (P3-D3); the
`CanAttachPart` consequence gate (P3-D2); Onyx's `SurgicalHealRemovesConsequenceAndUnblocks` and
`HealingDamageKeepsConsequenceBlocked` tests (D7).

**Organs:** `FunctionalOrganComponent`; all seven declarations in `OrganConsequenceComponents.cs`
(`BodyOrgansChangedEvent`, `BodyAnatomyComponent`, `MissingEarsComponent`, `MissingEyesComponent`,
`BreathingImmunityComponent`, `MissingHeadComponent`, `InitiallyLungedComponent`);
`Content.Server/_Onyx/Body/OrganEffectSystem.cs` (all five of its jobs);
`MissingHeartComponent.cs`; `BodyStasis.cs`; `TaggedOrganComponent`/`TaggedOrganSystem` (dead code at the pin);
`ProfileOrgansComponent`; `SharedVisualBodySystem.ProfileOrgans.cs`;
`_Onyx/Body/Systems/BodyInventorySlotSystem.cs`; the health-analyzer organ readout
(`HealthAnalyzerOrganInfo`/`HealthAnalyzerWoundDiagnostic`); the ~20 non-`OrganHuman*` organ roots; Option B's
`WolfmedOrganProfilePrototype`; `OrganCategoryPrototype`; any organ-healing path.

**Armour:** Option C's slot-derived coverage default, its `SlotFlags → BodyPartType` table and its CCVar
(P3-D5); the full 272-entry content pass (P3-D6); `traumaDeductions` (P3-D20); `ShowArmorOnExamine`; the
armour-examine covered-parts line and its locale string; part-aware `_Mono` armour plates; Onyx's
`MaskComponent.IsToggled` gate; `WoundTargetResolver`'s anatomical scatter for unaimed fire.

**Visuals:** `Content.Client/Damage/DamageVisualsComponent.cs`; Onyx's `DisplacementMapSystem` integration;
Onyx's `ProtoId`-ification of `DamageOverlayGroups`/`DamageGroup`; `HumanoidVisualLayers.Groin`; Hand/Foot
`targetLayers` on the live overlay (Onyx does not add them either); any sprite-pixel test.

**Other:** `RepairSelectionAndSnapshotValidationTest` / `_Onyx.Repairable`; the tourniquet test;
`BodyConsequencesTest`'s inventory-slot coupling and `StandUpAttemptEvent` (neither mechanism exists in WG);
`HealingMultiplier` tuning (still on record from phase 2); prediction of wound-host routing (D35).

---

## 9. Work-package table (final)

| WP | Title | Model | Files |
|---|---|---|---|
| **WP11-0** | Zero-dependency test debt — T-REATTACH + T-VISUALS against already-shipped phase-1 code | **sonnet** | 2 |
| **WP11-1** | Amputation — vendored `AmputationSystem.cs` (26-row edit table), `OrganDamageSystem` D26 lift, P3-D1 vital-loss charge | **opus** | 3 |
| **WP11-2** | Organ damage — `_WF/Wolfmed/Body/organs.yml`, PROTO A, `WolfmedOrganConsequenceSystem`, `OrganHealthSystem` guards | **opus** | 4 |
| **WP11-3** | Per-part (locational) armour — `ArmorComponent.Wolfmed.cs`, the `WolfmedPartArmorSystem` rewrite, PROTO B, 5 tests | **opus** | 5 |
| **WP11-4** | Limb damage sprites — HOOK 20 + the `_WF` client partial (Option A); HOOK 21 + PROTO C + 156 RSI files (Option B) | **sonnet** | 2 / 5 |
| **WP11-5** | Amputation + organ tests — 17 tests across 4 files (1 restored, 3 ported, 5 new amputation, 8 new organ) | **opus** | 4 |
| **WP11-6** | Docs + manifest reconcile — **sole owner of `Docs/Wolfmed/`** at this stage | **sonnet** | 3 |

File counts cover new `_WF` files, modified vendored files, upstream hook files, prototypes and tests. They
**exclude** `Docs/Wolfmed/WOLFMED_MANIFEST.md`, which every package appends to (ground rule 6), except in
WP11-6 where the manifest *is* the work. WP11-4's `2 / 5` is Option A alone / Option A + Option B.

**Model rationale.** Opus for WP11-1 (a 26-row edit table on a vendored file, two signature changes, and the
new vital-loss charge whose ordering inside `RemovePart` is load-bearing), WP11-2 (seven prototypes × six
damage multipliers where a typo is a silent balance change, plus the `TerminatingOrDeleted` guards that cost
WP9 thirteen unrelated failures and the raise-don't-subscribe pattern), WP11-3 (the one package where a
plausible-looking handler silently kills every AP weapon, and where two of the ported tests are red against
Onyx's own code) and WP11-5 (every literal is a prediction until measured — P2-D16 — and three of the
assertions only mean anything because of one fixture line). Sonnet for WP11-0 (two tests against shipped
code), WP11-4 (one new partial plus five lines of hook, fully specified in §2.3 — take opus instead if Option
B is in scope, because the rote port has two compile traps and a 156-file copy) and WP11-6 (a reconcile).

---

## Revision notes (CRITIQUE3 pass)

This revision applies `C:/Users/jzo12/Documents/Wolfmed/plan/p3/CRITIQUE3.md`. Every finding was re-derived from the real trees
before being accepted; nothing was taken on CRITIQUE3's word. One of its proposed fixes was rejected in favour
of a better one, and one of its "verified" numbers turned out to be wrong.

### Blocker

**B3-1 — HOOK 19 is a D2 breach. ACCEPTED, and fixed a third way.** Re-verified in full: `BaseHead` is the only
`vital: true` prototype in the repo (`Resources/Prototypes/Body/Parts/base.yml:93`) but it has **26**
descendants, ten of which are not wound hosts; `VitalDamage`'s only consumer,
`SharedBodySystem.PartRemoveDamage` (`Content.Shared/Body/Systems/SharedBodySystem.Parts.cs:395-408`), is not
host-gated, and GUARD B gates only the sever decision (`_Shitmed/…/SharedBodySystem.Targeting.cs:234`). So
`vitalDamage: 300` would have tripled de-heading damage for monkeys, kobolds, goblins, Protogen, skeletons,
silicons, IPCs and chimera.

CRITIQUE3's preferred fix (charge missing critical parts inside `CheckVitalDamage`) was **rejected with
evidence**: that function has no way to know *how much* damage left with the part, so it would need remembered
state anyway, and it would split the mob-state readout from the damage the defibrillator (`:212`) and the
alerts actually see. The plan now charges the loss where it happens, in code that is already host-gated:
`WolfmedBodyPartLifecycleSystem.OnPartRemoved` (`<WoundHostComponent, BodyPartRemovedEvent>`,
`Content.Server`, already carrying the WP9 `TerminatingOrDeleted` guards) applies systemic Bloodloss equal to
the removed vital part's own total damage. Verified: `RemovePart` raises the event at `Parts.cs:356` and calls
`PartRemoveDamage` at `:362`, so both land in the same call; `Bloodloss` is absent from
`WoundHostComponent.LocalizedDamageTypes` (`WoundDamageComponents.cs:35-44`) so it routes systemic and **is**
counted by `CheckVitalDamage`; `GetBodyChildrenOfType` is public (`Parts.cs:991`).

This is strictly better than either alternative on four counts: **zero** upstream files (the hook list drops
from six entries to five), no new subscription, no balance deviation at all — and it closes a hole flat-300 left
open, because a **Blunt** decapitation needs ≥ 350 on the head and a flat 300 would still have read −50.
New test **T-AMP-VITAL** gates both halves, including the non-host regression.

### Majors

* **M3-1 (hand/foot wounds invisible) — ACCEPTED.** Re-verified: `TryGetVisualLayer:253-260` writes
  `LHand/RHand/LFoot/RFoot`, none of which is in the stock six `targetLayers`
  (`Entities/Mobs/Species/base.yml:61-75`), and `ls Resources/Textures/Mobs/Effects/brute_damage.rsi` is exactly
  36 states with no hand or foot prefix. Fixed with CRITIQUE3's cheapest option: **P3-D25**, a four-line fold in
  `UpdatePartDamageVisuals` (§2.3), plus §7.3 deviation 18, a user decision (8), a risk row (15) and a
  server-side anchor in T-VISUALS.
* **M3-2 (nothing can catch the parent-vs-part severity bug) — ACCEPTED.** The WP11-5 fixture torso now carries
  `amputationConsequenceSeverity: 50`, and T-AMP-CONSEQUENCE-1 and -SEPARATE assert **50**. Verified that no
  prototype in either tree overrides the field, so without the fixture line both readings return 35.
* **M3-3 (`HumanoidVisualLayers.Groin` does not exist in WG) — ACCEPTED.** Confirmed at
  `ONYX Content.Client/Damage/DamageVisualsSystem.cs:143` against WG's enum (no `Groin`). Added to §2.3 as an
  Option-B port trap and to §8.7 as risk 14.
* **M3-4 (T-AMP-OVERFLOW (a) unreachable) — ACCEPTED.** Re-derived from `ONYX AmputationSystem.cs:97-105`: any
  Blunt chunk ≥ 50 is a finishing hit, so the arm detaches at ~300 and 500 is unreachable. The setup is now
  20 × 25 Blunt, then one 50, with the reasoning in the test comment.

### Minors

M-1 (missing `using Content.Shared.Body.Organ;`), M-2 (WP11-3's four `[TestPrototypes]` ids, including the
deliberate `WoundFoundationArmorAllHead` rename), M-3 (the double `OrganEnableChangedEvent`, now stated in §2.2
and covered by a tick in T-ORG-FUNC), M-4 (the inflated `TryChangeDamage` return — §7.3 item 19), M-5 (the new
per-tick organ query — a measurement the WP11-2 checkpoint now owes), M-6 (the manifest `:85` row is a
*correction*, not an append: the real sites are `:25-26` and `:38-39`), M-7 (aimed hand/foot shots bypass an
annotated vest — now in §8.3 and decision 2), M-8 (HOOK 20's `ForceUpdate`/`TrackAllDamage` narrowing — now a
required comment at the hook), M-9 (three of P3-1's four fields ship unset — §7.3 item 21), M-10 (`ONYX :78`
needs no edit, with the reason) — **all ten accepted and folded in.** The ordering nit (re-run T-VISUALS after
WP11-4) is now in the WP11-4 checkpoint.

### One CRITIQUE3 claim corrected

CRITIQUE3 §5 reproduces the plan's stacking figure as "heavy vest + SWAT helmet goes from `0.25 × 0.80 = 0.20`
on every part to 0.25 torso / **0.85** head". That mixes two different helmets and, more importantly, assumes
the helmet is annotated. **It is not.** PROTO B annotates one helmet — `ClothingHeadBPHelmetLight`, the only
`- type: Armor` in `_Mono/…/bulletproof_helmets.yml` (`:13`) — and the five `_Mono` vests;
`ClothingHeadHelmetSwat` (`Entities/Clothing/Head/helmets.yml:68-85`, Piercing **0.80**) keeps unset coverage
and therefore keeps protecting the torso exactly as it does today. The real effect of the annotation is
therefore **not** a torso change at all: aimed-torso protection is unchanged for vest + SWAT, while **aimed
head damage quadruples** for anyone wearing a vest (2.8 → 11.2 from one 14-Piercing round), because the vest
stops covering the head. §8.3 is rewritten around the corrected table, P3-D26 records the correction, and user
decision 2 is restated with the head number rather than the torso number.

### What did not change

The 30 hits-to-amputate cells, the amputation thresholds and finishing minimums, the organ arithmetic
(15 × 0.3 = 4.5 → exactly 4 applications), the unaimed-hit weights (total 17), the subscription audit, the
build order and every P3-Dnn decision not named above were re-checked against the tree and stand as written.
