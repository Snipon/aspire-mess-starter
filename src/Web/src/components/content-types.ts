/** Display labels for the CMS content type aliases the API returns (facet values and URLs keep the alias). */
const labels: Record<string, string> = { article: 'Article', blogPost: 'Blog post', site: 'Page', page: 'Page' };

export const contentTypeLabel = (type: string) => labels[type] ?? type;
