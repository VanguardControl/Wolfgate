using Content.Client._WF.Stylesheets;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetCracker.Survey;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.GameStates;

namespace Content.Client._WF.PlanetCracker.Survey;

/// <summary>
/// Flips a deep vein's <see cref="WFDeepVeinVisualLayers.Marker"/> sprite layer on for veins the LOCAL player has
/// pulsed, and sets its state and ore tint. This is what makes a revealed vein drawable and therefore clickable and
/// examinable, and it is per-player for free because it is a client-local sprite write and never an Appearance key.
/// It also does not collide with the _CE ZLevels draw-depth trap: those systems overwrite DrawDepth every frame, and
/// nothing here touches DrawDepth - only layer visibility, state and colour, on a state-change event.
/// </summary>
public sealed partial class WFDeepVeinVisualsSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    /// <summary>
    /// Which skin colour each ore tints its vein with. Keyed by the verified OrePrototype ids the Asclepiu table
    /// rolls from; anything else falls back to the skin's accent, so a new ore is dull rather than invisible.
    /// </summary>
    private static readonly Dictionary<string, Func<WolfgateSkin, Color>> OreTints = new()
    {
        ["OreSteel"] = skin => skin.TextMuted,
        ["OreCoal"] = skin => skin.TextDisabled,
        ["OreSpaceQuartz"] = skin => skin.EdgeLight,
        ["OreSilver"] = skin => skin.Text,
        ["OreGold"] = skin => skin.Caution,
        ["OrePlasma"] = skin => skin.Danger,
        ["OreUranium"] = skin => skin.Good,
    };

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        // This system owns EXACTLY these two pairs. WFDeepVeinOverlaySystem owns ComponentInit, ComponentShutdown,
        // LocalPlayerAttachedEvent and LocalPlayerDetachedEvent on WFSurveyedComponent; a second directed
        // subscription on any of those pairs crashes the client at start.
        SubscribeLocalEvent<WFSurveyedComponent, AfterAutoHandleStateEvent>(OnSurveyedState);
        SubscribeLocalEvent<WFDeepVeinComponent, ComponentStartup>(OnVeinStartup);
    }

    /// <summary>A reveal arrived; every vein already on this client has to catch up in one pass.</summary>
    private void OnSurveyedState(Entity<WFSurveyedComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (_player.LocalEntity != ent.Owner)
            return;

        RefreshAll();
    }

    /// <summary>A vein entered PVS, possibly long after the pulse that revealed it.</summary>
    private void OnVeinStartup(Entity<WFDeepVeinComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(ent.Owner, out var sprite))
            return;

        Refresh(ent.Owner, ent.Comp, sprite, Revealed(), CurrentSkin());
    }

    /// <summary>Re-applies visibility, state and tint to every deep vein this client knows about.</summary>
    public void RefreshAll()
    {
        var revealed = Revealed();
        var skin = CurrentSkin();

        var query = EntityQueryEnumerator<WFDeepVeinComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var vein, out var sprite))
        {
            Refresh(uid, vein, sprite, revealed, skin);
        }
    }

    /// <summary>One vein's Marker layer: visible only when this player pulsed it, state by richness, colour by ore.</summary>
    private void Refresh(EntityUid uid, WFDeepVeinComponent vein, SpriteComponent sprite, HashSet<NetEntity>? revealed, WolfgateSkin skin)
    {
        if (!_sprite.LayerMapTryGet((uid, sprite), WFDeepVeinVisualLayers.Marker, out var index, false))
            return;

        var visible = revealed != null && revealed.Contains(GetNetEntity(uid));

        _sprite.LayerSetVisible((uid, sprite), index, visible);

        if (!visible)
            return;

        // deep_vein.rsi ships exactly two states; name no others.
        _sprite.LayerSetRsiState((uid, sprite), index, vein.Rich ? "vein-rich" : "vein");
        _sprite.LayerSetColor((uid, sprite), index, Tint(vein.Ore.Id, skin));
    }

    /// <summary>The local player's revealed set, or null when there is no local player or it has never pulsed.</summary>
    private HashSet<NetEntity>? Revealed()
    {
        if (_player.LocalEntity is not { } local || !TryComp<WFSurveyedComponent>(local, out var surveyed))
            return null;

        return surveyed.Revealed;
    }

    /// <summary>The skin the client is drawing with right now.</summary>
    private WolfgateSkin CurrentSkin()
    {
        return WolfgateSkins.Get(_cfg.GetCVar(WolfgateCVars.UiStyle));
    }

    /// <summary>Skin colour for an ore id; never a bare colour literal.</summary>
    private static Color Tint(string ore, WolfgateSkin skin)
    {
        return OreTints.TryGetValue(ore, out var pick) ? pick(skin) : skin.Accent;
    }
}
