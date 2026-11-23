## Zones

wg-zone-safe-title = SAFE ZONE
wg-zone-safe-summary = Policy protects you here: no theft, no unprovoked attacks. Cause trouble and you lose that protection.
wg-zone-safe-entered = You entered the {$place} Safe Zone. {$summary}
wg-zone-safe-left = You left the {$place} Safe Zone. Ordinary escalation rules apply.
wg-zone-safe-ring = SAFE ZONE

wg-zone-outpost-title = PROTECTED OUTPOST
wg-zone-outpost-summary = Ceasefire: this outpost, and ships within 256 m of it, may not be struck while the war is COLD.
wg-zone-outpost-hot-title = OUTPOST AT WAR
wg-zone-outpost-hot-summary = The war is HOT: this outpost, and ships within 256 m of it, may be struck.
wg-zone-outpost-entered = You are within 256 m of {$place}. {$summary}
wg-zone-outpost-left = You left the outpost zone of {$place}.
wg-zone-outpost-ring = OUTPOST

wg-zone-wilderness-title = WILDERNESS: NO PROTECTION
wg-zone-wilderness-summary = A PvP zone: anyone may attack anyone here without escalation. { $onfoot ->
    [yes] Stay and you are marked.
   *[no] Ships that stay are marked.
}
wg-zone-wilderness-entered = You entered a WILDERNESS round {$place}. {$summary}
wg-zone-wilderness-left = You left the Wilderness round {$place}.
wg-zone-wilderness-ring = WILDERNESS

wg-zone-notsafe-title = NOT A SAFE ZONE
wg-zone-notsafe-summary = This station is not a Safe Zone. Ordinary rules and escalation apply.
wg-zone-notsafe-mobile-summary = A mobile base: not protected from port strikes, though the ceasefire still holds while the war is COLD.
wg-zone-notsafe-entered = You are aboard {$place}. {$summary}
wg-zone-notsafe-left = You left {$place}.
wg-zone-notsafe-ring = NOT SAFE

wg-zone-chat-wrap = [color={$color}][bold]{$message}[/bold][/color]

## The Wilderness mark

wg-zone-skull-marked = { $ship ->
    [yes] Your ship is now MARKED.
   *[no] You are now MARKED.
} Anyone may engage { $ship ->
    [yes] it
   *[no] you
} without escalation, anywhere, Safe Zone included, until the mark runs out, {$minutes} minutes after you leave the Wilderness.
wg-zone-skull-leaving = You left the Wilderness. { $ship ->
    [yes] Your ship stays
   *[no] You stay
} MARKED for {$minutes} minutes: no protection anywhere, Safe Zone included.
wg-zone-skull-expired = { $ship ->
    [yes] Your ship's mark has run out. Ordinary rules protect it again.
   *[no] Your mark has run out. Ordinary rules protect you again.
}
wg-zone-skull-inherited = { $ship ->
    [yes] Your ship is now MARKED: its pilot is.
   *[no] You took the helm of a MARKED ship, and are MARKED with it.
} Anyone may engage { $ship ->
    [yes] it
   *[no] you
} without escalation, anywhere, Safe Zone included, until the mark runs out.
wg-zone-skull-attacked = { $ship ->
    [yes] Your ship is now MARKED
   *[no] You are now MARKED
} for attacking a ship in a Wilderness from outside it. Anyone may engage { $ship ->
    [yes] it
   *[no] you
} without escalation, anywhere, Safe Zone included, for {$minutes} minutes.
wg-zone-skull-staff-marked = Staff have MARKED { $ship ->
    [yes] your ship
   *[no] you
}. Anyone may engage { $ship ->
    [yes] it
   *[no] you
} without escalation, anywhere, Safe Zone included, until the mark runs out, {$minutes} minutes after you leave the Wilderness.
wg-zone-skull-staff-cleared = Staff have cleared { $ship ->
    [yes] your ship's mark. Ordinary rules protect it again.
   *[no] your mark. Ordinary rules protect you again.
}
wg-zone-skull-staff-standing = Staff have given { $ship ->
    [yes] your ship
   *[no] you
} a STANDING MARK. Anyone may engage { $ship ->
    [yes] it
   *[no] you
} without escalation, anywhere, Safe Zone included. It does not run out: it lasts until staff clear it, or until { $ship ->
    [yes] the ship is destroyed.
   *[no] you die.
}
wg-zone-skull-staff-ended = { $ship ->
    [yes] Your ship's standing mark has ended: it is no longer a ship. Ordinary rules protect it again.
   *[no] Your standing mark ended with your death. Ordinary rules protect you again.
}

## Banner and titles

