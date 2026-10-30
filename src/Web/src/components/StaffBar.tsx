import type { Shortcut } from '@/lib/staff-shortcuts';
import { useVisitorJson } from '@/lib/use-visitor-json';

interface Bar {
  name: string;
  shortcuts: Shortcut[];
}

/**
 * Top bar for signed-in staff: shortcuts into the CMS and the other back offices, chosen by role for the page being
 * viewed (its CMS id or product SKU, from the <body> data attributes). Pages render nothing per visitor, so the bar is
 * fetched (/api/staff, no-store) and renders nothing for everyone else.
 */
export default function StaffBar() {
  const bar = useVisitorJson<Bar>(() => {
    const { cmsId, sku } = document.body.dataset;
    const query = new URLSearchParams(Object.entries({ cmsId, sku }).filter((e): e is [string, string] => Boolean(e[1])));
    return `/api/staff?${query}`;
  });

  if (!bar) return null;

  return (
    <nav aria-label="Staff" className="bg-primary text-primary-foreground">
      <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-x-5 gap-y-1 px-4 py-1.5 text-sm sm:px-6">
        <span className="font-semibold">{bar.name}</span>
        <ul className="flex flex-wrap gap-x-4 gap-y-1">
          {bar.shortcuts.map((s) => (
            <li key={s.href}>
              <a
                href={s.href}
                className="underline decoration-primary-foreground/40 underline-offset-4 hover:decoration-foreground-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary-foreground"
              >
                {s.label}
              </a>
            </li>
          ))}
        </ul>
        <form method="post" action="/auth/staff/logout" className="ml-auto">
          <button
            type="submit"
            className="font-semibold hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary-foreground"
          >
            Sign out
          </button>
        </form>
      </div>
    </nav>
  );
}
