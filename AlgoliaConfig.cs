namespace Vettvangur.Algolia;
public class AlgoliaConfig
{
	public string ApplicationId { get; set; } = string.Empty;
	public string AdminApiKey { get; set; } = string.Empty;
	public IEnumerable<AlgoliaIndex> Indexes { get; set; } = [];
}

public class AlgoliaIndex
{
	public string IndexName { get; set; } = string.Empty;
	public IEnumerable<AlgoliaIndexContentType> ContentTypes { get; set; } = [];
}

public class AlgoliaIndexContentType
{
	public string Alias { get; set; } = string.Empty;
	public IEnumerable<string> Properties { get; set; } = [];
}
