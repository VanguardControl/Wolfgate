using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared.Guidebook;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WF.Wolfmed.Medical;

/// <summary>
/// UI3: the "what do I do about this" window. One instance per analyzer panel, refilled on each click, so
/// a medic clicking row after row keeps one window rather than collecting a stack of them.
/// </summary>
/// <remarks>
/// Built in code rather than XAML: a <see cref="FancyWindow"/> subclass with generated name references
/// cannot use the names the base window already owns, and this window has four controls.
/// </remarks>
public sealed class WolfmedTreatmentWindow : FancyWindow
{
    private const string TreatmentGuide = "WoundTreatment";

    private readonly RichTextLabel _summary;
    private readonly BoxContainer _steps;

    public WolfmedTreatmentWindow()
    {
        MinSize = new Vector2(340, 220);
        SetSize = new Vector2(420, 360);
        Resizable = true;
        HelpGuidebookIds = [new ProtoId<GuideEntryPrototype>(TreatmentGuide)];

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(6, 4, 6, 6),
        };

        _summary = new RichTextLabel { HorizontalExpand = true, Margin = new Thickness(0, 0, 0, 4) };
        root.AddChild(_summary);
        root.AddChild(new PanelContainer { StyleClasses = { "LowDivider" }, Margin = new Thickness(0, 0, 0, 4) });

        _steps = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
        };
        var scroll = new ScrollContainer
        {
            VerticalExpand = true,
            HorizontalExpand = true,
            HScrollEnabled = false,
        };
        scroll.AddChild(_steps);
        root.AddChild(scroll);

        var guidebook = new Button
        {
            Text = Loc.GetString("wolfmed-treatment-guidebook-button"),
            HorizontalAlignment = HAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0),
        };
        guidebook.OnPressed += _ => Help();
        root.AddChild(guidebook);

        AddChild(root);
    }

    /// <summary>Refills the window. <paramref name="steps"/> is one numbered step per line.</summary>
    public void Show(string title, string summary, string steps)
    {
        Title = title;
        _summary.SetMessage(FormattedMessage.FromMarkupPermissive(summary));

        _steps.RemoveAllChildren();
        foreach (var line in steps.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var label = new RichTextLabel
            {
                HorizontalExpand = true,
                Margin = new Thickness(0, 0, 0, 3),
            };
            label.SetMessage(FormattedMessage.FromMarkupPermissive(line.Trim()));
            _steps.AddChild(label);
        }

        if (!IsOpen)
            OpenCentered();
        else
            MoveToFront();
    }
}
