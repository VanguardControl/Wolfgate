# Xenonids prototype staging

RMC-14's xeno prototypes, run through `Tools/_WF/Xenonids/import_rmc.py` (resource paths point at `_WF/Xenonids`, type names follow `namespaces.yml`) and parked here, outside `Resources`, so the YAML linter and the server do not load them while the components they use do not exist. A file moves to `Resources/Prototypes/_WF/Xenonids/<same sub path>` with the lane that brings its C#; the `deps` files move with the first file that needs them (split a block out into the module when it is used elsewhere).

Rebuilt by `python Tools/_WF/Xenonids/stage_prototypes.py` from RMC-14 `7fca80389a`. Do not edit the files here for gameplay changes you want to keep; edit them after they have moved.

## Staged files (95)

- `Access/`
  - `Access/xeno_access.yml`
- `Actions/`
  - `Actions/Xeno/xeno_action_base.yml`
  - `Actions/Xeno/xeno_construction_actions.yml`
  - `Actions/Xeno/xeno_evolve_actions.yml`
  - `Actions/Xeno/xeno_offense_actions.yml`
  - `Actions/Xeno/xeno_self_actions.yml`
  - `Actions/Xeno/xeno_utility_actions.yml`
- `Alerts/`
  - `Alerts/Xeno/xeno_armor_alerts.yml`
  - `Alerts/Xeno/xeno_energy_alerts.yml`
  - `Alerts/Xeno/xeno_health_alerts.yml`
  - `Alerts/Xeno/xeno_night_vision_alerts.yml`
  - `Alerts/Xeno/xeno_plasma_alerts.yml`
- `Audio/`
  - `Audio/xeno_sound_collections.yml`
- `Body/`
  - `Body/Organs/xeno.yml`
  - `Body/Parts/xeno.yml`
  - `Body/Prototypes/xeno.yml`
- `Damage/`
  - `Damage/xeno_damage_containers.yml`
  - `Damage/xeno_modifier_sets.yml`
- `Designer/`
  - `Designer/design_nodes.yml`
- `Effects/`
  - `Effects/xeno_acid.yml`
  - `Effects/xeno_acid_blast.yml`
  - `Effects/xeno_stomp.yml`
  - `Effects/xeno_structures.yml`
  - `Effects/xeno_tail.yml`
- `Entities/`
  - `Entities/Mobs/Xeno/base_xeno.yml`
  - `Entities/Mobs/Xeno/boiler.yml`
  - `Entities/Mobs/Xeno/burrower.yml`
  - `Entities/Mobs/Xeno/carrier.yml`
  - `Entities/Mobs/Xeno/crusher.yml`
  - `Entities/Mobs/Xeno/defender.yml`
  - `Entities/Mobs/Xeno/drone.yml`
  - `Entities/Mobs/Xeno/hive.yml`
  - `Entities/Mobs/Xeno/hivelord.yml`
  - `Entities/Mobs/Xeno/king.yml`
  - `Entities/Mobs/Xeno/larva.yml`
  - `Entities/Mobs/Xeno/lesser_drone.yml`
  - `Entities/Mobs/Xeno/lurker.yml`
  - `Entities/Mobs/Xeno/parasite.yml`
  - `Entities/Mobs/Xeno/praetorian.yml`
  - `Entities/Mobs/Xeno/queen.yml`
  - `Entities/Mobs/Xeno/ravager.yml`
  - `Entities/Mobs/Xeno/runner.yml`
  - `Entities/Mobs/Xeno/sentinel.yml`
  - `Entities/Mobs/Xeno/spitter.yml`
  - `Entities/Mobs/Xeno/warrior.yml`
  - `Entities/Objects/Weapons/Projectiles/xeno_spike.yml`
  - `Entities/Objects/Xeno/xeno_impacts.yml`
  - `Entities/Objects/Xeno/xeno_projectiles.yml`
  - `Entities/Objects/Xeno/xeno_smoke.yml`
  - `Entities/Structures/Xeno/Hive/xeno_acid_pillar.yml`
  - `Entities/Structures/Xeno/Hive/xeno_base_hive.yml`
  - `Entities/Structures/Xeno/Hive/xeno_egg_morpher.yml`
  - `Entities/Structures/Xeno/Hive/xeno_hive_cluster.yml`
  - `Entities/Structures/Xeno/Hive/xeno_hive_core.yml`
  - `Entities/Structures/Xeno/Hive/xeno_hive_plasma.yml`
  - `Entities/Structures/Xeno/Hive/xeno_hive_recovery.yml`
  - `Entities/Structures/Xeno/xeno_deployed_traps.yml`
  - `Entities/Structures/Xeno/xeno_doors.yml`
  - `Entities/Structures/Xeno/xeno_egg.yml`
  - `Entities/Structures/Xeno/xeno_floor_resin.yml`
  - `Entities/Structures/Xeno/xeno_fruit.yml`
  - `Entities/Structures/Xeno/xeno_nest.yml`
  - `Entities/Structures/Xeno/xeno_resin_hole.yml`
  - `Entities/Structures/Xeno/xeno_tunnel.yml`
  - `Entities/Structures/Xeno/xeno_walls.yml`
  - `Entities/Structures/Xeno/xeno_weeds.yml`
