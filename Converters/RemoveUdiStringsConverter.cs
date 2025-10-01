using System.Text.RegularExpressions;

namespace Vettvangur.Algolia.Converters;

public sealed class RemoveUdiStringsFromRTEConverter : IAlgoliaPropertyValueConverter
{
	public int Order => int.MaxValue; // run very late

	public bool CanHandle(AlgoliaPropertyContext ctx)
		=> string.Equals(ctx.Property.PropertyType.PropertyEditorAlias, "Umbraco.TinyMCE", StringComparison.OrdinalIgnoreCase);

	public object? Convert(AlgoliaPropertyContext ctx, object? source)
	{
		if (source is not string s) return source;

		// Match umb://{entity}/<guid> where <guid> can be 32 hex (no dashes) OR standard dashed GUID
		const string guidNoDashes = @"[0-9a-fA-F]{32}";
		const string guidDashed = @"[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}";
		var pattern = $@"umb://[a-z]+/(?:{guidNoDashes}|{guidDashed})";

		var noUdi = Regex.Replace(
			s,
			pattern,
			string.Empty,
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
		);

		// Collapse extra whitespace after removing UDIs
		noUdi = Regex.Replace(noUdi, @"\s{2,}", " ").Trim();

		return string.IsNullOrWhiteSpace(noUdi) ? null : noUdi;
	}
}
