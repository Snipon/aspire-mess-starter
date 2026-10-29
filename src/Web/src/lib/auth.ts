import { createCipheriv, createDecipheriv, hkdfSync, randomBytes } from 'node:crypto';
import { deflateRawSync, inflateRawSync } from 'node:zlib';
import type { AstroCookies } from 'astro';
import * as oidc from 'openid-client';
import { log } from './log';

/**
 * Customer sign-in: OpenID Connect authorization code flow with PKCE against the shop's identity provider (a Keycloak
 * realm locally, Entra External ID in production), run by the server as a confidential client. The session lives in
 * an encrypted cookie; pages stay shared-cached because nothing per visitor is rendered into them (the header asks
 * /api/me in the browser). Off unless OIDC_AUTHORITY is set.
 *
 * OIDC_AUTHORITY  issuer URL (discovery at <issuer>/.well-known/openid-configuration)
 * OIDC_CLIENT_ID, OIDC_CLIENT_SECRET
 * OIDC_SCOPE      default "openid profile email"; add what the provider needs for refresh tokens and API access tokens
 * SESSION_SECRET  encrypts the session cookie (AES-256-GCM, key derived with HKDF)
 * SITE_URL        public origin for the redirect URIs (the reverse proxy), e.g. http://localhost:8082
 */
const SESSION_COOKIE = 'session';
const FLOW_COOKIE = 'auth_flow';
const SESSION_MAX_AGE = 60 * 60 * 24 * 30;
const FLOW_MAX_AGE = 60 * 10;
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

interface Flow {
  state: string;
  nonce: string;
  verifier: string;
  returnTo: string;
}

export const authEnabled = () => Boolean(process.env.OIDC_AUTHORITY);

function setting(name: string): string {
  const value = process.env[name];
  if (!value) throw new Error(`${name} environment variable is not set (needed because OIDC_AUTHORITY is).`);
  return value;
}

const siteUrl = (url: URL) => (process.env.SITE_URL || url.origin).replace(/\/+$/, '');
const redirectUri = (url: URL) => `${siteUrl(url)}/auth/callback`;
const cookieOptions = (url: URL, path: string, maxAge: number) => ({
  path,
  maxAge,
  httpOnly: true,
  sameSite: 'lax' as const,
  secure: siteUrl(url).startsWith('https:'),
});

let configuration: Promise<oidc.Configuration> | undefined;

/** Discovery document and JWKS of the provider, fetched once (again after a failure). */
function provider(): Promise<oidc.Configuration> {
  configuration ??= oidc
    .discovery(new URL(setting('OIDC_AUTHORITY')), setting('OIDC_CLIENT_ID'), setting('OIDC_CLIENT_SECRET'), undefined, { timeout: 10 })
    .then((config) => {
      // Also verify the ID token's signature against the provider's JWKS (iss, aud, exp and nonce are always checked).
      oidc.enableNonRepudiationChecks(config);
      return config;
    })
    .catch((error) => {
      configuration = undefined;
      throw error;
    });
  return configuration;
}

let key: Buffer | undefined;
const sessionKey = () => (key ??= Buffer.from(hkdfSync('sha256', setting('SESSION_SECRET'), '', 'web-session-cookie', 32)));

/**
 * Encrypts and authenticates a cookie value (AES-256-GCM; the cookie name is the additional data, so one cookie's value
 * cannot be replayed as another's). Deflated first, which keeps a session with its tokens well under 4 KB.
 */
function seal(name: string, data: unknown): string {
  const iv = randomBytes(12);
  const cipher = createCipheriv('aes-256-gcm', sessionKey(), iv).setAAD(Buffer.from(name));
  const body = Buffer.concat([cipher.update(deflateRawSync(JSON.stringify(data))), cipher.final()]);
  return Buffer.concat([iv, cipher.getAuthTag(), body]).toString('base64url');
}

/** The sealed value, or undefined when it is missing, tampered with or sealed with another secret. */
function unseal<T>(name: string, value: string | undefined): T | undefined {
  if (!value) return undefined;
  try {
    const raw = Buffer.from(value, 'base64url');
    const decipher = createDecipheriv('aes-256-gcm', sessionKey(), raw.subarray(0, 12)).setAAD(Buffer.from(name));
    decipher.setAuthTag(raw.subarray(12, 28));
    return JSON.parse(inflateRawSync(Buffer.concat([decipher.update(raw.subarray(28)), decipher.final()])).toString('utf8')) as T;
  } catch {
    return undefined;
  }
}

