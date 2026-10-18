// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
namespace Content.Shared._WF.Xenonids.Parasite;

[ByRefEvent]
public record struct GetInfectedIncubationMultiplierEvent(int stage)
{
    public List<float> Additions = new();
    public List<float> Multipliers = new();

    public void Add(float multiplier)
    {
        Additions.Add(multiplier);
    }

    public void Multiply(float multiplier)
    {
        Multipliers.Add(multiplier);
    }
}
