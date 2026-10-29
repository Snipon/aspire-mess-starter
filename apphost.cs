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
var cachePurgeSecret = builder.AddParameter("cache-purge-secret",
    new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);
var commerceAdminPassword = builder.AddParameter("commerce-admin-password",
    new GenerateParameterDefault { MinLength = 16, Special = false }, secret: true, persist: true);
var commerceApiKey = builder.AddParameter("commerce-api-key",
    new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);
var commerceHashSalt = builder.AddParameter("commerce-hash-salt",
    new GenerateParameterDefault { MinLength = 64, Special = false }, secret: true, persist: true);
var idpAdminPassword = builder.AddParameter("idp-admin-password",
    new GenerateParameterDefault { MinLength = 16, Special = false }, secret: true, persist: true);
var cmsOidcSecret = builder.AddParameter("cms-oidc-secret",
    new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);
var commerceOidcSecret = builder.AddParameter("commerce-oidc-secret",
    new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);
var seqOidcSecret = builder.AddParameter("seq-oidc-secret",
    new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);
var webOidcSecret = builder.AddParameter("web-oidc-secret",
    new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);
var seqProxyCookieSecret = builder.AddParameter("seq-proxy-cookie-secret", // oauth2-proxy: 16, 24 or 32 bytes
    new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);
var webSessionSecret = builder.AddParameter("web-session-secret",
    new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);

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
// URIs there assume the fixed host ports below (cms 28048/44350, commerce 8081, web-cache 8082, seq 5341). Admin
// console: https://localhost:8080 (user admin, password keycloak-password).
const string KeycloakUrl = "https://localhost:8080"; // the integration serves HTTPS (dev certificate) on 8080
const string KeycloakInternalUrl = "https://keycloak.dev.internal:8443"; // container-to-container, trusted dev certificate
const string StaffOidcPath = "/realms/staff/protocol/openid-connect";
var keycloak = builder.AddKeycloak("keycloak", port: 8080)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume()
    .WithRealmImport("./infra/keycloak")
    .WithEnvironment("KC_HOSTNAME", KeycloakUrl) // tokens carry this issuer, also when containers call keycloak:8080
    .WithEnvironment("KC_HOSTNAME_BACKCHANNEL_DYNAMIC", "true")
    .WithEnvironment("STAFF_ADMIN_EMAIL", adminEmail)
    .WithEnvironment("STAFF_ADMIN_PASSWORD", idpAdminPassword)
    .WithEnvironment("CMS_OIDC_SECRET", cmsOidcSecret)
    .WithEnvironment("COMMERCE_OIDC_SECRET", commerceOidcSecret)
    .WithEnvironment("SEQ_OIDC_SECRET", seqOidcSecret)
    .WithEnvironment("WEB_OIDC_SECRET", webOidcSecret);

// Seq UI behind a staff login (oauth2-proxy, generic OIDC): only the "admin" value of the "roles" claim gets in; the
// session is refreshed (roles re-checked) every 5 minutes and sign-out also ends the IdP session. The browser signs in
// at https://localhost:8080 while the container redeems and refreshes tokens, fetches keys and logs out over the
// container network (keycloak.dev.internal, trusted through Aspire's SSL_CERT_DIR), so discovery is skipped and the
// endpoints are explicit. Host port fixed: the IdP redirect URI (infra/keycloak) names it.
builder.AddContainer("seq-proxy", "quay.io/oauth2-proxy/oauth2-proxy", "v7.15.5")
    .WithArgs(
        "--http-address=0.0.0.0:4180",
        $"--upstream=http://{seq.Resource.Name}:80", // Seq's UI port by container network alias (unpublished)
        "--provider=oidc",
        "--client-id=seq",
        $"--oidc-issuer-url={KeycloakUrl}/realms/staff",
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
    .WithEnvironment("DemoContent__Enabled", "true") // demo articles, blog posts and images; remove for a real project
    // Backoffice sign-in through the staff realm (button next to the local login). No WaitFor(keycloak): only sign-in
    // needs it, and the OIDC handler fetches discovery lazily.
    .WithEnvironment("BackOfficeOidc__Authority", $"{KeycloakUrl}/realms/staff")
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
    .WithEnvironment("CustomerAuth__Authority", $"{KeycloakUrl}/realms/shop") // customer access tokens from the web
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
    .WithEnvironment("DEMO_CONTENT", "true") // 2000 demo products; remove for a real project
    .WithEnvironment("OIDC_CLIENT_ID", "commerce")
    .WithEnvironment("OIDC_CLIENT_SECRET", commerceOidcSecret)
    .WithEnvironment("OIDC_ISSUER", $"{KeycloakUrl}/realms/staff") // the ID token's iss, checked on sign-in
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

// Astro SSR front end; only its server talks to the API. Customers sign in at the shop realm (client "web"); Aspire
// points NODE_EXTRA_CA_CERTS at the dev certificate, so Node trusts https://localhost:8080.
var web = builder.AddJavaScriptApp("web", "./src/Web", "dev")
    .WithNpm()
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .WithEnvironment("API_URL", api.GetEndpoint("http"))
    .WithEnvironment("OIDC_AUTHORITY", $"{KeycloakUrl}/realms/shop")
    .WithEnvironment("OIDC_CLIENT_ID", "web")
    .WithEnvironment("OIDC_CLIENT_SECRET", webOidcSecret)
    .WithEnvironment("SESSION_SECRET", webSessionSecret)
    .WithEnvironment("ASTRO_TELEMETRY_DISABLED", "1") // Astro CLI usage pings would otherwise show up as fetch spans
    .WithReference(seq)
    .WaitFor(api);

// Reverse proxy cache in front of the web (nginx, infra/web-cache): the site's entry point. The web's Cache-Control
// header decides what it keeps (src/Web/src/middleware.ts); the web stays reachable directly for debugging.
var webCache = builder.AddContainer("web-cache", "nginx", "1.29-alpine")
    .WithBindMount("infra/web-cache", "/etc/nginx/templates", isReadOnly: true)
    .WithEnvironment("NGINX_ENVSUBST_OUTPUT_DIR", "/etc/nginx") // nginx.conf.template -> /etc/nginx/nginx.conf
    .WithEnvironment("WEB_UPSTREAM", web.GetEndpoint("http"))
    .WithEnvironment("CACHE_PURGE_SECRET", cachePurgeSecret)
    .WithHttpEndpoint(port: 8082, targetPort: 8080) // fixed: the IdP redirect URI (infra/keycloak) names it
    .WithExternalHttpEndpoints()
    .WaitFor(web);

// The web builds its sign-in redirect URIs on the public entry point (web-cache), not on its own port.
web.WithEnvironment("SITE_URL", webCache.GetEndpoint("http"));

// The API purges the whole web cache after each content webhook (publish, unpublish, ...), once the search index is
// updated (endpoint reference only, no startup dependency).
api.WithEnvironment("WebCache__PurgeUrl", ReferenceExpression.Create($"{webCache.GetEndpoint("http")}/_purge"))
    .WithEnvironment("WebCache__PurgeSecret", cachePurgeSecret);

builder.Build().Run();

/// <summary>Generates a base64 string of <c>byteCount</c> cryptographically random bytes (e.g. an HMAC key).</summary>
sealed class Base64SecretDefault(int byteCount) : ParameterDefault
{
    public override string GetDefaultValue() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteCount));

    public override void WriteToManifest(ManifestPublishingContext context) =>
        new GenerateParameterDefault { MinLength = byteCount, Special = false }.WriteToManifest(context);
}
