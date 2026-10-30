import type { APIRoute } from 'astro';
import { getStaffSession } from '../../lib/staff';
import { backOfficesFromEnv, shortcutsFor } from '../../lib/staff-shortcuts';

/**
 * The staff top bar (StaffBar.tsx) for the page being viewed (?cmsId= or ?sku=): who is signed in and their
 * shortcuts, or null for everyone else. Pages render nothing per visitor, so the bar is
 * fetched in the browser.
 */
export const GET: APIRoute = ({ cookies, url }) => {
  const session = getStaffSession(cookies);
  const bar = session && {
    name: session.name,
    shortcuts: shortcutsFor(
      session.roles,
      { cmsId: url.searchParams.get('cmsId') ?? undefined, sku: url.searchParams.get('sku') ?? undefined },
      backOfficesFromEnv(),
    ),
  };
  return Response.json(bar ?? null, { headers: { 'Cache-Control': 'no-store' } });
};
