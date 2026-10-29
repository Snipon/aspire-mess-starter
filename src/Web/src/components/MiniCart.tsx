import { ShoppingCartIcon } from 'lucide-react';
import { useEffect, useState } from 'react';
import ResponsiveImage from '@/components/ResponsiveImage';
import { buttonVariants } from '@/components/ui/button';
import type { Cart } from '@/lib/api/client';
import { formatMoney } from '@/lib/format';
import { cn } from '@/lib/utils';

const MAX_ITEMS = 4;

/**
 * Cart icon with the item count and, on hover or focus, the cart's lines. Pages are shared-cached, so the cart is
 * fetched in the browser (/api/cart, no-store) and the server-rendered header never contains it.
 */
export default function MiniCart({ active }: { active: boolean }) {
  // undefined while loading, null for no cart (or when the cart could not be loaded).
  const [cart, setCart] = useState<Cart | null>();

  useEffect(() => {
    const load = () =>
      fetch('/api/cart')
        .then((r) => (r.ok ? (r.json() as Promise<Cart | null>) : null))
        .catch(() => null)
        .then(setCart);
    // Back/forward navigation restores the page as it was; the cart may have changed since.
    const onPageShow = (e: PageTransitionEvent) => e.persisted && load();
    load();
    window.addEventListener('pageshow', onPageShow);
    return () => window.removeEventListener('pageshow', onPageShow);
  }, []);

  const count = cart?.itemCount ?? 0;

  return (
    <div className="group relative">
      <a
        href="/cart"
        aria-current={active ? 'page' : undefined}
        className={cn(buttonVariants({ variant: 'ghost', size: 'icon' }), 'relative', active && 'bg-primary text-primary-foreground hover:bg-primary hover:text-primary-foreground')}
      >
        <ShoppingCartIcon aria-hidden="true" />
        <span className="sr-only">{count > 0 ? `Cart, ${count} ${count === 1 ? 'item' : 'items'}` : 'Cart'}</span>
        {count > 0 && (
          <span
            aria-hidden="true"
            className="font-narrow absolute -top-1 -right-1 flex h-5 min-w-5 items-center justify-center rounded-sm bg-primary px-1 text-xs font-bold text-primary-foreground tabular-nums ring-2 ring-background"
          >
            {count > 99 ? '99+' : count}
          </span>
        )}
      </a>
      {cart !== undefined && !active && (
        <div className="invisible absolute top-full right-0 z-20 w-80 pt-2 opacity-0 transition group-focus-within:visible group-focus-within:opacity-100 group-hover:visible group-hover:opacity-100 motion-reduce:transition-none">
          <div className="border border-border bg-popover p-4 text-popover-foreground">
            {!cart || cart.items.length === 0 ? (
              <p className="py-4 text-center text-sm text-muted-foreground">Your cart is empty.</p>
            ) : (
              <>
                <ul className="divide-y">
                  {cart.items.slice(0, MAX_ITEMS).map((item) => (
                    <li key={item.id} className="flex gap-3 py-2 first:pt-0">
                      <div className="size-12 shrink-0 overflow-hidden bg-muted">
                        {item.image && <ResponsiveImage image={{ ...item.image, alt: null }} sizes="48px" className="h-full w-full object-cover" />}
                      </div>
                      <div className="min-w-0 flex-1 text-sm">
                        <a href={item.path ?? '/shop'} className="line-clamp-2 font-semibold decoration-primary decoration-2 underline-offset-4 hover:underline">
                          {item.title}
                        </a>
                        <p className="font-narrow text-muted-foreground tabular-nums">
                          {item.quantity} × {formatMoney(item.unitPrice)}
                        </p>
                      </div>
                    </li>
                  ))}
                </ul>
                {cart.items.length > MAX_ITEMS && (
                  <p className="pt-2 text-sm text-muted-foreground">and {cart.items.length - MAX_ITEMS} more</p>
                )}
                <div className="mt-3 flex items-center justify-between border-t pt-3 text-sm">
                  <span className="text-muted-foreground">Subtotal</span>
                  <span className="font-wide text-base font-extrabold tabular-nums">{formatMoney(cart.subtotal)}</span>
                </div>
                <div className="mt-3 grid grid-cols-2 gap-2">
                  <a href="/cart" className={cn(buttonVariants({ variant: 'outline', size: 'sm' }))}>
                    View cart
                  </a>
                  <a href="/checkout" className={buttonVariants({ size: 'sm' })}>
                    Checkout
                  </a>
                </div>
              </>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
