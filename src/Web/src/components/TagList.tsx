import { badgeVariants } from '@/components/ui/badge';
import { tagHref } from '@/lib/format';
import { cn } from '@/lib/utils';

interface Props {
  tags: string[];
  /** Where each tag links to; defaults to the tag's article listing. */
  href?: (tag: string) => string;
  className?: string;
}

export default function TagList({ tags, href = tagHref, className }: Props) {
  if (tags.length === 0) return null;
  return (
    <ul className={cn('flex flex-wrap gap-1.5', className)} aria-label="Tags">
      {tags.map((tag) => (
        <li key={tag}>
          {/* z-10: stays clickable above a card's stretched link. */}
          <a href={href(tag)} className={cn(badgeVariants({ variant: 'secondary' }), 'relative z-10')}>
            {tag}
          </a>
        </li>
      ))}
    </ul>
  );
}
