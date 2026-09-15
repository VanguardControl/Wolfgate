# WP13-1 report — species part wiring and gib blocks (P5-1b)

Scope implemented: PLAN5 §4 WP13-1 — PROTO M/N/O/P, U3′(b), U15(a), U13′(b), plus the PROTO S/T companion
lines moved forward from WP13-2 by the task brief. WP13-0 had already created
`_WF/Wolfmed/Body/species_parts.yml` and `_WF/Wolfmed/Damage/containers.yml`, so this package is wiring only.

## 1. Files created / modified

| File | Status |
|---|---|
| `Resources/Prototypes/Body/Parts/slime.yml` | modified — PROTO M, 1 marked line (`:4`) |
| `Resources/Prototypes/Body/Parts/diona.yml` | modified — PROTO N, 1 marked line (`:3`) |
| `Resources/Prototypes/_EinsteinEngines/Body/Parts/ipc.yml` | modified — PROTO O(a)(b)(c) |
| `Resources/Prototypes/_Shitmed/Body/Parts/cybernetic.yml` | modified — PROTO P(a)(b)(c) |
| `Resources/Prototypes/Entities/Objects/Tools/welders.yml` | modified — PROTO S, 1 entry |
| `Resources/Prototypes/_Mono/Entities/Objects/Tools/nanite_applicator.yml` | modified — PROTO T, 1 entry |
| `Resources/Prototypes/_WF/Wolfmed/Body/species_parts.yml` | modified — `wounds`/`bodypart` `ContainerContainer` on all four abstracts + stale-note fix |
| `Docs/Wolfmed/WOLFMED_MANIFEST.md` | modified — `### WP13-1` section, `#### WP13-1-1` deviation, user-decision ticks |

New prototype ids: **none** (all 22 landed in WP13-0). New C# types, components or
`SubscribeLocalEvent` pairs: **none**. Locale changes: **none**. No file under `RobustToolbox` touched; no
git operation run.

## 2. Every WOLFGATE edit and why

1. **PROTO M** `Body/Parts/slime.yml:4` — `parent: [BaseItem, BasePart]` →
   `parent: [WolfmedPartSlime, BaseItem, BasePart]`. Attaches `SlimeBodyPartProfile` and
   `fractureProfile: null`. `WolfmedPartSlime` is **first** because every `[DataField]` inside a component is
   non-`Always`, so the first parent in the list wins (`SerializationManager.Composition.cs:40-58, 178-206`,
   re-read this pass).
2. **PROTO N** `Body/Parts/diona.yml:3` — same shape with `WolfmedPartDiona`
   (`PlantBodyPartProfile`, `fractureProfile: null`, `amputationThresholds: {}`).
3. **PROTO O(a)** `_EinsteinEngines/Body/Parts/ipc.yml:3` — `parent: BasePartInorganic` →
   `parent: [ WolfmedPartIpc, BasePartInorganic ]`. Attaches `IpcBodyPartProfile`.
4. **PROTO O(b)** same file — a **new** `- type: Damageable / damageContainer: InorganicWolfmed` block
   (`PartIPCBase` previously inherited stock `Inorganic`). U13′(b)/P5-D20: restores `Cold` and `Caustic`,
   which Onyx's `SiliconIpc` supports and Wolfgate's `Inorganic` does not — 2 of the 7
   `acceptedDamageTypes` on `IpcBodyPartProfile` and 2 of the 6 `damageTypes` on
   `IpcMechanicalDamageWound` were dead.
5. **PROTO O(c)** same file — `Destructible` `Blunt 110 → 190`, `Slash 150 → 210`, **no Heat rung added**.
   U3′(b)/P5-D8: parity with Mono's `MajorLimb`, the trigger every organic arm and leg carries. EE's own
   `# no ashing trigger` comment is preserved, so an IPC limb never burns to `Ash`. `TorsoIPC`'s own
   400/400 block untouched.
6. **PROTO P(a)** `_Shitmed/Body/Parts/cybernetic.yml:3` — `parent: BasePartInorganic` →
   `parent: [ WolfmedPartCybernetic, BasePartInorganic ]`. Attaches `CyberneticBodyPartProfile` +
   `CyberneticFractureProfile` (R9: the two agree).
7. **PROTO P(b)** same file `:10-11` — `damageContainer: Silicon` → `InorganicWolfmed` (superset of
   `Silicon`; nothing lost, `Cold`/`Caustic` gained).
