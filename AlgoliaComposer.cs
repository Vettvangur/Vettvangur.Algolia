using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;

namespace Vettvangur.Algolia;
internal sealed class AlgoliaComposer : IComposer
{
	public void Compose(IUmbracoBuilder builder)
	{
		builder
			.AddNotificationAsyncHandler<ContentCacheRefresherNotification, AlgoliaNotifications>();
			//.AddNotificationAsyncHandler<ContentPublishedNotification, AlgoliaNotifications>()
			//.AddNotificationAsyncHandler<ContentUnpublishingNotification, AlgoliaNotifications>()
			//.AddNotificationAsyncHandler<ContentMovedNotification, AlgoliaNotifications>()
			//.AddNotificationAsyncHandler<ContentMovedToRecycleBinNotification, AlgoliaNotifications>();
	}
}
