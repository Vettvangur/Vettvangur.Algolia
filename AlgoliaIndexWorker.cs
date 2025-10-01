using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Vettvangur.Algolia;

internal sealed class AlgoliaIndexWorker : BackgroundService
{
	private readonly ILogger<AlgoliaIndexWorker> _logger;
	private readonly IAlgoliaIndexQueue _queue;
	private readonly IServiceScopeFactory _scopeFactory;

	private readonly HashSet<int> _pendingRefresh = new();
	private bool _pendingRebuild;

	public AlgoliaIndexWorker(
		ILogger<AlgoliaIndexWorker> logger,
		IAlgoliaIndexQueue queue,
		IServiceScopeFactory scopeFactory)
	{
		_logger = logger;
		_queue = queue;
		_scopeFactory = scopeFactory;
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

				using var scope = _scopeFactory.CreateScope();
				var executor = scope.ServiceProvider.GetRequiredService<AlgoliaIndexExecutor>();

				await ProcessReadyWorkAsync(executor, stoppingToken);
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

	private async Task ProcessReadyWorkAsync(AlgoliaIndexExecutor executor, CancellationToken ct)
	{
		// Rebuild
		if (_pendingRebuild)
		{
			_pendingRefresh.Clear();
			_pendingRebuild = false;

			await executor.RebuildAsync(null, ct);
			return;
		}

		// Refresh by IDs
		if (_pendingRefresh.Count > 0)
		{
			var ids = _pendingRefresh.ToArray();
			_pendingRefresh.Clear();

			await executor.UpdateByIdsAsync(ids, ct);
		}
	}

}
