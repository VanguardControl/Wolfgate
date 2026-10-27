using Content.Shared.Interaction;
using Content.Shared.Jittering;
using Content.Shared.Power;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Xenobiology;

/// <summary>
/// Starts slime processors from a click or verb and switches them between collecting and processing.
/// </summary>
public sealed partial class SlimeProcessorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedJitteringSystem _jittering = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SlimeProcessorComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<SlimeProcessorComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<SlimeProcessorComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<SlimeProcessorComponent, GetVerbsEvent<InteractionVerb>>(OnGetVerbs);
    }

    private void OnInit(Entity<SlimeProcessorComponent> ent, ref ComponentInit args)
    {
        _container.EnsureContainer<Container>(ent, SlimeProcessorComponent.SlimeContainerName);
    }

    private void OnActivate(Entity<SlimeProcessorComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !CanActivate(ent))
            return;

        args.Handled = true;
        StartProcessing(ent);
    }

    private void OnGetVerbs(Entity<SlimeProcessorComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var canActivate = CanActivate(ent);
        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("comp-slime-processor-verb-activate"),
            Message = Loc.GetString(canActivate
                ? "comp-slime-processor-verb-activate-message-success"
                : "comp-slime-processor-verb-activate-message-no-slimes"),
            Disabled = !canActivate,
            Act = () => StartProcessing(ent),
        });
    }

    private void OnPowerChanged(Entity<SlimeProcessorComponent> ent, ref PowerChangedEvent args)
    {
        if (args.Powered)
        {
            StartCollecting(ent);
            return;
        }

        RemCompDeferred<ActiveSlimeProcessorComponent>(ent);
        RemCompDeferred<CollectingSlimeProcessorComponent>(ent);
        RemCompDeferred<JitteringComponent>(ent);
    }

    private bool CanActivate(Entity<SlimeProcessorComponent> ent)
    {
        return HasComp<CollectingSlimeProcessorComponent>(ent)
            && _container.TryGetContainer(ent, SlimeProcessorComponent.SlimeContainerName, out var slimes)
            && slimes.ContainedEntities.Count > 0;
    }

    /// <summary>
    /// Switches a processor to processing its stored slimes.
    /// </summary>
    public void StartProcessing(Entity<SlimeProcessorComponent> ent)
    {
        if (_net.IsClient || HasComp<ActiveSlimeProcessorComponent>(ent))
            return;

        RemComp<CollectingSlimeProcessorComponent>(ent);
        var active = AddComp<ActiveSlimeProcessorComponent>(ent);
        active.ProcessingFinishedMoment = _timing.CurTime + ent.Comp.ProcessingTime;
        Dirty(ent, active);

        _jittering.AddJitter(ent, -10, 100);
        _audio.PlayPvs(ent.Comp.ProcessSound, ent);
    }

    /// <summary>
    /// Switches a processor back to pulling in slimes.
    /// </summary>
    public void StartCollecting(Entity<SlimeProcessorComponent> ent)
    {
        if (_net.IsClient || HasComp<CollectingSlimeProcessorComponent>(ent))
            return;

        RemComp<ActiveSlimeProcessorComponent>(ent);
        RemComp<JitteringComponent>(ent);
        var collecting = AddComp<CollectingSlimeProcessorComponent>(ent);
        collecting.SlimeAcquireMoment = _timing.CurTime + ent.Comp.SlimeAcquireCooldown;
        Dirty(ent, collecting);
    }
}
