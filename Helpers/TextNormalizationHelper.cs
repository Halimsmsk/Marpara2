using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Morpara.Helpers
{
    public static class TextNormalizationHelper
    {
        /// <summary>
        /// Türkçe karakterler ve UTF-8 sorunları için güçlü normalizasyon
        /// </summary>
        public static string NormalizeCategoryName(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            // 1. Unicode normalizasyon (kompozit karakterleri ayrıştır)
            var normalized = input.Normalize(NormalizationForm.FormD);

            // 2. Diacritik işaretleri kaldır (accent marks)
            var stringBuilder = new StringBuilder();
            foreach (var c in normalized)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            // 3. Tekrar normalize et
            var result = stringBuilder.ToString().Normalize(NormalizationForm.FormC);

            // 4. Türkçe karakter dönüşümleri (hem küçük hem büyük harf)
            result = result
                .Replace("ç", "c").Replace("Ç", "c")
                .Replace("ğ", "g").Replace("Ğ", "g")
                .Replace("ı", "i").Replace("İ", "i")
                .Replace("ö", "o").Replace("Ö", "o")
                .Replace("ş", "s").Replace("Ş", "s")
                .Replace("ü", "u").Replace("Ü", "u");

            // 5. Küçük harfe çevir
            result = result.ToLowerInvariant();

            // 6. Boşlukları ve özel karakterleri temizle
            result = result
                .Replace(" ", "-")
                .Replace("_", "-")
                .Replace("&", "")      // ⚡ FIX: & karakterini kaldır ("yurt-disi-hesap-&-euro-iban" → "yurt-disi-hesap-euro-iban")
                .Replace(".", "")
                .Replace(",", "")
                .Replace(":", "")
                .Replace(";", "")
                .Replace("!", "")
                .Replace("?", "")
                .Replace("(", "")
                .Replace(")", "")
                .Replace("[", "")
                .Replace("]", "")
                .Replace("{", "")
                .Replace("}", "");

            // 7. Çoklu tire işaretlerini tek tire yap
            while (result.Contains("--"))
            {
                result = result.Replace("--", "-");
            }

            // 8. Baştan ve sondan tire kaldır
            result = result.Trim('-');

            return result;
        }

        /// <summary>
        /// İki kategori adının eşit olup olmadığını kontrol eder
        /// </summary>
        public static bool AreCategoriesEqual(string category1, string category2)
        {
            if (string.IsNullOrWhiteSpace(category1) && string.IsNullOrWhiteSpace(category2))
                return true;

            if (string.IsNullOrWhiteSpace(category1) || string.IsNullOrWhiteSpace(category2))
                return false;

            // Çoklu karşılaştırma yöntemleri
            var methods = new[]
            {
                // 1. Direkt karşılaştırma
                () => string.Equals(category1, category2, StringComparison.OrdinalIgnoreCase),
                
                // 2. Trim + karşılaştırma
                () => string.Equals(category1.Trim(), category2.Trim(), StringComparison.OrdinalIgnoreCase),
                
                // 3. Normalize edilmiş karşılaştırma
                () => string.Equals(NormalizeCategoryName(category1), NormalizeCategoryName(category2), StringComparison.OrdinalIgnoreCase),
                
                // 4. URL segment karşılaştırması
                () => string.Equals(ToUrlSegment(category1), ToUrlSegment(category2), StringComparison.OrdinalIgnoreCase)
            };

            return methods.Any(method => method());
        }

        /// <summary>
        /// URL segment formatına çevirir
        /// </summary>
        public static string ToUrlSegment(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            return NormalizeCategoryName(input);
        }

        /// <summary>
        /// Debug için detaylı karşılaştırma bilgisi
        /// </summary>
        public static string GetComparisonDebugInfo(string category1, string category2)
        {
            var info = new StringBuilder();
            info.AppendLine($"Original: '{category1}' vs '{category2}'");
            info.AppendLine($"Trimmed: '{category1?.Trim()}' vs '{category2?.Trim()}'");
            info.AppendLine($"Normalized: '{NormalizeCategoryName(category1)}' vs '{NormalizeCategoryName(category2)}'");
            info.AppendLine($"URL Segment: '{ToUrlSegment(category1)}' vs '{ToUrlSegment(category2)}'");
            info.AppendLine($"Are Equal: {AreCategoriesEqual(category1, category2)}");
            
            return info.ToString();
        }
    }
}