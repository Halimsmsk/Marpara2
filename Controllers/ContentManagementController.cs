using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Controllers;
using Umbraco.Cms.Core.Web;
using System.Text.Json;
using System.Linq;

namespace Morpara.Controllers
{
    /// <summary>
    /// Content Management API - Umbraco content oluşturma, güncelleme ve silme işlemleri
    /// </summary>
    public class ContentManagementController : UmbracoApiController
    {
        private readonly ILogger<ContentManagementController> _logger;
        private readonly IContentService _contentService;
        private readonly IContentTypeService _contentTypeService;
        private readonly IUmbracoContextAccessor _umbracoContextAccessor;

        public ContentManagementController(
            ILogger<ContentManagementController> logger,
            IContentService contentService,
            IContentTypeService contentTypeService,
            IUmbracoContextAccessor umbracoContextAccessor)
        {
            _logger = logger;
            _contentService = contentService;
            _contentTypeService = contentTypeService;
            _umbracoContextAccessor = umbracoContextAccessor;
        }

        /// <summary>
        /// Yeni bir sayfa/content oluşturur ve yayınlar
        /// POST /umbraco/api/ContentManagement/CreatePage
        /// </summary>
        [HttpPost]
        public IActionResult CreatePage([FromBody] CreatePageRequest request)
        {
            try
            {
                if (request == null)
                {
                    _logger.LogWarning("[CreatePage] Request body is null");
                    return BadRequest(new { error = "Request body cannot be null" });
                }

                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    _logger.LogWarning("[CreatePage] Page name is empty");
                    return BadRequest(new { error = "Page name is required" });
                }

                _logger.LogInformation("[CreatePage] Creating page: {Name}, Parent: {ParentId}, ContentType: {ContentType}", 
                    request.Name, request.ParentId, request.ContentType);

                // Parent content'i bul
                var parentContent = _contentService.GetById(request.ParentId);
                if (parentContent == null)
                {
                    _logger.LogWarning("[CreatePage] Parent content not found: {ParentId}", request.ParentId);
                    return NotFound(new { error = $"Parent content with ID {request.ParentId} not found" });
                }

                // Content type kontrolü
                var contentTypeAlias = request.ContentType ?? "page";
                var contentType = _contentTypeService.Get(contentTypeAlias);
                if (contentType == null)
                {
                    _logger.LogWarning("[CreatePage] Content type not found: {ContentType}", contentTypeAlias);
                    return BadRequest(new { error = $"Content type '{contentTypeAlias}' not found" });
                }

                // Yeni content oluştur
                var newContent = _contentService.Create(
                    request.Name,
                    parentContent,
                    contentTypeAlias
                );

                _logger.LogInformation("[CreatePage] Content created with ID: {Id}", newContent.Id);

                // Properties'leri set et
                if (request.Properties != null && request.Properties.Count > 0)
                {
                    _logger.LogInformation("[CreatePage] Setting {Count} properties", request.Properties.Count);
                    
                    foreach (var prop in request.Properties)
                    {
                        if (newContent.HasProperty(prop.Key))
                        {
                            try
                            {
                                // JSON object'leri string olarak kaydet
                                var value = prop.Value;
                                if (value != null && (value is JsonElement || value.GetType().IsClass && value.GetType() != typeof(string)))
                                {
                                    value = JsonSerializer.Serialize(prop.Value);
                                }

                                newContent.SetValue(prop.Key, value, request.Culture);
                                _logger.LogDebug("[CreatePage] Property set: {Key} = {Value}", prop.Key, value?.ToString()?.Substring(0, Math.Min(50, value?.ToString()?.Length ?? 0)));
                            }
                            catch (Exception propEx)
                            {
                                _logger.LogWarning(propEx, "[CreatePage] Failed to set property: {Key}", prop.Key);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("[CreatePage] Property not found on content type: {Key}", prop.Key);
                        }
                    }
                }

                // Content'i kaydet
                var saveResult = _contentService.Save(newContent);
                if (!saveResult.Success)
                {
                    var errors = string.Join(", ", saveResult.EventMessages.GetAll().Select(m => m.Message));
                    _logger.LogError("[CreatePage] Failed to save content: {Errors}", errors);
                    return BadRequest(new { error = $"Failed to save content: {errors}" });
                }

                _logger.LogInformation("[CreatePage] Content saved successfully");

                // Content'i yayınla
                var publishResult = _contentService.Publish(newContent, new[] { request.Culture });
                if (!publishResult.Success)
                {
                    _logger.LogError("[CreatePage] Failed to publish content");
                    return BadRequest(new { error = "Failed to publish content" });
                }

                _logger.LogInformation("[CreatePage] Content published successfully. ID: {Id}, Name: {Name}", 
                    newContent.Id, newContent.Name);

                // URL'i al
                string? url = null;
                try
                {
                    var umbracoContext = _umbracoContextAccessor.GetRequiredUmbracoContext();
                    var publishedContent = umbracoContext.Content?.GetById(newContent.Id);
                    url = publishedContent?.Url(request.Culture);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[CreatePage] Failed to get URL for content");
                }

                return Ok(new
                {
                    success = true,
                    id = newContent.Id,
                    key = newContent.Key,
                    name = newContent.Name,
                    contentType = newContent.ContentType.Alias,
                    url = url,
                    culture = request.Culture,
                    message = "Content created and published successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CreatePage] Unexpected error creating content");
                return StatusCode(500, new { error = $"Error creating content: {ex.Message}" });
            }
        }

        /// <summary>
        /// Content'i günceller
        /// PUT /umbraco/api/ContentManagement/UpdatePage/{id}
        /// </summary>
        [HttpPut]
        public IActionResult UpdatePage(int id, [FromBody] UpdatePageRequest request)
        {
            try
            {
                if (request == null)
                {
                    return BadRequest(new { error = "Request body cannot be null" });
                }

                _logger.LogInformation("[UpdatePage] Updating page: {Id}", id);

                var content = _contentService.GetById(id);
                if (content == null)
                {
                    _logger.LogWarning("[UpdatePage] Content not found: {Id}", id);
                    return NotFound(new { error = $"Content with ID {id} not found" });
                }

                // Properties'leri güncelle
                if (request.Properties != null && request.Properties.Count > 0)
                {
                    foreach (var prop in request.Properties)
                    {
                        if (content.HasProperty(prop.Key))
                        {
                            try
                            {
                                var value = prop.Value;
                                if (value != null && (value is JsonElement || value.GetType().IsClass && value.GetType() != typeof(string)))
                                {
                                    value = JsonSerializer.Serialize(prop.Value);
                                }

                                content.SetValue(prop.Key, value, request.Culture);
                            }
                            catch (Exception propEx)
                            {
                                _logger.LogWarning(propEx, "[UpdatePage] Failed to update property: {Key}", prop.Key);
                            }
                        }
                    }
                }

                // İsim güncellemesi
                if (!string.IsNullOrWhiteSpace(request.Name))
                {
                    content.Name = request.Name;
                }

                // Kaydet ve yayınla
                var saveResult = _contentService.Save(content);
                if (!saveResult.Success)
                {
                    var errors = string.Join(", ", saveResult.EventMessages.GetAll().Select(m => m.Message));
                    _logger.LogError("[UpdatePage] Failed to save content: {Errors}", errors);
                    return BadRequest(new { error = $"Failed to save content: {errors}" });
                }

                var publishResult = _contentService.Publish(content, new[] { request.Culture });
                if (!publishResult.Success)
                {
                    _logger.LogError("[UpdatePage] Failed to publish content");
                    return BadRequest(new { error = "Failed to publish content" });
                }

                _logger.LogInformation("[UpdatePage] Content updated successfully. ID: {Id}", id);

                return Ok(new
                {
                    success = true,
                    id = content.Id,
                    name = content.Name,
                    message = "Content updated and published successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[UpdatePage] Unexpected error updating content");
                return StatusCode(500, new { error = $"Error updating content: {ex.Message}" });
            }
        }

        /// <summary>
        /// Mevcut bir sayfayı template olarak kullanarak yeni sayfa oluşturur (deep copy)
        /// POST /umbraco/api/ContentManagement/CopyPage
        /// </summary>
        [HttpPost]
        public IActionResult CopyPage([FromBody] CopyPageRequest request)
        {
            try
            {
                if (request == null)
                {
                    return BadRequest(new { error = "Request body is required" });
                }

                if (request.TemplateId <= 0 && string.IsNullOrWhiteSpace(request.TemplateKey))
                {
                    return BadRequest(new { error = "Either TemplateId or TemplateKey is required" });
                }

                _logger.LogInformation("[CopyPage] Copying from template: {TemplateId}/{TemplateKey}, New name: {Name}, Parent: {ParentId}", 
                    request.TemplateId, request.TemplateKey, request.NewName, request.ParentId);

                var result = CopyPageInternal(request);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CopyPage] Unexpected error copying content");
                return StatusCode(500, new { error = $"Error copying content: {ex.Message}" });
            }
        }

        /// <summary>
        /// Birden fazla sayfayı JSON formatında toplu olarak oluşturur
        /// POST /umbraco/api/ContentManagement/BatchCopyPagesFromJson
        /// </summary>
        [HttpPost]
        public IActionResult BatchCopyPagesFromJson([FromBody] BatchCountryPageRequest request)
        {
            try
            {
                if (request == null || request.Pages == null || request.Pages.Count == 0)
                {
                    return BadRequest(new { error = "Pages array is required and cannot be empty" });
                }

                _logger.LogInformation("[BatchCopyPagesFromJson] Starting batch copy. Total pages: {Count}", request.Pages.Count);

                var results = new List<object>();
                var errors = new List<object>();

                // Her sayfa için templateKey ve parentKey'i set et
                foreach (var page in request.Pages)
                {
                    if (string.IsNullOrWhiteSpace(page.TemplateKey))
                    {
                        page.TemplateKey = request.TemplateKey;
                    }
                    if (string.IsNullOrWhiteSpace(page.ParentKey))
                    {
                        page.ParentKey = request.ParentKey;
                    }

                    try
                    {

                        _logger.LogInformation("[BatchCopyPagesFromJson] Creating page: {URL}", page.URL);
                        
                        var result = CopyPageFromJson(page);
                        
                        if (result is OkObjectResult okResult && okResult.Value != null)
                        {
                            results.Add(okResult.Value);
                        }
                        else if (result is ObjectResult errorResult)
                        {
                            errors.Add(new
                            {
                                page = page.URL ?? page.Keyword,
                                error = errorResult.Value
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[BatchCopyPagesFromJson] Failed to copy page: {URL}", page.URL);
                        errors.Add(new
                        {
                            page = page.URL ?? page.Keyword,
                            error = ex.Message
                        });
                    }
                }

                _logger.LogInformation("[BatchCopyPagesFromJson] Batch copy completed. Success: {Success}, Errors: {Errors}", 
                    results.Count, errors.Count);

                return Ok(new
                {
                    success = true,
                    total = request.Pages.Count,
                    succeeded = results.Count,
                    failed = errors.Count,
                    results = results,
                    errors = errors.Count > 0 ? errors : null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BatchCopyPagesFromJson] Unexpected error in batch copy");
                return StatusCode(500, new { error = $"Error in batch copy: {ex.Message}" });
            }
        }

        /// <summary>
        /// moneyTransferList altına tüm ülkeleri moneyTransfer item'ları olarak ekler
        /// POST /umbraco/api/ContentManagement/PopulateMoneyTransferList
        /// </summary>
        [HttpPost]
        public IActionResult PopulateMoneyTransferList([FromBody] PopulateMoneyTransferRequest request)
        {
            try
            {
                if (request == null || request.Countries == null || request.Countries.Count == 0)
                {
                    return BadRequest(new { error = "Countries array is required and cannot be empty" });
                }

                if (string.IsNullOrWhiteSpace(request.ParentKey))
                {
                    return BadRequest(new { error = "ParentKey (moneyTransferList ID) is required" });
                }

                _logger.LogInformation("[PopulateMoneyTransferList] Starting to populate {Count} countries under parent: {ParentKey}", 
                    request.Countries.Count, request.ParentKey);

                // Parent content'i bul (moneyTransferList)
                var parentContent = _contentService.GetById(Guid.Parse(request.ParentKey));
                if (parentContent == null)
                {
                    return BadRequest(new { error = "Parent moneyTransferList not found" });
                }

                if (parentContent.ContentType.Alias != "moneyTransferList")
                {
                    return BadRequest(new { error = "Parent must be of type 'moneyTransferList'" });
                }

                var results = new List<object>();
                var errors = new List<object>();

                // moneyTransfer content type'ını al
                var moneyTransferContentType = _contentTypeService.Get("moneyTransfer");
                if (moneyTransferContentType == null)
                {
                    return StatusCode(500, new { error = "moneyTransfer content type not found" });
                }

                foreach (var country in request.Countries)
                {
                    try
                    {
                        _logger.LogInformation("[PopulateMoneyTransferList] Creating country: {Country}", country.CountryName);

                        // URL için countryName'i normalize et (Türkçe karakterler, boşluklar vb.)
                        var urlName = country.CountryName.ToLowerInvariant()
                            .Replace(" ", "-")
                            .Replace("ç", "c")
                            .Replace("ğ", "g")
                            .Replace("ı", "i")
                            .Replace("ö", "o")
                            .Replace("ş", "s")
                            .Replace("ü", "u")
                            .Replace("İ", "i");

                        // Yeni moneyTransfer content oluştur
                        var newContent = _contentService.Create(
                            urlName,  // Name - URL-friendly countryName
                            parentContent.Id,     // Parent ID
                            moneyTransferContentType.Alias
                        );

                        // TR culture name set et
                        var contentTypeForCheck = _contentTypeService.Get(moneyTransferContentType.Alias);
                        if (contentTypeForCheck != null && contentTypeForCheck.VariesByCulture())
                        {
                            newContent.SetCultureName(country.CountryName, "tr-TR");
                        }

                        // TR kültürü için properties'leri set et
                        if (newContent.HasProperty("countryName"))
                        {
                            newContent.SetValue("countryName", country.CountryName, "tr-TR", null);
                        }
                        
                        if (newContent.HasProperty("countryIsoCode"))
                        {
                            newContent.SetValue("countryIsoCode", country.IsoCode, "tr-TR", null);
                        }
                        
                        if (newContent.HasProperty("countryMoneyIsoCode"))
                        {
                            newContent.SetValue("countryMoneyIsoCode", country.MoneyCurrency, "tr-TR", null);
                        }

                        // TR için URL name ayarla
                        if (newContent.HasProperty("umbracoUrlName"))
                        {
                            newContent.SetValue("umbracoUrlName", urlName, "tr-TR", null);
                        }

                        // EN kültürü için properties'leri set et (eğer countryNameEn varsa)
                        if (!string.IsNullOrEmpty(country.CountryNameEn))
                        {
                            // EN için URL oluştur
                            var urlNameEn = country.CountryNameEn.ToLowerInvariant()
                                .Replace(" ", "-")
                                .Replace("'", "");

                            // EN culture name set et
                            if (contentTypeForCheck != null && contentTypeForCheck.VariesByCulture())
                            {
                                newContent.SetCultureName(country.CountryNameEn, "en");
                            }

                            if (newContent.HasProperty("countryName"))
                            {
                                newContent.SetValue("countryName", country.CountryNameEn, "en", null);
                            }
                            
                            if (newContent.HasProperty("countryIsoCode"))
                            {
                                newContent.SetValue("countryIsoCode", country.IsoCode, "en", null);
                            }
                            
                            if (newContent.HasProperty("countryMoneyIsoCode"))
                            {
                                newContent.SetValue("countryMoneyIsoCode", country.MoneyCurrency, "en", null);
                            }

                            // EN için URL name ayarla
                            if (newContent.HasProperty("umbracoUrlName"))
                            {
                                newContent.SetValue("umbracoUrlName", urlNameEn, "en", null);
                            }
                        }

                        // Save
                        var saveResult = _contentService.Save(newContent);
                        
                        if (saveResult.Success)
                        {
                            // Publish (isteğe bağlı) - hem TR hem EN için
                            if (request.AutoPublish)
                            {
                                var culturesToPublish = !string.IsNullOrEmpty(country.CountryNameEn) 
                                    ? new[] { "tr-TR", "en" } 
                                    : new[] { "tr-TR" };
                                
                                var publishResult = _contentService.Publish(newContent, culturesToPublish);
                                if (publishResult.Success)
                                {
                                    _logger.LogInformation("[PopulateMoneyTransferList] Created and published: {Country} / {CountryEn} (ID: {Id})", 
                                        country.CountryName, country.CountryNameEn ?? "N/A", newContent.Id);
                                }
                            }

                            results.Add(new
                            {
                                countryName = country.CountryName,
                                countryNameEn = country.CountryNameEn,
                                isoCode = country.IsoCode,
                                contentId = newContent.Id,
                                contentKey = newContent.Key,
                                published = request.AutoPublish
                            });
                        }
                        else
                        {
                            var saveErrors = string.Join(", ", saveResult.EventMessages.GetAll().Select(m => m.Message));
                            errors.Add(new
                            {
                                country = country.CountryName,
                                error = $"Failed to save: {saveErrors}"
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[PopulateMoneyTransferList] Failed to create country: {Country}", country.CountryName);
                        errors.Add(new
                        {
                            country = country.CountryName,
                            error = ex.Message
                        });
                    }
                }

                _logger.LogInformation("[PopulateMoneyTransferList] Completed. Success: {Success}, Errors: {Errors}", 
                    results.Count, errors.Count);

                return Ok(new
                {
                    success = true,
                    total = request.Countries.Count,
                    succeeded = results.Count,
                    failed = errors.Count,
                    results = results,
                    errors = errors.Count > 0 ? errors : null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PopulateMoneyTransferList] Unexpected error");
                return StatusCode(500, new { error = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// JSON formatındaki ülke verisiyle sayfa kopyalar ve içeriği otomatik değiştirir
        /// POST /umbraco/api/ContentManagement/CopyPageFromJson
        /// </summary>
        [HttpPost]
        public IActionResult CopyPageFromJson([FromBody] CountryPageRequest request)
        {
            try
            {
                if (request == null)
                {
                    return BadRequest(new { error = "Request body is required" });
                }

                _logger.LogInformation("[CopyPageFromJson] Creating page: {URL}", request.URL);

                // URL oluştur: Eğer URL boşsa keyword'den oluştur
                var pageUrl = !string.IsNullOrEmpty(request.URL) 
                    ? request.URL 
                    : request.Keyword.ToLowerInvariant()
                        .Replace(" ", "-")
                        .Replace("ç", "c")
                        .Replace("ğ", "g")
                        .Replace("ı", "i")
                        .Replace("ö", "o")
                        .Replace("ş", "s")
                        .Replace("ü", "u");

                _logger.LogInformation("[CopyPageFromJson] Page URL will be: {URL}", pageUrl);

                // URL boş ise hata döndür
                if (string.IsNullOrWhiteSpace(pageUrl))
                {
                    _logger.LogError("[CopyPageFromJson] pageUrl is empty! URL: '{URL}', Keyword: '{Keyword}'", request.URL ?? "NULL", request.Keyword ?? "NULL");
                    return BadRequest(new { error = "URL or Keyword is required to generate page name" });
                }

                // ✅ TemplateId yoksa TemplateKey'den al
                if (request.TemplateId == 0 && !string.IsNullOrWhiteSpace(request.TemplateKey))
                {
                    var templateForId = _contentService.GetById(Guid.Parse(request.TemplateKey));
                    if (templateForId != null)// ✅ TemplateId yüklendi
                    {
                        request.TemplateId = templateForId.Id;
                        _logger.LogInformation("[CopyPageFromJson] TemplateId loaded from TemplateKey: {TemplateId}", request.TemplateId);
                    }
                }

                // CopyPageRequest oluştur
                var copyRequest = new CopyPageRequest
                {
                    TemplateKey = request.TemplateKey,
                    TemplateId = request.TemplateId,  // ✅ Template ID ekle
                    ParentKey = request.ParentKey,
                    NewName = pageUrl,  // URL veya keyword'den oluşturulan değer
                    Culture = "tr-TR",
                    PropertyOverrides = new Dictionary<string, object>
                    {
                        { "title", request.TRTitle },
                        { "searchTitle", request.TRKeywords },  // TRKeywords kullan
                        { "metaDescription", request.TRDescription },
                        { "keyword", request.TRKeywords }  // TRKeywords kullan
                    },
                    BlockGridReplacements = new Dictionary<string, string>()
                };

                // Ana field'ı parse et: ilk \n'e kadar title, sonrası description
                var anaParts = request.Ana.Split(new[] { '\n' }, 2);
                var yeniTitle = anaParts[0].Trim();
                var yeniDescription = anaParts.Length > 1 ? anaParts[1].Trim() : "";

                // heroBanner title - Template JSON'dan: "<p data-pm-slice=\"0 0 []\">Almanya Para Transferi</p>"
                var eskiAnaTitle = "{\\u0022markup\\u0022:\\u0022\\\\u003Cp data-pm-slice=\\\\u00220 0 []\\\\u0022\\\\u003EAlmanya Para Transferi\\\\u003C/p\\\\u003E\\u0022";
                var yeniAnaTitleEscaped = yeniTitle.Replace("'", "\\\\u0027").Replace("ç", "\\\\u00E7").Replace("ğ", "\\\\u011F").Replace("ı", "\\\\u0131").Replace("ö", "\\\\u00F6").Replace("ş", "\\\\u015F").Replace("ü", "\\\\u00FC").Replace("İ", "\\\\u0130").Replace("Ç", "\\\\u00C7").Replace("Ğ", "\\\\u011E").Replace("Ö", "\\\\u00D6").Replace("Ş", "\\\\u015E").Replace("Ü", "\\\\u00DC");
                copyRequest.BlockGridReplacements.Add(eskiAnaTitle, $"{{\\u0022markup\\u0022:\\u0022\\\\u003Cp data-pm-slice=\\\\u00220 0 []\\\\u0022\\\\u003E{yeniAnaTitleEscaped}\\\\u003C/p\\\\u003E\\u0022");
                
                // heroBanner description - Template JSON'dan: "<p data-pm-slice=\"0 0 []\">Morpara ile Almanya'ya para transferi çok kolay!..."
                if (!string.IsNullOrEmpty(yeniDescription))
                {
                    var almanyaDesc = "{\\u0022markup\\u0022:\\u0022\\\\u003Cp data-pm-slice=\\\\u00220 0 []\\\\u0022\\\\u003EMorpara ile Almanya\\\\u0027ya para transferi \\\\u00E7ok kolay! \\\\u0130ster Almanya banka hesaplar\\\\u0131na para g\\\\u00F6nder, istersen al\\\\u0131c\\\\u0131n\\\\u0131n ismine para g\\\\u00F6nder. \\\\u0130kisi de tek uygulamada. \\\\u0130sme para transferini yapmak i\\\\u00E7in MoneyGram i\\\\u015Flemlerinden al\\\\u0131c\\\\u0131n\\\\u0131n ad\\\\u0131na para g\\\\u00F6nderebilir, hesaba para transferi yapmak i\\\\u00E7in de direkt hesaba transferi se\\\\u00E7ebilirsin. \\\\u00DCstelik hesaba para transferinde i\\\\u015Flem \\\\u00FCcreti de yok!\\\\u003C/p\\\\u003E\\u0022";
                    var yeniDescEscaped = yeniDescription
                        .Replace("'", "\\\\u0027")
                        .Replace("ç", "\\\\u00E7")
                        .Replace("ğ", "\\\\u011F")
                        .Replace("ı", "\\\\u0131")
                        .Replace("ö", "\\\\u00F6")
                        .Replace("ş", "\\\\u015F")
                        .Replace("ü", "\\\\u00FC")
                        .Replace("İ", "\\\\u0130")
                        .Replace("Ç", "\\\\u00C7")
                        .Replace("Ğ", "\\\\u011E")
                        .Replace("Ö", "\\\\u00D6")
                        .Replace("Ş", "\\\\u015E")
                        .Replace("Ü", "\\\\u00DC")
                        .Replace("!", "!");
                    copyRequest.BlockGridReplacements.Add(almanyaDesc, $"{{\\u0022markup\\u0022:\\u0022\\\\u003Cp data-pm-slice=\\\\u00220 0 []\\\\u0022\\\\u003E{yeniDescEscaped}\\\\u003C/p\\\\u003E\\u0022");
                }

                // Komponent field'ı parse et: ilk \n title, ikinci \n description
                var komponentParts = request.Komponent.Split(new[] { '\n' }, 2);
                var ucretTitle = komponentParts[0].Trim();
                var ucretDescription = komponentParts.Length > 1 ? komponentParts[1].Trim() : "";

                // basicContentBlock #1 title - Template JSON'dan: "<p>Almanya'ya para transferi işlem ücreti nedir?</p>"
                var eskiUcretTitle = "\\\\u003Cp\\\\u003EAlmanya\\\\u0027ya para transferi i\\\\u015Flem \\\\u00FCcreti nedir?\\\\u003C/p\\\\u003E";
                var ucretTitleEscaped = ucretTitle.Replace("'", "\\\\u0027").Replace("ç", "\\\\u00E7").Replace("ğ", "\\\\u011F").Replace("ı", "\\\\u0131").Replace("ö", "\\\\u00F6").Replace("ş", "\\\\u015F").Replace("ü", "\\\\u00FC").Replace("İ", "\\\\u0130").Replace("Ç", "\\\\u00C7").Replace("Ğ", "\\\\u011E").Replace("Ö", "\\\\u00D6").Replace("Ş", "\\\\u015E").Replace("Ü", "\\\\u00DC");
                copyRequest.BlockGridReplacements.Add(eskiUcretTitle, $"\\\\u003Cp\\\\u003E{ucretTitleEscaped}\\\\u003C/p\\\\u003E");

                // basicContentBlock #1 description - Template JSON'dan: "<p data-pm-slice=\"0 0 []\">Almanya hesaplarına para gönderimde..."
                if (!string.IsNullOrEmpty(ucretDescription))
                {
                    // Template pattern with JSON wrapper and data-pm-slice
                    var eskiUcretDesc = "{\\u0022markup\\u0022:\\u0022\\\\u003Cp data-pm-slice=\\\\u00220 0 []\\\\u0022\\\\u003EAlmanya hesaplar\\\\u0131na para g\\\\u00F6nderimde Morpara mobil uygulamas\\\\u0131 ile i\\\\u015Flem \\\\u00FCcreti \\\\u00F6demezsin. Almanya\\\\u0027ya isme para transferinde ise MoneyGram i\\\\u015Flemleri ile avantajl\\\\u0131 kur ve cazip \\\\u00FCcretler ile transferini tamamlayabilirsin.\\\\u003C/p\\\\u003E\\u0022";
                    
                    // Yeni description escape et
                    var ucretDescEscaped = ucretDescription
                        .Replace("'", "\\\\u0027")
                        .Replace("ç", "\\\\u00E7")
                        .Replace("ğ", "\\\\u011F")
                        .Replace("ı", "\\\\u0131")
                        .Replace("ö", "\\\\u00F6")
                        .Replace("ş", "\\\\u015F")
                        .Replace("ü", "\\\\u00FC")
                        .Replace("İ", "\\\\u0130")
                        .Replace("Ç", "\\\\u00C7")
                        .Replace("Ğ", "\\\\u011E")
                        .Replace("Ö", "\\\\u00D6")
                        .Replace("Ş", "\\\\u015E")
                        .Replace("Ü", "\\\\u00DC");
                    
                    copyRequest.BlockGridReplacements.Add(eskiUcretDesc, $"{{\\u0022markup\\u0022:\\u0022\\\\u003Cp data-pm-slice=\\\\u00220 0 []\\\\u0022\\\\u003E{ucretDescEscaped}\\\\u003C/p\\\\u003E\\u0022");
                }

                // Komponent2 field'ı parse et: ilk \n title, ikinci \n description
                var komponent2Parts = request.Komponent2.Split(new[] { '\n' }, 2);
                var sureTitle = komponent2Parts[0].Trim();
                var sureDescription = komponent2Parts.Length > 1 ? komponent2Parts[1].Trim() : "";

                // basicContentBlock #2 title - Template JSON'dan: "<p>Almanya'ya para transferi süresi nedir?</p>"
                var eskiSureTitle = "\\\\u003Cp\\\\u003EAlmanya\\\\u0027ya para transferi s\\\\u00FCresi nedir?\\\\u003C/p\\\\u003E";
                var sureTitleEscaped = sureTitle.Replace("'", "\\\\u0027").Replace("ç", "\\\\u00E7").Replace("ğ", "\\\\u011F").Replace("ı", "\\\\u0131").Replace("ö", "\\\\u00F6").Replace("ş", "\\\\u015F").Replace("ü", "\\\\u00FC").Replace("İ", "\\\\u0130").Replace("Ç", "\\\\u00C7").Replace("Ğ", "\\\\u011E").Replace("Ö", "\\\\u00D6").Replace("Ş", "\\\\u015E").Replace("Ü", "\\\\u00DC");
                copyRequest.BlockGridReplacements.Add(eskiSureTitle, $"\\\\u003Cp\\\\u003E{sureTitleEscaped}\\\\u003C/p\\\\u003E");

                // basicContentBlock #2 description - Template JSON'dan: "<p data-pm-slice=\"0 0 []\">Almanya'ya para transferi süresi,MoneyGram..."
                if (!string.IsNullOrEmpty(sureDescription))
                {
                    // Template'deki gerçek format: RichText JSON with data-pm-slice
                    var eskiSureDesc = "{\\u0022markup\\u0022:\\u0022\\\\u003Cp data-pm-slice=\\\\u00220 0 []\\\\u0022\\\\u003EAlmanya\\\\u0027ya para transferi s\\\\u00FCresi,MoneyGram ile isme para transferlerinde dakikalar i\\\\u00E7inde g\\\\u00F6nderilmektedir. Almanya banka hesaplar\\\\u0131na para transferinde Morpara\\\\u0027dan transferin an\\\\u0131nda i\\\\u015Fleme al\\\\u0131n\\\\u0131r. Fakat al\\\\u0131c\\\\u0131ya teslim edilmesi kar\\\\u015F\\\\u0131 bankan\\\\u0131n s\\\\u00FCrelerine g\\\\u00F6re de\\\\u011Fi\\\\u015Fiklik g\\\\u00F6stermektedir.\\\\u003C/p\\\\u003E\\u0022";
                    var sureDescEscaped = sureDescription.Replace("'", "\\\\u0027").Replace("ç", "\\\\u00E7").Replace("ğ", "\\\\u011F").Replace("ı", "\\\\u0131").Replace("ö", "\\\\u00F6").Replace("ş", "\\\\u015F").Replace("ü", "\\\\u00FC").Replace("İ", "\\\\u0130").Replace("Ç", "\\\\u00C7").Replace("Ğ", "\\\\u011E").Replace("Ö", "\\\\u00D6").Replace("Ş", "\\\\u015E").Replace("Ü", "\\\\u00DC");
                    copyRequest.BlockGridReplacements.Add(eskiSureDesc, $"{{\\u0022markup\\u0022:\\u0022\\\\u003Cp data-pm-slice=\\\\u00220 0 []\\\\u0022\\\\u003E{sureDescEscaped}\\\\u003C/p\\\\u003E\\u0022");
                }

                // İngilizce versiyonlar (Unicode escaped)
                if (!string.IsNullOrEmpty(request.AnaEN))
                {
                    // Template format: JSON wrapper with data-pm-slice
                    var eskiAnaDescEN = "{\\u0022markup\\u0022:\\u0022\\\\u003Cp data-pm-slice=\\\\u00220 0 []\\\\u0022\\\\u003ESending money to Germany is easy with Morpara! You can transfer money directly to German bank accounts or send it to the recipient\\u0027s name \\u2014 all in one app. For cash pickup, you can use MoneyGram, and for account transfers, you can choose direct bank transfer. Plus, there are no transaction fees when transferring money to a bank account!\\\\u003C/p\\\\u003E\\u0022";
                    var anaDescENEscaped = request.AnaEN.Replace("'", "\\\\u0027").Replace("—", "\\\\u2014");
                    copyRequest.BlockGridReplacements.Add(eskiAnaDescEN, $"{{\\u0022markup\\u0022:\\u0022\\\\u003Cp data-pm-slice=\\\\u00220 0 []\\\\u0022\\\\u003E{anaDescENEscaped}\\\\u003C/p\\\\u003E\\u0022");
                }

                if (!string.IsNullOrEmpty(request.KomponentEN))
                {
                    // Template format: Plain p tag (NO data-pm-slice)
                    var eskiUcretDescEN = "\\\\u003Cp\\\\u003EWith the Morpara mobile app, you won\\u0027t pay any transaction fees when sending money to German accounts. MoneyGram cash pickup to Germany offers competitive rates and attractive fees.\\\\u003C/p\\\\u003E";
                    var ucretDescENEscaped = request.KomponentEN.Replace("'", "\\\\u0027").Replace("—", "\\\\u2014");
                    copyRequest.BlockGridReplacements.Add(eskiUcretDescEN, $"\\\\u003Cp\\\\u003E{ucretDescENEscaped}\\\\u003C/p\\\\u003E");
                }

                if (!string.IsNullOrEmpty(request.Komponent2EN))
                {
                    // Template format: JSON wrapper with data-pm-slice + &nbsp; (two spaces)
                    var eskiSureDescEN = "{\\u0022markup\\u0022:\\u0022\\\\u003Cp data-pm-slice=\\\\u00220 0 []\\\\u0022\\\\u003ESending money to Germany with MoneyGram is fast and reliable \\u2014 transfers are typically completed within just a few minutes.\\u00A0 If you\\u0027re sending money directly to a German bank account, Morpara processes the transfer instantly. Please note that the exact delivery time may vary depending on your recipient\\u0027s bank processing schedule. With Morpara and MoneyGram, you can enjoy a safe, quick, and convenient money transfer to Germany anytime you need.\\\\u003C/p\\\\u003E\\u0022";
                    var sureDescENEscaped = request.Komponent2EN.Replace("'", "\\\\u0027").Replace("—", "\\\\u2014");
                    copyRequest.BlockGridReplacements.Add(eskiSureDescEN, $"{{\\u0022markup\\u0022:\\u0022\\\\u003Cp data-pm-slice=\\\\u00220 0 []\\\\u0022\\\\u003E{sureDescENEscaped}\\\\u003C/p\\\\u003E\\u0022");
                }

                // EN kültürü için de aynı işlemleri yap
                var resultTR = CopyPageInternal(copyRequest);
                
                // TR'den gelen ID'yi al
                int? createdId = null;
                if (resultTR is OkObjectResult okResult && okResult.Value != null)
                {
                    var resultValue = okResult.Value;
                    var idProperty = resultValue.GetType().GetProperty("id");
                    if (idProperty != null)
                    {
                        createdId = idProperty.GetValue(resultValue) as int?;
                    }
                }
                
                // EN için CopyPageRequest oluştur (eğer EN verisi varsa)
                if (createdId.HasValue && !string.IsNullOrEmpty(request.ENTitle))
                {
                    _logger.LogInformation("[CopyPageFromJson] EN kültürü ekleniyor. Content ID: {Id}", createdId.Value);
                    
                    // Template'deki available cultures'ı kontrol et
                    var templateForCultureCheck = _contentService.GetById(Guid.Parse(request.TemplateKey));
                    
                    var availableCultures = templateForCultureCheck?.AvailableCultures.ToList();
                    var enCulture = availableCultures?.FirstOrDefault(c => c.StartsWith("en"));
                    
                    _logger.LogInformation("[CopyPageFromJson] Template available cultures: {Cultures}, EN culture: {EnCulture}", 
                        string.Join(", ", availableCultures ?? new List<string>()), enCulture ?? "NONE");
                    
                    _logger.LogInformation("[CopyPageFromJson] 🔍 ENKeywords: '{ENKeywords}', ENTitle: '{ENTitle}'", 
                        request.ENKeywords ?? "NULL", request.ENTitle ?? "NULL");
                    
                    // EN için URL oluştur - ENKeywords'den
                    var pageUrlEN = !string.IsNullOrEmpty(request.ENKeywords)
                        ? request.ENKeywords.ToLowerInvariant()
                            .Replace(" ", "-")
                            .Replace("ç", "c")
                            .Replace("ğ", "g")
                            .Replace("ı", "i")
                            .Replace("ö", "o")
                            .Replace("ş", "s")
                            .Replace("ü", "u")
                        : pageUrl;  // Fallback to TR URL
                    
                    var copyRequestEN = new CopyPageRequest
                    {
                        ExistingContentId = createdId.Value,  // Mevcut content'i güncelle
                        NewName = pageUrlEN,
                        Culture = enCulture ?? "en-GB",  // Template'deki EN culture'ı kullan
                        TemplateKey = request.TemplateKey,  // ✅ Template Key'i kopyala
                        TemplateId = request.TemplateId,     // ✅ Template ID'yi kopyala
                        PropertyOverrides = new Dictionary<string, object>
                        {
                            { "title", request.ENTitle },
                            { "searchTitle", request.ENKeywords },  // ENKeywords kullan
                            { "metaDescription", request.ENDescription ?? "" },
                            { "keyword", request.ENKeywords }  // ENKeywords kullan
                        },
                        BlockGridReplacements = new Dictionary<string, string>()
                    };
                    
                    _logger.LogInformation("[CopyPageFromJson] 🔍 EN PropertyOverrides - searchTitle: '{SearchTitle}', keyword: '{Keyword}'",
                        request.ENKeywords ?? "NULL", request.ENKeywords ?? "NULL");
                    
                    _logger.LogInformation("[CopyPageFromJson] 🔍 TR values for EN replacement - yeniTitle: '{YeniTitle}', ucretTitle: '{UcretTitle}', sureTitle: '{SureTitle}'",
                        yeniTitle, ucretTitle, sureTitle);
                    
                    // EN Block Grid replacements - template'deki EN değerlerden (Germany) yeni EN değerlere
                    if (!string.IsNullOrEmpty(request.AnaEN))
                    {
                        var anaPartsEN = request.AnaEN.Split(new[] { '\n' }, 2);
                        var yeniTitleEN = anaPartsEN[0].Trim();
                        var yeniDescriptionEN = anaPartsEN.Length > 1 ? anaPartsEN[1].Trim() : "";
                        
                        // Title replacement - Template'deki actual title
                        var eskiTitleEN = "Germany Money Transfer";
                        copyRequestEN.BlockGridReplacements.Add(eskiTitleEN, yeniTitleEN);
                        
                        _logger.LogInformation("[CopyPageFromJson] 🔍 EN Ana Title: '{Old}' → '{New}'", eskiTitleEN, yeniTitleEN);
                        
                        // Description replacement - Template'de Unicode escaped + data-pm-slice
                        if (!string.IsNullOrEmpty(yeniDescriptionEN))
                        {
                            // Template format: Unicode escaped \\u003Cp data-pm-slice=\\u00220 0 []\\u0022
                            var eskiDescEN = "\\u003Cp data-pm-slice=\\u00220 0 []\\u0022\\u003ESending money to Germany is easy with Morpara! You can transfer money directly to German bank accounts or send it to the recipient\u0027s name \u2014 all in one app. For cash pickup, you can use MoneyGram, and for account transfers, you can choose direct bank transfer. Plus, there are no transaction fees when transferring money to a bank account!\\u003C/p\\u003E";
                            var descENEscaped = yeniDescriptionEN.Replace("'", "\\u0027").Replace("—", "\\u2014");
                            var finalDescEN = $"\\u003Cp data-pm-slice=\\u00220 0 []\\u0022\\u003E{descENEscaped}\\u003C/p\\u003E";
                            
                            copyRequestEN.BlockGridReplacements.Add(eskiDescEN, finalDescEN);
                            _logger.LogInformation("[CopyPageFromJson] 🔍 EN Ana Desc (first 80): '{Old}'", eskiDescEN.Substring(0, Math.Min(80, eskiDescEN.Length)));
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(request.KomponentEN))
                    {
                        var komponentPartsEN = request.KomponentEN.Split(new[] { '\n' }, 2);
                        var ucretTitleEN = komponentPartsEN[0].Trim();
                        var ucretDescriptionEN = komponentPartsEN.Length > 1 ? komponentPartsEN[1].Trim() : "";
                        
                        // Title - HTML kullan
                        var eskiUcretTitleEN = "<p>What are the transaction fees for money transfers to Germany?</p>";
                        var finalUcretTitleEN = $"<p>{ucretTitleEN}</p>";
                        
                        copyRequestEN.BlockGridReplacements.Add(eskiUcretTitleEN, finalUcretTitleEN);
                        _logger.LogInformation("[CopyPageFromJson] 🔍 EN Ücret Title: '{Old}'", eskiUcretTitleEN);
                        
                        // Description - Template'deki TAM metin (won't - curly apostrophe)
                        if (!string.IsNullOrEmpty(ucretDescriptionEN))
                        {
                            var eskiUcretDescEN = "<p>With the Morpara mobile app, you won't pay any transaction fees when sending money to German accounts. MoneyGram cash pickup to Germany offers competitive rates and attractive fees.</p>";
                            var finalUcretDescEN = $"<p>{ucretDescriptionEN}</p>";
                            
                            copyRequestEN.BlockGridReplacements.Add(eskiUcretDescEN, finalUcretDescEN);
                            _logger.LogInformation("[CopyPageFromJson] 🔍 EN Ücret Desc (first 80): '{Old}'", eskiUcretDescEN.Substring(0, Math.Min(80, eskiUcretDescEN.Length)));
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(request.Komponent2EN))
                    {
                        var komponent2PartsEN = request.Komponent2EN.Split(new[] { '\n' }, 2);
                        var sureTitleEN = komponent2PartsEN[0].Trim();
                        var sureDescriptionEN = komponent2PartsEN.Length > 1 ? komponent2PartsEN[1].Trim() : "";
                        
                        // Title replacement - Template'de Unicode escaped + data-pm-slice
                        var eskiSureTitleEN = "\\u003Cp data-pm-slice=\\u00220 0 []\\u0022\\u003EWhat is the timeframe for money transfers to Germany?\\u003C/p\\u003E";
                        var titleENEscaped = sureTitleEN.Replace("'", "\\u0027").Replace("—", "\\u2014");
                        var finalSureTitleEN = $"\\u003Cp data-pm-slice=\\u00220 0 []\\u0022\\u003E{titleENEscaped}\\u003C/p\\u003E";
                        copyRequestEN.BlockGridReplacements.Add(eskiSureTitleEN, finalSureTitleEN);
                        _logger.LogInformation("[CopyPageFromJson] 🔍 EN Süre Title: '{Old}'", eskiSureTitleEN);
                        
                        // Description replacement - Template'de plain HTML format
                        if (!string.IsNullOrEmpty(sureDescriptionEN))
                        {
                            // Template format: \\u0026nbsp; (not \\u00A0) and \\u2019 (curly apostrophe)
                            var eskiSureDescEN = "\\u003Cp data-pm-slice=\\u00220 0 []\\u0022\\u003ESending money to Germany with MoneyGram is fast and reliable — transfers are typically completed within just a few minutes.\\u0026nbsp; If you\\u2019re sending money directly to a German bank account, Morpara processes the transfer instantly. Please note that the exact delivery time may vary depending on your recipient\\u2019s bank processing schedule. With Morpara and MoneyGram, you can enjoy a safe, quick, and convenient money transfer to Germany anytime you need.\\u003C/p\\u003E";
                            var descENEscaped = sureDescriptionEN.Replace("'", "\\u2019").Replace("—", "\\u2014");
                            var finalSureDescEN = $"\\u003Cp data-pm-slice=\\u00220 0 []\\u0022\\u003E{descENEscaped}\\u003C/p\\u003E";
                            copyRequestEN.BlockGridReplacements.Add(eskiSureDescEN, finalSureDescEN);
                            _logger.LogInformation("[CopyPageFromJson] 🔍 EN Süre Desc aranan (first 100): '{Old}'", eskiSureDescEN.Substring(0, Math.Min(100, eskiSureDescEN.Length)));
                        }
                    }

                    CopyPageInternal(copyRequestEN);
                    _logger.LogInformation("[CopyPage] EN kültürü güncellendi");
                }
                
                return resultTR;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CopyPageFromJson] Unexpected error");
                return StatusCode(500, new { error = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Birden fazla sayfayı toplu olarak kopyalar
        /// POST /umbraco/api/ContentManagement/BatchCopyPages
        /// </summary>
        [HttpPost]
        public IActionResult BatchCopyPages([FromBody] BatchCopyRequest request)
        {
            try
            {
                if (request == null || request.Pages == null || request.Pages.Count == 0)
                {
                    return BadRequest(new { error = "Pages array is required and cannot be empty" });
                }

                _logger.LogInformation("[BatchCopyPages] Starting batch copy. Total pages: {Count}", request.Pages.Count);

                var results = new List<object>();
                var errors = new List<object>();

                foreach (var pageRequest in request.Pages)
                {
                    try
                    {
                        var result = CopyPageInternal(pageRequest);
                        
                        if (result is OkObjectResult okResult && okResult.Value != null)
                        {
                            results.Add(okResult.Value);
                        }
                        else if (result is ObjectResult errorResult)
                        {
                            errors.Add(new
                            {
                                page = pageRequest.NewName,
                                error = errorResult.Value
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[BatchCopyPages] Failed to copy page: {Name}", pageRequest.NewName);
                        errors.Add(new
                        {
                            page = pageRequest.NewName,
                            error = ex.Message
                        });
                    }
                }

                _logger.LogInformation("[BatchCopyPages] Batch copy completed. Success: {Success}, Errors: {Errors}", 
                    results.Count, errors.Count);

                return Ok(new
                {
                    success = true,
                    total = request.Pages.Count,
                    succeeded = results.Count,
                    failed = errors.Count,
                    results = results,
                    errors = errors.Count > 0 ? errors : null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BatchCopyPages] Unexpected error in batch copy");
                return StatusCode(500, new { error = $"Error in batch copy: {ex.Message}" });
            }
        }

        private IActionResult CopyPageInternal(CopyPageRequest request)
        {
            try
            {
                if (request == null)
                {
                    return BadRequest(new { error = "Request is required" });
                }

                // Template content'i oku (hem TR hem EN için Block Grid replacements için gerekli)
                Umbraco.Cms.Core.Models.IContent? templateContent = null;
                if (!string.IsNullOrWhiteSpace(request.TemplateKey))
                {
                    templateContent = _contentService.GetById(Guid.Parse(request.TemplateKey));
                }
                else if (request.TemplateId > 0)
                {
                    templateContent = _contentService.GetById(request.TemplateId);
                }
                
                _logger.LogInformation("[CopyPage] 🔍 Template loaded: {IsNull}, TemplateKey: {Key}, TemplateId: {Id}", 
                    templateContent == null ? "NULL" : "OK", request.TemplateKey, request.TemplateId);

                // Eğer ExistingContentId varsa, mevcut content'i güncelle (EN kültürü için)
                if (request.ExistingContentId.HasValue)
                {
                    var existingContent = _contentService.GetById(request.ExistingContentId.Value);
                    if (existingContent == null)
                    {
                        return BadRequest(new { error = "Existing content not found" });
                    }
                    
                    _logger.LogInformation("[CopyPage] Updating existing content {Id} for culture {Culture}", request.ExistingContentId.Value, request.Culture);
                    
                    // EN kültürü için name set et
                    var existingContentType = _contentTypeService.Get(existingContent.ContentType.Alias);
                    if (existingContentType != null && existingContentType.VariesByCulture() && !string.IsNullOrWhiteSpace(request.Culture))
                    {
                        existingContent.SetCultureName(request.NewName, request.Culture);
                    }
                    
                    // Property overrides uygula
                    if (request.PropertyOverrides != null)
                    {
                        foreach (var prop in request.PropertyOverrides)
                        {
                            if (existingContent.HasProperty(prop.Key))
                            {
                                existingContent.SetValue(prop.Key, prop.Value, request.Culture);
                            }
                        }
                    }
                    
                    // Block Grid replacements uygula
                    if (request.BlockGridReplacements != null && request.BlockGridReplacements.Count > 0)
                    {
                        // EN için EN culture'dan template datasını al (çünkü template'de hala Almanya var)
                        var sourceHomeCulture = request.Culture;
                        
                        _logger.LogInformation("[CopyPage] 🔍 EN Block Grid - templateContent: {IsNull}, Culture: {Culture}", 
                            templateContent == null ? "NULL" : "OK", sourceHomeCulture);
                        
                        var homeProperty = templateContent?.GetValue<string>("home", sourceHomeCulture);
                        
                        _logger.LogInformation("[CopyPage] 🔍 Block Grid source culture: {SourceCulture}, target: {TargetCulture}, homeProperty length: {Length}", 
                            sourceHomeCulture, request.Culture, homeProperty?.Length ?? 0);
                        
                        if (!string.IsNullOrEmpty(homeProperty))
                        {
                            // EN template sample'ını logla
                            var germanySampleIdx = homeProperty.IndexOf("Germany");
                            if (germanySampleIdx >= 0)
                            {
                                var sampleLen = Math.Min(1000, homeProperty.Length - germanySampleIdx);
                                _logger.LogInformation("[CopyPage] 🔍 EN Template sample (Germany bölümü): {Sample}", homeProperty.Substring(germanySampleIdx, sampleLen));
                            }
                            
                            var modifiedValue = homeProperty;
                            int successCount = 0;
                            
                            // Tüm replacements'ları işle (titles ve descriptions)
                            _logger.LogInformation("[CopyPage] 🔍 EN: Processing {Count} replacements", request.BlockGridReplacements.Count);
                            
                            // Direkt string replacement - JSON escape'leri ile çalışır
                            foreach (var replacement in request.BlockGridReplacements)
                            {
                                var oldPreview = replacement.Key.Length > 80 ? replacement.Key.Substring(0, 80) + "..." : replacement.Key;
                                
                                // Hem normal hem de escaped versiyonları dene
                                var wasReplaced = false;
                                
                                // 1. Normal HTML replace (eğer JSON içinde unescaped varsa)
                                if (modifiedValue.Contains(replacement.Key))
                                {
                                    modifiedValue = modifiedValue.Replace(replacement.Key, replacement.Value);
                                    successCount++;
                                    wasReplaced = true;
                                    _logger.LogInformation("[CopyPage] ✅ EN Replaced (plain): {Key}", oldPreview);
                                }
                                
                                // Eğer bulunamadıysa, template'de search snippet göster
                                if (!wasReplaced && replacement.Key.Contains("Sending money to Germany with MoneyGram"))
                                {
                                    var searchText = "Sending money to Germany with MoneyGram";
                                    var idx = modifiedValue.IndexOf(searchText, StringComparison.Ordinal);
                                    if (idx >= 0)
                                    {
                                        var start = Math.Max(0, idx - 50);
                                        var len = Math.Min(300, modifiedValue.Length - start);
                                        var snippet = modifiedValue.Substring(start, len);
                                        _logger.LogWarning("[CopyPage] 🔍 Template snippet (300 chars): {Snippet}", snippet);
                                    }
                                }
                                
                                // 2. Escaped HTML replace - Template'deki format: \\u003Cp (double backslash!)
                                if (!wasReplaced)
                                {
                                    var escapedOld = replacement.Key
                                        .Replace("\\", "\\\\")      // Backslash'leri koru
                                        .Replace("<", "\\\\u003C")  // <p> → \\u003Cp
                                        .Replace(">", "\\\\u003E")  // </p> → \\u003C/p\\u003E
                                        .Replace("'", "\\\\u2019")  // Smart quote (right)
                                        .Replace("'", "\\\\u2018")  // Smart quote (left)
                                        .Replace("—", "\\\\u2014"); // Em dash
                                        // NOTE: ! ve ? escape edilmiyor template'de
                                        
                                    var escapedNew = replacement.Value
                                        .Replace("\\", "\\\\")
                                        .Replace("<", "\\\\u003C")
                                        .Replace(">", "\\\\u003E")
                                        .Replace("'", "\\\\u2019")
                                        .Replace("'", "\\\\u2018")
                                        .Replace("—", "\\\\u2014");
                                    
                                    if (modifiedValue.Contains(escapedOld))
                                    {
                                        modifiedValue = modifiedValue.Replace(escapedOld, escapedNew);
                                        successCount++;
                                        wasReplaced = true;
                                        _logger.LogInformation("[CopyPage] ✅ EN Replaced (escaped): {Key}", oldPreview);
                                    }
                                }
                                
                                if (!wasReplaced)
                                {
                                    _logger.LogWarning("[CopyPage] ❌ EN NOT FOUND: {Key}", oldPreview);
                                    
                                    // Debug: İlk 50 karakteri arayalım
                                    if (replacement.Key.Length > 50)
                                    {
                                        var searchStr = replacement.Key.Substring(0, 50);
                                        if (homeProperty.Contains(searchStr))
                                        {
                                            var idx = homeProperty.IndexOf(searchStr);
                                            var contextStart = Math.Max(0, idx - 20);
                                            var contextLen = Math.Min(200, homeProperty.Length - contextStart);
                                            _logger.LogWarning("[CopyPage] 🔎 Found partial match at index {Index}: {Context}", idx, homeProperty.Substring(contextStart, contextLen));
                                        }
                                    }
                                }
                            }
                            
                            existingContent.SetValue("home", modifiedValue, request.Culture);
                            _logger.LogInformation("[CopyPage] ✅ EN: {Success}/{Total} replacement başarılı", successCount, request.BlockGridReplacements.Count);
                        }
                    }
                    
                    // Kaydet ve publish et
                    var existingSaveResult = _contentService.Save(existingContent);
                    var existingPublishResult = _contentService.Publish(existingContent, new[] { request.Culture });
                    
                    _logger.LogInformation("[CopyPage] EN kültürü güncellendi: {Id}", existingContent.Id);
                    
                    return Ok(new
                    {
                        success = true,
                        id = existingContent.Id,
                        key = existingContent.Key,
                        name = existingContent.Name,
                        culture = request.Culture
                    });
                }

                if (request.TemplateId <= 0 && string.IsNullOrWhiteSpace(request.TemplateKey))
                {
                    return BadRequest(new { error = "Either TemplateId or TemplateKey is required" });
                }

                _logger.LogInformation("[CopyPage] Copying from template: {TemplateId}/{TemplateKey}, New name: '{Name}', Parent: {ParentId}", 
                    request.TemplateId, request.TemplateKey, request.NewName ?? "NULL", request.ParentId);

                if (string.IsNullOrWhiteSpace(request.NewName))
                {
                    _logger.LogError("[CopyPage] NewName is empty or whitespace! Value: '{Value}', Length: {Length}", 
                        request.NewName ?? "NULL", request.NewName?.Length ?? 0);
                    return BadRequest(new { error = "NewName is required" });
                }

                if (templateContent == null)
                {
                    return NotFound(new { error = $"Template content not found" });
                }

                // Parent belirle
                Umbraco.Cms.Core.Models.IContent? parentContent = null;
                
                if (!string.IsNullOrWhiteSpace(request.ParentKey))
                {
                    parentContent = _contentService.GetById(Guid.Parse(request.ParentKey));
                }
                else if (request.ParentId.HasValue)
                {
                    parentContent = _contentService.GetById(request.ParentId.Value);
                }
                else
                {
                    parentContent = _contentService.GetParent(templateContent);
                }

                if (parentContent == null)
                {
                    return BadRequest(new { error = "Parent content not found" });
                }

                // Yeni isimle Create oluştur (Copy yerine - böylece URL doğru oluşur)
                // NOT: request.NewName zaten validation'dan geçti, direkt kullan
                var newContent = _contentService.Create(
                    request.NewName,
                    parentContent,
                    templateContent.ContentType.Alias
                );

                if (newContent == null)
                {
                    return BadRequest(new { error = "Failed to create content" });
                }

                _logger.LogInformation("[CopyPage] Content created with name: {Name}", request.NewName);
                
                // Variant content type ise culture name'i de set et
                var contentType = _contentTypeService.Get(templateContent.ContentType.Alias);
                if (contentType != null && contentType.VariesByCulture() && !string.IsNullOrWhiteSpace(request.Culture))
                {
                    try
                    {
                        newContent.SetCultureName(request.NewName, request.Culture);
                        _logger.LogInformation("[CopyPage] Culture name set - Culture: {Culture}, Name: {Name}", request.Culture, request.NewName);
                    }
                    catch (Exception cultureEx)
                    {
                        _logger.LogWarning(cultureEx, "[CopyPage] Culture name set edilemedi, devam ediliyor");
                    }
                }

                // Template'den tüm property'leri kopyala
                foreach (var property in templateContent.Properties)
                {
                    if (newContent.HasProperty(property.Alias))
                    {
                        try
                        {
                            var value = templateContent.GetValue(property.Alias, request.Culture);
                            newContent.SetValue(property.Alias, value, request.Culture);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[CopyPage] Failed to copy property: {Alias}", property.Alias);
                        }
                    }
                }

                _logger.LogInformation("[CopyPage] Properties copied from template");

                // Block Grid içindeki text'leri değiştir (find & replace) - TEMPLATE'TEN AL!
                if (request.BlockGridReplacements != null && request.BlockGridReplacements.Count > 0)
                {
                    try
                    {
                        // Template'teki home property'sini kullan
                        var templateHomeProperty = templateContent.GetValue<string>("home", request.Culture);
                        
                        if (!string.IsNullOrEmpty(templateHomeProperty))
                        {
                            _logger.LogInformation("[CopyPage] 🔍 Block Grid replacements başlıyor: {Count} adet", request.BlockGridReplacements.Count);
                            _logger.LogInformation("[CopyPage] 🔍 Template home property length: {Length}", templateHomeProperty.Length);
                            
                            // Template'den sample paragraf log'la
                            var sampleStart = templateHomeProperty.IndexOf("Almanya");
                            if (sampleStart > 0)
                            {
                                var sampleLength = Math.Min(1000, templateHomeProperty.Length - sampleStart);
                                var sample = templateHomeProperty.Substring(sampleStart, sampleLength);
                                _logger.LogInformation("[CopyPage] 🔍 Template sample (Almanya bölümü): {Sample}", sample);
                            }
                            
                            // Morpara kelimesini ara
                            var morparaIdx = templateHomeProperty.IndexOf("Morpara");
                            if (morparaIdx >= 0)
                            {
                                var morparaSample = templateHomeProperty.Substring(morparaIdx, Math.Min(500, templateHomeProperty.Length - morparaIdx));
                                _logger.LogInformation("[CopyPage] 🔍 Template'de Morpara bölümü: {Sample}", morparaSample);
                            }
                            else
                            {
                                _logger.LogWarning("[CopyPage] ⚠️ Template'de 'Morpara' kelimesi BULUNAMADI!");
                            }
                            
                            // Süre description (s\\u00FCresi) bölümünü ara
                            var sureIdx = templateHomeProperty.IndexOf("s\\\\u00FCresi nedir");
                            if (sureIdx >= 0)
                            {
                                var sureSample = templateHomeProperty.Substring(sureIdx, Math.Min(1500, templateHomeProperty.Length - sureIdx));
                                _logger.LogInformation("[CopyPage] 🔍 Template'de Süre bölümü (1500 char): {Sample}", sureSample);
                            }
                            else
                            {
                                _logger.LogWarning("[CopyPage] ⚠️ Template'de 'süresi nedir' bulunamadı!");
                            }
                            
                            // Tüm aranan pattern'ları logla
                            _logger.LogInformation("[CopyPage] 🔍 Aranan pattern'lar:");
                            foreach (var kvp in request.BlockGridReplacements)
                            {
                                var keyPreview = kvp.Key.Length > 120 ? kvp.Key.Substring(0, 120) + "..." : kvp.Key;
                                var found = templateHomeProperty.Contains(kvp.Key);
                                _logger.LogInformation("[CopyPage] {Status} Pattern: {Pattern}", found ? "✅" : "❌", keyPreview);
                                
                                // Eğer bulunamazsa, benzer metinleri ara
                                if (!found && kvp.Key.Contains("Almanya"))
                                {
                                    var searchTerm = "Almanya";
                                    var idx = templateHomeProperty.IndexOf(searchTerm);
                                    if (idx >= 0)
                                    {
                                        var context = templateHomeProperty.Substring(Math.Max(0, idx - 50), Math.Min(200, templateHomeProperty.Length - Math.Max(0, idx - 50)));
                                        _logger.LogInformation("[CopyPage] 📍 Template'de 'Almanya' context: {Context}", context);
                                    }
                                }
                            }
                            
                            var modifiedValue = templateHomeProperty;
                            int successCount = 0;
                            
                            foreach (var replacement in request.BlockGridReplacements)
                            {
                                var oldPreview = replacement.Key.Length > 80 ? replacement.Key.Substring(0, 80) + "..." : replacement.Key;
                                var newPreview = replacement.Value.Length > 80 ? replacement.Value.Substring(0, 80) + "..." : replacement.Value;
                                
                                _logger.LogInformation("[CopyPage] 🔄 Değiştiriliyor: '{Old}' → '{New}'", oldPreview, newPreview);
                                
                                // 1. Normal string replacement dene
                                if (modifiedValue.Contains(replacement.Key))
                                {
                                    var beforeCount = modifiedValue.Length;
                                    modifiedValue = modifiedValue.Replace(replacement.Key, replacement.Value);
                                    var afterCount = modifiedValue.Length;
                                    successCount++;
                                    _logger.LogInformation("[CopyPage] ✅ Normal string ile değiştirildi (length: {Before}→{After})", beforeCount, afterCount);
                                    continue;
                                }
                                
                                // 2. Escape edilmiş JSON versiyonunu dene
                                var escapedKey = System.Text.Json.JsonSerializer.Serialize(replacement.Key);
                                var escapedValue = System.Text.Json.JsonSerializer.Serialize(replacement.Value);
                                
                                // Tırnak işaretlerini kaldır
                                if (escapedKey.StartsWith("\"") && escapedKey.EndsWith("\""))
                                    escapedKey = escapedKey.Substring(1, escapedKey.Length - 2);
                                if (escapedValue.StartsWith("\"") && escapedValue.EndsWith("\""))
                                    escapedValue = escapedValue.Substring(1, escapedValue.Length - 2);
                                
                                if (modifiedValue.Contains(escapedKey))
                                {
                                    var beforeCount = modifiedValue.Length;
                                    modifiedValue = modifiedValue.Replace(escapedKey, escapedValue);
                                    var afterCount = modifiedValue.Length;
                                    successCount++;
                                    _logger.LogInformation("[CopyPage] ✅ Escaped string ile değiştirildi (length: {Before}→{After})", beforeCount, afterCount);
                                    continue;
                                }
                                
                                // 3. Template'den extract edilmiş yakın eşleşmeleri bul
                                var similarMatches = FindSimilarText(templateHomeProperty, replacement.Key, 0.7);
                                if (similarMatches.Count > 0)
                                {
                                    _logger.LogWarning("[CopyPage] ❌ TAM EŞLEŞMEolmadı! Benzer metinler:");
                                    foreach (var match in similarMatches.Take(3))
                                    {
                                        var matchPreview = match.Length > 100 ? match.Substring(0, 100) + "..." : match;
                                        _logger.LogWarning("[CopyPage]     • {Match}", matchPreview);
                                    }
                                }
                                else
                                {
                                    _logger.LogWarning("[CopyPage] ❌ BULUNAMADI! Aranan (escaped): '{Escaped}'", 
                                        escapedKey.Length > 80 ? escapedKey.Substring(0, 80) + "..." : escapedKey);
                                }
                            }
                            
                            newContent.SetValue("home", modifiedValue, request.Culture);
                            _logger.LogInformation("[CopyPage] ✅ Toplam {Success}/{Total} replacement başarılı", successCount, request.BlockGridReplacements.Count);
                        }
                        else
                        {
                            _logger.LogError("[CopyPage] ❌ Template home property bulunamadı veya boş!");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[CopyPage] ❌ Block grid replacement hatası");
                    }
                }

                // Property overrides uygula
                if (request.PropertyOverrides != null && request.PropertyOverrides.Count > 0)
                {
                    foreach (var prop in request.PropertyOverrides)
                    {
                        if (newContent.HasProperty(prop.Key))
                        {
                            try
                            {
                                var value = prop.Value;
                                
                                // String array ise JSON serialize et (MultipleTextstring için)
                                if (value is string[] strArray)
                                {
                                    var jsonOptions = new JsonSerializerOptions
                                    {
                                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                                        WriteIndented = false
                                    };
                                    newContent.SetValue(prop.Key, JsonSerializer.Serialize(strArray, jsonOptions), request.Culture);
                                }
                                // String ise direkt kullan (Unicode escape'ten kaçın)
                                else if (value is string strValue)
                                {
                                    newContent.SetValue(prop.Key, strValue, request.Culture);
                                }
                                // JSON object ise serialize et
                                else if (value != null && (value is JsonElement || (value.GetType().IsClass && value.GetType() != typeof(string))))
                                {
                                    var jsonOptions = new JsonSerializerOptions
                                    {
                                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                                        WriteIndented = false
                                    };
                                    newContent.SetValue(prop.Key, JsonSerializer.Serialize(value, jsonOptions), request.Culture);
                                }
                                else
                                {
                                    newContent.SetValue(prop.Key, value, request.Culture);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "[CopyPage] Failed to override property: {Key}", prop.Key);
                            }
                        }
                    }
                }

                // Kaydet ve yayınla
                var saveResult = _contentService.Save(newContent);
                if (!saveResult.Success)
                {
                    var errors = string.Join(", ", saveResult.EventMessages.GetAll().Select(m => m.Message));
                    _logger.LogError("[CopyPage] Failed to save copied content: {Errors}", errors);
                    return BadRequest(new { error = $"Failed to save content: {errors}" });
                }

                var publishResult = _contentService.Publish(newContent, new[] { request.Culture });
                if (!publishResult.Success)
                {
                    _logger.LogError("[CopyPage] Failed to publish copied content");
                    return BadRequest(new { error = "Failed to publish content" });
                }

                _logger.LogInformation("[CopyPage] Content copied and published. New ID: {Id}, Name: {Name}", 
                    newContent.Id, newContent.Name);

                // URL'i al
                string? url = null;
                try
                {
                    var umbracoContext = _umbracoContextAccessor.GetRequiredUmbracoContext();
                    var publishedContent = umbracoContext.Content?.GetById(newContent.Id);
                    url = publishedContent?.Url(request.Culture);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[CopyPage] Failed to get URL");
                }

                return Ok(new
                {
                    success = true,
                    id = newContent.Id,
                    key = newContent.Key,
                    name = newContent.Name,
                    contentType = newContent.ContentType.Alias,
                    url = url,
                    parentId = parentContent?.Id,
                    parentKey = parentContent?.Key,
                    templateId = request.TemplateId > 0 ? request.TemplateId : (int?)null,
                    templateKey = request.TemplateKey,
                    message = "Content copied and published successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CopyPage] Unexpected error copying content");
                return StatusCode(500, new { error = $"Error copying content: {ex.Message}" });
            }
        }

        /// <summary>
        /// Content'i siler
        /// DELETE /umbraco/api/ContentManagement/DeletePage/{id}
        /// </summary>
        [HttpDelete]
        public IActionResult DeletePage(int id)
        {
            try
            {
                _logger.LogInformation("[DeletePage] Deleting page: {Id}", id);

                var content = _contentService.GetById(id);
                if (content == null)
                {
                    _logger.LogWarning("[DeletePage] Content not found: {Id}", id);
                    return NotFound(new { error = $"Content with ID {id} not found" });
                }

                var result = _contentService.Delete(content);
                if (!result.Success)
                {
                    var errors = string.Join(", ", result.EventMessages.GetAll().Select(m => m.Message));
                    _logger.LogError("[DeletePage] Failed to delete content: {Errors}", errors);
                    return BadRequest(new { error = $"Failed to delete content: {errors}" });
                }

                _logger.LogInformation("[DeletePage] Content deleted successfully. ID: {Id}", id);

                return Ok(new
                {
                    success = true,
                    id = id,
                    message = "Content deleted successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DeletePage] Unexpected error deleting content");
                return StatusCode(500, new { error = $"Error deleting content: {ex.Message}" });
            }
        }
        
        #region Helper Methods
        
        /// <summary>
        /// Template'den tüm HTML paragraf metinlerini extract eder
        /// </summary>
        private Dictionary<string, string> ExtractTemplateTexts(string html)
        {
            var result = new Dictionary<string, string>();
            
            try
            {
                // Escaped HTML paragraflarını bul: \\u003Cp\\u003E...\\u003C/p\\u003E
                var pattern = @"\\u003Cp[^>]*\\u003E(.*?)\\u003C/p\\u003E";
                var regex = new System.Text.RegularExpressions.Regex(pattern);
                var matches = regex.Matches(html);
                
                int index = 0;
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    if (match.Success && match.Groups.Count > 1)
                    {
                        var content = match.Groups[1].Value;
                        // Boş veya çok kısa paragrafları atla
                        if (!string.IsNullOrWhiteSpace(content) && content.Length > 10)
                        {
                            result[$"Para{index}"] = match.Value; // Tam paragrafı sakla (tag'lerle birlikte)
                            index++;
                        }
                    }
                }
                
                // Normal HTML paragraflarını da bul: <p>...</p>
                var normalPattern = @"<p[^>]*>(.*?)</p>";
                var normalRegex = new System.Text.RegularExpressions.Regex(normalPattern);
                var normalMatches = normalRegex.Matches(html);
                
                foreach (System.Text.RegularExpressions.Match match in normalMatches)
                {
                    if (match.Success && match.Groups.Count > 1)
                    {
                        var content = match.Groups[1].Value;
                        if (!string.IsNullOrWhiteSpace(content) && content.Length > 10)
                        {
                            result[$"NormalPara{index}"] = match.Value;
                            index++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ExtractTemplateTexts] Regex hatası");
            }
            
            return result;
        }
        
        /// <summary>
        /// Template içinde aranan metne benzer metinleri bulur (fuzzy match)
        /// </summary>
        private List<string> FindSimilarText(string haystack, string needle, double threshold = 0.7)
        {
            var results = new List<string>();
            
            try
            {
                // Uzun metinleri chunk'lara böl
                var chunkSize = Math.Max(needle.Length, 100);
                var overlap = chunkSize / 4;
                
                for (int i = 0; i < haystack.Length - chunkSize; i += chunkSize - overlap)
                {
                    var chunk = haystack.Substring(i, Math.Min(chunkSize, haystack.Length - i));
                    
                    // Levenshtein benzerliği hesapla
                    var similarity = CalculateSimilarity(needle, chunk);
                    
                    if (similarity >= threshold)
                    {
                        results.Add(chunk);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FindSimilarText] Hata");
            }
            
            return results.OrderByDescending(r => CalculateSimilarity(needle, r)).ToList();
        }
        
        /// <summary>
        /// İki string arasındaki benzerlik oranını hesaplar (0.0 - 1.0)
        /// </summary>
        private double CalculateSimilarity(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
                return 0.0;
            
            var maxLen = Math.Max(s1.Length, s2.Length);
            if (maxLen == 0)
                return 1.0;
            
            var distance = LevenshteinDistance(s1, s2);
            return 1.0 - ((double)distance / maxLen);
        }
        
        /// <summary>
        /// Levenshtein distance hesaplar
        /// </summary>
        private int LevenshteinDistance(string s1, string s2)
        {
            var len1 = s1.Length;
            var len2 = s2.Length;
            var matrix = new int[len1 + 1, len2 + 1];
            
            if (len1 == 0) return len2;
            if (len2 == 0) return len1;
            
            for (int i = 0; i <= len1; i++)
                matrix[i, 0] = i;
            for (int j = 0; j <= len2; j++)
                matrix[0, j] = j;
            
            for (int i = 1; i <= len1; i++)
            {
                for (int j = 1; j <= len2; j++)
                {
                    var cost = (s2[j - 1] == s1[i - 1]) ? 0 : 1;
                    matrix[i, j] = Math.Min(
                        Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost);
                }
            }
            
            return matrix[len1, len2];
        }
        
        #endregion
    }

    /// <summary>
    /// Create Page Request Model
    /// </summary>
    public class CreatePageRequest
    {
        /// <summary>Parent content ID</summary>
        public int ParentId { get; set; }

        /// <summary>Page name</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Content type alias (default: "page")</summary>
        public string? ContentType { get; set; }

        /// <summary>Culture code (default: "tr-TR")</summary>
        public string Culture { get; set; } = "tr-TR";

        /// <summary>Content properties (key-value pairs)</summary>
        public Dictionary<string, object>? Properties { get; set; }
    }

    /// <summary>
    /// Update Page Request Model
    /// </summary>
    public class UpdatePageRequest
    {
        /// <summary>New page name (optional)</summary>
        public string? Name { get; set; }

        /// <summary>Culture code (default: "tr-TR")</summary>
        public string Culture { get; set; } = "tr-TR";

        /// <summary>Content properties to update (key-value pairs)</summary>
        public Dictionary<string, object>? Properties { get; set; }
    }

    /// <summary>
    /// Copy Page Request Model
    /// </summary>
    public class CopyPageRequest
    {
        /// <summary>Template content ID to copy from (use either TemplateId or TemplateKey)</summary>
        public int TemplateId { get; set; }

        /// <summary>Template content Key (GUID) to copy from (alternative to TemplateId)</summary>
        public string? TemplateKey { get; set; }

        /// <summary>New page name</summary>
        public string NewName { get; set; } = string.Empty;

        /// <summary>Parent content ID (use either ParentId or ParentKey, optional)</summary>
        public int? ParentId { get; set; }

        /// <summary>Parent content Key (GUID) (alternative to ParentId, optional)</summary>
        public string? ParentKey { get; set; }

        /// <summary>Existing content ID to update (for EN culture update)</summary>
        public int? ExistingContentId { get; set; }

        /// <summary>Culture code (default: "tr-TR")</summary>
        public string Culture { get; set; } = "tr-TR";

        /// <summary>Include descendants (default: false)</summary>
        public bool IncludeDescendants { get; set; } = false;

        /// <summary>Property overrides to apply after copy (optional)</summary>
        public Dictionary<string, object>? PropertyOverrides { get; set; }

        /// <summary>Block Grid text replacements (find & replace in home property)</summary>
        public Dictionary<string, string>? BlockGridReplacements { get; set; }
    }

    /// <summary>
    /// Batch Copy Pages Request Model
    /// </summary>
    public class BatchCopyRequest
    {
        /// <summary>List of pages to copy</summary>
        public List<CopyPageRequest> Pages { get; set; } = new List<CopyPageRequest>();
    }

    /// <summary>
    /// Batch Country Page Request Model (JSON formatı - toplu sayfa oluşturma)
    /// </summary>
    public class BatchCountryPageRequest
    {
        /// <summary>Varsayılan template key (her sayfa için geçerli)</summary>
        public string TemplateKey { get; set; } = "e5e6c764-e573-4b6b-93ff-a78d15530dc7";
        
        /// <summary>Varsayılan parent key (her sayfa için geçerli)</summary>
        public string ParentKey { get; set; } = "cbe4eba7-e267-43ea-9ecd-82f6915301bc";
        
        /// <summary>Oluşturulacak sayfalar listesi</summary>
        public List<CountryPageRequest> Pages { get; set; } = new List<CountryPageRequest>();
    }

    /// <summary>
    /// Country Page Request Model (JSON formatı)
    /// </summary>
    public class CountryPageRequest
    {
        public string TemplateKey { get; set; } = "e5e6c764-e573-4b6b-93ff-a78d15530dc7";
        public int TemplateId { get; set; } = 0;  // ✅ Template ID ekle
        public string ParentKey { get; set; } = "cbe4eba7-e267-43ea-9ecd-82f6915301bc";
        public string ENKeywords { get; set; } = string.Empty;
        public string ENTitle { get; set; } = string.Empty;
        public string ENDescription { get; set; } = string.Empty;
        public string TRKeywords { get; set; } = string.Empty;
        public string TRTitle { get; set; } = string.Empty;
        public string TRDescription { get; set; } = string.Empty;
        public string URL { get; set; } = string.Empty;
        public string Keyword { get; set; } = string.Empty;
        public string Ana { get; set; } = string.Empty;
        public string AnaEN { get; set; } = string.Empty;
        public string Komponent { get; set; } = string.Empty;
        public string KomponentEN { get; set; } = string.Empty;
        public string Komponent2 { get; set; } = string.Empty;
        public string Komponent2EN { get; set; } = string.Empty;
    }

    /// <summary>
    /// Populate Money Transfer List Request Model
    /// </summary>
    public class PopulateMoneyTransferRequest
    {
        /// <summary>moneyTransferList parent'ın Key'i</summary>
        public string ParentKey { get; set; } = string.Empty;
        
        /// <summary>Ülkeler listesi</summary>
        public List<CountryInfo> Countries { get; set; } = new List<CountryInfo>();
        
        /// <summary>Otomatik publish edilsin mi?</summary>
        public bool AutoPublish { get; set; } = false;
    }

    /// <summary>
    /// Country Information Model
    /// </summary>
    public class CountryInfo
    {
        public string CountryName { get; set; } = string.Empty;
        public string CountryNameEn { get; set; } = string.Empty;
        public string IsoCode { get; set; } = string.Empty;
        public string MoneyCurrency { get; set; } = string.Empty;
    }
}

