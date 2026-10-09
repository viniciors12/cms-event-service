using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CmsEventService.Api.Auth;

/// <summary>Authenticates requests that carry HTTP Basic credentials.</summary>
public sealed class BasicAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    CredentialValidator validator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    /// <summary>Name of the authentication scheme.</summary>
    public const string SchemeName = "Basic";

    private const int MaxHeaderLength = 1024;

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // No credentials is not a failure: endpoints that need them will challenge.
        if (!Request.Headers.TryGetValue("Authorization", out var header))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!AuthenticationHeaderValue.TryParse(header, out var value) ||
            !SchemeName.Equals(value.Scheme, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(value.Parameter))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (value.Parameter.Length > MaxHeaderLength || !TryDecode(value.Parameter, out var username, out var password))
            return Task.FromResult(Fail("malformed credentials"));

        var principal = validator.Validate(username, password);
        if (principal is null)
            return Task.FromResult(Fail("invalid username or password"));

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }

    /// <inheritdoc />
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = $"{SchemeName} realm=\"CmsEventService\", charset=\"UTF-8\"";
        return base.HandleChallengeAsync(properties);
    }

    // Credentials are never logged, and neither is the username, which is attacker-controlled.
    private AuthenticateResult Fail(string reason)
    {
        Logger.LogWarning("Basic authentication failed: {Reason}", reason);
        return AuthenticateResult.Fail(reason);
    }

    private static bool TryDecode(string encoded, out string username, out string password)
    {
        username = password = string.Empty;

        var buffer = new byte[encoded.Length];
        if (!Convert.TryFromBase64String(encoded, buffer, out var written))
            return false;

        var decoded = Encoding.UTF8.GetString(buffer, 0, written);
        var separator = decoded.IndexOf(':');
        if (separator <= 0)
            return false;

        username = decoded[..separator];
        password = decoded[(separator + 1)..];
        return true;
    }
}
