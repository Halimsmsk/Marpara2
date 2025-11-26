using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Controllers;
using Umbraco.Cms.Core.Web;
using System.Text.Json;

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
                    if (templateForId != null)
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

                // Block Grid - transactionFeeCountry title değişikliği
                copyRequest.BlockGridReplacements.Add("\"value\":\"Almanya Para Transferi\"", $"\"value\":\"{yeniTitle}\"");
                
                // Block Grid - transactionFeeCountry description değişikliği (Unicode escaped HTML)
                if (!string.IsNullOrEmpty(yeniDescription))
                {
                    var almanyaDesc = "\\u003Cp\\u003EMorpara ile Almanya\\u0027ya para transferi \\u00E7ok kolay! \\u0130ster Almanya banka hesaplar\\u0131na para g\\u00F6nder, istersen al\\u0131c\\u0131n\\u0131n ismine para g\\u00F6nder. \\u0130kisi de tek uygulamada.\\u003C/p\\u003E\\u003Cp\\u003E\\u0130sme para transferini yapmak i\\u00E7in MoneyGram i\\u015Flemlerinden al\\u0131c\\u0131n\\u0131n ad\\u0131na para g\\u00F6nderebilir, hesaba para transferi yapmak i\\u00E7in de direkt hesaba transferi se\\u00E7ebilirsin. \\u00DCstelik hesaba para transferinde i\\u015Flem \\u00FCcreti de yok!\\u003C/p\\u003E\\u003Cp\\u003E\\u003C/p\\u003E";
                    
                    // Yeni description'ı da escape et
                    var yeniDescEscaped = yeniDescription
                        .Replace("'", "\\u0027")
                        .Replace("ç", "\\u00E7")
                        .Replace("ğ", "\\u011F")
                        .Replace("ı", "\\u0131")
                        .Replace("ö", "\\u00F6")
                        .Replace("ş", "\\u015F")
                        .Replace("ü", "\\u00FC")
                        .Replace("İ", "\\u0130")
                        .Replace("Ç", "\\u00C7")
                        .Replace("Ğ", "\\u011E")
                        .Replace("Ö", "\\u00D6")
                        .Replace("Ş", "\\u015E")
                        .Replace("Ü", "\\u00DC");
                    
                    copyRequest.BlockGridReplacements.Add(almanyaDesc, $"\\u003Cp\\u003E{yeniDescEscaped}\\u003C/p\\u003E");
                }

                // Komponent field'ı parse et: ilk \n title, ikinci \n description
                var komponentParts = request.Komponent.Split(new[] { '\n' }, 2);
                var ucretTitle = komponentParts[0].Trim();
                var ucretDescription = komponentParts.Length > 1 ? komponentParts[1].Trim() : "";

                // Block Grid - basicContentBlock 1 (işlem ücreti) title (Unicode escaped)
                var eskiUcretTitle = "\\u003Cp\\u003EAlmanya\\u0027ya para transferi i\\u015Flem \\u00FCcreti nedir?\\u003C/p\\u003E";
                var ucretTitleEscaped = ucretTitle.Replace("'", "\\u0027").Replace("ç", "\\u00E7").Replace("ğ", "\\u011F").Replace("ı", "\\u0131").Replace("ö", "\\u00F6").Replace("ş", "\\u015F").Replace("ü", "\\u00FC").Replace("İ", "\\u0130").Replace("Ç", "\\u00C7").Replace("Ğ", "\\u011E").Replace("Ö", "\\u00D6").Replace("Ş", "\\u015E").Replace("Ü", "\\u00DC");
                copyRequest.BlockGridReplacements.Add(eskiUcretTitle, $"\\u003Cp\\u003E{ucretTitleEscaped}\\u003C/p\\u003E");

                // Block Grid - basicContentBlock 1 (işlem ücreti) description (Unicode escaped)
                if (!string.IsNullOrEmpty(ucretDescription))
                {
                    _logger.LogInformation("[CopyPage] 📝 İŞLEM ÜCRETİ GELEN VERİ: '{Data}'", ucretDescription);
                    
                    // <p> ve </p> taglerini kaldır
                    var cleanText = ucretDescription
                        .Replace("<p>", "")
                        .Replace("</p>", "")
                        .Trim();
                    
                    // Escape et (Turkish chars) - ÇİFT BACKSLASH! Template'de \\u003Cp\\u003E var
                    var escapedText = cleanText
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
                    
                    // Template formatı: ÇİFT BACKSLASH! \\u003Cp\\u003E...\\u003C/p\\u003E
                    var finalFormat = $"\\\\u003Cp\\\\u003E{escapedText}\\\\u003C/p\\\\u003E";
                    
                    _logger.LogInformation("[CopyPage] 🔄 İŞLEM ÜCRETİ YENİ FORMAT (ilk 150 char): '{Data}'", finalFormat.Substring(0, Math.Min(150, finalFormat.Length)));
                    
                    // PLACEHOLDER kullan - template'den gerçek değeri CopyPageInternal'da bulacağız
                    copyRequest.BlockGridReplacements.Add("__UCRET_DESC_PLACEHOLDER__", finalFormat);
                }

                // Komponent2 field'ı parse et: ilk \n title, ikinci \n description
                var komponent2Parts = request.Komponent2.Split(new[] { '\n' }, 2);
                var sureTitle = komponent2Parts[0].Trim();
                var sureDescription = komponent2Parts.Length > 1 ? komponent2Parts[1].Trim() : "";

                // Block Grid - basicContentBlock 2 (transfer süresi) title (Unicode escaped)
                var eskiSureTitle = "\\u003Cp\\u003EAlmanya\\u0027ya para transferi s\\u00FCresi nedir?\\u003C/p\\u003E";
                var sureTitleEscaped = sureTitle.Replace("'", "\\u0027").Replace("ç", "\\u00E7").Replace("ğ", "\\u011F").Replace("ı", "\\u0131").Replace("ö", "\\u00F6").Replace("ş", "\\u015F").Replace("ü", "\\u00FC").Replace("İ", "\\u0130").Replace("Ç", "\\u00C7").Replace("Ğ", "\\u011E").Replace("Ö", "\\u00D6").Replace("Ş", "\\u015E").Replace("Ü", "\\u00DC");
                copyRequest.BlockGridReplacements.Add(eskiSureTitle, $"\\u003Cp\\u003E{sureTitleEscaped}\\u003C/p\\u003E");

                // Block Grid - basicContentBlock 2 (transfer süresi) description (Unicode escaped)
                if (!string.IsNullOrEmpty(sureDescription))
                {
                    var eskiSureDesc = "\\u003Cp\\u003EAlmanya\\u0027ya para transferi s\\u00FCresi,MoneyGram ile isme para transferlerinde dakikalar i\\u00E7inde g\\u00F6nderilmektedir.\\u003C/p\\u003E\\u003Cp\\u003EAlmanya banka hesaplar\\u0131na para transferinde Morpara\\u0027dan transferin an\\u0131nda i\\u015Fleme al\\u0131n\\u0131r. Fakat al\\u0131c\\u0131ya teslim edilmesi kar\\u015F\\u0131 bankan\\u0131n s\\u00FCrelerine g\\u00F6re de\\u011Fi\\u015Fiklik g\\u00F6stermektedir.\\u003C/p\\u003E";
                    var sureDescEscaped = sureDescription.Replace("'", "\\u0027").Replace("ç", "\\u00E7").Replace("ğ", "\\u011F").Replace("ı", "\\u0131").Replace("ö", "\\u00F6").Replace("ş", "\\u015F").Replace("ü", "\\u00FC").Replace("İ", "\\u0130").Replace("Ç", "\\u00C7").Replace("Ğ", "\\u011E").Replace("Ö", "\\u00D6").Replace("Ş", "\\u015E").Replace("Ü", "\\u00DC");
                    copyRequest.BlockGridReplacements.Add(eskiSureDesc, $"\\u003Cp\\u003E{sureDescEscaped}\\u003C/p\\u003E");
                }

                // İngilizce versiyonlar (Unicode escaped)
                if (!string.IsNullOrEmpty(request.AnaEN))
                {
                    var almanyaDescEN = "Sending money to Germany is easy with Morpara! You can transfer money directly to German bank accounts or send it to the recipient\\u0027s name \\u2014 all in one app.\\nFor cash pickup, you can use MoneyGram, and for account transfers, you can choose direct bank transfer. \\n\\nPlus, there are no transaction fees when transferring money to a bank account!";
                    copyRequest.BlockGridReplacements.Add(almanyaDescEN, request.AnaEN.Replace("\n", "\\n").Replace("'", "\\u0027").Replace("—", "\\u2014"));
                }

                if (!string.IsNullOrEmpty(request.KomponentEN))
                {
                    var ucretDescEN = "What are the transaction fees for money transfers to Germany?\\nWith the Morpara mobile app, you won\\u0027t pay any transaction fees when sending money to German accounts.\\nMoneyGram cash pickup to Germany offers competitive rates and attractive fees.";
                    copyRequest.BlockGridReplacements.Add(ucretDescEN, request.KomponentEN.Replace("\n", "\\n").Replace("'", "\\u0027").Replace("—", "\\u2014"));
                }

                if (!string.IsNullOrEmpty(request.Komponent2EN))
                {
                    var sureDescEN = "Sending money to Germany with MoneyGram is fast and reliable \\u2014 transfers are typically completed within just a few minutes.\\n\\nIf you\\u0027re sending money directly to a German bank account, Morpara processes the transfer instantly. Please note that the exact delivery time may vary depending on your recipient\\u0027s bank processing schedule.\\n\\nWith Morpara and MoneyGram, you can enjoy a safe, quick, and convenient money transfer to Germany anytime you need.";
                    copyRequest.BlockGridReplacements.Add(sureDescEN, request.Komponent2EN.Replace("\n", "\\n").Replace("'", "\\u0027").Replace("—", "\\u2014"));
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
                    
                    // EN Block Grid replacements - TR güncellenmiş değerlerini EN ile değiştir
                    if (!string.IsNullOrEmpty(request.AnaEN))
                    {
                        var anaPartsEN = request.AnaEN.Split(new[] { '\n' }, 2);
                        var yeniTitleEN = anaPartsEN[0].Trim();
                        var yeniDescriptionEN = anaPartsEN.Length > 1 ? anaPartsEN[1].Trim() : "";
                        
                        // Title replacement - TR başlığını (güncellenmiş) EN ile değiştir
                        // TR'de "Almanya Para Transferi" → "Afganistan Para Transferi" oldu
                        // Şimdi EN'de "Afganistan Para Transferi" → EN title yapıyoruz
                        copyRequestEN.BlockGridReplacements.Add($"\"value\":\"{yeniTitle}\"", $"\"value\":\"{yeniTitleEN}\"");
                        
                        // Description replacement - TR'den güncellenmiş description'ı EN ile değiştir
                        if (!string.IsNullOrEmpty(yeniDescriptionEN))
                        {
                            // EN için Unicode escaping
                            var cleanTextEN = yeniDescriptionEN
                                .Replace("'", "'")
                                .Trim();
                            
                            var escapedTextEN = cleanTextEN
                                .Replace("'", "\\\\u0027");
                            
                            var finalFormatEN = $"\\\\u003Cp\\\\u003E{escapedTextEN}\\\\u003C/p\\\\u003E";
                            
                            // TR'de güncellenen description formatını key olarak kullan
                            // TR description: yeniDescEscaped formatında
                            var trDescEscaped = yeniDescription
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
                            
                            var trDescFormat = $"\\\\u003Cp\\\\u003E{trDescEscaped}\\\\u003C/p\\\\u003E";
                            
                            // TR description → EN description
                            copyRequestEN.BlockGridReplacements.Add(trDescFormat, finalFormatEN);
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(request.KomponentEN))
                    {
                        var komponentPartsEN = request.KomponentEN.Split(new[] { '\n' }, 2);
                        var ucretTitleEN = komponentPartsEN[0].Trim();
                        var ucretDescriptionEN = komponentPartsEN.Length > 1 ? komponentPartsEN[1].Trim() : "";
                        
                        // Title replacement - TR başlığını (güncellenmiş) EN ile değiştir
                        // TR'de güncellenmiş ucretTitle'ı EN ucretTitle ile değiştir
                        copyRequestEN.BlockGridReplacements.Add($"\"value\":\"{ucretTitle}\"", $"\"value\":\"{ucretTitleEN}\"");
                        
                        // Description replacement - TR'den güncellenmiş description'ı EN ile değiştir
                        if (!string.IsNullOrEmpty(ucretDescriptionEN))
                        {
                            var cleanTextEN = ucretDescriptionEN
                                .Replace("'", "'")
                                .Trim();
                            
                            var escapedTextEN = cleanTextEN
                                .Replace("'", "\\\\u0027");
                            
                            var finalFormatEN = $"\\\\u003Cp\\\\u003E{escapedTextEN}\\\\u003C/p\\\\u003E";
                            
                            // TR ucretDescription formatını key olarak kullan
                            var trUcretEscaped = ucretDescription
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
                            
                            var trUcretFormat = $"\\\\u003Cp\\\\u003E{trUcretEscaped}\\\\u003C/p\\\\u003E";
                            
                            // TR description → EN description
                            copyRequestEN.BlockGridReplacements.Add(trUcretFormat, finalFormatEN);
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(request.Komponent2EN))
                    {
                        var komponent2PartsEN = request.Komponent2EN.Split(new[] { '\n' }, 2);
                        var sureTitleEN = komponent2PartsEN[0].Trim();
                        var sureDescriptionEN = komponent2PartsEN.Length > 1 ? komponent2PartsEN[1].Trim() : "";
                        
                        // Title replacement - TR başlığını (güncellenmiş) EN ile değiştir
                        // TR'de güncellenmiş sureTitle'ı EN sureTitle ile değiştir
                        copyRequestEN.BlockGridReplacements.Add($"\"value\":\"{sureTitle}\"", $"\"value\":\"{sureTitleEN}\"");
                        
                        // Description replacement - TR'den güncellenmiş description'ı EN ile değiştir
                        if (!string.IsNullOrEmpty(sureDescriptionEN))
                        {
                            var cleanTextEN = sureDescriptionEN
                                .Replace("'", "'")
                                .Trim();
                            
                            var escapedTextEN = cleanTextEN
                                .Replace("'", "\\\\u0027");
                            
                            var finalFormatEN = $"\\\\u003Cp\\\\u003E{escapedTextEN}\\\\u003C/p\\\\u003E";
                            
                            // TR sureDescription formatını key olarak kullan
                            var trSureEscaped = sureDescription
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
                            
                            var trSureFormat = $"\\\\u003Cp\\\\u003E{trSureEscaped}\\\\u003C/p\\\\u003E";
                            
                            // TR description → EN description
                            copyRequestEN.BlockGridReplacements.Add(trSureFormat, finalFormatEN);
                        }
                    }
                    
                    // EN kültürünü güncelle
                    CopyPageInternal(copyRequestEN);
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
                        // EN için TR culture'dan alıp EN'e çeviriyoruz
                        var sourceHomeCulture = request.Culture?.StartsWith("en") == true ? "tr-TR" : request.Culture;
                        
                        _logger.LogInformation("[CopyPage] 🔍 EN Block Grid - templateContent: {IsNull}, Culture: {Culture}", 
                            templateContent == null ? "NULL" : "OK", sourceHomeCulture);
                        
                        var homeProperty = templateContent?.GetValue<string>("home", sourceHomeCulture);
                        
                        _logger.LogInformation("[CopyPage] 🔍 Block Grid source culture: {SourceCulture}, target: {TargetCulture}, homeProperty length: {Length}", 
                            sourceHomeCulture, request.Culture, homeProperty?.Length ?? 0);
                        
                        if (!string.IsNullOrEmpty(homeProperty))
                        {
                            var modifiedValue = homeProperty;
                            int successCount = 0;
                            
                            // Tüm replacements'ları işle (titles ve descriptions)
                            _logger.LogInformation("[CopyPage] 🔍 EN: Processing {Count} replacements", request.BlockGridReplacements.Count);
                            foreach (var replacement in request.BlockGridReplacements)
                            {
                                if (modifiedValue.Contains(replacement.Key))
                                {
                                    modifiedValue = modifiedValue.Replace(replacement.Key, replacement.Value);
                                    successCount++;
                                    _logger.LogInformation("[CopyPage] ✅ Replaced: {Key} (first 50 chars)", 
                                        replacement.Key.Substring(0, Math.Min(50, replacement.Key.Length)));
                                }
                                else
                                {
                                    _logger.LogWarning("[CopyPage] ❌ NOT FOUND: {Key} (first 50 chars)", 
                                        replacement.Key.Substring(0, Math.Min(50, replacement.Key.Length)));
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
                            
                            // "Almanya Para Transferi" kelimesinin etrafındaki karakterleri göster
                            var titleIndex = templateHomeProperty.IndexOf("Almanya Para Transferi");
                            if (titleIndex > 0)
                            {
                                var start = Math.Max(0, titleIndex - 50);
                                var length = Math.Min(150, templateHomeProperty.Length - start);
                                var titleContext = templateHomeProperty.Substring(start, length);
                                _logger.LogInformation("[CopyPage] 🔎 'Almanya Para Transferi' etrafındaki format: {Context}", titleContext);
                            }
                            
                            var modifiedValue = templateHomeProperty;
                            int successCount = 0;
                            
                            // TR PLACEHOLDER - İşlem ücreti description
                            if (request.BlockGridReplacements.ContainsKey("__UCRET_DESC_PLACEHOLDER__"))
                            {
                                var newUcretDesc = request.BlockGridReplacements["__UCRET_DESC_PLACEHOLDER__"];
                                
                                _logger.LogInformation("[CopyPage] 🔎 TR PLACEHOLDER BULUNDU! Yeni değer (ilk 100): '{Data}'", newUcretDesc.Length > 100 ? newUcretDesc.Substring(0, 100) : newUcretDesc);
                                
                                // Template'den AYNEN kopyala - tam pattern: \\u003Cp\\u003EAlmanya hesaplar...\\u003C/p\\u003E
                                var oldUcretDesc = "\\\\u003Cp\\\\u003EAlmanya hesaplar\\\\u0131na para g\\\\u00F6nderimde Morpara mobil uygulamas\\\\u0131 ile i\\\\u015Flem \\\\u00FCcreti \\\\u00F6demezsin.\\\\u003C/p\\\\u003E";
                                
                                _logger.LogInformation("[CopyPage] 🔄 Eski değer (template): '{Old}'", oldUcretDesc);
                                _logger.LogInformation("[CopyPage] 🔄 Yeni değer (replacement): '{New}'", newUcretDesc);
                                
                                // Değiştir
                                if (modifiedValue.Contains(oldUcretDesc))
                                {
                                    modifiedValue = modifiedValue.Replace(oldUcretDesc, newUcretDesc);
                                    successCount++;
                                    _logger.LogInformation("[CopyPage] ✅ İŞLEM ÜCRETİ description değiştirildi!");
                                }
                                else
                                {
                                    _logger.LogWarning("[CopyPage] ❌ İŞLEM ÜCRETİ template değeri bulunamadı!");
                                }
                                
                                // Placeholder'ı kaldır
                                request.BlockGridReplacements.Remove("__UCRET_DESC_PLACEHOLDER__");
                            }
                            
                            // EN PLACEHOLDER - Ana description
                            if (request.BlockGridReplacements.ContainsKey("__ANA_DESC_EN_PLACEHOLDER__"))
                            {
                                var newAnaDescEN = request.BlockGridReplacements["__ANA_DESC_EN_PLACEHOLDER__"];
                                _logger.LogInformation("[CopyPage] 🔎 EN ANA PLACEHOLDER BULUNDU!");
                                
                                // Template'deki EN ana description pattern
                                var oldAnaDescEN = "\\\\u003Cp\\\\u003ESending money to Germany is easy with Morpara!\\\\u003C/p\\\\u003E";
                                
                                if (modifiedValue.Contains(oldAnaDescEN))
                                {
                                    modifiedValue = modifiedValue.Replace(oldAnaDescEN, newAnaDescEN);
                                    successCount++;
                                    _logger.LogInformation("[CopyPage] ✅ EN ANA description değiştirildi!");
                                }
                                else
                                {
                                    _logger.LogWarning("[CopyPage] ❌ EN ANA template değeri bulunamadı!");
                                }
                                
                                request.BlockGridReplacements.Remove("__ANA_DESC_EN_PLACEHOLDER__");
                            }
                            
                            // EN PLACEHOLDER - Ücret description
                            if (request.BlockGridReplacements.ContainsKey("__UCRET_DESC_EN_PLACEHOLDER__"))
                            {
                                var newUcretDescEN = request.BlockGridReplacements["__UCRET_DESC_EN_PLACEHOLDER__"];
                                _logger.LogInformation("[CopyPage] 🔎 EN ÜCRET PLACEHOLDER BULUNDU!");
                                
                                // Template'deki EN ücret description pattern - ilk paragrafı al
                                var oldUcretDescEN = "\\\\u003Cp\\\\u003EWith the Morpara mobile app, you won\\\\u0027t pay any transaction fees when sending money to German accounts.\\\\u003C/p\\\\u003E";
                                
                                if (modifiedValue.Contains(oldUcretDescEN))
                                {
                                    modifiedValue = modifiedValue.Replace(oldUcretDescEN, newUcretDescEN);
                                    successCount++;
                                    _logger.LogInformation("[CopyPage] ✅ EN ÜCRET description değiştirildi!");
                                }
                                else
                                {
                                    _logger.LogWarning("[CopyPage] ❌ EN ÜCRET template değeri bulunamadı!");
                                }
                                
                                request.BlockGridReplacements.Remove("__UCRET_DESC_EN_PLACEHOLDER__");
                            }
                            
                            // EN PLACEHOLDER - Süre description
                            if (request.BlockGridReplacements.ContainsKey("__SURE_DESC_EN_PLACEHOLDER__"))
                            {
                                var newSureDescEN = request.BlockGridReplacements["__SURE_DESC_EN_PLACEHOLDER__"];
                                _logger.LogInformation("[CopyPage] 🔎 EN SÜRE PLACEHOLDER BULUNDU!");
                                
                                // Template'deki EN süre description pattern - ilk paragrafı al
                                var oldSureDescEN = "\\\\u003Cp\\\\u003ESending money to Germany with MoneyGram is fast and reliable \\\\u2014 transfers are typically completed within just a few minutes.\\\\u003C/p\\\\u003E";
                                
                                if (modifiedValue.Contains(oldSureDescEN))
                                {
                                    modifiedValue = modifiedValue.Replace(oldSureDescEN, newSureDescEN);
                                    successCount++;
                                    _logger.LogInformation("[CopyPage] ✅ EN SÜRE description değiştirildi!");
                                }
                                else
                                {
                                    _logger.LogWarning("[CopyPage] ❌ EN SÜRE template değeri bulunamadı!");
                                }
                                
                                request.BlockGridReplacements.Remove("__SURE_DESC_EN_PLACEHOLDER__");
                            }
                            
                            foreach (var replacement in request.BlockGridReplacements)
                            {
                                var oldPreview = replacement.Key.Length > 60 ? replacement.Key.Substring(0, 60) + "..." : replacement.Key;
                                
                                _logger.LogInformation("[CopyPage] 🔍 Aranan: '{Old}'", oldPreview);
                                
                                // Escape edilmiş JSON string'lerini de dene
                                var escapedKey = System.Text.Json.JsonSerializer.Serialize(replacement.Key);
                                var escapedValue = System.Text.Json.JsonSerializer.Serialize(replacement.Value);
                                
                                // Tırnak işaretlerini kaldır (JSON serialize tırnak ekler)
                                if (escapedKey.StartsWith("\"") && escapedKey.EndsWith("\""))
                                {
                                    escapedKey = escapedKey.Substring(1, escapedKey.Length - 2);
                                }
                                if (escapedValue.StartsWith("\"") && escapedValue.EndsWith("\""))
                                {
                                    escapedValue = escapedValue.Substring(1, escapedValue.Length - 2);
                                }
                                
                                // Önce normal string'i dene
                                if (modifiedValue.Contains(replacement.Key))
                                {
                                    modifiedValue = modifiedValue.Replace(replacement.Key, replacement.Value);
                                    successCount++;
                                    _logger.LogInformation("[CopyPage] ✅ Normal string ile değiştirildi");
                                }
                                // Eğer normal bulunamadıysa escaped versiyonu dene
                                else if (modifiedValue.Contains(escapedKey))
                                {
                                    modifiedValue = modifiedValue.Replace(escapedKey, escapedValue);
                                    successCount++;
                                    _logger.LogInformation("[CopyPage] ✅ Escaped string ile değiştirildi");
                                }
                                else
                                {
                                    _logger.LogWarning("[CopyPage] ❌ BULUNAMADI! Escaped: '{Escaped}'", 
                                        escapedKey.Length > 60 ? escapedKey.Substring(0, 60) + "..." : escapedKey);
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
}
