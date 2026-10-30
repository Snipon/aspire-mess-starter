import type { FacetCount } from './api/client';

/** One filter group in FacetFilters.astro. */
export interface FacetGroup {
  label: string;
  values: FacetCount[];
  selected?: string;
  /** Display text for a value; the value itself stays in the link. */
  format?: (value: string) => string;
  /** Link that selects `value`, or clears the group's filter when `value` is null. */
  href: (value: string | null) => string;
}
