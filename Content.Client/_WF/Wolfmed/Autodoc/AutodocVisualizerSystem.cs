using Content.Shared._WF.Wolfmed.Autodoc;
using Robust.Client.GameObjects;
using Robust.Shared.Maths;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._WF.Wolfmed.Autodoc;

/// <summary>
/// Draws the pod. Two things a GenericVisualizer cannot do: a layer colour it sets is sticky, so the dim
/// unpowered colour stayed on the sprite for the rest of the round once the pod had ever been unpowered
/// (which it always is for the tick between map init and the power net's first update); and the occupant
/// has to be above the bed with the lid open and under it with the lid closed, which is a draw depth the
/// pod's own layers cannot express.
/// </summary>
public sealed class AutodocVisualizerSystem : VisualizerSystem<AutodocComponent>
{
    /// <summary>Colour of the base layer with no power. Every other state draws the art as it was authored.</summary>
    private static readonly Color Unpowered = Color.FromHex("#555555");

    protected override void OnAppearanceChange(EntityUid uid, AutodocComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null ||
            !AppearanceSystem.TryGetData<AutodocVisualState>(uid, AutodocVisuals.State, out var state, args.Component))
            return;

        var sprite = (uid, args.Sprite);
        var lidClosed = state is AutodocVisualState.Closed or AutodocVisualState.Operating;

        SpriteSystem.LayerSetColor(sprite, AutodocVisualLayers.Base,
            state == AutodocVisualState.Unpowered ? Unpowered : Color.White);

        SpriteSystem.LayerSetVisible(sprite, AutodocVisualLayers.Lid, lidClosed);
        if (lidClosed)
            SpriteSystem.LayerSetRsiState(sprite, AutodocVisualLayers.Lid,
                state == AutodocVisualState.Operating ? "operate" : "closed");

        // Open: the bed draws under the occupant lying in it. Closed: the lid draws over them.
        SpriteSystem.SetDrawDepth(sprite, (int) (lidClosed ? DrawDepth.OverMobs : DrawDepth.BelowMobs));
    }
}

public enum AutodocVisualLayers : byte
{
    Base,
    Lid,
}
