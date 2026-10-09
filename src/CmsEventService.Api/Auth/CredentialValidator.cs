using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Options;

namespace CmsEventService.Api.Auth;

/// <summary>Checks a username/password pair against the configured accounts.</summary>
public sealed class CredentialValidator
{
    private readonly List<Account> _accounts;

    /// <summary>Builds the validator from the configured accounts.</summary>
    public CredentialValidator(IOptions<AuthOptions> options)
    {
        var auth = options.Value;
        _accounts = auth.Users
            .Select(u => new Account(u.Username, Hash(u.Password), u.Role))
            .Append(new Account(auth.Cms.Username, Hash(auth.Cms.Password), Roles.Cms))
            .ToList();
    }

    /// <summary>Returns the authenticated principal, or null when the credentials are wrong.</summary>
    public ClaimsPrincipal? Validate(string username, string password)
    {
        var account = _accounts.FirstOrDefault(a => string.Equals(a.Username, username, StringComparison.Ordinal));

        // Hash and compare even for unknown users so response time does not reveal which usernames exist.
        var expected = account?.PasswordHash ?? new byte[SHA256.HashSizeInBytes];
        var matches = CryptographicOperations.FixedTimeEquals(Hash(password), expected);

        if (account is null || !matches)
            return null;

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, account.Username), new Claim(ClaimTypes.Role, account.Role)],
            BasicAuthenticationHandler.SchemeName);
        return new ClaimsPrincipal(identity);
    }

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private sealed record Account(string Username, byte[] PasswordHash, string Role);
}
