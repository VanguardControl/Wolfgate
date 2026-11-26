using Content.Shared._WG.StockExchange;
using NUnit.Framework;

namespace Content.Tests._WG.StockExchange;

/// <summary>Covers exchange limits, sheet conversion, silo space and faction precedence.</summary>
[TestFixture]
public sealed class WGStockPolicyTest
{
    [Test]
    public void SheetsAndUnitsConvert()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WGStockPolicy.UnitsPerSheet, Is.EqualTo(100));
            Assert.That(WGStockPolicy.Units(3), Is.EqualTo(300));
            Assert.That(WGStockPolicy.Sheets(350), Is.EqualTo(3));
        });
    }

    [Test]
    public void PersistentSpaceCostsAMillionPerSheet()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WGStockPolicy.PersistentSlotCost, Is.EqualTo(1_000_000));
            Assert.That(WGStockPolicy.SlotCost(1), Is.EqualTo(1_000_000));
            Assert.That(WGStockPolicy.SlotCost(3), Is.EqualTo(3_000_000));
        });
    }

    [Test]
    public void PersistentSpaceRespectsCapacity()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WGStockPolicy.Fits(0, 0, 1), Is.False, "A company opens with no persistent space.");
            Assert.That(WGStockPolicy.Fits(0, 1, 1), Is.True);
            Assert.That(WGStockPolicy.Fits(1, 1, 1), Is.False);
            Assert.That(WGStockPolicy.Fits(0, WGStockPolicy.MaximumPersistentSheets + 1, 1), Is.False);
        });
    }

    [Test]
    public void BoughtSheetsFillThePersistentSiloFirst()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WGStockPolicy.DeliverySplit(5, 3, 0), Is.EqualTo((3, 2)));
            Assert.That(WGStockPolicy.DeliverySplit(2, 3, 2), Is.EqualTo((1, 1)));
            Assert.That(WGStockPolicy.DeliverySplit(2, 3, 3), Is.EqualTo((0, 2)));
            Assert.That(WGStockPolicy.DeliverySplit(2, 0, 0), Is.EqualTo((0, 2)), "Factions and new companies have no persistent space.");
        });
    }

    [Test]
    public void OrdersHaveSaneLimits()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WGStockPolicy.ValidPrice(1), Is.True);
            Assert.That(WGStockPolicy.ValidPrice(0), Is.False);
            Assert.That(WGStockPolicy.ValidPrice(WGStockPolicy.MaximumPrice + 1), Is.False);
            Assert.That(WGStockPolicy.ValidQuantity(1), Is.True);
            Assert.That(WGStockPolicy.ValidQuantity(0), Is.False);
            Assert.That(WGStockPolicy.ValidQuantity(WGStockPolicy.MaximumQuantity + 1), Is.False);
        });
    }

    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("None", false)]
    [TestCase("TSF", true)]
    [TestCase("PDV", true)]
    public void FactionTakesPrecedenceOverCompany(string? faction, bool expected)
    {
        Assert.That(WGStockPolicy.FactionTakesPrecedence(faction), Is.EqualTo(expected));
    }
}
