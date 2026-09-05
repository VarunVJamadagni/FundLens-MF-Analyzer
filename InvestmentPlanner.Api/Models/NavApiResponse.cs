using System.Text.Json.Serialization;

namespace InvestmentPlanner.Api.Models
{
    /// <summary>
    /// Raw deserialization model matching the exact JSON shape returned by
    /// GET https://api.mfapi.in/mf/{schemeCode}
    ///
    /// This is intentionally a 1:1 mirror of the external API's JSON so that
    /// NAVService can safely parse it. Application code should NOT consume
    /// this type directly outside of NAVService - use MutualFundResponse instead.
    /// </summary>
    public class NavApiResponse
    {
        [JsonPropertyName("meta")]
        public NavApiMeta? Meta { get; set; }

        [JsonPropertyName("data")]
        public List<NavApiDataPoint>? Data { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    public class NavApiMeta
    {
        [JsonPropertyName("fund_house")]
        public string? FundHouse { get; set; }

        [JsonPropertyName("scheme_type")]
        public string? SchemeType { get; set; }

        [JsonPropertyName("scheme_category")]
        public string? SchemeCategory { get; set; }

        [JsonPropertyName("scheme_code")]
        public int SchemeCode { get; set; }

        [JsonPropertyName("scheme_name")]
        public string? SchemeName { get; set; }
    }

    public class NavApiDataPoint
    {
        // MFAPI returns these as strings, e.g. "date": "04-08-2026", "nav": "125.4567"
        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("nav")]
        public string? Nav { get; set; }
    }
}
