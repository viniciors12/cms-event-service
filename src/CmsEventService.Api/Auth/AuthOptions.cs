namespace CmsEventService.Api.Auth;

/// <summary>Basic-auth accounts: one for the CMS organization and one per API consumer.</summary>
public sealed class AuthOptions
{
    /// <summary>Configuration section that holds the accounts.</summary>
    public const string SectionName = "Auth";

    /// <summary>The CMS organization account.</summary>
    public CredentialOptions Cms { get; set; } = new();

    /// <summary>The API consumer accounts.</summary>
    public List<UserCredentialOptions> Users { get; set; } = [];
}

/// <summary>A username and password.</summary>
public class CredentialOptions
{
    /// <summary>The account name.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>The account password.</summary>
    public string Password { get; set; } = string.Empty;
}

/// <summary>An API consumer account and its role.</summary>
public sealed class UserCredentialOptions : CredentialOptions
{
    /// <summary><see cref="Roles.User"/> or <see cref="Roles.Admin"/>.</summary>
    public string Role { get; set; } = string.Empty;
}
