# Wolfmed death redesign plan

**Basis.** This plan draws on:
- `plan/p7/INPUT.md`: the owner's goal, the external review, the approved fixes and the standing decisions;
- `plan/p6/DEATH-RUNDOWN.md` ("the rundown"), read at `1cdd69c7c2`;
- `Docs/Wolfmed/DECISIONS.md`;
- the code on branch `Wolfmed` at `5464fb137e`. The only commit since the rundown changes the targeting UI and the analyzer window, so the rundown's line numbers still hold.

I re-checked the load-bearing claims against the code: the breathing rule, `Kill`, the ghost branch, the sedation model, the defib gate, the non-wound-host species and the pod's missing return prompt. For this revision I also checked the `MobThresholdsComponent` access rule, the brainless branch of `WolfmedLifeSystem.Tick`, the sedation lines, the crit-action grant path, the autodoc alarm enum, the `ghost` command path and the species file paths.

**Conventions.**
- Citations use the rundown's short file names (its Appendix B). Files outside that key are given as full repo paths.
- Every number I propose is a **starting value for playtesting**, written as *(start: value, `where it lives`)*. **new** marks a CVar or prototype field that does not exist yet. **existing** marks one that does.
- "Derived" means worked out from the code's own rates, not measured.
- "To confirm" names the file to check before building.
- **OD1–OD22** are this plan's owner decisions (§13). **D-numbers** (D1–D35) always mean the entries in `Docs/Wolfmed/DECISIONS.md`, for example DECISIONS D27 (the accumulator).
- **[OD1 wording]** marks player-facing text that depends on the open terminology decision OD1. If the owner picks OD1 (a), those strings say "brain death" instead. Only locale changes.

---

## Summary

**Goal.** Losing a fight should usually leave you crawling, talking and treating yourself rather than dying, with helplessness kept short and always explained. Every cause of incapacity has one visible reason, first aid that buys time, a definitive fix, and a predictable recovery.

**The new state ladder**

| State | Mob state | The player can | Breathes | Typical helpless time |
|---|---|---|---|---|
| **Up** | Alive | everything | yes | 0 |
| **Downed** (crawling) | Alive | crawl, talk, radio, use carried items on self, pick up items within reach, Call for help | yes | 0 (Downed is not helpless) |
| **Faint** | Critical | hear; see the reason and "you will come round shortly" | **yes** | ≤ 20 s |
| **Unconscious** | Critical | hear; see the reason and what will wake them | **yes**, unless §4 lists a suppressor | until the cause is fixed (per-cause budget, §2.3) |
| **Cardiac arrest** (Dying) | Critical, looks dead | hear; Succumb and Last Words, both honest | no | the rescue window, about 4 min untreated (§2.3 explains why it stays long) |
| **Catastrophic brain injury** (CBI) [OD1 wording] | Dead | ghost; told exactly how they can return | no | until surgery and a defib |
| **Permanent loss** | Dead | ghost; told they cannot return | — | — (rot, or no brain left; Wolfmed adds nothing) |

IPCs use the same rungs under machine names: Downed, **Shutdown** (Unconscious), **Thermal shutdown** (Dying, the core-heat route) and **Core failure** (CBI). They have no cardiac arrest (§3.11).

**The five biggest changes**

1. **Unconscious bodies breathe.** One marked hook replaces the upstream "Critical does not breathe" rule, and the brain's "not breathing" input reads real suffocation, not damage totals. This removes the hidden 4.3-minute clock (P2, P7).
2. **Pain knocks you out briefly, never for long.** A pain faint lasts at most 20 s. A strong painkiller ends it. Sustained pain keeps you Downed, and pain has no lethal route.
3. **Every state names its cause.** Per-cause alerts, transition messages and IPC HUD lines for the patient; a vitals block on the analyzer and truthful examine lines for the medic. Succumb and Last Words appear only while Dying and do exactly what their dialog says.
4. **Caps limit bookkeeping, never consequences.** The body-wide 600 cap becomes a per-part ceiling. Overflow still grows wounds, fluid loss and organ damage. Burns get an explicit fluid-loss route.
5. **Predictable revival, organs and species.** A shock leaves a breathing patient with a named remaining problem and a grace window to fix it. Organ damage follows hit size and location, not rolls. A conformance test makes every species' disable, kill and restore answers deliberate.

**Milestones** (§12)

| # | Name | Core content | Owner decisions needed first |
|---|---|---|---|
| M1a | The honest loop | breathing, the suffocation gate, causes and alerts, pain faint, honest Succumb, stable revival, IPC shutdown reasons, pickup, Call for help | OD1, OD2, OD3, OD4, OD5, OD6, OD9, OD21, OD22 |
| M1b | Burns and caps | per-part ceiling, overflow event, burn fluid loss and dressing, uncapped fire measurement | OD11, OD12 |
| M2 | Arrest, revival and medic information | vitals block, explanation card, positronic core repair, restart hook, sedation model, executions and suicide, wait as a ghost | OD7 (c), OD8, OD10, OD14, OD17, OD20 |
| M3 | Consequences keep mattering | deterministic organs, graded organ effects, brain and core injury input, stumps, barotrauma, blast head | OD15 |
| M4 | Species and IPC death | conformance test, organ data, non-wound-host species (both Synth branches), circulatory collapse, IPC core-heat route | OD16, OD10 |
| M5 | Remaining causes | toxins, radiation, cold, heat | OD13 |
| M6 | Leftovers | do-after interruption, routing arguments, inert tunables, DECISIONS.md corrections | OD18 |

---

## 1. Goal and design principles

### 1.1 The goal (the owner's)

"Losing a fight doesn't have to mean dying." The injured player crawls, calls for help, treats themselves and buys time. Painkillers restore function without repairing anything, so getting someone moving and making them safe are separate jobs. A recoverable arrest, followed by a brain injury that gets worse, tells medics whom to treat first.

### 1.2 The medical loop

Every cause must pass all four steps. Milestone 1 tests the loop for bleeding, burns, pain unconsciousness, oxygen deprivation and IPC shutdown.

| Step | What it means here |
|---|---|
| **1. Understandable incapacity** | One named cause at a time. The patient is told it as a symptom. A medic can find it by examining or with the analyzer. |
| **2. First aid buys time** | A quick field action slows or pauses the worsening without curing it. |
| **3. Definitive treatment** | Fixing that cause ends the state. Treating something else does not. |
| **4. Predictable recovery** | Once the cause is gone, the patient improves at a stated rate through the same rungs in reverse. Any leftover problem is visible. |

### 1.3 Terms used in this plan

| Term | Meaning |
|---|---|
| **Wound host** | A body with `WoundHostComponent`. Wolfmed, not damage totals, decides its state (MobThresholdSystem.cs:340-342). |
| **Input** | One thing consciousness reads: pain, blood, legs, stim crash, or a *pressure*. |
| **Pressure** | A 0–1 level another system writes under a key such as `arrest`, `hypoxia`, `sedation` or `shutdown` (SharedWolfmedConsciousnessSystem.cs:40). 0.7 or more means Downed (`PressureDownShare`, WolfmedConsciousnessSystem.cs:41, 156). 1 means Unconscious. |
| **Oxygenation** | The brain's oxygen, from 1 down to 0 (WolfmedLifeSystem.cs:243-261). |
| **Drain** | The rate at which oxygenation falls. `WolfmedLifeSystem.DrainRate` takes the worst of arrest, breathing, low blood and sepsis (WolfmedLifeSystem.cs:269-300). |
| **Cause** | **New.** The input that currently sets the body's state. Alerts, messages, the HUD, the analyzer and examine all read it. |
| **Route** | A named process that makes one cause worse at a stated rate, driven by one measurable quantity (blood volume, oxygenation, toxin load, core temperature). A route never reads another route's bookkeeping. |
| **Faint** | **New.** Unconsciousness with a fixed maximum length. You come round Downed even if the cause remains. |
| **Unconscious** | Unconsciousness that lasts as long as its bodily cause, such as blood at or below 35%. It ends when the cause is fixed. |
| **Dying** | Only cardiac arrest (organics) and **thermal shutdown** (IPC: the core-heat route is destroying the core, §3.11). Both are helpless states that end in death with no further event. Only Dying offers Succumb and Last Words (OD3). |
| **Helpless time** | Time the player can neither move nor speak: Faint, Unconscious, arrest, Shutdown, thermal shutdown. Downed does not count. Dead does not count either, because the ghost has been told how to return. |
| **Catastrophic brain injury (CBI)** [OD1 wording] | The brain organ at 0 HP. Today's "brain death" (`MobState.Dead`, OrganHealthSystem.cs:45-53), revivable by brain repair surgery and then a defib. The name is decision OD1. |
| **Permanent loss** | The character cannot come back: the body has rotted, or no brain is left to repair (OD1). |
| **Bookkeeping cap** | A limit on a stored number while the harm it stands for keeps having effects. |
| **Consequence cap** | A limit that makes further harm do nothing. This plan removes all of these. |
| **Marked hook** | A small edit in upstream, Onyx or Shitmed code, tagged `// WOLFGATE`, that hands off to `_WF/Wolfmed`. A **marked YAML line** is the same for prototypes. |
| **Scenario test** | An integration test that runs one injury from start to finish: injury, first aid, treatment, recovery. |
| **AVPU** | The standard first-aid consciousness scale: **A**lert, responds to **V**oice, responds to **P**ain, **U**nresponsive (§5.5). |

### 1.4 Principles

| # | Principle | What it rules out |
|---|---|---|
| A | **Downed is the default hurt state.** Recoverable injury leaves you Downed and able to act. | pain holding a body unconscious indefinitely (rundown §3.3) |
| B | **Full unconsciousness is kept for causes that warrant it.** Each such cause has a bounded length or ends when a rescuer fixes it. | minutes motionless under a generic "crit" display (P5) |
| C | **One cause, one explicit route.** No cause borrows another's hidden timer. | the shared suffocation clock (P2); Bloodloss read as not breathing (P7) |
| D | **Symptoms for the patient, vital signs for the medic.** Medics get words plus the few numbers they act on: blood %, temperature, sedation %, and the existing arrest countdown (wounds.ftl:156). | "severely injured and unconscious" for every cause; floods of internal timers |
| E | **Continuing harm always matters.** Caps may limit bookkeeping or pain only. | fire stopping at 600; a chest ignoring bullets at 250 (P3, P8) |
| F | **Only the dying may let go, and every choice to leave the body is honest.** | Succumb that neither kills nor tells the truth (P4) |
| G | **Species differences are deliberate, written down and tested.** | immunities that come from missing organ data (P9) |
| H | **Triage follows visible signs** (§1.5). | a priority order that depends on hidden numbers |
| I | **Owner code rules** (INPUT.md §4): Onyx under `_Onyx` with marks, glue under `_WF/Wolfmed`, minimal marked hooks, one subscriber per component+event pair, data in YAML and CVars, integration tests for logic. | — |

### 1.5 Triage from what a medic can see

After M2, a medic should be able to order patients from signs alone. The M2 playtest checks this.

| Priority | What the medic sees | Why | First action |
|---|---|---|---|
| 1 | Looks dead, no pulse; analyzer shows the arrest countdown | recoverable now; brain tissue loss starts in about 73 s | shock; CPR while it charges |
| 2 | Spurting or pooling blood, pale, weak fast pulse | the blood route is minutes from arrest | tourniquet or gauze, then transfuse |
| 3 | Pulse present, gasping or not breathing, blue lips | the oxygen route is running | air, internals; find the cause |
| 4 | Unconscious, breathing, pulse present, no active route listed | stable for now | treat the named cause |
| 5 | Downed and talking | can help themselves | tell them what to use; come back |
| 6 | CBI (dead) | nothing worsens for 60 min (rot) | surgery once the living are stable |

---

## 2. The target model

### 2.1 States

| State | Mob state | How you get here (full rules in §3) | Leaves on |
|---|---|---|---|
| **Up** | Alive | — | — |
| **Downed** | Alive + `WolfmedDownedComponent` | Any of: body pain ≥ 128.25; blood ≤ 50%; oxygenation ≤ 0.54; sedation ≥ 0.88; both legs gone; stim crash; brain injury (brain HP < 25%, M3); radiation sickness (M5); toxin load (M5); cold or heat (M5). IPC: pain, oil ≤ 50%, core HP < 25% (M3). | every input back under 0.9 of its line (`wolfmed.consc_hysteresis` existing, WolfmedCVars.cs:152-153), with the existing 2 s dwell |
| **Faint** | Critical | summed pain crosses the faint line while the faint is armed (§3.1); a heavy head blow (§3.6, M3) | its timer ends, or (pain only) a strong or emergency painkiller; then Downed |
| **Unconscious** | Critical | blood ≤ 35%; oxygenation ≤ 0.45; sedation 1.0; toxic coma (M5); deep cold (M5); heat stroke (M5). IPC: **Shutdown** (power, pump), oil ≤ 35%. | its cause clears |
| **Cardiac arrest** | Critical, looks dead (standing decision) | heart destroyed or removed; blood ≤ 30%; oxygenation ≤ 0.15; electrocution ≥ 60 after insulation; cold arrest (M5). Sepsis and toxins arrive through the oxygen trigger (§3.5). | a defib; or a heart put back when the heart was the cause (WolfmedLifeSystem.cs:434-442) |
| **Thermal shutdown** (IPC, M4) | Critical | chassis above the core-heat line while the core loses HP (§3.11) | chassis cools below the wake line (then Downed from pain), or core failure |
| **CBI** [OD1 wording] | Dead | brain HP 0; brain or head removed; gib; Succumb; execution | brain repair surgery, then a defib (existing) |
| **Core failure** (IPC) | Dead | core HP 0 (core-heat route, penetrating torso hits); core or head removed; gib; Succumb in thermal shutdown | core repair surgery (new, M2), then the restart button |
| **Permanent loss** | Dead | rot at 60 min (PerishableComponent.cs:18); no brain left to repair; other content's `Unrevivable` (OD1). Wolfmed adds nothing. | never |

Faint, Unconscious, arrest, Shutdown and thermal shutdown are all `MobState.Critical`, which keeps the upstream "no speech, no actions" rules with the least hook surface. What separates them is the new networked `Cause` field (§5.1).

### 2.2 What each state lets the player do

| Action | Up | Downed | Faint / Unconscious / Shutdown | Arrest / thermal shutdown (Dying) | CBI / Core failure |
|---|---|---|---|---|---|
| Move | normal | crawl 0.3× (LayingDownComponent.cs:13) | no | no | ghost |
| Use carried items on self | yes | yes, do-afters 1.5× (WolfmedDownedSystem.cs:161-176) | no | no | — |
| **Pick up items within reach** | yes | **yes (new, P16).** Today this is blocked (WolfmedDownedSystem.cs:122-128), contradicting DECISIONS AUTODOC5. | no | no | — |
| Attack, shoot, throw, touch others or doors | yes | no (standing decision; WolfmedDownedSystem.cs:130-153) | no | no | — |
| Talk, radio, emote | yes | yes | no (MobStateSystem.Subscribers.cs:143-152) | no | ghost chat |
| **Hear speech** | yes | yes | **yes, already.** ChatSystem filters only the speaker's crit LOOC (ChatSystem.cs:336, 834); M1a adds a test to pin this. | yes | ghost |
| **Call for help** (new) | — | yes | no | no | — |
| **Play dead** (new, replaces Fake Death; OD20) | — | yes | no | no | — |
| LOOC | yes | yes | off today (`looc.enabled_crit` false); OD19 | same | yes |
| **Breathe** | yes | yes | **yes, unless §4 applies** | organic arrest: no; IPC: never breathed | — |
| Succumb / Last Words | no | no | **no** | **yes** | — |
| "Wait as a ghost" (new, OD8) | — | — | after 90 s of continuous Unconscious or Shutdown **with no drain and no active route** (§5.4) | no (Succumb covers it) | — |

### 2.3 Helpless-time budget

These are design targets that the scenario tests assert. Each names what enforces it.

| Cause | Helpless state | Target (starting values) | What ends it | Tuning handle |
|---|---|---|---|---|
| Pain | Faint | **≤ 20 s per faint.** Another faint needs a fresh rise of 40 or a drop below the leave line first (§3.1). | timer, or a strong or emergency painkiller | `wolfmed.pain_faint_seconds` 20 new; `wolfmed.pain_faint_rise` 40 new |
| Head blow (M3) | Faint | ≤ 5 s | timer | `wolfmed.head_knockout_seconds` 5 new |
| Blood loss, bleeding stopped | Unconscious | **≈ 60 s** from 35% (19.5 u at 0.33 u/s, BloodstreamSystem.cs:128-131, derived); faster with a transfusion | blood > 41.5% | existing `wolfmed.consc_blood_out` |
| Oxygen, air restored | Unconscious | **≈ 7 s** from the Unconscious line to waking; ≈ 45 s from oxygenation 0.3 (refill 0.0042/s, derived) | oxygenation > 0.48 | existing `wolfmed.brain_refill_factor` 0.5 |
| Sedation overdose | Unconscious | ≤ 60 s after the reagent is gone (0.035/s decay, WolfmedPainReliefComponent.cs:85) | sedation < 0.96 | reagent `sedationPerUnit` (new, §3.4) |
| Cardiac arrest | Arrest | rescue window: brain loss at ~73 s, CBI at ~246 s untreated; 289 s / 534 s with CPR (rundown §5.3). Kept long on purpose (below). | defib | existing `wolfmed.brain_arrest_seconds` and the other `brain_*` CVars; OD22 |
| After a shock | Downed, or Unconscious when blood is still ≤ 35% | The patient breathes. **If blood was not the cause**, they come round Downed at once (§7.1). **Below 50% blood the brain keeps draining through the grace:** at ≤ 30% blood, oxygenation falls 0.5 → 0.45 in 15 s and → 0.4 (tissue loss) in 30 s (WolfmedLifeSystem.cs:288-297, derived). Held there by blood, the patient stays Unconscious until transfused above 41.5%. | transfusion | `wolfmed.post_shock_grace_seconds` 45 new; OD6 |
| IPC shutdown (power, pump) | Shutdown | **unbounded, by standing decision.** Softened by hearing, a distress flag, and the OD8 "wait as a ghost" option at 90 s. | cell, pump | `wolfmed.dormant_offer_seconds` 90 new |
| IPC thermal shutdown (M4) | Dying | until cooled; the rescue window is the core's HP ÷ the core-heat rate (§3.11, measured in M1a) | cooling below the wake line | `wolfmed.ipc_core_heat_rate`, `wolfmed.ipc_core_heat_wake_k` new |
| Toxic coma (M5) | Unconscious | until antitoxin; without it ≈ 2 min per 12 Poison above the wake line (clearance 0.1/s) | load < wake line | `wolfmed.toxin_clearance` 0.1 new |
| IPC pain | none | **0 s.** Mechanical pain only Downs (OD9). | — | — |

**Headline target.** A survivable combat injury (one or two gunshot wounds, a broken limb) spends **≤ 30 s helpless** and the rest Downed. Outside arrest, IPC shutdown, thermal shutdown and toxic coma, no single helpless stretch should exceed about one minute. Every helpless stretch shows its cause and what would help.

