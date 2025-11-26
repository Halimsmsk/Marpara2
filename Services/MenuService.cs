using Morpara.Services.Interfaces;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace Morpara.Services
{
    public class MenuService : IMenuService
    {
        private readonly IUmbracoContextAccessor _ctxAccessor;

        public MenuService(IUmbracoContextAccessor ctxAccessor)
        {
            _ctxAccessor = ctxAccessor;
        }

        public object GetStaticMenuItems(string id)
        {
            if (!_ctxAccessor.TryGetUmbracoContext(out var ctx))
                return new List<object>();

            IPublishedContent? item = Guid.TryParse(id, out var g)
                ? ctx.Content?.GetById(false, g)
                : int.TryParse(id, out var i)
                    ? ctx.Content?.GetById(i)
                    : null;

            if (item == null)
                return new List<object>();

            var children = item.Children.Select(child =>
            {
                var url = child.Url();
                string? title = null;

                // BlockGrid alanı gibi kullanılıyor
                var homeBlockGrid = child.Value<BlockGridModel>("home");

                if (homeBlockGrid != null)
                {
                    var staticSubPageBlock = homeBlockGrid
                        .FirstOrDefault(b => b.Content.ContentType.Alias == "staticSubPage");

                    if (staticSubPageBlock != null)
                    {
                        title = staticSubPageBlock.Content.Value<string>("title");
                    }
                }

                return new
                {
                    url,
                    title
                };
            }).ToList();

            return children;
        }

        public object GetStaticAbMenuItems(string id)
        {
            if (!_ctxAccessor.TryGetUmbracoContext(out var ctx))
                return new List<object>();

            IPublishedContent? item = Guid.TryParse(id, out var g)
                ? ctx.Content?.GetById(false, g)
                : int.TryParse(id, out var i)
                    ? ctx.Content?.GetById(i)
                    : null;

            if (item == null)
                return new List<object>();

            if (item.Parent == null)
                return new List<object>();

            var siblings = item.Parent.Children
                .Select<IPublishedContent, object>(child =>
                {
                    var url = child.Url();
                    string? title = child.Name;
                    bool active = child.Id == item.Id;

                    // Check if the child has children before accessing FirstChild()
                    if (child.Children != null && child.Children.Any())
                    {
                        var firstChild = child.FirstChild();
                        if (firstChild != null)
                        {
                            var homeBlockGrid = firstChild.Value<BlockGridModel>("home");

                            if (homeBlockGrid != null)
                            {
                                var staticSubPageBlock = homeBlockGrid
                                    .FirstOrDefault(b => b.Content?.ContentType?.Alias == "staticSubPage");

                                if (staticSubPageBlock?.Content != null)
                                {
                                    var specificTitle = staticSubPageBlock.Content.Value<string>("title");
                                    if (!string.IsNullOrEmpty(specificTitle))
                                    {
                                        title = specificTitle;
                                    }
                                }
                            }
                        }
                    }
                    // Alternatively, check directly for the home property on the child itself
                    else
                    {
                        var homeBlockGrid = child.Value<BlockGridModel>("home");

                        if (homeBlockGrid != null)
                        {
                            var staticSubPageBlock = homeBlockGrid
                                .FirstOrDefault(b => b.Content?.ContentType?.Alias == "staticSubPage");

                            if (staticSubPageBlock?.Content != null)
                            {
                                var specificTitle = staticSubPageBlock.Content.Value<string>("title");
                                if (!string.IsNullOrEmpty(specificTitle))
                                {
                                    title = specificTitle;
                                }
                            }
                        }
                    }

                    return new
                    {
                        url,
                        title,
                        active
                    };
                }).ToList();

            return siblings;
        }
    }
}
