namespace Vettvangur.Algolia;
internal sealed class AlgoliaDocument
{
	public required string ObjectID { get; set; }
	public required int NodeId { get; set; }
	public required string ContentTypeAlias { get; set; } = string.Empty;
	public required string Url { get; set; } = string.Empty;
	public required string Name { get; set; } = string.Empty;
	public required int Level { get; set; }
	public required DateTime UpdateDate { get; set; }
	public long UpdateDateUnixMs => new DateTimeOffset(UpdateDate).ToUnixTimeMilliseconds();
	public required DateTime CreateDate { get; set; }
	public long CreateDateUnixMs => new DateTimeOffset(CreateDate).ToUnixTimeMilliseconds();
	public IDictionary<string, object> Data { get; set; }
}
