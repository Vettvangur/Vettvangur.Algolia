using Algolia.Search.Clients;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.UmbracoSettings;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace Vettvangur.Algolia;

internal sealed class AlgoliaIndexExecutor
{
	private readonly ILogger<AlgoliaIndexExecutor> _logger;
	private readonly IUmbracoContextFactory _umbracoContextFactory;
	private readonly ISearchClient _client;
	private readonly AlgoliaConfig _config;
	private readonly IReadOnlyList<IAlgoliaDocumentEnricher> _enrichers;
	private readonly IReadOnlyList<IAlgoliaPropertyValueConverter> _propConverters;
	private readonly IPublishedValueFallback _publishedValueFallback;
	private readonly IVariationContextAccessor _variation;
	public AlgoliaIndexExecutor(
		ILogger<AlgoliaIndexExecutor> logger,
		IUmbracoContextFactory umbracoContextFactory,
		ISearchClient client,
		IOptions<AlgoliaConfig> config,
		IPublishedValueFallback publishedValueFallback,
		IVariationContextAccessor variation,
		IEnumerable<IAlgoliaDocumentEnricher>? enrichers = null,
		IEnumerable<IAlgoliaPropertyValueConverter>? propConverters = null
)
	{
		_logger = logger;
		_umbracoContextFactory = umbracoContextFactory;
		_client = client;
		_config = config.Value;
		_enrichers = (enrichers ?? Array.Empty<IAlgoliaDocumentEnricher>())
			  .OrderBy(e => e.Order)
			  .ToList();
		_propConverters = (propConverters ?? Array.Empty<IAlgoliaPropertyValueConverter>())
						  .OrderBy(c => c.Order).ToList();
		_publishedValueFallback = publishedValueFallback;
		_variation = variation;
	}

	// ---------- Public API ----------

	public async Task RebuildAllAsync(CancellationToken ct = default)
	{
		_logger.LogInformation("Starting full Algolia index rebuild...");

		using var cref = _umbracoContextFactory.EnsureUmbracoContext();
		var cache = cref.UmbracoContext?.Content;
		if (cache is null) return;

		foreach (var spec in BuildSpecs())
		{
			// Gather all nodes matching this index' content types (deduped)
			var nodesById = new Dictionary<int, IPublishedContent>();

			foreach (var alias in spec.Aliases)
			{
				var ctype = cache.GetContentType(alias);
				if (ctype is null) continue;

				var nodes = cache.GetByContentType(ctype);
				if (nodes is null) continue;

				foreach (var n in nodes) nodesById[n.Id] = n;
			}

			if (nodesById.Count == 0) continue;

			await UpsertForIndexAsync(
				baseIndexName: spec.IndexName,
				nodes: nodesById.Values,
				propsByAlias: spec.PropsByAlias,
				onlyCulture: null,
				ct: ct);

			_logger.LogInformation("Rebuilt Algolia index '{IndexName}' with {NodeCount} nodes.",
				spec.IndexName, nodesById.Count);
		}
	}

	public async Task UpsertAsync(IEnumerable<IPublishedContent> nodes, CancellationToken ct = default)
	{
		var list = nodes?.Where(n => n != null).DistinctBy(n => n.Id).ToList() ?? new();
		if (list.Count == 0) return;

		using var _ = _umbracoContextFactory.EnsureUmbracoContext(); // ensure Url()/Value() work

		foreach (var spec in BuildSpecs())
		{
			var filtered = FilterForIndex(list, spec.Aliases);
			if (filtered.Count == 0) continue;

			await UpsertForIndexAsync(
				baseIndexName: spec.IndexName,
				nodes: filtered,
				propsByAlias: spec.PropsByAlias,
				onlyCulture: null,
				ct: ct);
		}
	}

