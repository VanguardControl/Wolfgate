using System.Diagnostics.CodeAnalysis;
using Content.Shared._WF.CustomMarkings;
using Content.Shared._WF.Genitals.Events;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;

namespace Content.Client._WF.CustomMarkings;

/// <summary>
/// Draws the custom markings a humanoid wears. Art is fetched from the server once per hash and kept as an RSI in
/// <see cref="CustomMarkingResources"/>; bodies whose art hasn't arrived yet get it when it does.
/// </summary>
public sealed partial class CustomMarkingSystem : EntitySystem
{
    [Dependency] private IResourceCache _resCache = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    /// <summary>Layer keys are numbered, so this is also the most markings one body can show.</summary>
    private const int MaxLayers = CustomMarkingRules.MaxWornCap;

    private CustomMarkingResources _resources = default!;

    private readonly Dictionary<string, RSI> _art = new();

    /// <summary>Hashes the server has no art for, so they aren't asked for again.</summary>
    private readonly HashSet<string> _unavailable = new();

    /// <summary>Hashes asked for and not yet answered.</summary>
    private readonly HashSet<string> _requested = new();

    private readonly List<string> _toRequest = new();

    /// <summary>Raised when the art for a hash becomes drawable.</summary>
    public event Action<string>? ArtLoaded;

    public override void Initialize()
    {
        base.Initialize();

        _resources = CustomMarkingResources.For(_resCache);

        SubscribeNetworkEvent<CustomMarkingArtEvent>(OnArt);
        // GenitalsVisualizerSystem owns (GenitalsComponent, HumanoidMarkingsAppliedEvent); this pair is ours alone.
        SubscribeLocalEvent<HumanoidAppearanceComponent, HumanoidMarkingsAppliedEvent>(OnMarkingsApplied);

        InitializeLibrary();
    }

    /// <summary>The art for a hash if it is here already. Otherwise asks the server for it, once.</summary>
    public bool TryGetArt(string hash, [NotNullWhen(true)] out RSI? rsi)
    {
        if (_art.TryGetValue(hash, out rsi))
            return true;

        if (!CustomMarkingRules.IsValidHash(hash) || _unavailable.Contains(hash))
            return false;

        // Fetched before a reconnect: the root outlives this system.
        if (_resources.Has(hash))
            return Load(hash, out rsi);

        if (_requested.Add(hash))
            _toRequest.Add(hash);

        return false;
    }

    /// <summary>
    /// Keeps art the player just saved under the hash the server gave it, so it shows without a trip to fetch it.
    /// </summary>
    public void Remember(string hash, CustomMarkingArt art)
    {
        if (!CustomMarkingRules.IsValidHash(hash) || _art.ContainsKey(hash))
            return;

        _unavailable.Remove(hash);
        _resources.Store(hash, art.ToPng());
        if (Load(hash, out _))
            ArtLoaded?.Invoke(hash);
    }

    /// <summary>The PNG sheet for a hash that <see cref="TryGetArt"/> has returned.</summary>
    public bool TryGetPng(string hash, [NotNullWhen(true)] out byte[]? png)
    {
        return _resources.TryGetPng(hash, out png);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        while (_toRequest.Count > 0)
        {
            var count = Math.Min(_toRequest.Count, CustomMarkingRules.MaxRequestedArt);
            RaiseNetworkEvent(new CustomMarkingArtRequestEvent(_toRequest.GetRange(0, count)));
            _toRequest.RemoveRange(0, count);
        }
    }

    private void OnArt(CustomMarkingArtEvent ev)
    {
        if (!CustomMarkingRules.IsValidHash(ev.Hash))
            return;

        var asked = _requested.Remove(ev.Hash);
        if (ev.Png == null)
        {
            // Also sent unasked when an admin blocks art, so anything already fetched is dropped.
            _unavailable.Add(ev.Hash);
            _resources.Remove(ev.Hash);
            if (_art.Remove(ev.Hash))
                RefreshWearers(ev.Hash);

            return;
        }

        if (!asked)
            return;

        _resources.Store(ev.Hash, ev.Png);
        if (!Load(ev.Hash, out _))
            return;

        RefreshWearers(ev.Hash);
        ArtLoaded?.Invoke(ev.Hash);
    }

