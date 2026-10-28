# Aspire headless CMS demo

An [Aspire](https://aspire.dev) solution with a headless **Umbraco 18** CMS, a typed **.NET 10 API** with
swappable content and search adapters, **Meilisearch** for search, and an **Astro 7 + Tailwind 4** SSR website.
Local development runs entirely from the Aspire AppHost (`apphost.cs`); Azure deployment uses Terraform
(`infra/terraform`, see [docs/deployment.md](docs/deployment.md)).

## Architecture

```mermaid
flowchart LR
    browser([Browser])

    subgraph aspire[Aspire AppHost - apphost.cs]
        web["web<br/>Astro SSR (src/Web)"]
        api["api<br/>Minimal API + Scalar (src/Api)"]
        cms["cms<br/>Umbraco 18 headless (src/Cms)"]
        meili[("meilisearch<br/>container")]
        sql[("sql<br/>SQL Server container<br/>db: umbraco")]
    end

    browser -- "HTML pages" --> web
    browser -- "images (/media, HMAC-signed resizes)" --> cms
    browser -. "backoffice /umbraco" .-> cms
    web -- "typed client (openapi-fetch)<br/>API_URL" --> api
    api -- "IContentSource → Umbraco adapter<br/>Delivery API v2 + Api-Key" --> cms
    api -- "ISearchIndex → Meilisearch adapter" --> meili
    cms -- "webhook: publish / unpublish / delete / trash<br/>X-Webhook-Secret" --> api
    cms --> sql
```

- **cms** – Umbraco with the Delivery API enabled and protected by an API key (`PublicAccess=false`). There is no
  website rendering: no `AddWebsite()`/`UseWebsite()`, no Views, and templates are blocked in code
  (`src/Cms/Headless/HeadlessComposer.cs`). ImageSharp resize requests must carry a valid `hmac` (HMAC secret
  configured), so unsigned `?width=...` requests get a 400. On first start it installs unattended into SQL Server,
  seeds the `home`/`page` document types plus demo content (`ContentSeeder.cs`), and registers a webhook pointing
  at the API (`ContentWebhookRegistrar.cs`).
- **api** – the only thing the website talks to. It exposes a CMS- and search-engine-neutral contract
  (`src/Api/Contracts`) and builds absolute media URLs and HMAC-signed `srcset` renditions (480/960/1440/1920).
  On startup it ensures the search index and runs a full reindex in the background (retrying until the CMS has
  content). Content webhooks keep the index up to date afterwards.
- **web** – Astro server-side rendering only; the browser never calls the API directly. The API client is
  generated from the API's OpenAPI document.

## Prerequisites

- .NET SDK 10.0.300+
- Aspire CLI 13.6+ (`aspire --version`)
- Node.js 24+ and npm
- A container runtime: Docker, or **Podman** (see below)
- Optional, for deployment: Terraform 1.9+ (or OpenTofu) and the Azure CLI

## Run locally

```sh
aspire run                  # interactive; Ctrl+C to stop
# or detached:
aspire start && aspire describe
aspire stop
```

With Podman, tell Aspire which runtime to use (it is not baked into the repo because it is machine-specific):

```sh
export ASPIRE_CONTAINER_RUNTIME=podman        # bash/zsh
set -gx ASPIRE_CONTAINER_RUNTIME podman       # fish
```

What happens on start:

1. `sql` and `meilisearch` start as **persistent** containers with data volumes (they survive `aspire stop`).
2. `cms` waits for the database, installs Umbraco unattended (first run only), seeds demo content and registers the
   webhook. A fresh install takes about a minute; Umbraco's Delivery API query index fills about a minute after
   that.
3. `api` waits for Meilisearch, then reindexes in the background once the CMS returns content.
4. `web` runs `npm install` + `npm run dev` (Astro dev server on Aspire's `PORT`) once the api is healthy.

Secrets (`umbraco-admin-password`, `delivery-api-key`, `hmac-secret`, `webhook-secret`, plus Aspire's own
`sql-password` and `meilisearch-masterKey`) are generated on first run and stored in the AppHost user secrets:

```sh
dotnet user-secrets list --project apphost.cs
```

Keep them stable: the persistent SQL volume was initialised with `sql-password`. If you reset the user secrets,
also remove the `sql` container and its volume.

## URLs

| What | URL |
| --- | --- |
| Aspire dashboard | `https://localhost:17039` (the login link with token is printed by `aspire run` / `aspire start`) |
| Website | shown on the `web` resource in the dashboard (random port) |
| Umbraco backoffice | `https://localhost:44350/umbraco` (or `http://localhost:28048/umbraco`) |
| Backoffice login | user `admin@example.com` (parameter `umbraco-admin-email`), password = `Parameters:umbraco-admin-password` from the user secrets |
| API reference (Scalar) | `http://localhost:5194/scalar` |
| OpenAPI document | `http://localhost:5194/openapi/v1.json` |
| Delivery API (needs `Api-Key` header) | `http://localhost:28048/umbraco/delivery/api/v2/content/item/about` |

Scalar and the OpenAPI document are mapped in every environment on purpose for this demo (`src/Api/Program.cs`).

### API endpoints

| Method | Path | Notes |
| --- | --- | --- |
| GET | `/api/content?path=/about&culture=` | `Page` or 404 ProblemDetails |
| GET | `/api/content/{id}` | `Page` or 404 |
| GET | `/api/navigation` | `NavItem[]` tree |
| GET | `/api/search?q=&page=1&pageSize=10&contentType=` | `SearchResult`, highlights wrapped in `<mark>` |
| POST | `/api/search/reindex` | full reindex; header `X-Webhook-Secret` |
| POST | `/api/webhooks/content` | Umbraco webhook (`{"id": ...}` or `[{"id": ...}]`); header `X-Webhook-Secret` |

Trigger a reindex by hand:

```sh
curl -X POST -H "X-Webhook-Secret: <webhook-secret>" http://localhost:5194/api/search/reindex
```

## Adapters: how the API stays CMS/search-engine neutral

The endpoints only depend on two ports and the contract records:

- `src/Api/Content/IContentSource.cs` – `GetByPathAsync`, `GetByIdAsync`, `GetNavigationAsync`,
  `GetAllForIndexingAsync`.
- `src/Api/Search/ISearchIndex.cs` – `EnsureIndexAsync`, `SearchAsync`, `UpsertAsync`, `DeleteAsync`, `ResetAsync`.

Implementations live in adapter folders and never leak their types into `Contracts/` or `Endpoints/`:

- `src/Api/Content/Umbraco/` – typed `HttpClient` against the Umbraco Delivery API v2 (`DeliveryApiMapper` maps the
  JSON to `Page`, `ImageUrlSigner` signs renditions with ImageSharp.Web's `HMACUtilities`).
- `src/Api/Search/Meilisearch/` – Meilisearch client (`AddMeilisearchClient("meilisearch")`), index `content`.

The adapter is chosen at startup from configuration (`Providers:Content`, `Providers:Search`); an unknown value
throws at startup. The AppHost sets `Providers__Content=Umbraco` and `Providers__Search=Meilisearch`.

### Adding a provider

Example: a Contentful content source.

1. Create `src/Api/Content/Contentful/` with a `ContentfulContentSource : IContentSource`, its options and an
   `AddContentfulContentSource(this IHostApplicationBuilder)` extension that registers it (typed `HttpClient`,
   options validation, ...). Map the provider's JSON to the contract records there.
2. Add one switch arm in `src/Api/Content/ContentServiceCollectionExtensions.cs`:

   ```csharp
   case "Contentful":
       builder.AddContentfulContentSource();
       break;
   ```
3. Set `Providers__Content=Contentful` (AppHost `WithEnvironment`, or Terraform for Azure) plus the provider's own
   settings. Feed index updates by calling `POST /api/webhooks/content` with `{"id": "..."}` from the new CMS, or
   rely on `POST /api/search/reindex`.

A new search engine works the same way: `src/Api/Search/<Engine>/` implementing `ISearchIndex`, plus one arm in
`src/Api/Search/SearchServiceCollectionExtensions.cs` and `Providers__Search=<Engine>`.

## Regenerating the web API types

`dotnet build src/Api` regenerates `src/Api/openapi/Api.json` (Microsoft.Extensions.ApiDescription.Server). The
website's types are generated from that file and committed:

```sh
dotnet build src/Api
cd src/Web
npm run gen:api      # openapi-typescript ../Api/openapi/Api.json -> src/lib/api/schema.d.ts
npm run build        # astro check + astro build
```

## Tests

```sh
dotnet build AspireTest.slnx
dotnet test tests/Api.Tests     # HMAC signing vs real ImageSharp.Web middleware, mapping, provider selection
```

## Deployment

Azure (Container Apps, Azure SQL, Blob Storage for media, Azure Files for Meilisearch, Key Vault, ACR) is described
in Terraform under `infra/terraform`. See **[docs/deployment.md](docs/deployment.md)** for the architecture, building
and pushing images, `terraform apply`, the first run and secret rotation.
