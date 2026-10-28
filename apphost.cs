#:sdk Aspire.AppHost.Sdk@13.6.4
#:property AspireUseCliBundle=true
#:package Aspire.Hosting.SqlServer@13.6.4
#:package Aspire.Hosting.JavaScript@13.6.4
#:package Aspire.Hosting.Seq@13.6.4
#:package CommunityToolkit.Aspire.Hosting.Meilisearch@13.6.1
#:project ./src/Cms/Cms.csproj
#:project ./src/Api/Api.csproj

using System.Security.Cryptography;
using Aspire.Hosting.Publishing;

var builder = DistributedApplication.CreateBuilder(args);

// Parameters. Secrets are generated on first run and persisted to the AppHost user secrets,
// override them with `aspire config`/user secrets (Parameters:<name>) if needed.
var adminEmail = builder.AddParameter("umbraco-admin-email", "admin@example.com", publishValueAsDefault: true);
var adminPassword = builder.AddParameter("umbraco-admin-password",
    new GenerateParameterDefault { MinLength = 16, Special = false }, secret: true, persist: true);
var deliveryApiKey = builder.AddParameter("delivery-api-key",
    new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);
var hmacSecret = builder.AddParameter("hmac-secret", new Base64SecretDefault(64), secret: true, persist: true);
var webhookSecret = builder.AddParameter("webhook-secret",
    new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);
var reindexSecret = builder.AddParameter("reindex-secret",
    new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);

// Infrastructure containers (persistent so data and startup time survive AppHost restarts).
var sql = builder.AddSqlServer("sql")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume();
var db = sql.AddDatabase("umbraco");

var meilisearch = builder.AddMeilisearch("meilisearch")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume();

// Log search (http://localhost:5341, no authentication locally). Apps get ConnectionStrings__seq and ship their
// OpenTelemetry logs and traces to it in addition to the Aspire dashboard (ServiceDefaults AddSeqEndpoint).
var seq = builder.AddSeq("seq", port: 5341)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume();

// Headless Umbraco CMS.
var cms = builder.AddProject<Projects.Cms>("cms")
    .WithReference(db, "umbracoDbDSN")
    .WaitFor(db)
    .WithReference(seq)
    .WithEnvironment("ConnectionStrings__umbracoDbDSN_ProviderName", "Microsoft.Data.SqlClient")
    .WithEnvironment("Umbraco__CMS__Unattended__InstallUnattended", "true")
    .WithEnvironment("Umbraco__CMS__Unattended__UnattendedUserName", "Admin")
    .WithEnvironment("Umbraco__CMS__Unattended__UnattendedUserEmail", adminEmail)
    .WithEnvironment("Umbraco__CMS__Unattended__UnattendedUserPassword", adminPassword)
    .WithEnvironment("Umbraco__CMS__DeliveryApi__Enabled", "true")
    .WithEnvironment("Umbraco__CMS__DeliveryApi__PublicAccess", "false")
    .WithEnvironment("Umbraco__CMS__DeliveryApi__ApiKey", deliveryApiKey)
    .WithEnvironment("Umbraco__CMS__Imaging__HMACSecretKey", hmacSecret)
    .WithEnvironment("ContentWebhook__Secret", webhookSecret)
    .WithExternalHttpEndpoints();

// Typed API (content + search adapters, Scalar docs at /scalar).
var api = builder.AddProject<Projects.Api>("api")
    .WithReference(cms)
    .WithReference(meilisearch)
    .WithReference(seq)
    .WaitFor(meilisearch) // not WaitFor(cms): the API retries its initial reindex until the CMS is ready
    .WithEnvironment("Providers__Content", "Umbraco")
    .WithEnvironment("Providers__Search", "Meilisearch")
    .WithEnvironment("Umbraco__BaseUrl", "https+http://cms")
    .WithEnvironment("Umbraco__PublicBaseUrl", cms.GetEndpoint("http"))
    .WithEnvironment("Umbraco__DeliveryApiKey", deliveryApiKey)
    .WithEnvironment("Umbraco__HmacSecretKey", hmacSecret)
    .WithEnvironment("Webhooks__Secret", webhookSecret)
    .WithEnvironment("Search__ReindexSecret", reindexSecret)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

// The CMS pushes content changes to the API (endpoint reference only, no startup dependency).
cms.WithEnvironment("ContentWebhook__TargetUrl",
    ReferenceExpression.Create($"{api.GetEndpoint("http")}/api/webhooks/content"));

// Astro SSR front end; only its server talks to the API.
builder.AddJavaScriptApp("web", "./src/Web", "dev")
    .WithNpm()
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .WithEnvironment("API_URL", api.GetEndpoint("http"))
    .WithReference(seq)
    .WaitFor(api);

builder.Build().Run();

/// <summary>Generates a base64 string of <c>byteCount</c> cryptographically random bytes (e.g. an HMAC key).</summary>
sealed class Base64SecretDefault(int byteCount) : ParameterDefault
{
    public override string GetDefaultValue() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteCount));

    public override void WriteToManifest(ManifestPublishingContext context) =>
        new GenerateParameterDefault { MinLength = byteCount, Special = false }.WriteToManifest(context);
}
