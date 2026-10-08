using System.Diagnostics;
using System.Text.Json;
using CmsEventService.Api.Domain;

namespace CmsEventService.Api.Application;

/// <summary>
/// Applies CMS events to the local copy. Each event is isolated: an invalid or failing event
/// never prevents the rest of the batch from being processed. The ordering rules themselves
/// live in the domain; this class only loads state, asks the domain, and persists.
/// </summary>
public sealed class CmsEventProcessor(ICmsEventStore store, TimeProvider clock, ILogger<CmsEventProcessor> logger)
{
    public async Task<BatchResult> ProcessBatchAsync(IReadOnlyList<JsonElement> events, CancellationToken ct = default)
    {
        var started = Stopwatch.GetTimestamp();
        var results = new List<EventResult>(events.Count);

        // One commit for the whole batch: committing per event made a 1000-event batch take seconds.
        // Each event is still isolated, because a failing save is undone on its own (see the store).
        await using var scope = await store.BeginBatchAsync(ct);

        for (var i = 0; i < events.Count; i++)
        {
            results.Add(await ProcessOneAsync(i, events[i], ct));
        }

        await scope.CommitAsync(ct);

        var batch = new BatchResult(results);
        logger.LogInformation(
            "Batch processed in {ElapsedMs:F0} ms: {Total} events, {Applied} applied, {Ignored} ignored, {Rejected} rejected, {Failed} failed",
            Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            batch.Total, batch.Applied, batch.Ignored, batch.Rejected, batch.Failed);

        return batch;
    }

    private async Task<EventResult> ProcessOneAsync(int index, JsonElement raw, CancellationToken ct)
    {
        if (!EventValidator.TryParse(raw, out var ev, out var error))
        {
            logger.LogWarning("Event {Index} rejected: {Reason}", index, error);
            return new EventResult(index, ReadString(raw, "id"), ReadString(raw, "type"), EventOutcome.Rejected, error);
        }

        try
        {
            var (outcome, reason) = await ApplyAsync(ev!, ct);

            logger.LogInformation(
                "Event {Index} {Type} for entity {EntityId} v{Version} {Outcome}: {Reason}",
                index, ev!.Type, ev.Id, ev.Version, outcome, reason);

            return new EventResult(index, ev.Id, ev.Type.ToString(), outcome, reason);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Isolating the failure is the meaningful action here: drop whatever this event
            // left half-tracked so it cannot leak into the next one, and report it.
            store.DiscardChanges();
            logger.LogError(ex, "Event {Index} {Type} for entity {EntityId} failed", index, ev!.Type, ev.Id);
            return new EventResult(index, ev.Id, ev.Type.ToString(), EventOutcome.Failed, "internal error");
        }
    }

    private async Task<(EventOutcome, string)> ApplyAsync(ParsedEvent ev, CancellationToken ct)
    {
        var entity = await store.FindEntityAsync(ev.Id, ct);
        var tombstone = await store.FindTombstoneAsync(ev.Id, ct);

        return ev.Type == EventType.Delete
            ? await ApplyDeleteAsync(ev, entity, tombstone, ct)
            : await ApplyPublishOrUnpublishAsync(ev, entity, tombstone, ct);
    }

    private async Task<(EventOutcome, string)> ApplyDeleteAsync(
        ParsedEvent ev, CmsEntity? entity, DeletedEntity? tombstone, CancellationToken ct)
    {
        if (entity?.IsDeleteStale(ev.Timestamp) == true)
            return (EventOutcome.Ignored, "delete is older than the stored state");

        if (entity is not null)
            store.RemoveEntity(entity);

        if (tombstone is null)
            store.AddTombstone(new DeletedEntity(ev.Id, ev.Timestamp));
        else
            tombstone.RecordDelete(ev.Timestamp);

        await store.SaveChangesAsync(ct);

        return (EventOutcome.Applied, entity is null ? "entity not stored; tombstone recorded" : "entity deleted");
    }

    private async Task<(EventOutcome, string)> ApplyPublishOrUnpublishAsync(
        ParsedEvent ev, CmsEntity? entity, DeletedEntity? tombstone, CancellationToken ct)
    {
        var version = ev.Version!.Value;

        if (tombstone?.Covers(ev.Timestamp) == true)
            return (EventOutcome.Ignored, "event predates the entity's deletion");

        if (entity?.IsStale(version, ev.Timestamp) == true)
            return (EventOutcome.Ignored, $"stale event (stored v{entity.LatestVersion})");

        // Only reached for events newer than the delete: the CMS re-created the entity.
        if (tombstone is not null)
            store.RemoveTombstone(tombstone);

        var isPublished = ev.Type == EventType.Publish;
        var now = clock.GetUtcNow();

        if (entity is null)
            store.AddEntity(new CmsEntity(ev.Id, version, ev.Payload!, isPublished, ev.Timestamp, now));
        else
            entity.ApplyVersion(version, ev.Payload!, isPublished, ev.Timestamp, now);

        await store.SaveChangesAsync(ct);

        return (EventOutcome.Applied, entity is null ? "entity created" : "entity updated");
    }

    /// <summary>Best-effort read of a field from an event that failed validation, truncated for logs and responses.</summary>
    private static string? ReadString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.String)
            return null;

        var value = property.GetString();
        return value is { Length: > 100 } ? value[..100] : value;
    }
}
