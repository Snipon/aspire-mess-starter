import type { Link } from './api/client';

// publishDate is a calendar date ("2026-10-28"); format it in UTC so it never shifts by a day.
const dateFormat = new Intl.DateTimeFormat('en', { dateStyle: 'long', timeZone: 'UTC' });

export const formatDate = (date: string) => dateFormat.format(new Date(date));

const moneyFormats = new Map<string, Intl.NumberFormat>();

/** "$1,487.99"; one formatter per currency. */
export const formatMoney = (money: { amount: number; currency: string }) => {
  let format = moneyFormats.get(money.currency);
  if (!format) moneyFormats.set(money.currency, (format = new Intl.NumberFormat('en-US', { style: 'currency', currency: money.currency })));
  return format.format(money.amount);
};

export const formatNumber = (value: number) => value.toLocaleString('en-US');

export const tagHref = (tag: string) => `/tags/${encodeURIComponent(tag)}`;

/** Anchor attributes for a CMS link; new-window links get rel="noopener noreferrer". */
export const linkAttrs = (link: Link) => ({
  href: link.url,
  target: link.target ?? undefined,
  rel: link.target === '_blank' ? 'noopener noreferrer' : undefined,
});

/** Display labels for the CMS content type aliases the API returns (facet values and URLs keep the alias). */
const contentTypeLabels: Record<string, string> = { article: 'Article', blogPost: 'Blog post', site: 'Page', page: 'Page' };

export const contentTypeLabel = (type: string) => contentTypeLabels[type] ?? type;
