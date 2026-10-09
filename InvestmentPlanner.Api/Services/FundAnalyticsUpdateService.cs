using System.Text.Json;
using InvestmentPlanner.Api.Data;
using InvestmentPlanner.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace InvestmentPlanner.Api.Services;

public class FundAnalyticsUpdateService
{
    private readonly MfApiService _mfApiService;
    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FundAnalyticsUpdateService> _logger;

    private const int DefaultBatchSize = 50;
    private const int MaxConcurrentRequests = 5;

    public FundAnalyticsUpdateService(
        MfApiService mfApiService,
        AppDbContext dbContext,
        IConfiguration configuration,
        ILogger<FundAnalyticsUpdateService> logger)
    {
        _mfApiService = mfApiService;
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
    }

    // ============================================================
    // EXISTING ANALYTICS UPDATE
    // ============================================================

    public async Task<UpdateResult> UpdateFundsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Limit must be greater than zero.");
        }

        var funds =
            await LoadFundsAsync(cancellationToken);

        if (funds.Count == 0)
        {
            _logger.LogWarning(
                "No funds found in funds.json.");

            return new UpdateResult();
        }

        // Successful funds already stored in FundAnalytics.
        var existingSchemeCodes =
            (
                await _dbContext.FundAnalytics
                    .AsNoTracking()
                    .Select(x => x.SchemeCode)
                    .ToListAsync(cancellationToken)
            ).ToHashSet();

        // Permanently unavailable funds are stored separately.
        // They should not consume future processing batches.
        var permanentFailureSchemeCodes =
            (
                await _dbContext.FundAnalyticsFailures
                    .AsNoTracking()
                    .Where(x => x.IsPermanent)
                    .Select(x => x.SchemeCode)
                    .ToListAsync(cancellationToken)
            ).ToHashSet();

        _logger.LogInformation(
            "Found {ExistingCount} existing FundAnalytics records.",
            existingSchemeCodes.Count);

        _logger.LogInformation(
            "Found {PermanentFailureCount} permanently unavailable funds.",
            permanentFailureSchemeCodes.Count);

        // Only process funds that:
        // 1. Have a valid scheme code.
        // 2. Are not already successfully stored.
        // 3. Are not permanently unavailable.
        var fundsToProcess =
            funds
                .Where(fund =>
                    int.TryParse(
                        fund.SchemeCode,
                        out var schemeCode) &&
                    !existingSchemeCodes.Contains(schemeCode) &&
                    !permanentFailureSchemeCodes.Contains(schemeCode))
                .Take(limit)
                .ToList();

        if (fundsToProcess.Count == 0)
        {
            _logger.LogInformation(
                "No new funds are available for processing.");

            return new UpdateResult();
        }

        _logger.LogInformation(
            "Starting analytics update for {Count} funds.",
            fundsToProcess.Count);

        var result = new UpdateResult();

        var failedFunds =
            new List<FailedFund>();

        for (
            var batchStart = 0;
            batchStart < fundsToProcess.Count;
            batchStart += DefaultBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch =
                fundsToProcess
                    .Skip(batchStart)
                    .Take(DefaultBatchSize)
                    .ToList();

            _logger.LogInformation(
                "Processing batch {BatchStart}-{BatchEnd} of {Total}.",
                batchStart + 1,
                Math.Min(
                    batchStart + batch.Count,
                    fundsToProcess.Count),
                fundsToProcess.Count);

            using var semaphore =
                new SemaphoreSlim(
                    MaxConcurrentRequests);

            var tasks =
                batch.Select(
                    fund =>
                        FetchAnalyticsAsync(
                            fund,
                            semaphore,
                            cancellationToken))
                    .ToList();

            var batchResults =
                await Task.WhenAll(tasks);

            foreach (var batchResult in batchResults)
            {
                cancellationToken.ThrowIfCancellationRequested();

                result.Processed++;

                if (batchResult.Analytics == null)
                {
                    result.Failed++;

                    var failureReason =
                        batchResult.FailureReason ??
                        "Unknown failure.";

                    failedFunds.Add(
                        new FailedFund(
                            batchResult.SchemeCode,
                            failureReason));

                    await UpsertFailureAsync(
                        batchResult.SchemeCode,
                        failureReason,
                        batchResult.IsPermanentFailure,
                        cancellationToken);

                    continue;
                }

                try
                {
                    await UpsertAsync(
                        batchResult.Analytics,
                        cancellationToken);

                    // If this scheme previously had a transient failure
                    // record, remove it because it succeeded now.
                    await RemoveFailureAsync(
                        batchResult.SchemeCode,
                        cancellationToken);

                    result.Succeeded++;
                }
                catch (Exception ex)
                {
                    result.Failed++;

                    var databaseFailureReason =
                        $"Database error: {ex.Message}";

                    failedFunds.Add(
                        new FailedFund(
                            batchResult.SchemeCode,
                            databaseFailureReason));

                    _logger.LogError(
                        ex,
                        "Failed to save analytics for scheme {SchemeCode}.",
                        batchResult.SchemeCode);
                }
            }

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            _dbContext.ChangeTracker.Clear();

            LogProgress(
                result,
                fundsToProcess.Count);
        }

        _logger.LogInformation(
            "Analytics update completed. " +
            "Processed: {Processed}, Succeeded: {Succeeded}, Failed: {Failed}",
            result.Processed,
            result.Succeeded,
            result.Failed);

        if (failedFunds.Count > 0)
        {
            _logger.LogWarning(
                "========== FAILED FUNDS ({Count}) ==========",
                failedFunds.Count);

            foreach (var failedFund in failedFunds)
            {
                _logger.LogWarning(
                    "SchemeCode: {SchemeCode} | Reason: {Reason}",
                    failedFund.SchemeCode,
                    failedFund.Reason);
            }

            _logger.LogWarning(
                "========== END FAILED FUNDS ==========");
        }

        result.FailedFunds =
            failedFunds;

        return result;
    }

    // ============================================================
    // NEW: METADATA-ONLY SYNCHRONIZATION
    // ============================================================

    public async Task<FundMetadataSyncResult> SyncFundMetadataAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting fund metadata synchronization.");

        var funds =
            await LoadFundsAsync(cancellationToken);

        if (funds.Count == 0)
        {
            _logger.LogWarning(
                "No funds found in funds.json. Metadata synchronization skipped.");

            return new FundMetadataSyncResult();
        }

        // Convert valid SchemeCodes from funds.json into integers.
        var fundMetadataBySchemeCode =
            funds
                .Select(fund =>
                {
                    if (int.TryParse(
                            fund.SchemeCode,
                            out var schemeCode))
                    {
                        return new
                        {
                            IsValid = true,
                            SchemeCode = schemeCode,
                            Fund = fund
                        };
                    }

                    return new
                    {
                        IsValid = false,
                        SchemeCode = 0,
                        Fund = fund
                    };
                })
                .Where(x => x.IsValid)
                .GroupBy(x => x.SchemeCode)
                .ToDictionary(
                    x => x.Key,
                    x => x.First().Fund);

        var invalidSchemeCodes =
            funds.Count -
            fundMetadataBySchemeCode.Count;

        if (invalidSchemeCodes > 0)
        {
            _logger.LogWarning(
                "{Count} funds in funds.json had invalid SchemeCodes.",
                invalidSchemeCodes);
        }

        // Load only existing analytics records.
        //
        // IMPORTANT:
        // We are NOT loading NAV/return/CAGR data into a new object.
        // We are only updating metadata fields on existing records.
        var existingAnalytics =
            await _dbContext.FundAnalytics
                .ToListAsync(cancellationToken);

        var existingBySchemeCode =
            existingAnalytics
                .ToDictionary(
                    x => x.SchemeCode);

        var result =
            new FundMetadataSyncResult
            {
                Processed = fundMetadataBySchemeCode.Count,
                InvalidSchemeCodes = invalidSchemeCodes
            };

        foreach (var item in fundMetadataBySchemeCode)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var schemeCode =
                item.Key;

            var fund =
                item.Value;

            if (!existingBySchemeCode.TryGetValue(
                    schemeCode,
                    out var existing))
            {
                // This fund exists in funds.json but does not yet
                // have analytics in the database.
                //
                // We do NOT create a FundAnalytics record here because
                // that would create a record without NAV/return/CAGR data.
                result.MissingInDatabase++;

                continue;
            }

            result.Matched++;

            var changed = false;

            // --------------------------------------------------------
            // Metadata fields ONLY
            // --------------------------------------------------------

            if (!string.Equals(
                    existing.FundHouse,
                    fund.FundHouse,
                    StringComparison.Ordinal))
            {
                existing.FundHouse =
                    fund.FundHouse;

                changed = true;
            }

            if (!string.Equals(
                    existing.SchemeName,
                    fund.SchemeName,
                    StringComparison.Ordinal))
            {
                existing.SchemeName =
                    fund.SchemeName;

                changed = true;
            }

            if (!string.Equals(
                    existing.Plan,
                    fund.Plan,
                    StringComparison.Ordinal))
            {
                existing.Plan =
                    fund.Plan;

                changed = true;
            }

            if (!string.Equals(
                    existing.Option,
                    fund.Option,
                    StringComparison.Ordinal))
            {
                existing.Option =
                    fund.Option;

                changed = true;
            }

            if (!string.Equals(
                    existing.Category,
                    fund.Category,
                    StringComparison.Ordinal))
            {
                existing.Category =
                    fund.Category;

                changed = true;
            }

            if (!string.Equals(
                    existing.SubCategory,
                    fund.SubCategory,
                    StringComparison.Ordinal))
            {
                existing.SubCategory =
                    fund.SubCategory;

                changed = true;
            }

            if (changed)
            {
                result.Updated++;
            }
            else
            {
                result.Unchanged++;
            }
        }

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        _dbContext.ChangeTracker.Clear();

        _logger.LogInformation(
            "Fund metadata synchronization completed. " +
            "Processed: {Processed}, Matched: {Matched}, Updated: {Updated}, " +
            "Unchanged: {Unchanged}, MissingInDatabase: {MissingInDatabase}, " +
            "InvalidSchemeCodes: {InvalidSchemeCodes}",
            result.Processed,
            result.Matched,
            result.Updated,
            result.Unchanged,
            result.MissingInDatabase,
            result.InvalidSchemeCodes);

        return result;
    }

    // ============================================================
    // MFAPI ANALYTICS FETCH
    // ============================================================

    private async Task<FundProcessingResult> FetchAnalyticsAsync(
        FundRecord fund,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(
                fund.SchemeCode,
                out var schemeCode))
        {
            _logger.LogWarning(
                "Invalid scheme code {SchemeCode}.",
                fund.SchemeCode);

            return new FundProcessingResult(
                fund.SchemeCode,
                null,
                "Invalid scheme code.",
                true);
        }

        await semaphore.WaitAsync(
            cancellationToken);

        try
        {
            var mfApiResult =
                await _mfApiService.GetFundDataWithReasonAsync(
                    schemeCode,
                    cancellationToken);

            if (mfApiResult.Data == null)
            {
                return new FundProcessingResult(
                    fund.SchemeCode,
                    null,
                    mfApiResult.FailureReason ??
                    "MFAPI returned no usable fund data.",
                    mfApiResult.IsPermanentFailure);
            }

            var navData =
                mfApiResult.Data;

            if (navData.NavHistory.Count == 0)
            {
                return new FundProcessingResult(
                    fund.SchemeCode,
                    null,
                    "MFAPI returned no usable NAV history.",
                    true);
            }

            var analytics =
                BuildAnalytics(
                    fund,
                    schemeCode,
                    navData);

            return new FundProcessingResult(
                fund.SchemeCode,
                analytics,
                null,
                false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to fetch analytics for scheme {SchemeCode}.",
                schemeCode);

            return new FundProcessingResult(
                fund.SchemeCode,
                null,
                $"Analytics error: {ex.Message}",
                false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    // ============================================================
    // LOAD FUNDS.JSON
    // ============================================================

    private async Task<List<FundRecord>> LoadFundsAsync(
        CancellationToken cancellationToken)
    {
        var dataDirectory =
            _configuration["DataDirectory"];

        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            var projectRoot =
                Directory.GetParent(AppContext.BaseDirectory)!
                    .Parent!
                    .Parent!
                    .Parent!
                    .Parent!
                    .FullName;

            dataDirectory =
                Path.Combine(
                    projectRoot,
                    "Data");
        }

        var filePath =
            Path.Combine(
                dataDirectory,
                "funds.json");

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                "funds.json was not found.",
                filePath);
        }

        await using var stream =
            File.OpenRead(filePath);

        var funds =
            await JsonSerializer.DeserializeAsync<List<FundRecord>>(
                stream,
                cancellationToken: cancellationToken);

        return funds ??
               new List<FundRecord>();
    }

    // ============================================================
    // BUILD ANALYTICS
    // ============================================================

    private static FundAnalytics BuildAnalytics(
        FundRecord fund,
        int schemeCode,
        MutualFundResponse navData)
    {
        var orderedHistory =
            navData.NavHistory
                .OrderByDescending(x => x.Date)
                .ToList();

        if (orderedHistory.Count == 0)
        {
            throw new InvalidOperationException(
                $"No NAV history available for scheme {schemeCode}.");
        }

        var latest =
            orderedHistory[0];

        return new FundAnalytics
        {
            SchemeCode = schemeCode,

            FundHouse = fund.FundHouse,
            SchemeName = fund.SchemeName,
            Plan = fund.Plan,
            Option = fund.Option,
            Category = fund.Category,
            SubCategory = fund.SubCategory,

            LastNavDate =
                DateOnly.FromDateTime(
                    latest.Date),

            CurrentNAV =
                latest.Nav,

            Return1Month =
                CalculateReturn(
                    latest,
                    orderedHistory,
                    1),

            Return3Month =
                CalculateReturn(
                    latest,
                    orderedHistory,
                    3),

            Return6Month =
                CalculateReturn(
                    latest,
                    orderedHistory,
                    6),

            CAGR1Year =
                CalculateCagr(
                    latest,
                    orderedHistory,
                    1),

            CAGR3Year =
                CalculateCagr(
                    latest,
                    orderedHistory,
                    3),

            CAGR5Year =
                CalculateCagr(
                    latest,
                    orderedHistory,
                    5),

            CAGR10Year =
                CalculateCagr(
                    latest,
                    orderedHistory,
                    10),

            AnalyticsDate =
                DateTime.UtcNow,

            DataAsOfDate =
                DateOnly.FromDateTime(
                    latest.Date)
        };
    }

    // ============================================================
    // RETURN / CAGR CALCULATIONS
    // ============================================================

    private static decimal? CalculateReturn(
        NavDataPoint latest,
        List<NavDataPoint> history,
        int months)
    {
        var targetDate =
            latest.Date.AddMonths(-months);

        var historical =
            FindHistoricalNav(
                history,
                targetDate);

        if (historical == null ||
            historical.Nav <= 0)
        {
            return null;
        }

        return
            ((latest.Nav / historical.Nav) - 1m) *
            100m;
    }

    private static decimal? CalculateCagr(
        NavDataPoint latest,
        List<NavDataPoint> history,
        int years)
    {
        var targetDate =
            latest.Date.AddYears(-years);

        var historical =
            FindHistoricalNav(
                history,
                targetDate);

        if (historical == null ||
            historical.Nav <= 0)
        {
            return null;
        }

        var ratio =
            (double)(
                latest.Nav /
                historical.Nav);

        var cagr =
            (Math.Pow(
                ratio,
                1.0 / years) - 1.0) *
            100.0;

        return (decimal)cagr;
    }

    private static NavDataPoint? FindHistoricalNav(
        List<NavDataPoint> history,
        DateTime targetDate)
    {
        return history
            .Where(x => x.Date <= targetDate)
            .OrderByDescending(x => x.Date)
            .FirstOrDefault();
    }

    // ============================================================
    // ANALYTICS UPSERT
    // ============================================================

    private async Task UpsertAsync(
        FundAnalytics analytics,
        CancellationToken cancellationToken)
    {
        var existing =
            await _dbContext.FundAnalytics
                .FirstOrDefaultAsync(
                    x => x.SchemeCode ==
                         analytics.SchemeCode,
                    cancellationToken);

        if (existing == null)
        {
            _dbContext.FundAnalytics.Add(
                analytics);

            return;
        }

        existing.FundHouse =
            analytics.FundHouse;

        existing.SchemeName =
            analytics.SchemeName;

        existing.Plan =
            analytics.Plan;

        existing.Option =
            analytics.Option;

        existing.Category =
            analytics.Category;

        existing.SubCategory =
            analytics.SubCategory;

        existing.LastNavDate =
            analytics.LastNavDate;

        existing.CurrentNAV =
            analytics.CurrentNAV;

        existing.Return1Month =
            analytics.Return1Month;

        existing.Return3Month =
            analytics.Return3Month;

        existing.Return6Month =
            analytics.Return6Month;

        existing.CAGR1Year =
            analytics.CAGR1Year;

        existing.CAGR3Year =
            analytics.CAGR3Year;

        existing.CAGR5Year =
            analytics.CAGR5Year;

        existing.CAGR10Year =
            analytics.CAGR10Year;

        existing.AnalyticsDate =
            analytics.AnalyticsDate;

        existing.DataAsOfDate =
            analytics.DataAsOfDate;
    }

    // ============================================================
    // FAILURE HANDLING
    // ============================================================

    private async Task UpsertFailureAsync(
        string schemeCode,
        string failureReason,
        bool isPermanent,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(
                schemeCode,
                out var parsedSchemeCode))
        {
            return;
        }

        var existing =
            await _dbContext.FundAnalyticsFailures
                .FirstOrDefaultAsync(
                    x => x.SchemeCode ==
                         parsedSchemeCode,
                    cancellationToken);

        if (existing == null)
        {
            _dbContext.FundAnalyticsFailures.Add(
                new FundAnalyticsFailure
                {
                    SchemeCode = parsedSchemeCode,
                    FailureReason = failureReason,
                    IsPermanent = isPermanent,
                    LastAttemptedAt = DateTime.UtcNow
                });

            return;
        }

        existing.FailureReason =
            failureReason;

        existing.IsPermanent =
            isPermanent;

        existing.LastAttemptedAt =
            DateTime.UtcNow;
    }

    private async Task RemoveFailureAsync(
        string schemeCode,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(
                schemeCode,
                out var parsedSchemeCode))
        {
            return;
        }

        var existing =
            await _dbContext.FundAnalyticsFailures
                .FirstOrDefaultAsync(
                    x => x.SchemeCode ==
                         parsedSchemeCode,
                    cancellationToken);

        if (existing != null)
        {
            _dbContext.FundAnalyticsFailures.Remove(
                existing);
        }
    }

    // ============================================================
    // LOGGING
    // ============================================================

    private void LogProgress(
        UpdateResult result,
        int total)
    {
        _logger.LogInformation(
            "Progress: {Processed}/{Total}. " +
            "Succeeded: {Succeeded}, Failed: {Failed}",
            result.Processed,
            total,
            result.Succeeded,
            result.Failed);
    }

    // ============================================================
    // INTERNAL RESULT TYPES
    // ============================================================

    private sealed class FundProcessingResult
    {
        public string SchemeCode { get; }

        public FundAnalytics? Analytics { get; }

        public string? FailureReason { get; }

        public bool IsPermanentFailure { get; }

        public FundProcessingResult(
            string schemeCode,
            FundAnalytics? analytics,
            string? failureReason,
            bool isPermanentFailure)
        {
            SchemeCode = schemeCode;
            Analytics = analytics;
            FailureReason = failureReason;
            IsPermanentFailure = isPermanentFailure;
        }
    }

    public sealed class FailedFund
    {
        public string SchemeCode { get; }

        public string Reason { get; }

        public FailedFund(
            string schemeCode,
            string reason)
        {
            SchemeCode = schemeCode;
            Reason = reason;
        }
    }

    public sealed class FundMetadataSyncResult
    {
        public int Processed { get; set; }

        public int Matched { get; set; }

        public int Updated { get; set; }

        public int Unchanged { get; set; }

        public int MissingInDatabase { get; set; }

        public int InvalidSchemeCodes { get; set; }
    }

    private sealed class FundRecord
    {
        public string SchemeCode { get; set; } =
            string.Empty;

        public string FundHouse { get; set; } =
            string.Empty;

        public string SchemeName { get; set; } =
            string.Empty;

        public string Plan { get; set; } =
            string.Empty;

        public string Option { get; set; } =
            string.Empty;

        public string Category { get; set; } =
            string.Empty;

        public string SubCategory { get; set; } =
            string.Empty;

        public string LastNavDate { get; set; } =
            string.Empty;
    }

    public sealed class UpdateResult
    {
        public int Processed { get; set; }

        public int Succeeded { get; set; }

        public int Failed { get; set; }

        public List<FailedFund> FailedFunds { get; set; } =
            new();
    }
}