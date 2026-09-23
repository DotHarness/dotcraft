import { equip, isItemId, itemOf, items, originalAppearance, rarities, rarityWeights, slots, type Appearance, type ItemId, type Rarity, type Slot } from '@dotcraft/avatar'
import type { PetDropProgress, PetOutfit, PetSettings } from '../../shared/pet'

export const FIND_RUNTIME_MS = 30 * 60_000
export const FIND_TOKEN_GOAL = 1_000_000
export const EXCHANGE_COUNT = 10

export type Random = () => number

function knownItem(id: unknown): id is ItemId {
  return typeof id === 'string' && isItemId(id)
}

type Worn = Pick<Appearance, Slot>

function outfitOf(appearance: Appearance): Worn {
  return { head: appearance.head, face: appearance.face, hand: appearance.hand, back: appearance.back, skin: appearance.skin }
}

function sanitizeOutfit(outfit: PetOutfit): Worn {
  let appearance: Appearance = { ...originalAppearance }
  for (const slot of slots) {
    const id = outfit[slot]
    if (!knownItem(id) || itemOf(id).slot !== slot) continue
    const result = equip(appearance, slot, id)
    if (result.cleared.length === 0) appearance = result.appearance
  }
  return outfitOf(appearance)
}

function ownedOnly(outfit: PetOutfit, bag: PetSettings['bag']): PetOutfit {
  const next = { ...outfit }
  for (const slot of slots) {
    const id = next[slot]
    if (id !== 'none' && !bag[id]) next[slot] = 'none'
  }
  return next
}

export function sanitizePetSettings(settings: PetSettings): PetSettings {
  const bag: PetSettings['bag'] = {}
  for (const [id, count] of Object.entries(settings.bag)) if (knownItem(id)) bag[id] = count
  const last = knownItem(settings.drops.last) ? settings.drops.last : null
  return { ...settings, bag, outfit: ownedOnly(sanitizeOutfit(settings.outfit), bag), drops: { ...settings.drops, last } }
}

export function safeAppearance(appearance: Appearance): Appearance {
  return { ...originalAppearance, palette: appearance.palette, ...sanitizeOutfit(appearance) }
}

export function appearanceOf(settings: PetSettings): Appearance {
  if (!settings.customization) return { ...originalAppearance }
  return { ...originalAppearance, palette: settings.palette, ...(settings.outfit as Worn) }
}

export function findReady(drops: PetDropProgress): boolean {
  return drops.runtimeMs >= FIND_RUNTIME_MS && drops.tokens >= FIND_TOKEN_GOAL
}

function rollRarity(random: Random): Rarity {
  let roll = random() * rarities.reduce((sum, tier) => sum + rarityWeights[tier], 0)
  for (const tier of rarities) { roll -= rarityWeights[tier]; if (roll < 0) return tier }
  return rarities[rarities.length - 1]
}
function pickItem(rarity: Rarity, random: Random): ItemId {
  const pool = items.filter(item => item.rarity === rarity)
  return pool[Math.floor(random() * pool.length)].id
}
export function rollFind(random: Random): ItemId {
  return pickItem(rollRarity(random), random)
}
export function nextRarity(rarity: Rarity): Rarity | null {
  const index = rarities.indexOf(rarity)
  return index < rarities.length - 1 ? rarities[index + 1] : null
}
export function rollExchange(rarity: Rarity, random: Random): ItemId | null {
  const target = nextRarity(rarity)
  return target ? pickItem(target, random) : null
}

function bagAdd(bag: PetSettings['bag'], id: ItemId): PetSettings['bag'] {
  return { ...bag, [id]: (bag[id] ?? 0) + 1 }
}
function bagRemove(bag: PetSettings['bag'], id: ItemId): PetSettings['bag'] {
  const next = { ...bag }
  const left = (next[id] ?? 0) - 1
  if (left > 0) next[id] = left
  else delete next[id]
  return next
}
export function bagEntries(bag: PetSettings['bag']): Array<{ id: ItemId; count: number }> {
  return items.flatMap(item => (bag[item.id] ?? 0) > 0 ? [{ id: item.id, count: bag[item.id]! }] : [])
}
export function bagTotal(bag: PetSettings['bag']): number {
  return bagEntries(bag).reduce((sum, entry) => sum + entry.count, 0)
}
export function isWorn(outfit: PetOutfit, id: ItemId): boolean {
  return outfit[itemOf(id).slot] === id
}
function spareOf(entry: { id: ItemId; count: number }, outfit: PetOutfit): number {
  return entry.count - (isWorn(outfit, entry.id) ? 1 : 0)
}
export function spareTotal(bag: PetSettings['bag'], outfit: PetOutfit, rarity: Rarity): number {
  return bagEntries(bag)
    .filter(entry => itemOf(entry.id).rarity === rarity)
    .reduce((sum, entry) => sum + spareOf(entry, outfit), 0)
}
/** Duplicates go first so single finds stay in the bag when possible. */
export function autoTray(bag: PetSettings['bag'], outfit: PetOutfit, rarity: Rarity): ItemId[] {
  const entries = bagEntries(bag)
    .filter(entry => itemOf(entry.id).rarity === rarity)
    .sort((a, b) => b.count - a.count)
  const tray: ItemId[] = []
  for (const entry of entries) for (let copy = 1; copy < entry.count && tray.length < EXCHANGE_COUNT; copy++) tray.push(entry.id)
  for (const entry of entries) if (!isWorn(outfit, entry.id) && tray.length < EXCHANGE_COUNT) tray.push(entry.id)
  return tray
}
export function trayValid(bag: PetSettings['bag'], outfit: PetOutfit, tray: ItemId[]): boolean {
  if (tray.length !== EXCHANGE_COUNT) return false
  const rarity = itemOf(tray[0]).rarity
  if (!nextRarity(rarity) || tray.some(id => itemOf(id).rarity !== rarity)) return false
  return [...new Set(tray)].every(id => tray.filter(other => other === id).length <= spareOf({ id, count: bag[id] ?? 0 }, outfit))
}

export function wearItem(settings: PetSettings, id: ItemId): PetSettings {
  if (!settings.bag[id]) return settings
  const result = equip(appearanceOf({ ...settings, customization: true }), itemOf(id).slot, id)
  return { ...settings, outfit: outfitOf(result.appearance) }
}
export function wouldClear(settings: PetSettings, id: ItemId): Slot[] {
  return equip(appearanceOf({ ...settings, customization: true }), itemOf(id).slot, id).cleared
}
export function clearSlot(settings: PetSettings, slot: Slot): PetSettings {
  return { ...settings, outfit: { ...settings.outfit, [slot]: 'none' } }
}
export function recordFind(settings: PetSettings, id: ItemId): PetSettings {
  return { ...settings, bag: bagAdd(settings.bag, id), drops: { runtimeMs: 0, tokens: 0, found: settings.drops.found + 1, last: id } }
}
export function exchangeItems(settings: PetSettings, tray: ItemId[], id: ItemId): PetSettings {
  const bag = bagAdd(tray.reduce((left, spent) => bagRemove(left, spent), settings.bag), id)
  return { ...settings, bag, outfit: ownedOnly(settings.outfit, bag) }
}
