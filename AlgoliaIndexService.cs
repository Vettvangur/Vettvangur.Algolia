using Microsoft.Extensions.Logging;

namespace Vettvangur.Algolia;
/// <summary>
/// Enqueues Algolia indexing work and lets the background worker batch,
/// </summary>
public interface IAlgoliaIndexService
{
	/// <summary>
	/// Refresh a set of nodes. Add, Update or Remove
	/// </summary>
	Task UpdateByIdsAsync(int[] nodeId, CancellationToken ct = default);

	/// <summary>
	/// Full rebuild of all configured indexes: fetch only configured content types,
	/// group by culture, and write to &lt;baseIndex&gt;_&lt;culture&gt;.
	/// </summary>
	Task RebuildAsync(string? indexName = null, CancellationToken ct = default);
}

internal sealed class AlgoliaIndexService : IAlgoliaIndexService
{
	private readonly IAlgoliaIndexQueue _queue;
	private readonly ILogger<AlgoliaIndexService> _logger;

	public AlgoliaIndexService(IAlgoliaIndexQueue queue, ILogger<AlgoliaIndexService> logger)
	{
		_queue = queue;
		_logger = logger;
	}

	public Task UpdateByIdsAsync(int[] nodeId, CancellationToken ct = default)
	{
		if (nodeId == null || nodeId.Length == 0) return Task.CompletedTask;

		var job = new AlgoliaJob(AlgoliaJobType.UpdateByIds, nodeId);

		_queue.Enqueue(job);

		return Task.CompletedTask;
	}

	public Task RebuildAsync(string? indexName = null, CancellationToken ct = default)
	{
		var job = new AlgoliaJob(AlgoliaJobType.Rebuild, indexName: indexName);

		_queue.Enqueue(job);

		return Task.CompletedTask;
	}
}
