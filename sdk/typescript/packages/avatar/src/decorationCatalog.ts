import { itemOf, items, rarities, seriesIds, slots, type Item, type ItemId, type Rarity, type Series, type Slot } from './items.js'

export interface DecorationCopy {
  name: string
  feature: string
  colors: readonly string[]
  mount: string
}
export type Decoration = Item & DecorationCopy

export const slotLabels: Record<Slot, string> = { head: 'Head', face: 'Face', hand: 'Hand', back: 'Back', skin: 'Skin' }
export const rarityMeta: Record<Rarity, { label: string; color: string }> = {
  common: { label: 'Common', color: '#8a94a6' },
  uncommon: { label: 'Uncommon', color: '#3f9d5b' },
  rare: { label: 'Rare', color: '#3b7be8' },
  epic: { label: 'Epic', color: '#8b5cf6' },
  legendary: { label: 'Legendary', color: '#e0962c' },
}
export const seriesLabels: Record<Series, string> = {
  everyday: 'Everyday', dev: 'Dev culture', tech: 'Tech & cyber', fantasy: 'Fantasy', nature: 'Nature', snack: 'Snack', critters: 'Critters',
}

const held = 'Screen-left hand; shares the arm pivot, stows for laptop and question sign, then restores'
const copy: Record<ItemId, DecorationCopy> = {
  'baseball-cap': { name: 'Baseball cap', feature: 'Rounded crown with a short right-facing brim', colors: ['#e87967', '#b94f50', '#f5a18c'], mount: 'Crown overlaps the top edge; the brim stays inside the body' },
  'bucket-hat': { name: 'Bucket hat', feature: 'Flat top with a continuous downward brim', colors: ['#94bda6', '#5b887a', '#bed8c1'], mount: 'Brim follows the top edge with a slight center overlap' },
  beret: { name: 'Beret', feature: 'Soft left-leaning crown with a short stem', colors: ['#b17498', '#784c71', '#cf9db5'], mount: 'Narrow band rests on top; the crown leans left' },
  beanie: { name: 'Beanie', feature: 'Rounded crown and wide folded cuff with minimal texture', colors: ['#e9a653', '#bc753d', '#f7c676'], mount: 'Folded cuff sits on the top edge; crown stays centered' },
  'top-hat': { name: 'Top hat', feature: 'Tall crown, narrow brim, and burgundy band', colors: ['#465069', '#293247', '#c96e85'], mount: 'Narrow brim rests flat on top; crown tapers at its base' },
  'wizard-hat': { name: 'Wizard hat', feature: 'Curved point, wide brim, and gold star', colors: ['#8773c1', '#594b93', '#f4cc75'], mount: 'Wide brim rests on top; point curves into the center safe area' },
  'chef-hat': { name: 'Chef hat', feature: 'Three-lobed puff with an upright band', colors: ['#f5eee3', '#d7caba', '#fffaf1'], mount: 'Band rests at the center of the top edge' },
  'party-hat': { name: 'Party hat', feature: 'Cone with two colored bands and a pom-pom', colors: ['#ea9baf', '#8075b7', '#f5d07b'], mount: 'Shallow curved base follows the top of the body' },
  crown: { name: 'Crown', feature: 'Three rounded points with a central sapphire', colors: ['#efc65c', '#c99139', '#73b8cc'], mount: 'Curved gold band wraps the top edge; all points face up' },
  'hard-hat': { name: 'Hard hat', feature: 'Rounded shell, center ridge, and short front brim', colors: ['#f5bd48', '#d68d32', '#ffdc79'], mount: 'Short brim overlaps the top edge; ridge stays centered' },
  nightcap: { name: 'Nightcap', feature: 'Soft drooping point with a pom-pom', colors: ['#789cc9', '#4c709f', '#dce8f4'], mount: 'Soft band follows the top edge; pom-pom stays upper right' },
  'straw-hat': { name: 'Straw hat', feature: 'Low round crown, wide oval brim, and red band', colors: ['#e8c78a', '#c49b5e', '#c57568'], mount: 'Wide brim rests flat without entering the arm areas' },
  poop: { name: 'Poop', feature: 'Three rounded layers with a playful curled tip', colors: ['#a97b5f', '#805840', '#c49976'], mount: 'Wide bottom layer rests directly on top; color separates each layer' },
  banana: { name: 'Banana', feature: 'Crescent fruit with stems and an inner color plane', colors: ['#f3cf62', '#dbaa40', '#89705a'], mount: 'Outer curve touches the top; both stems point up' },
  'fried-egg': { name: 'Fried egg', feature: 'Irregular rounded white with a raised golden yolk', colors: ['#fff9e8', '#e6d9bb', '#f3be43'], mount: 'White spreads across the top; raised yolk defines the silhouette' },
  'rubber-duck': { name: 'Rubber duck', feature: 'Round head, flat orange bill, and short raised tail', colors: ['#f6d05e', '#e4ae3e', '#e58b4b'], mount: 'Belly rests flat on top, with bill left and tail right' },
  'paper-boat': { name: 'Paper boat', feature: 'Folded hull with a central triangular plane', colors: ['#d6e7eb', '#92b5c1', '#f1f7f5'], mount: 'Folded hull edge rests on top; main fold stays centered' },
  'traffic-cone': { name: 'Traffic cone', feature: 'Orange cone with a white band and flat base', colors: ['#ed985f', '#c96c43', '#fff0dd'], mount: 'Flat base rests on top; cone tapers upward' },
  sprout: { name: 'Sprout', feature: 'Short stem with two leaves pointing in different directions', colors: ['#89b875', '#537f59', '#b5d38f'], mount: 'Stem grows directly from the top edge without a pot or base' },
  donut: { name: 'Donut', feature: 'Clear center hole, half-ring of icing, and a few sprinkles', colors: ['#dbab70', '#bf8456', '#e5a0b3'], mount: 'Lower curve touches the top; center remains an open cutout' },
  'ringed-planet': { name: 'Ringed planet', feature: 'Banded sand-colored planet inside a tilted pale ring with a soft glow', colors: ['#d9b07a', '#b8875a', '#f1e3c8'], mount: 'Ring underside touches the top edge; the planet floats above the center' },
  'cat-ears': { name: 'Cat ears', feature: 'Two tan triangular ears with pink inner planes', colors: ['#c9a27e', '#f2a0b4'], mount: 'Ears rise from the top corners; nothing sits between them' },
  'cowboy-hat': { name: 'Cowboy hat', feature: 'Tall creased crown over a wide curved brim with a dark band', colors: ['#b98352', '#a8734a', '#6e4a2e'], mount: 'Brim rests across the top edge and stays clear of the arms' },
  mushroom: { name: 'Mushroom', feature: 'Red cap with cream spots on a short cream stem', colors: ['#e8654f', '#fff4ef', '#fff1dc'], mount: 'Stem stands on the top edge; the cap overhangs both sides' },
  'shark-fin': { name: 'Shark fin', feature: 'Gray dorsal fin with a lighter leading plane', colors: ['#8b95a5', '#a7b1c0'], mount: 'Fin base spans the top edge center' },
  'propeller-cap': { name: 'Propeller cap', feature: 'Yellow and red cap dome with a spinning propeller on a button', colors: ['#f6b500', '#e8654f', '#c9d2ff'], mount: 'Dome overlaps the top edge; the propeller spins flat at full size' },
  'graduation-cap': { name: 'Graduation cap', feature: 'Black mortarboard with a gold button and a swinging tassel', colors: ['#2b2f3a', '#3c4658', '#f6b500'], mount: 'Skull cap rests on the top edge; the board floats above it' },
  'ice-cream': { name: 'Ice cream', feature: 'Strawberry scoop with an upright cone and a cherry', colors: ['#f2a0b4', '#e0ad84', '#e8654f'], mount: 'Scoop rests on the top edge; the cone points up' },
  'flower-crown': { name: 'Flower crown', feature: 'Green wreath with alternating pink and yellow blossoms', colors: ['#89b875', '#f2a0b4', '#f6b500'], mount: 'Wreath follows the top edge without a brim' },
  'pirate-hat': { name: 'Pirate hat', feature: 'Black tricorn with an upturned brim and a white skull mark', colors: ['#2b2f3a', '#3c4658', '#ffffff'], mount: 'Brim rests on the top edge; points stay clear of the arms' },
  lightning: { name: 'Lightning', feature: 'Yellow bolt standing upright with a breathing glow', colors: ['#ffcf11', '#fff3c4'], mount: 'Lower tip touches the top edge center' },
  'crystal-cluster': { name: 'Crystal cluster', feature: 'Three violet crystals with a lighter facet and a breathing glow', colors: ['#8b5cf6', '#a78bfa', '#c4b5fd'], mount: 'Crystal bases sit on the top edge' },
  ufo: { name: 'UFO', feature: 'Hovering saucer with a glass dome, blinking lights and a tractor beam', colors: ['#8b95a5', '#a2c5d1', '#c7f6ff'], mount: 'Saucer floats above the head; the beam lands on the top edge' },

  shades: { name: 'Shades', feature: 'Two dark lenses with a short bridge pushed up on the brow', colors: ['#2b2f3a', '#6b7280'], mount: 'Fits within the forehead band above the screen' },
  'forehead-goggles': { name: 'Forehead goggles', feature: 'Two blue-gray lenses with a center bridge', colors: ['#8b7568', '#a2c5d1', '#dec8ae'], mount: 'Fits within the forehead band; bridge anchors above the screen' },
  'mecha-faceplate': { name: 'Mecha faceplate', feature: 'White face armor with a dark visor band, angular yellow-green eyes, a red chin plate and two vents', colors: ['#f4f6fb', '#1d2433', '#d94a3a'], mount: 'Replaces the face inside the white screen frame; the eyes change light, not shape' },
  'pixel-screen': { name: 'Pixel screen', feature: 'Dark monitor with phosphor-green pixel eyes: blocks, a blocky smile, a loading bar and dim dashes', colors: ['#1f2a3a', '#7dff8a'], mount: 'Replaces the face inside the white screen frame; pixel glyphs carry all four expressions' },
  'gold-faceplate': { name: 'Gold faceplate', feature: 'Armored gold plate whose fixed eye slits change light, not shape: cyan idle, warm flare, amber scan, dim ember', colors: ['#efc04a', '#bf8a2a', '#6fdcff'], mount: 'Replaces the face inside the white screen frame; light carries all four expressions' },
  'neon-visor': { name: 'Neon visor', feature: 'Full smoked visor with a pair of LED eyes that lid, squint and rest, plus a passing scan line', colors: ['#1d2433', '#4de3ff', '#c7f6ff'], mount: 'Replaces the face inside the white screen frame; the LED pair carries all four expressions' },

  'task-board': { name: 'Task board', feature: 'White planning board with connected role-color markers and a yellow accent', colors: ['#ffffff', '#3161f7', '#f6b500'], mount: held },
  wrench: { name: 'Wrench', feature: 'White open-ended wrench with role-color outlines and a yellow handle inset', colors: ['#ffffff', '#3161f7', '#f6b500'], mount: held },
  'coffee-mug': { name: 'Coffee mug', feature: 'White mug with a role-color band and rising steam', colors: ['#ffffff', '#3161f7', '#5a3c2a'], mount: held },
  shield: { name: 'Shield', feature: 'White shield with a role-color outline and upright check', colors: ['#ffffff', '#3161f7', '#f6b500'], mount: held },
  magnifier: { name: 'Magnifier', feature: 'Upright white lens frame with a role-color outline and a lower grip', colors: ['#ffffff', '#3161f7', '#f6b500'], mount: held },
  'control-panel': { name: 'Control panel', feature: 'White control panel with dark rails and role-color and yellow knobs', colors: ['#ffffff', '#3161f7', '#f6b500'], mount: held },
  'magic-wand': { name: 'Magic wand', feature: 'Dark wand tipped with a gold star and drifting sparkles', colors: ['#3b2f5c', '#f6b500', '#fff3c4'], mount: held },
  'energy-blade': { name: 'Energy blade', feature: 'Compact hilt with a blade that cycles cyan, magenta and green while it extends and retracts', colors: ['#2a3140', '#4de3ff', '#e6fbff'], mount: `${held}; the blade rises beside the body, never across the screen` },
  paintbrush: { name: 'Paintbrush', feature: 'Wooden brush with a steel ferrule and a role-color paint tip', colors: ['#e0ad84', '#8b95a5', '#3161f7'], mount: held },
  'boba-tea': { name: 'Boba tea', feature: 'White cup of milk tea with dark pearls and a yellow straw', colors: ['#ffffff', '#e0ad84', '#3c4658'], mount: held },
  gamepad: { name: 'Gamepad', feature: 'White controller with a role-color d-pad and two buttons', colors: ['#ffffff', '#3161f7', '#f6b500'], mount: held },
  flag: { name: 'Flag', feature: 'Steel pole with a waving role-color pennant', colors: ['#8b95a5', '#3161f7'], mount: `${held}; the pennant rises beside the body` },
  lantern: { name: 'Lantern', feature: 'Red paper lantern with dark caps and a warm breathing glow', colors: ['#e8654f', '#3c4658', '#ffd970'], mount: held },
  staff: { name: 'Staff', feature: 'Wooden staff topped with a glowing orb that shifts violet, cyan and pink', colors: ['#8a6520', '#8b5cf6', '#c4b5fd'], mount: `${held}; the orb rises beside the body` },

  cape: { name: 'Cape', feature: 'Crimson cape that flares wide below the body with a lighter lining', colors: ['#b23a48', '#d65a68'], mount: 'Collar sits at the shoulders behind the body; the flares show beside and below the base' },
  'crescent-moon': { name: 'Crescent moon', feature: 'Yellow crescent rising behind the head with a small star', colors: ['#f6d365', '#fff3c4'], mount: 'Moon shows behind the upper-left of the head' },
  jetpack: { name: 'Jetpack', feature: 'Two steel tanks with red caps and flickering flames', colors: ['#8b95a5', '#e8654f', '#ffb347'], mount: 'Tanks show beside both arms behind the body; flames flicker below' },
  'star-trail': { name: 'Star trail', feature: 'Three gold four-point stars twinkling beside the body', colors: ['#f6b500', '#fff3c4'], mount: 'Stars float beside the body and twinkle at full size' },
  balloons: { name: 'Balloons', feature: 'Three balloons on strings rising beside the head', colors: ['#e8654f', '#f6b500', '#4f7cf6'], mount: 'Strings meet behind the left shoulder; the bunch bobs at full size' },
  'bat-wings': { name: 'Bat wings', feature: 'Two scalloped violet wings with darker membranes that flap', colors: ['#5b3a8c', '#3d2563'], mount: 'Wings spread from behind the shoulders and stay clear of the head' },
  'butterfly-wings': { name: 'Butterfly wings', feature: 'Two pink two-lobed wings with cream inner planes that flap', colors: ['#f2a0b4', '#fff3c4'], mount: 'Wings spread from behind the shoulders' },
  halo: { name: 'Halo', feature: 'Flat golden aureole centered on the antenna light with a breathing glow', colors: ['#f6b500', '#ffe08a', '#fff7dc'], mount: 'Ring sits behind the light and hats; the light disc covers its center' },
  'angel-wings': { name: 'Angel wings', feature: 'Two white feathered wings with a soft glow and a slow flap', colors: ['#ffffff', '#eef1ff', '#c9d2ff'], mount: 'Wings spread from behind the shoulders' },
  'sun-rays': { name: 'Sun rays', feature: 'Twelve alternating gold rays turning slowly behind the body', colors: ['#f6b500', '#ffcf11'], mount: 'Rays radiate from the body center' },
  'orbit-ring': { name: 'Orbit ring', feature: 'Tilted orbit belt with three bodies that circle in front of and behind the robot', colors: ['#b9c4ff', '#f6b500', '#ff9ad9'], mount: 'Belt crosses low around the body; bodies pass in front below the screen' },
  'dragon-wings': { name: 'Dragon wings', feature: 'Two large crimson wings with dark membranes and ember tips that flap', colors: ['#b23a48', '#7f2634', '#ffb347'], mount: 'Wings spread wide from behind the shoulders' },

  stripes: { name: 'Stripes', feature: 'Diagonal light stripes across the body', colors: ['#ffffff'], mount: 'Overlay clipped to the body; reads through every palette' },
  chrome: { name: 'Chrome', feature: 'Mirror steel with a horizon line and a sweeping sheen', colors: ['#f4f7fb', '#9aa6b8', '#3c4658'], mount: 'Replaces the body and arm paint; face marks keep the palette' },
  gold: { name: 'Gold', feature: 'Polished gold with a sweeping sheen', colors: ['#f6d365', '#b8862c', '#fff3c4'], mount: 'Replaces the body and arm paint; face marks keep the palette' },
  lava: { name: 'Lava', feature: 'Molten rock whose glow flows between ember red and bright orange', colors: ['#e8451f', '#ffb347', '#4a1208'], mount: 'Replaces the body and arm paint; face marks keep the palette' },
  holographic: { name: 'Holographic', feature: 'Iridescent foil whose colors flow across the body', colors: ['#ff8ad4', '#8ad8ff', '#c9ff8a'], mount: 'Replaces the body and arm paint; face marks keep the palette' },
  galaxy: { name: 'Galaxy', feature: 'Deep space nebula that drifts between violet and blue with twinkling stars', colors: ['#4c1d95', '#2563eb', '#ffffff'], mount: 'Replaces the body and arm paint; stars twinkle on the surface' },
}

export const decorations: Decoration[] = items.map(item => ({ ...item, ...copy[item.id] }))
export const decorationOf = (id: ItemId): Decoration => ({ ...itemOf(id), ...copy[id] })
export const decorationName = (id: ItemId | 'none') => id === 'none' ? 'None' : copy[id].name
export function decorationsOf(slot: Slot): Decoration[] { return decorations.filter(item => item.slot === slot) }
export function decorationsByRarity(rarity: Rarity): Decoration[] { return decorations.filter(item => item.rarity === rarity) }
export function decorationsBySeries(series: Series): Decoration[] { return decorations.filter(item => item.series === series) }
export const rarityOrder = (rarity: Rarity) => rarities.indexOf(rarity)
export const slotOrder = (slot: Slot) => slots.indexOf(slot)
export const seriesOrder = (series: Series) => seriesIds.indexOf(series)
