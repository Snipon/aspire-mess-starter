# Astro-native UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace shadcn/ui and React in `src/Web` with Starwind UI and `.astro` components, with the same look and behaviour.

**Architecture:** Starwind components are copied into `src/components/starwind/` and their variant class strings are
replaced by the site's current ones (from `src/components/ui/*.tsx`), so markup changes but the rendered look does
not. The nine server-only `.tsx` components become `.astro`. The three per-visitor islands become `.astro` shells
plus custom elements that fill `<template>`s from `/api/staff|me|cart` via a small `loadVisitorJson` helper. React,
shadcn and their dependencies are removed last.

**Tech Stack:** Astro 7 (SSR, node adapter), Tailwind 4, Starwind UI (`starwind@3.4.1` CLI, `tailwind-variants`, `@tabler/icons`), `node:test`.

**Spec:** `docs/superpowers/specs/2026-10-30-astro-native-ui-design.md`

## Global Constraints

- All commands run in `src/Web` unless noted. Pin new dependencies to exact versions (`npm install -E`).
- Starwind components are added with `npx starwind@3.4.1 add <names>`, never `init`; if `add` needs a config, write
  `starwind.config.json` by hand (component dir `src/components/starwind`, CSS `src/styles/global.css`). If the CLI
  touches `global.css`, `astro.config.mjs` or `package.json` beyond the dependencies it installs, revert those edits.
- Visual parity: class strings come from the current `src/components/ui/*.tsx` verbatim; `className` becomes `class`.
- Islands never use `innerHTML` with API data; text goes through `textContent`, URLs through `href`/`src`/`srcset`.
- Pages render nothing per visitor; `/api/cart`, `/api/me`, `/api/staff` stay the only per-visitor sources.
- Lucide → Tabler (`@tabler/icons/outline/<name>.svg`, imported as an Astro component): `Check`→`check`,
  `CircleCheck`/`CheckCircle2`→`circle-check`, `CircleHelp`→`help-circle`, `CircleX`→`circle-x`,
  `TriangleAlert`→`alert-triangle`, `ShoppingCart`→`shopping-cart`, `UserRound`→`user`, `X`→`x`. Keep
  `aria-hidden="true"` and the current size classes.
- Types exported from `.tsx` components move to `src/lib`: `FacetGroup` → `src/lib/facets.ts`; `Row`, `State` →
  `src/lib/status.ts`.
- Match surrounding style (comment density, 2-space TS, single quotes in TS). No new dependency beyond those named.

## Review Focus

1. A cart item title or customer name containing `<img src=x onerror=alert(1)>` → shown as literal text in the mini cart / account menu, never parsed. Pinned in Task 4 Step 4 (`git grep -n -E "innerHTML|insertAdjacentHTML|outerHTML" src` → nothing) and the final review.
2. `/api/cart` returns 500 or the network fails → the header shows the plain cart link with no count and no panel, no console error. Pinned in Task 1 (`loadVisitorJson` → null) and Task 4 (null handling).
3. Back/forward to a page after adding to the cart → count refreshes (bfcache `pageshow` with `persisted`). Pinned in Task 1 unit test and Task 6 manual check.
4. JavaScript disabled → cart and account links still navigate; no empty broken panel is visible. Pinned in Task 6.
5. A cart with more than `MAX_ITEMS` lines, and a count over 99 → "and N more" and `99+`. Pinned in Task 4 steps and Task 6.

---

### Task 1: Test script, `loadVisitorJson`, HTML baseline

**Files:**
- Modify: `src/Web/package.json` (`test` script)
- Create: `src/Web/src/lib/visitor-json.ts`
- Test: `src/Web/src/lib/visitor-json.test.mjs`

**Interfaces:**
- Produces: `export function loadVisitorJson<T>(url: string, onValue: (value: T | null) => void): void` — fetches
  `url`; calls `onValue(json)` on 2xx, `onValue(null)` on non-2xx or a rejected fetch; registers a `pageshow` listener
  that refetches only when `event.persisted`.

