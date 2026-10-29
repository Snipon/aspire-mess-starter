import { CheckIcon } from 'lucide-react';
import { buttonVariants } from '@/components/ui/button';
import type { FacetCount } from '@/lib/api/client';
import { cn } from '@/lib/utils';

export interface FacetGroup {
  label: string;
  values: FacetCount[];
  selected?: string;
  /** Link that selects `value`, or clears the group's filter when `value` is null. */
  href: (value: string | null) => string;
}

interface Props {
  groups: FacetGroup[];
  className?: string;
}

const same = (a?: string, b?: string) => a?.toLowerCase() === b?.toLowerCase();

/** Facet links: a sidebar from lg up, horizontally scrolling chips below. */
export default function FacetFilters({ groups, className }: Props) {
  return (
    <aside className={cn('shrink-0 lg:w-56', className)} aria-label="Filters">
      {groups
        .filter((group) => group.values.length > 0)
        .map((group) => (
          <section key={group.label} className="mb-4 lg:mb-8 lg:border-t lg:border-ink lg:pt-3">
            <h2 className="text-sm font-bold lg:px-3">{group.label}</h2>
            <ul className="-mx-1 mt-2 flex gap-1 overflow-x-auto px-1 pb-1 lg:mx-0 lg:block lg:space-y-0.5 lg:px-0">
              {group.values.map((facet) => {
                const selected = same(facet.value, group.selected);
                return (
                  <li key={facet.value}>
                    <a
                      href={group.href(selected ? null : facet.value)}
                      aria-current={selected ? 'true' : undefined}
                      className={cn(
                        buttonVariants({ variant: selected ? 'default' : 'ghost', size: 'sm' }),
                        'h-9 w-full justify-between gap-3 px-3 text-sm font-medium [font-stretch:100%]',
                        // Chips below lg need an edge; the selected value is filled signal, like the active tab.
                        selected ? 'bg-primary text-primary-foreground' : 'border-border lg:border-transparent',
                      )}
                    >
                      <span className="flex items-center gap-1.5 truncate">
                        {selected && <CheckIcon aria-hidden="true" />}
                        {facet.value}
                      </span>
                      <span className={cn('font-narrow tabular-nums', selected ? 'text-primary-foreground/80' : 'text-muted-foreground')}>
                        {facet.count}
                      </span>
                    </a>
                  </li>
                );
              })}
            </ul>
          </section>
        ))}
    </aside>
  );
}
