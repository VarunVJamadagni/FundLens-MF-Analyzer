using System.Text.Json.Serialization;

namespace InvestmentPlanner.Web.Models
{
    /// <summary>
    /// Mirrors InvestmentPlanner.Api.Models.AnalyticsApiResponse exactly.
    /// The Web project is a separate deployable from the Api project, so it
    /// keeps its own copy of this DTO - the JsonPropertyName attributes below
    /// MUST stay in sync with the Api project's model so the Analytics page
    /// deserializes the API's response correctly.
    /// </summary>
    public class AnalyticsApiResponse
    {
        [JsonPropertyName("schemeCode")]
        public int SchemeCode { get; set; }

        [JsonPropertyName("schemeName")]
        public string SchemeName { get; set; } = string.Empty;

        [JsonPropertyName("currentNAV")]
        public decimal CurrentNAV { get; set; }

        [JsonPropertyName("return1Month")]
        public string Return1Month { get; set; } = "N/A";

        [JsonPropertyName("return3Month")]
        public string Return3Month { get; set; } = "N/A";

        [JsonPropertyName("return6Month")]
        public string Return6Month { get; set; } = "N/A";

        [JsonPropertyName("cagr1Year")]
        public string CAGR1Year { get; set; } = "N/A";

        [JsonPropertyName("cagr3Year")]
        public string CAGR3Year { get; set; } = "N/A";

        [JsonPropertyName("cagr5Year")]
        public string CAGR5Year { get; set; } = "N/A";

        [JsonPropertyName("cagr10Year")]
        public string CAGR10Year { get; set; } = "N/A";

        [JsonPropertyName("fundHouse")]
        public string? FundHouse { get; set; }

        [JsonPropertyName("schemeCategory")]
        public string? SchemeCategory { get; set; }

        [JsonPropertyName("schemeSubCategory")]
        public string? SchemeSubCategory { get; set; }

        [JsonPropertyName("plan")]
        public string Plan { get; set; } = "Standard";

        [JsonPropertyName("option")]
        public string Option { get; set; } = "Other";
    }
}
