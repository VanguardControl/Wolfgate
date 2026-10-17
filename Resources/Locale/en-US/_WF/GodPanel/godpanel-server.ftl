# God Panel, server side

cmd-godpanel-desc = Opens the God Panel.
cmd-godpanel-help = Usage: {$command}
cmd-godpanel-no-player = Only players can open the God Panel.
cmd-godpanelui-desc = Opens the God Panel window.
cmd-godpanelui-help = Usage: {$command}

wf-godpanel-space = deep space
wf-godpanel-pa-sender = {$ship} control

wf-godpanel-error-gone = That target is gone.
wf-godpanel-error-no-body = You have no body to do that with.
wf-godpanel-error-not-bringable = A grid or map can't be brought to you.
wf-godpanel-error-not-grid = That needs a ship or station.
wf-godpanel-error-no-text = There is no text to send.
wf-godpanel-error-no-speakers = That ship has no PA speakers online.
wf-godpanel-error-bad-code = That is not a situation code.
wf-godpanel-error-no-console = That ship has no powered console to ring.
wf-godpanel-error-no-encounter = That is not part of an encounter.

wf-godpanel-event-death = {$name} died on {$grid}.
wf-godpanel-event-crit = {$name} went down on {$grid}.
wf-godpanel-event-revived = {$name} is back on their feet on {$grid}.
wf-godpanel-event-ghosted = {$user} is a ghost.
wf-godpanel-event-joined-body = {$user} took {$name}.
wf-godpanel-event-connected = {$user} connected.
wf-godpanel-event-left = {$user} left.
wf-godpanel-event-round-started = Round {$id} started.
wf-godpanel-event-round-ended = Round {$id} ended.

wf-godpanel-event-encounter-started = {$name} started near {$place}.
wf-godpanel-event-encounter-resolved = {$name} near {$place}: {$result}.
wf-godpanel-event-ert = ERT {$team} called with {$count} places.

wf-godpanel-event-sector-changed = {$cell} passed from {$from} to {$to}.
wf-godpanel-event-sector-claimed = {$faction} took {$cell}.
wf-godpanel-event-sector-freed = {$cell} is free.
wf-godpanel-event-sector-contested = {$faction} is contesting {$cell}.
wf-godpanel-event-sector-retaken = {$cell} was retaken from {$faction}.
wf-godpanel-event-sector-disputed = {$faction} and a player ship are fighting over {$cell}.
wf-godpanel-event-sector-undisputed = {$cell} is no longer disputed by {$faction}.

wf-godpanel-event-war-hot = The war level is HOT.
wf-godpanel-event-war-cold = The war level is COLD.

wf-godpanel-event-ship-code = {$ship} is on {$code}.
wf-godpanel-event-ship-gq-on = {$ship} sounded general quarters.
wf-godpanel-event-ship-gq-off = {$ship} secured from general quarters.
wf-godpanel-event-ship-distress-on = {$ship} sent a distress call.
wf-godpanel-event-ship-distress-off = {$ship} ended its distress call.
wf-godpanel-event-ship-announce = {$ship} announced: {$text}

wf-godpanel-event-second-chance-yes = {$player} took a second chance as {$name}.
wf-godpanel-event-second-chance-no = {$player} turned down a second chance as {$name}.
wf-godpanel-event-ghost-offer-taken = {$player} took {$name}.

wf-godpanel-event-gm = {$gm} { $action ->
    [follow] followed {$target}
    [goto] went to {$target}
    [bring] brought {$target}
    [secondchance] offered {$target} a second chance
    [offertoghosts] offered {$target} to ghosts
    [rejuvenate] rejuvenated {$target}
    [poweron] powered {$target} on
    [poweroff] powered {$target} off
    [announce] announced on the PA of {$target}: {$text}
    [setcode] set the code of {$target} to {$text}
    [generalquarterson] sounded general quarters on {$target}
    [generalquartersoff] secured {$target} from general quarters
    [distresson] sent a distress call from {$target}
    [distressoff] ended the distress call of {$target}
    [hailasgod] hailed {$target} as God
    [playpasound] played a sound on the PA of {$target}: {$text}
    [stoppasound] stopped the PA sound of {$target}
    [endencounter] ended {$target}
    [playsound] played a sound for everyone: {$text}
    [stopsound] stopped every sound
    [pauseencounters] paused the encounter scheduler
    [resumeencounters] resumed the encounter scheduler
    [reaverpause] paused the Reaver campaign
    [reaverresume] resumed the Reaver campaign
    [warlevelcold] set the war level to COLD
    [warlevelhot] set the war level to HOT
   *[other] used {$action} on {$target}
}
