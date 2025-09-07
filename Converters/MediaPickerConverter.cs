using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Extensions;

namespace Vettvangur.Algolia.Converters;

public sealed class MediaPickerConverter : IAlgoliaPropertyValueConverter
{
	public int Order => 0;

	public bool CanHandle(AlgoliaPropertyContext ctx)
		=> string.Equals(ctx.Property.PropertyType.EditorAlias, "Umbraco.MediaPicker3", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(ctx.Property.PropertyType.EditorAlias, "Umbraco.MediaPicker", StringComparison.OrdinalIgnoreCase);

	public object? Convert(AlgoliaPropertyContext ctx, object? source)
	{
		return source switch
		{
			IEnumerable<IPublishedContent> many => many.Select(m => ToShape(m, ctx.Culture)).ToList(),
			IPublishedContent one => ToShape(one, ctx.Culture),
			_ => source
		};
	}

	private static Dictionary<string, object?> ToShape(IPublishedContent m, string? culture) => new()
	{
		["id"] = m.Id,
		["key"] = m.Key,
		["name"] = culture == null ? m.Name : m.Name(culture),
		["url"] = culture == null ? m.Url() : m.Url(culture),
		["type"] = m.ContentType.Alias
	};
}
