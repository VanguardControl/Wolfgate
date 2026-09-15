using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client.Damage;

/// <summary>Per-limb damage sprites for wound hosts (P3-4). Body of HOOK 20's insertions in DamageVisualsSystem.cs.</summary>
public sealed partial class DamageVisualsSystem
{
    /// <summary>Re-runs the damage visuals when a wound host's per-part damage projection changes.</summary>
    private void OnPartDamageVisualsState(Entity<PartDamageVisualsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (TryComp(ent, out DamageVisualsComponent? visuals) && TryComp(ent, out AppearanceComponent? appearance))
            HandleDamage(ent, appearance, visuals);

        // Option B: severed-limb wound rendering, independent of whether this entity has a DamageVisuals block.
        UpdateDetachedPartDamage(ent.Owner, ent.Comp);
    }

    /// <summary>Option B: a detached part's own BodyPartComponent state changed (e.g. it was just severed).</summary>
    private void OnBodyPartState(Entity<BodyPartComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (TryComp(ent, out PartDamageVisualsComponent? damage))
            UpdateDetachedPartDamage(ent.Owner, damage);
    }

    /// <summary>Drives each targeted sprite layer from that limb's own damage instead of the mob's aggregate (D30).</summary>
    private void UpdatePartDamageVisuals(EntityUid uid, SpriteComponent sprite,
        DamageVisualsComponent damageVisComp, PartDamageVisualsComponent partDamage)
    {
        foreach (var layerMapKey in damageVisComp.TargetLayerMapKeys)
        {
            var damage = layerMapKey is HumanoidVisualLayers humanoidLayer
                ? GetLayerDamage(partDamage, humanoidLayer)
                : null;
            var groups = damage?.GetDamagePerGroup(_prototypeManager);

            // The per-group threshold cache is deliberately not consulted here (WG's LastThresholdPerGroup is
            // keyed by group, not by (layer, group)); recompute per layer every time instead, as Onyx does.
            foreach (var group in damageVisComp.DamageOverlayGroups!.Keys)
            {
                var amount = groups?.GetValueOrDefault(group) ?? FixedPoint2.Zero;
                CheckThresholdBoundary(amount, FixedPoint2.Zero, damageVisComp, out var threshold);
                UpdateTargetLayer(sprite, damageVisComp, layerMapKey, group, threshold);
            }
        }
    }

    /// <summary>Damage shown on one layer: the limb itself plus the extremity Wolfgate has no layer for (P3-D25).</summary>
    private static DamageSpecifier? GetLayerDamage(PartDamageVisualsComponent partDamage, HumanoidVisualLayers layer)
    {
        partDamage.Damage.TryGetValue(layer, out var damage);

        // WOLFGATE: P3-D25 - fold LHand->LArm, RHand->RArm, LFoot->LLeg, RFoot->RLeg. WG's stock six-layer
        // overlay has no hand/foot targetLayers and no hand/foot art, so a faithful per-layer port would make
        // hand and foot wounds invisible where today (pre-phase-3) they light every limb layer.
        var extra = layer switch
        {
            HumanoidVisualLayers.LArm => HumanoidVisualLayers.LHand,
            HumanoidVisualLayers.RArm => HumanoidVisualLayers.RHand,
            HumanoidVisualLayers.LLeg => HumanoidVisualLayers.LFoot,
            HumanoidVisualLayers.RLeg => HumanoidVisualLayers.RFoot,
            _ => (HumanoidVisualLayers?) null,
        };

        if (extra is { } extraLayer && partDamage.Damage.TryGetValue(extraLayer, out var extraDamage))
            damage = damage is null ? extraDamage : damage + extraDamage;

        return damage;
    }

    // --- Option B: severed-limb wound rendering (P3-D18) ---

