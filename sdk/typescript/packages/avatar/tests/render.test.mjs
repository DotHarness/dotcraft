import assert from 'node:assert/strict'
import test from 'node:test'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { AppearanceAvatar } from '../dist/react.js'
import { originalAppearance } from '../dist/index.js'

test('multiple avatars have disjoint paint, clip and filter IDs', () => {
  const html = renderToStaticMarkup(createElement('div', null, ...Array.from({ length: 6 }, (_, index) => createElement(AppearanceAvatar, { appearance: { ...originalAppearance, head: 'ringed-planet', face: 'neon-visor', back: 'halo', skin: 'chrome' }, size: 64, key: index }))))
  const ids = [...html.matchAll(/ id="([^"]+)"/g)].map(match => match[1])
  assert.equal(ids.length, new Set(ids).size)
})
