using Hangfire;
using Microsoft.Extensions.Logging;

namespace Morpara.Services
{
    /// <summary>
    /// Hangfire job that refreshes sitemap cache every 3 hours by calling the API endpoints
    /// Her 3 saatte bir API endpoint'lerini çağırarak sitemap cache'ini yeniler
    /// </summary>
    public class SitemapCacheRefreshJob
    {
        private readonly ILogger<SitemapCacheRefreshJob> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public SitemapCacheRefreshJob(
            ILogger<SitemapCacheRefreshJob> logger,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Refreshes all sitemap caches by calling the API endpoints
        /// API endpoint'lerini çağırarak tüm sitemap cache'lerini yeniler
        /// </summary>
        public async Task RefreshSitemapsAsync()
        {
            try
            {
                _logger.LogInformation("[SitemapRefreshJob] Starting automatic sitemap cache refresh...");

                var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromMinutes(5);

                // API base URL'i al (localhost veya production)
                var baseUrl = "http://localhost:5000"; // Default Umbraco port

                // XML sitemap'i yenile
                try
                {
                    _logger.LogInformation("[SitemapRefreshJob] Refreshing XML sitemap...");
                    var xmlResponse = await httpClient.GetAsync($"{baseUrl}/umbraco/api/ContentApi/sitemap");
                    
                    if (xmlResponse.IsSuccessStatusCode)
                    {
                        var xmlContent = await xmlResponse.Content.ReadAsStringAsync();
                        var urlCount = CountUrlsInXml(xmlContent);
                        _logger.LogInformation("[SitemapRefreshJob] XML sitemap refreshed successfully. URLs: {Count}", urlCount);
                    }
                    else
                    {
                        _logger.LogWarning("[SitemapRefreshJob] XML sitemap refresh failed. Status: {Status}", xmlResponse.StatusCode);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[SitemapRefreshJob] Error refreshing XML sitemap");
                }

                // JSON sitemap TR'yi yenile
                try
                {
                    _logger.LogInformation("[SitemapRefreshJob] Refreshing JSON sitemap (tr)...");
                    var trResponse = await httpClient.GetAsync($"{baseUrl}/umbraco/api/ContentApi/sitemap/json?lang=tr");
                    
                    if (trResponse.IsSuccessStatusCode)
                    {
                        var trContent = await trResponse.Content.ReadAsStringAsync();
                        var count = CountUrlsInJson(trContent);
                        _logger.LogInformation("[SitemapRefreshJob] JSON sitemap (tr) refreshed successfully. URLs: {Count}", count);
                    }
                    else
                    {
                        _logger.LogWarning("[SitemapRefreshJob] JSON sitemap (tr) refresh failed. Status: {Status}", trResponse.StatusCode);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[SitemapRefreshJob] Error refreshing JSON sitemap (tr)");
                }

                // JSON sitemap EN'i yenile
                try
                {
                    _logger.LogInformation("[SitemapRefreshJob] Refreshing JSON sitemap (en)...");
                    var enResponse = await httpClient.GetAsync($"{baseUrl}/umbraco/api/ContentApi/sitemap/json?lang=en");
                    
                    if (enResponse.IsSuccessStatusCode)
                    {
                        var enContent = await enResponse.Content.ReadAsStringAsync();
                        var count = CountUrlsInJson(enContent);
                        _logger.LogInformation("[SitemapRefreshJob] JSON sitemap (en) refreshed successfully. URLs: {Count}", count);
                    }
                    else
                    {
                        _logger.LogWarning("[SitemapRefreshJob] JSON sitemap (en) refresh failed. Status: {Status}", enResponse.StatusCode);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[SitemapRefreshJob] Error refreshing JSON sitemap (en)");
                }

                _logger.LogInformation("[SitemapRefreshJob] Sitemap cache refresh completed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SitemapRefreshJob] Unexpected error during sitemap refresh");
            }
        }

        private int CountUrlsInXml(string xml)
        {
            return System.Text.RegularExpressions.Regex.Matches(xml, "<url>").Count;
        }

        private int CountUrlsInJson(string json)
        {
            return System.Text.RegularExpressions.Regex.Matches(json, "\"url\"").Count;
        }

        /// <summary>
        /// Registers the recurring job in Hangfire
        /// Hangfire'da tekrarlayan job'u kaydeder
        /// </summary>
        public static void ScheduleRecurringJob()
        {
            // Her 3 saatte bir çalış (00:00, 03:00, 06:00, 09:00, 12:00, 15:00, 18:00, 21:00)
            RecurringJob.AddOrUpdate<SitemapCacheRefreshJob>(
                "sitemap-cache-refresh",
                job => job.RefreshSitemapsAsync(),
                "0 */3 * * *", // Her 3 saatte bir (cron expression)
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                });
        }
    }
}
