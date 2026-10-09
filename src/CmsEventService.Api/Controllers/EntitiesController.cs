using System.ComponentModel.DataAnnotations;
using CmsEventService.Api.Application;
using CmsEventService.Api.Auth;
using CmsEventService.Api.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CmsEventService.Api.Controllers;

/// <summary>
/// Read access for API consumers. Regular users see published, enabled entities; admins also see
/// unpublished and disabled ones, from the same endpoints.
/// </summary>
[ApiController]
[Route("entities")]
[Authorize(Policy = Policies.Consumers)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class EntitiesController(IEntityReader reader, EntityAdminService admin) : ControllerBase
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private bool CanSeeHidden => User.IsInRole(Roles.Admin);

    [HttpGet]
    [EndpointSummary("List entities")]
    [EndpointDescription("Paged list ordered by id. Users see published, enabled entities; admins also see unpublished " +
        "and disabled ones. page starts at 1; pageSize is 1-100.")]
    [ProducesResponseType<PagedResult<EntityResponse>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<EntityResponse>> List(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, MaxPageSize)] int pageSize = DefaultPageSize,
        CancellationToken ct = default)
    {
        var result = await reader.ListAsync(CanSeeHidden, page, pageSize, ct);
        return result.Map(EntityResponse.From);
    }

    [HttpGet("{id}")]
    [EndpointSummary("Get one entity")]
    [EndpointDescription("Hidden entities are reported as 404 to regular users so their existence is not revealed.")]
    [ProducesResponseType<EntityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EntityResponse>> Get(string id, CancellationToken ct)
    {
        // Hidden entities are reported as missing to regular users, so their existence is not revealed.
        var entity = await reader.FindAsync(id, CanSeeHidden, ct);
        return entity is null ? NotFound() : EntityResponse.From(entity);
    }

    /// <summary>Hides the entity from regular users. Local override only: the CMS is not affected.</summary>
    [HttpPost("{id}/disable")]
    [EndpointSummary("Disable an entity (admin)")]
    [EndpointDescription("Hides the entity from regular users. A local override only: CMS data is not changed and later CMS events do not undo it.")]
    [Authorize(Policy = Policies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Disable(string id, CancellationToken ct) => SetDisabled(id, true, ct);

    /// <summary>Reverts <see cref="Disable"/>. Whether the entity is visible still depends on the CMS publication state.</summary>
    [HttpPost("{id}/enable")]
    [EndpointSummary("Enable an entity (admin)")]
    [EndpointDescription("Reverts a disable. Whether regular users see the entity still depends on whether the CMS has it published.")]
    [Authorize(Policy = Policies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Enable(string id, CancellationToken ct) => SetDisabled(id, false, ct);

    private async Task<IActionResult> SetDisabled(string id, bool disabled, CancellationToken ct)
    {
        var found = await admin.SetDisabledAsync(id, disabled, User.Identity!.Name!, ct);
        return found ? NoContent() : NotFound();
    }
}
