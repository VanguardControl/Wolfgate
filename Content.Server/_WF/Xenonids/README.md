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
