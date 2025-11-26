using Umbraco.Cms.Core.Models.PublishedContent;

namespace Morpara.Services.Interfaces
{
    public interface IBlogService
    {
        /// <summary>
        /// Gets blog list with pagination and filtering - original method name for compatibility
        /// </summary>
        object Bloglist(string id, int page = 1, int pageSize = 9, string orderBy = "Date ASC", string category = null!);

        /// <summary>
        /// Gets featured blog list - original method name for compatibility
        /// </summary>
        object FeaturedBlogList(string id, int page = 1, int pageSize = 3, string orderBy = "Date DESC", string category = null!);

        /// <summary>
        /// Gets blog post details
        /// </summary>
        object GetBlogPost(IPublishedContent content);

        /// <summary>
        /// Gets related blogs based on categories
        /// </summary>
        object GetRelatedBlogs(IPublishedContent blogPost, int limit = 3);

        /// <summary>
        /// Finds blog list node in content tree
        /// </summary>
        IPublishedContent? FindBlogListNode(IPublishedContent node);
    }
}
