export const VIEWBOX = "-15 -15 259 259";
export const CENTER = 114.2705;
const BLOB_PATH = "M228.541 114.228C228.541 130.133 225.184 145.994 218.738 160.534C212.674 174.217 203.904 186.669 193.065 196.988C155.933 232.34 99.497 238.596 55.5255 212.24C45.097 205.99 35.6851 198.072 27.7451 188.866C19.1926 178.953 12.3686 167.569 7.65781 155.351C2.60712 142.264 0 128.257 0 114.228C0 98.3219 3.35751 82.4611 9.80315 67.9215C15.8672 54.2382 24.6377 41.7862 35.4767 31.4668C72.6081 -3.88483 129.044 -10.1413 173.016 16.2153C183.444 22.4653 192.856 30.3829 200.796 39.5896C209.349 49.5018 216.173 60.8859 220.883 73.1037C225.934 86.1906 228.541 100.198 228.541 114.228Z";
export const COLORS: Record<string, { light: string; dark: string }> = {
  black: { light: "#000000", dark: "#FFFFFF" },
  brown: { light: "#A27952", dark: "#855C36" },
  red: { light: "#FF3E51", dark: "#E02135" },
  orange: { light: "#FF781C", dark: "#FF6700" },
  yellow: { light: "#FFAF38", dark: "#FF9800" },
  green: { light: "#00C972", dark: "#009957" },
  cyan: { light: "#1CC3B0", dark: "#00A592" },
  blue: { light: "#2A92FE", dark: "#0E74E0" },
  violet: { light: "#A97EFE", dark: "#804EE0" },
  magenta: { light: "#FF5EB1", dark: "#E02A88" },
  gray: { light: "#959595", dark: "#777777" },
};

export const SHIPPED_SHAPES = ["blob", "pebble", "squircle", "tablet", "wedge", "hex", "cloud", "teardrop"] as const;
function shippedRandom(seed: number): () => number {
  let value = seed >>> 0;
  return () => {
    value = value + 1831565813 | 0;
    let next = Math.imul(value ^ value >>> 15, 1 | value);
    next = next + Math.imul(next ^ next >>> 7, 61 | next) ^ next;
    return ((next ^ next >>> 14) >>> 0) / 4294967296;
  };
}
function shippedHash(value: string): number {
  let hash = 2166136261;
  for (let index = 0; index < value.length; index += 1) hash = Math.imul(hash ^ value.charCodeAt(index), 16777619);
  return hash >>> 0;
}
function shippedColorIndex(value: string): number {
  const seed = (shippedHash(value) ^ Math.imul(1, 2654435769)) >>> 0;
  return Math.floor(shippedRandom((seed ^ 2654435769) >>> 0)() * 10);
}
function shippedShapeHash(value: string): number {
  let hash = shippedHash(value);
  hash = Math.imul(hash ^ hash >>> 16, 73244475);
  hash = Math.imul(hash ^ hash >>> 13, 3266489909);
  return (hash ^ hash >>> 16) >>> 0;
}
export function resolvePersonaColor(agentId: string, color?: string | null): string {
  if (color != null && COLORS[color] != null) return color;
  return ["brown", "red", "orange", "yellow", "green", "cyan", "blue", "violet", "magenta", "gray"][shippedColorIndex(agentId)] ?? "gray";
}
export function resolvePersonaShape(agentId: string, shape?: string | null): string {
  if (shape != null && (SHIPPED_SHAPES as readonly string[]).includes(shape)) return shape;
  return SHIPPED_SHAPES[shippedShapeHash(agentId) % SHIPPED_SHAPES.length] ?? "blob";
}

type Point = [number, number];

const TAU = Math.PI * 2;
const round2 = (value: number) => Math.round(value * 100) / 100;
const clamp = (value: number, minimum: number, maximum: number) => value < minimum ? minimum : value > maximum ? maximum : value;

function smoothPath(points: readonly Point[]): string {
  const path = [`M${round2(points[0][0])} ${round2(points[0][1])}`];
  for (let index = 0; index < points.length; index += 1) {
    const previous = points[(index - 1 + points.length) % points.length];
    const current = points[index];
    const next = points[(index + 1) % points.length];
    const afterNext = points[(index + 2) % points.length];
    path.push(`C${round2(current[0] + (next[0] - previous[0]) / 6)} ${round2(current[1] + (next[1] - previous[1]) / 6)} ${round2(next[0] - (afterNext[0] - current[0]) / 6)} ${round2(next[1] - (afterNext[1] - current[1]) / 6)} ${round2(next[0])} ${round2(next[1])}`);
  }
  return `${path.join("")}Z`;
}

