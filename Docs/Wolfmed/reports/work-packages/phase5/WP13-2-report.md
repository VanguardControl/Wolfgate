# WP13-2 report — IPC as a wound host, and circulation (P5-1c / P5-2)

Scope implemented: PLAN5 §4 WP13-2 — **PROTO Q** (`MobIPC`) and the **§2.4** `SetBleedRates` hardening.
PROTO S and PROTO T (welder / nanite-applicator `damageContainers`) were **already landed by WP13-1** (its
deviation D1) and were deliberately not re-applied. User decisions honoured: **U1** (IPC keeps pain +
`PainShockTarget`), **U2(a)** (`SiliconWolfmed` on the mob, welder/nanite companions intact), **U16**
(`chemicalMaxVolume: 0`, no `InjectableSolution`), **U18** (PROTO R stays dropped), **U11(a)**
(`BloodRefreshAmount` left at Onyx's default), **U3′(b)** (190/210 limb gib parity — landed WP13-1, verified
here on a live IPC).

---

## 1. Files created / modified

| File | Status |
|---|---|
| `Resources/Prototypes/_EinsteinEngines/Entities/Mobs/Player/ipc.yml` | modified — PROTO Q: one marked 4-component block on `MobIPC` + `Destructible` Blunt 400 → 1500 (+48 / −1) |
| `Content.Server/_Onyx/Chemistry/Circulation/CirculatoryStreamSystem.cs` | modified — in-vendored `_Onyx`, 2nd marked site: `SetBleedRates` sums `rates.Values` (+9 / −3) |
| `Docs/Wolfmed/WOLFMED_MANIFEST.md` | modified — `### WP13-2` section, `#### WP13-2-1` finding, measured-state table, "what an IPC experiences" block, user-decision ticks (+303) |

**Files created: none.** New prototype ids, new C# types, new components, new `!type:` classes, new
`SubscribeLocalEvent` pairs, new LocIds: **none** (PLAN5 §5.1/§5.3 — phase 5 registers zero subscriptions).
Nothing under `RobustToolbox` touched; no `git stash`/`clean`/`checkout --`/`reset`/`commit` run.

A throwaway probe fixture (`Content.IntegrationTests/Tests/_WF/Wolfmed/ZzWp132ProbeTest.cs`) was created to
measure a live `MobIPC`, run once, and **deleted before the final builds**. It is not in the tree.

---

## 2. Every WOLFGATE edit and reason

### PROTO Q — `MobIPC` (`_EinsteinEngines/Entities/Mobs/Player/ipc.yml`)

Placed immediately after `components:` so the Wolfmed block reads as one section, mirroring Onyx's
`# <Onyx-IPCWounds>` fence. Source: ONYX `Corvax/Body/Species/ipc.yml:92-113` + `Body/species_base.yml:54`.

