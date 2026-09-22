using System.Net.Http.Json;
using InvestmentPlanner.Api.Models;

namespace InvestmentPlanner.Api.Services
{
    /// <summary>
    /// Responsible for:
    ///   - searching schemes
    ///   - searching the local AMFI-generated funds.json catalogue
    ///   - obtaining candidate schemes for the initial eligible-funds list
    ///   - determining eligibility when required
    ///
    /// User searches use the local AMFI catalogue first.
    /// Searches can match:
    ///   - Fund House
    ///   - Scheme Name
    ///   - Category
    ///   - Sub-Category
    ///
    /// MFAPI remains as a fallback when no local match is found.
    /// </summary>
    public class SchemeService
    {
        private readonly HttpClient _httpClient;
        private readonly AnalyticsService _analyticsService;
        private readonly ILogger<SchemeService> _logger;
        private readonly AmfiFundDataService _amfiFundDataService;

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
        /// Calls MFAPI's live search endpoint.
        /// Used as a fallback when the local AMFI catalogue
        /// does not contain a match.
        /// </summary>
        public async Task<List<Scheme>> SearchSchemesRawAsync(string query)
        {
            try
            {
                var search = query?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(search))
                    return new List<Scheme>();

                var response = await _httpClient.GetAsync(
                    $"mf/search?q={Uri.EscapeDataString(search)}");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "MFAPI search returned {StatusCode} for query '{Query}'",
                        response.StatusCode,
                        search);

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
        /// User-facing fund search.
        ///
        /// The local AMFI catalogue is searched first using:
        ///   - Fund House
        ///   - Scheme Name
        ///   - Category
        ///   - Sub-Category
        ///
        /// All local matches are returned.
        ///
        /// If no local match exists, MFAPI is used as a fallback
        /// with the existing result limit.
        /// </summary>
        public async Task<List<AnalyticsApiResponse>> SearchAsync(
            string query,
            int maxResults = DefaultSearchResultLimit)
        {
            var search = query?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(search))
                return new List<AnalyticsApiResponse>();

            List<Scheme> schemes;

            try
            {
                // -----------------------------------------------------
                // FIRST: SEARCH LOCAL AMFI CATALOGUE
                // -----------------------------------------------------

                var localFunds =
                    await _amfiFundDataService.SearchFundsAsync(search);

                if (localFunds.Count > 0)
                {
                    // IMPORTANT:
                    // Do not limit local search results.
                    //
                    // This allows:
                    // ICICI
                    // HDFC
                    // Flexi
                    // Large
                    // Mid
                    // Small
                    // etc.
                    //
                    // to return all matching schemes.
                    schemes = localFunds
                        .Select(f =>
                        {
                            if (!int.TryParse(
                                    f.SchemeCode,
                                    out var schemeCode))
                            {
                                return null;
                            }

                            return new Scheme
                            {
                                SchemeCode = schemeCode,
                                SchemeName = f.SchemeName
                            };
                        })
                        .Where(s => s != null)
                        .Select(s => s!)
                        .ToList();
                }
                else
                {
                    // -------------------------------------------------
                    // FALLBACK: MFAPI
                    // -------------------------------------------------

                    var rawResults =
                        await SearchSchemesRawAsync(search);

                    schemes = rawResults
                        .Take(maxResults)
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error searching local AMFI catalogue for query '{Query}'",
                    search);

                // If the local catalogue cannot be read,
                // preserve the existing MFAPI fallback.
                var rawResults =
                    await SearchSchemesRawAsync(search);

                schemes = rawResults
                    .Take(maxResults)
                    .ToList();
            }

            if (schemes.Count == 0)
                return new List<AnalyticsApiResponse>();

            // Remove duplicate scheme codes before requesting analytics.
            schemes = schemes
                .GroupBy(s => s.SchemeCode)
                .Select(g => g.First())
                .ToList();

            // ---------------------------------------------------------
            // BUILD ANALYTICS FOR THE MATCHING SCHEMES
            // ---------------------------------------------------------

            var analyticsTasks = schemes
                .Select(s =>
                    _analyticsService.BuildAnalyticsAsync(
                        s.SchemeCode,
                        s.SchemeName))
                .ToList();

            try
            {
                var results =
                    await Task.WhenAll(analyticsTasks);

                return results
                    .Where(r => r != null)
                    .Select(r => r!)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error building analytics for search query '{Query}'",
                    search);

                return new List<AnalyticsApiResponse>();
            }
        }

        /// <summary>
        /// Discovers funds that have all required analytics periods.
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

        /// <summary>
