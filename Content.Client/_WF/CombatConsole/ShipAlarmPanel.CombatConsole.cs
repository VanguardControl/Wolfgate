using System.Linq;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._WF.ShipPa.UI;

public sealed partial class ShipAlarmPanel
{
    private bool _wfInstrumentsRefitted;

    /// <summary>Rehouses the live PA controls into a compact bank without replacing their handlers.</summary>
    public void WfRefitInstruments()
    {
        if (_wfInstrumentsRefitted)
            return;
        _wfInstrumentsRefitted = true;

        var conditionCaption = ConditionLabel.Parent!.Children.OfType<Label>().Single(label => label != ConditionLabel);
        foreach (var label in new[] { conditionCaption, ConditionLabel, SpeakersCaption, SpeakersLabel })
        {
            label.Margin = new Thickness(0);
            label.MinHeight = 24;
            label.ClipText = true;
        }
        conditionCaption.ClipText = false;
        ConditionLabel.HorizontalExpand = true;
        SpeakersCaption.HorizontalExpand = SpeakersLabel.HorizontalExpand = true;
        SpeakersLabel.Align = Robust.Client.UserInterface.Controls.Label.AlignMode.Left;
        var readouts = Column(Row(conditionCaption, ConditionLabel), Column(SpeakersCaption, SpeakersLabel));
        readouts.SeparationOverride = 2;
        ((BoxContainer) SpeakersLabel.Parent!).SeparationOverride = 0;

        var codes = CodeContainer.Children.OfType<Button>().ToArray();
        foreach (var button in codes)
        {
            Detach(button);
            WfCompactAlarmButton(button);
        }
        CodeContainer.DisposeAllChildren();
        CodeContainer.Margin = new Thickness(0);
        CodeContainer.SeparationOverride = 4;
        for (var i = 0; i < codes.Length; i += 2)
        {
            var row = i + 1 < codes.Length ? Row(codes[i], codes[i + 1]) : Row(codes[i]);
            row.SeparationOverride = 4;
            CodeContainer.AddChild(row);
        }
        WfCompactAlarmButton(GeneralQuartersButton);
        WfCompactAlarmButton(CollisionAlertButton);
        WfCompactAlarmButton(AnnounceButton);
        WfCompactAlarmButton(SoundButton);
        WfCompactAlarmButton(SoundStopButton);
        AnnounceButton.HorizontalExpand = SoundButton.HorizontalExpand = SoundStopButton.HorizontalExpand = false;
        AnnounceButton.SetWidth = 84;
        SoundButton.SetWidth = 56;
        SoundStopButton.SetWidth = 48;
        foreach (var edit in new[] { AnnounceEdit, SoundEdit })
        {
            edit.Margin = new Thickness(0);
            edit.HorizontalExpand = true;
            edit.MinWidth = 100;
            edit.MinHeight = edit.SetHeight = 32;
        }
        var announce = Row(AnnounceEdit, AnnounceButton);
        var sound = Row(SoundEdit, SoundButton, SoundStopButton);
        announce.SeparationOverride = sound.SeparationOverride = 4;
        var body = Column(readouts, CodeContainer, GeneralQuartersButton, CollisionAlertButton, announce, sound);
        body.SeparationOverride = 6;
        DisposeAllChildren();
        Margin = new Thickness(0);
        HorizontalExpand = true;
        VerticalExpand = false;
        AddChild(body);
    }

    private static void WfCompactAlarmButton(Button button)
    {
        button.AddStyleClass("WfCompact");
        button.Margin = new Thickness(0);
        button.MinWidth = 0;
        button.MinHeight = button.SetHeight = 32;
        button.HorizontalExpand = true;
        Switch(button);
    }
}
