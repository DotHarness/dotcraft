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
  bumblebee: { shadow: '#3a2c08', accent: '#f6c343' },
  aurora: { shadow: '#06131f', accent: '#34d399' },
  terminal: { shadow: '#020604', accent: '#7dff8a' },
  patina: { shadow: '#123a33', accent: '#58b39a' },
  thermal: { shadow: '#12072e', accent: '#ff7a1f' },
  candy: { shadow: '#2a0309', accent: '#ff4d63' },
  racer: { shadow: '#1f4f73', accent: '#f28a1c' },
  prism: { shadow: '#151830', accent: '#a276ff' },
  void: { shadow: '#030206', accent: '#8b5cf6' },
} as const satisfies Partial<Record<SkinId, PaintMaterial>>

export type PaintSkinId = keyof typeof paintMaterials
export function isPaintSkin(id: SkinId | 'none'): id is PaintSkinId { return id in paintMaterials }
export function paintMaterialOf(id: SkinId | 'none'): PaintMaterial | undefined { return isPaintSkin(id) ? paintMaterials[id] : undefined }
