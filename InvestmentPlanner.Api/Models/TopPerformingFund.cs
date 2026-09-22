namespace InvestmentPlanner.Web.Models;

public class TopPerformingFund
{
    public int SchemeCode { get; set; }

    public string SchemeName { get; set; } = string.Empty;

    public string FundHouse { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string SubCategory { get; set; } = string.Empty;

    public string CAGR { get; set; } = string.Empty;
}