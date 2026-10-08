using System.Text.Json;
using CmsEventService.Api.Application;
using CmsEventService.Api.Domain;
using CmsEventService.Api.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CmsEventService.Tests.Integration;

/// <summary>End-to-end behavior of the processor against a real (in-memory SQLite) database.</summary>
public sealed class EventProcessingTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WriteDbContext _db;
    private readonly CmsEventProcessor _processor;

    public EventProcessingTests()
    {
        _connection.Open();
        _db = new WriteDbContext(new DbContextOptionsBuilder<WriteDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _processor = new CmsEventProcessor(new EfCmsEventStore(_db), TimeProvider.System, NullLogger<CmsEventProcessor>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static string Publish(string id, int version, string ts, string type = "publish") =>
        $$"""{ "type": "{{type}}", "id": "{{id}}", "version": {{version}}, "payload": { "v": {{version}} }, "timestamp": "{{ts}}" }""";

    private static string Delete(string id, string ts) =>
        $$"""{ "type": "delete", "id": "{{id}}", "timestamp": "{{ts}}" }""";

    private async Task<IReadOnlyList<EventResult>> Send(params string[] events)
    {
        var elements = events.Select(e => JsonDocument.Parse(e).RootElement.Clone()).ToList();
        var batch = await _processor.ProcessBatchAsync(elements);
        _db.ChangeTracker.Clear();
        return batch.Results;
    }

    private Task<CmsEntity?> Stored(string id) => _db.Entities.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);

    [Fact]
    public async Task Publish_creates_entity_with_version_and_payload()
    {
        var results = await Send(Publish("A", 1, "2024-01-01T00:00:00Z"));

        Assert.Equal(EventOutcome.Applied, results[0].Outcome);
        var e = await Stored("A");
        Assert.NotNull(e);
        Assert.Equal(1, e.LatestVersion);
        Assert.True(e.IsPublished);
        Assert.Contains("\"v\":1", e.Payload.Replace(" ", ""));
    }

    [Fact]
    public async Task Newer_version_replaces_the_stored_one()
    {
        await Send(Publish("A", 1, "2024-01-01T00:00:00Z"));
        await Send(Publish("A", 3, "2024-01-02T00:00:00Z"));

        Assert.Equal(3, (await Stored("A"))!.LatestVersion);
    }

    [Fact]
    public async Task Stale_version_is_ignored()
    {
        await Send(Publish("A", 3, "2024-01-02T00:00:00Z"));
        var results = await Send(Publish("A", 2, "2024-01-03T00:00:00Z"));

        Assert.Equal(EventOutcome.Ignored, results[0].Outcome);
        Assert.Equal(3, (await Stored("A"))!.LatestVersion);
    }

    [Fact]
    public async Task Repeated_event_is_idempotent()
    {
        var ev = Publish("A", 2, "2024-01-01T00:00:00Z");
        await Send(ev);
        var results = await Send(ev);

        Assert.NotEqual(EventOutcome.Rejected, results[0].Outcome);
        Assert.Single(_db.Entities);
        Assert.Equal(2, (await Stored("A"))!.LatestVersion);
    }

    [Fact]
    public async Task Unpublish_keeps_data_and_marks_entity_unpublished()
    {
        await Send(Publish("A", 1, "2024-01-01T00:00:00Z"));
        await Send(Publish("A", 1, "2024-01-02T00:00:00Z", "unPublish"));

        var e = await Stored("A");
        Assert.NotNull(e);
        Assert.False(e.IsPublished);
    }

    [Fact]
    public async Task Unpublish_of_a_version_never_published_stores_that_version()
    {
        // Stored v1; v2 was edited but never published, then unpublished.
        await Send(Publish("A", 1, "2024-01-01T00:00:00Z"));
        var results = await Send(Publish("A", 2, "2024-01-02T00:00:00Z", "unPublish"));

        Assert.Equal(EventOutcome.Applied, results[0].Outcome);
        var e = await Stored("A");
        Assert.Equal(2, e!.LatestVersion);
        Assert.False(e.IsPublished);
        Assert.Contains("\"v\":2", e.Payload.Replace(" ", ""));
    }

    [Fact]
    public async Task Unpublish_for_unknown_entity_creates_it_unpublished()
    {
        await Send(Publish("Z", 4, "2024-01-01T00:00:00Z", "unPublish"));

        var e = await Stored("Z");
        Assert.NotNull(e);
        Assert.Equal(4, e.LatestVersion);
        Assert.False(e.IsPublished);
    }

    [Fact]
    public async Task Unpublish_with_older_version_is_ignored()
    {
        await Send(Publish("A", 3, "2024-01-01T00:00:00Z"));
        var results = await Send(Publish("A", 2, "2024-01-02T00:00:00Z", "unPublish"));

        Assert.Equal(EventOutcome.Ignored, results[0].Outcome);
        Assert.True((await Stored("A"))!.IsPublished);
    }

    [Fact]
    public async Task Republish_of_same_version_after_unpublish_makes_it_visible_again()
    {
        await Send(Publish("A", 2, "2024-01-01T00:00:00Z", "unPublish"));
        await Send(Publish("A", 2, "2024-01-02T00:00:00Z"));

        Assert.True((await Stored("A"))!.IsPublished);
    }

    [Fact]
    public async Task Late_unpublish_does_not_override_a_newer_publish_of_same_version()
    {
        await Send(Publish("A", 2, "2024-01-02T00:00:00Z"));
        var results = await Send(Publish("A", 2, "2024-01-01T00:00:00Z", "unPublish"));

        Assert.Equal(EventOutcome.Ignored, results[0].Outcome);
        Assert.True((await Stored("A"))!.IsPublished);
    }

    [Fact]
    public async Task Delete_hard_deletes_the_entity()
    {
        await Send(Publish("A", 1, "2024-01-01T00:00:00Z"));
        var results = await Send(Delete("A", "2024-01-02T00:00:00Z"));

        Assert.Equal(EventOutcome.Applied, results[0].Outcome);
        Assert.Null(await Stored("A"));
    }

    [Fact]
    public async Task Publish_older_than_a_delete_does_not_resurrect_the_entity()
    {
        await Send(Delete("A", "2024-01-02T00:00:00Z"));
        var results = await Send(Publish("A", 1, "2024-01-01T00:00:00Z"));

        Assert.Equal(EventOutcome.Ignored, results[0].Outcome);
        Assert.Null(await Stored("A"));
        Assert.Single(_db.DeletedEntities);
    }

    [Fact]
    public async Task Publish_newer_than_a_delete_recreates_the_entity_and_clears_the_tombstone()
    {
        await Send(Delete("A", "2024-01-02T00:00:00Z"));
        await Send(Publish("A", 1, "2024-01-03T00:00:00Z"));

        Assert.NotNull(await Stored("A"));
        Assert.Empty(_db.DeletedEntities);
    }

    [Fact]
    public async Task Delete_older_than_the_stored_state_is_ignored()
    {
        await Send(Publish("A", 2, "2024-01-02T00:00:00Z"));
        var results = await Send(Delete("A", "2024-01-01T00:00:00Z"));

        Assert.Equal(EventOutcome.Ignored, results[0].Outcome);
        Assert.NotNull(await Stored("A"));
    }

    [Fact]
    public async Task Admin_disable_survives_later_cms_events()
    {
        await Send(Publish("A", 1, "2024-01-01T00:00:00Z"));
        var entity = await _db.Entities.FindAsync("A");
        entity!.SetDisabledByAdmin(true);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await Send(Publish("A", 2, "2024-01-02T00:00:00Z"));

        Assert.True((await Stored("A"))!.IsDisabledByAdmin);
    }

    [Fact]
    public async Task Batch_with_mixed_events_processes_each_independently()
    {
        var results = await Send(
            Publish("A", 1, "2024-01-01T00:00:00Z"),
            """{ "type": "bogus", "id": "B", "timestamp": "2024-01-01T00:00:00Z" }""",
            Publish("C", 1, "2024-01-01T00:00:00Z"));

        Assert.Equal(
            [EventOutcome.Applied, EventOutcome.Rejected, EventOutcome.Applied],
            results.Select(r => r.Outcome));
        Assert.NotNull(await Stored("A"));
        Assert.NotNull(await Stored("C"));
        Assert.Null(await Stored("B"));
    }

    [Fact]
    public async Task Rejected_event_stores_nothing()
    {
        await Send("""{ "type": "publish", "id": "A", "version": 0, "payload": {}, "timestamp": "2024-01-01T00:00:00Z" }""");

        Assert.Empty(_db.Entities);
        Assert.Empty(_db.DeletedEntities);
    }

    [Fact]
    public async Task Events_for_the_same_entity_in_one_batch_end_at_the_highest_version()
    {
        await Send(
            Publish("A", 1, "2024-01-01T00:00:00Z"),
            Publish("A", 3, "2024-01-03T00:00:00Z"),
            Publish("A", 2, "2024-01-02T00:00:00Z"));

        Assert.Equal(3, (await Stored("A"))!.LatestVersion);
    }

    [Fact]
    public async Task Batch_totals_match_the_individual_results()
    {
        var elements = new[]
        {
            Publish("A", 2, "2024-01-02T00:00:00Z"),
            Publish("A", 1, "2024-01-03T00:00:00Z"),
            """{ "type": "bogus", "id": "B", "timestamp": "2024-01-01T00:00:00Z" }"""
        }.Select(e => JsonDocument.Parse(e).RootElement.Clone()).ToList();

        var batch = await _processor.ProcessBatchAsync(elements);

        Assert.Equal((3, 1, 1, 1, 0), (batch.Total, batch.Applied, batch.Ignored, batch.Rejected, batch.Failed));
    }

    [Fact]
    public async Task Event_that_fails_in_the_database_is_reported_and_the_rest_of_the_batch_still_commits()
    {
        // A real database error for one specific entity, raised by the database itself.
        await _db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER reject_boom BEFORE INSERT ON Entities WHEN NEW.Id = 'boom' BEGIN SELECT RAISE(ABORT, 'rejected by trigger'); END;");

        var results = await Send(
            Publish("A", 1, "2024-01-01T00:00:00Z"),
            Publish("boom", 1, "2024-01-01T00:00:00Z"),
            Publish("C", 1, "2024-01-01T00:00:00Z"));

        Assert.Equal([EventOutcome.Applied, EventOutcome.Failed, EventOutcome.Applied], results.Select(r => r.Outcome));
        Assert.NotNull(await Stored("A"));
        Assert.Null(await Stored("boom"));
        Assert.NotNull(await Stored("C"));
    }
}
