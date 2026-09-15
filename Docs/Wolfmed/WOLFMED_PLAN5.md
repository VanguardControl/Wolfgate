# WOLFMED PHASE 5 — implementation plan (lead architect, revision 2)

**Scope:** DECISIONS.md "Phase 5" (2026-09-13), items P5-1 … P5-7. Phases 1–4 committed at
`2b4a4675d0 phase 4`.

- **WG** = `C:/Users/jzo12/Documents/GitHub/Wolfgate/.claude/worktrees/rules-motd-updates-11c89c`
  (branch `clanker/wolfmed-port-orchestration-454c3d`). RobustToolbox 277 is junctioned — never touched.
- **ONYX** = `C:/Users/jzo12/Documents/Wolfmed/onyx` @ `2f5bab9946539cbe083010c9ae6fbc59b47ae377`, sparse; absent paths read with
  `git -C C:/Users/jzo12/Documents/Wolfmed/onyx show HEAD:<path>`.
- Analyst inputs: `p5/species.md`, `p5/circulation.md`, `p5/capabilities.md`, `p5/numbness.md`,
  `p5/tests.md`. Critic input: `p5/CRITIQUE5.md`.
- **This revision re-verified every load-bearing claim against the real files a second time.** Where
  revision 1 was wrong, §1.4 and the Revision notes say so with the citation. Where CRITIQUE5 was wrong or
  overstated, §1.5 rejects it with the citation.

**Headline.** Phase 5 is the cheapest phase of the port by engineering volume and the most expensive by
balance surface. It adds **zero new C# types, zero new components, zero new `SubscribeLocalEvent` pairs**
and one optional in-vendored one-line hardening. Everything else is prototype data: 16 appended wound
prototypes, 4 new `_WF` part abstracts, 2 new damage containers, and 10 marked one-or-two-line upstream
edits. The phase-1 "shared stage-based metabolizer" blocker recorded in `WOLFMED_STATUS.md` is **factually
void at the pin** and is struck in WP13-7.

**The one thing revision 1 got wrong, and it changes three decisions.** Wolfgate's organic limbs **do**
carry gib triggers. Mono re-added them on two abstracts that revision 1 never looked at:

`Resources/Prototypes/Body/Parts/base.yml:275-307` (`id: MajorLimb` at `:276`)
```yaml
- type: entity # Mono
  abstract: true
  id: MajorLimb
  components:
  - type: BodyPart
    severIntegrity: 160
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTypeTrigger
        damageType: Blunt
        damage: 190 # 110->190
      behaviors:
      - !type:GibPartBehavior { }
    - trigger:
        !type:DamageTypeTrigger
        damageType: Slash
        damage: 210 # 150->210
      behaviors:
      - !type:GibPartBehavior { }
    - trigger:
        !type:DamageTypeTrigger
        damageType: Heat
        damage: 250 # 200->250
      behaviors:
      - !type:SpawnEntitiesBehavior
        spawnInContainer: true
        spawn:
          Ash:
            min: 1
            max: 1
      - !type:BurnBodyBehavior { }
      - !type:PlaySoundBehavior
        sound:
          collection: MeatLaserImpact
```
`MinorLimb` (`:310-341`) is the same shape at **Blunt 150 / Slash 180 / Heat 230**.

Every organic arm and leg is `parent: [MajorLimb, Wolfmed…]` (`:137, :156, :206, :226`); every hand and
foot is `parent: [MinorLimb, Wolfmed…]` (`:176, :191, :246, :261`). `BaseHead` is
`parent: WolfmedBaseHead` **only** (`:81-82`) — heads have **no** gib trigger at all. `BaseTorso` is
`parent: [BaseTorsoInorganic, WolfmedBaseTorso]` with its own 400/400/400 block
(`_Shitmed/Body/Parts/base.yml:47-77`).

`BasePart`'s own block really is fully commented out (`_Shitmed/Body/Parts/base.yml:8-36`, verified) — that
is what revision 1 saw. It is simply not where limb triggers live in this fork.

Consequences threaded through this plan: §1.2 P5-D8 is rewritten, §2.2's cybernetic comment is deleted,
PROTO O(b) is re-derived, PROTO R is **dropped entirely**, §8.1's diona/slime/cybernetic rows are corrected,
and three new user decisions (U15, U17, U19) exist.

---

## 1. Decisions

### 1.1 Earlier decisions that bind phase 5 (restated, unchanged)

