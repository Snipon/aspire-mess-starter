import ArticleCard from '@/components/ArticleCard';
import type { ArticleSummary } from '@/lib/api/client';

interface Props {
  articles: ArticleSummary[];
  headingLevel?: 'h2' | 'h3';
}

export default function ArticleGrid({ articles, headingLevel }: Props) {
  return (
    <ul className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
      {articles.map((article) => (
        <li key={article.id} className="flex">
          <ArticleCard article={article} headingLevel={headingLevel} />
        </li>
      ))}
    </ul>
  );
}
