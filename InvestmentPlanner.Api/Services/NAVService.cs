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
    /// </summary>
    public class NAVService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<NAVService> _logger;

        // MFAPI dates look like "04-08-2026" (dd-MM-yyyy).
        private const string MfApiDateFormat = "dd-MM-yyyy";

        public NAVService(HttpClient httpClient, ILogger<NAVService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        /// <summary>
        /// Fetches scheme details + full NAV history for a scheme code.
        /// Returns null if the scheme code is invalid, the fund can't be
        /// found, the API fails, or no usable NAV data exists.
        /// </summary>
        public async Task<MutualFundResponse?> GetFundDataAsync(int schemeCode)
        {
            if (schemeCode <= 0)
            {
                return null;
            }

            NavApiResponse? raw;

            try
            {
                var response = await _httpClient.GetAsync($"mf/{schemeCode}");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "MFAPI returned {StatusCode} for scheme {SchemeCode}",
                        response.StatusCode, schemeCode);
                    return null;
                }

                raw = await response.Content.ReadFromJsonAsync<NavApiResponse>();
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "MFAPI request timed out for scheme {SchemeCode}", schemeCode);
                return null;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "MFAPI request failed for scheme {SchemeCode}", schemeCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error fetching scheme {SchemeCode} from MFAPI", schemeCode);
                return null;
            }

            if (raw?.Meta == null || raw.Data == null)
            {
                return null;
            }

            var navHistory = new List<NavDataPoint>();

            foreach (var point in raw.Data)
            {
                if (string.IsNullOrWhiteSpace(point.Date) || string.IsNullOrWhiteSpace(point.Nav))
                {
                    continue; // skip malformed entry rather than failing the whole request
                }

                if (!DateTime.TryParseExact(
                        point.Date,
                        MfApiDateFormat,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var parsedDate))
                {
                    continue; // malformed date - skip
                }

                if (!decimal.TryParse(
                        point.Nav,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var parsedNav) || parsedNav <= 0)
                {
                    continue; // malformed / non-positive NAV - skip
                }

                navHistory.Add(new NavDataPoint { Date = parsedDate, Nav = parsedNav });
            }

            // Most recent first, so callers can always take .First() for "current".
            navHistory = navHistory.OrderByDescending(n => n.Date).ToList();

            return new MutualFundResponse
            {
                SchemeCode = schemeCode,
                SchemeName = raw.Meta.SchemeName ?? $"Scheme {schemeCode}",
                FundHouse = raw.Meta.FundHouse,
                SchemeType = raw.Meta.SchemeType,
                SchemeCategory = raw.Meta.SchemeCategory,
                NavHistory = navHistory
            };
        }
    }
}
