using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Vettvangur.Algolia;

internal sealed class AlgoliaIndexWorker : BackgroundService
{
	private readonly ILogger<AlgoliaIndexWorker> _logger;
	private readonly AlgoliaIndexQueue _queue;
	private readonly AlgoliaIndexExecutor _executor;

	private readonly HashSet<int> _pendingRefresh = new();
	private bool _pendingRebuild;

	public AlgoliaIndexWorker(
		ILogger<AlgoliaIndexWorker> logger,
		AlgoliaIndexQueue queue,
		AlgoliaIndexExecutor executor)
	{
		_logger = logger;
		_queue = queue;
		_executor = executor;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		_logger.LogInformation("AlgoliaIndexWorker started.");

		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				var hasItem = await _queue.Reader.WaitToReadAsync(stoppingToken);
				if (!hasItem) break;

				// Drain everything currently available (no timers, no fetching)
				while (_queue.Reader.TryRead(out var job))
					Accumulate(job);

				await ProcessReadyWorkAsync(stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
			catch (Exception ex)
			{
				_logger.LogError(ex, "AlgoliaIndexWorker loop error");
				await Task.Delay(200, stoppingToken);
			}
		}

		_logger.LogInformation("AlgoliaIndexWorker stopped.");
	}

	private void Accumulate(AlgoliaJob job)
	{
		switch (job.Type)
		{
			case AlgoliaJobType.Rebuild:
				_pendingRefresh.Clear();
				_pendingRebuild = true;
				break;

			case AlgoliaJobType.UpdateByIds:
				if (job.NodeIds != null)
				{
					foreach (var id in job.NodeIds)
						_pendingRefresh.Add(id);
				}
				break;
		}
	}

	private async Task ProcessReadyWorkAsync(CancellationToken ct)
	{
		// Rebuild
		if (_pendingRebuild)
		{
			_pendingRefresh.Clear();
			_pendingRebuild = false;
			await _executor.RebuildAsync(null, ct);
			return;
		}

		// Refresh by IDs
		if (_pendingRefresh.Count > 0)
		{
			var ids = _pendingRefresh.ToArray();
			_pendingRefresh.Clear();

			await _executor.UpdateByIdsAsync(ids, ct);
		}
	}

}
