import assert from 'node:assert/strict'
import test from 'node:test'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { Avatar, AppearanceAvatar, DecorationSwatch } from '../dist/react.js'
import { deriveAppearance, originalAppearance, items, itemsOf, decorations, equip, mascotPaletteOf, paintMaterials } from '../dist/index.js'
const render = props => renderToStaticMarkup(createElement(AppearanceAvatar, props))
const states = ['idle', 'thinking', 'working', 'waiting', 'blocked', 'done', 'greeting', 'sleep']
const arms = [['l-w', 165, 472, 136, 256, 58], ['r-w', 723, 472, 136, 256, 58], ['l-b', 188, 514, 90, 171, 19], ['r-b', 746, 514, 90, 171, 19]]
function assertArms(html) {
  for (const [name, x, y, w, h, rx] of arms) assert.ok(html.includes(`class="dca-part-arm-${name}" x="${x}" y="${y}" width="${w}" height="${h}" rx="${rx}"`))
}

test('every item mounts in every pose with the original paired arm geometry', () => {
  const base = deriveAppearance('Reviewer')
  for (const item of items) for (const state of states) {
    const appearance = equip(base, item.slot, item.id).appearance
    const html = render({ appearance, state, label: 'Agent' })
    assertArms(html)
    assert.ok(html.includes(`data-${item.slot}="${item.id}"`))
    assert.equal(html.includes('class="dca-part-light"'), appearance.head === 'none')
    if (item.slot === 'head') assert.ok(html.includes(`data-decoration="${item.id}"`))
    if (item.slot === 'back') assert.ok(html.includes(`data-back="${item.id}"`))
    if (item.slot === 'skin') assert.ok(html.includes(`data-skin-overlay="${item.id}"`))
  }
})

const paintSkins = Object.keys(paintMaterials)
const overlaySkins = itemsOf('skin').map(item => item.id).filter(id => !paintSkins.includes(id))

test('paint skins replace the body paint while face marks keep the palette', () => {
  for (const skin of paintSkins) {
    const html = render({ appearance: { ...deriveAppearance('Reviewer'), skin } })
    assert.ok(html.includes('dca-skin-paint'))
    assert.ok(!html.includes('class="dca-paint-body"'))
    assert.ok(html.includes('class="dca-paint-mark"'))
  }
  for (const skin of overlaySkins) {
    const html = render({ appearance: { ...deriveAppearance('Reviewer'), skin } })
    assert.ok(html.includes('class="dca-paint-body"'))
    assert.ok(html.includes('class="dca-part-surface"'))
  }
})

test('paint skins own the mascot energy accent and carry an energy wash', () => {
  const base = deriveAppearance('Reviewer')
  for (const skin of paintSkins) {
    assert.equal(mascotPaletteOf({ ...base, skin }).accent, paintMaterials[skin].accent)
    assert.equal(mascotPaletteOf({ ...base, skin }).markM, mascotPaletteOf(base).markM)
    assert.ok(render({ appearance: { ...base, skin }, size: 64 }).includes('dca-skin-energy'))
  }
  for (const skin of overlaySkins) {
    assert.equal(mascotPaletteOf({ ...base, skin }).accent, mascotPaletteOf(base).accent)
    assert.ok(!render({ appearance: { ...base, skin }, size: 64 }).includes('dca-skin-energy'))
  }
})

test('faceplates replace the native face, keep four expression layers and survive compact size', () => {
  for (const { id: face } of itemsOf('face').filter(item => item.zones.includes('screen'))) {
    const html = render({ appearance: { ...originalAppearance, face }, size: 64 })
    assert.ok(html.includes(`data-faceplate="${face}"`))
    assert.ok(!html.includes('data-profile-face='))
    for (const name of ['neutral', 'happy', 'operator', 'sleep']) assert.ok(html.includes(`data-mask-face="${name}"`))
    assert.ok(html.includes('class="dca-part-eyes"'))
    const compact = render({ appearance: { ...originalAppearance, face }, size: 16 })
    assert.ok(compact.includes(`data-faceplate="${face}"`) && !compact.includes('data-accessory='))
  }
  const brow = render({ appearance: { ...originalAppearance, face: 'forehead-goggles' }, size: 64 })
  assert.ok(brow.includes('data-profile-face=') && !brow.includes('data-faceplate='))
})

