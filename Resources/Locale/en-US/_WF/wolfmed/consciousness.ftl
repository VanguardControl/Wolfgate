# Consciousness and painkillers (CONSC).

alerts-wolfmed-downed-name = Downed
alerts-wolfmed-downed-desc = You are on the floor and can only reach yourself. You can crawl, talk and use what you are already carrying. You cannot stand, fight, shoot, pull or touch anything else until you are steadier.

# Painkiller tiers, as the analyzer and the guidebook name them.

wolfmed-pain-relief-tier-none = none
wolfmed-pain-relief-tier-weak = weak
wolfmed-pain-relief-tier-strong = strong
wolfmed-pain-relief-tier-stimulant = stimulant
wolfmed-pain-relief-tier-emergency = emergency

reagent-effect-guidebook-wolfmed-pain-relief =
    { $chance ->
        [1] Relieves
       *[other] chance to relieve
    } pain at the { $tier } tier, worth { $strength } pain. Treats nothing.

# Analyzer banners.

health-analyzer-wound-pain-relief = Pain relief: { $tier }, { $seconds }s left. Masks pain only; treats nothing.
health-analyzer-wound-sedation = Sedation { $percent }%. Breathing is depressed past 60%.
health-analyzer-wound-banner-pain-relief = Pain relief
health-analyzer-wound-banner-sedation = Sedation

# Wolfmed's own painkiller ladder.

reagent-name-wolfmed-analgesic = analgesic
reagent-desc-wolfmed-analgesic = A plain painkiller. Enough to get a wounded crewman back on their feet, never enough to keep them awake.

reagent-name-wolfmed-opiate = opiate
reagent-desc-wolfmed-opiate = A heavy painkiller. It will walk a patient off the field on a broken leg, and it will stop them breathing if you keep going.

reagent-name-wolfmed-stim = stim
reagent-desc-wolfmed-stim = Thirty seconds of standing upright, bought against the next thirty. Does nothing for blood or air.
