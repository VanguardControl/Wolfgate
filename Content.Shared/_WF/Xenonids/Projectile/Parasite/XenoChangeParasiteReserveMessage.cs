// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenonids.Projectile.Parasite;

[Serializable, NetSerializable]
public sealed class XenoChangeParasiteReserveMessage : BoundUserInterfaceMessage
{
    public int NewReserve;

    public XenoChangeParasiteReserveMessage(int newReserve)
    {
        NewReserve = newReserve;
    }
}


[Serializable, NetSerializable]
public enum XenoReserveParasiteChangeUI : byte
{
    Key
}
