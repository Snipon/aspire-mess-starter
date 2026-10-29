import { contentTypeLabel } from '@/components/content-types';
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
    <Card className="group relative flex-row gap-5 px-5 transition-colors hover:border-ink focus-within:outline-2 focus-within:outline-offset-2 focus-within:outline-ring">
      <div className="min-w-0 flex-1 space-y-2">
        <CardHeader className="px-0">
          <CardTitle className="flex items-baseline justify-between gap-4 text-lg leading-snug font-bold">
            <h2>
              <a href={hit.path} className="decoration-primary decoration-2 underline-offset-4 after:absolute after:inset-0 focus-visible:outline-none group-hover:underline">
                {hit.title}
              </a>
            </h2>
            <Badge variant="outline" className="shrink-0">
              {contentTypeLabel(hit.contentType)}
            </Badge>
          </CardTitle>
          <CardDescription className="font-narrow flex flex-wrap gap-x-3">
            <span className="text-primary">{hit.path}</span>
            {hit.publishDate && <time dateTime={hit.publishDate}>{formatDate(hit.publishDate)}</time>}
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
          className="chamfer hidden size-24 shrink-0 self-center bg-muted object-cover [--cut:0.75rem] sm:block"
        />
      )}
    </Card>
  );
}
