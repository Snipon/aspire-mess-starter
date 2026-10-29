import createClient from 'openapi-fetch';
import type { components, paths } from './schema';
import { log } from '../log';

export type Page = components['schemas']['Page'];
export type Block = components['schemas']['Block'];
export type Article = components['schemas']['Article'];
export type ArticleSummary = components['schemas']['ArticleSummary'];
export type Link = components['schemas']['Link'];
export type TagCount = components['schemas']['TagCount'];
export type Image = components['schemas']['Image'];
export type NavItem = components['schemas']['NavItem'];
export type SearchResult = components['schemas']['SearchResult'];
export type SearchHit = components['schemas']['SearchHit'];
export type ProblemDetails = components['schemas']['ProblemDetails'];
export type StatusReport = components['schemas']['StatusReport'];
export type HealthState = components['schemas']['HealthState'];

/** Timeout for calls whose failure the page can survive (navigation, status). */
const API_TIMEOUT_MS = 3000;
/** Upper bound for every other call, so an API that accepts connections but never answers cannot hold pages open. */
const DEFAULT_TIMEOUT_MS = 10000;

let client: ReturnType<typeof createClient<paths>> | undefined;

/**
 * Typed client for the Content API. Server-side only: the base URL comes from the
 * API_URL environment variable (injected by Aspire / the container platform).
 */
export function api() {
  if (!client) {
    const baseUrl = process.env.API_URL;
    if (!baseUrl) throw new Error('API_URL environment variable is not set.');
    client = createClient<paths>({
      baseUrl: baseUrl.replace(/\/+$/, ''),
      // Keeps a shorter per-call signal (navigation, status) and adds the default bound.
      fetch: (request) =>
        fetch(request, { signal: AbortSignal.any([request.signal, AbortSignal.timeout(DEFAULT_TIMEOUT_MS)]) }),
    });
  }
  return client;
}

/** Navigation for the layout; a failing API must not take every page down with it. */
export async function getNavigation(): Promise<NavItem[]> {
  try {
    // Bounded so a hanging API cannot hold every page (including /status) open.
    const { data } = await api().GET('/api/navigation', { signal: AbortSignal.timeout(API_TIMEOUT_MS) });
    return data ?? [];
  } catch (error) {
    log.error('Failed to load navigation', error);
    return [];
  }
}

/** All tags with their article counts; used for optional filters, so failures degrade to an empty list. */
export async function getTags(): Promise<TagCount[]> {
  try {
    const { data } = await api().GET('/api/tags');
    return data ?? [];
  } catch (error) {
    log.error('Failed to load tags', error);
    return [];
  }
}

/**
 * The API's dependency report plus the measured round trip. `report` is undefined when the API is unreachable,
 * times out (`httpStatus` undefined) or answers with anything but 200; the status page then shows the API as down.
 */
export async function getStatus(): Promise<{ report?: StatusReport; httpStatus?: number; latencyMs: number }> {
  const started = performance.now();
  try {
    const { data, response } = await api().GET('/api/status', { signal: AbortSignal.timeout(API_TIMEOUT_MS) });
    const latencyMs = Math.round(performance.now() - started);
    if (!data) log.warn('Status API returned no report', undefined, { 'http.response.status_code': response.status });
    return { report: data, httpStatus: response.status, latencyMs };
  } catch (error) {
    log.warn('Status API unreachable', error);
    return { latencyMs: Math.round(performance.now() - started) };
  }
}
