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
const rim = <I extends string>(id: I, rarity: Rarity, series: Series) =>
  ({ id, slot: 'face', rarity, series, zones: ['rim'] }) as const satisfies ItemSpec<'face', I>
const faceplate = <I extends string>(id: I, rarity: Rarity, series: Series) =>
  ({ id, slot: 'face', rarity, series, zones: ['screen'] }) as const satisfies ItemSpec<'face', I>
const hand = <I extends string>(id: I, rarity: Rarity, series: Series) =>
  ({ id, slot: 'hand', rarity, series, zones: ['hand'] }) as const satisfies ItemSpec<'hand', I>
const back = <I extends string>(id: I, rarity: Rarity, series: Series) =>
  ({ id, slot: 'back', rarity, series, zones: ['back'] }) as const satisfies ItemSpec<'back', I>
const skin = <I extends string>(id: I, rarity: Rarity, series: Series) =>
  ({ id, slot: 'skin', rarity, series, zones: ['body'] }) as const satisfies ItemSpec<'skin', I>

// Registry order is presentation order and also decides which item a name draws within its rarity.
export const items = [
  hat('baseball-cap', 'common'), hat('bucket-hat', 'common'), hat('beret', 'uncommon'), hat('beanie', 'common'),
  hat('top-hat', 'rare'), hat('wizard-hat', 'rare', 'fantasy'), hat('chef-hat', 'uncommon', 'snack'), hat('party-hat', 'uncommon'),
  hat('crown', 'epic', 'fantasy'), hat('hard-hat', 'common'), hat('nightcap', 'uncommon'), hat('straw-hat', 'common'),
  object('poop', 'uncommon', 'everyday'), object('banana', 'common', 'snack'), object('fried-egg', 'common', 'snack'),
  object('rubber-duck', 'uncommon', 'critters'), object('paper-boat', 'common', 'everyday'), object('traffic-cone', 'common', 'everyday'),
  object('sprout', 'common', 'nature'), object('donut', 'uncommon', 'snack'), object('ringed-planet', 'legendary', 'tech'),
  object('cat-ears', 'common', 'critters'), hat('cowboy-hat', 'common'), object('mushroom', 'common', 'nature'),
  object('shark-fin', 'uncommon', 'critters'), hat('propeller-cap', 'uncommon'), hat('graduation-cap', 'uncommon', 'dev'),
  object('ice-cream', 'uncommon', 'snack'), object('floppy-disk', 'uncommon', 'dev'), object('flower-crown', 'rare', 'nature'), hat('pirate-hat', 'rare', 'fantasy'),
  object('rocket', 'rare', 'dev'), object('lightning', 'epic', 'tech'), object('crystal-cluster', 'epic', 'fantasy'), object('ufo', 'legendary', 'tech'),
  object('mini-volcano', 'legendary', 'nature'), object('phoenix-perch', 'legendary', 'fantasy'),
  object('satellite-dish', 'common', 'tech'), object('cursor-arrow', 'common', 'dev'), hat('viking-helmet', 'uncommon', 'fantasy'),
  object('wind-up-key', 'rare', 'tech'), object('crab', 'rare', 'critters'), object('cupcake', 'rare', 'snack'),
  object('gears', 'epic', 'dev'), object('rain-cloud', 'epic', 'nature'), object('maneki-neko', 'legendary', 'critters'),

  brow('shades', 'common', 'everyday'), brow('headlamp', 'common', 'nature'),
  brow('forehead-goggles', 'uncommon', 'tech'), brow('heart-glasses', 'uncommon', 'everyday'), { ...brow('snorkel-mask', 'uncommon', 'nature'), zones: ['brow', 'rim'] },
  brow('watermelon-glasses', 'uncommon', 'snack'), brow('star-glasses', 'rare', 'everyday'),
  rim('bow-tie', 'common', 'everyday'), rim('neckerchief', 'common', 'everyday'), rim('ear-pencil', 'common', 'dev'), rim('sticky-note', 'common', 'everyday'),
  rim('bell-collar', 'uncommon', 'critters'), rim('lanyard-badge', 'uncommon', 'dev'), rim('prize-rosette', 'uncommon', 'everyday'), rim('cherry-charm', 'uncommon', 'snack'),
  rim('songbird', 'rare', 'critters'), rim('amulet', 'epic', 'fantasy'),
  faceplate('mecha-faceplate', 'rare', 'tech'), faceplate('pixel-screen', 'rare', 'dev'), faceplate('segment-display', 'rare', 'dev'), faceplate('knight-visor', 'rare', 'fantasy'),
  faceplate('gold-faceplate', 'epic', 'tech'), faceplate('porthole-helmet', 'epic', 'nature'), faceplate('neon-visor', 'legendary', 'tech'), faceplate('dragon-visor', 'legendary', 'fantasy'),
  faceplate('code-rain', 'legendary', 'dev'),

  hand('task-board', 'common', 'dev'), hand('wrench', 'common', 'dev'), hand('coffee-mug', 'common', 'dev'),
  hand('paintbrush', 'common', 'everyday'), hand('boba-tea', 'common', 'snack'),
  hand('umbrella', 'common', 'everyday'), hand('pizza-slice', 'common', 'snack'), hand('sunflower', 'common', 'nature'), hand('camera', 'common', 'everyday'),
  hand('walkie-talkie', 'common', 'tech'), hand('potion', 'common', 'fantasy'),
  hand('shield', 'uncommon', 'dev'), hand('magnifier', 'uncommon', 'dev'), hand('gamepad', 'uncommon', 'dev'), hand('flag', 'uncommon', 'everyday'),
  hand('binoculars', 'uncommon', 'nature'), hand('goldfish-bag', 'uncommon', 'critters'), hand('megaphone', 'uncommon', 'everyday'),
  hand('genie-lamp', 'uncommon', 'fantasy'), hand('lollipop', 'uncommon', 'snack'),
  hand('control-panel', 'rare', 'tech'), hand('lantern', 'rare', 'fantasy'), hand('parrot', 'rare', 'critters'), hand('firefly-jar', 'rare', 'nature'),
  hand('pickaxe', 'rare', 'everyday'), hand('bug-net', 'rare', 'dev'),
  hand('magic-wand', 'epic', 'fantasy'), hand('staff', 'epic', 'fantasy'), hand('trophy', 'epic', 'everyday'), hand('hot-pepper', 'epic', 'snack'), hand('plasma-globe', 'epic', 'tech'),
  hand('energy-blade', 'legendary', 'tech'), hand('thunder-hammer', 'legendary', 'tech'), hand('master-key', 'legendary', 'dev'), hand('pet-dragon', 'legendary', 'fantasy'),

  back('cape', 'common', 'fantasy'), back('crescent-moon', 'common', 'nature'),
  back('kite', 'common', 'everyday'), back('solar-panels', 'common', 'tech'), back('power-cord', 'common', 'dev'), back('surfboard', 'common', 'nature'),
  back('peeking-cat', 'common', 'critters'),
  back('jetpack', 'uncommon', 'tech'), back('star-trail', 'uncommon', 'fantasy'), back('balloons', 'uncommon', 'everyday'),
  back('fox-tail', 'uncommon', 'critters'), back('hero-scarf', 'uncommon', 'everyday'), back('guitar', 'uncommon', 'everyday'),
  back('donut-floatie', 'uncommon', 'snack'), back('ladybug', 'uncommon', 'critters'),
  back('bat-wings', 'rare', 'critters'), back('butterfly-wings', 'rare', 'critters'), back('comet', 'rare', 'tech'),
  back('dragon-tail', 'rare', 'fantasy'), back('drone-buddy', 'rare', 'tech'), back('shade-tree', 'rare', 'nature'),
  back('halo', 'epic', 'fantasy'), back('angel-wings', 'epic', 'fantasy'), back('sun-rays', 'epic', 'nature'), back('koi-orbit', 'epic', 'critters'),
  back('tesla-coils', 'epic', 'tech'),
  back('orbit-ring', 'legendary', 'tech'), back('dragon-wings', 'legendary', 'fantasy'), back('twin-blades', 'legendary', 'fantasy'),
  back('great-wave', 'legendary', 'nature'), back('cloud-dragon', 'legendary', 'critters'),

  skin('stripes', 'common', 'everyday'), skin('split', 'common', 'everyday'), skin('hoops', 'common', 'everyday'),
  skin('sunburst', 'uncommon', 'nature'), skin('dipped', 'uncommon', 'everyday'), skin('quartered', 'uncommon', 'everyday'), skin('bowtie', 'uncommon', 'everyday'),
  skin('pinwheel', 'rare', 'everyday'), skin('sonar', 'rare', 'tech'), skin('tide', 'rare', 'nature'),
  skin('chrome', 'epic', 'tech'), skin('gold', 'epic', 'fantasy'), skin('lava', 'epic', 'nature'),
  skin('bumblebee', 'epic', 'critters'), skin('aurora', 'epic', 'nature'), skin('terminal', 'epic', 'dev'), skin('patina', 'epic', 'nature'),
  skin('candy', 'epic', 'everyday'), skin('racer', 'epic', 'everyday'),
  skin('holographic', 'legendary', 'tech'), skin('galaxy', 'legendary', 'fantasy'), skin('thermal', 'legendary', 'tech'),
  skin('prism', 'legendary', 'tech'), skin('void', 'legendary', 'fantasy'),
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
