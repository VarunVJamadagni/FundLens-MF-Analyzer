using InvestmentPlanner.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InvestmentPlanner.Web.Pages
{
    public class IndexModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(IHttpClientFactory httpClientFactory, ILogger<IndexModel> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public void OnGet()
        {
            // No huge fund list on initial page load - the eligible list is
            // loaded on demand via OnGetEligibleAsync when the search box is focused.
        }

        /// <summary>
        /// Called via fetch('?handler=Eligible') when the user focuses the
        /// search box with an empty query. Forwards to GET /api/Scheme/eligible.
        /// </summary>
        public async Task<JsonResult> OnGetEligibleAsync()
        {
            try
            {
                var client = _httpClientFactory.CreateClient("InvestmentPlannerApi");
                var response = await client.GetAsync("api/Scheme/eligible");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Api returned {StatusCode} for eligible funds", response.StatusCode);
                    return new JsonResult(new List<object>());
                }

                var funds = await response.Content.ReadFromJsonAsync<List<AnalyticsApiResponse>>();
                return new JsonResult(funds ?? new List<AnalyticsApiResponse>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching eligible funds from Api");
                return new JsonResult(new List<object>());
            }
        }

        /// <summary>
        /// Called via fetch('?handler=Search&amp;query=...') as the user types.
        /// Forwards to GET /api/Scheme/search?query=... on the Api project.
        /// </summary>
        public async Task<JsonResult> OnGetSearchAsync(string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new JsonResult(new List<object>());
            }

            try
            {
                var client = _httpClientFactory.CreateClient("InvestmentPlannerApi");
                var response = await client.GetAsync($"api/Scheme/search?query={Uri.EscapeDataString(query)}");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Api returned {StatusCode} for search query '{Query}'", response.StatusCode, query);
                    return new JsonResult(new List<object>());
                }

                var funds = await response.Content.ReadFromJsonAsync<List<AnalyticsApiResponse>>();
                return new JsonResult(funds ?? new List<AnalyticsApiResponse>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching funds via Api for query '{Query}'", query);
                return new JsonResult(new List<object>());
            }
        }
    }
}
