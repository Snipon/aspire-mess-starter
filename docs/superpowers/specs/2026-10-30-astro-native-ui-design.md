# Astro-native UI: Starwind instead of shadcn/React

Date: 2026-10-30
Status: approved design, pending implementation plan

## Problem

The website (`src/Web`) uses shadcn/ui, which is React. Seven primitives live in `src/components/ui/*.tsx`, nine
server-rendered components are `.tsx`, and three per-visitor header pieces are hydrated React islands
(`StaffBar`, `AccountMenu`, `MiniCart` with `client:load`). That brings in `react`, `react-dom`, `@astrojs/react`,
`radix-ui`, `class-variance-authority`, `lucide-react` and `shadcn` for what is otherwise an Astro SSR site.

## Goal

No React and no shadcn in `src/Web`. Every component is an `.astro` file, the UI primitives come from
[Starwind UI](https://starwind.dev) (an Astro-native, shadcn-style component library), and the site looks and behaves
as it does today.

Success criteria:

- `git grep -E "react|lucide|radix|shadcn|class-variance-authority"` in `src/Web` finds nothing outside lock files
  and generated output; no `.tsx` or `.jsx` file remains; `@astrojs/react` is gone from `astro.config.mjs`.
- The built client bundle (`dist/client`) contains no React.
- Every page renders the same content with the same visual design (same tokens, fonts, spacing, square corners).
- Per-visitor behaviour is unchanged: the header's cart count, mini cart, account menu and staff bar are fetched in
  the browser from `/api/cart`, `/api/me` and `/api/staff` (`no-store`); pages still render nothing per visitor.
- `npm run check` and `npm test` pass (on Node 25 too) and CI stays green.

## Decisions

- **Library: Starwind UI.** Copy-in components (like shadcn) written in Astro with Tailwind 4 and
  `tailwind-variants`, vanilla JS for interactivity, and largely the same token names as shadcn. Rejected: daisyUI (its
  own theme system would change the look), hand-rolled components (no library).
- **Islands: Astro component + custom element.** Each per-visitor piece is an `.astro` component that renders a
  static shell; a small custom element defined in the component's `<script>` fetches its JSON and fills the shell.
  Rejected: server-rendering per visitor (every page would become private, and every render would call Drupal for the
  cart).
- **Icons: Tabler** (`@tabler/icons` SVG imports), the set Starwind's components already use, so the site has one
  icon set. The ~10 Lucide icons are replaced by their closest Tabler equivalents; shapes differ slightly.

## Design

### Dependencies

- Add (pinned to exact current versions): `starwind` (dev, CLI only), `tailwind-variants`, `@tabler/icons`.
- Remove: `react`, `react-dom`, `@types/react`, `@types/react-dom`, `@astrojs/react`, `radix-ui`,
  `class-variance-authority`, `lucide-react`, `shadcn`, and `components.json`.
- Keep: `cn` (`lib/utils.ts`), `tw-animate-css` and `astro-swiper` while something still uses them; remove
  `tw-animate-css` if nothing does after the migration.
- `astro.config.mjs` drops the `react()` integration.

### Starwind components

- Added with the Starwind CLI's `add` command (not `init`, which would rewrite `global.css` and config):
  `button`, `badge`, `card`, `alert`, `input`, `separator`, `pagination`. They live in `src/components/starwind/` and
  are project source from then on (edited for the site's look, like `components/ui` today).
- Add `starwind.config.json` if the CLI needs it to know the component directory and CSS file.
- Where a shadcn wrapper had a variant the site uses that Starwind lacks (for example a `buttonVariants`-style class
  helper for links styled as buttons), use Starwind's exported variants (`ButtonVariants` / `tv` helpers) rather than
  re-creating a shadcn API.

### Theme (`src/styles/global.css`)

- Keep every existing token and value.
- Replace `@import "shadcn/tailwind.css"` with whatever of it the site still needs, inlined (custom variants and base
  rules); nothing from the `shadcn` package remains.
- Add the tokens Starwind uses that the theme lacks, mapped onto the current palette in `:root` and `.dark` plus the
  `@theme inline` colour mappings: `--outline` (= `--ring`), `--error` / `--error-foreground` (= `--destructive` /
  its foreground), and `--info`, `--success`, `--warning` with foregrounds (new, muted to match the classic serif
  design).
- Adjust Starwind's default variants where they differ visibly from the current components (radius, focus ring,
  sizes), so the pages look the same.

### Static components

These `.tsx` files become `.astro` with the same props (types from `lib/api/client`) and markup, using Starwind
components and Tabler icons; callers keep their import names: `ArticleGrid`, `ArticleCard`, `TagList`,
`ResponsiveImage`, `ProductCard`, `SearchHitCard`, `Pager`, `FacetFilters`, `StatusPanel`. Existing `.astro` files that
import `@/components/ui/*` switch to the Starwind components. `src/components/ui/` is deleted.

### Per-visitor islands

`StaffBar.astro`, `AccountMenu.astro`, `MiniCart.astro`, each with:

- A static shell rendered by Astro: the parts that are the same for everyone (cart icon link, account icon link),
  plus `<template>` elements for the variable parts (staff bar contents, mini cart panel, cart line), styled with the
  same Tailwind classes and Starwind variants as today.
- A custom element (`<staff-bar>`, `<account-menu>`, `<mini-cart>`) defined in the component's `<script>` that loads
  its JSON with `loadVisitorJson` and fills the shell by cloning templates and setting `textContent`, `href` and
  attributes. Never `innerHTML` with API data (cart titles, names come from users).
- The same states as now: nothing until loaded; staff bar renders nothing for non-staff; account menu shows "Sign in"
  or the link to `/account`; mini cart shows the count badge (`99+` cap), an empty-cart message, up to `MAX_ITEMS`
  lines with "and N more", subtotal and the two buttons. The panel keeps its pure-CSS hover/focus-within behaviour and
  is not shown on `/cart`. `active` and the page's `data-cms-id` / `data-sku` come in as attributes.
- The staff bar's sign-out stays a plain `<form method="post">`.
- `ResponsiveImage` markup inside a cart line is reproduced in the template (`srcset`/`sizes` from the cart item's
  image), since an `.astro` component cannot render in the browser.

`src/lib/visitor-json.ts` replaces `use-visitor-json.ts`:
`loadVisitorJson<T>(url: string, onValue: (value: T | null) => void): void` fetches once (null on a non-OK answer or
a network error) and again on `pageshow` with `persisted` (back/forward cache restore).

### Tests and tooling

- `package.json` `test` script: `node --test "src/**/*.test.mjs"` (glob), which works on Node 22+ and 25; the
  current `node --test src/` fails on Node 25.
- New `src/lib/visitor-json.test.mjs`: value on 200, null on non-OK and on a network error, reload on a persisted
  `pageshow`, no reload on a non-persisted one (stubbed `fetch` and `window`).
- Existing `.test.mjs` files keep passing.

### Docs

README: the intro line and the "UI components" section describe Starwind (component directory, how to add one,
tokens, icons, custom-element islands). AGENTS.md: the `src/Web` row reads "Astro 7 SSR + Tailwind 4 + Starwind UI".

## Testing

- `npm run check` (0 errors, 0 warnings), `npm test`, `npm run build`; `grep -r react dist/client` finds nothing.
- On the running stack, compared with the current site: home, article list, an article, blog post, search with
  facets, shop list with filters and pager, a product page, cart, checkout, account, status, 404.
- Islands: add a product → count badge and mini cart update; empty cart message; more than `MAX_ITEMS` lines;
  account menu signed out and signed in; staff bar for a signed-in staff member with the right shortcuts on a CMS
  page and a product page; back/forward after changing the cart refreshes the count; with JavaScript off the cart and
  account links still work.

## Out of scope

- Visual redesign beyond the token aliases.
- New UI features or Starwind components the site does not use today.
- API or contract changes.
