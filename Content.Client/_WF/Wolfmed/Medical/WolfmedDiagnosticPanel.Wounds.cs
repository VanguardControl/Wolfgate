using System.Linq;
using System.Numerics;
using Content.Shared._Onyx.Medical;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.FixedPoint;
using Content.Shared.MedicalScanner;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.Wolfmed.Medical;

/// <summary>
/// UI2: the wounds tab. One card per body part, with the part's conditions as icon chips and its wounds
/// grouped by <see cref="WolfmedWoundCategory"/>, replacing the single joined line per part that phase 5
/// shipped. Every string the old line printed is still here, on the row or in its tooltip.
/// UI3: the card of the targeted part is marked and scrolled to, its header moves the target, and every
/// row, chip and banner carries treatment advice on hover and a procedure window on click.
/// </summary>
public sealed partial class WolfmedDiagnosticPanel
{
    private const float IconSize = 18f;
    private const float ChipIconSize = 14f;

    /// <summary>The category chip the medic clicked, or null for "show every part".</summary>
    private WolfmedWoundCategory? _categoryFilter;

    /// <summary>Whose findings are on screen, so the filter can be dropped when the patient changes.</summary>
    private NetEntity? _woundTarget;

    /// <summary>UI3: the local player's targeted body part, mirrored from the analyzer window.</summary>
    private TargetBodyPart? _targetedPart;

    /// <summary>UI3: the card to bring into view once layout has placed it.</summary>
    private Control? _scrollTo;

    /// <summary>
    /// UI3: set for the one redraw that follows a deliberate selection. Without it every scan tick would
    /// drag the list back to the targeted card while the medic is reading another one.
    /// </summary>
    private bool _scrollToTargeted;

    /// <summary>UI3: the procedure window, kept across repopulates. One per panel, not one per click.</summary>
    private WolfmedTreatmentWindow? _treatmentWindow;

    /// <summary>UI3: raised when the medic picks a part from a card header. The window moves the target.</summary>
    public event Action<TargetBodyPart>? OnPartSelected;

    /// <summary>UI3: called by the window whenever the local player's body-part target moves.</summary>
    public void SetTargetedPart(TargetBodyPart? part, bool scrollIntoView)
    {
        if (_targetedPart == part && !scrollIntoView)
            return;

        _targetedPart = part;
        _scrollToTargeted = scrollIntoView;
        RefreshWoundFilter();
        _scrollToTargeted = false;
    }

