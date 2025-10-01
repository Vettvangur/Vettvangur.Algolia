using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services.Changes;
using Umbraco.Cms.Core.Sync;

namespace Vettvangur.Algolia;

internal sealed class AlgoliaNotifications :
	INotificationAsyncHandler<ContentCacheRefresherNotification>
{
	private readonly IAlgoliaIndexService _indexer;
	private readonly ILogger<AlgoliaNotifications> _logger;
	private readonly IServerRoleAccessor _serverRoleAccessor;
	private readonly AlgoliaConfig _config;
	public AlgoliaNotifications(
		IAlgoliaIndexService indexer,
		ILogger<AlgoliaNotifications> logger,
		IServerRoleAccessor serverRoleAccessor,
		IOptions<AlgoliaConfig> config)
	{
		_indexer = indexer;
		_logger = logger;
		_serverRoleAccessor = serverRoleAccessor;
		_config = config.Value;
	}

	public Task HandleAsync(ContentCacheRefresherNotification notification, CancellationToken ct)
	{
		if (notification.MessageObject is not ContentCacheRefresher.JsonPayload[] payloads)
			return Task.CompletedTask;

		if (_config.EnforcePublisherOnly)
		{
			switch (_serverRoleAccessor.CurrentServerRole)
			{
				case ServerRole.Subscriber:
				case ServerRole.Unknown:
					_logger.LogInformation("Algolia indexing task will not run on this server role.");
					return Task.CompletedTask;
			}
		}

		var nodeIds = payloads
			.Where(p => p.ChangeTypes is TreeChangeTypes.RefreshNode or TreeChangeTypes.RefreshBranch)
			.Select(p => p.Id)
			.Distinct()
			.ToArray();

		if (nodeIds.Length == 0) return Task.CompletedTask;

		_ = _indexer.UpdateByIdsAsync(nodeIds, CancellationToken.None);

		return Task.CompletedTask;
	}
}
