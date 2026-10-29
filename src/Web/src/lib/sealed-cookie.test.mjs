// node --test (npm test): Node strips the TypeScript types of the imported module.
import assert from 'node:assert/strict';
import { test } from 'node:test';

// The key is derived once, on first use, so the secret must be set before the module seals anything.
process.env.SESSION_SECRET = 'test-secret';
const { seal, unseal } = await import('./sealed-cookie.ts');

const data = { sub: 'user-1', roles: ['editor'] };

test('a sealed value unseals to the same data', () => {
  assert.deepEqual(unseal('session', seal('session', data)), data);
});

test('a tampered value is rejected', () => {
  const raw = Buffer.from(seal('session', data), 'base64url');
  raw[raw.length - 1] ^= 1;
  assert.equal(unseal('session', raw.toString('base64url')), undefined);
});

test('a value sealed for one cookie is rejected as another', () => {
  assert.equal(unseal('staff_session', seal('session', data)), undefined);
});

test('a missing value is undefined', () => {
  assert.equal(unseal('session', undefined), undefined);
  assert.equal(unseal('session', ''), undefined);
});
