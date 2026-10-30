/**
 * Marbled seams, after Codrops' Seam Transitions demo 06 "Marble" (MIT, github.com/codrops/SeamTransitions). Where a
 * [data-seam] block (the hero with media, the CTA band) meets the page above or below it, the two surfaces are laid
 * along the edge in thin alternating bands and combed into tongues and chevrons as the edge comes up the screen, the
 * hero's photograph streaked along with them. The pattern follows the scroll position: it holds when the page stops,
 * and the edge is straight again when it is back at the bottom of the screen.
 *
 * Each seam is a WebGL 2 canvas over the empty space around the edge only: from just below the content above it to
 * just above the content below it ([data-seam-content] inside the block, the neighbouring block outside it, half the
 * gap when the neighbour has a seam too). Without WebGL 2, or when the visitor prefers reduced motion, edges stay
 * plain; without CORS on the photograph, the hero's side is its flat background.
 */

const VERTEX = `#version 300 es
uniform vec2 uSize;
out vec2 vPx;
void main() {
  vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2)); // one triangle over the canvas
  vPx = vec2(p.x, 1.0 - p.y) * uSize;
  gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}`;

// In CSS px, y down from the canvas's top. uEdge is where the block's edge is; uTravel how far it has come up from the
// bottom of the screen, uProgress the same as 0 to 1.
const FRAGMENT = `#version 300 es
precision highp float;

uniform vec2 uSize;
uniform float uEdge;
uniform float uTravel;
uniform float uProgress;
uniform vec3 uUpper;
uniform vec3 uLower;
uniform float uPhotoSide; // 1: the upper surface is the photograph, -1: the lower, 0: neither
uniform sampler2D uPhoto;
uniform vec4 uPhotoBox; // where the photograph is drawn (object-fit: cover), x y w h
uniform vec4 uHero; // the hero's box, x y w h
uniform vec4 uVeil; // the hero's darkening gradient: from x to y at z of the way, then to w
uniform float uVeilAcross; // 1: the gradient runs left to right, 0: bottom to top

in vec2 vPx;
out vec4 outColor;

float hash(vec2 p) {
  p = fract(p * vec2(123.34, 456.21));
  p += dot(p, p + 45.32);
  return fract(p.x * p.y);
}

float noise(vec2 p) {
  vec2 i = floor(p);
  vec2 f = fract(p);
  vec2 u = f * f * (3.0 - 2.0 * f);
  return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}

float fbm(vec2 p) {
  float v = 0.0;
  float a = 0.5;
  for (int i = 0; i < 5; i++) {
    v += a * noise(p);
    p = p * 2.03 + 17.1;
    a *= 0.5;
  }
  return v / 0.97;
}

float veil(vec2 px) {
  float t = clamp(uVeilAcross > 0.5 ? (px.x - uHero.x) / uHero.z : 1.0 - (px.y - uHero.y) / uHero.w, 0.0, 1.0);
  return t < uVeil.z ? mix(uVeil.x, uVeil.y, t / uVeil.z) : mix(uVeil.y, uVeil.w, (t - uVeil.z) / (1.0 - uVeil.z));
}

// The photograph as the hero draws it, under its veil. Outside the hero its edge row carries on, so whatever is combed
// out of the hero carries its colours.
vec3 photo(vec2 px) {
  px.y = clamp(px.y, uHero.y + 0.5, uHero.y + uHero.w - 0.5);
  vec2 uv = clamp((px - uPhotoBox.xy) / uPhotoBox.zw, 0.0, 1.0);
  return texture(uPhoto, uv).rgb * (1.0 - veil(px));
}

void main() {
  vec2 px = vPx;
  float d = px.y - uEdge; // below the edge, positive
  float below = uSize.y - uEdge;

  // How hard the comb has worked here: more as the edge comes up, most at the edge, nothing at the canvas's top and
  // bottom.
  float grown = smoothstep(0.0, 0.6, uProgress);
  float reach = smoothstep(-uEdge, 0.0, d) * (1.0 - smoothstep(0.0, below, d));
  float amount = grown * reach * reach;
  if (amount < 0.002) {
    outColor = vec4(0.0);
    return;
  }

  // Where this point's colour was combed from: a fine stroke across near the edge (feathering), a wide one along
  // (tongues up and down) and noise, each stroke's spacing and depth wandering, all moving on as the edge travels.
  float t = uTravel / 160.0;
  vec2 q = px;
  float feather = 2.5 * noise(vec2(q.x / 80.0, q.y / 40.0));
  q.x -= amount * (1.0 - smoothstep(30.0, 110.0, abs(d))) * 18.0 * sin(q.y / 6.0 - t * 1.3 + feather);
  float wander = 7.0 * noise(vec2(q.x / 150.0, 2.0 + t * 0.05));
  float swell = 0.3 + 1.4 * noise(vec2(q.x / 230.0, 1.0 + t * 0.05));
  q.y -= amount * 50.0 * swell * sin(q.x / 26.0 + wander + t);
  q += amount * 70.0 * (vec2(fbm(q / 170.0 + t * 0.1), fbm(q / 170.0 + 9.2 - t * 0.1)) - 0.5);

  // Before combing, the surfaces lie in bands along the edge: the upper one, then stripes of both, the upper's thinning
  // out downwards, then the lower one. The bands widen as the edge comes up.
  float s = (q.y - uEdge) / 16.0;
  float bands = 4.0 * grown;
  float share = 0.5 - 0.5 * clamp(s / bands, -1.0, 1.0);
  float w = fwidth(s);
  float h = smoothstep(share + w, share - w, fract(s)) * step(abs(s), bands) + step(s, -bands);
  vec3 upper = uPhotoSide > 0.5 ? photo(q) : uUpper;
  vec3 lower = uPhotoSide < -0.5 ? photo(q) : uLower;

  // Painted outright where the comb has moved anything, fading in from where it hasn't.
  outColor = vec4(mix(lower, upper, h), 1.0) * smoothstep(0.0, 0.02, amount);
}`;

