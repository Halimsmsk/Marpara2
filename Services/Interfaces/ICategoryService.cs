using Umbraco.Cms.Core.Models.PublishedContent;

namespace Morpara.Services.Interfaces
{
    public interface ICategoryService
    {
        /// <summary>
        /// Checks if content has a specific category
        /// </summary>
        bool HasCategory(IPublishedContent content, string categoryName);

        /// <summary>
        /// Gets category information from content
        /// </summary>
        object? GetCategoryInfo(IPublishedContent content, string? categoryName = null);

        /// <summary>
        /// Extracts slug from URL path
        /// </summary>
        string ExtractSlugFromUrl(string url);
    }
}
