using Morpara.Services.Interfaces;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace Morpara.Services
{
    public class MorparaCultureService : IMorparaCultureService
    {
        private readonly IUmbracoContextAccessor _ctxAccessor;
        private readonly ILanguageService _languageService;
        private readonly IVariationContextAccessor _variation;

        public MorparaCultureService(
            IUmbracoContextAccessor ctxAccessor,
            ILanguageService languageService,
            IVariationContextAccessor variation)
        {
            _ctxAccessor = ctxAccessor;
            _languageService = languageService;
            _variation = variation;
        }

        public Dictionary<string, object> GetAlternativeCulturesById(int contentId, int? categoryId = null)
        {
            var result = new Dictionary<string, object>();
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
                    // Eğer category varsa culture'a göre URL'sini ekle
                    if (categoryId.HasValue && !string.IsNullOrEmpty(finalUrl) && finalUrl != "#")
                    {
                        var categoryContent = ctx.Content?.GetById(categoryId.Value);
                        if (categoryContent != null)
                        {
                            // Her culture için category'nin kendi UrlSegment'ini al
                            var categoryUrlSegment = categoryContent.UrlSegment(isoCode) ?? categoryContent.Name(isoCode);
                            if (!string.IsNullOrEmpty(categoryUrlSegment))
                            {
                                finalUrl = finalUrl.TrimEnd('/') + "/" + categoryUrlSegment + "/";
                            }
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
