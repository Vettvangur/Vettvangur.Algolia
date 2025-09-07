using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Web;

namespace Vettvangur.Algolia;

internal sealed class AlgoliaNotifications :
	INotificationAsyncHandler<ContentPublishedNotification>,
	INotificationAsyncHandler<ContentUnpublishingNotification>,
	INotificationAsyncHandler<ContentMovedNotification>,
	INotificationAsyncHandler<ContentMovedToRecycleBinNotification>
{
	private readonly IAlgoliaIndexService _indexer;
	private readonly IUmbracoContextFactory _ctxFactory;
	private readonly ILogger<AlgoliaNotifications> _logger;

	public AlgoliaNotifications(
		IAlgoliaIndexService indexer,
		IUmbracoContextFactory ctxFactory,
		ILogger<AlgoliaNotifications> logger)
	{
		_indexer = indexer;
		_ctxFactory = ctxFactory;
		_logger = logger;
	}

	// PUBLISHED → upsert only if this event published anything (or no culture delta); 
	// always delete cultures that were explicitly unpublished in this event.
	public async Task HandleAsync(ContentPublishedNotification notification, CancellationToken ct)
	{
		try
		{
			// Build exact upsert pairs and delete-by-culture using only IContent
			var toUpsert = new List<(int nodeId, string culture)>();
			var deleteByCulture = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

			foreach (var e in notification.PublishedEntities) // IContent
			{
				var objectId = e.Key.ToString();
				var available = e.AvailableCultures ?? Enumerable.Empty<string>();

				var newlyPublished = available.Where(c => notification.HasPublishedCulture(e, c)).ToList();
				var newlyUnpublished = available.Where(c => notification.HasUnpublishedCulture(e, c)).ToList();

				// Deletions for cultures explicitly unpublished in this operation
				foreach (var cul in newlyUnpublished)
				{
					if (!deleteByCulture.TryGetValue(cul, out var list))
						deleteByCulture[cul] = list = new List<string>();
					list.Add(objectId);
				}

				// Upserts:
				// - if any cultures were published in this event → upsert those precise cultures
				// - if there was NO culture delta (republish/content update) → upsert all currently published cultures
				if (newlyPublished.Count > 0)
				{
					foreach (var cul in newlyPublished)
						toUpsert.Add((e.Id, cul));
				}
				else if (newlyPublished.Count == 0 && newlyUnpublished.Count == 0)
				{
					// no culture delta → use e.PublishedCultures (state AFTER publish)
					foreach (var cul in e.PublishedCultures ?? Enumerable.Empty<string>())
						toUpsert.Add((e.Id, cul));
				}
				// else: only unpublishes happened → no upsert here
			}

			// Upsert exact cultures
			if (toUpsert.Count > 0)
				await _indexer.UpsertAsync(toUpsert, delay: TimeSpan.FromSeconds(5), ct);

			// Delete cultures that are no longer live
			foreach (var (culture, ids) in deleteByCulture)
				await _indexer.DeleteAsync(ids.Distinct(), culture, ct);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Algolia: publish indexing failed");
		}
	}

	// UNPUBLISHING (before) → full unpublish: delete all cultures (the event is raised when all variants are being unpublished).
	public async Task HandleAsync(ContentUnpublishingNotification notification, CancellationToken ct)
	{
		try
		{
			var byCulture = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

			foreach (var content in notification.UnpublishedEntities) // IContent
			{
				var objectId = content.Key.ToString();
				var available = content.AvailableCultures ?? Enumerable.Empty<string>();

				// Docs: Unpublishing is raised for complete unpublishing; delete across all variants
				foreach (var cul in available)
				{
					if (!byCulture.TryGetValue(cul, out var list))
						byCulture[cul] = list = new List<string>();
					list.Add(objectId);
				}
			}

			foreach (var (culture, ids) in byCulture)
				await _indexer.DeleteAsync(ids.Distinct(), culture, ct);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Algolia: unpublish delete failed");
		}
	}

	// MOVED (reordered / parent change) → upsert affected
	public async Task HandleAsync(ContentMovedNotification notification, CancellationToken ct)
	{
		try
		{
			using var cref = _ctxFactory.EnsureUmbracoContext();
			var items = notification.MoveInfoCollection
				.Select(mi => cref.UmbracoContext?.Content?.GetById(mi.Entity.Id))
				.Where(pc => pc != null)!;

			await _indexer.UpsertAsync(items!, null, ct);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Algolia: move reindex failed");
		}
	}

	// MOVED TO RECYCLE BIN → delete (all cultures the item had published)
	public async Task HandleAsync(ContentMovedToRecycleBinNotification notification, CancellationToken ct)
	{
		try
		{
			var byCulture = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

			foreach (var mi in notification.MoveInfoCollection)
			{
				var content = mi.Entity;
				var objectId = content.Key.ToString();
				var cultures = content.PublishedCultures ?? Enumerable.Empty<string>();

				foreach (var cul in cultures)
				{
					if (!byCulture.TryGetValue(cul, out var list))
						byCulture[cul] = list = new List<string>();
					list.Add(objectId);
				}
			}

			foreach (var (culture, ids) in byCulture)
			{
				await _indexer.DeleteAsync(ids.Distinct(StringComparer.OrdinalIgnoreCase), culture, ct);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Algolia: recycle-bin delete failed");
		}
	}
}
