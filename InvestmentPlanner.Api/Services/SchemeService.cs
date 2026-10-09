using System.Net.Http.Json;

using InvestmentPlanner.Api.Data;

using InvestmentPlanner.Api.Models;

using InvestmentPlanner.Web.Models;

using Microsoft.EntityFrameworkCore;



namespace InvestmentPlanner.Api.Services

{

    /// <summary>

    /// Responsible for:

    ///   - searching schemes using calculated PostgreSQL analytics

    ///   - searching the local AMFI-generated funds.json catalogue when needed

    ///   - obtaining candidate schemes for the initial eligible-funds list

    ///   - determining eligibility when required

    ///   - filtering funds using calculated PostgreSQL analytics

    ///   - calculating Top Performing Funds

    /// </summary>

    public class SchemeService

    {

        private readonly HttpClient _httpClient;

        private readonly AnalyticsCacheService _analyticsCacheService;

        private readonly ILogger<SchemeService> _logger;

        private readonly AmfiFundDataService _amfiFundDataService;

        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;



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



        /*

         * Search and Filter can find hundreds or thousands of local

         * catalogue records.

         *

         * We do NOT want to calculate full historical analytics for all

         * of them in one HTTP request.

         *

         * Database-backed Search and Filter do NOT use this limit.

         */

        private const int MaxAnalyticsPerRequest = 15;

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



        /*

         * Prevents multiple Top Performing calculations from happening

         * simultaneously.

         */

        private static readonly SemaphoreSlim TopPerformingLock =

            new(1, 1);



        /*

         * Stores the completed Top Performing result.

         *

         * Once populated, subsequent requests return it immediately.

         */

        private static TopPerformingFundsResponse? _topPerformingCache;



        public SchemeService(

            HttpClient httpClient,

            AnalyticsCacheService analyticsCacheService,

            ILogger<SchemeService> logger,

            AmfiFundDataService amfiFundDataService,

            IDbContextFactory<AppDbContext> dbContextFactory)

        {

            _httpClient = httpClient;

            _analyticsCacheService = analyticsCacheService;

            _logger = logger;

            _amfiFundDataService = amfiFundDataService;

            _dbContextFactory = dbContextFactory;

        }



        // =============================================================

        // RAW MFAPI SEARCH

        // =============================================================



        /// <summary>

        /// Calls MFAPI's live search endpoint.

        ///

        /// Used only as a fallback when the local AMFI catalogue

        /// does not contain a match.

        /// </summary>

        public async Task<List<Scheme>> SearchSchemesRawAsync(

            string query)

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

                    await response.Content

                        .ReadFromJsonAsync<List<Scheme>>();



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

        /// Search is performed against FundAnalytics in PostgreSQL so that

        /// already-calculated analytics can be returned without downloading

        /// NAV histories from MFAPI.

        ///

        /// If the database contains no matching records, the existing

        /// AMFI/MFAPI path is used as a fallback.

        ///

        /// IMPORTANT:

        /// Database search does NOT apply the old 25-result limit.

        ///

        /// The browser receives all matching database records and performs

        /// the existing 15-results-per-page pagination on the client.

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



            try

