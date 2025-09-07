using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Web;

namespace Vettvangur.Algolia;

internal sealed class AlgoliaIndexWorker : BackgroundService
{
	private readonly ILogger<AlgoliaIndexWorker> _logger;
	private readonly AlgoliaIndexQueue _queue;
	private readonly AlgoliaIndexExecutor _executor;
	private readonly IUmbracoContextFactory _umbracoContextFactory;

	private static readonly TimeSpan GatherWindow = TimeSpan.FromSeconds(1.5);

	// Pending aggregations (ready to process)
	private readonly Dictionary<string, HashSet<int>> _pendingUpsertsByCulture = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<int> _pendingUpserts = new();
	private readonly Dictionary<string, HashSet<string>> _pendingDeletes = new(StringComparer.OrdinalIgnoreCase);
	private bool _pendingRebuild;

	// Not-yet-due jobs (per-job delay)
	private readonly List<AlgoliaJob> _delayed = new();

	public AlgoliaIndexWorker(
		ILogger<AlgoliaIndexWorker> logger,
		AlgoliaIndexQueue queue,
		AlgoliaIndexExecutor executor,
		IUmbracoContextFactory umbracoContextFactory)
	{
		_logger = logger;
		_queue = queue;
		_executor = executor;
		_umbracoContextFactory = umbracoContextFactory;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		_logger.LogInformation("AlgoliaIndexWorker started.");

		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				// 1) Move any due delayed jobs into the ready buckets
				DrainDueDelayedJobs();

				// 2) If we already have ready work, process it now (DON'T wait on the channel)
				if (!NoWorkReady())
				{
					await ProcessReadyWorkAsync(stoppingToken);
					continue;
				}

				// 3) Nothing ready: wait for either a new item OR the next delayed job to become due
				var nextDue = _delayed.Count > 0
					? _delayed.Min(j => j.ProcessAfterUtc!.Value)
					: (DateTimeOffset?)null;

				var waitToRead = _queue.Reader.WaitToReadAsync(stoppingToken).AsTask();

				if (nextDue.HasValue)
				{
					var now = DateTimeOffset.UtcNow;
					var dueIn = nextDue.Value - now;

					if (dueIn <= TimeSpan.Zero)
					{
						// A delayed job is already due; loop back so DrainDueDelayedJobs() will pick it up.
						continue;
					}

					// Wait for either a new queue item OR the next due delayed job
					var winner = await Task.WhenAny(waitToRead, Task.Delay(dueIn, stoppingToken));
					// If delay won, loop back; DrainDueDelayedJobs will promote the job to ready work
					// If read won, fall through to pull any available jobs and then process
				}
				else
				{
					// No delayed jobs; just wait for queue input
					var hasItem = await waitToRead;
					if (!hasItem) break;
				}

				// 4) Coalesce briefly and collect any available jobs (respect ProcessAfterUtc)
				var gatherUntil = DateTimeOffset.UtcNow + GatherWindow;
				while (DateTimeOffset.UtcNow <= gatherUntil && _queue.Reader.TryRead(out var job))
				{
					if (job.ProcessAfterUtc.HasValue && job.ProcessAfterUtc.Value > DateTimeOffset.UtcNow)
					{
						_delayed.Add(job);
						continue;
					}

					Accumulate(job);
				}

				// 5) Process whatever is ready now
				await ProcessReadyWorkAsync(stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
			catch (Exception ex)
			{
				_logger.LogError(ex, "AlgoliaIndexWorker loop error");
				await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
			}
		}

		_logger.LogInformation("AlgoliaIndexWorker stopped.");
	}
	// ————————————————————— helpers —————————————————————
	private async Task ProcessReadyWorkAsync(CancellationToken ct)
	{
		// Specific-culture upserts
		if (_pendingUpsertsByCulture.Count > 0)
		{
			var snapshot = _pendingUpsertsByCulture.ToArray();
			_pendingUpsertsByCulture.Clear();

			using var cref = _umbracoContextFactory.EnsureUmbracoContext();
			var cache = cref.UmbracoContext?.Content;

			foreach (var (culture, idSet) in snapshot)
			{
				var items = RehydrateOnce(cache, idSet);
				if (items.Count == 0) continue;

				await WithRetriesAsync(ct2 => _executor.UpsertByCultureAsync(items, culture, ct2), ct);
			}
		}

		// Rebuild
		if (_pendingRebuild)
		{
			await WithRetriesAsync(ct2 => _executor.RebuildAllAsync(ct2), ct);
			_pendingRebuild = false;
			// fall-through; there might also be deletes
		}

		// Generic upserts (all live cultures)
		if (_pendingUpserts.Count > 0)
		{
			var idsToProcess = _pendingUpserts.ToArray();
			_pendingUpserts.Clear();

			using var cref = _umbracoContextFactory.EnsureUmbracoContext();
			var cache = cref.UmbracoContext?.Content;
			var items = RehydrateOnce(cache, idsToProcess);

			if (items.Count > 0)
			{
				await WithRetriesAsync(ct2 => _executor.UpsertAsync(items, ct2), ct);
			}
		}

		// Deletes
		if (_pendingDeletes.Count > 0)
		{
			var snapshot = _pendingDeletes.ToArray();
			_pendingDeletes.Clear();

			foreach (var (culture, objectIdsSet) in snapshot)
			{
				var ids = objectIdsSet.ToList();
				await WithRetriesAsync(ct2 => _executor.DeleteAsync(ids, culture, ct2), ct);
			}
		}
	}

