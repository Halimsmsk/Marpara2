using Umbraco.Cms.Core.Models.PublishedContent;

namespace Morpara.Services.Interfaces
{
    public interface IPropertyMappingService
    {
        /// <summary>
        /// Maps a property value to the appropriate API format
        /// </summary>
        object? MapValue(IPublishedProperty prop, object? contentId = null, object? contentKey = null, 
            Dictionary<string, Dictionary<string, string>>? filterParams = null, string? originalUrl = null, string? categoryName = null);

        /// <summary>
        /// Converts an IPublishedElement to a dictionary for API response
        /// </summary>
        Dictionary<string, object> ConvertElementToDictionary(IPublishedElement element, object? contentId = null, object? contentKey = null);

        /// <summary>
        /// Shapes a content item for API response
        /// </summary>
        object Shape(IPublishedContent item);

        /// <summary>
        /// Parses filter parameters from string format
        /// </summary>
        Dictionary<string, Dictionary<string, string>> ParseFilterParameters(string filter);

        /// <summary>
        /// Gets the index of a block within its parent BlockGridModel
        /// </summary>
        int GetBlockIndex(object currentBlock, object blockGrid);

        /// <summary>
        /// Strips HTML tags from input
        /// </summary>
        string StripHtmlTags(string input);
    }
}
