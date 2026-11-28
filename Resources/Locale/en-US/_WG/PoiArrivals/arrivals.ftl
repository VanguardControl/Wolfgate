wg-poi-arrival-sender = Sector Traffic Control

# $station is the station's name; $range (km) and $bearing are from the sector core
wg-poi-arrival-anomalous-lab = The {$station} has dropped out of FTL {$range} km bearing {$bearing} from the sector core. Its directorate is taking contracts.
wg-poi-arrival-sevastopol = The {$station} has dropped out of FTL {$range} km bearing {$bearing} from the sector core. Its archive is open to couriers.

cmd-wg_poiarrivals-desc = Lists the stations still to arrive this shift, or jumps one or all of them in now.
cmd-wg_poiarrivals-help = Usage: {$command} [poi id | all]
cmd-wg_poiarrivals-hint = POI to jump in now
cmd-wg_poiarrivals-none = No stations are waiting to arrive.
cmd-wg_poiarrivals-entry = {$id}: due {$due} into the shift ({$left} from now)
cmd-wg_poiarrivals-not-pending = {$id} isn't waiting to arrive.
cmd-wg_poiarrivals-forced = {$id} is jumping in now.
cmd-wg_poiarrivals-failed = {$id} failed to load; see the server log.
cmd-wg_poiarrivals-round-over = The shift is over; nothing more arrives.
