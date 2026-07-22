namespace Vettvangur.Algolia;
public class AlgoliaConfig
{
	public string ApplicationId { get; set; } = string.Empty;
	public string AdminApiKey { get; set; } = string.Empty;
	public string SearchApiKey { get; set; } = string.Empty;
	public string? Environment { get; set; }
	public bool SearchCacheEnabled { get; set; }
	public int SearchCacheDurationMinutes { get; set; } = 5;
	public bool IncludeUserToken { get; set; } = true;
	public bool VaryCacheByUserToken { get; set; }
	public bool EnforcePublisherOnly { get; set; } = true;
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
	public IEnumerable<AlgoliaBlockContentPicker> BlockContentPickers { get; set; } = [];
}

public class AlgoliaBlockContentPicker
{
	public string ContainerAlias { get; set; } = string.Empty;
	public IEnumerable<string> BlockAliases { get; set; } = [];
	public IEnumerable<string> PickerAliases { get; set; } = [];
	public IEnumerable<string> ReferencedProperties { get; set; } = [];
	public string OutputAlias { get; set; } = string.Empty;
}
