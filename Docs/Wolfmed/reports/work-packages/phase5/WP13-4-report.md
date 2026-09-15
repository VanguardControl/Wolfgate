# WP13-4 — Cable coil `treatmentCapabilities` (P5-3, U5)

## 1. Files created/modified

| File | Status |
|---|---|
| `Resources/Prototypes/Entities/Objects/Tools/cable_coils.yml` | modified — 1 line added |
| `Docs/Wolfmed/WOLFMED_MANIFEST.md` | modified — WP13-4 table row + notes appended, U5 bullet updated from "not yet landed" to "LANDED in WP13-4" |

No new files, no new prototype ids, no C# touched.

## 2. WOLFGATE edit and reason

`CableStack`'s `- type: Healing` block (`cable_coils.yml`), directly under the existing
`damageContainers: [Silicon]`:

```yaml
    treatmentCapabilities: [Electrical] # WOLFGATE (P5-3): matches ONYX cable_coils.yml:179 and Wolfgate's own damageContainers: [Silicon] intent. Without it the default [Biological] (HealingComponent.cs:72-73) overlaps OrganicBodyPartProfile and the coil heals human Heat/Shock wounds at -3/-3 per 0.6s, because HOOK 8 skips the damageContainers check for wound hosts (HealingSystem.cs:201-210) and WoundHealingSystem.IsCompatiblePart never reads it (:154-166).
```

Reason: `HealingComponent.treatmentCapabilities` defaults to `[Biological]`. The wound-host healing path
(HOOK 8 in `HealingSystem.cs`) skips the `damageContainers` check entirely, and
`WoundHealingSystem.IsCompatiblePart` never reads `damageContainers` either — only
`treatmentCapabilities` gates a `Healing` item against a part profile's `acceptedTreatmentTypes`. Without
this line the cable coil silently healed organic (human) Heat/Shock wounds despite its
`damageContainers: [Silicon]` implying otherwise. This is exactly PLAN5 PROTO U / DECISIONS U5: nerf the
coil to Mechanical-adjacent healing only, matching Onyx's own annotation intent (Onyx uses
`[Mechanical, Electrical]` on its equivalent; Wolfgate's coil is scoped to `[Electrical]` per PLAN5's exact
spec, since `[Electrical]` already overlaps IPC/cybernetic `[Mechanical, Electrical]` profiles).

Per PLAN5 §4: **only this one line** — Onyx's `delay: 3.5` and Onyx's damage-type set are explicitly NOT
adopted; Wolfgate's existing `delay: 0.6` / Heat −3 / Shock −3 / Radiation −3 stand untouched.

## 3. Deviations from PLAN5

None. The single line matches PROTO U's spec verbatim (id, container list, comment content and reasoning).

## 4. Build / server output tails

Both builds — `Content.Server.csproj` and `Content.Client.csproj`, `-c DebugOpt`, sequential:

```
Build succeeded.
    0 Error(s)
```

Headless server, 120 s, port 1299 (`C:/tmp/wolfmed-plan/p5/wp/WP13-4-report-server.log`):

```
[INFO] cvarcontrol: Registered 33 CVars.
[INFO] root: Server Version 277.0.0.0 -> Ready
[WARN] eng: MainLoop: Cannot keep up!
[INFO] net: "::": "Socket bound to [::]:1299: True"
[INFO] net: "::": "Network thread started"
[INFO] net: "0.0.0.0": "Socket bound to 0.0.0.0:1299: True"
[INFO] net: "0.0.0.0": "Network thread started"
```

Zero `[ERRO]`/`[FATL]`/`Exception` lines anywhere in the log (grep confirmed). The `MainLoop: Cannot keep
up!` warning is a routine startup-load warning unrelated to this change (seen in prior WP packages' logs
too).

YAMLLinter and `DockTest` were not re-run this package — no new prototype ids and no new file means no new
failure surface for either; PLAN5 §4's Checkpoint line names them but the orchestrator (WP13-7) can run
them as a cross-package reconciliation pass if a fresh signal across all of phase 5 is wanted.

## 5. What later packages must know

- U5 is fully landed: cable coils only treat `Electrical`-tagged wound profiles now. IPC
  (`IpcBodyPartProfile`) and cybernetic (`CyberneticBodyPartProfile`) part profiles both carry
  `[Mechanical, Electrical]` (landed in WP13-1/WP13-2), so the coil still heals them — humans/organic
  hosts lose the heal entirely.
- This is a **changelog-worthy balance change** per DECISIONS.md §111 — WP13-7 (docs/manifest/status
  package) should surface it in whatever changelog-facing doc phase 5's summary produces; it is not a
  silent bugfix.
- No test in PLAN5 §6 (T-P5-*) explicitly covers this line; if WP13-6 wants a regression guard, the shape
  is: apply Heat/Shock damage to a human limb, run the cable-coil `Healing` do-after, assert the wound
  severity does NOT drop (organic case), then repeat on an IPC/cybernetic limb and assert it DOES drop.
