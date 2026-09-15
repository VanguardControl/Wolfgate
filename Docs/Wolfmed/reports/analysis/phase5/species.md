# P5-1 — Species profiles (Wolfmed phase 5)

Analyst report. **Read-only**: nothing in the worktree was modified. Every claim below is cited to a file
and line in the real tree (WG) or to `git -C C:/tmp/onyx show HEAD:<path>` (ONYX, pin `2f5bab9`).

- **WG** = `C:/Users/jzo12/Documents/GitHub/Wolfgate/.claude/worktrees/rules-motd-updates-11c89c`
- **ONYX** = `C:/tmp/onyx` @ `2f5bab9946539cbe083010c9ae6fbc59b47ae377`
- Phases 1–4 committed at `2b4a4675d0`. Binding constraints: `C:/tmp/wolfmed-plan/DECISIONS.md`
  (D2, D3, D4, D6, D8, D9, D32; "Phase 5" section P5-1…P5-7).

---

## §0. Executive summary of findings

1. **Onyx's `wounds.yml` is complete in WG except for the 16 prototypes WP7 deliberately dropped.** The
   phase-1 file is a verbatim copy of the Organic pieces plus three marked edits (D9 Torso fold, D20
   Caustic, §8.2-1 manipulation modifiers). Restoring the five species profiles, the cybernetic fracture
   profile and the ten species wound prototypes is a **pure append** — no re-edit of existing rows.
2. **The phase-1 "blocker" for non-organic circulatory streams does not exist at the pin.** Onyx defines
   exactly one `circulatoryStream` prototype (`Organic`,
   ONYX `Resources/Prototypes/_Onyx/Chemistry/circulatory_streams.yml:1`), *none* of the five
   `bodyPartProfile`s sets `circulatoryStream:`, and the field's C# default is `"Organic"`
   (WG `Content.Shared/_Onyx/Wounds/WoundPrototype.cs:157`). Onyx's IPC bleeds through an ordinary
   `BloodstreamComponent` whose reagent is Oil (ONYX `Resources/Prototypes/Corvax/Body/Species/ipc.yml:102-113`).
   **P5-2 route (a) is what Onyx itself does.** No metabolizer work is needed for P5-1.
3. **Everything the species wound prototypes need already exists in WG.** All five `!type:` behaviour
   classes resolve (§9), all locale keys are already shipped byte-identical (§8), the `bodyPartProfile`
   and `fractureProfile` prototype classes carry every field Onyx's YAML uses (§5.1), and **all 16 new
   prototype ids are unused anywhere in WG** (§10).
4. **P5-1 needs zero new C# and zero new `SubscribeLocalEvent` pairs.** It is YAML plus, if the user
   accepts the protogen recommendation, the deletion of one string literal.
5. **Three real defects the plan must fix, all found by verification, none of them in Onyx:**
   - **IPC limbs gib before they can be severed.** `PartIPCBase` gibs at Blunt 110 / Slash 150
     (WG `Resources/Prototypes/_EinsteinEngines/Body/Parts/ipc.yml:16,22`) while Onyx's IPC amputation
     thresholds are Slash 270 / Piercing 400 / Blunt 600 for an arm. Onyx's numbers are unreachable.
   - **IPC gib/death thresholds are not D22-adjusted.** Body damage on a wound host is the *sum* of every
     part (`WoundDamageProjectionSystem.RefreshBodyDamage`, WG
     `Content.Shared/_Onyx/Wounds/WoundDamageProjectionSystem.cs:155-195`). Organics were raised to
     `damage: 1500` for exactly this reason (WG `Resources/Prototypes/Entities/Mobs/Species/base.yml:271-278`);
     `MobIPC` still gibs at Blunt 400 (`.../ipc.yml:85`) and `PlayerSiliconHumanoidBase` at a total of 500
     (`.../silicon_base.yml:94`).
   - **Cybernetic limbs currently fracture like bone and bleed like flesh.** `CyberneticPartBase` inherits
     `BaseLeftArm` etc., which inherit `WolfmedBaseLeftArm` with `fractureProfile: OrganicFractureProfile`
     (WG `Resources/Prototypes/Body/Parts/base.yml:137`, `_WF/Wolfmed/Body/parts.yml:51-60`), and
     `WoundableComponent` is `EnsureComp`'d with its default `OrganicBodyPartProfile`
     (`WoundDamageProjectionSystem.cs:221` + `WoundDamageComponents.cs:161`). A `BionicLegs` trait holder
     has been walking around since phase 1 with bleeding, painful, bone-fracturing steel legs.
6. **Protogen is not synthetic in any mechanical sense** (`damageContainer: Biological`, `OrganicPart`
   limbs, Blood bloodstream, Hunger/Thirst/Respirator, butchers into `FoodMeatHuman`). The D32 exclusion
   should be **deleted**, not converted to a cybernetic host — see §12 decision D-P5-1-C.
7. **The cable coil is confirmed to be the one real `treatmentCapabilities` gap** (P4-D1/D7): it carries
   `- type: Healing` (WG `Resources/Prototypes/Entities/Objects/Tools/cable_coils.yml:36-45`) whose
   `TreatmentCapabilities` defaults to `[Biological]` (WG
   `Content.Server/Medical/Components/HealingComponent.cs:73`). The moment an IPC limb carries a
   Mechanical-only profile the coil stops working on it. The **welder is not affected** (§6.6).

---

## §1. ONYX `Resources/Prototypes/_Onyx/Wounds/wounds.yml` — full read

File is 1064 lines, 25 prototypes. Index (`git -C C:/tmp/onyx show HEAD:Resources/Prototypes/_Onyx/Wounds/wounds.yml`):

| line | type | id |
|---|---|---|
| 1 | bodyPartProfile | `OrganicBodyPartProfile` |
| 37 | bodyPartProfile | `IpcBodyPartProfile` |
| 69 | bodyPartProfile | `SlimeBodyPartProfile` |
| 99 | bodyPartProfile | `CyberneticBodyPartProfile` |
| 123 | bodyPartProfile | `PlantBodyPartProfile` |
| 153 | fractureProfile | `OrganicFractureProfile` |
| 193 | fractureProfile | `CyberneticFractureProfile` |
| 233 | wound | `BluntWound` |
| 286 | wound | `BoneFractureWound` |
| 337 | wound | `CyberneticFrameFractureWound` |
| 372 | wound | `SlashWound` |
| 421 | wound | `SystemicBleedingWound` |
| 430 | wound | `PiercingWound` |
| 479 | wound | `BurnWound` |
| 526 | wound | `ElectricalWound` |
| 559 | wound | `SurgicalIncisionWound` |
| 570 | wound | `DismembermentWound` |
| 583 | wound | `IpcMechanicalDamageWound` |
| 634 | wound | `CyberneticMechanicalDamageWound` |
| 682/728/770/812 | wound | `SlimeBluntWound` / `SlimeSlashWound` / `SlimePiercingWound` / `SlimeBurnWound` |
| 855/904/949/994 | wound | `PlantBluntWound` / `PlantSlashWound` / `PlantPiercingWound` / `PlantBurnWound` |
| 1041 | wound | `AmputationConsequenceWound` |
| 1048 | wound | `InternalBleedingWound` |
| 1059 | wound | `MedicalScarWound` |

### §1.1 The five `bodyPartProfile`s, every field

Fields omitted from a profile take the C# default in
WG `Content.Shared/_Onyx/Wounds/WoundPrototype.cs:134-183` — shown in the "(default)" rows.

| field | Organic | **Ipc** | **Slime** | **Cybernetic** | **Plant** |
|---|---|---|---|---|---|
| `treatmentCapabilities` | `[Biological]` | `[Mechanical, Electrical]` | `[Biological]` | `[Mechanical, Electrical]` | `[Biological]` |
| `bleedingMultiplier` | `1.0` | `1` | `1.15` | `0.5` | `1` |
| `scarrable` | (default `true`) | `false` | `false` | `false` | (default `true`) |
| `canFeelPain` | (default `true`) | (default **`true`**) | (default `true`) | **`false`** | (default `true`) |
| `passiveRecoveryMultiplier` | (default `1`) | `0` | (default `1`) | `0` | (default `1`) |
| `bedRecoveryMultiplier` | (default `1`) | `0` | (default `1`) | `0` | (default `1`) |
| `circulatoryStream` | (default `"Organic"`) | (default `"Organic"`) | (default `"Organic"`) | (default `"Organic"`) | (default `"Organic"`) |
| `acceptedDamageTypes` | Blunt, Slash, Piercing, Heat, Cold, Shock, Caustic | *identical 7* | *identical 7* | *identical 7* | *identical 7* |
| `organDamage.chances` | Head .05, Chest .04, Groin .04, Arm .02, Hand .01, Leg .02, Foot .01 | *identical 7 rows* | Head .05, Chest .04, Groin .04 | **absent** | Head .05, Chest .04, Groin .04 |
| `organDamage.maxAffected` | `2` | (default `1`) | (default `1`) | (n/a) | (default `1`) |

**`supportedWounds` per profile** (exact, in file order):

- **Organic** (12): `BluntWound, SlashWound, PiercingWound, BurnWound, ElectricalWound, BoneFractureWound,
  SurgicalIncisionWound, DismembermentWound, AmputationConsequenceWound, InternalBleedingWound,
  MedicalScarWound, SystemicBleedingWound`
- **Ipc** (6): `IpcMechanicalDamageWound, ElectricalWound, SurgicalIncisionWound, DismembermentWound,
  AmputationConsequenceWound, SystemicBleedingWound`
- **Slime** (10): `SlimeBluntWound, SlimeSlashWound, SlimePiercingWound, SlimeBurnWound, ElectricalWound,
  SurgicalIncisionWound, DismembermentWound, AmputationConsequenceWound, InternalBleedingWound,
  SystemicBleedingWound`
- **Cybernetic** (6): `CyberneticMechanicalDamageWound, ElectricalWound, CyberneticFrameFractureWound,
  SurgicalIncisionWound, DismembermentWound, AmputationConsequenceWound` — **no `SystemicBleedingWound`,
  no `InternalBleedingWound`, no `MedicalScarWound`.**
- **Plant** (12): `PlantBluntWound, PlantSlashWound, PlantPiercingWound, PlantBurnWound, ElectricalWound,
  SurgicalIncisionWound, DismembermentWound, AmputationConsequenceWound, InternalBleedingWound,
  MedicalScarWound, SystemicBleedingWound`

Things worth noticing, because they contradict the intuitive reading:

- **Onyx IPCs feel pain.** `IpcBodyPartProfile` never sets `canFeelPain: false`; only the *Cybernetic*
  profile does. `IpcMechanicalDamageWound` even carries `!type:WoundPainBehavior painPerSeverity: 0.87`
  (ONYX wounds.yml:606-608). Only a *bolted-on cybernetic limb* is numb.
- **Onyx IPCs bleed.** `bleedingMultiplier: 1` (full rate) and `IpcMechanicalDamageWound` has a
  base-level `!type:WoundBleedingBehavior rate: 0.08 chance: 1` — i.e. **every** chassis wound leaks,
  unlike organic wounds which only bleed above a `minimumSeverity` and sometimes on a chance roll.
- **The Cybernetic profile is the only one with `scarrable: false` AND `canFeelPain: false` AND no
  bleeding-capable wound list beyond `DismembermentWound`/`SurgicalIncisionWound`.**