	public async Task UpsertByCultureAsync(IEnumerable<IPublishedContent> nodes, string culture, CancellationToken ct = default)
	{
		var list = nodes?.Where(n => n != null).DistinctBy(n => n.Id).ToList() ?? new();
		if (list.Count == 0 || string.IsNullOrWhiteSpace(culture)) return;

		using var _ = _umbracoContextFactory.EnsureUmbracoContext();

		foreach (var spec in BuildSpecs())
		{
			var filtered = FilterForIndex(list, spec.Aliases);
			if (filtered.Count == 0) continue;

			await UpsertForIndexAsync(
				baseIndexName: spec.IndexName,
				nodes: filtered,
				propsByAlias: spec.PropsByAlias,
				onlyCulture: culture,
				ct: ct);
		}
	}

	public async Task DeleteAsync(IEnumerable<string> nodeKeys, string culture, CancellationToken ct = default)
	{
		if (string.IsNullOrWhiteSpace(culture)) return;

		var ids = nodeKeys?
			.Where(k => !string.IsNullOrWhiteSpace(k))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList() ?? new();

		if (ids.Count == 0) return;

		var cul = culture.ToLowerInvariant();

		foreach (var idx in _config.Indexes)
		{
			if (string.IsNullOrWhiteSpace(idx.IndexName)) continue;

			var indexName = $"{idx.IndexName}_{cul}";
			_logger.LogDebug("Delete {Count} objects from {IndexName}", ids.Count, indexName);

			await _client.DeleteObjectsAsync(
				indexName: indexName,
				objectIDs: ids,
				batchSize: 1000,
				waitForTasks: false,
				options: null,
				cancellationToken: ct);
		}
	}

	// ---------- Internals ----------

	/// Unifies the "all cultures" and "specific culture" upsert paths.
	private async Task UpsertForIndexAsync(
		string baseIndexName,
		IEnumerable<IPublishedContent> nodes,
		IDictionary<string, HashSet<string>> propsByAlias,
		string? onlyCulture,
		CancellationToken ct)
	{

		var byCulture = new Dictionary<string, List<AlgoliaDocument>>(StringComparer.OrdinalIgnoreCase);

		foreach (var node in nodes)
		{
			propsByAlias.TryGetValue(node.ContentType.Alias, out var allowedProps);

			// When onlyCulture is provided, process just that culture; otherwise, all node cultures.
			var cultures = onlyCulture != null
				? new[] { onlyCulture }
				: (node.Cultures?.Keys ?? Enumerable.Empty<string>());

			foreach (var cul in cultures)
			{
				if (string.IsNullOrWhiteSpace(cul)) continue;            

				if (!node.IsPublished(cul)) continue;

				AlgoliaDocument? doc = null;
				try
				{
					doc = MapAndEnrichForCulture(node, cul, baseIndexName, allowedProps);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex,
						"Failed to map/enrich node {Id} for culture {Culture}", node.Id, cul);
				}
				if (doc == null) continue;

				if (!byCulture.TryGetValue(cul, out var list))
					byCulture[cul] = list = new List<AlgoliaDocument>();
				list.Add(doc);
				
			}
		}

