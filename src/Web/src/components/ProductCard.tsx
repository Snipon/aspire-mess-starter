import ResponsiveImage from '@/components/ResponsiveImage';
import { Badge } from '@/components/ui/badge';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import type { ProductSummary } from '@/lib/api/client';
import { formatMoney, formatNumber } from '@/lib/format';

export default function ProductCard({ product }: { product: ProductSummary }) {
  return (
    <Card className="group relative w-full pt-0 transition-colors hover:border-ink focus-within:outline-2 focus-within:outline-offset-2 focus-within:outline-ring">
      <div className="aspect-[4/3] overflow-hidden bg-muted">
        {product.image && (
          <ResponsiveImage
            image={{ ...product.image, alt: null }}
            sizes="(min-width: 1024px) 240px, (min-width: 640px) 33vw, 50vw"
            className="h-full w-full object-cover"
          />
        )}
      </div>
      <CardHeader>
        <CardDescription className="font-narrow">
          <span className="font-semibold text-foreground">{product.brand}</span> {product.category}
        </CardDescription>
        <CardTitle className="text-base leading-snug font-bold text-balance">
          <h3>
            <a href={product.path} className="decoration-primary decoration-2 underline-offset-4 after:absolute after:inset-0 focus-visible:outline-none group-hover:underline">
              {product.title}
            </a>
          </h3>
        </CardTitle>
      </CardHeader>
      <CardContent className="mt-auto flex flex-wrap items-center justify-between gap-2">
        <span className="font-wide text-xl font-extrabold tabular-nums">{formatMoney(product.price)}</span>
        <span className="font-narrow text-sm text-muted-foreground">
          <span aria-hidden="true">★</span> {product.rating.toFixed(1)}
          <span className="sr-only"> out of 5</span> ({formatNumber(product.reviewCount)})
        </span>
        {!product.inStock && <Badge variant="secondary">Out of stock</Badge>}
      </CardContent>
    </Card>
  );
}
