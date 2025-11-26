using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;
using Newtonsoft.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Morpara.Notifications
{
    /// <summary>
    /// Block Grid alanlarında belirli blok türlerinin sadece bir adet eklenebilmesini sağlayan validation handler
    /// ContentApiController yapısı referans alınarak geliştirildi
    /// </summary>
    public class BlockGridValidationHandler : INotificationHandler<ContentSavingNotification>
    {
        private readonly ILogger<BlockGridValidationHandler> _logger;
        private readonly IContentTypeService _contentTypeService;
        
        // Sadece bir adet eklenebilecek blok türleri
        private readonly HashSet<string> _singleInstanceBlocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "representationList",
            "representationMap",
            "campingSlider",
            "blogFeatured",
        };

        // Münhasır blok türleri - eğer bunlar varsa başka hiçbir blok olamaz (kendilerinden sadece 1 adet)
        private readonly HashSet<string> _exclusiveBlocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "questionsPage",
            "blogList",
            "staticPage",
            "staticSubPage"
        };

        // Münhasır çoklu blok türleri - eğer bunlar varsa başka hiçbir blok olamaz ama kendilerinden birden fazla olabilir
        private readonly HashSet<string> _exclusiveMultipleBlocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "campingList"  // 2-3 adet campingList olabilir ama başka hiçbir blok eklenemez
        };

        // CampingList için maksimum limit
        private readonly Dictionary<string, int> _maxInstanceLimits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "campingList", 3 }  // Maksimum 3 adet campingList
        };

        // ContentTypeKey cache - performans için
        private readonly Dictionary<string, string> _contentTypeKeyCache = new Dictionary<string, string>();
        private readonly object _cacheLock = new object();

        public BlockGridValidationHandler(ILogger<BlockGridValidationHandler> logger, IContentTypeService contentTypeService)
        {
            _logger = logger;
            _contentTypeService = contentTypeService;
        }

        public void Handle(ContentSavingNotification notification)
        {
            _logger.LogError("🔄 [DEBUG] BlockGridValidationHandler ÇALIŞIYOR! Content count: {Count}", notification.SavedEntities.Count());
            
            var validationErrors = new List<string>();

            foreach (var content in notification.SavedEntities)
            {
                _logger.LogError("📄 [DEBUG] Content kontrol ediliyor: {ContentName} (Type: {ContentType})", content.Name, content.ContentType.Alias);
                
                var contentValidationErrors = ValidateBlockGridContent(content);
                validationErrors.AddRange(contentValidationErrors);
            }

            if (validationErrors.Count > 0)
            {
                _logger.LogError("❌ [VALIDATION] {Count} validation error(s) found - Save operation cancelled", validationErrors.Count);
                
                foreach (var error in validationErrors)
                {
                    _logger.LogError("🚫 [VALIDATION] {Error}", error);
                    notification.Messages.Add(new EventMessage("Block Grid Validation Error", 
                        error, 
                        EventMessageType.Error));
                }
                
                notification.Cancel = true;
                return;
            }

            _logger.LogError("✅ [DEBUG] Validation tamamlandı - hata bulunamadı: {Count} content(s)", notification.SavedEntities.Count());
        }

        private List<string> ValidateBlockGridContent(IContent content)
        {
            var validationErrors = new List<string>();

            _logger.LogError("🔍 [DEBUG] Content properties kontrol ediliyor: {PropertyCount} adet", content.Properties.Count());

            foreach (var property in content.Properties)
            {
                _logger.LogError("🎯 [DEBUG] Property: {PropertyAlias} - Editor: {Editor}", 
                    property.Alias, property.PropertyType?.PropertyEditorAlias ?? "NULL");

                if (property.PropertyType?.PropertyEditorAlias == "Umbraco.BlockGrid")
                {
                    _logger.LogError("✅ [DEBUG] Block Grid property bulundu: {PropertyAlias}", property.Alias);
                    
                    var propertyValidationErrors = TryValidateBlockGridProperty(content, property);
                    validationErrors.AddRange(propertyValidationErrors);
                }
            }

            _logger.LogError("📋 [DEBUG] Content validation tamamlandı: {ErrorCount} hata bulundu", validationErrors.Count);
            return validationErrors;
        }

        private List<string> TryValidateBlockGridProperty(IContent content, IProperty property)
        {
            var validationErrors = new List<string>();

            // Try different methods to get BlockGrid data
            var methods = new (string, Func<object>)[]
            {
                ("GetValue()", () => property.GetValue()),
                ("GetValue(culture)", () => property.GetValue("tr-TR")),
                ("GetValue(null, null)", () => property.GetValue(null, null)),
                ("content.GetValue(alias)", () => content.GetValue(property.Alias)),
                ("content.GetValue(alias, culture)", () => content.GetValue(property.Alias, "tr-TR"))
            };

            foreach (var (methodName, getValue) in methods)
            {
                try
                {
                    var value = getValue();
                    var result = TryValidateValue(content, property, value, methodName);
                    validationErrors.AddRange(result);
                    if (result.Count > 0) return validationErrors; // Stop at first successful validation
                }
                catch
                {
                    // Silent fail and try next method
                }
            }

            // Special culture variant check
            try
            {
                if (property.PropertyType != null && property.PropertyType.Variations.VariesByCulture())
                {
                    var valueCultureVariant = property.GetValue("tr-TR", null);
                    var result = TryValidateValue(content, property, valueCultureVariant, "GetValue(culture, segment)");
                    validationErrors.AddRange(result);
                    if (result.Count > 0) return validationErrors;
                }
            }
            catch
            {
                // Silent fail
            }
            
            return validationErrors;
        }

        private List<string> TryValidateValue(IContent content, IProperty property, object? value, string method)
        {
            var validationErrors = new List<string>();

            if (value == null)
                return validationErrors;

            // BlockGridModel ise direkt validate et
            if (value is BlockGridModel blockGridModel)
            {
                if (blockGridModel.Count > 0)
                {
                    var errors = ValidateBlockGridModel(content, property, blockGridModel);
                    validationErrors.AddRange(errors);
                }
                return validationErrors;
            }

            // String ise JSON parse et
            if (value is string jsonString && !string.IsNullOrEmpty(jsonString))
            {
                var errors = TryParseJsonToBlockGrid(content, property, jsonString, method);
                validationErrors.AddRange(errors);
                return validationErrors;
            }

            return validationErrors;
        }

        private List<string> ValidateBlockGridModel(IContent content, IProperty property, BlockGridModel blockGrid)
        {
            var validationErrors = new List<string>();
            var blockTypeCounts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            
            _logger.LogError("📦 [DEBUG] BlockGridModel validation başlıyor: {BlockCount} adet blok", blockGrid.Count);
            
            // Count block types
            foreach (var item in blockGrid)
            {
                var blockType = item.Content?.ContentType?.Alias;
                _logger.LogError("🔍 [DEBUG] Blok türü: '{BlockType}' (Key: {ContentKey})", 
                    blockType ?? "NULL", item.ContentKey);
                
                if (string.IsNullOrEmpty(blockType))
                    continue;

                if (!blockTypeCounts.ContainsKey(blockType))
                {
                    blockTypeCounts[blockType] = new List<string>();
                }
                
                blockTypeCounts[blockType].Add(item.ContentKey.ToString());
            }

            _logger.LogError("📊 [DEBUG] Bulunan blok türleri: {BlockTypes}", 
                string.Join(", ", blockTypeCounts.Select(kvp => $"'{kvp.Key}'({kvp.Value.Count})")));

            // Validate exclusive blocks
            var exclusiveErrors = ValidateExclusiveBlocks(content, property, blockTypeCounts);
            validationErrors.AddRange(exclusiveErrors);

            // Validate single instance blocks
            foreach (var blockType in _singleInstanceBlocks)
            {
                if (blockTypeCounts.TryGetValue(blockType, out var instances) && instances.Count > 1)
                {
                    var errorMessage = $"'{GetBlockDisplayName(blockType)}' bloğundan '{property.Alias}' alanında sadece bir adet bulunabilir. " +
                                     $"Şu anda {instances.Count} adet mevcut. Lütfen fazla olanları kaldırın.";
                    
                    validationErrors.Add(errorMessage);
                }
            }

            // Validate special rules
            var specialRuleErrors = ValidateSpecialRules(content, property, blockTypeCounts);
            validationErrors.AddRange(specialRuleErrors);

            _logger.LogError("📋 [DEBUG] BlockGridModel validation tamamlandı: {ErrorCount} hata", validationErrors.Count);
            return validationErrors;
        }

        private List<string> TryParseJsonToBlockGrid(IContent content, IProperty property, string jsonString, string method)
        {
            var validationErrors = new List<string>();
            
            // Check if JSON contains Block Grid structure
            if (!jsonString.Contains("\"contentData\"") || !jsonString.Contains("\"Layout\""))
                return validationErrors;

            try
            {
                var rawJson = JsonConvert.DeserializeObject<dynamic>(jsonString);
                if (rawJson == null) return validationErrors;

                var contentData = rawJson.contentData;
                var layout = rawJson.Layout;
                
                if (contentData == null || layout == null) return validationErrors;

                var blockGridLayout = layout["Umbraco.BlockGrid"];
                if (blockGridLayout == null) return validationErrors;

                var blockTypeCounts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                // Extract content data
                var contentDataDict = new Dictionary<string, dynamic>();
                foreach (var contentItem in contentData)
                {
                    string key = contentItem.key?.ToString();
                    if (!string.IsNullOrEmpty(key))
                    {
                        contentDataDict[key] = contentItem;
                    }
                }

                // Process layout items
                foreach (var layoutItem in blockGridLayout)
                {
                    string contentKey = layoutItem.contentKey?.ToString();
                    if (string.IsNullOrEmpty(contentKey)) continue;

                    if (contentDataDict.TryGetValue(contentKey, out var contentItem))
                    {
                        string contentTypeKey = contentItem.contentTypeKey?.ToString();
                        if (string.IsNullOrEmpty(contentTypeKey)) continue;

                        var blockType = GetBlockTypeFromContentTypeKey(contentTypeKey);
                        if (string.IsNullOrEmpty(blockType)) continue;

                        if (!blockTypeCounts.ContainsKey(blockType))
                        {
                            blockTypeCounts[blockType] = new List<string>();
                        }
                        
                        blockTypeCounts[blockType].Add(contentKey);
                    }
                }

                if (blockTypeCounts.Count > 0)
                {
                    var errors = ValidateManualBlockGrid(content, property, blockTypeCounts, method);
                    validationErrors.AddRange(errors);
                }
            }
            catch
            {
                // Silent fail on JSON parse errors
            }

            return validationErrors;
        }

        private string GetBlockTypeFromContentTypeKey(string contentTypeKey)
        {
            if (string.IsNullOrEmpty(contentTypeKey))
                return null;

            var normalizedKey = contentTypeKey.ToLowerInvariant().Trim();

            // Check cache first
            lock (_cacheLock)
            {
                if (_contentTypeKeyCache.TryGetValue(normalizedKey, out var cachedAlias))
                    return cachedAlias;
            }

            try
            {
                if (Guid.TryParse(normalizedKey, out var contentTypeGuid))
                {
                    var contentType = _contentTypeService.Get(contentTypeGuid);
                    
                    if (contentType != null)
                    {
                        var alias = contentType.Alias;
                        
                        // IMPORTANT: Return ALL block types, not just tracked ones
                        // This ensures exclusive blocks validation works for all blocks
                        lock (_cacheLock)
                        {
                            _contentTypeKeyCache[normalizedKey] = alias;
                        }
                        return alias;
                    }
                    else
                    {
                        // Cache null result
                        lock (_cacheLock)
                        {
                            _contentTypeKeyCache[normalizedKey] = null;
                        }
                        return null;
                    }
                }
                else
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        private List<string> ValidateManualBlockGrid(IContent content, IProperty property, Dictionary<string, List<string>> blockTypeCounts, string method)
        {
            var validationErrors = new List<string>();
            
            // Validate exclusive blocks
            var exclusiveErrors = ValidateExclusiveBlocks(content, property, blockTypeCounts);
            validationErrors.AddRange(exclusiveErrors);

            // Validate single instance blocks
            foreach (var blockType in _singleInstanceBlocks)
            {
                if (blockTypeCounts.TryGetValue(blockType, out var instances) && instances.Count > 1)
                {
                    var errorMessage = $"'{GetBlockDisplayName(blockType)}' bloğundan '{property.Alias}' alanında sadece bir adet bulunabilir. " +
                                     $"Şu anda {instances.Count} adet mevcut. Lütfen fazla olanları kaldırın.";
                    
                    validationErrors.Add(errorMessage);
                }
            }

            // Validate special rules
            var specialRuleErrors = ValidateSpecialRules(content, property, blockTypeCounts);
            validationErrors.AddRange(specialRuleErrors);
                
            return validationErrors;
        }

        private List<string> ValidateExclusiveBlocks(IContent content, IProperty property, Dictionary<string, List<string>> blockTypeCounts)
        {
            var validationErrors = new List<string>();
            
            _logger.LogError("🚫 [DEBUG] Exclusive block validation başlıyor...");
            
            // Find all exclusive blocks that are present (both single and multiple)
            var presentSingleExclusiveBlocks = blockTypeCounts
                .Where(kvp => _exclusiveBlocks.Contains(kvp.Key))
                .ToList();

            var presentMultipleExclusiveBlocks = blockTypeCounts
                .Where(kvp => _exclusiveMultipleBlocks.Contains(kvp.Key))
                .ToList();

            var allPresentExclusiveBlocks = presentSingleExclusiveBlocks.Concat(presentMultipleExclusiveBlocks).ToList();

            _logger.LogError("🔍 [DEBUG] Single Exclusive bloklar: {SingleExclusiveBlocks} - Bulunan: {PresentSingleBlocks}", 
                string.Join(", ", _exclusiveBlocks), 
                string.Join(", ", presentSingleExclusiveBlocks.Select(b => b.Key)));

            _logger.LogError("🔍 [DEBUG] Multiple Exclusive bloklar: {MultipleExclusiveBlocks} - Bulunan: {PresentMultipleBlocks}", 
                string.Join(", ", _exclusiveMultipleBlocks), 
                string.Join(", ", presentMultipleExclusiveBlocks.Select(b => b.Key)));

            // If no exclusive blocks are present, no validation needed
            if (allPresentExclusiveBlocks.Count == 0)
            {
                _logger.LogError("✅ [DEBUG] Hiç exclusive blok bulunamadı, validation geçildi");
                return validationErrors;
            }

            // If exclusive blocks exist, NO other blocks can exist
            var allOtherBlocks = blockTypeCounts
                .Where(kvp => !_exclusiveBlocks.Contains(kvp.Key) && !_exclusiveMultipleBlocks.Contains(kvp.Key))
                .ToList();

            _logger.LogError("⚠️ [DEBUG] Exclusive blok(lar) bulundu! Diğer bloklar: {OtherBlocks}", 
                string.Join(", ", allOtherBlocks.Select(b => b.Key)));

            if (allOtherBlocks.Any())
            {
                var exclusiveBlockNames = allPresentExclusiveBlocks.Select(kvp => $"'{GetBlockDisplayName(kvp.Key)}'");
                var otherBlockNames = allOtherBlocks.Select(kvp => $"'{GetBlockDisplayName(kvp.Key)}'");

                var errorMessage = $"'{property.Alias}' alanında münhasır bloklar bulunduğunda başka hiçbir blok eklenemez. " +
                                 $"Bu Blok Tek Olabilir Sayfada: {string.Join(", ", exclusiveBlockNames)}. " +
                                 $"Diğer bloklar: {string.Join(", ", otherBlockNames)}. " +
                                 $"Lütfen ya sadece münhasır blokları ya da diğer blokları kullanın.";
                
                _logger.LogError("🚫 [DEBUG] HATA OLUŞTURULUYOR: {Error}", errorMessage);
                validationErrors.Add(errorMessage);
            }

            // Check for multiple instances of single exclusive blocks (should only have 1)
            foreach (var exclusiveBlock in presentSingleExclusiveBlocks)
            {
                if (exclusiveBlock.Value.Count > 1)
                {
                    var errorMessage = $"'{GetBlockDisplayName(exclusiveBlock.Key)}' münhasır bloğundan '{property.Alias}' alanında sadece bir adet bulunabilir. " +
                                     $"Şu anda {exclusiveBlock.Value.Count} adet mevcut. Lütfen fazla olanları kaldırın.";
                    
                    _logger.LogError("🚫 [DEBUG] ÇOKLU INSTANCE HATASI (Single): {Error}", errorMessage);
                    validationErrors.Add(errorMessage);
                }
            }

            // Check for limits on multiple exclusive blocks
            foreach (var exclusiveBlock in presentMultipleExclusiveBlocks)
            {
                if (_maxInstanceLimits.TryGetValue(exclusiveBlock.Key, out var maxLimit))
                {
                    if (exclusiveBlock.Value.Count > maxLimit)
                    {
                        var errorMessage = $"'{GetBlockDisplayName(exclusiveBlock.Key)}' bloğundan '{property.Alias}' alanında maksimum {maxLimit} adet bulunabilir. " +
                                         $"Şu anda {exclusiveBlock.Value.Count} adet mevcut. Lütfen fazla olanları kaldırın.";
                        
                        _logger.LogError("🚫 [DEBUG] LIMIT AŞIMI HATASI (Multiple): {Error}", errorMessage);
                        validationErrors.Add(errorMessage);
                    }
                }
            }
            
            _logger.LogError("📋 [DEBUG] Exclusive validation tamamlandı: {ErrorCount} hata", validationErrors.Count);
            return validationErrors;
        }

        private List<string> ValidateSpecialRules(IContent content, IProperty property, Dictionary<string, List<string>> blockTypeCounts)
        {
            var validationErrors = new List<string>();
            
            // ContentApiController'da kullanılan blok türlerine göre özel kurallar
            
            // representationList ve representationMap aynı anda bulunamaz
            var hasRepList = blockTypeCounts.ContainsKey("representationList") && blockTypeCounts["representationList"].Count > 0;
            var hasRepMap = blockTypeCounts.ContainsKey("representationMap") && blockTypeCounts["representationMap"].Count > 0;
            
            if (hasRepList && hasRepMap)
            {
                var errorMessage = $"'{property.Alias}' alanında 'Temsilcilik Listesi' ve 'Temsilcilik Haritası' blokları aynı anda bulunamaz. " +
                                 "Lütfen sadece birini kullanın.";
                validationErrors.Add(errorMessage);
            }

            // blogList ve blogFeatured kontrolü - ContentApiController'da her ikisi de aynı parent'da olabiliyor
            // Ama blogFeatured maksimum 2 adet olabilir
            if (blockTypeCounts.TryGetValue("blogFeatured", out var blogFeaturedInstances) && blogFeaturedInstances.Count > 2)
            {
                var errorMessage = $"'{property.Alias}' alanında 'Öne Çıkan Blog' bloğundan maksimum 2 adet bulunabilir. " +
                                 $"Şu anda {blogFeaturedInstances.Count} adet mevcut.";
                validationErrors.Add(errorMessage);
            }

            // campingList ve campingSlider kontrolü - ContentApiController'da aynı parent'da olabilir
            // Ancak campingSlider maksimum 3 adet olabilir diyelim
            if (blockTypeCounts.TryGetValue("campingSlider", out var campingSliderInstances) && campingSliderInstances.Count > 3)
            {
                var errorMessage = $"'{property.Alias}' alanında 'Kampanya Slider' bloğundan maksimum 3 adet bulunabilir. " +
                                 $"Şu anda {campingSliderInstances.Count} adet mevcut.";
                validationErrors.Add(errorMessage);
            }

            // Content type'a özel kurallar
            var contentTypeErrors = ValidateContentTypeSpecificRules(content, property, blockTypeCounts);
            validationErrors.AddRange(contentTypeErrors);
            
            return validationErrors;
        }

        private List<string> ValidateContentTypeSpecificRules(IContent content, IProperty property, Dictionary<string, List<string>> blockTypeCounts)
        {
            var validationErrors = new List<string>();
            var contentTypeAlias = content.ContentType.Alias;

            switch (contentTypeAlias.ToLowerInvariant())
            {
                case "blogpage":
                case "blog":
                    var hasCampingList = blockTypeCounts.ContainsKey("campingList");
                    var hasCampingSlider = blockTypeCounts.ContainsKey("campingSlider");
                    
                    if (hasCampingList)
                    {
                        validationErrors.Add("Blog sayfalarında 'Kampanya Listesi' bloğu kullanılamaz. Lütfen 'Blog Listesi' veya 'Öne Çıkan Blog' bloğunu kullanın.");
                    }
                    
                    if (hasCampingSlider)
                    {
                        validationErrors.Add("Blog sayfalarında 'Kampanya Slider' bloğu kullanılamaz. Lütfen 'Blog Listesi' veya 'Öne Çıkan Blog' bloğunu kullanın.");
                    }
                    break;

                case "campaignpage":
                case "campaign":
                    var hasBlogList = blockTypeCounts.ContainsKey("blogList");
                    var hasBlogFeatured = blockTypeCounts.ContainsKey("blogFeatured");
                    
                    if (hasBlogList)
                    {
                        validationErrors.Add("Kampanya sayfalarında 'Blog Listesi' bloğu kullanılamaz. Lütfen 'Kampanya Listesi' veya 'Kampanya Slider' bloğunu kullanın.");
                    }
                    
                    if (hasBlogFeatured)
                    {
                        validationErrors.Add("Kampanya sayfalarında 'Öne Çıkan Blog' bloğu kullanılamaz. Lütfen 'Kampanya Listesi' veya 'Kampanya Slider' bloğunu kullanın.");
                    }
                    break;

                case "representationpage":
                case "representatives":
                    var hasBlogContent = blockTypeCounts.ContainsKey("blogList") || blockTypeCounts.ContainsKey("blogFeatured");
                    var hasCampingContent = blockTypeCounts.ContainsKey("campingList") || blockTypeCounts.ContainsKey("campingSlider");
                    
                    if (hasBlogContent)
                    {
                        validationErrors.Add("Temsilcilik sayfalarında blog blokları kullanılamaz. Lütfen 'Temsilcilik Listesi' veya 'Temsilcilik Haritası' bloğunu kullanın.");
                    }
                    
                    if (hasCampingContent)
                    {
                        validationErrors.Add("Temsilcilik sayfalarında kampanya blokları kullanılamaz. Lütfen 'Temsilcilik Listesi' veya 'Temsilcilik Haritası' bloğunu kullanın.");
                    }
                    break;
            }
            
            return validationErrors;
        }

        private string GetBlockDisplayName(string blockType)
        {
            return blockType.ToLowerInvariant() switch
            {
                "campinglist" => "Kampanya Listesi",
                "bloglist" => "Blog Listesi",
                "questionspage" => "Sorular Sayfası",
                "representationlist" => "Temsilcilik Listesi",
                "representationmap" => "Temsilcilik Haritası",
                "questionslist" => "Soru Listesi",
                "campingslider" => "Kampanya Slider",
                "blogfeatured" => "Öne Çıkan Blog",
                "staticpage" => "Statik Sayfa",
                "staticsubpage" => "Statik Alt Sayfa",
                "basicblock" => "Temel Blok",
                "basic" => "Temel Blok",
                _ => blockType
            };
        }

        /// <summary>
        /// Debug yardımcısı - Umbraco'daki tüm Element Type'ları listeler (sadece debug modda)
        /// </summary>
        private void LogAvailableContentTypes()
        {
            if (!_logger.IsEnabled(LogLevel.Debug))
                return;

            try
            {
                var elementTypes = _contentTypeService.GetAll()
                    .Where(ct => ct.IsElement)
                    .OrderBy(ct => ct.Alias)
                    .ToList();

                _logger.LogDebug("🔍 [DEBUG] Available Element Types ({Count} total):", elementTypes.Count);

                foreach (var elementType in elementTypes)
                {
                    var status = "⭕ NOT TRACKED";
                    if (_singleInstanceBlocks.Contains(elementType.Alias))
                        status = "🔂 SINGLE INSTANCE";
                    else if (_exclusiveBlocks.Contains(elementType.Alias))
                        status = "🚫 EXCLUSIVE (Single)";
                    else if (_exclusiveMultipleBlocks.Contains(elementType.Alias))
                        status = "🚫 EXCLUSIVE (Multiple)";

                    _logger.LogDebug("🧩 [ELEMENT TYPE] {Key} -> {Alias} | {Name} | {Status}", 
                        elementType.Key, elementType.Alias, elementType.Name, status);
                }

                var missingSingleTypes = _singleInstanceBlocks
                    .Where(alias => !elementTypes.Any(et => string.Equals(et.Alias, alias, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                var missingExclusiveTypes = _exclusiveBlocks
                    .Where(alias => !elementTypes.Any(et => string.Equals(et.Alias, alias, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                var missingExclusiveMultipleTypes = _exclusiveMultipleBlocks
                    .Where(alias => !elementTypes.Any(et => string.Equals(et.Alias, alias, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (missingSingleTypes.Any())
                {
                    _logger.LogDebug("⚠️ [MISSING SINGLE] Tracked but not found: {MissingTypes}", 
                        string.Join(", ", missingSingleTypes));
                }

                if (missingExclusiveTypes.Any())
                {
                    _logger.LogDebug("⚠️ [MISSING EXCLUSIVE] Tracked but not found: {MissingTypes}", 
                        string.Join(", ", missingExclusiveTypes));
                }

                if (missingExclusiveMultipleTypes.Any())
                {
                    _logger.LogDebug("⚠️ [MISSING EXCLUSIVE MULTIPLE] Tracked but not found: {MissingTypes}", 
                        string.Join(", ", missingExclusiveMultipleTypes));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [DEBUG] Error getting Element Types list");
            }
        }

        /// <summary>
        /// Cache temizleme metodu - development için
        /// </summary>
        private void ClearContentTypeCache()
        {
            lock (_cacheLock)
            {
                var cacheCount = _contentTypeKeyCache.Count;
                _contentTypeKeyCache.Clear();
                _logger.LogDebug("🗑️ [CACHE] ContentType cache cleared: {ClearedCount} entries", cacheCount);
            }
        }
    }

    /// <summary>
    /// ContentSaved event için BlockGrid validation handler
    /// Timing problemi varsa bunu kullanacağız
    /// </summary>
    public class BlockGridValidationSavedHandler : INotificationHandler<ContentSavedNotification>
    {
        private readonly ILogger<BlockGridValidationSavedHandler> _logger;
        
        public BlockGridValidationSavedHandler(ILogger<BlockGridValidationSavedHandler> logger)
        {
            _logger = logger;
        }

        public void Handle(ContentSavedNotification notification)
        {
            _logger.LogInformation("🔄 [SAVED-VALIDATION] ContentSaved Handler başlatılıyor - {Count} içerik kontrol ediliyor", 
                notification.SavedEntities.Count());

            foreach (var content in notification.SavedEntities)
            {
                _logger.LogInformation("📄 [SAVED-VALIDATION] İçerik: {ContentName} (ID: {ContentId}, Type: {ContentType})", 
                    content.Name, content.Id, content.ContentType.Alias);

                // Block Grid property'lerini kontrol et
                foreach (var property in content.Properties.Where(p => p.PropertyType?.PropertyEditorAlias == "Umbraco.BlockGrid"))
                {
                    var value = property.GetValue();
                    
                    _logger.LogDebug("🎯 [SAVED-VALIDATION] BLOCK GRID BULUNDU: {PropertyAlias} - Value: {ValueType}", 
                        property.Alias, value?.GetType().Name ?? "null");

                    if (value != null)
                    {
                        _logger.LogDebug("🔍 [SAVED-VALIDATION] Raw Value var! Type: {Type}", value.GetType().FullName);
                        
                        if (value is BlockGridModel blockGridModel && blockGridModel.Count > 0)
                        {
                            _logger.LogInformation("✅ [SAVED-VALIDATION] BlockGridModel bulundu: {BlockCount} blok", blockGridModel.Count);
                            
                            foreach (var item in blockGridModel)
                            {
                                var blockType = item.Content?.ContentType?.Alias;
                                _logger.LogDebug("📦 [SAVED-VALIDATION] Blok türü: '{BlockType}' (Key: {BlockKey})", 
                                    blockType, item.ContentKey);
                            }
                        }
                        else if (value is string jsonString && !string.IsNullOrEmpty(jsonString))
                        {
                            _logger.LogDebug("🔄 [SAVED-VALIDATION] JSON string: {Length} karakter", jsonString.Length);
                        }
                        else
                        {
                            _logger.LogDebug("🔍 [SAVED-VALIDATION] Bilinmeyen tip: {Type}", value.GetType().Name);
                        }
                    }
                    else
                    {
                        _logger.LogDebug("📭 [SAVED-VALIDATION] Block Grid hala null");
                    }
                }
            }

            _logger.LogInformation("✅ [SAVED-VALIDATION] ContentSaved Handler tamamlandı");
        }
    }

    /// <summary>
    /// Navigation content'leri (headerNav, footerNavbar) değiştiğinde cache'i temizleyen handler
    /// ContentApiController'daki cache implementasyonu ile entegre çalışır
    /// </summary>
    public class NavigationCacheInvalidationHandler : 
        INotificationHandler<ContentUnpublishedNotification>,
        INotificationHandler<ContentDeletedNotification>
    {
        private readonly ILogger<NavigationCacheInvalidationHandler> _logger;
        private readonly IMemoryCache _memoryCache;
        
        // Cache configuration - ContentApiController ile aynı değerler
        private const string HEADER_NAV_CACHE_KEY_PREFIX = "HeaderNav_";
        private const string FOOTER_NAV_CACHE_KEY_PREFIX = "FooterNav_";
        
        // Navigation content types
        private readonly HashSet<string> _navigationContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "headerNav",
            "footerNavbar"
        };

        public NavigationCacheInvalidationHandler(
            ILogger<NavigationCacheInvalidationHandler> logger, 
            IMemoryCache memoryCache)
        {
            _logger = logger;
            _memoryCache = memoryCache;
        }

     

        public void Handle(ContentUnpublishedNotification notification)
        {
            InvalidateCacheForContentEntities(notification.UnpublishedEntities, "UNPUBLISHED");
        }

        public void Handle(ContentDeletedNotification notification)
        {            
            InvalidateCacheForContentEntities(notification.DeletedEntities, "DELETED");
        }

        private void InvalidateCacheForPublishedEntities(IEnumerable<IPublishedContent> entities, string action)
        {
            foreach (var entity in entities)
            {
                if (_navigationContentTypes.Contains(entity.ContentType.Alias))
                {
                    _logger.LogInformation("[NAVIGATION CACHE] Content {Action}: {ContentType} (ID: {Id}, Name: {Name})", 
                        action, entity.ContentType.Alias, entity.Id, entity.Name);
                    
                    ClearNavigationCacheForContentType(entity.ContentType.Alias);
                }
            }
        }

        private void InvalidateCacheForContentEntities(IEnumerable<IContent> entities, string action)
        {
            foreach (var entity in entities)
            {
                var contentTypeAlias = entity.ContentType.Alias;
                if (_navigationContentTypes.Contains(contentTypeAlias))
                {
                    _logger.LogInformation("[NAVIGATION CACHE] Content {Action}: {ContentType} (ID: {Id}, Name: {Name})", 
                        action, contentTypeAlias, entity.Id, entity.Name);
                    
                    ClearNavigationCacheForContentType(contentTypeAlias);
                }
            }
        }

        private void ClearNavigationCacheForContentType(string contentTypeAlias)
        {
            try
            {
                var cultures = new[] { "tr-TR", "en" };
                var clearedKeys = new List<string>();

                foreach (var culture in cultures)
                {
                    string cacheKey;
                    if (contentTypeAlias.Equals("headerNav", StringComparison.OrdinalIgnoreCase))
                    {
                        cacheKey = $"{HEADER_NAV_CACHE_KEY_PREFIX}{contentTypeAlias}_{culture}";
                    }
                    else if (contentTypeAlias.Equals("footerNavbar", StringComparison.OrdinalIgnoreCase))
                    {
                        cacheKey = $"{FOOTER_NAV_CACHE_KEY_PREFIX}{contentTypeAlias}_{culture}";
                    }
                    else
                    {
                        continue; // Skip non-navigation content types
                    }

                    _memoryCache.Remove(cacheKey);
                    clearedKeys.Add(cacheKey);
                }

                if (clearedKeys.Any())
                {
                    _logger.LogInformation("[NAVIGATION CACHE INVALIDATED] Content type: {ContentType}, Cleared keys: {Keys}", 
                        contentTypeAlias, string.Join(", ", clearedKeys));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ERROR] Navigation cache invalidation failed for content type: {ContentType}", contentTypeAlias);
            }
        }
    }
}