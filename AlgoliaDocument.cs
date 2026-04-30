using System.Text.Json.Serialization;

namespace Vettvangur.Algolia;
public sealed class AlgoliaDocument
{
	private static readonly HashSet<string> ReservedFieldNames = new(StringComparer.OrdinalIgnoreCase)
	{
		"objectID",
		"nodeId",
		"contentTypeAlias",
		"url",
		"name",
		"updateDate",
		"updateDateUnixSecond",
		"createDate",
		"createDateUnixSecond"
	};

	public required string ObjectID { get; set; }
	public required int NodeId { get; set; }
	public required string ContentTypeAlias { get; set; } = string.Empty;
	public required string Url { get; set; } = string.Empty;
	public required string Name { get; set; } = string.Empty;
	public required DateTime UpdateDate { get; set; }
	public long UpdateDateUnixSecond => new DateTimeOffset(UpdateDate).ToUnixTimeSeconds();
	public required DateTime CreateDate { get; set; }
	public long CreateDateUnixSecond => new DateTimeOffset(CreateDate).ToUnixTimeSeconds();
	[JsonExtensionData]
	public IDictionary<string, object> Data { get; set; } = new Dictionary<string, object>();

	public bool TryAddField(string fieldName, object value)
	{
		if (string.IsNullOrWhiteSpace(fieldName) || ReservedFieldNames.Contains(fieldName))
			return false;

		Data[fieldName] = value;
		return true;
	}

	public void RemoveReservedFields()
	{
		foreach (var fieldName in ReservedFieldNames)
			Data.Remove(fieldName);
	}
}
