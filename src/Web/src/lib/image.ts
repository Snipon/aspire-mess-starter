import type { Image } from './api/client';

/**
 * The <img> attributes for a CMS or shop image: every signed rendition in srcset, the largest as the fallback src
 * (rather than the original upload), and the focal point as object-position. Used by ResponsiveImage.astro on the
 * server and by the mini cart in the browser.
 */
export function imageAttributes(image: Image, sizes: string) {
  const srcset = image.srcset.length ? image.srcset.map((r) => `${r.url} ${r.width}w`).join(', ') : undefined;
  return {
    src: image.srcset.at(-1)?.url ?? image.url,
    srcset,
    sizes: srcset ? sizes : undefined,
    width: image.width ?? undefined,
    height: image.height ?? undefined,
    alt: image.alt ?? '',
    objectPosition: image.focalPoint ? `${image.focalPoint.left * 100}% ${image.focalPoint.top * 100}%` : undefined,
  };
}

/** The ~1200px rendition for social cards and structured data; image URLs from the API are already absolute. */
export const cardImageUrl = (image: Image) => (image.srcset.find((r) => r.width >= 1200) ?? image.srcset.at(-1) ?? image).url;
