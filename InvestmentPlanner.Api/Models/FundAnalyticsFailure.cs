namespace InvestmentPlanner.Api.Models;

public class FundAnalyticsFailure
{
    public int SchemeCode { get; set; }

    public string FailureReason { get; set; } = string.Empty;

    public bool IsPermanent { get; set; }

    public DateTime LastAttemptedAt { get; set; }
}