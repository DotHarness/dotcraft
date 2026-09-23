import type { ItemId, Slot } from '@dotcraft/avatar'

export type PetOutfit = Record<Slot, ItemId | 'none'>

export interface PetDropProgress {
  runtimeMs: number
  tokens: number
  found: number
  last: ItemId | null
}

export interface PetSettings {
  customization: boolean
  /** -1 keeps the original brand paint; 0–11 pick a role palette. */
  palette: number
  outfit: PetOutfit
  /** Copies owned per item, including the worn one. */
  bag: Partial<Record<ItemId, number>>
  drops: PetDropProgress
}

export const PET_SLOTS: readonly Slot[] = ['head', 'face', 'hand', 'back', 'skin']
const PET_PALETTE_MIN = -1
const PET_PALETTE_MAX = 11
const BAG_MAX_ENTRIES = 500
const BAG_MAX_COUNT = 9999
/** Ids are validated against the registry by the renderer; main only keeps the shape sane. */
const ID_PATTERN = /^[a-z0-9-]{1,40}$/

const EMPTY_PET_OUTFIT: PetOutfit = { head: 'none', face: 'none', hand: 'none', back: 'none', skin: 'none' }
export const DEFAULT_PET_SETTINGS: PetSettings = {
  customization: true,
  palette: PET_PALETTE_MIN,
  outfit: EMPTY_PET_OUTFIT,
  bag: {},
  drops: { runtimeMs: 0, tokens: 0, found: 0, last: null }
}

function record(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === 'object' && !Array.isArray(value) ? (value as Record<string, unknown>) : null
}

function itemId(value: unknown): ItemId | null {
  return typeof value === 'string' && ID_PATTERN.test(value) ? (value as ItemId) : null
}

function wholeNumber(value: unknown, max = Number.MAX_SAFE_INTEGER): number {
  if (typeof value !== 'number' || !Number.isFinite(value) || value < 0) return 0
  return Math.min(max, Math.floor(value))
}

function normalizePetPalette(value: unknown): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) return PET_PALETTE_MIN
  return Math.min(PET_PALETTE_MAX, Math.max(PET_PALETTE_MIN, Math.round(value)))
}

function normalizePetOutfit(raw: unknown): PetOutfit {
  const source = record(raw)
  const outfit: PetOutfit = { ...EMPTY_PET_OUTFIT }
  if (!source) return outfit
  for (const slot of PET_SLOTS) {
    const id = itemId(source[slot])
    if (id) outfit[slot] = id
  }
  return outfit
}

function normalizePetBag(raw: unknown): PetSettings['bag'] {
  const source = record(raw)
  const bag: PetSettings['bag'] = {}
  if (!source) return bag
  let entries = 0
  for (const [key, value] of Object.entries(source)) {
    const id = itemId(key)
    const count = wholeNumber(value, BAG_MAX_COUNT)
    if (!id || count === 0) continue
    bag[id] = count
    if (++entries >= BAG_MAX_ENTRIES) break
  }
  return bag
}

function normalizePetDrops(raw: unknown): PetDropProgress {
  const source = record(raw)
  if (!source) return { ...DEFAULT_PET_SETTINGS.drops }
  return {
    runtimeMs: wholeNumber(source.runtimeMs),
    tokens: wholeNumber(source.tokens),
    found: wholeNumber(source.found),
    last: itemId(source.last)
  }
}

export function resolvePetSettings(raw: unknown): PetSettings {
  const source = record(raw) ?? {}
  return {
    customization: source.customization !== false,
    palette: normalizePetPalette(source.palette),
    outfit: normalizePetOutfit(source.outfit),
    bag: normalizePetBag(source.bag),
    drops: normalizePetDrops(source.drops)
  }
}

export function normalizePetSetting(raw: unknown): PetSettings | undefined {
  const settings = resolvePetSettings(raw)
  return JSON.stringify(settings) === JSON.stringify(DEFAULT_PET_SETTINGS) ? undefined : settings
}
