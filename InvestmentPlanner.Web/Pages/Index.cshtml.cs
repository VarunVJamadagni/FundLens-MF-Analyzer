using System.Net.Http.Json;
using InvestmentPlanner.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InvestmentPlanner.Web.Pages
{
    public class IndexModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(
            IHttpClientFactory httpClientFactory,
            ILogger<IndexModel> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public void OnGet()
        {
            // The landing page does not load eligible funds.
            // Funds are loaded only when the user searches
            // or applies filters.
        }

        /// <summary>
        /// Called via:
        /// GET ?handler=FilterOptions
        ///
        /// Returns all available filter values from the
        /// local AMFI-generated funds catalogue.
        /// </summary>
        public async Task<JsonResult> OnGetFilterOptionsAsync()
        {
            try
            {
                var client =
                    _httpClientFactory.CreateClient("InvestmentPlannerApi");

                var response =
                    await client.GetAsync("api/Scheme/filter-options");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Api returned {StatusCode} while fetching filter options",
                        response.StatusCode);

                    return new JsonResult(new FilterOptions());
                }

                var options =
                    await response.Content
                        .ReadFromJsonAsync<FilterOptions>();

                return new JsonResult(
                    options ?? new FilterOptions());
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error fetching filter options from Api");

                return new JsonResult(new FilterOptions());
            }
        }

        /// <summary>
        /// Called via:
        /// POST ?handler=Filter
        ///
        /// Forwards the selected filters to the API.
        /// </summary>
        public async Task<JsonResult> OnPostFilterAsync(
            [FromBody] FundFilterRequest request)
        {
            try
            {
                request ??= new FundFilterRequest();

                var client =
                    _httpClientFactory.CreateClient("InvestmentPlannerApi");

                var response =
                    await client.PostAsJsonAsync(
                        "api/Scheme/filter",
                        request);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Api returned {StatusCode} while filtering funds",
                        response.StatusCode);

                    return new JsonResult(new List<object>());
                }

                var funds = await response.Content.ReadFromJsonAsync<List<AnalyticsApiResponse>>();
return new JsonResult(funds ?? new List<AnalyticsApiResponse>());
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error filtering funds via Api");

                return new JsonResult(new List<object>());
            }
        }

        /// <summary>
        /// Existing search handler.
        ///
        /// This preserves the current search flow:
        /// GET ?handler=Search&query=...
        /// </summary>
        public async Task<JsonResult> OnGetSearchAsync(
            string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new JsonResult(new List<object>());
            }

            try
            {
                var client =
                    _httpClientFactory.CreateClient("InvestmentPlannerApi");

                var response =
                    await client.GetAsync(
                        $"api/Scheme/search?query={Uri.EscapeDataString(query)}");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Api returned {StatusCode} for search query '{Query}'",
                        response.StatusCode,
                        query);

                    return new JsonResult(new List<object>());
                }

                var funds =
                    await response.Content
                        .ReadFromJsonAsync<List<AnalyticsApiResponse>>();

                return new JsonResult(
                    funds ?? new List<AnalyticsApiResponse>());
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error searching funds via Api for query '{Query}'",
                    query);

                return new JsonResult(new List<object>());
            }
        }
    }
}