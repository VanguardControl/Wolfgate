using System.Linq;
using System.Numerics;
using Content.Client._WF.CustomMarkings;
using Content.Client._WF.CustomMarkings.UI;
using Content.Client._WF.Stylesheets;
using Content.Client.DisplacementMap;
using Content.Client.Inventory;
using Content.Client.Lobby;
using Content.Client.Players.PlayTimeTracking;
using Content.Shared._WF.AppearanceStudio;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Humanoid;
using Content.Shared.Item;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.AppearanceStudio.UI;

/// <summary>
/// The Appearance Studio: the character on a big doll, an outliner of its body parts, markings, worn and held items,
/// an inspector that moves, scales, hides and warps the selected part per facing, and a timeline that plays,
/// pauses and steps the animated layers. Edits go to the profile as tweaks; visibility and playback are preview only.
/// </summary>
public sealed class StudioWindow : CustomMarkingWindow
{
    private const float CommitDelay = 0.3f;

    /// <summary>Items the doll can hold to place and warp the hands against.</summary>
    private static readonly string[] HeldSamples =
        { "Crowbar", "Wrench", "FlashlightLantern", "ToolboxMechanicalFilled", "FireExtinguisher", "WeaponPistolMk58", "WeaponRifleAk", "Spear" };

    [Dependency] private IEntityManager _ent = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IResourceCache _res = default!;
    [Dependency] private JobRequirementsManager _jobRequirements = default!;

    private readonly StudioSpriteSystem _studio;
    private readonly SpriteSystem _sprite;
    private readonly DisplacementMapSystem _displacement;
    private readonly CustomMarkingSystem _customMarkings;
    private readonly Func<HumanoidCharacterProfile?> _profile;

    private List<StudioTweak> _tweaks;
    private EntityUid _doll;
    private EntityUid? _heldItem;
    private string? _heldSample;
    private int _facing;
    private (StudioTarget Target, string Key)? _selected;

    private bool _playing = true;
    private float _speed = 1f;
    private readonly HashSet<SpriteComponent.Layer> _paused = new();
    private readonly HashSet<(StudioTarget, string)> _previewHidden = new();

    /// <summary>Every layer key the held sample added to the doll, displacement maps included.</summary>
    private readonly HashSet<string> _heldKeys = new();

    private bool _outlinerStale = true;
    private float _commitIn = -1f;
    private bool _dragging;

    private readonly StudioViewport _viewport;
    private readonly BoxContainer _outliner;
    private readonly BoxContainer _inspector;
    private readonly List<Button> _facingButtons = new();
    private readonly Button _playButton;
    private readonly Label _speedLabel;
    private readonly List<int> _indices = new();
    private readonly List<string> _names = new();

    /// <summary>The keys each outliner group listed last, for its Show all / Hide all buttons.</summary>
    private readonly Dictionary<StudioTarget, List<string>> _groupKeys = new();

    /// <summary>Width the inspector's wrapped text keeps to.</summary>
    private const float InspectorTextWidth = 290f;

    private StudioWarpWindow? _warpWindow;

    /// <summary>The tweaks as edited, sent a moment after each change settles and when the window closes.</summary>
    public event Action<List<StudioTweak>>? Changed;

    public StudioWindow(Func<HumanoidCharacterProfile?> profile)
    {
        IoCManager.InjectDependencies(this);
        _studio = _ent.System<StudioSpriteSystem>();
        _sprite = _ent.System<SpriteSystem>();
        _displacement = _ent.System<DisplacementMapSystem>();
        _customMarkings = _ent.System<CustomMarkingSystem>();
        _profile = profile;
        _tweaks = StudioRules.Clean(profile()?.StudioTweaks, StudioRules.GetLimits(_cfg));

        Title = Loc.GetString("wf-studio-title");
        MinSize = new Vector2(1000, 640);

        // Outliner
        _outliner = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
        var outlinerScroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, MinWidth = 230, Children = { _outliner } };

