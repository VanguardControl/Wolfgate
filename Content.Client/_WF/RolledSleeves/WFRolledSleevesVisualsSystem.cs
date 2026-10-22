using System.Text.RegularExpressions;
using Content.Shared._WF.RolledSleeves;
using Content.Shared.Clothing;
using Content.Shared.Item;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client._WF.RolledSleeves;

/// <summary>
/// Cuts the sleeves of a rolled jumpsuit at draw time: its worn layers get the rolled-sleeve shader, fed a mask cut
/// from the human arm silhouette. Works on any jumpsuit sprite without a drawn rolled state.
/// </summary>
public sealed partial class WFRolledSleevesVisualsSystem : EntitySystem
{
    [Dependency] private SharedItemSystem _item = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private const string Shader = "WFRolledSleeves";
    private const string DisplacedShader = "WFRolledSleevesDisplaced";
    private const string MaskSuffix = "-wf-rolled-sleeves";
    private const string DisplacementSuffix = "-displacement";
    private const int MaskSize = 32;

    private static readonly ResPath MaskRsi = new("/Textures/_WF/RolledSleeves/sleeve_mask.rsi");

    /// <summary>Human jumpsuit states, optionally prefixed; species-specific states are left alone.</summary>
    private static readonly Regex JumpsuitState = new(@"^(?:[^-]+-)?equipped-INNERCLOTHING$", RegexOptions.Compiled);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFRolledSleevesComponent, EquipmentVisualsUpdatedEvent>(OnVisualsUpdated);
        SubscribeLocalEvent<WFRolledSleevesComponent, AfterAutoHandleStateEvent>(OnAfterState);
    }

    private void OnAfterState(Entity<WFRolledSleevesComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _item.VisualsChanged(ent);
    }

    private void OnVisualsUpdated(Entity<WFRolledSleevesComponent> ent, ref EquipmentVisualsUpdatedEvent args)
    {
        if (!ent.Comp.Rolled || !TryComp(args.Equipee, out SpriteComponent? sprite))
            return;

        // Copied: mask keys are added to the set while walking it.
        foreach (var key in new List<string>(args.RevealedLayers))
        {
            if (key.EndsWith(DisplacementSuffix) || key.EndsWith(MaskSuffix))
                continue;

            if (!_sprite.LayerMapTryGet((args.Equipee, sprite), key, out var index, false) ||
                sprite[index] is not SpriteComponent.Layer layer ||
                layer.State.Name is not { } state ||
                !JumpsuitState.IsMatch(state) ||
                sprite.LayerGetActualRSI(index) is not { } rsi ||
                rsi.Size.X != MaskSize || rsi.Size.Y != MaskSize)
            {
                continue;
            }

            var displaced = args.RevealedLayers.Contains(key + DisplacementSuffix);
            sprite.LayerSetShader(index, displaced ? DisplacedShader : Shader);

            var mask = new PrototypeLayerData
            {
                RsiPath = MaskRsi.ToString(),
                State = "mask",
                CopyToShaderParameters = new PrototypeCopyToShaderParameters
                {
                    LayerKey = key,
                    ParameterTexture = "rollMask",
                    ParameterUV = "rollMaskUV",
                },
            };

            // Shader-parameter layers have to sit below the layer they feed, so they're set before it draws.
            var maskKey = key + MaskSuffix;
            _sprite.AddLayer((args.Equipee, sprite), mask, index);
            _sprite.LayerMapSet((args.Equipee, sprite), maskKey, index);
            args.RevealedLayers.Add(maskKey);
        }
    }
}
