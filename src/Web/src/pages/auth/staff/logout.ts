import type { APIRoute } from 'astro';
import { staffEnabled, staffSignOut } from '../../../lib/staff';

/** Ends the staff session here and at the identity provider. POST only, so other sites cannot sign staff out. */
export const POST: APIRoute = async ({ cookies, url }) => {
  if (!staffEnabled()) return new Response(null, { status: 404, headers: { 'Cache-Control': 'no-store' } });
  const target = await staffSignOut(cookies, url);
  return new Response(null, { status: 303, headers: { Location: target, 'Cache-Control': 'no-store' } });
};