- `Roles/`
  - `Roles/Jobs/JobIcons/xeno_job_icons.yml`
  - `Roles/Xeno/Departments/xeno_department.yml`
  - `Roles/Xeno/Departments/xeno_job_groups.yml`
  - `Roles/Xeno/boiler.yml`
  - `Roles/Xeno/burrower.yml`
  - `Roles/Xeno/carrier.yml`
  - `Roles/Xeno/crusher.yml`
  - `Roles/Xeno/defender.yml`
  - `Roles/Xeno/drone.yml`
  - `Roles/Xeno/hivelord.yml`
  - `Roles/Xeno/king.yml`
  - `Roles/Xeno/larva.yml`
  - `Roles/Xeno/lesser_drone.yml`
  - `Roles/Xeno/lurker.yml`
  - `Roles/Xeno/parasite.yml`
  - `Roles/Xeno/praetorian.yml`
  - `Roles/Xeno/queen.yml`
  - `Roles/Xeno/ravager.yml`
  - `Roles/Xeno/runner.yml`
  - `Roles/Xeno/selectable.yml`
  - `Roles/Xeno/sentinel.yml`
  - `Roles/Xeno/spitter.yml`
  - `Roles/Xeno/warrior.yml`
- `SoundCollections/`
  - `SoundCollections/xeno_speech.yml`
  - `SoundCollections/xeno_structures.yml`
- `Trapper/`
  - `Trapper/acid_explosion.yml`
- `Voice/`
  - `Voice/Xenonid/xeno_emote_sounds.yml`
  - `Voice/Xenonid/xeno_emotes.yml`
  - `Voice/Xenonid/xeno_speech_sounds.yml`

## Dependencies (62 blocks in `Prototypes/deps/<kind>.yml`)

Prototypes the staged files reference that Wolfgate and the staged set do not define, copied from RMC-14 (path-rewritten). Each block is preceded by a `# RMC-14:` comment naming its source file.

### Tag (3)

- `CMRollerBed` from `Resources/Prototypes/_RMC14/Entities/Structures/Furniture/rollerbeds.yml`
- `RMCSynthGrabbable` from `Resources/Prototypes/_RMC14/rmc_tags.yml`
- `RMCXenoLarva` from `Resources/Prototypes/_RMC14/rmc_tags.yml`

### alert (5)

- `AreaInfo` from `Resources/Prototypes/_RMC14/Alerts/alerts.yml` (cut list: the reference is removed when the file moves in)
- `HiveCoreTracker` from `Resources/Prototypes/_RMC14/Alerts/alerts.yml`
- `HiveTracker` from `Resources/Prototypes/_RMC14/Alerts/alerts.yml`
- `LeaderTracker` from `Resources/Prototypes/_RMC14/Alerts/alerts.yml`
- `RMCUnconscious` from `Resources/Prototypes/_RMC14/Alerts/alerts.yml`

### alertCategory (7)

