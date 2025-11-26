using System;
using System.Collections.Generic;
using System.Linq;
using Examine;
using Examine.Search;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Extensions;
using Umbraco.Cms.Core.Models.Blocks;

namespace Umbraco.Services
{
    public class SearchService : ISearchService
    {
        private readonly IExamineManager _examineManager;
        private readonly IUmbracoContextFactory _contextFactory;
        private readonly ILogger<SearchService> _logger;
        private readonly ILocalizationService _localizationService;

        public SearchService(
            IExamineManager examineManager,
            IUmbracoContextFactory contextFactory,
            ILogger<SearchService> logger,
            ILocalizationService localizationService)
        {
            _examineManager = examineManager;
            _contextFactory = contextFactory;
            _logger = logger;
            _localizationService = localizationService;
        }

        public SearchResultModel QueryUmbraco(string searchTerm, int pageSize, int currentPage = 1, string lang = "tr-TR", string type = "")
        {
            var model = new SearchResultModel();

            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                _logger.LogWarning("Empty search term provided");
                return model;
            }

            // İndeksleri ve arama işlemini yapılandır
            var allIds = new HashSet<string>();

            // Dil kodlarını hazırla - SADECE belirtilen dilde arama yap
            var languagesToSearch = new List<string>();

            // Sadece istenilen dili ekle
            if (!string.IsNullOrEmpty(lang))
            {
                languagesToSearch.Add(lang);

                // Dil kodunun alt varyasyonlarını ekle (örn: tr-TR ise tr'yi de ekle)
                if (lang.Contains("-"))
                {
                    languagesToSearch.Add(lang.Split('-')[0]);
                }
            }
            else
            {
                // Hiç dil belirtilmemişse varsayılan tr-TR kullan
                languagesToSearch.Add("tr-TR");
                languagesToSearch.Add("tr");
            }

            _logger.LogInformation("Searching with language variants: {Languages}", string.Join(", ", languagesToSearch));
            _logger.LogInformation("Content type filter: {Type}", !string.IsNullOrEmpty(type) ? type : "all");

            // Eğer content type belirtilmemişse, varsayılan olarak "page" değerini kullan
            if (string.IsNullOrEmpty(type))
            {
                type = "page";
            }

            // Tüm indeksleri dene
            var indexNames = new[] {
        "ExternalIndex",
        "InternalIndex",
        "MemberIndex",
        "ContentIndex"
    };

