# Mentors

A player-support tier between players and staff. Mentors are trusted players who answer gameplay questions through
Mentor Help; they have no moderation power and are not admins. This module holds the roster (its own table, not an
admin flag), the tiers (Trainee, Mentor, Senior Mentor), duty on and off, the 100-hour application and its checks,
mentor bans, the marks a mentor on duty shows, mentor chat, the staff Mentors tab and the commands.

Mentors are an entity system, not an admin flag or an IoC manager. `WGMentorSystem` loads a connecting player's
roster row, keeps active mentors and their duty in memory (a mentor may start every connection off duty, a client
setting), sends each client its status (`WGClientMentorSystem`) and everyone the on-duty count, and grants, revokes,
changes tier and restores, writing the row's history and a `WGMentor` admin log line. Staff use
`mentor list|grant|revoke|tier|restore` (Moderator); mentors use `mentor on|off`; anyone can run `mentorwho`.

Mentor chat is a chat channel (`MentorChat`, kept out of replays with the admin channels) for active mentors, on duty
or not, and staff with Adminhelp or Event: select Mentor in the chat box, start a line with `}`, or `msay <text>`.
The server side is `ChatManager.Mentors.cs`; the client works out who may select it in `ChatUIController.Mentors.cs`,
and `MentorsUIController` refreshes the chat box when a player's mentor status changes.

Marks: `WGMentorMarkTagSystem` puts `WGMentorMarkComponent` (tier, crown setting) on the body an on-duty mentor
controls, never a ghost. It gives the examine line "They are a Mentor." (a chip on the examine card), the tier's crown
plate in the card's footer band beside the Wolfgate mark (`WGMentorSigil`), and the crown plate over the head
(`WGMentorIconSystem`, status icons `WGMentorIcon*`) unless the mentor turned it off in the mentor window
(`wg.mentor.show_crown`). OOC and LOOC lines from an on-duty mentor carry the `[M]` tag (`MentorChatTag`). The plates
are drawn by `Tools/_WG/Mentors/gen_mentor_marks.py`: a low silver crown for Trainees, gold for Mentors, gold on an
olive plate for Seniors.

Guidebook: the Mentor Code of Conduct (`WGMentorCodeOfConduct`) and Managing Mentors for staff
(`WGMentorStaffGuide`), under "Getting help".

Becoming a mentor: `WGMentorApplicationSystem`. Players open Mentoring in the Esc menu (`MentoringWindow`): progress
toward `wg.mentor.min_hours` of overall playtime, what stops them applying (`MentorEligibility`: hours, a ban in
force, a mentor ban, a ban or a Medium-or-worse note inside `wg.mentor.clean_days`, the `wg.mentor.reapply_days` wait
after a denial, revocation or resignation), the five-question form and the Code of Conduct. `wg.mentor.application_mode`
picks `auto` (Trainee at once), `review` (queued) or `hybrid` (at once on a spotless record, which also means no minor
notes and no earlier revocation; queued otherwise). Staff with Moderator review in the Mentors tab and Senior Mentors
in their mentor window; a denial needs a reason the player sees. Players reaching the hours are told once (the client
keeps `wg.mentor.eligible_noticed`). Mentors resign there, and inactive ones reactivate; Link Discord opens the
panel's one-time link.

Roster upkeep (`WGMentorSystem.Upkeep.cs`): at round start and at connect, an active mentor away
`wg.mentor.inactive_days` goes inactive and one with a server ban or a mentor ban is suspended (staff restore). A
Trainee is promoted to Mentor after each resolved or rated ticket once they have `wg.mentor.promote_tickets` resolved,
`wg.mentor.promote_rating` of the rated ones helpful and `wg.mentor.promote_days` on the roster.

Bans (D5): role bans stored as `Mentor:Mentor` (a mentor ban: suspended at once, can't apply) and `Mentor:Help` (a
Mentor Help mute; the ahelp still works), without the `Job:` prefix. Placed from the Ban Panel's Mentor group or
`mentorban <player> <mentor|help> <minutes or 0> <reason>` (Ban).

