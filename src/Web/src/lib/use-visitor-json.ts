import { useEffect, useState } from 'react';

/**
 * Per-visitor JSON (cart, sign-in state, staff bar) fetched in the browser, because pages are shared-cached. Undefined
 * while loading, null when there is nothing (or it could not be loaded). Reloaded on back/forward navigation, which
 * restores the page as it was although the visitor may have changed the cart or signed in or out since. `url` may be a
 * function when it depends on the document, which exists only inside the effect.
 */
export function useVisitorJson<T>(url: string | (() => string)): T | null | undefined {
  const [value, setValue] = useState<T | null>();

  useEffect(() => {
    const href = typeof url === 'function' ? url() : url;
    const load = () =>
      fetch(href)
        .then((r) => (r.ok ? (r.json() as Promise<T | null>) : null))
        .catch(() => null)
        .then(setValue);
    const onPageShow = (e: PageTransitionEvent) => e.persisted && load();
    load();
    window.addEventListener('pageshow', onPageShow);
    return () => window.removeEventListener('pageshow', onPageShow);
    // Once per page view: a function url is a new value on every render.
  }, []);

  return value;
}
