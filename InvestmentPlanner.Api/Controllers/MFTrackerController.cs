using InvestmentPlanner.Api.Models;
using InvestmentPlanner.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentPlanner.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MFTrackerController : ControllerBase
    {
        private readonly MFTrackerService _mfTrackerService;
        private readonly ILogger<MFTrackerController> _logger;

        public MFTrackerController(MFTrackerService mfTrackerService, ILogger<MFTrackerController> logger)
        {
            _mfTrackerService = mfTrackerService;
            _logger = logger;
        }

        /// <summary>
        /// Returns raw scheme details (name, meta, full NAV history) for a
        /// given scheme code, without computed returns/CAGR.
        /// GET /api/MFTracker/{schemeCode}
        /// </summary>
        [HttpGet("{schemeCode:int}")]
        public async Task<ActionResult<MutualFundResponse>> Get(int schemeCode)
        {
            if (schemeCode <= 0)
            {
                return BadRequest(new { message = "Invalid scheme code." });
            }

            try
            {
                var result = await _mfTrackerService.GetFundDetailsAsync(schemeCode);

                if (result == null)
                {
                    return NotFound(new { message = "Fund not found or no NAV data available." });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching MF tracker details for scheme {SchemeCode}", schemeCode);
                return StatusCode(500, new { message = "Unable to retrieve fund details at this time." });
            }
        }
    }
}
