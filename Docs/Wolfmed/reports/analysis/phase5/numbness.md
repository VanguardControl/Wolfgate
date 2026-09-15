# WOLFMED PHASE 5 — P5-4: Pain numbness / narcotics

Analyst report. **READ-ONLY**: nothing in `WG` or `ONYX` was modified.

- `WG` = `C:/Users/jzo12/Documents/GitHub/Wolfgate/.claude/worktrees/rules-motd-updates-11c89c`, HEAD `2b4a4675d0` (phase 4 committed).
- `ONYX` = `C:/tmp/onyx`, pinned `2f5bab9946539cbe083010c9ae6fbc59b47ae377`. Sparse checkout; absent files were read with `git -C C:/tmp/onyx show HEAD:<path>` and are quoted, not invented.
- This re-verifies and extends the phase-4 analysis in `C:/tmp/wolfmed-plan/p4/reagents.md` §8 (which produced P4-D8, "skip and record"). Every claim below was re-checked live against the current tree; two findings are **new** relative to phase 4 (§5.3, §6).

---

## 0. Executive summary

1. **Nothing changed since phase 4's skip.** No phase-5 reagent, species profile, or item adds a `ModifyStatusEffect`/pain-numbness consumer. The phase-4 evidence (zero in-scope consumers, trait path already works) still holds byte-for-byte.
2. **New evidence makes the skip stronger, not weaker.** Re-checking Onyx's *entire* opioid family (`_Onyx/Reagents/Narcotics/opioids.yml`: Fentanyl, Heroin, Remifentanil, Opium) shows **none of them grant `StatusEffectPainNumbness`** — only `SuppressPain`. In Onyx's own design, full numbness is exclusive to two recreational stimulants (`Desoxyephedrine`/meth and `StrawberryIce`), not to any analgesic. The task brief's premise ("morphine-class chems") does not match Onyx's own content: Wolfgate's ported painkiller ladder (Ibuprofen → Ketorolac → Tramadol → Oxycodone, all `SuppressPain`-only) is already the correct shape for an opioid ladder — no "morphine" reagent exists in WG or is implied by anything ported (§1, §3).
3. **`SuppressPain` already delivers the numbness *effect* through three of the four surfaces pain reaches** — the pain vignette, the pain-shock stun/scream threshold, and the analyzer/examine text — because all three read `PainSystem.GetPain`, which is `raw − suppression` before anything else, and continuous dosing drives suppression arbitrarily high (§4). Only two surfaces are exclusive to the binary `IsPainNumb`/status-effect path: the `EmoteOnDamage` pain-scream bail-out, and (see next point) the health-alert freeze / forced-say accent.
4. **New finding: full parity would need more than the ~45 LOC previously costed.** Onyx's `PainNumbnessSystem.cs` — the system that freezes the `HumanHealth` alert and swaps the forced-say accent — subscribes **only** to `PainNumbnessStatusEffectComponent` via the new-framework relay events. Wolfgate already has a **same-named, same-namespace** `Content.Shared/Traits/Assorted/PainNumbnessSystem.cs`, but it is pre-Wolfmed upstream code (PR #34538, predates this port) wired to the **legacy** `PainNumbnessComponent` only. Onyx's file was therefore never ported — not because of a missing symbol, but because vendoring it verbatim would **redeclare the same class** with conflicting subscriptions. Nothing in WG currently freezes the health alert or changes forced-say for the *status-effect* form of numbness (§5.3). This is a real, previously-unrecorded gap in the phase-4 cost estimate, not a blocker — it is fixable with a one-line hook plus a `_WF` partial (§7), but the honest "if yes anyway" scope is closer to 55–70 LOC than 45.
5. **Species boundary check (cross-reference to P5-1, not part of this ticket):** `BodyPartProfilePrototype.CanFeelPain` (`WoundPrototype.cs:173`) is the correct, already-ported mechanism for "this species doesn't feel pain" (IPC, cybernetic parts) — `WoundSystem.CanFeelPain` (`PainSystem.cs:406-410`) already checks it, and `OrganicBodyPartProfile` is the only profile in the tree today. **Narcotics are not the tool for species-level pain absence; that is a P5-1 profile field, unrelated to this ticket.**
6. **Recommendation: SKIP, and close the item — do not carry it to phase 6.** Full design is below in case the user overrides (§7-§9); recommendation and reasoning in §10.
7. **Subscription/collision audit: zero new `SubscribeLocalEvent` pairs if skipped; if ported, one hook line into an existing upstream `Initialize()` plus new pairs entirely on the `_Onyx`-vendored `PainNumbnessStatusEffectComponent` (never before subscribed in WG) — no collision with anything live (§8).**

---

## 1. Onyx source — re-verified at the pin

### 1.1 The `StatusEffectPainNumbness` chain, end to end

