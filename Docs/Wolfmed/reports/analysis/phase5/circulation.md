# P5-2 — Circulatory streams for non-organic species

Analyst report, phase 5. **READ-ONLY**: nothing in `WG` or `ONYX` was modified.

- `WG` = `C:/Users/jzo12/Documents/GitHub/Wolfgate/.claude/worktrees/rules-motd-updates-11c89c` (HEAD `2b4a4675d0`, phase 4 committed).
- `ONYX` = `C:/tmp/onyx`, pin `2f5bab9946539cbe083010c9ae6fbc59b47ae377`. Paths outside the sparse set read with `git show HEAD:<path>`; whole-tree searches with `git grep … HEAD`.
- Scope: DECISIONS.md §"Phase 5" **P5-2** only. P5-1 (species profiles/wound sets), P5-3 (`treatmentCapabilities`), P5-4 (pain numbness), P5-5 (analyzer text) are cross-referenced where circulation touches them, not planned here.

---

## 0. Headline

**Route (a) is correct, and it is correct because Onyx itself does exactly that.**

The phase-1 blocker ("Onyx's IPC/Slime/Plant streams need a shared stage-based metabolizer") rests on a premise that is **false at the pin**: Onyx ships **no IPC, Slime, Plant or Cybernetic circulatory stream**. It ships exactly one `circulatoryStream` prototype — `Organic` — and **none of its five `bodyPartProfile`s override `circulatoryStream`**, so every Onyx species, IPC and cybernetic prosthetics included, runs on the primary stream and the one `BloodstreamComponent` blood solution. IPCs bleed **Oil**, slimes bleed **Slime**, diona bleed **Sap** — all through the ordinary bloodstream, differentiated only by `bloodReferenceSolution` (Wolfgate's `bloodReagent`) and by the profile's `bleedingMultiplier`.

Therefore **P5-2 needs zero new C# and zero new stream prototypes.** Total cost of the chosen route:

| Change | Files | Lines |
|---|---|---|
| Add `Bloodstream` (+ oil, + volume) to `MobIPC` | 1 upstream YAML (`_EinsteinEngines/.../ipc.yml`) | ~10 marked |
| A `_WF` damage container so IPC oil-loss can actually damage it (**user decision**, §5.3) | 1 new `_WF` YAML + 1 line on `MobIPC` | ~8 |
| Defensive 1-line hardening of `SetBleedRates` (**recommended**, §4.4) | 1 `_Onyx` C# file | 1 |
| Slime / diona / cybernetic-limb circulation | — | **0** |

Everything else P5-1 needs (the `IpcBodyPartProfile` / `SlimeBodyPartProfile` / `PlantBodyPartProfile` / `CyberneticBodyPartProfile` prototypes and their wound sets) is profile data, not circulation.

**The one real trap** is §4.3: `SetBleedRates` in WG's trimmed system reads **only** the `Organic` key. If P5-1's implementer "helpfully" writes `circulatoryStream: Ipc` on a new profile, IPC bleeding silently becomes **zero** — no error, no log, tests that only assert wound severity still pass. That must be written into PLAN5 as a hard rule, and ideally fixed by the one-line change in §4.4.

---

## 1. What Onyx actually ships at the pin (task item 1)

### 1.1 `circulatory_streams.yml` — verified, one stream only

`ONYX Resources/Prototypes/_Onyx/Chemistry/circulatory_streams.yml` — **complete file**:

```yaml
- type: circulatoryStream
  id: Organic
```

Verification that this is the *only* definition anywhere in the tree:

```
$ git -C C:/tmp/onyx grep -ln "type: circulatoryStream" HEAD -- Resources
HEAD:Resources/Prototypes/_Onyx/Chemistry/circulatory_streams.yml

$ git -C C:/tmp/onyx grep -n "circulatoryStream" HEAD -- Resources
HEAD:Resources/Prototypes/_Onyx/Chemistry/circulatory_streams.yml:1:- type: circulatoryStream
```

That second grep is the decisive one: **the string `circulatoryStream` appears in Onyx's entire `Resources/` tree exactly once, on the line that declares the prototype itself.** No `bodyPartProfile` sets it. No mob sets it. The phase-1 analyst's finding is confirmed and is stronger than it was stated: not merely "only an Organic stream exists", but "nothing in Onyx content ever selects a stream at all".

`CirculatoryStreamPrototype.PrimaryStream = "Organic"` — `ONYX Content.Shared/_Onyx/Chemistry/Circulation/CirculatoryStreamPrototype.cs:12`. So the single stream that exists **is** the primary stream, and every secondary-stream code path in `CirculatoryStreamSystem.cs` is unreachable in Onyx too.

### 1.2 The five `bodyPartProfile`s — none sets `circulatoryStream`

`ONYX Resources/Prototypes/_Onyx/Wounds/wounds.yml`. Exact fields (quoted verbatim, `circulatoryStream` absent from all five, so all five inherit `BodyPartProfilePrototype.CirculatoryStream = "Organic"` from `ONYX Content.Shared/_Onyx/Wounds/WoundPrototype.cs:157`):

| Profile | line | `treatmentCapabilities` | `bleedingMultiplier` | `scarrable` | `canFeelPain` | passive/bed recovery |
|---|---:|---|---:|---|---|---|
| `OrganicBodyPartProfile` | 2 | `[Biological]` | `1.0` | (default `true`) | (default `true`) | (default 1/1) |
| `IpcBodyPartProfile` | 38 | `[Mechanical, Electrical]` | **`1`** | `false` | (default **`true`**) | `0` / `0` |
| `SlimeBodyPartProfile` | 70 | `[Biological]` | **`1.15`** | `false` | (default `true`) | (default 1/1) |
| `CyberneticBodyPartProfile` | 100 | `[Mechanical, Electrical]` | **`0.5`** | `false` | **`false`** | `0` / `0` |
| `PlantBodyPartProfile` | 124 | `[Biological]` | **`1`** | (default `true`) | (default `true`) | (default 1/1) |

**No profile uses `bleedingMultiplier: 0`.** Every Onyx species bleeds; only the rate and the reagent differ. An IPC bleeds at exactly the organic rate; a cybernetic prosthetic at half.

`supportedWounds` differences that matter to circulation:

- `IpcBodyPartProfile` (lines 49-57): `IpcMechanicalDamageWound, ElectricalWound, SurgicalIncisionWound, DismembermentWound, AmputationConsequenceWound, SystemicBleedingWound` — **no `InternalBleedingWound`**, no `MedicalScarWound`, no fracture wound.
- `CyberneticBodyPartProfile` (lines 110-116): `CyberneticMechanicalDamageWound, ElectricalWound, CyberneticFrameFractureWound, SurgicalIncisionWound, DismembermentWound, AmputationConsequenceWound` — **no `InternalBleedingWound` and no `SystemicBleedingWound`**.
- `SlimeBodyPartProfile` (lines 80-89) and `PlantBodyPartProfile` (lines 132-143): both **include** `InternalBleedingWound` and `SystemicBleedingWound`.

### 1.3 What an Onyx IPC actually does when wounded

`ONYX Resources/Prototypes/_Onyx/Wounds/wounds.yml:584` `IpcMechanicalDamageWound`:

```yaml
  behaviors:
  - !type:WoundBleedingBehavior
    rate: 0.08
    chance: 1
  - !type:WoundPainBehavior
    painPerSeverity: 0.87
    minSeverity: 10
```

So: **an Onyx IPC bleeds (always, `chance: 1`) and feels pain.** `CyberneticMechanicalDamageWound` (line 635) has the identical `WoundBleedingBehavior rate: 0.08, chance: 1` but **no pain behavior** — a cybernetic limb leaks and does not hurt, which is also why its profile sets `canFeelPain: false`.

What it bleeds — `ONYX Resources/Prototypes/Corvax/Body/Species/ipc.yml:96-113`:

```yaml
- type: entity
  parent:
  - AppearanceIpc
  - BaseSpeciesMob
  - MobBloodstream # <Onyx-IPCWounds>
  id: MobIpc
  components:
  # <Onyx-IPCWounds>
  - type: WoundHost
  - type: Bloodstream
    bloodReferenceSolution:
      reagents:
        - ReagentId: Oil
          Quantity: 250
    bloodlossDamage:
      types:
        Bloodloss: 0.5
    bloodlossHealDamage:
      types:
        Bloodloss: -1
  # </Onyx-IPCWounds>
  - type: Damageable
    damageModifierSet: Ipc
  - type: Injurable
    damageContainer: SiliconIpc
```

and its parts, `ONYX Resources/Prototypes/Corvax/Body/Species/ipc.yml:325-330`:

```yaml
  # <Onyx-IPCDismemberment>
  - type: Woundable
    profile: IpcBodyPartProfile
  - type: BodyPart
    fractureProfile: null
  # </Onyx-IPCDismemberment>
```

**Finding worth flagging (§5.3):** Onyx gives `MobIpc` `bloodlossDamage: Bloodloss: 0.5` but its damage container `SiliconIpc` (`ONYX Resources/Prototypes/Corvax/Damage/containers.yml:2-9`) is:

```yaml
  id: SiliconIpc
  supportedGroups:
    - Brute
    - Burn
    - Electronic # <Onyx-IonDamage>
  supportedTypes:
    - Heat
    - Shock
```

No `Airloss` group, no `Bloodloss` type. `DamageableSystem` drops unsupported types silently (WG's identical loop: `WG Content.Shared/Damage/Systems/DamageableSystem.cs:270-272`, `if (!dict.TryGetValue(type, out var oldValue)) continue;`). **So in Onyx, an IPC leaks oil, loses oil volume, and takes no damage for it.** Bleeding out is purely cosmetic/diagnostic on an Onyx IPC. This is a real Onyx behaviour, not a Wolfgate artefact, and it drives the only user decision in this report.

### 1.4 Slime and diona in Onyx

`ONYX Resources/Prototypes/Body/Species/slime.yml:162-166`:
```yaml
  - type: Bloodstream
    bloodReferenceSolution: # TODO Color slime blood based on their slime color or smth
      reagents:
      - ReagentId: Slime
        Quantity: 600
```
Parts at `:236-239`: `- type: Woundable / profile: SlimeBodyPartProfile`, `fractureProfile: null`.

`ONYX Resources/Prototypes/Body/Species/diona.yml:111-115`:
```yaml
  - type: Bloodstream
    bloodReferenceSolution:
      reagents:
      - ReagentId: Sap
        Quantity: 600
```
Parts at `:230-233`: `profile: PlantBodyPartProfile`, `fractureProfile: null`.

Neither declares `WoundHost` — both inherit it from `BaseSpeciesMobOrganic`, exactly as WG's slime/diona inherit it from `BaseMobSpeciesOrganic` today.

### 1.5 The "stage" concept is metabolism, not circulation

The only place the word *stages* appears near a species is the slime circulator organ, `ONYX Resources/Prototypes/Body/Species/slime.yml:359`:

```yaml
    stages: [ Digestion, Metabolites ] # <Onyx-SlimeCirculator-edited>
```

That is `MetabolizerComponent.Stages` (`ONYX Content.Shared/Metabolism/MetabolizerComponent.cs`), i.e. *which chemical-processing stages this organ runs* — nothing to do with which stream a limb bleeds into. The `MetabolismStage`/`MetabolitesStage` fields on `CirculatoryStreamPrototype` exist so that a *future* second stream could own its own metabolism pipeline; at the pin nothing exercises them. **Confirms P5-2's premise that the stage metabolizer is not required for species bleeding.**

---

## 2. What Wolfgate has today (task item 2)

### 2.1 The trimmed `CirculatoryStreamSystem`

`WG Content.Server/_Onyx/Chemistry/Circulation/CirculatoryStreamSystem.cs` — 81 lines, four public members, **no `Initialize()`, no `Update()`, zero `SubscribeLocalEvent`**. Header comment lines 11-17 lists everything dropped per D15. The three that matter:

`:25-30`
```csharp
    public ProtoId<CirculatoryStreamPrototype> GetPartStream(Entity<WoundableComponent> part)
    {
        return _prototypes.TryIndex(part.Comp.Profile, out var profile)
            ? profile.CirculatoryStream
            : CirculatoryStreamPrototype.PrimaryStream;
    }
```

`:70-80`
```csharp
    public void SetBleedRates(EntityUid body, Dictionary<ProtoId<CirculatoryStreamPrototype>, float> rates)
    {
        if (!TryComp(body, out BloodstreamComponent? bloodstream))
            return;

        // WOLFGATE: D15, only the primary stream exists, so the CirculatoryStreamComponent bookkeeping
        // and the SynchronizeStreams fallback below it are both dead code in phase 1.
        _bloodstream.TryModifyWoundBleedProjection(body,
            rates.GetValueOrDefault(CirculatoryStreamPrototype.PrimaryStream) - bloodstream.BleedAmount,
            bloodstream);
    }
```

`:32-50` / `:52-68` — `TryGetPartSolution` / `TryGetStreamSolution`. Both return `false` for any non-primary stream.

The file sits under `Content.Server/` but declares `namespace Content.Shared._Onyx.Chemistry.Circulation;` (`:9`) — Onyx's namespace preserved, Wolfgate's assembly. That is why `WoundBleedingSystem` also had to move to `Content.Server`.

### 2.2 Consumers — a complete list

```
$ grep -rn "TryGetPartSolution\|TryGetStreamSolution\|GetPartStream\|SetBleedRates" \
    WG/Content.Shared WG/Content.Server WG/Content.Client WG/Content.IntegrationTests --include=*.cs
```
Outside the defining file, **three call sites, all in one method**:

- `WG Content.Server/_Onyx/Wounds/WoundBleedingSystem.cs:306` — `var stream = _circulation.GetPartStream((part, woundable));`
- `:319` — same, for a just-inserted part
- `:324` — `_circulation.SetBleedRates(body, streamRates);`

**`TryGetPartSolution` and `TryGetStreamSolution` have zero callers in Wolfgate.** (In Onyx they are consumed by the per-part injector — `ONYX Content.Shared/Chemistry/EntitySystems/InjectorSystem.cs:50, 210-222, 424-474` — and by `ONYX Content.Shared/_Onyx/Body/Systems/BodyInventorySlotSystem.cs:19`. Neither is ported, per D6/D7 scope.) So the entire surface P5-2 has to keep correct is `GetPartStream` → `SetBleedRates`.

### 2.3 How wound bleeding reaches the bloodstream in WG

`WG Content.Server/_Onyx/Wounds/WoundBleedingSystem.cs:292-331` (`RefreshBody`) — byte-for-byte the Onyx logic:

```csharp
        if (!_net.IsServer || !HasComp<WoundHostComponent>(body) ||
            !TryComp(body, out BloodstreamComponent? bloodstream))
            return;
        ...
                var stream = _circulation.GetPartStream((part, woundable));
                streamRates[stream] = streamRates.GetValueOrDefault(stream) + wound.Comp2.CurrentRate;
        ...
        _circulation.SetBleedRates(body, streamRates);
```

Per-wound rate, `:347-364` (identical to `ONYX …/WoundBleedingSystem.cs:347-364`):

```csharp
        wound.Comp.BaseRate = wound.Comp.BleedingSeverity.Float() * behavior.Rate * bleedingMultiplier;
```

and the multiplier gate, `WG …/WoundBleedingSystem.cs:375-387` — identical to Onyx `:374-386`:

```csharp
    private bool TryGetBleedingMultiplier(EntityUid part, out float multiplier)
    {
        multiplier = 1f;
        if (!TryComp(part, out WoundableComponent? woundable) ||
            !_prototypes.TryIndex(woundable.Profile, out var profile))
            return true;

        if (profile.BleedingMultiplier <= 0f)
            return false;

        multiplier = profile.BleedingMultiplier;
        return true;
    }
```

A profile with `bleedingMultiplier: 0` therefore forces `BaseRate = CurrentRate = 0` on every wound of that part. Complemented by `WG Content.Shared/_Onyx/Wounds/WoundSystem.cs:101-108`:

```csharp
    public bool CanBleed(Entity<WoundableComponent?> part)
    {
        ...
        return profile.BleedingMultiplier > 0f;
    }
```

which also blocks `ModifyBodyBleeding` from ever creating a `SystemicBleedingWound` on such a part (`WG …/WoundBleedingSystem.cs:180-183`). **`bleedingMultiplier: 0` is a complete, data-only "this species does not bleed" switch. No code change needed to use it.** (Onyx never uses it; see §5.2.)

Internal bleeding bypasses circulation entirely — `WG Content.Server/_Onyx/Wounds/WoundInternalBleedingSystem.cs:63-69`:

```csharp
            if (TryGetBody(core.HoldingPart, out var body) && TryComp(body, out BloodstreamComponent? bloodstream))
            {
                var amount = FixedPoint2.New(internalBleeding.Rate * internalBleeding.Severity.Float() * frameTime);
                if (amount > FixedPoint2.Zero)
                    _bloodstream.TryModifyBloodLevel(body, -amount, bloodstream);
            }
```

Reagent-agnostic: it drains `BloodSolution` of whatever `BloodReagent` is.

### 2.4 WG's `BloodstreamComponent` and the phase-1 guards

`WG Content.Server/Body/Components/BloodstreamComponent.cs` — server-only (`:15 [RegisterComponent, Access(typeof(BloodstreamSystem), typeof(ReactionMixerSystem))]`, no `[NetworkedComponent]`). Relevant fields:

| Field | line | YAML key | default |
|---|---:|---|---|
| `BloodReagent` (`ProtoId<ReagentPrototype>`) | 142-143 | `bloodReagent` | `"Blood"` |
| `BloodMaxVolume` | 133-134 | `bloodMaxVolume` | `300` |
| `ChemicalMaxVolume` | 126-127 | `chemicalMaxVolume` | `250` |
| `BleedAmount` (not a `DataField`) | 44-45 | — | 0, clamped `[0, MaxBleedAmount]` |
| `MaxBleedAmount` | 56-57 | `maxBleedAmount` | `10.0` |
| `BleedReductionAmount` | 50-51 | `bleedReductionAmount` | `0.33` |
| `BloodRefreshAmount` | 83-84 | `bloodRefreshAmount` | `1.0` per `UpdateInterval` |
| `BloodlossThreshold` | 62-63 | `bloodlossThreshold` | `0.9` |
| `BloodlossDamage` (required) | 69-70 | `bloodlossDamage` | — |
| `BloodlossHealDamage` (required) | 76-77 | `bloodlossHealDamage` | — |
| `BleedPuddleThreshold` | 89-90 | `bleedPuddleThreshold` | `1.0` |
| `DamageBleedModifiers` | 99-100 | `damageBleedModifiers` | `"BloodlossHuman"` |
| `BleedingAlert` | 183-184 | `bleedingAlert` | `"Bleed"` |
| `UpdateInterval` | 31-32 | `updateInterval` | `3 s` |

**vs Onyx (shared, rewritten):** `bloodReagent` is Wolfgate/wizden-classic; Onyx replaced it with `bloodReferenceSolution` (a whole `Solution`). `MetabolitesSolutionName`, `AdjustedUpdateInterval` — MISSING in WG. `ChemicalSolutionName`/`ChemicalSolution` — present in WG, absent in Onyx. `BloodSolutionName`/`BloodSolution` — SAME. The whole bleed block (`BleedAmount`, `BleedReductionAmount`, `MaxBleedAmount`, `BloodlossThreshold`, `BloodlossDamage`, `BloodlossHealDamage`, `BloodRefreshAmount`, `BleedPuddleThreshold`, `DamageBleedModifiers`, `BleedingAlert`) — SAME names and types in both.

Phase-1 guards already in place (`WG Content.Server/Body/Systems/BloodstreamSystem.cs`):

- `:213-215` GUARD E — `OnDamageChanged` returns early for `WoundHostComponent`.
- `:280` GUARD E2 — pallor examine skipped for wound hosts.
- `:411-430` GUARD E3 — `TryModifyBleedAmount` is a no-op for wound hosts; `internal TryModifyWoundBleedProjection` (`:417-420`) is the sole write path, and is what `SetBleedRates` calls.

Everything downstream of `BleedAmount` is reagent-agnostic: `Update()` at `:133-139` does `TryModifyBloodLevel(uid, -BleedAmount)` then the (no-op for hosts) decay; `TryModifyBloodLevel` at `:368-406` splits from `BloodSolution` into `TemporarySolution` and spills with `_puddleSystem.TrySpillAt`; refill at `:208`/`:377` uses `component.BloodReagent`. **Changing `bloodReagent` is the entire mechanism needed to make a species bleed something else.**

### 2.5 Per-species bloodstream values in WG today

`WG Resources/Prototypes/Entities/Mobs/base.yml:234-248`:

```yaml
# Used for mobs that have a bloodstream
- type: entity
  save: false
  id: MobBloodstream
  abstract: true
  components:
  - type: SolutionContainerManager
  - type: InjectableSolution
    solution: chemicals
  - type: Bloodstream
    bloodlossDamage:
      types:
        Bloodloss: 0.5
    bloodlossHealDamage:
      types:
        Bloodloss: -1
```

(Onyx's is the same shape — `ONYX Resources/Prototypes/Entities/Mobs/base.yml:269-281` — differing only in `SolutionManager` vs `SolutionContainerManager` and `InjectableSolution / solution: bloodstream` vs `chemicals`. SAME numbers.)

`MobBloodstream` is one of the parents of `BaseMobSpeciesOrganic` (`WG Resources/Prototypes/Entities/Mobs/Species/base.yml:241-249`), which is where phase 1 put `- type: WoundHost` (`:252`).

| WG species | `bloodReagent` | citation | wound host today? |
|---|---|---|---|
| Slime | `Slime` | `Entities/Mobs/Species/slime.yml:82-83` | **yes** (inherits) |
| Diona | `Sap` | `Entities/Mobs/Species/diona.yml:48-49` | **yes** (inherits) |
| Protogen | `Blood` (default) | `_Mono/Entities/Mobs/Species/protogen.yml:2` parents `BaseMobSpeciesOrganic`; `damageContainer: Biological` at `:31-33` | **no** — stripped at runtime by `WG Content.Shared/_WF/Wolfmed/Body/WolfmedWoundHostExclusionSystem.cs:12` (`ExcludedAncestors = { "BaseMobProtogen" }`) |
| **IPC (`MobIPC`)** | **none — no `Bloodstream` component at all** | see below | **no** |
| Arachnid / moth / chitinid | `CopperBlood` / `InsectBlood` | `…/arachnid.yml:47`, `…/moth.yml:51`, `_DV/…/chitinid.yml:63` | yes |
| Vox / resomi / hydrakin | `AmmoniaBlood` | `…/vox.yml:56`, `_Moffstation/…/resomi.yml:73`, `_Obelisk/…/hydrakin.yml:36` | yes |
| Gingerbread | `Sugar` | `…/gingerbread.yml:32` | yes |

**IPC is the gap.** `WG Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/ipc.yml:1-3` — `MobIPC`, `parent: PlayerSiliconHumanoidBase`. That base's bloodstream block is **commented out**, `WG Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/silicon_base.yml:155-170`:

```yaml
  #- type: Bloodstream This is left commented out because it's not necessary for a robot, but you may want it.
  #  damageBleedModifiers: BloodlossIPC
  #  bloodReagent: Oil
  #  bleedReductionAmount: 0
  #  bloodMaxVolume: 500
  #  chemicalMaxVolume: 0
  #  bleedPuddleThreshold: 3
  #  bleedRefreshAmount: 0
  #  bloodLossThreshold: 0
  #  maxBleedAmount: 14
  #  bloodlossDamage:
  #    types:
  #      Burn: 1.5
  #  bloodlossHealDamage:
  #    types:
  #      Burn: 0
```

Notes on that dead block, because it will be tempting to just uncomment it:
- `BloodlossIPC` **does not exist** as a prototype (`grep -rn "BloodlossIPC" WG/Resources/Prototypes` → only this comment). Uncommenting it verbatim is a prototype-load error.
- `bleedRefreshAmount` and `bloodLossThreshold` are **misspellings** — the real keys are `bloodRefreshAmount` and `bloodlossThreshold` (`BloodstreamComponent.cs:83-84`, `:62-63`). RT drops unknown mapping keys silently, so those two lines would have done nothing (same class of bug as phase 2's `emotes:`/`emotesThreshold:`, DECISIONS §8.2-3).
- `types: { Burn: 1.5 }` is invalid: `Burn` is a damage **group** (`WG Resources/Prototypes/Damage/groups.yml:9-16`), not a type. It would have to be `groups:` or a concrete type.

IPC support pieces that **do** exist: `OrganIPCPump` — `WG Resources/Prototypes/_EinsteinEngines/Body/Organs/ipc.yml:54-67`, `name: micro pump`, `description: "A micro pump, used to circulate coolant."`, `- type: Organ / slotId: pump`, `- type: Heart`, and a `SolutionContainerManager` on `BaseIPCOrgan` (`:13-18`) holding `Oil 10`. It is slotted into the IPC torso at `WG Resources/Prototypes/_EinsteinEngines/Body/Prototypes/ipc.yml:21` (`pump: OrganIPCPump`) and declared on the torso part at `WG Resources/Prototypes/_EinsteinEngines/Body/Parts/ipc.yml:35-36`. The pump's `Metabolizer` block is commented out (`ipc.yml:70-76`) — irrelevant to bleeding.

`Oil` reagent — **SAME** in both forks. `WG Resources/Prototypes/Reagents/Consumable/Food/ingredients.yml:186-200` vs `ONYX Resources/Prototypes/Reagents/Consumable/Food/ingredients.yml:168-182`: identical `color: "#b67823"`, `boilingPoint: 300.0`, `meltingPoint: -16.0`, **`flammability: 2`**, `tileReactions: [!type:FlammableTileReaction {}]`. See §6.4 — IPC oil puddles are flammable, in both forks.

---

## 3. External-symbol status table (required by task)

Every symbol P5-2 touches or would touch, SAME / DIFFERENT / MISSING in WG.

| Symbol | Status in WG | Citation |
|---|---|---|
| `CirculatoryStreamPrototype` (`type: circulatoryStream`) | **SAME**, ported | `WG Content.Shared/_Onyx/Chemistry/Circulation/CirculatoryStreamPrototype.cs:10` |
| `CirculatoryStreamPrototype.PrimaryStream` | **SAME** (`"Organic"`) | ibid `:12` |
| `CirculatoryStreamPrototype.MetabolismStage` / `.MetabolitesStage` | **MISSING** — commented out | ibid `:26-31`, `// WOLFGATE (D15/WP3#2)` |
| `MetabolismStagePrototype` | **MISSING** entirely | no file; `Content.Shared/Body/Prototypes/MetabolismGroupPrototype.cs` is a different concept |
| `CirculatoryStreamComponent` | **SAME**, ported, **never attached to anything** | `WG Content.Shared/_Onyx/Chemistry/Circulation/CirculatoryStreamComponent.cs` |
| `CirculatoryStreamSystem.GetPartStream` | **SAME** signature | `WG Content.Server/_Onyx/Chemistry/Circulation/CirculatoryStreamSystem.cs:25` |
| `CirculatoryStreamSystem.SetBleedRates` | **DIFFERENT** — primary-key-only body (D15) | ibid `:70-80` |
| `CirculatoryStreamSystem.TryGetPartSolution` / `TryGetStreamSolution` | **DIFFERENT** — primary-only; **no callers in WG** | ibid `:32-68` |
| `SharedSolutionContainerSystem.TryCreateCirculatorySolution` / `TryDeleteCirculatorySolution` | **MISSING** — the partial was never ported | `grep -rn "CirculatorySolution" WG` → 0 hits |
| `SolutionManagerComponent` | **MISSING**; WG uses `SolutionContainerManagerComponent` | `WG Content.Shared/Chemistry/Components/SolutionManager/SolutionContainerManagerComponent.cs:13` (live, not obsolete) |
| `BleedModifierEvent`, `MetabolismExclusionEvent`, `StasisBedBuckledComponent` | **MISSING** | 0 hits in WG; only used by the dropped `Update()` loop |
| `BodyPartProfilePrototype.CirculatoryStream` | **SAME**, default `"Organic"` | `WG Content.Shared/_Onyx/Wounds/WoundPrototype.cs:157` |
| `BodyPartProfilePrototype.BleedingMultiplier` | **SAME** | ibid `:164` (`public float BleedingMultiplier = 1f;`) |
| `BodyPartProfilePrototype.TreatmentCapabilities` / `.Scarrable` / `.CanFeelPain` / `.PassiveRecoveryMultiplier` / `.BedRecoveryMultiplier` | **SAME** | ibid `:153-181` |
| `WoundableComponent.Profile` | **SAME**, default `"OrganicBodyPartProfile"` | `WG Content.Shared/_Onyx/Wounds/WoundDamageComponents.cs:160-161` |
| `BloodstreamComponent.BloodReagent` (`bloodReagent`) | **SAME as wizden**, **DIFFERENT from Onyx** (Onyx: `bloodReferenceSolution`, a `Solution`) | `WG Content.Server/Body/Components/BloodstreamComponent.cs:142-143` |
| `BloodstreamSystem.TryModifyBloodLevel` | **SAME** name, **DIFFERENT** arity (`EntityUid, FixedPoint2, BloodstreamComponent?`) | `WG Content.Server/Body/Systems/BloodstreamSystem.cs:368` |
| `BloodstreamSystem.TryModifyWoundBleedProjection` | **WOLFGATE-authored** (GUARD E3), mirrors Onyx's `internal` method | ibid `:417-420` |
| `damageContainer: SiliconIpc` | **MISSING** in WG (`Silicon` exists, different set) | `WG Resources/Prototypes/Damage/containers.yml:27-34` |
| `damageModifierSet: Ipc` (Onyx) / `IPC` (WG) | **DIFFERENT id casing**; WG uses `IPC` | `WG …/silicon_base.yml:28` |
| Reagent `Oil` | **SAME** | `WG Resources/Prototypes/Reagents/Consumable/Food/ingredients.yml:186` |
| Reagent `Slime` | **SAME** | `WG Resources/Prototypes/Reagents/biological.yml:59` |
| Reagent `Sap` | **SAME** | `WG Resources/Prototypes/Reagents/biological.yml:82` |
| `OrganIPCPump` (WG) / `OrganIpcHeart`-equivalent (Onyx) | **DIFFERENT id**, same role (`- type: Heart`) | `WG …/_EinsteinEngines/Body/Organs/ipc.yml:55` |

### 3.1 `SubscribeLocalEvent` pair audit

**The chosen route adds no subscriptions at all.** For completeness, the current state:

- `WG Content.Server/_Onyx/Chemistry/Circulation/CirculatoryStreamSystem.cs` has **no `Initialize()` and no subscriptions**. Onyx's five (`MetabolizerComponent+ComponentStartup`, `CirculatoryStreamComponent+{AfterAutoHandleStateEvent, ComponentShutdown, MetabolismExclusionEvent, SolutionRelayEvent<ReactionAttemptEvent>}` — `ONYX …/CirculatoryStreamSystem.cs:38-42`) were all dropped in phase 1 and stay dropped.
- Therefore **no new `(component, event)` pair is introduced, and no collision with the duplicate-directed-subscription crash is possible.**
- Cross-check of the one pair that *would* collide if the full system ever came back: `SolutionRelayEvent<ReactionAttemptEvent>` is already subscribed on `BloodstreamComponent` by `WG Content.Server/Body/Systems/BloodstreamSystem.cs:57-58`. Different component type, so even then it is legal — but noting it for the phase-6+ file.

### 3.2 Name-collision audit

New names introduced by the chosen route: **one** optional prototype id (a damage container, §5.3). Checks run across all of `WG/Content.{Shared,Server,Client}` and `WG/Resources/Prototypes`, `_Onyx` and `_WF` included:

- `grep -rln "Circulat" WG/Content.* WG/Resources/Prototypes` (excluding `bin`/`obj`) → `Content.Shared/Power/Generation/Teg/SharedTeg.cs` (the TEG **circulator**, an unrelated `TegCirculator*` machine), plus the three Wolfmed files. **No collision.**
- `grep -rn "type: circulatoryStream" WG/Resources` → one hit, `WG Resources/Prototypes/_Onyx/Chemistry/circulatory_streams.yml:1`, content identical to Onyx's (`- type: circulatoryStream` / `  id: Organic`). **No second stream is being added, so no id collision is possible.**
- `grep -rn "BloodlossIPC\|SiliconIpc" WG/Resources/Prototypes` → only the dead comment at `silicon_base.yml:156`. Both names are free. Recommended new id `BloodlossSiliconWolfmed` / `SiliconWolfmed` (§5.3) — neither exists.
- Damage container ids currently defined (`WG Resources/Prototypes/Damage/containers.yml`): `Biological, Inorganic, StructuralInorganic, Silicon, Shield, Box, ShadowHaze, ManifestedSpirit, BiologicalMetaphysical, OrganicPart`. No `SiliconWolfmed`.

---

## 4. The decision (task item 3): route (a), with one guard

### 4.1 Is anything hard-wired to the Organic stream?

Three candidates were checked.

**(i) `CirculatoryStreamComponent` on the mob — NO.** The component is never added anywhere in WG: `EnsureComp<CirculatoryStreamComponent>` appeared only in Onyx's `SynchronizeStreams`, which D15 dropped. Nothing reads it. It is inert ported data.

**(ii) `GetPartStream` lookups — NO.** `WG …/CirculatoryStreamSystem.cs:25-30` faithfully returns whatever the profile says. It is stream-agnostic.

**(iii) `SetBleedRates` — YES, this one is hard-wired.** `WG …/CirculatoryStreamSystem.cs:77-79` reads `rates.GetValueOrDefault(CirculatoryStreamPrototype.PrimaryStream)`. Any rate filed under a non-`Organic` key is **discarded without trace**, and because the expression is `<that value> - bloodstream.BleedAmount`, an all-non-Organic body converges to `BleedAmount == 0`.

So the answer to the task's question is: **one method is hard-wired, and it is hard-wired in the direction that makes route (a) mandatory rather than optional.** As long as no profile overrides `circulatoryStream` — which is exactly what Onyx does — the hard-wiring is invisible and correct.

### 4.2 Route (a) is chosen. Why it is *correct*, not merely cheap

1. **Fidelity.** Onyx's own IPC/Slime/Plant/Cybernetic profiles leave `circulatoryStream` at `Organic` (§1.1, §1.2). Route (a) is not an approximation of Onyx; it *is* Onyx.
2. **Behaviour parity.** Species differentiation in Onyx comes from three data fields, all of which WG already supports unchanged: the profile's `bleedingMultiplier` (`WoundPrototype.cs:164`, consumed at `WoundBleedingSystem.cs:359` and `WoundSystem.cs:107`), the profile's `supportedWounds` (consumed at `WoundSystem.cs:121`), and the mob's blood reagent (`BloodstreamComponent.cs:142-143`). There is no fourth mechanism that a second stream would supply.
3. **No dead weight.** A second `circulatoryStream` prototype would need `MetabolismStage`/`MetabolitesStage`, which WG commented out (`CirculatoryStreamPrototype.cs:26-31`), and `TryCreateCirculatorySolution`, which WG never ported. A "stream without the stage metabolizer" would be a prototype that no code path can initialise — it would produce exactly the silent-zero-bleed failure of §4.1(iii).

### 4.3 The hard rule PLAN5 must carry

> **P5-2 RULE: no `bodyPartProfile` in phase 5 may set `circulatoryStream`.** Every new profile (`IpcBodyPartProfile`, `SlimeBodyPartProfile`, `PlantBodyPartProfile`, `CyberneticBodyPartProfile`) omits the key and inherits `"Organic"`, exactly as Onyx's do. Setting it to anything else silently zeroes that species' bleeding.

### 4.4 Recommended 1-line hardening (removes the silent-failure mode)

`WG Content.Server/_Onyx/Chemistry/Circulation/CirculatoryStreamSystem.cs:77-79`, replace the `GetValueOrDefault` with a sum over all streams:

```csharp
        // WOLFGATE: P5-2. Wolfgate has exactly one blood solution per body (server-only BloodstreamComponent),
        // so every stream's bleed rate lands in the same place. Summing instead of reading only the primary key
        // makes a profile that sets `circulatoryStream:` degrade to "bleeds normally" instead of "silently stops
        // bleeding". Behaviour is identical while Organic is the only stream, which it is.
        var total = 0f;
        foreach (var rate in rates.Values)
            total += rate;

        _bloodstream.TryModifyWoundBleedProjection(body, total - bloodstream.BleedAmount, bloodstream);
```

Requires `using System.Linq;` only if written as `rates.Values.Sum()`; the explicit loop above needs no new using. **Difficulty: trivial. Risk: none — with one stream, `sum == GetValueOrDefault(Organic)` identically.** Strongly recommended: it converts the single remaining trap in this subsystem into a no-op.

### 4.5 Exact YAML/edits for the chosen route

Everything below is P5-2's complete footprint. **(A) is required; (B) is the user decision of §5.3; (C) is §4.4.**

**(A) `MobIPC` gains a bloodstream.** `WG Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/ipc.yml`, inside `MobIPC`'s `components:` (id and parent unchanged; do **not** add `MobBloodstream` to `parent:` — see note):

```yaml
  # WOLFGATE (P5-2): ONYX Resources/Prototypes/Corvax/Body/Species/ipc.yml:100-113 (<Onyx-IPCWounds>).
  # Onyx's bloodReferenceSolution (Oil 250) maps onto Wolfgate's classic bloodReagent + bloodMaxVolume.
  # Wolfmed wound bleeding writes BleedAmount through BloodstreamSystem.TryModifyWoundBleedProjection
  # (GUARD E3), and TryModifyBloodLevel spills BloodReagent — so an IPC leaks oil, not blood.
  # No circulatoryStream: IpcBodyPartProfile stays on the primary Organic stream, exactly as Onyx's does.
  - type: Bloodstream
    bloodReagent: Oil
    bloodMaxVolume: 250
    chemicalMaxVolume: 0
    bleedPuddleThreshold: 3
    bloodlossDamage:
      types:
        Bloodloss: 0.5
    bloodlossHealDamage:
      types:
        Bloodloss: -1
```

Notes on each key:
- `bloodReagent: Oil` — the whole point. `Oil` is SAME in both forks (§2.5).
- `bloodMaxVolume: 250` — matches Onyx's `Oil Quantity: 250`. Default would be 300.
- `chemicalMaxVolume: 0` — from EE's own dead block; keeps IPCs from becoming a chem sponge. **Verify at implementation time** that a 0-volume chemical solution does not trip `BloodstreamSystem.TryModifyBloodLevel:392-396` (it splits `ChemicalSolution.Volume / 10` into spilled blood — with volume 0 that split is empty, which is fine, but run the spawn-and-delete smoke test).
- `bleedPuddleThreshold: 3` — from EE's dead block; oil pools less readily than blood. Optional; drop it for pure Onyx defaults (D4).
- `bloodlossDamage` / `bloodlossHealDamage` — `DataField(required: true)` on `BloodstreamComponent.cs:69,76`, so **they cannot be omitted**. Values copied from Onyx. **On the `Silicon` container these are inert** — that is §5.3.
- **Do not** copy Onyx's `bleedReductionAmount`/`maxBleedAmount` tweaks: `BleedReductionAmount` is already dead for wound hosts (GUARD E3 no-ops the decay call, `BloodstreamSystem.cs:427-429`), and `MaxBleedAmount` at the default 10 is what every other wound host uses.
- **Why not add `MobBloodstream` to `parent:` like Onyx does:** it would also bring `- type: InjectableSolution / solution: chemicals`, making every IPC syringe-injectable — a gameplay change outside P5-2. `BloodstreamSystem.OnComponentInit` calls `EnsureSolution`, which does `EnsureComp<SolutionContainerManagerComponent>` (`WG Content.Shared/Chemistry/EntitySystems/SharedSolutionContainerSystem.cs:1056`), so the manager component is **not** needed in YAML. Add `- type: SolutionContainerManager` explicitly only if the implementer prefers it declared.

**(B) `MobIPC` damage container (user decision, §5.3).** New file `WG Resources/Prototypes/_WF/Wolfmed/Damage/containers.yml`:

```yaml
# WOLFGATE (P5-2): Silicon + Airloss. The stock Silicon container (Resources/Prototypes/Damage/containers.yml:27)
# has no Bloodloss type, so an IPC's oil loss — and WolfmedBodyPartLifecycleSystem's P3-D1 vital-part charge —
# are silently discarded by DamageableSystem's type filter. A separate id keeps borgs and every other Silicon
# entity on the stock container (D2). Onyx has the same hole; this is a deliberate Wolfgate divergence.
- type: damageContainer
  id: SiliconWolfmed
  supportedGroups:
  - Brute
  - Airloss
  supportedTypes:
  - Heat
  - Shock
  - Radiation
```

and on `MobIPC` (`WG Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/ipc.yml`):

```yaml
  - type: Damageable # WOLFGATE (P5-2): Silicon + Airloss so oil loss and P3-D1 decapitation damage land.
    damageContainer: SiliconWolfmed
    damageModifierSet: IPC
```

(`damageModifierSet: IPC` must be repeated — `Damageable` is one component, not a merge.) Stock `Silicon` is at `WG Resources/Prototypes/Damage/containers.yml:27-34`:
```yaml
- type: damageContainer
  id: Silicon
  supportedGroups:
  - Brute
  supportedTypes:
  - Heat
  - Shock
  - Radiation
```
`Airloss` = `Asphyxiation` + `Bloodloss` (`WG Resources/Prototypes/Damage/groups.yml:22-27`). Adding it also lets the tourniquet's `Asphyxiation: 5` cost apply to IPCs (§7.2) — desirable symmetry.

**(C)** the `SetBleedRates` sum from §4.4.

**And nothing else.** Specifically **not** needed: a second `circulatoryStream` prototype; any edit to `circulatory_streams.yml`; any edit to `CirculatoryStreamPrototype.cs` / `CirculatoryStreamComponent.cs`; any edit to slime, diona, or cybernetic-limb prototypes for circulation purposes; any new system, component, event, or subscription.

---

## 5. Per-species outcome, and the user decision

### 5.1 What each species gets, circulation-wise

| Species | `bloodReagent` | profile `bleedingMultiplier` | bleeds? | what a player sees | circulation work |
|---|---|---:|---|---|---|
| Human & organic lineage | `Blood` | 1.0 | yes | unchanged from phase 1-4 | **none** |
| **Slime** | `Slime` (already set) | 1.15 | yes, **15 % faster** | green slime puddles, bleeds out a bit quicker than a human; `SlimeBodyPartProfile` also makes them unscarrable and fracture-free | **none** |
| **Diona** | `Sap` (already set) | 1.0 | yes | leaks sap at the human rate; can scar (`PlantBodyPartProfile` leaves `scarrable` default true) | **none** |
| **IPC** | **`Oil` (to add)** | 1.0 | yes | **leaks flammable oil at the full organic rate**, `Bleed` alert, oil puddles; mechanical wounds impair then disable the limb | **edit (A)** (+ (B)) |
| **Cybernetic limb on an organic body** | the host's own reagent | 0.5 | yes, half rate | a cybernetic arm leaks the *wearer's* blood at half rate — Onyx behaviour, and correct: the limb is plumbed into the host's circulation | **none** |
| **Protogen** | `Blood` (default) | n/a — not a wound host | n/a | unchanged unless P5-1 opts it in | **none** (see §5.4) |

### 5.2 Should any species use `bleedingMultiplier: 0`?

Route (a) as the DECISIONS text phrases it offers "bleed multiplier 0 **/** a native bloodstream reagent". **Recommendation: use the reagent, not the zero, for every species — because Onyx does, and because the zero is strictly worse.**

`bleedingMultiplier: 0` does work perfectly as a data-only switch (§2.3), but it removes more than bleeding: `CanBleed` returning false also blocks `SystemicBleedingWound` creation (`WoundBleedingSystem.cs:180-183`), which is what `ModifyBodyBleeding` uses, which is in turn what `HealingSystem.Wolfmed`'s `BloodlossModifier` path drives (`WG Content.Server/_WF/Wolfmed/Medical/HealingSystem.Wolfmed.cs:137`). A `bleedingMultiplier: 0` species is also immune to the tourniquet (`TourniquetSystem.cs:112-113` requires `GetPartRate(part) > 0f`) and shows nothing in the phase-4 analyzer's bleeding row. That is a lot of phase-1-to-4 machinery switched off for one species.

Keep `bleedingMultiplier: 0` in reserve for a future truly-fluidless chassis (a borg chassis, say). Do not use it for IPC or cybernetics.

### 5.3 **USER DECISION: should an IPC's oil loss actually hurt it?**

The facts:

- WG's `Silicon` container supports `Brute, Heat, Shock, Radiation` (`WG Resources/Prototypes/Damage/containers.yml:27-34`). **No `Bloodloss`.**
- `DamageableSystem.TryChangeDamage` drops unsupported types with no log (`WG Content.Shared/Damage/Systems/DamageableSystem.cs:270-272`).
- So with edit (A) alone, `BloodstreamSystem.Update`'s bloodloss branch (`:142-149`) computes `amt` and applies nothing. **An IPC would leak oil forever with no consequence.**
- **Onyx has the identical hole** (§1.3): `SiliconIpc` also lacks `Airloss`, so Onyx IPCs also never take bloodloss damage. Onyx defaults (D4) = cosmetic bleeding.
- A second consequence: phase 3's P3-D1 vital-part charge (`WG Content.Server/_WF/Wolfmed/WolfmedBodyPartLifecycleSystem.cs:89-94`) deliberately uses `Bloodloss` because it is *not* in `WoundHostComponent.LocalizedDamageTypes`. On a `Silicon`-container IPC **that charge is dropped too** — decapitating an IPC wound host would cost it nothing.

| | **Option 1 — faithful (no edit B)** | **Option 2 — Wolfgate divergence (edit B)** |
|---|---|---|
| Cost | 0 | 1 new `_WF` prototype + 1 marked line |
| IPC bleeding | volume drains, oil puddles, `Bleed` alert, analyzer shows a rate — **no damage, ever** | oil loss below 90 % deals `Bloodloss 0.5 / (0.1 + pct)` per 3 s, scaling as it empties; an IPC can bleed out |
| IPC decapitation (P3-D1) | charges 0 | charges the head's accumulated damage as Bloodloss |
| Tourniquet cost on IPC | `Blunt 5` only (Asphyxiation dropped) | `Blunt 5 + Asphyxiation 5`, same as everyone |
| Side effect | — | `BloodstreamSystem.Update:154-159` also applies `TryApplyDrunkenness` + `DoStutter` while below threshold → a leaking IPC slurs and staggers. Thematically "degraded processing"; mechanically it is the same wobble every bleeding human gets |
| Risk to non-wound-hosts (D2) | none | **none** — a *new* container id, applied only to `MobIPC`. Borgs, cyborg parts and cybernetic limb entities keep stock `Silicon` verbatim |

**Recommendation: Option 2.** Bleeding with no consequence is a trap: the phase-4 analyzer panel will print a bleed rate and a clotting phase for a patient who cannot be harmed by it, and a medic who tourniquets an IPC will have achieved nothing measurable. It also restores the phase-3 decapitation charge that D2-safe code already computes and throws away. It costs one prototype, is confined to one entity, and it matches the user's standing preference for more lethality (DECISIONS §8.6-1). Record it as a Wolfgate balance deviation from Onyx.

If Option 1 is chosen instead, PLAN5 must say so explicitly in the guidebook/analyzer copy ("IPCs leak coolant but do not bleed out"), or players will misread the readout.

*(A third option — `bloodlossDamage: {types: {Heat: 0.5}}` on the stock `Silicon` container — was evaluated and rejected: `bloodlossHealDamage` would then passively heal `Heat`, i.e. free self-repair of laser damage every 3 s while topped up, with `ignoreResistances: true` (`BloodstreamSystem.cs:166-170`). Setting the heal to 0 avoids that but then the IPC never recovers the damage, which is worse than either option above. `Bloodloss` is the only self-limiting type, hence Option 2.)*

### 5.4 Protogen (cross-reference to P5-1)

Not a circulation question, but it lands here because the answer is bloodstream-shaped. `BaseMobProtogen` uses `damageContainer: Biological` (`WG Resources/Prototypes/_Mono/Entities/Mobs/Species/protogen.yml:31-33`) and inherits `MobBloodstream` with `bloodReagent: Blood`. **If P5-1 makes protogen a cybernetic wound host** (removing `"BaseMobProtogen"` from `WolfmedWoundHostExclusionSystem.ExcludedAncestors`, `WG Content.Shared/_WF/Wolfmed/Body/WolfmedWoundHostExclusionSystem.cs:12`), then circulation needs **only** a `bloodReagent: Oil` override to make it leak oil — its `Biological` container already supports `Bloodloss`, so edit (B) is not needed for it. If P5-1 leaves protogen excluded, P5-2 does nothing.

---

## 6. Cross-cutting consequences to write into PLAN5

### 6.1 Damage-container gaps on IPC parts (P5-1's problem, flagged here)

`IpcBodyPartProfile.acceptedDamageTypes` lists `Blunt, Slash, Piercing, Heat, Cold, Shock, Caustic` (`ONYX …/wounds.yml:43-49`). But WG's IPC parts parent `BasePartInorganic` (`WG Resources/Prototypes/_EinsteinEngines/Body/Parts/ipc.yml:3`) → `damageContainer: Inorganic` (`WG Resources/Prototypes/Body/Parts/base.yml:14-15`), which supports `Brute` + `Heat, Shock` **only** (`WG Resources/Prototypes/Damage/containers.yml:10-16`). **`Cold` and `Caustic` routed to an IPC limb will be dropped by the part's own `DamageableComponent`.** Onyx's IPC parts sit on its own `OrganIpcExternal` chain with a different container. P5-1 must decide whether to trim the profile's `acceptedDamageTypes` to match, or give IPC parts a `_WF` container. Same applies to cybernetic parts (`WG Resources/Prototypes/_Shitmed/Body/Parts/cybernetic.yml:10-11`, `damageContainer: Silicon` — no Cold, no Caustic).

### 6.2 Internal bleeding is organ-driven, so IPCs get none for free

`InternalBleedingWound` is never created in C# in WG. Its only producers are `WolfmedOrganComponent.destructionWound` entries — `WG Resources/Prototypes/_WF/Wolfmed/Body/organs.yml:54, 72, 89, 106, 124` (heart, lungs, liver, stomach, kidneys; brain and eyes have none). Those are attached **human-lineage only** (§8.6-7, header comment at `organs.yml:13`) via `parent:` edits in `Resources/Prototypes/Body/Organs/human.yml`. IPC organs (`OrganIPCPump`, `PositronicBrain`, `OrganIPCEyes`) carry no `WolfmedOrgan`, so **no IPC can ever develop internal bleeding** — which exactly matches `IpcBodyPartProfile` omitting `InternalBleedingWound`. Consistent by construction; zero work.

Same reasoning applies to slime and diona in the *other* direction: their profiles **do** list `InternalBleedingWound`, but WG's slime body uses `OrganSlimeLungs` etc. (`WG Resources/Prototypes/Body/Prototypes/slime.yml:20`), which also carry no `WolfmedOrgan`. So in practice slime/diona internal bleeding is reachable only through the surgery path (`WolfmedWoundSurgeryTest.cs:331` creates one manually) until organ data is extended. Not a P5-2 blocker; note it in the manifest so the profile's `supportedWounds` entry is not read as a shipped feature.

### 6.3 Analyzer / examine text (P5-5 cross-reference)

The circulation side surfaces the species reagent in exactly two user-visible places, both of which P5-5 owns:
- the `Bleed` alert (`BloodstreamComponent.cs:183-184`), shown by `TryModifyBleedAmount:434-441` — currently named for blood regardless of reagent;
- the phase-4 `WolfmedDiagnosticPanel` bleeding row and the phase-2 `_Onyx/HealthExaminable` part text.

P5-2 changes no strings. If P5-5 wants "leaking oil", the cheapest hook is the profile's `treatmentCapabilities` (`Mechanical`/`Electrical` vs `Biological`) — already on the prototype and already read by phase 4's treatment matching — rather than anything circulation-side.

### 6.4 Oil puddles are flammable

`Oil` has `flammability: 2` and `!type:FlammableTileReaction` (`WG Resources/Prototypes/Reagents/Consumable/Food/ingredients.yml:196-200`) — identical in Onyx. `TryModifyBloodLevel` spills the temporary solution via `_puddleSystem.TrySpillAt` (`WG Content.Server/Body/Systems/BloodstreamSystem.cs:398`). **A bleeding IPC lays down a flammable trail.** This is Onyx-faithful and probably desirable, but it is a genuine new hazard in firefights and belongs in the phase-5 status doc and the guidebook. `Slime` and `Sap` (`WG Resources/Prototypes/Reagents/biological.yml:59, 82`) have no such reaction.

### 6.5 Blood refresh

`BloodRefreshAmount` defaults to `1.0` per 3 s (`BloodstreamComponent.cs:83-84`), unconditional while alive (`BloodstreamSystem.cs:126-130`). Onyx's `MobIpc` does not override it, so an Onyx IPC regenerates oil. Edit (A) as written keeps that default (D4). If the user wants IPCs to need a refill (welder/oil can) rather than self-topping-up, add `bloodRefreshAmount: 0` — one key, and note that with Option 2 the IPC then cannot recover from a bad bleed without help. **Recommendation: keep the default for phase 5; revisit in the balance pass.** This is not a blocker either way.

---

## 7. Internal bleeding and tourniquets, per profile (task item 4)

| Profile | internal bleeding | tourniquet | why | code change |
|---|---|---|---|---|
| `OrganicBodyPartProfile` | yes (organ destruction, 5 organs) | yes | unchanged | none |
| `IpcBodyPartProfile` | **no** | **yes** | `InternalBleedingWound` absent from `supportedWounds` (`ONYX …/wounds.yml:49-57`) **and** no IPC organ carries `destructionWound` (§6.2). `bleedingMultiplier: 1` → `GetPartRate > 0` → `TourniquetSystem.CanApply` passes (`WG Content.Server/_Onyx/Medical/Tourniquet/TourniquetSystem.cs:112-113`) | none |
| `CyberneticBodyPartProfile` | **no** | **yes**, on a half-rate leak | `InternalBleedingWound` and `SystemicBleedingWound` both absent (`ONYX …/wounds.yml:110-116`); `bleedingMultiplier: 0.5` still > 0 | none |
| `SlimeBodyPartProfile` | listed, unreachable today (§6.2) | yes | `bleedingMultiplier: 1.15` | none |
| `PlantBodyPartProfile` | listed, unreachable today (§6.2) | yes | `bleedingMultiplier: 1` | none |

### 7.1 What a tourniquet does on an IPC / cybernetic limb

`TourniquetSystem.Apply` (`:95-110`) sets `BleedingTreatment.Clamped` on every wound on the selected part with `CurrentRate > 0`. Reagent-agnostic, profile-agnostic. On an IPC it clamps the `IpcMechanicalDamageWound`'s 0.08-per-severity leak; on a cybernetic arm it clamps the `CyberneticMechanicalDamageWound`'s. **Nothing to port, nothing to gate.**

Protogen keeps losing tourniquet function entirely for as long as it is excluded from `WoundHost` (P4-D11) — unchanged by P5-2.

### 7.2 The one asymmetry

`Tourniquet`'s cost is `Blunt: 5` + `Asphyxiation: 5` (`WG Resources/Prototypes/Entities/Objects/Specific/Medical/healing.yml:281-284`), applied through `_damage.TryApplyPartDamage` (`TourniquetSystem.cs:89`). On IPC/cybernetic parts (`Inorganic`/`Silicon` containers) `Asphyxiation` is dropped, so a tourniquet on a robot limb costs **half** what it costs on flesh. Under Option 2 of §5.3 this is fixed for the IPC *mob*, but the cost is applied to the **part**, not the mob, so the part container still filters it. **Recommendation: accept and document.** Equalising it would mean a `_WF` container for IPC/cybernetic parts, which belongs to §6.1's decision, not here.

---

## 8. What the stage-metabolizer rewrite would add — explicitly OUT OF SCOPE

Per P5-2 ("Do not port `MetabolismStagePrototype`/`SolutionManagerComponent` in phase 5"), none of the following is planned. Listed so the boundary is unambiguous and so a future phase knows what it is buying.

The rewrite's prerequisites in WG (all currently MISSING, §3):
1. `MetabolismStagePrototype` and the stage-based `MetabolizerComponent` (Onyx: `Content.Shared/Metabolism/…`, shared + networked; WG: `Content.Server/Body/Components/MetabolizerComponent.cs`, server-only, `List<MetabolismGroupEntry>` with no `Solutions` dict and no `Stages` set).
2. `SolutionManagerComponent` + `SharedSolutionContainerSystem.CreateDefaultSolution` (WG is still on the live, non-obsolete `SolutionContainerManagerComponent`).
3. `SharedSolutionContainerSystem.CirculatoryStreams.cs` (`TryCreateCirculatorySolution` / `TryDeleteCirculatorySolution`).
4. `BleedModifierEvent`, `MetabolismExclusionEvent`, `StasisBedBuckledComponent`.
5. A shared/predicted `BloodstreamComponent` — or an accepted permanent split, since Onyx's `CirculatoryStreamSystem` is `Content.Shared` and queries `BloodstreamComponent` + `MetabolizerComponent` directly (`ONYX …/CirculatoryStreamSystem.cs:51`), which cannot compile in `Content.Shared` while both are server-only in WG.
6. Restoring the five dropped subscriptions and `Update()`.

What it would then *buy*, feature by feature (`ONYX Content.Shared/_Onyx/Chemistry/Circulation/CirculatoryStreamSystem.cs`):

- **A genuinely second fluid on one body.** `InitializeStream` (`:252-290`) creates three extra solutions per stream (`SolutionName`, `MetabolitesSolutionName`, `TemporarySolutionName`), sizes them from `ReferenceSolution.Volume * MaxVolumeModifier`, and fills them. That is the only thing a stream prototype *does*. Today one body = one `bloodstream` solution; after the rewrite, a chimeric body could carry blood **and** oil simultaneously and bleed each from the right limbs.
- **Per-stream bleed-out with its own puddle.** `Update()` (`:45-98`) ticks every non-primary stream: raises `BleedModifierEvent`, halves the rate on a stasis bed (`StasisBedBuckledComponent`), floors dead bodies at 65 % of the reference volume, splits into the stream's own temporary solution and spills it. All of that is duplicated-but-separate from what `BloodstreamSystem` already does for the primary stream.
- **Per-stream metabolism.** `ConfigureMetabolizer` (`:200-250`) wires each stream's `MetabolismStage`/`MetabolitesStage` into the metabolizer's `Solutions` dict and `Stages` set, with `HasStageConflict` (`:292-308`) refusing two streams that would claim the same stage. This is the piece that needs the Onyx metabolizer wholesale, and it is what makes "IPC chems metabolise out of the oil, not the blood" possible.
- **Reaction isolation per stream.** `OnReactionAttempt` (`:358-382`) cancels entity-spawning/area reactions inside a stream solution. WG already gets the equivalent for the primary stream from `BloodstreamSystem` (`WG Content.Server/Body/Systems/BloodstreamSystem.cs:57-58, 71-106`).
- **Per-part injection and draw.** Onyx's `InjectorSystem` uses `TryGetPartSolution` to inject into the limb's own stream (`ONYX Content.Shared/Chemistry/EntitySystems/InjectorSystem.cs:50, 210-222, 424-474`). WG has no consumer of that API at all (§2.2).
- **Live stream churn on limb attach/detach.** `SynchronizeStreams`/`RemoveStream` (`:120-199`, `:310-327`) add and drop solutions as prosthetics are swapped.

**None of this is reachable at the Onyx pin** — every path above is gated on a non-primary stream existing, and none does (§1.1). Porting it in phase 5 would buy zero observable behaviour at the cost of the entire solution/metabolism rewrite. Correct call: out of scope, and it should be described in the status doc as *not deferred work but unused upstream capability*.

---

## 9. Files, difficulty, tests

### 9.1 File list

| File | Kind | Change |
|---|---|---|
| `WG Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/ipc.yml` | upstream, marked | **(A)** add `- type: Bloodstream` block to `MobIPC`; **(B)** add `- type: Damageable / damageContainer: SiliconWolfmed / damageModifierSet: IPC` |
| `WG Resources/Prototypes/_WF/Wolfmed/Damage/containers.yml` | **new** `_WF` | **(B)** `SiliconWolfmed` damage container |
| `WG Content.Server/_Onyx/Chemistry/Circulation/CirculatoryStreamSystem.cs` | vendored `_Onyx`, marked | **(C)** sum all stream rates in `SetBleedRates` |
| `WG Content.IntegrationTests/Tests/_WF/Wolfmed/WolfmedCirculationTest.cs` | **new** test | §9.3 |
| `WG Docs/Wolfmed/WOLFMED_MANIFEST.md`, `WOLFMED_STATUS.md`, `WOLFMED_PLAN5.md` | docs | rows + the §5.3 deviation |

Upstream footprint change: **+1 tracked upstream file** (`_EinsteinEngines/.../ipc.yml`), taking the phase-4 count of 47 to 48. `_Mono/.../protogen.yml` is already tracked and would not need a new row unless P5-1 opts protogen in.

### 9.2 Difficulty

**Low.** No new C# types, no new components, no new subscriptions, no new prototypes except one damage container. The only non-mechanical judgement is §5.3. Implementation is well under an hour; the risk is entirely in P5-1's profile YAML obeying the §4.3 rule.

### 9.3 Tests (feeding P5-6)

New file `WolfmedCirculationTest.cs`, modelled on `WG Content.IntegrationTests/Tests/_Onyx/Wounds/WoundBleedingTest.cs` (see `ProjectsTreatsAndTracksAttachmentTest:53`, `TourniquetStopsOnlySelectedPartTest:155` for the spawn + wound + assert shape):

1. **T-CIRC-IPC-OIL** — spawn `MobIPC`, assert `BloodstreamComponent.BloodReagent == "Oil"` and that `bloodstream` solution holds Oil at `bloodMaxVolume`.
2. **T-CIRC-IPC-BLEED** — wound an IPC limb, run `RefreshBody`, assert `BloodstreamComponent.BleedAmount > 0` and that oil volume falls after a `BloodstreamSystem` tick. **This is the test that catches a stray `circulatoryStream:` in P5-1's YAML.**
3. **T-CIRC-STREAM-DEFAULT** — assert every `BodyPartProfilePrototype` in the prototype manager has `CirculatoryStream == CirculatoryStreamPrototype.PrimaryStream`. Three lines; enforces §4.3 permanently and fails loudly instead of silently.
4. **T-CIRC-SLIME/DIONA** — spawn each, wound a limb, assert the spilled reagent id is `Slime` / `Sap` and the rate ratio tracks `1.15` / `1.0`.
5. **T-CIRC-CYBER-HALF** — attach a cybernetic arm to a human wound host, equal wound severity on the cybernetic arm and a flesh arm, assert the cybernetic rate is half.
6. **T-CIRC-IPC-BLOODLOSS** *(only under §5.3 Option 2)* — drain an IPC below `bloodlossThreshold`, assert its `Damageable` gains `Bloodloss`. Under Option 1, assert the opposite and comment why.
7. **T-CIRC-TOURNIQUET-IPC** — tourniquet a bleeding IPC limb, assert the rate goes to 0 and that a non-bleeding limb is refused (mirrors `TourniquetStopsOnlySelectedPartTest`).
8. **D2 canary** — spawn a borg / any `damageContainer: Silicon` non-wound-host, assert its container is still `Silicon` and its damage behaviour is unchanged by edit (B).

Plus the standard gates: `DockTest` first (project memory), `EntityTest|PrototypeSaveTest|DockTest` smoke after the YAML edits, and a 120 s headless server run with zero `[ERRO]`/`[FATL]` (edits (A)/(B) touch prototypes).

---

## 10. Blockers

**None for P5-2.** The phase-1 "blocker" is retired: Onyx ships no non-organic stream, so there is nothing to port and nothing the stage metabolizer is needed for.

Two things that are *not* blockers but must be decided/recorded:

1. **§5.3 — user decision.** Does IPC oil loss deal damage? Recommend yes, via a `_WF` `SiliconWolfmed` container (Option 2). Recording it as a Wolfgate divergence from Onyx, since Onyx's own `SiliconIpc` container makes IPC bloodloss inert.
2. **§4.3 — hard rule for P5-1.** No phase-5 profile may set `circulatoryStream`. Recommend also taking the §4.4 one-line `SetBleedRates` sum so the rule is enforced by code rather than by discipline.

One item handed to P5-1 rather than solved here: **§6.1**, the `Cold`/`Caustic` mismatch between `IpcBodyPartProfile.acceptedDamageTypes` and WG's `Inorganic`/`Silicon` part containers.
