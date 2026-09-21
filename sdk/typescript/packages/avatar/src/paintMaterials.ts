import type { SkinId } from './items.js'

export interface PaintMaterial {
  shadow: string
  accent: string
}

export const paintMaterials = {
  chrome: { shadow: '#1f2733', accent: '#c3ccd8' },
  holographic: { shadow: '#5b3a8c', accent: '#c98aff' },
  gold: { shadow: '#5a3a10', accent: '#f6d365' },
  lava: { shadow: '#3a0a05', accent: '#ff6a2a' },
  galaxy: { shadow: '#0b0620', accent: '#7c3aed' },
} as const satisfies Partial<Record<SkinId, PaintMaterial>>

export type PaintSkinId = keyof typeof paintMaterials
export function isPaintSkin(id: SkinId | 'none'): id is PaintSkinId { return id in paintMaterials }
export function paintMaterialOf(id: SkinId | 'none'): PaintMaterial | undefined { return isPaintSkin(id) ? paintMaterials[id] : undefined }
