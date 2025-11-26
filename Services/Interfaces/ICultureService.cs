using Umbraco.Cms.Core.Models.PublishedContent;

namespace Morpara.Services.Interfaces
{
    public interface IMorparaCultureService
    {
        /// <summary>
        /// Gets alternative culture URLs for a content item by ID
        /// </summary>
        Dictionary<string, object> GetAlternativeCulturesById(int contentId);

        /// <summary>
        /// Gets all available languages
        /// </summary>
        Task<IEnumerable<object>> GetAllLanguagesAsync();
    }
}