        // Viewport and its toolbar
        _viewport = new StudioViewport { VerticalExpand = true, HorizontalExpand = true, MinSize = new Vector2(360, 360) };
        _viewport.Dragged += OnDragged;
        _viewport.DragEnded += () =>
        {
            _dragging = false;
            ScheduleCommit();
        };

        var facingRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 2, HorizontalAlignment = HAlignment.Center };
        for (var i = 0; i < StudioRules.Facings; i++)
        {
            var facing = i;
            var button = new Button { Text = Loc.GetString($"wf-studio-facing-{facing}"), ToggleMode = true, MinWidth = 56 };
            button.OnPressed += _ => SetFacing(facing);
            _facingButtons.Add(button);
            facingRow.AddChild(button);
        }

        var zoom = new Slider { MinValue = 2, MaxValue = 14, Value = 8, MinWidth = 120 };
        zoom.OnValueChanged += range => _viewport.View.Scale = new Vector2(MathF.Round(range.Value));

        var heldPicker = new OptionButton { MinWidth = 150 };
        heldPicker.AddItem(Loc.GetString("wf-studio-held-none"), 0);
        var samples = new List<string?> { null };
        foreach (var sample in HeldSamples)
        {
            if (!_proto.TryIndex(sample, out var sampleProto))
                continue;

            heldPicker.AddItem(sampleProto.Name, samples.Count);
            samples.Add(sample);
        }

        heldPicker.OnItemSelected += args =>
        {
            heldPicker.SelectId(args.Id);
            _heldSample = samples[args.Id];
            RebuildHeld();
            _outlinerStale = true;
            RebuildInspector();
        };

        var toolbar = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8, HorizontalAlignment = HAlignment.Center };
        toolbar.AddChild(facingRow);
        toolbar.AddChild(new Label { Text = Loc.GetString("wf-studio-zoom") });
        toolbar.AddChild(zoom);
        toolbar.AddChild(new Label { Text = Loc.GetString("wf-studio-held-sample") });
        toolbar.AddChild(heldPicker);

        // Timeline
        _playButton = new Button { Text = Loc.GetString("wf-studio-pause-all"), MinWidth = 90 };
        _playButton.OnPressed += _ =>
        {
            _playing = !_playing;
            _playButton.Text = Loc.GetString(_playing ? "wf-studio-pause-all" : "wf-studio-play-all");
        };

        var step = new Button { Text = Loc.GetString("wf-studio-step-all") };
        step.OnPressed += _ => StepLayers(null);

        _speedLabel = new Label { Text = Loc.GetString("wf-studio-speed", ("speed", "1")), MinWidth = 80 };
        var speed = new Slider { MinValue = 0.25f, MaxValue = 3f, Value = 1f, MinWidth = 160 };
        speed.OnValueChanged += range =>
        {
            _speed = MathF.Round(range.Value * 4f) / 4f;
            _speedLabel.Text = Loc.GetString("wf-studio-speed", ("speed", _speed.ToString("0.##")));
        };

        var timeline = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        timeline.AddChild(new Label { Text = Loc.GetString("wf-studio-timeline"), StyleClasses = { StyleWolfgate.StyleClassCreatorFieldLabel } });
        timeline.AddChild(_playButton);
        timeline.AddChild(step);
        timeline.AddChild(_speedLabel);
        timeline.AddChild(speed);

        var centre = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6, HorizontalExpand = true };
        centre.AddChild(toolbar);
        centre.AddChild(_viewport);
        centre.AddChild(timeline);

        // Inspector
        _inspector = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        var inspectorScroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, MinWidth = 300, Children = { _inspector } };

        var columns = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 10, VerticalExpand = true };
        columns.AddChild(Card(outlinerScroll));
        columns.AddChild(centre);
        columns.AddChild(Card(inspectorScroll));

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6, Margin = new Thickness(8) };
        if (_jobRequirements.IsFeatureBanned(StudioRules.Feature))
            root.AddChild(StudioText.Wrapped(Loc.GetString("wf-studio-banned"), "Danger"));

        root.AddChild(columns);
        Contents.AddChild(root);

        OnClose += Closed;
        LoadDoll();
        SetFacing(0);
    }

    private static PanelContainer Card(Control child)
    {
        return new PanelContainer { StyleClasses = { StyleWolfgate.StyleClassCreatorCard }, Children = { child } };
    }

    #region Doll

    private void LoadDoll()
    {
        // The warp editor draws the old doll's sprite; it can't outlive it.
        _warpWindow?.Close();
        ClearHeld();
        if (_doll.IsValid())
            _ent.DeleteEntity(_doll);

        var profile = _profile();
        _doll = profile == null
            ? EntityUid.Invalid
            : UserInterfaceManager.GetUIController<LobbyUIController>().LoadProfileEntity(profile.WithStudioTweaks(_tweaks), null, true);

        _paused.Clear();
        _viewport.View.SetEntity(_doll.IsValid() ? _doll : null);
        _studio.SetPreviewFacing(_doll, StudioSpriteSystem.FacingDirection(_facing));
        RebuildHeld();
        _outlinerStale = true;
    }

    private void ClearHeld()
    {
        if (_doll.IsValid() && _ent.TryGetComponent<SpriteComponent>(_doll, out var sprite))
        {
            _studio.SetPreviewHeld(_doll, nameof(HandLocation.Left), null);
            _studio.SetPreviewHeld(_doll, nameof(HandLocation.Right), null);
            foreach (var key in _heldKeys)
            {
                _sprite.RemoveLayer((_doll, sprite), key, false);
            }
        }

        _heldKeys.Clear();

        if (_heldItem is { } item)
            _ent.DeleteEntity(item);

        _heldItem = null;
    }

    /// <summary>Puts the sample item in both of the doll's hands, drawn as a real hand would draw it.</summary>
    private void RebuildHeld()
    {
        ClearHeld();
        if (_heldSample == null || !_doll.IsValid() || !_ent.TryGetComponent<SpriteComponent>(_doll, out var sprite))
            return;

        var item = _ent.SpawnEntity(_heldSample, MapCoordinates.Nullspace);
        _heldItem = item;

        foreach (var hand in new[] { HandLocation.Left, HandLocation.Right })
        {
            var ev = new GetInhandVisualsEvent(_doll, hand);
            _ent.EventBus.RaiseLocalEvent(item, ev);

            var keys = new HashSet<string>();
            foreach (var (key, data) in ev.Layers)
            {
                var layerKey = $"wf-studio-held-{hand}-{key}";
                if (!keys.Add(layerKey))
                    continue;

                var index = _sprite.LayerMapReserve((_doll, sprite), layerKey);
                if (data.RsiPath == null && data.TexturePath == null)
                {
                    if (_ent.TryGetComponent<ItemComponent>(item, out var itemComp) && itemComp.RsiPath != null)
                        _sprite.LayerSetRsi((_doll, sprite), index, new ResPath(itemComp.RsiPath));
                    else if (_ent.TryGetComponent<SpriteComponent>(item, out var itemSprite))
                        _sprite.LayerSetRsi((_doll, sprite), index, itemSprite.BaseRSI);
                }

                _sprite.LayerSetData((_doll, sprite), index, data);
                if (_studio.TryGetHeldWarp(_doll, hand, out var warp))
                    _displacement.TryAddDisplacement(warp, sprite, index, layerKey, keys);
            }

            _studio.SetPreviewHeld(_doll, hand.ToString(), keys);
            _heldKeys.UnionWith(keys);
        }
    }

    /// <summary>Puts the edited tweaks on the doll. A reload rebuilds it, for what can't be undone in place.</summary>
    private void ShowTweaks(bool reload, bool redrawItems = false)
    {
        if (reload || !_ent.TryGetComponent<HumanoidAppearanceComponent>(_doll, out var humanoid))
        {
            LoadDoll();
            return;
        }

        humanoid.StudioTweaks = _tweaks.ConvertAll(t => t.Clone());
        if (redrawItems)
        {
            _studio.Redraw(_doll);
            RebuildHeld();
        }

        _outlinerStale = true;
    }

    #endregion

    #region Editing

    private StudioTweak Current()
    {
        var (target, key) = _selected!.Value;
        return StudioRules.Find(_tweaks, target, key)?.Clone() ?? new StudioTweak(target, key);
    }

    private void Commit(StudioTweak tweak, bool reload = false, bool redrawItems = false)
    {
        var before = StudioRules.Find(_tweaks, tweak.Target, tweak.Key);
        reload |= before is { Hidden: true } && !tweak.Hidden;
        redrawItems |= before?.Warp != tweak.Warp;

        _tweaks = StudioRules.Clean(StudioRules.With(_tweaks, tweak), StudioRules.GetLimits(_cfg));
        ShowTweaks(reload, redrawItems);
        ScheduleCommit();
        RebuildInspector();
    }

    private void ScheduleCommit()
    {
        _commitIn = CommitDelay;
    }

    /// <summary>Sends an edit still waiting out its commit delay now; nothing when none is waiting.</summary>
    public void FlushPending()
    {
        if (_commitIn >= 0f)
            Flush();
    }

    private void Flush()
    {
        _commitIn = -1f;
        Changed?.Invoke(_tweaks.ConvertAll(t => t.Clone()));
    }

    private void Nudge(Vector2i by)
    {
        if (_selected == null)
            return;

        var tweak = Current();
        tweak.Offsets[_facing] += by;
        Commit(tweak);
    }

    private void OnDragged(Vector2i step)
    {
        if (_selected == null)
            return;

        _dragging = true;
        Nudge(step);
    }

    private void SetFacing(int facing)
    {
        _facing = facing;
        var dir = StudioSpriteSystem.FacingDirection(facing);
        _viewport.View.OverrideDirection = dir;
        _studio.SetPreviewFacing(_doll, dir);
        for (var i = 0; i < _facingButtons.Count; i++)
        {
            _facingButtons[i].Pressed = i == facing;
        }

        RebuildInspector();
    }

    private void Select(StudioTarget target, string key)
    {
        _selected = (target, key);
        _outlinerStale = true;
        RebuildInspector();
    }

    #endregion

    #region Outliner

    private void RebuildOutliner()
    {
        _outlinerStale = false;
        _outliner.DisposeAllChildren();
        if (!_doll.IsValid())
            return;

        _studio.CollectBodyParts(_doll, _names);
        Group("wf-studio-group-body", StudioTarget.Body, _names);

        _studio.CollectMarkings(_doll, _names);
        Group("wf-studio-group-markings", StudioTarget.Marking, _names);

        _names.Clear();
        if (_ent.TryGetComponent<InventorySlotsComponent>(_doll, out var slots))
        {
            foreach (var (slot, keys) in slots.VisualLayerKeys)
            {
                if (keys.Count > 0)
                    _names.Add(slot);
            }
        }

        Group("wf-studio-group-worn", StudioTarget.Worn, _names);

        _names.Clear();
        _names.Add(nameof(HandLocation.Left));
        _names.Add(nameof(HandLocation.Right));
        Group("wf-studio-group-held", StudioTarget.Held, _names);
    }

    private void Group(string heading, StudioTarget target, List<string> keys)
    {
        var showAll = new Button { Text = Loc.GetString("wf-studio-show-all"), StyleClasses = { "OpenRight" }, Disabled = keys.Count == 0 };
        var hideAll = new Button { Text = Loc.GetString("wf-studio-hide-all"), StyleClasses = { "OpenLeft" }, Disabled = keys.Count == 0 };
        var groupKeys = keys.ToList();
        showAll.OnPressed += _ => SetGroupVisible(target, groupKeys, true);
        hideAll.OnPressed += _ => SetGroupVisible(target, groupKeys, false);
        _groupKeys[target] = groupKeys;

        _outliner.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 2),
            Children =
            {
                new Label { Text = Loc.GetString(heading), StyleClasses = { StyleWolfgate.StyleClassCreatorFieldLabel }, HorizontalExpand = true },
                showAll,
                hideAll,
            },
        });

        if (keys.Count == 0)
        {
            _outliner.AddChild(new Label { Text = Loc.GetString("wf-studio-group-empty"), StyleClasses = { "LabelSubText" } });
            return;
        }

        foreach (var key in keys)
        {
            var part = (target, key);
            var shown = new CheckBox
            {
                Pressed = !_previewHidden.Contains(part),
                ToolTip = Loc.GetString("wf-studio-preview-visible-tooltip"),
            };
            shown.OnToggled += args => SetPreviewVisible(target, key, args.Pressed);

            var tweaked = StudioRules.Find(_tweaks, target, key) != null;
            var select = new Button
            {
                Text = PartName(target, key) + (tweaked ? " •" : string.Empty),
                ToggleMode = true,
                Pressed = _selected == part,
                HorizontalExpand = true,
                ClipText = true,
            };
            select.OnPressed += _ => Select(target, key);

            _outliner.AddChild(new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                SeparationOverride = 4,
                Children = { shown, select },
            });
        }
    }

    private static string PartName(StudioTarget target, string key)
    {
        var loc = target switch
        {
            StudioTarget.Body => $"wf-studio-part-{key}",
            StudioTarget.Marking => $"marking-{key}",
            StudioTarget.Worn => $"wf-studio-slot-{key}",
            _ => $"wf-studio-hand-{key}",
        };

        return Loc.TryGetString(loc, out var name) ? name : key;
    }

    /// <summary>Shows or hides a whole outliner group in the preview.</summary>
    private void SetGroupVisible(StudioTarget target, List<string> keys, bool visible)
    {
        foreach (var key in keys)
        {
            SetPreviewVisible(target, key, visible);
        }

        _outlinerStale = true;
    }

    /// <summary>Hides every worn item but the selected one in the preview, to see it on the bare body.</summary>
    private void HideOtherClothing()
    {
        if (_selected is not { } selected || !_groupKeys.TryGetValue(StudioTarget.Worn, out var worn))
            return;

        foreach (var key in worn)
        {
            SetPreviewVisible(StudioTarget.Worn, key, selected.Target == StudioTarget.Worn && key == selected.Key);
        }

        _outlinerStale = true;
    }

    private void SetPreviewVisible(StudioTarget target, string key, bool visible)
    {
        if (visible)
        {
            _previewHidden.Remove((target, key));
            if (!_ent.TryGetComponent<SpriteComponent>(_doll, out var sprite))
                return;

            var hidden = StudioRules.Find(_tweaks, target, key) is { Hidden: true };
            _studio.CollectLayers(_doll, target, key, _indices);
            foreach (var index in _indices)
            {
                if (sprite[index] is SpriteComponent.Layer layer)
                    _sprite.LayerSetVisible(layer, !hidden);
            }
        }
        else
        {
            _previewHidden.Add((target, key));
        }
    }

    #endregion

    #region Inspector

    private void RebuildInspector()
    {
        _inspector.DisposeAllChildren();
        var limits = StudioRules.GetLimits(_cfg);

        if (_selected is not { } selected)
        {
            _inspector.AddChild(StudioText.Wrapped(Loc.GetString("wf-studio-select-hint"), "LabelSubText", InspectorTextWidth));
            return;
        }

        var tweak = Current();
        _inspector.AddChild(new Label { Text = PartName(selected.Target, selected.Key), StyleClasses = { StyleWolfgate.StyleClassCreatorHeading } });
        _inspector.AddChild(StudioText.Wrapped(
            Loc.GetString("wf-studio-facing-now", ("facing", Loc.GetString($"wf-studio-facing-{_facing}"))),
            "LabelSubText",
            InspectorTextWidth));

        // Offset
        var offset = tweak.GetOffset(_facing);
        _inspector.AddChild(Heading("wf-studio-offset"));
        _inspector.AddChild(OffsetRow("X", offset.X, new Vector2i(1, 0)));
        _inspector.AddChild(OffsetRow("Y", offset.Y, new Vector2i(0, 1)));
        _inspector.AddChild(StudioText.Wrapped(Loc.GetString("wf-studio-offset-hint", ("max", limits.MaxOffset)), "LabelSubText", InspectorTextWidth));

        var copyAll = ActionButton("wf-studio-copy-all", () =>
        {
            var t = Current();
            var at = t.GetOffset(_facing);
            for (var i = 0; i < StudioRules.Facings; i++)
            {
                t.Offsets[i] = at;
            }

            Commit(t);
        });

        var mirror = ActionButton("wf-studio-mirror", () =>
        {
            var other = _facing switch { 2 => 3, 3 => 2, _ => -1 };
            if (other < 0)
                return;

            var t = Current();
            var at = t.GetOffset(_facing);
            t.Offsets[other] = new Vector2i(-at.X, at.Y);
            Commit(t);
        });
        mirror.Disabled = _facing < 2;

        var resetFacing = ActionButton("wf-studio-reset-facing", () =>
        {
            var t = Current();
            t.Offsets[_facing] = Vector2i.Zero;
            Commit(t);
        });

        var resetPart = ActionButton("wf-studio-reset-part", () =>
        {
            var t = Current();
            Commit(new StudioTweak(t.Target, t.Key) { Warp = t.Warp }, reload: t.Hidden || !MathHelper.CloseTo(t.Scale, 1f));
        });

        foreach (var button in new[] { copyAll, mirror, resetFacing, resetPart })
        {
            _inspector.AddChild(button);
        }

        // Scale
        var scaleLabel = new Label { Text = Loc.GetString("wf-studio-scale", ("scale", tweak.Scale.ToString("0.00"))) };
        var scale = new Slider { MinValue = limits.MinScale, MaxValue = limits.MaxScale, Value = tweak.Scale, HorizontalExpand = true };
        scale.OnValueChanged += range =>
        {
            var t = Current();
            t.Scale = StudioRules.SnapScale(range.Value);
            scaleLabel.Text = Loc.GetString("wf-studio-scale", ("scale", t.Scale.ToString("0.00")));
            _tweaks = StudioRules.Clean(StudioRules.With(_tweaks, t), StudioRules.GetLimits(_cfg));
            ShowTweaks(false);
            ScheduleCommit();
        };
        _inspector.AddChild(Heading("wf-studio-scale-heading"));
        _inspector.AddChild(scaleLabel);
        _inspector.AddChild(scale);

        // Hide in game
        if (StudioRules.CanHide(selected.Target))
        {
            var hide = new CheckBox { Text = Loc.GetString("wf-studio-hide"), Pressed = tweak.Hidden, ToolTip = Loc.GetString("wf-studio-hide-tooltip") };
            hide.OnToggled += args =>
            {
                var t = Current();
                t.Hidden = args.Pressed;
                Commit(t);
            };
            _inspector.AddChild(hide);
        }

        // Warp
        if (StudioRules.CanWarp(selected.Target))
        {
            _inspector.AddChild(Heading("wf-studio-warp-heading"));
            var hideOthers = ActionButton("wf-studio-hide-other-clothing", HideOtherClothing);
            hideOthers.ToolTip = Loc.GetString("wf-studio-hide-other-clothing-tooltip");
            _inspector.AddChild(hideOthers);
            var warpsOn = limits.Warps;
            // A hand holds nothing until a sample is picked, and a warp of nothing shows nothing.
            var nothingHeld = selected.Target == StudioTarget.Held && _heldSample == null;
            var paint = new Button
            {
                Text = Loc.GetString(tweak.Warp == null ? "wf-studio-warp-paint" : "wf-studio-warp-edit"),
                Disabled = !warpsOn || nothingHeld,
                ToolTip = Loc.GetString(!warpsOn ? "wf-studio-warp-disabled" : nothingHeld ? "wf-studio-warp-hold-first" : "wf-studio-warp-tooltip"),
                HorizontalExpand = true,
            };
            paint.OnPressed += _ => OpenWarp();
            _inspector.AddChild(paint);
            if (warpsOn && nothingHeld)
                _inspector.AddChild(StudioText.Wrapped(Loc.GetString("wf-studio-warp-hold-first"), "LabelSubText", InspectorTextWidth));
        }

        // Playback of this part's layers
        _studio.CollectLayers(_doll, selected.Target, selected.Key, _indices);
        if (FirstAnimated() is { } first)
        {
            var paused = _paused.Contains(first.Layer);
            _inspector.AddChild(Heading("wf-studio-animation"));
            var play = new Button { Text = Loc.GetString(paused ? "wf-studio-play" : "wf-studio-pause") };
            play.OnPressed += _ =>
            {
                SetPartPaused(!_paused.Contains(first.Layer));
                RebuildInspector();
            };

            var stepPart = new Button { Text = Loc.GetString("wf-studio-step") };
            stepPart.OnPressed += _ =>
            {
                SetPartPaused(true);
                StepLayers(_indices);
                RebuildInspector();
            };

            _inspector.AddChild(new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                SeparationOverride = 4,
                Children =
                {
                    play,
                    stepPart,
                    new Label { Text = Loc.GetString("wf-studio-frame", ("frame", first.Layer.AnimationFrame + 1), ("frames", first.Frames)) },
                },
            });
        }
    }

    private static Label Heading(string loc)
    {
        return new Label { Text = Loc.GetString(loc), StyleClasses = { StyleWolfgate.StyleClassCreatorFieldLabel }, Margin = new Thickness(0, 6, 0, 0) };
    }

    private static Button ActionButton(string loc, Action act)
    {
        var button = new Button { Text = Loc.GetString(loc), HorizontalExpand = true };
        button.OnPressed += _ => act();
        return button;
    }

    private Control OffsetRow(string axis, int value, Vector2i unit)
    {
        var minus = new Button { Text = "-", MinWidth = 32 };
        minus.OnPressed += _ => Nudge(-unit);
        var plus = new Button { Text = "+", MinWidth = 32 };
        plus.OnPressed += _ => Nudge(unit);

        return new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4,
            Children =
            {
                new Label { Text = axis, MinWidth = 20 },
                minus,
                new Label { Text = Loc.GetString("wf-studio-pixels", ("value", value)), MinWidth = 60, Align = Label.AlignMode.Center },
                plus,
            },
        };
    }

    #endregion

    #region Playback

    private (SpriteComponent.Layer Layer, int Frames)? FirstAnimated()
    {
        if (!_ent.TryGetComponent<SpriteComponent>(_doll, out var sprite))
            return null;

        foreach (var index in _indices)
        {
            if (sprite[index] is SpriteComponent.Layer layer && Animated(layer) is { } frames)
                return (layer, frames);
        }

        return null;
    }

    private static int? Animated(SpriteComponent.Layer layer)
    {
        return layer.ActualRsi is { } rsi && rsi.TryGetState(layer.State, out var state) && state.IsAnimated
            ? state.DelayCount
            : null;
    }

    private void SetPartPaused(bool paused)
    {
        if (!_ent.TryGetComponent<SpriteComponent>(_doll, out var sprite))
            return;

        foreach (var index in _indices)
        {
            if (sprite[index] is not SpriteComponent.Layer layer)
                continue;

            if (paused)
                _paused.Add(layer);
            else
                _paused.Remove(layer);
        }
    }

    /// <summary>Moves layers on to their next frame: the given ones, or every animated layer of the doll.</summary>
    private void StepLayers(List<int>? indices)
    {
        if (!_ent.TryGetComponent<SpriteComponent>(_doll, out var sprite))
            return;

        var index = -1;
        foreach (var spriteLayer in sprite.AllLayers)
        {
            index++;
            if (indices != null && !indices.Contains(index)
                || spriteLayer is not SpriteComponent.Layer layer
                || Animated(layer) == null)
                continue;

            _sprite.LayerSetAutoAnimated(layer, false);
            _sprite.LayerSetAnimationTime(layer, layer.AnimationTime + layer.AnimationTimeLeft + 0.001f);
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_commitIn >= 0f && !_dragging)
        {
            _commitIn -= args.DeltaSeconds;
            if (_commitIn < 0f)
                Flush();
        }

        if (_outlinerStale)
            RebuildOutliner();

        if (!_ent.TryGetComponent<SpriteComponent>(_doll, out var sprite))
            return;

        foreach (var spriteLayer in sprite.AllLayers)
        {
            if (spriteLayer is not SpriteComponent.Layer layer || Animated(layer) == null)
                continue;

            if (!_playing || _paused.Contains(layer))
            {
                if (layer.AutoAnimated)
                    _sprite.LayerSetAutoAnimated(layer, false);

                continue;
            }

            if (MathHelper.CloseTo(_speed, 1f))
            {
                if (!layer.AutoAnimated)
                    _sprite.LayerSetAutoAnimated(layer, true);

                continue;
            }

            if (layer.AutoAnimated)
                _sprite.LayerSetAutoAnimated(layer, false);

            _sprite.LayerSetAnimationTime(layer, layer.AnimationTime + args.DeltaSeconds * _speed);
        }

        foreach (var (target, key) in _previewHidden)
        {
            _studio.CollectLayers(_doll, target, key, _indices);
            foreach (var index in _indices)
            {
                if (sprite[index] is SpriteComponent.Layer { Visible: true } layer)
                    _sprite.LayerSetVisible(layer, false);
            }
        }

        // Keep the inspector's indices on the selected part for the playback buttons.
        if (_selected is { } selected)
            _studio.CollectLayers(_doll, selected.Target, selected.Key, _indices);
    }

    #endregion

    #region Warp

    private void OpenWarp()
    {
        _warpWindow?.Close();
        if (_selected is not { } selected || !_ent.TryGetComponent<SpriteComponent>(_doll, out var sprite))
            return;

        _studio.CollectLayers(_doll, selected.Target, selected.Key, _indices);
        var itemLayers = new HashSet<int>(_indices);

        SixLabors.ImageSharp.PixelFormats.Rgba32[][]? frames = null;
        foreach (var index in _indices)
        {
            if (sprite[index] is SpriteComponent.Layer { ActualRsi: { } rsi } layer && layer.State.IsValid)
            {
                frames = StudioItemFrames.Read(_res, rsi, layer.State);
                if (frames != null)
                    break;
            }
        }

        StudioWarp? warp = null;
        if (Current().Warp is { } hash && _customMarkings.TryReadArt(hash, out var art))
            warp = StudioWarp.FromArt(art);

        var window = new StudioWarpWindow(PartName(selected.Target, selected.Key), frames, warp, _facing, sprite, itemLayers,
            facing => Current().GetOffset(facing));

        window.Previewed += painted =>
        {
            _studio.SetPreviewWarp(_doll, selected.Target, selected.Key, painted);
            RedrawItems(window, selected);
        };

        window.Saved += hash =>
        {
            _studio.SetPreviewWarp(_doll, selected.Target, selected.Key, null);
            var t = StudioRules.Find(_tweaks, selected.Target, selected.Key)?.Clone() ?? new StudioTweak(selected.Target, selected.Key);
            t.Warp = hash;
            Commit(t, redrawItems: true);
        };

        window.OnClose += () =>
        {
            if (_warpWindow == window)
                _warpWindow = null;

            _studio.SetPreviewWarp(_doll, selected.Target, selected.Key, null);
            if (_doll.IsValid())
                RedrawItems(null, selected);
        };

        _warpWindow = window;
        window.OpenCentered();
    }

    private void RedrawItems(StudioWarpWindow? window, (StudioTarget Target, string Key) part)
    {
        if (part.Target == StudioTarget.Held)
            RebuildHeld();
        else
            _studio.Redraw(_doll);

        if (window == null)
            return;

        _studio.CollectLayers(_doll, part.Target, part.Key, _indices);
        window.SetItemLayers(new HashSet<int>(_indices));
    }

    #endregion

    private void Closed()
    {
        _warpWindow?.Close();
        if (_commitIn >= 0f)
            Flush();

        ClearHeld();
        if (_doll.IsValid())
        {
            _studio.SetPreviewFacing(_doll, null);
            _ent.DeleteEntity(_doll);
        }

        _doll = EntityUid.Invalid;
    }
}
