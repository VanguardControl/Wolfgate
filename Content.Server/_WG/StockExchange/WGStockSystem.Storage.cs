using System.Linq;
using Content.Server._WG.Companies;
using Content.Shared._WG.StockExchange;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Robust.Shared.Map;

namespace Content.Server._WG.StockExchange;

public sealed partial class WGStockSystem
{
    /// <summary>Adds sheets of a material to an account's round or persistent stock.</summary>
    private static void AddMaterial(WGStockAccountComponent account, string material, int sheets, bool persistent)
    {
        if (sheets <= 0)
            return;
        var stock = persistent ? account.PersistentMaterials : account.RoundMaterials;
        stock[material] = stock.GetValueOrDefault(material) + sheets;
    }

    /// <summary>Takes sheets from an account, or returns false if it does not hold them.</summary>
    private static bool TakeMaterial(WGStockAccountComponent account, string material, int sheets, bool persistent)
    {
        if (sheets <= 0)
            return false;
        var stock = persistent ? account.PersistentMaterials : account.RoundMaterials;
        var held = stock.GetValueOrDefault(material);
        if (held < sheets)
            return false;
        if (held == sheets)
            stock.Remove(material);
        else
            stock[material] = held - sheets;
        return true;
    }

    /// <summary>How many whole sheets of material an entity holds, as a lathe counts them.</summary>
    private static int SheetsIn(EntityUid uid, EntityManager entities)
    {
        if (!entities.TryGetComponent<PhysicalCompositionComponent>(uid, out var composition))
            return 0;
        var units = composition.MaterialComposition.Values.Sum();
        if (units <= 0)
            return 0;
        var stacks = entities.TryGetComponent<StackComponent>(uid, out var stack) ? stack.Count : 1;
        return (int) ((long) units * stacks / WGStockPolicy.UnitsPerSheet);
    }

    /// <summary>
    /// Delivers bought materials to an account's storage. A company fills its persistent silo first and leaves the
    /// rest in round storage; a faction only has round storage.
    /// </summary>
    private void Deliver(EntityUid accountUid, WGStockGood good, int sheets)
    {
        if (sheets <= 0)
            return;
        var account = Comp<WGStockAccountComponent>(accountUid);
        if (account.Kind != WGStockOwnerKind.Company)
        {
            AddMaterial(account, good.Id, sheets, false);
            return;
        }

        if (_companies.StockCompany(account.CompanyId) is { } company)
        {
            account.PersistentCapacity = company.SiloSlots;
            account.PersistentMaterials.Clear();
            foreach (var (material, amount) in company.SiloMaterials)
                account.PersistentMaterials[material] = amount;
        }

        var (persistent, round) = WGStockPolicy.DeliverySplit(sheets, account.PersistentCapacity,
            account.PersistentMaterials.Values.Sum());
        if (persistent > 0)
        {
            AddMaterial(account, good.Id, persistent, true);
            SaveSilo(OwnerFromAccount(accountUid, account));
        }
        AddMaterial(account, good.Id, round, false);
    }

    /// <summary>Hands a spawned item to the actor, or leaves it at their feet when their hands are full.</summary>
    private void GiveTo(EntityUid actor, EntityUid item)
    {
        if (!_hands.TryPickupAnyHand(actor, item))
            _transform.SetCoordinates(item, Transform(actor).Coordinates.Offset(new System.Numerics.Vector2(0.2f, 0)));
    }

    private void GiveMaterial(EntityUid actor, string materialId, int sheets)
    {
        if (sheets <= 0 || !_protos.TryIndex<MaterialPrototype>(materialId, out var material) ||
            material.StackEntity == null)
            return;
        var spawned = _stacks.SpawnMultiple(material.StackEntity.Value, sheets, Transform(actor).Coordinates);
        foreach (var item in spawned)
            GiveTo(actor, item);
    }

    /// <summary>Puts the held material into an account's round storage.</summary>
    private bool DepositHeld(EntityUid actor, WGStockAccountComponent account, out string? error)
    {
        error = null;
        if (!_hands.TryGetActiveItem(actor, out var held))
        {
            error = "wg-stock-error-empty-hand";
            return false;
        }

        return DepositEntity(account, held.Value, out error);
    }

    /// <summary>Puts one material entity into an account's round storage.</summary>
    private bool DepositEntity(WGStockAccountComponent account, EntityUid entity, out string? error)
    {
        error = null;
        if (!HasComp<MaterialComponent>(entity))
        {
            error = "wg-stock-error-not-material";
            return false;
        }

        var sheets = SheetsIn(entity, EntityManager);
        var materialId = MaterialId(entity);
        if (sheets <= 0 || materialId == null)
        {
            error = "wg-stock-error-not-material";
            return false;
        }

        AddMaterial(account, materialId, sheets, false);
        Del(entity);
        return true;
    }

    /// <summary>The material an entity is mostly made of, for silo deposits.</summary>
    private static string? MaterialId(EntityUid uid, EntityManager entities)
    {
        if (!entities.TryGetComponent<PhysicalCompositionComponent>(uid, out var composition))
            return null;
        return composition.MaterialComposition.OrderByDescending(pair => pair.Value).FirstOrDefault().Key;
    }

    private string? MaterialId(EntityUid uid) => MaterialId(uid, EntityManager);

