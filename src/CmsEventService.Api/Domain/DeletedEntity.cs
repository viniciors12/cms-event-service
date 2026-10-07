namespace CmsEventService.Api.Domain;

/// <summary>
/// Tombstone left by a hard-delete, so a late or duplicated publish/unPublish that predates
/// the delete does not resurrect the entity.
/// </summary>
public class DeletedEntity
{
    // Required by EF Core.
    private DeletedEntity() { }

    public DeletedEntity(string id, DateTimeOffset deletedAt)
    {
        Id = id;
        DeletedAt = deletedAt;
    }

    public string Id { get; private set; } = null!;

    /// <summary>CMS timestamp of the latest delete event.</summary>
    public DateTimeOffset DeletedAt { get; private set; }

    /// <summary>True when an event is not newer than the delete, so it must not bring the entity back.</summary>
    public bool Covers(DateTimeOffset eventTimestamp) => eventTimestamp <= DeletedAt;

    public void RecordDelete(DateTimeOffset eventTimestamp)
    {
        if (eventTimestamp > DeletedAt)
            DeletedAt = eventTimestamp;
    }
}
