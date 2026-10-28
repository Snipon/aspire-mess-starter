# Deploying to Azure (Terraform)

The Aspire AppHost (`apphost.cs`) is only used for local development. Production runs on
Azure Container Apps and is described by the Terraform configuration in
[`infra/terraform`](../infra/terraform). Terraform owns every resource, generates every secret
and wires the same configuration keys the AppHost injects locally.

## Architecture

```
                 Internet (HTTPS, TLS terminated by Container Apps ingress)
         ┌───────────────────────┬────────────────────────┬──────────────────────┐
         ▼                       ▼                        ▼                      │
   ┌───────────┐  server-side ┌───────────┐  Delivery API ┌───────────┐  webhook │
   │   web     │ ───────────► │    api    │ ────────────► │    cms    │ ─────────┘
   │ Astro SSR │   API_URL    │ .NET API  │  Api-Key hdr  │ Umbraco   │ POST /api/webhooks/content
   └───────────┘              └─────┬─────┘               └──┬─────┬──┘
                                    │ internal ingress       │     │
                                    ▼                        ▼     ▼
                             ┌─────────────┐      ┌───────────┐ ┌──────────────────┐
                             │ meilisearch │      │ Azure SQL │ │ Storage: blob    │
                             │  (1 replica)│      │ "umbraco" │ │ container "media"│
                             │  EmptyDir   │      └───────────┘ └──────────────────┘
                             └─────────────┘   (index is a cache, rebuilt by api)
```

| Resource | Terraform file | Notes |
| --- | --- | --- |
| Resource group `rg-<name>` | `main.tf` | Everything lives here. |
| Log Analytics workspace | `main.tf` | Container Apps console + system logs. |
| Container Registry (Basic, admin disabled) | `main.tf` | Images `aspire-test/{cms,api,web}:<image_tag>`. |
| User-assigned identity `id-<name>-apps` | `main.tf`, `keyvault.tf` | `AcrPull` on the registry, `Key Vault Secrets User` on the vault. Used by all apps. |
| Key Vault (RBAC mode) | `keyvault.tf` | All secrets, referenced by Container Apps by *versionless* id. |
| Azure SQL server + database `umbraco` | `sql.tf` | SQL auth with a generated password; firewall allows Azure services. |
| Storage account | `storage.tf` | Blob container `media` (Umbraco media, private). |
| Container Apps environment | `containerapps.tf` | Consumption plan. |
| Container App `cms` | `containerapps.tf` | External ingress (backoffice `/umbraco`, media, Delivery API). 1 replica. TCP startup probe with a 10-minute budget for install/migrations. |
| Container App `api` | `containerapps.tf` | External ingress (`/api/*`, OpenAPI, Scalar). 1-3 replicas. |
| Container App `web` | `containerapps.tf` | External ingress, Astro Node standalone on port 8080. 1-3 replicas. |
| Container App `meilisearch` | `containerapps.tf` | **Internal** ingress only, ephemeral `EmptyDir` data (the index is rebuilt from the CMS). 1 replica. |
| Aspire dashboard (`dotNetComponents/aspire-dashboard`) | `observability.tf` | Built-in Container Apps component (azapi). Contributor role for `aspire_dashboard_user_object_ids`. |
| Container App `seq` + Premium Files share | `seq.tf` | Log search UI (external ingress, login required). 1 replica, data on a Premium Azure Files share. |
| Application Insights, availability tests, action group, metric alerts | `observability.tf` | Synthetic tests of web `/`, api `/health`, cms `/health`; alerts on availability, Container Apps and SQL metrics. |

App URLs are `https://<app>.<environment default domain>` (internal apps:
`https://<app>.internal.<default domain>`). Terraform computes them from the environment's
`default_domain` instead of reading them from the apps, which breaks the cms ↔ api cycle (cms
needs the api webhook URL, api needs the cms URL).

### Configuration contract

The keys are the same ones the AppHost sets locally; only the values differ.

