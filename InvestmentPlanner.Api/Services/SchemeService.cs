using System.Net.Http.Json;
using InvestmentPlanner.Api.Models;

namespace InvestmentPlanner.Api.Services
{
    /// <summary>
    /// Responsible for:
    ///   - searching schemes (via MFAPI's real search endpoint)
    ///   - obtaining candidate schemes for the "initial eligible funds" list
    ///   - determining eligibility when required
    ///
    /// IMPORTANT: SearchAsync always calls MFAPI's live search endpoint for
    /// whatever the user typed - it is never restricted to a fixed list of
    /// fund houses. The seed queries below are used ONLY to discover
    /// candidates for the initial/eligible list, and never gate what a user
    /// can search for.
    /// </summary>
    public class SchemeService
    {
        private readonly HttpClient _httpClient;
        private readonly AnalyticsService _analyticsService;
        private readonly ILogger<SchemeService> _logger;

        // Broad, generic seed terms used only to find candidates for the
        // initial eligible-funds list. These are not scheme codes and they
        // do not restrict user search in any way.
        private static readonly string[] EligibleCandidateSeeds =
        {
            "Direct Growth", "Bluechip", "Nifty 50", "Flexi Cap", "Large Cap", "Multi Cap", "Value Fund"
        };

        private const int DefaultSearchResultLimit = 25;
        private const int DefaultEligibleCount = 8;
        private const int MaxEligibleCandidatesToCheck = 40;

        public SchemeService(HttpClient httpClient, AnalyticsService analyticsService, ILogger<SchemeService> logger)
        {
            _httpClient = httpClient;
            _analyticsService = analyticsService;
            _logger = logger;
        }

        /// <summary>
        /// Calls MFAPI's real search endpoint for the given query and returns
        /// the raw scheme code / scheme name pairs MFAPI returns.
        /// </summary>
        public async Task<List<Scheme>> SearchSchemesRawAsync(string query)
        {
            try
            {
                var response = await _httpClient.GetAsync($"mf/search?q={Uri.EscapeDataString(query)}");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("MFAPI search returned {StatusCode} for query '{Query}'", response.StatusCode, query);
                    return new List<Scheme>();
                }

                var schemes = await response.Content.ReadFromJsonAsync<List<Scheme>>();
                return schemes ?? new List<Scheme>();
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "MFAPI search timed out for query '{Query}'", query);
                return new List<Scheme>();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "MFAPI search failed for query '{Query}'", query);
                return new List<Scheme>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error searching MFAPI for query '{Query}'", query);
                return new List<Scheme>();
            }
        }

        /// <summary>
        /// User-facing search: whatever the user typed or pasted is sent
        /// directly to MFAPI's search endpoint. Results are enriched with
        /// analytics. Funds lacking long-term history are still returned
        /// (with "N/A" for the periods they lack) - they are never hidden.
        /// </summary>
        public async Task<List<AnalyticsApiResponse>> SearchAsync(string query, int maxResults = DefaultSearchResultLimit)
        {
            var rawResults = await SearchSchemesRawAsync(query);

            var limited = rawResults.Take(maxResults).ToList();

            var analyticsTasks = limited
                .Select(s => _analyticsService.BuildAnalyticsAsync(s.SchemeCode, s.SchemeName))
                .ToList();

            var results = await Task.WhenAll(analyticsTasks);

            return results.Where(r => r != null).Select(r => r!).ToList();
        }

        /// <summary>
        /// Discovers a small set of funds that have ALL required analytics
        /// periods available (1M/3M/6M/1Y/3Y/5Y/10Y), without hardcoding any
        /// scheme codes and without fetching MFAPI's entire fund list.
        /// Stops as soon as enough eligible funds are found.
        /// </summary>
        public async Task<List<AnalyticsApiResponse>> GetEligibleFundsAsync(
            int desiredCount = DefaultEligibleCount,
            int maxCandidatesToCheck = MaxEligibleCandidatesToCheck)
        {
            var eligible = new List<AnalyticsApiResponse>();
            var checkedSchemeCodes = new HashSet<int>();

            foreach (var seed in EligibleCandidateSeeds)
            {
                if (eligible.Count >= desiredCount || checkedSchemeCodes.Count >= maxCandidatesToCheck)
                {
                    break;
                }

                var candidates = await SearchSchemesRawAsync(seed);

                foreach (var candidate in candidates)
                {
                    if (eligible.Count >= desiredCount || checkedSchemeCodes.Count >= maxCandidatesToCheck)
                    {
                        break;
                    }

                    if (!checkedSchemeCodes.Add(candidate.SchemeCode))
                    {
                        continue; // already checked this scheme via an earlier seed
                    }

                    AnalyticsApiResponse? analytics;
                    try
                    {
                        analytics = await _analyticsService.BuildAnalyticsAsync(candidate.SchemeCode, candidate.SchemeName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error evaluating eligibility for scheme {SchemeCode}", candidate.SchemeCode);
                        continue;
                    }

                    if (analytics != null && AnalyticsService.HasAllPeriods(analytics))
                    {
                        eligible.Add(analytics);
                    }
                }
            }

            return eligible;
        }
    }
}
