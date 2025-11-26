namespace Morpara.Services.Interfaces
{
    public interface ILocationService
    {
        /// <summary>
        /// Gets representation items with pagination and filtering
        /// </summary>
        object GetRepresentationItems(string id, int page = 1, int pageSize = 50, string orderBy = "A-Z", 
            string city = null!, string search = null!);

        /// <summary>
        /// Gets country data from location API
        /// </summary>
        List<object>? GetCountryDataFromLocationApi();

        /// <summary>
        /// Gets city data from location API
        /// </summary>
        List<object>? GetCityDataFromLocationApi();

        /// <summary>
        /// Gets district data from location API
        /// </summary>
        List<object> GetDistrictDataFromLocationApi(string cityId = null!);

        /// <summary>
        /// Gets districts by city ID
        /// </summary>
        object GetDistricts(string cityId, string culture = "tr-TR");
    }
}