wg-zone-banner-marked = { $ship ->
    [yes] YOUR SHIP IS MARKED
   *[no] YOU ARE MARKED
}: {$time} left. No protection anywhere, Safe Zone included.
wg-zone-banner-marked-held = { $ship ->
    [yes] YOUR SHIP IS MARKED
   *[no] YOU ARE MARKED
} while you stay here, and for {$minutes} minutes after you leave.
wg-zone-banner-marked-standing = { $ship ->
    [yes] YOUR SHIP HAS A STANDING MARK
   *[no] YOU HAVE A STANDING MARK
} from staff: no protection anywhere, Safe Zone included, until staff clear it or { $ship ->
    [yes] the ship is destroyed.
   *[no] you die.
}
wg-zone-banner-marked-permanent = YOU ARE MARKED FOR LIFE: no protection anywhere, Safe Zone included. It never runs out.
wg-zone-banner-marking = { $ship ->
    [yes] Your ship is marked
   *[no] You are marked
} in {$seconds} s. Leave now to avoid it.
wg-zone-banner-overridden = Your mark overrides this Safe Zone: you have no protection here.
wg-zone-banner-hint = Click for the rules.
wg-zone-pill-marked = MARKED {$time}
wg-zone-pill-marked-held = MARKED
wg-zone-pill-marked-standing = MARKED BY STAFF
wg-zone-pill-marked-permanent = MARKED FOR LIFE
wg-zone-pill-marking = Marked in {$seconds} s
wg-zone-banner-title = {$title}  ·  {$place}
wg-zone-title-entering = ENTERING {$title}
wg-zone-title-leaving = LEAVING {$title}
wg-zone-title-marked = MARKED
wg-zone-title-marked-sub = No protection anywhere until the mark runs out.
wg-zone-title-marked-standing-sub = A standing mark from staff: no protection anywhere until they clear it.
wg-zone-title-marked-permanent-sub = Marked for life: no protection anywhere, ever.

## Radar

wg-zone-radar-marked = MARKED

## Alerts

wg-alert-zone-safe-name = Safe Zone
wg-alert-zone-safe-desc = Policy protects you here: no theft, no unprovoked attacks. Cause trouble and you lose that protection.
wg-alert-zone-outpost-name = Faction outpost
wg-alert-zone-outpost-desc = Within 256 m of a faction outpost: protected from port strikes while the war is COLD, open to them while it is HOT.
wg-alert-zone-wilderness-name = Wilderness
wg-alert-zone-wilderness-desc = A PvP zone with no policy protection: anyone may attack anyone here without escalation. Staying marks a ship, and on some servers a person on foot; while it is counting, the sweep over this icon shows when.
wg-alert-zone-notsafe-name = Not a Safe Zone
wg-alert-zone-notsafe-desc = This station is not a Safe Zone. Ordinary rules and escalation apply.
wg-alert-marked-name = Marked
wg-alert-marked-desc = You, or your ship, are marked: for staying in a Wilderness, attacking into one from outside, sharing a pilot or ship that is, by staff, or for life by the Marked for Life trait. A standing mark from staff never runs out: it lasts until staff clear it, a marked person dies or a marked ship is destroyed. Until the mark runs out you have no protection anywhere, Safe Zone included. Others see the skull on radar and over a marked person. It holds while you are in a Wilderness; once out, the sweep over this icon counts down what is left.

## Guidebook

