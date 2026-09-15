# Wolfmed handoff

Wolfmed is Wolfgate's medical overhaul: port Space Onyx's wound system, then layer Wolfgate additions on top. It should stay modular enough that later Onyx updates can be re-pulled quickly.

Status: research finished, no code written. Branch `clanker/ss14-medical-systems-research-127399`.

## Decision

- **Chosen: Space Onyx wounds.** It is the most modular option and is data-driven.
- **Rejected:**
  - Goob WoundMed: rewrites `DamageableSystem`, is still mid-rework, and is tied to Goob's own projects.
  - Backmen BRUTALITÄT: the original version of Goob's WoundMed, and bound to Backmen-only code.
  - Colonial Marines Universe: about 40k lines, built on RMC-14 and Colonial Marines.
  - Offbrand: a vitals simulation that would replace the crit thresholds, and it's abandoned.
- **Stay on the old body system.** Monolith (our upstream) has no medical plans on GitHub and hasn't adopted Nubody. Adopting Nubody would break Shitmed.

## Source

- **Repo:** https://github.com/Space-Onyx/space-onyx-14, branch `master`. Research was done against commit `2f5bab9`; pin the commit you port from.
- **License:** code is AGPL-3.0-or-later, which is compatible (Wolfgate is AGPLv3). Keep Onyx's attribution and file headers. Check each asset's `meta.json` and skip anything CC-BY-NC-SA.
- **Locale:** `en-US` exists (`Resources/Locale/en-US/_Onyx/...`, including `prototypes/wounds/wounds.ftl`, `medical/fractures.ftl`, `medical/surgery.ftl` and `medical/tourniquet.ftl`).
- **Engine:** Onyx uses RobustToolbox **289.0.0**; Wolfgate uses **277.0.0**. Expect some newer-API fixes.
- **Reference copy.** A blobless sparse clone keeps this cheap:
  ```bash
  git clone --depth 1 --filter=blob:none --no-checkout https://github.com/Space-Onyx/space-onyx-14.git onyx
  cd onyx && git sparse-checkout set --no-cone 'Content.Shared/_Onyx/' 'Content.Server/_Onyx/' 'Content.Client/_Onyx/' 'Content.IntegrationTests/Tests/_Onyx/' 'Resources/Prototypes/_Onyx/' 'Resources/Locale/en-US/_Onyx/' 'Content.Shared/Body/' 'Content.Shared/Medical/' 'Content.Server/Medical/' 'Content.Shared/Armor/' 'Content.Shared/Bed/' 'Content.Shared/Damage/' 'Content.Server/Body/' 'Content.Shared/MedicalScanner/' && git read-tree -mu HEAD
  ```
  In Git Bash, don't start sparse patterns with `/`: MSYS rewrites them to `C:/Program Files/Git/...`.

## Wolfgate baseline (what Wolfmed replaces or extends)

- **Shitmed (older EE/Monolith port):**
  - `Content.Shared/_Shitmed`: 106 files.
  - 97 upstream files carry "Shitmed Change" markers.
  - Limb-targeting doll, per-part health (disabled at 90, severed at 130), part regen, 32 surgeries, cybernetics, Autosurgeon.
