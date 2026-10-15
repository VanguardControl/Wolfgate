using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.RoundSetup;

public sealed partial class WFRoundSetupWindow
{
    /// <summary>Defers background rebuilding while typing or using this window's detached dropdown popup.</summary>
    private bool IsEditingControls()
    {
        for (var focused = UserInterfaceManager.KeyboardFocused; focused != null; focused = focused.Parent)
        {
            if (focused == this)
                return true;
        }
        return HasOpenSelector(this);
    }

    private static bool HasOpenSelector(Control root)
    {
        foreach (var child in root.Children)
        {
            if (child is OptionButton option && option.OptionsScroll.VisibleInTree || HasOpenSelector(child))
                return true;
        }
        return false;
    }
}
