/** App roles (the identity provider's "roles" claim) that staff features know; other values are ignored. */
export const STAFF_ROLES = ['admin', 'editor'] as const;
export type StaffRole = (typeof STAFF_ROLES)[number];

export interface Shortcut {
  label: string;
  href: string;
}

/** The page being viewed, as far as the shortcuts care: its CMS document or its shop product. */
export interface PageContext {
  cmsId?: string;
  sku?: string;
}

/** Base URLs of the back offices; a shortcut is left out when its URL is not configured. */
export interface BackOffices {
  cms?: string;
  commerce?: string;
  logs?: string;
  identity?: string;
}

export const backOfficesFromEnv = (): BackOffices => ({
  cms: process.env.CMS_BACKOFFICE_URL,
  commerce: process.env.COMMERCE_ADMIN_URL,
  logs: process.env.LOGS_URL,
  identity: process.env.IDP_ADMIN_URL,
});

const trim = (url: string) => url.replace(/\/+$/, '');
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const SKU = /^[A-Za-z0-9_-]{1,64}$/;

/**
 * The top bar's shortcuts for these roles on this page. Editors get the CMS (and this page in it); admins also get the
 * shop admin (and this product in it), the logs and the identity provider's console.
 */
export function shortcutsFor(roles: readonly StaffRole[], page: PageContext, offices: BackOffices): Shortcut[] {
  const shortcuts: Shortcut[] = [];
  const add = (label: string, base: string | undefined, path = '') => base && shortcuts.push({ label, href: `${trim(base)}${path}` });
  const isAdmin = roles.includes('admin');

  if (isAdmin || roles.includes('editor')) {
    if (page.cmsId && GUID.test(page.cmsId)) add('Edit page', offices.cms, `/section/content/workspace/document/edit/${page.cmsId}`);
    add('Content', offices.cms);
  }
  if (isAdmin) {
    if (page.sku && SKU.test(page.sku)) add('Edit product', offices.commerce, `/admin/commerce/products/sku/${page.sku}`);
    add('Shop admin', offices.commerce, '/admin/commerce');
    add('Logs', offices.logs);
    add('Users', offices.identity);
    shortcuts.push({ label: 'System status', href: '/status' });
  }
  return shortcuts;
}
