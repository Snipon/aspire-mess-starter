import { defineMiddleware } from 'astro:middleware';
import { log } from './lib/log';

/**
 * One log record per request (method, path, status, duration). A page that throws is logged by 500.astro, and its
 * error page passes through here again as "responded 500".
 */
export const onRequest = defineMiddleware(async ({ request, url }, next) => {
  const started = performance.now();
  const response = await next();
  const durationMs = Math.round(performance.now() - started);
  log.info(`${request.method} ${url.pathname} responded ${response.status} in ${durationMs} ms`, {
    'http.request.method': request.method,
    'url.path': url.pathname,
    'http.response.status_code': response.status,
    'duration.ms': durationMs,
  });
  return response;
});
