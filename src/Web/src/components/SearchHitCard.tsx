import TagList from '@/components/TagList';
import { Badge } from '@/components/ui/badge';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import type { SearchHit } from '@/lib/api/client';
import { formatDate } from '@/lib/format';

interface Props {
  hit: SearchHit;
  tagHref: (tag: string) => string;
}

export default function SearchHitCard({ hit, tagHref }: Props) {
  return (
    <Card className="relative flex-row gap-5 px-5 transition hover:shadow-md focus-within:ring-2 focus-within:ring-ring">
      <div className="min-w-0 flex-1 space-y-2">
        <CardHeader className="px-0">
          <CardTitle className="flex items-baseline justify-between gap-4 text-lg font-semibold">
            <h2>
              <a href={hit.path} className="after:absolute after:inset-0 focus-visible:outline-none">
                {hit.title}
              </a>
            </h2>
            <Badge variant="outline" className="shrink-0">
              {hit.contentType}
            </Badge>
          </CardTitle>
          <CardDescription>
            <span className="text-primary">{hit.path}</span>
            {hit.publishDate && (
              <>
                <span aria-hidden="true"> · </span>
                <time dateTime={hit.publishDate}>{formatDate(hit.publishDate)}</time>
              </>
            )}
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-3 px-0 text-base text-muted-foreground">
          {hit.highlight ? (
            // The API HTML-escapes the text and only adds <mark> around matches.
            <p dangerouslySetInnerHTML={{ __html: hit.highlight }} />
          ) : (
            hit.summary && <p>{hit.summary}</p>
          )}
          <TagList tags={hit.tags} href={tagHref} />
        </CardContent>
      </div>
      {hit.imageUrl && (
        <img
          src={hit.imageUrl}
          alt=""
          width={96}
          height={96}
          loading="lazy"
          decoding="async"
          className="hidden size-24 shrink-0 self-center rounded-lg bg-muted object-cover sm:block"
        />
      )}
    </Card>
  );
}
