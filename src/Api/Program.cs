using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Auth;
using Api.Commerce;
using Api.Content;
using Api.Endpoints;
using Api.Search;
using Api.Status;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddContentSource();
builder.AddSearchIndex();
builder.AddCommerce();
builder.AddCustomerAuth();

builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<StatusReporter>();

// Strict number handling keeps numbers typed as `integer` (not `integer | string`) in the OpenAPI document.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "Content API";
        document.Info.Description = "Typed content and search API for the web front end. CMS and search engine are swappable adapters.";

        // [JsonPolymorphic] types (Block) are emitted as anyOf + discriminator; exactly one subtype applies, so declare
        // oneOf. Done on the finished document: in a schema transformer it breaks the generated subtype schema names.
        foreach (var schema in document.Components?.Schemas?.Values.OfType<OpenApiSchema>() ?? [])
        {
            if (schema.Discriminator is not null && schema.AnyOf is { Count: > 0 })
            {
                schema.OneOf = schema.AnyOf;
                schema.AnyOf = null;
            }
        }

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

        // [JsonPolymorphic] subtypes (HeroBlock, ...) are emitted without "type": "object" and without the discriminator
        // as required, although the serializer always writes it. Required, it lets clients switch on `type`.
        if (context.JsonTypeInfo.Type.BaseType?.GetCustomAttribute<JsonPolymorphicAttribute>() is { TypeDiscriminatorPropertyName: { } discriminator })
        {
            schema.Type ??= JsonSchemaType.Object;
            schema.Required ??= new HashSet<string>();
            schema.Required.Add(discriminator);
        }

        return Task.CompletedTask;
    });
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCustomerAuth();

// OpenAPI + Scalar are mapped in every environment on purpose (demo); restrict with RequireAuthorization or an
// environment check if the API is exposed publicly.
app.MapOpenApi(); // /openapi/v1.json
app.MapScalarApiReference(); // /scalar

app.MapContentEndpoints();
app.MapPreviewEndpoints();
app.MapSearchEndpoints();
app.MapCommerceEndpoints(app.Configuration);
app.MapStatusEndpoints();
app.MapDefaultEndpoints();

app.Run();
