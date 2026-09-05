using InvestmentPlanner.Api.Models;

namespace InvestmentPlanner.Api.Services
{
    /// <summary>
    /// Thin wrapper around NAVService for callers that just want a fund's
    /// raw name/NAV-history details (e.g. a lightweight "track this scheme
    /// code" lookup) without the full analytics calculation.
    /// </summary>
    public class MFTrackerService
    {
        private readonly NAVService _navService;

        public MFTrackerService(NAVService navService)
        {
            _navService = navService;
        }

        public Task<MutualFundResponse?> GetFundDetailsAsync(int schemeCode)
        {
            return _navService.GetFundDataAsync(schemeCode);
        }
    }
}