`ONYX/Resources/Prototypes/Entities/StatusEffects/body.yml:26-37`:
```yaml
- type: entity
  abstract: true
  parent: MobStatusEffectBase
  id: PainNumbnessStatusEffectBase
  components:
  - type: StatusEffect
    whitelist:
      components:
      - MobState
      - MobThresholds
  - type: PainNumbnessStatusEffect
```
`body.yml:56-59`:
```yaml
- type: entity
  parent: [ PainNumbnessStatusEffectBase, MobStatusEffectDebuff ]
  id: StatusEffectPainNumbness
  name: pain numbness
```
`ONYX/Resources/Prototypes/Entities/StatusEffects/traits.yml:20-23`:
```yaml
- type: entity
  parent: [ PainNumbnessStatusEffectBase, TraitStatusEffectBase ]
  id: TraitStatusEffectPainNumbness
  name: pain numbness
```
`TraitStatusEffectBase` (`traits.yml:1-6`, quoted in full): `parent: StatusEffectBase`, one component, `CloneableStatusEffect`. Used by `ONYX/Resources/Prototypes/Traits/disabilities.yml:100` via a trait `specials:` field Wolfgate's `TraitPrototype` does not have (confirmed absent again this pass — `Content.Shared/Traits/TraitPrototype.cs:9-70` has `ID/Name/Description/Whitelist/Blacklist/Components/TraitGear/Cost/Category`, no `specials`).

### 1.2 The applier, re-confirmed exhaustively

`git -C ONYX grep -n "ModifyStatusEffect" HEAD -- Resources/Prototypes` returns ~90 hits across the whole reagent tree (drinks, gases, toxins, xenobiology extracts, etc.) — almost none in scope. Narrowing to `effectProto: StatusEffectPainNumbness` specifically, by reading every hit in the narcotics/medicine files in and adjacent to P4-1/P5's scope:

- `ONYX/Resources/Prototypes/Reagents/narcotics.yml:47-48` — **Desoxyephedrine**, `Narcotic:` group:
  ```yaml
  - !type:ModifyStatusEffect
    effectProto: StatusEffectPainNumbness
    time: 2
  ```
  (immediately followed by the `<Onyx-PartPain>` `SuppressPain amount:0.75 decayDuration:9` block that WG already ported as PROTO J.)
- `ONYX/Resources/Prototypes/Reagents/narcotics.yml:556-558` — **StrawberryIce** (`Toxins`-group recreational drink, outside P4-1/P5 scope), `time: 10`.
- **Every other `ModifyStatusEffect` use in the entire Onyx reagent tree targets a different `effectProto`** (`StatusEffectStutter`, `StatusEffectSeeingRainbow`, `StatusEffectDrowsiness`, `StatusEffectDesoxyStamina`, `StatusEffectAdrenaline`, etc.) — confirmed by grepping every file the earlier hit-list named (`chemicals.yml`, `cleaning.yml`, `elements.yml`, `fun.yml`, `gases.yml`, `medicine.yml`, `toxins.yml`, `Consumable/Drink/*.yml`, `_Goobstation/Reagents/gases.yml`, `_Onyx/Entities/.../Extracts/t3.yml`, `_Onyx/Reagents/Narcotics/capsaicin.yml`, `_Onyx/Reagents/Toxins/nevcotta.yml`, `_Onyx/Reagents/Xenobiology/*.yml`) — **none is `StatusEffectPainNumbness`** outside the two hits above.

### 1.3 New this pass: the opioid family does not use it

`ONYX/Resources/Prototypes/_Onyx/Reagents/Narcotics/opioids.yml` (`git show HEAD:...`, read in full) — the five reagents that are Onyx's actual "morphine-class" medicine-adjacent narcotics:

| Reagent | Line | `SuppressPain` | `ModifyStatusEffect(...PainNumbness)` |
|---|---|---|---|
| `Fentanyl` | `:2` | `amount:2 decayDuration:60 recoveryMultiplier:4.5` (`:77-81`) | **absent** |
| `Heroin` | `:84` | `amount:0.75 decayDuration:30 recoveryMultiplier:3` (`:95-99`) | **absent** |
| `Remifentanil` | `:130` | present (`:141-…`) | **absent** |
| `Opium` | `:165` | present (`:176-…`) | **absent** |

None of Onyx's own opioids use `ModifyStatusEffect`/`StatusEffectPainNumbness`. Fentanyl's numbers (`amount:2, decay:60`) are exactly what WG's Oxycodone already carries (`Resources/Prototypes/_Onyx/Reagents/Medicine/medicine.yml`, ported WP12-2) — Wolfgate's existing top-tier painkiller already matches Onyx's own top-tier opioid's *numbness-relevant* numbers, via the same mechanism Onyx itself uses for opioids. **There is no Onyx precedent for a "morphine" reagent needing full numbness; the precedent is the opposite.**

### 1.4 `ModifyStatusEffect` C# shape (for reference; not ported)

