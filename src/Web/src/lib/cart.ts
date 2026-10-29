import type { AstroCookies } from 'astro';
import { api, type Cart } from './api/client';

const COOKIE = 'cart';
const MAX_AGE = 60 * 60 * 24 * 30;

export interface CartRef {
  id: string;
  token: string;
}

/** The cart cookie holds "<id>.<token>"; httpOnly, so page scripts never see the token. */
function readRef(cookies: AstroCookies): CartRef | undefined {
  const [id, token] = cookies.get(COOKIE)?.value.split('.') ?? [];
  return id && token ? { id, token } : undefined;
}

export const cartHeaders = (ref: CartRef) => ({ path: { cartId: ref.id }, header: { 'X-Cart-Token': ref.token } });

export function clearCart(cookies: AstroCookies) {
  cookies.delete(COOKIE, { path: '/' });
}

/** The visitor's open cart; a cookie for an unknown or checked-out cart is cleared. */
export async function loadCart(cookies: AstroCookies): Promise<{ ref: CartRef; cart: Cart } | undefined> {
  const ref = readRef(cookies);
  if (!ref) return undefined;
  const { data, response } = await api().GET('/api/carts/{cartId}', { params: cartHeaders(ref) });
  if (data) return { ref, cart: data };
  if (response.status !== 404 && response.status !== 409) throw new Error(`Cart API returned ${response.status}`);
  clearCart(cookies);
  return undefined;
}

/** The visitor's open cart, creating one (and its cookie) when there is none. */
export async function ensureCart(cookies: AstroCookies, url: URL): Promise<CartRef> {
  const existing = await loadCart(cookies);
  if (existing) return existing.ref;
  const { data } = await api().POST('/api/carts');
  if (!data) throw new Error('Could not create a cart');
  cookies.set(COOKIE, `${data.cart.id}.${data.token}`, {
    path: '/',
    httpOnly: true,
    sameSite: 'lax',
    secure: url.protocol === 'https:',
    maxAge: MAX_AGE,
  });
  return { id: data.cart.id, token: data.token };
}

/** Messages for the error codes the cart and checkout pass around in `?error=`. */
export const cartErrors: Record<string, string> = {
  out_of_stock: 'Not enough stock for that quantity.',
  product_not_found: 'That product is no longer available.',
  empty_cart: 'Your cart is empty.',
  cart_closed: 'That cart has already been checked out.',
  cart_not_found: 'Your cart has expired. Please add the items again.',
  invalid_request: 'That quantity is not possible.',
};
