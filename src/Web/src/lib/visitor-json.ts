/**
 * Per-visitor JSON (cart, sign-in state, staff bar) loaded in the browser by the header's custom elements, because
 * pages render nothing per visitor. `onValue` gets the JSON, or null when there is nothing (or it could not be
 * loaded). Loaded again on back/forward navigation, which restores the page as it was although the visitor may have
 * changed the cart or signed in or out since.
 */
export function loadVisitorJson<T>(url: string, onValue: (value: T | null) => void): void {
  const load = () =>
    fetch(url)
      .then((r) => (r.ok ? (r.json() as Promise<T | null>) : null))
      .catch(() => null)
      .then(onValue);
  load();
  window.addEventListener('pageshow', (e) => (e as PageTransitionEvent).persisted && load());
}
