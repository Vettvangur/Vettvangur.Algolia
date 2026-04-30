namespace Vettvangur.Algolia;

internal static class AlgoliaIndexNameResolver
{
	public static string Resolve(string indexName, string culture, string environment)
		=> $"{indexName.Trim().ToLowerInvariant()}.{environment.Trim().ToLowerInvariant()}.{culture.Trim().ToLowerInvariant()}";
}
