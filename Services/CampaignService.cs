using Morpara.Services.Interfaces;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Extensions;
using Microsoft.Extensions.Logging;

namespace Morpara.Services
{
    public class CampaignService : ICampaignService
    {
        private readonly IUmbracoContextAccessor _ctxAccessor;
        private readonly IVariationContextAccessor _variation;
        private readonly ILogger<CampaignService> _logger;

        public CampaignService(
            IUmbracoContextAccessor ctxAccessor,
            IVariationContextAccessor variation,
            ILogger<CampaignService> logger)
        {
            _ctxAccessor = ctxAccessor;
            _variation = variation;
            _logger = logger;
        }

        public object Gecmiskmp(string id, int page = 1, int pageSize = 10, string orderBy = "A-Z", 
            string category = null!, string campaignsType = null!)
        {
            _logger.LogInformation("[DEBUG] Gecmiskmp called with: id='{Id}', page={Page}, pageSize={PageSize}, orderBy='{OrderBy}', category='{Category}', campaignsType='{CampaignsType}'", 
                id, page, pageSize, orderBy, category, campaignsType);

            if (!_ctxAccessor.TryGetUmbracoContext(out var ctx))
            {
                _logger.LogWarning("[DEBUG] Umbraco context not available");
                return new List<object>();
            }

            // Get the current culture
            var culture = _variation.VariationContext?.Culture ?? "tr-TR";

            // First, find the parent content item by ID
            IPublishedContent? parent = Guid.TryParse(id, out var g)
                ? ctx.Content?.GetById(false, g)
                : int.TryParse(id, out var i)
                    ? ctx.Content?.GetById(i)
                    : null;

            if (parent == null)
            {
                _logger.LogWarning("[DEBUG] Parent content not found for id: {Id}", id);
                return new List<object>();
            }

            _logger.LogInformation("[DEBUG] Parent found: {ParentName} (ID: {ParentId})", parent.Name, parent.Id);

            // Get the campingList block from the parent that matches the campaign type
            var homeBlockGrid = parent.Value<BlockGridModel>("home");
            
            // Find the specific campingList block that matches the requested campaign type
            var campingListBlock = homeBlockGrid?.Where(b => b.Content?.ContentType?.Alias == "campingList")
                .FirstOrDefault(b => 
                {
                    var blockCampaignType = b.Content?.Value<string>("campaignsType") ?? "Past Campaigns";
                    var campaignTypeToMatch = !string.IsNullOrEmpty(campaignsType) ? campaignsType : "Past Campaigns";
                    return string.Equals(blockCampaignType, campaignTypeToMatch, StringComparison.OrdinalIgnoreCase);
                }) ?? homeBlockGrid?.FirstOrDefault(b => b.Content?.ContentType?.Alias == "campingList");

            _logger.LogInformation("[DEBUG] Looking for campingList block with campaignType: {RequestedType}", campaignsType ?? "Past Campaigns");
            _logger.LogInformation("[DEBUG] Found campingList block with title: {BlockTitle}", campingListBlock?.Content?.Value<string>("title") ?? "No title");

            // Extract parameters from the campingList block but prioritize the passed parameters
            var categoriesActive = campingListBlock?.Content?.Value<bool>("categoriesActive") ?? false;
            var loadMore = campingListBlock?.Content?.Value<string>("loadMore") ?? "Daha Fazla yükle";
            var notFoundCamping = campingListBlock?.Content?.Value<string>("notFoundCamping") ?? "Kampanya Bulunamadı";
            var allCategoriesTitle = campingListBlock?.Content?.Value<string>("allCategoriesTitle") ?? "Tüm Kategoriler";

            // Always use the passed pageSize parameter, don't override with block settings
            var campingPageSize = pageSize;

            // If a campaign type was provided via parameter, use it; otherwise use the one from the campingList block
            var campaignTypeToUse = !string.IsNullOrEmpty(campaignsType) ? campaignsType :
                campingListBlock?.Content?.Value<string>("campaignsType") ?? "Past Campaigns";

            var campingsOrder = orderBy;
            var detailButtonText = campingListBlock?.Content?.Value<string>("detailButtonText") ?? "Kampanyayı İncele";

            // Extract categories - simplified to just include title
            var categories = campingListBlock?.Content?.Value<IEnumerable<IPublishedElement>>("categories")?
                .Select(c => new
                {
                    title = c.Value<string>("title")
                }).ToList();

            // Get children of the parent that have campaign blocks
            var allChildren = parent.Children().ToList();
            _logger.LogInformation("[DEBUG] Parent has {ChildCount} children", allChildren.Count);

            var contentItems = parent.Children()
                .Where(child =>
                {
                    // Look for campaign blocks inside the home property
                    var childHomeBlockGrid = child.Value<BlockGridModel>("home");
                    var hasCampaignBlocks = childHomeBlockGrid != null &&
                           childHomeBlockGrid.Any(block => block.Content?.ContentType?.Alias == "campaign");
                    
                    if (hasCampaignBlocks)
                    {
                        _logger.LogInformation("[DEBUG] Child '{ChildName}' has campaign blocks", child.Name);
                    }
                    
                    return hasCampaignBlocks;
                })
                .Where(child =>
                {
                    // Get the campaign block
                    var childHomeBlockGrid = child.Value<BlockGridModel>("home");
                    var campaignBlock = childHomeBlockGrid?.FirstOrDefault(b => b.Content?.ContentType?.Alias == "campaign");

                    if (campaignBlock == null)
                    {
                        _logger.LogInformation("[DEBUG] Child '{ChildName}' has no campaign block", child.Name);
                        return false;
                    }

                    // Check active status
                    var active = campaignBlock.Content.Value<bool?>("active");
                    if (!active.GetValueOrDefault(false))
                    {
                        _logger.LogInformation("[DEBUG] Child '{ChildName}' campaign is not active", child.Name);
                        return false;
                    }

                    _logger.LogInformation("[DEBUG] Child '{ChildName}' campaign is active", child.Name);

                    // Filter by category if specified
                    if (!string.IsNullOrEmpty(category))
                    {
                        var campaignCategories = campaignBlock.Content.Value<IEnumerable<IPublishedContent>>("categories");
                        if (campaignCategories == null || !campaignCategories.Any(c => c.Name.Equals(category, StringComparison.OrdinalIgnoreCase)))
                            return false;
                    }

                    // Get current date for comparisons
                    var now = DateTime.Now.Date;

                    // Get expiration date
                    var expireDate = campaignBlock.Content.Value<DateTime?>("expireDate")?.Date;

                    // Check campaign type (Past, Current, or Future based on date)
                    switch (campaignTypeToUse)
                    {
                        case "Past Campaigns":
                            // Campaign has expired (end date is in the past)
                            return expireDate.HasValue && expireDate.Value < now;

                        case "Current Campaigns":
                            // Campaign is active now
                            return expireDate.HasValue && expireDate.Value >= now;

                        case "Future Campaigns":
                            // Campaign starts in the future
                            var startingDate = campaignBlock.Content.Value<DateTime?>("startingDate")?.Date;
                            return startingDate.HasValue && startingDate.Value > now;

                        default:
                            // Default behavior: return all active campaigns
                            return true;
                    }
                });

            // Apply sorting based on campingsOrder
            contentItems = campingsOrder switch
            {
                "None" => contentItems,
                "A-Z" => contentItems.OrderBy(child => child.Name),
                "CAMPİNG NUMBER ASC" => contentItems.OrderBy(child =>
                {
                    var childHomeBlockGrid = child.Value<BlockGridModel>("home");
                    var campaignBlock = childHomeBlockGrid?.FirstOrDefault(b => b.Content?.ContentType?.Alias == "campaign");
                    return campaignBlock?.Content?.Value<int?>("campingNumber") ?? 0;
                }),
                "CAMPİNG NUMBER DSC" => contentItems.OrderByDescending(child =>
                {
                    var childHomeBlockGrid = child.Value<BlockGridModel>("home");
                    var campaignBlock = childHomeBlockGrid?.FirstOrDefault(b => b.Content?.ContentType?.Alias == "campaign");
                    return campaignBlock?.Content?.Value<int?>("campingNumber") ?? 0;
                }),
                "DATE" => contentItems.OrderByDescending(child =>
                {
                    var childHomeBlockGrid = child.Value<BlockGridModel>("home");
                    var campaignBlock = childHomeBlockGrid?.FirstOrDefault(b => b.Content?.ContentType?.Alias == "campaign");
                    return campaignBlock?.Content?.Value<DateTime?>("startingDate") ?? DateTime.MinValue;
                }),
                _ => contentItems.OrderBy(child => child.Name) // Default to A-Z
            };

            var totalItems = contentItems.Count();
            var totalPages = (int)Math.Ceiling((double)totalItems / campingPageSize);

            contentItems = contentItems.Skip((page - 1) * campingPageSize).Take(campingPageSize);

            var result = new
            {
                contentType = "campingList",
                content = new
                {
                    contentTypeAlias = "campingList",
                    title = campingListBlock?.Content?.Value<string>("title") ?? "Kampanyalar",
                    titleSize = campingListBlock?.Content?.Value<string>("titleSize") ?? "h1",
                    categoriesActive = categoriesActive,
                    categories = categories,
                    allCategoriesTitle = allCategoriesTitle,
                    campingPageSize = campingPageSize, // Use the passed pageSize
                    campaignsType = campaignTypeToUse,
                    campingsOrder = campingsOrder,
                    detailButtonText = detailButtonText,
                    loadMore = loadMore,
                    notFoundCamping = notFoundCamping
                },
                pagination = new
                {
                    currentPage = page,
                    pageSize = campingPageSize,
                    totalItems = totalItems,
                    totalPages = totalPages
                },
                campaigns = contentItems.Select(item =>
                {
                    var childHomeBlockGrid = item.Value<BlockGridModel>("home");
                    var campaignBlock = childHomeBlockGrid?.FirstOrDefault(b => b.Content?.ContentType?.Alias == "campaign");

                    if (campaignBlock == null)
                        return null;

                    var title = campaignBlock.Content.Value<string>("title") ?? item.Name;
                    var shortDescription = campaignBlock.Content.Value<string>("shortDescription");

                    var imageMedia = campaignBlock.Content.Value<IPublishedContent>("image");
                    var mobileImageMedia = campaignBlock.Content.Value<IPublishedContent>("mobilImage");

                    var campaignCategories = campaignBlock.Content.Value<IEnumerable<IPublishedContent>>("categories")?
                        .Select(c => new
                        {
                            title = c.Name
                        }).ToList();

                    var startingDate = campaignBlock.Content.Value<DateTime?>("startingDate");
                    var expireDate = campaignBlock.Content.Value<DateTime?>("expireDate");

                    return new
                    {
                        title,
                        shortDescription,
                        image = imageMedia?.Url(),
                        mobileImage = mobileImageMedia?.Url(),
                        startingDate = startingDate.HasValue ? startingDate.Value.ToString("d.MM.yyyy HH:mm:ss") : null,
                        expireDate = expireDate.HasValue ? expireDate.Value.ToString("d.MM.yyyy HH:mm:ss") : null,
                        categories = campaignCategories,
                        url = item.Url(culture)
                    };
                })
                .Where(result => result != null)
                .ToList()
            };

            return result;
        }

