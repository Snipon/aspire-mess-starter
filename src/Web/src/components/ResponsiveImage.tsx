import type { Image } from '@/lib/api/client';

interface Props {
  image: Image;
  sizes?: string;
  className?: string;
  loading?: 'eager' | 'lazy';
}

export default function ResponsiveImage({ image, sizes = '(min-width: 1024px) 1024px, 100vw', className, loading = 'lazy' }: Props) {
  const srcset = image.srcset.length ? image.srcset.map((r) => `${r.url} ${r.width}w`).join(', ') : undefined;
  // Prefer the largest signed rendition over the original upload as the fallback src.
  const src = image.srcset.at(-1)?.url ?? image.url;
  const objectPosition = image.focalPoint ? `${image.focalPoint.left * 100}% ${image.focalPoint.top * 100}%` : undefined;
  return (
    <img
      src={src}
      srcSet={srcset}
      sizes={srcset ? sizes : undefined}
      width={image.width ?? undefined}
      height={image.height ?? undefined}
      alt={image.alt ?? ''}
      loading={loading}
      decoding="async"
      className={className}
      style={objectPosition ? { objectPosition } : undefined}
    />
  );
}