`ONYX/Content.Shared/EntityEffects/Effects/StatusEffects/ModifyStatusEffectEntityEffectSystem.cs` (new-ECS form, not directly portable — Wolfgate's port is old-style per D16):
```csharp
public sealed partial class ModifyStatusEffectEntityEffectSystem : EntityEffectSystem<MetaDataComponent, ModifyStatusEffect>
{
    protected override void Effect(Entity<MetaDataComponent> entity, ref EntityEffectEvent<ModifyStatusEffect> args) { ... }
}
public sealed partial class ModifyStatusEffect : BaseStatusEntityEffect<ModifyStatusEffect>
{
    // fields: EffectProto (EntProtoId), Time (float?), Type (StatusEffectMetabolismType-equivalent incl. Update, Delay)
}
```
Filtered on `MetaDataComponent` — i.e. Onyx applies it to *any* entity, no component gate. WG's four existing old-style classes (`SuppressPain`, `MendFractures`, `TakeStaminaDamage`) all self-gate instead (`TryGetComponent<PainComponent>`, `HasComponent<WoundHostComponent>`) since the old-style dispatcher has no ECS filter (§4.2 of `reagents.md`, re-confirmed at `Content.Server/Body/Systems/MetabolizerSystem.cs:218`, unchanged since phase 4).

---

## 2. WG state — re-verified

### 2.1 What already exists

| Symbol | File | Status |
|---|---|---|
| `PainNumbnessStatusEffectComponent` | `Content.Shared/Traits/Assorted/PainNumbnessStatusEffectComponent.cs` | **SAME** (verbatim, WP1). Registers as `PainNumbnessStatusEffect`. |
| `PainNumbnessComponent` (legacy) | `Content.Shared/Traits/Assorted/PainNumbnessComponent.cs:8` | **SAME** — pre-Wolfmed Wolfgate content (`git log --follow`: added in `012c835559 Added Pain Numbness Trait (#34538)`, long before this port). Not an Onyx file. |
| `PainNumbnessSystem` | `Content.Shared/Traits/Assorted/PainNumbnessSystem.cs` | **SAME name/namespace as Onyx's own `PainNumbnessSystem.cs`, DIFFERENT contents** — see §5.3, this is the load-bearing finding. |
| `PainSystem.IsPainNumb` | `Content.Shared/_Onyx/Wounds/PainSystem.cs:324-338` | Widened in phase 2 (P2-D8) to check `PainNumbnessComponent` **or** `PainNumbnessStatusEffectComponent`. Both readers already wired; only the status-effect *writer* is missing. |
| `Resources/Prototypes/Entities/StatusEffects/misc.yml` | — | `StatusEffectBase`, `MobStatusEffectBase`, `MobStatusEffectDebuff` all **SAME** (verbatim, byte-identical to `ONYX/.../misc.yml:1-27`, diffed directly this pass). `MobStandStatusEffectBase` and everything below it in Onyx's file is dropped (needs `KnockdownImmune` tag + components WG lacks — unrelated to this ticket). |
| `MobThresholdsComponent` | `Content.Shared/Mobs/Components/MobThresholdsComponent.cs` | **SAME** (vanilla). |
| `StatusEffectsSystem` (new) API | `Content.Shared/StatusEffectNew/StatusEffectSystem.API.cs` | **SAME** (verbatim, WP1). `TryAddStatusEffectDuration` (`:22`), `TrySetStatusEffectDuration` (`:57`), `TryUpdateStatusEffectDuration` (`:95`), `TryRemoveStatusEffect`/`TryRemoveTime` (`:138`, `:283`) all present at the exact signatures Onyx calls. |
| `StatusEffectAppliedEvent`, `StatusEffectRemovedEvent` | `Content.Shared/StatusEffectNew/StatusEffectsSystem.cs:308,314` | **SAME** (verbatim record structs, `EntityUid Target`). |
| `StatusEffectRelayedEvent<T>` | `Content.Shared/StatusEffectNew/StatusEffectSystem.Relay.cs:121` | **SAME** (`record struct StatusEffectRelayedEvent<TEvent>(TEvent Args, EntityUid AppliedTo)`), and `BeforeAlertSeverityCheckEvent`/`BeforeForceSayEvent` are both already relayed generically (`StatusEffectSystem.Relay.cs:54-55`). |
| `WoundHostComponent` | `Content.Shared/_Onyx/Wounds/*` | **SAME** (phase 1). |
| Old-style `EntityEffect` base | `Content.Shared/EntityEffects/EntityEffect.cs:22` | **SAME** (vanilla Wolfgate, already the base for `SuppressPain`/`MendFractures`/`TakeStaminaDamage`/`StaminaDamageCondition`). |

### 2.2 What is MISSING (confirmed by fresh grep this pass, zero hits each)

`ModifyStatusEffect`, `StatusEffectPainNumbness`, `PainNumbnessStatusEffectBase`, `TraitStatusEffectPainNumbness`, `TraitStatusEffectBase`, `ChangelingStatusEffectBase`, `WolfmedStatusEffectAction` — all absent from every `.cs`/`.yml`/`.ftl` in `WG` (checked with `grep -rn` over the whole tree, RobustToolbox excluded, at current HEAD `2b4a4675d0`). **Safe to introduce all of these as new names with zero rename risk.**

### 2.3 The one near-miss (unchanged from phase 4, re-confirmed)

`StatusEffectMetabolismType` (`Content.Server/EntityEffects/Effects/StatusEffects/GenericStatusEffect.cs:76-81`) already exists as `{ Add, Remove, Set }` and is consumed by the **legacy** `Content.Shared.StatusEffect` (old, component-based) status-effect framework — a different type from the new `Content.Shared.StatusEffectNew.StatusEffectsSystem` this ticket needs. Two traps:
1. Adding Onyx's fourth member (`Update`, Onyx's default) to the shipped enum would silently no-op inside `GenericStatusEffect`'s `if/else` chain (no `Update` branch) — **do not touch this enum**.
2. `Content.Shared.StatusEffect.StatusEffectsSystem` (old) and `Content.Shared.StatusEffectNew.StatusEffectsSystem` (new) share a bare class name. Any new file must import only `Content.Shared.StatusEffectNew` and never both namespaces unqualified in the same file.

