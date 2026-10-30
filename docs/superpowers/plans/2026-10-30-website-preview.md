# Website Preview Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** "Preview on website" in the Umbraco Save and preview menu opens the unpublished draft rendered by the Astro site, for signed-in staff only.

**Architecture:** Umbraco `IUrlProvider` (alias `website`) plus a `previewOption` manifest link the backoffice to
`{web}/preview/{key}`. The web page requires the staff session and calls a new API endpoint
`GET /api/preview/content/{id}` with a shared `X-Preview-Key`. The Umbraco adapter fetches the draft from the Delivery
API with `Preview: true`. Everything on the preview path is `Cache-Control: no-store`.

**Tech Stack:** Umbraco 18.2.2, .NET 10 minimal API + xUnit v3 (TestServer), Astro 7 SSR + openapi-fetch, Aspire 13.6, Terraform (azurerm).

**Spec:** `docs/superpowers/specs/2026-10-30-website-preview-design.md`

## Global Constraints

- Secret name `preview-api-key`, 32 random alphanumeric characters (`GeneratedSecret("preview-api-key")`). Never print or read its value.
- Config keys: API `Preview:ApiKey` (env `Preview__ApiKey`), web `PREVIEW_API_KEY`, CMS `Preview:WebUrl` (env `Preview__WebUrl`).
- Header names: `X-Preview-Key` (web → API), `Preview: true` (API → Delivery API).
- URL provider alias `website`; menu label `Preview on website`; icon `icon-globe`.
- Banner copy: `Preview: unpublished draft`.
- Every preview response (API and web) sends `Cache-Control: no-store`.
- Nothing outside `src/Api/Content/Umbraco` knows about Umbraco; the web knows only the API contract.
- Core code works with an empty CMS and with staff sign-in off; no new dependencies.
- Template rules: keep `AspireTest`/`aspire-test` name forms; no new fixed host ports.
- Generated files (`Api.json`, `schema.d.ts`) are regenerated, never edited by hand.

## Review Focus

1. A key of a different length than the configured one → 401, never an exception (FixedTimeEquals on unequal lengths). Pinned in Task 2.
2. A non-GUID id (`/api/preview/content/not-a-guid`) → 404, not 500. Pinned in Task 1 (adapter) and Task 2 (endpoint).
3. An invalid `culture` → 400, same as `/api/content/{id}`. Pinned in Task 2.
4. A page that has never been published → shown in preview (the reason preview exists). Pinned in the Task 6 manual check.
5. An anonymous visitor following a preview link with `?culture=` → after sign-in they return to the same URL including the query. Pinned in Task 6.

---

### Task 1: Adapter fetches drafts

**Files:**
- Modify: `src/Api/Content/IContentSource.cs`
- Modify: `src/Api/Content/Umbraco/UmbracoContentSource.cs:33-36, 112-114, 167-176`
- Modify: `tests/Api.Tests/SearchSyncTests.cs:253` (`FakeContent` implements the new member)
- Test: `tests/Api.Tests/UmbracoContentSourceTests.cs`

**Interfaces:**
- Produces: `Task<Page?> IContentSource.GetPreviewByIdAsync(string id, string? culture, CancellationToken ct)` — the
  draft (latest saved version) by document key; `null` for a non-GUID id or a missing item. Doc comment: "Unpublished
  changes included; callers must restrict access."

- [ ] **Step 1: Write the failing tests**

Extend `FixtureHandler` to also record the `Preview` header per request (`public List<string?> PreviewHeaders { get; } = []`,
add `request.Headers.TryGetValues("Preview", out var v) ? string.Join(",", v) : null` in `SendAsync`).

```csharp
[Fact]
public async Task Preview_requests_the_draft_by_key()
{
    _handler.Respond("delivery/article.json");

    var page = await _source.GetPreviewByIdAsync("c8a2d1e0-f315-4c2a-8616-f67e181cd240", culture: null, Ct);

    Assert.Equal("/umbraco/delivery/api/v2/content/item/c8a2d1e0-f315-4c2a-8616-f67e181cd240?expand=properties[relatedArticle]",
        Assert.Single(_handler.Requests));
    Assert.Equal("true", Assert.Single(_handler.PreviewHeaders));
    Assert.NotNull(page);
}

[Fact]
public async Task Published_lookup_does_not_ask_for_drafts()
{
    _handler.Respond("delivery/article.json");

    await _source.GetByIdAsync("c8a2d1e0-f315-4c2a-8616-f67e181cd240", culture: null, Ct);

    Assert.Null(Assert.Single(_handler.PreviewHeaders));
}

[Fact]
public async Task Preview_of_a_non_guid_id_is_not_found_without_a_request()
{
    Assert.Null(await _source.GetPreviewByIdAsync("not-a-guid", culture: null, Ct));
    Assert.Empty(_handler.Requests);
}
```

