export interface Box { left: number; top: number; right: number; bottom: number }

export interface Dot {
  at: number
  delay: number
  life: number
  x: number
  y: number
  fromX: number
  fromY: number
  vx: number
  vy: number
  gravity: number
  size: number
  hue: number
  peak: number
}

type Hue = (x: number) => number

const COLUMN = 3.7
const ROW = 3.1

function noise(x: number, y: number): number {
  const value = Math.sin(x * 12.9898 + y * 78.233) * 43758.5453
  return value - Math.floor(value)
}

function dot(now: number, x: number, y: number, hue: number, extra: Partial<Dot> = {}): Dot {
  return { at: now, delay: 0, life: 600, x, y, fromX: x, fromY: y, vx: 0, vy: 0, gravity: 0, size: 1.6, hue, peak: 1, ...extra }
}

export function bloom(now: number, caret: Box, hueAt: Hue, tempo: number): Dot[] {
  const cx = caret.left + 2
  const cy = (caret.top + caret.bottom) / 2
  const radius = 13 + tempo * 6
  const dots: Dot[] = []
  for (let gx = Math.floor((cx - radius) / COLUMN); gx <= Math.ceil((cx + radius) / COLUMN); gx++) {
    for (let gy = Math.floor((cy - radius) / ROW); gy <= Math.ceil((cy + radius) / ROW); gy++) {
      const x = gx * COLUMN + (gy % 2) * 1.75
      const y = gy * ROW
      const distance = Math.hypot(x - cx, (y - cy) * 1.25)
      const falloff = Math.exp(-(distance * distance) / (radius * radius * 0.45))
      if (noise(gx + now * 0.001, gy) > falloff * 1.15) continue
      dots.push(dot(now, x, y, hueAt(cx) + (x - cx) * 0.018 + (y - cy) * 0.01, {
        delay: distance * 14,
        life: 380 + noise(gy, gx) * 320,
        vy: -6,
        size: 1.25 + falloff * 0.6,
        peak: 0.35 + falloff * 0.65,
      }))
    }
  }
  return dots
}

export function sparks(now: number, caret: Box, hueAt: Hue, tempo: number, burst: boolean): Dot[] {
  const count = Math.round(5 + tempo * 5 + (burst ? 4 : 0))
  const x = caret.left
  const y = caret.top - 2
  return Array.from({ length: count }, (_, index) => {
    const spread = noise(index, now % 997) - 0.5
    return dot(now, x, y, hueAt(x) + spread * 0.6, {
      delay: index * 6,
      life: 520 + noise(now % 991, index) * 240,
      vx: spread * 90,
      vy: -(70 + noise(index * 3, now % 977) * 80) * (burst ? 1.25 : 1),
      gravity: 300,
      size: 1.3 + noise(index, 7) * 0.7,
    })
  })
}

export function resolve(now: number, glyph: Box, hueAt: Hue): Dot[] {
  const dots: Dot[] = []
  for (let x = glyph.left - 1.5; x <= glyph.right + 1.5; x += COLUMN * 0.8) {
    for (let y = glyph.top + 2; y <= glyph.bottom - 2; y += ROW * 0.9) {
      const seed = noise(x, y + now * 0.001)
      if (seed > 0.78) continue
      const angle = seed * Math.PI * 2
      const reach = 7 + seed * 9
      dots.push(dot(now, x, y, hueAt(glyph.left) + (x - glyph.left) * 0.03 + (y - glyph.top) * 0.012, {
        fromX: x + Math.cos(angle) * reach,
        fromY: y + Math.sin(angle) * reach - 4,
        delay: seed * 60,
        life: 460,
        size: 1.35,
      }))
    }
  }
  return dots
}

export function wake(now: number, caret: Box, hueAt: Hue, tempo: number): Dot[] {
  const dots: Dot[] = []
  const base = caret.bottom + 3
  const rows = 1 + Math.round(tempo * 3)
  for (let column = -2; column <= 1; column++) {
    const x = caret.left + column * COLUMN
    const height = Math.max(1, rows - Math.abs(column + 0.5) * 0.9)
    for (let row = 0; row < height; row++) {
      dots.push(dot(now, x + (row % 2) * 1.75, base + row * ROW, hueAt(caret.left) + column * 0.07 + row * 0.08, {
        life: 900 - row * 160,
        delay: row * 30,
        vx: -10,
        size: 1.7 - row * 0.2,
        peak: 0.95 - row * 0.18,
      }))
    }
  }
  return dots
}

export function drawDots(
  context: CanvasRenderingContext2D,
  dots: Dot[],
  now: number,
  origin: { x: number; y: number },
  color: (hue: number) => string,
): void {
  for (let index = dots.length - 1; index >= 0; index--) {
    const item = dots[index]
    const elapsed = now - item.at - item.delay
    const age = elapsed / item.life
    if (age >= 1) {
      dots.splice(index, 1)
      continue
    }
    if (age < 0) continue
    const seconds = elapsed / 1000
    const ease = 1 - (1 - Math.min(1, age / 0.45)) ** 3
    const x = item.fromX + (item.x - item.fromX) * ease + item.vx * seconds
    const y = item.fromY + (item.y - item.fromY) * ease + item.vy * seconds + item.gravity * seconds * seconds / 2
    context.globalAlpha = item.peak * Math.min(1, age * 7) * (1 - age ** 2.2)
    context.fillStyle = color(item.hue)
    context.fillRect(x - origin.x, y - origin.y, item.size, item.size)
  }
  context.globalAlpha = 1
}
