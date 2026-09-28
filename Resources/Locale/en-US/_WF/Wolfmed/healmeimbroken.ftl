# TEMPORARY (playtest 5): the healmeimbroken command and its ticket. Delete this file with the command once testing is
# done (Content.Server/_WF/Wolfmed/Commands/WolfmedBugRescueSystem.cs).

cmd-healmeimbroken-desc = TEMPORARY playtest command: heals you fully and files an admin ticket about the bug that made you use it.
cmd-healmeimbroken-help = healmeimbroken. A window asks what went wrong; your answer, your character's state and the fact that you used it go to the admins and to Discord. Using it for anything but a bug is a ban.

# The dialog is one label beside one line edit, so the prompt stays short and the explanation goes to chat first.
healmeimbroken-title = TEMPORARY playtest bug heal (misuse is a ban)
healmeimbroken-prompt = What broke?
healmeimbroken-brief = PLAYTEST BUG HEAL: in the window, say what you did, what happened and what should have happened. Submitting heals you fully and sends your text, with your character's state, to the admins and to Discord. Using this for anything but a bug is a ban.
healmeimbroken-not-in-game = You need to be in the round and in a body to use this.
healmeimbroken-disabled = The playtest heal is switched off.
healmeimbroken-cooldown = You used the playtest heal { $seconds } seconds ago. Wait { $wait } more seconds.
healmeimbroken-empty = Say what broke, or the admins cannot fix it. Nothing was healed.
healmeimbroken-done = You are healed and the admins have your report. This is a temporary playtest tool: using it for anything but a bug is a ban.
healmeimbroken-space = space

healmeimbroken-report = TEMPORARY PLAYTEST HEAL (healmeimbroken) used by { $player } playing { $character } on { $grid }. Misuse is a ban. REASON: { $reason }
healmeimbroken-report-state = STATE BEFORE: { $state }. { $vitals }.
healmeimbroken-report-dofirst = ADVICE WAS: { $advice }.
healmeimbroken-report-wounds = WOUNDS BEFORE: { $wounds }.
healmeimbroken-report-no-wounds = WOUNDS BEFORE: none.
healmeimbroken-wound-count = { $count }x { $name }
healmeimbroken-part-wounds = { $part }: { $wounds }
