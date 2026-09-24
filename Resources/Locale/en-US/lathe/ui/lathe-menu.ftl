lathe-menu-title = Lathe Menu
lathe-menu-queue = Queue
lathe-menu-server-list = Server list
lathe-menu-sync = Sync
lathe-menu-search-designs = Search designs
lathe-menu-category-all = All
lathe-menu-search-filter = Filter:
lathe-menu-amount = Amount:
lathe-menu-loop = Loop
lathe-menu-skip = Skip blocked jobs
lathe-menu-reagent-slot-examine = It has a slot for a beaker on the side.
lathe-reagent-dispense-no-container = Liquid pours out of {THE($name)} onto the floor!
lathe-menu-result-reagent-display = {$reagent} ({$amount}u)
lathe-menu-material-display = {$material} ({$amount})
lathe-menu-tooltip-display = {$amount} of {$material}
lathe-menu-description-display = [italic]{$description}[/italic]
lathe-menu-material-amount = { $amount ->
    [1] {NATURALFIXED($amount, 2)} {$unit}
    *[other] {NATURALFIXED($amount, 2)} {MAKEPLURAL($unit)}
}
lathe-menu-material-amount-missing = { $amount ->
    [1] {NATURALFIXED($amount, 2)} {$unit} of {$material} ([color=red]{NATURALFIXED($missingAmount, 2)} {$unit} missing[/color])
    *[other] {NATURALFIXED($amount, 2)} {MAKEPLURAL($unit)} of {$material} ([color=red]{NATURALFIXED($missingAmount, 2)} {MAKEPLURAL($unit)} missing[/color])
}
lathe-menu-entity-amount-missing = {$amount} of {$material} ([color=red]{$missingAmount} missing[/color])
lathe-menu-reagent-amount-missing = {$amount}u of {$material} ([color=red]{$missingAmount}u missing[/color])
lathe-menu-no-materials-message = No materials loaded.
lathe-menu-silo-linked-message = Ore silo connected
lathe-menu-fabricating-message = Fabricating...
lathe-menu-materials-title = Materials
lathe-menu-queue-title = Build Queue
lathe-menu-queue-amount-tooltip = Requested total (up to {$max}). Press Enter or leave the field to apply.
lathe-menu-recipes-title = Recipes
lathe-menu-quantity-hint = items per job
lathe-menu-queue-empty = Queue is clear. Choose a recipe to get started.
lathe-menu-supplies-title = Linked supplies
lathe-menu-parts-silo-online = Parts silo connected
lathe-menu-parts-silo-offline = Parts silo: not connected or unavailable
lathe-menu-chemical-silo-online = Chemical silo connected
lathe-menu-chemical-silo-offline = Chemical silo: not connected or unavailable
lathe-menu-status-printing = Printing
lathe-menu-status-ready = Ready
lathe-menu-status-waiting = Waiting for supplies
lathe-menu-status-design-unavailable = Design unavailable
lathe-menu-queue-missing-material = { $amount ->
    [1] {NATURALFIXED($amount, 2)} {$unit} of {$material}
    *[other] {NATURALFIXED($amount, 2)} {MAKEPLURAL($unit)} of {$material}
}
lathe-menu-queue-missing-entity = {$amount} × {$material}
lathe-menu-queue-missing-reagent = {NATURALFIXED($amount, 2)}u {$material}
lathe-menu-recipe-ready = Ready to start
lathe-menu-recipe-missing = Queue and wait for supplies

lathe-menu-silo-link-hint = Open a silo to connect this machine.
lathe-menu-cancel-job = Cancel job
lathe-menu-job-progress = Completed / requested

fabrication-silo-title = Fabrication silo
fabrication-silo-parts = Parts silo
fabrication-silo-chemicals = Chemical silo
fabrication-silo-machines = SUPPLY CONNECTIONS
fabrication-silo-components = Stored components
fabrication-silo-reagents = Stored chemicals
fabrication-silo-search = Search stored supplies
fabrication-silo-parts-hint = Insert components by using them on the silo. Identical parts share one inventory row. Connect machines below to supply their queued jobs.
fabrication-silo-chemicals-hint = Use a beaker or jerry can to deposit chemicals used in fabrication recipes. Unsupported chemicals stay in the container. Connect machines below to supply their queued jobs.
fabrication-silo-transferred = Transferred {NATURALFIXED($amount, 2)}u into the chemical silo.
fabrication-silo-container-empty = This container is empty.
fabrication-silo-chemicals-rejected = Not accepted: {$chemicals}. Only fabrication ingredients without special data can be stored; these chemicals remain in the container.
fabrication-silo-part-rejected = This item is not a component used in a fabrication recipe.
fabrication-silo-full = The parts silo is full.
fabrication-silo-insertion-failed = The silo could not accept these contents.
fabrication-silo-needs-container = Use a drainable chemical container, such as a beaker or jerry can.
fabrication-silo-container-unavailable = This container has no accessible liquid reservoir.
fabrication-silo-connected = Connected • supplying this machine
fabrication-silo-unavailable = Connected • supply paused: check silo power and machine range
fabrication-silo-available = Available to connect
fabrication-silo-connect = Connect
fabrication-silo-disconnect = Disconnect
fabrication-silo-no-machines = No machines available. Machines must be on the same grid, within 125 metres, and not connected to another silo of this type.
fabrication-silo-eject = Eject
fabrication-silo-eject-hint = Eject one stored item or physical stack from this group.
fabrication-silo-empty = No matching supplies stored.
