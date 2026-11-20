wg-mentor-duty-on = You're on duty as a Mentor.
wg-mentor-duty-off = You're off duty. You won't get questions or show Mentor marks.
wg-mentor-duty-toggle = On duty
wg-mentor-duty-start-off = Start every connection off duty
wg-mentor-who = { $count ->
    [one] 1 Mentor is on duty.
   *[other] { $count } Mentors are on duty.
    }
wg-mentor-who-names = Mentors on duty:
wg-mentor-granted = You're now a { $tier }. Open Mentor Help (Shift+F1) to see open questions.
wg-mentor-revoked = You've lost your Mentor status. Reason: { $reason }
wg-mentor-tier-changed = Your Mentor tier is now { $tier }.
wg-mentor-restored = Your Mentor status has been restored.
wg-mentor-tier-trainee = Trainee Mentor
wg-mentor-tier-mentor = Mentor
wg-mentor-tier-senior = Senior Mentor

cmd-mentor-desc = Manage Mentors, or your own Mentor duty status.
cmd-mentor-help = Usage: mentor on | off | list | grant <player> [tier] | revoke <player> <reason> | tier <player> <trainee|mentor|senior> | restore <player>
cmd-mentorwho-desc = Show how many Mentors are on duty.
wg-mentor-cmd-not-mentor = You aren't a Mentor.
wg-mentor-cmd-no-player = No player called { $player }.
wg-mentor-cmd-done = { $player } is now a { $tier }.
wg-mentor-cmd-revoked = { $player } is no longer a Mentor.

wg-chat-channel-humanized-mentor = Mentor
wg-chat-select-mentor = Mentor
wg-mentor-chat-wrap = MENTOR: { $name }: { $message }
cmd-msay-desc = Send a message to Mentor chat.
cmd-msay-help = Usage: msay <message>
hud-chatbox-select-channel-Mentor = { wg-chat-select-mentor }
hud-chatbox-channel-MentorChat = { wg-chat-channel-humanized-mentor }

wg-mentor-examine = They are a Mentor.
wg-examine-chip-mentor = Mentor
wg-mentor-card-tooltip = This player is a Mentor. Ask them how the game works in Mentor Help (Shift+F1).
wg-mentor-crown-option = Show my Mentor mark over my head
wg-mentor-chat-tag = [M]

guide-entry-wg-mentor-coc = Mentor Code of Conduct

# DRAFT: not on the strings page yet; Moon to confirm or rewrite before the PR.
wg-mentor-cmd-not-on-roster = { $player } isn't a Mentor.
wg-mentor-cmd-not-restorable = { $player } isn't suspended or inactive.
wg-mentor-cmd-list-empty = Nobody is on the Mentor roster.
wg-mentor-cmd-list-row = { $player }: { $tier }, { $state }
wg-mentor-state-active = active
wg-mentor-state-inactive = inactive
wg-mentor-state-suspended = suspended
