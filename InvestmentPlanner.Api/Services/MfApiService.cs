using System.Globalization;
using System.Net.Http.Json;
using InvestmentPlanner.Api.Models;

namespace InvestmentPlanner.Api.Services;

public class MfApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MfApiService> _logger;

    private const string MfApiDateFormat = "dd-MM-yyyy";

    public MfApiService(
        HttpClient httpClient,
        ILogger<MfApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Gets fund data from MFAPI.
    /// This method is kept for existing callers that only need
    /// the parsed MutualFundResponse.
    /// </summary>
    public async Task<MutualFundResponse?> GetFundDataAsync(
        int schemeCode,
        CancellationToken cancellationToken = default)
    {
        var result =
            await GetFundDataWithReasonAsync(
                schemeCode,
                cancellationToken);

        return result.Data;
    }

    /// <summary>
    /// Gets fund data from MFAPI and provides additional information
    /// when usable NAV data cannot be produced.
    /// </summary>
    public async Task<MfApiFundResult> GetFundDataWithReasonAsync(
        int schemeCode,
        CancellationToken cancellationToken = default)
    {
        if (schemeCode <= 0)
        {
            return new MfApiFundResult(
                null,
                "Invalid scheme code.",
                true);
        }

        try
        {
            var response =
                await _httpClient.GetAsync(
                    $"mf/{schemeCode}",
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "MFAPI returned HTTP {StatusCode} for scheme {SchemeCode}.",
                    (int)response.StatusCode,
                    schemeCode);

                var isPermanent =
                    response.StatusCode ==
                    System.Net.HttpStatusCode.NotFound;

                return new MfApiFundResult(
                    null,
                    $"MFAPI returned HTTP {(int)response.StatusCode}.",
                    isPermanent);
            }

            var raw =
                await response.Content.ReadFromJsonAsync<NavApiResponse>(
                    cancellationToken: cancellationToken);

            if (raw?.Meta == null)
            {
                _logger.LogWarning(
                    "MFAPI returned no metadata for scheme {SchemeCode}.",
                    schemeCode);

                return new MfApiFundResult(
                    null,
                    "MFAPI response did not contain fund metadata.",
                    true);
            }

            if (raw.Data == null || raw.Data.Count == 0)
            {
                _logger.LogWarning(
                    "MFAPI returned no NAV records for scheme {SchemeCode}.",
                    schemeCode);

                return new MfApiFundResult(
                    null,
                    "MFAPI returned no NAV records.",
                    true);
            }

            var navHistory =
                new List<NavDataPoint>();

            var zeroOrInvalidNavCount = 0;

            foreach (var point in raw.Data)
            {
                if (string.IsNullOrWhiteSpace(point.Date) ||
                    string.IsNullOrWhiteSpace(point.Nav))
                {
                    zeroOrInvalidNavCount++;
                    continue;
                }

                if (!DateTime.TryParseExact(
                        point.Date,
                        MfApiDateFormat,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var parsedDate))
                {
                    zeroOrInvalidNavCount++;
                    continue;
                }

                if (!decimal.TryParse(
                        point.Nav,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var parsedNav) ||
                    parsedNav <= 0)
                {
                    zeroOrInvalidNavCount++;
                    continue;
                }

                navHistory.Add(
                    new NavDataPoint
                    {
                        Date = parsedDate,
                        Nav = parsedNav
                    });
            }

            if (navHistory.Count == 0)
            {
                _logger.LogWarning(
                    "MFAPI returned NAV records for scheme {SchemeCode}, " +
                    "but none contained a usable NAV value. " +
                    "Invalid/zero records: {InvalidCount}.",
                    schemeCode,
                    zeroOrInvalidNavCount);

                return new MfApiFundResult(
                    null,
                    "MFAPI returned NAV data, but all NAV values were zero or invalid.",
                    true);
            }

            navHistory =
                navHistory
                    .OrderByDescending(x => x.Date)
                    .ToList();

            var result =
                new MutualFundResponse
                {
                    SchemeCode = schemeCode,

                    SchemeName =
                        raw.Meta.SchemeName
                        ?? $"Scheme {schemeCode}",

                    FundHouse =
                        raw.Meta.FundHouse,

                    SchemeType =
                        raw.Meta.SchemeType,

                    SchemeCategory =
                        raw.Meta.SchemeCategory,

                    NavHistory =
                        navHistory
                };

            return new MfApiFundResult(
                result,
                null,
                false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(
                ex,
                "MFAPI request timed out for scheme {SchemeCode}.",
                schemeCode);

            return new MfApiFundResult(
                null,
                "MFAPI request timed out.",
                false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "MFAPI request failed for scheme {SchemeCode}.",
                schemeCode);

            return new MfApiFundResult(
                null,
                "MFAPI request failed.",
                false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error while fetching MFAPI data for scheme {SchemeCode}.",
                schemeCode);

            return new MfApiFundResult(
                null,
                "Unexpected error while processing MFAPI response.",
                false);
        }
    }

    public sealed class MfApiFundResult
    {
        public MutualFundResponse? Data { get; }

        public string? FailureReason { get; }

        public bool IsPermanentFailure { get; }

        public MfApiFundResult(
            MutualFundResponse? data,
            string? failureReason,
            bool isPermanentFailure)
        {
            Data = data;
            FailureReason = failureReason;
            IsPermanentFailure = isPermanentFailure;
        }
    }
}