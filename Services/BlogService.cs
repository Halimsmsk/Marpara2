using Morpara.Services.Interfaces;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Extensions;
using System.Globalization;

namespace Morpara.Services
{
    public class BlogService : IBlogService
    {
        private readonly IUmbracoContextAccessor _ctxAccessor;
        private readonly IVariationContextAccessor _variation;

        public BlogService(
            IUmbracoContextAccessor ctxAccessor,
            IVariationContextAccessor variation)
        {
            _ctxAccessor = ctxAccessor;
            _variation = variation;
        }

        public object Bloglist(string id, int page = 1, int pageSize = 9, string orderBy = "Date ASC", string category = null!)
        {
            if (!_ctxAccessor.TryGetUmbracoContext(out var ctx))
                return new List<object>();

            // Get the current culture
            var culture = _variation.VariationContext?.Culture ?? "tr-TR";

            // First, find the parent content item by ID
            IPublishedContent? parent = Guid.TryParse(id, out var g)
                ? ctx.Content?.GetById(false, g)
                : int.TryParse(id, out var i)
                    ? ctx.Content?.GetById(i)
                    : null;

            if (parent == null)
                return new List<object>();

            // Get the blogList block from the parent
            var homeBlockGrid = parent.Value<BlockGridModel>("home");
            var blogListBlock = homeBlockGrid?.FirstOrDefault(b => b.Content?.ContentType?.Alias == "blogList");

            // Extract parameters from the blogList block but prioritize the passed parameters
            var categoriesActive = blogListBlock?.Content?.Value<bool>("categoriesActive") ?? false;
            var allCategoriesText = blogListBlock?.Content?.Value<string>("allCategoriesText") ?? "En Son";

            // Always use the passed pageSize parameter, don't override with block settings
            var blogPageSize = pageSize;
            var blogOrder = orderBy;

            // Extract categories
            var categories = blogListBlock?.Content?.Value<IEnumerable<IPublishedElement>>("categories")?
                .Select(c => new
                {
                    contentTypeAlias = c.ContentType.Alias,
                    contentId = parent.Id,
                    contentKey = parent.Key,
                    title = c.Value<string>("title"),
                    icon = c.Value<IPublishedContent>("icon")?.Url()
                }).ToList();

            // Get all children of the parent 
            var contentItems = parent.Children().ToList();

            // This is for debugging - log how many children we found
            var childCount = contentItems.Count;

            // Filter the content items based on conditions
            contentItems = contentItems
                .Where(child =>
                {
                    // Option 1: Check if this is a blog entry based on its content type
                    if (child.ContentType.Alias == "blogEntry")
                        return true;

                    // Option 2: Check if it has a blogPost block in its home property
                    var childHomeGrid = child.Value<BlockGridModel>("home");
                    if (childHomeGrid != null &&
                        childHomeGrid.Any(b => b.Content?.ContentType?.Alias == "blogPost"))
                        return true;

                    // Option 3: For pages with home.items structure (newer format)
                    var homeProperty = child.Properties.FirstOrDefault(p => p.Alias == "home");
                    if (homeProperty != null)
                    {
                        var homeValue = homeProperty.GetValue();
                        if (homeValue is Dictionary<string, object> homeDictionary &&
                            homeDictionary.TryGetValue("items", out var itemsObj) &&
                            itemsObj is IEnumerable<object> items)
                        {
                            // Try to find blogPost in the items collection
                            foreach (var item in items)
                            {
                                if (item is Dictionary<string, object> itemDict &&
                                    itemDict.TryGetValue("content", out var contentObj) &&
                                    contentObj is Dictionary<string, object> contentDict &&
                                    contentDict.TryGetValue("contentType", out var contentTypeObj) &&
                                    contentTypeObj is string contentTypeStr &&
                                    contentTypeStr == "blogPost")
                                {
                                    return true;
                                }
                            }
                        }
                    }

                    return false;
                })
                .Where(child =>
                {
                    // Filter by category if specified
                    if (string.IsNullOrEmpty(category) || category == allCategoriesText)
                        return true;

                    // Check for categories in blog entry format
                    var entryCategories = child.Value<IEnumerable<IPublishedContent>>("categories");
                    if (entryCategories != null &&
                        entryCategories.Any(c => c.Name.Equals(category, StringComparison.OrdinalIgnoreCase)))
                        return true;

                    // Check for categories in blogPost block format
                    var childHomeGrid = child.Value<BlockGridModel>("home");
                    var blogPostBlock = childHomeGrid?.FirstOrDefault(b => b.Content?.ContentType?.Alias == "blogPost");
                    if (blogPostBlock != null)
                    {
                        var blockCategories = blogPostBlock.Content.Value<IEnumerable<IPublishedContent>>("categories");
                        if (blockCategories != null &&
                            blockCategories.Any(c => c.Name.Equals(category, StringComparison.OrdinalIgnoreCase)))
                            return true;
                    }

                    return false;
                })
                .ToList();

