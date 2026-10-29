import type { APIRoute } from 'astro';
import { authEnabled, signOut } from '../../lib/auth';

/** Ends the session here and at the identity provider (RP-initiated logout). POST only, so other sites cannot sign visitors out. */
export const POST: APIRoute = async ({ cookies, url }) => {
  if (!authEnabled()) return new Response(null, { status: 404, headers: { 'Cache-Control': 'no-store' } });
  const target = await signOut(cookies, url);
  return new Response(null, { status: 303, headers: { Location: target, 'Cache-Control': 'no-store' } });
};
