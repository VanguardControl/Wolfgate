using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Client.Clothing;
using Content.Client.Inventory;
using Content.Client._WF.CustomMarkings;
using Content.Shared._WF.AppearanceStudio;
using Content.Shared.DisplacementMap;
using Content.Shared.Hands.Components;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WF.AppearanceStudio;

/// <summary>
/// Draws the parts a player placed in the Appearance Studio: every frame, moves, scales and hides the sprite layers
/// of body parts, markings, worn and held items for the way the body faces, and hands the clothing and hands
/// systems the warps painted for them.
/// </summary>
/// <remarks>
/// Layer offsets can't differ per facing, so the facing's offset is applied as it changes. What was applied to each
/// layer is remembered, so other systems' offsets stay and a rebuilt layer (a new object) starts clean.
/// </remarks>
public sealed class StudioSpriteSystem : EntitySystem
{
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private ClientClothingSystem _clothing = default!;
    [Dependency] private CustomMarkingSystem _customMarkings = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private IResourceCache _resCache = default!;

    private const string WarpPrefix = "warp-";

    /// <summary>Facing forced by a preview (a SpriteView turned by buttons), which the transform doesn't show.</summary>
    private readonly Dictionary<EntityUid, Direction> _previewFacing = new();

    /// <summary>Extra in-hand layer keys of a preview doll, which has no real hands.</summary>
    private readonly Dictionary<EntityUid, Dictionary<string, HashSet<string>>> _previewHeld = new();

    private readonly Dictionary<EntityUid, Placed> _placed = new();

    /// <summary>Which marking (and body part) drew a marking layer id.</summary>
    private readonly Dictionary<string, (string Marking, HumanoidVisualLayers Part)> _markingLayers = new();

    private readonly Dictionary<string, DisplacementData> _warps = new();

    /// <summary>Warps asked for and not here yet; bodies wearing them are redrawn when they come.</summary>
    private readonly HashSet<string> _pendingWarps = new();

    private readonly List<EntityUid> _gone = new();

    /// <summary>Warps being painted, shown on a preview doll by the name of their in-memory RSI.</summary>
    private readonly Dictionary<(EntityUid, StudioTarget, string), (string Name, DisplacementData Data)> _previewWarps = new();

    private int _previewCount;

    private readonly List<(EntityUid, StudioTarget, string)> _staleWarps = new();

    /// <summary>What the studio applied to one entity's layers.</summary>
    private sealed class Placed
    {
        public List<StudioTweak>? Tweaks;
        public string Warps = string.Empty;
        public readonly Dictionary<string, Applied> Layers = new();
        public readonly HashSet<string> Touched = new();
    }

    private struct Applied
    {
        public SpriteComponent.Layer Layer;
        public Vector2 Offset;
        public float Scale;
    }

