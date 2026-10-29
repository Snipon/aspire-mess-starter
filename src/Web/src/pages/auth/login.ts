import type { APIRoute } from 'astro';
import { authEnabled, beginSignIn } from '../../lib/auth';

/** Sends the visitor to the identity provider's sign-in (and registration) page; ?returnTo= is a same-site path. */
export const GET: APIRoute = async ({ cookies, url }) => {
  if (!authEnabled()) return new Response(null, { status: 404, headers: { 'Cache-Control': 'no-store' } });
  const target = await beginSignIn(cookies, url, url.searchParams.get('returnTo') ?? '/');
  return new Response(null, { status: 302, headers: { Location: target.href, 'Cache-Control': 'no-store' } });
};
