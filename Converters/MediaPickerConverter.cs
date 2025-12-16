using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace Vettvangur.Algolia.Converters;

public sealed class MediaPickerConverter : IAlgoliaPropertyValueConverter
{
	private readonly IUmbracoContextFactory _ctxFactory;
	private readonly ILogger<MediaPickerConverter> _logger;
	public int Order => 0;

	public MediaPickerConverter(IUmbracoContextFactory ctxFactory, ILogger<MediaPickerConverter> logger)
	{
		_ctxFactory = ctxFactory;
		_logger = logger;
	}	

	public bool CanHandle(AlgoliaPropertyContext ctx)
		=> string.Equals(ctx.Property.PropertyType.PropertyEditorAlias, "Umbraco.MediaPicker3", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(ctx.Property.PropertyType.PropertyEditorAlias, "Umbraco.MediaPicker", StringComparison.OrdinalIgnoreCase);

	public object? Convert(AlgoliaPropertyContext ctx, object? source)
	{
		var list = new List<Dictionary<string, object?>>();

		try
		{
			using var cref = _ctxFactory.EnsureUmbracoContext();
			var cache = cref.UmbracoContext?.Media;

			if (cache is null) return null;

			var value = ctx.Property.GetValue(ctx.PropCulture)?.ToString();

			if (string.IsNullOrEmpty(value)) return null;

			if (value.StartsWith('['))
			{
				var inputMedia = JsonSerializer.Deserialize<IEnumerable<MediaItem>>(value);

				if (inputMedia == null) return string.Empty;

				foreach (var item in inputMedia)
				{
					if (item == null) continue;

					var mediaItem = cache.GetById(Guid.Parse(item.MediaKey));

					if (mediaItem == null) continue;

					list.Add(Shape(mediaItem));
				}
			}
			else
			{
				if (TryParseGuidUdiString(value, out var parsed))
					return Shape(cache.GetById(parsed));

				if (Guid.TryParse(value, out var g))
					return Shape(cache.GetById(new GuidUdi(Constants.UdiEntityType.Media, g)));

				var parts = value.Split([',', ';', '\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length > 1)
				{
					foreach (var part in parts)
					{
						if (TryParseGuidUdiString(part, out var pu))
						{
							var c = cache.GetById(pu);

							var shape = Shape(c);

							if (shape != null) list.Add(shape);
							continue;
						}
						if (Guid.TryParse(part, out var pg))
						{
							var c = cache.GetById(new GuidUdi(Constants.UdiEntityType.Media, pg));

							var shape = Shape(c);

							if (shape != null) list.Add(shape);
						}
					}

					// Return empty array if nothing resolved (keeps schema stable)
					return list.Count > 0 ? list.ToArray() : Array.Empty<object>();
				}

			}
		} catch(Exception ex)
		{
			_logger.LogError(ex, "Error converting media picker value for content ID {ContentId}, property alias {PropertyAlias}. Value: {Value}",
			ctx.Content.Id, ctx.Property.Alias, source?.ToString());
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

	// Parses "umb://{entityType}/{guid}" → GuidUdi
	private static bool TryParseGuidUdiString(string value, out GuidUdi parsed)
	{
		parsed = default;
		if (string.IsNullOrWhiteSpace(value)) return false;
		if (!value.StartsWith("umb://", StringComparison.OrdinalIgnoreCase)) return false;

		var rest = value.Substring("umb://".Length);
		var slash = rest.IndexOf('/');
		if (slash <= 0 || slash >= rest.Length - 1) return false;

		var entityType = rest.Substring(0, slash);
		var guidPart = rest.Substring(slash + 1);

		if (!Guid.TryParse(guidPart, out var guid)) return false;

		parsed = new GuidUdi(entityType, guid);
		return true;
	}
}
