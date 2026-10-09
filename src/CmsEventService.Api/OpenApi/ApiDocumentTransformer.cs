using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

using CmsEventService.Api.Application;
using CmsEventService.Api.Auth;

namespace CmsEventService.Api.OpenApi;

/// <summary>Describes the API as a whole: what it is, how to authenticate, and who can do what.</summary>
public sealed class ApiDocumentTransformer : IOpenApiDocumentTransformer
{
    private const string Description = """
        Receives CMS webhook events and serves the resulting copy of the CMS entities.

        **Authentication.** Every endpoint requires HTTP Basic credentials. Accounts and roles:

        | Role | Can do |
        |---|---|
        | `Cms` | `POST /cms/events` only. This is the CMS organization. |
        | `User` | Read published entities that were not disabled by an admin. |
        | `Admin` | Read everything, including unpublished and disabled entities, and disable or enable entities. |

        **Versions.** An entity only moves forward in version. `publish` makes it visible, `unPublish`
        keeps its data but hides it, and `delete` removes it. An `unPublish` carries the payload, so a
        version that was never published can still be stored.

        **Admin override.** Disabling an entity is a local overlay: it never changes CMS data, and later CMS
        events do not undo it.
        """;

    /// <inheritdoc />
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Info = new OpenApiInfo
        {
            Title = "CMS Event Service",
            Version = "v1",
            Description = Description
        };

        // Relative, so Swagger UI calls whichever host served it (http or https profile) and the
        // committed document does not depend on the host that generated it.
        document.Servers = [new OpenApiServer { Url = "/", Description = "The host serving this document" }];

        // Lets Swagger UI offer the "Authorize" button; every endpoint requires Basic credentials.
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes[BasicAuthenticationHandler.SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "basic"
        };
        document.SecurityRequirements.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = BasicAuthenticationHandler.SchemeName
                }
            }] = []
        });

        DescribeEventBody(document);

        return Task.CompletedTask;
    }

    // The controller reads events as raw JSON so one bad event cannot fail the whole batch, which leaves
    // the generated request body shapeless. This documents the contract the validator enforces.
    private static void DescribeEventBody(OpenApiDocument document)
    {
        var eventSchema = new OpenApiSchema
        {
            Type = "object",
            Description = "One CMS event. Each event in a batch is validated on its own.",
            Required = new HashSet<string> { "type", "id", "timestamp" },
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["type"] = new()
                {
                    Type = "string",
                    Description = "Case-insensitive. add and update are treated as publish.",
                    Enum = new[] { "publish", "add", "update", "unPublish", "delete" }
                        .Select(v => (IOpenApiAny)new OpenApiString(v))
                        .ToList()
                },
                ["id"] = new() { Type = "string", Pattern = EventValidator.IdRegex, Description = "Entity id in the CMS." },
                ["version"] = new()
                {
                    Type = "integer", Format = "int32", Minimum = 1,
                    Description = "Required for publish, add, update and unPublish; ignored for delete."
                },
                ["payload"] = new()
                {
                    Type = "object",
                    Description = $"Required for publish, add, update and unPublish; up to {EventValidator.MaxPayloadLength} characters. " +
                        "An unPublish carries the payload of the version being unpublished."
                },
                ["timestamp"] = new() { Type = "string", Format = "date-time", Description = "When the event happened in the CMS (ISO-8601; UTC when no offset is given). " +
                    $"Rejected when more than {EventValidator.MaxClockSkew.TotalMinutes} minutes ahead of the server clock." }
            }
        };
        document.Components.Schemas["CmsEvent"] = eventSchema;

        var body = document.Paths["/cms/events"].Operations[OperationType.Post].RequestBody.Content["application/json"];
        body.Schema = new OpenApiSchema
        {
            Type = "array",
            Items = new OpenApiSchema { Reference = new OpenApiReference { Type = ReferenceType.Schema, Id = "CmsEvent" } }
        };
        body.Example = new OpenApiArray
        {
            Event("publish", "article-1", 3, "2024-01-02T10:00:00Z", new OpenApiObject { ["title"] = new OpenApiString("Travel guide") }),
            Event("unPublish", "article-2", 4, "2024-01-02T11:00:00Z", new OpenApiObject { ["title"] = new OpenApiString("Draft") }),
            Event("delete", "article-3", null, "2024-01-02T12:00:00Z", null)
        };
    }

    private static OpenApiObject Event(string type, string id, int? version, string timestamp, OpenApiObject? payload)
    {
        var ev = new OpenApiObject { ["type"] = new OpenApiString(type), ["id"] = new OpenApiString(id) };
        if (version is not null)
            ev["version"] = new OpenApiInteger(version.Value);
        if (payload is not null)
            ev["payload"] = payload;
        ev["timestamp"] = new OpenApiString(timestamp);
        return ev;
    }
}
