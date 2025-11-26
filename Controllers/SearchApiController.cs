using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Controllers;
using Umbraco.Services;
using Umbraco.Cms.Core.Models.Blocks;
using Morpara.Helpers;

namespace Morpara.Controllers
{
    public class SearchApiController : UmbracoApiController
    {
        private readonly ISearchService _searchService;
        private readonly IVariationContextAccessor _variationContextAccessor;
        private readonly IUmbracoContextFactory _umbracoContextFactory;
        private readonly int _pageSize = 10;

        public SearchApiController(
            ISearchService searchService,
            IVariationContextAccessor variationContextAccessor,
            IUmbracoContextFactory umbracoContextFactory)
        {
            _searchService = searchService;
            _variationContextAccessor = variationContextAccessor;
            _umbracoContextFactory = umbracoContextFactory;
        }

        [HttpGet]
        public IActionResult Search(string searchTerm, string culture = "tr-TR", string contentType = "")
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
                return BadRequest("Arama terimi boş olamaz.");

            _variationContextAccessor.VariationContext = new VariationContext(culture);

            // Daha fazla sonuç al, filtreledikten sonra 10 tane kalsın diye
            var searchPageSize = 50; // Daha fazla sonuç al
            var results = _searchService.QueryUmbraco(searchTerm, searchPageSize, 1, culture, contentType);

            // URL'leri takip etmek için HashSet kullan
            var usedUrls = new HashSet<string>();
            var filteredResults = new List<SearchResultItem>();

            // Ana arama sonuçlarını Level'a göre sırala (üst dizin önce)
            var sortedResults = results.Results.OfType<IPublishedContent>()
                .OrderBy(x => x.Level) // Level düşük olanlar önce (root'a yakın)
                .ThenBy(x => x.Name) // Sonra alfabetik sıra
                .ToList();

            // Sıralanmış sonuçları işle, duplicate kontrolü ile
            foreach (var content in sortedResults)
            {
                if (filteredResults.Count >= _pageSize) break;

                var searchItem = CreateSearchResultItem(content);
                if (searchItem != null && !usedUrls.Contains(searchItem.Path))
                {
                    usedUrls.Add(searchItem.Path);
                    filteredResults.Add(searchItem);
                }
            }

            // Eğer sonuç 10'dan az ise block grid'lerde arama yap
            if (filteredResults.Count < _pageSize)
            {
                var blockGridResults = SearchInBlockGrids(searchTerm, culture, _pageSize - filteredResults.Count, usedUrls);
                
                // Title ve description sonuçlarını ayır
                var titleResults = blockGridResults.Where(r => r.ParentName.StartsWith("FROM_TITLE:")).ToList();
                var descriptionResults = blockGridResults.Where(r => r.ParentName.StartsWith("FROM_DESCRIPTION:")).ToList();
                
                // ParentName'lerden işaretleri temizle
                foreach (var result in titleResults)
                {
                    result.ParentName = result.ParentName.Replace("FROM_TITLE:", "");
                }
                foreach (var result in descriptionResults)
                {
                    result.ParentName = result.ParentName.Replace("FROM_DESCRIPTION:", "");
                }
                
                // Önce title sonuçlarını ekle
                filteredResults.AddRange(titleResults.Take(_pageSize - filteredResults.Count));
                
                // Sonra description sonuçlarını ekle (eğer hala yer varsa)
                if (filteredResults.Count < _pageSize)
                {
                    filteredResults.AddRange(descriptionResults.Take(_pageSize - filteredResults.Count));
                }
            }

            var viewModel = new SearchResultViewModel
            {
                SearchTerm = searchTerm,
                TotalCount = filteredResults.Count, // Gerçek filtrelenmiş sonuç sayısı
                Results = filteredResults
            };

            return Ok(viewModel);
        }