/** A same-site path to return to after signing in; anything else (absolute or protocol-relative URLs) becomes "/". */
export function safeReturnTo(value: string | null | undefined): string {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.includes('\\')) return '/';
  const target = new URL(value, 'http://site.invalid');
  // Check the normalized path too: dot segments ("/.//evil.com") collapse into a protocol-relative "//evil.com".
  if (target.origin !== 'http://site.invalid' || target.pathname.startsWith('//') || target.pathname.startsWith('/auth/')) return '/';
  return target.pathname + target.search + target.hash;
}

/** Starts the code flow: remembers state, nonce and PKCE verifier in a short-lived cookie, returns the provider URL. */
export async function beginSignIn(cookies: AstroCookies, url: URL, returnTo: string): Promise<URL> {
  const config = await provider();
  const flow: Flow = {
    state: oidc.randomState(),
    nonce: oidc.randomNonce(),
    verifier: oidc.randomPKCECodeVerifier(),
    returnTo: safeReturnTo(returnTo),
  };
  cookies.set(FLOW_COOKIE, seal(FLOW_COOKIE, flow), cookieOptions(url, '/auth', FLOW_MAX_AGE));
  return oidc.buildAuthorizationUrl(config, {
    redirect_uri: redirectUri(url),
    scope: process.env.OIDC_SCOPE || 'openid profile email',
    code_challenge: await oidc.calculatePKCECodeChallenge(flow.verifier),
    code_challenge_method: 'S256',
    state: flow.state,
    nonce: flow.nonce,
  });
}

/**
 * Completes the code flow on /auth/callback: exchanges the code (state and PKCE checked), validates the ID token
 * (signature, iss, aud, exp, nonce) and starts the session. Returns where to go next; throws when sign-in failed.
 */
export async function completeSignIn(cookies: AstroCookies, url: URL): Promise<string> {
  const flow = unseal<Flow>(FLOW_COOKIE, cookies.get(FLOW_COOKIE)?.value);
  cookies.delete(FLOW_COOKIE, { path: '/auth' });
  if (!flow) throw new Error('No sign-in in progress (the flow cookie is missing or expired).');

  const callback = new URL(redirectUri(url));
  callback.search = url.search;
  const tokens = await oidc.authorizationCodeGrant(await provider(), callback, {
    pkceCodeVerifier: flow.verifier,
    expectedState: flow.state,
    expectedNonce: flow.nonce,
    idTokenExpected: true,
  });
  const claims = tokens.claims()!;
  const text = (value: unknown) => (typeof value === 'string' && value ? value : undefined);
  writeSession(cookies, url, {
    sub: claims.sub,
    email: text(claims.email),
    name: text(claims.name),
    givenName: text(claims.given_name),
    familyName: text(claims.family_name),
    accessToken: tokens.access_token,
    refreshToken: tokens.refresh_token,
    expiresAt: Math.floor(Date.now() / 1000) + (tokens.expires_in ?? 300),
  });
  return flow.returnTo;
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
  if (!session.refreshToken) {
    endSession(cookies);
    return undefined;
  }

  try {
    const tokens = await oidc.refreshTokenGrant(await provider(), session.refreshToken);
    writeSession(cookies, url, {
      ...session,
      accessToken: tokens.access_token,
      refreshToken: tokens.refresh_token ?? session.refreshToken,
      expiresAt: Math.floor(Date.now() / 1000) + (tokens.expires_in ?? 300),
    });
    return tokens.access_token;
  } catch (error) {
    // invalid_grant and the like: the provider's session is over. Anything else (provider down) is an error.
    if (!(error instanceof oidc.ResponseBodyError)) throw error;
    log.info(`Refresh token rejected (${error.error}); signing the customer out`);
    endSession(cookies);
    return undefined;
  }
}

/**
 * Ends the local session and returns the provider's end-session URL (RP-initiated logout), which comes back to the
 * home page; just the home page when the provider has no such endpoint or cannot be reached.
 */
export async function signOut(cookies: AstroCookies, url: URL): Promise<string> {
  endSession(cookies);
  const home = `${siteUrl(url)}/`;
  try {
    const config = await provider();
    if (!config.serverMetadata().end_session_endpoint) return home;
    return oidc.buildEndSessionUrl(config, { client_id: config.clientMetadata().client_id, post_logout_redirect_uri: home }).href;
  } catch (error) {
    log.warn('Identity provider unreachable on sign-out', error);
    return home;
  }
}