| App | Key | Production value |
| --- | --- | --- |
| cms | `ConnectionStrings__umbracoDbDSN` | Key Vault `umbraco-db-connection-string` (Azure SQL) |
| cms | `ConnectionStrings__umbracoDbDSN_ProviderName` | `Microsoft.Data.SqlClient` |
| cms | `Umbraco__CMS__Unattended__InstallUnattended` / `UpgradeUnattended` | `true` |
| cms | `Umbraco__CMS__Unattended__UnattendedUserName` / `UserEmail` / `UserPassword` | `Admin` / `var.umbraco_admin_email` / Key Vault `umbraco-admin-password` |
| cms | `Umbraco__CMS__DeliveryApi__Enabled` / `PublicAccess` / `ApiKey` | `true` / `false` / Key Vault `delivery-api-key` |
| cms | `Umbraco__CMS__Imaging__HMACSecretKey` | Key Vault `hmac-secret` (base64 of 64 random bytes) |
| cms | `Umbraco__Storage__AzureBlob__Media__ConnectionString` / `ContainerName` | Key Vault `media-storage-connection-string` / `media` |
| cms | `Umbraco__CMS__WebRouting__UmbracoApplicationUrl` | cms public URL |
| cms | `ContentWebhook__TargetUrl` / `ContentWebhook__Secret` | `<api url>/api/webhooks/content` / Key Vault `webhook-secret` |
| api | `ConnectionStrings__meilisearch` | Key Vault `meilisearch-connection-string` (`Endpoint=https://meilisearch.internal.…;MasterKey=…`) |
| api | `Providers__Content` / `Providers__Search` | `Umbraco` / `Meilisearch` |
| api | `Umbraco__BaseUrl` | `https+http://cms`, resolved by service discovery via `services__cms__https__0=<cms url>` |
| api | `Umbraco__PublicBaseUrl` | cms public URL (used for media URLs the browser loads) |
| api | `Umbraco__DeliveryApiKey` / `Umbraco__HmacSecretKey` / `Webhooks__Secret` | Key Vault `delivery-api-key` / `hmac-secret` / `webhook-secret` |
| api | `Search__ReindexSecret` | Key Vault `reindex-secret` |
| cms, api | `ConnectionStrings__seq` / `Aspire__Seq__ApiKey` | Seq URL (`https://seq.<default domain>`) / Key Vault `seq-ingestion-api-key` |
| web | `API_URL` | api public URL |
| web | `HOST` / `PORT` | `0.0.0.0` / `8080` |
| all .NET | `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_HTTP_PORTS`, `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `Production`, `8080`, `true` (TLS is terminated at the ingress) |

Production-only additions compared to the AppHost: `services__cms__https__0` (Aspire injects
this itself locally via `WithReference(cms)`), the `Umbraco__Storage__AzureBlob__Media__*` keys,
`UmbracoApplicationUrl`, `Aspire__Seq__ApiKey` (local Seq has no authentication) and the ASP.NET Core
hosting variables. `OTEL_*` variables are not set by Terraform; see [Aspire dashboard](#aspire-dashboard).

## Prerequisites

- Terraform >= 1.9 (or OpenTofu), Azure CLI (`az login`), and Docker or Podman.
- On the target subscription: permission to create resources **and role assignments**
  (Owner, or Contributor + User Access Administrator). Terraform grants its own identity
  `Key Vault Secrets Officer` so it can write the generated secrets.
- Resource providers registered: `Microsoft.App`, `Microsoft.OperationalInsights`,
  `Microsoft.ContainerRegistry`, `Microsoft.KeyVault`, `Microsoft.Sql`, `Microsoft.Storage`,
  `Microsoft.Insights` (Application Insights, availability tests, alerts).

```sh
cd infra/terraform
cp terraform.tfvars.example terraform.tfvars   # set subscription_id, name, location, ...
terraform init
```

`terraform.tfvars` and state files are git-ignored. For team use, configure the commented
`azurerm` backend in `versions.tf`: the state contains every generated secret, so store it
in a locked-down storage account.

## First deployment

Container Apps cannot be created until their images exist, so the first apply runs in two
steps.

**1. Create the registry (and its identity/role) first.**

```sh
terraform apply -target=azurerm_container_registry.this -target=azurerm_role_assignment.apps_acr_pull
```

**2. Build and push the images.** Use a unique tag per release (a git SHA is a good choice).

```sh
ACR=$(terraform output -raw acr_login_server)
TAG=$(git rev-parse --short HEAD)   # or any tag; pass the same value as image_tag below

# Log in (Docker: `az acr login --name <acr_name>` is enough)
az acr login --name "$(terraform output -raw acr_name)" --expose-token --query accessToken -o tsv \
  | podman login "$ACR" -u 00000000-0000-0000-0000-000000000000 --password-stdin

