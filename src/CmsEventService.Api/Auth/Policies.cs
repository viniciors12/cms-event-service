namespace CmsEventService.Api.Auth;

public static class Policies
{
    /// <summary>Only the CMS organization may push events.</summary>
    public const string CmsIngestion = nameof(CmsIngestion);
}
