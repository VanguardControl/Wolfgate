wolfmed-death-banner = YOU DIED
wolfmed-death-banner-sub = Your body has given out.

# BRAIN: the banner for the minutes between a stopped heart and brain death.
wolfmed-arrest-banner = YOUR HEART HAS STOPPED
wolfmed-arrest-banner-sub = Minutes left. Someone has to find you.

# BRAIN: what the paddles say.
wolfmed-defib-success = Sinus rhythm restored.
wolfmed-defib-no-response = No response. Charge again.
wolfmed-defib-not-monitored = Shock refused: no vital signs this device can read.
wolfmed-defib-no-brain = No neural activity. Nothing to restart.
wolfmed-defib-no-blood = Shock refused: blood {$percent}%. Transfuse ≈ {$units} u first; ≈ {$safe} u to {$line}%.
wolfmed-defib-brain-dead = Brain flatlined. Repair the brain first.

# M1a: the shared refusals. A shock succeeds on a roll, so nothing here promises it will work.
wolfmed-defib-no-heart = Shock refused: no heart.
wolfmed-defib-pulse-present = Shock refused: pulse present.

# M1a: the analyzer's line after a successful shock (plan §7.1).
wolfmed-analyzer-post-shock = Revived: blood {$percent}%. Transfuse ≈ {$units} u within {$seconds} s to keep the heart going; ≈ {$safe} u to {$line}% to stop the brain injury.
wolfmed-analyzer-post-shock-late = Revived: blood {$percent}%. Transfuse ≈ {$safe} u to {$line}% to stop the brain injury.

# M1a: Succumb, Last Words and the ghost command (plan §5.4). Exact consequences, [OD1 wording].
wolfmed-succumb-dialog-title = Let go?
wolfmed-succumb-dialog-text = Your heart has stopped ({ $cause }). Let go and you die now: catastrophic brain injury. Your brain stays; a medic can bring you back with brain repair and a defibrillator for about { $minutes } minutes. You will be asked to return.
wolfmed-succumb-dialog-text-no-decay = Your heart has stopped ({ $cause }). Let go and you die now: catastrophic brain injury. Your brain stays; a medic can bring you back with brain repair and a defibrillator. You will be asked to return.
wolfmed-succumb-dialog-accept = Let go
wolfmed-succumb-dialog-deny = Keep fighting
wolfmed-leave-dialog-title = Leave your body?
wolfmed-leave-dialog-text = Your body is left alive and empty. No coming back.
wolfmed-leave-dialog-accept = Leave
wolfmed-leave-dialog-deny = Stay
wolfmed-last-words-title = Last words
wolfmed-last-words-prompt = Whisper, if you want to (max { $max } chars)
wolfmed-last-words-whisper = { $words }...
