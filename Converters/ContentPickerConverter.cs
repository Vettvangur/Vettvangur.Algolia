using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace Vettvangur.Algolia.Converters;

public sealed class ContentPickerConverter : IAlgoliaPropertyValueConverter
{
	private readonly ILogger<ContentPickerConverter> _logger;
	private readonly IUmbracoContextFactory _ctxFactory;
	public int Order => 0;

	public ContentPickerConverter(IUmbracoContextFactory ctxFactory, ILogger<ContentPickerConverter> logger)
	{
		_ctxFactory = ctxFactory;
		_logger = logger;
	}

	public bool CanHandle(AlgoliaPropertyContext ctx)
	{
		var editor = ctx.Property.PropertyType.PropertyEditorAlias;
		return editor.Equals("Umbraco.ContentPicker", StringComparison.OrdinalIgnoreCase)
			|| editor.Equals("Umbraco.MultiNodeTreePicker", StringComparison.OrdinalIgnoreCase);
	}

	public object? Convert(AlgoliaPropertyContext ctx, object? value)
	{
		try
		{
			using var cref = _ctxFactory.EnsureUmbracoContext();
			var cache = cref.UmbracoContext?.Content;

			// Single item already resolved
			if (value is IPublishedContent one)
				return Shape(one, ctx.NodeCulture);

			// Multiple already resolved
			if (value is IEnumerable<IPublishedContent> many)
				return many.Select(x => Shape(x, ctx.NodeCulture)).ToArray();

			// UDI / GuidUDI instances
			if (value is Udi udi)
				return Shape(cache?.GetById(udi), ctx.NodeCulture);

			if (value is GuidUdi gudi)
				return Shape(cache?.GetById(gudi), ctx.NodeCulture);

			// Strings (UDI, GUID, comma/semicolon/whitespace-delimited)
			if (value is string s)
			{
				if (TryParseGuidUdiString(s, out var parsed))
					return Shape(cache?.GetById(parsed), ctx.NodeCulture);

				if (Guid.TryParse(s, out var g))
					return Shape(cache?.GetById(new GuidUdi(Constants.UdiEntityType.Document, g)), ctx.NodeCulture);

				var parts = s.Split(new[] { ',', ';', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length > 1)
				{
					var list = new List<Dictionary<string, object?>>();
					foreach (var part in parts)
					{
						if (TryParseGuidUdiString(part, out var pu))
						{
							var c = cache?.GetById(pu);

							var shape = Shape(c, ctx.NodeCulture);

							if (shape != null) list.Add(shape);
							continue;
						}
						if (Guid.TryParse(part, out var pg))
						{
							var c = cache?.GetById(new GuidUdi(Constants.UdiEntityType.Document, pg));

							var shape = Shape(c, ctx.NodeCulture);

							if (shape != null) list.Add(shape);
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
									.Select(x => Shape(x, ctx.NodeCulture))
									.Where(x => x != null)
									.ToArray();

			if (value is IEnumerable<GuidUdi> gudis)
				return gudis.Select(x => cache?.GetById(x))
									 .Where(x => x != null)!
									 .Select(x => Shape(x, ctx.NodeCulture))
									 .Where(x => x != null)
									 .ToArray();

			if (value is IEnumerable<string> sList)
			{
				var list = new List<Dictionary<string, object?>>();
				foreach (var us in sList)
				{
					if (TryParseGuidUdiString(us, out var pu))
					{
						var c = cache?.GetById(pu);

						var shape = Shape(c, ctx.NodeCulture);

						if (shape != null) list.Add(shape);
					}
					else if (Guid.TryParse(us, out var g))
					{
						var c = cache?.GetById(new GuidUdi(Constants.UdiEntityType.Document, g));

						var shape = Shape(c, ctx.NodeCulture);

						if (shape != null) list.Add(shape);
					}
				}
				return list.Count > 0 ? list.ToArray() : Array.Empty<object>();
			}

			return value;

		} catch (Exception ex)
		{
			_logger.LogError(ex, "Error converting content picker value for content ID {ContentId}, property alias {PropertyAlias}. Value: {Value}",
				ctx.Content.Id, ctx.Property.Alias, value?.ToString());
			return value;
		}

		
	}

	private static Dictionary<string, object?>? Shape(IPublishedContent? c, string? culture)
	{
		if (c == null) { return null; }

		var name = culture == null ? c.Name : c.IsInvariantOrHasCulture(culture) ? c.Name(culture) : c.Name;

		if (string.IsNullOrEmpty(name)) { return null; }

		return new Dictionary<string, object?>
		{
			["name"] = name
		};
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