- `AreaInfo` from `Resources/Prototypes/Alerts/categories.yml` (cut list: the reference is removed when the file moves in)
- `Armor` from `Resources/Prototypes/Alerts/categories.yml`
- `Energy` from `Resources/Prototypes/Alerts/categories.yml`
- `HiveTracker` from `Resources/Prototypes/_RMC14/Alerts/alerts.yml`
- `Plasma` from `Resources/Prototypes/Alerts/categories.yml`
- `Sight` from `Resources/Prototypes/Alerts/categories.yml`
- `Tracker` from `Resources/Prototypes/Alerts/categories.yml`

### damageContainer (1)

- `StructuralMarine` from `Resources/Prototypes/_RMC14/Damage/marine_damage_containers.yml`

### decal (4)

- `CMDecalBase` from `Resources/Prototypes/_RMC14/Decals/cm_decal_base.yml`
- `RMCDecalBloodBase` from `Resources/Prototypes/_RMC14/Decals/blood.yml`
- `RMCDecalGibSplatterDrone` from `Resources/Prototypes/_RMC14/Decals/gib_splatters.yml`
- `RMCDecalGibSplatterLesserDrone` from `Resources/Prototypes/_RMC14/Decals/gib_splatters.yml`

### department (1)

- `CMDepartmentBase` from `Resources/Prototypes/_RMC14/Roles/cm_jobs_base.yml`

### entity (21)

- `CMActionGhost` from `Resources/Prototypes/_RMC14/Actions/cm_mob_state_actions.yml`
- `CMBaseWallXeno` from `Resources/Prototypes/_RMC14/Entities/Structures/Base/base_structure.yml`
- `CMBaseXenoStructure` from `Resources/Prototypes/_RMC14/Entities/Structures/Base/base_structure.yml`
- `Dazed` from `Resources/Prototypes/_RMC14/StatusEffects/status_effects.yml`
- `RMCActionOpenTacticalMap` from `Resources/Prototypes/_RMC14/Actions/tactical_map_actions.yml` (cut list: the reference is removed when the file moves in)
- `RMCActionOpenTacticalMapXeno` from `Resources/Prototypes/_RMC14/Actions/tactical_map_actions.yml` (cut list: the reference is removed when the file moves in)
- `RMCBaseEffect` from `Resources/Prototypes/_RMC14/Effects/base.yml`
- `RMCBaseGibEffect` from `Resources/Prototypes/_RMC14/Effects/gib.yml`
- `RMCDecalSpawnerGibsBase` from `Resources/Prototypes/_RMC14/Entities/Markers/Spawners/Random/Decals/gibs.yml`
- `RMCDecalSpawnerGibsDrone` from `Resources/Prototypes/_RMC14/Entities/Markers/Spawners/Random/Decals/gibs.yml`
- `RMCDecalSpawnerGibsLesserDrone` from `Resources/Prototypes/_RMC14/Entities/Markers/Spawners/Random/Decals/gibs.yml`
- `RMCEffectExtraSlash` from `Resources/Prototypes/_RMC14/Effects/slash.yml`
- `RMCEffectSlam` from `Resources/Prototypes/_RMC14/Effects/slam.yml`
- `RMCEffectTailHit` from `Resources/Prototypes/_RMC14/Effects/tail.yml`
- `RMCEffectXenoTelegraphGreen` from `Resources/Prototypes/_RMC14/Effects/green.yml`
- `RMCEffectXenoTelegraphRedPulse` from `Resources/Prototypes/_RMC14/Effects/green.yml`
- `RMCGibEffectXenoLesserDrone` from `Resources/Prototypes/_RMC14/Effects/gib.yml`
- `RMCGibEffectXenoSmall` from `Resources/Prototypes/_RMC14/Effects/gib.yml`
- `RMCPointingArrowBig` from `Resources/Prototypes/_RMC14/Effects/rmc_pointing.yml`
- `RMCPointingArrowBigQueen` from `Resources/Prototypes/_RMC14/Effects/rmc_pointing.yml`
- `RMCWeaponArcXenoClaw` from `Resources/Prototypes/_RMC14/Effects/slash.yml`

### explosion (1)

- `RMCOBXenoTunnel` from `Resources/Prototypes/_RMC14/explosion.yml`

