using Morpara.Services.Interfaces;
using System.Text.RegularExpressions;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;
using Microsoft.Extensions.Logging;

namespace Morpara.Services
{
    public class PropertyMappingService : IPropertyMappingService
    {
        private readonly IUmbracoContextAccessor _ctxAccessor;
        private readonly IVariationContextAccessor _variationContextAccessor;
        private readonly IMenuService _menuService;
        private readonly IBlogService _blogService;
        private readonly IQuestionService _questionService;
        private readonly ICampaignService _campaignService;
        private readonly ILocationService _locationService;
        private readonly IContentRetrievalService _contentRetrievalService;
        private readonly ILogger<PropertyMappingService> _logger;

        public PropertyMappingService(
            IUmbracoContextAccessor ctxAccessor,
            IVariationContextAccessor variationContextAccessor,
            IMenuService menuService,
            IBlogService blogService,
            IQuestionService questionService,
            ICampaignService campaignService,
            ILocationService locationService,
            IContentRetrievalService contentRetrievalService,
            ILogger<PropertyMappingService> logger)
        {
            _ctxAccessor = ctxAccessor;
            _variationContextAccessor = variationContextAccessor;
            _menuService = menuService;
            _blogService = blogService;
            _questionService = questionService;
            _campaignService = campaignService;
            _locationService = locationService;
            _contentRetrievalService = contentRetrievalService;
            _logger = logger;
        }

        private Dictionary<string, string>? GetComponentFilterParams(
            Dictionary<string, Dictionary<string, string>>? filterParams,
            string componentId)
        {
            if (filterParams == null || !filterParams.ContainsKey(componentId))
                return null;
            return filterParams[componentId];
        }

        private string SafeToString(object? obj) => obj?.ToString() ?? string.Empty;

        private string GenerateFormKey(string title)
        {
            if (string.IsNullOrEmpty(title))
                return string.Empty;

            // Turkish to English character mapping (all lowercase)
            var turkishToEnglish = new Dictionary<char, char>
            {
                {'ç', 'c'}, {'Ç', 'c'},
                {'ğ', 'g'}, {'Ğ', 'g'},
                {'ı', 'i'}, {'I', 'i'},
                {'İ', 'i'}, {'i', 'i'},
                {'ö', 'o'}, {'Ö', 'o'},
                {'ş', 's'}, {'Ş', 's'},
                {'ü', 'u'}, {'Ü', 'u'}
            };

            var result = title.ToLowerInvariant();
            
            // Replace Turkish characters
            foreach (var pair in turkishToEnglish)
            {
                result = result.Replace(pair.Key, pair.Value);
            }

            // Remove spaces and special characters, keep only alphanumeric and convert to lowercase
            result = string.Concat(result.Where(c => char.IsLetterOrDigit(c))).ToLowerInvariant();

            return result;
        }

