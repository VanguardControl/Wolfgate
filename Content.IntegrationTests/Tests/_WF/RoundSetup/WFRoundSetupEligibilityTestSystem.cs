using System.Collections.Generic;
using Content.Server.GameTicking.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._WF.RoundSetup;

/// <summary>Counts a recipient's eligibility checks and can veto a job between snapshots.</summary>
public sealed class WFRoundSetupEligibilityTestSystem : EntitySystem
{
    public NetUserId? User;
    public readonly Dictionary<string, int> Calls = new();
    public bool DenyContractor;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<IsJobAllowedEvent>(OnAllowed);
    }

    private void OnAllowed(ref IsJobAllowedEvent args)
    {
        if (args.Player.UserId != User)
            return;
        Calls.TryGetValue(args.JobId.Id, out var count);
        Calls[args.JobId.Id] = count + 1;
        if (DenyContractor && args.JobId.Id == "Contractor")
            args.Cancelled = true;
    }
}
