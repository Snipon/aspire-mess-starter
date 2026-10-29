/** A same-site path to return to after signing in; anything else (absolute or protocol-relative URLs) becomes "/". */
export function safeReturnTo(value: string | null | undefined): string {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.includes('\\')) return '/';
  const target = new URL(value, 'http://site.invalid');
  // Check the normalized path too: dot segments ("/.//evil.com") collapse into a protocol-relative "//evil.com".
  if (target.origin !== 'http://site.invalid' || target.pathname.startsWith('//') || target.pathname.startsWith('/auth/')) return '/';
  return target.pathname + target.search + target.hash;
}
