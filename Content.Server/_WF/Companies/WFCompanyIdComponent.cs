namespace Content.Server._WF.Companies;

/// <summary>An ID card issued to a company member, naming their company and rank on examine.</summary>
[RegisterComponent]
public sealed partial class WFCompanyIdComponent : Component
{
    [DataField]
    public int CompanyId;

    [DataField]
    public string Company = string.Empty;

    [DataField]
    public string Tag = string.Empty;

    [DataField]
    public string Rank = string.Empty;

    /// <summary>The rank title put on the card at spawn, empty when it kept its job's title.</summary>
    [DataField]
    public string Title = string.Empty;
}
