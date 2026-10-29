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
// Status colours have no theme token: one strong hue per state for the edge bar and icon, on flat panel surfaces.
// The icon hues are !important to beat the Alert's svg text-current.
const bars: Record<State, string> = {
  healthy: 'border-l-emerald-600',
  degraded: 'border-l-amber-500',
  unhealthy: 'border-l-destructive',
  unknown: 'border-l-input',
};
const hues: Record<State, string> = {
  healthy: 'text-emerald-700!',
  degraded: 'text-amber-700!',
  unhealthy: 'text-destructive!',
  unknown: 'text-muted-foreground!',
};

export default function StatusPanel({ overall, rows }: { overall: State; rows: Row[] }) {
  const OverallIcon = icons[overall];
  return (
    <>
      <Alert role="status" className={cn('mt-8 border-l-8 px-5 py-5 *:[svg]:translate-y-1', bars[overall])}>
        <OverallIcon aria-hidden="true" className={cn('size-6!', hues[overall])} />
        <AlertTitle className="text-xl font-medium sm:text-2xl">{headlines[overall]}</AlertTitle>
      </Alert>

      <Card className="mt-6 gap-0 py-0">
        <ul className="divide-y">
          {rows.map((row) => {
            const Icon = icons[row.state];
            return (
              <li key={row.name} className="flex items-center justify-between gap-4 px-5 py-4">
                <div className="min-w-0">
                  <p className="font-semibold">{row.name}</p>
                  <p className="text-sm text-muted-foreground">{row.detail}</p>
                </div>
                <Badge variant="secondary" className="h-7 gap-1.5 px-2.5 text-sm">
                  <Icon aria-hidden="true" className={cn('size-4!', hues[row.state])} />
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
