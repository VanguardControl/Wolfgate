# UI3: the analyzer's "what is this and what do I do" text.
#
# Two keys per subject, derived from the wound prototype id or from a condition name:
#   wolfmed-treatment-short-<slug>  the tooltip, one or two lines
#   wolfmed-treatment-steps-<slug>  the procedure window, one numbered step per line
# A -mechanical variant of either is used when the part is a chassis and the advice differs.
#
# Every claim here is taken from WolfmedWoundTreatmentMatrixTest (which item reaches which wound, and what
# clears the ones no item reaches) and from the Wound treatment guidebook page. A step line must never start
# with a bracket, a dot or an asterisk: Fluent would end the pattern there.

# Analyzer chrome -------------------------------------------------------------

wolfmed-treatment-guidebook-button = Open guidebook
health-analyzer-wound-targeted-tag = targeted
health-analyzer-wound-target-part-hint = Aim at this part.
health-analyzer-wound-banner-sepsis = Sepsis
health-analyzer-wound-banner-blood-low = Low blood

# Categories ------------------------------------------------------------------

wolfmed-treatment-short-cat-cut = Open cuts. Gauze slows the bleeding, medicated sutures close them.
wolfmed-treatment-short-cat-puncture = Punctures. They bleed harder than cuts. Gauze, then medicated sutures.
wolfmed-treatment-short-cat-ballistic = Rounds and fragments. Pull anything lodged out with a hemostat first, then gauze and sutures.
wolfmed-treatment-short-cat-blunt = Blunt trauma. A bruise pack treats the bruising; fractures, dislocations and internal bleeding are separate findings.
wolfmed-treatment-short-cat-burn = Burns from heat, cold, shock or acid. Ointment or regenerative mesh, and wash acid off first.
wolfmed-treatment-short-cat-internal = Injuries inside the body. No dressing reaches these; they come out on the operating table.
wolfmed-treatment-short-cat-infection = Contaminated tissue. Antiseptic while it is local, spaceacillin once it has spread.
wolfmed-treatment-short-cat-mechanical = Chassis damage. A welding tool, a wrench or a cable coil, depending on the finding. No medicine reaches it.
wolfmed-treatment-short-cat-other = Findings that fit no other group. Click a row for what to do about it.

# Onyx per-damage-type defaults -----------------------------------------------

