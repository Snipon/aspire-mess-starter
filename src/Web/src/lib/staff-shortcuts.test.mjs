// node --test (npm test): Node strips the TypeScript types of the imported module.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { shortcutsFor } from './staff-shortcuts.ts';

const offices = { cms: 'https://cms.test/umbraco/', commerce: 'https://shop.test', logs: 'https://logs.test', identity: 'https://idp.test' };
const id = 'd8360124-a5a7-4865-a637-44fb684412ea';
const labels = (shortcuts) => shortcuts.map((s) => s.label);

test('editors get the CMS and the page in it', () => {
  const shortcuts = shortcutsFor(['editor'], { cmsId: id, sku: 'DG01222' }, offices);
  assert.deepEqual(labels(shortcuts), ['Edit page', 'Content']);
  assert.equal(shortcuts[0].href, `https://cms.test/umbraco/section/content/workspace/document/edit/${id}`);
});

test('admins also get the shop, logs, users and status', () => {
  const shortcuts = shortcutsFor(['admin'], { sku: 'DG01222' }, offices);
  assert.deepEqual(labels(shortcuts), ['Content', 'Edit product', 'Shop admin', 'Logs', 'Users', 'System status']);
  assert.equal(shortcuts[1].href, 'https://shop.test/admin/commerce/products/sku/DG01222');
});

test('no roles, no shortcuts', () => {
  assert.deepEqual(shortcutsFor([], { cmsId: id }, offices), []);
});

test('unconfigured back offices and malformed context are left out', () => {
  assert.deepEqual(labels(shortcutsFor(['admin'], { cmsId: '../x', sku: 'a/b' }, { cms: 'https://cms.test' })), ['Content', 'System status']);
});
