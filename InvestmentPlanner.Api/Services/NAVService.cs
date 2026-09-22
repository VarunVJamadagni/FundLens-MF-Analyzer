using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using InvestmentPlanner.Api.Models;

namespace InvestmentPlanner.Api.Services
{
    /// <summary>
    /// Responsible for:
    ///   - calling MFAPI
    ///   - retrieving NAV history
    ///   - deserializing the response
    ///   - safely handling API failures
    ///   - caching NAV history in memory
    /// </summary>
    public class NAVService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<NAVService> _logger;

        private const string MfApiDateFormat = "dd-MM-yyyy";

        // Keeps NAV data in memory so repeated analytics
        // requests for the same scheme do not call MFAPI again.
        private static readonly ConcurrentDictionary<int, CachedNavData>
            NavCache = new();

        private static readonly TimeSpan CacheDuration =
            TimeSpan.FromHours(12);

        public NAVService(
            HttpClient httpClient,
            ILogger<NAVService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<MutualFundResponse?> GetFundDataAsync(
            int schemeCode)
        {
            if (schemeCode <= 0)
            {
                return null;
            }

            // ---------------------------------------------------------
            // CHECK CACHE
            // ---------------------------------------------------------

            if (NavCache.TryGetValue(
                    schemeCode,
                    out var cached))
            {
                if (DateTime.UtcNow - cached.CreatedAtUtc < CacheDuration)
                {
                    return cached.Data;
                }

                // Remove expired entry.
                NavCache.TryRemove(
                    schemeCode,
                    out _);
            }

            NavApiResponse? raw;

            try
            {
                var response =
                    await _httpClient.GetAsync(
                        $"mf/{schemeCode}");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "MFAPI returned {StatusCode} for scheme {SchemeCode}",
                        response.StatusCode,
                        schemeCode);

                    return null;
                }

                raw =
                    await response.Content
                        .ReadFromJsonAsync<NavApiResponse>();
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(
                    ex,
                    "MFAPI request timed out for scheme {SchemeCode}",
                    schemeCode);

                return null;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(
                    ex,
                    "MFAPI request failed for scheme {SchemeCode}",
                    schemeCode);

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected error fetching scheme {SchemeCode} from MFAPI",
                    schemeCode);

                return null;
            }

            if (raw?.Meta == null ||
                raw.Data == null)
            {
                return null;
            }

            var navHistory =
                new List<NavDataPoint>();

            foreach (var point in raw.Data)
            {
                if (string.IsNullOrWhiteSpace(point.Date) ||
                    string.IsNullOrWhiteSpace(point.Nav))
                {
                    continue;
                }

                if (!DateTime.TryParseExact(
                        point.Date,
                        MfApiDateFormat,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var parsedDate))
                {
                    continue;
                }

                if (!decimal.TryParse(
                        point.Nav,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var parsedNav) ||
                    parsedNav <= 0)
                {
                    continue;
                }

                navHistory.Add(
                    new NavDataPoint
                    {
                        Date = parsedDate,
                        Nav = parsedNav
                    });
            }

            navHistory =
                navHistory
                    .OrderByDescending(n => n.Date)
                    .ToList();

            var result =
                new MutualFundResponse
                {
                    SchemeCode = schemeCode,

                    SchemeName =
                        raw.Meta.SchemeName ??
                        $"Scheme {schemeCode}",

                    FundHouse =
                        raw.Meta.FundHouse,

                    SchemeType =
                        raw.Meta.SchemeType,

                    SchemeCategory =
                        raw.Meta.SchemeCategory,

                    NavHistory =
                        navHistory
                };

            // ---------------------------------------------------------
            // STORE IN CACHE
            // ---------------------------------------------------------

            NavCache[schemeCode] =
                new CachedNavData
                {
                    Data = result,
                    CreatedAtUtc = DateTime.UtcNow
                };

            return result;
        }

        private sealed class CachedNavData
        {
            public MutualFundResponse Data { get; init; } = null!;

            public DateTime CreatedAtUtc { get; init; }
        }
    }
}