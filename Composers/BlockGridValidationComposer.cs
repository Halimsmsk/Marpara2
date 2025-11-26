using kraftvaerk.umbraco.blockfilter.Backend.Notifications;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Morpara.Notifications;

namespace Morpara.Composers
{
    /// <summary>
    /// Block Grid validation ve filtering handler'larýný kaydetmek için composer
    /// </summary>
    public class BlockGridValidationComposer : IComposer
    {
        public void Compose(IUmbracoBuilder builder)
        {
            // Test handler'ý kaydet
            builder.AddNotificationHandler<ContentSavingNotification, TestNotificationHandler>();
            
            // Block Grid validation handler'ý hem Saving hem Saved için kaydet
            builder.AddNotificationHandler<ContentSavingNotification, BlockGridValidationHandler>();
            
            // ContentSaved için de test edelim - belki timing problemi var
            builder.AddNotificationHandler<ContentSavedNotification, BlockGridValidationSavedHandler>();
            
            builder.AddNotificationHandler<ContentSavedNotification, AlternativeTestHandler>();

            // ? Kraftvaerk Block Filter paketi için handler'ý kaydet
            builder.AddNotificationAsyncHandler<RemodelBlockCatalogueNotification, BlockFilterNotificationHandler>();
        }
    }
}