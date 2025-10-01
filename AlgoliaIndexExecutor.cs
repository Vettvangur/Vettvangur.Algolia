using Algolia.Search.Clients;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Persistence.Querying;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Extensions;
namespace Vettvangur.Algolia;

internal sealed class AlgoliaIndexExecutor
{
	private readonly ILogger<AlgoliaIndexExecutor> _logger;
	private readonly ISearchClient _client;
	private readonly AlgoliaConfig _config;
	private readonly IReadOnlyList<IAlgoliaDocumentEnricher> _enrichers;
	private readonly IReadOnlyList<IAlgoliaPropertyValueConverter> _propConverters;
	private readonly IContentService _contentService;
	private readonly ILocalizationService _languageService;
	private readonly PropertyEditorCollection _propertyEditorsCollection;
	private readonly IContentTypeService _contentTypeService;
	private readonly IPublishedUrlProvider _urlProvider;
	private readonly IUmbracoContextFactory _umbracoContextFactory;
	private readonly IScopeProvider _scopeProvider;
	private readonly IMemoryCache _cache;

	public AlgoliaIndexExecutor(
		ILogger<AlgoliaIndexExecutor> logger,
		ISearchClient client,
		IOptions<AlgoliaConfig> config,
		IContentService contentService,
		ILocalizationService languageService,
		PropertyEditorCollection propertyEditorsCollection,
		IContentTypeService contentTypeService,
		IPublishedUrlProvider urlProvider,
		IUmbracoContextFactory umbracoContextFactory,
		IScopeProvider scopeProvider,
		IMemoryCache cache,
		IEnumerable<IAlgoliaDocumentEnricher>? enrichers = null,
		IEnumerable<IAlgoliaPropertyValueConverter>? propConverters = null)
	{
		_logger = logger;
		_client = client;
		_config = config.Value;
		_enrichers = (enrichers ?? Array.Empty<IAlgoliaDocumentEnricher>())
			  .OrderBy(e => e.Order)
			  .ToList();
		_propConverters = (propConverters ?? Array.Empty<IAlgoliaPropertyValueConverter>())
						  .OrderBy(c => c.Order).ToList();
		_contentService = contentService;
		_languageService = languageService;
		_propertyEditorsCollection = propertyEditorsCollection;
		_contentTypeService = contentTypeService;
		_urlProvider = urlProvider;
		_umbracoContextFactory = umbracoContextFactory;
		_scopeProvider = scopeProvider;
		_cache = cache;
	}

	// ---------- Public API ----------

	public async Task RebuildAsync(string? indexName = null, CancellationToken ct = default)
	{
		var indexes = string.IsNullOrEmpty(indexName) ? _config.Indexes : _config.Indexes.Where(x => x.IndexName.InvariantEquals(indexName));

		var allCultures = _languageService
			.GetAllLanguages()
			.Select(l => l.IsoCode)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();

		var contentTypeDictionary = _contentTypeService.GetAll().ToDictionary(x => x.Key);

		foreach (var index in _config.Indexes)
		{
			if (string.IsNullOrWhiteSpace(index.IndexName)) continue;

			var typeAliases = (index.ContentTypes ?? Enumerable.Empty<AlgoliaIndexContentType>())
				.Select(ct => ct.Alias)
				.Where(a => !string.IsNullOrWhiteSpace(a))
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			foreach (var alias in typeAliases)
			{
				using var ctx = _umbracoContextFactory.EnsureUmbracoContext();
				var contentType = ctx.UmbracoContext.Content?.GetContentType(alias);

				if (contentType is null) continue;

				var entitiesForIndex = _contentService.GetPagedOfType(contentType.Id, 0, int.MaxValue, out _, new Query<IContent>(_scopeProvider.SqlContext)
						  .Where(x => !x.Trashed));

				_logger.LogInformation("Building index for {ContentType} with {Count} items", alias, entitiesForIndex.Count());

				var (entitiesToUpsertByCulture, entitiesToDeleteByCulture)
					= BuildPerCultureBuckets(entitiesForIndex, allCultures);

				foreach (var (culture, list) in entitiesToUpsertByCulture)
				{
					var distinct = list.DistinctBy(x => x.Id).ToList();
					if (distinct.Count == 0) continue;

					await UpsertByNodesByCultureAsync(distinct, culture, index, allCultures, contentTypeDictionary, ct);
				}

				foreach (var (culture, keySet) in entitiesToDeleteByCulture)
				{
					if (keySet.Count == 0) continue;

					await DeleteAsync(keySet, culture, index, ct);
				}

				_logger.LogInformation("Finished Building index for {ContentType}", alias);

			}

		}
	}

