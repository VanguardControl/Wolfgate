using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Server._WG.Companies;
using Content.Server._WG.StockExchange;
using Content.Shared._WG.Companies;
using Content.Shared._WG.StockExchange;
using Content.Shared.Preferences;
using NUnit.Framework;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._WG.StockExchange;

/// <summary>The company's persistent silo: space is bought from its bank and survives a reload.</summary>
[TestFixture]
[TestOf(typeof(WGStockSystem))]
public sealed class WGStockTest
{
    [Test]
    public async Task CompanySiloCostsFromTheBankAndPersists()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var companies = server.System<WGCompanySystem>();
        var owner = pair.Player!;
        int? id = null;
        try
        {
            await WaitFor(() => companies.Loaded && owner.Status == SessionStatus.InGame);
            await Run(() => companies.HandleRequest(new WGCompanyRequest(WGCompanyAction.Create)
            {
                Name = "Silo Holdings",
                Tag = "SILO",
                Description = "Keeps what it buys.",
            }, owner));
            await Run(() => companies.Flush());
            await server.WaitAssertion(() => id = companies.FindCompany("SILO"));
            Assert.That(id, Is.Not.Null);

            await server.WaitPost(() => companies.AdminAdjust(id!.Value, 2_000_000, "Test", "silo funds"));
            await Run(() => companies.Flush());
            Assert.That(companies.GetBalance(id!.Value), Is.EqualTo(2_000_000));

            string? error = null;
            await server.WaitPost(() => companies.StockBuySiloSlots(owner, 1, out error));
            Assert.That(error, Is.Null, error);
            await server.WaitAssertion(() =>
            {
                Assert.That(companies.GetBalance(id!.Value), Is.EqualTo(1_000_000), "One sheet of silo space costs 1,000,000.");
                Assert.That(companies.StockCompany(owner)?.SiloSlots, Is.EqualTo(1));
            });

            await server.WaitPost(() => companies.StockSaveSilo(id!.Value, 1, new Dictionary<string, int> { { "Steel", 1 } }, 0, "Test"));
            await Run(() => companies.Flush());
            await Run(() => companies.Reload());
            await server.WaitAssertion(() =>
            {
                var saved = companies.StockCompany(owner);
                Assert.That(saved, Is.Not.Null);
                Assert.Multiple(() =>
                {
                    Assert.That(saved!.SiloSlots, Is.EqualTo(1));
                    Assert.That(saved.SiloMaterials.GetValueOrDefault("Steel"), Is.EqualTo(1));
                    Assert.That((saved.Permissions & WGCompanyPermissions.SiloWithdraw) != 0, Is.True,
                        "The owner may withdraw from the persistent silo.");
                });
            });
        }
        finally
        {
            if (id is { } left)
                await server.WaitPost(() => companies.AdminDisband(left));
            await Run(() => companies.Flush());
        }
        await pair.CleanReturnAsync();

        async Task Run(Func<Task> start)
        {
            Task task = null;
            await server.WaitPost(() => task = start());
            for (var i = 0; i < 600 && !task.IsCompleted; i++)
            {
                await pair.RunTicksSync(1);
                await Task.Delay(5);
            }
            Assert.That(task.IsCompleted, Is.True, "Server task timed out.");
            await task;
        }

        async Task WaitFor(Func<bool> condition)
        {
            var done = false;
            for (var i = 0; i < 600 && !done; i++)
            {
                await server.WaitPost(() => done = condition());
                if (!done)
                {
                    await pair.RunTicksSync(1);
                    await Task.Delay(10);
                }
            }
            Assert.That(done, Is.True);
        }
    }
}
