using InvestmentPlanner.Api.Data;
using InvestmentPlanner.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace InvestmentPlanner.Api.Services
{
    /// <summary>
    /// Provides stored mutual-fund analytics from the FundAnalytics database table.
    ///
    /// The expensive NAV fetching and analytics calculation is performed by
    /// FundAnalyticsUpdateService during the controlled population process.
    ///
    /// This service only reads the already-calculated analytics for normal
    /// fund-detail requests.
    ///
    /// A new DbContext is created for each database operation so that
    /// concurrent analytics requests do not share the same EF Core DbContext.
    /// </summary>
    public class AnalyticsService
    {
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly ILogger<AnalyticsService> _logger;

        public AnalyticsService(
            IDbContextFactory<AppDbContext> dbContextFactory,
            ILogger<AnalyticsService> logger)
        {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        /// <summary>
        /// Returns stored analytics for a single scheme.
        ///
        /// This method does NOT call MFAPI and does NOT recalculate returns/CAGR.
        /// It reads the analytics that were previously populated into PostgreSQL.
        ///
        /// A separate DbContext is created for this operation so multiple
        /// analytics requests can safely execute concurrently.
        ///
        /// Returns null when the scheme does not exist in FundAnalytics.
        /// </summary>
        public async Task<AnalyticsApiResponse?> BuildAnalyticsAsync(
            int schemeCode,
            string? schemeNameHint = null)
        {
            if (schemeCode <= 0)
            {
                return null;
            }

            try
            {
                await using var dbContext =
                    await _dbContextFactory.CreateDbContextAsync();

                var fund = await dbContext.FundAnalytics
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x => x.SchemeCode == schemeCode);

                if (fund == null)
                {
                    _logger.LogWarning(
                        "No stored analytics found for scheme {SchemeCode}",
                        schemeCode);

                    return null;
                }

                return MapToAnalyticsResponse(
                    fund,
                    schemeNameHint);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error loading stored analytics for scheme {SchemeCode}",
                    schemeCode);

                return null;
            }
        }

        /// <summary>
        /// Maps a stored FundAnalytics entity to the API response used
        /// by the Web application.
        /// </summary>
        private static AnalyticsApiResponse MapToAnalyticsResponse(
            FundAnalytics fund,
            string? schemeNameHint = null)
        {
            return new AnalyticsApiResponse
            {
                SchemeCode = fund.SchemeCode,

                SchemeName = string.IsNullOrWhiteSpace(fund.SchemeName)
                    ? (schemeNameHint ?? $"Scheme {fund.SchemeCode}")
                    : fund.SchemeName,

                CurrentNAV = fund.CurrentNAV,

                Return1Month = FormatNullablePercent(
                    fund.Return1Month),

                Return3Month = FormatNullablePercent(
                    fund.Return3Month),

                Return6Month = FormatNullablePercent(
                    fund.Return6Month),

                CAGR1Year = FormatNullablePercent(
                    fund.CAGR1Year),

                CAGR3Year = FormatNullablePercent(
                    fund.CAGR3Year),

                CAGR5Year = FormatNullablePercent(
                    fund.CAGR5Year),

                CAGR10Year = FormatNullablePercent(
                    fund.CAGR10Year),

                FundHouse = fund.FundHouse,

                SchemeCategory = fund.Category,

                SchemeSubCategory = fund.SubCategory,

                Plan = string.IsNullOrWhiteSpace(fund.Plan)
                    ? "Standard"
                    : fund.Plan,

                Option = string.IsNullOrWhiteSpace(fund.Option)
                    ? "Other"
                    : fund.Option
            };
        }

        /// <summary>
        /// Converts a nullable database percentage into the string format
        /// expected by AnalyticsApiResponse.
        ///
        /// Example:
        /// 15.2345 -> "15.23%"
        /// null    -> "N/A"
        /// </summary>
        private static string FormatNullablePercent(decimal? value)
        {
            if (value == null)
            {
                return "N/A";
            }

            return value.Value.ToString(
                "0.00",
                CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>
        /// Returns true only when every required period has real analytics data.
        ///
        /// Used by existing application logic to determine whether a fund
        /// has complete analytics coverage.
        /// </summary>
        public static bool HasAllPeriods(
            AnalyticsApiResponse response)
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
    }
}