        public object? MapValue(IPublishedProperty prop, object? contentId = null, object? contentKey = null,
            Dictionary<string, Dictionary<string, string>>? filterParams = null, string? originalUrl = null, string? categoryName = null)
        {
            var value = prop.GetValue();
            var alias = prop.Alias;

            if (value is IEnumerable<IPublishedElement> elementCollection)
            {
                return elementCollection
                    .Select(elem => ConvertElementToDictionary(elem, contentId, contentKey))
                    .ToList();
            }

            if (value is IPublishedElement singleElement)
            {
                return ConvertElementToDictionary(singleElement, contentId, contentKey);
            }

            if (value is IEnumerable<IPublishedContent> publishedContentList)
            {
                return publishedContentList.Select(c =>
                {
                    // moneyTransferList için children döndür
                    if (c.ContentType.Alias == "moneyTransferList")
                    {
                        var childrenData = c.Children().Select(child => ConvertPublishedContentToDictionary(child)).ToList();
                        return (object)new
                        {
                            contentTypeAlias = c.ContentType.Alias,
                            contentId = c.Id,
                            contentKey = c.Key,
                            children = childrenData
                        };
                    }
                    
                    // Diğer content type'lar için eski format
                    return (object)new
                    {
                        Id = c.Key,
                        Name = c.Name,
                        Url = c.Url()
                    };
                }).ToList();
            }

            if (value is IPublishedContent publishedContent)
            {
                // moneyTransferList için children döndür
                if (publishedContent.ContentType.Alias == "moneyTransferList")
                {
                    var childrenData = publishedContent.Children().Select(child => ConvertPublishedContentToDictionary(child)).ToList();
                    return new
                    {
                        contentTypeAlias = publishedContent.ContentType.Alias,
                        contentId = publishedContent.Id,
                        contentKey = publishedContent.Key,
                        children = childrenData
                    };
                }
                
                // Diğer content type'lar için eski format
                return new
                {
                    Id = publishedContent.Key,
                    Name = publishedContent.Name,
                    Url = publishedContent.Url()
                };
            }

            if (value is BlockGridModel blockGrid)
            {
                return MapBlockGridModel(blockGrid, contentId, contentKey, filterParams);
            }

            if (value is BlockListModel blockList)
            {
                return blockList.Select(item =>
                {
                    var contentType = item.Content?.ContentType?.Alias;
                    
                    // Special handling for representationList
                    if (contentType == "representationList")
                    {
                        return MapRepresentationListBlock(item, contentId, contentKey, filterParams);
                    }

                    // Special handling for form-related content types
                    if (contentType != null && (contentType.Contains("form", StringComparison.OrdinalIgnoreCase) || 
                                              contentType.Contains("Form", StringComparison.Ordinal)))
                    {
                        return new
                        {
                            ContentType = contentType,
                            Content = item.Content != null ? FilterFormContentProperties(item.Content, contentId, contentKey) : null,
                            Settings = item.Settings != null ? ConvertElementToDictionary(item.Settings, contentId, contentKey) : null
                        };
                    }

                    return new
                    {
                        ContentType = contentType,
                        Content = item.Content != null ? ConvertElementToDictionary(item.Content, contentId, contentKey) : null,
                        Settings = item.Settings != null ? ConvertElementToDictionary(item.Settings, contentId, contentKey) : null
                    };
                }).ToList();
            }

            if (value is IEnumerable<Link> linkList)
            {
                return linkList.Select(link => 
                {
                    string linkUrl = link.Url ?? string.Empty;
                    object? linkedContentId = null;
                    
                    // Eğer link internal content'e işaret ediyorsa kültüre göre URL al
                    if (link.Udi != null)
                    {
                        try
                        {
                            using var ctx = _ctxAccessor.GetRequiredUmbracoContext();
                            var linkedContent = ctx.Content.GetById(link.Udi);
                            if (linkedContent != null)
                            {
                                linkedContentId = linkedContent.Id;
                                // Mevcut kültürü al
                                var currentCulture = _variationContextAccessor.VariationContext?.Culture ?? "tr-TR";
                                var baseUrl = linkedContent.Url(currentCulture) ?? "";
                                
                                // Anchor kısmını orijinal URL'den al ve yeni URL'ye ekle
                                var originalUrl = link.Url ?? "";
                                var anchorIndex = originalUrl.IndexOf('#');
                                if (anchorIndex >= 0)
                                {
                                    var anchor = originalUrl.Substring(anchorIndex);
                                    linkUrl = baseUrl + anchor;
                                }
                                else
                                {
                                    linkUrl = baseUrl;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // Hata durumunda orijinal URL'yi kullan
                            linkUrl = link.Url ?? string.Empty;
                            _logger?.LogWarning("Error getting culture-specific URL for link in list: {Error}", ex.Message);
                        }
                    }
                    
                    // Eğer Udi yoksa veya resolve edilemezse, Name'e göre content bulmaya çalış
                    if (linkedContentId == null && !string.IsNullOrEmpty(link.Name))
                    {
                        try
                        {
                            using var ctx = _ctxAccessor.GetRequiredUmbracoContext();
                            var currentCulture = _variationContextAccessor.VariationContext?.Culture ?? "tr-TR";
                            
                            // Name'e göre content ara (URL segment olarak)
                            var allContent = ctx.Content.GetAtRoot().SelectMany(x => x.DescendantsOrSelf());
                            var matchingContent = allContent.FirstOrDefault(c => 
                                c.UrlSegment?.Equals(link.Name, StringComparison.OrdinalIgnoreCase) == true ||
                                c.Name?.Equals(link.Name, StringComparison.OrdinalIgnoreCase) == true);
                                
                            if (matchingContent != null)
                            {
                                linkedContentId = matchingContent.Id;
                                var baseUrl = matchingContent.Url(currentCulture) ?? "";
                                
                                // Anchor kısmını orijinal URL'den al ve yeni URL'ye ekle
                                var originalUrl = link.Url ?? "";
                                var anchorIndex = originalUrl.IndexOf('#');
                                if (anchorIndex >= 0)
                                {
                                    var anchor = originalUrl.Substring(anchorIndex);
                                    linkUrl = baseUrl + anchor;
                                }
                                else
                                {
                                    linkUrl = baseUrl;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning("Error finding content by name for link in list: {Name}, Error={Error}", 
                                link.Name, ex.Message);
                        }
                    }
                    
                    // Son kontrol: Eğer URL hala # ise ama ContentId varsa, o ID'den URL al
                    if ((linkUrl == "#" || linkUrl == string.Empty) && linkedContentId != null)
                    {
                        try
                        {
                            using var ctx = _ctxAccessor.GetRequiredUmbracoContext();
                            var currentCulture = _variationContextAccessor.VariationContext?.Culture ?? "tr-TR";
                            
                            if (int.TryParse(linkedContentId.ToString(), out int contentIdInt))
                            {
                                var contentById = ctx.Content.GetById(contentIdInt);
                                if (contentById != null)
                                {
                                    var baseUrl = contentById.Url(currentCulture) ?? "#";
                                    
                                    // Anchor kısmını orijinal URL'den al ve yeni URL'ye ekle
                                    var originalUrl = link.Url ?? "";
                                    var anchorIndex = originalUrl.IndexOf('#');
                                    if (anchorIndex >= 0)
                                    {
                                        var anchor = originalUrl.Substring(anchorIndex);
                                        linkUrl = baseUrl + anchor;
                                    }
                                    else
                                    {
                                        linkUrl = baseUrl;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning("Error resolving URL from ContentId in list: {ContentId}, Error={Error}", 
                                linkedContentId, ex.Message);
                        }
                    }
                    
                    return new
                    {
                        Url = linkUrl.Replace("##", "#"),
                        Name = link.Name,
                        Target = link.Target,
                        ContentId = linkedContentId
                    };
                }).ToList();
            }

            if (value is Link link)
            {
                string linkUrl = link.Url ?? string.Empty;
                object? linkedContentId = null;
                
                // Eğer link internal content'e işaret ediyorsa kültüre göre URL al
                if (link.Udi != null)
                {
                    try
                    {
                        using var ctx = _ctxAccessor.GetRequiredUmbracoContext();
                        var linkedContent = ctx.Content.GetById(link.Udi);
                        if (linkedContent != null)
                        {
                            linkedContentId = linkedContent.Id;
                            // Mevcut kültürü al
                            var currentCulture = _variationContextAccessor.VariationContext?.Culture ?? "tr-TR";
                            var baseUrl = linkedContent.Url(currentCulture) ?? "";
                            
                            // Anchor kısmını orijinal URL'den al ve yeni URL'ye ekle
                            var linkOriginalUrl = link.Url ?? "";
                            var anchorIndex = linkOriginalUrl.IndexOf('#');
                            if (anchorIndex >= 0)
                            {
                                var anchor = linkOriginalUrl.Substring(anchorIndex);
                                linkUrl = baseUrl + anchor;
                            }
                            else
                            {
                                linkUrl = baseUrl;
                            }
                            
                            _logger?.LogInformation("Link resolved: Name={Name}, ContentId={ContentId}, Culture={Culture}, URL={URL}", 
                                link.Name, linkedContentId, currentCulture, linkUrl);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Hata durumunda orijinal URL'yi kullan
                        linkUrl = link.Url ?? string.Empty;
                        _logger?.LogWarning("Error getting culture-specific URL for link: {Error}", ex.Message);
                    }
                }
                
                // Eğer Udi yoksa veya resolve edilemezse, Name'e göre content bulmaya çalış
                if (linkedContentId == null && !string.IsNullOrEmpty(link.Name))
                {
                    try
                    {
                        using var ctx = _ctxAccessor.GetRequiredUmbracoContext();
                        var currentCulture = _variationContextAccessor.VariationContext?.Culture ?? "tr-TR";
                        
                        // Name'e göre content ara (URL segment olarak)
                        var allContent = ctx.Content.GetAtRoot().SelectMany(x => x.DescendantsOrSelf());
                        var matchingContent = allContent.FirstOrDefault(c => 
                            c.UrlSegment?.Equals(link.Name, StringComparison.OrdinalIgnoreCase) == true ||
                            c.Name?.Equals(link.Name, StringComparison.OrdinalIgnoreCase) == true);
                            
                        if (matchingContent != null)
                        {
                            linkedContentId = matchingContent.Id;
                            linkUrl = matchingContent.Url(currentCulture) ?? link.Url ?? string.Empty;
                            
                            _logger?.LogInformation("Link resolved by name: Name={Name}, ContentId={ContentId}, Culture={Culture}, URL={URL}", 
                                link.Name, linkedContentId, currentCulture, linkUrl);
                        }
                        else
                        {
                            _logger?.LogWarning("Could not find content for link name: {Name}, Culture={Culture}", 
                                link.Name, currentCulture);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning("Error finding content by name for link: {Name}, Error={Error}", 
                            link.Name, ex.Message);
                    }
                }
                
                // Son kontrol: Eğer URL hala # ise ama ContentId varsa, o ID'den URL al
                if ((linkUrl == "#" || linkUrl == string.Empty) && linkedContentId != null)
                {
                    try
                    {
                        using var ctx = _ctxAccessor.GetRequiredUmbracoContext();
                        var currentCulture = _variationContextAccessor.VariationContext?.Culture ?? "tr-TR";
                        
                        if (int.TryParse(linkedContentId.ToString(), out int contentIdInt))
                        {
                            var contentById = ctx.Content.GetById(contentIdInt);
                            if (contentById != null)
                            {
                                linkUrl = contentById.Url(currentCulture) ?? "#";
                                _logger?.LogInformation("URL resolved from ContentId: ContentId={ContentId}, Culture={Culture}, URL={URL}", 
                                    linkedContentId, currentCulture, linkUrl);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning("Error resolving URL from ContentId: {ContentId}, Error={Error}", 
                            linkedContentId, ex.Message);
                    }
                }
                
                return new
                {
                    Url = linkUrl.Replace("##", "#"),
                    Name = link.Name,
                    Target = link.Target,
                    ContentId = linkedContentId
                };
            }

            if (value is IEnumerable<string> stringList)
            {
                return stringList.ToList();
            }

            if (value is string[] stringArray)
            {
                return stringArray.ToList();
            }

            if (value is MediaWithCrops mediaWithCrops)
            {
                return new
                {
                    Url = mediaWithCrops.MediaUrl(),
                    Name = mediaWithCrops.Name
                };
            }

            if (value != null && (value.GetType().IsPrimitive || value is string))
            {
                return value;
            }

            return value?.ToString();
        }

        private object MapBlockGridModel(BlockGridModel blockGrid, object? contentId, object? contentKey, 
            Dictionary<string, Dictionary<string, string>>? filterParams)
        {
            _logger.LogInformation("[DEBUG MapBlockGridModel] Processing BlockGrid with {BlockCount} blocks for contentId: {ContentId}", 
                blockGrid?.Count() ?? 0, contentId);
            
            // Log all block types in the grid
            var blockTypes = blockGrid?.Select(b => b.Content?.ContentType?.Alias ?? "null").ToList() ?? new List<string>();
            _logger.LogInformation("[DEBUG MapBlockGridModel] Block types found: [{BlockTypes}]", string.Join(", ", blockTypes));
            
            return blockGrid.Select<BlockGridItem, object>(item =>
            {
                var contentType = item.Content?.ContentType?.Alias;
                
                _logger.LogInformation("[DEBUG MapBlockGridModel] Processing block with contentType: '{ContentType}', contentId: {ContentId}", contentType, contentId);
                
                if (contentType == "questionsContent" || contentType == "questionsPage")
                {
                    _logger.LogInformation("[DEBUG MapBlockGridModel] Found questions-related block: '{ContentType}', contentId: {ContentId}, hasFilterParams: {HasFilterParams}", 
                        contentType, contentId, filterParams != null);
                    
                    if (filterParams != null)
                    {
                        _logger.LogInformation("[DEBUG MapBlockGridModel] Available filter params: [{FilterKeys}]", 
                            string.Join(", ", filterParams.Keys));
                    }
                }

                switch (contentType)
                {
                    case "staticPage" when contentId != null:
                        return new
                        {
                            ContentType = contentType,
                            Content = item.Content != null ? ConvertElementToDictionary(item.Content, contentId, contentKey) : null,
                            Settings = item.Settings != null ? ConvertElementToDictionary(item.Settings, contentId, contentKey) : null,
                            StaticMenuItems = _menuService.GetStaticMenuItems(contentId.ToString() ?? string.Empty)
                        };

                    case "staticSubPage":
                        return new
                        {
                            ContentType = contentType,
                            Content = item.Content != null ? ConvertElementToDictionary(item.Content, contentId, contentKey) : null,
                            Settings = item.Settings != null ? ConvertElementToDictionary(item.Settings, contentId, contentKey) : null,
                            StaticMenuItems = _menuService.GetStaticAbMenuItems(contentId?.ToString() ?? string.Empty)
                        };
//kkk
                    case "blogPost" when contentId != null:
                        if (_ctxAccessor.TryGetUmbracoContext(out var ctx))
                        {
                            IPublishedContent? blogContent = null;
                            if (contentId is int id)
                                blogContent = ctx.Content?.GetById(id);
                            else if (contentId is string idStr && int.TryParse(idStr, out var intId))
                                blogContent = ctx.Content?.GetById(intId);

                            if (blogContent != null)
                            {
                                return _blogService.GetBlogPost(blogContent);
                            }
                        }
                        break;

                    case "blogList" when contentId != null:
                        return MapBlogListBlock(item, blockGrid, contentId, contentKey, filterParams);

                    case "blogFeatured" when contentId != null:
                        return MapBlogFeaturedBlock(item, blockGrid, contentId, contentKey, filterParams);

                    case "campingList" when contentId != null:
                        return MapCampaignListBlock(item, blockGrid, contentId, contentKey, filterParams);

                    case "campingSlider" when contentId != null:
                        return MapCampaignSliderBlock(item, blockGrid, contentId, contentKey, filterParams);

                    case "representationList":
                        return MapRepresentationListBlock(item, blockGrid, contentId, contentKey, filterParams);

                    case "representationMap" when contentId != null:
                        return MapRepresentationMapBlock(item, blockGrid, contentId, contentKey, filterParams);

                    case "questionsList" when contentId != null:
                        return MapQuestionsListBlock(item, contentId, contentKey);

                    case "questionsPage" when contentId != null:
                        return MapQuestionsPageBlock(item, blockGrid, contentId, contentKey, filterParams);

                    case "questionsContent":
                        _logger.LogInformation("[DEBUG MapBlockGridModel] Processing questionsContent block with contentId: {ContentId}", contentId);
                        return MapQuestionsPageBlock(item, blockGrid, contentId, contentKey, filterParams);

                    case "formSecond" when contentId != null:
                    case "formType" when contentId != null:
                    case "formBlock" when contentId != null:
                        return MapFormBlock(item, contentType, contentId, contentKey);
                    
                    case "formBlock":
                        return MapFormBlock(item, contentType, contentId, contentKey);
                    
                    case "transactionFeeCountryList":
                        return MapTransactionFeeCountryListBlock(item, contentId, contentKey);
                }

                // Special handling for representationList if not caught by case above
                if (contentType == "representationList")
                {
                    return MapRepresentationListBlock(item, blockGrid, contentId, contentKey, filterParams);
                }

                // Special handling for heroBanner - provide default settings if null or apply defaults for empty values
                if (contentType == "heroBanner")
                {
                    Dictionary<string, object> settings;
                    
                    if (item.Settings != null)
                    {
                        settings = ConvertElementToDictionary(item.Settings, contentId, contentKey);
                        
                        // titleSize boşsa default değer ata
                        if (!settings.ContainsKey("titleSize") || string.IsNullOrEmpty(settings["titleSize"]?.ToString()))
                        {
                            settings["titleSize"] = "52px";
                        }
                        
                        // titleMobileSize boşsa default değer ata
                        if (!settings.ContainsKey("titleMobileSize") || string.IsNullOrEmpty(settings["titleMobileSize"]?.ToString()))
                        {
                            settings["titleMobileSize"] = "34px";
                        }
                    }
                    else
                    {
                        settings = new Dictionary<string, object>
                        {
                            { "titleSize", "52px" },
                            { "titleMobileSize", "34px" },
                            { "margin", "" },
                            { "marginMobile", "" },
                            { "padding", "" },
                            { "paddingMobile", "" },
                            { "orientation", "" }
                        };
                    }
                    
                    return new
                    {
                        ContentType = contentType,
                        Content = item.Content != null ? ConvertElementToDictionary(item.Content, contentId, contentKey) : null,
                        Settings = settings
                    };
                }

                // Special handling for basicContentBlock - provide default settings if null or apply defaults for empty values
                if (contentType == "basicContentBlock")
                {
                    Dictionary<string, object> settings;
                    
                    if (item.Settings != null)
                    {
                        settings = ConvertElementToDictionary(item.Settings, contentId, contentKey);
                        
                        // titleSize boşsa default değer ata
                        if (!settings.ContainsKey("titleSize") || string.IsNullOrEmpty(settings["titleSize"]?.ToString()))
                        {
                            settings["titleSize"] = "40px";
                        }
                        
                        // titleMobileSize boşsa default değer ata
                        if (!settings.ContainsKey("titleMobileSize") || string.IsNullOrEmpty(settings["titleMobileSize"]?.ToString()))
                        {
                            settings["titleMobileSize"] = "36px";
                        }
                    }
                    else
                    {
                        settings = new Dictionary<string, object>
                        {
                            { "titleSize", "40px" },
                            { "titleMobileSize", "36px" },
                            { "margin", "" },
                            { "marginMobile", "" },
                            { "padding", "" },
                            { "paddingMobile", "" },
                            { "orientation", "" }
                        };
                    }
                    
                    return new
                    {
                        ContentType = contentType,
                        Content = item.Content != null ? ConvertElementToDictionary(item.Content, contentId, contentKey) : null,
                        Settings = settings
                    };
                }

                // Special handling for sliders - provide default settings if null or apply defaults for empty values
                if (contentType == "sliders")
                {
                    Dictionary<string, object> settings;
                    
                    if (item.Settings != null)
                    {
                        settings = ConvertElementToDictionary(item.Settings, contentId, contentKey);
                        
                        // titleSize boşsa default değer ata
                        if (!settings.ContainsKey("titleSize") || string.IsNullOrEmpty(settings["titleSize"]?.ToString()))
                        {
                            settings["titleSize"] = "52px";
                        }
                        
                        // titleMobileSize boşsa default değer ata
                        if (!settings.ContainsKey("titleMobileSize") || string.IsNullOrEmpty(settings["titleMobileSize"]?.ToString()))
                        {
                            settings["titleMobileSize"] = "34px";
                        }
                    }
                    else
                    {
                        settings = new Dictionary<string, object>
                        {
                            { "titleSize", "52px" },
                            { "titleMobileSize", "34px" },
                            { "margin", "" },
                            { "marginMobile", "" },
                            { "padding", "" },
                            { "paddingMobile", "" },
                            { "orientation", "" }
                        };
                    }
                    
                    return new
                    {
                        ContentType = contentType,
                        Content = item.Content != null ? ConvertElementToDictionary(item.Content, contentId, contentKey) : null,
                        Settings = settings
                    };
                }

                return new
                {
                    ContentType = contentType,
                    Content = item.Content != null ? ConvertElementToDictionary(item.Content, contentId, contentKey) : null,
                    Settings = item.Settings != null ? ConvertElementToDictionary(item.Settings, contentId, contentKey) : null
                };

            }).ToList();
        }

        private object MapBlogListBlock(BlockGridItem item, BlockGridModel blockGrid, object contentId, object? contentKey,
            Dictionary<string, Dictionary<string, string>>? filterParams)
        {
            string blockIndex = GetBlockIndex(item, blockGrid).ToString();
            string componentId = $"blogList{{{contentId}}}-{blockIndex}";

            bool hasComponentFilter = filterParams != null && filterParams.ContainsKey(componentId);
            Dictionary<string, string>? componentFilterParams = hasComponentFilter && filterParams != null ? filterParams[componentId] : null;

            int pageSize = item.Content?.Value<int?>("pageSize") ?? 9;
            string sortOrder = item.Content?.Value<string>("blogOrder") ?? "Date ASC";

            int page = 1;
            string? category = null;

            if (componentFilterParams != null)
            {
                if (componentFilterParams.TryGetValue("page", out var pageStr) &&
                    int.TryParse(pageStr, out var parsedPage))
                {
                    page = parsedPage;
                }

                if (componentFilterParams.TryGetValue("categories", out var categoryValue))
                {
                    category = categoryValue;
                }
            }

                                    var blogs = _blogService.Bloglist(contentId?.ToString() ?? string.Empty, page, pageSize, sortOrder, category);

            return new
            {
                ContentType = "blogList",
                ComponentId = componentId,
                Content = item.Content != null ? ConvertElementToDictionary(item.Content, contentId, contentKey) : null,
                Settings = item.Settings != null ? ConvertElementToDictionary(item.Settings, contentId, contentKey) : null,
                Blogs = blogs,
                AppliedFilters = componentFilterParams
            };
        }

        private object MapBlogFeaturedBlock(BlockGridItem item, BlockGridModel blockGrid, object contentId, object? contentKey,
            Dictionary<string, Dictionary<string, string>>? filterParams)
        {
            string blockIndex = GetBlockIndex(item, blockGrid).ToString();
            string componentId = $"blogFeatured{{{contentId}}}-{blockIndex}";

            Dictionary<string, string>? componentFilterParams = GetComponentFilterParams(filterParams, componentId);

            int pageSize = item.Content?.Value<int?>("pageSize") ?? 3;
            string sortOrder = item.Content?.Value<string>("blogOrder") ?? "Date DESC";

            int page = 1;
            string? category = null;

            if (componentFilterParams != null)
            {
                if (componentFilterParams.TryGetValue("page", out var pageStr) &&
                    int.TryParse(pageStr, out var parsedPage))
                {
                    page = parsedPage;
                }

                if (componentFilterParams.TryGetValue("categories", out var categoryValue))
                {
                    category = categoryValue;
                }
            }

                                    var blogs = _blogService.FeaturedBlogList(SafeToString(contentId), page, pageSize, sortOrder, category);

            return new
            {
                ContentType = "blogFeatured",
                ComponentId = componentId,
                Content = item.Content != null ? ConvertElementToDictionary(item.Content, contentId, contentKey) : null,
                Settings = item.Settings != null ? ConvertElementToDictionary(item.Settings, contentId, contentKey) : null,
                Blogs = blogs,
                AppliedFilters = componentFilterParams
            };
        }

        private object MapCampaignListBlock(BlockGridItem item, BlockGridModel blockGrid, object contentId, object? contentKey,
            Dictionary<string, Dictionary<string, string>>? filterParams)
        {
            string blockIndex = GetBlockIndex(item, blockGrid).ToString();
            string componentId = $"campingList{{{contentId}}}-{blockIndex}";

            bool hasComponentFilter = filterParams != null && filterParams.ContainsKey(componentId);
            Dictionary<string, string>? componentFilterParams = hasComponentFilter && filterParams != null ? filterParams[componentId] : null;

            int pageSize = item.Content?.Value<int?>("campingPageSize") ?? 10;
            string sortOrder = item.Content?.Value<string>("campingsOrder") ?? "A-Z";
            string campaignType = item.Content?.Value<string>("campaignsType") ?? "Past Campaigns";

            int page = 1;
            string? category = null;

            if (componentFilterParams != null)
            {
                if (componentFilterParams.TryGetValue("page", out var pageStr) &&
                    int.TryParse(pageStr, out var parsedPage))
                {
                    page = parsedPage;
                }

                if (componentFilterParams.TryGetValue("categories", out var categoryValue))
                {
                    category = categoryValue;
                }
            }

                                    var campaigns = _campaignService.Gecmiskmp(
                contentId?.ToString() ?? string.Empty,
                page,
                pageSize,
                sortOrder,
                category,
                campaignType
            );

            return new
            {
                ContentType = "campingList",
                ComponentId = componentId,
                Campaigns = campaigns,
                Settings = item.Settings != null ? ConvertElementToDictionary(item.Settings, contentId, contentKey) : null,
                AppliedFilters = componentFilterParams
            };
        }

        private object MapCampaignSliderBlock(BlockGridItem item, BlockGridModel blockGrid, object contentId, object? contentKey,
            Dictionary<string, Dictionary<string, string>>? filterParams)
        {
            string blockIndex = GetBlockIndex(item, blockGrid).ToString();
            string componentId = $"campingSlider{{{contentId}}}-{blockIndex}";

            bool hasComponentFilter = filterParams != null && filterParams.ContainsKey(componentId);
            Dictionary<string, string>? componentFilterParams = hasComponentFilter && filterParams != null ? filterParams[componentId] : null;

            int pageSize = item.Content?.Value<int?>("campingMaxSlider") ?? 20;
            string sortOrder = item.Content?.Value<string>("campingsOrder") ?? "None";
            string campaignType = item.Content?.Value<string>("campaignsType") ?? "Current Campaigns";

            int page = 1;
            string? category = null;

            if (componentFilterParams != null)
            {
                if (componentFilterParams.TryGetValue("page", out var pageStr) &&
                    int.TryParse(pageStr, out var parsedPage))
                {
                    page = parsedPage;
                }

                if (componentFilterParams.TryGetValue("categories", out var categoryValue))
                {
                    category = categoryValue;
                }
            }

            // Get campaigns folder ID from campingsFolder Link
            string campaignsId = contentId?.ToString() ?? string.Empty;
            var campingsFolder = item.Content?.Value<Link>("campingsFolder");
            
            if (campingsFolder != null && !string.IsNullOrEmpty(campingsFolder.Url))
            {
                try
                {
                    // Use ContentRetrievalService to find content by URL
                    var (campaignsContent, categoryName) = _contentRetrievalService.GetContentByUrl(campingsFolder.Url, "tr-TR");
                    if (campaignsContent != null)
                    {
                        campaignsId = campaignsContent.Id.ToString();
                        _logger.LogInformation("[DEBUG] Found campaigns folder content ID: {CampaignsId} for URL: {Url}", campaignsId, campingsFolder.Url);
                    }
                    else
                    {
                        _logger.LogWarning("[DEBUG] Could not find content for URL: {Url}", campingsFolder.Url);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[DEBUG] Error finding content for URL: {Url}", campingsFolder.Url);
                }
            }
            else
            {
                _logger.LogInformation("[DEBUG] No campingsFolder specified, using contentId: {ContentId}", contentId);
            }

            var campaigns = _campaignService.Gecmiskmp(
                campaignsId,
                page,
                pageSize,
                sortOrder,
                category,
                campaignType
            );

            return new
            {
                ContentType = "campingSlider",
                ComponentId = componentId,
                Content = item.Content != null ? ConvertElementToDictionary(item.Content, contentId, contentKey) : null,
                Settings = item.Settings != null ? ConvertElementToDictionary(item.Settings, contentId, contentKey) : null,
                Campaigns = campaigns,
                AppliedFilters = componentFilterParams
            };
        }

        private object MapRepresentationListBlock(BlockGridItem item, BlockGridModel blockGrid, object? contentId, object? contentKey,
            Dictionary<string, Dictionary<string, string>>? filterParams)
        {
            // Debug log to see if this method is called
            _logger.LogInformation("MapRepresentationListBlock called!");
            string blockIndex = GetBlockIndex(item, blockGrid).ToString();
            string componentId = $"representationList{{{contentId}}}-{blockIndex}";

            bool hasComponentFilter = filterParams != null && filterParams.ContainsKey(componentId);
            Dictionary<string, string>? componentFilterParams = hasComponentFilter && filterParams != null ? filterParams[componentId] : null;

            int pageSize = item.Content?.Value<int?>("pageSize") ?? 50;
            string sortOrder = item.Content?.Value<string>("Order") ?? "A-Z";

            int page = 1;
            string? city = null;
            string? search = null;

            if (componentFilterParams != null)
            {
                if (componentFilterParams.TryGetValue("page", out var pageStr) &&
                    int.TryParse(pageStr, out var parsedPage))
                {
                    page = parsedPage;
                }

                if (componentFilterParams.TryGetValue("city", out var cityValue))
                {
                    city = cityValue;
                }

                if (componentFilterParams.TryGetValue("search", out var searchValue))
                {
                    search = searchValue;
                }
            }

            string? representativesId = ExtractRepresentativeId(item, contentId);

            var representationItems = _locationService.GetRepresentationItems(
                representativesId ?? contentId?.ToString() ?? string.Empty,
                page,
                pageSize,
                sortOrder,
                city,
                search
            );

            var contentLink = item.Content.Value<Link>("content");

            return new
            {
                contentType = "representationList",
                componentId = componentId,
                content = new
                {
                    contentTypeAlias = "representationList",
                    contentId = contentId,
                    contentKey = contentKey,
                    title = item.Content.Value<string>("title"),
                    representationListTitle = new
                    {
                        representionName = item.Content.Value<string>("representionName"),
                        representionShortName = item.Content.Value<string>("representionShortName"),
                        representionPhoneNumber = item.Content.Value<string>("representionPhoneNumber"),
                        representionAddress = item.Content.Value<string>("representionAddress"),
                        representionWorkTime = item.Content.Value<string>("representionWorkTime"),
                        repressentionLocation = item.Content.Value<string>("repressentionLocation"),
                    },
                    searchText = item.Content.Value<string>("searchText"),
                    searchIcon = item.Content.Value<IPublishedContent>("searchIcon")?.Url(),
                    seeOnMapTitle = item.Content.Value<string>("seeOnMapTitle"),
                    pageSize = item.Content.Value<int?>("pageSize") ?? 50,
                    content = contentLink != null ? new
                    {
                        url = contentLink.Url,
                        name = contentLink.Name,
                        target = contentLink.Target
                    } : null
                },
                settings = item.Settings != null ? ConvertElementToDictionary(item.Settings, contentId, contentKey) : null,
                representatives = representationItems,
                appliedFilters = componentFilterParams
            };
        }

        // Overload for BlockListItem
        private object MapRepresentationListBlock(BlockListItem item, object? contentId, object? contentKey,
            Dictionary<string, Dictionary<string, string>>? filterParams)
        {
            // Debug log to see if this method is called
            _logger.LogInformation("MapRepresentationListBlock (BlockListItem) called!");
            
            // For BlockListItem, we don't have a grid, so we use a simple index
            string componentId = $"representationList{{{contentId}}}-0";
            
            Dictionary<string, string>? componentFilterParams = null;
            bool hasComponentFilter = filterParams?.ContainsKey(componentId) == true;
            if (hasComponentFilter)
            {
                componentFilterParams = filterParams![componentId];
            }

            string? representativesId = ExtractRepresentativeId(item, contentId);

            var representationItems = _locationService.GetRepresentationItems(
                representativesId ?? contentId?.ToString() ?? string.Empty,
                1,
                500,
                "A-Z",
                null,
                null
            );

            var contentLink = item.Content?.Value<Link>("content");

            return new
            {
                contentType = "representationList",
                componentId = componentId,
                content = new
                {
                    contentTypeAlias = "representationList",
                    contentId = contentId,
                    contentKey = contentKey,
                    title = item.Content?.Value<string>("title"),
                    representationListTitle = new
                    {
                        representionName = item.Content?.Value<string>("representionName"),
                        representionShortName = item.Content?.Value<string>("representionShortName"),
                        representionPhoneNumber = item.Content?.Value<string>("representionPhoneNumber"),
                        representionAddress = item.Content?.Value<string>("representionAddress"),
                        representionWorkTime = item.Content?.Value<string>("representionWorkTime"),
                        repressentionLocation = item.Content?.Value<string>("repressentionLocation"),
                    },
                    searchText = item.Content?.Value<string>("searchText"),
                    searchIcon = item.Content?.Value<IPublishedContent>("searchIcon")?.Url(),
                    seeOnMapTitle = item.Content?.Value<string>("seeOnMapTitle"),
                    pageSize = item.Content?.Value<int?>("pageSize") ?? 50,
                    content = contentLink != null ? new
                    {
                        url = contentLink.Url,
                        name = contentLink.Name,
                        target = contentLink.Target
                    } : null
                },
                settings = item.Settings != null ? ConvertElementToDictionary(item.Settings, contentId, contentKey) : null,
                representatives = representationItems,
                appliedFilters = componentFilterParams
            };
        }

        private object MapRepresentationMapBlock(BlockGridItem item, BlockGridModel blockGrid, object contentId, object? contentKey,
            Dictionary<string, Dictionary<string, string>>? filterParams)
        {
            string blockIndex = GetBlockIndex(item, blockGrid).ToString();
            string componentId = $"representationMap{{{contentId}}}-{blockIndex}";

            string? representativesId = ExtractRepresentativeId(item, contentId);

            var representationItems = _locationService.GetRepresentationItems(
                representativesId ?? contentId.ToString(),
                1,
                500,
                "A-Z",
                null,
                null
            );

            return new
            {
                ContentType = "representationMap",
                ComponentId = componentId,
                Content = new
                {
                    contentTypeAlias = "representationMap",
                    contentId = contentId,
                    contentKey = contentKey,
                    title = item.Content.Value<string>("title"),
                    searchText = item.Content.Value<string>("description"),
                    ButtonOne = item.Content.Value<Link>("buttonOne"),
                    ButtonTwo = item.Content.Value<Link>("buttonTwo"),
                    Orientation = item.Content.Value<string>("orientation"),
                },
                Settings = item.Settings != null ? ConvertElementToDictionary(item.Settings, contentId, contentKey) : null,
                Representatives = representationItems
            };
        }

        private object MapQuestionsListBlock(BlockGridItem item, object contentId, object? contentKey)
        {
            _logger.LogInformation("[DEBUG MapQuestionsListBlock] Starting with contentId: {ContentId}", contentId);

            var contentLinks = item.Content.Value<IEnumerable<Link>>("content");

            if (contentLinks != null && contentLinks.Any())
            {
                var firstLink = contentLinks.FirstOrDefault();
                if (firstLink != null && firstLink.Udi != null)
                {
                    string? sssId = null;

                    var udiString = firstLink.Udi.ToString();
                    var guidMatch = Regex.Match(udiString, @"umb://document/([0-9a-f-]+)", RegexOptions.IgnoreCase);
                    if (guidMatch.Success && guidMatch.Groups.Count > 1)
                    {
                        sssId = guidMatch.Groups[1].Value;
                    }

                    _logger.LogInformation("[DEBUG MapQuestionsListBlock] Extracted sssId: {SssId}", sssId);

                    if (!string.IsNullOrEmpty(sssId))
                    {
                        var componentCategories = item.Content.Value<IEnumerable<IPublishedElement>>("categories")?
                            .Select(c => c.Value<string>("title"))
                            .Where(title => !string.IsNullOrEmpty(title))
                            .ToList();

                        _logger.LogInformation("[DEBUG MapQuestionsListBlock] Component categories: [{Categories}]", 
                            componentCategories != null ? string.Join(", ", componentCategories) : "null");

                        string? categoryParam = null;
                        if (componentCategories != null && componentCategories.Any())
                        {
                            categoryParam = string.Join(",", componentCategories);
                        }

                        _logger.LogInformation("[DEBUG MapQuestionsListBlock] Category param: {CategoryParam}", categoryParam);

                        bool? multiCategoriesContent = item.Content.Value<bool?>("mutliCategoriesContent");
                        int? maxQuestion = item.Content.Value<int?>("maxQuestion") ?? 20;

                        _logger.LogInformation("[DEBUG MapQuestionsListBlock] Calling GetSSSSorular with sssId: {SssId}, categoryParam: {CategoryParam}, maxQuestion: {MaxQuestion}, multiCategories: {MultiCategories}", 
                            sssId, categoryParam, maxQuestion, multiCategoriesContent);

                        var questions = _questionService.GetSSSSorular(sssId, categoryParam, maxQuestion, multiCategoriesContent);

                        _logger.LogInformation("[DEBUG MapQuestionsListBlock] GetSSSSorular returned {QuestionCount} questions", 
                            questions is IEnumerable<object> enumerable ? enumerable.Count() : "unknown count");

                        return new
                        {
                            contentType = "questionsList",
                            content = new
                            {
                                contentTypeAlias = "questionsList",
                                contentId = contentId,
                                contentKey = contentKey,
                                title = item.Content.Value<string>("title"),
                                content = contentLinks.Select(link => new
                                {
                                    url = link.Url,
                                    name = link.Name,
                                    target = link.Target
                                }).ToList(),
                                maxQuestion = maxQuestion,
                                multiCategoriesContent = multiCategoriesContent
                            },
                            settings = item.Settings != null ? ConvertElementToDictionary(item.Settings, contentId, contentKey) : null,
                            questions = questions ?? new object[] { }
                        };
                    }
                }
            }

            _logger.LogInformation("[DEBUG MapQuestionsListBlock] No content links found, returning empty questions");

            return new
            {
                contentType = "questionsList",
                content = ConvertElementToDictionary(item.Content, contentId, contentKey),
                settings = item.Settings != null ? ConvertElementToDictionary(item.Settings, contentId, contentKey) : null,
                questions = new object[] { }
            };
        }

        private object MapQuestionsPageBlock(BlockGridItem item, BlockGridModel? blockGrid, object? contentId, object? contentKey,
            Dictionary<string, Dictionary<string, string>>? filterParams)
        {
            string blockIndex = blockGrid != null ? GetBlockIndex(item, blockGrid).ToString() : "0";
            string componentId = $"questionsContent{{{contentId}}}-{blockIndex}";

            bool hasComponentFilter = filterParams != null && filterParams.ContainsKey(componentId);
            Dictionary<string, string>? componentFilterParams = hasComponentFilter ? filterParams![componentId] : null;

            // Also check for wildcard filter (eski sistem formatı)
            bool hasWildcardFilter = filterParams != null && filterParams.ContainsKey("wildcard:questionsContent");
            Dictionary<string, string>? wildcardFilterParams = hasWildcardFilter ? filterParams!["wildcard:questionsContent"] : null;

            // Also check for generic questionsContent filter (URL parameter format)
            bool hasGenericFilter = filterParams != null && filterParams.ContainsKey("questionsContent");
            Dictionary<string, string>? genericFilterParams = hasGenericFilter ? filterParams!["questionsContent"] : null;

            _logger.LogInformation("[DEBUG MapQuestionsPageBlock] Checking filters - componentId: {ComponentId}, hasComponent: {HasComponent}, hasWildcard: {HasWildcard}, hasGeneric: {HasGeneric}", 
                componentId, hasComponentFilter, hasWildcardFilter, hasGenericFilter);

            // Priority: specific component filter > wildcard filter > generic filter
            Dictionary<string, string>? activeFilterParams = componentFilterParams ?? wildcardFilterParams ?? genericFilterParams;

            if (activeFilterParams != null)
            {
                _logger.LogInformation("[DEBUG MapQuestionsPageBlock] Using filter params: [{FilterParams}]", 
                    string.Join(", ", activeFilterParams.Select(kvp => $"{kvp.Key}={kvp.Value}")));
            }

            int pageSize = item.Content?.Value<int?>("pageSize") ?? 10;
            string sortOrder = item.Content?.Value<string>("questionsOrder") ?? "A-Z";

            int page = 1;
            string? category = null;
            string? search = null;
            string? activeContentId = null;

            // 🔍 Arama parametrelerini kontrol et
            bool hasSearchFilter = false;

            if (activeFilterParams != null)
            {
                if (activeFilterParams.TryGetValue("page", out var pageStr) &&
                    int.TryParse(pageStr, out var parsedPage))
                {
                    page = parsedPage;
                }

                if (activeFilterParams.TryGetValue("categories", out var categoryValue))
                {
                    category = categoryValue;
                    _logger.LogInformation("[DEBUG PropertyMappingService] Category filter found: '{Category}'", category);
                }

                if (activeFilterParams.TryGetValue("search", out var searchValue))
                {
                    search = searchValue;
                    hasSearchFilter = !string.IsNullOrEmpty(searchValue);
                    _logger.LogInformation("[DEBUG PropertyMappingService] Search filter found: '{Search}'", search);
                }

                if (activeFilterParams.TryGetValue("activeContentId", out var activeContentIdValue))
                {
                    activeContentId = activeContentIdValue;
                }
            }

            // 🚀 ÖNEMLI: Arama yapılıyorsa pageSize'ı kaldır - TÜM sonuçlar dönmeli!
            if (hasSearchFilter)
            {
                pageSize = 1000; // Yüksek bir değer ver ki tüm sonuçlar dönebilsin
                _logger.LogInformation("[DEBUG PropertyMappingService] Search detected - setting pageSize to {PageSize} to return ALL results", pageSize);
            }

            var contentIdStr = contentId?.ToString() ?? string.Empty;
            
            _logger.LogInformation("[DEBUG MapQuestionsPageBlock] Calling QuestionService with: id='{ContentId}', page={Page}, pageSize={PageSize}, sortOrder='{SortOrder}', category='{Category}', search='{Search}', activeContentId='{ActiveContentId}', hasSearchFilter={HasSearchFilter}", 
                contentIdStr, page, pageSize, sortOrder, category, search, activeContentId, hasSearchFilter);

            var questions = _questionService.GetQuestionsItems(
                contentIdStr,
                page,
                pageSize,
                sortOrder,
                category ?? string.Empty,
                search ?? string.Empty,
                activeContentId ?? string.Empty
            );

            _logger.LogInformation("[DEBUG MapQuestionsPageBlock] QuestionService returned: {Questions}", questions);

            return new
            {
                contentType = "questionsPage",
                componentId = componentId,
                content = item.Content != null ? ConvertElementToDictionary(item.Content, contentId, contentKey) : null,
                settings = item.Settings != null ? ConvertElementToDictionary(item.Settings, contentId, contentKey) : null,
                questions = questions, // QuestionService already returns the correct format
                appliedFilters = componentFilterParams,
                hasSearchFilter = hasSearchFilter // Debug için eklendi
            };
        }

        private object MapFormBlock(BlockGridItem item, string contentType, object? contentId, object? contentKey)
        {
            var formProperty = item.Content.Properties.FirstOrDefault(p => p.Alias == "form");
            var formContent = formProperty?.GetValue();

            List<object> formContents = new List<object>();

            // Form'un gerçek source ID'sini bul
            object? actualFormContentId = contentId;
            object? actualFormContentKey = contentKey;

            // Önce form property'sinin değerini kontrol et - eğer IPublishedContent ise onun ID'sini kullan
            if (formContent is IPublishedContent directFormContent)
            {
                actualFormContentId = directFormContent.Id;
                actualFormContentKey = directFormContent.Key;
            }
            // Eğer form content picker var ise ondan ID al
            else
            {
                var formPickerProperty = item.Content.Properties.FirstOrDefault(p => 
                    p.PropertyType.EditorAlias == "Umbraco.ContentPicker" || 
                    p.PropertyType.EditorAlias == "Umbraco.MultiNodeTreePicker");

                if (formPickerProperty?.GetValue() is IPublishedContent pickedContent)
                {
                    actualFormContentId = pickedContent.Id;
                    actualFormContentKey = pickedContent.Key;
                }
                else if (formPickerProperty?.GetValue() is IEnumerable<IPublishedContent> pickedContents)
                {
                    var firstPicked = pickedContents.FirstOrDefault();
                    if (firstPicked != null)
                    {
                        actualFormContentId = firstPicked.Id;
                        actualFormContentKey = firstPicked.Key;
                    }
                }
            }

            if (formContent != null)
            {
                if (formContent is IEnumerable<IPublishedContent> publishedContents)
                {
                    foreach (var content in publishedContents)
                    {
                        var sensitiveProps = new HashSet<string> {
                            "host", "fromEmail", "smtpPassword", "smtpPort", "ssl",
                            "smtpUsername", "mailTemplate", "jiraEmail", "bccEmail",
                            "notificationEmail", "mailTitle", "sendMailUser"
                        };

                        var properties = content.Properties
                            .Where(p => !sensitiveProps.Contains(p.Alias))
                            .ToDictionary(
                                p => p.Alias,
                                p => MapValue(p, content.Id, content.Key)); // Form'un gerçek ID'sini kullan

                        formContents.Add(new
                        {
                            id = content.Id,
                            key = content.Key,
                            name = content.Name,
                            url = content.Url(),
                            properties = properties
                        });
                    }
                }
                else
                {
                    // Form element'leri için gerçek form ID'sini kullan
                    var form = ConvertElementToDictionary(item.Content, actualFormContentId, actualFormContentKey);
                    var sensitiveProps = new HashSet<string> {
                        "host", "fromEmail", "smtpPassword", "smtpPort", "ssl",
                        "smtpUsername", "mailTemplate", "jiraEmail", "bccEmail",
                        "notificationEmail", "mailTitle", "sendMailUser"
                    };

                    foreach (var prop in sensitiveProps)
                    {
                        form.Remove(prop);
                    }

                    formContents.Add(form);
                }
            }

            return new
            {
                Form = formContents,
                ContentType = contentType,
                Settings = item.Settings != null ? ConvertElementToDictionary(item.Settings, actualFormContentId, actualFormContentKey) : null
            };
        }

        private object MapTransactionFeeCountryListBlock(BlockGridItem item, object? contentId, object? contentKey)
        {
            if (item.Content == null)
            {
                return new
                {
                    ContentType = "transactionFeeCountryList",
                    Content = (object?)null,
                    Settings = item.Settings != null ? ConvertElementToDictionary(item.Settings, contentId, contentKey) : null
                };
            }

            // unselectedList'teki ID'leri al
            var unselectedList = item.Content.Value<IEnumerable<IPublishedContent>>("unselectedList");
            var unselectedIds = new HashSet<int>();
            
            if (unselectedList != null)
            {
                unselectedIds = unselectedList.Select(c => c.Id).ToHashSet();
                _logger.LogInformation("[DEBUG MapTransactionFeeCountryListBlock] unselectedList IDs: [{UnselectedIds}]", 
                    string.Join(", ", unselectedIds));
            }

            // Content'i dictionary'ye çevir
            var contentDict = new Dictionary<string, object>
            {
                { "contentTypeAlias", item.Content.ContentType?.Alias ?? "" },
                { "contentId", contentId ?? 0 },
                { "contentKey", contentKey ?? Guid.Empty }
            };

            // Her property'yi işle
            foreach (var property in item.Content.Properties)
            {
                if (property.Alias == "countryList")
                {
                    // countryList'i özel işle
                    var countryListValue = property.GetValue();
                    
                    if (countryListValue is IEnumerable<IPublishedContent> countryList)
                    {
                        var filteredCountryList = countryList.Select(moneyTransferList =>
                        {
                            if (moneyTransferList.ContentType.Alias == "moneyTransferList")
                            {
                                // Children'ları al ve unselectedList'te olmayanları filtrele
                                var allChildren = moneyTransferList.Children().ToList();
                                var filteredChildren = allChildren
                                    .Where(child => !unselectedIds.Contains(child.Id))
                                    .Select(child => ConvertPublishedContentToDictionary(child))
                                    .ToList();

                                _logger.LogInformation("[DEBUG MapTransactionFeeCountryListBlock] moneyTransferList ID: {Id}, Total children: {Total}, Filtered: {Filtered}",
                                    moneyTransferList.Id, allChildren.Count, filteredChildren.Count);

                                return (object)new
                                {
                                    contentTypeAlias = moneyTransferList.ContentType.Alias,
                                    contentId = moneyTransferList.Id,
                                    contentKey = moneyTransferList.Key,
                                    children = filteredChildren
                                };
                            }

                            return (object)new
                            {
                                Id = moneyTransferList.Key,
                                Name = moneyTransferList.Name,
                                Url = moneyTransferList.Url()
                            };
                        }).ToList();

                        contentDict[property.Alias] = filteredCountryList;
                    }
                    else
                    {
                        // Normal MapValue işlemi
                        var mappedValue = MapValue(property, contentId, contentKey);
                        if (mappedValue != null)
                        {
                            contentDict[property.Alias] = mappedValue;
                        }
                    }
                }
                else
                {
                    // Diğer property'ler için normal işlem
                    var mappedValue = MapValue(property, contentId, contentKey);
                    if (mappedValue != null)
                    {
                        contentDict[property.Alias] = mappedValue;
                    }
                }
            }

            return new
            {
                ContentType = "transactionFeeCountryList",
                Content = contentDict,
                Settings = item.Settings != null ? ConvertElementToDictionary(item.Settings, contentId, contentKey) : null
            };
        }

        private string? ExtractRepresentativeId(BlockGridItem item, object? contentId)
        {
            var contentLink = item.Content.Value<Link>("content");
            if (contentLink != null && contentLink.Udi != null)
            {
                var udiString = contentLink.Udi.ToString();
                var guidMatch = Regex.Match(udiString, @"umb://document/([0-9a-f-]+)", RegexOptions.IgnoreCase);
                if (guidMatch.Success && guidMatch.Groups.Count > 1)
                {
                    return guidMatch.Groups[1].Value;
                }
            }
            else
            {
                var contentLinks = item.Content.Value<IEnumerable<Link>>("content");
                if (contentLinks != null && contentLinks.Any())
                {
                    var firstLink = contentLinks.FirstOrDefault();
                    if (firstLink != null && firstLink.Udi != null)
                    {
                        var udiString = firstLink.Udi.ToString();
                        var guidMatch = Regex.Match(udiString, @"umb://document/([0-9a-f-]+)", RegexOptions.IgnoreCase);
                        if (guidMatch.Success && guidMatch.Groups.Count > 1)
                        {
                            return guidMatch.Groups[1].Value;
                        }
                    }
                }
            }

            if (contentLink != null && Guid.TryParse(contentLink.Name, out var linkGuid))
            {
                return linkGuid.ToString();
            }

            return null;
        }

        // Overload for BlockListItem
        private string? ExtractRepresentativeId(BlockListItem item, object? contentId)
        {
            var contentLink = item.Content?.Value<Link>("content");
            if (contentLink != null && contentLink.Udi != null)
            {
                var udiString = contentLink.Udi.ToString();
                var guidMatch = Regex.Match(udiString, @"umb://document/([0-9a-f-]+)", RegexOptions.IgnoreCase);
                if (guidMatch.Success && guidMatch.Groups.Count > 1)
                {
                    return guidMatch.Groups[1].Value;
                }
            }
            else
            {
                var contentLinks = item.Content?.Value<IEnumerable<Link>>("content");
                if (contentLinks != null && contentLinks.Any())
                {
                    var firstLink = contentLinks.FirstOrDefault();
                    if (firstLink != null && firstLink.Udi != null)
                    {
                        var udiString = firstLink.Udi.ToString();
                        var guidMatch = Regex.Match(udiString, @"umb://document/([0-9a-f-]+)", RegexOptions.IgnoreCase);
                        if (guidMatch.Success && guidMatch.Groups.Count > 1)
                        {
                            return guidMatch.Groups[1].Value;
                        }
                    }
                }
            }

            if (contentLink != null && Guid.TryParse(contentLink.Name, out var linkGuid))
            {
                return linkGuid.ToString();
            }

            return null;
        }

        public Dictionary<string, object> ConvertElementToDictionary(IPublishedElement element, object? contentId = null, object? contentKey = null)
        {
            var result = new Dictionary<string, object>();

            if (element.ContentType != null)
            {
                result["contentTypeAlias"] = element.ContentType.Alias;
            }

            // ✅ moneyTransferList için children kontrolü - element aslında IPublishedContent ise
            if (element is IPublishedContent publishedContentElement && 
                element.ContentType?.Alias == "moneyTransferList")
            {
                var childrenData = publishedContentElement.Children().Select(child => ConvertPublishedContentToDictionary(child)).ToList();
                
                result["contentId"] = publishedContentElement.Id;
                result["contentKey"] = publishedContentElement.Key;
                result["children"] = childrenData;
                
                _logger.LogInformation("[DEBUG ConvertElementToDictionary] moneyTransferList found with {ChildCount} children", childrenData.Count);
                
                return result;
            }

            // ⚡ Campaign için parent'tan validDates ve conditions al
            bool isCampaign = element.ContentType?.Alias == "campaign";
            string? validDates = null;
            string? conditions = null;

            if (isCampaign && contentId != null)
            {
                // contentId campaign sayfasının ID'si
                // Bizim parent'a ihtiyacımız var (kampanyalar klasörü)
                if (_ctxAccessor.TryGetUmbracoContext(out var ctx))
                {
                    IPublishedContent? campaignPage = null;
                    
                    // Campaign sayfasını bul
                    if (contentId is int id)
                    {
                        campaignPage = ctx.Content?.GetById(id);
                    }
                    else if (contentId is string idStr && int.TryParse(idStr, out var intId))
                    {
                        campaignPage = ctx.Content?.GetById(intId);
                    }
                    
                    if (campaignPage != null)
                    {
                        // Campaign sayfasının PARENT'ını al (kampanyalar klasörü)
                        var parentFolder = campaignPage.Parent;
                        
                        if (parentFolder != null)
                        {
                            _logger.LogInformation("[DEBUG Campaign] Campaign page: {CampaignPageName} (ID: {CampaignId}), Parent: {ParentName} (ID: {ParentId})", 
                                campaignPage.Name, campaignPage.Id, parentFolder.Name, parentFolder.Id);
                            
                            // Parent'ın home BlockGrid'ini al
                            var parentHomeBlockGrid = parentFolder.Value<BlockGridModel>("home");
                            if (parentHomeBlockGrid != null)
                            {
                                // campingList block'unu bul
                                var campaignsListBlock = parentHomeBlockGrid.FirstOrDefault(b => 
                                    b.Content?.ContentType?.Alias == "campingList");
                                
                                if (campaignsListBlock != null)
                                {
                                    // validDates ve conditions değerlerini al
                                    validDates = campaignsListBlock.Content?.Value<string>("validDates");
                                    conditions = campaignsListBlock.Content?.Value<string>("conditions");
                                    
                                    _logger.LogInformation("[DEBUG Campaign] ✅ Found campingList in parent - validDates: '{ValidDates}', conditions: '{Conditions}'", 
                                        validDates ?? "EMPTY", conditions ?? "EMPTY");
                                }
                                else
                                {
                                    _logger.LogWarning("[DEBUG Campaign] ❌ campingList block NOT FOUND in parent {ParentId}", parentFolder.Id);
                                }
                            }
                            else
                            {
                                _logger.LogWarning("[DEBUG Campaign] ❌ Parent has NO home BlockGrid");
                            }
                        }
                        else
                        {
                            _logger.LogWarning("[DEBUG Campaign] ❌ Campaign page has NO parent!");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("[DEBUG Campaign] ❌ Campaign page not found for contentId: {ContentId}", contentId);
                    }
                }
            }

            // Her element için doğru contentId ve contentKey'i belirle
            object? finalContentId = null;
            object? finalContentKey = null;

            // 1. Önce element'in kendi ID'si var mı kontrol et (eğer IPublishedContent ise)
            if (element is IPublishedContent publishedContent)
            {
                finalContentId = publishedContent.Id;
                finalContentKey = publishedContent.Key;
            }
            // 2. Element'in properties'inden content ID'yi ara
            else
            {
                var contentIdProperty = element.Properties.FirstOrDefault(p => 
                    p.Alias.Equals("contentId", StringComparison.OrdinalIgnoreCase) ||
                    p.Alias.Equals("id", StringComparison.OrdinalIgnoreCase));
                
                if (contentIdProperty?.GetValue() != null)
                {
                    finalContentId = contentIdProperty.GetValue();
                }

                var contentKeyProperty = element.Properties.FirstOrDefault(p => 
                    p.Alias.Equals("contentKey", StringComparison.OrdinalIgnoreCase) ||
                    p.Alias.Equals("key", StringComparison.OrdinalIgnoreCase));
                
                if (contentKeyProperty?.GetValue() != null)
                {
                    finalContentKey = contentKeyProperty.GetValue();
                }

                // 3. Eğer element kendi ID'sine sahip değilse, parent ID'yi kullan
                if (finalContentId == null)
                {
                    finalContentId = contentId;
                }
                if (finalContentKey == null)
                {
                    finalContentKey = contentKey;
                }
            }

            // Check if this is a form field element that needs a unique identifier
            bool isFormField = element.ContentType?.Alias != null && 
                              (element.ContentType.Alias == "string" || 
                               element.ContentType.Alias == "button" ||
                               element.ContentType.Alias == "aprovedCheckbox" ||
                               element.ContentType.Alias == "textarea" ||
                               element.ContentType.Alias == "email" ||
                               element.ContentType.Alias == "phone" ||
                               element.ContentType.Alias == "number" ||
                               element.ContentType.Alias == "dropdown" ||
                               element.ContentType.Alias == "checkbox" ||
                               element.ContentType.Alias == "radio" ||
                               element.ContentType.Alias == "fileUpload");

            // Form field'ları için özel benzersiz ID oluştur
            if (isFormField && finalContentId != null)
            {
                var titleProperty = element.Properties.FirstOrDefault(p => p.Alias == "title");
                var titleValue = titleProperty?.GetValue()?.ToString() ?? "";
                var formKey = !string.IsNullOrEmpty(titleValue) ? GenerateFormKey(titleValue) : (element.ContentType?.Alias ?? "field");
                
                // Form field için benzersiz ID oluştur: form_[baseId]_[formkey]_[type]
                var hash = Math.Abs($"{finalContentId}_{formKey}_{element.ContentType?.Alias}".GetHashCode());
                finalContentId = $"form_{hash}_{formKey}_{element.ContentType?.Alias ?? "unknown"}";
                finalContentKey = Guid.NewGuid();
            }

            if (finalContentId != null)
            {
                result["contentId"] = finalContentId;
            }

            if (finalContentKey != null)
            {
                result["contentKey"] = finalContentKey;
            }

            // Define sensitive properties that should be filtered out for form-related content types
            var sensitiveProps = new HashSet<string> {
                "host", "fromEmail", "smtpPassword", "smtpPort", "ssl",
                "smtpUsername", "mailTemplate", "jiraEmail", "bccEmail",
                "notificationEmail", "mailTitle", "sendMailUser"
            };

            // Check if this is a form-related content type
            bool isFormRelated = element.ContentType?.Alias != null && 
                                (element.ContentType.Alias.Contains("form", StringComparison.OrdinalIgnoreCase) ||
                                 element.ContentType.Alias.Contains("Form", StringComparison.Ordinal) ||
                                 isFormField);

            foreach (var property in element.Properties)
            {
                // Filter out sensitive properties for form-related content types
                if (isFormRelated && sensitiveProps.Contains(property.Alias))
                {
                    continue; // Skip sensitive properties
                }
                
                var mappedValue = MapValue(property, finalContentId, finalContentKey);
                if (mappedValue != null)
                {
                    result[property.Alias] = mappedValue;
                }

                // ⚡ Campaign için shortDescription'dan hemen sonra validDates ve conditions ekle
                if (isCampaign && property.Alias == "shortDescription")
                {
                    // validDates ve conditions değerlerini ekle (eğer dolu ise)
                    if (!string.IsNullOrEmpty(validDates))
                    {
                        result["validDates"] = validDates;
                        _logger.LogInformation("[DEBUG Campaign] ✅ Added validDates after shortDescription: {ValidDates}", validDates);
                    }
                    
                    if (!string.IsNullOrEmpty(conditions))
                    {
                        result["conditions"] = conditions;
                        _logger.LogInformation("[DEBUG Campaign] ✅ Added conditions after shortDescription: {Conditions}", conditions);
                    }
                    
                    // ⚡ Related campaigns ekle
                    if (finalContentId != null)
                    {
                        var relatedCampaignsData = _campaignService.GetRelatedCampaigns(finalContentId.ToString()!, maxItems: 3);
                        if (relatedCampaignsData != null)
                        {
                            result["relatedCampaigns"] = relatedCampaignsData;
                            _logger.LogInformation("[DEBUG Campaign] ✅ Added relatedCampaigns for contentId: {ContentId}", finalContentId);
                        }
                    }
                }
            }

            // Add FormKey for form elements that have a title
            if (element.ContentType?.Alias != null && 
                (element.ContentType.Alias == "string" || 
                 element.ContentType.Alias == "button" ||
                 element.ContentType.Alias == "aprovedCheckbox" ||
                 element.ContentType.Alias.Contains("form", StringComparison.OrdinalIgnoreCase)))
            {
                var titleProperty = element.Properties.FirstOrDefault(p => p.Alias == "title");
                if (titleProperty != null)
                {
                    var titleValue = titleProperty.GetValue()?.ToString();
                    if (!string.IsNullOrEmpty(titleValue))
                    {
                        result["FormKey"] = GenerateFormKey(titleValue);
                    }
                }
            }

            return result;
        }

        private Dictionary<string, object> FilterFormContentProperties(IPublishedElement element, object? contentId = null, object? contentKey = null)
        {
            var result = new Dictionary<string, object>();

            if (element.ContentType != null)
            {
                result["contentTypeAlias"] = element.ContentType.Alias;
            }

            if (contentId != null)
            {
                result["contentId"] = contentId;
            }

            if (contentKey != null)
            {
                result["contentKey"] = contentKey;
            }

            // Define sensitive properties that should be filtered out
            var sensitiveProps = new HashSet<string> {
                "host", "fromEmail", "smtpPassword", "smtpPort", "ssl",
                "smtpUsername", "mailTemplate", "jiraEmail", "bccEmail",
                "notificationEmail", "mailTitle", "sendMailUser"
            };

            foreach (var property in element.Properties)
            {
                // Always filter out sensitive properties for form content
                if (sensitiveProps.Contains(property.Alias))
                {
                    continue; // Skip sensitive properties
                }
                
                var mappedValue = MapValue(property, contentId, contentKey);
                if (mappedValue != null)
                {
                    result[property.Alias] = mappedValue;
                }
            }

            // Add FormKey for form elements that have a title
            if (element.ContentType?.Alias != null)
            {
                var titleProperty = element.Properties.FirstOrDefault(p => p.Alias == "title");
                if (titleProperty != null)
                {
                    var titleValue = titleProperty.GetValue()?.ToString();
                    if (!string.IsNullOrEmpty(titleValue))
                    {
                        result["FormKey"] = GenerateFormKey(titleValue);
                    }
                }
            }

            return result;
        }

        public object Shape(IPublishedContent item) => new
        {
            Key = item.Key,
            Name = item.Name,
            Url = item.Url(),
            ContentType = item.ContentType.Alias,
            Properties = item.Properties
                .ToDictionary(
                    keySelector: prop => prop.Alias,
                    elementSelector: prop => MapValue(prop)),
            Cultures = item.Cultures.ToDictionary(c => c.Key, c => item.Url(c.Key))
        };

        public Dictionary<string, Dictionary<string, string>> ParseFilterParameters(string filter)
        {
            _logger.LogInformation("[DEBUG ParseFilterParameters] Input filter: '{Filter}'", filter);

            var result = new Dictionary<string, Dictionary<string, string>>();

            if (string.IsNullOrWhiteSpace(filter))
            {
                _logger.LogInformation("[DEBUG ParseFilterParameters] Empty filter, returning empty result");
                return result;
            }

            var segments = filter.Split(':', StringSplitOptions.RemoveEmptyEntries);
            _logger.LogInformation("[DEBUG ParseFilterParameters] Split into {SegmentCount} segments: [{Segments}]", 
                segments.Length, string.Join(", ", segments));

            if (segments.Length < 3) 
            {
                _logger.LogWarning("[DEBUG ParseFilterParameters] Insufficient segments (need at least 3), returning empty result");
                return result;
            }

            var componentId = segments[0];
            
            if (!result.ContainsKey(componentId))
            {
                result[componentId] = new Dictionary<string, string>();
            }

            // Process parameter pairs starting from index 1
            // Format: componentId:param1:value1:param2:value2:...
            for (int i = 1; i < segments.Length - 1; i += 2)
            {
                if (i + 1 < segments.Length)
                {
                    var paramType = segments[i];
                    var paramValue = segments[i + 1];
                    
                    result[componentId][paramType] = paramValue;
                    
                    _logger.LogInformation("[DEBUG ParseFilterParameters] Added parameter - {ParamType}: '{ParamValue}'", 
                        paramType, paramValue);
                }
            }

            _logger.LogInformation("[DEBUG ParseFilterParameters] Final result: {Result}", 
                string.Join("; ", result.Select(kvp => $"{kvp.Key}=[{string.Join(",", kvp.Value.Select(v => $"{v.Key}={v.Value}"))}]")));

            return result;
        }

        public int GetBlockIndex(object currentBlock, object blockGrid)
        {
            if (currentBlock is BlockGridItem blockItem && blockGrid is BlockGridModel gridModel)
            {
                return gridModel.ToList().IndexOf(blockItem);
            }
            return 0;
        }

        public string StripHtmlTags(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return Regex.Replace(input, "<.*?>", string.Empty);
        }

        private Dictionary<string, object> ConvertPublishedContentToDictionary(IPublishedContent content)
        {
            var result = new Dictionary<string, object>
            {
                { "contentTypeAlias", content.ContentType.Alias },
                { "contentId", content.Id },
                { "contentKey", content.Key }
            };

            // Tüm property'leri ekle
            foreach (var property in content.Properties)
            {
                var value = property.GetValue();
                
                if (value != null)
                {
                    // Link için özel mapping
                    if (value is Link link)
                    {
                        result[property.Alias] = new
                        {
                            name = link.Name,
                            target = link.Target,
                            type = link.Type.ToString(),
                            udi = link.Udi?.ToString(),
                            url = link.Url
                        };
                    }
                    // MediaWithCrops için URL döndür
                    else if (value is MediaWithCrops mediaWithCrops)
                    {
                        result[property.Alias] = mediaWithCrops.MediaUrl();
                    }
                    // IPublishedContent için basit format
                    else if (value is IPublishedContent publishedContent)
                    {
                        result[property.Alias] = new
                        {
                            Id = publishedContent.Key,
                            Name = publishedContent.Name,
                            Url = publishedContent.Url()
                        };
                    }
                    // Primitive types ve string
                    else if (value.GetType().IsPrimitive || value is string)
                    {
                        result[property.Alias] = value;
                    }
                    else
                    {
                        result[property.Alias] = value.ToString() ?? string.Empty;
                    }
                }
            }

            return result;
        }
    }
}