            // Apply sorting based on blogOrder
            contentItems = blogOrder switch
            {
                "None" => contentItems,
                "A-Z" => contentItems.OrderBy(child => child.Name).ToList(),
                "Date ASC" => contentItems.OrderBy(child =>
                {
                    // Try to get date from blogPost block first
                    var childHomeGrid = child.Value<BlockGridModel>("home");
                    var blogPostBlock = childHomeGrid?.FirstOrDefault(b => b.Content?.ContentType?.Alias == "blogPost");
                    if (blogPostBlock != null)
                    {
                        var blogDate = blogPostBlock.Content.Value<DateTime?>("blogDate");
                        if (blogDate.HasValue)
                            return blogDate.Value;
                    }
                    // Fall back to CreateDate
                    return child.CreateDate;
                }).ToList(),
                "Date DESC" => contentItems.OrderByDescending(child =>
                {
                    // Try to get date from blogPost block first
                    var childHomeGrid = child.Value<BlockGridModel>("home");
                    var blogPostBlock = childHomeGrid?.FirstOrDefault(b => b.Content?.ContentType?.Alias == "blogPost");
                    if (blogPostBlock != null)
                    {
                        var blogDate = blogPostBlock.Content.Value<DateTime?>("blogDate");
                        if (blogDate.HasValue)
                            return blogDate.Value;
                    }
                    // Fall back to CreateDate
                    return child.CreateDate;
                }).ToList(),
                _ => contentItems.OrderByDescending(child => child.CreateDate).ToList() // Default to Date DESC
            };

            var totalItems = contentItems.Count;
            var totalPages = (int)Math.Ceiling((double)totalItems / blogPageSize);

            contentItems = contentItems.Skip((page - 1) * blogPageSize).Take(blogPageSize).ToList();

            var result = new
            {
                contentType = "blogList",
                content = new
                {
                    contentTypeAlias = "blogList",
                    contentId = parent.Id,
                    contentKey = parent.Key,
                    title = blogListBlock?.Content?.Value<string>("title") ?? "Blog",
                    allCategoriesText = allCategoriesText,
                    categoriesActive = categoriesActive,
                    categories = categories,
                    pageSize = blogPageSize,
                    blogOrder = blogOrder
                },
                pagination = new
                {
                    currentPage = page,
                    pageSize = blogPageSize,
                    totalItems = totalItems,
                    totalPages = totalPages
                },
                blogs = contentItems.Select(item =>
                {
                    try
                    {
                        // First check if we have a blogPost block
                        var childHomeGrid = item.Value<BlockGridModel>("home");
                        var blogPostBlock = childHomeGrid?.FirstOrDefault(b => b.Content?.ContentType?.Alias == "blogPost");

                        string title;
                        string? shortDescription = null;
                        DateTime? blogDate = null;
                        IPublishedContent? imageMedia = null;
                        IPublishedContent? coverImageMedia = null;
                        List<object>? blogCategories = null;

                        if (blogPostBlock != null)
                        {
                            // Extract data from blogPost block
                            title = blogPostBlock.Content.Value<string>("title") ?? item.Name;
                            shortDescription = blogPostBlock.Content.Value<string>("shortDescription");
                            blogDate = blogPostBlock.Content.Value<DateTime?>("blogDate");
                            imageMedia = blogPostBlock.Content.Value<IPublishedContent>("image");
                            coverImageMedia = blogPostBlock.Content.Value<IPublishedContent>("blogCoverImage");

                            var categories = blogPostBlock.Content.Value<IEnumerable<IPublishedContent>>("categories");
                            if (categories != null)
                            {
                                blogCategories = categories.Select(c => new
                                {
                                    title = c.Name,
                                    icon = c.Value<IPublishedContent>("icon")?.Url()
                                }).Cast<object>().ToList();
                            }
                        }
                        else
                        {
                            // Try to extract data directly from item properties
                            title = item.Value<string>("title") ?? item.Name;
                            shortDescription = item.Value<string>("excerpt") ?? item.Value<string>("shortDescription");
                            blogDate = item.Value<DateTime?>("blogDate");
                            imageMedia = item.Value<IPublishedContent>("featuredImage") ?? item.Value<IPublishedContent>("image");
                            coverImageMedia = item.Value<IPublishedContent>("blogCoverImage");

                            var categories = item.Value<IEnumerable<IPublishedContent>>("categories");
                            if (categories != null)
                            {
                                blogCategories = categories.Select(c => new
                                {
                                    title = c.Name,
                                    icon = c.Value<IPublishedContent>("icon")?.Url()
                                }).Cast<object>().ToList();
                            }
                        }

                        // Use blog date or fall back to creation date
                        var displayDate = blogDate ?? item.CreateDate;

                        return new
                        {
                            title,
                            shortDescription,
                            image = imageMedia?.Url(),
                            coverImage = coverImageMedia?.Url(),
                            date = displayDate.ToString("d.MM.yyyy HH:mm:ss"),
                            categories = blogCategories,
                            url = item.Url(culture)
                        };
                    }
                    catch (Exception ex)
                    {
                        // If there's an error processing this blog item, return minimal information
                        return new
                        {
                            title = item.Name,
                            shortDescription = (string?)$"Error: {ex.Message}",
                            image = (string?)null,
                            coverImage = (string?)null,
                            date = item.CreateDate.ToString("d.MM.yyyy HH:mm:ss"),
                            categories = (List<object>?)new List<object>(),
                            url = item.Url(culture)
                        };
                    }
                }).ToList()
            };

