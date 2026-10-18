# Xenonids port brief

Port of RMC-14's xenomorphs (every caste, the hive, weeds, resin structures, eggs, facehuggers, evolution, plasma,
abilities) onto Wolfgate. Player-controlled xenos first; NPC behaviour is added by the owner later, so keep RMC's HTN
hooks where they are cheap but do not port RMC's NPC module.

Sources: RMC-14 at `7fca80389a` (2026-10-15, MIT, engine v264.0.2 fork), checkout `E:/GitHub/RMC-14`. Wolfgate runs
engine v291.0.0; the worktree's `RobustToolbox/` is a private source copy of `E:/GitHub/RobustToolbox-291` (never
edit it, never delete it recursively, never `git submodule` anything).

Read `AGENTS.md` first. Everything below is specific to this port.

## Modules

| Module | Holds | Namespace |
|---|---|---|
| `RMCCore` | RMC-14 infrastructure the xeno code leans on and that later RMC ports can reuse: actions helpers, size stun/daze, slow/root, pulling, damage (`SharedRMCDamageableSystem`, obstacle slamming, damage over time), `CMArmor`, melee (`SharedRMCMeleeWeaponSystem`, melee multipliers, immune to unarmed), emotes, `RMCMapSystem`, maths, camera shake, aura, dialog, line, sprite draw depth, animations, on-collide damage, hands (whitelist pickup, drop on incapacitate), gibbing, deafness, blinded, stealth/invisibility, stamina, evasion, lag compensation + mob collision, `SharedCMChatSystem`, explosion bits (`StunOnExplosionReceived`, limit hits), projectile bits, shields (`XenoShieldSystem` and friends), scaling, `FixedIdentity`, `SoundOnDeath`, `RemoveComponents`, `BlockEntityStorage`, `RMCVisibleToGhostsOnly`, step-trigger immunity, vents (vent crawling) if Wolfgate has none | `Content.<Side>._WF.RMCCore.<Sub>` |
| `Xenonids` | everything under RMC's `_RMC14/Xenonids`, plus `Name`, `Announce` (xeno part), `Tracker/Xeno` | `Content.<Side>._WF.Xenonids.<Sub>` |

Folder structure inside a module mirrors RMC's (`Xenonids/Plasma`, `RMCCore/Stun`). RMC component, prototype and
locale IDs are kept (AGENTS.md allows that for ports); do not add `WF` prefixes to ported IDs. New Wolfgate-only
prototypes get the `WF` prefix.

`python Tools/_WF/Xenonids/import_rmc.py <Module> <rmc path>...` copies files with namespaces, `readonly`, `partial`
and resource paths rewritten; `Tools/_WF/Xenonids/namespaces.yml` is the map. Add rules there rather than hand-editing
usings in many files. Run `--dry-run` first and read the "still referenced" report.

