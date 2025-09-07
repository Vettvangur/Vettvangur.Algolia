using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace Vettvangur.Algolia.Converters;

public sealed class ContentPickerConverter : IAlgoliaPropertyValueConverter
{
	private readonly IUmbracoContextFactory _ctxFactory;
	public int Order => 0;

	public ContentPickerConverter(IUmbracoContextFactory ctxFactory) => _ctxFactory = ctxFactory;

	public bool CanHandle(AlgoliaPropertyContext ctx)
	{
		var editor = ctx.Property.PropertyType.EditorAlias;
		return editor.Equals("Umbraco.ContentPicker", StringComparison.OrdinalIgnoreCase)
			|| editor.Equals("Umbraco.MultiNodeTreePicker", StringComparison.OrdinalIgnoreCase);
	}

	public object? Convert(AlgoliaPropertyContext ctx, object? source)
	{
		// Already resolved by Umbraco
		if (source is IPublishedContent one)
			return Shape(one, ctx.Culture);

		if (source is IEnumerable<IPublishedContent> many)
			return many.Select(x => Shape(x, ctx.Culture)).ToList();

		using var cref = _ctxFactory.EnsureUmbracoContext();
		var cache = cref.UmbracoContext?.Content;

		// UDI / GuidUDI instances
		if (source is Udi udi)
			return Shape(cache?.GetById(udi), ctx.Culture);

		if (source is GuidUdi gudi)
			return Shape(cache?.GetById(gudi), ctx.Culture);

		// Strings
		if (source is string s)
		{
			if (TryParseGuidUdiString(s, out var parsed))
				return Shape(cache?.GetById(parsed), ctx.Culture);

			// plain GUID? assume content ("document")
			if (Guid.TryParse(s, out var g))
				return Shape(cache?.GetById(new GuidUdi(Constants.UdiEntityType.Document, g)), ctx.Culture);

			// comma/whitespace-delimited list of UDIs/GUIDs
			var parts = s.Split(new[] { ',', ';', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length > 1)
			{
				var list = new List<Dictionary<string, object?>>();
				foreach (var part in parts)
				{
					if (TryParseGuidUdiString(part, out var pu))
					{
						var c = cache?.GetById(pu);
						if (c != null) list.Add(Shape(c, ctx.Culture));
						continue;
					}
					if (Guid.TryParse(part, out var pg))
					{
						var c = cache?.GetById(new GuidUdi(Constants.UdiEntityType.Document, pg));
						if (c != null) list.Add(Shape(c, ctx.Culture));
					}
				}
				if (list.Count > 0) return list;
			}
		}

		// IEnumerable<...> of Udis / strings
		if (source is IEnumerable<Udi> udis)
			return udis.Select(x => cache?.GetById(x))
					   .Where(x => x != null)!
					   .Select(x => Shape(x!, ctx.Culture)).ToList();

		if (source is IEnumerable<GuidUdi> gudis)
			return gudis.Select(x => cache?.GetById(x))
						.Where(x => x != null)!
						.Select(x => Shape(x!, ctx.Culture)).ToList();

		if (source is IEnumerable<string> sList)
		{
			var list = new List<Dictionary<string, object?>>();
			foreach (var us in sList)
			{
				if (TryParseGuidUdiString(us, out var pu))
				{
					var c = cache?.GetById(pu);
					if (c != null) list.Add(Shape(c, ctx.Culture));
				}
				else if (Guid.TryParse(us, out var g))
				{
					var c = cache?.GetById(new GuidUdi(Constants.UdiEntityType.Document, g));
					if (c != null) list.Add(Shape(c, ctx.Culture));
				}
			}
			if (list.Count > 0) return list;
		}

		// Unknown shape → pass through (will serialize if possible)
		return source;
	}

	private static Dictionary<string, object?> Shape(IPublishedContent? c, string? culture)
		=> c == null
			? new Dictionary<string, object?>()
			: new Dictionary<string, object?>
			{
				["id"] = c.Id,
				["key"] = c.Key,
				["name"] = culture == null ? c.Name : c.Name(culture),
				["url"] = culture == null ? c.Url() : c.Url(culture),
				["contentTypeAlias"] = c.ContentType.Alias,
				["level"] = c.Level,
				["parentId"] = c.Parent?.Id
			};

	// Parses "umb://{entityType}/{guid}" → GuidUdi
	private static bool TryParseGuidUdiString(string value, out GuidUdi parsed)
	{
		parsed = default;
		if (string.IsNullOrWhiteSpace(value)) return false;
		if (!value.StartsWith("umb://", StringComparison.OrdinalIgnoreCase)) return false;

		// umb://document/xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
		var rest = value.Substring("umb://".Length);
		var slash = rest.IndexOf('/');
		if (slash <= 0 || slash >= rest.Length - 1) return false;

		var entityType = rest.Substring(0, slash);         // e.g. "document"
		var guidPart = rest.Substring(slash + 1);

		if (!Guid.TryParse(guidPart, out var guid)) return false;

		parsed = new GuidUdi(entityType, guid);
		return true;
	}
}
