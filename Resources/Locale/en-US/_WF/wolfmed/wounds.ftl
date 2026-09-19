# Ballistic wounds and pulling objects back out of them (W1).

wolfmed-wound-name-graze = graze
wolfmed-wound-name-gunshot = gunshot wound
wolfmed-wound-name-lodged-round = lodged round
wolfmed-wound-name-shrapnel = shrapnel wound

wolfmed-embedded-removal-start = { CAPITALIZE(THE($user)) } starts digging something out of { THE($target) }.
wolfmed-embedded-removal-start-self = { CAPITALIZE(THE($user)) } starts digging something out of { POSS-ADJ($user) } own body.
wolfmed-embedded-removal-success = Something comes free.
wolfmed-embedded-removal-partial = Something comes free. { $count } left in there.

health-analyzer-wound-embedded-short = embedded objects: { $count }

# Slash and bite wounds (W2).

wolfmed-wound-name-arterial-bleed = arterial bleed
wolfmed-wound-name-tendon-cut = severed tendon
wolfmed-wound-name-avulsion = avulsion

wolfmed-tourniquet-nowhere-to-tie = There is nothing here to tie a tourniquet around.

# Blunt trauma wounds and setting a dislocated joint (W3).

wolfmed-wound-name-crush-injury = crush injury
wolfmed-wound-name-concussion = concussion
wolfmed-wound-name-dislocation = dislocated joint
wolfmed-wound-name-organ-contusion = bruised organ

wolfmed-relocate-verb = Relocate joint
wolfmed-relocate-start = { CAPITALIZE(THE($user)) } starts forcing { THE($target) }'s joint back into place.
wolfmed-relocate-start-self = { CAPITALIZE(THE($user)) } grits { POSS-ADJ($user) } teeth and grabs { POSS-ADJ($user) } own joint.
wolfmed-relocate-success = The joint goes back in with a sickening pop.

# Burn wounds, cauterisation and washing off caustic (W4).

wolfmed-wound-name-charring = charred tissue
wolfmed-wound-name-frostbite = frostbite
wolfmed-wound-name-chemical-burn = chemical burn
wolfmed-wound-name-internal-burn = internal burns

wolfmed-cauterize-verb = Cauterise wound
wolfmed-cauterize-start = { CAPITALIZE(THE($user)) } presses { THE($tool) } against { THE($target) }'s wound.
wolfmed-cauterize-start-self = { CAPITALIZE(THE($user)) } presses { THE($tool) } against { POSS-ADJ($user) } own wound.
wolfmed-cauterize-success = The wound seals with a hiss and the smell of cooking.
wolfmed-cauterize-nothing = There is nothing bleeding there to seal.

wolfmed-chemical-burn-washed = The residue rinses away.

reagent-effect-guidebook-wash-chemical-burns =
    { $chance ->
        [1] Washes
       *[other] chance to wash
    } corrosive residue off burned tissue

# Infection, sepsis and necrosis (W5).

wolfmed-wound-name-necrosis = necrotic tissue
wolfmed-wound-stage-necrotic = dead

wolfmed-wound-cleaned = The antiseptic stings.
wolfmed-necrosis-warning = Something in your limb has gone cold and numb.
wolfmed-necrosis-dead = The flesh there has died.
wolfmed-tourniquet-loosen-verb = Loosen tourniquet
wolfmed-tourniquet-loosened = The tourniquet comes off and the bleeding starts again.

health-analyzer-wound-infection-local = infection: local
health-analyzer-wound-infection-spreading = infection: spreading
health-analyzer-wound-infection-septic = infection: septic
health-analyzer-wound-necrotic-short = NECROTIC
health-analyzer-wound-necrosis-risk-short = circulation failing
health-analyzer-wound-sepsis = [color=#d63c2c]SEPSIS[/color] - systemic infection at { $percent }%

alerts-wolfmed-sepsis-name = Sepsis
alerts-wolfmed-sepsis-desc = The infection is in your blood. You need antibiotics, and you needed them a while ago.

reagent-name-spaceacillin = spaceacillin
reagent-desc-spaceacillin = A broad-spectrum antibiotic. Clears an infected wound and pulls a septic patient back; a heavy dose is poisonous in its own right.

reagent-effect-guidebook-clean-wounds =
    { $chance ->
        [1] Cleans
       *[other] chance to clean
    } open wounds, preventing infection

reagent-effect-guidebook-treat-infection =
    { $chance ->
        [1] Clears
       *[other] chance to clear
    } infection from wounds and from the bloodstream

# Mechanical wounds (W6).

wolfmed-wound-name-dent = dent
wolfmed-wound-name-breach = chassis breach
wolfmed-wound-name-short-circuit = short circuit
wolfmed-wound-name-servo-damage = servo damage
wolfmed-wound-name-overheating = overheating

wolfmed-wound-stage-warm = warm
wolfmed-wound-stage-hot = hot
wolfmed-wound-stage-overheated = overheated

wolfmed-overheating-doused = Steam hisses off the casing.

health-analyzer-wound-overheating-short = running hot

reagent-effect-guidebook-cool-overheating =
    { $chance ->
        [1] Cools
       *[other] chance to cool
    } an overheated chassis

# Splinting a fracture (V5).

wolfmed-splint-start = { CAPITALIZE(THE($user)) } straps a splint around { THE($target) }'s limb.
wolfmed-splint-start-self = { CAPITALIZE(THE($user)) } starts strapping a splint around { POSS-ADJ($user) } own limb.
wolfmed-splint-success = The limb is braced. The bone stops shifting.
wolfmed-splint-no-part = Select the limb to splint on the targeting doll first.
wolfmed-splint-wrong-part = A splint goes around an arm or a leg. Nothing else here can be braced.
wolfmed-splint-no-fracture = There is no broken bone there.
wolfmed-splint-too-slight = The crack is too slight for a splint to hold anything.
wolfmed-splint-already-treated = That bone is already set.