1. **`- type: WoundHost`** — the whole point of P5-1c. Marked `# WOLFGATE (P5-1/P5-2, PROTO Q)`.
2. **`- type: PainShockTarget`** (P5-D10 / U1). Onyx puts this on `BaseSpeciesMob`, which its `MobIpc`
   parents, so Onyx IPCs *do* pain-shock; Wolfgate put it on `BaseMobSpeciesOrganic` (P2-D7), which `MobIPC`
   does not inherit. One line restores parity. `EmoteOnDamage` deliberately **not** added (Onyx's copy uses
   the broken `emotes:` key, and `MobIpc`'s `allowedEmotes` are `Boop`/`Whirr`).
3. **`- type: Bloodstream`** — `bloodReagent: Oil`, `bloodMaxVolume: 250`, `chemicalMaxVolume: 0`,
   `bloodlossDamage {Bloodloss: 0.5}`, `bloodlossHealDamage {Bloodloss: -1}` (P5-D4 / P5-D6 / U16).
   Onyx's `bloodReferenceSolution: Oil 250` has no Wolfgate equivalent; the classic `bloodReagent` +
   `bloodMaxVolume` pair is the mapping. Both `bloodloss*` fields are `[DataField(required: true)]`
   (`BloodstreamComponent.cs:69-77`), so both must be supplied. `Bloodloss`, never `Heat` (P5-D6): `Heat` is
   in `WoundHostComponent.LocalizedDamageTypes`, so a `Heat` bloodloss cost would route to a part, create
   chassis-wound severity and bleed at `chance: 1` — a self-reinforcing leak loop. `SolutionContainerManager`
   is not declared (PLAN5 §2.0 — `EnsureSolution` `EnsureComp`s it; see §5 for why that is safe here).
   `InjectableSolution` is not declared (U16 — no metabolizer, so an injectable solution is a trap).
4. **`- type: Damageable` / `damageContainer: SiliconWolfmed` + `damageModifierSet: IPC`** (P5-D5 / U2(a)).
   Stock `Silicon` has no `Bloodloss`, and `DamageableSystem` silently drops unsupported types
   (`DamageableSystem.cs:270-277`), so without this both the oil-loss damage and phase 3's P3-D1 vital-part
   `Bloodloss` charge (which `WolfmedBodyPartLifecycleSystem` already computes and applies to the body) are
   thrown away. `damageModifierSet: IPC` restated for readability only — component data merges per key.
5. **`Destructible` Blunt `400` → `1500`** (D22 / P5-D11), with the rationale comment placed above the block
   rather than inside the trigger mapping. Body damage on a wound host is the sum of every part's positive
   damage (`WoundDamageProjectionSystem.RefreshBodyDamage`), so a flat 400 is crossed by routine corpse
   damage — the same reason `BaseMobSpeciesOrganic` was raised to 1500. **`MobThresholds` untouched**: death
   stays at a projected total of 100.

### §2.4 — `CirculatoryStreamSystem.SetBleedRates` (in-vendored `_Onyx`)

`rates.GetValueOrDefault(PrimaryStream)` → a sum over `rates.Values`, marked
`// WOLFGATE: P5-2/P5-D3`. Bit-identical today (`Organic` is the only `circulatoryStream` prototype and none
of the five profiles selects another), but it turns the failure mode of a future `circulatoryStream:` from
**"that species silently stops bleeding, with no log"** into "bleeds normally". Complete rather than partial
(N4): `GetPartStream` → `SetBleedRates` is the only stream-aware path with callers; `TryGetPartSolution` and
`TryGetStreamSolution` have zero callers anywhere in WG. No new `using`.

### Two words in the manifest's phase-5 user-decision list

`Docs/Wolfmed/WOLFMED_MANIFEST.md` only. WP13-1's U13′(b) bullet read "…container first). **Not** applied to
`PartIPCBase` and `CyberneticPartBase` in WP13-1 … U13′(b) is COMPLETE", which contradicts itself and would
have misled WP13-7. "Not" → "and". The U2(a) bullet's "but it is still applied" → "; it is now applied".
Documentation clarity, no code impact.

---

## 3. Deviations from PLAN5

**D1 — PROTO S and PROTO T not executed here.** PLAN5 §4 assigns `Entities/Objects/Tools/welders.yml` and
`_Mono/Entities/Objects/Tools/nanite_applicator.yml` to WP13-2. WP13-1 landed both `- SiliconWolfmed`
entries early (its own deviation D1) and its report instructs WP13-2 not to re-add them. Verified present in
the working tree **before** PROTO Q's container change landed, so the R3 dead window — an IPC on
`SiliconWolfmed` with no repair path at all — never existed. `WeldingHealableComponent` measured present on a
spawned `MobIPC`, and both tools' `damageContainers` lists contain the new id. `_EinsteinEngines/Body/Parts/ipc.yml`
was likewise left untouched: WP13-1 fully consumed it.

**D2 — PROTO R remains dropped (U18), confirmed by measurement rather than by reasoning alone.** A spawned
`MobIPC` resolves **exactly one** `Destructible` threshold (a `DamageTypeTrigger`), confirming that
`MobIPC`'s own `thresholds` list replaces `PlayerSiliconHumanoidBase`'s `!type:DamageTrigger damage: 500`
wholesale. `silicon_base.yml` untouched; the phase-5 upstream file count stays at PLAN5's **55**, not 56.

**D3 — comment placement on the `Destructible` raise.** PLAN5's PROTO Q snippet puts the D22/P5-D11 comment
between `damageType: Blunt` and `damage: 1500`, i.e. at a shallower indent inside the trigger mapping. Legal
YAML but unreadable; the comment sits above `- type: Destructible` instead and names the 400 → 1500 change
explicitly. Same information, same file, no behaviour change.

**D4 — one finding PLAN5 does not have (WP13-2-1), reported, not fixed.** See §6.

Nothing else. No upstream file outside PLAN5 §3's WP13-2 list was touched.

---

## 4. Build / server / test output

```
Content.Server  (-c DebugOpt)                Build succeeded.  0 Error(s)
Content.Client  (-c DebugOpt)                Build succeeded.  0 Error(s)
Content.IntegrationTests (-c DebugOpt)       Build succeeded.  0 Error(s)
Content.YAMLLinter -c Release                No errors found in 75236 ms.
```
*(The first `Content.IntegrationTests` build failed once with MSB3027/MSB3021 — a `csc` process holding
`Robust.Shared.CompNetworkGenerator.dll` in the junctioned RobustToolbox. Transient; the immediate retry was
clean. Nothing in RobustToolbox was modified.)*

Headless server, 120 s, port 1299 (`WP13-2-report-server.log`) —
`grep -cE "\[ERRO\]|\[FATL\]|Exception"` = **0**:
```
[INFO] cvarcontrol: Registered 33 CVars.
[INFO] root: Server Version 277.0.0.0 -> Ready
[INFO] net: "::": "Socket bound to [::]:1299: True"
[INFO] net: "0.0.0.0": "Socket bound to 0.0.0.0:1299: True"
```
No bloodstream-solution warning, no unknown component/field, no missing prototype/parent, no duplicate id,
no Fluent duplicate.

Tests (`-c DebugOpt --no-build`):
```
DockTest                                       Total 3   Passed 3    (WP13-2-report-docktest.log)
EntityTest.SpawnAndDeleteAllEntitiesOnDifferentMaps
                                               Total 1   Passed 1    (WP13-2-report-entitytest.log)
_Onyx.Wounds|_Onyx.Body|_Onyx.Medical|Wolfmed  Total 98  Passed 98   (WP13-2-report-tests.log)
```
No environmental `db.ef` failures, so no re-run was needed.

`SpawnAndDeleteAllEntitiesOnDifferentMaps` is the requested proof that `MobIPC` spawns and deletes: the test
enumerates every non-abstract prototype that is not a test prototype and carries neither `MapGrid` nor
`RoomFill` nor the spawner category — `MobIPC` qualifies — spawns each on its own map, runs **450 ticks
(15 s, long enough for `BloodstreamSystem.Update` and `PainSystem.Update` to fire)**, then deletes everything
and asserts no errors.

### Measured live state of a spawned `MobIPC` (`WP13-2-probe.log`)

```
WoundHost=True   PainShockTarget=True   Pain=True
Bloodstream reagent=Oil maxVol=250 chemMax=0 lossDmg=[Bloodloss:0.5] healDmg=[Bloodloss:-1]
            refresh=1 threshold=0.9 bleedRed=0.33 maxBleed=10
Damageable  container=SiliconWolfmed modifier=IPC
            types=[Bloodloss,Blunt,Heat,Piercing,Radiation,Shock,Slash]
Destructible  1 threshold (DamageTypeTrigger)
InjectableSolution=False   WeldingHealable=True
PART TorsoIPC  type=Torso profile=IpcBodyPartProfile frac=null amp=0 maxDmg=0 container=InorganicWolfmed
PART HeadIPC / 2x Arm / 2x Hand / 2x Leg / 2x Foot
               profile=IpcBodyPartProfile frac=null amp=4 maxDmg=0 container=InorganicWolfmed
IpcBodyPartProfile canFeelPain=True bleedMult=1 stream=Organic caps=[Mechanical,Electrical]
   accepted=[Blunt,Slash,Piercing,Heat,Cold,Shock,Caustic] passive=0 bed=0 scar=False
```
R8 confirmed: `TorsoIPC` carries `WolfmedBodyPart` with `maxDamage: 0` and **0** amputation thresholds —
overflow disabled, correct for a torso (`AmputationSystem` skips torsos). Do not "fix" it.
R9 satisfied for IPC trivially (`fractureProfile: null` everywhere — IPCs never fracture).
P5-D3 confirmed: `circulatoryStream` resolves to `Organic`; no profile sets it.

---

## 5. Exactly what an IPC experiences now

- **Bleeds oil.** `IpcMechanicalDamageWound` carries a wound-level `WoundBleedingBehavior rate: 0.08
  chance: 1` with **no `minimumSeverity`**, so an IPC leaks from the first point of chassis damage;
  `bleedingMultiplier: 1`. The spilled reagent is `Oil` (`flammability: 2` + `FlammableTileReaction`) —
  **the trail can be set on fire.** Bleeding surfaces on the analyzer and in examine text exactly as an
  organic's does (the "fluid leak" wording is WP13-5's job).
