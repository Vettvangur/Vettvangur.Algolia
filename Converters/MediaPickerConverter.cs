using System.Text.Json;
using System.Text.Json.Serialization;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace Vettvangur.Algolia.Converters;

public sealed class MediaPickerConverter : IAlgoliaPropertyValueConverter
{
	private readonly IUmbracoContextFactory _ctxFactory;
	public int Order => 0;

	public MediaPickerConverter(IUmbracoContextFactory ctxFactory) => _ctxFactory = ctxFactory;

	public bool CanHandle(AlgoliaPropertyContext ctx)
		=> string.Equals(ctx.Property.PropertyType.PropertyEditorAlias, "Umbraco.MediaPicker3", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(ctx.Property.PropertyType.PropertyEditorAlias, "Umbraco.MediaPicker", StringComparison.OrdinalIgnoreCase);

	public object? Convert(AlgoliaPropertyContext ctx, object? source)
	{
		using var cref = _ctxFactory.EnsureUmbracoContext();
		var cache = cref.UmbracoContext?.Media;

		if (cache is null) return null;

		var list = new List<Dictionary<string, object?>>();

		var value = ctx.Property.GetValue(ctx.PropCulture)?.ToString();

		if (string.IsNullOrEmpty(value)) return null;

		var inputMedia = JsonSerializer.Deserialize<IEnumerable<MediaItem>>(value);

		if (inputMedia == null) return string.Empty;

		foreach (var item in inputMedia)
		{
			if (item == null) continue;

			var mediaItem = cache.GetById(Guid.Parse(item.MediaKey));

			if (mediaItem == null) continue;

			list.Add(Shape(mediaItem));
		}

		return list;
	}
	private static Dictionary<string, object?> Shape(IPublishedContent? c)
	=> c == null
		? new Dictionary<string, object?>()
		: new Dictionary<string, object?>
		{
			["name"] = c.Name,
			["url"] = c.Url()
		};

	public class MediaItem
	{
		[JsonPropertyName("key")]
		public string Key { get; set; } = string.Empty;

		[JsonPropertyName("mediaKey")]
		public string MediaKey { get; set; } = string.Empty;
	}
}
