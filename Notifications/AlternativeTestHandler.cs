using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Morpara.Notifications
{
    /// <summary>
    /// Alternatif event testi için handler
    /// </summary>
    public class AlternativeTestHandler : INotificationHandler<ContentSavedNotification>
    {
        private readonly ILogger<AlternativeTestHandler> _logger;

        public AlternativeTestHandler(ILogger<AlternativeTestHandler> logger)
        {
            _logger = logger;
        }

        public void Handle(ContentSavedNotification notification)
        {
            _logger.LogWarning("?? ALTERNATIVE HANDLER (ContentSaved) ÇALIÞIYOR! Content count: {Count}", 
                notification.SavedEntities.Count());

            foreach (var content in notification.SavedEntities)
            {
                _logger.LogWarning("?? Content kaydedildi: {Name} (Type: {Type})", 
                    content.Name, content.ContentType.Alias);
            }
        }
    }
}