**Why the arrest window stays long.** The review warns that a long rescue window is "tense for the rescuers and empty for the patient". Arrest is excluded from the headline budget on purpose:
- The owner's goal names it: "a recoverable arrest followed by worsening brain injury gives medics a reason to prioritize patients." A shorter window turns arrest into near-certain CBI unless a medic is already standing there.
- Arrest is the one state where the rescuer, not the patient, is doing the playing. The patient's alternative is not agency but death.
- The patient can leave at any moment through the honest Succumb (§5.4), which gives the same outcome as an untreated arrest. Nobody is forced to wait out 4–9 minutes.

**What the arrested patient has during the window:**
- **Hearing** (already works, ChatSystem.cs:336, 834).
- **The explanation card** (M2): the cause, "you need a defibrillator", and a coarse bar (no seconds) of how long the brain has.
- **A rescue line on the card** (M2, new): "Someone is giving you CPR" while `WolfmedCprComponent` is active, and "A medic is examining you" when an analyzer scans the body. The patient learns that help has arrived.
- **LOOC** if OD19 is approved.
- **Succumb and Last Words**, with exact text.

Shortening the window is OD22.

---

## 3. Every cause

### 3.0 Summary

| Cause | Disables | Worsens by (explicit route) | First aid | Definitive | Kills by | Milestone |
|---|---|---|---|---|---|---|
| **Pain** | Downed; brief Faint | new injury only | painkillers, splint, dressings | treat the wounds so their pain floors fall | **never** | M1a |
| **Blood loss** | Downed ≤ 50%, Unconscious ≤ 35% | bleeding rate; the brain drains below 50% blood | pressure, gauze, tourniquet, clamp | sutures or surgery, transfusion | arrest at ≤ 30%, then CBI | M1a |
| **Oxygen** | Downed ≤ 0.54, Unconscious ≤ 0.45 | real suffocation (§4.3); damaged lungs (M3) | air, internals, CPR, epinephrine | fix the air supply; lung repair | arrest at 0.15, then CBI | M1a, M3 |
| **Sedation** | Downed ≥ 0.88, Unconscious 1.0 | respiratory depression above 0.6, its own brain drain | stop dosing, CPR | antagonist (OD14), or time | through oxygen | M2 |
| **Cardiac arrest** | arrest | brain drain 1/120 per s | CPR ×0.25, epinephrine ×0.6, cold | defib after addressing the cause | CBI | M1a |
| **Brain injury** | concussion symptoms; Downed < 25% HP | hypoxic tissue loss below 0.4; head trauma | anything that holds oxygenation up | brain heal or repair surgery | CBI | M3 |
| **Head blow** | Faint ≤ 5 s | — | — | — | never by itself | M3 |
| **Burns and fire** | pain, then blood volume | **burn fluid loss** into blood; infection | extinguish, burn dressing, fluids | graft, transfusion | through blood | M1b |
| **Toxins** | Downed, toxic coma | toxin load; brain drain above the coma line | stop exposure; the liver clears it | antitoxin | through oxygen | M5 |
| **Radiation** | Downed | marrow route: regeneration stops, slow blood loss | leave the area | anti-radiation drugs, transfusion | through blood | M5 |
| **Cold** | Downed, Unconscious, arrest | core temperature | warmth | rewarming | arrest, with the brain protected | M5 |
| **Heat (not fire)** | Downed, Unconscious | core temperature; brain drain | cooling | cooling | CBI | M5 |
| **IPC power** | Shutdown | none (standing decision) | move somewhere safe | charged cell | never | M1a |
| **IPC pump** | Shutdown | none | — | replace the pump | never | M1a |
| **IPC oil** | Downed ≤ 50%, Shutdown ≤ 35% | breach leaks | weld the breach | weld, refill | never by itself | M1a (cause text) |
| **IPC overheat** | pain Downed; **thermal shutdown** (Dying) once the core starts losing HP | **core-heat route** | extinguish, cold room | cool, then core repair | core failure | M4 |
| **IPC core damage** | Downed < 25% | penetrating torso hits (M3), heat (M4) | stop the damage | core repair surgery | core failure | M2 to M4 |
| **IPC pain** | Downed only | new damage only | — | weld and cable | never | M1a |

### 3.1 Pain

**Today.**
- The Downed test reads the body's pain value, clamped at 135, which drifts away from the parts (P13). The Unconscious test reads summed part pain against 1.4 × 135 = 189 (WolfmedConsciousnessSystem.cs:258-268; PainSystem.cs:187-193, 208-219).
- Untreated wound floors never fall, so pain unconsciousness lasts indefinitely and feeds the suffocation clock (rundown §3.3; P2).
- The pain shock stands a Downed player up for 30 s, and at ≤ 50% blood it stops the heart (P14; PainSystem.cs:31-35, 268-310; WolfmedLifeSystem.cs:653-659).

**Target.**
- **Body pain = min(135, Σ part pain)**, recomputed on every change through a marked Onyx edit in `PainSystem.SetPain` (PainSystem.cs:208-219). In `GetPainBeforeAdrenaline` (:187-193), each part's share becomes part ÷ Σ parts, so suppression is subtracted once.
  - The Downed test keeps reading the capped body value after relief. Painkillers therefore still lift a badly burned patient: summed floors ≈ 480, body 135, minus an opiate's 70 gives 65, which stands them up. This keeps the standing decision.
- **Faint.**
  - When summed pain after strong relief crosses the faint line and the faint is *armed*, the body faints for *(start: 20 s, `wolfmed.pain_faint_seconds` new)* and then comes round Downed. The faint line is `wolfmed.consc_pain_out` (existing, a multiplier of 1.4 on the 135 soft cap, so 189; WolfmedCVars.cs:137-138).
  - The faint **re-arms** only when summed pain falls below the leave line (170.1), or rises *(start: 40, `wolfmed.pain_faint_rise` new)* above its level at waking. A steady injury therefore never re-faints. A fresh wound can.
  - A **Strong** or **Emergency** painkiller ends a faint at once and blocks new ones while active. This is the standing "painkillers bring you out of Unconscious" rule. Today only Emergency lifts the out test (`LiftsUnconscious`, :264-266).
  - Mechanics: two server fields on `WolfmedConsciousnessComponent` (`PainFaintUntil`, `PainFaintArmedBelow`). The check runs on the existing 0.5 s poll (:123-138).
- **Pain shock.**
  - It keeps its fall, scream and 2 s stun.
  - The shock arrest is removed as data only *(`wolfmed.arrest_shock_blood` existing, 0.5 → 0; WolfmedLifeSystem.cs:653-659)*.
  - Adrenaline (OD5, recommended): it **no longer stands you up**. For 30 s it gives crawl speed ×1.5 and removes the 1.5× do-after penalty, "drag yourself to safety".
  - This needs a marked edit, because `GetPain` applies ×0.7 to every reading (PainSystem.cs:175-178). The hard-coded shock constants move to CVars in the same edit: `wolfmed.pain_shock_threshold` 130, `wolfmed.pain_shock_rearm` 110, `wolfmed.adrenaline_seconds` 30, `wolfmed.adrenaline_crawl_multiplier` 1.5 (all new).
- **Worsens by.** New injury only. **Pain is never a route to death.**
- **Mechanical bodies** never faint from pain (OD9). `WolfmedShutdownSystem.IsMechanical` (WolfmedShutdownSystem.cs:50-62) gates the faint.

**First aid:** painkillers (unchanged tiers), a splint. **Definitive:** close the wounds. **Recovery:** part pain falls 1/9 per s to its floor; you stand below 115.4.

**Patient sees:**
- Downed alert "Downed: pain", with the text "The pain has put you on the floor. You can crawl and treat yourself. A painkiller will get you up; the wounds are still there."
- Faint: popup "The pain takes you under." The card and alert say "Passed out: pain. You will come round in a few seconds."
- Waking: "You come round, still in agony."
- Adrenaline start and end each get a line.

**Medic sees:**
- Examine: "writhing in pain", or "unresponsive, breathing".
- Analyzer: "Downed: pain" or "Fainted: pain", per-part pain in words, the active painkiller.

### 3.2 Blood loss

**Today.**
- Downed ≤ 50%, Unconscious ≤ 35%, arrest ≤ 30% (WolfmedCVars.cs:142-157).
- Below 50% the brain drains at (0.5 − blood) ÷ 0.2 ÷ 300 per s, reaching the full 1/300 per s at 30% blood (WolfmedLifeSystem.cs:288-297).
- Bloodloss damage is also read as "not breathing" and blocks the refill until blood is back to 90% (P7).
- Internal bleeding is rounded down each tick (P22).

**Target.**
- The lines stay. **Unconscious from blood keeps breathing** (§4).
- Blood reaches the brain **only** through the blood-volume input. Bloodloss damage becomes bookkeeping and nothing reads it (§4.4).
- **P22.** Internal bleeding ticks once a second instead of every frame, so each amount is above the 0.01 u rounding step (WoundInternalBleedingSystem.cs:52-71). This is the cheapest fix; add a remainder field only if a test shows residual loss.
- **New self-aid:** a Downed patient can hold pressure on their own wound. It is the existing bandage-style action on self at 1.5× time (to confirm the item and verb in `HealingSystem.Wolfmed.cs`).

**Rates** (rundown §3.2, unchanged): flesh bleed = severity × stage rate × 0.2 u/s, with a whole-body ceiling of 3.33 u/s (about 70 s from full to arrest). Regeneration is 0.33 u/s.

**First aid:** tourniquet, gauze (×0.25; arterial ×0.6), clamp, epinephrine (brain drain ×0.6).
**Definitive:** sutures, `SurgeryRepairArtery`, `SurgeryStopInternalBleeding`, then transfusion to above 50%. That line stops the brain drain.
**Recovery (derived):** from 35% with the bleeding stopped, you wake in ≈ 60 s and stand at 55% after about 2 more minutes, spent crawling. The brain keeps draining slowly until blood is above 50%, which the analyzer states ("transfuse ≈ N u to 50%").

**Patient sees:**
- Per-wound bleeding status while Up (slow, steady, heavy, spurting, naming the part).
- "Downed: blood loss", with "You are light-headed and cold. Stop the bleeding; you need blood. Painkillers will not help."
- "Unconscious: blood loss. You will come round as your blood recovers, if the bleeding stops."

**Medic sees:**
- Examine: pooling and trails, "pale and clammy", "weak, rapid pulse" (from `BloodBand`, §5.5).
- Analyzer: blood % with its trend in words, bleed rate per wound, and "transfuse ≈ N u".

### 3.3 Oxygen deprivation (airway, lungs, suffocation, vacuum)

**Today.**
- The brain's breathing input is the Airloss **group** (Asphyxiation plus Bloodloss) ÷ the MobThresholds Critical value 100 (WolfmedLifeSystem.cs:339-349; groups.yml:23-27).
- Any Airloss above 0 blocks the refill (:245-258).
- Critical bodies never inhale (RespiratorSystem.cs:84).
- Destroyed lungs stop inhaling (:125). Damaged lungs do nothing.

