using System.Net.Http.Json;
using InvestmentPlanner.Api.Models;

namespace InvestmentPlanner.Api.Services
{
    /// <summary>
    /// Responsible for:
    ///   - searching schemes
    ///   - searching fund houses through the local AMFI-generated funds.json
    ///   - obtaining candidate schemes for the "initial eligible funds" list
    ///   - determining eligibility when required
    ///
    /// IMPORTANT:
    ///   - If the query matches a fund house in funds.json, all active schemes
    ///     belonging to that fund house are returned.
    ///   - Otherwise, SearchAsync falls back to MFAPI's live search endpoint.
    ///   - The seed queries below are used ONLY to discover candidates for the
    ///     initial/eligible list.
    /// </summary>
    public class SchemeService
    {
        private readonly HttpClient _httpClient;
        private readonly AnalyticsService _analyticsService;
        private readonly ILogger<SchemeService> _logger;
        private readonly AmfiFundDataService _amfiFundDataService;

        // Broad, generic seed terms used only to find candidates for the
        // initial eligible-funds list. These are not scheme codes and they
        // do not restrict user search in any way.
        private static readonly string[] EligibleCandidateSeeds =
        {
            "Direct Growth",
            "Bluechip",
            "Nifty 50",
            "Flexi Cap",
            "Large Cap",
            "Multi Cap",
            "Value Fund"
        };

        private const int DefaultSearchResultLimit = 25;
        private const int DefaultEligibleCount = 8;
        private const int MaxEligibleCandidatesToCheck = 40;

        public SchemeService(
            HttpClient httpClient,
            AnalyticsService analyticsService,
            ILogger<SchemeService> logger,
            AmfiFundDataService amfiFundDataService)
        {
            _httpClient = httpClient;
            _analyticsService = analyticsService;
            _logger = logger;
            _amfiFundDataService = amfiFundDataService;
        }

        /// <summary>
        /// Calls MFAPI's live search endpoint for the given query and returns
        /// the raw scheme code / scheme name pairs MFAPI returns.
        /// </summary>
        public async Task<List<Scheme>> SearchSchemesRawAsync(string query)
        {
            try
            {
                var response = await _httpClient.GetAsync(
                    $"mf/search?q={Uri.EscapeDataString(query)}");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "MFAPI search returned {StatusCode} for query '{Query}'",
                        response.StatusCode,
                        query);

                    return new List<Scheme>();
                }

                var schemes =
                    await response.Content.ReadFromJsonAsync<List<Scheme>>();

                return schemes ?? new List<Scheme>();
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(
                    ex,
                    "MFAPI search timed out for query '{Query}'",
                    query);

                return new List<Scheme>();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(
                    ex,
                    "MFAPI search failed for query '{Query}'",
                    query);

                return new List<Scheme>();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected error searching MFAPI for query '{Query}'",
                    query);

                return new List<Scheme>();
            }
        }

        /// <summary>
        /// Searches the locally generated funds.json for a matching fund house.
        ///
        /// Returns ALL active schemes associated with the fund house.
        /// No 25-result limit is applied here.
        /// </summary>
        private async Task<List<Scheme>> SearchFundHouseFromJsonAsync(string query)
        {
            try
            {
                var funds =
                    await _amfiFundDataService.GetFundsByFundHouseAsync(query);

                return funds
                    .Select(f => new Scheme
                    {
                        SchemeCode = int.Parse(f.SchemeCode),
                        SchemeName = f.SchemeName
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error searching funds.json for fund house '{FundHouse}'",
                    query);

                return new List<Scheme>();
            }
        }

        /// <summary>
        /// User-facing search.
        ///
        /// First checks funds.json for a matching fund house.
        /// If a fund house is found:
        ///   - all active schemes from funds.json are used
        ///   - no 25-result limit is applied
        ///
        /// If no fund house is found:
        ///   - the existing MFAPI live search is used
        ///   - the existing 25-result limit is applied
        ///
        /// All results are then enriched using the existing analytics service.
        /// </summary>
        public async Task<List<AnalyticsApiResponse>> SearchAsync(
            string query,
            int maxResults = DefaultSearchResultLimit)
        {
            // First check the local AMFI fund catalogue.
            var fundHouseResults =
                await SearchFundHouseFromJsonAsync(query);

            List<Scheme> schemes;

            if (fundHouseResults.Count > 0)
            {
                // A fund house was found in funds.json.
                //
                // IMPORTANT:
                // Do not apply the normal maxResults limit here.
                // We want ALL active schemes belonging to this fund house.
                schemes = fundHouseResults;
            }
            else
            {
                // Not a fund-house search.
                // Preserve the existing MFAPI search behaviour.
                var rawResults =
                    await SearchSchemesRawAsync(query);

                schemes = rawResults
                    .Take(maxResults)
                    .ToList();
            }

            var analyticsTasks = schemes
                .Select(s =>
                    _analyticsService.BuildAnalyticsAsync(
                        s.SchemeCode,
                        s.SchemeName))
                .ToList();

            var results = await Task.WhenAll(analyticsTasks);

            return results
                .Where(r => r != null)
                .Select(r => r!)
                .ToList();
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
                if (eligible.Count >= desiredCount ||
                    checkedSchemeCodes.Count >= maxCandidatesToCheck)
                {
                    break;
                }

                var candidates =
                    await SearchSchemesRawAsync(seed);

                foreach (var candidate in candidates)
                {
                    if (eligible.Count >= desiredCount ||
                        checkedSchemeCodes.Count >= maxCandidatesToCheck)
                    {
                        break;
                    }

                    if (!checkedSchemeCodes.Add(candidate.SchemeCode))
                    {
                        continue;
                    }

                    AnalyticsApiResponse? analytics;

                    try
                    {
                        analytics =
                            await _analyticsService.BuildAnalyticsAsync(
                                candidate.SchemeCode,
                                candidate.SchemeName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Error evaluating eligibility for scheme {SchemeCode}",
                            candidate.SchemeCode);

                        continue;
                    }

                    if (analytics != null &&
                        AnalyticsService.HasAllPeriods(analytics))
                    {
                        eligible.Add(analytics);
                    }
                }
            }

            return eligible;
        }
    }
}