- **Oil loss actually damages.** `Bloodloss: 0.5` per bloodstream update while below 90 % fluid, healing back
  at `Bloodloss: -1` once topped up; `BloodRefreshAmount` stays at Onyx's default 1.0 (U11(a)), so an IPC can
  recover from a leak unaided. `SiliconWolfmed` is what lets any of it land, and it also restores phase 3's
  P3-D1 vital-part `Bloodloss` charge (decapitation etc.), which the body container previously discarded.
- **Feels pain, and can go into pain shock.** `canFeelPain` measured `True`; `IpcMechanicalDamageWound`
  carries `painPerSeverity: 0.87 minSeverity: 10`. Pain shock at 130 is a 2 s paralyze + jitter + a 30 s
  adrenaline window, and `PainSystem.UpdatePainShock` calls `TryEmoteWithChat(…, "Scream", forceEmote: true)`,
  **bypassing `allowedEmotes: [Boop, Whirr]` — an IPC screams on shock.**
  **There is no chemical relief of any kind:** no metabolizer (`OrganIPCPump`'s `Metabolizer` block is
  commented out), `chemicalMaxVolume: 0`, no `InjectableSolution`. No `SuppressPain`, no painkiller, no
  adrenaline chem. **Pain falls only as the chassis is repaired.** (R4 — flag for playtest; U1(b),
  `canFeelPain: false`, is a one-line reversal if it reads as punishing.)
