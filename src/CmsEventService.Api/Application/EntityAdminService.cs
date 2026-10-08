namespace CmsEventService.Api.Application;

/// <summary>Admin overrides on top of the CMS data. They never change what the CMS sent.</summary>
public sealed class EntityAdminService(ICmsEventStore store, ILogger<EntityAdminService> logger)
{
    /// <returns>False when the entity does not exist.</returns>
    public async Task<bool> SetDisabledAsync(string id, bool disabled, string admin, CancellationToken ct)
    {
        var entity = await store.FindEntityAsync(id, ct);
        if (entity is null)
            return false;

        entity.SetDisabledByAdmin(disabled);
        await store.SaveChangesAsync(ct);

        // Logged only once the entity is found, so the id is one we validated on ingestion.
        logger.LogInformation("Entity {EntityId} {Action} by admin {Admin}",
            entity.Id, disabled ? "disabled" : "enabled", admin);

        return true;
    }
}