    private void RefreshWearers(string hash)
    {
        var query = EntityQueryEnumerator<HumanoidAppearanceComponent>();
        while (query.MoveNext(out var uid, out var humanoid))
        {
            foreach (var marking in humanoid.CustomMarkings)
            {
                if (marking.Hash != hash)
                    continue;

                Refresh((uid, humanoid));
                break;
            }
        }
    }

    private bool Load(string hash, [NotNullWhen(true)] out RSI? rsi)
    {
        try
        {
            rsi = _resCache.GetResource<RSIResource>(CustomMarkingResources.PathFor(hash), false).RSI;
        }
        catch (Exception e)
        {
            Log.Error($"Custom marking art {hash} didn't load: {e.Message}");
            _unavailable.Add(hash);
            rsi = null;
            return false;
        }

        _art[hash] = rsi;
        return true;
    }

    private void OnMarkingsApplied(Entity<HumanoidAppearanceComponent> ent, ref HumanoidMarkingsAppliedEvent args)
    {
        Refresh(ent);
    }

    /// <summary>Rebuilds a body's custom marking layers from the list on its humanoid component.</summary>
    public void Refresh(Entity<HumanoidAppearanceComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var spriteComp))
            return;

        var sprite = new Entity<SpriteComponent?>(ent, spriteComp);
        for (var i = 0; i < MaxLayers; i++)
        {
            _sprite.RemoveLayer(sprite, LayerKey(i), false);
        }

        var worn = ent.Comp.CustomMarkings;
        for (var i = 0; i < worn.Count && i < MaxLayers; i++)
        {
            var marking = worn[i];
            if (!TryGetArt(marking.Hash, out var rsi))
                continue;

            var layer = _sprite.AddRsiLayer(sprite, CustomMarkingResources.State, rsi, GetLayerIndex(sprite, marking.Placement));
            _sprite.LayerMapSet(sprite, LayerKey(i), layer);
            _sprite.LayerSetVisible(sprite, layer, IsShown(ent.Comp, marking.Placement));
        }
    }

    /// <summary>The key of the layer drawing a body's custom marking at this position in its list.</summary>
    public static string LayerKey(int index)
    {
        return $"wf-custom-marking-{index}";
    }

    /// <summary>
    /// Where a placement's layer goes: just under the first layer that draws over it, which puts it above the
    /// body part's own markings and above custom markings added before it. Null, for the top, when the sprite has
    /// no such layer.
    /// </summary>
    public int? GetLayerIndex(Entity<SpriteComponent?> sprite, CustomMarkingPlacement placement)
    {
        int index;
        switch (placement)
        {
            case CustomMarkingPlacement.Behind:
                return _sprite.LayerMapTryGet(sprite, HumanoidVisualLayers.Chest, out index, false) ? index : 0;
            case CustomMarkingPlacement.Skin:
                if (_sprite.LayerMapTryGet(sprite, HumanoidVisualLayers.Genital, out index, false)
                    || _sprite.LayerMapTryGet(sprite, "jumpsuit", out index, false))
                    return index;

                break;
            case CustomMarkingPlacement.Hands:
                if (_sprite.LayerMapTryGet(sprite, "gloves", out index, false))
                    return index;

                break;
            case CustomMarkingPlacement.Hair:
                if (_sprite.LayerMapTryGet(sprite, HumanoidVisualLayers.HeadSide, out index, false)
                    || _sprite.LayerMapTryGet(sprite, HumanoidVisualLayers.HeadTop, out index, false))
                    return index;

                break;
            case CustomMarkingPlacement.Front:
                if (_sprite.LayerMapTryGet(sprite, "helmetcover", out index, false))
                    return index;

                break;
        }

        return null;
    }

    /// <summary>A custom marking is hidden along with the body layer clothing hides at its depth.</summary>
    private static bool IsShown(HumanoidAppearanceComponent humanoid, CustomMarkingPlacement placement)
    {
        HumanoidVisualLayers? follows = placement switch
        {
            CustomMarkingPlacement.Behind or CustomMarkingPlacement.Front => HumanoidVisualLayers.Tail,
            CustomMarkingPlacement.Skin => HumanoidVisualLayers.Chest,
            CustomMarkingPlacement.Hair => HumanoidVisualLayers.Hair,
            _ => null,
        };

        return follows is not { } layer
               || !humanoid.HiddenLayers.ContainsKey(layer) && !humanoid.PermanentlyHidden.Contains(layer);
    }
}
