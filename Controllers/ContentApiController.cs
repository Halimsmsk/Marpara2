using Microsoft.AspNetCore.Mvc;
using Morpara.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Controllers;
using Umbraco.Extensions;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Web.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using System.Reflection;

namespace Morpara.Controllers
{
    public class ContentApiController : UmbracoApiController
    {
        private readonly IContentRetrievalService _contentRetrievalService;
        private readonly IMorparaCultureService _cultureService;
        private readonly IPropertyMappingService _propertyMappingService;
        private readonly ICategoryService _categoryService;
        private readonly IBlogService _blogService;
        private readonly IQuestionService _questionService;
        private readonly IQuestionCategoriesService _questionCategoriesService;
        private readonly IMenuService _menuService;
        private readonly ICampaignService _campaignService;
        private readonly ILocationService _locationService;
        private readonly IVariationContextAccessor _variation;
        private readonly ILogger<ContentApiController> _logger;
        private readonly IUmbracoContextAccessor _umbracoContextAccessor;
        private readonly UmbracoHelper _umbracoHelper;
        private readonly IMemoryCache _memoryCache;

        // Cache configuration
        private readonly TimeSpan _navigationCacheDuration = TimeSpan.FromHours(2); // 2 saat cache süresi
        private readonly TimeSpan _sitemapCacheDuration = TimeSpan.FromHours(3); // 3 saat sitemap cache
        private const string HEADER_NAV_CACHE_KEY_PREFIX = "HeaderNav_";
        private const string FOOTER_NAV_CACHE_KEY_PREFIX = "FooterNav_";
        private const string SITEMAP_XML_CACHE_KEY = "Sitemap_XML";
        private const string SITEMAP_JSON_CACHE_KEY_PREFIX = "Sitemap_JSON_";

        public ContentApiController(
            IContentRetrievalService contentRetrievalService,
            IMorparaCultureService cultureService,
            IPropertyMappingService propertyMappingService,
            ICategoryService categoryService,
            IBlogService blogService,
            IQuestionService questionService,
            IQuestionCategoriesService questionCategoriesService,
            IMenuService menuService,
            ICampaignService campaignService,
            ILocationService locationService,
            IVariationContextAccessor variation,
            ILogger<ContentApiController> logger,
            IUmbracoContextAccessor umbracoContextAccessor,
            UmbracoHelper umbracoHelper,
            IMemoryCache memoryCache)
        {
            _contentRetrievalService = contentRetrievalService;
            _cultureService = cultureService;
            _propertyMappingService = propertyMappingService;
            _categoryService = categoryService;
            _blogService = blogService;
            _questionService = questionService;
            _questionCategoriesService = questionCategoriesService;
            _menuService = menuService;
            _campaignService = campaignService;
            _locationService = locationService;
            _variation = variation;
            _logger = logger;
            _umbracoContextAccessor = umbracoContextAccessor;
            _umbracoHelper = umbracoHelper;
            _memoryCache = memoryCache;
        }

        /// <summary>
        /// 🚀 Media Proxy Endpoint - 307 Redirect Problem Fix
        /// Umbraco media URL'lerini proxy ederek direkt file stream döndürür
        /// </summary>
        [HttpGet("umbraco/api/ContentApi/media/{*mediaPath}")]
        public async Task<IActionResult> GetMedia(string mediaPath)
        {
            try
            {
                _logger.LogInformation("[DEBUG MediaProxy] Proxying media request: {MediaPath}", mediaPath);

                // Media dosyasının fiziksel yolunu belirle
                var webRootPath = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootPath;
                var mediaFolder = "media";
                var fullPath = Path.Combine(webRootPath, mediaFolder, mediaPath);

                _logger.LogInformation("[DEBUG MediaProxy] Full file path: {FullPath}", fullPath);

                if (!System.IO.File.Exists(fullPath))
                {
                    _logger.LogWarning("[DEBUG MediaProxy] File not found: {FullPath}", fullPath);
                    return NotFound("Media dosyası bulunamadı.");
                }

                // File extension'dan MIME type belirle
                var extension = Path.GetExtension(fullPath).ToLowerInvariant();
                var contentType = GetContentType(extension);

                _logger.LogInformation("[DEBUG MediaProxy] Serving file with content type: {ContentType}", contentType);

                // Cache headers ekle (307 redirect'i önlemek için)
                Response.Headers.CacheControl = "public,max-age=31536000";
                Response.Headers.Expires = DateTime.UtcNow.AddYears(1).ToString("R");
                
                // Security headers
                Response.Headers.Add("X-Content-Type-Options", "nosniff");
                Response.Headers.Add("Referrer-Policy", "strict-origin-when-cross-origin");

                // File stream ile direkt döndür
                var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
                
                return File(fileStream, contentType, enableRangeProcessing: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ERROR MediaProxy] Error serving media file: {MediaPath}", mediaPath);
                return StatusCode(500, $"Media dosyası servis edilirken hata: {ex.Message}");
            }
        }

        /// <summary>
        /// File extension'a göre MIME type döndürür
        /// </summary>
        private string GetContentType(string extension)
        {
            return extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".svg" => "image/svg+xml",
                ".bmp" => "image/bmp",
                ".ico" => "image/x-icon",
                ".tiff" or ".tif" => "image/tiff",
                ".pdf" => "application/pdf",
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".avi" => "video/avi",
                ".mov" => "video/quicktime",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".ogg" => "audio/ogg",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".txt" => "text/plain",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".zip" => "application/zip",
                ".rar" => "application/x-rar-compressed",
                _ => "application/octet-stream"
            };
        }

        [HttpGet("umbraco/api/ContentApi/ById/{id}")]
        public IActionResult ById(string id)
        {
            var item = _contentRetrievalService.GetContentById(id);
            if (item == null)
                return NotFound("İçerik bulunamadı.");

            var children = _contentRetrievalService.GetShapedChildren(item);
            return Ok(children);
        }

