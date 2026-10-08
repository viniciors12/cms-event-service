namespace CmsEventService.Api.Auth;

public static class Policies
{
    /// <summary>Only the CMS organization may push events.</summary>
    public const string CmsIngestion = nameof(CmsIngestion);

    /// <summary>API consumers (users and admins) may read entities.</summary>
    public const string Consumers = nameof(Consumers);

    public const string AdminOnly = nameof(AdminOnly);
}
