import type { APIRoute } from 'astro';
import { siteUrl } from '../lib/site';

// Per-visitor pages and endpoints; search results stay crawlable but are noindex.
export const GET: APIRoute = ({ url }) =>
  new Response(
    `User-agent: *
Disallow: /api/
Disallow: /auth/
Disallow: /preview/
Disallow: /cart
Disallow: /checkout
Disallow: /account

Sitemap: ${siteUrl(url)}/sitemap.xml
`,
    { headers: { 'Content-Type': 'text/plain; charset=utf-8', 'Cache-Control': 'public, max-age=3600' } },
  );