// Hero.astro's darkening over its media (from-black/80 via-black/65 to-black/35 bottom to top on phones;
// from-black/80 via-black/60 via-55% to-black/20 left to right from sm).
const VEIL_PHONE = [0.8, 0.65, 0.5, 0.35];
const VEIL_SM = [0.8, 0.6, 0.55, 0.2];
const ROOM = 200; // the most a seam reaches either side of its edge, px
const DPR = 2;

interface Seam {
  section: HTMLElement;
  side: 'top' | 'bottom';
  canvas: HTMLCanvasElement;
  gl: WebGL2RenderingContext;
  uniform: (name: string, value: number | number[]) => void;
  photo?: HTMLImageElement; // once it is uploaded
  edge: number; // the edge's document y
  above: number;
  on: boolean;
  travel: number;
}

const docTop = (element: HTMLElement) => {
  let y = 0;
  for (let e: HTMLElement | null = element; e; e = e.offsetParent as HTMLElement | null) y += e.offsetTop;
  return y; // the layout position: the reveal's translate doesn't count
};

// Background colours as 0–1 RGB, via a 2D canvas so any CSS colour syntax works.
const swatch = document.createElement('canvas').getContext('2d', { willReadFrequently: true })!;
const colours = new Map<string, number[]>();
const colour = (element: Element) => {
  const css = getComputedStyle(element).backgroundColor;
  let rgb = colours.get(css);
  if (!rgb) {
    swatch.clearRect(0, 0, 1, 1);
    swatch.fillStyle = css;
    swatch.fillRect(0, 0, 1, 1);
    rgb = [...swatch.getImageData(0, 0, 1, 1).data.slice(0, 3)].map((c) => c / 255);
    colours.set(css, rgb);
  }
  return rgb;
};

function create(section: HTMLElement, side: Seam['side']): Seam | undefined {
  const canvas = document.createElement('canvas');
  canvas.setAttribute('aria-hidden', 'true');
  canvas.style.cssText = 'position:absolute;left:0;width:100%;z-index:1;pointer-events:none;display:none';
  const gl = canvas.getContext('webgl2', { antialias: false });
  if (!gl) return;
  const program = gl.createProgram();
  for (const [type, source] of [
    [gl.VERTEX_SHADER, VERTEX],
    [gl.FRAGMENT_SHADER, FRAGMENT],
  ] as const) {
    const shader = gl.createShader(type)!;
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    gl.attachShader(program, shader);
  }
  gl.linkProgram(program);
  if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
    console.warn('Seam shader failed', gl.getProgramInfoLog(program));
    return;
  }
  gl.useProgram(program);
  const locations = new Map<string, WebGLUniformLocation | null>();
  const uniform = (name: string, value: number | number[]) => {
    if (!locations.has(name)) locations.set(name, gl.getUniformLocation(program, name));
    const at = locations.get(name)!;
    if (typeof value === 'number') gl.uniform1f(at, value);
    else if (value.length === 2) gl.uniform2fv(at, value);
    else if (value.length === 3) gl.uniform3fv(at, value);
    else gl.uniform4fv(at, value);
  };
  document.body.append(canvas);
  return { section, side, canvas, gl, uniform, edge: 0, above: 0, on: false, travel: NaN };
}

// The hero's photograph, read into the seam's texture. A video hero uses its image (the poster).
function loadPhoto(seam: Seam, img: HTMLImageElement) {
  const source = new Image();
  source.crossOrigin = 'anonymous';
  source.src = img.currentSrc || img.src;
  source
    .decode()
    .then(() => {
      const { gl } = seam;
      gl.bindTexture(gl.TEXTURE_2D, gl.createTexture());
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, source);
      seam.photo = source;
      render(seam, true);
    })
    .catch(() => {}); // no CORS (or no image): the hero's side stays its background colour
}

