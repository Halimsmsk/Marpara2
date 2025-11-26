namespace Morpara.Services.Interfaces
{
    public interface ICampaignService
    {
        /// <summary>
        /// Gets historical campaigns with pagination and filtering - original method name for compatibility
        /// </summary>
        object Gecmiskmp(string id, int page = 1, int pageSize = 10, string orderBy = "A-Z", 
            string category = null!, string campaignsType = null!);

        /// <summary>
        /// Gets related campaigns based on current campaign's categories, excluding the current campaign
        /// </summary>
        object GetRelatedCampaigns(string currentCampaignId, int maxItems = 3);
    }
}