### htnCompound (3)

- `RMCParasiteActiveCompound` from `Resources/Prototypes/_RMC14/NPC/parasite.yml`
- `RMCParasiteInfect` from `Resources/Prototypes/_RMC14/NPC/parasite.yml`
- `RMCParasiteLeap` from `Resources/Prototypes/_RMC14/NPC/parasite.yml`

### job (2)

- `CMJobBase` from `Resources/Prototypes/_RMC14/Roles/cm_jobs_base.yml`
- `CMJobXenoBase` from `Resources/Prototypes/_RMC14/Roles/cm_jobs_base.yml`

### jobIcon (2)

- `CMJobIconBase` from `Resources/Prototypes/_RMC14/Roles/Jobs/JobIcons/base_job_icon.yml`
- `CMJobIconEmpty` from `Resources/Prototypes/_RMC14/Roles/Jobs/JobIcons/empty_job_icon.yml`

### npcFaction (1)

- `RMCXeno` from `Resources/Prototypes/_RMC14/Factions/rmc_factions.yml`

### radioChannel (1)

- `Hivemind` from `Resources/Prototypes/_RMC14/radio_channels.yml`

### reagent (2)

- `CMReagent` from `Resources/Prototypes/_RMC14/Reagents/base_reagent.yml`
- `RMCMolecularAcid` from `Resources/Prototypes/_RMC14/Reagents/toxins.yml`

### soundCollection (2)

- `RCMXenoClaw` from `Resources/Prototypes/_RMC14/SoundCollections/alienclaw.yml`
- `RMCExplosionBig` from `Resources/Prototypes/_RMC14/Audio/explosion_sound_collections.yml`

### statusEffect (3)

- `Blinded` from `Resources/Prototypes/_RMC14/StatusEffects/status_effects.yml`
- `Deaf` from `Resources/Prototypes/_RMC14/StatusEffects/status_effects.yml`
- `Unconscious` from `Resources/Prototypes/_RMC14/StatusEffects/status_effects.yml`

### trackerMode (3)

- `HiveCore` from `Resources/Prototypes/_RMC14/Trackers/Trackers.yml`
- `HiveLeader` from `Resources/Prototypes/_RMC14/Trackers/Trackers.yml`
- `Queen` from `Resources/Prototypes/_RMC14/Trackers/Trackers.yml`

## Unresolved ids

None.

## Referenced but not copied

Left out on purpose; a dependency block that names them needs the reference cut or the prototype ported when its file moves in.

- Guidebook entries (24), named by `GuideHelp` components of the staged mobs and structures; their pages are RMC's `Resources/ServerInfo/Guidebook/_RMC14/Guides/Xenonid`, not imported here: `RMCGuideHiveStructures`, `RMCGuideResinHoles`, `RMCGuideRoleBoiler`, `RMCGuideRoleBurrower`, `RMCGuideRoleCarrier`, `RMCGuideRoleCrusher`, `RMCGuideRoleDefender`, `RMCGuideRoleDesigner`, `RMCGuideRoleDrone`, `RMCGuideRoleEggsac`, `RMCGuideRoleHivelord`, `RMCGuideRoleLurker`, `RMCGuideRoleOppressor`, `RMCGuideRoleParasite`, `RMCGuideRolePraetorian`, `RMCGuideRoleQueen`, `RMCGuideRoleRavager`, `RMCGuideRoleRunner`, `RMCGuideRoleSentinel`, `RMCGuideRoleSpitter`, `RMCGuideRoleValkyrie`, `RMCGuideRoleVampire`, `RMCGuideRoleVanguard`, `RMCGuideRoleWarrior`
- Entities named by values inside dependency blocks (9). Dependency blocks are followed through `parent` and the id fields only, so RMC's building, weapon and medal trees stay out: `CMRollerBed`, `RMCMedalAmethystService`, `RMCMedalBronzeService`, `RMCMedalEmeraldService`, `RMCMedalGoldService`, `RMCMedalPlatinumService`, `RMCMedalPrismaticService`, `RMCMedalRubyService`, `RMCMedalSilverService`

## Ids the staged set defines that Wolfgate already defines

None.