    /// <summary>Moves sheets from a company's round stock into its persistent silo, within the space it has bought.</summary>
    private bool StorePersistent(OwnerInfo owner, string material, int sheets, out string? error)
    {
        error = null;
        if (owner.Kind != WGStockOwnerKind.Company)
        {
            error = "wg-stock-error-no-persistent";
            return false;
        }
        var used = owner.AccountComp.PersistentMaterials.Values.Sum();
        if (!WGStockPolicy.Fits(used, owner.AccountComp.PersistentCapacity, sheets))
        {
            error = "wg-stock-error-silo-full";
            return false;
        }
        if (!TakeMaterial(owner.AccountComp, material, sheets, false))
        {
            error = "wg-stock-error-stock";
            return false;
        }
        AddMaterial(owner.AccountComp, material, sheets, true);
        SaveSilo(owner);
        return true;
    }

    /// <summary>Takes sheets out of a company's persistent silo; only a rank with the silo permission may.</summary>
    private bool WithdrawPersistent(OwnerInfo owner, EntityUid actor, string material, int sheets, out string? error)
    {
        error = null;
        if (owner.Kind != WGStockOwnerKind.Company)
        {
            error = "wg-stock-error-no-persistent";
            return false;
        }
        if ((owner.Permissions & Content.Shared._WG.Companies.WGCompanyPermissions.SiloWithdraw) == 0)
        {
            error = "wg-stock-error-permission";
            return false;
        }
        if (!_protos.TryIndex<MaterialPrototype>(material, out var prototype) || prototype.StackEntity == null)
        {
            error = "wg-stock-error-not-material";
            return false;
        }
        if (!TakeMaterial(owner.AccountComp, material, sheets, true))
        {
            error = "wg-stock-error-stock";
            return false;
        }
        GiveMaterial(actor, material, sheets);
        SaveSilo(owner);
        return true;
    }

    /// <summary>Writes the company's persistent material stock through to its record and the database.</summary>
    private void SaveSilo(OwnerInfo owner)
    {
        _companies.StockSaveSilo(owner.CompanyId, owner.AccountComp.PersistentCapacity,
            new Dictionary<string, int>(owner.AccountComp.PersistentMaterials), owner.Profile, owner.ActorName);
    }

    /// <summary>Refreshes a company account's cached persistent silo from the company record.</summary>
    private void RefreshPersistent(OwnerInfo owner, WGCompanyStockInfo company)
    {
        owner.AccountComp.PersistentCapacity = company.SiloSlots;
        owner.AccountComp.PersistentMaterials.Clear();
        foreach (var (material, sheets) in company.SiloMaterials)
            owner.AccountComp.PersistentMaterials[material] = sheets;
    }

    /// <summary>Whether the account can pay, before an order takes its money.</summary>
    private bool CanPay(OwnerInfo owner, long amount)
    {
        if (amount <= 0)
            return false;
        return owner.Kind == WGStockOwnerKind.Company
            ? (_companies.GetBalance(owner.CompanyId) ?? 0) >= amount
            : owner.AccountComp.FactionBalance >= amount;
    }

    /// <summary>Takes money from an account, for escrow or a trade.</summary>
    private bool Pay(OwnerInfo owner, long amount, string note)
    {
        if (!CanPay(owner, amount))
            return false;
        if (owner.Kind == WGStockOwnerKind.Company)
            return _companies.StockSpend(owner.CompanyId, amount, owner.Profile, owner.ActorName, note);
        owner.AccountComp.FactionBalance -= amount;
        return true;
    }

    /// <summary>Pays money into an account, such as a sale or a refund.</summary>
    private void Receive(OwnerInfo owner, long amount, string note)
    {
        if (amount <= 0)
            return;
        if (owner.Kind == WGStockOwnerKind.Company)
            _companies.StockCredit(owner.CompanyId, amount, owner.Profile, owner.ActorName, note);
        else
            owner.AccountComp.FactionBalance += amount;
    }

    /// <summary>Adds money to a faction's exchange balance from the acting character's bank.</summary>
    private bool DepositFunds(EntityUid actor, OwnerInfo owner, int amount, out string? error)
    {
        error = null;
        if (amount <= 0)
        {
            error = "wg-stock-error-amount";
            return false;
        }
        if (owner.Kind == WGStockOwnerKind.Company)
        {
            if (!_players.TryGetSessionByEntity(actor, out var session) || !_companies.StockDeposit(session, amount, out error))
                return false;
            return true;
        }

        if (!_players.TryGetSessionByEntity(actor, out var factionSession) ||
            _preferences.GetPreferencesOrNull(factionSession.UserId) is not { } prefs ||
            prefs.SelectedCharacter is not Content.Shared.Preferences.HumanoidCharacterProfile profile ||
            !_bank.TryBankWithdraw(factionSession, prefs, profile, amount, out _))
        {
            error = "wg-stock-error-funds";
            return false;
        }
        owner.AccountComp.FactionBalance += amount;
        return true;
    }

    /// <summary>Takes money from an exchange account back to the acting character's bank.</summary>
    private bool WithdrawFunds(EntityUid actor, OwnerInfo owner, int amount, out string? error)
    {
        error = null;
        if (amount <= 0)
        {
            error = "wg-stock-error-amount";
            return false;
        }
        if (owner.Kind == WGStockOwnerKind.Company)
        {
            if (!_players.TryGetSessionByEntity(actor, out var session) || !_companies.StockWithdraw(session, amount, out error))
                return false;
            return true;
        }

        if (owner.AccountComp.FactionBalance < amount)
        {
            error = "wg-stock-error-balance";
            return false;
        }
        if (!_players.TryGetSessionByEntity(actor, out var factionSession) ||
            _preferences.GetPreferencesOrNull(factionSession.UserId) is not { } prefs ||
            prefs.SelectedCharacter is not Content.Shared.Preferences.HumanoidCharacterProfile profile ||
            !_bank.TryBankDeposit(factionSession, prefs, profile, amount, out _))
        {
            error = "wg-stock-error-failed";
            return false;
        }
        owner.AccountComp.FactionBalance -= amount;
        return true;
    }
}
