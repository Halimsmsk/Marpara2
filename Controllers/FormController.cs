using Microsoft.AspNetCore.Mvc;
using Morpara.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Web.Common.Controllers;
using Hangfire;
using Umbraco.Cms.Core;

namespace Morpara.Controllers
{
    [ApiController]
    [Route("umbraco/api/[controller]")]
    public class FormController : UmbracoApiController
    {
        private readonly ILogger<FormController> _logger;
        private readonly IContentService _contentService;
        private readonly IContentTypeService _contentTypeService;
        private readonly IDataTypeService _dataTypeService;
        private readonly IShortStringHelper _shortStringHelper;
        private readonly Umbraco.Cms.Infrastructure.Scoping.IScopeProvider _scopeProvider;

        // Simple in-memory cache to avoid redundant database checks
        private static readonly Dictionary<string, bool> _contentTypeCache = new();
        private static readonly Dictionary<int, bool> _containerCache = new();
        private static readonly Dictionary<int, int> _formContainerIdCache = new(); // Form ID -> Container ID mapping
        private static readonly object _cacheLock = new();

        // Dictionary for field type regex patterns
        private readonly Dictionary<string, string> _fieldTypePatterns = new Dictionary<string, string>
        {
            { "email", @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$" },
            // ⚡ Flexible phone validation: Supports formats like "+90 546 677 42 13", "5466774213", "+905466774213", "(546) 677-4213"
            // Allows: +, spaces, dashes, parentheses, and digits. Must have 10-15 digits total.
            { "tel", @"^[\s\d\+\-\(\)]+$" },
            { "text", @"^.+$" }, // Basic non-empty validation
            { "textarea", @"^[\s\S]*$" }, // Any text including line breaks
            { "date", @"^\d{4}-\d{2}-\d{2}$" }, // yyyy-MM-dd format
            { "checkbox", @"^(true|false|1|0|yes|no)$" }  // Boolean values with expanded options
        };

        private string GenerateFormKey(string title)
        {
            if (string.IsNullOrEmpty(title))
                return string.Empty;

            // Turkish to English character mapping (all lowercase)
            var turkishToEnglish = new Dictionary<char, char>
            {
                {'ç', 'c'}, {'Ç', 'c'},
                {'ğ', 'g'}, {'Ğ', 'g'},
                {'ı', 'i'}, {'I', 'i'},
                {'İ', 'i'}, {'i', 'i'},
                {'ö', 'o'}, {'Ö', 'o'},
                {'ş', 's'}, {'Ş', 's'},
                {'ü', 'u'}, {'Ü', 'u'}
            };

            var result = title.ToLowerInvariant();
            
            // Replace Turkish characters
            foreach (var pair in turkishToEnglish)
            {
                result = result.Replace(pair.Key, pair.Value);
            }

            // Remove spaces and special characters, keep only alphanumeric and convert to lowercase
            result = string.Concat(result.Where(c => char.IsLetterOrDigit(c))).ToLowerInvariant();

            return result;
        }

        public FormController(
            ILogger<FormController> logger,
            IContentService contentService,
            IContentTypeService contentTypeService,
            IDataTypeService dataTypeService,
            IShortStringHelper shortStringHelper,
            Umbraco.Cms.Infrastructure.Scoping.IScopeProvider scopeProvider)
        {
            _logger = logger;
            _contentService = contentService;
            _contentTypeService = contentTypeService;
            _dataTypeService = dataTypeService;
            _shortStringHelper = shortStringHelper;
            _scopeProvider = scopeProvider;
        }

        // GET /umbraco/api/Form/{id}
        [HttpGet("{id}")]
        public IActionResult Get(int id)
        {
            try
            {
                var formContent = _contentService.GetById(id);
                if (formContent == null)
                {
                    return NotFound($"Form bulunamadı: {id}");
                }

                var formFields = new List<object>();
                var formsProperty = formContent.Properties.FirstOrDefault(p => p.Alias == "forms");

                if (formsProperty != null)
                {
                    var formsJson = formsProperty.GetValue()?.ToString();
                    if (!string.IsNullOrEmpty(formsJson))
                    {
                        try
                        {
                            dynamic formsObject = JsonConvert.DeserializeObject(formsJson);
                            if (formsObject?.items != null)
                            {
                                foreach (var item in formsObject.items)
                                {
                                    if (item?.content?.properties?.title != null)
                                    {
                                        string fieldTitle = item.content.properties.title.ToString();
                                        formFields.Add(new
                                        {
                                            id = Guid.NewGuid().ToString("N"),
                                            title = fieldTitle,
                                            type = DetermineFieldType(fieldTitle),
                                            required = false,
                                            placeholder = $"{fieldTitle} giriniz"
                                        });
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Form JSON verisi ayrıştırılırken hata oluştu");
                        }
                    }
                }

                return Ok(new
                {
                    id = formContent.Id,
                    name = formContent.Name,
                    fields = formFields,
                    submitButtonText = "Gönder"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Form bilgileri alınırken hata oluştu");
                return StatusCode(500, "Form bilgileri alınırken hata oluştu");
            }
        }
        public class ContactFormModel
        {
            public string Name { get; set; }
            public string Email { get; set; }
            public string Subject { get; set; }
            public string Message { get; set; }
        }
        [HttpPost("sendmail")]
        public async Task<IActionResult> SendContactForm([FromBody] ContactFormModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                // Form verilerini JSON'a çevir
                var json = JsonConvert.SerializeObject(model);

                // Mail ayarları
                var mailSettings = new MailFormModel
                {
                    SmtpHost = "smtp.yandex.com.tr",
                    SmtpPort = 587,
                    SmtpUsername = "halim@puxo.com.tr",
                    SmtpPassword = "Halim12345@..",
                    SSL = true,
                    FromEmail = "halim@puxo.com.tr",
                    Sendtoadress = "hlmsmsk666@gmail.com",
                    NotificationEmail = ""
                };

                // SendForm extension method'unu çağır
                await typeof(ContactFormModel).SendForm(
                    json,
                    this,
                    mailSettings,
                    "/Views/testmail.cshtml",
                    "Yeni İletişim Formu"
                );

                return Ok(new { success = true, message = "Form başarıyla gönderildi." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
        // POST /umbraco/api/Form/{id}
        [HttpPost("{id}")]
        public async Task<IActionResult> Post(int id, [FromBody] JsonElement rawFormData, [FromQuery] string culture = "tr-TR")
        {
            var startTime = DateTime.Now;
            _logger.LogInformation($"Form işleme başladı: FormID={id}, Timestamp={startTime:yyyy-MM-dd HH:mm:ss.fff}");
            
            try
            {
                // Validate input
                if (rawFormData.ValueKind == JsonValueKind.Undefined || rawFormData.ValueKind == JsonValueKind.Null)
                {
                    return BadRequest(new {
                        success = false,
                        message = "Form verisi bulunamadı",
                        errors = new Dictionary<string, string[]> {
                            { "formData", new[] { "The formData field is required." } }
                        }
                    });
                }

                // Convert the form data to a dictionary of string values
                var formData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                
                // Deserialize the JsonElement to a dictionary
                try
                {
                    if (rawFormData.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in rawFormData.EnumerateObject())
                        {
                            string key = property.Name;
                            string value;
                            
                            switch (property.Value.ValueKind)
                            {
                                case JsonValueKind.String:
                                    value = property.Value.GetString() ?? string.Empty;
                                    break;
                                case JsonValueKind.True:
                                    value = "true";
                                    break;
                                case JsonValueKind.False:
                                    value = "false";
                                    break;
                                case JsonValueKind.Number:
                                    value = property.Value.GetRawText();
                                    break;
                                case JsonValueKind.Null:
                                    value = string.Empty;
                                    break;
                                default:
                                    value = property.Value.GetRawText();
                                    break;
                            }
                            
                            // Use the original key for storing, but create a normalized alias for validation later
                            formData.Add(key, value);
                        }
                        
                    }
                    else
                    {
                        return BadRequest(new {
                            success = false,
                            message = "Geçersiz form formatı",
                            errors = new Dictionary<string, string[]> {
                                { "formData", new[] { "Form verisi bir JSON nesnesi olmalıdır" } }
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error parsing form data");
                    return BadRequest(new {
                        success = false,
                        message = "Geçersiz form formatı",
                        errors = new Dictionary<string, string[]> {
                            { "formData", new[] { $"Form verisi ayrıştırılamadı: {ex.Message}" } }
                        }
                    });
                }

                if (formData.Count == 0)
                {
                    return BadRequest("Form verisi bulunamadı");
                }

                var formContent = _contentService.GetById(id);
                if (formContent == null)
                {
                    return NotFound($"Form bulunamadı: {id}");
                }

                _logger.LogInformation($"Form verileri hazırlandı: {DateTime.Now.Subtract(startTime).TotalMilliseconds}ms");

                // CONTAINER OPERATIONS - Optimize with minimal database calls
                var containerAlias = "formContainer";
                var containerContentType = _contentTypeService.Get(containerAlias);
                if (containerContentType == null)
                {
                    // Create a new container content type with all required properties
                    containerContentType = new ContentType(_shortStringHelper, -1)
                    {
                        Alias = containerAlias,
                        Name = "Form Yanıtları Klasörü",
                        Icon = "icon-folder",
                        AllowedAsRoot = true,
                    };

                    // Add a basic property group to the content type
                    containerContentType.AddPropertyGroup("formContainerSettings", "Ayarlar");
                    
                    // Save the content type
                    _contentTypeService.Save(containerContentType);
                    
                    // Log that we created a new content type
                    _logger.LogInformation("FormContainer content type oluşturuldu");
                }

                // Create a list to store form field information for content type creation
                var formFieldStructure = new List<(string Title, string Type, bool Required, string orjalias, string Regex, string RegexMessage, string FormKey)>();

                // Continue with form processing without waiting for the email to be sent
                // Replace the existing BlockGridModel approach with direct JSON deserialization
                var formsJson = formContent.GetValue<string>("forms", culture);
                
                _logger.LogWarning($"�🔥🔥 FORMS JSON FULL TEXT START 🔥🔥🔥");
                _logger.LogWarning($"{formsJson}");
                _logger.LogWarning($"🔥🔥🔥 FORMS JSON FULL TEXT END 🔥🔥🔥");
                _logger.LogWarning($"🔍 FORMS JSON LENGTH: {formsJson?.Length ?? 0} characters");
                
                if (!string.IsNullOrEmpty(formsJson))
                {
                    try
                    {
                        _logger.LogWarning($"🔥 ABOUT TO PARSE JSON...");
                        dynamic formsData = JsonConvert.DeserializeObject(formsJson);
                        _logger.LogWarning($"🔥 JSON PARSED SUCCESSFULLY!");
                        
                        // Check what structure we got
                        bool hasForms = formsData?.forms != null;
                        bool hasLayout = formsData?.Layout != null || formsData?.layout != null;
                        _logger.LogWarning($"🔍 PARSED JSON - hasForms: {hasForms}, hasLayout: {hasLayout}");
                        
                        // Log the forms array if it exists
                        if (hasForms)
                        {
                            _logger.LogWarning($"🔥 FORMS ARRAY EXISTS - Count: {formsData.forms.Count}");
                            int index = 0;
                            foreach (var item in formsData.forms)
                            {
                                var contentType = item?.contentType?.ToString() ?? "NULL";
                                _logger.LogWarning($"🔥 FORM ITEM [{index}] - contentType: '{contentType}'");
                                index++;
                            }
                        }
                        
                        _logger.LogWarning($"🔥 CALLING ExtractFormFieldStructure...");
                        formFieldStructure = ExtractFormFieldStructure(formsData);
                        _logger.LogWarning($"🔥 EXTRACTION COMPLETE - Got {formFieldStructure.Count} fields");
                        
                        // Log extracted structure for debugging
                        _logger.LogInformation($"Extracted {formFieldStructure.Count} form fields from structure");
                        _logger.LogInformation($"📋 Form Field Structure BEFORE VALIDATION:");
                        foreach (var field in formFieldStructure)
                        {
                            _logger.LogInformation($"   - Title: '{field.Title}', FormKey: '{field.FormKey}', Type: '{field.Type}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Form JSON verisi ayrıştırılırken hata oluştu");
                    }
                }
                else
                {
                    _logger.LogWarning("Forms JSON verisi bulunamadı.");
                }

                // Validate form data against form field structure
                _logger.LogWarning($"⚠️ ABOUT TO VALIDATE - formFieldStructure.Count: {formFieldStructure.Count}");
                var validationErrors = ValidateFormData(formData, formFieldStructure);
                if (validationErrors.Count > 0)
                {
                    _logger.LogWarning($"Form validation BAŞARISIZ - {validationErrors.Count} hata bulundu: {string.Join(", ", validationErrors.Keys)}");
                    return BadRequest(new { 
                        success = false, 
                        message = "Form doğrulama hatası. Lütfen gönderdiğiniz alanların form yapısına uygun olduğundan emin olun.", 
                        errors = validationErrors 
                    });
                }

                _logger.LogInformation($"Form validation BAŞARILI: {DateTime.Now.Subtract(startTime).TotalMilliseconds}ms");

                // EMAIL SENDING - Simple fire-and-forget without Hangfire (scope problemi yüzünden)
                // Email gönderimi form kaydetmeden SONRA yapılacak
                bool shouldSendEmail = true;

                // OPTIMIZED: Content type ve container hazırlığı - sadece gerektiğinde oluştur/güncelle
                string cultureCode = !string.IsNullOrEmpty(culture) && culture.Length >= 2 ? culture.Substring(0, 2).ToLowerInvariant() : "tr";
                
                // Önce hızlı kontrol yap - content type ve container varsa direk kullan
                var responseAlias = $"formResponse{id}{cultureCode}";
                var existingResponseType = _contentTypeService.Get(responseAlias);
                
                // Container'ı bul - OPTIMIZE: İlk bulduğumuzu cache'le, bir daha sorma!
                IContent existingContainer = null;
                
                // Önce cache'den dene
                int cachedContainerId = 0;
                lock (_cacheLock)
                {
                    if (_formContainerIdCache.TryGetValue(id, out cachedContainerId))
                    {
                        _logger.LogInformation($"✅ Container ID cache'den alındı: {cachedContainerId}");
                    }
                }
                
                if (cachedContainerId > 0)
                {
                    // Cache'den container ID varsa direkt GetById kullan (ÇOOK HIZLI!)
                    existingContainer = _contentService.GetById(cachedContainerId);
                }
                else
                {
                    // Cache'de yoksa ilk seferlik database'den sorgula
                    var existingContainerContentType = _contentTypeService.Get(containerAlias);
                    
                    if (existingContainerContentType != null)
                    {
                        _logger.LogWarning("⚠️ Container cache'de YOK - database sorgusu yapılıyor (YAVAŞ!)");
                        var containers = _contentService.GetPagedOfType(existingContainerContentType.Id, 0, 1, out long totalRecords, null, null);
                        if (containers.Any())
                        {
                            existingContainer = containers.First();
                            _logger.LogInformation($"Mevcut container bulundu: {existingContainer.Id}");
                            
                            // CACHE'E EKLE - bir daha sorgulamayacağız!
                            lock (_cacheLock)
                            {
                                _formContainerIdCache[id] = existingContainer.Id;
                                _logger.LogInformation($"✅ Container ID cache'e eklendi: {existingContainer.Id}");
                            }
                        }
                    }
                }
                
                if (existingResponseType != null && existingContainer != null)
                {
                    _logger.LogInformation($"Content type ve container zaten mevcut - hızlı yol kullanılıyor (Container ID: {existingContainer.Id})");
                    
                    // Eksik property kontrolü ve ekleme
                    var existingAliases = existingResponseType.PropertyTypes.Select(p => p.Alias).ToHashSet();
                    var missingFields = new List<(string Title, string Type, bool Required, string orjalias, string Regex, string RegexMessage, string FormKey)>();
                    
                    foreach (var field in formFieldStructure)
                    {
                        var alias = !string.IsNullOrEmpty(field.FormKey) ? field.FormKey : field.Title.ToSafeAlias(_shortStringHelper);
                        if (!existingAliases.Contains(alias))
                        {
                            missingFields.Add(field);
                        }
                    }
                    
                    if (missingFields.Count > 0)
                    {
                        _logger.LogWarning($"UYARI: {missingFields.Count} eksik property tespit edildi. Ekleniyorlar...");
                        
                        // Eksik property'leri ekle (senkron)
                        UpdateContentTypeProperties(existingResponseType, missingFields, formData);
                        
                        _logger.LogInformation($"✅ {missingFields.Count} eksik property eklendi.");
                    }
                }
                else
                {
                    // Content type veya container yoksa SADECE İLK SEFERDE oluştur
                    _logger.LogInformation("İLK FORM GÖNDERİMİ - Content type ve container oluşturuluyor...");
                    
                    try
                    {
                        var (container, responseContentType) = EnsureContentTypesAndContainerExist(id, cultureCode, formContent, formFieldStructure, formData);
                        
                        if (container == null || responseContentType == null)
                        {
                            _logger.LogError("Content type hazırlığı başarısız");
                            return StatusCode(500, "İlk form kaydı başarısız - content type oluşturulamadı");
                        }
                        
                        existingContainer = container;
                        existingResponseType = responseContentType;
                        _logger.LogInformation("İlk form kaydı için hazırlık tamamlandı");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Content type oluşturma hatası");
                        return StatusCode(500, $"Form hazırlama hatası: {ex.Message}");
                    }
                }

                _logger.LogInformation($"Content type hazırlığı tamamlandı: {DateTime.Now.Subtract(startTime).TotalMilliseconds}ms");
                
                try
                {
                    _logger.LogInformation($"Creating form response under container ID {existingContainer.Id}");
                    
                    var response = _contentService.Create($"{formContent.Name} {cultureCode} - {DateTime.Now:yyyy-MM-dd HH:mm}", existingContainer, existingResponseType.Alias);
                    if (response == null)
                    {
                        _logger.LogError("Form yanıtı oluşturulamadı");
                        return StatusCode(500, "Form işlenirken hata: Yanıt oluşturulamadı");
                    }
                    
                    // ⚡ OPTIMIZATION: İlk Save'i kaldırdık - sadece Create yaptık, henüz veritabanına kaydetmedik
                    // Tüm property'leri set ettikten sonra tek seferde Save yapacağız (line ~585)
                    
                    // Set all form field values - with optimized error handling and proper alias mapping
                    foreach (var field in formData)
                    {
                        string propertyAlias = null;
                        _logger.LogInformation($"🔍 Processing form field: '{field.Key}' = '{field.Value}'");
                        try 
                        {
                            // Find the correct property alias from form field structure
                            string fieldFormKeyLower = field.Key.ToLowerInvariant();
                            
                            // First try to match with FormKey (case-insensitive)
                            var matchedField = formFieldStructure.FirstOrDefault(f => 
                                f.FormKey.ToLowerInvariant() == fieldFormKeyLower ||
                                f.Title.Equals(field.Key, StringComparison.OrdinalIgnoreCase) ||
                                NormalizeFieldName(f.Title).Equals(field.Key, StringComparison.OrdinalIgnoreCase));
                            
                            if (matchedField.Title != null)
                            {
                                // Use the FormKey from matched field (this is the actual property alias created in Umbraco)
                                propertyAlias = matchedField.FormKey;
                                _logger.LogInformation($"🔍 Matched field '{field.Key}' -> FormKey: '{propertyAlias}', Type: '{matchedField.Type}'");
                                
                                // Verify property exists, if not try alternate versions
                                if (!response.Properties.Any(p => p.Alias.Equals(propertyAlias, StringComparison.OrdinalIgnoreCase)))
                                {
                                    // Try with original field key
                                    if (response.Properties.Any(p => p.Alias.Equals(field.Key, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        propertyAlias = field.Key;
                                        _logger.LogInformation($"Using original field key as alias: '{propertyAlias}'");
                                    }
                                    else
                                    {
                                        // Try normalized version
                                        var normalizedAlias = NormalizeFieldForOutput(matchedField.Title);
                                        if (response.Properties.Any(p => p.Alias.Equals(normalizedAlias, StringComparison.OrdinalIgnoreCase)))
                                        {
                                            propertyAlias = normalizedAlias;
                                            _logger.LogInformation($"Using normalized alias: '{propertyAlias}'");
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // Fallback to the original method
                                propertyAlias = field.Key.ToSafeAlias(_shortStringHelper);
                                _logger.LogWarning($"No field structure match found for '{field.Key}', using fallback alias '{propertyAlias}'");
                            }
                            
                            // Check if property exists
                            if (response.Properties.Any(p => p.Alias == propertyAlias))
                            {
                                var property = response.Properties.First(p => p.Alias == propertyAlias);
                                var dataType = _dataTypeService.GetDataType(property.PropertyType.DataTypeId);
                                
                                // Handle field value based on editor type
                                if (dataType != null)
                                {
                                    string editorAlias = dataType.EditorAlias;
                                    
                                    switch (editorAlias)
                                    {
                                        case "Umbraco.TrueFalse":  // Checkbox
                                            bool boolValue = field.Value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true || 
                                                field.Value == "1" || 
                                                field.Value?.Equals("yes", StringComparison.OrdinalIgnoreCase) == true;
                                            response.SetValue(propertyAlias, boolValue);
                                            break;
                                            
                                        case "Umbraco.DateTime":  // Date and DateTime
                                            if (DateTime.TryParse(field.Value, out DateTime dateValue))
                                            {
                                                response.SetValue(propertyAlias, dateValue);
                                            }
                                            else
                                            {
                                                response.SetValue(propertyAlias, field.Value);
                                            }
                                            break;
                                            
                                        case "Umbraco.Integer":
                                        case "Umbraco.Decimal":
                                            if (decimal.TryParse(field.Value, out decimal numValue))
                                            {
                                                response.SetValue(propertyAlias, numValue);
                                            }
                                            else
                                            {
                                                response.SetValue(propertyAlias, field.Value);
                                            }
                                            break;
                                            
                                        default:  // TextBox, TextArea, etc.
                                            response.SetValue(propertyAlias, field.Value);
                                            break;
                                    }
                                    _logger.LogInformation($"✅ Successfully set property '{propertyAlias}' = '{field.Value}' (editor: {dataType.EditorAlias})");
                                }
                                else
                                {
                                    // Fallback to string value if we can't determine the editor type
                                    response.SetValue(propertyAlias, field.Value);
                                    _logger.LogInformation($"✅ Successfully set property '{propertyAlias}' = '{field.Value}' (fallback to string)");
                                }
                            }
                            else
                            {
                                _logger.LogWarning($"Property '{propertyAlias}' not found in content type. Available properties: {string.Join(", ", response.Properties.Select(p => p.Alias))}");
                            }
                        }
                        catch (Exception fieldEx)
                        {
                            _logger.LogError(fieldEx, $"Setting field value failed for {field.Key} -> {propertyAlias}");
                        }
                    }
                    
                    // ⚡ ÖNEMLİ: Save işlemini BURADA YAPMIYORUZ!
                    // Çünkü Save → ContentCacheRefresher → DocumentUrlService → 30s timeout
                    // Bunun yerine tüm form data'yı Hangfire'a gönderip orada Create+Save+Publish yapacağız
                    
                    _logger.LogWarning($"⚡ Save işlemi Hangfire'a taşındı - Container: {existingContainer.Id}, Type: {existingResponseType.Alias}");
                    
                    // Return success immediately, handle saving, publishing and email in Hangfire
                    var totalTime = DateTime.Now.Subtract(startTime).TotalMilliseconds;
                    _logger.LogInformation($"Form hazırlığı tamamlandı: {totalTime}ms");
                    
                    // Run EVERYTHING in background with HANGFIRE to avoid timeout
                    if (shouldSendEmail)
                    {
                        var formId = formContent.Id;
                        var formName = formContent.Name;
                        
                        _logger.LogWarning($"🚀 HANGFIRE JOB PLANLANDI! Form: {formName} - HEMEN çalışacak (Create+Save+Publish+Email)");
                        
                        // ✅ ÖNEMLI: HttpContext varken email template'ini render et!
                        string renderedEmailHtml = null;
                        List<string> emailRecipients = null;
                        
                        try
                        {
                            // 1. Template'i render et (HttpContext aktif!)
                            _logger.LogInformation("📧 Rendering email template...");
                            renderedEmailHtml = RenderEmailTemplate(formContent, formData, formFieldStructure, culture);
                            _logger.LogInformation($"✅ Email template rendered: {renderedEmailHtml.Length} chars");
                            
                            // 2. Email alıcılarını parse et
                            string emailJsonString = formContent.GetValue<string>("sendMailUser", culture);
                            if (!string.IsNullOrEmpty(emailJsonString))
                            {
                                emailJsonString = emailJsonString.Replace("\\\"", "\"");
                                
                                try
                                {
                                    emailRecipients = System.Text.Json.JsonSerializer.Deserialize<List<string>>(emailJsonString);
                                }
                                catch (System.Text.Json.JsonException)
                                {
                                    if (emailJsonString.Contains(","))
                                    {
                                        emailRecipients = emailJsonString.Split(',').Select(e => e.Trim()).Where(e => !string.IsNullOrEmpty(e)).ToList();
                                    }
                                    else
                                    {
                                        emailRecipients = new List<string> { emailJsonString.Trim() };
                                    }
                                }
                                
                                _logger.LogInformation($"📧 Email alıcıları: {string.Join(", ", emailRecipients)}");
                            }
                            
                            // 3. @Model. reference'larını resolve et
                            if (emailRecipients != null && emailRecipients.Any())
                            {
                                var resolvedRecipients = new List<string>();
                                foreach (var email in emailRecipients)
                                {
                                    if (email.StartsWith("@Model."))
                                    {
                                        string propertyName = email.Substring(7);
                                        if (formData.TryGetValue(propertyName, out string modelValue) && !string.IsNullOrEmpty(modelValue))
                                        {
                                            resolvedRecipients.Add(modelValue);
                                            _logger.LogInformation($"Resolved {email} → {modelValue}");
                                        }
                                    }
                                    else
                                    {
                                        resolvedRecipients.Add(email);
                                    }
                                }
                                emailRecipients = resolvedRecipients;
                            }
                        }
                        catch (Exception renderEx)
                        {
                            _logger.LogError(renderEx, "❌ Email template rendering hatası");
                            // Fallback: basit HTML
                            renderedEmailHtml = CreateFallbackEmailHtml(formData, formContent.Name ?? "Form");
                        }
                        
                        // ⚡ HANGFIRE IMMEDIATE JOB - Tüm Create/Save/Publish/Email işlemleri background'da
                        // ⚡ DisableConcurrentExecution: Aynı anda birden fazla job çalışmasın (DocumentUrl çakışması önlenir)
                        // ⚡ AutomaticRetry(Attempts = 0): Timeout olursa retry yapma (zaten DocumentUrl sorunu var)
                        var jobId = BackgroundJob.Enqueue(() => CreateSavePublishAndSendEmailJob(
                            formId,
                            formName,
                            rawFormData.GetRawText(),
                            culture,
                            JsonConvert.SerializeObject(formData),
                            JsonConvert.SerializeObject(formFieldStructure),
                            existingContainer.Id,
                            existingResponseType.Alias,
                            cultureCode,
                            renderedEmailHtml,
                            emailRecipients != null ? JsonConvert.SerializeObject(emailRecipients) : null
                        ));
                        
                        _logger.LogWarning($"✅ Hangfire Job ID: {jobId} - Job planlandı (Email HTML hazır!)");
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ shouldSendEmail = false, Hangfire job planlanmayacak!");
                    }
                    
                    return Ok(new
                    {
                        success = true,
                        message = "Form başarıyla alındı, arka planda işleniyor...",
                        processingTime = totalTime
                    });
                }
                catch (Exception responseEx)
                {
                    _logger.LogError(responseEx, "Form yanıtı oluşturulurken hata");
                    return StatusCode(500, "Form işlenirken hata: " + responseEx.Message);
                }
            }
            catch (Exception ex)
            {
                var totalTime = DateTime.Now.Subtract(startTime).TotalMilliseconds;
                _logger.LogError(ex, "Form işlenirken hata oluştu - Süre: {Duration}ms", totalTime);
                return StatusCode(500, "Form işlenirken hata: " + ex.Message);
            }
        }

        // ⚡ HANGFIRE BACKGROUND JOB - Public olmalı, parametreler serializable olmalı
        // Bu metod: Create + Save + Publish + Email - HER ŞEYİ background'da yapar
        // ⚡ AutomaticRetry(Attempts = 0): DocumentUrl timeout olursa retry yapma (sorunu çözmez)
        // ⚡ DisableConcurrentExecution: Aynı anda birden fazla job çalışmasın
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 3600)]
        public async Task CreateSavePublishAndSendEmailJob(
            int formId, 
            string formName, 
            string rawFormDataJson, 
            string culture, 
            string formDataJson, 
            string formFieldStructureJson, 
            int containerId, 
            string contentTypeAlias, 
            string cultureCode,
            string renderedEmailHtml,
            string emailRecipientsJson)
        {
            try
            {
                _logger.LogWarning($"🔥 HANGFIRE JOB BAŞLADI (Create+Save+Publish+Email)! Form: {formName}");
                
                // 1. Content'i oluştur (memory'de)
                var container = _contentService.GetById(containerId);
                if (container == null)
                {
                    _logger.LogError($"❌ Container {containerId} bulunamadı!");
                    return;
                }
                
                _logger.LogInformation($"📝 Creating new content under container {containerId}...");
                var response = _contentService.Create($"{formName} {cultureCode} - {DateTime.Now:yyyy-MM-dd HH:mm}", container, contentTypeAlias);
                
                if (response == null)
                {
                    _logger.LogError("❌ Content oluşturulamadı!");
                    return;
                }
                
                // 2. Form data'yı parse et ve property'leri set et
                var formData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(formDataJson);
                var formFieldStructure = JsonConvert.DeserializeObject<List<(string Title, string Type, bool Required, string orjalias, string Regex, string RegexMessage, string FormKey)>>(formFieldStructureJson);
                
                _logger.LogInformation($"Setting {formData.Count} properties...");
                
                foreach (var field in formData)
                {
                    try
                    {
                        // Form field key'ini property alias'a çevir
                        var matchingField = formFieldStructure.FirstOrDefault(f => 
                            f.Title.Equals(field.Key, StringComparison.OrdinalIgnoreCase) || 
                            f.FormKey.Equals(field.Key, StringComparison.OrdinalIgnoreCase));
                        
                        string propertyAlias = null;
                        if (matchingField != default)
                        {
                            propertyAlias = !string.IsNullOrEmpty(matchingField.FormKey) ? matchingField.FormKey : matchingField.orjalias;
                        }
                        else
                        {
                            propertyAlias = field.Key;
                        }
                        
                        // Property'yi set et
                        if (response.Properties.Contains(propertyAlias))
                        {
                            // ⚡ Checkbox için özel dönüşüm - Umbraco TrueFalse editor'u Int32 bekler (0 veya 1)
                            object valueToSet = field.Value;
                            
                            if (matchingField != default && matchingField.Type == "checkbox")
                            {
                                // Boolean'a çevir ve ardından int'e (true=1, false=0)
                                bool boolValue = Convert.ToBoolean(field.Value);
                                valueToSet = boolValue ? 1 : 0;
                                _logger.LogInformation($"☑️ Checkbox conversion: '{field.Key}' = '{field.Value}' -> {valueToSet} (int)");
                            }
                            
                            response.SetValue(propertyAlias, valueToSet);
                        }
                    }
                    catch (Exception fieldEx)
                    {
                        _logger.LogWarning(fieldEx, $"Field set failed: {field.Key}");
                    }
                }
                
                // 3. Save - Scope kullanarak notification'ları suppress et (DocumentUrlService timeout'unu önle)
                _logger.LogInformation($"💾 Saving response...");
                var saveStartTime = DateTime.Now;
                
                // ✅ Suppress notifications to prevent DocumentUrlService timeout
                using (var scope = _scopeProvider.CreateScope(autoComplete: true))
                {
                    scope.Notifications.Suppress(); // Notification'ları devre dışı bırak
                    
                    var saveResult = _contentService.Save(response, userId: -1);
                    
                    var saveDuration = DateTime.Now.Subtract(saveStartTime).TotalMilliseconds;
                    _logger.LogWarning($"💾 Save tamamlandı - Süre: {saveDuration}ms, Result: {saveResult.Success}, Response ID: {response.Id}");
                    
                    if (!saveResult.Success)
                    {
                        _logger.LogError($"❌ Save failed! Errors: {string.Join(", ", saveResult.EventMessages?.GetAll().Select(m => m.Message) ?? Array.Empty<string>())}");
                        scope.Complete(); // Transaction'ı tamamla
                        return;
                    }
                    
                    scope.Complete(); // Transaction'ı başarıyla tamamla
                }
                
                // 4. Publish
                await Task.Delay(500);
                _logger.LogInformation($"📤 Publishing response {response.Id}...");
                
                var publishResult = _contentService.SendToPublication(response);
                _logger.LogInformation($"✅ Publish result: {publishResult}");
                
                // 5. Send email - ✅ YENİ: Render edilmiş HTML'i kullan!
                await Task.Delay(200);
                _logger.LogInformation($"📧 Sending emails for response {response.Id}...");
                
                if (!string.IsNullOrEmpty(renderedEmailHtml) && !string.IsNullOrEmpty(emailRecipientsJson))
                {
                    await SendRenderedEmailsJob(formId, renderedEmailHtml, emailRecipientsJson, culture);
                }
                else
                {
                    _logger.LogWarning("⚠️ Email HTML veya recipients boş, mail gönderilmeyecek");
                }
                
                _logger.LogInformation($"✅ HANGFIRE JOB TAMAMLANDI - Response {response.Id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ HANGFIRE JOB FAILED for form {formName}");
                throw; // Hangfire retry için
            }
        }

        // ⚡ HANGFIRE BACKGROUND JOB - Public olmalı, parametreler serializable olmalı
        // Hangfire bu metodu kendi scope'unda çalıştırır, dependency injection otomatik yapılır
        public async Task SavePublishAndSendEmailJob(int responseId, int formId, string rawFormDataJson, string culture, string formDataJson, string formFieldStructureJson, int containerId, string contentTypeAlias)
        {
            try
            {
                _logger.LogWarning($"🔥 HANGFIRE JOB BAŞLADI (Save+Publish+Email)! Response ID: {responseId}");
                
                // 1. Content'i oluştur ve kaydet
                var contentToSave = _contentService.GetById(responseId);
                
                if (contentToSave == null)
                {
                    _logger.LogError($"❌ Content {responseId} bulunamadı!");
                    return;
                }
                
                _logger.LogInformation($"💾 Saving response {responseId}...");
                var saveStartTime = DateTime.Now;
                
                // Save with notifications disabled - userId: -1
                var saveResult = _contentService.Save(contentToSave, userId: -1);
                
                var saveDuration = DateTime.Now.Subtract(saveStartTime).TotalMilliseconds;
                _logger.LogWarning($"💾 Save tamamlandı - Süre: {saveDuration}ms, Result: {saveResult.Success}");
                
                if (!saveResult.Success)
                {
                    _logger.LogError($"❌ Save failed! Errors: {string.Join(", ", saveResult.EventMessages?.GetAll().Select(m => m.Message) ?? Array.Empty<string>())}");
                    return;
                }
                
                // 2. Publish
                await Task.Delay(500); // Küçük gecikme
                _logger.LogInformation($"📤 Publishing response {responseId}...");
                
                var publishResult = _contentService.SendToPublication(contentToSave);
                _logger.LogInformation($"✅ Publish result: {publishResult}");
                
                // 3. Send email
                await Task.Delay(200);
                _logger.LogInformation($"📧 Sending emails for response {responseId}...");
                await SendFormEmailsAsync(formId, rawFormDataJson, culture, formDataJson, formFieldStructureJson);
                
                _logger.LogInformation($"✅ HANGFIRE JOB TAMAMLANDI - Response {responseId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ HANGFIRE JOB FAILED for response {responseId}");
                throw; // Hangfire retry için
            }
        }

        // ⚡ HANGFIRE BACKGROUND JOB - Public olmalı, parametreler serializable olmalı
        // Hangfire bu metodu kendi scope'unda çalıştırır, dependency injection otomatik yapılır
        public async Task PublishAndSendEmailJob(int responseId, int formId, string rawFormDataJson, string culture, string formDataJson, string formFieldStructureJson)
        {
            try
            {
                _logger.LogWarning($"🔥 HANGFIRE JOB BAŞLADI! Response {responseId}");
                
                // Job zaten 5 saniye gecikmeyle başladı, kısa bir güvenlik beklemesi yeterli
                await Task.Delay(500);
                
                // Publish first - GetById ile fresh instance alıyoruz
                _logger.LogInformation($"📤 Publishing response {responseId}...");
                var contentToPublish = _contentService.GetById(responseId);
                
                if (contentToPublish != null)
                {
                    var publishResult = _contentService.SendToPublication(contentToPublish);
                    _logger.LogInformation($"✅ Publish result: {publishResult}");
                }
                else
                {
                    _logger.LogWarning($"⚠️ Content {responseId} bulunamadı, publish atlandı");
                }
                
                // Then send email
                await Task.Delay(200);
                _logger.LogInformation($"📧 Sending emails for response {responseId}...");
                await SendFormEmailsAsync(formId, rawFormDataJson, culture, formDataJson, formFieldStructureJson);
                _logger.LogInformation($"✅ HANGFIRE JOB TAMAMLANDI - Response {responseId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ HANGFIRE JOB FAILED for response {responseId}");
                throw; // Hangfire retry için throw ediyoruz
            }
        }

        // ✅ YENİ HANGFIRE JOB: Render edilmiş HTML'i gönder (HttpContext'e ihtiyaç YOK!)
        // Bu metod background'da çalışır ve sadece SMTP işlemi yapar
        public async Task SendRenderedEmailsJob(int formId, string renderedEmailHtml, string emailRecipientsJson, string culture)
        {
            try
            {
                _logger.LogWarning($"📧 HANGFIRE EMAIL JOB BAŞLADI - Form ID: {formId}");
                
                // Email alıcılarını deserialize et
                var emailRecipients = JsonConvert.DeserializeObject<List<string>>(emailRecipientsJson);
                
                if (emailRecipients == null || !emailRecipients.Any())
                {
                    _logger.LogWarning("⚠️ Email alıcıları bulunamadı");
                    return;
                }
                
                _logger.LogInformation($"📧 Gönderilecek email sayısı: {emailRecipients.Count}");
                
                // Form content'inden SMTP ayarlarını al
                var formContent = _contentService.GetById(formId);
                if (formContent == null)
                {
                    _logger.LogError($"❌ Form content bulunamadı: {formId}");
                    return;
                }
                
                // SMTP settings
                var mailSettings = new MailFormModel
                {
                    SmtpHost = formContent.GetValue<string>("host", culture),
                    SmtpPort = formContent.GetValue<int>("smtpPort", culture),
                    SmtpUsername = formContent.GetValue<string>("smtpUsername", culture),
                    SmtpPassword = formContent.GetValue<string>("smtpPassword", culture),
                    SSL = formContent.GetValue<bool>("ssl", culture),
                    FromEmail = formContent.GetValue<string>("fromEmail", culture),
                    JiraEmail = formContent.GetValue<string>("jiraEmail", culture),
                    BCCEmail = formContent.GetValue<string>("bccEmail", culture),
                };
                
                string emailSubject = formContent.GetValue<string>("mailTitle", culture) ?? $"Form Gönderimi - {formContent.Name}";
                
                _logger.LogInformation($"📧 SMTP Settings - Host: {mailSettings.SmtpHost}, Port: {mailSettings.SmtpPort}, From: {mailSettings.FromEmail}");
                
                // Her bir alıcıya mail gönder (limited concurrency)
                var semaphore = new SemaphoreSlim(2, 2); // Max 2 concurrent
                var emailTasks = new List<Task>();
                
                foreach (var recipient in emailRecipients)
                {
                    var emailTask = Task.Run(async () =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            _logger.LogInformation($"📧 Sending email to: {recipient}");
                            
                            using (var message = new MailMessage(mailSettings.FromEmail, recipient))
                            {
                                message.Subject = emailSubject;
                                message.Body = renderedEmailHtml; // ✅ Hazır HTML kullan!
                                message.IsBodyHtml = true;
                                
                                // CC ve BCC ekle
                                if (!string.IsNullOrEmpty(mailSettings.JiraEmail))
                                {
                                    message.CC.Add(mailSettings.JiraEmail);
                                }
                                
                                if (!string.IsNullOrEmpty(mailSettings.BCCEmail))
                                {
                                    message.Bcc.Add(mailSettings.BCCEmail);
                                }
                                
                                // SMTP gönder
                                ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
                                
                                using (var smtp = new SmtpClient(mailSettings.SmtpHost, mailSettings.SmtpPort))
                                {
                                    smtp.EnableSsl = mailSettings.SSL;
                                    smtp.UseDefaultCredentials = false;
                                    smtp.Credentials = new NetworkCredential(mailSettings.SmtpUsername, mailSettings.SmtpPassword);
                                    smtp.Timeout = 10000;
                                    
                                    await smtp.SendMailAsync(message);
                                }
                            }
                            
                            _logger.LogInformation($"✅ Email gönderildi: {recipient}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"❌ Email gönderimi başarısız: {recipient}");
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });
                    
                    emailTasks.Add(emailTask);
                }
                
                // Tüm emailleri gönder (max 1 dakika timeout)
                try
                {
                    await Task.WhenAll(emailTasks).WaitAsync(TimeSpan.FromMinutes(1));
                    _logger.LogInformation($"✅ Tüm emailler işlendi - Form ID: {formId}");
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("⚠️ Email gönderimi timeout - bazı emailler gönderilememiş olabilir");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ HANGFIRE EMAIL JOB FAILED - Form ID: {formId}");
                throw;
            }
        }

        // Hangfire background job method - public olmalı ve parametreler serializable olmalı
        public async Task SendFormEmailsAsync(
            int formContentId, 
            string rawFormDataJson, 
            string culture, 
            string formDataJson, 
            string formFieldStructureJson)
        {
            try
            {
                // Deserialize parameters - rawFormDataJson zaten JSON string
                JsonDocument rawFormDataDoc = JsonDocument.Parse(rawFormDataJson);
                var formData = JsonConvert.DeserializeObject<Dictionary<string, string>>(formDataJson);
                var formFieldStructure = JsonConvert.DeserializeObject<List<(string Title, string Type, bool Required, string orjalias, string Regex, string RegexMessage, string FormKey)>>(formFieldStructureJson);
                
                // Get form content
                var formContent = _contentService.GetById(formContentId);
                if (formContent == null)
                {
                    _logger.LogError($"Form content bulunamadı: {formContentId}");
                    return;
                }
                
                string emailJsonString = formContent.GetValue<string>("sendMailUser", culture);
                _logger.LogWarning($"📧 Email gönderim başlıyor - sendMailUser değeri: {emailJsonString}");

                if (!string.IsNullOrEmpty(emailJsonString))
                {
                    List<string> emailList = null;
                    
                    // Replace escaped quotes to make it valid JSON
                    emailJsonString = emailJsonString.Replace("\\\"", "\"");
                    _logger.LogWarning($"📧 Temizlenmiş email JSON: {emailJsonString}");

                    // Try to parse as JSON array first
                    try
                    {
                        emailList = System.Text.Json.JsonSerializer.Deserialize<List<string>>(emailJsonString);
                        _logger.LogWarning($"📧 JSON array olarak parse edildi: {emailList?.Count ?? 0} adet email bulundu");
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        // If it's not a JSON array, treat it as a single email address or comma-separated list
                        _logger.LogWarning($"📧 JSON array değil, direkt string olarak işleniyor");
                        
                        if (emailJsonString.Contains(","))
                        {
                            // Comma-separated list
                            emailList = emailJsonString.Split(',').Select(e => e.Trim()).Where(e => !string.IsNullOrEmpty(e)).ToList();
                            _logger.LogWarning($"📧 Virgülle ayrılmış liste: {emailList.Count} adet email bulundu");
                        }
                        else
                        {
                            // Single email address
                            emailList = new List<string> { emailJsonString.Trim() };
                            _logger.LogWarning($"📧 Tek email adresi: {emailJsonString}");
                        }
                    }
                    
                    if (emailList != null && emailList.Count > 0)
                    {
                        _logger.LogWarning($"📧 Gönderilecek email adresleri: {string.Join(", ", emailList)}");
                    }

                    if (emailList != null)
                    {
                        // rawFormDataDoc zaten parse edilmiş - tekrar parse etmeye gerek yok!
                        JsonDocument jsonDoc = rawFormDataDoc;

                        // Send emails with limited concurrency to prevent timeout
                        var semaphore = new SemaphoreSlim(2, 2); // Max 2 concurrent email sends
                        var emailTasks = new List<Task>();

                        foreach (var email in emailList)
                        {
                            if (email == null) continue;

                            var emailTask = Task.Run(async () =>
                            {
                                await semaphore.WaitAsync();
                                try
                                {
                                    string emailToSend = email;

                                    // Check if this is a @Model. reference
                                    if (email.StartsWith("@Model."))
                                    {
                                        // Extract the property name after @Model.
                                        string propertyName = email.Substring(7); // Remove "@Model."

                                        // Try to get that property from the form data
                                        if (jsonDoc != null && jsonDoc.RootElement.TryGetProperty(propertyName, out JsonElement propValue))
                                        {
                                            string modelValue = propValue.GetString();
                                            if (!string.IsNullOrEmpty(modelValue))
                                            {
                                                emailToSend = modelValue;
                                                _logger.LogInformation($"Replaced {email} with {modelValue}");
                                            }
                                        }
                                        else
                                        {
                                            _logger.LogWarning($"Could not find property {propertyName} in form data");
                                            return; // Skip this email if property not found
                                        }
                                    }
                                    
                                    _logger.LogWarning($"📧 Mail gönderiliyor: {emailToSend}");
                                    _logger.LogWarning($"📧 SMTP Ayarları - Host: {formContent.GetValue<string>("host", culture)}, Port: {formContent.GetValue<int>("smtpPort", culture)}, From: {formContent.GetValue<string>("fromEmail", culture)}");
                                    
                                    var mailSettings = new MailFormModel
                                    {
                                        SmtpHost = formContent.GetValue<string>("host", culture),
                                        SmtpPort = formContent.GetValue<int>("smtpPort", culture),
                                        SmtpUsername = formContent.GetValue<string>("smtpUsername", culture),
                                        SmtpPassword = formContent.GetValue<string>("smtpPassword", culture),
                                        SSL = formContent.GetValue<bool>("ssl", culture),
                                        FromEmail = formContent.GetValue<string>("fromEmail", culture),
                                        Sendtoadress = emailToSend,
                                        NotificationEmail = formContent.GetValue<string>("notificationEmail", culture),
                                        JiraEmail = formContent.GetValue<string>("jiraEmail", culture),
                                        BCCEmail = formContent.GetValue<string>("bccEmail", culture),
                                    };

                                    // Convert form data from FormKeys to Property Aliases for mail template
                                    var mailFormData = new Dictionary<string, object>();
                                    
                                    // Process each form field and convert to property alias
                                    foreach (var field in formData)
                                    {
                                        // Find matching field in structure to get property alias
                                        var matchedField = formFieldStructure.FirstOrDefault(f => 
                                            f.FormKey.Equals(field.Key, StringComparison.OrdinalIgnoreCase) ||
                                            NormalizeFieldName(f.Title).Equals(NormalizeFieldName(field.Key), StringComparison.OrdinalIgnoreCase)
                                        );
                                        
                                        if (matchedField.Title != null)
                                        {
                                            string propertyAlias = NormalizeFieldForOutput(matchedField.Title);
                                            mailFormData[propertyAlias] = field.Value;
                                            _logger.LogInformation($"Mail mapping: '{field.Key}' → '{propertyAlias}' = '{field.Value}'");
                                        }
                                        else
                                        {
                                            // Fallback: use field key as-is
                                            mailFormData[field.Key] = field.Value;
                                            _logger.LogWarning($"Mail mapping fallback: '{field.Key}' = '{field.Value}'");
                                        }
                                    }
                                    
                                    string mailDataJson = System.Text.Json.JsonSerializer.Serialize(mailFormData);
                                    
                                    _logger.LogWarning($"📧 Mail template: {formContent.GetValue<string>("mailTemplate", culture)}, Mail title: {formContent.GetValue<string>("mailTitle", culture)}");
                                    
                                    await typeof(ContactFormModel).SendForm(
                                        mailDataJson,
                                        this,
                                        mailSettings,
                                        formContent.GetValue<string>("mailTemplate", culture),
                                        formContent.GetValue<string>("mailTitle", culture)
                                    ).ConfigureAwait(false);
                                    
                                    _logger.LogWarning($"✅ Email başarıyla gönderildi: {emailToSend}");
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, $"❌ Email gönderimi başarısız: {email} - Hata: {ex.Message}");
                                }
                                finally
                                {
                                    semaphore.Release();
                                }
                            });

                            emailTasks.Add(emailTask);
                        }

                        // Wait for all emails with timeout
                        try
                        {
                            await Task.WhenAll(emailTasks).WaitAsync(TimeSpan.FromMinutes(1));
                            _logger.LogInformation("Tüm emailler işlendi");
                        }
                        catch (TimeoutException)
                        {
                            _logger.LogWarning("Email gönderimi timeout - bazı emailler gönderilememiş olabilir");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Email gönderimi iptal edildi");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email gönderim görevi başarısız");
            }
        }

        private List<(string Title, string Type, bool Required, string orjalias, string Regex, string RegexMessage, string FormKey)> ExtractFormFieldStructure(dynamic formsData)
        {
            _logger.LogWarning($"🔥🔥🔥 EXTRACT FORM FIELD STRUCTURE CALLED! 🔥🔥🔥");
            
            var formFieldStructure = new List<(string Title, string Type, bool Required, string orjalias, string Regex, string RegexMessage, string FormKey)>();

            try
            {
                _logger.LogInformation("=== EXTRACT FORM FIELD STRUCTURE DEBUG ===");
                _logger.LogInformation($"🔍 Full formsData structure: {JsonConvert.SerializeObject(formsData, Formatting.Indented)}");
                
                // Check for Umbraco Block Grid structure (Layout + contentData) - case-sensitive check
                // Note: JSON property names are case-sensitive, Umbraco uses "Layout" (capital L)
                bool hasLayout = false;
                bool hasContentData = false;
                
                // Check with both lowercase and uppercase variants
                if (formsData != null)
                {
                    hasLayout = (formsData.Layout != null || formsData.layout != null);
                    hasContentData = (formsData.contentData != null || formsData.ContentData != null);
                }
                
                _logger.LogInformation($"🔍 Structure check - hasLayout: {hasLayout}, hasContentData: {hasContentData}");
                
                if (hasLayout && hasContentData)
                {
                    _logger.LogInformation("✅ Detected Umbraco Block Grid structure (Layout + contentData)");
                    
                    // Get the actual property values (handle both cases)
                    dynamic layoutObj = formsData.Layout ?? formsData.layout;
                    dynamic contentDataObj = formsData.contentData ?? formsData.ContentData;
                    dynamic settingsDataObj = formsData.settingsData ?? formsData.SettingsData;
                    
                    // Create dictionaries to map keys to actual data
                    var contentDataMap = new Dictionary<string, dynamic>();
                    foreach (var item in contentDataObj)
                    {
                        // Check both "key" and "contentKey" properties
                        string key = item?.key?.ToString() ?? item?.contentKey?.ToString();
                        if (!string.IsNullOrEmpty(key))
                        {
                            contentDataMap[key] = item;
                            _logger.LogInformation($"📦 Mapped content item with key: {key}");
                        }
                    }
                    
                    _logger.LogInformation($"📦 Created content map with {contentDataMap.Count} items");
                    
                    // Map settings data
                    var settingsDataMap = new Dictionary<string, dynamic>();
                    if (settingsDataObj != null)
                    {
                        foreach (var item in settingsDataObj)
                        {
                            string key = item?.key?.ToString();
                            if (!string.IsNullOrEmpty(key))
                            {
                                settingsDataMap[key] = item;
                                _logger.LogInformation($"⚙️ Mapped settings item with key: {key}");
                            }
                        }
                        _logger.LogInformation($"⚙️ Created settings map with {settingsDataMap.Count} items");
                    }
                    
                    // Process layout items - handle different layout structures
                    dynamic layoutItems = null;
                    
                    // Try to access Layout.Umbraco.BlockGrid via property access
                    try
                    {
                        if (layoutObj != null)
                        {
                            // Check if layoutObj has "Umbraco.BlockGrid" key (JSON.NET parsed as JObject)
                            if (layoutObj is Newtonsoft.Json.Linq.JObject jobj)
                            {
                                if (jobj["Umbraco.BlockGrid"] != null)
                                {
                                    layoutItems = jobj["Umbraco.BlockGrid"];
                                    _logger.LogInformation("📐 Using Layout['Umbraco.BlockGrid'] (JObject access)");
                                }
                            }
                            // Fallback: try property-style access
                            else if (layoutObj?.Umbraco?.BlockGrid != null)
                            {
                                layoutItems = layoutObj.Umbraco.BlockGrid;
                                _logger.LogInformation("📐 Using Layout.Umbraco.BlockGrid (property access)");
                            }
                        }
                    }
                    catch (Exception layoutEx)
                    {
                        _logger.LogError(layoutEx, "Error accessing layout structure");
                    }
                    
                    if (layoutItems != null)
                    {
                        foreach (var layoutItem in layoutItems)
                        {
                            string contentKey = layoutItem?.contentKey?.ToString();
                            string settingsKey = layoutItem?.settingsKey?.ToString();
                            _logger.LogInformation($"🔑 Processing layout item with contentKey: {contentKey}, settingsKey: {settingsKey}");
                            
                            if (contentDataMap.TryGetValue(contentKey, out var contentItem))
                            {
                                // Try different property names for content type
                                string contentType = contentItem?.contentTypeAlias?.ToString() 
                                    ?? contentItem?.contentTypeKey?.ToString() 
                                    ?? "";
                                    
                                _logger.LogInformation($"🔍 Processing block grid item with contentType: '{contentType}'");
                                
                                // Create a formItem structure similar to the old format
                                dynamic formItem = new System.Dynamic.ExpandoObject();
                                formItem.contentType = contentType;
                                formItem.content = contentItem;
                                
                                // Add settings if available
                                if (!string.IsNullOrEmpty(settingsKey) && settingsDataMap.TryGetValue(settingsKey, out var settingsItem))
                                {
                                    formItem.settings = settingsItem;
                                    _logger.LogInformation($"⚙️ Added settings for field");
                                }
                                else
                                {
                                    // Create empty settings object to prevent errors
                                    formItem.settings = new System.Dynamic.ExpandoObject();
                                }
                                
                                if (contentType == "string" || contentType == "324a96c9-a334-446e-ad6c-bafb2e92c9bf")
                                {
                                    ProcessStringField(formItem, formFieldStructure, "blockgrid");
                                }
                                else if (contentType == "twoColumnsString" || contentType == "51b59683-995b-4762-9f69-e828379ac76e")
                                {
                                    _logger.LogInformation("🎯 FOUND twoColumnsString in Block Grid! Processing nested fields...");
                                    ProcessTwoColumnsString(formItem, formFieldStructure);
                                }
                                else if (contentType == "aprovedCheckbox" || contentType == "a6365911-f294-4c05-b866-440c2a592814")
                                {
                                    // ⚡ aprovedCheckbox artık string gibi işleniyor, sadece checkbox type'ı olarak
                                    _logger.LogInformation("☑️ FOUND aprovedCheckbox in Block Grid! Processing as checkbox field...");
                                    ProcessStringField(formItem, formFieldStructure, "blockgrid-checkbox");
                                }
                                else if (contentType == "button" || contentType == "466960dd-7f1f-4b35-91a7-0b6146a3588b")
                                {
                                    _logger.LogInformation($"⏭️ Skipping button field");
                                }
                                else
                                {
                                    _logger.LogWarning($"❓ Unknown contentType: '{contentType}' - skipping");
                                }
                            }
                            else
                            {
                                _logger.LogWarning($"⚠️ Content key '{contentKey}' not found in content data map");
                            }
                        }
                    }
                    else
                    {
                        _logger.LogError("❌ Layout structure is null or unrecognized!");
                    }
                }
                // OLD APPROACH: Parse JSON forms directly (non-BlockGrid structure)
                else if (formsData?.forms != null)
                {
                    _logger.LogInformation("✅ Processing forms array directly from JSON (non-BlockGrid)...");
                    
                    foreach (var formItem in formsData.forms)
                    {
                        var contentType = formItem?.contentType?.ToString() ?? "";
                        var hasContent = formItem?.content != null;
                        _logger.LogInformation($"🔍 Processing form item with contentType: '{contentType}', hasContent: {hasContent}");
                        
                        // ⚡ Check for string field (by alias OR GUID)
                        if ((contentType == "string" || contentType == "324a96c9-a334-446e-ad6c-bafb2e92c9bf") && formItem?.content != null)
                        {
                            ProcessStringField(formItem, formFieldStructure, "direct");
                        }
                        // ⚡ Check for twoColumnsString (by alias OR GUID)
                        else if ((contentType == "twoColumnsString" || contentType == "51b59683-995b-4762-9f69-e828379ac76e") && formItem?.content != null)
                        {
                            _logger.LogInformation("🎯 FOUND twoColumnsString! Processing nested fields...");
                            ProcessTwoColumnsString(formItem, formFieldStructure);
                        }
                        // ⚡ Check for aprovedCheckbox (by alias OR GUID)
                        else if ((contentType == "aprovedCheckbox" || contentType == "a6365911-f294-4c05-b866-440c2a592814") && formItem?.content != null)
                        {
                            // ⚡ aprovedCheckbox artık string gibi işleniyor, sadece checkbox type'ı olarak
                            _logger.LogInformation("☑️ FOUND aprovedCheckbox (or GUID)! Processing as checkbox field...");
                            ProcessStringField(formItem, formFieldStructure, "direct-checkbox");
                        }
                        // ⚡ Check for button (by alias OR GUID)
                        else if (contentType == "button" || contentType == "466960dd-7f1f-4b35-91a7-0b6146a3588b")
                        {
                            _logger.LogInformation($"⏭️ Skipping button field");
                        }
                        else
                        {
                            _logger.LogWarning($"❓ Unknown or missing content - contentType: '{contentType}', hasContent: {hasContent}");
                        }
                    }
                }
                else
                {
                    _logger.LogError("❌ No forms array or BlockGrid structure found in formsData!");
                }
                
                _logger.LogInformation($"=== EXTRACT COMPLETE: Found {formFieldStructure.Count} total fields ===");
                foreach (var field in formFieldStructure)
                {
                    _logger.LogInformation($"Final field: {field.Title} -> FormKey: {field.FormKey}, Type: {field.Type}, Required: {field.Required}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Form field structure extraction failed");
            }

            return formFieldStructure;
        }

        // NEW: Process individual string field
        private void ProcessStringField(dynamic formItem, 
            List<(string Title, string Type, bool Required, string orjalias, string Regex, string RegexMessage, string FormKey)> formFieldStructure,
            string context)
        {
            try
            {
                var content = formItem?.content;
                var settings = formItem?.settings;
                
                // Debug: Log content type info
                var contentTypeAlias = content?.contentTypeAlias?.ToString() ?? "N/A";
                _logger.LogInformation($"🔍 ProcessStringField called - context: '{context}', contentTypeAlias: '{contentTypeAlias}'");
                
                // ⚡ Check if this is a checkbox field based on:
                // 1. Context contains "checkbox"
                // 2. contentTypeAlias is "aprovedCheckbox"
                // 3. contentTypeAlias is the aprovedCheckbox GUID
                bool isCheckbox = context.Contains("checkbox") || 
                                 contentTypeAlias == "aprovedCheckbox" ||
                                 contentTypeAlias == "a6365911-f294-4c05-b866-440c2a592814";
                
                _logger.LogInformation($"   isCheckbox detection - context: '{context}', contentTypeAlias: '{contentTypeAlias}', RESULT: {isCheckbox}");
                
                // Extract title from content.values array or direct property
                string title = "";
                string formKey = "";
                
                // Try to get from values array (Block Grid format)
                if (content?.values != null)
                {
                    foreach (var valueItem in content.values)
                    {
                        string alias = valueItem?.alias?.ToString() ?? "";
                        if (alias == "title")
                        {
                            title = valueItem?.value?.ToString() ?? "";
                        }
                        else if (alias == "FormKey")
                        {
                            formKey = valueItem?.value?.ToString() ?? "";
                        }
                    }
                }
                
                // Fallback: try direct property access (old format)
                if (string.IsNullOrEmpty(title))
                {
                    title = content?.title?.ToString() ?? "";
                }
                if (string.IsNullOrEmpty(formKey))
                {
                    formKey = content?.FormKey?.ToString() ?? "";
                }
                
                if (string.IsNullOrEmpty(title))
                {
                    _logger.LogWarning($"⚠️ Empty title in string field ({context})");
                    return;
                }
                
                _logger.LogInformation($"📝 Processing string field: '{title}' in {context} (isCheckbox: {isCheckbox})");
                
                // Get field settings
                string type = isCheckbox ? "checkbox" : "text";  // ⚡ Default to checkbox if detected
                bool required = false;
                string regex = "";
                string regexMessage = "";
                string orjalias = "string"; // ⚡ Default value, will be changed for checkbox
                
                // ⚡ FOR CHECKBOX FIELDS: Try to extract from direct properties first (Block Grid format)
                if (isCheckbox && content != null)
                {
                    _logger.LogInformation($"☑️ Checkbox field detected - checking for direct properties");
                    
                    // Try direct property access (Block Grid contentItem format)
                    try
                    {
                        var requiredValue = content.required?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(requiredValue))
                        {
                            required = requiredValue == "True" || requiredValue == "true" || requiredValue == "1" || requiredValue == "true";
                            _logger.LogInformation($"☑️ Checkbox required (direct): {required}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"⚠️ Could not read checkbox required (direct): {ex.Message}");
                    }
                    
                    // Use provided FormKey or generate one
                    if (string.IsNullOrEmpty(formKey))
                    {
                        formKey = GenerateFormKey(title);
                    }
                    
                    orjalias = "aprovedCheckbox"; // ⚡ Set checkbox orjalias
                    
                    formFieldStructure.Add((title, type, required, orjalias, regex, regexMessage, formKey));
                    _logger.LogInformation($"✅ Added CHECKBOX field ({context}): {title} -> FormKey: {formKey}, Type: {type}, Required: {required}");
                    return; // ⚡ Early return for checkbox
                }
                
                // FOR NON-CHECKBOX FIELDS: Continue with normal processing
                if (settings != null)
                {
                    string settingsType = "";
                    string settingsRequired = "";
                    string settingsRegex = "";
                    string settingsRegexMessage = "";
                    
                    // Try to get from values array (Block Grid format)
                    if (settings?.values != null)
                    {
                        foreach (var valueItem in settings.values)
                        {
                            string alias = valueItem?.alias?.ToString() ?? "";
                            string value = valueItem?.value?.ToString() ?? "";
                            
                            if (alias == "type")
                                settingsType = value;
                            else if (alias == "required")
                                settingsRequired = value;
                            else if (alias == "regex")
                                settingsRegex = value;
                            else if (alias == "regexMesage" || alias == "regexMessage")
                                settingsRegexMessage = value;
                        }
                    }
                    else
                    {
                        // Fallback: try direct property access (old format)
                        settingsType = settings?.type?.ToString() ?? "";
                        settingsRequired = settings?.required?.ToString() ?? "";
                        settingsRegex = settings?.regex?.ToString() ?? "";
                        settingsRegexMessage = settings?.regexMesage?.ToString() ?? "";
                    }
                    
                    // Parse field type from settings (sadece checkbox değilse)
                    if (!isCheckbox && !string.IsNullOrEmpty(settingsType))
                    {
                        if (settingsType.Contains("Email", StringComparison.OrdinalIgnoreCase))
                            type = "email";
                        else if (settingsType.Contains("Phone", StringComparison.OrdinalIgnoreCase))
                            type = "tel";
                        else if (settingsType.Contains("Country", StringComparison.OrdinalIgnoreCase))
                            type = "dropdown";
                        else if (settingsType.Contains("City", StringComparison.OrdinalIgnoreCase) ||
                                 settingsType.Contains("Ctiy", StringComparison.OrdinalIgnoreCase))
                            type = "dropdown";
                        else if (settingsType.Contains("Textarea", StringComparison.OrdinalIgnoreCase))
                            type = "textarea";
                        else if (settingsType.Contains("Date", StringComparison.OrdinalIgnoreCase))
                            type = "date";
                        else if (settingsType.Contains("Number", StringComparison.OrdinalIgnoreCase))
                            type = "number";
                    }
                    
                    // Parse required field
                    if (!string.IsNullOrEmpty(settingsRequired))
                    {
                        required = settingsRequired == "True" || 
                                  settingsRequired == "true" ||
                                  settingsRequired == "1";
                    }
                    
                    regex = settingsRegex;
                    regexMessage = settingsRegexMessage;
                }
                
                // Determine type from title if not set by settings (sadece checkbox değilse)
                if (!isCheckbox && type == "text" && !string.IsNullOrEmpty(title))
                {
                    var titleLower = title.ToLowerInvariant();
                    if (titleLower.Contains("email") || titleLower.Contains("e-posta"))
                        type = "email";
                    else if (titleLower.Contains("phone") || titleLower.Contains("telefon") || titleLower.Contains("gsm"))
                        type = "tel";
                    else if (titleLower.Contains("country") || titleLower.Contains("ülke"))
                        type = "dropdown";
                    else if (titleLower.Contains("city") || titleLower.Contains("şehir") || titleLower.Contains("ctiy"))
                        type = "dropdown";
                    else if (titleLower.Contains("tarih") || titleLower.Contains("date"))
                        type = "date";
                    else if (titleLower.Contains("mesaj") || titleLower.Contains("açıklama"))
                        type = "textarea";
                    else if (titleLower.Contains("sector") || titleLower.Contains("sektör"))
                        type = "dropdown";
                    else if (titleLower.Contains("trader") || titleLower.Contains("ticaret"))
                        type = "text";
                }
                
                // Use provided FormKey or generate one
                if (string.IsNullOrEmpty(formKey))
                {
                    formKey = GenerateFormKey(title);
                }
                
                // ⚡ For non-checkbox fields, orjalias is "string" (already set at top)
                // orjalias is already "string" from declaration above
                
                formFieldStructure.Add((title, type, required, orjalias, regex, regexMessage, formKey));
                _logger.LogInformation($"✅ Added string field ({context}): {title} -> FormKey: {formKey}, Type: {type}, Required: {required}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing string field in {context}: {ex.Message}");
            }
        }

        // NEW: Process twoColumnsString with nested fields
        private void ProcessTwoColumnsString(dynamic formItem, 
            List<(string Title, string Type, bool Required, string orjalias, string Regex, string RegexMessage, string FormKey)> formFieldStructure)
        {
            try
            {
                var content = formItem?.content;
                
                _logger.LogInformation($"🔍 ProcessTwoColumnsString - Content structure: {JsonConvert.SerializeObject(content, Formatting.Indented)}");
                
                // Extract oneBlock and twoBlock from content.values array (Block Grid format)
                string oneBlockJson = null;
                string twoBlockJson = null;
                
                if (content?.values != null)
                {
                    foreach (var valueItem in content.values)
                    {
                        string alias = valueItem?.alias?.ToString() ?? "";
                        string value = valueItem?.value?.ToString() ?? "";
                        
                        if (alias == "oneBlock")
                            oneBlockJson = value;
                        else if (alias == "twoBlock")
                            twoBlockJson = value;
                    }
                }
                
                // Process oneBlock
                if (!string.IsNullOrEmpty(oneBlockJson))
                {
                    _logger.LogInformation($"� Parsing oneBlock JSON");
                    try
                    {
                        var oneBlockData = JsonConvert.DeserializeObject<dynamic>(oneBlockJson);
                        ProcessNestedBlockGrid(oneBlockData, formFieldStructure, "oneBlock");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error parsing oneBlock JSON");
                    }
                }
                else
                {
                    _logger.LogWarning("⚠️ No oneBlock found in twoColumnsString");
                }
                
                // Process twoBlock
                if (!string.IsNullOrEmpty(twoBlockJson))
                {
                    _logger.LogInformation($"📦 Parsing twoBlock JSON");
                    try
                    {
                        var twoBlockData = JsonConvert.DeserializeObject<dynamic>(twoBlockJson);
                        ProcessNestedBlockGrid(twoBlockData, formFieldStructure, "twoBlock");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error parsing twoBlock JSON");
                    }
                }
                else
                {
                    _logger.LogWarning("⚠️ No twoBlock found in twoColumnsString");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing twoColumnsString: {ex.Message}");
            }
        }
        
        // NEW: Process nested Block Grid structure (for oneBlock/twoBlock inside twoColumnsString)
        private void ProcessNestedBlockGrid(dynamic blockGridData,
            List<(string Title, string Type, bool Required, string orjalias, string Regex, string RegexMessage, string FormKey)> formFieldStructure,
            string context)
        {
            try
            {
                _logger.LogInformation($"🔍 ProcessNestedBlockGrid for {context}");
                
                // This structure is the same as the main Block Grid: contentData, settingsData, Layout
                var contentDataMap = new Dictionary<string, dynamic>();
                var settingsDataMap = new Dictionary<string, dynamic>();
                
                // Map contentData
                if (blockGridData?.contentData != null)
                {
                    foreach (var item in blockGridData.contentData)
                    {
                        string key = item?.key?.ToString();
                        if (!string.IsNullOrEmpty(key))
                        {
                            contentDataMap[key] = item;
                            _logger.LogInformation($"  📦 Mapped nested content with key: {key}");
                        }
                    }
                }
                
                // Map settingsData
                if (blockGridData?.settingsData != null)
                {
                    foreach (var item in blockGridData.settingsData)
                    {
                        string key = item?.key?.ToString();
                        if (!string.IsNullOrEmpty(key))
                        {
                            settingsDataMap[key] = item;
                            _logger.LogInformation($"  ⚙️ Mapped nested settings with key: {key}");
                        }
                    }
                }
                
                // Process Layout items
                dynamic layoutItems = null;
                if (blockGridData?.Layout != null)
                {
                    var layoutObj = blockGridData.Layout;
                    
                    if (layoutObj is Newtonsoft.Json.Linq.JObject jobj && jobj["Umbraco.BlockGrid"] != null)
                    {
                        layoutItems = jobj["Umbraco.BlockGrid"];
                        _logger.LogInformation($"  � Using nested Layout['Umbraco.BlockGrid']");
                    }
                }
                
                if (layoutItems != null)
                {
                    foreach (var layoutItem in layoutItems)
                    {
                        string contentKey = layoutItem?.contentKey?.ToString();
                        string settingsKey = layoutItem?.settingsKey?.ToString();
                        
                        if (contentDataMap.TryGetValue(contentKey, out var contentItem))
                        {
                            string contentType = contentItem?.contentTypeKey?.ToString() ?? "";
                            _logger.LogInformation($"  🔍 Processing nested item with contentType: '{contentType}'");
                            
                            // Create formItem
                            dynamic formItem = new System.Dynamic.ExpandoObject();
                            formItem.contentType = contentType;
                            formItem.content = contentItem;
                            
                            // Add settings if available
                            if (!string.IsNullOrEmpty(settingsKey) && settingsDataMap.TryGetValue(settingsKey, out var settingsItem))
                            {
                                formItem.settings = settingsItem;
                            }
                            else
                            {
                                formItem.settings = new System.Dynamic.ExpandoObject();
                            }
                            
                            // Process based on content type (using GUID since it's nested)
                            if (contentType == "324a96c9-a334-446e-ad6c-bafb2e92c9bf") // string type GUID
                            {
                                ProcessStringField(formItem, formFieldStructure, context);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing nested block grid for {context}: {ex.Message}");
            }
        }

        // NEW: Process checkbox field
        private void ProcessCheckboxField(dynamic formItem, 
            List<(string Title, string Type, bool Required, string orjalias, string Regex, string RegexMessage, string FormKey)> formFieldStructure,
            string context)
        {
            try
            {
                var content = formItem?.content;
                
                // Extract title and required from content.values array or direct property
                string title = "";
                string formKey = "";
                string requiredValue = "";
                
                // Try to get from values array (Block Grid format)
                if (content?.values != null)
                {
                    foreach (var valueItem in content.values)
                    {
                        string alias = valueItem?.alias?.ToString() ?? "";
                        string value = valueItem?.value?.ToString() ?? "";
                        
                        if (alias == "title")
                            title = value;
                        else if (alias == "FormKey")
                            formKey = value;
                        else if (alias == "required")
                            requiredValue = value;
                    }
                }
                
                // Fallback: try direct property access (old format)
                if (string.IsNullOrEmpty(title))
                    title = content?.title?.ToString() ?? "";
                if (string.IsNullOrEmpty(formKey))
                    formKey = content?.FormKey?.ToString() ?? "";
                if (string.IsNullOrEmpty(requiredValue))
                    requiredValue = content?.required?.ToString() ?? "";
                
                if (string.IsNullOrEmpty(title))
                {
                    _logger.LogWarning($"⚠️ Empty title in checkbox field ({context})");
                    return;
                }
                
                _logger.LogInformation($"☑️ Processing checkbox field: '{title}' in {context}");
                
                bool required = requiredValue == "True" || 
                               requiredValue == "true" ||
                               requiredValue == "1";
                
                // Use provided FormKey or generate one
                if (string.IsNullOrEmpty(formKey))
                {
                    formKey = GenerateFormKey(title);
                }
                
                formFieldStructure.Add((title, "checkbox", required, "aprovedCheckbox", "", "", formKey));
                _logger.LogInformation($"✅ Added checkbox field ({context}): {title} -> FormKey: {formKey}, Required: {required}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing checkbox field in {context}: {ex.Message}");
            }
        }

        // Method to validate form data against form definition
        private Dictionary<string, string> ValidateFormData(Dictionary<string, string> formData, List<(string Title, string Type, bool Required, string orjalias, string Regex, string RegexMessage, string FormKey)> formFieldStructure)
        {
            var errors = new Dictionary<string, string>();
            var processedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            // Debug: Log form data keys
            _logger.LogInformation($"=== FORM VALIDATION DEBUG ===");
            _logger.LogInformation($"Received form data keys: {string.Join(", ", formData.Keys)}");
            _logger.LogInformation($"Form data keys (lowercase): {string.Join(", ", formData.Keys.Select(k => k.ToLowerInvariant()))}");
            
            // Debug: Log form field structure
            _logger.LogInformation($"Form field structure:");
            foreach (var field in formFieldStructure)
            {
                _logger.LogInformation($"  - Title: '{field.Title}', FormKey: '{field.FormKey}', Type: '{field.Type}', Required: {field.Required}");
            }
            
            // Create a map of all valid field names from the structure
            var validFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in formFieldStructure)
            {
                validFieldNames.Add(field.Title);
                validFieldNames.Add(NormalizeFieldName(field.Title));
                validFieldNames.Add(NormalizeFieldForOutput(field.Title));
                validFieldNames.Add(field.FormKey); // Add FormKey support
            }
            
            // First check each form field in the structure
            foreach (var field in formFieldStructure)
            {
                var originalTitle = field.Title;
                var fieldAlias = NormalizeFieldName(originalTitle);
                var formKey = field.FormKey;
                processedFields.Add(originalTitle);
                processedFields.Add(fieldAlias);
                processedFields.Add(NormalizeFieldForOutput(originalTitle));
                processedFields.Add(formKey);

                // Check if required field is missing or empty
                if (field.Required)
                {
                    bool fieldExists = false;
                    bool hasValue = false;
                    string foundKey = null;
                    
                    // Try different ways to match field names including FormKey (prioritize FormKey)
                    foreach (var key in formData.Keys)
                    {
                        // Primary match: Convert form key to lowercase and compare with FormKey
                        var keyLower = key.ToLowerInvariant();
                        var fieldFormKeyLower = formKey.ToLowerInvariant();
                        
                        if (keyLower.Equals(fieldFormKeyLower, StringComparison.OrdinalIgnoreCase) ||
                            key.Equals(originalTitle, StringComparison.OrdinalIgnoreCase) || 
                            IsFieldMatch(fieldAlias, key) ||
                            key.Equals(NormalizeFieldForOutput(originalTitle), StringComparison.OrdinalIgnoreCase))
                        {
                            fieldExists = true;
                            foundKey = key;
                            hasValue = !string.IsNullOrWhiteSpace(formData[key]);
                            break;
                        }
                    }
                    
                    if (!fieldExists || !hasValue)
                    {
                        errors[NormalizeFieldForOutput(originalTitle)] = $"'{originalTitle}' alanı zorunludur.";
                        continue;
                    }
                }

                // If field is provided, validate its format
                string matchedKey = null;
                string fieldValue = null;
                
                foreach (var key in formData.Keys)
                {
                    // Primary match: Convert form key to lowercase and compare with FormKey
                    var keyLower = key.ToLowerInvariant();
                    var fieldFormKeyLower = formKey.ToLowerInvariant();
                    
                    if (keyLower.Equals(fieldFormKeyLower, StringComparison.OrdinalIgnoreCase) ||
                        key.Equals(originalTitle, StringComparison.OrdinalIgnoreCase) || 
                        IsFieldMatch(fieldAlias, key) ||
                        key.Equals(NormalizeFieldForOutput(originalTitle), StringComparison.OrdinalIgnoreCase))
                    {
                        matchedKey = key;
                        fieldValue = formData[key];
                        break;
                    }
                }
                
                if (matchedKey != null && !string.IsNullOrWhiteSpace(fieldValue))
                {
                    string value = fieldValue;
                    
                    // For checkbox/boolean fields, normalize the value
                    if (field.Type.Equals("checkbox", StringComparison.OrdinalIgnoreCase))
                    {
                        // Convert value to valid boolean format
                        string lowerValue = value.ToLowerInvariant();
                        if (lowerValue == "true" || lowerValue == "1" || lowerValue == "yes")
                        {
                            formData[matchedKey] = "true";
                        }
                        else
                        {
                            formData[matchedKey] = "false";
                        }
                    }
                    // First check custom regex if provided
                    else if (!string.IsNullOrEmpty(field.Regex))
                    {
                        try 
                        {
                            if (!Regex.IsMatch(value, field.Regex))
                            {
                                // Use custom regex message if available, otherwise use default
                                string errorMessage = !string.IsNullOrEmpty(field.RegexMessage) 
                                    ? field.RegexMessage 
                                    : $"'{originalTitle}' geçerli bir formatta olmalıdır.";
                                
                                errors[NormalizeFieldForOutput(originalTitle)] = errorMessage;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Invalid regex pattern: {field.Regex}");
                            // Fall back to default patterns if custom regex fails
                            ValidateWithDefaultPatterns(errors, field, NormalizeFieldForOutput(originalTitle), value);
                        }
                    }
                    // Otherwise use default patterns
                    else
                    {
                        ValidateWithDefaultPatterns(errors, field, NormalizeFieldForOutput(originalTitle), value);
                    }
                }
            }

            // Check for fields in form data that don't exist in the form definition
            // Skip internal System.Text.Json fields like ValueKind
            var systemFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ValueKind" };
            
            foreach (var formKey in formData.Keys)
            {
                if (systemFields.Contains(formKey))
                    continue;
                    
                bool found = false;
                
                // Check against all possible variations of field names including FormKey
                foreach (var field in formFieldStructure)
                {
                    // Primary match: Convert form key to lowercase and compare with FormKey
                    var formKeyLower = formKey.ToLowerInvariant();
                    var fieldFormKeyLower = field.FormKey.ToLowerInvariant();
                    
                    if (formKeyLower.Equals(fieldFormKeyLower, StringComparison.OrdinalIgnoreCase) ||
                        formKey.Equals(field.Title, StringComparison.OrdinalIgnoreCase) ||
                        IsFieldMatch(NormalizeFieldName(field.Title), formKey) ||
                        formKey.Equals(NormalizeFieldForOutput(field.Title), StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        _logger.LogInformation($"Field match found: '{formKey}' matches '{field.Title}' (FormKey: '{field.FormKey}')");
                        break;
                    }
                }
                
                if (!found)
                {
                    _logger.LogWarning($"Field '{formKey}' not found in form structure. Available fields: {string.Join(", ", formFieldStructure.Select(f => $"{f.Title} (FormKey: {f.FormKey})"))}");
                    // Add validation error for unmatched fields
                    errors[formKey] = $"'{formKey}' form tanımında bulunmayan bir alan. Geçerli alanlar: {string.Join(", ", formFieldStructure.Select(f => f.FormKey))}";
                }
            }

            return errors;
        }

        // Helper method to normalize field names consistently
        private string NormalizeFieldName(string fieldName)
        {
            // Convert field name to consistent format for comparison
            return fieldName.ToLowerInvariant()
                .Replace(" ", "")
                .Replace("-", "")
                .Replace("_", "")
                .Replace("/", "")
                .Replace("\\", "")
                .Replace("ı", "i")
                .Replace("ğ", "g")
                .Replace("ü", "u")
                .Replace("ş", "s")
                .Replace("ö", "o")
                .Replace("ç", "c")
                .Replace("İ", "i")
                .Replace("Ğ", "g")
                .Replace("Ü", "u")
                .Replace("Ş", "s")
                .Replace("Ö", "o")
                .Replace("Ç", "c");
        }

        // Helper method to normalize field names for error output
        private string NormalizeFieldForOutput(string fieldName)
        {
            // First convert Turkish characters to English equivalents
            var converted = fieldName
                .Replace("ı", "i").Replace("İ", "I")
                .Replace("ğ", "g").Replace("Ğ", "G")
                .Replace("ü", "u").Replace("Ü", "U")
                .Replace("ş", "s").Replace("Ş", "S")
                .Replace("ö", "o").Replace("Ö", "O")
                .Replace("ç", "c").Replace("Ç", "C");
                
            // For property aliases, use PascalCase without spaces
            return string.Join("", 
                converted.Split(new[] { ' ', '-', '_', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word.Substring(1))
            );
        }
        
        // Helper method to check if field names match, considering different normalization methods
        private bool IsFieldMatch(string normalizedField, string inputField)
        {
            // Also normalize Turkish characters in the input field
            var normalizedInput = NormalizeFieldName(inputField);
            return normalizedField.Equals(normalizedInput, StringComparison.OrdinalIgnoreCase);
        }
        
        // Form alanı tipini belirle
        private string DetermineFieldType(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
                return "text";

            var lowerName = fieldName.ToLowerInvariant();

            if (lowerName.Contains("email") || lowerName.Contains("e-posta"))
                return "email";
            else if (lowerName.Contains("telefon") || lowerName.Contains("phone") || lowerName.Contains("gsm"))
                return "tel";
            else if (lowerName.Contains("mesaj") || lowerName.Contains("açıklama") || lowerName.Contains("yorum"))
                return "textarea";
            else if (lowerName.Contains("tarih") || lowerName.Contains("date"))
                return "date";
            else if (lowerName.Contains("onay") || lowerName.Contains("kabul") || lowerName.Contains("izin"))
                return "checkbox";

            return "text";
        }

        // Helper method to create PropertyType with correct parameters
        private PropertyType CreatePropertyType(int dataTypeId, string alias, string name)
        {
            try
            {
                // Get the DataType definition by ID first
                var dataType = _dataTypeService.GetDataType(dataTypeId);
                if (dataType == null)
                    throw new InvalidOperationException($"DataType bulunamadı: ID {dataTypeId}");
                    
                // Create the PropertyType using the constructor that takes IShortStringHelper, IDataType and alias
                return new PropertyType(_shortStringHelper, dataType, alias)
                {
                    Name = name,
                    Description = $"Form alanı: @Model[\"{ alias }\"]",
                    Mandatory = false
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"PropertyType oluşturulurken hata: {alias}");
                return null;
            }
        }

        // Helper method to validate with default patterns - fixed implementation
        private void ValidateWithDefaultPatterns(
            Dictionary<string, string> errors, 
            (string Title, string Type, bool Required, string orjalias, string Regex, string RegexMessage, string FormKey) field, 
            string fieldOutputName, 
            string value)
        {
            if (_fieldTypePatterns.ContainsKey(field.Type))
            {
                // Special handling for checkbox/boolean type
                if (field.Type.Equals("checkbox", StringComparison.OrdinalIgnoreCase))
                {
                    // Case-insensitive comparison for boolean values
                    string lowerValue = value.ToLowerInvariant();
                    if (!Regex.IsMatch(lowerValue, _fieldTypePatterns[field.Type], RegexOptions.IgnoreCase))
                    {
                        errors[fieldOutputName] = $"'{field.Title}' için geçerli bir değer giriniz (true/false).";
                    }
                }
                // ⚡ Special handling for phone numbers - flexible format validation
                else if (field.Type.Equals("tel", StringComparison.OrdinalIgnoreCase))
                {
                    var pattern = _fieldTypePatterns[field.Type];
                    if (!Regex.IsMatch(value, pattern))
                    {
                        errors[fieldOutputName] = GetValidationErrorMessage(field.Type, field.Title);
                    }
                    else
                    {
                        // Additional check: Must have at least 10 digits
                        var digitsOnly = Regex.Replace(value, @"[^\d]", "");
                        if (digitsOnly.Length < 10 || digitsOnly.Length > 15)
                        {
                            errors[fieldOutputName] = $"'{field.Title}' en az 10, en fazla 15 rakam içermelidir.";
                        }
                    }
                }
                else
                {
                    // Other field types
                    var pattern = _fieldTypePatterns[field.Type];
                    if (!Regex.IsMatch(value, pattern))
                    {
                        errors[fieldOutputName] = GetValidationErrorMessage(field.Type, field.Title);
                    }
                }
            }
        }

        // Fixed GetDataTypeByEditorAlias method implementation
        private IDataType GetDataTypeByEditorAlias(string editorAlias)
        {
            var dataTypes = _dataTypeService.GetAll();
            var dataType = dataTypes.FirstOrDefault(dt => dt.EditorAlias == editorAlias);
            
            if (dataType == null)
            {
                _logger.LogWarning($"DataType bulunamadı: {editorAlias}, fallback olarak metin kutusu kullanılıyor.");
                // Attempt to find a text box as fallback
                dataType = dataTypes.FirstOrDefault(dt => dt.EditorAlias == "Umbraco.TextBox") 
                    ?? dataTypes.FirstOrDefault(); // Last resort, take any data type
                
                if (dataType == null)
                    throw new InvalidOperationException($"Hiçbir DataType bulunamadı. Umbraco yükleme sorunu olabilir.");
            }
            
            return dataType;
        }

        // Add this method to create a property type safely
        private PropertyType CreatePropertyTypeWithDataType(IDataType dataType, string alias, string name)
        {
            if (dataType == null)
                throw new ArgumentNullException(nameof(dataType), "DataType null olamaz.");
                
            return new PropertyType(_shortStringHelper, dataType, alias)
            {
                Name = name,
                Description = $"Mail Template: @Model[\"{ alias }\"]",
                Mandatory = false,
                DataTypeId = dataType.Id // Access the Id property correctly
            };
        }

        // Generate appropriate validation error message based on field type
        private string GetValidationErrorMessage(string fieldType, string fieldName)
        {
            switch (fieldType.ToLowerInvariant())
            {
                case "email":
                    return $"'{fieldName}' geçerli bir e-posta adresi olmalıdır.";
                case "tel":
                    return $"'{fieldName}' geçerli bir telefon numarası olmalıdır.";
                case "date":
                    return $"'{fieldName}' geçerli bir tarih (YYYY-AA-GG) olmalıdır.";
                case "dropdown":
                    return $"'{fieldName}' için listeden bir seçim yapmalısınız.";
                case "checkbox":
                    return $"'{fieldName}' için geçerli bir değer giriniz (true/false).";
                default:
                    return $"'{fieldName}' geçerli bir değer değil.";
            }
        }

        // Add the GetDataTypeForFieldType method to map field types to Umbraco data types
        private IDataType GetDataTypeForFieldType(string fieldType)
        {
            string editorAlias;
            
            switch (fieldType.ToLowerInvariant())
            {
                case "email":
                    editorAlias = "Umbraco.TextBox"; // Email validation is handled in our code
                    break;
                case "tel":
                    editorAlias = "Umbraco.TextBox"; // Phone validation is handled in our code
                    break;
                case "textarea":
                    editorAlias = "Umbraco.TextArea";
                    break;
                case "dropdown":
                    editorAlias = "Umbraco.TextBox";
                    break;
                case "checkbox":
                    editorAlias = "Umbraco.TrueFalse";
                    break;
                case "number":
                    editorAlias = "Umbraco.Integer";
                    break;
                case "url":
                    editorAlias = "Umbraco.TextBox"; // URL validation is handled in our code
                    break;
                case "date":
                    editorAlias = "Umbraco.DateTime";
                    break;
                case "datetime":
                    editorAlias = "Umbraco.DateTime";
                    break;
                default:
                    editorAlias = "Umbraco.TextBox";
                    break;
            }
            
            return GetDataTypeByEditorAlias(editorAlias);
        }

        // OPTIMIZED: Content type'ları ve container'ı kontrol et/oluştur - sadece gerektiğinde + Cache
        private (IContent container, IContentType responseContentType) EnsureContentTypesAndContainerExist(
            int formId, 
            string cultureCode, 
            IContent formContent, 
            List<(string Title, string Type, bool Required, string orjalias, string Regex, string RegexMessage, string FormKey)> formFieldStructure,
            Dictionary<string, string> formData)
        {
            try
            {
                _logger.LogInformation("Content type hazırlığı başlıyor...");

                // 1. Container Content Type - cache kontrolü
                var containerAlias = "formContainer";
                IContentType containerContentType = null;
                
                lock (_cacheLock)
                {
                    if (_contentTypeCache.ContainsKey(containerAlias))
                    {
                        _logger.LogInformation("Container content type cache'den alınıyor");
                        containerContentType = _contentTypeService.Get(containerAlias);
                    }
                }

                if (containerContentType == null)
                {
                    containerContentType = _contentTypeService.Get(containerAlias);
                    if (containerContentType == null)
                    {
                        _logger.LogInformation("Container content type oluşturuluyor...");
                        containerContentType = new ContentType(_shortStringHelper, -1)
                        {
                            Alias = containerAlias,
                            Name = "Form Yanıtları Klasörü",
                            Icon = "icon-folder",
                            AllowedAsRoot = true,
                        };
                        containerContentType.AddPropertyGroup("formContainerSettings", "Ayarlar");
                        _contentTypeService.Save(containerContentType);
                        _logger.LogInformation("FormContainer content type oluşturuldu (tek seferlik)");
                    }
                    
                    // Cache'e ekle
                    lock (_cacheLock)
                    {
                        _contentTypeCache[containerAlias] = true;
                    }
                }
                else
                {
                    _logger.LogInformation("Container content type zaten mevcut - cache hit");
                }

                // 2. Container Content - cache kontrolü
                IContent container = null;
                
                lock (_cacheLock)
                {
                    if (_containerCache.ContainsKey(-1)) // -1 = root level container
                    {
                        _logger.LogInformation("Container content cache'den kontrol ediliyor");
                        container = _contentService.GetRootContent()
                            .FirstOrDefault(c => c.ContentType.Alias == containerAlias);
                    }
                }

                if (container == null)
                {
                    container = _contentService.GetRootContent()
                        .FirstOrDefault(c => c.ContentType.Alias == containerAlias);
                        
                    if (container == null)
                    {
                        _logger.LogInformation("Container content oluşturuluyor...");
                        container = _contentService.Create("Form Yanıtları", -1, containerAlias);
                        if (container == null)
                        {
                            _logger.LogError("Form yanıtları container'ı oluşturulamadı");
                            return (null, null);
                        }
                        
                        _contentService.Save(container);
                        _logger.LogInformation($"Form yanıtları container'ı oluşturuldu (tek seferlik). ID: {container.Id}");
                    }
                    
                    // Cache'e ekle
                    lock (_cacheLock)
                    {
                        _containerCache[-1] = true;
                    }
                }
                else
                {
                    _logger.LogInformation($"Container content zaten mevcut - cache hit. ID: {container.Id}");
                }

                // 3. Response Content Type - cache kontrolü
                var responseAlias = $"formResponse{formId}{cultureCode}";
                IContentType responseContentType = null;
                
                lock (_cacheLock)
                {
                    if (_contentTypeCache.ContainsKey(responseAlias))
                    {
                        _logger.LogInformation($"Response content type cache'den alınıyor: {responseAlias}");
                        responseContentType = _contentTypeService.Get(responseAlias);
                    }
                }

                bool contentTypeNeedsUpdate = false;
                
                if (responseContentType == null)
                {
                    responseContentType = _contentTypeService.Get(responseAlias);
                    
                    if (responseContentType == null)
                    {
                        _logger.LogInformation($"Response content type oluşturuluyor: {responseAlias}");
                        // Content type yoksa oluştur
                        responseContentType = new ContentType(_shortStringHelper, -1)
                        {
                            Alias = responseAlias,
                            Name = $"{formContent.Name} {cultureCode}",
                            Icon = "icon-document",
                            AllowedAsRoot = false
                        };
                        
                        responseContentType.AddPropertyGroup("formFields", "Form Alanları");
                        _contentTypeService.Save(responseContentType);
                        contentTypeNeedsUpdate = true;
                        
                        _logger.LogInformation($"Response content type oluşturuldu: {responseAlias}");
                    }
                    
                    // Cache'e ekle
                    lock (_cacheLock)
                    {
                        _contentTypeCache[responseAlias] = true;
                    }
                }
                else
                {
                    _logger.LogInformation($"Response content type zaten mevcut - cache hit: {responseAlias}");
                    
                    // Content type varsa, eksik property'leri kontrol et (sadece yeni field'lar varsa)
                    var existingAliases = responseContentType.PropertyTypes.Select(p => p.Alias).ToHashSet();
                    
                    // Hızlı kontrol: Form field'larından eksik olanları tespit et
                    foreach (var field in formFieldStructure)
                    {
                        // Önce FormKey'i dene (orijinal field name), sonra normalized version
                        var alias = !string.IsNullOrEmpty(field.FormKey) ? field.FormKey : field.Title.ToSafeAlias(_shortStringHelper);
                        if (!existingAliases.Contains(alias))
                        {
                            contentTypeNeedsUpdate = true;
                            _logger.LogInformation($"Eksik field tespit edildi: {field.Title} ({alias})");
                            break;
                        }
                    }
                    
                    // Form data'dan eksik olanları tespit et (fallback)
                    if (!contentTypeNeedsUpdate)
                    {
                        foreach (var field in formData)
                        {
                            // Field key'i direkt kullan (camelCase olarak geliyor)
                            var alias = field.Key;
                            if (!existingAliases.Contains(alias))
                            {
                                contentTypeNeedsUpdate = true;
                                _logger.LogInformation($"Eksik field tespit edildi (formData): {field.Key} ({alias})");
                                break;
                            }
                        }
                    }
                    
                    if (!contentTypeNeedsUpdate)
                    {
                        _logger.LogInformation($"Content type güncel - güncelleme gerekmedi: {responseAlias}");
                    }
                }

                // 4. Content type'ı güncelle (sadece gerektiğinde)
                if (contentTypeNeedsUpdate)
                {
                    _logger.LogInformation("Content type property'leri güncelleniyor...");
                    UpdateContentTypeProperties(responseContentType, formFieldStructure, formData);
                    
                    // Container ilişkisini kontrol et/kur
                    if (containerContentType.AllowedContentTypes?.Any(x => x.Alias == responseAlias) != true)
                    {
                        _logger.LogInformation("Container-response ilişkisi kuruluyor...");
                        var allowedTypes = containerContentType.AllowedContentTypes?.ToList() ?? new List<ContentTypeSort>();
                        int sortOrder = allowedTypes.Count;
                        var newSort = new ContentTypeSort(
                            Guid.Parse(responseContentType.Key.ToString()),
                            sortOrder, 
                            responseContentType.Alias);
                        
                        allowedTypes.Add(newSort);
                        containerContentType.AllowedContentTypes = allowedTypes;
                        _contentTypeService.Save(containerContentType);
                        
                        _logger.LogInformation($"Content type ilişkisi kuruldu: {containerAlias} -> {responseAlias}");
                    }
                }

                _logger.LogInformation("Content type hazırlığı başarıyla tamamlandı");
                return (container, responseContentType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Content type hazırlığı başarısız");
                return (null, null);
            }
        }

        // OPTIMIZED: Content type property'lerini güncelle (sadece eksik olanları ekle)
        private void UpdateContentTypeProperties(
            IContentType responseContentType, 
            List<(string Title, string Type, bool Required, string orjalias, string Regex, string RegexMessage, string FormKey)> formFieldStructure,
            Dictionary<string, string> formData)
        {
            try
            {
                var existingAliases = responseContentType.PropertyTypes.Select(p => p.Alias).ToHashSet();
                bool hasChanges = false;

                // Form field structure'dan eksik property'leri ekle
                if (formFieldStructure.Count > 0)
                {
                    foreach (var field in formFieldStructure)
                    {
                        // Önce FormKey'i kullan (orijinal field name), sonra fallback
                        var alias = !string.IsNullOrEmpty(field.FormKey) ? field.FormKey : field.Title.ToSafeAlias(_shortStringHelper);
                        
                        if (!existingAliases.Contains(alias))
                        {
                            try
                            {
                                var dataType = GetDataTypeForFieldType(field.Type);
                                var propertyType = CreatePropertyTypeWithDataType(dataType, alias, field.Title);
                                propertyType.Mandatory = field.Required;

                                responseContentType.AddPropertyType(propertyType, "formFields");
                                hasChanges = true;
                                _logger.LogInformation($"Yeni property eklendi: {field.Title} ({alias}) - Type: {field.Type}");
                            }
                            catch (Exception fieldEx)
                            {
                                _logger.LogError(fieldEx, $"Property eklenirken hata: {field.Title}");
                            }
                        }
                    }
                }
                else
                {
                    // Fallback: Form data'dan eksik property'leri ekle
                    var textboxDataType = GetDataTypeByEditorAlias("Umbraco.TextBox");
                    if (textboxDataType != null)
                    {
                        foreach (var field in formData)
                        {
                            // Field key'i direkt kullan (camelCase olarak geliyor)
                            var alias = field.Key;
                            
                            if (!existingAliases.Contains(alias))
                            {
                                try
                                {
                                    var propertyType = new PropertyType(_shortStringHelper, textboxDataType, alias)
                                    {
                                        Name = field.Key,
                                        Description = $"Mail Template: @Model[\"{alias}\"]",
                                        Mandatory = false,
                                        DataTypeId = textboxDataType.Id
                                    };
                                    
                                    responseContentType.AddPropertyType(propertyType, "formFields");
                                    hasChanges = true;
                                    _logger.LogInformation($"Yeni property eklendi (fallback): {field.Key} ({alias})");
                                }
                                catch (Exception fieldEx)
                                {
                                    _logger.LogError(fieldEx, $"Property eklenirken hata: {field.Key}");
                                }
                            }
                        }
                    }
                }

                // Sadece değişiklik varsa kaydet
                if (hasChanges)
                {
                    try
                    {
                        _logger.LogInformation("Content type kaydediliyor...");
                        
                        // ⚡ Direkt save - timeout yok, Umbraco'nun kendi timeout'una güveniyoruz
                        _contentTypeService.Save(responseContentType);
                        _logger.LogInformation("✅ Content type güncellendi - yeni property'ler eklendi");
                    }
                    catch (Exception saveEx)
                    {
                        _logger.LogError(saveEx, "❌ Content type save hatası");
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Content type property güncelleme başarısız");
            }
        }

        // ✅ HELPER METHOD: HttpContext varken mail template'ini render et
        // Bu metod HTTP request sırasında çalışır, Umbraco'nun view engine'ini kullanır
        private string RenderEmailTemplate(
            IContent formContent,
            Dictionary<string, string> formData,
            List<(string Title, string Type, bool Required, string orjalias, string Regex, string RegexMessage, string FormKey)> formFieldStructure,
            string culture)
        {
            string formName = formContent.Name ?? "Form";
            
            try
            {
                // Template path'i Umbraco'dan al
                string templatePath = formContent.GetValue<string>("mailTemplate", culture);
                
                if (string.IsNullOrEmpty(templatePath))
                {
                    _logger.LogWarning("⚠️ Mail template path boş, basit HTML döndürülüyor");
                    return CreateFallbackEmailHtml(formData, formName);
                }

                _logger.LogInformation($"📧 Rendering email template: {templatePath}");

                // ✅ YENİ YAKLAŞIM: Mapping yapma, direkt formData'yı kullan!
                // Bu sayede field name'ler olduğu gibi kalır (nameSurname, treaderName vs.)
                string mailDataJson = System.Text.Json.JsonSerializer.Serialize(formData);
                
                _logger.LogInformation($"📧 Mail data (no mapping): {mailDataJson}");
                
                // ✅ Umbraco'nun mevcut render metodunu kullan - HttpContext var!
                string renderedHtml = FormsMailHandler.RenderRazorViewToString(
                    this,
                    templatePath,
                    JObject.Parse(mailDataJson)
                );

                _logger.LogInformation($"✅ Template başarıyla render edildi: {renderedHtml.Length} karakter");
                return renderedHtml;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Template render hatası, fallback HTML kullanılıyor");
                return CreateFallbackEmailHtml(formData, formName);
            }
        }

        // Fallback: Basit HTML email oluştur (template render edilemezse)
        private string CreateFallbackEmailHtml(Dictionary<string, string> formData, string formName = "Form")
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html>");
            sb.AppendLine("<head>");
            sb.AppendLine("    <meta charset='utf-8' />");
            sb.AppendLine("    <title>Email Template</title>");
            sb.AppendLine("    <style>");
            sb.AppendLine("        body {");
            sb.AppendLine("            font-family: Arial, sans-serif;");
            sb.AppendLine("            line-height: 1.6;");
            sb.AppendLine("        }");
            sb.AppendLine("        .container {");
            sb.AppendLine("            width: 80%;");
            sb.AppendLine("            margin: 0 auto;");
            sb.AppendLine("        }");
            sb.AppendLine("        .header {");
            sb.AppendLine("            background-color: #f8f8f8;");
            sb.AppendLine("            padding: 20px;");
            sb.AppendLine("            text-align: center;");
            sb.AppendLine("        }");
            sb.AppendLine("        .content {");
            sb.AppendLine("            margin: 20px 0;");
            sb.AppendLine("        }");
            sb.AppendLine("        .content p {");
            sb.AppendLine("            margin: 10px 0;");
            sb.AppendLine("            padding: 8px;");
            sb.AppendLine("            border-bottom: 1px solid #e0e0e0;");
            sb.AppendLine("        }");
            sb.AppendLine("        .content strong {");
            sb.AppendLine("            display: inline-block;");
            sb.AppendLine("            width: 200px;");
            sb.AppendLine("            color: #333;");
            sb.AppendLine("        }");
            sb.AppendLine("        .footer {");
            sb.AppendLine("            background-color: #f8f8f8;");
            sb.AppendLine("            padding: 10px;");
            sb.AppendLine("            text-align: center;");
            sb.AppendLine("            font-size: 0.8em;");
            sb.AppendLine("        }");
            sb.AppendLine("    </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("    <div class='container'>");
            sb.AppendLine("        <div class='header'>");
            sb.AppendLine($"            <h1>{formName} - Form Gönderimi</h1>");
            sb.AppendLine("        </div>");
            sb.AppendLine("        <div class='content'>");
            
            foreach (var field in formData)
            {
                sb.AppendLine($"            <p><strong>{field.Key}:</strong> {field.Value}</p>");
            }
            
            sb.AppendLine("        </div>");
            sb.AppendLine("        <div class='footer'>");
            sb.AppendLine($"            <p>&copy; {DateTime.Now.Year} - Morpara</p>");
            sb.AppendLine("        </div>");
            sb.AppendLine("    </div>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");
            
            return sb.ToString();
        }
    }
}