namespace Morpara.Services.Interfaces
{
    public interface IQuestionService
    {
        /// <summary>
        /// Gets questions/FAQ items with pagination and filtering
        /// </summary>
        object GetQuestionsItems(string id, int page = 1, int pageSize = 10, string orderBy = "A-Z", 
            string category = null!, string search = null!, string activeContentId = null!);

        /// <summary>
        /// Gets SSS (Frequently Asked Questions) questions
        /// </summary>
        object GetSSSSorular(string id, string categories = null!, int? maxQuestion = null, bool? multiCategoriesContent = false);
    }
}
