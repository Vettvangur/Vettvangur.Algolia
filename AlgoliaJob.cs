namespace Vettvangur.Algolia;

internal sealed record AlgoliaJob(
	AlgoliaJobType Type,
	IReadOnlyList<int>? NodeIds = null,
	string? indexName = null
);
