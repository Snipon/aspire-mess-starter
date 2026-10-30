# Hero Video Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The hero block gets an optional Umbraco media-library video that plays as a full-viewport cover behind the hero text, with the image as poster and fallback.

**Architecture:** A new `video` property (Video Media Picker) on `heroBlock` in uSync; regenerated Delivery API models;
a `Video(Url, MimeType)` contract on `HeroBlock` mapped by `DeliveryApiMapper.GetVideo`; `Hero.astro` rewritten as one
full-cover design (video → image → plain).

**Tech Stack:** Umbraco 18 (uSync), .NET 10 API + xUnit v3, Astro 7 + Tailwind 4, ffmpeg (test clip only).

**Spec:** `docs/superpowers/specs/2026-10-30-hero-video-design.md`

## Global Constraints

- Generated files are regenerated, never hand-edited: `delivery-api.json` and `DeliveryApiModels.g.cs`
  (`node scripts/gen-delivery-api.mjs --from http://localhost:28048`), `src/Api/openapi/Api.json` (`dotnet build src/Api`),
  `src/Web/src/lib/api/schema.d.ts` (`npm run gen:api`).
- Video media type key `f6c515bb-653c-4bdc-821c-987729ebe327`; MIME map (case-insensitive extension): `mp4` → `video/mp4`, `webm` → `video/webm`, `ogv`/`ogg` → `video/ogg`.
- Unknown or unusable media maps to `null`, never throws (AGENTS: unknown types and blocks are never fatal).
- Only `Content/Umbraco` knows Umbraco; the web knows only the API contract.
- Never read or print secrets; backoffice actions (uSync import, media upload) are the user's.
- Match surrounding style; no new dependencies.

## Review Focus

1. Video picked but the media item was deleted / unpublished (Delivery API returns an empty array or a non-video) → hero falls back to its image. Pinned in Task 2 (`null` cases).
2. Hero that is not the first block (e.g. further down a page) → still full width, but must not pull itself up under the header. Pinned in Task 3 Step 3 (check a page with the hero second is not possible in demo content; reviewer checks the `first`-only top offset).
3. Narrow viewport (375px) and wide (1920px) → no horizontal scroll, text column aligned with the header. Pinned in Task 3 Step 4 screenshots.
4. `prefers-reduced-motion: reduce` → video hidden, image visible. Pinned in Task 4 Step 3.
5. Light-on-media text contrast over a bright image → overlay keeps headline and subheading readable. Pinned in Task 3 Step 4 screenshot review.

---

### Task 1: CMS schema (Video Media Picker + `video` on the hero)

**Files:**
- Create: `src/Cms/uSync/v18/DataTypes/VideoMediaPicker.config`
- Modify: `src/Cms/uSync/v18/ContentTypes/heroblock.config`

**Interfaces:**
- Produces: data type `Video Media Picker` (new fixed GUID key, `Umbraco.MediaPicker3`, `filter` = `f6c515bb-653c-4bdc-821c-987729ebe327`, `multiple: false`, `validationLimit` 0–1), and `heroBlock` property alias `video` (name "Video", new GUID key, SortOrder 3 after `image` — shift later sort orders by one, tab `content`, not mandatory, description "Plays muted and looping behind the hero. The image is its poster and the fallback.").

- [ ] **Step 1:** Write the data type file modelled exactly on `ImageMediaPicker.config` (BOM and layout included).
- [ ] **Step 2:** Add the `GenericProperty` to `heroblock.config` after `image`.
- [ ] **Step 3:** `dotnet build src/Cms` → succeeds (uSync files are content, build guards nothing else).
- [ ] **Step 4:** Commit — "Add a video picker to the hero block".
- [ ] **Step 5 (user):** rebuild/restart the cms resource, then in the backoffice Settings > uSync > Import, then restart the cms. Ask the user and wait; confirm afterwards that `http://localhost:28048/umbraco/swagger/delivery/swagger.json` (or the URL `gen-delivery-api.mjs` uses) lists `video` on the hero element.

### Task 2: API contract and mapping

**Files:**
- Regenerate: `src/Api/Content/Umbraco/delivery-api.json`, `DeliveryApiModels.g.cs`, `src/Api/openapi/Api.json`
- Create: `src/Api/Contracts/Video.cs`
- Modify: `src/Api/Contracts/Blocks.cs:16` (`HeroBlock` + `Video? Video`), `src/Api/Content/Umbraco/DeliveryApiMapper.cs` (hero mapping, `GetVideo`), every other `new HeroBlock(` call site (tests)
- Test: `tests/Api.Tests/DeliveryApiMapperTests.cs`, `tests/Api.Tests/ContractSerializationTests.cs`

**Interfaces:**
- Produces: `public sealed record Video(string Url, string MimeType);` (namespace `Api.Contracts`, doc comment like `Image`); `HeroBlock(string Heading, string? Subheading, Image? Image, Link? Cta, Video? Video)`; JSON `video: { url, mimeType } | null`.

