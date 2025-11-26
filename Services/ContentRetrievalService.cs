using Microsoft.AspNetCore.Mvc;
using Morpara.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;
using Umbraco.Cms.Core;
using Microsoft.Extensions.Logging;

namespace Morpara.Services
{
    public class ContentRetrievalService : IContentRetrievalService
    {
        private readonly IUmbracoContextAccessor _ctxAccessor;
        private readonly IPublishedContentQuery _contentQuery;
        private readonly IVariationContextAccessor _variation;
        private readonly ILogger<ContentRetrievalService> _logger;

        public ContentRetrievalService(
            IUmbracoContextAccessor ctxAccessor,
            IPublishedContentQuery contentQuery,
            IVariationContextAccessor variation,
            ILogger<ContentRetrievalService> logger)
        {
            _ctxAccessor = ctxAccessor;
            _contentQuery = contentQuery;
            _variation = variation;
            _logger = logger;
        }

        public IPublishedContent GetContentById(string id)
        {
            if (!_ctxAccessor.TryGetUmbracoContext(out var ctx))
                return null;

            return Guid.TryParse(id, out var g)
                ? ctx.Content?.GetById(false, g)
                : int.TryParse(id, out var i)
                    ? ctx.Content?.GetById(i)
                    : null;
        }

        public IPublishedContent GetContentByAlias(string alias, string culture)
        {
            if (!_ctxAccessor.TryGetUmbracoContext(out var ctx))
                return null;

            // Set culture context
            _variation.VariationContext = new VariationContext(culture);

            IPublishedContent content = null;

            // 1. Root seviyesinde alias'ı kontrol et
            var rootContents = ctx.Content.GetAtRoot(culture);
            content = rootContents.FirstOrDefault(c =>
                string.Equals(c.ContentType.Alias, alias, StringComparison.OrdinalIgnoreCase));

            // 2. Root seviyesinde bulunamazsa, tüm descendants'larda ara
            if (content == null)
            {
                content = rootContents
                    .SelectMany(root => root.Descendants())
                    .FirstOrDefault(c =>
                        string.Equals(c.ContentType.Alias, alias, StringComparison.OrdinalIgnoreCase));
            }

            // 3. Hala bulunamazsa, node name'e göre ara (footer, header gibi isimler için)
            if (content == null)
            {
                content = rootContents
                    .SelectMany(root => root.DescendantsOrSelf())
                    .FirstOrDefault(c =>
                        string.Equals(c.Name, alias, StringComparison.OrdinalIgnoreCase));
            }

            // 4. Son olarak URL segment'e göre ara
            if (content == null)
            {
                content = rootContents
                    .SelectMany(root => root.DescendantsOrSelf())
                    .FirstOrDefault(c =>
                    {
                        var urlSegment = c.Value<string>("umbracoUrlName") ?? c.Name.ToLowerInvariant();
                        return string.Equals(urlSegment, alias, StringComparison.OrdinalIgnoreCase);
                    });
            }

            return content;
        }

        public (IPublishedContent content, string categoryName) GetContentByUrl(string url, string culture, bool preview = false)
        {
            if (!_ctxAccessor.TryGetUmbracoContext(out var ctx))
                return (null, null);

            // Set culture context
            _variation.VariationContext = new VariationContext(culture);

            // URL'yi normalize et
            var normalized = (url ?? "/").Trim('/');
            var route = string.IsNullOrEmpty(normalized) ? "/" : "/" + normalized;

            IPublishedContent content = null;
            string categoryName = null;

            // 1. Doğrudan _contentQuery ile dene
            content = _contentQuery.Content(route);

            // 2. Umbraco Context ile dene (kültürsüz URL'yi kullan)
            if (content == null)
            {
                content = ctx.Content.GetByRoute(preview, route, null, culture);
            }

            // 3. Hala bulunamadıysa root içeriklerden ara
            if (content == null && (route == "/" || string.IsNullOrEmpty(route)))
            {
                content = ctx.Content.GetAtRoot(culture).FirstOrDefault();
            }

            // 4. Kategori bazlı URL için kontrol et
            if (content == null)
            {
                var result = FindContentWithCategory(normalized, culture, preview);
                content = result.content;
                categoryName = result.categoryName;
            }

            return (content, categoryName);
        }

