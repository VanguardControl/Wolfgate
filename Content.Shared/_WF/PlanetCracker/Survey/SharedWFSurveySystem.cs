using System.Numerics;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction.Events;
using Content.Shared.Mining;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.PlanetCracker.Survey;

/// <summary>
/// The one derivation of survey vocabulary, shared so the server's console state, the client's window and the vein
/// examine all rate a world the same way. Owns exactly two directed subscriptions, both on brand-new components:
/// the surveyor's use-in-hand, which only starts the DoAfter, and the deep vein's examine, which only reads.
/// Every write that reveals a vein is server-authored and lives in the server half.
/// </summary>
public sealed partial class SharedWFSurveySystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;

    /// <summary>The one shipped bands document every rating is measured against.</summary>
    public const string DefaultBands = "WFVeinRatingBands";

    /// <summary>Locale key prefix of the four yield band words the vein examine shows instead of a number.</summary>
    private const string YieldBandPrefix = "wf-vein-yield-band-";

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFSurveyorComponent, UseInHandEvent>(OnSurveyorUseInHand);
        SubscribeLocalEvent<WFDeepVeinComponent, ExaminedEvent>(OnVeinExamined);
    }

    /// <summary>Use in hand starts the scan DoAfter; the server's completion handler does the revealing.</summary>
    private void OnSurveyorUseInHand(Entity<WFSurveyorComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        // The surveyor deliberately carries no ItemToggleComponent: ItemToggleSystem.OnUseInHand sets args.Handled
        // unconditionally when ItemToggleComponent.OnUse is true (Content.Shared/Item/ItemToggle/ItemToggleSystem.cs:63),
        // so one of the two handlers would silently never fire.
        // No ActionBlocker call either: SharedInteractionSystem.UseInHandInteraction already ran CanInteract (:1218) and
        // CanUseHeldEntity (:1221), and setting Handled is what makes it call _useDelay.TryResetDelay (:1230) - which IS
        // the cooldown, configured by the prototype's UseDelay block.
        var doAfter = new DoAfterArgs(EntityManager,
            args.User,
            ent.Comp.ScanDuration,
            new WFSurveyScanDoAfterEvent(),
            ent.Owner,
            used: ent.Owner)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
        };

        _doAfter.TryStartDoAfter(doAfter);
        args.Handled = true;
    }

    /// <summary>Examine: nothing at all until this examiner has pulsed the vein, then the ore and a yield band.</summary>
    private void OnVeinExamined(Entity<WFDeepVeinComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (!IsRevealed(args.Examiner, GetNetEntity(ent.Owner)))
        {
            args.PushMarkup(Loc.GetString("wf-vein-examine-unknown"));
            return;
        }

        args.PushMarkup(Loc.GetString("wf-vein-examine-ore", ("ore", GetOreName(ent.Comp.Ore))));

        // A band word, never the number: the exact tonnage is F6's business and is not a survey readout.
        // Banded against the range the vein itself was stamped with, not the table default: the server scales that
        // range by the world's unsanctioned multiplier, so a fixed range would read every unsanctioned vein as
        // exceptional. An unstamped vein still falls back to the default range through the component's own default.
        args.PushMarkup(Loc.GetString("wf-vein-examine-yield",
            ("band", Loc.GetString(YieldBandKey(ent.Comp.TotalYield, ent.Comp.YieldRange)))));
    }

    /// <summary>Whether this viewer has revealed that vein with a surveyor pulse.</summary>
    public bool IsRevealed(EntityUid viewer, NetEntity vein)
    {
        return TryComp<WFSurveyedComponent>(viewer, out var surveyed) && surveyed.Revealed.Contains(vein);
    }

    /// <summary>The display name of an ore's dropped entity, falling back to the ore id when either lookup fails.</summary>
    public string GetOreName(ProtoId<OrePrototype> ore)
    {
        if (!_proto.TryIndex(ore, out var orePrototype))
            return ore.Id;

        return _proto.TryIndex(orePrototype.OreEntity, out var oreEntity) ? oreEntity.Name : ore.Id;
    }

    /// <summary>
    /// How good a table is, in the same units the bands document is written in: the weight-averaged per-ore value
    /// times the mean vein yield, doubled up by the unsanctioned multiplier on a world it is illegal to crack.
    /// </summary>
    public static float Score(WFVeinTablePrototype table, bool sanctioned)
    {
        var totalWeight = 0f;

        foreach (var entry in table.Ores.Values)
        {
            if (entry.Weight > 0f)
                totalWeight += entry.Weight;
        }

        if (totalWeight <= 0f)
            return 0f;

        var weightedValue = 0f;

        foreach (var entry in table.Ores.Values)
        {
            if (entry.Weight > 0f)
                weightedValue += entry.Weight / totalWeight * entry.Value;
        }

        var meanYield = (table.YieldRange.X + table.YieldRange.Y) / 2f;
        return weightedValue * meanYield * (sanctioned ? 1f : table.UnsanctionedMultiplier);
    }

    /// <summary>Which band a score falls in, by descending threshold.</summary>
    public static WFVeinRating Rate(float score, WFVeinRatingBandsPrototype bands)
    {
        if (score >= bands.VeryRich)
            return WFVeinRating.VeryRich;

        if (score >= bands.Rich)
            return WFVeinRating.Rich;

        if (score >= bands.Fair)
            return WFVeinRating.Fair;

        return WFVeinRating.Poor;
    }

    /// <summary>The locale key of the band word one vein's yield falls in, by quartile of its table's range.</summary>
    public static string YieldBandKey(int yield, Vector2 range)
    {
        var span = range.Y - range.X;

        if (span <= 0f)
            return YieldBandPrefix + "trace";

        var fraction = Math.Clamp((yield - range.X) / span, 0f, 1f);

        return fraction switch
        {
            < 0.25f => YieldBandPrefix + "trace",
            < 0.5f => YieldBandPrefix + "modest",
            < 0.75f => YieldBandPrefix + "strong",
            _ => YieldBandPrefix + "exceptional",
        };
    }
}