        public object GetRelatedCampaigns(string currentCampaignId, int maxItems = 3)
        {
            if (!_ctxAccessor.TryGetUmbracoContext(out var ctx))
                return new { contentType = "relatedCampaigns", campaigns = new List<object>() };

            // Get the current culture
            var culture = _variation.VariationContext?.Culture ?? "tr-TR";

            // Get the current campaign content
            IPublishedContent? currentCampaign = null;
            
            if (Guid.TryParse(currentCampaignId, out var g))
            {
                currentCampaign = ctx.Content?.GetById(false, g);
            }
            else if (int.TryParse(currentCampaignId, out var i))
            {
                currentCampaign = ctx.Content?.GetById(i);
            }

            if (currentCampaign == null)
                return new { contentType = "relatedCampaigns", campaigns = new List<object>() };

            // Get current campaign's categories
            var currentCampaignBlockGrid = currentCampaign.Value<BlockGridModel>("home");
            var currentCampaignBlock = currentCampaignBlockGrid?.FirstOrDefault(b => b.Content?.ContentType?.Alias == "campaign");
            
            if (currentCampaignBlock == null)
                return new { contentType = "relatedCampaigns", campaigns = new List<object>() };

            var currentCampaignCategories = currentCampaignBlock.Content.Value<IEnumerable<IPublishedContent>>("categories")?
                .Select(c => c.Name)
                .ToList() ?? new List<string>();

