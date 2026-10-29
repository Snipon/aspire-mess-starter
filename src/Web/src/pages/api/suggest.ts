import type { APIRoute } from 'astro';
import { api } from '../../lib/api/client';
import { log } from '../../lib/log';

export type Suggestion = { title: string; path: string; contentType: string };

const LIMIT = 6;
const MAX_QUERY_LENGTH = 100;

/** Autosuggest for the search inputs (SearchInput.astro): the top search hits. Failures degrade to no suggestions. */
export const GET: APIRoute = async ({ url }) => {
  const q = (url.searchParams.get('q') ?? '').trim().slice(0, MAX_QUERY_LENGTH);
  if (q.length < 2) return Response.json([]);

  let suggestions: Suggestion[] = [];
  try {
    const { data } = await api().GET('/api/search', {
      params: { query: { q, pageSize: LIMIT } },
      signal: AbortSignal.timeout(3000),
    });
    suggestions = (data?.hits ?? []).map(({ title, path, contentType }) => ({ title, path, contentType }));
  } catch (error) {
    log.warn('Search suggestions failed', error);
  }

  return Response.json(suggestions, { headers: { 'Cache-Control': 'public, max-age=60' } });
};
