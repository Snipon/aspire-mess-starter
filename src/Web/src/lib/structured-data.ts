import type { Article, BlogPost, Page, Product } from './api/client';

// schema.org JSON-LD for search engines. URLs are absolute: `origin` is siteUrl().

/** Script content for <script type="application/ld+json">; `<` is escaped so CMS text can never close the script. */
export const jsonLd = (data: unknown) => JSON.stringify(data).replace(/</g, '\\u003c');

export const productJsonLd = (product: Product, origin: string, imageUrl?: string) => ({
  '@context': 'https://schema.org',
  '@type': 'Product',
  name: product.title,
  sku: product.sku,
  description: product.summary ?? undefined,
  image: imageUrl,
  category: product.category,
  brand: { '@type': 'Brand', name: product.brand },
  offers: {
    '@type': 'Offer',
    url: origin + product.path,
    price: product.price.amount,
    priceCurrency: product.price.currency,
    availability: `https://schema.org/${product.stock > 0 ? 'InStock' : 'OutOfStock'}`,
  },
  aggregateRating: product.stats.reviewCount
    ? { '@type': 'AggregateRating', ratingValue: product.stats.rating, reviewCount: product.stats.reviewCount }
    : undefined,
});

export const articleJsonLd = (page: Page, article: Article | BlogPost, origin: string, imageUrl?: string) => ({
  '@context': 'https://schema.org',
  '@type': page.contentType === 'blogPost' ? 'BlogPosting' : 'Article',
  headline: page.title,
  description: page.summary ?? undefined,
  image: imageUrl,
  datePublished: article.publishDate,
  dateModified: page.updatedAt,
  url: origin + page.path,
  keywords: 'tags' in article && article.tags.length ? article.tags.join(', ') : undefined,
});

/** Trail of [name, path] pairs, outermost first. */
export const breadcrumbsJsonLd = (trail: [name: string, path: string][], origin: string) => ({
  '@context': 'https://schema.org',
  '@type': 'BreadcrumbList',
  itemListElement: trail.map(([name, path], i) => ({ '@type': 'ListItem', position: i + 1, name, item: origin + path })),
});
