using Algolia.Search.Clients;
using Algolia.Search.Models.Search;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Vettvangur.Algolia;

public sealed class AlgoliaSearchRequest
{
	public string IndexName { get; set; } = string.Empty;
	public string Culture { get; set; } = string.Empty;
	public string Query { get; set; } = string.Empty;
	public int Page { get; set; }
	public int HitsPerPage { get; set; } = 20;
}

public sealed class AlgoliaSearchHit
{
	public string ObjectId { get; set; } = string.Empty;
	public Dictionary<string, object?> Data { get; set; } = [];
}

public sealed class AlgoliaSearchResult
{
	public string IndexName { get; set; } = string.Empty;
	public string Query { get; set; } = string.Empty;
	public int Page { get; set; }
	public int HitsPerPage { get; set; }
	public int TotalHits { get; set; }
	public int TotalPages { get; set; }
	public int ProcessingTimeMs { get; set; }
	public IReadOnlyList<AlgoliaSearchHit> Hits { get; set; } = [];
}

public interface IAlgoliaSearchService
{
	Task<AlgoliaSearchResult> SearchAsync(AlgoliaSearchRequest request, CancellationToken ct = default);
}

internal sealed class AlgoliaSearchService : IAlgoliaSearchService
{
	private readonly AlgoliaConfig _config;
	private readonly IMemoryCache _cache;
	private readonly SearchClient _client;

	public AlgoliaSearchService(IOptions<AlgoliaConfig> config, IMemoryCache cache)
	{
		_config = config.Value;
		_cache = cache;

		var apiKey = string.IsNullOrWhiteSpace(_config.SearchApiKey)
			? _config.AdminApiKey
			: _config.SearchApiKey;

		_client = new SearchClient(_config.ApplicationId, apiKey);
	}

	public async Task<AlgoliaSearchResult> SearchAsync(AlgoliaSearchRequest request, CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (string.IsNullOrWhiteSpace(request.IndexName))
			throw new ArgumentException("IndexName is required.", nameof(request));

		if (string.IsNullOrWhiteSpace(request.Culture))
			throw new ArgumentException("Culture is required.", nameof(request));

		if (request.Page < 0)
			throw new ArgumentOutOfRangeException(nameof(request.Page), "Page must be zero or greater.");

		if (request.HitsPerPage <= 0)
			throw new ArgumentOutOfRangeException(nameof(request.HitsPerPage), "HitsPerPage must be greater than zero.");

		var cacheKey = BuildCacheKey(request);

		if (_config.SearchCacheEnabled)
		{
			return await _cache.GetOrCreateAsync(cacheKey, async entry =>
			{
				entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(Math.Max(1, _config.SearchCacheDurationMinutes));
				return await ExecuteSearchAsync(request, ct);
			}) ?? new AlgoliaSearchResult();
		}

		return await ExecuteSearchAsync(request, ct);
	}

	private async Task<AlgoliaSearchResult> ExecuteSearchAsync(AlgoliaSearchRequest request, CancellationToken ct)
	{
		var resolvedIndexName = ResolveIndexName(request.IndexName, request.Culture);
		var searchParams = new SearchParams(new SearchParamsObject
		{
			Query = request.Query?.Trim() ?? string.Empty,
			Page = request.Page,
			HitsPerPage = request.HitsPerPage
		});

		var response = await _client.SearchSingleIndexAsync<Dictionary<string, object?>>(resolvedIndexName, searchParams, cancellationToken: ct);

		return new AlgoliaSearchResult
		{
			IndexName = resolvedIndexName,
			Query = request.Query ?? string.Empty,
			Page = response.Page ?? request.Page,
			HitsPerPage = response.HitsPerPage ?? request.HitsPerPage,
			TotalHits = response.NbHits ?? 0,
			TotalPages = response.NbPages ?? 0,
			ProcessingTimeMs = response.ProcessingTimeMS ?? 0,
			Hits = response.Hits?
				.Select(MapHit)
				.ToList() ?? []
		};
	}

	private static AlgoliaSearchHit MapHit(Dictionary<string, object?> hit)
	{
		hit.TryGetValue("objectID", out var objectId);

		return new AlgoliaSearchHit
		{
			ObjectId = objectId?.ToString() ?? string.Empty,
			Data = hit
		};
	}

	private static string ResolveIndexName(string indexName, string culture)
		=> $"{indexName}_{culture.ToLowerInvariant()}";

	private static string BuildCacheKey(AlgoliaSearchRequest request)
		=> string.Join("|",
			request.IndexName.Trim(),
			request.Culture.Trim().ToLowerInvariant(),
			request.Query.Trim(),
			request.Page,
			request.HitsPerPage);
}
