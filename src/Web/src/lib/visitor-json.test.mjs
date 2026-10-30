// node --test (npm test): Node strips the TypeScript types of the imported module.
import assert from 'node:assert/strict';
import { afterEach, beforeEach, test } from 'node:test';
import { loadVisitorJson } from './visitor-json.ts';

let calls;
let answer;

beforeEach(() => {
  calls = 0;
  answer = () => Promise.resolve({ ok: true, json: () => Promise.resolve({ itemCount: 2 }) });
  globalThis.fetch = (url) => {
    calls++;
    return answer(url);
  };
  globalThis.window = new EventTarget();
});

afterEach(() => {
  delete globalThis.fetch;
  delete globalThis.window;
});

/** Collects every value passed to the callback; resolves once `count` have arrived. */
function collect(count) {
  const values = [];
  let done;
  const finished = new Promise((resolve) => (done = resolve));
  const onValue = (value) => {
    values.push(value);
    if (values.length === count) done(values);
  };
  return { onValue, finished };
}

function pageshow(persisted) {
  const event = new Event('pageshow');
  event.persisted = persisted;
  window.dispatchEvent(event);
}

test('passes the JSON of a 2xx answer', async () => {
  const { onValue, finished } = collect(1);
  loadVisitorJson('/api/cart', onValue);
  assert.deepEqual(await finished, [{ itemCount: 2 }]);
});

test('passes null for a non-2xx answer', async () => {
  answer = () => Promise.resolve({ ok: false, json: () => Promise.reject(new Error('no body')) });
  const { onValue, finished } = collect(1);
  loadVisitorJson('/api/cart', onValue);
  assert.deepEqual(await finished, [null]);
});

test('passes null when the request fails', async () => {
  answer = () => Promise.reject(new TypeError('network'));
  const { onValue, finished } = collect(1);
  loadVisitorJson('/api/cart', onValue);
  assert.deepEqual(await finished, [null]);
});

test('reloads on a back/forward cache restore', async () => {
  const { onValue, finished } = collect(2);
  loadVisitorJson('/api/cart', onValue);
  pageshow(true);
  assert.deepEqual(await finished, [{ itemCount: 2 }, { itemCount: 2 }]);
  assert.equal(calls, 2);
});

test('does not reload on a normal pageshow', async () => {
  const { onValue, finished } = collect(1);
  loadVisitorJson('/api/cart', onValue);
  pageshow(false);
  await finished;
  await new Promise((resolve) => setTimeout(resolve, 10));
  assert.equal(calls, 1);
});
