using System.Diagnostics.CodeAnalysis;
using System.Text;
using Content.Client.Clickable;
using Content.Shared._WF.CustomMarkings;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Graphics.RSI;

namespace Content.Client._WF.CustomMarkings;

// Art drawn on the body goes with the body parts under it: when one is hidden, such as a lost limb, the pixels
// that lay on it are left out. The cut copy is only made for bodies that have a part missing.
public sealed partial class CustomMarkingSystem
{
    [Dependency] private IClickMapManager _clickMap = default!;

    private const int MaxCutArt = 512;
    private const int MaxDecodedArt = 128;

    /// <summary>The body part layers that art can lie on.</summary>
    private static readonly HumanoidVisualLayers[] Limbs =
    {
        HumanoidVisualLayers.Chest,
        HumanoidVisualLayers.Head,
        HumanoidVisualLayers.RArm,
        HumanoidVisualLayers.LArm,
        HumanoidVisualLayers.RHand,
        HumanoidVisualLayers.LHand,
        HumanoidVisualLayers.RLeg,
        HumanoidVisualLayers.LLeg,
        HumanoidVisualLayers.RFoot,
        HumanoidVisualLayers.LFoot,
    };

    /// <summary>Cut art by what it was cut from and for which body; null where nothing of the art is left.</summary>
    private readonly Dictionary<string, RSI?> _cut = new();

    /// <summary>The pixels of fetched art, read back from its PNG the first time it has to be cut.</summary>
    private readonly Dictionary<string, CustomMarkingArt> _decoded = new();

    /// <summary>One body part layer lying under a marking.</summary>
    private readonly record struct Limb(int Index, HumanoidVisualLayers Part, RSI Rsi, RSI.StateId State, bool Facings, bool Hidden);

    /// <summary>Whether a placement draws on the body itself, rather than behind it or over hair and clothing.</summary>
    private static bool LiesOnBody(CustomMarkingPlacement placement)
    {
        return placement is CustomMarkingPlacement.Skin or CustomMarkingPlacement.Hands;
    }

    /// <summary>
    /// The art to draw for a marking on this body: the whole drawing, or, when body parts under it are hidden, a
    /// copy without the pixels that lay on them. Null when nothing is left to draw.
    /// </summary>
    /// <param name="depth">The layer index the marking will be added at; body parts at or above it draw over the art.</param>
    private RSI? ArtFor(Entity<SpriteComponent?> sprite, CustomMarking marking, RSI whole, int? depth)
    {
        if (!LiesOnBody(marking.Placement) || sprite.Comp is not { } comp)
            return whole;

        var limbs = new List<Limb>();
        var anyHidden = false;
        foreach (var part in Limbs)
        {
            if (!_sprite.LayerMapTryGet(sprite, part, out var index, false)
                || depth is { } above && index >= above
                || comp[index] is not SpriteComponent.Layer layer
                || layer.ActualRsi is not { } rsi
                || rsi.Size != new Vector2i(CustomMarkingRules.FrameSize, CustomMarkingRules.FrameSize)
                || !rsi.TryGetState(layer.State, out var state))
                continue;

            anyHidden |= !layer.Visible;
            limbs.Add(new Limb(index, part, rsi, layer.State, state.RsiDirections != RsiDirectionType.Dir1, !layer.Visible));
        }

        if (!anyHidden)
            return whole;

        // Lowest first, so the last one that is opaque at a pixel is the part the art there lies on.
        limbs.Sort((a, b) => a.Index.CompareTo(b.Index));

        var key = new StringBuilder(marking.Hash);
        foreach (var limb in limbs)
        {
            key.Append('|').Append((int) limb.Part).Append(limb.Hidden ? '-' : '+').Append(limb.Rsi.Path.ToString()).Append(':').Append(limb.State.Name);
        }

        var cutKey = key.ToString();
        if (_cut.TryGetValue(cutKey, out var known))
            return known;

        if (_cut.Count >= MaxCutArt)
            _cut.Clear();

        return _cut[cutKey] = Cut(marking.Hash, whole, limbs);
    }

    /// <summary>
    /// Where a mirror stands to split a body down its middle in one facing, read off the torso: the sum of its
    /// leftmost and rightmost columns, in the form <see cref="CustomMarkingSketch.MirrorAxis"/> takes. Null for a
    /// body with no torso sprite.
    /// </summary>
    public int? MirrorAxis(Entity<SpriteComponent?> sprite, int facing)
    {
        if (sprite.Comp is not { } comp
            || !_sprite.LayerMapTryGet(sprite, HumanoidVisualLayers.Chest, out var index, false)
            || comp[index] is not SpriteComponent.Layer layer
            || layer.ActualRsi is not { } rsi
            || !rsi.TryGetState(layer.State, out var state))
            return null;

        var direction = state.RsiDirections == RsiDirectionType.Dir1 ? RsiDirection.South : (RsiDirection) facing;
        int? left = null;
        var right = 0;
        for (var x = 0; x < rsi.Size.X; x++)
        {
            for (var y = 0; y < rsi.Size.Y; y++)
            {
                if (!_clickMap.IsOpaque(rsi, layer.State, direction, 0, new Vector2i(x, y)))
                    continue;

                left ??= x;
                right = x;
                break;
            }
        }

        return left + right;
    }

    private RSI? Cut(string hash, RSI whole, List<Limb> limbs)
    {
        if (!TryDecode(hash, out var art))
            return whole;

        var cut = art.Without((facing, x, y) =>
        {
            for (var i = limbs.Count - 1; i >= 0; i--)
            {
                var limb = limbs[i];
                var direction = limb.Facings ? (RsiDirection) facing : RsiDirection.South;
                if (_clickMap.IsOpaque(limb.Rsi, limb.State, direction, 0, new Vector2i(x, y)))
                    return limb.Hidden;
            }

            // On no body part at all: nothing to go missing with.
            return false;
        });

        if (cut.IsBlank())
            return null;

        if (cut.SamePixels(art))
            return whole;

        // Named after its own pixels, so the same cut is one RSI however it was reached, on this connection or the last.
        var name = $"{hash}-{cut.Fingerprint()}";
        if (!_resources.Has(name))
            _resources.Store(name, cut.ToPng());

        try
        {
            return _resCache.GetResource<RSIResource>(CustomMarkingResources.PathFor(name), false).RSI;
        }
        catch (Exception e)
        {
            Log.Error($"Custom marking art {name} didn't load: {e.Message}");
            return whole;
        }
    }

    private bool TryDecode(string hash, [NotNullWhen(true)] out CustomMarkingArt? art)
    {
        if (_decoded.TryGetValue(hash, out art))
            return true;

        if (!_resources.TryGetPng(hash, out var png) || CustomMarkingPng.Read(png) is not { } read)
            return false;

        if (_decoded.Count >= MaxDecodedArt)
            _decoded.Clear();

        art = _decoded[hash] = read;
        return true;
    }
}