/// Returns all available filter values from the local AMFI catalogue.
/// This does not call MFAPI.
/// </summary>
public async Task<FilterOptions> GetFilterOptionsAsync()
{
    var funds = await _amfiFundDataService.LoadFundsAsync();

    return new FilterOptions
    {
        FundHouses = funds
            .Where(f => !string.IsNullOrWhiteSpace(f.FundHouse))
            .Select(f => f.FundHouse.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList(),

        Categories = funds
            .Where(f => !string.IsNullOrWhiteSpace(f.Category))
            .Select(f => f.Category.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList(),

        SubCategories = funds
            .Where(f => !string.IsNullOrWhiteSpace(f.SubCategory))
            .Select(f => f.SubCategory.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList(),

        Plans = new List<string>
        {
            "Direct",
            "Regular"
        },

        Options = new List<string>
        {
            "Growth",
            "IDCW",
            "Other"
        }
    };
}

/// <summary>
/// Filters the local AMFI catalogue.
/// No analytics or MFAPI calls are made here.
/// </summary>
public async Task<List<AnalyticsApiResponse>> FilterFundsAsync(
    FundFilterRequest request)
{
    var funds =
        await _amfiFundDataService.LoadFundsAsync();

    if (request.FundHouses.Count > 0)
    {
        var selected = new HashSet<string>(
            request.FundHouses,
            StringComparer.OrdinalIgnoreCase);

        funds = funds
            .Where(f => selected.Contains(f.FundHouse))
            .ToList();
    }

    if (request.Categories.Count > 0)
    {
        var selected = new HashSet<string>(
            request.Categories,
            StringComparer.OrdinalIgnoreCase);

        funds = funds
            .Where(f => selected.Contains(f.Category))
            .ToList();
    }

    if (request.SubCategories.Count > 0)
    {
        var selected = new HashSet<string>(
            request.SubCategories,
            StringComparer.OrdinalIgnoreCase);

        funds = funds
            .Where(f => selected.Contains(f.SubCategory))
            .ToList();
    }

    if (request.Plans.Count > 0)
    {
        var selected = new HashSet<string>(
            request.Plans,
            StringComparer.OrdinalIgnoreCase);

        funds = funds
            .Where(f => selected.Contains(f.Plan))
            .ToList();
    }

    if (request.Options.Count > 0)
{
    var selected = new HashSet<string>(
        request.Options,
        StringComparer.OrdinalIgnoreCase);

    funds = funds
        .Where(f =>
            selected.Contains(
                NormalizeFilterOption(f.Option)))
        .ToList();
}

    // Convert the filtered AMFI records into the same
    // analytics response used by normal search.
    var schemes = funds
        .Select(f =>
        {
            if (!int.TryParse(
                    f.SchemeCode,
                    out var schemeCode))
            {
                return null;
            }

            return new Scheme
            {
                SchemeCode = schemeCode,
                SchemeName = f.SchemeName
            };
        })
        .Where(s => s != null)
        .Select(s => s!)
        .GroupBy(s => s.SchemeCode)
        .Select(g => g.First())
        .ToList();

    if (schemes.Count == 0)
    {
        return new List<AnalyticsApiResponse>();
    }

    var analyticsTasks = schemes
        .Select(s =>
            _analyticsService.BuildAnalyticsAsync(
                s.SchemeCode,
                s.SchemeName))
        .ToList();

    try
    {
        var results =
            await Task.WhenAll(analyticsTasks);

        return results
            .Where(r => r != null)
            .Select(r => r!)
            .ToList();
    }
    catch (Exception ex)
    {
        _logger.LogError(
            ex,
            "Error building analytics for filtered funds");

        return new List<AnalyticsApiResponse>();
    }
}

private static string NormalizeFilterOption(string? option)
{
    if (string.IsNullOrWhiteSpace(option))
        return "Other";

    var value = option.Trim();

    if (value.Equals(
        "Growth",
        StringComparison.OrdinalIgnoreCase))
    {
        return "Growth";
    }

    if (
        value.Equals(
            "IDCW",
            StringComparison.OrdinalIgnoreCase) ||

        value.Contains(
            "IDCW",
            StringComparison.OrdinalIgnoreCase) ||

        value.Contains(
            "Dividend",
            StringComparison.OrdinalIgnoreCase) ||

        value.Contains(
            "Income Distribution",
            StringComparison.OrdinalIgnoreCase)
    )
    {
        return "IDCW";
    }

    return "Other";
}
    }
}