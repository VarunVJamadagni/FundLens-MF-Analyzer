using System.Collections.Concurrent;
using System.Net.Http.Json;
using InvestmentPlanner.Api.Models;
using InvestmentPlanner.Web.Models;

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

        // =============================================================
        // ANALYTICS PERFORMANCE CONFIGURATION
        // =============================================================

        // Maximum number of analytics/MFAPI operations that can run
        // anywhere inside SchemeService at the same time.
        //
        // This is intentionally global so Search, Filter and
        // Top Performing cannot each create their own group of
        // concurrent MFAPI requests.
        private const int MaxConcurrentAnalytics = 6;

        private static readonly SemaphoreSlim AnalyticsLock =
            new(MaxConcurrentAnalytics);

        // Stores analytics by scheme code.
        //
        // Search, Filter, Eligible Funds and Top Performing can all
        // reuse analytics that have already been calculated.
        private static readonly ConcurrentDictionary<
            int,
            Lazy<Task<AnalyticsApiResponse?>>>
            AnalyticsCache = new();

        // =============================================================
        // TOP PERFORMING CONFIGURATION
        // =============================================================

        private static readonly string[] TopPerformingCategories =
        {
            "Debt",
            "Equity",
            "Gold & Silver",
            "Hybrid",
            "Index",
            "Solution Oriented"
        };

        // Prevents multiple Top Performing calculations from
        // happening simultaneously.
        private static readonly SemaphoreSlim TopPerformingLock =
            new(1, 1);

        // Stores the completed Top Performing result.
        // Once populated, the API returns it immediately.
        private static TopPerformingFundsResponse? _topPerformingCache;

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

        // =============================================================
        // RAW MFAPI SEARCH
        // =============================================================

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
                {
                    return new List<Scheme>();
                }

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

        // =============================================================
        // USER SEARCH
        // =============================================================

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
            {
                return new List<AnalyticsApiResponse>();
            }

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
                    // Do not limit local search results.
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

                // Preserve MFAPI fallback if local catalogue fails.

                var rawResults =
                    await SearchSchemesRawAsync(search);

                schemes = rawResults
                    .Take(maxResults)
                    .ToList();
            }

            if (schemes.Count == 0)
            {
                return new List<AnalyticsApiResponse>();
            }

            // Remove duplicate scheme codes.
            schemes = schemes
                .GroupBy(s => s.SchemeCode)
                .Select(g => g.First())
                .ToList();

            // ---------------------------------------------------------
            // BUILD ANALYTICS
            // ---------------------------------------------------------
            //
            // Analytics requests are globally limited to 6 concurrent
            // operations and cached by scheme code.
            //

            var analyticsTasks =
                schemes.Select(async scheme =>
                {
                    try
                    {
                        return await GetAnalyticsCachedAsync(
                            scheme.SchemeCode,
                            scheme.SchemeName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Unable to calculate analytics for scheme {SchemeCode}",
                            scheme.SchemeCode);

                        return null;
                    }
                });

            var results =
                await Task.WhenAll(analyticsTasks);

            return results
                .Where(r => r != null)
                .Select(r => r!)
                .ToList();
        }

        // =============================================================
        // ELIGIBLE FUNDS
        // =============================================================

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
                            await GetAnalyticsCachedAsync(
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

        // =============================================================
        // FILTER OPTIONS
        // =============================================================

        /// <summary>
        /// Returns all available filter values from the local AMFI catalogue.
        /// This does not call MFAPI.
        /// </summary>
        public async Task<FilterOptions> GetFilterOptionsAsync()
        {
            var funds =
                await _amfiFundDataService.LoadFundsAsync();

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

        // =============================================================
        // TOP PERFORMING FUNDS
        // =============================================================

        /// <summary>
        /// Returns the cached Top Performing Funds result.
        ///
        /// If the calculation has already completed,
        /// this returns immediately.
        ///
        /// If it has not completed yet, this calculates it normally.
        /// </summary>
        public async Task<TopPerformingFundsResponse>
            GetTopPerformingFundsAsync()
        {
            if (_topPerformingCache != null)
            {
                return _topPerformingCache;
            }

            return await CalculateTopPerformingFundsAsync();
        }

        /// <summary>
        /// Forces the Top Performing calculation.
        ///
        /// This method remains available if a future startup/background
        /// mechanism is added, but the current Program.cs does not
        /// call it automatically.
        /// </summary>
        public async Task RefreshTopPerformingFundsAsync()
        {
            await CalculateTopPerformingFundsAsync();
        }

        /// <summary>
        /// Performs the actual Top Performing calculation.
        ///
        /// Only Direct + Growth schemes belonging to the six
        /// configured categories are evaluated.
        /// </summary>
        private async Task<TopPerformingFundsResponse>
            CalculateTopPerformingFundsAsync()
        {
            if (_topPerformingCache != null)
            {
                return _topPerformingCache;
            }

            await TopPerformingLock.WaitAsync();

            try
            {
                // Another request may have finished while this request
                // was waiting for the lock.
                if (_topPerformingCache != null)
                {
                    return _topPerformingCache;
                }

                _logger.LogInformation(
                    "Starting Top Performing Funds calculation.");

                var funds =
                    await _amfiFundDataService.LoadFundsAsync();

                // -----------------------------------------------------
                // GET DIRECT + GROWTH SCHEMES
                // -----------------------------------------------------

                var schemes =
                    funds
                        .Where(f =>
                            !string.IsNullOrWhiteSpace(f.SchemeCode) &&
                            !string.IsNullOrWhiteSpace(f.SchemeName) &&

                            string.Equals(
                                f.Plan?.Trim(),
                                "Direct",
                                StringComparison.OrdinalIgnoreCase) &&

                            string.Equals(
                                NormalizeFilterOption(f.Option),
                                "Growth",
                                StringComparison.OrdinalIgnoreCase))
                        .Select(f =>
                        {
                            if (!int.TryParse(
                                    f.SchemeCode,
                                    out var schemeCode))
                            {
                                return null;
                            }

                            var topCategory =
                                GetTopPerformingCategory(
                                    f.Category,
                                    f.SubCategory,
                                    f.SchemeName);

                            if (topCategory == null)
                            {
                                return null;
                            }

                            return new TopPerformingFundCandidate
                            {
                                SchemeCode = schemeCode,
                                SchemeName = f.SchemeName,
                                FundHouse = f.FundHouse,
                                Category = f.Category,
                                SubCategory = f.SubCategory,
                                TopCategory = topCategory
                            };
                        })
                        .Where(f => f != null)
                        .Select(f => f!)
                        .GroupBy(f => f.SchemeCode)
                        .Select(g => g.First())
                        .ToList();

                _logger.LogInformation(
                    "Top Performing Funds: evaluating {Count} Direct Growth schemes.",
                    schemes.Count);

                // -----------------------------------------------------
                // BUILD ANALYTICS
                // -----------------------------------------------------
                //
                // GetAnalyticsCachedAsync uses the global analytics
                // semaphore, so Top Performing shares the same
                // six-request limit with Search and Filter.
                //

                var analyticsTasks =
                    schemes.Select(async fund =>
                    {
                        try
                        {
                            var analytics =
                                await GetAnalyticsCachedAsync(
                                    fund.SchemeCode,
                                    fund.SchemeName);

                            return new TopPerformingAnalyticsCandidate
                            {
                                Fund = fund,
                                Analytics = analytics
                            };
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(
                                ex,
                                "Unable to calculate analytics for scheme {SchemeCode}",
                                fund.SchemeCode);

                            return new TopPerformingAnalyticsCandidate
                            {
                                Fund = fund,
                                Analytics = null
                            };
                        }
                    });

                var results =
                    await Task.WhenAll(analyticsTasks);

                var validResults =
                    results
                        .Where(x => x.Analytics != null)
                        .ToList();

                _logger.LogInformation(
                    "Top Performing Funds: successfully calculated analytics for {Count} schemes.",
                    validResults.Count);

                // -----------------------------------------------------
                // GET TOP FUND FOR EACH PERIOD
                // -----------------------------------------------------

                var response =
                    new TopPerformingFundsResponse
                    {
                        ThreeYear =
                            GetTopFundsForPeriod(
                                validResults,
                                "3Y"),

                        FiveYear =
                            GetTopFundsForPeriod(
                                validResults,
                                "5Y"),

                        TenYear =
                            GetTopFundsForPeriod(
                                validResults,
                                "10Y")
                    };

                // -----------------------------------------------------
                // CACHE FINAL RESULT
                // -----------------------------------------------------

                _topPerformingCache = response;

                _logger.LogInformation(
                    "Top Performing Funds calculation completed. " +
                    "3Y: {ThreeYearCount}, 5Y: {FiveYearCount}, 10Y: {TenYearCount}",
                    response.ThreeYear.Count,
                    response.FiveYear.Count,
                    response.TenYear.Count);

                return response;
            }
            finally
            {
                TopPerformingLock.Release();
            }
        }

        // =============================================================
        // ANALYTICS CACHE
        // =============================================================

        /// <summary>
        /// Gets analytics for a scheme while ensuring:
        ///
        /// 1. The same scheme is not requested repeatedly.
        /// 2. No more than six analytics operations run globally
        ///    at the same time.
        ///
        /// This protects MFAPI when the Web application triggers
        /// Search, Filter and Top Performing operations.
        /// </summary>
        private async Task<AnalyticsApiResponse?>
            GetAnalyticsCachedAsync(
                int schemeCode,
                string? schemeName = null)
        {
            var lazyTask =
                AnalyticsCache.GetOrAdd(
                    schemeCode,
                    _ =>
                        new Lazy<Task<AnalyticsApiResponse?>>(
                            () =>
                                BuildAnalyticsWithLimitAsync(
                                    schemeCode,
                                    schemeName),
                            LazyThreadSafetyMode.ExecutionAndPublication));

            try
            {
                return await lazyTask.Value;
            }
            catch
            {
                // Remove only the failed cached task.
                // This allows a later request to retry the scheme.
                AnalyticsCache.TryRemove(
                    schemeCode,
                    out _);

                throw;
            }
        }

        /// <summary>
        /// Performs the actual analytics calculation while holding
        /// the global concurrency limit.
        /// </summary>
        private async Task<AnalyticsApiResponse?>
            BuildAnalyticsWithLimitAsync(
                int schemeCode,
                string? schemeName)
        {
            await AnalyticsLock.WaitAsync();

            try
            {
                return await _analyticsService
                    .BuildAnalyticsAsync(
                        schemeCode,
                        schemeName);
            }
            finally
            {
                AnalyticsLock.Release();
            }
        }

        // =============================================================
        // FIND TOP FUND FOR A PERIOD
        // =============================================================

        /// <summary>
        /// Finds the highest-performing fund for each of the six
        /// categories for the requested period.
        ///
        /// The period is evaluated independently. For example,
        /// a fund only needs a valid 3Y CAGR to compete for 3Y.
        /// It does not need valid 5Y and 10Y CAGR values.
        /// </summary>
        private static List<TopPerformingFund>
            GetTopFundsForPeriod(
                IEnumerable<TopPerformingAnalyticsCandidate> results,
                string period)
        {
            var topFunds = new List<TopPerformingFund>();

            foreach (var category in TopPerformingCategories)
            {
                var candidates =
                    results
                        .Where(x =>
                            x.Analytics != null &&
                            string.Equals(
                                x.Fund.TopCategory,
                                category,
                                StringComparison.OrdinalIgnoreCase))
                        .Select(x => new
                        {
                            x.Fund,
                            Analytics = x.Analytics!,
                            CAGR = GetCagrValue(
                                x.Analytics!,
                                period)
                        })
                        .Where(x => x.CAGR.HasValue)
                        .OrderByDescending(x => x.CAGR!.Value)
                        .ToList();

                if (candidates.Count == 0)
                {
                    continue;
                }

                var top = candidates[0];

                topFunds.Add(
                    new TopPerformingFund
                    {
                        SchemeCode = top.Fund.SchemeCode,
                        SchemeName = top.Fund.SchemeName,
                        FundHouse = top.Fund.FundHouse ?? string.Empty,
                        Category = top.Fund.TopCategory,
                        SubCategory =
                            top.Fund.SubCategory ?? string.Empty,
                        CAGR =
                            GetCagrText(
                                top.Analytics,
                                period)
                    });
            }

            return topFunds;
        }

        // =============================================================
        // CAGR HELPERS
        // =============================================================

        private static decimal? GetCagrValue(
            AnalyticsApiResponse analytics,
            string period)
        {
            string? value;

            switch (period)
            {
                case "3Y":
                    value = analytics.CAGR3Year;
                    break;

                case "5Y":
                    value = analytics.CAGR5Year;
                    break;

                case "10Y":
                    value = analytics.CAGR10Year;
                    break;

                default:
                    return null;
            }

            return TryParsePercentage(
                value,
                out var percentage)
                ? percentage
                : null;
        }

        private static string GetCagrText(
            AnalyticsApiResponse analytics,
            string period)
        {
            return period switch
            {
                "3Y" => analytics.CAGR3Year ?? "N/A",
                "5Y" => analytics.CAGR5Year ?? "N/A",
                "10Y" => analytics.CAGR10Year ?? "N/A",
                _ => "N/A"
            };
        }

        // =============================================================
        // CATEGORY MAPPING
        // =============================================================

        /// <summary>
        /// Converts AMFI category information into the six categories
        /// displayed by the Top Performing Funds section.
        /// </summary>
        private static string? GetTopPerformingCategory(
            string? category,
            string? subCategory,
            string? schemeName)
        {
            var normalizedCategory =
                (category ?? string.Empty)
                    .Trim()
                    .ToLowerInvariant();

            var normalizedSubCategory =
                (subCategory ?? string.Empty)
                    .Trim()
                    .ToLowerInvariant();

            var normalizedSchemeName =
                (schemeName ?? string.Empty)
                    .Trim()
                    .ToLowerInvariant();

            // ---------------------------------------------------------
            // DEBT
            // ---------------------------------------------------------

            if (normalizedCategory.Contains("debt") ||
                normalizedCategory.Contains("income/debt"))
            {
                return "Debt";
            }

            // ---------------------------------------------------------
            // EQUITY
            // ---------------------------------------------------------

            if (normalizedCategory.Contains("equity"))
            {
                return "Equity";
            }

            // ---------------------------------------------------------
            // HYBRID
            // ---------------------------------------------------------

            if (normalizedCategory.Contains("hybrid"))
            {
                return "Hybrid";
            }

            // ---------------------------------------------------------
            // INDEX
            // ---------------------------------------------------------

            if (normalizedCategory.Contains("index") ||
                normalizedSubCategory.Contains("index"))
            {
                return "Index";
            }

            // ---------------------------------------------------------
            // SOLUTION ORIENTED
            // ---------------------------------------------------------

            if (normalizedCategory.Contains("solution oriented"))
            {
                return "Solution Oriented";
            }

            // ---------------------------------------------------------
            // GOLD & SILVER
            // ---------------------------------------------------------

            var isOtherOrFundOfFunds =
                normalizedCategory.Contains("other") ||
                normalizedCategory.Contains("fund of funds");

            var isGoldOrSilver =
                normalizedSchemeName.Contains("gold") ||
                normalizedSchemeName.Contains("silver") ||
                normalizedSubCategory.Contains("gold") ||
                normalizedSubCategory.Contains("silver");

            if (isOtherOrFundOfFunds &&
                isGoldOrSilver)
            {
                return "Gold & Silver";
            }

            return null;
        }

        // =============================================================
        // FILTER FUNDS
        // =============================================================

        /// <summary>
        /// Filters the local AMFI catalogue.
        /// No analytics or MFAPI calls are made during the filtering
        /// operation itself. Analytics are calculated only for the
        /// resulting schemes.
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

            // Convert filtered AMFI records into Scheme objects.
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

            // ---------------------------------------------------------
            // BUILD ANALYTICS
            // ---------------------------------------------------------
            //
            // Analytics requests use the same global six-request limit
            // and cache as Search and Top Performing.
            //

            var analyticsTasks =
                schemes.Select(async scheme =>
                {
                    try
                    {
                        return await GetAnalyticsCachedAsync(
                            scheme.SchemeCode,
                            scheme.SchemeName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Unable to calculate analytics for filtered scheme {SchemeCode}",
                            scheme.SchemeCode);

                        return null;
                    }
                });

            var results =
                await Task.WhenAll(analyticsTasks);

            return results
                .Where(r => r != null)
                .Select(r => r!)
                .ToList();
        }

        // =============================================================
        // OPTION NORMALIZATION
        // =============================================================

        private static string NormalizeFilterOption(string? option)
        {
            if (string.IsNullOrWhiteSpace(option))
            {
                return "Other";
            }

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
                    StringComparison.OrdinalIgnoreCase))
            {
                return "IDCW";
            }

            return "Other";
        }

        // =============================================================
        // PERCENTAGE PARSING
        // =============================================================

        private static bool TryParsePercentage(
            string? value,
            out decimal percentage)
        {
            percentage = 0;

            if (string.IsNullOrWhiteSpace(value) ||
                value.Equals(
                    "N/A",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var cleaned =
                value
                    .Replace("%", "")
                    .Trim();

            return decimal.TryParse(
                cleaned,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out percentage);
        }

        // =============================================================
        // INTERNAL TYPES
        // =============================================================

        private sealed class TopPerformingFundCandidate
        {
            public int SchemeCode { get; init; }

            public string SchemeName { get; init; } = string.Empty;

            public string? FundHouse { get; init; }

            public string? Category { get; init; }

            public string? SubCategory { get; init; }

            public string TopCategory { get; init; } = string.Empty;
        }

        private sealed class TopPerformingAnalyticsCandidate
        {
            public required TopPerformingFundCandidate Fund { get; init; }

            public AnalyticsApiResponse? Analytics { get; init; }
        }
    }
}