using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CmsEventService.Tests.Integration;

/// <summary>
/// Keeps docs/openapi.json in step with what the running API publishes, so the committed
/// specification never drifts from the code.
/// </summary>
public sealed class OpenApiDocumentTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string RegenerateVariable = "UPDATE_OPENAPI";

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [Fact]
    public async Task Committed_openapi_document_matches_the_api()
    {
        using var client = factory.CreateClient();
        var actual = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"))!;
        var path = Path.Combine(RepositoryRoot(), "docs", "openapi.json");

        if (Environment.GetEnvironmentVariable(RegenerateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual.ToJsonString(Pretty) + "\n", new UTF8Encoding(false));
        }

        Assert.True(File.Exists(path), $"docs/openapi.json is missing. Create it with {RegenerateVariable}=1 dotnet test.");
        var committed = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.True(
            JsonNode.DeepEquals(committed, actual),
            $"docs/openapi.json is out of date. Regenerate it with {RegenerateVariable}=1 dotnet test --filter OpenApiDocumentTests");
    }

    [Fact]
    public async Task Document_describes_every_endpoint_with_a_summary_and_requires_basic_auth()
    {
        using var client = factory.CreateClient();
        var document = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"))!;

        var operations = document["paths"]!.AsObject()
            .SelectMany(path => path.Value!.AsObject().Select(op => (Route: $"{op.Key} {path.Key}", Operation: op.Value!)))
            .ToList();

        Assert.Equal(5, operations.Count);
        Assert.All(operations, o => Assert.False(
            string.IsNullOrWhiteSpace(o.Operation["summary"]?.GetValue<string>()), $"{o.Route} has no summary"));
        Assert.Equal("basic", document["components"]!["securitySchemes"]!["Basic"]!["scheme"]!.GetValue<string>());
        Assert.NotEmpty(document["security"]!.AsArray());
    }

    // Resolved from this source file's location, so it works wherever the test binaries are built.
    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CmsEventService.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not find the repository root.");
    }
}
