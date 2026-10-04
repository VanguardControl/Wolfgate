wf-encounter-ship-name = {$vessel} {$designation}
wf-encounter-name-convoy = Convoy {$designation}
wf-encounter-open-space = open space
wf-encounter-marker-distance = {$name}  {$distance} km

wf-encounter-name-patrol = TSF patrol {$designation}
wf-encounter-name-freighter = Independent freighter {$designation}
wf-encounter-name-tsf-transport = TSF transport {$designation}
wf-encounter-cargo-none = no cargo
wf-encounter-cargo-general = general cargo
wf-encounter-cargo-food = foodstuffs
wf-encounter-cargo-medical = medical supplies
wf-encounter-cargo-industrial = industrial materials
wf-encounter-cargo-parts = ship parts
wf-encounter-cargo-munitions = munitions
wf-encounter-cargo-arms = arms and equipment
wf-encounter-cargo-luxury = luxury goods
wf-encounter-cargo-salvage = salvage and scrap
wf-encounter-announce-freight-run = This is {$name}, entering the sector with {$cargo}. We make {$stops} stops, first {$destination}. Any traffic on our lane, please keep clear.
wf-encounter-announce-tsf-transport = All vessels, this is {$name}, a Trans-Solar Federation transport under escort, inbound to {$destination}. Stay outside one kilometre. You will not be warned twice.
# $need is fuel, thrusters or none. A real call gives the ship's registered name as scanners show it, its position and what it lacks.
wf-encounter-distress-adrift = Mayday, mayday. This is {$name}, adrift at {$x}, {$y}. { $need ->
    [fuel] Our generators are dry and our batteries are flat. We need fuel brought aboard to get them started.
    [thrusters] Our thrusters are wrecked. We need someone with a repair device or spare thrusters.
   *[none] We have lost propulsion. Requesting assistance from any vessel.
}
wf-encounter-zone-warn-label = WARNING ZONE
wf-encounter-zone-attack-label = ATTACK ZONE
wf-encounter-zone-warn-1 = {$intruder}, you are {$distance} metres off a restricted vessel. Divert your course and open the range now.
wf-encounter-zone-warn-2 = {$intruder}, you are inside our warning zone. Turn away immediately or you will be fired upon.
wf-encounter-zone-warn-3 = Vessel {$intruder}, this is your warning. Alter course away from us. Do not approach.
wf-encounter-zone-attack = {$intruder}, you were warned. Weapons free.
wf-encounter-pirate-zone-warn-1 = {$intruder}, cut your engines and stay right where you are. We are coming alongside.
wf-encounter-pirate-zone-warn-2 = You there, {$intruder}. Heave to and open your hold, and nobody has to get hurt.
wf-encounter-pirate-zone-warn-3 = {$intruder}, you are {$distance} metres into our hunting ground. Kill your drive.
wf-encounter-pirate-zone-attack = Too slow, {$intruder}. Light them up.
wf-encounter-name-pirates = Unidentified raiders {$designation}
wf-encounter-name-boarders = Pirate boarders {$designation}
wf-encounter-name-trader = Wandering trader {$designation}
wf-encounter-name-backup = TSF patrol {$designation} under attack
wf-encounter-announce-backup = Any vessel, any vessel, this is {$name}. We are engaged by pirate raiders and outgunned. Requesting immediate backup. The Federation pays its debts.
wf-encounter-reward-paid = For your help with {$name}, {$amount} spesos have been paid into your account.
wf-encounter-reward-paid-credits = For your help with {$name}, {$amount} spesos have been paid into your account and you have been issued {$credits} military credits.
wf-encounter-reward-thanks-tsf = All vessels that answered our call: the Federation thanks you. Payment has been made.
wf-encounter-reward-thanks-hauler = Whoever that was, thank you. We've sent what we can spare.
wf-encounter-announce-trader = {$name} is passing through the sector, open for business to anyone who comes alongside. Small stock, low prices, no refunds.
wf-encounter-name-ambush = Hauler {$designation} under attack
wf-encounter-sender-traffic = Sector traffic control
wf-encounter-announce-freighter = {$name} is entering the sector at {$origin}, inbound to {$destination} with general cargo.
wf-encounter-announce-ambush = Mayday, mayday, mayday. {$name}. Raiders have jumped us and we can't outrun them. Any vessel in range, we need guns out here. Please hurry.

