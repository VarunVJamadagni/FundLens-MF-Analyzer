using System.Text.Json.Serialization;

namespace InvestmentPlanner.Api.Models
{
    /// <summary>
    /// Canonical analytics shape for a mutual fund scheme.
    ///
    /// IMPORTANT: This exact same model/JSON shape is used for:
    ///   - fund cards in /api/Scheme/search results
    ///   - fund cards in /api/Scheme/eligible results
    ///   - the single-fund detail returned by /api/Analytics/{schemeCode}
    ///
    /// Reusing one model everywhere is a deliberate choice to avoid the
    /// property-name mismatch problem (e.g. "cagR1Year" vs "cagr1Year")
    /// across controllers/services/pages. JsonPropertyName attributes pin
    /// the exact wire format regardless of any global naming policy.
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

        // ---- Metadata used to power the filters on the home page. ----
        // FundHouse and SchemeCategory come directly from MFAPI's "meta"
        // block (real data). Plan and Option are inferred from the scheme
        // name because MFAPI does not expose them as separate fields.
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