            return result;
        }

        public object FeaturedBlogList(string id, int page = 1, int pageSize = 3, string orderBy = "Date DESC", string category = null!)
        {
            if (!_ctxAccessor.TryGetUmbracoContext(out var ctx))
                return new List<object>();

            // Get the current culture
            var culture = _variation.VariationContext?.Culture ?? "tr-TR";

            // Get the content item with the specified ID (this is the page containing the featured block)
            IPublishedContent? currentPage = Guid.TryParse(id, out var g)
                ? ctx.Content?.GetById(false, g)
                : int.TryParse(id, out var i)
                    ? ctx.Content?.GetById(i)
                    : null;

            if (currentPage == null)
                return new List<object>();

            // Find the parent content that may have blog posts
            // First try going one level up to find the parent
            var parent = currentPage.Parent;

            // If no parent or parent has no content, find blog root node
            if (parent == null || !parent.Children().Any())
            {
                // Try to find a blog root node (a node with blogList block)
                var rootNodes = ctx.Content?.GetAtRoot(culture);
                if (rootNodes != null)
                {
                    foreach (var root in rootNodes)
                    {
                        // Look recursively for a blog list node
                        parent = FindBlogListNode(root);
                        if (parent != null)
                            break;
                    }
                }
            }

            if (parent == null)
                return new List<object>();

            // Get the blogFeatured block from the current page for configuration
            var homeBlockGrid = currentPage.Value<BlockGridModel>("home");
            var blogFeaturedBlock = homeBlockGrid?.FirstOrDefault(b => b.Content?.ContentType?.Alias == "blogFeatured");

            // Extract parameters from the blogFeatured block but prioritize the passed parameters
            var title = blogFeaturedBlock?.Content?.Value<string>("title") ?? "Öne Çıkan Gönderiler";

            // Always use the passed pageSize parameter
            var featuredPageSize = pageSize;

