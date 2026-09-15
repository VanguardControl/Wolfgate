# WOLFMED PHASE 5 — P5-3 (`treatmentCapabilities` annotation pass)

Analyst report. **READ-ONLY**: nothing in `WG` or `ONYX` was modified.

- `WG` = `C:/Users/jzo12/Documents/GitHub/Wolfgate/.claude/worktrees/rules-motd-updates-11c89c`, HEAD `2b4a4675d0` (phase 4 complete).
- `ONYX` = `C:/tmp/onyx`, pinned `2f5bab9946539cbe083010c9ae6fbc59b47ae377`. Sparse checkout; absent files read with `git show HEAD:<path>` and quoted, not invented.
- Every claim is cited `file:line`.

---

## 0. Executive summary

1. **The gate is `TreatmentCapability` (`Biological`/`Mechanical`/`Electrical`), enforced by exactly two independent code paths**, both already vendored and both defaulting to `[Biological]`: `WoundHealingSystem.IsCompatiblePart` for `HealingComponent` items (topicals, tools) and `WoundDamageRoutingSystem.CanTreatPart`/`WithTreatmentCapabilities` for the `HealthChange`/`EvenHealthChange` reagent effects (HOOK 9). No third path exists. (§1)
2. **The pass is currently a no-op for every reagent and 10 of 11 `HealingComponent` items.** `OrganicBodyPartProfile` — the *only* `bodyPartProfile` in the tree — is `treatmentCapabilities: [Biological]` (`Resources/Prototypes/_Onyx/Wounds/wounds.yml:3`), matching every item/reagent's C# default. Writing `treatmentCapabilities: [Biological]` anywhere is a documentation-only no-op. (§3, §4)
3. **One item is already mis-scoped, right now, in the shipped tree.** Wolfgate's cable coil (`Resources/Prototypes/Entities/Objects/Tools/cable_coils.yml:36-45`) carries `damageContainers: [Silicon]` meaning "only heal silicon/cybernetic damage" — but `damageContainers` is a **dead parameter** on the wound-host healing path (`WoundHealingSystem.IsCompatiblePart`, `Content.Server/_Onyx/Wounds/WoundHealingSystem.cs:154-166`, never reads it) and `HealingSystem.TryHeal` explicitly **skips its own `DamageContainers` check for wound hosts** (`Content.Server/Medical/HealingSystem.cs:201-210`, HOOK 8). The *only* thing standing between the cable coil and an organic human's burn/shock wounds is `TreatmentCapabilities`, which nobody has ever set — so it defaults to `[Biological]`, which **does** overlap `OrganicBodyPartProfile`. **Right now, today, a cable coil heals a human's burn and shock wounds** at `-3.0` Heat/Shock per 0.6s application, completely bypassing its intended silicon-only restriction. This is the concrete "before" the task asks for (§5).
4. **Onyx's own source confirms the fix is exactly one field.** Onyx's cable coil (`ONYX Resources/Prototypes/Entities/Objects/Tools/cable_coils.yml:179-180`) is functionally identical to Wolfgate's (same three damage types, same delay) except it sets `treatmentCapabilities: [Electrical]`. That one line is the entire annotation Onyx applies to any ordinary item in this space — Onyx does not annotate Ointment, Brutepack, Gauze, Bloodpack, or Healing Toolbox at all (§2, §4).
5. **Wolfgate's cable coil was already correctly scoped by a different, coincidentally-matching mechanism**: its `damageContainers: [Silicon]` matches `Resources/Prototypes/_Shitmed/Body/Parts/cybernetic.yml:10-11`'s `damageContainer: Silicon` on Shitmed's own EE-style cybernetic prosthetic limbs (already-shipped content — a human can have an arm/leg replaced with a `CyberneticPartBase`-derived limb today). IPC parts use a **different** container, `Inorganic` (`Resources/Prototypes/Body/Parts/base.yml:15`, inherited by `PartIPCBase`). So the cable coil's real, intended target in Wolfgate's own fork is **cybernetic prosthetic limbs**, not IPC. `treatmentCapabilities: [Electrical]` is correct, since Onyx's own `CyberneticBodyPartProfile` is `[Mechanical, Electrical]` (`ONYX Resources/Prototypes/_Onyx/Wounds/wounds.yml:101`) (§2.1, §5).
6. **The fix is behaviourally inert until P5-1 ships a non-`Biological` `bodyPartProfile`.** Setting the cable coil to `[Electrical]` today, before any Mechanical/Electrical profile exists, makes it heal **nothing** on the current tree — closing today's leak but delivering zero new capability until P5-1 lands. **This is an explicit, observable regression for organic players between P5-3 and P5-1**: cable-coil self-heal of burns/shocks, which silently works today, stops working the moment this fix lands, and does not come back until a cybernetic-limb profile exists. Recorded as a sequencing decision (§7, §8).
7. **`AllowedWoundStages` is a second, separate lever the task also names** (Onyx restricts its two *strong* topicals — Regenerative Mesh and Medicated Suture — to `[Minor]`-stage wounds only, while the *weak* everyday items and the joke-tier `HealingToolbox` carry no stage restriction at all). Wolfgate's equivalents are unrestricted across the board and also numerically stronger from an earlier balance pass. Mirroring Onyx here is a **balance change**, not a bug fix, presented as an explicit, separate decision (§4, §7).
8. **"A welder heals IPC" turned out not to be a `TreatmentCapability` question at all.** Onyx's own `MobIpc` carries a plain `- type: Repairable` block (`ONYX/Resources/Prototypes/Corvax/Body/Species/ipc.yml:236-248`) — the same, unmodified vanilla component Wolfgate already ships — and the base `RepairableSystem` **never reads `TreatmentCapabilities`** (confirmed by reading `ONYX/Content.Shared/Repairable/RepairableSystem.cs` in full, §6.2). Wolfgate's `MobIpc` simply doesn't have a `Repairable` component yet; adding one costs zero new code and becomes wound-aware automatically once P5-1 ships `WoundHostComponent` + a profile for IPC, via the same generic routing bridge every reagent already uses (§6.3). This is cheaper than anything else in this report but belongs in **P5-1's** file list, not this pass's — documented here in full because the task named "welder" alongside the cable coil.
9. **A genuinely new, optional `HealingComponent`-on-`Welder` block remains the right tool for treating cybernetic *limbs* specifically** (a prosthetic arm on an otherwise-organic human) — `Repairable` is the wrong shape there because it targets a whole entity, not a chosen part (§6.4).
10. **No new `SubscribeLocalEvent` pair, no new component, no new `!type:` class** for the mandatory work (D-P5-3-A). Zero collision risk (§9).
11. **Recommendation:** land the cable-coil fix now (§7, D-P5-3-A: yes), skip the `AllowedWoundStages` balance mirroring (§7, D-P5-3-B: skip/record), hand the `MobIpc`-`Repairable` finding to P5-1 (§7, D-P5-3-C), and treat a `HealingComponent` on the welder for cybernetic-limb treatment as optional, deferred polish (§7, D-P5-3-D).

