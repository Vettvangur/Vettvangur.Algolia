using Algolia.Search.Clients;
using Algolia.Search.Models.Search;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Vettvangur.Algolia;

public sealed class AlgoliaContentSearchRequest
{
	public required string IndexName { get; set; }
	public required string Culture { get; set; }
	public required string Query { get; set; }
	public int Page { get; set; }
	public int HitsPerPage { get; set; } = 999;
	public string? UserToken { get; set; }
}

public sealed class AlgoliaContentSearchHit
{
	public string ObjectId { get; set; } = string.Empty;
	public int NodeId { get; set; }
	public string ContentTypeAlias { get; set; } = string.Empty;
	public string Url { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public DateTime? UpdateDate { get; set; }
	public long? UpdateDateUnixSecond { get; set; }
	public DateTime? CreateDate { get; set; }
	public long? CreateDateUnixSecond { get; set; }
	public Dictionary<string, object?> Data { get; set; } = [];
}

public sealed class AlgoliaContentSearchResult
{
	public string IndexName { get; set; } = string.Empty;
	public string Query { get; set; } = string.Empty;
	public int Page { get; set; }
	public int HitsPerPage { get; set; }
	public int TotalHits { get; set; }
	public int TotalPages { get; set; }
	public int ProcessingTimeMs { get; set; }
	public IReadOnlyList<AlgoliaContentSearchHit> Hits { get; set; } = [];
}

public interface IAlgoliaContentSearchService
{
	Task<AlgoliaContentSearchResult> SearchAsync(AlgoliaContentSearchRequest request, CancellationToken ct = default);
}

internal sealed class AlgoliaContentSearchService : IAlgoliaContentSearchService
{
	private static readonly HashSet<string> MetadataFieldNames = new(StringComparer.OrdinalIgnoreCase)
	{
		"objectID",
		"nodeId",
		"contentTypeAlias",
		"url",
		"name",
		"updateDate",
		"updateDateUnixSecond",
		"createDate",
		"createDateUnixSecond"
	};

	private readonly AlgoliaConfig _config;
	private readonly IMemoryCache _cache;
	private readonly SearchClient _client;
	private readonly string _environment;

	public AlgoliaContentSearchService(IOptions<AlgoliaConfig> config, IMemoryCache cache, IHostEnvironment hostEnvironment)
	{
		_config = config.Value;
		_cache = cache;
		_environment = string.IsNullOrWhiteSpace(_config.Environment) ? hostEnvironment.EnvironmentName : _config.Environment.Trim();

		var apiKey = string.IsNullOrWhiteSpace(_config.SearchApiKey)
			? _config.AdminApiKey
			: _config.SearchApiKey;

		_client = new SearchClient(_config.ApplicationId, apiKey);
	}

	public async Task<AlgoliaContentSearchResult> SearchAsync(AlgoliaContentSearchRequest request, CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (string.IsNullOrWhiteSpace(request.IndexName))
			throw new ArgumentException("IndexName is required.", nameof(request));

		if (string.IsNullOrWhiteSpace(request.Culture))
			throw new ArgumentException("Culture is required.", nameof(request));

		if (string.IsNullOrWhiteSpace(request.Query))
			throw new ArgumentException("Query is required.", nameof(request));

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
			}) ?? new AlgoliaContentSearchResult();
		}

		return await ExecuteSearchAsync(request, ct);
	}

	private async Task<AlgoliaContentSearchResult> ExecuteSearchAsync(AlgoliaContentSearchRequest request, CancellationToken ct)
	{
		var resolvedIndexName = ResolveIndexName(request.IndexName, request.Culture);
		var userToken = GetUserToken(request);
		var searchParams = new SearchParams(new SearchParamsObject
		{
			Query = request.Query?.Trim() ?? string.Empty,
			Page = request.Page,
			HitsPerPage = request.HitsPerPage,
			UserToken = userToken
		});

		var response = await _client.SearchSingleIndexAsync<Dictionary<string, object?>>(resolvedIndexName, searchParams, cancellationToken: ct);

		return new AlgoliaContentSearchResult
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

	private static AlgoliaContentSearchHit MapHit(Dictionary<string, object?> hit)
	{
		var objectId = GetValue(hit, "objectID");
		var data = hit
			.Where(kvp => !MetadataFieldNames.Contains(kvp.Key))
			.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

		return new AlgoliaContentSearchHit
		{
			ObjectId = objectId?.ToString() ?? string.Empty,
			NodeId = GetInt32(hit, "nodeId"),
			ContentTypeAlias = GetString(hit, "contentTypeAlias"),
			Url = GetString(hit, "url"),
			Name = GetString(hit, "name"),
			UpdateDate = GetDateTime(hit, "updateDate"),
			UpdateDateUnixSecond = GetInt64(hit, "updateDateUnixSecond"),
			CreateDate = GetDateTime(hit, "createDate"),
			CreateDateUnixSecond = GetInt64(hit, "createDateUnixSecond"),
			Data = data
		};
	}

	private static object? GetValue(IReadOnlyDictionary<string, object?> hit, string key)
	{
		if (hit.TryGetValue(key, out var value))
			return value;

		var alternateKey = char.ToUpperInvariant(key[0]) + key[1..];
		return hit.TryGetValue(alternateKey, out value) ? value : null;
	}

	private static string GetString(IReadOnlyDictionary<string, object?> hit, string key)
		=> GetValue(hit, key)?.ToString() ?? string.Empty;

	private static int GetInt32(IReadOnlyDictionary<string, object?> hit, string key)
	{
		var value = GetValue(hit, key);

		if (value is null)
			return 0;

		return value switch
		{
			int intValue => intValue,
			long longValue => (int)longValue,
			_ when int.TryParse(value.ToString(), out var parsed) => parsed,
			_ => 0
		};
	}

	private static long? GetInt64(IReadOnlyDictionary<string, object?> hit, string key)
	{
		var value = GetValue(hit, key);

		if (value is null)
			return null;

		return value switch
		{
			long longValue => longValue,
			int intValue => intValue,
			_ when long.TryParse(value.ToString(), out var parsed) => parsed,
			_ => null
		};
	}

	private static DateTime? GetDateTime(IReadOnlyDictionary<string, object?> hit, string key)
	{
		var value = GetValue(hit, key);

		if (value is null)
			return null;

		return value switch
		{
			DateTime dateTime => dateTime,
			DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime,
			_ when DateTime.TryParse(value.ToString(), out var parsed) => parsed,
			_ => null
		};
	}

	private string ResolveIndexName(string indexName, string culture)
		=> AlgoliaIndexNameResolver.Resolve(indexName, culture, _environment);

	private string? GetUserToken(AlgoliaContentSearchRequest request)
	{
		if (!_config.IncludeUserToken || string.IsNullOrWhiteSpace(request.UserToken))
			return null;

		return request.UserToken.Trim();
	}

	private string BuildCacheKey(AlgoliaContentSearchRequest request)
		=> string.Join("|",
			request.IndexName.Trim(),
			request.Culture.Trim().ToLowerInvariant(),
			request.Query?.Trim() ?? string.Empty,
			request.Page,
			request.HitsPerPage,
			_config.VaryCacheByUserToken ? GetUserToken(request) ?? string.Empty : string.Empty);
}