- **Drunk and stuttering below 90 % fluid.** `BloodstreamSystem.Update`'s bloodloss branch (`:141-162`) has
  no wound-host or species guard, unlike `OnDamageChanged` (GUARD E). This is what every bleeding organic
  already gets — Wolfgate-consistent, but a drunk robot is a flavour oddity.
- **Repaired only by welder, nanite applicator and cable coil.** `treatmentCapabilities:
  [Mechanical, Electrical]` overlaps no medicine, brute pack, ointment or gauze. The welder and the Mono
  nanite applicator work through `WeldingHealable` (whose `TryChangeDamage` on the mob routes to parts with
  no capability scope open, so it heals chassis wounds for free) — both tools now list `SiliconWolfmed`.
  The cable coil becomes `[Electrical]` in WP13-4 and overlaps. The **tourniquet** and **every wound
  surgery** also work (`SurgeryTarget` is on `PlayerSiliconHumanoidBase:319`).
- **Never passively or bed-heals** (`passiveRecoveryMultiplier: 0`, `bedRecoveryMultiplier: 0`), **never
  scars** (`scarrable: false`), **never fractures** (`fractureProfile: null` on all ten parts, so no
  `BrokenBones` alert and no `SurgeryMendFracture` on an IPC).
- **Limb loss** (from WP13-1, verified here): gib ceiling **Blunt 190 / Slash 210, no Heat rung** on every
  IPC slot, against the inherited organic amputation set per slot (arm Slash 130 / Piercing 250 / Blunt 250 /
  Heat 250). So Slash and Piercing sever, pure Blunt destroys first, and **an IPC limb never burns to `Ash`**.
  `TorsoIPC` keeps its own 400/400 and is never severable.
- **Dies at the same point as before** — `MobThresholds` untouched, death at a projected total of 100. But
  body damage is now the **sum of every part's positive damage** (D22) and decays far more slowly (passive
  regen is neutralised on wound hosts, and the Ipc profile's own recovery is 0), so IPCs will sit in
  `SlowOnDamage`'s 60/90/120 bands much longer than today (R7). The gib threshold was raised 400 → 1500 for
  exactly that reason.
- **Cold and Caustic hurt the limb but not the chassis total** — see §6.

---

## 6. WP13-2-1 — finding not in PLAN5: `Cold`/`Caustic` reach IPC parts but are dropped from the body total

U13′(b) restored `Cold` and `Caustic` on IPC and cybernetic **parts** (`InorganicWolfmed`), and both are in
`WoundHostComponent.LocalizedDamageTypes` (`WoundDamageComponents.cs:36-43`), so a Cold or Caustic hit really
does route to a limb and create `IpcMechanicalDamageWound` severity, pain, functionality loss and a leak.

But `SiliconWolfmed` — the **mob** container, PLAN5 §2.3, owned by WP13-0 — is stock `Silicon` + `Bloodloss`:
Brute + Heat/Shock/Radiation/Bloodloss, **no `Cold`, no `Caustic`** (measured `DamageDict` keys on a live
`MobIPC`: `Bloodloss, Blunt, Heat, Piercing, Radiation, Shock, Slash`).
`WoundDamageProjectionSystem.RefreshBodyDamage` projects part damage back with `_damage.SetDamage(body,
total)`, and `DamageableSystem` silently drops unsupported types, so the Cold/Caustic component never reaches
the body total that `MobThresholds` (death at 100) and `SlowOnDamage` read.