wf-encounter-preset-quiet = Quiet sector
wf-encounter-preset-standard = Standard sector
wf-encounter-preset-dangerous = Dangerous sector
wf-encounter-vote-title = How dangerous is the sector this round?
wf-encounter-vote-initiator = Sector traffic control
wf-encounter-vote-result = This round: {$preset}.

wf-encounter-resolution-expired = Expired
wf-encounter-resolution-completed = Completed
wf-encounter-resolution-decided = Decided
wf-encounter-resolution-destroyed = Destroyed
wf-encounter-resolution-ended = Ended

# Admin encounter window
wf-encounter-admin-title = Encounters
wf-encounter-admin-scheduler = Scheduler
wf-encounter-admin-enabled = Start encounters automatically
wf-encounter-admin-paused = Paused
wf-encounter-admin-next = Next attempt in {$time}
wf-encounter-admin-next-none = Not running
wf-encounter-admin-interval = Every (seconds)
wf-encounter-admin-to = to
wf-encounter-admin-cap = At most running
wf-encounter-admin-apply = Apply
wf-encounter-admin-schedule-now = Pick one now
wf-encounter-admin-start = Start an encounter
wf-encounter-admin-distance = Metres north of you
wf-encounter-admin-spawn = Start
wf-encounter-admin-prototype-manual = {$id} (manual only)
wf-encounter-admin-running = Encounters
wf-encounter-admin-none = No encounters.
wf-encounter-admin-bad-input = Enter whole seconds, a cap and a distance as plain numbers.
wf-encounter-admin-bad-settings = Intervals must be 30 to 86400 seconds with the longest not below the shortest, and the cap 0 to 10.
wf-encounter-admin-gone = That encounter or ship no longer exists.
wf-encounter-admin-entry = {$name} ({$prototype}): {$state}, running {$age}
wf-encounter-admin-expires = Expires in {$time}
wf-encounter-admin-ship = {$key}: {$name}, side {$side}, {$crew} crew, {$activity}
wf-encounter-admin-ship-disabled = {$key}: {$name}, side {$side}, {$crew} crew, out of the fight
wf-encounter-admin-ship-gone = {$key}: gone, side {$side}
wf-encounter-admin-teleport = Go to
wf-encounter-admin-resolve = Resolve
wf-encounter-admin-end = End and remove
wf-encounter-admin-crew = Crew
wf-encounter-admin-preset = Preset
wf-encounter-admin-budget = Budget in use: {$cost} of {$budget}
wf-encounter-admin-start-round = Place round-start encounters
wf-encounter-admin-reveal = Reveal

# wf_encounter command
cmd-wf_encounter-desc = Lists, spawns and ends encounters, and runs or pauses the encounter scheduler.
cmd-wf_encounter-help = Usage: {$command} list | spawn <prototype> [distance] | end <encounter> | schedule | pause | resume
cmd-wf_encounter-hint-sub = <subcommand>
cmd-wf_encounter-hint-prototype = <encounter prototype>
cmd-wf_encounter-hint-distance = [metres north of you]
cmd-wf_encounter-hint-uid = <encounter entity>
cmd-wf_encounter-list-line = {$uid} {$prototype} "{$name}" {$state}, {$ships} ships, origin {$x}, {$y}
cmd-wf_encounter-list-footer = {$count} encounters.
cmd-wf_encounter-state-active = Active
cmd-wf_encounter-no-player = You need a body or a ghost on a map to spawn an encounter.
cmd-wf_encounter-unknown = {$prototype} is not an encounter.
cmd-wf_encounter-bad-distance = {$arg} is not a distance between 0 and 20000.
cmd-wf_encounter-spawned = Started "{$name}" as {$uid}.
cmd-wf_encounter-spawn-failed = {$prototype} could not be started. Station encounters need a station (routes need two) on this map; otherwise the server log says which ship failed.
cmd-wf_encounter-not-found = {$arg} is not an encounter.
cmd-wf_encounter-ended = Ended {$uid} and removed its ships.
cmd-wf_encounter-scheduled = The scheduler started {$uid}.
cmd-wf_encounter-not-scheduled = The scheduler started nothing; the server log says why.
cmd-wf_encounter-paused = Encounter scheduler paused.
cmd-wf_encounter-resumed = Encounter scheduler resumed.
wf-encounter-distress-attacked = Mayday, mayday! This is {$name}. We are under attack at {$x}, {$y}. Any vessel, please, we need help!
wf-encounter-map-object = Contact: {$name}
wf-encounter-name-trader-surplus = Surplus dealer {$designation}
wf-encounter-announce-trader-surplus = {$name} is in the sector with decommissioned military stock. Arms, kit and ammunition, priced to move. Come alongside with your hands where we can see them.
wf-encounter-name-trader-curios = Curio dealer {$designation}
wf-encounter-announce-trader-curios = {$name} is passing through with antiques and curiosities from estates across the frontier. One of each, and none of it will be here tomorrow.
wf-encounter-name-trader-medic = Medical supplier {$designation}
wf-encounter-announce-trader-medic = {$name} is in the sector with medical stock: dressings, pens, pills and clinic equipment below station prices while they last.
wf-encounter-name-trader-shipwright = Shipwright {$designation}
wf-encounter-announce-trader-shipwright = {$name} is in the sector with ship fittings off the slip: thrusters, gyros, drives, airlocks and the boards to run them. Come alongside and bring your refit list.
wf-encounter-name-trader-fence = Pirate fence {$designation}
wf-encounter-announce-trader-fence = {$name} is moving goods through the sector, no questions asked. Arms, kit and cheap contraband, and anyone who comes alongside peacefully is welcome to look.