	public async Task UpdateByIdsAsync(int[] ids, CancellationToken ct = default)
	{
		if (ids == null || ids.Length == 0) return;

		var entities = _contentService.GetByIds(ids).Where(x => !x.Trashed).ToList();
		if (entities.Count == 0) return;

		var allCultures = await GetAllCulturesAsync();

		var contentTypeDictionary = await GetContentTypesAsync();

		foreach (var index in _config.Indexes)
		{
			if (string.IsNullOrWhiteSpace(index.IndexName)) continue;

			var typeAliases = (index.ContentTypes ?? Enumerable.Empty<AlgoliaIndexContentType>())
				.Select(ct => ct.Alias)
				.Where(a => !string.IsNullOrWhiteSpace(a))
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			if (typeAliases.Count == 0) continue;

			var entitiesForIndex = entities
				.Where(e => typeAliases.Contains(e.ContentType.Alias))
				.DistinctBy(e => e.Id)
				.ToList();

			if (entitiesForIndex.Count == 0) continue;

			var (entitiesToUpsertByCulture, entitiesToDeleteByCulture)
				= BuildPerCultureBuckets(entitiesForIndex, allCultures);

			foreach (var (culture, list) in entitiesToUpsertByCulture)
			{
				var distinct = list.DistinctBy(x => x.Id).ToList();
				if (distinct.Count == 0) continue;

				await UpsertByNodesByCultureAsync(distinct, culture, index, allCultures, contentTypeDictionary,  ct);
			}

			foreach (var (culture, keySet) in entitiesToDeleteByCulture)
			{
				if (keySet.Count == 0) continue;

				await DeleteAsync(keySet, culture, index, ct);
			}
		}
	}

	private static (
		Dictionary<string, List<IContent>> UpsertsByCulture,
		Dictionary<string, HashSet<string>> DeletesByCulture
	) BuildPerCultureBuckets(IEnumerable<IContent> entitiesForIndex, IEnumerable<string> allCultures)
	{
		var upserts = new Dictionary<string, List<IContent>>(StringComparer.OrdinalIgnoreCase);
		var deletes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

		foreach (var e in entitiesForIndex)
		{
			if (e.ContentType.Variations.VariesByCulture())
			{
				var cultures = (e.CultureInfos?.Values.Select(ci => ci.Culture) ?? Enumerable.Empty<string>())
							   .Where(c => !string.IsNullOrWhiteSpace(c));

				foreach (var cul in cultures)
				{
					if (e.IsCulturePublished(cul))
						AddList(upserts, cul, e);
					else
						AddSet(deletes, cul, e.Key.ToString());
				}
			}
			else
			{
				foreach (var cul in allCultures)
				{
					if (string.IsNullOrWhiteSpace(cul)) continue;

					if (e.Published)
						AddList(upserts, cul, e);
					else
						AddSet(deletes, cul, e.Key.ToString());
				}
			}
		}

		return (upserts, deletes);

		// --- local helpers ---
		static void AddList(Dictionary<string, List<IContent>> dict, string culture, IContent item)
		{
			if (!dict.TryGetValue(culture, out var list))
				dict[culture] = list = new List<IContent>();
			list.Add(item);
		}

		static void AddSet(Dictionary<string, HashSet<string>> dict, string culture, string key)
		{
			if (!dict.TryGetValue(culture, out var set))
				dict[culture] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			set.Add(key);
		}
	}

	private async Task UpsertByNodesByCultureAsync(IEnumerable<IContent> nodes, string culture, AlgoliaIndex index, IEnumerable<string> availableCultures, IDictionary<Guid, IContentType> contentTypeDictionary, CancellationToken ct)
	{
		var docs = new List<AlgoliaDocument>();

		foreach (var node in nodes)
		{
			var allowedProps = index.ContentTypes?
				.FirstOrDefault(ct => ct.Alias.Equals(node.ContentType.Alias, StringComparison.OrdinalIgnoreCase))
				?.Properties
				?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

			AlgoliaDocument? doc = null;
			try
			{
				doc = MapAndEnrichForCulture(node, culture, index.IndexName, allowedProps, availableCultures, contentTypeDictionary);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex,
					"Failed to map/enrich node {Id} for culture {Culture}", node.Id, culture);
			}
			if (doc == null) continue;
			docs.Add(doc);
		}

		if (docs.Count == 0) return;

		var indexName = $"{index.IndexName}_{culture.ToLowerInvariant()}";
		_logger.LogDebug("Save {Count} objects to {IndexName}", docs.Count, indexName);