- **Slime is `scarrable: false` but still bleeds hardest** (`1.15`).
- **Plant is the odd one out**: identical to Organic except the wound set and that it has no
  `BoneFractureWound`; it is still scarrable and still passively recovers.

### §1.2 The two `fractureProfile`s

`OrganicFractureProfile` (ONYX :153-191) and `CyberneticFractureProfile` (ONYX :193-231) are **field-for-field
identical except for two lines**:

```yaml
  id: CyberneticFractureProfile        #  vs  OrganicFractureProfile
  wound: CyberneticFrameFractureWound  #  vs  BoneFractureWound
```

Everything else is the same in both: `damageType: Blunt`, `severityMultiplier: 1`,
`resetTreatmentOnDamage: true`, `worsenMinimumDamage: 5`, `minimumHitDamage: 3`,
`accumulationMultiplier: 0.4`, `reductionMinimumGrade: Simple`, `removeWoundWhenMended: true`,
`alert: BrokenBones`, `alertMinimumGrade: Simple`, `alertHiddenTreatments: [Mended]`,
`treatmentEffectScales: {None: 1, Reduced: 0.25, Mended: 0}`, and the four grades
`Hairline 20 / 0.05 / 0.625 / 0.92`, `Simple 35 / 0.25 / 0.5 / 0.84`,
`Displaced 50 / 0.65 / 0 / 0.75`, `Comminuted 60 / 1 / 0 / 0.75`.

