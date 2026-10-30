/** Public origin for redirect URIs (SITE_URL), falling back to the request's. */
export const siteUrl = (url: URL) => (process.env.SITE_URL || url.origin).replace(/\/+$/, '');

/** Options for the web's own cookies: httpOnly, and Secure whenever the public site is served over https. */
export const cookieOptions = (url: URL, path: string, maxAge: number) => ({
  path,
  maxAge,
  httpOnly: true,
  sameSite: 'lax' as const,
  secure: siteUrl(url).startsWith('https:'),
});
