using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Content;
using Api.Endpoints;
using Api.Search;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddContentSource();
builder.AddSearchIndex();

builder.Services.AddProblemDetails();

// Strict number handling keeps numbers typed as `integer` (not `integer | string`) in the OpenAPI document.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "Content API";
        document.Info.Description = "Typed content and search API for the web front end. CMS and search engine are swappable adapters.";
        return Task.CompletedTask;
    });

    // Dictionary<string, JsonElement> (Page.properties) is emitted as a bare {"type":"object"}, which openapi-typescript
    // turns into Record<string, never>. Declare the values as "any JSON" so clients get Record<string, unknown>.
    options.AddSchemaTransformer((schema, context, _) =>
    {
        if (context.JsonTypeInfo.Type == typeof(Dictionary<string, JsonElement>))
        {
            schema.AdditionalPropertiesAllowed = true;
            schema.AdditionalProperties = new OpenApiSchema();
        }

        return Task.CompletedTask;
    });
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// OpenAPI + Scalar are mapped in every environment on purpose (demo); restrict with RequireAuthorization or an
// environment check if the API is exposed publicly.
app.MapOpenApi(); // /openapi/v1.json
app.MapScalarApiReference(); // /scalar

app.MapContentEndpoints();
app.MapSearchEndpoints();
app.MapDefaultEndpoints();

app.Run();
