import type { AstroCookies } from 'astro';
import { cookieOptions, expiresAt, OidcClient, textClaim } from './oidc';
import { seal, unseal } from './sealed-cookie';
import { log } from './log';

/**
 * Customer sign-in at the shop's identity provider (a Keycloak realm locally, Entra External ID in production), via
 * OIDC_AUTHORITY, OIDC_CLIENT_ID, OIDC_CLIENT_SECRET and OIDC_SCOPE (see OidcClient). The session lives in an encrypted
 * cookie and carries the access token for the API; pages stay shared-cached because nothing per visitor is rendered
 * into them (the header asks /api/me in the browser). Off unless OIDC_AUTHORITY is set.
 */
const customers = new OidcClient('OIDC', '/auth/callback');
const SESSION_COOKIE = 'session';
const SESSION_MAX_AGE = 60 * 60 * 24 * 30;
/** Refresh this long before the access token expires, so it does not expire on the way to the API. */
const EXPIRY_MARGIN_S = 30;

export interface Session {
  sub: string;
  email?: string;
  name?: string;
  givenName?: string;
  familyName?: string;
  accessToken: string;
  refreshToken?: string;
  /** Access token expiry, seconds since the epoch. */
  expiresAt: number;
}

export const authEnabled = () => customers.enabled;

export const beginSignIn = (cookies: AstroCookies, url: URL, returnTo: string | null) => customers.beginSignIn(cookies, url, returnTo);

/** Completes sign-in on /auth/callback and starts the session. Returns where to go next; throws when sign-in failed. */
export async function completeSignIn(cookies: AstroCookies, url: URL): Promise<string> {
  const { claims, tokens, returnTo } = await customers.completeSignIn(cookies, url);
  writeSession(cookies, url, {
    sub: claims.sub,
    email: textClaim(claims.email),
    name: textClaim(claims.name),
    givenName: textClaim(claims.given_name),
    familyName: textClaim(claims.family_name),
    accessToken: tokens.access_token,
    refreshToken: tokens.refresh_token,
    expiresAt: expiresAt(tokens),
  });
  return returnTo;
}

function writeSession(cookies: AstroCookies, url: URL, session: Session) {
  const value = seal(SESSION_COOKIE, session);
  if (value.length > 4000) log.warn(`Session cookie is ${value.length} bytes; browsers may drop it`);
  cookies.set(SESSION_COOKIE, value, cookieOptions(url, '/', SESSION_MAX_AGE));
}

/** The signed-in customer's session (its access token may have expired; see accessToken). */
export function getSession(cookies: AstroCookies): Session | undefined {
  return authEnabled() ? unseal<Session>(SESSION_COOKIE, cookies.get(SESSION_COOKIE)?.value) : undefined;
}

export function endSession(cookies: AstroCookies) {
  cookies.delete(SESSION_COOKIE, { path: '/' });
}

/**
 * A valid access token for the API, refreshed when it has expired. Undefined (and the session ended) when there is
 * no session or the provider no longer accepts its refresh token; the customer has to sign in again.
 */
export async function accessToken(cookies: AstroCookies, url: URL): Promise<string | undefined> {
  const session = getSession(cookies);
  if (!session) return undefined;
  if (session.expiresAt - EXPIRY_MARGIN_S > Date.now() / 1000) return session.accessToken;

  const tokens = session.refreshToken ? await customers.refresh(session.refreshToken) : undefined;
  if (!tokens) {
    endSession(cookies);
    return undefined;
  }
  writeSession(cookies, url, {
    ...session,
    accessToken: tokens.access_token,
    refreshToken: tokens.refresh_token ?? session.refreshToken,
    expiresAt: expiresAt(tokens),
  });
  return tokens.access_token;
}

/** Ends the session; returns where to send the browser (the provider's logout, which comes back home). */
export async function signOut(cookies: AstroCookies, url: URL): Promise<string> {
  endSession(cookies);
  return customers.signOutUrl(url);
}
