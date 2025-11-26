using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Morpara.Services.Interfaces
{
    public interface IContentRetrievalService
    {
        /// <summary>
        /// Retrieves content by ID (GUID or int)
        /// </summary>
        IPublishedContent GetContentById(string id);

        /// <summary>
        /// Retrieves content by alias
        /// </summary>
        IPublishedContent GetContentByAlias(string alias, string culture);

        /// <summary>
        /// Retrieves content by URL with category support
        /// </summary>
        (IPublishedContent content, string categoryName) GetContentByUrl(string url, string culture, bool preview = false);

        /// <summary>
        /// Finds content recursively by content type alias and name
        /// </summary>
        IPublishedContent FindContentRecursively(IPublishedContent node, string contentTypeAlias, string name);

        /// <summary>
        /// Finds content by path structure
        /// </summary>
        IPublishedContent FindContentByPathStructure(IPublishedContent node, string[] pathSegments, int currentIndex);

        /// <summary>
        /// Finds content by URL recursively
        /// </summary>
        IPublishedContent FindContentByUrl(IPublishedContent content, string url, string culture);

        /// <summary>
        /// Gets all children of a content item shaped for API response
        /// </summary>
        List<object> GetShapedChildren(IPublishedContent item);
    }
}