            // Get all blog posts from the parent, excluding the current page
            var contentItems = parent.Children()
                .Where(child => child.Id != currentPage.Id) // Exclude the current page
                .Where(child =>
                {
                    // Only consider blog entries
                    // Option 1: Check if this is a blog entry based on its content type
                    if (child.ContentType.Alias == "blogEntry")
                        return true;

                    // Option 2: Check if it has a blogPost block in its home property
                    var childHomeGrid = child.Value<BlockGridModel>("home");
                    if (childHomeGrid != null &&
                        childHomeGrid.Any(b => b.Content?.ContentType?.Alias == "blogPost"))
                        return true;

                    // Option 3: For newer format
                    var homeProperty = child.Properties.FirstOrDefault(p => p.Alias == "home");
                    if (homeProperty != null)
                    {
                        var homeValue = homeProperty.GetValue();
                        if (homeValue is Dictionary<string, object> homeDictionary &&
                            homeDictionary.TryGetValue("items", out var itemsObj) &&
                            itemsObj is IEnumerable<object> items)
                        {
                            foreach (var item in items)
                            {
                                if (item is Dictionary<string, object> itemDict &&
                                    itemDict.TryGetValue("content", out var contentObj) &&
                                    contentObj is Dictionary<string, object> contentDict &&
                                    contentDict.TryGetValue("contentType", out var contentTypeObj) &&
                                    contentTypeObj is string contentTypeStr &&
                                    contentTypeStr == "blogPost")
                                {
                                    return true;
                                }
                            }
                        }
                    }

                    return false;
                })
                .Where(child =>
                {
                    // Filter by category if specified
                    if (string.IsNullOrEmpty(category))
                        return true;

                    // Check for categories in blog entry format
                    var entryCategories = child.Value<IEnumerable<IPublishedContent>>("categories");
                    if (entryCategories != null &&
                        entryCategories.Any(c => c.Name.Equals(category, StringComparison.OrdinalIgnoreCase)))
                        return true;

                    // Check for categories in blogPost block format
                    var childHomeGrid = child.Value<BlockGridModel>("home");
                    var blogPostBlock = childHomeGrid?.FirstOrDefault(b => b.Content?.ContentType?.Alias == "blogPost");
                    if (blogPostBlock != null)
                    {
                        var blockCategories = blogPostBlock.Content.Value<IEnumerable<IPublishedContent>>("categories");
                        if (blockCategories != null &&
                            blockCategories.Any(c => c.Name.Equals(category, StringComparison.OrdinalIgnoreCase)))
                            return true;
                    }

                    return false;
                })
                .ToList();

            // Apply sorting based on orderBy
            contentItems = orderBy switch
            {
                "None" => contentItems,
                "A-Z" => contentItems.OrderBy(child => child.Name).ToList(),
                "Date ASC" => contentItems.OrderBy(child =>
                {
                    // Try to get date from blogPost block first
                    var childHomeGrid = child.Value<BlockGridModel>("home");
                    var blogPostBlock = childHomeGrid?.FirstOrDefault(b => b.Content?.ContentType?.Alias == "blogPost");
                    if (blogPostBlock != null)
                    {
                        var blogDate = blogPostBlock.Content.Value<DateTime?>("blogDate");
                        if (blogDate.HasValue)
                            return blogDate.Value;
                    }
                    // Fall back to CreateDate
                    return child.CreateDate;
                }).ToList(),
                "Date DESC" => contentItems.OrderByDescending(child =>
                {
                    // Try to get date from blogPost block first
                    var childHomeGrid = child.Value<BlockGridModel>("home");
                    var blogPostBlock = childHomeGrid?.FirstOrDefault(b => b.Content?.ContentType?.Alias == "blogPost");
                    if (blogPostBlock != null)
                    {
                        var blogDate = blogPostBlock.Content.Value<DateTime?>("blogDate");
                        if (blogDate.HasValue)
                            return blogDate.Value;
                    }
                    // Fall back to CreateDate
                    return child.CreateDate;
                }).ToList(),
                _ => contentItems.OrderByDescending(child => child.CreateDate).ToList() // Default to Date DESC
            };

            var totalItems = contentItems.Count;
            var totalPages = (int)Math.Ceiling((double)totalItems / featuredPageSize);

            // Apply pagination
            contentItems = contentItems.Skip((page - 1) * featuredPageSize).Take(featuredPageSize).ToList();

