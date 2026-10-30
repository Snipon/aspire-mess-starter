// node --test (npm test): Node strips the TypeScript types of the imported module.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { jsonLd } from './structured-data.ts';

test('CMS text cannot close the JSON-LD script', () => {
  const script = jsonLd({ headline: '</script><script>alert(1)</script>' });
  assert.ok(!script.includes('<'));
  assert.equal(JSON.parse(script).headline, '</script><script>alert(1)</script>');
});
