namespace CmsEventService.Api.Application;

/// <summary>The kind of change an event describes.</summary>
public enum EventType
{
    /// <summary>The entity was published, or created or updated and published.</summary>
    Publish,

    /// <summary>The entity was disabled in the CMS; its data is kept.</summary>
    Unpublish,

    /// <summary>The entity was removed from the CMS.</summary>
    Delete
}

/// <summary>An incoming event that passed validation. Payload is the JSON text as received.</summary>
public sealed record ParsedEvent(
    EventType Type,
    string Id,
    int? Version,
    string? Payload,
    DateTimeOffset Timestamp);
