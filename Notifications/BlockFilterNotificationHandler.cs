using kraftvaerk.umbraco.blockfilter.Backend.Notifications;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;

namespace Morpara.Notifications
{
    /// <summary>
    /// Kraftvaerk Block Filter paketi ile blok kataloðunu filtrelemek için handler
    /// Kullanýcý grubuna ve content type'a göre blok filtresi uygular
    /// </summary>
    public class BlockFilterNotificationHandler : INotificationAsyncHandler<RemodelBlockCatalogueNotification>
    {
        private readonly ILogger<BlockFilterNotificationHandler> _logger;

        public BlockFilterNotificationHandler(ILogger<BlockFilterNotificationHandler> logger)
        {
            _logger = logger;
        }

        public async Task HandleAsync(RemodelBlockCatalogueNotification notification, CancellationToken cancellationToken)
        {
            _logger.LogInformation("?? [BLOCK-FILTER] Blok kataloðu filtreleniyor - User: {UserName}, ContentType: {ContentType}, Available Blocks: {BlockCount}",
                notification.Model.User.Name,
                notification.Model.ContentTypeAlias,
                notification.Model.Blocks.Count);

            // ? ÖNEMLÝ: Önce global filtreleri uygula (tüm kullanýcýlar için)
            ApplyGlobalFilters(notification);
            
            // Blok filtrelerini uygula
            ApplyUserGroupFilters(notification);
            ApplyContentTypeFilters(notification);
            ApplySpecialFilters(notification);

            _logger.LogInformation("? [BLOCK-FILTER] Filtreleme tamamlandý - Kalan bloklar: {RemainingBlocks}",
                notification.Model.Blocks.Count);

            await Task.CompletedTask;
        }

        /// <summary>
        /// Tüm kullanýcýlar için uygulanan global filtreler (admin dahil)
        /// </summary>
        private void ApplyGlobalFilters(RemodelBlockCatalogueNotification notification)
        {
            // ?? Tüm kullanýcýlardan (admin dahil) gizlenecek bloklar
            var globallyHiddenBlocks = new List<string>
            {
                "basicContentBlock"  // Ana sayfa block grid'inde basicContentBlock hiç kimse göremesin
            };

            var initialBlockCount = notification.Model.Blocks.Count;

            notification.Model.Blocks = notification.Model.Blocks
                .Where(b => !globallyHiddenBlocks.Contains(b.Alias))
                .ToList();

            var filteredCount = initialBlockCount - notification.Model.Blocks.Count;

            if (filteredCount > 0)
            {
                _logger.LogInformation("?? [GLOBAL-FILTER] {FilteredCount} adet blok tüm kullanýcýlardan gizlendi: {HiddenBlocks}",
                    filteredCount, string.Join(", ", globallyHiddenBlocks));
            }
        }

        /// <summary>
        /// Kullanýcý grubuna göre blok filtreleri
        /// </summary>
        private void ApplyUserGroupFilters(RemodelBlockCatalogueNotification notification)
        {
            var user = notification.Model.User;
            var userGroups = user.Groups.Select(g => g.Name).ToList();

            _logger.LogDebug("?? [BLOCK-FILTER] Kullanýcý gruplarý: {UserGroups}", string.Join(", ", userGroups));

            // Admin olmayan kullanýcýlar için özel bloklarý gizle
            var adminOnlyBlocks = new List<string>
            {
                "staticPage",
                "staticSubPage",
                "questionsPage"
            };

            if (!userGroups.Contains("Administrators"))
            {
                notification.Model.Blocks = notification.Model.Blocks
                    .Where(b => !adminOnlyBlocks.Contains(b.Alias))
                    .ToList();

                _logger.LogDebug("?? [BLOCK-FILTER] Admin olmayan kullanýcý için {Count} adet admin bloðu gizlendi",
                    adminOnlyBlocks.Count);
            }

            // Editor kullanýcýlarý için geliþmiþ bloklarý gizle
            var advancedBlocks = new List<string>
            {
                "representationMap" // Sadece advanced editörler harita kullanabilir
            };

            if (!userGroups.Contains("Administrators") && !userGroups.Contains("Advanced Editors"))
            {
                notification.Model.Blocks = notification.Model.Blocks
                    .Where(b => !advancedBlocks.Contains(b.Alias))
                    .ToList();

                _logger.LogDebug("?? [BLOCK-FILTER] Temel kullanýcý için {Count} adet geliþmiþ blok gizlendi",
                    advancedBlocks.Count);
            }
        }