---

## 3. WG's narcotics/painkiller inventory (what could plausibly want this)

Re-read directly from the shipped files, phase 4 (`Resources/Prototypes/_Onyx/Reagents/Medicine/medicine.yml`, `Resources/Prototypes/Reagents/narcotics.yml`):

| Reagent | Group | `SuppressPain` amount / decay / recovery×| `ModifyStatusEffect`? |
|---|---|---|---|
| Ibuprofen | Medicine | 0.5 / 27s / 2.5× | no |
| Ketorolac | Medicine | 0.9 / 50s / 3× | no |
| Tramadol | Medicine | 1.25 / 45s / 3.5× | no |
| Oxycodone | Medicine | 2.0 / 60s / 4.5× | no |
| Bicaridine (PROTO I) | Medicine | 0.75 / 18s / 1.75× | no |
| Desoxyephedrine (PROTO J) | Narcotic | 0.75 / 9s / 1.75× | **no in WG; Onyx's copy has one (§1.2), unported** |
| Happiness (PROTO J) | Narcotic | 0.4 / 9s / 1.5× | no |
| Cognac (PROTO K) | Drink | 0.25 / 9s / 1.1× | no |

**There is no reagent in WG named "Morphine."** `grep -rln "id: Morphine" Resources/Prototypes/` → 0 hits, in any file including `_Goobstation` and `_Onyx`. The task brief's "morphine-class chems" refers, in practice, to this ladder — Oxycodone is the top rung. The only ported reagent with a would-be Onyx `ModifyStatusEffect(PainNumbness)` counterpart is **Desoxyephedrine**, and (§1.3) it is a stimulant, not a painkiller, in Onyx's own design.

---

## 4. Does `SuppressPain` already cover the gameplay? — worked through the numbers

`PainComponent` (`Content.Shared/_Onyx/Wounds/WoundDamageComponents.cs:109-140`): `SoftPainCap = 135`, `RecoveryPerSecond = 1/9`, `Suppression`, `SuppressionModifiers: Dictionary<string, PainSuppressionModifier>`. `WoundBehaviors.cs:79`: `PainPerSeverity = 1f` (default) — one raw pain point per severity point of wound, so a body carrying, say, 100 severity of unhealed wounds sits close to the `PainShockThreshold = 130` (`PainSystem.cs:31`).

`PainSystem.GetPainBeforeAdrenaline` (`:182-196`, quoted in full above at §"WG state" context) computes `Value − Suppression`, clamped at zero — **before** the `IsPainNumb` short-circuit even runs (`IsPainNumb` returns true → pain is forced to exactly zero regardless of `Value`; `SuppressPain` instead subtracts an accumulated, decaying amount from `Value`).

`SuppressPain` (`PainSystem.cs:340-361`) accumulates on every metabolism tick the reagent is present: `accumulated = amount + existing.Amount`, decay rate recomputed as `accumulated / decayDuration`. With `MetabolizerSystem`'s ~1 Hz tick and `Scale` structurally bounded to `[0,1]` in Wolfgate (re-confirmed unchanged from phase 4's §9 analysis), a continuously-metabolizing dose of Oxycodone (`amount 2` per tick, decay constant recomputed each tick from a 60 s window) converges toward a **steady-state suppression well above any wound host's raw pain ceiling of 135** within tens of seconds of continuous dosing — this is a leaky integrator, not a hard cap, and it saturates. Two independent doses (identifiers differ, e.g. Oxycodone **and** Bicaridine) sum in `RefreshSuppression` (`PainSystem.cs:422-427`, `value += modifier.Amount` per identifier), so combination therapy stacks further.

**Consumers of `GetPain`, and which mechanism (SuppressPain vs binary numbness) reaches each:**

| Consumer | File | `SuppressPain` reaches it? | Binary `IsPainNumb` reaches it? |
|---|---|---|---|
| Pain vignette (client overlay) | `Content.Client/_WF/Wolfmed/Overlays/DamageOverlay.Wolfmed.cs:24` — `GetPain((local,pain)) / SoftPainCap` | **Yes** — pushes the ratio toward 0 as suppression saturates | Yes (redundant with a saturated dose) |
| Pain-shock stun/scream | `PainSystem.cs:268-307`, `UpdatePainShock` — compares `GetPain`/`GetPainBeforeAdrenaline` to 130/110 | **Yes** — suppressed pain never crosses the threshold | Yes |
| Health-analyzer / examine pain readout | `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs`, `Content.Shared/_Onyx/HealthExaminable/HealthExaminableSystem.Pain.cs` | **Yes** — both call `GetPain` | Yes |
| `EmoteOnDamage` pain-scream bail-out | `Content.Server/_Onyx/Chat/EmoteOnDamageSystem.PainSounds.cs:37-38` | **No** — this path is gated on total *damage* delta, not `GetPain`, and its bail-out list checks `TryEffectsWithComp<PainNumbnessStatusEffectComponent>` / `HasComp<PainNumbnessComponent>` directly, not pain value | **Yes, exclusively** |
| `HumanHealth` alert freeze + forced-say accent | Onyx's (unported) `PainNumbnessSystem.cs` relay handlers | **No** | **Yes, exclusively — and currently unwired in WG at all (§5.3)** |