If the first block in `article.json` triggers a follow-up query (latest articles), enqueue `delivery/articles-query.json`
and assert on `_handler.Requests[0]` / `_handler.PreviewHeaders[0]` instead of `Single`.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~UmbracoContentSourceTests"`
Expected: build error, `GetPreviewByIdAsync` not defined.

- [ ] **Step 3: Implement**

Add the interface member. In `UmbracoContentSource`, add `bool preview = false` to `GetItemAsync` and `CreateRequest`;
`CreateRequest` adds `request.Headers.Add("Preview", "true")` when set. `GetPreviewByIdAsync` mirrors `GetByIdAsync`
with `preview: true`. Follow-up queries for blocks (latest articles/blog posts) stay published. `FakeContent` in
`SearchSyncTests` returns `Task.FromResult<Page?>(null)`.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~UmbracoContentSourceTests"`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Api/Content tests/Api.Tests
git commit -m "Fetch draft content from the Delivery API for preview"
```

### Task 2: Preview endpoint

**Files:**
- Create: `src/Api/Endpoints/PreviewEndpoints.cs`
- Create: `src/Api/Content/PreviewOptions.cs`
- Modify: `src/Api/Endpoints/ContentEndpoints.cs` (`WithProductAsync` from `private` to `internal` so preview fills the article's product the same way)
- Modify: `src/Api/Program.cs:84` (`app.MapPreviewEndpoints();` after `MapContentEndpoints`, and bind `PreviewOptions`)
- Regenerate: `src/Api/openapi/Api.json`
- Test: `tests/Api.Tests/PreviewEndpointsTests.cs`

**Interfaces:**
- Consumes: `IContentSource.GetPreviewByIdAsync` (Task 1).
- Produces: `GET /api/preview/content/{id}?culture=`, operation id `GetPreviewContentById`, tag `Preview`; header
  `X-Preview-Key`; responses 200 `Page`, 400/401/404 `ProblemDetails`; all with `Cache-Control: no-store`.
  `public sealed class PreviewOptions { public const string SectionName = "Preview"; public string? ApiKey { get; set; } }`.
  `public static IEndpointRouteBuilder MapPreviewEndpoints(this IEndpointRouteBuilder app)`.

- [ ] **Step 1: Write the failing tests**

Host like `StatusTests` (`WebApplication.CreateBuilder` + `UseTestServer`), register a stub `IContentSource` that
returns a `Page` for id `11111111-1111-1111-1111-111111111111` and `null` otherwise, configure `Preview:ApiKey` =
`"k3y-for-tests"` (or leave it unset), call `app.MapPreviewEndpoints()`.

```csharp
[Theory]
[InlineData(null)]
[InlineData("wrong")]
[InlineData("k3y-for-tests-but-longer")]
public async Task Rejects_a_missing_or_wrong_key(string? key)  // → 401, Cache-Control no-store, stub never called

[Fact] public async Task Returns_the_draft_with_a_valid_key()          // → 200, body title matches stub, Cache-Control no-store
[Fact] public async Task Unknown_or_malformed_id_is_not_found()        // ids "22222222-…" and "not-a-guid" → 404, no-store
[Fact] public async Task Invalid_culture_is_a_bad_request()            // ?culture=%%% with valid key → 400
[Fact] public async Task Is_off_without_a_configured_key()             // no Preview:ApiKey, any header → 404
```

Assert `response.Headers.CacheControl!.NoStore` is true on every response.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~PreviewEndpointsTests"`
Expected: build error, `MapPreviewEndpoints` not defined.

- [ ] **Step 3: Implement**

