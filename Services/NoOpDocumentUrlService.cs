using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Routing;

namespace Morpara.Services
{
    /// <summary>
    /// ⚡ PERFORMANCE FIX: No-op implementation of IDocumentUrlService
    /// Umbraco'nun DocumentUrlService'i SqlBulkCopy ile binlerce URL segment insert ediyor (30+ saniye timeout)
    /// Form response'lar için URL tracking'e gerek yok, bu servis hiçbir şey yapmıyor.
    /// </summary>
    public class NoOpDocumentUrlService : IDocumentUrlService
    {
        public Task<IEnumerable<UrlInfo>> GetContentUrlsAsync(Guid contentId)
        {
            // Boş liste döndür - URL tracking yok
            return Task.FromResult(Enumerable.Empty<UrlInfo>());
        }

        public Task<UrlInfo?> GetDocumentUrlAsync(Guid contentKey)
        {
            // Null döndür - URL tracking yok
            return Task.FromResult<UrlInfo?>(null);
        }

        public Task InitAsync(bool isRestarting, CancellationToken cancellationToken)
        {
            // Hiçbir şey yapma - initialization yok
            return Task.CompletedTask;
        }

        public Task<bool> HasPathBeenPublishedAsync(string path)
        {
            // Her zaman false döndür - path tracking yok
            return Task.FromResult(false);
        }

        public Task<UrlInfo?> GetUrlInfoAsync(Guid contentKey, string culture)
        {
            // Null döndür - URL tracking yok
            return Task.FromResult<UrlInfo?>(null);
        }

        public void CreateOrUpdateUrlSegments(IContent content)
        {
            // ⚡ PERFORMANCE FIX: Hiçbir şey yapma!
            // Bu metod DocumentUrlRepository.Save() → SqlBulkCopy çağırıp 30+ saniye timeout veriyordu
            // Artık hiçbir şey yapmıyor
        }

        public Task CreateOrUpdateUrlSegmentsAsync(Guid key)
        {
            // ⚡ PERFORMANCE FIX: Hiçbir şey yapma!
            return Task.CompletedTask;
        }

        public Task CreateOrUpdateUrlSegmentsAsync(IEnumerable<IContent> content)
        {
            // ⚡ PERFORMANCE FIX: Hiçbir şey yapma!
            return Task.CompletedTask;
        }

        public Guid? GetDocumentKeyByRoute(string route, string? culture = null, int? documentStartNodeId = null, bool hideTopLevelNode = false)
        {
            // Null döndür - route tracking yok
            return null;
        }

        public Task RebuildAllUrlsAsync()
        {
            // Hiçbir şey yapma - rebuild yok
            return Task.CompletedTask;
        }

        public string? GetUrlSegment(Guid contentKey, string? culture = null, bool published = true)
        {
            // Null döndür - segment tracking yok
            return null;
        }

        public Task CreateOrUpdateUrlSegmentsWithDescendantsAsync(Guid key)
        {
            // ⚡ PERFORMANCE FIX: Hiçbir şey yapma!
            return Task.CompletedTask;
        }

        public Task DeleteUrlsFromCacheAsync(IEnumerable<Guid> contentKeys)
        {
            // Hiçbir şey yapma - cache yok
            return Task.CompletedTask;
        }

        public Task<IEnumerable<UrlInfo>> ListUrlsAsync(Guid contentKey)
        {
            // Boş liste döndür - URL tracking yok
            return Task.FromResult(Enumerable.Empty<UrlInfo>());
        }

        public string? GetLegacyRouteFormat(Guid contentKey, string? culture = null, bool published = true)
        {
            // Null döndür - legacy route tracking yok
            return null;
        }

        public bool HasAny()
        {
            // Her zaman false döndür - hiç URL yok
            return false;
        }
    }
}
