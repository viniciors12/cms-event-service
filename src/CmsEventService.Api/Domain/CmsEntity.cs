using System.Linq.Expressions;

namespace CmsEventService.Api.Domain;

/// <summary>
/// Local copy of a CMS entity. CMS-driven state (<see cref="IsPublished"/>) and the local
/// admin override (<see cref="IsDisabledByAdmin"/>) are kept apart so CMS events never undo an admin decision.
/// </summary>
public class CmsEntity
{
    // Required by EF Core.
    private CmsEntity() { }

    public CmsEntity(string id, int version, string payload, bool isPublished, DateTimeOffset eventTimestamp, DateTimeOffset now)
    {
        Id = id;
        LatestVersion = version;
        Payload = payload;
        IsPublished = isPublished;
        LastEventTimestamp = eventTimestamp;
        UpdatedAt = now;
    }

    /// <summary>
    /// What regular users may see: published by the CMS and not disabled by an admin.
    /// An expression so database queries can filter with it directly.
    /// </summary>
    public static readonly Expression<Func<CmsEntity, bool>> IsVisibleToUsers =
        e => e.IsPublished && !e.IsDisabledByAdmin;

    public string Id { get; private set; } = null!;

    public int LatestVersion { get; private set; }

    /// <summary>Validated JSON object, stored as text.</summary>
    public string Payload { get; private set; } = null!;

    /// <summary>False after an unPublish event: data is kept but hidden from regular users.</summary>
    public bool IsPublished { get; private set; }

    public bool IsDisabledByAdmin { get; private set; }

    /// <summary>CMS timestamp of the last event applied; used to discard out-of-order events.</summary>
    public DateTimeOffset LastEventTimestamp { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// True when a publish/unPublish carries an older version, or the same version but an older
    /// timestamp (e.g. a late unPublish of a version that has since been re-published).
    /// </summary>
    public bool IsStale(int version, DateTimeOffset eventTimestamp) =>
        version < LatestVersion || (version == LatestVersion && eventTimestamp < LastEventTimestamp);

    public bool IsDeleteStale(DateTimeOffset eventTimestamp) => eventTimestamp < LastEventTimestamp;

    /// <summary>
    /// Takes the version carried by the event. This also covers the unPublish corner case: a version
    /// that was never published (so never stored) arrives with its payload and replaces what we had.
    /// </summary>
    public void ApplyVersion(int version, string payload, bool isPublished, DateTimeOffset eventTimestamp, DateTimeOffset now)
    {
        LatestVersion = version;
        Payload = payload;
        IsPublished = isPublished;
        LastEventTimestamp = eventTimestamp;
        UpdatedAt = now;
    }

    public void SetDisabledByAdmin(bool disabled) => IsDisabledByAdmin = disabled;
}
