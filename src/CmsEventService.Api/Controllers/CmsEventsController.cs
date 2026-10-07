using System.Text.Json;
using CmsEventService.Api.Application;
using Microsoft.AspNetCore.Mvc;

namespace CmsEventService.Api.Controllers;

[ApiController]
[Route("cms/events")]
public class CmsEventsController(CmsEventProcessor processor) : ControllerBase
{
    public const int MaxBatchSize = 1000;

    // Well above a realistic batch, far below MaxBatchSize x MaxPayloadLength.
    private const long MaxRequestBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Ingests a batch of CMS events. Responds 200 with one result per event, since the batch
    /// can partially succeed; 400 (problem details) only when the request itself is unusable.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    [ProducesResponseType<BatchResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Post([FromBody] List<JsonElement> events, CancellationToken ct)
    {
        if (events.Count == 0)
            return Problem("The batch must contain at least one event.", statusCode: StatusCodes.Status400BadRequest);

        if (events.Count > MaxBatchSize)
            return Problem($"The batch exceeds {MaxBatchSize} events.", statusCode: StatusCodes.Status400BadRequest);

        return Ok(await processor.ProcessBatchAsync(events, ct));
    }
}