cd ../..   # repository root

# cms: Dockerfile in src/Cms, build context src/ (it needs sibling projects such as ServiceDefaults)
podman build --platform linux/amd64 -f src/Cms/Dockerfile -t "$ACR/aspire-test/cms:$TAG" src
podman push "$ACR/aspire-test/cms:$TAG"

# api: Dockerfile in src/Api, build context src/ (it also needs ServiceDefaults)
podman build --platform linux/amd64 -f src/Api/Dockerfile -t "$ACR/aspire-test/api:$TAG" src
podman push "$ACR/aspire-test/api:$TAG"

# web: Dockerfile in src/Web
podman build --platform linux/amd64 -t "$ACR/aspire-test/web:$TAG" src/Web
podman push "$ACR/aspire-test/web:$TAG"
```

The web image uses the committed `src/Web/src/lib/api/schema.d.ts`, so run `npm run gen:api` in
`src/Web` before building if the API contract changed. Alternatively build in the cloud with
`az acr build` and skip the local runtime, e.g.
`az acr build -r <acr_name> -t aspire-test/cms:$TAG -f src/Cms/Dockerfile src`
(same pattern for `api`; `web` uses context `src/Web`).

**3. Apply everything.**

```sh
cd infra/terraform
terraform apply -var image_tag="$TAG"
terraform output
```

The apply waits 60 s after creating the role assignments (`time_sleep.rbac_propagation`)
because Azure RBAC propagation is eventually consistent. If an apply still fails with a 403
from Key Vault or an image pull error, wait a minute and run `terraform apply` again.

## First run

1. **cms** starts, finds an empty `umbraco` database and runs the unattended install
   (schema + `Admin` user with `umbraco_admin_email`). uSync then imports the document, element
   and data types from `src/Cms/uSync/v18` (copied into the image), the seeder creates the demo
   site, the Articles page, 50 articles and 12 placeholder images, and the content webhook that
   points at `<api url>/api/webhooks/content` is registered. Watch it with
   `az containerapp logs show -g <rg> -n cms --follow`.
2. **api** creates the Meilisearch `content` index and runs the initial full reindex in the
   background, retrying with backoff until the cms answers. After that it checks the index
   every minute and rebuilds it if it is empty (e.g. Meilisearch restarted on fresh storage).
   A reindex builds a private staging index and swaps it in atomically, so search keeps serving
   the previous contents while it runs, and a cms failure halfway leaves the live index intact.
   Every api replica does this on start, which is safe for the same reason. No manual step is
   needed; to force a reindex later:

   ```sh
   SECRET=$(az keyvault secret show --vault-name "$(terraform output -raw key_vault_name)" -n reindex-secret --query value -o tsv)
   curl -X POST -H "X-Reindex-Secret: $SECRET" "$(terraform output -raw api_url)/api/search/reindex"
   ```
3. Open `terraform output -raw web_url` for the site and `<cms_url>/umbraco` for the
   backoffice. The admin password is in Key Vault:

   ```sh
   az keyvault secret show --vault-name "$(terraform output -raw key_vault_name)" -n umbraco-admin-password --query value -o tsv
   ```

   Changing the password in the backoffice afterwards is fine: the unattended settings only
   apply to a fresh install.
4. API docs: `<api_url>/openapi/v1.json` and `<api_url>/scalar`. They are mapped in every
   environment on purpose for this demo (see `src/Api/Program.cs`); restrict them for a real
   production deployment.
5. Content changes reach the search index through the Umbraco webhook (Content Published,
   Unpublished, Deleted and Moved to Recycle Bin). Webhooks only start firing about a minute
   after cms startup (Umbraco's `WebhookFiring` background job), so changes made during a cold
   start are picked up by the next full reindex.

## Updating the application

Build and push new images with a new tag (step 2), then:

```sh
terraform apply -var image_tag="$NEW_TAG"
```

Every app runs in single-revision mode, so a changed image or environment value creates a new
revision and moves traffic to it once it starts. Database schema upgrades run on cms startup
(`UpgradeUnattended=true`).

## Secrets and rotation

All secrets are generated by Terraform (`random_password` / `random_bytes`) and stored in Key
Vault. The apps reference the **versionless** secret ids through the managed identity, so they
always resolve the latest version.

| Key Vault secret | Used by | Generated by |
| --- | --- | --- |
| `delivery-api-key` | cms (`DeliveryApi:ApiKey`), api | `random_password.delivery_api_key` |
| `hmac-secret` | cms (`Imaging:HMACSecretKey`), api | `random_bytes.hmac_secret` |
| `webhook-secret` | cms (webhook header), api (`Webhooks:Secret`) | `random_password.webhook_secret` |
| `reindex-secret` | api (`Search:ReindexSecret`) | `random_password.reindex_secret` |
| `meilisearch-master-key` | meilisearch | `random_password.meilisearch_master_key` |
| `meilisearch-connection-string` | api | derived from the master key |
| `sql-admin-password` | (reference copy) | `random_password.sql_admin` |
| `umbraco-db-connection-string` | cms | derived from the SQL password |
| `umbraco-admin-password` | cms (first install only) | `random_password.umbraco_admin` |
| `media-storage-connection-string` | cms | storage account primary key |
| `seq-admin-password` | seq (first start only) | `random_password.seq_admin` |
| `seq-ingestion-api-key` | cms, api (`Aspire:Seq:ApiKey`); registered in Seq once | `random_password.seq_ingestion_api_key` |

**Rotate a generated secret** by replacing its generator; derived secrets follow automatically:

```sh
terraform apply -replace=random_password.webhook_secret
terraform apply -replace=random_password.sql_admin          # also updates the SQL server password
terraform apply -replace=random_bytes.hmac_secret
```

Container Apps re-read Key Vault references on a schedule, not instantly, and environment
variables are only read at process start. After a rotation, restart the affected apps:

```sh
for app in cms api; do
  rev=$(az containerapp revision list -g <rg> -n $app --query "[?properties.active].name | [0]" -o tsv)
  az containerapp revision restart -g <rg> -n $app --revision "$rev"
