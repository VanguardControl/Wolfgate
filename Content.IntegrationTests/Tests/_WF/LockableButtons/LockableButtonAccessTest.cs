using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.DeviceLinking.Components;
using Content.Shared.Lock;

namespace Content.IntegrationTests.Tests._WF.LockableButtons;

/// <summary>
/// A locked button pressed without access must deny and not fire; once unlocked, anyone can press it.
/// </summary>
public sealed class LockableButtonAccessTest : InteractionTest
{
    [Test]
    public async Task LockedButtonWithoutAccessDoesNotFire()
    {
        await SpawnTarget("LockableButtonCaptain");
        var button = STarget!.Value;
        var lockSys = SEntMan.System<LockSystem>();
        var signal = SEntMan.GetComponent<SignalSwitchComponent>(button);

        Assert.That(lockSys.IsLocked(button), Is.True);
        Assert.That(signal.State, Is.False);

        await Activate();
        Assert.Multiple(() =>
        {
            Assert.That(signal.State, Is.False, "A denied press must not fire the button.");
            Assert.That(lockSys.IsLocked(button), Is.True);
        });

        await Server.WaitPost(() => lockSys.Unlock(button, null));
        await RunSeconds(1f);

        await Activate();
        Assert.That(signal.State, Is.True, "An unlocked button must fire for anyone.");
    }
}
