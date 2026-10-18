// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenonids.Parasite;

[Virtual]
public class SharedXenoParasiteSystem : EntitySystem
{
    protected readonly ProtoId<TagPrototype> ParasiteIsPreparingLeapProtoID = new("RMCXenoParasitePreparingLeap");

    public bool Infect(Entity<XenoParasiteComponent> parasite, EntityUid victim, bool popup = true, bool force = false)
    {
        return false;
    }
}
