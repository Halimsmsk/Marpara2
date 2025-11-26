using Morpara.Services.Interfaces;
using System.Text.RegularExpressions;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace Morpara.Services
{
    public class PropertyMappingServiceSimple : IPropertyMappingService
    {
        private readonly IUmbracoContextAccessor _ctxAccessor;

        public PropertyMappingServiceSimple(IUmbracoContextAccessor ctxAccessor)
        {
            _ctxAccessor = ctxAccessor;
        }

        public object? MapValue(IPublishedProperty prop, object? contentId = null, object? contentKey = null,
            Dictionary<string, Dictionary<string, string>>? filterParams = null, string? originalUrl = null, string? categoryName = null)
        {
            var value = prop.GetValue();

            if (value is IEnumerable<IPublishedElement> elementCollection)
            {
                return elementCollection
                    .Select(elem => ConvertElementToDictionary(elem, contentId, contentKey))
                    .ToList();
            }

            if (value is IPublishedElement singleElement)
            {
                return ConvertElementToDictionary(singleElement, contentId, contentKey);
            }

            if (value is IEnumerable<IPublishedContent> publishedContentList)
            {
                return publishedContentList.Select(c => new
                {
                    Id = c.Key,
                    Name = c.Name,
                    Url = c.Url()
                }).ToList();
            }

            if (value is IPublishedContent publishedContent)
            {
                return new
                {
                    Id = publishedContent.Key,
                    Name = publishedContent.Name,
                    Url = publishedContent.Url()
                };
            }

            if (value is BlockGridModel blockGrid)
            {
                // Simple BlockGrid mapping without complex service dependencies
                return blockGrid.Select(item => new
                {
                    ContentType = item.Content?.ContentType?.Alias,
                    Content = item.Content != null ? ConvertElementToDictionary(item.Content, contentId, contentKey) : null,
                    Settings = item.Settings != null ? ConvertElementToDictionary(item.Settings, contentId, contentKey) : null
                }).ToList();
            }

            if (value is BlockListModel blockList)
            {
                return blockList.Select(item => new
                {
                    ContentType = item.Content?.ContentType?.Alias,
                    Content = item.Content != null ? ConvertElementToDictionary(item.Content, contentId, contentKey) : null,
                    Settings = item.Settings != null ? ConvertElementToDictionary(item.Settings, contentId, contentKey) : null
                }).ToList();
            }

            if (value is IEnumerable<Link> linkList)
            {
                return linkList.Select(link => new
                {
                    Url = link.Url?.Replace("##", "#") ?? string.Empty,
                    Name = link.Name,
                    Target = link.Target
                }).ToList();
            }

            if (value is Link link)
            {
                return new
                {
                    Url = link.Url?.Replace("##", "#") ?? string.Empty,
                    Name = link.Name,
                    Target = link.Target
                };
            }

            if (value is IEnumerable<string> stringList)
            {
                return stringList.ToList();
            }

            if (value is string[] stringArray)
            {
                return stringArray.ToList();
            }

            if (value is MediaWithCrops mediaWithCrops)
            {
                return new
                {
                    Url = mediaWithCrops.MediaUrl(),
                    Name = mediaWithCrops.Name
                };
            }

            if (value != null && (value.GetType().IsPrimitive || value is string))
            {
                return value;
            }

            return value?.ToString();
        }

        public Dictionary<string, object> ConvertElementToDictionary(IPublishedElement element, object? contentId = null, object? contentKey = null)
        {
            var result = new Dictionary<string, object>();

            if (element.ContentType != null)
            {
                result["contentTypeAlias"] = element.ContentType.Alias;
            }

            if (contentId != null)
            {
                result["contentId"] = contentId;
            }

            if (contentKey != null)
            {
                result["contentKey"] = contentKey;
            }

            foreach (var property in element.Properties)
            {
                var mappedValue = MapValue(property, contentId, contentKey);
                if (mappedValue != null)
                {
                    result[property.Alias] = mappedValue;
                }
            }

            return result;
        }

        public object Shape(IPublishedContent item) => new
        {
            Key = item.Key,
            Name = item.Name,
            Url = item.Url(),
            ContentType = item.ContentType.Alias,
            Properties = item.Properties
                .ToDictionary(
                    keySelector: prop => prop.Alias,
                    elementSelector: prop => MapValue(prop) ?? new object()),
            Cultures = item.Cultures.ToDictionary(c => c.Key, c => item.Url(c.Key))
        };

        public Dictionary<string, Dictionary<string, string>> ParseFilterParameters(string filter)
        {
            var result = new Dictionary<string, Dictionary<string, string>>();

            if (string.IsNullOrWhiteSpace(filter))
                return result;

            var segments = filter.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 3) return result;

            var componentId = segments[0];
            var paramType = segments[1];
            var paramValue = segments[2];

            if (!result.ContainsKey(componentId))
            {
                result[componentId] = new Dictionary<string, string>();
            }

            result[componentId][paramType] = paramValue;

            return result;
        }

        public int GetBlockIndex(object currentBlock, object blockGrid)
        {
            if (currentBlock is BlockGridItem blockItem && blockGrid is BlockGridModel gridModel)
            {
                return gridModel.ToList().IndexOf(blockItem);
            }
            return 0;
        }

        public string StripHtmlTags(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            return Regex.Replace(input, "<.*?>", string.Empty);
        }
    }
}