| ID | As it applies to phase 5 | Evidence |
|---|---|---|
| **D2** | Entities without `WoundHostComponent` behave exactly as today. Phase 5 has **five** D2 pressure points: (a) appending wound prototypes to a global index, (b) the new `SiliconWolfmed` mob container, (c) the new `InorganicWolfmed` part container (U13′), (d) changing `PartIPCBase`'s gib triggers — which also affects **detached** IPC limbs, the one genuine recorded deviation (§8.3 R5), (e) the cable-coil `treatmentCapabilities` annotation. (a) and (e) are structurally D2-safe (§5.4); (b) is confined to one entity id; (c) to two part abstracts nothing else parents; (d) is a recorded deviation. | PLAN §1.1, PLAN4 §1.1 |
| **D3** | Phase 1 was organic humanoids only. **Phase 5 is the phase that lifts D3**: IPC, cybernetic limbs, slime and plant profiles land here. | DECISIONS §"Phase 5" P5-1 |
| **D4** | Balance = Onyx defaults. Phase 5 ships deliberate deviations, each a user decision in §8.4. | PLAN §1.1 |
| **D5** | Missing APIs get a `_WF/Wolfmed/Compat` shim. **Phase 5 adds no compat file.** Everything it needs already binds: `WoundableComponent.Profile`, `FractureProfilePrototype`, `WolfmedBodyPartComponent.FractureProfile`, `BloodstreamComponent.BloodReagent`. | verified §2.0 |
| **D6** | Vendored Onyx data at its Onyx relative path under `_Onyx/`; Wolfgate glue under `_WF/Wolfmed`; docs in `Docs/Wolfmed/`. | PLAN §1.1 |
| **D8** | Wolfgate stays on Shitmed's `BodyPartComponent`; Onyx's extra part fields live on `WolfmedBodyPartComponent` (`Content.Shared/_WF/Wolfmed/Body/WolfmedBodyPartComponent.cs:10-28`). **So Onyx's `- type: BodyPart / fractureProfile:` becomes `- type: WolfmedBodyPart / fractureProfile:` in every P5-1 translation.** | `WoundFractureSystem.cs:145-156` |
| **D9** | Onyx's `Chest` + `Groin` fold into Wolfgate's single `Torso`. `BodyPartType` (`Content.Shared/Body/Part/BodyPartType.cs:10-19`) has `Other, Torso, Head, Arm, Hand, Leg, Foot, Tail` — **no `Chest`, no `Groin`**. Three of the four new profiles carry `organDamage.chances` with both keys and **must** be folded or they fail to deserialize. | re-verified |
| **D20** | `Caustic` stays in `acceptedDamageTypes` and in `WoundHostComponent.LocalizedDamageTypes` (`Content.Shared/_Onyx/Wounds/WoundDamageComponents.cs:32-45`). | WG `wounds.yml:13` |
| **D22** | Body damage on a wound host is the **sum of every part's positive damage** plus systemic (`WoundDamageProjectionSystem.RefreshBodyDamage`, `:156-201`, `_damage.SetDamage(body, total)` at `:196`). That is why organics were raised to `damage: 1500` (`Entities/Mobs/Species/base.yml:270-278`). **Every new wound host needs the same review** (P5-D11). | re-verified |
| **D32** | `- type: WoundHost` sits on `BaseMobSpeciesOrganic` (`Entities/Mobs/Species/base.yml:252`); protogen is stripped at runtime by `WolfmedWoundHostExclusionSystem.ExcludedAncestors = { "BaseMobProtogen" }` (`Content.Shared/_WF/Wolfmed/Body/WolfmedWoundHostExclusionSystem.cs:12`). **Phase 5 revisits this** (P5-D9). | re-verified |
| **D33 / D29** | Body-level `PassiveDamage` is neutralised on wound hosts (`base.yml:283-288`, `damage: {}`); Onyx's per-profile `passiveRecoveryMultiplier` is the only passive heal. `IpcBodyPartProfile` and `CyberneticBodyPartProfile` set it to **0**, so those parts never passively or bed-heal. | ONYX `wounds.yml:40-41, 103-104` |
| **§8.2-1** | Onyx's `manipulationModifier` values are inverted (below 1 = *faster*). Phase 2 replaced them with the C# defaults `1.1 / 1.25 / 1.5 / 2.0` on `OrganicFractureProfile` (`WG wounds.yml:53-78`). **The same fold applies to `CyberneticFractureProfile`** (P5-D2). | WG `wounds.yml:53-56` |
| **§8.6-1** | Guns and lasers can sever; the Heat rows and the lowered finishing minimums live only in `_WF/Wolfmed/Body/parts.yml:9-22`. Phase 5 **inherits** that set for IPC and cybernetic parts rather than porting Onyx's own (P5-D7). | `_WF/Wolfmed/Body/parts.yml` |
| **§8.6-7 / P3-D7** | Organ damage is human-lineage only: `OrganDamageComponent`/`WolfmedOrgan*` are attached via 7 marked `parent:` edits in `Resources/Prototypes/Body/Organs/human.yml:53,103,150,189,215,249,270`. IPC/slime/diona/**protogen** organs carry neither. **`organDamage.chances` on the three new profiles therefore ships inert** (P5-D13), and so does protogen's `OrganicBodyPartProfile.organDamage` (P5-D19, new). | `_WF/Wolfmed/Body/organs.yml`, `OrganDamageSystem.cs:53` |
| **P4-D1 / P4-D7 / §8.4-7** | The `treatmentCapabilities` annotation pass on existing items was deferred to phase 5, "cable coil first". | `WOLFMED_STATUS.md:117-119` |
| **P4-D8 / §8.4-8** | Pain numbness was skipped and recorded in phase 4. **Phase 5 closes it permanently** (P5-D14). | `WOLFMED_STATUS.md` "Gaps still open" |
| **P2-D23** | Fracture creation is probabilistic below Comminuted (`creationChance` 0.05/0.25/0.65/1). Only a 75-Blunt hit is deterministic. Carries to `CyberneticFractureProfile` unchanged — the grade table is byte-identical. | ONYX `wounds.yml:211-231` |

### 1.2 New phase-5 decisions

| ID | Decision | Rationale and evidence |
|---|---|---|
| **P5-D1** | **Species profiles attach to the species *part* abstract, via a new `_WF` abstract placed FIRST in that abstract's `parent:` list.** Not to the mob, not by redeclaring ids. | Onyx does exactly this (`- type: Woundable / profile: X` on `OrganIpcExternal`, ONYX `Corvax/Body/Species/ipc.yml:322-330`; `OrganSlimePersonExternal` `slime.yml:232-239`; `OrganDionaExternal` `diona.yml:226-235`; `OnyxCyberneticPartBase` `_Onyx/Entities/Mobs/Customization/Parts/cybernetic.yml:1-21`). `WoundDamageProjectionSystem.SetupPart` uses `EnsureComp<WoundableComponent>(part)` (`:219-231`), which **keeps** a prototype-declared component, so YAML is the right lever. **First-parent-wins re-verified this pass:** `SerializationManager.PushComposition(Type, DataNode[], DataNode)` folds each parent onto the *accumulating* node (`RobustToolbox/Robust.Shared/Serialization/Manager/SerializationManager.Composition.cs:40-58`), and `PushInheritanceDefinition` (`:178-206`) keeps a key already present in the child unless the field is `InheritanceBehavior.Always`. `EntityPrototype.Components` is `[AlwaysPushInheritance]`, so components merge; individual `[DataField]`s inside them do not. Redeclaring `PartSlime` etc. in a `_WF` file would throw "Duplicate ID" — the same constraint phase 1 hit (`_WF/Wolfmed/Body/parts.yml:4-7`). **Warning for the implementer:** the shipped phase-1 files put the Wolfmed abstract **last** (`parent: [BaseTorsoInorganic, WolfmedBaseTorso]`, `_Shitmed/Body/Parts/base.yml:47`; `parent: [MajorLimb, WolfmedBaseLeftArm]`, `Body/Parts/base.yml:137`). That only worked because neither of those parents declares a `WolfmedBodyPart`. **Do not "normalise" the new files to match them.** |
| **P5-D2** | **The 16 prototypes are a verbatim Onyx append with exactly two classes of marked fold: (a) D9 `Chest`+`Groin` → `Torso` on the three profiles that carry `organDamage`; (b) the §8.2-1 manipulation-modifier correction on `CyberneticFractureProfile`.** Nothing else changes. | (a) is mandatory — `BodyPartType` has no `Chest`/`Groin`, and `OrganDamageRouting.Chances` is `Dictionary<BodyPartType, float>` (`WoundPrototype.cs:190-192`), so an unfolded copy fails deserialization. (b) is required for consistency: `CyberneticFractureProfile` (ONYX `wounds.yml:193-231`) is field-for-field identical to `OrganicFractureProfile` (`:153-191`) except `id:` and `wound:`, so shipping Onyx's raw `0.92/0.84/0.75/0.75` would make a **shattered cybernetic arm do-after 25 % faster** — exactly the bug the user ruled FIX on. |
| **P5-D3** | **No phase-5 `bodyPartProfile` may set `circulatoryStream`.** All four omit the key and inherit `"Organic"`. Enforced by a prototype-wide test (T-P5-14) and by a 1-line sum in `SetBleedRates`. | `git -C C:/Users/jzo12/Documents/Wolfmed/onyx grep -n "circulatoryStream" HEAD -- Resources` returns **one** hit, the declaration of `Organic` itself; none of Onyx's five profiles sets it (verified by reading ONYX `wounds.yml:1-151` in full). C# default is `"Organic"` (`WoundPrototype.cs:156-157`). WG's trimmed `SetBleedRates` reads **only** the primary key (`Content.Server/_Onyx/Chemistry/Circulation/CirculatoryStreamSystem.cs:70-80`), so a stray `circulatoryStream:` would **silently zero that species' bleeding** with no log. **Strengthened this pass:** `TryGetPartSolution` and `TryGetStreamSolution` (the other two stream-aware methods, `:32-68`) have **zero callers anywhere in WG** (`grep -rn` over `Content.Server`/`Content.Shared`, only `GetPartStream` at `WoundBleedingSystem.cs:306,319` and `SetBleedRates` at `:324` are called), so the §2.4 sum is a *complete* fix for the only reachable stream path, not a partial one. Route (a) of P5-2 is not an approximation of Onyx; it **is** Onyx. |
| **P5-D4** | **`MobIPC` becomes a wound host and gains a `Bloodstream` with `bloodReagent: Oil`.** | Onyx's `MobIpc` does exactly this (`ONYX Corvax/Body/Species/ipc.yml:92-113`: `parent: [AppearanceIpc, BaseSpeciesMob, MobBloodstream]`, `- type: WoundHost`, `bloodReferenceSolution: Oil 250`). `MobIPC` has **no** `Bloodstream` today; `PlayerSiliconHumanoidBase`'s block is commented out at `silicon_base.yml:155-169` and is **unusable verbatim** — it names a nonexistent `BloodlossIPC` modifier set, misspells `bleedRefreshAmount`/`bloodLossThreshold` (real keys `BloodRefreshAmount`/`BloodlossThreshold`, `BloodstreamComponent.cs:83-84, 62-63`), and writes `types: {Burn: 1.5}` where `Burn` is a damage **group**. `Oil` exists and is SAME in both forks (`Resources/Prototypes/Reagents/Consumable/Food/ingredients.yml:187-200`, `flammability: 2` + `!type:FlammableTileReaction`). Without a bloodstream, `WoundBleedingSystem.RefreshBody` early-returns (`Content.Server/_Onyx/Wounds/WoundBleedingSystem.cs:292-296`) and the profile's `bleedingMultiplier: 1` plus `IpcMechanicalDamageWound`'s `chance: 1` bleed behaviour are meaningless. |
| **P5-D5** | **IPC oil loss deals damage, via a new `_WF` `SiliconWolfmed` damage container — and the two `WeldingHealing` tools must gain that container id in the same package.** | WG's `Silicon` container has no `Bloodloss` (`Resources/Prototypes/Damage/containers.yml:27-34`: Brute + Heat/Shock/Radiation); `DamageableSystem` drops unsupported types silently (`DamageableSystem.cs:270-277` — `if (!dict.TryGetValue(type, out var oldValue)) continue;`). Option 2 also restores phase 3's P3-D1 vital-part `Bloodloss` charge, which `WolfmedBodyPartLifecycleSystem.OnPartRemoved` already computes and applies **to the body** (`Content.Server/_WF/Wolfmed/WolfmedBodyPartLifecycleSystem.cs:85-93`) and which the body container today throws away. **Companion edits are mandatory:** `WeldingHealableSystem.OnRepairFinished` gates on `damageable.DamageContainerID` ∈ the tool's `damageContainers` (`Content.Server/_EinsteinEngines/Silicon/WeldingHealable/WeldingHealableSystem.cs:29-41`), and both the welder (`welders.yml:117-119`) and the Mono nanite applicator (`_Mono/.../nanite_applicator.yml:47-50`) list `[Silicon]`. **Changing `MobIPC`'s container without touching those two lists silently removes the only way to repair an IPC in the game.** |
| **P5-D5b** | **`SiliconWolfmed` adds `Bloodloss` as a `supportedTypes` entry, NOT the whole `Airloss` group.** | Revision 1 took the group and justified it with "the Tourniquet's `Asphyxiation: 5` application cost also lands on IPCs". **That justification is false.** `TourniquetSystem.OnDoAfter` applies the cost with `_damage.TryApplyPartDamage(body, part, tourniquet.Comp.Damage, args.Args.User)` (`Content.Server/_Onyx/Medical/Tourniquet/TourniquetSystem.cs:88-89`) — to the **part**, whose container `SiliconWolfmed` does not change. The stated benefit can never materialise, and the group's only real effect is to open `Asphyxiation` as a live damage type on an entity that dies at a projected total of **100** (`_EE ipc.yml:70-73`). Practical exposure is small (the only C# writer of `Asphyxiation` damage is `RespiratorSystem`, and `MobIPC` has no `Respirator`; `MobAtmosExposed` deals only `Cold`/`Heat`, `Entities/Mobs/base.yml:147-161`), but the surface is unaudited and buys nothing. One word removes it. |
| **P5-D6** | **`bloodlossDamage` on `MobIPC` uses `Bloodloss`, never `Heat`.** | `species.md` §6.4 proposed `{Heat: 1.5}` to dodge the container gap. That is wrong: `Heat` **is** in `WoundHostComponent.LocalizedDamageTypes` (`WoundDamageComponents.cs:32-45`), so bloodloss damage would be routed to a body part, create `IpcMechanicalDamageWound` severity, which bleeds at `chance: 1` — **a self-reinforcing bleed loop**. `Bloodloss` is not localized, so it stays systemic and self-limits (it heals back via `bloodlossHealDamage` once topped up, `BloodstreamSystem.cs:162-176`). |
| **P5-D7** | **IPC and cybernetic limbs inherit Wolfgate's organic amputation thresholds from `Base<Slot>`; Onyx's own numbers are NOT ported.** | Onyx's IPC arm is `Slash 270 / Piercing 400 / Blunt 600` (ONYX `Corvax/Body/Species/ipc.yml:365-372`) and its cybernetic arm reuses exactly those (`_Onyx/.../cybernetic.yml:30-34`), against `WolfmedBaseLeftArm`'s `Slash 130 / Piercing 250 / Blunt 250 / Heat 250` (`_WF/Wolfmed/Body/parts.yml:52-59`). Onyx's set sits on a part chain whose gib ceiling is different from Wolfgate's; under Wolfgate's `MajorLimb`/`PartIPCBase` triggers a `Blunt 600` threshold is unreachable by many multiples. `WolfmedPartIpc`/`WolfmedPartCybernetic` therefore declare **no** `amputationThresholds` key, so RT inherits `Base<Slot>`'s (P5-D1). |
| **P5-D8** | **REWRITTEN. `PartIPCBase`'s two gib triggers go from Blunt 110 / Slash 150 to Blunt 190 / Slash 210 — parity with `MajorLimb`, the trigger every organic arm and leg actually carries — with no Heat rung added.** | Revision 1 said "Organic parts have no such trigger at all" and proposed 400/400 on that basis. **False** (see Headline): organic arms/legs gib at Blunt 190 / Slash 210 / Heat 250, hands/feet at 150/180/230, torsos at 400/400/400, heads never. 400/400 would make an IPC limb **2.1× tougher than a human arm**, and (D2, §8.3 R5) a *detached* IPC limb on the floor 2.1× tougher than a detached human one. That is a balance grab, not regression-prevention. 190/210 is the smallest change that makes `DismembermentWound` reachable for Blunt-adjacent mixed damage while keeping IPC arms and legs exactly as durable as flesh ones. `PartIPCBase` carries `- type: Destructible # no ashing trigger` with `!type:DamageTypeTrigger Blunt 110` and `Slash 150` → `GibPartBehavior` (`_EinsteinEngines/Body/Parts/ipc.yml:11-24`) and, being **first** in every IPC part's `parent:` list, it wins wholesale over `MajorLimb`/`MinorLimb` (`thresholds` is a plain `[DataField("thresholds")]` list, `Content.Server/Destructible/DestructibleComponent.cs:12-13`, so child replaces parent). `GibPartBehavior.Execute` calls `system.BodySystem.GibPart(owner, part)` with **no attached-to-a-body guard** (`Content.Server/_Shitmed/Destructible/Thresholds/Behaviors/GibPartBehavior.cs:13-19`); `GibPart` gates only on `IsPartRoot` and `part.CanSever` (`SharedBodySystem.Body.cs:385-390`, `CanSever` defaults `true`, `BodyPartComponent.cs:63`) and always `QueueDel`s the part (`GibbingSystem.cs:190-192`, `BodySystem.cs:166-167`). **The threshold is dormant today** (IPC damage is never routed to parts) and P5-1 activates it. **Two residual asymmetries are accepted and documented:** an IPC hand/foot gibs at 190/210 where an organic one gibs at 150/180 (tougher), and an IPC **head** gibs at 190 where an organic head has no gib trigger at all. Per-slot parity would need five more upstream blocks — offered as U3′(d). |
| **P5-D8b** | **NEW. `CyberneticPartBase` gains its own marked `Destructible` (Blunt 190 / Slash 210, `GibPartBehavior`, no Heat rung), mirroring EE's own `# no ashing trigger` precedent on `PartIPCBase`.** | `CyberneticPartBase` declares **no** `Destructible` (`_Shitmed/Body/Parts/cybernetic.yml:1-14`, read in full: `Sprite`, `Icon`, `Damageable{damageContainer: Silicon}`, `Cybernetics`, `PirateBountyItem`), and every concrete cybernetic limb takes the *other* parent's: `LeftArmCyberneticBase: parent: [ CyberneticPartBase, BaseLeftArm ]` (`:17-19`), and the same for the three other limb bases and the four hand/foot ids. So **today, already, before phase 5**, a cybernetic arm on a (wound-host) human is destroyed outright at Blunt 190, and at **Heat 250 a steel prosthetic spawns `Ash`, runs `BurnBodyBehavior` and plays the `MeatLaserImpact` flesh sound** (`BurnBodyBehavior.cs:29-34` → `SharedBodySystem.BurnPart`). This is pre-existing, not introduced by phase 5 — but phase 5 is the first document with the numbers side by side, and revision 1's comment asserting the opposite would have taught the implementer a false model. The one marked block also makes cybernetic hands/feet 190/210 rather than 150/180, which is defensible for a steel hand. **Optional** — U15. |
| **P5-D9** | **The protogen `WoundHost` exclusion is lifted; protogen becomes a plain organic wound host.** | `BaseMobProtogen` is biologically organic in every component it carries: `damageContainer: Biological` + `damageModifierSet: Protogen` (`_Mono/Entities/Mobs/Species/protogen.yml:31-33`), `- type: Hunger` (`:27`), `- type: Thirst` (`:34`), `Butcherable → FoodMeatHuman ×5` (`:35-39`), `- type: Respirator` (`:78-84`), `PartProtogen: parent: [BaseItem, BasePart]` → `damageContainer: OrganicPart` (`_Mono/Body/Parts/protogen.yml:4-5`), `Extractable` juice of `Fat` + `Blood` (**`_Mono/Body/Parts/protogen.yml:13-19`** — revision 1 cited the wrong file), and the inherited `MobBloodstream` with `bloodReagent: Blood`. Concrete parts are `parent: [PartProtogen, BaseTorso]` etc., so they inherit the full `WolfmedBase<Slot>` organic data for free. The only synthetic markers are `Deathgasp prototype: SiliconDeathgasp`, a robot typing indicator and a voice pack. Today it is the one species that silently loses the tourniquet (P4-D11), the analyzer panel, the pain overlay, every wound surgery and all localized damage. Cost: emptying one `HashSet` literal. **Census corrected:** the hosts created are `MobProtogen` (`_Mono/Entities/Mobs/Player/protogen.yml:4-5`, `roundStart: false`) and `MobProtogenRandom` (`_Goobstation/Entities/Mobs/Player/humanoid.yml:225-227`, `parent: MobProtogen`). `MobProtogenDummy` parents `BaseSpeciesDummy` (`_Mono/.../protogen.yml:86-89`) and is **not** affected. |
| **P5-D10** | **IPC feels pain (Onyx default) AND gains `- type: PainShockTarget`.** | `IpcBodyPartProfile` never sets `canFeelPain: false` — only `CyberneticBodyPartProfile` does (ONYX `wounds.yml:102`), and `IpcMechanicalDamageWound` carries `!type:WoundPainBehavior painPerSeverity: 0.87` (ONYX `:607-609`). Onyx puts `- type: PainShockTarget # <Onyx-PainShock>` on **`BaseSpeciesMob`** (ONYX `Resources/Prototypes/Body/species_base.yml:54`, `id:` at `:11`), and Onyx's `MobIpc` parents `[AppearanceIpc, BaseSpeciesMob, MobBloodstream]` (`ONYX Corvax/Body/Species/ipc.yml:93-97`) — **so Onyx IPCs do get pain shock**. Wolfgate put it on `BaseMobSpeciesOrganic` instead (P2-D7, `base.yml:256`), which `MobIPC` does not inherit; that is what creates the half-state `species.md` objected to. One marked line restores Onyx parity. **`EmoteOnDamage` is NOT added:** Onyx's copy uses the broken `emotes:` key (§8.2-3) and `MobIpc`'s `allowedEmotes: [Boop, Whirr]` (ONYX `ipc.yml:139`) excludes `Scream` anyway. Note pain shock itself force-emotes `"Scream"` with `forceEmote: true` (`PainSystem.cs:299-300`) — an IPC will scream on shock; flavour flag in §8.3 R4. |
| **P5-D11** | **REWRITTEN. `MobIPC`'s own `Destructible` Blunt trigger is raised 400 → 1500. `PlayerSiliconHumanoidBase` is NOT touched, and `MobThresholds` is NOT touched.** | Same reason D22 raised organics to 1500 (`base.yml:270-278`): post-death hits keep landing on parts and `RefreshBodyDamage` keeps summing them, so a flat 400 is crossed by routine corpse damage. `MobIPC` declares its own single-threshold `Destructible` (`_EE ipc.yml:80-87`). **Revision 1's PROTO R is dropped:** `DestructibleComponent.Thresholds` is a plain `[DataField("thresholds")]` (`Content.Server/Destructible/DestructibleComponent.cs:12-13`) and RT composes a field across parent and child only when the behaviour is `Always` (`SerializationManager.Composition.cs:196-206`), so `MobIPC`'s list **replaces** `PlayerSiliconHumanoidBase`'s `!type:DamageTrigger damage: 500` (`silicon_base.yml:91-96`) wholesale. And `MobIPC` is the only entity in the tree that parents `PlayerSiliconHumanoidBase` (`grep -rn` over `Resources/Prototypes` → 3 hits: the id, `MobIPC`'s `parent:`, and a TODO comment at `Entities/Mobs/Species/base.yml:9`). The 500 trigger is unreachable; editing it changes nothing and adds a tracked upstream file for zero behaviour. Death still occurs at the projected total of 100 (`_EE ipc.yml:70-73`), exactly as today. |
| **P5-D12** | **The cable coil gains `treatmentCapabilities: [Electrical]`, landing in the same phase as P5-1.** | `CableStack`'s `- type: Healing` (`Resources/Prototypes/Entities/Objects/Tools/cable_coils.yml:36-45`) relies on `damageContainers: [Silicon]` to stay off organics — but HOOK 8 makes `HealingSystem.TryHeal` skip that check entirely for wound hosts (`Content.Server/Medical/HealingSystem.cs:201-210`, the `!woundHost &&` guard), and `WoundHealingSystem.IsCompatiblePart` **accepts `damageContainers` as a parameter and never reads it** (`Content.Server/_Onyx/Wounds/WoundHealingSystem.cs:154-166`). The only surviving filter is `TreatmentCapabilities`, which defaults to `[Biological]` (`Content.Server/Medical/Components/HealingComponent.cs:72-73`, with `HealWounds` defaulting **true** at `:66-67`) and **overlaps `OrganicBodyPartProfile`**. So today a cable coil heals a human's Heat and Shock wounds at −3/−3 per **0.6 s** with unlimited stack charges. Onyx's own coil sets `treatmentCapabilities: [Electrical]` + `healWounds: true` (ONYX `cable_coils.yml:178-193`). One marked line. |
| **P5-D13** | **`organDamage.chances` on the Ipc/Slime/Plant profiles ships verbatim but inert; documented, with a regression canary rather than a positive organ test.** | `OrganDamageSystem` requires `OrganDamageComponent` on the organ (`Content.Server/_Onyx/Wounds/OrganDamageSystem.cs:53`); only the seven `OrganHuman*` ids carry it (P3-D7). Shipping the data keeps the file a verbatim Onyx copy and costs nothing. |
| **P5-D14** | **P5-4 (pain numbness / narcotics) is SKIPPED and CLOSED — not re-deferred.** | Zero consumers, second consecutive phase. Onyx's entire opioid family (`ONYX _Onyx/Reagents/Narcotics/opioids.yml`) uses **only** `SuppressPain`; `StatusEffectPainNumbness` appears on exactly two recreational stimulants (Desoxyephedrine, StrawberryIce). There is no `Morphine` reagent in WG at all. `SuppressPain` already reaches 3 of the 5 pain surfaces. Full parity is not ~45 LOC: Onyx's `PainNumbnessSystem.cs` is the same fully-qualified type as Wolfgate's pre-Wolfmed `Content.Shared/Traits/Assorted/PainNumbnessSystem.cs`, so parity means a hook + `_WF` partial, ~165 LOC. Species pain absence is `canFeelPain`, a P5-1 profile field, not a narcotics question. |
| **P5-D15** | **`MobIPC` does NOT gain `- type: Repairable`.** | `RepairableSystem` subscribes `<RepairableComponent, InteractUsingEvent>` (`Content.Shared/Repairable/RepairableSystem.cs:22`) and `WeldingHealableSystem` subscribes `<WeldingHealableComponent, InteractUsingEvent>` (`WeldingHealableSystem.cs:25`). `MobIPC` already carries `- type: WeldingHealable` (`_EE ipc.yml:129`). Adding `Repairable` puts two independent welder do-afters on the same interaction with no shared `Handled` contract — a double-repair race, for a capability WG already has. (Different components, so this is a behaviour risk, **not** the duplicate-directed-subscription crash class.) The welder keeps working through `WeldingHealable`, whose `TryChangeDamage` on the mob is routed to parts with **no** treatment-capability scope (`CanTreatPart` returns `true` when no scope is open, `WoundDamageRoutingSystem.cs:971-974`), so it heals `IpcMechanicalDamageWound` for free once P5-1 lands. |
| **P5-D16** | **No `- type: Healing` block is added to the welder in phase 5.** | `IpcBodyPartProfile` and `CyberneticBodyPartProfile` are both `[Mechanical, Electrical]`, and the P5-D12 cable coil is `[Electrical]` — **the coil already overlaps both**, so IPC and cybernetic parts get a working localized treatment tool the moment P5-1 lands. Adding a `Healing` block to a tool that already carries `WeldingHealing` risks the same double-handler tangle as P5-D15 (`InteractUsing` fires before `AfterInteract`). Deferred to the balance pass, recorded. |
| **P5-D17** | **The mechanical analyzer/examine wording (P5-5) ships as three analyzer strings plus one examine string; the seven `health-examinable-part-damage-*` mechanical variants are deferred.** | The payload `HealthAnalyzerWoundDiagnostic` (`Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs:9-24`) carries no material information, so the client cannot vary wording today. One appended positional member is a `_Onyx` in-vendored edit with exactly **one** construction site in the tree (`Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs:118`), mirroring how `HealthAnalyzerScannedUserMessage` already gained four appended fields in phase 4 (EXT 2). Onyx itself does none of this — its IPCs examine as "bruises" — so this is a Wolfgate improvement and the adjective set is legitimately deferrable. |
| **P5-D18** | **Skeleton stays out of the wound system, recorded as a known exclusion.** | `MobSkeletonPerson` parents `BaseMobSkeletonPerson → [MobFlammable, BaseMobSpecies]`, i.e. the non-Organic base, so it never had `WoundHost`. It is mechanically ready (`damageContainer: Biological`, `BasePart`-derived limbs, a real `Skeleton` body prototype) but "bone fracture" and "bleeding" on an undead skeleton need their own design. Never previously recorded anywhere — WP13-7 adds the manifest line. |
| **P5-D19** | **NEW. Protogen ships as an organic wound host with NO organ damage, organ destruction, internal bleeding or organ surgery — a stated deviation, not an omission.** | `OrganDamageComponent`/`WolfmedOrgan*` are attached only to the seven `OrganHuman*` ids (`Body/Organs/human.yml:53,103,150,189,215,249,270`). Protogen's body prototype uses `OrganProtogenBrain/Eyes/Lungs/Heart/Stomach/Liver/Kidneys`, all parented to `BaseProtogenOrgan` / `BaseProtogenOrganUnGibbable` (`_Mono/Body/Organs/protogen.yml:39,89,136,175,201,235,256`) — **none carries `OrganDamage` or `WolfmedOrgan`**. (`OrganProtogenEars` at `:126-127` parents `BaseHumanOrgan`, but ears are not one of the seven instrumented organs.) So `OrganicBodyPartProfile.organDamage.chances` (`WG wounds.yml:28-35`, `maxAffected: 2`) rolls on every hit for nothing, there is no `InternalBleedingWound` path, and no `SurgeryHeal<Organ>` chain applies. **This gap is pre-existing for every non-human organic host already shipped** (moth, vox, arachnid, diona, slime…), but phase 5 is the first phase to newly enrol a species into it, so it is recorded here. U12′. |
| **P5-D20** | **NEW. Wolfgate's part damage containers differ per species and that is what makes `Cold`/`Caustic` inert — but the fix is a `_WF` PART container, and it genuinely works.** | IPC parts use **`Inorganic`** (from `BasePartInorganic`, `Body/Parts/base.yml:8-16`: Brute + Heat/Shock); cybernetic parts use **`Silicon`** (`CyberneticPartBase` overrides, `cybernetic.yml:10-11`: Brute + Heat/Shock/Radiation); organic parts use `OrganicPart`. Onyx's `SiliconIpc` is Brute + the **`Burn` group** + `Electronic` (`ONYX Corvax/Damage/containers.yml`), and `Burn` = `Heat, Shock, Cold, Caustic` in both forks (`Resources/Prototypes/Damage/groups.yml:9-16`). **So Onyx IPC and cybernetic parts really do take Cold and Caustic, and Wolfgate's really do not** — two of the seven `acceptedDamageTypes` and two of the six `damageTypes` on both mechanical wounds are dead. This is a Wolfgate-only loss versus Onyx, not a hole Onyx shares. **The fix works because routing runs before the body's container filter:** `DamageableSystem.TryChangeDamage` raises the Wolfmed `DamageDealtEvent` seam at `:253-264`, and the container filter (`if (!dict.TryGetValue(type, …)) continue;`) is at `:270-277` — so a `Cold`/`Caustic` hit on an IPC reaches routing even though the *mob* container drops it, and it is the **part's** container that discards it. One new `_WF` container applied to the two part abstracts phase 5 already edits restores both types, touches no shared container, and is D2-safe. U13′. |

### 1.3 Disagreements between the analyst reports, resolved

| # | Conflict | Resolution | Evidence |
|---|---|---|---|
| 1 | `tests.md` §1.3: *"No cybernetic prosthetic part prototype exists in Wolfgate at all."* vs `species.md` §4.4: 14 entities exist. | **`species.md` is right; `tests.md` is wrong — but only ten of the fourteen are spawnable.** | `Resources/Prototypes/_Shitmed/Body/Parts/cybernetic.yml`: **abstract** — `CyberneticPartBase` (`:4`), `LeftArmCyberneticBase` (`:19`), `RightArmCyberneticBase` (`:33`), `LeftLegCyberneticBase` (`:47`), `RightLegCyberneticBase` (`:61`); **concrete** — `LeftHandCybernetic` (`:74`), `RightHandCybernetic` (`:82`), `LeftFootCybernetic` (`:90`), `RightFootCybernetic` (`:98`), `JawsOfLifeLeftArm` (`:106`), `JawsOfLifeRightArm` (`:120`), `SpeedLeftLeg` (`:134`), `SpeedRightLeg` (`:148`), `DexLeftHand` (`:161`), `DexRightHand` (`:170`). **`JawsOfLifeLeftArm` is the only concrete cybernetic *arm*** — every test that needs one must spawn that id. Content reachability is fine: lathe recipes `Recipes/Lathes/robotics.yml`, the Mono prybar trait (`Content.Server/_Mono/Traits/Physical/PrybarProstheticsSystem.cs`), and the Goob autosurgeon. |
| 2 | `tests.md` §1.3: *"No reagent named `Oil`/`Coolant` exists in WG"* and *"a Diona bleeds red blood"*. | **Both wrong.** | `Oil` at `Reagents/Consumable/Food/ingredients.yml:187`; `Slime` and `Sap` in `Reagents/biological.yml`. `Entities/Mobs/Species/diona.yml:49 bloodReagent: Sap`, `slime.yml:83 bloodReagent: Slime`. Slime and diona circulation needs **zero** work. |
| 3 | `species.md` D-P5-1-B: IPC should get `canFeelPain: false`. `tests.md` trap 2: keep Onyx's default and pin it with a test. | **`tests.md` wins, with `species.md`'s missing-`PainShockTarget` observation fixed rather than used as an argument against.** See P5-D10. | ONYX `species_base.yml:54` is on `BaseSpeciesMob`, which Onyx's `MobIpc` parents. |
| 4 | `species.md` §6.4: IPC `bloodlossDamage: {Heat: 1.5}`. `circulation.md` §5.3: a new `SiliconWolfmed` container with `Airloss`. | **`circulation.md` wins on the container, for a reason neither report gave (P5-D6) — but its `Airloss` *group* is wrong (P5-D5b).** | `WoundDamageComponents.cs:32-45`; `TourniquetSystem.cs:88-89` |
| 5 | `circulation.md` §4.5(B): *"`damageModifierSet: IPC` must be repeated — `Damageable` is one component, not a merge."* | **Wrong.** Component data merges per key: `ComponentRegistrySerializer` matches by registration and pushes composition per field; a key absent on the child is taken from the parent (`SerializationManager.Composition.cs:196-206`). Repeating it is harmless but not required; this plan repeats it for readability and marks why. | re-verified |
| 6 | `species.md` §12 T3: *"P4's `SurgeryMendFracture` completion check must be re-verified against `CyberneticFractureProfile`; it may need a capability gate."* | **Downgraded from a blocker to a test.** The chain is entirely profile-agnostic: `WolfmedSurgeryConditionSystem.OnFractureCheck` reads `_fractures.GetFracture(args.Part)` and compares `Comp2.Treatment` against the effect's target; `SurgeryMendFracture`'s gate is `WolfmedSurgeryFractureCondition minGrade: Hairline` (`_WF/Wolfmed/Surgery/surgeries.yml:33-45`) — no wound id, no capability, no profile anywhere. `- type: SurgeryTarget` is on `PlayerSiliconHumanoidBase:319`, so IPC surgery is reachable. `CyberneticFractureProfile` has the same `reductionMinimumGrade: Simple` and `removeWoundWhenMended: true`. It works; it is only *thematically* odd (bone gel on a steel frame). T-P5-8 asserts it. | re-verified |
| 7 | `capabilities.md` D-P5-3-C: give `MobIpc` a `Repairable` block. | **Rejected** — see P5-D15. | `RepairableSystem.cs:22`, `WeldingHealableSystem.cs:25`, `_EE ipc.yml:129` |
| 8 | `circulation.md` §9.1 lists the `SiliconWolfmed` change as self-contained. | **Incomplete.** It silently breaks welder and nanite-applicator repair of IPCs; two extra marked lines are mandatory. See P5-D5. | `WeldingHealableSystem.cs:29-41`, `welders.yml:117-119`, `nanite_applicator.yml:47-50` |
| 9 | `tests.md` decision 1: leave protogen excluded. `species.md` D-P5-1-C: lift it. | **`species.md` wins on evidence** (P5-D9); `tests.md`'s argument was cost, not evidence, and the cost is one `HashSet` literal. Presented to the user as U4 all the same, now with the organ gap (P5-D19) stated. | `_Mono/Entities/Mobs/Species/protogen.yml:27-40` |

### 1.4 Where revision 1 of this plan was wrong (all fixed below)

| # | Revision-1 claim | Truth | Where fixed |
|---|---|---|---|
| E1 | "Organic parts have no such trigger at all — `BasePart`'s is fully commented out." | `BasePart`'s is commented out, but Mono's `MajorLimb` (Blunt 190 / Slash 210 / Heat 250 + Ash) and `MinorLimb` (150/180/230) carry them and every organic arm/leg/hand/foot parents one of them. Heads have none; torsos have 400/400/400. | Headline, P5-D8, PROTO O(b), §8.1, U3′ |
| E2 | "cybernetic limbs carry no gib trigger at all (`CyberneticPartBase -> BasePartInorganic`, which declares no `Destructible`)." | `CyberneticPartBase` declares none, but it is only the *first* of two parents; `BaseLeftArm` supplies `MajorLimb`'s. Cybernetic limbs already gib at 190/210 and burn to `Ash` at Heat 250, **today**. | §2.2 (comment deleted), P5-D8b, U15, T-P5-19 |
| E3 | PROTO R: "`PlayerSiliconHumanoidBase`'s `!type:DamageTrigger damage: 500` is an all-types total and is the tighter of the two — it must be raised." | `MobIPC` declares its own `Destructible`, and `thresholds` is a non-`Always` `[DataField]`, so the child list replaces the parent's. `MobIPC` is the only descendant. The 500 trigger is unreachable; PROTO R is a no-op. | PROTO R **dropped**; upstream count 56 → 55 |
| E4 | §8.1: Diona — "NO — cannot be severed at all." | `amputationThresholds: {}` disables `AmputationSystem` only. Diona and slime limbs still inherit `MajorLimb`/`MinorLimb` and are **destroyed outright** at 190/210/250 — neither severable nor reattachable. | §8.1, U17, T-P5-20 |
| E5 | §2.3: "Airloss is taken as the whole group … so the Tourniquet's `Asphyxiation: 5` application cost also lands on IPCs." | The tourniquet cost is applied to the **part** (`TourniquetSystem.cs:88-89`), whose container does not change. The stated benefit cannot materialise; revision 1's own deviation 16 says the opposite. | P5-D5b, §2.3 |
| E6 | P5-D5: "Onyx has the identical hole (its `SiliconIpc` container also omits Airloss)." | True for Airloss, **false for `Cold`/`Caustic`**: Onyx's `SiliconIpc` takes the whole `Burn` group. This is a Wolfgate-only loss. | P5-D20, deviation 9, U13′ |
| E7 | P5-D9: "`roundStart: false`, 2 mob prototypes (`MobProtogen`, `MobProtogenDummy`)"; `Extractable` cited at `protogen.yml:14-20`. | The second host is `MobProtogenRandom` (`_Goobstation/.../humanoid.yml:225-227`); `MobProtogenDummy` parents `BaseSpeciesDummy`. `Extractable` is at `_Mono/Body/Parts/protogen.yml:13-19`. | P5-D9 |
| E8 | T-P5-4 / §1.3 row 1: "attach a real `LeftArmCyberneticBase`-derived part". | `LeftArmCyberneticBase` is `abstract: true`. The only concrete cybernetic arm is `JawsOfLifeLeftArm`. | §1.3 row 1, T-P5-4 |
| E9 | R8: "`TorsoIPC` has no `WolfmedBodyPart` at all." | True today, **false after PROTO O(a)** — `TorsoIPC: parent: [PartIPCBase, BaseTorsoInorganic]` will inherit `WolfmedPartIpc`'s block with `MaxDamage` at the component default `0` (`WolfmedBodyPartComponent.cs:16`). The conclusion (overflow disabled, correct for a torso) survives; the reason is rewritten. | §8.3 R8 |
| E10 | §7.1: "trimmed (13 of 29 prototypes)" → "complete (29 of 29)". | WG's `wounds.yml` holds **14** prototypes (`id:` at `3, 39, 83, 136, 187, 236, 245, 294, 341, 374, 385, 398, 405, 416`); Onyx's holds **30**. 14 + 16 = 30. | §7.1 |
| E11 | §7.1 lists `Content.Shared/_Onyx/Chemistry/Circulation/CirculatoryStreamSystem.cs`. | The file is at `Content.Server/_Onyx/Chemistry/Circulation/CirculatoryStreamSystem.cs` (its *namespace* is `Content.Shared._Onyx.Chemistry.Circulation`, which is what misled it). | §7.1, §3.3 |
| E12 | §7.2 cable-coil row: "Matches Onyx exactly." | Only the capability line matches. Onyx's coil is `delay: 3.5` with `Blunt/Slash/Piercing/Heat -3, Shock -5` and `damageContainers: [SiliconIpc]`; WG's is `delay: 0.6` with `Heat/Shock/Radiation -3` and `[Silicon]`. Phase 5 changes **one** line and leaves the rest. | §7.2 |
| E13 | Line-number drift: ONYX four-profile span "`36-142`" (really `37-151`); `IpcBodyPartProfile` recovery "`:39-40`" (really `:40-41`); `OrganSlimePersonExternal` "`slime.yml:230-243`" (id at `:232`, block `:235-239`); `OrganDionaExternal` "`diona.yml:224-236`" (id at `:226`, block `:229-235`); `OrganIpcExternal` "`ipc.yml:321-330`" (id at `:322`, block `:325-330`); `MobIPC` Destructible "`:76-87`" (really `:80-87`); `silicon_base` "`:91-95`" (really `:91-96`); `CyberneticBodyPartProfile canFeelPain` "`:101`" (really `:102`). Values quoted were all correct. | corrected throughout |
| E14 | §3.4: lists `HealthExaminable` and `DamageVisuals` among "`…/Player/ipc.yml`'s … untouched" components. | Both are on `PlayerSiliconHumanoidBase` (`:284`, `:269`), a different file — which, with PROTO R dropped, phase 5 now genuinely does not touch. The framing is corrected rather than contradicted. | §3.4 |

### 1.5 Where CRITIQUE5 is rejected or narrowed (with evidence)

| # | CRITIQUE5 | This plan's position |
|---|---|---|
| C1 | **B1 rated a blocker**: "half of `CyberneticBodyPartProfile` ships dead". | **Narrowed to major + a mandatory text fix.** The gib triggers are real (accepted, E2), but they are **pre-existing** — cybernetic limbs on human wound hosts already gib at 190/210 and ash at Heat 250 today, before phase 5. And `DismembermentWound` is **not** dead: `WolfmedBaseLeftArm`'s `Slash 130` and `Piercing 250` are both below/outside their gib rungs (Slash gib 210; there is **no Piercing rung at all**), so Slash and Piercing severing work. Only *pure* Blunt severing is unreachable, exactly as it already is for flesh. Accepting P5-D8b (U15) is a genuine improvement, not a prerequisite. |
| C2 | B1: hands/feet at "Blunt 150, which is *exactly* `WolfmedBaseLeftHand`'s Blunt amputation threshold — a coin-flip race." | **Not a race; the gib wins deterministically.** `AmputationSystem.HandlePartDamageApplied` needs **two** qualifying hits: the hit that crosses the threshold only calls `SetSeverable(part, true)` and returns (`AmputationSystem.cs:88-95` in the `!part.Comp.Severable` branch); amputation happens on a *later* finishing hit (`:109-112`). `DestructibleSystem` fires on the very hit that crosses 150. Wherever gib threshold ≤ amputation threshold for the same type, the part is destroyed first, every time. |
| C3 | B2 fix: "Change PROTO O(b) to Blunt 190 / Slash 210 (`MajorLimb` parity)". | **Accepted for arms and legs, with two asymmetries the critique did not surface:** `PartIPCBase` is a single block that also governs IPC **hands/feet** (organic parity would be `MinorLimb` 150/180) and the IPC **head** (organic parity is *no trigger*). 190/210 therefore makes IPC extremities tougher than flesh and leaves the IPC head destructible where a human head is not. Stated in P5-D8 and offered as U3′(d). |
| C4 | M3: taking the `Airloss` group "silently opens `Asphyxiation` on an entity that dies at 100 total". | **Accepted, severity narrowed.** The false justification is the real defect. Practical exposure is small: the only C# writer of `Asphyxiation` damage is `RespiratorSystem` and `MobIPC` has no `Respirator`; `MobAtmosExposed` deals `Cold`/`Heat` only (`Entities/Mobs/base.yml:147-161`); and with no metabolizer, reagent sources cannot reach it. The fix is taken anyway — it costs one word. |
| C5 | M4 fix: "`InorganicWolfmed` = `supportedGroups: [Brute, Burn]` + `supportedTypes: [Shock]`". | **Accepted with the shape corrected.** `Shock` is already inside the `Burn` group, so that `supportedTypes` entry is redundant; and the two part bases use *different* stock containers (`Inorganic` for IPC, `Silicon` for cybernetic), so a single replacement must be a superset: `supportedGroups: [Brute, Burn]` + `supportedTypes: [Radiation]`. The critique also did not establish *why* a part-level container is sufficient — it is, because routing runs before the body's type filter (P5-D20). |
| C6 | M6: "under U2(a) a leaking IPC **stutters and becomes drunk**." | **Accepted as a flavour note, not a defect.** The bloodloss branch (`BloodstreamSystem.cs:141-162`) is unguarded, so this is exactly what **every organic wound host already experiences** when bleeding out; it is Wolfgate-consistent, not IPC-specific. Listed in §8.1 and R4 as flavour. The `chemicalMaxVolume` half of M6 is accepted in full (U16). |
| C7 | M5: "the same gap applies to every non-human organic species already shipped as a host … so it is pre-existing". | **Agreed and adopted verbatim** as P5-D19, with the additional datum that `OrganProtogenEars` does parent `BaseHumanOrgan` (`_Mono/Body/Organs/protogen.yml:126-127`) — irrelevant, because ears are not one of the seven instrumented organs. |
| C8 | V-8 "the phase-1 metabolizer blocker really is void". | **Agreed, and strengthened**: `TryGetPartSolution`/`TryGetStreamSolution` have zero callers in WG (P5-D3), so `GetPartStream` → `SetBleedRates` is the *only* place a non-primary stream could ever matter. |

---

## 2. New `_WF` / vendored pieces

### 2.0 What already exists — verified this pass, nothing to build

| Needed by P5 | Status | Evidence |
|---|---|---|
| `bodyPartProfile` prototype kind and **every** field the four new profiles use | **SAME** | `Content.Shared/_Onyx/Wounds/WoundPrototype.cs:135-185` — `AcceptedDamageTypes`, `SupportedWounds`, `TreatmentCapabilities` (`:154`), `CirculatoryStream` (`:157`), `BleedingMultiplier` (`:164`), `Scarrable` (`:170`), `CanFeelPain` (`:173`), `PassiveRecoveryMultiplier` (`:177`), `BedRecoveryMultiplier` (`:181`), `OrganDamage` (`:184`) |
| `fractureProfile` prototype kind and all 13 fields; `FractureGradeSettings(threshold, movementModifier, manipulationModifier)` with C# defaults `Hairline(8, 0.9, 1.1)` … `Comminuted(40, 0.4, 2f)` | **SAME** | `WoundPrototype.cs:211-295` |
| `TreatmentCapability` enum `{Biological, Mechanical, Electrical}` | **SAME** | `WoundPrototype.cs:203-208` |
| `- type: Woundable` with `profile:` | **SAME**, default `"OrganicBodyPartProfile"` | `WoundDamageComponents.cs:161` |
| `- type: WolfmedBodyPart` with `fractureProfile:` (nullable), `amputationThresholds`, `maxDamage` (default **0**) | **SAME** | `Content.Shared/_WF/Wolfmed/Body/WolfmedBodyPartComponent.cs:10-28` |
| `fractureProfile: null` deserializing to null | **works** | `ValueDataNode.IsNullLiteral` matches `"null"` case-insensitively; `IsNull` is computed from it |
| All behaviour classes used by the 10 new wounds (`WoundPainBehavior`, `WoundScarBehavior`, `WoundBleedingBehavior`, `WoundFunctionalityBehavior`) | **SAME**, already exercised by the 14 shipped prototypes | `Content.Shared/_Onyx/Wounds/WoundBehaviors.cs` |
| Every `wound-name-*` / `wound-stage-*` / `wound-examine-frame-*` LocId the 16 prototypes need | **SAME — already shipped, 100 % complete** | `Resources/Locale/en-US/_Onyx/prototypes/wounds/wounds.ftl:1-44` (all 19 `wound-name-*` incl. `ipc-mechanical-damage`, `cybernetic-mechanical-damage`, `cybernetic-frame-fracture`, the 4 slime and 4 plant names; all 4 generic + 4 frame + 4 mechanical `wound-stage-*`); `Resources/Locale/en-US/_Onyx/medical/health-examinable.ftl:42-45` (the 4 `wound-examine-frame-*`) |
| `BrokenBones` alert (used by `CyberneticFractureProfile`) | **SAME** (phase 2) | `Resources/Prototypes/_Onyx/Alerts/alerts.yml` |
| `Oil`, `Slime`, `Sap` reagents | **SAME** | §1.3 row 2 |
| `BloodstreamComponent.bloodReagent` per-prototype override | **SAME** (wizden spelling; Onyx's `bloodReferenceSolution` is **MISSING**) | `Content.Server/Body/Components/BloodstreamComponent.cs:142-143` |
| `bloodlossDamage` / `bloodlossHealDamage` are `[DataField(required: true)]` | **SAME** — PROTO Q must supply both | `BloodstreamComponent.cs:69-77` |
| `SolutionContainerManagerComponent` on `MobIPC` | **not needed in YAML** — `EnsureSolution` does `EnsureComp` | `BloodstreamSystem.OnComponentInit:179-195` |
| `SurgeryMendFracture` chain on a cybernetic frame fracture | **works unchanged** | §1.3 row 6 |
| Cybernetic limb prototypes to attach the profile to | **SAME** — 5 abstract + 10 concrete | §1.3 row 1 |
| `damageModifierSet: IPC` (`Poison 0, Cold 0.2, Heat 1.5, Shock 2.5, Radiation 0.5`) | **SAME**, inherited by `MobIPC` from `silicon_base.yml:26-28` | `_EinsteinEngines/Damage/modifier_sets.yml:1-8` |
| `HealthAnalyzerWoundDiagnostic` has exactly one construction site | **SAME** | `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs:118` |

**MISSING / DIFFERENT symbols phase 5 deliberately does not port:** `InjurableComponent` (D19),
`damageContainer: SiliconIpc` (Onyx-only), the `Electronic`/Ion **damage group** (WG's `Electronic` is a
`damageModifierSet` id, `Resources/Prototypes/Damage/modifier_sets.yml:63` — there is no Ion damage type),
`BloodlossIPC` modifier set (never existed in WG), `TransplantCompatibilityComponent`,
`MechanicalOrganComponent`, `RepairableBodyPartComponent`, `VisualOrgan*`, `RoundstartCybernetics`,
`MetabolismStagePrototype`, `SolutionManagerComponent`,
`SharedSolutionContainerSystem.TryCreateCirculatorySolution`, `BleedModifierEvent`,
`MetabolismExclusionEvent`, `StasisBedBuckledComponent`, Onyx's `_Onyx/Repairable` +
`WelderRepairModesComponent`, Onyx's `_Onyx/Entities/Mobs/Customization/Parts/cybernetic.yml` and
`.../Organs/cybernetic.yml` (**duplicate ids with WG's `_Shitmed` set — porting them is a load-time crash**).

### 2.1 `Resources/Prototypes/_Onyx/Wounds/wounds.yml` — append 16 prototypes (P5-1)

The file is **420 lines / 14 prototypes** today (`id:` at `3, 39, 83, 136, 187, 236, 245, 294, 341, 374,
385, 398, 405, 416`). Onyx's holds **30** (`id:` at `2, 38, 70, 100, 124, 154, 194, 234, 287, 338, 373,
422, 431, 480, 527, 560, 571, 584, 635, 683, 729, 771, 813, 856, 905, 950, 995, 1042, 1049, 1060`).
14 + 16 = 30. Append Onyx's blocks **in Onyx's own file order** — the four profiles after
`OrganicBodyPartProfile`, `CyberneticFractureProfile` after `OrganicFractureProfile`, the ten wounds among
the existing wound list — and update the `:1` trim comment.

**Edit to line 1:**
```yaml
# WOLFGATE (WP7 → P5-1): Onyx's Ipc/Slime/Plant/Cybernetic profiles and their wound sets were dropped for
# phase 1 (D3) and restored verbatim in phase 5 (14 of 30 -> 30 of 30). Only two classes of marked fold
# exist below: D9 (Chest+Groin -> Torso) and DECISIONS §8.2-1 (manipulationModifier inversion,
# CyberneticFractureProfile).
```

**The four `bodyPartProfile`s.** Copy ONYX `wounds.yml:37-151` verbatim, with the D9 fold on the three that
carry `organDamage`. `IpcBodyPartProfile` as it must land (the only deltas are the three `# WOLFGATE` lines):

```yaml
- type: bodyPartProfile
  id: IpcBodyPartProfile
  treatmentCapabilities: [Mechanical, Electrical]
  passiveRecoveryMultiplier: 0
  bedRecoveryMultiplier: 0
  bleedingMultiplier: 1
  scarrable: false
  acceptedDamageTypes:
    - Blunt
    - Slash
    - Piercing
    - Heat
    - Cold    # WOLFGATE (P5-1/P5-D20): inert unless U13' ships - PartIPCBase's Inorganic container
    - Shock   #   (Brute + Heat/Shock) supports neither Cold nor Caustic. Onyx's SiliconIpc does.
    - Caustic # WOLFGATE (P5-1/P5-D20): as above. Kept so the profile stays a verbatim Onyx copy.
  supportedWounds:
    - IpcMechanicalDamageWound
    - ElectricalWound
    - SurgicalIncisionWound
    - DismembermentWound
    - AmputationConsequenceWound
    - SystemicBleedingWound
  organDamage:
    # WOLFGATE (P5-1/P5-D13): inert until IPC organs carry OrganDamage (§8.6-7 keeps organ damage
    # human-lineage only). Do NOT add maxAffected - Onyx sets it only on OrganicBodyPartProfile.
    chances:
      Head: 0.05
      # WOLFGATE (D9): Onyx's Chest 0.04 and Groin 0.04 fold into Torso 0.04 (not summed - independent
      # per-part rolls, and Wolfgate has one torso).
      Torso: 0.04
      Arm: 0.02
      Hand: 0.01
      Leg: 0.02
      Foot: 0.01
```

`SlimeBodyPartProfile` (ONYX `:69-97`): `treatmentCapabilities: [Biological]`, `bleedingMultiplier: 1.15`,
`scarrable: false`, the same 7 `acceptedDamageTypes`, `supportedWounds` = `SlimeBluntWound, SlimeSlashWound,
SlimePiercingWound, SlimeBurnWound, ElectricalWound, SurgicalIncisionWound, DismembermentWound,
AmputationConsequenceWound, InternalBleedingWound, SystemicBleedingWound` (**no `MedicalScarWound`**), and
`organDamage.chances` = `Head 0.05` + the D9-folded `Torso 0.04` **and nothing else** — Onyx lists only
`Head`/`Chest`/`Groin` here; **do not add the four limb rows** and **do not add `maxAffected`**.

`CyberneticBodyPartProfile` (ONYX `:99-121`): `treatmentCapabilities: [Mechanical, Electrical]`,
**`canFeelPain: false`** (`:102`), `passiveRecoveryMultiplier: 0`, `bedRecoveryMultiplier: 0`,
`bleedingMultiplier: 0.5`, `scarrable: false`, the same 7 accepted types, `supportedWounds` =
`CyberneticMechanicalDamageWound, ElectricalWound, CyberneticFrameFractureWound, SurgicalIncisionWound,
DismembermentWound, AmputationConsequenceWound`. **No `organDamage` block — nothing to fold.**

`PlantBodyPartProfile` (ONYX `:123-151`): `treatmentCapabilities: [Biological]`, `bleedingMultiplier: 1`,
**`scarrable` unset (default true)**, the same 7 accepted types, `supportedWounds` = the four Plant wounds
plus `ElectricalWound, SurgicalIncisionWound, DismembermentWound, AmputationConsequenceWound,
InternalBleedingWound, MedicalScarWound, SystemicBleedingWound`, and `organDamage.chances` = `Head 0.05` +
D9-folded `Torso 0.04` only (same two prohibitions as slime).

**`CyberneticFractureProfile`** — copy ONYX `:193-231`, which is field-for-field identical to
`OrganicFractureProfile` except `id:` and `wound:`, then apply the §8.2-1 fold:

```yaml
- type: fractureProfile
  id: CyberneticFractureProfile
  damageType: Blunt
  wound: CyberneticFrameFractureWound
  severityMultiplier: 1
  resetTreatmentOnDamage: true
  worsenMinimumDamage: 5
  minimumHitDamage: 3
  accumulationMultiplier: 0.4
  reductionMinimumGrade: Simple
  removeWoundWhenMended: true
  alert: BrokenBones
  alertMinimumGrade: Simple
  alertHiddenTreatments: [Mended]
  treatmentEffectScales:
    None: 1
    Reduced: 0.25
    Mended: 0
  # WOLFGATE (DECISIONS §8.2-1): same inversion fix as OrganicFractureProfile above (wounds.yml:53-56) -
  # Onyx's 0.92/0.84/0.75/0.75 would make a shattered cybernetic arm do-afters 25% FASTER. Restored to the
  # C# defaults in WoundPrototype.cs:270-295. movementModifier is untouched (correctly below 1).
  grades:
    Hairline:   { threshold: 20, creationChance: 0.05, movementModifier: 0.625, manipulationModifier: 1.1 }
    Simple:     { threshold: 35, creationChance: 0.25, movementModifier: 0.5,   manipulationModifier: 1.25 }
    Displaced:  { threshold: 50, creationChance: 0.65, movementModifier: 0,     manipulationModifier: 1.5 }
    Comminuted: { threshold: 60, creationChance: 1,    movementModifier: 0,     manipulationModifier: 2.0 }
```
*(Written in block style in the file, matching `OrganicFractureProfile`'s existing layout.)*

**The ten wound prototypes — byte-for-byte from Onyx, no folds.**

| id | ONYX lines | shape |
|---|---|---|
| `CyberneticFrameFractureWound` | `337-371` | `damageTypes: {}`, `maximumSeverity: 200`, 4 stages at 20/35/50/60 named `wound-stage-frame-*` with `examineDescription: wound-examine-frame-*`; behaviours `WoundFunctionalityBehavior Impaired` (Hairline) then `Disabled` ×3. **No pain behaviour at all**, unlike `BoneFractureWound`. |
| `IpcMechanicalDamageWound` | `583-633` | 6 damage types in one wound (`Blunt` reopen 18, `Slash` 15, `Piercing` 18, `Heat` 15, `Cold` 18, `Caustic` 12 — **no `Shock`**, which goes to `ElectricalWound`); `maximumSeverity: 200`; wound-level `!type:WoundBleedingBehavior rate: 0.08 chance: 1` (**no `minimumSeverity`** — leaks from the first point) and `!type:WoundPainBehavior painPerSeverity: 0.87 minSeverity: 10`; stages `Minor 0 / Moderate 25 (Impaired) / Severe 50 (Disabled) / Critical 80 (Disabled)` named `wound-stage-mechanical-*`. |
| `CyberneticMechanicalDamageWound` | `634-681` | Identical to the above **minus the `WoundPainBehavior`**. |
| `SlimeBluntWound` / `SlimeSlashWound` / `SlimePiercingWound` / `SlimeBurnWound` | `683 / 729 / 771 / 813` | The organic set minus `WoundScarBehavior` and minus `minimumSeverity` on every stage's bleeding. Base pain `0.7 / 0.67 / 0.67 / —` (Burn carries per-stage `0.8`). Stage names are the **generic** `wound-stage-{minor,moderate,severe,critical}`. |
| `PlantBluntWound` / `PlantSlashWound` / `PlantPiercingWound` / `PlantBurnWound` | `856 / 905 / 950 / 995` | The organic set with the same `minimumSeverity` removal but **keeping `WoundScarBehavior`** (thresholds 20/15/15/20). Numbers otherwise identical to the organic originals, including `painPerSeverity: 0.87`. |

> **Verification note.** WG's `BluntWound` carries `minimumSeverity: 8` on all four stage bleeding blocks
> (`wounds.yml:100-131`) and `SlashWound` carries `minimumSeverity: 9` (`:186-232`);
> `SlimeBluntWound`/`SlimeSlashWound`/`PlantBluntWound`/`PlantSlashWound` carry none. That is the single
> mechanical difference that makes slimes and diona leak from the first scratch.

### 2.2 `Resources/Prototypes/_WF/Wolfmed/Body/species_parts.yml` — NEW (P5-1)

```yaml
# Wolfmed per-species part data (P5-1). Values are Onyx's:
#   Slime  - ONYX Resources/Prototypes/Body/Species/slime.yml:232-239  (OrganSlimePersonExternal)
#   Plant  - ONYX Resources/Prototypes/Body/Species/diona.yml:226-235  (OrganDionaExternal)
#   Ipc    - ONYX Resources/Prototypes/Corvax/Body/Species/ipc.yml:322-330 (OrganIpcExternal)
#   Cyber  - ONYX Resources/Prototypes/_Onyx/Entities/Mobs/Customization/Parts/cybernetic.yml:1-21
#
# Same shape as _WF/Wolfmed/Body/parts.yml: separate abstract ids added to the species part base's
# `parent:` list with a one-line `# WOLFGATE` edit, because RT throws "Duplicate ID" on redeclaration.
# RT folds multiple parents in list order onto an accumulating node and never overwrites a key already
# present in that node unless the field is InheritanceBehavior.Always
# (SerializationManager.Composition.cs:40-58 and :178-206) - so these MUST be FIRST in the species base's
# parent list to beat Base<Slot>'s WolfmedBodyPart block.
#
# NOTE: the phase-1 files put the Wolfmed abstract LAST (Body/Parts/base.yml:137,
# _Shitmed/Body/Parts/base.yml:47). That worked only because those parents declare no WolfmedBodyPart.
# Do not "normalise" this file to match them.
#
# D8: Onyx's `- type: BodyPart / fractureProfile:` is Wolfgate's `- type: WolfmedBodyPart`.
# P5-D3: none of these profiles sets circulatoryStream - doing so silently zeroes the species' bleeding.
# P5-D7: no amputationThresholds key on Ipc/Cybernetic, so Base<Slot>'s organic P3 set is inherited.

- type: entity
  id: WolfmedPartSlime
  abstract: true
  components:
  - type: Woundable
    profile: SlimeBodyPartProfile
  - type: WolfmedBodyPart
    fractureProfile: null   # slime has no bones, as Onyx's slime does

- type: entity
  id: WolfmedPartDiona
  abstract: true
  components:
  - type: Woundable
    profile: PlantBodyPartProfile
  - type: WolfmedBodyPart
    fractureProfile: null
    # Onyx: diona limbs cannot be SEVERED by damage at all. An empty flow mapping is a PRESENT key, so it
    # suppresses Base<Slot>'s dict; `amputationThresholds:` with nothing after it parses as null, not as an
    # empty dict, and is NOT equivalent. AmputationSystem.HandlePartDamageApplied early-returns on
    # AmputationThresholds.Count == 0 (AmputationSystem.cs:80).
    # This does NOT make diona limbs indestructible - see PLAN5 §8.1 and U17.
    amputationThresholds: {}

- type: entity
  id: WolfmedPartIpc
  abstract: true
  components:
  - type: Woundable
    profile: IpcBodyPartProfile
  - type: WolfmedBodyPart
    fractureProfile: null
    # WOLFGATE (P5-D7): no amputationThresholds key, so Base<Slot>'s organic P3 set is inherited
    # (arm Slash 130 / Piercing 250 / Blunt 250 / Heat 250 + the P3 finishing minimums,
    # _WF/Wolfmed/Body/parts.yml:52-59). Onyx's own IPC numbers (arm 270/400/600, ONYX
    # Corvax/Body/Species/ipc.yml:365-372) sit on a different part chain; see PLAN5 P5-D7/P5-D8.

- type: entity
  id: WolfmedPartCybernetic
  abstract: true
  components:
  - type: Woundable
    profile: CyberneticBodyPartProfile
  - type: WolfmedBodyPart
    fractureProfile: CyberneticFractureProfile
    # amputationThresholds inherit Base<Slot>'s organic set (P5-D7). NOTE: cybernetic limbs DO carry a gib
    # trigger - CyberneticPartBase declares none, but every concrete limb's second parent is
    # BaseLeftArm/BaseLeftHand/... which parent Mono's MajorLimb (Blunt 190 / Slash 210 / Heat 250 + Ash)
    # or MinorLimb (150/180/230). See PLAN5 P5-D8b / U15.
```

**All four ids verified unused** across `Resources`, `Content.Shared`, `Content.Server`, `Content.Client`,
`Content.IntegrationTests` (§5.2).

### 2.3 `Resources/Prototypes/_WF/Wolfmed/Damage/containers.yml` — NEW (P5-2)

**Owned by WP13-0**, not WP13-2: WP13-1's part edits reference `InorganicWolfmed`, so the file must exist
before WP13-1 runs.

```yaml
# WOLFGATE (P5-2/P5-D5): Silicon + Bloodloss, for the IPC MOB. The stock Silicon container
# (Resources/Prototypes/Damage/containers.yml:27-34) has no Bloodloss type, so an IPC's oil-loss damage -
# and WolfmedBodyPartLifecycleSystem's P3-D1 vital-part charge, which is applied to the BODY
# (WolfmedBodyPartLifecycleSystem.cs:92-93) - are silently discarded by DamageableSystem's type filter
# (DamageableSystem.cs:270-277). A separate id keeps borgs, cyborg parts and every other
# `damageContainer: Silicon` entity on the stock container (D2).
#
# Bloodloss is added as a TYPE, not via the Airloss group (P5-D5b): the tourniquet's `Asphyxiation: 5`
# application cost is applied to the PART (TourniquetSystem.cs:88-89), whose container this does not
# change, so taking the whole group would buy nothing and open an unaudited Asphyxiation surface on an
# entity that dies at a projected total of 100.
- type: damageContainer
  id: SiliconWolfmed
  supportedGroups:
  - Brute
  supportedTypes:
  - Heat
  - Shock
  - Radiation
  - Bloodloss

# WOLFGATE (P5-2/P5-D20, U13'): Onyx parity for IPC and cybernetic PARTS. Onyx's SiliconIpc takes the whole
# Burn group (Heat, Shock, Cold, Caustic - Resources/Prototypes/Damage/groups.yml:9-16); Wolfgate's
# Inorganic (Brute + Heat/Shock, containers.yml:10-16) and Silicon (+ Radiation, :27-34) do not, so Cold and
# Caustic are silently dropped and two of the seven acceptedDamageTypes on both mechanical profiles - and
# two of the six damageTypes on both mechanical wounds - are dead.
#
# A PART container is the right lever: DamageableSystem raises the Wolfmed routing seam at :253-264, BEFORE
# the body's own type filter at :270-277, so a Cold/Caustic hit on an IPC reaches routing even though the
# mob container drops it; it is the part's container that discards it.
#
# This id is a superset of both stock containers it replaces (Inorganic on PartIPCBase, Silicon on
# CyberneticPartBase), so neither part base loses a type. Only those two abstracts use it (D2).
# Radiation is kept although it is not in WoundHostComponent.LocalizedDamageTypes and therefore never
# routes - a directly-targeted part can still take it.
- type: damageContainer
  id: InorganicWolfmed
  supportedGroups:
  - Brute
  - Burn
  supportedTypes:
  - Radiation
```

If U13′ = (a) "leave", omit the second block and leave `PartIPCBase`/`CyberneticPartBase` on their stock
containers; everything else in this plan is unchanged except T-P5-13 (canary) replaces T-P5-21 (positive).

### 2.4 `Content.Server/_Onyx/Chemistry/Circulation/CirculatoryStreamSystem.cs` — 1-line hardening (P5-D3)

In-vendored `_Onyx` edit, replacing the `GetValueOrDefault` at `:77-79`:

```csharp
        // WOLFGATE: P5-2/P5-D3. Wolfgate has exactly one blood solution per body (server-only
        // BloodstreamComponent), so every stream's bleed rate lands in the same place. Summing instead of
        // reading only the primary key makes a profile that sets `circulatoryStream:` degrade to "bleeds
        // normally" rather than "silently stops bleeding". Identical while Organic is the only stream.
        var total = 0f;
        foreach (var rate in rates.Values)
            total += rate;

        _bloodstream.TryModifyWoundBleedProjection(body, total - bloodstream.BleedAmount, bloodstream);
```
No new `using`. Behaviour is bit-identical today. This is a **complete** fix for the stream surface, not a
partial one: `SetBleedRates` and `GetPartStream` are the only stream-aware methods with callers
(`WoundBleedingSystem.cs:306, 319, 324`); `TryGetPartSolution` and `TryGetStreamSolution` (`:32-68`) have
**zero** callers anywhere in WG.

### 2.5 Locale additions (P5-5, P5-D17)

`Resources/Locale/en-US/_Onyx/medical/health-analyzer-component.ftl` — append, marked `# WOLFGATE (P5-5)`:
```
health-analyzer-wound-bleeding-short-mechanical = fluid leak
health-analyzer-wound-fracture-short-frame = frame damage: { $grade }
health-analyzer-wound-fracture-treated-short-frame = frame damage: { $grade } ({ $treatment })
```
`Resources/Locale/en-US/_Onyx/medical/health-examinable.ftl` — append:
```
health-examinable-part-bleeding-mechanical = leaking fluid
```
Existing keys these sit beside, verified: `health-analyzer-wound-bleeding-short = external bleeding`
(`:4`), `health-analyzer-wound-fracture-short = fracture: { $grade }` (`:17`),
`health-analyzer-wound-fracture-treated-short` (`:18`), `health-examinable-part-bleeding = active bleeding`
(`health-examinable.ftl:37`). **None of the four new keys already exists** (no duplicate-key risk). The
seven `health-examinable-part-damage-*` adjectives (`health-examinable.ftl:13-19`) are **deliberately left
flesh-flavoured** (P5-D17, U9).

### 2.6 Analyzer payload flag (P5-5)

`Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs` — in-vendored `_Onyx` edit, **one appended
positional member** on the `readonly record struct` (currently 10 members, `:9-24`):

```csharp
    HealthAnalyzerClottingPhase ClottingPhase,
    bool Mechanical) // WOLFGATE (P5-5): true when the part's bodyPartProfile is not [Biological]; lets the
                     // client say "fluid leak" / "frame damage" instead of "bleeding" / "fracture".
```
Producer: `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs`'s `BuildWoundDiagnostics`
sets it from `!profile.TreatmentCapabilities.Contains(TreatmentCapability.Biological)`. Both `woundable`
and `_prototypes` are already in scope in that per-part loop (`:26`, `:38-45`), and the struct has exactly
one construction site (`:118`), so the change is mechanical.
Consumers: `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml.cs` picks the `-mechanical` /
`-frame` LocIds when the flag is set, and
`Content.Shared/_Onyx/HealthExaminable/HealthExaminableSystem.PartStatus.cs` picks
`health-examinable-part-bleeding-mechanical`. **No new BUI message, no new subscription** — the same
"append an optional member to an existing payload" shape as phase 4's EXT 2.

---

## 3. Upstream `// WOLFGATE` hooks — the complete authorised phase-5 list

Nothing outside this section may be edited in an upstream (non-`_Onyx`, non-`_WF`) file without escalating.
Line numbers verified against HEAD `2b4a4675d0`.

### 3.1 Code hooks

**Phase 5 adds ZERO upstream code hooks.** Every system that reads the new data already exists and already
subscribes what it needs (`WoundSystem`, `WoundDamageRoutingSystem`, `WoundDamageProjectionSystem`,
`WoundFractureSystem`, `WoundBleedingSystem`, `WoundScarSystem`, `PainSystem`, `WoundHealingSystem`,
`CirculatoryStreamSystem`, `WolfmedBodyPartSystem`, `AmputationSystem`, `OrganDamageSystem`). The only C#
phase 5 touches outside `_Onyx`/`_WF` internals is:

| # | File | Site | Change | WP |
|---|---|---|---|---|
| **EXT 3** | `Content.Shared/_WF/Wolfmed/Body/WolfmedWoundHostExclusionSystem.cs` | `:12` | **One line** (P5-D9): `private static readonly HashSet<string> ExcludedAncestors = new(); // WOLFGATE (P5-D9): protogen is biologically organic (Biological container, OrganicPart limbs, Blood bloodstream, Hunger/Thirst, Respirator, FoodMeatHuman) - exclusion lifted. Add new synthetic species here.` This is a **`_WF` file**, not upstream; the single `<WoundHostComponent, ComponentInit>` subscription at `:17` is unchanged | WP13-3 |
| **EXT 4** | `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs` | `:31-34` | **Comment only** — the marked D2 comment names "a borg, a Protogen" as a surgery target without `WoundHost`. After P5-D9 that is stale; replace "a Protogen" with "a synthetic species". A `_WF` file, not upstream | WP13-3 |

### 3.2 Upstream prototype and data edits

| # | File | Change | WP |
|---|---|---|---|
| **PROTO M** | `Resources/Prototypes/Body/Parts/slime.yml`, `PartSlime`'s `parent:` at **`:4`** | **One line**: `  parent: [WolfmedPartSlime, BaseItem, BasePart] # WOLFGATE (P5-1): slime wound profile, no bone fractures` — `WolfmedPartSlime` **must be first** (P5-D1) | WP13-1 |
| **PROTO N** | `Resources/Prototypes/Body/Parts/diona.yml`, `PartDiona`'s `parent:` at **`:3`** | **One line**: `  parent: [WolfmedPartDiona, BaseItem, BasePart] # WOLFGATE (P5-1): plant wound profile, limbs cannot be severed (still destructible - see PLAN5 §8.1)` | WP13-1 |
| **PROTO O** | `Resources/Prototypes/_EinsteinEngines/Body/Parts/ipc.yml`, `PartIPCBase`'s `parent:` at **`:3`**, its `Damageable` (inherited) and its `Destructible` block at **`:11-24`** | **Three edits.** (a) `  parent: [WolfmedPartIpc, BasePartInorganic] # WOLFGATE (P5-1): IPC chassis wound profile`. (b) **U13′ only** — add `  - type: Damageable` / `    damageContainer: InorganicWolfmed # WOLFGATE (P5-D20): Onyx's SiliconIpc takes the whole Burn group; stock Inorganic drops Cold and Caustic, killing 2 of the 7 acceptedDamageTypes on IpcBodyPartProfile.` (c) **P5-D8** — `damage: 110` → `damage: 190` (`:16`) and `damage: 150` → `damage: 210` (`:22`), each with `# WOLFGATE (P5-D8): dormant until P5-1 routes damage to IPC parts. Parity with Mono's MajorLimb, which every organic arm and leg carries (Body/Parts/base.yml:276-307). No Heat rung is added - EE's own "# no ashing trigger" stands.` **Do not touch `TorsoIPC`'s own `Destructible` (`:37-51`)** — a torso is never severable | WP13-1 |
| **PROTO P** | `Resources/Prototypes/_Shitmed/Body/Parts/cybernetic.yml`, `CyberneticPartBase`'s `parent:` at **`:3`**, its `Damageable` at **`:10-11`**, and (U15) a new `Destructible` block | **Three edits.** (a) `  parent: [WolfmedPartCybernetic, BasePartInorganic] # WOLFGATE (P5-1): cybernetic wound + frame-fracture profile`. (b) **U13′ only** — `    damageContainer: InorganicWolfmed # WOLFGATE (P5-D20): restores Cold/Caustic, which Onyx's SiliconIpc supports; superset of stock Silicon.` (c) **U15 only, P5-D8b** — a marked `- type: Destructible` with `!type:DamageTypeTrigger Blunt 190` and `Slash 210` → `!type:GibPartBehavior { }` and **no Heat rung**, commented: `# WOLFGATE (P5-D8b): CyberneticPartBase declares no Destructible, so every concrete limb inherits its OTHER parent's - Mono's MajorLimb/MinorLimb - and a steel prosthetic currently spawns Ash, runs BurnBodyBehavior and plays MeatLaserImpact at Heat 250. Declaring one here wins (first parent, and thresholds is a non-Always DataField) and drops the ashing rung, mirroring PartIPCBase. Side effect: cybernetic hands/feet gib at 190/210 instead of MinorLimb's 150/180.` All 14 entities in the file descend from `CyberneticPartBase` | WP13-1 |
| **PROTO Q** | `Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/ipc.yml`, `MobIPC`'s `components:` (from `:6`) and its `Destructible` at **`:80-87`** | **One block + one number**, quoted in full below | WP13-2 |
| ~~PROTO R~~ | ~~`…/silicon_base.yml`~~ | **DROPPED** (E3 / P5-D11). `MobIPC`'s own `Destructible` replaces the parent's list wholesale, and `MobIPC` is the only descendant, so the edit is a no-op that would add a tracked upstream file for nothing | — |
| **PROTO S** | `Resources/Prototypes/Entities/Objects/Tools/welders.yml`, `WeldingHealing.damageContainers` at **`:118-119`** | **One line** (P5-D5, mandatory if PROTO Q's container change ships): `      - SiliconWolfmed # WOLFGATE (P5-D5): MobIPC moved to the SiliconWolfmed container; WeldingHealableSystem gates on this list (WeldingHealableSystem.cs:33-35) and would otherwise stop repairing IPCs entirely` | WP13-2 |
| **PROTO T** | `Resources/Prototypes/_Mono/Entities/Objects/Tools/nanite_applicator.yml`, `WeldingHealing.damageContainers` at **`:49-50`** | **One line**, identical to PROTO S | WP13-2 |
| **PROTO U** | `Resources/Prototypes/Entities/Objects/Tools/cable_coils.yml`, `CableStack`'s `- type: Healing` at **`:36-45`** | **One line** (P5-D12): `    treatmentCapabilities: [Electrical] # WOLFGATE (P5-3): matches ONYX cable_coils.yml:179 and Wolfgate's own damageContainers: [Silicon] intent. Without it the default [Biological] (HealingComponent.cs:72-73) overlaps OrganicBodyPartProfile and the coil heals human Heat/Shock wounds at -3/-3 per 0.6s, because HOOK 8 skips the damageContainers check for wound hosts (HealingSystem.cs:201-210) and WoundHealingSystem.IsCompatiblePart never reads it (:154-166).` Only this one line — Onyx's delay (3.5) and damage set are NOT adopted | WP13-4 |
| **PROTO V** | `Resources/Prototypes/_Mono/Entities/Mobs/Species/protogen.yml`, the marked comment at **`:3-4`** | **Comment replacement only** (P5-D9); the file is already a tracked upstream file: `  # WOLFGATE (D21/D32 → P5-D9): the exclusion was lifted in phase 5. BaseMobProtogen is biologically organic (Biological container, OrganicPart limbs, Blood bloodstream, Hunger/Thirst, Respirator, Butcherable → FoodMeatHuman) and is now an ordinary organic wound host. Its organs are BaseProtogenOrgan-derived and carry no OrganDamage - see P5-D19.` | WP13-3 |

**PROTO Q in full** (`MobIPC`; place the block immediately after `components:` so it reads as the Wolfmed
section, mirroring Onyx's `# <Onyx-IPCWounds>` fence):

```yaml
  # WOLFGATE (P5-1/P5-2): IPCs become wound hosts. ONYX Resources/Prototypes/Corvax/Body/Species/ipc.yml:92-113.
  - type: WoundHost
  # WOLFGATE (P5-D10): Onyx puts PainShockTarget on BaseSpeciesMob (ONYX Body/species_base.yml:54), which its
  # MobIpc parents (ONYX Corvax/Body/Species/ipc.yml:93-97) - Onyx IPCs DO get pain shock. Wolfgate put it on
  # BaseMobSpeciesOrganic (P2-D7, base.yml:256), which MobIPC does not inherit, so it is restated here.
  # IpcBodyPartProfile leaves canFeelPain at its default (true). EmoteOnDamage is deliberately NOT added:
  # Onyx's copy uses the broken `emotes:` key (§8.2-3) and MobIpc's allowedEmotes are Boop/Whirr.
  - type: PainShockTarget
  # WOLFGATE (P5-2/P5-D4): Onyx's bloodReferenceSolution (Oil 250) maps onto Wolfgate's classic
  # bloodReagent + bloodMaxVolume; WG's BloodstreamComponent has no bloodReferenceSolution field.
  # Wound bleeding writes BleedAmount through BloodstreamSystem.TryModifyWoundBleedProjection (GUARD E3)
  # and TryModifyBloodLevel spills BloodReagent - so an IPC leaks oil, not blood. SolutionContainerManager
  # is not declared: BloodstreamSystem's EnsureSolution EnsureComp's it. No circulatoryStream:
  # IpcBodyPartProfile stays on the primary Organic stream, exactly as Onyx's does (P5-D3).
  # chemicalMaxVolume: 0 (U16) because an IPC has no metabolizer - OrganIPCPump's Metabolizer block is
  # commented out (_EinsteinEngines/Body/Organs/ipc.yml:70-76) - so a 250u chemical solution would be a
  # trap. InjectableSolution is deliberately NOT added; Onyx gets it by parenting MobBloodstream
  # (Entities/Mobs/base.yml:236-248), Wolfgate does not, and nothing can metabolise what is injected.
  - type: Bloodstream
    bloodReagent: Oil
    bloodMaxVolume: 250
    chemicalMaxVolume: 0
    bloodlossDamage:
      types:
        Bloodloss: 0.5
    bloodlossHealDamage:
      types:
        Bloodloss: -1
  # WOLFGATE (P5-D5/P5-D5b): Silicon + the Bloodloss TYPE (not the Airloss group) so oil loss and P3-D1's
  # vital-part Bloodloss charge actually land. damageModifierSet is inherited per key from
  # PlayerSiliconHumanoidBase:26-28 (component data merges field by field); restated here only so the pair
  # is readable in one place.
  - type: Damageable
    damageContainer: SiliconWolfmed
    damageModifierSet: IPC
```
plus, in the existing `Destructible` block at `:80-87`:
```yaml
      # WOLFGATE (D22/P5-D11): body damage on a wound host is the sum of every part
      # (WoundDamageProjectionSystem.RefreshBodyDamage:156-201), so 400 is reachable from routine limb
      # damage - the same reason BaseMobSpeciesOrganic was raised to 1500 (base.yml:270-278).
        damage: 1500
```
**`MobThresholds` is not touched.** Death stays at a projected total of 100 (`:70-73`).

### 3.3 In-vendored-file `// WOLFGATE` edits (not upstream hooks)

| File | Edits | WP |
|---|---|---|
| `Resources/Prototypes/_Onyx/Wounds/wounds.yml` | The `:1` comment rewrite + 16 appended prototypes with the two fold classes (§2.1) | WP13-0 |
| `Content.Server/_Onyx/Chemistry/Circulation/CirculatoryStreamSystem.cs` | 1 site — the `SetBleedRates` sum (§2.4). *(File lives under `Content.Server`; its namespace is `Content.Shared._Onyx.Chemistry.Circulation`.)* | WP13-2 |
| `Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs` | 1 site — one appended `bool Mechanical` member (§2.6) | WP13-5 |
| `Content.Shared/_Onyx/HealthExaminable/HealthExaminableSystem.PartStatus.cs` | 1 site — mechanical bleeding LocId branch | WP13-5 |
| `Resources/Locale/en-US/_Onyx/medical/{health-analyzer-component,health-examinable}.ftl` | 4 appended keys (§2.5) | WP13-5 |

### 3.4 Explicitly NOT touched in phase 5 (and why)

- **`Resources/Prototypes/Damage/containers.yml`** — neither the stock `Silicon` nor `Inorganic` container
  is modified. Every borg, cyborg part and machine keeps them verbatim (D2). The two new ids live in `_WF`.
- **`Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/silicon_base.yml`** — not touched at all
  (PROTO R dropped, E3). That file owns `DamageVisuals` (`:269`), `HealthExaminable` (`:284`) and
  `SurgeryTarget` (`:319`); all three are inherited by `MobIPC` unchanged, which is why IPCs get per-limb
  damage sprites (`Content.Client/_WF/Wolfmed/Damage/DamageVisualsSystem.Wolfmed.cs`) and wound surgery for
  free.
- **`Resources/Prototypes/Body/Parts/base.yml` and `_Shitmed/Body/Parts/base.yml`** — `MajorLimb`,
  `MinorLimb`, `BaseHead`, `BaseTorso` and their gib thresholds are **not** re-tuned. The
  organic 190-gib-versus-250-Blunt-amputation contradiction (§8.3 R11) is pre-existing and goes to the
  balance pass, not to phase 5.
- **`Resources/Prototypes/Entities/Mobs/Species/base.yml`** — no change. Slime and diona already inherit
  `WoundHost`, `PainShockTarget` and `EmoteOnDamage` from `BaseMobSpeciesOrganic`; protogen will too, once
  EXT 3 stops stripping it.
- **`Resources/Prototypes/Entities/Objects/Specific/Medical/healing.yml`** — no `allowedWoundStages` on
  Regenerative Mesh or Medicated Suture (U6), and the `Tourniquet` block is untouched.
- **`Resources/Prototypes/Entities/Objects/Tools/welders.yml`'s entity body** — no `- type: Healing`
  block (P5-D16). Only the one-line `damageContainers` addition of PROTO S.
- **`- type: Repairable` on `MobIPC`** — P5-D15.
- **`Resources/Prototypes/_Mono/Body/Organs/protogen.yml`** — no organ instrumentation (P5-D19, U12′).
- **Onyx's `_Onyx/Entities/Mobs/Customization/Parts/cybernetic.yml` and `.../Organs/cybernetic.yml`** —
  duplicate ids with WG's `_Shitmed` set (`LeftHandCybernetic`, `RightFootCybernetic`, `JawsOfLifeLeftArm`,
  `SpeedLeftLeg`, `DexLeftHand` …). Porting them is a prototype-load crash.
- **`Resources/Prototypes/Entities/Mobs/Species/skeleton.yml`** — P5-D18.
- **`MetabolismStagePrototype`, `SolutionManagerComponent`, the stage-metabolizer rewrite** — DECISIONS
  P5-2 forbids it; §8.7 records exactly what it would buy (nothing reachable at the pin).
- **`StatusEffectPainNumbness` / `ModifyStatusEffect` / `Content.Shared/Traits/Assorted/PainNumbnessSystem.cs`**
  — P5-D14.
- Everything on PLAN §3's, PLAN2 §3's, PLAN3 §3's and PLAN4 §3.4's "explicitly NOT touched" lists.

### 3.5 Upstream-file count after phase 5

47 (after phase 4, `WOLFMED_STATUS.md:176-178`) **+ 8 new**: `Body/Parts/slime.yml`,
`Body/Parts/diona.yml`, `_EinsteinEngines/Body/Parts/ipc.yml`,
`_Shitmed/Body/Parts/cybernetic.yml`, `_EinsteinEngines/Entities/Mobs/Player/ipc.yml`,
`Entities/Objects/Tools/welders.yml`, `_Mono/Entities/Objects/Tools/nanite_applicator.yml`,
`Entities/Objects/Tools/cable_coils.yml` = **55**. `_Mono/Entities/Mobs/Species/protogen.yml` is already
tracked (its `# WOLFGATE` comment dates to phase 1) and needs no new row;
`_EinsteinEngines/Entities/Mobs/Player/silicon_base.yml` is **not** added (PROTO R dropped). WP13-7
re-measures with `git diff 2b4a4675d0 --name-only` filtered to files outside
`_Onyx`/`_WF`/`Content.IntegrationTests`.

---

## 4. Work packages

Build order — **packages run SEQUENTIALLY in the one worktree** (concurrent builds collide). Each appends
its own rows and deviations directly to `Docs/Wolfmed/WOLFMED_MANIFEST.md`; WP13-7 reconciles rather than
merges. **One owner per shared file** — named in every table.

```
WP13-0  wounds.yml (16 protos) + _WF damage containers   2 files
WP13-1  species part wiring + IPC/cyber gib blocks        5 files  [owns all four species part files]
WP13-2  IPC as a wound host + circulation                 4 files  [owns MobIPC, welders, nanite, CirculatoryStreamSystem]
WP13-3  protogen exclusion lift                           3 files
WP13-4  cable coil treatmentCapabilities                  1 file
WP13-5  mechanical analyzer/examine wording (P5-5)        6 files
WP13-6  tests (P5-6)                                      4 files
WP13-7  docs, manifest, status (P5-7)                     3 files
```

**Why the container file moved to WP13-0.** WP13-1's PROTO O(b)/P(b) reference `InorganicWolfmed`. A
`ProtoId<DamageContainerPrototype>` pointing at a missing id is a YAMLLinter failure, so the container file
must land first. It is pure data with no dependencies of its own.

### WP13-0 — Profile, wound and container prototypes (P5-1a, P5-2a)

| File | Action | Δ |
|---|---|---|
| `Resources/Prototypes/_Onyx/Wounds/wounds.yml` | rewrite `:1` comment; append 4 `bodyPartProfile`, 1 `fractureProfile`, 10 `wound` prototypes (§2.1) | ~+650 |
| `Resources/Prototypes/_WF/Wolfmed/Damage/containers.yml` | **new** — `SiliconWolfmed` + (U13′) `InorganicWolfmed` (§2.3) | +40 |

- **Prototypes added (18):** the 16 wound/profile ids plus 2 `damageContainer` ids.
- **Locale:** none. Every key already ships (§2.0).
- **Subscription pairs:** none. **Folds:** D9 on three profiles; §8.2-1 on `CyberneticFractureProfile`.
- **Traps:** do **not** add `maxAffected` to the three new `organDamage` blocks (Onyx sets it only on
  `OrganicBodyPartProfile`); do **not** add limb rows to Slime/Plant `chances` (Onyx lists Head/Chest/Groin
  only); every `Chest`/`Groin` key **must** become `Torso` or deserialization fails.
- **Checkpoint:** `dotnet run --project Content.YAMLLinter -c Release` clean; `Content.Server` and
  `Content.Client` 0 errors (`-c DebugOpt`); a 120 s headless server with zero `[ERRO]`/`[FATL]`.
  **This package alone changes no behaviour** — nothing references the new ids until WP13-1.

### WP13-1 — Species part wiring (P5-1b)

| File | Action | Owner note |
|---|---|---|
| `Resources/Prototypes/_WF/Wolfmed/Body/species_parts.yml` | **new** — 4 abstracts (§2.2) | new file |
| `Resources/Prototypes/Body/Parts/slime.yml` | PROTO M, 1 line at `:4` | sole owner |
| `Resources/Prototypes/Body/Parts/diona.yml` | PROTO N, 1 line at `:3` | sole owner |
| `Resources/Prototypes/_EinsteinEngines/Body/Parts/ipc.yml` | PROTO O(a) 1 line at `:3`; O(b) container (U13′); O(c) two numbers at `:16`/`:22` | sole owner — WP13-2 must not touch this file |
| `Resources/Prototypes/_Shitmed/Body/Parts/cybernetic.yml` | PROTO P(a) 1 line at `:3`; P(b) container (U13′); P(c) `Destructible` block (U15) | sole owner |

- **New entity ids (4):** `WolfmedPartSlime`, `WolfmedPartDiona`, `WolfmedPartIpc`, `WolfmedPartCybernetic`.
- **Subscription pairs:** none.
- **Ordering constraint:** `WolfmedPart*` **must be the first element** of each `parent:` list, and the
  species base must remain first in each concrete part's list (they already are:
  `LeftArmSlime: parent: [PartSlime, BaseLeftArm]` `slime.yml:51`,
  `LeftArmDiona: parent: [PartDiona, BaseLeftArm]` `diona.yml:33`,
  `LeftArmIPC: parent: [ PartIPCBase, BaseLeftArm ]` `_EE ipc.yml:63`,
  `LeftArmCyberneticBase: parent: [ CyberneticPartBase, BaseLeftArm ]` `cybernetic.yml:18`).
  **Do not copy the phase-1 files' Wolfmed-last ordering** (P5-D1 note).
- **Checkpoint:** YAMLLinter; `DockTest` **first** (project memory: `db.ef` sqlite warnings fail every pair
  test and mask real failures); then `EntityTest|PrototypeSaveTest`, which spawns every prototype and
  catches a bad `parent:` list instantly; then a headless run.
- **This is the package that changes diona, slime and cybernetic behaviour.** Do not proceed to WP13-2
  until `EntityTest` is green.

### WP13-2 — IPC as a wound host, and circulation (P5-1c / P5-2)

| File | Action |
|---|---|
| `Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/ipc.yml` | PROTO Q — `WoundHost`, `PainShockTarget`, `Bloodstream`, `Damageable`, `Destructible` 400→1500 |
| `Resources/Prototypes/Entities/Objects/Tools/welders.yml` | PROTO S — 1 line |
| `Resources/Prototypes/_Mono/Entities/Objects/Tools/nanite_applicator.yml` | PROTO T — 1 line |
| `Content.Server/_Onyx/Chemistry/Circulation/CirculatoryStreamSystem.cs` | §2.4 sum |

- **New prototype ids:** none (the containers landed in WP13-0). **Subscription pairs:** none.
- **Hard ordering:** PROTO S and PROTO T land in the **same commit** as PROTO Q's `Damageable` change, or
  welder repair of IPCs silently dies between them (P5-D5). T-P5-12 is the guard.
- **Checkpoint:** YAMLLinter; build; `DockTest`; headless 120 s; then manually confirm in the log that
  `MobIPC` spawns with no bloodstream-solution warning.

### WP13-3 — Protogen (P5-1d)

| File | Action |
|---|---|
| `Content.Shared/_WF/Wolfmed/Body/WolfmedWoundHostExclusionSystem.cs` | EXT 3 — `ExcludedAncestors = new()` |
| `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs` | EXT 4 — stale D2 comment at `:31-34` |
| `Resources/Prototypes/_Mono/Entities/Mobs/Species/protogen.yml` | PROTO V — comment replacement |

**Checkpoint:** build; `DockTest`; spawn `MobProtogen` **and `MobProtogenRandom`** in a headless run and
confirm no `RemCompDeferred`/`_deleteSet` assert (the WP9 crash class recorded in the exclusion system's
own comment at `:30-32`).

### WP13-4 — Cable coil (P5-3)

| File | Action |
|---|---|
| `Resources/Prototypes/Entities/Objects/Tools/cable_coils.yml` | PROTO U — 1 line |

**Sequencing is the point of splitting this out.** Landing it before WP13-0/WP13-1 would close the organic
leak while delivering nothing, leaving a dead window where cable coils heal nobody. Landing it here means
IPC and cybernetic parts (`[Mechanical, Electrical]`, which `[Electrical]` overlaps) gain the coil in the
same phase organics lose it. **Checkpoint:** YAMLLinter; `DockTest`.

### WP13-5 — Mechanical analyzer and examine wording (P5-5)

| File | Action |
|---|---|
| `Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs` | append `bool Mechanical` (§2.6) |
| `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs` | set the flag in `BuildWoundDiagnostics` (`:118`) |
| `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml.cs` | branch the three LocIds |
| `Content.Shared/_Onyx/HealthExaminable/HealthExaminableSystem.PartStatus.cs` | branch the examine LocId |
| `Resources/Locale/en-US/_Onyx/medical/health-analyzer-component.ftl` | 3 appended keys |
| `Resources/Locale/en-US/_Onyx/medical/health-examinable.ftl` | 1 appended key |

**No upstream file is touched.** WP13-3 also edits `HealthAnalyzerSystem.Wolfmed.cs` (comment only) — run
WP13-3 first and re-read the file. **Subscription pairs:** none. **Checkpoint:** build both assemblies;
YAMLLinter (catches a duplicate `.ftl` key); `DockTest`.

### WP13-6 — Tests (P5-6)

| File | Tests |
|---|---|
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedSpeciesProfileTest.cs` | **new** — T-P5-1, 4, 5, 6, 7, 8, 13/21, 19 |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedSpeciesSpawnTest.cs` | **new** — T-P5-2, 3, 9, 10, 11, 12, 14, 20 |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedTreatmentMatrixTest.cs` | **new** — T-P5-15, 16, 17 |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAnalyzerTest.cs` | **extend** — T-P5-18 |

No filename collision: the existing `_WF/Wolfmed` test directory holds `WolfmedAmputationTest`,
`WolfmedAnalyzerTest`, `WolfmedDamageBridgeTest`, `WolfmedExplosionTest`, `WolfmedMedicalPatchTest`,
`WolfmedOrganTest`, `WolfmedPainTest`, `WolfmedReagentTreatmentTest`, `WolfmedReattachTest`,
`WolfmedVisualsTest`, `WolfmedWoundSurgeryTest`.
**Checkpoint:** the full Wolfmed filter plus the smoke filter, both green (§6.3).

### WP13-7 — Docs, manifest, status (P5-7)

| File | Action |
|---|---|
| `Docs/Wolfmed/WOLFMED_PLAN5.md` | **new** — this plan, as shipped |
| `Docs/Wolfmed/WOLFMED_MANIFEST.md` | append §7's rows and the deviations block; reconcile WP13-0..6 |
| `Docs/Wolfmed/WOLFMED_STATUS.md` | "What phase 5 delivers"; upstream footprint 47 → 55; **strike the "Blocked on a shared stage-based metabolizer" line**; collapse "Next phases" to phase 6 + §8.7 |

---

## 5. Subscription and name-collision audit

### 5.1 Every `SubscribeLocalEvent` pair phase 5 registers

**None.** Phase 5 registers **zero** new directed subscriptions, on any component, in any assembly. This is
the first Wolfmed phase with an empty row here, and it is why the duplicate-directed-subscription crash
class (project memory: "only one system may subscribe a given component+event; crashes at server start")
has **zero exposure** this phase.

Pairs phase 5 relies on but does not add, for the record:
`<WoundHostComponent, ComponentInit>` (`WolfmedWoundHostExclusionSystem.cs:17`, unchanged by EXT 3);
`<WoundHostComponent, BeforeDamageChangedEvent>` / `<…, DamageChangedEvent>`
(`WoundDamageRoutingSystem.cs:64-65`); `<RepairableComponent, InteractUsingEvent>`
(`RepairableSystem.cs:22`) and `<WeldingHealableComponent, InteractUsingEvent>`
(`WeldingHealableSystem.cs:25`) — different components, so not the crash class, but **the pair P5-D15
refuses to stack a second do-after on**.

### 5.2 New names — every one grepped, all free

`rg` over `Resources`, `Content.Shared`, `Content.Server`, `Content.Client`, `Content.IntegrationTests`
(RobustToolbox excluded) returned **0 code/prototype hits** for all 22 new ids; the only hits anywhere in
the repo are prose mentions in `Docs/Wolfmed/reports/**` and `Docs/Wolfmed/WOLFMED_PLAN{,3}.md` (6 files,
all documentation).

| Kind | Names |
|---|---|
| `bodyPartProfile` (4) | `IpcBodyPartProfile`, `SlimeBodyPartProfile`, `CyberneticBodyPartProfile`, `PlantBodyPartProfile` |
| `fractureProfile` (1) | `CyberneticFractureProfile` |
| `wound` (10) | `IpcMechanicalDamageWound`, `CyberneticMechanicalDamageWound`, `CyberneticFrameFractureWound`, `SlimeBluntWound`, `SlimeSlashWound`, `SlimePiercingWound`, `SlimeBurnWound`, `PlantBluntWound`, `PlantSlashWound`, `PlantPiercingWound`, `PlantBurnWound` |
| abstract `entity` (4) | `WolfmedPartSlime`, `WolfmedPartDiona`, `WolfmedPartIpc`, `WolfmedPartCybernetic` |
| `damageContainer` (2) | `SiliconWolfmed`, `InorganicWolfmed` |
| LocIds (4) | `health-analyzer-wound-bleeding-short-mechanical`, `health-analyzer-wound-fracture-short-frame`, `health-analyzer-wound-fracture-treated-short-frame`, `health-examinable-part-bleeding-mechanical` |

No `[TestPrototypes]` id collision either — that pool is `Wound*`/`Wolfmed*` fixture ids and phase 5 adds none.

### 5.3 New `!type:` classes and components

**Zero.** Phase 5 introduces no `[RegisterComponent]`, no `EntityEffect` subclass and no new `!type:`
behaviour class. The four behaviour classes the ten new wounds use (`WoundPainBehavior`,
`WoundScarBehavior`, `WoundBleedingBehavior`, `WoundFunctionalityBehavior`) are each defined exactly once in
`Content.Shared` and are already resolved by the 14 shipped wound prototypes, so the old-style bare-class
`!type:` resolution is already proven for every one. `!type:GibPartBehavior` (PROTO P(c)) is likewise an
existing class already used by `MajorLimb`, `MinorLimb`, `BaseTorso`, `PartIPCBase` and `TorsoIPC`.
`Woundable`, `WolfmedBodyPart`, `WoundHost`, `PainShockTarget`, `Bloodstream`, `Damageable` and
`Destructible` are existing registered components; phase 5 only adds YAML declarations of them.

### 5.4 D2 analysis, per change

| Change | Reachable by a non-wound-host? | Verdict |
|---|---|---|
| 16 appended wound prototypes | `WoundSystem.CanCreateWound` requires a `WoundableComponent`, filters on `profile.AcceptedDamageTypes` and then on `profile.SupportedWounds` (`Content.Shared/_Onyx/Wounds/WoundSystem.cs:109-121`). `OrganicBodyPartProfile.supportedWounds` lists none of the 10 new ids. | **D2-safe by construction.** Cost: a few extra `Contains` checks per hit. |
| 2 new `damageContainer` ids | Referenced by exactly three entity ids total (`MobIPC`, `PartIPCBase`, `CyberneticPartBase`). No shared container is edited. | **D2-safe.** |
| 4 `_WF` part abstracts | Only reachable through a `Woundable`/`WolfmedBodyPart` declaration on a part, which only matters once the part's body is a host. A detached limb has both components but no system reads them without a host. Nothing outside `Body/Parts/slime.yml` and `diona.yml` parents `PartSlime`/`PartDiona`; nothing outside `_Shitmed/Body/Parts/cybernetic.yml` parents `CyberneticPartBase`; `MobDionaNymph`/`MobDionaNymphAccent` are `SimpleMobBase` with no body parts. | **D2-safe.** |
| PROTO S/T (`- SiliconWolfmed` on two tools) | Widens two tools' accepted-target list by one container that only `MobIPC` uses. | **D2-safe.** |
| PROTO O(c) — IPC limb gib 110/150 → 190/210 | **A detached IPC limb lying on the floor is not a wound host and becomes harder to destroy** (now exactly as hard as a detached human arm). | **Recorded D2 deviation** (§8.3 R5). |
| PROTO P(c) — cybernetic limb `Destructible` (U15) | **A detached cybernetic limb also changes**: hands/feet 150/180 → 190/210, and no Heat/Ash rung on any of them. | **Recorded D2 deviation** (§8.3 R12). |
| PROTO O(b)/P(b) — `InorganicWolfmed` (U13′) | Detached IPC and cybernetic limbs can now take `Cold`/`Caustic` on their own `Damageable`. No wound forms (no host), so it is a numeric change to an unread dict unless the limb is reattached. | **Recorded D2 deviation**, cosmetic. |
| PROTO U — cable coil `[Electrical]` | `HealingSystem.TryHeal`'s non-host branch still runs the `DamageContainers` check (`HealingSystem.cs:201-210`), and `TreatmentCapabilities` is read **only** by `WoundHealingSystem.IsCompatiblePart`, which requires a `WoundableComponent` and a body relationship (`:154-166`). | **D2-safe.** Non-hosts see no change; the change is a nerf to organic **hosts** (§8.2). |
| PROTO Q Destructible raise | `MobIPC` becomes a wound host in the same package. | **D2-safe.** |
| EXT 3 — protogen | Makes `MobProtogen` + `MobProtogenRandom` wound hosts. Nothing else in the set. | **Intentional**, U4. |

---

## 6. Test plan

### 6.1 Standing traps (apply to every phase-5 test)

1. **Run `DockTest` first, every package** (project memory: `db.ef` sqlite warnings fail every pair test).
2. **`[TestPrototypes]` ids are a single global pool** across the whole suite (PLAN2 §4 rule 3). Re-grep
   `grep -rhn "^  id: Wolfmed" Content.IntegrationTests/Tests/_WF/Wolfmed/*.cs` before adding any.
3. **Bleeding determinism.** `WoundBleedingBehavior.Chance` defaults to `1f` and is **omitted** on every
   Slash/Piercing-family wound in every profile, but is **explicitly 0.25–0.7 per stage** on every
   Blunt-family wound (`BluntWound`, `SlimeBluntWound`, `PlantBluntWound`). **Use Slash or Piercing, never
   Blunt, for any "this wound bleeds" assertion.** The two mechanical wounds bleed at the *wound* level
   with `chance: 1` and no `minimumSeverity` — deterministic from the first point.
4. **Fracture determinism (P2-D23).** Only a **75-Blunt** hit gives a deterministic grade (Comminuted,
   threshold 60, `creationChance: 1`). Identical on `CyberneticFractureProfile`.
5. **`canFeelPain: false` removes the component.** `SetupPart` does `RemComp<PainComponent>(part)`
   (`WoundDamageProjectionSystem.cs:219-231`). Assert `HasComponent<PainComponent>(part) == false`, **not**
   `GetPain(part) == 0`.
6. **Two-field agreement for fractures.** `WolfmedBodyPart.fractureProfile` and `Woundable.profile` must
   agree: `WoundFractureSystem` creates the fracture wound through `CreateOrMergeWound`, which is gated by
   `profile.SupportedWounds` — a `FractureProfile` whose wound the `bodyPartProfile` does not list is a
   **silent no-op**, not an exception. Assert the fracture *wound* exists, not just that damage accumulated.
7. **Gib triggers beat amputation wherever the gib number is lower, deterministically.** Amputation needs
   **two** qualifying hits — the hit that crosses `AmputationThresholds` only calls `SetSeverable(true)` and
   returns (`AmputationSystem.cs:88-95`); severing happens on a later finishing hit (`:109-112`). The
   Destructible threshold fires on the crossing hit itself. **Never write a pure-Blunt severing assertion
   for any limb** — Blunt amputation thresholds (arm 250, leg 300, hand 150, foot 170) sit at or above the
   gib rung (MajorLimb 190 / MinorLimb 150) on every species. Use Slash (arm 130, hand 70, leg 150,
   foot 80), which is well under the Slash rung (210 / 180), or Piercing, which has no gib rung at all.
8. **`GibPart` always deletes.** `GibbingSystem.TryGibEntityWithRef` ends in `if (gibType == GibType.Gib)
   QueueDel(gibbable)` (`:190-192`), and `BodySystem.GibPart` also `QueueDel`s when `gibs.Any()`
   (`:166-167`). Assert `Deleted(part)` after a tick, not a component state.
9. Don't write positive organ-damage tests for IPC/slime/diona/protogen (P5-D13, P5-D19).

### 6.2 Test table — every expected value derived, with its source

| # | Test | File | Setup | Assertions (derived values) | Depends on |
|---|---|---|---|---|---|
| **T-P5-1** | `IpcPartRoutesBleedsAndFeelsPainTest` | SpeciesProfile | spawn `MobIPC`; `TryApplyPartDamage(body, LeftArmIPC, Slash 20)` | wound is `IpcMechanicalDamageWound` at severity **20** (`severityMultiplier: 1`, ONYX `:591-594`); `HasComp<WoundBleedingComponent>` **true** (wound-level `chance: 1`, no minimum); `HasComp<PainComponent>(part)` **true** (Ipc leaves `canFeelPain` default — P5-D10, pinned here); `WoundScarSystem.CreateScar` returns null (`scarrable: false`) | WP13-1, WP13-2 |
| **T-P5-2** | `IpcIsAWoundHostWithTheIpcProfileTest` | SpeciesSpawn | spawn `MobIPC`, tick, delete | `HasComp<WoundHostComponent>` true; **every** body child's `WoundableComponent.Profile == "IpcBodyPartProfile"`; `BloodstreamComponent.BloodReagent == "Oil"`; `BloodMaxVolume == 250`; `ChemicalMaxVolume == 0` (U16); no exception on delete | WP13-2 |
| **T-P5-3** | `IpcLeaksOilAndTakesBloodlossTest` | SpeciesSpawn | wound an IPC limb (Slash 30), run `RefreshBody`, tick `BloodstreamSystem` past `BloodlossThreshold` (0.9, `BloodstreamComponent.cs:62-63`) | `BleedAmount > 0`; the `bloodstream` solution's reagent id is `Oil` and its volume falls; `DamageableComponent.Damage.DamageDict` gains a **`Bloodloss`** entry (**this is the assertion that catches a missing `SiliconWolfmed`**). Under U2(b), invert and comment why | WP13-2 |
| **T-P5-4** | `CyberneticLimbBleedsHalfFeelsNoPainAndFramefracturesTest` | SpeciesProfile | spawn `MobHuman`; detach left arm (`WolfmedBodySystem.TryDetachPart`); attach **`JawsOfLifeLeftArm`** — the only *concrete* cybernetic arm (§1.3 row 1; `LeftArmCyberneticBase` is `abstract: true`) — via `SharedBodySystem.AttachPart`; Slash 20, then Blunt 75 | wound is `CyberneticMechanicalDamageWound`; `HasComp<PainComponent>(part)` **false** (trap 5); scar refused; bleeding rate is exactly **half** the organic arm's at equal severity (`bleedingMultiplier: 0.5`, consumed at `WoundBleedingSystem.cs:359`); at 75 Blunt the fracture's `Grade == FractureGrade.Comminuted` **and** its wound is `CyberneticFrameFractureWound` (trap 6) | WP13-1 |
| **T-P5-5** | `SlimePartRoutesBleedsSoonerAndNeverFracturesTest` | SpeciesProfile | spawn `MobSlimePerson` (`Entities/Mobs/Player/slime.yml:4`); Slash 10 | wound is `SlimeSlashWound`; `WoundBleedingComponent` present at severity **10** (no `minimumSeverity`) where the organic control at severity 8 has a bleed rate of 0 (`SlashWound`'s `minimumSeverity: 9`, `WG wounds.yml:186-232`); `PainComponent` present; scar refused (`scarrable: false`); **no `WoundFractureComponent` ever**, any Blunt amount (`fractureProfile: null`) | WP13-1 |
| **T-P5-6** | `DionaPartScarsAndIsNeverSeverableTest` | SpeciesProfile | spawn `MobDiona` (`Entities/Mobs/Player/diona.yml:5`); Slash 20; then drive `LeftArmDiona` past **Slash 130** and deliver a 14-Piercing finishing hit | wound is `PlantSlashWound`; `CreateScar` **succeeds** (`scarrable` default true — the one non-organic species that scars); `PainComponent` present; **`WolfmedBodyPartComponent.AmputationThresholds.Count == 0`** and `BodyPartComponent.Severable` stays **false** at every damage level (`AmputationSystem.HandlePartDamageApplied` early-returns on `Count == 0`, `:80`); no fracture ever. **Keep the Slash total under 210** so the gib rung does not fire and confuse the assertion — the destruction case is T-P5-20 | WP13-1 |
| **T-P5-7** | `SlimeBleedsFifteenPercentFasterTest` | SpeciesProfile | equal-severity `SlimeSlashWound` on a slime and `SlashWound` on a human, same stage | slime `CurrentRate` / human `CurrentRate` == **1.15** within tolerance (`bleedingMultiplier` 1.15 vs 1.0) | WP13-1 |
| **T-P5-8** | `CyberneticFrameFractureIsMendableBySurgeryTest` | SpeciesProfile | `JawsOfLifeLeftArm` driven to Comminuted (75 Blunt); run `WolfmedSurgeryMendFractureEffect`'s `SurgeryStepEvent` and its `SurgeryStepCompleteCheckEvent` | `TryMend` succeeds; `removeWoundWhenMended: true` removes the `CyberneticFrameFractureWound`; the completion check does not stall (P4-D20's `reductionMinimumGrade: Simple` quirk is identical on both profiles). **Resolves `species.md` T3 empirically** | WP13-1 |
| **T-P5-9** | `ProtogenIsAWoundHostTest` | SpeciesSpawn | spawn `MobProtogen` **and `MobProtogenRandom`**, tick, delete | `HasComp<WoundHostComponent>` **true** on both (U4 = lift); every part's profile is `OrganicBodyPartProfile`; **no organ carries `OrganDamageComponent`** (pins P5-D19 as a known gap rather than letting it rot); no `RemCompDeferred` assert. Under U4(b), assert `WoundHost` absent instead | WP13-3 |
| **T-P5-10** | `IpcDoesNotGibFromRoutineLimbDamageTest` | SpeciesSpawn | spawn `MobIPC`; apply 600 total Blunt spread across limbs after death | the entity still exists (`Destructible` 1500); regression guard for P5-D11 | WP13-2 |
| **T-P5-11** | `IpcLimbSeverabilityAndGibCeilingTest` (**re-derived**) | SpeciesSpawn | (i) 140 pure **Slash** on `LeftArmIPC`; (ii) a fresh IPC, 195 pure **Blunt** on `LeftArmIPC` | (i) the part is **not deleted** (Slash gib rung 210) and `AmputationSystem` has marked it `Severable` (Slash threshold 130, `_WF/Wolfmed/Body/parts.yml:54-55`, progress `140/130 = 1.08 ≥ 1`); (ii) the part **is** deleted (Blunt gib rung 190 < Blunt threshold 250) — this documents trap 7 rather than asserting a bug. Under U3′(a) the (i) threshold drops to 145 Slash and (ii) to 115 Blunt; under (c) to 400/400 | WP13-1 |
| **T-P5-12** | `WelderStillRepairsAnIpcTest` | SpeciesSpawn | damage an IPC; run `WeldingHealableSystem`'s `SiliconRepairFinishedEvent` path with a fuelled `Welder` | damage falls, and an `IpcMechanicalDamageWound`'s severity falls with it (routed, unscoped — `CanTreatPart` returns true with no scope open, `WoundDamageRoutingSystem.cs:971-974`). **This is the test that catches a missing PROTO S/T** | WP13-2 |
| **T-P5-13** | `IpcAndCyberneticIgnoreColdAndCausticTest` (**U13′(a) only**) | SpeciesProfile | `TryChangeDamage(part, Cold 10)` and `(part, Caustic 10)` on an IPC part and a cybernetic part | `DamageableComponent.Damage.DamageDict` gains **no** `Cold`/`Caustic` key (`Inorganic` = Brute + Heat/Shock, `containers.yml:10-16`; `Silicon` = + Radiation, `:27-34`). Comment: expected to flip if the containers are ever extended — update deliberately, don't leave red | WP13-1 |
| **T-P5-21** | `IpcTakesColdAndCausticTest` (**U13′(b) only — replaces T-P5-13**) | SpeciesProfile | `TryApplyPartDamage(body, LeftArmIPC, Caustic 15)` and `(…, Cold 20)` | `DamageDict` gains both keys, and an `IpcMechanicalDamageWound` exists (`Caustic` and `Cold` are both in `IpcBodyPartProfile.acceptedDamageTypes` and in the wound's `damageTypes` — ONYX `:583-632`, `Cold` reopen 18, `Caustic` reopen 12). Restores two of seven accepted types to parity with Onyx | WP13-1 |
| **T-P5-14** | `EveryBodyPartProfileUsesThePrimaryStreamTest` | SpeciesSpawn | enumerate every `BodyPartProfilePrototype` in the prototype manager | `profile.CirculatoryStream == CirculatoryStreamPrototype.PrimaryStream` for all of them. Three lines; **enforces P5-D3 permanently and loudly instead of silently** | WP13-0 |
| **T-P5-15** | `TreatmentCapabilityMatrixTest` | TreatmentMatrix | organic host + IPC host; three synthetic `HealthChange`-shaped calls with `TreatmentCapabilities` `[Biological]` / `[Mechanical]` / `[Electrical]` against a `BluntWound` and an `IpcMechanicalDamageWound` | 6 cells: Biological heals only the organic wound; Mechanical and Electrical each heal only the IPC wound (`IpcBodyPartProfile` lists both); every mismatched cell leaves severity and `GetAllDamage` unchanged (mirrors `WolfmedReagentTreatmentTest.TreatmentCapabilityMismatchDoesNotHealTest`) | WP13-1 |
| **T-P5-16** | `CableCoilNoLongerHealsOrganicsButHealsMechanicalTest` | TreatmentMatrix | Heat-damaged organic host + Heat-damaged IPC part; a `CableApcStack`'s `HealingComponent` through `IsWoundDamaged`/`ResolveHealingPart` | organic: refused (`{Electrical}` ∩ `{Biological}` = ∅); IPC part: **heals** (`{Electrical}` ∩ `{Mechanical, Electrical}` ≠ ∅). Documents P5-D12's nerf and its compensating gain in one test | WP13-4 |
| **T-P5-17** | `CableCoilRadiationStillHealsSystemicallyTest` (regression guard) | TreatmentMatrix | organic host with systemic Radiation damage; same coil | the coil's `Radiation: -3.0` component still lands: `Radiation` is not in `WoundHostComponent.LocalizedDamageTypes` (`WoundDamageComponents.cs:32-45`), so it never reaches `CanTreatPart`. Guards against someone "fixing" the systemic bypass as a side effect | WP13-4 |
| **T-P5-18** | `MechanicalWoundDiagnosticTextResolvesTest` | `WolfmedAnalyzerTest.cs` (extend) | IPC host; `CreateOrMergeWound(part, "IpcMechanicalDamageWound", 30)` (Moderate — threshold 25); `BuildWoundDiagnostics(body)` | exactly one `HealthAnalyzerVisibleWound` with `Name == "wound-name-ipc-mechanical-damage"`, `StageName == "wound-stage-mechanical-moderate"`, `Count == 1`; both LocIds resolve through `ILocalizationManager` to **`"chassis damage"`** and **`"moderate"`** (`wounds.ftl:10, :38`), not a raw-key placeholder; `Mechanical == true` on the payload | WP13-5 |
| **T-P5-19** | `CyberneticLimbGibCeilingTest` (**new**) | SpeciesProfile | `JawsOfLifeLeftArm` on a human host: (i) 140 pure Slash; (ii) fresh limb, **Heat 260** | (i) not deleted, `Severable` true (Slash 130 threshold vs the 210 rung). (ii) **under U15(a)**: the part is **not** deleted and no `Ash` entity exists nearby (`CyberneticPartBase`'s own `Destructible` has no Heat rung). **Under U15(c)**: assert the opposite and comment that a steel prosthetic burns to `Ash` with `MeatLaserImpact` — the pre-existing behaviour (`Body/Parts/base.yml:294-307`, `BurnBodyBehavior.cs:29-34`) | WP13-1 |
| **T-P5-20** | `DionaLimbIsDestroyedNotSeveredTest` (**new**) | SpeciesSpawn | `MobDiona`; 195 pure Blunt on `LeftArmDiona` | `AmputationThresholds.Count == 0` **and** the part entity **is deleted** (inherited `MajorLimb` Blunt rung 190, `Body/Parts/base.yml:281-287`). This is the test that stops "diona limbs cannot be severed" being read as "diona limbs are indestructible" (§8.1, U17). Under U17(b), invert | WP13-1 |

**20 tests, 4 files** (T-P5-13 and T-P5-21 are alternatives, so exactly one of them ships). T-P5-3's second
half, T-P5-9, T-P5-11, T-P5-13/21, T-P5-19 and T-P5-20 are the decision-dependent ones (U2, U4, U3′, U13′,
U15, U17); all are written to assert whichever way the user goes, so none costs anything to defer.

**Deliberately not written:** positive organ-damage tests for IPC/slime/diona/protogen (P5-D13, P5-D19); any
pain-numbness status-effect test (P5-D14); any pure-Blunt severing assertion (trap 7); the "before"
regression tests `tests.md` §3.2 proposed (they document a bug this very phase fixes, and would have to be
deleted in the same package that writes them — T-P5-5/T-P5-6 assert the *correct* post-fix behaviour
instead, which is the useful half).

### 6.3 Run commands

```bash
# Wolfmed suite:
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt \
  --filter "FullyQualifiedName~_Onyx.Wounds|FullyQualifiedName~_Onyx.Body|FullyQualifiedName~_Onyx.Medical|FullyQualifiedName~Wolfmed"

# Smoke regression (unchanged from phase 4) - run DockTest FIRST, alone, every package:
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt --filter "FullyQualifiedName~DockTest"
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt \
  --filter "FullyQualifiedName~EntityTest|FullyQualifiedName~PrototypeSaveTest|FullyQualifiedName~DockTest"

# Prototype/locale sanity (catches a bad !type:, a missing profile field, a missing damageContainer id,
# a duplicate .ftl key):
dotnet run --project Content.YAMLLinter -c Release

# Headless server, 120 s, watching for [ERRO]/[FATL] - mandatory on WP13-0/1/2/3/4.
```
A worktree needs `RobustToolbox` junctioned from the main checkout before building.

---

## 7. Manifest rows

### 7.1 Row changes to existing rows (WP13-7 reconciles)

- `Resources/Prototypes/_Onyx/Wounds/wounds.yml` — status changes from **"trimmed (14 of 30 prototypes)"**
  to **"complete (30 of 30), 2 marked fold classes"**. *(The existing row reads "13 of 29"; both numbers
  are off by one — WG has 14 `id:` lines, Onyx has 30.)*
- `Content.Shared/_WF/Wolfmed/Body/WolfmedWoundHostExclusionSystem.cs` — note changes to "exclusion set is
  empty as of P5-D9; kept as the mechanism for any future synthetic species".
- `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs` — gains a second marked site
  (the EXT 4 comment) and the `Mechanical` flag producer.
- `Resources/Prototypes/_Mono/Entities/Mobs/Species/protogen.yml` — deviation note updated (exclusion
  lifted; organ gap recorded).
- `Content.Server/_Onyx/Chemistry/Circulation/CirculatoryStreamSystem.cs` — gains a second marked site.
  *(The existing row lists this file under `Content.Shared/…`; the path is `Content.Server/…` — its
  namespace is `Content.Shared._Onyx.Chemistry.Circulation`, which is what caused the mistake.)*
- The phase-1 note that "IPC/Slime/Plant streams need a shared stage-based metabolizer" is **struck** and
  replaced with the §8.7 finding.

### 7.2 New rows

| Onyx source | Wolfgate destination | Status | WP | Notes |
|---|---|---|---|---|
| `ONYX _Onyx/Wounds/wounds.yml:37-151` (4 profiles) | same path | vendored, 1 fold class | WP13-0 | D9 fold on Ipc/Slime/Plant `organDamage`; no `maxAffected`, no extra limb rows; `acceptedDamageTypes` kept verbatim with inert-marker comments |
| `ONYX …wounds.yml:193-231` (`CyberneticFractureProfile`) | same path | vendored, 1 fold | WP13-0 | §8.2-1 manipulation modifiers → 1.1/1.25/1.5/2.0. Byte-identical to `OrganicFractureProfile` otherwise |
| `ONYX …wounds.yml:337-371, 583-681, 683-1039` (10 wounds) | same path | vendored verbatim | WP13-0 | No folds. Stage LocIds for Slime/Plant are the **generic** `wound-stage-*`, not species-specific |
| ONYX `Corvax/Body/Species/ipc.yml:322-330`, `Body/Species/slime.yml:232-239`, `Body/Species/diona.yml:226-235`, `_Onyx/Entities/Mobs/Customization/Parts/cybernetic.yml:1-21` | `Resources/Prototypes/_WF/Wolfmed/Body/species_parts.yml` | new (re-expressed, D8) | WP13-1 | Onyx's `- type: BodyPart fractureProfile:` becomes `- type: WolfmedBodyPart`. `amputationThresholds` deliberately **not** ported for Ipc/Cybernetic (P5-D7); diona's `{}` **is** ported |
| — (no Onyx source) | `Resources/Prototypes/_WF/Wolfmed/Damage/containers.yml` | Wolfgate-authored | WP13-0 | `SiliconWolfmed` = Silicon + the `Bloodloss` **type**. `InorganicWolfmed` (U13′) = Brute + Burn + Radiation, restoring the `Cold`/`Caustic` Onyx's `SiliconIpc` has and WG's `Inorganic`/`Silicon` lack |
| ONYX `Corvax/Body/Species/ipc.yml:92-113` + `Body/species_base.yml:54` | `Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/ipc.yml` | upstream, marked (PROTO Q) | WP13-2 | `WoundHost`, `PainShockTarget`, `Bloodstream` (`bloodReagent`/`bloodMaxVolume`/`chemicalMaxVolume: 0` instead of Onyx's `bloodReferenceSolution`; **no `InjectableSolution`**, a deliberate divergence — Onyx gets one by parenting `MobBloodstream`), `Damageable`, D22 Destructible raise |
| — | `Entities/Objects/Tools/welders.yml`, `_Mono/Entities/Objects/Tools/nanite_applicator.yml` | upstream, marked (PROTO S/T) | WP13-2 | **Mandatory companion to PROTO Q's container change** |
| — | `Resources/Prototypes/Body/Parts/{slime,diona}.yml`, `_EinsteinEngines/Body/Parts/ipc.yml`, `_Shitmed/Body/Parts/cybernetic.yml` | upstream, marked (PROTO M/N/O/P) | WP13-1 | One `parent:` line each; `_EE ipc.yml` also gets the two gib numbers and (U13′) a container; `cybernetic.yml` also gets (U13′) a container and (U15) a `Destructible` block |
| ONYX `Entities/Objects/Tools/cable_coils.yml:179` | `Resources/Prototypes/Entities/Objects/Tools/cable_coils.yml` | upstream, marked (PROTO U) | WP13-4 | **One line only.** Onyx's coil is otherwise different (`delay: 3.5`, `Blunt/Slash/Piercing/Heat -3`, `Shock -5`, `damageContainers: [SiliconIpc]`); WG keeps its own `delay: 0.6` / `Heat/Shock/Radiation -3` / `[Silicon]` |
| — | `Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs` + 2 `.ftl` + 3 consumers | in-vendored / `_WF` | WP13-5 | P5-5. Onyx does none of this; Wolfgate improvement |
| — | 3 new `Content.IntegrationTests/Tests/_WF/Wolfmed/*.cs` + 1 extended | new tests | WP13-6 | 20 tests |

### 7.3 Deviations block to add (phase 5)

1. **`CyberneticFractureProfile` manipulation modifiers corrected** to 1.1/1.25/1.5/2.0 (§8.2-1 applied to
   the second profile). Onyx's values would make a shattered cybernetic arm faster at do-afters.
2. **IPC and cybernetic amputation thresholds are Wolfgate's organic set, not Onyx's** (P5-D7). Onyx's
   arm numbers (270/400/600) are unreachable under Wolfgate's gib rungs.
3. **IPC limb gib triggers changed 110/150 → 190/210** (P5-D8), matching Mono's `MajorLimb`, which every
   organic arm and leg carries. *Also affects detached IPC limbs — a recorded D2 deviation.* Two
   asymmetries are accepted: IPC **hands/feet** are now tougher than organic ones (190/210 vs
   `MinorLimb`'s 150/180), and the IPC **head** is destructible at 190 where an organic head has no gib
   trigger at all (`BaseHead: parent: WolfmedBaseHead`, `Body/Parts/base.yml:81-82`).
4. **(U15) `CyberneticPartBase` gains its own `Destructible`** (Blunt 190 / Slash 210, no Heat rung),
   which removes the pre-existing behaviour where a steel prosthetic at Heat 250 spawns `Ash`, runs
   `BurnBodyBehavior` and plays the `MeatLaserImpact` flesh sound (P5-D8b). Cybernetic hands/feet become
   190/210 rather than 150/180.
5. **`SiliconWolfmed` damage container** — IPC oil loss deals `Bloodloss`; Onyx's own IPC takes no bloodloss
   damage at all (P5-D5). Carries two mandatory companion tool edits. **`Bloodloss` is added as a type, not
   via the `Airloss` group** — the group's only cited benefit (the tourniquet's `Asphyxiation` cost) is
   applied to the *part*, not the body, so it could never materialise (P5-D5b).
6. **`MobIPC` gains `PainShockTarget` explicitly** because Wolfgate moved it to `BaseMobSpeciesOrganic`
   in P2-D7 whereas Onyx has it on `BaseSpeciesMob` (P5-D10). Net effect is Onyx parity.
7. **`MobIPC`'s gib threshold raised to 1500** (D22, P5-D11). `PlayerSiliconHumanoidBase` is **not** edited:
   `MobIPC`'s own `thresholds` list replaces the parent's, and `MobIPC` is its only descendant.
8. **Protogen is an organic wound host** — the D32 exclusion is lifted (P5-D9). Hosts created:
   `MobProtogen` and `MobProtogenRandom`. `MobProtogenDummy` parents `BaseSpeciesDummy` and is unaffected.
9. **Protogen has no organ damage, organ destruction, internal bleeding or organ surgery** (P5-D19). Its
   organs are `BaseProtogenOrgan`-derived and carry no `OrganDamage`/`WolfmedOrgan`. Pre-existing for every
   non-human organic host (moth, vox, arachnid, diona, slime), newly relevant because phase 5 enrols a
   species into it.
10. **`organDamage` on the Ipc/Slime/Plant profiles is inert** — no organ in those species carries
    `OrganDamageComponent` (P5-D13, §8.6-7 unchanged).
11. **`Cold`/`Caustic` on IPC and cybernetic parts: a Wolfgate-only loss versus Onyx, closed by U13′(b).**
    Onyx's `SiliconIpc` takes the whole `Burn` group (`Heat, Shock, Cold, Caustic`); WG's `Inorganic` and
    `Silicon` do not, so two of seven `acceptedDamageTypes` and two of six wound `damageTypes` were dead —
    cryogenics and acid, which in Onyx are among the strongest anti-robot tools. **This is not a hole Onyx
    shares** (revision 1 said it was). The `_WF` `InorganicWolfmed` container restores both without
    touching any shared container. Under U13′(a) it stays a documented loss.
12. **Cable coil no longer heals organic Heat/Shock wounds** — a live pre-existing leak, closed (P5-D12).
    The coil's other numbers stay Wolfgate's, not Onyx's.
13. **Regenerative Mesh and Medicated Suture keep Onyx's `allowedWoundStages` off** (U6).
14. **No `Repairable` on `MobIPC`, no `Healing` on the welder** (P5-D15, P5-D16).
15. **No `InjectableSolution` and `chemicalMaxVolume: 0` on `MobIPC`** (U16) — a divergence from Onyx,
    which parents `MobBloodstream`. An IPC has no metabolizer (`OrganIPCPump`'s `Metabolizer` block is
    commented out, `_EinsteinEngines/Body/Organs/ipc.yml:70-76`), so an injectable 250 u solution would be
    a trap.
16. **P5-4 pain numbness closed permanently**, not deferred (P5-D14). `PainNumbnessStatusEffectComponent`
    remains dead code: two readers, no writer.
17. **Skeleton (`MobSkeletonPerson`) is outside the wound system** — never was in it, now recorded (P5-D18).
18. **Slime and diona internal bleeding is listed in their profiles but unreachable** —
    `InternalBleedingWound` is only ever produced by `WolfmedOrganComponent.destructionWound`, attached
    human-lineage only (`_WF/Wolfmed/Body/organs.yml:54,…`). Surgery can still create one manually.
19. **A tourniquet costs half as much on a robot or plant limb** — its `Asphyxiation: 5` share is dropped by
    the part's container (`TourniquetSystem.cs:88-89` applies the cost to the part;
    `Inorganic`/`Silicon`/`InorganicWolfmed` support no `Airloss`). Accepted, and it is the reason
    deviation 5 does not take the group.
20. **Diona and slime limbs are destroyed, not severed, past 190/210 Blunt/Slash** (U17). Their
    `amputationThresholds: {}` disables `AmputationSystem` only; the inherited `MajorLimb`/`MinorLimb`
    `GibPartBehavior` still deletes the limb, so phase 3's thrown limb, stump `AmputationConsequenceWound`,
    `SurgeryHealAmputationConsequence` and the P4 reattachment gate are all unreachable for them.
    Pre-existing; recorded because phase 5 is the first document to state "cannot be severed".

---

## 8. Risks, balance, and user decisions

### 8.1 What each species' player experiences

Read with §8.1a, which gives the limb-loss numbers once instead of repeating them per row.

| | changes? | bleeds | pain | scars | bone / frame fracture | limb loss | passive + bed heal | treated by | organ damage |
|---|---|---|---|---|---|---|---|---|---|
| **Human & the other 23 organic species** | **only the cable coil** | Blood ×1.0 | yes | yes | bone | severed by Slash 130 / Piercing 250 / Heat 250 on an arm; **destroyed outright at Blunt 190 / Slash 210, and burned to Ash at Heat 250** — so *pure-Blunt* severing is unreachable (pre-existing, §8.3 R11) | yes | Biological — every medicine, topical, gauze, tourniquet, surgery — **minus the cable coil** | yes, human lineage only |
| **Diona** | **yes, big** | Sap ×1.0, **from the first scratch** | yes | **yes** (the only non-organic profile that scars) | **none** — no `BrokenBones` alert, no `SurgeryMendFracture`, no fracture movement/manipulation penalty ever | **never cleanly severed** (`amputationThresholds: {}`), but **still destroyed outright at Blunt 190 / Slash 210 and burned to Ash at Heat 250**. So no thrown limb, no stump wound, no reattachment — the limb simply ceases to exist | yes | Biological (unchanged) | **none** — diona organs carry no `OrganDamage`; no internal bleeding, no organ surgery (pre-existing) |
| **Slime** | **yes, big** | Slime ×**1.15**, **from the first scratch** | slightly less from blunt (`0.7` vs `0.87`); burns identical | **never**, including the surgical `MedicalScarWound` | **none** | same as diona: unseverable by threshold, still destroyed at 190/210/250 | yes, organic schedule | Biological (unchanged) | **none** |
| **IPC** | **yes — joins the whole medical system at once** | **Oil ×1.0, every chassis wound leaks (`chance: 1`, no minimum), and `Oil` is `flammability: 2` with a `FlammableTileReaction` — the trail can be set on fire** | **yes**, plus pain shock (2 s paralyze + forced scream + jitter + 30 s adrenaline) — **and no chemical relief exists at all** (no metabolizer). Also **drunk + stuttering while below 90 % fluid**, same as any bleeding organic | never | **none** | severable on the organic arm numbers (Slash 130 / Piercing 250 / Heat 250); limb gib ceiling 110/150 → **190/210** (U3′). **No Heat/Ash rung** — an IPC limb never burns to ash. IPC **hands/feet** end up tougher than flesh, the IPC **head** destructible where a human head is not | **never** — `passiveRecoveryMultiplier: 0`, `bedRecoveryMultiplier: 0` | **welder, nanite applicator, cable coil.** Tourniquet and every wound surgery work. **No medicine, no brute pack, no ointment, no gauze** | none |
| **Cybernetic limb on an organic body** (PrybarProsthetics / BionicLegs traits, lathe, autosurgeon) | **yes** | the wearer's own blood, **×0.5**, and only from `Dismemberment`/`SurgicalIncision` — mechanical wounds carry no bleed behaviour | **none — the limb is numb**; `PainComponent` is actively removed | never | **frame fracture** (microcrack → cracked → deformed → shattered), same thresholds and chances, same `BrokenBones` alert, same movement and manipulation penalties, mendable by `SurgeryMendFracture` | severable on the organic arm numbers; **today it is also destroyed at Blunt 190 / Slash 210 and burns to `Ash` with a flesh sound at Heat 250** — U15(a) removes the ash rung and moves hands/feet to 190/210 | **never** | **cable coil** (and the welder, only if the body is an IPC). No medicine | n/a |
| **Protogen** | **yes** (if U4 = lift) | Blood ×1.0 | yes, **including pain shock and the `EmoteOnDamage` screams** it inherits from `BaseMobSpeciesOrganic` | yes | bone | organic | yes | everything an organic gets — gains the tourniquet, the analyzer panel, the pain overlay and all wound surgeries it silently lacks today | **none** — `BaseProtogenOrgan`-derived organs carry no `OrganDamage`, so no organ damage, no organ destruction, no internal bleeding and none of the seven organ surgeries (P5-D19) |
| **Skeleton, borgs, monkeys, chimera, diona nymphs, every NPC** | **no change** | — | — | — | — | — | — | — | — |

### 8.1a The limb-loss numbers in one place

| part | gib trigger (`Destructible`) | amputation thresholds (`WolfmedBodyPart`) | net |
|---|---|---|---|
| organic **arm / leg** (`MajorLimb`) | Blunt 190, Slash 210, **Heat 250 → Ash + BurnBody + MeatLaserImpact** | arm Slash 130 / Piercing 250 / Blunt 250 / Heat 250; leg 150 / 250 / 300 / 250 | Slash and Piercing sever; pure Blunt destroys first |
| organic **hand / foot** (`MinorLimb`) | Blunt 150, Slash 180, Heat 230 → Ash | hand 70 / 200 / 150 / 200; foot 80 / 220 / 170 / 220 | Slash severs; Blunt ties the gib rung and the **gib wins** (trap 7) |
| organic **head** (`BaseHead`) | **none** | 200 / 200 / 350 / 200 | always severable, never destructible |
| organic **torso** (`BaseTorso`) | 400 / 400 / 400 | n/a (`AmputationSystem` skips `Torso`, D9) | destroyed only |
| **IPC** any limb (`PartIPCBase`, first parent, replaces the rung) | today Blunt 110 / Slash 150; **P5-D8 → 190 / 210**, no Heat rung | inherited organic set per slot | one block for every slot, hence the two asymmetries |
| **IPC torso** (`TorsoIPC`, own block) | 400 / 400 | n/a | unchanged |
| **cybernetic** limb (inherits `MajorLimb`/`MinorLimb` today) | 190/210/250-Ash or 150/180/230-Ash | inherited organic set per slot | U15(a) replaces with 190/210, no Heat rung |
| **diona / slime** limb | inherits `MajorLimb`/`MinorLimb` unchanged | **`{}` — empty** | destroyed only, never severed |

### 8.2 The one nerf, stated plainly

`CableApcStack`/`CableHVStack`/`CableMVStack` etc. have been healing organic Heat and Shock damage since
HOOK 8 landed in phase 4 — `-3.0` Heat and `-3.0` Shock per **0.6 s** (the fastest `delay` of any healing
item in the tree; Ointment and Brutepack are 2 s), with a 30-unit stack and no per-use consumption beyond
the stack. That is `-5/-5` per second versus Ointment's `-7.5/-7.5/-7.5/-5`, on an item every engineer
carries anyway. It was never intended — the item's own comment (`cable_coils.yml:32-35`) says it is for
"Estacao Pirata IPCs" and it relied on `damageContainers: [Silicon]`, which HOOK 8 bypasses for wound hosts.
**Closing it is correct, and it is still a nerf.** It belongs in the changelog.

### 8.3 Risks that will silently ship something broken if ignored

- **R1 — Parent order is the whole design.** If `WolfmedPart*` is not **first** in the species base's
  `parent:` list, `Base<Slot>`'s `fractureProfile: OrganicFractureProfile` wins and slimes get bones. The
  shipped phase-1 files use the opposite order for a reason that does not apply here (P5-D1). Assert it
  (T-P5-5 / T-P5-6), never eyeball it.
- **R2 — `amputationThresholds: {}` must survive inheritance.** An empty flow mapping is a *present* key
  and therefore suppresses the parent's dict. A typo (`amputationThresholds:` with nothing after it) parses
  as a **null**, not an empty dict. T-P5-6 asserts `Count == 0` on a spawned `LeftArmDiona`.
- **R3 — PROTO S/T are not optional.** Without them, the `SiliconWolfmed` container silently removes the
  only way to repair an IPC. T-P5-12 is the guard.
- **R4 — An IPC in pain has no way out.** IPCs have no metabolizer (`OrganIPCPump`'s `Metabolizer` block is
  commented out, `_EinsteinEngines/Body/Organs/ipc.yml:70-76`), so no painkiller, no `SuppressPain`, no
  adrenaline chem — and with `chemicalMaxVolume: 0` and no `InjectableSolution` there is nowhere to put one.
  Pain only falls as the chassis is repaired. Pain shock at 130 will also make an IPC **scream** —
  `PainSystem.UpdatePainShock` calls `TryEmoteWithChat(entity, "Scream", …, forceEmote: true)`
  (`PainSystem.cs:299-300`), bypassing `allowedEmotes`. And below 90 % fluid a leaking IPC becomes **drunk
  and stutters**: `BloodstreamSystem.Update`'s bloodloss branch (`:141-162`) carries no wound-host or
  species guard, unlike `OnDamageChanged` (GUARD E, `:212-215`). That is what every bleeding organic
  already experiences, so it is Wolfgate-consistent — but a drunk robot is a flavour oddity. Flag all of
  this for playtest.
- **R5 — Detached IPC limbs change durability.** PROTO O(c)'s gib change applies to the loose limb entity
  too. Recorded D2 deviation; under U3′(b) a detached IPC arm becomes exactly as tough as a detached human
  arm, which is the point.
- **R6 — `Cold` and `Caustic` are dead on IPC and cybernetic parts under U13′(a).** The profiles accept
  them and both mechanical wounds list them, but `Inorganic`/`Silicon` do not support them, so
  `ApplyPartChange` applies zero and no wound is created. **This is a Wolfgate-only loss versus Onyx**
  (deviation 11), not a hole Onyx shares. Under (a), do **not** write a test expecting "Caustic creates a
  chassis wound" — T-P5-13 pins the current behaviour as a canary instead. Under (b), T-P5-21 asserts the
  restored behaviour.
- **R7 — Body damage on an IPC is now the sum of its parts.** `SlowOnDamage` on `MobIPC` (`_EE ipc.yml:88-92`,
  60/90/120 → 0.7/0.5/0.3) reads that sum. Total damage dealt is unchanged, but on wound hosts it decays
  much more slowly (D29/D33 neutralised passive regen, and the Ipc profile's own recovery is 0), so IPCs
  will sit in the slow bands longer than they do today. Not a bug; watch it in playtest.
- **R8 — `TorsoIPC` will have a zeroed `WolfmedBodyPart` after PROTO O(a), and that is correct.**
  `TorsoIPC: parent: [ PartIPCBase, BaseTorsoInorganic ]` (`_EE ipc.yml:26-27`) inherits `WolfmedPartIpc`'s
  block, whose `maxDamage` key is absent, so `MaxDamage` takes the component default **0**
  (`WolfmedBodyPartComponent.cs:16`) — overflow disabled — and `AmputationThresholds` stays empty, which
  `AmputationSystem` skips for torsos anyway (`:78`). Document it or someone will later "fix" it.
  *(Revision 1 said `TorsoIPC` would have no `WolfmedBodyPart` at all; after PROTO O(a) that is false.)*
- **R9 — Two profile fields must agree for fractures.** `WolfmedBodyPart.fractureProfile` and
  `Woundable.profile` — `CyberneticFractureProfile` + `CyberneticBodyPartProfile`. Mismatch = silent no-op.
- **R10 — `EntityTest` is the cheapest catch.** It spawns and deletes every prototype in the game. Run it
  after WP13-1 and WP13-2 before anything else.
- **R11 — Pre-existing: organics can never be severed by pure Blunt.** `MajorLimb` gibs an arm at Blunt 190
  while `WolfmedBaseLeftArm`'s Blunt amputation threshold is 250 (and a leg's is 300 against the same 190);
  `MinorLimb` gibs a hand at Blunt 150 against a threshold of exactly 150, which the gib wins (trap 7).
  This is a phase-3 defect, not phase 5's — phase 5 is simply the first document with the numbers side by
  side. **Do not fix it in phase 5** (it would re-tune every organic limb in the game); hand it to the
  balance pass with these numbers.
- **R12 — Pre-existing: a steel prosthetic burns to `Ash`.** A cybernetic limb at Heat 250 runs
  `SpawnEntitiesBehavior{Ash}` + `BurnBodyBehavior` + `PlaySoundBehavior{MeatLaserImpact}` inherited from
  `MajorLimb`. U15(a) is the cheap fix; U15(c) ships it as-is and records it.
- **R13 — WP13-3 and WP13-5 both edit `HealthAnalyzerSystem.Wolfmed.cs`.** Run WP13-3 first and re-read the
  file in WP13-5. Sequential packages make this safe as long as nobody reorders them.

### 8.4 Genuine user decisions

Grouped: **A** must be answered before the named WP starts; **B** has a safe default and can be taken as
recommended without review.

| # | Group | Question | Options | **Default (recommendation)** | Needed by |
|---|---|---|---|---|---|
| **U3′** | **A** | IPC limb gib triggers. Revision 1 proposed 400/400 on the false premise that organic limbs have none; they have 190/210 (`MajorLimb`) and 150/180 (`MinorLimb`). | (a) leave 110/150; (b) **190/210 — parity with the trigger every organic arm and leg actually carries**; (c) 400/400 (= `BaseTorso`'s number, making an IPC limb 2.1× tougher than flesh, detached limbs included); (d) full per-slot parity — five extra marked blocks so IPC hands/feet are 150/180 and the IPC head has none | **(b)** — smallest change that makes `DismembermentWound` reachable, and it is the real organic number. Accept the two asymmetries (IPC extremities tougher, IPC head destructible) and note them in the changelog. Take (c) only if IPC limbs are *meant* to be tougher than flesh; (d) only if the asymmetries bother you enough to pay five more upstream edits | WP13-1 |
| **U15** | **A** | Cybernetic limbs inherit `MajorLimb`/`MinorLimb`, so **today** a steel arm is destroyed at Blunt 190 and at Heat 250 spawns `Ash`, runs `BurnBodyBehavior` and plays a flesh sound. Phase 5 does not create this, but it is the phase that makes those limbs medical. | (a) give `CyberneticPartBase` a marked `Destructible` (Blunt 190 / Slash 210, **no Heat rung**), mirroring `PartIPCBase`'s `# no ashing trigger`; (b) lower `WolfmedPartCybernetic`'s amputation thresholds below 190 instead; (c) ship as-is and record | **(a)** — one marked block in a file PROTO P already edits; removes the ash absurdity and makes cybernetic limbs behave like the IPC chassis they resemble. (b) diverges from organic parity for no gain. (c) is defensible (it is the status quo) but leaves a visible bug | WP13-1 |
| **U2** | **A** | Does IPC oil loss actually damage the IPC? | (a) `SiliconWolfmed` container + PROTO S/T; (b) faithful-to-Onyx: bleeding is cosmetic, no container change, no tool edits, 0 cost | **(a)** — bleeding with no consequence is a trap: the phase-4 analyzer will print a bleed rate and a clotting phase for a patient who cannot be harmed by it, and a medic who tourniquets an IPC achieves nothing measurable. It also restores phase 3's decapitation charge that D2-safe code already computes and discards. **If (b): say so in the guidebook copy** | WP13-2 |
| **U13′** | **A** | `Cold`/`Caustic` on IPC and cybernetic parts, which **Onyx supports and Wolfgate does not** — two of seven accepted types and two of six wound damage types are dead, i.e. cryogenics and acid do nothing to a robot where in Onyx they are the strongest anti-robot tools. | (a) leave, record as a Wolfgate-only loss, ship T-P5-13 as a canary; (b) add the `_WF` `InorganicWolfmed` container to the two part bases phase 5 already edits | **(b)** — it is D4 ("Onyx defaults"), it is cheap (one prototype + two lines phase 5 is already writing), it touches no shared container so it is D2-safe, and it works because routing runs before the body's type filter (P5-D20). It **is** a balance change: acid and cryo become anti-robot tools. If that is unwanted, (a) is honest as long as deviation 11 stays worded as a loss | WP13-1 |
| **U4** | **A** | Protogen: lift the `WoundHost` exclusion? | (a) lift — plain organic host; (b) keep excluded; (c) make it cybernetic | **(a)** — every component protogen carries is biologically organic (P5-D9); (c) would contradict Hunger/Thirst/Blood/Respirator/`FoodMeatHuman` and cost far more. `roundStart: false`; hosts created are `MobProtogen` + `MobProtogenRandom`; cost is one `HashSet` literal. **Accept with the stated organ gap (U12′)** | WP13-3 |
| **U5** | **A** | Fix the cable coil now? | (a) yes, in WP13-4; (b) leave the leak | **(a)** — live balance leak, one line, matches Onyx's intent. **It is a player-visible nerf (§8.2) and belongs in the changelog.** Sequenced after P5-1 so the dead window is zero | WP13-4 |
| **U1** | B | Do IPCs feel pain? | (a) Onyx-faithful: `canFeelPain` default true **+ `PainShockTarget` on `MobIPC`**; (b) `canFeelPain: false` as a marked deviation | **(a)** — Onyx puts `PainShockTarget` on `BaseSpeciesMob`, which its `MobIpc` inherits, so pain *shock* is Onyx behaviour too. **Caveat: an IPC has no chemical pain relief at all (R4).** If that reads as punishing at playtest, (b) is a one-line reversal | WP13-2 |
| **U16** | B | IPC chemical solution. | (a) `chemicalMaxVolume: 0` and no `InjectableSolution`; (b) Onyx-faithful injectable 250 u that never metabolizes | **(a)** — IPCs have no metabolizer (R4); an injectable solution that does nothing is the same trap U2 exists to avoid | WP13-2 |
| **U17** | B | Diona and slime limbs are destroyed at 190/210 rather than severed, so `amputationThresholds: {}` reads to the player as "limbs explode" rather than "limbs stay on". | (a) accept and say so honestly in §8.1, the guidebook and the changelog; (b) also give `PartSlime`/`PartDiona` their own raised triggers | **(a)** for phase 5 — (b) is two more species-specific `Destructible` edits on top of the two U3′/U15 already add, and the underlying contradiction is the pre-existing R11. T-P5-20 pins the behaviour either way | WP13-1 |
| **U12′** | B | Protogen organ damage. | (a) accept the gap and record it as a deviation; (b) extend the phase-3 organ `parent:` edits to `_Mono/Body/Organs/protogen.yml` | **(a)** — consistent with P3-D7 and with every other non-human organic host already shipped; but it must be a *stated* deviation, which revision 1 omitted | — |
| **U18** | B | Drop PROTO R (the `silicon_base.yml` edit)? | (a) drop; (b) keep as a defensive edit for a hypothetical second silicon humanoid | **(a)** — it is provably a no-op (`MobIPC` replaces the `thresholds` list and is the only descendant) and it would add a tracked upstream file for zero behaviour | WP13-2 |
| **U6** | B | Mirror Onyx's `allowedWoundStages: [Minor]` onto Regenerative Mesh and Medicated Suture? | (a) mirror; (b) leave unrestricted | **(b)** — Wolfgate's versions were already buffed past Onyx's baseline by an earlier Goobstation pass; a stage cap on top is two compounding balance changes | — |
| **U7** | B | Add a `- type: Healing` block to the welder for cybernetic limbs? | (a) yes; (b) defer | **(b)** — the cable coil's `[Electrical]` already overlaps both mechanical profiles, so those limbs are already treatable; adding `Healing` to a tool that also carries `WeldingHealing` risks an `InteractUsing`/`AfterInteract` double-handler | — |
| **U8** | B | P5-4 pain numbness / narcotics? | (a) skip and **close permanently**; (b) implement (Desoxyephedrine only, ~165 LOC) | **(a)** — zero consumers for the second consecutive phase, Onyx's own opioids don't use it, and full parity collides with a pre-Wolfmed same-named class | — |
| **U9** | B | P5-5 mechanical wording? | (a) three analyzer strings + one examine string, defer the seven damage adjectives; (b) all of it; (c) none | **(a)** — the analyzer strings are what a medic reads while choosing a tool; the examine adjectives are flavour, and Onyx itself says "bruises" on a robot | WP13-5 |
| **U10** | B | Skeleton (`MobSkeletonPerson`)? | (a) record as a known exclusion, defer; (b) make it an organic host now | **(a)** — mechanically ready, but "bone fracture" and "bleeding" on an undead skeleton need their own design, and phase 5 already carries four species | WP13-7 |
| **U11** | B | IPC oil regeneration? | (a) keep `BloodRefreshAmount`'s default 1.0 per update (Onyx does not override it); (b) `0`, so an IPC needs a refill | **(a)** for phase 5, revisit in the balance pass. Under U2(a), (b) would mean an IPC cannot recover from a bad leak without help | WP13-2 |
| **U14** | B | `- type: Repairable` on `MobIPC`? | (a) no; (b) yes | **(a)** — it would stack a second `InteractUsingEvent` welder do-after on an entity that already has `WeldingHealable`, for a capability WG already has | — |

### 8.5 Blockers

**None.** The phase-1 blocker recorded in `WOLFMED_STATUS.md` ("Blocked on a shared stage-based metabolizer
for non-organic circulatory streams") is **factually void at this pin** and is struck in WP13-7:
`git -C C:/Users/jzo12/Documents/Wolfmed/onyx grep -n "circulatoryStream" HEAD -- Resources` returns exactly one hit, the declaration
of `Organic` itself; none of Onyx's five `bodyPartProfile`s selects a stream; and in Wolfgate the only two
stream-aware methods with callers are `GetPartStream` and `SetBleedRates`, both of which §2.4 hardens.

The two items CRITIQUE5 rated blockers are real findings but are **not** blockers: both describe
**pre-existing** behaviour that phase 5 neither creates nor worsens (§1.5 C1, R11, R12). What they did
block was revision 1's *reasoning*, and that is fixed here.

### 8.6 Difficulty

**Low-to-Medium overall.** Zero new C# types, zero subscriptions, ~700 lines of copied YAML, 10 marked
upstream lines plus 2 upstream numbers, 2 optional upstream blocks. All the risk is in balance and in the
thirteen traps of §8.3. WP13-1 and WP13-2 are the only packages that need real care; WP13-0, WP13-3 and
WP13-4 are mechanical.

### 8.7 Explicitly out of scope for phase 5

- **The stage-metabolizer rewrite** (DECISIONS P5-2 forbids it). What it would require in WG, all currently
  MISSING: `MetabolismStagePrototype` + a stage-based shared `MetabolizerComponent`; `SolutionManagerComponent`
  + `CreateDefaultSolution`; `SharedSolutionContainerSystem.CirculatoryStreams.cs`
  (`TryCreateCirculatorySolution`/`TryDeleteCirculatorySolution`); `BleedModifierEvent`,
  `MetabolismExclusionEvent`, `StasisBedBuckledComponent`; a shared/predicted `BloodstreamComponent` (Onyx's
  `CirculatoryStreamSystem` is `Content.Shared` and queries `BloodstreamComponent` directly, which cannot
  compile while WG's is server-only); and the five dropped subscriptions plus `Update()`.
  What it would buy: a genuinely second fluid on one body, per-stream bleed-out with its own puddle,
  per-stream metabolism, per-stream reaction isolation, per-part injection, and live stream churn on limb
  swap. **Every one of those paths is gated on a non-primary stream existing, and none does at the pin —
  in Onyx either.** This is not deferred work; it is unused upstream capability, and the status doc should
  say so in those words.
- **Onyx's own surgery system** (D7), **Onyx's `_Onyx/Repairable` + `WelderRepairModesComponent`**, **Onyx's
  cybernetic part/organ prototypes** (duplicate ids — a load crash), **`TransplantCompatibility`**,
  **`RoundstartCybernetics`**, **`InjurableComponent`** (D19), an **Ion/`Electronic` damage type**.
- **Re-tuning `MajorLimb`/`MinorLimb`** and the pure-Blunt severing contradiction (R11) — balance pass.
- **Predicted routing** (D35) — phase 6.
- **The 272-entry locational-armour content pass** (P3-D6) — phase 6.
- **`HurtCommand` part argument** — phase 6 (patch kept at `C:/Users/jzo12/Documents/Wolfmed/plan/wp`).
- **Organ-damage instrumentation for non-human species** (U12′), **skeleton** (U10), **the mechanical
  examine adjective set** (U9), **`allowedWoundStages` balance mirroring** (U6), **a welder `Healing`
  block** (U7), **diona/slime limb triggers** (U17) — all recorded for the balance pass.
- **Pain numbness / narcotics** — **closed**, not deferred (P5-D14).

---

## 9. Work-package table (final)

| WP | Title | Model | Files | New ids | Upstream edits | Subs | Gate |
|---|---|---|---|---|---|---|---|
| WP13-0 | Profile, wound and container prototypes (P5-1a/P5-2a) | sonnet | 2 | 18 | 0 | 0 | YAMLLinter, build, headless |
| WP13-1 | Species part wiring + gib blocks (P5-1b) | **opus** | 5 | 4 | PROTO M/N/O/P | 0 | + `DockTest`, `EntityTest\|PrototypeSaveTest`, headless |
| WP13-2 | IPC as a wound host + circulation (P5-1c/P5-2) | **opus** | 4 | 0 | PROTO Q/S/T | 0 | + headless, spawn `MobIPC` |
| WP13-3 | Protogen exclusion lift (P5-1d) | sonnet | 3 | 0 | PROTO V (comment) + EXT 3/4 (`_WF`) | 0 | + spawn `MobProtogen` and `MobProtogenRandom` |
| WP13-4 | Cable coil `treatmentCapabilities` (P5-3) | sonnet | 1 | 0 | PROTO U | 0 | YAMLLinter, `DockTest` |
| WP13-5 | Mechanical analyzer and examine wording (P5-5) | sonnet | 6 | 4 LocIds | 0 | 0 | build both assemblies, YAMLLinter |
| WP13-6 | Tests (P5-6) | **opus** | 4 | 0 | 0 | 0 | full Wolfmed + smoke filters |
| WP13-7 | Docs, manifest, status (P5-7) | sonnet | 3 | — | — | — | — |

**Totals: 28 files, 22 new prototype/entity ids, 4 new LocIds, 10 marked upstream lines + 2 upstream
numbers + 2 optional upstream blocks, 8 newly-tracked upstream files (47 → 55), 0 new C# types, 0 new
components, 0 new subscription pairs, 20 tests.**

---

## Revision notes (revision 2, applied to CRITIQUE5)

Every critique item was re-verified against the real files before being accepted or rejected. Nothing was
taken on the critique's word, and nothing from revision 1 was left standing that a file contradicted.

### Blockers

| # | Disposition | What changed |
|---|---|---|
| **B1** — cybernetic limbs have gib triggers | **ACCEPTED on fact, NARROWED on severity** | Verified: `CyberneticPartBase` declares no `Destructible` (`_Shitmed/Body/Parts/cybernetic.yml:1-14`, read in full) and every concrete limb's second parent supplies `MajorLimb`'s or `MinorLimb`'s (`:18, :32, :46, :60, :73, :81, :89, :97`). Revision 1's `§2.2` comment is **deleted**; P5-D8b, U15, R12, T-P5-19 and deviation 4 are new. **Narrowed:** (i) this is *pre-existing* — cybernetic limbs already sit on human wound hosts and already gib and ash today, so phase 5 neither creates nor worsens it; (ii) `DismembermentWound` is **not** dead — `WolfmedBaseLeftArm`'s `Slash 130` is well under the Slash rung 210 and `Piercing 250` has **no** rung, so Slash and Piercing severing both work; only *pure Blunt* severing is unreachable, exactly as it already is for flesh. Rated major + mandatory text fix, not blocker. |
| **B2** — "organic parts have no gib trigger" is false | **ACCEPTED in full** | Verified `MajorLimb` (`Body/Parts/base.yml:275-307`) and `MinorLimb` (`:310-341`) and all eight `parent:` lines. P5-D8 rewritten from 400/400 to **190/210**; U3′ restated with four options and the real numbers; T-P5-11 re-derived (Slash 140 severable / Blunt 195 destroyed); deviation 3 rewritten. **Two things the critique missed and this revision adds:** `PartIPCBase` is one block governing *every* IPC slot, so 190/210 makes IPC hands/feet tougher than `MinorLimb`'s 150/180 and leaves the IPC **head** destructible where `BaseHead` (`parent: WolfmedBaseHead` only, `:81-82`) has no rung at all — both stated in P5-D8 and offered as U3′(d). |

### Majors

| # | Disposition | What changed |
|---|---|---|
| **M1** — PROTO R is a no-op | **ACCEPTED in full** | Verified `DestructibleComponent.Thresholds` is a plain `[DataField("thresholds")]` (`:12-13`), that non-`Always` fields keep the child key (`SerializationManager.Composition.cs:196-206`), that `MobIPC` declares its own `Destructible` (`_EE ipc.yml:80-87`), and that `MobIPC` is the only entity parenting `PlayerSiliconHumanoidBase` (3 grep hits: the id, `MobIPC`, a TODO). **PROTO R dropped**; upstream count 56 → **55**; WP13-2 6 files → 4; U18 added. |
| **M2** — diona/slime limbs are destroyed, not merely unseverable | **ACCEPTED in full** | Verified `LeftArmDiona: parent: [PartDiona, BaseLeftArm]` (`diona.yml:33`) and `LeftArmSlime: parent: [PartSlime, BaseLeftArm]` (`slime.yml:51`), and that `AmputationSystem.HandlePartDamageApplied` early-returns on `AmputationThresholds.Count == 0` (`:80`). §8.1 rows rewritten, §8.1a added, U17 and T-P5-20 added, deviation 20 added, the `WolfmedPartDiona` comment corrected. |
| **M3** — `Airloss` group is self-contradicting | **ACCEPTED, severity narrowed** | Verified the group is `Asphyxiation + Bloodloss` (`groups.yml:22-27`) and that `TourniquetSystem.OnDoAfter` applies the cost with `TryApplyPartDamage(body, part, …)` (`:88-89`) — to the part, so revision 1's stated benefit cannot exist. P5-D5b added; §2.3 now uses `supportedTypes: […, Bloodloss]`. **Narrowed:** the practical new exposure is small — `RespiratorSystem` is the only C# writer of `Asphyxiation` damage and `MobIPC` has no `Respirator`; `MobAtmosExposed` deals `Cold`/`Heat` only (`Entities/Mobs/base.yml:147-161`); no metabolizer means no reagent path. Taken anyway: it costs one word. |
| **M4** — `Cold`/`Caustic` is an Onyx-parity loss | **ACCEPTED and strengthened** | Verified Onyx's `SiliconIpc` = Brute + the **`Burn` group** + `Electronic` (`ONYX Corvax/Damage/containers.yml`) and that `Burn` = `Heat, Shock, Cold, Caustic` in both forks. Deviation 11 rewritten as a Wolfgate-only loss; P5-D20 added; U13′ added with a recommendation of (b). **Strengthened:** the critique asserted a part-container fix would work but did not show why. It does, because `DamageableSystem.TryChangeDamage` raises the Wolfmed routing seam at `:253-264` **before** the body's type filter at `:270-277` — so Cold/Caustic reach routing even on a `Silicon` body and it is the *part's* container that discards them. **Shape corrected:** the critique's `supportedTypes: [Shock]` is redundant (`Shock` is inside `Burn`), and the two part bases use *different* stock containers (`Inorganic` for IPC, `Silicon` for cybernetic), so `InorganicWolfmed` must be a superset: `supportedGroups: [Brute, Burn]` + `supportedTypes: [Radiation]`. |
| **M5** — protogen organ gap, census error, stale comment | **ACCEPTED in full** | Verified the seven `parent:` edits (`Body/Organs/human.yml:53,103,150,189,215,249,270`), that every protogen organ parents `BaseProtogenOrgan`/`BaseProtogenOrganUnGibbable` (`_Mono/Body/Organs/protogen.yml`), that `MobProtogenDummy` parents `BaseSpeciesDummy` (`:86-89`) and `MobProtogenRandom` parents `MobProtogen` (`_Goobstation/…/humanoid.yml:225-227`), that `Extractable` is at `_Mono/Body/Parts/protogen.yml:13-19`, and the stale comment at `HealthAnalyzerSystem.Wolfmed.cs:31-34`. P5-D19, deviation 9, EXT 4 and the T-P5-9 organ assertion are new; WP13-3 is now 3 files. **One addition:** `OrganProtogenEars` does parent `BaseHumanOrgan` (`:126-127`) — irrelevant, ears are not one of the seven instrumented organs. |
| **M6** — `Bloodstream` drags in drunk/stutter and a dead 250 u solution | **ACCEPTED, one half narrowed** | Verified the unguarded bloodloss branch (`BloodstreamSystem.cs:141-162`) against GUARD E on `OnDamageChanged` (`:212-215`), `ChemicalMaxVolume = 250` (`BloodstreamComponent.cs:126-127`), the `required: true` on both bloodloss fields (`:69-77`), and `MobBloodstream`'s `InjectableSolution` (`Entities/Mobs/base.yml:236-248`). `chemicalMaxVolume: 0` added to PROTO Q, the `InjectableSolution` omission made explicit, U16 and deviation 15 added. **Narrowed:** drunk + stutter is what *every* organic wound host already gets when bleeding out, so it is Wolfgate-consistent rather than an IPC-specific defect; listed in §8.1 and R4 as flavour, not as a bug. |
| **M7** — T-P5-4 names abstract prototypes | **ACCEPTED in full** | Verified all five abstract ids and all ten concrete ones. `JawsOfLifeLeftArm` is the only concrete cybernetic **arm**; T-P5-4, T-P5-8 and T-P5-19 all use it, and §1.3 row 1 now separates abstract from concrete. |

### Minors

m1–m8 all accepted and folded: ONYX profile span corrected to `37-151` with ids at 38/70/100/124 and the
recovery multipliers at `:40-41` (m1); the three Onyx species-part citations corrected (m2); `MobIPC`'s
`Destructible` to `:80-87` and `silicon_base`'s to `:91-96` (m3); §3.4's `HealthExaminable`/`DamageVisuals`
framing corrected — and now genuinely true, since PROTO R is dropped (m4); the `Extractable` citation
(m5); R8 rewritten with the real reason (`MaxDamage` defaults to 0, `WolfmedBodyPartComponent.cs:16`) (m6);
the "no `maxAffected`, no extra limb rows" trap stated explicitly in §2.1 and WP13-0 (m7); the
Wolfmed-abstract-ordering warning added to P5-D1, §2.2's header comment and WP13-1 (m8).

### Findings this revision adds beyond CRITIQUE5

| # | Finding | Evidence |
|---|---|---|
| **N1** | The prototype counts in §7.1 are off by one at both ends: WG's `wounds.yml` holds **14** prototypes, Onyx's **30**. The row should read "14 of 30" → "30 of 30", not "13 of 29" → "29 of 29". | `grep -n "^  id:"` on both files |
| **N2** | §7.1/§3.3 filed `CirculatoryStreamSystem.cs` under `Content.Shared/`. The file is at `Content.Server/_Onyx/Chemistry/Circulation/`; only its *namespace* is `Content.Shared._Onyx.Chemistry.Circulation`. | file listing |
| **N3** | §7.2 claimed the cable-coil edit "matches Onyx exactly". Only the capability line does — Onyx's coil is `delay: 3.5` with `Blunt/Slash/Piercing/Heat -3`, `Shock -5` and `damageContainers: [SiliconIpc]` (`ONYX cable_coils.yml:178-193`); WG's is `delay: 0.6`, `Heat/Shock/Radiation -3`, `[Silicon]`. Phase 5 changes one line and leaves the rest. | both files |
| **N4** | P5-D3's hardening is a **complete** fix, not a partial one: `TryGetPartSolution` and `TryGetStreamSolution` (`CirculatoryStreamSystem.cs:32-68`) have **zero callers** anywhere in WG, so `GetPartStream` → `SetBleedRates` is the only path a non-primary stream could ever take. | `grep -rn` over `Content.Server`/`Content.Shared` → 3 hits, all in `WoundBleedingSystem.cs:306,319,324` |
| **N5** | Organic **heads** have no gib trigger at all (`BaseHead: parent: WolfmedBaseHead`, `Body/Parts/base.yml:81-82`), but `HeadIPC: parent: [ PartIPCBase, BaseHead ]` takes `PartIPCBase`'s. So any single `PartIPCBase` number leaves the IPC head destructible where a human head is not. Folded into P5-D8, §8.1a and U3′(d). | `_EE ipc.yml:52-54` |
| **N6** | CRITIQUE5's "coin-flip race" between a 150 gib rung and a 150 Blunt amputation threshold is **not a race — the gib always wins**. `AmputationSystem.HandlePartDamageApplied` needs two qualifying hits: the crossing hit only calls `SetSeverable(true)` and returns (`:88-95`); severing happens on a later finishing hit (`:109-112`). `DestructibleSystem` fires on the crossing hit. Generalised into trap 7 and R11. | `AmputationSystem.cs:72-112` |
| **N7** | The IPC **part** container is `Inorganic` (inherited from `BasePartInorganic`, `Body/Parts/base.yml:8-16`), not `Silicon`; only `CyberneticPartBase` overrides to `Silicon` (`cybernetic.yml:10-11`). `InorganicWolfmed` must therefore be a superset of both. | both files |
| **N8** | `Resources/Prototypes/_WF/Wolfmed/Damage/containers.yml` had to move from WP13-2 to **WP13-0**: WP13-1's part edits reference `InorganicWolfmed`, and a dangling `ProtoId<DamageContainerPrototype>` fails YAMLLinter. | package ordering |
| **N9** | WP13-3 and WP13-5 both edit `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs`. Sequential execution makes this safe, but the one-owner-per-file rule needed an explicit note (R13). | WP tables |
| **N10** | `GibPart` always deletes the part — `GibbingSystem.TryGibEntityWithRef` ends in `if (gibType == GibType.Gib) QueueDel(gibbable)` (`:190-192`) and `BodySystem.GibPart` also `QueueDel`s when `gibs.Any()` (`:166-167`). It is gated only on `IsPartRoot` and `part.CanSever` (default `true`). Tests must assert `Deleted(part)`, not a component state (trap 8). | `SharedBodySystem.Body.cs:372-417`, `BodySystem.cs:143-170`, `BodyPartComponent.cs:63` |

### Things CRITIQUE5 cleared that this revision independently re-confirmed

RT first-parent-wins (V-1), the `Chest`/`Groin` fold being mandatory (V-2), the `CyberneticFractureProfile`
fold (V-3), 100 % locale completeness for all 16 prototypes (V-4 — re-extracted from
`wounds.ftl:1-44` and `health-examinable.ftl:38-45`), the `treatmentCapabilities` leak (V-5), PROTO S/T
being mandatory (V-6), the `Repairable` double-do-after (V-7), the void metabolizer blocker (V-8),
zero new subscriptions (V-9), zero name collisions across 22 ids (V-10), nil D2 exposure from PROTO M/N/P
(V-11), no unscheduled wound-host species (V-12), no Mono IPC overrides and no heart-side surprise from
`OrganIPCPump` (V-13), IPC surgery reachability via `SurgeryTarget` on `PlayerSiliconHumanoidBase:319`
(V-14), `Oil`'s flammability and the `Sap`/`Slime` blood reagents (V-15), the wound-shape derivations
(V-16), Onyx's `PainShockTarget` placement (V-17), the analyzer payload's single construction site (V-18),
the D22 projection mechanism (V-19), and the build order (V-20, amended by N8).
