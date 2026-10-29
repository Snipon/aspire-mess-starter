# AGENTS.md

Guidance for coding agents working in this repository. The README is the full reference; this file is the short
version of what to run, what not to touch and how changes flow through the stack.

## Layout

| Path | What |
| --- | --- |
| `apphost.cs` | Aspire AppHost (file-based): sql, meilisearch, seq, cms, api, web, web-cache |
| `src/Cms` | Headless Umbraco 18. Schema in `uSync/v18`; seeding, webhooks and Delivery API query extensions in `Headless/` |
| `src/Api` | .NET 10 minimal API. `Contracts/` is the public contract; `Content/` and `Search/` are adapters behind `IContentSource`/`ISearchIndex` |
| `src/Web` | Astro 7 SSR + React + Tailwind 4 + shadcn/ui. Talks only to the API (server-side) |
| `infra/web-cache` | nginx reverse proxy cache + njs purge endpoint |
| `infra/terraform` | Azure deployment (no web-cache there) |
| `tests/Api.Tests` | xUnit tests for the API (mapping fixtures in `Fixtures/delivery`) |

## Commands

```sh
dotnet build AspireTest.slnx && dotnet test AspireTest.slnx   # API/CMS build and tests
dotnet build apphost.cs                                        # AppHost
(cd src/Web && npm run check)                                  # astro check (types)
terraform -chdir=infra/terraform fmt -check -recursive

aspire start && aspire describe        # run detached; aspire stop to stop
aspire logs <resource>                 # cms, api, web, web-cache, ...
```

On a running AppHost, rebuild one .NET resource instead of restarting everything (Aspire MCP
`execute_resource_command` with `rebuild` on `cms` or `api`). Environment changes in `apphost.cs` need an AppHost
restart. The web runs `npm run dev` and hot-reloads.

## Generated files: never edit by hand

| File | Regenerate with | CI fails when stale |
| --- | --- | --- |
| `src/Api/Content/Umbraco/delivery-api.json` | `node scripts/gen-delivery-api.mjs --from http://localhost:<cms http port>` (running CMS) | — |
| `src/Api/Content/Umbraco/DeliveryApiModels.g.cs` | `node scripts/gen-delivery-api.mjs` | yes |
| `src/Api/openapi/Api.json` | `dotnet build src/Api` | yes |
| `src/Web/src/lib/api/schema.d.ts` | `cd src/Web && npm run gen:api` (after the API build) | yes |
| README architecture diagram | `node scripts/gen-architecture.mjs` | warning; CI commits it on main |

## Adding a content type or block (end to end)

1. **CMS schema**: add/edit `src/Cms/uSync/v18/ContentTypes/*.config` (or edit in the backoffice, which exports
   there). Register blocks in `DataTypes/ContentBlocks.config`, allowed children in the parent type's `<Structure>`.
   An existing database only imports uSync on first boot: import in the backoffice (Settings > uSync > Import),
   then restart the cms.
2. **Delivery API models**: refresh `delivery-api.json` from the running CMS and regenerate the models (table above).
3. **API**: map the new type in `DeliveryApiMapper` (unknown types and blocks are skipped or passed through, never
   fatal), add contract records in `Contracts/` (blocks need a `[JsonDerivedType]` on `Block`), index it in
   `SearchDocument.FromPage`, and exclude item types from navigation in `UmbracoContentSource.GetNavigationAsync`.
   Add mapper tests.
4. **Web**: `npm run gen:api`, export the type in `src/lib/api/client.ts`, render it (`BlockList.astro`,
   `[...slug].astro`). Prefer extending existing components over copies.
5. **Demo content** (optional): `ContentSeeder.cs`; it must stay idempotent and top up existing installs.
6. **Docs**: README content model table and API section.

## Rules

- **Secrets**: all secrets are Aspire parameters in the AppHost user secrets (`scripts/init-secrets.sh`). Never put
  them in `appsettings*.json`, `.env` or code, and never read or print their values; ask the user to run
  `aspire secret get ...` themselves when one is needed.
- **Demo content is opt-in** (`DemoContent:Enabled`, set in `apphost.cs` and `containerapps.tf`). Core code must
  work with an empty CMS.
- **Adapters stay neutral**: nothing outside `Content/Umbraco` and `Search/Meilisearch` knows about Umbraco or
  Meilisearch; the web knows only the API contract.
- **Caching**: the web middleware sets the shared-cache policy (`s-maxage`); pages that must not be cached send
  `Cache-Control: no-store`. The API purges web-cache after each content webhook (`WebCachePurger`).
- **Style**: match the surrounding code (comment density, naming, file-scoped namespaces, records for contracts).
  Keep changes minimal; no new dependencies for a few lines of code.
- **Persistent containers** (sql, meilisearch, seq) hold local data; do not remove them or their volumes without
  asking.
- Aspire-specific agent skills live in `.agents/skills` (orchestration, monitoring, deployment).
