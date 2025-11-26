using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Core;
using Umbraco.Cms.Web.Common.Controllers;

namespace Morpara.Controllers
{
    public class LocationController : UmbracoApiController
    {
        private readonly IUmbracoContextAccessor _umbracoContextAccessor;
        private readonly IUmbracoContextFactory _contextFactory;
        private readonly IPublishedContentQuery _publishedContentQuery;
        private readonly IVariationContextAccessor _variationContextAccessor;

        public LocationController(
            IUmbracoContextFactory contextFactory,
            IPublishedContentQuery publishedContentQuery,
            IVariationContextAccessor variationContextAccessor,
            IUmbracoContextAccessor umbracoContextAccessor)
        {
            _umbracoContextAccessor = umbracoContextAccessor;
            _contextFactory = contextFactory;
            _publishedContentQuery = publishedContentQuery;
            _variationContextAccessor = variationContextAccessor;
        }

        [HttpGet]
        public IActionResult GetCountries(string culture = "tr-TR", bool preview = false)
        {
            string alias = "country";

            return GetContentByAlias(alias, null, null, culture, preview);
        }

        [HttpGet]
        public IActionResult GetCities(Guid? countryId = null, string culture = "tr-TR", bool preview = false)
        {
            string alias = "city";
            string propertyAlias = "countryId";

            return GetContentByAlias(alias, propertyAlias, countryId, culture, preview);
        }

        [HttpGet]
        public IActionResult GetDistricts(int? cityId = null, string culture = "tr-TR", bool preview = false)
        {
            string alias = "district";
            string propertyAlias = "cityId";

            return GetContentByAliasForCity(alias, propertyAlias, cityId, culture, preview);
        }

        private IActionResult GetContentByAlias(string alias, string propertyAlias = null, Guid? parentId = null, string culture = "tr-TR", bool preview = false)
        {
            if (string.IsNullOrEmpty(alias))
            {
                return BadRequest("Parametre Hatası: Content alias is required");
            }

            using (var umbracoContextReference = _contextFactory.EnsureUmbracoContext())
            {
                // Set the culture for variant content
                _variationContextAccessor.VariationContext = new VariationContext(culture);

                // Query content based on alias
                var contentItemsQuery = _publishedContentQuery.ContentAtRoot()
                    .DescendantsOrSelfOfType(alias)
                    .Where(x => x.ContentType.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase));

                // Apply additional filter if property alias and parent ID are provided
                if (!string.IsNullOrEmpty(propertyAlias) && parentId.HasValue)
                {
                    contentItemsQuery = contentItemsQuery.Where(x =>
                        x.HasProperty(propertyAlias) &&
                        x.Value<Guid>(propertyAlias) == parentId.Value);
                }

                var contentItems = contentItemsQuery.ToList();

                if (!contentItems.Any())
                {
                    return NotFound("İçerik Bulunamadı");
                }

                // Handle preview mode if requested
                if (preview)
                {
                    if (_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
                    {
                        var previewContentItems = contentItems
                            .Select(item => umbracoContext.Content.GetById(true, item.Key))
                            .Where(item => item != null)
                            .ToList();

                        var response = previewContentItems.Select(item => new
                        {
                            Id = item.Key,
                            Name = item.Name,
                            Url = item.Url(mode: UrlMode.Absolute),
                            Properties = item.Properties.ToDictionary(prop => prop.Alias, prop => GetPropertyValue(prop))
                        });

                        return Ok(response);
                    }
                }
                else
                {
                    var response = contentItems.Select(item => new
                    {
                        Id = item.Key,
                        Name = item.Name,
                        Url = item.Url(mode: UrlMode.Absolute),
                        Properties = item.Properties.ToDictionary(prop => prop.Alias, prop => GetPropertyValue(prop))
                    });

                    return Ok(response);
                }
            }

            return StatusCode(500, "Beklenmedik bir hata oluştu.");
        }

        private IActionResult GetContentByAliasForCity(string alias, string propertyAlias = null, int? cityId = null, string culture = "tr-TR", bool preview = false)
        {
            if (string.IsNullOrEmpty(alias))
            {
                return BadRequest("Parametre Hatası: Content alias is required");
            }

            using (var umbracoContextReference = _contextFactory.EnsureUmbracoContext())
            {
                // Set the culture for variant content
                _variationContextAccessor.VariationContext = new VariationContext(culture);

                // Query content based on alias
                var contentItemsQuery = _publishedContentQuery.ContentAtRoot()
                    .DescendantsOrSelfOfType(alias)
                    .Where(x => x.ContentType.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase));

                // Apply additional filter if property alias and cityId are provided
                if (!string.IsNullOrEmpty(propertyAlias) && cityId.HasValue)
                {
                    contentItemsQuery = contentItemsQuery.Where(x =>
                        x.HasProperty(propertyAlias) &&
                        x.Value<int>(propertyAlias) == cityId.Value);
                }

                var contentItems = contentItemsQuery.ToList();

                if (!contentItems.Any())
                {
                    return NotFound("İçerik Bulunamadı");
                }

                // Handle preview mode if requested
                if (preview)
                {
                    if (_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
                    {
                        var previewContentItems = contentItems
                            .Select(item => umbracoContext.Content.GetById(true, item.Key))
                            .Where(item => item != null)
                            .ToList();

                        var response = previewContentItems.Select(item => new
                        {
                            Id = item.Key,
                            Name = item.Name,
                            Url = item.Url(mode: UrlMode.Absolute),
                            Properties = item.Properties.ToDictionary(prop => prop.Alias, prop => GetPropertyValue(prop))
                        });

                        return Ok(response);
                    }
                }
                else
                {
                    var response = contentItems.Select(item => new
                    {
                        Id = item.Key,
                        Name = item.Name,
                        Url = item.Url(mode: UrlMode.Absolute),
                        Properties = item.Properties.ToDictionary(prop => prop.Alias, prop => GetPropertyValue(prop))
                    });

                    return Ok(response);
                }
            }

            return StatusCode(500, "Beklenmedik bir hata oluştu.");
        }

        private object GetPropertyValue(IPublishedProperty property)
        {
            var value = property.GetValue();

            if (value is IEnumerable<IPublishedContent> publishedContentList)
            {
                return publishedContentList.Select(c => new
                {
                    Id = c.Key,
                    Name = c.Name,
                    Url = c.Url(mode: UrlMode.Absolute)
                }).ToList();
            }
            else if (value is IPublishedContent publishedContent)
            {
                return new
                {
                    Id = publishedContent.Key,
                    Name = publishedContent.Name,
                    Url = publishedContent.Url(mode: UrlMode.Absolute)
                };
            }
            else if (value is Udi udi)
            {
                return udi.ToString();
            }
            else if (value != null && (value.GetType().IsPrimitive || value is string || value is Guid || value is DateTime))
            {
                return value;
            }

            return value?.ToString();
        }
    }
}

