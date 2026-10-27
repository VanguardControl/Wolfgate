research-technology-wf-xenobiology = Xenobiology

## Slime processor

comp-slime-processor-verb-activate = Process slimes
comp-slime-processor-verb-activate-message-success = Turn the stored slimes into extracts.
comp-slime-processor-verb-activate-message-no-slimes = There are no slimes to process.

## Extracts

slime-extract-not-exhausted = The slime extract bubbles with life. Try adding blood, water or plasma.
slime-extract-exhausted = The slime extract is spent and no longer reacts.

## Scanner

slime-scanner-info = Name:{"\u0009"}[bold]{ $name }[/bold]
    Nutrition:{"\u0009"}[bold]{ $nutrition }[/bold]
    Mutation chance:{"\u0009"}[bold]{ $chance }%[/bold]

## Console

xenobiology-console-in-use = Someone is already using the console.
xenobiology-console-monkey-cube-inserted = Monkey cube stored. The console has { $cubes } { $cubes ->
    [1] cube
   *[other] cubes
    }.
xenobiology-console-mutation-potion-inserted = Mutation potion stored. The console has { $potions } { $potions ->
    [1] potion
   *[other] potions
    }.
xenobiology-console-stabilizer-potion-inserted = Stabilizer potion stored. The console has { $potions } { $potions ->
    [1] potion
   *[other] potions
    }.
xenobiology-console-slime-picked-up = Picked up { $name }.
xenobiology-console-slime-picked-up-fail-full = Can't pick up { $name }. The console already holds a slime.
xenobiology-console-slime-picked-up-fail-none-found = No slime under the eye.
xenobiology-console-slime-placed-down = Placed down { $name }.
xenobiology-console-slime-placed-down-fail-none-stored = No slime stored.
xenobiology-console-monkey-placed = Placed a monkey. { $cubes } { $cubes ->
    [1] cube
   *[other] cubes
    } left.
xenobiology-console-monkey-placed-fail-empty = Not enough monkey cubes ({ $cubes }). Insert some or recycle eaten monkeys.
xenobiology-console-monkey-recycled = Recycled { $monkeys } { $monkeys ->
    [1] monkey
   *[other] monkeys
    }. The console has { $cubes } { $cubes ->
    [1] cube
   *[other] cubes
    }.
xenobiology-console-monkey-recycled-failed-none = No eaten monkeys under the eye.
xenobiology-console-mutation-potion-applied-failed-empty = No mutation potions stored.
xenobiology-console-stabilizer-potion-applied-failed-empty = No stabilizer potions stored.

## Potions

slime-fireproof-max = That's already fireproof.
slime-fireproof-applied = Fireproofed. { $uses } { $uses ->
    [one] use
   *[other] uses
    } left.
slime-potion-radio-applied = { CAPITALIZE(THE($target)) } can now use the radio.
slime-potion-enhancer-applied = { CAPITALIZE(THE($target)) } now has { $uses } { $uses ->
    [one] use
   *[other] uses
    } left.
slime-potion-mutation-max = { CAPITALIZE(THE($target)) } is already at a 100% mutation chance.
slime-potion-mutation-min = { CAPITALIZE(THE($target)) } is already at a 0% mutation chance.
slime-potion-mutation-chance = { CAPITALIZE(THE($target)) } now has a { $chance }% chance to mutate.
slime-potion-name-changed = { $old } is now named { $new }.
slime-potion-name-changed-self = You are now named { $new }.
slime-potion-speed-applied = { CAPITALIZE(THE($target)) } now slows you to { $walk } walking and { $sprint } running.
slime-potion-steroid-applied = { CAPITALIZE(THE($target)) } will make { $amount } extra { $amount ->
    [one] extract
   *[other] extracts
    } when processed.
slime-gender-select-first = Pick a gender first.

slime-name-change-potion-ui-header = Name Change Potion
slime-name-change-potion-current-text-label = New name:
slime-name-change-potion-ui-reset-label-text = Reset
slime-name-change-potion-ui-clear-label-text = Clear

comp-gender-change-potion-category = Set gender
comp-gender-change-potion-neuter = Neuter (it/its)
comp-gender-change-potion-epicene = Epicene (they/them)
comp-gender-change-potion-female = Female (she/her)
comp-gender-change-potion-male = Male (he/him)
comp-gender-change-potion-neuter-set = Set to neuter.
comp-gender-change-potion-epicene-set = Set to epicene.
comp-gender-change-potion-female-set = Set to female.
comp-gender-change-potion-male-set = Set to male.
comp-gender-change-potion-neuter-set-already = Already neuter.
comp-gender-change-potion-epicene-set-already = Already epicene.
comp-gender-change-potion-female-set-already = Already female.
comp-gender-change-potion-male-set-already = Already male.

ghost-role-information-slime-sentience-potion-description = A sentience potion woke you up. You are free to do as you please.

## Reagents

reagent-name-orange-smoke-causer = orange smoke causer
reagent-desc-orange-smoke-causer = An unstable compound from orange slime extracts that turns straight into smoke.
reagent-name-blue-foam-causer = blue foam causer
reagent-desc-blue-foam-causer = An unstable compound from blue slime extracts that turns straight into foam.
reagent-name-regenerative-jelly = regenerative jelly
reagent-desc-regenerative-jelly = A healing jelly from purple slime extracts, a lot like omnizine.
reagent-name-advanced-mutation-toxin = advanced mutation toxin
reagent-desc-advanced-mutation-toxin = A toxin from black slime extracts. Enough of it in the blood turns you into a slime.

reagent-effect-guidebook-mutate-into-entity = { $chance ->
    [1] Turns
   *[other] turn
    } the drinker into a { $entity }
