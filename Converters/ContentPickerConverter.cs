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
		var editor = ctx.Property.PropertyType.PropertyEditorAlias;
		return editor.Equals("Umbraco.ContentPicker", StringComparison.OrdinalIgnoreCase)
			|| editor.Equals("Umbraco.MultiNodeTreePicker", StringComparison.OrdinalIgnoreCase);
	}

	public object? Convert(AlgoliaPropertyContext ctx, object? value)
	{
		using var cref = _ctxFactory.EnsureUmbracoContext();
		var cache = cref.UmbracoContext?.Content;

		// Single item already resolved
		if (value is IPublishedContent one)
			return Shape(one, ctx.Culture);

		// Multiple already resolved
		if (value is IEnumerable<IPublishedContent> many)
			return many.Select(x => Shape(x, ctx.Culture)).ToArray();

		// UDI / GuidUDI instances
		if (value is Udi udi)
			return Shape(cache?.GetById(udi), ctx.Culture);

		if (value is GuidUdi gudi)
			return Shape(cache?.GetById(gudi), ctx.Culture);

		// Strings (UDI, GUID, comma/semicolon/whitespace-delimited)
		if (value is string s)
		{
			if (TryParseGuidUdiString(s, out var parsed))
				return Shape(cache?.GetById(parsed), ctx.Culture);

			if (Guid.TryParse(s, out var g))
				return Shape(cache?.GetById(new GuidUdi(Constants.UdiEntityType.Document, g)), ctx.Culture);

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

				// Return empty array if nothing resolved (keeps schema stable)
				return list.Count > 0 ? list.ToArray() : Array.Empty<object>();
			}
		}

		// IEnumerable<...> of UDIs / strings
		if (value is IEnumerable<Udi> udis)
			return udis.Select(x => cache?.GetById(x))
								.Where(x => x != null)!
								.Select(x => Shape(x!, ctx.Culture))
								.ToArray();

		if (value is IEnumerable<GuidUdi> gudis)
			return gudis.Select(x => cache?.GetById(x))
								 .Where(x => x != null)!
								 .Select(x => Shape(x!, ctx.Culture))
								 .ToArray();

		if (value is IEnumerable<string> sList)
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
			return list.Count > 0 ? list.ToArray() : Array.Empty<object>();
		}

		return value;
	}

	private static Dictionary<string, object?> Shape(IPublishedContent? c, string? culture)
		=> c == null
			? new Dictionary<string, object?>()
			: new Dictionary<string, object?>
			{
				["name"] = culture == null ? c.Name : c.Name(culture)
			};

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