    /// <summary>Adds/updates the detached-part wound layers for one part and hides them once it is reattached.</summary>
    private void UpdateDetachedPartDamage(EntityUid uid, PartDamageVisualsComponent damage)
    {
        if (!TryComp(uid, out BodyPartComponent? part) || !TryComp(uid, out SpriteComponent? sprite))
            return;

        var detached = part.Body == null;
        foreach (var (layer, layerDamage) in damage.Damage)
        {
            if (!TryGetDetachedDamagePrefix(layer, out var prefix))
                continue;

            var groups = layerDamage.GetDamagePerGroup(_prototypeManager);
            UpdateDetachedDamageLayer(uid, sprite, layer, prefix, "Brute", groups.GetValueOrDefault("Brute"), detached);
            UpdateDetachedDamageLayer(uid, sprite, layer, prefix, "Burn", groups.GetValueOrDefault("Burn"), detached);
        }

        if (detached)
            return;

        // Reattached: hide every layer this part could have grown while it was off the body.
        foreach (var layer in Enum.GetValues<HumanoidVisualLayers>())
        {
            SetDetachedDamageLayerVisible(uid, sprite, layer, "Brute", false);
            SetDetachedDamageLayerVisible(uid, sprite, layer, "Burn", false);
        }
    }

    /// <summary>Creates (once) and updates one detached-part damage layer.</summary>
    private void UpdateDetachedDamageLayer(EntityUid uid, SpriteComponent sprite, HumanoidVisualLayers layer,
        string prefix, string group, FixedPoint2 amount, bool detached)
    {
        var threshold = GetDetachedDamageThreshold(amount);
        var key = $"WolfmedDetached{layer}{group}"; // WOLFGATE: layer-map key, no Onyx equivalent to preserve
        if (!SpriteSystem.LayerMapTryGet((uid, sprite), key, out var index, false))
        {
            index = SpriteSystem.AddLayer((uid, sprite), new SpriteSpecifier.Rsi(
                new ResPath($"_Onyx/Wounds/{group.ToLowerInvariant()}_damage.rsi"),
                $"{prefix}_{group}_10"));
            SpriteSystem.LayerMapSet((uid, sprite), key, index);
            if (group == "Brute")
                SpriteSystem.LayerSetColor((uid, sprite), index, Color.FromHex("#FF0000"));
        }

        var visible = detached && threshold > 0;
        SpriteSystem.LayerSetVisible((uid, sprite), index, visible);
        if (visible)
            SpriteSystem.LayerSetRsiState((uid, sprite), index, $"{prefix}_{group}_{threshold}");
    }

    private void SetDetachedDamageLayerVisible(EntityUid uid, SpriteComponent sprite, HumanoidVisualLayers layer, string group, bool visible)
    {
        if (SpriteSystem.LayerMapTryGet((uid, sprite), $"WolfmedDetached{layer}{group}", out var index, false))
            SpriteSystem.LayerSetVisible((uid, sprite), index, visible);
    }

    private static int GetDetachedDamageThreshold(FixedPoint2 amount)
    {
        var value = amount.Float();
        if (value >= 100) return 100;
        if (value >= 70) return 70;
        if (value >= 50) return 50;
        if (value >= 30) return 30;
        if (value >= 20) return 20;
        if (value >= 10) return 10;
        return 0;
    }

    /// <summary>Maps a visual layer to its detached-art state prefix. WOLFGATE: D9 - no HumanoidVisualLayers.Groin in WG; Onyx's Groin arm is dropped.</summary>
    private static bool TryGetDetachedDamagePrefix(HumanoidVisualLayers layer, out string prefix)
    {
        prefix = layer switch
        {
            HumanoidVisualLayers.Chest => "Chest",
            HumanoidVisualLayers.Head => "Head",
            HumanoidVisualLayers.LArm => "LArm",
            HumanoidVisualLayers.RArm => "RArm",
            HumanoidVisualLayers.LHand => "LHand",
            HumanoidVisualLayers.RHand => "RHand",
            HumanoidVisualLayers.LLeg => "LLeg",
            HumanoidVisualLayers.RLeg => "RLeg",
            HumanoidVisualLayers.LFoot => "LFoot",
            HumanoidVisualLayers.RFoot => "RFoot",
            _ => string.Empty,
        };
        return prefix.Length != 0;
    }
}
