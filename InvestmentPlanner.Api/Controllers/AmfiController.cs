using InvestmentPlanner.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentPlanner.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AmfiController : ControllerBase
{
    private readonly AmfiFundDataService _amfiFundDataService;

    public AmfiController(AmfiFundDataService amfiFundDataService)
    {
        _amfiFundDataService = amfiFundDataService;
    }

    [HttpPost("generate")]
    public async Task<IActionResult> GenerateFundsJson()
    {
        await _amfiFundDataService.GenerateFundsJsonAsync();

        return Ok(new
        {
            message = "funds.json generated successfully."
        });
    }
}