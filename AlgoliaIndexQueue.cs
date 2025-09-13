using System.Threading.Channels;

namespace Vettvangur.Algolia;
internal interface IAlgoliaIndexQueue
{
	bool TryEnqueue(AlgoliaJob job);
	ValueTask EnqueueAsync(AlgoliaJob job, CancellationToken ct = default);
	ChannelReader<AlgoliaJob> Reader { get; }
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

	public bool TryEnqueue(AlgoliaJob job) => _channel.Writer.TryWrite(job);

	public ValueTask EnqueueAsync(AlgoliaJob job, CancellationToken ct = default)
		=> _channel.Writer.WriteAsync(job, ct);
}
