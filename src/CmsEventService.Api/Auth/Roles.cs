namespace CmsEventService.Api.Auth;

/// <summary>Role names carried by the authenticated principal.</summary>
public static class Roles
{
    /// <summary>The CMS organization pushing events.</summary>
    public const string Cms = "Cms";

    /// <summary>A regular API consumer.</summary>
    public const string User = "User";

    /// <summary>An API consumer who also sees hidden entities and can disable them.</summary>
    public const string Admin = "Admin";
}
