# WOLFMED — implementation plan (lead architect)

**Onyx pin:** `2f5bab9946539cbe083010c9ae6fbc59b47ae377`, sparse reference at `C:/Users/jzo12/Documents/Wolfmed/onyx` (**ONYX** below).
**Wolfgate worktree:** `C:/Users/jzo12/Documents/GitHub/Wolfgate/.claude/worktrees/rules-motd-updates-11c89c` (**WG** below), branch `clanker/wolfmed-port-orchestration-454c3d`, RobustToolbox 277 junctioned at `WG/RobustToolbox`.

**Revision 2** (this document) folds in the completeness critique (`CRITIQUE.md`). Every critique item was re-verified against the primary sources before acceptance; two were rejected or re-scoped with evidence, and one *new* blocker-class error in revision 1 was found during that verification (D20 — see §1.3). Changes are listed in §9.

This document supersedes the per-area analyst reports wherever they disagree. Where a report is overruled, the reason is given inline. Agents follow this file literally; the analyst reports are evidence, not instructions.

**Ground rules for every agent:**

1. Vendored Onyx code keeps its Onyx path under `_Onyx/`, its Onyx license header, and its Onyx namespace. Every in-file change is marked `// WOLFGATE` with a one-line reason.
2. New Wolfgate code lives under `_WF/Wolfmed`. `_WF` style: no license header, `/// <summary>` one-liners, `[Dependency] private X _x = default!;` (no `readonly`).
3. Upstream Wolfgate files get one- or two-line `// WOLFGATE` hooks only, except where this plan explicitly authorises more (there are exactly four such places: §3 GUARD D, §3 GUARD D2, §3 GUARD E3, and HOOK 7/HOOK 8's `HealingComponent`/`HealingSystem` pair).
4. **Never** add a directed subscription for a `(Component, Event)` pair without checking §5 first. RT throws `Duplicate Subscriptions for comp=…, event=…` at `WG/RobustToolbox/Robust.Shared/GameObjects/EntityEventBus.Directed.cs:407,419` — a server-start crash. §5 now carries **answers**, not deferred questions.
5. Every WP ends with the build checkpoint: **`dotnet build Content.Server` and `dotnet build Content.Client` must both be green (0 errors)**, plus `dotnet build Content.IntegrationTests` from WP9 onward. YAML must lint in **Release**.
6. Record every file you touch in `Docs/Wolfmed/WOLFMED_MANIFEST.md` (§7) in the same commit.

---

## 1. Decisions

### 1.1 Binding decisions restated (unchanged)

| ID | Decision | One-line rationale | Evidence |
|---|---|---|---|
| **D1** | Port `Content.Shared/StatusEffectNew` verbatim from Onyx's copy of the upstream Wizden system; the old `Content.Shared.StatusEffect` stays and keeps serving existing content. | Keeps `_Onyx` wound files verbatim and is upstream code Monolith may inherit; the old system cannot express permanent effects or per-part effects. | `statuseffectnew.md` §7 (11/13 files byte-identical); `wounds-c.md` §8.3-A |
| **D2** | For entities with `WoundHostComponent`, Onyx's `WoundDamageRoutingSystem` owns part damage. Shitmed's spreading, sever-at-130 and part regen are bypassed for those entities with minimal `// WOLFGATE` guards. Entities without `WoundHostComponent` behave exactly as today. | One damage owner per entity; no double application. | `damage-bridge.md` §5 (guards A/B/C/E), §4 |
| **D3** | Phase 1 species: organic humanoids only. IPC / cybernetic / slime / plant profiles are later phases. | Scope control; Onyx wires IPC per-mob, not by inheritance. | `mob-wiring.md` §1.6 |
| **D4** | Balance: Onyx defaults. Tuning later via CCVars and prototypes. | Avoids re-deriving a tuned system during a port. | — |
| **D5** | Missing APIs get a compat layer in `Content.Shared/_WF/Wolfmed/Compat`; where a shim is impossible, a `// WOLFGATE` edit inside the vendored file. Upstream systems get one- or two-line hooks. | Keeps the vendored tree re-syncable. | `rt-api-gap.md` §3.1; this plan §2 |
| **D6** | Layout: vendored Onyx code in `Content.{Shared,Server,Client}/_Onyx/...`, `Resources/{Prototypes,Locale/en-US,Textures}/_Onyx/...` keeping Onyx's relative paths. Wolfgate glue in `_WF/Wolfmed`. Docs and manifest in `Docs/Wolfmed/`. | A re-sync becomes a diff. | `Docs/Wolfmed/` already exists |
| **D7** | Phase 1 keeps Wolfgate's Shitmed surgery. Onyx's own surgery system is NOT ported; wound surgeries are re-expressed on Shitmed's step system in phase 4. | Onyx surgery is 25 shared files + 70 prototypes and collides on two component names. | `medical-extras.md` §7b |
| **D8** | Stay on Wolfgate's Shitmed `BodyPartComponent`. Onyx's extra part fields go in a separate `_WF/Wolfmed` component. Onyx `_Onyx/Body` Nubody glue is not ported; only organ-damage and functional-organ pieces the wounds need. | Onyx's `BodyPartComponent.cs` *redeclares* four `Content.Shared.Body.Part` types — four `CS0101` collisions. | `body-organ.md` §0.1; `mob-wiring.md` §4.2 |

### 1.2 New decisions (D9–D26, as revised)

| ID | Decision | Rationale (one line) | Evidence report |
|---|---|---|---|
| **D9** | **Do not add `Chest`/`Groin` to `BodyPartType`.** Map `BodyPartType.Chest` → `BodyPartType.Torso` and delete every **`BodyPartType.Groin`** and **`HumanoidVisualLayers.Groin`** case, in vendored files and in ported YAML. **`TargetBodyPart.Groin` is NOT deleted** — it exists in Wolfgate (`WG/Content.Shared/_Shitmed/Targeting/TargetBodyPart.cs:16`, and is inside `TargetBodyPart.All` at `:30`), real Wolfgate code passes it, and `WoundTargetResolver` must fold it to the torso part exactly as `ConvertTargetBodyPart` already does (`WG/Content.Shared/_Shitmed/Body/Systems/SharedBodySystem.Targeting.cs:395`). `TargetBodyPart.Chest` does not exist and becomes `.Torso`. **Compensating balance edit:** set `[BodyPartType.Torso] = 4f` in `WoundHostComponent.TargetWeights` (Onyx has `Chest 2.5 + Groin 1.5`; folding without this silently drops the torso's share of random routing from 4.0/13.0 = 31 % to 2.5/11.5 = 22 %). | Every Wolfgate body part prototype declares `partType: Torso`, so a `Chest = 8` member would be a value no body ever carries and every Onyx `PartType == Chest` test would silently never match. `BodyPartType` is `{Other, Torso, Head, Arm, Hand, Leg, Foot, Tail}` (`WG/Content.Shared/Body/Part/BodyPartType.cs:11-19`) — verified. | `targeting.md` §3, `wounds-c.md` §5.3-C, `body-organ.md` §2.2. **Overrules** `wounds-a.md` §1.3-E and `wounds-b.md` §0.4(b). Groin/weight scoping per `CRITIQUE.md` m2/m3, verified. |
| **D10** | **Do not vendor Onyx's Targeting stack.** Skip `_Onyx/Targeting/{TargetBodyPart,TargetingComponent,SharedTargetingSystem,TargetResolverSystem}.cs`, all `Content.Server/_Onyx/Targeting`, all `Content.Client/_Onyx/Targeting`, and `PartStatus*`. Vendor only `DamageDistribution.cs`, `TargetingSnapshotComponent.cs`, `TargetingSnapshotSystem.cs`, `CCVars.Targeting.cs`. Wound code uses Shitmed's `TargetBodyPart` and a new `_WF/Wolfmed/Targeting/WoundTargetResolver`. | Onyx's `TargetingComponent` registers as `"Targeting"`, the exact name Shitmed's already uses → `ComponentFactory` throws at server start; and `TargetResolverSystem` switches on `BodyPartType.Chest`/`Groin`, forbidden by D9. | `hooks-a.md` finding 1; `targeting.md` §6; `mob-wiring.md` §4.2 |
| **D11** | **Split the two `DamageDealtEvent` subscriptions.** Routing keeps a new **pre-write** `DamageDealtEvent` (raised by GUARD D inside `TryChangeDamage`); `WoundDamageProjectionSystem` re-points to Wolfgate's existing **post-write** `DamageChangedEvent` on `WoundableComponent`. | Onyx can hang both off one event because its `DamageableSystem` is itself a subscriber; Wolfgate writes inline, so one raise point cannot serve both orderings. | `wounds-a.md` §7, `damage-bridge.md` §5.5 |
| **D12** | **The new-style damage API lives on a distinctly-named facade, `WolfmedDamageableSystem`** (`_WF/Wolfmed/Compat`), not as a partial on `DamageableSystem` and not as extension methods. Each vendored file that used `[Dependency] DamageableSystem _damage` gets a one-line `// WOLFGATE` swap to the facade. | Makes silent binding to Wolfgate's legacy `TryChangeDamage(EntityUid?, …)` impossible by construction. | `wounds-a.md` §1.3-T, `wounds-c.md` §0.8, `damage-bridge.md` §7.1 |
| **D13** | **Bloodstream stays server-only.** `WoundBleedingSystem`, `WoundInternalBleedingSystem`, `OrganDamageSystem` and `WoundHealingSystem` are vendored into `Content.Server/_Onyx/Wounds/` with their Onyx namespaces unchanged. | All four are already `_net.IsServer`-gated in every mutating path. | `wounds-b.md` §0.1 + §4.3, `wounds-c.md` §6.5, `circulation.md` §8 |
| **D14** | **Keep Wolfgate's server-only `HealingComponent`.** Add four `// WOLFGATE` `[DataField]`s to it (`HealDamage`, `HealWounds`, `TreatmentCapabilities`, `AllowedWoundStages`). Do **not** create a parallel shared healing component. **The two collection fields must be declared `HashSet<TreatmentCapability>` and `HashSet<string>?`**, not `List<>` — see D31. | D13 puts `WoundHealingSystem` in `Content.Server`, so the server component is directly reachable. | `wounds-b.md` §3.3, `medical-extras.md` §5(b) |
| **D15** | **`CirculatoryStreamSystem` goes to `Content.Server/_Onyx/Chemistry/Circulation/`**, trimmed to `GetPartStream` + the primary-stream branches of `TryGetPartSolution`/`TryGetStreamSolution`/`SetBleedRates`. All five `SubscribeLocalEvent`s are dropped. `SharedSolutionContainerSystem.CirculatoryStreams.cs` is **not** ported. `MetabolismStage`/`MetabolitesStage` are commented out of the prototype. | `Organic` (the only stream at the pin) **is** the primary stream, so ~80 % of the file is unreachable in phase 1. | `circulation.md` §6, §9, §4, §5.4 |
| **D16** | **Entity effects are rewritten old-style, keeping the exact class names.** Author `SuppressPain`, `MendFractures`, `TakeStaminaDamage`, `StaminaDamageCondition` in `Content.Shared/_WF/Wolfmed/EntityEffects/` deriving from Wolfgate's `EntityEffect`/`EntityEffectCondition`. Onyx's `ReagentTreatmentEffects.cs` *also* adds a `TreatmentCapabilities` datafield to three existing effect partials — `HealthChange`, **`EvenHealthChange`** and **`DistributedHealthChange`** (ONYX `ReagentTreatmentEffects.cs:9,15,21`). `HealthChange` and `EvenHealthChange` both exist in Wolfgate and both get the field plus the HOOK 9 branch; `DistributedHealthChange` exists nowhere in Wolfgate and must be authored if any ported reagent uses that tag. | `!type:` resolves by bare `Type.Name` against any public subclass of the field's base type (`RobustToolbox/Robust.Shared/Reflection/ReflectionManager.cs:319-359`). | `entityeffects-gap.md` §3; `CRITIQUE.md` m4, verified (`WG/Content.Server/EntityEffects/Effects/EvenHealthChange.cs:16`) |
| **D17** | **`WoundSystem.cs:34`'s `SubscribeLocalEvent<BodyComponent, RejuvenateEvent>` is deleted.** Its handler body is exposed as a public method and called from `WoundDamageProjectionSystem.OnRejuvenate`, which already owns `<WoundHostComponent, RejuvenateEvent>`. | `WG/Content.Server/_Mono/Body/Systems/BodyRejuvenateSystem.cs:28` already owns `<BodyComponent, RejuvenateEvent>` → server-start crash. | `wounds-b.md` §1.4, `wounds-c.md` §15, `damage-bridge.md` §8.1 |
| **D18** | **No `WolfmedHostComponent` marker.** Upstream guards test `HasComp<WoundHostComponent>` directly, with a `// WOLFGATE`-marked `using Content.Shared._Onyx.Wounds;`. | One source of truth; `WoundHostComponent` is `[NetworkedComponent]` so client-side guards work. **Overrules** `damage-bridge.md` §5.0. | `mob-wiring.md` §4.1; `hooks-a.md` §4a/§7a |
| **D19** | **Do not shim `InjurableComponent`.** `// WOLFGATE`-delete the `EnsureComp<InjurableComponent>` block from `WoundDamageProjectionSystem.SetupPart` (keep `EnsureComp<DamageableComponent>`). | Wolfgate parts already carry `Damageable(damageContainer: OrganicPart)` statically from `WG/Resources/Prototypes/_Shitmed/Body/Parts/base.yml:6-8`. | `mob-wiring.md` §3, §0.2; `wounds-a.md` §3.3/P7 |
| **D20** | ~~Drop `Caustic` from the routed damage set.~~ **REVERSED in revision 2. `WoundHostComponent.LocalizedDamageTypes` ships at Onyx's default — `Blunt, Slash, Piercing, Heat, Cold, Shock, Caustic` — with no `localizedDamageTypes:` override on the YAML.** | Revision 1's rationale ("Wolfgate's part container `OrganicPart` has no Caustic") is **false**. `OrganicPart` declares `supportedGroups: [Brute, Burn]` (`WG/Resources/Prototypes/Damage/containers.yml:78-82`) and **`Caustic` is a member of the `Burn` group** (`WG/Resources/Prototypes/Damage/groups.yml:10-16`). `DamageableInit` seeds every type of every supported *group* (`WG/Content.Shared/Damage/Systems/DamageableSystem.cs:109-121`), so `Caustic` is present in every body part's `DamageDict` and `TryChangeDamage`'s skip-unknown-type branch (`:256-257`) never fires for it. Onyx's `OrganicBodyPartProfile.acceptedDamageTypes` lists `Caustic` (ONYX `wounds.yml:5-12`) and `BurnWound.damageTypes` has a `Caustic` entry (`wounds.yml:480-490`), so routing it gives acid burns a real limb wound. Dropping it would have silently deleted a whole damage type from the wound system for no reason. | Verified directly in revision 2. **Overrules** `mob-wiring.md` §0.5 and revision 1's D20. |
| **D21** | **`- type: WoundHost` lands on `BaseMobSpeciesOrganic`** (`WG/Resources/Prototypes/Entities/Mobs/Species/base.yml:247`) as one `// WOLFGATE` block, **plus an explicit exclusion list for non-organic descendants** (§8.1 item 1). | One line covers the organic species; per-species lines drift. But the descendant set is **18 direct children across five fork directories**, not nine — see §8.1. | `mob-wiring.md` §2.2, §2.3-A; descendant enumeration verified in revision 2 |
| **D22** | **Raise the gib threshold on `BaseMobSpeciesOrganic` only**, not on `MobDamageable`. Override the `Destructible` Blunt threshold from 400 to 1500 in the same `// WOLFGATE` block as D21. | Once the projection sums part damage back onto the mob, 400 total is reachable from routine limb damage. Confining it leaves animals/silicons/structures untouched. | `mob-wiring.md` §0.4; `WG/Resources/Prototypes/Entities/Mobs/base.yml:65-77` verified |
| **D23** | **Armour penetration, tool and origin flag are threaded through the routed pass.** Append `float ArmorPenetration = 0f, EntityUid? Tool = null` to `BeforeDamageChangedEvent` (`// WOLFGATE`, populated at its single construction site), stash them in a `// WOLFGATE` side table in `WoundDamageRoutingSystem.OnBeforeDamageChanged`, and pass them from `RouteThroughBodyModifiers`' `ChangeDamage` call through the facade's optional parameters. | Routing cancels at `DamageableSystem.cs:214-215`, *before* the resistance block at `:226`, so armour is only ever applied on the re-entrant pass. Without this, every Wolfgate AP weapon loses its AP against every humanoid. | `damage-bridge.md` §8.7, `hooks-b.md` risk 1 |
| **D24** | **Do not hook the individual combat call sites in phase 1** (`SharedProjectileSystem`, `HitscanBasicDamageSystem`, `SharedMeleeWeaponSystem`, `DamageOtherOnHitSystem`, `DamageOnInteractSystem`, `ExplosionSystem`). **Conditional on D27** — without D27 this decision ships a combat regression. | Wolfgate's seam is *inside* `TryChangeDamage`, which every one of those already calls. Six upstream files and six divergent fork shapes avoided. **Overrules** `hooks-b.md` S1/S2/S3/#3/#6 placement. Re-open only if a call site is found that bypasses `TryChangeDamage`. | `damage-bridge.md` §3, `hooks-b.md` S1–S3 |
| **D25** | **`SharedChatSystem` gets a `public virtual void TryEmoteWithChat(EntityUid, string, …)` in a `_WF` partial and `WG/Content.Server/Chat/Systems/ChatSystem.Emote.cs:60` gets a one-word `// WOLFGATE override`.** Return type stays `void`. `ChatSystem.Emote.cs:85` (the `EmotePrototype` overload) is **not** touched. | `PainSystem` is shared and must compile into `Content.Client`; the call is inside a server-gated path so the shared no-op never runs. `PainSystem.cs:297` passes a **string** emote id and discards the return value, so `bool` buys nothing. One upstream word, and `PainSystem.cs` stays byte-identical. Verified: `SharedChatSystem` is `public abstract partial class` (`SharedChatSystem.cs:13`), `ChatSystem : SharedChatSystem` (`ChatSystem.cs:52`), `ChatTransmitRange` is shared (`SharedChatSystem.cs:391`). | `wounds-c.md` §1.3-A(a); `CRITIQUE.md` m1, verified |
| **D26** | **Fractures ship in phase 1 (`WoundFractureSystem` only); amputation does not.** `// WOLFGATE`-disable **both** `OrganDamageSystem.cs:24` (`[Dependency] private AmputationSystem _amputation = default!;`) **and** `:36` (`_amputation.HandlePartDamageApplied(part, ref args);`) with a `TODO: phase 3` note. `AmputationSystem`, `FractureEffectsSystem`, `FractureAlertSystem` are phase 2/3. | Commenting out only the *call* leaves the `[Dependency]` field referencing an unported type → `CS0246`. `AmputationSystem` is referenced nowhere else in `_Onyx/Wounds/**` (verified by grep: only its own declaration at `AmputationSystem.cs:16` and these two lines). | `wounds-c.md` §6.4, §13; `body-organ.md` §2.6; `CRITIQUE.md` M3, verified |

### 1.3 Decisions added in revision 2 (D27–D31)

| ID | Decision | Rationale | Evidence |
|---|---|---|---|
| **D27** | **`BeforeDamageChangedEvent` gains a third `// WOLFGATE` member, `DamageSpecifier? Applied = null`, and `DamageableSystem.cs:215` returns `before.Applied` instead of `null`.** `WoundDamageRoutingSystem` writes the routed delta into it. Godmode and stasis leave it null, so their behaviour is bit-identical. | Onyx's router cancels unconditionally (`WoundDamageRoutingSystem.cs:55-62`) and Wolfgate returns `null` on cancel (`DamageableSystem.cs:214-215`). With D24 declining to hook the combat sites, **every** call site that reads `TryChangeDamage`'s return sees "no damage" for every humanoid. The damage is still *applied*; only the *report* is lost. Worst case is a genuine bug, not cosmetics: `WG/Content.Shared/Weapons/Hitscan/Systems/HitscanBasicDamageSystem.cs:33-34` has `if (damageDealt == null) return;` **inside** `foreach (var hitEntity in args.HitEntities)`, so a piercing hitscan that passes through one humanoid stops damaging **everything behind them**. Also lost: Blunt→stamina conversion (`SharedMeleeWeaponSystem.cs:586-590`), `LogType.MeleeHit` admin logs (`:592-604`), `DoDamageEffect` (`:609-612`), the projectile red-flash and hit log (`SharedProjectileSystem.cs:164-172`), `DamageUserOnTriggerSystem.cs:38`, `DamageOnAttackedSystem.cs:76-79`, `DamageOnInteractSystem.cs:77-80`. | All line numbers verified in revision 2. `CRITIQUE.md` B3; `hooks-b.md` §S1-S3 is the only analyst material that touched this. |
| **D28** | **`WolfmedBodyEventBridgeSystem` and `WolfmedPartLifecycleSystem` are one system, `WolfmedBodyPartLifecycleSystem`, and it subscribes `<WoundHostComponent, BodyPartAddedEvent>` / `<WoundHostComponent, BodyPartRemovedEvent>` — never `<BodyComponent, …>`.** It ships in **WP5**, not WP2 (it needs `WoundHostComponent` and `WoundDamageProjectionSystem`). | `<BodyComponent, BodyPartAddedEvent>` and `<BodyComponent, BodyPartRemovedEvent>` are **already owned** by `WG/Content.Shared/_Shitmed/Body/Systems/SharedBodySystem.PartAppearance.cs:25-26`. Re-registering either throws `Duplicate Subscriptions` at server start. Both events are raised on the **body only** (`WG/Content.Shared/Body/Systems/SharedBodySystem.Parts.cs:337-338`, `:357-358`), so "subscribe on the part instead" is not available; but the body carries `WoundHostComponent` too, RT keys on `(component, event)`, and scoping to wound hosts is exactly the desired behaviour. | Verified by grep in revision 2. `CRITIQUE.md` B2. If a *part*-scoped hook is ever wanted, Shitmed raises `BodyPartComponentsModifyEvent` **on the part** at `SharedBodySystem.Parts.cs:334` and `:352`. |
| **D29** | **`PassiveDamage` is removed from wound hosts** in the same `// WOLFGATE` block as D21/D22, i.e. `- type: PassiveDamage` with `damage: {}` (or `allowedStates: []`). Recorded in the manifest as a deliberate balance deviation; revisit after the first playtest. | `BaseMobSpeciesOrganic` carries `- type: PassiveDamage … damageCap: 20, Heat -0.07, Brute -0.07` (`WG/Resources/Prototypes/Entities/Mobs/Species/base.yml:250-262`) and `PassiveDamageSystem.Update` applies it through `_damageable.TryChangeDamage(uid, comp.Damage, true, false, damage)` (`WG/Content.Shared/Damage/Systems/PassiveDamageSystem.cs:50`) — i.e. straight into GUARD D. After the bridge that becomes a **per-tick routed heal** on every lightly-wounded humanoid: (a) double passive healing, because Onyx already models recovery per part profile via `passiveRecoveryMultiplier`/`bedRecoveryMultiplier` and `FilterPartDamage` explicitly branches on `HasComp<PassiveDamageComponent>(origin)` (ONYX `WoundDamageRoutingSystem.cs:949-951`); (b) `damageCap: 20` silently changes meaning, because it reads `DamageableComponent.TotalDamage`, which after the bridge is the projection (Σ parts + systemic); (c) a `DamageChangedEvent` on the body every tick for every wounded player, driving `MobThresholdSystem.OnDamaged`, alerts, NPC aggro and every other listener. | Verified in revision 2. `CRITIQUE.md` M5. Not covered by any analyst report. |
| **D30** | **The facade's `SetDamage` zeroes types missing from the argument instead of removing them.** One-line deviation from Onyx, recorded in §8.2. | Onyx prunes (`ONYX Content.Shared/Damage/Systems/DamageableSystem.API.cs:42-46`), but Wolfgate's write loop *skips* any type absent from the dict (`WG/Content.Shared/Damage/Systems/DamageableSystem.cs:256-257`) and `DamageableInit` deliberately seeds every supported type to zero (`:107-121`). `RefreshBodyDamage` calls `SetDamage(body, total)` with only the non-zero types (`ONYX WoundDamageProjectionSystem.cs:143-183`), so an undamaged wound host would end up with an **empty** `DamageDict`. Consequences: `DamagePerGroup` loses its zero rows (health-analyzer and crew-monitor readouts), and any future non-routed write to the body's own `DamageableComponent` becomes a silent no-op. Zeroing is behaviourally identical for every reader of `TotalDamage`/`DamagePerGroup` and preserves the seeding invariant. | Verified in revision 2. `CRITIQUE.md` M8. Same mechanism as §8.3 trap 1. |
| **D31** | **`ResolveHealingPartEvent`'s parameter types are NOT changed. Instead the one construction site converts.** `WoundHealingSystem.cs:110` becomes a `// WOLFGATE` line converting `List<string>?` → `List<ProtoId<DamageContainerPrototype>>?`; `ResolveHealingPart` (`:44`) and `IsCompatiblePart` (`:154`) keep Onyx's signatures. | Revision 1's WP6 #5 changed only the two *parameter* types to `IReadOnlyList<string>?`. That does not fix the construction at `WoundHealingSystem.cs:110-112` (still `List<string>` → `IReadOnlyList<ProtoId<…>>`, CS1503) **and it breaks `:33`**, which forwards `args.DamageContainers` — still `IReadOnlyList<ProtoId<…>>`, because the event is shipped **verbatim** as WP4 file #1 (`ONYX WoundEvents.cs:122-133`) — into the newly-`string` parameter. `WG/Content.Server/Medical/Components/HealingComponent.cs:39` is `public List<string>? DamageContainers;` — verified. Same construction also demands `IReadOnlySet<TreatmentCapability>` and `IReadOnlySet<string>?`, hence D14's `HashSet<>` requirement. | Verified in revision 2. `CRITIQUE.md` M2. |

---

## 2. Compat layer spec — `Content.Shared/_WF/Wolfmed/Compat/`

Every file below is new `_WF` code (no license header, one-line `/// <summary>`). C# namespace is independent of file path: several of these deliberately declare an upstream namespace while living under `_WF/Wolfmed/Compat` so the vendored files stay verbatim.

### 2.1 `WolfmedDamageableSystem.cs` — the new-style damage API facade (D12)

```csharp
namespace Content.Shared._WF.Wolfmed.Compat;

/// <summary>Onyx-shaped DamageableSystem API over Wolfgate's older one. Vendored _Onyx files depend on this, never on DamageableSystem directly.</summary>
public sealed class WolfmedDamageableSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    /// <summary>Pass-through of Wolfgate's global topical-healing multiplier.</summary>
    public float UniversalTopicalsHealModifier => _damage.UniversalTopicalsHealModifier;

    /// <summary>Applies damage through Wolfgate's pipeline and returns the applied delta (never null).</summary>
    public DamageSpecifier ChangeDamage(
        Entity<DamageableComponent?> ent,
        DamageSpecifier damage,
        bool ignoreResistances = false,
        bool interruptsDoAfters = true,
        EntityUid? origin = null,
        bool ignoreGlobalModifiers = false,
        float armorPenetration = 0f,                       // WOLFGATE-only, D23
        EntityUid? tool = null,                            // WOLFGATE-only, D23
        DamageableSystem.DamageOriginFlag? originFlag = null); // WOLFGATE-only, D23

    /// <summary>Bool-returning form with the applied delta. The only TryChangeDamage on this type.</summary>
    public bool TryChangeDamage(
        Entity<DamageableComponent?> ent,
        DamageSpecifier damage,
        out DamageSpecifier newDamage,
        bool ignoreResistances = false,
        bool interruptsDoAfters = true,
        EntityUid? origin = null,
        bool ignoreGlobalModifiers = false,
        float armorPenetration = 0f,
        EntityUid? tool = null,
        DamageableSystem.DamageOriginFlag? originFlag = null);

    /// <summary>Copy of the entity's damage, positive entries only.</summary>
    public DamageSpecifier GetPositiveDamage(Entity<DamageableComponent> ent);

    /// <summary>Copy of the entity's positive damage restricted to one damage group.</summary>
    public DamageSpecifier GetPositiveDamage(Entity<DamageableComponent> ent, ProtoId<DamageGroupPrototype> group);

    /// <summary>Copy of the entity's whole damage specifier.</summary>
    public DamageSpecifier GetAllDamage(Entity<DamageableComponent?> ent);

    /// <summary>Total damage currently on the entity.</summary>
    public FixedPoint2 GetTotalDamage(Entity<DamageableComponent?> ent);

    /// <summary>Replaces the entity's damage wholesale and reports a real delta to DamageChangedEvent.</summary>
    public void SetDamage(Entity<DamageableComponent?> ent, DamageSpecifier damage);

    /// <summary>Zeroes every damage type on the entity.</summary>
    public void ClearAllDamage(Entity<DamageableComponent?> ent);

    /// <summary>Whether the entity's damage container supports this damage type.</summary>
    public bool CanBeDamagedBy(Entity<DamageableComponent?> ent, ProtoId<DamageTypePrototype> type);

    /// <summary>Heals an equal share of the amount from every non-zero damage type (phase 4).</summary>
    public DamageSpecifier HealEvenly(Entity<DamageableComponent?> ent, FixedPoint2 amount,
        ProtoId<DamageGroupPrototype>? group = null, EntityUid? origin = null);

    /// <summary>Heals the amount distributed proportionally to existing damage (phase 4).</summary>
    public DamageSpecifier HealDistributed(Entity<DamageableComponent?> ent, FixedPoint2 amount,
        ProtoId<DamageGroupPrototype>? group = null, EntityUid? origin = null);
}
```

**Implementation notes that must not be glossed over:**

- `ChangeDamage` forwards to `_damage.TryChangeDamage(ent.Owner, damage, ignoreResistances, interruptsDoAfters, ent.Comp, origin, ignoreGlobalModifiers, armorPenetration, canSever: false, canEvade: false, partMultiplier: 1f, targetPart: null, tool: tool, originFlag: originFlag)` and returns `?? new DamageSpecifier()`. `canSever: false` is belt-and-braces on top of GUARD B.
- `TryChangeDamage` = `newDamage = ChangeDamage(...); return !newDamage.Empty;`.
- **`SetDamage` must not be `comp.Damage = damage`.** It must prune-or-zero and report a real delta, or eight Wolfgate systems go silent (§3 GUARD F, §8.3 trap 5). Body, with **D30**'s zeroing deviation:
  ```csharp
  var delta = damage - ent.Comp.Damage;
  delta.TrimZeros();                                    // DamageSpecifier.cs:194 — verified
  // WOLFGATE (D30): Onyx removes types missing from `damage`; Wolfgate zeroes them so
  // DamageableInit's container seeding survives and TryChangeDamage:256-257 keeps working.
  foreach (var type in ent.Comp.Damage.DamageDict.Keys.ToArray())
      if (!damage.DamageDict.ContainsKey(type))
          ent.Comp.Damage.DamageDict[type] = FixedPoint2.Zero;
  foreach (var (type, amount) in damage.DamageDict)
      ent.Comp.Damage.DamageDict[type] = amount;
  _damage.DamageChanged(ent.Owner, ent.Comp, delta.Empty ? null : delta, interruptsDoAfters: false);
  ```
  `DamageableSystem.DamageChanged(EntityUid, DamageableComponent, DamageSpecifier?, bool, EntityUid?, bool?, float)` is public at `WG/Content.Shared/Damage/Systems/DamageableSystem.cs:156-157` — verified.
- **`CanBeDamagedBy` is a deliberate re-basing, not a straight port.** Onyx's takes `Entity<InjurableComponent?>` and tests `SupportsType(ent.Comp.DamageContainer, type)` (`ONYX DamageableSystem.API.cs:455-464`). D19 drops `InjurableComponent`, so this version reads `DamageableComponent.DamageContainerID` and indexes the `DamageContainerPrototype`, mirroring the seeding loop at `DamageableSystem.cs:103-121` (types **and** every type of every supported group). A null container means "supports everything"; a missing `DamageableComponent` returns false, matching Onyx's `Resolve(..., false)`. **Consequence to understand before touching `SystemicDamageComponent`:** the test is now "is this type in the mob's `Biological` container" (`WG/Resources/Prototypes/Entities/Mobs/base.yml:68-69`), and `WoundDamageProjectionSystem.RefreshBodyDamage` **deletes** systemic types that fail it (`ONYX WoundDamageProjectionSystem.cs:150-159`). `Biological` = groups `Brute, Burn, Toxin, Airloss, Genetic` (`WG/Resources/Prototypes/Damage/containers.yml:1-8`), so `Structural`, `Holy` and anything else outside those groups is silently dropped from systemic damage on a wound host.
- `HealEvenly`/`HealDistributed` are **phase 4** — declare them throwing `NotImplementedException` in WP2 and fill them in when reagent treatment lands. Onyx's real signatures are `(Entity<DamageableComponent?>, FixedPoint2, ProtoId<DamageGroupPrototype>?, EntityUid?)` (`ONYX DamageableSystem.API.cs:177,248`), correcting `rt-api-gap.md` §3.1, which described the second parameter as a `DamageSpecifier`.

**Serves:** `WoundDamageRoutingSystem` (`:158,161,373,380,520,534,621,704,730,734,805,814,818,921,985`), `WoundDamageProjectionSystem` (`:56,133,155,171,183`), `WoundFractureSystem` (`:65`), `WoundHealingSystem` (`:65,117,118,135`), `AmputationSystem` (`:78,182`), `_Onyx/Mobs/Systems/MobThresholdSystem.cs` (`:27,42`), `HealthExaminableSystem.PartStatus.cs:44`.

**Per-file `// WOLFGATE` dependency swap** (one line each):
```csharp
[Dependency] private WolfmedDamageableSystem _damage = default!; // WOLFGATE: Onyx-shaped damage API; see _WF/Wolfmed/Compat
```
in: `WoundDamageRoutingSystem.cs`, `WoundDamageProjectionSystem.cs`, `WoundFractureSystem.cs`, `WoundHealingSystem.cs`, `AmputationSystem.cs`, and later `HealthExaminableSystem.PartStatus.cs`. **`_Onyx/Mobs/Systems/MobThresholdSystem.cs` is an *addition*, not a swap** — see WP5 #3.

### 2.2 `DamageSpecifier.Wolfmed.cs` — partial on `DamageSpecifier`

```csharp
namespace Content.Shared.Damage;

public sealed partial class DamageSpecifier
{
    /// <summary>Onyx-compatible deep copy; Wolfgate only has a copy constructor.</summary>
    public DamageSpecifier Clone() => new(this);

    /// <summary>Copy containing only entries with a value above zero.</summary>
    public static DamageSpecifier GetPositive(DamageSpecifier damageSpec);

    /// <summary>Copy containing only entries with a value below zero.</summary>
    public static DamageSpecifier GetNegative(DamageSpecifier damageSpec);
}
```
`DamageSpecifier` is `public sealed partial class` (`WG/Content.Shared/Damage/DamageSpecifier.cs:20`) — verified. Bodies of the two statics copy ONYX `DamageSpecifier.cs:194-205`. No name collision exists for any of the three. **Serves:** `WoundDamageRoutingSystem.cs:69,158,374,520,942`, `AmputationSystem.cs:182`, `WoundBleedingSystem.cs:95`.

### 2.3 `DamageDealtEvent.cs` — the routing seam event (D11, GUARD D)

```csharp
namespace Content.Shared.Damage.Systems;

/// <summary>Raised after modifiers and before damage is written. Clearing the dict suppresses the write.</summary>
[ByRefEvent]
public record struct DamageDealtEvent(
    DamageSpecifier Damage,
    EntityUid? Origin,
    bool InterruptsDoAfters);
```
Declared in `Content.Shared.Damage.Systems` so the vendored files' existing `using Content.Shared.Damage.Systems;` resolves it and so `DamageableSystem.cs` needs only one added `using`. Deliberately **not** `readonly` (Onyx's is). Recorded in §8.2.

### 2.4 `AlertsSystem.UpdateAlert.cs` — partial on `AlertsSystem`

```csharp
namespace Content.Shared.Alert;

/// <summary>Port of Onyx's AlertsSystem.UpdateAlert: keeps the cooldown bar's start time when the end time shrinks.</summary>
public abstract partial class AlertsSystem
{
    public void UpdateAlert(EntityUid euid,
        ProtoId<AlertPrototype> alertType,
        short? severity = null,
        TimeSpan? cooldown = null,
        bool autoRemove = false,
        bool showCooldown = true)
    {
        if (_timing.ApplyingState)
            return;
        if (!TryGet(alertType, out var alert))
            return;
        if (cooldown == null)
        {
            ShowAlert(euid, alertType, severity, null, autoRemove, showCooldown);
            return;
        }
        TryGetAlertState(euid, alert.AlertKey, out var alertState);
        var start = alertState.Cooldown is { } existing && existing.Item2 >= cooldown.Value
            ? existing.Item1
            : _timing.CurTime;
        ShowAlert(euid, alertType, severity, (start, cooldown.Value), autoRemove, showCooldown);
    }
}
```
A partial, not an extension, because it needs `_timing` (`AlertsSystem.cs:11`, private). `AlertsSystem` is `public abstract partial class` (`WG/Content.Shared/Alert/AlertsSystem.cs:9`) — verified. **`AlertState.Cooldown` in Wolfgate is an *unnamed* `(TimeSpan, TimeSpan)?`** (`Content.Shared/Alert/AlertState.cs:10`), so `.Item1`/`.Item2`, not Onyx's `startTime`/`endTime`. **Serves:** `StatusEffectAlertSystem.cs:29,34,39`.

### 2.5 `StatusEffectsSystem.Wolfgate.cs` — `ProtoMan` for RT 277

```csharp
namespace Content.Shared.StatusEffectNew;

/// <summary>RT 277 has no EntitySystem.ProtoMan; this supplies it so the vendored files stay verbatim.</summary>
public sealed partial class StatusEffectsSystem
{
    [Dependency] private IPrototypeManager ProtoMan = default!;
}
```
**Serves:** `StatusEffectsSystem.cs:141`. Delete this file if Wolfgate ever moves to an RT that adds `EntitySystem.ProtoMan` (it will emit CS0108).

### 2.6 `EntityPrototypeCompatExtensions.cs`

```csharp
namespace Content.Shared.StatusEffectNew;   // deliberate: puts the extension in scope for the vendored files

/// <summary>RT 277 names this TryGetComponent; RT 289 (Onyx) renamed it to TryComp.</summary>
public static class EntityPrototypeCompatExtensions
{
    public static bool TryComp<T>(this EntityPrototype proto, [NotNullWhen(true)] out T? component, IComponentFactory factory)
        where T : IComponent, new()
        => proto.TryGetComponent(out component, factory);
}
```
Backs `StatusEffectsSystem.cs:144`. Underlying method at `RobustToolbox/Robust.Shared/Prototypes/EntityPrototype.cs:179`.

### 2.7 `WolfmedBodySystem.cs` — `TryDetachPart`

```csharp
namespace Content.Shared._WF.Wolfmed.Compat;

/// <summary>Onyx-shaped body helpers mapped onto Wolfgate's Shitmed body system.</summary>
public sealed class WolfmedBodySystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;

    /// <summary>Detaches a part from its parent slot and drops it, mirroring Onyx's TryDetachPart. `reparent` is ignored.</summary>
    public bool TryDetachPart(EntityUid part, bool reparent = true)
    {
        if (_body.GetParentPartAndSlotOrNull(part) is not { } parentSlot
            || !HasComp<BodyPartComponent>(part)
            || !_body.CanDetachPart(parentSlot.Parent, parentSlot.Slot, part))
            return false;

        // DropPart is protected; the amputate event is the public door to it, and it also drops held
        // items and raises BodyPartDroppedEvent, which Wolfgate appearance/cybernetics/targeting listen for.
        var ev = new AmputateAttemptEvent(part);
        RaiseLocalEvent(part, ref ev);
        return _body.GetParentPartOrNull(part) is null;
    }
}
```
Verified: `GetParentPartOrNull` at `SharedBodySystem.Parts.cs:414`, `GetParentPartAndSlotOrNull` at `:430`, `CanDetachPart` at `:718/:733`, `AmputateAttemptEvent` at `_Shitmed/Body/Events/BodyPartEvents.cs:9-10`. **Serves:** `AmputationSystem.cs:118` (phase 3) — ship in WP2 anyway so phase 3 has no new compat work, and WP9's `BodyConsequencesTest` needs it.

### 2.8 `StunSystemOnyxCompat.cs`

```csharp
namespace Content.Shared.Stunnable;

/// <summary>Onyx-shaped paralyse call mapped onto Wolfgate's TryParalyze.</summary>
public static class StunSystemOnyxCompat
{
    public static bool TryUpdateParalyzeDuration(this SharedStunSystem stun, EntityUid uid, TimeSpan? duration, bool visualized = false)
        => duration is { } d && d > TimeSpan.Zero && stun.TryParalyze(uid, d, refresh: false);
}
```
Extension is safe here: no instance member of that name exists in Wolfgate (`SharedStunSystem.cs:196,223,244` only has `TryStun`/`TryKnockdown`/`TryParalyze`). `refresh: false` (add-to-existing) is the closer match to "update duration". Behavioural deviation to record: Wolfgate re-triggers stun VFX each call. **Serves:** `PainSystem.cs:291`.

### 2.9 `SharedChatSystem.Wolfmed.cs` (+ the paired upstream override, D25)

```csharp
namespace Content.Shared.Chat;

public abstract partial class SharedChatSystem
{
    /// <summary>Shared entry point for emotes; only the server implementation does anything.</summary>
    public virtual void TryEmoteWithChat(EntityUid source, string emoteId,
        ChatTransmitRange range = ChatTransmitRange.Normal, bool hideLog = false, string? nameOverride = null,
        bool ignoreActionBlocker = false, bool forceEmote = false) { }
}
```
Signature copied **exactly** from `WG/Content.Server/Chat/Systems/ChatSystem.Emote.cs:60-68` (parameter names and defaults included, or the `override` will not bind). The paired upstream edit is HOOK 6 — one word. **Serves:** `PainSystem.cs:297`, which passes a string and discards the result.

### 2.10 `WolfmedBedHealMarkerComponent.cs` + `WolfmedBedHealMarkerSystem.cs`

```csharp
// Content.Shared/_WF/Wolfmed/Compat/WolfmedBedHealMarkerComponent.cs
namespace Content.Shared._WF.Wolfmed.Compat;

/// <summary>Shared stand-in for the server-only HealOnBuckleComponent so shared wound code can test for a healing bed.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedBedHealMarkerComponent : Component;
```
```csharp
// Content.Server/_WF/Wolfmed/Compat/WolfmedBedHealMarkerSystem.cs
/// <summary>Keeps the shared bed-heal marker in step with the server HealOnBuckleComponent.</summary>
public sealed class WolfmedBedHealMarkerSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<HealOnBuckleComponent, ComponentStartup>(OnStartup);
    }
    private void OnStartup(Entity<HealOnBuckleComponent> ent, ref ComponentStartup args)
        => EnsureComp<WolfmedBedHealMarkerComponent>(ent);
}
```
`HealOnBuckleComponent` is `Content.Server.Bed.Components` (`WG/Content.Server/Bed/Components/HealOnBuckleComponent.cs:6`), unreachable from a shared file. **`<HealOnBuckleComponent, ComponentStartup>` is free** — Wolfgate's only subscriptions on that component are `StrappedEvent`/`UnstrappedEvent` at `WG/Content.Server/Bed/BedSystem.cs:35-36` (verified by grep in revision 2; §5.2). Paired `// WOLFGATE` edit in the vendored `WoundDamageRoutingSystem.cs:3,952`:
```csharp
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE: HealOnBuckleComponent is server-only here
…
else if (origin is { } bed && HasComp<WolfmedBedHealMarkerComponent>(bed)) // WOLFGATE
```
Zero upstream edits.

### 2.11 `OnyxBodyEvents.cs` (WP2) + `WolfmedBodyPartLifecycleSystem.cs` (WP5, D28)

```csharp
// Content.Shared/_WF/Wolfmed/Compat/OnyxBodyEvents.cs
namespace Content.Shared.Body;

/// <summary>Raised on a part when it gains a body. Onyx shape.</summary>
public readonly record struct OrganGotInsertedEvent(EntityUid Target);

/// <summary>Raised on a part when it loses a body. Onyx shape.</summary>
public readonly record struct OrganGotRemovedEvent(EntityUid Target);
```

```csharp
// Content.Server/_WF/Wolfmed/WolfmedBodyPartLifecycleSystem.cs   (WP5)
/// <summary>Drives Onyx's part-lifecycle entry points from Wolfgate's body-scoped part events.</summary>
public sealed class WolfmedBodyPartLifecycleSystem : EntitySystem
{
    [Dependency] private WoundDamageProjectionSystem _projection = default!;
    [Dependency] private SharedBodySystem _body = default!;

    public override void Initialize()
    {
        // WOLFGATE (D28): <BodyComponent, BodyPart*Event> is owned by Shitmed's
        // SharedBodySystem.PartAppearance.cs:25-26. WoundHostComponent sits on the same entity
        // and scopes the handler to wound hosts, which is what we want anyway.
        SubscribeLocalEvent<WoundHostComponent, BodyPartAddedEvent>(OnPartAdded);
        SubscribeLocalEvent<WoundHostComponent, BodyPartRemovedEvent>(OnPartRemoved);
    }
}
```
The handler does two jobs:
1. Calls `WoundDamageProjectionSystem.OnPartInserted` / `OnPartRemoved` (`:95`, `:105`) — **these have no caller anywhere in the Onyx wound set**, so without this, wounds silently never initialise on surgically attached limbs (§8.3 trap 2).
2. Re-raises `OrganGotInsertedEvent`/`OrganGotRemovedEvent` on the part **and its whole subtree** (`GetBodyPartChildren`), matching Onyx's semantics (ONYX `_Onyx/Body/Systems/SharedBodySystem.cs:64-111`); Wolfgate raises once for the detached root only.

Organ-scoped events stay separate: Wolfgate raises `OrganAddedToBodyEvent`/`OrganRemovedFromBodyEvent` **on the organ** (`SharedBodySystem.Organs.cs:46,68`), so those are subscribed as `<OrganComponent, …>` — a free pair. **Serves:** `FractureEffectsSystem.cs:34,35` (phase 2) and WP5's projection wiring.

### 2.12 `WolfmedBodyPartComponent.cs` + `WolfmedBodyPartSystem.cs` (D8) — ships in WP4

```csharp
// Content.Shared/_WF/Wolfmed/Body/WolfmedBodyPartComponent.cs
namespace Content.Shared._WF.Wolfmed.Body;

/// <summary>Wound-system data Onyx keeps on its own BodyPartComponent; Wolfgate stays on Shitmed's, so it lives here.</summary>
[RegisterComponent]
public sealed partial class WolfmedBodyPartComponent : Component
{
    /// <summary>Fracture profile for this part. Null = no fractures.</summary>
    [DataField] public ProtoId<FractureProfilePrototype>? FractureProfile;

    /// <summary>Structural damage cap; damage past it becomes tear-off pressure instead. Zero disables overflow entirely.</summary>
    [DataField] public FixedPoint2 MaxDamage;

    /// <summary>Per-damage-type totals at which the part becomes severable.</summary>
    [DataField] public Dictionary<ProtoId<DamageTypePrototype>, FixedPoint2> AmputationThresholds = new();

    /// <summary>Minimum follow-up hit per damage type needed to detach a ruined part.</summary>
    [DataField] public Dictionary<ProtoId<DamageTypePrototype>, FixedPoint2> DismembermentFinishingDamage = new();

    /// <summary>Severity of the consequence wound left on the parent when this part is torn off.</summary>
    [DataField] public FixedPoint2 AmputationConsequenceSeverity = 35;

    /// <summary>Overrides the host's per-part-type dismemberment severity.</summary>
    [DataField] public FixedPoint2? DismembermentSeverity;
}
```
```csharp
// Content.Shared/_WF/Wolfmed/Body/WolfmedBodyPartSystem.cs
/// <summary>Reads Wolfmed part data off Shitmed body parts, with a no-wounds default.</summary>
public sealed class WolfmedBodyPartSystem : EntitySystem
{
    private static readonly WolfmedBodyPartComponent None = new();

    /// <summary>Wolfmed data for a part; a zeroed default when the part has none.</summary>
    public WolfmedBodyPartComponent Get(EntityUid part) => CompOrNull<WolfmedBodyPartComponent>(part) ?? None;
}
```
Not networked: nothing mutates these at runtime and the client gets them from the prototype at spawn. Exact per-site edits: `body-organ.md` §2.6 (follow that table literally). **Serves:** `AmputationSystem` ×11, `WoundDamageRoutingSystem` ×3, `WoundFractureSystem:146`, `FractureAlertSystem:24`.

**`MaxDamage` defaults to zero and that is load-bearing:** `AccumulateAmputationOverflow` early-returns when `MaxDamage <= FixedPoint2.Zero` (`ONYX WoundDamageRoutingSystem.cs:724-731`), so any limb WP7's `parts.yml` misses gets no amputation overflow *and* never resets `WoundableComponent.AmputationOverflow`. Phase 1 ships no `AmputationSystem` (D26), so the symptom is invisible until phase 3 and then presents as "some limbs can never be severed". WP7 carries an acceptance check for this.

### 2.13 `WoundTargetResolver.cs` (D10) — ships in WP4

`Content.Shared/_WF/Wolfmed/Targeting/WoundTargetResolver.cs`, a system exposing exactly the methods `WoundDamageRoutingSystem` calls, implemented over Shitmed's `SharedBodySystem`:

```csharp
namespace Content.Shared._WF.Wolfmed.Targeting;

/// <summary>Resolves TargetBodyPart values to Wolfgate body-part entities for wound routing.</summary>
public sealed class WoundTargetResolver : EntitySystem
{
    /// <summary>Resolves a single-bit target to a part, falling back hand→arm, foot→leg, else torso.</summary>
    public bool TryResolveAvailable(EntityUid body, TargetBodyPart target, out EntityUid part);

    /// <summary>Resolves a single-bit target to a part with no fallback.</summary>
    public bool TryResolveExact(EntityUid body, TargetBodyPart target, out EntityUid part);

    /// <summary>Resolves a requested target for a shooter; phase 1 resolves exactly, with no scatter.</summary>
    public bool TryResolve(EntityUid body, TargetBodyPart requested, EntityUid? shooter, out EntityUid part);

    /// <summary>Resolves the part the origin entity is currently aiming at.</summary>
    public bool TryResolve(EntityUid body, EntityUid origin, out EntityUid part);

    /// <summary>Every attached part matching any bit in the mask.</summary>
    public List<EntityUid> GetMatchingParts(EntityUid body, TargetBodyPart mask);
}
```
Built on `SharedBodySystem.ConvertTargetBodyPart` (`_Shitmed/Body/Systems/SharedBodySystem.Targeting.cs:389-408`), `GetTargetBodyPart` (`:368-384`), `GetBodyChildrenOfType` (`SharedBodySystem.Parts.cs:991`), `GetBodyChildren` (`SharedBodySystem.Body.cs:256`).

- **`TargetBodyPart.Groin` must resolve, not be rejected** (D9). `ConvertTargetBodyPart` already maps it to `(BodyPartType.Torso, BodyPartSymmetry.None)` at `:395`, so the resolver gets it for free by delegating; do not add a `Groin` rejection.
- Reads `CCVars.TargetingEnabled` to gate the wound path only — it must **not** gate Shitmed's existing always-on targeting (D2: non-wound-hosts unchanged).
- **Phase 1 resolves to the exact requested part with no anatomical-odds scatter** — Wolfgate's gun/melee systems already roll their own inaccuracy before calling in, and Onyx's `Roll()`, Wolfgate's `GetRandomBodyPart` and Shitmed's 33 %-off-torso `GetRandomPartSpread` are three incompatible designs (`targeting.md` §6 open question, resolved here).

Paired `// WOLFGATE` edits in `WoundDamageRoutingSystem.cs`: one `[Dependency]` line (`TargetResolverSystem` → `WoundTargetResolver`), one `using` swap (`Content.Shared._Onyx.Targeting` → `Content.Shared._Shitmed.Targeting` for the enum), and three `.Chest` → `.Torso` literals (`:481`, `:613`, plus `WoundDamageComponents.cs:47`).

### 2.14 Add `IsSelectable` to Shitmed's `SharedTargetingSystem` (3 lines, upstream)

`WG/Content.Shared/_Shitmed/Targeting/SharedTargetingSystem.cs`:
```csharp
    // WOLFGATE: Wolfmed snapshot/targeting needs a single-bit check; copied from Onyx's SharedTargetingSystem.
    public static bool IsSelectable(TargetBodyPart part)
        => part != 0 && (part & (part - 1)) == 0 && (part & TargetBodyPart.All) != 0;
```
`TargetBodyPart.All` includes `Groin` (`TargetBodyPart.cs:30`), so `Groin` is selectable — correct, and required by D9's scoping. **Serves:** `TargetingSnapshotSystem.cs:23,40`, WP5's `targetPart` handoff.

### 2.15 Entity-effect adapters (D16) — phase 4, `Content.Shared/_WF/Wolfmed/EntityEffects/`

Four new old-style classes with Onyx's exact simple names so ported `!type:` tags work verbatim:

| YAML `!type:` | New Wolfgate class | Base | Notes |
|---|---|---|---|
| `SuppressPain` | `Content.Shared._WF.Wolfmed.EntityEffects.SuppressPain` | `EntityEffect` | Explicit `TryGetComponent<PainComponent>` guard replaces the ECS base's automatic component filter. `args is EntityEffectReagentArgs r ? r.Scale : FixedPoint2.New(1)` replaces `args.Scale`. Override `ReagentEffectGuidebookText` (Wolfgate's name), not `EntityEffectGuidebookText`. |
| `MendFractures` | same namespace | `EntityEffect` | Guard on `WoundHostComponent`, loop `GetBodyChildren`, `WoundFractureSystem.GetFracture` + `WoundSystem.ChangeSeverity`. |
| `TakeStaminaDamage` | same namespace | `EntityEffect` | Wolfgate's `StaminaSystem.TakeStaminaDamage` already accepts `immediate`, so `Immediate` can actually be honoured — unlike Onyx's own version. |
| `StaminaDamageCondition` | same namespace | `EntityEffectCondition` | Mirrors `Content.Server/EntityEffects/EffectConditions/TotalDamage.cs`. |

