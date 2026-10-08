using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CmsEventService.Tests.Integration;

/// <summary>Visibility rules, admin overrides and paging of the read API, through the real HTTP pipeline.</summary>
public sealed class EntitiesEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private HttpClient Client(string? user = null, string? password = null)
    {
        var client = factory.CreateClient();
        if (user is not null)
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        return client;
    }

    private HttpClient Cms() => Client(ApiFactory.CmsUser, ApiFactory.CmsPassword);

    private HttpClient Admin() => Client(ApiFactory.AdminUser, ApiFactory.AdminPassword);

    private HttpClient Reader() => Client(ApiFactory.ReaderUser, ApiFactory.ReaderPassword);

    // Ids are unique per test because the tests of this class share one database.
    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private async Task Send(string type, string id, int version, string timestamp = "2024-01-01T00:00:00Z")
    {
        var body = $$"""[{ "type": "{{type}}", "id": "{{id}}", "version": {{version}}, "payload": { "name": "{{id}}", "n": {{version}} }, "timestamp": "{{timestamp}}" }]""";
        using var response = await Cms().PostAsync("/cms/events", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static List<string> Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()!).ToList();

    private async Task<List<string>> ListIds(HttpClient client, string query = "?pageSize=100")
    {
        using var response = await client.GetAsync("/entities" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Ids(await Json(response));
    }

    // --- visibility -------------------------------------------------------------------------

    [Fact]
    public async Task Users_list_only_published_entities_and_admins_list_everything()
    {
        var published = NewId("pub");
        var unpublished = NewId("unp");
        var disabled = NewId("dis");
        await Send("publish", published, 1);
        await Send("unPublish", unpublished, 1);
        await Send("publish", disabled, 1);
        (await Admin().PostAsync($"/entities/{disabled}/disable", null)).EnsureSuccessStatusCode();

        var userSees = await ListIds(Reader());
        var adminSees = await ListIds(Admin());

        Assert.Contains(published, userSees);
        Assert.DoesNotContain(unpublished, userSees);
        Assert.DoesNotContain(disabled, userSees);
        Assert.Contains(published, adminSees);
        Assert.Contains(unpublished, adminSees);
        Assert.Contains(disabled, adminSees);
    }

    [Fact]
    public async Task Hidden_entity_is_a_404_for_users_and_a_200_for_admins()
    {
        var id = NewId("hid");
        await Send("unPublish", id, 2);

        using var asUser = await Reader().GetAsync($"/entities/{id}");
        using var asAdmin = await Admin().GetAsync($"/entities/{id}");

        Assert.Equal(HttpStatusCode.NotFound, asUser.StatusCode);
        Assert.Equal(HttpStatusCode.OK, asAdmin.StatusCode);
        var body = await Json(asAdmin);
        Assert.False(body.GetProperty("isPublished").GetBoolean());
        Assert.Equal(2, body.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Entity_is_returned_with_its_payload_as_a_json_object()
    {
        var id = NewId("pay");
        await Send("publish", id, 3);

        using var response = await Reader().GetAsync($"/entities/{id}");

        var body = await Json(response);
        Assert.Equal(id, body.GetProperty("id").GetString());
        Assert.Equal(3, body.GetProperty("version").GetInt32());
        Assert.Equal(JsonValueKind.Object, body.GetProperty("payload").ValueKind);
        Assert.Equal(3, body.GetProperty("payload").GetProperty("n").GetInt32());
    }

    [Fact]
    public async Task Unknown_entity_is_a_404()
    {
        using var response = await Reader().GetAsync("/entities/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- admin override ---------------------------------------------------------------------

    [Fact]
    public async Task Admin_can_disable_and_re_enable_an_entity()
    {
        var id = NewId("tog");
        await Send("publish", id, 1);

        using var disable = await Admin().PostAsync($"/entities/{id}/disable", null);
        using var whileDisabled = await Reader().GetAsync($"/entities/{id}");
        using var enable = await Admin().PostAsync($"/entities/{id}/enable", null);
        using var afterEnable = await Reader().GetAsync($"/entities/{id}");

        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, whileDisabled.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, enable.StatusCode);
        Assert.Equal(HttpStatusCode.OK, afterEnable.StatusCode);
    }

    [Fact]
    public async Task Disabled_entity_is_flagged_for_admins()
    {
        var id = NewId("flg");
        await Send("publish", id, 1);
        await Admin().PostAsync($"/entities/{id}/disable", null);

        using var response = await Admin().GetAsync($"/entities/{id}");

        Assert.True((await Json(response)).GetProperty("isDisabledByAdmin").GetBoolean());
    }

    [Fact]
    public async Task Admin_disable_survives_later_cms_updates()
    {
        var id = NewId("sur");
        await Send("publish", id, 1, "2024-01-01T00:00:00Z");
        await Admin().PostAsync($"/entities/{id}/disable", null);

        await Send("publish", id, 2, "2024-01-02T00:00:00Z");

        using var asUser = await Reader().GetAsync($"/entities/{id}");
        using var asAdmin = await Admin().GetAsync($"/entities/{id}");
        Assert.Equal(HttpStatusCode.NotFound, asUser.StatusCode);
        var body = await Json(asAdmin);
        Assert.Equal(2, body.GetProperty("version").GetInt32());
        Assert.True(body.GetProperty("isDisabledByAdmin").GetBoolean());
    }

    [Fact]
    public async Task Disabling_does_not_change_the_cms_data()
    {
        var id = NewId("raw");
        await Send("publish", id, 4);
        await Admin().PostAsync($"/entities/{id}/disable", null);

        using var response = await Admin().GetAsync($"/entities/{id}");

        var body = await Json(response);
        Assert.Equal(4, body.GetProperty("version").GetInt32());
        Assert.True(body.GetProperty("isPublished").GetBoolean());
        Assert.Equal(4, body.GetProperty("payload").GetProperty("n").GetInt32());
    }

    [Fact]
    public async Task Disabling_an_unknown_entity_is_a_404()
    {
        using var response = await Admin().PostAsync("/entities/does-not-exist/disable", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("enable")]
    public async Task Regular_users_cannot_change_the_override(string action)
    {
        var id = NewId("usr");
        await Send("publish", id, 1);

        using var response = await Reader().PostAsync($"/entities/{id}/{action}", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Nobody_can_update_entities_through_the_api()
    {
        var id = NewId("rdo");
        await Send("publish", id, 1);
        var body = new StringContent("""{ "payload": {} }""", Encoding.UTF8, "application/json");

        using var put = await Admin().PutAsync($"/entities/{id}", body);
        using var delete = await Admin().DeleteAsync($"/entities/{id}");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, put.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, delete.StatusCode);
    }

    // --- authentication and roles -----------------------------------------------------------

    [Theory]
    [InlineData("/entities")]
    [InlineData("/entities/anything")]
    public async Task Anonymous_requests_get_a_401(string url)
    {
        using var response = await Client().GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_gets_a_401()
    {
        using var response = await Client(ApiFactory.ReaderUser, "wrong").GetAsync("/entities");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/entities")]
    [InlineData("/entities/anything")]
    public async Task Cms_credentials_cannot_read_entities(string url)
    {
        using var response = await Cms().GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- paging -----------------------------------------------------------------------------

    [Fact]
    public async Task Pages_are_stable_ordered_and_do_not_overlap()
    {
        foreach (var n in Enumerable.Range(0, 5))
            await Send("publish", NewId($"pg{n}"), 1);

        using var first = await Admin().GetAsync("/entities?page=1&pageSize=2");
        using var second = await Admin().GetAsync("/entities?page=2&pageSize=2");

        var firstPage = await Json(first);
        var secondPage = await Json(second);
        var firstIds = Ids(firstPage);
        var secondIds = Ids(secondPage);
        Assert.Equal(2, firstIds.Count);
        Assert.Equal(2, secondIds.Count);
        Assert.Equal(firstIds.Order(StringComparer.Ordinal), firstIds);
        Assert.Empty(firstIds.Intersect(secondIds));
        Assert.True(string.CompareOrdinal(firstIds[^1], secondIds[0]) < 0);
        Assert.True(firstPage.GetProperty("totalCount").GetInt32() >= 5);
        Assert.Equal(2, firstPage.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, secondPage.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task Page_past_the_end_is_empty_not_an_error()
    {
        using var response = await Admin().GetAsync("/entities?page=100000&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Ids(await Json(response)));
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=abc")]
    public async Task Invalid_paging_parameters_are_a_400(string query)
    {
        using var response = await Admin().GetAsync($"/entities?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
