#:sdk Aspire.AppHost.Sdk@13.6.4
#:property AspireUseCliBundle=true
#:property UserSecretsId=aspire-test-apphost
#:package Aspire.Hosting.SqlServer@13.6.4
#:package Aspire.Hosting.PostgreSQL@13.6.4
#:package Aspire.Hosting.JavaScript@13.6.4
#:package Aspire.Hosting.Seq@13.6.4
#:package Aspire.Hosting.Keycloak@13.6.4-preview.1.26523.11
#:package CommunityToolkit.Aspire.Hosting.Meilisearch@13.6.1
#:project ./src/Cms/Cms.csproj
#:project ./src/Api/Api.csproj

using System.Security.Cryptography;
using Aspire.Hosting.Publishing;

var builder = DistributedApplication.CreateBuilder(args);

// Parameters. Missing secrets are generated on first run and persisted to the AppHost user secrets (UserSecretsId
// above, so the store does not depend on the checkout path); scripts/init-secrets.sh creates them up front, and
// `aspire secret set Parameters:<name> <value>` overrides one.
IResourceBuilder<ParameterResource> GeneratedSecret(string name, int minLength = 32) =>
    builder.AddParameter(name, new GenerateParameterDefault { MinLength = minLength, Special = false }, secret: true,
        persist: true);

var adminEmail = builder.AddParameter("umbraco-admin-email", "admin@example.com", publishValueAsDefault: true);
var adminPassword = GeneratedSecret("umbraco-admin-password", 16);
var deliveryApiKey = GeneratedSecret("delivery-api-key");
var hmacSecret = builder.AddParameter("hmac-secret", new Base64SecretDefault(64), secret: true, persist: true);
var webhookSecret = GeneratedSecret("webhook-secret");
var reindexSecret = GeneratedSecret("reindex-secret");
var commerceAdminPassword = GeneratedSecret("commerce-admin-password", 16);
var commerceApiKey = GeneratedSecret("commerce-api-key");
var commerceHashSalt = GeneratedSecret("commerce-hash-salt", 64);
var idpAdminPassword = GeneratedSecret("idp-admin-password", 16);
var cmsOidcSecret = GeneratedSecret("cms-oidc-secret");
var commerceOidcSecret = GeneratedSecret("commerce-oidc-secret");
var seqOidcSecret = GeneratedSecret("seq-oidc-secret");
var webOidcSecret = GeneratedSecret("web-oidc-secret");
var webStaffOidcSecret = GeneratedSecret("web-staff-oidc-secret");
var seqProxyCookieSecret = GeneratedSecret("seq-proxy-cookie-secret"); // oauth2-proxy: 16, 24 or 32 bytes
var webSessionSecret = GeneratedSecret("web-session-secret");
var previewApiKey = GeneratedSecret("preview-api-key"); // the web's key for draft content (staff preview)

// Demo articles, blog posts, images and 2000 products, seeded on first start (CMS ContentSeeder, Drupal entrypoint).
// `dotnet new` sets this from --demo; core code works with an empty CMS and catalog.
const bool DemoContent = true;
var demoContent = DemoContent ? "true" : "false";

// Infrastructure containers (persistent so data and startup time survive AppHost restarts).
var sql = builder.AddSqlServer("sql")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume();
var db = sql.AddDatabase("umbraco");

var meilisearch = builder.AddMeilisearch("meilisearch")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume();

// Log search. Apps get ConnectionStrings__seq and ship their OpenTelemetry logs and traces to Seq in addition to the
// Aspire dashboard (ServiceDefaults AddSeqEndpoint). The host port (http://localhost:5342) is Seq's ingestion-only
// port: no UI or API there. The UI and API (container port 80) are not published; people use them through seq-proxy
// below (http://localhost:5341, staff login).
var seq = builder.AddSeq("seq", port: 5342)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume()
    .WithEnvironment("SEQ_API_INGESTIONPORT", "5341")
    .WithEndpoint("http", endpoint => endpoint.TargetPort = 5341)
    .WithUrlForEndpoint("http", url =>
    {
        url.DisplayText = "Seq ingestion (no UI)";
        url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
    });
