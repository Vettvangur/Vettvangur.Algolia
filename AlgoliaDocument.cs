namespace Vettvangur.Algolia;
public sealed class AlgoliaDocument
{
	public required string ObjectID { get; set; }
	public required int NodeId { get; set; }
	public required string ContentTypeAlias { get; set; } = string.Empty;
	public required string Url { get; set; } = string.Empty;
	public required string Name { get; set; } = string.Empty;
	public required DateTime UpdateDate { get; set; }
	public long UpdateDateUnixSecond => new DateTimeOffset(UpdateDate).ToUnixTimeSeconds();
	public required DateTime CreateDate { get; set; }
	public long CreateDateUnixSecond => new DateTimeOffset(CreateDate).ToUnixTimeSeconds();
	public IDictionary<string, object> Data { get; set; } = new Dictionary<string, object>();
}
