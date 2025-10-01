using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace Vettvangur.Algolia;
internal interface IAlgoliaIndexQueue
{
	void Enqueue(AlgoliaJob job);
	ChannelReader<AlgoliaJob> Reader { get; }
}

internal sealed class AlgoliaIndexQueue : IAlgoliaIndexQueue
{
	private readonly Channel<AlgoliaJob> _channel;
	public ChannelReader<AlgoliaJob> Reader => _channel.Reader;
	private readonly ILogger<AlgoliaIndexQueue> _logger;
	public AlgoliaIndexQueue(ILogger<AlgoliaIndexQueue> logger)
	{
		var options = new BoundedChannelOptions(10_000)
		{
			SingleReader = true,
			SingleWriter = false,
			FullMode = BoundedChannelFullMode.DropOldest
		};
		_channel = Channel.CreateBounded<AlgoliaJob>(options);
		_logger = logger;
	}

	public void Enqueue(AlgoliaJob job)
	{
		var ok = _channel.Writer.TryWrite(job);

		if (!ok)
			_logger.LogWarning("Algolia queue full. Dropped {Type} ({Count} ids).",
				job.Type, job.NodeIds?.Count ?? 0);
	}
}
