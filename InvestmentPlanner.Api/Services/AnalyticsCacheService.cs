using System.Collections.Concurrent;
using InvestmentPlanner.Api.Models;

namespace InvestmentPlanner.Api.Services
{
    /// <summary>
    /// Provides application-wide caching and concurrency control for
    /// analytics requests.
    ///
    /// The service is registered as a singleton because the cache should
    /// be shared across requests within the running API instance.
    ///
    /// Each analytics calculation creates its own DI scope, preventing
    /// scoped services and DbContexts from being retained by the cache.
    /// </summary>
    public class AnalyticsCacheService
    {
        private const int MaxConcurrentAnalytics = 2;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AnalyticsCacheService> _logger;

        private readonly SemaphoreSlim _analyticsLock =
            new(MaxConcurrentAnalytics);

        private readonly ConcurrentDictionary<
            int,
            Lazy<Task<AnalyticsApiResponse?>>> _cache = new();

        public AnalyticsCacheService(
            IServiceScopeFactory scopeFactory,
            ILogger<AnalyticsCacheService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        /// <summary>
        /// Gets cached analytics for a scheme or calculates them once.
        ///
        /// Multiple callers requesting the same scheme share the same
        /// in-flight task.
        /// </summary>
        public async Task<AnalyticsApiResponse?> GetAnalyticsAsync(
            int schemeCode,
            string? schemeName = null)
        {
            if (schemeCode <= 0)
            {
                return null;
            }

            var lazyTask = _cache.GetOrAdd(
                schemeCode,
                _ => new Lazy<Task<AnalyticsApiResponse?>>(
                    () => BuildAnalyticsWithLimitAsync(
                        schemeCode,
                        schemeName),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            try
            {
                return await lazyTask.Value;
            }
            catch
            {
                // Remove only the failed task so a future request
                // can retry the analytics calculation.
                _cache.TryRemove(
                    schemeCode,
                    out _);

                throw;
            }
        }

        /// <summary>
        /// Clears the cached analytics for one scheme.
        /// </summary>
        public bool Remove(int schemeCode)
        {
            return _cache.TryRemove(
                schemeCode,
                out _);
        }

        /// <summary>
        /// Clears all cached analytics.
        /// </summary>
        public void Clear()
        {
            _cache.Clear();
        }

        /// <summary>
        /// Creates a fresh dependency-injection scope for the analytics
        /// operation and applies the global concurrency limit.
        /// </summary>
        private async Task<AnalyticsApiResponse?>
            BuildAnalyticsWithLimitAsync(
                int schemeCode,
                string? schemeName)
        {
            await _analyticsLock.WaitAsync();

            try
            {
                await using var scope =
                    _scopeFactory.CreateAsyncScope();

                var analyticsService =
                    scope.ServiceProvider
                        .GetRequiredService<AnalyticsService>();

                return await analyticsService.BuildAnalyticsAsync(
                    schemeCode,
                    schemeName);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error calculating analytics for scheme {SchemeCode}",
                    schemeCode);

                throw;
            }
            finally
            {
                _analyticsLock.Release();
            }
        }
    }
}