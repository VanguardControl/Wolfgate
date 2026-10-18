// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
namespace Content.Shared._WF.Xenonids.Parasite;

[Virtual]
public class SharedXenoParasiteSystem : EntitySystem
{
    public bool Infect(Entity<XenoParasiteComponent> parasite, EntityUid victim, bool popup = true, bool force = false)
    {
        return false;
    }

    public void RefreshIncubationMultipliers(EntityUid ent)
    {
    }
}
