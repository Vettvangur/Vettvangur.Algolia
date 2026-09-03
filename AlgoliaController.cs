using Microsoft.AspNetCore.Mvc;
#if NET10_0_OR_GREATER
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
#else
using Umbraco.Cms.Web.BackOffice.Controllers;
#endif

namespace Vettvangur.Algolia;
#if NET10_0_OR_GREATER
[VersionedApiBackOfficeRoute("algolia")]
[ApiExplorerSettings(GroupName = "Algolia")]
public class AlgoliaController : ManagementApiControllerBase
#else
public class AlgoliaController : UmbracoAuthorizedApiController
#endif
{
	private readonly IAlgoliaIndexService _algoliaIndexService;
	public AlgoliaController(IAlgoliaIndexService algoliaIndexService)
	{
		_algoliaIndexService = algoliaIndexService;
	}

	[HttpPost("rebuild-indexes")]
	public async Task<IActionResult> RebuildIndexesAsync([FromQuery] string? indexName = null, CancellationToken ct = default)
	{
		try
		{
			await _algoliaIndexService.RebuildAsync(indexName, ct);
			return Ok(new { message = "Algolia indexes rebuild initiated." });
		}
		catch (Exception ex)
		{
			return StatusCode(500, new { error = ex.Message });
		}
	}
}
