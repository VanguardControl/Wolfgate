# WOLFMED PHASE 2 — implementation plan (lead architect)

**Onyx pin:** `2f5bab9946539cbe083010c9ae6fbc59b47ae377`, sparse reference at `C:/Users/jzo12/Documents/Wolfmed/onyx` (**ONYX**).
**Wolfgate worktree:** `C:/Users/jzo12/Documents/GitHub/Wolfgate/.claude/worktrees/rules-motd-updates-11c89c` (**WG**), branch `clanker/wolfmed-port-orchestration-454c3d`, phase 1 committed (`23c0a74cb9`), RobustToolbox 277 junctioned at `WG/RobustToolbox` — **never touched**.

This document is to phase 2 what `PLAN.md` is to phase 1. It supersedes the five phase-2 analyst reports
(`fractures.md`, `pain-hud.md`, `statuses.md`, `examine.md`, `tests.md`) wherever they disagree; every
overruling is given inline with the evidence that settled it. Agents follow this file literally; the analyst
reports are evidence, not instructions.

**Ground rules (unchanged from PLAN.md, restated because they are binding):**

1. Vendored Onyx code keeps its Onyx path under `_Onyx/`, its Onyx header (where it has one — note that
   `FractureEffectsSystem.cs` and `FractureAlertSystem.cs` have **no** licence header in Onyx; do not invent
   one), and its Onyx namespace. Every in-file change is marked `// WOLFGATE` with a one-line reason.
2. New Wolfgate code lives under `_WF/Wolfmed`. `_WF` style: no licence header, `/// <summary>` one-liners,
   `[Dependency] private X _x = default!;` (no `readonly`).
3. Upstream Wolfgate files get one- or two-line `// WOLFGATE` hooks only, **except the five places §3
   explicitly authorises** (GUARD F, HOOK 15, HOOK 16, HOOK 17, HOOK 18). Nothing outside §3 may be edited in
   an upstream file without escalating.
4. **Never** add a directed subscription for a `(Component, Event)` pair without checking §5 first. RT throws
   `Duplicate Subscriptions for comp=…, event=…` at `WG/RobustToolbox/Robust.Shared/GameObjects/EntityEventBus.Directed.cs:407,419`
   — a server-start crash.
5. Every WP ends with the build checkpoint: `dotnet build Content.Server`, `dotnet build Content.Client` **and**
   `dotnet build Content.IntegrationTests` all green (0 errors), `-c DebugOpt`. YAML lints in **Release** only
   (`ErrorNode` crashes the linter elsewhere).
6. Record every file you touch in `Docs/Wolfmed/WOLFMED_MANIFEST.md` (§7) in the same work package.
7. **No commits.** Work packages leave the tree uncommitted; snapshot a patch per WP under
   `C:/Users/jzo12/Documents/Wolfmed/plan/snapshots/`. The user commits.
8. Before blaming Wolfmed for a test failure, run `DockTest` first (the `db.ef` sqlite warnings fail every
   pair test in this repo — project memory).

---

## 1. Decisions

### 1.1 Phase-1 decisions that bind phase 2 (restated, unchanged)

