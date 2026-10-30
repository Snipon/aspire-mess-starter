/**
 * Marks [data-reveal] elements data-revealed as they enter the viewport, which plays their slide-up (global.css).
 * Elements already on screen are marked at once, so nothing visible at load disappears; the hidden starting state only
 * applies after this has run (data-reveal-ready on <html>). Off when the visitor prefers reduced motion.
 */
export function revealOnScroll() {
  if (!matchMedia('(prefers-reduced-motion: no-preference)').matches || !('IntersectionObserver' in window)) return;
  const observer = new IntersectionObserver(
    (entries) => {
      for (const entry of entries) {
        if (entry.isIntersecting) {
          entry.target.setAttribute('data-revealed', '');
          observer.unobserve(entry.target);
        }
      }
    },
    { rootMargin: '0px 0px -8% 0px' },
  );
  for (const element of document.querySelectorAll('[data-reveal]')) {
    const box = element.getBoundingClientRect();
    if (box.top < innerHeight && box.bottom > 0) element.setAttribute('data-revealed', '');
    else observer.observe(element);
  }
  document.documentElement.setAttribute('data-reveal-ready', '');
}
