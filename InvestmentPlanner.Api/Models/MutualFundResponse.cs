namespace InvestmentPlanner.Api.Models
{
    /// <summary>
    /// A cleaned-up, strongly typed representation of a mutual fund's details
    /// and NAV history, produced by NAVService after parsing the raw
    /// NavApiResponse from MFAPI. Malformed NAV entries are excluded here.
    /// NavHistory is sorted descending by date (most recent first).
    /// </summary>
    public class MutualFundResponse
    {
        public int SchemeCode { get; set; }
        public string SchemeName { get; set; } = string.Empty;
        public string? FundHouse { get; set; }
        public string? SchemeType { get; set; }
        public string? SchemeCategory { get; set; }
        public List<NavDataPoint> NavHistory { get; set; } = new();
    }

    public class NavDataPoint
    {
        public DateTime Date { get; set; }
        public decimal Nav { get; set; }
    }
}
