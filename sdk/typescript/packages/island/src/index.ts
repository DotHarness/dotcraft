export type IslandTheme = 'dark' | 'light'

export interface GlowPoint {
  x: number
  y: number
}

export interface GlowGeometry {
  edge: GlowPoint
  /** A closed clockwise path around the surface, `inset` from its edge, starting at `edge`. */
  path: string
  /** How far clockwise along `path` the comets meet, straight across from `edge`, as a share of its length. */
  meetAt: number
}

/** Without an origin the light starts at the top centre. */
export function glowGeometry(width: number, height: number, origin?: GlowPoint | null, inset = 1): GlowGeometry {
  const w = Math.max(0, width - 2 * inset)
  const h = Math.max(0, height - 2 * inset)
  const x = Math.min(w, Math.max(0, (origin?.x ?? width / 2) - inset))
  const y = Math.min(h, Math.max(0, (origin?.y ?? 0) - inset))
  const distances = origin ? [y, w - x, h - y, x] : [0, 1, 1, 1]
  const side = distances.indexOf(Math.min(...distances))
  const edge = [{ x, y: 0 }, { x: w, y }, { x, y: h }, { x: 0, y }][side]!
  const meet = [{ x, y: h }, { x: 0, y }, { x, y: 0 }, { x: w, y }][side]!
  const perimeter = 2 * (w + h)
  const along = (point: GlowPoint) =>
    point.y === 0 ? point.x : point.x === w ? w + point.y : point.y === h ? w + h + (w - point.x) : 2 * w + h + (h - point.y)
  const start = along(edge)
  const corners = [{ x: w, y: 0 }, { x: w, y: h }, { x: 0, y: h }, { x: 0, y: 0 }]
    .map((corner) => ({ corner, at: (along(corner) - start + perimeter) % perimeter || perimeter }))
    .sort((a, b) => a.at - b.at)
  const point = (p: GlowPoint) => `${round(p.x + inset)} ${round(p.y + inset)}`
  const path = `M${point(edge)}${corners.map(({ corner }) => `L${point(corner)}`).join('')}Z`
  const meetAt = ((along(meet) - start + perimeter) % perimeter) / perimeter
  return { edge: { x: edge.x + inset, y: edge.y + inset }, path, meetAt }
}

const round = (value: number) => Math.round(value * 100) / 100
