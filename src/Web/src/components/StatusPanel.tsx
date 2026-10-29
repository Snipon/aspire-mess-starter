import { CircleCheckIcon, CircleHelpIcon, CircleXIcon, TriangleAlertIcon, type LucideIcon } from 'lucide-react';
import { Alert, AlertTitle } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import type { HealthState } from '@/lib/api/client';
import { cn } from '@/lib/utils';

export type State = HealthState | 'unknown';

export interface Row {
  name: string;
  state: State;
  detail: string;
}

const labels: Record<State, string> = { healthy: 'Operational', degraded: 'Degraded', unhealthy: 'Down', unknown: 'Unknown' };
const headlines: Record<State, string> = {
  healthy: 'All systems operational',
  degraded: 'Some systems are degraded',
  unhealthy: 'Some systems are down',
  unknown: 'System status unknown',
};
const icons: Record<State, LucideIcon> = {
  healthy: CircleCheckIcon,
  degraded: TriangleAlertIcon,
  unhealthy: CircleXIcon,
  unknown: CircleHelpIcon,
};
// Status colours have no shadcn token; these sit on top of the Alert/Badge base styles.
const tones: Record<State, string> = {
  healthy: 'border-emerald-200 bg-emerald-50 text-emerald-900 dark:border-emerald-900 dark:bg-emerald-950 dark:text-emerald-100',
  degraded: 'border-amber-200 bg-amber-50 text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100',
  unhealthy: 'border-red-200 bg-red-50 text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100',
  unknown: 'border-border bg-muted text-foreground',
};

export default function StatusPanel({ overall, rows }: { overall: State; rows: Row[] }) {
  const OverallIcon = icons[overall];
  return (
    <>
      <Alert role="status" className={cn('mt-6', tones[overall])}>
        <OverallIcon aria-hidden="true" />
        <AlertTitle className="text-base">{headlines[overall]}</AlertTitle>
      </Alert>

      <Card className="mt-6 gap-0 py-0">
        <ul className="divide-y">
          {rows.map((row) => {
            const Icon = icons[row.state];
            return (
              <li key={row.name} className="flex items-center justify-between gap-4 px-5 py-4">
                <div className="min-w-0">
                  <p className="font-medium">{row.name}</p>
                  <p className="text-sm text-muted-foreground">{row.detail}</p>
                </div>
                <Badge variant="outline" className={cn('h-6 px-2.5 text-sm', tones[row.state])}>
                  <Icon aria-hidden="true" />
                  {labels[row.state]}
                </Badge>
              </li>
            );
          })}
        </ul>
      </Card>
    </>
  );
}
