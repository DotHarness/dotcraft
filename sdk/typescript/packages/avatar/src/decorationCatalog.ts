import { compatibility, primaryIds, type DecorationId, type PrimaryId, type SecondaryId } from './appearanceModel.js'

export interface Decoration {
  id: DecorationId
  name: string
  category: 'Hats' | 'Novelty' | 'Accessories'
  feature: string
  colors: readonly string[]
  mount: string
}
export const decorations: Decoration[] = [
  { id: 'baseball-cap', name: 'Baseball cap', category: 'Hats', feature: 'Rounded crown with a short right-facing brim', colors: ['#e87967', '#b94f50', '#f5a18c'], mount: 'Crown overlaps the top edge; the brim stays inside the body' },
  { id: 'bucket-hat', name: 'Bucket hat', category: 'Hats', feature: 'Flat top with a continuous downward brim', colors: ['#94bda6', '#5b887a', '#bed8c1'], mount: 'Brim follows the top edge with a slight center overlap' },
  { id: 'beret', name: 'Beret', category: 'Hats', feature: 'Soft left-leaning crown with a short stem', colors: ['#b17498', '#784c71', '#cf9db5'], mount: 'Narrow band rests on top; the crown leans left' },
  { id: 'beanie', name: 'Beanie', category: 'Hats', feature: 'Rounded crown and wide folded cuff with minimal texture', colors: ['#e9a653', '#bc753d', '#f7c676'], mount: 'Folded cuff sits on the top edge; crown stays centered' },
  { id: 'top-hat', name: 'Top hat', category: 'Hats', feature: 'Tall crown, narrow brim, and burgundy band', colors: ['#465069', '#293247', '#c96e85'], mount: 'Narrow brim rests flat on top; crown tapers at its base' },
  { id: 'wizard-hat', name: 'Wizard hat', category: 'Hats', feature: 'Curved point, wide brim, and gold star', colors: ['#8773c1', '#594b93', '#f4cc75'], mount: 'Wide brim rests on top; point curves into the center safe area' },
  { id: 'chef-hat', name: 'Chef hat', category: 'Hats', feature: 'Three-lobed puff with an upright band', colors: ['#f5eee3', '#d7caba', '#fffaf1'], mount: 'Band rests at the center of the top edge' },
  { id: 'party-hat', name: 'Party hat', category: 'Hats', feature: 'Cone with two colored bands and a pom-pom', colors: ['#ea9baf', '#8075b7', '#f5d07b'], mount: 'Shallow curved base follows the top of the body' },
  { id: 'crown', name: 'Crown', category: 'Hats', feature: 'Three rounded points with a central sapphire', colors: ['#efc65c', '#c99139', '#73b8cc'], mount: 'Curved gold band wraps the top edge; all points face up' },
  { id: 'hard-hat', name: 'Hard hat', category: 'Hats', feature: 'Rounded shell, center ridge, and short front brim', colors: ['#f5bd48', '#d68d32', '#ffdc79'], mount: 'Short brim overlaps the top edge; ridge stays centered' },
  { id: 'nightcap', name: 'Nightcap', category: 'Hats', feature: 'Soft drooping point with a pom-pom', colors: ['#789cc9', '#4c709f', '#dce8f4'], mount: 'Soft band follows the top edge; pom-pom stays upper right' },
  { id: 'straw-hat', name: 'Straw hat', category: 'Hats', feature: 'Low round crown, wide oval brim, and red band', colors: ['#e8c78a', '#c49b5e', '#c57568'], mount: 'Wide brim rests flat without entering the arm areas' },
  { id: 'poop', name: 'Poop', category: 'Novelty', feature: 'Three rounded layers with a playful curled tip', colors: ['#a97b5f', '#805840', '#c49976'], mount: 'Wide bottom layer rests directly on top; color separates each layer' },
  { id: 'banana', name: 'Banana', category: 'Novelty', feature: 'Crescent fruit with stems and an inner color plane', colors: ['#f3cf62', '#dbaa40', '#89705a'], mount: 'Outer curve touches the top; both stems point up' },
  { id: 'fried-egg', name: 'Fried egg', category: 'Novelty', feature: 'Irregular rounded white with a raised golden yolk', colors: ['#fff9e8', '#e6d9bb', '#f3be43'], mount: 'White spreads across the top; raised yolk defines the silhouette' },
  { id: 'rubber-duck', name: 'Rubber duck', category: 'Novelty', feature: 'Round head, flat orange bill, and short raised tail', colors: ['#f6d05e', '#e4ae3e', '#e58b4b'], mount: 'Belly rests flat on top, with bill left and tail right' },
  { id: 'paper-boat', name: 'Paper boat', category: 'Novelty', feature: 'Folded hull with a central triangular plane', colors: ['#d6e7eb', '#92b5c1', '#f1f7f5'], mount: 'Folded hull edge rests on top; main fold stays centered' },
  { id: 'traffic-cone', name: 'Traffic cone', category: 'Novelty', feature: 'Orange cone with a white band and flat base', colors: ['#ed985f', '#c96c43', '#fff0dd'], mount: 'Flat base rests on top; cone tapers upward' },
  { id: 'sprout', name: 'Sprout', category: 'Novelty', feature: 'Short stem with two leaves pointing in different directions', colors: ['#89b875', '#537f59', '#b5d38f'], mount: 'Stem grows directly from the top edge without a pot or base' },
  { id: 'donut', name: 'Donut', category: 'Novelty', feature: 'Clear center hole, half-ring of icing, and a few sprinkles', colors: ['#dbab70', '#bf8456', '#e5a0b3'], mount: 'Lower curve touches the top; center remains an open cutout' },
  { id: 'forehead-goggles', name: 'Forehead goggles', category: 'Accessories', feature: 'Two blue-gray lenses with a center bridge', colors: ['#8b7568', '#a2c5d1', '#dec8ae'], mount: 'Fits within the forehead band; bridge anchors above the screen' },
  {"id":"task-board","name":"Task board","category":"Accessories","feature":"White planning board with connected role-color markers and a yellow accent","colors":["#ffffff","#3161f7","#f6b500"],"mount":"Screen-left hand; shares the arm pivot, stows for laptop and question sign, then restores"},
  {"id":"wrench","name":"Wrench","category":"Accessories","feature":"White open-ended wrench with role-color outlines and a yellow handle inset","colors":["#ffffff","#3161f7","#f6b500"],"mount":"Screen-left hand; shares the arm pivot, stows for laptop and question sign, then restores"},
  {"id":"shield","name":"Shield","category":"Accessories","feature":"White shield with a role-color outline and upright check","colors":["#ffffff","#3161f7","#f6b500"],"mount":"Screen-left hand; shares the arm pivot, stows for laptop and question sign, then restores"},
  {"id":"magnifier","name":"Magnifier","category":"Accessories","feature":"Upright white lens frame with a role-color outline and a lower grip","colors":["#ffffff","#3161f7","#f6b500"],"mount":"Screen-left hand; shares the arm pivot, stows for laptop and question sign, then restores"},
  {"id":"control-panel","name":"Control panel","category":"Accessories","feature":"White control panel with dark rails and role-color and yellow knobs","colors":["#ffffff","#3161f7","#f6b500"],"mount":"Screen-left hand; shares the arm pivot, stows for laptop and question sign, then restores"},
]
export const decorationOf = (id: DecorationId) => decorations.find(item => item.id === id)!
export const decorationName = (id: PrimaryId | SecondaryId) => id === 'none' ? 'None' : decorationOf(id).name
export function compatibleNames(item: Decoration) {
  return item.category === 'Accessories'
    ? primaryIds.filter(id => compatibility[id].includes(item.id as SecondaryId)).map(decorationName).join(', ')
    : compatibility[item.id as PrimaryId].map(decorationName).join(', ')
}