            {

                // -----------------------------------------------------

                // FIRST: SEARCH STORED ANALYTICS IN POSTGRESQL

                // -----------------------------------------------------



                /*

                 * The analytics have already been calculated and stored

                 * in FundAnalytics.

                 *

                 * Therefore this request does NOT need to call MFAPI.

                 *

                 * No Take(maxResults) is intentionally used here.

                 * All matching database records are returned.

                 */

                await using var dbContext =
                    await _dbContextFactory.CreateDbContextAsync();

                var funds = await dbContext.FundAnalytics

                    .AsNoTracking()

                    .Where(f =>

                        EF.Functions.ILike(

                            f.SchemeName,

                            $"%{search}%") ||



                        EF.Functions.ILike(

                            f.FundHouse,

                            $"%{search}%") ||



                        EF.Functions.ILike(

                            f.Category,

                            $"%{search}%") ||



                        EF.Functions.ILike(

                            f.SubCategory,

                            $"%{search}%"))

                    .OrderBy(f => f.SchemeName)

                    .ToListAsync();



                _logger.LogInformation(

                    "Database search '{Query}' found {Count} analytics records.",

                    search,

                    funds.Count);



                if (funds.Count > 0)

                {

                    return funds

                        .Select(MapFundAnalyticsToResponse)

                        .ToList();

                }



                // -----------------------------------------------------

                // FALLBACK: LOCAL AMFI CATALOGUE

                // -----------------------------------------------------



                /*

                 * Some valid AMFI schemes may not have been populated

                 * into FundAnalytics yet.

                 *

                 * Preserve search functionality for those schemes by

                 * falling back to the existing catalogue/MFAPI path.

                 */

                var localFunds = await _amfiFundDataService

                    .SearchFundsAsync(search);



                var fallbackSchemes = localFunds

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

                    .Take(MaxAnalyticsPerRequest)

                    .ToList();



                // -----------------------------------------------------

                // FALLBACK: MFAPI SEARCH

                // -----------------------------------------------------



                if (fallbackSchemes.Count == 0)

                {

                    fallbackSchemes =

                        (await SearchSchemesRawAsync(search))

                            .Take(maxResults)

                            .ToList();

                }



                if (fallbackSchemes.Count == 0)

                {

                    return new List<AnalyticsApiResponse>();

                }



                // -----------------------------------------------------

                // CALCULATE FALLBACK ANALYTICS

                // -----------------------------------------------------



                var fallbackTasks =

                    fallbackSchemes.Select(async scheme =>

                    {

                        try

                        {

                            return await _analyticsCacheService.GetAnalyticsAsync(

                                scheme.SchemeCode,

                                scheme.SchemeName);

                        }

                        catch (Exception analyticsException)

                        {

                            _logger.LogWarning(

                                analyticsException,

                                "Unable to calculate analytics for scheme {SchemeCode}",

                                scheme.SchemeCode);



                            return null;

                        }

                    });



                var fallbackResults =

                    await Task.WhenAll(

                        fallbackTasks);



                return fallbackResults

                    .Where(r => r != null)

                    .Select(r => r!)

                    .ToList();

            }

            catch (Exception ex)

            {

                _logger.LogError(

                    ex,

                    "Error searching FundAnalytics for query '{Query}'.",

                    search);



                // -----------------------------------------------------

                // DATABASE FAILURE FALLBACK

                // -----------------------------------------------------



                /*

                 * If PostgreSQL search is temporarily unavailable,

                 * preserve the existing MFAPI fallback behaviour.

                 */

                var rawResults =

                    await SearchSchemesRawAsync(search);



                var schemes =

                    rawResults

                        .Take(maxResults)

                        .ToList();



                if (schemes.Count == 0)

                {

                    return new List<AnalyticsApiResponse>();

                }



                var analyticsTasks =

                    schemes.Select(async scheme =>

                    {

                        try

                        {

                            return await _analyticsCacheService.GetAnalyticsAsync(

                                scheme.SchemeCode,

                                scheme.SchemeName);

                        }

                        catch (Exception analyticsException)

                        {

                            _logger.LogWarning(

                                analyticsException,

                                "Unable to calculate analytics for scheme {SchemeCode}",

                                scheme.SchemeCode);



                            return null;

                        }

                    });



                var results =

                    await Task.WhenAll(

                        analyticsTasks);



                return results

                    .Where(r => r != null)

                    .Select(r => r!)

                    .ToList();

            }

        }



        // =============================================================

        // ELIGIBLE FUNDS

        // =============================================================



        /// <summary>

        /// Discovers funds that have all required analytics periods.

        /// </summary>

        public async Task<List<AnalyticsApiResponse>>