**Existing effects that need the same treatment, not a new class** (D16, revised): `HealthChange` **and** `EvenHealthChange` each get one new `[DataField] HashSet<TreatmentCapability> TreatmentCapabilities = [TreatmentCapability.Biological];` and a ~6-line `// WOLFGATE` wound-routing branch in `Effect()` (§3 HOOK 9), so every existing `!type:HealthChange` / `!type:EvenHealthChange` reagent keeps working unchanged. `DistributedHealthChange` does not exist in Wolfgate and must be authored from scratch if any ported reagent uses that tag — check before WP11 starts. Verified no name collision for `SuppressPain`, `MendFractures`, `TakeStaminaDamage`, `StaminaDamageCondition`, `DistributedHealthChange`.

### 2.16 `DamageSystemsNamespace.cs` — not needed

Wolfgate already has a `Content.Shared.Damage.Systems` namespace (`DamageContactsSystem.cs`, `PassiveDamageSystem.cs`), so the vendored files' `using Content.Shared.Damage.Systems;` resolves without a marker type. **Skip** `damage-bridge.md` §7.3's `WolfmedDamageSystemsMarker`.

### 2.17 Dead `using Content.Shared.Body;` — leave it alone

Onyx keeps `BodyComponent`/`OrganComponent` in the bare `Content.Shared.Body` namespace; Wolfgate keeps them in `Content.Shared.Body.Components` / `.Organ`. Nine wound files carry `using Content.Shared.Body;`, but only three actually need a symbol from it and get a `// WOLFGATE` using swap: `WoundSystem.cs:34` (`BodyComponent`), `OrganDamageSystem.cs:87` (`OrganComponent`), `WoundDamageProjectionSystem.cs:29` (`InitialBodySystem`). In the other six — `WoundPrototype.cs:3`, `WoundStatusEffectSystem.cs:1`, `BodyPartFunctionalitySystem.cs:1`, `WoundBleedingSystem.cs:3`, `FractureAlertSystem.cs:2`, `FractureEffectsSystem.cs:1` — the using **resolves and is unused**: C# declares every enclosing namespace, so `Content.Shared.Body` exists as the parent of `Content.Shared.Body.Part`. **Keep them verbatim.** Do not "clean" them; each removal is a spurious diff at re-sync time. **WP10-7 correction (PLAN2 P2-D3):** this note previously listed `FractureEffectsSystem.cs:34-35` as needing a using swap for `OrganGot*Event`. It does not — `Content.Shared/_WF/Wolfmed/Compat/OnyxBodyEvents.cs` declares `namespace Content.Shared.Body;` and both `OrganGot*Event` structs byte-shape-identical to Onyx's (including `[ByRefEvent]`-ness and the `Target` member name), so the verbatim `using Content.Shared.Body;` already resolves them with zero edits. Verified when WP10-1 landed the file.

