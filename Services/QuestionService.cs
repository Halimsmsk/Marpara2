using Morpara.Services.Interfaces;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Extensions;
using Microsoft.Extensions.Logging;
using Morpara.Helpers;

namespace Morpara.Services
{
    public class QuestionService : IQuestionService
    {
        private readonly IUmbracoContextAccessor _ctxAccessor;
        private readonly IVariationContextAccessor _variation;
        private readonly ILogger<QuestionService> _logger;

        public QuestionService(
            IUmbracoContextAccessor ctxAccessor,
            IVariationContextAccessor variation,
            ILogger<QuestionService> logger)
        {
            _ctxAccessor = ctxAccessor;
            _variation = variation;
            _logger = logger;
        }

        private string NormalizeCategoryName(string name)
        {
            // Wrapper for TextNormalizationHelper to maintain backward compatibility
            return TextNormalizationHelper.NormalizeCategoryName(name);
        }

        public object GetQuestionsItems(string id, int page = 1, int pageSize = 10, string orderBy = "A-Z", 
            string category = null!, string search = null!, string activeContentId = null!)
        {
            // Debug logging
            _logger.LogInformation("[DEBUG] GetQuestionsItems called with: id='{Id}', category='{Category}', search='{Search}', activeContentId='{ActiveContentId}'", 
                id, category, search, activeContentId);
            
            if (!_ctxAccessor.TryGetUmbracoContext(out var ctx))
                return new List<object>();

            // Get the current culture
            var culture = _variation.VariationContext?.Culture ?? "tr-TR";

            // Find the parent content item by ID
            IPublishedContent? parent = Guid.TryParse(id, out var g)
                ? ctx.Content?.GetById(false, g)
                : int.TryParse(id, out var i)
                    ? ctx.Content?.GetById(i)
                    : null;

            if (parent == null)
                return new List<object>();

            // ✨ YENİ: Eğer activeContentId bir URL slug ise, bunu ID'ye çevir
            if (!string.IsNullOrEmpty(activeContentId))
            {
                // Önce ID olarak dene (GUID veya int)
                bool isValidId = Guid.TryParse(activeContentId, out var _) || int.TryParse(activeContentId, out var _);
                
                if (!isValidId)
                {
                    // ID değilse, URL slug olarak kabul et
                    // URL'den son segment'i al
                    var slug = activeContentId.Trim('/').Split('/').LastOrDefault();
                    
                    _logger.LogInformation("[DEBUG SLUG] activeContentId '{ActiveContentId}' is not ID, treating as slug: '{Slug}'", activeContentId, slug);
                    
                    // Parent'ın tüm descendants'ları içinde slug ile ara
                    var matchedContent = parent.Descendants()
                        .FirstOrDefault(d => 
                            string.Equals(d.UrlSegment, slug, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(d.Name.ToLowerInvariant().Replace(" ", "-"), slug, StringComparison.OrdinalIgnoreCase)
                        );
                    
                    if (matchedContent != null)
                    {
                        _logger.LogInformation("[DEBUG SLUG] ✅ Found content by slug: '{Name}' (ID: {Id}, UrlSegment: '{UrlSegment}')", 
                            matchedContent.Name, matchedContent.Id, matchedContent.UrlSegment);
                        
                        // ID'yi güncelle ki aşağıdaki kod çalışsın
                        activeContentId = matchedContent.Id.ToString();
                    }
                    else
                    {
                        _logger.LogWarning("[DEBUG SLUG] ❌ Could not find content with slug: '{Slug}'", slug);
                        activeContentId = null;
                    }
                }
            }

            // Get the questionsPage block from the parent
            var homeBlockGrid = parent.Value<BlockGridModel>("home");
            var questionsPageBlock = homeBlockGrid?.FirstOrDefault(b => b.Content?.ContentType?.Alias == "questionsPage");

            // Extract parameters from the questionsPage block but prioritize the passed parameters
            var categoriesActive = questionsPageBlock?.Content?.Value<bool>("categoriesActive") ?? false;
            var allCategoriesText = questionsPageBlock?.Content?.Value<string>("allCategoriesText") ?? "Tümü";

            // Always use the passed pageSize parameter, don't override with block settings
            var questionsPageSize = pageSize;
            var questionsOrder = orderBy;

            var categories = questionsPageBlock?.Content?.Value<IEnumerable<IPublishedElement>>("categories")?
                .Select(c =>
                {
                    var categoryTitle = c.Value<string>("title");

                    // Check if this category matches the active filter (normalize both for comparison)
                    bool isSelected = false;

                    if (!string.IsNullOrEmpty(category) && !string.IsNullOrEmpty(categoryTitle))
                    {
                        // Use consistent normalization with new helper
                        isSelected = TextNormalizationHelper.AreCategoriesEqual(categoryTitle, category);
                        
                        _logger.LogInformation("[DEBUG] Category comparison: '{CategoryTitle}' vs '{Category}', Match: {IsSelected}", 
                            categoryTitle, category, isSelected);
                            
                        if (!isSelected)
                        {
                            // Debug bilgisi
                            var debugInfo = TextNormalizationHelper.GetComparisonDebugInfo(categoryTitle, category);
                            _logger.LogInformation("[DEBUG] Category comparison details:\n{DebugInfo}", debugInfo);
                        }
                    }

                    return new
                    {
                        contentTypeAlias = c.ContentType.Alias,
                        title = categoryTitle,
                        name = (c as IPublishedContent)?.Name(culture) ?? "",
                        icon = c.Value<IPublishedContent>("icon")?.Url(),
                        selected = isSelected
                    };
                }).ToList();

            // Check if the provided category is valid (exists in the defined categories)
            if (!string.IsNullOrEmpty(category) && category != allCategoriesText)
            {
                var validCategories = categories?.Select(c => c.title).ToList() ?? new List<string>();
                var isCategoryValid = validCategories.Any(c => TextNormalizationHelper.AreCategoriesEqual(c, category));
                
                _logger.LogInformation("[DEBUG] Category validation: '{Category}' valid={IsValid}, Available categories: {Categories}", 
                    category, isCategoryValid, string.Join(", ", validCategories));
                
                if (!isCategoryValid)
                {
                    _logger.LogWarning("[DEBUG] Invalid category '{Category}' provided - returning invalid category marker", category);
                    return new
                    {
                        contentType = "questionsPage",
                        isValidCategory = false,
                        invalidCategory = category,
                        content = new
                        {
                            contentTypeAlias = "questionsPage",
                            categoriesActive = categoriesActive,
                            categories = categories,
                            allCategoriesText = allCategoriesText,
                            questionsPageSize = questionsPageSize,
                            questionsOrder = questionsOrder
                        },
                        questions = new List<object>(),
                        pagination = new
                        {
                            currentPage = page,
                            pageSize = questionsPageSize,
                            totalItems = 0,
                            totalPages = 0,
                            hasActiveContent = false
                        }
                    };
                }
            }

            var contentItems = new List<IPublishedContent>();

            foreach (var child in parent.Children())
            {
                // ✅ FIX: Sadece aktif culture'da yayınlanmış children'ı dahil et
                if (!child.HasCulture(culture))
                {
                    _logger.LogInformation("[DEBUG] Skipping child '{ChildName}' (ID: {ChildId}) - not available in culture '{Culture}'", 
                        child.Name, child.Id, culture);
                    continue;
                }

                // Check if the child has questionsContent blocks
                var childHomeGrid = child.Value<BlockGridModel>("home", culture: culture);
                if (childHomeGrid != null && childHomeGrid.Any(b => b.Content?.ContentType?.Alias == "questionsContent"))
                {
                    _logger.LogInformation("[DEBUG] Including child '{ChildName}' (ID: {ChildId}) in culture '{Culture}'", 
                        child.Name, child.Id, culture);
                    contentItems.Add(child);
                }
            }

            // Then go deeper for pages that might have question content without being direct descendants
            var allDescendants = parent.Descendants()
                .Where(d => !contentItems.Contains(d)) // Avoid duplicates with direct children
                .Where(d => d.HasCulture(culture)) // ✅ FIX: Culture filtresi ekle
                .Where(d =>
                {
                    var dHomeGrid = d.Value<BlockGridModel>("home", culture: culture);
                    return dHomeGrid != null && dHomeGrid.Any(b => b.Content?.ContentType?.Alias == "questionsContent");
                });

            contentItems.AddRange(allDescendants);

            // Apply sorting based on questionsOrder
            contentItems = questionsOrder switch
            {
                "None" => contentItems,
                "A-Z" => contentItems.OrderBy(child => child.Name).ToList(),
                "Z-A" => contentItems.OrderByDescending(child => child.Name).ToList(),
                "Date ASC" => contentItems.OrderBy(child => child.CreateDate).ToList(),
                "Date DESC" => contentItems.OrderByDescending(child => child.CreateDate).ToList(),
                _ => contentItems.OrderBy(child => child.Name).ToList() // Default to A-Z
            };

            // First process all items to find the active content
            var allQuestionsTemp = new List<(IPublishedContent Item, BlockGridItem Block, string Title, bool IsActive, int Index)>();
            int itemIndex = 0;

            // Just to make debugging easier
            bool foundActiveContent = false;
            string activeContentTitle = "";
            int activeItemPosition = -1;

            // PHASE 1: Gather all filtered questions and find active item position
            foreach (var item in contentItems)
            {
                var itemHomeGrid = item.Value<BlockGridModel>("home", culture: culture);
                var questionBlocks = itemHomeGrid?
                    .Where(b => b.Content?.ContentType?.Alias == "questionsContent")
                    .ToList();

                if (questionBlocks == null || !questionBlocks.Any())
                    continue;

                foreach (var block in questionBlocks)
                {
                    try
                    {
                        var title = block.Content.Value<string>("title") ?? "Soru";

                        // Check if this item has already been added by title
                        if (allQuestionsTemp.Any(q => string.Equals(q.Title, title, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        // Check category filter
                        if (!string.IsNullOrEmpty(category) && category != allCategoriesText)
                        {
                            var questionBlockCategories = block.Content.Value<IEnumerable<IPublishedContent>>("categories");
                            _logger.LogInformation("[DEBUG] Question '{Title}' has categories: {Categories}", title, string.Join(", ", questionBlockCategories?.Select(c => c.Name) ?? new string[0]));
                            
                            if (questionBlockCategories == null)
                            {
                                _logger.LogInformation("[DEBUG] SKIPPING '{Title}' - no categories found", title);
                                continue;
                            }

                            bool hasMatchingCategory = false;
                            foreach (var cat in questionBlockCategories)
                            {
                                // Debug logging
                                var catName = cat.Name;
                                _logger.LogInformation("[DEBUG] Comparing category: '{CatName}' with filter: '{Category}'", catName, category);
                                
                                // Use new helper for robust comparison
                                bool categoryMatch = TextNormalizationHelper.AreCategoriesEqual(catName, category);
                                
                                if (categoryMatch)
                                {
                                    hasMatchingCategory = true;
                                    _logger.LogInformation("[DEBUG] MATCH FOUND: '{CatName}' == '{Category}'", catName, category);
                                    break;
                                }
                                else
                                {
                                    // Debug bilgisi sadece eşleşmediğinde
                                    var debugInfo = TextNormalizationHelper.GetComparisonDebugInfo(catName, category);
                                    _logger.LogInformation("[DEBUG] No match details:\n{DebugInfo}", debugInfo);
                                }
                            }

                            if (!hasMatchingCategory)
                            {
                                _logger.LogInformation("[DEBUG] SKIPPING item '{Title}' - no matching category", title);
                                continue; // Skip this block if it doesn't have the specified category
                            }
                            else
                            {
                                _logger.LogInformation("[DEBUG] INCLUDING item '{Title}' - category match found", title);
                            }
                        }

                        // Check search filter
                        if (!string.IsNullOrEmpty(search))
                        {
                            var searchTerm = search.Trim().ToLowerInvariant();
                            var blockTitle = title.ToLowerInvariant();
                            var questionContent = block.Content.Value<string>("questions") ?? string.Empty;
                            var plainTextContent = StripHtmlTags(questionContent)?.ToLowerInvariant() ?? string.Empty;

                            _logger.LogInformation("[DEBUG SEARCH] Searching for term: '{SearchTerm}' in title: '{BlockTitle}' and content preview: '{ContentPreview}'", 
                                searchTerm, blockTitle, plainTextContent.Length > 100 ? plainTextContent.Substring(0, 100) + "..." : plainTextContent);

                            bool titleMatch = blockTitle.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0;
                            bool contentMatch = plainTextContent.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0;
                            
                            _logger.LogInformation("[DEBUG SEARCH] Title match: {TitleMatch}, Content match: {ContentMatch}", titleMatch, contentMatch);

                            if (!titleMatch && !contentMatch)
                            {
                                _logger.LogInformation("[DEBUG SEARCH] SKIPPING '{Title}' - no search match found", title);
                                continue;
                            }
                            else
                            {
                                _logger.LogInformation("[DEBUG SEARCH] INCLUDING '{Title}' - search match found", title);
                            }
                        }

                        // Check if this is the active content
                        bool isActive = !string.IsNullOrEmpty(activeContentId) &&
                                        item.Id.ToString() == activeContentId;

                        // If active content, track its position and details
                        if (isActive)
                        {
                            foundActiveContent = true;
                            activeContentTitle = title;
                            activeItemPosition = itemIndex;
                        }

                        // Add to our temporary collection
                        allQuestionsTemp.Add((item, block, title, isActive, itemIndex));
                        itemIndex++;
                    }
                    catch
                    {
                        // Skip on error
                    }
                }
            }

            // Calculate total counts
            var totalUniqueQuestions = allQuestionsTemp.Count;
            var totalPages = (int)Math.Ceiling((double)totalUniqueQuestions / questionsPageSize);

            // ÖNEMLİ: Eğer aktif içerik belirtilmiş ve sayfa parametresi açıkça belirtilmemişse 
            // otomatik olarak aktif içeriğin olduğu sayfayı göster
            bool autoDetectPage = !string.IsNullOrEmpty(activeContentId);

            // Calculate which page the active item is on
            int activeItemPage = 1;
            if (activeItemPosition >= 0)
            {
                activeItemPage = (activeItemPosition / questionsPageSize) + 1;

                // Ensure the active page is within valid range
                if (activeItemPage > totalPages)
                    activeItemPage = totalPages;

                // FORCE the page to be the active item's page, regardless of what was passed
                if (autoDetectPage)
                {
                    page = activeItemPage;
                }
            }

            // Get paginated questions
            var paginatedQuestions = allQuestionsTemp
                .Skip((page - 1) * questionsPageSize)
                .Take(questionsPageSize);

            // Create a list to store all processed questions
            var allQuestions = new List<object>();

            // Process the paginated questions
            foreach (var (item, block, title, isActive, index) in paginatedQuestions)
            {
                try
                {
                    var questionsContent = block.Content.Value<string>("questions");

                    var blockCategories = block.Content.Value<IEnumerable<IPublishedContent>>("categories")?
                        .Select(c => new
                        {
                            contentTypeAlias = c.ContentType.Alias,
                            contentId = item.Id,
                            contentKey = item.Key,
                            title = c.Name,
                            name = c.UrlSegment ?? "",
                            icon = c.Value<IPublishedContent>("icon")?.Url(),
                            defaultvalue = c.Value<bool?>("default") ?? false
                        });
                    
                    // ⚡ Arama yapıldığında, default kategoriyi çıkar (sadece default olmayan kategoriler göster)
                    if (!string.IsNullOrEmpty(search))
                    {
                        blockCategories = blockCategories?.Where(c => !c.defaultvalue);
                    }
                    
                    var blockCategoriesList = blockCategories?.ToList();

                    // Generate URL with category
                    string? customUrl = null;
                    if (blockCategoriesList != null && blockCategoriesList.Any())
                    {
                        // ✅ FIX: Always use parent URL to build child URLs for culture consistency
                        var parentUrl = parent.Url(culture);
                        
                        if (!string.IsNullOrEmpty(parentUrl) && parentUrl != "#")
                        {
                            var questionSlug = item.UrlSegment ?? item.Name.ToLowerInvariant().Replace(" ", "-");
                            
                            string? categoryForUrl;
                            if (!string.IsNullOrEmpty(category) && category != allCategoriesText)
                            {
                                categoryForUrl = category;
                            }
                            else
                            {
                                var firstCategory = blockCategoriesList.FirstOrDefault(c => c.defaultvalue) ??
                                                  blockCategoriesList.FirstOrDefault();
                                categoryForUrl = firstCategory?.name;
                            }
                            
                            if (!string.IsNullOrEmpty(categoryForUrl))
                            {
                                customUrl = $"{parentUrl.TrimEnd('/')}/{categoryForUrl}/{questionSlug}/";
                            }
                        }
                    }

                    allQuestions.Add(new
                    {
                        contentType = "questionsContent",
                        content = new
                        {
                            contentTypeAlias = "questionsContent",
                            contentId = item.Id,
                            contentKey = item.Key,
                            title = title,
                            questions = questionsContent,
                            categories = blockCategoriesList,
                            url = customUrl ?? item.Url(culture),
                            isActive = isActive,  // Add the isActive flag
                            index = index  // Include index for debugging
                        }
                    });
                }
                catch (Exception ex)
                {
                    allQuestions.Add(new
                    {
                        contentType = "questionsContent",
                        content = new
                        {
                            contentTypeAlias = "questionsContent",
                            contentId = item.Id,
                            contentKey = item.Key,
                            title = "Error processing question",
                            questions = $"Error: {ex.Message}",
                            categories = new List<object>(),
                            url = item.Url(culture),
                            isActive = false
                        }
                    });
                }
            }

            var result = new
            {
                contentType = "questionsPage",
                content = new
                {
                    contentTypeAlias = "questionsPage",
                    contentId = parent.Id,
                    contentKey = parent.Key,
                    title = questionsPageBlock?.Content?.Value<string>("title") ?? "Sık Sorulan Sorular",
                    subtitle = questionsPageBlock?.Content?.Value<string>("subtitle"),
                    description = questionsPageBlock?.Content?.Value<string>("description"),
                    allCategoriesText = allCategoriesText,
                    categoriesActive = categoriesActive,
                    categories = categories,
                    pageSize = questionsPageSize,
                    questionsOrder = questionsOrder,
                    activeCategory = category,
                    searchTerm = search,
                    activeContentId = activeContentId,
                    // Debug information
                    foundActiveContent = foundActiveContent,
                    activeContentTitle = activeContentTitle,
                    activeItemPosition = activeItemPosition,
                    calculatedPage = activeItemPosition >= 0 ? (int?)activeItemPage : null,
                    forcedPage = autoDetectPage && activeItemPosition >= 0 ? (int?)activeItemPage : null
                },
                pagination = new
                {
                    currentPage = page,
                    pageSize = questionsPageSize,
                    totalItems = totalUniqueQuestions,
                    totalPages = totalPages
                },
                questions = allQuestions
            };

            return result;
        }

        public object GetSSSSorular(string id, string categories = null!, int? maxQuestion = null, bool? multiCategoriesContent = false)
        {
            _logger.LogInformation("[DEBUG GetSSSSorular] Called with id: {Id}, categories: {Categories}, maxQuestion: {MaxQuestion}, multiCategories: {MultiCategories}", 
                id, categories, maxQuestion, multiCategoriesContent);

            // Set default value for maxQuestion if not provided
            int actualMaxQuestion = maxQuestion ?? 20;

            if (!_ctxAccessor.TryGetUmbracoContext(out var ctx))
            {
                _logger.LogWarning("[DEBUG GetSSSSorular] Could not get Umbraco context");
                return new List<object>();
            }

            // Parse the ID to find the content item
            IPublishedContent? item = Guid.TryParse(id, out var g)
                                        ? ctx.Content?.GetById(false, g)
                                        : int.TryParse(id, out var i)
                                            ? ctx.Content?.GetById(i)
                                            : null;

            if (item == null)
            {
                _logger.LogWarning("[DEBUG GetSSSSorular] Could not find content item with id: {Id}", id);
                return new List<object>();
            }

            _logger.LogInformation("[DEBUG GetSSSSorular] Found content item: {ItemName} (ID: {ItemId})", item.Name, item.Id);

            // Parse multiple categories if provided (comma-separated)
            List<string>? categoryList = null;
            if (!string.IsNullOrEmpty(categories))
            {
                categoryList = categories.Split(',').Select(c => c.Trim()).ToList();
                _logger.LogInformation("[DEBUG GetSSSSorular] Parsed categories: [{CategoryList}]", string.Join(", ", categoryList));
            }
            else
            {
                _logger.LogInformation("[DEBUG GetSSSSorular] No categories provided - will return all questions");
            }

            // Get the current culture
            var culture = _variation.VariationContext?.Culture ?? "tr-TR";

            // If multiCategoriesContent is true, organize questions by category
            if (multiCategoriesContent == true)
            {
                // If no specific categories provided, get all questions
                if (categoryList == null || !categoryList.Any())
                {
                    _logger.LogInformation("[DEBUG GetSSSSorular] multiCategoriesContent=true but no categories specified - returning all questions");
                    
                    var result = new List<object>();
                    int itemCount = 0;
                    int maxItems = actualMaxQuestion;

                    foreach (var child in item.Children())
                    {
                        if (itemCount >= maxItems)
                            break;

                        // ✅ FIX: Culture kontrolü ekle
                        if (!child.HasCulture(culture))
                        {
                            _logger.LogInformation("[DEBUG GetSSSSorular] Skipping child '{ChildName}' - not available in culture '{Culture}'", 
                                child.Name, culture);
                            continue;
                        }

                        var homeBlockGrid = child.Value<BlockGridModel>("home", culture: culture);
                        if (homeBlockGrid == null) continue;

                        var questionBlocks = homeBlockGrid.Where(b => b.Content?.ContentType?.Alias == "questionsContent").ToList();
                        if (!questionBlocks.Any()) continue;

                        foreach (var block in questionBlocks)
                        {
                            if (itemCount >= maxItems)
                                break;

                            var title = block.Content.Value<string>("title") ?? "Soru";
                            var questionsContent = block.Content.Value<string>("questions");

                            var blockCategories = block.Content.Value<IEnumerable<IPublishedContent>>("categories")?
                                .Select(c => new
                                {
                                    contentTypeAlias = c.ContentType.Alias,
                                    contentId = child.Id,
                                    contentKey = child.Key,
                                    title = c.Name,
                                    name = c.UrlSegment ?? "",
                                    icon = c.Value<IPublishedContent>("icon")?.Url(),
                                    defaultvalue = c.Value<bool?>("default") ?? false
                                })
                                .ToList();

                            // Generate URL with category
                            string? customUrl = null;
                            if (blockCategories != null && blockCategories.Any())
                            {
                                var originalUrl = child.Url(culture);
                                
                                if (!string.IsNullOrEmpty(originalUrl) && originalUrl != "#")
                                {
                                    var urlParts = originalUrl.Trim('/').Split('/');
                                    if (urlParts.Length >= 2)
                                    {
                                        var basePath = urlParts[0];
                                        var questionSlug = urlParts[urlParts.Length - 1];
                                        var firstCategory = blockCategories.FirstOrDefault(c => c.defaultvalue) ??
                                                           blockCategories.FirstOrDefault();
                                        if (!string.IsNullOrEmpty(firstCategory?.name))
                                        {
                                            // Use UrlSegment (name), not title
                                            customUrl = $"/{basePath}/{firstCategory.name}/{questionSlug}/";
                                        }
                                    }
                                }
                                else
                                {
                                    // ⚡ FIX: Child URL is "#" - build from parent + UrlSegment
                                    var parentUrl = item.Url(culture);
                                    if (!string.IsNullOrEmpty(parentUrl) && parentUrl != "#")
                                    {
                                        var questionSlug = child.UrlSegment ?? child.Name.ToLowerInvariant().Replace(" ", "-");
                                        var firstCategory = blockCategories.FirstOrDefault(c => c.defaultvalue) ??
                                                           blockCategories.FirstOrDefault();
                                        if (!string.IsNullOrEmpty(firstCategory?.name))
                                        {
                                            customUrl = $"{parentUrl.TrimEnd('/')}/{firstCategory.name}/{questionSlug}/";
                                        }
                                    }
                                }
                            }

                            // Final URL: Ensure we always have a valid URL
                            var finalUrl = customUrl;
                            _logger.LogInformation("[DEBUG URL GEN 1] child.Url={ChildUrl}, customUrl={CustomUrl}, child.UrlSegment={UrlSegment}", 
                                child.Url(culture), customUrl, child.UrlSegment);
                            
                            if (string.IsNullOrEmpty(finalUrl) || finalUrl == "#")
                            {
                                // Last attempt: Force build from parent + category + slug
                                var parentUrl = item.Url(culture);
                                _logger.LogInformation("[DEBUG URL GEN 1] Fallback triggered - parentUrl={ParentUrl}", parentUrl);
                                
                                // ⚡ HARDCODED FALLBACK: If parent URL is also #, use hardcoded base path
                                if (string.IsNullOrEmpty(parentUrl) || parentUrl == "#")
                                {
                                    parentUrl = "/sikca-sorulan-sorular";
                                    _logger.LogInformation("[DEBUG URL GEN 1] Parent URL was #, using hardcoded: {ParentUrl}", parentUrl);
                                }
                                
                                var questionSlug = child.UrlSegment ?? child.Name.ToLowerInvariant().Replace(" ", "-");
                                var firstCategory = blockCategories?.FirstOrDefault(c => c.defaultvalue) ??
                                                   blockCategories?.FirstOrDefault();
                                if (!string.IsNullOrEmpty(firstCategory?.name))
                                {
                                    finalUrl = $"{parentUrl.TrimEnd('/')}/{firstCategory.name}/{questionSlug}/";
                                    _logger.LogInformation("[DEBUG URL GEN 1] Built URL with category: {FinalUrl}", finalUrl);
                                }
                                else
                                {
                                    // No category, just use parent + slug
                                    finalUrl = $"{parentUrl.TrimEnd('/')}/{questionSlug}/";
                                    _logger.LogInformation("[DEBUG URL GEN 1] Built URL without category: {FinalUrl}", finalUrl);
                                }
                            }
                            
                            _logger.LogInformation("[DEBUG URL GEN 1] FINAL URL for {ChildName}: {FinalUrl}", child.Name, finalUrl);

                            result.Add(new
                            {
                                contentType = "questionsContent",
                                content = new
                                {
                                    contentTypeAlias = "questionsContent",
                                    contentId = child.Id,
                                    contentKey = child.Key,
                                    title = title,
                                    questions = questionsContent,
                                    categories = blockCategories,
                                    url = finalUrl ?? "#"
                                }
                            });

                            itemCount++;
                        }
                    }

                    return result;
                }
                
                // Original logic for when categories are specified
                var resultByCategory = new Dictionary<string, List<object>>();

                // Initialize a list for each category
                foreach (var category in categoryList)
                {
                    resultByCategory[category] = new List<object>();
                }

                // Process each child for each category separately
                foreach (var category in categoryList)
                {
                    int categoryItemCount = 0;
                    int maxItemsForCategory = actualMaxQuestion;

                    // Look through all the children of the specified content item
                    foreach (var child in item.Children())
                    {
                        if (categoryItemCount >= maxItemsForCategory)
                            break;

                        // ✅ FIX: Culture kontrolü ekle
                        if (!child.HasCulture(culture))
                        {
                            _logger.LogInformation("[DEBUG GetSSSSorular] Skipping child '{ChildName}' - not available in culture '{Culture}'", 
                                child.Name, culture);
                            continue;
                        }

                        var homeBlockGrid = child.Value<BlockGridModel>("home", culture: culture);
                        if (homeBlockGrid == null) continue;

                        // Find all questionsContent blocks in this child
                        var questionBlocks = homeBlockGrid.Where(b => b.Content?.ContentType?.Alias == "questionsContent").ToList();
                        if (!questionBlocks.Any()) continue;

                        foreach (var block in questionBlocks)
                        {
                            if (categoryItemCount >= maxItemsForCategory)
                                break;

                            // Get categories for this question block
                            var blockCategories = block.Content.Value<IEnumerable<IPublishedContent>>("categories")?
                                .Select(c => c.Name)
                                .ToList();

                            // Skip if this block doesn't have the current category
                            if (blockCategories == null || !blockCategories.Contains(category, StringComparer.OrdinalIgnoreCase))
                                continue;

                            // Get title and questions content
                            var title = block.Content.Value<string>("title") ?? "Soru";
                            var questionsContent = block.Content.Value<string>("questions");

                            // Get formatted categories data
                            var categoriesData = block.Content.Value<IEnumerable<IPublishedContent>>("categories")?
                                .Select(c => new
                                {
                                    contentTypeAlias = c.ContentType.Alias,
                                    contentId = child.Id,
                                    contentKey = child.Key,
                                    title = c.Name,
                                    name = c.UrlSegment ?? "",
                                    icon = c.Value<IPublishedContent>("icon")?.Url(),
                                    defaultvalue = c.Value<bool?>("default") ?? false
                                })
                                .ToList();

                            // Generate URL with category
                            string? customUrl = null;
                            if (categoriesData != null && categoriesData.Any())
                            {
                                var originalUrl = child.Url(culture);
                                
                                if (!string.IsNullOrEmpty(originalUrl) && originalUrl != "#")
                                {
                                    // Extract base path and question slug
                                    var urlParts = originalUrl.Trim('/').Split('/');

                                    if (urlParts.Length >= 2)
                                    {
                                        // First part is base section (e.g. sikca-sorulan-sorular)
                                        var basePath = urlParts[0];

                                        var questionSlug = urlParts[urlParts.Length - 1];

                                        // Use the category parameter (already in URL segment format from component)
                                        customUrl = $"/{basePath}/{category}/{questionSlug}/";
                                    }
                                }
                                else
                                {
                                    // ⚡ FIX: Child URL is "#" - build from parent + UrlSegment
                                    var parentUrl = item.Url(culture);
                                    if (!string.IsNullOrEmpty(parentUrl) && parentUrl != "#")
                                    {
                                        var questionSlug = child.UrlSegment ?? child.Name.ToLowerInvariant().Replace(" ", "-");
                                        if (!string.IsNullOrEmpty(category))
                                        {
                                            customUrl = $"{parentUrl.TrimEnd('/')}/{category}/{questionSlug}/";
                                        }
                                    }
                                }
                            }

                            // Final URL: Ensure we always have a valid URL
                            var finalUrl = customUrl;
                            _logger.LogInformation("[DEBUG URL GEN 2] child.Url={ChildUrl}, customUrl={CustomUrl}, child.UrlSegment={UrlSegment}, category={Category}", 
                                child.Url(culture), customUrl, child.UrlSegment, category);
                            
                            if (string.IsNullOrEmpty(finalUrl) || finalUrl == "#")
                            {
                                // Last attempt: Force build from parent + category + slug
                                var parentUrl = item.Url(culture);
                                _logger.LogInformation("[DEBUG URL GEN 2] Fallback triggered - parentUrl={ParentUrl}", parentUrl);
                                
                                // ⚡ HARDCODED FALLBACK: If parent URL is also #, use hardcoded base path
                                if (string.IsNullOrEmpty(parentUrl) || parentUrl == "#")
                                {
                                    parentUrl = "/sikca-sorulan-sorular";
                                    _logger.LogInformation("[DEBUG URL GEN 2] Parent URL was #, using hardcoded: {ParentUrl}", parentUrl);
                                }
                                
                                var questionSlug = child.UrlSegment ?? child.Name.ToLowerInvariant().Replace(" ", "-");
                                if (!string.IsNullOrEmpty(category))
                                {
                                    finalUrl = $"{parentUrl.TrimEnd('/')}/{category}/{questionSlug}/";
                                    _logger.LogInformation("[DEBUG URL GEN 2] Built URL with category: {FinalUrl}", finalUrl);
                                }
                                else
                                {
                                    finalUrl = $"{parentUrl.TrimEnd('/')}/{questionSlug}/";
                                    _logger.LogInformation("[DEBUG URL GEN 2] Built URL without category: {FinalUrl}", finalUrl);
                                }
                            }
                            
                            _logger.LogInformation("[DEBUG URL GEN 2] FINAL URL for {ChildName}: {FinalUrl}", child.Name, finalUrl);

                            // Add to result for this category
                            resultByCategory[category].Add(new
                            {
                                contentType = "questionsContent",
                                content = new
                                {
                                    contentTypeAlias = "questionsContent",
                                    contentId = child.Id,
                                    contentKey = child.Key,
                                    title = title,
                                    questions = questionsContent,
                                    categories = categoriesData,
                                    url = finalUrl ?? "#"
                                }
                            });

                            categoryItemCount++;
                            if (categoryItemCount >= maxItemsForCategory)
                                break;
                        }
                    }
                }

                // Return just the flattened list of questions from all categories
                var combinedResults = new List<object>();
                foreach (var category in categoryList)
                {
                    combinedResults.AddRange(resultByCategory[category]);
                }

                // Return the flattened array without the questionsCollection wrapper
                return combinedResults;
            }
            else
            {
                _logger.LogInformation("[DEBUG GetSSSSorular] Single category mode - multiCategoriesContent = false");
                
                var result = new List<object>();
                int itemCount = 0;
                int maxItems = actualMaxQuestion;

                var childrenCount = item.Children().Count();
                _logger.LogInformation("[DEBUG GetSSSSorular] Processing {ChildrenCount} children", childrenCount);

                foreach (var child in item.Children())
                {
                    if (itemCount >= maxItems)
                        break;

                    _logger.LogInformation("[DEBUG GetSSSSorular] Processing child: {ChildName} (ID: {ChildId})", child.Name, child.Id);

                    // ✅ FIX: Culture kontrolü ekle
                    if (!child.HasCulture(culture))
                    {
                        _logger.LogInformation("[DEBUG GetSSSSorular] Skipping child '{ChildName}' - not available in culture '{Culture}'", 
                            child.Name, culture);
                        continue;
                    }

                    var homeBlockGrid = child.Value<BlockGridModel>("home", culture: culture);
                    if (homeBlockGrid == null) 
                    {
                        _logger.LogInformation("[DEBUG GetSSSSorular] Child {ChildName} has no home BlockGrid", child.Name);
                        continue;
                    }

                    var questionBlocks = homeBlockGrid.Where(b => b.Content?.ContentType?.Alias == "questionsContent").ToList();
                    if (!questionBlocks.Any()) 
                    {
                        _logger.LogInformation("[DEBUG GetSSSSorular] Child {ChildName} has no question blocks", child.Name);
                        continue;
                    }

                    _logger.LogInformation("[DEBUG GetSSSSorular] Child {ChildName} has {QuestionBlockCount} question blocks", child.Name, questionBlocks.Count);

                    foreach (var block in questionBlocks)
                    {
                        if (itemCount >= maxItems)
                            break;

                        var title = block.Content.Value<string>("title") ?? "Soru";
                        var questionsContent = block.Content.Value<string>("questions");

                        _logger.LogInformation("[DEBUG GetSSSSorular] Processing question block: {Title}", title);

                        // Get categories for this question block
                        var blockCategories = block.Content.Value<IEnumerable<IPublishedContent>>("categories")?
                            .Select(c => new
                            {
                                contentTypeAlias = c.ContentType.Alias,
                                contentId = child.Id,
                                contentKey = child.Key,
                                title = c.Name,
                                name = c.UrlSegment ?? "",
                                icon = c.Value<IPublishedContent>("icon")?.Url(),
                                defaultvalue = c.Value<bool?>("default") ?? false
                            })
                            .ToList();

                        var blockCategoryNames = blockCategories?.Select(c => c.title).ToList() ?? new List<string>();
                        _logger.LogInformation("[DEBUG GetSSSSorular] Question {Title} has categories: [{Categories}]", 
                            title, string.Join(", ", blockCategoryNames));

                        // If categories filter is specified, check if this block has any of the requested categories
                        if (categoryList != null && categoryList.Any())
                        {
                            _logger.LogInformation("[DEBUG GetSSSSorular] Filtering by categories: [{FilterCategories}]", 
                                string.Join(", ", categoryList));

                            // Skip this block if it doesn't have any of the requested categories
                            if (blockCategories == null || !blockCategories.Any(c =>
                            {
                                var normalizedBlockCategory = NormalizeCategoryName(c.title);
                                return categoryList.Any(filterCat => 
                                {
                                    var normalizedFilterCategory = NormalizeCategoryName(filterCat);
                                    var isMatch = string.Equals(normalizedBlockCategory, normalizedFilterCategory, StringComparison.OrdinalIgnoreCase);
                                    _logger.LogInformation("[DEBUG GetSSSSorular] Comparing '{BlockCat}' (normalized: '{NormBlockCat}') with '{FilterCat}' (normalized: '{NormFilterCat}') = {IsMatch}", 
                                        c.title, normalizedBlockCategory, filterCat, normalizedFilterCategory, isMatch);
                                    return isMatch;
                                });
                            }))
                            {
                                _logger.LogInformation("[DEBUG GetSSSSorular] Question {Title} skipped - no matching categories", title);
                                continue;
                            }
                            else
                            {
                                _logger.LogInformation("[DEBUG GetSSSSorular] Question {Title} matched category filter", title);
                            }
                        }
                        else
                        {
                            _logger.LogInformation("[DEBUG GetSSSSorular] No category filter applied - including question {Title}", title);
                        }

                        // Generate URL with category
                        string? customUrl = null;
                        if (blockCategories != null && blockCategories.Any())
                        {
                            var originalUrl = child.Url(culture);
                            
                            if (!string.IsNullOrEmpty(originalUrl) && originalUrl != "#")
                            {
                                // Extract base path and question slug
                                var urlParts = originalUrl.Trim('/').Split('/');

                                if (urlParts.Length >= 2)
                                {
                                    // First part is base section (e.g. sikca-sorulan-sorular)
                                    var basePath = urlParts[0];

                                    var questionSlug = urlParts[urlParts.Length - 1];

                                    // If a category is specified, use the first matching category
                                    // Otherwise use default category or first category
                                    string? categoryForUrl = null;

                                    if (categoryList != null && categoryList.Any())
                                    {
                                        // Find first matching category by name (UrlSegment)
                                        // Use normalized comparison for matching
                                        var matchedCategory = blockCategories.FirstOrDefault(c => 
                                            categoryList.Any(filterCat => 
                                                TextNormalizationHelper.AreCategoriesEqual(c.title, filterCat)
                                            )
                                        );
                                        categoryForUrl = matchedCategory?.name; // Use UrlSegment (name), not title
                                    }

                                    if (string.IsNullOrEmpty(categoryForUrl))
                                    {
                                        var firstCategory = blockCategories.FirstOrDefault(c => c.defaultvalue) ??
                                                           blockCategories.FirstOrDefault();
                                        categoryForUrl = firstCategory?.name; // Use UrlSegment (name), not title
                                    }

                                    if (!string.IsNullOrEmpty(categoryForUrl))
                                    {
                                        customUrl = $"/{basePath}/{categoryForUrl}/{questionSlug}/";
                                    }
                                }
                            }
                            else
                            {
                                // ⚡ FIX: Child URL is "#" - build from parent + UrlSegment
                                var parentUrl = item.Url(culture);
                                if (!string.IsNullOrEmpty(parentUrl) && parentUrl != "#")
                                {
                                    var questionSlug = child.UrlSegment ?? child.Name.ToLowerInvariant().Replace(" ", "-");
                                    
                                    string? categoryForUrl = null;
                                    if (categoryList != null && categoryList.Any())
                                    {
                                        var matchedCategory = blockCategories.FirstOrDefault(c => 
                                            categoryList.Any(filterCat => 
                                                TextNormalizationHelper.AreCategoriesEqual(c.title, filterCat)
                                            )
                                        );
                                        categoryForUrl = matchedCategory?.name;
                                    }
                                    
                                    if (string.IsNullOrEmpty(categoryForUrl))
                                    {
                                        var firstCategory = blockCategories.FirstOrDefault(c => c.defaultvalue) ??
                                                           blockCategories.FirstOrDefault();
                                        categoryForUrl = firstCategory?.name;
                                    }
                                    
                                    if (!string.IsNullOrEmpty(categoryForUrl))
                                    {
                                        customUrl = $"{parentUrl.TrimEnd('/')}/{categoryForUrl}/{questionSlug}/";
                                    }
                                }
                            }
                        }

                        // Final URL: Ensure we always have a valid URL
                        var finalUrl = customUrl;
                        _logger.LogInformation("[DEBUG URL GEN 3] child.Url={ChildUrl}, customUrl={CustomUrl}, child.UrlSegment={UrlSegment}", 
                            child.Url(culture), customUrl, child.UrlSegment);
                        
                        if (string.IsNullOrEmpty(finalUrl) || finalUrl == "#")
                        {
                            // Last attempt: Force build from parent + category + slug
                            var parentUrl = item.Url(culture);
                            _logger.LogInformation("[DEBUG URL GEN 3] Fallback triggered - parentUrl={ParentUrl}", parentUrl);
                            
                            // ⚡ HARDCODED FALLBACK: If parent URL is also #, use hardcoded base path
                            if (string.IsNullOrEmpty(parentUrl) || parentUrl == "#")
                            {
                                parentUrl = "/sikca-sorulan-sorular";
                                _logger.LogInformation("[DEBUG URL GEN 3] Parent URL was #, using hardcoded: {ParentUrl}", parentUrl);
                            }
                            
                            var questionSlug = child.UrlSegment ?? child.Name.ToLowerInvariant().Replace(" ", "-");
                            var firstCategory = blockCategories?.FirstOrDefault(c => c.defaultvalue) ??
                                               blockCategories?.FirstOrDefault();
                            if (!string.IsNullOrEmpty(firstCategory?.name))
                            {
                                finalUrl = $"{parentUrl.TrimEnd('/')}/{firstCategory.name}/{questionSlug}/";
                                _logger.LogInformation("[DEBUG URL GEN 3] Built URL with category: {FinalUrl}", finalUrl);
                            }
                            else
                            {
                                finalUrl = $"{parentUrl.TrimEnd('/')}/{questionSlug}/";
                                _logger.LogInformation("[DEBUG URL GEN 3] Built URL without category: {FinalUrl}", finalUrl);
                            }
                        }
                        
                        _logger.LogInformation("[DEBUG URL GEN 3] FINAL URL for {ChildName}: {FinalUrl}", child.Name, finalUrl);

                        // Add to result
                        result.Add(new
                        {
                            contentType = "questionsContent",
                            content = new
                            {
                                contentTypeAlias = "questionsContent",
                                contentId = child.Id,
                                contentKey = child.Key,
                                title = title,
                                questions = questionsContent,
                                categories = blockCategories,
                                url = finalUrl ?? "#"
                            }
                        });

                        itemCount++;
                        if (itemCount >= maxItems)
                            break;
                    }
                }

                // Return just the array without the questionsCollection wrapper
                return result;
            }
        }

        private string StripHtmlTags(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            // Simple HTML tag removal
            return System.Text.RegularExpressions.Regex.Replace(input, "<.*?>", string.Empty);
        }
    }
}
