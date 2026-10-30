#nullable enable
using System.Threading.Tasks;
using Content.Server._WF.Shipyard;

namespace Content.IntegrationTests.Tests._WF.Traders;

/// <summary>A used ship stays off the lot until its copy is written, and a copy that fails to write is dropped.</summary>
[TestFixture]
[TestOf(typeof(UsedShipMarketSystem))]
public sealed class UsedShipListingReadyTest
{
    [Test]
    public void ReadyFollowsTheWrite()
    {
        var writing = new TaskCompletionSource<string?>();
        var pending = new UsedShipListing { Writing = writing.Task };
        Assert.That(pending.Ready, Is.False, "A listing still being written is not ready.");
        Assert.That(pending.WriteFailed, Is.False);

        writing.SetResult("yaml");
        Assert.That(pending.Ready, Is.True);
        Assert.That(pending.Data, Is.EqualTo("yaml"));

        var failed = new UsedShipListing { Writing = Task.FromResult<string?>(null) };
        Assert.That(failed.Ready, Is.False);
        Assert.That(failed.WriteFailed, Is.True);

        Assert.That(new UsedShipListing().Ready, Is.True, "A listing with nothing being written is ready.");
    }
}
