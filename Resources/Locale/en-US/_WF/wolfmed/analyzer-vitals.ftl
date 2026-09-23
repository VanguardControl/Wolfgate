# M1a: the analyzer's vitals block (plan §5.5). State and cause, breathing, circulation, and the defib verdict.
# A shock succeeds on a roll, so the verdict never says it will work. [OD1 wording] on the dead line.

wolfmed-vitals-state-up = CONSCIOUS
wolfmed-vitals-state-up-mechanical = ONLINE
wolfmed-vitals-state-downed = DOWNED: { $cause }
wolfmed-vitals-state-faint = FAINTED: { $cause }
wolfmed-vitals-state-faint-timed = FAINTED: { $cause }, { $seconds } s
wolfmed-vitals-state-unconscious = UNCONSCIOUS: { $cause }
wolfmed-vitals-state-arrest = CARDIAC ARREST: { $cause }
wolfmed-vitals-state-shutdown = SHUTDOWN: { $cause }
wolfmed-vitals-state-dead = DEAD
wolfmed-vitals-state-dead-brain = DEAD: catastrophic brain injury
wolfmed-vitals-state-dead-mechanical = CORE FAILURE
wolfmed-vitals-blockers = { $state } (also: { $blockers })

wolfmed-vitals-cause-none = unknown cause
wolfmed-vitals-cause-pain = pain
wolfmed-vitals-cause-pain-mechanical = frame damage
wolfmed-vitals-cause-painfaint = pain
wolfmed-vitals-cause-blood = blood loss
wolfmed-vitals-cause-oil = hydraulic pressure low
wolfmed-vitals-cause-hypoxia = no oxygen
wolfmed-vitals-cause-sedation = overdose
wolfmed-vitals-cause-legs = legs
wolfmed-vitals-cause-crash = stim crash
wolfmed-vitals-cause-arrest = unknown cause
wolfmed-vitals-cause-shutdown = unknown cause
wolfmed-vitals-cause-other = other
wolfmed-vitals-cause-with-source = { $cause } ({ $source })

wolfmed-vitals-source-none = unknown cause
wolfmed-vitals-source-airway = no air
wolfmed-vitals-source-lungs = lungs failing
wolfmed-vitals-source-circulation = poor circulation
wolfmed-vitals-source-sepsis = sepsis
wolfmed-vitals-source-sedation = breathing depressed
wolfmed-vitals-source-arrestblood = blood
wolfmed-vitals-source-arrestoxygen = oxygen
wolfmed-vitals-source-arrestheart = heart
wolfmed-vitals-source-arrestsepsis = sepsis
wolfmed-vitals-source-arrestshock = shock
wolfmed-vitals-source-arrestother = unknown cause
wolfmed-vitals-source-power = no power
wolfmed-vitals-source-pump = coolant pump offline

wolfmed-vitals-breathing-normal = Breathing: normal
wolfmed-vitals-breathing-depressed = Breathing: depressed: sedation { $percent }%
wolfmed-vitals-breathing-gasping = Breathing: none: no air, gasping
wolfmed-vitals-breathing-none-none = Breathing: none
wolfmed-vitals-breathing-none-noair = Breathing: none: no air
wolfmed-vitals-breathing-none-lungs = Breathing: none: no working lungs
wolfmed-vitals-breathing-none-sedation = Breathing: none: sedation
wolfmed-vitals-breathing-none-arrest = Breathing: none: cardiac arrest
wolfmed-vitals-breathing-none-nobrain = Breathing: none: no brain
wolfmed-vitals-breathing-none-dead = Breathing: none
wolfmed-vitals-cooling-running = Cooling: pump running
wolfmed-vitals-cooling-offline = Cooling: pump offline

wolfmed-vitals-pulse-normal = pulse strong
wolfmed-vitals-pulse-low = pulse normal, pale
wolfmed-vitals-pulse-weak = pulse weak and rapid
wolfmed-vitals-pulse-critical = pulse barely palpable
wolfmed-vitals-pulse-none = no pulse
wolfmed-vitals-trend-steady = steady
wolfmed-vitals-trend-rising = rising
wolfmed-vitals-trend-falling = falling
wolfmed-vitals-trend-fallingfast = falling fast
wolfmed-vitals-circulation = Circulation: { $pulse }; blood { $percent }%, { $trend }
wolfmed-vitals-circulation-transfuse = Circulation: { $pulse }; blood { $percent }%, { $trend }; transfuse ≈ { $units } u to { $line }%
wolfmed-vitals-circulation-no-blood = Circulation: { $pulse }
wolfmed-vitals-hydraulics = Hydraulics: oil { $percent }%, { $trend }
wolfmed-vitals-hydraulics-refill = Hydraulics: oil { $percent }%, { $trend }; refill ≈ { $units } u to { $line }%

wolfmed-vitals-verdict-indicated = Defib: shock indicated
wolfmed-vitals-verdict-pulsepresent = Defib: refused: pulse present
wolfmed-vitals-verdict-noheart = Defib: refused: no heart, transplant first
wolfmed-vitals-verdict-noblood = Defib: refused: blood { $percent }%, transfuse ≈ { $units } u first (≈ { $safe } u to { $line }%)
wolfmed-vitals-verdict-braindead = Defib: refused: brain destroyed, brain repair surgery first
wolfmed-vitals-verdict-nobrain = Defib: refused: no brain
wolfmed-vitals-verdict-rotten = Defib: refused: body decayed
wolfmed-vitals-verdict-unrevivable = Defib: refused: { $reason }
