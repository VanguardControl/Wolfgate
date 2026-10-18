// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
namespace Content.Shared._WF.Xenonids.Hive;

[Virtual]
public class SharedXenoHiveSystem : EntitySystem
{
    public void SetSameHive(Entity<HiveMemberComponent?> src, Entity<HiveMemberComponent?> dest)
    {
    }

    public bool FromSameHive(Entity<HiveMemberComponent?> a, Entity<HiveMemberComponent?> b)
    {
        return false;
    }
}
