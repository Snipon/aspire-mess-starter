import type { APIRoute } from 'astro';
import { beginStaffSignIn, staffEnabled } from '../../../lib/staff';

/** Sends staff to the identity provider's sign-in; ?returnTo= is a same-site path. */
export const GET: APIRoute = async ({ cookies, url }) => {
  if (!staffEnabled()) return new Response(null, { status: 404, headers: { 'Cache-Control': 'no-store' } });
  const target = await beginStaffSignIn(cookies, url, url.searchParams.get('returnTo'));
  return new Response(null, { status: 302, headers: { Location: target.href, 'Cache-Control': 'no-store' } });
};
