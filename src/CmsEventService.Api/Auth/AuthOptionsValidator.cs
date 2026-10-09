using Microsoft.Extensions.Options;

namespace CmsEventService.Api.Auth;

/// <summary>Fails startup when the configured accounts are unusable or break the assessment's credential rules.</summary>
public sealed class AuthOptionsValidator : IValidateOptions<AuthOptions>
{
    /// <summary>Shortest allowed CMS username.</summary>
    public const int CmsUsernameMinLength = 10;
    /// <summary>Longest allowed CMS username.</summary>
    public const int CmsUsernameMaxLength = 20;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        var errors = new List<string>();

        if (options.Cms.Username.Length is < CmsUsernameMinLength or > CmsUsernameMaxLength)
            errors.Add($"Auth:Cms:Username must be {CmsUsernameMinLength}-{CmsUsernameMaxLength} characters.");

        if (!Guid.TryParse(options.Cms.Password, out _))
            errors.Add("Auth:Cms:Password must be a GUID.");

        if (options.Cms.Username.Contains(':'))
            errors.Add("Auth:Cms:Username must not contain ':'.");

        for (var i = 0; i < options.Users.Count; i++)
        {
            var user = options.Users[i];

            if (string.IsNullOrWhiteSpace(user.Username) || user.Username.Contains(':'))
                errors.Add($"Auth:Users:{i}:Username must be non-empty and must not contain ':'.");

            if (string.IsNullOrEmpty(user.Password))
                errors.Add($"Auth:Users:{i}:Password is required.");

            if (user.Role is not (Roles.User or Roles.Admin))
                errors.Add($"Auth:Users:{i}:Role must be '{Roles.User}' or '{Roles.Admin}'.");
        }

        // The CMS and the API consumers must be different accounts.
        var duplicated = options.Users
            .Select(u => u.Username)
            .Append(options.Cms.Username)
            .GroupBy(u => u, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
        errors.AddRange(duplicated.Select(u => $"Username '{u}' is configured more than once."));

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
