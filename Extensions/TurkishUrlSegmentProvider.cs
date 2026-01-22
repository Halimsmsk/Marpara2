using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Strings;

namespace Marpara2.Extensions;

public class TurkishUrlSegmentProvider : IUrlSegmentProvider
{
    private readonly IShortStringHelper _shortStringHelper;

    public TurkishUrlSegmentProvider(IShortStringHelper shortStringHelper)
    {
        _shortStringHelper = shortStringHelper;
    }

    public string? GetUrlSegment(IContentBase contentNode, string? culture = null)
    {
        var name = culture != null
            ? contentNode.GetCultureName(culture)
            : contentNode.Name;

        return GetUrlSegment(name ?? string.Empty);
    }

    private string GetUrlSegment(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Önce büyük harfleri normalize et (ToLowerInvariant İ karakterini düzgün dönüştürmez)
        text = text
            .Replace("İ", "i")
            .Replace("I", "i")  // Türkçe'de I → ı olmalı ama URL'de i kullanıyoruz
            .Replace("Ğ", "g")
            .Replace("Ü", "u")
            .Replace("Ş", "s")
            .Replace("Ö", "o")
            .Replace("Ç", "c");

        // Sonra küçük harfe çevir
        text = text.ToLowerInvariant();

        // Küçük Türkçe karakterleri normalize et
        text = text
            .Replace("ı", "i")
            .Replace("ğ", "g")
            .Replace("ü", "u")
            .Replace("ş", "s")
            .Replace("ö", "o")
            .Replace("ç", "c");

        return _shortStringHelper.CleanStringForUrlSegment(text);
    }
}
