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
| web | `API_URL` | api public URL |
| web | `HOST` / `PORT` | `0.0.0.0` / `8080` |
| all .NET | `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_HTTP_PORTS`, `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `Production`, `8080`, `true` (TLS is terminated at the ingress) |

Production-only additions compared to the AppHost: `services__cms__https__0` (Aspire injects
this itself locally via `WithReference(cms)`), the `Umbraco__Storage__AzureBlob__Media__*` keys,
`UmbracoApplicationUrl`, and the ASP.NET Core hosting variables.

## Prerequisites

- Terraform >= 1.9 (or OpenTofu), Azure CLI (`az login`), and Docker or Podman.
- On the target subscription: permission to create resources **and role assignments**
  (Owner, or Contributor + User Access Administrator). Terraform grants its own identity
  `Key Vault Secrets Officer` so it can write the generated secrets.
- Resource providers registered: `Microsoft.App`, `Microsoft.OperationalInsights`,
  `Microsoft.ContainerRegistry`, `Microsoft.KeyVault`, `Microsoft.Sql`, `Microsoft.Storage`.

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
   (schema + `Admin` user with `umbraco_admin_email`). The seeding composer then creates the
   `home`/`page` document types and sample content, and registers the content webhook that
   points at `<api url>/api/webhooks/content`. Watch it with
   `az containerapp logs show -g <rg> -n cms --follow`.
2. **api** creates the Meilisearch `content` index and runs the initial full reindex in the
   background, retrying with backoff until the cms answers. After that it checks the index
   every minute and rebuilds it if it is empty (e.g. Meilisearch restarted on fresh storage).
   A reindex builds a private staging index and swaps it in atomically, so search keeps serving
   the previous contents while it runs, and a cms failure halfway leaves the live index intact.
   Every api replica does this on start, which is safe for the same reason. No manual step is
   needed; to force a reindex later:

   ```sh
   SECRET=$(az keyvault secret show --vault-name "$(terraform output -raw key_vault_name)" -n webhook-secret --query value -o tsv)
   curl -X POST -H "X-Webhook-Secret: $SECRET" "$(terraform output -raw api_url)/api/search/reindex"
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
| `meilisearch-master-key` | meilisearch | `random_password.meilisearch_master_key` |
| `meilisearch-connection-string` | api | derived from the master key |
| `sql-admin-password` | (reference copy) | `random_password.sql_admin` |
| `umbraco-db-connection-string` | cms | derived from the SQL password |
| `umbraco-admin-password` | cms (first install only) | `random_password.umbraco_admin` |
| `media-storage-connection-string` | cms | storage account primary key |

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
  with access to Settings > Webhooks can read it**. It authorizes `POST /api/webhooks/content`
  and `POST /api/search/reindex` on the public api ingress. Neither can change content (both only
  re-read the CMS, and a reindex never empties the live index), so the worst case is load. Rotate
  it if a Settings user leaves. For stricter setups, put the reindex endpoint behind internal
  ingress or IP restrictions.
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

Do not edit Terraform-managed secrets directly in Key Vault: the next `terraform apply`
overwrites them.

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
- **Health probes.** The default ServiceDefaults only map `/health` and `/alive` in
  Development, so the apps use TCP probes. cms has an explicit TCP `startup_probe` with a
  10-minute budget (60 × 10 s), because Umbraco runs the unattended install and migrations
  before Kestrel listens. Map the health endpoints in Production and add
  `liveness_probe`/`readiness_probe` blocks if you want HTTP probes.
- **Custom domains.** Add `azurerm_container_app_custom_domain` resources and update
  `cms_url`/`api_url`/`web_url` in `main.tf` so the webhook target, media URLs and
  `UmbracoApplicationUrl` use the custom host names.
- **Destroy.** `terraform destroy` removes everything, including the database and media. The
  Key Vault stays soft-deleted for 7 days, so recreating it with the same name needs
  `az keyvault purge` (or a different `name`).