- [ ] **Step 1: Fix the test script** — `"test": "node --test \"src/**/*.test.mjs\""`. Run `npm test` → existing 10 tests pass on Node 25.

- [ ] **Step 2: Capture the HTML baseline** (stack running on the current code, `http://localhost:8082`): save the HTML of `/`, `/articles`, one article, one blog post, `/search?q=error`, `/shop`, one product page, `/cart`, `/status`, `/tags/dotnet`, `/does-not-exist` to the plan workspace (`.superpowers/sdd/2026-10-30-astro-native-ui/baseline/`). Write a small compare script there (not in the repo): for two HTML files, compare (a) visible text with whitespace collapsed and (b) the sorted set of class tokens; print differences. It is the regression check for Tasks 3–5.

- [ ] **Step 3: Write the failing tests** (look at how existing `.test.mjs` files import `.ts`; follow the same mechanism)

```js
test('passes the JSON of a 2xx answer', ...)            // fetch stub → {ok:true, json:()=>({itemCount:2})}; onValue gets {itemCount:2}
test('passes null for a non-2xx answer', ...)           // {ok:false} → null
test('passes null when the request fails', ...)         // fetch rejects → null, no throw
test('reloads on a back/forward cache restore', ...)    // dispatch pageshow {persisted:true} → fetch called twice, onValue twice
test('does not reload on a normal pageshow', ...)       // pageshow {persisted:false} → fetch called once
```

Stub `globalThis.fetch` and `globalThis.window` (an `EventTarget`); restore after each test.

- [ ] **Step 4: Run** `npm test` → the five new tests fail (module missing).

- [ ] **Step 5: Implement `loadVisitorJson`** in `src/lib/visitor-json.ts` (doc comment from `use-visitor-json.ts`, adapted).

- [ ] **Step 6: Run** `npm test` → 15 pass.

- [ ] **Step 7: Commit** — `git add src/Web/package.json src/Web/src/lib/visitor-json.*` — "Add loadVisitorJson for framework-free islands".

### Task 2: Starwind components with the site's styles

**Files:**
- Create: `src/Web/src/components/starwind/{button,badge,card,alert,input,separator,pagination}/…` (CLI)
- Create (if needed): `src/Web/starwind.config.json`
- Modify: `src/Web/package.json`, `package-lock.json` (`starwind` dev, `tailwind-variants`, `@tabler/icons`)
- Modify: `src/Web/src/styles/global.css` (token aliases)

**Interfaces:**
- Produces: `@/components/starwind/<name>` exports as generated (`Button`, `ButtonVariants`, `Badge`, `BadgeVariants`, `Card`, `CardHeader`, `CardTitle`, `CardDescription`, `CardContent`, `CardFooter`, `Alert`, `AlertTitle`, `AlertDescription`, `Input`, `Separator`, `Pagination`, `PaginationContent`, `PaginationItem`, `PaginationLink`, `PaginationPrevious`, `PaginationNext`, `PaginationEllipsis`). `ButtonVariants({ variant, size, class })` and `BadgeVariants(...)` return class strings, replacing `buttonVariants`/`badgeVariants`.
- Variants available after this task, exactly the current shadcn ones: button `variant` default | signal | outline | secondary | ghost | destructive | link, `size` default | xs | sm | lg | icon | icon-xs | icon-sm | icon-lg; badge default | secondary | destructive | outline | ghost | link; alert default | destructive. Pagination link keeps `isActive`.