---

## 3. Upstream `// WOLFGATE` hooks — the complete authorised list

Nothing outside this list may be edited in an upstream (non-`_Onyx`, non-`_WF`) file without escalating.

| # | File | Site | Change | WP |
|---|---|---|---|---|
| **GUARD A** | `Content.Shared/_Shitmed/Body/Systems/SharedBodySystem.Targeting.cs` | `:63/:66` + `:108` | Add `private EntityQuery<WoundHostComponent> _queryWoundHost;` beside `_queryTargeting`, assign it in `InitializeIntegrityQueue`, and open `OnTryChangePartDamage` with `if (_queryWoundHost.HasComp(ent)) return;`. **Not** `_net.IsServer`-gated — component presence only, or the client mispredicts every hit. | WP5 |
| **GUARD B** | same | `:222-234` | Add `&& !(partEnt.Comp.Body is { } b && _queryWoundHost.HasComp(b))` to the sever condition. Leave `CheckBodyPart` (`:233`) running — it drives limb disable/enable and the targeting doll. | WP5 |
| **GUARD C** | same | `:80-85` and `:101` | Add `&& !_queryWoundHost.HasComp(body)` to `ProcessIntegrityTick`'s condition, and skip the `_integrityJobQueue.EnqueueJob` for wound hosts in `Update`. | WP5 |
| **GUARD D** | `Content.Shared/Damage/Systems/DamageableSystem.cs` | insert between `:248` (`damage = ApplyUniversalAllModifiers(damage);`) and `:250` (`var delta = new DamageSpecifier();`), plus a query field beside `:37`/`:59` | The routing seam. ~10 lines: <br>```// WOLFGATE: Wolfmed routing seam. A handler that clears the dict keeps the damage off this entity's own\n// DamageableComponent and applies it to body parts instead.\nif (_woundHostQuery.HasComp(uid.Value))\n{\n    var dealt = new DamageDealtEvent(damage, origin, interruptsDoAfters);\n    RaiseLocalEvent(uid.Value, ref dealt);\n    if (damage.Empty)\n        return damage;\n}``` plus `using Content.Shared._Onyx.Wounds; // WOLFGATE`. **This is the one upstream change the port cannot avoid.** Verified: `:210-212` raises `BeforeDamageChangedEvent`, `:227-245` applies resistances and `DamageModifyEvent`, `:247-248` the universal modifiers, `:250-266` writes — so the seam sits after all modifiers and before the write, and the re-entrant routed pass does pick up armour at `:233-234`. | WP5 |
| **GUARD D2** | same | `:469-475` + construction site `:210-211` + `:214-215` | **Three** appended optional members on `BeforeDamageChangedEvent` — `float ArmorPenetration = 0f, EntityUid? Tool = null` (D23) and `DamageSpecifier? Applied = null` (D27) — populated at the single construction site; **and `:215` changes from `return null;` to `return before.Applied;`**. Appending is safe: it is a positional `record struct` with exactly one construction site, whose properties are already mutated by handlers (`args.Cancelled = true`). Godmode (`SharedGodmodeSystem.cs:19`) and stasis (`SharedStasisSystem.cs:52`) never set `Applied`, so they keep returning `null` and behave exactly as today. **Moved from WP8 to WP5 in revision 2** — without it the bridge ships a combat regression the moment WP7 lands `WoundHost` (D27). | WP5 |
| **GUARD E** | `Content.Server/Body/Systems/BloodstreamSystem.cs` | `:212` | `if (HasComp<WoundHostComponent>(ent)) return;` at the top of `OnDamageChanged`. | WP6 |
| **GUARD E2** | same | `:280` (drifted from this plan's `:274` by the time WP10-2 landed it — re-verified against the tree) | `!HasComp<WoundHostComponent>(ent) &&` in front of the `GetBloodLevelPercentage(...) < BloodlossThreshold` pale-message test. **Land this only together with the `_Onyx/HealthExaminable` port**, or the "looks pale" message silently disappears with nothing replacing it. | WP10-2 |
| **GUARD E3** | same | `:403-423` | Split `TryModifyBleedAmount` into a public wound-host-gated entry, an `internal TryModifyWoundBleedProjection`, and a shared private implementation taking `bool woundProjection` (full code in `hooks-a.md` §4c). This one gate also silently no-ops the passive-decay call at `:137` — **do not duplicate the guard there**. | WP6 |
| **GUARD E4** | `Content.Server/_Mono/Traits/Physical/HemophiliaSystem.cs` | `:38` | Same one-line `HasComp<WoundHostComponent>` guard at the top of `OnDamageChanged`. Re-express hemophilia as a wound-bleeding multiplier in `_WF/Wolfmed` later. | WP6 |
| **HOOK 5** | `Content.Shared/_Shitmed/Targeting/SharedTargetingSystem.cs` | end of class | Add the static `IsSelectable` (§2.14). | WP2 |
| **HOOK 6** | `Content.Server/Chat/Systems/ChatSystem.Emote.cs` | **`:60`** (the `string emoteId` overload) | `public void TryEmoteWithChat(` → `public override void TryEmoteWithChat(` — **one word, return type unchanged** (D25). **Do not touch `:85`**, the `EmotePrototype` overload; `PainSystem.cs:297` passes a string. | WP4 |
| **HOOK 7** | `Content.Server/Medical/Components/HealingComponent.cs` | field list | Four additive `// WOLFGATE` `[DataField]`s (D14): `bool HealDamage = true`, `bool HealWounds = true`, `HashSet<TreatmentCapability> TreatmentCapabilities = [TreatmentCapability.Biological]`, `HashSet<string>? AllowedWoundStages`. **The collection types are mandatory, not stylistic** — `ResolveHealingPartEvent` demands `IReadOnlySet<…>` (D31), which `List<>` does not implement. Needs a second `// WOLFGATE` line, `using Content.Shared._Onyx.Wounds;`, for `TreatmentCapability` (`ONYX WoundPrototype.cs:202-208`). | WP6 |
| **HOOK 8** | `Content.Server/Medical/HealingSystem.cs` | `OnDoAfter` (`:56`), `HasDamage` (`:134`), `TryHeal` (`:184`) | Wound-host branches (full code in `hooks-a.md` §5a/5b/5d). | WP6 |
| **HOOK 9** | `Content.Server/EntityEffects/Effects/HealthChange.cs` **and `EvenHealthChange.cs`** | `Effect()` + field list | `TreatmentCapabilities` field + ~6-line wound-routing branch, in **both** files (§2.15, `entityeffects-gap.md` §4 option 1). | WP11 |
| **HOOK 10** | `Content.Shared/Armor/SharedArmorSystem.cs` | `:42-46` (`OnDamageModify`) | Wound-host systemic-armour branch + `ApplyWoundSystemicArmor` helper. **Must route through Wolfgate's existing `DamageSpecifier.PenetrateArmor(component.Modifiers, args.Args.ArmorPenetration)`** (`:44-45`), not bypass it, or every Wolfgate AP weapon stops working against wound hosts. Full code in `hooks-a.md` §7a. | WP8 |
| **HOOK 11** | `Content.Shared/Mobs/Systems/MobThresholdSystem.cs` | **`:340`** (`CheckThresholds`) and **`:406`** (`UpdateAlerts`'s `damageable.TotalDamage`) | `CheckVitalDamage(target, damageableComponent)` in place of `damageableComponent.TotalDamage`. `CheckVitalDamage` falls back to total damage for non-wound-hosts, so no branch is needed. | WP8 |
| **HOOK 12** | `Content.Server/Medical/DefibrillatorSystem.cs` | `:209-211` | Same `CheckVitalDamage` substitution, so revival agrees with whatever decides death. | WP8 |
| **HOOK 13** | `Content.Shared/Execution/SharedExecutionSystem.cs` | `:221` | `_woundRouting.TryApplyLethalDamage(victim, meleeWeaponComp.Damage, attacker);` after `AttemptLightAttack`. Self-guards on `HasComp<WoundHostComponent>`, so it is safe unconditionally. | WP8 |

**Explicitly NOT hooked** (D24, now safe because of D27): `SharedProjectileSystem`, `HitscanBasicDamageSystem`, `SharedMeleeWeaponSystem`, `DamageOtherOnHitSystem`, `DamageOnInteractSystem`, `ExplosionSystem`.
**Explicitly NOT touched at all:** `Content.Shared/Bed/BedSystem.cs`, `SharedStaminaSystem.cs`, `ThermalRegulatorSystem.cs`, `CrewMonitoringConsoleSystem.cs`, suit sensors, `SharedCryoPodSystem.cs`, `SharedDoAfterSystem.cs` (Wolfgate's Goobstation `GetDoAfterDelayMultiplierEvent` at `:207-214` already does the job — subscribe to it from `_WF` instead), `MobStateSystem*.cs`, `RespiratorSystem.cs`, `Content.Shared/Body/Organ/OrganComponent.cs`, `Content.Shared/Body/Part/BodyPartType.cs`, `Content.Shared/Humanoid/HumanoidVisualLayers.cs`, `Content.Shared/_Shitmed/Targeting/TargetBodyPart.cs`.

---

## 4. Work packages

Build order and parallelism:

```
WP1 (StatusEffectNew) ─┐
WP2 (Compat core)     ─┼─► WP4 (Wounds core) ─► WP5 (Damage bridge) ─► WP6 (Bleeding/Healing) ─┬─► WP7 (Prototypes + mob wiring)
WP3 (Circulation data)─┘                                                                        ├─► WP8 (Bridge hardening)
                                                                                                └─► WP9 (Tests)
                                                                        WP10 (phase 2), WP11 (phase 4) — later, listed for the manifest
```
- **Parallel group A: WP1, WP2, WP3** — fully disjoint file sets.
- **Parallel group B: WP4.** **Group C: WP5.** **Group D: WP6.**
- **Parallel group E: WP7, WP8, WP9** — WP7 is YAML/locale/textures, WP8 is C#, WP9 is tests. WP9 cannot *pass* until WP7 lands `WoundHost`, so schedule WP9 to start with WP7/WP8 and finish after them.
- Target ≤12 files per WP. Where a WP exceeds that, split it at the marked seam.

---

### WP1 — StatusEffectNew framework (D1)

**Goal:** the `StatusEffectNew` framework compiles and loads with zero prototype errors, serving nothing yet.

**Files** (ONYX path → WG path, all identical paths per D1):

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | `Content.Shared/StatusEffectNew/Components/StatusEffectComponent.cs` | same | verbatim |
| 2 | `.../Components/StatusEffectContainerComponent.cs` | same | verbatim |
| 3 | `.../Components/StatusEffectAlertComponent.cs` | same | verbatim |
| 4 | `.../Components/CloneableStatusEffectComponent.cs` | same | verbatim (keep the UTF-8 BOM) |
| 5 | `.../Components/ExaminableStatusEffectComponent.cs` | same | verbatim |
| 6 | `.../Components/PermanentStatusEffectsComponent.cs` | same | verbatim |
| 7 | `.../Components/RejuvenateRemovedStatusEffectComponent.cs` | same | verbatim |
| 8 | `Content.Shared/StatusEffectNew/StatusEffectsSystem.cs` | same | verbatim |
| 9 | `.../StatusEffectSystem.API.cs` | same | verbatim (keep the unused `System.ComponentModel.Design` / `YamlDotNet.Core.Tokens` usings — they emit no IL, and `YamlDotNet.Core.Tokens` already resolves in content at `WG/Content.Shared/_Goobstation/Clothing/Systems/ClothingGrantingSystem.cs:6`) |
| 10 | `.../StatusEffectSystem.Relay.cs` | same | **edited** — see below |
| 11 | `.../StatusEffectAlertSystem.cs` | same | verbatim |
| 12 | `.../ExaminableStatusEffectSystem.cs` | same | **edited** — `[SubscribeLocalEvent]` → explicit `Initialize()` |
| 13 | `.../PermanentStatusEffectsSystem.cs` | same | **edited** — three `[SubscribeLocalEvent]` → explicit `Initialize()` |
| 14 | — | `Content.Shared/_WF/Wolfmed/Compat/StatusEffectsSystem.Wolfgate.cs` | new (§2.5) |
| 15 | — | `Content.Shared/_WF/Wolfmed/Compat/EntityPrototypeCompatExtensions.cs` | new (§2.6) |
| 16 | — | `Content.Shared/_WF/Wolfmed/Compat/AlertsSystem.UpdateAlert.cs` | new (§2.4) |
| 17 | `Content.Shared/Traits/Assorted/PainNumbnessStatusEffectComponent.cs` | same | **verbatim** — 20 lines. *Moved into WP1 in revision 2:* `PainSystem.cs:323` calls `_statusEffects.EnumerateStatusEffects<PainNumbnessStatusEffectComponent>(entity)` with `using Content.Shared.Traits.Assorted;` at `:13`, so WP4 cannot compile without it (`CS0246`). It drags nothing: its only dependency is `LocalizedDatasetPrototype` (`WG/Content.Shared/Dataset/LocalizedDatasetPrototype.cs`) and its default `ForceSayNumbDataset` already exists at `WG/Resources/Prototypes/Datasets/damage_force_say.yml:14`. No registration collision: Wolfgate has `PainNumbnessComponent`/`PainNumbnessSystem` in the same namespace (`WG/Content.Shared/Traits/Assorted/`), which register as `PainNumbness`, not `PainNumbnessStatusEffect`. |

**Exact edits to `StatusEffectSystem.Relay.cs`:**

Line 2 — remove the MartialArts using:
```diff
-using Content.Shared._Onyx.MartialArts; // <Onyx-MartialArts>
+// WOLFGATE: Onyx's _Onyx.MartialArts is not ported; the GetMeleeTargetModifiersEvent relay below is dropped.
```
After `using Content.Shared.Damage.Systems;` — add:
```diff
+using Content.Shared.Damage; // WOLFGATE: Wolfgate keeps DamageModifyEvent and ModifySlowOnDamageSpeedEvent in Content.Shared.Damage.
```
Delete 11 subscription lines, replacing each run with a `// WOLFGATE` comment giving the reason:
- `:47-49` — `StandUpAttemptEvent`, `StunEndAttemptEvent`, `RefreshStaminaCritThresholdEvent` do not exist in Wolfgate.
- `:52` — `GetMeleeTargetModifiersEvent` belongs to Onyx's MartialArts.
- `:61-63` — `EmoteActionEvent` does not exist; `EmoteEvent`, `AccentGetEvent`, `VocalSystem`, `MumbleAccentSystem` are server-side here.
- `:65` — `BleedModifierEvent` does not exist; Wolfgate bleeding is a server-side `BleedAmount`.
- `:67` — `RefreshPressureImmunityEvent` does not exist.
- `:69` — `SelfBeforeInjectEvent` does not exist.
- `:71` — `CatchAttemptEvent` does not exist.

Lines 33-45, 50-51, 53-54, 56-59, 66, 68, 72 stay verbatim. Keep `:51` (`GetBlurEvent`) — the type exists in Wolfgate even though Onyx tags it MartialArts.

**`ExaminableStatusEffectSystem.cs`:** replace the `[SubscribeLocalEvent]` attribute with
```csharp
    // WOLFGATE: RT 277 has no [SubscribeLocalEvent] source generator; subscribe explicitly.
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ExaminableStatusEffectComponent, StatusEffectRelayedEvent<ExaminedEvent>>(OnExaminedEvent);
    }
```

**`PermanentStatusEffectsSystem.cs`:** remove the three attributes (`:16,26,35`) and insert after the `[Dependency]` line:
```csharp
    // WOLFGATE: RT 277 has no [SubscribeLocalEvent] source generator; subscribe explicitly.
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PermanentStatusEffectsComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<PermanentStatusEffectsComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<PermanentStatusEffectsComponent, ComponentRemove>(OnRemove);
    }
```
Keep the `// <Onyx-OrganEffects>` block (`:13-24`) verbatim.

**Prototypes / locale (must land in the same commit — unknown `[EntityCategory]` ids log errors at `PrototypeManager.Categories.cs:76` and fail every integration-test pair):**

| Path | Content |
|---|---|
| `Resources/Prototypes/_Onyx/Entities/categories.yml` | the `StatusEffects` `entityCategory` from ONYX `Resources/Prototypes/Entities/categories.yml:31-34` |
| `Resources/Locale/en-US/_Onyx/entity-categories.ftl` | `entity-category-name-status-effects = Status Effects` |
| `Resources/Prototypes/Entities/StatusEffects/misc.yml` | **only** the abstract bases `StatusEffectBase`, `MobStatusEffectBase`, `MobStatusEffectDebuff` (and optionally `MobStandStatusEffectBase`). Drop every concrete entry — each needs a component we are not porting. |

**Deferred to WP10 (record in the manifest as deliberate):** `Resources/Prototypes/Entities/StatusEffects/movement.yml` (`StatusEffectSlowdown`), `Content.Shared/Movement/Components/MovementModStatusEffectComponent.cs` + trimmed `MovementModStatusSystem.cs`, `Resources/Prototypes/_Onyx/StatusEffects/wounds.yml`. *(`PainNumbnessStatusEffectComponent.cs` was on this list in revision 1 and has moved into the table above.)*
**Never ported:** `_Onyx/StatusEffects/surgery.yml` (needs `RaspyAccent`, which exists nowhere in Wolfgate, and D7 excludes Onyx surgery); `_Onyx/StatusEffects/{abductor_glands,breathing_immunity,cosmiccult,dementia,tile_movement,vampire}.yml`; `Content.{Shared,Server}/_Onyx/StatusEffects/Immunities/`.

**Sandbox / IoC:** none needed. `Content` is a whitelisted namespace *prefix* (`WG/RobustToolbox/Robust.Shared/ContentPack/Sandbox.yml:24-27`), and `System.Linq` (`:634`) / `System.Numerics` (`:646`) are already allowed. Systems and components auto-register.

**Hazard to document in `Docs/Wolfmed/`:** two types named `StatusEffectsSystem` now exist (`Content.Shared.StatusEffect` and `Content.Shared.StatusEffectNew`). Any file importing both namespaces gets CS0104. No ported file does.

**Checkpoint:** `dotnet build Content.Server` + `dotnet build Content.Client` green; server starts with zero `PrototypeManager` errors.

**File count:** 17 code files + 3 resource files.

---

### WP2 — Compat core (parallel with WP1 and WP3)

**Goal:** every Onyx-shaped API that does not depend on wound types exists and compiles. No behaviour change anywhere.

**Files (all new `_WF`, plus one 3-line upstream hook):**

| # | Path | Spec |
|---|---|---|
| 1 | `Content.Shared/_WF/Wolfmed/Compat/WolfmedDamageableSystem.cs` | §2.1. `HealEvenly`/`HealDistributed` may throw `NotImplementedException` until WP11. `SetDamage` must ship with D30's zeroing loop from day one. |
| 2 | `Content.Shared/_WF/Wolfmed/Compat/DamageSpecifier.Wolfmed.cs` | §2.2 |
| 3 | `Content.Shared/_WF/Wolfmed/Compat/DamageDealtEvent.cs` | §2.3 |
| 4 | `Content.Shared/_WF/Wolfmed/Compat/WolfmedBodySystem.cs` | §2.7 |
| 5 | `Content.Shared/_WF/Wolfmed/Compat/StunSystemOnyxCompat.cs` | §2.8 |
| 6 | `Content.Shared/_WF/Wolfmed/Compat/SharedChatSystem.Wolfmed.cs` | §2.9 — `virtual void`, signature copied verbatim from `ChatSystem.Emote.cs:60-68`. The paired `override` is HOOK 6 (WP4); this file alone compiles. |
| 7 | `Content.Shared/_WF/Wolfmed/Compat/WolfmedBedHealMarkerComponent.cs` | §2.10 |
| 8 | `Content.Server/_WF/Wolfmed/Compat/WolfmedBedHealMarkerSystem.cs` | §2.10 — pair verified free (§5.2) |
| 9 | `Content.Shared/_WF/Wolfmed/Compat/OnyxBodyEvents.cs` | §2.11 — event declarations only. **The bridge system moved to WP5** (D28). |
| 10 | `Content.Shared/_Shitmed/Targeting/SharedTargetingSystem.cs` | **upstream, HOOK 5** — 3-line static `IsSelectable` |

**Sandbox / IoC:** none. The only BCL surface the wound tree adds beyond implicit usings is `System.Linq` and `System.Numerics`, both already `All: True`.

**Checkpoint:** both builds green. `git diff --stat` shows exactly one upstream file touched.

**File count:** 10.

---

### WP3 — Circulation data layer + CCVars + targeting snapshot (parallel with WP1/WP2)

**Goal:** the prototype kinds, CVars and snapshot component `WoundPrototype.cs` and routing depend on exist, with zero behaviour.

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | `Content.Shared/_Onyx/Chemistry/Circulation/CirculatoryStreamComponent.cs` | same | verbatim |
| 2 | `Content.Shared/_Onyx/Chemistry/Circulation/CirculatoryStreamPrototype.cs` | same | **edited** — `// WOLFGATE`-comment out `MetabolismStage` and `MetabolitesStage`; `MetabolismStagePrototype` does not exist in Wolfgate and nothing in the phase-1 path reads them |
| 3 | `Content.Shared/_Onyx/CCVar/CCVars.Wounds.cs` | same | verbatim (zero name and zero cvar-string collisions — verified across all 49 `CCVar` files) |
| 4 | `Content.Shared/_Onyx/CCVar/CCVars.Surgery.cs` | same | verbatim |
| 5 | `Content.Shared/_Onyx/CCVar/CCVars.Targeting.cs` | same | verbatim |
| 6 | `Content.Shared/_Onyx/Targeting/DamageDistribution.cs` | same | verbatim |
| 7 | `Content.Shared/_Onyx/Targeting/TargetingSnapshotComponent.cs` | same | **edited** — `using Content.Shared._Onyx.Targeting;` → `using Content.Shared._Shitmed.Targeting;`; default `RequestedTarget = TargetBodyPart.Chest` → `.Torso` |
| 8 | `Content.Shared/_Onyx/Targeting/TargetingSnapshotSystem.cs` | same | **edited** — same `using` swap (it calls `SharedTargetingSystem.IsSelectable`, supplied by HOOK 5) |
| 9 | `Resources/Prototypes/_Onyx/Chemistry/circulatory_streams.yml` | same | verbatim (2 lines: `- type: circulatoryStream / id: Organic`) |

**Explicitly NOT ported:** `SharedSolutionContainerSystem.CirculatoryStreams.cs` (its two methods are only called from `InitializeStream`/`RemoveStream`, both dropped by D15) and `CirculatoryStreamSystem.cs` (WP6, server-side).

**Checkpoint:** both builds green; `circulatoryStream` prototype loads.

**File count:** 9.

---

### WP4 — Wounds core, shared half

**Goal:** wound data, lifecycle, pain, statuses and fractures compile and are addressable; nothing routes damage yet.

**Depends on:** WP1, WP2, WP3.

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | `Content.Shared/_Onyx/Wounds/WoundEvents.cs` | same | **verbatim** (every external symbol is SAME; `ResolveHealingPartEvent`'s `ProtoId`/`IReadOnlySet` types stay — D31) |
| 2 | `Content.Shared/_Onyx/Wounds/WoundBehaviors.cs` | same | **verbatim** (needs WP1's `StatusEffectComponent`) |
| 3 | `Content.Shared/_Onyx/Wounds/WoundPrototype.cs` | same | **verbatim** (needs WP3's `CirculatoryStreamPrototype`; keep the unused `using Content.Shared.Body;` — §2.17) |
| 4 | `Content.Shared/_Onyx/Wounds/WoundDamageComponents.cs` | same | **edited** — D9 mapping: `:20-21` `[BodyPartType.Chest] = 2.5f` + `[BodyPartType.Groin] = 1.5f` → **one row, `[BodyPartType.Torso] = 4f`** (m2's rebalance); delete `:53` (`[BodyPartType.Groin] = 160`); `:47` `SystemicPainTarget = TargetBodyPart.Chest` → `.Torso`; `:6` `using Content.Shared._Onyx.Targeting;` → `using Content.Shared._Shitmed.Targeting;`. **`LocalizedDamageTypes` (`:34-43`) stays verbatim, `Caustic` included** (D20 reversed). |
| 5 | `Content.Shared/_Onyx/Wounds/WoundSystem.cs` | same | **edited** — add `using Content.Shared.Body.Components; // WOLFGATE: BodyComponent lives here`; delete the `<BodyComponent, RejuvenateEvent>` subscription at `:34` and expose the handler body as `public void ClearBodyWounds(EntityUid body)` (D17) |
| 6 | `Content.Shared/_Onyx/Wounds/WoundScarSystem.cs` | same | **verbatim** (cleanest file in the set) |
| 7 | `Content.Shared/_Onyx/Wounds/WoundStatusEffectSystem.cs` | same | **verbatim** |
| 8 | `Content.Shared/_Onyx/Wounds/BodyPartFunctionalitySystem.cs` | same | **edited** — 1 line: `using Content.Shared._Onyx.Cybernetics;` → `using Content.Shared._Shitmed.Cybernetics;` |
| 9 | `Content.Shared/_Onyx/Wounds/PainSystem.cs` | same | **verbatim** (served by §2.8 + §2.9 + WP1 files 1-13 + WP1 file 17) |
| 10 | `Content.Shared/_Onyx/Wounds/WoundFractureSystem.cs` | same | **edited** — add `using Content.Shared.Damage; // WOLFGATE` (the file only has `.Components`/`.Systems` and uses both `DamageableComponent` and `DamageableSystem` → two CS0246s); `[Dependency]` swap to `WolfmedDamageableSystem`; `:143-146` read `FractureProfile` off `WolfmedBodyPartComponent` |
| 11 | — | `Content.Shared/_WF/Wolfmed/Body/WolfmedBodyPartComponent.cs` | new (§2.12) |
| 12 | — | `Content.Shared/_WF/Wolfmed/Body/WolfmedBodyPartSystem.cs` | new (§2.12) |
| 13 | — | `Content.Shared/_WF/Wolfmed/Targeting/WoundTargetResolver.cs` | new (§2.13) — must accept and fold `TargetBodyPart.Groin` |
| 14 | `Content.Server/Chat/Systems/ChatSystem.Emote.cs` | same | **upstream, HOOK 6** — one word, `:60` only |

> 14 files: split at #10 if one agent is too slow. #1–#9 + #14 is the natural first half; #10–#13 the second.

**Checkpoint:** both builds green; server starts; `wound`/`bodyPartProfile`/`fractureProfile` prototype kinds register (no YAML yet).

**File count:** 14.

---

### WP5 — The damage bridge (D2, D11, D23, D27, D28)

**Goal:** damage aimed at a `WoundHostComponent` mob lands on a body part and projects back onto the mob; nothing double-applies; `TryChangeDamage` still reports what it did.

**Depends on:** WP4.

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | `Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs` | same | **heavily edited** — see the edit list below |
| 2 | `Content.Shared/_Onyx/Wounds/WoundDamageProjectionSystem.cs` | same | **edited** — see below |
| 3 | `Content.Shared/_Onyx/Mobs/Systems/MobThresholdSystem.cs` | same | **edited** (absent from the sparse checkout — read with `git -C C:/Users/jzo12/Documents/Wolfmed/onyx show HEAD:Content.Shared/_Onyx/Mobs/Systems/MobThresholdSystem.cs`). Wolfgate's `MobThresholdSystem` is `sealed partial` in namespace `Content.Shared.Mobs.Systems` (`WG/.../MobThresholdSystem.cs:12`), so the partial attaches. **Edits: (a) *add* `[Dependency] private WolfmedDamageableSystem _damageable = default!;` — there is no dependency to *swap*: the vendored partial declares only `[Dependency] private SharedBodySystem _body` at `:15` and uses `_damageable` at `:27` and `:42`, expecting it on Onyx's main class; Wolfgate's class declares only `_mobStateSystem` and `_alerts` (`:14-15`), so neither `_damageable` nor `_body` collides.** (b) `criticalParts` `{ Head, Chest, Groin }` (`:31-34`) → `{ Head, Torso }` (D9). `body.RootContainer?.ContainedEntity` compiles against `WG/Content.Shared/Body/Components/BodyComponent.cs:26`. |
| 4 | `Content.Shared/Damage/Systems/DamageableSystem.cs` | same | **upstream, GUARD D + GUARD D2** |
| 5 | `Content.Shared/_Shitmed/Body/Systems/SharedBodySystem.Targeting.cs` | same | **upstream, GUARDs A + B + C** |
| 6 | — | `Content.Server/_WF/Wolfmed/WolfmedBodyPartLifecycleSystem.cs` | new (§2.11, **D28**) — subscribes `<WoundHostComponent, BodyPartAddedEvent/BodyPartRemovedEvent>`, calls `OnPartInserted`/`OnPartRemoved`, fans `OrganGot*Event` over the subtree |

**`WoundDamageRoutingSystem.cs` — the exact `// WOLFGATE` edit list:**

| Site | Edit |
|---|---|
| usings | `Content.Shared._Onyx.Targeting` → `Content.Shared._Shitmed.Targeting`; `Content.Shared.Bed.Components` → `Content.Shared._WF.Wolfmed.Compat`; add `Content.Shared._WF.Wolfmed.Body`, `Content.Shared._WF.Wolfmed.Targeting` |
| `[Dependency]` block | `DamageableSystem _damage` → `WolfmedDamageableSystem _damage`; `TargetResolverSystem _targetResolver` → `WoundTargetResolver _targetResolver`; add `WolfmedBodyPartSystem _wfPart` |
| `:50` and `:52` | Add `before: [typeof(SharedArmorPlateSystem)]` to **both** `BeforeDamageChangedEvent` subscriptions or neither. RT requires a system subscribing the same event twice to use identical before/after sets (`EntityEventBus.Ordering.cs:70-78`). This ordering is **mandatory**: `SharedArmorPlateSystem.OnBeforeDamageChanged` (`_Mono/ArmorPlate/SharedArmorPlateSystem.cs:49`) mutates `args.Damage` in place and would absorb twice per hit; it is saved only by its own `if (args.Cancelled …) return;` at `:51`. |
| `:55-62` (`OnBeforeDamageChanged`) | Three additions. (a) `if (args.Cancelled) return;` — godmode/stasis already refused the hit, and ordering against them is impossible from shared code because both concrete systems live in server/Goobstation assemblies and RT keys ordering on the concrete type. (b) The Shitmed `targetPart` handoff: if `args.TargetPart is { } t && SharedTargetingSystem.IsSelectable(t) && _targetResolver.TryResolveAvailable(ent, t, out var part)`, stash it; `try { RouteThroughBodyModifiers(...) } finally { clear }`. **`IsSelectable` is mandatory** — Wolfgate passes composite masks in real code (`TargetBodyPart.All` from `HealthChange.cs:173`, `Torso \| <random>` from `SharedBodySystem.Targeting.cs:129`), and leaving the request unset for a composite is the *correct* behaviour. (c) **D27:** after `RouteThroughBodyModifiers(...)` returns, write the accumulated routed delta into `args.Applied`. |
| **D27 accumulator** (new) | A `// WOLFGATE` side table `private readonly Dictionary<EntityUid, DamageSpecifier> _appliedDelta = new();` cleared in `OnBeforeDamageChanged`'s `finally`. Three write points, all of which already compute the value: `ApplySystemicDamage`'s `applied` local (`:978-996`), `RouteAppliedDamage`'s `out var appliedDamage` (`:704-712`), and `ApplyLocalizedHealing`'s `appliedDamage` (`:921-931`). `RouteThroughBodyModifiers` already returns a bool via `_applied` (`:620-621`); the new table is the same bookkeeping carrying the amount instead of a flag. |
| `:481`, `:613` | `TargetBodyPart.Chest` → `.Torso` (D9). Note `:613`'s defibrillator branch resolves `TargetBodyPart.Chest` → `.Torso`, which `TryResolveAvailable` maps to the torso part. |
| `:545-572` (`TryGetActiveHandPart`) | Rewrite the hands block: Wolfgate's `Hand` is a **class** (`HandsComponent.cs:107`) so `hand.Value.Location` does not compile; `GetActiveHand` returns `Hand?` not `string?`; `HandLocation` has no `FunctionalLeft/FunctionalRight` (`:156-161`). Replacement in `wounds-a.md` §1.3-C. |
| `:466` | `_mobThreshold.CheckVitalDamage(body, damageable)` — supplied by file #3 |
| `:729-730`, `:735`, `:883-885` | `bodyPart.MaxDamage` → `_wfPart.Get(part).MaxDamage`; `part.Parent == null` → `_body.GetParentPartOrNull(parts[i]) is null`; `part.PartType == BodyPartType.Chest` → `.Torso`; `part.AmputationThresholds` → `_wfPart.Get(parts[i]).AmputationThresholds` |
| `:952` | `HasComp<HealOnBuckleComponent>` → `HasComp<WolfmedBedHealMarkerComponent>` |

**`WoundDamageProjectionSystem.cs` — the exact `// WOLFGATE` edit list:**

| Site | Edit |
|---|---|
| `[Dependency]` block | swap to `WolfmedDamageableSystem`; **delete** the `CirculatoryStreamSystem` dependency (D15) |
| `:29` | `after: [typeof(InitialBodySystem)]` → `after: [typeof(SharedBodySystem)]` — `InitialBodySystem` is Nubody glue that does not exist here. Verify empirically with test T-SETUP that `WoundableComponent` lands on every part after map-init. |
| `:31` + `:82-87` | **D11**: `SubscribeLocalEvent<WoundableComponent, DamageDealtEvent>(…, after: [typeof(DamageableSystem)])` → `SubscribeLocalEvent<WoundableComponent, DamageChangedEvent>(OnPartDamageDealt)`; handler signature `ref DamageDealtEvent` → `DamageChangedEvent args` and `if (args.DamageDelta is not { } delta) return;`, using `delta` in place of `args.Damage`. |
| `:37` | delete the `_circulation.SynchronizeStreams(body)` call (dead in phase 1 — `Organic` *is* `PrimaryStream`, so `SynchronizeStreams` never attaches a `CirculatoryStreamComponent` to an organic body) |
| `:40-80` (`OnRejuvenate`) | **D17**: call `_wounds.ClearBodyWounds(body)` first |
| `:112-119` (`GetDetachedRoot`) | `CompOrNull<BodyPartComponent>(root)?.Parent` → `_body.GetParentPartOrNull(root)` |
| `:150-159` (`RefreshBodyDamage`) | No edit, but **read §2.1's `CanBeDamagedBy` note**: this loop deletes systemic damage types the mob's `Biological` container does not support. |
| `:209`, `:215-220` (`SetupPart`) | **D19**: delete the `EnsureComp<InjurableComponent>` block entirely. Keep `EnsureComp<WoundableComponent>`, `EnsureComp<DamageableComponent>`, the `PainComponent` branch and `EnsureComp<BodyPartFunctionalityComponent>`. |
| `:239-240` | `case (BodyPartType.Chest, _)` → `(BodyPartType.Torso, _)`; **delete** the `Groin` case (`HumanoidVisualLayers` has `Chest` at `:15` and no `Groin`) |

**Checkpoint:** both builds green. No behaviour change yet — nothing has `WoundHostComponent` until WP7. Note that with GUARD D2 in place, `TryChangeDamage` behaves identically for every non-wound-host entity (the new members default to null/zero and nothing writes them).

**File count:** 6.

---

### WP6 — Bleeding, circulation system, healing (server half, D13/D14/D15)

**Goal:** wounds bleed, clot, heal, and organ damage rolls.

**Depends on:** WP5.

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | `Content.Shared/_Onyx/Chemistry/Circulation/CirculatoryStreamSystem.cs` | `Content.Server/_Onyx/Chemistry/Circulation/CirculatoryStreamSystem.cs` | **trimmed** (D15) — keep `GetPartStream`, `TryGetPartSolution`, `TryGetStreamSolution`, `SetBleedRates`, primary-stream branches only. Drop `Update`, `SynchronizeStreams`, `GetAttachedStreams`, `ConfigureMetabolizer`, `InitializeStream`, `HasStageConflict`, `RemoveStream`, and all five subscriptions. `SetBleedRates` reduces to one line: `_bloodstream.TryModifyWoundBleedProjection(body, rates.GetValueOrDefault(PrimaryStream) - bloodstream.BleedAmount)`. Namespace stays `Content.Shared._Onyx.Chemistry.Circulation`. |
| 2 | `Content.Shared/_Onyx/Wounds/WoundBleedingSystem.cs` | `Content.Server/_Onyx/Wounds/WoundBleedingSystem.cs` | **moved, otherwise near-verbatim** — `using Content.Shared.Body.Components;` → `using Content.Server.Body.Components;` for `BloodstreamComponent`; `using Content.Shared.Body.Systems;` → `using Content.Server.Body.Systems;` for `BloodstreamSystem`. `DamageSpecifier.GetPositive` comes from §2.2. Keep the dead `using Content.Shared.Body;` at `:3` (§2.17). |
| 3 | `Content.Shared/_Onyx/Wounds/WoundInternalBleedingSystem.cs` | `Content.Server/_Onyx/Wounds/WoundInternalBleedingSystem.cs` | **moved + one edit**, same two `using` swaps, plus a **mandatory** `// WOLFGATE` fix at `:67`: `_bloodstream.TryModifyBloodLevel((body, bloodstream), -amount);` → `_bloodstream.TryModifyBloodLevel(body, -amount, bloodstream);`. Revision 1 said "verify it binds" — **it does not.** Wolfgate's signature is `public bool TryModifyBloodLevel(EntityUid uid, FixedPoint2 amount, BloodstreamComponent? component = null)` (`WG/Content.Server/Body/Systems/BloodstreamSystem.cs:363`), and reaching `EntityUid` from the tuple literal needs **two** user-defined conversions — `(EntityUid, T)` → `Entity<T>` (`RobustToolbox/Robust.Shared/GameObjects/Entity.cs:34`) then `Entity<T>` → `EntityUid` (`:44`) — which C# does not chain. Result: `CS1503`. Every other tuple-literal call site in the wound set targets an `Entity<T>`/`Entity<T?>` parameter, where exactly one conversion applies, so this is the only one; the hands block at `WoundDamageRoutingSystem.cs:545-572` and `FractureEffectsSystem.cs:135-141` are already scheduled for rewrite. |
| 4 | `Content.Shared/_Onyx/Wounds/OrganDamageSystem.cs` | `Content.Server/_Onyx/Wounds/OrganDamageSystem.cs` | **moved + edited** — `using Content.Shared.Body;` → `using Content.Shared.Body.Organ;`; organ health reads go through `WolfmedOrganComponent` (`wounds-c.md` §6.3(b) replacement block); **`// WOLFGATE`-disable both `:24` (`[Dependency] private AmputationSystem _amputation = default!;`) and `:36` (`_amputation.HandlePartDamageApplied(part, ref args);`)** with the same `TODO: phase 3` marker (D26). Commenting out only `:36` leaves `CS0246` on `:24`. |
| 5 | `Content.Shared/_Onyx/Wounds/WoundHealingSystem.cs` | `Content.Server/_Onyx/Wounds/WoundHealingSystem.cs` | **moved + edited** — `using Content.Shared.Medical.Healing;` → `using Content.Server.Medical.Components;` (D14); `[Dependency]` swap to `WolfmedDamageableSystem`; **D31: leave `:44` and `:154` at Onyx's parameter types and convert at `:110` instead**: <br>```// WOLFGATE: Wolfgate's HealingComponent.DamageContainers is List<string>.\nhealing.Comp.DamageContainers?.Select(x => new ProtoId<DamageContainerPrototype>(x)).ToList(),``` <br>(needs `using System.Linq;`). Revision 1's parameter-type change fixed neither `:110` nor `:33`. |
| 6 | `Content.Shared/_Onyx/Body/OrganDamageComponent.cs` | same | **verbatim** (23 lines, only depends on `DamageTypePrototype`) |
| 7 | — | `Content.Shared/_WF/Wolfmed/Body/WolfmedOrganComponent.cs` | new: `Health`, `MaxHealth`, `DestructionWound`, `DestructionWoundSeverity` (Wolfgate's `OrganComponent` has none) |
| 8 | `Content.Shared/_Onyx/Body/Systems/OrganHealthSystem.cs` | `Content.Server/_Onyx/Body/Systems/OrganHealthSystem.cs` | **adapted** — health reads move to `WolfmedOrganComponent`; `TryGetOrganInSlot`/`TryRemoveOrgan` → Wolfgate's `RemoveOrgan` (`SharedBodySystem.Organs.cs:172`); relocate `OrganFunctionChangedEvent` here (`FunctionalOrganComponent` itself is **not** ported) |
| 9 | `Content.Server/Body/Systems/BloodstreamSystem.cs` | same | **upstream, GUARD E + GUARD E3** |
| 10 | `Content.Server/_Mono/Traits/Physical/HemophiliaSystem.cs` | same | **upstream, GUARD E4** |
| 11 | `Content.Server/Medical/Components/HealingComponent.cs` | same | **upstream, HOOK 7** — `HashSet<>` types, not `List<>` |
| 12 | `Content.Server/Medical/HealingSystem.cs` | same | **upstream, HOOK 8** |

**Checkpoint:** both builds green; `dotnet build Content.IntegrationTests` green.

**File count:** 12.

---

### WP7 — Prototypes, mob wiring, locale, textures (parallel group E)

**Goal:** organic humanoids are wound hosts and the content loads with zero errors.

**Depends on:** WP4 (prototype kinds), WP6 (for the behaviour to be complete).

| # | Onyx source | Wolfgate destination | Status |
|---|---|---|---|
| 1 | `Resources/Prototypes/_Onyx/Wounds/wounds.yml` | same | **edited** — keep `OrganicBodyPartProfile`, `OrganicFractureProfile` and the 12 organic wounds (`BluntWound`, `SlashWound`, `PiercingWound`, `BurnWound`, `ElectricalWound`, `BoneFractureWound`, `SurgicalIncisionWound`, `DismembermentWound`, `AmputationConsequenceWound`, `InternalBleedingWound`, `MedicalScarWound`, `SystemicBleedingWound`). Drop the Ipc/Slime/Plant/Cybernetic profiles and their 11 wounds (phase 5). In `organDamage.chances` (`:26-34`), **merge `Chest: 0.04` and `Groin: 0.04` into `Torso: 0.04`** (do not sum — they are independent per-part rolls, and Wolfgate has one torso). `OrganicBodyPartProfile.acceptedDamageTypes` keeps `Caustic` (D20 reversed). `BurnWound`'s `Caustic` entry (`:488-490`) stays. |
| 2 | `Resources/Locale/en-US/_Onyx/prototypes/wounds/wounds.ftl` | same | **verbatim** (43 keys, complete — nothing missing) |
| 3 | `Resources/Locale/en-US/_Onyx/medical/fractures.ftl` | same | **verbatim** (`alerts-broken-bones-name`/`-desc`) |
| 4 | `Resources/Prototypes/_Onyx/Alerts/alerts.yml` | same | **edited** — the `BrokenBones` entry **only**; drop `ModsuitPower`, `Centered`, `HierophantBeat`, `DragonPower`, `SneakAttack`, `LossOfSurprise`. (`wounds.yml` references exactly one external prototype, `alert: BrokenBones` at `:164`, plus the five `!type:Wound*Behavior` tags supplied by WP4.) |
| 5 | `Resources/Textures/_Onyx/Interface/Alerts/fracture.rsi` | same | **verbatim** — `CC-BY-SA-3.0`, "Taken from tgstation, redrawn by darkrell". Copy `meta.json` unchanged. |
| 6 | — | `Resources/Prototypes/_WF/Wolfmed/Body/parts.yml` | new — `WolfmedBodyPart` on the abstract limb bases with Onyx's numbers. **Onyx's `Chest` row (`fractureProfile: OrganicFractureProfile`, `maxDamage: 250`, no thresholds) maps to Wolfgate's `BaseTorso`; Onyx's `Groin` row is dropped.** Full YAML in `body-organ.md` §5.2. **Duplicate-id caveat:** if re-declaring an existing abstract id in a second file errors, instead declare `WolfmedBase<Part>` abstracts here and add them to each `Base<Part>`'s `parent:` list with a `// WOLFGATE` comment. |
| 7 | — | `Resources/Prototypes/Entities/Mobs/Species/base.yml` | **upstream, D21 + D22 + D29** — see the block below |

**The `BaseMobSpeciesOrganic` block (WP7 #7).** RT's prototype loader does not merge component lists across files by id, so this is an in-place edit of `BaseMobSpeciesOrganic`'s `components:` list (`WG/Resources/Prototypes/Entities/Mobs/Species/base.yml:247+`):

```yaml
  # WOLFGATE - Wolfmed phase 1
  - type: WoundHost                       # D20: localizedDamageTypes left at Onyx's default, Caustic included
  - type: Destructible                    # D22: part damage now projects onto the mob
    thresholds:
    - trigger:
        !type:DamageTypeTrigger
        damageType: Blunt
        damage: 1500
      behaviors:
      - !type:GibBehavior { }
  - type: PassiveDamage                   # D29: Onyx models passive recovery per part profile; layering
    allowedStates: [ Alive ]              #      Wolfgate's body-level regen on top double-heals and
    damageCap: 20                         #      re-interprets damageCap as the projection total.
    damage: {}
```

**Non-organic descendants must be excluded.** `BaseMobSpeciesOrganic` has **18 direct children** across five fork directories (verified by grep in revision 2): `arachnid`, `diona`, `dwarf`, `gingerbread`, `human`, `moth`, `reptilian`, `slime`, `vox` (base) + `chitinid`, `feroxi`, `rodentia`, `vulpkanin` (`_DV`) + `tajaran`, `yowie` (`_Goobstation`) + `asakim`, `protogen` (`_Mono`) + `hydrakin` (`_Obelisk`), plus whatever inherits from those. At least one is synthetic: `WG/Resources/Prototypes/_Mono/Entities/Mobs/Species/protogen.yml:2` parents `BaseMobSpeciesOrganic` and `:73-75` gives it `prototype: SiliconDeathgasp`. **WP7 must re-enumerate the descendant list at implementation time** (forks add species) and add a `- type: WoundHost` removal — or a per-species profile override — for every entry §8.1 item 1 resolves as non-organic.

**Acceptance check for `parts.yml` coverage (m6).** `WolfmedBodyPartComponent.MaxDamage` defaults to zero and zero **disables amputation overflow entirely** (§2.12). Every limb abstract must have a row. The abstracts are split across two files, not one: `WG/Resources/Prototypes/Body/Parts/base.yml` — `BasePartInorganic:9`, `BaseTorsoInorganic:35`, `BaseHead:81`, `BaseLeftArm:137`, `BaseRightArm:156`, `BaseLeftHand:176`, `BaseRightHand:191`, `BaseLeftLeg:206`, `BaseRightLeg:226`, `BaseLeftFoot:246`, `BaseRightFoot:261` — and `WG/Resources/Prototypes/_Shitmed/Body/Parts/base.yml` — `BasePart:5`, `BaseTorso:51`. (Revision 1 and the critique both pointed only at the `_Shitmed` directory, which holds just two of the thirteen.)

**Textures deliberately not ported:** `Resources/Textures/_Onyx/Wounds/{brute,burn}_damage.rsi` (156 files). They are cosmetic re-skins of sprites Wolfgate already ships at `Textures/Mobs/Effects/`; `wounds.yml` has zero texture references. Licensing is clean (`CC-BY-SA-3.0`, Ubaser / Citadel-Station-13) if they are wanted later.

**Checkpoint:** `dotnet build` green; **YAML lints in Release**; server starts with zero prototype errors; a spawned `MobHuman` has `WoundHostComponent` and every part has `WoundableComponent` after map-init.

**File count:** 7.

---

### WP8 — Bridge hardening: AP passthrough, armour, thresholds, execution

**Goal:** armour penetration, vital-damage thresholds and executions behave correctly for wound hosts.

**Depends on:** WP5, WP6. (GUARD D2 itself moved to WP5 — see D27.)

| # | File | Change |
|---|---|---|
| 1 | `Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs` | **D23** — `// WOLFGATE` side table `Dictionary<EntityUid, (float Ap, EntityUid? Tool, DamageOriginFlag? Flag)>` filled in `OnBeforeDamageChanged`, cleared in the `finally`, and read at `:621`'s `ChangeDamage` call |
| 2 | `Content.Shared/Armor/SharedArmorSystem.cs` | **HOOK 10** |
| 3 | `Content.Shared/Mobs/Systems/MobThresholdSystem.cs` | **HOOK 11** (two sites: `:340` in `CheckThresholds`, `:406` in `UpdateAlerts`) |
| 4 | `Content.Server/Medical/DefibrillatorSystem.cs` | **HOOK 12** (`:209-211`) |
| 5 | `Content.Shared/Execution/SharedExecutionSystem.cs` | **HOOK 13** (`:221`) |

**Checkpoint:** both builds green; tests T-AP, T-RESULT and T-PIERCE (§6) pass.

**File count:** 5.

---

### WP9 — Tests

See §6 for the full plan. Files land in `WG/Content.IntegrationTests/Tests/_Onyx/Wounds/` (6 ported files) and `.../Tests/_WF/Wolfmed/` (1 new). Starts with WP7/WP8; green only after both land.

**File count:** 7.

---

### WP10 / WP11 — later phases (listed so the manifest is complete, not scoped here)

- **WP10 (phase 2 — fractures, pain HUD, statuses):** `FractureEffectsSystem.cs` (hands block rewritten, `OrganGot*` bridge from §2.11, subscribe Wolfgate's existing `GetDoAfterDelayMultiplierEvent` instead of adding Onyx's), `FractureAlertSystem.cs`, `MovementModStatusEffectComponent` + trimmed `MovementModStatusSystem`, `StatusEffectSlowdown` prototype chain, `_Onyx/StatusEffects/wounds.yml`, the pain damage overlay, `EmoteOnDamage` pain sounds, GUARD E2 + `_Onyx/HealthExaminable`.
- **WP11 (phases 3–4 — amputation, organs, treatment, UI):** `AmputationSystem.cs` (29 edits per `body-organ.md` §2.6 + reconciling Shitmed's `DropPart` cascade with GUARD B; re-enable OrganDamageSystem `:24`/`:36`), organ prototypes, explosion wound distribution (note the Mono `DamageOriginFlag.Explosion` plate-protection gap), `Tourniquet` (verbatim, server-side per D13), `MedicalPatch` (verbatim, zero wound dependency — could land any time), `GroupHealSpecifier` (verbatim; **preserve its Wega GPL-3.0 header and record the second license in the manifest**), the four entity effects (D16) + HOOK 9 on **both** `HealthChange` and `EvenHealthChange` (+ `DistributedHealthChange` if needed), wound surgeries on Shitmed steps (D7; reuse Wolfgate's existing `SurgeryOpenIncision`/`SurgeryCloseIncision`/`BoneGel`/`BoneSetter`, rename the two colliding components), health-analyzer wound diagnostics.

---

## 5. Duplicate directed subscription audit

RT stores **one** registration per `(component, event)` pair for the whole bus, not per system (`EntityEventBus.Directed.cs:407,418-419`). Every pair the port registers. **All deferred checks from revision 1 have been run; §5.3 no longer contains questions.**

### 5.1 Pairs with a confirmed Wolfgate conflict

| Pair | Port registrant | Wolfgate owner | Resolution |
|---|---|---|---|
| `BodyComponent`, `RejuvenateEvent` | `WoundSystem.cs:34` | `WG/Content.Server/_Mono/Body/Systems/BodyRejuvenateSystem.cs:28` | **D17** — delete the port's subscription; `WoundDamageProjectionSystem.OnRejuvenate` (which owns `<WoundHostComponent, RejuvenateEvent>`) calls `WoundSystem.ClearBodyWounds` first. No upstream edit. |
| `BodyComponent`, `BodyPartAddedEvent` | `WolfmedBodyPartLifecycleSystem` (wanted) | `WG/Content.Shared/_Shitmed/Body/Systems/SharedBodySystem.PartAppearance.cs:25` | **D28** — subscribe `<WoundHostComponent, BodyPartAddedEvent>` instead. Both events are raised on the **body only** (`SharedBodySystem.Parts.cs:337-338`, `:357-358`), and the body carries `WoundHostComponent`, so the wound-host-scoped pair is both free and correctly scoped. |
| `BodyComponent`, `BodyPartRemovedEvent` | `WolfmedBodyPartLifecycleSystem` (wanted) | same, `:26` | **D28** — as above, `<WoundHostComponent, BodyPartRemovedEvent>`. |

Those are the **three** confirmed collisions with existing Wolfgate code. Every other component the port subscribes is new.

### 5.2 Pairs the port registers (all new components unless noted)

| Component | Event | Registrant | Free? |
|---|---|---|---|
| `WoundHostComponent` | `BeforeDamageChangedEvent` | `WoundDamageRoutingSystem:50` | yes — Wolfgate's only three `BeforeDamageChangedEvent` subscribers are `GodmodeComponent` (`SharedGodmodeSystem.cs:19`), `InsideStasisComponent` (`SharedStasisSystem.cs:52`), `ArmorPlateProtectedComponent` (`SharedArmorPlateSystem.cs:46`) |
| `WoundableComponent` | `BeforeDamageChangedEvent` | `WoundDamageRoutingSystem:52` | yes |
| `WoundHostComponent` | `DamageDealtEvent` | `WoundDamageRoutingSystem:51` | yes — the event is new |
| `WoundableComponent` | `DamageChangedEvent` | `WoundDamageProjectionSystem` (after D11) | yes — Shitmed owns `<BodyPartComponent, DamageChangedEvent>` (`SharedBodySystem.Targeting.cs:70`), a different component. Both fire on the same part in undefined order; GUARD B is what keeps that safe. **Claimed exclusively; nothing in `_WF` may also take it.** |
| `WoundHostComponent` | `MapInitEvent` | `WoundDamageProjectionSystem:29` | yes |
| `WoundHostComponent` | `RejuvenateEvent` | `WoundDamageProjectionSystem:30` | yes |
| `WoundHostComponent` | `BodyPartAddedEvent` / `BodyPartRemovedEvent` | `WolfmedBodyPartLifecycleSystem` (D28) | yes — verified, only `BodyComponent`, `HandsComponent`, `MantisBladeArmComponent` and `CorticalBorerInfestedComponent` hold those events today |
| `OrganComponent` | `OrganAddedToBodyEvent` / `OrganRemovedFromBodyEvent` | `WolfmedBodyPartLifecycleSystem` (D28) | yes — Wolfgate raises both **on the organ** (`SharedBodySystem.Organs.cs:46,68`) |
| `HealOnBuckleComponent` | `ComponentStartup` | `WolfmedBedHealMarkerSystem` (§2.10) | **yes — verified.** Wolfgate's only subscriptions on that component are `StrappedEvent`/`UnstrappedEvent` (`WG/Content.Server/Bed/BedSystem.cs:35-36`) |
| `WoundableComponent` | `RejuvenateEvent` | `WoundSystem:35` | yes |
| `WoundableComponent` | `ComponentInit` | `WoundSystem:33` | yes |
| `WoundableComponent` | `PartDamageAppliedEvent` | `OrganDamageSystem` | yes — **the only subscriber to this pair in the whole wound set.** `WoundSystem`, `WoundFractureSystem`, `AmputationSystem` and `WoundBleedingSystem` each expose a `HandlePartDamageApplied` that `OrganDamageSystem` calls in a fixed order (`OrganDamageSystem.cs:33-37`). **Do not convert any of them to their own subscription.** |
| `WoundableComponent` | `PartDamageOverflowedEvent` | `AmputationSystem` (phase 3) | yes |
| `WoundableComponent` | `WoundCreatedEvent` / `WoundChangedEvent` / `WoundStateChangedEvent` / `WoundRemovedEvent` | `WoundStatusEffectSystem:33-36` | yes |
| `WoundBleedingComponent` | `ComponentInit`, `ComponentShutdown`, `WoundCreatedEvent`, `WoundChangedEvent`, `WoundStateChangedEvent`, `WoundRemovedEvent` | `WoundBleedingSystem:38-43` | yes |
| `WoundInternalBleedingComponent` | `WoundChangedEvent`, `WoundStateChangedEvent`, `WoundRemovedEvent` | `WoundInternalBleedingSystem:20-22` | yes |
| `WoundComponent` | `WoundStateChangedEvent` | `WoundScarSystem:23` | yes |
| `WoundScarComponent` | `WoundTreatmentAttemptEvent` | `WoundScarSystem:24` | yes |
| `WoundFractureComponent` | `WoundChangedEvent` | `WoundFractureSystem` | yes |
| `WoundHostComponent` | `SleepStateChangedEvent` | `WoundBleedingSystem:44` | yes — Wolfgate's existing subscriber is `<MobStateComponent, …>` (`SleepingSystem.cs:50`) |
| `WoundHostComponent` | `ResolveHealingPartEvent` | `WoundHealingSystem:28` | yes |
| `PainComponent` | `RejuvenateEvent` | `PainSystem:42` | yes |
| `PainShockTargetComponent` | `ComponentStartup` | `PainSystem` | yes |
| `StatusEffectContainerComponent` / `StatusEffectComponent` / `RejuvenateRemovedStatusEffectComponent` / `ExaminableStatusEffectComponent` / `PermanentStatusEffectsComponent` | WP1's relay + lifecycle events | `StatusEffectNew` systems | yes — all seven components are new; registered names `StatusEffect`, `StatusEffectContainer`, `StatusEffectAlert`, `CloneableStatusEffect`, `ExaminableStatusEffect`, `PermanentStatusEffects`, `RejuvenateRemovedStatusEffect` all verified absent from Wolfgate code and YAML. `PainNumbnessStatusEffect` (WP1 #17) likewise — Wolfgate's existing component registers as `PainNumbness`. |
| `WoundHostComponent` | `RefreshMovementSpeedModifiersEvent`, `GetManipulationDurationMultiplierEvent` | `FractureEffectsSystem` (phase 2) | yes |
| `WoundableComponent` | `OrganGotInsertedEvent` / `OrganGotRemovedEvent` / `BodyPartFunctionalityChangedEvent` | `FractureEffectsSystem` (phase 2) | yes |

### 5.3 Component-registration names — no collisions

All 14 wound components (`WoundHostComponent`, `PartDamageVisualsComponent`, `PainComponent`, `PainShockTargetComponent`, `WoundableComponent`, `BodyPartFunctionalityComponent`, `WoundComponent`, `WoundFunctionalityComponent`, `WoundBleedingComponent`, `WoundInternalBleedingComponent`, `WoundFractureComponent`, `WoundScarComponent`, `SystemicDamageComponent`, `OrganDamageComponent`) are absent from Wolfgate C# and YAML. All 14 live in one shared file (`WoundDamageComponents.cs:15-300`), so D13's move of four *systems* to `Content.Server` does not strand a component on the server. D8/D10's are the only registration collisions in the whole port.

CCVars likewise: `targeting.enabled`, `targeting.use_anatomical_odds`, `targeting.downed_targets_are_exact`, `WoundsBodyPartFunctionalityEnabled`, `WoundsBleeding*`, `ExplosionLimbDamageVariation`, `ExplosionWoundMultiplier` all return zero hits across `WG/Content.Shared` + `WG/Content.Server`.

### 5.4 Ordering constraints (not duplicates, but crash-adjacent)

- `WoundDamageRoutingSystem` subscribes `BeforeDamageChangedEvent` twice (`:50`, `:52`). RT requires a system subscribing the same event twice to use **identical** before/after sets — apply `before: [typeof(SharedArmorPlateSystem)]` to both or neither.
- `before:`/`after:` on an **abstract** system type compiles and silently does nothing (RT keys on `GetType()`). `SharedGodmodeSystem` and `SharedStasisSystem` are abstract, so ordering against them is impossible from shared code — the `if (args.Cancelled) return;` guard is the order-independent fix. `SharedArmorPlateSystem` is `sealed` and shared, so ordering against it works.
- Unknown ordering types do **not** throw (`EntityEventBus.Ordering.cs:100` passes `allowMissing: true`), so a stale `before: [typeof(DamageableSystem)]` is inert, not fatal — but it also buys nothing.
- **Armour is not double-applied on the part.** The routed part write uses `ignoreResistances: true` (`WoundDamageRoutingSystem.cs:704-710`), and Wolfgate raises `DamageModifyEvent` only inside `if (!ignoreResistances)` (`DamageableSystem.cs:227-237`), so Shitmed's `<BodyPartComponent, DamageModifyEvent>` (`SharedBodySystem.Targeting.cs:69`) does not fire on the routed pass.

---

## 6. Test plan (WP9)

**Location:** `WG/Content.IntegrationTests/Tests/_Onyx/Wounds/` (ported) and `WG/Content.IntegrationTests/Tests/_WF/Wolfmed/` (new).
**Harness:** Wolfgate already has the same `GameTest` fixture Onyx's tests use (`WG/Content.IntegrationTests/Fixtures/GameTest.cs` + `GameTest.{Pair,Entities,CVars,CommonPoolSettings}.cs` + `Fixtures/Attributes/`), so the harness pattern needs no change.
**Before blaming Wolfmed for a failure:** run `DockTest` first. The `db.ef` sqlite warnings fail every pair test in this repo (project memory). Lint YAML in Release; `ErrorNode` crashes the linter.

### 6.1 Onyx tests to port

| Onyx test | Port? | Fixture adaptations |
|---|---|---|
| `WoundDamageFoundationTest.cs` (718 lines, 10 tests) | **Yes — the primary contract test.** | `Chest` → `Torso` everywhere (D9); drop `- type: Injurable` from the 10 `[TestPrototypes]`; replace `TargetingComponent.DefaultOdds()` (Onyx's nested-dict shape) with Shitmed's flat `TargetOdds` dictionary; `graph.TryDetachPart` (`:318`) → `_wfBody.TryDetachPart`; every `_damage.GetAllDamage(x)` / `GetPositiveDamage` goes through `WolfmedDamageableSystem`; body prototype gets Shitmed's `Body`/`BodyPart` shape instead of Nubody's `OrganBase*`. |
| `WoundBleedingTest.cs` (6 tests) | **Yes, minus the tourniquet test.** | `using Content.Shared.Medical.Healing;` → `Content.Server.Medical.Components`; `BloodstreamComponent`/`BloodstreamSystem` resolve from `Content.Server.Body.*`. **Skip the `Tourniquet` test** — no such prototype exists until WP11. |
| `WoundScarTest.cs` (1 test) | **Yes — highest portability, pure wound API.** | none beyond the namespace fix |
| `WoundFractureTest.cs` (3 tests) | **Yes.** | `FractureProfile` reads move to `WolfmedBodyPartComponent`; `FractureEffectSystem.GetDurationMultiplier` assertions are phase 2 — split that test out. |
| `BodyConsequencesTest.cs` (3 tests) | **Yes — near-direct port, zero wound dependencies.** Doubles as a signature-compatibility check for `SharedBodySystem`. | `TryDetachPart` → `WolfmedBodySystem.TryDetachPart`; the Groin-detach test becomes a Torso-detach test (D9); `StandUpAttemptEvent` does not exist in Wolfgate — drop that assertion. |
| `WoundHealingTest.cs` (5 tests) | **Partially.** Port 4; skip the `ResolveRepairPartEvent`/`ValidateRepairPartEvent` test. | Same namespace fix; the skipped test needs `Content.Shared._Onyx.Repairable` (4 files, outside the sparse checkout, not in scope). |
| `HealthAnalyzerPartDamageTest.cs` | **Defer to WP11.** | — |
| `WoundSurgeryTest.cs`, `WoundSurgeryScarTest.cs`, `SurgeryStepSequencePrototypeTest.cs`, `TransplantCompatibilityPrototypeTest.cs` | **No — do not port.** | They exercise Onyx's own surgery / transplant framework, excluded by D7 and D8. Their *scenarios* are worth re-deriving against Shitmed steps in WP11. |

### 6.2 New Wolfgate bridge test — `WolfmedDamageBridgeTest.cs`

`WG/Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedDamageBridgeTest.cs`. Two `[TestPrototypes]` entities: `WolfmedBridgeBody` (WoundHost + Targeting + Damageable + MobState + MobThresholds + Shitmed body) and `WolfmedControlBody` (identical **minus** `WoundHost`) — the control is what proves D2's "entities without `WoundHostComponent` behave exactly as today".

| # | Test | Assertion |
|---|---|---|
| **T1** | `WoundHostRoutesToTargetedPart` | Attacker with `TargetingComponent.Target = LeftArm`; one `TryChangeDamage(body, {Blunt:10}, origin: attacker)`. `Comp<DamageableComponent>(leftArm).TotalDamage == 10`, every other part `0`, `Comp<DamageableComponent>(body).TotalDamage == 10` (the projection). |
| **T2** | `WoundHostCreatesWoundOnPart` | After T1, `WoundableComponent.WoundsContainer` on the arm holds exactly one entity with `WoundComponent.Prototype == "BluntWound"` and `Severity > 0`. |
| **T3** | `ExplicitTargetPartIsHonoured` | `TryChangeDamage(body, {Slash:10}, targetPart: TargetBodyPart.Head)` → head 10, arms 0. Proves the WP5 `targetPart` handoff. |
| **T4** | `NoDoubleApplication` | One `TryChangeDamage(body, {Blunt:10})`; assert `Σ(part.TotalDamage) + SystemicDamage.GetTotal() == 10` **and** `body.TotalDamage == 10`. A Shitmed+Onyx double-apply reads as 20, or 10-on-part *and* 10-on-body. |
| **T5** | `NonWoundHostUnchanged` | Same attack on `WolfmedControlBody` → `leftArm.TotalDamage == 10 * 0.7` (`GetPartDamageModifier(Arm)`, `SharedBodySystem.Targeting.cs:432`), `body.TotalDamage == 10`, `!HasComp<WoundableComponent>(leftArm)`. |
| **T6** | `SystemicDamageStaysOnBody` | `TryChangeDamage(body, {Asphyxiation:5})` → every part 0, `SystemicDamageComponent.Damage.GetTotal() == 5`, `body.TotalDamage == 5`. **Then call `RefreshBodyDamage` a second time and re-assert** — the second pass is what catches a `CanBeDamagedBy` container regression, because `RefreshBodyDamage` *deletes* unsupported systemic types (§2.1). |
| **T7** | `NoSeverAtOneThirty` | Drive one arm past `SeverIntegrity = 130` while `!Enabled`; assert `_body.BodyHasChild(body, leftArm)` — GUARD B holds and Onyx's overflow is the only dismemberment path. |
| **T8** | `NoShitmedRegen` | Set a part to 40 damage, advance `HealingTime + 1` s (30 s, `BodyPartComponent.cs:87`); assert unchanged on the wound host and **regenerated** on the control (GUARD C). |
| **T9** | `PartStillDisablesAtNinety` | Drive an arm to ≥90 → `BodyPartComponent.Enabled == false` and `TargetingComponent.BodyStatus[LeftArm] != Healthy`. Proves `CheckBodyPart` was deliberately left running. |
| **T10** | `GodmodeStillBlocks` | `EnableGodmode(body)`, hit it: all parts 0, body 0, wounds container empty, **and `TryChangeDamage` returns `null`** (D27's `Applied` stays unset for godmode). |
| **T11** | `ArmorPlateAbsorbsOnce` | Equip an `ArmorPlateProtected` mob with a plate of known durability; one hit; plate durability drops by exactly one hit's worth. Catches the `before: [typeof(SharedArmorPlateSystem)]` ordering bug. |
| **T12** | `DamageChangedDeltaSurvivesProjection` | Probe subscribing `DamageChangedEvent` on the body; one hit; assert `DamageDelta != null && DamageIncreased`. Catches the GUARD F silent-death class that would kill bleeding, hemophilia, wake-on-damage, forced say, kill attribution, NPC aggro and ignite-on-heat. |
| **T13** | `MobThresholdsStillTrigger` | Pile damage across parts past crit then past death; assert `Critical` then `Dead`. Proves the projection feeds `MobThresholdSystem`. |
| **T14** | `VitalPartRemovalStillKills` | Remove the vital part; `PartRemoveDamage` applies 100 `Bloodloss` (`SharedBodySystem.Parts.cs:406`); assert it landed in `SystemicDamageComponent` and the mob died. |
| **T15** | `RejuvenateClearsEverything` | Damage parts, create wounds, raise `RejuvenateEvent`; assert all part damage 0, systemic empty, no `WoundComponent` entities, `body.TotalDamage == 0`, `MobState == Alive`. Covers the D17 merge. |
| **T16** | `NoRecursionUnderLoad` | 200 sequential alternating damage/heal hits; no stack overflow, no `Debug.Assert` trip, consistent totals. Canary for the `_routing`/`_projecting` guards. |
| **T-AP** | `ArmorPenetrationReachesWoundHosts` | Equip armour with a known `Blunt` modifier; hit the wound host with `armorPenetration: 1.0` and with `0.0`; assert the penetrating hit lands strictly more damage on the part. **This is the gate for D23.** |
| **T-RESULT** | `TryChangeDamageReportsRoutedDamage` | One melee light attack on `WolfmedBridgeBody` must return a **non-null, non-empty** `DamageSpecifier` whose total equals the damage that landed, and a `Blunt` melee hit must still produce stamina damage (`SharedMeleeWeaponSystem.cs:586-590`). **Gate for D27.** |
| **T-PIERCE** | `PiercingHitscanDamagesEntitiesBehindAWoundHost` | A hitscan whose `args.HitEntities` holds a wound host first and a second entity behind it must damage **both**. Directly exercises the `if (damageDealt == null) return;` inside the loop at `HitscanBasicDamageSystem.cs:33-34`. |
| **T-SETUP** | `EveryPartGetsWoundableOnMapInit` | Spawn `MobHuman`; assert every entry of `_body.GetBodyChildren(body)` has `WoundableComponent` and a `DamageableComponent`. Gates the `after: [typeof(SharedBodySystem)]` ordering change in WP5. **Also assert** that after one `RefreshBodyDamage`, `Comp<DamageableComponent>(body).Damage.DamageDict.Count` still equals the `Biological` container's supported-type count — the D30 zeroing invariant. |
| **T-CAUSTIC** | `CausticRoutesToTheHitPart` | `TryChangeDamage(body, {Caustic:10}, targetPart: LeftArm)` → the arm holds 10 Caustic **and** a `BurnWound`; systemic empty. Locks in D20's reversal: `OrganicPart` supports the `Burn` group, `Burn` contains `Caustic`, and `BurnWound.damageTypes` has a `Caustic` entry. Re-check this test whenever `localizedDamageTypes`, `OrganicPart` or the `Burn` group changes. |
| **T-PASSIVE** | `NoBodyLevelPassiveRegenOnWoundHosts` | A wound host with 10 Blunt on one arm and no treatment still has 10 Blunt on that arm after 60 s of simulated time. **Gate for D29** — without it, `PassiveDamageSystem` heals the limb through the router every tick. |

---

## 7. `Docs/Wolfmed/WOLFMED_MANIFEST.md`

### 7.1 Format

```markdown
# Wolfmed port manifest

**Onyx commit:** `2f5bab9946539cbe083010c9ae6fbc59b47ae377` (Space-Onyx/space-onyx-14 master)
**Last re-sync:** <date> by <agent/WP>

Status values: `verbatim` (byte-identical), `modified` (vendored `_Onyx` file with `// WOLFGATE` edits),
`adapted` (vendored but relocated and/or restructured), `new` (Wolfgate-authored `_WF` code or an upstream
`// WOLFGATE` hook), `skipped` (deliberately not ported).

| Onyx path | Wolfgate path | Status | WP | Notes |
|---|---|---|---|---|
```

One row per file. Every `skipped` row must say *why*, so a future re-sync does not re-litigate it. Upstream hooks use `—` for the Onyx path.

### 7.2 Initial rows

| Onyx path | Wolfgate path | Status | WP | Notes |
|---|---|---|---|---|
| `Content.Shared/StatusEffectNew/Components/*.cs` (7) | same | verbatim | WP1 | `CloneableStatusEffectComponent.cs` has a UTF-8 BOM — preserve it |
| `Content.Shared/StatusEffectNew/StatusEffectsSystem.cs` | same | verbatim | WP1 | needs the `ProtoMan` + `EntityPrototype.TryComp` compat files |
| `Content.Shared/StatusEffectNew/StatusEffectSystem.API.cs` | same | verbatim | WP1 | keeps Onyx's three unused usings on purpose |
| `Content.Shared/StatusEffectNew/StatusEffectSystem.Relay.cs` | same | modified | WP1 | 1 using removed, 1 added, 11 relay lines dropped |
| `Content.Shared/StatusEffectNew/StatusEffectAlertSystem.cs` | same | verbatim | WP1 | needs `AlertsSystem.UpdateAlert` compat |
| `Content.Shared/StatusEffectNew/ExaminableStatusEffectSystem.cs` | same | modified | WP1 | `[SubscribeLocalEvent]` → explicit `Initialize()` |
| `Content.Shared/StatusEffectNew/PermanentStatusEffectsSystem.cs` | same | modified | WP1 | same, ×3 |
| `Content.Shared/Traits/Assorted/PainNumbnessStatusEffectComponent.cs` | same | verbatim | WP1 | required by `PainSystem.cs:323`; registers as `PainNumbnessStatusEffect`, no clash with Wolfgate's `PainNumbness` |
| `Resources/Prototypes/Entities/categories.yml:31-34` | `Resources/Prototypes/_Onyx/Entities/categories.yml` | adapted | WP1 | only the `StatusEffects` entityCategory |
| `Resources/Locale/en-US/entity-categories.ftl:7` | `Resources/Locale/en-US/_Onyx/entity-categories.ftl` | adapted | WP1 | one key |
| `Resources/Prototypes/Entities/StatusEffects/misc.yml` | same | adapted | WP1 | only the 3–4 abstract bases |
| `Resources/Prototypes/Entities/StatusEffects/{body,clumsy,damage,speech,traits,weather}.yml` | — | skipped | — | non-wound status effects; each needs components we do not port |
| `Resources/Prototypes/_Onyx/StatusEffects/surgery.yml` | — | skipped | — | needs `RaspyAccent`; D7 excludes Onyx surgery |
| — | `Content.Shared/_WF/Wolfmed/Compat/StatusEffectsSystem.Wolfgate.cs` | new | WP1 | supplies `ProtoMan`; delete on an RT upgrade that adds it |
| — | `Content.Shared/_WF/Wolfmed/Compat/EntityPrototypeCompatExtensions.cs` | new | WP1 | RT 277 names it `TryGetComponent` |
| — | `Content.Shared/_WF/Wolfmed/Compat/AlertsSystem.UpdateAlert.cs` | new | WP1 | partial, not extension; `AlertState.Cooldown` is an unnamed tuple here |
| — | `Content.Shared/_WF/Wolfmed/Compat/WolfmedDamageableSystem.cs` | new | WP2 | D12 facade; **D30** zeroing `SetDamage`; `CanBeDamagedBy` re-based onto `DamageableComponent` |
| — | `Content.Shared/_WF/Wolfmed/Compat/DamageSpecifier.Wolfmed.cs` | new | WP2 | `Clone`, `GetPositive`, `GetNegative` |
| — | `Content.Shared/_WF/Wolfmed/Compat/DamageDealtEvent.cs` | new | WP2 | non-`readonly` unlike Onyx's — deliberate |
| — | `Content.Shared/_WF/Wolfmed/Compat/WolfmedBodySystem.cs` | new | WP2 | `TryDetachPart` via `AmputateAttemptEvent`; `reparent` ignored |
| — | `Content.Shared/_WF/Wolfmed/Compat/StunSystemOnyxCompat.cs` | new | WP2 | `TryParalyze(refresh: false)`; re-triggers stun VFX, unlike Onyx |
| — | `Content.Shared/_WF/Wolfmed/Compat/SharedChatSystem.Wolfmed.cs` | new | WP2 | `virtual void`; paired with HOOK 6 at `ChatSystem.Emote.cs:60` |
| — | `Content.Shared/_WF/Wolfmed/Compat/WolfmedBedHealMarkerComponent.cs` + `Content.Server/.../WolfmedBedHealMarkerSystem.cs` | new | WP2 | `HealOnBuckleComponent` is server-only here |
| — | `Content.Shared/_WF/Wolfmed/Compat/OnyxBodyEvents.cs` | new | WP2 | event declarations only |
| — | `Content.Server/_WF/Wolfmed/WolfmedBodyPartLifecycleSystem.cs` | new | WP5 | **D28** — one system, `<WoundHostComponent, BodyPart*Event>`; drives `OnPartInserted`/`OnPartRemoved` and fans `OrganGot*` over the subtree |
| — | `Content.Shared/_Shitmed/Targeting/SharedTargetingSystem.cs` | new (hook) | WP2 | HOOK 5, 3-line static `IsSelectable` |
| `Content.Shared/_Onyx/Chemistry/Circulation/CirculatoryStreamComponent.cs` | same | verbatim | WP3 | |
| `Content.Shared/_Onyx/Chemistry/Circulation/CirculatoryStreamPrototype.cs` | same | modified | WP3 | `MetabolismStage`/`MetabolitesStage` commented out; phase-5 re-sync point |
| `Content.Shared/_Onyx/Chemistry/Circulation/SharedSolutionContainerSystem.CirculatoryStreams.cs` | — | skipped | — | only called from `InitializeStream`/`RemoveStream`, both dropped by D15 |
| `Content.Shared/_Onyx/Chemistry/Circulation/CirculatoryStreamSystem.cs` | `Content.Server/_Onyx/Chemistry/Circulation/CirculatoryStreamSystem.cs` | adapted | WP6 | D15: server-side, primary-stream paths only, all 5 subscriptions dropped |
| `Content.Shared/_Onyx/CCVar/{CCVars.Wounds,CCVars.Surgery,CCVars.Targeting}.cs` | same | verbatim | WP3 | zero name and zero cvar-string collisions |
| `Content.Shared/_Onyx/Targeting/DamageDistribution.cs` | same | verbatim | WP3 | |
| `Content.Shared/_Onyx/Targeting/TargetingSnapshotComponent.cs` | same | modified | WP3 | `_Shitmed` enum; default `.Chest` → `.Torso` |
| `Content.Shared/_Onyx/Targeting/TargetingSnapshotSystem.cs` | same | modified | WP3 | `_Shitmed` enum; uses HOOK 5's `IsSelectable` |
| `Content.Shared/_Onyx/Targeting/{TargetBodyPart,TargetingComponent,SharedTargetingSystem,TargetResolverSystem}.cs` | — | skipped | — | D10: `TargetingComponent` registers as `"Targeting"` (crash); the resolver needs `BodyPartType.Chest`/`Groin` (D9) |
| `Content.{Server,Client}/_Onyx/Targeting/**`, `_Onyx/Targeting/PartStatus*.cs` | — | skipped | — | D10; Shitmed's `TargetIntegrity` doll stays. Revisit in phase 3/4 |
| `Content.Shared/_Onyx/Wounds/WoundEvents.cs` | same | verbatim | WP4 | `ResolveHealingPartEvent` keeps Onyx's `ProtoId`/`IReadOnlySet` types (D31) |
| `Content.Shared/_Onyx/Wounds/WoundBehaviors.cs` | same | verbatim | WP4 | needs `StatusEffectComponent` (D1) |
| `Content.Shared/_Onyx/Wounds/WoundPrototype.cs` | same | verbatim | WP4 | needs `CirculatoryStreamPrototype`; dead `using Content.Shared.Body;` kept on purpose |
| `Content.Shared/_Onyx/Wounds/WoundDamageComponents.cs` | same | modified | WP4 | D9: `Chest`+`Groin` weights fold to one `Torso = 4f` row; `Groin` dismemberment row deleted; `SystemicPainTarget` → `.Torso`; `_Shitmed` targeting using. **`LocalizedDamageTypes` verbatim incl. `Caustic`** |
| `Content.Shared/_Onyx/Wounds/WoundSystem.cs` | same | modified | WP4 | `Body.Components` using; D17 rejuvenate |
| `Content.Shared/_Onyx/Wounds/WoundScarSystem.cs` | same | verbatim | WP4 | |
| `Content.Shared/_Onyx/Wounds/WoundStatusEffectSystem.cs` | same | verbatim | WP4 | dead `using Content.Shared.Body;` kept |
| `Content.Shared/_Onyx/Wounds/BodyPartFunctionalitySystem.cs` | same | modified | WP4 | `_Shitmed.Cybernetics` using. `wounds.body_part_functionality_enabled` defaults **false**, so this is inert until phase 3 |
| `Content.Shared/_Onyx/Wounds/PainSystem.cs` | same | verbatim | WP4 | served by the stun + chat shims + `PainNumbnessStatusEffectComponent` |
| `Content.Shared/_Onyx/Wounds/WoundFractureSystem.cs` | same | modified | WP4 | missing `using Content.Shared.Damage;`; `FractureProfile` → `WolfmedBodyPartComponent` |
| — | `Content.Shared/_WF/Wolfmed/Body/WolfmedBodyPart{Component,System}.cs` | new | WP4 | D8; 6 fields, 15 call sites; `MaxDamage` default 0 disables overflow |
| — | `Content.Shared/_WF/Wolfmed/Targeting/WoundTargetResolver.cs` | new | WP4 | D10; no scatter in phase 1; accepts and folds `TargetBodyPart.Groin` |
| `Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs` | same | modified | WP5/WP8 | the heaviest file: ~22 `// WOLFGATE` edits (WP5 table) + the D23 AP side table and the D27 applied-delta accumulator |
| `Content.Shared/_Onyx/Wounds/WoundDamageProjectionSystem.cs` | same | modified | WP5 | D11 event swap, D17, D19, D9 layers, `Parent` walk |
| `Content.Shared/_Onyx/Mobs/Systems/MobThresholdSystem.cs` | same | modified | WP5 | absent from the sparse checkout — read via `git show`. **Adds** `_damageable`; vital parts `{Head, Torso}` |
| — | `Content.Shared/Damage/Systems/DamageableSystem.cs` | new (hook) | WP5 | GUARD D (~10 lines) + GUARD D2 (3 event members + `return before.Applied;`). **The one upstream change the port cannot avoid** |
| — | `Content.Shared/_Shitmed/Body/Systems/SharedBodySystem.Targeting.cs` | new (hook) | WP5 | GUARDs A/B/C, ~6 lines. Component-gated, never `_net.IsServer`-gated |
| `Content.Shared/_Onyx/Wounds/WoundBleedingSystem.cs` | `Content.Server/_Onyx/Wounds/WoundBleedingSystem.cs` | adapted | WP6 | D13: server-side; two `using` swaps |
| `Content.Shared/_Onyx/Wounds/WoundInternalBleedingSystem.cs` | `Content.Server/_Onyx/Wounds/…` | adapted | WP6 | D13 + the `TryModifyBloodLevel` argument fix at `:67` |
| `Content.Shared/_Onyx/Wounds/OrganDamageSystem.cs` | `Content.Server/_Onyx/Wounds/…` | adapted | WP6 | D13 + D26 (**both** `:24` and `:36` disabled) + organ-health redirect |
| `Content.Shared/_Onyx/Wounds/WoundHealingSystem.cs` | `Content.Server/_Onyx/Wounds/…` | adapted | WP6 | D13 + D14 + D31 (convert at `:110`, signatures untouched) |
| `Content.Shared/_Onyx/Body/OrganDamageComponent.cs` | same | verbatim | WP6 | |
| `Content.Shared/_Onyx/Body/Systems/OrganHealthSystem.cs` | `Content.Server/_Onyx/Body/Systems/OrganHealthSystem.cs` | adapted | WP6 | organ health → `WolfmedOrganComponent`; `RemoveOrgan` |
| `Content.Shared/_Onyx/Body/Part/BodyPartComponent.cs` | — | skipped | — | D8: redeclares four `Content.Shared.Body.Part` types → CS0101 ×4 |
| `Content.Shared/_Onyx/Body/Systems/SharedBodySystem.cs` | — | skipped | — | D8: Onyx's is `sealed`, Wolfgate's is `abstract partial`, same namespace |
| `Content.Shared/_Onyx/Body/{FunctionalOrganComponent,OrganConsequenceComponents,TaggedOrgan*,ProfileOrgans*}.cs`, `Content.Server/_Onyx/Body/**` | — | skipped | — | Nubody / Onyx-surgery glue |
| — | `Content.Server/Body/Systems/BloodstreamSystem.cs` | new (hook) | WP6 | GUARD E + E3 |
| — | `Content.Server/_Mono/Traits/Physical/HemophiliaSystem.cs` | new (hook) | WP6 | GUARD E4 |
| — | `Content.Server/Medical/{Components/HealingComponent.cs,HealingSystem.cs}` | new (hook) | WP6 | HOOK 7 (`HashSet<>` types) + HOOK 8 |
| `Resources/Prototypes/_Onyx/Wounds/wounds.yml` | same | modified | WP7 | organic subset only; `Chest`+`Groin` organ-damage rows fold to `Torso` |
| `Resources/Locale/en-US/_Onyx/prototypes/wounds/wounds.ftl` | same | verbatim | WP7 | 43 keys, complete |
| `Resources/Locale/en-US/_Onyx/medical/fractures.ftl` | same | verbatim | WP7 | `BrokenBones` keys live here |
| `Resources/Prototypes/_Onyx/Alerts/alerts.yml` | same | modified | WP7 | `BrokenBones` only |
| `Resources/Textures/_Onyx/Interface/Alerts/fracture.rsi` | same | verbatim | WP7 | CC-BY-SA-3.0, tgstation/darkrell |
| `Resources/Textures/_Onyx/Wounds/{brute,burn}_damage.rsi` (156 files) | — | skipped | — | cosmetic re-skins of sprites Wolfgate already ships |
| — | `Resources/Prototypes/_WF/Wolfmed/Body/parts.yml` | new | WP7 | Onyx's per-limb numbers; Chest row → `BaseTorso`, Groin row dropped; all 13 abstracts must be covered |
| — | `Resources/Prototypes/Entities/Mobs/Species/base.yml` | new (hook) | WP7 | D21 `WoundHost` + D22 gib threshold + **D29 `PassiveDamage` neutralisation** on `BaseMobSpeciesOrganic` |
| — | `Content.Shared/Armor/SharedArmorSystem.cs` | new (hook) | WP8 | HOOK 10, must route through `PenetrateArmor` |
| — | `Content.Shared/Mobs/Systems/MobThresholdSystem.cs` | new (hook) | WP8 | HOOK 11 ×2 (`:340`, `:406`) |
| — | `Content.Server/Medical/DefibrillatorSystem.cs` | new (hook) | WP8 | HOOK 12 |
| — | `Content.Shared/Execution/SharedExecutionSystem.cs` | new (hook) | WP8 | HOOK 13 |
| `Content.Shared/_Onyx/Wounds/{ReagentTreatmentEffects,ReagentTreatmentSystems,SuppressPainEntityEffect}.cs` | — | skipped | — | D16: rewritten old-style in `_WF/Wolfmed/EntityEffects`; `HealthChange`/`EvenHealthChange` get HOOK 9 instead |
| `Content.IntegrationTests/Tests/_Onyx/Wounds/{WoundSurgeryTest,WoundSurgeryScarTest}.cs`, `.../Medical/SurgeryStepSequencePrototypeTest.cs`, `.../Body/TransplantCompatibilityPrototypeTest.cs` | — | skipped | — | D7/D8 |
| `Content.Shared/_Onyx/Medical/Surgery/**`, `Content.Shared/_Onyx/Surgery/**` | — | skipped | — | D7. Two component-name collisions if ever revisited |
| `Content.Shared/Bed/BedSystem.cs`, `SharedStaminaSystem.cs`, `ThermalRegulatorSystem.cs`, `CrewMonitoringConsoleSystem.cs`, suit sensors, `SharedCryoPodSystem.cs` | — | skipped | — | every Onyx marker in them belongs to an unrelated feature |
| `Content.Shared/DoAfter/SharedDoAfterSystem.cs` | — | skipped | — | Wolfgate's Goobstation `GetDoAfterDelayMultiplierEvent` (`:207-214`) already provides the seam |
| `Content.Shared/Changeling/Systems/RegenerativeStasisSystem.cs` | — | skipped | — | Wolfgate has no Changeling |

---

## 8. Risks and open items

### 8.1 Needs the user before the affected WP starts

1. **Which `BaseMobSpeciesOrganic` descendants ship as wound hosts?** (D21, D3.) `- type: WoundHost` on `BaseMobSpeciesOrganic` reaches **18 direct children**: `arachnid`, `diona`, `dwarf`, `gingerbread`, `human`, `moth`, `reptilian`, `slime`, `vox`, `chitinid`, `feroxi`, `rodentia`, `vulpkanin`, `tajaran`, `yowie`, `asakim`, `protogen`, `hydrakin` — plus their descendants. Two sub-questions:
   **(a) Plant and slime species** (`diona`, `slime`) would run on `OrganicBodyPartProfile` until phase 5 supplies `PlantBodyPartProfile`/`SlimeBodyPartProfile` — Onyx itself lets them inherit `WoundHost` and only overrides their part profiles, so shipping them on Organic defaults is Onyx-consistent but not Onyx-equivalent.
   **(b) Synthetics.** `protogen` parents `BaseMobSpeciesOrganic` yet carries `prototype: SiliconDeathgasp` (`_Mono/.../protogen.yml:73-75`). Organic bleeding, fractures, scars and pain on a synthetic is exactly what D3 defers to phase 5.
   If the answer to (b) is "exclude", D21 stays one line plus one `- type: WoundHost` **removal** per excluded species — still far cheaper than per-species additions, but the manifest must record every exclusion. **Blocks WP7.** Recommendation: ship (a), exclude (b), and re-enumerate the descendant list at implementation time because forks add species.
2. **Balance: `PassiveDamage` is switched off for wound hosts** (D29). Onyx models passive recovery per part profile (`passiveRecoveryMultiplier`, `bedRecoveryMultiplier`) and `FilterPartDamage` explicitly branches on a `PassiveDamageComponent` origin, so keeping Wolfgate's body-level regen would double-heal *and* silently re-interpret `damageCap: 20` against the projected total. If the user would rather keep body-level regen and drop Onyx's, say so before WP7 — the change is one YAML block either way. **Blocks WP7.**
3. **Balance: limb damage against armour roughly doubles.** Today Shitmed applies armour twice to a part (body inventory relay at `SharedBodySystem.Targeting.cs:174-176`, *plus* the `PartDamage` modifier set, *plus* `GetPartDamageModifier(type)` — head ×0.5, limbs ×0.7), and the part is hit *before* the body's own resistance block. After the bridge, armour applies exactly once and `GetPartDamageModifier` no longer applies to wound-host parts. Onyx's profile multipliers may or may not compensate. **This needs a playtest measurement before blaming `wounds.yml` severities.** Does not block any WP.
4. **Environmental damage now makes limb wounds.** `Barotrauma` (`Blunt 0.50/s`, `Heat 0.1/s`, `base.yml:250-254`) and `Temperature` (`Cold 0.1/s`, `Heat 1.5/s`, `:268-276`) sit on the exact prototype D21 targets, and all four types are in `localizedDamageTypes`. A hull breach or a hot room therefore routes to a randomly-picked limb every tick and creates blunt/burn wounds there. This is Onyx-consistent and intended — recorded so the first playtest report of "space makes my arm bleed" is recognised as designed, not as a bug. Does not block any WP.
5. **Do-after interruption on wound hosts.** The projection calls `SetDamage(..., interruptsDoAfters: false)`, so damage will no longer cancel a wound host's do-afters. The alternative is threading `interruptsDoAfters` through `RefreshBodyDamage`, adding a `// WOLFGATE` parameter to a vendored method. **Recommendation: accept the loss for phase 1 and revisit after playtest.**
6. **Phase-1 damage is unpredicted for wound hosts.** Onyx's routing is server-only by construction (`!_net.IsServer` at eight entry points, including `OnBeforeDamageChanged:57`). With GUARDs A/B/C component-gated, the client does **not** cancel and therefore applies the full hit to the body's own `DamageableComponent`, then accepts the server's projected value one tick later. The mispredict is transient and self-correcting, but damage numbers and the limb doll will flicker. On a gun-PvP server this will be noticed. Predicting the routing is a phase-6 project. **Confirm this is acceptable before WP7 ships to a playtest.**

### 8.2 Known deviations from Onyx, recorded deliberately

- `DamageDealtEvent` is not `readonly` (§2.3).
- `BeforeDamageChangedEvent` carries three Wolfgate-only members (`ArmorPenetration`, `Tool`, `Applied`) and `TryChangeDamage` returns `before.Applied` instead of `null` on cancel (D23, D27). Godmode and stasis are unaffected.
- The facade's `SetDamage` **zeroes** types missing from the argument where Onyx **removes** them (D30), to preserve `DamageableInit`'s container seeding.
- `CanBeDamagedBy` tests the mob's `DamageableComponent.DamageContainerID` instead of Onyx's `InjurableComponent.DamageContainer` (D19 drops `InjurableComponent`). Consequence: systemic damage types outside the mob's `Biological` container are deleted on the next `RefreshBodyDamage`.
- `WoundHostComponent.TargetWeights` has one `Torso = 4f` row where Onyx has `Chest 2.5` + `Groin 1.5`, so random routing keeps Onyx's 31 % torso share instead of dropping to 22 % (D9).
- Routing's `before: [typeof(DamageableSystem)]` ordering hint is inert in Wolfgate (the write is inline, not a subscriber); correctness comes from where GUARD D sits inside `TryChangeDamage`.
- `WolfmedBodySystem.TryDetachPart` ignores `reparent` and goes through `AmputateAttemptEvent`. Wolfgate's `CanDetachPart` has a refusal path Onyx does not — if Shitmed's own severing has already mangled the slot, amputation can silently no-op. Assert this in the phase-3 tests.
- `StunSystemOnyxCompat.TryUpdateParalyzeDuration` re-triggers stun VFX on each call; Onyx's silently extends the timer.
- `SharedChatSystem.TryEmoteWithChat` is `virtual void`, not Onyx's `bool`; the single caller discards the result (D25).
- `SuppressPain` scale semantics differ: Onyx's framework clamps `Scale` to ≤1 unless `Scaling` is set; Wolfgate's `EntityEffectReagentArgs.Scale` is unclamped. A D4 tuning item for WP11.
- `wounds.body_part_functionality_enabled` ships **false** (Onyx's own default), so `BodyPartFunctionalitySystem` returns `Functional` unconditionally and Shitmed's `Enabled` thresholds remain the only limb-disable mechanism in phases 1–2. Deliberate: the two systems are orthogonal, and adopting Onyx's as a *replacement* on its own defaults would ship Wolfgate with no limb disabling at all.
- `WoundStatusEffectBehavior` is declared in `WoundBehaviors.cs` but **no wound prototype uses it at this pin**, and the two status effects it would drive (`StatusEffectBurnSlowdown`, `StatusEffectWoundImpairment`) are orphaned in Onyx too. Phase 1 ports neither.
- `PassiveDamage` is neutralised on wound hosts (D29).

### 8.3 Traps that will silently ship a broken build if ignored

1. **`TryChangeDamage` skips damage types not already in the dict** (`DamageableSystem.cs:256-257`). Onyx's `OnDamageDealt` *adds* new keys. Any damage type a part's container does not list vanishes with no error. This is why D30 zeroes instead of removes, and why T-CAUSTIC and T-SETUP both exist. Re-check whenever `localizedDamageTypes`, `OrganicPart` or the `Burn`/`Brute` group membership changes. **Note the revision-1 error this trap caused:** D20 dropped `Caustic` on the belief that `OrganicPart` lacked it, when in fact `OrganicPart` supports the whole `Burn` group and `Burn` contains `Caustic`. Check *group* membership, not just `supportedTypes`.
2. **`WoundDamageProjectionSystem.OnPartInserted`/`OnPartRemoved` have no caller** anywhere in the Onyx wound set (`:95`, `:105`). If `WolfmedBodyPartLifecycleSystem` is forgotten, wounds silently never initialise on surgically attached limbs. T-SETUP covers map-init only — add a surgery-attach assertion in phase 3.
3. **GUARDs A/B/C must be component-gated, never `_net.IsServer`-gated.** Server-only guards give permanent client mispredict: the client keeps running Shitmed's spread while the server routes through Onyx.
4. **`SharedArmorPlateSystem` double-absorb.** Routing must be ordered `before: [typeof(SharedArmorPlateSystem)]` on both of its `BeforeDamageChangedEvent` subscriptions. Wolfgate-specific — Onyx has no armour plates.
5. **`DamageChangedEvent.DamageDelta` must survive the projection** (GUARD F inside `WolfmedDamageableSystem.SetDamage`). Without it, eight systems go silent for every wound host — bleeding, hemophilia, wake-on-damage, pain screams, forced say, kill attribution, NPC retaliation and ignite-on-heat — all of which early-return on `DamageDelta == null`. T12 is the gate.
6. **`TryChangeDamage`'s return value must survive the cancel** (D27). Without it the damage still lands but every caller reads "nothing happened", and `HitscanBasicDamageSystem.cs:33-34` turns that into "pierce stops at the first humanoid". T-RESULT and T-PIERCE are the gates.
7. **Reagent id collisions, phase 4:** `Stasizium` already exists in `WG/Resources/Prototypes/_Goobstation/Reagents/medicine.yml` and `SalicylicAcid` in `WG/Resources/Prototypes/_NF/Reagents/chemicals.yml`, both with different definitions. Vendoring Onyx's reagent files verbatim **will fail prototype loading**. Decide rename-vs-reuse before WP11 starts.
8. **Component-registration collisions, phase 4:** `SurgeryTendWoundsEffectComponent` and `SurgeryWoundedConditionComponent` already exist in Wolfgate under those exact registered names, with different fields and already wired to shipping prototypes. Vendoring Onyx's `WoundSurgeryComponents.cs` as written crashes `IComponentFactory` at boot. Extend Wolfgate's versions; do not vendor Onyx's.
9. **Health-analyzer UI, phase 4:** Onyx and Shitmed independently rewrote the same screen into incompatible shapes (Onyx: split control/window + an always-all-parts status doll from the unported Targeting UI; Shitmed: one monolithic class + a second-round-trip `HealthAnalyzerPartMessage`). This needs an explicit "parallel UI vs. graft" decision before it is scheduled.
10. **`GroupHealSpecifier.cs` carries a second license** (Wega, GPL-3.0) layered under Onyx's AGPL-3.0-or-later. Preserve the header verbatim and record the extra provenance in the manifest.
11. **Phase-5 blocker, recorded now:** IPC/Slime/Plant circulatory streams cannot be ported until Wolfgate's metabolizer gets the shared, stage-based rewrite (`MetabolismStagePrototype`, `SolutionManagerComponent`, shared `MetabolizerComponent`). A much larger, separate project than anything in phases 1–4.

---

## 9. Revision notes

Revision 2 (this document) applied `CRITIQUE.md` after re-deriving every item from the primary sources. Changes, in the order the critique raised them:

**Blockers, all accepted and verified.**
- **B1** → WP1 gains file #17, `Content.Shared/Traits/Assorted/PainNumbnessStatusEffectComponent.cs`, and it is removed from WP1's "Deferred to WP10" list. Verified `PainSystem.cs:323` + `:13`, and that the component's only dependency (`LocalizedDatasetPrototype`) and its default prototype (`damage_force_say.yml:14`) both exist in Wolfgate. Also verified no registration clash with Wolfgate's existing `PainNumbnessComponent`.
- **B2** → new **D28**. `<BodyComponent, BodyPartAddedEvent/BodyPartRemovedEvent>` confirmed taken by `SharedBodySystem.PartAppearance.cs:25-26`; both events confirmed raised on the body only (`SharedBodySystem.Parts.cs:337-338`, `:357-358`). `WolfmedBodyEventBridgeSystem` and `WolfmedPartLifecycleSystem` merged into one `WolfmedBodyPartLifecycleSystem`, moved from WP2 to WP5, subscribing `<WoundHostComponent, …>`. §5.1 now lists three confirmed collisions; §5.3's deferred checks were run and moved into §5.2.
- **B3** → new **D27**. All seven cited call sites re-read and confirmed, including the `return` **inside** the `foreach` at `HitscanBasicDamageSystem.cs:33-34`. `BeforeDamageChangedEvent` gains `DamageSpecifier? Applied`; `DamageableSystem.cs:215` returns it; the router accumulates the routed delta from the three places that already compute it. GUARD D2 moved from WP8 to WP5. New tests T-RESULT and T-PIERCE; §8.3 gains trap 6.

**Majors, all accepted and verified.**
- **M1** → WP6 #3 gains a mandatory `// WOLFGATE` edit at `WoundInternalBleedingSystem.cs:67`. Confirmed the two-user-defined-conversion problem against `Entity.cs:34,44` and `BloodstreamSystem.cs:363`.
- **M2** → new **D31**. Signatures stay at Onyx's types; the conversion happens at `WoundHealingSystem.cs:110`. HOOK 7's two collection datafields are pinned to `HashSet<>`, with the extra `using Content.Shared._Onyx.Wounds;` called out.
- **M3** → D26 now disables `OrganDamageSystem.cs:24` as well as `:36`. Confirmed `AmputationSystem` appears nowhere else in `_Onyx/Wounds/**`.
- **M4** → D21 rewritten; §8.1 item 1 now lists all 18 direct descendants (verified by grep) and splits the question into plant/slime vs synthetics, with `protogen.yml:2,73-75` cited.
- **M5** → new **D29**. `PassiveDamage` neutralised on the wound-host base; WP7 #7's YAML block updated; new test T-PASSIVE.
- **M6** → WP5 #3 reworded from "`[Dependency]` swap" to "**add** `_damageable`", with both class declarations quoted.
- **M7** → §2.1 gains an explicit implementation note on the re-basing and its consequence for `SystemicDamageComponent`; T6 extended to a second `RefreshBodyDamage` pass.
- **M8** → new **D30**. Facade `SetDamage` zeroes rather than removes; T-SETUP gains the dict-count assertion; §8.2 records the deviation.

**Minors.**
- **m1** accepted — D25 reduced to `virtual void` + one-word `override`; HOOK 6 re-targeted to `ChatSystem.Emote.cs:60` (verified: `:60` takes `string`, `:85` takes `EmotePrototype`, `PainSystem.cs:297` passes a string and discards the result).
- **m2** accepted — D9 sets `[BodyPartType.Torso] = 4f`; recorded in §8.2.
- **m3** accepted — D9 re-scoped: `TargetBodyPart.Groin` stays (verified `TargetBodyPart.cs:16,30`); only `BodyPartType.Groin` and `HumanoidVisualLayers.Groin` are deleted. §2.13 says the resolver folds it to the torso.
- **m4** accepted — D16 and §2.15 now cover `EvenHealthChange` (exists in Wolfgate, needs HOOK 9) and `DistributedHealthChange` (absent, must be authored). HOOK 9 applies to two files.
- **m5** accepted — new §2.17 records which five dead `using Content.Shared.Body;` lines stay on purpose.
- **m6** accepted **and corrected**: the critique pointed the acceptance check at `Resources/Prototypes/_Shitmed/Body/Parts/`, which holds only `BasePart` and `BaseTorso`. The other eleven limb abstracts live in `Resources/Prototypes/Body/Parts/base.yml`. WP7 cites both files with line numbers.
- **m7** accepted — §8.1 item 4.

**Found during verification, not in the critique.**
- **D20 reversed (blocker-class).** Revision 1 dropped `Caustic` from `localizedDamageTypes` because "`OrganicPart` has no Caustic". `OrganicPart` declares `supportedGroups: [Brute, Burn]` (`containers.yml:78-82`) and `Caustic` is a member of `Burn` (`groups.yml:10-16`), and `DamageableInit` seeds every type of every supported group (`DamageableSystem.cs:109-121`) — so `Caustic` is present on every part and would have routed correctly. Onyx's `OrganicBodyPartProfile` accepts it and `BurnWound` has a `Caustic` entry. Revision 1 would have silently deleted acid limb damage for no reason. The YAML override is gone, T-CAUSTIC now asserts the opposite, and §8.3 trap 1 records the *group-membership* lesson.
- **HOOK 11's line numbers corrected** from `:338` to `:340` (`CheckThresholds`) and the `UpdateAlerts` read pinned at `:406`.
- **HOOK 12's site pinned** at `DefibrillatorSystem.cs:209-211`.
- **§8.1 item 6 sharpened:** the client does not cancel (the router is `_net.IsServer`-gated at `:57`), so the client applies the full hit to the body and is corrected a tick later — a transient mispredict, not a silent no-op.
- **Parallel groups made explicit** (A–E) and per-WP file counts added, since WP7/WP8/WP9 were previously drawn as three separate branches without saying which may share an agent.
