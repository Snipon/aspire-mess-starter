import type { APIRoute } from 'astro';
import { getSession } from '../../lib/auth';

/**
 * The signed-in customer's name and email (or null) for the header's account menu (AccountMenu.tsx). Pages are
 * shared-cached, so the header fetches this per visitor in the browser, like the mini cart.
 */
export const GET: APIRoute = ({ cookies }) => {
  const session = getSession(cookies);
  const me = session ? { name: session.name ?? session.email ?? 'Account', email: session.email ?? null } : null;
  return Response.json(me, { headers: { 'Cache-Control': 'no-store' } });
};
