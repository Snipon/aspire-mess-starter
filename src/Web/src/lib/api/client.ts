import createClient from 'openapi-fetch';
import type { components, paths } from './schema';

export type Page = components['schemas']['Page'];
export type Image = components['schemas']['Image'];
export type NavItem = components['schemas']['NavItem'];
export type SearchResult = components['schemas']['SearchResult'];
export type SearchHit = components['schemas']['SearchHit'];
export type ProblemDetails = components['schemas']['ProblemDetails'];

let client: ReturnType<typeof createClient<paths>> | undefined;

/**
 * Typed client for the Content API. Server-side only: the base URL comes from the
 * API_URL environment variable (injected by Aspire / the container platform).
 */
export function api() {
  if (!client) {
    const baseUrl = process.env.API_URL;
    if (!baseUrl) throw new Error('API_URL environment variable is not set.');
    client = createClient<paths>({ baseUrl: baseUrl.replace(/\/+$/, '') });
  }
  return client;
}

/** Navigation for the layout; a failing API must not take every page down with it. */
export async function getNavigation(): Promise<NavItem[]> {
  try {
    const { data } = await api().GET('/api/navigation');
    return data ?? [];
  } catch (error) {
    console.error('Failed to load navigation', error);
    return [];
  }
}
