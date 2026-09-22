namespace InvestmentPlanner.Web.Models;

public class TopPerformingFundsResponse
{
    public List<TopPerformingFund> ThreeYear { get; set; } = new();

    public List<TopPerformingFund> FiveYear { get; set; } = new();

    public List<TopPerformingFund> TenYear { get; set; } = new();
}