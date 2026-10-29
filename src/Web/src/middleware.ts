import { defineMiddleware } from 'astro:middleware';
import { log } from './lib/log';

/**
 * Shared-cache policy for successful pages that set none themselves (/status opts out with no-store): the reverse
 * proxy in front of the web (web-cache in apphost.cs) serves them for up to a minute, then revalidates in the
 * background, and keeps serving the last good copy for a day while the web is down. Browsers always revalidate.
 */
const PAGE_CACHE_CONTROL = 'public, max-age=0, s-maxage=60, stale-while-revalidate=300, stale-if-error=86400';

/**
 * One log record per request (method, path, status, duration). A page that throws is logged by 500.astro, and its
 * error page passes through here again as "responded 500".
 */
export const onRequest = defineMiddleware(async ({ request, url }, next) => {
  const started = performance.now();
  const response = await next();
  if ((request.method === 'GET' || request.method === 'HEAD') && response.status === 200 && !response.headers.has('Cache-Control')) {
    response.headers.set('Cache-Control', PAGE_CACHE_CONTROL);
  }
  const durationMs = Math.round(performance.now() - started);
  log.info(`${request.method} ${url.pathname} responded ${response.status} in ${durationMs} ms`, {
    'http.request.method': request.method,
    'url.path': url.pathname,
    'http.response.status_code': response.status,
    'duration.ms': durationMs,
  });
  return response;
});
