namespace InvestmentPlanner.Api.Models;

public class AmfiFund
{
    public string SchemeCode { get; set; } = string.Empty;
    public string FundHouse { get; set; } = string.Empty;
    public string SchemeName { get; set; } = string.Empty;

    public string Plan { get; set; } = string.Empty;
    public string Option { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;
    public string SubCategory { get; set; } = string.Empty;

    public string LastNavDate { get; set; } = string.Empty;
}