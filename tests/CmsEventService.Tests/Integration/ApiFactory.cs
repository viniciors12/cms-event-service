using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace CmsEventService.Tests.Integration;

/// <summary>Runs the real app in memory, with known credentials and an isolated in-memory database.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string CmsUser = "cms-ingest-svc";
    public const string CmsPassword = "343efcc7-4158-460c-b78b-be9b228b4bf5";
    public const string AdminUser = "admin@cms.local";
    public const string AdminPassword = "73987aa8-bf41-42fa-843b-6c6812163158";
    public const string ReaderUser = "reader@cms.local";
    public const string ReaderPassword = "a729e858-e8e9-47dd-bcb9-49d13b618e33";

    private readonly string _database = $"file:{Guid.NewGuid():N}?mode=memory&cache=shared";

    // A shared in-memory database lives only while a connection to it stays open.
    private readonly SqliteConnection _keepAlive;

    public ApiFactory()
    {
        _keepAlive = new SqliteConnection($"Data Source={_database}");
        _keepAlive.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_database}");
        builder.UseSetting("Auth:Cms:Username", CmsUser);
        builder.UseSetting("Auth:Cms:Password", CmsPassword);
        builder.UseSetting("Auth:Users:0:Username", AdminUser);
        builder.UseSetting("Auth:Users:0:Password", AdminPassword);
        builder.UseSetting("Auth:Users:0:Role", "Admin");
        builder.UseSetting("Auth:Users:1:Username", ReaderUser);
        builder.UseSetting("Auth:Users:1:Password", ReaderPassword);
        builder.UseSetting("Auth:Users:1:Role", "User");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _keepAlive.Dispose();
    }
}