		var resp = await _client.SaveObjectsAsync(
			indexName: indexName,
			objects: docs,
			batchSize: 1000,
			waitForTasks: true,
			options: null,
			cancellationToken: ct);
	}

	public async Task DeleteAsync(IEnumerable<string> nodeKeys, string culture, AlgoliaIndex index, CancellationToken ct = default)
	{
		if (string.IsNullOrWhiteSpace(culture)) return;

		var indexName = $"{index.IndexName}_{culture}";

		_logger.LogDebug("Delete {Count} objects from {IndexName}", nodeKeys.Count(), indexName);

		await _client.DeleteObjectsAsync(
			indexName: indexName,
			objectIDs: nodeKeys,
			batchSize: 1000,
			waitForTasks: false,
			options: null,
			cancellationToken: ct);
	}

	private AlgoliaDocument? MapDocument(IContent c, string? culture, string baseIndexName, HashSet<string> allowedProps, IEnumerable<string> availableCultures, IDictionary<Guid, IContentType> contentTypeDictionary)
	{
		using var contextReference = _umbracoContextFactory.EnsureUmbracoContext();

		var name = culture == null ? c.Name : c.GetCultureName(culture) ?? "";

		if (string.IsNullOrEmpty(name)) return null;

		var url = culture == null ? _urlProvider.GetUrl(c.Id) : _urlProvider.GetUrl(c.Id, culture: culture);

		var doc = new AlgoliaDocument
		{
			ObjectID = c.Key.ToString(),
			NodeId = c.Id,
			ContentTypeAlias = c.ContentType.Alias,
			Url = url,
			Name = name,
			UpdateDate = c.UpdateDate,
			CreateDate = c.CreateDate,
			Data = new Dictionary<string, object>()
		};

		foreach (var alias in allowedProps)
		{
			var prop = c.Properties.FirstOrDefault(x => x.Alias == alias);

			if (prop is null) continue;

			var propCulture = prop.PropertyType.Variations.VariesByCulture()
				? culture
				: null;

			var value = ReadPropertyValue(prop, propCulture, availableCultures, contentTypeDictionary);

			var ctx = new AlgoliaPropertyContext(c, prop, propCulture, culture, baseIndexName);
			var converted = ConvertProperty(ctx, value);

			if (converted is not null) doc.Data[alias] = converted;
		}

		return doc;
	}

	private AlgoliaDocument? MapAndEnrichForCulture(
		IContent content,
		string? culture,
		string baseIndexName,
		HashSet<string> allowedProps,
		IEnumerable<string> availableCultures, 
		IDictionary<Guid, IContentType> contentTypeDictionary)
	{
		var doc = MapDocument(content, culture, baseIndexName, allowedProps, availableCultures, contentTypeDictionary);
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

	private object? ConvertProperty(AlgoliaPropertyContext ctx, object? value)
	{
		foreach (var c in _propConverters)
		{
			if (c.CanHandle(ctx))
				value = c.Convert(ctx, value);
		}
		return value;
	}
	private object? ReadPropertyValue(IProperty property, string? culture, IEnumerable<string> availableCultures, IDictionary<Guid, IContentType> contentTypeDictionary)
	{
		var propertyEditor = _propertyEditorsCollection.FirstOrDefault(p => p.Alias == property.PropertyType.PropertyEditorAlias);
		if (propertyEditor == null)
		{
			return default;
		}

		var indexValues  = propertyEditor.PropertyIndexValueFactory.GetIndexValues(
			property,
			culture,
			null,
			true,
			availableCultures,
			contentTypeDictionary);

		if (indexValues == null || !indexValues.Any()) return new KeyValuePair<string, object>(property.Alias, string.Empty);

		var indexValue = indexValues.First();

		var returnValue = indexValue.Value.FirstOrDefault()?.ToString() ?? "";

		return returnValue;
	}
	private Task<string[]> GetAllCulturesAsync()
	{
		return _cache.GetOrCreateAsync("umbraco:languages:all", entry =>
		{
			entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);

			var cultures = _languageService
				.GetAllLanguages()
				.Select(l => l.IsoCode)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();

			return Task.FromResult(cultures);
		});
	}

	private Task<Dictionary<Guid, IContentType>> GetContentTypesAsync()
	{
		return _cache.GetOrCreateAsync("umbraco:contenttypes:byKey", entry =>
		{
			entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);

			var dict = _contentTypeService
				.GetAll()
				.ToDictionary(x => x.Key);

			return Task.FromResult(dict);
		});
	}
}
