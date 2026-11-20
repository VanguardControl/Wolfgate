using Content.Client._WG.MentorHelp;

namespace Content.Client.UserInterface.Systems.Bwoink;

public sealed partial class AHelpUIController
{
    /// <summary>
    /// When the player picked the how-to topic, hands their text to Mentor Help instead of sending it to staff.
    /// True when it did.
    /// </summary>
    private bool WgSendToMentorHelp(string text)
    {
        if (UIHelper is not UserAHelpUIHandler { CategorySelector: { HowTo: true } selector })
            return false;

        selector.Reset();
        UIManager.GetUIController<MentorHelpUIController>().AskFromAHelp(text);
        return true;
    }
}
