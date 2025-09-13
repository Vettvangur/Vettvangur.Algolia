using Umbraco.Cms.Core.Models;

namespace Vettvangur.Algolia;
public sealed record AlgoliaEnrichmentContext(
	IContent Content,
	string? Culture,
	string BaseIndexName,
	IReadOnlySet<string>? AllowedPropertyAliases
);

public interface IAlgoliaDocumentEnricher
{
	/// <summary>Modify or add fields to the mapped Algolia document.</summary>
	void Enrich(AlgoliaDocument doc, AlgoliaEnrichmentContext ctx);

	/// <summary>Lower runs first; default 0.</summary>
	int Order => 0;
}
