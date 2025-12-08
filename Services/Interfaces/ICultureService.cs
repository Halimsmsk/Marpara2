using Umbraco.Cms.Core.Models.PublishedContent;

namespace Morpara.Services.Interfaces
{
    public interface IMorparaCultureService
    {
        /// <summary>
        /// Gets alternative culture URLs for a content item by ID
        /// </summary>
        /// <param name="categoryId">Optional category ID to append culture-specific URL segment</param>
        Dictionary<string, object> GetAlternativeCulturesById(int contentId, int? categoryId = null);

        /// <summary>
        /// Gets all available languages
        /// </summary>
        Task<IEnumerable<object>> GetAllLanguagesAsync();
    }
}
