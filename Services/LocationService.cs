using Morpara.Services.Interfaces;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Extensions;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Services;
using Morpara.Controllers;
using System.Reflection;
using Umbraco.Cms.Web.Common.PublishedModels;
using Umbraco.Cms.Core;

namespace Morpara.Services
{
    public class LocationService : ILocationService
    {
        private readonly IUmbracoContextAccessor _ctxAccessor;
        private readonly IVariationContextAccessor _variation;
        private readonly IServiceProvider _serviceProvider;
        private readonly IPublishedContentQuery _contentQuery;

        public LocationService(
            IUmbracoContextAccessor ctxAccessor,
            IVariationContextAccessor variation,
            IServiceProvider serviceProvider,
            IPublishedContentQuery contentQuery)
        {
            _ctxAccessor = ctxAccessor;
            _variation = variation;
            _serviceProvider = serviceProvider;
            _contentQuery = contentQuery;
        }

        public object GetRepresentationItems(string id, int page = 1, int pageSize = 50, string orderBy = "A-Z", 
            string city = null!, string search = null!)
        {
            if (!_ctxAccessor.TryGetUmbracoContext(out var ctx))
                return new List<object>();

            // Get the current culture
            var culture = _variation.VariationContext?.Culture ?? "tr-TR";

            // First, find the parent content item by ID
            IPublishedContent? parent = Guid.TryParse(id, out var g)
                ? ctx.Content?.GetById(false, g)
                : int.TryParse(id, out var i)
                    ? ctx.Content?.GetById(i)
                    : null;

            if (parent == null)
                return new List<object>();

            // Get child pages first
            var childPages = parent.Children().ToList();

            // List to store all representation items (from both direct properties and BlockGrid blocks)
            var representationItems = new List<(IPublishedContent Page, IPublishedElement? Block, bool IsDirectChild)>();

            // Process each child page to extract the representationItem from its BlockGrid
            foreach (var childPage in childPages)
            {
                // Check if the child has a home BlockGrid property
                var homeBlockGrid = childPage.Value<BlockGridModel>("home");
                if (homeBlockGrid != null)
                {
                    // Find representationItem blocks within the home property
                    var repBlocks = homeBlockGrid
                        .Where(b => b.Content?.ContentType?.Alias == "representationItem" ||
                                    b.Content?.ContentType?.Alias == "representation" ||
                                    b.Content?.ContentType?.Alias == "representative")
                        .ToList();

                    // If we found any representationItem blocks, add them to our collection
                    foreach (var block in repBlocks)
                    {
                        representationItems.Add((childPage, block.Content, false));
                    }
                }

                // If the child itself is a representationItem, also consider that
                if (childPage.ContentType.Alias == "representationItem" ||
                    childPage.ContentType.Alias == "representation" ||
                    childPage.ContentType.Alias == "representative")
                {
                    representationItems.Add((childPage, null, true));
                }
            }

            // Apply search filter if specified
            if (!string.IsNullOrEmpty(search))
            {
                var searchTerm = search.Trim().ToLowerInvariant();
                representationItems = representationItems
                    .Where(item =>
                    {
                        // Get the representative name and address to search in
                        string repName = item.IsDirectChild
                            ? item.Page.Value<string>("representativeName", culture) ?? item.Page.Name
                            : item.Block?.Value<string>("representativeName") ?? item.Page.Name;

                        string address = item.IsDirectChild
                            ? item.Page.Value<string>("adress", culture) ?? item.Page.Value<string>("address", culture) ?? ""
                            : item.Block?.Value<string>("adress") ?? item.Block?.Value<string>("address") ?? "";

                        string cityValue = item.IsDirectChild
                            ? item.Page.Value<string>("city", culture) ?? ""
                            : item.Block?.Value<string>("city") ?? "";

                        string relatedPerson = item.IsDirectChild
                            ? item.Page.Value<string>("relatedPerson", culture) ?? ""
                            : item.Block?.Value<string>("relatedPerson") ?? "";

                        // Combine all fields for searching
                        string searchText = $"{repName} {address} {cityValue} {relatedPerson}".ToLowerInvariant();

                        // Check if searchTerm exists in any of the fields
                        return searchText.Contains(searchTerm);
                    })
                    .ToList();
            }

            // Apply city filter if specified
            if (!string.IsNullOrEmpty(city))
            {
                representationItems = representationItems
                    .Where(item =>
                    {
                        var cityValue = item.IsDirectChild
                            ? item.Page.Value<string>("city", culture)
                            : item.Block?.Value<string>("city");

                        return cityValue != null &&
                               string.Equals(cityValue, city, StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();
            }

            // Apply sorting based on orderBy parameter
            representationItems = orderBy switch
            {
                "None" => representationItems,
                "A-Z" => representationItems.OrderBy(item => item.Page.Name).ToList(),
                "Z-A" => representationItems.OrderByDescending(item => item.Page.Name).ToList(),
                "CITY ASC" => representationItems.OrderBy(item =>
                {
                    return item.IsDirectChild
                        ? item.Page.Value<string>("city", culture)
                        : item.Block?.Value<string>("city");
                }).ToList(),
                "CITY DSC" => representationItems.OrderByDescending(item =>
                {
                    return item.IsDirectChild
                        ? item.Page.Value<string>("city", culture)
                        : item.Block?.Value<string>("city");
                }).ToList(),
                "DATE ASC" => representationItems.OrderBy(item =>
                {
                    var date = item.IsDirectChild
                        ? item.Page.Value<DateTime?>("openingDate")
                        : item.Block?.Value<DateTime?>("openingDate");
                    return date ?? item.Page.CreateDate;
                }).ToList(),
                "DATE DESC" => representationItems.OrderByDescending(item =>
                {
                    var date = item.IsDirectChild
                        ? item.Page.Value<DateTime?>("openingDate")
                        : item.Block?.Value<DateTime?>("openingDate");
                    return date ?? item.Page.CreateDate;
                }).ToList(),
                _ => representationItems.OrderBy(item => item.Page.Name).ToList() // Default to A-Z
            };

            // Collect all available cities for filtering
            var availableCities = representationItems
                .Select(item =>
                {
                    return item.IsDirectChild
                        ? item.Page.Value<string>("city", culture)
                        : item.Block?.Value<string>("city");
                })
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            // Count total items before pagination
            var totalItems = representationItems.Count();
            var totalPages = (int)Math.Ceiling((double)totalItems / pageSize);

            // Apply pagination
            representationItems = representationItems.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            // Format the result with all the representationItem properties
            var result = new
            {
                contentType = "representationList",
                content = new
                {
                    contentTypeAlias = "representationList",
                    contentId = parent.Id,
                    contentKey = parent.Key,
                    title = parent.Name,
                    cities = availableCities,
                    searchTerm = search // Ekle
                },
                pagination = new
                {
                    currentPage = page,
                    pageSize = pageSize,
                    totalItems = totalItems,
                    totalPages = totalPages
                },
                representatives = representationItems.Select(item =>
                {
                    var page = item.Page;
                    var block = item.Block;
                    var isDirectChild = item.IsDirectChild;

                    // Get properties depending on whether we're using a direct child or a block
                    string repName = isDirectChild
                        ? page.Value<string>("representativeName", culture) ?? page.Name
                        : block?.Value<string>("representativeName") ?? page.Name;

                    string? relatedPerson = isDirectChild
                        ? page.Value<string>("relatedPerson", culture)
                        : block?.Value<string>("relatedPerson");

                    string? cityValue = isDirectChild
                        ? page.Value<string>("city", culture)
                        : block?.Value<string>("city");

                    string? address = isDirectChild
                        ? page.Value<string>("adress", culture) ?? page.Value<string>("address", culture)
                        : block?.Value<string>("adress") ?? block?.Value<string>("address");

                    string? phone = isDirectChild
                        ? page.Value<string>("phone", culture)
                        : block?.Value<string>("phone");

                    string? locLong = isDirectChild
                        ? page.Value<string>("locationLong", culture)
                        : block?.Value<string>("locationLong");

                    string? locLat = isDirectChild
                        ? page.Value<string>("locationLat", culture)
                        : block?.Value<string>("locationLat");

                    string? mapObj = isDirectChild
                        ? page.Value<string>("map", culture)
                        : block?.Value<string>("map");

                    bool active = isDirectChild
                        ? page.Value<bool>("active", culture)
                        : block?.Value<bool>("active") ?? false;

                    string? centralBankCode = isDirectChild
                        ? page.Value<string>("centralBankCode", culture)
                        : block?.Value<string>("centralBankCode");

                    string? workHours = isDirectChild
                        ? page.Value<string>("workHours", culture)
                        : block?.Value<string>("workHours");

                    DateTime? openingDate = isDirectChild
                        ? page.Value<DateTime?>("openingDate", culture)
                        : block?.Value<DateTime?>("openingDate");

                    var logo = isDirectChild
                        ? page.Value<IPublishedContent>("logo", culture)
                        : block?.Value<IPublishedContent>("logo");

                    return new
                    {
                        contentType = isDirectChild ? page.ContentType.Alias : "representationItem",
                        name = page.Name,
                        createDate = page.CreateDate.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        updateDate = page.UpdateDate.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        route = new
                        {
                            path = page.Url(culture),
                            startItem = new
                            {
                                id = page.Root()?.Key.ToString(),
                                path = page.Root()?.Name
                            }
                        },
                        id = page.Key,
                        properties = new
                        {
                            openingDate = openingDate?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                            representativeName = repName,
                            relatedPerson = relatedPerson,
                            logo = logo?.Url(),
                            city = cityValue,
                            address = address,
                            phone = phone,
                            locationLong = locLong,
                            locationLat = locLat,
                            map = mapObj,
                            active = active,
                            centralBankCode = centralBankCode,
                            workHours = workHours
                        }
                    };
                }).ToList()
            };

            return result;
        }

        public List<object>? GetCountryDataFromLocationApi()
        {
            try
            {
                // LocationController instance oluştur
                var contextFactory = _serviceProvider.GetRequiredService<IUmbracoContextFactory>();
                var locationController = new LocationController(
                    contextFactory,
                    _contentQuery,
                    _variation,
                    _ctxAccessor
                );

                var result = locationController.GetCountries(culture: "tr-TR", preview: false);

                if (result is OkObjectResult okResult && okResult.Value != null)
                {
                    // LocationController'dan gelen veriyi dönüştür
                    if (okResult.Value is IEnumerable<object> countries)
                    {
                        var countryList = countries.Select(country =>
                        {
                            var countryProps = country.GetType().GetProperties();
                            var idProp = countryProps.FirstOrDefault(p => p.Name == "Id");
                            var nameProp = countryProps.FirstOrDefault(p => p.Name == "Name");
                            var propertiesProp = countryProps.FirstOrDefault(p => p.Name == "Properties");

                            string countryId = idProp?.GetValue(country)?.ToString() ?? "";
                            string countryName = nameProp?.GetValue(country)?.ToString() ?? "";

                            // Properties içinden ek bilgileri al
                            string countryCode = "";
                            string internalCountryId = "";
                            if (propertiesProp?.GetValue(country) is Dictionary<string, object> properties)
                            {
                                if (properties.TryGetValue("countryCode", out var codeValue))
                                {
                                    countryCode = codeValue?.ToString() ?? "";
                                }
                                if (properties.TryGetValue("cId", out var cIdValue))
                                {
                                    internalCountryId = cIdValue?.ToString() ?? "";
                                }
                            }

                            return new
                            {
                                id = countryId,
                                name = countryName,
                                countryId = internalCountryId,
                                code = countryCode
                            };
                        }).Cast<object>().ToList();

                        return countryList;
                    }
                }

                // Fallback olarak null döndür
                return null;
            }
            catch (Exception ex)
            {
                // Hata durumunda null döndür
                System.Diagnostics.Debug.WriteLine($"LocationController'dan ülke verileri alınırken hata: {ex.Message}");
                return null;
            }
        }

        public List<object>? GetCityDataFromLocationApi()
        {
            try
            {
                // LocationController instance oluştur
                var contextFactory = _serviceProvider.GetRequiredService<IUmbracoContextFactory>();
                var locationController = new LocationController(
                    contextFactory,
                    _contentQuery,
                    _variation,
                    _ctxAccessor
                );

                var result = locationController.GetCities(culture: "tr-TR", preview: false);

                if (result is OkObjectResult okResult && okResult.Value != null)
                {
                    // LocationController'dan gelen veriyi dönüştür
                    if (okResult.Value is IEnumerable<object> cities)
                    {
                        var cityList = cities.Select(city =>
                        {
                            var cityProps = city.GetType().GetProperties();
                            var idProp = cityProps.FirstOrDefault(p => p.Name == "Id");
                            var nameProp = cityProps.FirstOrDefault(p => p.Name == "Name");
                            var propertiesProp = cityProps.FirstOrDefault(p => p.Name == "Properties");

                            string cityId = idProp?.GetValue(city)?.ToString() ?? "";
                            string cityName = nameProp?.GetValue(city)?.ToString() ?? "";

                            // Properties içinden cityId veya cityCode gibi ek bilgileri al
                            string cityCode = "";
                            string internalCityId = "";
                            if (propertiesProp?.GetValue(city) is Dictionary<string, object> properties)
                            {
                                if (properties.TryGetValue("cId", out var cIdValue))
                                {
                                    internalCityId = cIdValue?.ToString() ?? "";
                                }
                                if (properties.TryGetValue("cityCode", out var codeValue))
                                {
                                    cityCode = codeValue?.ToString() ?? "";
                                }
                            }

                            return new
                            {
                                id = cityId,
                                name = cityName,
                                cityId = internalCityId, // İlçeler için kullanılacak ID
                                code = cityCode
                            };
                        }).Cast<object>().ToList();

                        return cityList;
                    }
                }

                // Fallback olarak varsayılan şehir listesi
                return null;
            }
            catch (Exception ex)
            {
                // Hata durumunda varsayılan şehir listesi döndür
                System.Diagnostics.Debug.WriteLine($"LocationController'dan şehir verileri alınırken hata: {ex.Message}");
                return null;
            }
        }

        public List<object> GetDistrictDataFromLocationApi(string cityId = null!)
        {
            try
            {
                // Şehir ID'si varsa o şehre ait ilçeleri al
                int? cityIdInt = null;
                if (!string.IsNullOrEmpty(cityId) && int.TryParse(cityId, out var parsedCityId))
                {
                    cityIdInt = parsedCityId;
                }

                var contextFactory = _serviceProvider.GetRequiredService<IUmbracoContextFactory>();
                var locationController = new LocationController(
                    contextFactory,
                    _contentQuery,
                    _variation,
                    _ctxAccessor
                );

                var result = locationController.GetDistricts(cityIdInt, culture: "tr-TR", preview: false);

                if (result is OkObjectResult okResult && okResult.Value != null)
                {
                    if (okResult.Value is IEnumerable<object> districts)
                    {
                        var districtList = districts.Select(district =>
                        {
                            var districtProps = district.GetType().GetProperties();
                            var idProp = districtProps.FirstOrDefault(p => p.Name == "Id");
                            var nameProp = districtProps.FirstOrDefault(p => p.Name == "Name");

                            return new
                            {
                                id = idProp?.GetValue(district)?.ToString() ?? "",
                                name = nameProp?.GetValue(district)?.ToString() ?? ""
                            };
                        }).Cast<object>().ToList();

                        return districtList;
                    }
                }

                return new List<object>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LocationController'dan ilçe verileri alınırken hata: {ex.Message}");
                return new List<object>();
            }
        }

        public object GetDistricts(string cityId, string culture = "tr-TR")
        {
            try
            {
                if (string.IsNullOrEmpty(cityId))
                {
                    return new { error = "Şehir ID'si gereklidir." };
                }

                // Şehir ID'sini integer'a çevir
                if (!int.TryParse(cityId, out var cityIdInt))
                {
                    return new { error = "Geçersiz şehir ID'si." };
                }

                // LocationController instance oluştur
                var contextFactory = _serviceProvider.GetRequiredService<IUmbracoContextFactory>();
                var locationController = new LocationController(
                    contextFactory,
                    _contentQuery,
                    _variation,
                    _ctxAccessor
                );

                var result = locationController.GetDistricts(cityIdInt, culture, false);

                if (result is OkObjectResult okResult && okResult.Value != null)
                {
                    // LocationController'dan gelen veriyi form için uygun hale getir
                    if (okResult.Value is IEnumerable<object> districts)
                    {
                        var districtList = districts.Select(district =>
                        {
                            var districtProps = district.GetType().GetProperties();
                            var idProp = districtProps.FirstOrDefault(p => p.Name == "Id");
                            var nameProp = districtProps.FirstOrDefault(p => p.Name == "Name");

                            return new
                            {
                                id = idProp?.GetValue(district)?.ToString() ?? "",
                                name = nameProp?.GetValue(district)?.ToString() ?? ""
                            };
                        }).Cast<object>().ToList();

                        return districtList;
                    }
                }

                return new List<object>();
            }
            catch (Exception ex)
            {
                return new { error = $"İlçeler alınırken hata oluştu: {ex.Message}" };
            }
        }
    }
}
