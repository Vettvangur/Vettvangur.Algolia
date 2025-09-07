using System.Threading.Channels;

namespace Vettvangur.Algolia;
internal interface IAlgoliaIndexQueue
{
	ValueTask EnqueueAsync(AlgoliaJob job, CancellationToken ct = default);
}

internal sealed class AlgoliaIndexQueue : IAlgoliaIndexQueue
{
	private readonly Channel<AlgoliaJob> _channel;
	public ChannelReader<AlgoliaJob> Reader => _channel.Reader;

	public AlgoliaIndexQueue()
	{
		var options = new BoundedChannelOptions(10_000)
		{
			SingleReader = true,
			SingleWriter = false,
			FullMode = BoundedChannelFullMode.Wait
		};
		_channel = Channel.CreateBounded<AlgoliaJob>(options);
	}

	public ValueTask EnqueueAsync(AlgoliaJob job, CancellationToken ct = default)
		=> _channel.Writer.WriteAsync(job, ct);
}