---

## 1. The mechanism, re-verified in the current tree

### 1.1 The enum and its two consumers

```csharp
[Serializable, NetSerializable]
public enum TreatmentCapability : byte
{
    Biological,
    Mechanical,
    Electrical,
}
```
`Content.Shared/_Onyx/Wounds/WoundPrototype.cs:202-208`. Three values only — there is no separate "Cybernetic" capability; Onyx's own `CyberneticBodyPartProfile` expresses "cybernetic" as `[Mechanical, Electrical]` (§2.1).

**Path A — `HealingComponent` items** (topicals, tools; driven by `Content.Server/Medical/HealingSystem.cs`'s `AfterInteractEvent`/`UseInHandEvent` → `HealingDoAfterEvent` do-after):

```csharp
public bool IsCompatiblePart(
    EntityUid body, EntityUid part,
    IReadOnlyList<ProtoId<DamageContainerPrototype>>? damageContainers,
    IReadOnlySet<TreatmentCapability> treatmentCapabilities)
{
    if (!_body.BodyHasChild(body, part) || !TryComp(part, out WoundableComponent? woundable) ||
        !_prototypes.TryIndex(woundable.Profile, out var profile) ||
        !profile.TreatmentCapabilities.Overlaps(treatmentCapabilities))
        return false;
    return true;
}
```
`Content.Server/_Onyx/Wounds/WoundHealingSystem.cs:154-166`. **`damageContainers` is accepted as a parameter and never read in the body** — a dead parameter carried through from `ResolveHealingPartEvent`/`ResolveHealingPart` (`WoundHealingSystem.cs:41-88`) for signature parity with Onyx, but nothing in the wound-host path uses it to filter. The *only* filter is the last line: does the target part's `WoundableComponent.Profile`'s `BodyPartProfilePrototype.TreatmentCapabilities` overlap the item's `HealingComponent.TreatmentCapabilities`.

Call chain: `HealingSystem.TryHeal` (`Content.Server/Medical/HealingSystem.cs:196-233`) → for a `WoundHostComponent` target, skips the vanilla `DamageContainers` gate entirely (`:201-210`, `!woundHost &&` guard, HOOK 8) → `OnWoundHostDoAfter`/`IsWoundDamaged` (`Content.Server/_WF/Wolfmed/Medical/HealingSystem.Wolfmed.cs:41-148`) → `ResolveHealingPartEvent` → `WoundHealingSystem.OnResolveHealingPart` (`Content.Server/_Onyx/Wounds/WoundHealingSystem.cs:32-39`) → `ResolveHealingPart` → `IsCompatiblePart`.

**Path B — `HealthChange`/`EvenHealthChange` reagent effects** (HOOK 9, metabolised chems):

```csharp
private bool CanTreatPart(EntityUid body, EntityUid part)
{
    if (!_treatmentCapabilities.TryGetValue(body, out var capabilities))
        return true;                                    // unscoped: no restriction at all
    return TryComp(part, out WoundableComponent? woundable) &&
           _prototypes.TryIndex(woundable.Profile, out var profile) &&
           profile.TreatmentCapabilities.Overlaps(capabilities);
}
```
`Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs:971-979`, entered only inside a `WithTreatmentCapabilities(body, capabilities, action)` scope (`:136-147`). `HealthChange.Effect()` (`Content.Server/EntityEffects/Effects/HealthChange.cs:172-191`) and `EvenHealthChange.Effect()` (`Content.Server/EntityEffects/Effects/EvenHealthChange.cs:141-155`) open that scope only when the change is a heal (`amount < 0`) on a `WoundHostComponent` target; both classes carry the same field:
```csharp
[DataField]
public HashSet<TreatmentCapability> TreatmentCapabilities = [TreatmentCapability.Biological];
```
(`HealthChange.cs:42-43`, `EvenHealthChange.cs:41-42`.)

**Important corollary of the `if (!_treatmentCapabilities.TryGetValue(...)) return true;` line above: any `TryChangeDamage`/`HealEvenly`/`ClearAllDamage` call that is *not* wrapped in `WithTreatmentCapabilities` is completely unscoped on a wound host** — `CanTreatPart` refuses nothing. This applies to every caller in the tree that heals a `WoundHostComponent` entity outside the two paths above (relevant to §6.2-§6.3's `RepairableComponent` finding).

**These two paths never share code and are not unified** — an item's `TreatmentCapabilities` and a reagent's are set independently, in different files, by different authors, and nothing enforces them being consistent. Confirmed correct by the phase-4 `reagents.md` report (§7.2 there) and re-verified directly against the shipped source above.

### 1.2 The default that makes today's tree "just work"

`BodyPartProfilePrototype.TreatmentCapabilities` defaults to `[TreatmentCapability.Biological]` (`WoundPrototype.cs:153-154`), and `HealingComponent.TreatmentCapabilities` / `HealthChange.TreatmentCapabilities` / `EvenHealthChange.TreatmentCapabilities` all default to the same single value (`Content.Server/Medical/Components/HealingComponent.cs:72-73`; `HealthChange.cs:42-43`; `EvenHealthChange.cs:41-42`). Since `OrganicBodyPartProfile` — the only profile in the tree (`wounds.yml:1-30`, comment at `:1`: *"WOLFGATE (WP7): Ipc/Slime/Plant/Cybernetic profiles and their wounds are dropped for phase 1 (D3)"*) — is also `[Biological]`, **every unannotated item and reagent already treats every current wound host correctly**, purely because both sides of the `Overlaps()` check share the same one-element default set. This is why phase 4's `reagents.md` (§7.1, §7.2) correctly recommended annotating nothing: there was nothing to fix for organics, and no non-Biological profile existed yet to annotate *for*.

### 1.3 What bypasses the gate entirely: systemic damage

`WoundHostComponent.LocalizedDamageTypes` = `{Blunt, Slash, Piercing, Heat, Cold, Shock, Caustic}` (`Content.Shared/_Onyx/Wounds/WoundDamageComponents.cs:35-44`, left at the Onyx default per `Resources/Prototypes/Entities/Mobs/Species/base.yml:252`'s D20 comment). `WoundDamageRoutingSystem.RouteAppliedDamage` splits every change into `systemic` vs `localized` by this set (`WoundDamageRoutingSystem.cs:735-740`) **before** either gate ever runs. Only the localized share reaches `ApplyLocalizedHealing`/`CanTreatPart` (`:742-759`); the systemic share goes straight to `ApplySystemicDamage`, which is gated only by `DamageableSystem.CanBeDamagedBy` — a damage-container check, not a treatment-capability check (`WoundDamageRoutingSystem.cs:1086-1107`). **Toxin, Airloss/Asphyxiation, Bloodloss, Genetic, Cellular and Radiation heals always land, on any wound host, regardless of `treatmentCapabilities`.** This is Onyx's own behaviour too (confirmed against `ONYX/Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs` in the phase-4 report), not a Wolfgate bug — but it means the cable coil's `Radiation: -3.0` component (§5) heals systemically and unconditionally today and will continue to after any fix; only its `Heat`/`Shock` components are capability-gated.

### 1.4 A name the task lists that is unrelated to `TreatmentCapability`

`WoundFractureSystem.CanTreat` (`Content.Shared/_Onyx/Wounds/WoundFractureSystem.cs:186`) gates `FractureTreatment` (`None`/`Reduced`/`Mended`) against `FractureGrade`, not `TreatmentCapability` — it answers "is this fracture grade eligible for this treatment step", already fixed for the Hairline-grade edge case in phase 4 (P4-D20, `WOLFMED_STATUS.md:244-247`). It is not part of this pass; noted for completeness since the task names it alongside the routing/healing files.

---

## 2. Onyx's own annotations — the complete, exhaustive set

```
git -C C:/tmp/onyx grep -n -E "treatmentCapabilities|healWounds|allowedWoundStages" HEAD -- Resources/Prototypes
```
returns **exactly 10 hits across 4 files** — the entire footprint of item/reagent/repair-side wound-treatment annotation in Onyx at the pin:

| File:line | Entity/prototype | Field | Value |
|---|---|---|---|
| `Resources/Prototypes/Corvax/Body/Species/ipc.yml:236-248` | **`MobIpc`'s own `- type: Repairable` block** (`id: MobIpc` at `:97`) — not an item, and not read by the base repair system at all (§6.2) | `treatmentCapabilities: [Mechanical]` (inert for `RepairableSystem`; present only for the optional modal-welder add-on) | flat `damage: {Blunt:-8, Slash:-6, Piercing:-5, Heat:-7, Cold:-6, Caustic:-4}`, `fuelCost:5`, `doAfterDelay:3`, `selfRepairPenalty:2` |
| `Resources/Prototypes/Entities/Objects/Specific/Medical/healing.yml:82-83` | **Regenerative Mesh** | `healWounds: true`, `allowedWoundStages: [Minor]` | — |
| `Resources/Prototypes/Entities/Objects/Specific/Medical/healing.yml:180-181` | **Medicated Suture** | `healWounds: true`, `allowedWoundStages: [Minor]` | — |
| `Resources/Prototypes/Entities/Objects/Specific/Medical/healing.yml:374` | **Healing Toolbox** | `healWounds: true` | — (no stage restriction; redundant with the class default) |
| `Resources/Prototypes/Entities/Objects/Tools/cable_coils.yml:179-180` | **Cable coil** (`CableApcStack`, the `##Corvax-species-start` block) | `treatmentCapabilities: [Electrical]`, `healWounds: true` | — |
| `Resources/Prototypes/_Onyx/Entities/Surgery/surgery_steps.yml:985,1005` | two wound-surgery steps | `healWounds: true` | out of scope (surgery, not an item/reagent) |
| `Resources/Prototypes/_Onyx/Wounds/wounds.yml:3,39,71,101,125` | the five `bodyPartProfile`s | `treatmentCapabilities` | `Organic=[Biological]`, `Ipc=[Mechanical,Electrical]`, `Slime=[Biological]`, `Cybernetic=[Mechanical,Electrical]`, `Plant=[Biological]` |

**Everything else in Onyx's healing.yml — Ointment, Brutepack, Bloodpack, Gauze, and the base `Tourniquet` — carries no annotation at all**, i.e. relies on the class defaults (`HealWounds=true`, `AllowedWoundStages=null` [all stages], `TreatmentCapabilities=[Biological]`). Verified by reading `ONYX/Resources/Prototypes/Entities/Objects/Specific/Medical/healing.yml` in full (417 lines).

Onyx's `HealingComponent` itself (`ONYX/Content.Shared/Medical/Healing/HealingComponent.cs`) is the **stock, un-extended** upstream component — the four wound-treatment fields live in a separate partial, `ONYX/Content.Shared/_Onyx/Medical/Healing/HealingComponent.Onyx.cs`, with identical defaults to what Wolfgate's HOOK 7 inlined directly into `HealingComponent.cs` (`Content.Server/Medical/Components/HealingComponent.cs:55-80`, marked `// WOLFGATE: HOOK 7 / D14`).

### 2.1 Onyx's `bodyPartProfile`s, for reference (not this pass's job — P5-1's)

```yaml
- type: bodyPartProfile
  id: IpcBodyPartProfile
  treatmentCapabilities: [Mechanical, Electrical]
  ...
- type: bodyPartProfile
  id: CyberneticBodyPartProfile
  treatmentCapabilities: [Mechanical, Electrical]
  canFeelPain: false
  ...
- type: bodyPartProfile
  id: SlimeBodyPartProfile
  treatmentCapabilities: [Biological]
  ...
- type: bodyPartProfile
  id: PlantBodyPartProfile
  treatmentCapabilities: [Biological]
  ...
```
(`ONYX/Resources/Prototypes/_Onyx/Wounds/wounds.yml:36-129`, quoted in relevant part.) Slime and Plant are `[Biological]` — identical to Organic — so **nothing in this pass changes for slime or plant hosts even after P5-1 ships their profiles**: their items/reagents already carry the correct default. Only Ipc and Cybernetic (`[Mechanical, Electrical]`) create any new capability requirement, and only for the items discussed in §5-§6.

---

## 3. WG inventory — `HealthChange`/`EvenHealthChange` reagents

`grep -c "!type:HealthChange" Resources/Prototypes` → **205** uses; `!type:EvenHealthChange` → **22** uses (up from phase 4's snapshot of 156/17 — phase 4 added Osteogen, Ibuprofen, Ketorolac, Tramadol, Oxycodone and the Stasizium `MendFractures` block, none of which change this pass's conclusion). All but the few flagged below live under `Resources/Prototypes/Reagents/**`; the rest are non-reagent NPC/plant self-damage or self-heal reactions (`Entities/Mobs/NPCs/{miscellaneous,slimes,space}.yml`, `Entities/Mobs/Species/{diona,reptilian,skeleton,slime}.yml`, `Objects/Misc/kudzu.yml`, `_Mono/Entities/Mobs/Chimera/{biomass,chimera_base}.yml`) — spot-checked (`diona.yml:63,78`) and confirmed to be **herbicide damage reactions**, not heals, so `amount < 0`'s HOOK 9 gate never opens for them regardless.

**No reagent in the tree needs an edit.** Every `HealthChange`/`EvenHealthChange` block defaults `TreatmentCapabilities = [Biological]` (§1.2), which is correct for every wound host that exists today (organic only) and will remain correct for Slime and Plant once P5-1 ships them (§2.1, both `[Biological]`). The only conceivable non-default reagent would be one meant to repair a machine or a synthetic body — none exists in `Resources/Prototypes/Reagents/**`, `_NF`, `_Mono`, or `_Goobstation` (re-confirmed by the same grep phase 4's `reagents.md` §7.1 ran; nothing has been added since that changes the conclusion).

**Recommendation: write zero `treatmentCapabilities:` lines into any reagent.** This matches phase 4's D-P4-1-F decision and nothing has changed to reopen it.

---

## 4. WG inventory — `HealingComponent` items

`grep -rln "type: Healing$" Resources/Prototypes` → **exactly 4 files**, unchanged from phase 4's count (`Tourniquet`'s `Healing` block was removed in phase 4, converted to a dedicated `Tourniquet` component — `Resources/Prototypes/Entities/Objects/Specific/Medical/healing.yml:279-295`, PROTO D — so it is not in this table; it treats bleeding through a separate P4-2 mechanism outside this pass's scope):

| Item | WG file:line | Damage healed | Current `treatmentCapabilities` | Current `allowedWoundStages` | Onyx equivalent | Proposed | Rationale |
|---|---|---|---|---|---|---|---|
| **Ointment** (+ `Ointment1`, `Ointment10Lingering`) | `Entities/Objects/Specific/Medical/healing.yml:33-40` | Heat/Cold/Shock −15, Caustic −10 | default `[Biological]` | default (all) | unannotated | **no change** | Onyx's Ointment is also unannotated; default already correct for the only profile that exists |
| **Regenerative Mesh** (+ `OintmentAdvanced1`) | `…healing.yml:90-97` | Heat/Cold/Shock/Caustic −20 | default `[Biological]` | default (all) | `allowedWoundStages: [Minor]` | **optional**: `allowedWoundStages: [Minor]` | Onyx restricts its strong burn topical to early-stage wounds only; WG's is unrestricted and also numerically stronger (Goobstation buff, comment at `:98`). Balance call, not a bug — see D-P5-3-B |
| **Brutepack** (+ variants) | `…healing.yml:136-141` | Brute group −45 | default `[Biological]` | default (all) | unannotated | **no change** | matches Onyx exactly (unannotated) |
| **Medicated Suture** (+ `BrutepackAdvanced1`) | `…healing.yml:191-197` | Brute group −60, bloodloss −10 | default `[Biological]` | default (all) | `allowedWoundStages: [Minor]` | **optional**: `allowedWoundStages: [Minor]` | same rationale as Regenerative Mesh — Onyx's is the "closes wounds early" tier item; WG's is unrestricted and much stronger (comment at `:196`: buffed due to surgery changes) |
| **Bloodpack** | `…healing.yml:235-241` | Bloodloss −2.5, +30 blood | default `[Biological]` | default (all) | unannotated | **no change** | matches Onyx |
| **Gauze** (+ variants) | `…healing.yml:312-319` | Slash −15, Piercing −20, bloodloss −10 | default `[Biological]` | default (all) | unannotated | **no change** | matches Onyx |
| **Aloe Cream** | `…healing.yml:353-364` (inherits `Ointment`'s block, no override) | same as Ointment | default `[Biological]` | default (all) | (no Onyx equivalent; Wolfgate-only reskin) | **no change** | pure reskin, no separate Healing block to annotate |
| **Healing Toolbox** | `…healing.yml:376-385` | Brute/Burn/Toxin group −150 each, bloodloss −20 | default `[Biological]` | default (all) | `healWounds: true` (redundant, no stage restriction) | **no change** | matches Onyx (a "joke/powerful" item deliberately unrestricted) |
| **Cloth** (crafting material) | `Entities/Objects/Materials/materials.yml:95-102` | Slash/Piercing −0.5, bloodloss −4 | default `[Biological]` | default (all) | no Onyx equivalent found in the medical items file | **no change** | improvised-bandage flavour item |
| **Resin Jelly** (xeno) | `_Mono/Entities/Objects/Specific/Xeno/xeno_drops.yml:14-24` | Brute/Burn groups, bloodloss −50, **Poison +5** (side effect) | default `[Biological]` | default (all) | no Onyx equivalent | **no change** | xenomorph-only item on organic hosts; the `Poison: 5` component is systemic (§1.3) and bypasses the gate regardless |
| **Cable coil** (`CableStack`, all LV/MV/HV variants by inheritance) | `Entities/Objects/Tools/cable_coils.yml:36-45` | Heat/Shock −3.0, **Radiation −3.0 (systemic, ungated)** | default `[Biological]` — **currently overlaps `OrganicBodyPartProfile`, an active leak (§5)** | default (all) | `treatmentCapabilities: [Electrical]` | **`treatmentCapabilities: [Electrical]`** | closes the leak; matches Onyx exactly; matches WG's own `damageContainers: [Silicon]` intent (§5) |

Every "no change" row is identical in spirit to phase 4's `reagents.md` §7.2 table; the one row that changed since phase 4 is the cable coil, whose treatment is expanded below because P5-3 explicitly asks this pass to act on it (phase 4 recorded it as *"the first phase-5 action"*, `WOLFMED_STATUS.md:117-119,299`).

---

## 5. The concrete bug: the cable coil today

**Reproduction, traced through the actual code (no test run — this is the READ-only trace; a headless test is proposed in §10 to confirm it empirically):**

1. A human player (any `WoundHostComponent` organic) has taken Heat or Shock damage — trivially common (a lighter, a taser, a welder backfire).
2. They (or a medic) use a `CableApcStack`/`CableHVStack`/`CableMVStack`/etc. (all inherit `CableStack`'s `- type: Healing` block, `cable_coils.yml:36-45`) on themselves via `UseInHandEvent`/`AfterInteractEvent`.
3. `HealingSystem.TryHeal` (`HealingSystem.cs:196-233`) sees `HasComp<WoundHostComponent>(target)` → `woundHost = true` → **skips the `damageContainers: [Silicon]` check entirely** (`:201-210`, the `!woundHost &&` guard).
4. `OnWoundHostDoAfter`/`IsWoundDamaged` (`HealingSystem.Wolfmed.cs:41-148`) raises `ResolveHealingPartEvent`, which `WoundHealingSystem.OnResolveHealingPart` answers via `ResolveHealingPart` → `IsCompatiblePart` (`WoundHealingSystem.cs:154-166`) — the **only** check left is `profile.TreatmentCapabilities.Overlaps(treatmentCapabilities)`.
5. `treatmentCapabilities` here is the cable coil's `HealingComponent.TreatmentCapabilities`, which nobody set, so it is the C# default `[Biological]` (`HealingComponent.cs:72-73`). The target part's profile is `OrganicBodyPartProfile`, `treatmentCapabilities: [Biological]` (`wounds.yml:3`). `{Biological}.Overlaps({Biological})` → **true**.
6. The heal is accepted and applied: **`-3.0 Heat` and `-3.0 Shock` land on the resolved organic body part**, healing a `BurnWound`/`ElectricalWound` exactly as if the item were Ointment, at a **0.6-second delay** (`cable_coils.yml:37`) — faster than every other topical in the tree (Ointment/Brutepack/Gauze/Bloodpack all use the `HealingComponent.Delay` default of 2s; nothing else in `healing.yml` overrides it downward).
7. The `-3.0 Radiation` component is **not** in `WoundHostComponent.LocalizedDamageTypes` (§1.3), so it always lands systemically regardless of any fix — this part of the item's behaviour was never gated and is not part of the bug.

**Why this happened:** the cable coil's `Healing` block was ported from the EE/Estacao-Pirata fork (`cable_coils.yml:32-35`'s comment: *"Same as Ointment but divided by 5 and 3... Estacao Pirata IPCs"*) **before** Wolfmed's wound system existed. Its author relied on `damageContainers: [Silicon]` to keep it off organics — a mechanism that worked perfectly under Wolfgate's original flat `DamageableComponent` model, and silently stopped working the moment HOOK 8 (phase 4) taught `HealingSystem` to route wound hosts around the `DamageContainers` check. **This is not a Wolfmed authoring mistake; it is an emergent interaction between pre-existing content and the new wound system**, exactly the kind of gap D2/HOOK-8 audits are meant to catch, and it was not caught earlier because nobody had reason to look at `cable_coils.yml` until this task named it.

**Scope of the leak today:** small in magnitude (−3/−3 per 0.6s, i.e. −5/−5 per second, versus Ointment's −15/−15/−15/−10 per 2s = −7.5/−7.5/−7.5/−5 per second) but **unlimited in application count** (a cable stack holds up to 30 charges per `CableApcStack`/etc., and — being a stack item, not a limited-use consumable like most topicals — is by far the single cheapest, most available Heat/Shock healer in the game, since every player who does electrical work carries cable coils anyway). It is a real, if minor, balance leak on live organic gameplay, not merely an inert plumbing gap like HOOK 9 was.

---

## 6. The welder question

### 6.1 What exists today

`Content.Shared/Repairable/RepairableComponent.cs` is Wolfgate's **vanilla, un-extended** repair component (`Content.Shared.Repairable` namespace) used for structures/machines repair via any tool with the right `ToolQuality`. It has no `TreatmentCapabilities` field, no relationship to `WoundableComponent`, and is not part of the Wolfmed port (D5's "missing APIs" list does not mention it, and no manifest row exists for it — phase 1's WP9 dropped only `_Onyx.Repairable`/`TransplantCompatibility`, an unrelated system, as out of scope: `Docs/Wolfmed/WOLFMED_MANIFEST.md:135,530`). `Resources/Prototypes/Entities/Objects/Tools/welders.yml:1-100` (`Welder`) has a `- type: Tool` with `qualities: Welding` (`:96-99`) and **no `- type: Healing` block at all**. WG's own `MobIpc` (`Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/ipc.yml:1-131`) carries **no `- type: Repairable` either** — grepped, no hits. **An IPC in Wolfgate cannot be welder-repaired by any mechanism today**, independent of Wolfmed; this is a pre-existing content gap.

### 6.2 What Onyx actually does — and why `RepairableComponent.TreatmentCapabilities` almost never matters

Two, genuinely separate mechanisms exist in Onyx, and it is easy to conflate them (an earlier pass over this report did):

**(a) A plain `- type: Repairable` block directly on `MobIpc` itself** (`ONYX/Resources/Prototypes/Corvax/Body/Species/ipc.yml:236-248`, quoted in §2's table) — the ordinary vanilla mechanism, letting *any* welder repair an IPC's flat damage, exactly like a damaged airlock. Re-reading `ONYX/Content.Shared/Repairable/RepairableSystem.cs` in full confirms its `Repair`/`OnRepairDoAfter` call `_damageableSystem.HealEvenly`/`ChangeDamage`/`ClearAllDamage` directly on the resolved target and **never reference `TreatmentCapabilities`, `TreatmentCapability`, or any wound type**. The `treatmentCapabilities: [Mechanical]` on that block is inert for this system.

**(b) A separate, optional `WelderRepairSystem`** (`ONYX/Content.Shared/_Onyx/Repairable/WelderRepairSystem.cs`, 155 lines, read in full): a `WelderRepairModesComponent` on the welder **tool itself**, holding a dictionary of player-selectable `WelderRepairMode`s (`Name`, `Damage`, `DoAfterDelay`, `TreatmentCapabilities = [Mechanical]`, `FuelMultiplier`), driven by its own `InteractUsingEvent`/verb system and its own do-after event. It is *this* system, not (a), that reads `RepairableComponent.TreatmentCapabilities`/`RepairableBodyPartComponent` (via a companion partial, `ONYX/Content.Shared/_Onyx/Repairable/RepairableComponent.TreatmentCapabilities.cs`) — and only when the welder in the player's hand additionally carries `WelderRepairModesComponent`, i.e. a special tool, not an ordinary one.

**Consequence: "any welder repairs an IPC" in Onyx is (a), not (b), and is not a `TreatmentCapability` feature at all.** (b) is richer polish (a selectable-mode menu) layered on top, useful only to a player who owns the special modal welder. **None of (a) or (b) exists in Wolfgate** — phase 1's WP9 explicitly kept `_Onyx.Repairable` and its tests out of scope, and nothing since has reopened it (`Docs/Wolfmed/WOLFMED_MANIFEST.md:135,530`). Porting (b) wholesale would be new scope: a new component, a new verb menu, a new do-after event, ~150 LOC plus prototypes — not an annotation pass.

### 6.3 The recommended path for IPC — flag to P5-1, not this pass

Giving `MobIpc` a plain `- type: Repairable` block, mirroring `ipc.yml:236-248`'s shape (numbers to be tuned by whoever balances P5-1's IPC profile):
- costs **zero new C#** — `RepairableComponent`/`RepairableSystem` already exist in WG, unmodified, exactly as vanilla SS14/Onyx ship them;
- needs **no `treatmentCapabilities:` field at all** — the base system never reads one (§6.2a);
- **becomes wound-aware automatically**, the moment P5-1 gives `MobIpc` `WoundHostComponent` + an `IpcBodyPartProfile`: `RepairSomeDamage`/`ClearAllDamage`/`SetAllDamage` call into `DamageableSystem` on the IPC's own body, which — being a `WoundHostComponent` entity — gets intercepted by `WoundDamageRoutingSystem.OnBeforeDamageChanged`/`OnDamageDealt` (subscribed generically to any `WoundHostComponent`, `WoundDamageRoutingSystem.cs:64-65`, not filtered by caller), exactly the same routing a reagent's `HealthChange` already rides today (§1.1 Path B, confirmed end-to-end by phase 4's `reagents.md` §4.1).
- is **unscoped** (§1.1's corollary) — `CanTreatPart` returns `true` unconditionally for it — but this is harmless here because only IPC-shaped bodies would ever carry `Repairable` on themselves; a plain welder is never used this way on a human, because humans never have `RepairableComponent`. There is no organic-leak risk analogous to the cable coil's (§5).

**This is squarely P5-1's file list** (it requires `WoundHostComponent` + `IpcBodyPartProfile` to matter, and touches the IPC species prototype, not a treatment item), but it is recorded here in full because it is the actual, correct, minimal-cost answer to "does a welder heal an IPC." **Recommend flagging it to whoever implements P5-1** rather than this pass attempting it, since it is inert without P5-1's profile work regardless of which report authors the YAML line.

### 6.4 What this leaves for cybernetic *limbs* specifically — a genuinely optional `HealingComponent` addition

§6.3's `Repairable`-on-`MobIpc` fix is the right shape for a *whole-body* synthetic, but wrong for **a cybernetic limb replacement attached to an otherwise-organic human** (`_Shitmed/Body/Parts/cybernetic.yml`, §5's row 5): giving the human's own body a `Repairable` component would let a welder "repair" the *entire* human — organic tissue included — since `RepairableComponent`/`RepairableSystem` always targets `ent.Owner`, the whole mob, never a chosen part.

For that narrower case, a `HealingComponent` block on the welder is the right shape, because `WoundHealingSystem.ResolveHealingPart`/`IsCompatiblePart` (§1.1 Path A) resolves and gates a **specific part** — the exact mechanism the cable coil already uses for the same limbs (§5).

```yaml
# WOLFGATE (Wolfmed P5-3, new content): lets a welder treat Mechanical/Electrical wound-host PARTS
# (cybernetic limb replacements only — IPC repair is handled separately via Repairable, §6.3).
# Inert until P5-1 ships a bodyPartProfile with Mechanical/Electrical capability.
- type: Healing
  treatmentCapabilities: [Mechanical, Electrical]
  healWounds: true
  damageContainers:
  - Silicon
  damage:
    types:
      Blunt: -3
      Slash: -3
      Piercing: -3
      Heat: -3
  delay: 2
```
placed on `Welder` in `welders.yml`. `damageContainers: [Silicon]` (not `[Inorganic]`) because this targets the same `CyberneticPartBase`-derived limbs the cable coil already targets (`damageContainer: Silicon`, `_Shitmed/Body/Parts/cybernetic.yml:10-11`), not IPC's `Inorganic`-container parts. As established (§1.1), this field is inert on the wound-host path regardless; setting it to match the real target avoids a second silent mismatch of the kind §5 documents. `treatmentCapabilities: [Mechanical, Electrical]` (both, unlike the cable coil's `[Electrical]`-only) is a judgement call — a welder plausibly repairs both mechanical and electrical cybernetic faults where a cable coil is electrical-only, not a derived fact. Because `[Mechanical, Electrical]` shares nothing with `OrganicBodyPartProfile`'s `[Biological]`, this cannot leak onto organic tissue even by accident, and heals nothing until P5-1 ships `CyberneticBodyPartProfile` — safe to author now. No `allowedWoundStages` restriction, matching Onyx's welder-repair design (neither (a) nor (b) in §6.2 has a stage concept) — mechanical repair is meant to be usable at any wound stage.

Open questions for the user if this is taken: should every welder tier (base `Welder`, any `_Goobstation`/`_Mono` experimental/upgraded welder) get it, or only the base entity; and is `-3` per type at a 2s delay the right magnitude relative to the cable coil's faster `-3.0`/0.6s (a reasonable division — cable coil the quick field patch, welder the broader but slower tool — but a balance call, not a derived fact).

**Not recommended: porting Onyx's `WelderRepairSystem`/`WelderRepairModesComponent` (§6.2b) verbatim** for its selectable-mode verb-menu UX — ~150 LOC of new vendored systems and two new components, reopening phase 1's closed D5 scope decision, for no gameplay outcome the plain `HealingComponent` block above doesn't already deliver for the cybernetic-limb case (and §6.3's `Repairable` grant already covers the IPC case more cheaply).

---

## 7. Decisions the user must make

| ID | Question | Options | Recommendation |
|---|---|---|---|
| **D-P5-3-A** | Fix the cable-coil leak (§5) now? | (1) add `treatmentCapabilities: [Electrical]` now (2) leave as-is | **(1) Fix it now.** It is a live, if minor, balance leak on organic gameplay today (§5), the fix is one marked line, and it matches Onyx's own annotation exactly (§2). **Player-visible consequence: organic wound hosts lose the ability to self-heal Heat/Shock damage with a cable coil, immediately, until P5-1 ships a Mechanical/Electrical-capable profile.** This must be called out in the phase-5 status doc and, if there is a public changelog, to players — it is a nerf, not a neutral fix, even though it is the correct one. |
| **D-P5-3-B** | Mirror Onyx's `allowedWoundStages: [Minor]` onto Regenerative Mesh and Medicated Suture (§4, §2)? | (1) mirror Onyx (2) leave unrestricted (current) | **(2) Leave unrestricted, record as a deliberate deviation.** This is a balance change dressed as a port: Wolfgate's versions of both items were already numerically buffed well past Onyx's baseline by an earlier (Goobstation) balance pass, and restricting them to `[Minor]`-stage wounds on top of that buff is two independent balance changes compounding, not a single coherent one. If the user wants Onyx's "strong-but-early-only" design philosophy for these two items specifically, it is a one-line addition each with no code changes — cheap to take later if the balance pass wants it. |
| **D-P5-3-C** | Flag the `MobIpc`/`Repairable` finding (§6.2-§6.3) to whoever implements P5-1? | note it / ignore | **Note it.** It is the cheapest, most correct path to "a welder heals an IPC," costs zero code, but only matters once `WoundHostComponent` + an IPC profile exist — squarely P5-1's file list. |
| **D-P5-3-D** | Add a `HealingComponent` block to the base `Welder` for cybernetic-*limb* treatment (§6.4)? | (1) add it now (inert until P5-1) (2) defer to land with P5-1's `CyberneticBodyPartProfile` (3) skip | **(2) Defer to land with P5-1**, or **(1) now** if the user wants it authored ahead of time — it is inert and safe either way (§6.4). Not required for IPC (§6.3 covers that separately and more cheaply). |

### What each affected party experiences

- **Organic humans (and, per D32, Diona/Slime until their own profiles land):** the *only* observable change from this pass, if D-P5-3-A is taken, is that **cable coils stop healing burn/shock damage** — a nerf to an interaction most players and medics likely never noticed was even possible, since nothing in the item's tooltip or flavour text mentions it. No other organic item changes (D-P5-3-B recommends no change to Regenerative Mesh/Medicated Suture). If D-P5-3-B is instead taken as "mirror Onyx," Regenerative Mesh and Medicated Suture additionally stop working on anything past a Minor-stage wound — a second, larger nerf to two commonly-carried medkit items.
- **IPC, Cybernetic-limb wearers, Slime, Plant:** **no change at all from this pass alone.** `treatmentCapabilities: [Electrical]`/`[Mechanical, Electrical]` on the cable coil/welder, and a `Repairable` block on `MobIpc`, are all inert until P5-1 ships a matching `bodyPartProfile` — these species/limb types do not have `WoundHostComponent` yet (D32/D3), so none of the code paths this report examines run for them at all today. This pass is preparatory, not functional, for anyone except organic humans (who experience it as a small nerf, per above).
- **Protogen:** unaffected either way — excluded from `WoundHost` entirely (D32) and stays outside every mechanism this report covers.

### Blockers

**None.** This pass has no missing API, no missing prototype field, and no collision (§9). The only real dependency is sequencing against P5-1 (§8), which is a scheduling decision, not a technical blocker.

---

## 8. Sequencing note (re-stated for the work-package owner)

If this work package (P5-3) runs **before** P5-1 (species profiles): landing D-P5-3-A closes the cable-coil leak immediately (organic nerf, above) but delivers no positive capability until P5-1 lands — there will be a window, of unknown length, where cable coils heal nothing at all on any wound host. If P5-1 runs first and ships `CyberneticBodyPartProfile` with `treatmentCapabilities: [Mechanical, Electrical]` (mirroring Onyx, §2.1), then landing D-P5-3-A in the same or an immediately following package closes the gap with zero dead window. **Recommend sequencing this package's cable-coil fix to land in the same commit/PR as P5-1's profile work, or immediately after it**, purely to avoid a period where the item is strictly worse than today for no player-visible gain. If the user wants the fix landed defensively regardless of P5-1's timeline (e.g. because the leak is considered a correctness bug that should not wait), that is D-P5-3-A option (1) taken standalone — technically safe, just with the dead window noted above.

---

## 9. Collision and subscription audit

- **No new `SubscribeLocalEvent` pair.** This pass touches only existing `[DataField]`s (`HealingComponent.TreatmentCapabilities`/`AllowedWoundStages`, already declared at `HealingComponent.cs:72-79`) via YAML; no new C# file, no new system, no new component.
- **No new component registration.** `treatmentCapabilities:`/`allowedWoundStages:`/`healWounds:` are existing datafields on the existing `Healing` component.
- **No new `!type:` class** for the cable-coil fix (D-P5-3-A) or the stage-restriction option (D-P5-3-B) — pure data. The optional welder block (D-P5-3-D) adds one new `- type: Healing` block to an existing entity — also pure data, no new type. The optional IPC `Repairable` grant (D-P5-3-C, P5-1's job) adds one new `- type: Repairable` block to an existing entity using WG's existing, unmodified vanilla component — also no new type.
- **`grep -rn "treatmentCapabilities\s*:" Resources/Prototypes`** confirms the cable coil is the only item in the entire `Resources/Prototypes` tree that would carry a non-default value after D-P5-3-A — no other file needs re-checking for a conflicting value.

---

## 10. Test plan (feeds P5-6)

1. **T-CAP-LEAK-BEFORE** (documents the bug; a regression-documentation aid for the PR description, not a permanent assertion): a `CableApcStack` heals a Heat-damaged organic wound host's part — **passes today**, demonstrating §5's leak.
2. **T-CAP-SCOPE-ORGANIC**: after D-P5-3-A, the same cable-coil-on-organic-Heat-damage scenario **fails to heal** (`IsWoundDamaged`/`TryApplyHealing` returns false/no change) — the fix, asserted.
3. **T-CAP-SCOPE-MECHANICAL** (needs a P5-1 fixture or a test-only stub `bodyPartProfile` with `treatmentCapabilities: [Electrical]`): the same cable coil **does** heal a part using that profile — proves the annotation is correct, not merely restrictive.
4. **T-CAP-MATRIX** (the "at least three items" the DECISIONS text asks for): Ointment (`[Biological]`, unannotated) heals an organic part; cable coil (`[Electrical]`) does not heal an organic part but does heal a Mechanical/Electrical-profiled test part; Regenerative Mesh continues to heal a non-Minor-stage wound (proving D-P5-3-B's "leave unrestricted" was actually taken, not silently reverted).
5. **T-CAP-SYSTEMIC-BYPASS** (regression guard, mirrors phase 4's T-P4-SYSTEMIC-BYPASS): the cable coil's `Radiation: -3.0` component still heals an organic host's systemic Radiation damage even when its Heat/Shock components are refused by capability — guards against someone "fixing" the systemic bypass (§1.3) as a side effect of this pass.
6. *(For P5-1, not this pass, noted for continuity)* **T-CAP-IPC-REPAIR**: once `MobIpc` carries `Repairable` and P5-1's profile lands, a plain welder (no `WelderRepairModesComponent`) reduces both an IPC part's `DamageableComponent.Damage` and its `IpcMechanicalDamageWound`/equivalent severity — confirms §6.3's "wound-aware for free" claim empirically.

All headless, `Content.IntegrationTests/Tests/_WF/Wolfmed/`, matching the existing `WolfmedDamageBridgeTest.cs`/`WolfmedPainTest.cs` layout. Run `DockTest` first per project memory (`db.ef` sqlite warnings fail every pair test and mask real failures).

---

## 11. Files and difficulty

| # | File | Action | Diff | Difficulty |
|---|---|---|---|---|
| 1 | `Resources/Prototypes/Entities/Objects/Tools/cable_coils.yml` | mark: add `treatmentCapabilities: [Electrical]` to `CableStack`'s existing `Healing` block (D-P5-3-A) | 2 | **Low** |
| 2 | `Resources/Prototypes/Entities/Objects/Specific/Medical/healing.yml` | (only if D-P5-3-B is reversed by the user) mark: add `allowedWoundStages: [Minor]` to Regenerative Mesh and Medicated Suture | 4 | **Low** |
| 3 | `Resources/Prototypes/Entities/Objects/Tools/welders.yml` | (only if D-P5-3-D is taken) new marked `- type: Healing` block on `Welder` for cybernetic-limb treatment | ~12 | **Low** |
| 4 | *(P5-1's file, not this pass's — cross-referenced)* `Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/ipc.yml` | new marked `- type: Repairable` block on `MobIpc` (D-P5-3-C) | ~10 | **Low** |
| 5 | `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedTreatmentCapabilityTest.cs` | NEW — §10 | ~120 | **Low-Medium** |
| 6 | `Docs/Wolfmed/WOLFMED_MANIFEST.md`, `WOLFMED_STATUS.md` | append: record the cable-coil fix, the leak it closes, the dead-window caveat (§8), and D-P5-3-B/C/D's outcomes | — | **Low** |

**Overall difficulty: Low.** This is the cheapest work package in phase 5 by a wide margin — the entire mandatory fix (D-P5-3-A) is a two-character YAML diff, fully derived from evidence already in the tree, with no code, no new types, and no collision risk. The only real cost is the sequencing judgment call against P5-1 (§8) and the three optional decisions (B, C, D) the user may want deferred.