| ID | Decision as it applies to phase 2 | Evidence |
|---|---|---|
| **D1** | `StatusEffectNew` is vendored and live. Phase 2 adds **no** new StatusEffectNew consumer — see P2-D1. | WP1 |
| **D2** | Entities without `WoundHostComponent` behave exactly as today. Every phase-2 upstream hook must be `HasComp<WoundHostComponent>`-scoped or behaviourally inert for non-hosts. | PLAN §1.1 |
| **D4** | Balance = Onyx defaults. Two phase-2 numbers contradict this in spirit (P2-D13, P2-D16); both are recorded as deviations and escalated, not silently "fixed". | PLAN §1.1 |
| **D5** | Missing APIs get a compat shim in `Content.Shared/_WF/Wolfmed/Compat`; where impossible, a `// WOLFGATE` edit in the vendored file. Phase 2 adds exactly **one** new shim (§2.1). | PLAN §1.1 |
| **D6** | Layout: vendored Onyx code at its Onyx relative path under `_Onyx/`; Wolfgate glue under `_WF/Wolfmed`; docs in `Docs/Wolfmed/`. | PLAN §1.1 |
| **D8** | Wolfgate stays on Shitmed's `BodyPartComponent`. Onyx's extra part fields live on `WolfmedBodyPartComponent`, read through `WolfmedBodyPartSystem.Get(EntityUid)`. **This is the one edit `FractureAlertSystem.cs` needs.** Precedent already in the tree: `WG/Content.Shared/_Onyx/Wounds/WoundFractureSystem.cs:151` `var profileId = _wfPart.Get(part).FractureProfile; // WOLFGATE: D8, FractureProfile lives on WolfmedBodyPartComponent.` | verified directly |
| **D9** | No `BodyPartType.Chest`/`.Groin`. `WG/Content.Shared/Body/Part/BodyPartType.cs` is `{Other, Torso, Head, Arm, Hand, Leg, Foot, Tail}`. Phase 2's only affected file is `HealthExaminableSystem.PartStatus.cs`'s `PartOrder` switch (P2-D11 / WP10-2). Neither fracture file references those members (verified by reading both Onyx files in full). | verified directly |
| **D10** | Onyx's Targeting stack is not vendored, **including `PartStatus*`**. That is exactly what creates the one new shim phase 2 needs (§2.1). | PLAN §1.2 |
| **D12** | Vendored files bind `WolfmedDamageableSystem`, never `DamageableSystem`. Relevant to `EmoteOnDamageSystem.PainSounds.cs` (§2/WP10-5) and — per PLAN §2.1's own "Serves" list — `HealthExaminableSystem.PartStatus.cs:44`. | PLAN §2.1 |
| **D13** | Server-only wound systems live in `Content.Server/_Onyx/Wounds/` with Onyx namespaces unchanged. Phase 2 applies the same pattern to `Content.Server/_Onyx/Chat/EmoteOnDamageSystem.PainSounds.cs`: **file path is `_Onyx`, namespace stays `Content.Server.Chat.Systems`** (it is a partial of the upstream class). | PLAN §1.2 |
| **D18** | Guards test `HasComp<WoundHostComponent>` directly, with a `// WOLFGATE` `using Content.Shared._Onyx.Wounds;`. No marker component. | PLAN §1.2 |
| **D25 / HOOK 6** | `SharedChatSystem.TryEmoteWithChat` shim + the one-word `override` at `WG/Content.Server/Chat/Systems/ChatSystem.Emote.cs:60` are **already shipped** and are what makes `PainSystem.cs:297`'s pain-shock `Scream` work. Do not re-do. | verified directly |
| **D26** | `AmputationSystem` stays skipped (phase 3). `FractureEffectsSystem` + `FractureAlertSystem` are phase 2 — this plan. The two `// WOLFGATE`-disabled `_amputation` lines in `OrganDamageSystem.cs:24,36` **stay disabled**. | PLAN §1.2 |
| **D28** | `WolfmedBodyPartLifecycleSystem` (server) subscribes `<WoundHostComponent, BodyPartAddedEvent/RemovedEvent>` and re-raises `OrganGot*` over the subtree. It is the raiser phase 2's `FractureEffectSystem` subscribes. **Do not weaken its `TerminatingOrDeleted` guards** (`:29`/`:50` today) — that is the `DebugAssertException` WP9 fixed. | PLAN §1.3, WP9 |
| **D29** | `PassiveDamage` stays neutralised on wound hosts (`damage: {}`). Phase 2 adds the test that locks it (T-PASSIVE). | PLAN §1.3 |
| **D32** | `WoundHost` is on `BaseMobSpeciesOrganic`; Protogen is excluded. Every phase-2 mob-wiring line goes in the **same existing `# WOLFGATE` block** so it tracks that exclusion. | DECISIONS.md |
| **D35** | Wound-host damage routing is unpredicted. The pain HUD inherits one round-trip of "nothing happened" per hit and does not make it worse. | DECISIONS.md |
| **P2-3** | `wounds.body_part_functionality_enabled` stays `false` (`WG/Content.Shared/_Onyx/CCVar/CCVars.Wounds.cs:7-8`, verified). Fracture movement/manipulation multipliers apply regardless — the fracture branch of `GetEffect` is not cvar-gated. See P2-D14 for the one path that is still live behind it. | DECISIONS.md |
| **P2-4** | Pain is live for the first time (WP9's `ModifyPainGainEvent(1f)` fix). **Every number the pain HUD or a fracture test shows must be measured on a real mob, not copied from Onyx.** | DECISIONS.md, WP9 §3.2 |

### 1.2 New phase-2 decisions

| ID | Decision | One-line rationale | Evidence |
|---|---|---|---|
| **P2-D1** | **Skip the entire status-effect movement chain.** Do not port `Content.Shared/Movement/Components/MovementModStatusEffectComponent.cs`, `Content.Shared/Movement/Systems/MovementModStatusSystem.cs`, `FrictionStatusEffectComponent`, `Resources/Prototypes/Entities/StatusEffects/movement.yml`'s `StatusEffectSlowdown` chain, `Resources/Prototypes/_Onyx/StatusEffects/wounds.yml`, `MobStandStatusEffectBase`, or a `KnockdownImmune` tag. Reclassify the three "deferred → WP10" manifest rows (`:34`, `:35`) as **not needed**. | Nothing in phase-2 scope reaches them. `FractureEffectSystem` applies both penalties **directly**: `OnRefreshSpeed` calls `args.ModifySpeed(1f - (1f - modifier) * partScale * treatmentScale)` and `OnGetMultiplier` does `args.Multiplier *= 1f + (modifier - 1f) * …` — no status-effect entity is created, applied or queried anywhere in the file. The only generic wound→status-effect bridge, `WoundStatusEffectBehavior.StatusEffect`, is populated by **zero** wounds (`grep -ni "statuseffect" Resources/Prototypes/_Onyx/Wounds/wounds.yml` → 0 hits, both trees). `StatusEffectBurnSlowdown`/`StatusEffectWoundImpairment` are orphaned repo-wide at the pin. `MobStandStatusEffectBase` has zero concrete descendants. Porting `MovementModStatusSystem` would additionally drag in the undocumented `FrictionStatusEffectComponent`. | ONYX `FractureEffectsSystem.cs` read in full (verified); `statuses.md` §1-§3; `pain-hud.md` §6.3. WP1 independently reached the same conclusion and left the standing comment at `WG/Resources/Prototypes/Entities/StatusEffects/misc.yml:31-32`. |
| **P2-D2** | **`FractureEffectsSystem.TryGetUsedHandSymmetry` is replaced wholesale** with the exact body in §4/WP10-1, using WG's `IsHolding(EntityUid, EntityUid?, out Hand?, HandsComponent?)` overload. An extension-method shim is impossible. | Verified in WG today: `SharedHandsSystem.cs:177` `public Hand? GetActiveHand(Entity<HandsComponent?> entity)`; `:285` `public bool IsHolding(EntityUid uid, [NotNullWhen(true)] EntityUid? entity, [NotNullWhen(true)] out Hand? inHand, HandsComponent? handsComp = null)`; `:306` `public bool TryGetHand(EntityUid handsUid, string handId, [NotNullWhen(true)] out Hand? hand, …)`; `HandsComponent.cs:107` `public sealed class Hand`; `HandsComponent.cs:156-161` `enum HandLocation : byte { Left, Middle, Right }`. Extension methods cannot win: WG's instance `GetActiveHand(Entity<HandsComponent?>)` is applicable to Onyx's exact argument list, and C# prefers an applicable instance member. | **Overrules `tests.md` §4.3**, which asserts a `string`-out `IsHolding` exists "at `SharedHandsSystem.cs:438`" — that is Onyx's line number; WG has no such overload (grep of all `public .*IsHolding` in WG returns exactly `:280` and `:285`). **Overrules `statuses.md` §6**, which concludes "neither WG overload matches the call shape" and that the `used` branch needs a hand-enumeration rewrite — the 4-arg `:285` overload matches exactly. `fractures.md` §2.3 is correct and its code is adopted verbatim. |
| **P2-D3** | **`FractureEffectsSystem.cs:1`'s `using Content.Shared.Body;` is NOT swapped.** No `// WOLFGATE` edit for the `OrganGot*` events. | `WG/Content.Shared/_WF/Wolfmed/Compat/OnyxBodyEvents.cs` declares `namespace Content.Shared.Body;` and both events as `[ByRefEvent] public readonly record struct …(EntityUid Target);` — byte-shape-identical to ONYX `Content.Shared/Body/BodyComponent.cs:37-46`, including by-ref-ness and the member name `Target`. | File read in full (verified). **PLAN.md §2's "FractureEffectsSystem.cs:34-35 gets a `// WOLFGATE` using swap" note is stale** and must be corrected when `WOLFMED_PLAN2.md` lands. |
| **P2-D4** | **The do-after multiplier is bridged from a new `_WF` system, not by editing any do-after file.** `WolfmedFractureDoAfterSystem` subscribes `<WoundHostComponent, GetDoAfterDelayMultiplierEvent>` and calls `FractureEffectSystem.GetDurationMultiplier(uid)` with `used: null`. `SharedDoAfterSystem.cs` and `DoAfterDelayMultiplierSystem.cs` are **not touched**. Two accepted losses, both recorded as deviations: (a) the `used` item is unavailable so the **active hand** decides symmetry; (b) `args.MultiplyDelay == false` do-afters escape the penalty. | PLAN §3's "Explicitly NOT touched" row forbids `SharedDoAfterSystem.cs`. Verified: `WG/Content.Shared/_Goobstation/DoAfter/DoAfterDelayMultiplierSystem.cs:33-38` `public sealed class GetDoAfterDelayMultiplierEvent(float multiplier = 1f) : EntityEventArgs, IBodyPartRelayEvent { public float Multiplier = multiplier; public BodyPartType TargetBodyPart => BodyPartType.Hand; }` — **no `Used` member**; raised at `WG/Content.Shared/DoAfter/SharedDoAfterSystem.cs:208-214` only inside `if (args.MultiplyDelay)`. Onyx's own raiser is `ONYX Content.Shared/DoAfter/SharedDoAfterSystem.cs:262`. | Verified directly. The 2-line `Used`-preserving alternative is escalated as a **user decision** (§8.2 item 2), not taken unilaterally. **Corrects `statuses.md` §8 note** that Onyx never wires the event to a real do-after — Onyx does, at `SharedDoAfterSystem.cs:262-264`. |
| **P2-D5** | **There is no pain or shock alert to assert. P2-2's "pain-alert assertion" is struck** and replaced by (a) an overlay-level assertion and (b) the pain-shock stun test. Do **not** invent a Wolfgate-only `Pain` alert. | `PainSystem.cs` contains zero `AlertsSystem`/`ShowAlert`/`ClearAlert` references in either tree; `PainComponent`/`PainShockTargetComponent` carry no alert field; ONYX `Resources/Prototypes/_Onyx/Alerts/alerts.yml` declares only `BrokenBones` (plus the unrelated `Centered`), and `categories.yml` declares only `TileMovement`/`Counter`/`Ninjutsu`. DECISIONS.md's own hedge ("once pain-hud defines the alert") anticipates this; the condition is unmet. | `pain-hud.md` §2, `tests.md` §0.1, `statuses.md` §5 — all three agree independently. |
| **P2-D6** | **The pain HUD is a ~10-line read inside Wolfgate's existing client `DamageOverlay.Draw()`**, not a port of Onyx's shared damage-overlay refactor. | Onyx's `Content.Shared/DamageOverlay/**` + `Content.Client/DamageOverlay/**` use the `[SubscribeLocalEvent]` attribute **8 times**; that attribute does not exist in RT 277 (`grep -rn "class SubscribeLocalEventAttribute" WG/RobustToolbox` → **zero hits**, verified). Wolfgate's `DamageOverlayUiController` cannot host a fix either: `UiController` exposes only broadcast subscriptions (`RobustToolbox/Robust.Client/UserInterface/Controllers/UiController.Subscriptions.cs`), so `<PainComponent, AfterAutoHandleStateEvent>` is unavailable to it, and its four refresh triggers never fire on pain decay (0.11/s with no damage event). A `Draw()` read is structurally what Onyx's own `DamageOverlay.Draw` does. The full-refactor alternative costs: 8 attribute rewrites, a new upstream `Content.Shared.DamageOverlay` subsystem, **deleting** `Content.Client/UserInterface/Systems/DamageOverlays/**`, a `- type: DamageOverlay` on `MobDamageable` (every damageable mob), a new `GetDamagePerGroup` on the D12 facade, and losing Mono's `PainNumbnessComponent` branch. | `pain-hud.md` §3.1-§3.5, RT absence re-verified here. |
| **P2-D7** | **`- type: PainShockTarget` lands on `BaseMobSpeciesOrganic`**, inside the existing `# WOLFGATE — Wolfmed phase 1 (D21/D32)` block, so it tracks `WoundHost` and the Protogen exclusion. **This is new scope not named in DECISIONS.md P2-1** and is required, not optional. | `grep -rn "PainShockTarget" WG/Resources/` → **nothing** (verified). `PainSystem.Update` iterates only `EntityQueryEnumerator<PainComponent, MobStateComponent, PainShockTargetComponent>` (`:143`) and `RaisePainChanged` only calls `UpdatePainShock` when the component is present (`:441-443`). So pain shock — the 2 s paralyse, the forced `Scream`, the jitter and the 30 s ×0.7 adrenaline window — has **never run on a real mob**. Onyx puts it on `BaseSpeciesMob` (ONYX `Resources/Prototypes/Body/species_base.yml:54`). Every dependency verified present: `Scream` (`Resources/Prototypes/Voice/speech_emotes.yml:3`), `StunSystemOnyxCompat.TryUpdateParalyzeDuration`, `SharedJitteringSystem.DoJitter` with `Jitter`'s `alwaysAllowed: true`, and the **old** `StatusEffectsComponent` with `Stun`/`KnockedDown` on `BaseMobSpecies` (`base.yml:125-140`), inherited by `BaseMobSpeciesOrganic` (`:247`). | `pain-hud.md` §4.1, `tests.md` §4.5 — found independently by both. |
| **P2-D8** | **`PainSystem.IsPainNumb` is widened by one `// WOLFGATE` clause to honour the legacy `PainNumbnessComponent`.** | As shipped, `IsPainNumb` (`WG/Content.Shared/_Onyx/Wounds/PainSystem.cs:324-331`) tests only `PainNumbnessStatusEffectComponent`. Verified: WG has the component (`Content.Shared/Traits/Assorted/PainNumbnessStatusEffectComponent.cs:12`, WP1 file #17) but **none** of `PainNumbnessStatusEffectBase`, `StatusEffectPainNumbness`, `TraitStatusEffectPainNumbness`, `TraitStatusEffectBase` or `CloneableStatusEffect`; and WG's `TraitPrototype` has no `specials:` at all, so nothing can apply a status effect from a trait. WG's shipped `PainNumbness` trait grants the Mono/legacy `PainNumbnessComponent` (`Content.Shared/Traits/Assorted/PainNumbnessSystem.cs:14-17`). **Result today: `IsPainNumb` is permanently false**, a pain-numb character still gets the pain vignette, still screams from pain shock and still takes the full pain stun — and `HighPainThreshold`'s `mutuallyExclusiveTraits: [PainNumbness]` guards nothing. | Verified directly. **Overrules `statuses.md` §7** ("a correct, harmless, already-complete no-op; no action needed") — it is correct that the code compiles, but it leaves a player-visible hole the trait promises to fill. `pain-hud.md` §6.1 is adopted. |
| **P2-D9** | **Onyx's `EmoteOnDamage` pain sounds ARE a real, in-scope phase-2 port** (3 Onyx files), done **additively** against Wolfgate's existing component, **with Onyx's YAML key bug corrected** (`emotes:` → `emotesThreshold:`) and recorded as a corrected-upstream-bug deviation. | Verified: ONYX ships `Content.Server/_Onyx/Chat/EmoteOnDamageSystem.PainSounds.cs`, and `Content.Server/Chat/EmoteOnDamageComponent.cs` carries `[DataField] public Dictionary<float, HashSet<ProtoId<EmotePrototype>>> EmotesThreshold = new(); // <Onyx-PainSounds-edited>` plus `AllowedDamageType`, `PainThreshold = 6f`, `LastTotalDamage` inside `// <Onyx-PainSounds>` tags. **Onyx's own `species_base.yml:125` writes `emotes:` for a field whose YAML key is `emotesThreshold`** — RT drops unknown mapping keys at read and raises `FieldNotFoundErrorNode` at validation, so the feature is **dead at the Onyx pin**. Wolfgate's component is a different shape (`[DataField("emotes", customTypeSerializer: PrototypeIdHashSetSerializer<EmotePrototype>)] public HashSet<string> Emotes`), and replacing it would silently change `ZombieSystem`'s two `AddEmote(uid, "Scream")` call sites. | **Overrules `examine.md` §9**, which concludes the item is "already fully shipped" and should be struck. `examine.md` verified the *pain-shock `Scream`* (`PainSystem.cs:297`, genuinely shipped in WP4/WP9) and conflated it with Onyx's separate `EmoteOnDamage` feature; it also asserts WG's `EmoteOnDamageSystem` is unrelated to wound pain, which is true of WG's copy but not of Onyx's, which Onyx explicitly extended. `pain-hud.md` §4.2 is adopted in full. **Because the feature never ran at the pin, shipping it is new behaviour, not a faithful port — escalated as a user decision (§8.2 item 3), with "port it, key corrected" as the recommendation** (same class as, and precedent set by, WP9's accepted `ModifyPainGainEvent` fix). |
| **P2-D10** | **Five new upstream hooks are authorised**: GUARD F, HOOK 14, HOOK 15, HOOK 16, HOOK 17, HOOK 18 (six sites-groups; GUARD E2 already existed). Numbering is assigned here to resolve the collision between the reports — `examine.md` and `pain-hud.md` both proposed "HOOK 14" for different files. | §3 gives each one's file, line, exact code and WP. | This plan. |
| **P2-D11** | **One new compat shim: `Content.Shared/_WF/Wolfmed/Compat/PartStatusSeverity.cs`**, declaring `PartDamageSeverity` + a static `PartStatusSystem.GetSeverity` in `namespace Content.Shared._Onyx.Targeting`, so `HealthExaminableSystem.PartStatus.cs`'s `using` and unqualified call resolve with **zero edits to the vendored file**. | D10 excludes `_Onyx/Targeting/PartStatus*`. Verified: `grep -rn "PartDamageSeverity\|class PartStatusSystem"` across `WG/Content.{Shared,Server,Client}` → **zero hits**; `WG/Content.Shared/_Onyx/Targeting/` holds only `DamageDistribution.cs`, `TargetingSnapshotComponent.cs`, `TargetingSnapshotSystem.cs`. Same pattern as PLAN §2.4-§2.10 (a `_WF` file deliberately declaring an upstream/Onyx namespace). | `examine.md` §5, re-verified here. **This gap was missed by every phase-1 report** — phase 1 never touched HealthExaminable. |
| **P2-D12** | **The 8-line WP7 stopgap block in `wounds.ftl` is deleted in the same work package that adds `health-examinable.ftl`.** `Resources/Locale/en-US/_Onyx/targeting/part-status.ftl` is **skipped** (orphaned — its one key has zero consumers at the pin). | Verified: `WG/Resources/Locale/en-US/_Onyx/prototypes/wounds/wounds.ftl:46-52` carries the `# WOLFGATE (WP7)` comment and the four `wound-examine-fracture-*` keys, which `health-examinable.ftl` also defines. Fluent throws on duplicate ids. `WOLFMED_MANIFEST.md:356-360` names this exact scenario and asks for the check. | `examine.md` §7, re-verified here. |
| **P2-D13** | **`OrganicFractureProfile.manipulationModifier` ships unchanged at Onyx's YAML values (D4), recorded as a balance deviation, and escalated to the user.** Do not "fix" it in this phase. | Verified: `WG/Resources/Prototypes/_Onyx/Wounds/wounds.yml` grades declare `manipulationModifier: 0.92 / 0.84 / 0.75 / 0.75` — all **below 1** — while the formula is `args.Multiplier *= 1f + (modifier - 1f) * partScale * treatmentScale`, so *below 1 means faster*. The C# defaults run the other way (`WoundPrototype.cs`: `Hairline = new(8, 0.9f, 1.1f)`, `Simple = new(15, 0.8f, 1.25f)`, `Displaced = new(25, 0.6f, 1.5f)`, `Comminuted = new(40, 0.4f, 2f)`), as does the non-fracture fallback branch (`Disabled => 2.5f`, `Impaired => 1.25f`). **With the shipped data a shattered arm makes every do-after 25 % faster.** This also explains Onyx's stale `2f` test literal — that test was written against the C# defaults. `movementModifier` is unaffected (correctly below 1, and its formula reads it that way). | Verified directly. `fractures.md` §8.1. |
| **P2-D14** | **P2-3 restated with one caveat:** the cvar being `false` does **not** make `GetEffect`'s fallback branch inert. `BodyPartFunctionalitySystem.GetState` returns `Disabled` for a part with `CyberneticsComponent.Disabled` *above* the cvar gate (`BodyPartFunctionalitySystem.cs:19-23`), so a disabled cybernetic limb still yields mobility `0f` / manipulation `2.5f` through a path reachable in Wolfgate today. Assert it or at minimum note it. | Read directly. | `fractures.md` §2.7. |
| **P2-D15** | **`HighPainThreshold` ports with two prototype adaptations:** `conflicts:` → `mutuallyExclusiveTraits:` (declared on the `_Onyx` entry only — WG checks both directions client-side at `Content.Client/Lobby/UI/HumanoidProfileEditor.xaml.cs:998`, so `Resources/Prototypes/Traits/disabilities.yml` needs **no** edit), and `cost: 3` is **dropped** (Wolfgate's `Quirks` category declares no `maxTraitPoints`, so `WithTraitPreference` short-circuits and the cost is inert; WG's own `quirks.yml` entries declare no cost). `specials:` has no Wolfgate equivalent and is not ported. Both trees use `components:`; **neither uses `functions:`**. | `pain-hud.md` §5.2. | |
| **P2-D16** | **Every numeric literal in a ported phase-2 test is a prediction until measured.** P2-4 is mandatory. Derived predictions for `EffectsRefreshOnTreatmentHealingAndDetachTest` against the *shipped* data (Onyx's literals `0.4f` and `2f` are both stale): leg 75 → Comminuted (threshold 60), `movementModifier: 0`, `PartEffectScales[Leg] = 0.5`, `TreatmentEffectScales[None] = 1` → `ModifySpeed(1 - (1-0)*0.5*1)` = **walk 0.5**; arm 75 → Comminuted, `manipulationModifier: 0.75`, `Arm` absent from `PartEffectScales` so scale `1f` → `1 + (0.75-1)*1*1` = **multiplier 0.75**. Put the derivation in a `// WOLFGATE` comment at each assertion, exactly as WP9 did. | Same defect class WP9 documented in its §3.2 (`GradeBoundariesAreDeterministicTest` asserted 15/30/50/75 against a profile declaring 20/35/50/60). | `fractures.md` §7; grades and scales re-verified here. |
| **P2-D17** | **No client-side `OrganGot*` mirror in phase 2.** `WolfmedBodyPartLifecycleSystem` is server-only, so the client never refreshes movement speed on limb attach/detach. Mitigated: `MovementSpeedModifierComponent.WalkSpeedModifier`/`.SprintSpeedModifier` are `[AutoNetworkedField]`, so the server value corrects the client within one state. A 20-line client-only `_WF` mirror is a legitimate follow-up (directed subscriptions are per-`IEntityManager`, so it is **not** a duplicate registration) — flagged, not done. | `fractures.md` §2.4 item 2, §8.2. | |
| **P2-D18** | **`FractureEffectSystem.RefreshTransferredPart` is kept verbatim as intentionally uncalled** (its only Onyx caller is the D7/D16-deferred `SpeciesChangeEntityEffectSystem`). Record it in the manifest as dead-but-deliberate rather than deleting it and creating a re-sync diff. | `fractures.md` §2.6. | |
| **P2-D19** | **`FractureEffectSystem` and `FractureAlertSystem` land together, in that dependency order, in one work package.** `FractureAlertSystem.Refresh` has no caller other than `FractureEffectSystem`'s six handlers, and `FractureEffectSystem` holds `[Dependency] private FractureAlertSystem _fractureAlerts`. Splitting them ships either a dead system or a `CS0246`. | ONYX both files read in full (verified). | `tests.md` §7 item 5. |
| **P2-D20** | **GUARD F calls `AddPartStatusMarkup` from the `else` of the wound-host wrap, not unconditionally as Onyx does.** Recorded as a deliberate divergence from Onyx. | Onyx puts the call **outside** the `if (!HasComp<WoundHostComponent>(uid))` block (ONYX `Content.Shared/HealthExaminable/HealthExaminableSystem.cs`: the `</Onyx-PartHealthExamine-edited>` closing tag is at `:105`, the `AddPartStatusMarkup(uid, examiner, msg);` call is a *separate* tagged block at `:107-109`). Safe in Onyx because everything with a body there is a wound host. In Wolfgate it is a **D2 breach**: `HealthExaminableComponent` sits on `BaseMob` (`WG/Resources/Prototypes/Entities/Mobs/base.yml:109`), not on `BaseMobSpeciesOrganic`, and `AddPartStatusMarkup` only early-returns when `_body.GetBodyChildren` is empty — so borg chassis, NPC silicons, EE silicons, animals (`Entities/Mobs/NPCs/animals.yml:603,966,1051` all carry `- type: Body`) and above all **Protogen**, which D32 deliberately strips `WoundHost` from, would get the Onyx part-status readout *in addition to* today's threshold text. It does not crash — `WoundSystem.GetWounds` is `Resolve(..., false)`-guarded (`WoundSystem.cs:155-159`) — so build and lint stay green and nobody notices until someone examines a borg. | Both files read in full (verified). **CRITIQUE2 B2 accepted in full.** Whether silicons *should* get part status is a scope decision for the user (§8.2 item 8), not a side effect of a hook. |
| **P2-D21** | **`WoundFractureBodyGraph` gains a `left hand` slot (`LeftHandHuman`) and `WoundFractureBody` gains `- type: Hands`. This is a hard prerequisite of T-FRACT-EFFECTS, not an optional extra for T-FRACT-HANDS.** | `FractureEffectSystem.OnGetMultiplier` (`:109-112`) returns immediately unless `TryGetUsedHandSymmetry` succeeds, whose first guard is `if (!TryComp(body, out HandsComponent? hands)) return false;` — kept verbatim by the P2-D2 rewrite. The shipped fixture (`WG/Content.IntegrationTests/Tests/_Onyx/Wounds/WoundFractureTest.cs:22-47`) has **no** `Hands` and no hand slot, and `InventoryBase` supplies only `Inventory` + `InventorySlots`. A hand also only exists once an **enabled** `BodyPartType.Hand` part with an enabled parent is attached — `WG/Content.Server/Hands/Systems/HandsSystem.cs:116-136` (`TryAddHand`: `part.Comp.PartType != BodyPartType.Hand → return`; `BodyPartSymmetry.Left => HandLocation.Left`; `AddHand(uid, slot, location)`), driven from `HandleBodyPartAdded` at `:138-141`; `AddHand` sets `ActiveHand` when none is set (`SharedHandsSystem.cs:59-60`). Without the fixture change `GetDurationMultiplier(body)` is **`1f`** ("no hand found"), which masquerades as "fractures do not affect manipulation", and T-FRACT-HANDS's `Comp<HandsComponent>(body)` guard **throws**. Onyx's own fixture has no hands either (ONYX `WoundFractureTest.cs:22-67`) — a second, independent reason its `2f` literal could never have passed. `LeftHandHuman`/`RightArmHuman`/`RightHandHuman` all exist (`Resources/Prototypes/Body/Parts/human.yml:72,63,83`). | Verified directly. **CRITIQUE2 B3 accepted in full.** |
| **P2-D22** | **Onyx's pain sounds are gated by a one-line `// WOLFGATE` `HasComp<WoundHostComponent>` guard at the top of `HandlePainDamageEmote`**, and P2-D7's stated rationale is corrected: the phase-1 `# WOLFGATE` block does **not** track the D32 Protogen exclusion. | D32 is a C# opt-out that removes exactly one component: `WG/Content.Shared/_WF/Wolfmed/Body/WolfmedWoundHostExclusionSystem.cs:12` `ExcludedAncestors = { "BaseMobProtogen" }`, `:17` `SubscribeLocalEvent<WoundHostComponent, ComponentInit>`, `:33` `RemComp<WoundHostComponent>(ent)`. Anything else added to `BaseMobSpeciesOrganic` therefore stays on Protogen. `PainShockTarget` there is **inert** (the only `EnsureComp<PainComponent>` sites are `WoundDamageProjectionSystem.cs:212,224,237`, all behind the wound-host path; `PainSystem.Update`'s shock query at `:143` needs all three components) — harmless, but the stated reason was still false and would have entered the manifest as a wrong claim. `EmoteOnDamage` there is **live**: `HandlePainDamageEmote` (ONYX `Content.Server/_Onyx/Chat/EmoteOnDamageSystem.PainSounds.cs:19-62`) checks threshold count, cooldown, `_random.Prob`, mob state and pain numbness — **never** `HasComp<WoundHostComponent>` — and reads `_damageable.GetTotalDamage(uid)`, which works on any damageable. D2 breach. The guard costs nothing: `using Content.Shared._Onyx.Wounds;` is already line 1 of that file. | Verified directly. **CRITIQUE2 M1 accepted in full**; fix (b) is taken over widening the exclusion system, because a `HasComp<WoundHostComponent>` gate is also the honest gate for a feature shipping as "Wolfmed pain sounds". |
| **P2-D23** | **Fracture creation is a dice roll. Every phase-2 fracture test reaches its grade through a `creationChance: 1` hit and then re-grades by changing severity; no test may create a fracture with a sub-Comminuted hit.** | `WG/Content.Shared/_Onyx/Wounds/WoundFractureSystem.cs:51-55`: `var hitGrade = GetGrade(profile, effectiveTrauma); if (hitGrade == FractureGrade.None || !profile.Grades.TryGetValue(hitGrade, out var gradeSettings) || !_random.Prob(Math.Clamp(gradeSettings.CreationChance, 0f, 1f))) return;`. The shipped profile declares `Hairline creationChance: 0.05`, `Simple: 0.25`, `Displaced: 0.65`, `Comminuted: 1` (`Resources/Prototypes/_Onyx/Wounds/wounds.yml:56-76`), so a 35-damage hit creates a fracture **25 % of the time** — three runs in four fail. `OnWoundChanged` (`:73-86`, re-verified) re-grades with **no** random roll, and `severityMultiplier: 1` (`wounds.yml:39`) makes severity == damage, so `WoundSystem.ChangeSeverity(wound, delta)` (`WoundSystem.cs:284`) is a deterministic grade dial. This is exactly why phase 1's `PostArmorHitAndTreatmentPreconditionsTest` hits for 150 through a ×0.5 armour to land on 75. | Verified directly. **CRITIQUE2 M2 accepted in full.** A "prediction until measured" note (P2-D16) is no defence against non-determinism. |
| **P2-D24** | **Every bespoke pain fixture declares `- type: StatusEffects` with an explicit `allowed: [Stun, KnockedDown, Jitter]` *and* `- type: MobState`.** | `Stun` and `KnockedDown` are **not** `alwaysAllowed` (`WG/Resources/Prototypes/status_effects.yml:4-10`; only `Jitter:17` and `PressureImmunity:39` are, plus three downstream files). `BaseMobSpecies` gets them by listing them explicitly (`Entities/Mobs/Species/base.yml:125-127`). `StunSystemOnyxCompat.TryUpdateParalyzeDuration` → `SharedStunSystem.TryParalyze` (`SharedStunSystem.cs:244-251`) `Resolve`s `StatusEffectsComponent` and then goes through `TryKnockdown` + `TryStun`, both of which check the allowed list — so on a fixture with a default (empty) `allowed` the stun silently no-ops and the test fails for a reason unrelated to pain. `MobState` is equally load-bearing and was missed by every report *and* by CRITIQUE2: `PainSystem.Update`'s shock loop is `EntityQueryEnumerator<PainComponent, MobStateComponent, PainShockTargetComponent>` (`PainSystem.cs:143`), so a fixture without `MobStateComponent` is never visited at all. `Jitter` is belt-and-braces (`_jitter.DoJitter` at `PainSystem.cs:297` needs the component to exist). | Verified directly. **CRITIQUE2 M3 accepted and extended** with the `MobState` half. |

---

## 2. New compat / `_WF` pieces

Phase 2 adds exactly **two** new `_WF` files. Everything else it needs already exists from phase 1 — verified
present and usable: `WolfmedDamageableSystem`, `DamageDealtEvent`, `AlertsSystem.UpdateAlert` (not used by
phase 2), `StunSystemOnyxCompat`, `WolfmedBodySystem.TryDetachPart`, `OnyxBodyEvents` +
`WolfmedBodyPartLifecycleSystem`, `WoundTargetResolver`, `WolfmedBodyPartComponent` +
`WolfmedBodyPartSystem`, `SharedChatSystem.Wolfmed.cs` + HOOK 6, and — **added after CRITIQUE2 M5** —
**`WoundStatusEffectSystem`**.

`FractureEffectSystem` holds `[Dependency] private WoundStatusEffectSystem _statusEffects = default!;`
(ONYX `FractureEffectsSystem.cs:23`) and calls it in two handlers: `:64` `_statusEffects.HandlePartInserted(part.Owner);`
and `:71` `_statusEffects.HandlePartRemoved(part.Owner, args.Target);`. Both symbols exist in WG today —
`WG/Content.Shared/_Onyx/Wounds/WoundStatusEffectSystem.cs:109` `public void HandlePartRemoved(EntityUid part, EntityUid body)`
and `:119` `public void HandlePartInserted(EntityUid part)` — so this is **not** a build break. But a repo-wide grep
shows their only caller today is the system's own `RefreshPartWounds` at `:133`. **WP10-1 therefore switches on
Onyx's wound→status-effect apply/remove-on-limb-attach path for the first time.** It is inert only because P2-D1
established that no wound prototype populates `WoundStatusEffectBehavior.StatusEffect`. That dependency between
P2-D1's evidence and WP10-1's runtime behaviour is now stated explicitly and recorded as §7 deviation 14.

### 2.1 `Content.Shared/_WF/Wolfmed/Compat/PartStatusSeverity.cs` — the D10 gap (P2-D11)

```csharp
namespace Content.Shared._Onyx.Targeting; // deliberate: HealthExaminableSystem.PartStatus.cs's `using` and its
                                          // unqualified PartStatusSystem.GetSeverity(...) call resolve unmodified.

/// <summary>Severity classification from Onyx's Targeting PartStatusSystem, without the Targeting-doll types D10 excludes.</summary>
public static class PartStatusSystem
{
    /// <summary>Buckets a part's total damage the way Onyx's part-status readout does.</summary>
    public static PartDamageSeverity GetSeverity(float damage) => damage switch
    {
        <= 0f => PartDamageSeverity.None,
        < 15f => PartDamageSeverity.Minor,
        < 40f => PartDamageSeverity.Moderate,
        < 70f => PartDamageSeverity.Severe,
        _ => PartDamageSeverity.Critical,
    };
}

/// <summary>How badly a body part is damaged, for examine text and colour.</summary>
public enum PartDamageSeverity : byte
{
    None,
    Minor,
    Moderate,
    Severe,
    Critical,
}
```

Body copied verbatim from ONYX `Content.Shared/_Onyx/Targeting/PartStatusSystem.cs` (read via `git show`).
Onyx's sibling members `PartStatus`, `FractureGrade`-typed `Missing` and `PartStatusComponent` belong solely
to the Targeting-doll UI and are **not** ported. `severity.ToString().ToLowerInvariant()` yields exactly
`none/minor/moderate/severe/critical`, which is what the client's `SeverityColor` switch and the five
`health-examinable-part-severity-*` locale keys expect — unchanged from Onyx.

**Registers nothing, subscribes nothing** (a plain `static class`, not an `EntitySystem`).
**Serves:** `Content.Shared/_Onyx/HealthExaminable/HealthExaminableSystem.PartStatus.cs`.

### 2.2 `Content.Shared/_WF/Wolfmed/DoAfter/WolfmedFractureDoAfterSystem.cs` (P2-D4)

```csharp
using Content.Shared._Goobstation.DoAfter;
using Content.Shared._Onyx.Wounds;

namespace Content.Shared._WF.Wolfmed.DoAfter;

/// <summary>Feeds Onyx's fracture manipulation multiplier into Wolfgate's do-after delay event.</summary>
public sealed class WolfmedFractureDoAfterSystem : EntitySystem
{
    [Dependency] private FractureEffectSystem _fractureEffects = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        // Onyx raises its own GetManipulationDurationMultiplierEvent from SharedDoAfterSystem; PLAN §3 keeps
        // that file untouched, so we hang off Wolfgate's existing Goobstation multiplier event instead.
        // Pair verified free: the event is otherwise held by DoAfterDelayMultiplierComponent and BodyComponent.
        SubscribeLocalEvent<WoundHostComponent, GetDoAfterDelayMultiplierEvent>(OnGetDelayMultiplier);
    }

    private void OnGetDelayMultiplier(Entity<WoundHostComponent> ent, ref GetDoAfterDelayMultiplierEvent args)
    {
        // Wolfgate's event carries no Used item, so the active hand decides the symmetry.
        args.Multiplier *= _fractureEffects.GetDurationMultiplier(ent.Owner);
    }
}
```

**Handler shape is `ref`, deliberately**, matching the only existing subscriber of this event
(`WG/Content.Shared/_Goobstation/DoAfter/DoAfterDelayMultiplierSystem.cs:27`
`private void OnGetMultiplier(Entity<DoAfterDelayMultiplierComponent> ent, ref GetDoAfterDelayMultiplierEvent args)`)
even though `GetDoAfterDelayMultiplierEvent` is a **class**, not a `[ByRefEvent]` struct. This compiles and runs
today: RT's by-ref/by-value consistency check lives only in the broadcast path
(`EntityEventBus.Broadcast.cs:217-222`); the directed overloads (`EntityEventBus.Directed.cs:243,273`) do no such
check. **Corrects `fractures.md` §4.3**, which proposed a by-value handler "to match every other subscriber" —
the actual subscriber uses `ref`.

**Do NOT** subscribe `BodyPartRelayedEvent<GetDoAfterDelayMultiplierEvent>` on `WoundableComponent` as an
alternative: the relay targets `BodyPartType.Hand` only (missing fractured *arms*, and Onyx's
`ManipulationParts` is `[Arm, Hand]`) and fires for **both** hands, double-applying.

`GetDurationMultiplier(EntityUid body, EntityUid? used = null)` is `FractureEffectsSystem.cs`'s public API and
raises `GetManipulationDurationMultiplierEvent` on the body, so the vendored handler runs unmodified.

**Serves:** every do-after with `MultiplyDelay = true`.

---

## 3. Upstream `// WOLFGATE` hooks — the complete authorised phase-2 list

Nothing outside this table may be edited in an upstream (non-`_Onyx`, non-`_WF`) file without escalating.
Line numbers were re-verified against the tree as it stands today (phase 1 committed).

| # | File | Site | Change | WP |
|---|---|---|---|---|
| **GUARD E2** | `Content.Server/Body/Systems/BloodstreamSystem.cs` | **`:280`** (drifted from PLAN's `:274`; re-verified) | One line: `if (!HasComp<WoundHostComponent>(ent) && GetBloodLevelPercentage(ent, ent) < ent.Comp.BloodlossThreshold) // WOLFGATE: GUARD E2 — Onyx's HealthExaminable covers pallor per-part for wound hosts`. **No new `using`** — GUARD E already added `using Content.Shared._Onyx.Wounds;`. **Land only together with the HealthExaminable port**, or the "looks pale" message vanishes with nothing replacing it. **Do NOT touch the two `BleedAmount`-based bleeding messages immediately above** (`:271-277`) — `WoundBleedingSystem` already projects onto the body's own `BloodstreamComponent.BleedAmount` via GUARD E3, so they read correctly for wound hosts today. | WP10-2 |
| **GUARD F** | `Content.Shared/HealthExaminable/HealthExaminableSystem.cs` | 5 sites: using block, `:32`, `:45`, `:49`+`:94`, `:96` | (a) `+ using Content.Shared._Onyx.Wounds; // WOLFGATE: GUARD F`. (b) `:32` `var markup = CreateMarkup(uid, args.User, component, damage); // WOLFGATE: GUARD F, examiner param for self-vs-other pain visibility`. (c) `:45` signature becomes `public FormattedMessage CreateMarkup(EntityUid uid, EntityUid examiner, HealthExaminableComponent component, DamageableComponent damage) // WOLFGATE: GUARD F`. (d) wrap the legacy threshold loop: open `if (!HasComp<WoundHostComponent>(uid)) { // WOLFGATE: GUARD F — legacy threshold text is for non-wound-hosts only` after `:49` (`var first = true;`), close after `:94` (the `if (msg.IsEmpty)` block's `}`). (e) **P2-D20 — the part-status call is the `else` of that wrap, not an unconditional statement:** `else AddPartStatusMarkup(uid, examiner, msg); // WOLFGATE: GUARD F — wound hosts only (D2); Onyx calls this unconditionally because every bodied entity there is a wound host`. Shape: <pre>if (!HasComp&lt;WoundHostComponent&gt;(uid)) // WOLFGATE: GUARD F<br>{<br>    … legacy threshold loop + the msg.IsEmpty fallback …<br>}<br>else<br>    AddPartStatusMarkup(uid, examiner, msg); // WOLFGATE: GUARD F — wound hosts only (D2).</pre> Recorded as a deviation from Onyx (§7 deviation 15). **Deliberate simplification vs Onyx: skip Onyx's `_damageable.GetAllDamage` swap inside the legacy branch** — it is a semantic no-op there (D2 guarantees non-wound-hosts are unaffected), and skipping it means this file needs **no `WolfmedDamageableSystem` dependency at all**. **`HealthExaminableComponent.cs` is NOT touched** — WG's 4-value `Thresholds` keeps serving silicons/animals/IPC exactly as today. **Adds no subscription.** | WP10-2 |
| **HOOK 14** | `Content.Client/Examine/ExamineSystem.cs` | **`:202`**, **`:279-281`** | (a) `:202` `_examineTooltipOpen = new Popup { MaxWidth = 560 }; // WOLFGATE: HOOK 14 — was 400; Onyx's part-status boxes are 520 wide`. **This widens every examine popup in the game, not just wound hosts'** — the one HOOK 14 site that is not behaviour-neutral for non-hosts (CRITIQUE2 m3, accepted). Ship it (Onyx does the same) and record it as §7 deviation 16. (b) wrap `:279-281` (the `richLabel` construction) in `if (!TryAddPartStatusMessage(vBox, message)) { … } // WOLFGATE: HOOK 14`. `ExamineSystem` is `public sealed partial class ExamineSystem : ExamineSystemShared` in `namespace Content.Client.Examine` in **both** trees, so Onyx's `_Onyx` partial attaches directly. **Adds no subscription.** | WP10-2 |
| **HOOK 15** | `Content.Client/UserInterface/Systems/DamageOverlays/Overlays/DamageOverlay.cs` | after **`:64`**, and **`:158`** | **Two one-line edits upstream; the body lives in a `_WF` partial** (`DamageOverlay` is declared `public sealed partial class DamageOverlay : Overlay` at `:11`, so `Content.Client/_WF/Wolfmed/Overlays/DamageOverlay.Wolfmed.cs` can declare the same namespace and hold the logic — this is what DECISIONS.md's phase-2 rule "upstream hooks one or two lines (bodies in `_WF` partials)" asks for, and it keeps the new `using`s out of the upstream file). (a) one line after the eye/viewport guard (`:60-64`) and **before** the lerp block (`:96`): `TryApplyWolfmedPain(); // WOLFGATE: HOOK 15 — pain owns the brute vignette on wound hosts`. Writing `BruteLevel` (not `_oldBruteLevel`) is correct: the existing lerp at `:96-103` then smooths it exactly as it does today. The partial resolves `PainSystem` **inside the call**, not in a constructor — the overlay is built from `DamageOverlayUiController.Initialize()`, which can run before entity systems exist. (b) `:158` `_bruteShader.SetParameter("darknessAlphaOuter", 0.8f);` → `0.8f * level;` with a `// WOLFGATE` note citing ONYX `Content.Client/DamageOverlay/DamageOverlay.cs:175` (`<Onyx-PartPain-edited>`). `level` is in scope (`:138-139` `float level = 0f; level = _oldBruteLevel;`). **Adds no subscription** (an `Overlay`, not an `EntitySystem`). **CRITIQUE2 m1** (the code block referenced `PainComponent`/`PainSystem`/`FixedPoint2` while `DamageOverlay.cs:1-7` imports none of them) is **resolved by construction**: the `using`s go in the `_WF` partial. | WP10-4 |
| **HOOK 16** | `Content.Client/UserInterface/Systems/DamageOverlays/DamageOverlayUiController.cs` | **`:98`** | Extend the existing Mono `PainNumbnessComponent` condition at `:98` (`if (!EntityManager.HasComponent<PainNumbnessComponent>(entity)) // Mono - makes this look better`) so the `DamagePerGroup["Brute"]`/`["Burn"]` computation is also skipped when pain owns the vignette: `if (!EntityManager.HasComponent<PainNumbnessComponent>(entity) && !WolfmedPainOwnsVignette(entity)) // WOLFGATE: HOOK 16`. `DamageOverlayUiController` is also `partial` (`:17`), so `WolfmedPainOwnsVignette` — a one-line `HasComponent<PainComponent>` — lives in `Content.Client/_WF/Wolfmed/Overlays/DamageOverlayUiController.Wolfmed.cs` and the upstream file needs **no new `using`**. Without this hook the controller writes a brute level computed off the **projection** (Σ all part damage, i.e. large) on every `MobThresholdChecked`, and `Draw` overwrites it a frame later — an intermediate red flash. **Adds no subscription.** **Ordering (CRITIQUE2 m6): HOOK 16's behaviour is only correct once WP10-3's `IsPainNumb` widening lands** — a tree with WP10-4 but not WP10-3 shows the vignette to pain-numb characters. Both are group F1; ship them together, and if only one can land, land WP10-3 first. | WP10-4 |
| **HOOK 17** | `Content.Server/Chat/EmoteOnDamageComponent.cs` | field list | Four **purely additive** `// WOLFGATE` `[DataField]`s, leaving `Emotes` untouched: `[DataField("emotesThreshold")] public Dictionary<float, HashSet<ProtoId<EmotePrototype>>> EmotesThreshold = new();`, `[DataField("allowedDamageType")] public HashSet<string> AllowedDamageType = ["Blunt","Caustic","Heat","Cold","Piercing","Shock","Slash"];`, `[DataField("painThreshold")] public float PainThreshold = 6f;`, `[ViewVariables] public float LastTotalDamage;`. Plus `using Robust.Shared.Prototypes;` for `ProtoId<>`. Explicit `[DataField("…")]` names match WG's house style in this file. **Onyx *replaces* `Emotes`; we do not** — that would silently change the two `ZombieSystem` `AddEmote(uid, "Scream")` call sites (`ZombieSystem.cs:181`, `ZombieSystem.Transform.cs:145`). The existing `[Access(typeof(EmoteOnDamageSystem))]` already covers these. | WP10-5 |
| **HOOK 18** | `Content.Server/Chat/Systems/EmoteOnDamageSystem.cs` | **`:27`** (first statement of `OnDamage`, declared `:25`) | **One line**, inserted *before* the existing `if (!args.DamageIncreased) return;`, matching Onyx's placement: `HandlePainDamageEmote(uid, emoteOnDamage, args); // WOLFGATE: Wolfmed pain sounds (<Onyx-PainSounds>)`. **No `AddEmote`/`RemoveEmote` threshold overloads** — nothing in Wolfgate calls them. | WP10-5 |

**Explicitly NOT touched in phase 2** (and why):

- `Content.Shared/DoAfter/SharedDoAfterSystem.cs` and `Content.Shared/_Goobstation/DoAfter/DoAfterDelayMultiplierSystem.cs` — P2-D4's `_WF` bridge avoids both. The 2-line `Used`-preserving variant is a user decision (§8.2 item 2).
- `Content.Shared/HealthExaminable/HealthExaminableComponent.cs` — GUARD F's wrap makes its `Thresholds` unreachable for wound hosts; non-hosts keep today's behaviour (D2).
- `Content.Shared/Movement/**` — P2-D1.
- `Content.Shared/Hands/**` — P2-D2 replaces the vendored method instead.
- `Content.Shared/Body/Part/BodyPartType.cs`, `Content.Shared/Humanoid/HumanoidVisualLayers.cs`, `Content.Shared/_Shitmed/Targeting/TargetBodyPart.cs` — D9 stands.
- `Resources/Prototypes/Traits/disabilities.yml` — P2-D15: `MutuallyExclusiveTraits` is checked in both directions client-side (`HumanoidProfileEditor.xaml.cs:998`), so the `_Onyx` entry alone suffices.
- Everything on PLAN §3's original "Explicitly NOT touched" list.

**In-vendored-file `// WOLFGATE` edits** (not upstream hooks, listed here for completeness — full text in §4):

| File | Edits |
|---|---|
| `Content.Shared/_Onyx/Wounds/FractureAlertSystem.cs` | 3: `using Content.Shared._WF.Wolfmed.Body;`, `[Dependency] private WolfmedBodyPartSystem _wfPart`, and `bodyPart.FractureProfile` → `_wfPart.Get(part).FractureProfile` (D8) |
| `Content.Shared/_Onyx/Wounds/FractureEffectsSystem.cs` | 1: `TryGetUsedHandSymmetry` body replaced (P2-D2) |
| `Content.Shared/_Onyx/Wounds/PainSystem.cs` | 1: `IsPainNumb` widened (P2-D8) |
| `Content.Shared/_Onyx/HealthExaminable/HealthExaminableSystem.PartStatus.cs` | **4** (was stated as 2 — CRITIQUE2 B1/m2, both accepted): (1) `:6` `using Content.Shared.Damage.Components;` → `using Content.Shared.Damage; // WOLFGATE: DamageableComponent lives here in Wolfgate, not in .Damage.Components.`; (2) **add** `using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE: D12 damage facade.`; (3) **add** `[Dependency] private WolfmedDamageableSystem _damageable = default!; // WOLFGATE: D12` — an *addition*, not a swap, because in Onyx that dependency lives in the **base** `HealthExaminableSystem.cs:15`, which GUARD F deliberately leaves without a facade; (4) `PartOrder`'s `Chest`/`Groin` folded to `Torso` (D9) |
| `Content.Server/_Onyx/Chat/EmoteOnDamageSystem.PainSounds.cs` | **4** (was stated as 1-2): (1) **add** `using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE: D12` (CRITIQUE2 B1's third site, accepted); (2) `[Dependency] private DamageableSystem _damageable` → `WolfmedDamageableSystem` (D12); (3) the P2-D22 wound-host guard as the first statement of `HandlePainDamageEmote`; (4) Onyx's `ProtoMan` → the injected `IPrototypeManager` **if referenced** — at the pin it is not (the file was read in full), so this edit is expected to be a no-op |

---

## 4. Work packages

Build order and parallelism:

```
 group F1 (fully disjoint file sets, run in parallel)
 ┌─ WP10-1  Fractures + T-FIXTURE        (4 files)   [owns WoundFractureTest.cs's prototype block]
 ├─ WP10-2  HealthExaminable + GUARD E2  (10 files)
 ├─ WP10-3  HighPainThreshold + IsPainNumb (5 files)  ← land before WP10-4 if they must be split
 ├─ WP10-4  Pain HUD overlay             (4 files: 2 hooks + 2 _WF partials)
 └─ WP10-6a Bridge tests T-AP / T-PASSIVE (1 file)   ← no phase-2 dependency, write first
                         │
 group F2 ───────────────┴─► WP10-5  Pain sounds + mob wiring (4 files)  [owns base.yml]
                         │
 group F3 ───────────────┴─► WP10-6b Fracture + pain tests   (2 files)
                         │
 group F4 ───────────────┴─► WP10-7  Docs + manifest         (3 files)
```

**Serialisation rule 0 — `WoundFractureTest.cs`:** WP10-1 lands **only** the `[TestPrototypes]` block
(T-FIXTURE / P2-D21: the `left hand` slot, `- type: Hands`, and the second `WoundFractureHandsBody`) and leaves
the test methods alone; WP10-6b (group F3, strictly after F1) writes every assertion. The fixture belongs to
WP10-1 because without it WP10-1's own build checkpoint cannot demonstrate that `GetDurationMultiplier` reaches
a hand at all.

**Serialisation rule 1 — `base.yml`:** `Resources/Prototypes/Entities/Mobs/Species/base.yml` is owned
**exclusively by WP10-5**. Both `- type: PainShockTarget` (P2-D7) and `- type: EmoteOnDamage` (P2-D9) go in the
same existing `# WOLFGATE — Wolfmed phase 1 (D21/D32)` block at `:250-252`. No other WP edits that file.

**Serialisation rule 2 — the docs (CRITIQUE2 M4, accepted).** Ground rule 6 tells every WP to append to
`Docs/Wolfmed/WOLFMED_MANIFEST.md`, and §4's graph runs WP10-1/-2/-3/-4/-6a in parallel — five concurrent
appends to one file, with one silently losing, exactly the hazard §8.3 item 2 already identifies for `base.yml`.
**WP10-7 is the sole editor of `Docs/Wolfmed/WOLFMED_MANIFEST.md`, `Docs/Wolfmed/WOLFMED_PLAN.md` and
`Docs/Wolfmed/WOLFMED_STATUS.md`.** Every other WP writes its manifest rows and deviations to
`C:/Users/jzo12/Documents/Wolfmed/plan/p2/manifest-rows-WP10-N.md` instead — same content, same format as §7's tables — and
WP10-7 merges them. Ground rule 6 is amended accordingly for phase 2: *record every file you touch in your own
`manifest-rows-WP10-N.md`*. A WP that produces no such file has not finished.

---

### WP10-1 — Fractures (parallel group F1)

**Goal:** fracture movement and manipulation penalties are live, the `BrokenBones` alert tracks grade and
treatment, and do-afters are slowed by fractured arms.

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | `Content.Shared/_Onyx/Wounds/FractureAlertSystem.cs` | same | **modified** (3 edits) |
| 2 | `Content.Shared/_Onyx/Wounds/FractureEffectsSystem.cs` | same | **modified** (1 method replaced) |
| 3 | — | `Content.Shared/_WF/Wolfmed/DoAfter/WolfmedFractureDoAfterSystem.cs` | **new** (§2.2) |

**Order matters:** #1 before #2 (`FractureEffectSystem` holds `[Dependency] private FractureAlertSystem _fractureAlerts`), #2 before #3.

**Exact edits, file 1 (`FractureAlertSystem.cs`, 46 lines, class `FractureAlertSystem`):**

Add to the using block and the dependency block:
```csharp
using Content.Shared._WF.Wolfmed.Body; // WOLFGATE: D8 keeps Onyx's part fields on WolfmedBodyPartComponent.
…
    [Dependency] private WolfmedBodyPartSystem _wfPart = default!; // WOLFGATE: D8, Onyx's extra part fields.
```
Replace the loop head (`:22-27`):
```csharp
        foreach (var (part, bodyPart) in _body.GetBodyChildren(uid))
        {
            // WOLFGATE: D8, FractureProfile lives on WolfmedBodyPartComponent, not Shitmed's BodyPartComponent.
            if (_wfPart.Get(part).FractureProfile is not { } profileId ||
                !_prototypes.TryIndex(profileId, out FractureProfilePrototype? profile) ||
                profile.Alert is not { } alert)
                continue;
```
`bodyPart` becomes an unused deconstruction variable — **leave it**, do not change it to `_` (matches
`WoundFractureSystem.cs:148`'s precedent; an unused deconstruction variable is not a warning).
**Keep `using Content.Shared.Body;` verbatim** (P2-D3).
`FractureAlertSystem` has **no `Initialize` and no subscriptions** — pure API (`Refresh(EntityUid?)`).
Note its second loop (`foreach (var profile in _prototypes.EnumeratePrototypes<FractureProfilePrototype>())`)
clears any alert no live profile claims — keep verbatim.

**Exact edits, file 2 (`FractureEffectsSystem.cs`, 204 lines, class `FractureEffectSystem`):**

The file name (`…Effects…`) does not match the class name (`FractureEffectSystem`) in Onyx. **Keep both as-is** —
Onyx's own test and `SpeciesChangeEntityEffectSystem` name the class `FractureEffectSystem`.

Replace `TryGetUsedHandSymmetry` (`:126-151`) with, verbatim:
```csharp
    // WOLFGATE: Wolfgate's hands API hands back Hand objects, not hand-id strings (GetActiveHand returns
    // Hand?, IsHolding's out param is Hand?), Hand is a class so there is no .Value, and HandLocation has no
    // Functional* members. Same rewrite as WoundDamageRoutingSystem.TryGetActiveHandPart (:606-633).
    private bool TryGetUsedHandSymmetry(EntityUid body, EntityUid? used, out BodyPartSymmetry symmetry)
    {
        symmetry = BodyPartSymmetry.None;
        if (!TryComp(body, out HandsComponent? hands))
            return false;

        Hand? hand;
        if (used is { } item)
        {
            if (!_hands.IsHolding(body, item, out hand, hands))
                return false;
        }
        else
            hand = _hands.GetActiveHand((body, hands));

        if (hand is null)
            return false;

        symmetry = hand.Location switch
        {
            HandLocation.Left => BodyPartSymmetry.Left,
            HandLocation.Right => BodyPartSymmetry.Right,
            _ => BodyPartSymmetry.None,
        };
        return symmetry != BodyPartSymmetry.None;
    }
```
Overload binding for `_hands.IsHolding(body, item, out hand, hands)`: four arguments
`(EntityUid, EntityUid, out Hand?, HandsComponent)` match `SharedHandsSystem.cs:285` exactly; the 2-arg `:280`
overload is not applicable. No ambiguity.

**Semantics preserved:** which hand (the `used` item's hand if held, else the active hand) and which limb
(`OnGetMultiplier` then filters `GetBodyChildren` by `ManipulationParts` **and** `bodyPart.Symmetry == symmetry`)
are both unchanged. The only loss is `HandLocation.Functional*`, which no Wolfgate entity can carry — a
nil-behaviour loss, not a compromise. `HandLocation`'s reversed ordinals between trees are irrelevant: the
switch is by name.

**`WoundStatusEffectSystem` is a dependency of this file** (CRITIQUE2 M5, accepted): keep
`[Dependency] private WoundStatusEffectSystem _statusEffects = default!;` (`:23`) and both call sites (`:64`
`HandlePartInserted`, `:71` `HandlePartRemoved`) **verbatim**. Both methods exist in WG
(`WoundStatusEffectSystem.cs:119` and `:109`), so nothing needs shimming — but note in the WP report that this
gives Onyx's wound→status-effect limb-attach path its first caller in Wolfgate. It is inert at the pin only
because no wound prototype names a `StatusEffect` (P2-D1); if one ever does, re-check.

**No other edit.** Specifically: no using swap (P2-D3); the `ref` subscription to the class event
`RefreshMovementSpeedModifiersEvent` (`:29`) is legal in RT 277; `GetManipulationDurationMultiplierEvent` is
already vendored at `WoundEvents.cs:116` and only needs the WP10-1 #3 raiser.

**Prototypes / locale / textures: ZERO.** All verified already shipped by WP7 and byte-identical to Onyx:
`BrokenBones` alert (`Resources/Prototypes/_Onyx/Alerts/alerts.yml`, **no `category:`**, so no
`AlertCategoryPrototype` and `ClearAlertCategory` is never called), `fracture.rsi/{brokenbones.png,meta.json}`
(CC-BY-SA-3.0, tgstation/darkrell, 32×32, state `brokenbones`), `fractures.ftl` (2 keys),
`OrganicFractureProfile` with all four grades and `alert:`/`alertMinimumGrade:`/`alertHiddenTreatments:`, and the
`fractureProfile: OrganicFractureProfile` rows on all ten `Wolfmed*` part abstracts in
`Resources/Prototypes/_WF/Wolfmed/Body/parts.yml`. Only `ShowAlert`/`ClearAlert` are called — **not**
`UpdateAlert`.

**Subscription pairs registered:** 8 by `FractureEffectSystem` + 1 by the bridge. All verified free — see §5.

**Build checkpoint:** Server + Client + IntegrationTests green. **The client build is the one that matters** —
it proves the shared file carries no server-only reference. `FractureEffectsSystem` deliberately has **no**
`_net.IsServer` gate (`OnRefreshSpeed` must run client-side or movement mispredicts every tick); all its reads
are from `[NetworkedComponent, AutoGenerateComponentState]` components plus the prototype-sourced
`WolfmedBodyPartComponent`.

**After landing:** re-run `EntityTest.SpawnAndDeleteAllEntities*` and `PrototypeSaveTest`.
`FractureEffectSystem.OnPartChanged` reaches `EnsureComp<BodyPartFunctionalityComponent>` via
`_functionality.Refresh` → `RefreshPart` — the same shape as the `DebugAssertException` WP9 fixed
(D28's `TerminatingOrDeleted` guards).

---

### WP10-2 — HealthExaminable + GUARD E2 (parallel group F1)

**Goal:** examining a wound host shows a part-by-part injury breakdown (and, for yourself, a pain word) instead
of the stale body-level threshold text, and the now-meaningless "looks pale" line is retired.

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | `Content.Shared/_Onyx/HealthExaminable/HealthExaminableSystem.PartStatus.cs` | same | **modified** (**4** edits) |
| 2 | `Content.Shared/_Onyx/HealthExaminable/HealthExaminableSystem.Pain.cs` | same | **verbatim** |
| 3 | `Content.Client/_Onyx/HealthExaminable/ExamineSystem.PartStatus.cs` | same | **verbatim** |
| 4 | `Content.Client/_Onyx/HealthExaminable/PartStatusTag.cs` | same | **verbatim** |
| 5 | — | `Content.Shared/_WF/Wolfmed/Compat/PartStatusSeverity.cs` | **new** (§2.1) |
| 6 | — | `Content.Shared/HealthExaminable/HealthExaminableSystem.cs` | **new (hook)** — GUARD F |
| 7 | — | `Content.Client/Examine/ExamineSystem.cs` | **new (hook)** — HOOK 14 |
| 8 | — | `Content.Server/Body/Systems/BloodstreamSystem.cs` | **new (hook)** — GUARD E2, `:280` |
| 9 | `Resources/Locale/en-US/_Onyx/medical/health-examinable.ftl` | same | **verbatim** (**36** message ids — the file was read in full and counted; PLAN2's earlier "43" was copied from the manifest's *`wounds.ftl`* row and CRITIQUE2's m4 "45" is also wrong) |
| 10 | — | `Resources/Locale/en-US/_Onyx/prototypes/wounds/wounds.ftl` | **modified — DELETE `:46-52`** |
| — | `Resources/Locale/en-US/_Onyx/targeting/part-status.ftl` | — | **skipped** (orphaned, P2-D12) |

**Why this ports so cleanly:** WG's `Content.Shared/HealthExaminable/HealthExaminableSystem.cs` is the
**untouched upstream ancestor Onyx forked from** — same namespace, same `public sealed partial class`, same
method names; the only differences from Onyx's edited copy are exactly GUARD F's two tag blocks
(`<Onyx-SelfPainExamine-edited>`, `<Onyx-PartHealthExamine-edited>`). The same holds for
`Content.Client/Examine/ExamineSystem.cs` (`public sealed partial class ExamineSystem : ExamineSystemShared` in
`namespace Content.Client.Examine`, byte-identical declaration). **Both Onyx partials attach directly — no `_WF`
indirection for the partial mechanism itself.**

**File 1's four `// WOLFGATE` edits** (was "two" — CRITIQUE2 B1 and m2, both verified and accepted):

**(1) and (2) — the using block.** Onyx's `:6` is `using Content.Shared.Damage.Components;` and the body uses
the type at `if (TryComp(part, out DamageableComponent? damageable))`. In Wolfgate the *file* is at
`Content.Shared/Damage/Components/DamageableComponent.cs` but the *namespace* is not:
`WG/Content.Shared/Damage/Components/DamageableComponent.cs:9` `namespace Content.Shared.Damage`, `:21`
`public sealed partial class DamageableComponent : Component`. `Content.Shared.Damage.Components` does exist in
WG, so the `using` compiles — and then `DamageableComponent` is unresolved: **CS0246**. Phase 1 hit exactly this
and marked it at `WG/Content.Shared/_Onyx/Wounds/WoundFractureSystem.cs:4`. So:
```csharp
using Content.Shared.Damage; // WOLFGATE: DamageableComponent lives here in Wolfgate, not in .Damage.Components.
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE: D12 damage facade.
```
**(3) — the facade dependency is an addition, not a swap.** In Onyx, `_damageable` is declared on the **base**
partial (`ONYX Content.Shared/HealthExaminable/HealthExaminableSystem.cs:15`
`[Dependency] private DamageableSystem _damageable = default!;`), and WG's copy of that file has only
`[Dependency] private ExamineSystemShared _examineSystem = default!;` at `:12`. GUARD F deliberately leaves the
base file facade-free, so the line has to be **added here**, or an implementer searching for a line to change
will not find one:
```csharp
    [Dependency] private WolfmedDamageableSystem _damageable = default!; // WOLFGATE: D12, Onyx-shaped damage API.
```
`WolfmedDamageableSystem.GetPositiveDamage(Entity<DamageableComponent> ent)` is at
`WG/Content.Shared/_WF/Wolfmed/Compat/WolfmedDamageableSystem.cs:89`, which is what `:44`'s
`_damageable.GetPositiveDamage((part, damageable))` binds to.
`_body` needs **no** action: Onyx declares `[Dependency] private SharedBodySystem _body` in
`HealthExaminableSystem.Pain.cs:8`, which file 2 ports verbatim — which is why files 1 and 2 must land together.

**(4) — the `PartOrder` fold (D9):**
```csharp
    private static int PartOrder(BodyPartType type) => type switch
    {
        BodyPartType.Head => 0,
        BodyPartType.Torso => 1, // WOLFGATE (D9): Chest and Groin folded to Torso; Wolfgate's enum has neither.
        BodyPartType.Arm => 3,
        BodyPartType.Hand => 4,
        BodyPartType.Leg => 5,
        BodyPartType.Foot => 6,
        BodyPartType.Tail => 7,
        _ => 8,
    };
```
(Non-contiguous literals are cosmetic; switch expressions do not require contiguity.)

**Nothing else in file 1 needs touching**, and three things that look like they might do not:
`WoundComponent`, `WoundBleedingComponent` and `WoundScarComponent` are all still in **`Content.Shared`**
(`WG/Content.Shared/_Onyx/Wounds/WoundDamageComponents.cs:181,210,259`) despite D13 moving
`WoundBleedingSystem` to `Content.Server`, so a shared file may reference them; `WoundStageDefinition.ExamineDescription`
exists (`WoundBehaviors.cs:139`) and `WoundPrototype.GetStageDefinition` at `WoundPrototype.cs:76`; and the
`ProtoId<WoundPrototype> SurgicalIncision = "SurgicalIncisionWound"` constant resolves to a real prototype
(`Resources/Prototypes/_Onyx/Wounds/wounds.yml:370`) even though D7 skips Onyx surgery — and it is only ever
compared by id, never indexed, so it would be safe regardless.

**File 2 needs zero edits.** Its dependencies (`SharedBodySystem`, `PainSystem`, `PainSystem.GetPain`) are all
phase-1 present. **It is also where file 1 gets `_body` from** (`[Dependency] private SharedBodySystem _body`
and `[Dependency] private PainSystem _pain`, ONYX `HealthExaminableSystem.Pain.cs:8-9`) — files 1 and 2 do not
compile apart.

**`using Content.Shared._Onyx.Targeting;` in file 1 keeps resolving** once file 5 lands in that namespace.

**Hooks:** exact code in §3 (GUARD F, HOOK 14, GUARD E2).

**Sandbox: nothing to do.** Every client UI type files 3/4 use (`BoxContainer`, `ScrollContainer`,
`PanelContainer`, `ContainerButton`, `RichTextLabel`, `StyleBoxFlat`, `Thickness`, the `RichText` tag types,
`Vector2Helpers.Infinity`, `FormattedMessage`/`MarkupNode` helpers) is already used elsewhere in WG's client
code with no `Sandbox.yml` entry. Onyx's `[partstatus …]`/`[partstatusend]` markup tags need **no** engine
registration — `FormattedMessage`'s parser is purely syntactic, and the client walks `message.Nodes` by hand
before any node reaches `RichTextLabel.SetMessage`'s `tagsAllowed` path.

**Subscription pairs registered: ZERO.** Neither `_Onyx` partial declares `Initialize()`;
`HealthExaminableSystem`'s existing `<HealthExaminableComponent, GetVerbsEvent<ExamineVerb>>` is untouched;
the client partial's `TryAddPartStatusMessage` is called synchronously from `UpdateTooltipInfo`; the new shim is
a `static class`.

**Build checkpoint:** Server + Client + IntegrationTests green, **then** YAML/locale: launch a headless server
and confirm no Fluent duplicate-id error. **Step 10 is a deletion and is easy to forget** — the WP task
description must name the file explicitly.

---

### WP10-3 — `HighPainThreshold` trait + pain-numbness widening (parallel group F1)

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | `Content.Shared/_Onyx/Traits/HighPainThresholdComponent.cs` | same | **verbatim** |
| 2 | `Content.Shared/_Onyx/Traits/HighPainThresholdSystem.cs` | same | **verbatim** |
| 3 | `Resources/Prototypes/_Onyx/Traits/quirks.yml` | same | **adapted** — `HighPainThreshold` entry only |
| 4 | `Resources/Locale/en-US/_Onyx/traits/quirks.ftl` | same | **adapted** — 2 keys only |
| 5 | — | `Content.Shared/_Onyx/Wounds/PainSystem.cs` | **modified** — `IsPainNumb` widening |

File 1 is `[RegisterComponent, NetworkedComponent]` with one `[DataField] public float PainMultiplier = 0.75f;`.
File 2 is 23 lines: `SubscribeLocalEvent<HighPainThresholdComponent, ModifyPainGainEvent>(OnModifyPainGain);` +
`args.Multiplier *= ent.Comp.PainMultiplier;`. `Content.Shared._Onyx.Wounds.ModifyPainGainEvent` resolves
(`WoundEvents.cs:44`).

**Correctness:** `ModifyPainGainEvent` is raised on the **body** (`PainSystem.cs:114-116`, `:239-241` —
`target = part.Body ?? entity`), and `TraitSystem` adds trait components to `args.Mob`. They agree.

**File 3 (trimmed, same trim WP7 applied to `_Onyx/Alerts/alerts.yml`):** keep Onyx's SPDX header; port only
the `HighPainThreshold` block; `conflicts: [PainNumbness]` → `mutuallyExclusiveTraits: [PainNumbness]`
(resolves against WG's own pre-existing `PainNumbness` trait at `Resources/Prototypes/Traits/disabilities.yml:69`);
**drop `cost: 3`** (P2-D15). Do not port `Voracious`/`ColdBlooded`/etc.

**File 5's edit** — inside `IsPainNumb` (`PainSystem.cs:324-331`), before the `EnumerateStatusEffects` return:
```csharp
        // WOLFGATE (P2-D8): Wolfgate's PainNumbness trait grants the legacy PainNumbnessComponent
        // (Content.Shared/Traits/Assorted/PainNumbnessComponent.cs); Onyx's status-effect form
        // (StatusEffectPainNumbness) has no applier here — TraitPrototype has no `specials:`, and the
        // narcotics that apply it are phase 4. Honour both.
        if (HasComp<PainNumbnessComponent>(entity))
            return true;
```
Place it **after** the existing part→body redirect (`if (TryComp(entity, out BodyPartComponent? part) && part.Body is { } body) entity = body;`)
so it tests the body, matching the status-effect branch. `Content.Shared.Traits.Assorted` is already
`using`-ed at `PainSystem.cs:13` — **no new using**.

**Registration check:** `grep -rn "HighPainThreshold"` across WG C#, YAML and FTL → **zero hits** (verified).
Free.

**Subscription pair:** `<HighPainThresholdComponent, ModifyPainGainEvent>` — free (§5).

---

### WP10-4 — Pain HUD overlay (parallel group F1)

| # | Path | Status |
|---|---|---|
| 1 | `Content.Client/UserInterface/Systems/DamageOverlays/Overlays/DamageOverlay.cs` | **modified (hook)** — HOOK 15, **2 one-line edits** |
| 2 | `Content.Client/UserInterface/Systems/DamageOverlays/DamageOverlayUiController.cs` | **modified (hook)** — HOOK 16, **1 condition** |
| 3 | `Content.Client/_WF/Wolfmed/Overlays/DamageOverlay.Wolfmed.cs` | **new** — HOOK 15's body |
| 4 | `Content.Client/_WF/Wolfmed/Overlays/DamageOverlayUiController.Wolfmed.cs` | **new** — HOOK 16's predicate |

**Both upstream classes are `partial`** (`DamageOverlay.cs:11` `public sealed partial class DamageOverlay : Overlay`;
`DamageOverlayUiController.cs:17` `public sealed partial class DamageOverlayUiController : UIController`), so the
logic goes in `_WF` partials and the upstream edits stay at one line each — which is what DECISIONS.md's phase-2
rule asks for ("upstream hooks one or two lines (bodies in `_WF` partials)") and which also disposes of
CRITIQUE2 m1: `DamageOverlay.cs:1-7` imports neither `Content.Shared._Onyx.Wounds` nor `Content.Shared.FixedPoint`,
and now it does not need to.

**HOOK 15 site (a)** — one line after the eye/viewport guard at `:60-64` and **before** the lerp block at `:96`:
```csharp
        TryApplyWolfmedPain(); // WOLFGATE: HOOK 15 — pain owns the brute vignette on wound hosts (Wolfmed phase 2).
```

**File 3, `Content.Client/_WF/Wolfmed/Overlays/DamageOverlay.Wolfmed.cs`:**
```csharp
using Content.Shared._Onyx.Wounds;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;

namespace Content.Client.UserInterface.Systems.DamageOverlays.Overlays;

public sealed partial class DamageOverlay
{
    /// <summary>Overrides the brute vignette with Onyx's pain level on wound hosts.</summary>
    private void TryApplyWolfmedPain()
    {
        // Onyx replaces the brute-damage vignette with a pain vignette
        // (ONYX Content.Shared/DamageOverlay/SharedDamageOverlaySystem.cs:93-97). Wolfgate has no shared damage
        // overlay and RT 277 has no [SubscribeLocalEvent], so this reads the networked PainComponent directly -
        // PainSystem.GetPain is a pure read of AutoNetworkedFields. The system is resolved here, not in a
        // constructor: the overlay is built from DamageOverlayUiController.Initialize(), which can run before
        // entity systems exist.
        if (State == MobState.Dead ||
            _playerManager.LocalEntity is not { } local ||
            !_entityManager.TryGetComponent(local, out PainComponent? pain) ||
            pain.SoftPainCap <= FixedPoint2.Zero)
            return;

        var painLevel = FixedPoint2
            .Min(1f, _entityManager.System<PainSystem>().GetPain((local, pain)) / pain.SoftPainCap)
            .Float();
        BruteLevel = painLevel < 0.05f ? 0f : painLevel;
    }
}
```
`_entityManager` and `_playerManager` are already injected (`DamageOverlay.cs:15-16`) and are reachable from the
partial. `State` is the field the controller writes (`:24`). Writing `BruteLevel` (not `_oldBruteLevel`) is
deliberate — the existing lerp at `:96-103` smooths it exactly as it does today. The `State == MobState.Dead`
bail matches Onyx, which explicitly zeroes `PainLevel` on death (`PainSystem` does not clear pain on death); the
existing `Critical` guard at `:145` already suppresses the red vignette there, also matching Onyx.
`Robust.Shared.Maths` and `Robust.Shared.GameObjects` are global usings in Content.Client
(`Content.Client/GlobalUsings.cs:8,10`), so `System<T>()` and `Color` need no import.

**HOOK 15 site (b):** `:158` `_bruteShader.SetParameter("darknessAlphaOuter", 0.8f);` → `0.8f * level`
(`<Onyx-PartPain-edited>`). `level` is in scope from `:138-139`. This is the *only* other difference between
WG's `Draw` body and Onyx's.

**HOOK 16 + file 4:** the upstream edit is the `:98` condition (see §3);
`Content.Client/_WF/Wolfmed/Overlays/DamageOverlayUiController.Wolfmed.cs` declares
`namespace Content.Client.UserInterface.Systems.DamageOverlays;`, `public sealed partial class DamageOverlayUiController`,
and one method:
```csharp
    /// <summary>True when Wolfmed's pain level, not brute damage, drives the vignette for this entity.</summary>
    private bool WolfmedPainOwnsVignette(EntityUid entity) => EntityManager.HasComponent<PainComponent>(entity);
```
**Ordering (CRITIQUE2 m6):** WP10-4 is only *correct* once WP10-3's `IsPainNumb` widening lands — HOOK 16 stops
the controller zeroing `BruteLevel` for `PainComponent` holders, and the pain-numb suppression the HUD then
relies on is P2-D8's widening in WP10-3. Both are group F1 with disjoint files; ship them together.

**Numbers this will show** (P2-4 — measure, do not assume): overlay level = `min(1, GetPain / SoftPainCap)`
with `SoftPainCap = 135` (`WoundDamageComponents.cs:137`) and a 0.05 floor, so the vignette starts at pain
**6.75** and sits at **0.963** when pain shock fires at 130. `PainComponent.Value` on the body is the sum of the
parts' raw pain (`WoundDamageProjectionSystem.RefreshBodyPain`), so the HUD reads the **body's** component.

**Prediction:** zero. Every `PainSystem` mutator is `_net.IsServer`-gated; every reader touches only
`[AutoNetworkedField]` state. **Do not subscribe `PainChangedEvent` client-side** — every `RaisePainChanged`
call sits inside a server-gated method, so it would compile and never run. The `Draw()` design needs no trigger
at all. **Caveat to record:** `PainComponent.{SoftPainCap, RecoveryPerSecond, DamageMultipliers}` are `[DataField]`
**without** `[AutoNetworkedField]`; because no prototype declares `- type: Pain`, both sides instantiate from the
same C# initialisers and agree. The day anyone wants a per-species `softPainCap`, they must declare it in YAML
**and** make the field `[AutoNetworkedField]`, or the HUD's denominator desyncs.

**Sandbox: nothing to do.** `FixedPoint2`, `IEntityManager.System<T>()`, `IPlayerManager.LocalEntity` are all
already used in these two files and in `Content.Client/Overlays/EntityHealthBarOverlay.cs`.

**Subscription pairs registered: ZERO** (an `Overlay` and a `UIController`, neither of which can hold a directed
subscription).

**Build checkpoint:** Client build is the gate. Also spot-check visually or via an integration assertion on the
computed level (§6, T-PAIN-OVERLAY) — the vignette itself is not headlessly assertable.

---

### WP10-5 — Pain sounds + mob wiring (group F2; **owns `base.yml`**)

**Runs after F1** only because it is the sole owner of `Resources/Prototypes/Entities/Mobs/Species/base.yml`;
its C# is independent.

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | — | `Content.Server/Chat/EmoteOnDamageComponent.cs` | **new (hook)** — HOOK 17 |
| 2 | — | `Content.Server/Chat/Systems/EmoteOnDamageSystem.cs` | **new (hook)** — HOOK 18 |
| 3 | `Content.Server/_Onyx/Chat/EmoteOnDamageSystem.PainSounds.cs` | same | **modified** (1-2 edits) |
| 4 | — | `Resources/Prototypes/Entities/Mobs/Species/base.yml` | **modified** — 2 blocks |

**File 3:** vendor with `public sealed partial class EmoteOnDamageSystem` in **`namespace Content.Server.Chat.Systems`**
(file path `_Onyx`, namespace upstream — exactly D13's pattern; Onyx's own file already declares that namespace
at `:12`). Four edits (§3's table):

1. **`using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE: D12`** — missing from PLAN2's earlier "1-2 edits"
   count (CRITIQUE2 B1's third site, accepted).
2. `[Dependency] private DamageableSystem _damageable = default!;` (`:17`) →
   `[Dependency] private WolfmedDamageableSystem _damageable = default!; // WOLFGATE: D12`. **The call site at
   `:21` `_damageable.GetTotalDamage(uid).Float()` compiles unchanged**: the facade's overload is
   `public FixedPoint2 GetTotalDamage(Entity<DamageableComponent?> ent)`
   (`WolfmedDamageableSystem.cs:134`) and RT's `Entity<T>` declares
   `public static implicit operator Entity<T?>(EntityUid owner)`
   (`RobustToolbox/Robust.Shared/GameObjects/Entity.cs:39`), so `EntityUid` converts implicitly. Writing
   `((uid, null))` is equivalent; do not "fix" it to `Comp<DamageableComponent>(uid).TotalDamage`, which would
   read the raw projection instead of the facade.
3. **P2-D22 — the wound-host gate, as the first statement of `HandlePainDamageEmote`:**
   ```csharp
       // WOLFGATE (P2-D22): D32 strips only WoundHostComponent from Protogen, so without this gate the
       // D32-excluded species would still get Wolfmed's pain screams - a D2 breach. Onyx needs no such
       // check because every mob there is a wound host.
       if (!HasComp<WoundHostComponent>(uid))
           return;
   ```
   `using Content.Shared._Onyx.Wounds;` is already line 1 of Onyx's file, so this costs no import.
4. Onyx's other usings all resolve in WG as-is: `Content.Shared.Damage` (`DamageChangedEvent` is declared in
   `Content.Shared/Damage/Systems/DamageableSystem.cs:542`, `namespace Content.Shared.Damage` at `:25`),
   `Content.Shared.Damage.Components`, `Content.Shared.Damage.Systems` and `Content.Shared.Damage.Events` all
   exist as namespaces in WG, and `Content.Shared.StatusEffectNew`'s `StatusEffectsSystem`
   (`StatusEffectSystem.API.cs:8,10`) is the phase-1 vendored one. If Onyx's copy referenced `ProtoMan` we would
   use the class's already-injected `IPrototypeManager _prototypeManager` (`EmoteOnDamageSystem.cs:14`) — RT 277
   has no `EntitySystem.ProtoMan` — but at the pin the file does not reference it, so this is expected to be a
   no-op. Keep `StatusEffectsSystem`
(`TryEffectsWithComp<PainNumbnessStatusEffectComponent>` resolves, `StatusEffectSystem.API.cs:357`) — note it is
currently unreachable in WG for the same reason as P2-D8; the widening in WP10-3 is on the *other* (legacy)
component, so consider adding the same `HasComp<PainNumbnessComponent>` clause here for consistency, or record
the asymmetry.

**File 3 registers NO subscription.** `<EmoteOnDamageComponent, DamageChangedEvent>` is already owned by
`Content.Server/Chat/Systems/EmoteOnDamageSystem.cs:22`; a second subscription is a server-start crash. The pain
path is reached **from** that handler via HOOK 18.

**File 4 — two blocks, both appended to the existing `# WOLFGATE — Wolfmed phase 1 (D21/D32)` block on
`BaseMobSpeciesOrganic` (`base.yml:250-252`).** **P2-D22 corrects the rationale** PLAN2 previously gave here:
that block does *not* track the Protogen exclusion — D32 is a C# opt-out that removes `WoundHostComponent`
only (`WolfmedWoundHostExclusionSystem.cs:12,17,33`). `PainShockTarget` on Protogen is inert (no `PainComponent`
is ever ensured on a non-host), and `EmoteOnDamage` is gated in C# by P2-D22's guard, not by the YAML block.
```yaml
  # WOLFGATE - Wolfmed phase 2 (P2-D7). ONYX Resources/Prototypes/Body/species_base.yml:54 (<Onyx-PainShock>);
  # placed here rather than on BaseMobSpecies so only organic profiles carry it. Inert on D32-excluded
  # Protogen: PainComponent is only ever ensured on wound hosts.
  - type: PainShockTarget

  # WOLFGATE - Wolfmed phase 2 (P2-D9). ONYX Resources/Prototypes/Body/species_base.yml:124-133
  # (<Onyx-PainSounds>, verified at species_base.yml:124-133). Key corrected from Onyx's `emotes:` to
  # `emotesThreshold:` - Onyx's own YAML does not bind (RT drops unknown keys; the field is EmotesThreshold).
  # Gated to wound hosts in C# by P2-D22. Recorded as a corrected-upstream-bug deviation.
  - type: EmoteOnDamage
    emotesThreshold:
      50: [ Scream ]
      80: [ Scream, Crying ]
    emoteChance: 0.6
    withChat: true
    hiddenFromChatWindow: true
    emoteCooldown: 8
```
`Emotes` is left empty, so WG's existing `OnDamage` body returns at its `Emotes.Count == 0` check — **no double
emote**. `Scream` (`Resources/Prototypes/Voice/speech_emotes.yml:3`) and `Crying` (`:110`) both exist.

**`DamageChangedEvent` still fires on a wound host** — `WoundDamageProjectionSystem.RefreshBodyDamage` writes
through the facade's `SetDamage`, which calls `DamageableSystem.DamageChanged(..., delta, interruptsDoAfters: false)`
(D30). WP9's `DamageChangedDeltaSurvivesProjectionTest` is the standing gate.

**Pain-shock balance warning:** `StunSystemOnyxCompat` maps onto `TryParalyze`, which re-triggers stun VFX on
every call (a recorded §8.2 deviation from phase 1). `UpdatePainShock` disarms after each shock and rearms only
below pain 110, so repeat firing is bounded — but **this is the first build in which it will actually run**.

**Build checkpoint:** Server + Client + IntegrationTests green; YAML lints in **Release**.

---

### WP10-6 — Tests

**Split into 6a (group F1, no dependency) and 6b (group F3).** Full content in §6.

| Part | File | Tests |
|---|---|---|
| **6a** | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedDamageBridgeTest.cs` (extend) | T-AP, T-PASSIVE-A, T-PASSIVE-B |
| **6b** | `Content.IntegrationTests/Tests/_Onyx/Wounds/WoundFractureTest.cs` (extend) | ported `EffectsRefreshOnTreatmentHealingAndDetachTest`, fracture-alert (+ below-threshold negative), hand-symmetry |
| **6b** | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedPainTest.cs` (new) | T-PAIN-OVERLAY, T-PAIN-SHOCK, T-HIGH-PAIN-THRESHOLD, T-PAIN-NUMB |

---

### WP10-7 — Docs (group F4)

| # | Path | Action |
|---|---|---|
| 1 | `Docs/Wolfmed/WOLFMED_PLAN2.md` | **new** (P2-5) — this document, adapted for the repo |
| 2 | `Docs/Wolfmed/WOLFMED_MANIFEST.md` | **modified** — §7's rows and deviations |
| 3 | `Docs/Wolfmed/` status doc | **modified** — phase 2 marked done, phase 3/4 scope restated |

Also correct, in `Docs/Wolfmed/WOLFMED_PLAN.md`: the stale "`FractureEffectsSystem.cs:34-35` gets a `// WOLFGATE`
using swap" note (P2-D3) and the stale GUARD E2 line number `:274` → `:280`.

---

## 5. Duplicate directed subscription audit

RT stores **one** registration per `(component, event)` pair for the whole bus, not per system
(`EntityEventBus.Directed.cs:407,418-419`).

### 5.1 Every pair phase 2 registers

| # | Component | Event | Registrant | Existing WG owner of that pair | Free? |
|---|---|---|---|---|---|
| 1 | `WoundHostComponent` | `RefreshMovementSpeedModifiersEvent` | `FractureEffectSystem:29` | none | **yes** |
| 2 | `WoundHostComponent` | `GetManipulationDurationMultiplierEvent` | `FractureEffectSystem:30` | none — the event is **declared** at `WoundEvents.cs:116` (`public record struct GetManipulationDurationMultiplierEvent(EntityUid? Used, float Multiplier = 1f);`) and is **neither raised nor subscribed anywhere in WG today**; its only raiser is `FractureEffectSystem.GetDurationMultiplier`, which does not exist yet (CRITIQUE2 m7, accepted — the old wording understated how dead it is) | **yes** |
| 3 | `WoundFractureComponent` | `FractureGradeChangedEvent` | `FractureEffectSystem:31` | none | **yes** |
| 4 | `WoundFractureComponent` | `FractureTreatmentChangedEvent` | `FractureEffectSystem:32` | none | **yes** |
| 5 | `WoundFractureComponent` | `WoundRemovedEvent` | `FractureEffectSystem:33` | none — `WoundFractureComponent` holds only `WoundChangedEvent` (`WoundFractureSystem.cs:24`); `WoundRemovedEvent` is held on *other* components (`WoundableComponent` `WoundStatusEffectSystem.cs:34`, `WoundBleedingComponent` `WoundBleedingSystem.cs:44`, `WoundInternalBleedingComponent` `WoundInternalBleedingSystem.cs:22`) | **yes** |
| 6 | `WoundableComponent` | `OrganGotInsertedEvent` | `FractureEffectSystem:34` | none (WP5's `WolfmedBodyPartLifecycleSystem` **raises**, does not subscribe) | **yes** |
| 7 | `WoundableComponent` | `OrganGotRemovedEvent` | `FractureEffectSystem:35` | none | **yes** |
| 8 | `WoundableComponent` | `BodyPartFunctionalityChangedEvent` | `FractureEffectSystem:36` | none | **yes** |
| 9 | `WoundHostComponent` | `GetDoAfterDelayMultiplierEvent` | `WolfmedFractureDoAfterSystem` (§2.2) | the event is held by `DoAfterDelayMultiplierComponent` (`_Goobstation/DoAfter/DoAfterDelayMultiplierSystem.cs:13`) and `BodyComponent` (`_Shitmed/Body/Systems/SharedBodySystem.Relay.cs:11`) — **different components** | **yes** |
| 10 | `HighPainThresholdComponent` | `ModifyPainGainEvent` | `HighPainThresholdSystem` | none — `ModifyPainGainEvent` is raised twice (`PainSystem.cs:114`, `:239`) and has zero subscribers anywhere in WG | **yes** |

**Reference — `WoundHostComponent`'s currently claimed events** (so a future WP can check without re-grepping):
`MapInitEvent` and `RejuvenateEvent` (`WoundDamageProjectionSystem.cs:33-34`), `BeforeDamageChangedEvent` and
`DamageDealtEvent` (`WoundDamageRoutingSystem.cs:64-65`), `SleepStateChangedEvent` (`WoundBleedingSystem.cs:45`),
`ResolveHealingPartEvent` (`WoundHealingSystem.cs:29`), `BodyPartAddedEvent`/`BodyPartRemovedEvent`
(`WolfmedBodyPartLifecycleSystem.cs:22-23`), `ComponentInit` (`WolfmedWoundHostExclusionSystem.cs:17`).

**Reference — `WoundableComponent`'s currently claimed events:** `ComponentInit` and `RejuvenateEvent`
(`WoundSystem.cs:33,36`), `DamageChangedEvent` (`WoundDamageProjectionSystem.cs:38`),
`BeforeDamageChangedEvent` (`WoundDamageRoutingSystem.cs:66`), `PartDamageAppliedEvent`
(`OrganDamageSystem.cs:31`), `Wound{Created,Changed,StateChanged,Removed}Event`
(`WoundStatusEffectSystem.cs:31-34`).

### 5.2 Pairs phase 2 deliberately does **not** register

| Pair | Why not |
|---|---|
| `<EmoteOnDamageComponent, DamageChangedEvent>` | **Already owned** by `Content.Server/Chat/Systems/EmoteOnDamageSystem.cs:22`. The pain path is reached from that handler via HOOK 18. A second subscription is a server-start crash. |
| `<PainComponent, AfterAutoHandleStateEvent>` | Free (the only `PainComponent` subscription in WG is `PainSystem.cs:43`, `RejuvenateEvent`; `:42` is `<PainShockTargetComponent, ComponentStartup>` — line numbers re-verified), but the P2-D6 `Draw()` design needs no trigger. Named here only so the rejected client-system variant is documented. |
| `<PainNumbnessStatusEffectComponent, StatusEffectRelayedEvent<BeforeForceSayEvent>>` | Not registered. It would be a *different* pair from WG's existing `<PainNumbnessComponent, BeforeForceSayEvent>` (`PainNumbnessSystem.cs:16`), so both could coexist — but **beware**: `Content.Shared/Bed/Sleep/SleepingSystem.cs:70` orders `after: [typeof(PainNumbnessSystem)]`, so any new handler must live inside that same class or the ordering silently stops applying. |
| `<HealOnBuckleComponent, ComponentStartup>`, `<WoundHostComponent, BodyPartAddedEvent/RemovedEvent>`, `<OrganComponent, OrganAddedToBodyEvent/RemovedFromBodyEvent>` | Already registered by phase 1. Do not re-register. |

### 5.3 Component-registration names

`HighPainThreshold` is the **only** new registered component name phase 2 adds. `grep -rn "HighPainThreshold"`
across WG C#, YAML and FTL → **zero hits** (verified). Free.
`PartStatusSystem`/`PartDamageSeverity` are a static class + enum, not components — `grep -rn "PartDamageSeverity\|class PartStatusSystem"`
across `Content.{Shared,Server,Client}` → zero hits. Free.

### 5.4 Ordering constraints

- `FractureEffectSystem` subscribes two distinct event types (`FractureGradeChangedEvent`,
  `FractureTreatmentChangedEvent`) to two `OnChanged` **overloads**, and two more to two `OnPartChanged`
  overloads. Resolution is by event type — nothing to change.
- Both fracture-grade/treatment events are raised on **the part and the wound** (`WoundFractureSystem.cs:168-169`,
  `:182-183`), which is why the `<WoundFractureComponent, …>` pairs work.
- `WoundRemovedEvent` is raised on both part and wound (`WoundSystem.cs:368-370`); the handler reads `args.Part`,
  correct for a wound-scoped subscription.
- `WolfmedFractureDoAfterSystem` must not declare `before:`/`after:` against `DoAfterDelayMultiplierSystem` —
  the multipliers compose multiplicatively, so order is irrelevant, and an unnecessary ordering edge is a
  future crash surface.

---

## 6. Test plan

**Location:** `WG/Content.IntegrationTests/Tests/_Onyx/Wounds/` (ported) and
`WG/Content.IntegrationTests/Tests/_WF/Wolfmed/` (new).
**All literals are predictions until measured (P2-D16 / P2-4).**

### 6.1 Standing traps (apply to every test)

1. **`TerminatingOrDeleted`.** `RecursiveDeleteEntity` detaches every part while a mob terminates;
   `EnsureComp<PartDamageVisualsComponent>` / `EnsureComp<BodyPartFunctionalityComponent>` throw
   `DebugAssertException` on a terminating entity. `WolfmedBodyPartLifecycleSystem.cs:29,50` already guards —
   **any new WP10 code reacting to `BodyPartAdded/Removed` or `OrganGot*` must repeat it.** A single unguarded
   spawn/dispose poisons the shared pooled pair for every other test (WP9 lost 13 unrelated tests to this).
   Symptom: `Skipped — Test was dirty-disposed.` with no assertion text on a test that never touched wounds.
2. **`DockTest` first.** `db.ef` sqlite warnings fail every pair test in this repo (project memory). If
   `TestDockingConfig`/`TestPlanetDock` also fail, the environment is the cause.
3. **`Assert.Multiple` bodies must be fully synchronous** — no `async`/`await` inside the lambda, or assertions
   silently never run and the test reports green.
4. **`[TestPrototypes]` ids are pool-global.** Taken today: `WoundFoundationBody*`, `WoundBleedingBody*`,
   `WoundHealingBody*`, `WoundScarBody*`, `WoundFractureBody*`/`WoundFractureArmor`, `WolfmedBridgeBody*`.
   New ids below are chosen clear of those; re-check if another phase-2 WP lands prototypes first.
5. **YAML lints in Release only.**
6. **`dotnet build Content.IntegrationTests -c DebugOpt` is a required checkpoint** alongside Server/Client.

### 6.2 Test table

| # | Test | File | Setup | Key assertion | Depends on |
|---|---|---|---|---|---|
| **T-AP** | `ArmorPenetrationReachesWoundHostsTest` | `_WF/…/WolfmedDamageBridgeTest.cs` | Three `WolfmedBridgeBody`s: armoured@AP=0, armoured@AP=1, unarmoured@AP=0. New `WolfmedBridgeArmor` prototype (`Clothing slots: [outerClothing]`, `Armor modifiers: coefficients: {Blunt: 0.5}`), mirroring `WoundFractureArmor`. One `TryChangeDamage(body, {Blunt:10}, targetPart: TargetBodyPart.LeftArm, armorPenetration: X)` each. | armoured AP=0 arm **5**; armoured AP=1 arm **10**; unarmoured arm **10**; AP=1 > AP=0. Derivation: `DamageSpecifier.PenetrateArmor` (`DamageSpecifier.cs:306-330`) returns the set unchanged at `penetration == 0` and a **new empty** set at `>= 1f` (`:314-315`); `ApplyModifierSet` (`:133-163`) leaves a type unmodified when the set has no entry (`:157`) — so an empty set is a true no-op, not a zero-out. Exercises `WolfmedPartArmorSystem.OnPartDamageModify` (`_WF/Wolfmed/Armor/WolfmedPartArmorSystem.cs:28-32`), the sole subscriber of `<ArmorComponent, InventoryRelayedEvent<PartDamageModifyEvent>>`. **Gate for D23/WP8.** | **none** — write first |
| **T-PASSIVE-A** | `RealWoundHostPassiveDamageIsNeutralisedTest` | same | Real `MobHuman`; assert `Comp<PassiveDamageComponent>(body).Damage.Empty`; `routing.TryApplyPartDamage(body, leftArm, {Blunt:10})`; advance 60 s | arm still exactly 10 Blunt. Also re-verifies D21/D32 wiring (that `MobHuman` really is a wound host). **Gate for D29.** | none |
| **T-PASSIVE-B** | `PassiveDamageMechanismStillRoutesIfReenabledTest` | same | New `WolfmedPassiveWoundHost` (WoundHost + real `PassiveDamage` `Blunt: -5`, `damageCap: 0`) and `WolfmedPassiveControl` (identical minus `WoundHost`); damage both; advance 2 s | control heals (< 10); wound host does **not**. This is a **canary, framed as documenting an unguarded mechanism**, not proof of safety: PLAN's literal wording is near-tautological against the shipped YAML (`damage: {}` short-circuits `TryChangeDamage` at `DamageableSystem.cs:213-216` *before* the `WoundHostComponent` check), so only this fixture proves the routing would double-heal if the YAML guard were removed. | none |
| **T-FIXTURE** | fixture extension (P2-D21) — **prerequisite of T-FRACT-EFFECTS, not an option under T-FRACT-HANDS** | `_Onyx/…/WoundFractureTest.cs` `:22-58` | Add a `left hand` slot to `WoundFractureBodyGraph` and `- type: Hands` to `WoundFractureBody`: <pre>slots:<br>  torso: { part: TorsoHuman, connections: [left arm, left leg] }<br>  left arm: { part: LeftArmHuman, connections: [left hand] }<br>  left hand: { part: LeftHandHuman }<br>  left leg: { part: LeftLegHuman }</pre> and `- type: Hands` on `WoundFractureBody`. **Exactly one hand** — see T-FRACT-HANDS for why the right side goes on a separate id. Also add `WoundFractureHandsBodyGraph`/`WoundFractureHandsBody` (both arms, both hands, `Hands`, `WoundHost`, `Damageable`, `MovementSpeedModifier`) for T-FRACT-HANDS | `GetActiveHand((body, Comp<HandsComponent>(body)))` is non-null after spawn. Without this, `GetDurationMultiplier` is `1f` for "no hand found" and T-FRACT-HANDS's `Comp<HandsComponent>` guard throws. `LeftHandHuman`/`RightArmHuman`/`RightHandHuman` exist (`Body/Parts/human.yml:72,63,83`) | **none** — land it with WP10-1 |
| **T-FRACT-EFFECTS** | `EffectsRefreshOnTreatmentHealingAndDetachTest` (ported) | `_Onyx/…/WoundFractureTest.cs` — replaces the `:116-117` skip marker | **`WoundFractureBody` as extended by T-FIXTURE** (torso + left arm + **left hand** + left leg, `MovementSpeedModifier`, `WoundHost`, **`Hands`**). 75 Blunt to the leg, then 75 to the arm, then `TryMend`, then detach the leg. **Assert the active hand is non-null *before* the multiplier assertion** | **walk 0.5** after the leg fracture (Onyx's stale literal is `0.4f`); **manipulation 0.75** after the arm fracture (Onyx's stale literal is `2f`) — with the hand present the arithmetic holds: fractured left arm Comminuted → `manipulationModifier 0.75`, `Arm` absent from `PartEffectScales` (`WoundDamageComponents.cs:81-92` declares only `Leg 0.5`, `Foot 0.5`, `Hand 0.75`) → `1 + (0.75-1)·1·1 = 0.75`, while the undamaged left hand contributes `1 + (1-1)·0.75·1 = 1` (its `GetEffect` falls through to `BodyPartFunctionalitySystem.GetState`, which returns `Functional` with the cvar off — `BodyPartFunctionalitySystem.cs:23-24`); product **0.75**; **1f** after `TryMend` (`removeWoundWhenMended: true` → wound removed → fallback `Functional`); **walk 1f** after detach. Derivations in P2-D16 — **put each in a `// WOLFGATE` comment at its assertion**. Adaptations: `graph.TryDetachPart(leg)` → `entities.System<WolfmedBodySystem>().TryDetachPart(leg)` (precedent `WoundScarTest.cs:83`, `BodyConsequencesTest.cs`); `entityManager.System<FractureEffectSystem>()` (class name, not file name); add `using Content.Shared.Movement.Components;` | WP10-1 |
| **T-FRACT-HANDS** | hand-symmetry guard + assertion | same file | **Guard assertion first:** `GetActiveHand((body, Comp<HandsComponent>(body)))` is non-null and belongs to the fixture's arm — otherwise the multiplier test silently measures "no hand found" (`1f`) and masquerades as "fractures don't affect manipulation". **Then:** fracture the *left* arm, hold an item in a *right* hand, assert `GetDurationMultiplier(body, item) == 1f`. Needs a **second body id**, `WoundFractureHandsBody`, on its own graph with `left arm`→`left hand` **and** `right arm`→`right hand` plus `- type: Hands`. **Do not add the right side to `WoundFractureBody`:** T-FRACT-EFFECTS calls `GetDurationMultiplier(body)` with `used: null`, which resolves the **active** hand, and `AddHand` makes whichever hand attaches first active (`SharedHandsSystem.cs:59-60`) — body-graph slot order is not something a test should depend on. One hand on `WoundFractureBody` makes that unambiguous; T-FRACT-HANDS drives the `used` branch (`_hands.IsHolding`), which does not consult the active hand at all | proves the P2-D2 rewrite preserved Onyx's semantics. **Onyx has no such test** — this is new coverage for the one method we rewrote, and is the highest-value new assertion in phase 2 | WP10-1 |
| **T-FRACT-ALERT** | `FractureAlertTracksGradeAndTreatmentTest` | same file | **P2-D23 — deterministic path only.** `WoundFractureBody`; assert `IsShowingAlert(body, "BrokenBones")` is false; `routing.TryApplyPartDamage(body, leg, Spec(60))` → effective trauma 60 → Comminuted, `creationChance: 1`, severity 60 (`severityMultiplier: 1`); assert true; `fractures.TryMend(fracture.Owner)` | false → true → false. Mend clears it two ways at once (`alertHiddenTreatments: [Mended]` and `removeWoundWhenMended: true`), both handled by `FractureAlertSystem.Refresh`'s `!profile.AlertHiddenTreatments.Contains(...)` test. `AlertsSystem.IsShowingAlert` is at `AlertsSystem.cs:39`. **Cannot pass with `FractureAlertSystem` alone** — `Refresh` has no caller but `FractureEffectSystem`'s handlers (P2-D19). **Never create the fracture with a 35 hit**: `Simple.creationChance` is `0.25`, so that version fails three runs in four | WP10-1 |
| **T-FRACT-ALERT-NEG** | `FractureAlertRespectsMinimumGradeTest` | same file | **P2-D23 — reach Hairline by healing, not by hitting.** Fresh body; 60 Blunt to the leg (Comminuted, chance 1) → alert true; `_wounds.ChangeSeverity(fracture.Owner, -25)` → severity 35 → `OnWoundChanged` re-grades to **Simple** with no roll → alert still true; `ChangeSeverity(..., -10)` → severity 25 → **Hairline** → alert **false**. Optionally a second body damaged to 19 (below `Hairline`'s threshold 20), where `GetGrade` returns `None` and `WoundFractureSystem.cs:52-53` returns before the roll — deterministic, but it only proves "no fracture, no alert" | true → true → false. This is the only construction that actually tests `alertMinimumGrade: Simple` rather than "a fracture exists". The old "leg to 25" version had a 5 % chance of a Hairline appearing and was merely misleading rather than flaky | WP10-1 |
| **T-PAIN-OVERLAY** | `PainOverlayLevelTracksPainTest` | `_WF/…/WolfmedPainTest.cs` (new) | Real `MobHuman`; drive pain to known values | `min(1, PainSystem.GetPain(body) / PainComponent.SoftPainCap)` matches the expectation, and floors to 0 below pain 6.75. **Replaces P2-2's impossible pain-alert assertion** (P2-D5). Server-side and headlessly computable; the vignette itself is not | WP10-3 (nothing strictly, but run after) |
| **T-PAIN-SHOCK** | `PainShockStunsAtThresholdTest` | same | **Once WP10-5 lands, use real `MobHuman`.** Interim (write now): bespoke `WolfmedPainShockBody` — **P2-D24: `WoundHost` + `PainShockTarget` + `- type: StatusEffects` with an explicit `allowed: [Stun, KnockedDown, Jitter]` + `- type: MobState`** (plus `Body`, `Damageable`, `MovementSpeedModifier`). A default `StatusEffects` with an empty `allowed` list makes `TryParalyze` silently no-op, because neither `Stun` nor `KnockedDown` is `alwaysAllowed` (`status_effects.yml:4-10`); and without `MobStateComponent` the entity is never visited at all, since `PainSystem.Update`'s shock loop is `EntityQueryEnumerator<PainComponent, MobStateComponent, PainShockTargetComponent>` (`PainSystem.cs:143`). Either omission fails the test for a reason unrelated to pain. Push `GetPain(body)` past 130 — **recompute, do not assume a single hit's number**; pain is summed across the whole body and `DamageMultipliers["Blunt"] = 0.87` | `StunnedComponent` present; `PainShockTargetComponent.Armed == false`; `AdrenalineEnds != null`; then `GetPain` drops by the ×0.7 adrenaline factor. First end-to-end exercise of WP9's open item 1 | WP10-5 for the real-mob version |
| **T-HIGH-PAIN** | `HighPainThresholdReducesWoundPainGainTest` | same | Control `WolfmedBridgeBody` vs new `WolfmedHighPainThresholdBody` (`- type: HighPainThreshold`); 10 Blunt to each head via `routing.TryApplyPartDamage` | control `GetRawPain(head)` **8.7** (matches the existing `WoundDamageFoundationTest.cs:508` baseline); traited **6.525** (`8.7 × 0.75`); traited < control. Also a canary that `ModifyPainGainEvent`'s default is still `1` after the WP9 fix. **Cheapest, blocker-free test in the set — write it first among 6b** | WP10-3 |
| **T-PAIN-NUMB** | `PainNumbnessSuppressesWoundPainTest` | same | Mob with `- type: PainNumbness` (the legacy component) | `GetPain == 0` and no pain-shock emote. **Gate for P2-D8** — without the widening this test fails, which is precisely the point | WP10-3 |
| **T-CYBER-FALLBACK** | *(optional, P2-D14)* | `_Onyx/…/WoundFractureTest.cs` | A part with `CyberneticsComponent.Disabled` | mobility `0f` / manipulation `2.5f` through `GetEffect`'s fallback branch, proving the cvar gate does not make that branch inert | WP10-1 |

### 6.3 Run commands

```
dotnet build Content.Server/Content.Server.csproj -c DebugOpt -v q -nologo
dotnet build Content.Client/Content.Client.csproj -c DebugOpt -v q -nologo
dotnet build Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt -v q -nologo
dotnet run --project Content.YAMLLinter -c Release
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt --no-build --filter "FullyQualifiedName~DockTest"
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt --no-build \
  --filter "FullyQualifiedName~_Onyx.Wounds|FullyQualifiedName~Wolfmed" --logger "console;verbosity=detailed"
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt --no-build \
  --filter "FullyQualifiedName~PrototypeSaveTest|FullyQualifiedName~EntityTest"
```

---

## 7. Manifest rows to add / change

**Row changes (existing rows):**

| Manifest line | Change |
|---|---|
| `:34` `Resources/Prototypes/Entities/StatusEffects/movement.yml … deferred … WP10` | → **`skipped`**, note: "P2-D1 — no consumer at the pin; `MovementModStatusEffect` would also drag in the undocumented `FrictionStatusEffectComponent`" |
| `:35` `Resources/Prototypes/_Onyx/StatusEffects/wounds.yml … deferred … WP10` | → **`skipped`**, note: "P2-D1 — `StatusEffectBurnSlowdown`/`StatusEffectWoundImpairment` orphaned repo-wide at the pin" |
| `:77` `{AmputationSystem,FractureEffectsSystem,FractureAlertSystem}.cs … skipped … WP10/WP11` | **Split into three rows**: `AmputationSystem.cs` stays `skipped / WP11`; `FractureEffectsSystem.cs` → `modified / WP10-1`; `FractureAlertSystem.cs` → `modified / WP10-1` |
| `:89` BloodstreamSystem hook row | append "**GUARD E2 landed in WP10-2 at `:280`** (the line drifted from PLAN's `:274`)" |
| `:105` `wounds.ftl … plus 4 added` | append "**the 4 `wound-examine-fracture-*` keys were DELETED in WP10-2** when `_Onyx/medical/health-examinable.ftl` landed (P2-D12)" |
| `:129` `WoundFractureTest.cs … adapted … WP9` | → "3 of Onyx's 3 + 3 new (alert, alert-negative, hand-symmetry); fixture extended with a left hand + `Hands` and a second `WoundFractureHandsBody` (P2-D21); literals re-derived per P2-D16; alert tests made deterministic per P2-D23" |
| `:356-360` deviation block for the WP7 locale stopgap | mark **resolved** in WP10-2 |

**New rows:**

| Onyx path | Wolfgate path | Status | WP | Notes |
|---|---|---|---|---|
| `Content.Shared/_Onyx/Wounds/FractureAlertSystem.cs` | same | modified | WP10-1 | 3 `// WOLFGATE` edits: D8 `FractureProfile` redirect + dependency + using. No `Initialize`, no subscriptions. Uses `ShowAlert`/`ClearAlert` only, never `UpdateAlert` |
| `Content.Shared/_Onyx/Wounds/FractureEffectsSystem.cs` | same | modified | WP10-1 | 1 `// WOLFGATE` edit: `TryGetUsedHandSymmetry` replaced (P2-D2). 8 subscriptions, all free. `RefreshTransferredPart` kept verbatim and intentionally uncalled (P2-D18) |
| — | `Content.Shared/_WF/Wolfmed/DoAfter/WolfmedFractureDoAfterSystem.cs` | new | WP10-1 | P2-D4. `<WoundHostComponent, GetDoAfterDelayMultiplierEvent>`, `ref` handler |
| `Content.Shared/_Onyx/HealthExaminable/HealthExaminableSystem.PartStatus.cs` | same | modified | WP10-2 | **4** edits: `using Content.Shared.Damage.Components;` → `Content.Shared.Damage` (CS0246 otherwise), **add** `using Content.Shared._WF.Wolfmed.Compat;`, **add** the D12 facade `[Dependency]` (an addition — Onyx declares it in the base partial), D9 `PartOrder` fold |
| `Content.Shared/_Onyx/HealthExaminable/HealthExaminableSystem.Pain.cs` | same | verbatim | WP10-2 | self-only pain visibility is Onyx's deliberate anti-metagame design |
| `Content.Client/_Onyx/HealthExaminable/ExamineSystem.PartStatus.cs` | same | verbatim | WP10-2 | attaches to WG's `ExamineSystem` partial with no indirection |
| `Content.Client/_Onyx/HealthExaminable/PartStatusTag.cs` | same | verbatim | WP10-2 | `[partstatus]` markup needs no engine tag registration |
| — | `Content.Shared/_WF/Wolfmed/Compat/PartStatusSeverity.cs` | new | WP10-2 | P2-D11. Declares `namespace Content.Shared._Onyx.Targeting` deliberately; fills the D10-excluded `PartStatusSystem.GetSeverity`/`PartDamageSeverity` gap |
| — | `Content.Shared/HealthExaminable/HealthExaminableSystem.cs` | new (hook) | WP10-2 | **GUARD F**, 5 sites. Onyx's `GetAllDamage` swap deliberately skipped (no-op for non-hosts) so the file needs no facade dependency |
| — | `Content.Client/Examine/ExamineSystem.cs` | new (hook) | WP10-2 | **HOOK 14**, 2 sites (`:202` width 400→560; `:279-281` part-status wrap) |
| `Resources/Locale/en-US/_Onyx/medical/health-examinable.ftl` | same | verbatim | WP10-2 | **36** message ids (file read in full and counted: 4 `health-examinable-pain-*`, 2 `-part-title-*`, 2 `-part-summary*`, 2 `-part-chat-line*`, `-part-injuries`, 7 `-part-damage-*`, 5 `-part-severity-*`, 2 `-part-wound-*`, `-part-incision-open`, `-part-bleeding`, `-part-scars`, 4 `wound-examine-fracture-*`, 4 `wound-examine-frame-*`). PLAN2's earlier "43" was the manifest's `wounds.ftl` figure; CRITIQUE2 m4's "45" is also wrong. The 4 `wound-examine-frame-*` keys have no consumer at the pin — harmless dead weight, not a collision; the 4 `wound-examine-fracture-*` keys **are** the collision P2-D12 deletes from `wounds.ftl` |
| `Resources/Locale/en-US/_Onyx/targeting/part-status.ftl` | — | skipped | WP10-2 | orphaned at the pin (its one key has zero consumers) |
| `Content.Shared/_Onyx/Traits/HighPainThresholdComponent.cs` | same | verbatim | WP10-3 | registers as `HighPainThreshold`; zero collision |
| `Content.Shared/_Onyx/Traits/HighPainThresholdSystem.cs` | same | verbatim | WP10-3 | `<HighPainThresholdComponent, ModifyPainGainEvent>`, free |
| `Resources/Prototypes/_Onyx/Traits/quirks.yml` | same | adapted | WP10-3 | `HighPainThreshold` only; `conflicts:` → `mutuallyExclusiveTraits:`; `cost:` dropped (P2-D15) |
| `Resources/Locale/en-US/_Onyx/traits/quirks.ftl` | same | adapted | WP10-3 | 2 keys only |
| — | `Content.Shared/_Onyx/Wounds/PainSystem.cs` | modified | WP10-3 | P2-D8 — `IsPainNumb` widened to the legacy `PainNumbnessComponent` (adds to WP9's 2 existing `// WOLFGATE` edits in this file) |
| — | `Content.Client/UserInterface/Systems/DamageOverlays/Overlays/DamageOverlay.cs` | modified (hook) | WP10-4 | **HOOK 15**, 2 one-line sites; body in the `_WF` partial |
| — | `Content.Client/UserInterface/Systems/DamageOverlays/DamageOverlayUiController.cs` | modified (hook) | WP10-4 | **HOOK 16**, 1 condition; predicate in the `_WF` partial |
| — | `Content.Client/_WF/Wolfmed/Overlays/DamageOverlay.Wolfmed.cs` | new | WP10-4 | HOOK 15's body — `TryApplyWolfmedPain()`; keeps `Content.Shared._Onyx.Wounds` / `FixedPoint` out of the upstream file |
| — | `Content.Client/_WF/Wolfmed/Overlays/DamageOverlayUiController.Wolfmed.cs` | new | WP10-4 | HOOK 16's `WolfmedPainOwnsVignette(EntityUid)` predicate |
| `Content.Server/_Onyx/Chat/EmoteOnDamageSystem.PainSounds.cs` | same | modified | WP10-5 | namespace stays `Content.Server.Chat.Systems` (D13 pattern); **4** edits: `using Content.Shared._WF.Wolfmed.Compat;`, D12 facade swap, the **P2-D22 `HasComp<WoundHostComponent>` gate**, and the `ProtoMan` swap (expected no-op at the pin). Registers no subscription |
| — | `Content.Server/Chat/EmoteOnDamageComponent.cs` | new (hook) | WP10-5 | **HOOK 17**, 4 additive datafields. `Emotes` untouched so `ZombieSystem`'s `AddEmote` path is unaffected |
| — | `Content.Server/Chat/Systems/EmoteOnDamageSystem.cs` | new (hook) | WP10-5 | **HOOK 18**, 1 line at `:27` |
| — | `Resources/Prototypes/Entities/Mobs/Species/base.yml` | modified | WP10-5 | P2-D7 `- type: PainShockTarget` + P2-D9 `- type: EmoteOnDamage` with the corrected key, both in the phase-1 `# WOLFGATE` block |
| — | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedPainTest.cs` | new | WP10-6b | T-PAIN-OVERLAY / T-PAIN-SHOCK / T-HIGH-PAIN / T-PAIN-NUMB |
| — | `Docs/Wolfmed/WOLFMED_PLAN2.md` | new | WP10-7 | P2-5 |

**Deviations to record:**

1. **P2-D13 — `OrganicFractureProfile.manipulationModifier` is inverted relative to the formula.** Ships at
   Onyx's 0.92/0.84/0.75/0.75 (below 1 ⇒ *faster* do-afters) against C# defaults of 1.1/1.25/1.5/2.0 and a
   fallback branch of 2.5/1.25. D4 keeps Onyx's data; escalated to the user.
2. **P2-D4 — `Used` is lost from the do-after multiplier.** The active hand decides symmetry instead of the
   held item's hand.
3. **P2-D4 — `MultiplyDelay == false` do-afters escape the fracture penalty.** 20+ call sites including
   `DefibrillatorSystem.cs:148`, `FoodSystem.cs:220`, `DrinkSystem.cs:232`, `_EE/Carrying/CarryingSystem.cs:276`,
   `_White/Standing/SharedLayingDownSystem.cs:144`, `RCDSystem.cs:230`, `SharedMechSystem.cs:569`. Onyx applies
   it unconditionally.
4. **P2-D9 — Onyx's `species_base.yml:125` `emotes:` key does not bind** (the field is `EmotesThreshold` →
   `emotesThreshold`); RT drops unknown mapping keys at read and raises `FieldNotFoundErrorNode` at validation,
   so Onyx's pain sounds are dead at the pin. Wolfgate corrects the key. Same class as WP9's
   `ModifyPainGainEvent` find.
5. **P2-D9 — `EmoteOnDamageComponent` is extended additively, not replaced.** Onyx swaps `Emotes` for
   `EmotesThreshold`; Wolfgate keeps both so `ZombieSystem`'s two `AddEmote(uid, "Scream")` call sites are
   unaffected.
6. **P2-D15 — `cost: 3` dropped** from the `HighPainThreshold` trait (Wolfgate's `Quirks` category declares no
   `maxTraitPoints`, so it is inert) and **`conflicts:` renamed** to `mutuallyExclusiveTraits:`.
7. **P2-D8 — `IsPainNumb` widened** beyond Onyx to honour the legacy `PainNumbnessComponent`, because Onyx's
   `StatusEffectPainNumbness` has no applier in Wolfgate.
8. **P2-D6 — the pain HUD is a `Draw()` read, not Onyx's networked shared overlay.** Behaviourally identical for
   `Alive`/`Critical`/`Dead`; structurally different, and a re-sync will not pick it up automatically.
9. **P2-D17 — the client never sees limb attach/detach movement refreshes** (server-only `OrganGot*` raise);
   `[AutoNetworkedField]` speed modifiers correct it within one state.
10. **P2-D14 — `wounds.body_part_functionality_enabled` is `false`, but the cybernetics `Disabled` path is still
    live** through `GetEffect`'s fallback branch.
11. **P2-D18 — `FractureEffectSystem.RefreshTransferredPart` is dead but deliberately kept** (its Onyx caller
    `SpeciesChangeEntityEffectSystem` is D7/D16-deferred).
12. **P2-D1 — the whole `MovementModStatusEffect`/`StatusEffectSlowdown`/`MobStandStatusEffectBase` surface is
    reclassified from "deferred" to "not needed".**
13. Carried forward from WP9: **`StunSystemOnyxCompat` maps onto `TryParalyze`, which re-triggers stun VFX on
    every call.** Phase 2 is the first build in which pain shock actually fires — watch for stun spam.
14. **WP10-1 gives Onyx's wound→status-effect limb-attach path its first caller in Wolfgate**
    (`FractureEffectSystem:64,71` → `WoundStatusEffectSystem.HandlePartInserted/HandlePartRemoved`, which today
    have no caller outside `RefreshPartWounds:133`). Inert at the pin because no wound prototype populates
    `WoundStatusEffectBehavior.StatusEffect` (P2-D1); revisit if one ever does.
15. **P2-D20 — GUARD F calls `AddPartStatusMarkup` only for wound hosts; Onyx calls it unconditionally.** The
    divergence exists because `HealthExaminableComponent` is on `BaseMob` in Wolfgate, so borgs, silicons,
    animals and D32-excluded Protogen would otherwise get the Onyx readout on top of the legacy text.
16. **HOOK 14 (a) widens every examine popup in the game from 400 px to 560 px**, not only wound hosts' — the one
    HOOK 14 site that is not behaviour-neutral for non-hosts. Onyx does the same.
17. **P2-D22 — Onyx's pain sounds are gated on `HasComp<WoundHostComponent>`; Onyx has no such check.** Required
    because D32 removes only `WoundHostComponent`, not the components added beside it on `BaseMobSpeciesOrganic`.
18. **P2-D15 / CRITIQUE2 m8 — `PainNumbness`'s trait tooltip will not mention `HighPainThreshold`.** Selection is
    blocked in both directions (`HumanoidProfileEditor.xaml.cs:998`
    `selProto.MutuallyExclusiveTraits.Contains(traitId) || thisProto.MutuallyExclusiveTraits.Contains(sel)`), but
    the tooltip enumeration at `:725-728` reads only `trait.MutuallyExclusiveTraits`, so the exclusion is
    invisible from the `PainNumbness` side. Accepted: adding the reciprocal entry would mean editing
    `Resources/Prototypes/Traits/disabilities.yml`, which §3 forbids.

---

## 8. Risks and genuine user decisions

### 8.1 Blockers

**None outstanding.** Five blocking defects have been found and fixed so far — two by the analyst reports, three
by CRITIQUE2 — and all five are resolved in this document. Nothing is left that requires an answer before work
can start; §8.2's nine items are choices, not obstacles.

**The three CRITIQUE2 blockers, all verified against source and all fixed here:** B1 (a CS0246 in
`HealthExaminableSystem.PartStatus.cs` from Onyx's `Content.Shared.Damage.Components` using — fixed in §3 and
§4/WP10-2, edit count 2 → 4); B2 (GUARD F would have given the Onyx part-status readout to every bodied
non-wound-host, a D2 breach — fixed by P2-D20); B3 (`WoundFractureBody` has no hands, so T-FRACT-EFFECTS would
have measured `1f` and T-FRACT-HANDS would have thrown — fixed by P2-D21). Full audit trail in the Revision
notes at the end of this document.

**The two items the analyst reports labelled "blocker"** both resolve to scope changes with concrete
replacements, already folded into this plan:

- **`pain-hud.md`'s "blocker for P2-2": there is no pain or shock alert anywhere in Onyx.** Resolved by
  **P2-D5** — the assertion is replaced by T-PAIN-OVERLAY plus T-PAIN-SHOCK. Independently confirmed by
  `tests.md` §0.1 and `statuses.md` §5.
- **`statuses.md`'s / `tests.md`'s "hands block won't compile".** Real, but fully solved: **P2-D2** gives the
  exact replacement, verified against WG's actual signatures, with a phase-1 precedent already in the tree at
  `WoundDamageRoutingSystem.cs:606-633`. Neither report's *diagnosis* of which WG overloads exist was accurate;
  `fractures.md`'s was, and is what this plan adopts.

### 8.2 Genuine user decisions — needed before the named WP starts

| # | Decision | Default if no answer | Needed by |
|---|---|---|---|
| **1** | **Fracture manipulation balance (P2-D13).** Ship Onyx's YAML (`0.92/0.84/0.75/0.75` — a shattered arm makes do-afters **25 % faster**) and record the deviation, **or** set the four values to the C# defaults (`1.1/1.25/1.5/2.0`) behind a `# WOLFGATE` balance comment so a broken arm slows you down as the formula, the C# defaults, the fallback branch and Onyx's own test literal all intend. | **Ship Onyx's YAML** (D4), record the deviation. | WP10-1 (only the YAML, so it can also be changed after) |
| **2** | **Do-after `Used` semantics (P2-D4).** Accept the active-hand approximation with zero upstream edits, **or** authorise a 2-line escalation: add `public EntityUid? Used;` to `GetDoAfterDelayMultiplierEvent` (`_Goobstation/DoAfter/DoAfterDelayMultiplierSystem.cs:33-38`) and `new GetDoAfterDelayMultiplierEvent { Used = args.Used }` at `SharedDoAfterSystem.cs:211`, giving Onyx-exact semantics. PLAN §3 currently forbids touching `SharedDoAfterSystem.cs`. | **Zero-edit version.** | WP10-1 |
| **3** | **Onyx's `EmoteOnDamage` pain sounds (P2-D9).** Port them with the key corrected — which means shipping a feature that **never ran in Onyx** (new behaviour, not a faithful port) — **or** strike the item and record that Onyx's own YAML does not bind. Note that the pain-shock `Scream` (`PainSystem.cs:297`) is separate and already shipped either way. | **Port with the key corrected**, recorded as a corrected-upstream-bug deviation, on WP9's `ModifyPainGainEvent` precedent. | WP10-5 |
| **4** | **Authorise GUARD F + HOOK 14** (`HealthExaminableSystem.cs`, 5 sites; `Content.Client/Examine/ExamineSystem.cs`, 2 sites) — neither was on PLAN §3's list; both exceed the "one or two lines" cap in aggregate, though not per site. Without them there is no part-status examine, and GUARD E2 would delete the "looks pale" line with nothing replacing it. | **Authorise** (already written into §3). | WP10-2 |
| **5** | **Authorise HOOK 15 + HOOK 16** (the two upstream client overlay files) — likewise not on PLAN §3's list. Without them there is **no pain HUD at all**; the only alternative is the rejected full shared-overlay refactor (§P2-D6). | **Authorise** (already written into §3). | WP10-4 |
| **6** | **Authorise HOOK 17 + HOOK 18** (4 additive datafields + 1 line on `EmoteOnDamage`), or take the `_WF`-twin fallback (`WolfmedPainEmoteComponent`/`System` on a free `<…, DamageChangedEvent>` pair — zero upstream edits, but ~60 duplicated lines that drift at the next re-sync). Moot if decision 3 is "strike". | **Authorise the additive version.** | WP10-5 |
| **7** | **`PartDamageVisualsComponent` is networked but has no consumer** — `WoundDamageProjectionSystem` `EnsureComp`s and networks it, and Onyx's only consumer (`Content.Client/Damage/DamageVisualsSystem.cs` + `_Onyx/Wounds/{brute,burn}_damage.rsi`) is unported. It is paying networking cost for nothing. Phase 2, phase 3, or never? | **Defer to phase 3**, note it in the manifest. | not blocking |
| **8** | **Should non-wound-hosts with bodies get the Onyx part-status readout?** P2-D20 scopes GUARD F's `AddPartStatusMarkup` to wound hosts, because giving it to borgs, NPC/EE silicons, animals and D32-excluded Protogen would breach D2 and would duplicate, not replace, their threshold text. If the team *wants* a part-by-part readout on silicons, that is its own scope item (it would also need `HealthExaminableComponent`'s `Thresholds` reconsidered for those entities). | **Wound hosts only** (P2-D20). | WP10-2 |
| **9** | **`WoundPrototype.HealingMultiplier` is 1 for every ported wound** — carried forward from WP9 open item 3, which PLAN2 previously dropped (CRITIQUE2 m5, accepted). Any topical therefore heals a wound's severity one-for-one with the damage it heals; Onyx's own tests imply `0.15` was intended. A D4 tuning item: fix now, or leave for the balance pass? | **Leave for the balance pass** and keep it on the record; it is not a phase-2 gate. | not blocking |

### 8.3 Risks that will silently ship a broken build if ignored

1. **The `wounds.ftl` deletion is a removal, not an addition** (P2-D12). Nobody will be looking at that file
   while adding `health-examinable.ftl`. Forgetting it is a Fluent duplicate-id crash at startup. **The WP10-2
   task description must name `Resources/Locale/en-US/_Onyx/prototypes/wounds/wounds.ftl:46-52` explicitly.**
2. **`base.yml` has one owner.** If two work packages edit the phase-1 `# WOLFGATE` block concurrently, one
   silently loses. WP10-5 owns it.
3. **Every Onyx test literal in phase 2's scope is stale** (P2-D16). Copying `0.4f`/`2f` produces two failing
   assertions whose real cause is Onyx's superseded profile, not Wolfgate's port — exactly the trap WP9
   documented in its §3.2.
4. **`FractureAlertSystem` without `FractureEffectSystem` is dead code; `FractureEffectSystem` without
   `FractureAlertSystem` is `CS0246`** (P2-D19). Never split them.
5. **A second `<EmoteOnDamageComponent, DamageChangedEvent>` subscription is a server-start crash.** The pain
   path must be *called from* the existing handler (HOOK 18), never subscribed independently.
6. **Do not weaken `WolfmedBodyPartLifecycleSystem`'s `TerminatingOrDeleted` guards.**
   `FractureEffectSystem.OnPartChanged` reaches `EnsureComp<BodyPartFunctionalityComponent>` through them;
   that is the `DebugAssertException` class of bug that cost WP9 13 unrelated test failures.
7. **P2-D24 — `- type: StatusEffects` with an explicit `allowed: [Stun, KnockedDown, Jitter]` *and*
   `- type: MobState` are both required on any bespoke pain fixture.** `Stun`/`KnockedDown` are not
   `alwaysAllowed`, so a default `StatusEffects` makes `TryParalyze` silently no-op; and without
   `MobStateComponent` the entity never enters `PainSystem.Update`'s shock query at all. Either omission fails
   T-PAIN-SHOCK for a reason unrelated to pain.
8. **P2-D23 — fracture creation is a dice roll.** Any test that creates a fracture with a Hairline (5 %),
   Simple (25 %) or Displaced (65 %) hit is flaky by construction. Create at ≥ 60 (`creationChance: 1`), then
   re-grade with `WoundSystem.ChangeSeverity`, which runs through `OnWoundChanged` with no roll.
9. **P2-D21 — `WoundFractureBody` has no hands**, so `GetDurationMultiplier` returns `1f` and the whole
    manipulation half of T-FRACT-EFFECTS measures nothing. Extend the fixture *before* writing the test.
10. **P2-D20 — GUARD F's `AddPartStatusMarkup` must be in the `else`.** Copying Onyx's unconditional placement
    compiles, lints and runs; it just quietly gives borgs and Protogen the Onyx readout.
11. **Five F1 work packages are told to append to one manifest.** Serialisation rule 2 (§4) redirects them to
    `manifest-rows-WP10-N.md`; WP10-7 merges. Without it, four of the five appends are lost.
12. **Phase 2 is the first build in which pain, pain shock and the pain HUD have ever run on a real mob**
   (WP9 open item 1). Treat every balance number as unvalidated, including the ones in this document.


### 8.4 Deferred, explicitly out of scope

| Item | Reason |
|---|---|
| `MovementModStatusEffectComponent`, `MovementModStatusSystem`, `FrictionStatusEffectComponent`, `movement.yml`'s `StatusEffectSlowdown` chain, `_Onyx/StatusEffects/wounds.yml`, `MobStandStatusEffectBase`, a `KnockdownImmune` tag | **No consumer at the pin** (P2-D1). If a later phase wants taser/flash/vomit slowdowns, that is a self-contained port, not a wound dependency. |
| `Resources/Locale/en-US/_Onyx/targeting/part-status.ftl` | Orphaned — its one key has zero consumers at the pin (P2-D12). |
| `StatusEffectPainNumbness`, `PainNumbnessStatusEffectBase`, `TraitStatusEffectPainNumbness`, `TraitStatusEffectBase`, `CloneableStatusEffect` | Need `!type:ModifyStatusEffect` (a StatusEffectNew entity effect Wolfgate lacks) and `TraitPrototype.specials:` (which Wolfgate lacks). Phase 4, with the narcotics. |
| Onyx's `Content.Shared/DamageOverlay/**` + `Content.Client/DamageOverlay/**` shared refactor | P2-D6. Revisit only if Wolfgate takes the upstream refactor for its own reasons. |
| Onyx's Targeting-doll `PartStatus`/`PartStatusComponent` | D10. Only `GetSeverity`/`PartDamageSeverity` are shimmed. |
| `PartDamageVisualsComponent` consumers (`Content.Client/Damage/DamageVisualsSystem.cs`, `_Onyx/Wounds/{brute,burn}_damage.rsi`) | Per-limb damage sprites — its own package (§8.2 item 7). |
| Health-analyzer pain readout (`health-analyzer-wound-pain`, `…-short`) | PLAN §6.1 → WP11. |
| Guidebook (`Resources/Locale/en-US/_Onyx/guidebook/wounds.ftl`, 90+ lines) | Unscoped; cheap and high-value once the systems are live. |
| A client-side `OrganGot*` mirror | P2-D17 — optional follow-up, not required for correctness. |
| `WoundPrototype.HealingMultiplier = 1` on every ported wound (WP9 open item 3) | A D4 balance-tuning item, not a phase-2 gate. Restored here after CRITIQUE2 m5: WP9's other four open items were all carried into this plan and this one was silently dropped. Also raised as §8.2 item 9 so the user can pull it forward. |
| `AmputationSystem` and everything phase 3/4 | D26, D7, D16. |

---

## 9. Work-package table (final)

| WP | Title | Group | Model | Files |
|---|---|---|---|---|
| **WP10-1** | Fractures — `FractureEffectSystem` + `FractureAlertSystem` + the do-after bridge + the T-FIXTURE prototype extension | F1 | **opus** | 4 |
| **WP10-2** | HealthExaminable part status + pain examine + GUARD E2 / GUARD F + HOOK 14 | F1 | **opus** | 10 |
| **WP10-3** | `HighPainThreshold` trait + `IsPainNumb` widening | F1 | **sonnet** | 5 |
| **WP10-4** | Pain HUD overlay (HOOK 15 + HOOK 16 + two `_WF` partials) | F1 | **sonnet** | 4 |
| **WP10-6a** | Bridge tests — T-AP, T-PASSIVE-A, T-PASSIVE-B | F1 | **sonnet** | 1 |
| **WP10-5** | Pain sounds (HOOK 17 / HOOK 18 + `PainSounds.cs`) + mob wiring; **owns `base.yml`** | F2 | **opus** | 4 |
| **WP10-6b** | Fracture + pain tests | F3 | **opus** | 2 |
| **WP10-7** | Docs + manifest merge; **sole owner of `Docs/Wolfmed/`** | F4 | **sonnet** | 3 |

File counts cover new `_WF` files, modified vendored files, upstream hook files, prototypes, locale and tests.
They exclude `manifest-rows-WP10-N.md`, which every WP writes (serialisation rule 2).
WP10-1's 4 = `FractureAlertSystem.cs`, `FractureEffectsSystem.cs`, `WolfmedFractureDoAfterSystem.cs`,
`WoundFractureTest.cs` (T-FIXTURE's prototype block only — WP10-6b writes the tests).
WP10-4's 4 = two upstream hook files + two `_WF` partials.

**Model rationale.** Opus for WP10-1 (the hands rewrite, eight subscriptions, the D8 redirect), WP10-2 (ten
files, a five-site restructuring of an upstream method, a new namespace-borrowing shim and a locale deletion
that is a removal nobody will be looking for), WP10-5 (a partial class in a foreign namespace, an additive
component extension that must not disturb `ZombieSystem`, and the only `base.yml` edit) and WP10-6b (every
literal is a prediction and three of the tests fail for non-Wolfmed reasons if the fixture is wrong). Sonnet
for the rest: WP10-3 is two verbatim files plus a five-line trait prototype and a two-line widening; WP10-4 is
three short edits once §4 spells out the partials; WP10-6a is one test file with no phase-2 dependency;
WP10-7 is a merge.

---

## Revision notes (CRITIQUE2 pass)

This revision applies `C:/Users/jzo12/Documents/Wolfmed/plan/p2/CRITIQUE2.md`. Every finding was re-derived from the real trees
before being accepted; nothing was taken on CRITIQUE2's word, and one of its numbers turned out to be wrong.

### Blockers — all three accepted, all three independently verified

| # | Verdict | What changed |
|---|---|---|
| **B1** | **Accepted, and widened.** ONYX `HealthExaminableSystem.PartStatus.cs:6` is `using Content.Shared.Damage.Components;` and the body uses `DamageableComponent` at `:41`; WG puts that type in `namespace Content.Shared.Damage` (`Content.Shared/Damage/Components/DamageableComponent.cs:9,21`) while `Content.Shared.Damage.Components` still exists as a namespace — so the `using` compiles and the type does not: CS0246. Phase 1's `WoundFractureSystem.cs:4` is the precedent. CRITIQUE2 named three missing `using`s across two files; re-reading both files raised the edit counts from "2" and "1-2" to **4 and 4**, and confirmed m2 — the `_damageable` line is an *addition*, not a swap, because Onyx declares it in the base partial at `HealthExaminableSystem.cs:15` and WG's base file has only `_examineSystem` at `:12`. §3, §4/WP10-2 and §7 updated. |
| **B2** | **Accepted in full → new decision P2-D20.** Confirmed by reading ONYX `HealthExaminableSystem.cs:53-115`: the `</Onyx-PartHealthExamine-edited>` tag closes at `:105` and `AddPartStatusMarkup(uid, examiner, msg);` is a separate tagged block at `:107-109`, outside the wound-host wrap. Confirmed the Wolfgate exposure independently: `HealthExaminable` is on `BaseMob` (`Entities/Mobs/base.yml:109`) and `Entities/Mobs/NPCs/animals.yml:603,966,1051` carry `- type: Body`, so animals alone make this a D2 breach before borgs or D32-excluded Protogen are counted. GUARD F (e) is now the `else` of the wrap; recorded as §7 deviation 15 and escalated as §8.2 item 8. |
| **B3** | **Accepted in full → new decision P2-D21.** Confirmed the fixture (`WoundFractureTest.cs:22-47`) has no `Hands` and no hand slot; that `HandsSystem.cs:116-136` only creates a hand for an enabled `BodyPartType.Hand` whose parent is enabled; that `AddHand` sets `ActiveHand` when none is set (`SharedHandsSystem.cs:59-60`); and that Onyx's own fixture (`ONYX WoundFractureTest.cs:22-67`) has no hands either — a second, independent reason its `2f` literal could never have passed. **Extended beyond CRITIQUE2:** its fix puts both arms and both hands on the one graph, but T-FRACT-EFFECTS calls `GetDurationMultiplier(body)` with `used: null`, which resolves the *active* hand — with two hands that depends on body-graph slot order. `WoundFractureBody` therefore gets exactly **one** hand; T-FRACT-HANDS gets its own `WoundFractureHandsBody`, and its `used`-item branch goes through `_hands.IsHolding`, which never consults the active hand. |

### Majors — all five accepted

| # | Verdict | What changed |
|---|---|---|
| **M1** | **Accepted → P2-D22.** Verified `WolfmedWoundHostExclusionSystem.cs:12,17,33` removes only `WoundHostComponent`, and that ONYX `EmoteOnDamageSystem.PainSounds.cs:19-62` never tests it. Took CRITIQUE2's fix (b), the one-line guard at the top of `HandlePainDamageEmote`: `using Content.Shared._Onyx.Wounds;` is already line 1 of that file, so it costs nothing, and it is the honest gate for a feature shipping as "Wolfmed pain sounds". P2-D7's rationale is rewritten in §4/WP10-5 and in the `base.yml` comment; `PainShockTarget` on Protogen is separately shown to be inert. |
| **M2** | **Accepted → P2-D23.** Verified the roll at `WoundFractureSystem.cs:51-55` and the profile's `0.05 / 0.25 / 0.65 / 1` (`wounds.yml:56-76`), and verified the deterministic alternative: `OnWoundChanged:73-86` re-grades with no roll, and `severityMultiplier: 1` (`wounds.yml:39`) makes `WoundSystem.ChangeSeverity` (`:284`) an exact grade dial. Both alert tests respecified: create at 60 (Comminuted, chance 1), then walk the grade down. The new negative actually tests `alertMinimumGrade: Simple` instead of "a fracture exists". |
| **M3** | **Accepted and extended → P2-D24.** Verified `status_effects.yml:4-10` (no `alwaysAllowed` on `Stun`/`KnockedDown`), `base.yml:125-127` (BaseMobSpecies lists them explicitly) and `SharedStunSystem.cs:244-251`. **Added a second requirement CRITIQUE2 missed:** `PainSystem.Update`'s shock loop is `EntityQueryEnumerator<PainComponent, MobStateComponent, PainShockTargetComponent>` (`PainSystem.cs:143`), so the fixture also needs `- type: MobState` or it is never visited at all. |
| **M4** | **Accepted.** Serialisation rule 2 added to §4: WP10-7 is the sole editor of `Docs/Wolfmed/WOLFMED_MANIFEST.md`, `WOLFMED_PLAN.md` and `WOLFMED_STATUS.md`; every other WP emits `C:/Users/jzo12/Documents/Wolfmed/plan/p2/manifest-rows-WP10-N.md` for WP10-7 to merge. Ground rule 6 is amended for phase 2, and §8.3 carries the risk. |
| **M5** | **Accepted.** Verified `[Dependency] private WoundStatusEffectSystem _statusEffects` at ONYX `FractureEffectsSystem.cs:23` with calls at `:64` and `:71`, and that both methods exist in WG (`WoundStatusEffectSystem.cs:109,119`) with no caller outside the system's own `RefreshPartWounds:133`. Added to §2's "already exists" list, to WP10-1's exact-edit section, to §7's manifest row, and as §7 deviation 14 with the P2-D1 dependency stated. |

### Minors

| # | Verdict |
|---|---|
| **m1** | **Accepted, solved differently.** The gap is real — `DamageOverlay.cs:1-7` imports none of `Content.Shared._Onyx.Wounds` or `Content.Shared.FixedPoint`. Rather than add them upstream, HOOK 15's body moves into `Content.Client/_WF/Wolfmed/Overlays/DamageOverlay.Wolfmed.cs`: `DamageOverlay` is `partial` (`:11`), as is `DamageOverlayUiController` (`:17`). This also brings both hooks back inside DECISIONS.md's phase-2 rule that upstream hooks are one or two lines with bodies in `_WF` partials, and removes HOOK 16's new `using` as well. |
| **m2** | **Accepted**, folded into B1: the D12 dependency is an addition, not a swap. |
| **m3** | **Accepted** — recorded as §7 deviation 16 and noted inline on HOOK 14 (a). |
| **m4** | **Half rejected, with evidence.** The count *is* wrong, but not to 45: `Resources/Locale/en-US/_Onyx/medical/health-examinable.ftl` was read in full and declares **36** message ids (itemised in §7). PLAN2's "43" came from the manifest's `wounds.ftl` row; CRITIQUE2's "45" is unsupported by the file. §4/WP10-2 and §7 now say 36. |
| **m5** | **Accepted** — WP9 open item 3 (`HealingMultiplier = 1` on every ported wound) restored to §8.4 and raised as §8.2 item 9. |
| **m6** | **Accepted** — stated in §3's HOOK 16 row and in §4/WP10-4: WP10-4 is only correct once WP10-3's `IsPainNumb` widening lands; ship them together, WP10-3 first if they must be split. |
| **m7** | **Accepted** — §5.1 row 2 now reads "declared at `WoundEvents.cs:116`, neither raised nor subscribed in WG today". |
| **m8** | **Accepted** — verified both sites (`HumanoidProfileEditor.xaml.cs:998` bidirectional, `:725-728` one-directional) and recorded as §7 deviation 18. `Resources/Prototypes/Traits/disabilities.yml` stays untouched, per §3. |

### Corrections and additions of my own, beyond CRITIQUE2

1. **`health-examinable.ftl` has 36 message ids**, not 43 and not 45 (see m4).
2. **`- type: MobState` is a second hard requirement of the bespoke pain fixture** (P2-D24) — missed by all five analyst reports *and* by CRITIQUE2.
3. **T-FRACT-HANDS needs its own body prototype**, because a two-handed `WoundFractureBody` makes T-FRACT-EFFECTS' active hand depend on slot order (see B3).
4. **`_body` in `HealthExaminableSystem.PartStatus.cs` needs no edit** — Onyx declares it in `HealthExaminableSystem.Pain.cs:8`, which WP10-2 ports verbatim. That makes files 1 and 2 inseparable, which §4/WP10-2 now states explicitly.
5. **`_damageable.GetTotalDamage(uid)` in `PainSounds.cs` compiles unchanged** against the facade's `Entity<DamageableComponent?>` overload (`WolfmedDamageableSystem.cs:134`) via RT's implicit `EntityUid` conversion (`RobustToolbox/Robust.Shared/GameObjects/Entity.cs:39`). PLAN2 previously prescribed an unnecessary rewrite of that call site.
6. **Three types that look like D13 relocation hazards are not:** `WoundComponent`, `WoundBleedingComponent` and `WoundScarComponent` are all still in `Content.Shared` (`WoundDamageComponents.cs:181,210,259`), so the shared `PartStatus.cs` may reference them.
7. **`SurgicalIncisionWound` exists** (`Resources/Prototypes/_Onyx/Wounds/wounds.yml:370`) despite D7 skipping Onyx surgery, and `PartStatus.cs` only ever compares it by id — so that `ProtoId` constant is safe either way.
8. **`PainSystem.cs:42` is `<PainShockTargetComponent, ComponentStartup>`, not the `RejuvenateEvent` subscription** (`:43`) — §5.2 corrected.
9. **`severityMultiplier: 1`** (`wounds.yml:39`) is what makes P2-D23's `ChangeSeverity` arithmetic exact; stated so an implementer can check it rather than trust it.
10. **§9, the final work-package table**, added with parallel groups, model recommendations and file counts.

### Claims re-verified and deliberately left standing

P2-D2's hands rewrite (`SharedHandsSystem.cs:177,280,285,306`; `HandsComponent.cs:107,113,156-161`); P2-D3's
`OnyxBodyEvents` shape; the whole §5.1 subscription audit, re-grepped for all ten pairs — zero subscribers for
`GetManipulationDurationMultiplierEvent`, `FractureGradeChangedEvent`, `FractureTreatmentChangedEvent`,
`OrganGotInsertedEvent`, `OrganGotRemovedEvent`, `BodyPartFunctionalityChangedEvent` and `ModifyPainGainEvent`;
three for `WoundRemovedEvent`, none on `WoundFractureComponent`; two for `GetDoAfterDelayMultiplierEvent`, both
on other components; and none of the 24 `RefreshMovementSpeedModifiersEvent` subscribers is on
`WoundHostComponent`. Also standing: the legality of a `ref` handler on the class event
(`DoAfterDelayMultiplierSystem.cs:27` against the by-value raise at `SharedDoAfterSystem.cs:208-214`); P2-D12's
locale collision (`wounds.ftl:46-52`); P2-D9's dead `emotes:` key (ONYX `EmoteOnDamageComponent.cs:25` versus
`species_base.yml:124-133`); GUARD E2's site (`BloodstreamSystem.cs:280`); GUARD F's line numbers; WP10-1
needing zero prototypes, locale or textures; the client sandbox being clear (`Content.Client/GlobalUsings.cs:10`
covers `Vector2Helpers`; `ScrollContainer.ReturnMeasure` at RT `:73`; `FormattedMessage.FromMarkupPermissive` /
`RemoveMarkupPermissive` / `EscapeStringParameter` at RT `:118,126,159,139`); `HighPainThreshold` being
collision-free with `Quirks` carrying no `maxTraitPoints` (`Traits/categories.yml:10-12`) and `PainNumbness`
present at `Traits/disabilities.yml:69`; P2-D8's placement (`PainSystem.cs:324-331`); P2-D14
(`BodyPartFunctionalitySystem.cs:19-24` — the cybernetics `Disabled` return sits above the cvar gate); and the
HUD numbers (`SoftPainCap = 135` at `WoundDamageComponents.cs:137`, `PainShockThreshold = 130` at
`PainSystem.cs:32`). None of these were changed.