test('rim items keep the native face, hide at compact size and combine with brimmed hats', () => {
  const rim = { ...originalAppearance, face: 'bow-tie' }
  const full = render({ appearance: rim, size: 64 })
  assert.ok(full.includes('data-accessory="bow-tie"') && full.includes('data-profile-face=') && !full.includes('data-faceplate='))
  assert.ok(!render({ appearance: rim, size: 16 }).includes('data-accessory='))
  const hatted = equip(rim, 'head', 'cowboy-hat')
  assert.deepEqual(hatted.cleared, [])
  const html = render({ appearance: hatted.appearance, size: 64 })
  assert.ok(html.includes('data-decoration="cowboy-hat"') && html.includes('data-accessory="bow-tie"'))
})

test('orbiting items split into a behind-body half and an in-front half; flat items stay behind', () => {
  const split = back => {
    const html = render({ appearance: { ...originalAppearance, back }, size: 64 })
    const backIndex = html.indexOf(`data-back="${back}"`), frontIndex = html.indexOf(`data-back-front="${back}"`), faceIndex = html.indexOf('data-profile-face=')
    assert.ok(backIndex > 0 && frontIndex > 0 && backIndex < faceIndex && faceIndex < frontIndex)
    return html
  }
  split('donut-floatie')
  for (const back of ['orbit-ring', 'koi-orbit']) {
    const orbit = split(back)
    const bodies = (orbit.match(/dca-fx-orbit-front/g) ?? []).length
    assert.ok(bodies > 0 && bodies === (orbit.match(/dca-fx-orbit-back/g) ?? []).length)
  }
  for (const back of ['halo', 'cape']) assert.ok(!render({ appearance: { ...originalAppearance, back }, size: 64 }).includes('data-back-front='))
})

test('unnamed avatar uses original paint; every specimen renders', () => {
  const plain = renderToStaticMarkup(createElement(Avatar, { name: '', label: 'DotCraft' }))
  assert.ok(plain.includes('#2458f7')); assert.ok(plain.includes('#5f82f7')); assert.ok(plain.includes('#8fa5ff'))
  assert.ok(plain.includes('aria-label="DotCraft"'))
  for (const { id } of decorations) assert.match(renderToStaticMarkup(createElement(DecorationSwatch, { id })), /<svg[^>]*><(g|path|circle|rect|defs)\b/)
})

test('size tiers hide secondary layers at compact size and gate effects', () => {
  const loaded = { ...originalAppearance, head: 'rubber-duck', face: 'forehead-goggles', hand: 'shield', back: 'halo', skin: 'stripes' }
  for (const size of [16, 20]) {
    const html = render({ appearance: loaded, size, motion: 'on' })
    assert.ok(html.includes('data-size-tier="compact"') && html.includes('data-effects="off"') && html.includes('data-motion="off"'))
    assert.ok(!html.includes('data-accessory=') && !html.includes('data-held-decoration=') && !html.includes('data-skin-overlay='))
    assert.ok(html.includes('data-back="halo"') && html.includes('data-decoration="rubber-duck"'))
  }
  const standard = render({ appearance: loaded, size: 36, motion: 'on' })
  assert.ok(standard.includes('data-size-tier="standard"') && standard.includes('data-effects="static"'))
  assert.ok(standard.includes('data-accessory="forehead-goggles"') && standard.includes('data-skin-overlay="stripes"'))
  const full = render({ appearance: loaded, size: 64, motion: 'off' })
  assert.ok(full.includes('data-size-tier="full"') && full.includes('data-effects="static"'))
  assert.ok(itemsOf('hand').every(item => render({ appearance: { ...originalAppearance, hand: item.id }, state: 'working', size: 44 }).includes('data-held-state="stowed"')))
})

test('multiple avatars have disjoint paint, clip and filter IDs', () => {
  const html = renderToStaticMarkup(createElement('div', null, ...Array.from({ length: 6 }, (_, index) => createElement(AppearanceAvatar, { appearance: { ...originalAppearance, head: 'ringed-planet', face: 'neon-visor', back: 'halo', skin: 'chrome' }, size: 64, key: index }))))
  const ids = [...html.matchAll(/ id="([^"]+)"/g)].map(match => match[1])
  assert.equal(ids.length, new Set(ids).size)
})