// AddSeq's /health check would hit the ingestion port (404); seq-proxy's health check covers Seq instead.
foreach (var check in seq.Resource.Annotations.OfType<HealthCheckAnnotation>().ToList())
    seq.Resource.Annotations.Remove(check);

// Identity provider (local stand-in for Entra ID): realm "staff" signs in admins and editors (Umbraco backoffice,
// Drupal admin, Seq), realm "shop" signs in customers (self-registration). Realms, clients and the staff admin user
// (umbraco-admin-email / idp-admin-password) are imported from infra/keycloak on the first start only; the redirect
// URIs there assume the fixed host ports below (cms launch profile, commerce, web, seq-proxy). Admin
// console: https://localhost:8080 (user admin, password keycloak-password).
const string KeycloakUrl = "https://localhost:8080"; // the integration serves HTTPS (dev certificate) on the host port
const string KeycloakInternalUrl = "https://keycloak.dev.internal:8443"; // container-to-container, trusted dev certificate
const string StaffIssuer = KeycloakUrl + "/realms/staff";
const string ShopIssuer = KeycloakUrl + "/realms/shop";
const string StaffOidcPath = "/realms/staff/protocol/openid-connect";
var keycloak = builder.AddKeycloak("keycloak", port: 8080)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume()
    .WithRealmImport("./infra/keycloak")
    .WithEnvironment("KC_HOSTNAME", KeycloakUrl) // tokens carry this issuer, also when containers call keycloak:8080
    .WithEnvironment("KC_HOSTNAME_BACKCHANNEL_DYNAMIC", "true")
    // Every local app sets cookies on "localhost" (cookies ignore the port), and browsers send them all to Keycloak;
    // Quarkus' default 8 KB header limits (HTTP/1.1 and, over HTTPS, HTTP/2) then answer sign-ins with 431. Local
    // only: Azure signs in with Entra ID.
    .WithEnvironment("QUARKUS_HTTP_LIMITS_MAX_HEADER_SIZE", "64K")
    .WithEnvironment("QUARKUS_HTTP_LIMITS_MAX_HEADER_LIST_SIZE", "65536")
    .WithEnvironment("STAFF_ADMIN_EMAIL", adminEmail)
    .WithEnvironment("STAFF_ADMIN_PASSWORD", idpAdminPassword)
    .WithEnvironment("CMS_OIDC_SECRET", cmsOidcSecret)
    .WithEnvironment("COMMERCE_OIDC_SECRET", commerceOidcSecret)
    .WithEnvironment("SEQ_OIDC_SECRET", seqOidcSecret)
    .WithEnvironment("WEB_OIDC_SECRET", webOidcSecret)
    .WithEnvironment("WEB_STAFF_OIDC_SECRET", webStaffOidcSecret);

