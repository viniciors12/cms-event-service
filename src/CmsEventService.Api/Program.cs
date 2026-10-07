using System.Text.Json.Serialization;
using CmsEventService.Api.Application;
using CmsEventService.Api.Auth;
using CmsEventService.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();

builder.Services.AddOpenApi(o => o.AddDocumentTransformer((document, _, _) =>
{
    // Lets Swagger UI offer the "Authorize" button; every endpoint requires Basic credentials.
    var basic = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "basic" };
    document.Components ??= new OpenApiComponents();
    document.Components.SecuritySchemes[BasicAuthenticationHandler.SchemeName] = basic;
    document.SecurityRequirements.Add(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = BasicAuthenticationHandler.SchemeName }
        }] = []
    });
    return Task.CompletedTask;
}));

builder.Services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.SectionName).ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();
builder.Services.AddSingleton<CredentialValidator>();
builder.Services.AddAuthentication(BasicAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(BasicAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorizationBuilder()
    // Everything is confidential: endpoints without an explicit policy still require a signed-in user.
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(Policies.CmsIngestion, p => p.RequireRole(Roles.Cms));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ICmsEventStore, EfCmsEventStore>();
builder.Services.AddScoped<CmsEventProcessor>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
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
