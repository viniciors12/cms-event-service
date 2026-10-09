using System.Security.Claims;

using Microsoft.Extensions.Options;

using CmsEventService.Api.Auth;

namespace CmsEventService.Tests.Unit;

public class CredentialValidatorTests
{
    private const string CmsUser = "cms-ingest-svc";
    private const string CmsPassword = "343efcc7-4158-460c-b78b-be9b228b4bf5";

    private static readonly CredentialValidator Validator = new(Options.Create(new AuthOptions
    {
        Cms = new CredentialOptions { Username = CmsUser, Password = CmsPassword },
        Users =
        [
            new UserCredentialOptions { Username = "admin@cms.local", Password = "admin-secret", Role = Roles.Admin },
            new UserCredentialOptions { Username = "reader@cms.local", Password = "reader-secret", Role = Roles.User }
        ]
    }));

    [Theory]
    [InlineData(CmsUser, CmsPassword, Roles.Cms)]
    [InlineData("admin@cms.local", "admin-secret", Roles.Admin)]
    [InlineData("reader@cms.local", "reader-secret", Roles.User)]
    public void Valid_credentials_yield_a_principal_with_the_account_role(string username, string password, string role)
    {
        var principal = Validator.Validate(username, password);

        Assert.NotNull(principal);
        Assert.Equal(username, principal.Identity!.Name);
        Assert.True(principal.IsInRole(role));
        Assert.Single(principal.FindAll(ClaimTypes.Role));
    }

    [Theory]
    [InlineData(CmsUser, "wrong-password")]
    [InlineData(CmsUser, "")]
    [InlineData(CmsUser, CmsPassword + " ")]
    [InlineData(CmsUser, "343EFCC7-4158-460C-B78B-BE9B228B4BF5")]
    [InlineData("unknown-user", CmsPassword)]
    [InlineData("", "")]
    [InlineData("CMS-INGEST-SVC", CmsPassword)]
    [InlineData("admin@cms.local", "reader-secret")]
    public void Wrong_or_unknown_credentials_are_rejected(string username, string password) =>
        Assert.Null(Validator.Validate(username, password));

    [Fact]
    public void A_users_password_does_not_unlock_the_cms_account()
    {
        Assert.Null(Validator.Validate(CmsUser, "admin-secret"));
    }
}
