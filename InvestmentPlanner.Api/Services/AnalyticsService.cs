using InvestmentPlanner.Api.Models;

namespace InvestmentPlanner.Api.Services
{
    /// <summary>
    /// The ONLY implementation responsible for:
    ///   - finding historical NAV (on or before a target date)
    ///   - calculating monthly returns
    ///   - calculating CAGR
    ///   - handling missing historical periods (returns "N/A", never throws)
    /// </summary>
    public class AnalyticsService
    {
        private readonly NAVService _navService;
        private readonly ILogger<AnalyticsService> _logger;

        public AnalyticsService(NAVService navService, ILogger<AnalyticsService> logger)
        {
            _navService = navService;
            _logger = logger;
        }

        /// <summary>
        /// Builds the full analytics response for a scheme code. Returns null
        /// only when the fund itself cannot be found or has no usable NAV
        /// data at all - individual missing periods are represented as "N/A"
        /// inside the response rather than failing the whole call.
        /// </summary>
        public async Task<AnalyticsApiResponse?> BuildAnalyticsAsync(int schemeCode, string? schemeNameHint = null)
        {
            MutualFundResponse? fund;

            try
            {
                fund = await _navService.GetFundDataAsync(schemeCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error building analytics for scheme {SchemeCode}", schemeCode);
                return null;
            }

            if (fund == null || fund.NavHistory.Count == 0)
            {
                return null;
            }

            // NavHistory is sorted descending by NAVService, so the first entry is "current".
            var latest = fund.NavHistory[0];
            var currentNav = latest.Nav;
            var currentDate = latest.Date;

            decimal? historicalNavOnOrBefore(DateTime target)
            {
                // "most appropriate NAV on or before the target date" - no exact match required.
                NavDataPoint? match = null;
                foreach (var point in fund.NavHistory)
                {
                    if (point.Date <= target && (match == null || point.Date > match.Date))
                    {
                        match = point;
                    }
                }
                return match?.Nav;
            }

            string? calculateReturnPercent(int months)
            {
                var targetDate = currentDate.AddMonths(-months);
                var historicalNav = historicalNavOnOrBefore(targetDate);

                if (historicalNav is null or <= 0)
                {
                    return null;
                }

                var pct = ((currentNav / historicalNav.Value) - 1m) * 100m;
                return FormatPercent(pct);
            }

            string? calculateCagrPercent(int years)
            {
                var targetDate = currentDate.AddYears(-years);
                var historicalNav = historicalNavOnOrBefore(targetDate);

                if (historicalNav is null or <= 0)
                {
                    return null;
                }

                // ((Current NAV / Historical NAV) ^ (1 / Years)) - 1
                var ratio = (double)(currentNav / historicalNav.Value);
                var cagr = (Math.Pow(ratio, 1.0 / years) - 1.0) * 100.0;
                return FormatPercent((decimal)cagr);
            }

            var (category, subCategory) = SplitCategory(fund.SchemeCategory);

            return new AnalyticsApiResponse
            {
                SchemeCode = fund.SchemeCode,
                SchemeName = string.IsNullOrWhiteSpace(fund.SchemeName)
                    ? (schemeNameHint ?? $"Scheme {schemeCode}")
                    : fund.SchemeName,
                CurrentNAV = currentNav,

                Return1Month = calculateReturnPercent(1) ?? "N/A",
                Return3Month = calculateReturnPercent(3) ?? "N/A",
                Return6Month = calculateReturnPercent(6) ?? "N/A",

                CAGR1Year = calculateCagrPercent(1) ?? "N/A",
                CAGR3Year = calculateCagrPercent(3) ?? "N/A",
                CAGR5Year = calculateCagrPercent(5) ?? "N/A",
                CAGR10Year = calculateCagrPercent(10) ?? "N/A",

                FundHouse = fund.FundHouse,
                SchemeCategory = category,
                SchemeSubCategory = subCategory,
                Plan = InferPlan(fund.SchemeName),
                Option = InferOption(fund.SchemeName)
            };
        }

        /// <summary>
        /// Returns true only when every required period (1M/3M/6M/1Y/3Y/5Y/10Y)
        /// has real data - used to decide "initial eligible fund" membership.
        /// </summary>
        public static bool HasAllPeriods(AnalyticsApiResponse response)
        {
            return response.CurrentNAV > 0
                && response.Return1Month != "N/A"
                && response.Return3Month != "N/A"
                && response.Return6Month != "N/A"
                && response.CAGR1Year != "N/A"
                && response.CAGR3Year != "N/A"
                && response.CAGR5Year != "N/A"
                && response.CAGR10Year != "N/A";
        }

        private static string FormatPercent(decimal value)
        {
            return value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "%";
        }

        private static (string? category, string? subCategory) SplitCategory(string? rawCategory)
        {
            if (string.IsNullOrWhiteSpace(rawCategory))
            {
                return (null, null);
            }

            // MFAPI categories commonly look like "Debt Scheme - Liquid Fund".
            var parts = rawCategory.Split(" - ", 2, StringSplitOptions.TrimEntries);
            return parts.Length == 2 ? (parts[0], parts[1]) : (rawCategory, null);
        }

        private static string InferPlan(string schemeName)
        {
            var name = schemeName.ToLowerInvariant();

            if (name.Contains("defunct") || name.Contains("closed for subscription"))
            {
                return "Defunct";
            }
            if (name.Contains("direct"))
            {
                return "Direct";
            }
            if (name.Contains("regular"))
            {
                return "Regular";
            }
            if (name.Contains("institutional"))
            {
                return "Institutional";
            }
            if (name.Contains("retail"))
            {
                return "Retail";
            }

            return "Standard";
        }

        private static string InferOption(string schemeName)
        {
            var name = schemeName.ToLowerInvariant();

            if (name.Contains("idcw") || name.Contains("dividend"))
            {
                return "IDCW";
            }
            if (name.Contains("growth"))
            {
                return "Growth";
            }

            return "Other";
        }
    }
}