        private (IPublishedContent content, string categoryName) FindContentWithCategory(string normalized, string culture, bool preview)
        {
            _logger.LogInformation("FindContentWithCategory called with normalized={Normalized}, culture={Culture}, preview={Preview}", normalized, culture, preview);
            
            if (!_ctxAccessor.TryGetUmbracoContext(out var ctx))
            {
                _logger.LogWarning("Could not get Umbraco context in FindContentWithCategory");
                return (null, null);
            }

            var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            _logger.LogInformation("URL segments: [{Segments}], Length: {Count}", string.Join(", ", segments), segments.Length);
            
            IPublishedContent content = null;
            string categoryName = null;

            // 2 veya daha fazla segment varsa kategori çıkarımı yap
            // 2 segment: sikca-sorulan-sorular/limitler (kategori: limitler, sayfa: sikca-sorulan-sorular)
            // 3+ segment: sikca-sorulan-sorular/morpos-sanal-pos/sanal-pos-nedir (kategori: morpos-sanal-pos)
            if (segments.Length >= 2)
            {
                if (segments.Length == 2)
                {
                    // 2 segment durumu: ikinci segment kategori, birinci segment sayfa
                    categoryName = segments[1]; // "limitler"
                    _logger.LogInformation("2 segments detected - categoryName from segments[1]: {CategoryName}", categoryName);

                    // Ana sayfayı ara (/sikca-sorulan-sorular/)
                    var basePage = $"/{segments[0]}/";
                    _logger.LogInformation("Constructed basePage URL for 2 segments: {BasePageUrl}", basePage);

                    content = _contentQuery.Content(basePage);
                    _logger.LogInformation("ContentQuery result for {BasePageUrl}: {ContentFound}", basePage, content != null ? $"Found: {content.Name}" : "Not found");

                    if (content == null)
                    {
                        content = ctx.Content.GetByRoute(preview, basePage, null, culture);
                        _logger.LogInformation("GetByRoute result for {BasePageUrl}: {ContentFound}", basePage, content != null ? $"Found: {content.Name}" : "Not found");
                    }
                }
                else if (segments.Length >= 3)
                {
                    // 3+ segment durumu: ikinci segment kategori olabilir
                    categoryName = segments[1]; // İkinci segment kategori olabilir
                    _logger.LogInformation("3+ segments detected - categoryName from segments[1]: {CategoryName}", categoryName);

                    // Kategori olmadan URL oluştur (/sikca-sorulan-sorular/sanal-pos-nedir/)
                    var withoutCategory = $"/{segments[0]}/{segments[segments.Length - 1]}/";
                    _logger.LogInformation("Constructed withoutCategory URL: {WithoutCategoryUrl}", withoutCategory);

                    // Yeni URL ile içerik ara
                    content = _contentQuery.Content(withoutCategory);
                    _logger.LogInformation("ContentQuery result for {WithoutCategoryUrl}: {ContentFound}", withoutCategory, content != null ? $"Found: {content.Name}" : "Not found");

                    if (content == null)
                    {
                        content = ctx.Content.GetByRoute(preview, withoutCategory, null, culture);
                        _logger.LogInformation("GetByRoute result for {WithoutCategoryUrl}: {ContentFound}", withoutCategory, content != null ? $"Found: {content.Name}" : "Not found");
                    }
                }
            }

            // Son segment ile arama yapalım
            if (content == null && segments.Length > 0)
            {
                var lastSegment = segments.Last();
                _logger.LogInformation("Content not found with category URL, searching by last segment: {LastSegment}", lastSegment);

                // Tüm içerikler arasında son segment ile eşleşen içeriği ara
                var allContent = ctx.Content.GetAtRoot()
                    .SelectMany(root => root.Descendants())
                    .Where(c =>
                    {
                        // ✅ Culture check: Only include content available in the requested culture
                        if (!c.HasCulture(culture))
                        {
                            _logger.LogDebug("Skipping {ContentName} (ID: {ContentId}) - culture {Culture} not available", c.Name, c.Id, culture);
                            return false;
                        }
                        
                        var urlName = c.Value<string>("umbracoUrlName") ?? c.Name?.ToLowerInvariant().Replace(" ", "-");
                        return string.Equals(urlName, lastSegment, StringComparison.OrdinalIgnoreCase);
                    });

                var candidateCount = allContent.Count();
                _logger.LogInformation("Found {CandidateCount} candidates matching last segment {LastSegment} with culture {Culture}", candidateCount, lastSegment, culture);

                foreach (var candidate in allContent)
                {
                    _logger.LogInformation("Checking candidate: {CandidateName} (ID: {CandidateId}), categoryName is empty: {CategoryNameEmpty}", 
                        candidate.Name, candidate.Id, string.IsNullOrEmpty(categoryName));
                    
                    if (string.IsNullOrEmpty(categoryName)) // || HasCategory(candidate, categoryName) - will need to inject ICategoryService
                    {
                        content = candidate;
                        _logger.LogInformation("Selected candidate: {ContentName} (ID: {ContentId})", candidate.Name, candidate.Id);
                        break;
                    }
                }
            }

            _logger.LogInformation("FindContentWithCategory returning - Content: {ContentFound}, CategoryName: {CategoryName}", 
                content != null ? $"{content.Name} (ID: {content.Id})" : "null", categoryName ?? "null");
            
            return (content, categoryName);
        }

