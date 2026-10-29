import type { AstroCookies } from 'astro';
import { OidcClient, textClaim } from './oidc';
import { seal, unseal } from './sealed-cookie';
import { cookieOptions } from './site';
import { STAFF_ROLES, type StaffRole } from './staff-shortcuts';

/**
 * Staff sign-in on the website (STAFF_OIDC_AUTHORITY, _CLIENT_ID, _CLIENT_SECRET; the staff realm locally, Entra ID in
 * production), for the top bar with shortcuts into the CMS and the other back offices. The session keeps only who it
 * is and the app roles from the ID token's "roles" claim; it holds no tokens and ends after a working day, so role
 * changes at the provider apply by the next sign-in. Off unless STAFF_OIDC_AUTHORITY is set.
 */
const staff = new OidcClient('STAFF_OIDC', '/auth/staff/callback');
const SESSION_COOKIE = 'staff_session';
const SESSION_MAX_AGE = 60 * 60 * 8;

export interface StaffSession {
  sub: string;
  name: string;
  roles: StaffRole[];
}

export const staffEnabled = () => staff.enabled;

export const beginStaffSignIn = (cookies: AstroCookies, url: URL, returnTo: string | null) => staff.beginSignIn(cookies, url, returnTo);

/**
 * Completes staff sign-in on /auth/staff/callback. Only users with a known role get a session (anyone else in the
 * directory has no shortcuts to see). Returns where to go next; throws when sign-in failed or was refused.
 */
export async function completeStaffSignIn(cookies: AstroCookies, url: URL): Promise<string> {
  const { claims, returnTo } = await staff.completeSignIn(cookies, url);
  const claimed = Array.isArray(claims.roles) ? claims.roles : [];
  const roles = STAFF_ROLES.filter((role) => claimed.includes(role));
  if (roles.length === 0) throw new Error(`Staff sign-in refused for ${claims.sub}: no ${STAFF_ROLES.join(' or ')} role`);
  const session: StaffSession = { sub: claims.sub, name: textClaim(claims.name) ?? textClaim(claims.email) ?? 'Staff', roles };
  cookies.set(SESSION_COOKIE, seal(SESSION_COOKIE, session), cookieOptions(url, '/', SESSION_MAX_AGE));
  return returnTo;
}

export function getStaffSession(cookies: AstroCookies): StaffSession | undefined {
  return staffEnabled() ? unseal<StaffSession>(SESSION_COOKIE, cookies.get(SESSION_COOKIE)?.value) : undefined;
}

/** Ends the session; returns where to send the browser (the provider's logout, which comes back home). */
export async function staffSignOut(cookies: AstroCookies, url: URL): Promise<string> {
  cookies.delete(SESSION_COOKIE, { path: '/' });
  return staff.signOutUrl(url);
}