Staff: the Mentors tab in the admin menu (`MentorStaffTab`, server side `WGMentorStaffSystem`) has the roster with
history and notes and grant, tier, revoke, restore and notes buttons (Moderator), pending applications, stored tickets
searchable by number or player or mentor name with transcripts (Adminhelp or Moderator), and the leaderboard with
escalations and fallbacks.

Leaderboard (`MentorBoard`, `WGMentorBoardSystem`): this week, this month and all time, cached five minutes, in the
mentor window and the staff tab. The mentor holding the claim at close gets the resolve and every other mentor who
posted an assist; a ticket the asker ended after an answer counts as resolved. Ranked by helpful resolves, then
resolves, then the faster median first answer; the rating share shows from five ratings. Watches and critter visits
are counted from the ticket's `watch` and `critter` rows. Reports per mentor aren't counted: a report isn't stored on
the ticket.

Panel: `Content.Server/Symphony/SymphonyMentorsSystem.cs` (see the Symphony README).

## Data

- `wolfgate_mentor`: one row per player who has been a mentor, kept after they leave the roster so the `history`
  stays. `tier` is `trainee`, `mentor` or `senior`; `state` is `active`, `inactive` or `suspended` while on the
  roster and `revoked` or `resigned` off it; `source` is `auto`, `staff` or `panel`. `history` is a JSON list of
  changes (when, who, what, why). The SSymphony panel reads it and may write it.
- `wolfgate_mentor_application`: each application with its form answers and record check as JSON; `state` is
  `pending`, `approved`, `auto_approved`, `denied` or `withdrawn`.

