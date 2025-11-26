using Morpara.Services.Interfaces;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Extensions;

namespace Morpara.Services
{
    public class CategoryService : ICategoryService
    {
        public bool HasCategory(IPublishedContent content, string categoryName)
        {
            if (content == null || string.IsNullOrEmpty(categoryName))
                return false;

            // Check for categories in questionsContent block
            var homeGrid = content.Value<BlockGridModel>("home");
            if (homeGrid != null)
            {
                foreach (var block in homeGrid)
                {
                    if (block.Content?.ContentType.Alias == "questionsContent")
                    {
                        var categories = block.Content.Value<IEnumerable<IPublishedElement>>("categories");
                        if (categories != null)
                        {
                            return categories.Any(c =>
                                string.Equals(c.Value<string>("title"), categoryName, StringComparison.OrdinalIgnoreCase));
                        }
                    }
                }
            }

            return false;
        }

        public object? GetCategoryInfo(IPublishedContent content, string? categoryName = null)
        {
            if (content == null)
                return null;

            // Look for categories in questionsContent block
            var homeGrid = content.Value<BlockGridModel>("home");
            if (homeGrid != null)
            {
                var questionBlock = homeGrid.FirstOrDefault(b => b.Content?.ContentType?.Alias == "questionsContent");
                if (questionBlock != null)
                {
                    var categories = questionBlock.Content.Value<IEnumerable<IPublishedContent>>("categories");
                    if (categories != null && categories.Any())
                    {
                        // If categoryName is specified, look for that specific category
                        IPublishedContent? categoryContent = null;
                        if (!string.IsNullOrEmpty(categoryName))
                        {
                            categoryContent = categories.FirstOrDefault(c =>
                                string.Equals(c.Name, categoryName, StringComparison.OrdinalIgnoreCase));
                        }

                        // If not found or not specified, use the first one
                        if (categoryContent == null)
                        {
                            categoryContent = categories.First();
                        }

                        return new
                        {
                            Id = categoryContent.Id,
                            Key = categoryContent.Key,
                            Name = categoryContent.Name,
                            Url = categoryContent.Url()
                        };
                    }
                }
            }

            return null;
        }

        public string ExtractSlugFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return string.Empty;

            url = url.Trim('/');
            var segments = url.Split('/');
            return segments.LastOrDefault() ?? string.Empty;
        }
    }
}
