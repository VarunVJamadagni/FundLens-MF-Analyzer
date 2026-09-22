using InvestmentPlanner.Api.Models;
using InvestmentPlanner.Api.Services;
using InvestmentPlanner.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentPlanner.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SchemeController : ControllerBase
    {
        private readonly SchemeService _schemeService;
        private readonly ILogger<SchemeController> _logger;

        public SchemeController(SchemeService schemeService, ILogger<SchemeController> logger)
        {
            _schemeService = schemeService;
            _logger = logger;
        }

        /// <summary>
        /// GET /api/Scheme/search?query={query}
        /// Always hits MFAPI's live search endpoint - never restricted to a
        /// hardcoded list of fund houses. Funds lacking long-term history are
        /// still returned (with "N/A" for those periods).
        /// </summary>
        [HttpGet("search")]
        public async Task<ActionResult<List<AnalyticsApiResponse>>> Search([FromQuery] string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Ok(new List<AnalyticsApiResponse>());
            }

            try
            {
                var results = await _schemeService.SearchAsync(query.Trim());
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching schemes for query '{Query}'", query);
                return StatusCode(500, new { message = "Unable to search funds at this time. Please try again later." });
            }
        }

        /// <summary>
/// GET /api/Scheme/filter-options
/// Returns all available filter values from the local AMFI catalogue.
/// </summary>
[HttpGet("filter-options")]
public async Task<ActionResult<FilterOptions>> FilterOptions()
{
    try
    {
        var options = await _schemeService.GetFilterOptionsAsync();

        return Ok(options);
    }
    catch (Exception ex)
    {
        _logger.LogError(
            ex,
            "Error fetching filter options");

        return StatusCode(
            500,
            new
            {
                message = "Unable to retrieve filter options."
            });
    }
}

/// <summary>
/// POST /api/Scheme/filter
/// Filters funds from the local AMFI catalogue.
/// </summary>
[HttpPost("filter")]
public async Task<ActionResult<List<AnalyticsApiResponse>>> Filter(
    [FromBody] FundFilterRequest request)
{
    try
    {
        var results =
            await _schemeService.FilterFundsAsync(request);

        return Ok(results);
    }
    catch (Exception ex)
    {
        _logger.LogError(
            ex,
            "Error filtering funds");

        return StatusCode(
            500,
            new
            {
                message = "Unable to filter funds at this time."
            });
    }
}

        /// <summary>
        /// GET /api/Scheme/eligible
        /// Returns a small set of funds that have every required analytics
        /// period available. Never hardcoded - discovered dynamically.
        /// </summary>
        [HttpGet("eligible")]
        public async Task<ActionResult<List<AnalyticsApiResponse>>> Eligible()
        {
            try
            {
                var results = await _schemeService.GetEligibleFundsAsync();
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching eligible funds");
                return StatusCode(500, new { message = "Unable to retrieve eligible funds at this time. Please try again later." });
            }
        }

        [HttpGet("top-performing")]
public async Task<ActionResult<TopPerformingFundsResponse>>
    TopPerforming()
{
    try
    {
        var results =
            await _schemeService
                .GetTopPerformingFundsAsync();

        return Ok(results);
    }
    catch (Exception ex)
    {
        _logger.LogError(
            ex,
            "Error fetching top-performing funds");

        return StatusCode(
            500,
            new
            {
                message =
                    "Unable to retrieve top-performing funds."
            });
    }
}
    }
}