8. **PROTO P(c)** same file — a **new** marked `- type: Destructible # no ashing trigger`,
   `Blunt 190` / `Slash 210` → `GibPartBehavior`, **no Heat rung** (U15(a)/P5-D8b). Removes the pre-existing
   absurdity where a steel prosthetic spawned `Ash`, ran `BurnBodyBehavior` and played `MeatLaserImpact` at
   Heat 250 via the inherited `MajorLimb`/`MinorLimb` block (R12).
9. **PROTO S** `Entities/Objects/Tools/welders.yml` — `- SiliconWolfmed` appended to
   `WeldingHealing.damageContainers`, marked. P5-D5/R3: `WeldingHealableSystem.OnRepairFinished` gates on
   this list, so once WP13-2 moves `MobIPC` onto `SiliconWolfmed` the only way to repair an IPC disappears
   without it.
10. **PROTO T** `_Mono/Entities/Objects/Tools/nanite_applicator.yml` — identical entry, same reason.
11. **`_WF/Wolfmed/Body/species_parts.yml`** — a marked `- type: ContainerContainer` declaring `bodypart`
    and `wounds` on all four abstracts (see deviation 2), plus replacing WP13-0's now-stale
    "wiring is a later package's job" header note.

## 3. Deviations from PLAN5

**D1 — PROTO S/T executed in WP13-1, not WP13-2.** PLAN5 §4 gives `welders.yml` and
`nanite_applicator.yml` to WP13-2; the task brief for this package explicitly required the companion lines
here. Landing them early is inert — no entity carries `SiliconWolfmed` yet — and it removes the R3 dead
window entirely. **WP13-2 must not re-add these two entries**; it still owns PROTO Q (`MobIPC`'s
`WoundHost`/`PainShockTarget`/`Bloodstream`/`Damageable`/`Destructible 1500`) and the §2.4
`CirculatoryStreamSystem` sum. Recorded in the manifest.

**D2 — `wounds` container declared statically (addition to PLAN5 §2.2, new work item WP13-1-1).**
Not anticipated by PLAN5. The moment PROTO M/N/O/P landed,
`PrototypeSaveTest.UninitializedSaveTest` failed on **40** prototypes (every `*IPC`, `*Slime`, `*Diona`,
`*Cybernetic` part) with `modifies component on spawn: ContainerContainer`. Cause:
`WoundSystem.OnWoundableInit` `EnsureContainer`s `"wounds"` on **`ComponentInit`**
(`Content.Shared/_Onyx/Wounds/WoundSystem.cs:60-63`) — i.e. at spawn. Phases 1-4 never hit this because
organic parts get `Woundable` from `WoundDamageProjectionSystem.SetupPart`'s runtime `EnsureComp`, while
P5-D1's whole design is a *static* `Woundable` declaration. Fixed inside the `_WF` file (not upstream) by
declaring both containers on the four abstracts:

```yaml
  - type: ContainerContainer
    containers:
      bodypart: !type:Container
        ents: []
      wounds: !type:Container
        ents: []
```

`ContainerManagerComponent.Containers` is a plain `[DataField]` dictionary
(`RobustToolbox/.../ContainerManagerComponent.cs:23-24`), so this **replaces** `BasePartInorganic`'s
`{bodypart}` rather than merging — hence `bodypart` is restated. `BaseTorsoInorganic`'s `torso_slot` was
already suppressed by the same first-parent rule *before* this package and is still created at runtime by
`SharedBodySystem`; nothing changes there. `EnsureContainer` now finds the declared container, whose
`!type:Container` defaults (`showEnts: False`, `occludes: True`) match what it would have created.

**D3 — comment formatting on PROTO O(c).** PLAN5 asks for the full `# WOLFGATE (P5-D8)` rationale on *each*
of the two changed numbers. The full rationale sits once above the `Destructible` block and each number
carries a one-line marker; duplicating six comment lines twice inside one block reads worse for no gain.

**Two factual corrections to PLAN5 found while measuring (no code impact, but later packages and the
guidebook copy depend on them):**

- **Organic heads DO have a gib trigger.** PLAN5 N5 / P5-D8 / §8.1a assert `BaseHead` is
  `parent: WolfmedBaseHead` only and therefore carries no rung. It declares its own
  `- type: Destructible # Mono` at `Resources/Prototypes/Body/Parts/base.yml:105-133` with
  **Blunt 500 / Slash 600 / Heat 700 → Ash + BurnBody + MeatLaserImpact** (measured on a spawned
  `HeadHuman`). So the accepted asymmetry is not "IPC head destructible, human head not" but "IPC head
  gibs at 190 where an organic head needs 500". It is still **not a regression** — `PartIPCBase` was
  110/150, so U3′(b) makes the IPC head 1.7× tougher than today. `HeadIPC` amputation thresholds are
  Slash 200 / Piercing 200 / Blunt 350 / Heat 200, so Slash decapitation still works (200 < 210) and pure
  Blunt decapitation does not (350 > 190). Fix, if wanted, is U3′(d); handed to the balance pass.