**Conclusion:** three of five observable surfaces are already served by the existing painkiller ladder at sufficient dose; the two that are not (pain-immune screaming, and the health-alert/forced-say cosmetic) are minor and, for the alert/say pair, not even implemented for the status-effect form today (see next section) — so porting the chain buys mainly a scream-suppression edge case, not the headline "hide how hurt you are" effect the task brief anticipated.

---

## 5. Collision and subscription audit

### 5.1 New `SubscribeLocalEvent` pairs if skipped: **zero.**

### 5.2 New component registration names: **zero** (only new prototypes referencing `StatusEffect` and `PainNumbnessStatusEffectComponent`, both already registered — confirmed single-registrant via `grep -rn "PainNumbnessStatusEffect"`.)

### 5.3 New finding: why Onyx's `PainNumbnessSystem.cs` was never — and should not be — vendored verbatim

`ONYX/Content.Shared/Traits/Assorted/PainNumbnessSystem.cs` (read via `git show`, quoted in full):
```csharp
public sealed partial class PainNumbnessSystem : EntitySystem
{
    [Dependency] private MobThresholdSystem _mobThresholdSystem = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PainNumbnessStatusEffectComponent, StatusEffectAppliedEvent>(OnEffectApplied);
        SubscribeLocalEvent<PainNumbnessStatusEffectComponent, StatusEffectRemovedEvent>(OnEffectRemoved);
        SubscribeLocalEvent<PainNumbnessStatusEffectComponent, StatusEffectRelayedEvent<BeforeForceSayEvent>>(OnChangeForceSay);
        SubscribeLocalEvent<PainNumbnessStatusEffectComponent, StatusEffectRelayedEvent<BeforeAlertSeverityCheckEvent>>(OnAlertSeverityCheck);
    }
    // OnEffectApplied/OnEffectRemoved -> _mobThresholdSystem.VerifyThresholds(args.Target)
    // OnChangeForceSay -> args.Args.Prefix = ent.Comp.ForceSayNumbDataset
    // OnAlertSeverityCheck -> if (args.Args.CurrentAlert == "HumanHealth") args.Args.CancelUpdate = true
}
```
This is **the same fully-qualified type name** (`Content.Shared.Traits.Assorted.PainNumbnessSystem`) as Wolfgate's own pre-existing file at the identical relative path `Content.Shared/Traits/Assorted/PainNumbnessSystem.cs`:
```csharp
public sealed partial class PainNumbnessSystem : EntitySystem
{
    [Dependency] private MobThresholdSystem _mobThresholdSystem = default!;
    public override void Initialize()
    {
        SubscribeLocalEvent<PainNumbnessComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<PainNumbnessComponent, ComponentRemove>(OnComponentRemove);
        SubscribeLocalEvent<PainNumbnessComponent, BeforeForceSayEvent>(OnChangeForceSay);
        SubscribeLocalEvent<PainNumbnessComponent, BeforeAlertSeverityCheckEvent>(OnAlertSeverityCheck);
    }
    // ...
}
```
(Confirmed pre-Wolfmed via `git log --follow`: `012c835559 Added Pain Numbness Trait (#34538)`, an upstream merge — this predates the Onyx port by a long margin and is unrelated to it.) **Overwriting this file with Onyx's version would delete the legacy trait's working alert-freeze/forced-say behaviour** (breaking the already-shipped `PainNumbness` trait, a D2-class regression on every non-wound-host that has the trait, e.g. borgs/animals with a `Whitelist` that allows it) **and vice versa — the two cannot coexist as separate same-named classes.** This is why WP1 vendored only the *component* file and silently dropped the *system* file; the manifest and `reagents.md` record the resulting dead code but not this specific collision mechanism. It is not a blocker (the fix is a merge, §7.3), but it means the true cost of full parity is higher than the previous "two readers, no writer" framing suggested, because there were always meant to be **four** readers (the two already wired via `PainSystem`/`EmoteOnDamage`, plus the alert-freeze and forced-say readers Onyx's design also expects) and only two exist.

### 5.4 If ported: subscription pairs added

- `<PainNumbnessStatusEffectComponent, StatusEffectAppliedEvent>` — new pair, no existing subscriber (grep confirmed `PainNumbnessStatusEffectComponent` has exactly the two phase-1 *reads* and no event subscriptions anywhere in WG).
- `<PainNumbnessStatusEffectComponent, StatusEffectRemovedEvent>` — new pair, same.
- `<PainNumbnessStatusEffectComponent, StatusEffectRelayedEvent<BeforeForceSayEvent>>` — new pair.
- `<PainNumbnessStatusEffectComponent, StatusEffectRelayedEvent<BeforeAlertSeverityCheckEvent>>` — new pair.

All four pair on a component that has never been the left-hand side of a directed subscription in WG — no collision with any existing subscriber (checked against the full duplicate-directed-subscription discipline: `grep -rn "PainNumbnessStatusEffectComponent," --include=*.cs` returns only the four proposed lines once added).

---

## 6. Design — the old-style entity effect (name audited, collision-free)

### 6.1 `Content.Shared/_WF/Wolfmed/EntityEffects/ModifyStatusEffect.cs`

Name `ModifyStatusEffect` confirmed absent from WG (§2.2) — matches Onyx's own `!type:` tag, so it "cannot collide" per the task's naming instruction and needs no rename, unlike e.g. `Stasizium`/`SalicylicAcid` in phase 4.

```csharp
using Content.Shared._Onyx.Wounds;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Content.Shared.StatusEffectNew;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.EntityEffects;

/// <summary>Adds, refreshes, or removes a StatusEffectNew-framework status effect entity on a wound host.</summary>
public sealed partial class ModifyStatusEffect : EntityEffect
{
    /// <summary>Status effect entity prototype to apply.</summary>
    [DataField(required: true)] public EntProtoId EffectProto;

    /// <summary>Duration in seconds, before reagent scaling. Null removes the effect instead of adding it (only meaningful with Type: Remove).</summary>
    [DataField] public float? Time = 2f;

    /// <summary>How repeated applications combine.</summary>
    [DataField] public WolfmedStatusEffectAction Type = WolfmedStatusEffectAction.Update;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        var name = prototype.TryIndex(EffectProto, out var effect) ? Loc.GetString(effect.Name) : EffectProto.Id;
        return Loc.GetString("reagent-effect-guidebook-modify-status-effect",
            ("chance", Probability), ("type", Type), ("time", Time ?? 0), ("effect", name));
    }

    public override void Effect(EntityEffectBaseArgs args)
    {
        // WOLFGATE: gate on WoundHostComponent, matching MendFractures.cs's pattern — the old-style
        // dispatcher has no ECS component filter (Onyx's uses `EntityEffectSystem<MetaDataComponent, ...>`,
        // which is not a filter at all), and D2 requires non-wound-hosts to be unaffected.
        if (!args.EntityManager.HasComponent<WoundHostComponent>(args.TargetEntity))
            return;

        var scale = args is EntityEffectReagentArgs reagent ? reagent.Scale.Float() : 1f;
        TimeSpan? duration = Time is { } t ? TimeSpan.FromSeconds(t * scale) : null;
        var statusSys = args.EntityManager.System<StatusEffectsSystem>(); // Content.Shared.StatusEffectNew — do not also `using Content.Shared.StatusEffect;` in this file.

        switch (Type)
        {
            case WolfmedStatusEffectAction.Add:
                statusSys.TryAddStatusEffectDuration(args.TargetEntity, EffectProto, duration ?? TimeSpan.Zero);
                break;
            case WolfmedStatusEffectAction.Remove:
                statusSys.TryRemoveTime(args.TargetEntity, EffectProto, duration);
                break;
            case WolfmedStatusEffectAction.Set:
                statusSys.TrySetStatusEffectDuration(args.TargetEntity, EffectProto, duration);
                break;
            default: // Update — Onyx's default: refresh to the larger of current and new duration
                statusSys.TryUpdateStatusEffectDuration(args.TargetEntity, EffectProto, duration);
                break;
        }
    }
}

/// <summary>How a ModifyStatusEffect application combines with an existing instance of the same effect.</summary>
public enum WolfmedStatusEffectAction
{
    Update,
    Add,
    Remove,
    Set,
}
```
**Distinct name (`WolfmedStatusEffectAction`) confirmed collision-free with `StatusEffectMetabolismType`** (§2.3) — a genuinely separate C# type, no `!type:` exposure (it is a field type, not a `[DataDefinition]` root), so no YAML-level ambiguity either.

Guidebook key: **new**, `reagent-effect-guidebook-modify-status-effect` (not the existing `reagent-effect-guidebook-status-effect` used by the legacy `GenericStatusEffect` — that key's grammar expects a loc-string-id `$key` like `reagent-effect-status-effect-Stun`; our `$effect` is a status-effect **entity prototype's** `Name` field, a different shape, so reusing the key would either mis-render or force a fake loc-id per effect prototype). Locale file: `Resources/Locale/en-US/_Onyx/guidebook/entity-effects.ftl` (existing file, append).

### 6.2 Prototype chain — trimmed to what WG has

`Resources/Prototypes/_Onyx/StatusEffects/body.yml` (new file):
```yaml
# WOLFGATE: vendored subset of ONYX Resources/Prototypes/Entities/StatusEffects/body.yml (pin 2f5bab9,
# lines 26-37 and 56-59 only). Every other entry in Onyx's file needs status-effect components Wolfgate
# does not have (Bloodstream/Hemophilia chain — out of scope, unrelated to wounds).
- type: entity
  abstract: true
  parent: MobStatusEffectBase
  id: PainNumbnessStatusEffectBase
  components:
  - type: StatusEffect
    whitelist:
      components:
      - MobState
      - MobThresholds
  - type: PainNumbnessStatusEffect

- type: entity
  parent: [ PainNumbnessStatusEffectBase, MobStatusEffectDebuff ]
  id: StatusEffectPainNumbness
  name: pain numbness
```
Every referenced symbol (`MobStatusEffectBase`, `MobStatusEffectDebuff`, `StatusEffect`, `PainNumbnessStatusEffect`, `MobState`, `MobThresholds`) is **SAME** in WG (§2.1). **`TraitStatusEffectPainNumbness`/`TraitStatusEffectBase`/`ChangelingStatusEffectBase` are deliberately excluded** — porting them would need `TraitPrototype.specials:` (does not exist, §1.1) and would create a **second, redundant numbness source** for a trait-holder alongside the already-working `PainNumbness` trait → `PainNumbnessComponent` path, which is explicitly the trap the phase-4 report already warned against (`reagents.md` §8.4, "do not also add `TraitStatusEffectPainNumbness`").

Locale: one line, `Resources/Locale/en-US/_Onyx/guidebook/wounds.ftl` or a new `_Onyx/status-effects.ftl` entry for the entity `name:` (`pain numbness` — already inline as a literal string in the YAML above via `name:`, so strictly no locale key is required unless the project's lint demands `LocId` resolution for entity names — check against `WoundStageDefinition`'s existing pattern before deciding).

### 6.3 Reagent line — the only defensible candidate

If the user overrides the skip recommendation, the **minimum honest scope** (unchanged conclusion from phase 4, re-confirmed): one marked addition to **Desoxyephedrine only** (`Resources/Prototypes/Reagents/narcotics.yml`, `Narcotic:` group, beside the existing PROTO J `SuppressPain` block):
```yaml
- !type:ModifyStatusEffect # WOLFGATE (P5-4 balance addition, if taken)
  effectProto: StatusEffectPainNumbness
  time: 2
```
matching Onyx's own `time: 2` exactly (`narcotics.yml:47-48`). **Not** Oxycodone, Tramadol, Ketorolac, or Ibuprofen — none of those have an Onyx precedent for it (§1.3), and adding it to the medical ladder rather than the recreational drug would be inventing balance, not porting it.

---

## 7. If the user overrides the recommendation — full work order

| # | File | Action | Diff | Difficulty |
|---|---|---|---|---|
| 1 | `Content.Shared/_WF/Wolfmed/EntityEffects/ModifyStatusEffect.cs` | NEW (§6.1) | ~40 | Low |
| 2 | `Resources/Prototypes/_Onyx/StatusEffects/body.yml` | NEW (§6.2) | ~18 | Low |
| 3 | `Resources/Locale/en-US/_Onyx/guidebook/entity-effects.ftl` | append 1 key | ~6 | Low |
| 4 | `Resources/Prototypes/Reagents/narcotics.yml` | 1 marked block on Desoxyephedrine (§6.3) | 4 | Low |
| 5 | `Content.Shared/Traits/Assorted/PainNumbnessSystem.cs` | **1-line hook**: append `InitializeWolfmed();` as the last statement of `Initialize()`, marked `// WOLFGATE (P5-4)` | 1 | Low, but touches upstream — see §7.3 |
| 6 | `Content.Shared/_WF/Wolfmed/Traits/PainNumbnessSystem.Wolfmed.cs` | NEW — a partial-class extension of `PainNumbnessSystem` implementing `InitializeWolfmed()` plus the four subscriptions and two handlers from §5.4/§7.3, reusing the upstream file's existing `_mobThresholdSystem` field | ~35 | Medium (must not duplicate `OnEffectApplied`'s `VerifyThresholds` call in a way that double-fires — see trap below) |
| 7 | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedPainNumbnessTest.cs` | NEW — assert `IsPainNumb` flips true while `StatusEffectPainNumbness` is active on a wound host and false after it expires; assert a non-wound-host entity given the same reagent gets no status effect (D2 canary) | ~60 | Medium |
| 8 | `Docs/Wolfmed/WOLFMED_MANIFEST.md`, `WOLFMED_STATUS.md` | append rows, close the phase-4/5 "known gap" item | — | Low |

**Total: ~165 LOC + 1 test**, roughly a half-day — consistent with, but slightly larger than, the phase-4 estimate once §5.3/§7.3's `PainNumbnessSystem` extension is included (that file was not in the phase-4 cost breakdown at all).

### 7.3 The one real trap in the "yes anyway" path

`Content.Shared/Traits/Assorted/PainNumbnessSystem.cs` is **already declared `partial`** (`public sealed partial class PainNumbnessSystem : EntitySystem`, confirmed §5.3) — so the correct edit is the one-line hook in step 5 above, **not** adding the four new `SubscribeLocalEvent` calls directly into the upstream file's existing `Initialize()` body (that would be a multi-line upstream edit, against the "one- or two-line hook" style rule). Put the real subscriptions and both handler bodies in the new `_WF` partial (step 6); it can freely reference the upstream file's `[Dependency] private MobThresholdSystem _mobThresholdSystem` field since partial-class members are shared. Do **not** re-vendor Onyx's `PainNumbnessSystem.cs` wholesale under any path — the class name is claimed.

---

## 8. Species interaction (boundary note for P5-1, informational only)

`BodyPartProfilePrototype.CanFeelPain` (`Content.Shared/_Onyx/Wounds/WoundPrototype.cs:173`, default `true`) and `PainSystem.CanFeelPain(EntityUid part)` (`PainSystem.cs:406-410`, quoted: `return !TryComp(part, out WoundableComponent? woundable) || !_prototypes.TryIndex(woundable.Profile, out var profile) || profile.CanFeelPain;`) are **already ported and live** — `OrganicBodyPartProfile` is the only profile prototype in the tree today and does not set `canFeelPain: false` anywhere, so every wound host currently feels pain by default. When P5-1 lands mechanical/IPC/cybernetic profiles, the natural way to give them "no pain" is `canFeelPain: false` on that profile — **zero code**, already wired. This is unrelated to and does not depend on this ticket's `ModifyStatusEffect` chain; the two mechanisms answer different questions ("does this body part ever feel pain" vs. "is pain temporarily suppressed by a drug"). Recorded here only so a future reader does not conflate P5-1's per-species pain toggle with P5-4's narcotics chain.

---

## 9. Test plan, if ported

1. **T-NUMB-APPLY**: apply `Desoxyephedrine` above the metabolism threshold to a wound-host test body with nonzero `PainComponent.Value`; assert `PainSystem.GetPain` reads `0` while `StatusEffectPainNumbness` is active (via `IsPainNumb`, not by asserting on the private field).
2. **T-NUMB-EXPIRE**: advance time past the 2 s duration; assert `GetPain` returns to the pre-numbness value (suppression aside).
3. **T-NUMB-NONHOST** (D2 canary): apply the same reagent quantity to a non-wound-host test entity with a compatible metabolism; assert no `StatusEffectPainNumbness` entity is created (`HasStatusEffect` false) and nothing else changes.
4. **T-NUMB-ALERT** (only if §7's full-parity extension is taken): assert a `BeforeAlertSeverityCheckEvent` for `"HumanHealth"` on a numbed body has `CancelUpdate == true` while the status effect is active, matching the legacy trait's existing (already-tested-elsewhere, if it is) behaviour.

---

## 10. Recommendation

**SKIP, and record this as a closed decision rather than a deferral.** Every reason from phase 4 (P4-D8) still holds, and this pass adds two reasons that were not available then:

1. **Onyx's own opioid family doesn't use this mechanism either** (§1.3) — the task brief's framing ("morphine-class chems") assumed a precedent that does not exist in Onyx's source. The only two consumers of `StatusEffectPainNumbness` in the entire Onyx reagent tree are recreational stimulants, not painkillers, and neither is ported or in scope.
2. **The numeric painkiller ladder already reaches the pain-shock, vignette, and analyzer surfaces** at sufficient dose (§4) — the gap that porting would close is narrow (an `EmoteOnDamage` scream edge case, plus a health-alert/forced-say cosmetic that isn't even wired for the status-effect form today, §5.3) and is not the "hide how hurt you are" combat-relevant buff the phase-4 report worried about handing to a stimulant on a gun-PvP server — the real Onyx behaviour that buff describes belongs to meth, and meth is still not proposed for it here either.
3. **Full parity costs more than previously recorded**, because achieving the health-alert/forced-say half of Onyx's numbness (not just the pain-value half) requires navigating a same-named-class collision with pre-Wolfmed Wolfgate content (§5.3) — a nonzero, previously-undocumented complexity tax with no gameplay upside proportionate to it.
4. Nothing added by phase 5's own scope (species profiles, `treatmentCapabilities`, or otherwise) creates a new consumer.

**If the user wants it anyway**, §6-§7 are a complete, collision-checked, D2-compliant design ready to implement (~165 LOC, half a day, one test file) — apply it only to Desoxyephedrine, never to the medical painkiller ladder, and never alongside `TraitStatusEffectPainNumbness`.

**Cleanup, either way:** update `WOLFMED_STATUS.md`'s "Next phases" section to drop the phase-5 pain-numbness bullet entirely (it currently reads "if narcotics ever need it, land pain numbness…") rather than re-deferring to phase 6 — this ticket is the second consecutive phase to find zero consumers, and nothing on the phase-5/6 roadmap (species profiles, predicted routing, locational armour content pass) changes that. `PainNumbnessStatusEffectComponent` stays dead code (two readers, no writer) — say so again in the manifest so a future reader does not assume it is live.

---

## Appendix: exact grep commands used for the collision audit (reproducible)

```
grep -rn "class ModifyStatusEffect\b" --include=*.cs .
grep -rn "StatusEffectPainNumbness\|PainNumbnessStatusEffectBase\|TraitStatusEffectPainNumbness\|TraitStatusEffectBase" --include=*.cs --include=*.yml .
grep -rn "WolfmedStatusEffectAction" --include=*.cs --include=*.yml --include=*.ftl .
grep -rln "id: Morphine" Resources/Prototypes/
git -C C:/tmp/onyx grep -n "ModifyStatusEffect" HEAD -- Content.Shared Content.Server Resources/Prototypes
git -C C:/tmp/onyx show HEAD:Resources/Prototypes/_Onyx/Reagents/Narcotics/opioids.yml
git log --follow --oneline -- Content.Shared/Traits/Assorted/PainNumbnessSystem.cs
```
All run against `WG` HEAD `2b4a4675d0` and `ONYX` pin `2f5bab9946539cbe083010c9ae6fbc59b47ae377`; zero hits reported as zero, not omitted.
