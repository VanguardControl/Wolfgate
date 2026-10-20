# Synthetics

What a synthetic-biotype body can do with itself that flesh cannot. Everything here gates on the Synthetic biotype
(`SharedWFSyntheticsSystem.IsSynthetic`): the IPC and Synth species have it natively, any other species gets it from
the character's profile. Nothing keys on a species or a mob prototype.

- **Chassis panel** (`WFActionChassisPanel`, `WFChassisPanelSystem`): the body's own window onto its parts and
  components. A part can be *isolated* (servos and sensors cut: the limb does nothing and its wounds put no pain on
  the body) and restored, or *detached* in one piece. A component can be *disconnected* once the bay is open.
  *Reboot* takes the chassis down for a few seconds and clears its sensor load and lockups. *Overclock* runs the
  servos faster for charge and heat. A mechanic opens someone else's panel with a multitool.
- **Chassis bay** (`WFChassisBaySystem`): the torso plating opened by the owner from the panel or by anyone with the
  verb. While open, components are pulled and seated by hand - no surgery - and the chassis is slower and its
  components exposed. The owner sees it on their HUD.
- **Loose limbs** (`WFSyntheticLimbSystem`): chassis limbs come off more readily than flesh and go back on by
  clicking the limb on the body. A limb put back by hand works, badly, until a cable coil repairs its coupling.