done
```

Notes per secret:

- **webhook-secret**: both sides change together. The cms seeding composer updates the
  registered webhook's `X-Webhook-Secret` header on startup, so restart cms *and* api.
  The secret is stored as a plain custom header on the Umbraco webhook, so **backoffice users
  with access to Settings > Webhooks can read it**. It only authorizes `POST /api/webhooks/content`
  on the public api ingress, which re-reads the named node from the CMS and cannot change content,
  so the worst case is load. Rotate it if a Settings user leaves.
- **reindex-secret**: only the api uses it (`X-Reindex-Secret` on `POST /api/search/reindex`,
  compared in constant time; the webhook secret is not accepted there). Restart api. For stricter
  setups, put the reindex endpoint behind internal ingress or IP restrictions.
- **hmac-secret**: previously signed resize URLs stop validating. The api signs URLs on every
  request, so only cached HTML (CDN, browser) holds stale URLs until it expires.
- **delivery-api-key**: restart cms and api together; requests fail with 401 in between.
- **meilisearch-master-key**: `terraform apply` creates new meilisearch and api revisions.
  Meilisearch starts empty (ephemeral storage) and the api rebuilds the index within about a
  minute; search returns no hits in between.
- **media-storage-connection-string**: rotate the storage key with
  `az storage account keys renew`, then `terraform apply` to pick up the new connection string.
- **umbraco-admin-password** only matters for a fresh install; change the live password in the
  backoffice.
- **seq-ingestion-api-key**: after replacing it, register the new token in Seq (see
  [Seq bootstrap](#seq)) and remove the old key there, then restart cms and api. With
  `RequireApiKeyForWritingEvents` on, logs are rejected until the new key is registered.
- **seq-admin-password** only matters for a fresh (empty) Seq data share.

Do not edit Terraform-managed secrets directly in Key Vault: the next `terraform apply`
overwrites them.

## Observability

| What | Where | Answers |
| --- | --- | --- |
| Aspire dashboard | `terraform output -raw aspire_dashboard_url` | Live traces, metrics and structured logs (in memory, recent only). |
| Seq | `terraform output -raw seq_url` | Searchable log/trace history, signals and log-based alerts. |
| Azure Monitor | Application Insights `appi-<name>`, alert rules in the resource group | Is it up from outside? Platform metrics (restarts, 5xx, CPU/memory, replicas, SQL). |
| Container Apps logs | Log Analytics `log-<unique>` / `az containerapp logs show` | Raw console and system logs. |

### Aspire dashboard

`observability.tf` enables the environment's built-in Aspire dashboard
(`Microsoft.App/managedEnvironments/dotNetComponents`, `componentType = "AspireDashboard"`).
azurerm has no resource for it, so it is an `azapi_resource` on API version
`2025-10-02-preview`, which is the version Aspire 13.6's own Container Apps deployment uses
and the newest one the azapi 2.13 schema accepts.

- **Telemetry.** Terraform sets no `OTEL_*` variables, and neither does Aspire's own Container
  Apps deployment (Aspire skips OTLP variables in publish mode). Its dashboard still receives
  telemetry, which indicates that Container Apps injects the dashboard's OTLP endpoint into the
  apps of the environment. ServiceDefaults enables the OTLP exporter only when
  `OTEL_EXPORTER_OTLP_ENDPOINT` is set. This has not been tested against a live environment
  yet, so after the first apply open the dashboard and check that `cms` and `api` appear. If they don't, set `OTEL_EXPORTER_OTLP_ENDPOINT` on the
  apps yourself. The dashboard keeps telemetry in memory, so a dashboard restart clears it. Seq is
  the durable store.
- **Access.** Only users with write access (Contributor or Owner) on the Container Apps
  environment can open it. Put their Entra **user** object IDs in
  `aspire_dashboard_user_object_ids` (`az ad user show --id you@example.com --query id -o tsv`),
  and Terraform assigns them Contributor on the environment. The assignment must be made to the
  user directly: per Microsoft's troubleshooting guide, roles granted through a security group are
  not honoured by the dashboard, which is why there is no group variable. Contributor also allows
  changing the environment, so keep the list short.
- **URL.** `https://aspire-dashboard.ext.<environment default domain>` (output
  `aspire_dashboard_url`). Custom domains are not supported for the dashboard. It can take a
  couple of minutes after creation before the URL stops returning 404/421.
