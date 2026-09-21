import assert from 'node:assert/strict'
import test from 'node:test'
import { deriveAppearance, originalAppearance, items, itemsOf, slots, rarities, rarityWeights, slotPresence, hasConflicts, conflicts, equip, canEquip, appearanceWall } from '../dist/index.js'

test('name identities are deterministic, normalized and cover every slot', () => {
  assert.deepEqual(deriveAppearance('Reviewer'), { version: 1, palette: 10, baseFace: 4, head: 'wizard-hat', face: 'none', hand: 'task-board', back: 'none', skin: 'stripes' })
  assert.deepEqual(deriveAppearance('Explorer'), { version: 1, palette: 10, baseFace: 1, head: 'cowboy-hat', face: 'none', hand: 'lantern', back: 'crescent-moon', skin: 'none' })
  assert.deepEqual(deriveAppearance('Fixture 29'), { version: 1, palette: 9, baseFace: 1, head: 'traffic-cone', face: 'forehead-goggles', hand: 'flag', back: 'crescent-moon', skin: 'stripes' })
  assert.deepEqual(deriveAppearance('Fixture 55'), { version: 1, palette: 1, baseFace: 0, head: 'cat-ears', face: 'none', hand: 'shield', back: 'cape', skin: 'lava' })
  assert.deepEqual(deriveAppearance('  Reviewer \n'), deriveAppearance('Reviewer'))
  assert.deepEqual(deriveAppearance('cafe\u0301'), deriveAppearance('café'))
  assert.notDeepEqual(deriveAppearance('reviewer'), deriveAppearance('Reviewer'))
  assert.notDeepEqual(deriveAppearance('Agent One'), deriveAppearance('Agent  One'))
  assert.deepEqual(deriveAppearance(' \n'), originalAppearance)
})

test('the registry spans five slots, every rarity and unique ids', () => {
  assert.equal(new Set(items.map(item => item.id)).size, items.length)
  for (const slot of slots) assert.ok(itemsOf(slot).some(item => item.rarity === 'legendary'), `${slot} has no legendary item`)
  for (const rarity of rarities) assert.ok(items.some(item => item.rarity === rarity), rarity)
  for (const hat of itemsOf('head').filter(item => item.zones.includes('brow'))) {
    assert.ok(conflicts(hat.id, 'forehead-goggles'))
    assert.ok(!conflicts(hat.id, 'gold-faceplate') && !conflicts(hat.id, 'neon-visor'))
  }
  assert.ok(!conflicts('rubber-duck', 'forehead-goggles'))
})

test('large samples follow slot presence and rarity weights, reach every item and never conflict', () => {
  const N = 40000
  const present = Object.fromEntries(slots.map(slot => [slot, 0]))
  const seen = Object.fromEntries(items.map(item => [item.id, 0]))
  const byRarity = Object.fromEntries(rarities.map(rarity => [rarity, 0]))
  for (let index = 0; index < N; index++) {
    const appearance = deriveAppearance(`Agent ${index}`)
    assert.ok(appearance.palette >= 0 && appearance.palette < 12)
    assert.ok(appearance.baseFace >= 0 && appearance.baseFace < 5)
    assert.ok(!hasConflicts(appearance))
    for (const slot of slots) if (appearance[slot] !== 'none') { present[slot]++; seen[appearance[slot]]++ }
  }
  for (const item of items) { assert.ok(seen[item.id] > 0, `${item.id} unreachable`); byRarity[item.rarity] += seen[item.id] }
  for (const slot of ['head', 'hand', 'back', 'skin']) assert.ok(Math.abs(present[slot] / N - slotPresence[slot]) < .02, slot)
  const headTiers = rarities.map(rarity => itemsOf('head').filter(item => item.rarity === rarity).reduce((sum, item) => sum + seen[item.id], 0))
  const total = headTiers.reduce((a, b) => a + b, 0)
  const weight = rarities.reduce((sum, rarity) => sum + rarityWeights[rarity], 0)
  rarities.forEach((rarity, index) => assert.ok(Math.abs(headTiers[index] / total - rarityWeights[rarity] / weight) < .015, rarity))
  assert.ok(byRarity.legendary < byRarity.epic && byRarity.epic < byRarity.rare && byRarity.rare < byRarity.uncommon && byRarity.uncommon < byRarity.common)
})

test('equip clears conflicting slots and reports them', () => {
  const base = { ...deriveAppearance('Reviewer'), face: 'neon-visor' }
  assert.ok(canEquip(base, 'face', 'neon-visor'))
  assert.ok(!canEquip(base, 'face', 'forehead-goggles'))
  const goggles = equip({ ...base, head: 'rubber-duck' }, 'face', 'forehead-goggles')
  assert.deepEqual(goggles.cleared, [])
  const hat = equip(goggles.appearance, 'head', 'beanie')
  assert.equal(hat.appearance.face, 'none')
  assert.deepEqual(hat.cleared, ['face'])
  assert.equal(equip(hat.appearance, 'back', 'halo').cleared.length, 0)
  assert.equal(equip(hat.appearance, 'head', 'none').appearance.head, 'none')
  assert.ok(!canEquip(base, 'head', 'shield'))
  assert.throws(() => equip(base, 'head', 'shield'), TypeError)
})

test('walls reproduce from their seed and cells replay individually', () => {
  const first = appearanceWall('review / seed')
  assert.equal(first.length, 100)
  assert.equal(new Set(first.map(item => item.id)).size, 100)
  assert.deepEqual(appearanceWall('review / seed'), first)
  for (const item of first) assert.deepEqual(deriveAppearance(item.id), item.appearance)
  assert.notDeepEqual(appearanceWall('review / seed', 1), first)
})
