// XENOPORT-STUB: replaced by the Xenonids module port; keeps RMC's file name and namespace.
namespace Content.Shared._WF.Xenonids.Evolution;

[ByRefEvent]
public readonly record struct NewXenoEvolvedEvent(Entity<XenoEvolutionComponent> OldXeno, EntityUid NewXeno, bool SubtractPoints);