class ArtifactPath {
  d = "";
  x = 0;
  y = 0;
  move(x: number, y: number) { this.d += `M${round2(x)} ${round2(y)}`; this.x = x; this.y = y; return this; }
  line(x: number, y: number) { this.d += `L${round2(x)} ${round2(y)}`; this.x = x; this.y = y; return this; }
  curve(x1: number, y1: number, x2: number, y2: number, x: number, y: number) {
    this.d += `C${round2(x1)} ${round2(y1)} ${round2(x2)} ${round2(y2)} ${round2(x)} ${round2(y)}`;
    this.x = x; this.y = y; return this;
  }
  corner(previous: Point, current: Point, next: Point, radius: number) {
    const unit = (from: Point, to: Point): Point => {
      const x = from[0] - to[0], y = from[1] - to[1], length = Math.hypot(x, y) || 1;
      return [x / length, y / length];
    };
    const before = unit(previous, current), after = unit(next, current);
    const start: Point = [current[0] + before[0] * radius, current[1] + before[1] * radius];
    const end: Point = [current[0] + after[0] * radius, current[1] + after[1] * radius];
    if (this.d) this.line(start[0], start[1]); else this.move(start[0], start[1]);
    this.d += `Q${round2(current[0])} ${round2(current[1])} ${round2(end[0])} ${round2(end[1])}`;
    this.x = end[0]; this.y = end[1]; return this;
  }
  arc(cx: number, cy: number, rx: number, ry: number, start: number, end: number) {
    const segments = Math.max(1, Math.ceil(Math.abs(end - start) / (Math.PI / 2)));
    const step = (end - start) / segments;
    const control = 4 / 3 * Math.tan(step / 4);
    let angle = start;
    for (let index = 0; index < segments; index += 1) {
      const nextAngle = angle + step;
      const from: Point = [cx + rx * Math.cos(angle), cy + ry * Math.sin(angle)];
      const to: Point = [cx + rx * Math.cos(nextAngle), cy + ry * Math.sin(nextAngle)];
      this.curve(from[0] - control * rx * Math.sin(angle), from[1] + control * ry * Math.cos(angle), to[0] + control * rx * Math.sin(nextAngle), to[1] - control * ry * Math.cos(nextAngle), to[0], to[1]);
      angle = nextAngle;
    }
    return this;
  }
  close() { return `${this.d}Z`; }
}

function sampledPath(generator: (angle: number) => Point, count = 128): string {
  const points: Point[] = [];
  for (let index = 0; index < count; index += 1) points.push(generator(index / count * TAU));
  return smoothPath(points);
}

function roundedPolygon(radius: number, sides: number, cornerRadius: number, start = 0): string {
  const points = Array.from({ length: sides }, (_, index): Point => {
    const angle = start + index / sides * TAU;
    return [CENTER + Math.cos(angle) * radius, CENTER + Math.sin(angle) * radius];
  });
  const path = new ArtifactPath();
  for (let index = 0; index < sides; index += 1) path.corner(points[(index - 1 + sides) % sides], points[index], points[(index + 1) % sides], cornerRadius);
  return path.close();
}

function cloudPath(points: readonly [number, number, number][], count = 160): string {
  return sampledPath((angle) => {
    const cos = Math.cos(angle), sin = Math.sin(angle);
    let radius = 0;
    for (const [x, y, circleRadius] of points) {
      const dx = x - CENTER, dy = y - CENTER, projection = cos * dx + sin * dy;
      const discriminant = projection * projection - (dx * dx + dy * dy) + circleRadius * circleRadius;
      if (discriminant <= 0) continue;
      radius = Math.max(radius, projection + Math.sqrt(discriminant));
    }
    return [CENTER + cos * radius, CENTER + sin * radius];
  }, count);
}

function squirclePath(width: number, height: number, exponent: number): string {
  return sampledPath((angle) => {
    const cos = Math.cos(angle), sin = Math.sin(angle);
    return [CENTER + Math.sign(cos) * Math.pow(Math.abs(cos), 2 / exponent) * width, CENTER + Math.sign(sin) * Math.pow(Math.abs(sin), 2 / exponent) * height];
  });
}

function tabletPath(width: number, height: number): string {
  return new ArtifactPath().move(CENTER - width + height, CENTER - height).line(CENTER + width - height, CENTER - height)
    .arc(CENTER + width - height, CENTER, height, height, -Math.PI / 2, Math.PI / 2).line(CENTER - width + height, CENTER + height)
    .arc(CENTER - width + height, CENTER, height, height, Math.PI / 2, Math.PI * 3 / 2).close();
}

function teardropPath(width: number, top: number, bottom: number, cornerRadius: number): string {
  const ratio = clamp(width / (bottom - top), -1, 1), height = Math.sqrt(1 - ratio * ratio);
  const right: Point = [CENTER + width * height, bottom - width * ratio];
  const left: Point = [CENTER - width * height, bottom - width * ratio];
  const angle = Math.atan2(right[1] - bottom, right[0] - CENTER);
  return new ArtifactPath().corner(right, [CENTER, top], left, cornerRadius).line(left[0], left[1]).arc(CENTER, bottom, width, width, Math.PI - angle, angle).close();
}

