using InvestmentPlanner.Api.Models;
using InvestmentPlanner.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentPlanner.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AnalyticsController : ControllerBase
    {
        private readonly AnalyticsService _analyticsService;
        private readonly ILogger<AnalyticsController> _logger;

        public AnalyticsController(AnalyticsService analyticsService, ILogger<AnalyticsController> logger)
        {
            _analyticsService = analyticsService;
            _logger = logger;
        }

        /// <summary>
        /// GET /api/Analytics/{schemeCode}
        /// Returns NAV + returns + CAGR for a single scheme. Missing periods
        /// are represented as "N/A" - this endpoint only fails (404/500)
        /// when the fund itself can't be found or the API is unavailable.
        /// </summary>
        [HttpGet("{schemeCode:int}")]
        public async Task<ActionResult<AnalyticsApiResponse>> GetAnalytics(int schemeCode)
        {
            if (schemeCode <= 0)
            {
                return BadRequest(new { message = "Invalid scheme code." });
            }

            try
            {
                var result = await _analyticsService.BuildAnalyticsAsync(schemeCode);

                if (result == null)
                {
                    return NotFound(new { message = "Fund not found or no NAV data available." });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching analytics for scheme {SchemeCode}", schemeCode);
                return StatusCode(500, new { message = "Unable to retrieve fund analytics at this time. Please try again later." });
            }
        }
    }
}