- [ ] **Step 1:** Regenerate the Delivery API JSON and models (constraint command); `git diff --stat` shows only the hero `Video` property and its media model use.
- [ ] **Step 2: Failing tests** in `DeliveryApiMapperTests` (inline JSON parsed with `Parse<ApiContentResponseModel>` from a copy of `delivery/site.json` whose hero block properties get a `video` array):

```csharp
[Fact] Maps_hero_video_with_absolute_url_and_mime_type()   // video item {mediaType:"umbracoMediaVideo", url:"/media/x/clip.MP4", extension:"mp4"} → Video("https://cms.example.com/media/x/clip.MP4","video/mp4"); image still mapped
[Theory] [InlineData("webm","video/webm")] [InlineData("ogv","video/ogg")] [InlineData("ogg","video/ogg")]
Maps_known_video_extensions(string ext, string mime)
[Fact] Ignores_a_non_video_or_unknown_video()              // mediaType "Image" → Video null; umbracoMediaVideo with extension "mov" → null; url "" → null
[Fact] Hero_without_video_keeps_its_image()                // existing site.json → hero.Video null, Image not null
```

  `ContractSerializationTests`: the existing `HeroBlock` sample gets `new Video("https://cdn/clip.mp4", "video/mp4")` and the round-trip assertion checks `Video`.

- [ ] **Step 3:** `dotnet test tests/Api.Tests --filter "FullyQualifiedName~DeliveryApiMapperTests|FullyQualifiedName~ContractSerializationTests"` → fails to build (`Video` / 5-arg `HeroBlock` missing).
- [ ] **Step 4:** Implement the contract and `GetVideo` (pattern-match `[UmbracoMediaVideoMediaWithCropsModel { Url: { Length: > 0 } url } v, ..]`, extension from `v.Extension ?? Path.GetExtension(url)`, MIME switch); pass `GetVideo(p?.Video)` in the hero mapping; fix other `new HeroBlock(` call sites.
- [ ] **Step 5:** Same test command → pass; then `dotnet build AspireTest.slnx && dotnet test AspireTest.slnx` → all pass; `Api.json` contains `"Video"`.
- [ ] **Step 6:** Commit — "Map the hero video into the content contract".

### Task 3: Full-cover hero on the web

**Files:**
- Regenerate: `src/Web/src/lib/api/schema.d.ts`
- Modify: `src/Web/src/lib/api/client.ts` (export `type Video`), `src/Web/src/components/blocks/Hero.astro`, `src/Web/src/styles/global.css` (`body` `overflow-x: clip`)

**Interfaces:**
- Consumes: `Extract<Block, { type: 'hero' }>` with `video?: Video | null`; `imageAttributes` from `@/lib/image` for the poster URL (its `src`); `ResponsiveImage.astro`.

- [ ] **Step 1:** `npm run gen:api`; export `Video`.
- [ ] **Step 2:** Rewrite `Hero.astro` per the spec: `<section>` breakout `relative left-1/2 w-screen -translate-x-1/2` (or `mx-[calc(50%-50vw)]`), `first` → negative top margin equal to main's `py-10 sm:py-16`, min height `min-h-[60vh] lg:min-h-[70vh]`, background layers (image `absolute inset-0 h-full w-full object-cover`, focal point via `ResponsiveImage`; video same box with `motion-reduce:hidden`), overlay `bg-gradient-to-r from-black/70 via-black/40 to-transparent` when media, content `relative mx-auto max-w-6xl px-4 sm:px-6` with light text when media (`text-white`, subheading `text-white/85`), today's colours otherwise. Keep `first` → `h1`, eager image. Keep the `border-b`-free separation (the following block's spacing stays as today; add `mb-14 sm:mb-20`).
- [ ] **Step 3:** `npm run check` → 0/0. HTML of `/` contains no `<video` (demo has no video) and the hero `<img>` with `sizes="100vw"`.
- [ ] **Step 4:** Headless-Chrome screenshots of `/` and `/articles` at 1280×900 and 375×812 (`document.documentElement.scrollWidth === innerWidth` asserted), compared with the current main screenshots; read them for alignment and contrast.
- [ ] **Step 5:** Commit — "Turn the hero into a full-cover background".

### Task 4: Docs and the video path

**Files:**
- Modify: `README.md` (content model row, API section)

- [ ] **Step 1:** README: `heroBlock` row lists `video` (Video Media Picker, plays muted and looping behind the hero; image is poster and fallback); API section notes `HeroBlock.video` (`{ url, mimeType }` or null). Commit — "Document the hero video".
- [ ] **Step 2:** Generate a test clip outside the repo: `ffmpeg -f lavfi -i testsrc2=size=1280x720:rate=30 -t 6 -pix_fmt yuv420p -c:v libx264 -movflags +faststart <scratchpad>/hero-test.mp4`. Ask the user to upload it to the media library (Media > Create > Video), pick it on the Home hero and publish.
- [ ] **Step 3:** Headless Chrome on `/`: the hero `<video>` has `src`/`type` from the API, `poster` set, `paused === false` after load, `muted && loop`; with `Emulation.setEmulatedMedia` `prefers-reduced-motion: reduce` the video is `display: none` and the image visible; screenshot for the user. Afterwards the user may remove the test video from the hero.
