using CmsEventService.Api.Auth;

namespace CmsEventService.Tests.Unit;

public class AuthOptionsValidatorTests
{
    private static readonly AuthOptionsValidator Validator = new();

    private static AuthOptions Valid() => new()
    {
        Cms = new CredentialOptions { Username = "cms-ingest-svc", Password = Guid.NewGuid().ToString() },
        Users = [new UserCredentialOptions { Username = "admin@cms.local", Password = "secret", Role = Roles.Admin }]
    };

    [Fact]
    public void Valid_configuration_passes() =>
        Assert.True(Validator.Validate(null, Valid()).Succeeded);

    [Fact]
    public void Missing_configuration_fails_so_the_app_does_not_start_unprotected() =>
        Assert.True(Validator.Validate(null, new AuthOptions()).Failed);

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(20, true)]
    [InlineData(21, false)]
    public void Cms_username_must_be_10_to_20_characters(int length, bool valid)
    {
        var options = Valid();
        options.Cms.Username = new string('c', length);

        Assert.Equal(valid, Validator.Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public void Cms_password_must_be_a_guid(string password)
    {
        var options = Valid();
        options.Cms.Password = password;

        Assert.True(Validator.Validate(null, options).Failed);
    }

    [Fact]
    public void Cms_and_user_accounts_must_be_different()
    {
        var options = Valid();
        options.Users[0].Username = options.Cms.Username.ToUpperInvariant();

        Assert.True(Validator.Validate(null, options).Failed);
    }

    [Fact]
    public void Usernames_cannot_contain_a_colon_because_basic_auth_splits_on_it()
    {
        var options = Valid();
        options.Users[0].Username = "bad:name";

        Assert.True(Validator.Validate(null, options).Failed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Superuser")]
    [InlineData(Roles.Cms)]
    public void Users_can_only_be_User_or_Admin(string role)
    {
        var options = Valid();
        options.Users[0].Role = role;

        Assert.True(Validator.Validate(null, options).Failed);
    }
}
