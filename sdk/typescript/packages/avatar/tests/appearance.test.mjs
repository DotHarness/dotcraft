import assert from 'node:assert/strict'
import test from 'node:test'
import { deriveAppearance, originalAppearance, isCompatible, primaryIds, secondaryIds, compatibility } from '../dist/index.js'

test('frozen name identities and normalization', () => {
  assert.deepEqual(deriveAppearance('agent'), {version:2,palette:1,baseFace:2,primary:'traffic-cone',secondary:'shield'})
  assert.deepEqual(deriveAppearance('leader'), {version:2,palette:6,baseFace:1,primary:'wizard-hat',secondary:'none'})
  assert.deepEqual(deriveAppearance('猫猫🐱'), {version:2,palette:10,baseFace:4,primary:'paper-boat',secondary:'none'})
  // V2 deliberately redraws only the secondary pool; these frozen V1 dimensions must not move.
  assert.deepEqual(
    ['agent', 'leader', '猫猫🐱'].map(name => {
      const { palette, baseFace, primary } = deriveAppearance(name)
      return { palette, baseFace, primary }
    }),
    [
      {palette:1,baseFace:2,primary:'traffic-cone'},
      {palette:6,baseFace:1,primary:'wizard-hat'},
      {palette:10,baseFace:4,primary:'paper-boat'},
    ],
  )
  assert.deepEqual(deriveAppearance('  agent \n'), deriveAppearance('agent'))
  assert.deepEqual(deriveAppearance('cafe\u0301'), deriveAppearance('café'))
  assert.notDeepEqual(deriveAppearance('Agent'), deriveAppearance('agent'))
  assert.notDeepEqual(deriveAppearance('Agent One'), deriveAppearance('Agent  One'))
  assert.deepEqual(deriveAppearance(' \n'), originalAppearance)
  assert.notDeepEqual(deriveAppearance('renamed'), deriveAppearance('agent'))
})
test('the frozen V2 pool has 26 objects, 135 allowed combinations and balanced draws', () => {
  assert.equal(primaryIds.length + secondaryIds.length - 2, 26)
  assert.equal(Object.values(compatibility).reduce((n, ids) => n + ids.length, 0), 135)
  const count = Object.fromEntries(primaryIds.map(id=>[id,0])); let bare=0
  for(let i=0;i<21000;i++) {
    const a=deriveAppearance(`Agent ${i}`)
    assert.ok(isCompatible(a.primary,a.secondary)); count[a.primary]++
    if(a.secondary==='none') bare++
  }
  for(const n of Object.values(count)) assert.ok(n>800 && n<1200)
  assert.ok(bare>9900 && bare<11100)
  for(const hat of primaryIds.slice(1,13)) assert.equal(isCompatible(hat,'forehead-goggles'), false)
})
test('frozen V2 draws cover every held accessory with actual names', () => {
  const fixtures = {
    'Fixture 1': {version:2,palette:9,baseFace:0,primary:'hard-hat',secondary:'task-board'},
    'Fixture 2': {version:2,palette:1,baseFace:2,primary:'none',secondary:'control-panel'},
    'Fixture 5': {version:2,palette:9,baseFace:4,primary:'bucket-hat',secondary:'shield'},
    'Fixture 7': {version:2,palette:1,baseFace:3,primary:'banana',secondary:'wrench'},
    'Fixture 21': {version:2,palette:2,baseFace:3,primary:'poop',secondary:'magnifier'},
  }
  for (const [name, expected] of Object.entries(fixtures)) assert.deepEqual(deriveAppearance(name), expected)
})
