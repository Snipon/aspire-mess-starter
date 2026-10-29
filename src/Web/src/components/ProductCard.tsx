import ResponsiveImage from '@/components/ResponsiveImage';
import { Badge } from '@/components/ui/badge';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import type { ProductSummary } from '@/lib/api/client';
import { formatMoney, formatNumber } from '@/lib/format';

export default function ProductCard({ product }: { product: ProductSummary }) {
  return (
    <Card className="group relative w-full pt-0 transition hover:-translate-y-0.5 hover:shadow-md focus-within:ring-2 focus-within:ring-ring motion-reduce:transition-none motion-reduce:hover:translate-y-0">
      <div className="aspect-[4/3] overflow-hidden bg-muted">
        {product.image && (
          <ResponsiveImage
            image={{ ...product.image, alt: null }}
            sizes="(min-width: 1024px) 240px, (min-width: 640px) 33vw, 50vw"
            className="h-full w-full object-cover transition duration-300 group-hover:scale-[1.03] motion-reduce:transition-none"
          />
        )}
      </div>
      <CardHeader>
        <CardDescription>
          {product.brand} · {product.category}
        </CardDescription>
        <CardTitle className="text-base font-semibold text-balance">
          <h3>
            <a href={product.path} className="after:absolute after:inset-0 focus-visible:outline-none group-hover:text-primary">
              {product.title}
            </a>
          </h3>
        </CardTitle>
      </CardHeader>
      <CardContent className="mt-auto flex flex-wrap items-center justify-between gap-2">
        <span className="text-lg font-semibold tabular-nums">{formatMoney(product.price)}</span>
        <span className="text-sm text-muted-foreground">
          <span aria-hidden="true">★</span> {product.rating.toFixed(1)}
          <span className="sr-only"> out of 5</span> ({formatNumber(product.reviewCount)})
        </span>
        {!product.inStock && <Badge variant="secondary">Out of stock</Badge>}
      </CardContent>
    </Card>
  );
}
