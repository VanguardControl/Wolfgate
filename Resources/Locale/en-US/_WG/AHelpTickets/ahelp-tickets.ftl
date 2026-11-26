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
wg-ahelp-action-teleport = Teleport to
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
