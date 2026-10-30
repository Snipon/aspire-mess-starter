import type { HealthState } from './api/client';

/** A component's state on the status page; unknown when the API could not report it. */
export type State = HealthState | 'unknown';

/** One row of StatusPanel.astro. */
export interface Row {
  name: string;
  state: State;
  detail: string;
}
