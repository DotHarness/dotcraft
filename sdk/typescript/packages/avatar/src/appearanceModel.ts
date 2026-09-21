import { conflicts, itemOf, items, rarities, slots, type BackId, type FaceId, type HandId, type HeadId, type ItemId, type ItemOf, type Rarity, type SkinId, type Slot, type Zone } from './items.js'

export interface Appearance {
  version: 1
  palette: number
  baseFace: number
  head: HeadId | 'none'
  face: FaceId | 'none'
  hand: HandId | 'none'
  back: BackId | 'none'
  skin: SkinId | 'none'
}
export type SlotValue<S extends Slot> = ItemOf<S> | 'none'

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

export function equippedIds(appearance: Appearance): ItemId[] {
  return slots.map(slot => appearance[slot]).filter((id): id is ItemId => id !== 'none')
}
export function occupiedZones(appearance: Appearance): Set<Zone> {
  return new Set(equippedIds(appearance).flatMap(id => [...itemOf(id).zones]))
}
export function canEquip(appearance: Appearance, slot: Slot, id: ItemId | 'none'): boolean {
  if (id === 'none') return true
  return slots.every(other => other === slot || appearance[other] === 'none' || !conflicts(appearance[other] as ItemId, id))
}
export function equip<S extends Slot>(appearance: Appearance, slot: S, id: SlotValue<S>): { appearance: Appearance; cleared: Slot[] } {
  const next: Appearance = { ...appearance, [slot]: id }
  const cleared: Slot[] = []
  if (id !== 'none') for (const other of slots) {
    const current = next[other]
    if (other !== slot && current !== 'none' && conflicts(current, id)) { (next as Record<Slot, string>)[other] = 'none'; cleared.push(other) }
  }
  return { appearance: next, cleared }
}
export function hasConflicts(appearance: Appearance): boolean {
  const ids = equippedIds(appearance)
  return ids.some((a, index) => ids.slice(index + 1).some(b => conflicts(a, b)))
}

export function deriveAppearance(name: string): Appearance {
  const seed = normalizeName(name)
  if (!seed) return { ...originalAppearance }
  const draw = (dimension: string) => hashSeed(JSON.stringify(['dotcraft-avatar', 1, seed, dimension])) / 0x100000000
  const appearance: Appearance = { ...originalAppearance, palette: Math.floor(draw('palette') * 12), baseFace: Math.floor(draw('face') * 5) }
  const occupied = new Set<Zone>()
  for (const slot of slots) {
    if (draw(`${slot}-presence`) >= slotPresence[slot]) continue
    const slotItems = items.filter(item => item.slot === slot)
    const tiers = rarities.filter(rarity => slotItems.some(item => item.rarity === rarity))
    let roll = draw(`${slot}-rarity`) * tiers.reduce((sum, rarity) => sum + rarityWeights[rarity], 0)
    let rarity: Rarity = tiers[tiers.length - 1]
    for (const tier of tiers) { roll -= rarityWeights[tier]; if (roll < 0) { rarity = tier; break } }
    // A tier with no zone-compatible item leaves the slot empty, so rare combinations stay rare.
    const pool = slotItems.filter(item => item.rarity === rarity && item.zones.every(zone => !occupied.has(zone)))
    if (!pool.length) continue
    const pick = pool[Math.floor(draw(`${slot}-item`) * pool.length)]
    ;(appearance as Record<Slot, string>)[slot] = pick.id
    for (const zone of pick.zones) occupied.add(zone)
  }
  return appearance
}
export function sampleSeed(seed: string, round: number, index: number) {
  return JSON.stringify([seed, round, index])
}
export function appearanceWall(seed: string, round = 0) {
  return Array.from({ length: 100 }, (_, index) => {
    const id = sampleSeed(seed, round, index)
    return { id, appearance: deriveAppearance(id) }
  })
}
