import { UserRoundIcon } from 'lucide-react';
import { useEffect, useState } from 'react';
import { buttonVariants } from '@/components/ui/button';
import { useVisitorJson } from '@/lib/use-visitor-json';
import { cn } from '@/lib/utils';

interface Me {
  name: string;
  email: string | null;
}

/**
 * "Sign in" for visitors, a link to /account for signed-in customers. Pages are shared-cached, so the signed-in state
 * is fetched in the browser (/api/me, no-store) and the server-rendered header never contains it.
 */
export default function AccountMenu({ active }: { active: boolean }) {
  // undefined while loading, null when signed out (or when the session could not be read).
  const me = useVisitorJson<Me>('/api/me');
  const [returnTo, setReturnTo] = useState('/');

  useEffect(() => setReturnTo(location.pathname + location.search), []);

  if (me === undefined) return <span className="size-9" aria-hidden="true" />;
  if (!me) {
    return (
      <a href={`/auth/login?returnTo=${encodeURIComponent(returnTo)}`} className={buttonVariants({ variant: 'ghost', size: 'sm' })}>
        Sign in
      </a>
    );
  }

  return (
    <a
      href="/account"
      title={me.email ?? me.name}
      aria-current={active ? 'page' : undefined}
      className={cn(buttonVariants({ variant: 'ghost', size: 'icon' }), active && 'bg-primary text-primary-foreground hover:bg-primary hover:text-primary-foreground')}
    >
      <UserRoundIcon aria-hidden="true" />
      <span className="sr-only">Account ({me.name})</span>
    </a>
  );
}