// Seq UI behind a staff login (oauth2-proxy, generic OIDC): only the "admin" value of the "roles" claim gets in; the
// session is refreshed (roles re-checked) every 5 minutes and sign-out also ends the IdP session. The browser signs in
// at https://localhost:8080 while the container redeems and refreshes tokens, fetches keys and logs out over the
// container network (keycloak.dev.internal, trusted through Aspire's SSL_CERT_DIR), so discovery is skipped and the
// endpoints are explicit. Host port fixed: the IdP redirect URI (infra/keycloak) names it.
var seqProxy = builder.AddContainer("seq-proxy", "quay.io/oauth2-proxy/oauth2-proxy", "v7.15.5")
    .WithArgs(
        "--http-address=0.0.0.0:4180",
        $"--upstream=http://{seq.Resource.Name}:80", // Seq's UI port by container network alias (unpublished)
        "--provider=oidc",
        "--client-id=seq",
        $"--oidc-issuer-url={StaffIssuer}",
        "--skip-oidc-discovery",
        $"--login-url={KeycloakUrl}{StaffOidcPath}/auth",
        $"--redeem-url={KeycloakInternalUrl}{StaffOidcPath}/token",
        $"--oidc-jwks-url={KeycloakInternalUrl}{StaffOidcPath}/certs",
        "--scope=openid email profile", // the default adds "groups" (invalid_scope)
        "--code-challenge-method=S256",
        "--redirect-url=http://localhost:5341/oauth2/callback",
        "--oidc-groups-claim=roles",
        "--allowed-group=admin",
        "--email-domain=*",
        "--skip-provider-button",
        "--cookie-name=_seq_proxy", // cookies ignore ports: keep it apart from other localhost apps
        "--cookie-secure=false", // plain http locally
        "--cookie-expire=8h",
        "--cookie-refresh=5m",
        $"--backend-logout-url={KeycloakInternalUrl}{StaffOidcPath}/logout?id_token_hint={{id_token}}",
        "--skip-auth-route=GET=^/health$") // Seq's health status only, for the health check below
    .WithEnvironment("OAUTH2_PROXY_CLIENT_SECRET", seqOidcSecret)
    .WithEnvironment("OAUTH2_PROXY_COOKIE_SECRET", seqProxyCookieSecret)
    .WithHttpEndpoint(port: 5341, targetPort: 4180)
    .WithUrlForEndpoint("http", url => url.DisplayText = "Seq")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WaitFor(seq);

// Commerce database (Drupal on PostgreSQL), persistent like the CMS database.
var commerceDb = builder.AddPostgres("commerce-postgres")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume()
    .AddDatabase("commercedb", "commerce");

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
    .WithEnvironment("DemoContent__Enabled", demoContent)
    // Backoffice sign-in through the staff realm (button next to the local login). No WaitFor(keycloak): only sign-in
    // needs it, and the OIDC handler fetches discovery lazily.
    .WithEnvironment("BackOfficeOidc__Authority", StaffIssuer)
    .WithEnvironment("BackOfficeOidc__ClientId", "cms")
    .WithEnvironment("BackOfficeOidc__ClientSecret", cmsOidcSecret)
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
    .WithEnvironment("Preview__ApiKey", previewApiKey)
    .WithEnvironment("CustomerAuth__Authority", ShopIssuer) // customer access tokens from the web
    .WithEnvironment("CustomerAuth__Audience", "shop-api")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

// Drupal Commerce (src/Commerce): catalog, carts, orders and the fake card payment, behind the API. The image installs
// Drupal into the empty database on first start and seeds 2000 demo products (DEMO_CONTENT); admin UI at /user/login,
// signed in through the staff realm (OIDC_*; without OIDC_CLIENT_ID the password form stays). The browser is sent to
// the authorization and logout endpoints on localhost; Drupal calls the token endpoint from inside its container.
var commerce = builder.AddDockerfile("commerce", "./src/Commerce")
    .WithReference(commerceDb) // COMMERCEDB_HOST/_PORT/_USERNAME/_PASSWORD/_DATABASENAME, read by settings.php
    .WaitFor(commerceDb)
    .WithVolume("commerce-files", "/opt/drupal/web/sites/default/files")
    .WithEnvironment("DRUPAL_HASH_SALT", commerceHashSalt)
    .WithEnvironment("DRUPAL_ADMIN_EMAIL", adminEmail)
    .WithEnvironment("DRUPAL_ADMIN_PASSWORD", commerceAdminPassword)
    .WithEnvironment("COMMERCE_API_KEY", commerceApiKey)
    .WithEnvironment("DEMO_CONTENT", demoContent)
    .WithEnvironment("OIDC_CLIENT_ID", "commerce")
    .WithEnvironment("OIDC_CLIENT_SECRET", commerceOidcSecret)
    .WithEnvironment("OIDC_ISSUER", StaffIssuer) // the ID token's iss, checked on sign-in
    .WithEnvironment("OIDC_AUTHORIZATION_ENDPOINT", $"{KeycloakUrl}{StaffOidcPath}/auth")
    .WithEnvironment("OIDC_TOKEN_ENDPOINT", $"{KeycloakInternalUrl}{StaffOidcPath}/token")
    .WithEnvironment("OIDC_END_SESSION_ENDPOINT", $"{KeycloakUrl}{StaffOidcPath}/logout")
    .WithHttpEndpoint(port: 8081, targetPort: 80) // fixed: the IdP redirect URI (infra/keycloak) names it
    .WithHttpHealthCheck("/headless/health")
    .WithExternalHttpEndpoints();

