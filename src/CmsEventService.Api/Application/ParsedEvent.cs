namespace CmsEventService.Api.Application;

public enum EventType
{
    Publish,
    Unpublish,
    Delete
}

/// <summary>An incoming event that passed validation. Payload is canonical JSON text.</summary>
public sealed record ParsedEvent(
    EventType Type,
    string Id,
    int? Version,
    string? Payload,
    DateTimeOffset Timestamp);