- **Slime limbs ARE severable.** PLAN5 §8.1/§8.1a say slime limbs are "unseverable by threshold, same as
  diona". Onyx gives `amputationThresholds: {}` to `OrganDionaExternal` **only**, not to
  `OrganSlimePersonExternal` (`ONYX Body/Species/slime.yml:235-239`); `species_parts.yml` reproduces Onyx
  correctly. Measured: `LeftArmSlime` → `Slash 130 / Piercing 250 / Blunt 250 / Heat 250`, the ordinary
  organic set. Only diona are unseverable.

## 4. Effective `Destructible` thresholds per affected prototype, after inheritance

Measured, not derived: a throwaway `GameTest` harness spawned each prototype and read
`DestructibleComponent.Thresholds`, `DamageableComponent.DamageContainerID`, `WoundableComponent.Profile`
and `WolfmedBodyPartComponent` (raw dump at `C:/tmp/wolfmed-plan/p5/wp/WP13-1-dump.log`; the harness file was
deleted before the final builds). Reasoning over every parent was necessary — a child's `thresholds` list
replaces the parent's and the first parent in the list wins.

| prototype(s) | before WP13-1 | after WP13-1 | winning block |
|---|---|---|---|
| `LeftArmIPC`, `RightArmIPC`, `LeftLegIPC`, `RightLegIPC` | Blunt 110 / Slash 150 | **Blunt 190 / Slash 210, no Heat** | `PartIPCBase` beats `MajorLimb` (190/210/250+Ash) |
| `LeftHandIPC`, `RightHandIPC`, `LeftFootIPC`, `RightFootIPC` | Blunt 110 / Slash 150 | **Blunt 190 / Slash 210, no Heat** | `PartIPCBase` beats `MinorLimb` (150/180/230+Ash) — IPC extremities tougher than flesh (accepted, P5-D8) |
| `HeadIPC` | Blunt 110 / Slash 150 | **Blunt 190 / Slash 210, no Heat** | `PartIPCBase` beats `BaseHead`'s 500/600/700 (see correction above) |
| `TorsoIPC` | Blunt 400 / Slash 400 | **unchanged** | `TorsoIPC`'s own block |
| `LeftArmCyberneticBase`, `RightArmCyberneticBase`, `LeftLegCyberneticBase`, `RightLegCyberneticBase`, `JawsOfLife{Left,Right}Arm`, `Speed{Left,Right}Leg` | Blunt 190 / Slash 210 / **Heat 250 → Ash + BurnBody + MeatLaserImpact** | **Blunt 190 / Slash 210, no Heat** | new `CyberneticPartBase` block beats `MajorLimb` |
| `Left/RightHandCybernetic`, `Left/RightFootCybernetic`, `Dex{Left,Right}Hand` | Blunt 150 / Slash 180 / **Heat 230 → Ash** | **Blunt 190 / Slash 210, no Heat** | new `CyberneticPartBase` block beats `MinorLimb` |
| all slime/diona **arms and legs** | Blunt 190 / Slash 210 / Heat 250 + Ash | **unchanged** | `MajorLimb` (the `_WF` abstracts declare no `Destructible`) |
| all slime/diona **hands and feet** | Blunt 150 / Slash 180 / Heat 230 + Ash | **unchanged** | `MinorLimb` |
| `TorsoSlime`, `TorsoDiona` | 400 / 400 / 400 + Ash | **unchanged** | `BaseTorso` |
| `HeadSlime`, `HeadDiona` | Blunt 500 / Slash 600 / Heat 700 + Ash | **unchanged** | `BaseHead` |
| every human-lineage part | — | **unchanged** (D2) | `MajorLimb` / `MinorLimb` / `BaseHead` / `BaseTorso` |

Profile wiring, measured on the same run:

- `*IPC` → `IpcBodyPartProfile`, `fractureProfile: null`, container `InorganicWolfmed`
  (`DamageDict` keys `Blunt, Caustic, Cold, Heat, Piercing, Radiation, Shock, Slash`).
- `*Cybernetic` → `CyberneticBodyPartProfile` + `CyberneticFractureProfile` (R9 satisfied), container
  `InorganicWolfmed`.