            var result = new
            {
                contentType = "blogFeatured",
                content = new
                {
                    contentTypeAlias = "blogFeatured",
                    contentId = currentPage.Id,
                    contentKey = currentPage.Key,
                    title = title,
                    pageSize = featuredPageSize
                },
                pagination = new
                {
                    currentPage = page,
                    pageSize = featuredPageSize,
                    totalItems = totalItems,
                    totalPages = totalPages
                },
                blogs = contentItems.Select(item =>
                {
                    try
                    {
                        // First check if we have a blogPost block
                        var childHomeGrid = item.Value<BlockGridModel>("home");
                        var blogPostBlock = childHomeGrid?.FirstOrDefault(b => b.Content?.ContentType?.Alias == "blogPost");

                        string title;
                        string? shortDescription = null;
                        DateTime? blogDate = null;
                        IPublishedContent? imageMedia = null;
                        IPublishedContent? coverImageMedia = null;
                        List<object>? blogCategories = null;

                        if (blogPostBlock != null)
                        {
                            // Extract data from blogPost block
                            title = blogPostBlock.Content.Value<string>("title") ?? item.Name;
                            shortDescription = blogPostBlock.Content.Value<string>("shortDescription");
                            blogDate = blogPostBlock.Content.Value<DateTime?>("blogDate");
                            imageMedia = blogPostBlock.Content.Value<IPublishedContent>("image");
                            coverImageMedia = blogPostBlock.Content.Value<IPublishedContent>("blogCoverImage");

                            var categories = blogPostBlock.Content.Value<IEnumerable<IPublishedContent>>("categories");
                            if (categories != null)
                            {
                                blogCategories = categories.Select(c => new
                                {
                                    title = c.Name,
                                    icon = c.Value<IPublishedContent>("icon")?.Url()
                                }).Cast<object>().ToList();
                            }
                        }
                        else
                        {
                            // Try to extract data directly from item properties
                            title = item.Value<string>("title") ?? item.Name;
                            shortDescription = item.Value<string>("excerpt") ?? item.Value<string>("shortDescription");
                            blogDate = item.Value<DateTime?>("blogDate");
                            imageMedia = item.Value<IPublishedContent>("featuredImage") ?? item.Value<IPublishedContent>("image");
                            coverImageMedia = item.Value<IPublishedContent>("blogCoverImage");

                            var categories = item.Value<IEnumerable<IPublishedContent>>("categories");
                            if (categories != null)
                            {
                                blogCategories = categories.Select(c => new
                                {
                                    title = c.Name,
                                    icon = c.Value<IPublishedContent>("icon")?.Url()
                                }).Cast<object>().ToList();
                            }
                        }

                        // Use blog date or fall back to creation date
                        var displayDate = blogDate ?? item.CreateDate;

                        return new
                        {
                            title,
                            shortDescription,
                            image = imageMedia?.Url(),
                            coverImage = coverImageMedia?.Url(),
                            date = displayDate.ToString("d.MM.yyyy HH:mm:ss"),
                            categories = blogCategories,
                            url = item.Url(culture)
                        };
                    }
                    catch (Exception ex)
                    {
                        // If there's an error processing this blog item, return minimal information
                        return new
                        {
                            title = item.Name,
                            shortDescription = (string?)$"Error: {ex.Message}",
                            image = (string?)null,
                            coverImage = (string?)null,
                            date = item.CreateDate.ToString("d.MM.yyyy HH:mm:ss"),
                            categories = (List<object>?)new List<object>(),
                            url = item.Url(culture)
                        };
                    }
                }).ToList()
            };

            return result;
        }

        public object GetBlogPost(IPublishedContent content)
        {
            if (content == null)
                return null;

            var homeBlockGrid = content.Value<BlockGridModel>("home");
            var blogPostBlock = homeBlockGrid?.FirstOrDefault(b => b.Content?.ContentType?.Alias == "blogPost");

            if (blogPostBlock == null)
                return null;

            var culture = _variation.VariationContext?.Culture ?? "tr-TR";

            var title = blogPostBlock.Content.Value<string>("title") ?? content.Name;
            var shortDescription = blogPostBlock.Content.Value<string>("shortDescription");
            var blogContent = blogPostBlock.Content.Value<string>("blogContent");
            var blogDate = blogPostBlock.Content.Value<DateTime?>("blogDate");

            var image = blogPostBlock.Content.Value<IPublishedContent>("image");
            var mobileImage = blogPostBlock.Content.Value<IPublishedContent>("mobileImage");
            var blogCoverImage = blogPostBlock.Content.Value<IPublishedContent>("blogCoverImage");

            var categories = blogPostBlock.Content.Value<IEnumerable<IPublishedContent>>("categories")?
                .Select(c => new
                {
                    title = c.Name,
                    icon = c.Value<IPublishedContent>("icon")?.Url()
                }).ToList();

            // Blog ana sayfasındaki tüm kategorileri bul
            var blogListCategories = new List<object>();
            var blogListParent = content.Parent;
            if (blogListParent != null)
            {
                var parentHomeBlockGrid = blogListParent.Value<BlockGridModel>("home");
                var blogListBlock = parentHomeBlockGrid?.FirstOrDefault(b => b.Content?.ContentType?.Alias == "blogList");
                if (blogListBlock != null)
                {
                    blogListCategories = blogListBlock.Content.Value<IEnumerable<IPublishedElement>>("categories")?
                        .Select(c => new
                        {
                            title = c.Value<string>("title"),
                            icon = c.Value<IPublishedContent>("icon")?.Url()
                        }).Cast<object>().ToList() ?? new List<object>();
                }
            }

