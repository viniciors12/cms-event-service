namespace CmsEventService.Api.Application;

/// <summary>Admin overrides on top of the CMS data. They never change what the CMS sent.</summary>
public sealed class EntityAdminService(ICmsEventStore store, ILogger<EntityAdminService> logger)
{
    /// <returns>False when the entity does not exist.</returns>
    public async Task<bool> SetDisabledAsync(string id, bool disabled, string admin, CancellationToken ct)
    {
        if (!await store.SetDisabledByAdminAsync(id, disabled, ct))
            return false;

        // Logged only once the entity is found, so the id is one we validated on ingestion.
        logger.LogInformation("Entity {EntityId} {Action} by admin {Admin}",
            id, disabled ? "disabled" : "enabled", admin);

        return true;
    }
}
