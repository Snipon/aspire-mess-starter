import type { APIRoute } from 'astro';
import { getSitemap } from '../lib/api/client';
import { siteUrl } from '../lib/site';

const escape = (value: string) => value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

/** Every published page and product, plus the shop listing (not a CMS page). */
export const GET: APIRoute = async ({ url }) => {
  const { pages, products } = await getSitemap();
  const origin = siteUrl(url);
  const entries = [
    ...pages.map((page) => ({ path: page.path, updatedAt: page.updatedAt })),
    ...(products.length ? [{ path: '/shop', updatedAt: undefined }] : []),
    ...products.map((product) => ({ path: `/shop/${encodeURIComponent(product.slug)}`, updatedAt: product.updatedAt })),
  ];
  const xml = `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
${entries.map((e) => `  <url><loc>${escape(origin + e.path)}</loc>${e.updatedAt ? `<lastmod>${e.updatedAt}</lastmod>` : ''}</url>`).join('\n')}
</urlset>
`;
  return new Response(xml, { headers: { 'Content-Type': 'application/xml; charset=utf-8', 'Cache-Control': 'public, max-age=3600' } });
};
