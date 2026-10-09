using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using CmsEventService.Api.Domain;
using CmsEventService.Api.Infrastructure;

namespace CmsEventService.Tests.Integration;

/// <summary>
/// Two writers interleaving on the same entity, as concurrent batches could on a server database.
/// SQLite serializes whole batches, so the interleaving is reproduced by hand with two contexts.
/// </summary>
public sealed class ConcurrencyTests : IDisposable
{
    private static readonly DateTimeOffset T1 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = T1.AddDays(1);
    private static readonly DateTimeOffset T3 = T1.AddDays(2);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DbContextOptions<WriteDbContext> _options;

    public ConcurrencyTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<WriteDbContext>().UseSqlite(_connection).Options;

        using var db = new WriteDbContext(_options);
        db.Database.EnsureCreated();
        db.Entities.Add(new CmsEntity("A", 1, "{}", true, T1, T1));
        db.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Writer_that_loaded_an_older_version_fails_instead_of_overwriting_a_newer_event()
    {
        await using var slow = new WriteDbContext(_options);
        var stale = await slow.Entities.SingleAsync(e => e.Id == "A");

        await using (var fast = new WriteDbContext(_options))
        {
            var current = await fast.Entities.SingleAsync(e => e.Id == "A");
            current.ApplyVersion(3, """{"v":3}""", true, T3, T3);
            await fast.SaveChangesAsync();
        }

        stale.ApplyVersion(2, """{"v":2}""", true, T2, T2);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => slow.SaveChangesAsync());
        await using var check = new WriteDbContext(_options);
        Assert.Equal(3, (await check.Entities.SingleAsync(e => e.Id == "A")).LatestVersion);
    }

    [Fact]
    public async Task Writer_that_loaded_an_older_timestamp_of_the_same_version_fails()
    {
        await using var slow = new WriteDbContext(_options);
        var stale = await slow.Entities.SingleAsync(e => e.Id == "A");

        await using (var fast = new WriteDbContext(_options))
        {
            var current = await fast.Entities.SingleAsync(e => e.Id == "A");
            current.ApplyVersion(1, "{}", false, T3, T3);
            await fast.SaveChangesAsync();
        }

        stale.ApplyVersion(1, "{}", true, T2, T2);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => slow.SaveChangesAsync());
    }

    [Fact]
    public async Task Admin_override_in_between_does_not_conflict_with_a_cms_event_and_both_are_kept()
    {
        await using var ingestion = new WriteDbContext(_options);
        var entity = await ingestion.Entities.SingleAsync(e => e.Id == "A");

        await using (var admin = new WriteDbContext(_options))
            Assert.True(await new EfCmsEventStore(admin).SetDisabledByAdminAsync("A", true, CancellationToken.None));

        entity.ApplyVersion(2, """{"v":2}""", true, T2, T2);
        await ingestion.SaveChangesAsync();

        await using var check = new WriteDbContext(_options);
        var stored = await check.Entities.SingleAsync(e => e.Id == "A");
        Assert.Equal(2, stored.LatestVersion);
        Assert.True(stored.IsDisabledByAdmin);
    }

    [Fact]
    public async Task Admin_override_of_a_missing_entity_reports_not_found()
    {
        await using var db = new WriteDbContext(_options);

        Assert.False(await new EfCmsEventStore(db).SetDisabledByAdminAsync("missing", true, CancellationToken.None));
    }
}
