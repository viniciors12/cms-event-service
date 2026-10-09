using System.Text.Json;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using CmsEventService.Api.Application;
using CmsEventService.Api.Auth;

namespace CmsEventService.Api.Controllers;

/// <summary>Receives webhook events from the CMS.</summary>
[ApiController]
[Route("cms/events")]
[Authorize(Policy = Policies.CmsIngestion)]
public class CmsEventsController(CmsEventProcessor processor) : ControllerBase
{
    /// <summary>Most events accepted in one request.</summary>
    public const int MaxBatchSize = 1000;

    // Well above a realistic batch, far below MaxBatchSize x MaxPayloadLength.
    private const long MaxRequestBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Ingests a batch of CMS events. Responds 200 with one result per event, since the batch
    /// can partially succeed; 400 (problem details) only when the request itself is unusable.
    /// </summary>
    [HttpPost]
    [EndpointSummary("Ingest a batch of CMS events")]
    [EndpointDescription("Accepts up to 1000 events (publish, add, update, unPublish, delete). Each event is validated and " +
        "applied on its own: the response lists the outcome of every event (Applied, Ignored as obsolete, Rejected as invalid, " +
        "or Failed). Reserved for the CMS account.")]
    [RequestSizeLimit(MaxRequestBytes)]
    [ProducesResponseType<BatchResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Post([FromBody] List<JsonElement> events, CancellationToken ct)
    {
        if (events.Count == 0)
            return Problem("The batch must contain at least one event.", statusCode: StatusCodes.Status400BadRequest);

        if (events.Count > MaxBatchSize)
            return Problem($"The batch exceeds {MaxBatchSize} events.", statusCode: StatusCodes.Status400BadRequest);

        return Ok(await processor.ProcessBatchAsync(events, ct));
    }
}