wolfmed-treatment-short-blunt-wound = Crushed tissue under unbroken skin. A bruise pack is the whole treatment.
wolfmed-treatment-steps-blunt-wound =
    1. Select the injured part on the doll.
    2. Apply a bruise pack to the patient, and repeat until the wound stops being listed.
    3. Scan again. A heavy blunt hit often leaves a fracture or an internal bleed behind, and neither shows as this wound.
    Do not: [color=#e06c5a]ointment, gauze and sutures do not reach blunt damage and will refuse the part.[/color]

wolfmed-treatment-short-slash-wound = An open cut. Gauze slows the bleeding, medicated sutures close it.
wolfmed-treatment-steps-slash-wound =
    1. Select the injured part on the doll.
    2. Press gauze on the part until the bleeding stops.
    3. Apply medicated sutures to close the cut.
    4. Clean it with antiseptic if it was left open for long.
    Do not: [color=#e06c5a]a bruise pack does nothing for a cut. Removing damage barely closes a wound, so expect to finish with sutures.[/color]

wolfmed-treatment-short-piercing-wound = A puncture. It bleeds harder than a cut and hurts more.
wolfmed-treatment-steps-piercing-wound =
    1. Select the injured part on the doll.
    2. Check for an embedded object first: nothing closes a wound with something still in it.
    3. Press gauze on the part until the bleeding stops.
    4. Apply medicated sutures to close it.
    Do not: [color=#e06c5a]a bruise pack does nothing for a puncture.[/color]

wolfmed-treatment-short-burn-wound = Burned tissue from heat, cold, shock or acid. Ointment or regenerative mesh.
wolfmed-treatment-steps-burn-wound =
    1. If acid caused it, wash the patient first, or the residue keeps burning.
    2. Select the burned part on the doll.
    3. Apply ointment, or regenerative mesh for a deep burn.
    4. Watch the top stage. A burn that reaches critical chars the tissue, and only a skin graft surgery clears that.
    Do not: [color=#e06c5a]a bruise pack and gauze do not reach burns.[/color]

wolfmed-treatment-short-electrical-wound = Shock damage through the part. Ointment or regenerative mesh.
wolfmed-treatment-steps-electrical-wound =
    1. Cut the power source before you touch the patient.
    2. Select the part on the doll.
    3. Apply ointment or regenerative mesh.
    4. A strong shock also leaves internal burns. Check the heart on the organs tab.
wolfmed-treatment-short-electrical-wound-mechanical = Shock damage in the chassis wiring. A cable coil repairs it.
wolfmed-treatment-steps-electrical-wound-mechanical =
    1. Cut the power source first.
    2. Select the part on the doll.
    3. Use a cable coil on the chassis until the damage clears.
    Do not: [color=#e06c5a]ointment, mesh and every other topical refuse a chassis outright.[/color]

wolfmed-treatment-short-bone-fracture-wound = A broken bone. It slows movement and hand work until it is set and mended.
wolfmed-treatment-steps-bone-fracture-wound =
    1. Select the broken limb on the doll.
    2. In the field, use a splint on an arm, hand, leg or foot. That sets the bone and cuts the penalty to a quarter.
    3. A hairline crack is too slight for a splint to hold.
    4. On the table, open an incision, set the bone with a bone setter, then mend it with bone gel.
    5. Osteogen knits a simple break without surgery, and stasizium knits any grade.
    Do not: [color=#e06c5a]set is not mended. The bone is still broken, and a fresh hard blow to the limb undoes a splint.[/color]

wolfmed-treatment-short-systemic-bleeding-wound = Blood loss with no one part to blame. Only surgery stops it.
wolfmed-treatment-steps-systemic-bleeding-wound =
    1. Watch the blood level on the left while you work.
    2. Run the Stop Bleeding surgery through an open incision.
    3. Replace what was lost once the bleeding has stopped.
    Do not: [color=#e06c5a]no dressing reaches this. There is no part to put one on.[/color]
wolfmed-treatment-short-systemic-bleeding-wound-mechanical = Fluid loss with no one part to blame. Only surgery stops it.
wolfmed-treatment-steps-systemic-bleeding-wound-mechanical =
    1. Open the chassis and run the Stop Bleeding surgery.
    2. Check every part for an unsealed breach afterwards.

wolfmed-treatment-short-internal-bleeding-wound = Bleeding inside the part. No dressing reaches it; it comes out on the table.
wolfmed-treatment-steps-internal-bleeding-wound =
    1. Note the part. The bleeding is invisible from outside and only the analyzer shows it.
    2. Open an incision on that part.
    3. Run the Stop Internal Bleeding surgery.
    4. Check the organs tab. A destroyed organ or a crushing blow is the usual cause.
    Do not: [color=#e06c5a]gauze and sutures do not reach an internal bleed.[/color]

wolfmed-treatment-short-surgical-incision-wound = An incision left open. It bleeds while it is open and it is the classic way to catch an infection.
wolfmed-treatment-steps-surgical-incision-wound =
    1. Finish the procedure you started.
    2. Close the incision. The last step of every procedure seals it with a cautery.
    3. If it is still bleeding after that, run the Stop Bleeding surgery.
    4. Clean the site with antiseptic if the patient lay open for a while.
wolfmed-treatment-short-surgical-incision-wound-mechanical = An open access panel. It leaks while it is open.
wolfmed-treatment-steps-surgical-incision-wound-mechanical =
    1. Finish the procedure you started.
    2. Seal the panel with a cautery as the last step.
    3. A chassis cannot be infected, so there is nothing to clean.

wolfmed-treatment-short-dismemberment-wound = The stump of a severed limb. It bleeds fast and nothing clots it.
wolfmed-treatment-steps-dismemberment-wound =
    1. Apply a tourniquet to the stump at once if there is anything to tie around.
    2. Get the patient to a table and run the Stop Bleeding surgery.
    3. Loosen the tourniquet as soon as the bleeding is under control. Ten minutes under one kills what is left.
    4. A severed limb keeps for five minutes. Reattach inside that, or fit a replacement part.
    Do not: [color=#e06c5a]gauze will not hold a stump. This wound carries no damage type, so no topical reaches it.[/color]
wolfmed-treatment-short-dismemberment-wound-mechanical = A severed mount. Fluid runs out of it and nothing seals itself.
wolfmed-treatment-steps-dismemberment-wound-mechanical =
    1. Apply a tourniquet to the stump if there is anything to tie around. On a chassis it is bleeding control and nothing more.
    2. Run the Stop Bleeding surgery.
    3. Fit a replacement limb.

wolfmed-treatment-short-amputation-consequence-wound = Untreated damage where a limb came off. It hides the surgeries that would attach a new part.
wolfmed-treatment-steps-amputation-consequence-wound =
    1. Stop the bleeding from the stump first.
    2. Open an incision on the stump.
    3. Run the Repair Amputation Damage surgery.
    4. The attachment surgeries appear once it is gone.
wolfmed-treatment-short-amputation-consequence-wound-mechanical = An unfinished mount where a limb came off. It hides the surgeries that would fit a new one.
wolfmed-treatment-steps-amputation-consequence-wound-mechanical =
    1. Seal any leak from the mount first.
    2. Open the mount and run the Repair Amputation Damage surgery.
    3. The attachment surgeries appear once it is gone.

wolfmed-treatment-short-medical-scar-wound = A scar. It is a record of treatment that finished, not an injury.
wolfmed-treatment-steps-medical-scar-wound =
    1. Nothing to do. A scar carries no pain, no bleeding and no loss of function.
    2. Nothing removes one either. Treat the findings above it and ignore this row.

# Onyx chassis defaults -------------------------------------------------------

wolfmed-treatment-short-ipc-mechanical-damage-wound = The total damage on an IPC chassis. A welding tool, a wrench or a cable coil brings it down.
wolfmed-treatment-steps-ipc-mechanical-damage-wound =
    1. Pull out anything embedded first. Nothing repairs a part with an object still in it.
    2. Select the damaged part on the doll.
    3. Use a welding tool on the chassis for dents and breaches. A wrench does dents too and needs no fuel.
    4. Use a cable coil for shock damage.
    Do not: [color=#e06c5a]no topical, reagent or medicine reaches a chassis at all.[/color]

wolfmed-treatment-short-cybernetic-mechanical-damage-wound = The total damage on a cybernetic limb. A welding tool, a wrench or a cable coil brings it down.
wolfmed-treatment-steps-cybernetic-mechanical-damage-wound =
    1. Pull out anything embedded first.
    2. Select the limb on the doll.
    3. Use a welding tool, or a wrench for dents, and a cable coil for shock damage.
    Do not: [color=#e06c5a]a cybernetic limb is repaired, not treated. Medicine does nothing for it even on a human patient.[/color]

wolfmed-treatment-short-cybernetic-frame-fracture-wound = A bent frame in a cybernetic limb. It slows movement and hand work until it is straightened.
wolfmed-treatment-steps-cybernetic-frame-fracture-wound =
    1. Open the limb.
    2. Run the Mend Fracture surgery: set the frame with a bone setter, then mend it with bone gel.
    3. Seal it.

# Onyx non-human tissue -------------------------------------------------------

wolfmed-treatment-short-slime-blunt-wound = A blunt hit on slime tissue. A bruise pack works exactly as it does on flesh.
wolfmed-treatment-steps-slime-blunt-wound =
    1. Select the injured part on the doll.
    2. Apply a bruise pack until the wound stops being listed.

wolfmed-treatment-short-slime-slash-wound = A cut in slime tissue. Gauze, then medicated sutures.
wolfmed-treatment-steps-slime-slash-wound =
    1. Press gauze on the part until the bleeding stops.
    2. Apply medicated sutures to close it.

wolfmed-treatment-short-slime-piercing-wound = A puncture in slime tissue. Gauze, then medicated sutures.
wolfmed-treatment-steps-slime-piercing-wound =
    1. Check for an embedded object and pull it out first.
    2. Press gauze on the part, then close it with medicated sutures.

wolfmed-treatment-short-slime-burn-wound = A burn on slime tissue. Ointment or regenerative mesh.
wolfmed-treatment-steps-slime-burn-wound =
    1. Wash the patient first if acid caused it.
    2. Apply ointment, or regenerative mesh for a deep burn.

wolfmed-treatment-short-plant-blunt-wound = A blunt hit on plant tissue. A bruise pack treats it.
wolfmed-treatment-steps-plant-blunt-wound =
    1. Select the injured part on the doll.
    2. Apply a bruise pack until the wound stops being listed.

wolfmed-treatment-short-plant-slash-wound = A cut in plant tissue. It loses sap the way flesh loses blood.
wolfmed-treatment-steps-plant-slash-wound =
    1. Press gauze on the part until the loss stops.
    2. Apply medicated sutures to close it.

wolfmed-treatment-short-plant-piercing-wound = A puncture in plant tissue. Gauze, then medicated sutures.
wolfmed-treatment-steps-plant-piercing-wound =
    1. Check for an embedded object and pull it out first.
    2. Press gauze on the part, then close it with medicated sutures.

wolfmed-treatment-short-plant-burn-wound = A burn on plant tissue. Ointment or regenerative mesh.
wolfmed-treatment-steps-plant-burn-wound =
    1. Wash the patient first if acid caused it.
    2. Apply ointment, or regenerative mesh for a deep burn.

# W1 ballistic ----------------------------------------------------------------

wolfmed-treatment-short-wolfmed-graze-wound = A round that only caught the limb. It bleeds briefly and stops on its own.
wolfmed-treatment-steps-wolfmed-graze-wound =
    1. Nothing is needed in a fight. The bleed stops by itself.
    2. Press gauze on the part if you want it closed now.
    3. Clean it with antiseptic if the patient will be a while. An open wound still catches an infection.

wolfmed-treatment-short-wolfmed-gunshot-wound = A round that went through. Good bleeding, real pain, and a high infection risk.
wolfmed-treatment-steps-wolfmed-gunshot-wound =
    1. Check the part for a lodged round first. Nothing closes a wound with one still in it.
    2. Press gauze on the part. That slows the bleeding and cuts the infection rate to about a seventh.
    3. Apply medicated sutures to close it. A sutured wound stops the infection clock outright.
    4. Clean the site with antiseptic if it was left open: ethanol, bleach or spaceacillin on the skin.
    Do not: [color=#e06c5a]a bruise pack does nothing here.[/color]

wolfmed-treatment-short-wolfmed-lodged-round-wound = A round still in the part. It never clots, never closes, and blocks every other treatment on that part.
wolfmed-treatment-steps-wolfmed-lodged-round-wound =
    1. Select the part on the targeting doll.
    2. Use a hemostat or tweezers on the patient and wait out the bar. That pulls the round out cleanly.
    3. Any sharp item works instead, a knife, a scalpel or a shard, but it is slower, it hurts, and it leaves a fresh dirty cut that infects faster.
    4. Only then treat what is underneath with gauze and medicated sutures.
    Do not: [color=#e06c5a]nothing at all works while an object is in the part. Not gauze, not sutures, not surgery.[/color]

wolfmed-treatment-short-wolfmed-shrapnel-wound = Fragments in the part. Each one blocks treatment, and the analyzer counts how many are left.
wolfmed-treatment-steps-wolfmed-shrapnel-wound =
    1. Select the part on the targeting doll.
    2. Pull one fragment out with a hemostat or tweezers. Repeat until the embedded count reaches zero.
    3. Treat what is underneath with gauze and medicated sutures.
    Do not: [color=#e06c5a]do not dig with a knife unless you have to. It is slower, it hurts, and it leaves a dirty cut.[/color]

# W2 slash and bite -----------------------------------------------------------

wolfmed-treatment-short-wolfmed-arterial-bleed-wound = A cut artery. It bleeds several times faster than anything else and it never clots.
wolfmed-treatment-steps-wolfmed-arterial-bleed-wound =
    1. On an arm, hand, leg or foot, apply a tourniquet. That stops the flow outright.
    2. Only then will medicated sutures close the wound underneath.
    3. In the torso or the head there is nothing to tie around. Run the Repair Severed Artery surgery, which clamps the vessel with a hemostat and then sutures it.
    4. With nothing else to hand, hold something hot and use the Cauterise wound verb. It costs a deep burn and a great deal of pain.
    5. Take the tourniquet off as soon as the wound is closed. Ten minutes under one kills the limb.
    Do not: [color=#e06c5a]gauze only slows an arterial bleed. It buys minutes, it will not take the wound's severity, and it will not close it.[/color]

wolfmed-treatment-short-wolfmed-tendon-cut-wound = A severed tendon. The limb stays slow or lame until it is repaired on the table.
wolfmed-treatment-steps-wolfmed-tendon-cut-wound =
    1. Open an incision on the limb.
    2. Run the Repair Severed Tendon surgery.
    3. Seal the incision.
    Do not: [color=#e06c5a]no topical reaches a tendon. Gauze and sutures close the cut over it and change nothing underneath.[/color]

wolfmed-treatment-short-wolfmed-avulsion-wound = A bite that tore tissue away. It bleeds, it almost always scars, and it infects readily.
wolfmed-treatment-steps-wolfmed-avulsion-wound =
    1. Press gauze on the part until the bleeding stops.
    2. Apply medicated sutures to close it.
    3. Clean it with antiseptic. A bite is one of the dirtiest wounds there is.
    4. Expect a scar. Scars are a record, not an injury.

# W3 blunt trauma -------------------------------------------------------------

wolfmed-treatment-short-wolfmed-crush-injury-wound = Crushed tissue. It slows the limb, seeps at its worst, and often starts an internal bleed.
wolfmed-treatment-steps-wolfmed-crush-injury-wound =
    1. Apply a bruise pack to the part until the wound clears.
    2. Scan again for internal bleeding, a fracture and a dislocated joint. One heavy hit can leave all three.

wolfmed-treatment-short-wolfmed-concussion-wound = A concussion. Knockdowns, blurred sight and slurred speech until it fades.
wolfmed-treatment-steps-wolfmed-concussion-wound =
    1. There is no treatment. It fades on its own.
    2. It fades four times faster while the patient sleeps or lies buckled in a medical bed. Put them in one.
    3. Check the head for a fracture while you are there.
    Do not: [color=#e06c5a]painkillers hide what it hurts and do nothing for the concussion itself.[/color]

wolfmed-treatment-short-wolfmed-dislocation-wound = A joint out of its socket. It costs the limb its use exactly as a fracture does.
wolfmed-treatment-steps-wolfmed-dislocation-wound =
    1. Select the limb on the targeting doll.
    2. Alt-click the patient and choose Relocate joint.
    3. On yourself it takes two and a half times as long and hurts twice as much. Get someone else to do it.
    Do not: [color=#e06c5a]no item sets a joint. A splint is for a broken bone, not a popped one.[/color]

wolfmed-treatment-short-wolfmed-organ-contusion-wound = A bruised organ under the bruising. The wound and the organ are treated separately.
wolfmed-treatment-steps-wolfmed-organ-contusion-wound =
    1. Apply a bruise pack to the chest for the wound itself.
    2. Read the organ's condition on the organs tab.
    3. Restore the organ with its own surgery. A bruise pack does nothing for what is under the ribs.

# W4 burns --------------------------------------------------------------------

wolfmed-treatment-short-wolfmed-charring-wound = Charred, dead tissue left by a burn that reached its critical stage. The limb does not work and it can rot.
wolfmed-treatment-steps-wolfmed-charring-wound =
    1. Open an incision on the part.
    2. Graft it with a skin graft. Skin grafts are in the burn kit and the surgical crate, come out of the medical vendor, and print on a medical lathe.
    3. Seal the incision.
    4. Watch the necrosis flag while you work. Charred tissue is already dying.
    Do not: [color=#e06c5a]nothing in a medkit reaches charred tissue. It carries no damage type, so ointment and mesh both refuse it.[/color]

wolfmed-treatment-short-wolfmed-frostbite-wound = Frozen tissue. It numbs the part, so the patient under-reports it, and deep frostbite risks rotting.
wolfmed-treatment-steps-wolfmed-frostbite-wound =
    1. Get the patient out of the cold first.
    2. Apply ointment or regenerative mesh to the part.
    3. Trust the analyzer over the patient. A numb limb does not hurt.
    4. Watch the necrosis flag. A limb frozen through is already dying.

wolfmed-treatment-short-wolfmed-chemical-burn-wound = Acid still on the skin, eating the part. Wash it off first or the damage comes straight back.
wolfmed-treatment-steps-wolfmed-chemical-burn-wound =
    1. Rinse the patient. Water from any source works: a splash, a spray bottle, a fire extinguisher, a puddle, space cleaner.
    2. Only then apply ointment or regenerative mesh to the burn.
    Do not: [color=#e06c5a]treating the burn before the wash is wasted ointment. The residue keeps eating the part.[/color]

wolfmed-treatment-short-wolfmed-internal-burn-wound = Burns inside the body from a strong shock. It damages the heart and locks the muscles for a moment.
wolfmed-treatment-steps-wolfmed-internal-burn-wound =
    1. Cut the power source before you touch the patient.
    2. Apply ointment or regenerative mesh to the part.
    3. Check the heart on the organs tab and run the Heal Heart surgery if it has lost condition.

# W5 time ---------------------------------------------------------------------

wolfmed-treatment-short-wolfmed-necrosis-wound = Dead tissue. It is permanent, the limb works badly, and it keeps the patient septic while it is attached.
wolfmed-treatment-steps-wolfmed-necrosis-wound =
    1. Nothing treats it. No reagent, no topical and no surgery brings dead tissue back.
    2. Amputate the part and fit a replacement.
    3. Give spaceacillin for the sepsis it has already caused.
    Do not: [color=#e06c5a]leaving it attached keeps the infection running. It is the source.[/color]

# W6 mechanical ---------------------------------------------------------------

wolfmed-treatment-short-wolfmed-dent-wound = Blunt force on a chassis. Cosmetic until it is deep, then it slows the limb.
wolfmed-treatment-steps-wolfmed-dent-wound =
    1. Select the part on the doll.
    2. Use a welding tool on the chassis, or a wrench, which needs no fuel and does nothing else.
    3. Repeat until the analyzer stops listing it.

wolfmed-treatment-short-wolfmed-breach-wound = A hole in the chassis leaking coolant and hydraulic fluid. It never clots.
wolfmed-treatment-steps-wolfmed-breach-wound =
    1. Pull out anything embedded first with a hemostat or tweezers. Nothing seals a breach with an object in it.
    2. Weld the breach shut with a welding tool.
    3. Repeat until the leak stops being listed.
    Do not: [color=#e06c5a]a wrench and a cable coil do not seal a breach, and no dressing reaches a chassis.[/color]

wolfmed-treatment-short-wolfmed-short-circuit-wound = Shorted wiring. The frame locks up and throws sparks each time it worsens.
wolfmed-treatment-steps-wolfmed-short-circuit-wound =
    1. Cut the power source first.
    2. Select the part on the doll.
    3. Use a cable coil on the chassis until it clears.
    Do not: [color=#e06c5a]a welding tool does not reach shorted wiring.[/color]

wolfmed-treatment-short-wolfmed-servo-damage-wound = A cut servo run. The limb stays slow or lame until the run behind the panel is replaced.
wolfmed-treatment-steps-wolfmed-servo-damage-wound =
    1. Open the limb.
    2. Run the Replace Damaged Servo surgery. Its tool is a cable coil.
    3. Seal it.
    Do not: [color=#e06c5a]welding the panel shut does nothing for the servo underneath.[/color]

wolfmed-treatment-short-wolfmed-overheating-wound = The part is running too hot to work properly. Time and cold are the only treatments.
wolfmed-treatment-steps-wolfmed-overheating-wound =
    1. No tool reaches it. The part sheds heat on its own.
    2. It cools faster in a cold room or under anything cold.
    3. Fastest of all, hose the patient down with water or a fire extinguisher.

# Conditions ------------------------------------------------------------------

wolfmed-treatment-short-cond-bleeding = This part is losing blood. Gauze slows it, medicated sutures close it, a tourniquet stops a limb outright.
wolfmed-treatment-steps-cond-bleeding =
    1. Press gauze on the part to slow the bleeding.
    2. Apply medicated sutures to close the wound and stop it.
    3. If it will not stop and it is a limb, apply a tourniquet and get the patient to a table.
    4. Take the tourniquet off within ten minutes, or the limb dies under it.
    Do not: [color=#e06c5a]a bruise pack does not stop bleeding, and gauze only slows an arterial bleed.[/color]
wolfmed-treatment-short-cond-bleeding-mechanical = The chassis is leaking coolant and hydraulic fluid. A welding tool seals it.
wolfmed-treatment-steps-cond-bleeding-mechanical =
    1. Pull out anything embedded first.
    2. Weld the breach shut with a welding tool.
    3. A chassis breach never clots. It runs until it is sealed.

wolfmed-treatment-short-cond-internal-bleeding = Blood is being lost inside this part. Nothing you can put on the skin reaches it.
wolfmed-treatment-steps-cond-internal-bleeding =
    1. Get the patient to a table.
    2. Open an incision on this part.
    3. Run the Stop Internal Bleeding surgery.
    4. Check the organs tab. A destroyed organ is the usual cause.

wolfmed-treatment-short-cond-fracture = A broken bone in this part. It slows movement and hand work until it is set and mended.
wolfmed-treatment-steps-cond-fracture =
    1. In the field, splint an arm, hand, leg or foot. The break goes to set, which is a quarter of the penalty.
    2. A hairline crack is too slight for a splint to hold.
    3. On the table, open an incision, set the bone with a bone setter, then mend it with bone gel.
    4. Osteogen knits a simple break without surgery, and stasizium knits any grade.
    Do not: [color=#e06c5a]set is not mended, and a fresh hard blow to the limb undoes a splint.[/color]
wolfmed-treatment-short-cond-fracture-mechanical = The frame in this part is bent. It slows movement and hand work until it is straightened.
wolfmed-treatment-steps-cond-fracture-mechanical =
    1. Open the limb.
    2. Set the frame with a bone setter, then mend it with bone gel.
    3. Seal it.

wolfmed-treatment-short-cond-clotting = How the bleeding here is going: clotting in progress, stopped, or only partly stopped.
wolfmed-treatment-steps-cond-clotting =
    1. Clotting in progress means the wound will stop shortly on its own. Watch it rather than spending gauze.
    2. Bleeding stopped means this part is no longer losing blood, but the wound is still open and can still infect.
    3. Partial hemostasis means one wound here has stopped and another has not. Find the one still running.
    4. An arterial bleed and a wound with an object in it never clot at all.
wolfmed-treatment-short-cond-clotting-mechanical = How the leak here is going: sealant setting, sealed, or only partly sealed.
wolfmed-treatment-steps-cond-clotting-mechanical =
    1. Sealant setting means the leak will stop shortly.
    2. Leak sealed means this part is no longer losing fluid.
    3. Partially sealed means one breach here is still open. Weld it.

wolfmed-treatment-short-cond-embedded = Objects still in this part. Each one blocks every treatment on the part until it is out.
wolfmed-treatment-steps-cond-embedded =
    1. Select the part on the targeting doll.
    2. Use a hemostat or tweezers on the patient. One object comes out per attempt.
    3. Repeat until the count reaches zero, then treat the wound underneath.
    4. A sharp item works instead but is slower, hurts, and leaves a dirty cut.

wolfmed-treatment-short-cond-infection-local = The wound itself has gone bad. It hurts, and it reopens faster than a dressing closes it.
wolfmed-treatment-steps-cond-infection-local =
    1. Apply antiseptic to the skin: ethanol, bleach or spaceacillin. That drains a local infection over the next minute.
    2. Close the wound. Sutures or cautery stop the infection clock outright.
    3. Gauze alone cuts the rate to about a seventh, and a tourniquet halves it.
    Do not: [color=#e06c5a]antiseptic does nothing once the infection has spread past the wound.[/color]

wolfmed-treatment-short-cond-infection-spreading = The infection has left the wound. The patient runs a fever and takes toxin damage.
wolfmed-treatment-steps-cond-infection-spreading =
    1. Give spaceacillin in the bloodstream. Antiseptic on the skin no longer helps.
    2. Bottles come from the medical vendor and the medical supplies crate. Chemistry makes it from cryptobiolin and inaprovaline in equal parts.
    3. Close or clean the wound underneath, or it will simply infect again.
    Do not: [color=#e06c5a]more than about 25 units of spaceacillin is poisonous in its own right. Treat and stop.[/color]

wolfmed-treatment-short-cond-infection-septic = The infection is in the blood. Sepsis kills if nobody acts.
wolfmed-treatment-steps-cond-infection-septic =
    1. Give spaceacillin now. It clears every wound at once and pulls the sepsis back.
    2. Find the source. Sepsis keeps growing while a spreading wound or a necrotic part is still there.
    3. A necrotic limb has to come off. Nothing else stops it feeding the sepsis.
    Do not: [color=#e06c5a]more than about 25 units of spaceacillin is poisonous in its own right. Treat and stop.[/color]

wolfmed-treatment-short-cond-necrosis = The tissue here is dead. It is permanent and it keeps the patient septic.
wolfmed-treatment-steps-cond-necrosis =
    1. Amputate the part and fit a replacement.
    2. Give spaceacillin for the sepsis it has caused.
    Do not: [color=#e06c5a]no reagent, topical or surgery brings dead tissue back.[/color]

wolfmed-treatment-short-cond-necrosis-risk = Circulation here is failing. The part dies if nothing changes.
wolfmed-treatment-steps-cond-necrosis-risk =
    1. A tourniquet is a ten-minute clock. Suture what is under it, or use the Loosen tourniquet verb and accept the bleeding.
    2. A limb frozen through or burned to charring is already dying. Warm it, treat the burn, or graft the charring now.
    3. A severed limb keeps for five minutes. Reattach inside that or it goes back on dead.
    4. The patient feels the limb go cold before the end. Believe them.

wolfmed-treatment-short-cond-overheating = The part is running too hot to work properly. Time and cold are the only treatments.
wolfmed-treatment-steps-cond-overheating =
    1. No tool reaches it.
    2. Move the patient somewhere cold, or hold something cold against the part.
    3. Water or a fire extinguisher cools it in one step.

wolfmed-treatment-short-cond-impaired = This part is working at reduced effectiveness. Treat what is listed on this card and it comes back.
wolfmed-treatment-steps-cond-impaired =
    1. Work through the findings on this card. Fractures, crush injuries, dislocations, severed tendons and damaged servos all cost function.
    2. A fracture shadows the others. Mend the bone first, then scan again.

wolfmed-treatment-short-cond-disabled = This part does not work at all. Something on this card has to be fixed before it will.
wolfmed-treatment-steps-cond-disabled =
    1. Mend the fracture first. A broken bone is the usual cause and it hides everything else.
    2. Relocate a dislocated joint with the Relocate joint verb.
    3. Severed tendons and damaged servos need their own surgeries.
    4. Dead tissue never comes back. That part has to be replaced.

wolfmed-treatment-short-cond-unavailable = The part is missing or detached. Nothing on it can be treated while it is off the body.
wolfmed-treatment-steps-cond-unavailable =
    1. Stop the bleeding from the stump: a tourniquet, then the Stop Bleeding surgery.
    2. Run Repair Amputation Damage on the stump before you attach anything.
    3. A severed limb keeps for five minutes. Put it back inside that, or fit a replacement part.

wolfmed-treatment-short-cond-sepsis = Systemic infection. It grows while any source is alive and it kills if it is left alone.
wolfmed-treatment-steps-cond-sepsis =
    1. Give spaceacillin now. It clears every wound at once and pulls the sepsis back.
    2. Find the source: a spreading infection on some part, or dead tissue.
    3. Amputate a necrotic limb. While it is attached the sepsis keeps climbing.
    Do not: [color=#e06c5a]more than about 25 units of spaceacillin is poisonous in its own right. Treat and stop.[/color]

wolfmed-treatment-short-cond-blood-low = The patient is dangerously low on blood. Stop the loss first, then replace what is gone.
wolfmed-treatment-steps-cond-blood-low =
    1. Work down this tab and stop every bleeding part. Replacing blood while it is still running out is wasted.
    2. Gauze slows a bleed, medicated sutures close it, a tourniquet stops a limb outright.
    3. Then give a bloodpack to put back what was lost.
    Do not: [color=#e06c5a]a tourniquet left on for ten minutes kills the limb. Close the wound under it and take it off.[/color]
