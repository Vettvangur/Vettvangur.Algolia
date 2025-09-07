using Umbraco.Cms.Core.Models.PublishedContent;

namespace Vettvangur.Algolia;
public interface IAlgoliaPropertyValueConverter
{
	int Order => 0; // lower runs first

	bool CanHandle(AlgoliaPropertyContext ctx);

	/// Return the transformed value (must be JSON-serializable), or the original value.
	object? Convert(AlgoliaPropertyContext ctx, object? source);
}

public sealed record AlgoliaPropertyContext(
	IPublishedContent Content,
	IPublishedProperty Property,
	string? Culture,
	string BaseIndexName
);
