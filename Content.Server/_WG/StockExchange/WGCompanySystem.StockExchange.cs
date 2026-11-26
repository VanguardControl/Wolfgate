using Content.Shared._WG.Companies;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server._WG.Companies;

/// <summary>A company's market-facing state: its bank, permissions and persistent material silo.</summary>
public sealed record WGCompanyStockInfo(int Id, string Name, string Tag, long Balance, bool Frozen, WGCompanyPermissions Permissions,
    int SiloSlots, Dictionary<string, int> SiloMaterials, int Profile, string CharacterName);

public sealed partial class WGCompanySystem
{
    /// <summary>A company by id, for exchange settlements that happen while no member is acting.</summary>
    public WGCompanyStockInfo? StockCompany(int companyId)
    {
        if (!_companies.TryGetValue(companyId, out var company))
            return null;
        return new WGCompanyStockInfo(company.Id, company.Name, company.Tag, company.Balance, company.Frozen,
            WGCompanyPermissions.All, company.SiloSlots, new Dictionary<string, int>(company.SiloMaterials), 0, string.Empty);
    }

    /// <summary>The acting character's company, if they are in one and the exchange may act for it.</summary>
    public WGCompanyStockInfo? StockCompany(ICommonSession session)
    {
        if (CurrentCharacter(session) is not { } character || CompanyOf(character.ProfileId) is not { } company)
            return null;
        var member = company.Members[character.ProfileId];
        return new WGCompanyStockInfo(company.Id, company.Name, company.Tag, company.Balance, company.Frozen,
            Permissions(company, member), company.SiloSlots, new Dictionary<string, int>(company.SiloMaterials),
            character.ProfileId, character.Profile.Name);
    }

    /// <summary>The acting character's profile id and name, for ledger rows and exchange orders.</summary>
    public (int ProfileId, string Name)? CharacterFor(ICommonSession session) =>
        CurrentCharacter(session) is { } character ? (character.ProfileId, character.Profile.Name) : null;

    /// <summary>Sends an exchange notice to every online member of a company.</summary>
    public void StockNotify(int companyId, string message)
    {
        if (!_companies.TryGetValue(companyId, out var company))
            return;
        foreach (var member in company.Members.Values)
        {
            if (_players.TryGetSessionById(member.User, out var session) && session.Status == SessionStatus.InGame)
                _chat.DispatchServerMessage(session, message);
        }
    }

    /// <summary>Whether the acting character's company rank holds a permission.</summary>
    public bool StockHasPermission(ICommonSession session, WGCompanyPermissions permission)
    {
        if (CurrentCharacter(session) is not { } character || CompanyOf(character.ProfileId) is not { } company)
            return false;
        return Has(company, company.Members[character.ProfileId], permission);
    }

    /// <summary>The acting character adds money from their bank to the company account.</summary>
    public bool StockDeposit(ICommonSession session, int amount, out string? error)
    {
        error = null;
        if (CurrentCharacter(session) is not { } character || CompanyOf(character.ProfileId) is not { } company)
        {
            error = "wg-stock-error-no-company";
            return false;
        }
        error = Deposit(company, company.Members[character.ProfileId], character, amount, out _);
        if (error == null)
            Touch(company);
        return error == null;
    }

    /// <summary>The acting character takes money from the company account to their bank, within their cap.</summary>
    public bool StockWithdraw(ICommonSession session, int amount, out string? error)
    {
        error = null;
        if (CurrentCharacter(session) is not { } character || CompanyOf(character.ProfileId) is not { } company)
        {
            error = "wg-stock-error-no-company";
            return false;
        }
        error = Withdraw(company, company.Members[character.ProfileId], character, amount, out _);
        if (error == null)
            Touch(company);
        return error == null;
    }

    /// <summary>Takes money from a company account for an exchange purchase; false if it cannot cover it.</summary>
    public bool StockSpend(int companyId, long amount, int? actor, string actorName, string note)
    {
        return amount > 0 && _companies.TryGetValue(companyId, out var company) &&
               Transact(company, WGCompanyLedgerKind.MarketTrade, -amount, actor, actorName, note);
    }

    /// <summary>Pays exchange proceeds into a company account.</summary>
    public void StockCredit(int companyId, long amount, int? actor, string actorName, string note)
    {
        if (amount <= 0 || !_companies.TryGetValue(companyId, out var company))
            return;
        Transact(company, WGCompanyLedgerKind.MarketTrade, amount, actor, actorName, note);
    }

    /// <summary>Spends company money for persistent silo space, in sheets.</summary>
    public bool StockBuySiloSlots(ICommonSession session, int sheets, out string? error)
    {
        error = null;
        if (sheets <= 0 || sheets > Content.Shared._WG.StockExchange.WGStockPolicy.MaximumSlotsAtOnce)
        {
            error = "wg-stock-error-amount";
            return false;
        }
        if (CurrentCharacter(session) is not { } character || CompanyOf(character.ProfileId) is not { } company)
        {
            error = "wg-stock-error-no-company";
            return false;
        }
        if (!Has(company, company.Members[character.ProfileId], WGCompanyPermissions.EditDetails) &&
            company.Owner != character.ProfileId)
        {
            error = "wg-stock-error-permission";
            return false;
        }
        var next = company.SiloSlots + sheets;
        if (next > Content.Shared._WG.StockExchange.WGStockPolicy.MaximumPersistentSheets)
        {
            error = "wg-stock-error-silo-full";
            return false;
        }
        var cost = Content.Shared._WG.StockExchange.WGStockPolicy.SlotCost(sheets);
        if (company.Balance < cost)
        {
            error = "wg-stock-error-balance";
            return false;
        }
        if (!Transact(company, WGCompanyLedgerKind.SiloSlot, -cost, character.ProfileId, character.Profile.Name,
                $"{sheets} sheets"))
        {
            error = "wg-stock-error-failed";
            return false;
        }
        company.SiloSlots = next;
        SaveStockSilo(company);
        Touch(company);
        return true;
    }

    /// <summary>Writes a company's persistent silo to memory and the database.</summary>
    public void StockSaveSilo(int companyId, int slots, Dictionary<string, int> materials, int? actor = null, string actorName = "")
    {
        if (!_companies.TryGetValue(companyId, out var company))
            return;
        company.SiloSlots = Math.Clamp(slots, 0, Content.Shared._WG.StockExchange.WGStockPolicy.MaximumPersistentSheets);
        company.SiloMaterials.Clear();
        foreach (var (material, amount) in materials)
        {
            if (amount > 0)
                company.SiloMaterials[material] = amount;
        }
        SaveStockSilo(company);
        Touch(company);
    }

    private void SaveStockSilo(Company company) =>
        Track(_db.SetCompanySiloAsync(company.Id, company.SiloSlots, WriteSilo(company.SiloMaterials)));

    /// <summary>Serialises persistent silo stock for its database column.</summary>
    public static string WriteSilo(Dictionary<string, int> materials)
    {
        var parts = new List<string>();
        foreach (var (material, sheets) in materials)
        {
            if (sheets > 0)
                parts.Add($"{material}:{sheets}");
        }
        return string.Join(';', parts);
    }

    /// <summary>Reads persistent silo stock from its database column.</summary>
    public static Dictionary<string, int> ParseSilo(string? stored)
    {
        var result = new Dictionary<string, int>();
        if (string.IsNullOrWhiteSpace(stored))
            return result;
        foreach (var part in stored.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var split = part.LastIndexOf(':');
            if (split <= 0 || !int.TryParse(part[(split + 1)..], out var sheets) || sheets <= 0)
                continue;
            result[part[..split]] = sheets;
        }
        return result;
    }
}
