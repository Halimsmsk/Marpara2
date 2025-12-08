using Umbraco.Cms.Core.Models.PublishedContent;

namespace Morpara.Services.Interfaces
{
    public interface IMorparaCultureService
    {
        /// <summary>
        /// Gets alternative culture URLs for a content item by ID
        /// </summary>
        /// <param name="categoryName">Optional category URL segment to append to URLs</param>
        /// <param name="activeContentId">Optional active content URL name to append to URLs (for detail pages)</param>
        Dictionary<string, object> GetAlternativeCulturesById(int contentId, string? categoryName = null, string? activeContentId = null);

        /// <summary>
        /// Gets all available languages
        /// </summary>
        Task<IEnumerable<object>> GetAllLanguagesAsync();
    }
}
