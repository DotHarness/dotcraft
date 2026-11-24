import { items, seriesIds, slots, type AnyItemId, type AnySeries, type ExternalItemId, type ItemId, type ItemSpec, type Rarity, type Series, type Slot, type SlotItemId, type Zone } from './items.js'
import { coreDecorationCopy, seriesLabels, type DecorationCopy } from './decorationCatalog.js'
import { paintMaterialOf, type PaintMaterial } from './paintMaterials.js'
import type { PaletteEntry } from './palette.js'

export interface AvatarItemSpec<S extends Slot = Slot, I extends string = AnyItemId> extends ItemSpec<S, I> {
  derivable?: boolean
  paintMaterial?: PaintMaterial
  meta?: DecorationCopy
  swatchViewBox?: string
}

export interface AvatarOutfitPreset {
  id: string
  name: string
  description?: string
  palette: number
  baseFace?: number
  antenna?: 'ball' | 'star'
  head?: SlotItemId<'head'> | 'none'
  face?: SlotItemId<'face'> | 'none'
  hand?: SlotItemId<'hand'> | 'none'
  back?: SlotItemId<'back'> | 'none'
  skin?: SlotItemId<'skin'> | 'none'
}

export interface AvatarPackSpec {
  namespace: string
  label: string
  derivable?: boolean
  seriesLabels?: Readonly<Record<string, string>>
  palettes?: Readonly<Record<number, PaletteEntry>>
  items: readonly AvatarItemSpec[]
  presets?: readonly AvatarOutfitPreset[]
}

export interface AvatarRegistryOptions {
  includeCore?: boolean
  packs?: readonly AvatarPackSpec[]
}

export interface AvatarRegistry {
  readonly items: readonly AvatarItemSpec[]
  readonly packs: readonly AvatarPackSpec[]
  readonly presets: readonly AvatarOutfitPreset[]
  readonly seriesIds: readonly AnySeries[]
  has(id: string): boolean
  get(id: string): AvatarItemSpec | undefined
  itemsOf<S extends Slot>(slot: S, options?: { derivableOnly?: boolean }): AvatarItemSpec<S>[]
  paletteOf(palette: number): PaletteEntry | undefined
  paintMaterialOf(skin: string): PaintMaterial | undefined
  seriesLabelOf(series: string): string
  decorationOf(id: string): (AvatarItemSpec & DecorationCopy) | undefined
}

const coreSwatchViewBoxes: Partial<Record<ItemId, string>> = {
  'vocal-twintails': '26 140 972 780',
  'twin-tails': '150 135 724 680',
  'sheriff-star': '396 720 232 232',
}

const KEBAB_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/
const ALLOWED_ZONES: Record<Slot, readonly Zone[]> = {
  head: ['top', 'brow'],
  face: ['brow', 'rim', 'screen'],
  hand: ['hand'],
  back: ['back'],
  skin: ['body'],
}

function validateZones(id: string, slot: Slot, itemZones: readonly Zone[]): void {
  if (!itemZones.length) throw new TypeError(`Avatar item ${id} must declare at least one zone`)
  const allowed = ALLOWED_ZONES[slot]
  for (const zone of itemZones) {
    if (!allowed.includes(zone)) throw new TypeError(`Avatar item ${id} in slot ${slot} cannot occupy zone ${zone}`)
  }
  if (slot === 'head' && !itemZones.includes('top')) throw new TypeError(`Head item ${id} must occupy top`)
  if (slot === 'face' && itemZones.includes('screen') && itemZones.length > 1) {
    throw new TypeError(`Faceplate item ${id} cannot combine screen with other zones`)
  }
}

export function validateAvatarPackSpec(pack: AvatarPackSpec): void {
  if (!KEBAB_PATTERN.test(pack.namespace)) {
    throw new TypeError(`Avatar pack namespace "${pack.namespace}" must be kebab-case`)
  }
  const prefix = `${pack.namespace}:`
  const seen = new Set<string>()
  for (const item of pack.items) {
    if (!item.id.startsWith(prefix) || !KEBAB_PATTERN.test(item.id.slice(prefix.length))) {
      throw new TypeError(`Avatar item "${item.id}" must be prefixed with "${prefix}" and kebab-case`)
    }
    if (seen.has(item.id)) throw new TypeError(`Duplicate avatar item "${item.id}" in pack "${pack.namespace}"`)
    seen.add(item.id)
    if (!slots.includes(item.slot)) throw new TypeError(`Invalid slot "${String(item.slot)}" on "${item.id}"`)
    validateZones(item.id, item.slot, item.zones)
    if (item.paintMaterial && item.slot !== 'skin') {
      throw new TypeError(`Only skin items may declare paintMaterial ("${item.id}")`)
    }
  }
  if (pack.palettes) {
    for (const rawKey of Object.keys(pack.palettes)) {
      const key = Number(rawKey)
      if (!Number.isInteger(key) || key > -3) {
        throw new TypeError(`Pack palette index ${rawKey} in "${pack.namespace}" must be an integer <= -3`)
      }
    }
  }
}

