using Morpara.Services;
using Morpara.Services.Interfaces;
using Umbraco.Services;

namespace Morpara.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddMorparaServices(this IServiceCollection services)
        {
            // Register all services
            services.AddScoped<IContentRetrievalService, ContentRetrievalService>();
            services.AddScoped<IMorparaCultureService, MorparaCultureService>();
            services.AddScoped<IPropertyMappingService, PropertyMappingService>(); // Full implementation instead of Simple
            services.AddScoped<ICategoryService, CategoryService>();
            services.AddScoped<IBlogService, BlogService>();
            services.AddScoped<IQuestionService, QuestionService>();
            services.AddScoped<IQuestionCategoriesService, QuestionCategoriesService>();
            services.AddScoped<IMenuService, MenuService>();
            services.AddScoped<ICampaignService, CampaignService>();
            services.AddScoped<ILocationService, LocationService>();
            services.AddScoped<ISearchService, SearchService>();

            return services;
        }
    }
}
