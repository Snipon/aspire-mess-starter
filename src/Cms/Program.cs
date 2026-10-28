WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

#if DEBUG
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
#endif

builder.AddServiceDefaults();

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