export const coreOutfitPresets: readonly AvatarOutfitPreset[] = [
  {
    id: 'vocal-synth',
    name: 'Vocal Synth Diva',
    description: 'Legendary cyan-teal vocal twin-tails with holo notes, starry visor, sailor collar, and stage mic.',
    palette: 10,
    baseFace: 1,
    head: 'vocal-twintails',
    face: 'starry-visor',
    hand: 'stage-mic',
    back: 'none',
    skin: 'sailor-collar',
  },
  {
    id: 'starlight-idol',
    name: 'Starlight Idol',
    description: 'Sakura twin-tails, starry anime visor, concert glow stick, sailor collar, and back waist ribbon.',
    palette: 4,
    baseFace: 1,
    head: 'twin-tails',
    face: 'starry-visor',
    hand: 'glow-stick',
    back: 'waist-ribbon',
    skin: 'sailor-collar',
  },
  {
    id: 'plush-bunny',
    name: 'Plush Bunny Kigurumi',
    description: 'Cozy bunny hood, pastel fleece costume suit, ribbon choker, and stage mic.',
    palette: 5,
    baseFace: 1,
    head: 'plush-bunny-hood',
    face: 'ribbon-choker',
    hand: 'stage-mic',
    back: 'none',
    skin: 'plush-fleece',
  },
  {
    id: 'dino-onesie',
    name: 'Dino Onesie',
    description: 'Mint dinosaur hood with spikes, candy necklace, boba tea, and dragon tail.',
    palette: 9,
    baseFace: 2,
    head: 'dino-hood',
    face: 'candy-necklace',
    hand: 'boba-tea',
    back: 'dragon-tail',
    skin: 'hoops',
  },
]

function fallbackCopy(item: AvatarItemSpec): DecorationCopy {
  const rawName = item.id.includes(':') ? item.id.split(':')[1]! : item.id
  return {
    name: rawName.replace(/-/g, ' ').replace(/^\w/, char => char.toUpperCase()),
    feature: `${item.rarity} ${item.slot} item`,
    colors: item.paintMaterial ? [item.paintMaterial.accent, item.paintMaterial.shadow] : ['#4f7cf6'],
    mount: `Mounts in the ${item.slot} slot`,
  }
}

export function createAvatarRegistry(options: AvatarRegistryOptions = {}): AvatarRegistry {
  const includeCore = options.includeCore !== false
  const packs = options.packs ?? []
  const allItems: AvatarItemSpec[] = []
  const byId = new Map<string, AvatarItemSpec>()
  const palettes = new Map<number, PaletteEntry>()
  const customSeriesLabels = new Map<string, string>(Object.entries(seriesLabels))
  const allSeries = new Set<AnySeries>(includeCore ? seriesIds : [])
  const presets: AvatarOutfitPreset[] = includeCore ? [...coreOutfitPresets] : []

  if (includeCore) {
    for (const item of items) {
      const normalized: AvatarItemSpec = {
        ...item,
        derivable: true,
        paintMaterial: item.slot === 'skin' ? paintMaterialOf(item.id) : undefined,
        meta: coreDecorationCopy[item.id],
        swatchViewBox: coreSwatchViewBoxes[item.id],
      }
      allItems.push(normalized)
      byId.set(normalized.id, normalized)
    }
  }

  for (const pack of packs) {
    validateAvatarPackSpec(pack)
    const packDerivable = pack.derivable ?? false
    if (pack.seriesLabels) {
      for (const [key, label] of Object.entries(pack.seriesLabels)) {
        const seriesKey = (key.includes(':') ? key : `${pack.namespace}:${key}`) as ExternalItemId
        customSeriesLabels.set(seriesKey, label)
        customSeriesLabels.set(key, label)
      }
    }
    if (pack.palettes) {
      for (const [rawKey, entry] of Object.entries(pack.palettes)) {
        const key = Number(rawKey)
        if (palettes.has(key)) throw new TypeError(`Duplicate palette index ${key} registered by "${pack.namespace}"`)
        palettes.set(key, entry)
      }
    }
    for (const item of pack.items) {
      if (byId.has(item.id)) throw new TypeError(`Avatar item "${item.id}" is already registered`)
      const normalized: AvatarItemSpec = { ...item, derivable: item.derivable ?? packDerivable }
      allItems.push(normalized)
      byId.set(normalized.id, normalized)
      allSeries.add(normalized.series)
    }
    if (pack.presets) presets.push(...pack.presets)
  }

  return {
    items: allItems,
    packs,
    presets,
    seriesIds: [...allSeries],
    has: id => byId.has(id),
    get: id => byId.get(id),
    itemsOf<S extends Slot>(slot: S, filter?: { derivableOnly?: boolean }) {
      return allItems.filter((item): item is AvatarItemSpec<S> =>
        item.slot === slot && (!filter?.derivableOnly || item.derivable !== false),
      )
    },
    paletteOf: palette => palettes.get(palette),
    paintMaterialOf: skin => byId.get(skin)?.paintMaterial ?? paintMaterialOf(skin),
    seriesLabelOf: series => customSeriesLabels.get(series) ?? series,
    decorationOf(id: string) {
      const item = byId.get(id)
      return item ? { ...item, ...(item.meta ?? fallbackCopy(item)) } : undefined
    },
  }
}

export const defaultRegistry: AvatarRegistry = createAvatarRegistry()
export type { Series }
