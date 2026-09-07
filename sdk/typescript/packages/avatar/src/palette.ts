export interface PaletteEntry {
  key: string
  bodyD: string
  bodyM: string
  bodyL: string
  markD: string
  markL: string
  shadow: string
  accent: string
}

/** Complete paint vocabulary used by the Composer mascot. */
export interface MascotPaintPalette extends PaletteEntry {
  /** Middle face-mark stop; profile palettes intentionally reuse `markL`. */
  markM: string
}

/** Canonical DotCraft mascot paint when no Agent Profile is selected. */
export const DEFAULT_MASCOT_PALETTE: MascotPaintPalette = {
  key: 'default',
  bodyD: '#2458f7',
  bodyM: '#5f82f7',
  bodyL: '#8fa5ff',
  markD: '#2257f5',
  markM: '#577df7',
  markL: '#8ca2ff',
  shadow: '#0b3d62',
  accent: '#5f82f7'
}

/** 12 role palettes. The first five define the built-in Agent Profile roles. */
export const PALETTE: PaletteEntry[] = [
  { key: 'blue', bodyD: '#2563eb', bodyM: '#4f7cf6', bodyL: '#8198f5', markD: '#2563eb', markL: '#6f8df5', shadow: '#07307c', accent: '#4f7cf6' },
  { key: 'indigo', bodyD: '#4f46e5', bodyM: '#6366f1', bodyL: '#8b8ff8', markD: '#3730a3', markL: '#818cf8', shadow: '#1e1b4b', accent: '#6366f1' },
  { key: 'violet', bodyD: '#6d28d9', bodyM: '#8b5cf6', bodyL: '#a78bfa', markD: '#5b21b6', markL: '#a78bfa', shadow: '#4c1d95', accent: '#8b5cf6' },
  { key: 'fuchsia', bodyD: '#a21caf', bodyM: '#d946ef', bodyL: '#e9a8f5', markD: '#86198f', markL: '#e879f9', shadow: '#581c87', accent: '#d946ef' },
  { key: 'pink', bodyD: '#be185d', bodyM: '#ec4899', bodyL: '#f9a8d4', markD: '#9d174d', markL: '#f472b6', shadow: '#831843', accent: '#ec4899' },
  { key: 'rose', bodyD: '#be123c', bodyM: '#f43f5e', bodyL: '#fda4af', markD: '#9f1239', markL: '#fb7185', shadow: '#881337', accent: '#f43f5e' },
  { key: 'orange', bodyD: '#c2410c', bodyM: '#f97316', bodyL: '#fdba74', markD: '#9a3412', markL: '#fb923c', shadow: '#7c2d12', accent: '#f97316' },
  { key: 'amber', bodyD: '#d97706', bodyM: '#eab308', bodyL: '#fbbf24', markD: '#92400e', markL: '#f59e0b', shadow: '#78350f', accent: '#f59e0b' },
  { key: 'lime', bodyD: '#4d7c0f', bodyM: '#84cc16', bodyL: '#bef264', markD: '#3f6212', markL: '#a3e635', shadow: '#365314', accent: '#84cc16' },
  { key: 'green', bodyD: '#15803d', bodyM: '#22c55e', bodyL: '#4ade80', markD: '#166534', markL: '#4ade80', shadow: '#14532d', accent: '#22c55e' },
  { key: 'teal', bodyD: '#0f766e', bodyM: '#14b8a6', bodyL: '#5eead4', markD: '#115e59', markL: '#2dd4bf', shadow: '#134e4a', accent: '#14b8a6' },
  { key: 'sky', bodyD: '#0284c7', bodyM: '#0ea5e9', bodyL: '#38bdf8', markD: '#0369a1', markL: '#22d3ee', shadow: '#075985', accent: '#0ea5e9' }
]


export function paletteOf(spec: { palette: number }): PaletteEntry { return PALETTE[spec.palette] ?? DEFAULT_MASCOT_PALETTE }
export function mascotPaletteOf(spec?: { palette: number }): MascotPaintPalette {
 if (!spec || spec.palette === -1) return DEFAULT_MASCOT_PALETTE
 const paint = paletteOf(spec); return { ...paint, markM: paint.markL }
}
