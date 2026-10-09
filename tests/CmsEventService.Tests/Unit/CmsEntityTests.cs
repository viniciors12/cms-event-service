using CmsEventService.Api.Domain;

namespace CmsEventService.Tests.Unit;

public class CmsEntityTests
{
    private static readonly DateTimeOffset T1 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = T1.AddDays(1);
    private static readonly DateTimeOffset T3 = T1.AddDays(2);

    private static CmsEntity Stored(int version, DateTimeOffset at, bool published = true) =>
        new("A", version, "{}", published, at, at);

    [Fact]
    public void Older_version_is_stale_even_with_a_newer_timestamp() =>
        Assert.True(Stored(3, T1).IsStale(2, T3));

    [Fact]
    public void Newer_version_is_not_stale_even_with_an_older_timestamp() =>
        Assert.False(Stored(2, T2).IsStale(3, T1));

    [Fact]
    public void Same_version_and_timestamp_is_not_stale_so_retries_are_idempotent() =>
        Assert.False(Stored(2, T2).IsStale(2, T2));

    [Fact]
    public void Same_version_with_older_timestamp_is_stale() =>
        Assert.True(Stored(2, T2).IsStale(2, T1));

    [Fact]
    public void Same_version_with_newer_timestamp_is_not_stale() =>
        Assert.False(Stored(2, T1).IsStale(2, T2));

    [Fact]
    public void Delete_is_stale_only_when_older_than_the_stored_state()
    {
        var entity = Stored(2, T2);

        Assert.True(entity.IsDeleteStale(T1));
        Assert.False(entity.IsDeleteStale(T2));
        Assert.False(entity.IsDeleteStale(T3));
    }

    [Fact]
    public void ApplyVersion_replaces_cms_state()
    {
        var entity = Stored(1, T1);

        entity.ApplyVersion(2, """{"v":2}""", isPublished: false, T2, T2);

        Assert.Equal(2, entity.LatestVersion);
        Assert.Equal("""{"v":2}""", entity.Payload);
        Assert.False(entity.IsPublished);
        Assert.Equal(T2, entity.LastEventTimestamp);
        Assert.False(entity.IsDisabledByAdmin);
    }

    // The admin-disabled cases need the override, which only the store writes; see ConcurrencyTests
    // and EntitiesEndpointTests.
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Users_only_see_published_entities(bool published, bool visible)
    {
        var entity = Stored(1, T1, published);

        Assert.Equal(visible, CmsEntity.IsVisibleToUsers.Compile()(entity));
    }

    [Fact]
    public void Tombstone_covers_events_that_are_not_newer_than_the_delete()
    {
        var tombstone = new DeletedEntity("A", T2);

        Assert.True(tombstone.Covers(T1));
        Assert.True(tombstone.Covers(T2));
        Assert.False(tombstone.Covers(T3));
    }

    [Fact]
    public void Tombstone_only_moves_forward_in_time()
    {
        var tombstone = new DeletedEntity("A", T2);

        tombstone.RecordDelete(T1);
        Assert.Equal(T2, tombstone.DeletedAt);

        tombstone.RecordDelete(T3);
        Assert.Equal(T3, tombstone.DeletedAt);
    }
}
