# WP13-0 report — Profile, wound and container prototypes (P5-1a, P5-2a)

## 1. Files created/modified

| File | Status |
|---|---|
| `Resources/Prototypes/_Onyx/Wounds/wounds.yml` | modified — 14 → 30 prototypes |
| `Resources/Prototypes/_WF/Wolfmed/Damage/containers.yml` | new |
| `Resources/Prototypes/_WF/Wolfmed/Body/species_parts.yml` | new |
| `Docs/Wolfmed/WOLFMED_MANIFEST.md` | modified — appended Phase 5 / WP13-0 section |

`Docs/Wolfmed/DECISIONS.md` shows as modified in `git status` but that change predates this package (an
earlier session's informational copy of the Phase-5 DECISIONS answers); not touched here.

## 2. WOLFGATE edits and reasons

- **`wounds.yml:1`** — trim comment rewritten from "WP7" (14/30) to "WP7 → P5-1" (30/30), stating the two
  fold classes (D9, §8.2-1).
- **4 `bodyPartProfile`s** (`IpcBodyPartProfile`, `SlimeBodyPartProfile`, `CyberneticBodyPartProfile`,
  `PlantBodyPartProfile`) copied verbatim from ONYX `wounds.yml:37-151`, inserted after
  `OrganicBodyPartProfile`. D9 fold applied to the three that carry `organDamage`: `Chest: 0.04` +
  `Groin: 0.04` → `Torso: 0.04` (Wolfgate's `BodyPartType` has neither Chest nor Groin — an unfolded copy
  fails deserialization). No `maxAffected` added (Onyx sets it only on `OrganicBodyPartProfile`); no extra
  limb rows added to Slime/Plant. `acceptedDamageTypes` kept verbatim with `# WOLFGATE (P5-1/P5-D20)`
  inert-marker comments on `Cold`/`Shock`/`Caustic` — dead until a later package's `InorganicWolfmed`
  wiring.
- **`CyberneticFractureProfile`** copied from ONYX `:193-231`, inserted after `OrganicFractureProfile`.
  §8.2-1 fold applied: `manipulationModifier` 0.92/0.84/0.75/0.75 → C# defaults 1.1/1.25/1.5/2.0 (Onyx's
  values are all below 1, which the formula reads as *faster*, so an unfolded copy would make a shattered
  cybernetic arm's do-afters 25% quicker — the same bug DECISIONS already fixed once on
  `OrganicFractureProfile`).
- **11 `wound` prototypes** copied byte-for-byte, no folds: `CyberneticFrameFractureWound` (after
  `BoneFractureWound`); `IpcMechanicalDamageWound`, `CyberneticMechanicalDamageWound`, and the 8
  Slime/Plant wounds (all after `DismembermentWound`, before `AmputationConsequenceWound`).
