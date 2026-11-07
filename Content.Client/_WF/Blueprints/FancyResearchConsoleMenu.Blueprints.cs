namespace Content.Client._Goobstation.Research.UI;

public sealed partial class FancyResearchConsoleMenu
{
    /// <summary>Runs the action when the Lost patterns button is pressed.</summary>
    public void WFBindPatternsButton(Action onPressed)
    {
        WFPatternsButton.OnPressed += _ => onPressed();
    }
}