- `*Slime` → `SlimeBodyPartProfile`, `fractureProfile: null`, organic amputation thresholds, container
  `OrganicPart` (unchanged).
- `*Diona` → `PlantBodyPartProfile`, `fractureProfile: null`, **`amputationThresholds.Count == 0`** (R2
  satisfied), container `OrganicPart` (unchanged).
- Human-lineage parts resolve **no** static `Woundable` at all — D2 intact.
- **R8 confirmed:** `TorsoIPC` now carries `WolfmedBodyPart` with `maxDamage: 0` and empty
  `amputationThresholds` — overflow disabled, correct for a torso. Do not "fix" it.

D2 deviations recorded (all anticipated by PLAN5): detached IPC limbs 110/150 → 190/210 (R5); detached
cybernetic limbs lose the Heat/Ash rung and hands/feet go 150/180 → 190/210 (R12); detached IPC and
cybernetic limbs can now take `Cold`/`Caustic` on their own `Damageable` (cosmetic — no host, no wound).

## 5. Build / server / test output

```
Content.Server  (-c DebugOpt)   Build succeeded.  0 Error(s)
Content.Client  (-c DebugOpt)   Build succeeded.  0 Error(s)
Content.YAMLLinter -c Release   No errors found in 78202 ms.
```

Headless server, 120 s, port 1299 (`WP13-1-report-server.log`) — `grep -cE "\[ERRO\]|\[FATL\]|Exception"` = **0**:
```
[INFO] ticker: Restarting round!
[INFO] system.mind: Wiping all minds
[INFO] entity: Flushing entities. Entity count: 0
[INFO] company.manager: All company members data loaded in 2s
[INFO] cvarcontrol: Registered 31 CVars.
[INFO] root: Server Version 277.0.0.0 -> Ready
```

Tests (all `-c DebugOpt --no-build`):
```
DockTest                                   Total 3   Passed 3   (WP13-1-report-docktest.log)
EntityTest|PrototypeSaveTest               Total 8   Passed 6   Skipped 2   Failed 0   (WP13-1-report-entitytest.log)
_Onyx.Wounds|_Onyx.Body|_Onyx.Medical|Wolfmed
                                           Total 98  Passed 98  Failed 0   (WP13-1-report-tests.log)
```
`EntityTest|PrototypeSaveTest` failed 40 assertions on the first run (all `ContainerContainer`); green after
the WP13-1-1 fix. No environmental `db.ef` failures occurred, so no re-run was needed.

## 6. What later packages must know

- **WP13-2 must NOT re-add PROTO S/T.** Both `- SiliconWolfmed` entries are already in `welders.yml` and
  `nanite_applicator.yml`. WP13-2 still owns PROTO Q (`MobIPC`) and §2.4's `SetBleedRates` sum, and
  `_EinsteinEngines/Body/Parts/ipc.yml` is now fully consumed by WP13-1 — WP13-2 must not touch it.
- **U3′(b), U15(a) and U13′(b) are COMPLETE.** U2(a) is half done: the container exists and the two tool
  lines are in, but `MobIPC`'s `Damageable` still points at stock `Silicon` — WP13-2's job.
- **Static `Woundable` forces a static `wounds` container.** Any future package that puts
  `- type: Woundable` on a prototype must also declare the `wounds` container (and restate whatever
  containers the first parent would otherwise have supplied), or `PrototypeSaveTest.UninitializedSaveTest`
  fails. This is the general rule behind WP13-1-1.
- **For WP13-6 (tests):** T-P5-5/6 (parent order), T-P5-11 (IPC gib numbers), T-P5-19 (cybernetic gib
  numbers) and T-P5-20 (diona destroyed not severed) can all be written against the measured table in §4.
  Use `JawsOfLifeLeftArm` for a concrete cybernetic arm (the `*CyberneticBase` ids are abstract). Assert
  `Deleted(part)` for gibbing (N10), not a component state.
- **For WP13-7 (docs/guidebook/changelog):** PLAN5's "organic heads have no gib trigger" and "slime limbs are
  unseverable" are both wrong (§3 above). §8.1/§8.1a of the shipped `WOLFMED_PLAN5.md` must be corrected, and
  the changelog should say plainly that cybernetic limbs no longer burn to ash and that cybernetic
  hands/feet got tougher (150/180 → 190/210).
- **Balance pass:** the IPC head at 190 versus an organic head at 500 is the largest remaining asymmetry from
  this package; U3′(d) (five per-slot blocks) is the fix if it is unwanted.
