namespace InvestmentPlanner.Api.Models;

public class FundAnalytics
{
    // Fund identity / metadata
    public int SchemeCode { get; set; }

    public string FundHouse { get; set; } = string.Empty;

    public string SchemeName { get; set; } = string.Empty;

    public string Plan { get; set; } = string.Empty;

    public string Option { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string SubCategory { get; set; } = string.Empty;

    // NAV date is a calendar date, not a timestamp.
    public DateOnly LastNavDate { get; set; }

    // Current NAV and calculated performance
    public decimal CurrentNAV { get; set; }

    public decimal? Return1Month { get; set; }

    public decimal? Return3Month { get; set; }

    public decimal? Return6Month { get; set; }

    public decimal? CAGR1Year { get; set; }

    public decimal? CAGR3Year { get; set; }

    public decimal? CAGR5Year { get; set; }

    public decimal? CAGR10Year { get; set; }

    // Tracking dates
    // Actual timestamp, stored as UTC.
    public DateTime AnalyticsDate { get; set; }

    // NAV data date is a calendar date, not a timestamp.
    public DateOnly DataAsOfDate { get; set; }
}