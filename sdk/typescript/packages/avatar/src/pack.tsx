import { createContext, useContext, type ReactNode } from 'react'
import type { AnySeries, ExternalItemId, Rarity } from './items.js'
import type { DecorationCopy } from './decorationCatalog.js'
import type { MotionProfile } from './decorationMotion.js'
import type { PaintMaterial } from './paintMaterials.js'
import type { PaletteEntry } from './palette.js'
import { createAvatarRegistry, defaultRegistry, validateAvatarPackSpec, type AvatarOutfitPreset, type AvatarPackSpec, type AvatarRegistry } from './registry.js'

interface BaseComponentItem<S extends string, Z extends string> {
  id: ExternalItemId
  slot: S
  rarity: Rarity
  series: AnySeries
  zones: readonly Z[]
  derivable?: boolean
  meta?: DecorationCopy
  swatchViewBox?: string
}

export interface AvatarHeadComponentItem extends BaseComponentItem<'head', 'top' | 'brow'> {
  motion?: MotionProfile
  render: () => ReactNode
  renderBack?: () => ReactNode
}

export interface AvatarFaceAccessoryComponentItem extends BaseComponentItem<'face', 'brow' | 'rim'> {
  kind: 'accessory'
  render: () => ReactNode
}

export interface AvatarFaceplateComponentItem extends BaseComponentItem<'face', 'screen'> {
  kind: 'faceplate'
  renderPlate: () => ReactNode
  renderEyes: () => ReactNode
}

export interface AvatarHandComponentItem extends BaseComponentItem<'hand', 'hand'> {
  render: () => ReactNode
}

export interface AvatarBackComponentItem extends BaseComponentItem<'back', 'back'> {
  renderBack: () => ReactNode
  renderFront?: () => ReactNode
}

export interface AvatarSkinOverlayComponentItem extends BaseComponentItem<'skin', 'body'> {
  kind: 'overlay'
  renderSurface: () => ReactNode
}

export interface AvatarSkinPaintComponentItem extends BaseComponentItem<'skin', 'body'> {
  kind: 'paint'
  paintMaterial: PaintMaterial
  renderPaint: (paintId: string) => ReactNode
  renderSurface?: () => ReactNode
}

export type AvatarComponentItem =
  | AvatarHeadComponentItem
  | AvatarFaceAccessoryComponentItem
  | AvatarFaceplateComponentItem
  | AvatarHandComponentItem
  | AvatarBackComponentItem
  | AvatarSkinOverlayComponentItem
  | AvatarSkinPaintComponentItem

export interface AvatarComponentPack extends Omit<AvatarPackSpec, 'items'> {
  items: readonly AvatarComponentItem[]
}

export interface AvatarCatalogOptions {
  includeCore?: boolean
  packs?: readonly AvatarComponentPack[]
}

export interface AvatarCatalog {
  readonly registry: AvatarRegistry
  readonly packs: readonly AvatarComponentPack[]
  componentOf(id: string): AvatarComponentItem | undefined
}

export function defineAvatarPack<const P extends AvatarComponentPack>(pack: P): P {
  validateAvatarPackSpec(pack)
  return pack
}

export function createAvatarCatalog(options: AvatarCatalogOptions = {}): AvatarCatalog {
  const packs = options.packs ?? []
  const registry = packs.length === 0 && options.includeCore !== false
    ? defaultRegistry
    : createAvatarRegistry({ includeCore: options.includeCore, packs })
  const components = new Map<string, AvatarComponentItem>()
  for (const pack of packs) {
    for (const item of pack.items) components.set(item.id, item)
  }
  return {
    registry,
    packs,
    componentOf: id => components.get(id),
  }
}

export const defaultAvatarCatalog: AvatarCatalog = createAvatarCatalog()

const AvatarCatalogContext = createContext<AvatarCatalog>(defaultAvatarCatalog)

export function AvatarCatalogProvider({ catalog, children }: { catalog: AvatarCatalog; children: ReactNode }) {
  return <AvatarCatalogContext.Provider value={catalog}>{children}</AvatarCatalogContext.Provider>
}

export function useAvatarCatalog(override?: AvatarCatalog): AvatarCatalog {
  const inherited = useContext(AvatarCatalogContext)
  return override ?? inherited
}
