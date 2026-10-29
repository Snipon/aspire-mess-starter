import { UserRoundIcon } from 'lucide-react';
import { useEffect, useState } from 'react';
import { buttonVariants } from '@/components/ui/button';

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
  const [me, setMe] = useState<Me | null>();
  const [returnTo, setReturnTo] = useState('/');

  useEffect(() => {
    const load = () =>
      fetch('/api/me')
        .then((r) => (r.ok ? (r.json() as Promise<Me | null>) : null))
        .catch(() => null)
        .then(setMe);
    // Back/forward navigation restores the page as it was; the visitor may have signed in or out since.
    const onPageShow = (e: PageTransitionEvent) => e.persisted && load();
    setReturnTo(location.pathname + location.search);
    load();
    window.addEventListener('pageshow', onPageShow);
    return () => window.removeEventListener('pageshow', onPageShow);
  }, []);

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
      className={buttonVariants({ variant: active ? 'secondary' : 'ghost', size: 'icon' })}
    >
      <UserRoundIcon aria-hidden="true" />
      <span className="sr-only">Account ({me.name})</span>
    </a>
  );
}
