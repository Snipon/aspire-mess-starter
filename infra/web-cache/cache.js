// njs handlers for nginx.conf.template: a cache generation that is part of the cache key, and the purge endpoint
// that bumps it. The generation starts at the start-up time, so a restarted nginx never reuses keys of an older run.
import crypto from 'crypto';

const KEY = 'generation';

function generation() {
  const dict = ngx.shared.cache;
  if (!dict.has(KEY)) dict.add(KEY, Date.now()); // add is a no-op when another worker got there first
  return String(dict.get(KEY));
}

// Hashing both sides first makes the comparison time independent of where they differ.
const digest = (value) => crypto.createHash('sha256').update(value).digest('base64');

/** POST /_purge with X-Purge-Secret: every cached page becomes a miss. 204 on success. */
function purge(r) {
  if (r.method !== 'POST') return r.return(405);
  const expected = process.env.CACHE_PURGE_SECRET;
  if (!expected || digest(r.headersIn['X-Purge-Secret'] || '') !== digest(expected)) return r.return(401);

  ngx.shared.cache.incr(KEY, 1, Date.now());
  r.return(204);
}

export default { generation, purge };
