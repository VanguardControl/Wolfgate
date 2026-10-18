# Xenonids

RMC-14's xenomorphs, ported from RMC-14 `7fca80389a` onto the RMCCore module. Xenos are player mobs of the `CMXeno*`
prototypes: they belong to a hive (`CMXenoHive`, `HiveMember`), spend plasma on abilities, regain plasma and health
on their hive's weeds, rest, watch hive members, track the queen and talk to their hive with the radio prefix (`;` or
`:`), which only hive members and ghosts hear. They keep Wolfgate's `Xeno` NPC faction and use Wolfgate's night vision.

Players become xenos by admin spawn or by taking over an unplayed xeno offered as a ghost role (xeno mobs carry
`GhostTakeoverAvailable`); burrowed larvae join a hive through `SharedXenoHiveSystem.JoinBurrowedLarva`. Larvae burst
from infected hosts once the parasite lane lands.

Entry points: `XenoSystem` (access, healing, host test `IsHostCandidate`), `SharedXenoHiveSystem` / `XenoHiveSystem`
(hives, queens, burrowed larvae, unlock announcements), `XenoHiveChatSystem` (hivemind, called from `ChatSystem`),
`XenoPlasmaSystem`, `SharedXenoWeedsSystem` / `XenoWeedsSystem` (weed spread and weed buffs), `XenoRestSystem`,
`SharedXenoWatchSystem`, `HiveTrackerSystem`, `XenoAnnounceSystem` and the `xenoannounce` command, and the client
`XenoVisualizerSystem`, `XenoHudOverlay` and `XenoWideSwingSystem` (the `CMXenoWideSwing` key, Space by default).

The port arrives in lanes (see `Docs/_WF/Xenonids/PORT_BRIEF.md`). This core lane brought the base xeno, the larva, the
hive, weeds, plasma, energy, rest, watch, zoom, charge and tail stab. Still to come: structures and resin
construction, eggs and parasites, evolution and the other castes, and the queen's command abilities. Types those lanes
own are stubs marked `XENOPORT-STUB`, and prototype parts that need them are commented out with `XENOPORT(<lane>)`.
Marines, tactical maps, intel, dropship hijack, areas, RMC fire, medical and language are cut; each cut is a
`WOLFGATE:` comment where the code or prototype changed.

Eggs, parasites, spit and acid (lifecycle lane): `XenoEggSystem` (eggs, ovipositor, egg retriever and fragile eggs),
`SharedXenoParasiteSystem` (facehugger attach, incubation and the larva burst; the concrete systems are
`Server/Leap/XenoParasiteSystem` and `Client/Infected/XenoParasiteSystem`, keep exactly one subclass of each),
`XenoProjectileSystem` and `XenoSpitSystem` (spit, acid stacks, bone chips), `SharedXenoAcidSystem` and
`XenoAcidHoleSystem` (corrosive acid on items and structures, acid holes in walls). A parasite is attached with
`SharedXenoParasiteSystem.Infect`; once its fall-off delay passes the host carries `VictimInfectedComponent`, and
when `BurstAt` passes a larva appears in the host and bursts out when it moves, or automatically a minute later. A host
that dies or rots with a larva inside bursts. RMC's surgery (`CMSurgeryTarget`, `RMCSurgeryXenoHeart`, larva removal
and autodoc) is cut, so an infection can only end in a burst or the host's death. The parasite AI (idle, active, trap
leaps, HTN) needs RMC's NPC module and is not ported: `ParasiteAI`, `TrapParasite` and their delay components are data
only, ghosts can still take over a parasite through its ghost verb. Cut with RMC fire and chemistry: extinguisher
vapor removing acid, the acid fire alert, grenade acid inheritance, gun second wind and flares burning faster under acid.
