// Lucent crosshair codes: decoding (the same format as Lucent's CrosshairCode), drawing (the same pixel rules as Lucent's CrosshairRenderer)
// and a tiny PNG writer. No dependencies; runs in Cloudflare Workers and in any modern browser or Node 18+.

const PREFIX = "LCX1-";
const limit = (v, min, max) => Math.max(min, Math.min(max, v));

function parseColor(value, fallback) {
  if (!value) return fallback;
  let hex = String(value).trim().replace(/^#/, "");
  if (hex.length === 8) hex = hex.slice(2);
  if (!/^[0-9a-fA-F]{6}$/.test(hex)) return fallback;
  const n = parseInt(hex, 16);
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
}
const formatColor = (c) => "#" + c.map((x) => x.toString(16).padStart(2, "0")).join("").toUpperCase();

function clamp(s) {
  s.name = String(s.name || "").replace(/[;|]/g, " ").trim();
  if (!s.name) s.name = "Custom";
  if (s.name.length > 24) s.name = s.name.slice(0, 24);
  s.length = limit(s.length, 0, 40); s.thickness = limit(s.thickness, 1, 10); s.gap = limit(s.gap, 0, 30);
  s.lengthY = limit(s.lengthY, 0, 40); s.gapY = limit(s.gapY, 0, 30);
  s.dotSize = limit(s.dotSize, 1, 12);
  s.rotation = limit(s.rotation, 0, 45); s.opacity = limit(s.opacity, 20, 100);
  s.offsetX = limit(s.offsetX, -80, 80); s.offsetY = limit(s.offsetY, -80, 80);
  s.outlineThickness = limit(s.outlineThickness, 1, 3);
  s.fill = parseColor(s.fill, [255, 255, 255]);
  s.outlineColor = parseColor(s.outlineColor, [0, 0, 0]);
  return s;
}

// Returns the settings, or null when the text is not a Lucent crosshair code.
export function decodeCrosshair(code) {
  if (!code) return null;
  code = String(code).trim();
  if (!code.startsWith(PREFIX)) return null;
  let parts;
  try {
    const bin = atob(code.slice(PREFIX.length));
    const bytes = Uint8Array.from(bin, (c) => c.charCodeAt(0));
    parts = new TextDecoder().decode(bytes).split(";");
  } catch (e) { return null; }
  if ((parts.length !== 18 && parts.length !== 21) || parts[1].length !== 4 || parts[5].length !== 2) return null;
  const ints = {};
  for (const i of [2, 3, 4, 6, 8, 9, 10, 11, 12, 13, 17]) {
    if (!/^-?\d+$/.test(parts[i])) return null;
    ints[i] = parseInt(parts[i], 10);
  }
  const on = (c) => c === "1";
  const s = {
    name: parts[0], top: on(parts[1][0]), right: on(parts[1][1]), bottom: on(parts[1][2]), left: on(parts[1][3]),
    length: ints[2], thickness: ints[3], gap: ints[4], dot: on(parts[5][0]), squareDot: on(parts[5][1]), dotSize: ints[6],
    rotation: ints[10], opacity: ints[11], offsetX: ints[12], offsetY: ints[13],
    fill: parts[14], outline: parts[15] === "1", outlineColor: parts[16], outlineThickness: ints[17],
  };
  if (parts.length === 21) {
    if (!/^-?\d+$/.test(parts[19]) || !/^-?\d+$/.test(parts[20])) return null;
    s.linked = parts[18] !== "0"; s.lengthY = parseInt(parts[19], 10); s.gapY = parseInt(parts[20], 10);
  } else { s.linked = true; s.lengthY = s.length; s.gapY = s.gap; }
  return clamp(s);
}

const vLength = (s) => (s.linked ? s.length : s.lengthY);
const vGap = (s) => (s.linked ? s.gap : s.gapY);
const hasArms = (s) => (s.length > 0 && (s.left || s.right)) || (vLength(s) > 0 && (s.top || s.bottom));
const evenCenter = (s) => (hasArms(s) ? s.thickness % 2 === 0 : s.dot && s.dotSize % 2 === 0);
const dotPixels = (s) => ((s.dotSize % 2 === 0) === evenCenter(s) ? s.dotSize : s.dotSize + 1);

function extentOf(s) {
  let reach = Math.trunc((s.thickness + 1) / 2);
  if (hasArms(s)) reach = Math.max(reach, Math.max(s.gap + s.length, vGap(s) + vLength(s)));
  if (s.dot) reach = Math.max(reach, Math.trunc((dotPixels(s) + 1) / 2));
  return reach + (s.outline ? s.outlineThickness : 0) + 1;
}
const inBand = (value, thickness) => { const low = -Math.trunc(thickness / 2); return value >= low && value <= low + thickness - 1; };
const inRun = (value, gap, length, positive, shift) =>
  positive ? value >= gap && value <= gap + length - 1 : value <= -gap - shift && value >= -(gap + length - 1) - shift;

// The pixels of the crosshair itself, as offsets from the center pixel (a size x size grid of booleans).
function fillMask(s, extent) {
  const size = extent * 2 + 1;
  const mask = Array.from({ length: size }, () => new Array(size).fill(false));
  const angle = (s.rotation * Math.PI) / 180, cos = Math.cos(angle), sin = Math.sin(angle);
  const even = evenCenter(s), middle = even ? -0.5 : 0, dotSize = dotPixels(s), dotRadius = dotSize / 2, shift = even ? 1 : 0;
  for (let y = -extent; y <= extent; y++) {
    for (let x = -extent; x <= extent; x++) {
      let on = false;
      if (hasArms(s)) {
        const px = x - middle, py = y - middle;
        const u = s.rotation === 0 ? x : Math.round(px * cos + py * sin + middle);
        const v = s.rotation === 0 ? y : Math.round(-px * sin + py * cos + middle);
        on = (s.right && inRun(u, s.gap, s.length, true, 0) && inBand(v, s.thickness)) ||
          (s.left && inRun(u, s.gap, s.length, false, shift) && inBand(v, s.thickness)) ||
          (s.bottom && inRun(v, vGap(s), vLength(s), true, 0) && inBand(u, s.thickness)) ||
          (s.top && inRun(v, vGap(s), vLength(s), false, shift) && inBand(u, s.thickness));
      }
      if (!on && s.dot) {
        const dx = x - middle, dy = y - middle;
        on = s.squareDot
          ? Math.abs(dx) <= dotRadius - 0.5 + 1e-9 && Math.abs(dy) <= dotRadius - 0.5 + 1e-9
          : dx * dx + dy * dy <= dotRadius * dotRadius + (dotSize <= 2 ? 0.25 : 0);
      }
      mask[x + extent][y + extent] = on;
    }
  }
  return mask;
}

// A short description for the reply under the picture.
export function describeCrosshair(s) {
  const lines = [];
  const arms = [s.top && "top", s.right && "right", s.bottom && "bottom", s.left && "left"].filter(Boolean);
  if (hasArms(s)) {
    lines.push(arms.length === 4 ? "Four arms" : "Arms: " + arms.join(", "));
    lines.push(`Length ${s.length}${s.linked ? "" : " x " + s.lengthY}, gap ${s.gap}${s.linked ? "" : " x " + s.gapY}, thickness ${s.thickness}`);
  }
  if (s.dot) lines.push(`${s.squareDot ? "Square" : "Round"} dot, size ${s.dotSize}`);
  if (s.rotation) lines.push(`Turned ${s.rotation} degrees`);
  lines.push(`Color ${formatColor(s.fill)}` + (s.outline ? `, outline ${formatColor(s.outlineColor)}` : ", no outline") + (s.opacity < 100 ? `, ${s.opacity}% opacity` : ""));
  return lines;
}

// ---- PNG
const crcTable = (() => {
  const t = new Uint32Array(256);
  for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; t[n] = c >>> 0; }
  return t;
})();
function crc32(bytes) { let c = 0xffffffff; for (let i = 0; i < bytes.length; i++) c = crcTable[(c ^ bytes[i]) & 255] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; }
function chunk(type, data) {
  const out = new Uint8Array(12 + data.length), view = new DataView(out.buffer);
  view.setUint32(0, data.length);
  for (let i = 0; i < 4; i++) out[4 + i] = type.charCodeAt(i);
  out.set(data, 8);
  view.setUint32(8 + data.length, crc32(out.subarray(4, 8 + data.length)));
  return out;
}
async function zlib(bytes) {
  const stream = new Blob([bytes]).stream().pipeThrough(new CompressionStream("deflate"));
  return new Uint8Array(await new Response(stream).arrayBuffer());
}
async function encodePng(width, height, rgb) {
  const raw = new Uint8Array((width * 3 + 1) * height);
  for (let y = 0; y < height; y++) { raw[y * (width * 3 + 1)] = 0; raw.set(rgb.subarray(y * width * 3, (y + 1) * width * 3), y * (width * 3 + 1) + 1); }
  const ihdr = new Uint8Array(13), v = new DataView(ihdr.buffer);
  v.setUint32(0, width); v.setUint32(4, height); ihdr[8] = 8; ihdr[9] = 2;
  const parts = [new Uint8Array([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", ihdr), chunk("IDAT", await zlib(raw)), chunk("IEND", new Uint8Array(0))];
  const png = new Uint8Array(parts.reduce((n, p) => n + p.length, 0));
  let at = 0; for (const p of parts) { png.set(p, at); at += p.length; }
  return png;
}

// The crosshair on a dark panel and a light panel side by side (so the outline's job is visible), each pixel drawn as a block.
export async function renderCrosshairPng(s) {
  const extent = extentOf(s), size = extent * 2 + 1, mask = fillMask(s, extent);
  const panel = 200, scale = limit(Math.floor((panel - 16) / size), 2, 14);
  const width = panel * 2, height = panel, rgb = new Uint8Array(width * height * 3);
  const backgrounds = [[22, 22, 27], [196, 201, 210]];
  const alpha = s.opacity / 100, reach = s.outline ? s.outlineThickness : 0;
  for (let p = 0; p < 2; p++) {
    const bg = backgrounds[p];
    for (let y = 0; y < height; y++) for (let x = 0; x < panel; x++) { const i = (y * width + p * panel + x) * 3; rgb[i] = bg[0]; rgb[i + 1] = bg[1]; rgb[i + 2] = bg[2]; }
    const x0 = p * panel + Math.round((panel - size * scale) / 2), y0 = Math.round((panel - size * scale) / 2);
    for (let gy = 0; gy < size; gy++) for (let gx = 0; gx < size; gx++) {
      let color = null;
      if (mask[gx][gy]) color = s.fill;
      else if (reach > 0) {
        outer: for (let dy = -reach; dy <= reach; dy++) for (let dx = -reach; dx <= reach; dx++) {
          const nx = gx + dx, ny = gy + dy;
          if (nx >= 0 && ny >= 0 && nx < size && ny < size && mask[nx][ny]) { color = s.outlineColor; break outer; }
        }
      }
      if (!color) continue;
      for (let by = 0; by < scale; by++) for (let bx = 0; bx < scale; bx++) {
        const i = ((y0 + gy * scale + by) * width + x0 + gx * scale + bx) * 3;
        for (let c = 0; c < 3; c++) rgb[i + c] = Math.round(color[c] * alpha + rgb[i + c] * (1 - alpha));
      }
    }
  }
  return encodePng(width, height, rgb);
}
