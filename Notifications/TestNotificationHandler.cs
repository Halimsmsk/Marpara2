using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Morpara.Notifications
{
    /// <summary>
    /// Test amaçlý basit notification handler - çalýþýp çalýþmadýðýný anlamak için
    /// </summary>
    public class TestNotificationHandler : INotificationHandler<ContentSavingNotification>
    {
        private readonly ILogger<TestNotificationHandler> _logger;

        public TestNotificationHandler(ILogger<TestNotificationHandler> logger)
        {
            _logger = logger;
        }

        public void Handle(ContentSavingNotification notification)
        {
            _logger.LogWarning("?? TEST HANDLER ÇALIÞIYOR! Content count: {Count}", 
                notification.SavedEntities.Count());

            foreach (var content in notification.SavedEntities)
            {
                _logger.LogWarning("?? Content kaydediliyor: {Name} (Type: {Type})", 
                    content.Name, content.ContentType.Alias);
            }
        }
    }
}