	private void DrainDueDelayedJobs()
	{
		if (_delayed.Count == 0) return;

		var now = DateTimeOffset.UtcNow;
		for (int i = _delayed.Count - 1; i >= 0; i--)
		{
			var j = _delayed[i];
			if (!j.ProcessAfterUtc.HasValue || j.ProcessAfterUtc.Value <= now)
			{
				_delayed.RemoveAt(i);
				Accumulate(j);
			}
		}
	}

	private bool NoWorkReady()
		=> _pendingRebuild == false
		   && _pendingUpserts.Count == 0
		   && _pendingUpsertsByCulture.Count == 0
		   && _pendingDeletes.Count == 0;

	private void Accumulate(AlgoliaJob job)
	{
		switch (job.Type)
		{
			case AlgoliaJobType.RebuildAll:
				_pendingUpserts.Clear();
				_pendingDeletes.Clear();
				_pendingUpsertsByCulture.Clear();
				_pendingRebuild = true;
				break;

			case AlgoliaJobType.UpsertByIdCultures:
				if (job.NodeCultures != null)
				{
					foreach (var nc in job.NodeCultures)
					{
						if (!_pendingUpsertsByCulture.TryGetValue(nc.Culture, out var set))
							_pendingUpsertsByCulture[nc.Culture] = set = new HashSet<int>();
						set.Add(nc.NodeId);
					}
				}
				break;

			case AlgoliaJobType.UpsertByIds:
				if (job.NodeIds != null)
				{
					foreach (var id in job.NodeIds)
						_pendingUpserts.Add(id);
				}
				break;

			case AlgoliaJobType.DeleteByObjectIds:
				if (job.ObjectIds != null && !string.IsNullOrWhiteSpace(job.Culture))
				{
					var cul = job.Culture!;
					if (!_pendingDeletes.TryGetValue(cul, out var set))
						_pendingDeletes[cul] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					foreach (var oid in job.ObjectIds) set.Add(oid);
				}
				break;
		}
	}

	private static List<IPublishedContent> RehydrateOnce(IPublishedContentCache? cache, IEnumerable<int> ids)
		=> ids.Select(id => cache?.GetById(id))
			  .Where(pc => pc != null)!
			  .ToList();

	private static async Task WithRetriesAsync(Func<CancellationToken, Task> action, CancellationToken ct)
	{
		var delay = TimeSpan.FromMilliseconds(250);
		for (var attempt = 1; ; attempt++)
		{
			try { await action(ct); return; }
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch
			{
				if (attempt >= 5) throw;
				await Task.Delay(delay, ct);
				delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 5000));
			}
		}
	}
}