Resources: textures go to `Resources/Textures/_WF/Xenonids/...` (RMC's `_RMC14/Mobs/Xenonids/...` becomes
`_WF/Xenonids/Mobs/Xenonids/...`, the tool does this), audio to `Resources/Audio/_WF/Xenonids/...`, locale to
`Resources/Locale/en-US/_WF/Xenonids/*.ftl`, prototypes to `Resources/Prototypes/_WF/Xenonids/...`. Prototypes that are
imported before their components exist wait in `Tools/_WF/Xenonids/staging/` (outside `Resources`, so the linter and
server don't load them) and move into `Resources/Prototypes/_WF/Xenonids` with the lane that brings their C#.

## What Wolfgate already has (reuse, don't duplicate)

- `Content.Shared/_RMC14/CCVar/RMCCVars.cs` (Frontier's copy; a cvar the xeno code reads that is missing goes in a
  new `[CVarDefs] WFXenoCVars` in `Content.Shared/_WF/Xenonids/WFXenoCVars.cs`, not in that file),
  `_RMC14/Weapons/Ranged/Prediction` (gun prediction), `_RMC14/Vendors`, `_RMC14/CombatMode`, `_RMC14/Wieldable`.
- `Content.Shared/_WF/Barricades`: `BarricadeComponent`, `BarbedComponent`, `DirectionalAttackBlockerComponent`,
  `SharedDirectionalAttackBlockSystem`, `RMCUpgradeSystem` (RMC's Entrenching/Barricade). Map
  `Content.Shared._RMC14.Entrenching` and `.Barricade` there.
- `Content.Shared/_WF/Vehicles`: RMC's vehicle framework (`GridVehicleMoverComponent`, `HardpointSystem`,
  `VehicleWheelSystem`, `VehicleInteriorOccupantComponent`, renamed base `RMCVehicleSystem`). Xeno-vs-vehicle code
  (crusher vs wheels, tail attacks into interiors) may compile against it; cut it where it costs more than ten lines.
- NightVision: `Content.Shared/NightVision/SharedNightVisionSystem` + `Content.Shared/Overlays/NightVisionComponent`
  (same type names as RMC's). Do NOT port RMC's NightVision; xenos use Wolfgate's component.
  `RMCNightVisionVisibleComponent` and `RMCMesonsNonviewableComponent` become empty marker components in `Xenonids`.
- StatusEffectNew: `Content.Shared.StatusEffectNew.StatusEffectsSystem` (RMC calls it `SharedStatusEffectsSystem`;
  same API). Stun: `SharedStunSystem.TryParalyze/TryStun/TrySlowdown/TryKnockdown` exist. Stamina: `StaminaSystem`
  (RMC: `SharedStaminaSystem`).
- Wolfmed (`_WF/Wolfmed`) is the medical system; RMC's Medical module is not ported. Drop `RMCMedicalExamine`,
  `RMCBlockMedicalExamine`, `CMSurgeryTarget`, `RMCSurgeryXenoHeart`, `RMCDefibrillatorBlocked`, `RMCHealthExaminable`
  from prototypes (or make them marker components if a system needs the flag).
- Upstream `MobXeno*` NPC prototypes and `Mobs/Aliens/Xenos` textures exist; they are unrelated and stay.
- `Content.Client/_Shitmed/Xenonids/UI/XenoChoiceControl` is Shitmed's copy of RMC's control. Reuse it if its API
  fits; otherwise import RMC's as `RMCXenoChoiceControl`.
- `Xeno` NPC faction exists in `ai_factions.yml`; xeno mobs take `NpcFactionMember` with faction `Xeno`.

## API deltas (RMC-14 is on upstream's newer Actions API; Wolfgate is on the older one)

| RMC | Wolfgate |
|---|---|
| `ActionComponent`, `Entity<ActionComponent>` | `BaseActionComponent`; `GetActions(uid)` yields `(EntityUid Id, BaseActionComponent Comp)` tuples |
| `TargetActionComponent` | `BaseTargetActionComponent` (`EntityTargetActionComponent`, `WorldTargetActionComponent`, `EntityWorldTargetActionComponent`) |
| `_actions.GetAction(uid)` → `Entity<ActionComponent>?` | `_actions.TryGetActionData(uid, out var comp)`; write a `GetAction` helper in `SharedRMCActionsSystem` |
| `_actions.GetEvent(uid)` | `comp.BaseEvent` via `TryGetActionData`; helper |
| `_actions.SetIcon`, `SetIconOn`, `IsCooldownActive`, `RemoveCooldown`, `SetEvent`, `SetEventTarget` | missing; add helpers in `SharedRMCActionsSystem` using `BaseActionComponent` fields + `Dirty` |
| `ActionValidateEvent` | missing; the attempt checks that use it move into `RMCActionUseAttemptEvent` handling or a marked hook in `SharedActionsSystem.PerformAction` |
| YAML `- type: Action` / `- type: TargetAction` on action prototypes | not a component here; RMC action prototypes use `InstantAction`/`EntityTargetAction`/`WorldTargetAction` with the same fields (`useDelay`, `icon`, `event`, `priority`, `itemIconStyle`, `checkCanInteract`, `range`, `whitelist`, `canTargetSelf`). Convert each action prototype by hand; the field names mostly match. |

Known engine traps from the v264 to v291 gap: `IMapManager` is gone from most places (use `SharedMapSystem`,
`TransformSystem`); `DrawDepth` needs the alias `using DrawDepthTag = Robust.Shared.GameObjects.DrawDepth;` when
Content's `DrawDepth` enum is in scope; `EntityManager.SpawnAtPosition`, `Spawn` overloads changed in places;
`SharedTransformSystem.GetMoverCoordinates` exists; `PhysicsComponent` fields are read through `SharedPhysicsSystem`.
Let the compiler guide you and fix the pattern in one place where you can (an extension method in `RMCCore`).

## Cut list

Replace with the stated alternative; never leave dead `using`s or stubs that compile but silently break play.

| RMC thing | Here |
|---|---|
| `MarineComponent` (host / enemy test) | `XenoSystem.IsHostCandidate(uid)` in `Xenonids`: alive `MobStateComponent` holder with `HumanoidAppearanceComponent` or `BodyComponent`, not a xeno. `Marines` namespace is never imported. |
| `TacticalMap*`, `XenoMapTracked`, `XenoStructureMapTracked`, `AreaInfo`, `TacMapXenoAlert` | dropped from prototypes and code |
| `Intel*`, `Commendation*`, `Spottable`, `MotionDetectorTracked`, `RMCCanBeFultoned`, `JobPrefix`, `Skills`, `Language`, `LanguageLearning`, `RMCSurvivor`, `EquipSurvivorPreset`, `EntityPreset`, `FiremanCarriable`, `MultiHandedHolder`, `Undisposable`, `RMCPointing`, `RMCInnateRadioTextIncrease`, `RMCBuckle*`, `BuckleWhitelist`, `CrashLandable`, `InnateCommandSpeech`, `RoofingEntity`, `Synth`, `BlockIdExamine`, `ShortExamine` | dropped |
| Dropship hijack (`DropshipHijacker`, `HijackBurrowedSurge`, hijack events) | dropped; the queen's hijack ability is not ported |
| `CMDistressSignalRuleComponent`, `RMCPlanetSystem`, `MarinesLandedChangedEvent`, `SharedRMCGameTickerSystem`, `RMCPlayerJoinedLobbyEvent`, job slot scaling | dropped; larva and evolution timers run from round start or hive creation |
| Communications tower, orbital cannon, sentry, bioscan, ARES, `Announce` marine side | dropped |
| RMC fire (`SharedRMCFlammableSystem`, `OnFireComponent`, tile fire, `RMCFireColor`, `RMCImmuneToFireTileDamage`, fire patting) | upstream `FlammableComponent`; `RMCImmuneToIgnition` becomes a marker checked in a `Xenonids` `IgnitionAttemptEvent` handler if upstream raises one, else dropped |
| `AreaSystem` checks (no weeds in some areas) | removed; weeds grow anywhere weedable |
| RMC Medical, Chemistry (`ReagentPrototype`, `VaporHitEvent`), Vendors, Storage (`Limit`), Cryostorage | Wolfgate upstream equivalents or dropped |
| `RMCCVars.X` missing here | `WFXenoCVars.X` |
| `DeletedByWeedKiller` | marker component in `Xenonids` (no weed killer yet) |
| Xeno figurines, `fun.yml`, survivor jobs, `Roles/Jobs/Survivor` | not ported |

## Upstream hooks

The xeno code subscribes to events that RMC raises from upstream files. Each needs a marked hook in Wolfgate's copy
of that file (`// WOLFGATE(RMCCore): reason` or `WOLFGATE(Xenonids)`), the smallest that works. Known candidates (from
RMC's `git diff` of these files; check each for the xeno-relevant part): `SharedMeleeWeaponSystem`
(`MeleeAttackAttemptEvent`, melee range events), `DamageableSystem` (`DamageModifyAfterResistEvent`,
`DamageStateCritBeforeDamageEvent`), `PullingSystem` (`RMCPullToggleEvent`, pull attempt hooks),
`SharedActionsSystem` (validate/attempt), `SharedHandsSystem.Drop`, `SharedInteractionSystem`, `SharedDoorSystem`
(xeno doors), `ActionBlockerSystem`, `Identity` (`RMCGetFixedIdentityEvent`), `StatusEffectsSystem`,
`SharedStationSpawningSystem`, `FlammableSystem`, `ExplosionSystem`, client `ActionsSystem`, `SpriteSystem` draw depth
(`GetDrawDepthEvent`), ventcrawl attempt. Wolfgate's `_WF/Barricades` already hooks `SharedMeleeWeaponSystem.AttemptAttack`;
extend that pattern, don't add a second subscription to the same (component, event) pair.

## Verification per lane

1. `dotnet build Content.Client/Content.Client.csproj -c DebugOpt -p:WarningsAsErrors=nullable` (builds Shared too) and
   the same for `Content.Server`. CI treats nullable warnings as errors.
2. Every prototype you move into `Resources/Prototypes/_WF/Xenonids` must load: run the YAML linter
   (`dotnet run --project Content.YAMLLinter -c Release`) or, cheaper while iterating, start a headless server
   (`dotnet run --project Content.Server -c DebugOpt -- --cvar game.auto_pause_empty=false`, read its log for
   `[ERRO]`/`[FATL]`, stop it).
3. Logic that is cheap to test gets an NUnit test under `Content.IntegrationTests/Tests/_WF/Xenonids` (no
   `Destructive = true` pools).
4. Report what you ran and what you did not.

Commits: short lowercase subject, no body (`xeno core`, `rmccore`), one per verified lane; never amend or rebase.
Don't run `python Tools/_WF/Ci/modules.py --write`; the integrator does that once at the end.