> **Consequence for the port:** the phase-2 §8.2-1 correction (manipulation modifiers inverted; the four
> values replaced with the C# defaults `1.1 / 1.25 / 1.5 / 2.0`, WG `Resources/Prototypes/_Onyx/Wounds/wounds.yml:57-78`)
> is a *bug fix in the shared formula*, so it must be applied to `CyberneticFractureProfile` too. Shipping
> Onyx's raw `0.92 / 0.84 / 0.75 / 0.75` on the cybernetic profile would make a shattered cybernetic arm
> do-after **25 % faster** — exactly the bug the user already ruled FIX on.

### §1.3 The species wound prototypes

**`IpcMechanicalDamageWound`** (ONYX :583-632) and **`CyberneticMechanicalDamageWound`** (ONYX :634-680) are
identical except that the cybernetic one has **no `WoundPainBehavior`**:

```yaml
  damageTypes:
    Blunt:   { reopenMinimumDamage: 18, severityMultiplier: 1 }
    Slash:   { reopenMinimumDamage: 15 }                       # severityMultiplier defaults to 1
    Piercing:{ reopenMinimumDamage: 18, severityMultiplier: 1 }
    Heat:    { reopenMinimumDamage: 15 }
    Cold:    { reopenMinimumDamage: 18, severityMultiplier: 1 }
    Caustic: { reopenMinimumDamage: 12, severityMultiplier: 1 }
  maximumSeverity: 200
  behaviors:
  - !type:WoundBleedingBehavior { rate: 0.08, chance: 1 }
  - !type:WoundPainBehavior { painPerSeverity: 0.87, minSeverity: 10 }   # Ipc ONLY
  stages:
    Minor    severity 0   name wound-stage-mechanical-minor    (no behaviors)
    Moderate severity 25  name wound-stage-mechanical-moderate  Functionality: Impaired
    Severe   severity 50  name wound-stage-mechanical-severe    Functionality: Disabled
    Critical severity 80  name wound-stage-mechanical-critical  Functionality: Disabled
```

Note: **one wound covers six damage types** (Blunt/Slash/Piercing/Heat/Cold/Caustic). There is no separate
mechanical burn wound; a laser and a crowbar both add to the same "chassis damage" pool. Shock goes to the
shared `ElectricalWound` instead.

**`CyberneticFrameFractureWound`** (ONYX :337-370) mirrors `BoneFractureWound` with `damageTypes: {}` and
`maximumSeverity: 200`, four stages at severity 20/35/50/60 named
`wound-stage-frame-{hairline,simple,displaced,comminuted}` with `examineDescription:
wound-examine-frame-*`, and behaviours `Functionality: Impaired` (Hairline) then `Disabled` (the rest).
**It carries no `WoundPainBehavior` at all**, where `BoneFractureWound` carries one per stage
(`painPerSeverity 0.35 / 0.5 / 0.65 / 0.8, minSeverity 15, oneTime: true`).

**Slime set** (ONYX :682-853). Four wounds, one per organic analogue, differing from the organic versions
in exactly two ways: **no `WoundScarBehavior`** (consistent with `scarrable: false`) and **no
`minimumSeverity` on any `WoundBleedingBehavior`** — a slime bleeds from the very first point of severity
where a human needs severity ≥ 8–9.

| | `SlimeBluntWound` | `SlimeSlashWound` | `SlimePiercingWound` | `SlimeBurnWound` |
|---|---|---|---|---|
| damage types | Blunt (reopen 18) | Slash (reopen 15) | Piercing (reopen 18) | Heat 15 / Cold 18 / Caustic 12 |
| base pain | `0.7`, minSev 10 | `0.67`, minSev 10 | `0.67`, minSev 10 | none (per-stage `0.8`, minSev 10 ×4) |
| scar | **none** | **none** | **none** | **none** |
| bleed by stage | .05 (ch .25) / .08 (ch .35) / .12 (ch .5) / .18 (ch .7) | .1 / .15 / .2 / .3 | .15 / .2 / .25 / .35 | none |
| functionality | Impaired @50, Disabled @80 | Impaired @50, Disabled @80 | Impaired @50, Disabled @80 | **none** |

(Compare `SlimeBluntWound` base pain `0.7` against `BluntWound`'s `0.87` — slime hurts slightly less.)

**Plant set** (ONYX :855-1039). Four wounds identical to the *organic* originals in every number,
**including the `WoundScarBehavior`** (`PlantBlunt` threshold 20, `PlantSlash`/`PlantPiercing` threshold 15,
`PlantBurn` threshold 20) and including `BluntWound`'s `painPerSeverity: 0.87`. The only differences from
the organic set are the `name:` LocIds and the absence of `minimumSeverity` on the bleeding behaviours —
same change as the slime set. `PlantBurnWound` is the only one of the eight species burn wounds that keeps
a scar behaviour.

---

## §2. WG's phase-1 copy — what was trimmed and D9-edited

`WG/Resources/Prototypes/_Onyx/Wounds/wounds.yml` is 420 lines, 13 prototypes. Verified by a
per-prototype diff against ONYX (script: split on `- type:`, match by `id:`, unified diff).

**Result: exactly three content deltas and sixteen missing prototypes. Nothing else differs.**

| delta | where | what |
|---|---|---|
| trim | WG `:1` | `# WOLFGATE (WP7): Ipc/Slime/Plant/Cybernetic profiles and their wounds are dropped for phase 1 (D3).` |
| D9 | WG `:29-31` | `OrganicBodyPartProfile.organDamage.chances`: Onyx's `Chest: 0.04` + `Groin: 0.04` folded to a single `Torso: 0.04` (not summed) |
| D20 | WG `:13` | `Caustic` kept in `acceptedDamageTypes` with a trailing marked comment (D20 reversed) |
| D20 | WG `:302` | `BurnWound.damageTypes.Caustic` kept with a trailing marked comment |
| §8.2-1 | WG `:57-78` | `OrganicFractureProfile` gains a 4-line `# WOLFGATE` block and the four `manipulationModifier`s become `1.1 / 1.25 / 1.5 / 2.0` |

**Missing from WG (all 16, the exact P5-1 payload):**
`IpcBodyPartProfile`, `SlimeBodyPartProfile`, `CyberneticBodyPartProfile`, `PlantBodyPartProfile`,
`CyberneticFractureProfile`, `IpcMechanicalDamageWound`, `CyberneticMechanicalDamageWound`,
`CyberneticFrameFractureWound`, `SlimeBluntWound`, `SlimeSlashWound`, `SlimePiercingWound`,
`SlimeBurnWound`, `PlantBluntWound`, `PlantSlashWound`, `PlantPiercingWound`, `PlantBurnWound`.

The twelve retained wound prototypes (`BluntWound`, `BoneFractureWound`, `SlashWound`,
`SystemicBleedingWound`, `PiercingWound`, `BurnWound`, `ElectricalWound`, `SurgicalIncisionWound`,
`DismembermentWound`, `AmputationConsequenceWound`, `InternalBleedingWound`, `MedicalScarWound`) are
**byte-identical to Onyx** apart from the one `BurnWound` Caustic comment.

---

## §3. How ONYX attaches non-organic profiles

Onyx never puts a profile on a mob. It puts it on the **part entity prototype**, with two components:

```yaml
  - type: Woundable
    profile: <XBodyPartProfile>
  - type: BodyPart
    fractureProfile: <YFractureProfile> | null
    amputationThresholds: {...} | {}
```

`WoundHostComponent` lives on the mob. Evidence, per species:

**IPC** — ONYX `Resources/Prototypes/Corvax/Body/Species/ipc.yml`
- `:97-113` `MobIpc` (parent `[AppearanceIpc, BaseSpeciesMob, MobBloodstream]`) gets
  `- type: WoundHost` (bare, no field overrides) and a Bloodstream:
  ```yaml
  - type: WoundHost
  - type: Bloodstream
    bloodReferenceSolution:
      reagents:
        - ReagentId: Oil
          Quantity: 250
    bloodlossDamage:      { types: { Bloodloss: 0.5 } }
    bloodlossHealDamage:  { types: { Bloodloss: -1 } }
  ```
- `:236-249` `- type: Repairable  treatmentCapabilities: [Mechanical]  fuelCost: 5  doAfterDelay: 3
  selfRepairPenalty: 2` with `damage: {Blunt: -8, Slash: -6, Piercing: -5, Heat: -7, Cold: -6, Caustic: -4}`.
- `:320-340` `OrganIpcExternal` (abstract, the shared external-part base) carries
  `- type: Woundable  profile: IpcBodyPartProfile` and `- type: BodyPart  fractureProfile: null`.
- `:355-446` per-limb `amputationThresholds`:
  Head `Slash 420 / Piercing 360 / Blunt 840`; Arm `270 / 400 / 600`; Hand `150 / 270 / 360`;
  Leg `330 / 470 / 520`; Foot `180 / 300 / 220`.
- ONYX `Resources/Prototypes/_Onyx/Body/chest_groin.yml:350-366` `OrganIpcChest`: `fractureProfile: null`
  only (inherits the chest default); `:367-387` `OrganIpcGroin` adds `Slash 480 / Piercing 450 / Blunt 960`.
- `:449-459` the IPC brain is an `OrganBaseBrain` + `TorsoOrgan`; `:461-481` the eyes carry
  `- type: MechanicalOrgan`.

**Slime** — ONYX `Resources/Prototypes/Body/Species/slime.yml:230-243`
```yaml
- type: entity
  parent: OrganSlimePerson
  id: OrganSlimePersonExternal
  abstract: true
  components:
  # <Onyx-SlimeSurgery>
  - type: Woundable
    profile: SlimeBodyPartProfile
  - type: BodyPart
    fractureProfile: null
  # </Onyx-SlimeSurgery>
```
Note: **no `amputationThresholds` override** — slime limbs keep the organic thresholds and can be severed.
Torso and groin *do* get `amputationThresholds: {}` (ONYX `chest_groin.yml:158-186`).
`MobSlimePerson` inherits `WoundHost` from `BaseSpeciesMobOrganic`
(ONYX `Resources/Prototypes/Body/species_base.yml:211-265`); its bloodstream is
`bloodReferenceSolution: [Slime 600]` (ONYX `slime.yml:162-166`).

**Diona (Plant)** — ONYX `Resources/Prototypes/Body/Species/diona.yml:224-236`
```yaml
- type: entity
  parent: [ OrganDiona, OrganDionaVisual ]
  id: OrganDionaExternal
  abstract: true
  components:
  # <Onyx-PlantMaterial>
  - type: Woundable
    profile: PlantBodyPartProfile
  - type: BodyPart
    fractureProfile: null
    amputationThresholds: {}
  # </Onyx-PlantMaterial>
```
**Diona limbs cannot be severed at all** (`amputationThresholds: {}`). Chest and groin likewise
(ONYX `chest_groin.yml:187-215`). Bloodstream is `[Sap 600]` (ONYX `diona.yml:111-115`).

**Cybernetic limbs** — ONYX `Resources/Prototypes/_Onyx/Entities/Mobs/Customization/Parts/cybernetic.yml:1-21`
```yaml
- type: entity
  abstract: true
  id: OnyxCyberneticPartBase
  components:
  - type: Cybernetics
  - type: Woundable
    profile: CyberneticBodyPartProfile
  - type: BodyPart
    fractureProfile: CyberneticFractureProfile
  - type: TransplantCompatibility
    profile: Cybernetic
  - type: Damageable
  - type: Injurable
    damageContainer: SiliconIpc
  - type: RepairableBodyPart
```
Every concrete cybernetic limb (`LeftArmCybernetic` … `RightFootCybernetic`, and the WardTakahashi /
JawsOfLife / Speed variants) **re-states `fractureProfile: CyberneticFractureProfile`** and carries the
**same amputation thresholds as the IPC limb of the same type** (Arm 270/400/600, Hand 150/270/360,
Leg 330/470/520, Foot 180/300/220 — ONYX `:28-33, 74-79, 118-123, 168-173`).
Cybernetic **organs** (`.../Organs/cybernetic.yml:1-13`) get `- type: Organ  destructionWound: null
destructionWoundSeverity: 0` — i.e. a destroyed cybernetic organ leaves no internal bleeding.

**Animals** get `WoundHost` on `BaseMobAnimal` (ONYX `Resources/Prototypes/Body/Animals/animal.yml:51`)
with the organic profile; `_Onyx/Body/Parts/animal.yml:61` gives the *slime NPC* the `Slime`
TransplantCompatibility profile only — not a woundable profile. Out of P5-1 scope.

> **Terminology trap:** `profile:` appears under *two different components* in Onyx.
> `- type: TransplantCompatibility  profile: Mechanical|Slime|Plant|Flesh|Cybernetic|Xenomorph|Biosynthetic`
> is **not** a wound profile; it is Onyx's transplant-matching system, which Wolfgate does not have.
> Only `- type: Woundable  profile: ...` names a `bodyPartProfile`.

---

## §4. The Wolfgate side

### §4.1 Species census — who is and is not a wound host today

28 `- type: species` prototypes. Ancestry computed from a full parse of
`Resources/Prototypes/**/*.yml` (18 986 entity prototypes).

**Wound hosts today (26 species, via `BaseMobSpeciesOrganic`
→ `Resources/Prototypes/Entities/Mobs/Species/base.yml:252 - type: WoundHost`):**
Human, Dwarf, Felinid, Oni, Harpy, Resomi, Goblin (all via `BaseMobHuman`), Arachnid, Chitinid, Moth,
Reptilian, Vox, Rodentia, Vulpkanin, Tajaran, Yowie, Asakim, Feroxi, Hydrakin, Gingerbread,
**Diona**, **SlimePerson**. 106 entity prototypes descend from `BaseMobSpeciesOrganic`.

**NOT wound hosts (the P5-1 target list):**

| species | mob | base | why not | file |
|---|---|---|---|---|
| **IPC** (`roundStart: true`) | `MobIPC` | `PlayerSiliconHumanoidBase` → `[BaseMob, MobDamageable, MobCombat, MobAtmosExposed, MobFlammable]` | never inherits `BaseMobSpeciesOrganic`, so never gets `WoundHost` | `_EinsteinEngines/Entities/Mobs/Player/ipc.yml:1-3`, `.../silicon_base.yml:1-6` |
| **Protogen** (`roundStart: false`) | `MobProtogen`, `MobProtogenRandom` | `BaseMobProtogen` → `BaseMobSpeciesOrganic` | inherits `WoundHost` then has it **stripped at runtime** | `_Mono/Entities/Mobs/Species/protogen.yml:1-5`; `Content.Shared/_WF/Wolfmed/Body/WolfmedWoundHostExclusionSystem.cs:12` |
| **Skeleton** (`roundStart: false`) | `MobSkeletonPerson` | `BaseMobSkeletonPerson` → `[MobFlammable, BaseMobSpecies]` | parents plain `BaseMobSpecies`, not the Organic variant | `Entities/Mobs/Species/skeleton.yml:1-7` |
| **Cyborg** | `BorgChassisSelectable` | borg chassis | not a humanoid body at all — out of scope | `_Shitmed/Species/cyborg.yml` |
| **Monkey / Kobold** | `MobMonkey`, `MobKobold` | `MobBaseAncestor` → `SimpleMobBase` | NPC animal line; never had `WoundHost` | `Entities/Mobs/NPCs/animals.yml` |

`WolfmedWoundHostExclusionSystem.cs:12`:
```csharp
private static readonly HashSet<string> ExcludedAncestors = new() { "BaseMobProtogen" };
```
This is the **only** exclusion. Skeleton, IPC, borgs and animals were never included in the first place.

> **Skeleton is a gap nobody has recorded.** It is a playable (ghost-role / antag) species on
> `damageContainer: Biological` with `damageModifierSet: Skeleton`, a `Skeleton` body prototype and normal
> `BasePart`-derived limbs — i.e. it would work as an organic wound host with zero data, but it is silently
> outside the system because of one missing parent. Flagged in §12 as decision **D-P5-1-E**.

### §4.2 Part / organ trees of the four target species

All four use the same Shitmed shape: an abstract species part base, then one concrete entity per slot
whose parent list is `[<species base>, Base<Slot>]`.

| species | abstract part base | its parents | damage container | concrete example |
|---|---|---|---|---|
| Diona | `PartDiona` (`Body/Parts/diona.yml:2-4`) | `[BaseItem, BasePart]` | `OrganicPart` (via `BasePart`, `_Shitmed/Body/Parts/base.yml:4-8`) | `LeftArmDiona: parent: [PartDiona, BaseLeftArm]` |
| Slime | `PartSlime` (`Body/Parts/slime.yml:3-4`) | `[BaseItem, BasePart]` | `OrganicPart` | `LeftArmSlime: parent: [PartSlime, BaseLeftArm]` |
| Protogen | `PartProtogen` (`_Mono/Body/Parts/protogen.yml:4-5`) | `[BaseItem, BasePart]` | `OrganicPart` | `TorsoProtogen: parent: [PartProtogen, BaseTorso]` |
| IPC | `PartIPCBase` (`_EinsteinEngines/Body/Parts/ipc.yml:3-4`) | `BasePartInorganic` | **`Inorganic`** (`Body/Parts/base.yml:15-16`) | `LeftArmIPC: parent: [PartIPCBase, BaseLeftArm]` |
| Cybernetic | `CyberneticPartBase` (`_Shitmed/Body/Parts/cybernetic.yml:3-4`) | `BasePartInorganic`, then `- type: Damageable damageContainer: Silicon` at `:10-11` | **`Silicon`** | `LeftArmCyberneticBase: parent: [CyberneticPartBase, BaseLeftArm]` |

**Damage containers** (`Resources/Prototypes/Damage/containers.yml`, groups from `Damage/groups.yml`):

| container | line | supported types |
|---|---|---|
| `OrganicPart` | `:79-82` | Brute + Burn = Blunt, Slash, Piercing, Heat, Shock, Cold, Caustic (7) |
| `Inorganic` | `:11-16` | Brute + Heat + Shock = Blunt, Slash, Piercing, Heat, Shock (5) — **no Cold, no Caustic** |
| `Silicon` | `:28-34` | Brute + Heat + Shock + Radiation (6) — **no Cold, no Caustic** |

**Organs.** Phase 3 annotated seven *human-lineage* organs only (§8.6-7): `Resources/Prototypes/Body/Organs/human.yml`
lines 53, 103, 150, 189, 215, 249, 270 each gained `WolfmedOrgan*` in the parent list
(`_WF/Wolfmed/Body/organs.yml`). Diona organs descend from `BaseDionaOrgan: parent: BaseItem`
(`Body/Organs/diona.yml:2-3`); slime has `SentientSlimeCore: parent: BaseItem` and `OrganSlimeLungs:
parent: BaseHumanOrgan` (`Body/Organs/slime.yml:2-3, 46-47`); protogen has
`BaseProtogenOrganUnGibbable: parent: BaseItem` (`_Mono/Body/Organs/protogen.yml:2-3`); IPC has
`BaseIPCOrgan: parent: BaseItem` (`_EinsteinEngines/Body/Organs/ipc.yml:1-3`).
**None of them carry `OrganDamageComponent`**, and `OrganDamageSystem` requires it
(`Content.Server/_Onyx/Wounds/OrganDamageSystem.cs:53,65`,
`HasComp<OrganDamageComponent>(organ.Id)`). So **porting `organDamage.chances` on the Ipc/Slime/Plant
profiles is a guaranteed no-op** until organs are annotated — consistent with §8.6-7, and to be recorded.

**One live exception:** `BaseCyberneticEyes: parent: OrganHumanEyes`
(`_Shitmed/Body/Organs/cybernetic.yml:1-4`), and `OrganHumanEyes` now parents `WolfmedOrganEyes`
(`Body/Organs/human.yml:103`). So **cybernetic eyes already take organ damage and already leave no
destruction wound** (WolfmedOrganEyes declares none) — accidentally matching Onyx's
`destructionWound: null`. No action needed; worth one manifest line.

### §4.3 IPC today: how "bleeding" and repair work

- **No bloodstream at all.** `PlayerSiliconHumanoidBase` has the entire block commented out at
  `_EinsteinEngines/Entities/Mobs/Player/silicon_base.yml:155-171`, with the upstream author's own
  suggested values: `damageBleedModifiers: BloodlossIPC`, `bloodReagent: Oil`, `bleedReductionAmount: 0`,
  `bloodMaxVolume: 500`, `chemicalMaxVolume: 0`, `bleedPuddleThreshold: 3`, `bloodLossThreshold: 0`,
  `maxBleedAmount: 14`, `bloodlossDamage: {Burn: 1.5}`, `bloodlossHealDamage: {Burn: 0}`.
  `BloodlossIPC` **does not exist** in WG — the only bloodloss modifier set is `BloodlossHuman`
  (`Resources/Prototypes/Damage/modifier_sets.yml:248`).
- **The "pump" is purely decorative.** `OrganIPCPump` (`_EinsteinEngines/Body/Organs/ipc.yml:55-70`) is
  `- type: Heart` with a 10 u Oil `organ` solution and a **commented-out** `Metabolizer`. It circulates
  nothing; there is no bloodstream for it to drive.
- **Repair is welder-only.** `MobIPC` carries `- type: WeldingHealable` (`.../ipc.yml:129`).
  `WeldingHealableSystem.OnRepairFinished` (`Content.Server/_EinsteinEngines/Silicon/WeldingHealable/WeldingHealableSystem.cs:41`)
  calls `_damageableSystem.TryChangeDamage(uid, component.Damage, true, false, origin: args.User)` on the
  **mob**, gated on the mob's `DamageContainerID` ∈ the tool's `damageContainers`.
  Welder: `Silicon`, `Blunt/Piercing/Slash −25`, `fuelCost: 5` (`Entities/Objects/Tools/welders.yml:117-125`).
  Mono nanite applicator: `Silicon`, `Blunt/Piercing/Slash −12.5`, `Heat/Shock/Radiation −9`
  (`_Mono/Entities/Objects/Tools/nanite_applicator.yml:47-59`).
  Cable coil: `- type: Healing  delay: 0.6  damageContainers: [Silicon]  damage: {Heat −3, Shock −3,
  Radiation −3}` (`Entities/Objects/Tools/cable_coils.yml:36-45`).
- **Thresholds today:** `MobIPC` `MobThresholds {0: Alive, 100: Dead}` (`.../ipc.yml:70-74`), no crit;
  `Destructible` `DamageTypeTrigger Blunt 400 → GibBehavior` (`:80-87`);
  `PlayerSiliconHumanoidBase` `MobThresholds {0: Alive, 165: Dead}` (`:87-90`) and
  `Destructible !type:DamageTrigger damage: 500 → GibBehavior` (`:91-95`).
- IPC has `Targeting` + `SurgeryTarget` (`.../silicon_base.yml` near `:239`) — Shitmed surgery already
  works on IPCs, so the phase-4 wound surgeries will reach them.
- IPC `HealthExaminable` lists `Blunt, Slash, Piercing, Heat, Shock`.
- IPC `DamageVisuals`: `thresholds [10,20,30,50,70,100]`, `targetLayers` = Chest/Head/LArm/LLeg/RArm/RLeg,
  `damageOverlayGroups: Brute` only, colour `#DD8822`. The phase-3 per-limb hook
  (`Content.Client/_WF/Wolfmed/Damage/DamageVisualsSystem.Wolfmed.cs:32-50`) drives exactly those
  `TargetLayerMapKeys`, so **IPCs get per-limb damage sprites for free** the moment they become hosts.
- IPC has **no `PainShockTarget`** and **no `EmoteOnDamage`** (both live on `BaseMobSpeciesOrganic`
  `:256, 262-270`), so even if IPC parts feel pain there would be no pain shock and no scream emotes.

### §4.4 Cybernetic limbs today, and who has them

`_Shitmed/Body/Parts/cybernetic.yml` defines `CyberneticPartBase` plus 8 abstract/concrete limbs and 6
special variants (`JawsOfLifeLeftArm`, `JawsOfLifeRightArm`, `SpeedLeftLeg`, `SpeedRightLeg`,
`DexLeftHand`, `DexRightHand`). They reach players three ways:

1. **Roundstart traits.** `PrybarProsthetics` (`_Mono/Traits/physical.yml:2-8`) → `PrybarProstheticsSystem`
   replaces both arms with JWL arms on spawn; `BionicLegs` (`:116-122`) → `BionicLegsSystem.ReplaceLegs`
   spawns `SpeedLeftLeg` / `SpeedRightLeg` (`Content.Server/_Mono/Traits/Physical/BionicLegsSystem.cs:53,60`).
   **These are organic humans wearing steel limbs at roundstart.**
2. **Lathe + research.** `SMCybernetics` pack (`Recipes/Lathes/Packs/robotics.yml:28-37`) and
   `CyberneticEnhancements` tech (`Research/civilianservices.yml:187-202`).
3. **Goob autosurgeon** (`_Goobstation/Entities/Objects/Devices/autosurgeon.yml:75 newPartProto: SpeedLeftLeg`).

`LeftArmCyberneticBase` also has `- type: GenerateChildPart  id: LeftHandCybernetic`, so an arm brings a
matching hand.

> **Id collision note:** Onyx's cybernetic file declares the *same ids* (`LeftArmCybernetic`,
> `RightFootCybernetic`, `JawsOfLifeLeftArm`, `SpeedLeftLeg`, `DexLeftHand`…). **Do not port Onyx's
> `_Onyx/Entities/Mobs/Customization/Parts/cybernetic.yml`** — it would be a duplicate-id crash. Annotate
> Wolfgate's own file instead.

### §4.5 How `WolfmedBodyPartComponent` data is assigned today

`Resources/Prototypes/_WF/Wolfmed/Body/parts.yml` declares ten abstracts —
`WolfmedBaseTorso`, `WolfmedBaseHead`, `WolfmedBase{Left,Right}{Arm,Hand,Leg,Foot}` — each holding
`- type: WolfmedBodyPart` with `fractureProfile: OrganicFractureProfile`, `maxDamage` (torso only, 250),
`amputationThresholds` (Onyx's organic numbers, verified identical to ONYX `base_organs.yml:96-99,146-149,
189-192,231-234,275-278` and `_Onyx/Body/chest_groin.yml`) and the P3-balance `Heat` rows +
`dismembermentFinishingDamage: {Piercing: 12, Heat: 15}`.

They are wired in by **one-line marked `parent:` edits on the upstream abstracts**, because RT's
`ComponentRegistrySerializer` throws "Duplicate ID" if a second file redeclares an id:

- `Resources/Prototypes/Body/Parts/base.yml:81` `parent: WolfmedBaseHead # WOLFGATE (WP7, D8): adds Wolfmed fracture/amputation data`
- `:137, :156, :176, :191, :206, :226, :246, :261` — `parent: [MajorLimb, WolfmedBaseLeftArm] # Mono, WOLFGATE (WP7, D8)` and the seven siblings
- `Resources/Prototypes/_Shitmed/Body/Parts/base.yml:50` `parent: [BaseTorsoInorganic, WolfmedBaseTorso] # WOLFGATE (WP7, D8)`

`WoundableComponent` is **never declared in YAML anywhere in WG** (`grep -rn "Woundable" Resources/Prototypes`
returns only `_DV/Body/Parts/feroxi.yml:17 - type: WoundableVisuals`, an unrelated DV component). It is
added by code: `WoundDamageProjectionSystem.SetupPart` at
`Content.Shared/_Onyx/Wounds/WoundDamageProjectionSystem.cs:219-229`:
```csharp
private void SetupPart(EntityUid part)
{
    EnsureComp<WoundableComponent>(part);
    EnsureComp<DamageableComponent>(part);
    if (_pain.CanFeelPain(part)) EnsureComp<PainComponent>(part); else RemComp<PainComponent>(part);
    EnsureComp<BodyPartFunctionalityComponent>(part);
```
with `WoundableComponent.Profile` defaulting to `"OrganicBodyPartProfile"`
(`Content.Shared/_Onyx/Wounds/WoundDamageComponents.cs:161`). **`EnsureComp` keeps a
prototype-declared component**, so `- type: Woundable  profile: X` in YAML is exactly the right lever —
and it is why every non-organic limb is silently organic today.

---

## §5. Mechanism verification — what a profile actually does in WG

Everything below was read in the real WG files; no Onyx behaviour was assumed.

### §5.1 The prototype classes carry every field Onyx's YAML uses

`Content.Shared/_Onyx/Wounds/WoundPrototype.cs:134-183` `BodyPartProfilePrototype`:
`AcceptedDamageTypes`, `SupportedWounds`, `TreatmentCapabilities` (`= [TreatmentCapability.Biological]`),
`CirculatoryStream` (`= "Organic"`), `BleedingMultiplier` (`= 1f`), `Scarrable` (`= true`),
`CanFeelPain` (`= true`), `PassiveRecoveryMultiplier` (`= 1f`), `BedRecoveryMultiplier` (`= 1f`),
`OrganDamage` (`OrganDamageRouting { Chances, MaxAffected = 1 }`).
`:203-208` `enum TreatmentCapability : byte { Biological, Mechanical, Electrical }`.
`:210-262` `FractureProfilePrototype` with every Onyx field.
**No field in any of the five profiles or two fracture profiles is missing.** `localizedDamageTypes` is
**not** a profile field — it lives on `WoundHostComponent.LocalizedDamageTypes`
(`WoundDamageComponents.cs:35-44`, default the same 7 types).

### §5.2 Where each profile field is read

| field | reader | effect |
|---|---|---|
| `AcceptedDamageTypes` | `WoundDamageRoutingSystem.FilterPartDamage:1053`, `CanPartReceiveDamage:1082`, `WoundSystem.CanCreateWound:117` | a type not in the set is stripped before it reaches the part, and can never create a wound |
| `SupportedWounds` | `WoundSystem.CanCreateWound:121` (`Count == 0 \|\| Contains(prototype)`) | the wound-prototype whitelist per profile |
| `TreatmentCapabilities` | `WoundDamageRoutingSystem.CanTreatPart:971-979`, `WoundHealingSystem.IsCompatiblePart:153-166` | **`CanTreatPart` returns `true` when the caller declared no capabilities** — an unannotated healing source heals anything |
| `BleedingMultiplier` | `WoundSystem.CanBleed:101-108` (`> 0f`) and the bleeding rate path | `0` disables bleeding entirely |
| `Scarrable` | `WoundScarSystem` | gates `MedicalScarWound` creation |
| `CanFeelPain` | `PainSystem.CanFeelPain:406-411`, used by `SetupPart` to add/remove `PainComponent` | `false` removes the part's `PainComponent` |
| `PassiveRecoveryMultiplier` / `BedRecoveryMultiplier` | `FilterPartDamage:1057-1073` (origin has `PassiveDamageComponent` / `WolfmedBedHealMarkerComponent`) | `0` blocks passive + bed healing on that part |
| `CirculatoryStream` | `CirculatoryStreamSystem.GetPartStream:25-30` | only `"Organic"` resolves to a solution in WG's trimmed system |
| `OrganDamage` | `OrganDamageSystem:44-69` | **requires `OrganDamageComponent` on the organ** (`:53`) |

### §5.3 Fracture profile resolution

`WoundFractureSystem.TryGetProfile` (`Content.Shared/_Onyx/Wounds/WoundFractureSystem.cs:145-156`):
```csharp
var profileId = _wfPart.Get(part).FractureProfile; // WOLFGATE: D8, FractureProfile lives on WolfmedBodyPartComponent.
if (profileId is not { } id || !_prototypes.TryIndex(id, out var indexed)) return false;
```
`WolfmedBodyPartSystem.Get` returns a **shared zeroed default** when the component is absent
(`Content.Shared/_WF/Wolfmed/Body/WolfmedBodyPartSystem.cs:5-11`), so "no component" == "no fractures,
no amputation thresholds, `MaxDamage 0`". A YAML `fractureProfile: null` is therefore equivalent to having
no component, but is explicit and survives a parent that sets one.

### §5.4 RT multi-parent precedence — **first parent wins** (this is load-bearing for §6)

`RobustToolbox/Robust.Shared/Prototypes/PrototypeManager.cs:417-437`: for `parents.Length > 1` the
resolved parent maps are passed to `PushCompositionWithGenericNode(kind, parentMaps, child)`.
`RobustToolbox/Robust.Shared/Serialization/Manager/SerializationManager.Composition.cs:26-57`:
```csharp
var node = child;
foreach (var parent in parents) { var newNode = pusher(type, parent, node, context); node = newNode; }
```
and `RobustToolbox/Robust.Shared/Serialization/Markdown/Mapping/MappingDataNode.cs:412-425`:
```csharp
public override MappingDataNode PushInheritance(MappingDataNode node)
{
    var newNode = Copy();
    foreach (var (key, val) in node) { if (_children.ContainsKey(key)) continue; newNode.Remove(key); newNode.Add(key, val.Copy()); }
    return newNode;
}
```
i.e. keys already present in the accumulating child are never overwritten. Because parents are folded in
list order, **the earliest parent in a `parent: [A, B]` list wins any conflicting key**, and
`ComponentRegistrySerializer` applies the same rule per component and then per field
(`.../ComponentRegistrySerializer.cs:203-226`).

**Consequence:** every species part in WG is written `parent: [Part<Species>, Base<Slot>]` — the species
base is already first. Adding the Wolfmed data to the species base therefore beats `BaseLeftArm`'s
`WolfmedBodyPart` block, per field. Verified spellings: `LeftArmDiona: parent: [PartDiona, BaseLeftArm]`
(`Body/Parts/diona.yml`), `LeftArmSlime: parent: [PartSlime, BaseLeftArm]` (`slime.yml`),
`LeftArmIPC: parent: [PartIPCBase, BaseLeftArm]` (`_EinsteinEngines/Body/Parts/ipc.yml:64`),
`LeftArmCyberneticBase: parent: [CyberneticPartBase, BaseLeftArm]` (`_Shitmed/Body/Parts/cybernetic.yml:18`).

### §5.5 Wound creation is indexed globally, then filtered by profile

`WoundSystem.HandlePartDamageApplied` (`Content.Shared/_Onyx/Wounds/WoundSystem.cs:66-99`) walks
`_woundsByDamageType[type]` — a global index of every `wound` prototype listing that damage type — and
calls `CanCreateWound(part, prototype.ID, type)` per candidate. Adding ten species wounds therefore
**cannot** affect organics (the Organic profile's `supportedWounds` excludes them); it only lengthens the
per-hit loop by at most 4 entries for Blunt/Slash/Piercing and 3 for Heat/Cold/Caustic. **D2 is satisfied
by construction.**

### §5.6 Damage that a profile accepts but the container refuses is silently lost

`WoundDamageRoutingSystem.ApplyPartChange` calls `_wolfmedDamage.TryChangeDamage(part, …, out var appliedDamage, …)`
and raises `PartDamageAppliedEvent(body, part, appliedDamage, …)` (`:1029-1041`) — wounds are created from
**applied** damage. A part whose `DamageableComponent` container omits a type applies zero of it.
Cross-referencing §4.2:

| profile | part container | accepted-by-profile but refused-by-container |
|---|---|---|
| Ipc (on `PartIPCBase`, `Inorganic`) | 5 types | **Cold, Caustic** |
| Cybernetic (on `CyberneticPartBase`, `Silicon`) | 6 types | **Cold, Caustic** |
| Slime / Plant (`OrganicPart`) | 7 types | none |

So `IpcMechanicalDamageWound`'s `Cold` and `Caustic` rows and
`CyberneticMechanicalDamageWound`'s are inert in WG. Harmless (they are also not in the *body's* `Silicon`
container, so the damage never existed), but it must be documented or a future reader will file it as a bug.

### §5.7 Bleeding requires a `BloodstreamComponent` on the body, and fails soft without one

`Content.Server/_Onyx/Wounds/WoundBleedingSystem.RefreshBody:293-296`:
```csharp
if (!_net.IsServer || !HasComp<WoundHostComponent>(body) ||
    !TryComp(body, out BloodstreamComponent? bloodstream))
    return;
```
and `CirculatoryStreamSystem.SetBleedRates:70-80` early-returns the same way. **No crash** — but
`WoundBleedingComponent`s exist, tick, and drain nothing: no puddles, no `Bleed` alert, no blood level
drop, and the analyzer would report "external bleeding" that costs the patient nothing. An IPC made a
wound host with `bleedingMultiplier: 1` and no bloodstream is **cosmetically wrong**, not broken.

`BloodstreamComponent` in WG (`Content.Server/Body/Components/BloodstreamComponent.cs`) has
`BloodReagent` (`:142-143`, `= "Blood"`), `BloodMaxVolume` (`:133-134`, `= 300`),
`DamageBleedModifiers` (`:99-100`, `= "BloodlossHuman"`), `BloodlossDamage`/`BloodlossHealDamage`
(`:69-77`, **both `required: true`**), `MaxBleedAmount` (`:56-57`), `BleedReductionAmount` (`:50-51`),
`BleedPuddleThreshold` (`:89-90`), `BloodlossThreshold` (`:62-63`), `BleedingAlert` (`:183-184`).
**It has no `bloodReferenceSolution` field** — Onyx's spelling. The WG-native equivalent is
`bloodReagent:` + `bloodMaxVolume:`. Slime and diona already use it:
`Entities/Mobs/Species/slime.yml:82-83 bloodReagent: Slime`,
`Entities/Mobs/Species/diona.yml:48-49 bloodReagent: Sap`.

### §5.8 Body damage on a wound host is the sum of its parts

`WoundDamageProjectionSystem.RefreshBodyDamage` (`:155-195`) builds `total` from `SystemicDamageComponent`
plus `GetPositiveDamage(part)` for **every** body child and calls `_damage.SetDamage(body, total)`. This is
why D22 raised the organic gib trigger to 1500 (`Entities/Mobs/Species/base.yml:271-278`). Any new wound
host needs the same review of its `Destructible` and `MobThresholds`.

---

## §6. The proposed mapping, with exact YAML

### §6.0 One-line summary table

| WG part set | Onyx profile | fracture profile | severable? | scars | pain | bleeds |
|---|---|---|---|---|---|---|
| every `BasePart` descendant (default) | `OrganicBodyPartProfile` | `OrganicFractureProfile` | yes | yes | yes | Blood |
| `PartSlime` (10 parts) | `SlimeBodyPartProfile` | **none** | yes (organic thresholds) | **no** | yes | Slime ×1.15 |
| `PartDiona` (10 parts) | `PlantBodyPartProfile` | **none** | **no** (`{}`) | yes | yes | Sap ×1.0 |
| `PartIPCBase` (10 parts) | `IpcBodyPartProfile` | **none** | yes (see §6.3) | no | **decision** | **decision** (Oil) |
| `CyberneticPartBase` (14 entities) | `CyberneticBodyPartProfile` | `CyberneticFractureProfile` | yes | no | **no** | ×0.5, mechanical wounds don't bleed |
| `PartProtogen` (10 parts) | `OrganicBodyPartProfile` (unchanged) | `OrganicFractureProfile` | yes | yes | yes | Blood |

### §6.1 File A — append the 16 prototypes to `Resources/Prototypes/_Onyx/Wounds/wounds.yml`

Copy ONYX's blocks **verbatim**, in Onyx's own file order, with exactly these `# WOLFGATE` folds:

1. **D9 fold on three profiles.** `IpcBodyPartProfile`, `SlimeBodyPartProfile` and `PlantBodyPartProfile`
   each carry `organDamage.chances` with `Chest: 0.04` and `Groin: 0.04`. Fold to a single `Torso: 0.04`
   exactly as `OrganicBodyPartProfile` already is (WG `:29-31`), with the same comment wording.
   `CyberneticBodyPartProfile` has no `organDamage` block — nothing to fold.
2. **§8.2-1 fold on `CyberneticFractureProfile`.** Replace the four `manipulationModifier` values with
   `1.1 / 1.25 / 1.5 / 2.0` and repeat the marked explanation block, or reference it.
3. **Nothing else.** The ten species wound prototypes are copied byte-for-byte.

Example (the Ipc profile as it should land; `# WOLFGATE` lines are the only deltas):

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
    - Cold    # WOLFGATE (P5): inert - the Inorganic container on PartIPCBase supports neither Cold nor Caustic.
    - Shock
    - Caustic # WOLFGATE (P5): inert, as above. Kept so the profile stays a verbatim Onyx copy.
  supportedWounds:
    - IpcMechanicalDamageWound
    - ElectricalWound
    - SurgicalIncisionWound
    - DismembermentWound
    - AmputationConsequenceWound
    - SystemicBleedingWound
  organDamage:
    chances:
      Head: 0.05
      # WOLFGATE (D9): Onyx's Chest 0.04 and Groin 0.04 fold into Torso 0.04 (not summed - independent per-part rolls, one torso).
      Torso: 0.04
      Arm: 0.02
      Hand: 0.01
      Leg: 0.02
      Foot: 0.01
    # WOLFGATE (P5): inert until IPC organs carry OrganDamage (DECISIONS §8.6-7 keeps organ damage human-lineage only).
```

### §6.2 File B — `Resources/Prototypes/_WF/Wolfmed/Body/species_parts.yml` (new)

A second `_WF` abstract file in the exact shape of the existing `parts.yml` header comment. Four abstracts:

```yaml
# Wolfmed per-species part data (P5-1). Values are Onyx's:
#   Slime  - ONYX Resources/Prototypes/Body/Species/slime.yml:236-240 (OrganSlimePersonExternal)
#   Plant  - ONYX Resources/Prototypes/Body/Species/diona.yml:229-234 (OrganDionaExternal)
#   Ipc    - ONYX Resources/Prototypes/Corvax/Body/Species/ipc.yml:325-330 (OrganIpcExternal)
#   Cyber  - ONYX Resources/Prototypes/_Onyx/Entities/Mobs/Customization/Parts/cybernetic.yml:6-10
#
# Same shape as _WF/Wolfmed/Body/parts.yml: separate abstract ids added to the species part base's
# `parent:` list with a one-line `# WOLFGATE` edit, because RT throws "Duplicate ID" on redeclaration.
# RT folds multiple parents in list order and a key already present is never overwritten
# (RobustToolbox/Robust.Shared/Serialization/Markdown/Mapping/MappingDataNode.cs:412-425), so putting
# these FIRST in the species base's parent list makes them beat Base<Slot>'s WolfmedBodyPart block.

- type: entity
  id: WolfmedPartSlime
  abstract: true
  components:
  - type: Woundable
    profile: SlimeBodyPartProfile
  - type: WolfmedBodyPart
    fractureProfile: null   # slime has no bones; Onyx sets this on every slime part

- type: entity
  id: WolfmedPartDiona
  abstract: true
  components:
  - type: Woundable
    profile: PlantBodyPartProfile
  - type: WolfmedBodyPart
    fractureProfile: null
    amputationThresholds: {}   # Onyx: diona limbs cannot be severed by damage

- type: entity
  id: WolfmedPartIpc
  abstract: true
  components:
  - type: Woundable
    profile: IpcBodyPartProfile
  - type: WolfmedBodyPart
    fractureProfile: null
    # WOLFGATE (P5 balance): Onyx's IPC amputation thresholds (arm 270/400/600) are unreachable in
    # Wolfgate - PartIPCBase gibs the limb at Blunt 110 / Slash 150
    # (Resources/Prototypes/_EinsteinEngines/Body/Parts/ipc.yml:10-25). The organic thresholds inherited
    # from Base<Slot> (WolfmedBase*, arm 130/250/250 + P3 Heat rows) are left in place instead; see §6.3.

- type: entity
  id: WolfmedPartCybernetic
  abstract: true
  components:
  - type: Woundable
    profile: CyberneticBodyPartProfile
  - type: WolfmedBodyPart
    fractureProfile: CyberneticFractureProfile
```

**Wiring, four one-line marked edits:**

```yaml
# Resources/Prototypes/Body/Parts/slime.yml:4
  parent: [WolfmedPartSlime, BaseItem, BasePart] # WOLFGATE (P5-1): slime wound profile, no bone fractures

# Resources/Prototypes/Body/Parts/diona.yml:3
  parent: [WolfmedPartDiona, BaseItem, BasePart] # WOLFGATE (P5-1): plant wound profile, unseverable limbs

# Resources/Prototypes/_EinsteinEngines/Body/Parts/ipc.yml:3
  parent: [WolfmedPartIpc, BasePartInorganic] # WOLFGATE (P5-1): IPC chassis wound profile

# Resources/Prototypes/_Shitmed/Body/Parts/cybernetic.yml:3
  parent: [WolfmedPartCybernetic, BasePartInorganic] # WOLFGATE (P5-1): cybernetic wound + frame-fracture profile
```

`WolfmedPart*` must be **first** so it wins the `fractureProfile` / `amputationThresholds` keys against
`Base<Slot>` (§5.4). It does not need to beat `BasePart`/`BasePartInorganic` (neither declares
`Woundable` or `WolfmedBodyPart`), but first is still correct and consistent.

### §6.3 IPC amputation thresholds — recommendation

Onyx IPC arm = `Slash 270 / Piercing 400 / Blunt 600`; the WG IPC limb is destroyed by `GibPartBehavior`
at **Blunt 110 / Slash 150** (`_EinsteinEngines/Body/Parts/ipc.yml:10-25`), lower even than the organic
`MajorLimb` 190/210 (`Body/Parts/base.yml:280-293`). Onyx's numbers can never be hit.

Three options:

| | change | effect |
|---|---|---|
| **A (recommended)** | leave `amputationThresholds` off `WolfmedPartIpc`; the organic P3 set is inherited from `Base<Slot>` | IPC limbs sever on the same schedule as organic limbs (Heat rows and the P3 finishing minimums included). Consistent with §8.6-1, zero extra numbers to balance. |
| B | port Onyx's IPC numbers **and** raise `PartIPCBase`'s Destructible to ≥ 600 Blunt / ≥ 300 Slash | faithful to Onyx; IPC limbs become far harder to remove than organic ones and IPC limb-gibbing effectively disappears. Two upstream-file balance edits. |
| C | port Onyx's numbers unchanged | IPC limbs can *never* be severed; the `DismembermentWound`/`AmputationConsequenceWound` entries in the profile become dead. Not recommended. |

### §6.4 `MobIPC` — the mob-level block

```yaml
# Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/ipc.yml, inside MobIPC's components:
  # WOLFGATE (P5-1): IPCs become wound hosts. ONYX Resources/Prototypes/Corvax/Body/Species/ipc.yml:101.
  - type: WoundHost
  # WOLFGATE (P5-1/P5-2): Onyx's IPC bleeds oil through an ordinary Bloodstream (ONYX ipc.yml:102-113).
  # Wolfgate's BloodstreamComponent has no `bloodReferenceSolution` field, so this is the native spelling;
  # the values are the ones EE left commented at _EinsteinEngines/.../silicon_base.yml:155-171.
  # bloodlossDamage is Heat, not Bloodloss: the mob's Silicon damage container does not support Bloodloss
  # (Resources/Prototypes/Damage/containers.yml:28-34), so a Bloodloss charge would be silently dropped.
  - type: SolutionContainerManager
  - type: Bloodstream
    bloodReagent: Oil
    bloodMaxVolume: 500
    chemicalMaxVolume: 0
    bleedReductionAmount: 0
    bleedPuddleThreshold: 3
    bloodlossThreshold: 0
    maxBleedAmount: 14
    bloodlossDamage:     { types: { Heat: 1.5 } }
    bloodlossHealDamage: { types: { Heat: 0 } }
  # WOLFGATE (D22, P5-1): body damage on a wound host is the sum of every part
  # (WoundDamageProjectionSystem.RefreshBodyDamage), so 400 is now reachable from routine limb damage -
  # the same reason BaseMobSpeciesOrganic was raised to 1500 (Entities/Mobs/Species/base.yml:271-278).
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTypeTrigger
        damageType: Blunt
        damage: 1500
      behaviors:
      - !type:GibBehavior { }
```

`PlayerSiliconHumanoidBase:91-95`'s `!type:DamageTrigger damage: 500` (**total** damage, all types) needs
the same treatment or must be overridden on `MobIPC`; `MobIPC` does not currently override it and a
wound-host IPC will cross 500 aggregate long before it dies at 100.

**Do not touch `MobThresholds`.** Death still happens at the projected total of 100, exactly as today.

Optional, per decision D-P5-1-B: `- type: PainShockTarget` (needed for pain shock) and an `EmoteOnDamage`
block (needs emotes in `MobIPC`'s `Speech.allowedEmotes` — today `Boop`/`Whirr` do not exist there; WG's
`MobIPC` has no `allowedEmotes` key at all).

### §6.5 Protogen

One-line deletion, `Content.Shared/_WF/Wolfmed/Body/WolfmedWoundHostExclusionSystem.cs:12`:
```csharp
private static readonly HashSet<string> ExcludedAncestors = new();   // was { "BaseMobProtogen" }
```
plus removing the marked comment at `Resources/Prototypes/_Mono/Entities/Mobs/Species/protogen.yml:2-4`.
Justification in §12 (D-P5-1-C). If the user instead wants protogen to be *cybernetic*, the change is much
larger: `PartProtogen` would need `WolfmedPartCybernetic`, the Blood bloodstream would need replacing, and
`Hunger`/`Thirst`/`Respirator`/`Butcherable → FoodMeatHuman` would all read as contradictions.

### §6.6 What P5-1 does **not** need to touch

- **The welder / nanite applicator keep working on IPCs with zero changes.** `WeldingHealableSystem` calls
  `TryChangeDamage` on the mob with no treatment-capability context, and `CanTreatPart` returns `true`
  when the routing system has no capabilities registered for that body
  (`WoundDamageRoutingSystem.cs:971-974`). The routed pass also carries `HealWounds = true` (the
  `_skipWoundHealing` set is only populated by the explicit `TryApplyLocalizedDamage`/`TryApplyOriginDamage`
  entry points, `:181, :623`), so a welder will heal `IpcMechanicalDamageWound`s as well as part damage.
- **Medical items stop working on IPC/cybernetic limbs automatically.** `HealingComponent.TreatmentCapabilities`
  defaults to `[Biological]` (`Content.Server/Medical/Components/HealingComponent.cs:73`), so every brute
  pack, ointment and gauze is already correctly excluded once the profile is Mechanical-only. No negative
  annotation is needed — only the positive one on the coil (P5-3).
- **Slime and diona bloodstreams.** Already `Slime` and `Sap`.
- **Locale.** Already complete (§8).

---

## §7. What each species' player will experience

### Diona (Plant profile) — `roundStart: true`, 3 mob prototypes
- **Limbs can no longer be severed at all.** Today a diona arm can be cut off by a machete or finished by
  a 14-Piercing round like anyone else's; after P5-1 `amputationThresholds: {}` makes that impossible.
  This is a *player-facing buff* and the single biggest balance change in P5-1.
- **No more broken bones.** No `BoneFractureWound`, no `BrokenBones` alert, no fracture movement/do-after
  penalty, no `SurgeryMendFracture` on a diona. (Diona are already the "regenerates in light" species;
  this fits.)
- Bleeding, pain, scars, internal bleeding, surgical incisions, systemic bleeding, passive recovery and
  bed recovery: **all identical to today**, since the Plant profile matches Organic in every number.
- Wound names change to "plant tissue contusion / laceration / puncture / burn" on examine and the
  analyzer.
- Blood is already Sap; puddles are unchanged.

### Slime (Slime profile) — `roundStart: true`, 3 mob prototypes
- **No broken bones** (same as diona).
- **Limbs still sever** on the organic schedule (Onyx does not empty slime thresholds).
- **Bleeds sooner and harder.** `bleedingMultiplier: 1.15` and the slime wounds drop the organic
  `minimumSeverity: 8/9` gate, so a slime leaks from the very first scratch where a human would not.
- **No scars, ever** (`scarrable: false`) — including the surgical `MedicalScarWound`, which is not in the
  slime `supportedWounds` list. A slime surgeon's patient never keeps a mark.
- Slightly less pain from blunt trauma (`0.7` vs `0.87`); burns hurt the same.
- Wound names become "slime deformation / laceration / puncture / damage".

### IPC (Ipc profile) — `roundStart: true`, 1 mob prototype
This is a species that has been **completely outside the medical system** for four phases and joins it all
at once. Concretely, after P5-1 an IPC:
- has per-limb damage, per-limb armour, targeted limb shots, limb functionality loss (Impaired at
  chassis-damage severity 25, Disabled at 50), severable limbs, stump trauma, limb re-attachment surgery,
  the phase-2 part-status examine and the phase-4 analyzer panel — none of which it has today;
- takes **one wound type for everything except electricity**: "chassis damage", superficial → moderate →
  severe → critical. Shock still makes a separate "electrical trauma";
- **never scars** and **never passively heals** (`passiveRecoveryMultiplier: 0`, `bedRecoveryMultiplier: 0`):
  an IPC parked in a medbay bed recovers *nothing*. Today it also recovers nothing, so this is parity —
  but it makes the welder/coil the only route, which is the intent;
- **cannot be treated by any medicine, brute pack, ointment, gauze, tourniquet or wound surgery**
  (`treatmentCapabilities: [Mechanical, Electrical]` vs `HealingComponent`'s `[Biological]` default);
  the welder and nanite applicator keep working (§6.6) and the cable coil needs its P5-3 annotation;
- **never gets a bone fracture** (`fractureProfile: null`) — the `BrokenBones` alert never fires;
- **leaks oil** if the Bloodstream lands (§6.4): oil puddles, the `Bleed` alert, a blood-level readout on
  the analyzer, and an ordinary tourniquet/clamp becoming meaningful. Every chassis wound bleeds
  (`chance: 1`), so this is not a rare event — **this is the change with the largest gameplay footprint in
  P5-1** and the reason D-P5-1-A exists;
- **feels pain** unless the user overrides it (D-P5-1-B): the client pain vignette would appear on IPC
  players, but pain *shock* would not fire (no `PainShockTarget`) and no scream emotes would play
  (no `EmoteOnDamage`). That is an inconsistent half-state and should be resolved deliberately.

### Protogen — `roundStart: false`, 2 mob prototypes
If the exclusion is lifted: protogen gets **exactly what every other organic humanoid has had since phase 1**
— wounds, bleeding (Blood), pain and the pain overlay, fractures and the `BrokenBones` alert, amputation,
part-status examine, the analyzer panel, the tourniquet (which they lose entirely today, P4-D11), and every
wound surgery. No organ damage (their organs have no `OrganDamage`, §4.2). Nothing regresses; the species is
biologically organic in every component it carries.

### Anyone wearing a cybernetic limb (Cybernetic profile)
This affects **organic humans**, not a species: `PrybarProsthetics` and `BionicLegs` trait holders, lathe
customers, and autosurgeon patients. On that limb only:
- **it stops hurting** (`canFeelPain: false`, and `SetupPart` actively `RemComp`s the `PainComponent`);
- **it stops bleeding** in practice — the only bleeding wounds it supports are `DismembermentWound` and
  `SurgicalIncisionWound` (`CyberneticMechanicalDamageWound` has no bleed behaviour at all), and even those
  run at `bleedingMultiplier: 0.5`;
- **it never scars**, **never passively heals**, and **never heals in a bed**;
- **brute packs, ointment and medicine stop working on it** — steel arm, steel treatment;
- **it breaks differently**: `CyberneticFractureProfile` gives it a "frame fracture" (microcrack → cracked
  → deformed → shattered) instead of a bone fracture, on the *same* thresholds/chances, still raising the
  `BrokenBones` alert and still applying the movement and (corrected) manipulation penalties, and still
  mendable by the phase-4 `SurgeryMendFracture` chain — but the wound it clears is
  `CyberneticFrameFractureWound`, so **P4's `SurgeryMendFracture` completion check must be re-verified
  against the cybernetic profile** (see §12 blocker T3);
- it takes "cybernetic damage" instead of contusions/lacerations/burns, and **feels nothing while it
  happens** — a player can lose a cybernetic arm's function without a pain cue.

---

## §8. P5-5 — analyzer and examine strings

### §8.1 Already done — no work needed
`Resources/Locale/en-US/_Onyx/prototypes/wounds/wounds.ftl` in WG is **byte-identical to Onyx's** and
already contains every species key:
`wound-name-ipc-mechanical-damage = chassis damage`, `wound-name-cybernetic-mechanical-damage = cybernetic damage`,
`wound-name-cybernetic-frame-fracture = frame fracture`, the four `wound-name-slime-*`, the four
`wound-name-plant-*`, and the stage keys `wound-stage-mechanical-{minor,moderate,severe,critical}`
(`superficial / moderate / severe / critical`) and `wound-stage-frame-{hairline,simple,displaced,comminuted}`
(`microcrack / cracked / deformed / shattered`).

`Resources/Locale/en-US/_Onyx/medical/health-examinable.ftl` is likewise identical to Onyx's and already
ships `wound-examine-frame-hairline = thin frame cracks`, `-simple = deep frame cracks`,
`-displaced = deformed frame`, `-comminuted = destroyed frame`.

So **every wound name and stage an IPC, cybernetic limb, slime or diona can produce already renders
correctly** in the phase-2 examine (`HealthExaminableSystem.PartStatus.cs:108`, which `Loc.GetString`s the
stage's `examineDescription`) and in the phase-4 analyzer panel
(`Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml.cs:149-151`, which `Loc.GetString`s
`wound.Name` and the stage name straight out of the payload).

### §8.2 What still reads wrong, and the cheapest fix
Three strings are hard-coded and assume flesh:

| key | file | shown when | wrong for |
|---|---|---|---|
| `health-analyzer-wound-bleeding-short = external bleeding` | `Resources/Locale/en-US/_Onyx/medical/health-analyzer-component.ftl:4` | any `BleedingRate > 0` | an IPC leaking oil |
| `health-examinable-part-bleeding = active bleeding` | `.../_Onyx/medical/health-examinable.ftl` | any bleeding part | same |
| `health-analyzer-wound-fracture-short = fracture: { $grade }` | `.../health-analyzer-component.ftl:17` | any `Fracture != None` | a cybernetic frame fracture |
| `health-examinable-part-damage-{blunt,slash,piercing,heat,cold,shock,caustic}` = `bruises/cuts/puncture wounds/burns/frostbite/electrical burns/chemical burns` | `.../health-examinable.ftl` | examine, per damage type present | a chassis: "bruises" on steel |

The analyzer payload is `HealthAnalyzerWoundDiagnostic`
(`Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs:9-24`, a `readonly record struct` with ten
positional members, byte-identical to Onyx's except the `_Shitmed.Targeting` using). **It carries no
material/profile information**, so the client cannot currently vary the wording.

Recommended minimum (cheap, additive, no payload change):
- add a `bool Mechanical` (or `ProtoId<BodyPartProfilePrototype> Profile`) member to
  `HealthAnalyzerWoundDiagnostic` — it is a `_Onyx` vendored file, so the addition is a `// WOLFGATE` edit
  with an appended member, mirroring how `HealthAnalyzerScannedUserMessage` already gained four appended
  optional parameters (`Content.Shared/MedicalScanner/HealthAnalyzerScannedUserMessage.cs:28`);
- in `WolfmedDiagnosticPanel`, pick `health-analyzer-wound-bleeding-short-mechanical = fluid leak` and
  `health-analyzer-wound-fracture-short-frame = frame damage: { $grade }` when the flag is set;
- add `health-examinable-part-bleeding-mechanical = leaking fluid` and, if the user wants it, a
  mechanical variant set of `health-examinable-part-damage-*` (`dents / gashes / punctures / scorching /
  … / scorched contacts / corrosion`).

Note that **Onyx itself does none of this** — Onyx's IPC examine says "bruises" and "active bleeding" too.
This is a Wolfgate improvement, so it is a legitimate candidate to defer. If deferred, record it.

### §8.3 One string that is already right
`fracture-grade-{hairline,simple,displaced,comminuted}` lives in
`Resources/Locale/en-US/_Onyx/guidebook/entity-effects.ftl:25-28` and is shared by both fracture profiles —
the grade words are material-neutral. Only the `fracture:` label needs a variant.

---

## §9. External symbols — SAME / DIFFERENT / MISSING in WG

| symbol (as Onyx uses it) | status in WG | evidence |
|---|---|---|
| `bodyPartProfile` prototype kind + all 10 fields | **SAME** | `Content.Shared/_Onyx/Wounds/WoundPrototype.cs:134-183` |
| `fractureProfile` prototype kind + all 13 fields | **SAME** | `.../WoundPrototype.cs:210-262` |
| `OrganDamageRouting` (`chances`, `maxAffected`) | **SAME** | `.../WoundPrototype.cs:186-198` |
| `TreatmentCapability` enum (`Biological/Mechanical/Electrical`) | **SAME** | `.../WoundPrototype.cs:203-208` |
| `WoundableComponent` (`- type: Woundable`, field `profile`) | **SAME** | `Content.Shared/_Onyx/Wounds/WoundDamageComponents.cs:155-170` |
| `WoundHostComponent` (`- type: WoundHost`) | **SAME** | `.../WoundDamageComponents.cs:14-92` |
| `!type:WoundPainBehavior` / `WoundScarBehavior` / `WoundBleedingBehavior` / `WoundFunctionalityBehavior` / `WoundInternalBleedingBehavior` | **SAME** — each class defined exactly once | `grep "class <X>" Content.Shared` → 1 hit each |
| `BodyPartType` enum values `Chest`, `Groin` | **MISSING** (D9) — WG has `Torso` only, no `Groin` | `.../WoundDamageComponents.cs:20-27, 51-53` |
| `FractureGrade`, `FractureTreatment`, `BleedingTreatment`, `BodyPartFunctionalityState`, `WoundMergeMode` | **SAME** | `WoundPrototype.cs:262-295`, `WoundDamageComponents.cs` |
| `circulatoryStream` prototype `Organic` | **SAME** (byte-identical 2-line file) | WG `Resources/Prototypes/_Onyx/Chemistry/circulatory_streams.yml` vs ONYX same path |
| `CirculatoryStreamPrototype.MetabolismStage` / `MetabolitesStage` | **MISSING** (D15/WP3#2, commented out) | `Content.Shared/_Onyx/Chemistry/Circulation/CirculatoryStreamPrototype.cs:25-30` |
| `BloodstreamComponent.bloodReferenceSolution` | **MISSING** — WG uses `bloodReagent` + `bloodMaxVolume` | `Content.Server/Body/Components/BloodstreamComponent.cs:142-143, 133-134` |
| `BloodlossIPC` damage modifier set | **MISSING** — only `BloodlossHuman` exists | `Resources/Prototypes/Damage/modifier_sets.yml:248` |
| `SiliconIpc` damage container (Onyx's `Injurable` container for IPC/cybernetic parts) | **MISSING** — WG has `Inorganic` and `Silicon` | `Resources/Prototypes/Damage/containers.yml:11,28` |
| `InjurableComponent` | **MISSING** (D19, not ported) | `WoundDamageProjectionSystem.cs:227-228` comment |
| `TransplantCompatibilityComponent` | **MISSING** — Onyx-only transplant system, not needed | no hits in WG |
| `MechanicalOrganComponent`, `RepairableBodyPartComponent`, `RoundstartCyberneticsComponent`, `VisualOrgan*`, `DirectionalLimbLayer` | **MISSING** — Onyx Nubody/customization stack, out of scope (D8) | no hits in WG |
| `_Onyx/Repairable` (`RepairableComponent.TreatmentCapabilities`, `WelderComponent.RepairModes`, `WelderRepairSystem`) | **MISSING** — WG's equivalent is EE's `WeldingHealable`/`WeldingHealing` | `Content.Server/_EinsteinEngines/Silicon/WeldingHealable/` |
| `HealthAnalyzerWoundDiagnostic` record | **SAME** (only the `using` differs) | `Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs` vs ONYX same path |
| every `wound-name-*` / `wound-stage-*` / `wound-examine-frame-*` LocId used by the 16 prototypes | **SAME** (files byte-identical) | §8.1 |
| `BrokenBones` alert (used by `CyberneticFractureProfile`) | **SAME** | `Resources/Prototypes/_Onyx/Alerts/alerts.yml` (phase 2) |
| `Oil`, `Slime`, `Sap` reagents | **SAME** | `Oil` used at `_NF/Entities/Mobs/NPCs/arcadiaindustries.yml:75`; `Slime`/`Sap` at species yml |

---

## §10. Collision and subscription audit

**New prototype ids — all 16 are unused anywhere in WG.** Verified with
`grep -rn "<id>" Resources Content.Shared Content.Server Content.Client --include=*.yml --include=*.cs --include=*.ftl`:
`IpcBodyPartProfile 0`, `SlimeBodyPartProfile 0`, `CyberneticBodyPartProfile 0`, `PlantBodyPartProfile 0`,
`CyberneticFractureProfile 0`, `IpcMechanicalDamageWound 0`, `CyberneticMechanicalDamageWound 0`,
`CyberneticFrameFractureWound 0`, `SlimeBluntWound 0`, `SlimeSlashWound 0`, `SlimePiercingWound 0`,
`SlimeBurnWound 0`, `PlantBluntWound 0`, `PlantSlashWound 0`, `PlantPiercingWound 0`, `PlantBurnWound 0`.

**New `_WF` abstract entity ids — all unused:** `WolfmedPartIpc 0`, `WolfmedPartSlime 0`,
`WolfmedPartDiona 0`, `WolfmedPartCybernetic 0`.

**Effect / behaviour classes.** P5-1 introduces **no new `!type:` class**. All five behaviour classes the
species wounds use already exist exactly once in `Content.Shared` and are already resolved by the existing
twelve wound prototypes, so the old-style bare-class-name `!type:` resolution is already proven for each.

**Component names.** `Woundable` and `WolfmedBodyPart` are existing registered components; P5-1 only adds
YAML declarations of them. No new component is registered, so no `[RegisterComponent]` name collision is
possible.

**`SubscribeLocalEvent` pairs.** **P5-1 adds none.** Every system that reads the new data
(`WoundSystem`, `WoundDamageRoutingSystem`, `WoundFractureSystem`, `WoundBleedingSystem`, `PainSystem`,
`WoundScarSystem`, `OrganDamageSystem`, `WoundHealingSystem`, `CirculatoryStreamSystem`,
`WolfmedBodyPartSystem`) already exists and already subscribes what it needs.
The only C# touched is the one-line literal in `WolfmedWoundHostExclusionSystem.cs:12`, whose single
subscription `<WoundHostComponent, ComponentInit>` (`:17`) is unchanged.
If §8.2's analyzer wording is taken, that adds a field to a `record struct` and a branch in a client
control — still no subscription.

**Duplicate-directed-subscription risk: zero for P5-1.** (Recorded because project memory flags it as a
server-start crash class.)

**Id-collision hazard to avoid:** do not port ONYX
`Resources/Prototypes/_Onyx/Entities/Mobs/Customization/Parts/cybernetic.yml` or
`.../Organs/cybernetic.yml` — they declare `LeftArmCybernetic`, `RightHandCybernetic`,
`JawsOfLifeLeftArm`, `SpeedLeftLeg`, `BasicCyberneticEyes`, `MedicalCyberneticEyes`,
`SecurityCyberneticEyes` … all of which already exist in WG with different definitions
(`_Shitmed/Body/Parts/cybernetic.yml`, `_Shitmed/Body/Organs/cybernetic.yml`).

---

## §11. Ordered file list and difficulty

Sequential, one owner per file (the phase-2/3/4 execution rule).

| # | file | action | lines | difficulty |
|---|---|---|---|---|
| 1 | `Resources/Prototypes/_Onyx/Wounds/wounds.yml` | append 16 prototypes verbatim + 4 marked folds; update the `:1` trim comment | ~+650 | **Low** — mechanical copy, two known folds |
| 2 | `Resources/Prototypes/_WF/Wolfmed/Body/species_parts.yml` | **new** — 4 abstracts (§6.2) | ~55 | **Low** |
| 3 | `Resources/Prototypes/Body/Parts/slime.yml:4` | one marked `parent:` edit | 1 | **Low** |
| 4 | `Resources/Prototypes/Body/Parts/diona.yml:3` | one marked `parent:` edit | 1 | **Low** |
| 5 | `Resources/Prototypes/_EinsteinEngines/Body/Parts/ipc.yml:3` | one marked `parent:` edit | 1 | **Low** |
| 6 | `Resources/Prototypes/_Shitmed/Body/Parts/cybernetic.yml:3` | one marked `parent:` edit | 1 | **Low** |
| 7 | `Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/ipc.yml` | `WoundHost` + `Bloodstream` + `Destructible` override (§6.4) | ~25 | **Medium** — the balance-sensitive one |
| 8 | `Content.Shared/_WF/Wolfmed/Body/WolfmedWoundHostExclusionSystem.cs:12` | empty the set (if D-P5-1-C = lift) | 1 | **Low** |
| 9 | `Resources/Prototypes/_Mono/Entities/Mobs/Species/protogen.yml:2-4` | replace the exclusion comment | 3 | **Low** |
| 10 | *(P5-5, optional)* `Content.Shared/_Onyx/Medical/HealthAnalyzerWoundDiagnostic.cs`, `Content.Server/_Onyx/Medical/*` producer, `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml.cs`, `Resources/Locale/en-US/_Onyx/medical/{health-analyzer-component,health-examinable}.ftl` | mechanical-variant wording (§8.2) | ~40 | **Medium** |
| 11 | `Docs/Wolfmed/WOLFMED_MANIFEST.md`, `WOLFMED_STATUS.md`, `WOLFMED_PLAN5.md` | rows + deviations | — | **Low** |

**Overall difficulty: Low-to-Medium.** The engineering is trivial; the risk is entirely in balance and in
the three traps below. Upstream footprint added: **5 files** (slime/diona/ipc-parts/cybernetic-parts
`parent:` lines + `MobIPC`), all one- or two-line marked edits except `MobIPC`'s data block — taking the
tracked upstream count from 47 to 52.

**Verification gates before the package is green:**
`dotnet run --project Content.YAMLLinter -c Release`; a 120 s headless server with zero `[ERRO]`/`[FATL]`;
`DockTest` first; then `EntityTest|PrototypeSaveTest` (spawn-all catches a bad `parent:` list instantly);
then the P5-6 species tests.

---

## §12. Decisions for the user, and blockers

### Decisions

**D-P5-1-A — Do IPCs leak oil?** (`MobIPC` gets a `Bloodstream` with `bloodReagent: Oil`, or the Ipc
profile gets a `# WOLFGATE` `bleedingMultiplier: 0`.)
*Recommendation: **YES, add the oil bloodstream** (§6.4).* Onyx's own IPC does exactly this; EE left the
block commented with the values ready; the Ipc profile's `bleedingMultiplier: 1` and
`IpcMechanicalDamageWound`'s `chance: 1` bleed behaviour are meaningless without it, and the analyzer/
examine would otherwise report bleeding that costs nothing. Use `bloodlossDamage: {Heat: 1.5}`, not
Bloodloss — the Silicon container does not support Bloodloss. *Risk:* IPC combat gets noticeably deadlier
and medics need a clamp/tourniquet workflow for robots. *Reversible:* one YAML block.

**D-P5-1-B — Do IPCs feel pain?** Onyx says yes (`IpcBodyPartProfile` does not set `canFeelPain: false`).
WG IPCs feel nothing today and lack `PainShockTarget` and `EmoteOnDamage`.
*Recommendation: **NO — add `canFeelPain: false` to the WG copy of `IpcBodyPartProfile` as a marked
deviation.*** Reasons: (a) shipping pain without `PainShockTarget` gives a pain vignette that can never
resolve into the pain-shock mechanic, an inconsistent half-feature; (b) a screaming robot needs emote and
sound work that is not in P5-1's scope; (c) "IPCs don't feel pain" is the intuitive reading for players and
matches the `DamagedSiliconAccent`/`EmitBuzzWhileDamaged` flavour WG already ships. If the user prefers
Onyx fidelity, the alternative is: keep `canFeelPain` true **and** add `PainShockTarget` + an
`EmoteOnDamage` block with robot-appropriate emotes — a bigger, noisier change.

**D-P5-1-C — Protogen: lift the exclusion, or make it cybernetic?**
*Recommendation: **lift it — make protogen a plain organic wound host.*** Evidence: `damageContainer:
Biological` (`_Mono/.../protogen.yml:33-35`), `PartProtogen: parent: [BaseItem, BasePart]` →
`OrganicPart` limbs, Blood bloodstream inherited from `MobBloodstream`, `Hunger` + `Thirst` +
`Respirator`, `Butcherable → FoodMeatHuman ×5`, organs full of `Nutriment`. The only synthetic markers are
`Deathgasp prototype: SiliconDeathgasp`, a robot typing indicator and the IPC voice pack. Cost: one string
literal. Gain: protogen stops being the one species that silently loses the tourniquet (P4-D11), the
analyzer panel, the pain overlay and every wound surgery. *Risk:* low — `roundStart: false`, 2 prototypes.

**D-P5-1-D — IPC amputation thresholds: A, B or C?** (§6.3.)
*Recommendation: **A*** — inherit Wolfgate's organic P3 numbers, because Onyx's are unreachable under
`PartIPCBase`'s gib triggers and matching them would require raising those triggers (an upstream balance
edit nobody asked for).

**D-P5-1-E — Skeleton.** Never in scope, never recorded, and mechanically ready to be an organic wound
host (Biological container, `BasePart` limbs, a real `Skeleton` body prototype). It is currently the only
biological humanoid outside the system.
*Recommendation: **record it as a known exclusion in the manifest and leave it for phase 6.*** Skeletons
are undead — "bone fracture" and "bleeding" on a skeleton need their own thought (arguably they want the
Plant-style no-bleed treatment), and P5 is already carrying four species.

**D-P5-1-F — P5-5 mechanical wording** (§8.2): ship the `fluid leak` / `frame damage:` variants, or accept
Onyx's flesh-flavoured strings on robots?
*Recommendation: **ship the two analyzer variants plus `health-examinable-part-bleeding-mechanical`; defer
the seven `health-examinable-part-damage-*` mechanical variants*** to the balance/polish pass. The two
analyzer strings are the ones a medic reads while deciding what tool to grab; the examine adjectives are
flavour.

### Blockers and traps for the implementing agent

- **T1 — Parent order is the whole design.** `WolfmedPart*` **must be first** in the species base's
  `parent:` list, and the species base must remain first in each concrete part's list. If either is
  reversed, `Base<Slot>`'s `fractureProfile: OrganicFractureProfile` wins and slimes get bones. Assert this
  in a test (`WolfmedBodyPartComponent.FractureProfile == null` on `LeftArmSlime`), not by eye.
- **T2 — `amputationThresholds: {}` must survive inheritance.** An empty flow mapping is a *present key*
  and therefore suppresses the parent's dict (`MappingDataNode.PushInheritance`). Verify with a spawned
  `LeftArmDiona` in the test, because a typo (`amputationThresholds:` with nothing after it) parses as a
  null value, not an empty dict, and may behave differently.
- **T3 — Re-verify P4's fracture surgery against `CyberneticFractureProfile`.** `WolfmedSurgeryMendFractureEffect`'s
  completion check was written around `WoundFractureSystem.CanTreat`'s `reductionMinimumGrade: Simple`
  quirk (P4-D20, status doc "Bugs found"). The cybernetic profile has the *same* `reductionMinimumGrade`,
  so the logic should carry — but the wound it removes is `CyberneticFrameFractureWound`, and
  `SurgeryMendFracture`'s steps use BoneSetter/BoneGel on a steel frame, which is thematically odd and may
  need a `treatmentCapabilities`-style gate. **Must be tested, not assumed.**
- **T4 — `MobIPC`'s `Destructible`/`DamageTrigger` must be raised** or IPCs will gib from accumulated limb
  damage (§5.8). This is a correctness bug, not a balance preference.
- **T5 — `organDamage` on the three new profiles is inert.** Ship it for fidelity, but record it: no
  IPC/slime/diona organ carries `OrganDamageComponent`, so §8.6-7 still holds and no organ will ever be
  hit. Do not write a test that expects organ damage on these species.
- **T6 — Do not port Onyx's cybernetic prototype files** (§10) — duplicate ids with WG's `_Shitmed` set.
- **T7 — The `Cold`/`Caustic` rows on the mechanical profiles and wounds are dead** in WG because of the
  `Inorganic`/`Silicon` containers (§5.6). If a test asserts "Caustic creates a chassis wound", it will
  fail for a reason that is not a port defect.
- **No hard blocker exists for P5-1.** The phase-1 "shared stage-based metabolizer" blocker recorded in
  `WOLFMED_STATUS.md` ("Next phases → Phase 5") is **factually void at this pin**: Onyx ships one
  circulatory stream and none of the species profiles reference another (§0.2). The status doc's "Blocked
  on a shared stage-based metabolizer for non-organic circulatory streams" line should be struck in P5-7.
