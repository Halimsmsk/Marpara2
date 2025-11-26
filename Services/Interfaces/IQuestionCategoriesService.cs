using Umbraco.Cms.Core.Models.PublishedContent;

namespace Morpara.Services.Interfaces
{
    public interface IQuestionCategoriesService
    {
        /// <summary>
        /// Kategori alias'ına göre kategorinin belirtilen dildeki URL segment'ini alır
        /// </summary>
        /// <param name="categoryAlias">Kategori alias'ı (örn: "para-yukleme", "morpos-sanal-pos")</param>
        /// <param name="culture">Dil kodu (örn: "en", "tr-TR")</param>
        /// <returns>Kategorinin belirtilen dildeki URL segment'i</returns>
        string? GetCategoryUrlSegment(string categoryAlias, string culture);

        /// <summary>
        /// Kategori alias'ına göre kategori content'ini bulur
        /// </summary>
        /// <param name="categoryAlias">Kategori alias'ı</param>
        /// <returns>Kategori content'i</returns>
        IPublishedContent? FindCategoryByAlias(string categoryAlias);

        /// <summary>
        /// Kategori alias'ının İngilizce karşılığını alır
        /// </summary>
        /// <param name="turkishCategoryAlias">Türkçe kategori alias'ı</param>
        /// <returns>İngilizce kategori URL segment'i</returns>
        string GetEnglishCategorySegment(string turkishCategoryAlias);
    }
}