Order of checks: no configured key → 404; key mismatch → 401 (`CryptographicOperations.FixedTimeEquals` over the UTF-8
bytes; unequal lengths simply return false); invalid culture → 400 (`RequestValidation.IsValidCulture`); then
`GetPreviewByIdAsync` → 404 or 200 via `ContentEndpoints.WithProductAsync`. Set `no-store` with an endpoint filter on
the group (`context.HttpContext.Response.Headers.CacheControl = "no-store"`) so every status gets it. Use
`Problems.NotFound/BadRequest` and `TypedResults.Problem(statusCode: 401)` like the existing endpoints. Document the
header in OpenAPI with `[FromHeader(Name = "X-Preview-Key")] string? key`. Bind `PreviewOptions` in `Program.cs` with
`builder.Services.Configure<PreviewOptions>(builder.Configuration.GetSection(PreviewOptions.SectionName))`.

- [ ] **Step 4: Run tests and the full build**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~PreviewEndpointsTests" && dotnet build src/Api`
Expected: tests pass; `src/Api/openapi/Api.json` now contains `/api/preview/content/{id}`.

- [ ] **Step 5: Commit**

```bash
git add src/Api tests/Api.Tests
git commit -m "Add the staff preview endpoint for draft content"
```

### Task 3: Umbraco preview target

**Files:**
- Create: `src/Cms/Headless/WebsitePreviewUrlProvider.cs`
- Create: `src/Cms/wwwroot/App_Plugins/WebsitePreview/umbraco-package.json`
- Modify: `src/Cms/Headless/HeadlessComposer.cs` (register the provider)

**Interfaces:**
- Consumes: web route `/preview/{key}?culture=` (Task 4).
- Produces: `internal sealed class WebsitePreviewUrlProvider(IConfiguration configuration) : IUrlProvider`, `Alias => "website"`.

- [ ] **Step 1: Implement the provider**

`GetUrl` → `null`; `GetOtherUrls` → empty; `GetPreviewUrlAsync(content, culture, segment)` → when
`Preview:WebUrl` is empty return `Task.FromResult<UrlInfo?>(null)`, otherwise
`UrlInfo.AsUrl($"{webUrl.TrimEnd('/')}/preview/{content.Key:D}" + (culture is null ? "" : $"?culture={Uri.EscapeDataString(culture)}"), Alias, culture)`.
Match the exact `IUrlProvider` member signatures in Umbraco.Core 18.2.2 (check with the compiler; `GetPreviewUrlAsync`
returns `Task<UrlInfo?>`). Register with `builder.UrlProviders().Append<WebsitePreviewUrlProvider>();` in `Compose`.

- [ ] **Step 2: Add the manifest** (same shape and `$schema` as `App_Plugins/ProductPicker/umbraco-package.json`)

```json
{
  "type": "workspaceActionMenuItem",
  "kind": "previewOption",
  "alias": "Headless.PreviewOption.Website",
  "name": "Preview on website",
  "forWorkspaceActions": "Umb.WorkspaceAction.Document.SaveAndPreview",
  "weight": 100,
  "meta": { "label": "Preview on website", "icon": "icon-globe", "urlProviderAlias": "website" }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build AspireTest.slnx`
Expected: 0 errors, 0 new warnings.

- [ ] **Step 4: Commit**

```bash
git add src/Cms
git commit -m "Add a website preview target to the Umbraco backoffice"
```

### Task 4: Web preview page

**Files:**
- Create: `src/Web/src/components/PageView.astro` (body of `[...slug].astro`, moved unchanged)
- Create: `src/Web/src/pages/preview/[id].astro`
- Modify: `src/Web/src/pages/[...slug].astro` (renders `PageView`)
- Modify: `src/Web/src/lib/api/client.ts` (add `previewPage`)
- Modify: `src/Web/src/layouts/Layout.astro` (optional `noindex` prop → `<meta name="robots" content="noindex">`)
- Regenerate: `src/Web/src/lib/api/schema.d.ts`

**Interfaces:**
- Consumes: `GET /api/preview/content/{id}` (Task 2); `getStaffSession`, `staffEnabled` from `lib/staff.ts`; login route `/auth/staff/login?returnTo=`.
- Produces: `PageView.astro` props `{ page: Page; banner?: string; noindex?: boolean }`;
  `export async function previewPage(id: string, culture: string | null): Promise<Page | undefined>` — `undefined` on
  404, throws on any other non-200 (logged by `500.astro`); reads `PREVIEW_API_KEY` and throws if it is not set.

