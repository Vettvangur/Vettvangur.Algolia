using Umbraco.Cms.Core.Models.PublishedContent;

namespace Vettvangur.Algolia;
/// <summary>
/// Enqueues Algolia indexing work and lets the background worker batch/coalesce,
/// optionally deferring execution (per job) to allow the published cache to settle.
/// </summary>
public interface IAlgoliaIndexService
{
	/// <summary>
	/// Upsert a set of nodes. If <paramref name="culture"/> is null, the service will
	/// detect each node’s published cultures and write to &lt;baseIndex&gt;_&lt;culture&gt; per config.
	/// </summary>
	Task UpsertAsync(IEnumerable<IPublishedContent> nodes, TimeSpan? delay = null, CancellationToken ct = default);

	/// <summary>
	/// Upsert specific (node, culture) pairs. The worker will map only the provided culture
	/// for each node and write into the corresponding per-culture index
	/// </summary>
	Task UpsertAsync(IEnumerable<(int nodeId, string culture)> nodeCultures, TimeSpan? delay = null, CancellationToken ct = default);

	/// <summary>
	/// Delete by Algolia objectID (node.Key.ToString()) from ALL configured base indexes
	/// for the specified culture (i.e., from &lt;baseIndex&gt;_&lt;culture&gt;).
	/// </summary>
	Task DeleteAsync(IEnumerable<string> nodeKeys, string culture, CancellationToken ct = default);

	/// <summary>
	/// Full rebuild of all configured indexes: fetch only configured content types,
	/// group by culture, and write to &lt;baseIndex&gt;_&lt;culture&gt;.
	/// </summary>
	Task RebuildAllAsync(CancellationToken ct = default);
}

internal sealed class AlgoliaIndexService : IAlgoliaIndexService
{
	private readonly IAlgoliaIndexQueue _queue;

	public AlgoliaIndexService(IAlgoliaIndexQueue queue) => _queue = queue;

	public async Task UpsertAsync(IEnumerable<IPublishedContent> nodes, TimeSpan? delay = null, CancellationToken ct = default)
	{
		var ids = nodes?.Where(n => n != null).Select(n => n.Id).Distinct().ToArray() ?? [];
		if (ids.Length == 0) return;

		await _queue.EnqueueAsync(new AlgoliaJob(
			Type: AlgoliaJobType.UpsertByIds,
			NodeIds: ids,
			ProcessAfterUtc: delay.HasValue ? DateTimeOffset.UtcNow.Add(delay.Value) : null
		), ct);
	}

	public async Task UpsertAsync(IEnumerable<(int nodeId, string culture)> nodeCultures, TimeSpan? delay = null, CancellationToken ct = default)
	{
		var list = nodeCultures?
			.Where(p => !string.IsNullOrWhiteSpace(p.culture))
			.Select(p => new NodeCulture(p.nodeId, p.culture))
			.Distinct()
			.ToArray() ?? Array.Empty<NodeCulture>();

		if (list.Length == 0) return;

		await _queue.EnqueueAsync(new AlgoliaJob(
			Type: AlgoliaJobType.UpsertByIdCultures,
			NodeCultures: list,
			ProcessAfterUtc: delay.HasValue ? DateTimeOffset.UtcNow.Add(delay.Value) : null
		), ct);
	}

	public async Task DeleteAsync(IEnumerable<string> nodeKeys, string culture, CancellationToken ct = default)
	{
		var keys = nodeKeys?.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToArray() ?? [];
		if (keys.Length == 0 || string.IsNullOrWhiteSpace(culture)) return;

		await _queue.EnqueueAsync(new AlgoliaJob(
			Type: AlgoliaJobType.DeleteByObjectIds,
			ObjectIds: keys,
			Culture: culture
		), ct);
	}

	public Task RebuildAllAsync(CancellationToken ct = default)
		=> _queue.EnqueueAsync(new AlgoliaJob(AlgoliaJobType.RebuildAll), ct).AsTask();
}