- [ ] **Step 1:** `npm install -E -D starwind@3.4.1`, then `npx starwind add button badge card alert input separator pagination` (see Global Constraints about config/init). `npm install -E tailwind-variants @tabler/icons` if the CLI did not.
- [ ] **Step 2:** In each component's `variants.ts` and `.astro` files, replace Starwind's class strings, variant names, sizes and defaults with those of the matching `src/components/ui/*.tsx` (including `data-slot`/`data-variant` attributes where the site's CSS or classes rely on them, e.g. `in-data-[slot=…]`, `group/button`).
- [ ] **Step 3:** `global.css`: in `:root`/`.dark` add `--outline: var(--ring)`, `--error: var(--destructive)`, `--error-foreground` and `--info`, `--success`, `--warning` (+ `-foreground`) in the existing muted palette; map them in `@theme inline` (`--color-outline`, `--color-error`, …). Only needed if a kept Starwind class still references them after Step 2.
- [ ] **Step 4:** `npm run check` → 0 errors, 0 warnings (nothing imports the new components yet).
- [ ] **Step 5: Commit** — "Add Starwind components styled like the current UI".

### Task 3: Static components and pages on Starwind

**Files:**
- Create: `src/Web/src/components/{ArticleGrid,ArticleCard,TagList,ResponsiveImage,ProductCard,SearchHitCard,Pager,FacetFilters,StatusPanel}.astro`, `src/Web/src/lib/facets.ts`, `src/Web/src/lib/status.ts`
- Delete: the nine matching `.tsx` files
- Modify: every `.astro` file importing `@/components/ui/*` (except islands, Task 4) or `lucide-react`: `ArticleView`, `SearchInput`, `SignInFailed`, `NotFound`, `blocks/{Hero,Cta,LatestArticles,ArticleList}`, `pages/{search,account,cart,checkout,status,shop/index,shop/[slug],tags/[tag]}`

**Interfaces:**
- Consumes: Task 2 exports; `ResponsiveImage.astro` props `{ image: Image; sizes: string; class?: string; loading?: 'lazy' | 'eager' }` (same as the `.tsx` props, `className` → `class`).
- Produces: `.astro` components with the props of their `.tsx` predecessors; `FacetGroup` from `@/lib/facets`, `Row`/`State` from `@/lib/status`.

- [ ] **Step 1:** Convert the nine components (JSX → Astro template: `className`→`class`, `.map` keeps working, `key` dropped, React-only attributes like `inputMode`/`autoComplete` → `inputmode`/`autocomplete`).
- [ ] **Step 2:** Switch every listed `.astro` file to `@/components/starwind/*`, `ButtonVariants`/`BadgeVariants`, the `.astro` components and Tabler icons.
- [ ] **Step 3:** `npm run check` → 0 errors, 0 warnings; `git grep -n -E "components/ui/|lucide-react" src` → only `AccountMenu.tsx`, `MiniCart.tsx`, `StaffBar.tsx`, `src/components/ui/`.
- [ ] **Step 4:** Compare the baseline pages (Task 1 Step 2 script) against the dev server → no text differences; class-token differences only from removed React/shadcn internals (e.g. `data-slot` values) and are listed in the commit message.
- [ ] **Step 5: Commit** — "Render static components with Astro and Starwind".

### Task 4: Per-visitor islands as custom elements

**Files:**
- Create: `src/Web/src/components/{StaffBar,AccountMenu,MiniCart}.astro`
- Delete: `src/Web/src/components/{StaffBar,AccountMenu,MiniCart}.tsx`, `src/Web/src/lib/use-visitor-json.ts`
- Modify: `src/Web/src/layouts/Layout.astro` (drop `client:load`; pass `active` as before)

**Interfaces:**
- Consumes: `loadVisitorJson` (Task 1); `ButtonVariants` (Task 2); `/api/staff?cmsId=&sku=` (from `document.body.dataset`), `/api/me`, `/api/cart` shapes as today (`Cart` from `lib/api/client`).
- Produces: custom elements `<staff-bar>`, `<account-menu active>`, `<mini-cart active>` (boolean attribute `active` when on `/account` / `/cart`).

- [ ] **Step 1: `AccountMenu.astro`** — shell: nothing visible until loaded (as now). Templates for the signed-out "Sign in" link and the signed-in account link (current classes, Tabler `user` icon). Script: `customElements.define('account-menu', …)` with `connectedCallback` → `loadVisitorJson('/api/me', …)`; null → sign-in template, value → account template with the name set via `textContent`.
- [ ] **Step 2: `MiniCart.astro`** — shell: the cart link with icon and `sr-only` "Cart" rendered by Astro (works without JS), an empty count badge element, and the panel container (hidden until loaded, not rendered when `active`). Templates: empty message, cart line (image `img` with `srcset`/`sizes="48px"` built from the item's image as `ResponsiveImage` does, title link, `quantity × unitPrice`), "and N more", subtotal + buttons. Script fills count (`99+` cap), `sr-only` text (`Cart, N item(s)`), lines (`MAX_ITEMS` as today) and subtotal using `formatMoney` from `@/lib/format`.
- [ ] **Step 3: `StaffBar.astro`** — template for the bar (name, shortcuts list item, sign-out form). Script builds the `/api/staff` query from `document.body.dataset` exactly as now; null → renders nothing.
- [ ] **Step 4:** `Layout.astro` uses the three `.astro` components; `npm run check` → 0 errors; `git grep -n -E "from 'react'|use-visitor-json|client:load|innerHTML|insertAdjacentHTML|outerHTML" src` → nothing.
- [ ] **Step 5:** On the dev server: signed out → "Sign in" shown, cart link without count; add a product → count and panel lines; cart with 0 items → empty message; `/cart` → no panel; baseline compare of `/` and `/shop` → no text differences.
- [ ] **Step 6: Commit** — "Turn the per-visitor islands into custom elements".

### Task 5: Remove React and shadcn; docs

**Files:**
- Delete: `src/Web/src/components/ui/`, `src/Web/components.json`
- Modify: `src/Web/astro.config.mjs` (drop `react()`), `src/Web/package.json` + lock (uninstall `react react-dom @types/react @types/react-dom @astrojs/react radix-ui class-variance-authority lucide-react shadcn`, and `tw-animate-css` if unused), `src/Web/tsconfig.json` (drop React JSX settings if present), `src/Web/src/styles/global.css` (inline what is still needed from `shadcn/tailwind.css`, drop the import), `README.md` (intro + "UI components" section), `AGENTS.md` (`src/Web` row)

- [ ] **Step 1:** Read `node_modules/shadcn/dist/tailwind.css` (or the package's `tailwind.css`) before uninstalling; copy the custom variants / base rules the site's classes use into `global.css`.
- [ ] **Step 2:** Uninstall, edit config, delete files.
- [ ] **Step 3: Verify** — `npm run check` (0/0), `npm test`, `npm run build`; `grep -rl -i react dist/client` → nothing; `git grep -n -i -E "react|lucide|radix|shadcn|class-variance" -- src package.json astro.config.mjs tsconfig.json` → nothing; `git ls-files 'src/**/*.tsx'` → nothing.
- [ ] **Step 4:** Baseline compare of all pages → no text differences, class differences as in Task 3.
- [ ] **Step 5:** README: intro "Astro 7 + Tailwind 4 + Starwind UI"; replace "UI components (shadcn/ui)" with "UI components (Starwind UI)": components in `src/Web/src/components/starwind` (add with `npx starwind@3.4.1 add <name>` in `src/Web`, then restyle with the site's tokens), Tabler icons, per-visitor islands as custom elements with `loadVisitorJson`. AGENTS.md `src/Web` row: "Astro 7 SSR + Tailwind 4 + Starwind UI".
- [ ] **Step 6: Commit** — "Remove React and shadcn from the web".

### Task 6: End-to-end check on the running stack

- [ ] **Step 1:** The web hot-reloads; restart only if `npm install` changes are not picked up (`aspire stop && aspire start`).
- [ ] **Step 2:** Pages from the spec's Testing list render; baseline compare clean.
- [ ] **Step 3:** Islands: add product → badge + panel; more than `MAX_ITEMS` lines → "and N more"; back/forward after a cart change refreshes the count; JS disabled (browser tool) → cart/account links work, no visible empty panel; force `/api/cart` failure is not possible from outside, so check null handling by code review.
- [ ] **Step 4 (needs the user):** signed-in customer account menu; staff bar shortcuts on a CMS page and a product page.
