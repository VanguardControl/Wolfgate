using System.Linq;
using Content.Shared._WG.Augments;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;

namespace Content.Shared._Goobstation.Autosurgeon;

public sealed partial class AutoSurgeonSystem
{
    [Dependency] private SharedPopupSystem _wgPopup = default!;

    /// <summary>Whether the body takes no augments; tells the user so from the server.</summary>
    public bool WgRefusesBody(EntityUid body)
    {
        if (!TryComp(body, out WGNoAugmentsComponent? none))
            return false;

        if (_netManager.IsServer)
            _wgPopup.PopupEntity(Loc.GetString(none.AutosurgeonRefuse, ("target", Identity.Entity(body, EntityManager))), body, body);
        return true;
    }

    /// <summary>The part or organ an upgrading autosurgeon works on in a body, if it has one.</summary>
    public EntityUid? WgUpgradeTarget(EntityUid body, AutoSurgeonComponent surgeon)
    {
        var target = surgeon.TargetOrgan == null
            ? _body.GetBodyChildrenOfType(body, surgeon.TargetBodyPart, symmetry: surgeon.TargetBodyPartSymmetry)
                .FirstOrDefault().Id
            : _body.GetBodyOrgans(body).FirstOrDefault(organ => organ.Component.SlotId == surgeon.TargetOrgan).Id;
        return target.Valid ? target : null;
    }

    /// <summary>Gives a body the upgrade an autosurgeon installs, as if it had been used on it.</summary>
    public bool WgApplyUpgrade(EntityUid body, AutoSurgeonComponent surgeon)
    {
        if (surgeon.NewPartProto != null || WgUpgradeTarget(body, surgeon) is not { } target)
            return false;

        var added = AddComponents(target, surgeon.ComponentsToPart);
        if (added != null && added.Count == 0)
            return false;

        // A copy: the handlers keep the registry on the part, and this one may belong to a prototype.
        var toUser = surgeon.ComponentsToUser == null ? null : new ComponentRegistry(surgeon.ComponentsToUser);
        if (surgeon.TargetOrgan == null)
            HandleBodyPart(body, target, toUser);
        else
            HandleOrgan(body, target, toUser);
        return true;
    }

    /// <summary>Whether a body carries the upgrade an autosurgeon installs.</summary>
    public bool WgHasUpgrade(EntityUid body, AutoSurgeonComponent surgeon)
    {
        if (surgeon.NewPartProto != null || WgUpgradeTarget(body, surgeon) is not { } target)
            return false;

        if (surgeon.ComponentsToPart != null
            && surgeon.ComponentsToPart.Keys.Any(name => !HasComp(target, _componentFactory.GetRegistration(name).Type)))
            return false;

        if (surgeon.ComponentsToUser is not { Count: > 0 } toUser)
            return surgeon.ComponentsToPart is { Count: > 0 };

        var onAdd = surgeon.TargetOrgan == null
            ? CompOrNull<BodyPartComponent>(target)?.OnAdd
            : CompOrNull<OrganComponent>(target)?.OnAdd;
        return onAdd != null && toUser.Keys.All(onAdd.ContainsKey);
    }
}
