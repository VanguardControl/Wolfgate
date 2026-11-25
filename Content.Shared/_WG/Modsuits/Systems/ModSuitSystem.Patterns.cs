using System.Linq;
using Content.Shared._WG.Modsuits.Components;
using Content.Shared._WG.Modsuits.Modules;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Modsuits.Systems;

/// <summary>Why <see cref="ModSuitSystem.TryReplate"/> refused.</summary>
public enum ModReplateRefusal : byte
{
    None,
    UnknownPattern,
    NotInactive,
    Busy,
    PartsOut,
}

public sealed partial class ModSuitSystem
{
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>
    /// Swaps the suit's theme, parts and inbuilt modules for a pattern's. The suit must be off with every part
    /// retracted. Removable modules the new theme forbids or can't fit leave newest first, never storage modules,
    /// into the suit's storage or next to the suit.
    /// </summary>
    public bool TryReplate(Entity<ModSuitComponent> ent,
        ProtoId<ModPatternPrototype> patternId,
        out ModReplateRefusal refusal,
        List<EntityUid>? removed = null)
    {
        refusal = CanReplate(ent, patternId);
        if (refusal != ModReplateRefusal.None)
            return false;

        var pattern = _proto.Index(patternId);

        foreach (var part in ent.Comp.PartEntities.Values.ToArray())
        {
            if (TryComp<ModPartComponent>(part, out var partComp))
                DropStoredClothing((part, partComp));
            PredictedDel(part);
        }
        ent.Comp.PartEntities.Clear();

        foreach (var module in ent.Comp.ModulesContainer.ContainedEntities.ToArray())
        {
            if (!TryComp<ModModuleComponent>(module, out var moduleComp) || moduleComp.Removable)
                continue;

            DetachModule(ent, (module, moduleComp));
            PredictedDel(module);
        }

        ent.Comp.Theme = pattern.Theme;
        ent.Comp.Parts = new Dictionary<string, EntProtoId>(pattern.Parts);
        ent.Comp.InbuiltModules = new List<EntProtoId>(pattern.InbuiltModules);
        ent.Comp.Pattern = patternId;

        SpawnParts(ent);
        SpawnInbuiltModules(ent);
        ShedModules(ent, removed);

        RefreshDrain(ent);
        Dirty(ent);
        RaiseUpdated(ent);
        return true;
    }

    /// <summary>Checks <see cref="TryReplate"/>'s refusals without changing anything.</summary>
    public ModReplateRefusal CanReplate(Entity<ModSuitComponent> ent, ProtoId<ModPatternPrototype> patternId)
    {
        if (!_proto.HasIndex(patternId))
            return ModReplateRefusal.UnknownPattern;

        if (ent.Comp.State != ModSuitState.Inactive)
            return ModReplateRefusal.NotInactive;

        if (ent.Comp.DeployQueue.Count > 0 || ent.Comp.ActivationQueue.Count > 0)
            return ModReplateRefusal.Busy;

        foreach (var part in ent.Comp.PartEntities.Values)
        {
            if (!ent.Comp.PartsContainer.Contains(part))
                return ModReplateRefusal.PartsOut;
        }

        return ModReplateRefusal.None;
    }

    /// <summary>Removes removable modules the current theme forbids, then the newest ones until complexity fits.</summary>
    private void ShedModules(Entity<ModSuitComponent> ent, List<EntityUid>? removed)
    {
        var theme = _proto.Index(ent.Comp.Theme);
        var removable = new List<Entity<ModModuleComponent>>();
        foreach (var module in ent.Comp.ModulesContainer.ContainedEntities)
        {
            if (TryComp<ModModuleComponent>(module, out var comp)
                && comp.Removable
                && !HasComp<ModStorageModuleComponent>(module))
                removable.Add((module, comp));
        }

        var shed = new List<Entity<ModModuleComponent>>();
        foreach (var module in removable)
        {
            if (_whitelist.IsWhitelistFail(theme.AllowedModules, module) || _whitelist.IsWhitelistPass(theme.BlacklistedModules, module))
                shed.Add(module);
        }

        var complexity = GetComplexity(ent) - shed.Sum(m => m.Comp.Complexity);
        for (var i = removable.Count - 1; i >= 0 && complexity > theme.ComplexityMax; i--)
        {
            if (shed.Contains(removable[i]))
                continue;

            shed.Add(removable[i]);
            complexity -= removable[i].Comp.Complexity;
        }

        foreach (var module in shed)
        {
            DetachModule(ent, module);
            if (!TryComp<StorageComponent>(ent, out var storage)
                || !_storage.Insert(ent, module, out _, storageComp: storage, playSound: false))
                _transform.DropNextTo(module.Owner, ent.Comp.Wearer ?? ent.Owner);

            removed?.Add(module);
        }
    }

    /// <summary>Takes a module out of the suit the way removal does, without a user or popups.</summary>
    private void DetachModule(Entity<ModSuitComponent> ent, Entity<ModModuleComponent> module)
    {
        if (module.Comp.Active)
            SetModuleActive(ent, module, false);

        if (ent.Comp.Wearer is { } wearer)
            RemoveModuleAction(module, wearer);

        _container.Remove(module.Owner, ent.Comp.ModulesContainer);
        module.Comp.Suit = null;
        Dirty(module);

        var ev = new ModModuleRemovedEvent(ent);
        RaiseLocalEvent(module, ref ev);
    }
}
