using Algolia.Search.Clients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Vettvangur.Algolia.Converters;

namespace Vettvangur.Algolia;
public static class AlgoliaServiceCollectionExtensions
{
	public static IServiceCollection AddVettvangurAlgolia(
		this IServiceCollection services,
		Action<AlgoliaConfig>? configure = null)
	{
		var ob = services.AddOptions<AlgoliaConfig>()
			.BindConfiguration("Algolia");

		if (configure is not null) ob.Configure(configure);

		services.TryAddSingleton<ISearchClient>(sp =>
		{
			var cfg = sp.GetRequiredService<IOptions<AlgoliaConfig>>().Value;
			return new SearchClient(cfg.ApplicationId, cfg.AdminApiKey);
		});

		// Queue + worker + executor + dispatcher
		services.TryAddSingleton<AlgoliaIndexQueue>();
		services.TryAddSingleton<IAlgoliaIndexQueue>(sp => sp.GetRequiredService<AlgoliaIndexQueue>());

		services.TryAddSingleton<AlgoliaIndexExecutor>();
		services.AddHostedService<AlgoliaIndexWorker>();

		services.TryAddSingleton<IAlgoliaIndexService, AlgoliaIndexService>();

		services.TryAddEnumerable(ServiceDescriptor.Singleton<IAlgoliaPropertyValueConverter, MediaPickerConverter>());
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IAlgoliaPropertyValueConverter, ContentPickerConverter>());
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IAlgoliaPropertyValueConverter, RemoveUdiStringsConverter>());
		return services;
	}
}
