# RMCCore

RMC-14's combat infrastructure, ported as the base the Xenonids port (and later RMC ports) builds on: action helpers
with RMC's names over Wolfgate's actions API, size-based stuns and daze, slows and roots, RMC pulling, damage over time
and obstacle slamming, `CMArmor`, RMC melee rules, emotes, map and line helpers, camera shake, auras, dialogs, sprite
draw depth, on-collide damage, given hands and pickup whitelists, gibbing, deafness, invisibility, stamina, evasion,
mob collision, explosion and projectile extras, xeno shields, `FixedIdentity`, vent crawling and RMC's tether visuals.

Nothing here spawns on its own; it does something once a prototype carries its components. RMC behaviour that hooks
generic components (self-attack, pulling after an attack, the grab lunge and facing) only applies to entities with
`RMCSize`, so Wolfgate's own mobs play as before. Marine, skill, squad, RMC fire, medical and language content is cut;
each cut is a `// WOLFGATE:` line where the code changed. Types the port needs from the Xenonids module are stubs under
`_WF/Xenonids` marked `XENOPORT-STUB`. Port notes and API differences are in `Docs/_WF/Xenonids/PORT_BRIEF.md`.
