# WOLFMED PHASE 5 — P5-6 test plan

Scope: DECISIONS.md's Phase 5 section, item P5-6. Read-only analysis; nothing in `WG` was modified. Onyx
pinned at `2f5bab9946539cbe083010c9ae6fbc59b47ae377` (`C:\tmp\onyx`, sparse checkout — every Onyx citation
below not in the sparse set is read via `git -C C:/tmp/onyx show HEAD:<path>`, noted explicitly). All WG
citations are against `C:\Users\jzo12\Documents\GitHub\Wolfgate\.claude\worktrees\rules-motd-updates-11c89c`.

**Baseline**: Phase 4 is committed (`2b4a4675d0`). Phase 5 (P5-1 through P5-5) has **not started** — the
`p5/wp` directory is empty. This report therefore does two things at once: (a) plans the P5-6 test suite
against the profile data DECISIONS.md commits the port to (Onyx's pinned numbers, D4), verified directly
against Onyx's source rather than assumed; and (b) documents, as I found them, several facts about
Wolfgate's *current* tree that the P5-1..P5-5 implementers will need and that change what "shipped" means
for a few fields. Every numeric assertion below cites the Onyx source line or the WG mechanism that consumes
it. Where a P5-3/P5-4 mechanism doesn't exist yet and has no fixed design, I say so and give the cheapest
design that reuses an already-proven WG mechanism, flagged as a recommendation, not a fact.

---

## 1. What already exists that these tests build on

### 1.1 The five `bodyPartProfile`s, exact fields (Onyx source, byte-for-byte)

`ONYX Resources/Prototypes/_Onyx/Wounds/wounds.yml:1-152` (`git show HEAD:...`, full file re-read):

| Field | Organic (shipped) | Ipc | Slime | Cybernetic | Plant |
|---|---|---|---|---|---|
| `treatmentCapabilities` | `[Biological]` | `[Mechanical, Electrical]` | `[Biological]` | `[Mechanical, Electrical]` | `[Biological]` |
| `bleedingMultiplier` | 1.0 | 1 | 1.15 | 0.5 | 1 |
| `scarrable` | *(unset → true)* | **false** | **false** | **false** | *(unset → true)* |
| `canFeelPain` | *(unset → true)* | *(unset → true)* | *(unset → true)* | **false** | *(unset → true)* |
| `passiveRecoveryMultiplier` | *(unset → 1)* | **0** | *(unset → 1)* | **0** | *(unset → 1)* |
| `bedRecoveryMultiplier` | *(unset → 1)* | **0** | *(unset → 1)* | **0** | *(unset → 1)* |
| `supportedWounds` | 12 (organic set) | `IpcMechanicalDamageWound, ElectricalWound, SurgicalIncisionWound, DismembermentWound, AmputationConsequenceWound, SystemicBleedingWound` | `SlimeBlunt/Slash/Piercing/BurnWound, ElectricalWound, SurgicalIncisionWound, DismembermentWound, AmputationConsequenceWound, InternalBleedingWound, SystemicBleedingWound` | `CyberneticMechanicalDamageWound, ElectricalWound, CyberneticFrameFractureWound, SurgicalIncisionWound, DismembermentWound, AmputationConsequenceWound` | `PlantBlunt/Slash/Piercing/BurnWound, ElectricalWound, SurgicalIncisionWound, DismembermentWound, AmputationConsequenceWound, InternalBleedingWound, MedicalScarWound, SystemicBleedingWound` |
| `organDamage.chances` keys | Head/Chest/Groin/Arm/Hand/Leg/Foot (5/4/4/2/1/2/1 %) | **same 7 keys as Organic** | **Head/Chest/Groin only** (5/4/4 %) | *(none set)* | **Head/Chest/Groin only** (5/4/4 %) |
| fracture profile assigned to parts | `OrganicFractureProfile` | *(none — no bones)* | *(none — no bones)* | `CyberneticFractureProfile` | *(none — no bones)* |

Defaults come from `BodyPartProfilePrototype` itself (`ONYX Content.Shared/_Onyx/Wounds/WoundPrototype.cs:135-183`,
re-read in full): `Scarrable = true`, `CanFeelPain = true`, `PassiveRecoveryMultiplier = 1f`,
`BedRecoveryMultiplier = 1f`, `BleedingMultiplier = 1f`. **Ipc leaves `canFeelPain` unset** — Onyx's own
design keeps IPC pain feedback live; only Cybernetic prosthetics are pain-numb. This is not a Wolfgate
choice to make; it is what the pinned data says, and a test must catch a P5-1 author "fixing" it by adding
`canFeelPain: false` to Ipc by analogy with Cybernetic.

`CyberneticFractureProfile` (`wounds.yml:194-230`) is **byte-identical** to `OrganicFractureProfile`
(`:153-192`) except `wound: CyberneticFrameFractureWound` and the id — same four grade thresholds
(Hairline 20 / Simple 35 / Displaced 50 / Comminuted 60), same `accumulationMultiplier: 0.4`,
`minimumHitDamage: 3`, same `alert: BrokenBones`. Every existing organic fracture-grade assertion in
`WoundFractureTest.cs`/`WolfmedReagentTreatmentTest.cs` (e.g. "75 Blunt is the only deterministic fracture
grade, Comminuted, threshold 60, creationChance 1") transfers to Cybernetic **unchanged** — same numbers,
different wound id.

**Simplifying fact used throughout this plan:** every damage-type entry on every one of these wounds either
omits `severityMultiplier` (default `1f`, `WoundDamageTypeSettings.cs`, re-read) or sets it to `1`
explicitly. So for every species profile below, **severity == damage amount**, exactly like the organic
`BluntWound` derivation already established in `WolfmedReagentTreatmentTest.TreatmentCapabilityMatchHealsWoundTest`.

### 1.2 The mechanism these values actually flow through (WG, verified)

- `WoundableComponent.Profile` (`Content.Shared/_Onyx/Wounds/WoundDamageComponents.cs:160-161`) defaults to
  `"OrganicBodyPartProfile"` but is `EnsureComp`'d, not overwritten, by
  `WoundDamageProjectionSystem.SetupPart` (`Content.Shared/_Onyx/Wounds/WoundDamageProjectionSystem.cs:219-229`)
  — if a part's prototype YAML already declares `- type: Woundable` with an explicit `profile:`, `EnsureComp`
  finds it and does nothing. **This is the one line P5-1 needs per part-abstract**, exactly the pattern
  `mob-wiring.md` §1.2 already documented for Onyx.
- `PainSystem.CanFeelPain(EntityUid part)` (`Content.Shared/_Onyx/Wounds/PainSystem.cs:406-411`) reads
  `profile.CanFeelPain` off `WoundableComponent.Profile` and is called from the SAME `SetupPart` before the
  `EnsureComp<PainComponent>`/`RemComp<PainComponent>` branch (`WoundDamageProjectionSystem.cs:224-227`) — so
  a Cybernetic part never gets `PainComponent` at all, while an Ipc part does.
- `WoundBleedingSystem.TryGetBleedingMultiplier` (`Content.Server/_Onyx/Wounds/WoundBleedingSystem.cs:375-387`)
  reads `profile.BleedingMultiplier` off the same component; **`<= 0` disables bleeding outright** (forces
  `BaseRate`/`CurrentRate` to 0). None of the five shipped profiles use this, but P5-2's own DECISIONS text
  raises `bleedingMultiplier: 0` as a candidate for non-organic species — see §8.
- `WoundScarSystem.CreateScar` (`Content.Shared/_Onyx/Wounds/WoundScarSystem.cs:64-71`) refuses
  `if (... && !profile.Scarrable)`. Ipc/Slime/Cybernetic never scar; Plant does (default `true`).
- `WoundSystem`'s creation gate (`Content.Shared/_Onyx/Wounds/WoundSystem.cs:121`):
  `profile.SupportedWounds.Count == 0 || profile.SupportedWounds.Contains(prototype)` — a wound whose
  prototype the profile doesn't list is **silently never created** (`CreateOrMergeWound` returns `null`),
  not an exception. This is the gate every "wrong wound type for this species" assertion below relies on.
- `WoundFractureSystem.TryGetProfile` (`Content.Shared/_Onyx/Wounds/WoundFractureSystem.cs:145-155`) reads
  `FractureProfile` off **`WolfmedBodyPartComponent`** (`Content.Shared/_WF/Wolfmed/Body/WolfmedBodyPartComponent.cs:13`,
  the D8 shim), a **different component from `WoundableComponent.Profile`**. **A cybernetic part prototype
  must set both fields correctly and in agreement** — `WolfmedBodyPart.fractureProfile: CyberneticFractureProfile`
  *and* `Woundable.profile: CyberneticBodyPartProfile` (whose `SupportedWounds` must list
  `CyberneticFrameFractureWound`, per §1.1) — because `WoundFractureSystem.cs:57` calls
  `_wounds.CreateOrMergeWound(part, profile.Wound, ...)`, which routes through the §121 gate above. **If a
  P5-1 author sets `FractureProfile` but leaves the part on the default `OrganicBodyPartProfile`, fracture
  tracking silently never creates a wound** — `OrganicBodyPartProfile.SupportedWounds` doesn't list
  `CyberneticFrameFractureWound` either. This two-field-agreement trap gets its own test (§4, T-SP-CYBER-FRACTURE).
- `HealthChange.Effect` (`Content.Server/EntityEffects/Effects/HealthChange.cs:41-43,185-191`, HOOK 9) and
  `WoundDamageRoutingSystem.CanTreatPart` (`Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs:971-977`)
  are the **only** gate any reagent or item's healing goes through today: `profile.TreatmentCapabilities.Overlaps(capabilities)`.
  There is **no severity/stage awareness anywhere in this gate** — "stage gating" for a treatment item does
  not exist as a mechanism today. The one real precedent for "this cure only works up to a point" is
  `MendFractures.MinimumGrade`/`MaximumGrade` (already shipped, `Osteogen` vs `Stasizium` in
  `WolfmedReagentTreatmentTest.ShippedReagentsCarryTheRightEffectInTheRightGroupTest`) — see §3.4 for how
  this plan reuses it for Cybernetic frame fractures rather than inventing new machinery.

### 1.3 Wolfgate's species reality today (verified, matters for fixture design)

- **`MobIPC` is not a wound host today, and won't become one "for free."** `MobIPC`
  (`Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/ipc.yml:2`, `parent: PlayerSiliconHumanoidBase`)
  and `PlayerSiliconHumanoidBase` (`Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/silicon_base.yml:3-5`,
  `parent: [BaseMob, MobDamageable, MobCombat, MobAtmosExposed, MobFlammable]`) never touch
  `BaseMobSpeciesOrganic` (the phase-1 `WoundHost` choke point, `base.yml:239-252`). P5-1 must add
  `- type: WoundHost` directly on `MobIPC`, exactly as Onyx does on its own `MobIpc`
  (`ONYX Resources/Prototypes/Corvax/Body/Species/ipc.yml:96-101`, via `git show`).
- **Diona and Slime are already wound hosts today, with the wrong profile — a live, silent bug.**
  Both `diona.yml:4` and `slime.yml:3` parent `BaseMobSpeciesOrganic` (already true since phase 1), and
  `grep -rn "type: Woundable" Resources/Prototypes` finds **zero** hits anywhere in the tree. So every Diona
  and Slime mob spawned today gets `WoundHostComponent` and every part defaults to `OrganicBodyPartProfile`
  via `EnsureComp` (§1.2) — a Diona bleeds red blood, feels ordinary pain, can scar, and can break bones,
  none of which the shipped design intends once P5-1 lands. This is exactly `mob-wiring.md` §1.6/§2.3-A's
  flagged risk, now materialized. §4's T-SP-REGRESSION-* tests pin the *current* (wrong) behaviour and the
  *post-P5-1* (correct) behaviour as a before/after pair, so the fix is provably a fix.
- **Protogen is excluded, and nothing else is.** `WolfmedWoundHostExclusionSystem.ExcludedAncestors`
  (`Content.Shared/_WF/Wolfmed/Body/WolfmedWoundHostExclusionSystem.cs:12`) is `{"BaseMobProtogen"}` only.
  DECISIONS' phase-5 section explicitly leaves "decide whether protogen becomes a cybernetic wound host"
  open — see §8 decision 1.
- **No cybernetic prosthetic part prototype exists in Wolfgate at all.** `grep -rn "id:.*Cybernetic.*Arm\|id:.*Cybernetic.*Leg"
  Resources/Prototypes` finds only cosmetic sprite/marking/organ entries (`_Mono/Body/Organs/cybernetics.yml`,
  `_Shitmed/Species/cybernetics.yml`'s humanoid-sprite rows); `Content.Shared/_Shitmed/Cybernetics/CyberneticsComponent.cs`
  is a bare `Disabled` marker, not a body-part component. Onyx's own template
  (`ONYX Resources/Prototypes/_Onyx/Entities/Mobs/Customization/Parts/cybernetic.yml:1-56`, via `git show`)
  parents each cybernetic part on the corresponding **organic** organ prototype
  (`parent: [OrganHumanArmLeft, OnyxCyberneticPartBase]`) so it fits the same body-graph slot — the WG
  translation is `parent: [LeftArmHuman, <new cybernetic part base>]`. §3.3's mixed-profile test therefore
  declares its own `[TestPrototypes]` cybernetic-shaped part (mirroring the established pattern in
  `WolfmedAmputationTest.cs`'s bespoke parts) rather than depending on a real P5-1 prototype id that doesn't
  exist yet; §5 also asks for one lightweight smoke test against whatever real id P5-1 ships.
- **`Inorganic` (IPC/cybernetic parts' damage container) does not support Cold or Caustic — the same gap
  phase 1 found for organic parts.** `BasePartInorganic` (`Resources/Prototypes/Body/Parts/base.yml:9-15`)
  sets `damageContainer: Inorganic`; `Inorganic` (`Resources/Prototypes/Damage/containers.yml:9-15`) supports
  only `Brute` (Blunt/Slash/Piercing, `Resources/Prototypes/Damage/groups.yml:1-7`) plus `Heat`/`Shock`. Both
  `IpcBodyPartProfile.acceptedDamageTypes` and `CyberneticBodyPartProfile.acceptedDamageTypes` list `Cold`
  and `Caustic`, which the container silently drops before the wound layer ever sees them (`DamageableComponent`
  only tracks types its container supports). §4's T-SP-CONTAINER-GAP canary documents this exactly like the
  phase-1 organic-Caustic gap (`mob-wiring.md` §0.5) was documented rather than silently ported around.
- **The locale is already 100% complete for all five profiles — nothing to port here.**
  `Resources/Locale/en-US/_Onyx/prototypes/wounds/wounds.ftl` already carries **43 keys** (verified: `grep -c`),
  including `wound-name-ipc-mechanical-damage`, `wound-name-cybernetic-mechanical-damage`,
  `wound-name-cybernetic-frame-fracture`, all four `wound-name-slime-*`, all four `wound-name-plant-*`, and all
  four `wound-stage-mechanical-*` — every key the 11 not-yet-shipped wound prototypes will need was ported in
  an earlier phase even though nothing references it yet. §4's T-AN-MECH-TEXT test can therefore assert real
  resolved strings, not just that a `LocId` was constructed.
- **`BloodstreamComponent.BloodReagent` already exists and is overridable per-prototype**
  (`Content.Server/Body/Components/BloodstreamComponent.cs:143`, `ProtoId<ReagentPrototype> BloodReagent = "Blood"`).
  This is the cheap route DECISIONS' P5-2 asks for: `MobIPC` can set `- type: Bloodstream / bloodReagent: <OilId>`
  (Onyx's own `MobIpc` does the equivalent with `bloodReferenceSolution.reagents[0].ReagentId: Oil`,
  `ONYX Resources/Prototypes/Corvax/Body/Species/ipc.yml:102-106`, via `git show` — that exact field doesn't
  exist on WG's server-only `BloodstreamComponent`, but `BloodReagent` gives the same visible result for a
  fraction of the cost) — **without** touching `bleedingMultiplier` or porting the circulatory-stream
  metabolizer rewrite `circulation.md` ruled out of scope. No reagent named `Oil`/`Coolant` exists in WG yet
  (`grep -rln "id: Oil$\|id: Coolant$" Resources/Prototypes/Reagents` → none) — a new reagent or reuse of an
  existing lubricant-flavoured one is a P5-2 content decision, not a test-plan one. See §8 decision 2.

### 1.4 Suite conventions this plan follows (from `Content.IntegrationTests/Tests/_Onyx/Wounds`, `Tests/_WF/Wolfmed`, 98 tests read)

- `[TestFixture]` + `GameTest` base, `[TestPrototypes] private const string Prototypes = @"..."` blocks,
  `Pair.Server` / `server.WaitAssertion(() => {...})`, `Pair.CreateTestMap()`. No UI driving.
- **`[TestPrototypes]` ids are a single global pool across the whole suite** (PLAN2 §4 rule 3, PLAN3 §4 rule
  3) — every new id below was checked against `grep -rhn "^  id: Wolfmed" Content.IntegrationTests/Tests/_WF/Wolfmed/*.cs`
  (67 existing ids, listed in full during this analysis) and is clear. Re-grep at implementation time.
- **Every expected value is derived from a cited prototype field, never assumed** — this plan follows suit;
  every table cell in §4 either cites the Onyx source line above or an existing WG mechanism.
- **Known dice-roll traps, and a new one this phase adds:**
  - P2-D23 (fracture creation is probabilistic below Comminuted) — reused unchanged for Cybernetic (§1.1).
  - **New for phase 5**: `WoundBleedingBehavior.Chance` defaults to `1f`
    (`Content.Shared/_Onyx/Wounds/WoundBehaviors.cs:29-30`) and is **omitted** (thus deterministic) on every
    Slash/Piercing-family wound in every profile (`SlashWound`, `SlimeSlashWound`, `SlimePiercingWound`,
    `PlantSlashWound`, `PlantPiercingWound` — none of their stage-level `WoundBleedingBehavior` blocks set
    `chance:`), but is **explicitly overridden to 0.25–0.7 per stage** on every Blunt-family wound
    (`BluntWound`, `SlimeBluntWound`, `PlantBluntWound`). **Use a Slash or Piercing hit, never a Blunt hit,
    for any deterministic "this wound bleeds" assertion on Slime/Plant** — exactly the existing convention
    already used for organic bleeding in `WolfmedAnalyzerTest.Bleed` (comment: "SlashWound's bleeding
    behaviour is `minimumSeverity: 9`"). `IpcMechanicalDamageWound`/`CyberneticMechanicalDamageWound` bleed
    at the wound's top-level `behaviors:` (not nested per-stage) with `chance: 1` explicitly — deterministic
    from the first point of damage, no minimum severity gate at all, so those need no such care.

---

## 2. Derived per-profile behaviour matrix (what each species/limb-type will show a player)

| | Organic (control) | IPC | Cybernetic limb | Slime | Plant (Diona) |
|---|---|---|---|---|---|
| Routes to a body part | yes (shipped) | yes (P5-1) | yes (P5-1) | yes (already true — wrong profile until P5-1) | yes (already true — wrong profile until P5-1) |
| Bleeds | yes, blood | **yes**, rate ×1 (reagent TBD, §8) | **yes**, rate ×0.5 (coolant) | yes, rate ×1.15 | yes, rate ×1 |
| Feels pain | yes | **yes** | **no** | yes | yes |
| Can scar | yes | no | no | no | **yes** |
| Can fracture bones | yes (`BoneFractureWound`) | no | **yes** — `CyberneticFrameFractureWound`, same grade thresholds | no | no |
| Wound family on a Blunt hit | `BluntWound` | `IpcMechanicalDamageWound` | `CyberneticMechanicalDamageWound` | `SlimeBluntWound` | `PlantBluntWound` |
| Treatable by | Biological items | Mechanical **and** Electrical items | Mechanical **and** Electrical items | Biological items | Biological items |
| Organ damage reaches | Head/Chest/Groin/Arm/Hand/Leg/Foot | same 7 (data present; **no organ instrumented yet**, §3.6) | n/a (no organs) | Head/Chest/Groin only (data present; **no organ instrumented yet**) | Head/Chest/Groin only (same gap) |
| Passive/bed healing | full | **none** (multiplier 0) | **none** (multiplier 0) | full | full |

---

## 3. Test groups and rationale

### 3.1 One wound-host test per new species profile (routing / bleeding / pain / fracture)

Four tests, one per profile, in a new file `WolfmedSpeciesProfileTest.cs`. Each spawns a real species mob
(`MobIPC`, a synthetic Cybernetic-profile part on a Shitmed graph, `MobSlimePerson`, `MobDiona` — the last two
using the **real, already-shipped** species prototypes, since their body graphs and parts already exist; only
IPC and Cybernetic need any new prototype at all) and asserts the full matrix row: which wound prototype a
Blunt/Slash hit creates, whether a `WoundBleedingComponent` attaches (using the Slash-family determinism from
§1.4), whether `PainComponent` exists on the part at all (not just whether pain is zero — `CanFeelPain(part) == false`
means `RemComp<PainComponent>` runs, so `HasComponent` is the right assertion, not `GetPain() == 0`), whether
a scar can be created, and — for Organic/Cybernetic only — the fracture grade at the same deterministic 75-Blunt
hit already established in `WolfmedReagentTreatmentTest.MendFracturesReducesMatchingFractureTest`.

### 3.2 Regression: Diona/Slime's current mis-profile (before/after P5-1)

Two tests (or two assertions within one test, run against `main`/pre-P5-1 to prove the bug, then re-run
post-P5-1 to prove the fix) — spawn `MobDiona`/`MobSlimePerson` today, hit a part with Blunt, and assert the
wound created is `BluntWound` (today's wrong answer) with a `WoundBleedingComponent` present and pain nonzero.
**This test is written to fail once P5-1 lands** (the wound should become `PlantBluntWound`/`SlimeBluntWound`)
— it exists to make P5-1's fix a visible, deliberate test change (the same pattern `T-AMP-CONSEQUENCE-SEPARATE`
used to replace a superseded test in phase 3), not a silent behaviour shift nobody notices.

### 3.3 Mixed profiles on one host: a cybernetic limb on an organic body

One test. A real `MobHuman` wound host has its left arm detached (`WolfmedBodySystem.TryDetachPart`, already
used in `WolfmedAnalyzerTest.TreatmentDisappearsFromTheReadoutTest`) and replaced
(`SharedBodySystem.AttachPart`, already used in `WoundDamageFoundationTest`'s reattach path) with a bespoke
`[TestPrototypes]` part carrying `- type: Woundable / profile: CyberneticBodyPartProfile` and
`- type: WolfmedBodyPart / fractureProfile: CyberneticFractureProfile`, parented to `LeftArmHuman` exactly as
Onyx parents its own `LeftArmCybernetic` on `OrganHumanArmLeft` (§1.3). The test then hits **both** arms with
identical damage and asserts each produces its own profile's wound family, only the organic arm can scar or
feel pain suppressed by `SuppressPain`, only the cybernetic arm resists `passiveRecoveryMultiplier`-driven
healing, and — the two-field-agreement trap from §1.2 — that the cybernetic arm's fracture is a
`WoundFractureComponent` wrapping `CyberneticFrameFractureWound`, not silently absent. Also asserts the
body-level `PainComponent.Pain` (summed by `WoundDamageProjectionSystem.RefreshBodyPain`,
`Content.Shared/_Onyx/Wounds/WoundDamageProjectionSystem.cs:230-234`) equals the organic arm's pain alone,
proving the cybernetic arm contributes zero rather than merely "not tested."

### 3.4 Treatment-capability matrix (≥3 items × 2 wound types), including "stage gating"

Reuses the exact, already-proven `HOOK 9` mechanism from `WolfmedReagentTreatmentTest.cs` (§1.2) rather than
guessing at unshipped P5-3 machinery — the underlying gate (`profile.TreatmentCapabilities.Overlaps(...)`)
cannot change shape without also breaking phase 4's tests, so this matrix is low-risk regardless of exactly
which cable-coil/welder prototypes P5-3 ships. Three synthetic "items" (constructed the same way
`WolfmedReagentTreatmentTest.Args` builds a full-strength effect call, no chemistry needed) × two wound types:

| | `BluntWound` on an Organic arm | `IpcMechanicalDamageWound` on an IPC part |
|---|---|---|
| Biological item (`TreatmentCapabilities:[Biological]`) | **heals** | no-op |
| Mechanical item (`[Mechanical]`, stands in for a cable coil) | no-op | **heals** |
| Electrical item (`[Electrical]`, stands in for a welder/jumper) | no-op | **heals** (Ipc profile lists both Mechanical and Electrical) |

"Stage gating" has no existing per-severity item mechanism (§1.2) — the only shipped precedent is
`MendFractures.MinimumGrade`/`MaximumGrade` (Osteogen vs. Stasizium). This plan's recommended, lowest-risk
reading: a P5-3 "nanite repair kit" style item that mends `CyberneticFrameFractureWound` the same way
Osteogen mends `BoneFractureWound`, capped `MaximumGrade: Simple` — i.e. it fixes a Hairline/Simple frame
fracture but a Displaced/Comminuted one still needs a real surgeon. The test
(`CyberneticFractureGradeGatesTheRepairKitTest`) is a direct transplant of
`WolfmedReagentTreatmentTest`'s existing Osteogen-vs-Stasizium logic onto the Cybernetic wound id — see §4.
If P5-3 instead adds a genuine severity/stage cap directly on `HealthChange` (a new field), the same test
shape (drive the wound to a stage, assert the capped item refuses, assert the uncapped one doesn't) still
applies; only the field name changes.

### 3.5 IPC and Protogen spawn-and-delete, plus the existing smoke filter

The generic `EntityTest.SpawnAndDeleteAllEntitiesOnDifferentMaps` (`Content.IntegrationTests/Tests/EntityTest.cs:24`)
already spawns and deletes every entity prototype in the game, `MobIPC`/`MobProtogen` included, and asserts
only "did not throw." It already covers whatever new cybernetic part prototypes P5-1 adds automatically,
with zero new code. What it does **not** check is anything wound-specific. A new, small
`WolfmedSpeciesSpawnTest.cs` adds the species-specific half: spawn `MobIPC`, tick once, assert
`WoundHostComponent` is present and every part resolves `IpcBodyPartProfile`; spawn `MobProtogen`, assert
`WoundHostComponent` is **absent** (or, if §8 decision 1 goes the other way, present with a specific
profile); delete both without an exception. Then re-run the existing smoke filter
(`--filter "FullyQualifiedName~EntityTest|FullyQualifiedName~PrototypeSaveTest|FullyQualifiedName~DockTest"`)
unmodified — it is a regression gate, not something phase 5 needs to add rows to.

### 3.6 Analyzer diagnostic text for a mechanical wound

`HealthAnalyzerSystem.BuildWoundDiagnostics` (`Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs:30-104`,
HOOK 23/P4-D26, already `public` for exactly this reason) builds `HealthAnalyzerVisibleWound(LocId Name, LocId? StageName, int Count)`
per distinct (wound, stage) pair (`:101-103`) via `prototype.GetStageDefinition(wound.Comp.Severity)?.Name`
(`Content.Shared/_Onyx/Wounds/WoundPrototype.cs:76`). A new test on a mechanical (IPC or Cybernetic) wound
host asserts the server-computed message contents directly: creating an `IpcMechanicalDamageWound` at
severity 30 (Moderate: threshold 25, §1.1's IPC row) produces exactly one `HealthAnalyzerVisibleWound` with
`Name == "wound-name-ipc-mechanical-damage"` and `StageName == "wound-stage-mechanical-moderate"` — and,
because the locale is already complete (§1.3), resolving both `LocId`s through `ILocalizationManager` proves
real text ("chassis damage" / "moderate", per the shipped `.ftl`), not a missing-key placeholder. This closes
the loop the phase-2 `wounds.ftl` deletion trap opened: a locale regression here would be silent otherwise.

### 3.7 Pain-numbness / narcotics (conditional on P5-4 shipping)

`reagents.md` §8 already fully specced this (from phase 4, deferred): `ModifyStatusEffect` old-style effect
(~45 LOC), two new prototypes (`PainNumbnessStatusEffectBase`, `StatusEffectPainNumbness`), and **one test**
— confirmed still true, nothing in P5-1..P5-3 touches this chain. If P5-4 ships it (DECISIONS: "if any
ported or Wolfgate reagent should grant pain numbness"), add `WolfmedPainNumbnessStatusTest.cs` (a **new**
file — do not reuse `WolfmedPainTest.cs`'s existing `WolfmedPainNumbBody` id or its trait-based mechanism,
which is a completely different code path, `PainNumbnessComponent` vs. `PainNumbnessStatusEffectComponent`)
asserting `PainSystem.IsPainNumb` flips to `true` while `StatusEffectPainNumbness` is applied via the new
effect and back to `false` once it expires, and that `GetPain` reads zero meanwhile while `GetRawPain` is
unaffected (mirroring `SuppressPainLowersEffectivePainTest`'s existing Get/GetRaw split). **If P5-4 records
this as skipped** (as P4-5 did), this test is **not written**; §4 lists it as conditional and §8 flags the
decision explicitly rather than silently omitting it from the plan.

### 3.8 Damage-container gap canary

One test (or an assertion folded into 3.1's IPC/Cybernetic rows): apply Cold and Caustic damage to an IPC or
Cybernetic part and assert the part's `DamageableComponent.Damage.DamageDict` gains **no** entry for either
type (today's `Inorganic` container behaviour, §1.3) — a canary against the profile's `acceptedDamageTypes`
silently promising something the container can't deliver. If P5-1 fixes the container (adding `Cold`/`Caustic`
to `Inorganic.supportedTypes`, the same fix phase 1 flagged for `OrganicPart`), this test is expected to flip
and should be updated deliberately, not left red.

---

## 4. Full test-by-test table

Every "derived value" cites §1/§2 above; none is assumed. File column names the **new** or **extended** file;
"Depends on" names the phase-5 work package the test cannot run before (using WP13-N as a placeholder
numbering — phase 5 has no WP numbers assigned yet; PLAN5's author should renumber to match whatever scheme
it adopts).

| # | Test | File | Setup | Assertions (derived values) | Depends on |
|---|---|---|---|---|---|
| 1 | `IpcPartRoutesBleedsAndFeelsPainTest` | `WolfmedSpeciesProfileTest.cs` (new) | Spawn `MobIPC`; `routing.TryApplyPartDamage(body, part, Spec("Slash", 20))` on a limb (Slash, not Blunt — §1.4 determinism) | Wound created is `IpcMechanicalDamageWound` at severity 20 (severityMultiplier 1, §1.1); `HasComponent<WoundBleedingComponent>` true (mechanical wounds bleed at wound-level `chance:1`, no minimum severity); `HasComponent<PainComponent>(part)` true; `WoundScarSystem.CreateScar` returns `null` (Ipc `scarrable:false`) | P5-1 (WoundHost on `MobIPC`, `IpcBodyPartProfile` on IPC parts) |
| 2 | `CyberneticPartRoutesBleedsFeelsNoPainAndFracturesTest` | same | Bespoke `[TestPrototypes]` cybernetic part (§3.3's fixture) in a `WolfmedCyberneticBodyGraph`; Slash 20 for wound/bleed/scar; separately, Blunt 75 for fracture (deterministic Comminuted, §1.1) | Wound is `CyberneticMechanicalDamageWound`; `HasComponent<WoundBleedingComponent>` true; `HasComponent<PainComponent>(part)` **false** (`CanFeelPain` gate, §1.2); scar refused; after 75 Blunt, `WoundFractureSystem.GetFracture(part)!.Value.Comp2.Grade == FractureGrade.Comminuted` **and** the fracture's `Comp1` wraps a `CyberneticFrameFractureWound` (the two-field-agreement assertion, §1.2/§3.3) | P5-1 |
| 3 | `SlimePartRoutesAndBleedsAtIncreasedRateTest` | same | Spawn `MobSlimePerson` (real, shipped species); `SlimeSlashWound`-eligible hit (Slash, deterministic) | Wound is `SlimeSlashWound`; bleeding present; `HasComponent<PainComponent>` true; scar refused (`scarrable:false`); **no** `WoundFractureComponent` ever appears regardless of Blunt amount (no `fractureProfile` assigned to slime parts) | P5-1 (`Woundable/profile: SlimeBodyPartProfile` on slime parts) |
| 4 | `PlantPartRoutesBleedsAndScarsTest` | same | Spawn `MobDiona` (real, shipped species); `PlantSlashWound`-eligible hit | Wound is `PlantSlashWound`; bleeding present; pain present; `WoundScarSystem.CreateScar` **succeeds** (`scarrable:true`, the one species besides Organic that can scar); no fracture ever, any Blunt amount | P5-1 (`Woundable/profile: PlantBodyPartProfile` on diona parts) |
| 5 | `DionaCurrentlyMisclassifiedAsOrganicTest` (regression, run pre-P5-1) | same | Spawn `MobDiona` **today**; Blunt hit | Wound created is `BluntWound` (not `PlantBluntWound`) — documents the live bug (§1.3, §3.2). Comment: expected to start failing the moment P5-1 lands; that failure is the fix, update the test then, don't skip it silently |
| 6 | `SlimeCurrentlyMisclassifiedAsOrganicTest` (regression, run pre-P5-1) | same | Spawn `MobSlimePerson` today; Blunt hit | Wound created is `BluntWound`, `WoundFractureSystem` can create a `BoneFractureWound` on a slime at 75 Blunt (should be impossible post-fix) | none (documents today) |
| 7 | `CyberneticLimbOnOrganicBodyKeepsProfilesSeparateTest` | same | Real `MobHuman`; detach left arm (`WolfmedBodySystem.TryDetachPart`); attach the §3.3 bespoke cybernetic part via `SharedBodySystem.AttachPart`; hit both arms identically (Slash 20, then Blunt 75 each) | Left (cybernetic) arm: `CyberneticMechanicalDamageWound`, no `PainComponent`, `CyberneticFrameFractureWound` at 75 Blunt. Right (organic) arm: `BluntWound`/`SlashWound`, `PainComponent` present and nonzero, `BoneFractureWound` at 75 Blunt. Body-level `PainComponent.Pain` (`RefreshBodyPain` sum) equals the right arm's own raw pain exactly — the cybernetic arm's contribution is 0, not merely small | P5-1 |
| 8 | `TreatmentCapabilityMatrixTest` | `WolfmedTreatmentMatrixTest.cs` (new) | Organic wound host + IPC wound host; three synthetic `HealthChange`-style calls (`TreatmentCapabilities = [Biological]`/`[Mechanical]`/`[Electrical]`) against a `BluntWound` and an `IpcMechanicalDamageWound` | The 6-cell table in §3.4: Biological heals only the organic wound; Mechanical and Electrical each heal only the IPC wound; every mismatched cell leaves severity and `GetAllDamage` unchanged (mirrors `TreatmentCapabilityMismatchDoesNotHealTest`'s exact pattern) | P5-1 (IPC profile) — no new P5-3 prototype required, since this exercises the gate directly |
| 9 | `CyberneticFractureGradeGatesTheRepairKitTest` | same | Cybernetic-profile part (§3.3 fixture) driven to Comminuted (75 Blunt, deterministic); `new MendFractures { Wounds = ["CyberneticFrameFractureWound"], MaximumGrade = FractureGrade.Simple, Amount = 5 }.Effect(args)`, then the same with `Wounds = []`, `MaximumGrade = Comminuted` | The `Simple`-capped call is a no-op on a Comminuted fracture (severity unchanged) — the "stage gating" case; the uncapped call reduces severity by 5, mirroring `MendFracturesReducesMatchingFractureTest`'s Osteogen-vs-Stasizium logic exactly, retargeted at the Cybernetic wound id | P5-3 (or: exercises only shipped `MendFractures`, so it can run the moment P5-1's Cybernetic profile lands, ahead of any real P5-3 item existing) |
| 10 | `IpcSpawnAndDeleteDoesNotThrowTest` | `WolfmedSpeciesSpawnTest.cs` (new) | `entities.SpawnEntity("MobIPC", map.GridCoords)`, tick once, then `entities.DeleteEntity(body)` | No exception; `HasComponent<WoundHostComponent>` true; every part's `WoundableComponent.Profile == "IpcBodyPartProfile"` | P5-1 |
| 11 | `ProtogenSpawnAndDeleteDoesNotThrowTest` | same | `entities.SpawnEntity("MobProtogen", map.GridCoords)`, tick, delete | No exception; `HasComponent<WoundHostComponent>` matches whatever §8 decision 1 resolves to (absent if excluded, present with the agreed profile if not) | P5-1 (decision-dependent) |
| 12 | *(regression)* re-run `EntityTest\|PrototypeSaveTest\|DockTest` | n/a — existing suite | `dotnet test ... --filter "FullyQualifiedName~EntityTest\|FullyQualifiedName~PrototypeSaveTest\|FullyQualifiedName~DockTest"` | Same pass count as phase 4's baseline (content.md: 9 passed, 2 permanent `[Ignore]`s) plus no new failures from the cybernetic part prototypes | P5-1 (new prototypes exist) |
| 13 | `MechanicalWoundDiagnosticTextResolvesTest` | `WolfmedAnalyzerTest.cs` (extend) | IPC (or Cybernetic) wound host; `wounds.CreateOrMergeWound(part, "IpcMechanicalDamageWound", 30)` (Moderate stage, threshold 25, §1.1); `analyzer.BuildWoundDiagnostics(body)` | Exactly one `HealthAnalyzerVisibleWound` with `Name == "wound-name-ipc-mechanical-damage"`, `StageName == "wound-stage-mechanical-moderate"`, `Count == 1`; `_loc.GetString(name)/(stageName)` resolve to non-empty text different from the raw key (`"chassis damage"`/`"moderate"` per the shipped `.ftl`, §1.3) | P5-1 |
| 14 | `IpcAndCyberneticIgnoreColdAndCausticDamageTest` | `WolfmedSpeciesProfileTest.cs` | IPC and Cybernetic parts; `damage.TryChangeDamage(part, Spec("Cold", 10))` and `Spec("Caustic", 10)` directly on the part's `DamageableComponent` | `DamageableComponent.Damage.DamageDict` gains no `Cold`/`Caustic` key on either part — the `Inorganic` container gap (§1.3/§3.8). Comment: expected to flip if P5-1 also extends `Inorganic.supportedTypes`; update deliberately, don't leave red silently | P5-1 |
| 15 | `PainNumbnessStatusEffectTest` *(conditional)* | `WolfmedPainNumbnessStatusTest.cs` (new, only if P5-4 ships §3.7) | Apply the new `ModifyStatusEffect{ EffectProto = "StatusEffectPainNumbness" }` to a wound host in pain | `PainSystem.IsPainNumb(body)` flips true while applied, `GetPain` reads 0, `GetRawPain` unaffected; flips back false after expiry | P5-4, only if not recorded as skipped |

**27 assertions across 15 tests** (T-14 and T-8 each bundle several sub-assertions per the tables above,
matching this suite's existing density — e.g. `WolfmedAnalyzerTest`'s single `ChottingPhaseIsClassifiedTest`
already carries 5 sub-cases). Test count: **13 unconditional + 2 conditional** (T-11 depends only on which way
§8 decision 1 goes, not whether it ships at all; T-15 depends on whether P5-4 ships).

---

## 5. File placement

| File | Status | Owner (suggested WP) |
|---|---|---|
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedSpeciesProfileTest.cs` | new | P5-6 test WP (tests 1-7, 14) |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedTreatmentMatrixTest.cs` | new | P5-6 test WP (tests 8-9) |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedSpeciesSpawnTest.cs` | new | P5-6 test WP (tests 10-11) |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedAnalyzerTest.cs` | extended | P5-5's WP (test 13 — same file/class already `[TestOf(typeof(HealthAnalyzerSystem))]`) |
| `Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedPainNumbnessStatusTest.cs` | new, conditional | P5-4's WP, only if taken (test 15) |

Per this suite's serialisation convention (PLAN2 §4, PLAN4 §4: one owner per test file, sequential packages),
tests 1-7/14 (species profile mechanics) and tests 8-9 (treatment matrix) are natural candidates for the
**same** WP if P5-1 and P5-3 land in the same package, since 1-7/14 cannot compile without P5-1's new
`bodyPartProfile`/`Woundable` YAML and 8-9 need only P5-1 (not P5-3, as noted in the table) — but keeping
them in separate files still lets a P5-3-only package add test 9's real-item smoke test later without
touching the P5-1 file.

---

## 6. Run commands

```bash
# Full Wolfmed suite, once phase 5 lands (mirrors phase 4's filter, unchanged):
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt \
  --filter "FullyQualifiedName~_Onyx.Wounds|FullyQualifiedName~_Onyx.Body|FullyQualifiedName~_Onyx.Medical|FullyQualifiedName~Wolfmed"

# Smoke regression (unmodified from phase 4):
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt \
  --filter "FullyQualifiedName~EntityTest|FullyQualifiedName~PrototypeSaveTest|FullyQualifiedName~DockTest"

# Prototype/locale sanity (catches a bad !type:, a missing bodyPartProfile field, a duplicate .ftl key):
dotnet run --project Content.YAMLLinter -c Release

# Headless server boot, watching for [ERRO]/[FATL] on any package touching a prototype/locale file
# (per-project-memory convention: run DockTest first every package).
```

A worktree needs `RobustToolbox` junctioned from the main checkout before building (see `WOLFMED_STATUS.md`'s
"How to verify" section, unchanged for phase 5).

---

## 7. Traps specific to this phase (beyond §1.4's dice-roll notes)

1. **The two-field agreement for fractures (§1.2, §3.3, §4 test 2/7).** Setting `WolfmedBodyPart.fractureProfile`
   without also setting `Woundable.profile` to a profile whose `SupportedWounds` includes that fracture's
   wound id is a **silent no-op**, not a crash — `CreateOrMergeWound` just returns `null`. Every cybernetic
   fixture in this plan asserts the fracture wound actually exists, not just that damage accumulated.
2. **IPC keeps `canFeelPain` at its default (`true`)** — do not "fix" this by analogy with Cybernetic. Test 1
   pins it explicitly so a future edit that adds `canFeelPain: false` to `IpcBodyPartProfile` fails loudly.
3. **Blunt-family wounds roll bleeding chance; Slash/Piercing-family wounds don't** (§1.4). Every
   bleeding-presence assertion in this plan uses Slash or Piercing damage. A future test author copying the
   pattern for a Blunt hit will get an intermittent failure, not a design bug.
4. **The regression tests (4, 5 in the table) are deliberately written to fail once the bug they document is
   fixed.** They must be updated (not silently deleted) the moment P5-1 lands, mirroring how
   `T-AMP-CONSEQUENCE-SEPARATE` explicitly replaced a superseded phase-3 test rather than quietly vanishing.
5. **`WolfmedPainNumbBody` (phase 2) and any new pain-numbness status-effect fixture (§3.7) are unrelated
   mechanisms with similar names** — `PainNumbnessComponent` (trait, already live) vs.
   `PainNumbnessStatusEffectComponent` (status effect, dead code today, §3.7). Do not reuse the phase-2
   fixture id or assume the phase-2 test's assertions transfer.
6. **Organ-damage coverage for IPC/Slime/Plant is a separate, larger gap than this plan closes.**
   `OrganDamageComponent`/`WolfmedOrganComponent` exist on exactly seven `OrganHuman*` ids and nowhere else
   (P3-D7); IPC's `OrganIPCPump`/`PositronicBrain`/`OrganIPCEyes` and every Slime/Diona organ carry neither.
   The `organDamage.chances` dictionaries in §1.1 are therefore currently **inert data** for these species —
   this plan does not add organ-level tests for them (P5-1's own scope note already says organ-damage
   coverage is bundled with the profile work, "currently silent no-ops"); if P5-1 does *not* instrument these
   organs, the honest test is a **regression canary** ("heavy torso damage on an IPC never touches
   `OrganIPCPump`'s health, because it has none"), not a positive organ-damage test. Recommend adding that
   canary (cheap, ~10 lines) rather than silently leaving organ coverage untested either way — see §8
   decision 3.

---

## 8. Decisions the user must make

1. **Does Protogen become a wound host at all in phase 5, and if so with which profile?**
   DECISIONS' own P5-1 text leaves this open. Recommendation: **leave Protogen excluded for phase 5**
   (zero new risk, one line already correct) and revisit only if the user specifically wants Protogen limbs
   to take localized damage; test 11 is written to assert whichever way this goes, so it costs nothing to
   defer.
2. **How does IPC/Cybernetic bleeding actually look — literal red blood (Onyx's own unfixed gap, since even
   Onyx's `bloodReferenceSolution.Oil` field has no direct WG equivalent) or a real Oil/Coolant reagent via
   the already-existing `BloodstreamComponent.BloodReagent` override (§1.3)?**
   Recommendation: **take the cheap `bloodReagent:` override** — a few lines, zero new systems, visibly
   correct ("IPC bleeds oil, not blood"), and it doesn't block on the circulatory-stream metabolizer rewrite
   `circulation.md` already ruled out of scope for this phase. If taken, this plan's bleeding tests should
   additionally assert the puddle/solution reagent id, not just that `BleedAmount` rises — flag to whichever
   WP implements P5-2 so the test gets that one extra line.
3. **Do IPC/Slime/Plant organs get real `OrganDamage`/`WolfmedOrgan` instrumentation in phase 5, or does that
   stay a documented gap?** (§7 trap 6.) Recommendation: **document as a gap, add the cheap regression
   canary, defer real coverage** — P3-D7 already limited organ damage to human-lineage species for
   cost reasons that apply equally here (each species needs its own per-organ YAML, and IPC/Slime/Diona
   organs live in three different fork namespaces), and nothing in DECISIONS' phase-5 scope commits to it
   beyond "including their organ-damage coverage" in one summary sentence — worth a direct confirmation
   before a WP spends a full package on it.
4. **Does P5-4 (pain numbness/narcotics) ship at all?** If skipped (as P4-5 was, for the same reasons —
   no in-scope reagent needs it, the trait path already works), test 15 is simply never written; say so
   explicitly in `WOLFMED_STATUS.md` rather than leaving a silent gap in the count.

---

## 9. Blockers

None that block writing or running this test plan once P5-1 lands. The two structural blockers this report
surfaces are **implementation** blockers for P5-1/P5-2/P5-3, not test-authoring ones, and are already
reflected as explicit test assertions above rather than as open questions:

- The `Inorganic` damage container's missing `Cold`/`Caustic` support (§1.3, test 14) — mirrors the
  already-known phase-1 `OrganicPart`/Caustic gap; same fix, same decision shape, deferred to P5-1.
- The fracture two-field agreement (§1.2, tests 2/7) — not a blocker so much as a footgun that needs one
  sentence in whichever WP writes the Cybernetic part prototypes, and a test that catches it if missed.
