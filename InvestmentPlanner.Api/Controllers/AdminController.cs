using InvestmentPlanner.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentPlanner.Api.Controllers
{
    [ApiController]
    [Route("api/admin")]
    public class AdminController : ControllerBase
    {
        private readonly FundAnalyticsUpdateService _updateService;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            FundAnalyticsUpdateService updateService,
            ILogger<AdminController> logger)
        {
            _updateService = updateService;
            _logger = logger;
        }

        /// <summary>
        /// Updates analytics for new funds that are not already present
        /// in the FundAnalytics table.
        ///
        /// This endpoint can trigger MFAPI requests and analytics
        /// calculations for the requested number of funds.
        /// </summary>
        [HttpPost("update-funds")]
        public async Task<IActionResult> UpdateFunds(
            [FromQuery] int limit = 5,
            CancellationToken cancellationToken = default)
        {
            if (limit < 1 || limit > 500)
            {
                return BadRequest(new
                {
                    message = "Limit must be between 1 and 500."
                });
            }

            try
            {
                var result =
                    await _updateService.UpdateFundsAsync(
                        limit,
                        cancellationToken);

                return Ok(new
                {
                    message = "Fund analytics update completed.",
                    result.Processed,
                    result.Succeeded,
                    result.Failed,
                    result.FailedFunds
                });
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                return StatusCode(499, new
                {
                    message = "Fund analytics update was cancelled."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error during fund analytics update.");

                return StatusCode(500, new
                {
                    message =
                        "Unable to complete the fund analytics update."
                });
            }
        }

        /// <summary>
        /// Synchronizes fund metadata from funds.json into the existing
        /// FundAnalytics database records.
        ///
        /// IMPORTANT:
        /// This endpoint does NOT fetch MFAPI data and does NOT recalculate
        /// NAV, returns, or CAGR.
        /// </summary>
        [HttpPost("sync-fund-metadata")]
        public async Task<IActionResult> SyncFundMetadata(
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result =
                    await _updateService.SyncFundMetadataAsync(
                        cancellationToken);

                return Ok(new
                {
                    message =
                        "Fund metadata synchronization completed.",

                    result.Processed,
                    result.Matched,
                    result.Updated,
                    result.Unchanged,
                    result.MissingInDatabase,
                    result.InvalidSchemeCodes
                });
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                return StatusCode(499, new
                {
                    message =
                        "Fund metadata synchronization was cancelled."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error during fund metadata synchronization.");

                return StatusCode(500, new
                {
                    message =
                        "Unable to synchronize fund metadata."
                });
            }
        }
    }
}