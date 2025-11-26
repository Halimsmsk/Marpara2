using Microsoft.Extensions.Logging;
using Morpara.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common;
using Umbraco.Extensions;

namespace Morpara.Services
{
    public class QuestionCategoriesService : IQuestionCategoriesService
    {
        private readonly IUmbracoContextAccessor _umbracoContextAccessor;
        private readonly UmbracoHelper _umbracoHelper;
        private readonly ILogger<QuestionCategoriesService> _logger;

        public QuestionCategoriesService(
            IUmbracoContextAccessor umbracoContextAccessor,
            UmbracoHelper umbracoHelper,
            ILogger<QuestionCategoriesService> logger)
        {
            _umbracoContextAccessor = umbracoContextAccessor;
            _umbracoHelper = umbracoHelper;
            _logger = logger;
        }

        public string? GetCategoryUrlSegment(string categoryAlias, string culture)
        {
            try
            {
                var categoryContent = FindCategoryByAlias(categoryAlias);
                if (categoryContent == null)
                {
                    return null;
                }

                var categoryUrl = categoryContent.Url(culture);

                if (!string.IsNullOrEmpty(categoryUrl) && !categoryUrl.Contains("#"))
                {
                    var urlParts = categoryUrl.Trim('/').Split('/');
                    if (urlParts.Length > 0)
                    {
                        return urlParts.Last();
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting URL segment for alias '{CategoryAlias}' in culture '{Culture}': {Message}", categoryAlias, culture, ex.Message);
                return null;
            }
        }

        public IPublishedContent? FindCategoryByAlias(string categoryAlias)
        {
            try
            {
                var umbracoContext = _umbracoContextAccessor.GetRequiredUmbracoContext();

                var allContentAtRoot = umbracoContext.Content?.GetAtRoot();
                if (allContentAtRoot == null)
                {
                    return null;
                }

                var allCategories = allContentAtRoot
                    .SelectMany(x => x.DescendantsOrSelf())
                    .Where(x => x.ContentType.Alias == "category");

                var category = allCategories.FirstOrDefault(c =>
                {
                    var urlSegment = c.UrlSegment?.ToLowerInvariant() ?? "";
                    return urlSegment == categoryAlias.ToLowerInvariant();
                });

                return category;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding category by alias '{CategoryAlias}': {Message}", categoryAlias, ex.Message);
                return null;
            }
        }

        public string GetEnglishCategorySegment(string turkishCategoryAlias)
        {
            try
            {
                var categoryContent = FindCategoryByAlias(turkishCategoryAlias);
                if (categoryContent == null)
                {
                    return turkishCategoryAlias;
                }

                var englishSegment = GetCategoryUrlSegment(turkishCategoryAlias, "en");
                if (!string.IsNullOrEmpty(englishSegment))
                {
                    return englishSegment;
                }

                if (categoryContent.HasProperty("englishName") && categoryContent.HasValue("englishName"))
                {
                    var englishName = categoryContent.Value<string>("englishName");
                    if (!string.IsNullOrEmpty(englishName))
                    {
                        var englishSlug = englishName.ToLowerInvariant()
                            .Replace(" ", "-")
                            .Replace("ç", "c")
                            .Replace("ğ", "g")
                            .Replace("ı", "i")
                            .Replace("ö", "o")
                            .Replace("ş", "s")
                            .Replace("ü", "u")
                            .Replace("İ", "i");
                        
                        return englishSlug;
                    }
                }

                var translatedSegment = GetBasicTranslation(turkishCategoryAlias);
                return translatedSegment;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting English segment for '{TurkishAlias}': {Message}", turkishCategoryAlias, ex.Message);
                return turkishCategoryAlias;
            }
        }

        private string GetBasicTranslation(string turkishSegment)
        {
            // Temel Türkçe-İngilizce çeviriler
            var translations = new Dictionary<string, string>
            {
                { "para-yukleme", "money-deposit" },
                { "para-cekme", "money-withdrawal" },
                { "para-gonderme", "money-transfer" },
                { "para-isteme", "money-request" },
                { "morpara-hesap", "morpara-account" },
                { "morpara-card", "morpara-card" },
                { "morpara-app", "morpara-app" },
                { "morpara-kampanyalari", "morpara-campaigns" },
                { "morpara-profilim", "my-morpara-profile" },
                { "gizlilik-ve-guvenlik", "privacy-and-security" },
                { "limitler", "limits" },
                { "abonelikler-ve-odemeler", "subscriptions-and-payments" },
                { "temsilcilik", "agency" },
                { "yurt-disi-para-transferi", "international-money-transfer" },
                { "yurt-disi-hesap-euro-iban", "international-account-euro-iban" },
                { "morpos-sanal-pos", "morpos-virtual-pos" },
                { "genel", "general" },
                { "kurumsal", "corporate" }
            };

            return translations.TryGetValue(turkishSegment.ToLowerInvariant(), out var translation) 
                ? translation 
                : turkishSegment;
        }
    }
}