- The environment is the legacy consumption-only type (no `workload_profile`). Aspire's own
  deployments always create a workload-profiles environment. If creating the component fails on
  this environment type, add a `Consumption` workload profile to
  `azurerm_container_app_environment.this` (this recreates the environment).

### Seq

Seq is the log search and alerting tool, both locally (AppHost resource `seq`) and in Azure
(`seq.tf`). The apps send OpenTelemetry logs and traces to it with the same configuration in both
places: `ConnectionStrings__seq` makes `AddServiceDefaults()` call `AddSeqEndpoint("seq")`
(Aspire.Seq), which adds OTLP exporters to `<seq>/ingest/otlp` next to the dashboard exporter.
Umbraco logs through Serilog, which bypasses the OpenTelemetry logger provider, so the cms adds
Serilog OpenTelemetry sinks for the dashboard and Seq. The Node web app is not instrumented.

**Hosting and storage trade-off.** Seq is a database. Datalust recommends only Azure Disks or
**Premium** Azure Files for its storage (standard Azure Files leads to corruption) and lists VMs,
AKS and ACI as supported hosts, not Container Apps. Container Apps cannot mount Azure Disks, so
this setup uses the closest supported storage: a Premium (`FileStorage`) SMB share (100 GiB
minimum, billed by provisioned size), one replica, and never more than one Seq process per share.
The remaining risk is the revision overlap: in single-revision mode Container Apps starts the new
revision before stopping the old one, so a change to the `seq` app (image, environment) briefly
gives two Seq processes the same data directory. Change the Seq app rarely. For a Seq upgrade,
deactivate the running revision first
(`az containerapp revision deactivate -g <rg> -n seq --revision <name>`) and then run
`terraform apply`. If Seq becomes business-critical, move it to an Azure VM or ACI with an Azure
Disk, or use Datalust's hosted Seq, and point `ConnectionStrings__seq` at it. EmptyDir (as used
for Meilisearch) would avoid the overlap problem but loses all logs on every restart.

