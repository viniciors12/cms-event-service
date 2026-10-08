using System.Text.Json;
using CmsEventService.Api.Domain;

namespace CmsEventService.Api.Contracts;

public sealed record EntityResponse(
    string Id,
    int Version,
    JsonElement Payload,
    bool IsPublished,
    bool IsDisabledByAdmin,
    DateTimeOffset UpdatedAt)
{
    public static EntityResponse From(CmsEntity entity)
    {
        // Payload is validated JSON, so it is returned as a nested object rather than an escaped string.
        using var payload = JsonDocument.Parse(entity.Payload);

        return new EntityResponse(
            entity.Id,
            entity.LatestVersion,
            payload.RootElement.Clone(),
            entity.IsPublished,
            entity.IsDisabledByAdmin,
            entity.UpdatedAt);
    }
}
