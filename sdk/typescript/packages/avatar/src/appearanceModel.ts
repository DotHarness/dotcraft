// Frozen v1 draw order. IDs are serialized; positions are only sampling weights.
export const primaryIds = ['none', 'baseball-cap', 'bucket-hat', 'beret', 'beanie', 'top-hat', 'wizard-hat', 'chef-hat', 'party-hat', 'crown', 'hard-hat', 'nightcap', 'straw-hat', 'poop', 'banana', 'fried-egg', 'rubber-duck', 'paper-boat', 'traffic-cone', 'sprout', 'donut'] as const
export const heldIds = ['task-board', 'wrench', 'shield', 'magnifier', 'control-panel'] as const
export type HeldId = typeof heldIds[number]
export const secondaryIds = ['none', 'forehead-goggles', ...heldIds] as const
export type PrimaryId = typeof primaryIds[number]
export type SecondaryId = typeof secondaryIds[number]
export type DecorationId = Exclude<PrimaryId | SecondaryId, 'none'>
export interface Appearance {
  version: 2
  palette: number
  baseFace: number
  primary: PrimaryId
  secondary: SecondaryId
}

// Explicit allow-list, including the undecorated head. No cartesian-product fallback.
const faceAccessories = ['none', ...heldIds] as const
export const compatibility: Record<PrimaryId, readonly SecondaryId[]> = {
  none: [...faceAccessories, 'forehead-goggles'],
  'baseball-cap': faceAccessories, 'bucket-hat': faceAccessories, beret: faceAccessories,
  beanie: faceAccessories, 'top-hat': faceAccessories, 'wizard-hat': faceAccessories,
  'chef-hat': faceAccessories, 'party-hat': faceAccessories, crown: faceAccessories,
  'hard-hat': faceAccessories, nightcap: faceAccessories, 'straw-hat': faceAccessories,
  poop: [...faceAccessories, 'forehead-goggles'], banana: [...faceAccessories, 'forehead-goggles'],
  'fried-egg': [...faceAccessories, 'forehead-goggles'], 'rubber-duck': [...faceAccessories, 'forehead-goggles'],
  'paper-boat': [...faceAccessories, 'forehead-goggles'], 'traffic-cone': [...faceAccessories, 'forehead-goggles'],
  sprout: [...faceAccessories, 'forehead-goggles'], donut: [...faceAccessories, 'forehead-goggles'],
}
export function isCompatible(primary: PrimaryId, secondary: SecondaryId) {
  return compatibility[primary].includes(secondary)
}
export function withPrimary(appearance: Appearance, primary: PrimaryId): Appearance {
  return { ...appearance, primary, secondary: isCompatible(primary, appearance.secondary) ? appearance.secondary : 'none' }
}
export function hashSeed(seed: string): number {
  let value = 0x811c9dc5
  for (let index = 0; index < seed.length; index++) value = Math.imul(value ^ seed.charCodeAt(index), 0x01000193)
  // Avalanche avoids correlations between adjacent sample IDs and independent dimensions.
  value ^= value >>> 16; value = Math.imul(value, 0x7feb352d)
  value ^= value >>> 15; value = Math.imul(value, 0x846ca68b)
  return (value ^ (value >>> 16)) >>> 0
}
export function isHeld(id: SecondaryId): id is HeldId { return (heldIds as readonly string[]).includes(id) }
export const originalAppearance: Appearance = { version: 2, palette: -1, baseFace: 0, primary: 'none', secondary: 'none' }
export function normalizeName(name: string): string { return name.trim().normalize('NFC') }
export function deriveAppearance(name: string): Appearance {
  const seed = normalizeName(name)
  if (!seed) return { ...originalAppearance }
  const draw = (dimension: string) => hashSeed(JSON.stringify(['dotcraft-avatar', 1, seed, dimension])) / 0x100000000
  const primary = primaryIds[Math.floor(draw('primary') * primaryIds.length)]
  const choices = compatibility[primary].filter(id => id !== 'none')
  const secondaryDraw = (dimension: string) => hashSeed(JSON.stringify(['dotcraft-avatar', 2, seed, dimension])) / 0x100000000
  const secondary = secondaryDraw('secondary-presence') < .5 ? 'none' : choices[Math.floor(secondaryDraw('secondary-item') * choices.length)]
  return { version: 2, palette: Math.floor(draw('palette') * 12), baseFace: Math.floor(draw('face') * 5), primary, secondary }
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