- [ ] **Step 1: Regenerate the client types**

Run: `cd src/Web && npm run gen:api`
Expected: `schema.d.ts` has `"/api/preview/content/{id}"`.

- [ ] **Step 2: Extract `PageView.astro`** and make `[...slug].astro` render it (404 branch stays in the route).
Banner when `banner` is set: a full-width bar above the content using existing tokens (`bg-primary text-primary-foreground`, like `StaffBar.tsx`).

- [ ] **Step 3: Add `previewPage` and `pages/preview/[id].astro`**

Route logic, in order: `!staffEnabled()` → status 404 + `NotFound`; no session → `Astro.redirect('/auth/staff/login?returnTo=' + encodeURIComponent(url.pathname + url.search), 302)`;
`previewPage(id, culture)` → `undefined` → 404 + `NotFound`; else `PageView` with `banner="Preview: unpublished draft"`
and `noindex`. Set `Astro.response.headers.set('Cache-Control', 'no-store')` first, so redirects and 404s carry it too.

- [ ] **Step 4: Type check**

Run: `cd src/Web && npm run check`
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Web
git commit -m "Render Umbraco drafts on /preview for signed-in staff"
```

### Task 5: Secrets, AppHost, Azure and docs

**Files:**
- Modify: `apphost.cs` (parameter after `webSessionSecret`; `Preview__ApiKey` on `api`; `PREVIEW_API_KEY` on `web`; `cms.WithEnvironment("Preview__WebUrl", webCache.GetEndpoint("http"))` next to line 255)
- Modify: `scripts/init-secrets.sh:18` (add `preview-api-key` to `params`)
- Modify: `infra/terraform/keyvault.tf` (`random_password.preview_api_key`, length 40, special false; `"preview-api-key"` in `local.secrets`)
- Modify: `infra/terraform/containerapps.tf` (`preview-api-key` in the api `for_each` at :312 and the web one at :462, env `Preview__ApiKey` / `PREVIEW_API_KEY` with `secret_name`; cms env `Preview__WebUrl = local.web_url`)
- Modify: `README.md` (secrets table row, API section endpoint, a short "Preview" section under the CMS section)

- [ ] **Step 1: Make the edits above.**

- [ ] **Step 2: Verify**

Run: `dotnet build apphost.cs && terraform -chdir=infra/terraform fmt -check -recursive && scripts/init-secrets.sh | grep preview-api-key`
Expected: build succeeds, fmt prints nothing, `set   preview-api-key` (value never printed).

- [ ] **Step 3: Commit**

```bash
git add apphost.cs scripts/init-secrets.sh infra/terraform README.md
git commit -m "Wire the preview key through the AppHost, Azure and docs"
```

### Task 6: End-to-end check on the running stack

- [ ] **Step 1:** `aspire stop && aspire start` (environment changed), wait until `cms`, `api`, `web`, `web-cache` are Healthy.
- [ ] **Step 2:** Full suite: `dotnet test AspireTest.slnx` → all pass.
- [ ] **Step 3:** In the backoffice, change an article's title, **Save** (not publish), choose Save and preview ▸ **Preview on website**. Expected: sign-in via the staff realm (no prompt if already signed in), then the new title with the "Preview: unpublished draft" banner.
- [ ] **Step 4:** The public URL of that article via web-cache (`http://localhost:8082/articles/...`) still shows the old title.
- [ ] **Step 5:** Create a new page, save without publishing, preview it. Expected: it renders (Review Focus 4).
- [ ] **Step 6:** In a private window open `/preview/<key>?culture=en-US`. Expected: 302 to `/auth/staff/login?returnTo=%2Fpreview%2F<key>%3Fculture%3Den-US`; after sign-in, back on the same URL (Review Focus 5).
- [ ] **Step 7:** `curl -si http://localhost:5194/api/preview/content/<key>` → `401` and `Cache-Control: no-store`. `curl -sI` the preview page through web-cache twice while signed in → `Cache-Control: no-store` both times.
- [ ] **Step 8:** Revert the test edits in the backoffice (discard the draft, delete the test page).
