using Morpara.Services.Interfaces;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;
using Microsoft.Extensions.Logging;

namespace Morpara.Services
{
    public class MorparaCultureService : IMorparaCultureService
    {
        private readonly IUmbracoContextAccessor _ctxAccessor;
        private readonly ILanguageService _languageService;
        private readonly IVariationContextAccessor _variation;
        private readonly ILogger<MorparaCultureService> _logger;

        public MorparaCultureService(
            IUmbracoContextAccessor ctxAccessor,
            ILanguageService languageService,
            IVariationContextAccessor variation,
            ILogger<MorparaCultureService> logger)
        {
            _ctxAccessor = ctxAccessor;
            _languageService = languageService;
            _variation = variation;
            _logger = logger;
            _languageService = languageService;
            _variation = variation;
        }

    public Dictionary<string, object> GetAlternativeCulturesById(int contentId, string? categoryName = null, string? activeContentId = null)
    {
        _logger.LogInformation("[DEBUG GetAltCultures] Called with contentId: {ContentId}, categoryName: {CategoryName}, activeContentId: {ActiveContentId}", contentId, categoryName, activeContentId);            var result = new Dictionary<string, object>();
            if (!_ctxAccessor.TryGetUmbracoContext(out var ctx))
                return result;

            // Önce ana içeriği bul
            var mainContent = ctx.Content?.GetById(contentId);
            if (mainContent == null)
                return result;

            var key = mainContent.Key;
            var languages = _languageService.GetAllAsync().Result;

            foreach (var lang in languages)
            {
                var isoCode = lang.IsoCode;
                IPublishedContent cultureContent = null;
                string finalUrl = null;

                // 1. Önce ana içerikte bu kültür var mı bak
                if (mainContent.Cultures != null && mainContent.Cultures.ContainsKey(isoCode))
                {
                    cultureContent = mainContent;

                    // URL alma stratejileri
                    var url = cultureContent.Url(isoCode);

                    // Eğer URL boş veya # ise alternatif yöntemler dene
                    if (string.IsNullOrEmpty(url) || url == "#")
                    {
                        // Alternatif 1: VariationContext ile dene
                        var originalContext = _variation.VariationContext;
                        try
                        {
                            _variation.VariationContext = new VariationContext(isoCode);
                            url = cultureContent.Url();
                        }
                        finally
                        {
                            _variation.VariationContext = originalContext;
                        }
                    }

                    // Hala boş ise manuel URL oluştur
                    if (string.IsNullOrEmpty(url) || url == "#")
                    {
                        var urlSegment = cultureContent.Value<string>("umbracoUrlName", isoCode) ??
                                         cultureContent.Name?.ToLowerInvariant().Replace(" ", "-");

                        if (!string.IsNullOrEmpty(urlSegment))
                        {
                            // Parent URL'ini al
                            var parentUrl = cultureContent.Parent?.Url(isoCode) ?? "";
                            if (parentUrl == "#") parentUrl = "";

                            finalUrl = $"/{isoCode}{parentUrl.TrimEnd('/')}/{urlSegment}/".Replace("//", "/");
                        }
                    }
                    else
                    {
                        finalUrl = url;
                    }
                }
                else
                {
                    // 2. Yoksa, aynı Key'e sahip olanı o kültürde bul
                    var allContent = ctx.Content.GetAtRoot(isoCode)
                        .SelectMany(root => root.DescendantsOrSelf());
                    cultureContent = allContent.FirstOrDefault(x =>
                        x.Key == key && x.Cultures != null && x.Cultures.ContainsKey(isoCode));

                    if (cultureContent != null)
                    {
                        finalUrl = cultureContent.Url(isoCode);

                        if (string.IsNullOrEmpty(finalUrl) || finalUrl == "#")
                        {
                            var urlSegment = cultureContent.Value<string>("umbracoUrlName", isoCode) ??
                                             cultureContent.Name?.ToLowerInvariant().Replace(" ", "-");

                            if (!string.IsNullOrEmpty(urlSegment))
                            {
                                var parentUrl = cultureContent.Parent?.Url(isoCode) ?? "";
                                if (parentUrl == "#") parentUrl = "";

                                finalUrl = $"/{isoCode}{parentUrl.TrimEnd('/')}/{urlSegment}/".Replace("//", "/");
                            }
                        }
                    }
                }

                if (cultureContent != null)
                {
                    // Eğer category name varsa URL'e ekle
                    if (!string.IsNullOrEmpty(categoryName) && !string.IsNullOrEmpty(finalUrl) && finalUrl != "#")
                    {
                        _logger.LogInformation("[DEBUG GetAltCultures] Processing category '{CategoryName}' for culture: {Culture}", categoryName, isoCode);
                        
                        // categoryName'i kullanarak o culture'daki category'yi bul
                        var categoryInCulture = ctx.Content?.GetAtRoot()
                            .SelectMany(root => root.DescendantsOfType("category"))
                            .FirstOrDefault(cat => cat.UrlSegment(isoCode) == categoryName || 
                                                   cat.UrlSegment() == categoryName ||
                                                   cat.Name(isoCode)?.ToLowerInvariant() == categoryName.ToLowerInvariant());
                        
                        if (categoryInCulture != null)
                        {
                            var categoryUrlSegment = categoryInCulture.UrlSegment(isoCode) ?? categoryInCulture.Name(isoCode);
                            _logger.LogInformation("[DEBUG GetAltCultures] Category UrlSegment for {Culture}: {Segment}", isoCode, categoryUrlSegment);
                            
                            if (!string.IsNullOrEmpty(categoryUrlSegment))
                            {
                                finalUrl = finalUrl.TrimEnd('/') + "/" + categoryUrlSegment + "/";
                                _logger.LogInformation("[DEBUG GetAltCultures] Final URL with category for {Culture}: {Url}", isoCode, finalUrl);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("[DEBUG GetAltCultures] Category not found with name: {CategoryName} for culture: {Culture}", categoryName, isoCode);
                        }
                    }
                    else
                    {
                        _logger.LogInformation("[DEBUG GetAltCultures] Skipping category - categoryName: {CategoryName}, finalUrl: {Url}", categoryName, finalUrl);
                    }
                    
                    // Eğer activeContentId varsa URL'ye ekle (detay sayfası)
                    if (!string.IsNullOrEmpty(activeContentId) && !string.IsNullOrEmpty(finalUrl) && finalUrl != "#")
                    {
                        _logger.LogInformation("[DEBUG GetAltCultures] Processing activeContentId '{ActiveContentId}' for culture: {Culture}", activeContentId, isoCode);
                        
                        // activeContentId'yi kullanarak o culture'daki içeriği bul
                        var activeContent = ctx.Content?.GetAtRoot()
                            .SelectMany(root => root.DescendantsOrSelf())
                            .FirstOrDefault(c => c.UrlSegment(isoCode) == activeContentId || 
                                                 c.UrlSegment() == activeContentId ||
                                                 c.Name(isoCode)?.ToLowerInvariant() == activeContentId.ToLowerInvariant());
                        
                        if (activeContent != null)
                        {
                            var activeContentUrlSegment = activeContent.UrlSegment(isoCode) ?? activeContent.Name(isoCode);
                            _logger.LogInformation("[DEBUG GetAltCultures] ActiveContent UrlSegment for {Culture}: {Segment}", isoCode, activeContentUrlSegment);
                            
                            if (!string.IsNullOrEmpty(activeContentUrlSegment))
                            {
                                finalUrl = finalUrl.TrimEnd('/') + "/" + activeContentUrlSegment + "/";
                                _logger.LogInformation("[DEBUG GetAltCultures] Final URL with activeContent for {Culture}: {Url}", isoCode, finalUrl);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("[DEBUG GetAltCultures] ActiveContent not found with id: {ActiveContentId} for culture: {Culture}", activeContentId, isoCode);
                        }
                    }
                    
                    result[isoCode] = new
                    {
                        path = !string.IsNullOrEmpty(finalUrl) && finalUrl != "#" ? finalUrl : null
                    };
                }
                else
                {
                    result[isoCode] = new { id = (int?)null, path = (string)null };
                }
            }

            return result;
        }

        public async Task<IEnumerable<object>> GetAllLanguagesAsync()
        {
            var languages = await _languageService.GetAllAsync();
            return languages.Select(lang => new
            {
                Id = lang.Id,
                IsoCode = lang.IsoCode,
                CultureName = lang.CultureName,
                IsDefault = lang.IsDefault
            });
        }
    }
}
