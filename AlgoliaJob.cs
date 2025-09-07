namespace Vettvangur.Algolia;

internal sealed record NodeCulture(int NodeId, string Culture);
internal sealed record AlgoliaJob(
	AlgoliaJobType Type,
	IReadOnlyList<int>? NodeIds = null,
	IReadOnlyList<NodeCulture>? NodeCultures = null,
	IReadOnlyList<string>? ObjectIds = null,
	string? Culture = null,
	DateTimeOffset? ProcessAfterUtc = null
);