            foreach (var indexName in indexNames)
            {
                try
                {
                    if (_examineManager.TryGetIndex(indexName, out IIndex? index))
                    {
                        _logger.LogInformation("Searching in index: {IndexName}", indexName);
                        var indexResults = SearchInIndex(index, searchTerm, languagesToSearch, type);

                        if (indexResults.Any())
                        {
                            _logger.LogInformation("Found {Count} results in index {IndexName}",
                                indexResults.Count(), indexName);

                            allIds.UnionWith(indexResults);
                        }
                        else
                        {
                            _logger.LogInformation("No results found in index {IndexName}", indexName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error searching in index {IndexName}", indexName);
                }
            }

            // Her zaman direkt içerik aramayı da yap (block grid'ler için)
            try
            {
                _logger.LogInformation("Running direct content search for block grids");
                var directResults = SearchInDirectContent(searchTerm, languagesToSearch, type);
                if (directResults.Any())
                {
                    _logger.LogInformation("Found {Count} additional results from direct content search", directResults.Count);
                    allIds.UnionWith(directResults);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during direct content search");
            }

            // Sonuç işleme
            var skip = (currentPage - 1) * pageSize;
            var take = pageSize;
            var pagedIds = allIds.Skip(skip).Take(take);

            // IDs'leri içeriklere dönüştür
            using var contextReference = _contextFactory.EnsureUmbracoContext();
            var contents = new List<IPublishedContent>();

            // İçerik tipleri için allowlist
            var allowedContentTypes = GetAllowedContentTypes(type);
            _logger.LogInformation("Filtering results by content types: {ContentTypes}", string.Join(", ", allowedContentTypes));

            foreach (var id in pagedIds)
            {
                // Doğrudan içerikleri bulmak için ID veya GUID'i kontrol et
                IPublishedContent? content = null;

                // Int ID mi kontrol et
                if (int.TryParse(id, out var nodeId))
                {
                    content = contextReference.UmbracoContext.Content?.GetById(nodeId);
                    if (content != null)
                    {
                        _logger.LogDebug("Found content by ID {Id}: {Name}", nodeId, content.Name);
                    }
                }

                // GUID olabilir mi kontrol et
                if (content == null && Guid.TryParse(id, out var guidId))
                {
                    content = contextReference.UmbracoContext.Content?.GetById(guidId);
                    if (content != null)
                    {
                        _logger.LogDebug("Found content by GUID {Id}: {Name}", guidId, content.Name);
                    }
                }

                if (content != null)
                {
                    // İçerik tipi kontrolü
                    if (allowedContentTypes.Contains(content.ContentType.Alias))
                    {
                        contents.Add(content);
                        _logger.LogDebug("Added content {Id} of type {ContentType} to results",
                            content.Id, content.ContentType.Alias);
                    }
                    else
                    {
                        _logger.LogDebug("Filtered out content {Id} with type {ContentType}",
                            content.Id, content.ContentType.Alias);
                    }
                }
                else
                {
                    _logger.LogWarning("Could not find content with ID {Id}", id);
                }
            }

            model.Results = contents;
            model.TotalCount = contents.Count; // Use filtered count instead of all IDs

            _logger.LogInformation(
                "Search completed for term '{SearchTerm}'. Found {TotalCount} filtered results.",
                searchTerm, model.TotalCount);

            return model;
        }

        // Add this new helper method to get allowed content types
        private HashSet<string> GetAllowedContentTypes(string typeFilter)
        {
            var allowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(typeFilter))
            {
                // Default to only page content types if no filter specified
                allowedTypes.Add("page");
                allowedTypes.Add("contentPage");
                allowedTypes.Add("homepage");
                allowedTypes.Add("category");
                return allowedTypes;
            }

            var typeFilters = typeFilter.Split(',', StringSplitOptions.RemoveEmptyEntries);

            foreach (var filter in typeFilters)
            {
                var trimmedFilter = filter.Trim();
                if (string.IsNullOrWhiteSpace(trimmedFilter)) continue;

                // Get content type aliases based on the filter
                var contentTypeAliases = GetContentTypeAliasesByType(trimmedFilter);
                foreach (var alias in contentTypeAliases)
                {
                    allowedTypes.Add(alias);
                }
            }

            return allowedTypes;
        }

        // Update GetContentTypeAliasesByType to be more comprehensive for page and category
        private string[] GetContentTypeAliasesByType(string type)
        {
            return type.ToLowerInvariant() switch
            {
                "blog" => new[] { "blogPost" },
                "faq" => new[] { "questionsContent", "supportContent" },
                "page" => new[] { "page", "contentPage", "homepage", "landingPage", "articlePage" },
                "category" => new[] { "category", "blogCategory", "productCategory", "faqCategory" },
                _ => new[] { type }
            };
        }
        private HashSet<string> SearchInIndex(IIndex index, string searchTerm, IEnumerable<string> languages, string type)
        {
            var results = new HashSet<string>();
            var processedTerm = ProcessSearchTerm(searchTerm);

            foreach (var language in languages)
            {
                try
                {
                    _logger.LogDebug("Searching index for language: {Language}", language);

                    // 1. Dil kodunu al (tr-TR => tr)
                    string langCode = language.Split('-')[0].ToLowerInvariant();

                    // 2. Alan adlarını belirle
                    var fieldsToSearch = new List<string>();

                    // Temel alanlar
                    fieldsToSearch.AddRange(new[] {
                // Düz alan adları
                "title", "name", "metaDescription", "nodeName", "keywords",
                "content", "bodyText", "summary", "__nodeName", "__IndexType",
                
                // Varyasyonlu alan adları
                $"title_{langCode}", $"name_{langCode}",
                $"metaDescription_{langCode}",
                $"nodeName_{langCode}", $"bodyText_{langCode}",
                
                // Umbraco 15 alan adları
                $"umbracoNaviHide", $"properties_title",
                $"properties_title_{langCode}", $"properties_metaDescription",
                $"properties_metaDescription_{langCode}"
            });

                    // Dil değişkenli alan adları
                    string langVariant = language.Replace("-", "_");
                    fieldsToSearch.AddRange(new[] {
                $"title_{langVariant}", $"name_{langVariant}",
                $"metaDescription_{langVariant}"
            });

                    // Her alan için ayrı sorgu oluştur ve OR ile birleştir
                    var searcher = index.Searcher;
                    IBooleanOperation? operation = null;

                    foreach (var field in fieldsToSearch)
                    {
                        try
                        {
                            if (operation == null)
                            {
                                operation = searcher.CreateQuery().Field(field, processedTerm);
                            }
                            else
                            {
                                operation = operation.Or().Field(field, processedTerm);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error adding field {Field} to query", field);
                        }
                    }

                    // Fallback: Managed query dene
                    if (operation == null)
                    {
                        operation = searcher.CreateQuery().ManagedQuery(processedTerm);
                    }

                    // İçerik türü filtresi ekle
                    if (!string.IsNullOrEmpty(type))
                    {
                        try
                        {
                            // Allowlist yaklaşımıyla içerik türlerini al
                            var allowedContentTypes = GetAllowedContentTypes(type);

                            // İçerik türü filtresi için ayrı bir sorgu oluştur
                            var typeQuery = searcher.CreateQuery();
                            IBooleanOperation? typeOperation = null;

                            foreach (var alias in allowedContentTypes)
                            {
                                if (typeOperation == null)
                                {
                                    typeOperation = typeQuery.Field("__NodeTypeAlias", alias);
                                }
                                else
                                {
                                    typeOperation = typeOperation.Or().Field("__NodeTypeAlias", alias);
                                }
                            }

                            // Type filtresi ve ana sorguyu AND ile birleştir
                            if (typeOperation != null && operation != null)
                            {
                                var typeResults = typeOperation.Execute();
                                var mainResults = operation.Execute();

                                var typeIds = typeResults.Select(r => r.Id).ToHashSet();
                                var mainIds = mainResults.Select(r => r.Id).ToHashSet();

                                var combinedIds = mainIds.Intersect(typeIds);
                                results.UnionWith(combinedIds);

                                _logger.LogInformation("Added {Count} results with content type filter for language {Lang}",
                                    combinedIds.Count(), language);

                                // Remove the continue statement to ensure we search all languages
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error applying content type filter");
                        }
                    }

                    // Sonuçları al (içerik türü filtresi uygulanamadıysa buraya gelecek)
                    if (operation != null)
                    {
                        try
                        {
                            var searchResults = operation.Execute();
                            var ids = searchResults.Select(x => x.Id);
                            results.UnionWith(ids);

                            _logger.LogInformation("Added {Count} results for language {Lang}",
                                ids.Count(), language);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error executing search for language {Lang}", language);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error searching in language {Language}", language);
                }
            }

            return results;
        }
        private HashSet<string> SearchInDirectContent(string searchTerm, IEnumerable<string> languages, string type)
        {
            var results = new HashSet<string>();
            searchTerm = searchTerm.ToLowerInvariant();

            try
            {
                using var contextReference = _contextFactory.EnsureUmbracoContext();
                var allContent = contextReference.UmbracoContext.Content?.GetAtRoot()
                    .SelectMany(x => x.DescendantsOrSelf());

                if (allContent == null) return results;

                // İçerik tipi için allowlist
                var allowedContentTypes = GetAllowedContentTypes(type);

                foreach (var content in allContent)
                {
                    // İçerik türü kontrolü
                    if (!allowedContentTypes.Contains(content.ContentType.Alias))
                    {
                        _logger.LogDebug("Direct search: Skipping content {Id} with type {ContentType} not in allowed types",
                            content.Id, content.ContentType.Alias);
                        continue;
                    }

                    // Dil kontrolü - sadece belirtilen kültürlerde mevcut olan içerikleri dahil et
                    bool hasContentInLanguage = false;
                    foreach (var language in languages)
                    {
                        try
                        {
                            var hasName = !string.IsNullOrEmpty(content.Name(language));
                            var hasTitle = !string.IsNullOrEmpty(content.Value<string>("title", language));
                            
                            if (hasName || hasTitle)
                            {
                                hasContentInLanguage = true;
                                break;
                            }
                        }
                        catch
                        {
                            // Dil varyasyonu yoksa geç
                            continue;
                        }
                    }

                    if (!hasContentInLanguage)
                    {
                        _logger.LogDebug("Direct search: Skipping content {Id} - no content in specified languages",
                            content.Id);
                        continue;
                    }

                    bool isMatch = false;

                    // Dil bazlı değerleri kontrol et
                    foreach (var language in languages)
                    {
                        // Temel özellikleri kontrol et
                        if (content.Name(language)?.ToLowerInvariant().Contains(searchTerm) == true ||
                            content.Value<string>("title", language)?.ToLowerInvariant().Contains(searchTerm) == true ||
                            content.Value<string>("metaDescription", language)?.ToLowerInvariant().Contains(searchTerm) == true)
                        {
                            isMatch = true;
                            break;
                        }

                        // Tüm özellikler üzerinde dön ve kontrol et
                        foreach (var property in content.Properties)
                        {
                            var value = property.GetValue(language)?.ToString();
                            if (!string.IsNullOrEmpty(value) &&
                                value.ToLowerInvariant().Contains(searchTerm))
                            {
                                isMatch = true;
                                break;
                            }
                        }

                        // Block grid içerisindeki block'ların title'larını kontrol et
                        if (!isMatch)
                        {
                            try
                            {
                                var homeBlockGrid = content.Value<BlockGridModel>("home");
                                if (homeBlockGrid != null)
                                {
                                    foreach (var block in homeBlockGrid)
                                    {
                                        // Block'un title property'sini kontrol et
                                        var blockTitle = block.Content?.Value<string>("title", language);
                                        if (!string.IsNullOrEmpty(blockTitle) &&
                                            blockTitle.ToLowerInvariant().Contains(searchTerm))
                                        {
                                            isMatch = true;
                                            _logger.LogDebug("Direct search: Found match in block title for content {Id}: {BlockTitle}",
                                                content.Id, blockTitle);
                                            break;
                                        }

                                        // Block'un diğer text property'lerini de kontrol et
                                        if (block.Content != null)
                                        {
                                            foreach (var blockProperty in block.Content.Properties)
                                            {
                                                var blockValue = blockProperty.GetValue(language)?.ToString();
                                                if (!string.IsNullOrEmpty(blockValue) &&
                                                    blockValue.ToLowerInvariant().Contains(searchTerm))
                                                {
                                                    isMatch = true;
                                                    _logger.LogDebug("Direct search: Found match in block property {PropertyAlias} for content {Id}",
                                                        blockProperty.Alias, content.Id);
                                                    break;
                                                }
                                            }
                                        }

                                        if (isMatch) break;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Error searching in block grid for content {Id}", content.Id);
                            }
                        }

                        if (isMatch) break;
                    }

                    if (isMatch)
                    {
                        _logger.LogDebug("Direct search: Adding matching content ID {Id}, type: {ContentType}",
                            content.Id, content.ContentType.Alias);
                        results.Add(content.Id.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing direct content search");
            }

            return results;
        }
        private string ProcessSearchTerm(string searchTerm)
        {
            searchTerm = searchTerm.Trim();

            // Ayrıştırıcılar: boşluk ve noktalama işaretleri
            var terms = searchTerm.Split(new[] { ' ', '.', ',', ';', ':', '-', '_', '/' }, StringSplitOptions.RemoveEmptyEntries);

            if (terms.Length == 0)
                return string.Empty;

            var processedTerms = new List<string>();

            foreach (var term in terms)
            {
                var cleanTerm = term.Trim();
                if (cleanTerm.Length <= 1) continue;

                // Lucene özel karakterlerini escape et
                cleanTerm = cleanTerm
                    .Replace("\"", "\\\"")
                    .Replace("+", "\\+")
                    .Replace("-", "\\-")
                    .Replace("&&", "\\&&")
                    .Replace("||", "\\||")
                    .Replace("!", "\\!")
                    .Replace("(", "\\(")
                    .Replace(")", "\\)")
                    .Replace("{", "\\{")
                    .Replace("}", "\\}")
                    .Replace("[", "\\[")
                    .Replace("]", "\\]")
                    .Replace("^", "\\^")
                    .Replace("~", "\\~")
                    .Replace("?", "\\?")
                    .Replace(":", "\\:");

                processedTerms.Add(cleanTerm);

                // Joker karakteri ekle (2 veya daha fazla karakter için)
                if (cleanTerm.Length >= 2)
                {
                    processedTerms.Add(cleanTerm + "*");
                }
            }

            // En az bir terim yoksa, orijinal arama terimini döndür
            if (!processedTerms.Any())
                return searchTerm;

            return string.Join(" OR ", processedTerms.Distinct());
        }

    }

    public interface ISearchService
    {
        SearchResultModel QueryUmbraco(string searchTerm, int pageSize, int currentPage, string lang, string type);
    }

    public class SearchResultModel
    {
        public IEnumerable<IPublishedContent> Results { get; set; } = Enumerable.Empty<IPublishedContent>();
        public int TotalCount { get; set; }
    }
}