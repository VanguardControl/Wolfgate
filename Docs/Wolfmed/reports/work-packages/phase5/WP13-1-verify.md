# WP13-1 verification — species part wiring and gib blocks

Verifier pass over `C:/tmp/wolfmed-plan/p5/wp/WP13-1-report.md` against DECISIONS.md (Phase 5 + §8.4),
PLAN5.md (§WP13-1, §1, §3, §5, §8, revision notes), the worktree at
`C:/Users/jzo12/Documents/GitHub/Wolfgate/.claude/worktrees/rules-motd-updates-11c89c` (WG) and
`C:/tmp/onyx` (ONYX, a sparse checkout — files outside the checked-out paths are still readable via
`git show HEAD:<path>`, used below for the Corvax IPC/Damage files).

## 1. Build / headless server

Both builds re-run independently, both 0 errors:

```
Content.Server (-c DebugOpt): Build succeeded.  0 Error(s)
Content.Client (-c DebugOpt): Build succeeded.  0 Error(s)
```

Headless server, port 1299, ~130 s wall clock
(`C:/tmp/wolfmed-plan/p5/wp/WP13-1-verify-server.log`, 109 lines): reached
`[INFO] root: Server Version 277.0.0.0 -> Ready` and bound the port
(`Socket bound to [::]:1299: True` / `0.0.0.0:1299: True`). `grep -nE "\[ERRO\]|\[FATL\]|Exception"` →
**0 matches**. Matches the report's own WP13-1-report-server.log result (also 0).

The report's own build/test artifacts were also cross-checked:
- `WP13-1-report-docktest.log`: 3/3 passed. `db.ef`/sqlite `[WARN]` lines present (11, all migration
  warnings, e.g. `PRAGMA foreign_keys = 0` and one `UpdateDataOperation` note on `admin_notes`) but **none
  are failures** — consistent with project memory ("db.ef sqlite warnings fail every pair test") not firing
  here; DockTest is still green.
- `WP13-1-report-entitytest.log`: `EntityTest|PrototypeSaveTest` 6 passed / 2 skipped / 0 failed.
- `WP13-1-report-tests.log`: `_Onyx.Wounds|_Onyx.Body|_Onyx.Medical|Wolfmed` 98/98 passed.
- `WP13-1-report-server.log`: reaches Ready, 0 ERRO/FATL/Exception.

**Verdict: PASS.**

## 2. Upstream discipline

```
git diff HEAD --stat -- Content.Shared Content.Server Content.Client Resources Content.IntegrationTests
```
gives exactly 7 tracked files (713 insertions(+), 8 deletions(-)):

| file | under `_Onyx`/`_WF`? | lines changed | marked? |
|---|---|---|---|
| `Resources/Prototypes/Body/Parts/diona.yml` | no | 1 | yes — `# WOLFGATE (P5-1): ...` on the changed line |
| `Resources/Prototypes/Body/Parts/slime.yml` | no | 1 | yes — same pattern |
| `Resources/Prototypes/_EinsteinEngines/Body/Parts/ipc.yml` | no | 18 (+16/-2) | yes — parent line marked; new `Damageable` block prefaced by a 5-line `# WOLFGATE (P5-2/P5-D20, U13'b)` comment; each changed threshold number carries its own inline `# WOLFGATE (P5-D8): 110 -> 190, ...` / `150 -> 210, ...` |
| `Resources/Prototypes/Entities/Objects/Tools/welders.yml` | no | 4 (+4) | yes — 3-line `# WOLFGATE (P5-D5, PROTO S)` comment directly above the new `- SiliconWolfmed` entry |
| `Resources/Prototypes/_Mono/Entities/Objects/Tools/nanite_applicator.yml` | no | 3 (+3) | yes — same pattern (PROTO T) |
| `Resources/Prototypes/_Onyx/Wounds/wounds.yml` | **yes, `_Onyx`** | 664 | out of scope — see below |
| `Resources/Prototypes/_Shitmed/Body/Parts/cybernetic.yml` | no | 28 (+27/-1) | yes — parent line marked; `damageContainer` line prefaced by a 4-line comment; new `Destructible` block prefaced by a 6-line `# WOLFGATE (P5-D8b, U15a)` comment |

`wounds.yml`'s 664-line diff is a byte-for-byte match to the already-verified `WP13-0.patch` (diffed the two
patches directly — zero difference beyond the file header), confirming WP13-1 made **no** changes to it and
its content was already cleared by `WP13-0-verify.md`.

