namespace Content.Client.UserInterface.Systems.Info;

public sealed partial class InfoUIController
{
    /// <summary>Raised once the player accepts the rules popup.</summary>
    public event Action? RulesAccepted;

    /// <summary>Whether the rules popup is up and waiting to be accepted.</summary>
    public bool RulesPopupOpen => _rulesPopup != null;
}
