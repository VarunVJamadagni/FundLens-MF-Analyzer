using System.Text.Json.Serialization;

namespace InvestmentPlanner.Api.Models
{
    /// <summary>
    /// Represents a single entry returned by MFAPI's search endpoint:
    /// GET https://api.mfapi.in/mf/search?q={query}
    /// </summary>
    public class Scheme
    {
        [JsonPropertyName("schemeCode")]
        public int SchemeCode { get; set; }

        [JsonPropertyName("schemeName")]
        public string SchemeName { get; set; } = string.Empty;
    }
}
