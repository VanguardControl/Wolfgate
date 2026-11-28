## Server lines in the ahelp

wg-ahelp-ticket-closed-staff = [color=#7fd48a]{ $staff } closed { $number ->
    [0] the ticket
   *[other] ticket #{ $number }
} as { $state }.[/color]
wg-ahelp-ticket-closed-player-rejected = Staff have closed your ahelp: it isn't something staff can act on. If you think that's wrong, send another message with more detail.
wg-ahelp-ticket-closed-player-ic = Staff have closed your ahelp as an in-character matter. Sort it out in character; send another message if anything breaks the rules.
wg-ahelp-ticket-round-end-closer = Round end
wg-ahelp-ticket-stale = [color=#ffb347]{ $number ->
    [0] This ticket
   *[other] Ticket #{ $number }
} has waited { $minutes } min without a staff reply.[/color]
wg-ahelp-assist-line = [color=#ffb347]{ $staff } asked the other staff for help with this ticket.[/color]
wg-ahelp-assist-chat = { $staff } asks for help with { $number ->
    [0] a ticket
   *[other] ticket #{ $number }
} from { $player }.
wg-ahelp-assist-chat-wrapped = [color=#ffb347]{ $text }[/color] [ahelpticket={ $key }/]
wg-ahelp-loadout-none = [color=#ffb347]No loadout to give: the player needs a body, a job and a loadout picked for it.[/color]
wg-ahelp-loadout-broke = [color=#ffb347]The player can't afford their loadout ({ $price } credits), so nothing was given.[/color]
wg-ahelp-loadout-given = [color=#7fd48a]{ $staff } gave the player their loadout ({ $count } items){ $charged ->
    [0] {" "}for free
   *[other] , charging them { $charged } credits
}.[/color]

## Ticket panel

wg-ahelp-tickets-title = Admin Help Tickets
wg-ahelp-tickets-search-placeholder = Search names, #numbers, or press Enter to search what was said
wg-ahelp-tickets-filter-mine = Mine
wg-ahelp-tickets-filter-online = Online
wg-ahelp-tickets-filter-notes = Has notes
wg-ahelp-tickets-filter-closed = Show closed
wg-ahelp-tickets-group-unclaimed = Unclaimed
wg-ahelp-tickets-group-mine = Mine
wg-ahelp-tickets-group-claimed = Claimed by others
wg-ahelp-tickets-group-closed = Closed this round
wg-ahelp-tickets-empty = No tickets this round.
wg-ahelp-tickets-none-selected = Pick a ticket, or use the Message verb on a player to start one.
wg-ahelp-tickets-number = #{ $number }
wg-ahelp-tickets-number-pending = #…
wg-ahelp-tickets-age = { $minutes ->
    [0] now
   *[other] { $minutes }m
}
wg-ahelp-tickets-claim = Claim
wg-ahelp-tickets-unclaim = Unclaim
wg-ahelp-tickets-resolve = Resolve
wg-ahelp-tickets-reject = Reject
wg-ahelp-tickets-ic = IC issue
wg-ahelp-tickets-reopen = Reopen
wg-ahelp-tickets-macros = Macros
wg-ahelp-tickets-admin-only = Admin only
wg-ahelp-tickets-play-sound = Play sound
wg-ahelp-tickets-pop-out = Pop out
wg-ahelp-tickets-state-Open = Open
wg-ahelp-tickets-state-Claimed = Claimed
wg-ahelp-tickets-state-Resolved = Resolved
wg-ahelp-tickets-state-Rejected = Rejected
wg-ahelp-tickets-state-IcIssue = IC issue
wg-ahelp-tickets-state-ClosedRoundEnd = Closed at round end
wg-ahelp-tickets-header = { $character } ({ $username }) · { $state }{ $assignee ->
    [none] {""}
   *[other] {" "}· { $assignee }
}
wg-ahelp-tickets-collision = { $name } is also replying.
wg-ahelp-tickets-offline = offline
wg-ahelp-tickets-section-ticket = Ticket
wg-ahelp-tickets-section-player = Player
wg-ahelp-tickets-section-ships = Ships
wg-ahelp-tickets-claim-tooltip = Takes the ticket. The player sees that you are on it.
wg-ahelp-tickets-unclaim-tooltip = Puts the ticket back as unclaimed. The player sees it is waiting for staff again.
wg-ahelp-tickets-reopen-tooltip = Opens the closed ticket again and claims it. The player sees that you are on it.
wg-ahelp-tickets-close-tooltip = Closes the ticket. The player is told: "{ $message }"
wg-ahelp-tickets-assist = Request assistance
wg-ahelp-tickets-assist-tooltip = Pings the other staff online with a sound and a chat link to this ticket.
wg-ahelp-tickets-export = Export .txt
wg-ahelp-tickets-export-tooltip = Saves this conversation to a text file.
wg-ahelp-tickets-viewers = Also looking: { $names }
wg-ahelp-tickets-ship-tooltip = Open this ship in Shipmaster
wg-ahelp-export-header = Ticket #{ $number } · round { $round } · { $character } ({ $username }) · { $state }
wg-ahelp-assist-link = [Open ticket]
wg-ahelp-chat-placeholder = Type a message and press Enter
wg-ahelp-chat-send = Send

## Player card

wg-ahelp-card-playtime = Playtime: { $hours }h{ $new ->
    [true] {" "}(new player)
   *[false] {""}
}
wg-ahelp-card-antag = Antagonist
wg-ahelp-card-notes = Notes: { $notes } · Bans: { $bans }
wg-ahelp-card-bank = Bank: { $bank }
wg-ahelp-card-tickets = Previous tickets: { $count }
wg-ahelp-card-loading = Loading…

## Quick actions

wg-ahelp-action-follow = Follow
wg-ahelp-action-bring = Bring
wg-ahelp-action-respawn = Respawn
wg-ahelp-action-aheal = Aheal
wg-ahelp-action-notes = Notes
wg-ahelp-action-bans = Bans
wg-ahelp-action-ban = Ban
wg-ahelp-action-kick = Kick
wg-ahelp-action-logs = Logs
wg-ahelp-action-investigate = Investigate
wg-ahelp-action-player-panel = Player panel
wg-ahelp-action-loadout = Provide loadout
wg-ahelp-action-loadout-tooltip = Gives the player their job loadout again, free or charged to their bank.

## Provide loadout

wg-ahelp-loadout-title = Provide loadout
wg-ahelp-loadout-prompt = Give { $player } the loadout they picked for their job? { $price ->
    [-1] The panel doesn't know its price yet.
    [0] It costs nothing.
   *[other] It costs { $price } credits.
}
wg-ahelp-loadout-free = Give for free
wg-ahelp-loadout-charge = { $price ->
    [-1] Charge the player
   *[other] Charge the player ({ $price })
}
wg-ahelp-loadout-cancel = Cancel

## History and search

wg-ahelp-history-title = Ticket history
wg-ahelp-history-search-title = Tickets matching "{ $text }"
wg-ahelp-history-entry = #{ $number } · R{ $round } · { $username } · { $state } · { $date }
wg-ahelp-history-empty = Nothing found.
wg-ahelp-history-line = [color=gray]{ $time }[/color] { $source ->
    [staff] [color=#ff6b6b]{ $author }[/color]
    [discord] [color=#8fa3ff]{ $author } (Discord)[/color]
    [system] [color=gray]{ $author }[/color]
   *[player] { $author }
}{ $adminOnly ->
    [true] {" "}[color=#ffb347](admin only)[/color]
   *[false] {""}
}: { $text }

## Player side

wg-ahelp-player-form-message = What happened? Who, where and when helps staff most.
wg-ahelp-player-form-submit = Submit ticket
wg-ahelp-player-closed = This ticket is closed. Write again to open a new one, or press New ticket.
wg-ahelp-player-new-ticket = New ticket
wg-ahelp-player-new-ticket-open = Your ticket is still open. Write in it to add to it.
wg-ahelp-player-export-header = Ticket #{ $number }
wg-ahelp-player-status = Ticket { $number ->
    [0] (being filed)
   *[other] #{ $number }
}: { $state ->
    [Open] waiting for staff
    [Claimed] { $handler ->
        [none] staff are on it
       *[other] { $handler } is on it
    }
   *[other] closed
}
