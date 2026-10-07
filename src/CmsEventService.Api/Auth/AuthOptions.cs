namespace CmsEventService.Api.Auth;

/// <summary>Basic-auth accounts: one for the CMS organization and one per API consumer.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public CredentialOptions Cms { get; set; } = new();

    public List<UserCredentialOptions> Users { get; set; } = [];
}

public class CredentialOptions
{
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

public sealed class UserCredentialOptions : CredentialOptions
{
    /// <summary><see cref="Roles.User"/> or <see cref="Roles.Admin"/>.</summary>
    public string Role { get; set; } = string.Empty;
}