api.WithEnvironment("Providers__Commerce", "Drupal")
    .WithEnvironment("Drupal__BaseUrl", commerce.GetEndpoint("http"))
    .WithEnvironment("Drupal__PublicBaseUrl", commerce.GetEndpoint("http"))
    .WithEnvironment("Drupal__ApiKey", commerceApiKey);

// The CMS pushes content changes to the API, and its product picker searches the API's products (endpoint references
// only, no startup dependency).
cms.WithEnvironment("ContentWebhook__TargetUrl",
    ReferenceExpression.Create($"{api.GetEndpoint("http")}/api/webhooks/content"))
    .WithEnvironment("ProductPicker__ApiUrl", api.GetEndpoint("http"));

// Astro SSR front end; only its server talks to the API. Customers sign in at the shop realm (client "web"), staff at
// the staff realm (client "web-staff") for the top bar with shortcuts into the back offices; Aspire points
// NODE_EXTRA_CA_CERTS at the dev certificate, so Node trusts https://localhost:8080.
var web = builder.AddJavaScriptApp("web", "./src/Web", "dev")
    .WithNpm()
    .WithHttpEndpoint(port: 8082, env: "PORT") // fixed: the IdP redirect URIs (infra/keycloak) name it
    .WithExternalHttpEndpoints()
    .WithEnvironment("API_URL", api.GetEndpoint("http"))
    .WithEnvironment("OIDC_AUTHORITY", ShopIssuer)
    .WithEnvironment("OIDC_CLIENT_ID", "web")
    .WithEnvironment("OIDC_CLIENT_SECRET", webOidcSecret)
    .WithEnvironment("SESSION_SECRET", webSessionSecret)
    .WithEnvironment("PREVIEW_API_KEY", previewApiKey)
    .WithEnvironment("STAFF_OIDC_AUTHORITY", StaffIssuer)
    .WithEnvironment("STAFF_OIDC_CLIENT_ID", "web-staff")
    .WithEnvironment("STAFF_OIDC_CLIENT_SECRET", webStaffOidcSecret)
    .WithEnvironment("CMS_BACKOFFICE_URL", ReferenceExpression.Create($"{cms.GetEndpoint("https")}/umbraco"))
    .WithEnvironment("COMMERCE_ADMIN_URL", commerce.GetEndpoint("http"))
    .WithEnvironment("LOGS_URL", seqProxy.GetEndpoint("http"))
    .WithEnvironment("IDP_ADMIN_URL", $"{KeycloakUrl}/admin/staff/console/")
    .WithEnvironment("ASTRO_TELEMETRY_DISABLED", "1") // Astro CLI usage pings would otherwise show up as fetch spans
    .WithReference(seq)
    .WaitFor(api);

// The web builds its sign-in redirect URIs on its public URL.
web.WithEnvironment("SITE_URL", web.GetEndpoint("http"));

// "Preview on website" in the backoffice opens drafts on the website's staff preview (/preview/{key}).
cms.WithEnvironment("Preview__WebUrl", web.GetEndpoint("http"));

builder.Build().Run();

/// <summary>Generates a base64 string of <c>byteCount</c> cryptographically random bytes (e.g. an HMAC key).</summary>
sealed class Base64SecretDefault(int byteCount) : ParameterDefault
{
    public override string GetDefaultValue() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteCount));

    public override void WriteToManifest(ManifestPublishingContext context) =>
        new GenerateParameterDefault { MinLength = byteCount, Special = false }.WriteToManifest(context);
}