- **`SiliconWolfmed` damage container** (new) — `Silicon` (Brute + Heat/Shock/Radiation) + `Bloodloss` as a
  **type**, not the `Airloss` group (P5-D5b: the tourniquet applies its `Asphyxiation` cost to the part,
  whose container this doesn't touch, so the group buys nothing and opens an unaudited surface for free).
- **`InorganicWolfmed` damage container** (new, U13′(b)) — `supportedGroups: [Brute, Burn]` +
  `supportedTypes: [Radiation]`, a superset of both `Inorganic` (IPC parts) and `Silicon` (cybernetic
  parts), restoring `Cold`/`Caustic` that Onyx's own `SiliconIpc` has and Wolfgate's stock containers lack.
- **`species_parts.yml`** (new) — 4 abstract entities re-expressing Onyx's IPC/Slime/Diona/Cybernetic part
  profiles: `WolfmedPartSlime`, `WolfmedPartDiona`, `WolfmedPartIpc`, `WolfmedPartCybernetic`. D8: Onyx's
  `- type: BodyPart / fractureProfile:` becomes `- type: WolfmedBodyPart / fractureProfile:`. P5-D7: no
  `amputationThresholds` key on Ipc/Cybernetic (Base<Slot>'s organic gun/laser-severing set is inherited
  instead of Onyx's own, unreachable-under-Wolfgate's-gib-rungs numbers). Diona's
  `amputationThresholds: {}` is set (disables `AmputationSystem` only; the inherited gib trigger still
  destroys the limb, U17).

## 3. Deviations from PLAN5 with justification

**One deviation.** PLAN5 §4 assigns `species_parts.yml`'s creation to **WP13-1** ("Species part wiring"),
bundled with the upstream `parent:`-list edits (PROTO M/N/O/P) that actually wire the four abstracts onto
`Body/Parts/{slime,diona}.yml`, `_EinsteinEngines/Body/Parts/ipc.yml` and
`_Shitmed/Body/Parts/cybernetic.yml`. My task brief for this invocation explicitly scoped the file's
*creation* into WP13-0 alongside `wounds.yml` and `containers.yml`. I followed the brief: the file is
created here, self-contained, **not wired to anything** — no `parent:` list in the tree references any of
the four new ids yet, so RT never loads them (they're `abstract: true`) and this package changes zero
runtime behaviour, exactly like the `wounds.yml`/`containers.yml` work PLAN5 itself assigns to WP13-0. The
upstream wiring edits (PROTO M/N/O/P) and the `InorganicWolfmed` container assignment to
`PartIPCBase`/`CyberneticPartBase` remain unimplemented and are explicitly flagged below for whichever
package implements WP13-1.

No other deviations. Fold classes, trap avoidance (no `maxAffected`, no extra limb rows, D9 fold,
§8.2-1 fold) and file placement all match PLAN5 §2.1/§2.2/§2.3 exactly.

## 4. Build/server/test output tails

**Content.Server build:**
```
Build succeeded.
    0 Error(s)
```

**Content.Client build:**
```
Build succeeded.
    0 Error(s)
```

**Headless server** (120 s, port 1299, `C:/tmp/wolfmed-plan/p5/wp/WP13-0-server.log`):
```
[INFO] cvarcontrol: Registered 33 CVars.
[INFO] root: Server Version 277.0.0.0 -> Ready
[WARN] eng: MainLoop: Cannot keep up!
[INFO] net: "::": "Socket bound to [::]:1299: True"
```
Zero `[ERRO]`/`[FATL]`/`Exception` lines (grep returned nothing). The lone `[WARN]` is the pre-existing,
unrelated main-loop notice seen in every prior WP checkpoint.

**Release YAML linter** (`C:/tmp/wolfmed-plan/p5/wp/WP13-0-yamllint-full.log`):
```
No errors found in 103947 ms.
```

**Independent structural check:** a standalone PyYAML parse (permissive `!type:` tag handler) of the edited
`wounds.yml` confirms exactly 30 top-level documents in the expected id order (Organic×2 → Ipc → Slime →
Cybernetic → Plant → OrganicFracture → CyberneticFracture → Blunt → BoneFracture → CyberneticFrameFracture
→ Slash → SystemicBleeding → Piercing → Burn → Electrical → SurgicalIncision → Dismemberment →
IpcMechanicalDamage → CyberneticMechanicalDamage → 4×Slime → 4×Plant → AmputationConsequence →
InternalBleeding → MedicalScar).

No tests were run — WP13-0 owns no test files per PLAN5 §4 (tests are WP13-6).

## 5. What later packages must know

- **`species_parts.yml`'s four abstracts are unwired.** WP13-1 must add one `# WOLFGATE` line to each of
  `Body/Parts/slime.yml` (`:4`), `Body/Parts/diona.yml` (`:3`), `_EinsteinEngines/Body/Parts/ipc.yml` (`:3`,
  plus the two gib-threshold numbers and the `InorganicWolfmed` container swap), and
  `_Shitmed/Body/Parts/cybernetic.yml` (`:3`, plus the `InorganicWolfmed` container swap and, if U15 is
  taken, a marked `Destructible` block) — **`WolfmedPart*` must be first in each `parent:` list**, per the
  file's own header comment and PLAN5 P5-D1 (RT first-parent-wins; putting it last only worked for phase-1
  files because none of *their* other parents declare a `WolfmedBodyPart`).
- **`SiliconWolfmed` is not yet on `MobIPC`.** WP13-2 must set `MobIPC`'s `Damageable.damageContainer` to
  `SiliconWolfmed` and, in the **same** package, add `SiliconWolfmed` to the welder's (`welders.yml`) and
  nanite applicator's (`_Mono/.../nanite_applicator.yml`) `damageContainers` lists — landing the mob
  container change without the two tool edits silently breaks IPC repair (P5-D5).
- **`InorganicWolfmed` is not yet on any part.** WP13-1 applies it to both `PartIPCBase` and
  `CyberneticPartBase`. Until then the `Cold`/`Shock`/`Caustic` markers left in the four profiles'
  `acceptedDamageTypes` (see inline `# WOLFGATE (P5-1/P5-D20)` comments) stay inert — this is expected and
  by design (verbatim-Onyx-copy requirement), not a bug to fix in this file.
- **Nothing in this package changes behaviour for any existing entity.** All 22 new ids (18 prototypes + 4
  abstracts) are unreferenced; the headless-clean result here is not yet evidence that IPC/slime/diona/
  cybernetic wounds work end-to-end — that only becomes testable after WP13-1/WP13-2 wire the profiles in.
- **Manifest ownership:** `Docs/Wolfmed/WOLFMED_MANIFEST.md` now has a "## Phase 5" section with a WP13-0
  subsection. WP13-7 should reconcile, not duplicate, the "Phase 5 — user decisions" tracking block I added
  (it currently only ticks off what WP13-0 touched: the `SiliconWolfmed`/`InorganicWolfmed` prototype
  *creation* half of U2(a)/U13′(b); everything else in DECISIONS' Phase-5 answers section is still open).
