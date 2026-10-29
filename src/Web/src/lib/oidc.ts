import type { AstroCookies } from 'astro';
import * as oidc from 'openid-client';
import { safeReturnTo } from './return-to';
import { seal, unseal } from './sealed-cookie';
import { log } from './log';

const FLOW_COOKIE_PATH = '/auth';
const FLOW_MAX_AGE = 60 * 10;

interface Flow {
  state: string;
  nonce: string;
  verifier: string;
  returnTo: string;
}

/** What a completed sign-in yields: the validated ID token claims, the tokens and where to go next. */
export interface SignIn {
  claims: oidc.IDToken;
  tokens: oidc.TokenEndpointResponse;
  returnTo: string;
}

/** Public origin for redirect URIs (the reverse proxy in front of the web), falling back to the request's. */
export const siteUrl = (url: URL) => (process.env.SITE_URL || url.origin).replace(/\/+$/, '');

export const cookieOptions = (url: URL, path: string, maxAge: number) => ({
  path,
  maxAge,
  httpOnly: true,
  sameSite: 'lax' as const,
  secure: siteUrl(url).startsWith('https:'),
});

/**
 * One OpenID Connect provider, used as a confidential client with the authorization code flow and PKCE. Configured
 * from <envPrefix>_AUTHORITY (issuer; discovery at <issuer>/.well-known/openid-configuration), _CLIENT_ID,
 * _CLIENT_SECRET and optional _SCOPE; off while _AUTHORITY is unset. Knows the protocol only: what a sign-in means
 * (sessions, roles) is up to the caller.
 */
export class OidcClient {
  private configuration: Promise<oidc.Configuration> | undefined;
  private readonly flowCookie: string;

  constructor(
    private readonly envPrefix: string,
    /** Route that receives the authorization code, e.g. /auth/callback. Must be registered at the provider. */
    private readonly callbackPath: string,
  ) {
    this.flowCookie = `${envPrefix.toLowerCase()}_flow`;
  }

  get enabled(): boolean {
    return Boolean(process.env[`${this.envPrefix}_AUTHORITY`]);
  }

  private setting(name: string): string {
    const value = process.env[`${this.envPrefix}_${name}`];
    if (!value) throw new Error(`${this.envPrefix}_${name} environment variable is not set (needed because ${this.envPrefix}_AUTHORITY is).`);
    return value;
  }

  private redirectUri(url: URL): string {
    return `${siteUrl(url)}${this.callbackPath}`;
  }

  /** Discovery document and JWKS of the provider, fetched once (again after a failure). */
  private provider(): Promise<oidc.Configuration> {
    this.configuration ??= oidc
      .discovery(new URL(this.setting('AUTHORITY')), this.setting('CLIENT_ID'), this.setting('CLIENT_SECRET'), undefined, { timeout: 10 })
      .then((config) => {
        // Also verify the ID token's signature against the provider's JWKS (iss, aud, exp and nonce are always checked).
        oidc.enableNonRepudiationChecks(config);
        return config;
      })
      .catch((error) => {
        this.configuration = undefined;
        throw error;
      });
    return this.configuration;
  }

  /** Starts the code flow: remembers state, nonce and PKCE verifier in a short-lived cookie, returns the provider URL. */
  async beginSignIn(cookies: AstroCookies, url: URL, returnTo: string | null | undefined): Promise<URL> {
    const config = await this.provider();
    const flow: Flow = {
      state: oidc.randomState(),
      nonce: oidc.randomNonce(),
      verifier: oidc.randomPKCECodeVerifier(),
      returnTo: safeReturnTo(returnTo),
    };
    cookies.set(this.flowCookie, seal(this.flowCookie, flow), cookieOptions(url, FLOW_COOKIE_PATH, FLOW_MAX_AGE));
    return oidc.buildAuthorizationUrl(config, {
      redirect_uri: this.redirectUri(url),
      scope: process.env[`${this.envPrefix}_SCOPE`] || 'openid profile email',
      code_challenge: await oidc.calculatePKCECodeChallenge(flow.verifier),
      code_challenge_method: 'S256',
      state: flow.state,
      nonce: flow.nonce,
    });
  }

  /**
   * Completes the code flow on the callback route: exchanges the code (state and PKCE checked) and validates the ID
   * token (signature, iss, aud, exp, nonce). Throws when sign-in failed (cancelled, expired, replayed, invalid).
   */
  async completeSignIn(cookies: AstroCookies, url: URL): Promise<SignIn> {
    const flow = unseal<Flow>(this.flowCookie, cookies.get(this.flowCookie)?.value);
    cookies.delete(this.flowCookie, { path: FLOW_COOKIE_PATH });
    if (!flow) throw new Error('No sign-in in progress (the flow cookie is missing or expired).');

    const callback = new URL(this.redirectUri(url));
    callback.search = url.search;
    const tokens = await oidc.authorizationCodeGrant(await this.provider(), callback, {
      pkceCodeVerifier: flow.verifier,
      expectedState: flow.state,
      expectedNonce: flow.nonce,
      idTokenExpected: true,
    });
    return { claims: tokens.claims()!, tokens, returnTo: flow.returnTo };
  }

  /** New tokens for a refresh token; undefined when the provider no longer accepts it (its session is over). */
  async refresh(refreshToken: string): Promise<oidc.TokenEndpointResponse | undefined> {
    try {
      return await oidc.refreshTokenGrant(await this.provider(), refreshToken);
    } catch (error) {
      // invalid_grant and the like. Anything else (provider down) is an error.
      if (!(error instanceof oidc.ResponseBodyError)) throw error;
      log.info(`Refresh token rejected by the ${this.envPrefix} provider (${error.error})`);
      return undefined;
    }
  }

  /**
   * The provider's end-session URL (RP-initiated logout) coming back to the home page; just the home page when the
   * provider has no such endpoint or cannot be reached. The caller ends its own session.
   */
  async signOutUrl(url: URL): Promise<string> {
    const home = `${siteUrl(url)}/`;
    try {
      const config = await this.provider();
      if (!config.serverMetadata().end_session_endpoint) return home;
      return oidc.buildEndSessionUrl(config, { client_id: config.clientMetadata().client_id, post_logout_redirect_uri: home }).href;
    } catch (error) {
      log.warn(`The ${this.envPrefix} identity provider is unreachable on sign-out`, error);
      return home;
    }
  }
}

/** Optional string claim. */
export const textClaim = (value: unknown) => (typeof value === 'string' && value ? value : undefined);

/** Seconds since the epoch at which a token response's access token expires. */
export const expiresAt = (tokens: oidc.TokenEndpointResponse) => Math.floor(Date.now() / 1000) + (tokens.expires_in ?? 300);
