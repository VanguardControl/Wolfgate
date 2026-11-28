## Window

wg-log-investigator-title = Log Investigator
wg-log-investigator-round = Round
wg-log-investigator-current = Current
wg-log-investigator-from = From
wg-log-investigator-to = to
wg-log-investigator-end = end
wg-log-investigator-players = Players
wg-log-investigator-player-search = Find a player by account or character name
wg-log-investigator-all-players = Only logs involving all of them
wg-log-investigator-entity = Entity { $id }
wg-log-investigator-text = Text in the message (Enter searches)
wg-log-investigator-non-players = Include logs with no player
wg-log-investigator-oldest-first = Oldest first
wg-log-investigator-categories = Categories
wg-log-investigator-search = Search
wg-log-investigator-collapse = Fold repeats
wg-log-investigator-live = Live
wg-log-investigator-live-tooltip = On this round's newest-first list with no end time, new logs keep coming in.
wg-log-investigator-new-logs = { $count ->
    [one] 1 new log
   *[other] { $count } new logs
} (show)
wg-log-investigator-more = Load more
wg-log-investigator-copy = Copy all
wg-log-investigator-count = { $loaded } loaded of { $total }
wg-log-investigator-behind = (this round: the last few seconds may not be in yet)
wg-log-investigator-run = ×{ $count } over { $seconds }s:
wg-log-investigator-pick-row = Pick a log to see who and what it names.
wg-log-investigator-filter = Filter
wg-log-investigator-follow = Follow
wg-log-investigator-vv = VV
wg-log-investigator-nearby = Nearby
wg-log-investigator-timeline = Timeline
wg-log-investigator-encounter = Encounter
wg-log-investigator-radius = Nearby radius (m)
wg-log-investigator-radius-tooltip = How far from a log's position Nearby and Encounter look for players. Nearby's report can then filter the logs to them.
wg-log-investigator-report-filter = Filter the logs to these players
wg-log-investigator-preset-all = All logs
wg-log-investigator-preset-combat = Combat
wg-log-investigator-preset-comms = Chat and comms
wg-log-investigator-preset-ships = Ships
wg-log-investigator-preset-economy = Economy

## Categories

wg-log-category-combat = Combat
wg-log-category-chat = Chat and comms
wg-log-category-interaction = Interaction
wg-log-category-ships = Ships
wg-log-category-economy = Economy
wg-log-category-medical = Medical and hazards
wg-log-category-atmos = Atmos
wg-log-category-world = Construction and world
wg-log-category-round = Round and spawning
wg-log-category-admin = Admin
wg-log-category-wolfgate = Wolfgate actions

## Reports

wg-log-report-encounter-title = Encounter report
wg-log-report-timeline-title = Timeline of { $player }
wg-log-report-nearby-title = Who was within { $radius } m at { $time }
wg-log-report-first-strike = First strike
wg-log-report-first-strike-line = { $time }: { $attacker } hit { $victim } with { $weapon }
wg-log-report-hits = Who hit whom
wg-log-report-hit-line = { $attacker } → { $victim }: { $hits } hits, { $damage } damage ({ $weapons }), { $first }–{ $last }
wg-log-report-no-hits = No player was hit in this window.
wg-log-report-via = { $gunner } (via { $gun })
wg-log-report-npc = Players hitting non-players
wg-log-report-npc-line = { $attacker }: { $hits } hits on { $targets } targets
wg-log-report-deaths = Deaths
wg-log-report-chat-before = Said in the 10 minutes before the first strike
wg-log-report-no-chat = Nothing was said by the parties, and no hails between their ships.
wg-log-report-nearby = Nearby
wg-log-report-nearby-line = { $name }: { $distance } m at { $time }{ $sameGrid ->
    [true] , same grid
   *[false] {""}
}{ $alive ->
    [true] {""}
   *[false] , dead
}{ $spoke ->
    [true] , spoke
   *[false] {""}
}
wg-log-report-nobody-nearby = Nobody was sampled nearby. Positions are recorded every few seconds from when this feature went live.
wg-log-report-phases = Phases
wg-log-report-phase-title = { $from }–{ $to } · { $category } ({ $count })
wg-log-report-phase-skipped = { $count } logs in the middle of this phase are left out.
wg-log-report-timeline-cut = { $phases } more phases are left out; narrow the time window.
wg-log-report-no-logs = No logs for this player in the window.
wg-log-report-pick-player = Pick a player first.

## Commands

cmd-wglogs-desc = Opens the log investigator, filled in.
cmd-wglogs-help = Usage: wglogs [round N] [player GUID-or-username]... [entity ID]... [text WORDS...]
cmd-wglogs-bad-arg = Can't read "{ $arg } { $value }".
cmd-wglogs-off = The log investigator is off (wg.admin_logs.investigator); use adminlogs.
cmd-wginvestigate-desc = Opens the log investigator on an ahelp ticket, with its encounter report.
cmd-wginvestigate-help = Usage: wginvestigate TICKETKEY (the ticket panel's Investigate button runs this)
cmd-wginvestigate-no-ticket = No ticket with that key this round.
