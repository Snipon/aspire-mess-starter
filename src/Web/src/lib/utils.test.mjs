// node --test (npm test): Node strips the TypeScript types of the imported module.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { cn, tv } from './utils.ts';

test('Fibonacci font sizes merge as sizes, not colours', () => {
  assert.equal(cn('text-primary-foreground text-13', 'text-16'), 'text-primary-foreground text-16');
  assert.equal(tv({ base: 'text-13 text-primary-foreground', variants: { size: { lg: 'text-16' } } })({ size: 'lg' }), 'text-primary-foreground text-16');
  assert.equal(cn('rounded-3 max-w-610', 'rounded-5 max-w-987'), 'rounded-5 max-w-987');
});