**Target.**
- The breathing input reads **real suffocation** (§4.3): 0 unless the respirator is currently suffocating (`RespiratorComponent.SuffocationCycles > 0`, RespiratorComponent.cs:70; reset at RespiratorSystem.cs:116). When it is suffocating, it is Asphyxiation ÷ *(start: 100, `wolfmed.airloss_full` new)*. That CVar replaces the last gameplay read of the MobThresholds Critical value.
- The moment breathing resumes, the drain stops and the refill starts, even while Asphyxiation is still above 0.
- **Timeline, no air, derived (today's rates, now only while actually suffocating):**

  | Event | Time |
  |---|---|
  | saturation runs out | ~6 s |
  | Downed (0.54) | ~188 s |
  | Unconscious (0.45) | ~205 s |
  | brain tissue loss (0.4) | ~214 s |
  | arrest (0.15) | ~259 s |
  | CBI | ~398 s |

  Lower `wolfmed.airloss_full` to shorten it. Measure in the M1a oxygen scenario first.
- **Vacuum** kills faster through barotrauma bleeding (blood arrest about 100 s in, rundown §6.3). With P23 fixed (M3), barotrauma lands on a weighted random part instead of the victim's own doll selection.
- **Damaged lungs (M3)** add a named breathing input: (0.5 − lung health fraction) × 2 × *(start: 1, `wolfmed.lung_damage_factor` new)* while lungs are below 50% HP. It feeds the same drain as suffocation. This is the explicit "damaged lungs" route the review asked for.
- The stock `LowOxygen` alert, whose text always blames the air (alerts.ftl:1-2), is replaced on wound hosts by "Can't breathe: {source}". Sources: no air; lungs damaged; no lungs.

**First aid:** air or internals (a Downed patient can fit their own internals, an existing self-use action), CPR (it stands in for breathing, WolfmedLifeSystem.cs:281), epinephrine.
**Definitive:** restore the air supply; `SurgeryHealLungs` or a lung transplant.
**Recovery:** refill 0.0042/s. You wake about 7 s after air returns from the Unconscious line, and stand about 19 s later.

**Patient sees:** "Short of breath", then "Suffocating: {source}. Find air or internals." Gasps, as today.

**Medic sees:**
- Examine: "gasping" (pulse present), "not breathing", "blue lips" (oxygenation below *(start: 0.54, `wolfmed.examine_cyanosis_oxygenation` new)*, the hypoxia Downed line).
- Analyzer: "Breathing: none — no air / lungs failed", brain oxygen in words.

### 3.4 Sedation and overdose

**Today.**
- Sedation integrates with no ceiling. While any dose is active it rises by the dose's rate for the whole metabolism time, and it decays only when no dose is active (WolfmedPainReliefSystem.cs:196-217).
- Full sedation comes after 4.5–8.3 u of a strong painkiller, with no warning (P15).
- Halving the rate is **not** enough: Opiate gives 0.018 ÷ 0.1 metabolism = 0.18 per unit, so 7 u halved still passes 0.6 (critic's check).
- The existing lines: breathing is depressed above 0.6 (`SedationAirlossThreshold`, WolfmedPainReliefComponent.cs:91-93). Depression = (sedation − 0.6) ÷ 0.4 (WolfmedPainReliefSystem.cs:95-104), written as the `sedation` pressure, which Downs at 0.7 (WolfmedConsciousnessSystem.cs:41, 156). So Downed arrives at sedation 0.88 and Unconscious at 1.0.

**Target (model change, all in M2).**
- Sedation moves toward a **target** proportional to the units of sedating reagent currently in the bloodstream *(new reagent field `sedationPerUnit` in painkillers.yml and onyx-medicine.yml)*. It rises at *(start: 0.05/s, `wolfmed.sedation_rise` new)* and falls at the existing 0.035/s (`SedationDecayPerSecond`, WolfmedPainReliefComponent.cs:85) when above the target. A steady dose therefore levels off.
- Starting values: one standard dose (the amount one use of the item delivers; to confirm per item in the reagent and item prototypes) targets about 0.45. Roughly double reaches 0.88 (Downed). Full sedation takes about three doses.
- Depression above 0.6 stays its **own** brain drain (WolfmedLifeSystem.cs:283-286). The respirator keeps running, so an overdose is not counted twice. `SedationOverdoseTakesAirTest` (WolfmedConsciousnessTest.cs:486) keeps its "no Asphyxiation" assertion.
- **Warnings** (self popups, M2 only; none ship in M1a):

  | Line | Text | Lives in |
  |---|---|---|
  | 0.4 | "You feel heavy and drowsy." | *(start: 0.4, `wolfmed.sedation_warn` new)* |
  | 0.6 | "Your breathing slows. Another dose could stop it." | the existing field `SedationAirlossThreshold` 0.6 (WolfmedPainReliefComponent.cs:91-93), so the warning moves with the depression line |
  | 0.8 | "You can barely stay awake." | *(start: 0.8, `wolfmed.sedation_warn_heavy` new)*; it must stay below the 0.88 Downed point |

- **Antagonist reagent** (OD14): each unit metabolised lowers sedation by *(start: 0.3, reagent field `sedationReversePerUnit` new, on the antagonist prototype)*.
- The autodoc caps sedation at 0.5 (`wolfmed.autodoc_sedation_cap` existing, WolfmedCVars.cs:297-298) and stays below 0.6. Recheck it after the model change.

**Patient sees:** "Drowsy", then "Unconscious: overdose. Your breathing is slowed."
**Medic sees:** "breathing slowly and shallowly", pinpoint pupils; analyzer sedation % (exists, consciousness.ftl:22-25) plus "breathing depressed".

### 3.5 Cardiac arrest

**Today.** The triggers are (WolfmedLifeSystem.cs:394-428, 550-567, 653-670):
- heart at 0 HP or removed;
- blood ≤ 30%;
- oxygenation ≤ 0.15;
- sepsis ≥ 80 with a 1%/s roll;
- a pain shock at ≤ 50% blood;
- electrocution ≥ 60, measured before insulation (P28).

Succumb and Last Words are offered but do not kill (P4).

**Target.**
- **Triggers kept:** heart, blood, oxygen.
- **Pain-shock trigger removed** (M1a, data).
- **Sepsis made deterministic** (M2, data): `wolfmed.arrest_sepsis_chance` 0.01 → 0. Sepsis ≥ 80 keeps draining the brain at 1/600 per s (`brain_sepsis_seconds`; WolfmedLifeSystem.cs:300-301), and the arrest arrives through the oxygen trigger about 8.5 min later (derived). `wolfmed.arrest_sepsis` stays at 80, because it also gates that drain.
- **Electrocution** reads the shock after insulation: a marked edit at ElectrocutionSystem.cs:388-394 (M2).
- Every trigger records its **cause**: blood, oxygen, heart, sepsis, shock, cold or toxin (the last two in M5).
- **Worsens by:** brain drain 1/120 per s. Tissue loss begins at 73 s; CBI at 246 s untreated.
- **First aid:** CPR (×0.25), epinephrine (×0.6), cold (×0.5 / ×0.1).
- **Definitive:** address the cause (transfuse, replace the heart, restore air), then **defib** (§7).

**Patient sees:**
- The existing banner "YOUR HEART HAS STOPPED" (death.ftl:5-6), then silence (the existing flatline).
- The card (M2): "Cardiac arrest ({cause}). You need a defibrillator. Brain injury begins in about a minute without CPR." A coarse bar, not a clock, plus the rescue line (§2.3).
- **Succumb** and **Last Words** appear (§5.4).

**Medic sees:**
- "Appears dead", "no pulse" (existing).
- Analyzer: "CARDIAC ARREST: {cause}" plus the existing brain-death countdown (wounds.ftl:156).
- The defib verdict in words: "Shock will work", "Shock refused: blood 24%, transfuse ≈ N u", or "No heart: transplant first".

### 3.6 Brain and core injury

**Today.**
- Below oxygenation 0.4 the brain organ loses HP permanently (WolfmedLifeSystem.cs:352-367).
- Cold slows the drain but not the tissue loss (P24).
- Weapon damage to the brain is about 4% per head hit (P18).
- `SurgeryHealBrain` and `SurgeryRepairBrain` exist (wf-surgeries.yml:178, 314).
- A body with no `WolfmedBrain` (every IPC) takes an early branch in `WolfmedLifeSystem.Tick` that only zeroes the hypoxia pressure and returns before `UpdatePressures` (WolfmedLifeSystem.cs:219-227). Nothing reads core HP for consciousness.

**Target.**
- **Graded consequences (M3), organic brain:**

  | Brain HP left | Effect |
  |---|---|
  | < 60% | concussion effects (existing) |
  | < 25% | **holds the patient Downed**, cause Brain ("head injury"). A new `injury` pressure is written in `UpdatePressures` (WolfmedLifeSystem.cs:445-463) *(start: line 0.25, `wolfmed.consc_brain_down` new)* |
  | 0 | CBI |

- **IPC core (M3).** The same `injury` pressure is written **in the brainless branch of `Tick`** (WolfmedLifeSystem.cs:219-227), next to the existing hypoxia reset. It reads the positronic core organ's HP fraction. Below *(start: 0.25, `wolfmed.consc_core_down` new)* the chassis is Downed with cause Core. Core HP never Shuts the chassis down by itself; core failure at 0 is the existing death path.
- **Pressure value.** Both write *(start: 0.75, `wolfmed.injury_down_pressure` new)*. It must sit between the Downed share 0.7 (WolfmedConsciousnessSystem.cs:41) and 1, so brain and core injury Down but never knock out.
- There is no Unconscious rung for brain or core damage. Neither recovers on its own, so it would be helpless time that only surgery could end.
- **Heavy head blow (M3):** Blunt ≥ 30 to the head causes a Faint with cause HeadBlow *(start: 5 s, `wolfmed.head_knockout_seconds`, `wolfmed.head_knockout_blunt` 30, both new)*. It is detected in the new `WolfmedOrganThresholdSystem`, which the M3 organ hook already calls on every part hit (§8), so it needs no new subscription.
- **Cold (M2, P24):** `UpdateBrainDamage` multiplies tissue loss by the same cold factor as the drain.
- **Traumatic route:** deterministic organ reach (§8).

**First aid:** anything that holds oxygenation up. **Definitive:** `SurgeryHealBrain` for a living, damaged brain; `SurgeryRepairBrain`, then a defib, for CBI; `SurgeryRepairCore` (M2) for an IPC. **Recovery:** trauma effects for `wolfmed.brain_trauma_minutes` (existing 30), organics only (§7.2).

**Patient sees:** "Your thoughts are foggy", "Your speech slurs"; "Downed: head injury". IPC: "CORE INTEGRITY {word}". **Medic sees:** "unequal pupils, confused"; the analyzer brain line in words (it already shows tissue and oxygen, wounds.ftl:158-160).

### 3.7 Burns and fire

**Today.**
- Fire and body heat stop landing at the 600 ambient cap, about 29 s into a 10-stack fire (P3).
- Burn wounds have no bleeding behaviour, and Heat cauterises (WolfmedCauterySystem.cs:53-59; wounds.yml:554-608).
- The only killers are the hidden suffocation clock (≈ 4.5 min) and sepsis (15–22 min) (rundown §6.1).
- Dressed burns infect at the untreated rate (P20). Infection openness is the profile's `TreatmentMultipliers` entry for the wound's **bleeding** treatment (WolfmedInfectionSystem.cs:163-166), so a burn is always "untreated".

**Target.**
- **The cap goes, replaced by a per-part ceiling (§6).** Fire keeps growing burns, fluid loss and, on the torso, organ damage. It cannot delete a limb or the head (OD12).
- **Fluid-loss route (new, OD11).**
  - Each open burn wound at severity ≥ *(start: 20, burn wound field `fluidLossFrom` new)* loses blood volume at severity × *(start: 0.002 u/s, `fluidLossPerSeverity` new)*. Global multiplier: `wolfmed.burn_fluid_rate` 1 new.
  - The fields go on the Onyx `BurnWound` stages as marked YAML lines (wounds.yml:554-608), plus the Wolfmed burn prototypes (burns.yml).
  - **Dressed** burns lose × *(start: 0.25, `wolfmed.burn_dressed_fluid_factor` new)*.
  - Removal must **not** spill a blood puddle, because burns weep; they do not bleed. `BloodstreamSystem.TryModifyBloodLevel` spills negative amounts into puddles (BloodstreamSystem.cs:385-407), so use a direct split of the blood solution that is discarded. To confirm the API in BloodstreamSystem.cs.
  - Fluid loss runs once a second, skips Dead bodies, and is **not** scaled ×0.25 by arrest.
  - Derived examples:
    - 150 total burn severity loses 0.3 u/s, which regeneration cancels.
    - 600 loses 1.2 u/s gross, 0.87 net: Downed by blood ≈ 172 s, Unconscious ≈ 224 s, arrest ≈ 241 s after the burns exist.
    - An uncapped 10-stack fire will leave **more** than the capped trace's ≈ 600 (critic estimate: Heat on the order of 2,000, derived, not measured). **The M1b burn scenario measures the real total first**, and the rates are retuned against it.
- **Burn dressing** (ointment, burn packs, gel) marks the wound `WolfmedDressedComponent` in `HealingSystem.Wolfmed.cs`. `WolfmedInfectionSystem` openness (WolfmedInfectionSystem.cs:163-175) uses *(start: 0.15, infection profile field `dressedMultiplier` new)* when the wound is dressed and the bleeding treatment is None (P20, burns).
- **Charring** keeps its necrosis clock (480 s at ≥ 80).

**First aid:**
- Extinguish: an extinguisher, or resist to pat out. To confirm the resist action works while Downed, in `FlammableSystem.cs`; if not, allow it as part of the Downed self-use rule.
- Burn dressing.
- Painkillers to get moving.

**Definitive:** skin graft (burn kit), then fluids or blood. **Recovery:** fluid loss stops once dressed or grafted; blood regenerates as in §3.2.

**Patient sees:** "You are on fire! Resist to pat it out." Then the status "Burns weeping fluid (severe): dress your burns; you will need fluids." Then the blood-loss texts.

**Medic sees:** examine "weeping burns"; analyzer "fluid loss from burns: fast" next to the bleed rate, so a burn patient reads as a fluids patient.

### 3.8 Toxins and poison (M5)

**Today.** Poison sits in the body pool with no cap and no route. Its pain goes to the torso only, which clamps at 135, so poison can Down you but never knock you out or kill you (P12; WoundDamageRoutingSystem.cs:1097-1135).

**Target (OD13).**
- **Toxin load** = systemic Poison (`SystemicDamageComponent`, not the body total, so wound damage never counts as toxin).

  | Load | Effect | Starting value |
  |---|---|---|
  | ≥ 60 | Downed ("poisoned": nausea, weakness) | `wolfmed.consc_toxin_down` 60 new |
  | ≥ 120 | Unconscious ("toxic coma"), **breathing kept** | `wolfmed.consc_toxin_out` 120 new |
  | above the coma line | brain drains at 1/600 per s; arrest arrives through the oxygen trigger (same shape as sepsis) | `wolfmed.brain_toxin_seconds` 600 new |

- **Separate the pools.** Infection and sepsis stop dealing Poison (WolfmedInfectionSystem.cs:210, 252). Sepsis already has its own brain drain, so a septic patient is not driven twice. This is part of OD13.
- **Clearance.** A working liver clears *(start: 0.1 Poison/s, `wolfmed.toxin_clearance` new)*. An impaired liver (M3 bands) clears × *(start: 0.5, organ field `impairedClearanceFactor` new)*. This is the one exception to "no passive regeneration" (Species/base.yml:301-305) and needs owner approval (OD13).
- **IPCs:** Poison is dropped on IPCs today. No toxin route for mechanical bodies.

**First aid:** stop the exposure; charcoal. **Definitive:** antitoxin (Dylovene). **Recovery:** the load falls; you wake, then stand.
**Patient sees:** "Poisoned", "Unconscious: toxic coma. You will wake as the poison clears." **Medic sees:** vomiting; the analyzer toxin load in words and liver function.

### 3.9 Radiation (M5)

- **Marrow route (new):**
  - Radiation ≥ *(start: 40, `wolfmed.rad_marrow_stop` new)* stops blood regeneration.
  - Radiation ≥ *(start: 100, `wolfmed.rad_marrow_bleed` new)* also loses *(start: 0.1 u/s, `wolfmed.rad_marrow_rate` new)*.
  - Downed at ≥ *(start: 80, `wolfmed.consc_rad_down` new)*, cause Radiation ("radiation sickness").
  - It kills through the **blood route**, which medics already understand.
- **Regeneration seam.** Stopping regeneration needs a marked hook in the upstream regeneration step (BloodstreamSystem.cs:128-131) that asks a `_WF` method for a multiplier. To confirm the exact line.
- **IPCs:** the marrow route does not apply (no blood regeneration from marrow). Radiation stays slowdown and Downed through pain only. This is deliberate and listed in §9.
- **First aid:** leave the source. **Definitive:** Hyronalin or Arithrazine, plus transfusion.
- **Patient sees:** "Radiation sickness". **Medic sees:** radiation load in words, and "blood not regenerating".

### 3.10 Cold and heat (M5)

**Today.**
- Cold damage is ≤ 0.8/s below 260 K air.
- Cold slows the oxygen drain but not tissue loss (P24).
- Heat does nothing to the brain.
- A burning body reaches about 800 K (rundown §6.1).

**Target.**
- **Measure first.** The regulator holds a body about 22 K above freezing air (rundown §6.3), and TemperatureSpeed already slows bodies below 293 K (Species/base.yml:305-322). Before picking lines, the M5 opening task measures body equilibrium in cold rooms.
- **Lines are offsets from each species' own `TemperatureComponent` damage thresholds**, so every species is covered:

  | Core temperature | State | Starting value |
  |---|---|---|
  | < cold threshold + 30 K | Downed ("hypothermic", shivering) | `wolfmed.hypothermia_down_offset` 30 new |
  | < cold threshold + 15 K | Unconscious, breathing | `wolfmed.hypothermia_out_offset` 15 new |
  | < cold threshold + 2 K | arrest, cause "cold"; the existing ×0.1 protection applies | `wolfmed.hypothermia_arrest_offset` 2 new |
  | > heat threshold − 7 K | Downed ("heat exhaustion") | `wolfmed.hyperthermia_down_offset` 7 new |
  | > heat threshold | Unconscious ("heat stroke"), brain drain 1/300 per s | `wolfmed.hyperthermia_brain_seconds` 300 new |

- **Heat inputs are suppressed while the body is on fire and for 30 s after** *(start: `wolfmed.heat_fire_grace_seconds` 30 new)*. Otherwise every fire would trigger heat stroke and override the burn route and the 20 s faint budget.
- Fever's 313 K ceiling stays below the heat lines. A fever reads "feverish" and never Downs.
- Cold arrest is the most rescuable arrest, because the brain is protected.

**First aid:** warm or cool the patient. **Definitive:** core temperature back to normal. **Patient sees:** "Hypothermic" / "Heat stroke" with advice. **Medic sees:** "cold and stiff" / "hot, dry skin"; the analyzer's body temperature (exists).

### 3.11 IPC: power, pump, oil, overheating, core, pain

**Standing decisions kept:** no Airloss-group damage; no oxygenation clock; shutdown (no power or pump) is Unconscious, not death. Thermal shutdown is a **different cause**: it is the core-heat route, which is lethal by OD10, not power or pump loss.

**Today (P1).**
- An IPC cannot arrest (WolfmedLifeSystem.cs:486-490).
- Fire deals it 0 Heat. Overheat stops at the 600 cap after about 20 s.
- Pain holds an IPC under indefinitely.
- A destroyed positronic brain cannot be repaired (rundown §5.8).
- The HUD says STANDBY for every cause (WolfmedSyntheticHudOverlaySystem.cs:111-112, 205-239).
- IPCs hear the human heartbeat (WolfmedCritHeartbeatSystem.cs:103-125).

| Cause | Disables | Worsens by | First aid | Definitive | Recovery | IPC HUD | Medic sees |
|---|---|---|---|---|---|---|---|
| **Power** | Shutdown | nothing (standing decision) | move somewhere safe | charged cell | Up within 2 s | "CELL EMPTY: SHUTDOWN. AWAITING POWER." Low-power warnings beforehand | "status lights dark"; analyzer "Shutdown: no power" |
| **Pump** | Shutdown | nothing | — | replace the pump (WolfmedLifeSystem.cs:570-577) | Up on insertion | "COOLANT PUMP OFFLINE: SHUTDOWN" | "no pump hum"; analyzer "Shutdown: pump" |
| **Oil** (exists; the blood input on a mechanical body) | Downed ≤ 50%, Shutdown ≤ 35%, cause Oil | breach leaks | weld the breach | weld, refill | as oil returns | "HYDRAULIC PRESSURE LOW" | oil puddles; oil % |
| **Overheat** (M4) | pain Downed while the chassis heats; **thermal shutdown** (Dying, cause CoreHeat) once the chassis passes *(start: 500 K, `wolfmed.ipc_core_heat_k` new)* and the core starts losing HP at *(start: 0.15 HP/s, `wolfmed.ipc_core_heat_rate` new)*. The rate is **recalibrated from the M1a IPC fire measurement**: the chassis sits above 383 K for about 166 s in a 10-stack fire (rundown §6.2), but its time above 500 K is not known. Target: an untreated 10-stack fire destroys the core, and extinguishing within about 60 s saves it. | core-heat route | extinguisher, cold room; the IPC can pat itself out while still Downed, before 500 K | cool, weld, then core repair | thermal shutdown ends below *(start: 450 K, `wolfmed.ipc_core_heat_wake_k` new)*; the IPC comes back Downed (pain), if the core survives | "CORE TEMP CRITICAL" with a temperature bar while Downed; "THERMAL SHUTDOWN: CORE DAMAGE" once the core cooks | "smoking, too hot to touch"; chassis and core temperature |
| **Core damage** | Downed < 25% (M3, §3.6) | penetrating torso hits (§8, M3) and heat | stop the damage | **core repair surgery** (new, M2), then restart | restart button | "CORE INTEGRITY {word}" (CORE DEGRADED exists, synthetic-hud.ftl:56) | analyzer core line (wounds.ftl:162-163, reworded in M2, §7.2) |
| **Pain** | **Downed only** ("MOBILITY LOST"); never Shutdown (OD9). The pain shock is a 2 s "sensor overload" stun. | new damage only | — | weld, cable | stands as pain falls | "MOBILITY LOST: FRAME DAMAGE" plus a new pain/sensor row | "servos grinding" |

- **Dying for an IPC** is thermal shutdown only (OD3 (b)). It is helpless, like arrest, so Succumb and Last Words fit it: Last Words is a final transmission. Succumb gives core failure. A conscious, crawling, overheating IPC never sees them.
- **Why a shutdown rather than a conscious Dying.** Principle F offers the exit only to a player who has lost agency. A talking IPC does not need Last Words, and giving Succumb to one who can still crawl out of the fire invites quitting. Thermal shutdown also reads naturally as machine self-protection.
- **Shutdown agency (OD8):** an automatic distress flag on medical and robotics HUDs while shut down, and "Wait as a ghost" after 90 s (§5.4).
- **Audio:** the heartbeat loop is gated off for mechanical bodies with `WolfmedSyntheticHudOverlaySystem.OwnsView` (M1a). A fan hum that stops on shutdown is optional polish.

---

## 4. Breathing

### 4.1 The rule

**A wound host breathes in every state except those in §4.2.** Pain, blood loss, hypoxic unconsciousness, toxic coma, cold, heat, Downed, and brain damage short of CBI do not stop the respirator. They have their own routes (§3). **Pain unconsciousness keeps breathing.**

### 4.2 Exactly what stops or depresses breathing

| Condition | Effect | Where |
|---|---|---|
| Dead | respirator skipped | existing (RespiratorSystem.cs:79) |
| Cardiac arrest | no breathing | new `WolfmedBreathingSystem` (§4.5) |
| No brain | no breathing | existing `DebrainedComponent` (RespiratorSystem.cs:84), kept |
| Lungs removed or destroyed | nothing inhaled, so real suffocation | existing (RespiratorSystem.cs:125) |
| No breathable gas (vacuum, bad mix, no internals) | real suffocation | existing respirator |
| Sedation > 0.6 | depression, as its **own** brain drain; the respirator keeps running | existing (WolfmedPainReliefSystem.cs:95-104; WolfmedLifeSystem.cs:283-286) |
| Lungs < 50% HP (M3) | a named breathing input into the drain | new (§3.3) |
| Airway obstruction or choking | not modelled | deferred; it would be one more row |

### 4.3 The breathing input (replaces "Airloss group ÷ 100")

`WolfmedLifeSystem.AirlossLevel` (WolfmedLifeSystem.cs:339-349) becomes a **breathing level**, the maximum of:

1. **Suffocation:** 0 unless `RespiratorComponent.SuffocationCycles > 0`; otherwise min(1, Asphyxiation ÷ `wolfmed.airloss_full`). It reads Asphyxiation only, never Bloodloss.
2. **Lung damage** (M3): §3.3.
3. **Sedation depression:** already taken as the maximum today (:283-286).

This is server `_WF` code reading a server component, so it needs **no hook**. The respirator updates saturation on its own interval, and the life tick reads the result within 1 s.

### 4.4 Untangling P7 (Bloodloss read as not breathing)

- The brain no longer reads the Airloss group. Blood reaches the brain **only** through the blood-volume input (WolfmedLifeSystem.cs:288-297).
- **Refill** runs whenever every *live* drain is 0: arrest, breathing level, low blood, sepsis, and later toxin and heat. Leftover Asphyxiation or Bloodloss damage never blocks it again.
- Asphyxiation and Bloodloss stay as analyzer bookkeeping. `wolfmed.airloss_cap` 200 stays. Nothing in the life or brain code reads Bloodloss.
- With `AirlossLevel` gone, the defib's skipped −40 `zapHeal` (DefibrillatorSystem.cs:226-229) no longer matters.

### 4.5 Untangling P2 (upstream "Critical does not breathe")

- **One marked hook** at RespiratorSystem.cs:84: `!_mobState.IsIncapacitated(uid)` becomes `!_wolfmedBreathing.BreathingSuppressed(uid)`.
- New `WolfmedBreathingSystem` (Content.Server/_WF/Wolfmed/Life/):
  - Not a wound host, or `wolfmed.consciousness` off: returns `IsIncapacitated`, the upstream rule unchanged.
  - Wound host: returns `IsDead || HasComp<WolfmedCardiacArrestComponent>`.
- The gasp-in-arrest guard stays (RespiratorSystem.cs:99-111).
- **Intended side effects:** internals, sleeping gas and toxic gas now reach Unconscious bodies. Gasps become rare and honest.
- **Examine** is shared code (WolfmedVisualInspectionSystem.cs:214-229), but the respirator is server-only. Add a networked `Breathing` byte on `WolfmedConsciousnessComponent` (Normal / Depressed / Gasping / None), set by the life tick. Examine and the analyzer read it, and "not breathing" becomes true for every cause, including Critical bodies and destroyed lungs. Sedation above 0.6 now reads **Depressed**, not "not breathing" as today (WolfmedVisualInspectionSystem.cs:228-229).

---

## 5. Legibility

### 5.1 The cause, as data

- `WolfmedConsciousnessComponent` gains `[AutoNetworkedField] Cause`. Values:

  | Cause | States it can set | Sub-source |
  |---|---|---|
  | None | Up | — |
  | Pain | Downed | — |
  | PainFaint | Faint | — |
  | HeadBlow (M3) | Faint | — |
  | Blood | Downed, Unconscious | — |
  | Oil | Downed, Shutdown (IPC) | — |
  | Hypoxia | Downed, Unconscious | Airway / Lungs / Sepsis / Toxin / Heat |
  | Sedation | Downed, Unconscious | — |
  | Brain (M3) | Downed | — |
  | Core (M3, IPC) | Downed | — |
  | Toxin (M5) | Downed, Unconscious | — |
  | Radiation (M5) | Downed | — |
  | Cold (M5) | Downed, Unconscious | — |
  | Heat (M5) | Downed, Unconscious | — |
  | Legs | Downed | — |
  | Crash | Downed | — |
  | Arrest | Dying | arrest cause (blood, oxygen, heart, sepsis, shock, cold, toxin) |
  | CoreHeat (M4, IPC) | Dying (thermal shutdown) | — |
  | Shutdown (IPC) | Shutdown | Power / Pump |

  Core damage gets its own value rather than reusing Brain, because its text, HUD line and treatment all differ.
- `Evaluate` (WolfmedConsciousnessSystem.cs:141-193) already computes every input. It keeps them and picks the one that meets the current state's line.
  - Ties (only causes that meet the current state's line compete): Arrest > CoreHeat > Shutdown > Blood > Oil > Hypoxia > Sedation > Toxin > Cold > Heat > Radiation > Brain > Core > HeadBlow > PainFaint > Pain > Legs > Crash.
  - The hypoxia sub-source is the largest active drain.
- `Apply` (:208-243) raises a new `WolfmedConsciousnessChangedEvent(oldState, newState, oldCause, newCause)`. Raising our own event avoids a second subscriber on a pair that is already taken.
- Per-cause text lives in a `wolfmedConsciousnessCause` **prototype**. Fields: `alertDowned`, `alertOut`, transition lines (down, out, wake, stand), `symptom`, `help`, `syntheticHudLine`, `dying` (bool).

### 5.2 Patient alerts, messages and the explanation card

**Alert ownership (M1a).**
- `MobThresholdsComponent` is `[Access(typeof(MobThresholdSystem))]` (MobThresholdsComponent.cs:11), and `MobThresholdSystem` has no setter for `TriggersAlerts` (its public API ends at `SetAllowRevives`, MobThresholdSystem.cs:324). `_WF` code therefore cannot write the field.
- **Chosen fix: a marked setter hook.** Add `SetTriggersAlerts(uid, bool)` to `MobThresholdSystem`, next to `SetAllowRevives`, marked `// WOLFGATE`. The new `WolfmedConditionAlertSystem` calls it at wound-host startup when `wolfmed.consciousness` is on. `UpdateAlerts` then returns early (MobThresholdSystem.cs:386-387). The field is already networked (:39, 51).
- **Rejected alternative: `triggersAlerts: false` in YAML** on `BaseMobSpeciesOrganic` and `MobIPC`. That would also silence stock alerts on any descendant that `WolfmedWoundHostExclusionSystem` strips of `WoundHost`, and on every body when `wolfmed.consciousness` is off. `MobThresholds` is declared on `BaseMob` (Resources/Prototypes/Entities/Mobs/base.yml:100), so the organic line would also rely on prototype merging.
- A new `WolfmedConditionAlertSystem` handles the change event and shows:

  | State | Alert |
  |---|---|
  | Up | `HumanHealth` / `BorgHealth`, severity from the worst Downed-level input, not vital damage (fixes the healthy icon on a bled-out patient, rundown §4.7 item 7) |
  | Downed | the per-cause alert (moved from `WolfmedDownedSystem.OnStartup`, :69-78) |
  | Faint, Unconscious, Shutdown, Dying | the per-cause alert |
  | Dead | `HumanDead` / `BorgDead`, because turning off `TriggersAlerts` also stops those |

- Check `PainNumbnessSystem`, which subscribes to `BeforeAlertSeverityCheckEvent` (PainNumbnessSystem.cs:17). Decide whether the pain-numbness trait feeds the new severity. Grep the other `HumanCrit` and `HumanHealth` readers before M1a.

**Alert copy (starting wording).**

| Cause | Downed alert | Out alert | Short text |
|---|---|---|---|
| Pain | Downed: pain | Passed out: pain | "A painkiller will get you up; the wounds are still there." / "You will come round in a few seconds." |
| Blood | Downed: blood loss | Unconscious: blood loss | "Stop the bleeding; you need blood. Painkillers will not help." / "You will come round as your blood recovers, if the bleeding stops." |
| Oxygen | Short of breath | Unconscious: no oxygen | "Get to air." / "You need air: internals, or somewhere with atmosphere." |
| Sedation | Drowsy | Unconscious: overdose | "Painkillers are slowing you down." / "Too much painkiller; your breathing is slowed." |
| Arrest | — | Cardiac arrest: {cause} | "You need a defibrillator. Brain injury begins in about a minute without CPR. You can choose to let go." |
| Head blow, head injury, toxin, radiation, cold, heat, legs, crash | one line each, same pattern | | |
| IPC (Oil, Core, CoreHeat, Shutdown) | HUD lines (§5.6) | | |

**Transition messages** (none today, rundown §4.7 item 5). One self popup per transition, in `_WF/wolfmed/consciousness.ftl`:
- going down, fainting, going Unconscious, waking, standing;
- adrenaline start and end, stim crash;
- heart stopping;
- heart restarting: "Your heart lurches back into rhythm. You are still {cause}: {help}."

The sedation warnings (0.4 / 0.6 / 0.8) are not M1a messages. They ship with the sedation model in M2 (§3.4).

**Explanation card (M2).** A small client text panel on the unconscious screen, drawn from the cause prototype, with no numbers:
- the cause;
- what is happening, in symptoms;
- what will wake you;
- for a Faint, "you will come round shortly";
- while Dying, a coarse bar and the rescue line (§2.3).

**Dying view (M2).** The `pressure − 1` depth term is dead code (WolfmedConsciousnessSystem.cs:196-206). Depth follows the active route's progress instead: blood 35% → 30%, oxygenation 0.45 → 0.15, toxin 120 → coma drain. A Faint uses its own short white-out, so "brief" looks different from "dying".

### 5.3 Crawling-stage actions

| Action | Milestone | Rule |
|---|---|---|
| **Pick up within reach** (P16) | M1a | `WolfmedDownedSystem.OnInteractionAttempt` (:120-128) also allows an unanchored `ItemComponent` within *(start: 1.5 m, `wolfmed.downed_reach` new)*, which covers your own and the adjacent tiles. The owner already intended this (DECISIONS AUTODOC5). |
| **Call for help** | M1a | Downed only. Shouts an editable short line at normal shout range (no new number) and flags you on medical HUDs *(start: 60 s, `wolfmed.call_for_help_seconds` new)*. Cooldown *(start: 30 s, `wolfmed.call_for_help_cooldown` new)*. A HUD flag avoids radio plumbing; a radio location ping is optional later. |
| **Check yourself** | M2 | Up or Downed. Lists your own symptoms in words, reusing the self path of `WolfmedVisualInspectionSystem`. |
| **Play dead** (OD20) | M2 | Downed only. Examine at range reads "appears lifeless" until you move, speak or act. Replaces Fake Death, which is useless while Unconscious (crit.yml:19-21). |
| **Aid an adjacent Downed person** (OD7 (c)) | M2, if approved | pressure or gauze only, at 1.5× time, within the same `wolfmed.downed_reach` |

### 5.4 Succumb, Last Words and ghosting

**Owner-approved rule:** these appear only while Dying, they end the character's life revivably, and the dialog says exactly what happens.

**Mechanics (M1a).**
1. **Strip the upstream crit actions on both sides.** `MobStateActionsComponent` is not networked (MobStateActionsComponent.cs:12) and `MobStateActionsSystem` is shared (MobStateActionsSystem.cs:9). It grants from the per-entity `Actions` dictionary on every state change (:16, 36-53). A new **shared** `WolfmedCritActionsSystem` (Content.Shared/_WF/Wolfmed/Life/) therefore removes the `MobState.Critical` entry at `ComponentStartup` on client and server alike. `ComponentStartup` runs after `MobStateComponent`'s `ComponentInit` grant (:17), while the body is still Alive, so nothing crit-only is granted yet. To confirm that no other system subscribes `MobStateActionsComponent` + `ComponentStartup`. It covers organic and IPC wound hosts, with no YAML list-inheritance risk and no hook (the component has no access rule).
2. **New actions** in `Resources/Prototypes/_WF/Wolfmed/Actions/dying.yml` (`ActionWolfmedSuccumb`, `ActionWolfmedLastWords`) that raise **new event types**. `CritMobActionsSystem` already subscribes `CritSuccumbEvent` and `CritLastWordsEvent` (CritMobActionsSystem.cs:33-35), so reusing those would also run the upstream ghost path.
3. **New `WolfmedDyingActionsSystem`** (`_WF/Wolfmed/Life`). It grants the actions from `StartArrest` and removes them from `EndArrest` and on death, using direct calls (WolfmedLifeSystem.cs:486-511) rather than new subscriptions. Thermal shutdown grants and removes them the same way (M4).
4. **On confirm,** in one call:
   1. set the brain (or positronic core) organ to 0 HP;
   2. `EndArrest` (or end thermal shutdown);
   3. `WolfmedLifeSystem.Kill`;
   4. only then the ghost attempt with a returnable ghost.

   `Kill` alone only changes the mob state (WolfmedLifeSystem.cs:517-524), which would let a plain defib revive at 85% (WolfmedRevivalSystem.cs:62-80, 88-92) and make the dialog false. `OrganHealthSystem` applies Dead only on its next update (OrganHealthSystem.cs:44-53), so `Kill` must run first.
5. **Marked hook in the GhostSystem kill-crit branch** (GhostSystem.cs:692-718), which the `ghost` console command reaches (GhostCommand.cs:53). The command path has **no confirmation dialog of its own** (GhostCommand.cs:19-55 calls `OnGhostAttempt` directly), so no client hook is needed. On a wound host the hook:
   - never deals the Asphyxiation "top-up", so it can no longer *heal* anyone;
   - **in arrest or thermal shutdown**, does not ghost. It opens the **Succumb dialog** (item 4) and returns without ghosting. The player sees the same honest text whichever way they tried to leave;
   - **in any other state** (Up, Downed, Faint, Unconscious, Shutdown), opens a Wolfmed confirm dialog with the text below and, on confirm, ghosts with `canReturn = false` and leaves the body as it is. Otherwise every 20 s faint would offer a free returnable scouting ghost.
   - To confirm how the command reports a handled-but-not-ghosted attempt (GhostCommand.cs:53-55 prints "denied" on false).
6. **Pod return prompt.** The hand defib opens `ReturnToBodyEui` on success (DefibrillatorSystem.cs:241-249). The pod path does not (AutodocSystem.Procedure.cs:952). Add the prompt in `_WF` code (no hook), so the Succumb dialog is true for pod revivals.

**Succumb dialog (starting wording) [OD1 wording]:**

> **Let go?** Your heart has stopped ({cause}). If you let go, your character dies now: catastrophic brain injury. You become a ghost. A medic can still bring the body back with brain repair surgery and a defibrillator, until it decays in about {minutes} minutes. If they do, you will be offered the chance to return. [Let go] [Keep fighting]

For an IPC in thermal shutdown: "Your core is overheating … core failure … core repair and a restart".

**Last Words:** whisper up to 30 characters (existing limit, CritMobActionsSystem.cs:27), then the same dialog.

**`ghost` command while not Dying (Up, Downed, Faint, Unconscious, Shutdown):** "Your character will be left alive but empty. You cannot return to this body. [Leave] [Stay]"

**Executions and suicide (M2; P10, P29, OD17).**
- **Execution** sets the brain (or core) organ to 0 HP and then calls `Kill`, the same order as Succumb. It is CBI and revivable. It replaces DECISIONS HOOK 13's torso top-up (SharedExecutionSystem.cs:224-226), which no longer kills.
- **Suicide** does the same and then ghosts with `canReturn = false` (the upstream meaning), through a marked edit in `Content.Server/Chat/SuicideSystem.cs` (to confirm the line).

**Wait as a ghost (OD8, M2).**
- **When offered:** after *(start: 90 s, `wolfmed.dormant_offer_seconds` new)* of continuous Unconscious or Shutdown that is not Dying, **and only while nothing is getting worse**: `DrainRate` is 0 for a body with a brain (WolfmedLifeSystem.cs:269), and no route is active (fluid loss, marrow loss, toxin or heat drain, core heat). Blood-loss and hypoxic Unconscious never qualify, because the brain drains below 50% blood and below full breathing. In practice it is offered for IPC shutdown and for a stable Unconscious body.
- **Withdrawn** the moment a drain or route starts, even mid-dialog.
- **Text:**

  > Your body is stable for now ({cause}). It stays where it is. If someone wakes or repairs you, you will be offered the chance to return. If your body starts getting worse, you will be told.

- **While waiting:** if a drain or route starts, the ghost gets "Your body is getting worse: {route}. You can return to it now." Returning is always allowed.
- **Mechanics:** a returnable ghost that visits from a living body, made directly through the mind system's visit path, not through `OnGhostAttempt`, so the item 5 hook does not make it non-returnable (to confirm `MindSystem.Visit` or the equivalent). The body is unchanged.

### 5.5 What the medic reads

**Examine (shared code, reading networked coarse values):**
- consciousness on the **AVPU** scale. The bands come from state and cause, with no new numbers:
  - **A**lert: Up, or Downed and talking;
  - responds to **V**oice: sedation at or above `wolfmed.sedation_warn` (§3.4), or Downed by sedation;
  - responds to **P**ain: Faint;
  - **U**nresponsive: Unconscious, Shutdown, Dying.
- breathing: normal / slow and shallow / gasping / not breathing (from `Breathing`);
- circulation (a new networked `BloodBand` byte, set by the life tick):

  | Band | Blood | Examine | Line lives in |
  |---|---|---|---|
  | Normal | > 80% | pulse strong | *(start: 0.8, `wolfmed.blood_band_pale` new)* |
  | Low | 50–80% | pale | same |
  | Weak | 35–50% | pale and clammy, weak rapid pulse | existing `wolfmed.consc_blood_down` 0.5 |
  | Critical | ≤ 35% | barely palpable pulse | existing `wolfmed.consc_blood_out` 0.35 |
  | None | arrest or Dead | no pulse | — |

- signs: blue lips (§3.3, `wolfmed.examine_cyanosis_oxygenation`), weeping burns, pinpoint pupils, unequal pupils.

**Analyzer vitals block** (top of the panel; `HealthAnalyzerSystem.Wolfmed.cs:176-191`; client `HealthAnalyzerWindow.Wolfmed.cs`; the pod mounts the same panel):

| Line | Example |
|---|---|
| State and cause | `DOWNED: blood loss` / `FAINTED: pain` / `SHUTDOWN: no power` |
| Breathing | `normal` / `depressed: sedation 72%` / `none: lungs destroyed` |
| Circulation | `pulse weak; blood 41%, falling fast; transfuse ≈ 27 u to 50%` |
| Brain | existing line in words, plus the existing arrest countdown |
| Active routes | each running route with its first-aid step |
| After a restart | `Arrest cause: blood. Still present: yes. Transfuse.` Kept for *(start: 300 s, `wolfmed.arrest_cause_memory_seconds` new)* |
| Defib verdict | `Shock will work` / `Refused: pulse present` / `Refused: no heart, transplant first` |

M1a ships the state-and-cause, breathing and circulation lines. M2 ships the rest.

### 5.6 The synthetic HUD for IPCs

- The STANDBY banner (WolfmedSyntheticHudOverlaySystem.cs:111-112) is replaced by the cause's own line from the existing data-driven `syntheticHudLine` prototypes (DECISIONS.md:684-687):
  - CELL EMPTY;
  - PUMP OFFLINE;
  - HYDRAULIC PRESSURE LOW;
  - MOBILITY LOST;
  - CORE INTEGRITY {word};
  - CORE TEMP CRITICAL, then THERMAL SHUTDOWN (M4).
- The shutdown **reason** is a networked field set in `WolfmedShutdownSystem.Refresh` (:109-124).
- Add a pain/sensor row and a core-temperature row to the system block.

---

## 6. Damage caps

### 6.1 What changes

| Cap today | Problem | Replacement | Milestone |
|---|---|---|---|
| `wolfmed.body_damage_cap` 600 on hits with no origin (WoundDamageRoutingSystem.cs:798-812; WolfmedBodyPartSystem.cs:43-75) | past 600, fire, heat, overheat, EMP, cold and reagents do nothing; 600 Poison makes a body fire-proof (P3); the admin command cannot pass it (P31) | a **per-part ceiling** for no-origin hits: stored damage stops at *(start: 0.8, `wolfmed.ambient_part_cap_fraction` new)* × the part's lowest destruction threshold (arm 152, leg 152, hand and foot 120, head 400; torso keeps 250). Implemented inside `ClampToBodyCap` at the existing call site (WoundDamageRoutingSystem.cs:799). The body-wide 600 is retired; the CVar name stays, repurposed as a corpse ceiling on Dead bodies (0 turns it off). | M1b |
| Admin part command (Content.Server/_WF/Wolfmed/Commands/DamageCommand.Wolfmed.cs) | it has no origin, so the per-part ceiling would still stop it at 0.8 × destruction; admins could not test limb or head destruction | the command sets a one-call **ceiling bypass** that `ClampToBodyCap` reads, so admin damage lands in full. `_WF` code on both ends, no hook. | M1b |
| Torso `maxDamage` 250 (parts.yml:30) | overflow does nothing but evisceration (P8) | kept as **bookkeeping**; the overflow produces consequences (§6.2) | M1b |
| `wolfmed.airloss_cap` 200 | harmless after §4 | kept | — |
| Pain soft cap 135 per part (WoundDamageComponents.cs:149) | capping pain is fine | kept | — |
| Wound severity caps (per prototype) | a natural limit | kept; a wound at cap escalates (§6.3) | — |
| Organ damage 4.5 per roll | part of the rare-roll problem | replaced by §8 with a per-hit organ cap | M3 |
| External bleed ceiling 3.33 u/s | extra wounds bleed no faster | **kept on purpose:** it guarantees about a 70 s rescue window. Extra wounds still add pain and infection, and each must be closed. | — |
| Barotrauma stop at Blunt + Heat 200 (BarotraumaSystem.cs:240) | lands on the victim's own doll part (P23) | stop kept; part choice fixed (§3.3) | M3 |

**Why a per-part ceiling rather than no cap on the living:**
- Uncapped, a 10-stack fire puts about 290 Heat on each limb, past the Heat 250 ash line (Parts/base.yml:274-308). That derivation is the critic's, not measured.
- `BurnPart` leaves no stump and deletes the burns on that limb, and with them their fluid loss.
- The head ashes at Heat 700 (Parts/base.yml:105-133), which would put fire-caused death into the permanence question.

Weapons and explosions, which have an origin, can still destroy limbs as today.

### 6.2 Where the overflow goes (M1b)

- **One marked Onyx edit** in `WoundDamageRoutingSystem` (:800-829). When a hit overflows the torso's 250 or the ambient per-part ceiling, raise `PartDamageAppliedEvent` with **the overflow as its damage** and a new `Overflow: true` flag. It goes before the early return when nothing was kept, or after the normal event when some was. The flag is a new field on the event record (marked Onyx edit, WoundEvents.cs:47).
- **Handlers.** `OrganDamageSystem.OnPartDamageApplied` (OrganDamageSystem.cs:33-38) is the single subscriber and calls the others in a load-bearing order: wounds, fractures, amputation, bleeding, organs.
  - **Wounds** grow up to their prototype caps.
  - **Bleeding**, or burn fluid loss for burns, follows wound growth.
  - **Organs** take overflow through §8 (M3; until then the existing roll runs on overflow too).
  - **Amputation and fractures skip** `Overflow` events, because the torso never severs and fractures read stored Blunt. This skip is a marked Onyx edit in that same dispatcher method.
- **Where overflow pain comes from.** Pain is added from the stored-damage change (WoundDamageProjectionSystem.cs:99-105), and overflow is by definition not stored. So overflow adds **no direct pain**. It adds pain only through **wound pain floors** as the wounds grow, and part pain stays clamped at 135 (WoundDamageComponents.cs:149). A torso holding 250 (250 × 0.67 ≈ 167) already sits at that clamp, so on a saturated torso overflow is felt as bleeding, fluid loss and organ damage, not as more pain.
- **Evisceration** keeps its own `PartDamageOverflowedEvent` (:803-805), raised first.
- **The DECISIONS D27 accumulator (`AccumulateApplied`) must not count overflow**, or the body's damage number drifts.
- `PartDamageAppliedEvent` otherwise keeps its current meaning (applied damage, :814-829).

### 6.3 What stops runaway numbers instead

- per-part bookkeeping caps and the corpse ceiling;
- wound severity caps: a wound at cap escalates, burn → charring, and on a saturated torso into organs;
- the pain soft cap;
- the whole-body gib at Blunt 1500 (Species/base.yml:289-295);
- above all, the **lethal routes**: blood runs out, organs fail, the core cooks. Harm ends the body before numbers can grow without limit;
- systemic Poison and Radiation are bounded by their own routes (§3.8, §3.9), not by a shared budget.

The shown body total becomes a sum of capped parts plus the systemic pool, so it stays bounded without discarding harm.

---

## 7. Revival and stability

### 7.1 A successful shock (P6)

**Today.**
- Success sets oxygenation to max(current, 0.35) (WolfmedLifeSystem.cs:52). That is below Unconscious (0.45) and inside the tissue-loss band (0.4).
- The patient is Critical and so does not breathe, and any Airloss blocks the refill. The heart stops again 36 s to 2 min later (rundown §5.6).

**The post-shock course (M1a):**

1. **The heart restarts.** The patient breathes (§4.5), and nothing leftover blocks the refill (§4.4).
2. **Oxygenation = max(current, 0.5)** *(start: `wolfmed.post_shock_oxygenation` 0.5 new; replaces the constant `RestoredOxygenation`; also used by `RepairBrain`, :527-543)*.
   - 0.5 sits clearly above the Unconscious line and the tissue-loss line. A value of 0.45 would sit exactly on the line, where float rounding decides the state.
   - **If blood was not the cause** (heart, oxygen with air restored, electrocution) and blood is above 50%, no drain runs. The patient comes round **Downed at once** (hypoxia pressure 0.83) and stands in about 15 s (derived: Up needs oxygenation above 0.56). There is no forced post-arrest timer.
   - **If blood is still low**, the patient is held by blood. At or below 35% they stay **Unconscious, cause Blood**, and breathing. The brain keeps draining (step 4).
3. **Post-shock grace** *(start: 45 s, `wolfmed.post_shock_grace_seconds` new)*: the blood and oxygen arrest triggers are held off. The drains still run and are shown. This is the medic's window to transfuse or restore air.
4. **The remaining problem is visible.**
   - Patient: "Your heart lurches back into rhythm. You are still {cause}: {help}."
   - Analyzer: "REVIVED. Still at risk: blood 26%. Transfuse ≥ 15 u within 45 s or the heart stops again; ≈ 72 u to 50% to stop the brain injury."
   - Below 50% blood the brain keeps draining and the refill waits (WolfmedLifeSystem.cs:245-297). Below 41.5% the patient stays Unconscious from blood, which is honest and named.
5. **Dependable fix:** the cause's definitive treatment (§3). For blood, transfuse above 50%.

**A shock at the recommended gate (blood 25%, 300 u human pool), no transfusion, derived:**

| Time after the shock | Oxygenation | What happens |
|---|---|---|
| 0 s | 0.50 | breathing; Unconscious, cause Blood; grace starts |
| 15 s | 0.45 | hypoxia would also hold the patient Unconscious |
| 30 s | 0.40 | tissue loss starts, slowly: under 0.1 brain HP by 45 s (`wolfmed.brain_damage_rate` 0.1 at 0, falling linearly to 0 at 0.4) |
| 45 s | 0.35 | grace ends; blood still ≤ 30%, so **the heart stops again** |
| ~105 s | 0.15 | the oxygen trigger, if blood was raised above 30% but not to 50% (sooner or later with partial transfusion) |

**Is 45 s enough?** For the immediate goal, yes: avoiding the re-arrest takes only 15 u (25% → 30%), and the pod already transfuses before it shocks. Stopping the brain drain takes 75 u to reach 50%, which the medic has about 105 s for after the shock. The tissue lost inside the grace is negligible. Hand medics must carry blood when they shock a bled-out patient, which the analyzer's refusal and verdict lines already tell them. Whether a hand medic can really deliver 15 u in 45 s depends on the blood pack's delivery rate, which the M1a bleeding scenario measures (to confirm in `HealingSystem.Wolfmed.cs`). If playtest shows they cannot, raise the grace to 90 s. This is OD6 (c).

**Migration:** `BrainRepairMakesADeadBodyDefibrillatableTest` (WolfmedBrainTest.cs:189, the assertion at :231 expects Critical after the shock) and any test asserting 0.35 must read the CVar.

### 7.2 Revival holes (P11)

| Hole | Fix | Where | Milestone |
|---|---|---|---|
| Blood gate 0.40 sits above the 0.30 blood arrest, although DECISIONS.md:730-734 records the fix as "strictly under" and the code comment agrees (WolfmedRevivalSystem.cs:70-72; WolfmedCVars.cs:243-244) | **OD6.** Recommended: `wolfmed.defib_blood` (existing) 0.40 → *(start: 0.25)*, with the grace window and a refusal and analyzer line naming the units to 30% and to 50% | CVar, locale | M1a |
| Defib reports success on a beating heart (WolfmedRevivalSystem.cs:37-53) | refuse: "Pulse present: shock not indicated"; deal only the zap | `GetRefusal` | M1a |
| Pod ignores rot, `Unrevivable` and a missing heart. The pod already calls `GetRefusal` (AutodocSystem.Procedure.cs:928-952), so the fix is in that function. | move the rot and `Unrevivable` checks (DefibrillatorSystem.cs:195-204) into `GetRefusal`, and add "no heart: transplant first" for species whose heart carries Wolfmed data. The hand defib keeps its own checks, so DefibrillatorSystem.cs is not edited. | WolfmedRevivalSystem.cs:62-80 | M1a |
| Pod never offers a return | §5.4 item 6 | `_WF` autodoc | M1a |
| IPC restart button revives headless or brainless chassis, and also demands total damage under 100, a hidden damage-total gate (DeadStartupButtonSystem.cs:40-67) | marked hook: on a wound host, **replace** the damage check with `WolfmedRevivalSystem.GetRefusal` (core present with HP > 0, head attached, power and pump present) and give the reason | DeadStartupButtonSystem.cs | M2 |
| Destroyed positronic brain can only be replaced by a different person (rundown §5.8) | `SurgeryRepairCore`: a copy of `SurgeryRepairBrain` with `part: Torso`, `slot: posbrain` and chassis steps (wrench, multitool, welder). The condition already has a `Slot` field (WolfmedSurgeryConditionSystem.cs:87-101). `RepairBrain` already finds the posbrain, because `PositronicBrain` carries `Brain` (mmi.yml:110), and OrganHealthSystem keeps Brain organs at 0 HP rather than deleting them (OrganHealthSystem.cs:44-53). Mostly YAML. | wf-surgeries.yml:314-330 | M2 |
| The analyzer's core lines advise "Brain repair surgery" for IPC core damage (wounds.ftl:162-163), which cannot reach the posbrain today | reword both lines to name core repair (`SurgeryRepairCore`) | wounds.ftl:162-163 | M2 |
| `RepairBrain` would add `WolfmedBrainTrauma` concussion effects (slurred speech, fog) to an IPC | **recommended: skip the trauma component on mechanical bodies** (`IsMechanical`); the repaired core instead shows "CORE RESTORED: DIAGNOSTICS" on the HUD for the same `wolfmed.brain_trauma_minutes`, with no gameplay effect. Listed under OD10. | `RepairBrain` (WolfmedLifeSystem.cs:527-543) | M2 |
| Pain shock re-arrests revived patients at ≤ 50% blood | trigger removed (§3.5) | CVar | M1a |

**Autodoc and faints (M1a):**
- `MaintainAnaesthesia` skips any Critical body (AutodocSystem.Procedure.cs:349), so a fainted occupant would wake mid-procedure.
- The pod's `AutodocAlarm.Critical` alarm (AutodocSystem.Triage.cs:322) would sound for every faint.
- Fix:
  - treat a Faint (cause PainFaint or HeadBlow) as "anaesthetise", so the pod keeps sedating;
  - raise `AutodocAlarm.Critical` only for non-Faint causes.
- The existing alarms keep their meanings: `AutodocAlarm.Dying` still means "the brain is draining" and `AutodocAlarm.Arrest` still means "the heart has stopped" (AutodocComponent.cs:413-425). The plan's word "Dying" (§1.3) is never used as an alarm name.
- Add both cases to the autodoc suite.

---

## 8. Organs (P18)

**Today.**
- A low-chance roll per hit: head 5%, torso 4%, limbs 2%. Each organ then rolls again. At most 4.5 HP per roll against 15 HP (OrganDamageSystem.cs:40-77; OrganDamageComponent.cs:22).
- The heart is hit on about 1% of torso hits, and never once the torso holds 250.

**Target (M3): whether and how hard an organ is hurt is deterministic.**

1. **Reach line per part and type** *(new prototype field `organReach` on the Wolfmed part data in parts.yml; to confirm the component in WolfmedBodyPartComponent.cs)*.

   | Part | Piercing | Slash | Blunt | Heat | Shock |
   |---|---|---|---|---|---|
   | Torso | 10 | 18 | 30 | 20 | 15 |
   | Head | 8 | 15 | 15 | 20 | 15 |

   A hit reaches the organs when its damage **after armour** exceeds the line. Overflow events on a saturated part (§6.2) always reach them.
2. **No roll for which organ.** The excess is **split across the part's organs by the existing `selectionWeight`** (organs.yml:52-134). Organ damage = (hit − line) × the organ's per-type multiplier × weight share × *(start: 4, `wolfmed.organ_damage_scale` new)*, capped at *(start: 5 HP per organ per hit, `wolfmed.organ_hit_cap` new)*. The Onyx part rolls (wounds.yml:53-62, 95-106) and per-organ `hitChance` are bypassed.
3. **Hook:** a marked Onyx edit in `OrganDamageSystem.OnPartDamageApplied`. When the part carries `organReach`, it calls the new `WolfmedOrganThresholdSystem` and returns; otherwise it keeps the old roll. The component+event pair keeps its single owner. The head-blow faint (§3.6) is checked in the same call.
4. **Calibration (unarmoured rifle round, 14 Piercing, torso; derived at scale 4):**
   - lungs (share 1.38 ÷ 4.19 ≈ 33%) take ≈ 2.2 HP per hit and are impaired after about 4 hits;
   - the heart (≈ 15%) takes ≈ 1.1 per hit and fails after about 13–14 hits.

   Chest shots therefore cause breathlessness well before cardiac arrest, which the owner should know. At scale 1 these counts would be about 4× higher. The M3 calibration test pins the chosen scale.
5. **Graded organ bands** (new organ fields `impairedBelow` 0.5, plus one factor field per effect below). OK / impaired / failed, shown on the analyzer:

   | Organ | Impaired (< 50%) | Failed (0) |
   |---|---|---|
   | Lungs | named breathing input (§3.3), "short of breath" | no breathing (existing) |
   | Heart | "weak, irregular pulse"; blood regeneration × *(start: 0.5, `impairedRegenFactor` new)* | arrest (existing) |
   | Liver | toxin clearance × *(start: 0.5, `impairedClearanceFactor` new)* | clearance 0 |
   | Brain | §3.6 | CBI |
   | IPC pump | cooling × *(start: 0.5, `impairedCoolingFactor` new)* (overheats sooner) | Shutdown |
   | IPC core | Downed below 25% (§3.6) | core failure |

6. **Same-family chance rolls become bands** (OD15):
   - the internal-burn heart damage becomes *(start: 0.2, `wolfmed.electric_heart_factor` new)* × the Shock above *(start: 15, `wolfmed.electric_heart_from` new)*, instead of 35/50/75% rolls (WolfmedElectricalBurnSystem.cs:98-149);
   - the crush internal bleed happens always at Blunt ≥ 40, instead of 40% at ≥ 30 (wound_rules.yml:193-205; the 40 lives in that rule's existing threshold field);
   - the lodged-round chances stay as flavour.
7. **Limbs (P19):** raise Blunt and Heat destruction above severing on arms, legs and feet (Parts/base.yml:274-343), so limbs sever with a stump and bleed before they can be destroyed.
8. **Blast head (P27):** `WolfmedExplosionSystem` vetoes Onyx's per-part head roll when `wolfmed.blast_dismember_head` is off (WoundDamageRoutingSystem.cs:988-1022). If that roll raises no event `_WF` can cancel, this is one marked Onyx edit (to confirm).

**Why this helps medics.** "A chest shot over 10 reaches the organs, and lungs go first" is a rule a medic can learn. Armour lowers organ damage by lowering the hit, not through a separate roll.

---

## 9. Species

### 9.1 Approach

- **Every round-start species must answer three questions:** what disables it, what kills it, what restores it.
- **Conformance test** `WolfmedSpeciesConformanceTest` (M4). It spawns every `roundStart: true` species and fails **with the species name** unless an explicit `wolfmedSpeciesException` list (YAML, with a reason string per entry) excuses it, if:
  - the body is not a wound host;
  - the brain lacks `WolfmedBrain` + `WolfmedOrgan` (organics);
  - the heart or lungs lack `WolfmedOrgan`;
  - a mechanical body lacks a core and pump with `WolfmedOrgan`;
  - 29% blood fails to arrest it, or brain removal fails to kill it (organics); a pulled power source fails to shut it down, or core removal fails to kill it (mechanical).

This replaces a per-species archetype prototype with a much cheaper check.

### 9.2 The matrix today and the fix

This is the engineer draft's resolution of every round-start body prototype, corrected by the critic: Avali has full data. The conformance test is the authority.

| Group | Species | Brain / heart / lungs today | Consequence today | Fix |
|---|---|---|---|---|
| **A. Full data** | Human, Oni, Dwarf, Chitinid, Resomi, Thaven, Vox, Avali | clock / data / data | complete | reference profile |
| **A′. Lungs lack data** | Feroxi, Harpy, Goblin, Hydrakin | clock / data / **none** | lungs cannot be destroyed by trauma | add `WolfmedOrganLungs` as a parent (marked one-line edits, as human.yml does) |
| **B. Heart lacks data** | Moth, Reptilian, Vulpkanin, Canine, Felionoid, Tajaran, Rodentia, Arachnid (animal lungs too), ProtoThaven; also Felinid and Asakim through `OrganAnimalHeart` (animal.yml:139-157) | clock / **none** / some | heart can never arrest; removal does nothing (P9) | add `WolfmedOrganHeart` to `OrganAnimalHeart` and the arachnid heart. **A balance change: announce it.** |
| **C. No brain clock** | Protogen and its Proto-subspecies, Skrell, Diona, ProtoDionae, SlimePerson, ProtoSlimePerson | **none** | never arrest; brain removal does not kill; defib always says "brain-dead" (rundown §5.7) | add `WolfmedOrganBrain` (and heart and lung data where present). **Slime:** `SentientSlimeCore` counts as the brain, and slimes survive decapitation deliberately (the core is in the torso). **Diona:** the nymph organ is the brain (to confirm in diona organs yml). |
| **C′. No heart** | Diona, Slime | — | — | **"Circulatory collapse"** (OD16): the same arrest state, triggered by blood, oxygen or toxins, never by a heart. `StartArrest` already ignores the heart (WolfmedLifeSystem.cs:486-500), so only strings are new. |
| **D. Not wound hosts at all** | Synth, Shadekin, ProtoKin | — | parented to `BaseMobSpecies`, not `BaseMobSpeciesOrganic` where `WoundHost` lives (Resources/Prototypes/_HL/Entities/Mobs/Species/synth.yml:8; Resources/Prototypes/_StarLight/Entities/Mobs/Species/shadekin.yml:4; Resources/Prototypes/_HL/Entities/Mobs/Species/protogen_subspecies.yml:626; Resources/Prototypes/Entities/Mobs/Species/base.yml:265-270). They run **stock thresholds** (crit 100, dead 200), and **none of Wolfmed applies** | **OD16.** Shadekin and ProtoKin: reparent to `BaseMobSpeciesOrganic`, or add `WoundHost` plus part profiles. This is not an organ parent edit. Shadekin organs already carry Wolfmed data. **Synth:** the owner picks organic or mechanical; M4 scopes both branches (§12). |
| **E. Mechanical** | IPC | positronic core with data, no clock; pump with data | cannot die except by core, head or gib; pain holds it under (P1) | §3.11: deliberate machine ladder; core-heat route; core repair; pain Downs only |

**Synth today.** Synth is a hybrid: it has `Hunger`, `Thirst`, a `Bloodstream` of `SynthBlood`, a `SynthBattery` and `BatteryDrinker`, and the `Synth` damage container (synth.yml:77-217). That is why its body type is an owner decision.

### 9.3 Deliberate differences to record in the exception list

| Species | Difference | Reason |
|---|---|---|
| IPC | no oxygen clock, no arrest, no Airloss, power and pump shutdown never kill; radiation marrow route off | standing decisions |
| Slime | survives decapitation | the core is in the torso |
| Diona, Slime | circulatory collapse instead of heart arrest | no heart |
| Synth (if mechanical) | the IPC rows above, powered by `SynthBattery` | OD16 |
| Vox and other gas breathers | no rule needed: the suffocation input reads the respirator, which already knows each species' gas | — |

---

## 10. Terminology and permanence

**Today.**
- "Brain death" is `MobState.Dead`, reversible by `SurgeryRepairBrain` plus a defib (DECISIONS.md:456-461).
- The only hard stop is rot at 60 min, and it stops the hand paddles only (DefibrillatorSystem.cs:195-199).
- The death banner says "YOU DIED / Your body has given out" (death.ftl:1-2).

**The tension.** The owner's rule is "No one should be unrevivable; brain death is when you actually die." The review points out that if brain death is routinely repaired, the word overstates the stakes, and players cannot tell a setback from the end.

**Recommendation (OD1, open per INPUT.md §4).** Three player-facing terms, each with honest ghost text, used identically in alerts, the analyzer, examine, the guidebook and DECISIONS.md:

| Term | Mechanic | Ghost text |
|---|---|---|
| **Cardiac arrest** | Critical, looks dead | (still in the body) "Your heart has stopped. You need a defibrillator." |
| **Catastrophic brain injury** (replaces "brain death") | `MobState.Dead`, brain HP 0, not rotten, brain organ present | "You are dead: catastrophic brain injury. A surgeon can repair your brain and restart your heart before your body decays (about {minutes} min). If they do, you will be offered a return." |
| **Permanent loss** | rotten; or no brain organ left anywhere to repair; or other content's `Unrevivable` | "Your character is gone. You cannot return." |

**Notes.**
- This keeps the owner's rule. Wolfmed never adds `UnrevivableComponent`, and the per-part ceiling (§6) stops fire from ashing a head.
- Destroying a part drops its organs as items (`GibType.Drop`, SharedBodySystem.Body.cs:392-399, 434-437). A gibbed or ashed head therefore still leaves a brain item.
- The mind travels with the brain (BrainSystem.cs:30-75).
- **Executions and suicide** follow the same terms: execution leaves CBI, suicide leaves CBI with a ghost that cannot return (§5.4, OD17).
- **Sub-questions for the owner:**
  1. Can a recovered brain be put back into a body (head reattached or replaced) and revived as the same person? Recommended: **yes, if the brain item survives.** To confirm the surgical path in shitmed-surgeries.yml and `WolfmedLifeSystem.HasBrain`.
  2. Is destroying the brain item itself (burned, eaten, spaced) the only Wolfmed-visible permanent loss besides rot? Recommended: **yes**, stated in the ghost text.
- **Locale only** for the rename (death.ftl, defib lines, analyzer, guidebook). Code names stay. M1a writes the strings marked [OD1 wording] with the recommended terms; if the owner picks OD1 (a), only those strings change.

---

## 11. Disposition of P1–P31

| # | Problem | Disposition | Where | Milestone | Acceptance test |
|---|---|---|---|---|---|
| P1 | IPC cannot die; pain holds it under forever | **Fixed:** pain Downs only; core-heat route with thermal shutdown; torso hits reach the core; core repair; restart hook | §3.11, §7.2, §8 | M1a, M2, M3, M4 | `PainScenarioTest` (IPC branch), `IpcCoreInputTest`, `CoreRepairTest`, `IpcFireScenarioTest` |
| P2 | hidden 4.3-min suffocation clock | **Fixed:** Unconscious breathes; explicit suppressors | §4 | M1a | `PainScenarioTest`, `OxygenScenarioTest` |
| P3 | 600 cap makes fire harmless; burns have no route | **Fixed:** per-part ceiling with overflow consequences; burn fluid loss | §6, §3.7 | M1b | `BurnScenarioTest` |
| P4 | Succumb and Last Words in pain crit; do not kill; can heal | **Fixed:** Dying only; brain to 0 then Kill; no Asphyxiation top-up; honest dialog on every exit path; pod return prompt | §5.4 | M1a | `HonestEndingScenarioTest` |
| P5 | player cannot tell state or reason | **Fixed:** cause field, alerts, messages, HUD reasons, IPC heartbeat off (M1a); card, vitals, dying depth (M2) | §5 | M1a, M2 | `AnalyzerStateLinesTest`, `AnalyzerVitalsTest` |
| P6 | successful shock relapses | **Fixed:** breathing, clean refill, oxygenation 0.5, grace window, visible remaining problem | §7.1 | M1a | `BleedingScenarioTest`, `PostShockOxygenTest` |
| P7 | Bloodloss read as not breathing | **Fixed** | §4.3–4.4 | M1a | `BleedingScenarioTest` |
| P8 | torso at 250 ignores hits | **Fixed:** overflow event (M1b); deterministic organs (M3) | §6.2, §8 | M1b, M3 | `SaturatedTorsoTest`, `OrganCalibrationTest` |
| P9 | species without clock or heart data | **Fixed:** conformance test, parent edits, group D reparent, circulatory collapse | §9 | M4 | `WolfmedSpeciesConformanceTest`, `SpeciesArrestTest` |
| P10 | executions no longer kill | **Fixed:** execution sets brain HP 0, then `Kill` (CBI, revivable; OD17). Replaces DECISIONS HOOK 13's torso top-up through the M2 marked hook at SharedExecutionSystem.cs:224-226 | §5.4 (Executions and suicide) | M2 | `ExecutionAndSuicideTest` |
| P11 | revival holes | **Fixed:** shared refusal (rot, `Unrevivable`, heart, pulse), blood gate (OD6), pod return prompt (M1a); restart hook, core repair (M2) | §7.2 | M1a, M2 | `HonestEndingScenarioTest`, autodoc additions, `RestartHookTest`, `CoreRepairTest` |
| P12 | Poison, Radiation, Cellular cannot knock out or kill | **Fixed** for Poison and Radiation. **Cellular deferred** to M6's deferred list: no normal-play source found in the rundown that needs a route; revisit if content uses it | §3.8–3.9 | M5; M6 (deferred) | `ToxinScenarioTest`, `RadiationScenarioTest` |
| P13 | body pain drifts from parts | **Fixed:** body = min(135, Σ parts); suppression counted once | §3.1 | M1a | `BodyPainTracksPartsTest` |
| P14 | pain shock stands Downed up; shock arrest at low blood | **Fixed:** arrest trigger removed (M1a); adrenaline becomes a crawl boost (OD5) | §3.1 | M1a | `PainShockNoArrestTest`, `PainScenarioTest` |
| P15 | ordinary doses overdose silently | **Fixed:** target-based sedation model, warnings, antagonist | §3.4 | M2 | `SedationModelTest` |
| P16 | Downed cannot pick up dropped items | **Fixed** | §5.3 | M1a | `DownedPickupTest` |
| P17 | damage never interrupts do-afters | **Fixed if OD18 approved:** a single part hit ≥ 10 interrupts self-treatment and surgery; bleeding, fire and systemic ticks do not | §13 | M6 | `HitInterruptsSelfTreatmentTest` |
| P18 | weapons almost never damage organs | **Fixed:** reach lines, weight split | §8 | M3 | `OrganCalibrationTest` |
| P19 | limbs destroyed, not severed | **Fixed:** destruction thresholds above severing | §8 | M3 | `StumpTest` |
| P20 | infection reads the bleeding treatment only | **Fixed:** burn dressing (M1b); sutures set a treated state (M2) | §3.7 | M1b, M2 | `BurnDressingInfectionTest`, `SutureInfectionTest` |
| P21 | acid residue grows the plain burn | **Fixed:** residue grows only its chemical-burn wound (burns.yml:128-163; `WolfmedChemicalBurnSystem`) | §12 M5 | M5 | `AcidResidueTest` |
| P22 | internal bleeding rounded away | **Fixed:** tick once a second | §3.2 | M1a | `InternalBleedTickTest` |
| P23 | barotrauma hits the victim's own doll part; uncapped | **Fixed:** marked edit at BarotraumaSystem.cs:262, null origin on wound hosts, so a weighted random part. "Uncapped" is moot under §6. | §3.3 | M3 | `BarotraumaPartTest` |
| P24 | cold slows oxygen loss, not tissue loss | **Fixed** | §3.6 | M2 | `ColdBrainTest` |
| P25 | routing drops `ignoreResistances` and part multiplier | **Fixed:** pass the caller's arguments through (WoundDamageRoutingSystem.cs:99, 676-679). Low priority | §12 M6 | M6 | `RoutingPassesIgnoreResistancesTest` |
| P26 | lasers ignore aim; bullets use the doll at impact; snapshot unread | **Deferred:** combat accuracy, not the medical loop; its own targeting pass | — | later | — |
| P27 | blast can take the head with the CVar off | **Fixed** | §8 | M3 | `BlastHeadTest` |
| P28 | electrocution arrest uses pre-insulation shock | **Fixed** (marked edit, ElectrocutionSystem.cs:388-394) | §3.5 | M2 | `InsulatedShockTest` |
| P29 | suicide does not kill; zombie flicker | **Suicide fixed:** brain 0, Kill, non-returnable ghost (upstream meaning; OD17). **Zombie flicker deferred:** rare, cosmetic, needs zombie-system reading | §5.4 | M2; later | `ExecutionAndSuicideTest` |
| P30 | inert tunables | Stim-on-strong slowdown: **fixed** (M2). Fracture with no grade and necrosis risk multiplier: **fixed or removed** (M6). Onyx stage functionality off: **dropped**, documented as deliberate (Wolfmed limb penalties replace it). `wolfmed.consciousness` toggled mid-round: **dropped**, documented as restart-only, including the `TriggersAlerts` side effect | §12 M2, M6 | M2, M6 | `StimOnStrongTest`; M6 `FractureGradeTest`, `NecrosisRiskTest` |
| P31 | admin part command cannot pass 600 | **Fixed** by §6 plus the admin ceiling bypass | §6.1 | M1b | `AmbientCeilingTest` |
| — | *new:* Synth, Shadekin, ProtoKin are not wound hosts | OD16 | §9 | M4 | `WolfmedSpeciesConformanceTest` |
| — | *new:* airway obstruction or choking not modelled | **Deferred;** one more §4.2 row | — | later | — |
| — | *new:* pod does not offer a return after revival | **Fixed** | §5.4 | M1a | `HonestEndingScenarioTest` |
| — | *docs:* rundown Appendix A lists 20 doc/code mismatches | each milestone corrects DECISIONS.md for what it changes; the rest in M6 | — | all | — |

---

## 12. Delivery

### 12.0 Rules for every milestone

- Code goes under `_WF/Wolfmed`. Onyx edits and upstream hooks are marked `// WOLFGATE` and listed in the manifest. One subscriber per component+event pair: use Wolfmed's own events (§5.1) and direct calls, and check server start for duplicate-subscription crashes.
- Every number goes in a CVar or prototype field.
- **Gating (lean):** build per stage; YAML lint, a headless server start and the milestone's tests once at the end. The existing Wolfmed suite (57 files in `Content.IntegrationTests/Tests/_WF/Wolfmed/`), all autodoc tests included, stays green.
- **Scenario tests** live under `Content.IntegrationTests/Tests/_WF/Wolfmed/Scenarios/` and share one non-destructive pair (CI memory budget). A `WolfmedScenario` helper drives time through the public seams:
  - `WolfmedLifeSystem.Tick(body, seconds)` (WolfmedLifeSystem.cs:211);
  - `WolfmedConsciousnessSystem.Evaluate` (:141);
  - new seams for the respirator saturation step and the fluid-loss tick.

  The pattern is the `Run` helper in `WolfmedBrainTest` (WolfmedBrainTest.cs:572). Each milestone adds one real-time smoke test of at most 3 simulated minutes. Pin CVars in tests and assert order plus bands (±20% on derived times), not exact seconds.
- Before blaming a change for pair-test failures, check the known DB-warning trap.
- Every milestone below has the same blocks: scope, design rules, systems and files with every marked edit, test migration, acceptance tests with assertions, and an owner playtest.

### Marked-edit inventory

| # | File | Kind | What | Milestone |
|---|---|---|---|---|
| 1 | Content.Server/Body/Systems/RespiratorSystem.cs:84 | upstream | breathing rule hands off to `WolfmedBreathingSystem` | M1a |
| 2 | Content.Server/Ghost/GhostSystem.cs:692-718 | upstream | no Asphyxiation top-up; Succumb dialog in Dying; confirm dialog and non-returnable ghost otherwise | M1a |
| 3 | Content.Shared/Mobs/Systems/MobThresholdSystem.cs (new method beside :324) | upstream | `SetTriggersAlerts` setter | M1a |
| 4 | Content.Shared/_Onyx/Wounds/PainSystem.cs:175-219 | Onyx | body = min(135, Σ); shares; adrenaline; shock CVars | M1a |
| 5 | Content.Server/_Onyx/Wounds/WoundInternalBleedingSystem.cs:52-71 | Onyx | once-a-second tick | M1a |
| 6 | Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs:800-829 | Onyx | overflow `PartDamageAppliedEvent` | M1b |
| 7 | Content.Shared/_Onyx/Wounds/WoundEvents.cs:47 | Onyx | `Overflow` field | M1b |
| 8 | Content.Server/_Onyx/Wounds/OrganDamageSystem.cs:33-38 | Onyx | fractures and amputation skip `Overflow` | M1b |
| 9 | Content.Server/_EinsteinEngines/Silicon/DeadStartupButton/DeadStartupButtonSystem.cs:40-67 | upstream (EE) | restart uses `GetRefusal` | M2 |
| 10 | Content.Server/Electrocution/ElectrocutionSystem.cs:388-394 | upstream | arrest reads post-insulation shock | M2 |
| 11 | Content.Shared/Execution/SharedExecutionSystem.cs:224-226 | upstream | existing HOOK 13 rewritten (not a new hook) | M2 |
| 12 | Content.Server/Chat/SuicideSystem.cs (to confirm the line) | upstream | brain 0, Kill, non-returnable ghost | M2 |
| 13 | Content.Server/_Onyx/Wounds/OrganDamageSystem.cs, `OnPartDamageApplied` | Onyx | hand-off to `WolfmedOrganThresholdSystem` (same method as #8, second block) | M3 |
| 14 | Content.Server/Atmos/EntitySystems/BarotraumaSystem.cs:262 | upstream | null origin on wound hosts | M3 |
| 15 | Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs:988-1022 | Onyx | blast head veto, only if no cancellable event exists (to confirm) | M3 |
| 16 | Content.Server/Body/Systems/BloodstreamSystem.cs:128-131 | upstream | regeneration multiplier seam | M5 |
| 17 | Content.Shared/_Onyx/Wounds/WoundDamageRoutingSystem.cs:99, 676-679 | Onyx | pass `ignoreResistances` and part multiplier | M6 |
| 18 | do-after interruption (to confirm: a `_WF` handler, or one marked edit in the routing path) | Onyx or none | part hit ≥ 10 interrupts | M6 |

**Marked YAML lines** (not code hooks): wounds.yml `BurnWound` stages (M1b); onyx-medicine.yml `sedationPerUnit` (M2); wound_rules.yml crush rule and Parts/base.yml limb thresholds (M3); organ and species parents, including synth.yml:8, shadekin.yml:4 and protogen_subspecies.yml:626 (M4).

**No hook needed** (`_WF` code or data only): the crit-action strip (§5.4 item 1), the pod return prompt, `GetRefusal`, the admin ceiling bypass, `ClampToBodyCap`, the heartbeat gate, the shock-arrest and blood-gate CVars, the chemical-burn fix, infection pools.

### M1a: the honest loop (no damage-model change)

**Scope:**
- breathing (§4);
- cause and alerts (§5.1–5.2, minus the card and the sedation warnings);
- pain faint and one pain number (§3.1);
- pain-shock arrest removal;
- adrenaline per OD5;
- honest Succumb and Last Words, the crit-action strip, the ghost hook with its dialogs, the pod return prompt (§5.4);
- post-shock course (§7.1);
- revival refusals and the blood gate (§7.2, M1a rows);
- autodoc faint handling (§7.2);
- internal bleed tick (P22);
- pickup within reach and Call for help (§5.3);
- IPC shutdown reason, oil cause, HUD lines, pain Downs only, heartbeat gate;
- analyzer state, cause, breathing and circulation lines; networked `Breathing` and `BloodBand`; examine "not breathing", "gasping" and the circulation words.

**Design rules implemented:** principles A, B, C, D, F; §4 entire; the OD2, OD3, OD4, OD5, OD6 and OD9 recommendations; OD1's wording for the strings marked [OD1 wording].

**If a decision goes the other way:** OD1 (a) swaps locale strings only. OD4 (b) or (c) replaces the faint timer with a held or periodic faint; the scenario budgets change. OD5 (b) keeps the stand-up and drops the crawl boost. OD6 (b) keeps the 0.40 gate; `BleedingScenarioTest` then shocks at 40% after a transfusion. OD9 (b) adds a 10 s IPC reboot faint.

**Systems and files:**

| Kind | Items |
|---|---|
| New `_WF` | `WolfmedBreathingSystem`, `WolfmedConditionAlertSystem`, `WolfmedDyingActionsSystem`, shared `WolfmedCritActionsSystem`, `wolfmedConsciousnessCause` prototypes, `Actions/dying.yml`, `Alerts/alerts.yml` entries, `consciousness.ftl`, `death.ftl` |
| Changed `_WF` | `WolfmedLifeSystem` (breathing level, refill rule, post-shock oxygenation and grace, `Breathing` and `BloodBand`), `WolfmedConsciousnessSystem` and its component (Cause, faint fields, Breathing, BloodBand), `WolfmedRevivalSystem` (`GetRefusal`), `WolfmedShutdownSystem` (reason), `WolfmedDownedSystem` (pickup, Call for help), `WolfmedVisualInspectionSystem`, `HealthAnalyzerSystem.Wolfmed.cs`, `AutodocSystem.Procedure.cs` and `.Triage.cs`, `WolfmedSyntheticHudSystem`, `WolfmedCritHeartbeatSystem`, `WolfmedCVars` |
| Marked edits (5) | inventory #1–#5 |
| Data | `wolfmed.arrest_shock_blood` 0; `wolfmed.defib_blood` 0.25 |

**Test migration:**

| File | Test | Why it changes |
|---|---|---|
| WolfmedBrainTest.cs | `SuffocationDrainsAndRecoversTest` (:325) | must suffocate the body for real (airless tile or a saturation seam) |
| WolfmedBrainTest.cs | `BrainRepairMakesADeadBodyDefibrillatableTest` (:189, assertion at :231) | asserts Critical and 0.35 after the shock |
| WolfmedBrainTest.cs | `DefibrillatorNeedsBloodAndABrainTest` (:241) | blood gate 0.40 → 0.25; pulse-present refusal |
| WolfmedBrainTest.cs, WolfmedConsciousnessTest.cs, WolfmedSpeciesSpawnTest.cs, WolfmedVitalLimitsTest.cs | Asphyxiation readers | the brain no longer reads the Airloss group |
| WolfmedConsciousnessTest.cs | `PainDownsAndThenReleasesTest` (:144), `PainCritIsLiftedByStrongPainkillersOnlyTest` (:237), `EmergencyPenLiftsThenCrashesTest` (:428) | pain faint replaces held pain unconsciousness |
| WolfmedConsciousnessTest.cs | `DownedReachesOnlyItselfTest` (:197) | pickup within reach is now allowed; the test also sets body pain directly, which is now recomputed from the parts (P13) |
| WolfmedPainTest.cs | `PainShockStunsAtThresholdTest` (:171) | shock constants move to CVars; adrenaline no longer stands you up |
| WolfmedSyntheticHudTest.cs | `StandbyOnlyWhenTheChassisIsDownTest` (:350) | STANDBY is replaced by per-cause lines |
| WolfmedCritHeartbeatTest.cs | `HeartbeatTracksLocalPlayerCritTest` (:23) | the IPC gate; faints are now Critical and must not start the heartbeat |
| WolfmedVisualInspectionTest.cs | any case asserting "not breathing" from sedation | examine now reads `Breathing`; sedation above 0.6 is Depressed, not None (WolfmedVisualInspectionSystem.cs:228-229) |
| WolfmedAnalyzerTest.cs | cases reading the top of the panel | the M1a state, breathing and circulation lines |
| WolfmedDownedTransitionTest.cs, WolfmedAvailabilityTest.cs | alert and item assertions | alert ownership moves; new actions |
| WolfmedArrestLooksDeadTest.cs, WolfmedThresholdFallbackTest.cs | all | must stay green: arrest still does not gasp; non-wound hosts keep stock rules |
| any | grep `ActionCritSuccumb` | crit actions are stripped on wound hosts |

**Acceptance tests:**

| Test | Asserts |
|---|---|
| `BleedingScenarioTest` | Human, arterial arm wound (Slash 25). Downed at ≤ 50% with cause Blood; Unconscious at ≤ 35% **while inhaling** (saturation above its suffocation threshold). Analyzer lines: `DOWNED: blood loss`, breathing `normal`, circulation names the blood %. Tourniquet at 55%: never Unconscious; stands after regeneration. Blood held at 60% for 10 min: oxygenation stays 1 (P7). **Untreated branch:** arrest at ≤ 30% with cause "blood"; Succumb appears only now; at 24% the defib refuses and the analyzer names the units; at the gate (25%) a shock succeeds. After the shock: **Unconscious, cause Blood, breathing, grace active**; no re-arrest inside the grace. Transfused to 60% at 10 s: **Downed within 10 s**, Up within 60 s, no re-arrest in 10 min. **Second branch:** shocked at 25% and not transfused: re-arrest at 45 s ±20%. Records the blood pack's delivery rate (OD6 (c) input). |
| `PostShockOxygenTest` | Oxygen arrest in an airless room; air restored; shock: Downed at once, breathing, cause Hypoxia; Up within 15 s ±20%; no Unconscious. |
| `PainScenarioTest` | Summed pain ≥ 189 at full blood gives a Faint with cause PainFaint, breathing on, Downed within 20 s. No second faint while pain is steady over 10 min; Asphyxiation 0, oxygenation 1, no arrest, no Succumb. A +40 rise faints again. An opiate ends a faint and stands the patient up. The pain shock does not stand them up (OD5). Analyzer: `FAINTED: pain`. An IPC with heavy chassis wounds is Downed and **never** Unconscious. |
| `BodyPainTracksPartsTest` (P13) | Body pain equals min(135, Σ parts) after every change, including direct part edits; an opiate's suppression lowers the Downed reading once, not once per part. |
| `PainShockNoArrestTest` (P14) | A pain shock at 45% blood: stun and fall, no arrest, no `WolfmedCardiacArrestComponent`. |
| `OxygenScenarioTest` | Airless tile, no internals: Downed, then Unconscious (cause Hypoxia, sub-source Airway) at the derived times ±20%; examine says "gasping"; analyzer breathing `none: no air`. Internals fitted at Unconscious: drain 0 within 3 s while Asphyxiation is still above 0; awake within 15 s. Lungs removed in station air: suffocates with sub-source Lungs. |
| `InternalBleedTickTest` (P22) | An internal bleed of severity 10 loses 0.2 u/s ±20% over 60 s (none today); severity 20 loses 0.4 u/s. |
| `DownedPickupTest` (P16) | Downed: picks up an item on its own tile and one adjacent; cannot pick up at 3 m; cannot fire the picked-up gun. |
| `CallForHelpTest` | Downed: the action shouts, sets the medical-HUD flag for 60 s, and is refused during the 30 s cooldown; not available Up or Unconscious. |
| `IpcShutdownScenarioTest` | Pull the cell: Shutdown (reason Power), HUD line matches, analyzer `SHUTDOWN: no power`, no death over 30 simulated min; reinsert: Up within 2 s. Destroy the pump: reason Pump; replace it: Up. Oil at 45%: Downed with cause Oil. No heartbeat for an IPC. **Measurement:** a 10-stack fire records chassis temperature over time (the M4 calibration input). |
| `HonestEndingScenarioTest` | Faint, blood-Unconscious and shutdown bodies have no Succumb. An arrested body has it. Using it gives Dead, brain 0, no arrest component, a returnable ghost, and unchanged Asphyxiation. **The `ghost` command in arrest opens the Succumb dialog and does not ghost until it is confirmed.** The `ghost` command from a faint opens the "left alive but empty" dialog; confirming gives a non-returnable ghost and no damage. Brain repair plus defib (hand and pod) revives, and the return prompt opens. |
| `CriticalHearingTest` | a Critical player receives nearby speech (pins the existing behaviour). |
| `AnalyzerStateLinesTest` | for Downed (pain, blood), Faint, Unconscious (blood, hypoxia), arrest and Shutdown, the state-and-cause, breathing and circulation lines match §5.5; `BloodBand` matches the blood %. |
| Autodoc additions | a fainted occupant is anaesthetised; `AutodocAlarm.Critical` does not sound for a faint; the pod refuses a rotten corpse and a heartless body with the hand defib's messages. |

**Owner playtest:**
- get shot, crawl, pick up your dropped gun (you cannot fire it), bandage yourself, Call for help;
- faint from a beating and come round; take a painkiller;
- walk into an airless room without internals, then put them on;
- bleed out, be shocked, and read the remaining problem; transfuse within the grace, and once without;
- pull an IPC's cell; read the HUD;
- press Succumb in arrest, read the dialog, then be revived in the pod;
- type `ghost` while Downed and while in arrest, and read both dialogs.

Check that each screen explains itself.

### M1b: burns and caps

**Scope:**
- per-part ceiling in `ClampToBodyCap`, the corpse ceiling and the admin bypass (§6.1);
- the overflow event with the Overflow flag, excluding the accumulator and skipped by amputation and fractures (§6.2);
- burn fluid loss with no puddle, dressing, burn infection coverage (§3.7);
- confirm or fix extinguishing while Downed;
- measure the uncapped fire first, then set the burn rate.

**Design rules implemented:** principle E; §6; the OD11 and OD12 recommendations.

**If a decision goes the other way:** OD11 (b) or (c) drops the fluid-loss route; burns then kill only through overflow organ damage or sepsis. OD12 (b) removes the per-part ceiling on the living; `BurnPart` then deletes limbs in long fires.

**Systems and files:**
- `_WF`: `WolfmedBodyPartSystem` (`ClampToBodyCap`), `DamageCommand.Wolfmed.cs` (bypass), new `WolfmedFluidLossSystem` and `WolfmedDressedComponent`, `HealingSystem.Wolfmed.cs`, `WolfmedInfectionSystem` (`dressedMultiplier`), burns.yml, `WolfmedCVars`.
- Marked edits (3): inventory #6–#8.
- Marked YAML: wounds.yml `BurnWound` stages (`fluidLossFrom`, `fluidLossPerSeverity`).

**Test migration:** `DamageTotalsNeverCritAWoundHostTest` (WolfmedConsciousnessTest.cs:101); `WolfmedDamageCommandTest`; `WolfmedBurnWoundTest` and `WolfmedEviscerationTest` (overflow now raises a second event; evisceration must still fire first); `WolfmedInfectionTest` (dressed burns).

**Acceptance tests:**

| Test | Asserts |
|---|---|
| `BurnScenarioTest` | 10-stack fire: Heat keeps landing past the old 600 total; burn severity keeps rising; no limb or head is destroyed by environmental heat; one faint of ≤ 20 s, then Downed (pain); breathing throughout, Asphyxiation 0; blood falls at the fluid-loss rate with no blood puddle; untreated, the patient arrests with cause Blood inside the window derived from the **measured** severity (±20%). Extinguished, dressed and given 60 u at 60 s: alive, Downed or Up, blood stable at 10 min. An opiate stands them up. |
| `SaturatedTorsoTest` | A torso at 250 shot 5 more times: each shot raises the gunshot wound's **severity** and the torso's **bleed rate**, and delivers an `Overflow` event to the organ step (a test seam counts it; organ damage itself is asserted in M3); no fracture or amputation from the overflow; stored torso damage stays 250; torso pain stays at the 135 clamp; the body total does not drift. |
| `AmbientCeilingTest` | the admin part command with the bypass can pass 600 and destroy a limb (P31); an arm under fire stops at 152 stored while its burn keeps growing; a corpse is capped. |
| `BurnDressingInfectionTest` (P20) | two identical contaminated burns, one dressed: the dressed one's infection progresses at 0.15 × the undressed rate ±20%. |

**Owner playtest:** set someone on fire and treat them (extinguish, dress, fluids); leave one untreated and watch the named route; burn an IPC and watch it Down but stay conscious; use the admin part command to destroy a limb.

### M2: arrest, revival and medic information

**Scope:**
- explanation card with the rescue line; full vitals block; AVPU and examine signs; dying view by route (§5.2, §5.5);
- Check yourself, Play dead, aid to an adjacent Downed person if OD7 (c) is approved;
- wait as a ghost and the IPC distress flag (OD8);
- core repair surgery, the reworded core lines, no brain trauma on IPCs, and the restart-button hook (§7.2);
- sedation model, all three warnings, antagonist (§3.4, OD14);
- stim-on-strong fix (P30);
- deterministic sepsis (data), electrocution after insulation (P28), cold slowing tissue loss (P24), suture coverage (P20);
- executions and suicide (P10, P29; §5.4).

**Design rules implemented:** principles D, F, H; §1.5 triage; §5.2 card; §5.5; §3.4; the OD7 (c), OD8, OD10 (core repair half), OD14, OD17 and OD20 recommendations.

**If a decision goes the other way:** OD7 (a) or (b) drops the aid action. OD8 (a) drops wait as a ghost and its test; (c) removes the 90 s delay. OD14 "none" leaves time as the only overdose fix. OD17 "permanent" would conflict with the no-unrevivable rule and needs OD1 first; "no kill" keeps P10 open. OD20 (b) deletes Fake Death instead.

**Systems and files:**
- `_WF`: analyzer, examine, client card overlay, `WolfmedDyingEffectsSystem`, `WolfmedDownedSystem` (Check yourself, Play dead, aid), `WolfmedPainReliefSystem` and component, reagent YAML (`sedationPerUnit`, antagonist reagent with `sedationReversePerUnit`, recipe, vendor), wf-surgeries.yml (`SurgeryRepairCore`), wounds.ftl:162-163, `WolfmedLifeSystem.RepairBrain` and `UpdateBrainDamage`, `WolfmedInfectionSystem`, a `_WF` wait-as-ghost system, `WolfmedCVars`.
- Marked edits: inventory #9, #10, #12 (new) and #11 (existing hook rewritten).
- Marked YAML: onyx-medicine.yml `sedationPerUnit`.

**Test migration:**

| File | Test | Why it changes |
|---|---|---|
| WolfmedDyingLevelTest.cs | its `[Test]` cases (:11) | depth follows the route, not `pressure − 1` |
| WolfmedAnalyzerTest.cs | panel layout cases | the full vitals block |
| WolfmedConsciousnessTest.cs | `StackedPainkillersHitTheReliefCapTest` (:286), `SedationOverdoseTakesAirTest` (:486) | target-based sedation; the second keeps "no Asphyxiation" |
| WolfmedBrainTest.cs | `ArrestClockRunsOutTest` (:120, cold branch :157) | cold now slows tissue loss too |
| WolfmedBrainTest.cs | `MechanicalShutdownAndDeathTest` (:382) | restart hook and core repair |
| WolfmedSyntheticHudTest.cs | core line cases | reworded core lines; CORE RESTORED line |
| WolfmedInfectionTest.cs | suture cases | sutures set a treated state |
| WolfmedAutodoc*Test.cs | anaesthesia cases | recheck `wolfmed.autodoc_sedation_cap` under the new model |
| any | grep `arrest_sepsis_chance` | sepsis arrest is no longer a roll |

**Acceptance tests:**

| Test | Asserts |
|---|---|
| `AnalyzerVitalsTest` | each cause shows the right state, breathing and circulation words; the active-routes line lists each running route; after a restart, "Arrest cause … Still present" for 300 s and gone after; the defib verdict text |
| `ExplanationCardTest` | the card text matches the cause prototype for Faint, blood Unconscious and arrest; during CPR the rescue line appears |
| `RestartHookTest` | the restart button refuses a headless or coreless chassis with the reason; a repaired IPC over 100 total damage **can** restart |
| `CoreRepairTest` | a positronic core at 0 → `SurgeryRepairCore` → restart → Alive with the **same mind**; no `WolfmedBrainTrauma` on the chassis; the analyzer core line names core repair |
| `SedationModelTest` | one standard dose of each strong painkiller levels off below 0.6; two reach Downed; warnings fire once each at 0.4, at `SedationAirlossThreshold` and at 0.8; the antagonist wakes an overdose; no Asphyxiation in any case |
| `StimOnStrongTest` (P30) | a stimulant taken with a strong painkiller applies the documented slowdown |
| `SepsisDeterministicTest` | sepsis 80 drains the brain and arrests by oxygen at the derived time ±20% on every run |
| `ColdBrainTest` | below 20 °C, tissue loss is slowed ×0.1 |
| `InsulatedShockTest` | an electrocution through insulated gloves does not arrest |
| `SutureInfectionTest` (P20) | a sutured wound infects at its treated rate, below an untreated one |
| `ExecutionAndSuicideTest` | execution gives Dead, brain 0, and brain repair plus defib revives; suicide gives Dead, brain 0, non-returnable ghost |
| `WaitAsGhostTest` (if OD8) | offered after 90 s of shutdown; the ghost can return; the body is unchanged; not offered during a faint; **not offered while the brain drains** (blood-Unconscious at 33%, and hypoxic Unconscious); withdrawn and the ghost told when a route starts after the offer |
| `PlayDeadTest` | examine at range reads "appears lifeless" while Downed and still; cleared by moving or speaking |

**Owner playtest:** a medic triages three patients using only examine and the analyzer (§1.5); a full arrest rescue by hand and in the pod, watching the card as the patient; an overdose and its reversal; an IPC repaired from core failure; wait as a ghost from a shut-down IPC.

### M3: consequences keep mattering

**Scope:**
- deterministic organ reach and split, and graded organ bands (§8), including the lung breathing input;
- brain and IPC core injury input (Downed < 25%) and the head-blow faint (§3.6);
- electrical and crush bands (OD15);
- limb destruction thresholds with stumps (P19); barotrauma part choice (P23); blast head veto (P27).

**Design rules implemented:** principles C, E, H; §8; §3.6; the OD15 recommendation; OD10's "penetrating torso hits reach the core".

**If a decision goes the other way:** OD15 "keep" leaves the electrical and crush rolls; only item 6 of §8 is dropped.

**Systems and files:**
- `_WF`: new `WolfmedOrganThresholdSystem`, `WolfmedLifeSystem` (`injury` pressure in `UpdatePressures` and in the brainless branch of `Tick`; lung input), `WolfmedElectricalBurnSystem`, `WolfmedExplosionSystem`, parts.yml (`organReach`), organs.yml (bands and factor fields), `WolfmedCVars`.
- Marked edits: inventory #13, #14, and #15 if needed.
- Marked YAML: wound_rules.yml crush rule; Parts/base.yml limb thresholds.

**Test migration:** `WolfmedOrganTest` (forced rolls) is rewritten; `WolfmedExplosionTest` (head veto); `WolfmedAmputationTest` (limb thresholds); `WolfmedBluntWoundTest` (crush band); any electrical-burn case in `WolfmedBurnWoundTest` (heart band).

**Acceptance tests:**

| Test | Asserts |
|---|---|
| `OrganCalibrationTest` | 10 identical Piercing-14 torso hits give identical organ damage on every run; lungs are impaired within 3–5 hits and the heart fails within 12–16 at the chosen scale |
| `LungRouteTest` | lungs at 30% raise the breathing level; the patient becomes short of breath, then hypoxic (sub-source Lungs), in station air |
| `BrainInjuryInputTest` | brain at 20% HP is Downed (cause Brain), breathing, not Unconscious; a Blunt-30 head hit faints for ≤ 5 s with cause HeadBlow |
| `IpcCoreInputTest` | an IPC core at 20% HP is Downed with cause Core and is **not** Shutdown; at 30% it is Up; the HUD shows CORE INTEGRITY |
| `ElectricalHeartBandTest` | Shock 35 gives heart damage 0.2 × 20 = 4 on every run; Shock 15 gives none |
| `StumpTest` | a club destroys a limb only after it would have severed, leaving a bleeding stump |
| `BarotraumaPartTest` (P23) | barotrauma on a body whose doll selects the left arm spreads across parts by weight over 100 ticks, not all on the left arm |
| `BlastHeadTest` | with the CVar off, a blast never takes the head |

**Owner playtest:** firefights; learn "a chest shot means lungs first, then heart"; melee and explosions; shoot an IPC's torso until it Downs.

### M4: species and IPC death

**Scope:**
- conformance test and exception list (§9.1);
- organ parent edits (groups A′, B, C);
- circulatory collapse strings;
- group D: reparent Shadekin and ProtoKin; **Synth per OD16, both branches scoped:**
  - **M4-organic** (if Synth is organic): reparent to `BaseMobSpeciesOrganic`, give its organs Wolfmed data, keep `SynthBattery` as a hunger-like need with no shutdown.
  - **M4-mechanical** (if Synth is mechanical): mechanical part profiles, a positronic core and pump with `WolfmedOrgan`, `IsMechanical` recognising it, `WolfmedShutdownSystem.HasPower` reading `SynthBattery`, the synthetic HUD, and the exception-list entry. Only the chosen branch is built.
- IPC core-heat route and thermal shutdown, calibrated from the M1a measurement, with the Dying grant (OD3, OD10).

**Design rules implemented:** principle G; §9; §3.11 overheat row; the OD3 (IPC half), OD10 and OD16 recommendations.

**If a decision goes the other way:** OD16 (b) replaces the parent edits with exception-list entries for the species the owner names. OD10 (a) drops the core-heat route; an IPC can then still die only by core, head or gib.

**Systems and files:**
- Species and organ prototypes: Resources/Prototypes/_HL/Entities/Mobs/Species/synth.yml, Resources/Prototypes/_StarLight/Entities/Mobs/Species/shadekin.yml, Resources/Prototypes/_HL/Entities/Mobs/Species/protogen_subspecies.yml (ProtoKin), animal.yml, and the arachnid, skrell, protogen, diona and slime organ files (all marked YAML lines).
- `_WF`: locale, `WolfmedOverheatSystem` (core-heat route and thermal shutdown; also correct its summary comment, WolfmedOverheatSystem.cs:13-19), `WolfmedDyingActionsSystem`, `WolfmedShutdownSystem` (Synth power, mechanical branch only), the `wolfmedSpeciesException` prototype.
- Marked code edits: none.

**Test migration:** `WolfmedSpeciesSpawnTest`, `WolfmedSpeciesProfileTest`; `WolfmedOverheatTest.OverheatBurnsInsteadOfKillingTest` (:22), which asserts that overheating never kills.

**Acceptance tests:**

| Test | Asserts |
|---|---|
| `WolfmedSpeciesConformanceTest` | passes for every round-start species, or names it with its exception reason |
| `SpeciesArrestTest` | a moth's heart destroyed arrests it; a skrell at 29% blood arrests; a diona's brain removed kills it; a slime survives decapitation; a shadekin at 29% blood arrests |
| `SynthBranchTest` | organic branch: a synth at 29% blood arrests and brain removal kills. Mechanical branch: an empty `SynthBattery` gives Shutdown (reason Power) and no death over 30 min; core removal gives core failure |
| `IpcFireScenarioTest` | a burning IPC below 500 K is Downed (pain), conscious, has **no** Succumb and can pat itself out; above 500 K it enters thermal shutdown (Critical, cause CoreHeat) and gains Succumb and Last Words; cooled below 450 K it returns to Downed and loses them; an untreated 10-stack fire destroys the core (core failure); extinguished within 60 s the core survives |

**Owner playtest:** one round each as a moth, a diona, a slime, a shadekin, a synth and an IPC; an IPC set on fire, once put out early and once left.

### M5: the remaining causes

**Scope:**
- opening task: measure cold-room body equilibrium;
- toxin load, clearance and pool separation (§3.8, OD13);
- radiation marrow route and its regeneration seam (§3.9);
- cold and heat inputs with the fire grace (§3.10);
- acid residue fix (P21);
- alerts, messages and cause text for Toxin, Radiation, Cold and Heat.

**Design rules implemented:** principles C, D; §3.8–3.10; the OD13 recommendation.

**If a decision goes the other way:** OD13 (b) drops the toxin and radiation routes and keeps infection dealing Poison; only cold and heat ship.

**Systems and files:**
- `_WF`: `WolfmedLifeSystem` (toxin, cold, heat pressures and drains; cold arrest trigger), a toxin clearance step reading the liver band, `WolfmedInfectionSystem` (stop dealing Poison, WolfmedInfectionSystem.cs:210, 252), `WolfmedChemicalBurnSystem` and burns.yml (P21), cause prototypes and locale, `WolfmedCVars`.
- Marked edit: inventory #16 (BloodstreamSystem.cs:128-131).

**Test migration:** `WolfmedInfectionTest` (sepsis and infection no longer deal Poison); any chemical-burn residue case (grep `WolfmedChemicalBurn` under the Wolfmed tests); `WolfmedBleedingLifecycleTest` (regeneration now passes through the seam).

**Acceptance tests:**

| Test | Asserts |
|---|---|
| `ToxinScenarioTest` | Poison 60: Downed, cause Toxin. Poison 130: Unconscious, breathing, brain drains at 1/600 per s ±20%, arrest by oxygen at the derived time. Dylovene at coma: load falls, wakes below 108 (the 0.9 leave line), then stands. Untreated clearance matches 0.1/s ±20%; an impaired liver halves it. |
| `RadiationScenarioTest` | Radiation 40: blood flat over 60 s at 90% (regeneration stopped). Radiation 100: blood falls 0.1 u/s ±20%. Radiation 80: Downed, cause Radiation. Hyronalin brings radiation below 40 and regeneration resumes. An IPC at Radiation 100 loses no oil. |
| `HypothermiaScenarioTest` | Lines taken from the M5 measurement: Downed, Unconscious (breathing), then arrest with cause Cold; the brain drain is ×0.1; rewarming wakes the patient. |
| `HeatStrokeScenarioTest` | Core temperature above threshold − 7 K: Downed, cause Heat. Above threshold: Unconscious, breathing, brain drain 1/300 per s ±20%. Cooling wakes them. While on fire and for 30 s after: no Heat cause. |
| `SepsisNotToxinTest` | a septic body's toxin load stays 0. |
| `AcidResidueTest` (P21) | acid residue grows its chemical-burn wound only; the plain burn wound's severity does not change. |

**Owner playtest:** a chemistry poisoning; a radiation leak; a freezer; a hot room; a fire (no heat stroke).

### M6: leftovers

**Scope:**
- do-after interruption if OD18 is approved (P17);
- routing arguments (P25);
- fracture grade and necrosis multiplier (P30);
- DECISIONS.md corrections from rundown Appendix A;
- the deferred items, if still wanted: P26 (targeting), zombie flicker (P29), airway obstruction, **Cellular damage route (P12)**.

**Design rules implemented:** principle E (P25 restores dropped arguments); the OD18 recommendation.

**If a decision goes the other way:** OD18 (b) drops the interruption and its test.

**Systems and files:**
- `_WF`: a do-after interruption handler (to confirm whether it needs inventory #18); fracture and necrosis components (to confirm whether they live in `_Onyx` or `_WF`); `WolfmedCVars` (`wolfmed.doafter_interrupt_damage` 10 new).
- Marked edits: inventory #17, and #18 if needed.
- Docs: Docs/Wolfmed/DECISIONS.md.

**Test migration:** `WolfmedDamageBridgeTest` (routing arguments); `WolfmedBluntWoundTest` (fracture grade); `WolfmedInfectionTest` (necrosis multiplier); `WolfmedTreatmentProcedureTest` and `WolfmedWoundSurgeryTest` (do-afters may now be interrupted).

**Acceptance tests:**

| Test | Asserts |
|---|---|
| `HitInterruptsSelfTreatmentTest` | a part hit of 10 cancels a self-bandage and a surgery step; a hit of 9, a bleed tick, a fire tick and a systemic tick do not |
| `RoutingPassesIgnoreResistancesTest` | a routed hit with `ignoreResistances` ignores armour; the part multiplier is applied once |
| `FractureGradeTest` | a fracture reports its grade and the grade changes its penalty (or the field is removed and the test asserts it is gone) |
| `NecrosisRiskTest` | the necrosis risk multiplier changes necrosis progress (or it is removed) |

**Owner playtest:** bandage yourself while being shot; use admin damage with `ignoreResistances`; read DECISIONS.md against what the game does.

---

## 13. Decisions the owner must make

"Needed before" is the first milestone that builds on the answer. "Other answer" says what changes if the owner does not take the recommendation.

| # | Decision | Options | Recommendation | Needed before | Other answer |
|---|---|---|---|---|---|
| **OD1** | **Terminology and permanence** (open per INPUT.md §4) | (a) keep "brain death"; (b) "catastrophic brain injury" for revivable Dead, and "permanent loss" for rot or no brain left; (c) add a Wolfmed-made permanent death (e.g. CBI for N min) | **(b).** Honest stakes with no mechanical change, and the "no one is unrevivable" rule stays. Also confirm the §10 sub-questions: a surviving brain item can be revived as the same person (**yes**), and destroying the brain item is permanent (**yes**). | M1a | (a): swap the [OD1 wording] strings. (c): revisits a standing decision; adds a timer and `Unrevivable`, a new milestone item. |
| OD2 | What Succumb does | (a) brain to 0 (CBI), then Kill; (b) Dead with brain intact, so a defib alone revives. A third option, "nothing but ghosting", is **ruled out** by the approved fix that Succumb really ends the character's life (INPUT.md §3). | **(a).** It equals the untreated outcome, and the dialog can be exactly true. | M1a | (b): skip the brain step; the dialog says "a defibrillator alone can bring you back". |
| OD3 | Who may Succumb ("actually dying") | (a) arrest only; (b) arrest plus IPC thermal shutdown (the core-heat route); (c) (b) plus any body whose brain is losing tissue outside arrest | **(b).** The approval says "actually dying" without defining it. Thermal shutdown is helpless like arrest (§3.11). (c) adds little, because tissue loss outside arrest ends in arrest anyway. | M1a (organic), M4 (IPC) | (a): an overheating IPC has no exit until core failure. |
| OD4 | Pain unconsciousness | (a) bounded faint, re-armed only by a fresh rise or recovery, ended by strong painkillers; (b) held while pain lasts, breathing on; (c) periodic faint every N s while pain stays high | **(a).** It gives a hard helpless-time bound and puts sustained pain in the crawling stage. | M1a | (b) or (c): the pain budget in §2.3 and `PainScenarioTest` change. |
| OD5 | Pain-shock adrenaline | (a) no stand-up; 30 s crawl ×1.5 and no do-after penalty; (b) keep the 30 s stand-up, announced; (c) ×0.7 on the faint test only | **(a).** It keeps the "one last push" inside the crawling stage and fixes P14's stand-up half. All options remove the shock arrest. | M1a | (b) or (c): only the adrenaline block of the PainSystem edit changes. |
| **OD6** | **Defib blood gate and post-shock grace** (touches your recorded fix, DECISIONS.md:730-734: "strictly under the threshold", which the shipped 0.40 does not meet) | (a) honour the recorded intent: gate 0.25 plus a 45 s grace on the blood and oxygen triggers, with the refusal and analyzer naming units to 30% and 50%; (b) keep 0.40, labelled "transfuse ≈ N u first", which **revisits** your recorded decision; (c) (a) with a 90 s grace | **(a)**, moving to **(c)** only if the M1a measurement shows a hand medic cannot give 15 u in 45 s. A shock works on the arrests that actually happen, and the remaining problem is named with a dependable fix. The pod already transfuses first. | M1a | (b): `BleedingScenarioTest` shocks after transfusing to 40%. |
| OD7 | Downed reach | (a) self and carried items only (today); (b) plus floor items within reach; (c) (b) plus pressure or gauze on an adjacent Downed person | **(b)** in M1a (already intended, AUTODOC5). Try **(c)** in the M2 playtest. | M1a (b); M2 (c) | (a): drop `DownedPickupTest`. |
| OD8 | Long non-dying helplessness | (a) nothing; (b) automatic distress flag on HUDs, plus a returnable "wait as a ghost" after 90 s, offered only while nothing is draining, with exact text; (c) offer it at once | **(b).** It respects "shutdown is not death" without trapping the player. In practice it covers IPC shutdown and stable Unconscious bodies (§5.4). Risk: a returning ghost may have seen things (§14). | M2 | (a): drop the M2 item and its test. |
| OD9 | IPC pain | (a) Downed only; the shock is a 2 s "sensor overload" stun; (b) a 10 s "reboot" faint with the same re-arm rule; (c) (a) plus a later "suppress damage sensors" self-action costing heat | **(a).** No painkiller reaches an IPC, so a pain knockout could only be undone by welding. Keep (b) and (c) as later flavour. | M1a | (b): the IPC branch of `PainScenarioTest` expects a 10 s faint. |
| OD10 | IPC death routes | (a) core, head or gib only (today); (b) add the core-heat route with thermal shutdown and penetrating torso hits reaching the core, with core repair surgery; power and pump loss stay non-lethal. Sub-question: should a repaired core carry the brain-trauma effects? | **(b).** Fixes P1 and keeps "power-loss survival" as a deliberate trait. Sub-question: **no trauma on IPCs**; a cosmetic "CORE RESTORED" HUD line instead (§7.2). | M2 (core repair), M3 (torso hits), M4 (heat) | (a): drop the core-heat route, `IpcCoreInputTest` stays (Downed), `IpcFireScenarioTest` changes. |
| OD11 | Burns' lethal route | (a) fluid loss into blood volume; (b) organ cooking only; (c) sepsis only (today's fallback) | **(a)**, plus overflow into torso organs. It is visible and treatable, and medics already know the blood route. | M1b | see M1b. |
| OD12 | Can environmental harm destroy limbs or the head? | (a) no: per-part ceiling at 0.8 × destruction, with overflow still causing consequences; (b) yes: uncapped on the living | **(a).** A fire should not quietly dismember, or burn a head into the permanence question. Weapons and explosions still can. | M1b | see M1b. |
| OD13 | Toxins and radiation lethality | (a) routes as §3.8–3.9, with liver clearance (the one passive-healing exception) and infection no longer dealing Poison; (b) keep them non-lethal | **(a)**, all three parts. | M5 | see M5. |
| OD14 | Opioid antagonist | add a new reagent / reuse an existing one / none | **Add one** (Fluent name, medical vendor, chemistry recipe). The overdose loop needs a definitive fix. | M2 | see M2. |
| OD15 | Remaining chance rolls | bands for death-relevant rolls (sepsis arrest, crush internal bleed, electrical heart damage) / keep | **Bands** for those. Keep the flavour rolls (lodged rounds). The sepsis half is data in M2. | M2 (sepsis), M3 (the rest) | see M3. |
| OD16 | Species | (a) parity: every organic gets full data; heartless species get "circulatory collapse"; Synth, Shadekin and ProtoKin become wound hosts; (b) deliberate exceptions such as "Diona cannot arrest". Sub-question: **is Synth organic or mechanical?** | **(a)**, with recorded exceptions only where §9.3 lists them. Synth: the owner's call; M4 has a branch for each. | M4 | see M4. |
| OD17 | Executions and suicide | execution: CBI (revivable) / permanent / no kill (today). Suicide: Dead, brain 0, non-returnable ghost (upstream meaning) | **Execution = CBI; suicide as stated.** | M2 | see M2. |
| OD18 | Damage interrupts do-afters (P17) | (a) a single part hit ≥ *(start: 10, `wolfmed.doafter_interrupt_damage` new)* interrupts self-treatment and surgery; (b) never (today) | **(a)**, in M6. Being shot while bandaging should matter. | M6 | see M6. |
| OD19 | LOOC while Unconscious (`looc.enabled_crit`, a server CVar) | on / off | **On.** It costs nothing and eases helpless time, including the arrest window. | any time (server config) | off: the arrest window keeps hearing, the card and Succumb only. |
| OD20 | Fake Death | (a) becomes "Play dead" while Downed; (b) remove it | **(a).** It is roleplay for a conscious player. | M2 | see M2. |
| OD21 | Standing decisions | — | **Keep all.** Two are read rather than changed: "painkillers bring you out … unless blood or oxygen" extends naturally to sedation, toxins, temperature and brain injury; "IPC shutdown is not death" stays, and the new IPC death route is heat (thermal shutdown), not power or pump shutdown. OD6 is the one place this plan touches a recorded decision. | M1a | — |
| OD22 | Arrest rescue window (the review's helpless-time point) | (a) keep ≈ 4 min untreated, ≈ 9 min with CPR, and give the patient hearing, the card with a coarse bar and rescue line, LOOC (OD19) and an honest Succumb; (b) shorten it, e.g. `wolfmed.brain_arrest_seconds` 120 → 60, halving both windows; (c) keep it for rescuers but offer the arrested patient "wait as a ghost" with return | **(a).** The window is what makes arrest the triage signal the owner's goal names, and Succumb means nobody is forced to sit through it (§2.3). (c) would duplicate Succumb with a weaker promise. | M1a (CVar), M2 (card) | (b): every arrest time in §2.3, §1.5 and the tests halves. |

---

## 14. Risks

| Risk | Why | Mitigation |
|---|---|---|
| **Combat feels less lethal** | pain no longer suffocates; faints are short; crawling survivors | Downed still cannot attack, shoot or throw (WolfmedDownedSystem.cs:130-153). Blood, burns, organs, toxins and arrest stay lethal on named routes. Playtest time-to-death per weapon. |
| **The breathing hook changes an upstream rule** | every mob breathes through it; the autodoc and sedation assume Critical does not breathe | Gate strictly on wound hosts. A test that a non-wound-host Critical mob still does not breathe (`WolfmedThresholdFallbackTest` stays green). Autodoc and brain suites run every milestone. Test N2O and plasma on an Unconscious body. |
| **Cap replacement exposes unexercised paths** | long fires, EMP storms and reagent floods produce numbers nobody has seen | Per-part ceiling and corpse ceiling in the same milestone as retirement (M1b). The uncapped fire is measured before the burn rate is set. P31 is fixed, so admins can test. |
| **Uncapped fire totals differ from the derivations** | the burn timelines were derived on the capped trace | M1b measures first; acceptance windows derive from the measurement. |
| **Organ determinism shifts weapon lethality** | guns that never reached organs now do; lungs go first | `wolfmed.organ_damage_scale` gives one-knob tuning; the calibration test pins it; announce the change. |
| **Species data changes are balance changes** | moths and others can now arrest from heart trauma; group D gets Wolfmed at all | Announce them. The conformance test pins the intent, and the exception list is the release valve. |
| **Alert takeover breaks readers** | `TriggersAlerts` false also stops Dead alerts and `PainNumbnessSystem`'s severity hook | The condition system covers Dead. The setter is called only on wound hosts with `wolfmed.consciousness` on. Grep `HumanCrit` and `HumanHealth` readers before M1a. |
| **Post-shock relapse on low blood** | a patient shocked at 25% re-arrests at 45 s without 15 u | The analyzer names the deadline and the units; the M1a scenario measures blood-pack delivery; OD6 (c) is the fallback. |
| **Faint side effects** | `DamageForceSay` force-sends the typed line on every entry to Critical (DamageForceSaySystem.cs:74-82) | Accept it (it reads as a blurt). Suppress for faints only if playtest finds it spammy. |
| **Returnable ghosts carry knowledge back** | wait as a ghost (OD8), and revival after Succumb | Same exposure as any defib revival today. State it in the server rules. The `ghost` command outside Dying stays non-returnable. |
| **Sedation model change breaks pod anaesthesia** | the pod caps sedation at 0.5 through the same field | Recheck `wolfmed.autodoc_sedation_cap` behaviour in M2. An anaesthetised occupant shows as sedated, not as a faint. |
| **One-subscriber rule** | cause, faint, breathing, alerts and the crit-action strip react to the same moments | Route through Wolfmed's own change event and direct calls; the strip uses a pair confirmed unused; the server start check catches duplicates. |
| **Client and server disagree on actions** | the crit-action strip runs on a non-networked component | The strip runs in a shared system on both sides (§5.4 item 1). |
| **M1 size** | about 16 systems | Split into M1a (no damage-model change) and M1b (caps and burns), each with its own tests and playtest. |
| **Hook drift on upstream merges** | **5 code edits in M1a and 15 to 18 in total** (inventory in §12: 8 upstream, 7–9 Onyx, plus 1 existing hook rewritten), and marked YAML lines in M1b, M2, M3 and M4 | One line or block per hook handing off to `_WF`, listed in the manifest. Each hook's Wolfmed branch is covered by a named acceptance test. |
| **Placeholder art** | new per-cause alerts reuse existing icons (downed.rsi, crit, low oxygen, sepsis) | Listed as art debt; the text carries the meaning. |
| **Network cost** | new fields: Cause, Breathing, BloodBand, shutdown Reason | small enums and bytes, dirtied only on change (the existing pattern, WolfmedConsciousnessSystem.cs:215-220). |
| **Timing brittleness in tests** | the scenarios assert time windows | Pin CVars and assert order plus ±20% bands, as the bleed-rate tests already do. |
| **Derived numbers are not measurements** | nothing in the rundown or this plan was run | M1a and M1b scenarios are the first measurements; revisit every starting value against them. |