- **Damage:** Shitmed spreads part damage *inside* `DamageableSystem.TryChangeDamage` (`targetPart`, `canSever`, `partMultiplier`).
- **Bleeding:** upstream single `BleedAmount`. `BloodstreamSystem`, `HealingSystem` and `BedSystem` are server-side here (Onyx's versions are shared/predicted).
- **Statuses:** old `StatusEffectsSystem` only. There is **no** `StatusEffectNew`.
- **Not present:** wounds, fractures, pain, internal bleeding, scars, IVs.
- **Extras:** CPR (`_EinsteinEngines`), medical bounties (`_NF`), 60-min rot.

## Onyx inventory (medical-relevant)

| Area | Path | Size |
|---|---|---|
| Wounds core | `Content.Shared/_Onyx/Wounds` | 22 files, 5,152 lines |
| Medical (surgery, tourniquet, healing, analyzer data, autosurgeon) | `Content.Shared/_Onyx/Medical` | 33 files, 2,971 lines |
| Medical server (surgery, medical patch, biomass) | `Content.Server/_Onyx/Medical` | 10 files, 967 lines |
| Body layer on top of Nubody (their own `BodyPartComponent`, organ damage, functional organs) | `Content.Shared/_Onyx/Body` | 24 files, 1,761 lines |
| Damage helpers (armor penetration, group heal) | `Content.Shared/_Onyx/Damage` | 7 files, 398 lines |
| Targeting (doll, snapshot, resolver) | `Content.*/_Onyx/Targeting` | about 10 shared files plus client UI |
| Circulation | `Content.Shared/_Onyx/Chemistry/Circulation` | 4 files |
| Examine part status and pain | `Content.*/_Onyx/HealthExaminable` | 4 files |
| Prototypes | `Resources/Prototypes/_Onyx/Wounds/wounds.yml` (1,064 lines), `_Onyx/Body` (38 files), `_Onyx/Entities/Surgery` | |
| Tests | `Content.IntegrationTests/Tests/_Onyx/{Wounds (8), Medical (2), Body (2)}` | |
| Textures | `Resources/Textures/_Onyx/Wounds` (156 files) | |
| CCVars | `Content.Shared/_Onyx/CCVar/CCVars.Wounds.cs`, `CCVars.Surgery.cs` | |

### Wound content (`wounds.yml`)

- **Body-part profiles:** Organic, Ipc, Slime, Cybernetic, Plant. Each sets the bleed multiplier, whether it scars, whether it feels pain, and passive and bed recovery.
- **Fracture profiles:** Organic and Cybernetic.
  - Grades: Hairline 20 / Simple 35 / Displaced 50 / Comminuted 60.
  - Creation chance: 5 / 25 / 65 / 100%.
  - Movement multiplier: 0.625 / 0.5 / 0 / 0. Hand-use multiplier: 0.92 down to 0.75.
  - Treatment goes None → Reduced (effects ×0.25) → Mended. New damage resets treatment.
- **Organic wounds:**
  - Blunt, Slash, Piercing, Burn (Heat/Cold/Caustic) and Electrical (Shock).
  - Stages: Minor 0 / Moderate 25 / Severe 50 (part Impaired) / Critical 80 (part Disabled). Bleeding and pain scale per stage.
  - Wounds reopen at 12–18 damage.
- **Special wounds:** BoneFracture, SystemicBleeding, SurgicalIncision, Dismemberment, AmputationConsequence, InternalBleeding (0.02 × severity drain, no puddles), MedicalScar (permanent).
- **Species sets:** Ipc and Cybernetic mechanical damage, CyberneticFrameFracture, and Slime and Plant variants.

### Key mechanics

- **Routing** (`WoundDamageRoutingSystem`): subscribes to `BeforeDamageChangedEvent` on `WoundHostComponent`, cancels the event, picks a part, then re-applies the damage.
  - Part choice, in order: requested part → targeting snapshot → active hand → weighted random.
  - Damage is split into systemic and localized; overflow feeds amputation.
- **Pain** (`PainSystem`): per-type conversion, with a floor set by open wounds.
  - Shock arms below 110 and triggers at 130: 2s stun, forced scream, jitter, then 30s of adrenaline (pain ×0.7).
  - Suppressions stack and decay.
- **Amputation:** once a part has overflowed, a finishing hit severs it (explosions also have a chance). The limb is thrown and the parent gets a consequence wound.
- **Organ damage:** a weighted chance per hit to damage organs in the part, capped per organ.
- **Scars:** chance-based when severe wounds close. Scars block treatment.

## Dependency map: Onyx → Wolfgate

| Onyx dependency | Used by | Wolfgate equivalent / action |
|---|---|---|
| `_body.GetBodyChildren`, `BodyHasChild`, `GetPartOrgans`, `GetBodyPartChildren`, `GetBodyChildrenOfType` | Wounds (about 22 calls) | **Already exist** with the same names in `Content.Shared/Body/Systems/SharedBodySystem.Parts.cs` / `.Body.cs`. Verify the signatures match. |
| `_body.TryDetachPart(part, reparent)` | `AmputationSystem` | Map to Wolfgate `DetachPart` / `CanDetachPart` (`SharedBodySystem.Parts.cs:702-751`). |
| Onyx `BodyPartComponent` fields: `FractureProfile`, `MaxDamage`, `AmputationThresholds`, `DismembermentFinishingDamage`, `AmputationConsequenceSeverity`, `DismembermentSeverity` | Wounds, fractures, amputation | Wolfgate's Shitmed `BodyPartComponent` lacks these. Put them in a separate Wolfmed part component instead of editing the upstream one. |
| `Content.Shared._Onyx.Targeting` | `WoundDamageComponents`, `WoundDamageRoutingSystem` | Map to `_Shitmed/Targeting` (`TargetBodyPart`, Shitmed `targetPart`). |
| Shitmed part-damage spreading inside `DamageableSystem` | — | **Main conflict.** For `WoundHost` entities, either bypass Shitmed's spread and let Onyx route, or feed Shitmed's resolved `targetPart` into Onyx. Don't let both apply damage. |
| `Content.Shared.StatusEffectNew` | `PainSystem`, `WoundBehaviors`, `WoundStatusEffectSystem` | Missing. Either port upstream's `StatusEffectNew` (large) or adapt to the old `StatusEffectsSystem`. **Ask the user.** |
| `_Onyx.Chemistry.Circulation` | Bleeding, damage projection, `WoundPrototype` | Port (4 files). |
| `_Onyx.Cybernetics` | `BodyPartFunctionalitySystem` | Map to `_Shitmed/Cybernetics`. |
| `_Onyx.Body` (`OrganDamageComponent` etc.) | `OrganDamageSystem` | Port only the organ-damage and functional-organ pieces; skip the Nubody glue. |
| Shared/predicted `BloodstreamSystem`, `HealingSystem`, `BedSystem` | Hooks | These are server-side in Wolfgate, so put the hooks server-side. |

### Upstream files Onyx hooks (lines mentioning Onyx)

- **Relevant, re-create as small `// WOLFGATE` hooks:**
  - `Content.Server/Medical/HealthAnalyzerSystem.cs` (37) and `Content.Shared/MedicalScanner/HealthAnalyzerScannedUserMessage.cs` (22).
  - `Content.Server/Body/Systems/RespiratorSystem.cs` (27).
  - `Content.Shared/Body/Systems/BloodstreamSystem.cs` (15).
  - `Content.Shared/Medical/Healing/HealingSystem.cs` (14) and `HealingDoAfterEvent.cs` (1).
  - `Content.Shared/Armor/SharedArmorSystem.cs` (13).
  - `Content.Server/Damage/Commands/HurtCommand.cs` (12).
  - `Content.Shared/Bed/BedSystem.cs` (9).
  - `Content.Server/Damage/Systems/DamageOtherOnHitSystem.cs` (7).
  - `SharedStaminaSystem.cs` (6), `ThermalRegulatorSystem.cs` (5), `DamageOnInteractSystem.cs` (4).
  - `CrewMonitoringConsoleSystem.cs` (3), `SharedDefibrillatorSystem.cs` (2), suit sensors, `SharedCryoPodSystem.cs` (1).
- **Skip:** the `Content.Shared/Body/*` hooks (`BodySystem`, `InitialBodySystem`, `VisualBody*`, `OrganComponent`, `HandOrgan*`). They are Nubody glue.

## Surgery

- Onyx has its own Shitmed-derived surgery (25 shared files, 70+ surgery prototypes).
- **Phase 1 keeps Wolfgate's Shitmed surgery.** Port only the wound surgeries and their steps onto Shitmed's step system:
  - `SurgeryStopBleeding`, `SurgeryTendWoundsBruteDeep` / `BurnDeep`, `SurgeryStopInternalBleeding`, `SurgeryMendFracture`, `SurgeryHealAmputationConsequence`, `SurgeryHeal<Organ>`.
- The surgery IDs overlap with ours (e.g. `SurgeryOpenIncision`). Reuse ours; don't duplicate them.
- Wolfgate's `BoneGel` / `BoneSetter` tools already exist with no logic behind them, so they can drive fracture reduction and mending.

## Modularity rules (for quick Onyx re-syncs)

1. **Vendor Onyx files into `_Onyx` folders.** Keep them as close to verbatim as possible, and mark every edit inside a vendored file with `// WOLFGATE`.
2. **Wolfgate glue and additions go in `_WF/Wolfmed`:** `Content.{Shared,Server,Client}/_WF/Wolfmed`, `Resources/Prototypes/_WF/Wolfmed`, `Resources/Locale/en-US/_WF/Wolfmed`. This covers adapters, Shitmed bridging and Wolfmed-only features.
3. **Upstream edits** are one- or two-line hooks marked `// WOLFGATE`.
4. **Keep a port manifest.** Record the Onyx commit and, for each file, its Onyx path, Wolfgate path and whether it was modified. That makes each re-sync a diff.
5. **Gate on a CCVar.** Reuse Onyx's `CCVars.Wounds` for enabling and tuning.

## Suggested phases

1. **Core:** Circulation, `WoundPrototype`/`WoundSystem`, routing, bleeding, internal bleeding and healing, plus the Shitmed damage bridge. Organic humans only. Port the Onyx wound tests.
2. **Fractures and pain:** make the `StatusEffectNew` decision first. Add the fracture alert and effects, the pain shock alert, and the high-pain-threshold trait.
3. **Amputation, organ damage, scars:** retire Shitmed's sever-at-130 and part regen for wound hosts.
4. **Treatment:**
   - Reagent treatment effects and `SuppressPainEntityEffect`.
   - Tourniquet (`_Onyx/Medical/Tourniquet`) and medical patch.
   - Wound surgeries.
   - Health analyzer wound diagnostics and examine part status.
5. **Other species profiles:** IPC, cybernetic, slime, plant. Map them to Wolfgate species; IPCs use Mono's pump organ.
6. **Wolfmed additions:** the user's own features, in `_WF/Wolfmed`.

## Known gotchas (from project memory)

- **Tests:** use headless integration tests, not client driving. The sqlite warnings from `db.ef` fail every pair test.
- **YAML:** lint in Release. `ErrorNode` crashes the linter.
- **Client sandbox:** client/shared types must be allowed in `Sandbox.yml`.
- **Duplicate subscriptions:** only one system may subscribe a given component+event pair. Check `BeforeDamageChangedEvent` / `DamageChangedEvent` subscribers on mob components before adding Onyx's.
- **Worktree builds:** junction RobustToolbox from the main checkout.
- **Comments:** `_WF` files have no license header, and comments are short `/// <summary>` one-liners (match `_WF/SafetyDepositBox`).
- **Balance:** Wolfgate is ship/gun PvP, so fractures and bleeding from gunfire need tuning. Rot is 60 min, and CPR revives from dead.

## Open questions for the user

- `StatusEffectNew`: port it from upstream, or adapt pain and wound statuses to the old system?
- Fully replace Shitmed limb health and severing for wound hosts, or run both?
- Which species ship in phase 1?
- Balance targets: how lethal should bleeding and fractures be in gunfights?

## Unverified

- Tuning numbers other than the ones quoted from `wounds.yml`.
- Whether Onyx's body-API signatures match Wolfgate's exactly.
- The full client-side footprint (targeting UI, surgery UI).
- Asset licenses.
- RobustToolbox 277 vs 289 API gaps.