		// Save per culture
		foreach (var (culture, docs) in byCulture)
		{
			if (docs.Count == 0) continue;

			var indexName = $"{baseIndexName}_{culture.ToLowerInvariant()}";
			_logger.LogDebug("Save {Count} objects to {IndexName}", docs.Count, indexName);

			await _client.SaveObjectsAsync(
				indexName: indexName,
				objects: docs,
				batchSize: 1000,
				waitForTasks: false,
				options: null,
				cancellationToken: ct);
		}
	}

	private static List<IPublishedContent> FilterForIndex(IEnumerable<IPublishedContent> nodes, HashSet<string> aliases)
		=> nodes.Where(n => aliases.Contains(n.ContentType.Alias))
				.DistinctBy(n => n.Id)
				.ToList();

	private IEnumerable<IndexSpec> BuildSpecs()
	{
		foreach (var idx in _config.Indexes)
		{
			if (string.IsNullOrWhiteSpace(idx.IndexName)) continue;

			var aliases = (idx.ContentTypes ?? Enumerable.Empty<AlgoliaIndexContentType>())
				.Select(ct => ct.Alias)
				.Where(a => !string.IsNullOrWhiteSpace(a))
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			if (aliases.Count == 0) continue;

			var propsByAlias = (idx.ContentTypes ?? Enumerable.Empty<AlgoliaIndexContentType>())
				.GroupBy(ct => ct.Alias, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(
					g => g.Key,
					g => g.SelectMany(x => x.Properties ?? Enumerable.Empty<string>())
						  .ToHashSet(StringComparer.OrdinalIgnoreCase),
					StringComparer.OrdinalIgnoreCase);

			yield return new IndexSpec(idx.IndexName, aliases, propsByAlias);
		}
	}

	private sealed record IndexSpec(
		string IndexName,
		HashSet<string> Aliases,
		IDictionary<string, HashSet<string>> PropsByAlias);

	private AlgoliaDocument? MapForCulture(IPublishedContent c, string? culture, string baseIndexName, HashSet<string>? allowedProps = null)
	{
		if (!c.IsPublished(culture)) return null;

		var name = culture == null ? c.Name : c.Name(culture) ?? "";
		var url = culture == null ? c.Url() : c.Url(culture);

		var doc = new AlgoliaDocument
		{
			ObjectID = c.Key.ToString(),
			NodeId = c.Id,
			ContentTypeAlias = c.ContentType.Alias,
			Url = url,
			Name = name,
			UpdateDate = c.UpdateDate,
			CreateDate = c.CreateDate,
			Level = c.Level,
			Data = new Dictionary<string, object>()
		};

		foreach (var alias in allowedProps)
		{
			var prop = c.GetProperty(alias);
			if (prop is null) continue;

			var raw = ReadPropertyValue(c, prop, culture);
			if (raw is null) continue;

			var ctx = new AlgoliaPropertyContext(c, prop, culture, baseIndexName);
			var converted = ConvertProperty(ctx, raw);
			if (converted is not null) doc.Data[alias] = converted;
		}

		return doc;
	}

	private AlgoliaDocument? MapAndEnrichForCulture(
		IPublishedContent content,
		string? culture,
		string baseIndexName,
		HashSet<string>? allowedProps)
	{
		var doc = MapForCulture(content, culture, baseIndexName, allowedProps);
		if (doc is null || _enrichers.Count == 0) return doc;

		var ctx = new AlgoliaEnrichmentContext(
			Content: content,
			Culture: culture,
			BaseIndexName: baseIndexName,
			AllowedPropertyAliases: allowedProps
		);

		foreach (var enricher in _enrichers)
			enricher.Enrich(doc, ctx);

		return doc;
	}

	private object? ConvertProperty(AlgoliaPropertyContext ctx, object? raw)
	{
		var value = raw;
		foreach (var c in _propConverters)
		{
			if (c.CanHandle(ctx))
				value = c.Convert(ctx, value);
		}
		return value;
	}

	private object? ReadPropertyValue(IPublishedContent content, IPublishedProperty prop, string? culture)
	{
		var isVariant = prop.PropertyType.Variations.VariesByCulture();
		object? val;

		if (isVariant)
		{
			using (PushVariation(culture))
				val = content.Value(_publishedValueFallback, prop.Alias, culture: culture);

			if (val is null)
				val = prop.GetSourceValue(culture);
		}
		else
		{
			using (PushVariation(null))
				val = content.Value(_publishedValueFallback, prop.Alias) ?? prop.GetSourceValue();
		}

		return val;
	}

	private sealed class VariationScope : IDisposable
	{
		private readonly IVariationContextAccessor _accessor;
		private readonly VariationContext? _previous;
		public VariationScope(IVariationContextAccessor accessor, string? culture)
		{
			_accessor = accessor;
			_previous = accessor.VariationContext;
			accessor.VariationContext = new VariationContext(culture);
		}
		public void Dispose() => _accessor.VariationContext = _previous;
	}

	private IDisposable PushVariation(string? culture) => new VariationScope(_variation, culture);
}
