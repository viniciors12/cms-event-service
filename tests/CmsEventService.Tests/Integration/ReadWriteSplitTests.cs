using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using CmsEventService.Api.Application;
using CmsEventService.Api.Domain;
using CmsEventService.Api.Infrastructure;

namespace CmsEventService.Tests.Integration;

public sealed class ReadWriteSplitTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"cms-test-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_file) + "*"))
            File.Delete(path);
    }

    [Fact]
    public async Task Read_context_refuses_to_save()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ReadDbContext>().UseSqlite(connection).Options;
        await using var db = new ReadDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Entities.Add(new CmsEntity("A", 1, "{}", true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task Read_context_does_not_track_loaded_entities()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var writeOptions = new DbContextOptionsBuilder<WriteDbContext>().UseSqlite(connection).Options;
        await using (var write = new WriteDbContext(writeOptions))
        {
            await write.Database.EnsureCreatedAsync();
            write.Entities.Add(new CmsEntity("A", 1, "{}", true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            await write.SaveChangesAsync();
        }

        var readOptions = new DbContextOptionsBuilder<ReadDbContext>()
            .UseSqlite(connection)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;
        await using var read = new ReadDbContext(readOptions);

        Assert.NotNull(await read.Entities.FirstOrDefaultAsync());
        Assert.Empty(read.ChangeTracker.Entries());
    }

    [Fact]
    public async Task App_works_end_to_end_with_reads_on_a_real_read_only_sqlite_connection()
    {
        await using var factory = new FileDatabaseFactory(_file);
        using var client = factory.CreateClient();
        var cms = Basic(ApiFactory.CmsUser, ApiFactory.CmsPassword);
        var reader = Basic(ApiFactory.ReaderUser, ApiFactory.ReaderPassword);

        using var post = new HttpRequestMessage(HttpMethod.Post, "/cms/events")
        {
            Content = new StringContent(
                """[{ "type": "publish", "id": "A", "version": 1, "payload": { "x": 1 }, "timestamp": "2024-01-01T00:00:00Z" }]""",
                Encoding.UTF8, "application/json")
        };
        post.Headers.Authorization = cms;
        using var posted = await client.SendAsync(post);

        using var get = new HttpRequestMessage(HttpMethod.Get, "/entities/A");
        get.Headers.Authorization = reader;
        using var response = await client.SendAsync(get);

        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Concurrent_batches_on_a_real_database_all_apply_without_failures()
    {
        var options = new DbContextOptionsBuilder<WriteDbContext>().UseSqlite($"Data Source={_file}").Options;
        await using (var setup = new WriteDbContext(options))
            await setup.Database.EnsureCreatedAsync();

        async Task<BatchResult> RunBatch(string prefix)
        {
            await using var db = new WriteDbContext(options);
            var processor = new CmsEventProcessor(new EfCmsEventStore(db), TimeProvider.System, NullLogger<CmsEventProcessor>.Instance);
            var events = Enumerable
                .Range(0, 100)
                .Select(i => JsonDocument.Parse(
                    $$"""{ "type": "publish", "id": "{{prefix}}-{{i}}", "version": 1, "payload": {}, "timestamp": "2024-01-01T00:00:00Z" }""").RootElement.Clone())
                .ToList();
            return await processor.ProcessBatchAsync(events);
        }

        var batches = Enumerable
            .Range(0, 4)
            .Select(n => Task.Run(() => RunBatch($"b{n}")));
        var results = await Task.WhenAll(batches);

        Assert.All(results, r => Assert.Equal((100, 0), (r.Applied, r.Failed)));
        await using var check = new WriteDbContext(options);
        Assert.Equal(400, await check.Entities.CountAsync());
    }

    [Fact]
    public async Task Unhandled_errors_are_a_generic_problem_details_500_in_production()
    {
        await using var factory = new FileDatabaseFactory(_file, environment: "Production",
            readConnection: Path.Combine(Path.GetTempPath(), "missing-dir-for-test", "secret-path.db"));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/entities");
        request.Headers.Authorization = Basic(ApiFactory.ReaderUser, ApiFactory.ReaderPassword);

        using var response = await client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("secret-path", body);
        Assert.DoesNotContain("Sqlite", body, StringComparison.OrdinalIgnoreCase);
    }

    private static AuthenticationHeaderValue Basic(string user, string password) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));

    /// <summary>Real database file; the read connection is opened with Mode=ReadOnly, as in production settings.</summary>
    private sealed class FileDatabaseFactory(string file, string environment = "Development", string? readConnection = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Default", $"Data Source={file}");
            builder.UseSetting("ConnectionStrings:ReadOnly", readConnection is null
                ? $"Data Source={file};Mode=ReadOnly"
                : $"Data Source={readConnection};Mode=ReadOnly");
            builder.UseSetting("Auth:Cms:Username", ApiFactory.CmsUser);
            builder.UseSetting("Auth:Cms:Password", ApiFactory.CmsPassword);
            builder.UseSetting("Auth:Users:0:Username", ApiFactory.AdminUser);
            builder.UseSetting("Auth:Users:0:Password", ApiFactory.AdminPassword);
            builder.UseSetting("Auth:Users:0:Role", "Admin");
            builder.UseSetting("Auth:Users:1:Username", ApiFactory.ReaderUser);
            builder.UseSetting("Auth:Users:1:Password", ApiFactory.ReaderPassword);
            builder.UseSetting("Auth:Users:1:Role", "User");
        }
    }
}
