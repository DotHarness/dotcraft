import assert from 'node:assert/strict'
import test from 'node:test'
import { glowGeometry } from '../dist/index.js'

test('without an island the comets run from the top centre to meet at the bottom centre', () => {
  const glow = glowGeometry(1920, 1080, null, 0)
  assert.deepEqual(glow.edge, { x: 960, y: 0 })
  assert.equal(glow.meetAt, 0.5)
})

test('the comets meet straight across from an island off centre', () => {
  const glow = glowGeometry(1000, 500, { x: 800, y: 20 }, 0)
  assert.deepEqual(glow.edge, { x: 800, y: 0 })
  // Clockwise from the island: 200 to the corner, 500 down, 200 back to the point below it.
  assert.equal(glow.meetAt, 900 / 3000)
})

test('the path starts at the edge nearest the island and runs clockwise', () => {
  const glow = glowGeometry(1000, 500, { x: 990, y: 250 }, 0)
  assert.equal(glow.path, 'M1000 250L1000 500L0 500L0 0L1000 0Z')
  assert.equal(glow.meetAt, 0.5)
})
