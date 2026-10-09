using System.Text.Json.Serialization;
using CmsEventService.Api.Application;
using CmsEventService.Api.Auth;
using CmsEventService.Api.Infrastructure;
using CmsEventService.Api.OpenApi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();

builder.Services.AddOpenApi(o => o.AddDocumentTransformer<ApiDocumentTransformer>());

builder.Services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.SectionName).ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();
builder.Services.AddSingleton<CredentialValidator>();
builder.Services.AddAuthentication(BasicAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(BasicAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorizationBuilder()
    // Everything is confidential: endpoints without an explicit policy still require a signed-in user.
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(Policies.CmsIngestion, p => p.RequireRole(Roles.Cms))
    .AddPolicy(Policies.Consumers, p => p.RequireRole(Roles.User, Roles.Admin))
    .AddPolicy(Policies.AdminOnly, p => p.RequireRole(Roles.Admin));

var writeConnection = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
// Reads may use a separate (read-only or replica) connection; without one they share the writer's database.
var readConnection = builder.Configuration.GetConnectionString("ReadOnly") ?? writeConnection;
builder.Services.AddDbContext<WriteDbContext>(o => o.UseSqlite(writeConnection));
builder.Services.AddDbContext<ReadDbContext>(o => o
    .UseSqlite(readConnection)
    .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ICmsEventStore, EfCmsEventStore>();
builder.Services.AddScoped<CmsEventProcessor>();
builder.Services.AddScoped<IEntityReader, EfEntityReader>();
builder.Services.AddScoped<EntityAdminService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<WriteDbContext>().Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    // Unhandled errors become a generic problem-details 500; internals are never sent to the client.
    app.UseExceptionHandler();
}

if (app.Environment.IsDevelopment())
{
    // The document describes the API only (no data), and Swagger UI must be able to fetch it.
    app.MapOpenApi().AllowAnonymous();

    // Swagger UI over the built-in OpenAPI document, served at the root (/ and /index.html).
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "CMS Event Service v1");
        o.RoutePrefix = string.Empty;
    });
}

app.UseHttpsRedirection();

app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposed so integration tests can use WebApplicationFactory<Program>.
public partial class Program;
