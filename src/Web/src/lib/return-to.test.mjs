// node --test (npm test): Node strips the TypeScript types of the imported module.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { safeReturnTo } from './return-to.ts';

test('same-site paths are kept with their query and hash', () => {
  assert.equal(safeReturnTo('/shop?page=2#top'), '/shop?page=2#top');
});

test('anything that could leave the site becomes /', () => {
  for (const value of [null, '', 'https://evil.test', '//evil.test', '/\\evil.test', '/.//evil.test', 'javascript:alert(1)', '/auth/login']) {
    assert.equal(safeReturnTo(value), '/', String(value));
  }
});
