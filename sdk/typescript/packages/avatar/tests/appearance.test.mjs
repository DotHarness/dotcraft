import assert from 'node:assert/strict'
import test from 'node:test'
import {
  deriveAppearance, originalAppearance, items, itemsOf, slots, rarities, rarityWeights, slotPresence,
  hasConflicts, conflicts, equip, canEquip, appearanceWall, createAvatarRegistry, applyOutfitPreset, normalizeAppearance, mascotPaletteOf,
} from '../dist/index.js'

test('name derivation normalizes whitespace and Unicode NFC while empty names return originalAppearance', () => {
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
    assert.ok(conflicts(hat.id, 'headlamp') && conflicts(hat.id, 'snorkel-mask'))
    assert.ok(!conflicts(hat.id, 'gold-faceplate') && !conflicts(hat.id, 'neon-visor'))
    assert.ok(!conflicts(hat.id, 'bow-tie') && !conflicts(hat.id, 'ear-pencil'))
  }
  assert.ok(itemsOf('head').every(item => !item.zones.includes('rim')))
  assert.ok(!conflicts('rubber-duck', 'headlamp'))
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
  assert.ok(!canEquip(base, 'face', 'headlamp'))
  const lamp = equip({ ...base, head: 'rubber-duck' }, 'face', 'headlamp')
  assert.deepEqual(lamp.cleared, [])
  const hat = equip(lamp.appearance, 'head', 'beanie')
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

test('extension packs validate namespaces, preserve non-derivable hashes, and resolve presets and palettes', () => {
  assert.throws(() => createAvatarRegistry({
    packs: [{ namespace: 'studio', label: 'Studio', items: [{ id: 'unprefixed', slot: 'head', rarity: 'common', series: 'everyday', zones: ['top'] }] }],
  }), TypeError)
  assert.throws(() => createAvatarRegistry({
    packs: [{ namespace: 'studio', label: 'Studio', items: [{ id: 'studio:bad-hat', slot: 'head', rarity: 'common', series: 'everyday', zones: ['rim'] }] }],
  }), TypeError)
  assert.throws(() => createAvatarRegistry({
    packs: [{ namespace: 'studio', label: 'Studio', palettes: { [-2]: { key: 'reserved', bodyD: '#000', bodyM: '#111', bodyL: '#222', markD: '#333', markL: '#444', shadow: '#000', accent: '#555' } }, items: [] }],
  }), TypeError)

  const customPalette = { key: 'berry', bodyD: '#d63f79', bodyM: '#f26a9c', bodyL: '#ffb3d0', markD: '#b8255f', markL: '#ff8fb8', shadow: '#5e1431', accent: '#f26a9c' }
  const pack = {
    namespace: 'idol',
    label: 'Idol Pack',
    palettes: { [-3]: customPalette },
    items: [
      { id: 'idol:hood', slot: 'head', rarity: 'rare', series: 'idol:stage', zones: ['top', 'brow'] },
      { id: 'idol:collar', slot: 'face', rarity: 'common', series: 'idol:stage', zones: ['rim'] },
      { id: 'idol:dress', slot: 'skin', rarity: 'epic', series: 'idol:stage', zones: ['body'], paintMaterial: { shadow: '#4a1028', accent: '#ff70a6' } },
    ],
    presets: [{ id: 'idol:full', name: 'Idol Full', palette: -3, head: 'idol:hood', face: 'idol:collar', skin: 'idol:dress' }],
  }
  const isolated = createAvatarRegistry({ packs: [pack] })
  for (let i = 0; i < 50; i++) {
    assert.deepEqual(deriveAppearance(`Sample ${i}`, isolated), deriveAppearance(`Sample ${i}`))
  }

  const presetLook = applyOutfitPreset(pack.presets[0], originalAppearance, isolated)
  assert.equal(presetLook.palette, -3)
  assert.equal(presetLook.head, 'idol:hood')
  assert.equal(mascotPaletteOf({ palette: -3, skin: 'none' }, isolated).bodyD, '#d63f79')
  assert.equal(mascotPaletteOf(presetLook, isolated).accent, '#ff70a6')

  const lampSwap = equip(presetLook, 'face', 'headlamp', isolated)
  assert.deepEqual(lampSwap.cleared, ['head'])
  assert.equal(lampSwap.appearance.head, 'none')
  assert.equal(normalizeAppearance(presetLook).head, 'none')

  const derivable = createAvatarRegistry({ packs: [{ ...pack, derivable: true }], includeCore: false })
  const drawn = Array.from({ length: 40 }, (_, i) => deriveAppearance(`Custom ${i}`, derivable))
  assert.ok(drawn.some(look => look.head === 'idol:hood'))
  assert.ok(drawn.every(look => !hasConflicts(look, derivable)))
})
