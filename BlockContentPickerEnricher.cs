using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace Vettvangur.Algolia;

internal sealed class BlockContentPickerEnricher : IAlgoliaDocumentEnricher
{
	private static readonly Regex HtmlTags = new("<.*?>", RegexOptions.Compiled);
	private readonly AlgoliaConfig _config;
	private readonly IUmbracoContextFactory _umbracoContextFactory;
	private readonly ILogger<BlockContentPickerEnricher> _logger;

	public int Order => 0;

	public BlockContentPickerEnricher(
		IOptions<AlgoliaConfig> config,
		IUmbracoContextFactory umbracoContextFactory,
		ILogger<BlockContentPickerEnricher> logger)
	{
		_config = config.Value;
		_umbracoContextFactory = umbracoContextFactory;
		_logger = logger;
	}

	public void Enrich(AlgoliaDocument doc, AlgoliaEnrichmentContext ctx)
	{
		var rules = _config.Indexes
			.Where(index => index.IndexName.InvariantEquals(ctx.BaseIndexName))
			.SelectMany(index => index.ContentTypes)
			.Where(contentType => contentType.Alias.InvariantEquals(ctx.Content.ContentType.Alias))
			.SelectMany(contentType => contentType.BlockContentPickers)
			.Where(IsValid)
			.ToArray();

		if (rules.Length == 0) return;

		using var contextReference = _umbracoContextFactory.EnsureUmbracoContext();
		var content = contextReference.UmbracoContext?.Content?.GetById(ctx.Content.Key);
		if (content is null) return;

		foreach (var rule in rules)
		{
			try
			{
				var text = GetSearchText(content, rule, ctx.Culture);
				if (!string.IsNullOrWhiteSpace(text))
					AddText(doc, rule.OutputAlias, text);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex,
					"Failed to index block content picker for content ID {ContentId}, container {ContainerAlias}",
					ctx.Content.Id,
					rule.ContainerAlias);
			}
		}
	}

	private static void AddText(AlgoliaDocument doc, string outputAlias, string text)
	{
		if (doc.Data.TryGetValue(outputAlias, out var existing) && existing is string existingText)
			text = $"{existingText} {text}";

		doc.TryAddField(outputAlias, text);
	}

	private static bool IsValid(AlgoliaBlockContentPicker rule)
		=> !string.IsNullOrWhiteSpace(rule.ContainerAlias)
			&& !string.IsNullOrWhiteSpace(rule.OutputAlias)
			&& rule.BlockAliases.Any()
			&& rule.PickerAliases.Any()
			&& rule.ReferencedProperties.Any();

	private static string GetSearchText(IPublishedContent content, AlgoliaBlockContentPicker rule, string? culture)
	{
		var blocks = GetBlocks(content, rule.ContainerAlias, culture);
		var allowedBlocks = rule.BlockAliases.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var values = new List<string>();

		foreach (var block in blocks.Where(block => allowedBlocks.Contains(block.ContentType.Alias)))
		{
			foreach (var pickerAlias in rule.PickerAliases)
			{
				foreach (var pickedContent in GetPickedContent(block.Value(pickerAlias, culture)))
				{
					foreach (var propertyAlias in rule.ReferencedProperties)
					{
						var value = ToSearchText(pickedContent.Value(propertyAlias, culture));
						if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
					}
				}
			}
		}

		return string.Join(' ', values.Distinct(StringComparer.Ordinal));
	}

	private static IEnumerable<IPublishedElement> GetBlocks(IPublishedContent content, string containerAlias, string? culture)
	{
		var value = content.Value(containerAlias, culture);
		return value switch
		{
			BlockListModel list => list.Select(item => item.Content),
			BlockGridModel grid => GetGridBlocks(grid),
			_ => Enumerable.Empty<IPublishedElement>()
		};
	}

	private static IEnumerable<IPublishedElement> GetGridBlocks(IEnumerable<BlockGridItem> items)
	{
		foreach (var item in items)
		{
			yield return item.Content;

			foreach (var area in item.Areas)
			{
				foreach (var child in GetGridBlocks(area))
					yield return child;
			}
		}
	}

	private static IEnumerable<IPublishedContent> GetPickedContent(object? value)
		=> value switch
		{
			IPublishedContent content => [content],
			IEnumerable<IPublishedContent> contents => contents,
			_ => Enumerable.Empty<IPublishedContent>()
		};

	private static string? ToSearchText(object? value)
	{
		if (value is null) return null;

		var text = HtmlTags.Replace(WebUtility.HtmlDecode(value.ToString() ?? string.Empty), " ");
		return string.IsNullOrWhiteSpace(text) ? null : text;
	}
}