// Where the seam's canvas goes: the edge, and the empty space either side of it.
function measure(seam: Seam) {
  const { section, side, canvas, gl } = seam;
  const top = docTop(section);
  const bottom = top + section.offsetHeight;
  const content = section.querySelector<HTMLElement>('[data-seam-content]');
  let neighbour = section as Element | null;
  do neighbour = side === 'top' ? neighbour!.previousElementSibling : neighbour!.nextElementSibling;
  while (neighbour && !neighbour.getClientRects().length); // skip scripts
  if (!(neighbour instanceof HTMLElement)) neighbour = null;
  seam.on = false;
  canvas.style.display = 'none';
  if (!content || !neighbour) return;
  const padding = getComputedStyle(content);
  const contentTop = docTop(content) + parseFloat(padding.paddingTop);
  const contentBottom = docTop(content) + content.offsetHeight - parseFloat(padding.paddingBottom);
  const share = neighbour.hasAttribute('data-seam') ? 2 : 1;
  const gap = (side === 'top' ? top - (docTop(neighbour) + neighbour.offsetHeight) : docTop(neighbour) - bottom) / share;
  const above = Math.round(Math.min(ROOM, side === 'top' ? gap : bottom - contentBottom));
  const below = Math.round(Math.min(ROOM, side === 'top' ? contentTop - top : gap));
  if (above < 8 || below < 8) return;

  seam.edge = side === 'top' ? top : bottom;
  seam.above = above;
  seam.on = true;
  const width = document.documentElement.clientWidth;
  const dpr = Math.min(DPR, devicePixelRatio || 1);
  canvas.style.top = `${seam.edge - above}px`;
  canvas.style.height = `${above + below}px`;
  canvas.style.display = 'block';
  canvas.width = Math.round(width * dpr);
  canvas.height = Math.round((above + below) * dpr);
  gl.viewport(0, 0, canvas.width, canvas.height);
  seam.uniform('uSize', [width, above + below]);
  seam.uniform('uEdge', above);
  seam.uniform('uVeilAcross', matchMedia('(min-width: 40rem)').matches ? 1 : 0);
  seam.uniform('uVeil', matchMedia('(min-width: 40rem)').matches ? VEIL_SM : VEIL_PHONE);
}

function render(seam: Seam, force = false) {
  if (!seam.on) return;
  const { section, side, canvas, gl, photo } = seam;
  const box = canvas.getBoundingClientRect();
  if (box.top > innerHeight || box.bottom < 0) return;
  const travel = innerHeight - (box.top + seam.above);
  if (travel === seam.travel && !force) return;
  seam.travel = travel;

  seam.uniform('uTravel', travel);
  seam.uniform('uProgress', Math.min(1, Math.max(0, travel / innerHeight)));
  // Colours each time, so a theme switch carries over.
  const page = colour(document.body);
  seam.uniform('uUpper', side === 'top' ? page : colour(section));
  seam.uniform('uLower', side === 'top' ? colour(section) : page);
  seam.uniform('uPhotoSide', photo ? (side === 'top' ? -1 : 1) : 0);
  if (photo) {
    // From the rendered boxes, so the hero's drift (global.css) is followed.
    const hero = section.getBoundingClientRect();
    const img = section.querySelector('img')!;
    const shown = img.getBoundingClientRect();
    const [x, y] = getComputedStyle(img).objectPosition.split(' ').map((p) => parseFloat(p) / 100);
    const scale = Math.max(shown.width / photo.naturalWidth, shown.height / photo.naturalHeight);
    const w = photo.naturalWidth * scale;
    const h = photo.naturalHeight * scale;
    seam.uniform('uHero', [hero.left - box.left, hero.top - box.top, hero.width, hero.height]);
    seam.uniform('uPhotoBox', [shown.left - box.left + (shown.width - w) * x, shown.top - box.top + (shown.height - h) * y, w, h]);
  }
  gl.clearColor(0, 0, 0, 0);
  gl.clear(gl.COLOR_BUFFER_BIT);
  gl.drawArrays(gl.TRIANGLES, 0, 3);
}

export function marbleSeams() {
  if (!matchMedia('(prefers-reduced-motion: no-preference)').matches) return;
  const seams: Seam[] = [];
  for (const section of document.querySelectorAll<HTMLElement>('[data-seam]')) {
    for (const side of ['top', 'bottom'] as const) {
      const seam = create(section, side);
      if (!seam) return; // no WebGL 2
      seams.push(seam);
      const img = section.hasAttribute('data-hero') && section.querySelector('img');
      if (img) loadPhoto(seam, img);
    }
  }
  if (!seams.length) return;

  let frame = 0;
  let forced = false;
  const schedule = (force: boolean) => {
    forced ||= force;
    frame ||= requestAnimationFrame(() => {
      seams.forEach((seam) => render(seam, forced));
      frame = 0;
      forced = false;
    });
  };
  const layout = () => {
    seams.forEach(measure);
    schedule(true);
  };
  new ResizeObserver(layout).observe(document.body); // late images and fonts move the edges
  addEventListener('resize', layout);
  addEventListener('scroll', () => schedule(false), { passive: true });
  new MutationObserver(() => schedule(true)).observe(document.documentElement, { attributeFilter: ['class'] }); // theme
}