            return new
            {
                contentType = "blogPost",
                content = new
                {
                    contentTypeAlias = "blogPost",
                    contentId = content.Id,
                    contentKey = content.Key,
                    title,
                    blogDate = blogDate.HasValue ? blogDate.Value.ToString("dd.MM.yyyy HH:mm:ss") : null,
                    shortDescription,
                    blogContent,
                    image = image != null ? new
                    {
                        url = image.Url(),
                        width = image.Value<int>("umbracoWidth"),
                        height = image.Value<int>("umbracoHeight")
                    } : null,
                    mobileImage = mobileImage?.Url(),
                    blogCoverImage = blogCoverImage != null ? new
                    {
                        url = blogCoverImage.Url(),
                        width = blogCoverImage.Value<int>("umbracoWidth"),
                        height = blogCoverImage.Value<int>("umbracoHeight")
                    } : null,
                    categories, // Bu blog yazısının kategorileri
                    allCategories = blogListCategories // Blog ana sayfasındaki tüm kategoriler
                },
                relatedBlogs = GetRelatedBlogs(content)
            };
        }

        public object GetRelatedBlogs(IPublishedContent blogPost, int limit = 3)
        {
            if (!_ctxAccessor.TryGetUmbracoContext(out var ctx) || blogPost == null)
                return new List<object>();

            var culture = _variation.VariationContext?.Culture ?? "tr-TR";

            // Get current blog's categories
            var homeBlockGrid = blogPost.Value<BlockGridModel>("home");
            var blogPostBlock = homeBlockGrid?.FirstOrDefault(b => b.Content?.ContentType?.Alias == "blogPost");

            if (blogPostBlock == null)
                return new List<object>();

            var currentCategories = blogPostBlock.Content.Value<IEnumerable<IPublishedContent>>("categories");

            if (currentCategories == null || !currentCategories.Any())
                return new List<object>();

            var categoryIds = currentCategories.Select(c => c.Id).ToList();

            // Get parent blog list page
            var parent = blogPost.Parent;
            if (parent == null)
                return new List<object>();

            // Get other blog posts from same parent that share categories
            var relatedBlogs = parent.Children()
                .Where(child => child.Id != blogPost.Id) // Exclude current blog
                .Where(child =>
                {
                    var childHomeGrid = child.Value<BlockGridModel>("home");
                    var childBlogBlock = childHomeGrid?.FirstOrDefault(b => b.Content?.ContentType?.Alias == "blogPost");

                    if (childBlogBlock == null)
                        return false;

                    var childCategories = childBlogBlock.Content.Value<IEnumerable<IPublishedContent>>("categories");

                    return childCategories != null &&
                           childCategories.Any(c => categoryIds.Contains(c.Id));
                })
                .OrderByDescending(blog => blog.CreateDate)
                .Take(limit)
                .Select(item =>
                {
                    var itemHomeGrid = item.Value<BlockGridModel>("home");
                    var itemBlogBlock = itemHomeGrid?.FirstOrDefault(b => b.Content?.ContentType?.Alias == "blogPost");

                    var title = itemBlogBlock?.Content.Value<string>("title") ?? item.Name;
                    var shortDescription = itemBlogBlock?.Content.Value<string>("shortDescription");
                    var blogDate = itemBlogBlock?.Content.Value<DateTime?>("blogDate") ?? item.CreateDate;
                    var coverImage = itemBlogBlock?.Content.Value<IPublishedContent>("blogCoverImage");
                    var itemCategories = itemBlogBlock?.Content.Value<IEnumerable<IPublishedContent>>("categories");

                    return new
                    {
                        title,
                        shortDescription,
                        image = coverImage?.Url(),
                        date = blogDate.ToString("dd.MM.yyyy HH:mm:ss"),
                        url = item.Url(culture),
                        categories = itemCategories?.Select(cat => new
                        {
                            title = cat.Value<string>("title") ?? cat.Name,
                            icon = cat.Value<IPublishedContent>("icon")?.Url()
                        }).ToList()
                    };
                })
                .ToList();

            return relatedBlogs;
        }

        public IPublishedContent? FindBlogListNode(IPublishedContent node)
        {
            // Check if this node has a blogList block
            var homeBlockGrid = node.Value<BlockGridModel>("home");
            if (homeBlockGrid != null &&
                homeBlockGrid.Any(b => b.Content?.ContentType?.Alias == "blogList"))
            {
                return node;
            }

            // Check children recursively
            foreach (var child in node.Children())
            {
                var found = FindBlogListNode(child);
                if (found != null)
                    return found;
            }

            return null;
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
