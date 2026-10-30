// node --test (npm test): Node strips the TypeScript types of the imported module.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { imageAttributes } from './image.ts';

const image = {
  url: '/media/original.jpg',
  alt: 'A desk',
  width: 1200,
  height: 800,
  focalPoint: { left: 0.25, top: 0.5 },
  srcset: [
    { url: '/media/a-480.jpg', width: 480 },
    { url: '/media/a-960.jpg', width: 960 },
  ],
};

test('uses the largest rendition as src and lists every rendition in srcset', () => {
  assert.deepEqual(imageAttributes(image, '48px'), {
    src: '/media/a-960.jpg',
    srcset: '/media/a-480.jpg 480w, /media/a-960.jpg 960w',
    sizes: '48px',
    width: 1200,
    height: 800,
    alt: 'A desk',
    objectPosition: '25% 50%',
  });
});

test('falls back to the original without renditions, focal point or alt', () => {
  assert.deepEqual(imageAttributes({ ...image, alt: null, focalPoint: null, width: null, height: null, srcset: [] }, '48px'), {
    src: '/media/original.jpg',
    srcset: undefined,
    sizes: undefined,
    width: undefined,
    height: undefined,
    alt: '',
    objectPosition: undefined,
  });
});
