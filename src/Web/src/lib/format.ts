import type { Link } from './api/client';

// publishDate is a calendar date ("2026-10-28"); format it in UTC so it never shifts by a day.
const dateFormat = new Intl.DateTimeFormat('en', { dateStyle: 'long', timeZone: 'UTC' });

export const formatDate = (date: string) => dateFormat.format(new Date(date));

export const tagHref = (tag: string) => `/tags/${encodeURIComponent(tag)}`;

/** Anchor attributes for a CMS link; new-window links get rel="noopener noreferrer". */
export const linkAttrs = (link: Link) => ({
  href: link.url,
  target: link.target ?? undefined,
  rel: link.target === '_blank' ? 'noopener noreferrer' : undefined,
});
