import ResponsiveImage from '@/components/ResponsiveImage';
import TagList from '@/components/TagList';
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card';
import type { ArticleSummary } from '@/lib/api/client';
import { formatDate } from '@/lib/format';

interface Props {
  article: ArticleSummary;
  headingLevel?: 'h2' | 'h3';
}

export default function ArticleCard({ article, headingLevel: Heading = 'h3' }: Props) {
  return (
    <Card className="group relative w-full pt-0 transition hover:-translate-y-0.5 hover:shadow-md focus-within:ring-2 focus-within:ring-ring motion-reduce:transition-none motion-reduce:hover:translate-y-0">
      <div className="aspect-[16/10] overflow-hidden bg-muted">
        {article.image && (
          <ResponsiveImage
            image={{ ...article.image, alt: null }}
            sizes="(min-width: 1024px) 320px, (min-width: 640px) 50vw, 100vw"
            className="h-full w-full object-cover transition duration-300 group-hover:scale-[1.03] motion-reduce:transition-none"
          />
        )}
      </div>
      <CardHeader>
        <CardDescription>
          <time dateTime={article.publishDate}>{formatDate(article.publishDate)}</time>
        </CardDescription>
        <CardTitle className="text-lg font-semibold text-balance">
          <Heading>
            <a href={article.path} className="after:absolute after:inset-0 focus-visible:outline-none group-hover:text-primary">
              {article.title}
            </a>
          </Heading>
        </CardTitle>
      </CardHeader>
      {article.summary && (
        <CardContent className="flex-1">
          <p className="line-clamp-3 text-base text-muted-foreground">{article.summary}</p>
        </CardContent>
      )}
      {article.tags.length > 0 && (
        <CardFooter className="mt-auto border-t-0 bg-transparent">
          <TagList tags={article.tags} />
        </CardFooter>
      )}
    </Card>
  );
}