            // Find the campaigns parent (should be the campaigns folder)
            var campaignsParent = currentCampaign.Parent;
            if (campaignsParent == null)
                return new { contentType = "relatedCampaigns", campaigns = new List<object>() };

            var now = DateTime.Now.Date;

            // Get all campaign siblings, excluding the current campaign
            var relatedCampaigns = campaignsParent.Children()
                .Where(child => child.Id != currentCampaign.Id) // Exclude current campaign
                .Where(child =>
                {
                    // Look for campaign blocks inside the home property
                    var childHomeBlockGrid = child.Value<BlockGridModel>("home");
                    return childHomeBlockGrid != null &&
                           childHomeBlockGrid.Any(block => block.Content?.ContentType?.Alias == "campaign");
                })
                .Select(child => new
                {
                    Content = child,
                    CampaignBlock = child.Value<BlockGridModel>("home")?.FirstOrDefault(b => b.Content?.ContentType?.Alias == "campaign")
                })
                .Where(item => item.CampaignBlock != null)
                .Where(item =>
                {
                    // Check active status first
                    var active = item.CampaignBlock.Content.Value<bool?>("active");
                    if (!active.GetValueOrDefault(false))
                        return false;

                    // UPDATED LOGIC: Show both current AND future campaigns
                    var expireDate = item.CampaignBlock.Content.Value<DateTime?>("expireDate")?.Date;
                    
                    // Include campaign if it's not yet expired (current OR future campaigns)
                    return expireDate.HasValue && expireDate.Value >= now;
                })
                .Select(item => new
                {
                    item.Content,
                    item.CampaignBlock,
                    Categories = item.CampaignBlock.Content.Value<IEnumerable<IPublishedContent>>("categories")?
                        .Select(c => c.Name)
                        .ToList() ?? new List<string>(),
                    Title = item.CampaignBlock.Content.Value<string>("title") ?? item.Content.Name,
                    ShortDescription = item.CampaignBlock.Content.Value<string>("shortDescription"),
                    Image = item.CampaignBlock.Content.Value<IPublishedContent>("image"),
                    MobileImage = item.CampaignBlock.Content.Value<IPublishedContent>("mobilImage"),
                    StartingDate = item.CampaignBlock.Content.Value<DateTime?>("startingDate"),
                    ExpireDate = item.CampaignBlock.Content.Value<DateTime?>("expireDate")
                })
                .ToList();