    /// <summary>Drains the pending scroll once the card it points at has been laid out.</summary>
    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_scrollTo is not { } card)
            return;

        _scrollTo = null;
        if (card.Parent == null)
            return;

        var offset = WoundsTab.GetScrollValue().Y;
        Control? walk = card;
        while (walk != null && walk != WoundsTab)
        {
            offset += walk.Position.Y;
            walk = walk.Parent;
        }

        if (walk != null)
            WoundsTab.VScrollTarget = MathF.Max(0f, offset - 8f);
    }

    private void DrawWoundDiagnostics(HealthAnalyzerScannedUserMessage msg)
    {
        // Populate runs on every scan update; the filter has to survive that, but not a new patient.
        if (_woundTarget != msg.TargetEntity)
        {
            _woundTarget = msg.TargetEntity;
            _categoryFilter = null;
        }

        WoundAlertsContainer.RemoveAllChildren();
        WoundCategoryStrip.RemoveAllChildren();
        WoundFindingsContainer.RemoveAllChildren();
        WoundStateLabel.Visible = true;

        VitalDamageRow.Visible = msg.VitalDamage != null;
        if (msg.VitalDamage is { } vital)
            VitalDamageLabel.Text = vital.ToString();

        if (msg.ScanMode != true)
        {
            WoundStateLabel.SetMessage(Loc.GetString("health-analyzer-wound-diagnostics-inactive"));
            return;
        }

        if (IsDangerousBloodLevel(msg.BloodLevel))
            WoundAlertsContainer.AddChild(CreateAlertRow(
                "blood_low",
                WolfmedWoundStyle.Bleeding,
                Loc.GetString("health-analyzer-wound-blood-level-dangerous"),
                "blood-low"));

        if (msg.WoundDiagnostics == null)
        {
            WoundStateLabel.SetMessage(Loc.GetString("health-analyzer-wound-diagnostics-unavailable"));
            return;
        }

        WoundStateLabel.Visible = false;

        // W5: systemic, so it is banner-level rather than an entry against any one part.
        if (msg.WoundDiagnostics.Sepsis > 0f)
            WoundAlertsContainer.AddChild(CreateAlertRow(
                "sepsis",
                WolfmedWoundStyle.Necrosis,
                Loc.GetString("health-analyzer-wound-sepsis",
                    ("percent", (int) MathF.Round(msg.WoundDiagnostics.Sepsis))),
                "sepsis"));

        BuildCategoryStrip(msg.WoundDiagnostics);

        var cards = 0;
        foreach (var part in SharedTargetingSystem.GetValidParts())
        {
            if (!msg.WoundDiagnostics.Parts.TryGetValue(part, out var diagnostic))
                continue;

            if (_categoryFilter is { } filter &&
                !diagnostic.VisibleWounds.Any(wound => wound.Category == filter))
                continue;

            var card = CreatePartCard(part, diagnostic);
            WoundFindingsContainer.AddChild(card);
            if (_scrollToTargeted && part == _targetedPart)
                _scrollTo = card;
            cards++;
        }

        if (cards > 0)
            return;

        var empty = _categoryFilter is { } shown
            ? Loc.GetString("health-analyzer-wound-no-findings-filtered",
                ("category", Loc.GetString(WolfmedWoundCategories.NameKey(shown))))
            : Loc.GetString("health-analyzer-wound-no-findings");

        WoundFindingsContainer.AddChild(new Label
        {
            Text = empty,
            StyleClasses = { "LabelSubText" },
            Margin = new Thickness(2, 2, 0, 0),
        });
    }

    /// <summary>One chip per category present on the patient. Clicking one filters the cards below.</summary>
    private void BuildCategoryStrip(HealthAnalyzerWoundDiagnostics diagnostics)
    {
        var counts = new Dictionary<WolfmedWoundCategory, int>();
        foreach (var diagnostic in diagnostics.Parts.Values)
        {
            foreach (var wound in diagnostic.VisibleWounds)
                counts[wound.Category] = counts.GetValueOrDefault(wound.Category) + wound.Count;
        }

        // A filter whose category has healed away would hide every card with no way back.
        if (_categoryFilter is { } filter && !counts.ContainsKey(filter))
            _categoryFilter = null;

        foreach (var category in WolfmedWoundCategories.All)
        {
            if (!counts.TryGetValue(category, out var count))
                continue;

            WoundCategoryStrip.AddChild(CreateCategoryChip(category, count));
        }
    }

    private Control CreateCategoryChip(WolfmedWoundCategory category, int count)
    {
        var name = Loc.GetString(WolfmedWoundCategories.NameKey(category));
        var colour = WolfmedWoundStyle.Category(category);
        var selected = _categoryFilter == category;

        var button = new Button
        {
            StyleClasses = { "OpenBoth" },
            // UI3: the count, then what the category means and what closes it.
            ToolTip = Tooltip(
                Loc.GetString("health-analyzer-wound-category-chip", ("category", name), ("count", count)),
                Advice(WolfmedTreatmentAdvice.CategoryShortKey(category), false)),
            Margin = new Thickness(0, 0, 3, 3),
            MinHeight = 22,
        };
        button.OnPressed += _ =>
        {
            _categoryFilter = selected ? null : category;
            RefreshWoundFilter();
        };

        var row = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 3,
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(4, 0),
        };
        row.AddChild(Icon(WolfmedWoundCategories.IconState(category), colour, ChipIconSize));
        row.AddChild(new Label
        {
            Text = count.ToString(),
            StyleClasses = { "LabelSubText" },
            FontColorOverride = selected ? Color.White : colour,
            VerticalAlignment = VAlignment.Center,
        });
        button.AddChild(row);
        button.Modulate = selected ? Color.White : new Color(0.8f, 0.8f, 0.8f);
        return button;
    }

    /// <summary>
    /// Redraws the cards from the message the panel is already holding. Cheaper than waiting for the next
    /// scan tick, which is up to a second away and would make the chip feel dead.
    /// </summary>
    private void RefreshWoundFilter()
    {
        if (_lastMessage is { } message)
            DrawWoundDiagnostics(message);
    }

    private Control CreateAlertRow(string icon, Color colour, string text, string condition)
    {
        var panel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = WolfmedWoundStyle.AlertBackground,
                BorderColor = colour,
                BorderThickness = new Thickness(3, 0, 0, 0),
            },
            Margin = new Thickness(0, 0, 0, 3),
            HorizontalExpand = true,
        };

        var row = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            HorizontalExpand = true,
            Margin = new Thickness(6, 3, 4, 3),
        };
        row.AddChild(Icon(icon, colour, IconSize));

        var label = new RichTextLabel { HorizontalExpand = true };
        label.SetMessage(FormattedMessage.FromMarkupPermissive(text));
        row.AddChild(label);

        panel.AddChild(row);
        // UI3: a banner is a finding like any other, so it opens the same procedure window. The tooltip uses
        // the plain title rather than the banner text, which carries colour markup a tooltip cannot render.
        var title = Loc.GetString($"health-analyzer-wound-banner-{condition}");
        return Clickable(panel, title, condition, false, title);
    }

    private Control CreatePartCard(TargetBodyPart part, HealthAnalyzerWoundDiagnostic diagnostic)
    {
        // UI3: the targeted part gets a full border in its accent colour rather than the left bar alone.
        var targeted = part == _targetedPart;
        var accent = WolfmedWoundStyle.Accent(diagnostic);
        var card = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = targeted ? WolfmedWoundStyle.CardTargeted : WolfmedWoundStyle.CardBackground,
                BorderColor = targeted ? WolfmedWoundStyle.TargetedBorder : accent,
                BorderThickness = targeted ? new Thickness(3, 1, 1, 1) : new Thickness(3, 0, 0, 0),
            },
            Margin = new Thickness(0, 0, 0, 4),
            HorizontalExpand = true,
        };

        var body = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
            Margin = new Thickness(6, 3, 4, 4),
        };

        body.AddChild(CreateCardHeader(part, diagnostic, targeted));

        foreach (var row in CreateWoundRows(diagnostic))
            body.AddChild(row);

        if (CreateFooter(diagnostic) is { } footer)
            body.AddChild(footer);

        card.AddChild(body);
        return card;
    }

    /// <summary>Part name on the left, the part's conditions as icon chips on the right.</summary>
    private Control CreateCardHeader(TargetBodyPart part, HealthAnalyzerWoundDiagnostic diagnostic, bool targeted)
    {
        var header = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 4,
            HorizontalExpand = true,
        };

        // UI3: the part name is the second way to aim; it does exactly what the doll does.
        var name = new ContainerButton
        {
            ToolTip = Loc.GetString("health-analyzer-wound-target-part-hint"),
            VerticalAlignment = VAlignment.Center,
        };
        name.AddChild(new Label
        {
            Text = Loc.GetString($"targeting-part-{PartKey(part)}"),
            StyleClasses = { "LabelHeading" },
            VerticalAlignment = VAlignment.Center,
        });
        name.OnPressed += _ => OnPartSelected?.Invoke(part);
        header.AddChild(name);

        if (targeted)
        {
            header.AddChild(new Label
            {
                Text = Loc.GetString("health-analyzer-wound-targeted-tag"),
                StyleClasses = { "LabelSubText" },
                FontColorOverride = WolfmedWoundStyle.TargetedBorder,
                VerticalAlignment = VAlignment.Center,
            });
        }

        header.AddChild(new Control { HorizontalExpand = true });

        foreach (var chip in CreateConditionChips(diagnostic))
            header.AddChild(chip);

        return header;
    }

    /// <summary>
    /// The part-level findings, in the order the old text line printed them. Wording is unchanged, so the
    /// W6 mechanical switch still picks the chassis variants.
    /// </summary>
    private List<Control> CreateConditionChips(HealthAnalyzerWoundDiagnostic diagnostic)
    {
        // W6: a chassis does not bleed or fracture, it leaks and deforms.
        var mech = diagnostic.Mechanical;
        var mechanical = mech ? "-mechanical" : string.Empty;
        var frame = mech ? "-frame" : string.Empty;
        var chips = new List<Control>();

        if (diagnostic.Fracture != FractureGrade.None)
        {
            var grade = Loc.GetString($"fracture-grade-{diagnostic.Fracture.ToString().ToLowerInvariant()}");
            var text = diagnostic.FractureTreatment == FractureTreatment.None
                ? Loc.GetString($"health-analyzer-wound-fracture-short{frame}", ("grade", grade))
                : Loc.GetString($"health-analyzer-wound-fracture-treated-short{frame}",
                    ("grade", grade),
                    ("treatment", Loc.GetString(
                        $"health-analyzer-wound-fracture-treatment-{diagnostic.FractureTreatment.ToString().ToLowerInvariant()}")));
            chips.Add(CreateChip("fracture", WolfmedWoundStyle.Fracture, grade, text, "fracture", mech));
        }

        if (diagnostic.BleedingRate > 0f)
        {
            chips.Add(CreateChip(
                "bleeding",
                WolfmedWoundStyle.Bleeding,
                null,
                Loc.GetString($"health-analyzer-wound-bleeding-short{mechanical}"),
                "bleeding",
                mech));
        }

        if (diagnostic.InternalBleedingRate > 0f)
        {
            chips.Add(CreateChip(
                "internal_bleeding",
                WolfmedWoundStyle.InternalBleeding,
                null,
                Loc.GetString("health-analyzer-wound-internal-bleeding-short"),
                "internal-bleeding",
                mech));
        }

        if (diagnostic.ClottingPhase is HealthAnalyzerClottingPhase.InProgress
            or HealthAnalyzerClottingPhase.Complete
            or HealthAnalyzerClottingPhase.Mixed)
        {
            chips.Add(CreateChip(
                "clotting",
                WolfmedWoundStyle.Clotting,
                null,
                Loc.GetString(
                    $"health-analyzer-wound-clotting-{diagnostic.ClottingPhase.ToString().ToLowerInvariant()}{mechanical}"),
                "clotting",
                mech));
        }

        // W1: before the scars, because it is the finding that decides what the medic does next.
        if (diagnostic.EmbeddedObjects > 0)
        {
            chips.Add(CreateChip(
                "embedded",
                WolfmedWoundStyle.Embedded,
                diagnostic.EmbeddedObjects.ToString(),
                Loc.GetString("health-analyzer-wound-embedded-short", ("count", diagnostic.EmbeddedObjects)),
                "embedded",
                mech));
        }

        // W5: dead tissue outranks everything else on the part; nothing but amputation clears it.
        if (diagnostic.Necrotic)
        {
            chips.Add(CreateChip("necrosis", WolfmedWoundStyle.Necrosis, null,
                Loc.GetString("health-analyzer-wound-necrotic-short"), "necrosis", mech));
        }
        else if (diagnostic.NecrosisRisk)
        {
            chips.Add(CreateChip("necrosis", WolfmedWoundStyle.Pain, null,
                Loc.GetString("health-analyzer-wound-necrosis-risk-short"), "necrosis-risk", mech));
        }

        if (diagnostic.Infection != WolfmedInfectionStage.None)
        {
            var text = Loc.GetString(
                $"health-analyzer-wound-infection-{diagnostic.Infection.ToString().ToLowerInvariant()}");
            chips.Add(CreateChip("infection", WolfmedWoundStyle.Infection,
                diagnostic.Infection.ToString().ToLowerInvariant(), text,
                WolfmedTreatmentAdvice.InfectionCondition(diagnostic.Infection), mech));
        }

        // W6: a hot part reads as hot even once the wound itself has cooled past its first stage.
        if (diagnostic.Overheating)
        {
            chips.Add(CreateChip("overheating", WolfmedWoundStyle.Overheating, null,
                Loc.GetString("health-analyzer-wound-overheating-short"), "overheating", mech));
        }

        if (diagnostic.Functionality != BodyPartFunctionalityState.Functional)
        {
            chips.Add(CreateChip("impaired", WolfmedWoundStyle.Impaired, null,
                Loc.GetString(
                    $"health-analyzer-wound-functionality-{diagnostic.Functionality.ToString().ToLowerInvariant()}"),
                WolfmedTreatmentAdvice.FunctionalityCondition(diagnostic.Functionality),
                mech));
        }

        return chips;
    }

    /// <summary>Visible wounds, one block per category, in the order the server sorted them.</summary>
    private List<Control> CreateWoundRows(HealthAnalyzerWoundDiagnostic diagnostic)
    {
        var rows = new List<Control>();
        foreach (var wound in diagnostic.VisibleWounds)
        {
            var colour = WolfmedWoundStyle.Category(wound.Category);
            var name = Loc.GetString(wound.Name);
            var row = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal,
                SeparationOverride = 5,
                HorizontalExpand = true,
                Margin = new Thickness(1, 2, 0, 0),
            };

            row.AddChild(Icon(WolfmedWoundCategories.IconState(wound.Category), colour, IconSize));
            row.AddChild(new Label
            {
                Text = name,
                VerticalAlignment = VAlignment.Center,
            });

            if (wound.StageName is { } stage)
            {
                row.AddChild(new Label
                {
                    Text = Loc.GetString(stage),
                    FontColorOverride = WolfmedWoundStyle.StageText,
                    VerticalAlignment = VAlignment.Center,
                });
            }

            if (wound.Count > 1)
            {
                row.AddChild(new Label
                {
                    Text = "x" + wound.Count,
                    FontColorOverride = colour,
                    VerticalAlignment = VAlignment.Center,
                });
            }

            // UI3: the row is the click target for that wound's procedure; the category name stays on hover.
            var category = Loc.GetString(WolfmedWoundCategories.NameKey(wound.Category));
            if (string.IsNullOrEmpty(wound.Prototype))
            {
                row.ToolTip = category;
                rows.Add(row);
                continue;
            }

            rows.Add(Clickable(
                row,
                Capitalize(name),
                WolfmedTreatmentAdvice.ShortKey(wound.Prototype),
                WolfmedTreatmentAdvice.StepsKey(wound.Prototype),
                diagnostic.Mechanical,
                category));
        }

        return rows;
    }

    /// <summary>Pain and scars: real findings, but never the reason a medic looks at the card.</summary>
    private Control? CreateFooter(HealthAnalyzerWoundDiagnostic diagnostic)
    {
        // W7: a chassis reports the same figure, but it is not pain.
        var mechanical = diagnostic.Mechanical ? "-mechanical" : string.Empty;
        var parts = new List<string>();

        if (diagnostic.Pain > FixedPoint2.Zero)
            parts.Add(Loc.GetString($"health-analyzer-wound-pain-short{mechanical}", ("pain", diagnostic.Pain)));

        if (diagnostic.ScarCount > 0)
            parts.Add(Loc.GetString("health-analyzer-wound-scars-short", ("count", diagnostic.ScarCount)));

        if (parts.Count == 0)
            return null;

        var row = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 5,
            HorizontalExpand = true,
            Margin = new Thickness(1, 3, 0, 0),
        };
        row.AddChild(Icon(diagnostic.ScarCount > 0 && diagnostic.Pain <= FixedPoint2.Zero ? "scar" : "pain",
            WolfmedWoundStyle.Scar,
            ChipIconSize));
        row.AddChild(new Label
        {
            Text = string.Join("   ", parts),
            StyleClasses = { "LabelSubText" },
            VerticalAlignment = VAlignment.Center,
        });
        return row;
    }

    /// <summary>A pill with a tinted pictogram, an optional short value, and the full wording on hover.</summary>
    private Control CreateChip(
        string icon,
        Color colour,
        string? text,
        string headline,
        string condition,
        bool mechanical)
    {
        var panel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = WolfmedWoundStyle.ChipBackground },
            Margin = new Thickness(0, 0, 2, 0),
            VerticalAlignment = VAlignment.Center,
        };

        var row = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 2,
            Margin = new Thickness(3, 2),
        };
        row.AddChild(Icon(icon, colour, ChipIconSize));

        if (!string.IsNullOrEmpty(text))
        {
            row.AddChild(new Label
            {
                Text = text,
                StyleClasses = { "LabelSubText" },
                FontColorOverride = colour,
                VerticalAlignment = VAlignment.Center,
            });
        }

        panel.AddChild(row);
        // UI3: the chip's own wording, then what to do about it, then the procedure on click.
        return Clickable(panel, Capitalize(headline), condition, mechanical, headline);
    }

    /// <summary>Wraps a condition's control so it hovers with advice and opens the procedure on click.</summary>
    private Control Clickable(Control content, string title, string condition, bool mechanical, string headline) =>
        Clickable(
            content,
            title,
            WolfmedTreatmentAdvice.ConditionShortKey(condition),
            WolfmedTreatmentAdvice.ConditionStepsKey(condition),
            mechanical,
            headline);

    private Control Clickable(
        Control content,
        string title,
        string shortKey,
        string stepsKey,
        bool mechanical,
        string headline)
    {
        var advice = Advice(shortKey, mechanical);
        var button = new ContainerButton
        {
            HorizontalExpand = content.HorizontalExpand,
            VerticalAlignment = content.VerticalAlignment,
            ToolTip = Tooltip(headline, advice),
        };
        button.AddChild(content);
        button.OnPressed += _ => OpenTreatment(title, advice, Advice(stepsKey, mechanical));
        return button;
    }

    /// <summary>The <c>-mechanical</c> variant where the data has one, else the plain key, else nothing.</summary>
    private static string Advice(string key, bool mechanical)
    {
        if (mechanical &&
            Loc.TryGetString(key + WolfmedTreatmentAdvice.MechanicalSuffix, out var chassis))
            return chassis;

        return Loc.TryGetString(key, out var advice) ? advice : string.Empty;
    }

    private static string Tooltip(string headline, string advice) =>
        string.IsNullOrEmpty(advice) ? headline : headline + "\n" + advice;

    private void OpenTreatment(string title, string summary, string steps)
    {
        if (string.IsNullOrEmpty(summary) && string.IsNullOrEmpty(steps))
            return;

        _treatmentWindow ??= new WolfmedTreatmentWindow();
        _treatmentWindow.Show(title, summary, steps);
    }

    private TextureRect Icon(string state, Color colour, float size) => new()
    {
        Texture = _icons.Get(state),
        SetSize = new Vector2(size, size),
        Stretch = TextureRect.StretchMode.KeepAspectCentered,
        Modulate = colour,
        VerticalAlignment = VAlignment.Center,
    };

    internal static bool IsDangerousBloodLevel(float level) => !float.IsNaN(level) && level < DangerousBloodLevel;

    private static string PartKey(TargetBodyPart part) => part.ToString()
        .Replace("Left", "left-")
        .Replace("Right", "right-")
        .ToLowerInvariant();
}
