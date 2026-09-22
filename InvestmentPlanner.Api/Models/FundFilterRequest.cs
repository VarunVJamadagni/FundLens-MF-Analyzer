namespace InvestmentPlanner.Api.Models;

public class FundFilterRequest
{
    public List<string> FundHouses { get; set; } = new();
    public List<string> Categories { get; set; } = new();
    public List<string> SubCategories { get; set; } = new();
    public List<string> Plans { get; set; } = new();
    public List<string> Options { get; set; } = new();
}