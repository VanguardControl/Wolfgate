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
wg-zone-wilderness-summary = { $open ->
    [yes] Anyone may attack anyone here without escalation. Stay and you are marked.
   *[no] TSF and PDV may fight here without escalation; everyone else still needs it. Stay and you are marked.
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
} { $open ->
    [yes] Anyone
   *[no] TSF and PDV
} may engage { $ship ->
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
} { $open ->
    [yes] Anyone
   *[no] TSF and PDV
} may engage { $ship ->
    [yes] it
   *[no] you
} without escalation, anywhere, Safe Zone included, until the mark runs out.
wg-zone-skull-attacked = { $ship ->
    [yes] Your ship is now MARKED
   *[no] You are now MARKED
} for attacking a ship in a Wilderness from outside it. { $open ->
    [yes] Anyone
   *[no] TSF and PDV
} may engage { $ship ->
    [yes] it
   *[no] you
} without escalation, anywhere, Safe Zone included, for {$minutes} minutes.

## Banner and titles

wg-zone-banner-marked = { $ship ->
    [yes] YOUR SHIP IS MARKED
   *[no] YOU ARE MARKED
}: {$time} left. No protection anywhere, Safe Zone included.
wg-zone-banner-marked-held = { $ship ->
    [yes] YOUR SHIP IS MARKED
   *[no] YOU ARE MARKED
} while you stay here, and for {$minutes} minutes after you leave.
wg-zone-banner-marking = { $ship ->
    [yes] Your ship is marked
   *[no] You are marked
} in {$seconds} s. Leave now to avoid it.
wg-zone-banner-overridden = Your mark overrides this Safe Zone: you have no protection here.
wg-zone-banner-hint = Click for the rules.
wg-zone-pill-marked = MARKED {$time}
wg-zone-pill-marked-held = MARKED
wg-zone-pill-marking = Marked in {$seconds} s
wg-zone-banner-title = {$title}  ·  {$place}
wg-zone-title-entering = ENTERING {$title}
wg-zone-title-leaving = LEAVING {$title}
wg-zone-title-marked = MARKED
wg-zone-title-marked-sub = No protection anywhere until the mark runs out.

## Radar

wg-zone-radar-marked = MARKED

## Alerts

wg-alert-zone-safe-name = Safe Zone
wg-alert-zone-safe-desc = Policy protects you here: no theft, no unprovoked attacks. Cause trouble and you lose that protection.
wg-alert-zone-outpost-name = Faction outpost
wg-alert-zone-outpost-desc = Within 256 m of a faction outpost: protected from port strikes while the war is COLD, open to them while it is HOT.
wg-alert-zone-wilderness-name = Wilderness
wg-alert-zone-wilderness-desc = No policy protection. Who may fight here without escalation is in the Wilderness rules: click the zone banner. Stay and you, or your ship, are marked; the sweep over this icon counts down to it.
wg-alert-zone-notsafe-name = Not a Safe Zone
wg-alert-zone-notsafe-desc = This station is not a Safe Zone. Ordinary rules and escalation apply.
wg-alert-marked-name = Marked
wg-alert-marked-desc = You, or your ship, are marked: for staying in a Wilderness, attacking into one from outside, or sharing a pilot or ship that is. Until the mark runs out you have no protection anywhere, Safe Zone included. Others see the skull on radar and over a marked person. It holds while you are in a Wilderness; once out, the sweep over this icon counts down what is left.

## Guidebook

guide-entry-wg-wilderness = Zones and the Wilderness
wg-wilderness-rule-who = { $open ->
    [yes] -- [bold]Anyone may fight anyone[/bold] in a Wilderness without escalation.
   *[no] -- [bold]TSF and PDV may fight each other[/bold] in a Wilderness without escalation, even while COLD. Everyone else still needs escalation.
}
wg-wilderness-rule-mark = { $onfoot ->
    [yes] -- A ship, or a person not aboard one,
   *[no] -- A ship
} that stays [bold]{$seconds} seconds[/bold] in a Wilderness is [color=#d93c3c][bold]marked[/bold][/color]: a skull on every radar, and over a marked person's head. The mark holds while it stays and runs out [bold]{$minutes} minutes[/bold] after it leaves. Those who may fight in a Wilderness may engage a marked ship or person [bold]anywhere, Safe Zone included[/bold], until then.
wg-wilderness-rule-pilot = -- A ship and the people aboard carry their own marks. A marked pilot marks the ship they fly, and a marked ship marks whoever takes its helm, with the time the mark has left.
wg-wilderness-rule-attack = -- Damaging a ship in a Wilderness from outside one marks you, and the ship you fire from, for [bold]{$minutes} minutes[/bold].

## Options

ui-options-wg-zones = Zones
ui-options-wg-wilderness-border = Coloured screen border in a Wilderness and while marked
ui-options-wg-wilderness-rings = Zone edges on radars

## Admin command

cmd-wg_zone-desc = Shows your zone and the Wilderness marks on you and your ship, or sets or clears either.
cmd-wg_zone-help = Usage: wg_zone [info|mark|clear] [ship]
cmd-wg_zone-hint = info, mark or clear
cmd-wg_zone-ship-hint = ship, for the ship you're aboard; nothing, for you
cmd-wg_zone-no-body = You need a body.
cmd-wg_zone-no-ship = You aren't aboard a ship.
cmd-wg_zone-held = held (in a Wilderness)
cmd-wg_zone-ship = {$ship}, mark: {$mark}
cmd-wg_zone-info = Zone: {$zone}. Your mark: {$mark}. Ship: {$ship}.
cmd-wg_zone-marked = Marked {$carrier}.
cmd-wg_zone-cleared = Cleared the mark on {$carrier}.