    public override void Initialize()
    {
        base.Initialize();

        _customMarkings.ArtLoaded += OnArtLoaded;
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        IndexMarkings();
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _customMarkings.ArtLoaded -= OnArtLoaded;
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<MarkingPrototype>())
            IndexMarkings();
    }

    private void IndexMarkings()
    {
        _markingLayers.Clear();
        foreach (var marking in _proto.EnumeratePrototypes<MarkingPrototype>())
        {
            foreach (var sprite in marking.Sprites)
            {
                if (sprite is SpriteSpecifier.Rsi rsi)
                    _markingLayers[$"{marking.ID}-{rsi.RsiState}"] = (marking.ID, marking.BodyPart);
            }
        }
    }

    /// <summary>Makes a preview doll use this facing for its offsets; null to go back to its transform.</summary>
    public void SetPreviewFacing(EntityUid uid, Direction? facing)
    {
        if (facing is { } dir)
            _previewFacing[uid] = dir;
        else
            _previewFacing.Remove(uid);
    }

    /// <summary>Tells the system which layers a preview doll draws for a hand, so held tweaks move them.</summary>
    public void SetPreviewHeld(EntityUid uid, string hand, HashSet<string>? keys)
    {
        if (keys == null)
        {
            if (_previewHeld.TryGetValue(uid, out var held))
                held.Remove(hand);

            return;
        }

        _previewHeld.GetOrNew(uid)[hand] = keys;
    }

    /// <summary>South 0, north 1, east 2, west 3: the facing a body shows now.</summary>
    public int GetFacing(EntityUid uid, SpriteComponent sprite)
    {
        Direction dir;
        if (_previewFacing.TryGetValue(uid, out var preview))
            dir = preview;
        else if (sprite.EnableDirectionOverride)
            dir = sprite.DirectionOverride;
        else
            dir = (_xform.GetWorldRotation(uid) + _eye.CurrentEye.Rotation).GetCardinalDir();

        return FacingIndex(dir);
    }

    public static int FacingIndex(Direction dir)
    {
        return dir switch
        {
            Direction.North => 1,
            Direction.East => 2,
            Direction.West => 3,
            _ => 0,
        };
    }

    public static Direction FacingDirection(int facing)
    {
        return facing switch
        {
            1 => Direction.North,
            2 => Direction.East,
            3 => Direction.West,
            _ => Direction.South,
        };
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = EntityQueryEnumerator<HumanoidAppearanceComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var humanoid, out var sprite))
        {
            if (humanoid.StudioTweaks.Count == 0 && !_placed.ContainsKey(uid))
                continue;

            Update((uid, humanoid, sprite));
        }

        foreach (var uid in _placed.Keys)
        {
            if (!Exists(uid))
                _gone.Add(uid);
        }

        // Preview dolls are respawned often and may never be placed; drop what they left behind.
        foreach (var uid in _previewFacing.Keys)
        {
            if (!Exists(uid))
                _gone.Add(uid);
        }

        foreach (var uid in _previewHeld.Keys)
        {
            if (!Exists(uid))
                _gone.Add(uid);
        }

        foreach (var key in _previewWarps.Keys)
        {
            if (!Exists(key.Item1))
                _staleWarps.Add(key);
        }

        foreach (var key in _staleWarps)
        {
            SetPreviewWarp(key.Item1, key.Item2, key.Item3, null);
        }

        _staleWarps.Clear();

        foreach (var uid in _gone)
        {
            _placed.Remove(uid);
            _previewFacing.Remove(uid);
            _previewHeld.Remove(uid);
        }

        _gone.Clear();
    }

    private void Update(Entity<HumanoidAppearanceComponent, SpriteComponent> ent)
    {
        var (uid, humanoid, sprite) = ent;
        Entity<SpriteComponent> body = (uid, sprite);
        var placed = _placed.GetOrNew(uid);

        if (!ReferenceEquals(placed.Tweaks, humanoid.StudioTweaks))
        {
            placed.Tweaks = humanoid.StudioTweaks;
            var warps = string.Join(',', StudioRules.Warps(humanoid.StudioTweaks));
            if (warps != placed.Warps)
            {
                placed.Warps = warps;
                Redraw(uid);
            }
        }

        var facing = GetFacing(uid, sprite);
        placed.Touched.Clear();

        foreach (var tweak in humanoid.StudioTweaks)
        {
            var offset = (Vector2) tweak.GetOffset(facing) / EyeManager.PixelsPerMeter;
            switch (tweak.Target)
            {
                case StudioTarget.Body:
                    if (!Enum.TryParse<HumanoidVisualLayers>(tweak.Key, out var part))
                        break;

                    if (_sprite.LayerMapTryGet(body.AsNullable(), part, out var partIndex, false))
                        Apply(placed, body, partIndex, tweak.Key, offset, tweak);

                    foreach (var layerId in humanoid.ClientMarkingLayers)
                    {
                        if (_markingLayers.TryGetValue(layerId, out var drawn) && drawn.Part == part)
                            Apply(placed, body, layerId, offset, tweak);
                    }

                    break;
                case StudioTarget.Marking:
                    foreach (var layerId in humanoid.ClientMarkingLayers)
                    {
                        if (_markingLayers.TryGetValue(layerId, out var drawn) && drawn.Marking == tweak.Key)
                            Apply(placed, body, layerId, offset, tweak);
                    }

                    break;
                case StudioTarget.Worn:
                    if (TryComp<InventorySlotsComponent>(uid, out var slots)
                        && slots.VisualLayerKeys.TryGetValue(tweak.Key, out var worn))
                        ApplyAll(placed, body, worn, offset, tweak);

                    break;
                case StudioTarget.Held:
                    if (Enum.TryParse<HandLocation>(tweak.Key, out var location)
                        && TryComp<HandsComponent>(uid, out var hands)
                        && hands.RevealedLayers is var revealed
                        && revealed.TryGetValue(location, out var held))
                        ApplyAll(placed, body, held, offset, tweak);

                    if (_previewHeld.TryGetValue(uid, out var previewHeld)
                        && previewHeld.TryGetValue(tweak.Key, out var previewKeys))
                        ApplyAll(placed, body, previewKeys, offset, tweak);

                    break;
            }
        }

        // Layers no tweak covers any more go back to how they were.
        if (placed.Layers.Count == placed.Touched.Count)
        {
            if (humanoid.StudioTweaks.Count == 0)
                _placed.Remove(uid);

            return;
        }

        var stale = new List<string>();
        foreach (var (key, applied) in placed.Layers)
        {
            if (placed.Touched.Contains(key))
                continue;

            stale.Add(key);
            // Body parts are keyed by enum in the sprite, so look for the layer itself rather than its key.
            foreach (var layer in sprite.AllLayers)
            {
                if (!ReferenceEquals(layer, applied.Layer))
                    continue;

                Restore(applied);
                break;
            }
        }

        foreach (var key in stale)
        {
            placed.Layers.Remove(key);
        }

        if (humanoid.StudioTweaks.Count == 0 && placed.Layers.Count == 0)
            _placed.Remove(uid);
    }

    private void ApplyAll(Placed placed, Entity<SpriteComponent> sprite, HashSet<string> keys, Vector2 offset, StudioTweak tweak)
    {
        foreach (var key in keys)
        {
            // A displacement map layer only feeds the shader; it isn't drawn.
            if (key.EndsWith("-displacement", StringComparison.Ordinal))
                continue;

            Apply(placed, sprite, key, offset, tweak);
        }
    }

    private void Apply(Placed placed, Entity<SpriteComponent> sprite, string key, Vector2 offset, StudioTweak tweak)
    {
        if (_sprite.LayerMapTryGet(sprite.AsNullable(), key, out var index, false))
            Apply(placed, sprite, index, key, offset, tweak);
    }

    private void Apply(Placed placed, Entity<SpriteComponent> sprite, int index, string key, Vector2 offset, StudioTweak tweak)
    {
        if (sprite.Comp[index] is not SpriteComponent.Layer layer || !placed.Touched.Add(key))
            return;

        if (!placed.Layers.TryGetValue(key, out var applied) || !ReferenceEquals(applied.Layer, layer))
            applied = new Applied { Layer = layer, Offset = Vector2.Zero, Scale = 1f };

        if (applied.Offset != offset)
        {
            _sprite.LayerSetOffset(layer, layer.Offset + offset - applied.Offset);
            applied.Offset = offset;
        }

        if (!applied.Scale.Equals(tweak.Scale) && applied.Scale > 0f)
        {
            _sprite.LayerSetScale(layer, layer.Scale / applied.Scale * tweak.Scale);
            applied.Scale = tweak.Scale;
        }

        if (tweak.Hidden && layer.Visible)
            _sprite.LayerSetVisible(layer, false);

        placed.Layers[key] = applied;
    }

    private void Restore(Applied applied)
    {
        _sprite.LayerSetOffset(applied.Layer, applied.Layer.Offset - applied.Offset);
        if (applied.Scale > 0f)
            _sprite.LayerSetScale(applied.Layer, applied.Layer.Scale / applied.Scale);
    }

    /// <summary>Draws the worn and held items again, so they pick up warps that changed or arrived.</summary>
    public void Redraw(EntityUid uid)
    {
        if (TryComp<InventoryComponent>(uid, out var inventory))
            _clothing.InitClothing(uid, inventory);

        if (!TryComp<HandsComponent>(uid, out var hands))
            return;

        foreach (var (name, hand) in hands.Hands)
        {
            if (hand.HeldEntity is { } held)
                RaiseLocalEvent(uid, new VisualsChangedEvent(GetNetEntity(held), name));
        }
    }

    /// <summary>The warp a body's player painted for what it wears in a slot, if it is here to draw.</summary>
    public bool TryGetWornWarp(EntityUid equipee, string slot, [NotNullWhen(true)] out DisplacementData? warp)
    {
        return TryGetWarp(equipee, StudioTarget.Worn, slot, out warp);
    }

    /// <summary>The warp a body's player painted for what it holds in a hand, if it is here to draw.</summary>
    public bool TryGetHeldWarp(EntityUid uid, HandLocation location, [NotNullWhen(true)] out DisplacementData? warp)
    {
        return TryGetWarp(uid, StudioTarget.Held, location.ToString(), out warp);
    }

    /// <summary>
    /// Shows a warp being painted on a preview doll in place of the part's saved one; null to stop. The doll must
    /// be redrawn (<see cref="Redraw"/>) to show it.
    /// </summary>
    public void SetPreviewWarp(EntityUid uid, StudioTarget target, string key, StudioWarp? warp)
    {
        var slot = (uid, target, key);
        if (_previewWarps.Remove(slot, out var old))
            CustomMarkingResources.For(_resCache).Remove(old.Name);

        if (warp == null)
            return;

        var name = $"studio-preview-{_previewCount++}";
        CustomMarkingResources.For(_resCache).Store(name, warp.ToArt().ToPng(), linear: true);
        _previewWarps[slot] = (name, WarpData(name));
    }

    private bool TryGetWarp(EntityUid uid, StudioTarget target, string key, [NotNullWhen(true)] out DisplacementData? warp)
    {
        if (_previewWarps.TryGetValue((uid, target, key), out var preview))
        {
            warp = preview.Data;
            return true;
        }

        warp = null;
        return TryComp<HumanoidAppearanceComponent>(uid, out var humanoid)
               && StudioRules.Find(humanoid.StudioTweaks, target, key) is { Warp: { } hash }
               && TryGetWarpData(hash, out warp);
    }

    /// <summary>A warp by hash as a displacement map, fetching its art the first time it is asked for.</summary>
    public bool TryGetWarpData(string hash, [NotNullWhen(true)] out DisplacementData? warp)
    {
        if (_warps.TryGetValue(hash, out warp))
            return true;

        if (!_customMarkings.TryGetArt(hash, out _))
        {
            _pendingWarps.Add(hash);
            return false;
        }

        if (!_customMarkings.TryGetPng(hash, out var png))
            return false;

        // The art loads as colours; a displacement map needs its bytes as they are.
        var name = WarpPrefix + hash;
        CustomMarkingResources.For(_resCache).Store(name, png, linear: true);

        warp = WarpData(name);
        _warps[hash] = warp;
        return true;
    }

    private static DisplacementData WarpData(string name)
    {
        return new DisplacementData
        {
            SizeMaps = new Dictionary<int, PrototypeLayerData>
            {
                [EyeManager.PixelsPerMeter] = new()
                {
                    RsiPath = CustomMarkingResources.PathFor(name).ToString(),
                    State = CustomMarkingResources.State,
                },
            },
        };
    }

    /// <summary>The indices of the sprite layers a part draws on a body now.</summary>
    public void CollectLayers(EntityUid uid, StudioTarget target, string key, List<int> indices)
    {
        indices.Clear();
        if (!TryComp<SpriteComponent>(uid, out var spriteComp) || !TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
            return;

        Entity<SpriteComponent?> sprite = (uid, spriteComp);
        int index;
        switch (target)
        {
            case StudioTarget.Body:
                if (!Enum.TryParse<HumanoidVisualLayers>(key, out var part))
                    return;

                if (_sprite.LayerMapTryGet(sprite, part, out index, false))
                    indices.Add(index);

                foreach (var layerId in humanoid.ClientMarkingLayers)
                {
                    if (_markingLayers.TryGetValue(layerId, out var drawn) && drawn.Part == part
                        && _sprite.LayerMapTryGet(sprite, layerId, out index, false))
                        indices.Add(index);
                }

                break;
            case StudioTarget.Marking:
                foreach (var layerId in humanoid.ClientMarkingLayers)
                {
                    if (_markingLayers.TryGetValue(layerId, out var drawn) && drawn.Marking == key
                        && _sprite.LayerMapTryGet(sprite, layerId, out index, false))
                        indices.Add(index);
                }

                break;
            case StudioTarget.Worn:
                if (TryComp<InventorySlotsComponent>(uid, out var slots) && slots.VisualLayerKeys.TryGetValue(key, out var worn))
                    AddKeys(sprite, worn, indices);

                break;
            case StudioTarget.Held:
                if (Enum.TryParse<HandLocation>(key, out var location)
                    && TryComp<HandsComponent>(uid, out var hands)
                    && hands.RevealedLayers is var revealed
                    && revealed.TryGetValue(location, out var held))
                    AddKeys(sprite, held, indices);

                if (_previewHeld.TryGetValue(uid, out var previewHeld) && previewHeld.TryGetValue(key, out var previewKeys))
                    AddKeys(sprite, previewKeys, indices);

                break;
        }
    }

    private void AddKeys(Entity<SpriteComponent?> sprite, HashSet<string> keys, List<int> indices)
    {
        foreach (var key in keys)
        {
            if (!key.EndsWith("-displacement", StringComparison.Ordinal) && _sprite.LayerMapTryGet(sprite, key, out var index, false))
                indices.Add(index);
        }
    }

    /// <summary>The body parts a doll draws, by HumanoidVisualLayers name, for the studio's outliner.</summary>
    public void CollectBodyParts(EntityUid uid, List<string> parts)
    {
        parts.Clear();
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        foreach (var part in Enum.GetValues<HumanoidVisualLayers>())
        {
            if (_sprite.LayerMapTryGet((uid, sprite), part, out var index, false)
                && sprite[index] is SpriteComponent.Layer { Visible: true } layer
                && layer.State.IsValid)
                parts.Add(part.ToString());
        }
    }

    /// <summary>The markings a body draws, by id.</summary>
    public void CollectMarkings(EntityUid uid, List<string> markings)
    {
        markings.Clear();
        if (!TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
            return;

        foreach (var layerId in humanoid.ClientMarkingLayers)
        {
            if (_markingLayers.TryGetValue(layerId, out var drawn) && !markings.Contains(drawn.Marking))
                markings.Add(drawn.Marking);
        }
    }

    private void OnArtLoaded(string hash)
    {
        if (!_pendingWarps.Remove(hash))
            return;

        var query = EntityQueryEnumerator<HumanoidAppearanceComponent>();
        while (query.MoveNext(out var uid, out var humanoid))
        {
            foreach (var tweak in humanoid.StudioTweaks)
            {
                if (tweak.Warp != hash)
                    continue;

                Redraw(uid);
                break;
            }
        }
    }
}
