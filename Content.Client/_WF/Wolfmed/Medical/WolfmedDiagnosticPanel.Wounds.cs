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
using Robust.Shared.Utility;

namespace Content.Client._WF.Wolfmed.Medical;

/// <summary>
/// UI2: the wounds tab. One card per injured body part, with the part's conditions as icon chips and its
/// wounds grouped by <see cref="WolfmedWoundCategory"/>, replacing the single joined line per part that
/// phase 5 shipped. Every string the old line printed is still here, on the row or in its tooltip.
/// </summary>
public sealed partial class WolfmedDiagnosticPanel
{
    private const float IconSize = 18f;
    private const float ChipIconSize = 14f;

    /// <summary>The category chip the medic clicked, or null for "show every part".</summary>
    private WolfmedWoundCategory? _categoryFilter;

    /// <summary>Whose findings are on screen, so the filter can be dropped when the patient changes.</summary>
    private NetEntity? _woundTarget;

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
                Loc.GetString("health-analyzer-wound-blood-level-dangerous")));

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
                    ("percent", (int) MathF.Round(msg.WoundDiagnostics.Sepsis)))));

        BuildCategoryStrip(msg.WoundDiagnostics);

        var cards = 0;
        foreach (var part in SharedTargetingSystem.GetValidParts())
        {
            if (!msg.WoundDiagnostics.Parts.TryGetValue(part, out var diagnostic))
                continue;

            if (_categoryFilter is { } filter &&
                !diagnostic.VisibleWounds.Any(wound => wound.Category == filter))
                continue;

            WoundFindingsContainer.AddChild(CreatePartCard(part, diagnostic));
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
            ToolTip = Loc.GetString("health-analyzer-wound-category-chip", ("category", name), ("count", count)),
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

    private Control CreateAlertRow(string icon, Color colour, string text)
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
        return panel;
    }

    private Control CreatePartCard(TargetBodyPart part, HealthAnalyzerWoundDiagnostic diagnostic)
    {
        var card = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = WolfmedWoundStyle.CardBackground,
                BorderColor = WolfmedWoundStyle.Accent(diagnostic),
                BorderThickness = new Thickness(3, 0, 0, 0),
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

        body.AddChild(CreateCardHeader(part, diagnostic));

        foreach (var row in CreateWoundRows(diagnostic))
            body.AddChild(row);

        if (CreateFooter(diagnostic) is { } footer)
            body.AddChild(footer);

        card.AddChild(body);
        return card;
    }

    /// <summary>Part name on the left, the part's conditions as icon chips on the right.</summary>
    private Control CreateCardHeader(TargetBodyPart part, HealthAnalyzerWoundDiagnostic diagnostic)
    {
        var header = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 4,
            HorizontalExpand = true,
        };

        header.AddChild(new Label
        {
            Text = Loc.GetString($"targeting-part-{PartKey(part)}"),
            StyleClasses = { "LabelHeading" },
            VerticalAlignment = VAlignment.Center,
        });
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
        var mechanical = diagnostic.Mechanical ? "-mechanical" : string.Empty;
        var frame = diagnostic.Mechanical ? "-frame" : string.Empty;
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
            chips.Add(CreateChip("fracture", WolfmedWoundStyle.Fracture, grade, text));
        }

        if (diagnostic.BleedingRate > 0f)
        {
            chips.Add(CreateChip(
                "bleeding",
                WolfmedWoundStyle.Bleeding,
                null,
                Loc.GetString($"health-analyzer-wound-bleeding-short{mechanical}")));
        }

        if (diagnostic.InternalBleedingRate > 0f)
        {
            chips.Add(CreateChip(
                "internal_bleeding",
                WolfmedWoundStyle.InternalBleeding,
                null,
                Loc.GetString("health-analyzer-wound-internal-bleeding-short")));
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
                    $"health-analyzer-wound-clotting-{diagnostic.ClottingPhase.ToString().ToLowerInvariant()}{mechanical}")));
        }

        // W1: before the scars, because it is the finding that decides what the medic does next.
        if (diagnostic.EmbeddedObjects > 0)
        {
            chips.Add(CreateChip(
                "embedded",
                WolfmedWoundStyle.Embedded,
                diagnostic.EmbeddedObjects.ToString(),
                Loc.GetString("health-analyzer-wound-embedded-short", ("count", diagnostic.EmbeddedObjects))));
        }

        // W5: dead tissue outranks everything else on the part; nothing but amputation clears it.
        if (diagnostic.Necrotic)
        {
            chips.Add(CreateChip("necrosis", WolfmedWoundStyle.Necrosis, null,
                Loc.GetString("health-analyzer-wound-necrotic-short")));
        }
        else if (diagnostic.NecrosisRisk)
        {
            chips.Add(CreateChip("necrosis", WolfmedWoundStyle.Pain, null,
                Loc.GetString("health-analyzer-wound-necrosis-risk-short")));
        }

        if (diagnostic.Infection != WolfmedInfectionStage.None)
        {
            var text = Loc.GetString(
                $"health-analyzer-wound-infection-{diagnostic.Infection.ToString().ToLowerInvariant()}");
            chips.Add(CreateChip("infection", WolfmedWoundStyle.Infection,
                diagnostic.Infection.ToString().ToLowerInvariant(), text));
        }

        // W6: a hot part reads as hot even once the wound itself has cooled past its first stage.
        if (diagnostic.Overheating)
        {
            chips.Add(CreateChip("overheating", WolfmedWoundStyle.Overheating, null,
                Loc.GetString("health-analyzer-wound-overheating-short")));
        }

        if (diagnostic.Functionality != BodyPartFunctionalityState.Functional)
        {
            chips.Add(CreateChip("impaired", WolfmedWoundStyle.Impaired, null,
                Loc.GetString(
                    $"health-analyzer-wound-functionality-{diagnostic.Functionality.ToString().ToLowerInvariant()}")));
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
            var row = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal,
                SeparationOverride = 5,
                HorizontalExpand = true,
                Margin = new Thickness(1, 2, 0, 0),
                ToolTip = Loc.GetString(WolfmedWoundCategories.NameKey(wound.Category)),
            };

            row.AddChild(Icon(WolfmedWoundCategories.IconState(wound.Category), colour, IconSize));
            row.AddChild(new Label
            {
                Text = Loc.GetString(wound.Name),
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

            rows.Add(row);
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
    private Control CreateChip(string icon, Color colour, string? text, string tooltip)
    {
        var panel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = WolfmedWoundStyle.ChipBackground },
            ToolTip = tooltip,
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
        return panel;
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
