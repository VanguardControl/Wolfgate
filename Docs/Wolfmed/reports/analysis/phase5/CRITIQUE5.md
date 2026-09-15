# CRITIQUE5 — completeness critic on PLAN5

**Role:** completeness critic. Everything below was re-verified by grepping the real files at
`WG = C:/Users/jzo12/Documents/GitHub/Wolfgate/.claude/worktrees/rules-motd-updates-11c89c` (HEAD
`2b4a4675d0 phase 4`) and `ONYX = C:/tmp/onyx @ 2f5bab9`. Nothing here is taken from PLAN5 or from
`species.md` / `circulation.md` / `capabilities.md` / `numbness.md` / `tests.md` on their word.

**Verdict.** PLAN5 is a strong plan — its central mechanism claims (RT first-parent-wins, the
`treatmentCapabilities` leak, the mandatory `WeldingHealing` companion edits, the LocId completeness,
`amputationThresholds: {}`) all survive independent verification. But it has a **systematic blind spot: it
reasons about `Destructible`/`GibPartBehavior` from the wrong parent in three places.** Wolfgate's organic
limbs *do* carry gib triggers (Mono's `MajorLimb`/`MinorLimb`), which PLAN5 states they do not. That single
mis-read produces two blockers, invalidates one user decision's framing (U3), makes one upstream edit
(PROTO R) a no-op, and makes the diona row of the player-experience table wrong.

- 2 blockers, 7 major, 8 minor.
- 1 contradiction with DECISIONS.md: none material — but §8.4-7's "cable coil first in phase 5" is honoured,
  and P5-2's "cheapest route that gives *correct* behaviour" is arguably violated by B7.
- The phase-1 "stage-based metabolizer" blocker really is void (independently re-verified, §V-8).

---

## 1. BLOCKERS

### B1 — Cybernetic limbs DO have gib triggers. PLAN5 §2.2 asserts they do not, and ships the profile on that premise.

**PLAN5 claim** (`§2.2`, comment on `WolfmedPartCybernetic`):
> `# amputationThresholds inherit Base<Slot>'s organic set. Onyx's cybernetic limbs reuse its IPC numbers;`
> `# same reasoning as WolfmedPartIpc applies, and cybernetic limbs carry no gib trigger at all`
> `# (CyberneticPartBase -> BasePartInorganic, which declares no Destructible).`

**Evidence.** `CyberneticPartBase` is only *one* of two parents. Every concrete cybernetic limb takes the
other parent's `Destructible`:

`Resources/Prototypes/_Shitmed/Body/Parts/cybernetic.yml:16-19`
```yaml
- type: entity
  abstract: true
  parent: [ CyberneticPartBase, BaseLeftArm ]
  id: LeftArmCyberneticBase
```
`Resources/Prototypes/Body/Parts/base.yml:137` — `BaseLeftArm: parent: [MajorLimb, WolfmedBaseLeftArm]`, and
`Resources/Prototypes/Body/Parts/base.yml:325-360` (`MajorLimb`, id at `:276`):
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
        damage: 250 # 200->230
      behaviors:
      - !type:SpawnEntitiesBehavior
        spawnInContainer: true
        spawn:
          Ash: { min: 1, max: 1 }
      - !type:BurnBodyBehavior { }
      - !type:PlaySoundBehavior
        sound: { collection: MeatLaserImpact }
```
`MinorLimb` (`:313`) is the same shape at Blunt 150 / Slash 180 for hands and feet.

`CyberneticPartBase` declares **no `Destructible` key at all** (`_Shitmed/Body/Parts/cybernetic.yml:1-14`,
verified in full — `Sprite`, `Icon`, `Damageable{damageContainer: Silicon}`, `Cybernetics`,
`PirateBountyItem`). Under RT composition (see §V-1) the key is therefore absent from the accumulating child
when `BaseLeftArm` is pushed, so `MajorLimb`'s three thresholds land intact. `GibPartBehavior.Execute` has no
attached-to-a-body guard (`Content.Server/_Shitmed/Destructible/Thresholds/Behaviors/GibPartBehavior.cs:13-19`
— it only checks `BodyPartComponent` then calls `system.BodySystem.GibPart`), exactly as PLAN5 states for the
IPC case.

**Consequence once WP13-1 routes damage to cybernetic parts (it is inert today, exactly like the IPC case
P5-D8 fixes):**
- A cybernetic arm is **destroyed at 190 accumulated Blunt**, below `WolfmedBaseLeftArm`'s Blunt amputation
  threshold of 250 (`_WF/Wolfmed/Body/parts.yml:49-60`). `DismembermentWound` and
  `AmputationConsequenceWound` — both listed in `CyberneticBodyPartProfile.supportedWounds` — are therefore
  **dead for Blunt**, and the limb can never be severed-and-reattached. This is verbatim the failure mode
  P5-D8 calls "regression-prevention, not a balance grab" for IPCs.
- At **Heat 250 a steel prosthetic spawns `Ash`, runs `BurnBodyBehavior` and plays `MeatLaserImpact`.**
- Hands/feet (`LeftHandCybernetic` … `DexRightHand`) gib at Blunt 150, which is *exactly*
  `WolfmedBaseLeftHand`'s Blunt amputation threshold (`parts.yml:71-81`) — a coin-flip race.

**Fix.** Either (a) give `CyberneticPartBase` its own marked `Destructible` block in `_Shitmed/Body/Parts/cybernetic.yml`
(one new upstream edit, same shape as PROTO O(b), no Heat/Ash rung), or (b) lower the cybernetic
amputation thresholds below 190/210 in `WolfmedPartCybernetic`. Extend **U3** to cover cybernetic limbs, and
add a cybernetic twin of T-P5-11. Delete the false comment in §2.2 either way — as written it will teach the
implementer the wrong model.

---

### B2 — P5-D8's premise ("organic parts have no gib trigger") is false, so the 400/400 number is unjustified and makes IPC limbs 2.1× tougher than flesh.

**PLAN5 claim** (P5-D8):
> "Organic parts have no such trigger at all — `BasePart`'s is fully commented out
> (`_Shitmed/Body/Parts/base.yml:8-36`). … 400 matches `BaseTorso`'s organic gib numbers."

**Evidence.** `BasePart`'s block really is commented out (`_Shitmed/Body/Parts/base.yml:8-36`, verified), but
that is not where limb triggers live in Wolfgate. Mono re-added them on `MajorLimb`/`MinorLimb`
(`Resources/Prototypes/Body/Parts/base.yml:276`, `:313`, quoted in B1), and every organic arm, leg, hand and
foot inherits them:
`BaseLeftArm: parent: [MajorLimb, WolfmedBaseLeftArm]` (`:137`), `BaseLeftHand: parent: [MinorLimb, WolfmedBaseLeftHand]` (`:176`), etc.
`BaseTorso`'s 400/400/400 (`_Shitmed/Body/Parts/base.yml:48-64`) is the **torso** number, not a limb number,
so "matches `BaseTorso`'s organic gib numbers" compares a limb to a torso.

**Consequences.**
1. The correct limb comparison is **190 / 210**, not "none". Raising `PartIPCBase` from 110/150 to 400/400
   makes an IPC arm **2.1× tougher than a human arm**, and (D2 deviation R5) a detached IPC limb on the floor
   2.1× tougher than a detached human limb. That is a real balance grab, not regression-prevention, and
   **U3 does not tell the user this** — U3's framing is "raise (a) vs leave (b)", with the justification
   resting on a false comparison.
2. Because organic arms gib at Blunt **190** while their Blunt amputation threshold is **250**
   (`_WF/Wolfmed/Body/parts.yml:54-58`), organics can *never* be Blunt-severed today. Post-raise, IPCs can.
   PLAN5's §8.1 table shows organics and IPCs both as "severable" without noting this inversion, and
   **T-P5-11** ("accumulate 300 Blunt on `LeftArmIPC`; the part is not deleted and is marked severable at
   Blunt 250") would fail on the organic control it implicitly claims parity with.

**Fix.** Change PROTO O(b) to `Blunt 190 / Slash 210` (`MajorLimb` parity) unless the user explicitly wants
IPC limbs tougher than flesh; restate U3 with the real numbers (110/150 today → 190/210 parity → 400/400
tougher-than-flesh, three options); re-derive T-P5-11's expected values. Separately, flag the
organic 190-gib-vs-250-amputation contradiction to the balance pass — it is a pre-existing phase-3 defect,
not phase 5's, but phase 5 is the first document to have the numbers side by side.

---

## 2. MAJOR

### M1 — PROTO R is a no-op edit, and P5-D11's justification for it is wrong.

**PLAN5 claim** (P5-D11 / PROTO R): "`PlayerSiliconHumanoidBase`'s `!type:DamageTrigger damage: 500`
(`silicon_base.yml:91-95`) is an **all-types total** and is the tighter of the two — it must be raised or IPCs
gib faster than they die."

**Evidence.** `DestructibleComponent.Thresholds` is a plain `[DataField("thresholds")]` with default
inheritance behaviour:
`Content.Server/Destructible/DestructibleComponent.cs:12-13`
```csharp
        [DataField("thresholds")]
        public List<DamageThreshold> Thresholds = new();
```
RT composes a field across parent and child **only** when the behaviour is `Always`:
`RobustToolbox/Robust.Shared/Serialization/Manager/SerializationManager.Composition.cs:196-206`
```csharp
                if (parent.TryGetValue(key, out var parentValue))
                {
                    if (newMapping.TryGetValue(key, out var childValue))
                    {
                        if (field.InheritanceBehavior == InheritanceBehavior.Always)
                            newMapping[key] = PushComposition(field.FieldType, parentValue, childValue, context);
                    }
                    else
                        newMapping.Add(key, parentValue);
                }
```
`MobIPC` declares its own `Destructible` with a single threshold
(`Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/ipc.yml:80-87`):
```yaml
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTypeTrigger
        damageType: Blunt
        damage: 400
      behaviors:
      - !type:GibBehavior { }
```
so `PlayerSiliconHumanoidBase`'s `thresholds` list (`silicon_base.yml:91-96`) is **replaced wholesale, not
merged**. And `MobIPC` is the *only* entity in the tree that parents `PlayerSiliconHumanoidBase`
(`grep -rn "PlayerSiliconHumanoidBase" Resources/Prototypes` → 3 hits: the id itself, `MobIPC`'s `parent:`,
and a TODO comment in `Entities/Mobs/Species/base.yml:9`).

**Therefore the 500 trigger is unreachable today and PROTO R changes nothing.** It adds one newly-tracked
upstream file for zero behaviour (56 → 55 if dropped).

**Fix.** Drop PROTO R, or keep it and relabel it honestly as a defensive edit for a future second silicon
humanoid — but remove the "tighter of the two" claim from P5-D11, which is false.

---

### M2 — §8.1's Diona row is wrong for the player: diona limbs are still destroyed, just never severed.

**PLAN5 claim** (§8.1): Diona — "**NO — cannot be severed at all** (`amputationThresholds: {}`)".

**Evidence.** `amputationThresholds: {}` only disables `AmputationSystem`. Verified:
`Content.Shared/_Onyx/Wounds/AmputationSystem.cs:143-155`
```csharp
    private static float GetThresholdProgress(
        DamageSpecifier damage,
        IReadOnlyDictionary<ProtoId<DamageTypePrototype>, FixedPoint2> thresholds)
    {
        var progress = 0f;
        foreach (var (type, threshold) in thresholds) { … }
        return progress;
    }
```
— an empty dict yields 0, and `ReachedThreshold` (`:136-141`) is `>= 1f`. Correct, and PLAN5's T-P5-6 is
sound *for the amputation path*. But `LeftArmDiona: parent: [PartDiona, BaseLeftArm]`
(`Resources/Prototypes/Body/Parts/diona.yml`, concrete parts at `:33` etc.) still inherits `MajorLimb`'s
`GibPartBehavior` at **Blunt 190 / Slash 210** and `BurnBodyBehavior` + `Ash` at **Heat 250** (B1 evidence).
Same for slime limbs (`LeftArmSlime: parent: [PartSlime, BaseLeftArm]`, `Body/Parts/slime.yml:51`).

**Consequence.** A diona arm hit for 190 Blunt is **deleted outright** — it is neither severable *nor*
reattachable, and all of phase 3's amputation content (thrown limb, stump `AmputationConsequenceWound`,
`SurgeryHealAmputationConsequence`, the P4 reattachment gate) is dead for diona. PLAN5's headline —
"the two biggest player-visible changes in phase 5 are IPCs becoming fully medical and **diona limbs becoming
unseverable**" — describes a state the player will experience as "diona limbs explode instead of coming off".

**Fix.** Reword §8.1's diona and slime rows to "destroyed by heavy damage, never cleanly severed or
reattachable"; add a test that a `LeftArmDiona` at 190 Blunt is deleted (or, as a new user decision, raise
`PartDiona`'s inherited triggers the same way B1/B2 resolve the cybernetic and IPC cases, so that diona's
`amputationThresholds: {}` actually means "unbreakable limbs" rather than "limbs that skip straight to
destruction").

---

### M3 — `SiliconWolfmed` adds the whole `Airloss` group, i.e. `Asphyxiation`, and the plan's stated reason for doing so is self-contradicting.

**PLAN5** (§2.3):
> `# Airloss is taken as the whole group (Asphyxiation + Bloodloss, Resources/Prototypes/Damage/groups.yml:20-27)`
> `# so the Tourniquet's `Asphyxiation: 5` application cost also lands on IPCs, matching organics.`

**Evidence.** The group is exactly those two types (`Resources/Prototypes/Damage/groups.yml:22-27`, verified).
But PLAN5's own deviation 16 says:
> "**A tourniquet costs half as much on a robot limb** — its `Asphyxiation: 5` share is dropped by the part's
> `Inorganic`/`Silicon` container even under P5-D5 (the cost is applied to the *part*)."

Both cannot be true. The tourniquet's cost is applied to the part
(`Resources/Prototypes/Entities/Objects/Specific/Medical/healing.yml:280-284`, `- type: Tourniquet` with
`Asphyxiation: 5`), whose container `PROTO O` does not change, so **the stated benefit never materialises**
and the only real effect of taking the whole group is to make IPCs newly vulnerable to `Asphyxiation` from
every other source in the game. `MobIPC` today cannot take `Asphyxiation` at all (`Silicon` container,
`containers.yml:27-34`), has no `Respirator`, and dies at a **projected total of 100**
(`_EE ipc.yml:71-74`, `100: Dead # mono`), so any Asphyxiation source that does not gate on a `Respirator`
now contributes to killing an IPC.

**Fix.** Use `supportedTypes: [Heat, Shock, Radiation, Bloodloss]` and no `Airloss` group. One word, removes
an entire unaudited damage surface, keeps every P5-D5 benefit (oil-loss damage + the P3-D1 vital-part
`Bloodloss` charge). Fold the correction into U2 so the user is deciding between "Bloodloss only" and
"faithful-to-Onyx cosmetic bleeding", not between "Bloodloss + Asphyxiation" and nothing.

---

### M4 — `Cold`/`Caustic` are NOT "the same hole Onyx has"; Wolfgate loses two of seven accepted damage types relative to Onyx.

**PLAN5** frames R6/U13/deviation 9 as a cost-free inert-data situation, and P5-D5 says "Onyx has the
identical hole (its `SiliconIpc` container also omits Airloss)".

**Evidence.** The Airloss half of that claim is true. The Cold/Caustic half is the opposite of true.
`ONYX Resources/Prototypes/Corvax/Damage/containers.yml`:
```yaml
- type: damageContainer
  id: SiliconIpc
  supportedGroups:
    - Brute
    - Burn
    - Electronic # <Onyx-IonDamage>
  supportedTypes:
    - Heat
    - Shock
```
and the `Burn` **group** is `Heat, Shock, Cold, Caustic` (WG's own
`Resources/Prototypes/Damage/groups.yml:9-16`; Onyx's is the same). So in Onyx an IPC or cybernetic part
really does take `Cold` and `Caustic`, really does form `IpcMechanicalDamageWound` from them (the wound
carries `Cold: reopenMinimumDamage 18` and `Caustic: reopenMinimumDamage 12`, `ONYX wounds.yml:595-602`),
and really does bleed from them at `chance: 1`. In Wolfgate, `Inorganic` (Brute + Heat/Shock,
`containers.yml:10-16`) and `Silicon` (Brute + Heat/Shock/Radiation, `:27-34`) discard both silently.
Onyx additionally has an `Electronic` group (Ion) that Wolfgate has no equivalent for.

**Consequence.** Two of the seven `acceptedDamageTypes` on both mechanical profiles, and 2 of the 6
`damageTypes` on both mechanical wounds, are dead — cryogenics and acid do nothing to an IPC limb where in
Onyx they are among the strongest anti-robot tools. That is a *behaviour* deviation, not an inert-data
footnote, and it is not in the deviations block in those terms.

**Fix.** Reword deviation 9 and R6 to say plainly that this is a Wolfgate-only loss versus Onyx. If the user
wants parity, the cheap route is the pattern PLAN5 already accepts for the mob: one more `_WF` container
(`InorganicWolfmed` = `supportedGroups: [Brute, Burn]` + `supportedTypes: [Shock]`) applied only to
`PartIPCBase` and `CyberneticPartBase` via the two `parent:` lines P5 already edits — D2-safe, no shared
container touched. Present it as a user decision rather than burying it in U13's "(a) leave, document".

---

### M5 — Protogen's outcome is overclaimed, its deviation row is missing, and P5-D9's prototype census is wrong.

**PLAN5 §8.1, protogen row:** "everything an organic gets — gains the tourniquet, the analyzer panel, the
pain overlay and all wound surgeries it silently lacks today." **P5-D9:** "`roundStart: false`, 2 mob
prototypes (`MobProtogen`, `MobProtogenDummy`)."

**Evidence — organ gap.** Organ damage and organ destruction are attached to the seven `OrganHuman*` ids
only, by the phase-3 marked `parent:` edits:
`Resources/Prototypes/Body/Organs/human.yml:53,103,150,189,215,249,270`, e.g.
`  parent: [BaseHumanOrgan, WolfmedOrganHeart] # WOLFGATE (WP11-2, D8)`, with the data in
`Resources/Prototypes/_WF/Wolfmed/Body/organs.yml` (`WolfmedOrganHeart` carries
`destructionWound: InternalBleedingWound`, `:54-55`). Protogen's body prototype
(`_Mono/Body/Prototypes/protogen.yml`) uses `OrganProtogenBrain/Eyes/Heart/Lungs/Stomach/Liver/Kidneys`, all
parented to `BaseProtogenOrgan` / `BaseProtogenOrganUnGibbable` (`_Mono/Body/Organs/protogen.yml:39,89,136,175,201,235,256`)
— **none carries `OrganDamage` or `WolfmedOrgan`.** `OrganDamageSystem` requires `OrganDamageComponent`
(`Content.Server/_Onyx/Wounds/OrganDamageSystem.cs:53`).

So a protogen wound host gets: **no organ damage, no organ destruction, no `InternalBleedingWound`, and no
`SurgeryHeal<Organ>` chain** — while `OrganicBodyPartProfile.organDamage.chances` (`WG wounds.yml:28-35`,
`maxAffected: 2`) rolls on every hit for nothing. That is exactly the P5-D13 situation the plan documents for
Ipc/Slime/Plant, and deviation 8 lists only "the Ipc/Slime/Plant profiles".

**Evidence — census.** `MobProtogenDummy` parents `BaseSpeciesDummy`, not `BaseMobProtogen`
(`_Mono/Entities/Mobs/Species/protogen.yml:86-89`), so it is unaffected by EXT 3. The actual second host is
`MobProtogenRandom` (`_Goobstation/Entities/Mobs/Player/humanoid.yml:225-227`, `parent: MobProtogen`).
`MobProtogen` itself is at `_Mono/Entities/Mobs/Player/protogen.yml:4-5`.

**Evidence — stale marked comment WP13-3 misses.** `Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs:31-34`:
```csharp
        // WOLFGATE (D2): Onyx gates on SurgeryTargetComponent; a Shitmed surgery target without WoundHost
        // (a borg, a Protogen) has no WoundableComponent anywhere and would report "no findings" instead of
        // "unavailable".
```
WP13-3's file list is two files and does not include this one.

**Evidence — P5-D9 citation.** P5-D9 cites "`Extractable` juice of `Fat` + `Blood` (`protogen.yml:14-20`)".
`_Mono/Entities/Mobs/Species/protogen.yml:14-20` is `SwappableInstrument`/`UserInterface`; the file contains
no `Extractable`. The real block is `_Mono/Body/Parts/protogen.yml:13-19`. The *substance* of P5-D9 (protogen
is biologically organic) is otherwise fully confirmed: `damageContainer: Biological` + `damageModifierSet: Protogen`
(`:31-33`), `Hunger` (`:27`), `Thirst` (`:34`), `Butcherable → FoodMeatHuman ×5` (`:35-39`),
`Respirator` (`:78-84`), `PartProtogen: parent: [BaseItem, BasePart]` (`_Mono/Body/Parts/protogen.yml:4-5`)
→ `damageContainer: OrganicPart`, concrete parts `parent: [PartProtogen, BaseTorso]` etc. so they inherit the
full `WolfmedBase<Slot>` organic data for free.

**Fix.** Correct the census; add a deviation row "protogen is an organic wound host with no organ damage,
organ destruction, internal bleeding or organ surgery — its organs are `BaseProtogenOrgan`-derived (P3-D7)";
add the analyzer comment to WP13-3's file list; either accept the gap explicitly in U4 or offer a (c) option
that extends the seven `parent:` edits to `_Mono/Body/Organs/protogen.yml`. Note: the same gap applies to
every non-human organic species already shipped as a host (moth, vox, arachnid…), so it is pre-existing —
but phase 5 is the first phase to newly enrol a species into it.

---

### M6 — Adding `Bloodstream` to `MobIPC` drags in drunkenness, stuttering and a dead 250 u chemical solution; none is surfaced.

**Evidence.** The bloodloss branch of `BloodstreamSystem.Update` has **no wound-host or species guard**,
unlike `OnDamageChanged` which carries GUARD E:
`Content.Server/Body/Systems/BloodstreamSystem.cs:142-162`
```csharp
            if (bloodPercentage < bloodstream.BloodlossThreshold && !_mobStateSystem.IsDead(uid))
            {
                var amt = bloodstream.BloodlossDamage / (0.1f + bloodPercentage);
                _damageableSystem.TryChangeDamage(uid, amt, ignoreResistances: false, interruptsDoAfters: false);
                _drunkSystem.TryApplyDrunkenness(uid, (float) bloodstream.UpdateInterval.TotalSeconds * 2, applySlur: false);
                _stutteringSystem.DoStutter(uid, bloodstream.UpdateInterval * 2, refresh: false);
```
versus `:212-214`
```csharp
    private void OnDamageChanged(Entity<BloodstreamComponent> ent, ref DamageChangedEvent args)
    {
        // WOLFGATE: GUARD E, wound hosts get their bleeding from WoundBleedingSystem instead.
        if (HasComp<WoundHostComponent>(ent))
            return;
```
So under U2(a) a leaking IPC **stutters and becomes drunk**. Also `OnComponentInit` (`:182-195`) creates a
`chemicals` solution sized by `ChemicalMaxVolume`, default `250`
(`Content.Server/Body/Components/BloodstreamComponent.cs:126-127`) — on an entity whose only metabolising
organ has its `Metabolizer` block commented out (`_EinsteinEngines/Body/Organs/ipc.yml:70-76`, verified,
which is also R4's evidence). Onyx's own commented-out Wolfgate block set `chemicalMaxVolume: 0`
(`silicon_base.yml:160`). PLAN5's PROTO Q sets neither that nor `InjectableSolution`, whereas
`MobBloodstream` — which **Onyx's `MobIpc` parents** (`ONYX Corvax/Body/Species/ipc.yml:93-97`) — declares it:
`Resources/Prototypes/Entities/Mobs/base.yml:236-248`
```yaml
  id: MobBloodstream
  abstract: true
  components:
  - type: SolutionContainerManager
  - type: InjectableSolution
    solution: chemicals
  - type: Bloodstream
    bloodlossDamage: { types: { Bloodloss: 0.5 } }
    bloodlossHealDamage: { types: { Bloodloss: -1 } }
```
(PLAN5's inline `bloodlossDamage`/`bloodlossHealDamage` values are byte-identical to this, and both fields
are `[DataField(required: true)]` at `BloodstreamComponent.cs:69-77` — PROTO Q correctly supplies both.)

**Fix.** Add `chemicalMaxVolume: 0` to PROTO Q with a marked comment (recommended — no metabolizer, so a
chemical solution is a trap), state explicitly that `InjectableSolution` is deliberately omitted (a divergence
from Onyx, which parents `MobBloodstream`), and add the drunk/stutter effect to §8.1's IPC row and to R4.
`DamageBleedModifiers` defaulting to `BloodlossHuman` (`BloodstreamComponent.cs:99-100`,
`modifier_sets.yml:248-260`) is correctly inert under GUARD E — verified, no action.

---

### M7 — T-P5-4 and §1.3 row 1 name abstract prototypes that cannot be spawned.

**PLAN5 §1.3 row 1** lists 14 cybernetic entities as proof the tests can use "real ids, not a
`[TestPrototypes]` stub", and **T-P5-4** says "attach a real `LeftArmCyberneticBase`-derived part".

**Evidence.** `Resources/Prototypes/_Shitmed/Body/Parts/cybernetic.yml` —
`CyberneticPartBase` (`:2-4`), `LeftArmCyberneticBase` (`:17-19`), `RightArmCyberneticBase` (`:31-33`),
`LeftLegCyberneticBase` (`:45-47`), `RightLegCyberneticBase` (`:59-61`) are all `abstract: true`. Only ten ids
are spawnable: `LeftHandCybernetic` (`:74`), `RightHandCybernetic` (`:82`), `LeftFootCybernetic` (`:90`),
`RightFootCybernetic` (`:98`), `JawsOfLifeLeftArm` (`:106`), `JawsOfLifeRightArm` (`:120`), `SpeedLeftLeg` (`:134`),
`SpeedRightLeg` (`:148`), `DexLeftHand` (`:162`), `DexRightHand` (`:170`).

**Fix.** T-P5-4 must spawn `JawsOfLifeLeftArm` — the only concrete cybernetic **arm** in the tree. (Content
reachability is otherwise fine: lathe recipes `Recipes/Lathes/robotics.yml:227,249,282`, research
`Research/civilianservices.yml:194-198`, the Mono prybar trait
`Content.Server/_Mono/Traits/Physical/PrybarProstheticsSystem.cs:60`, and two shuttle maps.)

---

## 3. MINOR

| # | Finding | Evidence |
|---|---|---|
| m1 | §2.1 cites ONYX `wounds.yml:36-142` for the four profiles; the real span is `37-151` (ids at 38 / 70 / 100 / 124). The D33 row cites `:39-40` for `IpcBodyPartProfile`'s recovery multipliers; they are at `:40-41`. | `git -C C:/tmp/onyx show HEAD:Resources/Prototypes/_Onyx/Wounds/wounds.yml \| grep -n "^  id:"` |
| m2 | §2.2's Onyx source citations drift: `OrganSlimePersonExternal` is at `slime.yml:232` with the Woundable block at `:235-239` (cited `230-243`); `OrganDionaExternal` at `diona.yml:226` with the block at `:230-235` (cited `224-236`); `OrganIpcExternal` at `ipc.yml:322` with the block at `:326-330` (cited `321-330`). Values quoted are all correct. | `git show HEAD:Resources/Prototypes/Body/Species/{slime,diona}.yml`, `Corvax/Body/Species/ipc.yml` |
| m3 | PROTO Q cites `MobIPC`'s `Destructible` at `:76-87`; it is `:80-87`. PROTO R cites `:91-95`; it is `:91-96`. | `_EE/.../ipc.yml`, `silicon_base.yml` |
| m4 | §3.4 lists `HealthExaminable` and `DamageVisuals` among "`…/Player/ipc.yml`'s … untouched" components. Both are on `PlayerSiliconHumanoidBase` (`silicon_base.yml:269`, `:284`), which PROTO R **does** touch — the framing is self-inconsistent. The substance (IPCs get per-limb damage sprites for free) holds. | grep on both files |
| m5 | P5-D9 cites `protogen.yml:14-20` for `Extractable` Fat+Blood; that block is in `_Mono/Body/Parts/protogen.yml:13-19`. See M5. | both files |
| m6 | R8 states "`TorsoIPC` has no `WolfmedBodyPart` at all". True today; **false after PROTO O(a)** — `TorsoIPC: parent: [PartIPCBase, BaseTorsoInorganic]` (`_EE ipc.yml:26-27`) will inherit `WolfmedPartIpc`'s block, with `MaxDamage` at the component default 0 (`WolfmedBodyPartComponent.cs:16`). The conclusion (overflow disabled, correct for a torso) survives; the stated reason must be rewritten or R8 actively misleads. | `WolfmedBodyPartComponent.cs`, `_EE ipc.yml` |
| m7 | Implementation trap not stated: Onyx's `IpcBodyPartProfile.organDamage` has **no `maxAffected`** (only `OrganicBodyPartProfile` sets `maxAffected: 2`), and `SlimeBodyPartProfile` / `PlantBodyPartProfile` list only `Head`/`Chest`/`Groin` — no Arm/Hand/Leg/Foot rows. §2.1's prose is right; say explicitly "do not add `maxAffected`, do not add the four limb rows" so a verbatim-copy pass does not normalise them. | `ONYX wounds.yml:59-67, 93-97, 147-151`; `WoundPrototype.cs:190-196` (`MaxAffected = 1` default) |
| m8 | The shipped phase-1 precedent puts the Wolfmed abstract **last** (`_Shitmed/Body/Parts/base.yml:47`, `parent: [BaseTorsoInorganic, WolfmedBaseTorso]`; `Body/Parts/base.yml:137`, `parent: [MajorLimb, WolfmedBaseLeftArm]`), which is the *opposite* of P5-D1's mandatory-first rule. It only worked because those parents declare no `WolfmedBodyPart`. WP13-1 should carry a one-line warning so nobody "normalises" the new files to match the old ones. | both files |

---

## 4. What I checked and CLEARED (so the user knows the negative space)

- **V-1 RT first-parent-wins — CONFIRMED, P5-D1 is correct.** `PrototypeManager.cs:426-436` builds
  `parentMaps` in declaration order; `SerializationManager.PushComposition(Type, DataNode[], DataNode)`
  (`SerializationManager.Composition.cs:44-57`) folds each parent onto the *accumulating* node, so a key
  written by `parents[0]` is present when `parents[1]` is pushed and is kept;
  `ComponentRegistrySerializer.PushInheritance` (`:192-215`) matches by component registration and recurses;
  `PushInheritanceDefinition` (`:184-206`) keeps a child key unless `InheritanceBehavior.Always`.
  `EntityPrototype.Components` is `[AlwaysPushInheritance]` (`EntityPrototype.cs:153-156`).
  Consequences all verified: `fractureProfile: null` (a present key) beats `Base<Slot>`'s
  `OrganicFractureProfile`; `amputationThresholds` (absent on `WolfmedPartIpc`/`WolfmedPartCybernetic`) is
  inherited from `Base<Slot>`; `amputationThresholds: {}` on `WolfmedPartDiona` suppresses it.
- **V-2 D9 fold is mandatory.** `OrganDamageRouting.Chances` is `Dictionary<BodyPartType, float>`
  (`WoundPrototype.cs:190-192`) and `BodyPartType` is `Other, Torso, Head, Arm, Hand, Leg, Foot, Tail`
  (`Content.Shared/Body/Part/BodyPartType.cs:10-19`) — `Chest`/`Groin` would fail deserialization.
- **V-3 §8.2-1 fold.** `FractureGradeSettings(FixedPoint2 threshold, float movementModifier, float manipulationModifier)`
  with C# defaults `Hairline(8, 0.9, 1.1)` … `Comminuted(40, 0.4, 2f)` (`WoundPrototype.cs:270-295`,
  `:253-262`), matching the shipped `OrganicFractureProfile` fold (`WG wounds.yml:53-78`). Onyx's
  `CyberneticFractureProfile` (`ONYX wounds.yml:193-231`) is field-identical to `OrganicFractureProfile` except
  `id:` and `wound:` — PLAN5's claim is exact.
- **V-4 Locale — 100 % complete, independently verified.** I extracted every `wound-name-*`,
  `wound-stage-*` and `wound-examine-*` LocId from the 10 new wound prototypes (27 distinct keys) and grepped
  `Resources/Locale/en-US/`: **zero missing**. The four `wound-examine-frame-*` keys are already at
  `_Onyx/medical/health-examinable.ftl:38-41`. The four new P5-5 keys do not already exist (no duplicate-key
  risk); the base keys they sit beside are at `_Onyx/medical/health-analyzer-component.ftl:4,17,18` and
  `health-examinable.ftl:37`, exactly as §2.5 says.
- **V-5 P5-D12 (cable coil) — CONFIRMED, the leak is real.** `HealingComponent.TreatmentCapabilities`
  defaults to `[Biological]` (`Content.Server/Medical/Components/HealingComponent.cs:72-73`) and
  `HealWounds` defaults to **true** (`:66-67`); HOOK 8 skips the `DamageContainers` check for wound hosts
  (`Content.Server/Medical/HealingSystem.cs:201-210`); `WoundHealingSystem.IsCompatiblePart` takes
  `damageContainers` and never reads it (`Content.Server/_Onyx/Wounds/WoundHealingSystem.cs:154-166`).
  `CableStack`'s block is `damageContainers: [Silicon]`, `delay: 0.6`, `Heat -3 / Shock -3 / Radiation -3`
  (`Entities/Objects/Tools/cable_coils.yml:36-45`). Onyx's own coil is
  `treatmentCapabilities: [Electrical]` + `healWounds: true` (`ONYX cable_coils.yml:178-193`).
- **V-6 PROTO S/T are genuinely mandatory.** `WeldingHealableSystem.OnRepairFinished` hard-gates on
  `component.DamageContainers.Contains(damageable.DamageContainerID)`
  (`Content.Server/_EinsteinEngines/Silicon/WeldingHealable/WeldingHealableSystem.cs:29-36`); the welder's
  list is at `Entities/Objects/Tools/welders.yml:117-119` and the nanite applicator's at
  `_Mono/Entities/Objects/Tools/nanite_applicator.yml:47-50`. P5-D5's "new finding" is correct and §1.3
  row 8's correction of `circulation.md` stands.
- **V-7 P5-D15 (no `Repairable` on `MobIPC`) is correctly reasoned.** `RepairableSystem.cs:22` and
  `WeldingHealableSystem.cs:25` both subscribe `InteractUsingEvent`, on *different* components, so it is a
  double-do-after risk rather than the duplicate-directed-subscription crash class — PLAN5 describes it
  accurately. `MobIPC` already carries `- type: WeldingHealable` (`_EE ipc.yml:129`).
- **V-8 The phase-1 metabolizer blocker really is void.** `git -C C:/tmp/onyx grep -n "circulatoryStream" HEAD -- Resources`
  → one hit, the `Organic` declaration. None of Onyx's five profiles sets it (verified by reading
  `ONYX wounds.yml:1-151` in full). `BodyPartProfilePrototype.CirculatoryStream` defaults to `"Organic"`
  (`WoundPrototype.cs:156-157`), and WG's trimmed `SetBleedRates` reads only the primary key
  (`Content.Server/_Onyx/Chemistry/Circulation/CirculatoryStreamSystem.cs:70-80`). P5-D3 and the §2.4
  one-line sum are both correct and the hardening is worth taking.
- **V-9 Zero new subscriptions — CONFIRMED.** Phase 5 adds no system. `WolfmedWoundHostExclusionSystem`'s
  single `<WoundHostComponent, ComponentInit>` (`:17`) is untouched by EXT 3
  (`Content.Shared/_WF/Wolfmed/Body/WolfmedWoundHostExclusionSystem.cs:12`, `ExcludedAncestors = new() { "BaseMobProtogen" }`).
  No duplicate-directed-subscription exposure.
- **V-10 Name collisions — none.** `WolfmedPartSlime`, `WolfmedPartDiona`, `WolfmedPartIpc`,
  `WolfmedPartCybernetic`, `SiliconWolfmed`: zero hits across `Resources`, `Content.*`. The 16 prototype ids:
  present only in ONYX and in `Docs/`. No `[TestPrototypes]` id collision (the pool is
  `Wound*` / `Wolfmed*` fixture ids; PLAN5 adds none).
- **V-11 D2 exposure from PROTO M/N/P is nil.** Nothing outside `Body/Parts/slime.yml` and
  `Body/Parts/diona.yml` parents `PartSlime`/`PartDiona` (grep, zero hits). `MobDionaNymph` /
  `MobDionaNymphAccent` are `SimpleMobBase` (`Entities/Mobs/NPCs/animals.yml:3793`) with no body parts.
  Nothing outside `_Shitmed/Body/Parts/cybernetic.yml` parents `CyberneticPartBase`.
- **V-12 No unscheduled wound-host species.** Every part tree rooted in `BasePartInorganic` was enumerated:
  `Body/Parts/silicon.yml` (borgs, not hosts), `Body/Parts/skeleton.yml` (P5-D18, not a host —
  `MobSkeletonPerson` is off the `BaseMobSpeciesOrganic` line), `_EinsteinEngines/Body/Parts/ipc.yml`
  (scheduled), `_Mono/Body/Parts/chimera.yml` (used by `MonoBaseMobLetoferol` →
  `MobNonHumanHostileBase`, not a host), `_Shitmed/Body/Parts/base.yml`, `_Shitmed/Body/Parts/cybernetic.yml`
  (scheduled). No profile is referenced by a part the plan does not schedule, and no scheduled profile lacks
  a consumer.
- **V-13 Mono has no IPC prototype overrides.** `find Resources/Prototypes/_Mono -iname "*ipc*"` → only a
  turret graph and `Recipes/Lathes/Packs/ipcs.yml`. `MobIPC` appears in exactly two files
  (`_EinsteinEngines/Entities/Mobs/Player/ipc.yml`, `_EinsteinEngines/Species/ipc.yml`). Mono's only IPC
  edits are inline (`100: Dead # mono`, `drainPerSecond: 0.65 # Mono`, `keySlots: 8 # Mono`,
  `RetroMonitorView # Mono`). The "Mono IPC with pump organ" of DECISIONS P5-1 is
  `OrganIPCPump` (`_EinsteinEngines/Body/Organs/ipc.yml:54-76`) with `- type: Heart` and its `Metabolizer`
  commented out; `HeartSystem` (`Content.Server/_Shitmed/Body/Organ/HeartSystem.cs`) only manages
  `DelayedDeathComponent` and does **not** interact with `BloodstreamComponent`, so PROTO Q's bloodstream
  introduces no heart-side surprise. `TorsoIPC` holds `posbrain: PositronicBrain` + `pump: OrganIPCPump`
  (`_EinsteinEngines/Body/Prototypes/ipc.yml:12-21`), so decapitating an IPC does not remove its brain.
- **V-14 IPC surgery is reachable.** `- type: SurgeryTarget` is on `PlayerSiliconHumanoidBase:319`
  (and on `BaseMobSpeciesOrganic` at `base.yml:233`). The P4 wound surgeries are profile-agnostic:
  `SurgeryTendWoundsBruteDeep`/`BurnDeep` gate on `SurgeryWoundedCondition { woundGroup, minWoundSeverity: 100 }`
  (`_WF/Wolfmed/Surgery/surgeries.yml:63-89`) and the fracture chain on
  `WolfmedSurgeryFractureCondition minGrade: Hairline` (`:34-45`) — no capability and no wound id, so §1.3
  row 6's downgrade of `species.md` T3 to a test is correct.
- **V-15 `Oil` is real and flammable.** `Resources/Prototypes/Reagents/Consumable/Food/ingredients.yml:186-200`
  — `flammability: 2` and `!type:FlammableTileReaction`. §8.1's "flammable puddles" is right. `Sap`
  (`diona.yml:49`) and `Slime` (`slime.yml:83`) confirmed as the existing per-species blood reagents, so
  §1.3 row 2's correction of `tests.md` stands and slime/diona circulation genuinely needs zero work.
- **V-16 Test derivations spot-checked.** `SlimeSlashWound` (`ONYX wounds.yml:728-767`) really has no
  `minimumSeverity` on any of its four stage bleeding blocks and no `WoundScarBehavior`, with
  `painPerSeverity: 0.67`; WG's `SlashWound` (`WG wounds.yml:186-232`) carries `minimumSeverity: 9` on all
  four. Trap 3 and T-P5-5 are correctly derived. `IpcMechanicalDamageWound` (`ONYX :583-632`) really carries
  wound-level `WoundBleedingBehavior rate: 0.08, chance: 1` with no minimum and
  `WoundPainBehavior painPerSeverity: 0.87, minSeverity: 10`, and really has no `Shock` row.
  `CyberneticFrameFractureWound` (`:337-370`) really has no pain behaviour.
- **V-17 P5-D10 is right and resolves the `species.md`/`tests.md` conflict correctly.**
  `ONYX Resources/Prototypes/Body/species_base.yml:54` — `- type: PainShockTarget # <Onyx-PainShock>` on
  `BaseSpeciesMob` (`id:` at `:11`), and Onyx's `MobIpc` parents `[AppearanceIpc, BaseSpeciesMob, MobBloodstream]`
  (`ONYX Corvax/Body/Species/ipc.yml:93-97`). Wolfgate's copy is on `BaseMobSpeciesOrganic`
  (`Entities/Mobs/Species/base.yml:256`), which `MobIPC` does not inherit. `MobIpc`'s
  `allowedEmotes: [ Boop, Whirr ]` is at `ONYX ipc.yml:139`.
- **V-18 The analyzer payload change is safe.** `HealthAnalyzerWoundDiagnostic` has exactly **one**
  construction site in the whole tree (`Content.Server/_WF/Wolfmed/Medical/HealthAnalyzerSystem.Wolfmed.cs:118`);
  `woundable` and `_prototypes` are both in scope in `BuildWoundDiagnostics`'s per-part loop (`:26, :38-45`),
  so the `Mechanical` flag is computable there. The consumers exist:
  `Content.Client/_WF/Wolfmed/Medical/WolfmedDiagnosticPanel.xaml(.cs)` and
  `Content.Shared/_Onyx/HealthExaminable/HealthExaminableSystem.PartStatus.cs`.
- **V-19 D22 mechanism.** `WoundDamageProjectionSystem.RefreshBodyDamage` (`:156-201`) sums
  `GetPositiveDamage` over every part plus container-filtered systemic and calls `_damage.SetDamage(body, total)`
  at `:192`. `SetupPart` (`:219-230`) does `EnsureComp<WoundableComponent>` (so a prototype-declared
  `Woundable` survives — P5-D1's lever is correct) and `RemComp<PainComponent>` when `!CanFeelPain`
  (so trap 5 is right).
- **V-20 Build order is sound.** WP13-0 (prototypes) → WP13-1 (references them) → WP13-2 (needs WP13-1's
  parts) → WP13-4 (needs WP13-0/1 so the coil gains mechanical targets in the same phase it loses organic
  ones). PROTO S/T are correctly bound into WP13-2 alongside PROTO Q. One-owner-per-file holds: WP13-1 owns
  `_EinsteinEngines/Body/Parts/ipc.yml`, WP13-2 owns `_EinsteinEngines/Entities/Mobs/Player/ipc.yml` — two
  different files despite the confusable names. The only inter-package window is WP13-1→WP13-2, where IPC
  parts carry `Woundable` while `MobIPC` is not yet a host; harmless (no routing, no reader).

---

## 5. Per-species player experience — corrected table

Changes from PLAN5 §8.1 in **bold**.

| | bleeds | pain | scars | fracture | limb loss | passive heal | treated by | organ damage |
|---|---|---|---|---|---|---|---|---|
| Human & other organics | Blood ×1.0 | yes | yes | bone | severed at Slash 130 / Heat 250 / Piercing 250; **destroyed outright at Blunt 190 / Slash 210, so Blunt severing is unreachable (pre-existing)** | yes | everything **minus the cable coil** | yes (human-lineage organs only) |
| **Diona** | Sap ×1.0, from the first scratch | yes | yes | none | **never cleanly severed (`amputationThresholds: {}`), but still destroyed outright at Blunt 190 / Slash 210 / Heat 250 (Ash + BurnBody)** | yes | Biological | **none — `OrganDiona*`-equivalents carry no `OrganDamage`; no internal bleeding, no organ surgery** |
| **Slime** | Slime ×1.15, from the first scratch | 0.7 blunt / 0.67 slash-pierce vs organic 0.87 | never | none | **same as diona: unseverable by threshold only where thresholds exist; still gibbable at 190/210** | yes | Biological | **none** |
| **IPC** | Oil ×1.0, every chassis wound leaks (`chance: 1`, no minimum), flammable puddles | yes + pain shock, **no chemical relief exists** | never | none | severable; limb gib ceiling 110/150 → **(B2: choose 190/210 parity or 400/400)** | never (`passiveRecoveryMultiplier: 0`) | welder, nanite applicator, cable coil; tourniquet and wound surgeries work; **no medicine** | none |
| | **plus: drunk + stuttering while leaking (M6); a dead 250 u chemical solution unless `chemicalMaxVolume: 0` (M6); `Cold`/`Caustic` do nothing, unlike in Onyx (M4); `Asphyxiation` becomes a live damage type unless M3 is applied** | | | | | | | |
| **Cybernetic limb** | wearer's blood ×0.5, only from Dismemberment/SurgicalIncision | **none** (`PainComponent` removed) | never | **frame fracture, 4 grades, `BrokenBones` alert, mendable by `SurgeryMendFracture`** | **B1: destroyed at Blunt 190 / Slash 210 before the 250 severing threshold; burns to Ash at Heat 250** | never | cable coil | n/a |
| **Protogen** | Blood ×1.0 | yes | yes | bone | organic | yes | everything an organic gets **except organ damage, internal bleeding and the seven organ surgeries (M5)** | **none** |
| Skeleton, borgs, NPCs, chimera, diona nymphs | no change | | | | | | | |

---

## 6. Decisions the user must make (new or materially revised)

| # | Question | Recommendation |
|---|---|---|
| **U3′** (revised) | IPC limb gib triggers: (a) leave 110/150, (b) **190/210 — parity with Mono's `MajorLimb`, which is what organic limbs actually have**, (c) 400/400 as PLAN5 proposes (2.1× tougher than flesh, also on detached limbs). | **(b)** — it is the smallest change that makes `DismembermentWound` reachable, and it is the real organic parity number. (c) only if IPC limbs are *meant* to be tougher; say so in the changelog. |
| **U15** (new) | Cybernetic limbs gib at Blunt 190 / Slash 210 / Heat 250 (Ash + BurnBody) and can therefore never be Blunt-severed. (a) give `CyberneticPartBase` a marked `Destructible` (no Heat/Ash rung), (b) lower `WolfmedPartCybernetic`'s amputation thresholds below 190, (c) ship as-is. | **(a)** — one more marked upstream line in a file PROTO P already edits; also removes a steel arm that spawns Ash. |
| **U2′** (revised) | `SiliconWolfmed` membership: (a) `supportedTypes: [… Bloodloss]`, (b) `supportedGroups: [… Airloss]` as PLAN5 proposes. | **(a)** — the stated reason for the group (tourniquet cost) is contradicted by deviation 16, and (b) silently opens `Asphyxiation` on an entity that dies at 100 total. |
| **U16** (new) | IPC chemical solution: (a) `chemicalMaxVolume: 0` and no `InjectableSolution` (diverges from Onyx, which parents `MobBloodstream`), (b) Onyx-faithful injectable 250 u that never metabolizes. | **(a)** — IPCs have no metabolizer (R4); an injectable solution that does nothing is the same trap U2 exists to avoid. |
| **U17** (new) | Diona/slime limb destruction: (a) accept "destroyed but never severed", (b) raise their inherited `MajorLimb`/`MinorLimb` triggers so `amputationThresholds: {}` actually means unbreakable. | **(a)** for phase 5, recorded honestly in §8.1 and the guidebook; (b) is a fourth species-specific `Destructible` edit and can wait for the balance pass. |
| **U12′** (revised) | Protogen organ damage: (a) accept the gap and record it, (b) extend the phase-3 organ `parent:` edits to `_Mono/Body/Organs/protogen.yml`. | **(a)** — consistent with P3-D7, but it must be a *stated* deviation, not an omission from §8.1. |
| **U13′** (revised) | `Cold`/`Caustic` on IPC/cybernetic parts, which **Onyx supports and Wolfgate does not** (M4): (a) leave and record as a Wolfgate-only loss, (b) add a `_WF` `InorganicWolfmed` container to the two part bases the phase already edits. | **(b)** is cheap and D2-safe (no shared container touched) and restores two of seven accepted types; (a) is defensible but must stop describing it as a hole Onyx shares. |
| **U18** (new) | Drop PROTO R (M1)? | **Yes** — it is a no-op and inflates the upstream footprint. |

PLAN5's U1, U4–U11, U14 I checked and agree with as framed, subject to the corrections above.

---

## 7. Summary

- **Blockers: B1** (cybernetic limbs have `MajorLimb` gib triggers, so half of `CyberneticBodyPartProfile`
  ships dead and a steel arm burns to Ash) and **B2** (P5-D8's "organic parts have no gib trigger" is false,
  so the 400/400 number and U3's framing are both unjustified).
- **Majors: M1** PROTO R is a no-op with a false justification; **M2** diona/slime limbs are destroyed, not
  merely unseverable; **M3** `SiliconWolfmed`'s `Airloss` group is self-contradicting and opens
  `Asphyxiation`; **M4** `Cold`/`Caustic` are an Onyx-parity loss, not a shared hole; **M5** protogen's organ
  gap, census error and a stale marked comment; **M6** `Bloodstream` on `MobIPC` drags in drunk/stutter and a
  dead chemical solution; **M7** T-P5-4 names abstract prototypes.
- Everything else in PLAN5 that I could falsify, I tried to and could not. The plan's core inheritance,
  capability, welding-gate, locale and subscription analysis is sound, and the phase-1 metabolizer blocker is
  genuinely void.