guide-entry-wg-wilderness = Zones and the Wilderness
wg-wilderness-rule-who = -- A Wilderness is a [color=#d93c3c][bold]PvP zone for everyone[/bold][/color]: anyone may fight anyone there without escalation, whatever their faction, even while COLD.
wg-wilderness-rule-mark = { $onfoot ->
    [yes] -- A ship, or a person not aboard one,
   *[no] -- A ship
} that stays [bold]{$seconds} seconds[/bold] in a Wilderness is [color=#d93c3c][bold]marked[/bold][/color]: a skull on every radar, and over a marked person's head. The mark holds while it stays and runs out [bold]{$minutes} minutes[/bold] after it leaves. Anyone may engage a marked ship or person [bold]anywhere, Safe Zone included[/bold], until then.
wg-wilderness-rule-pilot = -- A ship and the people aboard carry their own marks. A marked pilot marks the ship they fly, and a marked ship marks whoever takes its helm, with the time the mark has left.
wg-wilderness-rule-attack = -- Damaging a ship in a Wilderness from outside one marks you, and the ship you fire from, for [bold]{$minutes} minutes[/bold].
wg-wilderness-rule-permanent = -- A character with the [bold]Marked for Life[/bold] trait carries the mark always: it never runs out and outlasts death. A ship they fly is marked while they fly it.
wg-wilderness-rule-standing = -- Staff can give a ship or person a [color=#d93c3c][bold]standing mark[/bold][/color]. It never runs out: it lasts until staff clear it, the person dies or the ship is destroyed. A pilot flying a ship with one, or the ship a pilot with one flies, is marked for as long as they fly together.

## Trait

trait-wg-marked-for-life-name = Marked for Life
trait-wg-marked-for-life-desc = You carry the Wilderness mark always, from the moment you arrive: a skull over your head and on every radar, and no protection anywhere, Safe Zone included. Anyone may engage you without escalation wherever you are. It never runs out and outlasts death, and a ship you fly is marked while you fly it. Only staff can clear it.

## Options

ui-options-wg-zones = Zones
ui-options-wg-wilderness-banner = Zone banner
ui-options-wg-wilderness-titles = Title on crossing a zone edge
ui-options-wg-wilderness-sounds = Zone sounds
ui-options-wg-wilderness-border = Coloured screen border
ui-options-wg-wilderness-always = In every zone
ui-options-wg-wilderness-danger = Only where you can be attacked
ui-options-wg-wilderness-danger-tooltip = Where you can be attacked: in a Wilderness, near an outpost at war, or anywhere while you are marked.
ui-options-wg-wilderness-off = Off
ui-options-wg-wilderness-border-wilderness = In a Wilderness and while marked
ui-options-wg-wilderness-border-marked = Only while marked
ui-options-wg-wilderness-chat = Chat line on entering or leaving a zone
ui-options-wg-wilderness-rings = Zone edges on radars

## Admin verbs

wg-zone-verb-mark = Wilderness mark
wg-zone-verb-mark-description = Mark {$carrier} now, as if they had stayed in a Wilderness. They are told.
wg-zone-verb-clear = Clear Wilderness mark
wg-zone-verb-clear-description = Clear the mark on {$carrier}, and on the ship they fly. They are told.
wg-zone-verb-mark-ship = Wilderness mark ship
wg-zone-verb-mark-ship-description = Mark the ship {$carrier} now, as if it had stayed in a Wilderness. Those aboard are told.
wg-zone-verb-standing = Standing Wilderness mark
wg-zone-verb-standing-description = Give {$carrier} a mark that never runs out: it lasts until staff clear it or they die. They are told.
wg-zone-verb-standing-ship = Standing Wilderness mark on ship
wg-zone-verb-standing-ship-description = Give the ship {$carrier} a mark that never runs out: it lasts until staff clear it or the ship is destroyed. Those aboard are told.
wg-zone-verb-clear-ship = Clear ship's Wilderness mark
wg-zone-verb-clear-ship-description = Clear the mark on the ship {$carrier}, and on whoever is flying it. Those aboard are told.

## Admin command

cmd-wg_zone-desc = Shows a person's zone and the Wilderness marks on them and their ship, or sets or clears either.
cmd-wg_zone-help = Usage: wg_zone [info|mark|standing|clear] [ship] [player or entity id]. With no target, you; with ship, the ship the target is aboard. A standing mark never runs out: it lasts until cleared, death or the ship's destruction. Clearing a pilot or ship clears the other too, unless it has a standing mark.
cmd-wg_zone-hint = info, mark, standing or clear
cmd-wg_zone-target-hint = a player or entity id, or nothing for you; ship, for the ship they're aboard
cmd-wg_zone-no-body = You need a body, or name a target.
cmd-wg_zone-player-no-body = {$player} has no body.
cmd-wg_zone-no-target = No player or entity {$target}.
cmd-wg_zone-cannot-carry = {$target} can't carry a mark: only ships, vehicles and living people can.
cmd-wg_zone-no-ship = They aren't aboard a ship.
cmd-wg_zone-held = held (in a Wilderness)
cmd-wg_zone-ship = {$ship}, mark: {$mark}
cmd-wg_zone-info = {$target}. Zone: {$zone}. Mark: {$mark}. Ship: {$ship}.
cmd-wg_zone-marked = Marked {$carrier}.
cmd-wg_zone-cleared = Cleared the mark on {$carrier}.
cmd-wg_zone-standing = Gave {$carrier} a standing mark.
cmd-wg_zone-standing-state = standing (until cleared)
cmd-wg_zone-permanent-state = for life (Marked for Life trait)
cmd-wg_zone-permanent = {$carrier} is already marked for life by their trait; clear it first to set another mark.
cmd-wg_zone-kept = {$kept} keeps its standing mark, so {$carrier} is marked again while they fly together.