# Larger ambush
wf-encounter-name-ambush-large = Freighter {$designation} under attack
wf-encounter-announce-ambush-large = Mayday, mayday, mayday. {$name}. Two raiders have us boxed in and our guns can't hold them off. Any vessel in range, we need help now. We will pay what we can.

# Stranded freighters. The adrift call is wf-encounter-distress-adrift above.
wf-encounter-name-stranded = Disabled freighter {$designation}
wf-encounter-reward-thanks-stranded = That's thrust under us again. Whoever you are, thank you. We've sent what we can spare.

# False mayday. It is made at once, on the common channel, by the ship that baits the trap. Unlike a real call it gives no
# registered name and no position, says only that the drive is gone, and names a hull class that is not the one on the scanner.
wf-encounter-announce-false-mayday = Mayday, mayday. This is a Brute-class hauler, adrift and losing power. We have no drive left. Come alongside and dock, any vessel. We can't hold out much longer.
wf-encounter-announce-false-mayday-large = Mayday, mayday. This is an Olympus-class freighter, adrift with the lights failing. We have no drive left. Come alongside and dock, please. We can't hold out much longer.

# Skirmishes
wf-encounter-name-skirmish-patrol = Patrol engaging raiders {$designation}
wf-encounter-announce-skirmish-patrol = Traffic control to all vessels: {$name}. A TSF patrol has run pirate raiders to ground and the two are about to trade fire. Neither is taking calls. Keep clear, unless you mean to take a side.
wf-encounter-name-skirmish-bands = Rival raiders {$designation}
wf-encounter-announce-skirmish-bands = Traffic control to all vessels: {$name}. Two raider bands have found each other in open space and are settling an old score. Neither is taking calls. Stay clear of both, or pick one.
wf-encounter-name-skirmish-corsairs = Patrol engaging corsairs {$designation}
wf-encounter-announce-skirmish-corsairs = Traffic control to all vessels: {$name}. A TSF patrol has cornered a RedSail corsair crew in open space. Expect heavy fire. Anything that gets between them is on its own.
wf-encounter-name-skirmish-hostile = Running battle {$designation}
wf-encounter-announce-skirmish-hostile = Traffic control to all vessels: {$name}. Two armed parties are fighting it out in open space, and both are firing on anything that comes near. Do not approach either one.
wf-encounter-reward-thanks-skirmish-tsf = TSF patrol to the vessels that stood with us: your help is noted, and it has been paid for.
wf-encounter-reward-thanks-skirmish-pirates = You fight well for a stranger. Here is a cut of what we took off them.
wf-encounter-reward-thanks-skirmish-corsairs = RedSail remembers a favour. Take your share and fly on.

# Black market transport. Only a rumour goes out, once someone has fought it.
wf-encounter-name-blackmarket = Unregistered transport {$designation}
wf-encounter-announce-blackmarket = Sector traffic control has unconfirmed reports of weapons fire around {$name}, a vessel on no manifest. Whatever it carries, it isn't licensed. Keep your distance.
wf-encounter-blackmarket-zone-warn-1 = {$intruder}, you are {$distance} metres off a private vessel. Turn around. We won't ask twice.
wf-encounter-blackmarket-zone-warn-2 = {$intruder}, this is private cargo, and whatever you think you saw, you didn't. Open the range.
wf-encounter-blackmarket-zone-warn-3 = You are drifting into something that is none of your business, {$intruder}. Fly on.
wf-encounter-blackmarket-zone-attack = {$intruder}, that was your last chance. Open fire.