function pathSamples(path: string): Point[] {
  const tokens = path.match(/[MLCQZmlcqz]|-?\d*\.?\d+(?:e[-+]?\d+)?/gi) ?? [];
  const samples: Point[] = [];
  let index = 0, command = "", startX = 0, startY = 0, x = 0, y = 0;
  const number = () => Number(tokens[index++]);
  const addLine = (toX: number, toY: number) => {
    const length = Math.hypot(toX - x, toY - y), count = Math.max(2, Math.ceil(length / 4));
    for (let step = 1; step <= count; step += 1) samples.push([x + (toX - x) * step / count, y + (toY - y) * step / count]);
    x = toX; y = toY;
  };
  while (index < tokens.length) {
    if (/^[a-z]$/i.test(tokens[index])) command = tokens[index++].toUpperCase();
    if (command === "Z") { addLine(startX, startY); continue; }
    if (command === "M") { x = number(); y = number(); startX = x; startY = y; samples.push([x, y]); command = "L"; continue; }
    if (command === "L") { addLine(number(), number()); continue; }
    if (command === "Q") {
      const x1 = number(), y1 = number(), endX = number(), endY = number(), fromX = x, fromY = y;
      const count = Math.max(2, Math.ceil((Math.hypot(x1 - x, y1 - y) + Math.hypot(endX - x1, endY - y1)) / 4));
      for (let step = 1; step <= count; step += 1) { const t = step / count, inverse = 1 - t; samples.push([inverse * inverse * fromX + 2 * inverse * t * x1 + t * t * endX, inverse * inverse * fromY + 2 * inverse * t * y1 + t * t * endY]); }
      x = endX; y = endY; continue;
    }
    if (command === "C") {
      const x1 = number(), y1 = number(), x2 = number(), y2 = number(), endX = number(), endY = number(), fromX = x, fromY = y;
      const count = Math.max(2, Math.ceil((Math.hypot(x1 - x, y1 - y) + Math.hypot(x2 - x1, y2 - y1) + Math.hypot(endX - x2, endY - y2)) / 4));
      for (let step = 1; step <= count; step += 1) { const t = step / count, inverse = 1 - t; samples.push([inverse ** 3 * fromX + 3 * inverse ** 2 * t * x1 + 3 * inverse * t ** 2 * x2 + t ** 3 * endX, inverse ** 3 * fromY + 3 * inverse ** 2 * t * y1 + 3 * inverse * t ** 2 * y2 + t ** 3 * endY]); }
      x = endX; y = endY; continue;
    }
    index += 1;
  }
  return samples;
}

function normalizeArtifactPath(path: string): string {
  const samples = pathSamples(path);
  const xs = samples.map(([x]) => x), ys = samples.map(([, y]) => y);
  const minX = Math.min(...xs), maxX = Math.max(...xs), minY = Math.min(...ys), maxY = Math.max(...ys);
  const offsetX = CENTER - (minX + maxX) / 2, offsetY = CENTER - (minY + maxY) / 2;
  const scale = clamp(228.44 / Math.max(maxX - minX, maxY - minY), .9, 1.35);
  if (Math.abs(scale - 1) < .005 && Math.abs(offsetX) < .5 && Math.abs(offsetY) < .5) return path;
  let numberIndex = 0;
  return path.replace(/-?\d*\.?\d+(?:e[-+]?\d+)?/gi, (value) => {
    const coordinate = Number(value) + (numberIndex++ % 2 === 0 ? offsetX : offsetY);
    return String(round2(CENTER + (coordinate - CENTER) * scale));
  });
}

const ARTIFACT_SHAPE_PATHS: Record<string, string> = {
  blob: normalizeArtifactPath(BLOB_PATH),
  pebble: normalizeArtifactPath(sampledPath((angle) => { const radius = 108 * (1 + .075 * (Math.sin(angle * 2 + 1.1) * .6 + Math.sin(angle * 3 - 1.1) * .4)); return [CENTER + Math.cos(angle) * radius, CENTER + Math.sin(angle) * radius * .98]; })),
  squircle: normalizeArtifactPath(squirclePath(107, 107, 4.2)),
  tablet: normalizeArtifactPath(tabletPath(114, 74)),
  wedge: normalizeArtifactPath(roundedPolygon(130, 3, 60, -Math.PI / 2)),
  hex: normalizeArtifactPath(roundedPolygon(114, 6, 20, Math.PI / 6)),
  cloud: normalizeArtifactPath(cloudPath([[CENTER - 62, CENTER + 26, 56], [CENTER + 62, CENTER + 26, 54], [CENTER, CENTER + 34, 62], [CENTER - 24, CENTER - 30, 62], [CENTER + 38, CENTER - 26, 54]])),
  teardrop: normalizeArtifactPath(teardropPath(88, CENTER - 114, CENTER + 26, 18)),
};
export const personaShapePath = (shape: string) => ARTIFACT_SHAPE_PATHS[shape] ?? ARTIFACT_SHAPE_PATHS.blob;