        /// <summary>
        /// Content type'a göre blok filtreleri
        /// </summary>
        private void ApplyContentTypeFilters(RemodelBlockCatalogueNotification notification)
        {
            var contentTypeAlias = notification.Model.ContentTypeAlias?.ToLowerInvariant();
            
            _logger.LogDebug("?? [BLOCK-FILTER] Content Type: {ContentType}", contentTypeAlias);

            switch (contentTypeAlias)
            {
                case "home":
                case "homepage":
                    // Ana sayfada sadece temel bloklar (basicContentBlock zaten global olarak gizlendi)
                    var homeAllowedBlocks = new List<string>
                    {
                        "representationList",
                        "representationMap",
                        "campingSlider",
                        "blogFeatured"
                    };

                    notification.Model.Blocks = notification.Model.Blocks
                        .Where(b => homeAllowedBlocks.Contains(b.Alias))
                        .ToList();

                    _logger.LogDebug("?? [BLOCK-FILTER] Ana sayfa için {Count} blok türü ile sýnýrlandý", homeAllowedBlocks.Count);
                    break;

                case "blogpage":
                case "blog":
                    // Blog sayfalarýnda blog ile ilgili bloklar
                    var blogAllowedBlocks = new List<string>
                    {
                        "blogList",
                        "blogFeatured"
                    };

                    notification.Model.Blocks = notification.Model.Blocks
                        .Where(b => blogAllowedBlocks.Contains(b.Alias))
                        .ToList();

                    _logger.LogDebug("?? [BLOCK-FILTER] Blog sayfasý için {Count} blok türü ile sýnýrlandý", blogAllowedBlocks.Count);
                    break;

                case "campaignpage":
                case "campaign":
                    // Kampanya sayfalarýnda kampanya ile ilgili bloklar
                    var campaignAllowedBlocks = new List<string>
                    {
                        "campingList",
                        "campingSlider"
                    };

                    notification.Model.Blocks = notification.Model.Blocks
                        .Where(b => campaignAllowedBlocks.Contains(b.Alias))
                        .ToList();

                    _logger.LogDebug("??? [BLOCK-FILTER] Kampanya sayfasý için {Count} blok türü ile sýnýrlandý", campaignAllowedBlocks.Count);
                    break;

                case "representationpage":
                case "representatives":
                    // Temsilcilik sayfalarýnda temsilcilik ile ilgili bloklar
                    var representationAllowedBlocks = new List<string>
                    {
                        "representationList",
                        "representationMap"
                    };

                    notification.Model.Blocks = notification.Model.Blocks
                        .Where(b => representationAllowedBlocks.Contains(b.Alias))
                        .ToList();

                    _logger.LogDebug("?? [BLOCK-FILTER] Temsilcilik sayfasý için {Count} blok türü ile sýnýrlandý", representationAllowedBlocks.Count);
                    break;

                case "questionpage":
                case "faq":
                    // Soru sayfalarýnda sadece soru bloklarý
                    var questionAllowedBlocks = new List<string>
                    {
                        "questionsPage"
                    };

                    notification.Model.Blocks = notification.Model.Blocks
                        .Where(b => questionAllowedBlocks.Contains(b.Alias))
                        .ToList();

                    _logger.LogDebug("? [BLOCK-FILTER] Soru sayfasý için {Count} blok türü ile sýnýrlandý", questionAllowedBlocks.Count);
                    break;
            }
        }

        /// <summary>
        /// Özel durum filtreleri
        /// </summary>
        private void ApplySpecialFilters(RemodelBlockCatalogueNotification notification)
        {
            // Münhasýr bloklar - eðer bunlar seçilirse diðerleri gizlenir
            var exclusiveBlocks = new List<string>
            {
                "questionsPage",
                "blogList",
                "staticPage",
                "staticSubPage"
            };

            // Münhasýr çoklu bloklar
            var exclusiveMultipleBlocks = new List<string>
            {
                "campingList"
            };

            // Eðer münhasýr bir blok mevcutsa, katalogda sadece o kategorideki bloklarý göster
            var currentContentBlocks = GetCurrentContentBlocks(notification);
            
            if (currentContentBlocks.Any())
            {
                var hasExclusiveBlock = currentContentBlocks.Any(block => 
                    exclusiveBlocks.Contains(block) || exclusiveMultipleBlocks.Contains(block));

                if (hasExclusiveBlock)
                {
                    var currentExclusiveBlocks = currentContentBlocks
                        .Where(block => exclusiveBlocks.Contains(block) || exclusiveMultipleBlocks.Contains(block))
                        .ToList();

                    if (currentExclusiveBlocks.Any())
                    {
                        // Sadece mevcut münhasýr blok türünü göster
                        notification.Model.Blocks = notification.Model.Blocks
                            .Where(b => currentExclusiveBlocks.Contains(b.Alias))
                            .ToList();

                        _logger.LogDebug("?? [BLOCK-FILTER] Münhasýr blok mevcut - Sadece {ExclusiveBlocks} türü gösteriliyor",
                            string.Join(", ", currentExclusiveBlocks));
                    }
                }
            }

            // Test/Debug bloklarý prod ortamýnda gizle
            if (!IsDebugEnvironment())
            {
                var debugBlocks = new List<string>
                {
                    "testBlock",
                    "debugBlock",
                    "sampleBlock"
                };

                notification.Model.Blocks = notification.Model.Blocks
                    .Where(b => !debugBlocks.Contains(b.Alias))
                    .ToList();

                _logger.LogDebug("?? [BLOCK-FILTER] Production ortamýnda {Count} debug bloðu gizlendi", debugBlocks.Count);
            }
        }

        /// <summary>
        /// Þu anda content'te bulunan blok türlerini al
        /// Bu metod BlockGridValidationHandler'daki mantýðý kullanabilir
        /// </summary>
        private List<string> GetCurrentContentBlocks(RemodelBlockCatalogueNotification notification)
        {
            // Bu metod gerçek implementasyonda content'in mevcut bloklarýný alacak
            // Þimdilik boþ liste döndürüyoruz
            return new List<string>();
        }

        /// <summary>
        /// Debug ortamý kontrolü
        /// </summary>
        private bool IsDebugEnvironment()
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }
}