Read and written through `ServerDbManager.Mentors.cs`.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WG/Mentors/ChatManager.Mentors.cs`](ChatManager.Mentors.cs)
- [`Content.Server/_WG/Mentors/Commands/MentorBanCommand.cs`](Commands/MentorBanCommand.cs)
- [`Content.Server/_WG/Mentors/Commands/MentorCommand.cs`](Commands/MentorCommand.cs)
- [`Content.Server/_WG/Mentors/Commands/MentorSayCommand.cs`](Commands/MentorSayCommand.cs)
- [`Content.Server/_WG/Mentors/Commands/MentorWhoCommand.cs`](Commands/MentorWhoCommand.cs)
- [`Content.Server/_WG/Mentors/MentorBoard.cs`](MentorBoard.cs)
- [`Content.Server/_WG/Mentors/MentorChatTag.cs`](MentorChatTag.cs)
- [`Content.Server/_WG/Mentors/MentorEligibility.cs`](MentorEligibility.cs)
- [`Content.Server/_WG/Mentors/MentorRows.cs`](MentorRows.cs)
- [`Content.Server/_WG/Mentors/ServerDbBase.Mentors.cs`](ServerDbBase.Mentors.cs)
- [`Content.Server/_WG/Mentors/ServerDbManager.Mentors.cs`](ServerDbManager.Mentors.cs)
- [`Content.Server/_WG/Mentors/WGMentorApplicationSystem.cs`](WGMentorApplicationSystem.cs)
- [`Content.Server/_WG/Mentors/WGMentorBoardSystem.cs`](WGMentorBoardSystem.cs)
- [`Content.Server/_WG/Mentors/WGMentorMarkTagSystem.cs`](WGMentorMarkTagSystem.cs)
- [`Content.Server/_WG/Mentors/WGMentorStaffSystem.cs`](WGMentorStaffSystem.cs)
- [`Content.Server/_WG/Mentors/WGMentorSystem.cs`](WGMentorSystem.cs)
- [`Content.Server/_WG/Mentors/WGMentorSystem.Upkeep.cs`](WGMentorSystem.Upkeep.cs)

### Shared

- [`Content.Shared/_WG/Mentors/MentorBans.cs`](../../../Content.Shared/_WG/Mentors/MentorBans.cs)
- [`Content.Shared/_WG/Mentors/MentorBoardEvents.cs`](../../../Content.Shared/_WG/Mentors/MentorBoardEvents.cs)
- [`Content.Shared/_WG/Mentors/MentorCVars.cs`](../../../Content.Shared/_WG/Mentors/MentorCVars.cs)
- [`Content.Shared/_WG/Mentors/MentorEvents.cs`](../../../Content.Shared/_WG/Mentors/MentorEvents.cs)
- [`Content.Shared/_WG/Mentors/MentorPageEvents.cs`](../../../Content.Shared/_WG/Mentors/MentorPageEvents.cs)
- [`Content.Shared/_WG/Mentors/MentorStaffEvents.cs`](../../../Content.Shared/_WG/Mentors/MentorStaffEvents.cs)
- [`Content.Shared/_WG/Mentors/WGMentorMarkComponent.cs`](../../../Content.Shared/_WG/Mentors/WGMentorMarkComponent.cs)
- [`Content.Shared/_WG/Mentors/WGMentorMarkSystem.cs`](../../../Content.Shared/_WG/Mentors/WGMentorMarkSystem.cs)

### Client

- [`Content.Client/_WG/Mentors/BanPanel.Mentors.cs`](../../../Content.Client/_WG/Mentors/BanPanel.Mentors.cs)
- [`Content.Client/_WG/Mentors/ChatUIController.Mentors.cs`](../../../Content.Client/_WG/Mentors/ChatUIController.Mentors.cs)
- [`Content.Client/_WG/Mentors/MentorApplicationsControl.cs`](../../../Content.Client/_WG/Mentors/MentorApplicationsControl.cs)
- [`Content.Client/_WG/Mentors/MentorBoardControl.cs`](../../../Content.Client/_WG/Mentors/MentorBoardControl.cs)
- [`Content.Client/_WG/Mentors/MentoringUIController.cs`](../../../Content.Client/_WG/Mentors/MentoringUIController.cs)
- [`Content.Client/_WG/Mentors/MentoringWindow.cs`](../../../Content.Client/_WG/Mentors/MentoringWindow.cs)
- [`Content.Client/_WG/Mentors/MentorStaffTab.cs`](../../../Content.Client/_WG/Mentors/MentorStaffTab.cs)
- [`Content.Client/_WG/Mentors/MentorsUIController.cs`](../../../Content.Client/_WG/Mentors/MentorsUIController.cs)
- [`Content.Client/_WG/Mentors/WGClientMentorPageSystem.cs`](../../../Content.Client/_WG/Mentors/WGClientMentorPageSystem.cs)
- [`Content.Client/_WG/Mentors/WGClientMentorSystem.cs`](../../../Content.Client/_WG/Mentors/WGClientMentorSystem.cs)
- [`Content.Client/_WG/Mentors/WGMentorIconSystem.cs`](../../../Content.Client/_WG/Mentors/WGMentorIconSystem.cs)
- [`Content.Client/_WG/Mentors/WGMentorSigil.cs`](../../../Content.Client/_WG/Mentors/WGMentorSigil.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WG/Mentors/MentorApplicationTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Mentors/MentorApplicationTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Mentors/MentorBanTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Mentors/MentorBanTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Mentors/MentorChatTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Mentors/MentorChatTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Mentors/MentorDbTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Mentors/MentorDbTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Mentors/MentorMarksTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Mentors/MentorMarksTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Mentors/MentorRosterTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Mentors/MentorRosterTest.cs)

### Unit tests

- [`Content.Tests/_WG/Mentors/MentorBoardRulesTest.cs`](../../../Content.Tests/_WG/Mentors/MentorBoardRulesTest.cs)
- [`Content.Tests/_WG/Mentors/MentorEligibilityRulesTest.cs`](../../../Content.Tests/_WG/Mentors/MentorEligibilityRulesTest.cs)

### Prototypes

- [`Resources/Prototypes/_WG/Mentors/guidebook.yml`](../../../Resources/Prototypes/_WG/Mentors/guidebook.yml)
- [`Resources/Prototypes/_WG/Mentors/status_icons.yml`](../../../Resources/Prototypes/_WG/Mentors/status_icons.yml)

### Localization

- [`Resources/Locale/en-US/_WG/Mentors/mentors.ftl`](../../../Resources/Locale/en-US/_WG/Mentors/mentors.ftl)

### Textures

- [`Resources/Textures/_WG/Mentors/mentor_mark.rsi/`](../../../Resources/Textures/_WG/Mentors/mentor_mark.rsi/)

### Guidebook

- [`Resources/ServerInfo/_WG/Mentors/Guidebook/CodeOfConduct.xml`](../../../Resources/ServerInfo/_WG/Mentors/Guidebook/CodeOfConduct.xml)
- [`Resources/ServerInfo/_WG/Mentors/Guidebook/Staff.xml`](../../../Resources/ServerInfo/_WG/Mentors/Guidebook/Staff.xml)

### Tools

- [`Tools/_WG/Mentors/gen_mentor_marks.py`](../../../Tools/_WG/Mentors/gen_mentor_marks.py)

## Non-modular edits

- [`Content.Client/Administration/UI/AdminMenuWindow.xaml`](../../../Content.Client/Administration/UI/AdminMenuWindow.xaml)
  - xmlns:mentors for the Mentors tab
  - the staff Mentors tab
- [`Content.Client/Administration/UI/AdminMenuWindow.xaml.cs`](../../../Content.Client/Administration/UI/AdminMenuWindow.xaml.cs)
- [`Content.Client/Administration/UI/BanPanel/BanPanel.xaml.cs`](../../../Content.Client/Administration/UI/BanPanel/BanPanel.xaml.cs): mentor ban and Mentor Help mute boxes
- [`Content.Client/Chat/Managers/ChatManager.cs`](../../../Content.Client/Chat/Managers/ChatManager.cs): mentor chat
- [`Content.Client/Options/UI/EscapeMenu.xaml`](../../../Content.Client/Options/UI/EscapeMenu.xaml): the Mentoring page, to apply or manage being a Mentor
- [`Content.Client/UserInterface/Systems/Chat/ChatUIController.cs`](../../../Content.Client/UserInterface/Systems/Chat/ChatUIController.cs): mentors and staff see / filter mentor chat
- [`Content.Client/UserInterface/Systems/Chat/Controls/ChannelFilterPopup.xaml.cs`](../../../Content.Client/UserInterface/Systems/Chat/Controls/ChannelFilterPopup.xaml.cs)
- [`Content.Client/UserInterface/Systems/Chat/Controls/ChannelSelectorButton.cs`](../../../Content.Client/UserInterface/Systems/Chat/Controls/ChannelSelectorButton.cs)
- [`Content.Client/UserInterface/Systems/Chat/Controls/ChannelSelectorPopup.cs`](../../../Content.Client/UserInterface/Systems/Chat/Controls/ChannelSelectorPopup.cs)
- [`Content.Client/UserInterface/Systems/EscapeMenu/EscapeUIController.cs`](../../../Content.Client/UserInterface/Systems/EscapeMenu/EscapeUIController.cs): the Mentoring page in the game menu
- [`Content.Server.Database/Model.cs`](../../../Content.Server.Database/Model.cs)
  - the mentor roster and applications to join it
  - defaults for rows the panel writes, applications listed by player
- [`Content.Server/Administration/Managers/BanManager.cs`](../../Administration/Managers/BanManager.cs)
  - mentor bans aren't jobs
  - mentor bans keep their own prefix
  - a mentor ban suspends the mentor at once
- [`Content.Server/Chat/Managers/ChatManager.cs`](../../Chat/Managers/ChatManager.cs)
  - mentor chat
  - mentor tag on OOC
- [`Content.Server/Chat/Systems/ChatSystem.cs`](../../Chat/Systems/ChatSystem.cs): mentor tag on LOOC
- [`Content.Shared.Database/LogType.cs`](../../../Content.Shared.Database/LogType.cs): mentor roster, duty and ban actions
- [`Content.Shared/Chat/ChatChannel.cs`](../../../Content.Shared/Chat/ChatChannel.cs)
  - mentor chat
  - keep mentor chat out of replays too
- [`Content.Shared/Chat/ChatChannelExtensions.cs`](../../../Content.Shared/Chat/ChatChannelExtensions.cs): mentor chat tint
- [`Content.Shared/Chat/ChatSelectChannel.cs`](../../../Content.Shared/Chat/ChatSelectChannel.cs): mentor chat
- [`Content.Shared/Chat/SharedChatSystem.cs`](../../../Content.Shared/Chat/SharedChatSystem.cs): mentor chat

<!-- WOLFGATE-GENERATED END -->
