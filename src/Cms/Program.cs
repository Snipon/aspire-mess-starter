using Serilog;
using Serilog.Core;
using Serilog.Sinks.OpenTelemetry;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

#if DEBUG
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
#endif

builder.AddServiceDefaults();

// Umbraco replaces ILoggerFactory with Serilog, so its logs bypass the OpenTelemetry logger provider from
// ServiceDefaults (dashboard + Seq). Umbraco's Serilog pipeline also reads sinks from DI (ReadFrom.Services),
// so add the same two destinations as Serilog sinks.
if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    // Endpoint, protocol, headers and service name come from the OTEL_* variables (Aspire / Container Apps).
    builder.Services.AddSingleton<ILogEventSink>(_ => new LoggerConfiguration().WriteTo.OpenTelemetry(_ => { }).CreateLogger());
}

if (builder.Configuration.GetConnectionString("seq") is { Length: > 0 } seqUrl)
{
    // Same OTLP shape as the other apps' logs in Seq; only the service name/resource attributes come from OTEL_*.
    var seqApiKey = builder.Configuration["Aspire:Seq:ApiKey"];
    builder.Services.AddSingleton<ILogEventSink>(_ => new LoggerConfiguration()
        .WriteTo.OpenTelemetry(options =>
            {
                options.Endpoint = $"{seqUrl}/ingest/otlp";
                options.Protocol = OtlpProtocol.HttpProtobuf;
                if (!string.IsNullOrEmpty(seqApiKey))
                {
                    options.Headers["X-Seq-ApiKey"] = seqApiKey;
                }
            },
            name => name is "OTEL_SERVICE_NAME" or "OTEL_RESOURCE_ATTRIBUTES" ? Environment.GetEnvironmentVariable(name) : null)
        .CreateLogger());
}

// Headless: backoffice + Delivery API only. No AddWebsite()/UseWebsite(), so Umbraco never
// routes front-end requests to Razor templates. Media (/media/*, incl. ImageSharp processing)
// is served by the core Umbraco pipeline and does not depend on the website feature.
IUmbracoBuilder umbraco = builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddDeliveryApi()
    // Normally registered by AddWebsite(); the backoffice Management API depends on its services.
    // ModelsMode is "Nothing" (appsettings.json), so no models are generated.
    .AddModelsBuilder()
    .AddComposers();

// Optional Azure Blob media storage (used by the Azure deployment). Locally media lives in wwwroot/media.
if (!string.IsNullOrEmpty(builder.Configuration["Umbraco:Storage:AzureBlob:Media:ConnectionString"]))
{
    umbraco.AddAzureBlobMediaFileSystem().AddAzureBlobImageSharpCache();
}

umbraco.Build();

WebApplication app = builder.Build();

await app.BootUmbracoAsync();

app.MapDefaultEndpoints();

app.UseUmbraco()
    .WithMiddleware(u => u.UseBackOffice())
    .WithEndpoints(u =>
    {
        // UseBackOfficeEndpoints maps attribute-routed controllers, which includes the Delivery API.
        u.UseBackOfficeEndpoints();
    });

await app.RunAsync();
