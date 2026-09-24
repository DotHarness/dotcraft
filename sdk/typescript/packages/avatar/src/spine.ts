export type Pt = readonly [number, number]

export const n0 = (v: number) => Math.round(v)
const cr = (a: number, b: number, c: number, d: number, t: number) => .5 * (2 * b + (c - a) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (3 * b - a - 3 * c + d) * t * t * t)
const dcr = (a: number, b: number, c: number, d: number, t: number) => .5 * ((c - a) + 2 * (2 * a - 5 * b + 4 * c - d) * t + 3 * (3 * b - a - 3 * c + d) * t * t)

export function along(spine: readonly Pt[], t: number) {
  const seg = spine.length - 1, u = Math.min(Math.max(t, 0), 1) * seg, k = Math.min(Math.floor(u), seg - 1), f = u - k
  const p = (i: number) => spine[Math.min(Math.max(i, 0), seg)]
  const a = p(k - 1), b = p(k), c = p(k + 1), d = p(k + 2)
  const tx = dcr(a[0], b[0], c[0], d[0], f), ty = dcr(a[1], b[1], c[1], d[1], f), len = Math.hypot(tx, ty) || 1
  return { x: cr(a[0], b[0], c[0], d[0], f), y: cr(a[1], b[1], c[1], d[1], f), tx: tx / len, ty: ty / len }
}

function smooth(points: Pt[]) {
  const mid = (i: number) => { const a = points[(i + points.length) % points.length], b = points[(i + 1) % points.length]; return `${n0((a[0] + b[0]) / 2)} ${n0((a[1] + b[1]) / 2)}` }
  return `M${mid(-1)}${points.map((p, i) => `Q${n0(p[0])} ${n0(p[1])} ${mid(i)}`).join('')}Z`
}

// A tapered band along a Catmull-Rom spine; offset and share place it across the width (positive = the spine's outer normal).
export function tube(spine: readonly Pt[], width: (t: number) => number, from = 0, to = 1, offset = 0, share = 1, steps = 36) {
  const left: Pt[] = [], right: Pt[] = []
  for (let i = 0; i <= steps; i++) {
    const t = from + ((to - from) * i) / steps, s = along(spine, t), w = width(t), c = offset * w, h = (share * w) / 2
    left.push([s.x - s.ty * (c + h), s.y + s.tx * (c + h)]); right.push([s.x - s.ty * (c - h), s.y + s.tx * (c - h)])
  }
  return smooth([...left, ...right.reverse()])
}

export function ridge(spine: readonly Pt[], width: (t: number) => number, from: number, to: number, count: number, height: number, side = 1, lean = .5) {
  let d = ''
  for (let i = 0; i < count; i++) {
    const t = from + ((to - from) * (i + .5)) / count, s = along(spine, t), w = width(t), e = w * .4 * side, h = height * w / 100, b = h * .6
    const bx = s.x - s.ty * e, by = s.y + s.tx * e, nx = -s.ty * side, ny = s.tx * side
    d += `M${n0(bx - s.tx * b)} ${n0(by - s.ty * b)}L${n0(bx + nx * h + s.tx * lean * h)} ${n0(by + ny * h + s.ty * lean * h)}L${n0(bx + s.tx * b)} ${n0(by + s.ty * b)}Z`
  }
  return d
}

// A tapered flame lock from (x, y) toward angle; bend curls the tip clockwise when positive.
export function lock(x: number, y: number, angle: number, len: number, w: number, bend = .4) {
  const a = (angle * Math.PI) / 180, c = Math.cos(a), s = Math.sin(a)
  const at = (u: number, v: number): Pt => [x + u * c - v * s, y + u * s + v * c]
  return tube([at(0, 0), at(len * .35, -bend * len * .1), at(len * .7, bend * len * .06), at(len, bend * len * .32)], t => w * (1 - t) + 3, 0, 1, 0, 1, 14)
}

export function talon(x: number, y: number, angle: number, len: number, w: number, curl = 1) {
  const a = (angle * Math.PI) / 180, c = Math.cos(a), s = Math.sin(a)
  const p = (u: number, v: number) => `${n0(x + u * c - v * curl * s)} ${n0(y + u * s + v * curl * c)}`
  return `M${p(0, -w / 2)}Q${p(len * .7, -w * .6)} ${p(len, w * .35)}Q${p(len * .45, w * .2)} ${p(0, w / 2)}Z`
}

export const oval = (cx: number, cy: number, rx: number, ry = rx) => `M${cx - rx} ${cy}a${rx} ${ry} 0 1 0 ${2 * rx} 0a${rx} ${ry} 0 1 0 ${-2 * rx} 0Z`

// Mirrors an absolute path made of M, L, C, Q and Z about x = 0.
export function mirror(d: string) {
  return d.replace(/([MLCQ])([^MLCQZ]*)/g, (_, cmd: string, args: string) => {
    const nums = args.trim().split(/[\s,]+|(?=-)/).filter(Boolean).map(Number)
    return cmd + nums.map((v, i) => (i % 2 ? v : -v)).join(' ')
  })
}