            GetEligibleFundsAsync(

                int desiredCount = DefaultEligibleCount,

                int maxCandidatesToCheck =

                    MaxEligibleCandidatesToCheck)

        {

            var eligible =

                new List<AnalyticsApiResponse>();



            var checkedSchemeCodes =

                new HashSet<int>();



            foreach (var seed in EligibleCandidateSeeds)

            {

                if (eligible.Count >= desiredCount ||

                    checkedSchemeCodes.Count >=

                    maxCandidatesToCheck)

                {

                    break;

                }



                var candidates =

                    await SearchSchemesRawAsync(seed);



                foreach (var candidate in candidates)

                {

                    if (eligible.Count >= desiredCount ||

                        checkedSchemeCodes.Count >=

                        maxCandidatesToCheck)

                    {

                        break;

                    }



                    if (!checkedSchemeCodes.Add(

                            candidate.SchemeCode))

                    {

                        continue;

                    }



                    AnalyticsApiResponse? analytics;



                    try

                    {

                        analytics =

                            await _analyticsCacheService.GetAnalyticsAsync(

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

                        AnalyticsService.HasAllPeriods(

                            analytics))

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

        /// Returns all available filter values from the local

        /// AMFI catalogue.

        ///

        /// This method does NOT call MFAPI.

        /// </summary>

        public async Task<FilterOptions> GetFilterOptionsAsync()
{
    var funds = await _amfiFundDataService.LoadFundsAsync();

    // Use exact subcategory values from the database used by filtering.
    await using var dbContext =
        await _dbContextFactory.CreateDbContextAsync();

    var subCategories = await dbContext.FundAnalytics
        .AsNoTracking()
        .Where(f =>
            f.SubCategory != null &&
            f.SubCategory.Trim() != "")
        .Select(f => f.SubCategory!.Trim())
        .Distinct()
        .OrderBy(x => x)
        .ToListAsync();

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

        SubCategories = subCategories,

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

        /// If the calculation has already completed, this returns

        /// immediately.

        ///

        /// Otherwise the calculation is performed once.

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

        /// </summary>

        public async Task RefreshTopPerformingFundsAsync()

        {

            await CalculateTopPerformingFundsAsync();

        }



        /// <summary>

        /// Performs the Top Performing calculation.

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

                if (_topPerformingCache != null)

                {

                    return _topPerformingCache;

                }



                _logger.LogInformation(

                    "Starting Top Performing Funds calculation.");



                var funds =

                    await _amfiFundDataService

                        .LoadFundsAsync();



                // -----------------------------------------------------

                // GET DIRECT + GROWTH SCHEMES

                // -----------------------------------------------------



                var schemes =

                    funds

                        .Where(f =>

                            !string.IsNullOrWhiteSpace(

                                f.SchemeCode) &&



                            !string.IsNullOrWhiteSpace(

                                f.SchemeName) &&



                            string.Equals(

                                f.Plan?.Trim(),

                                "Direct",

                                StringComparison.OrdinalIgnoreCase) &&



                            string.Equals(

                                NormalizeFilterOption(

                                    f.Option),

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



                /*

                 * IMPORTANT:

                 *

                 * AnalyticsCacheService applies the global concurrency limit.

                 *

                 * Therefore Top Performing can safely request analytics

                 * for many schemes without sharing an EF Core DbContext.

                 */

                var analyticsTasks =

                    schemes.Select(async fund =>

                    {

                        try

                        {

                            var analytics =

                                await _analyticsCacheService.GetAnalyticsAsync(

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

                    await Task.WhenAll(

                        analyticsTasks);



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



                _topPerformingCache =

                    response;



                _logger.LogInformation(

                    "Top Performing Funds calculation completed. " +

                    "3Y: {ThreeYearCount}, " +

                    "5Y: {FiveYearCount}, " +

                    "10Y: {TenYearCount}",

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

        /*

         * Analytics caching and concurrency control are handled by

         * AnalyticsCacheService.

         *

         * SchemeService only requests analytics through that shared

         * application-level service.

         */

        // =============================================================

        // FIND TOP FUND FOR A PERIOD

        // =============================================================



        /// <summary>

        /// Finds the highest-performing fund for each configured

        /// category for the requested period.

        /// </summary>

        private static List<TopPerformingFund>

            GetTopFundsForPeriod(

                IEnumerable<

                    TopPerformingAnalyticsCandidate> results,

                string period)

        {

            var topFunds =

                new List<TopPerformingFund>();



            foreach (var category

                in TopPerformingCategories)

            {

                var candidates =

                    results

                        .Where(x =>

                            x.Analytics != null &&

                            string.Equals(

                                x.Fund.TopCategory,

                                category,

                                StringComparison

                                    .OrdinalIgnoreCase))

                        .Select(x => new

                        {

                            x.Fund,

                            Analytics = x.Analytics!,

                            CAGR = GetCagrValue(

                                x.Analytics!,

                                period)

                        })

                        .Where(x =>

                            x.CAGR.HasValue)

                        .OrderByDescending(x =>

                            x.CAGR!.Value)

                        .ToList();



                if (candidates.Count == 0)

                {

                    continue;

                }



                var top =

                    candidates[0];



                topFunds.Add(

                    new TopPerformingFund

                    {

                        SchemeCode =

                            top.Fund.SchemeCode,



                        SchemeName =

                            top.Fund.SchemeName,



                        FundHouse =

                            top.Fund.FundHouse ??

                            string.Empty,



                        Category =

                            top.Fund.TopCategory,



                        SubCategory =

                            top.Fund.SubCategory ??

                            string.Empty,



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

                    value =

                        analytics.CAGR3Year;

                    break;



                case "5Y":

                    value =

                        analytics.CAGR5Year;

                    break;



                case "10Y":

                    value =

                        analytics.CAGR10Year;

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

                "3Y" =>

                    analytics.CAGR3Year ??

                    "N/A",



                "5Y" =>

                    analytics.CAGR5Year ??

                    "N/A",



                "10Y" =>

                    analytics.CAGR10Year ??

                    "N/A",



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

        private static string?

            GetTopPerformingCategory(

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



            if (normalizedCategory.Contains(

                    "solution oriented"))

            {

                return "Solution Oriented";

            }



            // ---------------------------------------------------------

            // GOLD & SILVER

            // ---------------------------------------------------------



            var isOtherOrFundOfFunds =

                normalizedCategory.Contains("other") ||

                normalizedCategory.Contains(

                    "fund of funds");



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

        /// Filters the calculated FundAnalytics records stored in

        /// PostgreSQL.

        ///

        /// Filtering itself does not call MFAPI.

        ///

        /// IMPORTANT:

        /// There is intentionally NO Take(15) here.

        ///

        /// All matching database records are returned to the Web

        /// application. The existing JavaScript handles pagination

        /// and displays 15 funds per page.

        /// </summary>

        public async Task<List<AnalyticsApiResponse>>

            FilterFundsAsync(

                FundFilterRequest request)

        {

            request ??= new FundFilterRequest();

            await using var dbContext =
                await _dbContextFactory.CreateDbContextAsync();

            var query =

                dbContext.FundAnalytics

                    .AsNoTracking()

                    .AsQueryable();



            // ---------------------------------------------------------

            // FUND HOUSE

            // ---------------------------------------------------------



            if (request.FundHouses.Count > 0)

            {

                var selected =

                    request.FundHouses

                        .Where(x =>

                            !string.IsNullOrWhiteSpace(x))

                        .Select(x =>

                            x.Trim())

                        .ToList();



                if (selected.Count > 0)

                {

                    query =

                        query.Where(f =>

                            selected.Contains(

                                f.FundHouse));

                }

            }



            // ---------------------------------------------------------

            // CATEGORY

            // ---------------------------------------------------------



            if (request.Categories.Count > 0)

            {

                var selected =

                    request.Categories

                        .Where(x =>

                            !string.IsNullOrWhiteSpace(x))

                        .Select(x =>

                            x.Trim())

                        .ToList();



                if (selected.Count > 0)

                {

                    query =

                        query.Where(f =>

                            selected.Contains(

                                f.Category));

                }

            }



            // ---------------------------------------------------------

            // SUB-CATEGORY

            // ---------------------------------------------------------



            if (request.SubCategories.Count > 0)

            {

                var selected =

                    request.SubCategories

                        .Where(x =>

                            !string.IsNullOrWhiteSpace(x))

                        .Select(x =>

                            x.Trim())

                        .ToList();



                if (selected.Count > 0)

                {

                    query =

                        query.Where(f =>

                            selected.Contains(

                                f.SubCategory));

                }

            }



            // ---------------------------------------------------------

            // PLAN

            // ---------------------------------------------------------



            if (request.Plans.Count > 0)

            {

                var selected =

                    request.Plans

                        .Where(x =>

                            !string.IsNullOrWhiteSpace(x))

                        .Select(x =>

                            x.Trim())

                        .ToList();



                if (selected.Count > 0)

                {

                    query =

                        query.Where(f =>

                            selected.Contains(

                                f.Plan));

                }

            }



            // ---------------------------------------------------------

            // OPTION

            // ---------------------------------------------------------

            //

            // Normalize the requested UI values into SQL-translatable

            // predicates.

            //

            // NormalizeFilterOption itself is intentionally NOT called

            // inside the EF query because it is a C# method and cannot

            // be translated directly into SQL.

            // ---------------------------------------------------------



            if (request.Options.Count > 0)

            {

                var selectedOptions =

                    new HashSet<string>(

                        request.Options

                            .Where(x =>

                                !string.IsNullOrWhiteSpace(x))

                            .Select(x =>

                                x.Trim()),

                        StringComparer.OrdinalIgnoreCase);



                var includeGrowth =

                    selectedOptions.Contains(

                        "Growth");



                var includeIdcw =

                    selectedOptions.Contains(

                        "IDCW");



                var includeOther =

                    selectedOptions.Contains(

                        "Other");



                if (!includeGrowth &&

                    !includeIdcw &&

                    !includeOther)

                {

                    return new List<AnalyticsApiResponse>();

                }



                query =

                    query.Where(f =>

                        (

                            includeGrowth &&

                            f.Option != null &&

                            EF.Functions.ILike(

                                f.Option,

                                "Growth")

                        )

                        ||

                        (

                            includeIdcw &&

                            f.Option != null &&

                            (

                                EF.Functions.ILike(

                                    f.Option,

                                    "%IDCW%")

                                ||

                                EF.Functions.ILike(

                                    f.Option,

                                    "%Dividend%")

                                ||

                                EF.Functions.ILike(

                                    f.Option,

                                    "%Income Distribution%")

                            )

                        )

                        ||

                        (

                            includeOther &&

                            (

                                string.IsNullOrWhiteSpace(

                                    f.Option)

                                ||

                                (

                                    !EF.Functions.ILike(

                                        f.Option,

                                        "Growth")

                                    &&

                                    !EF.Functions.ILike(

                                        f.Option,

                                        "%IDCW%")

                                    &&

                                    !EF.Functions.ILike(

                                        f.Option,

                                        "%Dividend%")

                                    &&

                                    !EF.Functions.ILike(

                                        f.Option,

                                        "%Income Distribution%")

                                )

                            )

                        ));

            }



            // ---------------------------------------------------------

            // EXECUTE DATABASE QUERY

            // ---------------------------------------------------------



            /*

             * IMPORTANT:

             *

             * There is NO Take(MaxAnalyticsPerRequest) here.

             *

             * FundAnalytics already contains calculated analytics,

             * so returning all matching records does not require MFAPI

             * calls.

             *

             * The Web application's JavaScript will paginate these

             * records at 15 per page.

             */

            var funds =

                await query

                    .OrderBy(f =>

                        f.SchemeName)

                    .ToListAsync();



            _logger.LogInformation(

                "Database filter matched {Count} analytics records.",

                funds.Count);



            return funds

                .Select(

                    MapFundAnalyticsToResponse)

                .ToList();

        }



        // =============================================================

        // DATABASE ANALYTICS -> API RESPONSE

        // =============================================================



        private static AnalyticsApiResponse

            MapFundAnalyticsToResponse(

                FundAnalytics fund)

        {

            return new AnalyticsApiResponse

            {

                SchemeCode =

                    fund.SchemeCode,



                SchemeName =

                    fund.SchemeName,



                CurrentNAV =

                    fund.CurrentNAV,



                Return1Month =

                    FormatPercentage(

                        fund.Return1Month),



                Return3Month =

                    FormatPercentage(

                        fund.Return3Month),



                Return6Month =

                    FormatPercentage(

                        fund.Return6Month),



                CAGR1Year =

                    FormatPercentage(

                        fund.CAGR1Year),



                CAGR3Year =

                    FormatPercentage(

                        fund.CAGR3Year),



                CAGR5Year =

                    FormatPercentage(

                        fund.CAGR5Year),



                CAGR10Year =

                    FormatPercentage(

                        fund.CAGR10Year),



                FundHouse =

                    fund.FundHouse,



                SchemeCategory =

                    fund.Category,



                SchemeSubCategory =

                    fund.SubCategory,



                Plan =

                    fund.Plan,



                Option =

                    NormalizeFilterOption(

                        fund.Option)

            };

        }



        private static string

            FormatPercentage(

                decimal? value)

        {

            return value.HasValue

                ? $"{value.Value:0.00}%"

                : "N/A";

        }



        // =============================================================

        // OPTION NORMALIZATION

        // =============================================================



        private static string

            NormalizeFilterOption(

                string? option)

        {

            if (string.IsNullOrWhiteSpace(option))

            {

                return "Other";

            }



            var value =

                option.Trim();



            if (value.Equals(

                    "Growth",

                    StringComparison.OrdinalIgnoreCase))

            {

                return "Growth";

            }



            if (

                value.Equals(

                    "IDCW",

                    StringComparison.OrdinalIgnoreCase)

                ||

                value.Contains(

                    "IDCW",

                    StringComparison.OrdinalIgnoreCase)

                ||

                value.Contains(

                    "Dividend",

                    StringComparison.OrdinalIgnoreCase)

                ||

                value.Contains(

                    "Income Distribution",

                    StringComparison.OrdinalIgnoreCase)

            )

            {

                return "IDCW";

            }



            return "Other";

        }



        // =============================================================

        // PERCENTAGE PARSING

        // =============================================================



        private static bool

            TryParsePercentage(

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

                    .Replace(

                        "%",

                        "")

                    .Trim();



            return decimal.TryParse(

                cleaned,

                System.Globalization

                    .NumberStyles.Any,

                System.Globalization

                    .CultureInfo.InvariantCulture,

                out percentage);

        }



        // =============================================================

        // INTERNAL TYPES

        // =============================================================



        private sealed class

            TopPerformingFundCandidate

        {

            public int SchemeCode

            {

                get;

                init;

            }



            public string SchemeName

            {

                get;

                init;

            } = string.Empty;



            public string? FundHouse

            {

                get;

                init;

            }



            public string? Category

            {

                get;

                init;

            }



            public string? SubCategory

            {

                get;

                init;

            }



            public string TopCategory

            {

                get;

                init;

            } = string.Empty;

        }



        private sealed class

            TopPerformingAnalyticsCandidate

        {

            public required

                TopPerformingFundCandidate Fund

            {

                get;

                init;

            }



            public AnalyticsApiResponse?

                Analytics

            {

                get;

                init;

            }

        }

    }

}