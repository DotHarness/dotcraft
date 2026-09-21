export const slots = ['head', 'face', 'hand', 'back', 'skin'] as const
export type Slot = typeof slots[number]
export const rarities = ['common', 'uncommon', 'rare', 'epic', 'legendary'] as const
export type Rarity = typeof rarities[number]
export const seriesIds = ['everyday', 'dev', 'tech', 'fantasy', 'nature', 'snack', 'critters'] as const
export type Series = typeof seriesIds[number]
export const zones = ['top', 'brow', 'rim', 'screen', 'hand', 'back', 'body'] as const
export type Zone = typeof zones[number]

export interface ItemSpec<S extends Slot = Slot, I extends string = string> {
  id: I
  slot: S
  rarity: Rarity
  series: Series
  zones: readonly Zone[]
}

const hat = <I extends string>(id: I, rarity: Rarity, series: Series = 'everyday') =>
  ({ id, slot: 'head', rarity, series, zones: ['top', 'brow'] }) as const satisfies ItemSpec<'head', I>
const object = <I extends string>(id: I, rarity: Rarity, series: Series) =>
  ({ id, slot: 'head', rarity, series, zones: ['top'] }) as const satisfies ItemSpec<'head', I>
const brow = <I extends string>(id: I, rarity: Rarity, series: Series) =>
  ({ id, slot: 'face', rarity, series, zones: ['brow'] }) as const satisfies ItemSpec<'face', I>
const faceplate = <I extends string>(id: I, rarity: Rarity, series: Series) =>
  ({ id, slot: 'face', rarity, series, zones: ['screen'] }) as const satisfies ItemSpec<'face', I>
const hand = <I extends string>(id: I, rarity: Rarity, series: Series) =>
  ({ id, slot: 'hand', rarity, series, zones: ['hand'] }) as const satisfies ItemSpec<'hand', I>
const back = <I extends string>(id: I, rarity: Rarity, series: Series) =>
  ({ id, slot: 'back', rarity, series, zones: ['back'] }) as const satisfies ItemSpec<'back', I>
const skin = <I extends string>(id: I, rarity: Rarity, series: Series) =>
  ({ id, slot: 'skin', rarity, series, zones: ['body'] }) as const satisfies ItemSpec<'skin', I>

// Registry order is presentation order only; sampling is weighted by rarity, then uniform.
export const items = [
  hat('baseball-cap', 'common'), hat('bucket-hat', 'common'), hat('beret', 'uncommon'), hat('beanie', 'common'),
  hat('top-hat', 'rare'), hat('wizard-hat', 'rare', 'fantasy'), hat('chef-hat', 'uncommon', 'snack'), hat('party-hat', 'uncommon'),
  hat('crown', 'epic', 'fantasy'), hat('hard-hat', 'common'), hat('nightcap', 'uncommon'), hat('straw-hat', 'common'),
  object('poop', 'uncommon', 'everyday'), object('banana', 'common', 'snack'), object('fried-egg', 'common', 'snack'),
  object('rubber-duck', 'uncommon', 'critters'), object('paper-boat', 'common', 'everyday'), object('traffic-cone', 'common', 'everyday'),
  object('sprout', 'common', 'nature'), object('donut', 'uncommon', 'snack'), object('ringed-planet', 'legendary', 'tech'),
  object('cat-ears', 'common', 'critters'), hat('cowboy-hat', 'common'), object('mushroom', 'common', 'nature'),
  object('shark-fin', 'uncommon', 'critters'), hat('propeller-cap', 'uncommon'), hat('graduation-cap', 'uncommon', 'dev'),
  object('ice-cream', 'uncommon', 'snack'), object('flower-crown', 'rare', 'nature'), hat('pirate-hat', 'rare', 'fantasy'),
  object('lightning', 'epic', 'tech'), object('crystal-cluster', 'epic', 'fantasy'), object('ufo', 'legendary', 'tech'),

  brow('shades', 'common', 'everyday'), brow('forehead-goggles', 'uncommon', 'tech'),
  faceplate('mecha-faceplate', 'rare', 'tech'), faceplate('pixel-screen', 'rare', 'dev'),
  faceplate('gold-faceplate', 'epic', 'tech'), faceplate('neon-visor', 'legendary', 'tech'),

  hand('task-board', 'common', 'dev'), hand('wrench', 'common', 'dev'), hand('coffee-mug', 'common', 'dev'),
  hand('paintbrush', 'common', 'everyday'), hand('boba-tea', 'common', 'snack'),
  hand('shield', 'uncommon', 'dev'), hand('magnifier', 'uncommon', 'dev'), hand('gamepad', 'uncommon', 'dev'), hand('flag', 'uncommon', 'everyday'),
  hand('control-panel', 'rare', 'tech'), hand('lantern', 'rare', 'fantasy'),
  hand('magic-wand', 'epic', 'fantasy'), hand('staff', 'epic', 'fantasy'), hand('energy-blade', 'legendary', 'tech'),

  back('cape', 'common', 'fantasy'), back('crescent-moon', 'common', 'nature'),
  back('jetpack', 'uncommon', 'tech'), back('star-trail', 'uncommon', 'fantasy'), back('balloons', 'uncommon', 'everyday'),
  back('bat-wings', 'rare', 'critters'), back('butterfly-wings', 'rare', 'critters'),
  back('halo', 'epic', 'fantasy'), back('angel-wings', 'epic', 'fantasy'), back('sun-rays', 'epic', 'nature'),
  back('orbit-ring', 'legendary', 'tech'), back('dragon-wings', 'legendary', 'fantasy'),

  skin('stripes', 'common', 'everyday'),
  skin('chrome', 'epic', 'tech'), skin('gold', 'epic', 'fantasy'), skin('lava', 'epic', 'nature'),
  skin('holographic', 'legendary', 'tech'), skin('galaxy', 'legendary', 'fantasy'),
] as const

export type Item = typeof items[number]
export type ItemId = Item['id']
export type ItemOf<S extends Slot> = Extract<Item, { slot: S }>['id']
export type HeadId = ItemOf<'head'>
export type FaceId = ItemOf<'face'>
export type HandId = ItemOf<'hand'>
export type BackId = ItemOf<'back'>
export type SkinId = ItemOf<'skin'>

const byId = new Map<string, Item>(items.map(item => [item.id, item]))
export function itemOf(id: ItemId): Item { return byId.get(id)! }
export function isItemId(id: string): id is ItemId { return byId.has(id) }
export function itemsOf<S extends Slot>(slot: S): Extract<Item, { slot: S }>[] {
  return items.filter((item): item is Extract<Item, { slot: S }> => item.slot === slot)
}
export function slotIds<S extends Slot>(slot: S): ItemOf<S>[] { return itemsOf(slot).map(item => item.id) as ItemOf<S>[] }
export function conflicts(a: ItemId, b: ItemId): boolean {
  const right: readonly Zone[] = itemOf(b).zones
  return itemOf(a).zones.some(zone => right.includes(zone))
}