            // Score campaigns based on category similarity and date relevance
            var scoredCampaigns = relatedCampaigns
                .Select(campaign => new
                {
                    Campaign = campaign,
                    CategoryScore = currentCampaignCategories.Count > 0 
                        ? campaign.Categories.Count(cat => currentCampaignCategories.Contains(cat, StringComparer.OrdinalIgnoreCase))
                        : 0,
                    // Add date-based bonus scoring
                    DateScore = GetDateScore(campaign.StartingDate, campaign.ExpireDate, now)
                })
                .Select(item => new
                {
                    item.Campaign,
                    item.CategoryScore,
                    item.DateScore,
                    TotalScore = item.CategoryScore * 10 + item.DateScore // Category weight is higher
                })
                .Where(item => item.CategoryScore > 0 || currentCampaignCategories.Count == 0 || relatedCampaigns.Count <= maxItems)
                .OrderByDescending(item => item.TotalScore) // Sort by total relevance score
                .ThenByDescending(item => item.Campaign.StartingDate ?? DateTime.MinValue) // Then by start date
                .Take(maxItems)
                .Select(item => new
                {
                    title = item.Campaign.Title,
                    shortDescription = item.Campaign.ShortDescription,
                    image = item.Campaign.Image?.Url(),
                    mobileImage = item.Campaign.MobileImage?.Url(),
                    startingDate = item.Campaign.StartingDate.HasValue ? item.Campaign.StartingDate.Value.ToString("d.MM.yyyy HH:mm:ss") : null,
                    expireDate = item.Campaign.ExpireDate.HasValue ? item.Campaign.ExpireDate.Value.ToString("d.MM.yyyy HH:mm:ss") : null,
                    categories = item.Campaign.Categories.Select(c => new { title = c }).ToList(),
                    url = item.Campaign.Content.Url(culture),
                    relevanceScore = item.CategoryScore,
                    dateScore = item.DateScore,
                    totalScore = item.TotalScore
                })
                .ToList();

            return new
            {
                contentType = "relatedCampaigns",
                content = new
                {
                    contentTypeAlias = "relatedCampaigns",
                    maxItems = maxItems,
                    matchedCategories = currentCampaignCategories,
                    totalFound = relatedCampaigns.Count,
                    debug = new
                    {
                        currentCampaignId = currentCampaignId,
                        parentId = campaignsParent.Id,
                        parentName = campaignsParent.Name,
                        totalSiblings = campaignsParent.Children().Count(),
                        activeSiblings = relatedCampaigns.Count
                    }
                },
                campaigns = scoredCampaigns
            };
        }

        private int GetDateScore(DateTime? startingDate, DateTime? expireDate, DateTime now)
        {
            // Give bonus points for campaigns based on their timing
            if (!startingDate.HasValue || !expireDate.HasValue)
                return 0;

            var start = startingDate.Value.Date;
            var expire = expireDate.Value.Date;

            if (expire < now) // Expired campaigns - lowest priority
                return 0;
            else if (start <= now && expire >= now) // Currently active campaigns - highest priority
                return 10;
            else if (start > now) // Future campaigns - medium priority
                return 5;

            return 1; // Default fallback
        }
    }
}