        public IPublishedContent FindContentRecursively(IPublishedContent node, string contentTypeAlias, string name)
        {
            if (string.Equals(node.ContentType.Alias, contentTypeAlias, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(node.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            foreach (var child in node.Children)
            {
                var found = FindContentRecursively(child, contentTypeAlias, name);
                if (found != null)
                    return found;
            }

            return null;
        }

        public IPublishedContent FindContentByPathStructure(IPublishedContent node, string[] pathSegments, int currentIndex)
        {
            if (currentIndex >= pathSegments.Length)
                return node;

            var targetSegment = pathSegments[currentIndex];

            foreach (var child in node.Children)
            {
                var childUrl = child.Url();
                var childSegments = childUrl.Trim('/').Split('/');
                var lastSegment = childSegments.LastOrDefault();

                if (string.Equals(lastSegment, targetSegment, StringComparison.OrdinalIgnoreCase))
                {
                    return FindContentByPathStructure(child, pathSegments, currentIndex + 1);
                }
            }

            return null;
        }

        public IPublishedContent FindContentByUrl(IPublishedContent content, string url, string culture)
        {
            var contentUrl = content.Url(culture);
            if (contentUrl.Equals(url, StringComparison.OrdinalIgnoreCase) ||
                contentUrl.Equals($"/{culture}{url}", StringComparison.OrdinalIgnoreCase))
            {
                return content;
            }

            foreach (var child in content.Children)
            {
                var found = FindContentByUrl(child, url, culture);
                if (found != null)
                    return found;
            }

            return null;
        }

        public List<object> GetShapedChildren(IPublishedContent item)
        {
            return item.Children().Select(child => new
            {
                Key = child.Key,
                Name = child.Name,
                Url = child.Url(),
                ContentType = child.ContentType.Alias,
                Properties = child.Properties.ToDictionary(
                    p => p.Alias,
                    p => p.GetValue()),
                Cultures = child.Cultures.ToDictionary(c => c.Key, c => child.Url(c.Key))
            }).Cast<object>().ToList();
        }
    }
}
