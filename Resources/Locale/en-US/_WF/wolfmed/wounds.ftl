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