**Onyx does not have this asymmetry** — its `SiliconIpc` takes the whole `Burn` group on the mob as well as
on the part. Consequence: acid and cryogenics on an IPC produce wounds, pain and oil leakage, and kill only
*indirectly* via the `Bloodloss` the leak generates; they cannot themselves push an IPC to 100.

**Deliberately not fixed in this package.** `_WF/Wolfmed/Damage/containers.yml` is WP13-0's file; PLAN5 §2.3
specifies `SiliconWolfmed`'s shape exactly; and widening it would also change how the mob's
`damageModifierSet: IPC` (`Cold 0.2`) applies at the body level. The one-line fix, if the balance pass wants
Onyx parity, is `supportedGroups: [Brute, Burn]` on `SiliconWolfmed`.

---

## 7. What later packages must know

- **WP13-6 (tests).** Every number in §4's measured table is test-ready and was read off a live `MobIPC`, not
  derived. T-P5-12 (welder/nanite still repair an IPC) can assert `WeldingHealableComponent` + both tools'
  `damageContainers`. **Do not write a test asserting "Caustic/Cold raises an IPC's *body* damage"** — assert
  it on the **part** (a wound is created) and pin the body-level drop as a canary, per §6. An IPC test needs
  no fracture assertions (`fractureProfile: null` on every part) and no scar assertions
  (`scarrable: false`). For a pain-shock test remember `PainShockTarget` is now on the mob.
- **WP13-5 (analyzer/examine wording).** The `Mechanical` flag's producer will see
  `IpcBodyPartProfile.TreatmentCapabilities = [Mechanical, Electrical]` — no `Biological` — so the
  `!profile.TreatmentCapabilities.Contains(Biological)` test fires correctly for every IPC part. `MobIPC`
  inherits `HealthExaminable` and `DamageVisuals` from `PlayerSiliconHumanoidBase`, so the examine path and
  the per-limb damage sprites are already live for IPCs.
- **WP13-7 (docs/status/changelog).** Upstream file count is **55**, not 56 (PROTO R stayed dropped, U18 —
  measured, not assumed). The changelog needs: IPCs bleed oil and can burn their own trail; IPCs feel pain,
  can pain-shock and **scream**, with no chemical relief; IPCs go drunk/stuttering below 90 % fluid; IPCs are
  repaired only by welder/nanite/cable coil; IPCs never passively heal. Also carry WP13-1's two corrections
  to PLAN5 §8.1/§8.1a (organic heads *do* have a gib trigger at 500/600/700; slime limbs *are* severable) and
  add §6's Cold/Caustic asymmetry to the deviations block.
- **General rule from §5's near-miss.** `MobIPC` escapes `PrototypeSaveTest.UninitializedSaveTest` only
  because it inherits `save: false` from `PlayerSiliconHumanoidBase:2`. `BloodstreamSystem.OnComponentInit`
  `EnsureComp`s `SolutionContainerManagerComponent`, so **any future package that declares `- type:
  Bloodstream` on a map-savable prototype must also declare `- type: SolutionContainerManager`**, or the test
  fails with "gains a component on spawn" — the same class as WP13-1-1.
- **Balance pass inbox from this package:** the Cold/Caustic body-total asymmetry (§6); R7's slow-band
  stickiness on IPCs; R4's no-pain-relief-at-all; and U1(b) as the one-line reversal if IPC pain reads badly.

---

## 8. Artefacts

| Path | Contents |
|---|---|
| `C:/tmp/wolfmed-plan/p5/wp/WP13-2-report-server.log` | 120 s headless run, port 1299, 0 errors |
| `C:/tmp/wolfmed-plan/p5/wp/WP13-2-report-docktest.log` | `DockTest` 3/3 |
| `C:/tmp/wolfmed-plan/p5/wp/WP13-2-report-entitytest.log` | `SpawnAndDeleteAllEntitiesOnDifferentMaps` 1/1 |
| `C:/tmp/wolfmed-plan/p5/wp/WP13-2-report-tests.log` | Wolfmed suite 98/98 |
| `C:/tmp/wolfmed-plan/p5/wp/WP13-2-probe.log` | measured live `MobIPC` dump |
| `C:/tmp/wolfmed-plan/p5/wp/WP13-2-probe-run.log` | probe harness run log |
