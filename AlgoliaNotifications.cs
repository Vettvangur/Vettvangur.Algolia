using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Changes;
using Umbraco.Cms.Core.Sync;

namespace Vettvangur.Algolia;

internal sealed class AlgoliaNotifications :
	INotificationAsyncHandler<ContentCacheRefresherNotification>
{
	private readonly IAlgoliaIndexService _indexer;
	private readonly ILogger<AlgoliaNotifications> _logger;
	private readonly IServerRoleAccessor _serverRoleAccessor;
	private readonly IRelationService _relationService;
	private readonly AlgoliaConfig _config;
	public AlgoliaNotifications(
		IAlgoliaIndexService indexer,
		ILogger<AlgoliaNotifications> logger,
		IServerRoleAccessor serverRoleAccessor,
		IRelationService relationService,
		IOptions<AlgoliaConfig> config)
	{
		_indexer = indexer;
		_logger = logger;
		_serverRoleAccessor = serverRoleAccessor;
		_relationService = relationService;
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

		var changedNodeIds = payloads
			.Where(p => p.ChangeTypes is TreeChangeTypes.RefreshNode or TreeChangeTypes.RefreshBranch)
			.Select(p => p.Id)
			.Distinct()
			.ToArray();

		if (changedNodeIds.Length == 0) return Task.CompletedTask;

		var nodeIds = changedNodeIds
			.Concat(GetReferencingPageIds(changedNodeIds))
			.Distinct()
			.ToArray();

		_ = _indexer.UpdateByIdsAsync(nodeIds, CancellationToken.None);

		return Task.CompletedTask;
	}

	private IEnumerable<int> GetReferencingPageIds(IEnumerable<int> changedNodeIds)
	{
		if (!_config.Indexes
			.SelectMany(index => index.ContentTypes)
			.Any(contentType => contentType.BlockContentPickers.Any()))
		{
			return [];
		}

		var parentIds = new HashSet<int>();

		foreach (var changedNodeId in changedNodeIds)
		{
			try
			{
				const int pageSize = 250;
				var pageIndex = 0L;
				long total;

				do
				{
					var parents = _relationService.GetPagedParentEntitiesByChildId(
						changedNodeId,
						pageIndex++,
						pageSize,
						out total,
						[UmbracoObjectTypes.Document]);

					parentIds.UnionWith(parents.Select(parent => parent.Id));
				}
				while (pageIndex * pageSize < total);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Failed to find pages referencing content ID {ContentId}", changedNodeId);
			}
		}

		return parentIds;
	}
}
