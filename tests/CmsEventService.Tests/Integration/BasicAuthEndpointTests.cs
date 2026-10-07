using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace CmsEventService.Tests.Integration;

/// <summary>Basic authentication and role checks against the real HTTP pipeline.</summary>
public sealed class BasicAuthEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ValidBatch =
        """[{ "type": "publish", "id": "A", "version": 1, "payload": { "x": 1 }, "timestamp": "2024-01-01T00:00:00Z" }]""";

    private static string Basic(string user, string password) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));

    private async Task<HttpResponseMessage> PostEvents(string? scheme = null, string? parameter = null)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/cms/events")
        {
            Content = new StringContent(ValidBatch, Encoding.UTF8, "application/json")
        };
        if (scheme is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue(scheme, parameter);

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Valid_cms_credentials_are_accepted()
    {
        var response = await PostEvents("Basic", Basic(ApiFactory.CmsUser, ApiFactory.CmsPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Basic_scheme_is_case_insensitive()
    {
        var response = await PostEvents("basic", Basic(ApiFactory.CmsUser, ApiFactory.CmsPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Missing_credentials_get_a_401_with_a_basic_challenge()
    {
        var response = await PostEvents();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Basic");
    }

    [Theory]
    [InlineData(ApiFactory.CmsUser, "wrong-password")]
    [InlineData(ApiFactory.CmsUser, "")]
    [InlineData("unknown-user", ApiFactory.CmsPassword)]
    [InlineData("CMS-INGEST-SVC", ApiFactory.CmsPassword)]
    [InlineData(ApiFactory.CmsUser, ApiFactory.AdminPassword)]
    public async Task Invalid_user_password_combinations_get_a_401(string user, string password)
    {
        var response = await PostEvents("Basic", Basic(user, password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Basic", "%%%not-base64%%%")]
    [InlineData("Basic", "bm9jb2xvbg==")] // "nocolon"
    [InlineData("Basic", "OnBhc3N3b3Jk")] // ":password" (empty username)
    public async Task Malformed_credentials_get_a_401(string scheme, string parameter)
    {
        var response = await PostEvents(scheme, parameter);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Oversized_credentials_get_a_401()
    {
        var response = await PostEvents("Basic", Basic(new string('u', 2000), "p"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Other_auth_schemes_get_a_401()
    {
        var response = await PostEvents("Bearer", "some-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(ApiFactory.AdminUser, ApiFactory.AdminPassword)]
    [InlineData(ApiFactory.ReaderUser, ApiFactory.ReaderPassword)]
    public async Task Valid_user_credentials_cannot_push_events(string user, string password)
    {
        var response = await PostEvents("Basic", Basic(user, password));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_requests_never_reach_the_processor()
    {
        await PostEvents("Basic", Basic(ApiFactory.CmsUser, "wrong"));

        using var client = factory.CreateClient();
        // A later, valid request still works: the failed one left nothing behind.
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Basic(ApiFactory.CmsUser, ApiFactory.CmsPassword));
        var response = await client.PostAsync("/cms/events", new StringContent(ValidBatch, Encoding.UTF8, "application/json"));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"applied\":1", body);
    }

    [Fact]
    public async Task Openapi_document_is_available_without_credentials_and_declares_basic_auth()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"scheme\": \"basic\"", await response.Content.ReadAsStringAsync());
    }
}
