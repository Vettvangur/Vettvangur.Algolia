using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.BackOffice.Controllers;

namespace Vettvangur.Algolia;
public class AlgoliaController : UmbracoAuthorizedApiController
{
	private readonly IAlgoliaIndexService _algoliaIndexService;
	public AlgoliaController(IAlgoliaIndexService algoliaIndexService)
	{
		_algoliaIndexService = algoliaIndexService;
	}

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
