using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Events;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;

namespace Morpara.Composers
{
    /// <summary>
    /// Composer to disable DocumentUrlService cache refresh notifications
    /// This prevents the expensive DocumentUrlRepository.Save operation that causes timeouts
    /// </summary>
    public class DisableDocumentUrlServiceComposer : IComposer
    {
        public void Compose(IUmbracoBuilder builder)
        {
            // Remove ContentTreeChangeDistributedCacheNotificationHandler from DI
            // This handler triggers DocumentUrlService.CreateOrUpdateUrlSegmentsAsync which causes timeout
            var descriptor = builder.Services.FirstOrDefault(d => 
                d.ServiceType == typeof(INotificationHandler<ContentCacheRefresherNotification>) &&
                d.ImplementationType == typeof(ContentTreeChangeDistributedCacheNotificationHandler));
            
            if (descriptor != null)
            {
                builder.Services.Remove(descriptor);
            }
        }
    }
}
