## Survey console
wf-survey-console-title = Sector Survey Console
wf-survey-console-system = { $system }
wf-survey-console-hint = Ping or rebuild the shuttle console's map to refresh the destination tree.
wf-survey-console-count = { $count } bodies
wf-survey-console-empty = No bodies found in this system.
wf-survey-detail-none = Select a body to read its FTL destination.

## Survey table columns. Each has a fixed cell width in WFSurveyPlanetRow, so keep these short.
wf-survey-col-body = BODY
wf-survey-col-distance = DISTANCE
wf-survey-col-status = STATUS
wf-survey-col-crack = CRACK
wf-survey-col-veins = VEINS

## Survey rows
wf-survey-row-distance = { $distance } tiles
wf-survey-row-distance-unknown = distance unknown
wf-survey-row-sanctioned = sanctioned
wf-survey-row-unsanctioned = unsanctioned
wf-survey-row-cracked = cracked
wf-survey-row-intact = intact
wf-survey-row-no-surface = no crackable ground
wf-survey-row-beacon = FTL destination: { $beacon }
wf-survey-row-beacon-none = No FTL destination on file.

## Vein rating
wf-survey-rating-poor = poor
wf-survey-rating-fair = fair
wf-survey-rating-rich = rich
wf-survey-rating-very-rich = very rich
wf-survey-rating-unknown = unrated

## Surveyor
wf-surveyor-not-on-ground = The surveyor needs solid planet ground beneath it.
wf-surveyor-found = Found { $count } dense returns.
wf-surveyor-found-none = No dense returns nearby.

## Deep vein
wf-vein-examine-unknown = It is unremarkable, as far as you can tell.
wf-vein-examine-ore = A seam of { $ore } runs through the rock.
wf-vein-examine-yield = The seam looks { $band }.
wf-vein-yield-band-trace = barely worth the trouble
wf-vein-yield-band-modest = modest
wf-vein-yield-band-strong = substantial
wf-vein-yield-band-exceptional = exceptional

## wfsurvey command
cmd-wfsurvey-desc = List star system bodies, deep veins and reveal state.
cmd-wfsurvey-help = Usage: { $command } <list | veins [radius] | reveal [radius]>
cmd-wfsurvey-invalid-args = Expected one of: list, veins, reveal.
cmd-wfsurvey-hint-sub = <list|veins|reveal>
cmd-wfsurvey-hint-radius = [radius]
cmd-wfsurvey-list-header = { $system }: { $count } bodies
cmd-wfsurvey-veins-header = { $count } deep veins within { $radius } tiles
cmd-wfsurvey-bad-radius = Not a radius: { $radius }
cmd-wfsurvey-no-body = That console command needs a body in the world to measure from.
cmd-wfsurvey-no-console = No single survey console to read: stand on a grid that has one, or have exactly one in the world.
cmd-wfsurvey-list-row = { $name } | { $beacon } | { $distance } tiles | { $sanctioned } | { $cracked } | { $rating }
cmd-wfsurvey-list-empty = No bodies found in this system.
cmd-wfsurvey-veins-row = { $vein } | { $ore } | yield { $yield } | rich { $rich }
cmd-wfsurvey-veins-none = No deep veins within radius.
cmd-wfsurvey-revealed = Revealed { $count } deep veins.
