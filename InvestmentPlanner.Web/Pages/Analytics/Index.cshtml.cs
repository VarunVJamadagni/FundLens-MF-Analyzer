using System.Net;
using InvestmentPlanner.Web.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InvestmentPlanner.Web.Pages.Analytics
{
    public class IndexModel : PageModel
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<IndexModel> _logger;

        public AnalyticsApiResponse? Fund { get; private set; }
        public string? ErrorMessage { get; private set; }

        public IndexModel(IHttpClientFactory httpClientFactory, ILogger<IndexModel> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task OnGetAsync(int? schemeCode)
        {
            if (schemeCode is null or <= 0)
            {
                ErrorMessage = "No fund was specified.";
                return;
            }

            try
            {
                var client = _httpClientFactory.CreateClient("InvestmentPlannerApi");
                var response = await client.GetAsync($"api/Analytics/{schemeCode}");

                if (response.IsSuccessStatusCode)
                {
                    Fund = await response.Content.ReadFromJsonAsync<AnalyticsApiResponse>();

                    if (Fund == null)
                    {
                        ErrorMessage = "Fund data could not be loaded.";
                    }
                }
                else if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    ErrorMessage = "This fund could not be found.";
                }
                else
                {
                    ErrorMessage = "Unable to load fund details at this time. Please try again later.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading analytics for scheme {SchemeCode}", schemeCode);
                ErrorMessage = "Unable to load fund details at this time. Please try again later.";
            }
        }
    }
}
