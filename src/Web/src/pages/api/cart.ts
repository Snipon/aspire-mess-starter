import type { APIRoute } from 'astro';
import { loadCart } from '../../lib/cart';
import { log } from '../../lib/log';

const NO_STORE = { 'Cache-Control': 'no-store' };

/**
 * The visitor's cart (or null) for the header's mini cart (MiniCart.tsx). Pages render nothing per visitor, so the
 * header fetches this per visitor in the browser instead of rendering the cart server-side.
 */
export const GET: APIRoute = async ({ cookies }) => {
  try {
    const loaded = await loadCart(cookies);
    return Response.json(loaded?.cart ?? null, { headers: NO_STORE });
  } catch (error) {
    log.warn('Mini cart failed', error);
    return Response.json(null, { status: 502, headers: NO_STORE });
  }
};
