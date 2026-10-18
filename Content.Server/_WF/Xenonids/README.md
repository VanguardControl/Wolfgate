# Xenonids

RMC-14's xenomorphs (`7fca80389a`), ported onto the RMCCore module. Xenos are player mobs of the `CMXeno*` and
`RMCXeno*` prototypes: they belong to a hive, spend plasma on abilities, heal on weeds, rest, evolve through
castes, build in resin and talk to their hive with the radio prefix (`;` or `:`), which only hive members and ghosts
hear. They keep Wolfgate's `Xeno` NPC faction and night vision. The spawn menu lists them under Xenonids.

Becoming a xeno: a ghost takes a burrowed larva from a hive with the Join Xeno action, and admins add larva with
`xeno_larva <count>`. Admin-spawning any caste puts it in the default hive, which `WFXenoHiveBootstrapSystem` creates
on demand. Unplayed xenos carry `GhostTakeoverAvailable`, and hive cores and pylons offer lesser drone ghost roles.

Roster: larva, drone (gardener, healer), lesser drone, parasite (watcher), runner (acider), lurker (vampire), warrior,
ravager (berserker, hedgehog), praetorian (valkyrie, dancer, vanguard, oppressor), defender (steelcrest), crusher
(charger), boiler (sapper), spitter, sentinel, burrower, hivelord (resin whisperer, designer), carrier (eggsac), queen
and king. Tiers unlock over round time and evolve with points; the queen and king command the hive.

The loop: drones plant weeds and order resin structures (walls, doors, nests, tunnels, hive cores, pylons, clusters,
fruit), queens and carriers lay eggs, a facehugger infects a host, the incubated larva bursts out and joins the hive.
Entry points: `XenoSystem`, `SharedXenoHiveSystem`, `XenoEvolutionSystem`, `SharedXenoParasiteSystem`,
`SharedXenoWeedsSystem`, `XenoConstruction*`, `XenoEggSystem`, `XenoHiveChatSystem` and the client `XenoHudOverlay`.

Cut from RMC: marines, tactical maps, intel, dropship hijack, areas, RMC fire, medical and surgery (an infection ends
in a burst or the host's death), language, the parasite and other xeno NPC AI, guidebook pages, figurines, RMC's
night vision and the ghost xeno HUD toggle. Each cut is a `WOLFGATE:` comment where the code or prototype changed.
Port notes and API differences are in `Docs/_WF/Xenonids/PORT_BRIEF.md`.
