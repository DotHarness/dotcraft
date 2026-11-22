import { conflicts, rarities, slots, type AnyItemId, type ItemId, type Rarity, type Slot, type SlotItemId, type Zone } from './items.js'
import { defaultRegistry, type AvatarOutfitPreset, type AvatarRegistry } from './registry.js'

export interface Appearance {
  version: 1
  palette: number
  baseFace: number
  head: SlotItemId<'head'> | 'none'
  face: SlotItemId<'face'> | 'none'
  hand: SlotItemId<'hand'> | 'none'
  back: SlotItemId<'back'> | 'none'
  skin: SlotItemId<'skin'> | 'none'
  antenna?: 'ball' | 'star'
}
export type SlotValue<S extends Slot> = SlotItemId<S> | 'none'

export const slotPresence: Record<Slot, number> = { head: .85, face: .35, hand: .4, back: .25, skin: .3 }
export const rarityWeights: Record<Rarity, number> = { common: 55, uncommon: 27, rare: 12, epic: 5, legendary: 1 }

export function hashSeed(seed: string): number {
  let value = 0x811c9dc5
  for (let index = 0; index < seed.length; index++) value = Math.imul(value ^ seed.charCodeAt(index), 0x01000193)
  // Avalanche avoids correlations between adjacent sample IDs and independent dimensions.
  value ^= value >>> 16; value = Math.imul(value, 0x7feb352d)
  value ^= value >>> 15; value = Math.imul(value, 0x846ca68b)
  return (value ^ (value >>> 16)) >>> 0
}
export const originalAppearance: Appearance = { version: 1, palette: -1, baseFace: 0, head: 'none', face: 'none', hand: 'none', back: 'none', skin: 'none' }
export function normalizeName(name: string): string { return name.trim().normalize('NFC') }

export function equippedIds(appearance: Appearance): ItemId[]
export function equippedIds(appearance: Appearance, registry: AvatarRegistry): AnyItemId[]
export function equippedIds(appearance: Appearance, registry: AvatarRegistry = defaultRegistry): AnyItemId[] {
  return slots.map(slot => appearance[slot]).filter((id): id is AnyItemId => id !== 'none' && registry.has(id))
}
export function occupiedZones(appearance: Appearance, registry: AvatarRegistry = defaultRegistry): Set<Zone> {
  return new Set(equippedIds(appearance, registry).flatMap(id => [...(registry.get(id)?.zones ?? [])]))
}
export function canEquip<S extends Slot>(appearance: Appearance, slot: S, id: SlotValue<S>, registry: AvatarRegistry = defaultRegistry): boolean {
  if (id === 'none') return true
  const candidate = registry.get(id)
  if (!candidate || candidate.slot !== slot) return false
  return slots.every(other => other === slot || appearance[other] === 'none' || !conflicts(appearance[other] as AnyItemId, id, registry))
}
export function equip<S extends Slot>(appearance: Appearance, slot: S, id: SlotValue<S>, registry: AvatarRegistry = defaultRegistry): { appearance: Appearance; cleared: Slot[] } {
  if (id !== 'none') {
    const candidate = registry.get(id)
    if (!candidate || candidate.slot !== slot) throw new TypeError(`Cannot equip ${id} in the ${slot} slot`)
  }
  const next: Appearance = { ...appearance, [slot]: id }
  const cleared: Slot[] = []
  if (id !== 'none') for (const other of slots) {
    const current = next[other]
    if (other !== slot && current !== 'none' && conflicts(current, id, registry)) {
      ;(next as unknown as Record<Slot, string>)[other] = 'none'
      cleared.push(other)
    }
  }
  return { appearance: next, cleared }
}
export function hasConflicts(appearance: Appearance, registry: AvatarRegistry = defaultRegistry): boolean {
  const ids = equippedIds(appearance, registry)
  return ids.some((a, index) => ids.slice(index + 1).some(b => conflicts(a, b, registry)))
}

export function normalizeAppearance(appearance: Appearance, registry: AvatarRegistry = defaultRegistry): Appearance {
  let next: Appearance = {
    version: 1,
    palette: appearance.palette,
    baseFace: appearance.baseFace,
    head: 'none',
    face: 'none',
    hand: 'none',
    back: 'none',
    skin: 'none',
    ...(appearance.antenna ? { antenna: appearance.antenna } : {}),
  }
  for (const slot of slots) {
    const id = appearance[slot]
    if (id === 'none') continue
    const item = registry.get(id)
    if (!item || item.slot !== slot) continue
    const result = equip(next, slot, id, registry)
    if (result.cleared.length === 0) next = result.appearance
  }
  return next
}

export function applyOutfitPreset(preset: AvatarOutfitPreset, base: Appearance = originalAppearance, registry: AvatarRegistry = defaultRegistry): Appearance {
  let next: Appearance = {
    ...base,
    version: 1,
    palette: preset.palette,
    baseFace: preset.baseFace ?? base.baseFace,
    head: 'none',
    face: 'none',
    hand: 'none',
    back: 'none',
    skin: 'none',
  }
  if (preset.antenna) next.antenna = preset.antenna
  else delete next.antenna
  for (const slot of slots) {
    const value = preset[slot]
    if (value && value !== 'none') next = equip(next, slot, value, registry).appearance
  }
  return next
}

export function deriveAppearance(name: string, registry: AvatarRegistry = defaultRegistry): Appearance {
  const seed = normalizeName(name)
  if (!seed) return { ...originalAppearance }
  const draw = (dimension: string) => hashSeed(JSON.stringify(['dotcraft-avatar', 1, seed, dimension])) / 0x100000000
  const appearance: Appearance = { ...originalAppearance, palette: Math.floor(draw('palette') * 12), baseFace: Math.floor(draw('face') * 5) }
  const occupied = new Set<Zone>()
  for (const slot of slots) {
    if (draw(`${slot}-presence`) >= slotPresence[slot]) continue
    const slotItems = registry.itemsOf(slot, { derivableOnly: true })
    const tiers = rarities.filter(rarity => slotItems.some(item => item.rarity === rarity))
    if (!tiers.length) continue
    let roll = draw(`${slot}-rarity`) * tiers.reduce((sum, rarity) => sum + rarityWeights[rarity], 0)
    let rarity: Rarity = tiers[tiers.length - 1]
    for (const tier of tiers) { roll -= rarityWeights[tier]; if (roll < 0) { rarity = tier; break } }
    // A tier with no zone-compatible item leaves the slot empty, so rare combinations stay rare.
    const pool = slotItems.filter(item => item.rarity === rarity && item.zones.every(zone => !occupied.has(zone)))
    if (!pool.length) continue
    const pick = pool[Math.floor(draw(`${slot}-item`) * pool.length)]
    ;(appearance as unknown as Record<Slot, string>)[slot] = pick.id
    for (const zone of pick.zones) occupied.add(zone)
  }
  return appearance
}
export function sampleSeed(seed: string, round: number, index: number) {
  return JSON.stringify([seed, round, index])
}
export function appearanceWall(seed: string, round = 0, registry: AvatarRegistry = defaultRegistry) {
  return Array.from({ length: 100 }, (_, index) => {
    const id = sampleSeed(seed, round, index)
    return { id, appearance: deriveAppearance(id, registry) }
  })
}
