wolfmed-death-banner = YOU DIED
wolfmed-death-banner-sub = Your body has given out.

# BRAIN: the banner for the minutes between a stopped heart and brain death.
wolfmed-arrest-banner = YOUR HEART HAS STOPPED
wolfmed-arrest-banner-sub = You have minutes. Somebody has to find you.

# BRAIN: what the paddles say.
wolfmed-defib-success = Sinus rhythm restored.
wolfmed-defib-no-response = No response.
wolfmed-defib-no-brain = No neural activity detected. There is nothing to restart.
wolfmed-defib-no-blood = Shock refused: blood {$percent}%, too low to circulate. Transfuse ≈ {$units} u first; ≈ {$safe} u to reach {$line}%.
wolfmed-defib-brain-dead = Brain activity flatlined. Neural repair required before defibrillation.

# M1a: the shared refusals. A shock succeeds on a roll, so nothing here promises it will work.
wolfmed-defib-no-heart = Shock refused: no heart detected. Transplant first.
wolfmed-defib-pulse-present = Shock refused: pulse present. Shock not indicated.

# M1a: the analyzer's line after a successful shock (plan §7.1).
wolfmed-analyzer-post-shock = Revived: blood {$percent}%. Transfuse ≈ {$units} u within {$seconds} s to stop the heart stopping again; ≈ {$safe} u to {$line}% to stop the brain injury.
wolfmed-analyzer-post-shock-late = Revived: blood {$percent}%. Transfuse ≈ {$safe} u to {$line}% to stop the brain injury.

# M1a: Succumb, Last Words and the ghost command (plan §5.4). Exact consequences, [OD1 wording].
wolfmed-succumb-dialog-title = Let go?
wolfmed-succumb-dialog-text = Your heart has stopped ({ $cause }). If you let go, your character dies now: catastrophic brain injury. Your brain stays in your body, and you stay you. You become a ghost. A medic can still bring you back with brain repair surgery and a defibrillator, until your body decays in about { $minutes } minutes. If they do, you will be offered the chance to return.
wolfmed-succumb-dialog-text-no-decay = Your heart has stopped ({ $cause }). If you let go, your character dies now: catastrophic brain injury. Your brain stays in your body, and you stay you. You become a ghost. A medic can still bring you back with brain repair surgery and a defibrillator. If they do, you will be offered the chance to return.
wolfmed-succumb-dialog-accept = Let go
wolfmed-succumb-dialog-deny = Keep fighting
wolfmed-leave-dialog-title = Leave your body?
wolfmed-leave-dialog-text = Your character will be left alive but empty. You cannot return to this body.
wolfmed-leave-dialog-accept = Leave
wolfmed-leave-dialog-deny = Stay
wolfmed-last-words-title = Last words
wolfmed-last-words-prompt = Whisper to anyone nearby (up to { $max } characters). Then you choose whether to let go.
wolfmed-last-words-whisper = { $words }...