**Licensing.** Without a license key Seq runs on the free *Individual* plan: one user account
(the `admin` user below), which is fine for one person. A team needs a paid Seq subscription
(<https://datalust.co/pricing>). Apply the key under *Settings > License*.

**Ingestion.** The apps reach Seq through its FQDN `https://seq.<default domain>`. Calls between
apps of the same environment stay inside the environment. The same ingress also serves the UI, so
`/ingest` is reachable from the internet. cms and api send `X-Seq-ApiKey` (Key Vault
`seq-ingestion-api-key`, `Aspire:Seq:ApiKey`), and the bootstrap below makes Seq reject events
without a known key (`RequireApiKeyForWritingEvents`). Until then Seq accepts events with or
without a key, so logging works from the first start.

**Bootstrap (once, after the first apply).**

```sh
SEQ_URL=$(terraform output -raw seq_url)
KV=$(terraform output -raw key_vault_name)
seqcli() { podman run --rm -i docker.io/datalust/seqcli:2025.2 "$@"; }   # or docker

# 1. Open $SEQ_URL and log in as "admin" with the first-run password, which Seq makes you change:
az keyvault secret show --vault-name "$KV" -n seq-admin-password --query value -o tsv

# 2. API keys can only be created by a logged-in user (an API key cannot create keys):
read -rs SEQ_ADMIN_PASSWORD   # the new admin password
INGEST_KEY=$(az keyvault secret show --vault-name "$KV" -n seq-ingestion-api-key --query value -o tsv)
echo "$SEQ_ADMIN_PASSWORD" | seqcli apikey create -s "$SEQ_URL" -t apps --token "$INGEST_KEY" \
  --connect-username admin --connect-password-stdin
SEQ_API_KEY=$(echo "$SEQ_ADMIN_PASSWORD" | seqcli apikey create -s "$SEQ_URL" -t automation \
  --permissions Read,Write,Project,Organization,System --connect-username admin --connect-password-stdin)
# Keep $SEQ_API_KEY somewhere safe (e.g. as a secret in your pipeline); it is the alerts.sh credential.

# 3. Signals, alerts, notifications, retention, ingestion lock-down:
SEQ_URL="$SEQ_URL" SEQ_API_KEY="$SEQ_API_KEY" SEQ_RETENTION="$(terraform output -raw seq_retention)" \
SEQ_REQUIRE_INGEST_API_KEY=true \
ALERT_EMAILS="ops@example.com" SMTP_HOST=smtp.example.com SMTP_PORT=587 \
SMTP_USERNAME=... SMTP_PASSWORD=... SMTP_FROM=seq@example.com \
ALERT_WEBHOOK_URL="https://..." \
../seq/alerts.sh
```

### Alerts

**Azure Monitor** (`observability.tf`) notifies the action group `ag-<name>-ops`: the e-mail
addresses in `alert_email_addresses`, plus `alert_webhook_url` if set. The webhook gets the Azure
Monitor *common alert schema* JSON. Slack and Teams incoming webhooks don't render that format, so
point it at a Logic App or Teams/Power Automate workflow that does.

| Alert | Signal | Fires when |
| --- | --- | --- |
| `availability-{web,api,cms}` | Standard availability test of `web /`, `api /health`, `cms /health` every 5 min from `availability_test_locations` (5 by default), expecting 200 with a valid TLS certificate | All but two locations fail |
| `{cms,api,web}-5xx` | `Requests` with `statusCodeCategory = 5xx` | More than `http_5xx_alert_threshold` (10) in 5 min |
| `<app>-restarts` | `RestartCount` (cumulative per replica) | A replica restarted more than twice. Resolves when the replica is replaced. |
| `<app>-cpu` / `<app>-memory` | `CpuPercentage` / `MemoryPercentage` (preview metrics) | Average above 85% of the container limit for 15 min |
| `{api,web}-replicas-max` | `Replicas` | At `max_replicas` for 15 min (scale-out limit reached) |
| `sql-cpu` / `sql-storage` / `sql-connection-failed` | `cpu_percent` / `storage_percent` / `connection_failed` on the `umbraco` database | CPU above 80% (15 min avg), storage above 85%, more than 5 failed connections in 15 min |

Application Insights (workspace-based, on the existing Log Analytics workspace) is used only for
the availability tests. The apps do not send telemetry to it.

**Seq** (`infra/seq/alerts.sh`) covers what only the logs know. The script is idempotent:
signals, app instances and alerts are matched by title and updated in place. It runs seqcli in a
container (podman or docker) and uses curl + jq for alerts, because seqcli has no alert command.
Notifications use the Seq apps `Seq.App.Mail.Smtp` (any SMTP server, e.g. Azure Communication
Services, Microsoft 365, SendGrid; `SMTP_SECURITY` defaults to `RequireTls`) and
`Seq.App.HttpRequest`, which posts `{"text": "<alert message>"}` to `ALERT_WEBHOOK_URL`. A Slack
incoming webhook accepts that as is. For Teams, use a workflow that reads the `text` field. Seq installs both from nuget.org, so the Seq container needs
outbound internet access. Use the same recipients as `alert_email_addresses`.

| Seq alert | Signal filter (based on what the code logs) | Fires when |
| --- | --- | --- |
| Error spike | Log events (not spans) at Error/Fatal/Critical from any app | More than `ERROR_SPIKE_THRESHOLD` (10) in 5 min |
| Webhook secret rejected | api server spans with `http.route` in `/api/webhooks/content`, `/api/search/reindex` and status 401. The api returns 401 without logging, so the span is the signal. | More than 5 in 5 min |
| Search indexing failing | `Reindex failed; retrying in {Delay}` / `Search index is empty; rebuilding from the content source` (`SearchIndexInitializer`), or any log with a Meilisearch exception | 3 or more in 10 min |
| Delivery API failing | Logs of the Umbraco adapter's typed client (`System.Net.Http.HttpClient.IContentSource.LogicalHandler`): `HTTP request failed…` or a status of 400 or more other than 404 | Any in 5 min |

The filters depend on those message templates, routes and the `IContentSource` HttpClient name.
Update `alerts.sh` when you change them.

### Health endpoints

ServiceDefaults maps `/health` (all registered checks, which on the api includes Meilisearch) and
`/alive` (only the `live`-tagged self check) in **every** environment. The response body is only
the aggregate status (`Healthy`, `Degraded` or `Unhealthy`, with HTTP 503 when unhealthy). No
check names, descriptions or exceptions are returned, so the endpoints are safe to expose on the
public ingress. Both are excluded from tracing. The Seq exporter registers no health check, so a
Seq outage never makes an app unhealthy.

- The availability tests call `api /health` and `cms /health`, so the api test also fails when
  Meilisearch is down.
- The api has an HTTP `liveness_probe` on `/alive`, so a hung process is restarted, but a
  Meilisearch or CMS outage never is. There is deliberately no readiness probe on `/health`, which
  would take every api replica out of rotation when Meilisearch is down. cms keeps its TCP startup
  probe.

## Operational notes and limitations

- **Single replicas.** cms runs one replica: Umbraco keeps data protection keys and caches in
  the container, so a restart signs backoffice users out. Scaling cms out needs persistent data
  protection keys and Umbraco load-balancing configuration. Meilisearch runs one replica,
  because each replica would hold its own index.
- **Meilisearch storage is ephemeral.** Meilisearch's LMDB store must not live on a network
  file system (SMB), and Container Apps runs the old and new revision side by side during every
  update, so a shared volume would be opened by two processes. The index is a cache of CMS
  content, so it runs on `EmptyDir`: every restart or new revision starts empty and the api
  rebuilds it within about a minute (search returns no hits until then). For a large corpus
  where a rebuild is slow, use Meilisearch Cloud or a workload-profile environment with a
  dedicated disk.
- **Search sync of subtrees.** Umbraco only notifies about the node that changed. When a page
  is renamed, moved (`ContentMoved` webhook), unpublished or deleted, the api also re-reads every
  document indexed below the page's old path, so descendants get their new path or drop out of
  the index. A reindex that is running while a delete webhook arrives can still re-add the
  deleted page when it swaps in; the next publish of that page or the next reindex corrects it.
- **Network.** The environment is not VNet-integrated, so Azure SQL allows "Azure services"
  (`0.0.0.0` rule) and the storage account accepts public authenticated traffic. Add a VNet,
  private endpoints and `public_network_access_enabled = false` for stricter isolation.
- **Health probes.** See [Health endpoints](#health-endpoints). cms keeps an explicit TCP
  `startup_probe` with a 10-minute budget (60 × 10 s), because Umbraco runs the unattended
  install and migrations before Kestrel listens.
- **Custom domains.** Add `azurerm_container_app_custom_domain` resources and update
  `cms_url`/`api_url`/`web_url` in `main.tf` so the webhook target, media URLs and
  `UmbracoApplicationUrl` use the custom host names.
- **Destroy.** `terraform destroy` removes everything, including the database and media. The
  Key Vault stays soft-deleted for 7 days, so recreating it with the same name needs
  `az keyvault purge` (or a different `name`).
