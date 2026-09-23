using Content.Client.Eui;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Eui;
using JetBrains.Annotations;
using Robust.Client.Graphics;

namespace Content.Client._WF.Wolfmed.Life;

/// <summary>The client half of the server's <c>WolfmedChoiceEui</c>. Closing the window is a no.</summary>
[UsedImplicitly]
public sealed class WolfmedChoiceEui : BaseEui
{
    private readonly WolfmedChoiceWindow _window = new();
    private bool _answered;

    public WolfmedChoiceEui()
    {
        _window.AcceptChoice.OnPressed += _ => Answer(true);
        _window.DenyChoice.OnPressed += _ => Answer(false);
        _window.OnClose += () => Answer(false);
    }

    public override void Opened()
    {
        IoCManager.Resolve<IClyde>().RequestWindowAttention();
        _window.OpenCentered();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is WolfmedChoiceEuiState choice)
            _window.SetChoice(choice.Title, choice.Text, choice.Accept, choice.Deny);
    }

    public override void Closed()
    {
        base.Closed();
        _answered = true;
        _window.Close();
    }

    private void Answer(bool accepted)
    {
        if (_answered)
            return;

        _answered = true;
        SendMessage(new WolfmedChoiceMessage(accepted));
        _window.Close();
    }
}