        [HttpGet("umbraco/api/ContentApi/{alias}")]
        public IActionResult ByAlias(string alias, bool? preview = false, string culture = "tr-TR", string? filter = null)
        {
            if (string.IsNullOrEmpty(alias))
                return BadRequest("Alias parametresi gereklidir.");

            var activeCulture = culture ?? "tr-TR";
            var filterParams = _propertyMappingService.ParseFilterParameters(filter ?? string.Empty);

            try
            {
                var content = _contentRetrievalService.GetContentByAlias(alias, activeCulture);
                if (content == null)
                    return NotFound($"'{alias}' alias'ına sahip içerik bulunamadı.");

                // Cache kontrol - sadece headerNav ve footerNavbar için
                bool isNavigationContent = content.ContentType.Alias == "headerNav" || content.ContentType.Alias == "footerNavbar";
                
                if (!isNavigationContent)
                {
                    return NotFound($"'{alias}' alias'ına sahip navigation içeriği bulunamadı.");
                }
                // Preview mode'da cache kullanma
                bool usePreview = preview ?? false;
                
                // Cache key oluştur
                string cacheKey = content.ContentType.Alias == "headerNav" 
                    ? $"{HEADER_NAV_CACHE_KEY_PREFIX}{alias}_{activeCulture}" 
                    : $"{FOOTER_NAV_CACHE_KEY_PREFIX}{alias}_{activeCulture}";

                // Cache'den kontrol et (preview mode değilse)
                if (!usePreview && _memoryCache.TryGetValue(cacheKey, out object? cachedResult))
                {
                    _logger.LogInformation("[CACHE HIT] Navigation content served from cache: {Alias} ({ContentType}) - Culture: {Culture}", 
                        alias, content.ContentType.Alias, activeCulture);
                    return Ok(cachedResult);
                }

                // Cache miss veya preview mode - content'i işle
                _logger.LogInformation("[CACHE MISS] Processing navigation content: {Alias} ({ContentType}) - Culture: {Culture}, Preview: {Preview}", 
                    alias, content.ContentType.Alias, activeCulture, usePreview);

                var contentUrl = content.Url(activeCulture);
                if (string.IsNullOrWhiteSpace(contentUrl) || contentUrl == "#")
                {
                    contentUrl = $"/{activeCulture}/{alias}";
                }

                var result = new
                {
                    Id = content.Id,
                    Key = content.Key,
                    Level = content.Level,
                    Name = content.Name,
                    Alias = alias,
                    Culture = activeCulture,
                    Url = contentUrl,
                    ContentType = content.ContentType.Alias,
                    ContentPath = content.Path,
                    ParentUrl = content.Parent()?.Url(activeCulture),
                    FilterParams = filterParams,
                    AlternativeCultures = _cultureService.GetAlternativeCulturesById(content.Id),
                    Properties = content.Properties.ToDictionary(
                        p => p.Alias,
                        p => _propertyMappingService.MapValue(p, content.Id, content.Key, filterParams)),
                    // Cache metadata
                    CachedAt = DateTime.UtcNow,
                    FromCache = false
                };

                // Cache'e kaydet (preview mode değilse)
                if (!usePreview)
                {
                    var cacheOptions = new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = _navigationCacheDuration,
                        Priority = CacheItemPriority.High, // Navigation content'leri yüksek öncelikli
                        Size = 1 // Memory pressure için size tracking
                    };

                    // Content değiştiğinde cache'i temizlemek için dependency ekle
                    cacheOptions.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration
                    {
                        EvictionCallback = (key, value, reason, state) =>
                        {
                            _logger.LogInformation("[CACHE EVICTED] Navigation cache evicted: {Key}, Reason: {Reason}", 
                                key, reason);
                        }
                    });

                    _memoryCache.Set(cacheKey, result, cacheOptions);
                    
                    _logger.LogInformation("[CACHE SET] Navigation content cached: {Alias} ({ContentType}) - Culture: {Culture}, Duration: {Duration}", 
                        alias, content.ContentType.Alias, activeCulture, _navigationCacheDuration);
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ERROR] Navigation content processing failed: {Alias} - Culture: {Culture}", alias, activeCulture);
                return StatusCode(500, $"Navigation içerik işlenirken hata: {ex.Message}");
            }
        }

        [HttpGet("umbraco/api/ContentApi/ByUrl")]
        public IActionResult ByUrl(string? url, bool? preview = false, string culture = "tr-TR", string? filter = null)
        {
            var activeCulture = culture ?? "tr-TR";
            _variation.VariationContext = new VariationContext(activeCulture);

            // URL'yi normalize et - exactly like original
            var normalized = (url ?? "/").Trim('/');
            var route = string.IsNullOrEmpty(normalized) ? "/" : "/" + normalized;

            var filterParams = _propertyMappingService.ParseFilterParameters(filter ?? string.Empty);

            try
            {
                var result = _contentRetrievalService.GetContentByUrl(route, activeCulture, preview ?? false);
                var content = result.content;
                var categoryName = result.categoryName;

                if (content == null)
                    return NotFound($"İçerik bulunamadı → Url:{url} | Culture:{activeCulture} | Route:{route}");

                // Sadece page content type'ı olan içerikleri döndür
                if (content.ContentType.Alias != "page")
                    return NotFound($"Page içeriği bulunamadı → Url:{url} | ContentType:{content.ContentType.Alias}");

                // Manual filtering for questionsContent pages (category and search parameters)
                if (content.ContentType.Alias == "page")
                {
                    // Check if this page has questionsContent or questionsPage blocks
                    var homeProperty = content.GetProperty("home");
                    var hasQuestionsContent = false;
                    
                    if (homeProperty != null && homeProperty.HasValue())
                    {
                        var blockGrid = homeProperty.GetValue() as Umbraco.Cms.Core.Models.Blocks.BlockGridModel;
                        if (blockGrid != null)
                        {
                            hasQuestionsContent = blockGrid.Any(b => 
                                b.Content?.ContentType?.Alias == "questionsContent" || 
                                b.Content?.ContentType?.Alias == "questionsPage");
                        }
                    }
                    
                    _logger.LogInformation("[DEBUG ContentApiController] Page '{PageName}' hasQuestionsContent: {HasQuestionsContent}", content.Name, hasQuestionsContent);
                    
                    // ✨ YENİ: Child page (questionsContent var ama questionsPage yok) ise, parent'a redirect et ve activeContentId ekle
                    var hasQuestionsPage = false;
                    if (homeProperty != null && homeProperty.HasValue())
                    {
                        var blockGrid = homeProperty.GetValue() as Umbraco.Cms.Core.Models.Blocks.BlockGridModel;
                        if (blockGrid != null)
                        {
                            hasQuestionsPage = blockGrid.Any(b => b.Content?.ContentType?.Alias == "questionsPage");
                        }
                    }
                    
                    if (hasQuestionsContent && !hasQuestionsPage)
                    {
                        _logger.LogInformation("[DEBUG ContentApiController] Child page detected (has questionsContent but no questionsPage). Redirecting to parent with activeContentId={ContentId}", content.Id);
                        
                        // Parent'ın URL'ini al
                        var parent = content.Parent;
                        if (parent != null)
                        {
                            // ✅ FIX: Parent URL'den culture prefix'ini çıkar
                            var parentUrl = parent.Url(activeCulture);
                            _logger.LogInformation("[DEBUG ContentApiController] Parent URL with culture: {ParentUrl}", parentUrl);
                            
                            // Culture prefix varsa kaldır (/en/faq/ -> faq/)
                            var parentUrlNoCulture = parentUrl.TrimStart('/');
                            if (activeCulture != "tr-TR" && parentUrlNoCulture.StartsWith($"{activeCulture}/", StringComparison.OrdinalIgnoreCase))
                            {
                                parentUrlNoCulture = parentUrlNoCulture.Substring(activeCulture.Length + 1);
                            }
                            _logger.LogInformation("[DEBUG ContentApiController] Parent URL without culture prefix: {ParentUrlNoCulture}", parentUrlNoCulture);
                            
                            // Child'ın slug'ını activeContentId olarak ekle
                            var childSlug = content.UrlSegment ?? content.Name.ToLowerInvariant().Replace(" ", "-");
                            
                   
                            string? categorySlugFromUrl = null;
                            if (!string.IsNullOrEmpty(url))
                            {
                                var urlParts = url.Trim('/').Split('/');
                                if (urlParts.Length >= 3)
                                {

                                    categorySlugFromUrl = urlParts[1];
                                    _logger.LogInformation("[DEBUG ContentApiController] Extracted category slug from URL: {CategorySlug}", categorySlugFromUrl);
                                }
                            }
                            
                            // Filter string oluştur (format: componentId:param1:value1:param2:value2)
                            var filterString = $"questionsPage:activeContentId:{childSlug}";
                            
                            // Önce URL'den gelen kategoriyi kullan, yoksa categoryName'i kullan
                            var categoryToUse = categorySlugFromUrl ?? categoryName;
                            if (!string.IsNullOrEmpty(categoryToUse))
                            {
                                filterString += $":categories:{categoryToUse}";
                            }
                            
                            _logger.LogInformation("[DEBUG ContentApiController] Redirecting to parent with filter: {FilterString}", filterString);
                            
                            // Parent'a redirect et (ByUrl metodunu yeniden çağır) - culture prefix olmadan
                            return ByUrl(parentUrlNoCulture, preview, activeCulture, filterString);
                        }
                    }
                    
                    // Sadece questionsPage bloğu varsa filter parametrelerini ayarla
                    if (hasQuestionsPage)
                    {
                        // Ensure filterParams exists
                        if (filterParams == null)
                        {
                            filterParams = new Dictionary<string, Dictionary<string, string>>();
                        }
                        
                        // Use the wildcard:questionsContent format like the old system
                        var wildcardKey = "wildcard:questionsContent";
                        if (!filterParams.ContainsKey(wildcardKey))
                        {
                            filterParams[wildcardKey] = new Dictionary<string, string>();
                        }
                        
                        filterParams[wildcardKey]["componentType"] = "questionsContent";
                        filterParams[wildcardKey]["wildcard"] = "true";
                        
                        // Add category filter if category is in URL
                        if (!string.IsNullOrEmpty(categoryName))
                        {
                            // Use proper normalization like QuestionService does
                            var normalizedCategoryName = Morpara.Helpers.TextNormalizationHelper.NormalizeCategoryName(categoryName);
                            filterParams[wildcardKey]["categories"] = normalizedCategoryName;
                            
                            _logger.LogInformation("[DEBUG ContentApiController] Added category filter: wildcard:questionsContent with categories='{NormalizedCategoryName}' (original: '{CategoryName}')", normalizedCategoryName, categoryName);
                        }
                        
                        // Copy existing parameters from original filter params
                        var originalFilterParams = _propertyMappingService.ParseFilterParameters(filter ?? string.Empty);
                        _logger.LogInformation("[DEBUG ContentApiController] Original filter: '{Filter}', parsed {Count} filter groups", filter ?? "null", originalFilterParams?.Count ?? 0);
                        
                        if (originalFilterParams != null)
                        {
                            foreach (var kvp in originalFilterParams)
                            {
                                _logger.LogInformation("[DEBUG ContentApiController] Processing filter group: '{Key}' with {ParamCount} parameters", kvp.Key, kvp.Value.Count);
                                
                                // Handle questionsPage parameters - map them to wildcard:questionsContent
                                if (kvp.Key == "questionsPage")
                                {
                                    foreach (var param in kvp.Value)
                                    {
                                        if (param.Key == "categories")
                                        {
                                            // Use proper category normalization
                                            var normalizedCategory = Morpara.Helpers.TextNormalizationHelper.NormalizeCategoryName(param.Value);
                                            filterParams[wildcardKey]["categories"] = normalizedCategory;
                                            _logger.LogInformation("[DEBUG ContentApiController] Added category from questionsPage filter: '{NormalizedCategory}' (original: '{Original}')", normalizedCategory, param.Value);
                                        }
                                        else
                                        {
                                            filterParams[wildcardKey][param.Key] = param.Value;
                                            _logger.LogInformation("[DEBUG ContentApiController] Added questionsPage parameter: {Key}='{Value}'", param.Key, param.Value);
                                        }
                                    }
                                }
                                // Copy search parameters directly
                                else if (kvp.Key == "search" || kvp.Key.StartsWith("search"))
                                {
                                    foreach (var param in kvp.Value)
                                    {
                                        filterParams[wildcardKey][param.Key] = param.Value;
                                        _logger.LogInformation("[DEBUG ContentApiController] Added search parameter: {Key}='{Value}'", param.Key, param.Value);
                                    }
                                }
                            }
                        }
                        
                        // Also handle direct search parameter format (search:term)
                        if (!string.IsNullOrEmpty(filter) && filter.Contains("search:"))
                        {
                            var searchMatch = System.Text.RegularExpressions.Regex.Match(filter, @"search:([^&\s]+)");
                            if (searchMatch.Success)
                            {
                                var searchTerm = searchMatch.Groups[1].Value;
                                filterParams[wildcardKey]["search"] = searchTerm;
                                _logger.LogInformation("[DEBUG ContentApiController] Added direct search parameter: search='{SearchTerm}'", searchTerm);
                            }
                        }
                    }
                }

                var contentUrl = content.Url(activeCulture);
                if (string.IsNullOrWhiteSpace(contentUrl) || contentUrl == "#")
                {
                    contentUrl = "/" + activeCulture + (route == "/" ? string.Empty : route);
                }

                // Kategori formatı ile URL oluştur (eğer kategorisi varsa)
                if (!string.IsNullOrEmpty(categoryName) && _categoryService.HasCategory(content, categoryName))
                {
                    var parentContent = content.Parent<IPublishedContent>();
                    var baseUrl = parentContent?.Url(activeCulture).TrimEnd('/') ?? string.Empty;
                    var contentSlug = _categoryService.ExtractSlugFromUrl(content.Url(activeCulture));
                    contentUrl = $"{baseUrl}/{categoryName}/{contentSlug}/";
                }

                // Kategori bilgisini al
                var categoryInfo = _categoryService.GetCategoryInfo(content, categoryName);

                // Handle category content type
                if (content.ContentType.Alias == "category")
                {
                    var parentContent = content.Parent;
                    var parentUrl = parentContent?.Url(activeCulture) ?? "/";
                    parentUrl = parentUrl.TrimEnd('/');

                    var parentPath = parentContent?.Url(activeCulture) ?? "/";
                    var uri = new Uri(parentPath, UriKind.RelativeOrAbsolute);
                    var parentRelativePath = uri.IsAbsoluteUri ? uri.PathAndQuery : parentPath;

                    if (filterParams == null)
                    {
                        filterParams = new Dictionary<string, Dictionary<string, string>>();
                    }

                    string filterString = $"questionsPage:categories:{content.Name}";
                    return ByUrl(parentRelativePath, preview, activeCulture, filterString);
                }

                return Ok(new
                {
                    Id = content.Id,
                    Key = content.Key,
                    Level = content.Level,
                    Name = content.Name,
                    Culture = activeCulture,
                    OriginalUrl = url,
                    Route = route,
                    Url = contentUrl,
                    ContentType = content.ContentType.Alias,
                    ContentPath = content.Path,
                    CategoryName = categoryName,
                    Category = categoryInfo,
                    ParentUrl = content.Parent()?.Url(activeCulture),
                    FilterParams = filterParams,
                    AlternativeCultures = _cultureService.GetAlternativeCulturesById(content.Id),
                    Properties = content.Properties.ToDictionary(
                        p => p.Alias,
                        p => _propertyMappingService.MapValue(p, content.Id, content.Key, filterParams, url, categoryName))
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"İçerik aranırken hata: {ex.Message}");
            }
        }

        [HttpGet("umbraco/api/ContentApi/GetSSSSorular/{id}/{categories?}")]
        public IActionResult GetSSSSorular(string id, string? categories = null, int? maxQuestion = null, bool? multiCategoriesContent = false)
        {
            try
            {
                var result = _questionService.GetSSSSorular(id, categories, maxQuestion, multiCategoriesContent);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"SSS sorular alınırken hata: {ex.Message}");
            }
        }

        [HttpGet("umbraco/api/ContentApi/GetDistricts")]
        public IActionResult GetDistricts(string cityId, string culture = "tr-TR")
        {
            try
            {
                var result = _locationService.GetDistricts(cityId, culture);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"İlçeler alınırken hata: {ex.Message}");
            }
        }

        [HttpGet("umbraco/api/ContentApi/sitemap")]
        public IActionResult GetSitemap(string culture = "tr-TR")
        {
            try
            {
                // Cache kontrolü
                if (_memoryCache.TryGetValue(SITEMAP_XML_CACHE_KEY, out string? cachedXml))
                {
                    _logger.LogInformation("[CACHE HIT] XML Sitemap served from cache. Size: {Size} bytes", cachedXml?.Length ?? 0);
                    
                    // DEBUG: Cache içeriğini kontrol et
                    var urlCount = System.Text.RegularExpressions.Regex.Matches(cachedXml ?? "", "<url>").Count;
                    _logger.LogWarning("[DEBUG CACHE] Cached sitemap contains {UrlCount} URLs", urlCount);
                    
                    return Content(cachedXml!, "application/xml");
                }

                _logger.LogInformation("[CACHE MISS] Generating XML sitemap");

                var umbracoContext = _umbracoContextAccessor.GetRequiredUmbracoContext();
                
                // Tüm published content'i al ve page olanları filtrele
                var allContent = umbracoContext.Content.GetAtRoot().SelectMany(x => x.DescendantsOrSelf());
                var pageContents = allContent.Where(x => x.ContentType.Alias == "page").ToList();
                
                _logger.LogInformation("[DEBUG] Total pages found: {PageCount}", pageContents.Count);
                
                // XML sitemap oluştur
                var sitemap = new System.Text.StringBuilder();
                sitemap.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                sitemap.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\" xmlns:xhtml=\"http://www.w3.org/1999/xhtml\">");
                
                var baseUrl = "https://morpara.com";
                var addedUrlCount = 0;
                
                foreach (var content in pageContents)
                {
                    // representationItem içeren sayfaları sitemap'ten çıkar
                    if (!HasRepresentationItem(content))
                    {
                        AddUrlToSitemap(sitemap, content, baseUrl);
                        addedUrlCount++;
                    }
                    else
                    {
                        _logger.LogInformation("[DEBUG] Skipped page with representationItem: {PageName} (ID: {PageId})", content.Name, content.Id);
                    }
                }

                _logger.LogInformation("[DEBUG] Added {AddedCount} URLs to sitemap (out of {TotalPages} pages)", addedUrlCount, pageContents.Count);

                sitemap.AppendLine("</urlset>");

                var sitemapXml = sitemap.ToString();

                // Cache'e kaydet
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = _sitemapCacheDuration,
                    Priority = CacheItemPriority.Normal,
                    Size = 1
                };

                _memoryCache.Set(SITEMAP_XML_CACHE_KEY, sitemapXml, cacheOptions);

                _logger.LogInformation("[CACHE SET] XML Sitemap cached for {Duration}", _sitemapCacheDuration);

                return Content(sitemapXml, "application/xml");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ERROR ContentApiController Sitemap] Error generating sitemap: {Message}", ex.Message);
                return StatusCode(500, $"Sitemap oluşturulurken hata: {ex.Message}");
            }
        }

        [HttpGet("umbraco/api/ContentApi/sitemap/json")]
        public IActionResult GetSitemapJson(string lang = "tr")
        {
            try
            {
                // lang parametresini culture'a çevir
                var culture = lang.ToLower() == "en" ? "en" : "tr-TR";
                var cacheKey = $"{SITEMAP_JSON_CACHE_KEY_PREFIX}{lang}";

                // Cache kontrolü
                if (_memoryCache.TryGetValue(cacheKey, out List<object>? cachedJson))
                {
                    _logger.LogInformation("[CACHE HIT] JSON Sitemap served from cache for lang: {Lang}", lang);
                    return Ok(cachedJson);
                }
                
                _logger.LogInformation("[CACHE MISS] Generating JSON sitemap for lang: {Lang} (culture: {Culture})", lang, culture);

                var umbracoContext = _umbracoContextAccessor.GetRequiredUmbracoContext();
                
                // Tüm published content'i al ve page olanları filtrele
                var allContent = umbracoContext.Content.GetAtRoot().SelectMany(x => x.DescendantsOrSelf());
                var pageContents = allContent.Where(x => x.ContentType.Alias == "page");
                
                var baseUrl = "https://morpara.com";
                var sitemapEntries = new List<object>();
                
                // Her content için sadece bir kez işle (culture'e göre filtreleme yapmadan)
                var processedContentIds = new HashSet<int>();
                
                foreach (var content in pageContents)
                {
                    // Tekrar işlemeyi önle
                    if (processedContentIds.Contains(content.Id))
                        continue;
                    
                    processedContentIds.Add(content.Id);
                    
                    // representationItem içeren sayfaları sitemap'ten çıkar
                    if (HasRepresentationItem(content))
                        continue;

                    // noIndex kontrolü - seçili dilde
                    if (content.HasProperty("noIndex") && content.Value<bool>("noIndex", culture))
                        continue;

                    // Seçili dildeki URL'i al
                    var contentUrl = content.Url(culture);
                    
                    // Geçersiz URL'leri atla
                    if (string.IsNullOrWhiteSpace(contentUrl) || contentUrl.Contains("#"))
                        continue;

                    // Kategori bilgisini block grid'den çıkar
                    string? categoryName = null;
                    if (HasQuestionsContent(content))
                    {
                        categoryName = ExtractCategoryFromBlockGrid(content);
                        
                        // Kategori bulunduysa URL'yi yeniden oluştur
                        if (!string.IsNullOrEmpty(categoryName))
                        {
                            contentUrl = BuildUrlWithCategory(content, categoryName, culture);
                        }
                        else
                        {
                            // FAQ sayfası ama kategori yok - bu sayfayı atla (sadece kategorili olanları göster)
                            _logger.LogInformation("[DEBUG ContentApiController SitemapJson] Skipping FAQ page without category: {ContentId} ({ContentName})", 
                                content.Id, content.Name);
                            continue;
                        }
                    }

                    // Tam URL oluştur
                    var fullUrl = baseUrl + contentUrl;
                    
                    // Change frequency
                    string? changeFreq = null;
                    if (content.HasProperty("siteMapChangeFrequency"))
                    {
                        changeFreq = content.Value<string>("siteMapChangeFrequency", culture);
                    }

                    // Entry oluştur - kategori varsa ekle
                    var entry = new
                    {
                        url = fullUrl,
                        lastModified = content.UpdateDate.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                        changeFrequency = changeFreq,
                        priority = (double?)null, // Varsayılan olarak null
                        category = categoryName // Kategori bilgisini ekle
                    };
                    
                    sitemapEntries.Add(entry);
                    
                    _logger.LogInformation("[DEBUG ContentApiController SitemapJson] Added entry: {Url}, Category: {Category}", 
                        fullUrl, categoryName ?? "none");
                }

                // Cache'e kaydet
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = _sitemapCacheDuration,
                    Priority = CacheItemPriority.Normal,
                    Size = 1
                };

                _memoryCache.Set(cacheKey, sitemapEntries, cacheOptions);

                _logger.LogInformation("[CACHE SET] JSON Sitemap cached for lang: {Lang}, Duration: {Duration}, Entries: {Count}", 
                    lang, _sitemapCacheDuration, sitemapEntries.Count);

                return Ok(sitemapEntries);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ERROR ContentApiController SitemapJson] Error generating JSON sitemap: {Message}", ex.Message);
                return StatusCode(500, $"JSON sitemap oluşturulurken hata: {ex.Message}");
            }
        }

        private void AddUrlToSitemap(System.Text.StringBuilder sitemap, IPublishedContent content, string baseUrl)
        {
            try
            {
                // Sadece Türkçe (default culture) için URL girişi oluştur
                var defaultCulture = "tr-TR";
                var contentUrl = content.Url(defaultCulture);
                
                // Geçersiz URL'leri atla
                if (string.IsNullOrWhiteSpace(contentUrl) || contentUrl.Contains("#"))
                {
                    _logger.LogInformation("[DEBUG AddUrl] Skipped invalid URL for: {PageName} (ID: {PageId}), URL: {Url}", content.Name, content.Id, contentUrl);
                    return;
                }

                // noIndex kontrolü
                if (content.HasProperty("noIndex") && content.Value<bool>("noIndex", defaultCulture))
                {
                    _logger.LogInformation("[DEBUG AddUrl] Skipped noIndex page: {PageName} (ID: {PageId})", content.Name, content.Id);
                    return;
                }

                // Kategori bilgisini block grid'den çıkar
                string? categoryName = null;
                if (HasQuestionsContent(content))
                {
                    categoryName = ExtractCategoryFromBlockGrid(content);
                    
                    // Kategori bulunduysa URL'yi yeniden oluştur
                    if (!string.IsNullOrEmpty(categoryName))
                    {
                        contentUrl = BuildUrlWithCategory(content, categoryName, defaultCulture);
                        _logger.LogInformation("[DEBUG AddUrl] FAQ page with category: {PageName} (ID: {PageId}), Category: {Category}, URL: {Url}", 
                            content.Name, content.Id, categoryName, contentUrl);
                    }
                    else
                    {
                        _logger.LogInformation("[DEBUG AddUrl] FAQ page WITHOUT category - still adding: {PageName} (ID: {PageId}), URL: {Url}", 
                            content.Name, content.Id, contentUrl);
                    }
                }

                // Tam URL oluştur
                var fullUrl = baseUrl + contentUrl;
                
                _logger.LogInformation("[DEBUG AddUrl] Adding URL to sitemap: {PageName} (ID: {PageId}), Full URL: {FullUrl}", 
                    content.Name, content.Id, fullUrl);
                
                // URL entry başlat
                sitemap.AppendLine("  <url>");
                sitemap.AppendLine($"    <loc>{System.Security.SecurityElement.Escape(fullUrl)}</loc>");
                
                // Son güncelleme tarihi
                var updateDate = content.UpdateDate;
                sitemap.AppendLine($"    <lastmod>{updateDate:yyyy-MM-dd}</lastmod>");
                
                // Change frequency
                if (content.HasProperty("siteMapChangeFrequency"))
                {
                    var changeFreq = content.Value<string>("siteMapChangeFrequency", defaultCulture);
                    if (!string.IsNullOrEmpty(changeFreq))
                    {
                        sitemap.AppendLine($"    <changefreq>{changeFreq.ToLower()}</changefreq>");
                    }
                }
                
                // Priority (default 0.5)
                sitemap.AppendLine("    <priority>0.5</priority>");
                
                // x-default için Türkçe URL'yi ekle
                sitemap.AppendLine($"    <xhtml:link rel=\"alternate\" hreflang=\"x-default\" href=\"{System.Security.SecurityElement.Escape(fullUrl)}\" />");
                
                // Tüm kültürler için hreflang ekle
                foreach (var cultureInfo in content.Cultures)
                {
                    var altCultureCode = cultureInfo.Key;
                    var altUrl = content.Url(altCultureCode);
                    
                    if (string.IsNullOrWhiteSpace(altUrl) || altUrl.Contains("#"))
                        continue;
                    
                    // Kategori varsa alternatif URL'yi de oluştur
                    if (!string.IsNullOrEmpty(categoryName) && HasQuestionsContent(content))
                    {
                        altUrl = BuildUrlWithCategory(content, categoryName, altCultureCode);
                    }
                    
                    var altFullUrl = baseUrl + altUrl;
                    var hreflang = altCultureCode.ToLower();
                    
                    sitemap.AppendLine($"    <xhtml:link rel=\"alternate\" hreflang=\"{hreflang}\" href=\"{System.Security.SecurityElement.Escape(altFullUrl)}\" />");
                }
                
                sitemap.AppendLine("  </url>");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ERROR ContentApiController] Error adding URL to sitemap for content {ContentId}: {Message}", content.Id, ex.Message);
            }
        }

        private object CreateSitemapEntry(IPublishedContent content, string culture)
        {
            try
            {
                var contentUrl = content.Url(culture);
                var alternativeCultures = new Dictionary<string, object>();

                // Kategori bilgisini block grid'den çıkar (questionsContent varsa)
                string? categoryName = null;
                if (content.ContentType.Alias == "page" && HasQuestionsContent(content))
                {
                    categoryName = ExtractCategoryFromBlockGrid(content);
                    
                    // Eğer kategori bulunduysa URL'yi yeniden oluştur
                    if (!string.IsNullOrEmpty(categoryName))
                    {
                        contentUrl = BuildUrlWithCategory(content, categoryName, culture);
                    }
                }

                // Aynı ID'nin tüm kültürlerini al - basit yaklaşım
                foreach (var cultureInfo in content.Cultures)
                {
                    var cultureUrl = content.Url(cultureInfo.Key);
                    
                    // Block grid'de questionsContent varsa kategori ekle
                    if (!string.IsNullOrEmpty(categoryName) && HasQuestionsContent(content))
                    {
                        cultureUrl = BuildUrlWithCategory(content, categoryName, cultureInfo.Key);
                    }
                    
                    alternativeCultures[cultureInfo.Key] = new
                    {
                        path = cultureUrl
                    };
                }

                // Eğer sadece Türkçe var ve FAQ sayfasıysa, İngilizce varsayılan URL oluştur
                if (!alternativeCultures.ContainsKey("en") && HasQuestionsContent(content))
                {
                    var englishUrl = BuildDefaultEnglishUrl(content, categoryName);
                    if (!string.IsNullOrEmpty(englishUrl))
                    {
                        alternativeCultures["en"] = new { path = englishUrl };
                    }
                }

                // Properties mapping - Sadece sitemap için gerekli olanlar
                var properties = new Dictionary<string, object>();
                
                if (content.HasProperty("canonicalUrl"))
                    properties["canonicalUrl"] = content.Value<string>("canonicalUrl") ?? "";
                
                if (content.HasProperty("noIndex"))
                    properties["noIndex"] = content.Value<bool>("noIndex");
                
                if (content.HasProperty("siteMapChangeFrequency"))
                    properties["siteMapChangeFrequency"] = content.Value<string>("siteMapChangeFrequency") ?? "";

                return new
                {
                    name = content.Name,
                    culture = culture,
                    originalUrl = contentUrl,
                    route = contentUrl,
                    url = contentUrl,
                    contentType = content.ContentType.Alias,
                    category = categoryName,
                    alternativeCultures = alternativeCultures,
                    properties = properties
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ERROR ContentApiController] Error creating sitemap entry for content {ContentId}: {Message}", content.Id, ex.Message);
                return new
                {
                    name = content.Name,
                    culture = culture,
                    originalUrl = content.Url(culture),
                    route = content.Url(culture),
                    url = content.Url(culture),
                    contentType = content.ContentType.Alias,
                    category = (string?)null,
                    alternativeCultures = new { },
                    properties = new { }
                };
            }
        }

        private string? ExtractCategoryFromBlockGrid(IPublishedContent content)
        {
            try
            {
                // Page'in home property'sindeki block grid'i al
                var homeBlockGrid = content.Value<BlockGridModel>("home");
                if (homeBlockGrid != null)
                {
                    // questionsContent block'larını bul
                    var questionBlocks = homeBlockGrid
                        .Where(b => b.Content?.ContentType?.Alias == "questionsContent")
                        .ToList();
                    
                    _logger.LogInformation("[DEBUG ContentApiController] Found {Count} questionsContent blocks for content {ContentId}", questionBlocks.Count, content.Id);
                    
                    foreach (var block in questionBlocks)
                    {
                        // Bu block'un categories property'sini kontrol et - QuestionService'teki gibi
                        var categories = block.Content?.Value<IEnumerable<IPublishedContent>>("categories");
                        if (categories != null && categories.Any())
                        {
                            // İlk kategoriyi al (QuestionService'teki gibi)
                            var firstCategory = categories.First();
                            _logger.LogInformation("[DEBUG ContentApiController] First category found in block: {CategoryName} (ID: {CategoryId})", firstCategory.Name, firstCategory.Id);
                            
                            // Kategori content'inin gerçek adını al
                            var categoryName = firstCategory.Name;
                            _logger.LogInformation("[DEBUG ContentApiController] Category name: {CategoryName}", categoryName);
                            
                            // URL segment'ini döndür (kategori alias'ı)
                            var categoryUrlSegment = firstCategory.UrlSegment ?? categoryName;
                            _logger.LogInformation("[DEBUG ContentApiController] Normalized category: {NormalizedCategory}", categoryUrlSegment);
                            
                            return categoryUrlSegment;
                        }
                    }
                }
                
                _logger.LogInformation("[DEBUG ContentApiController] No categories found in questionsContent blocks for content {ContentId}", content.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ERROR ContentApiController] Error getting categories from blocks: {Message}", ex.Message);
            }

            return null;
        }

        private string? BuildDefaultEnglishUrl(IPublishedContent content, string? categoryName)
        {
            try
            {
                // Türkçe URL'den İngilizce URL oluştur
                var turkishUrl = content.Url("tr-TR");
                if (string.IsNullOrEmpty(turkishUrl) || turkishUrl.Contains("#"))
                    return null;

                // /sikca-sorulan-sorular/ -> /en/faq/
                var englishUrl = turkishUrl.Replace("/sikca-sorulan-sorular/", "/en/faq/");
                
                // Kategori varsa İngilizce kategori URL segment'ini al
                if (!string.IsNullOrEmpty(categoryName))
                {
                    var englishCategorySlug = _questionCategoriesService.GetEnglishCategorySegment(categoryName);
                    if (!string.IsNullOrEmpty(englishCategorySlug))
                    {
                        // URL'deki Türkçe kategoriyi İngilizce ile değiştir
                        englishUrl = englishUrl.Replace($"/{categoryName}/", $"/{englishCategorySlug}/");
                        _logger.LogInformation("[DEBUG ContentApiController] Category replacement: '{TurkishCategory}' -> '{EnglishCategory}'", 
                            categoryName, englishCategorySlug);
                    }
                    else
                    {
                        _logger.LogWarning("[DEBUG ContentApiController] Could not find English category slug for: '{CategoryName}'", categoryName);
                    }
                }
                
                // Content'in URL segment'ini İngilizceye çevir
                englishUrl = TranslateContentUrlSegment(englishUrl);
                
                _logger.LogInformation("[DEBUG ContentApiController] BuildDefaultEnglishUrl - Turkish: {TurkishUrl}, English: {EnglishUrl}, Category: {Category}", 
                    turkishUrl, englishUrl, categoryName);
                
                return englishUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ERROR ContentApiController] Error building default English URL: {Message}", ex.Message);
                return null;
            }
        }

        private string TranslateUrlSegment(string url)
        {

            return url;
        }

        private string TranslateContentUrlSegment(string url)
        {
            // Content'in URL segment'ini İngilizceye çevir
           

            return url;
        }

        private bool HasQuestionsContent(IPublishedContent content)
        {
            try
            {
                // Page'in home property'sindeki block grid'i kontrol et
                var homeBlockGrid = content.Value<BlockGridModel>("home");
                if (homeBlockGrid != null)
                {
                    // questionsContent block'ları var mı kontrol et
                    var hasQuestionBlocks = homeBlockGrid
                        .Any(b => b.Content?.ContentType?.Alias == "questionsContent");
                    
                    return hasQuestionBlocks;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ERROR ContentApiController] Error checking for questionsContent: {Message}", ex.Message);
                return false;
            }
        }

        private bool HasRepresentationItem(IPublishedContent content)
        {
            try
            {
                // Page'in home property'sindeki block grid'i kontrol et
                var homeBlockGrid = content.Value<BlockGridModel>("home");
                if (homeBlockGrid != null)
                {
                    // representationItem block'ları var mı kontrol et
                    var hasRepresentationBlocks = homeBlockGrid
                        .Any(b => b.Content?.ContentType?.Alias == "representationItem" ||
                                  b.Content?.ContentType?.Alias == "representation" ||
                                  b.Content?.ContentType?.Alias == "representative");
                    
                    return hasRepresentationBlocks;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ERROR ContentApiController] Error checking for representationItem: {Message}", ex.Message);
                return false;
            }
        }

        private string BuildUrlWithCategory(IPublishedContent content, string categoryName, string culture)
        {
            try
            {
                // Parent'ın URL'ini al ve kategoriyi ekle
                var parentUrl = content.Parent()?.Url(culture)?.TrimEnd('/') ?? "";
                
                // Her kültür için doğru URL segment'ini al
                var originalUrl = content.Url(culture);
                var contentUrlName = "";
                
                if (!string.IsNullOrEmpty(originalUrl) && !originalUrl.Contains("#"))
                {
                    // URL'den son segment'i çıkar (o kültürdeki gerçek segment)
                    var urlParts = originalUrl.Trim('/').Split('/');
                    if (urlParts.Length > 0)
                    {
                        contentUrlName = urlParts.Last();
                    }
                }
                
                // Eğer URL segment'i alınamazsa, fallback olarak normalize edilmiş ismi kullan
                if (string.IsNullOrEmpty(contentUrlName))
                {
                    contentUrlName = content.UrlSegment ?? NormalizeUrlSegment(content.Name);
                }
                
                // Kategori adını dile göre çevir
                var localizedCategoryName = culture == "en" ? GetEnglishCategoryName(categoryName) : categoryName;
                
                var newUrl = $"{parentUrl}/{localizedCategoryName}/{contentUrlName}/";
                
                _logger.LogInformation("[DEBUG ContentApiController] BuildUrlWithCategory - Culture: {Culture}, Original: {OriginalUrl}, New: {NewUrl}, Category: {Category} -> {LocalizedCategory}, Segment: {ContentSegment}", 
                    culture, originalUrl, newUrl, categoryName, localizedCategoryName, contentUrlName);
                
                return newUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ERROR ContentApiController] Error building URL with category: {Message}", ex.Message);
                return content.Url(culture);
            }
        }

        private string GetEnglishCategoryName(string turkishCategoryName)
        {
            try
            {
                _logger.LogInformation("[DEBUG ContentApiController] Looking for English equivalent of category: '{TurkishCategory}'", turkishCategoryName);
                
                // QuestionCategoriesService kullanarak İngilizce segment al
                var englishSegment = _questionCategoriesService.GetEnglishCategorySegment(turkishCategoryName);
                
                if (!string.IsNullOrEmpty(englishSegment))
                {
                    _logger.LogInformation("[DEBUG ContentApiController] Found English category segment: '{EnglishSegment}' for Turkish: '{TurkishCategory}'", englishSegment, turkishCategoryName);
                    return englishSegment;
                }
                
                _logger.LogWarning("[DEBUG ContentApiController] Could not find English equivalent for category: '{TurkishCategory}'", turkishCategoryName);
                
                // Son çare: basit çeviri mantığı
                var simpleTranslation = GetSimpleTranslation(turkishCategoryName);
                if (simpleTranslation != turkishCategoryName)
                {
                    _logger.LogInformation("[DEBUG ContentApiController] Using simple translation: '{TurkishCategory}' -> '{EnglishCategory}'", turkishCategoryName, simpleTranslation);
                    return simpleTranslation;
                }
                
                return turkishCategoryName;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ERROR ContentApiController] Error finding English category name for '{TurkishCategory}': {Message}", turkishCategoryName, ex.Message);
                return turkishCategoryName;
            }
        }

        private string GetSimpleTranslation(string turkishText)
        {
            // Basit çeviri mantığı - sadece bilinen terimler için
           

       
            return turkishText;
        }

        private string NormalizeUrlSegment(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            return name.ToLowerInvariant()
                .Replace(" ", "-")
                .Replace("ç", "c")
                .Replace("ğ", "g")
                .Replace("ı", "i")
                .Replace("ö", "o")
                .Replace("ş", "s")
                .Replace("ü", "u");
        }

        private string CleanHtmlTags(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            // HTML taglarını temizle ve normalize et
            var cleaned = System.Text.RegularExpressions.Regex.Replace(input, @"<[^>]*>", string.Empty);
            
            // HTML entities'i decode et (&amp; &lt; &gt; vb.)
            cleaned = System.Net.WebUtility.HtmlDecode(cleaned);
            
            // Fazla whitespace'leri temizle
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ");
            
            return cleaned.Trim();
        }

        [HttpPost("umbraco/api/ContentApi/UpdateSearchTitle/{id}")]
        public IActionResult UpdateSearchTitle(int id, [FromQuery] string culture = "tr-TR")
        {
            try
            {
                var umbracoContext = _umbracoContextAccessor.GetRequiredUmbracoContext();
                var publishedContent = umbracoContext.Content.GetById(id);
                if (publishedContent == null)
                    return NotFound("Sayfa bulunamadı.");

                var homeBlocks = publishedContent.Value<BlockGridModel>("home", culture);
                if (homeBlocks == null || !homeBlocks.Any())
                    return BadRequest($"Home block grid ({culture}) bulunamadı veya boş.");

                var firstBlock = homeBlocks.FirstOrDefault();
                var firstTitle = firstBlock?.Content?.Value<string>("title", culture);
                if (string.IsNullOrEmpty(firstTitle))
                    return BadRequest($"İlk blockta title ({culture}) bulunamadı.");

                // HTML taglarını temizle
                var cleanTitle = CleanHtmlTags(firstTitle);
                if (string.IsNullOrEmpty(cleanTitle))
                    return BadRequest($"Title HTML temizlendikten sonra boş kaldı.");

                var contentServiceObj = HttpContext.RequestServices.GetService(typeof(IContentService));
                if (contentServiceObj == null)
                    return StatusCode(500, "IContentService bulunamadı.");
                var contentService = (IContentService)contentServiceObj;
                var contentToUpdate = contentService.GetById(id);
                if (contentToUpdate == null)
                    return NotFound("Güncellenecek içerik bulunamadı.");

                contentToUpdate.SetValue("searchTitle", cleanTitle, culture);
                contentService.Save(contentToUpdate);
                var cultures = new[] { culture };
                contentService.Publish(contentToUpdate, cultures, 0);

                return Ok(new { id, searchTitle = cleanTitle, culture });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Güncelleme sırasında hata: {ex.Message}");
            }
        }

        [HttpPost("umbraco/api/ContentApi/UpdateSearchTitleForDescendants/{id}")]
        public IActionResult UpdateSearchTitleForDescendants(int id, [FromQuery] string culture = "tr-TR")
        {
            var umbracoContext = _umbracoContextAccessor.GetRequiredUmbracoContext();
            var rootContent = umbracoContext.Content.GetById(id);
            if (rootContent == null)
                return NotFound("Ana sayfa bulunamadı.");

            var allPages = rootContent.DescendantsOrSelf().Where(x => x.ContentType.Alias == "page").ToList();
            var results = new List<object>();

            var contentServiceObj = HttpContext.RequestServices.GetService(typeof(IContentService));
            if (contentServiceObj == null)
                return StatusCode(500, "IContentService bulunamadı.");
            var contentService = (IContentService)contentServiceObj;

            foreach (var page in allPages)
            {
                try
                {
                    var homeBlocks = page.Value<BlockGridModel>("home", culture);
                    if (homeBlocks == null || !homeBlocks.Any())
                    {
                        results.Add(new { id = page.Id, error = $"Home block grid ({culture}) yok veya boş." });
                        continue;
                    }
                    var firstBlock = homeBlocks.FirstOrDefault();
                    var firstTitle = firstBlock?.Content?.Value<string>("title", culture);
                    if (string.IsNullOrEmpty(firstTitle))
                    {
                        results.Add(new { id = page.Id, error = $"İlk blockta title ({culture}) yok." });
                        continue;
                    }
                    
                    // HTML taglarını temizle
                    var cleanTitle = CleanHtmlTags(firstTitle);
                    if (string.IsNullOrEmpty(cleanTitle))
                    {
                        results.Add(new { id = page.Id, error = $"Title HTML temizlendikten sonra boş kaldı." });
                        continue;
                    }
                    
                    var contentToUpdate = contentService.GetById(page.Id);
                    if (contentToUpdate == null)
                    {
                        results.Add(new { id = page.Id, error = "Güncellenecek içerik yok." });
                        continue;
                    }
                    contentToUpdate.SetValue("searchTitle", cleanTitle, culture);
                    contentService.Save(contentToUpdate);
                    var cultures = new[] { culture };
                    contentService.Publish(contentToUpdate, cultures, 0);
                    results.Add(new { id = page.Id, searchTitle = cleanTitle, culture });
                }
                catch (Exception ex)
                {
                    results.Add(new { id = page.Id, error = ex.Message });
                }
            }
            return Ok(results);
        }

        [HttpPost("umbraco/api/ContentApi/UpdateSearchTitleForChildren/{id}")]
        public IActionResult UpdateSearchTitleForChildren(int id, [FromQuery] string culture = "tr-TR")
        {
            var umbracoContext = _umbracoContextAccessor.GetRequiredUmbracoContext();
            var rootContent = umbracoContext.Content.GetById(id);
            if (rootContent == null)
                return NotFound("Ana sayfa bulunamadı.");

            // Sadece child sayfalarını al (kendisi dahil değil)
            var childPages = rootContent.Descendants().Where(x => x.ContentType.Alias == "page").ToList();
            var results = new List<object>();

            var contentServiceObj = HttpContext.RequestServices.GetService(typeof(IContentService));
            if (contentServiceObj == null)
                return StatusCode(500, "IContentService bulunamadı.");
            var contentService = (IContentService)contentServiceObj;

            foreach (var page in childPages)
            {
                try
                {
                    var homeBlocks = page.Value<BlockGridModel>("home", culture);
                    if (homeBlocks == null || !homeBlocks.Any())
                    {
                        results.Add(new { id = page.Id, error = $"Home block grid ({culture}) yok veya boş." });
                        continue;
                    }
                    var firstBlock = homeBlocks.FirstOrDefault();
                    var firstTitle = firstBlock?.Content?.Value<string>("title", culture);
                    
                    // Eğer title yoksa representativeName kullan
                    string? titleToUse = firstTitle;
                    if (string.IsNullOrEmpty(firstTitle))
                    {
                        var representativeName = firstBlock?.Content?.Value<string>("representativeName", culture);
                        if (!string.IsNullOrEmpty(representativeName))
                        {
                            titleToUse = representativeName;
                        }
                        else
                        {
                            results.Add(new { id = page.Id, error = $"İlk blockta title ve representativeName ({culture}) yok." });
                            continue;
                        }
                    }
                    
                    // HTML taglarını temizle
                    var cleanTitle = CleanHtmlTags(titleToUse);
                    if (string.IsNullOrEmpty(cleanTitle))
                    {
                        results.Add(new { id = page.Id, error = $"Title HTML temizlendikten sonra boş kaldı." });
                        continue;
                    }
                    
                    var contentToUpdate = contentService.GetById(page.Id);
                    if (contentToUpdate == null)
                    {
                        results.Add(new { id = page.Id, error = "Güncellenecek içerik yok." });
                        continue;
                    }
                    contentToUpdate.SetValue("title", cleanTitle, culture);
                    contentService.Save(contentToUpdate);
                    var cultures = new[] { culture };
                    contentService.Publish(contentToUpdate, cultures, 0);
                    results.Add(new { id = page.Id, searchTitle = cleanTitle, culture });
                }
                catch (Exception ex)
                {
                    results.Add(new { id = page.Id, error = ex.Message });
                }
            }
            return Ok(results);
        }

        /// <summary>
        /// Sitemap cache'lerini temizler (XML ve JSON)
        /// </summary>
        [HttpPost("umbraco/api/ContentApi/ClearSitemapCache")]
        public IActionResult ClearSitemapCache()
        {
            try
            {
                var clearedCount = 0;
                
                // XML sitemap cache'ini temizle
                _memoryCache.Remove(SITEMAP_XML_CACHE_KEY);
                clearedCount++;
                _logger.LogInformation("[CACHE CLEARED] XML Sitemap cache cleared");
                
                // JSON sitemap cache'lerini temizle (tr ve en)
                var langs = new[] { "tr", "en" };
                foreach (var lang in langs)
                {
                    var cacheKey = $"{SITEMAP_JSON_CACHE_KEY_PREFIX}{lang}";
                    _memoryCache.Remove(cacheKey);
                    clearedCount++;
                    _logger.LogInformation("[CACHE CLEARED] JSON Sitemap cache cleared for lang: {Lang}", lang);
                }
                
                return Ok(new 
                { 
                    message = "Sitemap cache'leri temizlendi", 
                    clearedCount,
                    timestamp = DateTime.UtcNow 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ERROR] Error clearing sitemap cache: {Message}", ex.Message);
                return StatusCode(500, $"Cache temizlenirken hata: {ex.Message}");
            }
        }

        /// <summary>
        /// Navigation cache'lerini temizler (HeaderNav ve FooterNavbar)
        /// </summary>
        [HttpPost("umbraco/api/ContentApi/ClearNavigationCache")]
        public IActionResult ClearNavigationCache([FromQuery] string? alias = null, [FromQuery] string? culture = null)
        {
            try
            {
                var clearedCaches = new List<string>();
                var cultures = new[] { "tr-TR", "en" }; // Desteklenen kültürler

                if (!string.IsNullOrEmpty(alias) && !string.IsNullOrEmpty(culture))
                {
                    // Specific cache clear
                    var cacheKey = alias.Equals("headerNav", StringComparison.OrdinalIgnoreCase) 
                        ? $"{HEADER_NAV_CACHE_KEY_PREFIX}{alias}_{culture}"
                        : $"{FOOTER_NAV_CACHE_KEY_PREFIX}{alias}_{culture}";
                    
                    _memoryCache.Remove(cacheKey);
                    clearedCaches.Add(cacheKey);
                }
                else
                {
                    // Clear all navigation caches
                    var navigationAliases = new[] { "headerNav", "footerNavbar" };
                    
                    foreach (var navAlias in navigationAliases)
                    {
                        foreach (var cultureCode in cultures)
                        {
                            var headerCacheKey = $"{HEADER_NAV_CACHE_KEY_PREFIX}{navAlias}_{cultureCode}";
                            var footerCacheKey = $"{FOOTER_NAV_CACHE_KEY_PREFIX}{navAlias}_{cultureCode}";
                            
                            _memoryCache.Remove(headerCacheKey);
                            _memoryCache.Remove(footerCacheKey);
                            
                            clearedCaches.Add(headerCacheKey);
                            clearedCaches.Add(footerCacheKey);
                        }
                    }
                }

                _logger.LogInformation("[CACHE CLEAR] Navigation cache cleared. Keys: {CacheKeys}", 
                    string.Join(", ", clearedCaches));

                return Ok(new 
                { 
                    success = true, 
                    message = "Navigation cache başarıyla temizlendi",
                    clearedCaches = clearedCaches,
                    clearedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ERROR] Navigation cache clearing failed");
                return StatusCode(500, $"Cache temizlenirken hata: {ex.Message}");
            }
        }

        /// <summary>
        /// Cache durumunu kontrol eder
        /// </summary>
        [HttpGet("umbraco/api/ContentApi/CacheStatus")]
        public IActionResult GetCacheStatus()
        {
            try
            {
                var cacheStatus = new List<object>();
                var cultures = new[] { "tr-TR", "en" };
                var navigationAliases = new[] { "headerNav", "footerNavbar" };

                foreach (var alias in navigationAliases)
                {
                    foreach (var culture in cultures)
                    {
                        var headerCacheKey = $"{HEADER_NAV_CACHE_KEY_PREFIX}{alias}_{culture}";
                        var footerCacheKey = $"{FOOTER_NAV_CACHE_KEY_PREFIX}{alias}_{culture}";

                        var keys = new[] { headerCacheKey, footerCacheKey };
                        
                        foreach (var key in keys)
                        {
                            var isCached = _memoryCache.TryGetValue(key, out var cachedValue);
                            DateTime? cachedAt = null;
                            
                            if (isCached && cachedValue != null)
                            {
                                // Try to get CachedAt property using reflection
                                var cachedType = cachedValue.GetType();
                                var cachedAtProperty = cachedType.GetProperty("CachedAt");
                                if (cachedAtProperty != null)
                                {
                                    cachedAt = cachedAtProperty.GetValue(cachedValue) as DateTime?;
                                }
                            }
                            
                            cacheStatus.Add(new
                            {
                                key = key,
                                alias = alias,
                                culture = culture,
                                isCached = isCached,
                                cachedAt = cachedAt
                            });
                        }
                    }
                }

                return Ok(new
                {
                    cacheStatus = cacheStatus,
                    cacheDuration = _navigationCacheDuration,
                    checkedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ERROR] Cache status check failed");
                return StatusCode(500, $"Cache durumu kontrol edilirken hata: {ex.Message}");
            }
        }

        /// <summary>
        /// Belirli bir content type'ın cache'ini temizler
        /// </summary>
        /// <param name="contentTypeAlias">headerNav veya footerNavbar</param>
        private void ClearNavigationCacheForContentType(string contentTypeAlias)
        {
            var cultures = new[] { "tr-TR", "en" };
            var clearedKeys = new List<string>();

            foreach (var culture in cultures)
            {
                string cacheKey;
                if (contentTypeAlias.Equals("headerNav", StringComparison.OrdinalIgnoreCase))
                {
                    cacheKey = $"{HEADER_NAV_CACHE_KEY_PREFIX}{contentTypeAlias}_{culture}";
                }
                else if (contentTypeAlias.Equals("footerNavbar", StringComparison.OrdinalIgnoreCase))
                {
                    cacheKey = $"{FOOTER_NAV_CACHE_KEY_PREFIX}{contentTypeAlias}_{culture}";
                }
                else
                {
                    continue; // Skip non-navigation content types
                }

                _memoryCache.Remove(cacheKey);
                clearedKeys.Add(cacheKey);
            }

            if (clearedKeys.Any())
            {
                _logger.LogInformation("[CACHE INVALIDATION] Navigation cache cleared for content type: {ContentType}, Keys: {Keys}", 
                    contentTypeAlias, string.Join(", ", clearedKeys));
            }
        }

        /// <summary>
        /// Navigation cache invalidation - bu method notification handler tarafından çağrılabilir
        /// </summary>
        /// <param name="contentTypeAlias">Content type alias</param>
        public void InvalidateNavigationCache(string? contentTypeAlias = null)
        {
            try
            {
                if (string.IsNullOrEmpty(contentTypeAlias))
                {
                    // Clear all navigation caches
                    ClearNavigationCacheForContentType("headerNav");
                    ClearNavigationCacheForContentType("footerNavbar");
                }
                else if (contentTypeAlias.Equals("headerNav", StringComparison.OrdinalIgnoreCase) ||
                         contentTypeAlias.Equals("footerNavbar", StringComparison.OrdinalIgnoreCase))
                {
                    ClearNavigationCacheForContentType(contentTypeAlias);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ERROR] Navigation cache invalidation failed for content type: {ContentType}", contentTypeAlias);
            }
        }
    }
}