**Marking style note (not a defect):** the two new `Damageable`/`damageContainer` lines in `ipc.yml`/
`cybernetic.yml` and the four new lines of the new `Destructible` block in `cybernetic.yml` do not each
carry their own inline `# WOLFGATE` tag — they are attributed to one marker comment immediately above the
block. This matches the project's own established convention (see PLAN5 §3.2's worked PROTO Q example,
which marks a whole new component block with one leading comment, and the D3 deviation the report itself
records for PROTO O(c)'s two-numbers-vs-one-comment tradeoff). Every changed/added line outside `_Onyx`/`_WF`
is attributable to an adjacent WOLFGATE comment; none is unmarked or unexplained.

**Docs**: `Docs/Wolfmed/DECISIONS.md`'s diff (visible in the §7 snapshot, which includes `Docs`) predates
WP13-1 — diffed identical to the corresponding section of `WP13-0.patch` except for one trailing sentence
already present there (the WP13-0 "Execution" bullet). WP13-1 made no further edit to it.
`Docs/Wolfmed/WOLFMED_MANIFEST.md` is WP13-1's own manifest addition (§5 below), not a "hook" needing a
WOLFGATE marker.

**PROTO S/T (D1) — file-ownership deviation, not a marking or authorisation problem.** PLAN5 §3.2/§4 assign
`welders.yml` and `nanite_applicator.yml` to **WP13-2**, not WP13-1. The report's D1 states the task brief
for this invocation explicitly required landing the two companion lines early and documents forward guidance
("WP13-2 must not re-add these two entries"). Verified this is D2-safe and inert: `grep -rln SiliconWolfmed`
across `Resources/Prototypes` returns exactly 3 files — `containers.yml` (the definition, WP13-0), and the
two tool files just edited — **no entity anywhere references `SiliconWolfmed` as its own
`damageContainer`** (confirmed `MobIPC`'s `Damageable` is still absent/default — PROTO Q has not landed).
So the two lines are unreachable dead data until WP13-2 runs, exactly as the report claims. This is a
work-package-boundary deviation with the same shape and same justification standard as the WP13-0
`species_parts.yml` deviation the prior verification pass (`WP13-0-verify.md`) already accepted as
non-blocking. Treated the same way here: **flagged, not a blocker or major.**

Every marked line was checked for §3 authorisation:
- PROTO M/N/O(a)(c)/P(a)(b)(c) — all quoted verbatim (content, not just shape) in PLAN5 §3.2's table for
  WP13-1; content matches exactly (thresholds 190/210, no Heat rung, `InorganicWolfmed` container, parent
  ordering).
- O(b) — U13′ only, DECISIONS §8.4 confirms U13′ = (b): landed correctly.
- P(c) — U15 only, DECISIONS §8.4 confirms U15 = (a): landed correctly.
- PROTO S/T — authorised content-wise by PLAN5 §3.2 (quoted verbatim), only the *package* differs (D1
  above).

**Verdict: PASS**, with D1 (PROTO S/T early landing) and the block-comment marking style both noted as
accepted, non-blocking deviations.

## 3. Vendoring fidelity

WP13-1 itself vendors nothing new from Onyx — all Onyx-sourced content (`wounds.yml`'s 16 prototypes,
`species_parts.yml`'s four abstracts) landed in WP13-0 and is unchanged here (confirmed in §2). WP13-1's
Onyx-sourced content is the *shape* of the four upstream `parent:`/container edits, which were checked
against Onyx directly:

- `git show HEAD:Resources/Prototypes/Corvax/Body/Species/ipc.yml` (read via `git show` — this path is
  outside ONYX's sparse-checkout working tree, so a plain `find`/`grep` on disk returns nothing; the object
  is still fetchable from the partial clone) — `OrganIpcExternal` declares `Woundable/profile:
  IpcBodyPartProfile` and `BodyPart/fractureProfile: null`, **no** `amputationThresholds` key. Matches
  `WolfmedPartIpc` (WP13-0) and P5-D7's stated reasoning that Onyx's own per-slot IPC numbers
  (`OrganIpcArmLeft`, Slash 270/Piercing 400/...) sit on a different, lower prototype and are correctly
  *not* copied onto the abstract.
- `git show HEAD:Resources/Prototypes/Corvax/Damage/containers.yml` — `SiliconIpc` =
  `supportedGroups: [Brute, Burn, Electronic]` + `supportedTypes: [Heat, Shock]` (the latter two redundant
  with `Burn`). `Burn` group (`Resources/Prototypes/Damage/groups.yml:10-16`, WG) = `Heat, Shock, Cold,
  Caustic`. This confirms the report's and PLAN5's repeated claim that Onyx's SiliconIpc container carries
  the whole Burn group including `Cold`/`Caustic`, which WG's stock `Inorganic`
  (`Brute + Heat/Shock`) and `Silicon` (`Brute + Heat/Shock/Radiation`) do not — the motivating fact behind
  U13′(b)/`InorganicWolfmed`.
- `InorganicWolfmed` (`_WF/Wolfmed/Damage/containers.yml`, WP13-0, unchanged by WP13-1) =
  `supportedGroups: [Brute, Burn]` + `supportedTypes: [Radiation]` = a strict superset of both `Inorganic`
  and `Silicon` — verified by direct comparison of the three container definitions. No type is lost by
  switching either part base onto it.
- Diona: `git show HEAD:Resources/Prototypes/Corvax/Body/Species/diona.yml` (or the checked-out
  `Body/Species/diona.yml`) — `OrganDionaExternal` has `amputationThresholds: {}`; `OrganSlimePersonExternal`
  does **not**. `WolfmedPartDiona`/`WolfmedPartSlime` (WP13-0) reproduce this asymmetry correctly, which the
  report's own §3 "factual correction" catches PLAN5 getting wrong (PLAN5 says both are "unseverable by
  threshold" — false for slime).

Every prototype-block edit WP13-1 itself makes (the four `parent:` lines, two `Damageable` additions, three
number changes, one new `Destructible` block) is quoted **verbatim** in PLAN5 §3.2 and matches the worktree
byte-for-byte (diffed by eye against the git diff in §2) — there is no line in WP13-1's own diff that
differs from Onyx-motivated content without a WOLFGATE marker explaining the divergence.

**Verdict: PASS.**

## 4. Collisions / new C# / inheritance reasoning

- **New prototype ids:** none in WP13-1 (all 22 landed in WP13-0, confirmed unused before this package and
  wired now). `grep -rn "id: WolfmedPart(Slime|Diona|Ipc|Cybernetic)"` across `Resources/` → exactly one
  definition site each, in `_WF/Wolfmed/Body/species_parts.yml` (WP13-0). No duplicates.
- **New C# files:** `git status --porcelain -- Content.Shared Content.Server Content.Client
  Content.IntegrationTests | grep -i '\.cs$'` → **empty**. Zero new `.cs` files, tests included.
- **`SiliconWolfmed`/`InorganicWolfmed` reachability:** grepped repo-wide; `InorganicWolfmed` is referenced
  by exactly `PartIPCBase` and `CyberneticPartBase` (the two authorised sites) plus its own definition.
  `SiliconWolfmed` is referenced by exactly the two tool files plus its own definition (see §2 D1). No
  unintended collisions.

### Inheritance chains, resolved by hand, for every Destructible/damage-container change

**IPC (`_EinsteinEngines/Body/Parts/ipc.yml`).** `PartIPCBase: parent: [WolfmedPartIpc, BasePartInorganic]`.
Every concrete IPC slot is `parent: [PartIPCBase, Base<Slot>]` (`HeadIPC`, `LeftArmIPC`, `RightArmIPC`,
`LeftLegIPC`, `RightLegIPC`, `LeftHandIPC`, `RightHandIPC`, `LeftFootIPC`, `RightFootIPC` — all 9 confirmed
by direct read of the file) — `PartIPCBase` is first, so its `Destructible`/`Damageable` (both non-`Always`
`[DataField]`s, confirmed on `DestructibleComponent`/`ContainerManagerComponent`) win outright over whatever
`BaseHead`/`MajorLimb`/`MinorLimb` the second parent supplies:

| prototype(s) | effective Destructible after WP13-1 | matches report? |
|---|---|---|
| Left/RightArmIPC, Left/RightLegIPC | Blunt 190 / Slash 210, no Heat (from `PartIPCBase`, beats `BaseLeftArm→MajorLimb`'s 190/210/250+Ash) | yes |
| Left/RightHandIPC, Left/RightFootIPC | Blunt 190 / Slash 210, no Heat (beats `MinorLimb`'s 150/180/230+Ash) | yes |
| HeadIPC | Blunt 190 / Slash 210, no Heat (beats `BaseHead`'s **own** `Destructible # Mono` — 500/600/700+Ash, confirmed present at `Body/Parts/base.yml:105-133`, contradicting PLAN5's "heads have no gib trigger" claim; report's correction is verified accurate) | yes |
| TorsoIPC | unchanged, 400/400 (own block, `parent: [PartIPCBase, BaseTorsoInorganic]`, but `TorsoIPC` itself redeclares `Destructible`, which wins for the same first-in-list reason) | yes |

**Cybernetic (`_Shitmed/Body/Parts/cybernetic.yml`).** `CyberneticPartBase: parent: [WolfmedPartCybernetic,
BasePartInorganic]`. All 14 other ids in the file descend from it (counted: `LeftArmCyberneticBase`,
`RightArmCyberneticBase`, `LeftLegCyberneticBase`, `RightLegCyberneticBase`, `LeftHandCybernetic`,
`RightHandCybernetic`, `LeftFootCybernetic`, `RightFootCybernetic`, `JawsOfLifeLeftArm`,
`JawsOfLifeRightArm`, `SpeedLeftLeg`, `SpeedRightLeg`, `DexLeftHand`, `DexRightHand` — matches the report's
"All 14" claim exactly). The 4 `*Base` ids are `parent: [CyberneticPartBase, Base<Slot>]` (`CyberneticPartBase`
first); the 4 hand/foot ids are `parent: [CyberneticPartBase, Base<Slot>]` directly; `JawsOfLife*`/`Speed*`
single-parent onto the matching `*Base`; `DexLeftHand`/`DexRightHand` single-parent onto
`Left/RightHandCybernetic`. Every leaf therefore resolves `CyberneticPartBase`'s new `Destructible` (first
parent, or inherited unchanged through a single-parent chain that already resolved it):

| prototype(s) | effective Destructible after WP13-1 | matches report? |
|---|---|---|
| Left/RightArmCyberneticBase, Left/RightLegCyberneticBase, JawsOfLife{Left,Right}Arm, Speed{Left,Right}Leg | Blunt 190 / Slash 210, no Heat (beats `MajorLimb`'s 190/210/**250+Ash**) | yes |
| Left/RightHandCybernetic, Left/RightFootCybernetic, Dex{Left,Right}Hand | Blunt 190 / Slash 210, no Heat (beats `MinorLimb`'s 150/180/**230+Ash**) | yes |

No Ash/Heat rung anywhere on IPC or cybernetic parts, confirmed by reading the new `Destructible` blocks
directly (both list only `Blunt`/`Slash` triggers, no `Heat` trigger, no `SpawnEntitiesBehavior{Ash}`) — the
report's "no Ash rung on cybernetic parts" claim holds.

**Slime/diona (unchanged legs/arms/hands/feet, confirmed for completeness).** `LeftArmSlime: parent:
[PartSlime, BaseLeftArm]`, `LeftArmDiona: parent: [PartDiona, BaseLeftArm]`; `PartSlime`/`PartDiona`
themselves declare no `Destructible` (only `Woundable`/`WolfmedBodyPart` via `WolfmedPartSlime`/
`WolfmedPartDiona`, first parent), so `BaseLeftArm→MajorLimb`'s 190/210/250+Ash passes through unchanged —
matches the report's "unchanged" row.

**Verdict: PASS** — every inheritance claim in the report's §4 table was independently re-derived from the
actual `parent:` chains and matches exactly, including the two counts (9 IPC slots, 14 cybernetic
descendants) and the "no Heat/Ash on cybernetic parts" guarantee.

## 5. Manifest

`Docs/Wolfmed/WOLFMED_MANIFEST.md` diff adds a `### WP13-1` section with one table row per Onyx-motivated
edit (slime, diona, ipc PROTO O a/b/c, cybernetic PROTO P a/b/c, PROTO S, PROTO T, species_parts.yml wiring)
— one row per file touched, matching the report's 8-file table (species_parts.yml gets a row; the manifest
file itself does not need a row about itself). The WP13-1-1 deviation (static `wounds` container),
the effective-threshold table, the two factual corrections (organic heads / slime severability), and the
"what later packages must know" section are all present in the manifest, matching the report content
verified above. `Phase 5 — user decisions` section correctly updates U3′(b)/U15(a)/U2(a)/U13′(b) status to
reflect WP13-1's landing without overclaiming U2(a) as complete (it correctly states `MobIPC`'s `Damageable`
is still WP13-2's job).

**Verdict: PASS.**

## 6. Plan conformance / DECISIONS §8.4

Every file in PLAN5's WP13-1 table (§4) is present and edited as specified:
`_WF/Wolfmed/Body/species_parts.yml` (owned by WP13-0 per an accepted prior deviation, wired here),
`Body/Parts/slime.yml`, `Body/Parts/diona.yml`, `_EinsteinEngines/Body/Parts/ipc.yml`,
`_Shitmed/Body/Parts/cybernetic.yml` — all 5 confirmed present and correctly edited (§2/§4 above). The two
extra files (`welders.yml`, `nanite_applicator.yml`) are additions beyond the WP13-1 table, justified as D1
(§2 above).

DECISIONS.md §8.4 answers relevant to this WP:
- **U3′ = (b)** "190/210 MajorLimb parity for IPC limb gib triggers" — landed exactly (PROTO O(c)), verified
  in §4.
- **U15 = (a)** "marked Destructible on CyberneticPartBase, no Heat/Ash rung" — landed exactly (PROTO P(c)),
  verified in §4.
- **U13′ = (b)** "_WF InorganicWolfmed part container restoring Cold/Caustic" — landed on both `PartIPCBase`
  and `CyberneticPartBase` (PROTO O(b)/P(b)), verified in §2/§3.
- **U2 = (a)** "SiliconWolfmed container + PROTO S/T companion lines" — the container (WP13-0) and the two
  companion lines (PROTO S/T, landed early per D1) are in place; `MobIPC`'s own `Damageable` switch is
  correctly left to WP13-2 and the manifest says so explicitly, not silently.
- No other §8.4 row (U1, U4, U5, U16-18) calls for anything in WP13-1's scope, and none of those files were
  touched.

**Verdict: PASS.**

## 7. Snapshot

```
git -C WG diff HEAD -- Content.Shared Content.Server Content.Client Resources Docs Content.IntegrationTests > C:/tmp/wolfmed-plan/p5/snapshots/WP13-1.patch
git -C WG ls-files --others --exclude-standard -- Content.Shared Content.Server Content.Client Resources Docs Content.IntegrationTests > C:/tmp/wolfmed-plan/p5/snapshots/WP13-1.untracked.txt
```

- `WP13-1.patch` — written, 1060 lines, 9 `diff --git` headers (`Docs/Wolfmed/DECISIONS.md` — pre-existing
  from before WP13-1, see §2; `Docs/Wolfmed/WOLFMED_MANIFEST.md`; the 7 tracked files from §2).
- `WP13-1.untracked.txt` — written, 2 lines: `Resources/Prototypes/_WF/Wolfmed/Body/species_parts.yml`,
  `Resources/Prototypes/_WF/Wolfmed/Damage/containers.yml` (both WP13-0-owned, edited-in-place by WP13-1 for
  species_parts.yml — see WP13-1-1).

## 8. Cybernetic/Destructible parent-chain confirmation (explicit)

Covered inline in §4 — restated for the checklist: every modified part prototype (`PartIPCBase` and its 9
concrete descendants; `CyberneticPartBase` and its 14 concrete descendants) had its full parent chain
resolved by hand from the actual files, not assumed from the report. In every case the effective
`Destructible` thresholds are Blunt 190 / Slash 210 with **no Heat trigger and no `Ash`
(`SpawnEntitiesBehavior`) behavior anywhere** — confirmed by reading both new `Destructible` blocks directly,
neither of which contains a `Heat` trigger entry. No cybernetic part rungs Ash.

## Summary

All 7 tracked non-`_Onyx` files carry only WOLFGATE-attributable changes, content matches PLAN5 §3.2
verbatim, every DECISIONS §8.4 answer relevant to this WP is honoured, inheritance chains re-derived by hand
match the report's table exactly (including the two counts and the no-Ash guarantee), vendoring fidelity
against Onyx (via `git show` where the sparse checkout omits the Corvax paths) confirms the motivating facts
behind U13′(b), builds are 0/0 errors, the headless server is clean, and the two independently-verified test
logs (DockTest, EntityTest|PrototypeSaveTest, and the Wolfmed suite) are all green with no db.ef-caused
failures. Two process deviations are flagged, both already justified in the report/manifest and both
D2-safe/inert: D1 (PROTO S/T landed one package early) and the WP13-1-1 addition (static `wounds` container,
required by a real `PrototypeSaveTest` failure and fixed inside the `_WF` file, not upstream). Neither rises
to blocker or major.

## Files touched by this verification

Only `C:/tmp/wolfmed-plan/p5/wp/WP13-1-verify.md` (this file), `C:/tmp/wolfmed-plan/p5/wp/WP13-1-verify-server.log`,
and the two snapshot files under `C:/tmp/wolfmed-plan/p5/snapshots/`. No file under WG was modified.