        private List<SearchResultItem> SearchInBlockGrids(string searchTerm, string culture, int maxResults, HashSet<string> usedUrls)
        {
            var titleResults = new List<SearchResultItem>();
            var descriptionResults = new List<SearchResultItem>();
            
            using var umbracoContext = _umbracoContextFactory.EnsureUmbracoContext();
            _variationContextAccessor.VariationContext = new VariationContext(culture);
            
            // Tüm page'leri al ve Level'a göre sırala (üst dizin önce)
            var allPages = umbracoContext.UmbracoContext.Content?.GetAtRoot()
                .SelectMany(x => x.DescendantsOrSelf())
                .Where(x => x.ContentType.Alias == "page")
                .Where(x => x.Level > 1) // En üst seviye sayfaları atla
                .OrderBy(x => x.Level) // Level düşük olanlar önce (root'a yakın)
                .ThenBy(x => x.Name) // Sonra alfabetik sıra
                .ToList();

            if (allPages == null) return new List<SearchResultItem>();

            var searchTermLower = searchTerm.ToLowerInvariant();

            // 1. ÖNCELİK: Title'da arama yap
            foreach (var page in allPages)
            {
                if (titleResults.Count >= maxResults) break;

                try
                {
                    // Sayfa title'ında ara
                    var pageTitle = page.Value<string>("title") ?? page.Name ?? "";
                    var searchTitle = page.Value<string>("searchTitle") ?? "";
                    
                    var titleText = (!string.IsNullOrEmpty(searchTitle) ? searchTitle : pageTitle).ToLowerInvariant();
                    
                    if (!string.IsNullOrEmpty(titleText) && titleText.Contains(searchTermLower))
                    {
                        var searchItem = CreateSearchResultItem(page);
                        if (searchItem != null && !usedUrls.Contains(searchItem.Path))
                        {
                            usedUrls.Add(searchItem.Path);
                            // Title sonuçlarını işaretle
                            searchItem.ParentName = "FROM_TITLE:" + searchItem.ParentName;
                            titleResults.Add(searchItem);
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }

            // 2. ÖNCELİK: Description'da arama yap
            foreach (var page in allPages)
            {
                if (descriptionResults.Count >= maxResults) break;

                try
                {
                    // Page'in home property'sindeki block grid'i kontrol et
                    var homeBlockGrid = page.Value<BlockGridModel>("home");
                    if (homeBlockGrid != null && homeBlockGrid.Any())
                    {
                        // İlk block'un description'ına bak
                        var firstBlock = homeBlockGrid.FirstOrDefault();
                        if (firstBlock?.Content != null)
                        {
                            var description = firstBlock.Content.Value<string>("description") ?? 
                                            firstBlock.Content.Value<string>("desc") ??
                                            firstBlock.Content.Value<string>("content") ?? "";

                            // HTML taglerini temizle ve arama yap
                            var plainDescription = StripHtmlTags(description).ToLowerInvariant();
                            
                            if (!string.IsNullOrEmpty(plainDescription) && 
                                plainDescription.Contains(searchTermLower))
                            {
                                // Bu sayfayı sonuçlara ekle (URL duplicate kontrolü ile)
                                var searchItem = CreateSearchResultItem(page);
                                if (searchItem != null && !usedUrls.Contains(searchItem.Path))
                                {
                                    usedUrls.Add(searchItem.Path);
                                    // Description sonuçlarını işaretle
                                    searchItem.ParentName = "FROM_DESCRIPTION:" + searchItem.ParentName;
                                    descriptionResults.Add(searchItem);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Hata durumunda bu sayfayı atla
                    continue;
                }
            }

            // Önce title sonuçları, sonra description sonuçları birleştir
            var results = new List<SearchResultItem>();
            results.AddRange(titleResults.Take(maxResults));
            
            // Eğer hala yer varsa description sonuçları ekle
            var remainingSlots = maxResults - results.Count;
            if (remainingSlots > 0)
            {
                results.AddRange(descriptionResults.Take(remainingSlots));
            }

            return results;
        }

        private string StripHtmlTags(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return System.Text.RegularExpressions.Regex.Replace(input, "<.*?>", string.Empty);
        }

        private string FormatParentName(string parentName)
        {
            if (string.IsNullOrEmpty(parentName))
                return string.Empty;

            // Tire ile ayrılmış kelimeleri düzenle
            // "sanal-pos" -> "Sanal pos"
            var words = parentName.Split('-', StringSplitOptions.RemoveEmptyEntries);
            var formattedWords = words.Select(word => 
            {
                if (string.IsNullOrEmpty(word))
                    return word;
                
                // İlk harfi büyük yap, geri kalanını küçük yap
                return char.ToUpper(word[0]) + (word.Length > 1 ? word.Substring(1).ToLower() : "");
            });

            return string.Join(" ", formattedWords);
        }

        private SearchResultItem? CreateSearchResultItem(IPublishedContent content)
        {
            var excludedAliases = new[]
            {
                "homepage", "website", "morparaDifferent", "cartContent", "cartFeatures",
                "representation", "fotterApp", "configuration", "newsletterSubscriptions", "contactRequests"
            };

            if (excludedAliases.Contains(content.ContentType.Alias))
                return null;

            // En üst seviye sayfaları (Level 1) arama sonuçlarından çıkar, Level 2 ve altında arama yap
            if (content.Level <= 1)
                return null;

            // representationItem içeren sayfaları arama sonuçlarından çıkar
            if (content.ContentType.Alias == "page" && HasRepresentationItem(content))
                return null;

            // searchTitle varsa onu kullan, yoksa title'ı kullan
            var searchTitle = content.Value<string>("searchTitle");
            var title = !string.IsNullOrEmpty(searchTitle) ? searchTitle : content.Value<string>("title") ?? "Başlık Bulunamadı";
            var path = content.Url();
            var parentAlias = content.Parent?.ContentType.Alias ?? "";

            // FAQ pages için kategori bilgisini URL'e ekle (culture-independent)
            // page content type'ı olsa da içinde questionsContent block'ları olabilir
            if (content.ContentType.Alias == "page" && HasQuestionContent(content))
            {
                path = GetQuestionUrlWithCategory(content, path);
            }

            return new SearchResultItem
            {
                Title = title,
                Key = content.Key.ToString(),
                Id = content.Id.ToString(),
                ContentType = content.ContentType.Alias,
                ParentName = content.Level >= 3 && parentAlias is not ("homepage" or "website") ? FormatParentName(content.Parent?.Name ?? "") : "",
                Path = path
            };
        }

        private bool HasQuestionContent(IPublishedContent content)
        {
            try
            {
                // Page'in home property'sindeki block grid'i kontrol et
                var homeBlockGrid = content.Value<BlockGridModel>("home");
                if (homeBlockGrid != null)
                {
                    // questionsContent block'larını ara
                    return homeBlockGrid.Any(b => b.Content?.ContentType?.Alias == "questionsContent");
                }
                return false;
            }
            catch
            {
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
                    // representationItem block'larını ara
                    return homeBlockGrid.Any(b => b.Content?.ContentType?.Alias == "representationItem" ||
                                                   b.Content?.ContentType?.Alias == "representation" ||
                                                   b.Content?.ContentType?.Alias == "representative");
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private string GetQuestionUrlWithCategory(IPublishedContent content, string originalUrl)
        {
            Console.WriteLine($"[DEBUG] Content ID {content.Id}, ContentType: {content.ContentType.Alias}");
            
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
                    
                    Console.WriteLine($"[DEBUG] Found {questionBlocks.Count} questionsContent blocks");
                    
                    foreach (var block in questionBlocks)
                    {
                        // Bu block'un categories property'sini kontrol et
                        var categories = block.Content?.Value<IEnumerable<IPublishedContent>>("categories");
                        if (categories != null && categories.Any())
                        {
                            var firstCategory = categories.First();
                            Console.WriteLine($"[DEBUG] First category found in block: {firstCategory.Name}");
                            
                            var categoryName = TextNormalizationHelper.NormalizeCategoryName(firstCategory.Name);
                            Console.WriteLine($"[DEBUG] Normalized category: {categoryName}");
                            
                            var newUrl = BuildUrlWithCategory(content, categoryName);
                            Console.WriteLine($"[DEBUG] Original URL: {originalUrl}, New URL: {newUrl}");
                            return newUrl;
                        }
                    }
                }
                
                Console.WriteLine($"[DEBUG] No categories found in questionsContent blocks for content ID {content.Id}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] Error getting categories from blocks: {ex.Message}");
            }

            return originalUrl;
        }

        private string BuildUrlWithCategory(IPublishedContent content, string categoryName)
        {
            // Parent'ın URL'ini al ve kategoriyi ekle
            var parentUrl = content.Parent?.Url()?.TrimEnd('/') ?? "";
            var contentUrlName = content.UrlSegment ?? content.Name.ToLowerInvariant()
                .Replace(" ", "-")
                .Replace("ç", "c")
                .Replace("ğ", "g")
                .Replace("ı", "i")
                .Replace("ö", "o")
                .Replace("ş", "s")
                .Replace("ü", "u");
            
            return $"{parentUrl}/{categoryName}/{contentUrlName}/";
        }

        private string InsertCategoryIntoUrl(string originalUrl, string categoryName)
        {
            var parts = originalUrl.Trim('/').Split('/');
            
            // URL'de en az 2 segment varsa (parent/child), kategoriyi ortaya ekle
            if (parts.Length >= 2)
            {
                var newParts = new List<string> { parts[0], categoryName };
                newParts.AddRange(parts.Skip(1));
                return "/" + string.Join('/', newParts) + "/";
            }

            return originalUrl;
        }

        private string GetPropertyValues(IEnumerable<IPublishedProperty> properties, string originalUrl)
        {
            foreach (var property in properties)
            {
                var value = property.GetValue();

                if (value is IEnumerable<IPublishedContent> contentList)
                {
                    var first = contentList.FirstOrDefault();
                    if (first != null)
                        return ReplaceLastUrlSegment(originalUrl, first.Name);
                }
                else if (!string.IsNullOrEmpty(value?.ToString()))
                {
                    return ReplaceLastUrlSegment(originalUrl, value.ToString()!);
                }
            }

            return originalUrl;
        }

        private string ReplaceLastUrlSegment(string originalUrl, string newSegment)
        {
            var parts = originalUrl.Trim('/').Split('/');
            if (parts.Length < 2) return originalUrl;

            parts[^2] = newSegment;
            return "/" + string.Join('/', parts);
        }
    }

    public class SearchResultViewModel
    {
        public string SearchTerm { get; set; }
        public int TotalCount { get; set; }
        public int CurrentPage { get; set; }
        public IEnumerable<SearchResultItem> Results { get; set; } = Enumerable.Empty<SearchResultItem>();
    }

    public class SearchResultItem
    {
        public string Title { get; set; }
        public string ParentName { get; set; }
        public string Key { get; set; }
        public string Id { get; set; }
        public string ContentType { get; set; }
        public string Path { get; set; }
    }
}
